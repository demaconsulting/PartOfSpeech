using System.Diagnostics;
using System.Globalization;
using System.Text;

namespace DemaConsulting.PartOfSpeech.Trainer;

/// <summary>
///     Command-line entry point of the trainer.
/// </summary>
/// <remarks>
///     <para>
///         Commands: <c>train</c> (train, write the weights file, or with <c>--check</c> verify that the committed file is
///         current), <c>eval</c> (score a weights file on the development sets), <c>eval-shapes</c> (score a weights file on the
///         hard-shape development set), <c>eval-arm</c> (the dev-only scoring of a trained arm of the context-feature experiment),
///         <c>shapes-tokenize</c> (tokenize a text file for building that set), <c>calibrate</c> (choose the softmax
///         temperature on the open development split), <c>compare</c> (paired bootstrap of two weights files) and
///         <c>glossary-probe</c> (simulated glossaries on the open development split) and <c>glossary-eval</c> (the dev-only
///         evaluation of the <c>Glossary</c> API), <c>thresholds</c> (the development-only confidence curve and operating points)
///         and <c>evaluate</c> (the one held-out evaluation, behind a validated selection). The exit code is 0 on success, 1
///         when <c>train --check</c> finds the committed weights stale or <c>evaluate</c> reports a fired stop condition, and 2
///         for a usage or data error. Run without arguments for the usage text.
///     </para>
///     <para>
///         The program only parses arguments and does I/O and printing; the engine is in <c>Training/</c> and
///         <c>Evaluation/</c>, which the tests compile and exercise directly. Every command except <c>evaluate</c> reads
///         nothing but the two development files (see <see cref="DataGuard"/>), except <c>eval-shapes</c>, which reads the
///         files of the hard-shape development set (its tranches), <c>eval-arm</c>, which reads the same development files and
///         the mapped development table of the treebank cache (and no option names a data file), and <c>shapes-tokenize</c>, which reads and writes only the two files it is given
///         (both outside the data folder of the repository);
///         <c>evaluate</c> reads the held-out sets
///         only through the guarded readers and only with a selection artifact that is valid for the frozen model.
///     </para>
/// </remarks>
internal static class Program
{
    /// <summary>
    ///     The repository-relative location of the committed configuration.
    /// </summary>
    private const string DefaultConfig = "tools/DemaConsulting.PartOfSpeech.Trainer/training.json";

    /// <summary>
    ///     The repository-relative location of the committed weights.
    /// </summary>
    private const string DefaultWeights = "src/DemaConsulting.PartOfSpeech/Resources/weights.bin";

    /// <summary>
    ///     The salt that separates the seed of the simulated glossary hints from the seed of the shuffle.
    /// </summary>
    private const ulong GlossarySeedSalt = 25214903917UL;

    /// <summary>
    ///     The options that take a value.
    /// </summary>
    private static readonly string[] ValueOptions =
    {
        "--config", "--output", "--weights", "--label", "--log", "--epochs", "--hash-bits", "--min-count", "--prune", "--scale",
        "--seed", "--temperature", "--restrict", "--history", "--source", "--max-sentences", "--mask-disputed", "--silver-min",
        "--sample-permille", "--checkpoints", "--a", "--b", "--resamples", "--m0-output", "--m1-output", "--dump-dir", "--ud-cache",
        "--in", "--out", "--tranche", "--features", "--preview-folds", "--json",
    };

    /// <summary>
    ///     The options that are switches.
    /// </summary>
    private static readonly string[] FlagOptions = { "--check", "--update-config", "--eval", "--calibrate", "--preflight" };

    /// <summary>
    ///     The repository-relative folder of the evaluation record (the selection artifact and the held-out results).
    /// </summary>
    private const string EvaluationFolder = "docs/evaluation";

    /// <summary>
    ///     The file name, inside <see cref="EvaluationFolder"/>, of the disclosure paragraph that the generated Markdown reports carry
    ///     (a missing or blank file means no notice). The text describes one model and one evaluation and must be revised or removed
    ///     before a later evaluation of a different model is rendered.
    /// </summary>
    private const string DisclosureFile = "disclosure.md";
    /// <summary>
    ///     The harmful errors at 0.45 of the frozen model on the open development split (the freeze table of the run log;
    ///     for the model retrained after the corpus cleanup C the development figure of the "Corpus cleanup C" sections
    ///     of the run log replaces the first freeze figure of 94: 103 of 4,467 decisions for the first retrained model,
    ///     90 of 4,451 decisions for the model adopted after the pre-registered <c>-ing</c> follow-up, 100 of 4,451
    ///     decisions for the model with the clause-cue feature group that the context-feature experiment selected, and
    ///     91 of 4,451 decisions for the model that adds the imperative training source (the figures that <c>thresholds</c>
    ///     reported for these models before it wrote anything).
    /// </summary>
    private const int FrozenOpenHarmful = 91;

    /// <summary>
    ///     The harmful errors at 0.45 of the frozen model on the gold v2 development split (the freeze table of the run log;
    ///     for the model retrained after the corpus cleanup C the development figure of the "Corpus cleanup C" sections
    ///     of the run log replaces the first freeze figure of 9: 14 of 611 decisions for the first retrained model,
    ///     11 of 611 decisions for the model adopted after the pre-registered <c>-ing</c> follow-up, 16 of 611 decisions
    ///     for the model with the clause-cue feature group, and 14 of 611 decisions for the model that adds the imperative
    ///     training source, as reported by <c>thresholds</c>).
    /// </summary>
    private const int FrozenGoldHarmful = 14;

    /// <summary>
    ///     The usage text.
    /// </summary>
    private const string Usage = @"Usage: DemaConsulting.PartOfSpeech.Trainer <command> [options]

Commands:
  train [--config FILE] [--output FILE] [--check [--weights FILE]] [--eval] [--update-config]
        [--label TEXT] [--log FILE] [--checkpoints 4,6,8] [--calibrate]
        [--epochs N] [--hash-bits N] [--min-count N] [--prune N] [--scale S] [--seed N] [--temperature T]
        [--restrict none|history|full] [--history predicted|gold]
        [--features none|F1,F2,F3,F4|positionCues,clauseCues,rightPreview,inflection] [--preview-folds N]
        [--source NAME:WEIGHT]... [--max-sentences N] [--sample-permille N] [--mask-disputed true|false] [--silver-min N]
      Trains and writes the weights (default: the embedded resource). With --check it retrains with the
      committed configuration and compares the uncompressed image with the committed file (exit 1 if stale);
      --weights names the committed file when it is not the embedded resource. Overrides and --calibrate are for
      experiments: they require --output so that the shipped weights are never overwritten, and are not allowed with
      --check. --calibrate fits the temperature of the run on the open
      development split before the run is written and scored, so confidence-based metrics are comparable.
      --features selects the context feature groups (none, or any of positionCues=F1, clauseCues=F2, rightPreview=F3,
      inflection=F4; the groups are stored in the weights header); rightPreview trains on jackknife preview tags of
      --preview-folds folds (default 5) made by document hash. Both are experiment overrides.
  eval --weights FILE [--temperature T] [--label TEXT] [--log FILE]
      Scores a weights file on the development sets (open dev, gold v2 dev) through the real decoder.
  eval-shapes [--weights FILE] [--label TEXT] [--tranche standard|hard|both|compound-head|headings|named-state|new]
      Scores a weights file (default: the embedded model) on the hard-shape development set through the real decoder and
      prints Markdown: per shape the target-token accuracy, harmful errors and harmful errors per 1,000 noun-verb decisions,
      and the control accuracy. The standard tranche (dev.tsv) and the hard tranche (hard.tsv) each get a section, and a third
      section pools both (default --tranche both; a missing hard file is then noted, not scored; --tranche hard needs the file).
      The single-shape tranches compound-head (compound-head.tsv), headings (headings.tsv, scored by exact tag) and named-state
      (named-state.tsv) each get a section of their own with the target accuracy, harmful errors, harmful errors per 1,000
      decisions, the target items wrong at a confidence of at least 0.95 and the control accuracy; --tranche NAME scores one of
      them (the file is required), --tranche new scores all three (a missing file is noted, not scored).
      Reads the shapes development files only; they are development sets, never a held-out figure.
  shapes-tokenize --in FILE --out FILE
      Tokenizes a text file with the library tokenizer, one sentence per line: each output line is the sentence, the number
      of sentences the tokenizer found and the tokens separated by single spaces, tab separated. Reads and writes only the two
      files (used by tools/data/shapes_dev.py to build the shapes development set); no repository data is read.
  eval-arm --weights FILE --label TEXT --json FILE
      Dev-only scoring of a trained weights file for the context-feature experiment: the shapes development set (standard and
      hard tranche, per shape, with per-item outcomes of the target items), open dev, gold v2 dev, the mapped development part of
      the treebank (local cache, pinned SHA-256), the size of the file and the single-thread throughput of the default tagger
      (one warm-up pass and five timed passes over the open dev and treebank dev tokens, median). Writes the result as JSON to
      --json. It reads no held-out set and accepts no data path; --weights and --json must lie outside data/ and tools/data/.
  calibrate --weights FILE [--config FILE] [--update-config]
      Chooses the softmax temperature that minimizes the log-likelihood on the open development split.
  compare --a FILE --b FILE [--resamples N] [--seed N]
      Paired bootstrap by document of the difference between two weights files on the development sets.
  glossary-probe --weights FILE [--seed N]
      Simulated glossaries on the open development split: boost versus hard constraint.
  glossary-experiment [--config FILE] [--seed N] [--m0-output FILE] [--m1-output FILE]
      Dev-only glossary decision (E8): trains the configured model (M0) and a model with trained glossary features (M1),
      compares score boosts on M0 with the features of M1 on simulated glossaries and prints the pre-registered decision.
  glossary-eval [--seed N] [--resamples N]
      Dev-only evaluation of the Glossary API (E9): simulated glossaries (noun compounds, single words, inflection, acronym
      case, gold v2 head effect, history effect, non-head rule) through Tagger.Tag(tokens, glossary) with the embedded model;
      hard and soft modes against no glossary, 0/10/30% wrong entries, paired bootstrap by document. Reads the two
      development files only and takes no path.
  thresholds [--seed N] [--resamples N]
      Development-only confidence curve and operating points: scores the embedded model on the two development sets,
      applies the pre-registered rule (strict and balanced points) and writes docs/evaluation/operating-points.json and
      docs/evaluation/confidence-thresholds.md. Refuses to write when the 0.45 row does not reproduce the frozen figures.
      The report carries the paragraph of docs/evaluation/disclosure.md, if that file exists and is not blank.
  evaluate [--preflight] [--dump-dir DIR] [--ud-cache DIR] [--seed N] [--resamples N]
      The one held-out evaluation of the embedded model. Needs the committed operating-points.json for the frozen model
      (the held-out readers cannot open a file without it) and refuses to run when docs/evaluation/test-results.json
      or its lock file test-results.json.running exists; the lock is created exclusively before scoring and removed when
      the results are written (a stale lock of an aborted run is deleted by hand and recorded in RUN-LOG.md).
      test-results.md carries the paragraph of docs/evaluation/disclosure.md, if that file exists and is not blank.
      --preflight parses every held-out input and prints counts only. --dump-dir (outside the repository) receives
      the per-token records of the run. There is no weights, data or threshold option.

Exit codes: 0 success, 1 stale weights (train --check) or a stop condition fired (evaluate), 2 usage or data error.";

    /// <summary>
    ///     Runs the tool.
    /// </summary>
    /// <param name="args">The command-line arguments.</param>
    /// <returns>0 on success, 1 when the check fails, 2 for a usage or data error.</returns>
    internal static int Main(string[] args)
    {
        try
        {
            if (args.Length == 0 || args[0] == "--help" || args[0] == "-h")
            {
                Console.WriteLine(Usage);
                return args.Length == 0 ? 2 : 0;
            }

            var line = CommandLine.Parse(args);
            var root = FindRepositoryRoot();
            switch (line.Command)
            {
                case "train":
                    return Train(line, root);
                case "eval":
                    return Eval(line, root);
                case "eval-shapes":
                    return EvalShapes(line, root);
                case "eval-arm":
                    return EvalArm(line, root);
                case "shapes-tokenize":
                    return ShapesTokenize(line, root);
                case "calibrate":
                    return Calibrate(line, root);
                case "compare":
                    return Compare(line, root);
                case "glossary-probe":
                    return Probe(line, root);
                case "glossary-experiment":
                    return GlossaryDecision(line, root);
                case "glossary-eval":
                    return GlossaryEvaluate(line, root);
                case "thresholds":
                    return Thresholds(line, root);
                case "evaluate":
                    return Evaluate(line, root);
                default:
                    throw new ArgumentException($"Unknown command '{line.Command}'.");
            }
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException or InvalidOperationException or ArgumentException or UnauthorizedAccessException or FormatException)
        {
            Console.Error.WriteLine(ex.Message);
            if (ex is ArgumentException)
            {
                Console.Error.WriteLine(Usage);
            }

            return 2;
        }
    }

    /// <summary>
    ///     Runs the <c>train</c> command.
    /// </summary>
    /// <param name="line">The parsed command line.</param>
    /// <param name="root">The repository root.</param>
    /// <returns>The exit code.</returns>
    private static int Train(CommandLine line, string root)
    {
        // Load the committed configuration and apply the experiment overrides
        var configPath = line.Value("--config") ?? Path.Combine(root, DefaultConfig.Replace('/', Path.DirectorySeparatorChar));
        var committed = ConfigFile.Load(configPath);
        var config = committed.Clone();
        var overridden = ApplyOverrides(config, line);
        config.Validate();
        var check = line.Flag("--check");
        var calibrate = line.Flag("--calibrate");
        if (check && (overridden || calibrate || line.Value("--output") != null || line.Flag("--update-config")))
        {
            throw new ArgumentException("--check verifies the committed configuration and cannot be combined with overrides, --calibrate, --output or --update-config.");
        }

        if (calibrate && line.Flag("--update-config"))
        {
            throw new ArgumentException("--calibrate is for experiments; use the calibrate command with --update-config to commit a temperature.");
        }

        if (line.Flag("--update-config") && overridden)
        {
            throw new ArgumentException("--update-config records the image of the committed configuration and cannot be combined with overrides.");
        }

        // An experiment must name its output so that it never overwrites the shipped weights (fail fast, before the corpora load)
        OutputGuard.EnsureExplicitOutput(overridden || calibrate, line.Value("--output"));

        // Train, reporting every epoch and evaluating the requested checkpoints
        var lexicon = EmbeddedModel.Lexicon;
        var data = CorpusReader.LoadSources(root, config);
        Console.WriteLine($"Training on {data.Count:N0} sentences ({data.Sum(s => s.Tokens.Length):N0} tokens per epoch), {Describe(config)}");
        var checkpoints = ParseCheckpoints(line.Value("--checkpoints"));
        var label = line.Value("--label") ?? "run";
        var log = line.Value("--log");
        DevData? dev = null;

        // The right preview group trains on the tags of fold models that did not see the sentence (see Jackknife); they come first
        Dictionary<TrainingSentence, int[]>? previews = null;
        if ((config.ResolveGroups() & FeatureGroups.RightPreview) != 0)
        {
            var jackknifeClock = Stopwatch.StartNew();
            previews = Jackknife.Compute(config, lexicon, data, Console.WriteLine);
            Console.WriteLine($"Jackknife of {config.PreviewFolds} folds finished in {jackknifeClock.Elapsed.TotalSeconds:0.0} s; peak working set {PeakMegabytes()} MB");
        }

        var clock = Stopwatch.StartNew();
        var perceptron = PerceptronTrainer.Train(config, lexicon, data, report =>
        {
            Console.WriteLine(
                $"epoch {report.Epoch,2}/{config.Epochs}  steps {report.Steps,9:N0}  mistakes {report.Mistakes,8:N0} ({100.0 * report.Mistakes / report.Steps:0.00}%)  {report.Seconds,6:0.0} s  peak working set {PeakMegabytes()} MB");
            if (checkpoints.Contains(report.Epoch) && report.Epoch < config.Epochs)
            {
                dev ??= DevData.Load(root);
                var rows = ModelBuilder.Rows(report.Perceptron, config);
                var file = ModelBuilder.Encode(rows, config, false);
                var checkpointConfig = config.Clone();
                if (calibrate)
                {
                    file = FitTemperature(file, dev, checkpointConfig);
                }

                Report($"{label}@ep{report.Epoch}", Describe(checkpointConfig) + $" epochs={report.Epoch}", file, 0, clock.Elapsed.TotalSeconds, dev, log, root);
            }
        }, previews: previews);
        var trainSeconds = clock.Elapsed.TotalSeconds;

        // The image is what is compared and hashed; the committed file is the compressed form of the same payload
        var finalRows = ModelBuilder.Rows(perceptron, config);
        if (calibrate)
        {
            dev ??= DevData.Load(root);
            FitTemperature(ModelBuilder.Encode(finalRows, config, false), dev, config);
        }

        var image = ModelBuilder.Encode(finalRows, config, false);
        var sha = ModelBuilder.Sha256Hex(image);
        Console.WriteLine($"Trained {perceptron.Steps:N0} steps in {trainSeconds:0.0} s; image {image.Length:N0} bytes, {finalRows.Count:N0} rows, sha256 {sha}");
        if (check)
        {
            return Verify(config, image, sha, line.Value("--weights") ?? Path.Combine(root, DefaultWeights.Replace('/', Path.DirectorySeparatorChar)));
        }

        // Write the weights file, optionally record the image hash, and optionally evaluate
        var output = line.Value("--output") ?? Path.Combine(root, DefaultWeights.Replace('/', Path.DirectorySeparatorChar));
        var compressed = ModelBuilder.Encode(finalRows, config, true);
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(output))!);
        File.WriteAllBytes(output, compressed);
        Console.WriteLine($"Wrote {output}: {compressed.Length:N0} bytes deflated, {image.Length:N0} bytes raw.");
        if (line.Flag("--update-config"))
        {
            committed.ImageSha256 = sha;
            ConfigFile.Save(configPath, committed);
            Console.WriteLine($"Recorded imageSha256 in {configPath}.");
        }

        if (line.Flag("--eval") || log != null)
        {
            dev ??= DevData.Load(root);
            Report(label, Describe(config), image, compressed.Length, trainSeconds, dev, log, root);
        }

        Console.WriteLine($"Peak working set {PeakMegabytes()} MB.");
        return 0;
    }

    /// <summary>
    ///     Fits the temperature of a trained image on the open development split and records it in the (experiment) configuration.
    /// </summary>
    /// <param name="image">The trained image.</param>
    /// <param name="dev">The development data.</param>
    /// <param name="config">The experiment configuration that receives the temperature (never the committed one).</param>
    /// <returns>The image with the fitted temperature in its header.</returns>
    private static byte[] FitTemperature(byte[] image, DevData dev, TrainingConfig config)
    {
        var best = Calibration.Fit(image, dev.Open);
        config.Temperature = best.Temperature;
        return ModelBuilder.WithTemperature(image, (float)best.Temperature);
    }

    /// <summary>
    ///     Compares a fresh image with the committed weights file and the recorded hash.
    /// </summary>
    /// <param name="config">The committed configuration.</param>
    /// <param name="image">The fresh uncompressed image.</param>
    /// <param name="sha">The SHA-256 of the fresh image.</param>
    /// <param name="committedPath">The committed weights file.</param>
    /// <returns>0 when everything is current, 1 otherwise.</returns>
    private static int Verify(TrainingConfig config, byte[] image, string sha, string committedPath)
    {
        // The command that fixes a stale or missing file is part of the message so the failure is actionable
        const string Regenerate = "dotnet run --project tools/DemaConsulting.PartOfSpeech.Trainer -c Release -- train --update-config";
        var committed = File.Exists(committedPath) ? File.ReadAllBytes(committedPath) : null;
        var failed = false;
        switch (ModelBuilder.Check(image, committed))
        {
            case CheckStatus.Current:
                Console.WriteLine($"The weights {committedPath} are current.");
                break;
            case CheckStatus.Missing:
                Console.Error.WriteLine($"The weights {committedPath} do not exist. Regenerate them with: {Regenerate}");
                failed = true;
                break;
            case CheckStatus.Invalid:
                Console.Error.WriteLine($"The weights {committedPath} are not a valid weights file of this build. Regenerate them with: {Regenerate}");
                failed = true;
                break;
            default:
                Console.Error.WriteLine($"The weights {committedPath} do not match a fresh training with the committed configuration. Regenerate them with: {Regenerate}");
                failed = true;
                break;
        }

        if (!string.Equals(config.ImageSha256, sha, StringComparison.Ordinal))
        {
            Console.Error.WriteLine($"imageSha256 of the configuration ({(config.ImageSha256.Length == 0 ? "empty" : config.ImageSha256)}) does not match the fresh image ({sha}). Regenerate with: {Regenerate}");
            failed = true;
        }

        return failed ? 1 : 0;
    }

    /// <summary>
    ///     Runs the <c>eval</c> command.
    /// </summary>
    /// <param name="line">The parsed command line.</param>
    /// <param name="root">The repository root.</param>
    /// <returns>The exit code.</returns>
    private static int Eval(CommandLine line, string root)
    {
        var path = line.Value("--weights") ?? Path.Combine(root, DefaultWeights.Replace('/', Path.DirectorySeparatorChar));
        var file = File.ReadAllBytes(path);
        if (line.Value("--temperature") is { } temperature)
        {
            file = ModelBuilder.WithTemperature(file, float.Parse(temperature, CultureInfo.InvariantCulture));
        }

        var dev = DevData.Load(root);
        Report(line.Value("--label") ?? Path.GetFileName(path), "eval " + Path.GetFileName(path), file, 0, 0, dev, line.Value("--log"), root);
        return 0;
    }

    /// <summary>
    ///     Runs the <c>eval-shapes</c> command: scores a weights file on the hard-shape development set and prints the Markdown report.
    /// </summary>
    /// <param name="line">The parsed command line.</param>
    /// <param name="root">The repository root.</param>
    /// <returns>The exit code.</returns>
    private static int EvalShapes(CommandLine line, string root)
    {
        var path = line.Value("--weights") ?? Path.Combine(root, DefaultWeights.Replace('/', Path.DirectorySeparatorChar));
        var file = File.ReadAllBytes(path);
        var which = line.Value("--tranche") ?? "both";
        ShapesDevCommand.EnsureSelection(which);

        var label = line.Value("--label") ?? Path.GetFileName(path);
        var tagger = DevEvaluator.CreateTagger(file);
        var lines = ShapesDevCommand.Run(root, tagger, label, ModelBuilder.Sha256Hex(file), which);
        foreach (var text in lines)
        {
            Console.WriteLine(text);
        }

        return 0;
    }

    /// <summary>
    ///     Runs the <c>eval-arm</c> command: scores a trained weights file on the development sets of the context-feature experiment
    ///     and writes the result as JSON.
    /// </summary>
    /// <param name="line">The parsed command line.</param>
    /// <param name="root">The repository root.</param>
    /// <returns>The exit code.</returns>
    /// <exception cref="InvalidOperationException">The weights or the result file lies in the data folder or the data tooling folder.</exception>
    /// <exception cref="InvalidDataException">The weights are not a valid file, or a development input is missing or does not match its pin.</exception>
    private static int EvalArm(CommandLine line, string root)
    {
        // The command has no data option at all; the only paths are the weights to read and the result to write, and neither may lie in a data folder
        var weightsPath = DataGuard.EnsureArmFile(root, RequireValue(line, "--weights"));
        var jsonPath = DataGuard.EnsureArmFile(root, RequireValue(line, "--json"));
        var label = RequireValue(line, "--label");
        var weights = File.ReadAllBytes(weightsPath);
        var committed = ConfigFile.Load(Path.Combine(root, DefaultConfig.Replace('/', Path.DirectorySeparatorChar)));
        var data = ArmEvaluationData.Load(root);
        (data.Inputs.Commit, data.Inputs.Dirty) = GitState(root);
        var echo = new ArmConfigEcho { PreviewFolds = committed.PreviewFolds, Epochs = committed.Epochs, Prune = committed.PruneThreshold };
        var json = ArmEvaluation.Evaluate(weights, label, echo, data);
        Directory.CreateDirectory(Path.GetDirectoryName(jsonPath)!);
        File.WriteAllText(jsonPath, json, new UTF8Encoding(false));
        Console.WriteLine($"Wrote {jsonPath} ({json.Length:N0} characters) for {label}.");
        return 0;
    }

    /// <summary>
    ///     Asks git for the commit of the repository and whether tracked files have uncommitted changes.
    /// </summary>
    /// <param name="root">The repository root.</param>
    /// <returns>The commit and the dirty flag; <c>unknown</c> and dirty when git does not answer.</returns>
    private static (string Commit, bool Dirty) GitState(string root)
    {
        var commit = RunGit(root, "rev-parse HEAD");
        var status = RunGit(root, "status --porcelain --untracked-files=no");
        return commit == null || status == null ? ("unknown", true) : (commit.Trim(), status.Trim().Length > 0);
    }

    /// <summary>
    ///     Runs git in the repository and returns its output.
    /// </summary>
    /// <param name="root">The repository root.</param>
    /// <param name="arguments">The arguments.</param>
    /// <returns>The standard output, or <c>null</c> when git is missing or fails.</returns>
    private static string? RunGit(string root, string arguments)
    {
        try
        {
            // Development tool: git is resolved from the PATH like in the shell of the person who runs the experiment
#pragma warning disable S4036
            var start = new ProcessStartInfo("git", arguments) { WorkingDirectory = root, RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false };
#pragma warning restore S4036
            using var process = Process.Start(start);
            if (process == null)
            {
                return null;
            }

            var output = process.StandardOutput.ReadToEnd();
            process.StandardError.ReadToEnd();
            process.WaitForExit();
            return process.ExitCode == 0 ? output : null;
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException or IOException)
        {
            return null;
        }
    }

    /// <summary>
    ///     Runs the <c>shapes-tokenize</c> command: tokenizes the lines of a text file with the library tokenizer.
    /// </summary>
    /// <param name="line">The parsed command line.</param>
    /// <param name="root">The repository root.</param>
    /// <returns>The exit code.</returns>
    /// <exception cref="InvalidDataException">An input line contains a tab character.</exception>
    /// <exception cref="InvalidOperationException">An input or output path lies inside the data folder of the repository.</exception>
    private static int ShapesTokenize(CommandLine line, string root)
    {
        // Path guard: the command must never read or overwrite repository data (training, development or held-out files)
        var input = DataGuard.EnsureOutsideData(root, RequireValue(line, "--in"));
        var output = DataGuard.EnsureOutsideData(root, RequireValue(line, "--out"));
        var result = new StringBuilder();
        foreach (var text in File.ReadAllLines(input, Encoding.UTF8))
        {
            if (text.Contains('\t'))
            {
                throw new InvalidDataException($"{input}: a sentence contains a tab character.");
            }

            var sentences = Tokenizer.Tokenize(text);
            var tokens = sentences.SelectMany(s => s.Tokens).Select(t => t.Text);
            result.Append(text).Append('\t').Append(sentences.Count.ToString(CultureInfo.InvariantCulture)).Append('\t').Append(string.Join(" ", tokens)).Append('\n');
        }

        File.WriteAllText(output, result.ToString(), new UTF8Encoding(false));
        return 0;
    }

    /// <summary>
    ///     Runs the <c>calibrate</c> command.
    /// </summary>
    /// <param name="line">The parsed command line.</param>
    /// <param name="root">The repository root.</param>
    /// <returns>The exit code.</returns>
    private static int Calibrate(CommandLine line, string root)
    {
        var file = File.ReadAllBytes(RequireValue(line, "--weights"));
        var dev = DevData.Load(root);
        var best = Calibration.Fit(file, dev.Open, point => Console.WriteLine(Calibration.Format(point)));
        Console.WriteLine($"Best temperature on the open development split: {best.Temperature.ToString("0.###", CultureInfo.InvariantCulture)}  ({Calibration.Format(best)})");

        // Calibration of the gold v2 development set at that temperature, for information only
        var tagger = DevEvaluator.CreateTagger(ModelBuilder.WithTemperature(file, (float)best.Temperature));
        var gold = Metrics.Compute(DevEvaluator.EvaluateGold(tagger, dev.Gold, false));
        Console.WriteLine($"Gold v2 dev at that temperature: NLL {gold.Nll:0.0000}, ECE {gold.Ece:0.00}%");
        if (line.Flag("--update-config"))
        {
            var configPath = line.Value("--config") ?? Path.Combine(root, DefaultConfig.Replace('/', Path.DirectorySeparatorChar));
            var config = ConfigFile.Load(configPath);
            config.Temperature = best.Temperature;
            ConfigFile.Save(configPath, config);
            Console.WriteLine($"Recorded the temperature in {configPath}. Retrain with --update-config to refresh the weights and imageSha256.");
        }

        return 0;
    }

    /// <summary>
    ///     Runs the <c>compare</c> command.
    /// </summary>
    /// <param name="line">The parsed command line.</param>
    /// <param name="root">The repository root.</param>
    /// <returns>The exit code.</returns>
    private static int Compare(CommandLine line, string root)
    {
        var a = DevEvaluator.CreateTagger(File.ReadAllBytes(RequireValue(line, "--a")));
        var b = DevEvaluator.CreateTagger(File.ReadAllBytes(RequireValue(line, "--b")));
        var resamples = ParseInt32(line.Value("--resamples"), "--resamples", Bootstrap.DefaultResamples);
        var seed = ParseUInt64(line.Value("--seed"), "--seed", 1UL);
        var dev = DevData.Load(root);
        var resultA = DevEvaluator.Evaluate(a, dev.Open, dev.Gold);
        var resultB = DevEvaluator.Evaluate(b, dev.Open, dev.Gold);

        // Paired differences (a minus b) by document, for each set and pooled
        PrintBootstrap("open dev", resultA.Open, resultB.Open, resamples, seed);
        PrintBootstrap("gold v2 dev", resultA.Gold, resultB.Gold, resamples, seed);
        PrintBootstrap("pooled", resultA.Pooled, resultB.Pooled, resamples, seed);
        return 0;
    }

    /// <summary>
    ///     Runs the <c>glossary-probe</c> command.
    /// </summary>
    /// <param name="line">The parsed command line.</param>
    /// <param name="root">The repository root.</param>
    /// <returns>The exit code.</returns>
    private static int Probe(CommandLine line, string root)
    {
        var file = File.ReadAllBytes(RequireValue(line, "--weights"));
        var seed = ParseUInt64(line.Value("--seed"), "--seed", 1UL);
        foreach (var text in GlossaryProbe.Run(file, DevData.Load(root).Open, EmbeddedModel.Lexicon, seed))
        {
            Console.WriteLine(text);
        }

        return 0;
    }

    /// <summary>
    ///     Runs the <c>glossary-eval</c> command: the dev-only evaluation of the glossary API with the embedded model.
    /// </summary>
    /// <param name="line">The parsed command line.</param>
    /// <param name="root">The repository root.</param>
    /// <returns>The exit code.</returns>
    /// <exception cref="InvalidOperationException">The embedded weights resource is missing.</exception>
    private static int GlossaryEvaluate(CommandLine line, string root)
    {
        var seed = ParseUInt64(line.Value("--seed"), "--seed", 1UL);
        var resamples = ParseInt32(line.Value("--resamples"), "--resamples", Bootstrap.DefaultResamples);

        // The embedded model is what Tagger.Default uses; the data are the two development sets and nothing else
        using var stream = EmbeddedModel.OpenResource(EmbeddedModel.WeightsResourceName)
            ?? throw new InvalidOperationException($"The embedded resource '{EmbeddedModel.WeightsResourceName}' is missing.");
        using var buffer = new MemoryStream();
        stream.CopyTo(buffer);
        var dev = DevData.Load(root);
        foreach (var text in GlossaryEvaluation.Run(buffer.ToArray(), dev.Open, dev.Gold, EmbeddedModel.Lexicon, seed, resamples))
        {
            Console.WriteLine(text);
        }

        return 0;
    }

    /// <summary>
    ///     Runs the <c>thresholds</c> command: the development-only confidence curve and the pre-registered operating points.
    /// </summary>
    /// <param name="line">The parsed command line.</param>
    /// <param name="root">The repository root.</param>
    /// <returns>The exit code.</returns>
    /// <exception cref="InvalidDataException">The embedded model is not the frozen one or the 0.45 row does not reproduce the frozen figures.</exception>
    private static int Thresholds(CommandLine line, string root)
    {
        var seed = ParseUInt64(line.Value("--seed"), "--seed", 1UL);
        var resamples = ParseInt32(line.Value("--resamples"), "--resamples", Bootstrap.DefaultResamples);

        // The model must be the frozen one: its image hash is recorded in the configuration
        var weights = OperatingPointsFile.ReadEmbeddedWeights();
        var modelSha = ModelBuilder.Sha256Hex(PerceptronModel.ToUncompressedImage(weights));
        var config = ConfigFile.Load(Path.Combine(root, DefaultConfig.Replace('/', Path.DirectorySeparatorChar)));
        if (!string.Equals(modelSha, config.ImageSha256, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException("The embedded weights are not the frozen model of training.json; the selection is made for the frozen model only.");
        }

        // Score the two development sets (the only data this command can read) and select the points
        var dev = DevData.Load(root);
        var result = DevEvaluator.Evaluate(DevEvaluator.CreateTagger(weights), dev.Open, dev.Gold);
        var selection = DevSelection.Create(modelSha, config.Temperature, result.Open, result.Gold, resamples, seed);

        // Consistency check: the 0.45 row must reproduce the harmful errors of the freeze table, otherwise nothing is written
        Console.WriteLine($"At 0.45: harmful {selection.Open.Harmful} (open dev), {selection.Gold.Harmful} (gold v2 dev), {selection.Pooled.Harmful} (pooled); frozen figures {FrozenOpenHarmful}, {FrozenGoldHarmful}, {FrozenOpenHarmful + FrozenGoldHarmful}.");
        if (selection.Open.Harmful != FrozenOpenHarmful || selection.Gold.Harmful != FrozenGoldHarmful)
        {
            throw new InvalidDataException("The 0.45 row does not reproduce the frozen development figures; nothing was written.");
        }

        var folder = Path.Combine(root, EvaluationFolder.Replace('/', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(folder);
        File.WriteAllText(Path.Combine(folder, "operating-points.json"), OperatingPointsFile.Format(selection.ToArtifact(OperatingPointsFile.DevFiles(root))));
        File.WriteAllText(Path.Combine(folder, "confidence-thresholds.md"), EvaluationReport.RenderDev(selection, EvaluationReport.ReadDisclosure(Path.Combine(folder, DisclosureFile))));
        foreach (var point in selection.Points)
        {
            Console.WriteLine(point.Row == null
                ? $"{point.Name}: no grid threshold qualifies (target {point.MaxHarmfulPer1000Answered:0.0} per 1,000 answered at coverage >= {point.MinCoveragePercent:0}%)."
                : $"{point.Name}: threshold {OperatingPointRule.Format(point.Row.Threshold)}, coverage {point.Row.CoveragePercent:0.0}%, harmful {point.Row.HarmfulPer1000Answered:0.0} per 1,000 answered ({point.Row.Harmful} of {point.Row.Answered}).");
        }

        Console.WriteLine($"Wrote operating-points.json and confidence-thresholds.md to {folder}.");
        return 0;
    }

    /// <summary>
    ///     Runs the <c>evaluate</c> command: the one held-out evaluation of the embedded model.
    /// </summary>
    /// <param name="line">The parsed command line.</param>
    /// <param name="root">The repository root.</param>
    /// <returns>0 on success, 1 when a stop condition of the protocol fired, 2 for a gate or data error.</returns>
    /// <exception cref="ArgumentException">An option is combined with <c>--preflight</c> that has no meaning there.</exception>
    /// <exception cref="InvalidDataException">The selection artifact is not valid for the frozen model or an input differs from its recorded hash or count.</exception>
    private static int Evaluate(CommandLine line, string root)
    {
        var preflight = line.Flag("--preflight");
        var seed = ParseUInt64(line.Value("--seed"), "--seed", 1UL);
        var resamples = ParseInt32(line.Value("--resamples"), "--resamples", Bootstrap.DefaultResamples);
        if (preflight && line.Value("--dump-dir") != null)
        {
            throw new ArgumentException("--preflight prints counts only and writes nothing; it cannot be combined with --dump-dir.");
        }

        // The gate: the committed selection must be valid for the frozen model and the committed development files
        var weights = OperatingPointsFile.ReadEmbeddedWeights();
        var modelSha = ModelBuilder.Sha256Hex(PerceptronModel.ToUncompressedImage(weights));
        var config = ConfigFile.Load(Path.Combine(root, DefaultConfig.Replace('/', Path.DirectorySeparatorChar)));
        var folder = Path.Combine(root, EvaluationFolder.Replace('/', Path.DirectorySeparatorChar));
        var selection = OperatingPointsFile.Load(Path.Combine(folder, "operating-points.json"), OperatingPointsFile.Expect(root, modelSha, config.ImageSha256, weights));

        // Run once, and keep the per-token records outside the repository
        var resultsPath = Path.Combine(folder, "test-results.json");
        string? dump = null;
        if (!preflight)
        {
            DataGuard.EnsureNotEvaluated(resultsPath);
            dump = line.Value("--dump-dir") is { } directory ? DataGuard.EnsureOutsideRepository(root, directory) : null;
        }

        // Read every input through the guard and compare the counts with the documented ones (counts only)
        var data = HeldOutData.Load(root, selection, line.Value("--ud-cache") ?? DataGuard.UdCacheFolder);
        var counts = data.Counts();
        foreach (var count in counts)
        {
            Console.WriteLine($"{count.Name}: {count.Actual:N0} (documented {count.Expected:N0}){(count.Matches ? string.Empty : " MISMATCH")}");
        }

        if (counts.Any(c => !c.Matches))
        {
            Console.Error.WriteLine("A held-out input does not match its documented size; stopping (a stop condition of the protocol).");
            return 2;
        }

        if (preflight)
        {
            Console.WriteLine("Pre-flight passed: every input was read, its hash and header verified and its size matches. Nothing was scored.");
            return 0;
        }

        // The one run: take the exclusive run lock (atomic, before anything is scored), judge every set with the embedded
        // model, then measure the throughput of the default tagger. An exception from here on leaves the lock behind.
        using var runLock = DataGuard.AcquireRunLock(resultsPath);
        var scores = HeldOutEvaluator.Score(DevEvaluator.CreateTagger(weights), EmbeddedModel.Lexicon, data);
        var results = new ResultsData
        {
            ModelImageSha256 = modelSha,
            SelectionSha256 = selection.SelectionSha256,
            RuleId = selection.Data.RuleId,
            Resamples = resamples,
            Seed = seed,
            Runtime = System.Runtime.InteropServices.RuntimeInformation.FrameworkDescription + " on " + System.Runtime.InteropServices.RuntimeInformation.OSDescription,
            TokensPerSecond = HeldOutEvaluator.Throughput(Tagger.Default, data.OpenTest.Select(s => s.Tokens).ToList()),
        };
        results.Inputs.AddRange(data.Inputs.Select(i => new InputRecord { Name = i.Key, Sha256 = i.Value }));
        var views = new (string Name, List<EvalToken> Tokens)[]
        {
            (HeldOutEvaluator.Names.OpenTest, scores.OpenTest),
            (HeldOutEvaluator.Names.GoldTest, scores.Gold),
            (HeldOutEvaluator.Names.GoldTest + "-full-agreement", scores.GoldFullAgreement),
            (HeldOutEvaluator.Names.GoldTest + "-to-not", scores.GoldConvention),
            (HeldOutEvaluator.Names.Probe, scores.Probe),
            (HeldOutEvaluator.Names.Challenge, scores.Challenge),
            (HeldOutEvaluator.Names.UdMapped, scores.UdMapped),
            (HeldOutEvaluator.Names.UdRaw, scores.UdRaw),
        };
        foreach (var (name, tokens) in views)
        {
            results.Sets.Add(EvaluationReport.Summarize(name, tokens, selection.SelectedPoints, resamples, seed));
        }

        results.TripwiresFired.AddRange(EvaluationReport.Tripwires(results, selection));

        // Write the per-token records first, then the report, and the results file (the run-once marker) last; completing the
        // lock writes the results file exclusively and only then removes the lock file
        if (dump != null)
        {
            foreach (var (name, tokens) in EvaluationReport.Views(scores))
            {
                EvaluationReport.WriteDump(dump, name, tokens);
            }

            Console.WriteLine($"Wrote the per-token records to {dump}.");
        }

        File.WriteAllText(Path.Combine(folder, "test-results.md"), EvaluationReport.RenderResults(results, scores, EvaluationReport.ReadDisclosure(Path.Combine(folder, DisclosureFile))));
        runLock.Complete(OperatingPointsFile.Format(results));
        Console.WriteLine($"Wrote test-results.md and test-results.json to {folder}.");
        Console.WriteLine(results.TripwiresFired.Count == 0 ? "No stop condition fired." : "Stop conditions fired:\n" + string.Join("\n", results.TripwiresFired));
        return results.TripwiresFired.Count == 0 ? 0 : 1;
    }

    /// <summary>
    ///     Runs the <c>glossary-experiment</c> command.
    /// </summary>
    /// <param name="line">The parsed command line.</param>
    /// <param name="root">The repository root.</param>
    /// <returns>The exit code.</returns>
    private static int GlossaryDecision(CommandLine line, string root)
    {
        // M0: the configured model; M1: the same configuration trained with simulated glossary hints
        var configPath = line.Value("--config") ?? Path.Combine(root, DefaultConfig.Replace('/', Path.DirectorySeparatorChar));
        var config = ConfigFile.Load(configPath).Clone();
        config.Validate();

        // The experiment decodes in a single pass: refuse a configuration with the right preview group before anything is trained
        var groups = config.ResolveGroups();
        HintedPredictor.EnsureSupported(groups);
        var seed = ParseUInt64(line.Value("--seed"), "--seed", 1UL);
        var lexicon = EmbeddedModel.Lexicon;
        var data = CorpusReader.LoadSources(root, config);
        var dev = DevData.Load(root);

        var clock = Stopwatch.StartNew();
        var rows0 = ModelBuilder.Rows(PerceptronTrainer.Train(config, lexicon, data), config);
        var file0 = ModelBuilder.Encode(rows0, config, false);
        Console.WriteLine($"M0 trained in {clock.Elapsed.TotalSeconds:0.0} s: {rows0.Count:N0} rows, temperature {config.Temperature}");
        clock.Restart();
        var glossary = new GlossaryTraining(lexicon, GlossaryExperiment.TrainingHintPercent, GlossaryExperiment.TrainingWrongPercent, (ulong)config.Seed ^ GlossarySeedSalt);
        var rows1 = ModelBuilder.Rows(PerceptronTrainer.Train(config, lexicon, data, null, glossary), config);
        var config1 = config.Clone();
        var file1 = ModelBuilder.Encode(rows1, config1, false);
        config1.Temperature = Calibration.Fit(file1, dev.Open).Temperature;
        Console.WriteLine($"M1 trained in {clock.Elapsed.TotalSeconds:0.0} s: {rows1.Count:N0} rows, fitted temperature {config1.Temperature}");

        if (line.Value("--m0-output") is { } m0Output)
        {
            File.WriteAllBytes(m0Output, ModelBuilder.Encode(rows0, config, true));
        }

        if (line.Value("--m1-output") is { } m1Output)
        {
            File.WriteAllBytes(m1Output, ModelBuilder.Encode(rows1, config1, true));
        }

        var m0 = new HintedPredictor(config.HashBits, rows0, config.Scale, (float)config.Temperature, lexicon, groups);
        var m1 = new HintedPredictor(config1.HashBits, rows1, config1.Scale, (float)config1.Temperature, lexicon, groups);
        foreach (var text in GlossaryExperiment.Run(m0, m1, file0, dev.Open, lexicon, seed))
        {
            Console.WriteLine(text);
        }

        return 0;
    }

    /// <summary>
    ///     Prints the paired bootstrap of two outcome lists.
    /// </summary>
    /// <param name="name">The name of the set.</param>
    /// <param name="a">The outcomes of the first model.</param>
    /// <param name="b">The outcomes of the second model.</param>
    /// <param name="resamples">The number of resamples.</param>
    /// <param name="seed">The seed.</param>
    private static void PrintBootstrap(string name, List<Outcome> a, List<Outcome> b, int resamples, ulong seed)
    {
        Bootstrap.Compare(a, b, resamples, seed, out var accuracy, out var harmful);
        string Format(BootstrapResult r) => string.Format(CultureInfo.InvariantCulture, "{0:+0.000;-0.000;0.000} [{1:+0.000;-0.000;0.000}, {2:+0.000;-0.000;0.000}]{3}", r.Difference, r.Low, r.High, r.ContainsZero ? " (tied)" : string.Empty);
        Console.WriteLine($"{name,-12} accuracy a-b (points): {Format(accuracy)}   harmful per 1k N/V a-b: {Format(harmful)}");
    }

    /// <summary>
    ///     Evaluates a weights file on the development sets, prints the summary and optionally appends a run-log row.
    /// </summary>
    /// <param name="label">The label of the run.</param>
    /// <param name="settings">The one-line description of the settings.</param>
    /// <param name="file">The weights file (compressed or not).</param>
    /// <param name="deflatedBytes">The size of the compressed file, or 0 when not known.</param>
    /// <param name="trainSeconds">The training time, or 0.</param>
    /// <param name="dev">The development data.</param>
    /// <param name="log">The run-log file, or <c>null</c>.</param>
    /// <param name="root">The repository root.</param>
    private static void Report(string label, string settings, byte[] file, int deflatedBytes, double trainSeconds, DevData dev, string? log, string root)
    {
        var model = PerceptronModel.Load(file);
        var tagger = DevEvaluator.CreateTagger(file);
        var summary = new RunSummary
        {
            Label = label,
            Settings = settings,
            Result = DevEvaluator.Evaluate(tagger, dev.Open, dev.Gold),
            Rows = model.RowCount,
            Weights = model.WeightCount,
            RawBytes = PerceptronModel.ToUncompressedImage(file).Length,
            DeflatedBytes = deflatedBytes,
            TrainSeconds = trainSeconds,
        };
        var open = Metrics.Compute(summary.Result.Open);
        var gold = Metrics.Compute(summary.Result.Gold);
        var convention = Metrics.Compute(summary.Result.Convention);
        Console.WriteLine($"[{label}] open dev : {open.Tokens} tokens, accuracy {open.Accuracy:0.00}%, ambiguous ({open.AmbiguousTokens}) {open.AmbiguousAccuracy:0.00}%, harmful {open.HarmfulErrors} ({open.HarmfulPer1000:0.0} per 1k of {open.NounVerbDecisions} N/V), NLL {open.Nll:0.0000}, ECE {open.Ece:0.00}%, gold outside candidates {open.OutsideCandidates}");
        Console.WriteLine($"[{label}] gold dev : {gold.Tokens} targets, accuracy {gold.Accuracy:0.00}%, harmful {gold.HarmfulErrors} ({gold.HarmfulPer1000:0.0} per 1k of {gold.NounVerbDecisions} N/V), NLL {gold.Nll:0.0000}, ECE {gold.Ece:0.00}%; to/not ({convention.Tokens}) accuracy {convention.Accuracy:0.00}% (reported separately)");
        Console.WriteLine($"[{label}] model    : {summary.Rows:N0} rows, {summary.Weights:N0} weights, {summary.RawBytes:N0} bytes raw, temperature {model.Temperature:0.###}, scale {model.Scale}");
        if (log != null)
        {
            var path = Path.IsPathRooted(log) ? log : Path.Combine(root, log.Replace('/', Path.DirectorySeparatorChar));
            RunLog.Append(path, summary);
            Console.WriteLine($"Appended a row to {path}.");
        }
    }

    /// <summary>
    ///     Applies the experiment overrides of the command line to a configuration.
    /// </summary>
    /// <param name="config">The configuration to change.</param>
    /// <param name="line">The parsed command line.</param>
    /// <returns><c>true</c> if any setting was overridden.</returns>
    /// <exception cref="ArgumentException">A value is malformed or a source name is unknown.</exception>
    private static bool ApplyOverrides(TrainingConfig config, CommandLine line)
    {
        var changed = false;
        int? Int(string name) => line.Value(name) is { } text ? int.Parse(text, CultureInfo.InvariantCulture) : null;
        if (Int("--epochs") is { } epochs)
        {
            config.Epochs = epochs;
            changed = true;
        }

        if (Int("--hash-bits") is { } hashBits)
        {
            config.HashBits = hashBits;
            changed = true;
        }

        if (Int("--min-count") is { } minCount)
        {
            config.MinCount = minCount;
            changed = true;
        }

        if (Int("--prune") is { } prune)
        {
            config.PruneThreshold = prune;
            changed = true;
        }

        if (Int("--scale") is { } scale)
        {
            config.QuantScale = scale;
            changed = true;
        }

        if (line.Value("--seed") is { } seed)
        {
            config.Seed = long.Parse(seed, CultureInfo.InvariantCulture);
            changed = true;
        }

        if (line.Value("--temperature") is { } temperature)
        {
            config.Temperature = double.Parse(temperature, CultureInfo.InvariantCulture);
            changed = true;
        }

        if (line.Value("--restrict") is { } restrict)
        {
            config.Restriction = ParseEnum<RestrictionMode>(restrict, "--restrict");
            changed = true;
        }

        if (line.Value("--history") is { } history)
        {
            config.History = ParseEnum<HistoryMode>(history, "--history");
            changed = true;
        }

        if (line.Value("--features") is { } features)
        {
            config.Features = TrainingConfig.ParseFeatureList(features);
            changed = true;
        }

        if (line.Value("--preview-folds") is { } folds)
        {
            config.PreviewFolds = int.Parse(folds, CultureInfo.InvariantCulture);
            changed = true;
        }

        return ApplySourceOverrides(config, line) || changed;
    }

    /// <summary>
    ///     Applies the overrides that concern the sources.
    /// </summary>
    /// <param name="config">The configuration to change.</param>
    /// <param name="line">The parsed command line.</param>
    /// <returns><c>true</c> if any source setting was overridden.</returns>
    /// <exception cref="ArgumentException">A value is malformed or a source name is unknown.</exception>
    private static bool ApplySourceOverrides(TrainingConfig config, CommandLine line)
    {
        var changed = false;
        foreach (var assignment in line.Values("--source"))
        {
            var colon = assignment.LastIndexOf(':');
            var source = colon > 0 ? config.Sources.FirstOrDefault(s => s.Name == assignment.Substring(0, colon)) : null;
            if (source == null)
            {
                throw new ArgumentException($"--source expects NAME:WEIGHT with a source of the configuration ({string.Join(", ", config.Sources.Select(s => s.Name))}); got '{assignment}'.");
            }

            source.Weight = int.Parse(assignment.Substring(colon + 1), CultureInfo.InvariantCulture);
            changed = true;
        }

        // The remaining source options apply to every source
        foreach (var source in config.Sources)
        {
            if (line.Value("--max-sentences") is { } max)
            {
                source.MaxSentences = int.Parse(max, CultureInfo.InvariantCulture);
                changed = true;
            }

            if (line.Value("--sample-permille") is { } permille)
            {
                source.SamplePermille = int.Parse(permille, CultureInfo.InvariantCulture);
                changed = true;
            }

            if (line.Value("--mask-disputed") is { } mask)
            {
                source.MaskDisputed = bool.Parse(mask);
                changed = true;
            }

            if (line.Value("--silver-min") is { } silver)
            {
                source.MinSilverProbability = int.Parse(silver, CultureInfo.InvariantCulture);
                changed = true;
            }
        }

        return changed;
    }

    /// <summary>
    ///     Parses an enumeration value, ignoring case.
    /// </summary>
    /// <typeparam name="T">The enumeration type.</typeparam>
    /// <param name="text">The text.</param>
    /// <param name="option">The option name for the message.</param>
    /// <returns>The value.</returns>
    /// <exception cref="ArgumentException">The text names no member.</exception>
    private static T ParseEnum<T>(string text, string option)
        where T : struct
    {
        return Enum.TryParse<T>(text, true, out var value)
            ? value
            : throw new ArgumentException($"{option} expects one of {string.Join(", ", Enum.GetNames(typeof(T)).Select(n => n.ToLowerInvariant()))}; got '{text}'.");
    }

    /// <summary>
    ///     Parses an optional unsigned 64-bit option, so that a malformed or out-of-range value is a usage error.
    /// </summary>
    /// <param name="text">The option text, or <c>null</c> when the option is absent.</param>
    /// <param name="option">The option name for the message.</param>
    /// <param name="fallback">The value when the option is absent.</param>
    /// <returns>The value.</returns>
    /// <exception cref="ArgumentException">The text is not an unsigned 64-bit integer.</exception>
    private static ulong ParseUInt64(string? text, string option, ulong fallback)
    {
        if (text == null)
        {
            return fallback;
        }

        return ulong.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out var value)
            ? value
            : throw new ArgumentException($"{option} expects a non-negative integer that fits in 64 bits; got '{text}'.");
    }

    /// <summary>
    ///     Parses an optional positive 32-bit option, so that a malformed or out-of-range value is a usage error.
    /// </summary>
    /// <param name="text">The option text, or <c>null</c> when the option is absent.</param>
    /// <param name="option">The option name for the message.</param>
    /// <param name="fallback">The value when the option is absent.</param>
    /// <returns>The value.</returns>
    /// <exception cref="ArgumentException">The text is not a positive 32-bit integer.</exception>
    private static int ParseInt32(string? text, string option, int fallback)
    {
        if (text == null)
        {
            return fallback;
        }

        return int.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out var value) && value > 0
            ? value
            : throw new ArgumentException($"{option} expects a positive integer that fits in 32 bits; got '{text}'.");
    }

    /// <summary>
    ///     Parses the checkpoint list.
    /// </summary>
    /// <param name="text">The comma-separated epochs, or <c>null</c>.</param>
    /// <returns>The epochs.</returns>
    private static HashSet<int> ParseCheckpoints(string? text) =>
        text == null
            ? new HashSet<int>()
            : new HashSet<int>(text.Split(',').Select(t => int.Parse(t.Trim(), CultureInfo.InvariantCulture)));

    /// <summary>
    ///     Gets the value of a required option.
    /// </summary>
    /// <param name="line">The parsed command line.</param>
    /// <param name="name">The option name.</param>
    /// <returns>The value.</returns>
    /// <exception cref="ArgumentException">The option is missing.</exception>
    private static string RequireValue(CommandLine line, string name) =>
        line.Value(name) ?? throw new ArgumentException($"{line.Command} needs {name}.");

    /// <summary>
    ///     Describes the settings of a configuration on one line.
    /// </summary>
    /// <param name="config">The configuration.</param>
    /// <returns>The description.</returns>
    private static string Describe(TrainingConfig config) =>
        string.Format(
            CultureInfo.InvariantCulture,
            "sources={0} epochs={1} hashBits={2} minCount={3} prune={4} S={5} T={6} restriction={7} history={8} seed={9}{10}",
            string.Join(",", config.Sources.Where(s => s.Weight > 0).Select(s => s.Name + ":" + s.Weight)),
            config.Epochs,
            config.HashBits,
            config.MinCount,
            config.PruneThreshold,
            config.QuantScale,
            config.Temperature,
            config.Restriction,
            config.History,
            config.Seed,
            DescribeFeatures(config));

    /// <summary>
    ///     Describes the feature groups of a configuration for <see cref="Describe"/>; empty for the shipped feature set, so that its line does not change.
    /// </summary>
    /// <param name="config">The configuration.</param>
    /// <returns>The text to append (with a leading space), or an empty string.</returns>
    private static string DescribeFeatures(TrainingConfig config)
    {
        var groups = config.ResolveGroups();
        if (groups == FeatureGroups.None)
        {
            return string.Empty;
        }

        var folds = (groups & FeatureGroups.RightPreview) != 0 ? $" previewFolds={config.PreviewFolds}" : string.Empty;
        return " features=" + string.Join("+", TrainingConfig.NamesOf(groups)) + folds;
    }

    /// <summary>
    ///     Gets the peak working set of the process in megabytes.
    /// </summary>
    /// <returns>The peak working set.</returns>
    private static long PeakMegabytes()
    {
        using var process = Process.GetCurrentProcess();
        return process.PeakWorkingSet64 / (1024 * 1024);
    }

    /// <summary>
    ///     Finds the repository root: the nearest folder above the tool that holds the solution file.
    /// </summary>
    /// <returns>The full path of the repository root.</returns>
    /// <exception cref="DirectoryNotFoundException">No parent folder holds <c>PartOfSpeech.slnx</c>.</exception>
    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory != null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "PartOfSpeech.slnx")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException("PartOfSpeech.slnx was not found above " + AppContext.BaseDirectory);
    }

    /// <summary>
    ///     The two development sets, loaded together.
    /// </summary>
    private sealed class DevData
    {
        /// <summary>
        ///     Gets the open development sentences.
        /// </summary>
        public List<TrainingSentence> Open { get; private init; } = new();

        /// <summary>
        ///     Gets the gold v2 development targets.
        /// </summary>
        public List<GoldTarget> Gold { get; private init; } = new();

        /// <summary>
        ///     Loads both development sets through the guarded readers.
        /// </summary>
        /// <param name="root">The repository root.</param>
        /// <returns>The data.</returns>
        public static DevData Load(string root) => new() { Open = DevSets.ReadOpenDev(root), Gold = DevSets.ReadGoldDev(root) };
    }

    /// <summary>
    ///     A parsed command line: the command, its switches and its (possibly repeated) values.
    /// </summary>
    private sealed class CommandLine
    {
        /// <summary>
        ///     The values by option name.
        /// </summary>
        private readonly Dictionary<string, List<string>> _values = new(StringComparer.Ordinal);

        /// <summary>
        ///     The switches that were given.
        /// </summary>
        private readonly HashSet<string> _flags = new(StringComparer.Ordinal);

        /// <summary>
        ///     Gets the command name.
        /// </summary>
        public string Command { get; private init; } = string.Empty;

        /// <summary>
        ///     Parses the arguments.
        /// </summary>
        /// <param name="args">The arguments; the first is the command.</param>
        /// <returns>The parsed command line.</returns>
        /// <exception cref="ArgumentException">An option is unknown or has no value.</exception>
        public static CommandLine Parse(string[] args)
        {
            var line = new CommandLine { Command = args[0] };
            var index = 1;
            while (index < args.Length)
            {
                var argument = args[index++];
                if (Array.IndexOf(FlagOptions, argument) >= 0)
                {
                    line._flags.Add(argument);
                }
                else if (Array.IndexOf(ValueOptions, argument) >= 0 && index < args.Length)
                {
                    if (!line._values.TryGetValue(argument, out var list))
                    {
                        list = new List<string>();
                        line._values[argument] = list;
                    }

                    list.Add(args[index++]);
                }
                else
                {
                    throw new ArgumentException($"Unknown or incomplete argument '{argument}'.");
                }
            }

            return line;
        }

        /// <summary>
        ///     Determines whether a switch was given.
        /// </summary>
        /// <param name="name">The switch.</param>
        /// <returns><c>true</c> if it was given.</returns>
        public bool Flag(string name) => _flags.Contains(name);

        /// <summary>
        ///     Gets the last value of an option.
        /// </summary>
        /// <param name="name">The option.</param>
        /// <returns>The value, or <c>null</c> if the option was not given.</returns>
        public string? Value(string name) => _values.TryGetValue(name, out var list) ? list[list.Count - 1] : null;

        /// <summary>
        ///     Gets all values of a repeatable option.
        /// </summary>
        /// <param name="name">The option.</param>
        /// <returns>The values in command-line order; empty if the option was not given.</returns>
        public IReadOnlyList<string> Values(string name) => _values.TryGetValue(name, out var list) ? list : Array.Empty<string>();
    }
}

## ModelTrainer

![Part Of Speech Structure](PartOfSpeechView.svg)

### Purpose

`ModelTrainer` is the build-time tool `tools/DemaConsulting.PartOfSpeech.Trainer` that trains the averaged-perceptron
weights that the package embeds (`src/DemaConsulting.PartOfSpeech/Resources/weights.bin`) from the committed corpora, with
the feature extractor, the lexicon restriction and the weights writer of the library itself. It also selects every
setting of the model on development data only, and it checks that the committed weights are exactly what the committed
corpora and the committed configuration produce. It also chooses the confidence thresholds on development data and runs
the one held-out evaluation of the frozen model (see _Confidence Thresholds_ and _Held-Out Evaluation_). It is a unit
directly under the system, like `Upos`. It is not part of
the NuGet package and not part of the runtime; the library never calls it. The tool is a .NET 10 console program
(`Program.cs`, `ConfigFile.cs`, `OperatingPointsFile.cs`, `RunLog.cs`) with an engine in two folders (`Training/` and
`Evaluation/`); the engine
files use only the base class library, so they are compiled into the test project and run by the unit tests on every
target framework, including .NET Framework.

### Data Model

- **`TrainingConfig`** and **`SourceConfig`** (`Training/TrainingConfig.cs`): the complete description of one run, a
  plain validated object. A source has a name, a repository-relative path, an integer `Weight` (how many times its
  sentences enter every epoch; 0 leaves it out), `MaskDisputed`, `MinSilverProbability`, `SamplePermille` and
  `MaxSentences`. The run has `Epochs`, `HashBits` (18 to 24), `MinCount`, `PruneThreshold`, `QuantScale` (a power of
  two; the file scale is its reciprocal, which is exact), `Seed`, `Restriction` (`None`, `History` or `Full`),
  `History` (`Predicted` or `Gold`), `Temperature` and `ImageSha256`. There is no floating-point training setting:
  the temperature is chosen once by the `calibrate` command and is only written into the file header.
- **`training.json`** (`ConfigFile.cs`, System.Text.Json, strict: an unknown member or an out-of-range value is an error):
  the committed selected configuration in canonical form, so that rewriting it makes no changes. The selected values are
  in _Selection of the Shipped Model_ below.
- **`TrainingSentence`** (`Training/CorpusReader.cs`): the tokens of a sentence, the gold tag of every token or "no
  signal" (a masked token), and the id of the sentence.
- **`AveragedPerceptron`** (`Training/AveragedPerceptron.cs`): the dense tables of the weights `W` (`int`) and of the
  time-weighted update sums `U` (`long`), each `2^hashBits * 17` cells, the step counter `T` and the number of
  mistakes.
- **`DeterministicRandom`** (`Training/DeterministicRandom.cs`): SplitMix64 with a Fisher-Yates shuffle; the sequence for
  seed 0 is pinned by a test.
- **`DataGuard`** (`Evaluation/DevSets.cs`): the allow-list of the two development files, the deny-list for training
  sources, which works on the canonical path (absolute, with symbolic links and junctions resolved on .NET 6 and later)
  and requires a training source to lie below the `data` folder of the repository, and the held-out allow-list (the four
  repository sets and the cache of the external treebank), whose resolver and header check need a validated selection.
  `HeldOutReader` (same file) opens the held-out files and verifies their hashes against `data/MANIFEST.tsv` (the
  treebank against its pins) and their headers.
- **Metrics** (`Evaluation/Metrics.cs`): per-token outcomes, accuracy, accuracy on lexicon-ambiguous tokens, harmful
  errors (gold NOUN answered VERB or gold VERB answered NOUN with confidence at least 0.45, counted per 1,000
  noun-verb decisions), negative log-likelihood and expected calibration error (15 equal-width bins), and the paired
  bootstrap by document (1,000 resamples, fixed seed) with 95% intervals.
- **`EvalToken`** and **`EvalTally`** (`Evaluation/EvalTally.cs`): a judged token (set, document, sentence, form, gold,
  chosen label, the 17 probabilities and the `Outcome` made by `Metrics.Judge`, so that development and held-out use one
  definition of a decision and a harmful error) and the additive counts of a set of tokens (accuracy, ambiguous accuracy,
  noun-verb decisions, harmful errors, log-likelihood sum, 15 calibration bins, answered and harmful counts at every
  threshold of the grid, the reliability bins, the noun-verb confusion, per-label true and false positives and pairs).
  A bootstrap resample is the sum of the tallies of the drawn documents; a test asserts that the tally agrees with
  `Metrics.Compute`.
- **`ConfidenceCurve`**, **`CurveRow`**, **`ReliabilityRow`**, **`SelectedPoint`** and **`OperatingPointRule`**
  (`Evaluation/ConfidenceCurve.cs`): the threshold grid (0.45 to 0.95 in steps of 0.05, then 0.96 to 0.99, built from
  integer hundredths), the curve of a tally, the reliability table and the pre-registered selection rule.
- **`ClusterBootstrap`** (`Evaluation/ClusterBootstrap.cs`): percentile intervals of any statistic of a tally over
  resampled clusters (documents, or single rows or sentences for a set without documents); a paired difference is the
  statistic `f - g` on the same draw.
- **`OperatingPointsData`**, **`OperatingPointsExpectation`** and **`SelectedOperatingPoints`**
  (`Evaluation/OperatingPoints.cs`): the content of the selection artifact and the validated proof of a development
  selection; **`OperatingPointsFile`** (`OperatingPointsFile.cs`, System.Text.Json) reads and writes `operating-points.json`
  and `test-results.json` in canonical form and builds the expectation from the embedded model, `training.json` and
  `data/MANIFEST.tsv`.
- **`DevSelection`**, **`SetRecord`**, **`PointResult`**, **`ResultsData`** and **`ReportFormat`**
  (`Evaluation/EvaluationData.cs`) and **`EvaluationReport`** (`Evaluation/EvaluationReport.cs`): the development
  selection, the summaries of the held-out sets, the stop conditions and the deterministic writers of the reports and of
  the per-token dump.
- **`HeldOutSets`**, **`HeldOutData`**, **`UdSentence`** (`Evaluation/HeldOutSets.cs`) and **`HeldOutEvaluator`**
  (`Evaluation/HeldOutEvaluator.cs`): the line parsers of the held-out formats, the data of one run with its documented
  counts, and the judging of every set and view with the embedded model.

### Key Methods

- **Training** (`PerceptronTrainer.Train`): the sentences of all sources are loaded (each repeated by its weight), the
  allowed tags of every token are computed once with `Lexicon.AllowedTags` (the same code that the decoder uses), and
  for every epoch the sentence order is shuffled with `DeterministicRandom` (continuing from the order of the previous
  epoch) and every sentence is decoded from left to right: `FeatureExtractor` gives the 22 buckets of the token (the
  four history features use the two previous tags), the integer scores of the 17 tags are summed, the guess is made,
  and on a mistake against a gold tag the weights of every feature are changed by +1 for the gold tag and -1 for the
  guessed tag, and the update is recorded as `t * delta` in `U`. A token without a training signal is only decoded: it
  is a step of the averaging (because it is decoded at run time) and it enters the history.
- **Predicted history**: the history is the tag that the current weights guessed, as at run time, so the model is trained
  for the conditions it is used under. The restriction mode decides how much of the lexicon the training sees: mode `None`
  guesses among all 17 tags, mode `History` (the shipped mode, "B") updates from the unrestricted guess and takes the
  history from the restricted guess (what the decoder would write), and mode `Full` guesses among the allowed tags only;
  the gold tag counts as allowed when the lexicon misses it, so the update stays defined.
- **Exact averaging and quantization** (`AveragedPerceptron.Quantize`): the averaged weight is `W - U / T`. The stored
  integer is `RoundHalfAwayFromZero(S * (W * T - U) / T)`, evaluated exactly in `long` arithmetic with `S` the
  quantization factor (a power of two), clamped to the 16-bit range without -32768; a weight whose magnitude is at or
  below the prune threshold is dropped, and a row whose weights are all zero is dropped. No floating-point value enters
  the weights. The updates, the sums and the quantization are `checked`, so a run too large for the 64-bit sums fails
  with a clear exception instead of wrapping around; the check costs one flag test per addition and does not change any
  weight of a run that fits (`train --check` still reproduces the committed image).
- **Minimum count**: when `MinCount` is above 1 a feature takes part in training only if it occurs often enough in the
  training signal.
- **Building the file** (`ModelBuilder`): the rows go to `PerceptronModelWriter.Compile` (which orders the buckets) with
  `hashBits`, the temperature, the scale and `FeatureExtractor.FeatureSetVersion`; `Build(..., compress: false)` gives
  the uncompressed image that is compared and hashed, `compress: true` the committed file.
- **`train --check`**: retrains with `training.json`, builds the uncompressed image and compares it with
  `PerceptronModel.ToUncompressedImage` of the committed file and with `imageSha256`; the status is `Current`, `Missing`,
  `Stale` or `Invalid`, and the exit code is 1 with the regeneration command in the message unless the file is current.
  The same method is how `LexiconCompiler --check` works for the lexicon. `build.ps1` and the CI build run it (about 15
  seconds, on all three operating systems; every job compares its own retraining with the same pinned `imageSha256`, so
  a difference between the operating systems fails the job of that system).
- **Evaluation** (`DevEvaluator`, `DevSets`, `Metrics`, `Calibration`): a weights file is scored on the two development
  sets through the real decoder (`Tagger.Load` on the in-memory file), so a difference between the training predictor and
  the runtime shows up as a measured difference. `Calibration.Fit` chooses the temperature with the minimum
  log-likelihood on the open development split. `compare` runs the paired bootstrap between two weights files.
- **Glossary experiment** (`GlossaryExperiment`, `GlossaryProbe`, `GlossaryFeatures`): see _Glossary Decision_.
- **Glossary evaluation** (`GlossaryEvaluation`, `GlossaryEvaluationReport`): see _Glossary Evaluation_.
- **Commands** (`Program.cs`): `train` (with experiment overrides, `--check`, `--update-config`, `--eval`, `--log`,
  `--calibrate`, `--checkpoints`), `eval`, `calibrate`, `compare`, `glossary-probe`, `glossary-experiment` and
  `glossary-eval` (`--seed`, `--resamples`; it takes no weights or data path and appends no run-log row), `thresholds`
  (development data only) and `evaluate` (the one held-out run; see _Held-Out Evaluation_); the exit
  code is 0 for success, 1 for stale weights (or a stop condition that fired in `evaluate`) and 2 for a usage or data
  error. Overrides are rejected together with
  `--check` and `--update-config`. An experiment (overrides or `--calibrate`) must name `--output`, so it can never
  overwrite the shipped weights (`OutputGuard`, which fails fast, before the corpora are loaded, with exit code 2). The
  README of the tool lists every option.

### Determinism Contract

The training is integer arithmetic (`int` weights, `long` sums), the shuffle uses `DeterministicRandom` and never
`System.Random`, the writer sorts the buckets, and the temperature is a configuration value instead of a fitted one, so
the uncompressed image is designed to be byte-identical on every operating system and framework for the same corpora,
configuration and library; the CI build verifies this on Windows, Linux and macOS against one pinned hash. The Deflate
bytes of the committed file depend on the zlib of the runtime, so the check compares uncompressed
images. A test trains a small subset (300 open sentences, three epochs, 18 bits) and compares the SHA-256 of the image
with a pinned value on every target framework, with and without lexicon restriction; if the extractor, the lexicon
or the open training corpus changes, those values must be pinned again (a failing test prints the new hash). The
environmental inputs are the Unicode normalization and the culture-invariant case mapping and character classification
of the few non-ASCII tokens of the corpora, which are stable by Unicode policy; any difference would surface as a
failing `train --check` or golden test in the CI job of that system.

### Development-Only Selection

Every setting of the shipped model was selected on `data/open/dev.tsv` and `data/eval/gold-v2/dev.tsv` only. The rule
is code, not convention: evaluation accepts exactly those two paths (compared as full paths, so a relative spelling or a
dotted detour cannot slip past), a file at a development path must announce a development split in its header, and
training sources must lie below the `data` folder of the repository and are checked against a deny-list that refuses
held-out sets, evaluation folders and also the development files (training on them would invalidate the selection).
The training check works on the canonical path: it is made absolute (a rooted source path or a `..` detour that leaves
the data folder is refused) and symbolic links and junctions are resolved with
`FileSystemInfo.ResolveLinkTarget` (a safe-looking name that points at held-out data is refused); on .NET Framework,
which only compiles this file for the tests, links are not resolved. The held-out names (the open and gold test
splits, the shape probe, the challenge set and the external treebank) are spelled in `DevSets.cs` only, and a test scans
every other trainer source for them, so the development and training commands have no code path that opens such a file;
the one command that reads them is described in _Held-Out Evaluation_. The selection protocol was written into
`RUN-LOG.md` before the first experiment: the primary criterion is
the pooled harmful errors per 1,000 noun-verb decisions, an option must stay within 0.30 accuracy points of the best, ties
(paired bootstrap intervals containing zero) go to the smaller model and then to fewer sources, and a noise floor was
measured with two more seeds. The settings were chosen by coordinate descent (no interactions), which is a stated
limitation, as are the small size of the gold development set (2,025 targets) and the fact that both development sets are
labeled by language models.

### Confidence Thresholds

A consumer who wants to avoid confident noun-verb mistakes needs a threshold below which it abstains. The `thresholds`
command measures that on **development data only**, with a rule that is written in the protocol section of `RUN-LOG.md`
before any curve existed.

- **Decisions and rates.** A decision is a token whose gold label is a noun or a verb and for which both were candidates
  (the existing `Outcome.NounVerb`). It is answered at a threshold t when the confidence of the chosen tag is at least
  t, and
  a harmful error is an answered decision with the other one of the two tags. The curve gives, for every threshold of the
  grid, the answered decisions, the coverage, the harmful errors, the harmful errors per 1,000 answered and per 1,000
  decisions (the 0.45 row of the last column is the figure of the freeze table of the run log: 94, 9 and 103 harmful
  errors on open dev, gold v2 dev and pooled). The reliability table groups the decisions by confidence.
- **Rule `nv-pooled-dev-v1`.** On the pooled development decisions the first grid threshold with at most 5.0 harmful
  errors per 1,000 answered and a coverage of at least 60% is the `strict` point, the first with at most 10.0 and at
  least 85%
  is the `balanced` point. If no grid threshold qualifies the point is recorded as none; neither target nor grid is relaxed.
- **Output.** `docs/evaluation/operating-points.json` (the artifact read by `evaluate`: rule id, model image hash,
  temperature, development file hashes, grid, points with their pooled development figures) and
  `docs/evaluation/confidence-thresholds.md` (curves of both development sets and of the pooled set, reliability table,
  rule, points with bootstrap intervals over documents). Both are written by the command and committed; the command
  refuses to write when the 0.45 row does not reproduce the frozen figures or when the embedded weights are not the model
  of `training.json`.
- **Limitation.** A threshold chosen on development data is optimistic, and the gold v2 development set has few harmful
  errors; the held-out run reports what the thresholds do on data that played no part in choosing them.

### Held-Out Evaluation

The held-out sets (open test split, gold v2 test split, shape probe, challenge set and the external treebank, which is
fetched into a git-ignored cache) are evaluated **once**, by the `evaluate` command, for the frozen model. The order
"selection first, test once" is enforced by code:

- **The gate.** `evaluate` loads `operating-points.json` through `OperatingPointsFile.Load`, which validates it
  (`SelectedOperatingPoints.Validate`): schema version, rule id, the model image hash must equal the hash of the embedded
  weights and the `imageSha256` of `training.json`, the development file hashes must equal `data/MANIFEST.tsv`, and every
  threshold must be on the grid. The held-out resolver and every held-out reader take the validated object as a required
  parameter, so no held-out file can be opened without it. `evaluate` has no weights, data path or threshold option.
- **Verification before parsing.** `HeldOutReader` compares the SHA-256 of each repository file with
  `data/MANIFEST.tsv` and
  checks its header (the open, gold and challenge files announce their split or set; the probe has its column row); the
  treebank source file is compared with its pin in `tools/data/pins`, and the mapped table is read from the cache that
  the fetch script of the data tools writes. A mismatch is a data error (exit code 2) before any line is parsed.
- **Run once.** `evaluate` refuses to start when `docs/evaluation/test-results.json` exists. There is no `--force`: a
  repeat needs that committed file to be deleted, which is visible in the history and must be recorded in the run log.
  The results file is written last, so an aborted run leaves no marker.
- **Pre-flight.** `evaluate --preflight` performs the gate and the verification, parses every input and prints only
  counts (sentences, tokens, documents, rows) next to the documented ones; it prints no tag, prediction or metric and
  writes nothing. If a count differs, both modes stop with exit code 2 (a stop condition of the protocol).
- **Per-token dump.** `--dump-dir DIR` receives one TSV per set with the gold label, the chosen tag, the confidence
  and the
  17 probabilities of every token (for the comparison with other taggers outside the repository). The folder must lie
  outside the repository, links resolved, because the treebank words are third-party text.
- **Scoring.** The embedded model is created with all 16 alternatives (the candidates of a token decide what a
  decision is)
  and every set is judged with `Metrics.Judge`, exactly as on development data. Gold v2 reports its `to` and `not`
  targets separately and a subset whose labelers agreed fully; the probe is reported per shape; the challenge set per
  sentence and never merged into a headline number. The treebank is judged once and reported in two views: the tags after
  the convention mapping of `data/CONVENTIONS.md` (primary) and the raw tags. The throughput of the default tagger is
  measured separately (one warm-up pass, then the median of three passes over the open test sentences).
- **Statistics.** Each set and view gets accuracy, ambiguous-token accuracy, noun-verb confusion, harmful errors, calibration
  error, log-likelihood and per-tag precision and recall, with 95% percentile intervals over documents (rows or sentences
  where a set has none), and each operating point gets its coverage, its harmful rate per 1,000 answered with an
  interval and
  the paired difference of the harmful errors per 1,000 decisions against 0.45 on the same decisions.
- **Stop conditions.** The protocol lists the conditions that declare a model or data defect (count mismatch, gold outside
  the candidates for more than 1% of a set, open test accuracy more than 2.0 points below development with an interval that
  excludes it, a point whose harmful rate on the open test split is more than twice its development rate, more than 5%
  harmful errors on the probe, calibration error above 3% on the open test split). `evaluate` evaluates them and the
  report names every one that fired; the exit code is then 1 and the response is to stop and report, not to tune.
- **Reports.** `docs/evaluation/test-results.md` and `.json` are generated, deterministic and committed as produced; the
  treebank appears only as aggregate figures and tag-level confusion.

### Selection of the Shipped Model

The committed `training.json` (`RUN-LOG.md` has every run and the analysis):

| Setting | Value | Decided by |
| --- | --- | --- |
| Sources | open x2, MASC, silver (probability at least 80%) | +0.57 points over open alone |
| `disputed` tokens | trained, not masked | accuracy +0.2 points, harmful errors tied |
| Restriction, history | mode B (`History`), predicted history | fully restricted training (C) failed the floor |
| Epochs | 6 | tied with 10 epochs, fewest within 0.05 points of the best |
| Hash bits | 19 | 18 was borderline, 19 is tied with 22 |
| Pruning | minimum count 1, magnitude threshold 8 units | largest tied threshold; the next is not tied |
| Quantization | `quantScale` 8 (scale 1/8) | tied with the unpruned scale 16; scale 4 is not tied |
| Temperature | 6.727 | minimum log-likelihood on open development data |

The result is a file of 119,632 rows and 290,862 weights, 1,111,898 bytes uncompressed and 492,971 bytes compressed;
training takes about 7 to 14 seconds on one thread (6,285,552 steps), and the check about 15 seconds. Development
results through the real decoder: open development 96.07% accuracy (95.28% on lexicon-ambiguous tokens), gold
development 95.34% (without the `to` and `not` targets, which follow another convention and are reported
separately), harmful errors 20.2 per 1,000 noun-verb decisions pooled, calibration error 0.35% (open) and 2.37% (gold).
These are development figures; they say nothing about held-out data.

### Glossary Decision

The later glossary mode (`TokenConstraint.Prefer`) needs to know whether the model should learn how far to trust a
glossary. The question was answered by an experiment on development data with thresholds fixed beforehand: a model M1
with trained "glossary says X" features (trained with simulated hints on 30% of the lexicon-ambiguous tokens, 10% of
them wrong) against the shipped model M0 with a score boost on type-level simulated glossaries. M1 lost more accuracy
without a glossary than the rule allows and did not beat the boost on covered tokens or on the neighbors of
covered tokens. (The eligibility of a training token for a hint uses the allowed tags of the token as written, the same
mask as the runtime decoder, so a capitalized token also allows PROPN and X; a first run that lower-cased the token was
corrected and repeated, with the same decision.) **Decision: boost only.** The feature set stays at version 1 and the
library's extractor and decoder are
unchanged. The measured boost curve was the starting point for calibrating the boosts: up to about two temperatures
(a log-odds shift of 2) give most of the benefit of right entries and cost at most 0.5 points on wrong entries at 10%
wrong; larger boosts cost more on wrong entries, and eight or more behave like a hard constraint. This curve is not the
final word on the default: the strength that the library ships as `GlossaryOptions.DefaultSoftStrength` was chosen
later, in the evaluation of the real glossary (see _Glossary Evaluation_ and
section E9 of `RUN-LOG.md`), first by a rule that was fixed beforehand and then, after the first run, by a
replacement rule whose reason is recorded as a dated change; the cost of that choice on wrong entries is also recorded
there. The experiment code
(`glossary-probe`, `glossary-experiment`, `GlossaryFeatures`, and the optional `glossary` parameter of the trainer, which
is `null` for the shipped model and does not change its pinned hash) stays in the tool so that the decision can be
reproduced.

### Glossary Evaluation

The glossary that the library ships (see _Glossary Design_) was measured with the command `glossary-eval` before its
defaults were fixed. The evaluation tags the development data through the real `Tagger` with real `Glossary` objects, so
what is measured is what a caller gets. It reads only the two development sets, through `DevSets` and `DataGuard`, and
has no path parameter at all (a test checks that the sources contain no file API and no data file name), so it cannot
open a held-out set. Its protocol, the simulated glossaries and the two decision rules were written into `RUN-LOG.md`
before the evaluation code existed, and the results and decisions are in section E9 of the same file; this design does
not repeat any measured figure.

- **Simulated glossaries**, all reproducible from a seed (`DeterministicRandom`, types sorted before they are shuffled):
  noun compounds taken from runs of gold tags in the open development set (with an at-risk subset whose first word the
  lexicon also lists as a verb), single technical words (the phase 3 eligibility), lemmas of the gold development
  targets for inflection, all-capital acronyms against their lower-case homographs for letter case, the target forms of
  the gold development set for the head effect, and two-word phrasal verbs for the rule for the second word. A
  share of the entries is made wrong by replacing the head tag with another allowed class. A term that cannot be an entry
  (no letter or digit, or split by the tokenizer) is skipped.
- **Systems** compared on identical tokens: no glossary, hard mode and soft mode at each strength of a grid; the
  default soft strength of the library must be a member of the grid (a test checks it).
- **Metrics**: accuracy over all tokens and over categories (every scored token is in exactly one: head right, head wrong,
  non-head, neighbor, uncovered), harmful errors as in the model selection but with the baseline's decision count as
  denominator, and confident noun-verb swaps counted even when a hard constraint leaves one candidate (so the cost of a
  wrong hard entry is visible), each with a paired bootstrap by document. A parity check proves that the real glossary
  path gives the tags of the hand-built constraint path of the phase 3 probe, and an invariant check proves that the tokens
  before the first match of a sentence are unchanged.
- **Decision rules**, implemented as functions of the result tables (`ChooseSoftStrength`, `ChooseNonHeadRule`) and
  tested on synthetic tables: `ChooseSoftStrength` is the rule that fixes the default soft strength of the library (the
  largest strength of the grid whose paired accuracy interval in the scenario AB at full coverage with 30% wrong entries
  does not lie below zero and whose confident swaps do not rise), which replaced the first-run rule after the first run
  (`RUN-LOG.md` records the reason as a dated change); a test applies it to the recorded cells and requires the shipped
  default. The first-run rule (`ChooseFirstRunSoftStrength`, the strength chosen among the admissible strengths of one
  pre-registered scenario) is kept and printed as the historical decision, so that the first run stays reproducible. The
  particle rule for the second word of a two-word verb
  term with the head first was adopted because its bar was met; the shipped scope of that rule is the measured shape
  (two-token verb terms, head first), and other shapes were not measured.

### Error Handling

Invalid configuration (an unknown member, an out-of-range value, no source, a source with weight above 16, a corpus
line with an unknown tag) throws `InvalidDataException` with the setting or the file and line, and the command line
turns it into exit code 2 with the message. A held-out or development file given as a training source, and an
evaluation input that is not a development file, throw `InvalidOperationException` before any file is opened (so does a
training source that lies outside the `data` folder after resolution, and a link cycle). A gold development row with a
negative, non-numeric or out-of-sentence target index throws `InvalidDataException`. A held-out gate failure (no
selection artifact, an artifact for another model, rule or development file, a held-out file that differs from its recorded
hash or announces another set, a missing or unpinned treebank cache, a malformed probe or treebank row, a count that
differs from the documented one) throws `InvalidDataException`; an existing results file and a dump folder inside the
repository throw `InvalidOperationException`; all of these are exit code 2 and none of them opens or evaluates a held-out
file further. A weights
file that is not valid for this build (damaged, or built for another feature set) makes the check report `Invalid`. A
stale or missing committed file is reported with exit code 1 and the command that regenerates it. An integer sum of the
averaging that exceeds its range (`checked` arithmetic, practically unreachable: the product of epochs, repeats, tokens
and scale would have to exceed 2^63) throws `InvalidOperationException` with that formula in the message. Nothing is caught
inside the engine; `Program.cs` is the only place that maps exceptions to exit codes.

### Dependencies

- **FeatureExtractor**, **DecoderWorkspace** and **Lexicon** (the same extraction and the same `AllowedTags` rules as at
  run time), **PerceptronModel** (`Load`, `ToUncompressedImage`), **PerceptronModelWriter**, **BinaryFormat**
  (header patching for the temperature), **Upos**; **Tagger**, **TagDecoder** and **TokenConstraint** for the evaluation
  and the glossary experiment through the real decoder; **Glossary** (with its builder, entry, options and match types)
  for the glossary evaluation through the real tagger. All of these are internal and reached through
  `InternalsVisibleTo` (the assembly name `DemaConsulting.PartOfSpeech.Trainer` is reserved there).
- The base class library and, for `training.json`, `operating-points.json` and `test-results.json`, System.Text.Json
  (net10.0 only; the engine does not use it).
- The committed corpora `data/open/train.tsv`, `data/masc/masc.tsv` and `data/silver/silver.tsv`, and the two
  development files; for `evaluate` the held-out files of `data/`, `data/MANIFEST.tsv`, and the pinned local cache of the
  external treebank that the data tools fetch (the tool itself uses no network).

### Callers

The developer (the commands above), `build.ps1` and the CI build (`train --check`), and the test project, which compiles
the engine files and calls them directly. Nothing in the library calls this unit. `build.ps1` and CI never run `thresholds`
or `evaluate`, so CI never reads a held-out file through the evaluation code.

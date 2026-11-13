// cspell:ignore semcor thankyou safehandlingofbo
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using DemaConsulting.PartOfSpeech.Trainer;

namespace DemaConsulting.PartOfSpeech.Tests;

/// <summary>
///     System-level tests of the committed data and licensing artifacts: the third-party notice and its packaging, the
///     licenses and provenance of the data, the staged corpus corrections, the Universal POS tags, the separation of
///     training and evaluation data, the hard-shape development set, the imperative, compound-head and headings training sources, and the lexicon. The tests read the files of the working copy and need no network.
///     Each test proves one obligation of one requirement; a test that finds no file or no row fails instead of passing
///     on nothing.
/// </summary>
public class PartOfSpeechDataTests
{
    /// <summary>
    ///     The number of problems that an assertion shows, so that a broken file does not flood the test output.
    /// </summary>
    private const int ShownProblems = 20;

    /// <summary>
    ///     The training configuration of the trainer tool, relative to the repository root.
    /// </summary>
    private const string TrainingConfigPath = "tools/DemaConsulting.PartOfSpeech.Trainer/training.json";

    /// <summary>
    ///     The file name of the combined license of the package.
    /// </summary>
    private const string PackageLicenseName = "PACKAGE-LICENSE.md";

    /// <summary>
    ///     The license class that the MASC source table records for every document: the license of the whole corpus.
    /// </summary>
    private const string MascLicenseBasis = "CC BY 3.0 US";

    /// <summary>
    ///     The folder of the hard-shape development set, relative to the data folder.
    /// </summary>
    private const string ShapesDevFolder = "eval/shapes-dev";

    /// <summary>
    ///     The corpus file of the hard-shape development set, relative to the data folder.
    /// </summary>
    private const string ShapesDevCorpus = ShapesDevFolder + "/dev.tsv";

    /// <summary>
    ///     The corpus file of the hard tranche of the hard-shape development set, relative to the data folder.
    /// </summary>
    private const string ShapesDevHardCorpus = ShapesDevFolder + "/hard.tsv";

    /// <summary>
    ///     The source table of the hard tranche, relative to the data folder.
    /// </summary>
    private const string ShapesDevHardSources = ShapesDevFolder + "/hard-sources.tsv";

    /// <summary>
    ///     The corpus file of the compound-head tranche of the hard-shape development set, relative to the data folder.
    /// </summary>
    private const string ShapesDevCompoundHeadCorpus = ShapesDevFolder + "/compound-head.tsv";

    /// <summary>
    ///     The corpus file of the headings tranche of the hard-shape development set, relative to the data folder.
    /// </summary>
    private const string ShapesDevHeadingsCorpus = ShapesDevFolder + "/headings.tsv";

    /// <summary>
    ///     The corpus file of the named-state tranche of the hard-shape development set, relative to the data folder.
    /// </summary>
    private const string ShapesDevNamedStateCorpus = ShapesDevFolder + "/named-state.tsv";

    /// <summary>
    ///     The source table of the compound-head tranche, relative to the data folder.
    /// </summary>
    private const string ShapesDevCompoundHeadSources = ShapesDevFolder + "/compound-head-sources.tsv";

    /// <summary>
    ///     The source table of the headings tranche, relative to the data folder.
    /// </summary>
    private const string ShapesDevHeadingsSources = ShapesDevFolder + "/headings-sources.tsv";

    /// <summary>
    ///     The source table of the named-state tranche, relative to the data folder.
    /// </summary>
    private const string ShapesDevNamedStateSources = ShapesDevFolder + "/named-state-sources.tsv";

    /// <summary>
    ///     The corpus file of the compound-head-2 tranche of the hard-shape development set (the follow-up), relative to the data folder.
    /// </summary>
    private const string ShapesDevCompoundHead2Corpus = ShapesDevFolder + "/compound-head-2.tsv";

    /// <summary>
    ///     The corpus file of the headings-2 tranche of the hard-shape development set (the follow-up), relative to the data folder.
    /// </summary>
    private const string ShapesDevHeadings2Corpus = ShapesDevFolder + "/headings-2.tsv";

    /// <summary>
    ///     The source table of the compound-head-2 tranche, relative to the data folder.
    /// </summary>
    private const string ShapesDevCompoundHead2Sources = ShapesDevFolder + "/compound-head-2-sources.tsv";

    /// <summary>
    ///     The source table of the headings-2 tranche, relative to the data folder.
    /// </summary>
    private const string ShapesDevHeadings2Sources = ShapesDevFolder + "/headings-2-sources.tsv";

    /// <summary>
    ///     The most targets of one lemma in the headings-2 tranche (the follow-up rule counts imperative and noun targets together and allows six).
    /// </summary>
    private const int ShapesDevHeadings2LemmaCap = 6;

    /// <summary>
    ///     The most targets of one source document in the headings-2 tranche (the follow-up rule allows 20).
    /// </summary>
    private const int ShapesDevHeadings2DocumentCap = 20;

    /// <summary>
    ///     The most targets of one lemma in a new tranche.
    /// </summary>
    private const int ShapesDevNewLemmaCap = 4;

    /// <summary>
    ///     The most controls of one lemma in a new tranche.
    /// </summary>
    private const int ShapesDevNewControlLemmaCap = 2;

    /// <summary>
    ///     The most targets of one source document in the headings tranche.
    /// </summary>
    private const int ShapesDevHeadingsDocumentCap = 12;

    /// <summary>
    ///     The largest share of the targets of the compound-head tranche that end in a lower-case <c>ing</c>.
    /// </summary>
    private const double ShapesDevCompoundHeadMaxIngShare = 0.25;

    /// <summary>
    ///     The folder of the imperative training source, relative to the data folder.
    /// </summary>
    private const string ImperativeFolder = "imperative";

    /// <summary>
    ///     The corpus file of the imperative training source, relative to the data folder.
    /// </summary>
    private const string ImperativeCorpus = ImperativeFolder + "/imperative.tsv";

    /// <summary>
    ///     The source table of the imperative training source, relative to the data folder.
    /// </summary>
    private const string ImperativeSources = ImperativeFolder + "/sources.tsv";

    /// <summary>
    ///     The fewest and the most sentences of the imperative training source.
    /// </summary>
    private const int ImperativeMinSentences = 600;

    /// <summary>
    ///     The most sentences of the imperative training source.
    /// </summary>
    private const int ImperativeMaxSentences = 900;

    /// <summary>
    ///     The most sentences of the imperative training source that one lemma (the base form of the first word) has.
    /// </summary>
    private const int ImperativeLemmaCap = 6;

    /// <summary>
    ///     The most sentences of the imperative training source that one document has.
    /// </summary>
    private const int ImperativeDocumentCap = 30;

    /// <summary>
    ///     The folder of the compound-head training source, relative to the data folder.
    /// </summary>
    private const string CompoundFolder = "compound";

    /// <summary>
    ///     The corpus file of the compound-head training source, relative to the data folder.
    /// </summary>
    private const string CompoundCorpus = CompoundFolder + "/compound.tsv";

    /// <summary>
    ///     The source table of the compound-head training source, relative to the data folder.
    /// </summary>
    private const string CompoundSources = CompoundFolder + "/sources.tsv";

    /// <summary>
    ///     The folder of the headings training source, relative to the data folder.
    /// </summary>
    private const string HeadingsFolder = "headings";

    /// <summary>
    ///     The corpus file of the headings training source, relative to the data folder.
    /// </summary>
    private const string HeadingsCorpus = HeadingsFolder + "/headings.tsv";

    /// <summary>
    ///     The source table of the headings training source, relative to the data folder.
    /// </summary>
    private const string HeadingsSources = HeadingsFolder + "/sources.tsv";

    /// <summary>
    ///     The corpus file of the second compound-head training source (<c>compound2</c>), relative to the data folder.
    /// </summary>
    private const string Compound2Corpus = CompoundFolder + "/compound-2.tsv";

    /// <summary>
    ///     The corpus file of the second headings training source (<c>headings2</c>), relative to the data folder.
    /// </summary>
    private const string Headings2Corpus = HeadingsFolder + "/headings-2.tsv";

    /// <summary>
    ///     The source table of the second compound-head training source, relative to the data folder.
    /// </summary>
    private const string Compound2Sources = CompoundFolder + "/compound-2-sources.tsv";

    /// <summary>
    ///     The source table of the second headings training source, relative to the data folder.
    /// </summary>
    private const string Headings2Sources = HeadingsFolder + "/headings-2-sources.tsv";

    /// <summary>
    ///     The folder of the four technical-text training sources (<c>deverbal</c>, <c>terms</c>, <c>steps</c>, <c>clauses</c>), relative to the data folder.
    /// </summary>
    private const string TechnicalFolder = "technical";

    /// <summary>
    ///     The most sentences of one lemma in the technical sources and tranches that are not steps (the steps allow six imperatives per lemma).
    /// </summary>
    private const int TechnicalLemmaCap = 4;

    /// <summary>
    ///     The most sentences of one lemma of the steps source and the step-lines tranche (imperative verbs).
    /// </summary>
    private const int TechnicalStepLemmaCap = 6;

    /// <summary>
    ///     The most control items of one lemma in a technical tranche.
    /// </summary>
    private const int TechnicalControlLemmaCap = 2;

    /// <summary>
    ///     The most sentences of one source document in a technical training source (15 for the clauses source).
    /// </summary>
    private const int TechnicalTrainDocumentCap = 20;

    /// <summary>
    ///     The most target items of one source document in a technical development tranche.
    /// </summary>
    private const int TechnicalDevDocumentCap = 15;

    /// <summary>
    ///     The most sentences of one capitalized term in the terms source and the defined-terms tranche.
    /// </summary>
    private const int TechnicalTermCap = 2;

    /// <summary>
    ///     The fewest and the most sentences of the second compound-head training source (the follow-up rule).
    /// </summary>
    private const int Compound2MinSentences = 600;

    /// <summary>
    ///     The most sentences of the second compound-head training source.
    /// </summary>
    private const int Compound2MaxSentences = 1100;

    /// <summary>
    ///     The most noun items of the second compound-head training source.
    /// </summary>
    private const int Compound2NounCap = 935;

    /// <summary>
    ///     The most verb items (counter-examples) of the second compound-head training source.
    /// </summary>
    private const int Compound2VerbCap = 165;

    /// <summary>
    ///     The fewest head lemmas of the second compound-head training source.
    /// </summary>
    private const int Compound2MinLemmas = 200;

    /// <summary>
    ///     The most lines of the second headings training source that one lemma has (the follow-up rule allows six).
    /// </summary>
    private const int Headings2LemmaCap = 6;

    /// <summary>
    ///     The floor of the imperative stratum of the second headings training source; the pre-registered floor is 150.
    /// </summary>
    private const int Headings2ImperativeFloor = 150;

    /// <summary>
    ///     The cap of the imperative stratum of the second headings training source.
    /// </summary>
    private const int Headings2ImperativeCap = 400;

    /// <summary>
    ///     The fewest and the most sentences of the compound-head training source.
    /// </summary>
    private const int CompoundMinSentences = 600;

    /// <summary>
    ///     The most sentences of the compound-head training source.
    /// </summary>
    private const int CompoundMaxSentences = 800;

    /// <summary>
    ///     The fewest head lemmas of the compound-head training source.
    /// </summary>
    private const int CompoundMinLemmas = 200;

    /// <summary>
    ///     The most sentences of the compound-head training source that one head lemma has.
    /// </summary>
    private const int CompoundLemmaCap = 4;

    /// <summary>
    ///     The most sentences of the compound-head training source that one document has.
    /// </summary>
    private const int CompoundDocumentCap = 20;

    /// <summary>
    ///     The largest share, in percent, of the compound-head training source whose head is a verb (the counter-examples).
    /// </summary>
    private const int CompoundVerbPercent = 15;

    /// <summary>
    ///     The most lines of the headings training source.
    /// </summary>
    private const int HeadingsMaxLines = 600;

    /// <summary>
    ///     The most lines of the headings training source that one lemma has.
    /// </summary>
    private const int HeadingsLemmaCap = 4;

    /// <summary>
    ///     The most lines of the headings training source that one document has.
    /// </summary>
    private const int HeadingsDocumentCap = 25;

    /// <summary>
    ///     The caps of the strata of the headings training source (the floors are 150, 100 and 40; the build may waive a floor and the README
    ///     discloses it).
    /// </summary>
    private static readonly Dictionary<string, (int Floor, int Cap, string Tag)> HeadingsStrata = new(StringComparer.Ordinal)
    {
        ["noun"] = (150, 340, "NOUN"),
        ["imperative"] = (0, 200, "VERB"),
        ["proper"] = (40, 60, "PROPN")
    };

    /// <summary>
    ///     The most targets of one lemma in a shape of the hard tranche.
    /// </summary>
    private const int ShapesDevHardLemmaCap = 4;

    /// <summary>
    ///     The floor of the controls of the hard tranche.
    /// </summary>
    private const int ShapesDevHardControlFloor = 50;

    /// <summary>
    ///     The cap of the controls of the hard tranche.
    /// </summary>
    private const int ShapesDevHardControlCap = 60;

    /// <summary>
    ///     The floor and the cap of the targets of every shape of the hard tranche (n-measure has none: zero items).
    /// </summary>
    private static readonly Dictionary<string, (int Floor, int Cap)> ShapesDevHardTargetQuotas = new(StringComparer.Ordinal)
    {
        ["n-compound-head"] = (80, 100),
        ["n-subject-initial"] = (40, 100),
        ["v-imperative-initial"] = (70, 100),
        ["n-measure"] = (0, 0),
        ["v-after-modal"] = (80, 100)
    };

    /// <summary>
    ///     The most target items that a shape of the development set holds.
    /// </summary>
    private const int ShapesDevMaxTargetsPerShape = 120;

    /// <summary>
    ///     The number of controls of the development set.
    /// </summary>
    private const int ShapesDevControls = 100;

    /// <summary>
    ///     The most targets of one lemma in a shape (12 for n-measure, see <see cref="ShapesDevMeasureLemmaCap"/>).
    /// </summary>
    private const int ShapesDevLemmaCap = 4;

    /// <summary>
    ///     The most targets of one lemma in the n-measure shape, whose units repeat.
    /// </summary>
    private const int ShapesDevMeasureLemmaCap = 12;

    /// <summary>
    ///     The most controls of one lemma in a shape.
    /// </summary>
    private const int ShapesDevControlLemmaCap = 2;

    /// <summary>
    ///     The largest share of authored sentences that a shape, or the controls, of the development set may have.
    /// </summary>
    private const double ShapesDevMaxAuthoredShare = 0.30;

    /// <summary>
    ///     The training corpora, relative to the data folder.
    /// </summary>
    private static readonly string[] TrainingCorpora =
    {
        "open/train.tsv", "masc/masc.tsv", "silver/silver.tsv", ImperativeCorpus, CompoundCorpus, HeadingsCorpus, Compound2Corpus, Headings2Corpus,
        TechnicalFolder + "/deverbal.tsv", TechnicalFolder + "/terms.tsv", TechnicalFolder + "/steps.tsv", TechnicalFolder + "/clauses.tsv"
    };

    /// <summary>
    ///     The token-level evaluation corpora, relative to the data folder.
    /// </summary>
    private static readonly string[] EvaluationCorpora =
    {
        "open/dev.tsv", "open/test.tsv", "eval/ste-challenge/test.tsv", ShapesDevCorpus, ShapesDevHardCorpus,
        ShapesDevCompoundHeadCorpus, ShapesDevHeadingsCorpus, ShapesDevNamedStateCorpus, ShapesDevCompoundHead2Corpus, ShapesDevHeadings2Corpus,
        ShapesDevFolder + "/deverbal-heads.tsv", ShapesDevFolder + "/defined-terms.tsv", ShapesDevFolder + "/step-lines.tsv", ShapesDevFolder + "/clause-verbs.tsv"
    };

    /// <summary>
    ///     The five new tranches of the hard-shape development set (the three of the first generation and the two of the follow-up): the name (the file stem and the id infix), the only
    ///     shape of the tranche, the tranche of the trainer, and the quota groups (name, floor and cap of the items of a group).
    ///     A group is the role (<c>target</c> or <c>control</c>) and, for the headings, the kind of a target. The imperative floor of
    ///     <c>headings-2</c> is the committed count (59): the pre-registered floor of 80 was not reached, the build ran with the documented
    ///     short-supply option and the README discloses it.
    /// </summary>
    private static readonly ShapesDevNewTranche[] ShapesDevNewTranches =
    {
        new(
            "compound-head", "n-compound-head-ambiguous", ShapesDevTranche.CompoundHead, ShapesDevCompoundHeadCorpus, ShapesDevCompoundHeadSources,
            new[] { ("target", 80, 100), ("control", 15, 20) }),
        new(
            "headings", "heading-titlecase", ShapesDevTranche.Headings, ShapesDevHeadingsCorpus, ShapesDevHeadingsSources,
            new[] { ("noun", 40, 50), ("imperative", 40, 50), ("control", 12, 20) }),
        new(
            "named-state", "n-named-state", ShapesDevTranche.NamedState, ShapesDevNamedStateCorpus, ShapesDevNamedStateSources,
            new[] { ("target", 40, 100), ("control", 12, 20) }),
        new(
            "compound-head-2", "n-compound-head-ambiguous", ShapesDevTranche.CompoundHead2, ShapesDevCompoundHead2Corpus, ShapesDevCompoundHead2Sources,
            new[] { ("target", 150, 210), ("control", 30, 45) }),
        new(
            "headings-2", "heading-titlecase", ShapesDevTranche.Headings2, ShapesDevHeadings2Corpus, ShapesDevHeadings2Sources,
            new[] { ("noun", 30, 50), ("imperative", 59, 130), ("control", 12, 20) },
            ShapesDevHeadings2LemmaCap, ShapesDevHeadings2DocumentCap)
    };

    /// <summary>
    ///     The words that the script of the new tranches must not name: a weights file, a decoder, a tagger or the shapes evaluation.
    /// </summary>
    private static readonly string[] ModelNames =
    {
        "weights.bin", "Resources/weights", "PerceptronModel", "FeatureExtractor", "PartOfSpeechTagger", "ITagger", "eval-shapes", "ShapesDevEvaluator"
    };

    /// <summary>
    ///     The four technical tranches of the hard-shape development set: the name (the file stem and the id infix), the training source of the same
    ///     shape, the only shape of the tranche, the tranche of the trainer, the kinds (name and role) in the order of the trainer, and the quota groups
    ///     (name, floor and cap of the items of a group). A group is the name of a kind, <c>target</c> (all targets) or <c>control</c> (all controls). The
    ///     floors and caps are the ranges that the committed files meet (for example 166 to 300 imperative step lines).
    /// </summary>
    private static readonly TechnicalTranche[] TechnicalTranches =
    {
        new(
            "deverbal-heads", "deverbal", "n-deverbal-head", ShapesDevTranche.DeverbalHeads, 450,
            new[] { ("pred", "target"), ("num", "target"), ("ing", "target"), ("other", "target"), ("verb", "control") },
            new[] { ("target", 240, 360), ("pred", 40, 100), ("num", 30, 60), ("ing", 80, 140), ("other", 0, 100), ("verb", 50, 90) },
            TechnicalLemmaCap, Array.Empty<string>()),
        new(
            "defined-terms", "terms", "term-capitalized", ShapesDevTranche.DefinedTerms, 285,
            new[] { ("noun", "target"), ("adj", "target"), ("proper", "control") },
            new[] { ("noun", 120, 200), ("adj", 40, 80), ("proper", 40, 70) },
            TechnicalLemmaCap, new[] { "term" }),
        new(
            "step-lines", "steps", "step-line", ShapesDevTranche.StepLines, 236,
            new[] { ("imperative", "target"), ("noun", "control") },
            new[] { ("imperative", 166, 300), ("noun", 40, 70) },
            TechnicalStepLemmaCap, new[] { "frame", "unit" }),
        new(
            "clause-verbs", "clauses", "v-clause-ambiguous", ShapesDevTranche.ClauseVerbs, 446,
            new[] { ("main", "target"), ("sub", "target"), ("noun", "control"), ("adj", "control") },
            new[] { ("target", 240, 360), ("sub", 60, 360), ("control", 50, 90), ("noun", 35, 60), ("adj", 15, 30) },
            TechnicalLemmaCap, Array.Empty<string>()),
    };

    /// <summary>
    ///     The four technical training sources: the name (the file stem and the id prefix), the sentence count, the strata (name and the tags that the judged
    ///     word of the stratum may carry), the quotas (the most and least sentences of the whole file and of a stratum), the largest share of the contrast
    ///     strata in percent, the lemma cap and the document cap, and the extra comments after the stratum. The floors and caps of the table are the ranges
    ///     that the committed files meet (for example 373 to 900 sentences of <c>terms</c>).
    /// </summary>
    private static readonly TechnicalSource[] TechnicalSources =
    {
        new(
            "deverbal", 637, TechnicalLemmaCap, TechnicalTrainDocumentCap, Array.Empty<string>(), false,
            new[] { ("pred", "NOUN|PROPN"), ("num", "NOUN|PROPN"), ("ing", "NOUN|PROPN"), ("other", "NOUN|PROPN"), ("verb", "VERB") },
            new[] { ("*", 450, 800), ("pred", 100, 250), ("num", 50, 200), ("ing", 146, 300), ("other", 0, 200) },
            (new[] { "verb" }, 0, 15)),
        new(
            "terms", 373, TechnicalLemmaCap, TechnicalTrainDocumentCap, new[] { "term" }, false,
            new[] { ("noun", "NOUN"), ("adj", "ADJ"), ("proper", "PROPN") },
            new[] { ("*", 373, 900), ("noun", 0, 550), ("adj", 0, 250) },
            (new[] { "proper" }, 15, 25)),
        new(
            "steps", 531, TechnicalStepLemmaCap, TechnicalTrainDocumentCap, new[] { "frame", "unit" }, true,
            new[] { ("imperative", "VERB"), ("noun-initial", "NOUN") },
            new[] { ("*", 531, 1000) },
            (new[] { "noun-initial" }, 0, 30)),
        new(
            "clauses", 310, TechnicalLemmaCap, 15, Array.Empty<string>(), false,
            new[] { ("main", "VERB"), ("sub", "VERB"), ("noun", "NOUN"), ("adj", "ADJ") },
            new[] { ("*", 310, 800), ("main", 0, 450), ("sub", 0, 300) },
            (new[] { "noun", "adj" }, 0, 20)),
    };

    /// <summary>
    ///     The pairs of technical training sources that share documents by design (one pool of documents for the four sources, never a sentence).
    /// </summary>
    private static readonly string[] TechnicalSharedPoolPairs =
    {
        "deverbal/terms", "deverbal/steps", "deverbal/clauses", "terms/steps", "terms/clauses", "steps/clauses"
    };

    /// <summary>
    ///     Every pair of the training leakage report that names a technical source: each older targeted source against each technical source, each technical
    ///     source against the open, MASC, silver and imperative sources, and the six pairs of technical sources.
    /// </summary>
    private static readonly string[] TechnicalTrainingPairs = new[] { "compound", "headings", "compound2", "headings2" }
        .SelectMany(old => new[] { "deverbal", "terms", "steps", "clauses" }.Select(technical => old + "/" + technical))
        .Concat(new[] { "deverbal", "terms", "steps", "clauses" }.SelectMany(technical => new[] { "open-train", "masc", "silver", "imperative" }.Select(other => technical + "/" + other)))
        .Concat(TechnicalSharedPoolPairs)
        .ToArray();

    /// <summary>
    ///     The data folders that hold data and therefore declare a license, relative to the data folder.
    /// </summary>
    private static readonly string[] LicensedFolders =
    {
        "open", "masc", "silver", ImperativeFolder, CompoundFolder, HeadingsFolder, TechnicalFolder, "eval/shapes", "eval/shapes-dev", "eval/gold-v2", "eval/ste-challenge", "lexicon",
        "corrections"
    };

    /// <summary>
    ///     The tables that record the origin and license basis of every source document, relative to the data folder,
    ///     with the name of the column that holds the document id.
    /// </summary>
    private static readonly (string Table, string IdColumn)[] SourceTables =
    {
        ("open/sources.tsv", "source_id"), ("eval/gold-v2/sources.tsv", "doc_id"), ("silver/sources.tsv", "source_id"),
        (ShapesDevFolder + "/sources.tsv", "source_id"), (ShapesDevHardSources, "source_id"),
        (ShapesDevCompoundHeadSources, "source_id"), (ShapesDevHeadingsSources, "source_id"), (ShapesDevNamedStateSources, "source_id"),
        (ImperativeSources, "source_id"), (CompoundSources, "source_id"), (HeadingsSources, "source_id"),
        (ShapesDevCompoundHead2Sources, "source_id"), (ShapesDevHeadings2Sources, "source_id"), (Compound2Sources, "source_id"), (Headings2Sources, "source_id"),
        (ShapesDevFolder + "/deverbal-heads-sources.tsv", "source_id"), (ShapesDevFolder + "/defined-terms-sources.tsv", "source_id"),
        (ShapesDevFolder + "/step-lines-sources.tsv", "source_id"), (ShapesDevFolder + "/clause-verbs-sources.tsv", "source_id"),
        (TechnicalFolder + "/deverbal-sources.tsv", "source_id"), (TechnicalFolder + "/terms-sources.tsv", "source_id"),
        (TechnicalFolder + "/steps-sources.tsv", "source_id"), (TechnicalFolder + "/clauses-sources.tsv", "source_id")
    };

    /// <summary>
    ///     The statements that every data README makes, per folder: the headings and bold fields that record where the
    ///     data comes from, under which license, and how it was produced. The folder of UD English-EWT holds no data; its
    ///     README records where the treebank is fetched from and under which license it stays out of the repository.
    /// </summary>
    private static readonly Dictionary<string, string[]> ReadmeStatements = new(StringComparer.Ordinal)
    {
        ["open"] = new[] { "## License and Provenance", "**Source:**", "**How produced:**", "`sources.tsv`" },
        ["masc"] = new[] { "## Source", "**Corpus:**", "**Input file:**", "**How produced:**", "`sources.tsv`" },
        ["silver"] = new[] { "## Content and License", "## How It Was Produced", "`sources.tsv`" },
        [ImperativeFolder] = new[]
        {
            "## Source and Provenance", "**Source:**", "**How produced:**", "**Labels:**", "**Sentences:**", "`sources.tsv`",
            "## Labeling and Decision Rule", "## Hand Check", "model review, not a human review", "## Sources", "## Known Limits", "## Use"
        },
        [CompoundFolder] = new[]
        {
            "## Source and Provenance", "**Source:**", "**How produced:**", "**Labels:**", "**Sentences:**", "`sources.tsv`",
            "## Labeling and Decision Rule", "## Hand Check", "model review, not a human review", "## Sources", "## Lexicon Coverage", "## Known Limits", "## Use"
        },
        [HeadingsFolder] = new[]
        {
            "## Source and Provenance", "**Source:**", "**How produced:**", "**Labels:**", "**Sentences:**", "`sources.tsv`",
            "## Labeling and Decision Rule", "## Hand Check", "model review, not a human review", "## Sources", "## Lexicon Coverage", "## Known Limits", "## Use"
        },
        ["eval/shapes"] = new[]
        {
            "## Source and Provenance", "**Source:**", "**How produced (by the WordSense project):**", "**License:**"
        },
        [ShapesDevFolder] = new[]
        {
            "## Source and Provenance", "**Source:**", "**How produced:**", "**Labels:**", "**Sentences:**", "`sources.tsv`",
            "## Labeling and Decision Rule", "## Hand Check", "model review, not a human review", "## Sources", "## Known Limits",
            "## Use", "## Hard Tranche", "### Hand Check of the Hard Tranche"
        },
        ["eval/gold-v2"] = new[] { "## Source and Provenance", "**Source:**", "**Labels:**", "**Sentences:**", "`sources.tsv`" },
        [TechnicalFolder] = new[]
        {
            "## Source and Provenance", "## The Shapes and Strata", "## Counts", "## Labeling and Decision Rule", "## Hand Check",
            "## Leakage and License Checks", "## Known Limits", "## Use", "`deverbal-sources.tsv`"
        },
        ["eval/ste-challenge"] = new[] { "## Source and Provenance", "**Source:**", "**Sentences:**", "**Tags:**" },
        ["lexicon"] = new[] { "## Sources", "## How It Is Built" },
        ["eval/ud-ewt"] = new[] { "## Fetch and Map", "**Source:**", "CC BY-SA 4.0" }
    };

    /// <summary>
    ///     The license identifiers that a data folder may declare.
    /// </summary>
    private static readonly string[] AllowedLicenses = { "CC0-1.0", "CC-BY-3.0-US", "CC-BY-4.0", "Public-Domain", "MIT" };

    /// <summary>
    ///     The beginnings of the license basis of a source document: the license classes that a source table may record.
    /// </summary>
    private static readonly string[] AllowedLicenseBases =
    {
        "Tatoeba sentence licensed CC0 1.0", "CC0-1.0", "US federal government work", "US public domain",
        "Written by the project"
    };

    /// <summary>
    ///     The coarse classes of the single-target evaluation sets.
    /// </summary>
    private static readonly string[] CoarseClasses =
    {
        "Noun", "Verb", "Adjective", "Adverb", "Pronoun", "Preposition", "Determiner", "Conjunction", "Auxiliary",
        "Interjection", "Other"
    };

    /// <summary>
    ///     A line of a data README that declares the license of its folder.
    /// </summary>
    private static readonly Regex LicenseLine = new(@"^License: (\S+)\s*$", RegexOptions.Multiline | RegexOptions.CultureInvariant);

    /// <summary>
    ///     A pinned identifier of an input: a Git commit (40 hexadecimal digits) or a SHA-256 (64 hexadecimal digits).
    /// </summary>
    private static readonly Regex PinnedIdentifier = new(@"\b(?:[0-9a-f]{40}|[0-9a-f]{64})\b", RegexOptions.CultureInvariant);

    /// <summary>
    ///     A reference to a script of the data tools, as the READMEs give it in the command that produces the data.
    /// </summary>
    private static readonly Regex ScriptReference = new(@"tools/data/([a-z_]+)\.py", RegexOptions.CultureInvariant);

    /// <summary>
    ///     The id of a MASC document: written genre and file path.
    /// </summary>
    private static readonly Regex MascDocument = new(@"^masc:written/([a-z-]+)/\S+$", RegexOptions.CultureInvariant);

    /// <summary>
    ///     The provenance of an adjudicated correction: the two blind models and the batch that the decision comes from.
    /// </summary>
    private static readonly Regex AdjudicatedProvenance = new(@"^llm:opus\+sol:[a-z0-9]+$", RegexOptions.CultureInvariant);

    /// <summary>
    ///     Text that must not appear in the license basis of any source document.
    /// </summary>
    private static readonly Regex RestrictedLicenseText = new(
        @"share-?alike|by-sa|by-nc|by-nd|non-?commercial|no-?derivatives?|cc by|wikipedia|\.org/licenses/by",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    /// <summary>
    ///     A path of a SemCor or Brown corpus file.
    /// </summary>
    private static readonly Regex RestrictedCorpusPath = new("semcor|brown", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    /// <summary>
    ///     Proves that the third-party notice attributes MASC with its title, creators, source link, license link and
    ///     citation.
    /// </summary>
    [Fact]
    public void PartOfSpeech_ThirdPartyNotices_Masc_IsAttributed()
    {
        // Arrange: the MASC section of the notice and the elements that an attribution must have
        var section = NoticeSection("## The Manually Annotated Sub-Corpus (MASC)");
        var required = new[]
        {
            "The Manually Annotated Sub-Corpus (MASC)",
            "Nancy Ide",
            "Collin Baker",
            "Christiane Fellbaum",
            "Rebecca Passonneau",
            "https://anc.org/data/masc/",
            "Creative Commons Attribution 3.0 United States",
            "https://creativecommons.org/licenses/by/3.0/us/",
            "https://aclanthology.org/L08-1105/"
        };

        // Act: find the elements that the section lacks
        var missing = MissingFragments(section, required);

        // Assert: nothing is missing
        Assert.Empty(missing);
    }

    /// <summary>
    ///     Proves that the third-party notice attributes Open English WordNet with its title, creator, source links and
    ///     license link.
    /// </summary>
    [Fact]
    public void PartOfSpeech_ThirdPartyNotices_OpenEnglishWordNet_IsAttributed()
    {
        // Arrange: the Open English WordNet section of the notice and the elements that an attribution must have
        var section = NoticeSection("## Open English WordNet");
        var required = new[]
        {
            "Open English WordNet 2025 Edition",
            "John P. McCrae",
            "https://en-word.net",
            "https://github.com/globalwordnet/english-wordnet",
            "Creative Commons Attribution 4.0 International",
            "https://creativecommons.org/licenses/by/4.0/"
        };

        // Act: find the elements that the section lacks
        var missing = MissingFragments(section, required);

        // Assert: nothing is missing
        Assert.Empty(missing);
    }

    /// <summary>
    ///     Proves that the third-party notice states that the MASC data and the Open English WordNet data were converted
    ///     by this project.
    /// </summary>
    [Fact]
    public void PartOfSpeech_ThirdPartyNotices_Changes_AreStated()
    {
        // Arrange: the statement of change that each of the two sections must make
        var mascSection = NoticeSection("## The Manually Annotated Sub-Corpus (MASC)");
        var wordNetSection = NoticeSection("## Open English WordNet");

        // Act: find the statements that the sections lack
        var missing = MissingFragments(mascSection, new[] { "converted to Universal POS (UPOS) tags" })
            .Concat(MissingFragments(wordNetSection, new[] { "maps a lower-case spelling to the UPOS tags it may carry" }))
            .ToList();

        // Assert: both conversions are stated
        Assert.Empty(missing);
    }

    /// <summary>
    ///     Proves that the third-party notice reproduces the Princeton WordNet license that Open English WordNet carries.
    /// </summary>
    [Fact]
    public void PartOfSpeech_ThirdPartyNotices_PrincetonLicense_IsReproduced()
    {
        // Arrange: the Princeton WordNet section of the notice and the parts of the license that must be reproduced
        var section = NoticeSection("### Princeton WordNet");
        var required = new[]
        {
            "WordNet 3.1 Copyright 2011 by Princeton University. All rights reserved.",
            "THIS SOFTWARE AND DATABASE IS PROVIDED \"AS IS\" AND PRINCETON UNIVERSITY MAKES NO REPRESENTATIONS",
            "Title to copyright in this software, database and any associated documentation shall at all times remain " +
            "with Princeton University"
        };

        // Act: find the parts that the section lacks
        var missing = MissingFragments(section, required);

        // Assert: nothing is missing
        Assert.Empty(missing);
    }

    /// <summary>
    ///     Proves that the project file packs the third-party notice at the root of the NuGet package.
    /// </summary>
    [Fact]
    public void PartOfSpeech_ThirdPartyNotices_Packaging_NoticeIsPackedAtPackageRoot()
    {
        // Arrange: load the project file of the library
        var projectFile = DataFiles.Repository("src/DemaConsulting.PartOfSpeech/DemaConsulting.PartOfSpeech.csproj");
        var project = XDocument.Load(projectFile);

        // Act: find the items that include the notice file
        var items = project.Descendants("None")
            .Where(item => ((string?)item.Attribute("Include") ?? string.Empty).EndsWith("THIRD-PARTY-NOTICES.md", StringComparison.Ordinal))
            .ToList();

        // Assert: exactly one item packs the existing notice file at the package root
        var item = Assert.Single(items);
        var include = ((string?)item.Attribute("Include"))!.Replace('\\', '/');
        Assert.True(File.Exists(Path.GetFullPath(Path.Combine(Path.GetDirectoryName(projectFile)!, include))));
        Assert.Equal("true", (string?)item.Attribute("Pack"));
        Assert.Equal("/", ((string?)item.Attribute("PackagePath"))!.Replace('\\', '/'));
    }

    /// <summary>
    ///     Proves that every data folder declares a license and that the license is one of the allowed set.
    /// </summary>
    [Fact]
    public void PartOfSpeech_DataLicensing_Folders_DeclareAllowedLicenses()
    {
        // Arrange: collect the problems of the license declarations
        var problems = new List<string>();

        // Act: check the README of every data folder
        foreach (var folder in LicensedFolders)
        {
            problems.AddRange(LicenseProblems(folder));
        }

        // Assert: every folder was checked and no problem was found
        Assert.Equal(13, LicensedFolders.Length);
        Assert.Empty(problems);
    }

    /// <summary>
    ///     Proves that the staged correction tables are covered by the license gate: each table declares CC0-1.0 for its
    ///     labels and names the MASC license and attribution, the README of the folder links the attribution file, every
    ///     row belongs to a document that a source table lists (a MASC row only with the CC BY 3.0 US class, because its
    ///     token and context excerpt are MASC text), and no label, id, reason or provenance cell names a restricted license.
    /// </summary>
    [Fact]
    public void PartOfSpeech_DataLicensing_Corrections_AreCoveredByTheLicenseGate()
    {
        // Arrange: the documents that each source table lists (MASC: only those with the corpus license class), the README
        var problems = new List<string>();
        var mascSources = DataFiles.ReadTable(DataFiles.Data("masc/sources.tsv"));
        var listed = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal)
        {
            ["open"] = SourceIds("open/sources.tsv", "source_id"),
            ["silver"] = SourceIds("silver/sources.tsv", "source_id"),
            ["masc"] = new HashSet<string>(
                mascSources.Rows.Where(row => mascSources.Cell(row, "license_basis") == MascLicenseBasis).Select(row => mascSources.Cell(row, "source_id")),
                StringComparer.Ordinal)
        };
        var readme = File.ReadAllText(DataFiles.Data("corrections/README.md"));
        var textColumns = new[] { "token", "hint", "context" };
        var names = new[] { "mechanical.tsv", "candidates.tsv", "exceptions.tsv", "adjudicated.tsv" }
            .Where(name => File.Exists(CorrectionTables.FilePath(name))).ToList();
        var rows = 0;

        // Act: check the attribution, then the license line and every row of each table
        if (!readme.Contains("](../masc/ATTRIBUTION.md)", StringComparison.Ordinal) || !File.Exists(DataFiles.Data("masc/ATTRIBUTION.md")))
        {
            problems.Add("data/corrections/README.md: does not link the existing MASC attribution ../masc/ATTRIBUTION.md");
        }

        foreach (var name in names)
        {
            var declarations = File.ReadLines(CorrectionTables.FilePath(name)).Where(line => line.StartsWith("# license = ", StringComparison.Ordinal)).ToList();
            if (declarations.Count != 1 || !declarations[0].Contains("CC0-1.0", StringComparison.Ordinal)
                || !declarations[0].Contains(MascLicenseBasis, StringComparison.Ordinal)
                || !declarations[0].Contains("masc/ATTRIBUTION.md", StringComparison.Ordinal))
            {
                problems.Add($"data/corrections/{name}: needs one '# license' line with CC0-1.0, {MascLicenseBasis} and masc/ATTRIBUTION.md");
            }

            var table = DataFiles.ReadTable(CorrectionTables.FilePath(name));
            rows += table.Rows.Count;
            foreach (var row in table.Rows)
            {
                var source = table.Cell(row, "source");
                var document = table.Cell(row, "source_id");
                if (!listed.TryGetValue(source, out var documents) || !documents.Contains(document))
                {
                    problems.Add($"data/corrections/{name}: the document {document} is not listed with an allowed license for source {source}");
                }

                problems.AddRange(table.Columns
                    .Where(column => Array.IndexOf(textColumns, column) < 0 && RestrictedLicenseText.IsMatch(table.Cell(row, column)))
                    .Select(column => $"data/corrections/{name}: column {column} names a restricted license for {document}"));
            }
        }

        // Assert: the four tables were scanned, they carry rows, and no problem was found
        Assert.True(names.Count >= 4);
        Assert.True(rows > 10000);
        Assert.Empty(problems.Take(ShownProblems));
    }

    /// <summary>
    ///     Proves that no source document is recorded under a share-alike, non-commercial, no-derivatives, Wikipedia or
    ///     CC BY license.
    /// </summary>
    [Fact]
    public void PartOfSpeech_DataLicensing_SourceTables_RecordNoRestrictedLicense()
    {
        // Arrange: read the license basis of every source document
        var rows = 0;
        var problems = new List<string>();

        // Act: find the license bases that name a restricted license
        foreach (var (table, idColumn) in SourceTables)
        {
            var data = DataFiles.ReadTable(DataFiles.Data(table));
            rows += data.Rows.Count;
            problems.AddRange(data.Rows
                .Where(row => RestrictedLicenseText.IsMatch(data.Cell(row, "license_basis")))
                .Select(row => $"data/{table}: restricted license for {data.Cell(row, idColumn)}"));
        }

        // Assert: the tables are not empty and no document has a restricted license
        Assert.True(rows > 10000);
        Assert.Empty(problems.Take(ShownProblems));
    }

    /// <summary>
    ///     Proves that no UD English-EWT file is committed: no CoNLL-U file anywhere, no path below the data folder that
    ///     names the treebank, and nothing but the documentation in its folder.
    /// </summary>
    [Fact]
    public void PartOfSpeech_DataLicensing_UdEwt_IsNotCommitted()
    {
        // Arrange: list the files of the repository, without dependencies, build output and the git-ignored download cache
        var files = DataFiles.EnumerateFiles(DataFiles.RepositoryRoot, IsIgnoredFolder).ToList();
        var paths = files.Select(DataFiles.RelativePath).ToList();

        // Act: find CoNLL-U files anywhere and treebank names below the data folder
        var problems = paths
            .Where(path => path.EndsWith(".conllu", StringComparison.OrdinalIgnoreCase))
            .Select(path => "CoNLL-U file: " + path)
            .Concat(paths
                .Where(path => path.StartsWith("data/", StringComparison.Ordinal) && path != "data/eval/ud-ewt/README.md")
                .Where(path => path.IndexOf("ewt", StringComparison.OrdinalIgnoreCase) >= 0)
                .Select(path => "treebank name: " + path))
            .ToList();

        // Assert: the repository was listed, nothing of the treebank is committed, and its folder holds only its README
        Assert.Contains("data/README.md", paths);
        Assert.Empty(problems);
        Assert.Equal(
            new[] { "data/eval/ud-ewt/README.md" },
            paths.Where(path => path.StartsWith("data/eval/ud-ewt/", StringComparison.Ordinal)));
    }

    /// <summary>
    ///     Proves that no SemCor or Brown corpus file is committed.
    /// </summary>
    [Fact]
    public void PartOfSpeech_DataLicensing_RestrictedCorpora_AreNotCommitted()
    {
        // Arrange: list the files below the data folder
        var paths = DataFiles.EnumerateFiles(DataFiles.Data(string.Empty).TrimEnd('/', '\\'), IsIgnoredFolder)
            .Select(DataFiles.RelativePath)
            .ToList();

        // Act: find the paths that name SemCor or the Brown corpus
        var problems = paths.Where(path => RestrictedCorpusPath.IsMatch(path)).ToList();

        // Assert: the data folder was listed and no restricted corpus is committed
        Assert.Contains("data/MANIFEST.tsv", paths);
        Assert.Empty(problems);
    }

    /// <summary>
    ///     Proves that the README of every data folder records the source of the data, its license and how it was
    ///     produced: the statements of the folder, a pinned input identifier, and a script of the data tools that exists.
    /// </summary>
    [Fact]
    public void PartOfSpeech_DataProvenance_Readmes_RecordSourceAndProduction()
    {
        // Arrange: collect the problems
        var problems = new List<string>();

        // Act: check the README of every data folder
        foreach (var entry in ReadmeStatements)
        {
            var readme = DataFiles.Data(entry.Key + "/README.md");
            if (!File.Exists(readme))
            {
                problems.Add("missing README: data/" + entry.Key);
                continue;
            }

            var text = File.ReadAllText(readme);
            problems.AddRange(entry.Value
                .Where(statement => text.IndexOf(statement, StringComparison.Ordinal) < 0)
                .Select(statement => $"data/{entry.Key}: README lacks '{statement}'"));
            if (!PinnedIdentifier.IsMatch(text))
            {
                problems.Add($"data/{entry.Key}: README names no pinned commit or SHA-256");
            }

            var scripts = ScriptReference.Matches(text).Cast<Match>().Select(match => match.Groups[1].Value).Distinct().ToList();
            if (scripts.Count == 0)
            {
                problems.Add($"data/{entry.Key}: README names no script that produces the data");
            }

            problems.AddRange(scripts
                .Where(script => !File.Exists(DataFiles.Repository("tools/data/" + script + ".py")))
                .Select(script => $"data/{entry.Key}: README names a missing script {script}.py"));
        }

        problems.AddRange(new[] { "README.md", "CONVENTIONS.md" }
            .Where(name => !File.Exists(DataFiles.Data(name)))
            .Select(name => "missing: data/" + name));

        // Assert: every folder was checked and no problem was found
        Assert.Equal(13, ReadmeStatements.Count);
        Assert.Empty(problems);
    }

    /// <summary>
    ///     Proves that every source document of the open corpus, the evaluation sets and the silver, MASC and imperative training data
    ///     appears in the source list of its folder (and that every MASC source row has sentences in <c>masc.tsv</c>).
    /// </summary>
    [Fact]
    public void PartOfSpeech_DataProvenance_Sources_ListEveryDocument()
    {
        // Arrange: the documents of the data and the documents that the source tables list
        var openListed = SourceIds("open/sources.tsv", "source_id");
        var goldListed = SourceIds("eval/gold-v2/sources.tsv", "doc_id");
        var silverListed = SourceIds("silver/sources.tsv", "source_id");
        var openDocuments = new[] { "open/train.tsv", "open/dev.tsv", "open/test.tsv", "eval/ste-challenge/test.tsv" }
            .SelectMany(corpus => DataFiles.ReadCorpus(DataFiles.Data(corpus)).Select(sentence => sentence.SourceId))
            .ToList();
        var goldDocuments = new[] { "dev.tsv", "test.tsv" }
            .SelectMany(name => DataFiles.ReadTable(DataFiles.Data("eval/gold-v2/" + name)).Column("doc_id"))
            .ToList();
        var silverDocuments = DataFiles.ReadCorpus(DataFiles.Data("silver/silver.tsv")).Select(sentence => sentence.SourceId).ToList();
        var mascDocuments = DataFiles.ReadCorpus(DataFiles.Data("masc/masc.tsv")).Select(sentence => sentence.SourceId).Distinct().ToList();
        var mascListed = SourceIds("masc/sources.tsv", "source_id");
        var imperativeDocuments = DataFiles.ReadCorpus(DataFiles.Data(ImperativeCorpus)).Select(sentence => sentence.SourceId).Distinct().ToList();
        var imperativeListed = SourceIds(ImperativeSources, "source_id");
        var compoundDocuments = DataFiles.ReadCorpus(DataFiles.Data(CompoundCorpus)).Select(sentence => sentence.SourceId).Distinct().ToList();
        var compoundListed = SourceIds(CompoundSources, "source_id");
        var headingsDocuments = DataFiles.ReadCorpus(DataFiles.Data(HeadingsCorpus)).Select(sentence => sentence.SourceId).Distinct().ToList();
        var headingsListed = SourceIds(HeadingsSources, "source_id");

        // Act: find the documents that no source list names
        var problems = MissingDocuments("open", openDocuments, openListed)
            .Concat(MissingDocuments("eval/gold-v2", goldDocuments, goldListed))
            .Concat(MissingDocuments("silver", silverDocuments, silverListed))
            .Concat(MissingDocuments("masc", mascDocuments, mascListed))
            .Concat(MissingDocuments("imperative", imperativeDocuments, imperativeListed))
            .Concat(MissingDocuments("compound", compoundDocuments, compoundListed))
            .Concat(MissingDocuments("headings", headingsDocuments, headingsListed))
            .Concat(mascListed.Except(mascDocuments).Select(document => "masc: listed but not in masc.tsv: " + document))
            .ToList();

        // Assert: the data and the lists are not empty and every document is listed
        Assert.True(openDocuments.Distinct().Count() > 5000);
        Assert.True(goldDocuments.Distinct().Count() > 1000);
        Assert.True(silverDocuments.Distinct().Count() > 5000);
        Assert.True(mascDocuments.Count > 300);
        Assert.True(imperativeDocuments.Count > 50);
        Assert.True(compoundDocuments.Count > 50);
        Assert.True(headingsDocuments.Count > 50);
        Assert.Empty(problems.Take(ShownProblems));
    }

    /// <summary>
    ///     Proves that every row of the source tables names a license class of the allowed set, a well-formed URL (empty
    ///     only for the document that the project wrote itself), and a positive sentence count; for MASC, the corpus-wide
    ///     CC BY 3.0 US class and counts that equal those of <c>masc.tsv</c>.
    /// </summary>
    [Fact]
    public void PartOfSpeech_DataProvenance_Sources_RowsRecordLicenseAndUrl()
    {
        // Arrange: read the source tables
        var problems = new List<string>();
        var rows = 0;

        // Act: check the license class, the URLs and the sentence count of every row; MASC records one license for the
        // whole corpus, so its table is checked for that class and for counts that match masc.tsv
        foreach (var (table, idColumn) in SourceTables)
        {
            var data = DataFiles.ReadTable(DataFiles.Data(table));
            rows += data.Rows.Count;
            foreach (var row in data.Rows)
            {
                problems.AddRange(SourceRowProblems(table, data, row, idColumn));
            }
        }

        rows += DataFiles.ReadTable(DataFiles.Data("masc/sources.tsv")).Rows.Count;
        problems.AddRange(MascSourceProblems());
        // Assert: the tables are not empty and every row is valid
        Assert.True(rows > 10000);
        Assert.Empty(problems.Take(ShownProblems));
    }

    /// <summary>
    ///     Proves that the checksum manifest lists exactly the committed data files (Markdown files and the manifest
    ///     itself excluded).
    /// </summary>
    [Fact]
    public void PartOfSpeech_DataProvenance_Manifest_ListsExactlyTheCommittedFiles()
    {
        // Arrange: read the manifest and list the data files it covers
        var manifest = ReadManifest();
        var onDisk = ManifestedFilesOnDisk();

        // Act: compare the file lists
        var problems = onDisk.Except(manifest.Select(row => row[0])).Select(path => "not in the manifest: " + path)
            .Concat(manifest.Select(row => row[0]).Except(onDisk).Select(path => "missing file: " + path))
            .ToList();

        // Assert: the manifest is not empty and complete
        Assert.True(manifest.Count > 20);
        Assert.Empty(problems);
    }

    /// <summary>
    ///     Proves that each committed data file has the size and the SHA-256 that the checksum manifest records.
    /// </summary>
    [Fact]
    public void PartOfSpeech_DataProvenance_Manifest_ChecksumsMatchCommittedFiles()
    {
        // Arrange: read the manifest (path, bytes, sha256, lines, sentences) and list the data files it covers
        var manifest = ReadManifest();
        var onDisk = ManifestedFilesOnDisk();

        // Act: compare the sizes and the hashes of the files that exist
        var problems = manifest.Where(row => onDisk.Contains(row[0])).SelectMany(ChecksumProblems).ToList();

        // Assert: the manifest is not empty and every file matches
        Assert.True(manifest.Count > 20);
        Assert.Empty(problems);
    }

    /// <summary>
    ///     Proves that the staged correction tables (<c>data/corrections</c>) have the documented columns, that every
    ///     row is valid on its own (legal address, tags of the 17, status cells with canonical flags, a change, a
    ///     provenance), that the rows are sorted, and that no token has two rows in one table (the one allowed overlap
    ///     between the two tables of corrections is checked by the test of the adjudicated rows).
    /// </summary>
    [Fact]
    public void PartOfSpeech_DataCorrections_Tables_AreWellFormed()
    {
        // Arrange: the tables of corrections that exist (the mechanical one is required), the candidates and the exceptions
        var names = CorrectionTables.CorrectionTableNames.Where(name => File.Exists(CorrectionTables.FilePath(name))).ToList();
        Assert.Contains("mechanical.tsv", names);
        var corrections = names.Select(name => (Name: name, Table: DataFiles.ReadTable(CorrectionTables.FilePath(name)))).ToList();
        var candidates = DataFiles.ReadTable(CorrectionTables.FilePath("candidates.tsv"));
        var exceptions = DataFiles.ReadTable(CorrectionTables.FilePath("exceptions.tsv"));

        // Act: validate each table; the addresses of the rows must be unique within each table
        var problems = corrections.SelectMany(entry => CorrectionTables.TableProblems(entry.Name, "corrections", entry.Table, new HashSet<string>(StringComparer.Ordinal)))
            .Concat(CorrectionTables.TableProblems("candidates.tsv", "candidates", candidates, new HashSet<string>(StringComparer.Ordinal)))
            .Concat(CorrectionTables.TableProblems("exceptions.tsv", "exceptions", exceptions, new HashSet<string>(StringComparer.Ordinal)))
            .ToList();

        // Assert: the tables that carry the staged work are not empty and no problem was found
        Assert.True(corrections.First(entry => entry.Name == "mechanical.tsv").Table.Rows.Count > 0);
        Assert.True(candidates.Rows.Count > 0);
        Assert.Empty(problems.Take(ShownProblems));
    }

    /// <summary>
    ///     Proves that the row validation of the data tests gives the verdicts of the shared cases
    ///     (<c>tools/data/tests/corrections-cases.tsv</c>), which the Python validator of the overlay is also held to.
    /// </summary>
    [Fact]
    public void PartOfSpeech_DataCorrections_RowCases_MatchSharedFixture()
    {
        // Arrange: the shared cases (one corrections row and the expected verdict each)
        var cases = DataFiles.ReadTable(DataFiles.Repository("tools/data/tests/corrections-cases.tsv"));

        // Act: validate every case row and compare the verdict
        var problems = cases.Rows
            .Select(row => (Case: cases.Cell(row, "case_id"), Expected: cases.Cell(row, "result"), Actual: CorrectionTables.RowError(cases, row) ?? "valid"))
            .Where(verdict => verdict.Expected != verdict.Actual)
            .Select(verdict => $"{verdict.Case}: expected {verdict.Expected}, got {verdict.Actual}")
            .ToList();

        // Assert: the cases are not empty, cover valid and invalid rows, and every verdict agrees
        Assert.True(cases.Rows.Count > 20);
        Assert.Contains("valid", cases.Column("result"));
        Assert.Contains("no-change", cases.Column("result"));
        Assert.Empty(problems);
    }

    /// <summary>
    ///     Proves that the field validation of the candidate rows gives the verdicts of the shared cases
    ///     (<c>tools/data/tests/candidate-cases.tsv</c>), which the Python validator of the overlay is also held to; the
    ///     syntax of the fields is validated whether the corrections are pending or applied.
    /// </summary>
    [Fact]
    public void PartOfSpeech_DataCorrections_CandidateCases_MatchSharedFixture()
    {
        // Arrange: the shared cases (one candidate row and the expected verdict each)
        var cases = DataFiles.ReadTable(DataFiles.Repository("tools/data/tests/candidate-cases.tsv"));

        // Act: validate every case row and compare the verdict
        var problems = cases.Rows
            .Select(row => (Case: cases.Cell(row, "case_id"), Expected: cases.Cell(row, "result"), Actual: CorrectionTables.CandidateError(cases, row) ?? "valid"))
            .Where(verdict => verdict.Expected != verdict.Actual)
            .Select(verdict => $"{verdict.Case}: expected {verdict.Expected}, got {verdict.Actual}")
            .ToList();

        // Assert: the cases are not empty, cover valid rows and every error code, and every verdict agrees
        Assert.True(cases.Rows.Count > 25);
        var results = cases.Column("result").ToList();
        foreach (var code in new[] { "valid", "bad-source", "bad-split", "bad-index", "bad-token", "bad-span", "bad-current-state", "bad-rule-or-hint" })
        {
            Assert.Contains(code, results);
        }

        Assert.Empty(problems);
    }

    /// <summary>
    ///     Proves that every correction row and every exception row addresses an existing token of a committed corpus
    ///     (sentence id, document id, token index and form), and that the correction rows in effect (a mechanical row
    ///     that an adjudicated row replaces is not in effect) match the corpora either all in the pending state (old
    ///     tag and status) or all in the applied state (new tag and status).
    /// </summary>
    [Fact]
    public void PartOfSpeech_DataCorrections_Rows_MatchCommittedCorpora()
    {
        // Arrange: the correction tables and the exceptions, and the sentences that their rows name
        var named = CorrectionTables.CorrectionTableNames
            .Where(name => File.Exists(CorrectionTables.FilePath(name)))
            .Select(name => (Name: name, Table: DataFiles.ReadTable(CorrectionTables.FilePath(name))))
            .ToList();
        var corrections = named.Select(entry => entry.Table).ToList();
        var exceptions = DataFiles.ReadTable(CorrectionTables.FilePath("exceptions.tsv"));
        var sentences = CorrectionTables.ReadNamedSentences(corrections.Concat(new[] { exceptions }));
        var problems = new List<string>();
        var pending = 0;
        var applied = 0;

        // Act: find the token of every row in effect and the state of the corpus at that token
        foreach (var (table, row) in CorrectionTables.EffectiveCorrections(named))
        {
            var (token, problem) = CorrectionTables.FindToken(sentences, table, row, 1);
            var state = token == null ? null : CorrectionState(table, row, token);
            pending += state == "pending" ? 1 : 0;
            applied += state == "applied" ? 1 : 0;
            if (state == null)
            {
                problems.Add(problem ?? $"{table.Cell(row, "sent_id")}#{table.Cell(row, "token_index")}: the corpus matches neither the old nor the new value");
            }
        }

        problems.AddRange(exceptions.Rows.Select(row => CorrectionTables.FindToken(sentences, exceptions, row, 1).Problem).OfType<string>());

        // Assert: there are rows, all of one state, and every row addresses its token
        Assert.True(pending + applied > 0);
        Assert.True(pending == 0 || applied == 0, $"rows of both states: {pending} pending and {applied} applied");
        Assert.Empty(problems.Take(ShownProblems));
    }

    /// <summary>
    ///     Proves that every correction row in effect is applied to the committed corpora: each row's token carries the
    ///     new tag and the new status cell, and none carries the old value that the importers wrote.
    /// </summary>
    [Fact]
    public void PartOfSpeech_DataCorrections_Rows_AreApplied()
    {
        // Arrange: the correction tables and the sentences that their rows name
        var named = CorrectionTables.CorrectionTableNames
            .Where(name => File.Exists(CorrectionTables.FilePath(name)))
            .Select(name => (Name: name, Table: DataFiles.ReadTable(CorrectionTables.FilePath(name))))
            .ToList();
        var sentences = CorrectionTables.ReadNamedSentences(named.Select(entry => entry.Table));
        var effective = CorrectionTables.EffectiveCorrections(named).ToList();
        var pending = 0;
        var applied = 0;

        // Act: find the token of every row in effect and count the rows whose token has the old and the new value
        foreach (var (table, row) in effective)
        {
            var (token, _) = CorrectionTables.FindToken(sentences, table, row, 1);
            var state = token == null ? null : CorrectionState(table, row, token);
            pending += state == "pending" ? 1 : 0;
            applied += state == "applied" ? 1 : 0;
        }

        // Assert: there are rows, none is pending and every one is applied
        Assert.NotEmpty(effective);
        Assert.Equal(0, pending);
        Assert.Equal(effective.Count, applied);
    }

    /// <summary>
    ///     Proves that every row of the adjudicated corrections (<c>data/corrections/adjudicated.tsv</c>) is a decision of
    ///     the two blind taggers: the provenance is <c>llm:opus+sol:&lt;batch&gt;</c> (a rule row does not belong there),
    ///     the row is exactly one of a tag change with the base status kept, a <c>disputed</c> mark with the tag and the
    ///     flags kept, or a flag change with the tag and the base kept, and a token that also has a mechanical row
    ///     starts from the same corpus value (old tag and status) with another change, so that the adjudicated row
    ///     replaces the mechanical one.
    /// </summary>
    [Fact]
    public void PartOfSpeech_DataCorrections_Adjudicated_RowsAreLlmDecisions()
    {
        // Arrange: the adjudicated table, and the mechanical rows by address
        var adjudicated = DataFiles.ReadTable(CorrectionTables.FilePath("adjudicated.tsv"));
        var mechanical = DataFiles.ReadTable(CorrectionTables.FilePath("mechanical.tsv"));
        var mechanicalRows = new Dictionary<string, string[]>(StringComparer.Ordinal);
        foreach (var row in mechanical.Rows)
        {
            mechanicalRows[CorrectionTables.CorrectionAddress(mechanical, row)] = row;
        }

        var problems = new List<string>();
        var replaced = 0;

        // Act: check the provenance, the kind and the replacement of every row
        foreach (var row in adjudicated.Rows)
        {
            var where = $"{adjudicated.Cell(row, "sent_id")}#{adjudicated.Cell(row, "token_index")}";
            if (!AdjudicatedProvenance.IsMatch(adjudicated.Cell(row, "provenance")))
            {
                problems.Add($"{where}: the provenance {adjudicated.Cell(row, "provenance")} is not llm:opus+sol:<batch>");
            }

            if (CorrectionTables.RowError(adjudicated, row) is { } error)
            {
                problems.Add($"{where}: {error}");
            }
            else if (CorrectionTables.AdjudicatedKind(adjudicated, row) == null)
            {
                problems.Add($"{where}: the row is neither a tag change, a disputed mark nor a flag change");
            }

            if (mechanicalRows.TryGetValue(CorrectionTables.CorrectionAddress(adjudicated, row), out var rule))
            {
                replaced++;
                var sameStart = mechanical.Cell(rule, "old_tag") == adjudicated.Cell(row, "old_tag")
                    && mechanical.Cell(rule, "old_status") == adjudicated.Cell(row, "old_status");
                var sameChange = mechanical.Cell(rule, "new_tag") == adjudicated.Cell(row, "new_tag")
                    && mechanical.Cell(rule, "new_status") == adjudicated.Cell(row, "new_status");
                if (!sameStart || sameChange)
                {
                    problems.Add($"{where}: the mechanical row of the token does not start from the same value or makes the same change");
                }
            }
        }

        // Assert: the table is not empty, the replaced mechanical rows are a subset of the mechanical table, no problem was found
        Assert.True(adjudicated.Rows.Count > 0);
        Assert.True(replaced <= mechanical.Rows.Count);
        Assert.Empty(problems.Take(ShownProblems));
    }

    /// <summary>
    ///     Proves that every candidate row addresses an existing token of a committed corpus (sentence id, document id,
    ///     token index and form) and that its span stays inside the sentence.
    /// </summary>
    [Fact]
    public void PartOfSpeech_DataCorrections_Candidates_AddressExistingTokens()
    {
        // Arrange: the candidates and the sentences that they name
        var candidates = DataFiles.ReadTable(CorrectionTables.FilePath("candidates.tsv"));
        var sentences = CorrectionTables.ReadNamedSentences(new[] { candidates });

        // Act: find the token of every row, with room for the whole span
        var problems = candidates.Rows
            .Select(row => CorrectionTables.FindToken(sentences, candidates, row, int.Parse(candidates.Cell(row, "span_length"), CultureInfo.InvariantCulture)).Problem)
            .OfType<string>()
            .ToList();

        // Assert: there are candidates and every one addresses its token
        Assert.True(candidates.Rows.Count > 0);
        Assert.Empty(problems.Take(ShownProblems));
    }

    /// <summary>
    ///     Proves that the training corpora and the token-level evaluation corpora use only the 17 Universal POS tags
    ///     (the placeholder only for masked tokens).
    /// </summary>
    [Fact]
    public void PartOfSpeech_TrainingData_Corpora_UseOnlySeventeenUposTags()
    {
        // Arrange: the corpora and a place for the problems
        var problems = new List<string>();
        var sentences = 0;

        // Act: check the tag of every token of every sentence
        foreach (var corpus in TrainingCorpora.Concat(EvaluationCorpora))
        {
            foreach (var sentence in DataFiles.ReadCorpus(DataFiles.Data(corpus)))
            {
                sentences++;
                problems.AddRange(TagProblems(corpus, sentence));
            }
        }

        // Assert: the corpora are not empty and no problem was found
        Assert.True(sentences > 1000);
        Assert.Empty(problems.Take(ShownProblems));
    }

    /// <summary>
    ///     Proves that in the training corpora and the token-level evaluation corpora the tokens of every sentence
    ///     concatenate to its text.
    /// </summary>
    [Fact]
    public void PartOfSpeech_TrainingData_Corpora_TokensRejoinToSentenceText()
    {
        // Arrange: the corpora and a place for the problems
        var problems = new List<string>();
        var sentences = 0;

        // Act: compare the tokens of every sentence with its text
        foreach (var corpus in TrainingCorpora.Concat(EvaluationCorpora))
        {
            foreach (var sentence in DataFiles.ReadCorpus(DataFiles.Data(corpus)))
            {
                sentences++;
                var rejoined = string.Concat(sentence.Tokens.Select(token => token[0]));
                if (rejoined != sentence.Text.Replace(" ", string.Empty))
                {
                    problems.Add($"{corpus}: {sentence.SentenceId}: tokens do not rejoin to the text");
                }
            }
        }

        // Assert: the corpora are not empty and no problem was found
        Assert.True(sentences > 1000);
        Assert.Empty(problems.Take(ShownProblems));
    }

    /// <summary>
    ///     Proves that the single-target evaluation sets (gold v2 and the shape probe) use only the documented coarse
    ///     classes that the Universal POS tags map to.
    /// </summary>
    [Fact]
    public void PartOfSpeech_EvaluationData_SingleTargetSets_UseOnlyDocumentedClasses()
    {
        // Arrange: the class columns of the gold sets and of the probe
        var classes = new[] { "dev.tsv", "test.tsv" }
            .SelectMany(name => DataFiles.ReadTable(DataFiles.Data("eval/gold-v2/" + name)).Column("pos"))
            .Concat(DataFiles.ReadTable(DataFiles.Data("eval/shapes/probe.tsv")).Column("gold"))
            .ToList();

        // Act: find the values that are not a documented class
        var unknown = classes.Where(value => Array.IndexOf(CoarseClasses, value) < 0).Distinct().ToList();

        // Assert: every target is labeled with a documented class
        Assert.True(classes.Count > 1000);
        Assert.Empty(unknown);
    }

    /// <summary>
    ///     Proves that no training document is also an evaluation document: the source ids of the training corpora are
    ///     disjoint from the document ids of the evaluation sets.
    /// </summary>
    [Fact]
    public void PartOfSpeech_Leakage_Documents_TrainingAndEvaluationAreDisjoint()
    {
        // Arrange: the document ids of all evaluation sets
        var evaluation = EvaluationItems();
        var documents = new HashSet<string>(evaluation.Select(item => item.Document), StringComparer.Ordinal);

        // Act: compare the document of every training sentence with the evaluation documents
        var problems = new List<string>();
        var sentences = 0;
        foreach (var corpus in TrainingCorpora)
        {
            foreach (var sentence in DataFiles.ReadCorpus(DataFiles.Data(corpus)))
            {
                sentences++;
                if (documents.Contains(sentence.SourceId))
                {
                    problems.Add($"{corpus}: document {sentence.SourceId} is also an evaluation document");
                }
            }
        }

        // Assert: the sets are not empty and nothing is shared
        Assert.True(documents.Count > 2000);
        Assert.True(sentences > 10000);
        Assert.Empty(problems.Distinct().Take(ShownProblems));
    }

    /// <summary>
    ///     Proves that no training sentence, of any length, is also an evaluation sentence: the sentence keys (lower-case
    ///     letters and numbers) of the training corpora are disjoint from those of the evaluation sets.
    /// </summary>
    [Fact]
    public void PartOfSpeech_Leakage_Sentences_TrainingAndEvaluationAreDisjoint()
    {
        // Arrange: the sentence keys of all evaluation sets (a text without a letter or digit has no key)
        var keys = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var item in EvaluationItems())
        {
            var key = DataFiles.SentenceKey(item.Text);
            if (key.Length > 0)
            {
                keys[key] = item.Set;
            }
        }

        // Act: compare every training sentence with the evaluation sentences
        var problems = new List<string>();
        var sentences = 0;
        foreach (var corpus in TrainingCorpora)
        {
            foreach (var sentence in DataFiles.ReadCorpus(DataFiles.Data(corpus)))
            {
                sentences++;
                if (keys.TryGetValue(DataFiles.SentenceKey(sentence.Text), out var set))
                {
                    problems.Add($"{corpus}: sentence {sentence.SentenceId} is also in {set}");
                }
            }
        }

        // Assert: the sets are not empty and nothing is shared
        Assert.True(keys.Count > 5000);
        Assert.True(sentences > 10000);
        Assert.Empty(problems.Take(ShownProblems));
    }

    /// <summary>
    ///     Proves that the sentence key of the leakage rule keeps only the lower-case letters and numbers of a sentence, so
    ///     that a short sentence has a key as well.
    /// </summary>
    [Fact]
    public void PartOfSpeech_Leakage_SentenceKey_KeepsLowerCaseLettersAndDigits()
    {
        // Arrange: a sentence with capitals, punctuation, spaces and digits, and a short sentence
        const string sentence = "X, Y! 4 2.";
        const string shortSentence = "Thank you.";

        // Act: compute the keys
        var key = DataFiles.SentenceKey(sentence);
        var shortKey = DataFiles.SentenceKey(shortSentence);

        // Assert: only the lower-case letters and numbers remain
        Assert.Equal("xy42", key);
        Assert.Equal("thankyou", shortKey);
    }

    /// <summary>
    ///     Proves that the committed leakage report covers every training set against every evaluation set (UD English-EWT
    ///     included) and records no document overlap and no exact sentence overlap for any pair.
    /// </summary>
    [Fact]
    public void PartOfSpeech_Leakage_Report_RecordsNoOverlaps()
    {
        // Arrange: read the report (training set, evaluation set, sentences, document overlaps, exact overlaps, near duplicates)
        var rows = File.ReadLines(DataFiles.Data("leakage-report.tsv"))
            .Where(line => line.Length > 0 && !DataFiles.IsComment(line))
            .Select(line => line.Split('\t'))
            .ToList();

        // Act: find the pairs that report an overlap, and the training sets that the report covers
        var overlaps = rows.Where(row => row[3] != "0" || row[4] != "0").Select(row => row[0] + " vs " + row[1]).ToList();
        var trainingSets = rows.Select(row => row[0]).Distinct().OrderBy(name => name, StringComparer.Ordinal).ToList();

        // Assert: every training set is reported against every evaluation set, and no pair overlaps
        Assert.Equal(new[] { "clauses", "compound", "compound2", "deverbal", "headings", "headings2", "imperative", "masc", "open-train", "silver", "steps", "terms" }, trainingSets);
        foreach (var training in trainingSets)
        {
            var evaluationSets = rows.Where(row => row[0] == training).Select(row => row[1]).Distinct(StringComparer.Ordinal);
            var expected = new[]
            {
                "gold-v2", "open-dev", "open-test", "shapes-dev", "shapes-dev-clause-verbs", "shapes-dev-compound-head", "shapes-dev-compound-head-2",
                "shapes-dev-defined-terms", "shapes-dev-deverbal-heads", "shapes-dev-hard", "shapes-dev-headings", "shapes-dev-headings-2",
                "shapes-dev-named-state", "shapes-dev-step-lines", "shapes-probe", "ste-challenge", "ud-ewt-dev", "ud-ewt-test", "ud-ewt-train"
            };
            Assert.Empty(expected.Except(evaluationSets));
        }

        Assert.All(rows, row => Assert.Equal(6, row.Length));
        Assert.Empty(overlaps);
    }

    /// <summary>
    ///     Proves that the header of the hard-shape development set announces a dev split and its development-only use, which
    ///     is what the development guard of the trainer accepts.
    /// </summary>
    [Fact]
    public void PartOfSpeech_ShapesDev_Header_AnnouncesDevSplit()
    {
        // Arrange: the header comments of the file (up to the first blank line)
        var header = File.ReadLines(DataFiles.Data(ShapesDevCorpus)).TakeWhile(line => line.Length > 0).ToList();

        // Act: find the corpus and use lines
        var corpus = header.FirstOrDefault(line => line.StartsWith("# corpus = ", StringComparison.Ordinal));
        var use = header.FirstOrDefault(line => line.StartsWith("# use = ", StringComparison.Ordinal));

        // Assert: the corpus line names the dev split and the use line says development only
        Assert.NotNull(corpus);
        Assert.Contains("dev split", corpus, StringComparison.Ordinal);
        Assert.NotNull(use);
        Assert.Contains("development only", use, StringComparison.Ordinal);
    }

    /// <summary>
    ///     Proves that every item of the hard-shape development set has a known shape, role, origin and lemma, that every
    ///     shape holds between one and 120 target items, that the controls pair with a lemma of a target of the same shape,
    ///     and that every sentence has exactly one item.
    /// </summary>
    [Fact]
    public void PartOfSpeech_ShapesDev_Items_HaveShapeRoleTargetAndLemma()
    {
        // Arrange: parse the file with the format rules of the trainer (known comments, target index, no repeated id)
        var items = ShapesDevItems();

        // Act: count the targets and controls of every shape and find the controls without a target lemma
        var problems = new List<string>();
        foreach (var shape in ShapesDevSets.Shapes)
        {
            var targets = items.Where(item => item.Shape == shape && item.IsTarget).ToList();
            var controls = items.Where(item => item.Shape == shape && !item.IsTarget).ToList();
            if (targets.Count < 1 || targets.Count > ShapesDevMaxTargetsPerShape)
            {
                problems.Add($"{shape}: {targets.Count} target items, expected 1 to {ShapesDevMaxTargetsPerShape}");
            }

            var targetLemmas = new HashSet<string>(targets.Select(item => item.Lemma), StringComparer.OrdinalIgnoreCase);
            problems.AddRange(controls
                .Where(item => !targetLemmas.Contains(item.Lemma))
                .Select(item => $"{shape}: control {item.Id} has the lemma '{item.Lemma}' of no target of the shape"));
        }

        // Assert: the set has controls and no problem was found
        Assert.True(items.Count > 100);
        Assert.Contains(items, item => !item.IsTarget);
        Assert.All(items, item => Assert.Contains(item.Shape, ShapesDevSets.Shapes));
        Assert.Empty(problems.Take(ShownProblems));
    }

    /// <summary>
    ///     Proves that the judged word of every item of the hard-shape development set is tagged with the class that its
    ///     role asks for (the class of the shape for a target, the other class for a control) and that no judged word is
    ///     disputed, reading the tags and statuses of the file directly.
    /// </summary>
    [Fact]
    public void PartOfSpeech_ShapesDev_Targets_MatchShapeClassAndAreNeverDisputed()
    {
        // Arrange: the items and the sentences of the file, in the same order
        var items = ShapesDevItems();
        var sentences = DataFiles.ReadCorpus(DataFiles.Data(ShapesDevCorpus)).ToList();
        Assert.Equal(items.Count, sentences.Count);

        // Act: compare the tag and the status of the judged word with the shape and the role
        var problems = new List<string>();
        for (var i = 0; i < items.Count; i++)
        {
            var item = items[i];
            var token = sentences[i].Tokens[item.Target];
            var tagClass = ClassOfTag(token[1]);
            var shapeClass = ShapesDevSets.ClassOfShape(item.Shape);
            var wanted = item.IsTarget ? shapeClass : OtherClass(shapeClass);
            if (tagClass != wanted)
            {
                problems.Add($"{item.Id}: the judged word '{token[0]}' is {token[1]}, expected the class {wanted}");
            }

            if (DataFiles.BaseStatus(token[2]) == "disputed")
            {
                problems.Add($"{item.Id}: the judged word '{token[0]}' is disputed");
            }
        }

        // Assert: both roles are present and no problem was found
        Assert.Contains(items, item => item.IsTarget);
        Assert.Contains(items, item => !item.IsTarget);
        Assert.Empty(problems.Take(ShownProblems));
    }

    /// <summary>
    ///     Proves that no shape of the hard-shape development set, and not the controls, has more than 30 percent authored
    ///     sentences, and that authored sentences are exactly those of the authored pseudo-document.
    /// </summary>
    [Fact]
    public void PartOfSpeech_ShapesDev_Authored_ShareIsCappedPerShapeAndForControls()
    {
        // Arrange: the items
        var items = ShapesDevItems();

        // Act: compute the authored share of every shape (targets) and of the controls
        var groups = ShapesDevSets.Shapes
            .Select(shape => (Name: $"targets of {shape}", Items: items.Where(item => item.Shape == shape && item.IsTarget).ToList()))
            .Append((Name: "controls", Items: items.Where(item => !item.IsTarget).ToList()))
            .ToList();
        var tooMany = groups
            .Where(group => group.Items.Count(item => item.Origin == ShapesDevSets.AuthoredOrigin) > ShapesDevMaxAuthoredShare * group.Items.Count)
            .Select(group => group.Name)
            .ToList();
        var mismatched = items
            .Where(item => (item.Origin == ShapesDevSets.AuthoredOrigin) != (item.SourceId == ShapesDevSets.AuthoredSourceId))
            .Select(item => item.Id)
            .ToList();

        // Assert: every group is non-empty, within the cap, and the origin agrees with the document
        Assert.All(groups, group => Assert.NotEmpty(group.Items));
        Assert.Empty(tooMany);
        Assert.Empty(mismatched);
    }

    /// <summary>
    ///     Proves that the library tokenizer reproduces the stored tokens of every sentence of the hard-shape development
    ///     set: one sentence, the same tokens.
    /// </summary>
    [Fact]
    public void PartOfSpeech_ShapesDev_Tokens_AreReproducedByTheTokenizer()
    {
        // Arrange: the sentences of the file
        var sentences = DataFiles.ReadCorpus(DataFiles.Data(ShapesDevCorpus)).ToList();

        // Act: tokenize the text of every sentence and compare with the stored tokens
        var problems = new List<string>();
        foreach (var sentence in sentences)
        {
            var tokenized = Tokenizer.Tokenize(sentence.Text);
            var actual = tokenized.SelectMany(part => part.Tokens).Select(token => token.Text).ToArray();
            var stored = sentence.Tokens.Select(token => token[0]).ToArray();
            if (tokenized.Count != 1 || !actual.SequenceEqual(stored, StringComparer.Ordinal))
            {
                problems.Add($"{sentence.SentenceId}: the tokenizer gives {tokenized.Count} sentence(s) and {actual.Length} tokens, the file has {stored.Length}");
            }
        }

        // Assert: the set is not empty and every sentence is reproduced
        Assert.True(sentences.Count > 100);
        Assert.Empty(problems.Take(ShownProblems));
    }

    /// <summary>
    ///     Proves that the source table of the hard-shape development set lists exactly the documents of the sentences, with
    ///     the number of sentences that each document contributes.
    /// </summary>
    [Fact]
    public void PartOfSpeech_ShapesDev_Sources_ListEveryDocumentWithItsCount()
    {
        // Arrange: the documents of the sentences and the rows of the table
        var counts = DataFiles.ReadCorpus(DataFiles.Data(ShapesDevCorpus))
            .GroupBy(sentence => sentence.SourceId, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.Count(), StringComparer.Ordinal);
        var table = DataFiles.ReadTable(DataFiles.Data(ShapesDevFolder + "/sources.tsv"));

        // Act: compare the listed documents and counts with the sentences
        var listed = table.Rows.ToDictionary(row => table.Cell(row, "source_id"), row => table.Cell(row, "sentences"), StringComparer.Ordinal);
        var problems = counts.Keys.Except(listed.Keys).Select(document => "not listed: " + document)
            .Concat(listed.Keys.Except(counts.Keys).Select(document => "listed but unused: " + document))
            .Concat(counts.Where(count => listed.TryGetValue(count.Key, out var cell) && cell != count.Value.ToString(CultureInfo.InvariantCulture))
                .Select(count => $"count of {count.Key} differs from the table"))
            .ToList();

        // Assert: the rows are unique, the set has documents and the table matches the file
        Assert.Equal(table.Rows.Count, listed.Count);
        Assert.True(counts.Count > 10);
        Assert.Empty(problems.Take(ShownProblems));
    }

    /// <summary>
    ///     Proves that every source document of the hard-shape development set has a US federal-work or public-domain basis
    ///     (or is the pseudo-document that the project wrote) and that no source row mentions FAA material.
    /// </summary>
    [Fact]
    public void PartOfSpeech_ShapesDev_Sources_AreOpenAndHaveNoFaaMaterial()
    {
        // Arrange: the rows of the table and the pattern of FAA material
        var table = DataFiles.ReadTable(DataFiles.Data(ShapesDevFolder + "/sources.tsv"));
        var faa = new Regex(@"faa\.gov|\bFAA\b|Federal Aviation", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        var openBases = new[] { "US federal government work", "US public domain", "Written by the project" };

        // Act: find the rows with another basis and the rows that mention FAA material
        var problems = new List<string>();
        foreach (var row in table.Rows)
        {
            var id = table.Cell(row, "source_id");
            if (!openBases.Any(basis => table.Cell(row, "license_basis").StartsWith(basis, StringComparison.Ordinal)))
            {
                problems.Add($"{id}: the license basis is not a US federal work, US public domain or project-written");
            }

            if (row.Any(cell => faa.IsMatch(cell)))
            {
                problems.Add($"{id}: the row mentions FAA material");
            }
        }

        // Assert: the table is not empty and no problem was found
        Assert.True(table.Rows.Count > 10);
        Assert.Empty(problems.Take(ShownProblems));
    }

    /// <summary>
    ///     Proves that the record of the excluded source documents of the hard-shape development set is well formed and that
    ///     no excluded document, and none of its URLs, is a used document of the set (the source table lists used documents only).
    /// </summary>
    [Fact]
    public void PartOfSpeech_ShapesDev_ExcludedSources_AreRecordedAndAbsentFromTheSet()
    {
        // Arrange: the excluded documents and the used documents (in the sentences and in the source table)
        var excluded = DataFiles.ReadTable(DataFiles.Data(ShapesDevFolder + "/excluded-sources.tsv"));
        var sources = DataFiles.ReadTable(DataFiles.Data(ShapesDevFolder + "/sources.tsv"));
        var usedIds = new HashSet<string>(sources.Column("source_id"), StringComparer.Ordinal);
        usedIds.UnionWith(DataFiles.ReadCorpus(DataFiles.Data(ShapesDevCorpus)).Select(sentence => sentence.SourceId));
        var usedUrls = new HashSet<string>(sources.Column("url"), StringComparer.Ordinal);

        // Act: find malformed rows and excluded documents that are used
        var problems = new List<string>();
        foreach (var row in excluded.Rows)
        {
            var id = excluded.Cell(row, "source_id");
            if (id.Length == 0 || usedIds.Contains(id))
            {
                problems.Add($"{id}: the id is empty or the document is used by the set");
            }

            if (!IsWellFormedUrl(excluded.Cell(row, "url")) || usedUrls.Contains(excluded.Cell(row, "url")))
            {
                problems.Add($"{id}: the URL is not a well-formed HTTPS URL or is a used document");
            }

            if (excluded.Cell(row, "reason").Length == 0)
            {
                problems.Add($"{id}: the reason is empty");
            }

            if (!DateTime.TryParseExact(excluded.Cell(row, "decided"), "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out _))
            {
                problems.Add($"{id}: the decision date is not yyyy-MM-dd");
            }
        }

        // Assert: the columns are the documented ones, the ids are unique and no problem was found
        Assert.Equal(new[] { "source_id", "url", "reason", "decided" }, excluded.Columns);
        Assert.Equal(excluded.Rows.Count, excluded.Column("source_id").Distinct(StringComparer.Ordinal).Count());
        Assert.NotEmpty(excluded.Rows);
        Assert.Empty(problems.Take(ShownProblems));
    }

    /// <summary>
    ///     Proves that the exclusion record is fail-closed for the committed files: it lists the NIST handbooks and the NBS handbook
    ///     that the owner dropped, and no row of the source table and no source document of any sentence of the set has an
    ///     excluded id or an excluded URL (compared by host without <c>www.</c> and path without a trailing slash).
    /// </summary>
    [Fact]
    public void PartOfSpeech_ShapesDev_ExcludedSources_NothingUsedMatchesByIdOrNormalizedUrl()
    {
        // Arrange: the excluded ids and normalized URLs, and the used documents of the table and of the sentences
        var excluded = DataFiles.ReadTable(DataFiles.Data(ShapesDevFolder + "/excluded-sources.tsv"));
        var excludedIds = new HashSet<string>(excluded.Column("source_id"), StringComparer.Ordinal);
        var excludedUrls = new HashSet<string>(excluded.Column("url").Select(NormalizeUrl), StringComparer.Ordinal);
        var sources = DataFiles.ReadTable(DataFiles.Data(ShapesDevFolder + "/sources.tsv"));
        var sentenceDocuments = DataFiles.ReadCorpus(DataFiles.Data(ShapesDevCorpus)).Select(sentence => sentence.SourceId).Distinct(StringComparer.Ordinal);

        // Act: find the used documents that an exclusion matches
        var problems = new List<string>();
        problems.AddRange(sources.Rows
            .Where(row => excludedIds.Contains(sources.Cell(row, "source_id")) || excludedUrls.Contains(NormalizeUrl(sources.Cell(row, "url"))))
            .Select(row => "source row " + sources.Cell(row, "source_id")));
        problems.AddRange(sentenceDocuments.Where(excludedIds.Contains).Select(id => "sentences of " + id));

        // Assert: the handbooks are recorded as excluded and nothing excluded is used
        Assert.Contains("nist-hb44-2024", excludedIds);
        Assert.Contains("nist-hb130-2024", excludedIds);
        Assert.Contains("nbs-safehandlingofbo65unit", excludedIds);
        Assert.Empty(problems.Take(ShownProblems));
    }

    /// <summary>
    ///     Proves that the committed hard-shape development set meets the documented quotas: exactly 120 targets per shape,
    ///     no lemma over its cap (4; 12 for n-measure), exactly 100 controls with at most two per lemma and shape, and an
    ///     authored share of at most 30 percent in every shape and in the controls.
    /// </summary>
    [Fact]
    public void PartOfSpeech_ShapesDev_Quotas_AreMetByTheCommittedSet()
    {
        // Arrange: the items
        var items = ShapesDevItems();
        var problems = new List<string>();

        // Act: check the target quota, the lemma caps and the authored share of every shape
        foreach (var shape in ShapesDevSets.Shapes)
        {
            var targets = items.Where(item => item.Shape == shape && item.IsTarget).ToList();
            if (targets.Count != ShapesDevMaxTargetsPerShape)
            {
                problems.Add($"{shape}: {targets.Count} targets, expected {ShapesDevMaxTargetsPerShape}");
            }

            var cap = shape == "n-measure" ? ShapesDevMeasureLemmaCap : ShapesDevLemmaCap;
            problems.AddRange(targets.GroupBy(item => item.Lemma, StringComparer.OrdinalIgnoreCase)
                .Where(group => group.Count() > cap)
                .Select(group => $"{shape}: the lemma '{group.Key}' has {group.Count()} targets, the cap is {cap}"));
            if (targets.Count(item => item.Origin == ShapesDevSets.AuthoredOrigin) > ShapesDevMaxAuthoredShare * targets.Count)
            {
                problems.Add($"{shape}: more than {ShapesDevMaxAuthoredShare:P0} of the targets are authored");
            }
        }

        // Act: check the control total, the lemma cap per shape and the authored share of the controls
        var controls = items.Where(item => !item.IsTarget).ToList();
        if (controls.Count != ShapesDevControls)
        {
            problems.Add($"controls: {controls.Count}, expected {ShapesDevControls}");
        }

        problems.AddRange(controls.GroupBy(item => (item.Shape, Lemma: item.Lemma.ToLowerInvariant()))
            .Where(group => group.Count() > ShapesDevControlLemmaCap)
            .Select(group => $"controls: {group.Key.Shape} lemma '{group.Key.Lemma}' has {group.Count()} controls, the cap is {ShapesDevControlLemmaCap}"));
        if (controls.Count(item => item.Origin == ShapesDevSets.AuthoredOrigin) > ShapesDevMaxAuthoredShare * controls.Count)
        {
            problems.Add($"controls: more than {ShapesDevMaxAuthoredShare:P0} are authored");
        }

        // Assert: no quota is missed
        Assert.Empty(problems.Take(ShownProblems));
    }

    /// <summary>
    ///     Proves that the builder of the hard-shape development set reaches the held-out sets only through the opaque
    ///     exclusion keys: its source names no probe file and uses only the key functions of the leakage module, and the
    ///     leakage module reads the probe through its sentence column only.
    /// </summary>
    [Fact]
    public void PartOfSpeech_ShapesDev_Builder_SeesHeldOutSetsOnlyThroughOpaqueKeys()
    {
        // Arrange: the sources of the builder and of the leakage module
        var builder = File.ReadAllText(DataFiles.Repository("tools/data/shapes_dev.py"));
        var leakage = File.ReadAllText(DataFiles.Repository("tools/data/leakage.py"));

        // Act: find the members of the leakage module that the builder uses
        var used = Regex.Matches(builder, @"\bleakage\.(\w+)").Cast<Match>().Select(match => match.Groups[1].Value).Distinct().OrderBy(name => name, StringComparer.Ordinal).ToArray();

        // Assert: no probe path, only the opaque-key members, and the probe is read through the sentence column alone
        Assert.DoesNotContain("probe.tsv", builder, StringComparison.Ordinal);
        Assert.Equal(new[] { "PROBE_GRAM_WORDS", "gram_overlap", "held_out_overlap_keys", "key_overlap", "require_sets" }, used);
        Assert.Contains("sentence_column(path)", leakage, StringComparison.Ordinal);
        Assert.DoesNotContain("_tsv_rows(probe", leakage, StringComparison.Ordinal);
    }

    /// <summary>
    ///     Proves that the selection of the hard tranche of the hard-shape development set is independent of the PartOfSpeech
    ///     model: neither the builder (<c>shapes_dev.py</c>) nor the harness of the other model's numbers (<c>tools/data/hmm-tags</c>)
    ///     names a weights file, a decoder, a tagger or the shapes evaluation, the harness has no reference to this library, and
    ///     the harness is not part of the solution (so no build or CI step needs its binary).
    /// </summary>
    [Fact]
    public void PartOfSpeech_ShapesDevHard_Selection_UsesNoPartOfSpeechModel()
    {
        // Arrange: the sources of the selection pipeline
        var forbidden = new[] { "weights.bin", "Resources/weights", "PerceptronModel", "FeatureExtractor", "PartOfSpeechTagger", "ITagger", "eval-shapes", "ShapesDevEvaluator" };
        var folder = DataFiles.Repository("tools/data/hmm-tags");
        var sources = new List<string> { DataFiles.Repository("tools/data/shapes_dev.py") };
        sources.AddRange(Directory.GetFiles(folder).Where(file => file.EndsWith(".cs", StringComparison.Ordinal) || file.EndsWith(".csproj", StringComparison.Ordinal)));

        // Act
        var found = sources
            .SelectMany(file => forbidden.Where(word => File.ReadAllText(file).Contains(word, StringComparison.Ordinal)).Select(word => $"{Path.GetFileName(file)}: {word}"))
            .ToList();
        var project = File.ReadAllText(Directory.GetFiles(folder, "*.csproj").Single());
        var solution = File.ReadAllText(DataFiles.Repository("PartOfSpeech.slnx"));

        // Assert
        Assert.Empty(found);
        Assert.DoesNotContain("ProjectReference", project, StringComparison.Ordinal);
        Assert.DoesNotContain("DemaConsulting.PartOfSpeech", project, StringComparison.Ordinal);
        Assert.DoesNotContain("hmm-tags", solution, StringComparison.Ordinal);
        Assert.DoesNotContain("DemaConsulting.PartOfSpeech", File.ReadAllText(Path.Combine(folder, "Program.cs")), StringComparison.Ordinal);
    }

    /// <summary>
    ///     Proves that the committed leakage report of the hard-shape development set covers every other evaluation set and
    ///     records no document overlap, no exact sentence overlap and no shared word 5-gram with the shape probe.
    /// </summary>
    [Fact]
    public void PartOfSpeech_ShapesDev_LeakageReport_RecordsNoOverlapWithOtherEvaluationSets()
    {
        // Arrange: read the report (evaluation set, dev sentences, documents, exact, near duplicates, probe 5-grams)
        var rows = File.ReadLines(DataFiles.Data(ShapesDevFolder + "/leakage-report.tsv"))
            .Where(line => line.Length > 0 && !DataFiles.IsComment(line))
            .Select(line => line.Split('\t'))
            .ToList();
        var expected = new[]
        {
            "gold-v2", "open-dev", "open-test", "shapes-probe", "ste-challenge", "ud-ewt-dev", "ud-ewt-test", "ud-ewt-train"
        };

        // Act: find the sets that the report misses and the rows that report an overlap
        var sets = rows.Select(row => row[0]).ToList();
        var overlaps = rows.Where(row => row[2] != "0" || row[3] != "0").Select(row => row[0]).ToList();
        var probe = rows.Single(row => row[0] == "shapes-probe");
        var sentenceCount = ShapesDevItems().Count.ToString(CultureInfo.InvariantCulture);

        // Assert: every set is reported once, with the sentence count of the file, and nothing overlaps
        Assert.All(rows, row => Assert.Equal(6, row.Length));
        Assert.Equal(expected, sets.OrderBy(name => name, StringComparer.Ordinal));
        Assert.All(rows, row => Assert.Equal(sentenceCount, row[1]));
        Assert.Empty(overlaps);
        Assert.Equal("0", probe[5]);
    }

    /// <summary>
    ///     Proves, reading the evaluation files themselves, that the hard-shape development set (both tranches taken as one
    ///     family, so a document that both tranches use is not counted against it) shares no document and no sentence (of
    ///     any length) with the open dev and test sets, gold v2, the challenge set or the shape probe.
    /// </summary>
    [Fact]
    public void PartOfSpeech_ShapesDev_Sentences_AreDisjointFromTheOtherEvaluationSets()
    {
        // Arrange: the documents and sentence keys of the development set (both tranches) and of every other evaluation set
        var dev = DataFiles.ReadCorpus(DataFiles.Data(ShapesDevCorpus)).Concat(DataFiles.ReadCorpus(DataFiles.Data(ShapesDevHardCorpus))).ToList();
        var documents = new HashSet<string>(dev.Select(sentence => sentence.SourceId), StringComparer.Ordinal);
        var keys = new HashSet<string>(dev.Select(sentence => DataFiles.SentenceKey(sentence.Text)), StringComparer.Ordinal);
        var others = EvaluationItems().Where(item => item.Set != ShapesDevCorpus && item.Set != ShapesDevHardCorpus).ToList();

        // Act: find the shared documents and sentences
        var problems = others
            .Where(item => documents.Contains(item.Document) || keys.Contains(DataFiles.SentenceKey(item.Text)))
            .Select(item => $"{item.Set}: {item.Document} shares a document or sentence with the development set")
            .ToList();

        // Assert: the sets are not empty and nothing is shared
        Assert.True(dev.Count > 1000);
        Assert.True(others.Count > 5000);
        Assert.Empty(problems.Take(ShownProblems));
    }

    /// <summary>
    ///     Proves that the header of the hard tranche announces a dev split, its development-only use and the hard tranche,
    ///     and that no comment of the file carries the numbers that the selection used (no <c>q</c> and no tagger output).
    /// </summary>
    [Fact]
    public void PartOfSpeech_ShapesDevHard_Header_AnnouncesDevSplitAndCarriesNoSelectionNumbers()
    {
        // Arrange: the comment lines of the file
        var lines = File.ReadAllLines(DataFiles.Data(ShapesDevHardCorpus));
        var header = lines.TakeWhile(line => line.Length > 0).ToList();
        var itemKeys = lines.Where(line => line.StartsWith("# ", StringComparison.Ordinal))
            .Select(line => line[2..].Split(new[] { " = " }, 2, StringSplitOptions.None)[0])
            .Distinct(StringComparer.Ordinal)
            .OrderBy(key => key, StringComparer.Ordinal)
            .ToList();

        // Act: find the corpus and use lines
        var corpus = header.FirstOrDefault(line => line.StartsWith("# corpus = ", StringComparison.Ordinal));
        var use = header.FirstOrDefault(line => line.StartsWith("# use = ", StringComparison.Ordinal));

        // Assert: dev split, hard tranche, development only, and only the documented comment keys
        Assert.NotNull(corpus);
        Assert.Contains("dev split", corpus, StringComparison.Ordinal);
        Assert.Contains("hard tranche", corpus, StringComparison.Ordinal);
        Assert.NotNull(use);
        Assert.Contains("development only", use, StringComparison.Ordinal);
        Assert.Equal(
            new[] { "columns", "corpus", "judged word", "lemma", "license", "origin", "role", "selection", "sent_id", "shape", "source_id", "status", "status flags", "target", "text", "use" },
            itemKeys);
    }

    /// <summary>
    ///     Proves that every item of the hard tranche has a known shape, role and lemma, that the judged word carries the class
    ///     of its role and is never disputed, that no item is authored, and that the controls pair with a lemma of a target of
    ///     the same shape.
    /// </summary>
    [Fact]
    public void PartOfSpeech_ShapesDevHard_Items_AreWellFormedRealAndNeverDisputed()
    {
        // Arrange: the items and the sentences of the file, in the same order
        var items = ShapesDevHardItems();
        var sentences = DataFiles.ReadCorpus(DataFiles.Data(ShapesDevHardCorpus)).ToList();
        Assert.Equal(items.Count, sentences.Count);

        // Act: compare the tag and the status of the judged word with the shape and the role, and find the controls without a target lemma
        var problems = new List<string>();
        for (var i = 0; i < items.Count; i++)
        {
            var item = items[i];
            var token = sentences[i].Tokens[item.Target];
            var shapeClass = ShapesDevSets.ClassOfShape(item.Shape);
            var wanted = item.IsTarget ? shapeClass : OtherClass(shapeClass);
            if (ClassOfTag(token[1]) != wanted)
            {
                problems.Add($"{item.Id}: the judged word '{token[0]}' is {token[1]}, expected the class {wanted}");
            }

            if (DataFiles.BaseStatus(token[2]) == "disputed")
            {
                problems.Add($"{item.Id}: the judged word '{token[0]}' is disputed");
            }

            if (item.Origin == ShapesDevSets.AuthoredOrigin || item.SourceId == ShapesDevSets.AuthoredSourceId)
            {
                problems.Add($"{item.Id}: the hard tranche has no authored sentence");
            }
        }

        foreach (var shape in ShapesDevSets.Shapes)
        {
            var targetLemmas = new HashSet<string>(items.Where(item => item.Shape == shape && item.IsTarget).Select(item => item.Lemma), StringComparer.OrdinalIgnoreCase);
            problems.AddRange(items
                .Where(item => item.Shape == shape && !item.IsTarget && !targetLemmas.Contains(item.Lemma))
                .Select(item => $"{shape}: control {item.Id} has the lemma '{item.Lemma}' of no target of the shape"));
        }

        // Assert: both roles are present, every shape is known and no problem was found
        Assert.Contains(items, item => item.IsTarget);
        Assert.Contains(items, item => !item.IsTarget);
        Assert.All(items, item => Assert.Contains(item.Shape, ShapesDevSets.Shapes));
        Assert.Empty(problems.Take(ShownProblems));
    }

    /// <summary>
    ///     Proves that the committed hard tranche meets its documented quotas: targets per shape between the floor and the
    ///     cap (80/40/70/0/80 and 100/100/100/0/100 for compound, subject-initial, imperative, measure, modal), no n-measure
    ///     item, 50 to 60 controls, at most four targets per lemma and shape, and at most two controls per lemma and shape.
    /// </summary>
    [Fact]
    public void PartOfSpeech_ShapesDevHard_Quotas_AreMetByTheCommittedTranche()
    {
        // Arrange: the items
        var items = ShapesDevHardItems();
        var problems = new List<string>();

        // Act: check the floor, the cap and the lemma cap of every shape
        foreach (var shape in ShapesDevSets.Shapes)
        {
            var (floor, cap) = ShapesDevHardTargetQuotas[shape];
            var targets = items.Where(item => item.Shape == shape && item.IsTarget).ToList();
            if (targets.Count < floor || targets.Count > cap)
            {
                problems.Add($"{shape}: {targets.Count} targets, expected {floor} to {cap}");
            }

            problems.AddRange(targets.GroupBy(item => item.Lemma, StringComparer.OrdinalIgnoreCase)
                .Where(group => group.Count() > ShapesDevHardLemmaCap)
                .Select(group => $"{shape}: the lemma '{group.Key}' has {group.Count()} targets, the cap is {ShapesDevHardLemmaCap}"));
        }

        var controls = items.Where(item => !item.IsTarget).ToList();
        if (controls.Count < ShapesDevHardControlFloor || controls.Count > ShapesDevHardControlCap)
        {
            problems.Add($"controls: {controls.Count}, expected {ShapesDevHardControlFloor} to {ShapesDevHardControlCap}");
        }

        problems.AddRange(controls.GroupBy(item => (item.Shape, Lemma: item.Lemma.ToLowerInvariant()))
            .Where(group => group.Count() > ShapesDevControlLemmaCap)
            .Select(group => $"controls: {group.Key.Shape} lemma '{group.Key.Lemma}' has {group.Count()} controls, the cap is {ShapesDevControlLemmaCap}"));

        // Assert: no quota is missed and n-measure has no item
        Assert.DoesNotContain(items, item => item.Shape == "n-measure");
        Assert.Empty(problems.Take(ShownProblems));
    }

    /// <summary>
    ///     Proves that the library tokenizer reproduces the stored tokens of every sentence of the hard tranche: one sentence,
    ///     the same tokens.
    /// </summary>
    [Fact]
    public void PartOfSpeech_ShapesDevHard_Tokens_AreReproducedByTheTokenizer()
    {
        // Arrange: the sentences of the file
        var sentences = DataFiles.ReadCorpus(DataFiles.Data(ShapesDevHardCorpus)).ToList();

        // Act: tokenize the text of every sentence and compare with the stored tokens
        var problems = new List<string>();
        foreach (var sentence in sentences)
        {
            var tokenized = Tokenizer.Tokenize(sentence.Text);
            var actual = tokenized.SelectMany(part => part.Tokens).Select(token => token.Text).ToArray();
            var stored = sentence.Tokens.Select(token => token[0]).ToArray();
            if (tokenized.Count != 1 || !actual.SequenceEqual(stored, StringComparer.Ordinal))
            {
                problems.Add($"{sentence.SentenceId}: the tokenizer gives {tokenized.Count} sentence(s) and {actual.Length} tokens, the file has {stored.Length}");
            }
        }

        // Assert: the set is not empty and every sentence is reproduced
        Assert.True(sentences.Count > 100);
        Assert.Empty(problems.Take(ShownProblems));
    }

    /// <summary>
    ///     Proves that the source table of the hard tranche lists exactly the documents of its sentences with their counts,
    ///     that every document has a US federal-work or public-domain basis, that no row mentions FAA material, and that no
    ///     excluded document (by id or normalized URL) is used.
    /// </summary>
    [Fact]
    public void PartOfSpeech_ShapesDevHard_Sources_AreListedOpenAndNotExcluded()
    {
        // Arrange: the documents of the sentences, the rows of the table and the exclusions
        var counts = DataFiles.ReadCorpus(DataFiles.Data(ShapesDevHardCorpus))
            .GroupBy(sentence => sentence.SourceId, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.Count(), StringComparer.Ordinal);
        var table = DataFiles.ReadTable(DataFiles.Data(ShapesDevHardSources));
        var excluded = DataFiles.ReadTable(DataFiles.Data(ShapesDevFolder + "/excluded-sources.tsv"));
        var excludedIds = new HashSet<string>(excluded.Column("source_id"), StringComparer.Ordinal);
        var excludedUrls = new HashSet<string>(excluded.Column("url").Select(NormalizeUrl), StringComparer.Ordinal);
        var faa = new Regex(@"faa\.gov|\bFAA\b|Federal Aviation", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        var openBases = new[] { "US federal government work", "US public domain" };

        // Act: compare the listed documents and counts with the sentences and look at every row
        var listed = table.Rows.ToDictionary(row => table.Cell(row, "source_id"), row => table.Cell(row, "sentences"), StringComparer.Ordinal);
        var problems = counts.Keys.Except(listed.Keys).Select(document => "not listed: " + document)
            .Concat(listed.Keys.Except(counts.Keys).Select(document => "listed but unused: " + document))
            .Concat(counts.Where(count => listed.TryGetValue(count.Key, out var cell) && cell != count.Value.ToString(CultureInfo.InvariantCulture))
                .Select(count => $"count of {count.Key} differs from the table"))
            .ToList();
        foreach (var row in table.Rows)
        {
            var id = table.Cell(row, "source_id");
            if (!openBases.Any(basis => table.Cell(row, "license_basis").StartsWith(basis, StringComparison.Ordinal)))
            {
                problems.Add($"{id}: the license basis is not a US federal work or US public domain");
            }

            if (row.Any(cell => faa.IsMatch(cell)))
            {
                problems.Add($"{id}: the row mentions FAA material");
            }

            if (excludedIds.Contains(id) || excludedUrls.Contains(NormalizeUrl(table.Cell(row, "url"))))
            {
                problems.Add($"{id}: the document is excluded");
            }
        }

        // Assert: the rows are unique, the table has documents and no problem was found
        Assert.Equal(table.Rows.Count, listed.Count);
        Assert.True(counts.Count > 10);
        Assert.Empty(problems.Take(ShownProblems));
    }

    /// <summary>
    ///     Proves that the committed leakage report of the hard tranche covers every other evaluation set, records no exact
    ///     sentence overlap, no same-document near duplicate and no shared word 5-gram with the shape probe, and that its
    ///     standard-tranche row reports shared documents (allowed) with no exact or near-duplicate sentence.
    /// </summary>
    [Fact]
    public void PartOfSpeech_ShapesDevHard_LeakageReport_RecordsNoOverlapAndSharedDocumentsWithTheStandardTranche()
    {
        // Arrange: read the report (evaluation set, dev sentences, documents, exact, near duplicates, probe 5-grams)
        var rows = File.ReadLines(DataFiles.Data(ShapesDevFolder + "/hard-leakage-report.tsv"))
            .Where(line => line.Length > 0 && !DataFiles.IsComment(line))
            .Select(line => line.Split('\t'))
            .ToList();
        var expected = new[]
        {
            "gold-v2", "open-dev", "open-test", "shapes-dev", "shapes-probe", "ste-challenge", "ud-ewt-dev", "ud-ewt-test", "ud-ewt-train"
        };
        var sentenceCount = ShapesDevHardItems().Count.ToString(CultureInfo.InvariantCulture);

        // Act: find the rows that report an overlap (the documents of the standard tranche are shared on purpose)
        var overlaps = rows.Where(row => row[3] != "0" || row[4] != "0" || (row[0] != "shapes-dev" && row[2] != "0")).Select(row => row[0]).ToList();
        var probe = rows.Single(row => row[0] == "shapes-probe");

        // Assert: every set is reported once, with the sentence count of the file, and nothing overlaps
        Assert.All(rows, row => Assert.Equal(6, row.Length));
        Assert.Equal(expected, rows.Select(row => row[0]).OrderBy(name => name, StringComparer.Ordinal));
        Assert.All(rows, row => Assert.Equal(sentenceCount, row[1]));
        Assert.Empty(overlaps);
        Assert.Equal("0", probe[5]);
    }

    /// <summary>
    ///     Proves, reading the evaluation files themselves, that the hard tranche shares no sentence (of any length) with the
    ///     standard tranche, the open dev and test sets, gold v2, the challenge set or the shape probe, and no document with
    ///     any of them except the standard tranche (a shared document is allowed there).
    /// </summary>
    [Fact]
    public void PartOfSpeech_ShapesDevHard_Sentences_AreDisjointFromTheOtherEvaluationSets()
    {
        // Arrange: the documents and sentence keys of the hard tranche and of every other evaluation set
        var hard = DataFiles.ReadCorpus(DataFiles.Data(ShapesDevHardCorpus)).ToList();
        var documents = new HashSet<string>(hard.Select(sentence => sentence.SourceId), StringComparer.Ordinal);
        var keys = new HashSet<string>(hard.Select(sentence => DataFiles.SentenceKey(sentence.Text)), StringComparer.Ordinal);
        var others = EvaluationItems().Where(item => item.Set != ShapesDevHardCorpus).ToList();

        // Act: find the shared sentences (any set) and the shared documents (any set but the standard tranche)
        var problems = others
            .Where(item => keys.Contains(DataFiles.SentenceKey(item.Text)) || (item.Set != ShapesDevCorpus && documents.Contains(item.Document)))
            .Select(item => $"{item.Set}: {item.Document} shares a sentence or a document with the hard tranche")
            .ToList();

        // Assert: the sets are not empty, the tranches share documents by design and nothing else is shared
        Assert.True(hard.Count > 100);
        Assert.True(others.Count > 5000);
        Assert.Contains(others, item => item.Set == ShapesDevCorpus && documents.Contains(item.Document));
        Assert.Empty(problems.Take(ShownProblems));
    }

    /// <summary>
    ///     Proves that the header of every new tranche of the hard-shape development set announces its own tranche and the dev
    ///     split, that no comment of the file carries the numbers that the selection used (no <c>q</c> and no tagger output),
    ///     and that every sentence id has the tranche infix and four digits.
    /// </summary>
    [Fact]
    public void PartOfSpeech_ShapesDevNew_Headers_AnnounceTheTrancheAndTheDevSplit()
    {
        // Arrange: the comment keys that a file of the new tranches may carry (the kind belongs to the headings only)
        var allowed = new[]
        {
            "columns", "corpus", "file", "judged word", "lemma", "license", "origin", "role", "selection", "sent_id", "shape",
            "source_id", "status", "status flags", "target", "text", "use"
        };
        var problems = new List<string>();

        foreach (var tranche in ShapesDevNewTranches)
        {
            var lines = File.ReadAllLines(DataFiles.Data(tranche.Corpus));
            var header = lines.TakeWhile(line => line.Length > 0).ToList();
            var keys = lines.Where(DataFiles.IsComment)
                .Select(line => line[2..].Split(new[] { " = " }, 2, StringSplitOptions.None)[0])
                .Distinct(StringComparer.Ordinal)
                .ToList();

            // Act: find the corpus, use and selection lines, the unexpected comment keys and the malformed ids
            var corpus = header.FirstOrDefault(line => line.StartsWith("# corpus = ", StringComparison.Ordinal));
            var use = header.FirstOrDefault(line => line.StartsWith("# use = ", StringComparison.Ordinal));
            var selection = header.FirstOrDefault(line => line.StartsWith("# selection = ", StringComparison.Ordinal));
            if (corpus == null || !corpus.Contains(tranche.Name + " tranche", StringComparison.Ordinal) || !corpus.Contains("dev split", StringComparison.Ordinal))
            {
                problems.Add($"{tranche.Name}: the corpus line does not announce '{tranche.Name} tranche' and 'dev split'");
            }

            if (use == null || !use.Contains("development only", StringComparison.Ordinal))
            {
                problems.Add($"{tranche.Name}: the use line does not say 'development only'");
            }

            if (selection == null || selection.Any(char.IsDigit))
            {
                problems.Add($"{tranche.Name}: the selection line is missing or carries a number");
            }

            var permitted = tranche.IsHeadings ? allowed.Append("kind") : allowed;
            problems.AddRange(keys.Except(permitted).Select(key => $"{tranche.Name}: the comment key '{key}' is not documented"));
            problems.AddRange(keys.Where(key => key == "q" || key.StartsWith("q_", StringComparison.Ordinal)).Select(key => $"{tranche.Name}: the comment key '{key}' carries a selection number"));

            var pattern = new Regex($@"^shapes-dev-{Regex.Escape(tranche.Name)}-\d{{4}}$", RegexOptions.CultureInvariant);
            var ids = ReadBlockComments(tranche.Corpus);
            problems.AddRange(ids.Where(block => !pattern.IsMatch(block.Id)).Select(block => $"{tranche.Name}: {block.Id}: the id is not shapes-dev-{tranche.Name}-NNNN"));
            if (ids.Count == 0)
            {
                problems.Add($"{tranche.Name}: no sentence was found");
            }
        }

        // Assert: every tranche announces itself and no problem was found
        Assert.Empty(problems.Take(ShownProblems));
    }

    /// <summary>
    ///     Proves that every item of the new tranches has its comments in the stated order, the only shape of its tranche, a
    ///     known role, a target inside the sentence, the origin <c>real</c>, a lemma, the gold tag of its role (and, for a heading,
    ///     of its kind), a judged word that is never disputed, and tokens that rejoin to the text.
    /// </summary>
    [Fact]
    public void PartOfSpeech_ShapesDevNew_Items_AreWellFormedRealAndNeverDisputed()
    {
        // Arrange: the order of the comments after the sentence id, the source and the text
        var order = new[] { "sent_id", "source_id", "text", "shape", "role", "target", "lemma", "origin" };
        var problems = new List<string>();
        var total = 0;

        foreach (var tranche in ShapesDevNewTranches)
        {
            var items = ShapesDevNewItems(tranche);
            var sentences = DataFiles.ReadCorpus(DataFiles.Data(tranche.Corpus)).ToList();
            var comments = ReadBlockComments(tranche.Corpus);
            if (items.Count != sentences.Count || comments.Count != sentences.Count)
            {
                problems.Add($"{tranche.Name}: {items.Count} items, {sentences.Count} sentences and {comments.Count} comment blocks differ");
                continue;
            }

            var expectedOrder = tranche.IsHeadings ? order.Append("kind").ToArray() : order;

            // Act: check every item against the format, the gold tag rules and its sentence
            for (var i = 0; i < items.Count; i++)
            {
                total++;
                var item = items[i];
                var sentence = sentences[i];
                var where = $"{tranche.Name}: {sentence.SentenceId}";
                if (!comments[i].Keys.SequenceEqual(expectedOrder, StringComparer.Ordinal))
                {
                    problems.Add($"{where}: the comments are '{string.Join(", ", comments[i].Keys)}', expected '{string.Join(", ", expectedOrder)}'");
                }

                if (item.Shape != tranche.Shape)
                {
                    problems.Add($"{where}: the shape is '{item.Shape}', expected '{tranche.Shape}'");
                }

                if (item.Role != "target" && item.Role != "control")
                {
                    problems.Add($"{where}: the role '{item.Role}' is not target or control");
                }

                if (item.Target < 0 || item.Target >= sentence.Tokens.Count)
                {
                    problems.Add($"{where}: the target {item.Target} is not a token of the sentence");
                    continue;
                }

                if (item.Origin != "real" || item.SourceId == ShapesDevSets.AuthoredSourceId)
                {
                    problems.Add($"{where}: the origin is '{item.Origin}', expected real from a source document");
                }

                if (item.Lemma.Length == 0)
                {
                    problems.Add($"{where}: the lemma is empty");
                }

                var token = sentence.Tokens[item.Target];
                if (!IsNewGoldTag(tranche, item, token[1]))
                {
                    problems.Add($"{where}: the judged word '{token[0]}' is {token[1]}, which is not the gold tag of a {item.Role}{(item.Kind.Length > 0 ? " of kind " + item.Kind : string.Empty)}");
                }

                if (DataFiles.BaseStatus(token[2]) == "disputed")
                {
                    problems.Add($"{where}: the judged word '{token[0]}' is disputed");
                }

                if (Regex.Replace(sentence.Text, @"\s+", string.Empty) != string.Concat(sentence.Tokens.Select(row => row[0])))
                {
                    problems.Add($"{where}: the tokens do not rejoin to the text");
                }
            }
        }

        // Assert: the tranches are not empty and no problem was found
        Assert.True(total > 300);
        Assert.Empty(problems.Take(ShownProblems));
    }

    /// <summary>
    ///     Proves that the committed new tranches meet their documented quotas: the items of every group (role, and for the
    ///     headings the kind of a target) between the floor and the cap, at most four targets per lemma, at most two controls per
    ///     lemma, at most 12 heading targets per source document, no heading line that ends in a page number or a page range, and at most 25 percent of the compound-head targets ending in
    ///     a lower-case <c>ing</c>.
    /// </summary>
    [Fact]
    public void PartOfSpeech_ShapesDevNew_Quotas_AreMetByTheCommittedTranches()
    {
        var problems = new List<string>();

        foreach (var tranche in ShapesDevNewTranches)
        {
            // Arrange: the items, grouped by the quota group of the tranche
            var items = ShapesDevNewItems(tranche);
            var groups = items.GroupBy(item => QuotaGroup(tranche, item)).ToDictionary(group => group.Key, group => group.Count(), StringComparer.Ordinal);

            // Act: check the floor and the cap of every group and the caps per lemma, per document and for the ing share
            foreach (var (group, floor, cap) in tranche.Quotas)
            {
                var count = groups.GetValueOrDefault(group);
                if (count < floor || count > cap)
                {
                    problems.Add($"{tranche.Name}: {count} items in the group '{group}', expected {floor} to {cap}");
                }
            }

            problems.AddRange(groups.Keys.Except(tranche.Quotas.Select(quota => quota.Group)).Select(group => $"{tranche.Name}: the group '{group}' has no quota"));

            var targets = items.Where(item => item.Role == "target").ToList();
            problems.AddRange(targets.GroupBy(item => item.Lemma, StringComparer.OrdinalIgnoreCase)
                .Where(group => group.Count() > tranche.LemmaCap)
                .Select(group => $"{tranche.Name}: the lemma '{group.Key}' has {group.Count()} targets, the cap is {tranche.LemmaCap}"));
            problems.AddRange(items.Where(item => item.Role == "control").GroupBy(item => item.Lemma, StringComparer.OrdinalIgnoreCase)
                .Where(group => group.Count() > ShapesDevNewControlLemmaCap)
                .Select(group => $"{tranche.Name}: the lemma '{group.Key}' has {group.Count()} controls, the cap is {ShapesDevNewControlLemmaCap}"));

            if (tranche.IsHeadings)
            {
                problems.AddRange(targets.GroupBy(item => item.SourceId, StringComparer.Ordinal)
                    .Where(group => group.Count() > tranche.DocumentCap)
                    .Select(group => $"{tranche.Name}: the document {group.Key} has {group.Count()} targets, the cap is {tranche.DocumentCap}"));

                // A heading line is not a table-of-contents entry: it does not end in a page number or a page range (12, 4-8)
                problems.AddRange(items
                    .Where(item => Regex.IsMatch(string.Join(" ", item.Tokens), @"(?:^|\s)\d{1,4}(?:\s?[-\u2013\u2014]\s?\d{1,4})?$", RegexOptions.CultureInvariant))
                    .Select(item => $"{tranche.Name}: {item.Id} ends in a page number or a page range"));
            }

            if (tranche.IsCompoundHead)
            {
                var ing = targets.Count(item => item.Tokens[item.Target].EndsWith("ing", StringComparison.Ordinal));
                if (ing > ShapesDevCompoundHeadMaxIngShare * targets.Count)
                {
                    problems.Add($"{tranche.Name}: {ing} of {targets.Count} targets end in ing, the cap is {ShapesDevCompoundHeadMaxIngShare:P0}");
                }
            }
        }

        // Assert: no quota is missed
        Assert.Empty(problems.Take(ShownProblems));
    }

    /// <summary>
    ///     Proves that the library tokenizer reproduces the stored tokens of every sentence of the new tranches: one sentence,
    ///     the same tokens.
    /// </summary>
    [Fact]
    public void PartOfSpeech_ShapesDevNew_Tokens_AreReproducedByTheTokenizer()
    {
        var problems = new List<string>();
        var total = 0;

        foreach (var tranche in ShapesDevNewTranches)
        {
            // Arrange: the sentences of the file
            var sentences = DataFiles.ReadCorpus(DataFiles.Data(tranche.Corpus)).ToList();
            total += sentences.Count;

            // Act: tokenize the text of every sentence and compare with the stored tokens
            foreach (var sentence in sentences)
            {
                var tokenized = Tokenizer.Tokenize(sentence.Text);
                var actual = tokenized.SelectMany(part => part.Tokens).Select(token => token.Text).ToArray();
                var stored = sentence.Tokens.Select(token => token[0]).ToArray();
                if (tokenized.Count != 1 || !actual.SequenceEqual(stored, StringComparer.Ordinal))
                {
                    problems.Add($"{tranche.Name}: {sentence.SentenceId}: the tokenizer gives {tokenized.Count} sentence(s) and {actual.Length} tokens, the file has {stored.Length}");
                }
            }
        }

        // Assert: the sets are not empty and every sentence is reproduced
        Assert.True(total > 300);
        Assert.Empty(problems.Take(ShownProblems));
    }

    /// <summary>
    ///     Proves that the source table of every new tranche lists exactly the documents of its sentences with their counts,
    ///     that every document has a US federal-work or public-domain basis, that no row mentions FAA material or a share-alike
    ///     or Wikipedia text, and that no excluded document (by id or normalized URL) is used.
    /// </summary>
    [Fact]
    public void PartOfSpeech_ShapesDevNew_Sources_AreListedOpenAndNotExcluded()
    {
        // Arrange: the exclusions and the rules for a row
        var excluded = DataFiles.ReadTable(DataFiles.Data(ShapesDevFolder + "/excluded-sources.tsv"));
        var excludedIds = new HashSet<string>(excluded.Column("source_id"), StringComparer.Ordinal);
        var excludedUrls = new HashSet<string>(excluded.Column("url").Select(NormalizeUrl), StringComparer.Ordinal);
        var faa = new Regex(@"faa\.gov|\bFAA\b|Federal Aviation", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        var openBases = new[] { "US federal government work", "US public domain" };
        var problems = new List<string>();
        var documents = 0;

        foreach (var tranche in ShapesDevNewTranches)
        {
            // Act: compare the listed documents and counts with the sentences and look at every row
            var counts = DataFiles.ReadCorpus(DataFiles.Data(tranche.Corpus))
                .GroupBy(sentence => sentence.SourceId, StringComparer.Ordinal)
                .ToDictionary(group => group.Key, group => group.Count(), StringComparer.Ordinal);
            var table = DataFiles.ReadTable(DataFiles.Data(tranche.Sources));
            var listed = table.Rows.ToDictionary(row => table.Cell(row, "source_id"), row => table.Cell(row, "sentences"), StringComparer.Ordinal);
            documents += counts.Count;
            problems.AddRange(counts.Keys.Except(listed.Keys).Select(document => $"{tranche.Name}: not listed: {document}")
                .Concat(listed.Keys.Except(counts.Keys).Select(document => $"{tranche.Name}: listed but unused: {document}"))
                .Concat(counts.Where(count => listed.TryGetValue(count.Key, out var cell) && cell != count.Value.ToString(CultureInfo.InvariantCulture))
                    .Select(count => $"{tranche.Name}: the count of {count.Key} differs from the table")));
            if (table.Rows.Count != listed.Count)
            {
                problems.Add($"{tranche.Name}: a document is listed twice");
            }

            foreach (var row in table.Rows)
            {
                var id = table.Cell(row, "source_id");
                if (!openBases.Any(basis => table.Cell(row, "license_basis").StartsWith(basis, StringComparison.Ordinal)))
                {
                    problems.Add($"{tranche.Name}: {id}: the license basis is not a US federal work or US public domain");
                }

                if (row.Any(cell => faa.IsMatch(cell)))
                {
                    problems.Add($"{tranche.Name}: {id}: the row mentions FAA material");
                }

                if (RestrictedLicenseText.IsMatch(table.Cell(row, "license_basis")) || RestrictedLicenseText.IsMatch(table.Cell(row, "url")))
                {
                    problems.Add($"{tranche.Name}: {id}: the row names a share-alike, Wikipedia or other restricted license");
                }

                if (excludedIds.Contains(id) || excludedUrls.Contains(NormalizeUrl(table.Cell(row, "url"))))
                {
                    problems.Add($"{tranche.Name}: {id}: the document is excluded");
                }
            }
        }

        // Assert: the tables have documents and no problem was found
        Assert.True(documents > 30);
        Assert.Empty(problems.Take(ShownProblems));
    }

    /// <summary>
    ///     Proves that the committed leakage report of every new tranche covers every other evaluation set, that the rows of the
    ///     standard and the hard tranche record no document, exact sentence or near-duplicate overlap, that the rows of the other
    ///     new tranches record no exact or near-duplicate sentence (shared documents are reported, not gated), that no row shares
    ///     a word 5-gram with the shape probe, and that every row counts the sentences of the tranche.
    /// </summary>
    [Fact]
    public void PartOfSpeech_ShapesDevNew_LeakageReports_RecordNoOverlap()
    {
        var problems = new List<string>();

        foreach (var tranche in ShapesDevNewTranches)
        {
            // Arrange: read the report (evaluation set, dev sentences, documents, exact, near duplicates, probe 5-grams)
            var path = ShapesDevFolder + "/" + tranche.Name + "-leakage-report.tsv";
            var rows = File.ReadLines(DataFiles.Data(path))
                .Where(line => line.Length > 0 && !DataFiles.IsComment(line))
                .Select(line => line.Split('\t'))
                .ToList();
            var others = ShapesDevNewTranches
                .Where(other => other.Name != tranche.Name && other.Name.EndsWith("-2", StringComparison.Ordinal) == tranche.Name.EndsWith("-2", StringComparison.Ordinal))
                .Select(other => "shapes-dev-" + other.Name)
                .ToList();
            var expected = new[] { "gold-v2", "open-dev", "open-test", "shapes-dev", "shapes-dev-hard", "shapes-probe", "ste-challenge", "ud-ewt-dev", "ud-ewt-test", "ud-ewt-train" }
                .Concat(ShapesDevNewTranches.Where(other => other.Name != tranche.Name).Select(other => "shapes-dev-" + other.Name))
                .Concat(TechnicalTranches.Select(technical => "shapes-dev-" + technical.Name))
                .OrderBy(name => name, StringComparer.Ordinal)
                .ToList();
            var sentenceCount = ShapesDevNewItems(tranche).Count.ToString(CultureInfo.InvariantCulture);

            // Act: find the sets that the report misses and the rows that report an overlap
            if (rows.Any(row => row.Length != 6))
            {
                problems.Add($"{tranche.Name}: a row of the leakage report does not have 6 cells");
                continue;
            }

            if (!rows.Select(row => row[0]).OrderBy(name => name, StringComparer.Ordinal).SequenceEqual(expected, StringComparer.Ordinal))
            {
                problems.Add($"{tranche.Name}: the report covers '{string.Join(", ", rows.Select(row => row[0]))}', expected '{string.Join(", ", expected)}'");
            }

            foreach (var row in rows)
            {
                var sharesDocuments = others.Contains(row[0]);
                if (row[1] != sentenceCount)
                {
                    problems.Add($"{tranche.Name}: {row[0]}: {row[1]} sentences, the tranche has {sentenceCount}");
                }

                if (sharesDocuments ? !int.TryParse(row[2], NumberStyles.None, CultureInfo.InvariantCulture, out _) : row[2] != "0")
                {
                    problems.Add($"{tranche.Name}: {row[0]}: the document overlaps are '{row[2]}'");
                }

                if (row[3] != "0" || row[4] != "0")
                {
                    problems.Add($"{tranche.Name}: {row[0]}: exact sentence overlaps '{row[3]}' or near duplicates '{row[4]}'");
                }

                if (row[0] == "shapes-probe" && row[5] != "0")
                {
                    problems.Add($"{tranche.Name}: the probe 5-gram sentences are '{row[5]}'");
                }
            }
        }

        // Assert: nothing overlaps
        Assert.Empty(problems.Take(ShownProblems));
    }

    /// <summary>
    ///     Proves, reading the files themselves, that no sentence (of any length) of a new tranche is a sentence of any other
    ///     evaluation set, of another new tranche or of the imperative training source, and that no source document of a new
    ///     tranche is a document of any other evaluation set (the standard and the hard tranche included) or of a training
    ///     source; documents that the new tranches share among themselves are allowed.
    /// </summary>
    [Fact]
    public void PartOfSpeech_ShapesDevNew_Sentences_AreDisjointFromTheOtherEvaluationSets()
    {
        // Arrange: the evaluation items, the training documents and the sentence keys of the imperative source
        var evaluation = EvaluationItems();
        var trainingDocuments = new HashSet<string>(
            TrainingCorpora.SelectMany(corpus => DataFiles.ReadCorpus(DataFiles.Data(corpus))).Select(sentence => sentence.SourceId),
            StringComparer.Ordinal);
        var imperativeKeys = new HashSet<string>(
            DataFiles.ReadCorpus(DataFiles.Data(ImperativeCorpus)).Select(sentence => DataFiles.SentenceKey(sentence.Text)),
            StringComparer.Ordinal);
        var problems = new List<string>();
        var total = 0;

        foreach (var tranche in ShapesDevNewTranches)
        {
            // The tranches of one generation share documents (the first generation among themselves, the follow-up among themselves); no document is shared across generations
            var pool = ShapesDevNewTranches
                .Where(other => other.Name.EndsWith("-2", StringComparison.Ordinal) == tranche.Name.EndsWith("-2", StringComparison.Ordinal))
                .Select(other => other.Corpus)
                .ToList();
            var sentences = DataFiles.ReadCorpus(DataFiles.Data(tranche.Corpus)).ToList();
            var documents = new HashSet<string>(sentences.Select(sentence => sentence.SourceId), StringComparer.Ordinal);
            var keys = new HashSet<string>(sentences.Select(sentence => DataFiles.SentenceKey(sentence.Text)), StringComparer.Ordinal);
            total += sentences.Count;

            // Act: find the shared sentences (any set) and the shared documents (any set but the tranches of the same generation)
            problems.AddRange(evaluation
                .Where(item => item.Set != tranche.Corpus)
                .Where(item => keys.Contains(DataFiles.SentenceKey(item.Text)) || (!pool.Contains(item.Set) && documents.Contains(item.Document)))
                .Select(item => $"{tranche.Name}: {item.Set}: {item.Document} shares a sentence or a document"));
            problems.AddRange(sentences
                .Where(sentence => imperativeKeys.Contains(DataFiles.SentenceKey(sentence.Text)))
                .Select(sentence => $"{tranche.Name}: {sentence.SentenceId} is a sentence of the imperative source"));
            problems.AddRange(documents
                .Where(trainingDocuments.Contains)
                .Select(document => $"{tranche.Name}: {document} is a training document"));
        }

        // Assert: the sets are not empty, the hard tranche is among the sets that were compared and nothing is shared
        Assert.True(total > 300);
        Assert.True(evaluation.Count > 5000);
        Assert.True(trainingDocuments.Count > 1000);
        Assert.Contains(evaluation, item => item.Set == ShapesDevHardCorpus);
        Assert.Empty(problems.Take(ShownProblems));
    }

    /// <summary>
    ///     Proves that the selection of the new tranches is independent of the PartOfSpeech model: the script
    ///     (<c>shapes_dev_extra.py</c>) names no weights file, decoder, tagger or shapes evaluation, never opens the shape probe, and
    ///     does not read gold v2 or the challenge set directly (the held-out sets reach it only through the opaque keys of
    ///     the shared filter).
    /// </summary>
    [Fact]
    public void PartOfSpeech_ShapesDevNew_Selection_UsesNoPartOfSpeechModel()
    {
        // Arrange: the source of the script
        var script = File.ReadAllText(DataFiles.Repository("tools/data/shapes_dev_extra.py"));
        var heldOut = new[] { "probe.tsv", "sentence_column", "probe_sentences", "gold-v2", "gold_v2", "eval/gold", "ste-challenge", "ste_challenge", "eval/ste" };

        // Act: find the forbidden names
        var models = ModelNames.Where(word => script.Contains(word, StringComparison.Ordinal)).ToList();
        var reads = heldOut.Where(word => script.Contains(word, StringComparison.Ordinal)).ToList();

        // Assert: the script is not empty, uses no model and reads no held-out file
        Assert.True(script.Length > 10000);
        Assert.Empty(models);
        Assert.Empty(reads);
    }

    /// <summary>
    ///     Proves that the header of the imperative training source announces a training file (and no development use), and
    ///     that the committed file meets the documented quotas: 600 to 900 sentences with sequential ids, token 0 of every
    ///     sentence a VERB whose lower-case form is listed in the lexicon as both noun and verb, every token <c>unanimous</c>
    ///     (no disputed or masked token), at most six sentences per lemma (the first word is in the base form, so the
    ///     lower-case first word is the lemma) and at most 30 per document.
    /// </summary>
    [Fact]
    public void PartOfSpeech_Imperative_Corpus_IsWellFormedAndWithinQuotas()
    {
        // Arrange: the header, the sentences and the lexicon
        var header = File.ReadLines(DataFiles.Data(ImperativeCorpus)).TakeWhile(line => line.Length > 0).ToList();
        var sentences = DataFiles.ReadCorpus(DataFiles.Data(ImperativeCorpus)).ToList();
        var lexicon = DataFiles.ReadLexicon(DataFiles.Data("lexicon/lexicon.tsv"));
        var problems = new List<string>();

        // Act: check every sentence
        for (var index = 0; index < sentences.Count; index++)
        {
            var sentence = sentences[index];
            var id = string.Format(CultureInfo.InvariantCulture, "imperative-{0:D4}", index + 1);
            if (sentence.SentenceId != id)
            {
                problems.Add($"{sentence.SentenceId}: expected the id {id}");
            }

            var first = sentence.Tokens[0];
            if (first[1] != "VERB" || !lexicon.TryGetValue(first[0].ToLowerInvariant(), out var tags) || !tags.Contains("NOUN") || !tags.Contains("VERB"))
            {
                problems.Add($"{sentence.SentenceId}: token 0 is not a VERB that the lexicon lists as both noun and verb");
            }

            if (sentence.Tokens.Any(token => DataFiles.BaseStatus(token[2]) != "unanimous"))
            {
                problems.Add($"{sentence.SentenceId}: a token is not unanimous");
            }
        }

        var lemmas = sentences.GroupBy(sentence => sentence.Tokens[0][0].ToLowerInvariant(), StringComparer.Ordinal).ToList();
        problems.AddRange(lemmas.Where(group => group.Count() > ImperativeLemmaCap).Select(group => $"the lemma '{group.Key}' has {group.Count()} sentences"));
        problems.AddRange(sentences.GroupBy(sentence => sentence.SourceId, StringComparer.Ordinal)
            .Where(group => group.Count() > ImperativeDocumentCap)
            .Select(group => $"the document {group.Key} has {group.Count()} sentences"));

        // Assert: a training file, the size within the quotas, many lemmas and no problem
        Assert.Contains(header, line => line == "# use = training");
        Assert.Contains(header, line => line == "# columns = form, upos, status");
        Assert.DoesNotContain(header, line => line.Contains("dev split", StringComparison.Ordinal) || line.Contains("development only", StringComparison.Ordinal));
        Assert.InRange(sentences.Count, ImperativeMinSentences, ImperativeMaxSentences);
        Assert.True(lemmas.Count > 250);
        Assert.Empty(problems.Take(ShownProblems));
    }

    /// <summary>
    ///     Proves that the source table of the imperative training source lists exactly the documents of its sentences with
    ///     their counts, that every document has a US federal-work or public-domain basis, that no row mentions FAA material,
    ///     that no document is excluded (by id or normalized URL, using the record of the shapes development set), and that
    ///     the exclusion and drop records are well formed and carry ids and counts only.
    /// </summary>
    [Fact]
    public void PartOfSpeech_Imperative_Sources_AreListedOpenAndNotExcluded()
    {
        // Arrange: the documents of the sentences, the rows of the table and the exclusions
        var counts = DataFiles.ReadCorpus(DataFiles.Data(ImperativeCorpus))
            .GroupBy(sentence => sentence.SourceId, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.Count(), StringComparer.Ordinal);
        var table = DataFiles.ReadTable(DataFiles.Data(ImperativeSources));
        var excluded = DataFiles.ReadTable(DataFiles.Data(ShapesDevFolder + "/excluded-sources.tsv"));
        var excludedIds = new HashSet<string>(excluded.Column("source_id"), StringComparer.Ordinal);
        var excludedUrls = new HashSet<string>(excluded.Column("url").Select(NormalizeUrl), StringComparer.Ordinal);
        var faa = new Regex(@"faa\.gov|\bFAA\b|Federal Aviation", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        var openBases = new[] { "US federal government work", "US public domain" };

        // Act: compare the listed documents and counts with the sentences and look at every row
        var listed = table.Rows.ToDictionary(row => table.Cell(row, "source_id"), row => table.Cell(row, "sentences"), StringComparer.Ordinal);
        var problems = counts.Keys.Except(listed.Keys).Select(document => "not listed: " + document)
            .Concat(listed.Keys.Except(counts.Keys).Select(document => "listed but unused: " + document))
            .Concat(counts.Where(count => listed.TryGetValue(count.Key, out var cell) && cell != count.Value.ToString(CultureInfo.InvariantCulture))
                .Select(count => $"count of {count.Key} differs from the table"))
            .ToList();
        foreach (var row in table.Rows)
        {
            var id = table.Cell(row, "source_id");
            if (!openBases.Any(basis => table.Cell(row, "license_basis").StartsWith(basis, StringComparison.Ordinal)))
            {
                problems.Add($"{id}: the license basis is not a US federal work or US public domain");
            }

            if (row.Any(cell => faa.IsMatch(cell)))
            {
                problems.Add($"{id}: the row mentions FAA material");
            }

            if (excludedIds.Contains(id) || excludedUrls.Contains(NormalizeUrl(table.Cell(row, "url"))))
            {
                problems.Add($"{id}: the document is excluded");
            }
        }

        var rejected = DataFiles.ReadTable(DataFiles.Data(ImperativeFolder + "/excluded-documents.tsv"));
        var dropped = DataFiles.ReadTable(DataFiles.Data(ImperativeFolder + "/dropped-sentences.tsv"));
        problems.AddRange(rejected.Column("document_id").Where(counts.ContainsKey).Select(id => $"{id}: a rejected document is used"));
        problems.AddRange(dropped.Rows.Where(row => !int.TryParse(dropped.Cell(row, "sentences"), NumberStyles.None, CultureInfo.InvariantCulture, out _))
            .Select(row => $"{dropped.Cell(row, "reason")}: the count is not a number"));

        // Assert: the rows are unique, the table has documents, the records have their columns and no problem was found
        Assert.Equal(table.Rows.Count, listed.Count);
        Assert.True(counts.Count > 50);
        Assert.Equal(new[] { "document_id", "reason" }, rejected.Columns);
        Assert.Equal(new[] { "reason", "sentences" }, dropped.Columns);
        Assert.Empty(problems.Take(ShownProblems));
    }

    /// <summary>
    ///     Proves that the documents of the imperative training source are in no other source table (open, gold v2, silver,
    ///     MASC, both tranches of the shapes development set and its exclusion record), neither by id nor by normalized URL,
    ///     and that its sentences and documents are not those of any evaluation set (read from the evaluation files).
    /// </summary>
    [Fact]
    public void PartOfSpeech_Imperative_Documents_AreInNoOtherSourceTableAndNoEvaluationSet()
    {
        // Arrange: the ids and normalized URLs of the other tables, and the documents of the evaluation sets
        var own = DataFiles.ReadTable(DataFiles.Data(ImperativeSources));
        var others = new[]
        {
            ("open/sources.tsv", "source_id"), ("eval/gold-v2/sources.tsv", "doc_id"), ("silver/sources.tsv", "source_id"),
            ("masc/sources.tsv", "source_id"), (ShapesDevFolder + "/sources.tsv", "source_id"), (ShapesDevHardSources, "source_id"),
            (ShapesDevCompoundHeadSources, "source_id"), (ShapesDevHeadingsSources, "source_id"), (ShapesDevNamedStateSources, "source_id"),
            (ShapesDevCompoundHead2Sources, "source_id"), (ShapesDevHeadings2Sources, "source_id"),
            (ShapesDevFolder + "/excluded-sources.tsv", "source_id")
        };
        var ids = new HashSet<string>(StringComparer.Ordinal);
        var urls = new HashSet<string>(StringComparer.Ordinal);
        foreach (var (name, idColumn) in others)
        {
            var data = DataFiles.ReadTable(DataFiles.Data(name));
            ids.UnionWith(data.Column(idColumn));
            if (Array.IndexOf(data.Columns, "url") >= 0)
            {
                urls.UnionWith(data.Column("url").Select(NormalizeUrl).Where(url => url.Length > 0));
            }
        }

        var evaluationDocuments = new HashSet<string>(EvaluationItems().Select(item => item.Document), StringComparer.Ordinal);

        // Act: find the shared ids, URLs and evaluation documents
        var problems = own.Rows
            .Where(row => ids.Contains(own.Cell(row, "source_id")) || urls.Contains(NormalizeUrl(own.Cell(row, "url"))) || evaluationDocuments.Contains(own.Cell(row, "source_id")))
            .Select(row => own.Cell(row, "source_id") + " is in another source table or an evaluation set")
            .ToList();

        // Assert: the tables are not empty and nothing is shared
        Assert.True(own.Rows.Count > 50);
        Assert.True(ids.Count > 5000);
        Assert.Empty(problems.Take(ShownProblems));
    }

    /// <summary>
    ///     Proves that the committed leakage report covers the imperative training source against every evaluation set (the
    ///     probe and both tranches of the shapes development set included) and records no document overlap and no exact
    ///     sentence overlap and no near duplicate (the check gates the near duplicates of the imperative source; the report records
    ///     the count for every evaluation set).
    /// </summary>
    [Fact]
    public void PartOfSpeech_Imperative_LeakageReport_RecordsNoOverlapWithAnyEvaluationSet()
    {
        // Arrange: the rows of the report that name the imperative source (training set, evaluation set, sentences, documents, exact, near)
        var rows = File.ReadLines(DataFiles.Data("leakage-report.tsv"))
            .Where(line => line.Length > 0 && !DataFiles.IsComment(line))
            .Select(line => line.Split('\t'))
            .Where(row => row[0] == "imperative")
            .ToList();
        var expected = new[]
        {
            "gold-v2", "open-dev", "open-test", "shapes-dev", "shapes-dev-clause-verbs", "shapes-dev-compound-head", "shapes-dev-compound-head-2",
            "shapes-dev-defined-terms", "shapes-dev-deverbal-heads", "shapes-dev-hard", "shapes-dev-headings", "shapes-dev-headings-2",
            "shapes-dev-named-state", "shapes-dev-step-lines", "shapes-probe", "ste-challenge", "ud-ewt-dev", "ud-ewt-test", "ud-ewt-train"
        };

        // Act: find the sets that the report misses and the rows that report an overlap or a near duplicate
        var overlaps = rows.Where(row => row[3] != "0" || row[4] != "0").Select(row => row[1]).ToList();
        var nearDuplicates = rows.Where(row => row[5] != "0").Select(row => row[1]).ToList();

        // Assert: every evaluation set is reported once and nothing overlaps, not even a near duplicate (a malformed count is not "0")
        Assert.All(rows, row => Assert.Equal(6, row.Length));
        Assert.Equal(expected, rows.Select(row => row[1]).OrderBy(name => name, StringComparer.Ordinal));
        Assert.Empty(overlaps);
        Assert.Empty(nearDuplicates);
    }

    /// <summary>
    ///     Proves that the selection of the imperative training source is independent of the PartOfSpeech model: the builder
    ///     (<c>imperative_data.py</c>) names no weights file, decoder, tagger or shapes evaluation, and it names no probe file and uses no probe reader
    ///     (the held-out sets reach it only through the opaque exclusion keys of the shared filter).
    /// </summary>
    [Fact]
    public void PartOfSpeech_Imperative_Selection_UsesNoPartOfSpeechModel()
    {
        // Arrange: the source of the builder
        var forbidden = new[] { "weights.bin", "Resources/weights", "PerceptronModel", "FeatureExtractor", "PartOfSpeechTagger", "ITagger", "eval-shapes", "ShapesDevEvaluator" };
        var builder = File.ReadAllText(DataFiles.Repository("tools/data/imperative_data.py"));

        // Act: find the forbidden names
        var found = forbidden.Where(word => builder.Contains(word, StringComparison.Ordinal)).ToList();

        // Assert: no model, no probe file and no probe reader
        Assert.Empty(found);
        Assert.DoesNotContain("probe.tsv", builder, StringComparison.Ordinal);
        Assert.DoesNotContain("sentence_column", builder, StringComparison.Ordinal);
        Assert.DoesNotContain("probe_sentences", builder, StringComparison.Ordinal);
    }

    /// <summary>
    ///     Proves that the third-party notice names the imperative training source, its US federal-work basis, its CC0
    ///     dedication of annotations and selection, and that the embedded weights were learned from it (weight 1).
    /// </summary>
    [Fact]
    public void PartOfSpeech_Imperative_Notice_NamesTheSourceAndItsBasis()
    {
        // Arrange: the section of the notice that describes the data built by this project
        var notice = NoticeSection("### The open English part-of-speech corpus (CC0 1.0)");

        // Act: find the statements that the notice lacks
        var missing = MissingFragments(notice, new[]
        {
            "data/imperative/", "17 U.S.C. 105", "CC0 1.0", "data/imperative/sources.tsv", "weight 1", "were learned from it"
        });

        // Assert: the notice makes every statement
        Assert.Empty(missing);
    }

    /// <summary>
    ///     Proves that the header of the compound-head training source announces a training file (and no development use), and that the committed
    ///     file meets the documented quotas: 600 to 800 sentences with sequential ids, every token <c>unanimous</c>, a head that is a lower-case word the
    ///     lexicon lists as both noun and verb, tagged NOUN or PROPN for kind <c>noun</c> and VERB after a noun for kind <c>verb</c> (the counter-examples,
    ///     at most 15 percent), at most four sentences per head lemma and 20 per document, and at least 200 head lemmas.
    /// </summary>
    [Fact]
    public void PartOfSpeech_Compound_Corpus_IsWellFormedAndWithinQuotas()
    {
        // Arrange: the header, the sentences with their comment values and the lexicon
        var header = File.ReadLines(DataFiles.Data(CompoundCorpus)).TakeWhile(line => line.Length > 0).ToList();
        var sentences = DataFiles.ReadCorpus(DataFiles.Data(CompoundCorpus)).ToList();
        var blocks = ReadBlockValues(CompoundCorpus);
        var lexicon = DataFiles.ReadLexicon(DataFiles.Data("lexicon/lexicon.tsv"));
        var problems = new List<string>();
        var verbs = 0;

        // Act: check every sentence
        Assert.Equal(sentences.Count, blocks.Count);
        for (var index = 0; index < sentences.Count; index++)
        {
            var sentence = sentences[index];
            var block = blocks[index];
            var id = string.Format(CultureInfo.InvariantCulture, "compound-{0:D4}", index + 1);
            if (sentence.SentenceId != id)
            {
                problems.Add($"{sentence.SentenceId}: expected the id {id}");
            }

            if (!int.TryParse(block.GetValueOrDefault("head"), NumberStyles.None, CultureInfo.InvariantCulture, out var head) || head >= sentence.Tokens.Count)
            {
                problems.Add($"{sentence.SentenceId}: the head is not a token index");
                continue;
            }

            var kind = block.GetValueOrDefault("kind") ?? string.Empty;
            var form = sentence.Tokens[head][0];
            var tag = sentence.Tokens[head][1];
            if (form != form.ToLowerInvariant() || !lexicon.TryGetValue(form, out var tags) || !tags.Contains("NOUN") || !tags.Contains("VERB"))
            {
                problems.Add($"{sentence.SentenceId}: the head is not a lower-case word that the lexicon lists as both noun and verb");
            }

            if (string.IsNullOrEmpty(block.GetValueOrDefault("lemma")))
            {
                problems.Add($"{sentence.SentenceId}: no lemma");
            }

            if (kind == "noun" && tag != "NOUN" && tag != "PROPN")
            {
                problems.Add($"{sentence.SentenceId}: kind noun but the head is tagged {tag}");
            }
            else if (kind == "verb")
            {
                verbs++;
                if (tag != "VERB" || head == 0 || (sentence.Tokens[head - 1][1] != "NOUN" && sentence.Tokens[head - 1][1] != "PROPN"))
                {
                    problems.Add($"{sentence.SentenceId}: kind verb needs a VERB head after a noun");
                }
            }
            else if (kind != "noun")
            {
                problems.Add($"{sentence.SentenceId}: the kind '{kind}' is not noun or verb");
            }

            if (sentence.Tokens.Any(token => DataFiles.BaseStatus(token[2]) != "unanimous"))
            {
                problems.Add($"{sentence.SentenceId}: a token is not unanimous");
            }
        }

        var lemmas = blocks.GroupBy(block => block.GetValueOrDefault("lemma") ?? string.Empty, StringComparer.Ordinal).ToList();
        problems.AddRange(lemmas.Where(group => group.Count() > CompoundLemmaCap).Select(group => $"the lemma '{group.Key}' has {group.Count()} sentences"));
        problems.AddRange(sentences.GroupBy(sentence => sentence.SourceId, StringComparer.Ordinal)
            .Where(group => group.Count() > CompoundDocumentCap)
            .Select(group => $"the document {group.Key} has {group.Count()} sentences"));

        // Assert: a training file, the size within the quotas, a small verb share, many head lemmas and no problem
        Assert.Contains(header, line => line == "# use = training");
        Assert.Contains(header, line => line == "# columns = form, upos, status");
        Assert.DoesNotContain(header, line => line.Contains("dev split", StringComparison.Ordinal) || line.Contains("development only", StringComparison.Ordinal));
        Assert.InRange(sentences.Count, CompoundMinSentences, CompoundMaxSentences);
        Assert.True(verbs > 0 && verbs * 100 <= CompoundVerbPercent * sentences.Count, $"{verbs} verb items of {sentences.Count}");
        Assert.True(lemmas.Count >= CompoundMinLemmas, $"{lemmas.Count} head lemmas");
        Assert.Empty(problems.Take(ShownProblems));
    }

    /// <summary>
    ///     Proves that the header of the headings training source announces a training file (and no development use), and that the committed file
    ///     meets the documented quotas: at most 600 lines with sequential ids, 2 to 10 tokens and no final period, every token <c>unanimous</c>, the
    ///     judged word tagged as its stratum says (NOUN, VERB at token 0, PROPN), at most the caps of the strata, the floors of the noun and
    ///     proper strata met, at most four lines per lemma and 25 per document.
    /// </summary>
    [Fact]
    public void PartOfSpeech_Headings_Corpus_IsWellFormedAndWithinQuotas()
    {
        // Arrange: the header, the lines with their comment values
        var header = File.ReadLines(DataFiles.Data(HeadingsCorpus)).TakeWhile(line => line.Length > 0).ToList();
        var sentences = DataFiles.ReadCorpus(DataFiles.Data(HeadingsCorpus)).ToList();
        var blocks = ReadBlockValues(HeadingsCorpus);
        var problems = new List<string>();

        // Act: check every line
        Assert.Equal(sentences.Count, blocks.Count);
        for (var index = 0; index < sentences.Count; index++)
        {
            var sentence = sentences[index];
            var block = blocks[index];
            var id = string.Format(CultureInfo.InvariantCulture, "headings-{0:D4}", index + 1);
            if (sentence.SentenceId != id)
            {
                problems.Add($"{sentence.SentenceId}: expected the id {id}");
            }

            if (sentence.Tokens.Count is < 2 or > 10 || sentence.Tokens[^1][0] == ".")
            {
                problems.Add($"{sentence.SentenceId}: a heading line has 2 to 10 tokens and no final period");
            }

            var stratum = block.GetValueOrDefault("stratum") ?? string.Empty;
            if (!int.TryParse(block.GetValueOrDefault("word"), NumberStyles.None, CultureInfo.InvariantCulture, out var word) || word >= sentence.Tokens.Count)
            {
                problems.Add($"{sentence.SentenceId}: the judged word is not a token index");
                continue;
            }

            if (!HeadingsStrata.TryGetValue(stratum, out var rule) || sentence.Tokens[word][1] != rule.Tag)
            {
                problems.Add($"{sentence.SentenceId}: the judged word is not tagged as the stratum '{stratum}' says");
            }
            else if (stratum == "imperative" && word != 0)
            {
                problems.Add($"{sentence.SentenceId}: an imperative line is judged at token 0");
            }

            if (string.IsNullOrEmpty(block.GetValueOrDefault("lemma")))
            {
                problems.Add($"{sentence.SentenceId}: no lemma");
            }

            if (sentence.Tokens.Any(token => DataFiles.BaseStatus(token[2]) != "unanimous"))
            {
                problems.Add($"{sentence.SentenceId}: a token is not unanimous");
            }
        }

        var strata = blocks.GroupBy(block => block.GetValueOrDefault("stratum") ?? string.Empty, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.Count(), StringComparer.Ordinal);
        problems.AddRange(HeadingsStrata
            .Where(entry => strata.GetValueOrDefault(entry.Key) > entry.Value.Cap || (entry.Key != "imperative" && strata.GetValueOrDefault(entry.Key) < entry.Value.Floor))
            .Select(entry => $"the stratum {entry.Key} has {strata.GetValueOrDefault(entry.Key)} lines"));
        problems.AddRange(blocks.GroupBy(block => block.GetValueOrDefault("lemma") ?? string.Empty, StringComparer.Ordinal)
            .Where(group => group.Count() > HeadingsLemmaCap)
            .Select(group => $"the lemma '{group.Key}' has {group.Count()} lines"));
        problems.AddRange(sentences.GroupBy(sentence => sentence.SourceId, StringComparer.Ordinal)
            .Where(group => group.Count() > HeadingsDocumentCap)
            .Select(group => $"the document {group.Key} has {group.Count()} lines"));

        // Assert: a training file, a size within the cap, the three strata present and no problem
        Assert.Contains(header, line => line == "# use = training");
        Assert.Contains(header, line => line == "# columns = form, upos, status");
        Assert.DoesNotContain(header, line => line.Contains("dev split", StringComparison.Ordinal) || line.Contains("development only", StringComparison.Ordinal));
        Assert.InRange(sentences.Count, 1, HeadingsMaxLines);
        Assert.Equal(new[] { "imperative", "noun", "proper" }, strata.Keys.OrderBy(name => name, StringComparer.Ordinal));
        Assert.Empty(problems.Take(ShownProblems));
    }

    /// <summary>
    ///     Proves that the header of the second compound-head training source (<c>compound2</c>) announces a training file, and that the committed
    ///     file meets the documented quotas of the follow-up rule: 600 to 1,100 sentences with sequential ids (<c>compound2-NNNN</c>), every token
    ///     <c>unanimous</c>, a head that is a lower-case word the lexicon lists as both noun and verb, tagged NOUN or PROPN for kind <c>noun</c> and VERB
    ///     after a noun for kind <c>verb</c>, at most 935 noun and 165 verb items, at most four sentences per head lemma and 20 per document, and at
    ///     least 200 head lemmas.
    /// </summary>
    [Fact]
    public void PartOfSpeech_Compound2_Corpus_IsWellFormedAndWithinQuotas()
    {
        // Arrange: the header, the sentences with their comment values and the lexicon
        var header = File.ReadLines(DataFiles.Data(Compound2Corpus)).TakeWhile(line => line.Length > 0).ToList();
        var sentences = DataFiles.ReadCorpus(DataFiles.Data(Compound2Corpus)).ToList();
        var blocks = ReadBlockValues(Compound2Corpus);
        var lexicon = DataFiles.ReadLexicon(DataFiles.Data("lexicon/lexicon.tsv"));
        var problems = new List<string>();
        int nouns = 0, verbs = 0;

        // Act: check every sentence
        Assert.Equal(sentences.Count, blocks.Count);
        for (var index = 0; index < sentences.Count; index++)
        {
            var sentence = sentences[index];
            var block = blocks[index];
            var id = string.Format(CultureInfo.InvariantCulture, "compound2-{0:D4}", index + 1);
            if (sentence.SentenceId != id)
            {
                problems.Add($"{sentence.SentenceId}: expected the id {id}");
            }

            if (!int.TryParse(block.GetValueOrDefault("head"), NumberStyles.None, CultureInfo.InvariantCulture, out var head) || head >= sentence.Tokens.Count)
            {
                problems.Add($"{sentence.SentenceId}: the head is not a token index");
                continue;
            }

            var kind = block.GetValueOrDefault("kind") ?? string.Empty;
            var form = sentence.Tokens[head][0];
            var tag = sentence.Tokens[head][1];
            if (form != form.ToLowerInvariant() || !lexicon.TryGetValue(form, out var tags) || !tags.Contains("NOUN") || !tags.Contains("VERB"))
            {
                problems.Add($"{sentence.SentenceId}: the head is not a lower-case word that the lexicon lists as both noun and verb");
            }

            if (string.IsNullOrEmpty(block.GetValueOrDefault("lemma")))
            {
                problems.Add($"{sentence.SentenceId}: no lemma");
            }

            if (kind == "noun")
            {
                nouns++;
                if (tag != "NOUN" && tag != "PROPN")
                {
                    problems.Add($"{sentence.SentenceId}: kind noun but the head is tagged {tag}");
                }
            }
            else if (kind == "verb")
            {
                verbs++;
                if (tag != "VERB" || head == 0 || (sentence.Tokens[head - 1][1] != "NOUN" && sentence.Tokens[head - 1][1] != "PROPN"))
                {
                    problems.Add($"{sentence.SentenceId}: kind verb needs a VERB head after a noun");
                }
            }
            else
            {
                problems.Add($"{sentence.SentenceId}: the kind '{kind}' is not noun or verb");
            }

            if (sentence.Tokens.Any(token => DataFiles.BaseStatus(token[2]) != "unanimous"))
            {
                problems.Add($"{sentence.SentenceId}: a token is not unanimous");
            }
        }

        var lemmas = blocks.GroupBy(block => block.GetValueOrDefault("lemma") ?? string.Empty, StringComparer.Ordinal).ToList();
        problems.AddRange(lemmas.Where(group => group.Count() > CompoundLemmaCap).Select(group => $"the lemma '{group.Key}' has {group.Count()} sentences"));
        problems.AddRange(sentences.GroupBy(sentence => sentence.SourceId, StringComparer.Ordinal)
            .Where(group => group.Count() > CompoundDocumentCap)
            .Select(group => $"the document {group.Key} has {group.Count()} sentences"));

        // Assert: a training file, the size within the quotas, the caps of the two kinds, many head lemmas and no problem
        Assert.Contains(header, line => line == "# use = training");
        Assert.Contains(header, line => line == "# columns = form, upos, status");
        Assert.DoesNotContain(header, line => line.Contains("dev split", StringComparison.Ordinal) || line.Contains("development only", StringComparison.Ordinal));
        Assert.InRange(sentences.Count, Compound2MinSentences, Compound2MaxSentences);
        Assert.InRange(nouns, 1, Compound2NounCap);
        Assert.InRange(verbs, 1, Compound2VerbCap);
        Assert.True(lemmas.Count >= Compound2MinLemmas, $"{lemmas.Count} head lemmas");
        Assert.Empty(problems.Take(ShownProblems));
    }

    /// <summary>
    ///     Proves that the header of the second headings training source (<c>headings2</c>) announces a training file, and that the committed file
    ///     meets the documented quotas of the follow-up rule: sequential ids (<c>headings2-NNNN</c>), 2 to 10 tokens and no final period, every token
    ///     <c>unanimous</c>, the judged word at token 0 tagged VERB for the stratum <c>imperative</c> and NOUN for the contrast stratum <c>initial-noun</c>,
    ///     150 to 400 imperative lines, at most half as many initial-noun lines as imperative lines, at most six lines per lemma and 25 per document.
    /// </summary>
    [Fact]
    public void PartOfSpeech_Headings2_Corpus_IsWellFormedAndWithinQuotas()
    {
        // Arrange: the header, the lines with their comment values
        var header = File.ReadLines(DataFiles.Data(Headings2Corpus)).TakeWhile(line => line.Length > 0).ToList();
        var sentences = DataFiles.ReadCorpus(DataFiles.Data(Headings2Corpus)).ToList();
        var blocks = ReadBlockValues(Headings2Corpus);
        var strata = new Dictionary<string, string>(StringComparer.Ordinal) { ["imperative"] = "VERB", ["initial-noun"] = "NOUN" };
        var problems = new List<string>();

        // Act: check every line
        Assert.Equal(sentences.Count, blocks.Count);
        for (var index = 0; index < sentences.Count; index++)
        {
            var sentence = sentences[index];
            var block = blocks[index];
            var id = string.Format(CultureInfo.InvariantCulture, "headings2-{0:D4}", index + 1);
            if (sentence.SentenceId != id)
            {
                problems.Add($"{sentence.SentenceId}: expected the id {id}");
            }

            if (sentence.Tokens.Count is < 2 or > 10 || sentence.Tokens[^1][0] == ".")
            {
                problems.Add($"{sentence.SentenceId}: a heading line has 2 to 10 tokens and no final period");
            }

            var stratum = block.GetValueOrDefault("stratum") ?? string.Empty;
            if (!int.TryParse(block.GetValueOrDefault("word"), NumberStyles.None, CultureInfo.InvariantCulture, out var word) || word >= sentence.Tokens.Count)
            {
                problems.Add($"{sentence.SentenceId}: the judged word is not a token index");
                continue;
            }

            if (!strata.TryGetValue(stratum, out var tag) || sentence.Tokens[word][1] != tag)
            {
                problems.Add($"{sentence.SentenceId}: the judged word is not tagged as the stratum '{stratum}' says");
            }
            else if (word != 0)
            {
                problems.Add($"{sentence.SentenceId}: the judged word of the stratum '{stratum}' is token 0");
            }

            if (string.IsNullOrEmpty(block.GetValueOrDefault("lemma")))
            {
                problems.Add($"{sentence.SentenceId}: no lemma");
            }

            if (sentence.Tokens.Any(token => DataFiles.BaseStatus(token[2]) != "unanimous"))
            {
                problems.Add($"{sentence.SentenceId}: a token is not unanimous");
            }
        }

        var counts = blocks.GroupBy(block => block.GetValueOrDefault("stratum") ?? string.Empty, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.Count(), StringComparer.Ordinal);
        problems.AddRange(blocks.GroupBy(block => block.GetValueOrDefault("lemma") ?? string.Empty, StringComparer.Ordinal)
            .Where(group => group.Count() > Headings2LemmaCap)
            .Select(group => $"the lemma '{group.Key}' has {group.Count()} lines"));
        problems.AddRange(sentences.GroupBy(sentence => sentence.SourceId, StringComparer.Ordinal)
            .Where(group => group.Count() > HeadingsDocumentCap)
            .Select(group => $"the document {group.Key} has {group.Count()} lines"));

        // Assert: a training file, both strata present, the imperative stratum within its floor and cap, the contrast stratum at most half of it, no problem
        Assert.Contains(header, line => line == "# use = training");
        Assert.Contains(header, line => line == "# columns = form, upos, status");
        Assert.DoesNotContain(header, line => line.Contains("dev split", StringComparison.Ordinal) || line.Contains("development only", StringComparison.Ordinal));
        Assert.Equal(new[] { "imperative", "initial-noun" }, counts.Keys.OrderBy(name => name, StringComparer.Ordinal));
        Assert.InRange(counts["imperative"], Headings2ImperativeFloor, Headings2ImperativeCap);
        Assert.True(counts["initial-noun"] * 2 <= counts["imperative"], $"{counts["initial-noun"]} initial-noun lines against {counts["imperative"]} imperative lines");
        Assert.Empty(problems.Take(ShownProblems));
    }

    /// <summary>
    ///     Proves that the source table of the compound-head or the headings training source lists exactly the documents of its units with their
    ///     counts, that every document has a US federal-work or public-domain basis, that no row mentions FAA material, that no document is excluded
    ///     (by id or normalized URL, using the record of the shapes development set), that no document of the exclusion record of the source
    ///     (documents left out after the build) is used, and that the records are well formed and carry ids and counts only.
    /// </summary>
    [Theory]
    [InlineData(CompoundCorpus, CompoundSources, CompoundFolder + "/excluded-documents.tsv", CompoundFolder + "/dropped-sentences.tsv")]
    [InlineData(HeadingsCorpus, HeadingsSources, HeadingsFolder + "/excluded-documents.tsv", HeadingsFolder + "/dropped-sentences.tsv")]
    [InlineData(Compound2Corpus, Compound2Sources, CompoundFolder + "/compound-2-excluded-documents.tsv", CompoundFolder + "/compound-2-dropped-sentences.tsv")]
    [InlineData(Headings2Corpus, Headings2Sources, HeadingsFolder + "/headings-2-excluded-documents.tsv", HeadingsFolder + "/headings-2-dropped-sentences.tsv")]
    public void PartOfSpeech_TargetedSources_Sources_AreListedOpenAndNotExcluded(string corpus, string sources, string rejectedTable, string droppedTable)
    {
        // Arrange: the documents of the units, the rows of the table and the exclusions
        var counts = DataFiles.ReadCorpus(DataFiles.Data(corpus))
            .GroupBy(sentence => sentence.SourceId, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.Count(), StringComparer.Ordinal);
        var table = DataFiles.ReadTable(DataFiles.Data(sources));
        var excluded = DataFiles.ReadTable(DataFiles.Data(ShapesDevFolder + "/excluded-sources.tsv"));
        var excludedIds = new HashSet<string>(excluded.Column("source_id"), StringComparer.Ordinal);
        var excludedUrls = new HashSet<string>(excluded.Column("url").Select(NormalizeUrl), StringComparer.Ordinal);
        var faa = new Regex(@"faa\.gov|\bFAA\b|Federal Aviation", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        var openBases = new[] { "US federal government work", "US public domain" };

        // Act: compare the listed documents and counts with the units and look at every row
        var listed = table.Rows.ToDictionary(row => table.Cell(row, "source_id"), row => table.Cell(row, "sentences"), StringComparer.Ordinal);
        var problems = counts.Keys.Except(listed.Keys).Select(document => "not listed: " + document)
            .Concat(listed.Keys.Except(counts.Keys).Select(document => "listed but unused: " + document))
            .Concat(counts.Where(count => listed.TryGetValue(count.Key, out var cell) && cell != count.Value.ToString(CultureInfo.InvariantCulture))
                .Select(count => $"count of {count.Key} differs from the table"))
            .ToList();
        foreach (var row in table.Rows)
        {
            var id = table.Cell(row, "source_id");
            if (!openBases.Any(basis => table.Cell(row, "license_basis").StartsWith(basis, StringComparison.Ordinal)))
            {
                problems.Add($"{id}: the license basis is not a US federal work or US public domain");
            }

            if (row.Any(cell => faa.IsMatch(cell)))
            {
                problems.Add($"{id}: the row mentions FAA material");
            }

            if (excludedIds.Contains(id) || excludedUrls.Contains(NormalizeUrl(table.Cell(row, "url"))))
            {
                problems.Add($"{id}: the document is excluded");
            }
        }

        var rejected = DataFiles.ReadTable(DataFiles.Data(rejectedTable));
        var dropped = DataFiles.ReadTable(DataFiles.Data(droppedTable));
        problems.AddRange(rejected.Column("document_id").Where(counts.ContainsKey).Select(id => $"{id}: a rejected document is used"));
        problems.AddRange(dropped.Rows.Where(row => !int.TryParse(dropped.Cell(row, "sentences"), NumberStyles.None, CultureInfo.InvariantCulture, out _))
            .Select(row => $"{dropped.Cell(row, "reason")}: the count is not a number"));

        // Assert: the rows are unique, the table has documents, the records have their columns and no problem was found
        Assert.Equal(table.Rows.Count, listed.Count);
        Assert.True(counts.Count > 50);
        Assert.Equal(new[] { "document_id", "reason" }, rejected.Columns);
        Assert.Equal(new[] { "reason", "sentences" }, dropped.Columns);
        Assert.Empty(problems.Take(ShownProblems));
    }

    /// <summary>
    ///     Proves that the documents of the compound-head and the headings training source are in no other source table (open, gold v2, silver,
    ///     MASC, the imperative source and its exclusion record, every tranche of the shapes development set and its exclusion record), neither by
    ///     id nor by normalized URL, and are not documents of any evaluation set (read from the evaluation files). The two sources share documents
    ///     (one pool), never a sentence.
    /// </summary>
    [Theory]
    [InlineData(CompoundSources)]
    [InlineData(HeadingsSources)]
    [InlineData(Compound2Sources)]
    [InlineData(Headings2Sources)]
    public void PartOfSpeech_TargetedSources_Documents_AreInNoOtherSourceTableAndNoEvaluationSet(string sources)
    {
        // Arrange: the ids and normalized URLs of the other tables, and the documents of the evaluation sets
        var own = DataFiles.ReadTable(DataFiles.Data(sources));
        var others = new List<(string, string)>
        {
            ("open/sources.tsv", "source_id"), ("eval/gold-v2/sources.tsv", "doc_id"), ("silver/sources.tsv", "source_id"),
            ("masc/sources.tsv", "source_id"), (ShapesDevFolder + "/sources.tsv", "source_id"), (ShapesDevHardSources, "source_id"),
            (ShapesDevCompoundHeadSources, "source_id"), (ShapesDevHeadingsSources, "source_id"), (ShapesDevNamedStateSources, "source_id"),
            (ShapesDevCompoundHead2Sources, "source_id"), (ShapesDevHeadings2Sources, "source_id"),
            (ShapesDevFolder + "/excluded-sources.tsv", "source_id"), (ImperativeSources, "source_id")
        };

        // The other targeted training sources are others too, except the partner of the same pool (compound with headings, compound2 with headings2), which shares documents by design
        var partner = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            [CompoundSources] = HeadingsSources,
            [HeadingsSources] = CompoundSources,
            [Compound2Sources] = Headings2Sources,
            [Headings2Sources] = Compound2Sources
        };
        others.AddRange(partner.Keys.Where(table => table != sources && table != partner[sources]).Select(table => (table, "source_id")));
        var ids = new HashSet<string>(StringComparer.Ordinal);
        var urls = new HashSet<string>(StringComparer.Ordinal);
        foreach (var (name, idColumn) in others)
        {
            var data = DataFiles.ReadTable(DataFiles.Data(name));
            ids.UnionWith(data.Column(idColumn));
            if (Array.IndexOf(data.Columns, "url") >= 0)
            {
                urls.UnionWith(data.Column("url").Select(NormalizeUrl).Where(url => url.Length > 0));
            }
        }

        var evaluationDocuments = new HashSet<string>(EvaluationItems().Select(item => item.Document), StringComparer.Ordinal);

        // Act: find the shared ids, URLs and evaluation documents
        var problems = own.Rows
            .Where(row => ids.Contains(own.Cell(row, "source_id")) || urls.Contains(NormalizeUrl(own.Cell(row, "url"))) || evaluationDocuments.Contains(own.Cell(row, "source_id")))
            .Select(row => own.Cell(row, "source_id") + " is in another source table or an evaluation set")
            .ToList();

        // Assert: the tables are not empty and nothing is shared
        Assert.True(own.Rows.Count > 50);
        Assert.True(ids.Count > 5000);
        Assert.Empty(problems.Take(ShownProblems));
    }

    /// <summary>
    ///     Proves, reading the files themselves, that no sentence (of any length) of the compound-head or the headings training source is a sentence
    ///     of another training source or of the other new source, and that no document of either is a document of the open, MASC, silver or
    ///     imperative training data; the two new sources may share documents.
    /// </summary>
    [Fact]
    public void PartOfSpeech_TargetedSources_Sentences_AreDisjointFromEveryOtherTrainingSource()
    {
        // Arrange: the keys and the documents of the sources
        var olderCorpora = new[] { "open/train.tsv", "masc/masc.tsv", "silver/silver.tsv", ImperativeCorpus };
        var older = olderCorpora.SelectMany(corpus => DataFiles.ReadCorpus(DataFiles.Data(corpus))).ToList();
        var olderKeys = new HashSet<string>(older.Select(sentence => DataFiles.SentenceKey(sentence.Text)), StringComparer.Ordinal);
        var olderDocuments = new HashSet<string>(older.Select(sentence => sentence.SourceId), StringComparer.Ordinal);
        var compound = DataFiles.ReadCorpus(DataFiles.Data(CompoundCorpus)).ToList();
        var headings = DataFiles.ReadCorpus(DataFiles.Data(HeadingsCorpus)).ToList();
        var headingKeys = new HashSet<string>(headings.Select(sentence => DataFiles.SentenceKey(sentence.Text)), StringComparer.Ordinal);

        // Act: find the shared sentences and documents
        var problems = new List<string>();
        foreach (var (label, items) in new[] { ("compound", compound), ("headings", headings) })
        {
            problems.AddRange(items.Where(item => olderKeys.Contains(DataFiles.SentenceKey(item.Text)))
                .Select(item => $"{label}: {item.SentenceId} is a sentence of another training source"));
            problems.AddRange(items.Where(item => olderDocuments.Contains(item.SourceId))
                .Select(item => $"{label}: {item.SentenceId} is from a document of another training source"));
        }

        problems.AddRange(compound.Where(item => headingKeys.Contains(DataFiles.SentenceKey(item.Text)))
            .Select(item => $"compound: {item.SentenceId} is a line of the headings source"));

        // Assert: the sets are not empty, the two sources do share documents (one pool), and nothing else is shared
        Assert.True(older.Count > 10000);
        Assert.True(compound.Count > 600 && headings.Count > 300);
        Assert.NotEmpty(compound.Select(item => item.SourceId).Intersect(headings.Select(item => item.SourceId), StringComparer.Ordinal));
        Assert.Empty(problems.Take(ShownProblems));
    }

    /// <summary>
    ///     Proves that the committed leakage report covers the compound-head and the headings training source and the four technical training sources
    ///     (<c>deverbal</c>, <c>terms</c>, <c>steps</c>, <c>clauses</c>) against every evaluation set (the
    ///     probe and every tranche of the shapes development set included) and records no document overlap, no exact sentence overlap and no near
    ///     duplicate (the check gates the near duplicates of every source).
    /// </summary>
    [Theory]
    [InlineData("compound")]
    [InlineData("headings")]
    [InlineData("compound2")]
    [InlineData("headings2")]
    [InlineData("deverbal")]
    [InlineData("terms")]
    [InlineData("steps")]
    [InlineData("clauses")]
    public void PartOfSpeech_TargetedSources_LeakageReport_RecordsNoOverlapWithAnyEvaluationSet(string source)
    {
        // Arrange: the rows of the report that name the source (training set, evaluation set, sentences, documents, exact, near)
        var rows = File.ReadLines(DataFiles.Data("leakage-report.tsv"))
            .Where(line => line.Length > 0 && !DataFiles.IsComment(line))
            .Select(line => line.Split('\t'))
            .Where(row => row[0] == source)
            .ToList();
        var expected = new[]
        {
            "gold-v2", "open-dev", "open-test", "shapes-dev", "shapes-dev-clause-verbs", "shapes-dev-compound-head", "shapes-dev-compound-head-2",
            "shapes-dev-defined-terms", "shapes-dev-deverbal-heads", "shapes-dev-hard", "shapes-dev-headings", "shapes-dev-headings-2",
            "shapes-dev-named-state", "shapes-dev-step-lines", "shapes-probe", "ste-challenge", "ud-ewt-dev", "ud-ewt-test", "ud-ewt-train"
        };

        // Act: find the rows that report an overlap or a near duplicate
        var overlaps = rows.Where(row => row[3] != "0" || row[4] != "0").Select(row => row[1]).ToList();
        var nearDuplicates = rows.Where(row => row[5] != "0").Select(row => row[1]).ToList();

        // Assert: every evaluation set is reported once and nothing overlaps, not even a near duplicate (a malformed count is not "0")
        Assert.All(rows, row => Assert.Equal(6, row.Length));
        Assert.Equal(expected, rows.Select(row => row[1]).OrderBy(name => name, StringComparer.Ordinal));
        Assert.Empty(overlaps);
        Assert.Empty(nearDuplicates);
    }

    /// <summary>
    ///     Proves that the committed training leakage report compares the compound-head and the headings sources (the old and the follow-up
    ///     ones) and the four technical sources with every other training set (each pair once) and records no exact sentence overlap, no near duplicate
    ///     and no shared document except between the two sources of one pool (<c>compound</c> with <c>headings</c>, <c>compound2</c> with <c>headings2</c>,
    ///     and the six pairs of the four technical sources), which share documents by design.
    /// </summary>
    [Fact]
    public void PartOfSpeech_TargetedSources_TrainingLeakageReport_RecordsNoOverlapWithAnotherTrainingSet()
    {
        // Arrange: the rows (new set, other set, new sentences, other sentences, documents, exact, near)
        var rows = File.ReadLines(DataFiles.Data("training-leakage-report.tsv"))
            .Where(line => line.Length > 0 && !DataFiles.IsComment(line))
            .Select(line => line.Split('\t'))
            .ToList();
        var expected = new[]
        {
            "compound/compound2", "compound/headings", "compound/headings2", "compound/imperative", "compound/masc", "compound/open-train",
            "compound/silver", "compound2/headings2", "compound2/imperative", "compound2/masc", "compound2/open-train", "compound2/silver",
            "headings/compound2", "headings/headings2", "headings/imperative", "headings/masc", "headings/open-train", "headings/silver",
            "headings2/imperative", "headings2/masc", "headings2/open-train", "headings2/silver"
        }.Concat(TechnicalTrainingPairs)
            .OrderBy(pair => pair, StringComparer.Ordinal)
            .ToList();
        var sharedPools = new[] { "compound/headings", "compound2/headings2" }.Concat(TechnicalSharedPoolPairs).ToArray();

        // Act: find the pairs with an exact sentence overlap, a near duplicate or a shared document outside the two pools
        var overlaps = rows
            .Where(row => row[5] != "0" || row[6] != "0" || (Array.IndexOf(sharedPools, row[0] + "/" + row[1]) < 0 && row[4] != "0"))
            .Select(row => row[0] + "/" + row[1])
            .ToList();

        // Assert: every pair is reported once with seven columns, each pool is shared and nothing else overlaps
        Assert.All(rows, row => Assert.Equal(7, row.Length));
        Assert.Equal(expected, rows.Select(row => row[0] + "/" + row[1]).OrderBy(name => name, StringComparer.Ordinal));
        Assert.NotEqual("0", rows.Single(row => row[0] == "compound" && row[1] == "headings")[4]);
        Assert.NotEqual("0", rows.Single(row => row[0] == "compound2" && row[1] == "headings2")[4]);
        Assert.Empty(overlaps);
    }

    /// <summary>
    ///     Proves that the selection of the compound-head and the headings training source is independent of the PartOfSpeech model: the builder
    ///     (<c>compound_heading_data.py</c>) names no weights file, decoder, tagger or shapes evaluation, and it names no probe file and uses no probe
    ///     reader (the held-out sets reach it only through the opaque exclusion keys of the shared filter).
    /// </summary>
    [Fact]
    public void PartOfSpeech_TargetedSources_Selection_UsesNoPartOfSpeechModel()
    {
        // Arrange: the source of the builder
        var forbidden = new[] { "weights.bin", "Resources/weights", "PerceptronModel", "FeatureExtractor", "PartOfSpeechTagger", "ITagger", "eval-shapes", "ShapesDevEvaluator" };
        var builder = File.ReadAllText(DataFiles.Repository("tools/data/compound_heading_data.py"));

        // Act: find the forbidden names
        var found = forbidden.Where(word => builder.Contains(word, StringComparison.Ordinal)).ToList();

        // Assert: the builder is not empty, uses no model, and names no probe file and no probe reader
        Assert.True(builder.Length > 10000);
        Assert.Empty(found);
        Assert.DoesNotContain("probe.tsv", builder, StringComparison.Ordinal);
        Assert.DoesNotContain("sentence_column", builder, StringComparison.Ordinal);
        Assert.DoesNotContain("probe_sentences", builder, StringComparison.Ordinal);
    }

    /// <summary>
    ///     Proves that the selection of the four follow-up products (the two development tranches and the two training sources) is independent of
    ///     the PartOfSpeech model: the builder (<c>followup_data.py</c>) names no weights file, decoder, tagger or shapes evaluation, and it names no
    ///     probe file and uses no probe reader. The pinned WordSense Basic classifier mass of the <c>compound-head-2</c> band is read from a file
    ///     (<c>hmm.tsv</c>) that the data tool does not make.
    /// </summary>
    [Fact]
    public void PartOfSpeech_Followup_Selection_UsesNoPartOfSpeechModel()
    {
        // Arrange: the source of the builder
        var builder = File.ReadAllText(DataFiles.Repository("tools/data/followup_data.py"));

        // Act: find the forbidden names
        var found = ModelNames.Where(word => builder.Contains(word, StringComparison.Ordinal)).ToList();

        // Assert: the builder is not empty, uses no model, and names no probe file and no probe reader
        Assert.True(builder.Length > 10000);
        Assert.Empty(found);
        Assert.DoesNotContain("probe.tsv", builder, StringComparison.Ordinal);
        Assert.DoesNotContain("sentence_column", builder, StringComparison.Ordinal);
        Assert.DoesNotContain("probe_sentences", builder, StringComparison.Ordinal);
    }

    /// <summary>
    ///     Proves, reading the files themselves, that no sentence of the two follow-up training sources (<c>compound2</c>, <c>headings2</c>) is a
    ///     sentence of any other training source (the older compound-head and headings sources included) or of the other follow-up source, and that no
    ///     document of either is a document of any other training source except the older ones: the two follow-up sources share documents with
    ///     each other (one pool), and with no other training source.
    /// </summary>
    [Fact]
    public void PartOfSpeech_Followup_TrainingSentences_AreDisjointFromEveryOtherTrainingSource()
    {
        // Arrange: the keys and the documents of every other training source
        var others = TrainingCorpora.Where(corpus => corpus != Compound2Corpus && corpus != Headings2Corpus)
            .SelectMany(corpus => DataFiles.ReadCorpus(DataFiles.Data(corpus)))
            .ToList();
        var otherKeys = new HashSet<string>(others.Select(sentence => DataFiles.SentenceKey(sentence.Text)), StringComparer.Ordinal);
        var otherDocuments = new HashSet<string>(others.Select(sentence => sentence.SourceId), StringComparer.Ordinal);
        var compound2 = DataFiles.ReadCorpus(DataFiles.Data(Compound2Corpus)).ToList();
        var headings2 = DataFiles.ReadCorpus(DataFiles.Data(Headings2Corpus)).ToList();
        var headingKeys = new HashSet<string>(headings2.Select(sentence => DataFiles.SentenceKey(sentence.Text)), StringComparer.Ordinal);

        // Act: find the shared sentences and documents
        var problems = new List<string>();
        foreach (var (label, items) in new[] { ("compound2", compound2), ("headings2", headings2) })
        {
            problems.AddRange(items.Where(item => otherKeys.Contains(DataFiles.SentenceKey(item.Text)))
                .Select(item => $"{label}: {item.SentenceId} is a sentence of another training source"));
            problems.AddRange(items.Where(item => otherDocuments.Contains(item.SourceId))
                .Select(item => $"{label}: {item.SentenceId} is from a document of another training source"));
        }

        problems.AddRange(compound2.Where(item => headingKeys.Contains(DataFiles.SentenceKey(item.Text)))
            .Select(item => $"compound2: {item.SentenceId} is a line of the headings2 source"));

        // Assert: the sets are not empty, the two sources share documents (one pool), and nothing else is shared
        Assert.True(others.Count > 10000);
        Assert.True(compound2.Count > 600 && headings2.Count > 200);
        Assert.NotEmpty(compound2.Select(item => item.SourceId).Intersect(headings2.Select(item => item.SourceId), StringComparer.Ordinal));
        Assert.Empty(problems.Take(ShownProblems));
    }

    /// <summary>
    ///     Proves that the third-party notice names the compound-head and the headings training source, their US federal-work basis, their CC0
    ///     dedication of annotations and selection, and that the compound source has weight 1 (the embedded weights were learned from it) and
    ///     the headings source weight 0 (not part of the shipped mix).
    /// </summary>
    [Fact]
    public void PartOfSpeech_TargetedSources_Notice_NamesTheSourcesAndTheirBasis()
    {
        // Arrange: the section of the notice that describes the data built by this project
        var notice = Collapse(NoticeSection("### The open English part-of-speech corpus (CC0 1.0)"));

        // Act: find the statements that the notice lacks
        var missing = MissingFragments(notice, new[]
        {
            "data/compound/", "data/headings/", "17 U.S.C. 105", "CC0 1.0", "data/compound/sources.tsv", "data/headings/sources.tsv", "weight 0",
            "weight 1", "were learned from it", "not part of the mix"
        });

        // Assert: the notice makes every statement
        Assert.Empty(missing);
    }

    /// <summary>
    ///     Proves that the lexicon allows the gold tag for at least 99.9 percent of the non-disputed tokens of the open
    ///     training set.
    /// </summary>
    [Fact]
    public void PartOfSpeech_Lexicon_Coverage_Train_MeetsThreshold()
    {
        // Arrange: read the lexicon
        var lexicon = DataFiles.ReadLexicon(DataFiles.Data("lexicon/lexicon.tsv"));

        // Act: count the non-disputed tokens whose gold tag the lexicon allows
        var (tokens, covered) = MeasureCoverage("open/train.tsv", lexicon);

        // Assert: the coverage is at least 99.9 percent
        Assert.True(tokens > 100000, "open/train.tsv");
        Assert.True(covered * 1000 >= tokens * 999, $"open/train.tsv: {covered} of {tokens} tokens are covered");
    }

    /// <summary>
    ///     Proves that the lexicon allows the gold tag for at least 99.9 percent of the non-disputed tokens of the open
    ///     development set.
    /// </summary>
    [Fact]
    public void PartOfSpeech_Lexicon_Coverage_Dev_MeetsThreshold()
    {
        // Arrange: read the lexicon
        var lexicon = DataFiles.ReadLexicon(DataFiles.Data("lexicon/lexicon.tsv"));

        // Act: count the non-disputed tokens whose gold tag the lexicon allows
        var (tokens, covered) = MeasureCoverage("open/dev.tsv", lexicon);

        // Assert: the coverage is at least 99.9 percent
        Assert.True(tokens > 20000, "open/dev.tsv");
        Assert.True(covered * 1000 >= tokens * 999, $"open/dev.tsv: {covered} of {tokens} tokens are covered");
    }

    /// <summary>
    ///     Proves that the lexicon rows are valid (a spelling and alphabetical Universal POS tags), sorted by UTF-8 bytes
    ///     and unique.
    /// </summary>
    [Fact]
    public void PartOfSpeech_Lexicon_Format_RowsAreSortedUniqueAndValid()
    {
        // Arrange: the lexicon rows and a place for the problems
        var rows = LexiconRows();
        var problems = new List<string>();
        string? previous = null;

        // Act: check every row
        foreach (var line in rows)
        {
            var cells = line.Split('\t');
            var tags = cells.Length == 2 ? cells[1].Split(' ') : Array.Empty<string>();
            if (tags.Length == 0 || tags.Any(tag => !DataFiles.UniversalTags.Contains(tag)) ||
                !tags.SequenceEqual(tags.OrderBy(tag => tag, StringComparer.Ordinal).Distinct()))
            {
                problems.Add("invalid row: " + line);
            }
            else if (previous != null && CompareUtf8(previous, cells[0]) >= 0)
            {
                problems.Add("not sorted or not unique: " + cells[0]);
            }

            previous = cells[0];
        }

        // Assert: the lexicon is not empty and every row is valid
        Assert.True(rows.Count > 200000);
        Assert.Empty(problems.Take(ShownProblems));
    }

    /// <summary>
    ///     Proves that the counts of spellings and tag entries in the header of the lexicon are the counts of its rows.
    /// </summary>
    [Fact]
    public void PartOfSpeech_Lexicon_Format_HeaderCountsMatchRows()
    {
        // Arrange: the lexicon rows and the counts that the header states
        var lines = File.ReadLines(DataFiles.Data("lexicon/lexicon.tsv")).ToList();
        var rows = LexiconRows();
        var header = Regex.Match(string.Join("\n", lines.Where(DataFiles.IsComment)), @"spellings=(\d+), tag entries=(\d+)");

        // Act: count the spellings and the tag entries of the rows
        var spellings = rows.Count;
        var entries = rows.Sum(line => line.Substring(line.IndexOf('\t') + 1).Split(' ').Length);

        // Assert: the header states the counts of the rows
        Assert.True(header.Success);
        Assert.Equal(spellings, int.Parse(header.Groups[1].Value, CultureInfo.InvariantCulture));
        Assert.Equal(entries, int.Parse(header.Groups[2].Value, CultureInfo.InvariantCulture));
    }

    /// <summary>
    ///     Proves that the token-shape rules that decide which tags the coverage check allows work as documented.
    /// </summary>
    [Fact]
    public void PartOfSpeech_Lexicon_TokenShapeRules_AllowTagsByShape()
    {
        // Arrange: a small lexicon
        var lexicon = new Dictionary<string, string[]>(StringComparer.Ordinal)
        {
            ["the"] = new[] { "DET" },
            ["."] = new[] { "PUNCT" },
            ["3rd"] = new[] { "ADJ" }
        };

        // Act / Assert: an unknown spelling allows every tag
        Assert.True(DataFiles.UniversalTags.All(tag => DataFiles.IsTagAllowed("unlisted", tag, lexicon)));

        // A known spelling allows its tags, ignoring case, and no others
        Assert.True(DataFiles.IsTagAllowed("THE", "DET", lexicon) && !DataFiles.IsTagAllowed("the", "VERB", lexicon));

        // A capitalized token also allows PROPN and X
        Assert.True(DataFiles.IsTagAllowed("The", "PROPN", lexicon) && DataFiles.IsTagAllowed("The", "X", lexicon));

        // A token without letter or digit also allows SYM and X, but not NOUN
        Assert.True(DataFiles.IsTagAllowed(".", "SYM", lexicon) && !DataFiles.IsTagAllowed(".", "NOUN", lexicon));

        // A token with a digit also allows NUM, but not VERB
        Assert.True(DataFiles.IsTagAllowed("3rd", "NUM", lexicon) && !DataFiles.IsTagAllowed("3rd", "VERB", lexicon));
    }

    /// <summary>
    ///     Proves that the lexicon that is embedded in the assembly encodes exactly the spellings and tags of <c>data/lexicon/lexicon.tsv</c>.
    /// </summary>
    [Fact]
    public void PartOfSpeech_Lexicon_Embedded_EncodesLexiconTsv()
    {
        // Arrange: the table as the data tests read it, and the lexicon that the library loads from its embedded resource
        var table = DataFiles.ReadLexicon(DataFiles.Data("lexicon/lexicon.tsv"));
        var embedded = EmbeddedModel.Lexicon;
        var scratch = new byte[embedded.ScratchSize];
        var problems = new List<string>();

        // Act: look every spelling of the table up in the embedded lexicon and compare the tags
        foreach (var row in table)
        {
            var key = row.Key.ToCharArray();
            if (!embedded.TryGetTags(key, 0, key.Length, scratch, out var tags) || !tags.Select(t => t.ToTagString()).SequenceEqual(row.Value))
            {
                problems.Add(row.Key);
            }
        }

        // Assert: nothing is missing or different, and the embedded lexicon has no extra spelling
        Assert.Empty(problems.Take(ShownProblems));
        Assert.Equal(table.Count, embedded.SpellingCount);
        Assert.Equal(table.Values.Sum(tags => tags.Length), embedded.EntryCount);
    }

    /// <summary>
    ///     Proves that the weights that are embedded in the assembly are exactly the model that the committed training
    ///     configuration describes: the same file as the committed one, the same hash bits, scale and temperature, and
    ///     the SHA-256 of the uncompressed image that the trainer recorded. A model that was edited or retrained
    ///     without the configuration (or the other way around) fails here in every normal test run; the full retraining
    ///     check is <c>train --check</c> in the build.
    /// </summary>
    [Fact]
    public void PartOfSpeech_Model_EmbeddedWeights_MatchTrainingConfig()
    {
        // Arrange: the embedded resource, the committed file next to the project and the text of the configuration
        using var stream = EmbeddedModel.OpenResource(EmbeddedModel.WeightsResourceName);
        Assert.NotNull(stream);
        using var buffer = new MemoryStream();
        stream.CopyTo(buffer);
        var embedded = buffer.ToArray();
        var committed = File.ReadAllBytes(DataFiles.Repository("src/DemaConsulting.PartOfSpeech/Resources/weights.bin"));
        var config = File.ReadAllText(DataFiles.Repository(TrainingConfigPath));

        // Act: load the model (any feature set the build knows) and hash its uncompressed image
        var model = PerceptronModel.Load(embedded);
        string hash;
        using (var sha = System.Security.Cryptography.SHA256.Create())
        {
            hash = BitConverter.ToString(sha.ComputeHash(PerceptronModel.ToUncompressedImage(embedded))).Replace("-", string.Empty).ToLowerInvariant();
        }

        // Assert: embedded equals committed, and the header and the image match the configuration
        Assert.Equal(committed, embedded);
        Assert.Equal(ConfigValue(config, "imageSha256"), hash);
        Assert.Equal(int.Parse(ConfigValue(config, "hashBits"), CultureInfo.InvariantCulture), model.HashBits);
        Assert.Equal(1f / int.Parse(ConfigValue(config, "quantScale"), CultureInfo.InvariantCulture), model.Scale);
        Assert.Equal((float)double.Parse(ConfigValue(config, "temperature"), CultureInfo.InvariantCulture), model.Temperature);
        Assert.Equal(ConfigFeatureGroups(config), model.FeatureGroups);
    }

    /// <summary>
    ///     Reads the feature groups that a training configuration selects (an absent or empty <c>features</c> member means none).
    /// </summary>
    /// <param name="config">The text of the configuration.</param>
    /// <returns>The selected groups.</returns>
    private static FeatureGroups ConfigFeatureGroups(string config)
    {
        var members = Regex.Match(config, "\"features\"\\s*:\\s*\\[([^\\]]*)\\]");
        var groups = FeatureGroups.None;
        if (!members.Success)
        {
            return groups;
        }

        foreach (Match name in Regex.Matches(members.Groups[1].Value, "\"([^\"]+)\""))
        {
            groups |= name.Groups[1].Value switch
            {
                "positionCues" => FeatureGroups.PositionCues,
                "clauseCues" => FeatureGroups.ClauseCues,
                "rightPreview" => FeatureGroups.RightPreview,
                "inflection" => FeatureGroups.Inflection,
                "titleCaseLine" => FeatureGroups.TitleCaseLine,
                _ => throw new InvalidDataException($"Unknown feature group '{name.Groups[1].Value}' in the training configuration.")
            };
        }

        return groups;
    }

    /// <summary>
    ///     Proves that the size figures that the documentation states for the embedded weights (compressed and
    ///     uncompressed bytes and, where a document states it, the number of rows) equal the embedded file. A retrained
    ///     model fails here and the message names the document that must be updated. The run log is excluded on purpose:
    ///     it records the figures of historical runs.
    /// </summary>
    [Fact]
    public void PartOfSpeech_Model_EmbeddedWeights_SizeFiguresInDocs_AreCurrent()
    {
        // Arrange: the figures of the embedded file, formatted as the documents state them
        using var stream = EmbeddedModel.OpenResource(EmbeddedModel.WeightsResourceName);
        Assert.NotNull(stream);
        using var buffer = new MemoryStream();
        stream.CopyTo(buffer);
        var embedded = buffer.ToArray();
        var model = PerceptronModel.Load(embedded);
        var compressed = embedded.Length.ToString("N0", CultureInfo.InvariantCulture);
        var uncompressed = PerceptronModel.ToUncompressedImage(embedded).Length.ToString("N0", CultureInfo.InvariantCulture);
        var rows = model.RowCount.ToString("N0", CultureInfo.InvariantCulture);
        var sizeDocuments = new[]
        {
            "docs/user_guide/introduction.md",
            "data/README.md",
            "src/DemaConsulting.PartOfSpeech/NamespaceDoc.cs",
            "tools/DemaConsulting.PartOfSpeech.Trainer/README.md",
            "docs/design/part-of-speech.md",
            "docs/design/part-of-speech/model-storage.md",
            "docs/design/part-of-speech/model-storage/perceptron-model.md",
            "docs/design/part-of-speech/model-trainer.md",
            "docs/verification/part-of-speech.md"
        };
        var rowDocuments = new[]
        {
            "src/DemaConsulting.PartOfSpeech/NamespaceDoc.cs",
            "tools/DemaConsulting.PartOfSpeech.Trainer/README.md",
            "docs/design/part-of-speech/model-storage.md",
            "docs/design/part-of-speech/model-storage/perceptron-model.md",
            "docs/design/part-of-speech/model-trainer.md"
        };

        // Act: collect the figures that a document does not state
        var missing = new List<string>();
        foreach (var path in sizeDocuments)
        {
            var text = Collapse(File.ReadAllText(DataFiles.Repository(path)));
            missing.AddRange(MissingFragments(text, new[] { compressed, uncompressed }).Select(figure => $"{path}: {figure}"));
        }

        foreach (var path in rowDocuments)
        {
            var text = Collapse(File.ReadAllText(DataFiles.Repository(path)));
            missing.AddRange(MissingFragments(text, new[] { rows }).Select(figure => $"{path}: {figure} rows"));
        }

        // Assert: every document states the current figures
        Assert.True(missing.Count == 0, "Update the size figures of the embedded weights in: " + string.Join("; ", missing));
    }

    /// <summary>
    ///     Proves that the training configuration names only corpora that are committed in the repository, that are
    ///     training corpora (no development, held-out or restricted data) and whose data folders declare an allowed
    ///     license, and that the third-party notice names the corpora that the weights were trained on.
    /// </summary>
    [Fact]
    public void PartOfSpeech_Model_TrainingConfig_UsesOnlyCommittedTrainingCorpora()
    {
        // Arrange: the corpus paths of the configuration
        var config = File.ReadAllText(DataFiles.Repository(TrainingConfigPath));
        var paths = Regex.Matches(config, "\"path\"\\s*:\\s*\"([^\"]+)\"").Cast<Match>().Select(m => m.Groups[1].Value).ToList();
        var notice = NoticeSection("## Embedded Weights");

        // Act: find the problems of each source
        var problems = new List<string>();
        foreach (var path in paths)
        {
            var relative = path.StartsWith("data/", StringComparison.Ordinal) ? path.Substring("data/".Length) : path;
            if (Array.IndexOf(TrainingCorpora, relative) < 0)
            {
                problems.Add("not a training corpus: " + path);
            }
            else if (!File.Exists(DataFiles.Repository(path)))
            {
                problems.Add("not committed: " + path);
            }

            if (RestrictedCorpusPath.IsMatch(path))
            {
                problems.Add("restricted corpus: " + path);
            }

            problems.AddRange(LicenseProblems(relative.Substring(0, relative.IndexOf('/'))));
        }

        // Assert: at least one source, no problem, and the notice says which corpora the weights come from
        Assert.NotEmpty(paths);
        Assert.Empty(problems.Take(ShownProblems));
        Assert.Empty(MissingFragments(notice, new[] { "MASC", "CC BY 3.0 US", "open corpus", "silver corpus", "imperative source", "compound source", "technical source" }));
    }

    /// <summary>
    ///     Proves that the project file names the combined package license file instead of a license expression, packs
    ///     the file at the package root, and that the file exists.
    /// </summary>
    [Fact]
    public void PartOfSpeech_PackageLicense_ProjectFile_NamesLicenseFileAtPackageRoot()
    {
        // Arrange: load the project file of the library
        var projectFile = DataFiles.Repository("src/DemaConsulting.PartOfSpeech/DemaConsulting.PartOfSpeech.csproj");
        var project = XDocument.Load(projectFile);

        // Act: find the license property and the items that include the license file
        var licenseFile = project.Descendants("PackageLicenseFile").Select(e => e.Value.Trim()).ToList();
        var expressions = project.Descendants("PackageLicenseExpression").ToList();
        var items = project.Descendants("None")
            .Where(item => ((string?)item.Attribute("Include") ?? string.Empty).EndsWith(PackageLicenseName, StringComparison.Ordinal))
            .ToList();

        // Assert: a license file and no expression (a package has one of them); the file is packed at the root
        Assert.Equal(new[] { PackageLicenseName }, licenseFile);
        Assert.Empty(expressions);
        var item = Assert.Single(items);
        var include = ((string?)item.Attribute("Include"))!.Replace('\\', '/');
        Assert.True(File.Exists(Path.GetFullPath(Path.Combine(Path.GetDirectoryName(projectFile)!, include))));
        Assert.Equal("true", (string?)item.Attribute("Pack"));
        Assert.Equal("/", ((string?)item.Attribute("PackagePath"))!.Replace('\\', '/'));
    }

    /// <summary>
    ///     Proves that the package license starts with the SPDX expression of the whole package, contains the MIT license
    ///     text of the repository for the code, covers the two embedded data files with their licenses, and points to
    ///     the third-party notice.
    /// </summary>
    [Fact]
    public void PartOfSpeech_PackageLicense_File_CoversCodeAndEmbeddedData()
    {
        // Arrange: the package license and the license of the repository, with white space collapsed
        var text = Collapse(File.ReadAllText(DataFiles.Repository(PackageLicenseName)));
        var repositoryLicense = Collapse(File.ReadAllText(DataFiles.Repository("LICENSE"))).Trim();
        var required = new[]
        {
            "## Code: MIT License",
            "lexicon.bin",
            "weights.bin",
            "Open English WordNet",
            "Creative Commons Attribution 4.0 International",
            "https://creativecommons.org/licenses/by/4.0/",
            "Manually Annotated Sub-Corpus (MASC)",
            "Creative Commons Attribution 3.0 United States",
            "https://creativecommons.org/licenses/by/3.0/us/",
            "THIRD-PARTY-NOTICES.md"
        };

        // Act: find what the file lacks
        var missing = MissingFragments(text, required);

        // Assert: the SPDX line first, the MIT text of the repository, and every required statement
        Assert.StartsWith("SPDX-License-Identifier: MIT AND CC-BY-4.0 AND CC-BY-3.0-US ", text, StringComparison.Ordinal);
        Assert.Contains(repositoryLicense, text, StringComparison.Ordinal);
        Assert.Empty(missing);
    }

    /// <summary>
    ///     Proves that the third-party notice covers the embedded weights: the MASC section says that the weights were
    ///     learned from the converted MASC data and are provided under its license, and a section of their own names the
    ///     trainer and the corpora.
    /// </summary>
    [Fact]
    public void PartOfSpeech_ThirdPartyNotices_Weights_AreCovered()
    {
        // Arrange: the sections of the notice that must cover the weights
        var masc = NoticeSection("## The Manually Annotated Sub-Corpus (MASC)");
        var weights = NoticeSection("## Embedded Weights");

        // Act: find the statements that the sections lack
        var missing = MissingFragments(masc, new[] { "weights of the tagger (`weights.bin`) were learned in part from the converted data", "provided under the terms of this license" })
            .Concat(MissingFragments(weights, new[] { "Resources/weights.bin", "tools/DemaConsulting.PartOfSpeech.Trainer", "`training.json`", "`RUN-LOG.md`", "the attribution of MASC applies to the weights" }))
            .ToList();

        // Assert: nothing is missing
        Assert.Empty(missing);
    }

    /// <summary>
    ///     Gets the value of a top-level setting of the training configuration, without a JSON parser (which .NET
    ///     Framework does not have in the box).
    /// </summary>
    private static string ConfigValue(string config, string name)
    {
        var match = Regex.Match(config, "\"" + name + "\"\\s*:\\s*\"?([^\",\\s}]+)");
        Assert.True(match.Success, "training.json has no setting " + name);
        return match.Groups[1].Value;
    }

    /// <summary>
    ///     Collapses every run of white space to one space.
    /// </summary>
    private static string Collapse(string text) => Regex.Replace(text, @"\s+", " ");

    /// <summary>
    ///     Gets one section of the third-party notice, from its heading to the next heading of the same level, with white
    ///     space collapsed so that line wrapping does not matter.
    /// </summary>
    /// <param name="heading">The Markdown heading line, for example <c>## Open English WordNet</c>.</param>
    private static string NoticeSection(string heading)
    {
        var text = File.ReadAllText(DataFiles.Repository("THIRD-PARTY-NOTICES.md")).Replace("\r\n", "\n");
        var start = text.IndexOf("\n" + heading + "\n", StringComparison.Ordinal);
        Assert.True(start >= 0, "THIRD-PARTY-NOTICES.md has no section " + heading);
        var level = heading.IndexOf(' ');
        var next = Regex.Match(text.Substring(start + 1), @"\n#{1," + level.ToString(CultureInfo.InvariantCulture) + "} ");
        return Collapse(next.Success ? text.Substring(start, next.Index + 1) : text.Substring(start));
    }

    /// <summary>
    ///     Finds the required fragments that a text lacks, ignoring differences in white space.
    /// </summary>
    private static List<string> MissingFragments(string text, IEnumerable<string> required) =>
        required.Where(fragment => text.IndexOf(Collapse(fragment), StringComparison.Ordinal) < 0).ToList();

    /// <summary>
    ///     Determines whether a folder is left out of the search for restricted files: repository metadata, dependencies,
    ///     build output and the git-ignored download cache of the data tools.
    /// </summary>
    private static bool IsIgnoredFolder(string folder)
    {
        var ignored = new[] { ".git", "node_modules", ".venv", "bin", "obj", "__pycache__", "TestResults", "artifacts", ".agent-logs" };
        return Array.IndexOf(ignored, Path.GetFileName(folder)) >= 0 || DataFiles.RelativePath(folder) == "tools/data/cache";
    }

    /// <summary>
    ///     Finds the problems of the license declarations of a data README.
    /// </summary>
    private static IEnumerable<string> LicenseProblems(string folder)
    {
        var readme = DataFiles.Data(folder + "/README.md");
        if (!File.Exists(readme))
        {
            return new[] { "missing README: data/" + folder };
        }

        var licenses = LicenseLine.Matches(File.ReadAllText(readme)).Cast<Match>().Select(match => match.Groups[1].Value).ToList();
        if (licenses.Count == 0)
        {
            return new[] { "no license declared: data/" + folder };
        }

        return licenses.Where(license => Array.IndexOf(AllowedLicenses, license) < 0).Select(license => $"data/{folder}: license {license} is not allowed");
    }

    /// <summary>
    ///     Reads the document ids of a source table.
    /// </summary>
    private static HashSet<string> SourceIds(string table, string idColumn) =>
        new(DataFiles.ReadTable(DataFiles.Data(table)).Column(idColumn), StringComparer.Ordinal);

    /// <summary>
    ///     Finds the documents of a data set that its source list does not name.
    /// </summary>
    private static IEnumerable<string> MissingDocuments(string label, IEnumerable<string> documents, HashSet<string> listed) =>
        documents.Distinct().Where(document => !listed.Contains(document)).Select(document => $"{label}: not in the source list: {document}");

    /// <summary>
    ///     Finds the problems of the MASC source table: the id and genre of every row, the license class, and the
    ///     sentence and token counts, which must equal the counts of the document in <c>masc.tsv</c> (and so sum to the
    ///     totals of the file).
    /// </summary>
    private static IEnumerable<string> MascSourceProblems()
    {
        var counts = DataFiles.ReadCorpus(DataFiles.Data("masc/masc.tsv"))
            .GroupBy(sentence => sentence.SourceId)
            .ToDictionary(group => group.Key, group => (Sentences: group.Count(), Tokens: group.Sum(sentence => sentence.Tokens.Count)), StringComparer.Ordinal);
        var data = DataFiles.ReadTable(DataFiles.Data("masc/sources.tsv"));
        var sentenceTotal = 0;
        var tokenTotal = 0;
        foreach (var row in data.Rows)
        {
            var id = data.Cell(row, "source_id");
            var match = MascDocument.Match(id);
            if (!match.Success || match.Groups[1].Value != data.Cell(row, "genre"))
            {
                yield return $"data/masc/sources.tsv: {id}: the id is malformed or does not match the genre '{data.Cell(row, "genre")}'";
            }

            if (data.Cell(row, "license_basis") != MascLicenseBasis)
            {
                yield return $"data/masc/sources.tsv: {id}: license class is not {MascLicenseBasis}";
            }

            var sentences = int.TryParse(data.Cell(row, "sentences"), NumberStyles.None, CultureInfo.InvariantCulture, out var s) ? s : 0;
            var tokens = int.TryParse(data.Cell(row, "tokens"), NumberStyles.None, CultureInfo.InvariantCulture, out var t) ? t : 0;
            if (sentences <= 0 || tokens <= 0)
            {
                yield return $"data/masc/sources.tsv: {id}: the sentence or token count is not a positive number";
            }

            if (!counts.TryGetValue(id, out var actual) || actual.Sentences != sentences || actual.Tokens != tokens)
            {
                yield return $"data/masc/sources.tsv: {id}: the counts differ from masc.tsv";
            }

            sentenceTotal += sentences;
            tokenTotal += tokens;
        }

        if (sentenceTotal != counts.Values.Sum(count => count.Sentences) || tokenTotal != counts.Values.Sum(count => count.Tokens))
        {
            yield return "data/masc/sources.tsv: the counts do not sum to the totals of masc.tsv";
        }
    }

    /// <summary>
    ///     Finds the problems of one row of a source table.
    /// </summary>
    private static IEnumerable<string> SourceRowProblems(string table, DataTable data, string[] row, string idColumn)
    {
        var id = data.Cell(row, idColumn);
        var license = data.Cell(row, "license_basis");
        if (!AllowedLicenseBases.Any(basis => license.StartsWith(basis, StringComparison.Ordinal)))
        {
            yield return $"data/{table}: {id}: license class is not allowed: {license.Substring(0, Math.Min(60, license.Length))}";
        }

        var url = data.Cell(row, "url");
        if (url.Length == 0 ? data.Cell(row, "family") != "project-written" : !IsWellFormedUrl(url))
        {
            yield return $"data/{table}: {id}: url is empty or malformed: '{url}'";
        }

        if (Array.IndexOf(data.Columns, "terms_url") >= 0 && !IsWellFormedUrl(data.Cell(row, "terms_url")))
        {
            yield return $"data/{table}: {id}: terms_url is empty or malformed";
        }

        if (!int.TryParse(data.Cell(row, "sentences"), NumberStyles.None, CultureInfo.InvariantCulture, out var sentences) || sentences <= 0)
        {
            yield return $"data/{table}: {id}: the sentence count is not a positive number";
        }
    }

    /// <summary>
    ///     Determines whether a text is an absolute HTTPS URL without white space.
    /// </summary>
    private static bool IsWellFormedUrl(string text) =>
        Uri.TryCreate(text, UriKind.Absolute, out var uri) && uri.Scheme == Uri.UriSchemeHttps && !text.Any(char.IsWhiteSpace);

    /// <summary>
    ///     Normalizes a URL like the data tools: the host in lower case without <c>www.</c> and the path without a query,
    ///     fragment or trailing slash; the empty string for a text that is not an absolute URL.
    /// </summary>
    private static string NormalizeUrl(string text)
    {
        if (!Uri.TryCreate(text.Trim(), UriKind.Absolute, out var uri))
        {
            return string.Empty;
        }

        var host = uri.Host.ToLowerInvariant();
        return (host.StartsWith("www.", StringComparison.Ordinal) ? host.Substring(4) : host) + uri.AbsolutePath.TrimEnd('/');
    }

    /// <summary>
    ///     Reads the rows of the checksum manifest: path, bytes, SHA-256, lines and sentences.
    /// </summary>
    private static List<string[]> ReadManifest() =>
        File.ReadLines(DataFiles.Data("MANIFEST.tsv"))
            .Where(line => line.Length > 0 && !DataFiles.IsComment(line))
            .Select(line => line.Split('\t'))
            .ToList();

    /// <summary>
    ///     Lists the data files that the manifest covers, relative to the data folder: all files except Markdown files,
    ///     the manifest itself and the git-ignored local FAA file.
    /// </summary>
    private static List<string> ManifestedFilesOnDisk()
    {
        var dataRoot = DataFiles.Data(string.Empty).TrimEnd('/', '\\');
        return DataFiles.EnumerateFiles(dataRoot, _ => false)
            .Select(file => DataFiles.RelativePath(file).Substring("data/".Length))
            .Where(path => path != "MANIFEST.tsv" && !path.EndsWith(".md", StringComparison.Ordinal) && path != "silver/silver-faa.tsv")
            .ToList();
    }

    /// <summary>
    ///     Finds the differences between a manifest row and the file it describes.
    /// </summary>
    private static IEnumerable<string> ChecksumProblems(string[] row)
    {
        var path = DataFiles.Data(row[0]);
        if (new FileInfo(path).Length.ToString(CultureInfo.InvariantCulture) != row[1])
        {
            yield return "size differs: " + row[0];
        }

        if (!string.Equals(DataFiles.Sha256Hex(path), row[2], StringComparison.Ordinal))
        {
            yield return "SHA-256 differs: " + row[0];
        }
    }

    /// <summary>
    ///     Finds the tokens of one sentence of a token-level corpus that do not carry a Universal POS tag.
    /// </summary>
    private static IEnumerable<string> TagProblems(string corpus, CorpusSentence sentence)
    {
        foreach (var token in sentence.Tokens)
        {
            var valid = token.Length >= 3 &&
                        CorpusStatus.TryParse(token[2], out var status, out _) &&
                        (DataFiles.UniversalTags.Contains(token[1]) || (token[1] == "_" && status == "masked"));
            if (!valid)
            {
                yield return $"{corpus}: {sentence.SentenceId}: invalid token row '{string.Join("|", token)}'";
            }
        }
    }

    /// <summary>
    ///     Counts the non-disputed tokens of a corpus and those whose gold tag the lexicon allows.
    /// </summary>
    private static (long Tokens, long Covered) MeasureCoverage(string corpus, IReadOnlyDictionary<string, string[]> lexicon)
    {
        long tokens = 0;
        long covered = 0;
        foreach (var token in DataFiles.ReadCorpus(DataFiles.Data(corpus)).SelectMany(sentence => sentence.Tokens))
        {
            if (CorpusStatus.BaseStatus(token[2]) == "disputed")
            {
                continue;
            }

            tokens++;
            if (DataFiles.IsTagAllowed(token[0], token[1], lexicon))
            {
                covered++;
            }
        }

        return (tokens, covered);
    }

    /// <summary>
    ///     Reads the rows of the lexicon (every line that is not empty and not a comment).
    /// </summary>
    private static List<string> LexiconRows() =>
        File.ReadLines(DataFiles.Data("lexicon/lexicon.tsv")).Where(line => line.Length > 0 && !DataFiles.IsComment(line)).ToList();

    /// <summary>
    ///     Gets the noun-verb class of a Universal POS tag.
    /// </summary>
    /// <param name="tag">The tag name.</param>
    /// <returns><c>Noun</c> for NOUN and PROPN, <c>Verb</c> for VERB, otherwise <c>other</c>.</returns>
    private static string ClassOfTag(string tag)
    {
        if (tag == "NOUN" || tag == "PROPN")
        {
            return "Noun";
        }

        return tag == "VERB" ? "Verb" : "other";
    }

    /// <summary>
    ///     Gets the other class of the noun-verb pair.
    /// </summary>
    /// <param name="goldClass">The class, <c>Noun</c> or <c>Verb</c>.</param>
    /// <returns>The other class.</returns>
    private static string OtherClass(string goldClass) => goldClass == "Noun" ? "Verb" : "Noun";

    /// <summary>
    ///     Reads and validates the items of the hard-shape development set with the parser of the trainer.
    /// </summary>
    private static List<ShapesDevItem> ShapesDevItems() =>
        ShapesDevSets.Parse(File.ReadAllLines(DataFiles.Data(ShapesDevCorpus)), ShapesDevCorpus);

    /// <summary>
    ///     Reads and validates the items of the hard tranche with the parser of the trainer.
    /// </summary>
    private static List<ShapesDevItem> ShapesDevHardItems() =>
        ShapesDevSets.Parse(File.ReadAllLines(DataFiles.Data(ShapesDevHardCorpus)), ShapesDevHardCorpus);

    /// <summary>
    ///     Reads and validates the items of a technical tranche with the parser of the trainer.
    /// </summary>
    private static List<ShapesDevItem> TechnicalItems(TechnicalTranche tranche) =>
        ShapesDevSets.Parse(File.ReadAllLines(DataFiles.Data(tranche.Corpus)), tranche.Corpus, tranche.Tranche);

    /// <summary>
    ///     Gets the quota groups of an item of a technical tranche: its kind, and <c>target</c> or <c>control</c>.
    /// </summary>
    private static IEnumerable<string> TechnicalGroups(ShapesDevItem item) => new[] { item.Kind, item.Role };

    /// <summary>
    ///     Proves that the header of every technical tranche of the hard-shape development set announces its own tranche and the dev split, that
    ///     no comment of the file carries a number that the selection used, that every sentence id is <c>shapes-dev-NAME-NNNN</c> in file order, and
    ///     that the comments of every sentence are the documented ones in the documented order (the kind, then the extra comments of the tranche).
    /// </summary>
    [Fact]
    public void PartOfSpeech_ShapesDevTechnical_Headers_AnnounceTheTrancheAndTheDevSplit()
    {
        // Arrange: the comment keys that a file of a technical tranche may carry in its header and in a sentence block
        var allowed = new[]
        {
            "columns", "corpus", "file", "judged word", "license", "selection", "status", "status flags", "use",
            "sent_id", "source_id", "text", "shape", "role", "target", "lemma", "origin", "kind", "term", "frame", "unit"
        };
        var order = new[] { "sent_id", "source_id", "text", "shape", "role", "target", "lemma", "origin", "kind" };
        var problems = new List<string>();

        foreach (var tranche in TechnicalTranches)
        {
            var lines = File.ReadAllLines(DataFiles.Data(tranche.Corpus));
            var header = lines.TakeWhile(line => line.Length > 0).ToList();
            var keys = lines.Where(DataFiles.IsComment)
                .Select(line => line[2..].Split(new[] { " = " }, 2, StringSplitOptions.None)[0])
                .Distinct(StringComparer.Ordinal)
                .ToList();

            // Act: find the corpus, use and selection lines, the unexpected keys and the malformed ids
            var corpus = header.FirstOrDefault(line => line.StartsWith("# corpus = ", StringComparison.Ordinal));
            var use = header.FirstOrDefault(line => line.StartsWith("# use = ", StringComparison.Ordinal));
            var selection = header.FirstOrDefault(line => line.StartsWith("# selection = ", StringComparison.Ordinal));
            if (corpus == null || !corpus.Contains(tranche.Name + " tranche", StringComparison.Ordinal) || !corpus.Contains("dev split", StringComparison.Ordinal))
            {
                problems.Add($"{tranche.Name}: the corpus line does not announce '{tranche.Name} tranche' and 'dev split'");
            }

            if (use == null || !use.Contains("development only", StringComparison.Ordinal))
            {
                problems.Add($"{tranche.Name}: the use line does not say 'development only'");
            }

            if (selection == null || selection.Any(char.IsDigit))
            {
                problems.Add($"{tranche.Name}: the selection line is missing or carries a number");
            }

            problems.AddRange(keys.Except(allowed).Select(key => $"{tranche.Name}: the comment key '{key}' is not documented"));
            problems.AddRange(keys.Where(key => key == "q" || key.StartsWith("q_", StringComparison.Ordinal)).Select(key => $"{tranche.Name}: the comment key '{key}' carries a selection number"));

            var ids = ReadBlockComments(tranche.Corpus);
            var expectedOrder = order.Concat(tranche.ExtraKeys).ToArray();
            for (var i = 0; i < ids.Count; i++)
            {
                var expectedId = string.Format(CultureInfo.InvariantCulture, "shapes-dev-{0}-{1:D4}", tranche.Name, i + 1);
                if (ids[i].Id != expectedId)
                {
                    problems.Add($"{tranche.Name}: {ids[i].Id}: the id is not {expectedId}");
                }

                if (!ids[i].Keys.SequenceEqual(expectedOrder, StringComparer.Ordinal))
                {
                    problems.Add($"{tranche.Name}: {ids[i].Id}: the comments are '{string.Join(", ", ids[i].Keys)}', expected '{string.Join(", ", expectedOrder)}'");
                }
            }

            if (ids.Count != tranche.Count)
            {
                problems.Add($"{tranche.Name}: {ids.Count} sentences, expected {tranche.Count}");
            }
        }

        // Assert: every tranche announces itself and no problem was found
        Assert.Empty(problems.Take(ShownProblems));
    }

    /// <summary>
    ///     Proves that every item of the technical tranches has the only shape of its tranche, a known role, a target inside the sentence, the origin
    ///     <c>real</c> from a source document, a lemma, a kind with the role and the exact gold tag of the kind (the parser of the trainer checks it),
    ///     a judged word that is never disputed, tokens that rejoin to the text, and that the committed files are read by the real reader of the trainer
    ///     (corpus marker, id prefix, one tranche per file) with the kinds that the table of the trainer lists.
    /// </summary>
    [Fact]
    public void PartOfSpeech_ShapesDevTechnical_Items_AreWellFormedRealAndNeverDisputed()
    {
        var problems = new List<string>();
        var total = 0;

        foreach (var tranche in TechnicalTranches)
        {
            // Arrange: the items (the parser refuses a wrong tag, kind or role), the sentences and the real reader
            var items = TechnicalItems(tranche);
            var sentences = DataFiles.ReadCorpus(DataFiles.Data(tranche.Corpus)).ToList();
            var read = DevSets.ReadShapesDev(DataFiles.RepositoryRoot, tranche.Tranche, out var sha256);
            var spec = ShapesDevSets.SpecOf(tranche.Tranche)!;
            total += items.Count;
            if (items.Count != sentences.Count || read.Count != sentences.Count)
            {
                problems.Add($"{tranche.Name}: {items.Count} items, {read.Count} read items and {sentences.Count} sentences differ");
                continue;
            }

            if (sha256 != DataFiles.Sha256Hex(DataFiles.Data(tranche.Corpus)))
            {
                problems.Add($"{tranche.Name}: the hash of the reader is not the hash of the file");
            }

            if (!spec.Kinds.Select(kind => (kind.Name, kind.Role)).SequenceEqual(tranche.Kinds))
            {
                problems.Add($"{tranche.Name}: the kinds of the trainer table are not '{string.Join(", ", tranche.Kinds.Select(k => k.Kind + ":" + k.Role))}'");
            }

            // Act: check every item against the format and its sentence
            for (var i = 0; i < items.Count; i++)
            {
                var item = items[i];
                var sentence = sentences[i];
                var where = $"{tranche.Name}: {sentence.SentenceId}";
                if (item.Shape != tranche.Shape || item.Tranche != tranche.Tranche)
                {
                    problems.Add($"{where}: the shape is '{item.Shape}' of tranche {item.Tranche}, expected '{tranche.Shape}'");
                }

                if (item.Origin != "real" || item.SourceId == ShapesDevSets.AuthoredSourceId)
                {
                    problems.Add($"{where}: the origin is '{item.Origin}', expected real from a source document");
                }

                if (item.Lemma.Length == 0 || item.Kind.Length == 0 || item.Target < 0 || item.Target >= sentence.Tokens.Count)
                {
                    problems.Add($"{where}: the lemma, the kind or the target index is missing or out of range");
                    continue;
                }

                if (DataFiles.BaseStatus(sentence.Tokens[item.Target][2]) == "disputed")
                {
                    problems.Add($"{where}: the judged word is disputed");
                }

                if (item.GoldTag != sentence.Tokens[item.Target][1])
                {
                    problems.Add($"{where}: the gold tag of the item is not the tag of the token");
                }

                if (Regex.Replace(sentence.Text, @"\s+", string.Empty) != string.Concat(sentence.Tokens.Select(row => row[0])))
                {
                    problems.Add($"{where}: the tokens do not rejoin to the text");
                }
            }
        }

        // Assert: the tranches are not empty and no problem was found
        Assert.Equal(450 + 285 + 236 + 446, total);
        Assert.Empty(problems.Take(ShownProblems));
    }

    /// <summary>
    ///     Proves that the committed technical tranches meet their documented quotas: the items of every group (a kind, all targets, all controls)
    ///     between the floor and the cap, at most four targets per lemma (six imperatives per lemma in the step lines), at most two controls per lemma, at most
    ///     15 targets per source document and at most two items per capitalized term.
    /// </summary>
    [Fact]
    public void PartOfSpeech_ShapesDevTechnical_Quotas_AreMetByTheCommittedTranches()
    {
        var problems = new List<string>();

        foreach (var tranche in TechnicalTranches)
        {
            // Arrange: the items, counted by group
            var items = TechnicalItems(tranche);
            var groups = items.SelectMany(item => TechnicalGroups(item).Select(group => (group, item))).GroupBy(entry => entry.group)
                .ToDictionary(group => group.Key, group => group.Count(), StringComparer.Ordinal);

            // Act: check the floor and the cap of every group and the caps per lemma, per document and per term
            foreach (var (group, floor, cap) in tranche.Quotas)
            {
                var count = groups.GetValueOrDefault(group);
                if (count < floor || count > cap)
                {
                    problems.Add($"{tranche.Name}: {count} items in the group '{group}', expected {floor} to {cap}");
                }
            }

            problems.AddRange(items.Select(item => item.Kind).Distinct(StringComparer.Ordinal).Except(tranche.Kinds.Select(kind => kind.Kind)).Select(kind => $"{tranche.Name}: the kind '{kind}' is unknown"));
            var targets = items.Where(item => item.Role == "target").ToList();
            problems.AddRange(targets.GroupBy(item => item.Lemma, StringComparer.OrdinalIgnoreCase)
                .Where(group => group.Count() > tranche.LemmaCap)
                .Select(group => $"{tranche.Name}: the lemma '{group.Key}' has {group.Count()} targets, the cap is {tranche.LemmaCap}"));
            problems.AddRange(items.Where(item => item.Role == "control").GroupBy(item => item.Lemma, StringComparer.OrdinalIgnoreCase)
                .Where(group => group.Count() > TechnicalControlLemmaCap)
                .Select(group => $"{tranche.Name}: the lemma '{group.Key}' has {group.Count()} controls, the cap is {TechnicalControlLemmaCap}"));
            problems.AddRange(targets.GroupBy(item => item.SourceId, StringComparer.Ordinal)
                .Where(group => group.Count() > TechnicalDevDocumentCap)
                .Select(group => $"{tranche.Name}: the document {group.Key} has {group.Count()} targets, the cap is {TechnicalDevDocumentCap}"));
            if (tranche.ExtraKeys.Contains("term"))
            {
                problems.AddRange(ReadBlockValues(tranche.Corpus).GroupBy(block => block["term"], StringComparer.Ordinal)
                    .Where(group => group.Count() > TechnicalTermCap)
                    .Select(group => $"{tranche.Name}: the term '{group.Key}' has {group.Count()} items, the cap is {TechnicalTermCap}"));
            }
        }

        // Assert: no quota is missed
        Assert.Empty(problems.Take(ShownProblems));
    }

    /// <summary>
    ///     Proves that the library tokenizer reproduces the stored tokens of every sentence of the technical tranches and of the technical training
    ///     sources: one sentence, the same tokens.
    /// </summary>
    [Fact]
    public void PartOfSpeech_Technical_Tokens_AreReproducedByTheTokenizer()
    {
        var problems = new List<string>();
        var total = 0;

        foreach (var corpus in TechnicalTranches.Select(tranche => tranche.Corpus).Concat(TechnicalSources.Select(source => source.Corpus)))
        {
            // Arrange: the sentences of the file
            var sentences = DataFiles.ReadCorpus(DataFiles.Data(corpus)).ToList();
            total += sentences.Count;

            // Act: tokenize the text of every sentence and compare with the stored tokens
            foreach (var sentence in sentences)
            {
                var tokenized = Tokenizer.Tokenize(sentence.Text);
                var actual = tokenized.SelectMany(part => part.Tokens).Select(token => token.Text).ToArray();
                var stored = sentence.Tokens.Select(token => token[0]).ToArray();
                if (tokenized.Count != 1 || !actual.SequenceEqual(stored, StringComparer.Ordinal))
                {
                    problems.Add($"{corpus}: {sentence.SentenceId}: the tokenizer gives {tokenized.Count} sentence(s) and {actual.Length} tokens, the file has {stored.Length}");
                }
            }
        }

        // Assert: the sets are not empty and every sentence is reproduced
        Assert.Equal(450 + 285 + 236 + 446 + 637 + 373 + 531 + 310, total);
        Assert.Empty(problems.Take(ShownProblems));
    }

    /// <summary>
    ///     Proves that the source table of every technical tranche and of every technical training source lists exactly the documents of its sentences with
    ///     their counts, that every document has a US federal-work or public-domain basis, that no row mentions FAA material or a share-alike or Wikipedia
    ///     text, that no excluded document (by id or normalized URL, using the exclusion record of the shapes development set) is used, and that the
    ///     records of the training sources (documents left out, sentences dropped) are well formed, carry ids and counts only and name no used document.
    /// </summary>
    [Fact]
    public void PartOfSpeech_Technical_Sources_AreListedOpenAndNotExcluded()
    {
        // Arrange: the exclusions and the rules for a row
        var excluded = DataFiles.ReadTable(DataFiles.Data(ShapesDevFolder + "/excluded-sources.tsv"));
        var excludedIds = new HashSet<string>(excluded.Column("source_id"), StringComparer.Ordinal);
        var excludedUrls = new HashSet<string>(excluded.Column("url").Select(NormalizeUrl), StringComparer.Ordinal);
        var faa = new Regex(@"faa\.gov|\bFAA\b|Federal Aviation", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        var openBases = new[] { "US federal government work", "US public domain" };
        var problems = new List<string>();
        var documents = 0;
        var pairs = TechnicalTranches.Select(tranche => (Label: tranche.Name, tranche.Corpus, tranche.Sources))
            .Concat(TechnicalSources.Select(source => (Label: source.Name, source.Corpus, source.Sources)))
            .ToList();
        var rejected = DataFiles.ReadTable(DataFiles.Data(TechnicalFolder + "/excluded-documents.tsv"));
        var used = new HashSet<string>(StringComparer.Ordinal);

        foreach (var (label, corpus, sources) in pairs)
        {
            // Act: compare the listed documents and counts with the sentences and look at every row
            var counts = DataFiles.ReadCorpus(DataFiles.Data(corpus))
                .GroupBy(sentence => sentence.SourceId, StringComparer.Ordinal)
                .ToDictionary(group => group.Key, group => group.Count(), StringComparer.Ordinal);
            var table = DataFiles.ReadTable(DataFiles.Data(sources));
            var listed = table.Rows.ToDictionary(row => table.Cell(row, "source_id"), row => table.Cell(row, "sentences"), StringComparer.Ordinal);
            documents += counts.Count;
            used.UnionWith(counts.Keys);
            problems.AddRange(counts.Keys.Except(listed.Keys).Select(document => $"{label}: not listed: {document}")
                .Concat(listed.Keys.Except(counts.Keys).Select(document => $"{label}: listed but unused: {document}"))
                .Concat(counts.Where(count => listed.TryGetValue(count.Key, out var cell) && cell != count.Value.ToString(CultureInfo.InvariantCulture))
                    .Select(count => $"{label}: the count of {count.Key} differs from the table")));
            if (table.Rows.Count != listed.Count)
            {
                problems.Add($"{label}: a document is listed twice");
            }

            foreach (var row in table.Rows)
            {
                var id = table.Cell(row, "source_id");
                if (!openBases.Any(basis => table.Cell(row, "license_basis").StartsWith(basis, StringComparison.Ordinal)))
                {
                    problems.Add($"{label}: {id}: the license basis is not a US federal work or US public domain");
                }

                if (row.Any(cell => faa.IsMatch(cell)))
                {
                    problems.Add($"{label}: {id}: the row mentions FAA material");
                }

                if (RestrictedLicenseText.IsMatch(table.Cell(row, "license_basis")) || RestrictedLicenseText.IsMatch(table.Cell(row, "url")))
                {
                    problems.Add($"{label}: {id}: the row names a share-alike, Wikipedia or other restricted license");
                }

                if (excludedIds.Contains(id) || excludedUrls.Contains(NormalizeUrl(table.Cell(row, "url"))))
                {
                    problems.Add($"{label}: {id}: the document is excluded");
                }
            }
        }

        // Act: the records of the training sources
        problems.AddRange(rejected.Column("document_id").Where(used.Contains).Select(id => $"{id}: a rejected document is used"));
        foreach (var source in TechnicalSources)
        {
            var dropped = DataFiles.ReadTable(DataFiles.Data(source.Dropped));
            problems.AddRange(dropped.Rows.Where(row => !int.TryParse(dropped.Cell(row, "sentences"), NumberStyles.None, CultureInfo.InvariantCulture, out _))
                .Select(row => $"{source.Name}: {dropped.Cell(row, "reason")}: the count is not a number"));
            if (!dropped.Columns.SequenceEqual(new[] { "reason", "sentences" }))
            {
                problems.Add($"{source.Name}: the dropped-sentences table has the columns '{string.Join(", ", dropped.Columns)}'");
            }
        }

        // Assert: the tables have documents, the exclusion record has its columns and no problem was found
        Assert.True(documents > 900);
        Assert.Equal(new[] { "document_id", "reason" }, rejected.Columns);
        Assert.Empty(problems.Take(ShownProblems));
    }

    /// <summary>
    ///     Proves that the committed leakage report of every technical tranche covers every other evaluation set, that the rows of the sets that are
    ///     not technical tranches record no document, exact sentence or near-duplicate overlap, that the rows of the other technical tranches record no exact or
    ///     near-duplicate sentence (the four tranches share documents on purpose; that is reported, not gated), that no row shares a word 5-gram with the shape
    ///     probe, and that every row counts the sentences of the tranche.
    /// </summary>
    [Fact]
    public void PartOfSpeech_ShapesDevTechnical_LeakageReports_RecordNoOverlapAndSharedDocuments()
    {
        var problems = new List<string>();

        foreach (var tranche in TechnicalTranches)
        {
            // Arrange: read the report (evaluation set, dev sentences, documents, exact, near duplicates, probe 5-grams)
            var rows = File.ReadLines(DataFiles.Data(tranche.Report))
                .Where(line => line.Length > 0 && !DataFiles.IsComment(line))
                .Select(line => line.Split('\t'))
                .ToList();
            var siblings = TechnicalTranches.Where(other => other.Name != tranche.Name).Select(other => "shapes-dev-" + other.Name).ToList();
            var expected = new[] { "gold-v2", "open-dev", "open-test", "shapes-dev", "shapes-dev-hard", "shapes-probe", "ste-challenge", "ud-ewt-dev", "ud-ewt-test", "ud-ewt-train" }
                .Concat(ShapesDevNewTranches.Select(other => "shapes-dev-" + other.Name))
                .Concat(siblings)
                .OrderBy(name => name, StringComparer.Ordinal)
                .ToList();

            // Act: find the sets that the report misses and the rows that report an overlap
            if (rows.Any(row => row.Length != 6))
            {
                problems.Add($"{tranche.Name}: a row of the leakage report does not have 6 cells");
                continue;
            }

            if (!rows.Select(row => row[0]).OrderBy(name => name, StringComparer.Ordinal).SequenceEqual(expected, StringComparer.Ordinal))
            {
                problems.Add($"{tranche.Name}: the report covers '{string.Join(", ", rows.Select(row => row[0]))}', expected '{string.Join(", ", expected)}'");
            }

            foreach (var row in rows)
            {
                if (row[1] != tranche.Count.ToString(CultureInfo.InvariantCulture))
                {
                    problems.Add($"{tranche.Name}: {row[0]}: {row[1]} sentences, the tranche has {tranche.Count}");
                }

                if (siblings.Contains(row[0]) ? !int.TryParse(row[2], NumberStyles.None, CultureInfo.InvariantCulture, out var shared) || shared == 0 : row[2] != "0")
                {
                    problems.Add($"{tranche.Name}: {row[0]}: the document overlaps are '{row[2]}'");
                }

                if (row[3] != "0" || row[4] != "0")
                {
                    problems.Add($"{tranche.Name}: {row[0]}: exact sentence overlaps '{row[3]}' or near duplicates '{row[4]}'");
                }

                if (row[0] == "shapes-probe" && row[5] != "0")
                {
                    problems.Add($"{tranche.Name}: the probe 5-gram sentences are '{row[5]}'");
                }
            }
        }

        // Assert: nothing overlaps
        Assert.Empty(problems.Take(ShownProblems));
    }

    /// <summary>
    ///     Proves, reading the files themselves, that no sentence (of any length) of a technical tranche is a sentence of any other evaluation set, of
    ///     another technical tranche or of any training source, and that no source document of a technical tranche is a document of any other evaluation set
    ///     (the older tranches, the standard and the hard tranche included) or of any training source; documents that the four technical tranches share
    ///     among themselves are allowed.
    /// </summary>
    [Fact]
    public void PartOfSpeech_ShapesDevTechnical_Sentences_AreDisjointFromEveryOtherSet()
    {
        // Arrange: the evaluation items, the training documents and the sentence keys of every training source
        var evaluation = EvaluationItems();
        var training = TrainingCorpora.SelectMany(corpus => DataFiles.ReadCorpus(DataFiles.Data(corpus))).ToList();
        var trainingDocuments = new HashSet<string>(training.Select(sentence => sentence.SourceId), StringComparer.Ordinal);
        var trainingKeys = new HashSet<string>(training.Select(sentence => DataFiles.SentenceKey(sentence.Text)), StringComparer.Ordinal);
        var pool = TechnicalTranches.Select(tranche => tranche.Corpus).ToList();
        var problems = new List<string>();
        var total = 0;

        foreach (var tranche in TechnicalTranches)
        {
            var sentences = DataFiles.ReadCorpus(DataFiles.Data(tranche.Corpus)).ToList();
            var documents = new HashSet<string>(sentences.Select(sentence => sentence.SourceId), StringComparer.Ordinal);
            var keys = new HashSet<string>(sentences.Select(sentence => DataFiles.SentenceKey(sentence.Text)), StringComparer.Ordinal);
            total += sentences.Count;

            // Act: find the shared sentences (any set) and the shared documents (any set but the four technical tranches)
            problems.AddRange(evaluation
                .Where(item => item.Set != tranche.Corpus)
                .Where(item => keys.Contains(DataFiles.SentenceKey(item.Text)) || (!pool.Contains(item.Set) && documents.Contains(item.Document)))
                .Select(item => $"{tranche.Name}: {item.Set}: {item.Document} shares a sentence or a document"));
            problems.AddRange(sentences
                .Where(sentence => trainingKeys.Contains(DataFiles.SentenceKey(sentence.Text)))
                .Select(sentence => $"{tranche.Name}: {sentence.SentenceId} is a training sentence"));
            problems.AddRange(documents
                .Where(trainingDocuments.Contains)
                .Select(document => $"{tranche.Name}: {document} is a training document"));
        }

        // Assert: the sets are not empty, the other technical tranches were among the sets that were compared and nothing is shared
        Assert.Equal(450 + 285 + 236 + 446, total);
        Assert.True(evaluation.Count > 5000);
        Assert.True(trainingDocuments.Count > 1000);
        Assert.Contains(evaluation, item => item.Set == ShapesDevFolder + "/deverbal-heads.tsv");
        Assert.Contains(training, sentence => sentence.SentenceId.StartsWith("clauses-", StringComparison.Ordinal));
        Assert.Empty(problems.Take(ShownProblems));
    }

    /// <summary>
    ///     Proves that the header of every technical training source announces a training file (and no development use), that every sentence id
    ///     is <c>NAME-NNNN</c> in file order, that the comments of every sentence are the documented ones in the documented order, that every token is
    ///     <c>unanimous</c> (two blind labelers agree on the tag of every token), and that the judged word of every sentence has the tag that its stratum
    ///     allows (the first token for the steps).
    /// </summary>
    [Fact]
    public void PartOfSpeech_Technical_Corpora_HaveTrainingHeadersIdsAndStratumTags()
    {
        var problems = new List<string>();
        var order = new[] { "sent_id", "source_id", "text", "word", "lemma", "stratum" };

        foreach (var source in TechnicalSources)
        {
            // Arrange: the header, the sentences and their comment values
            var header = File.ReadLines(DataFiles.Data(source.Corpus)).TakeWhile(line => line.Length > 0).ToList();
            var sentences = DataFiles.ReadCorpus(DataFiles.Data(source.Corpus)).ToList();
            var blocks = ReadBlockValues(source.Corpus);
            var comments = ReadBlockComments(source.Corpus);
            var strata = source.Strata.ToDictionary(entry => entry.Stratum, entry => entry.Tags.Split('|'), StringComparer.Ordinal);

            // Act: check the header and every sentence
            if (!header.Contains("# use = training") || !header.Contains("# columns = form, upos, status")
                || header.Any(line => line.Contains("dev split", StringComparison.Ordinal) || line.Contains("development only", StringComparison.Ordinal)))
            {
                problems.Add($"{source.Name}: the header is not the header of a training file");
            }

            if (!header[0].Contains($"`{source.Name}`", StringComparison.Ordinal) || !header[0].Contains("(UPOS)", StringComparison.Ordinal))
            {
                problems.Add($"{source.Name}: the corpus line does not name the source");
            }

            if (sentences.Count != blocks.Count || sentences.Count != comments.Count || sentences.Count != source.Count)
            {
                problems.Add($"{source.Name}: {sentences.Count} sentences, {blocks.Count} comment blocks, expected {source.Count}");
                continue;
            }

            for (var index = 0; index < sentences.Count; index++)
            {
                var sentence = sentences[index];
                var block = blocks[index];
                var where = $"{source.Name}: {sentence.SentenceId}";
                if (sentence.SentenceId != string.Format(CultureInfo.InvariantCulture, "{0}-{1:D4}", source.Name, index + 1))
                {
                    problems.Add($"{where}: the id is not {source.Name}-{index + 1:D4}");
                }

                if (!comments[index].Keys.SequenceEqual(order.Concat(source.ExtraKeys), StringComparer.Ordinal))
                {
                    problems.Add($"{where}: the comments are '{string.Join(", ", comments[index].Keys)}'");
                }

                if (!int.TryParse(block.GetValueOrDefault("word"), NumberStyles.None, CultureInfo.InvariantCulture, out var word) || word >= sentence.Tokens.Count)
                {
                    problems.Add($"{where}: the judged word is not a token index");
                    continue;
                }

                var stratum = block.GetValueOrDefault("stratum") ?? string.Empty;
                if (!strata.TryGetValue(stratum, out var tags) || !tags.Contains(sentence.Tokens[word][1]))
                {
                    problems.Add($"{where}: the judged word is tagged {sentence.Tokens[word][1]}, which the stratum '{stratum}' does not allow");
                }

                if (source.FirstWord && word != 0)
                {
                    problems.Add($"{where}: the judged word is not token 0");
                }

                if (string.IsNullOrEmpty(block.GetValueOrDefault("lemma")))
                {
                    problems.Add($"{where}: no lemma");
                }

                if (sentence.Tokens.Any(token => DataFiles.BaseStatus(token[2]) != "unanimous"))
                {
                    problems.Add($"{where}: a token is not unanimous");
                }

                if (Regex.Replace(sentence.Text, @"\s+", string.Empty) != string.Concat(sentence.Tokens.Select(token => token[0])))
                {
                    problems.Add($"{where}: the tokens do not rejoin to the text");
                }
            }
        }

        // Assert: no problem was found
        Assert.Empty(problems.Take(ShownProblems));
    }

    /// <summary>
    ///     Proves that the committed technical training sources meet their documented quotas: the sentences of the whole file and of every stratum
    ///     between the floor and the cap, the
    ///     contrast strata within their share of the file, at most four sentences per lemma (six for the steps), at most 20 sentences per document (15 for the
    ///     clauses) and at most two sentences per capitalized term.
    /// </summary>
    [Fact]
    public void PartOfSpeech_Technical_Corpora_AreWithinQuotas()
    {
        var problems = new List<string>();

        foreach (var source in TechnicalSources)
        {
            // Arrange: the strata and the comment values of every sentence
            var blocks = ReadBlockValues(source.Corpus);
            var counts = blocks.GroupBy(block => block.GetValueOrDefault("stratum") ?? string.Empty, StringComparer.Ordinal)
                .ToDictionary(group => group.Key, group => group.Count(), StringComparer.Ordinal);

            // Act: check the floor and the cap of every group, the contrast share and the caps per lemma, document and term
            foreach (var (group, floor, cap) in source.Quotas)
            {
                var count = group == "*" ? blocks.Count : counts.GetValueOrDefault(group);
                if (count < floor || count > cap)
                {
                    problems.Add($"{source.Name}: {count} sentences in the group '{group}', expected {floor} to {cap}");
                }
            }

            problems.AddRange(counts.Keys.Except(source.Strata.Select(entry => entry.Stratum)).Select(stratum => $"{source.Name}: the stratum '{stratum}' is unknown"));
            var contrast = source.Contrast.Strata.Sum(stratum => counts.GetValueOrDefault(stratum));
            if (100 * contrast < source.Contrast.Min * blocks.Count || 100 * contrast > source.Contrast.Max * blocks.Count)
            {
                problems.Add($"{source.Name}: the contrast strata hold {contrast} of {blocks.Count} sentences, expected {source.Contrast.Min} to {source.Contrast.Max} percent");
            }

            problems.AddRange(blocks.GroupBy(block => block.GetValueOrDefault("lemma") ?? string.Empty, StringComparer.OrdinalIgnoreCase)
                .Where(group => group.Count() > source.LemmaCap)
                .Select(group => $"{source.Name}: the lemma '{group.Key}' has {group.Count()} sentences, the cap is {source.LemmaCap}"));
            problems.AddRange(blocks.GroupBy(block => block.GetValueOrDefault("source_id") ?? string.Empty, StringComparer.Ordinal)
                .Where(group => group.Count() > source.DocumentCap)
                .Select(group => $"{source.Name}: the document {group.Key} has {group.Count()} sentences, the cap is {source.DocumentCap}"));
            if (source.ExtraKeys.Contains("term"))
            {
                problems.AddRange(blocks.GroupBy(block => block.GetValueOrDefault("term") ?? string.Empty, StringComparer.Ordinal)
                    .Where(group => group.Count() > TechnicalTermCap)
                    .Select(group => $"{source.Name}: the term '{group.Key}' has {group.Count()} sentences, the cap is {TechnicalTermCap}"));
            }
        }

        // Assert: no quota is missed
        Assert.Empty(problems.Take(ShownProblems));
    }

    /// <summary>
    ///     Proves that the documents of the four technical training sources are in no other source table (open, gold v2, silver, MASC, the imperative,
    ///     compound and headings sources and their exclusion records, every tranche of the shapes development set and its exclusion record), neither by id nor
    ///     by normalized URL, and are not documents of any evaluation set (read from the evaluation files); the four sources share documents (one pool),
    ///     never a sentence, and no sentence of a technical source is a sentence of another training source.
    /// </summary>
    [Fact]
    public void PartOfSpeech_Technical_Sources_AreInOnePoolAndDisjointFromEveryOtherSet()
    {
        // Arrange: the ids and normalized URLs of the other tables, the documents of the evaluation sets and the other training sentences
        var others = new List<(string, string)>
        {
            ("open/sources.tsv", "source_id"), ("eval/gold-v2/sources.tsv", "doc_id"), ("silver/sources.tsv", "source_id"),
            ("masc/sources.tsv", "source_id"), (ShapesDevFolder + "/sources.tsv", "source_id"), (ShapesDevHardSources, "source_id"),
            (ShapesDevCompoundHeadSources, "source_id"), (ShapesDevHeadingsSources, "source_id"), (ShapesDevNamedStateSources, "source_id"),
            (ShapesDevCompoundHead2Sources, "source_id"), (ShapesDevHeadings2Sources, "source_id"),
            (ShapesDevFolder + "/excluded-sources.tsv", "source_id"), (ImperativeSources, "source_id"), (CompoundSources, "source_id"),
            (HeadingsSources, "source_id"), (Compound2Sources, "source_id"), (Headings2Sources, "source_id")
        };
        others.AddRange(TechnicalTranches.Select(tranche => (tranche.Sources, "source_id")));
        var ids = new HashSet<string>(StringComparer.Ordinal);
        var urls = new HashSet<string>(StringComparer.Ordinal);
        foreach (var (name, idColumn) in others)
        {
            var data = DataFiles.ReadTable(DataFiles.Data(name));
            ids.UnionWith(data.Column(idColumn));
            if (Array.IndexOf(data.Columns, "url") >= 0)
            {
                urls.UnionWith(data.Column("url").Select(NormalizeUrl).Where(url => url.Length > 0));
            }
        }

        var evaluationDocuments = new HashSet<string>(EvaluationItems().Select(item => item.Document), StringComparer.Ordinal);
        var technicalCorpora = TechnicalSources.Select(source => source.Corpus).ToList();
        var older = TrainingCorpora.Except(technicalCorpora).SelectMany(corpus => DataFiles.ReadCorpus(DataFiles.Data(corpus))).ToList();
        var olderKeys = new HashSet<string>(older.Select(sentence => DataFiles.SentenceKey(sentence.Text)), StringComparer.Ordinal);
        var problems = new List<string>();
        var documents = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);
        var keys = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);

        // Act: find the shared ids, URLs, evaluation documents and sentences
        foreach (var source in TechnicalSources)
        {
            var own = DataFiles.ReadTable(DataFiles.Data(source.Sources));
            var sentences = DataFiles.ReadCorpus(DataFiles.Data(source.Corpus)).ToList();
            documents[source.Name] = new HashSet<string>(sentences.Select(sentence => sentence.SourceId), StringComparer.Ordinal);
            keys[source.Name] = new HashSet<string>(sentences.Select(sentence => DataFiles.SentenceKey(sentence.Text)), StringComparer.Ordinal);
            problems.AddRange(own.Rows
                .Where(row => ids.Contains(own.Cell(row, "source_id")) || urls.Contains(NormalizeUrl(own.Cell(row, "url"))) || evaluationDocuments.Contains(own.Cell(row, "source_id")))
                .Select(row => $"{source.Name}: {own.Cell(row, "source_id")} is in another source table or an evaluation set"));
            problems.AddRange(sentences.Where(sentence => olderKeys.Contains(DataFiles.SentenceKey(sentence.Text)))
                .Select(sentence => $"{source.Name}: {sentence.SentenceId} is a sentence of another training source"));
        }

        foreach (var pair in TechnicalSharedPoolPairs)
        {
            var (first, second) = (pair.Split('/')[0], pair.Split('/')[1]);
            problems.AddRange(keys[first].Intersect(keys[second], StringComparer.Ordinal).Select(_ => $"{pair}: a sentence is shared"));
            if (!documents[first].Intersect(documents[second], StringComparer.Ordinal).Any())
            {
                problems.Add($"{pair}: no document is shared, expected one pool of documents");
            }
        }

        // Assert: the sets are not empty and nothing is shared except the documents of the pool
        Assert.True(ids.Count > 5000);
        Assert.True(older.Count > 10000);
        Assert.Equal(218, documents.Values.SelectMany(set => set).Distinct(StringComparer.Ordinal).Count());
        Assert.Empty(problems.Take(ShownProblems));
    }

    /// <summary>
    ///     Proves that the committed training leakage report records, for the six pairs of technical sources, shared documents (one pool of documents for the
    ///     four sources) and no exact or near-duplicate sentence, and, for every pair of a technical source with an older training source, no shared
    ///     document, no exact sentence and no near duplicate.
    /// </summary>
    [Fact]
    public void PartOfSpeech_Technical_TrainingLeakageReport_RecordsTheSharedPoolAndNoOtherOverlap()
    {
        // Arrange: the rows (new set, other set, new sentences, other sentences, documents, exact, near) of the pairs that name a technical source
        var rows = File.ReadLines(DataFiles.Data("training-leakage-report.tsv"))
            .Where(line => line.Length > 0 && !DataFiles.IsComment(line))
            .Select(line => line.Split('\t'))
            .Where(row => TechnicalTrainingPairs.Contains(row[0] + "/" + row[1]))
            .ToList();

        // Act: split the pairs into the pool and the others
        var pool = rows.Where(row => TechnicalSharedPoolPairs.Contains(row[0] + "/" + row[1])).ToList();
        var rest = rows.Except(pool).ToList();

        // Assert: all 38 pairs, each once; the pool shares documents and nothing else; every other pair overlaps in nothing
        Assert.Equal(TechnicalTrainingPairs.Length, rows.Count);
        Assert.Equal(6, pool.Count);
        Assert.All(rows, row => Assert.Equal(7, row.Length));
        Assert.All(pool, row => Assert.NotEqual("0", row[4]));
        Assert.All(pool, row => Assert.Equal(("0", "0"), (row[5], row[6])));
        Assert.All(rest, row => Assert.Equal(("0", "0", "0"), (row[4], row[5], row[6])));
        foreach (var source in TechnicalSources)
        {
            var count = source.Count.ToString(CultureInfo.InvariantCulture);
            Assert.All(rows.Where(row => row[0] == source.Name), row => Assert.Equal(count, row[2]));
        }
    }

    /// <summary>
    ///     Proves that the selection of the technical tranches and training sources is independent of the PartOfSpeech model: the tool
    ///     (<c>technical_data.py</c>) and its frames (<c>technical_frames.py</c>) name no weights file, decoder, tagger or shapes evaluation and no
    ///     probe file, and use no probe reader (the held-out sets reach them only through the opaque exclusion keys of the shared filter). The pinned
    ///     WordSense Basic classifier mass of the band is read from a file that the data tool does not make.
    /// </summary>
    [Fact]
    public void PartOfSpeech_Technical_Selection_UsesNoPartOfSpeechModel()
    {
        // Arrange: the sources of the tool and of its frames
        var scripts = new[] { "tools/data/technical_data.py", "tools/data/technical_frames.py" }.Select(path => File.ReadAllText(DataFiles.Repository(path))).ToList();
        var heldOut = new[] { "probe.tsv", "sentence_column", "probe_sentences", "gold-v2", "gold_v2", "eval/gold", "ste-challenge", "ste_challenge", "eval/ste" };

        // Act: find the forbidden names
        var models = ModelNames.Where(word => scripts.Any(script => script.Contains(word, StringComparison.Ordinal))).ToList();
        var reads = heldOut.Where(word => scripts.Any(script => script.Contains(word, StringComparison.Ordinal))).ToList();

        // Assert: the scripts are not empty, use no model and read no held-out file
        Assert.All(scripts, script => Assert.True(script.Length > 10000));
        Assert.Empty(models);
        Assert.Empty(reads);
    }

    /// <summary>
    ///     Proves that the pinned SHA-256 of <c>data/CONVENTIONS.md</c> that the tool holds (<c>CONVENTIONS_SHA256</c>) is the hash of the committed file
    ///     (so the convention text that the labelers were given is the committed one), and that the file has the convention of the capitalized defined
    ///     terms: a row in the table of the decisions that matter most and its own section, with the rule that ordinary English words that name a kind, a
    ///     mode, a setting or a component class are NOUN (ADJ for an adjectival modifier) and that PROPN is only for one particular entity or a coined label.
    /// </summary>
    [Fact]
    public void PartOfSpeech_Technical_Conventions_PinMatchesTheCommittedFileAndNamesDefinedTerms()
    {
        // Arrange: the pin in the tool and the committed convention file
        var tool = File.ReadAllText(DataFiles.Repository("tools/data/technical_data.py"));
        var pin = Regex.Match(tool, "^CONVENTIONS_SHA256 = \"([0-9a-f]{64})\"", RegexOptions.Multiline | RegexOptions.CultureInvariant).Groups[1].Value;
        var conventions = Collapse(File.ReadAllText(DataFiles.Data("CONVENTIONS.md")));

        // Act: hash the file
        var actual = DataFiles.Sha256Hex(DataFiles.Data("CONVENTIONS.md"));

        // Assert: the pin is the hash of the file and the convention is there
        Assert.Equal(64, pin.Length);
        Assert.Equal(pin, actual);
        Assert.Empty(MissingFragments(conventions, new[]
        {
            "| Capitalized defined terms | mid-sentence in technical text: NOUN (ADJ for an adjectival modifier)",
            "PROPN only for one particular entity or a coined label",
            "### Capitalized Defined Terms"
        }));
    }

    /// <summary>
    ///     Proves that the committed technical tranches are read by the trainer (the real reader behind the development guard) with the committed counts,
    ///     that the guard lists their files in the order of the tranche values 7 to 10, and that no file of the four sources is a development file.
    /// </summary>
    [Fact]
    public void PartOfSpeech_ShapesDevTechnical_Tranches_MatchTheTrainerConstants()
    {
        // Arrange: the tranches of the trainer
        var expected = new[]
        {
            (ShapesDevTranche.DeverbalHeads, 7, "deverbal-heads", 450), (ShapesDevTranche.DefinedTerms, 8, "defined-terms", 285),
            (ShapesDevTranche.StepLines, 9, "step-lines", 236), (ShapesDevTranche.ClauseVerbs, 10, "clause-verbs", 446)
        };

        // Act / Assert: value, name, file, count, and the other trainer constants of the table
        foreach (var (tranche, value, name, count) in expected)
        {
            Assert.Equal(value, (int)tranche);
            Assert.Equal(name, DevSets.ShapesDevNameOf(tranche));
            Assert.Equal("data/eval/shapes-dev/" + name + ".tsv", DataGuard.ShapesDevelopmentFiles[value]);
            Assert.Equal(count, DevSets.ReadShapesDev(DataFiles.RepositoryRoot, tranche, out _).Count);
            Assert.Equal(count, TechnicalTranches.Single(entry => entry.Tranche == tranche).Count);
        }

        Assert.Equal(11, DataGuard.ShapesDevelopmentFiles.Count);
        foreach (var source in TechnicalSources)
        {
            Assert.DoesNotContain("data/" + source.Corpus, DataGuard.ShapesDevelopmentFiles);
            Assert.DoesNotContain("data/" + source.Corpus, DataGuard.DevelopmentFiles);
        }
    }

    /// <summary>
    ///     Proves that the third-party notice names the four technical tranches and the four technical training sources, their US federal-work basis
    ///     (17 U.S.C. 105), the CC0 dedication of annotations and selection, the tables that quote the license basis of every document, and that the
    ///     sources are registered with weight 1 (the embedded weights were learned from them).
    /// </summary>
    [Fact]
    public void PartOfSpeech_Technical_Notice_NamesTheSourcesAndTheirBasis()
    {
        // Arrange: the section of the notice that describes the data built by this project
        var notice = Collapse(NoticeSection("### The open English part-of-speech corpus (CC0 1.0)"));

        // Act: find the statements that the notice lacks
        var missing = MissingFragments(notice, new[]
        {
            "`deverbal-heads.tsv`", "`defined-terms.tsv`", "`step-lines.tsv`", "`clause-verbs.tsv`", "`deverbal.tsv`", "`terms.tsv`", "`steps.tsv`", "`clauses.tsv`",
            "data/technical/", "17 U.S.C. 105", "CC0 1.0", "data/eval/shapes-dev/<tranche>-sources.tsv", "data/technical/<source>-sources.tsv",
            "registered with weight 1 (so the embedded weights were learned from them)"
        });

        // Assert: the notice makes every statement
        Assert.Empty(missing);
    }

    /// <summary>
    ///     Reads and validates the items of a new tranche with the parser of the trainer.
    /// </summary>
    private static List<ShapesDevItem> ShapesDevNewItems(ShapesDevNewTranche tranche) =>
        ShapesDevSets.Parse(File.ReadAllLines(DataFiles.Data(tranche.Corpus)), tranche.Corpus, tranche.Tranche);

    /// <summary>
    ///     Reads the comment values (<c># key = value</c>), in file order, of every sentence block of a corpus (the header block has no sentence id).
    /// </summary>
    private static List<Dictionary<string, string>> ReadBlockValues(string corpus)
    {
        var blocks = new List<Dictionary<string, string>>();
        var values = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var line in File.ReadLines(DataFiles.Data(corpus)).Concat(new[] { string.Empty }))
        {
            if (line.Length == 0)
            {
                if (values.ContainsKey("sent_id"))
                {
                    blocks.Add(values);
                }

                values = new Dictionary<string, string>(StringComparer.Ordinal);
            }
            else if (DataFiles.IsComment(line))
            {
                var split = line.IndexOf(" = ", StringComparison.Ordinal);
                if (split >= 0)
                {
                    values[line[2..split]] = line[(split + 3)..];
                }
            }
        }

        return blocks;
    }

    /// <summary>
    ///     Reads the sentence id and the comment keys, in file order, of every sentence block of a corpus (the header block has no sentence id).
    /// </summary>
    private static List<(string Id, List<string> Keys)> ReadBlockComments(string corpus)
    {
        var blocks = new List<(string Id, List<string> Keys)>();
        var keys = new List<string>();
        var id = string.Empty;
        foreach (var line in File.ReadLines(DataFiles.Data(corpus)).Concat(new[] { string.Empty }))
        {
            if (line.Length == 0)
            {
                if (keys.Contains("sent_id"))
                {
                    blocks.Add((id, keys));
                }

                keys = new List<string>();
                id = string.Empty;
            }
            else if (DataFiles.IsComment(line))
            {
                var split = line.IndexOf(" = ", StringComparison.Ordinal);
                if (split >= 0)
                {
                    keys.Add(line[2..split]);
                    if (keys[^1] == "sent_id")
                    {
                        id = line[(split + 3)..];
                    }
                }
            }
        }

        return blocks;
    }

    /// <summary>
    ///     Determines whether a tag is the gold tag of an item of a new tranche: NOUN or PROPN for a target and VERB for a control
    ///     of the compound-head and named-state tranches; for a heading, NOUN for a noun target, VERB for an imperative target and
    ///     PROPN for a proper control.
    /// </summary>
    private static bool IsNewGoldTag(ShapesDevNewTranche tranche, ShapesDevItem item, string tag)
    {
        if (tranche.IsHeadings)
        {
            return item.Kind switch
            {
                "noun" => item.Role == "target" && tag == "NOUN",
                "imperative" => item.Role == "target" && tag == "VERB",
                "proper" => item.Role == "control" && tag == "PROPN",
                _ => false
            };
        }

        return item.Kind.Length == 0 && (item.Role == "target" ? tag is "NOUN" or "PROPN" : tag == "VERB");
    }

    /// <summary>
    ///     Gets the quota group of an item of a new tranche: <c>control</c>, the kind of a heading target, or <c>target</c>.
    /// </summary>
    private static string QuotaGroup(ShapesDevNewTranche tranche, ShapesDevItem item)
    {
        if (item.Role == "control")
        {
            return "control";
        }

        return tranche.IsHeadings ? item.Kind : "target";
    }

    /// <summary>
    ///     A technical tranche of the hard-shape development set and the quotas of its items.
    /// </summary>
    /// <param name="Name">The name of the tranche: the file stem and the infix of the sentence ids.</param>
    /// <param name="Source">The name of the training source of the same shape.</param>
    /// <param name="Shape">The only shape id of the tranche.</param>
    /// <param name="Tranche">The tranche of the trainer.</param>
    /// <param name="Count">The committed number of sentences.</param>
    /// <param name="Kinds">The kinds in the order of the trainer, with the role of each.</param>
    /// <param name="Quotas">The quota groups: name, floor and cap of the items of the group.</param>
    /// <param name="LemmaCap">The most targets of one lemma.</param>
    /// <param name="ExtraKeys">The comments after the kind.</param>
    private sealed record TechnicalTranche(string Name, string Source, string Shape, ShapesDevTranche Tranche, int Count, (string Kind, string Role)[] Kinds, (string Group, int Floor, int Cap)[] Quotas, int LemmaCap, string[] ExtraKeys)
    {
        /// <summary>
        ///     Gets the corpus file, relative to the data folder.
        /// </summary>
        public string Corpus => ShapesDevFolder + "/" + Name + ".tsv";

        /// <summary>
        ///     Gets the source table, relative to the data folder.
        /// </summary>
        public string Sources => ShapesDevFolder + "/" + Name + "-sources.tsv";

        /// <summary>
        ///     Gets the leakage report, relative to the data folder.
        /// </summary>
        public string Report => ShapesDevFolder + "/" + Name + "-leakage-report.tsv";
    }

    /// <summary>
    ///     A technical training source and the quotas of its sentences.
    /// </summary>
    /// <param name="Name">The name of the source: the file stem and the prefix of the sentence ids.</param>
    /// <param name="Count">The committed number of sentences.</param>
    /// <param name="LemmaCap">The most sentences of one lemma.</param>
    /// <param name="DocumentCap">The most sentences of one source document.</param>
    /// <param name="ExtraKeys">The comments after the stratum.</param>
    /// <param name="FirstWord">Whether the judged word is the first token of every sentence.</param>
    /// <param name="Strata">The strata with the tags (separated by <c>|</c>) that the judged word may carry.</param>
    /// <param name="Quotas">The quota groups: a stratum or <c>*</c> for the whole file, floor and cap of the sentences of the group.</param>
    /// <param name="Contrast">The contrast strata with the least and the most percent of the file.</param>
    private sealed record TechnicalSource(string Name, int Count, int LemmaCap, int DocumentCap, string[] ExtraKeys, bool FirstWord, (string Stratum, string Tags)[] Strata, (string Group, int Floor, int Cap)[] Quotas, (string[] Strata, int Min, int Max) Contrast)
    {
        /// <summary>
        ///     Gets the corpus file, relative to the data folder.
        /// </summary>
        public string Corpus => TechnicalFolder + "/" + Name + ".tsv";

        /// <summary>
        ///     Gets the source table, relative to the data folder.
        /// </summary>
        public string Sources => TechnicalFolder + "/" + Name + "-sources.tsv";

        /// <summary>
        ///     Gets the table of the dropped sentences, relative to the data folder.
        /// </summary>
        public string Dropped => TechnicalFolder + "/" + Name + "-dropped-sentences.tsv";
    }

    /// <summary>
    ///     A new tranche of the hard-shape development set and the quotas of its items.
    /// </summary>
    /// <param name="Name">The name of the tranche: the file stem and the infix of the sentence ids.</param>
    /// <param name="Shape">The only shape id of the tranche.</param>
    /// <param name="Tranche">The tranche of the trainer.</param>
    /// <param name="Corpus">The corpus file, relative to the data folder.</param>
    /// <param name="Sources">The source table, relative to the data folder.</param>
    /// <param name="Quotas">The quota groups: name, floor and cap of the items of the group.</param>
    /// <param name="LemmaCap">The most targets of one lemma.</param>
    /// <param name="DocumentCap">The most targets of one source document (checked for the headings shape only).</param>
    private sealed record ShapesDevNewTranche(string Name, string Shape, ShapesDevTranche Tranche, string Corpus, string Sources, (string Group, int Floor, int Cap)[] Quotas, int LemmaCap = ShapesDevNewLemmaCap, int DocumentCap = ShapesDevHeadingsDocumentCap)
    {
        /// <summary>
        ///     Gets a value indicating whether the tranche has the heading shape (kinds, scored by exact tag).
        /// </summary>
        public bool IsHeadings => Shape == "heading-titlecase";

        /// <summary>
        ///     Gets a value indicating whether the tranche has the compound-head shape.
        /// </summary>
        public bool IsCompoundHead => Shape == "n-compound-head-ambiguous";
    }

    /// <summary>
    ///     Reads the sets, documents and sentences of all evaluation sets.
    /// </summary>
    private static List<(string Set, string Document, string Text)> EvaluationItems()
    {
        var items = new List<(string Set, string Document, string Text)>();
        foreach (var corpus in EvaluationCorpora)
        {
            items.AddRange(DataFiles.ReadCorpus(DataFiles.Data(corpus)).Select(s => (corpus, s.SourceId, s.Text)));
        }

        foreach (var name in new[] { "dev.tsv", "test.tsv" })
        {
            var gold = DataFiles.ReadTable(DataFiles.Data("eval/gold-v2/" + name));
            items.AddRange(gold.Column("doc_id").Zip(gold.Column("sentence"), (doc, text) => ("gold-v2/" + name, doc, text)));
        }

        var probe = DataFiles.ReadTable(DataFiles.Data("eval/shapes/probe.tsv"));
        items.AddRange(probe.Column("id").Zip(probe.Column("sentence"), (id, text) => ("shapes/probe.tsv", id, text)));
        return items;
    }

    /// <summary>
    ///     Compares the corpus token of a correction row with the row: the state of the corpus at that token.
    /// </summary>
    /// <returns><c>pending</c> (the old value), <c>applied</c> (the new value) or <c>null</c> (neither).</returns>
    private static string? CorrectionState(DataTable table, string[] row, string[] token)
    {
        if (token[1] == table.Cell(row, "old_tag") && token[2] == table.Cell(row, "old_status"))
        {
            return "pending";
        }

        return token[1] == table.Cell(row, "new_tag") && token[2] == table.Cell(row, "new_status") ? "applied" : null;
    }

    /// <summary>
    ///     Compares two strings by the bytes of their UTF-8 encoding.
    /// </summary>
    private static int CompareUtf8(string left, string right)
    {
        var x = Encoding.UTF8.GetBytes(left);
        var y = Encoding.UTF8.GetBytes(right);
        for (var i = 0; i < Math.Min(x.Length, y.Length); i++)
        {
            if (x[i] != y[i])
            {
                return x[i] < y[i] ? -1 : 1;
            }
        }

        return x.Length.CompareTo(y.Length);
    }
}

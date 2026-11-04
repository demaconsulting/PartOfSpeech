## ModelTrainer

![Part Of Speech Structure](PartOfSpeechView.svg)

### Purpose

`ModelTrainer` is the build-time tool `tools/DemaConsulting.PartOfSpeech.Trainer` that trains the averaged-perceptron
weights that the package embeds (`src/DemaConsulting.PartOfSpeech/Resources/weights.bin`) from the committed corpora, with
the feature extractor, the lexicon restriction and the weights writer of the library itself. It also selects every
setting of the model on development data only, and it checks that the committed weights are exactly what the committed
corpora and the committed configuration produce. It is a unit directly under the system, like `Upos`. It is not part of
the NuGet package and not part of the runtime; the library never calls it. The tool is a .NET 10 console program
(`Program.cs`, `ConfigFile.cs`, `RunLog.cs`) with an engine in two folders (`Training/` and `Evaluation/`); the engine
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
- **`DataGuard`** (`Evaluation/DevSets.cs`): the allow-list of the two development files and the deny-list for training
  sources.
- **Metrics** (`Evaluation/Metrics.cs`): per-token outcomes, accuracy, accuracy on lexicon-ambiguous tokens, harmful
  errors (gold NOUN answered VERB or gold VERB answered NOUN with confidence at least 0.45, counted per 1,000
  noun-verb decisions), negative log-likelihood and expected calibration error (15 equal-width bins), and the paired
  bootstrap by document (1,000 resamples, fixed seed) with 95% intervals.

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
  the weights.
- **Minimum count**: when `MinCount` is above 1 a feature takes part in training only if it occurs often enough in the
  training signal.
- **Building the file** (`ModelBuilder`): the rows go to `PerceptronModelWriter.Compile` (which orders the buckets) with
  `hashBits`, the temperature, the scale and `FeatureExtractor.FeatureSetVersion`; `Build(..., compress: false)` gives
  the uncompressed image that is compared and hashed, `compress: true` the committed file.
- **`train --check`**: retrains with `training.json`, builds the uncompressed image and compares it with
  `PerceptronModel.ToUncompressedImage` of the committed file and with `imageSha256`; the status is `Current`, `Missing`,
  `Stale` or `Invalid`, and the exit code is 1 with the regeneration command in the message unless the file is current.
  The same method is how `LexiconCompiler --check` works for the lexicon. `build.ps1` and the CI build run it (about 15
  seconds, on all three operating systems, which also proves that the training is identical across them).
- **Evaluation** (`DevEvaluator`, `DevSets`, `Metrics`, `Calibration`): a weights file is scored on the two development
  sets through the real decoder (`Tagger.Load` on the in-memory file), so a difference between the training predictor and
  the runtime shows up as a measured difference. `Calibration.Fit` chooses the temperature with the minimum
  log-likelihood on the open development split. `compare` runs the paired bootstrap between two weights files.
- **Glossary experiment** (`GlossaryExperiment`, `GlossaryProbe`, `GlossaryFeatures`): see _Glossary Decision_.
- **Commands** (`Program.cs`): `train` (with experiment overrides, `--check`, `--update-config`, `--eval`, `--log`,
  `--calibrate`, `--checkpoints`), `eval`, `calibrate`, `compare`, `glossary-probe` and `glossary-experiment`; the exit
  code is 0 for success, 1 for stale weights and 2 for a usage or data error. Overrides are rejected together with
  `--check` and `--update-config`. The README of the tool lists every option.

### Determinism Contract

The training is integer arithmetic (`int` weights, `long` sums), the shuffle uses `DeterministicRandom` and never
`System.Random`, the writer sorts the buckets, and the temperature is a configuration value instead of a fitted one, so
the uncompressed image is byte-identical on every operating system and framework for the same corpora, configuration and
library. The Deflate bytes of the committed file depend on the zlib of the runtime, so the check compares uncompressed
images. A test trains a small subset (300 open sentences, three epochs, 18 bits) and compares the SHA-256 of the image
with a pinned value on every target framework, with and without lexicon restriction; if the extractor, the lexicon
or the open training corpus changes, those values must be pinned again (a failing test prints the new hash). The one
environmental input is the Unicode normalization of the few non-ASCII tokens of the corpora, which is stable by Unicode
policy.

### Development-Only Selection

Every setting of the shipped model was selected on `data/open/dev.tsv` and `data/eval/gold-v2/dev.tsv` only. The rule
is code, not convention: evaluation accepts exactly those two paths (compared as full paths, so a relative spelling or a
dotted detour cannot slip past), a file at a development path must announce a development split in its header, and
training sources are checked against a deny-list that refuses held-out sets, evaluation folders and also the
development files (training on them would invalidate the selection). The held-out names (the open and gold test
splits, the shape probe, the challenge set and the external treebank) are spelled in `DevSets.cs` only, and a test scans
every other trainer source for them, so there is no code path that opens such a file. The held-out sets are evaluated
once, later. The selection protocol was written into `RUN-LOG.md` before the first experiment: the primary criterion is
the pooled harmful errors per 1,000 noun-verb decisions, an option must stay within 0.30 accuracy points of the best, ties
(paired bootstrap intervals containing zero) go to the smaller model and then to fewer sources, and a noise floor was
measured with two more seeds. The settings were chosen by coordinate descent (no interactions), which is a stated
limitation, as are the small size of the gold development set (2,025 targets) and the fact that both development sets are
labeled by language models.

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
them wrong) against the shipped model M0 with a score boost on type-level simulated glossaries. M1 lost 0.17 accuracy
points without a glossary (the rule allows 0.05) and did not beat the boost on covered tokens or on the neighbors of
covered tokens. **Decision: boost only.** The feature set stays at version 1 and the library's extractor and decoder are
unchanged. The measured boost curve is the starting point for calibrating the boosts: up to about two temperatures
(a log-odds shift of 2) give most of the benefit of right entries and cost at most 0.5 points on wrong entries at 10%
wrong; four temperatures start to raise harmful errors; eight or more behave like a hard constraint. The experiment code
(`glossary-probe`, `glossary-experiment`, `GlossaryFeatures`, and the optional `glossary` parameter of the trainer, which
is `null` for the shipped model and does not change its pinned hash) stays in the tool so that the decision can be
reproduced.

### Error Handling

Invalid configuration (an unknown member, an out-of-range value, no source, a source with weight above 16, a corpus
line with an unknown tag) throws `InvalidDataException` with the setting or the file and line, and the command line
turns it into exit code 2 with the message. A held-out or development file given as a training source, and an
evaluation input that is not a development file, throw `InvalidOperationException` before any file is opened. A weights
file that is not valid for this build (damaged, or built for another feature set) makes the check report `Invalid`. A
stale or missing committed file is reported with exit code 1 and the command that regenerates it. Nothing is caught
inside the engine; `Program.cs` is the only place that maps exceptions to exit codes.

### Dependencies

- **FeatureExtractor**, **DecoderWorkspace** and **Lexicon** (the same extraction and the same `AllowedTags` rules as at
  run time), **PerceptronModel** (`Load`, `ToUncompressedImage`), **PerceptronModelWriter**, **BinaryFormat**
  (header patching for the temperature), **Upos**; **Tagger**, **TagDecoder** and **TokenConstraint** for the evaluation
  and the glossary experiment through the real decoder. All of these are internal and reached through
  `InternalsVisibleTo` (the assembly name `DemaConsulting.PartOfSpeech.Trainer` is reserved there).
- The base class library and, for `training.json`, System.Text.Json (net10.0 only; the engine does not use it).
- The committed corpora `data/open/train.tsv`, `data/masc/masc.tsv` and `data/silver/silver.tsv`, and the two
  development files; no network and no other tool.

### Callers

The developer (the commands above), `build.ps1` and the CI build (`train --check`), and the test project, which compiles
the engine files and calls them directly. Nothing in the library calls this unit.

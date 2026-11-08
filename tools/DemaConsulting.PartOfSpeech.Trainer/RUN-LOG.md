<!-- markdownlint-disable MD013 MD060 -->
<!-- cspell:ignore gamify mkabl mkarm worktree worktrees isalpha scorings numpy scipy selftest nltk -->

# Trainer Run Log

This log records the dev-only model selection of the PartOfSpeech tagger weights. It was started **before the first
experiment** with the selection rule below, and every run (including the ones that were not chosen) has a row in the
run table at the end of the file. The table is appended by the tool (`--log RUN-LOG.md --label TEXT`), so it must stay the
last thing in the file; the analysis sections that follow the experiments are therefore written in the section
"Experiment notes and decisions" before the table.

## Data discipline

Selection used **only** the two development sets: the open corpus dev split (`data/open/dev.tsv`, 1,489 sentences) and the
gold v2 dev split (`data/eval/gold-v2/dev.tsv`, 2,025 single-target tokens, `to`/`not` reported separately). The trainer
has no code path that reads anything else (`Evaluation/DevSets.cs`, tested). The open test split, gold v2 test split,
shape probe, STE challenge set and UD English-EWT were **not** opened, evaluated or inspected for any purpose during this
step. The figures of the spike (see "Spike reference") that are taken from its report are dev figures only; test
figures of the spike are not used for any choice.

## E0: pre-registered selection rule (written before any experiment)

Metrics (all through the real runtime decoder: `Tagger.Load` on the in-memory weights, lexicon restriction on):

- **Harmful error**: gold NOUN answered VERB, or gold VERB answered NOUN, with confidence >= 0.45. Reported per 1,000
  N/V decisions (decisions: gold NOUN or VERB with both tags among the decoder's candidates). Open dev counts UPOS
  NOUN/VERB; gold v2 dev counts the Noun/Verb classes (PROPN counts as Noun there).
- **Accuracy**: all scored tokens of open dev; **ambiguous-token accuracy** = tokens whose folded lower-case form has at
  least two tags in the lexicon; gold v2 dev accuracy without `to`/`not`.
- **NLL** (mean negative log probability of the gold tag among the decoder's candidates) and **ECE** (top-label
  confidence against accuracy, 15 equal-width bins), both on open dev at the temperature of the run (gold v2 dev
  values are printed by `eval`/`calibrate`).
- Model size: rows, weights, uncompressed and deflated KB; training seconds.

Temperature. Confidence-based metrics (harmful errors at 0.45, ECE, NLL) depend on the softmax temperature, so every
experiment run is scored at **its own temperature, fitted by `train --calibrate`**: minimum NLL on open dev over the
geometric grid of `Calibration.Fit`. The temperature never changes a chosen tag and is therefore not a modeling
decision; the committed temperature is chosen once by `calibrate` on the final model (E7).

Decision rule, applied in this order:

1. **Primary**: the option with the lowest **pooled harmful errors per 1,000 N/V decisions** (open dev + gold v2 dev,
   pooled = all N/V decisions of both sets together, so the open set dominates by token count; this is the "pooled
   harmful per 1k N/V" column).
2. **Accuracy floor**: an option is only acceptable if its pooled accuracy (all scored tokens of both sets together, as
   in the `compare` pooled line) is not below the best option's pooled accuracy by more than **0.30 points**.
3. **Ties**: two options are *tied* when the paired bootstrap by document (`compare`, 1,000 resamples, fixed seed) of
   their pooled difference has a 95% interval containing 0 for **both** harmful per 1k and accuracy. Among tied options
   prefer the **smaller model** (fewer deflated bytes), then the **fewer sources**, then the simpler setting. An option
   with a significantly better accuracy and not-worse harmful errors is not a tie.
4. **Noise floor**: the E0 baseline is retrained with two other seeds; differences in a single-run comparison that are
   smaller than the spread between the three seeds are treated as ties even when a bootstrap interval excludes 0
   narrowly (the bootstrap resamples documents, not training randomness).
5. **Size**: the model must fit **about 1 MB deflated or less**. Size knobs (hash bits, minimum count, magnitude pruning,
   scale) are chosen as the smallest setting that stays within the accuracy floor of rule 2 relative to the unpruned
   model and is tied to it by rule 3; if the 1 MB target cannot be met within the floor, the trade-off is reported.
6. **Epochs** are chosen from one run with checkpoints (`--checkpoints`): the fewest epochs whose pooled accuracy is within
   0.05 points of the best checkpoint; the choice is confirmed by a dedicated run compared with `compare`.
7. **Glossary feature (E8)** uses the thresholds of the plan (section "E8 glossary decision" below), fixed before the
   experiment: the feature is adopted only if all three conditions hold.
8. Coordinate descent in the order E1..E7; interactions between steps are not explored (stated limitation). Known
   limitation: gold v2 dev has only 2,025 targets and is the only out-of-domain dev evidence (UD-EWT is off-limits), so
   data sources that mainly help out-of-domain text (MASC) may look neutral on dev; the rule decides on that evidence
   and phase 5 will show the truth on the sets that are not touched here.

Experiment plan (one run at a time, single thread, below-normal priority):

| Step | Question | Runs |
| --- | --- | --- |
| E0 | Baseline: open train only, spike settings (hash 21, 10 epochs, S=16, unrestricted training); seed spread (2 more seeds) | 3 |
| E1 | Training restriction `none` (A), `history` (B), `full` (C); gold history | 3-4 |
| E2 | Data mix: open; +MASC; +silver; both; `disputed` masked or included; silver probability threshold; open weight 2 | 8-10 |
| E3 | Epochs 4..15 from one run with checkpoints | 1-2 |
| E4 | Hash bits 19..22 | 4 |
| E5 | Pruning: minimum feature count 1/2/3/5, magnitude threshold 0..3; size target | 6-10 |
| E6 | Quantization scale 8/16/32/64 | 4 |
| E7 | Temperature: `calibrate` on open dev; NLL and ECE on open dev and gold v2 dev | 1 |
| E8 | Glossary: boost curves of the final model versus a trained "glossary says X" feature | 1 train + probes |
| F | Freeze: `train --update-config`, `train --check` twice, size and time | 2 |

## Spike reference (dev figures only)

The spike (the earlier Python study, 13-class tag space, scored tokens of its open dev split, which differs slightly in
tag space and tokenization from this 17-tag UPOS setting) logged these **dev** figures in its state file and report;
they are the only dev figures it recorded. Its test figures are not used here.

| Spike variant (dev) | Accuracy % | Ambiguous % | Notes |
| --- | ---: | ---: | --- |
| P (open only, 10 iterations), unrestricted decode | 95.91 | 94.27 | iterations 4/8/10/15: 95.69/95.87/95.91/95.94 |
| P (open only), decode restricted to the lexicon | 95.41 | 93.64 | spike lexicon missed about 2% of gold |
| P (open only) + ambiguity-class feature (ablation) | 96.67 | - | not in the specified feature set |
| H (previous Basic tagger) | 94.94 | 92.85 | the HMM-like baseline of the spike |
| P (open only), training with the restriction | 92.4 restricted | - | "broke the model" with the spike lexicon |

The spike recorded no harmful-error counts and no ECE on dev for the variants (harmful errors and ECE were test
figures; the single dev harmful remark is "Brown-full: 83 against 108" for the combo selection). NLL on dev, at a
saturated temperature of 4.0: P open 0.1319 unrestricted / 0.3015 restricted.

## Experiment notes and decisions

Tooling note: `train --calibrate` (added at the start of this step, experiments only) fits the temperature of each run on
open dev by minimum NLL before scoring; the committed temperature is chosen separately (E7). Runs were trained at
below-normal priority on a machine whose CPUs were fully loaded by other users, so the "Train s" column varies between 3 s
and 340 s for runs of similar size; it is not a performance measurement (the final timing is in the freeze section).

### E0: baseline and seed noise

Open train only, hash 21, 10 epochs, S=16, unrestricted training (mode A), seed 20260101: open dev 95.53% (spike dev:
95.91 unrestricted / 95.41 restricted; the 13-class space is easier than 17-tag UPOS), ambiguous 94.41%, 113 harmful, gold
dev 94.45%, 450 KB deflated (1,001 KB raw), 99,511 rows. Two more seeds (11, 12): open acc 95.51 / 95.47, pooled harmful
per 1k 24.9 / 24.9 / 23.0 (seeds 20260101 / 11 / 12). **Noise floor** (rule 4): about +-0.05 points of open accuracy,
+-1 harmful per 1k pooled, and gold-dev harmful counts of 11 to 18 (up to 5 per 1k between seeds). Paired bootstrap of seed
20260101 against seed 12: pooled accuracy +0.046 [-0.082, +0.160], harmful +1.96 [-0.98, +5.20], both tied.

### E1: restriction mode (open train only)

| Mode | Open acc % | Gold acc % | Pooled harmful per 1k | Rows | Deflated KB |
| --- | ---: | ---: | ---: | ---: | ---: |
| A unrestricted training | 95.53 | 94.45 | 24.9 | 99,511 | 450 |
| **B** history from the restricted guess | 95.56 | 94.70 | 22.8 | 97,803 | 443 |
| C fully restricted | 94.74 | 94.05 | 24.1 | 72,939 | 315 |
| A, gold history | 95.36 | 93.95 | 24.3 | 92,007 | 419 |
| C, gold history | 94.55 | 93.55 | 24.3 | 68,009 | 295 |

Paired bootstrap A minus B: pooled accuracy -0.042 [-0.163, +0.072], harmful +2.16 [-0.58, +5.00] (tied; the gold-dev
harmful interval alone excludes 0, but gold-dev seed noise is of the same size, rule 4). A minus C: accuracy +0.765
[+0.560, +0.975]. **Decision: B** (tied with A, smaller, and its history matches the runtime decoder); C fails the 0.30
accuracy floor by a wide margin (-0.77 pooled; the cause was not investigated further: the lexicon covers gold, but
fully restricted updates never see the unrestricted competitors that the runtime shape candidates compete with);
gold history is worse than predicted history on gold dev (-0.75). The spike's finding that training with the restriction
breaks the model (92.4%) is reproduced only mildly here (-0.8 points), as the plan expected from the better lexicon.

### E2: data mix (restriction B, hash 21, 10 epochs, S=16; open = `open` train)

The rule's tie set is evaluated against the best option by paired bootstrap (pooled). Selected rows (full list in the
table; "dispute" = `disputed` open tokens trained instead of masked):

| Mix | Open acc % | Gold acc % | Pooled harmful per 1k | Deflated KB |
| --- | ---: | ---: | ---: | ---: |
| open x1 (B) | 95.56 | 94.70 | 22.8 | 443 |
| open x1 + MASC | 95.56 | 95.14 | 20.4 | 1,069 |
| open x1 + silver | 95.63 | 94.65 | 23.6 | 707 |
| open x1 + MASC + silver | 95.83 | 95.44 | 21.0 | 1,281 |
| open x2 + MASC + silver | 95.91 | 95.59 | 20.0 | 1,294 |
| open x3 + MASC + silver | 96.03 | 95.49 | 19.0 | 1,297 |
| open x2 + MASC | 95.81 | 95.29 | 19.8 | 1,083 |
| open x2 + silver | 95.84 | 94.65 | 22.8 | 725 |
| open x2 + MASC + silver, silver >= 95 | 95.91 | 95.54 | 21.0 | 1,221 |
| open x2 + MASC + silver, silver >= 80 | 95.98 | 95.44 | 19.8 | 1,256 |
| open x2 + MASC + silver, dispute | **96.13** | 95.54 | **19.4** | 1,342 |
| open x1 + MASC + silver, dispute | 96.00 | 95.19 | 20.0 | 1,323 |
| open x3 + MASC + silver, dispute | 96.10 | 95.24 | 20.6 | 1,349 |
| open x2 + MASC, dispute | 95.82 | 95.09 | 21.4 | 1,132 |
| open x2 + silver, dispute | 95.98 | 94.70 | 24.5 | 787 |
| open x1, dispute | 95.81 | 94.55 | 25.1 | 507 |
| open x2 + MASC + silver, dispute, silver >= 80 | 96.11 | 95.09 | 20.8 | 1,304 |
| open x2 + MASC + silver x2, dispute | 95.98 | 95.14 | 19.8 | 1,365 |

Paired bootstrap results (pooled, a minus b, accuracy in points and harmful per 1k, 95% intervals):

- **Disputed tokens**: training them (a) against masking them (b) in open x2 + MASC + silver: accuracy +0.195
  [+0.058, +0.321], harmful -0.59 [-2.72, +1.68]. Not a tie (the accuracy interval excludes 0 and exceeds the seed
  noise): **`disputed` tokens are trained, not masked** (caveat: dev `disputed` tokens carry the same LLM labeling
  idiosyncrasies as the training ones, which may flatter open dev; gold v2 dev is unchanged, 95.59 against 95.54).
- **Mix** (all with disputed training): the best by the primary rule is open x2 + MASC + silver (19.4 per 1k). Against
  open x1 only: accuracy +0.368 [+0.164, +0.574], harmful -5.69 [-9.55, -1.77] (not tied: the mix wins); against open x2 +
  silver (no MASC): accuracy +0.195 [+0.026, +0.379], harmful -5.10 [-8.34, -1.57] (MASC helps on both); against open x2 +
  MASC (no silver): accuracy +0.315 [+0.155, +0.456], harmful -1.96 [-4.43, +0.63] (silver helps accuracy); against open x1
  - MASC + silver: accuracy +0.145 [+0.014, +0.276], harmful -0.59 [-2.97, +1.92] (open weight 2 is better by accuracy);
  against open x3: tied on both (+0.050 / -1.18) and larger, so **open weight 2**. Silver weight 2: accuracy +0.166
  [+0.041, +0.294] for weight 1 (not tied, weight 1 wins).
- **Silver threshold**: all labeled against `>= 80%`: tied (accuracy +0.050 [-0.066, +0.168], harmful -1.37 [-4.08,
  +1.24]); `>= 95%` had higher harmful errors without masking, so it was not pursued. The tie goes to the smaller model:
  **silver probability >= 80**.

Decision for E3 onward: sources open x2 + MASC x1 + silver x1 (>= 80%), `disputed` trained, restriction mode B.
MASC is retained because the variants without it lose by the rule. Model size of the
mix is 1.30 MB deflated before pruning, above the 1 MB target; E5 resolves that.

### E3: epochs (selected mix, hash 21, S=16)

One 15-epoch run with checkpoints (averaged weights after each listed epoch; the rows are `E3 epochs@epN`). Pooled accuracy
(token-weighted over both dev sets): 3 epochs 95.92, 4: 95.96, **6: 96.01**, 8: 95.98, 10: 96.04, 12: 96.05, 15: 96.03.
The fewest epochs within 0.05 points of the best (96.05) is 6. A dedicated 6-epoch run reproduces the checkpoint exactly
and is tied with 10 epochs (accuracy -0.028 [-0.108, +0.053], harmful +0.79 [-0.78, +2.32]); 15 epochs against 10 is also
tied (0.000 [-0.071, +0.081]). **Decision: 6 epochs** (the spike used 10, with a different, smaller corpus).

### E4: hash bits (6 epochs, no pruning)

| Hash bits | Open acc % | Gold acc % | Pooled harmful per 1k | Rows | Deflated KB |
| --- | ---: | ---: | ---: | ---: | ---: |
| 18 | 96.05 | 95.19 | 20.6 | 169,829 | 1,021 |
| 19 | 96.10 | 95.29 | 20.8 | 212,007 | 1,114 |
| 20 | 96.07 | 95.04 | 20.2 | 238,547 | 1,175 |
| 21 | 96.08 | 95.04 | 21.6 | 253,979 | 1,222 |
| 22 | 96.18 | 95.14 | 20.2 | 261,528 | 1,258 |

Against 21 bits every smaller size is tied (18: -0.014 [-0.132, +0.107]; 19: +0.032 [-0.074, +0.141]; 20: -0.011
[-0.121, +0.097]); 22 bits is the best by accuracy (+0.103 [0.000, +0.215] against 21). Against the best (22): 19 bits is
tied (-0.071 [-0.180, +0.042], harmful +0.59 [-1.94, +3.10]); 18 bits is borderline (-0.117 [-0.234, 0.000], an interval
that only just contains 0 and a deficit that is above the seed noise of 0.05), so it is **not** treated as clean tie.
**Decision: 19 bits**, the smallest size that is tied with the best without a borderline interval. (Hash bits and
pruning interact only through collisions, which were not explored; the pruning step was run at 19 bits.)

### E5: pruning (19 bits, 6 epochs, S=16)

Magnitude pruning drops every quantized weight whose absolute value is at most the threshold (in units of 1/16); the
minimum count drops tag-independent features seen fewer than N times in the training tokens.

| Setting | Open acc % | Gold acc % | Pooled harmful per 1k | Rows | Raw KB | Deflated KB |
| --- | ---: | ---: | ---: | ---: | ---: | ---: |
| none | 96.10 | 95.29 | 20.8 | 212,007 | 2,472 | 1,114 |
| minCount 2 | 96.06 | 95.24 | 22.0 | 195,841 | 2,338 | 1,067 |
| minCount 3 | 96.03 | 95.29 | 21.4 | 176,820 | 2,174 | 1,008 |
| minCount 5 | 96.03 | 94.94 | 21.6 | 146,464 | 1,894 | 897 |
| prune 1 / 2 / 3 | 96.10 / 96.09 / 96.10 | 95.29 | 20.8 | about 207,000-210,000 | 2,362-2,436 | 1,059-1,096 |
| prune 6 | 96.07 | 95.29 | 21.0 | 200,491 | 2,233 | 996 |
| prune 10 | 96.07 | 95.19 | 21.8 | 186,987 | 2,004 | 889 |
| **prune 16** | 96.04 | 95.09 | 20.6 | 120,860 | 1,101 | 531 |
| prune 20 | 95.97 | 95.19 | 21.2 | 109,623 | 968 | 466 |
| prune 24 | 95.91 | 95.14 | 22.6 | 95,023 | 810 | 394 |
| prune 32 | 95.83 | 95.09 | 24.7 | 61,548 | 488 | 250 |
| prune 48 | 95.27 | 94.55 | 26.9 | 29,242 | 212 | 116 |

Paired bootstrap against the unpruned model (pooled accuracy / harmful per 1k): minCount 5 -0.088 [-0.194, +0.021] /
+0.79 [-1.40, +3.02] (tied); prune 10 -0.035 [-0.090, +0.017] (tied); **prune 16 -0.067 [-0.144, +0.004] / -0.20
[-1.59, +1.22] (tied)**; prune 20 -0.120 [-0.214, -0.022] (accuracy no longer tied); prune 24 -0.188 [-0.298, -0.078];
prune 32 -0.265 [-0.392, -0.131] with harmful +3.93 [+1.36, +6.36] (excluded); prune 48 -0.818. **Decision: magnitude
prune of 1.0 in real weight units (16 units at S=16)**, the largest threshold tied with the unpruned model; the minimum
count stays 1 (it is dominated by the magnitude threshold). Size target (about 1 MB deflated) is met by a wide margin:
without any pruning the model is 1.11 MB deflated, with the selected pruning about 0.5 MB.

### E6: quantization scale (19 bits, 6 epochs, prune = 1.0 real units: S=4 -> 4, 8 -> 8, 16 -> 16, 32 -> 32, 64 -> 64)

| S | Open acc % | Gold acc % | Pooled harmful per 1k | Rows | Deflated KB |
| --- | ---: | ---: | ---: | ---: | ---: |
| 4 | 96.01 | 95.09 | 21.4 | 116,928 | 425 |
| **8** | 96.07 | 95.34 | 20.2 | 119,632 | 481 |
| 16 | 96.04 | 95.09 | 20.6 | 120,860 | 531 |
| 32 | 96.04 | 95.09 | 20.4 | 121,525 | 573 |
| 64 | 96.04 | 95.09 | 20.4 | 121,814 | 607 |

S=8 against S=16: accuracy +0.046 [+0.010, +0.086], harmful -0.39 [-1.45, +0.56] (tied on harmful, marginally better and
smaller); S=32 and S=64 against 16 are exactly tied. S=4 is smaller (425 KB) but not tied: against S=8 accuracy -0.071
[-0.122, -0.017] and against the unpruned model -0.092 [-0.164, -0.018]. S=8 against the unpruned model: -0.021 [-0.095,
+0.050] (tied). **Decision: S=8** (prune threshold 8 units, 1.0 real), 481 KB deflated. A 3-bit fraction is enough; the
`int16` range is never close to saturated at this scale.

### E7: temperature

`calibrate` on open dev for the selected model (coarse quarter-octave grid then a fine 1/32-octave grid, minimum NLL):
**T = 6.727** (open dev NLL 0.13277, ECE 0.35%; the spike's P temperature was 5.4 with 13 classes and a different score
scale). At that temperature gold v2 dev has NLL 0.2423 and ECE 2.37% (gold dev is more overconfident than the open
split; a temperature fitted on gold dev would be lower in confidence, but gold dev is the smaller, harder set and the
rule fits on the open split, as in the spike). Recorded in `training.json`; row `E7 final-candidate`.

Selected configuration so far (before E8): sources open x2 + MASC x1 + silver x1 (probability >= 80%), `disputed` trained,
restriction B (`history`), predicted history, 6 epochs, 19 hash bits, minimum count 1, magnitude prune 8 units, S = 8,
T = 6.727, seed 20260101.

## E8 glossary decision (thresholds fixed before the experiment)

Question: does a Ste100Mark-style type-level glossary need model support (a trained "glossary says X" feature, model M1),
or does a calibrated `Prefer(tag, boost)` on the final model M0 suffice?

Setup (open dev only: gold v2 dev has one target per sentence and cannot show propagation to neighbors): eligible types =
word types with at least 2 lexicon tags, at least 3 dev occurrences and a stable (>= 90%) majority gold tag; scenarios:
coverage 30% and 100% of the eligible types; wrong entries r = 0, 10, 30% (a different allowed tag); fixed seeds.
M0 with `Prefer(tag, b)`, b in {0.5, 1, 2, 4, 8} (units of the model temperature = log-odds), and `Only` as the hard reference.
M1: the final configuration trained with two extra features of the hinted tag (`Glossary(tag)` and `Glossary(tag) x
PrevTag1`) present only when a hint exists; training hints are simulated per token (30% of lexicon-ambiguous tokens with a
gold tag, 10% of the hints wrong, fresh deterministic draw every epoch).

Definitions used by the rule:

- **gain(b)** = overall open-dev accuracy with the glossary minus without, at 30% coverage and r = 0; gain(hard) the same
  for `Only`.
- **b\*** = the smallest b with gain(b) >= 95% of gain(hard) and a covered-wrong loss at r = 10% of at most 2x the loss of the
  hard constraint (loss = drop in accuracy on the covered tokens whose entry is wrong; this second clause is the plan's
  and it is non-binding because the hard constraint has the largest loss).
- Adopt the feature only if **all** hold: (a) M1 without a glossary against M0: accuracy not lower by more than 0.05 points
  and harmful errors not higher (paired bootstrap by document); (b) M1 beats M0 + b\* on covered-token accuracy at r = 10%
  by >= 0.5 points, or on the uncovered tokens of the same sentences by >= 0.2 points, with a paired 95% interval
  excluding 0; (c) harmful errors with a wrong glossary (r = 10%) are not worse than for M0 + b\*.
- Otherwise: boost only; phase 4 calibrates the Prefer boosts on dev, starting from the measured curve below.

Operational details (fixed before the run, in `Evaluation/GlossaryExperiment.cs`): the primary scenario is 30% coverage
with 10% wrong entries; b\* is searched over the grid {0.5, 1, 2, 4, 8, 16}; condition (a) passes if the point
difference of M1-without-glossary minus M0-without-glossary is >= -0.05 accuracy points over all open-dev tokens and the
harmful-per-1k difference is not significantly above 0 (not: lower bound of the interval above 0); condition (b) uses the
covered tokens (right and wrong entries together) and, separately, the uncovered tokens of the same sentences, with the
paired bootstrap by document (1,000 resamples) of M1-with-glossary minus M0 + b\*; condition (c) passes if the harmful-per-1k
difference M1 minus M0 + b\* is <= 0 or its interval contains 0. M1 uses the glossary features only when a token has a hint.
All decoding of this experiment uses the experiment's own decoder (`HintedPredictor`), whose parity with the real decoder
on M0 is printed first; the no-glossary comparison M1 against M0 is also repeated through the real decoder (`compare`).

### E8 results (command `glossary-experiment`, seed 1; open dev, 613 eligible types)

The experiment's decoder equals the real decoder on M0: 0 tag mismatches over 26,232 tokens without a glossary and 0 with
`Prefer(2 x temperature)` on a 30% glossary. M0 is the model of the selected configuration (T = 6.727); M1 is the same
configuration trained with simulated hints (30% of the lexicon-ambiguous tokens, 10% of the hints wrong, fresh draws per
epoch), 111,046 rows, fitted T = 6.874. Boost b is in units of the temperature (log-odds). "Covered" = tokens of
glossary types; right/wrong = whether the entry is the gold tag; "neighbors" = uncovered tokens of sentences with a
covered token. Accuracies in percent (n of tokens in the column header of the scenario: 30% coverage, r = 10%: 3,135
covered-right, 1,719 covered-wrong, 21,236 neighbors; 30% coverage, r = 0: 4,564 / 27 / 19,853).

| System | 30% cov., r=0: covered | r=0: overall | 30%, r=10%: covered right | covered wrong | neighbors | overall | harmful | 30%, r=30%: overall | harmful |
| --- | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: |
| M0, no glossary | 99.63 | 96.07 | 99.11 | 99.59 | 95.34 | 96.07 | 94 | 96.07 | 94 |
| M0 + Prefer 0.5 | 99.80 | 96.10 | 99.36 | 99.48 | 95.34 | 96.09 | 93 | 96.07 | 92 |
| M0 + Prefer 1 | 99.89 | 96.10 | 99.52 | 99.42 | 95.34 | 96.11 | 93 | 96.06 | 93 |
| **M0 + Prefer 2** | 99.98 | 96.12 | 99.74 | 99.07 | 95.34 | 96.11 | 93 | 96.04 | 94 |
| M0 + Prefer 4 | 100.00 | 96.09 | 99.97 | 97.50 | 95.33 | 96.03 | 100 | 95.83 | 99 |
| M0 + Prefer 8 | 100.00 | 96.08 | 100.00 | 10.12 | 95.31 | 90.28 | 115 | 87.90 | 114 |
| M0 + Prefer 16 | 100.00 | 96.08 | 100.00 | 0.06 | 95.30 | 89.62 | 132 | 86.83 | 152 |
| M0 + Only (hard) | 100.00 | 96.08 | 100.00 | 0.00 | 95.30 | 89.61 | 91 (see note) | 86.83 | 96 |
| M1, no glossary | 99.54 | 95.85 | 99.11 | 99.48 | 95.08 | 95.85 | 111 | 95.85 | 111 |
| M1 + glossary features | 99.96 | 95.89 | 99.81 | 99.13 | 95.09 | 95.92 | 105 | 95.85 | 107 |

With 100% coverage and r = 10%: M0 + Prefer 2 covered-right 99.87 and covered-wrong 92.78 (no glossary: 99.48 / 95.91),
overall 96.14 against 96.07, harmful 90 against 94; Prefer 4: 99.97 / 86.53, overall 95.97; M1 features 99.90 / 90.52,
overall 95.91, neighbors 92.87 (M0: 93.18).

Decision rule, evaluated by the tool:

- b\*: gain of the hard constraint at 30% coverage and r = 0 is only +0.011 points (3 tokens), because the stable-tag types
  of the glossary are already tagged correctly 99.6% of the time. Every boost reaches 95% of that, so the rule picks the
  smallest, b\* = 0.5 (loss on wrong entries at r = 10%: 0.12 points against 99.59 for the hard constraint, so the clause
  holds). The rule is degenerate here (the benefit to measure is tiny); the curve above is the informative result.
- (a) M1 without a glossary against M0 without a glossary: accuracy **-0.221 [-0.373, -0.082]** points (needs >= -0.05),
  harmful per 1k +3.79 [+1.03, +6.84] (significantly higher): **FAIL**. Confirmed through the real decoder with `compare`
  (M1 minus M0, pooled accuracy -0.216 [-0.347, -0.086], open dev -0.221 [-0.373, -0.082], gold dev -0.149 [-0.493, +0.197]).
- (b) M1 with the glossary against M0 + b\*: covered accuracy +0.165 [-0.057, +0.399] (needs +0.5 with the interval above 0),
  neighbors -0.250 [-0.411, -0.088] (needs +0.2): **FAIL**.
- (c) harmful per 1k, M1 minus M0 + b\* at r = 10%: +2.68 [-0.24, +5.81] (105 against 93): PASS.

**Decision: boost only. The trained glossary feature is not adopted; the library's feature set (`FeatureSetVersion` 1),
`FeatureExtractor`, `TagDecoder`, `DecoderWorkspace` and the golden hashes are unchanged.** The feature costs
0.22 points of ordinary accuracy and buys nothing that a boost does not (M1 reaches 99.81% on covered-right tokens at
r = 10%, M0 + Prefer 2 99.74%, with no gain on wrong entries (99.13 against 99.07) and a loss on neighbors).

**Correction (code review of the experiment).** The first run of this experiment decided which training tokens were
eligible for a hint from the lower-cased token, which dropped the PROPN and X candidates that a capitalized token has at
run time. The eligibility now uses the same candidate mask as the decoder (`TagSelection.AllowedMask`, computed for the
token as written), and the experiment and the probe were re-run with the pre-registered thresholds unchanged. M0 and every
`Prefer` row are unaffected (they do not use the glossary-trained model; the probe output is identical). Only the M1 rows
moved: 111,046 rows instead of 112,100, fitted T = 6.874 instead of 6.727; accuracy cost of the feature without a glossary
0.22 points instead of 0.17 (harmful +3.79 per 1k, now significantly above 0, instead of +1.12); (b) covered accuracy
+0.165 instead of +0.185, neighbors -0.250 instead of -0.170; (c) +2.68 instead of +0.67 per 1k (still not significant).
Every verdict is the same (a: FAIL, b: FAIL, c: PASS), so the decision stands: boost only, feature set unchanged.

Phase 4 starting point (calibrate on dev): boosts up to about 2 x temperature (a log-odds shift of 2, about 6.5 raw score
units at T = 6.727) give nearly the full benefit on right entries (99.1 -> 99.7%) and lose at most 0.5 points on wrong
entries at r = 10% (99.6 -> 99.1%) with no net harmful-error increase; at 4 x the loss on wrong entries is 2.1 points and
harmful errors start to rise (100 against 94); from 8 x the soft constraint is a hard one (a wrong glossary then costs
3 to 12 points of overall accuracy at 10 to 30% wrong entries and 30 to 100% coverage) and harmful errors with a wrong
glossary grow (115 to 498). Note on the hard-constraint rows: with a single candidate the decision is no longer an N/V
decision, so `Only` shows few harmful errors by construction (the metric counts only decisions between noun and verb).
Limits: the glossary simulation covers one language-model-labeled split (open dev), type-level stable-tag types only, and
the effect on neighbors is through the history only; no sensitivity runs of M1 with other hint rates were made (the
plan prescribes one M1 training).

## Final configuration and results (freeze)

Frozen in `training.json` by `train --update-config`; the weights file is `src/DemaConsulting.PartOfSpeech/Resources/weights.bin`,
which the library embeds as the assembly resource `DemaConsulting.PartOfSpeech.Resources.weights.bin` (`Tagger.Default`).

| Setting | Value |
| --- | --- |
| Sources | open x2, MASC x1, silver x1 (labeled tokens with probability >= 80%); `disputed` tokens trained, not masked |
| Training restriction | B (`history`: predicted history from the restricted guess, update from the unrestricted guess) |
| Epochs, hash bits, seed | 6, 19, 20260101 |
| Pruning | minimum count 1, magnitude threshold 8 quantized units (1.0 in real units) |
| Quantization scale | S = 8 (`scale` = 0.125) |
| Temperature | 6.727 (minimum NLL on open dev) |
| Glossary | boost only (no trained feature, `FeatureSetVersion` 1 unchanged) |
| `imageSha256` | `68673a9ff48fea494991a82b6106b42e3cd63f9ef0c8c3fde4fc80477060f32a` |

Reproducibility: `train --check` twice, both exit 0 and the same SHA-256 of the uncompressed image
(`68673a9f...0f32a`). Size: 119,632 rows, 290,862 weights, **1,111,898 bytes uncompressed (1,086 KB), 492,971 bytes
deflated (481 KB)**, within the target of about 1 MB. Training time (single thread, below-normal priority, 8-CPU affinity,
on a machine that other users kept busy): 7.8 s for the logged freeze run (6,285,552 steps, 11.4 s wall clock including
the corpus load and the dev evaluation), 12.3 s and 14.1 s inside the two `--check` runs (14.9 s and 15.2 s wall clock);
peak working set about 0.2 to 0.6 GB (dense tables of 2^19 x 17 cells). Individual experiment runs took up to 340 s when
the machine was saturated; that is contention, not a property of the trainer.

Final development results (real decoder, T = 6.727; the spike's dev figures are given only where it logged them):

| Set | Accuracy % | Ambiguous-token acc. % | Harmful (N<->V at >= 0.45) | per 1k N/V decisions | NLL | ECE % |
| --- | ---: | ---: | ---: | ---: | ---: | ---: |
| Open dev (26,232 tokens) | **96.07** | 95.28 (17,700 tokens) | 94 | 21.0 (of 4,484) | 0.1328 | 0.35 |
| Gold v2 dev (2,017 targets, without `to`/`not`) | **95.34** | not computed | 9 | 14.7 (of 611) | 0.2423 | 2.37 |
| Pooled | 96.02 | - | 103 | 20.2 (of 5,095) | - | - |
| Spike P (open only, 10 iterations), open dev | 95.91 unrestricted, 95.41 restricted | 94.27 / 93.64 | not logged on dev | not logged | 0.1319 unrestricted at a saturated T = 4.0 | not logged |
| Spike H (previous Basic tagger), open dev | 94.94 | 92.85 | not logged | not logged | not logged | not logged |

Gold v2 `to`/`not` (8 targets) are right in 37.5% of the cases (convention difference between the tag classes, reported
separately as agreed). Figures are not exactly comparable with the spike: 17 UPOS tags against its 13 classes, the
corpus tokenization (clitics split) and the lexicon-based definition of "ambiguous". There is no HMM baseline number in
this repository besides the spike's H; no new HMM was trained.

Paired bootstrap by document of the final model (a) against other models (pooled; accuracy in points, harmful per 1k):

- against the E0 baseline (open only, spike settings): accuracy +0.566 [+0.350, +0.762], harmful -4.71 [-8.01, -1.22]; open
  dev +0.541 [+0.333, +0.756], gold dev +0.892 [+0.200, +1.593];
- against open-only with the chosen restriction (E1 B): +0.524 [+0.304, +0.731], harmful -2.55 [-5.68, +0.60] (tied);
- against the unpruned, 21-bit, 10-epoch version of the same mix: -0.067 [-0.178, +0.061], harmful +0.79 [-1.65, +3.43]
  (tied): the size reduction (2.9 MB raw / 1.3 MB deflated down to 1.1 MB / 0.5 MB) costs nothing measurable.

Why the dev results are not below the spike's: the dev accuracy of the open-only baseline (95.53% at 17 tags) is close to
the spike's (95.91% at 13 classes), and the mixture of the three corpora adds +0.5 points; the missing FAA text in the
silver data does not show on these dev sets (they have no maintenance-instruction text), so the question stays open for
the STE challenge set and the shape probe, which nothing in this step has looked at.

Limitations of the selection (all dev, coordinate descent, no interactions beyond what is stated): the dev sets are
LLM-labeled (open) and a small out-of-domain set (gold v2, 2,017 targets, wide intervals: e.g. harmful counts of 9 to 18
between seeds); training `disputed` tokens and a high MASC weight were chosen on that evidence; a confidence temperature
fitted on the open split leaves gold dev overconfident (ECE 2.37%); the model of the chosen configuration was trained
once per seed 20260101 (the seed spread of the baseline, not of the final model, was measured).

## E9 glossary evaluation (phase 4; protocol fixed before the run)

This section was written **before the first run of `glossary-eval`**; the results section after it only records what the
command printed and applies the two rules below without changing them.

Question: the library now has a real `Glossary` (terms with allowed tags, hard or soft, inflection, case handling) that
`Tagger.Tag(IReadOnlyList<string>, Glossary)` applies. What does it buy on development data, what do wrong entries cost
(0, 10 and 30% of the entries), how far does the effect reach into the tokens after a match, and which soft strength
should be the default? The type-level, single-word measurement of phase 3 is in the section "E8 results" and is not
repeated; the parity check below ties the new path to it.

Data discipline: the command reads the open dev split and the gold v2 dev split through `DevSets` (guarded by `DataGuard`)
and nothing else. `GlossaryEvaluation` takes the already loaded sentences, has no path parameter, and cannot open a file;
the command accepts only `--seed` and `--resamples`. The open test split, gold v2 test split, shape probe, STE challenge
set and UD English-EWT are neither opened nor evaluated. Every glossary is simulated **from the same dev text it is
scored on** (that is what a domain glossary is, but it makes the benefit in-sample); the numbers are dev figures, not
test figures.

### E9 protocol

Systems (same tokens in the same order, so the paired bootstrap applies): **no glossary**; **hard**; **soft** at strength
s in {0.5, 1, 1.5, 2, 3, 4} (units of the model temperature). The tagger is `Tagger.Load` of the embedded weights with all
16 alternatives (as `eval` does); every glossary run goes through the public `Tagger.Tag(IReadOnlyList<string>,
Glossary)`. Glossaries are case-insensitive, with inflection off in A, B, D and E (the simulation lists the surface
forms) and on in C and G. Wrong entries r = 0, 10, 30% are drawn deterministically with `DeterministicRandom`; coverage 30%
and 100% of the distinct terms; all type lists are sorted in ordinal order before the seeded shuffle. Default seed 1.

Scenarios:

- **A, noun compounds (open dev).** From each maximal run of gold NOUN/PROPN/ADJ tokens that ends in NOUN or PROPN take the
  last up to three tokens (at least two); every token must be alphabetic (a hyphen is allowed) and listed in the lexicon
  with an open-class tag (NOUN, PROPN, VERB, ADJ or ADV). Entry = lower-cased term, tag = gold tag of the last token, head =
  last token. A wrong entry draws its tag uniformly from {VERB, ADJ, the other of NOUN/PROPN}. A term also matches every
  other occurrence of the same token sequence (type-level, as in phase 3), and each occurrence is scored against its own
  gold tags. The **at-risk subset** is the matches whose first token the lexicon lists with VERB; it is reported by the
  accuracy of that first token. Seed of the scenario with number n (1 to 6 over coverage 30%/100% and r = 0/10/30%): seed + 10 + n.
- **B, single-word technical words (open dev).** `GlossaryProbe.EligibleTypes` (the phase-3 criteria) and
  `GlossaryProbe.BuildGlossary` with the phase-3 seeds (seed + n), as `Glossary` entries. **Parity check**, printed first:
  for every B scenario and every system, the tags of the real glossary path equal the tags of the hand-built
  `TokenConstraint` path of `GlossaryProbe` (0 mismatches expected), and the row figures (covered right/wrong counts and
  accuracy, neighbor count and accuracy, overall accuracy, harmful errors) equal the printed `glossary-probe` rows of the
  same system.
- **AB, the primary scenario.** The entries of A and B of the same coverage and r in one glossary (A terms have at least
  two tokens, B terms one, so no term is in both).
- **C, inflection (gold v2 dev).** Entries = lemmas of the gold Noun and Verb targets (not `to`/`not`; a lemma with both
  classes has both tags), inflection on, hard. For targets whose form differs from the lemma, per class: the share found
  by a generated form (**recall**; irregular forms are missed on purpose), the share whose gold class is among the tags
  that the matched form allows (**precision**), and the class accuracy without a glossary and with the hard glossary.
- **D, case sensitivity (open dev and gold v2 dev).** Acronym types = all-capital tokens of two to five letters whose lower
  case is a lexicon word and whose gold is NOUN or PROPN (open dev, majority gold) or Noun (gold v2); one case-insensitive
  and one case-sensitive glossary, hard. Counts over the scored tokens: homograph tokens (same letters, not all capital),
  how many each glossary matches, how many of those it forces to a tag that is not their gold, and how many it turns from
  right to wrong; the acronym tokens themselves are counted as the benefit.
- **E, gold v2 dev, head effect only.** One target per sentence, so no history effect is measurable. Entries = target forms
  of the Noun and Verb targets (majority class per form, ties Noun), right = the class, wrong = the other class (the
  costly noun-verb error), coverage 30% and 100%, r = 0/10/30%; type-level, so other tokens of a sentence can match too.
- **F, history effect (from A, B and AB on open dev).** Every scored token of a sentence with a match that is not itself
  covered is classified by its distance after the nearest preceding match: **+1**, **+2**, **+3 or later**, or **before**
  the first match. Per class: accuracy without a glossary and the change per system, with the flips right to wrong and
  wrong to right on identical tokens, for r = 0 and 10%. **Invariant**: tokens before the first match of a sentence get
  exactly the tags of the no-glossary run, in every scenario and system (0 differences required).
- **G, the non-head rule for non-nominal entries.** Phrasal verbs from gold: VERB followed directly by ADP, ADV or PART
  (alphabetic tokens). Entry = lower-cased pair, tag VERB, head = first token, inflection on, hard mode; a wrong entry draws
  its tag from {NOUN, ADJ}. Three rules for the second token: **R0** none (the library default `None`), **R1** hard limit to
  ADP, ADV, PART (`WithTokenTags`), **R2** hard limit to every tag except VERB and AUX. Scenario seed: seed + 20 + n.

Metrics per cell: accuracy over all open-dev (or gold-target) tokens, and over the categories **head right** (a covered
head whose entry contains the gold tag), **head wrong**, **non-head** (the other tokens of a match), **neighbors**
(uncovered tokens of sentences with a match; F splits them) and uncovered tokens; **harmful errors** exactly as E0 defines
them (`Metrics.Judge`, confidence at least 0.45) per 1,000 N/V decisions with the **denominator fixed to the no-glossary
run's N/V decisions**; and **confident N<->V swaps** = a gold NOUN answered VERB or a gold VERB answered NOUN with
confidence at least 0.45 whether or not both were candidates (so a hard constraint that leaves one candidate still counts),
per 1,000 of the same denominator. Paired bootstrap by document (glossary minus no glossary; 1,000 resamples; the seed of
the command) for overall accuracy (points), harmful errors and swaps (per 1,000 baseline N/V decisions).

**Pre-registered rule for the soft strength** (the default of `GlossaryOptions.SoftStrength`): the primary scenario is AB at
30% coverage. A strength is **admissible** if (i) the overall accuracy at r = 10% is not below the no-glossary accuracy
(point difference >= 0), (ii) the confident swaps at r = 10% are not above the no-glossary run (point difference <= 0), and
(iii) the overall accuracy at r = 30% is at most 0.5 points below the no-glossary accuracy. Among the admissible strengths
choose the one with the highest **covered-token accuracy at r = 0** (head and non-head tokens of all matches); strengths
within 0.1 points of the best are tied and the **smallest** of them wins. If none is admissible the smallest strength of the
grid is the default and the result says so. Hard mode is reported as the reference and stays the default mode of
`GlossaryOptions` (a curated glossary is trusted); the rule decides only the strength of soft mode. The chosen number is
written once, in the decision of the results section, and the constant `GlossaryOptions.DefaultSoftStrength` must be a
member of the evaluated grid (a test checks it). *(Rule change after the first run, 2026-11-04: this rule is kept as written
and its original decision stays in the results; the default strength now follows the rule in "Rule change after the first
run" at the end of the E9 results. The sentence above about where the number is written holds for that subsection. The
command computes both rules: the current one decides, this one is printed as the historical decision.)*

**Pre-registered bar for scenario G** (the default of the non-head rule of non-nominal terms, now `None`): primary scenario
G at 100% coverage, **covered-token accuracy** (head and second token of the matches). R1 or R2 replaces `None` only if,
against R0, the covered-token accuracy at r = 0 improves by at least **0.5 points** with a paired 95% interval (covered
tokens only, by document) that excludes 0, **and** the covered-token accuracy at r = 10% does not fall (point difference
>= 0). If both qualify the larger gain at r = 0 wins. Otherwise the default stays `None`, and this log says that the data are
too thin to justify a rule (per-token `WithTokenTags` remains the way to say it).

Limits stated in advance: the glossaries are simulated from the evaluated text (in-sample); the open dev labels are
language-model labeled and the gold v2 set has one target per sentence; the strength rule is a point rule on one scenario
(the paired intervals are reported next to it, but they do not decide); throughput and allocation are measured by tests,
not by this command; no test or held-out figure is looked at.

### E9 results (command `glossary-eval --seed 1 --resamples 1000`; development data only)

The command ran once on the embedded model in about 20 seconds (single thread, below-normal priority). During the run the
library's non-head default for non-nominal terms was `None` (R0 below) and every soft system set its own strength, so the
provisional constant did not enter. The protocol above was not changed after the run; two rules were applied to the figures
(decisions at the end). Tables show the **change against no glossary** (paired by document, 95% interval) and the accuracy of
categories; the no-glossary overall accuracy, harmful errors and N/V decisions are in the section "Final configuration and
results (freeze)" and are not repeated. The no-glossary confident swaps per 1,000 N/V decisions equal that section's harmful
errors per 1,000 (with all candidates available every confident N<->V swap of the unconstrained model is a harmful error);
they are the reference of the swap columns. Terms: coverage = share of the distinct terms; r = share of wrong entries;
"covered" = tokens of matches (head and non-head); "neighbors" = uncovered tokens of sentences with a match.

**Parity (B).** The real glossary path and the hand-built `TokenConstraint` path of `glossary-probe` (the same entries) give
0 different tags over 1,101,744 token decisions (6 scenarios x 7 systems). Sixteen of the simulated terms cannot be glossary
entries as written and were left out of the glossaries (the printed phase-3 rows were produced with them): `! " $ % & ( ) , -- . ... : ; ? dr. mr.`
(terms without a letter or digit are refused, and the tokenizer splits `dr.` and `mr.` into word and period, so a
pre-tokenized `Dr.` cannot match). The hand-built path over the complete phase-3 glossaries reproduces all 30 printed rows of
`glossary-probe` that share a system with this grid (hard is its `only`; boosts 0.5, 1, 2, 4) exactly; the real path differs
from those rows in all 30, which is exactly the effect of the 16 left-out terms (mostly punctuation: they make up about 1,700
of the covered tokens of the 30% glossary, so the figures of B are those of single *words*).

**Scenario sizes** (scored tokens per category; every row has its own seed, so the term sets differ between rows):

| Scenario | coverage % | wrong % | entries | head right | head wrong | non-head | neighbors | no match | at-risk first tokens |
| --- | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: |
| A | 30 | 0 | 502 | 544 | 0 | 651 | 8,561 | 16,476 | 163 |
| A | 30 | 10 | 502 | 496 | 70 | 673 | 8,914 | 16,079 | 178 |
| A | 30 | 30 | 502 | 384 | 152 | 640 | 8,268 | 16,788 | 151 |
| A | 100 | 0 | 1,674 | 1,779 | 0 | 2,108 | 15,438 | 6,907 | 535 |
| A | 100 | 10 | 1,674 | 1,609 | 170 | 2,108 | 15,438 | 6,907 | 535 |
| A | 100 | 30 | 1,674 | 1,249 | 530 | 2,108 | 15,438 | 6,907 | 535 |
| B | 30 | 0 | 176 | 2,872 | 27 | 0 | 19,724 | 3,609 | 0 |
| B | 30 | 10 | 180 | 2,833 | 301 | 0 | 20,061 | 3,037 | 0 |
| B | 30 | 30 | 176 | 2,935 | 652 | 0 | 20,305 | 2,340 | 0 |
| B | 100 | 0 | 597 | 9,175 | 91 | 0 | 16,700 | 266 | 0 |
| B | 100 | 10 | 597 | 8,338 | 928 | 0 | 16,700 | 266 | 0 |
| B | 100 | 30 | 597 | 6,293 | 2,973 | 0 | 16,700 | 266 | 0 |
| AB | 30 | 0 | 678 | 3,323 | 25 | 651 | 19,594 | 2,639 | 163 |
| AB | 30 | 10 | 682 | 3,247 | 362 | 673 | 19,500 | 2,450 | 178 |
| AB | 30 | 30 | 678 | 3,242 | 789 | 640 | 19,812 | 1,749 | 151 |
| AB | 100 | 0 | 2,271 | 9,894 | 73 | 2,108 | 13,941 | 216 | 535 |
| AB | 100 | 10 | 2,271 | 8,962 | 1,005 | 2,108 | 13,941 | 216 | 535 |
| AB | 100 | 30 | 2,271 | 6,767 | 3,200 | 2,108 | 13,941 | 216 | 535 |

#### AB (the primary scenario), coverage 30%

Accuracy on the covered tokens (%) and change of the overall accuracy (points, paired 95% interval), by r:

| System | covered r=0 | covered r=10 | covered r=30 | overall r=0 | overall r=10 | overall r=30 |
| --- | ---: | ---: | ---: | ---: | ---: | ---: |
| no glossary | 97.87 | 97.83 | 98.22 | - | - | - |
| hard | 98.57 | 90.78 | 82.55 | +0.11 [+0.04, +0.16] | -1.19 [-1.39, -0.98] | -2.87 [-3.12, -2.63] |
| soft 0.5 | 98.05 | 97.99 | 98.29 | +0.03 [0.00, +0.06] | +0.03 [0.00, +0.06] | +0.01 [-0.01, +0.03] |
| soft 1 | 98.17 | 98.20 | 98.29 | +0.05 [+0.01, +0.09] | +0.06 [+0.02, +0.11] | +0.01 [-0.02, +0.05] |
| soft 1.5 | 98.30 | 98.27 | 98.27 | +0.06 [+0.02, +0.11] | +0.07 [+0.02, +0.13] | +0.01 [-0.03, +0.04] |
| soft 2 | 98.40 | 98.39 | 98.16 | +0.08 [+0.03, +0.13] | +0.09 [+0.03, +0.16] | -0.02 [-0.06, +0.03] |
| soft 3 | 98.45 | 98.62 | 97.99 | +0.09 [+0.03, +0.14] | +0.12 [+0.04, +0.23] | -0.05 [-0.12, +0.02] |
| soft 4 | 98.57 | 98.32 | 97.71 | +0.11 [+0.05, +0.16] | +0.07 [-0.02, +0.17] | -0.10 [-0.18, -0.02] |

Change of the harmful errors and of the confident N<->V swaps per 1,000 no-glossary N/V decisions (swaps: absolute value, then the change with its paired 95% interval); the harmful errors of a hard system are few by construction (one candidate is no N/V decision), the swaps are not:

| System | harmful r=0 | harmful r=10 | harmful r=30 | swaps r=0 | swaps r=10 | swaps r=30 |
| --- | ---: | ---: | ---: | ---: | ---: | ---: |
| no glossary | - | - | - | 20.96 | 20.96 | 20.96 |
| hard | -2.01 [-4.22, -0.45] | -2.45 [-5.16, -0.23] | -1.12 [-3.79, +1.42] | 18.96 (-2.01 [-4.22, -0.45]) | 31.67 (+10.70 [+6.31, +14.94]) | 42.37 (+21.41 [+15.92, +27.29]) |
| soft 0.5 | -0.45 [-1.14, 0.00] | -0.22 [-0.94, +0.51] | -0.45 [-1.31, +0.45] | 20.52 (-0.45 [-1.14, 0.00]) | 20.74 (-0.22 [-0.94, +0.51]) | 20.52 (-0.45 [-1.31, +0.45]) |
| soft 1 | -0.45 [-1.14, 0.00] | -0.89 [-2.20, +0.25] | -0.89 [-2.27, +0.46] | 20.52 (-0.45 [-1.14, 0.00]) | 20.07 (-0.89 [-2.20, +0.25]) | 20.07 (-0.89 [-2.27, +0.46]) |
| soft 1.5 | -0.89 [-1.84, -0.20] | -1.12 [-3.00, +0.46] | -1.12 [-2.94, +0.46] | 20.07 (-0.89 [-1.84, -0.20]) | 19.85 (-1.12 [-3.00, +0.46]) | 19.85 (-1.12 [-2.94, +0.46]) |
| soft 2 | -1.56 [-3.29, -0.23] | -1.34 [-3.25, +0.44] | -0.89 [-2.80, +0.76] | 19.40 (-1.56 [-3.29, -0.23]) | 19.63 (-1.34 [-3.25, +0.44]) | 20.07 (-0.89 [-2.80, +0.76]) |
| soft 3 | -2.01 [-4.22, -0.45] | -2.01 [-4.71, +0.43] | -0.22 [-2.73, +2.12] | 18.96 (-2.01 [-4.22, -0.45]) | 18.96 (-2.01 [-4.71, +0.43]) | 20.74 (-0.22 [-2.73, +2.12]) |
| soft 4 | -2.01 [-4.22, -0.45] | -0.22 [-3.15, +2.55] | 0.00 [-2.62, +2.42] | 18.96 (-2.01 [-4.22, -0.45]) | 20.74 (-0.22 [-3.15, +2.55]) | 20.96 (0.00 [-2.62, +2.42]) |

Accuracy (%) by token category at r = 10%:

| System | head right | head wrong | non-head | neighbors | no match in sentence |
| --- | ---: | ---: | ---: | ---: | ---: |
| no glossary | 98.86 | 97.24 | 93.16 | 95.94 | 94.04 |
| hard | 100.00 | 0.00 | 95.10 | 95.89 | 94.04 |
| soft 0.5 | 99.17 | 96.41 | 93.16 | 95.94 | 94.04 |
| soft 1 | 99.38 | 96.13 | 93.61 | 95.94 | 94.04 |
| soft 1.5 | 99.54 | 95.30 | 93.76 | 95.94 | 94.04 |
| soft 2 | 99.66 | 94.75 | 94.21 | 95.94 | 94.04 |
| soft 3 | 99.94 | 93.65 | 94.95 | 95.93 | 94.04 |
| soft 4 | 99.97 | 89.78 | 94.95 | 95.93 | 94.04 |

#### AB, coverage 100%

| System | covered r=0 | covered r=10 | covered r=30 | overall r=0 | overall r=10 | overall r=30 |
| --- | ---: | ---: | ---: | ---: | ---: | ---: |
| no glossary | 98.02 | 98.02 | 98.02 | - | - | - |
| hard | 98.61 | 90.87 | 72.61 | +0.27 [+0.16, +0.39] | -3.31 [-3.63, -2.98] | -11.90 [-12.39, -11.37] |
| soft 0.5 | 98.19 | 98.16 | 98.11 | +0.08 [+0.03, +0.12] | +0.06 [+0.02, +0.11] | +0.04 [+0.01, +0.08] |
| soft 1 | 98.39 | 98.33 | 98.24 | +0.18 [+0.11, +0.24] | +0.14 [+0.08, +0.21] | +0.11 [+0.05, +0.17] |
| soft 1.5 | 98.49 | 98.41 | 98.25 | +0.22 [+0.15, +0.30] | +0.18 [+0.11, +0.26] | +0.11 [+0.05, +0.17] |
| soft 2 | 98.54 | 98.42 | 98.19 | +0.24 [+0.16, +0.33] | +0.18 [+0.10, +0.26] | +0.08 [0.00, +0.15] |
| soft 3 | 98.62 | 98.39 | 97.81 | +0.27 [+0.17, +0.39] | +0.17 [+0.07, +0.29] | -0.10 [-0.22, +0.01] |
| soft 4 | 98.63 | 98.21 | 97.11 | +0.28 [+0.17, +0.39] | +0.09 [-0.02, +0.21] | -0.43 [-0.55, -0.30] |

| System | harmful r=0 | harmful r=10 | harmful r=30 | swaps r=0 | swaps r=10 | swaps r=30 |
| --- | ---: | ---: | ---: | ---: | ---: | ---: |
| no glossary | - | - | - | 20.96 | 20.96 | 20.96 |
| hard | -6.02 [-9.29, -3.21] | -5.13 [-8.62, -2.08] | -1.12 [-4.35, +2.27] | 16.50 (-4.46 [-7.87, -1.54]) | 46.16 (+25.20 [+18.87, +31.75]) | 122.66 (+101.69 [+90.13, +114.27]) |
| soft 0.5 | -1.34 [-2.43, -0.39] | -1.34 [-2.43, -0.39] | -0.67 [-1.45, 0.00] | 19.63 (-1.34 [-2.43, -0.39]) | 19.63 (-1.34 [-2.43, -0.39]) | 20.29 (-0.67 [-1.45, 0.00]) |
| soft 1 | -2.68 [-4.54, -1.01] | -2.45 [-4.34, -0.66] | -1.78 [-3.52, 0.00] | 18.29 (-2.68 [-4.54, -1.01]) | 18.51 (-2.45 [-4.34, -0.66]) | 19.18 (-1.78 [-3.52, 0.00]) |
| soft 1.5 | -3.57 [-5.66, -1.60] | -3.12 [-5.25, -0.99] | -2.01 [-4.27, +0.21] | 17.40 (-3.57 [-5.66, -1.60]) | 17.84 (-3.12 [-5.25, -0.99]) | 18.96 (-2.01 [-4.27, +0.21]) |
| soft 2 | -4.24 [-6.91, -1.86] | -3.35 [-6.05, -0.89] | -2.45 [-5.28, +0.22] | 16.73 (-4.24 [-6.91, -1.86]) | 17.62 (-3.35 [-6.05, -0.89]) | 18.51 (-2.45 [-5.28, +0.22]) |
| soft 3 | -4.46 [-7.87, -1.61] | -2.90 [-6.36, +0.23] | +0.45 [-3.15, +4.12] | 16.50 (-4.46 [-7.87, -1.61]) | 18.06 (-2.90 [-6.36, +0.23]) | 21.41 (+0.45 [-3.15, +4.12]) |
| soft 4 | -4.24 [-7.54, -1.38] | -2.23 [-5.84, +0.93] | +4.24 [+0.86, +7.66] | 16.73 (-4.24 [-7.54, -1.38]) | 18.73 (-2.23 [-5.84, +0.93]) | 25.20 (+4.24 [+0.86, +7.66]) |

| System (r = 10%) | head right | head wrong | non-head | neighbors | no match in sentence |
| --- | ---: | ---: | ---: | ---: | ---: |
| no glossary | 99.10 | 96.32 | 94.26 | 94.74 | 72.69 |
| hard | 100.00 | 0.00 | 95.40 | 94.71 | 72.69 |
| soft 0.5 | 99.33 | 95.52 | 94.45 | 94.74 | 72.69 |
| soft 1 | 99.55 | 94.73 | 94.83 | 94.75 | 72.69 |
| soft 1.5 | 99.68 | 94.53 | 94.88 | 94.75 | 72.69 |
| soft 2 | 99.77 | 93.43 | 95.07 | 94.74 | 72.69 |
| soft 3 | 99.89 | 91.24 | 95.40 | 94.75 | 72.69 |
| soft 4 | 99.92 | 88.66 | 95.49 | 94.75 | 72.69 |

#### A (compounds only) and B (single words only), change of the overall accuracy (points) and of the confident swaps (per 1,000)

| Scenario, coverage | System | overall r=0 | overall r=10 | overall r=30 | swaps r=0 | swaps r=10 | swaps r=30 |
| --- | --- | ---: | ---: | ---: | ---: | ---: | ---: |
| A, 30% | hard | +0.10 [+0.06, +0.13] | -0.16 [-0.26, -0.08] | -0.53 [-0.66, -0.40] | -1.34 | +2.68 | +8.92 |
| A, 30% | soft 1.5 | +0.02 [+0.01, +0.04] | +0.04 [+0.01, +0.07] | +0.02 [-0.01, +0.05] | -0.22 | -1.12 | -0.89 |
| A, 30% | soft 4 | +0.08 [+0.05, +0.12] | +0.09 [+0.04, +0.14] | +0.02 [-0.02, +0.06] | -1.34 | -1.78 | -0.67 |
| A, 100% | hard | +0.25 [+0.18, +0.32] | -0.41 [-0.55, -0.30] | -1.85 [-2.10, -1.62] | -4.91 | +7.14 | +30.11 |
| A, 100% | soft 1.5 | +0.13 [+0.08, +0.17] | +0.11 [+0.06, +0.15] | +0.07 [+0.03, +0.12] | -3.12 | -2.90 | -2.01 |
| A, 100% | soft 4 | +0.23 [+0.16, +0.29] | +0.19 [+0.12, +0.25] | +0.06 [0.00, +0.13] | -4.68 | -3.57 | -1.12 |
| B, 30% | hard | +0.01 [-0.04, +0.06] | -1.04 [-1.23, -0.87] | -2.39 [-2.62, -2.16] | -0.89 | +8.47 | +13.60 |
| B, 30% | soft 1.5 | +0.04 [0.00, +0.09] | +0.04 [-0.01, +0.09] | -0.01 [-0.05, +0.02] | -0.67 | 0.00 | -0.22 |
| B, 30% | soft 4 | +0.02 [-0.03, +0.07] | -0.01 [-0.08, +0.09] | -0.12 [-0.19, -0.05] | -0.89 | +1.34 | +1.12 |
| B, 100% | hard | +0.03 [-0.07, +0.14] | -3.17 [-3.47, -2.87] | -11.15 [-11.65, -10.64] | -0.67 | +24.31 | +90.77 |
| B, 100% | soft 1.5 | +0.11 [+0.05, +0.17] | +0.09 [+0.04, +0.16] | +0.03 [-0.02, +0.09] | -1.12 | -0.89 | -0.45 |
| B, 100% | soft 4 | +0.08 [-0.02, +0.18] | -0.10 [-0.20, +0.02] | -0.53 [-0.65, -0.42] | -0.89 | +0.89 | +4.91 |

(The swap columns are changes against no glossary; the full A and B tables, with every strength, the intervals of the swaps
and the category tables, are printed by the command.)

**A, at-risk subset** (the first token of a compound is listed with VERB in the lexicon; accuracy of that first token, %):

| System | cov 30% r=0 (n=163) | cov 30% r=10 (n=178) | cov 30% r=30 (n=151) | cov 100% r=0 (n=535) | cov 100% r=10 (n=535) | cov 100% r=30 (n=535) |
| --- | ---: | ---: | ---: | ---: | ---: | ---: |
| no glossary | 88.34 | 88.76 | 88.74 | 89.91 | 89.91 | 89.91 |
| hard | 93.87 | 94.94 | 93.38 | 94.02 | 94.02 | 93.27 |
| soft 0.5 | 88.96 | 88.76 | 89.40 | 90.47 | 90.47 | 90.09 |
| soft 1 | 89.57 | 89.89 | 91.39 | 91.40 | 91.40 | 90.84 |
| soft 1.5 | 89.57 | 90.45 | 92.05 | 91.59 | 91.59 | 91.03 |
| soft 2 | 90.80 | 92.13 | 92.05 | 92.34 | 92.34 | 91.78 |
| soft 3 | 92.64 | 94.94 | 92.72 | 93.64 | 93.64 | 92.90 |
| soft 4 | 93.87 | 94.94 | 93.38 | 94.02 | 94.02 | 93.27 |

**F, history effect** (scenario AB at 30% coverage; change of accuracy in points on identical tokens, with the flips right to wrong / wrong to right in parentheses; no-glossary accuracy of the class in the fourth column):

| r % | uncovered tokens | tokens | no glossary % | hard | soft 0.5 | soft 1 | soft 1.5 | soft 2 | soft 3 | soft 4 |
| --- | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: |
| 0 | before the first match | 5,271 | 95.47 | 0.00 (-0/+0) | 0.00 (-0/+0) | 0.00 (-0/+0) | 0.00 (-0/+0) | 0.00 (-0/+0) | 0.00 (-0/+0) | 0.00 (-0/+0) |
| 0 | +1 after a match | 2,996 | 96.70 | 0.00 (-0/+0) | 0.00 (-0/+0) | 0.00 (-0/+0) | 0.00 (-0/+0) | 0.00 (-0/+0) | 0.00 (-0/+0) | 0.00 (-0/+0) |
| 0 | +2 after a match | 2,392 | 96.28 | 0.00 (-0/+0) | 0.00 (-0/+0) | 0.00 (-0/+0) | 0.00 (-0/+0) | 0.00 (-0/+0) | 0.00 (-0/+0) | 0.00 (-0/+0) |
| 0 | +3 or later | 8,935 | 96.17 | 0.00 (-0/+0) | 0.00 (-0/+0) | 0.00 (-0/+0) | 0.00 (-0/+0) | 0.00 (-0/+0) | 0.00 (-0/+0) | 0.00 (-0/+0) |
| 10 | before the first match | 4,333 | 94.92 | 0.00 (-0/+0) | 0.00 (-0/+0) | 0.00 (-0/+0) | 0.00 (-0/+0) | 0.00 (-0/+0) | 0.00 (-0/+0) | 0.00 (-0/+0) |
| 10 | +1 after a match | 3,183 | 96.29 | -0.09 (-3/+0) | 0.00 (-0/+0) | 0.00 (-0/+0) | 0.00 (-0/+0) | 0.00 (-0/+0) | 0.00 (-0/+0) | 0.00 (-0/+0) |
| 10 | +2 after a match | 2,568 | 95.95 | -0.27 (-7/+0) | 0.00 (-0/+0) | 0.00 (-0/+0) | 0.00 (-0/+0) | 0.00 (-0/+0) | -0.08 (-2/+0) | -0.08 (-2/+0) |
| 10 | +3 or later | 9,416 | 96.28 | 0.00 (-0/+0) | 0.00 (-0/+0) | 0.00 (-0/+0) | 0.00 (-0/+0) | 0.00 (-0/+0) | 0.00 (-0/+0) | 0.00 (-0/+0) |

Scenarios A and B alone show the same pattern (at r = 10% hard: A -1 token at +1; B -2 at +1, -7/+1 at +2; soft: B -2 at +2 for
strengths 3 and 4, nothing else). **Invariant**: tokens before the first match of their sentence differ from the no-glossary
tags in 0 of 482,839 comparisons (all scenarios and systems).

#### E, gold v2 dev (one target per sentence; type-level glossaries of target forms, wrong entry = the other class)

| Coverage | System | covered r=0 | covered r=10 | covered r=30 | overall r=0 | overall r=10 | overall r=30 | swaps r=0 | swaps r=10 | swaps r=30 |
| --- | --- | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: |
| 30% | no glossary | 98.04 | 97.77 | 97.11 | - | - | - | 14.73 | 14.73 | 14.73 |
| 30% | hard | 97.06 | 84.40 | 66.88 | -0.20 [-0.46, +0.05] | -2.38 [-3.07, -1.67] | -4.66 [-5.71, -3.65] | +8.18 | +73.65 | +157.12 |
| 30% | soft 1.5 | 98.53 | 97.49 | 98.39 | +0.10 [0.00, +0.26] | -0.05 [-0.24, +0.10] | +0.20 [+0.05, +0.41] | 0.00 | +3.27 | -3.27 |
| 30% | soft 4 | 98.28 | 96.10 | 98.07 | +0.05 [-0.10, +0.21] | -0.30 [-0.66, +0.05] | +0.15 [-0.15, +0.44] | 0.00 | +4.91 | 0.00 |
| 100% | no glossary | 97.41 | 97.41 | 97.41 | - | - | - | 14.73 | 14.73 | 14.73 |
| 100% | hard | 97.25 | 86.25 | 68.85 | -0.10 [-0.86, +0.62] | -6.84 [-8.36, -5.48] | -17.50 [-19.33, -15.77] | +11.46 | +234.04 | +585.92 |
| 100% | soft 1.5 | 98.30 | 97.82 | 97.90 | +0.55 [+0.20, +0.94] | +0.25 [-0.11, +0.63] | +0.30 [+0.05, +0.59] | -4.91 | +1.64 | -1.64 |
| 100% | soft 4 | 98.71 | 97.73 | 95.79 | +0.79 [+0.25, +1.33] | +0.20 [-0.40, +0.77] | -0.99 [-1.73, -0.30] | -9.82 | +6.55 | +44.19 |

(The swap columns are changes against no glossary. At r = 0 the entry of a form is its majority class, so the minority
occurrences of forms that occur as both noun and verb are "wrong" even then; that is why the hard system loses on the covered
targets at r = 0. The full table with every strength is printed by the command.)

**C, inflection from gold lemmas** (725 lemma entries, hard; recall = found, precision = found targets whose gold class is allowed by the matched form):

| Targets | n | found % | generated form % | precision % | no glossary % | hard glossary % |
| --- | ---: | ---: | ---: | ---: | ---: | ---: |
| Noun, inflected form | 236 | 95.34 | 93.64 | 99.56 | 99.15 | 99.58 |
| Noun, base form | 515 | 100.00 | 0.00 | 100.00 | 97.28 | 99.81 |
| Verb, inflected form | 227 | 54.63 | 51.54 | 97.58 | 96.92 | 96.04 |
| Verb, base form | 240 | 100.00 | 0.00 | 100.00 | 98.75 | 100.00 |

Inflected targets that no entry found are the irregular and the longer doubling forms, as documented: `bought (buy)`, `built (build)`,
`clergymen (clergyman)`, `committed (commit)`, `crept (creep)`, `drawn (draw)`, `fell (fall)`, `felt (feel)`, `found (find)`,
`heard (hear)`, `held (hold)`, `kt (knot)` (first twelve in ordinal order).

**D, case sensitivity** (27 acronym entries, hard; over the scored tokens of open dev and gold v2 dev):

| Tokens | n | no glossary right | insensitive matched | forced wrong | right to wrong | right | sensitive matched | forced wrong | right to wrong | right |
| --- | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: |
| acronym tokens (all capitals) | 65 | 59 | 65 | 1 | 0 | 64 | 65 | 1 | 0 | 64 |
| homograph tokens (other letter case) | 263 | 257 | 263 | 256 | 250 | 7 | 0 | 0 | 0 | 257 |

**G, phrasal verbs** (915 distinct candidates, hard mode; covered = head and second token; "change vs R0" on the covered tokens, points, paired 95% interval; R0 = the library default of the time, `None`; R1 = second token limited to ADP, ADV, PART; R2 = second token not VERB or AUX):

| coverage % | wrong % | System | covered tokens | covered | head | second token | change vs R0 (covered) |
| --- | --- | --- | ---: | ---: | ---: | ---: | ---: |
| 30 | 0 | no glossary | 756 | 95.37 | 94.97 | 95.77 | - |
| 30 | 0 | R0 | 756 | 96.43 | 97.09 | 95.77 | - |
| 30 | 0 | R1 | 756 | 97.35 | 97.09 | 97.62 | +0.93 [+0.25, +1.70] |
| 30 | 0 | R2 | 756 | 96.43 | 97.09 | 95.77 | 0.00 [0.00, 0.00] |
| 30 | 10 | no glossary | 742 | 96.09 | 95.69 | 96.50 | - |
| 30 | 10 | R0 | 742 | 92.59 | 89.22 | 95.96 | - |
| 30 | 10 | R1 | 742 | 93.26 | 89.22 | 97.30 | +0.67 [-0.15, +1.61] |
| 30 | 10 | R2 | 742 | 92.72 | 89.22 | 96.23 | +0.13 [0.00, +0.44] |
| 30 | 30 | no glossary | 716 | 94.83 | 94.97 | 94.69 | - |
| 30 | 30 | R0 | 716 | 81.56 | 68.44 | 94.69 | - |
| 30 | 30 | R1 | 716 | 82.54 | 68.44 | 96.65 | +0.98 [+0.27, +1.96] |
| 30 | 30 | R2 | 716 | 81.56 | 68.44 | 94.69 | 0.00 [0.00, 0.00] |
| 100 | 0 | no glossary | 2,344 | 95.39 | 95.73 | 95.05 | - |
| 100 | 0 | R0 | 2,344 | 95.99 | 96.84 | 95.14 | - |
| 100 | 0 | R1 | 2,344 | 96.89 | 96.84 | 96.93 | +0.90 [+0.47, +1.37] |
| 100 | 0 | R2 | 2,344 | 96.03 | 96.84 | 95.22 | +0.04 [0.00, +0.14] |
| 100 | 10 | no glossary | 2,340 | 95.38 | 95.73 | 95.04 | - |
| 100 | 10 | R0 | 2,340 | 90.85 | 86.67 | 95.04 | - |
| 100 | 10 | R1 | 2,340 | 91.79 | 86.67 | 96.92 | +0.94 [+0.49, +1.45] |
| 100 | 10 | R2 | 2,340 | 90.90 | 86.67 | 95.13 | +0.04 [0.00, +0.14] |
| 100 | 30 | no glossary | 2,342 | 95.39 | 95.73 | 95.05 | - |
| 100 | 30 | R0 | 2,342 | 80.83 | 66.87 | 94.79 | - |
| 100 | 30 | R1 | 2,342 | 81.73 | 66.87 | 96.58 | +0.90 [+0.49, +1.34] |
| 100 | 30 | R2 | 2,342 | 80.87 | 66.87 | 94.88 | +0.04 [0.00, +0.14] |

#### Application of the two pre-registered rules

Soft strength rule, scenario AB at 30% coverage (accuracy changes in points, swap change per 1,000, covered-token accuracy in %):

| Strength | accuracy r=10 | swaps r=10 | accuracy r=30 | covered r=0 | admissible |
| --- | ---: | ---: | ---: | ---: | --- |
| 0.5 | +0.03 | -0.22 | +0.01 | 98.05 | yes |
| 1 | +0.06 | -0.89 | +0.01 | 98.17 | yes |
| 1.5 | +0.07 | -1.12 | +0.01 | 98.30 | yes |
| 2 | +0.09 | -1.34 | -0.02 | 98.40 | yes |
| 3 | +0.12 | -2.01 | -0.05 | 98.45 | yes |
| 4 | +0.07 | -0.22 | -0.10 | 98.57 | yes |

**Original decision of the first run (soft strength): 4. Superseded by the rule change after the first run, below.** All six
strengths are admissible; the highest covered-token accuracy at r = 0 is the
grid maximum, and the next strength (3) is 0.12 points below it, outside the 0.1 tie. `GlossaryOptions.DefaultSoftStrength`
was set to this value after the first run; the current value is stated in the rule-change subsection below. The default mode stays
**hard**. Observations that were **not** part of the rule and did not change it at the time: 4 is the edge of the grid, so the rule cannot
say whether more would be better; with a wrong share of 30% at 100% coverage a strength of 4 costs accuracy and adds
confident swaps (the AB 100% table: -0.43 points and +4.24 swaps per 1,000, intervals excluding 0), which strengths up to 2 do
not; at the primary scenario's own 30% coverage the margin of 4 over 3 is small (covered accuracy at r = 0: 0.12 points). A user
who doubts the glossary's quality can set a lower `SoftStrength`. (These observations are what the rule change below acts on.)

**Decision (non-head rule): the bar is met by R1**, so the non-nominal default changes from `None` to the particles-only rule
(`GlossaryConstraints.NonNominalRule` is now `ParticlesOnly`). At coverage 100% against R0 the covered-token accuracy changes by
+0.90 points at r = 0 (paired interval [+0.47, +1.37], bar +0.5 with the interval above 0) and by +0.94 at r = 10% (bar: not
lower); R2 gains +0.04 [0.00, +0.14] and does not meet the bar. The gain is the second token of the pair (about 95 to 97%) and
is the same with 30% wrong entries, because the rule does not depend on the head entry. Scope of the evidence, not a result of
the rule: the data are two-token phrasal verbs (gold VERB followed by ADP, ADV or PART) with the head on the first token
(about 900 distinct candidates, 2,344 covered tokens at 100%). The shipped scope of the rule is narrower than the default it
replaced: it applies only to an entry whose tags contain VERB, whose term has exactly two tokens and whose head is the first
token (the second token is then the non-head token), and every other non-head token of a non-noun term (such as `it` in
`take it apart`) is left to the model, because other shapes (adjective phrases, longer verb phrases, a verb term with
the head last) were not measured; `WithTokenTags` overrides it per token. The head of a verb entry is the first token by default
when every later token is in a closed particle list that the lexicon confirms (`shut down` and `carry out` are inferred, `take care`
keeps the last token) and `WithHead` overrides it; that choice of head is a design decision and was not measured here.

Limits of this evaluation (as stated in advance): in-sample glossaries on one LLM-labeled split and the small gold v2 set; type-level
matching, so the terms apply to every occurrence; punctuation types and `dr.`/`mr.` were not representable (parity paragraph);
the effect on neighbors is small (F) because the model usually agrees with the glossary where it matters; no test or held-out
figure was used.

#### Consistency re-run with the adopted defaults

After the two decisions were applied to the library (the non-head default and the default strength), the command was run
again with the same seed and resamples, to see what the shipped state does. The rule was applied to every non-nominal entry
in that run and narrowed afterwards (see the decision above), so the cells of wrong ADJ or other-noun-kind entries below were
measured under the wider rule. Every figure above that does not involve a wrong
entry of a non-nominal term is unchanged (all r = 0 cells, scenario B, E, C, D, and the right-entry rows), because the
adopted rule only touches the non-head tokens of entries whose tags are not NOUN or PROPN, and in A and AB only the wrong
entries (a VERB, ADJ or other-noun-kind tag) are such entries. Those cells move a little, mostly for the worse (the second
token of a wrong compound entry is now pushed towards a particle). The original pre-registered strength rule gives the **same
decision** as in the first run (all six strengths admissible, 4 chosen; superseded by the rule change below); its table changes only in the r = 30% column. The G comparison against
R0 now compares R1 with itself, so it is not repeated.

| Strength (AB, 30%) | accuracy r=10 | swaps r=10 | accuracy r=30 | covered r=0 | admissible |
| --- | ---: | ---: | ---: | ---: | --- |
| 0.5 | +0.03 | -0.22 | 0.00 | 98.05 | yes |
| 1 | +0.06 | -0.89 | 0.00 | 98.17 | yes |
| 1.5 | +0.07 | -1.12 | 0.00 | 98.30 | yes |
| 2 | +0.09 | -1.34 | -0.02 | 98.40 | yes |
| 3 | +0.12 | -2.01 | -0.06 | 98.45 | yes |
| 4 | +0.07 | -0.22 | -0.12 | 98.57 | yes |

Changed cells of the shipped state (overall accuracy change in points with the paired interval; swaps per 1,000 as the change
against no glossary): AB at 30% coverage, hard: -1.41 [-1.65, -1.17] at r = 10% and -3.35 [-3.64, -3.03] at r = 30%; soft 4:
+0.07 [-0.02, +0.17] and -0.12 [-0.21, -0.04]. AB at 100%, hard: -3.80 [-4.21, -3.40] and -13.35 [-13.94, -12.74]; soft 4:
+0.08 [-0.04, +0.20] and -0.50 [-0.63, -0.37], with +4.46 [+0.98, +8.01] swaps at r = 30%. The at-risk first tokens of scenario A
with a hard glossary and wrong entries lose accuracy (A at 100% coverage: 88.60 at r = 10% and 77.01 at r = 30%), soft strength 4
keeps most of it (93.83 and 92.90). The history pattern (F) is unchanged apart from the tokens of the wrong compound entries
(A at r = 10%, hard: -2 tokens at +1).

#### Rule change after the first run (2026-11-04): the default soft strength is 2

This subsection records a change that was made **after** the first run and after the original decision above. The
pre-registered rule, the first-run tables and the original decision (4) are left as they were written; nothing was re-run and
the grid was not extended.

**Reason (objective mismatch).** The pre-registered rule chose the strength with the highest covered-token accuracy when
every entry is right (r = 0), among strengths that were admissible at 30% coverage. Soft mode exists for glossaries that are
not curated and may be wrong (a curated glossary uses hard mode), so an objective that is measured on right entries only rewards
the property that hurts when entries are wrong. The observations of the original decision already pointed there: with 30%
wrong entries at 100% coverage the largest strengths cost accuracy and add confident swaps.

**New rule.** The default is the largest strength of the grid {0.5, 1, 1.5, 2, 3, 4} for which, in scenario AB at 100% coverage
with 30% wrong entries, (i) the change of the overall accuracy against no glossary is not worse than zero (the upper bound of
the paired 95% interval is at least 0) and (ii) the confident swaps per 1,000 do not rise (the point change is at most 0).

**Application to the existing "AB, coverage 100%" tables** (first run; r = 30% columns):

| Strength | overall accuracy change r=30 | swaps change r=30 | (i) interval upper >= 0 | (ii) swaps <= 0 | passes |
| --- | ---: | ---: | --- | --- | --- |
| 0.5 | +0.04 [+0.01, +0.08] | -0.67 | yes | yes | yes |
| 1 | +0.11 [+0.05, +0.17] | -1.78 | yes | yes | yes |
| 1.5 | +0.11 [+0.05, +0.17] | -2.01 | yes | yes | yes |
| 2 | +0.08 [0.00, +0.15] | -2.45 | yes | yes | yes |
| 3 | -0.10 [-0.22, +0.01] | +0.45 | yes | no | no |
| 4 | -0.43 [-0.55, -0.30] | +4.24 | no | no | no |

Strength 4 fails the overall-accuracy condition (upper bound -0.30) and the swap condition; strength 3 passes the accuracy
condition only because its interval just reaches 0, and fails the swap condition (+0.45); strength 2 passes both. **Decision
(soft strength, current): 2.** `GlossaryOptions.DefaultSoftStrength` is set to this value (this and the constant are the only
places where the number is written; other documents link here), and it is a member of the evaluated grid (a test checks it).
The default mode stays **hard**, as in the original decision. The consistency re-run above printed the changed AB 100% cells of
hard and of soft 4 only (soft 4 still fails: -0.50 [-0.63, -0.37] and +4.46 swaps); the rule change was applied to the
first-run table as decided, not to a new run. The limits of the evaluation stated above (in-sample, simulated glossaries,
development data) apply to this choice too, and a point rule on one scenario remains a point rule.

**Reproducibility (added with the code change that follows this decision).** The command `glossary-eval` now implements the
new rule (`GlossaryEvaluation.ChooseSoftStrength`): it prints the table above for the scenario AB at 100% coverage with 30%
wrong entries and a "Decision (soft strength)" line that names the chosen strength, says whether the constant is a member of
the grid and whether it equals the chosen strength. The first-run rule is still computed from the same run and printed
afterwards as the "Historical decision (soft strength, rule of the first run)", so the first-run decision (4) stays
reproducible and is no longer the one that the command presents as the decision. A re-run with the same seed and
resamples (shipped state: the particles-only rule for two-token phrasal verbs, the head inference of the entries) printed
the decision 2 and printed the "AB, coverage 30%" and "AB, coverage 100%" tables of overall accuracy change, confident
swaps and category accuracy equal to the first-run tables of this section; no recorded figure was changed. A test checks the
rule on the recorded cells and that its outcome is the constant.

What hard mode costs when entries are wrong is in the same tables (the hard rows of "AB, coverage 100%" and of scenario E):
hard mode trusts the entries completely, so a wrong entry forces a wrong tag. That is the intended behavior for a curated
glossary and the reason soft mode exists for a glossary that is not curated.

## Phase 5 protocol (written before any threshold curve or held-out number exists)

This section was committed **before** the first run of `thresholds` and before any held-out file was read by the new
code. It fixes the definitions, the rule that selects the confidence thresholds, the grid and the stop conditions. The
sections that record results only apply this text; nothing in it is changed after a number has been seen. The model
(`weights.bin`, `imageSha256` `68673a9f...0f32a`, temperature 6.727) and `training.json` are frozen: the held-out
evaluation does not retrain, recalibrate, reweigh or re-threshold anything.

### Definitions

- **Decision**: a scored token whose gold label is NOUN or VERB (open sets, UD) or the class Noun or Verb (gold v2, where
  PROPN counts as Noun) and for which both tags are among the decoder's candidates (the chosen tag and its alternatives).
  This is the `NounVerb` flag of the existing outcome; the pooled development set has 5,095 decisions (open dev 4,484,
  gold v2 dev without `to`/`not` 611).
- **Answered at t**: the confidence of the chosen tag is at least t.
- **Harmful at t**: a decision that is answered at t with the other one of the two tags (NOUN for VERB or the reverse).
- **Coverage at t**: answered decisions divided by all decisions, in percent.
- **Harmful per 1,000 answered at t**: 1,000 times harmful at t divided by the answered decisions at t (0 when nothing is
  answered). **Harmful per 1,000 decisions** divides by all decisions instead; at t = 0.45 it is the figure of the freeze
  table above. The consistency check of the dev run: the 0.45 row must show 94 harmful for open dev, 9 for gold v2 dev and
  103 pooled.
- **Grid**: 0.45 to 0.95 in steps of 0.05, then 0.96, 0.97, 0.98 and 0.99 (15 thresholds). No other threshold is a candidate.

### Selection rule (id `nv-pooled-dev-v1`)

The rule operates on the pooled development decisions only (open dev and gold v2 dev, `to` and `not` excluded). Thresholds
are scanned in ascending order and the **first** grid threshold that satisfies both conditions is the operating point:

| Point | Harmful per 1,000 answered (pooled dev) | Coverage floor (pooled dev) |
| --- | --- | --- |
| `strict` | at most 5.0 | at least 60% |
| `balanced` | at most 10.0 | at least 85% |

If no grid threshold satisfies both conditions the point is recorded as **none**; the targets, the floors and the grid are
not relaxed afterwards, and a threshold outside the grid is not substituted. The report also shows both development sets
separately, the reliability table of the decisions and the 95% bootstrap interval of each selected point, so that the
reader sees the noise of the selection (gold v2 dev has 611 decisions and 9 harmful errors at 0.45). The selection is a
dev-only choice and therefore optimistic; this is stated in every place that quotes the thresholds.

### Process and gates

1. `thresholds` (development sets only) writes `docs/evaluation/operating-points.json` and
   `docs/evaluation/confidence-thresholds.md`. They are committed and pushed **before** any held-out number exists.
2. `evaluate --preflight` parses every held-out input and prints counts only (no tag, prediction or metric). It is the only
   thing that runs before the real run.
3. `evaluate` runs **once** (it refuses to run when `docs/evaluation/test-results.json` exists), scores the embedded model
   on the held-out sets and applies the committed operating points. It has no weights, data or threshold option and cannot
   open a held-out file without a validated `operating-points.json` that names the frozen model and the two dev files.
4. A defect of the **evaluation code** found after seeing the output may be fixed only when it is reproduced on a fixture,
   recorded in this log with its reason, and the results file is deleted on purpose for one re-run. The model, the
   configuration, the temperature and the thresholds never change. A **model** defect (a tripwire below) stops the work: it
   is reported and not tuned.

### Stop conditions (tripwires of the held-out run)

A tripwire declares a model or data defect by a rule written in advance; the response is to stop and report, not to tune.

- a held-out set cannot be parsed, or its counts differ from the documented ones (open test 1,501 sentences, gold v2 test
  3,047 targets, shape probe 560 rows, challenge set 32 sentences and 272 tokens, UD test 2,077 sentences and 25,086 words);
- the gold label is outside the decoder's candidates for more than 1% of the scored tokens of a held-out set;
- the accuracy on the open test split is more than 2.0 points below that of the open development split (96.07%) and the
  paired document interval of the difference excludes 0;
- a selected operating point has a harmful rate (per 1,000 answered) on the open test split that is more than twice its
  pooled development rate;
- the harmful errors of the shape probe at 0.45 are more than 5% of its 560 rows (more than 28);
- the expected calibration error on the open test split is above 3%.

### Held-out sets and views

- Open test split, gold v2 test split (`to` and `not` reported separately, as on dev; the `full`-agreement subset as a
  second row), shape probe (per shape), challenge set (reported separately, never merged into a headline number) and UD
  English-EWT test.
- **UD English-EWT** is a local run only: the pinned files are fetched into the git-ignored cache by
  `tools/data/fetch_ud_ewt.py` (SHA-256 pinned in `tools/data/pins/ud-ewt.tsv`), mapped to the library conventions by the
  four rules of `data/CONVENTIONS.md`, and scored on the **mapped** tags (primary view) and on the raw UD tags (second
  view). Only aggregate numbers and tag-level confusion are committed; no UD form, sentence or sentence id is committed.
  The document of a UD sentence is its `sent_id` without the trailing `-NNNN`.
- Metrics per set: accuracy (all tokens and lexicon-ambiguous tokens), noun-verb confusion, harmful errors and harmful
  per 1,000 decisions, ECE (15 equal-width bins), mean NLL (floor 1e-6), per-tag precision and recall, 95% percentile
  bootstrap intervals (1,000 resamples, seed 1) over documents (over rows where a set has no documents: shape probe,
  challenge set), the paired difference of each operating point against the 0.45 point, and the single-thread throughput
  of the default options (warm-up, then the median of three passes).
- The off-repository comparison with the WordSense taggers and the beta.6 regression check use the per-token dump of the
  same single run (`--dump-dir`, which must be outside the repository); they add no figure of those taggers to the
  repository.

### Recipe

```text
dotnet run -c Release --project tools/DemaConsulting.PartOfSpeech.Trainer -- thresholds
python tools/data/fetch_ud_ewt.py fetch
python tools/data/fetch_ud_ewt.py map
dotnet run -c Release --project tools/DemaConsulting.PartOfSpeech.Trainer -- evaluate --preflight
dotnet run -c Release --project tools/DemaConsulting.PartOfSpeech.Trainer -- evaluate --dump-dir <folder outside the repository>
```

`thresholds` and its two output files are committed first; only then are the last two commands run. The reports are
committed as produced.

### Outcome of the development selection

`thresholds` (seed 1, 1,000 resamples; development data only) ran once the engine was committed and applied the rule above
without change. The 0.45 row reproduced the frozen figures: 94 harmful errors on open dev, 9 on gold v2 dev and 103
pooled (4,484, 611 and 5,095 decisions). Result on the pooled development decisions (the full curves, the reliability
table and the bootstrap intervals are in `docs/evaluation/confidence-thresholds.md`, the selection artifact is
`docs/evaluation/operating-points.json`):

| Point | Threshold | Coverage | Harmful per 1,000 answered | Harmful of answered |
| --- | ---: | ---: | ---: | ---: |
| `strict` (at most 5.0, coverage at least 60%) | 0.95 | 80.6% | 3.9 | 16 of 4,105 |
| `balanced` (at most 10.0, coverage at least 85%) | 0.80 | 91.0% | 9.9 | 46 of 4,636 |

Both targets were met on the grid (strict: 0.90 gives 6.4 per 1,000, so 0.95 is the first threshold that qualifies;
balanced: 0.75 gives 10.8, so 0.80 is the first); nothing was relaxed. These are development figures and optimistic by
selection; the held-out run reports what the thresholds do on data that played no part in choosing them.

### Erratum found by the preflight (documented size of the UD test words)

`evaluate --preflight` (counts only: no tag, prediction or metric was produced or seen) matched every documented size
(open test 1,501 sentences, 26,185 tokens, 517 documents; gold v2 test 3,047 targets, 856 documents; shape probe 560 rows;
challenge set 32 sentences, 272 tokens; UD test 2,077 sentences) except the UD test words: it found 25,094 where the
protocol above says 25,086. The 25,086 came from the planning notes, not from the data. The pinned file
`en_ewt-ud-test.conllu` (SHA-256 verified against `tools/data/pins/ud-ewt.tsv` after a fresh fetch into a cache outside the
repository) has 2,077 sentences and 25,094 syntactic words (354 multiword-token range lines and 2 empty-node lines are not
words); three independent counts agree: the fetch script's mapper (rule counts add up to 25,094), the trainer's parser and
a stand-alone line count of the conllu file. The mismatch was therefore an error in the documented count, an evaluation-code
constant (`HeldOutData.Counts`), not a data or model defect and not a parse failure. The constant was corrected to 25,094
and a fixture test pins the documented sizes; nothing else changed (model, configuration, weights, thresholds and operating
points are untouched) and the tripwire was not treated as a model defect because no held-out label, prediction or metric
had been produced. The 25,086 in the protocol text above is left as written for the record.

### Outcome of the held-out run (one run, results committed as produced)

`evaluate` ran (BelowNormal, one process, the treebank from a cache outside the repository, the per-token records in a
folder outside the repository) and wrote `docs/evaluation/test-results.md` and `.json`, which were committed as produced.
The run printed that three stop conditions of the protocol fired. Nothing was tuned. The three findings, with the reading
of each (the results were produced once; two later runs, after the errata below, regenerated the report only):

- **`shape-probe`: 48 harmful errors at 0.45, more than 28 (5% of the 560 rows). Finding: a real limitation of the model,
  reported, not tuned.** This is the stop condition about the model's behavior and it stands: 48 of 548 answered noun-verb
  decisions (86.3 per 1,000 decisions), concentrated in the shapes `n-compound-head` (16), `n-subject-initial` (12),
  `v-imperative-initial` (12) and `n-measure` (6). No model, configuration, threshold or selection was changed in response.
- **`gold-v2-test-to-not`: gold outside the decoder's candidates for 50.00% (4 of 8 targets).** The set is the eight `to` and
  `not` targets that are a documented convention difference and are reported separately from the headline row (as on
  development). The 1% limit fires here by construction of the convention targets, not through a model defect.
- **`ud-test-raw`: gold outside the decoder's candidates for 1.68% (421 of 25,094 words).** The raw view is the treebank's
  tags before the four convention rules of `data/CONVENTIONS.md` (the mapping changes 1,070 words); the library does not
  produce these tags by design, so the raw gold lies outside the candidates by construction. The primary, mapped view is at
  0.56% (140 words), under the limit.

Interpretation of the last two: the stop-condition rule on gold
outside the candidates is meant for the headline views of a held-out set; it applies to the `to`/`not` convention view
and to the raw UD view only as views that are reported separately, and the firing of those two is not a model defect. The
protocol text above applies the 1% limit to every view; it was not changed after seeing the result, and the two findings are
reported as fired.

All other stop conditions held: open test accuracy 96.10% against 96.07% on development; the harmful rates per 1,000
answered on open test at the strict (4.1) and balanced (9.3) points are below twice the pooled development rates (3.9 and
9.9); open test ECE 0.28%; open test gold outside the candidates 0.27%.

### Erratum: column labels of the noun-verb confusion table (presentational writer fix, report regenerated)

The first `test-results.md` (commit `e760c29`) labeled the six cells of the table "Noun-verb confusion" in the order noun,
verb, other, but `EvalTally.Confusion` stores them as the chosen tag other (0), noun (1), verb (2) after each gold tag
(`(gold - 1) * 3 + chosen`). The numbers were correct; only the six column labels were wrong (the open-test row
`59 | 2,379 | 32 | 45 | 59 | 1,839` is read as noun as other 59, noun as noun 2,379, noun as verb 32, verb as other 45, verb as
noun 59, verb as verb 1,839, whose swaps 32 + 59 equal the 91 harmful errors of that row). The report writer now labels the
columns in the stored order (noun as other, noun as noun, noun as verb, verb as other, verb as noun, verb as verb), and a
fixture test pins the labels against six known distinct counts.

Procedure: the old `test-results.*` run marker was deleted and `evaluate` was run once more with the same inputs. The
evaluation of the frozen model is deterministic; no model, configuration, threshold, operating point or selection changed,
and nothing else in the evaluation logic changed. The regenerated files differ from the first ones in exactly two places:
the throughput line and the six column labels. Every other figure is identical, and the per-token records written outside
the repository are byte-identical (SHA-256 of all six dump files unchanged). The same three stop conditions fired with the
same values. The throughput of the committed report is the one of the second run: 514,620 tokens per second (the first run
measured 488,158; it is a timing, not a result of the model).

### Hardening of the evaluation guards (after the held-out run)

A code review of the held-out engine found three weaknesses in the guards. They are closed with fixture tests only; the
held-out run was not repeated and no model, configuration, threshold, operating point, scoring or metric logic, selection
artifact or committed result changed. (1) The gate now requires exactly the development files of the data manifest (each
once), exactly the points `strict` and `balanced` with the registered targets, the registered grid and grid thresholds, and,
when the development data is present, that the points equal what the registered rule selects from the development curve of
the embedded model; the committed `operating-points.json` passes it. (2) The cached mapped treebank table is verified
against the SHA-256 that the committed `test-results.json` records for it (a constant of the evaluation guard) before it is
parsed; a counts-only `evaluate --preflight` confirmed that the cache matches the pin. (3) The run-once guard is atomic:
`evaluate` creates `docs/evaluation/test-results.json.running` exclusively immediately before scoring and removes it after the
results file is written. A run that is aborted after scoring began leaves the lock behind, and the lock blocks every later run
until a person deletes it by hand; any such deletion, with its reason, must be recorded in this log.

### Second hardening of the gate (after the formal review of the pull request)

The formal review found that the gate of the previous section still depended on the artifact in two places. Both are closed
with fixture tests; the model, the configuration, the thresholds, the selection artifact and every figure are unchanged.
(1) The development derivation is no longer optional: `OperatingPointsFile.Expect` refuses a development file that is
missing or whose bytes do not have the manifest hash, and `SelectedOperatingPoints.Validate` refuses an expectation without
the derivation, so absent development data cannot skip the check. (2) The development accuracy that the open-test accuracy
stop condition compares with (`devOpenAccuracyPercent`) is recomputed from the frozen model on the open development split
and must equal the recorded value; before, the stop condition trusted the file. The committed selection passes both
(`evaluate --preflight` ran the gate on the real data). The negative target index of the shape probe was already refused
(the parser reads the index without a sign) and is now also rejected by an explicit range check and a test.

### Erratum: incomplete statistics of the report (second report regeneration)

The review found that the report did not have everything that the design and the protocol promised: the precision and
recall by tag were written for three views only, the intervals of the ambiguous-token accuracy, of the log-likelihood and
of every tag figure were missing, and `test-results.json` had none of the tag figures or of the noun-verb confusion. The
report writer now writes, for every set and view (eight in all), the interval of the ambiguous-token accuracy and of the
log-likelihood, the precision and recall of every tag that has a gold token or was chosen with 95% intervals (document
bootstrap, the same draws as the other statistics, so no existing interval can change), and the six confusion cells; the
JSON carries all of them. A precision or recall that is undefined is 0 in the figure and in every resample.

Procedure: the per-token records of the first run were not kept in a form that carries the decoder's candidates, so the
figures could not be recomputed without tagging again; the one permitted re-run was used. The old `test-results.json` (the
run marker) was deleted on purpose and `evaluate` was run once more with the same inputs, the same seed and resamples and the
same frozen model (the gate, including the new development derivation, passed first). Nothing was tuned and nothing else in
the evaluation logic changed. A comparison of the old and the new `test-results.json` found every existing member of every
set, and every top-level member, identical (model and selection hashes, input hashes, counts, accuracies, harmful errors,
calibration errors, log-likelihoods, every operating point with its intervals, the stop conditions) except the throughput
line, which is a timing: the committed report now shows 315,213 tokens per second (earlier runs: 488,158 and 514,620; the
machine was busy with other work during this run). The same three stop conditions fired with the same values. No further
re-run is permitted for a presentational change; a figure that is wrong is a defect to be reported, not regenerated.

## Corpus cleanup C: pre-registered gates (written before the retraining)

Context. The staged corrections of the convention linter (`data/corrections/mechanical.tsv`, 2,225 rows, and
`adjudicated.tsv`, 7,063 rows; 3 mechanical rows are superseded, so 9,285 effective rows) were applied to open train, dev and
test, MASC and silver (commit "apply the staged corrections", before this section), the manifest was regenerated, and the lexicon
is rebuilt and the model retrained in the commits after this one. This section was written and committed **before** any
retraining, so the git history shows that the gates were fixed first. Nothing in it is tuned afterwards.

Which labels changed (stated plainly). Only the open corpus (train, dev, **test**), MASC and silver changed. Gold v2, the shape
probe, the STE challenge set and the UD English-EWT labels did **not** change (the overlay cannot address them: the STE set is
linted but never corrected). Among the evaluation sets only the open dev and the open test labels moved, so the open test
result of a retrained model is not strictly comparable with the old record; gold v2, the shape probe, the STE challenge set
and UD-EWT are fair before-and-after comparisons. On open dev 274 tokens of 26,232 changed their tag and 213 their status cell
(flags and `disputed` marks; 465 correction rows in 1,489 sentences).

Method (a scratch harness outside the repository, `%TEMP%\pos-c`, not committed). The model is the one a user gets: weights plus
the embedded lexicon of its own tree. The "old model" is the assembly built from commit `a94abd0` (`weights.bin` 492,971 bytes,
`imageSha256` `68673a9f...0f32a`, temperature 6.727, the old lexicon: 262,667 spellings / 339,880 entries), snapshotted before any
change. A small tool tags the forms of a corpus file with `Tagger.Load` (`MaxAlternatives` = the limit) and writes the chosen tag,
the confidence and the alternatives; a Python script scores the predictions. Definitions are those of E0: a decision is a gold
NOUN or VERB token with both tags among the decoder's candidates (chosen tag and alternatives); a harmful error is a chosen tag
that is the other of NOUN and VERB with confidence >= 0.45; rates are per 1,000 decisions. Intervals are percentile intervals of
a bootstrap by document (open: `source_id`, 502 documents on dev; UD: the sentence id without its running number, 318 documents),
1,000 resamples, seed 20261104, 95%; differences are paired (the same resample for both models). UD-EWT dev is the mapped view
(`tools/data/cache/ud-ewt/ud_dev.tsv`, 25,148 tokens, the same cache and mapping as the earlier participle spike).

Baseline reproduction (acceptance test of the harness, run before any file changed). The harness reproduces the figures of the
earlier records exactly: open dev 96.07% and **94 harmful of 4,484 decisions** (20.96 per 1k) on the old labels; the trainer's
`eval` on the old weights prints the same two figures (and gold v2 dev 95.34%, 9 harmful of 611); UD-EWT dev mapped agreement
**23,227 of 25,148 = 92.361%** (interval [91.80, 92.88]; the earlier spike printed [91.80, 92.95] from a different random
sequence of the same procedure).

| Old model (a94abd0) | Tokens | Decisions | Harmful | Per 1k [95%] | Agreement / accuracy % |
| --- | ---: | ---: | ---: | --- | --- |
| open dev, old labels (reproduction) | 26,232 | 4,484 | 94 | 20.96 [16.12, 26.30] | 96.07 [95.20, 96.63] |
| open dev, **relabeled** (the G2a baseline) | 26,232 | 4,468 | 92 | 20.59 [15.62, 25.96] | 96.05 [95.24, 96.61] |
| UD-EWT dev, mapped (the G1 and G2b baseline) | 25,148 | 4,573 | 100 | 21.87 [18.03, 26.19] | 92.36 [91.80, 92.88] |

The harmful rate on UD-EWT dev was never recorded before; it is measured here, with the old model, before the retraining.

The gates (all on development data; none reads a held-out file):

- **G1 (UD-EWT dev agreement).** The new model's agreement with the mapped UD-EWT dev labels is at least the old model's:
  **23,227 correct tokens of 25,148 (92.361%, reported as 92.36%)**. Pass or fail by the point estimate. The paired bootstrap
  interval of the difference (new minus old) is reported, not used for the verdict.
- **G2 (harmful noun-verb errors, both must hold).** (a) On open dev with the relabeled gold labels, the new model's harmful
  errors per 1,000 decisions are no higher than the old model's on the same labels (**20.59**, 92 of 4,468). (b) On UD-EWT dev
  the new model's harmful errors per 1,000 decisions are no higher than the old model's (**21.87**, 100 of 4,573). Pass or fail by
  the point estimates (no tolerance); the rates, counts and intervals of both models and the paired difference are reported. The
  decision sets of the two models differ slightly (the lexicon changed); the rate per decision is the compared quantity.
- **G3 (linter).** `python tools/data/check_conventions.py --tier fix --enforce` exits 0 over open train, dev, test, MASC and silver
  (the evaluation-only STE set is shown and counts for nothing). The check tier stays report-only and its counts are reported
  (expected 298 after the application: open train 95, dev 12, test 17, MASC 66, silver 106, STE 2).
- **G4 (build).** `build.ps1` is green: Release build of every target, `LexiconCompiler --check`, `train --check` (reproducible
  image), the API-docs check and all tests on all target frameworks. The held-out evaluation record must also be consistent
  with the shipped model before G4 can be final; that part is decided after the development gates, only if they pass.

Selection rule. Only development sets are used. The shipped `training.json` settings are used unchanged (sources and weights,
restriction `history`, 6 epochs, 19 hash bits, minimum count 1, pruning threshold 8, scale 8, seed 20260101, disputed tokens
trained): **no hyperparameter is searched**. The one value that is refit is the softmax temperature, by the existing
`calibrate` on open dev with the new labels (minimum NLL; it never changes a chosen tag), exactly as the first freeze did.

Stop rule. If G1, G2 or G3 fails: nothing is tuned, no held-out file is opened or evaluated, `thresholds` and `evaluate` are not
run, and the pull request is a draft with the failure report. A passing development result does not authorize any further
tuning either.

Reproducibility of the labels and tools. The application is deterministic (a second application on a pristine copy gives the
same SHA-256 for all five files); a second `apply` is refused; `make_corrections.py --check` passes in the applied state (it
reverses the rows in a temporary copy and regenerates); the fix-tier linter finds 0 violations over all sources including the
STE set; the check tier has 298 (the counts above).

## Corpus cleanup C: results of the development gates (retrained model; verdict: G2 failed, stop rule applies)

Procedure (exactly the pre-registered one; no option other than `--update-config` was used). `dotnet build -c Release` of the
trainer (the project reference embeds the rebuilt lexicon), `train` with the shipped `training.json` (temperature still 6.727,
irrelevant for the fit), `calibrate --update-config` (open dev with the corrected labels: minimum NLL at **T = 6.583**, was 6.727;
ECE 0.222%, NLL 0.12232; gold v2 dev at that T: NLL 0.2589, ECE 2.41%), then `train --update-config --label "C retrained (frozen)"`
(this wrote the weights, the `imageSha256` and the run-table row below), and `train --check` twice (both exit 0, the same image
SHA-256). The library was rebuilt so that it embeds the new weights, and a second copy of the scratch tool (`dump-new`) was built
from that tree.

| Model | Old (a94abd0) | New (corpus cleanup C) |
| --- | --- | --- |
| Rows / weights | 119,632 / 290,862 | 116,448 / 281,682 |
| Image (uncompressed) | 1,111,898 bytes | 1,077,990 bytes (1,053 KB) |
| `weights.bin` (deflated) | 492,971 bytes (481 KB) | 477,899 bytes (467 KB) |
| `imageSha256` | `68673a9ff48fea494991a82b6106b42e3cd63f9ef0c8c3fde4fc80477060f32a` | `dca810e400d6e5cf27cdbb126db810c4ead6aaa07bb410709f3cedb515da339d` |
| Temperature | 6.727 | 6.583 |
| Lexicon | 262,667 spellings / 339,880 entries (641,418 bytes) | 262,635 / 339,823 (639,617 bytes) |
| Training | 6,285,552 steps, 7.8 s | 6,285,552 steps, 5.3 s (peak working set 271 MB) |

Gate table (development data only; all figures from the scratch harness of the pre-registration, bootstrap by document, 1,000
resamples, seed 20261104, 95% percentile intervals, differences paired):

| Gate | Old model | New model | New minus old (paired) | Verdict |
| --- | --- | --- | --- | --- |
| **G1** UD-EWT dev agreement (mapped, 25,148 tokens) | 23,227 correct, 92.361 [91.803, 92.881] | 23,338 correct, **92.803** [92.252, 93.312] | **+0.441** [+0.243, +0.628] points | **PASS** (92.803 >= 92.361) |
| **G2a** harmful per 1k, open dev, relabeled | 92 of 4,468 = **20.59** [15.62, 25.96] | 103 of 4,467 = **23.06** [17.53, 29.07] | +2.47 [0.00, +5.26] | **FAIL** (23.06 > 20.59) |
| **G2b** harmful per 1k, UD-EWT dev | 100 of 4,573 = **21.87** [18.03, 26.19] | 107 of 4,570 = **23.41** [18.77, 28.59] | +1.55 [-1.50, +4.93] | **FAIL** (23.41 > 21.87) |
| **G3** `check_conventions.py --tier fix --enforce` | - | exit 0, 0 violations in open train, dev, test, MASC, silver and STE | - | **PASS** |
| **G4** `build.ps1` | - | **FAIL** (not satisfiable under the stop rule: only the 10 held-out-record pin tests are red, see below) | - | **FAIL**; later reissued: PASS (see "owner decision" section) |

Check tier (report only, unchanged by the gate): 298 = open train 95, dev 12, test 17, MASC 66, silver 106, STE 2 (251
`be-participle-adj-not-gradable`, 26 `be-aux-passive`, 13 `infinitive-to`, 4 `be-passive-agent`, 2 `no-all-degree`, 2 `out-of-phrasal`).

**Verdict.** G1 and G3 pass; **G2 fails on both of its parts** by the pre-registered point-estimate rule. The stop rule applies:
nothing is tuned or re-selected, `thresholds` and `evaluate` were not run, and no held-out file was read. The frozen figures
`FrozenOpenHarmful` and `FrozenGoldHarmful` of `thresholds` (Program.cs) are not changed. The failure is stated plainly; the
interval of the paired difference of G2a touches 0 (lower bound 0.000) and that of G2b contains 0, and the 11 and 7 extra harmful
errors are of the size that the seed spread of E0 showed on the old dev labels (106 to 113 harmful for three seeds of one
configuration), but the gate was fixed as a point-estimate rule and is reported as failed, not re-interpreted.

Informational figures (no gate; same harness or `eval`):

| Set | Old model | New model |
| --- | --- | --- |
| Open dev, own labels: accuracy | 96.07% (old labels), 96.05% [95.24, 96.61] on the relabeled dev | **96.37%** [95.60, 96.89]; paired difference on the relabeled dev +0.320 [+0.173, +0.462] points |
| Open dev: ambiguous-token accuracy, NLL, ECE | 95.28%, 0.1328, 0.35% (old labels) | 95.64% (17,628 tokens), 0.1223, 0.22% |
| Gold v2 dev (labels unchanged; 2,017 targets) | 95.34%, harmful 9 of 611 = 14.7 per 1k, NLL 0.2423, ECE 2.37% | 94.99%, harmful 14 of 611 = 22.9 per 1k, NLL 0.2589, ECE 2.41% |
| Pooled harmful, open dev + gold v2 dev | 103 of 5,095 = 20.2 per 1k (old labels) | 117 of 5,078 = 23.0 per 1k |
| Gold outside the decoder's candidates (open dev) | 40 | 33 |

Where the extra harmful errors are (diagnostic, development data only, no model or setting is chosen with it). On the relabeled
open dev 79 harmful errors are shared by both models, 13 occur only in the old and 24 only in the new model (new: 13 gold VERB
answered NOUN and 11 gold NOUN answered VERB, among them `dabbling` 2, `feeding`, `nibble`, `hit`, `use`, `worry`, `move`, `mind`);
both models keep the same hard types (`pay`, `learning`, `diving`, `swimming`, `foraging`, `check`, `mash`, `gamify`). On UD-EWT dev
79 are shared, 21 only old, 28 only new (17 gold VERB answered NOUN, e.g. `reply` 3, `look` 2, `focus`, `rip`, `type`, `cancel`;
11 the other way). The retrained model loses mostly on base-form verbs against nouns (`reply`, `look`, `type`) on UD. The corrections
moved many conventions at once (ADJ>VERB 1,034, NOUN>PROPN 644, VERB>AUX 540, ADV>ADJ 467, PRON>DET 305 of the 9,285 changes);
whether any of them explains the loss was not tested, and nothing here is used to retune.

Consequences recorded for the pull request. (1) The held-out record (`docs/evaluation/test-results.*`, `operating-points.json`,
`confidence-thresholds.md`) still describes the old model and the old dev files; the tests that pin it
(`PartOfSpeech_Evaluation_Results_RecordTheShippedModelAndInputs`, two `OperatingPointsFile_Load_CommittedSelection_*` tests)
therefore fail on this branch by design, and G4 cannot be green until the held-out record is reissued or the model is not the
shipped one. (2) The development gates being failed, reissuing it is not authorized by the pre-registration. (3) The golden
hashes of the small trainer runs (`ModelTrainerTests`) and the size figures of the documents were re-pinned to the retrained
files; they carry no judgment.

### Corpus cleanup C: stop decision, G4 and decisions on the open questions of the plan

**Stop decision.** The pre-registered gates G1 and G3 pass and G2 fails on both of its parts (G2a open dev, G2b UD-EWT dev). By the stop
rule written before the retraining, nothing is tuned and no held-out file is read: `thresholds` and `evaluate` were not run, the
held-out record `docs/evaluation/{test-results.*,operating-points.json,confidence-thresholds.md}` is not reissued, the constants
`FrozenOpenHarmful` and `FrozenGoldHarmful` of `thresholds` are not changed, and the pull request is opened as a draft with this
report. The gate texts and verdicts above are unchanged by this section.

**G4 (build) is reported as FAIL, and cannot be satisfied on this branch.** `build.ps1` passes the Release build of every target, the
`LexiconCompiler --check`, `train --check` and the API-docs check, and runs every test on net481, net8.0, net9.0 and net10.0. The only
tests that fail are the ones that pin the held-out record to the shipped model and to the development files:
`PartOfSpeech_Evaluation_Results_RecordTheShippedModelAndInputs` (net481, net8.0, net9.0, net10.0) and
`PartOfSpeech_Evaluation_OperatingPointsFile_Load_CommittedSelection_EqualsDevDerivation` and
`..._PassesGateForFrozenModel` (net8.0, net9.0, net10.0; 10 failures in all). They fail because the held-out record names the old
model hash and the old development file hashes. This is the accepted, documented consequence of the stop rule (decision Q2), not a defect
of the retraining; it ends only when a held-out record is reissued for a model, which the pre-registration forbids after the G2 failure.
Nothing else is red.

**Decisions on the open questions of the plan** (recorded as decisions of this change; none changes a gate or a verdict):

| # | Question | Decision |
| --- | --- | --- |
| Q1 | Where does the old-versus-new comparison live? | In this run log (and the pull request description), not in `docs/evaluation/*`: `AGENTS.md` asks the documents to describe the current state once, and tests pin the figures of the evaluation record. `docs/evaluation/README.md` carries only a short status note that the record describes the earlier model and that the retrained one was not evaluated on held-out data. |
| Q2 | CI is red on the draft (the three held-out-record tests) | Accepted. The pull request is a draft, the failing tests are named in its description, and `thresholds` is not run to turn them green (it would reselect thresholds for a model that failed its gate). |
| Q3 | Tolerance for G2 | None. The gate was pre-registered as a point-estimate rule and is judged as written (23.06 > 20.59 and 23.41 > 21.87), although the paired intervals touch or contain 0. |
| Q4 | `import_silver.py verify` on the corrected silver file | Documented, no code change: `verify` describes the derivation and passes only on the pre-correction file (`corrections.py apply --write --reverse`); see `data/silver/README.md` and `tools/data/README.md`. |
| Q5 | The 1,779 new `disputed` tokens | They stay in training (`maskDisputed` false in `training.json`, as for the 7,449 earlier ones); nothing was tuned on them. |
| Q6 | Gold v2, the shape probe and the STE challenge set | Their labels are not corrected here (the overlay cannot address them); bringing them to the same conventions is left to a later change. |

Documents and tests changed with this decision (no model, setting or weight): the data READMEs and `CONVENTIONS.md` describe the applied
state, the design and verification documents state the applied state, the new requirement `PartOfSpeech-Data-CorrectionsApplied`
with its test `PartOfSpeech_DataCorrections_Rows_AreApplied`, and the README, user guide and packed API reference no longer say that the
embedded model was evaluated on held-out data.

## Corpus cleanup C: owner decision after the dev-only diagnosis

This section is written and committed **before** `thresholds` or `evaluate` is run for the retrained model, so that the git history
shows the order: gates (pre-registered), gate result (G2 failed), diagnosis (development data only), owner decision (this commit),
and only then the held-out commands.

**G2 failed as pre-registered, and it stays recorded as FAILED.** The gate was a point-estimate rule on the single shipped seed of
each model. On that seed the harmful noun-verb errors per 1,000 decisions rose from 20.59 to 23.06 on the relabeled open dev
(+2.47) and from 21.87 to 23.41 on UD-EWT dev (+1.55). Nothing in the gate text, the verdicts or the results sections above is
changed by this section, and no gate is restated, re-interpreted or altered.

**The decision is the owner's.** After the failure a diagnosis on development data only was run (below). The owner of the
repository then accepted the retrained model on the basis of that diagnosis (option 1: accept the full cleanup, held-out
evaluation of the retrained model). This is an **owner decision, not a restated gate**: G2 is not re-defined as passed, it is
not replaced by a different test, and no tolerance was added to it. The decision overrides the stop rule of the
pre-registration for the held-out step only, and the owner's reason is that the rise lies within the seed noise of the training
while the agreement gain is real. The consequence is accepted knowingly: a real rise of about +1 harmful error per 1,000
decisions is **not excluded** by the diagnosis. Nothing about the training, the weights, the lexicon, `training.json` or the
data changes with this decision.

### Diagnosis (development data only)

The full write-up (`DIAGNOSIS.md` with its scripts and outputs) lives in a session folder outside the repository, so it is not
part of the record; **the key tables are copied below**, and that file name is only a pointer. The diagnosis is post hoc: it was
designed after the G2 failure had been seen.

Method.

- **Arms.** OLD is `a94abd0` (the data, lexicon and `training.json` before the cleanup), NEW is this branch at `02dac97`. Each arm's
  trainer was built in its own scratch worktree, so each uses its own lexicon and corpora. The shipped `training.json` settings were
  used unchanged (sources open x2, MASC, silver >= 80, restriction `history`, predicted history, 6 epochs, 19 hash bits, minimum
  count 1, pruning 8, scale 8); only the shuffle seed changes. The temperature is refit on the arm's own open dev for every run,
  exactly as the shipped model's `calibrate` did.
- **Seeds.** 5 per arm: 20260101 (the shipped seed), 11, 12, 13, 14 (11 and 12 are the E0 seeds of this log; 13 and 14 are new).
- **Acceptance test.** The runs at the shipped seed reproduce the figures of the gate table exactly (open dev 92 of 4,468 old and
  103 of 4,467 new; UD-EWT dev 100 of 4,573 and 107 of 4,570; UD agreement 23,227 and 23,338), and the weights of those runs are
  byte-identical to the shipped `weights.bin` of both arms.
- **Scoring.** The runtime decoder (`Tagger.Load`) through the scratch tool of the pre-registration, against each arm's own
  library; definitions as in the pre-registration (decision, harmful at confidence >= 0.45, per 1,000 decisions). The old arm is
  scored on the **new** (relabeled) open dev labels, and both arms on the UD-EWT dev mapped view. Only open dev and UD-EWT dev were
  read; no held-out file and no gold v2 dev.
- **Statistics.** Sample standard deviation over the 5 seeds; Welch t interval for the difference of the means (independent
  shuffles, so unpaired); a combined bootstrap (2,000 replicates, seed 20261104) that resamples documents (shared by both arms) and
  the 5 seeds with replacement within each arm, then takes the difference of the arm means.

Seed table (per-seed values; counts in brackets; the shipped seed is the first column).

| Metric | Arm | 20260101 | 11 | 12 | 13 | 14 | mean ± sd |
| --- | --- | ---: | ---: | ---: | ---: | ---: | ---: |
| UD-EWT dev agreement % | OLD | 92.361 (23,227) | 92.218 (23,191) | 92.302 (23,212) | 92.146 (23,173) | 92.254 (23,200) | 92.256 ± 0.082 |
| | NEW | 92.803 (23,338) | 92.711 (23,315) | 92.655 (23,301) | 92.695 (23,311) | 92.747 (23,324) | 92.722 ± 0.056 |
| Open dev harmful / 1k (relabeled) | OLD | 20.591 (92/4,468) | 20.143 (90) | 21.486 (96) | 20.367 (91) | 22.605 (101) | 21.038 ± 1.013 |
| | NEW | 23.058 (103/4,467) | 21.491 (96) | 21.939 (98) | 22.386 (100) | 22.610 (101) | 22.297 ± 0.605 |
| UD-EWT dev harmful / 1k | OLD | 21.867 (100/4,573) | 22.305 (102) | 20.774 (95) | 24.054 (110) | 21.867 (100) | 22.174 ± 1.194 |
| | NEW | 23.414 (107/4,570) | 23.195 (106) | 23.632 (108) | 22.101 (101) | 22.976 (105) | 23.063 ± 0.591 |
| Open dev accuracy % (relabeled, context) | OLD | 96.054 | 96.016 | 95.952 | 96.020 | 95.936 | 95.996 ± 0.050 |
| | NEW | 96.375 | 96.325 | 96.367 | 96.371 | 96.298 | 96.347 ± 0.034 |

Difference table (NEW minus OLD, mean of 5 seeds; both interval types; where the shipped-seed difference falls among the 25
seed-pair differences).

| Metric | Mean difference | Welch t 95% (df) | Seed + document bootstrap 95% | Contains 0? | Shipped-seed difference | Rank of 25 | Pairs >= shipped / <= 0 |
| --- | ---: | --- | --- | --- | ---: | --- | --- |
| UD agreement (points) | +0.466 | [+0.362, +0.570] (7.1) | [+0.303, +0.624] | no (better) | +0.441 | 15 | 64% / 0% |
| Open harmful / 1k | +1.258 | [-0.008, +2.525] (6.5), p = 0.051 | [-1.021, +3.433] | yes (barely for Welch) | +2.467 | 3 | 12% / 12% |
| UD harmful / 1k | +0.890 | [-0.577, +2.357] (5.9), p = 0.187 | [-1.594, +3.236] | yes | +1.546 | 7 | 32% / 24% |
| Open accuracy (points) | +0.351 | [+0.288, +0.415] (7.0) | [+0.216, +0.486] | no (better) | +0.320 | 17 | 68% / 0% |

Reading. The shipped open-dev pair is a large draw (rank 3 of 25; 12% of the seed pairs are as large, 12% are at or below 0) and
the shipped UD pair a middling one (rank 7 of 25). The standard deviation over seeds of one arm (0.6 to 1.2 per 1,000, 3 to 5
errors) is as large as the shipped difference. The agreement gain on UD-EWT dev (+0.47 points) and the open dev accuracy gain
(+0.35 points) are positive in all 25 seed pairs. The diagnosis does **not** show that the harmful rate is unchanged: both mean
differences are positive (+1.26, +0.89), the Welch interval on open dev only just contains 0, and 5 seeds give low power (the
standard error of the open-dev difference is about 0.55 per 1,000). The honest reading is a change of 0 to about +2.5 harmful
errors per 1,000 decisions (0 to about 11 errors on open dev), best estimate about +1 per 1,000, next to a clear gain in agreement.

Error diff at the shipped seeds (old scored on the new labels). A new error is wrong in the new and right in the old model, a
fixed error the reverse.

| Set | New errors | Fixed | Both wrong | Old total | New total |
| --- | ---: | ---: | ---: | ---: | ---: |
| Open dev | 24 | 13 | 79 | 92 | 103 |
| UD-EWT dev | 28 | 21 | 79 | 100 | 107 |
| Pooled | 52 | 34 | 158 | 192 | 210 |

Pattern findings (all post hoc and uncorrected, hypotheses only).

- The new errors are diffuse. 48 new-error forms: 9 (19%) have a training token whose tag a correction changed, against 8 of 33 fixed
  forms (24%) and 546 of 5,169 (11%) of all dev gold noun and verb forms: about 80% of the new errors are on forms no correction
  touched. None of the 24 new open-dev errors is on a relabeled dev token (79 of 8,353 gold noun and verb tokens, 0.9%, were
  relabeled), so the relabeling of the dev file cannot explain the G2a rise (the UD labels did not change at all).
- Churn is symmetric across seeds: tokens with a harmful error in at least 3 more of the five new-arm models than of the five old-arm
  models number 11 on open dev, the reverse 12; on UD 16 and 12. Mean harmful errors per model over five seeds: open 94.0 old and
  99.6 new, UD 101.4 old and 105.4 new. The same form can churn in both directions (`look` is a new error twice and a fixed one
  once on UD).
- Direction: the new errors are 30 gold VERB answered NOUN and 22 gold NOUN answered VERB, the fixed ones 12 and 22 (Fisher p =
  0.050). The new model tends to lose base-form verbs against nouns (`look`, `reply`, `type`, `focus`, `worry`, `move`, `mind`).
  Of nine pattern tests, the only one with p < 0.05 is "after a determiner, adjective, number or possessive" (15 new against 3
  fixed, p = 0.031); it does not survive a correction for nine tests.
- The only rule with an association is `adj:prenominal-ing` (the adjudicated ADJ>VERB and ADJ>NOUN moves of -ing words, 645 of the
  5,640 changed training tags): present in 7 of 48 new-error forms against 113 of 5,169 dev noun and verb forms (enrichment 6.67,
  Fisher p = 0.0001), but 3 of 33 fixed-error forms (new against fixed p = 0.35). The enrichment is confounded with morphology (the
  rule touches only -ing words): conditional on -ing forms, 7 of 15 touched forms are new errors (p = 0.058) against 3 of 11 fixed
  (25% base rate). So the association is **weak and confounded**; touched -ing forms are over-represented among errors of both kinds.
  Every other rule has at most one error form (`adj:no-detector` 3 new, 3 fixed; `adj:propn-boundary` 1 and 1).

Prenominal-ing ablation (**exploratory, post hoc, not adopted**: a test of the one hypothesis the error diff suggested, with
nothing chosen by it). The 861 correction rows of `adj:prenominal-ing` that touch development or training data (MASC 518, silver
170, open train 151, open dev 22; the 17 open-test rows were excluded, so no held-out file was touched or needed) were reversed in
a scratch worktree, the lexicon rebuilt (339,819 entries) and the same five seeds trained and scored on the same new dev labels.
The ablated model is not committed and was not used for anything.

| Metric | ABL per seed (20260101, 11, 12, 13, 14) | ABL mean ± sd | OLD mean | NEW mean | ABL − NEW (Welch 95%; bootstrap 95%) | ABL − OLD (Welch 95%; bootstrap 95%) |
| --- | --- | ---: | ---: | ---: | --- | --- |
| UD agreement % | 92.711, 92.779, 92.683, 92.616, 92.747 | 92.707 ± 0.063 | 92.256 | 92.722 | −0.015 ([−0.102, +0.071]; [−0.156, +0.112]) | +0.451 ([+0.344, +0.558]; [+0.283, +0.628]) |
| Open harmful / 1k | 20.148 (90), 21.939 (98), 21.491 (96), 22.386 (100), 20.148 (90) | 21.222 ± 1.031 | 21.038 | 22.297 | −1.075 ([−2.360, +0.211]; [−3.315, +1.256]) | +0.184 ([−1.307, +1.675]; [−2.270, +2.528]) |
| UD harmful / 1k | 22.538 (103), 23.851 (109), 20.788 (95), 21.663 (99), 22.101 (101) | 22.188 ± 1.133 | 22.174 | 23.063 | −0.875 ([−2.272, +0.521]; [−3.145, +1.552]) | +0.015 ([−1.683, +1.713]; [−2.614, +2.749]) |
| Open accuracy % | 96.348, 96.329, 96.302, 96.298, 96.352 | 96.326 ± 0.025 | 95.996 | 96.347 | −0.021 ([−0.065, +0.023]; [−0.105, +0.065]) | +0.330 ([+0.269, +0.391]; [+0.186, +0.464]) |

Without the `prenominal-ing` corrections the harmful rate returns to the old level (open 21.22 against 21.04, UD 22.19 against
22.17) and the accuracy gain stays, but the difference to the new model is within noise (both intervals contain 0), so this is
consistent with a small `prenominal-ing` effect and does not establish one. The ablation is **not adopted**.

Caveats (as in the diagnosis).

- Post hoc: designed after the G2 failure had been seen; the pattern and rule tests are uncorrected and generate hypotheses only.
- Five seeds per arm is little: the Welch degrees of freedom are about 6 to 8, the open-dev interval is borderline and a real rise of
  about +1 per 1,000 (4 to 6 errors) is **not excluded**. The shuffle seed is one source of variance; the lexicon and data also
  differ between the arms, and the documents are covered by the bootstrap, which resamples the five observed seeds (a coarse
  distribution), so the Welch interval is the primary statement.
- The decision sets of the two arms differ slightly (the lexicon candidates), so rates rather than counts are compared, as in the
  gates. The corpora have no lemma column; form statistics use the lower-cased form. The error diff uses one seed pair, so its
  token-level patterns are descriptive.

### Decision and order of the following steps

The owner accepted the retrained model on the basis of the diagnosis above: the harmful-rate rise at the shipped seed is within the
seed noise, the agreement gain is real (+0.47 points on UD-EWT dev, Welch [+0.36, +0.57]), and the gain in open dev accuracy is
real too. **This is an owner decision.** G2 remains FAILED as pre-registered; no gate text, verdict or threshold was altered; the
diagnosis did not replace a gate. The held-out step is therefore run for the retrained model as a new record and not as an erratum
of the earlier one: first `thresholds` (development data only, with the two frozen harmful counts of `Program.cs` set to the
retrained model's development figures), then `evaluate` exactly once, with no tuning of anything. G4 (the build) can become green
once the held-out record is reissued; the G4 row above keeps its original FAIL text and is annotated when that has happened.

**G4 after the reissue (added 2026-11-06, after the held-out record was committed).** With the new held-out record committed
(commits `939c0fa` and `58da737`) and the documents updated, `pwsh ./build.ps1` at commit `52bb1e5` is **green**: Release build
of every target, `LexiconCompiler --check` (262,635 spellings), `train --check` (image `dca810e4...339d`, current), the API-docs
check, and all tests on net481, net8.0, net9.0 and net10.0 (3,225 tests, 0 failed, 1 skipped on net481 as before: link
resolution needs .NET 6 or later). `pwsh ./lint.ps1` exits 0 and `dotnet reviewmark --lint` reports nothing. G4 is therefore
**PASS** for the retrained model with its reissued record. This does not change the verdict of G2, which stays FAILED as
pre-registered.

## Corpus cleanup C: held-out evaluation of the retrained model (a new record for a new model, not an erratum)

This is a **new** evaluation record for the retrained model. It does not correct or replace the text of the earlier record
("Outcome of the held-out run" and its errata above), which describes the earlier model and stays as written. It was produced
after the owner decision of the previous section, in this order (git history): owner decision (`6388220`), `thresholds`
(`939c0fa`), the one `evaluate` (`58da737`), this section and the documents after it.

Procedure, exactly the one of the protocol; nothing was tuned.

1. The two constants of the `thresholds` command, `FrozenOpenHarmful` and `FrozenGoldHarmful` in `Program.cs`, held the first
   freeze figures (94 and 9); they were set to the development figures of the retrained model, 103 (open dev, 4,467
   decisions) and 14 (gold v2 dev, 611 decisions), 117 pooled, which the trainer's `eval` printed before (the figures of the
   gate table above). They only guard that the 0.45 row of the curve reproduces the freeze figures; they do not select
   anything. `thresholds` (seed 1, 1,000 resamples, development data only, rule `nv-pooled-dev-v1`, grid, targets and floors
   unchanged) then reproduced 103, 14 and 117 and wrote `operating-points.json` (new model hash, new dev file hashes) and
   `confidence-thresholds.md`. These were committed before the held-out run.
2. `evaluate --preflight` passed (every documented size matched: open test 1,501 sentences, 26,185 tokens, 517 documents; gold
   v2 test 3,047 targets, 856 documents; probe 560; challenge set 32 sentences, 272 tokens; UD test 2,077 sentences, 25,094
   words); it scored nothing.
3. `evaluate` refuses to run while `docs/evaluation/test-results.json` exists. The old `test-results.md` and `.json` (the record
   of the earlier model) were **deleted on purpose** for this run, because the record of a retrained model is a new record, not
   a re-run of the old one; they stay recoverable in git history (for example `git show 6388220:docs/evaluation/test-results.md`)
   and were copied outside the repository before the deletion.
4. `evaluate --dump-dir <folder outside the repository>` ran **once** (BelowNormal, 8 logical CPUs, the treebank from the
   git-ignored cache). It exited with 1 because of the same three stop conditions as the earlier record (findings, not
   failures of the run); the committed files are the output as produced. **No second `evaluate` run was made.**

Stop conditions of the protocol (all three fired again, with these values; earlier values in brackets):

- `shape-probe`: 52 harmful errors at 0.45 against a limit of 28 (5% of 560 rows) (earlier 48). This is the finding about the model's
  behavior and it stands, as before, reported and not tuned.
- `gold-v2-test-to-not`: gold outside the candidates for 50.00% of the 8 targets (earlier the same); the `to`/`not` convention
  difference, a view reported separately.
- `ud-test-raw`: gold outside the candidates for 1.93% (484 of 25,094 words) (earlier 1.68%, 421); the raw treebank tags that the
  library does not produce by design. The primary mapped view is at 0.55% (138 words) (earlier 0.56%, 140), under the limit.

All other stop conditions held: the open test accuracy 96.46% against 96.37% on development; the harmful rates per 1,000
answered on open test at the strict (5.7) and balanced (6.5) points are below twice the pooled development rates (4.8 and
7.7); open test ECE 0.24%; open test gold outside the candidates 41 of 26,185 tokens (0.16%).

**What is comparable (stated plainly).** The labels of the **open test** split changed with the corrections (476 correction
rows in it), so its before-and-after is **not strictly comparable**: the new model is scored against corrected labels and the
earlier model against the old ones. The labels of **UD-EWT, gold v2, the shape probe and the STE challenge set did not change**
(the correction tooling does not cover gold v2 or the probe; the STE set is linted but never corrected), so these four are
the **fair comparisons**: the same sentences and labels, an earlier and a retrained model. The old figures below are those of the
earlier record (model image `68673a9f...0f32a`); the new figures are those of the new record (model image `dca810e4...339d`,
temperature 6.583). Intervals are the 95% percentile bootstrap over documents of each record (1,000 resamples, seed 1); no
paired interval between the two models was computed, so overlapping intervals are not a test.

Accuracy and harmful errors, earlier model (old) and retrained model (new), at the harmful limit 0.45 (same columns as the
report; "n/c" marks the set whose labels changed):

| Set | Model | Accuracy % | Ambiguous accuracy % | Decisions | Harmful | Harmful per 1,000 decisions | ECE % | NLL | Gold outside candidates |
| --- | --- | --- | --- | ---: | ---: | --- | --- | --- | ---: |
| open-test (n/c: labels changed) | old | 96.10 [95.69, 96.49] | 95.16 [94.72, 95.61] | 4,413 | 91 | 20.6 [15.2, 26.5] | 0.28 [0.19, 0.61] | 0.1394 [0.1211, 0.1602] | 71 |
| | new | 96.46 [96.07, 96.81] | 95.56 [95.19, 95.92] | 4,408 | 91 | 20.6 [15.7, 26.1] | 0.24 [0.19, 0.52] | 0.1163 [0.1013, 0.1322] | 41 |
| gold-v2-test | old | 94.57 [93.67, 95.39] | 91.70 [90.33, 92.93] | 936 | 23 | 24.6 [14.9, 34.8] | 1.82 [1.47, 2.80] | 0.2178 [0.1812, 0.2596] | 7 |
| | new | 94.34 [93.46, 95.16] | 91.34 [90.02, 92.55] | 936 | 29 | 31.0 [19.9, 42.4] | 2.23 [1.80, 3.18] | 0.2491 [0.2065, 0.2971] | 12 |
| gold-v2-test-full-agreement | old | 95.39 [94.54, 96.24] | 92.49 [91.12, 93.81] | 713 | 17 | 23.8 [13.4, 35.9] | 1.43 [1.11, 2.34] | 0.1838 [0.1448, 0.2241] | 4 |
| | new | 95.16 [94.26, 96.00] | 92.11 [90.65, 93.51] | 713 | 21 | 29.5 [17.9, 40.7] | 1.83 [1.32, 2.70] | 0.1960 [0.1560, 0.2379] | 5 |
| gold-v2-test-to-not (8 targets) | old | 50.00 [12.50, 87.50] | 100.00 | 0 | 0 | 0.0 | 56.66 [27.17, 88.44] | 6.9865 [1.9304, 12.0984] | 4 |
| | new | 50.00 [12.50, 87.50] | 100.00 | 0 | 0 | 0.0 | 55.56 [25.67, 88.55] | 6.9707 [1.8828, 12.0995] | 4 |
| shape-probe (560 rows) | old | 89.11 [86.61, 91.61] | 89.21 [86.57, 91.56] | 556 | 48 | 86.3 [64.5, 109.9] | 2.38 [1.99, 5.31] | 0.2766 [0.2228, 0.3351] | 0 |
| | new | 87.50 [84.82, 90.18] | 87.41 [84.78, 90.13] | 556 | 52 | 93.5 [69.8, 118.7] | 1.99 [2.01, 5.24] | 0.2829 [0.2233, 0.3433] | 0 |
| challenge-set (STE, 272 tokens) | old | 87.13 [81.18, 92.70] | 88.78 [82.65, 93.72] | 86 | 0 | 0.0 | 7.80 [3.84, 12.98] | 0.3317 [0.1953, 0.4885] | 0 |
| | new | 87.50 [82.56, 92.37] | 87.37 [80.98, 92.51] | 86 | 0 | 0.0 | 8.51 [5.28, 13.67] | 0.3842 [0.2251, 0.5604] | 0 |
| ud-test-mapped (UD-EWT, primary view) | old | 92.30 [91.75, 92.82] | 91.71 [91.05, 92.26] | 4,413 | 73 | 16.5 [12.6, 20.9] | 2.36 [2.06, 2.81] | 0.3175 [0.2909, 0.3505] | 140 |
| | new | 92.97 [92.46, 93.44] | 92.52 [91.90, 93.05] | 4,410 | 83 | 18.8 [14.5, 23.5] | 2.06 [1.78, 2.50] | 0.2982 [0.2733, 0.3319] | 138 |
| ud-test-raw (UD-EWT, raw tags) | old | 88.40 [87.84, 88.91] | 87.13 [86.45, 87.73] | 4,010 | 72 | 18.0 [13.7, 22.6] | 6.22 [5.86, 6.73] | 0.6062 [0.5731, 0.6457] | 421 |
| | new | 89.05 [88.53, 89.53] | 88.29 [87.60, 88.86] | 4,007 | 82 | 20.5 [15.6, 25.8] | 5.96 [5.59, 6.48] | 0.6106 [0.5764, 0.6509] | 484 |

(The shape-probe ECE interval of the new record, [2.01, 5.24], does not contain its point estimate 1.99; that is how the
report's percentile interval of this small set behaves and it is reproduced as written.)

Operating points. The rule `nv-pooled-dev-v1` was applied unchanged to the dev curve of the retrained model. The first grid
threshold that meets the strict target (at most 5.0 harmful per 1,000 answered, coverage at least 60%) is now 0.90 (it was 0.95),
and the first that meets the balanced target (at most 10.0, coverage at least 85%) is now 0.85 (it was 0.80: at 0.80 the
retrained model has 47 harmful of 4,677 answered, 10.05 per 1,000, which the report rounds to 10.0 but which is above the
target, and the rule is applied to the unrounded figure). Development figures (pooled open dev and
gold v2 dev, 95% bootstrap intervals over documents):

| Point | Model | Threshold | Coverage % [95%] | Harmful per 1,000 answered [95%] | Harmful of answered |
| --- | --- | ---: | --- | --- | --- |
| strict | old | 0.95 | 80.6 [79.1, 82.0] | 3.9 [1.8, 6.1] | 16 of 4,105 |
| | new | 0.90 | 86.5 [85.2, 87.7] | 4.8 [2.7, 7.0] | 21 of 4,392 |
| balanced | old | 0.80 | 91.0 [90.0, 92.0] | 9.9 [6.6, 13.4] | 46 of 4,636 |
| | new | 0.85 | 89.8 [88.7, 90.9] | 7.7 [4.8, 11.0] | 35 of 4,562 |

On the held-out sets (the thresholds are those selected on development data; the earlier thresholds were 0.95 and 0.80, so
the rows compare the points of each record, not one threshold):

| Set | Point | Model (threshold) | Coverage % | Harmful per 1,000 answered [95%] | Harmful of answered |
| --- | --- | --- | ---: | --- | --- |
| open-test (n/c) | reference 0.45 | old | 99.9 | 20.6 [15.2, 26.5] | 91 of 4,407 |
| | | new | 99.8 | 20.7 [15.8, 26.1] | 91 of 4,399 |
| | strict | old (0.95) | 82.3 | 4.1 [2.0, 6.9] | 15 of 3,631 |
| | | new (0.90) | 87.7 | 5.7 [3.0, 8.8] | 22 of 3,868 |
| | balanced | old (0.80) | 92.3 | 9.3 [6.1, 12.6] | 38 of 4,072 |
| | | new (0.85) | 90.9 | 6.5 [3.5, 9.7] | 26 of 4,005 |
| gold-v2-test | reference 0.45 | old | 99.8 | 24.6 [14.9, 35.0] | 23 of 934 |
| | | new | 99.9 | 31.0 [19.9, 42.4] | 29 of 935 |
| | strict | old (0.95) | 81.3 | 3.9 [0.0, 9.1] | 3 of 761 |
| | | new (0.90) | 86.3 | 5.0 [1.2, 10.3] | 4 of 808 |
| | balanced | old (0.80) | 92.5 | 10.4 [3.9, 17.8] | 9 of 866 |
| | | new (0.85) | 89.1 | 9.6 [3.7, 17.3] | 8 of 834 |
| shape-probe | reference 0.45 | old | 98.6 | 87.6 [65.6, 111.3] | 48 of 548 |
| | | new | 98.0 | 95.4 [71.3, 120.9] | 52 of 545 |
| | strict | old (0.95) | 59.5 | 9.1 [0.0, 19.7] | 3 of 331 |
| | | new (0.90) | 67.8 | 18.6 [5.4, 33.5] | 7 of 377 |
| | balanced | old (0.80) | 80.6 | 42.4 [25.2, 61.7] | 19 of 448 |
| | | new (0.85) | 74.1 | 26.7 [12.1, 42.3] | 11 of 412 |
| challenge-set | strict and balanced | old and new | 69.8 to 84.9 (old), 76.7 to 77.9 (new) | 0.0 | 0 of 60 to 73 (old), 0 of 66 and 67 (new) |
| ud-test-mapped | reference 0.45 | old | 99.3 | 16.7 [12.7, 21.0] | 73 of 4,382 |
| | | new | 99.4 | 18.9 [14.8, 23.6] | 83 of 4,384 |
| | strict | old (0.95) | 76.9 | 2.9 [1.3, 5.0] | 10 of 3,395 |
| | | new (0.90) | 84.1 | 5.1 [2.8, 7.8] | 19 of 3,708 |
| | balanced | old (0.80) | 89.2 | 7.4 [4.8, 10.2] | 29 of 3,936 |
| | | new (0.85) | 87.4 | 5.7 [3.5, 8.6] | 22 of 3,855 |
| ud-test-raw | strict | old (0.95) | 77.9 | 3.2 [1.5, 5.4] | 10 of 3,125 |
| | | new (0.90) | 84.7 | 5.6 [3.1, 8.5] | 19 of 3,395 |
| | balanced | old (0.80) | 89.6 | 8.1 [5.2, 11.3] | 29 of 3,593 |
| | | new (0.85) | 87.8 | 6.2 [3.8, 9.4] | 22 of 3,520 |

Shape probe by shape (harmful errors at 0.45, old then new): `n-obj-light` 1 and 1, `n-compound-head` 16 and 17,
`n-modifier` 0 and 0, `n-subject-initial` 12 and 12, `n-measure` 6 and 6, `v-imperative-initial` 12 and 14, `v-after-modal`
1 and 2. Open test per tag: NOUN precision/recall 96.19/96.21 (old) and 96.46/96.82 (new), VERB 94.31/95.72 and 94.87/96.30
(the gold tag counts differ because the labels changed). The throughput of the new report (889,218
tokens per second, one thread) is a timing of the machine; the earlier one (315,213) was measured on a busy machine and the two
are not a comparison of the models.

**Reading, with what is worse stated first.**

- **Worse than before on the fair comparison sets.** The harmful noun-verb errors per 1,000 decisions at 0.45 are higher for
  the retrained model on **gold v2 test** (24.6 to 31.0; 23 to 29 errors; the full-agreement subset 23.8 to 29.5), on the
  **shape probe** (86.3 to 93.5; 48 to 52) and on **UD-EWT** (mapped 16.5 to 18.8, 73 to 83; raw 18.0 to 20.5). All three
  moved in the direction that the pre-registered G2 had flagged on the development sets, and the diagnosis had said that a real
  rise of about +1 per 1,000 was not excluded; the size here (about +2 to +7 per 1,000) is larger than that estimate on each
  set. Every old and new interval overlaps (the sets are small: 29 and 23 errors on gold v2), so no single set proves a
  regression, but the direction is the same on all three. Gold v2 accuracy (94.57 to 94.34), its ambiguous accuracy, ECE
  (1.82 to 2.23) and NLL (0.2178 to 0.2491) are also slightly worse, and so are the shape probe accuracy (89.11 to 87.50) and its
  NLL, and the NLL of the challenge set (0.3317 to 0.3842; its accuracy is 87.13 to 87.50, harmful 0 and 0).
- **Better than before.** UD-EWT agreement improved on both views (mapped 92.30 to 92.97, raw 88.40 to 89.05; ambiguous
  91.71 to 92.52), with a lower ECE (2.36 to 2.06) and NLL (0.3175 to 0.2982) on the mapped view; the open test accuracy
  rose 96.10 to 96.46 with a lower NLL and ECE (not strictly comparable: the labels changed); the number of golds outside the
  candidates on the open test split fell from 71 to 41 (also not comparable).
- **Open test harmful rate unchanged** at 91 errors, 20.6 per 1,000 decisions (not strictly comparable).
- **Operating points.** The strict threshold moved from 0.95 to 0.90 and the balanced from 0.80 to 0.85. On the held-out
  open test split the new strict point answers more (87.7% against 82.3%) at a **higher** harmful rate (5.7 against 4.1 per
  1,000 answered; the target of 5.0 was met on development data, 4.8, and is exceeded on the open test split, where the
  interval [3.0, 8.8] contains 5.0); the balanced point has a lower rate (6.5 against 9.3) at a similar coverage (90.9% against
  92.3%). On gold v2 test strict is 5.0 (earlier 3.9) and balanced 9.6 (earlier 10.4); on UD mapped strict 5.1 (earlier 2.9) and
  balanced 5.7 (earlier 7.4); on the shape probe strict 18.6 (earlier 9.1) and balanced 26.7 (earlier 42.4). The strict point is
  worse than before on every held-out set where it is not zero, and the balanced point is better or equal on open test, UD and
  the shape probe and slightly better on gold v2; these are different thresholds, and the points of the two records answer
  different shares of the decisions.
- **What is not shown.** The held-out evaluation did not tune anything and no test figure was used to change a threshold, the
  model or a setting. The shape probe weakness of the earlier record (stop condition fired) persists and is slightly larger.

## Corpus cleanup C: pre-registered `prenominal-ing` follow-up

This section is written and committed **before any training of the follow-up arms**, together with the frozen item list of the
compound-head measure (`tools/DemaConsulting.PartOfSpeech.Trainer/prenominal-ing/`), so the git history shows the order:
pre-registration (this commit), then training and scoring, then the results section. It changes no gate, no verdict, no weight, no
lexicon, no `training.json` and no data of the branch. The owner decision behind it: run the pre-registered `-ing` follow-up on the
same branch (the diagnosis above found one weak, post hoc association with the `prenominal-ing` corrections).

### Disclosure (read first)

- **The held-out sets have been seen once for this model.** The held-out evaluation of commit `58da737` (open test, gold v2 test,
  shape probe, STE challenge set, UD-EWT test) was run once for the model of commit `dca810e4...339d`. Everything here is
  **development data only** (open dev, UD-EWT dev); no held-out file is evaluated, scored or inspected by this follow-up. If an arm
  other than FULL were ever adopted, a second held-out evaluation of that model would be a **disclosed second look** at sets already
  seen for the sibling model, and would carry that label.
- **The primary figures of FULL and NO-ING are already partly known.** NO-ING is built exactly like the ablation arm of the
  diagnosis (same rows, same builder logic, same seeds), and FULL is the diagnosis arm NEW (the data, lexicon and weights have not
  changed since `02dac97`; later commits changed only prose). Both were scored on the dev sets in the diagnosis, whose tables (above)
  gave a mean difference of about -1.1 harmful per 1,000 on open dev and about -0.9 on UD-EWT dev (ablation minus new), i.e. about
  -1.0 pooled, so the primary comparison is **foreseeable** and is expected to reproduce. What is new here: (1) a rule fixed in
  advance, (2) the compound-head measure and its constraint, built from gold labels before any run of this follow-up and **not
  computed on any earlier prediction file**, (3) a negative-control arm, (4) the reproduction itself. The hypothesis came from a
  post hoc look at dev data, so even a clear dev result is evidence of the dev-selection kind and nothing more.
- Open dev and UD-EWT dev have been used many times in this log (calibration, selection, the diagnosis). They are development
  data; the selection here is made on them by the rule below and on nothing else.

### Arms

Every arm: 5 seeds, the **same seeds as the diagnosis** (20260101, 11, 12, 13, 14); the shipped `training.json` settings unchanged
(sources open x2, MASC, silver >= 80, restriction `history`, predicted history, 6 epochs, 19 hash bits, minimum count 1, pruning 8,
scale 8; only the shuffle seed changes); `train --seed S --calibrate --output <scratch file>`, i.e. the softmax temperature is refit
for each run on the arm's own open dev (minimum NLL, as the shipped model's `calibrate` did, and as in the diagnosis; it never
changes a chosen tag). Each arm has its own scratch worktree, its own corpora, its own rebuilt lexicon and its own trainer build.
OLD is **not** an arm here. 3 arms x 5 seeds = 15 runs; no other run, seed or setting is trained for this follow-up.

| Arm | Definition |
| --- | --- |
| **FULL** | The current branch data, lexicon and `training.json` (nothing removed). The diagnosis arm NEW. |
| **NO-ING** | FULL minus every effective row of `data/corrections/adjudicated.tsv` whose detector is `prenominal-ing` (below). The tokens of those rows keep their pre-cleanup labels (tag and status cell) in whichever split the row belongs to. `mechanical.tsv` is unchanged (no mechanical row is touched; no removed row supersedes one: checked, 0). |
| **NO-CTRL** (negative control) | FULL minus a comparable-sized set of `adjudicated.tsv` rows of **one unrelated detector**, `propn-boundary` (below), built the same way. |

**Detector of a row.** A row of `adjudicated.tsv` carries detector X when a row of `data/corrections/candidates.tsv` with
`candidate_rule` X covers its token (the candidate's `token_index` up to `token_index + span_length - 1`). A row covered by several
candidate rows carries every one of their rules and belongs to each of them (168 of the 7,063 rows have more than one detector; 1,958
have none: dependents and tagger-flagged tokens). This is the definition of the diagnosis builder (`mkabl.py`).

**Rows removed (re-counted exactly on this branch, `mkarm.py --count-only`).** The 17 (`prenominal-ing`) and 12 (`propn-boundary`) rows
of **open test** are **left out** of the removal: the held-out file is neither read for scoring nor reversed; open test is not a
training, calibration or scoring file, so the arms differ from FULL in open train, open dev, MASC and silver only.

| Detector | Open train | Open dev | Open test (left out) | MASC | Silver | Total with open test | **Removed (without open test)** |
| --- | ---: | ---: | ---: | ---: | ---: | ---: | ---: |
| `prenominal-ing` (NO-ING) | 151 | 22 | 17 | 518 | 170 | 878 | **861** |
| `propn-boundary` (NO-CTRL) | 152 | 19 | 12 | 563 | 0 | 746 | **734** |

(The diagnosis counted the same 861 `prenominal-ing` rows: 518 + 170 + 151 + 22. 8 of them carry a second detector,
`mention-quoted` 6 and `mention-the-word` 2; the two removal sets share 0 rows.)

**Choice of the control detector (made now, by row count and by morphology only, before any training).** The window of the brief is
861 +- 25% = 646 to 1,076 rows. The adjudicated-row counts per detector (rows removed, i.e. without open test; all detectors counted
with the definition above): `foreign-token` 1,076 (+25.0%: at the edge of the window), `prenominal-ing` 861, `propn-boundary` **734
(-14.7%)**, `mention-quoted` 626 (-27.3%, outside), `prenominal-ed` 429, `be-participle-adj-not-gradable` 328, `mention-the-word` 214,
`language-name` 199, and fewer below. The two detectors inside the window are `foreign-token` and `propn-boundary`; **`propn-boundary`
is the closest to 861** (|734 - 861| = 127 against |1,076 - 861| = 215) and it is not related to -ing morphology (it moves a
capitalization/name boundary, mostly NOUN against PROPN; `prenominal-ed` is excluded as the morphological sibling in any case). It is
chosen on these two facts only. Limit stated in advance: the control removes corrections of a different kind (NOUN/PROPN) than the
tested one (ADJ/VERB/NOUN of -ing words), so it shows what removing an arbitrary comparable set does to the noun-verb figures, not
what removing a verb/adjective set would do.

**How an arm is built (scratch worktrees only; the main working tree, `weights.bin`, `lexicon.*` and `data/` are never written).**

1. `git worktree add <scratch>\wt-<arm> <this commit>` (detached) for each arm; for NO-ING and NO-CTRL the git-ignored
   `tools/data/cache/wordsense` of the main tree is copied into the worktree (the lexicon builder needs the pinned WordSense forms).
2. NO-ING and NO-CTRL: `python tools/DemaConsulting.PartOfSpeech.Trainer/prenominal-ing/mkarm.py --worktree <wt> --detector
   prenominal-ing|propn-boundary` rewrites, in the worktree only, `adjudicated.tsv` to the rows of the set (open test left out) and
   `mechanical.tsv` to its header; then `python tools/data/corrections.py apply --write --reverse` in the worktree restores exactly
   those tokens (the tool reads the open test file only as part of its checks of the addresses; it is not modified and no row of it
   is in the set).
3. **The lexicon is rebuilt from the arm's own corpora**: `python tools/data/build_lexicon.py` (open train and MASC attestation; it
   writes the arm's `data/lexicon/lexicon.tsv`), then `LexiconCompiler` (the arm's `lexicon.bin`), then `dotnet build -c Release` of
   the arm's trainer (the project reference embeds the arm's lexicon and library). For FULL nothing is rebuilt.
4. Training: `train --seed S --calibrate --output <scratch>\<arm>-s<S>.bin` with the arm's own trainer. A scoring tool (the
   `Dump` tool of the diagnosis: `Tagger.Load`, `MaxAlternatives` limit) is built against **each arm's own library**, so every arm
   is decoded with its own weights and its own lexicon.

**Scoring sets (all arms alike).** Every arm is scored on **FULL's relabeled open dev labels** (the committed `data/open/dev.tsv`,
like for like, as in the diagnosis; the arm's own open dev, which differs in 22 labels for NO-ING and 19 for NO-CTRL, is used only for
the temperature refit) and on UD-EWT dev (the mapped view, `tools/data/cache/ud-ewt/ud_dev.tsv`, 25,148 tokens). Nothing else is
read for scoring.

**Definitions (those of E0 and of the diagnosis).** A decision is a gold NOUN or VERB token with both tags among the decoder's
candidates; a harmful error is a chosen tag that is the other of NOUN and VERB with confidence >= 0.45; rates are per 1,000
decisions; UD-EWT agreement is the share of tokens whose chosen tag equals the mapped gold tag. **Pooled harmful rate of a run** =
(harmful errors on open dev + harmful errors on UD-EWT dev) / (decisions on open dev + decisions on UD-EWT dev) x 1,000, computed per
run (the decision counts differ slightly per arm because the lexicon candidates differ).

### The dev compound-head measure (frozen)

Built before any training of this follow-up, by the committed script `prenominal-ing/build_items.py`, from the committed
`data/open/dev.tsv` (sha256 `31c9048de0824867fab6d7a9f56bedbe36f8e4282bbcc4ffcedde84f1b4cbf91`, the relabeled dev) and FULL's
committed `data/lexicon/lexicon.tsv` (sha256 `bcd7ea4538c77b6fea7bcb0b0df710cd85d49726811cd433b330ba95e910f614`) **only**: gold labels,
the form and the lexicon, no prediction of any model, and no test or held-out data. The item list is the same for all arms. It is
committed as `prenominal-ing/items-open-dev.tsv` (sha256 `c2ca9d9b5f2a2cc65e0fa3ed3a5f2c1b247797f0f3230b749db2e78ca2e57ea8`); `build_items.py --check`
rebuilds it byte for byte.

The rule (exactly): let `form` be the NFC form lower-cased, `plain` mean that it is purely alphabetic (`str.isalpha`) with at least 4
letters, `tags` its lexicon tags (a form not in the lexicon is not an item) and `next` the gold tag of the following token of the same
sentence.

- **Compound item**: `plain`, gold NOUN, `next` in {NOUN, PROPN}, and `tags` contain NOUN and VERB. Class `compound-ing` if the form
  ends in `ing`, else `compound-zero` (zero-derived noun/verb forms such as `water`, `storm`, `report`). This is the "-ing or
  zero-derived modifier directly before a noun head that is labeled NOUN" of the brief (a determiner or a number cannot qualify: the
  token must be gold NOUN and lexicon NOUN+VERB).
- **Control item (class `verbal-ing`)**: `plain`, gold VERB, the form ends in `ing`, `tags` contain ADJ, NOUN and VERB (the signature
  that the lexicon builder gives to the gerund/participle of a verb: base verbs such as `bring` or `sing` are not items). No
  condition on the next token.

Counts of the frozen list: **248 compound items (41 `compound-ing`, 207 `compound-zero`)** and **384 `verbal-ing` items**, 632 in
all.

Metrics, per run, with the chosen tag of the runtime decoder (`Tagger.Tag`, the tag of the token; no confidence condition and no
candidate condition): **compound-head NOUN accuracy** = share of the 248 compound items whose chosen tag is NOUN (the two classes
are also reported separately, descriptively); **verbal-ing VERB accuracy** = share of the 384 control items whose chosen tag is VERB.

### Selection rule (fixed now; applied exactly as written; nothing is tuned afterwards)

Candidates are **FULL and NO-ING**. NO-CTRL is **not** a candidate winner: it is reported for context only (it is evidence about
whether removing *any* comparable set of rows lowers the harmful rates). The statistics below are computed from the per-run values of
the 5 seeds of each arm.

- **Primary.** The mean over the 5 seeds of the **pooled harmful noun-verb rate** (harmful errors summed over open dev and UD-EWT
  dev divided by decisions summed over both sets, x 1,000, computed per run). Lower wins.
- **Constraints** (on the winner). (C1) The winner's mean UD-EWT dev agreement is within **0.15 points** of the best candidate
  arm's mean agreement (the higher of FULL and NO-ING). (C2) The winner's mean compound-head NOUN accuracy is within **2 points** of
  the best candidate arm's mean (the higher of FULL and NO-ING).
- **Tie-break.** If the primary difference between FULL and the lowest candidate arm is **under 0.5 per 1,000**, the winner is
  **FULL**, the convention-consistent default.
- **Reading.** The lowest-primary candidate is the provisional winner. If it is FULL, FULL is selected. If it is NO-ING, NO-ING is
  selected only when the primary difference is at least 0.5 per 1,000 **and** C1 and C2 both hold; otherwise FULL is selected. FULL
  is the default in every case not listed for NO-ING. (Literal reading, stated for the case the rule did not foresee: if FULL has the
  lowest primary but would itself fail C1 or C2 against NO-ING, FULL is still selected, because the rule has no route that selects
  the arm with the higher primary; the constraint check is reported either way.)
- **Shipped seed.** The shipped seed stays **20260101** whichever arm wins; nothing here selects a seed.
- **Reported but not part of the rule** (descriptive): per arm mean +- sample standard deviation (n = 5) and the per-seed values of
  every metric; differences against FULL with a Welch t 95% interval and a combined seed + document bootstrap (2,000 replicates, seed
  20261104, resampling documents shared by the arms and the 5 seeds within each arm); the item-class breakdown of the compound
  measure; the harmful rates per set. Reading aid for the control (does not change the selection): if NO-CTRL's mean pooled rate
  lies below FULL's by at least half of NO-ING's reduction, the report says that the NO-ING reduction is not specific to -ing rows;
  otherwise that a comparable removal does not match it.

### Procedure, acceptance test and stop rule

1. The three arms are trained and scored through the real runtime decoder (`Tagger.Load`), 15 runs, one heavy process at a time.
2. **Acceptance test (before the rule is applied).** FULL at the shipped seed 20260101 must reproduce the committed model's dev
   numbers (open dev 103 harmful of 4,467 decisions, UD-EWT dev 107 of 4,570, UD-EWT agreement 23,338 of 25,148 = 92.803%), its
   weights must be **byte-identical** to the committed `weights.bin`, and the five FULL seeds must reproduce the diagnosis arm NEW
   (UD agreement 92.803, 92.711, 92.655, 92.695, 92.747). NO-ING is expected to reproduce the diagnosis ablation arm (UD
   agreement 92.711, 92.779, 92.683, 92.616, 92.747; open harmful 90, 98, 96, 100, 90; UD harmful 103, 109, 95, 99, 101). If the
   FULL acceptance fails, the follow-up stops and is reported as failed without a selection; a NO-ING or NO-CTRL arm that differs
   from the diagnosis is reported as a deviation (the build is deterministic, so this would mean a construction defect).
3. The rule above is applied to the numbers, the table and the verdict are recorded in this log (a section "Results and selection
   (dev only)"), and nothing is adopted: the committed data, lexicon, weights and `training.json` stay as they are. Adopting NO-ING,
   if it is selected, is a separate step with its own record and, for the held-out sets, a disclosed second look.
4. `evaluate` and `thresholds` are not run; no held-out file is evaluated.

### Results and selection (dev only)

Order of events (the git history and the file times agree): pre-registration commit `476a51d` at 2026-11-06 09:29:28 (-05:00); the first
follow-up model was trained at 09:31:23 and the last at 09:35:01; the scoring was run after all 15 models existed. Arm builds: the
rows removed are exactly those of the pre-registration (NO-ING 861: open train 151, open dev 22, MASC 518, silver 170; NO-CTRL 734:
open train 152, open dev 19, MASC 563); the rebuilt lexicon of NO-ING has 262,635 spellings / 339,819 entries (`lexicon.bin` 639,711
bytes, as in the diagnosis), that of NO-CTRL 262,634 / 339,798 (639,690 bytes); the shipped-seed images are 1,077,990 bytes (FULL),
1,080,650 (NO-ING, 116,662 rows). Scoring: scratch tool `prenominal-ing/score_arms.py` (committed with this record) over the
prediction files of the runtime decoder; only open dev and UD-EWT dev were read; no `evaluate`, `thresholds` or held-out file.

**Acceptance test: passed on every point.** FULL at the shipped seed: open dev 103 harmful of 4,467 decisions, UD-EWT dev 107 of 4,570,
UD agreement 23,338 of 25,148 (92.803%), and its weights are **byte-identical** to the committed `weights.bin` (SHA-256
`ccd38ef2...bce`). The five FULL seeds reproduce the diagnosis arm NEW (UD agreement 92.803, 92.711, 92.655, 92.695, 92.747) and their
weights are byte-identical to the diagnosis models; NO-ING reproduces the diagnosis ablation exactly (agreement 92.711, 92.779, 92.683,
92.616, 92.747; open harmful 90, 98, 96, 100, 90; UD harmful 103, 109, 95, 99, 101; weights byte-identical to the diagnosis models).
So the FULL and NO-ING numbers below are the diagnosis numbers; what is new is the pooled rate, the compound-head measure, NO-CTRL
and the rule.

Per-seed values (seeds 20260101, 11, 12, 13, 14) and mean +- sample sd (n = 5). Harmful counts in brackets are harmful / decisions.

| Metric | Arm | 20260101 | 11 | 12 | 13 | 14 | mean +- sd |
| --- | --- | ---: | ---: | ---: | ---: | ---: | ---: |
| **Pooled harmful / 1k** (primary) | FULL | 23.238 (210/9,037) | 22.353 (202) | 22.795 (206) | 22.242 (201) | 22.795 (206) | **22.685 +- 0.399** |
| | NO-ING | 21.357 (193) | 22.906 (207) | 21.135 (191) | 22.021 (199) | 21.135 (191) | **21.711 +- 0.760** |
| | NO-CTRL | 22.242 (201) | 21.689 (196) | 23.016 (208) | 23.348 (211) | 20.914 (189) | 22.242 +- 0.987 |
| Open dev harmful / 1k (relabeled) | FULL | 23.058 (103/4,467) | 21.491 (96) | 21.939 (98) | 22.386 (100) | 22.610 (101) | 22.297 +- 0.605 |
| | NO-ING | 20.148 (90) | 21.939 (98) | 21.491 (96) | 22.386 (100) | 20.148 (90) | 21.222 +- 1.031 |
| | NO-CTRL | 22.834 (102) | 20.372 (91) | 23.058 (103) | 22.163 (99) | 19.700 (88) | 21.625 +- 1.507 |
| UD-EWT dev harmful / 1k | FULL | 23.414 (107/4,570) | 23.195 (106) | 23.632 (108) | 22.101 (101) | 22.976 (105) | 23.063 +- 0.591 |
| | NO-ING | 22.538 (103) | 23.851 (109) | 20.788 (95) | 21.663 (99) | 22.101 (101) | 22.188 +- 1.133 |
| | NO-CTRL | 21.663 (99) | 22.976 (105) | 22.976 (105) | 24.508 (112) | 22.101 (101) | 22.845 +- 1.090 |
| UD-EWT dev agreement % | FULL | 92.803 | 92.711 | 92.655 | 92.695 | 92.747 | 92.722 +- 0.056 |
| | NO-ING | 92.711 | 92.779 | 92.683 | 92.616 | 92.747 | 92.707 +- 0.063 |
| | NO-CTRL | 92.683 | 92.747 | 92.799 | 92.683 | 92.811 | 92.745 +- 0.061 |
| **Compound-head NOUN accuracy %** (248 items) | FULL | 90.323 | 90.323 | 88.306 | 89.919 | 89.919 | **89.758 +- 0.836** |
| | NO-ING | 87.500 | 89.516 | 88.710 | 89.516 | 88.710 | **88.790 +- 0.826** |
| | NO-CTRL | 89.113 | 89.113 | 88.710 | 89.113 | 88.710 | 88.952 +- 0.221 |
| compound-ing NOUN accuracy % (41) | FULL | 70.732 | 68.293 | 58.537 | 65.854 | 65.854 | 65.854 +- 4.563 |
| | NO-ING | 60.976 | 60.976 | 63.415 | 63.415 | 63.415 | 62.439 +- 1.336 |
| | NO-CTRL | 63.415 | 65.854 | 68.293 | 65.854 | 63.415 | 65.366 +- 2.041 |
| compound-zero NOUN accuracy % (207) | FULL | 94.203 | 94.686 | 94.203 | 94.686 | 94.686 | 94.493 +- 0.265 |
| | NO-ING | 92.754 | 95.169 | 93.720 | 94.686 | 93.720 | 94.010 +- 0.942 |
| | NO-CTRL | 94.203 | 93.720 | 92.754 | 93.720 | 93.720 | 93.623 +- 0.529 |
| **Verbal-ing VERB accuracy %** (384 items) | FULL | 92.969 | 92.188 | 93.750 | 93.490 | 93.750 | 93.229 +- 0.664 |
| | NO-ING | 89.844 | 90.885 | 90.104 | 90.885 | 92.448 | 90.833 +- 1.015 |
| | NO-CTRL | 93.750 | 93.490 | 93.229 | 94.531 | 94.010 | 93.802 +- 0.501 |
| Open dev accuracy % (context) | FULL | 96.375 | 96.325 | 96.367 | 96.371 | 96.298 | 96.347 +- 0.034 |
| | NO-ING | 96.348 | 96.329 | 96.302 | 96.298 | 96.352 | 96.326 +- 0.025 |
| | NO-CTRL | 96.375 | 96.340 | 96.302 | 96.394 | 96.367 | 96.356 +- 0.035 |

Differences against FULL (arm minus FULL, mean of the 5 seeds; descriptive, the rule is the selection). Welch t 95% interval
(degrees of freedom, p) and the combined seed + document bootstrap 95% interval (2,000 replicates, seed 20261104):

| Metric | NO-ING - FULL: difference, Welch, bootstrap | NO-CTRL - FULL: difference, Welch, bootstrap |
| --- | --- | --- |
| Pooled harmful / 1k | **-0.974** [-1.911, -0.036] (6.0, p = 0.044); [-2.581, +0.699] | -0.443 [-1.647, +0.762] (5.3, p = 0.39); [-1.979, +1.046] |
| Open dev harmful / 1k | -1.075 [-2.360, +0.211] (6.5, p = 0.088); [-3.454, +1.285] | -0.672 [-2.511, +1.168]; [-2.622, +1.226] |
| UD-EWT dev harmful / 1k | -0.875 [-2.272, +0.521] (6.0, p = 0.18); [-3.104, +1.514] | -0.219 [-1.567, +1.129]; [-2.228, +1.842] |
| UD-EWT dev agreement (points) | -0.015 [-0.102, +0.071]; [-0.148, +0.114] | +0.022 [-0.063, +0.107]; [-0.106, +0.151] |
| Compound-head NOUN accuracy (points) | -0.968 [-2.180, +0.245] (8.0, p = 0.10); [-2.908, +0.701] | -0.806 [-1.831, +0.218]; [-2.778, +0.918] |
| compound-ing NOUN accuracy (points) | -3.415 [-8.995, +2.165]; [-12.246, +4.375] | -0.488 [-6.070, +5.094]; [-6.286, +5.000] |
| compound-zero NOUN accuracy (points) | -0.483 [-1.635, +0.669]; [-2.039, +0.788] | -0.870 [-1.520, -0.219] (p = 0.017); [-3.051, +0.953] |
| **Verbal-ing VERB accuracy (points)** | **-2.396 [-3.683, -1.109] (6.9, p = 0.003); [-4.270, -0.714]** | +0.573 [-0.296, +1.442]; [-0.400, +1.734] |
| Open dev accuracy (points) | -0.021 [-0.065, +0.023]; [-0.106, +0.063] | +0.008 [-0.042, +0.059]; [-0.080, +0.099] |

NO-ING minus NO-CTRL on the pooled harmful rate: -0.531 per 1,000, Welch [-1.830, +0.768].

**Application of the pre-registered rule, exactly as written.**

- Primary (mean pooled harmful per 1,000): FULL 22.685, NO-ING 21.711; the lowest candidate is NO-ING and the difference FULL minus
  NO-ING is **0.974 per 1,000**, which is not under the 0.5 tie-break threshold.
- C1 (UD-EWT agreement within 0.15 points of the best candidate): the best is FULL (92.722); NO-ING 92.707, shortfall 0.015: **holds**.
- C2 (compound-head NOUN accuracy within 2 points of the best candidate): the best is FULL (89.758); NO-ING 88.790, shortfall
  0.968: **holds**.
- **Selected arm: NO-ING** (it has the lowest primary mean by at least 0.5 per 1,000 and both constraints hold). The shipped seed
  stays 20260101 whichever arm wins.
- Negative control (context only, not a candidate): NO-CTRL (734 `propn-boundary` rows removed) has a pooled mean of 22.242, 0.443
  per 1,000 below FULL (Welch interval contains 0). The pre-registered reading aid compares it with half of the NO-ING reduction
  (0.487): 0.443 is **below** that half, so the aid reads "a comparable removal does not match the NO-ING reduction". It is a narrow
  margin (the NO-CTRL reduction is 45% of the NO-ING one), the two arms' interval of difference contains 0 (-0.531, Welch [-1.830,
  +0.768]), and removing a set of the same size of an unrelated detector also moved the harmful rates down by about 0.4 per 1,000:
  part of the NO-ING reduction may be what any such removal does or ordinary seed variation.

**Reading (what is better and what is worse, stated plainly).**

- The primary evidence is thin. The Welch interval of the pooled difference ([-1.911, -0.036]) just excludes 0 while the combined
  seed + document bootstrap interval ([-2.581, +0.699]) contains it; the rule is a point-estimate rule on 5 seeds and the number
  that selects NO-ING (0.974) is about the size of the seed standard deviation of an arm (0.4 to 1.0). The result is the one the
  diagnosis already showed for these two arms, so it is not an independent confirmation.
- **Not part of the rule, and against NO-ING: the verbal -ing VERB accuracy falls 2.4 points** (93.229 to 90.833; Welch [-3.683,
  -1.109], bootstrap [-4.270, -0.714]; the per-seed ranges barely overlap: FULL 92.188 to 93.750, NO-ING 89.844 to 92.448):
  without the `prenominal-ing` corrections fewer genuine verbal -ing tokens are tagged VERB (the wrong tags were not tabulated).
  The rule had no constraint on this control, as pre-registered, and the rule is applied as written; but the cost is visible and
  is one of the things an adoption decision has to weigh.
- Compound-head NOUN accuracy is lower in NO-ING (-0.97 points; within the 2-point constraint), driven by the 41 `compound-ing`
  items (-3.4 points, wide interval, 41 items); the zero-derived items hardly move. UD-EWT agreement and open dev accuracy are
  unchanged (-0.015 and -0.021 points).
- NO-ING keeps the pre-cleanup labels of the 861 tokens (and rebuilds the lexicon from them), so it moves part of the corpus back
  to the old convention for -ing words (the corrected `ADJ`/`VERB` choices of `prenominal-ing`); whether the convention linter
  and the corrections tables stay consistent with such files was not checked here and is for the adoption step, if any.

**Nothing is adopted.** The committed data, lexicon, weights and `training.json` are unchanged by this follow-up; `evaluate` and
`thresholds` were not run; no held-out set was read. If NO-ING is to be adopted, that is a separate step (step 3) with its own record
and, for the held-out sets, a disclosed second look (the held-out sets were seen once, for the model of `58da737`).

**Reproducibility note (added after the review of this record; the committed figures above are not altered).** The tables above
were produced by the first version of `prenominal-ing/score_arms.py`, which used numpy 2.2 (`default_rng`, seed 20261104) and scipy
1.18 for the Student t quantile and the bootstrap; neither package was declared in the repository. The script is now standard
library only (`math`, `statistics`, `random`; the t quantile and p-value come from the regularized incomplete beta function, checked
by `--selftest` against published values). It is meant to be run against a worktree of `476a51d` or `0d7a0b3` with the scratch
folder of the 15 runs, as in the Procedure above. Re-running it on the same 30 prediction files (worktree of `0d7a0b3`) reproduces
the committed output **exactly** for the acceptance test, every per-seed value, mean and sample sd, every Welch interval, degree of
freedom and p-value, and the rule outcome (selected arm NO-ING, same numbers); only the 18 bootstrap-interval cells differ, because
`random.Random(20261104)` is another stream than the numpy generator, so they agree with the committed ones statistically and not
digit for digit. Examples (committed, then standard library): pooled harmful rate NO-ING - FULL [-2.581, +0.699] and [-2.643,
+0.640]; NO-CTRL - FULL [-1.979, +1.046] and [-1.925, +0.961]; verbal-ing VERB accuracy NO-ING - FULL [-4.270, -0.714] and
[-4.204, -0.580]. Over the 18 cells the largest endpoint difference is 0.30 points (the 41-item `compound-ing` row, interval width
16.6) and no endpoint moves by more than 5.4% of its interval width, which is the Monte-Carlo error of 2,000 replicates. No conclusion changes: whether an interval contains 0 is the same in all 18 cells (the pooled bootstrap interval of
NO-ING - FULL contains 0 in both, the verbal-ing one excludes 0 in both).

## Corpus cleanup C: adoption of the NO-ING model (step 3 of the pre-registered follow-up; written before any held-out command)

The pre-registered `prenominal-ing` follow-up selected NO-ING (above), the owner decided to adopt it, and this section records the
adoption **before any held-out command** of this step (`thresholds` and `evaluate` have not been run; git history shows the
order: tooling, data, retraining, then this section, then the held-out commands, then the evaluation record). No hyperparameter,
source, setting or seed changed.

### Disclosure: the second look

**The held-out sets (open test, gold v2 test, shape probe, STE challenge set, UD-EWT test) were seen once for the FULL model**
(the evaluation of commit `58da737`, model image `dca810e4...339d`). The evaluation after this section is the **second
evaluation of the model family** and a **disclosed second look** at sets that were already seen for the sibling model: the
selection between FULL and NO-ING was made on development data only, but the held-out sets are no longer unseen for this
family, so its held-out result is evidence of a weaker kind than the first. It is **not** an erratum of the earlier records,
which stay as written. **Any further held-out evaluation would be a third look and is not planned.** The one `evaluate` of this
step is run once; no second run is made after a result exists (an infrastructure failure without a result may be rerun without
looking at results to tune).

### What changed

- **Data.** Every adjudicated row whose token a `prenominal-ing` candidate covers is withdrawn from `adjudicated.tsv`: **878
  rows**, i.e. the 861 rows of the NO-ING arm (open train 151, open dev 22, MASC 518, silver 170) **and the 17 open test rows**.
  Decision and reason: the arm left the 17 test rows out only so that no held-out file was read; with them left in, the corpora
  would follow two conventions for `-ing` words (open train and dev withdrawn, open test not). Withdrawing all 878 gives open
  train, dev and test the same convention. The 17 test rows had **no influence on training, calibration or selection** (open test
  is never trained on), so the trained model is identical to the NO-ING arm at seed 20260101. **These 17 open test labels
  differ from the labels that the first held-out evaluation used.** The rows stay as provenance in
  `data/corrections/withdrawn-prenominal-ing.tsv` (the new `corrections.py withdraw` command; the file is never applied and
  `validate` requires the old corpus value of every token of it); `adjudicated.tsv` has 6,185 rows (3,289 tag changes, 1,311
  flag changes, 1,585 `disputed` marks; 7,063, 3,972, 1,312 and 1,779 before). Effective rows: 8,407 (9,285 before; 2,222
  mechanical in effect and 6,185 adjudicated, 3 mechanical rows superseded as before). The corpora were regenerated with the
  state-aware tooling, no data file was edited by hand: `apply --write --reverse` (all rows), `withdraw --detector prenominal-ing
  --write`, `apply --write`; then `normalize_eol.py` (no change), `make_manifest.py`, `make_corrections.py --check` (passes in the
  applied state) and `fixed-point` (0 left), `check_leakage.py`, `check_licenses.py` (the new file is scanned) all pass. The
  corrections README records the decision, the reason and the caveats.
- **Convention linter (G3 check).** `check_conventions.py --tier fix --enforce`: **0 violations of the fix tier in every source and
  split** (open train/dev/test, MASC, silver, STE), exit 0. Check tier (report only) **298**, unchanged: open train 95, dev 12,
  test 17, MASC 66, silver 106, STE 2 (251 `be-participle-adj-not-gradable`, 26 `be-aux-passive`, 13 `infinitive-to`, 4
  `be-passive-agent`, 2 `no-all-degree`, 2 `out-of-phrasal`).
- **Lexicon.** Rebuilt (`build_lexicon.py`, `LexiconCompiler`, `check_lexicon.py` gate G1 passes): 262,635 spellings, **339,819**
  entries (339,823 before), `lexicon.bin` **639,711** bytes (639,617), 1,626,269 bytes uncompressed, 225 masks. Coverage: open
  train 99.935% of all tokens and 100.000% of the non-disputed ones (201,130 of 201,131), **open dev 99.874% and 99.921%** non-disputed
  (25,277 of 25,297; 593 unknown-spelling tokens), open test 99.843% and 99.909% (reported as always, never used to choose
  anything), MASC 99.859% and 99.864%. The K sweep was re-run (`sweep.tsv`; the chosen K = 1, M = 2 row is unchanged in its
  figures).
- **Model.** `train` with the shipped `training.json` (sources open x2, MASC, silver >= 80, restriction `history`, predicted
  history, 6 epochs, 19 hash bits, minimum count 1, pruning 8, scale 8, **seed 20260101**), then `calibrate --update-config` (the
  temperature is refit on the new open dev: **6.442**, NLL 0.12220, ECE 0.260%; it was 6.583), `train --update-config` and
  `train --check` (exit 0). 116,662 rows, **282,426 weights, 1,080,650 bytes raw, 478,835 bytes deflated** (116,448 rows,
  281,682 weights, 1,077,990 and 477,899 before), `imageSha256` `a0d45458308f78dbe74669e563ea236a0a7b4688638d690dd0995ac4de988da3`.
  **The new `weights.bin` is byte-identical to the NO-ING seed-20260101 weights of the follow-up** (SHA-256 `439d4af5...4092`
  both; the arm calibrated on its own open dev, which equals the new open dev), and the predictions of the embedded model on
  open dev and UD-EWT dev are byte-identical to the follow-up's prediction files. Golden hashes of `ModelTrainerTests`,
  the lexicon counts of `EmbeddedModelTests` and `LexiconTests` and the size figures of the documents were re-pinned as in the
  first cleanup phase; the Python suite (256 tests, 9 of them new) passes, and the C# suite fails only the three tests that
  pin the held-out record (they pass again when the record is reissued below).

### Development figures of the adopted model (dev data only)

Definitions as in the pre-registration (decision = gold NOUN or VERB with both tags among the candidates; harmful = the other of
NOUN and VERB with confidence >= 0.45; per 1,000 decisions; bootstrap by document, 1,000 resamples, seed 20261104; differences
paired). Open dev has **two label sets** now: FULL's relabeled dev (the one the follow-up scored every arm on, 4,467
decisions) and the dev file of this model (22 labels reverted, 4,451 decisions); both are shown. The figures are of the seed
20260101 model; the five-seed means of the follow-up are in its table.

| Measure | Earlier model (before the cleanup) | FULL (58da737) | NO-ING (adopted) |
| --- | --- | --- | --- |
| Open dev harmful per 1k, FULL's labels | 92 of 4,468 = 20.59 | 103 of 4,467 = 23.058 [17.527, 29.066] | **90 of 4,467 = 20.148 [15.322, 25.472]** (-2.910 [-5.867, 0.000] vs FULL) |
| Open dev harmful per 1k, own labels | 94 of 4,484 = 20.96 (old labels) | 102 of 4,451 = 22.916 [17.419, 29.083] (FULL's predictions on these labels) | **90 of 4,451 = 20.220 [15.357, 25.542]** (-2.696 [-5.613, +0.259]) |
| Open dev accuracy % | 96.07 (old labels) | 96.375 | 96.348 (FULL's labels), 96.378 [95.582, 96.922] (own labels) |
| UD-EWT dev agreement % (mapped, 25,148 tokens) | 92.361 [91.803, 92.881] | 92.803 [92.252, 93.312] | **92.711 [92.167, 93.187]** (23,315; -0.091 [-0.274, +0.100] vs FULL) |
| UD-EWT dev harmful per 1k | 100 of 4,573 = 21.87 | 107 of 4,570 = 23.414 [18.774, 28.587] | **103 of 4,570 = 22.538 [17.835, 27.489]** (-0.875 [-4.515, +2.799]) |
| Pooled harmful per 1k (open dev FULL's labels + UD dev) | n/a | 210 of 9,037 = 23.238 | 193 of 9,037 = 21.357 |
| Gold v2 dev (trainer `eval`) | 95.34%, 9 of 611 | 94.99%, 14 of 611 = 22.9 | 95.24%, **11 of 611 = 18.0**, NLL 0.2601, ECE 1.99% |
| Open dev (trainer `eval`, own labels) | 96.07% | 96.37% | 96.38%, ambiguous 95.68% (17,628 tokens), NLL 0.1222, ECE 0.26% |
| Verbal -ing VERB accuracy, % (384 frozen items) | n/a | 92.969 (357 of 384) | **89.844 (345 of 384)** |
| Compound-head NOUN accuracy, % (248 frozen items) | n/a | 90.323 (224) | **87.500 (217)**; compound-ing 60.976 (25 of 41) vs 70.732; compound-zero 92.754 vs 94.203 |

The earlier model (before the cleanup) was scored on the relabeled open dev in the first cleanup phase (92 of 4,468) and on the
UD-EWT dev (100 of 4,573); it is a different model on a corpus with different labels, and these are the figures of the gate
table above. The gate G2 of the first phase stays recorded as failed for FULL and is not re-judged; for information, the
adopted model is below the earlier model's open dev rate (20.15 against 20.59) and above its UD dev rate (22.54 against 21.87).

**What is worse (stated before the held-out evaluation).** (1) The cost already found by the follow-up: the verbal `-ing`
VERB accuracy of the adopted model is 89.844% against 92.969% for FULL at this seed (-2.4 points on the five-seed means, Welch
[-3.683, -1.109]); the selection rule had no constraint on it. (2) The compound-head NOUN accuracy is 87.500% against 90.323%
(-0.97 points on the five-seed means, inside the 2-point constraint; `compound-ing` items 41, the largest drop). (3) UD-EWT dev
agreement is 0.09 points below FULL at this seed (inside the C1 constraint). (4) The primary evidence behind the choice is thin:
the pooled difference of 0.974 harmful per 1,000 on five seeds has a Welch interval that just excludes 0 and a bootstrap interval
that contains it, a comparable removal of an unrelated detector gave about half of it, and the seed-20260101 differences above
have paired intervals that contain 0 on every dev measure (the open dev one with FULL's labels reaches 0.000 at its upper end).
The adopted model is selected on development data by the pre-registered rule, nothing more.

### Procedure of the held-out step (fixed now)

1. Copy the committed held-out record (`docs/evaluation/test-results.md` and `.json`, `operating-points.json`,
   `confidence-thresholds.md`) outside the repository for the comparison; it stays in git history (the FULL record at `58da737`).
2. `thresholds` (development data only; rule `nv-pooled-dev-v1`, grid, targets and floors unchanged) recomputes the strict and
   balanced operating points for the adopted model. The two constants `FrozenOpenHarmful` and `FrozenGoldHarmful` of the
   trainer (103 and 14 for FULL) are set to the development figures of the adopted model as the trainer's `eval` and the curve
   report them (90 and 11); they only guard that the 0.45 row reproduces the freeze figures.
3. Delete the old `test-results.json` and `.md` (`evaluate` refuses to run while one exists; the record of a retrained model is a
   new record), then `evaluate` **once**. The stop conditions that fired in both earlier records (gold v2 `to`/`not`, UD-EWT raw
   gold outside the candidates, the shape probe's harmful errors) may fire again; they are reported, not tuned.
4. The evaluation record is written afterwards as one more section of this log, side by side for the earlier model, FULL and
   the adopted model; open test is not strictly comparable across the three (its labels changed twice: with the cleanup, and for
   the 17 withdrawn test rows); UD-EWT, gold v2, the shape probe and the STE set have unchanged labels and are the fair
   comparisons. Nothing is claimed as an improvement unless the data show it.

### `thresholds` result (development data only; committed before the held-out run)

`thresholds` first refused to write with the old constants (it printed 90, 11 and 101 harmful errors at 0.45 against the frozen
figures 103, 14 and 117 of FULL and wrote nothing); the two constants were then set to 90 and 11 (comments in `Program.cs`) and
the command wrote `operating-points.json` and `confidence-thresholds.md` (model image `a0d45458...8da3`, temperature 6.442, new
dev file hashes). The rule `nv-pooled-dev-v1` gave: **strict 0.95** (coverage 81.8% [80.4, 83.3], 3.1 harmful per 1,000 answered
[1.3, 5.3], 13 of 4,141) and **balanced 0.80** (coverage 92.4% [91.4, 93.3], 9.0 [5.8, 12.1], 42 of 4,675); the earlier model had the
same thresholds (strict 0.95: 80.6%, 3.9, 16 of 4,105; balanced 0.80: 91.0%, 9.9, 46 of 4,636) and FULL had 0.90 and 0.85
(strict 86.5%, 4.8; balanced 89.8%, 7.7).

## Corpus cleanup C: held-out evaluation of the adopted NO-ING model (disclosed second look; a new record, not an erratum)

This is a **new** record for the adopted model. It does not correct or replace the earlier two ("Outcome of the held-out run"
and its errata for the model before the cleanup, and "Corpus cleanup C: held-out evaluation of the retrained model" for FULL),
which stay as written. Order (git history): adoption record above (`01b842a`), `thresholds` (`24b0664`), the deletion of the
FULL record on purpose (`524fbc0`; `evaluate` refuses to run while a record exists; the FULL record stays in git at `58da737`
and was copied outside the repository first), the one `evaluate` (`4d40101`), this section and the documents after it.

**This is the second look at these held-out sets.** They were evaluated once for the FULL model and are evaluated here for its
sibling, NO-ING, which was selected on development data by the pre-registered rule. A third look is not planned. `evaluate
--preflight` passed first (it scores nothing); `evaluate --dump-dir <folder outside the repository>` then ran **once** (BelowNormal,
the treebank from the git-ignored cache). It exited with 1 because of the stop conditions below (findings, not failures of the
run) and the committed files are the output as produced. **No second `evaluate` run was made.** Nothing was tuned.

Stop conditions of the protocol: the **same three fired as in both earlier records** (earlier model, FULL in brackets):

- `shape-probe`: 48 harmful errors at 0.45 against a limit of 28 (5% of 560 rows) (48, 52).
- `gold-v2-test-to-not`: gold outside the candidates for 50.00% of the 8 targets (the same, the same); the `to`/`not` convention
  difference, a view reported separately.
- `ud-test-raw`: gold outside the candidates for 1.93% (484 of 25,094 words) (1.68%, 1.93%). The primary mapped view is at 0.55%
  (138 words) (0.56%, 0.55%), under the limit.

All other stop conditions held: the open test accuracy 96.53% against 96.38% on development; the harmful rates per 1,000 answered
on open test at the strict (3.5) and balanced (7.3) points are below twice the pooled development rates (3.1 and 9.0); open test
ECE 0.20%; open test gold outside the candidates 41 of 26,185 tokens (0.16%).

**What is comparable (stated plainly).** The labels of the **open test** split changed **twice** relative to the earliest record:
with the corrections of the first cleanup (476 correction rows in it), and again with the **17 withdrawn `prenominal-ing` test
rows** (relative to FULL's record), so the open test figures of the three models are **not strictly comparable** with each other
(the earlier model was scored on the original labels, FULL on the corrected ones, NO-ING on corrected labels minus these 17
corrections). The labels of **UD-EWT, gold v2, the shape probe and the STE challenge set are unchanged** in all three records,
and these four are the **fair comparisons** across the earlier model (model image `68673a9f...`, main `a94abd0`), FULL (`dca810e4...`,
`58da737`) and NO-ING (`a0d45458...`, this record). Intervals are the 95% percentile bootstrap over documents of each record (1,000
resamples, seed 1); no paired interval between models was computed, so overlapping intervals are not a test.

Accuracy and harmful errors at the harmful limit 0.45, earlier model, FULL and NO-ING (same columns as the report; "n/c" marks
the set whose labels are not comparable across the three):

| Set | Model | Accuracy % | Ambiguous accuracy % | Decisions | Harmful | Harmful per 1,000 decisions | ECE % | NLL | Gold outside candidates |
| --- | --- | --- | --- | ---: | ---: | --- | --- | --- | ---: |
| open-test (n/c) | earlier | 96.10 [95.69, 96.49] | 95.16 [94.72, 95.61] | 4,413 | 91 | 20.6 [15.2, 26.5] | 0.28 [0.19, 0.61] | 0.1394 [0.1211, 0.1602] | 71 |
| | FULL | 96.46 [96.07, 96.81] | 95.56 [95.19, 95.92] | 4,408 | 91 | 20.6 [15.7, 26.1] | 0.24 [0.19, 0.52] | 0.1163 [0.1013, 0.1322] | 41 |
| | **NO-ING** | 96.53 [96.12, 96.87] | 95.69 [95.30, 96.05] | 4,392 | 82 | 18.7 [14.0, 23.7] | 0.20 [0.17, 0.52] | 0.1161 [0.1013, 0.1320] | 41 |
| gold-v2-test | earlier | 94.57 [93.67, 95.39] | 91.70 [90.33, 92.93] | 936 | 23 | 24.6 [14.9, 34.8] | 1.82 [1.47, 2.80] | 0.2178 [0.1812, 0.2596] | 7 |
| | FULL | 94.34 [93.46, 95.16] | 91.34 [90.02, 92.55] | 936 | 29 | 31.0 [19.9, 42.4] | 2.23 [1.80, 3.18] | 0.2491 [0.2065, 0.2971] | 12 |
| | **NO-ING** | 94.37 [93.55, 95.18] | 91.39 [90.11, 92.64] | 936 | 28 | 29.9 [19.0, 41.2] | 2.49 [2.02, 3.38] | 0.2497 [0.2052, 0.2989] | 12 |
| gold-v2-test-full-agreement | earlier | 95.39 [94.54, 96.24] | 92.49 [91.12, 93.81] | 713 | 17 | 23.8 [13.4, 35.9] | 1.43 [1.11, 2.34] | 0.1838 [0.1448, 0.2241] | 4 |
| | FULL | 95.16 [94.26, 96.00] | 92.11 [90.65, 93.51] | 713 | 21 | 29.5 [17.9, 40.7] | 1.83 [1.32, 2.70] | 0.1960 [0.1560, 0.2379] | 5 |
| | **NO-ING** | 95.23 [94.36, 96.05] | 92.23 [90.85, 93.54] | 713 | 22 | 30.9 [18.9, 44.3] | 1.85 [1.43, 2.74] | 0.1971 [0.1557, 0.2424] | 5 |
| gold-v2-test-to-not (8 targets) | earlier | 50.00 [12.50, 87.50] | 100.00 | 0 | 0 | 0.0 | 56.66 [27.17, 88.44] | 6.9865 [1.9304, 12.0984] | 4 |
| | FULL | 50.00 [12.50, 87.50] | 100.00 | 0 | 0 | 0.0 | 55.56 [25.67, 88.55] | 6.9707 [1.8828, 12.0995] | 4 |
| | **NO-ING** | 50.00 [12.50, 87.50] | 100.00 | 0 | 0 | 0.0 | 55.64 [24.45, 87.44] | 6.9910 [1.9506, 12.0940] | 4 |
| shape-probe (560 rows) | earlier | 89.11 [86.61, 91.61] | 89.21 [86.57, 91.56] | 556 | 48 | 86.3 [64.5, 109.9] | 2.38 [1.99, 5.31] | 0.2766 [0.2228, 0.3351] | 0 |
| | FULL | 87.50 [84.82, 90.18] | 87.41 [84.78, 90.13] | 556 | 52 | 93.5 [69.8, 118.7] | 1.99 [2.01, 5.24] | 0.2829 [0.2233, 0.3433] | 0 |
| | **NO-ING** | 89.64 [87.14, 92.14] | 89.57 [87.07, 92.09] | 556 | 48 | 86.3 [63.1, 109.9] | 3.53 [2.84, 6.63] | 0.2704 [0.2143, 0.3291] | 0 |
| challenge-set (STE, 272 tokens) | earlier | 87.13 [81.18, 92.70] | 88.78 [82.65, 93.72] | 86 | 0 | 0.0 | 7.80 [3.84, 12.98] | 0.3317 [0.1953, 0.4885] | 0 |
| | FULL | 87.50 [82.56, 92.37] | 87.37 [80.98, 92.51] | 86 | 0 | 0.0 | 8.51 [5.28, 13.67] | 0.3842 [0.2251, 0.5604] | 0 |
| | **NO-ING** | 88.60 [83.03, 93.65] | 89.90 [83.65, 94.79] | 86 | 0 | 0.0 | 6.25 [4.04, 11.47] | 0.3644 [0.2095, 0.5371] | 0 |
| ud-test-mapped (UD-EWT, primary view) | earlier | 92.30 [91.75, 92.82] | 91.71 [91.05, 92.26] | 4,413 | 73 | 16.5 [12.6, 20.9] | 2.36 [2.06, 2.81] | 0.3175 [0.2909, 0.3505] | 140 |
| | FULL | 92.97 [92.46, 93.44] | 92.52 [91.90, 93.05] | 4,410 | 83 | 18.8 [14.5, 23.5] | 2.06 [1.78, 2.50] | 0.2982 [0.2733, 0.3319] | 138 |
| | **NO-ING** | 92.85 [92.33, 93.31] | 92.43 [91.83, 92.96] | 4,410 | 79 | 17.9 [13.9, 22.5] | 2.22 [1.90, 2.70] | 0.3025 [0.2770, 0.3373] | 138 |
| ud-test-raw (UD-EWT, raw tags) | earlier | 88.40 [87.84, 88.91] | 87.13 [86.45, 87.73] | 4,010 | 72 | 18.0 [13.7, 22.6] | 6.22 [5.86, 6.73] | 0.6062 [0.5731, 0.6457] | 421 |
| | FULL | 89.05 [88.53, 89.53] | 88.29 [87.60, 88.86] | 4,007 | 82 | 20.5 [15.6, 25.8] | 5.96 [5.59, 6.48] | 0.6106 [0.5764, 0.6509] | 484 |
| | **NO-ING** | 88.92 [88.38, 89.40] | 88.18 [87.53, 88.74] | 4,007 | 79 | 19.7 [15.3, 24.8] | 6.15 [5.75, 6.67] | 0.6191 [0.5849, 0.6607] | 484 |

(The shape-probe ECE interval of the FULL record, [2.01, 5.24], does not contain its point estimate 1.99; that is how the report's
percentile interval of this small set behaved and it was reproduced as written.)

**Operating points.** The rule `nv-pooled-dev-v1` was applied unchanged to the dev curve of NO-ING and selected the **same
thresholds as for the earlier model (strict 0.95, balanced 0.80)**; FULL had 0.90 and 0.85. Development figures (pooled open dev and
gold v2 dev, 95% bootstrap intervals over documents):

| Point | Model | Threshold | Coverage % [95%] | Harmful per 1,000 answered [95%] | Harmful of answered |
| --- | --- | ---: | --- | --- | --- |
| strict | earlier | 0.95 | 80.6 [79.1, 82.0] | 3.9 [1.8, 6.1] | 16 of 4,105 |
| | FULL | 0.90 | 86.5 [85.2, 87.7] | 4.8 [2.7, 7.0] | 21 of 4,392 |
| | **NO-ING** | 0.95 | 81.8 [80.4, 83.3] | 3.1 [1.3, 5.3] | 13 of 4,141 |
| balanced | earlier | 0.80 | 91.0 [90.0, 92.0] | 9.9 [6.6, 13.4] | 46 of 4,636 |
| | FULL | 0.85 | 89.8 [88.7, 90.9] | 7.7 [4.8, 11.0] | 35 of 4,562 |
| | **NO-ING** | 0.80 | 92.4 [91.4, 93.3] | 9.0 [5.8, 12.1] | 42 of 4,675 |

On the held-out sets (the thresholds are those selected on development data; coverage = share of the noun-verb decisions answered):

| Set | Point | Model (threshold) | Coverage % | Harmful per 1,000 answered [95%] | Harmful of answered |
| --- | --- | --- | ---: | --- | --- |
| open-test (n/c) | reference 0.45 | earlier | 99.9 | 20.6 [15.2, 26.5] | 91 of 4,407 |
| | | FULL | 99.8 | 20.7 [15.8, 26.1] | 91 of 4,399 |
| | | NO-ING | 99.7 | 18.7 [14.1, 23.8] | 82 of 4,381 |
| | strict | earlier (0.95) | 82.3 | 4.1 [2.0, 6.9] | 15 of 3,631 |
| | | FULL (0.90) | 87.7 | 5.7 [3.0, 8.8] | 22 of 3,868 |
| | | **NO-ING (0.95)** | 83.8 | 3.5 [1.6, 6.0] | 13 of 3,682 |
| | balanced | earlier (0.80) | 92.3 | 9.3 [6.1, 12.6] | 38 of 4,072 |
| | | FULL (0.85) | 90.9 | 6.5 [3.5, 9.7] | 26 of 4,005 |
| | | **NO-ING (0.80)** | 93.3 | 7.3 [4.5, 10.4] | 30 of 4,097 |
| gold-v2-test | reference 0.45 | earlier | 99.8 | 24.6 [14.9, 35.0] | 23 of 934 |
| | | FULL | 99.9 | 31.0 [19.9, 42.4] | 29 of 935 |
| | | NO-ING | 99.8 | 30.0 [19.1, 41.3] | 28 of 934 |
| | strict | earlier (0.95) | 81.3 | 3.9 [0.0, 9.1] | 3 of 761 |
| | | FULL (0.90) | 86.3 | 5.0 [1.2, 10.3] | 4 of 808 |
| | | **NO-ING (0.95)** | 82.2 | 6.5 [1.4, 13.3] | 5 of 769 |
| | balanced | earlier (0.80) | 92.5 | 10.4 [3.9, 17.8] | 9 of 866 |
| | | FULL (0.85) | 89.1 | 9.6 [3.7, 17.3] | 8 of 834 |
| | | **NO-ING (0.80)** | 91.9 | 9.3 [3.4, 16.5] | 8 of 860 |
| shape-probe | reference 0.45 | earlier | 98.6 | 87.6 [65.6, 111.3] | 48 of 548 |
| | | FULL | 98.0 | 95.4 [71.3, 120.9] | 52 of 545 |
| | | NO-ING | 98.0 | 88.1 [64.3, 112.1] | 48 of 545 |
| | strict | earlier (0.95) | 59.5 | 9.1 [0.0, 19.7] | 3 of 331 |
| | | FULL (0.90) | 67.8 | 18.6 [5.4, 33.5] | 7 of 377 |
| | | **NO-ING (0.95)** | 60.6 | 11.9 [2.8, 24.0] | 4 of 337 |
| | balanced | earlier (0.80) | 80.6 | 42.4 [25.2, 61.7] | 19 of 448 |
| | | FULL (0.85) | 74.1 | 26.7 [12.1, 42.3] | 11 of 412 |
| | | **NO-ING (0.80)** | 77.5 | 37.1 [19.0, 55.8] | 16 of 431 |
| challenge-set | strict and balanced | earlier, FULL, NO-ING | 69.8 to 84.9, 76.7 to 77.9, 70.9 to 80.2 | 0.0 | 0 of 60 to 73, 0 of 66 and 67, 0 of 61 to 69 |
| ud-test-mapped | reference 0.45 | earlier | 99.3 | 16.7 [12.7, 21.0] | 73 of 4,382 |
| | | FULL | 99.4 | 18.9 [14.8, 23.6] | 83 of 4,384 |
| | | NO-ING | 99.5 | 18.0 [14.1, 22.6] | 79 of 4,389 |
| | strict | earlier (0.95) | 76.9 | 2.9 [1.3, 5.0] | 10 of 3,395 |
| | | FULL (0.90) | 84.1 | 5.1 [2.8, 7.8] | 19 of 3,708 |
| | | **NO-ING (0.95)** | 79.3 | 2.6 [1.1, 4.4] | 9 of 3,497 |
| | balanced | earlier (0.80) | 89.2 | 7.4 [4.8, 10.2] | 29 of 3,936 |
| | | FULL (0.85) | 87.4 | 5.7 [3.5, 8.6] | 22 of 3,855 |
| | | **NO-ING (0.80)** | 89.8 | 6.6 [4.1, 9.5] | 26 of 3,961 |
| ud-test-raw | strict | earlier (0.95) | 77.9 | 3.2 [1.5, 5.4] | 10 of 3,125 |
| | | FULL (0.90) | 84.7 | 5.6 [3.1, 8.5] | 19 of 3,395 |
| | | **NO-ING (0.95)** | 80.2 | 2.8 [1.2, 4.8] | 9 of 3,214 |
| | balanced | earlier (0.80) | 89.6 | 8.1 [5.2, 11.3] | 29 of 3,593 |
| | | FULL (0.85) | 87.8 | 6.2 [3.8, 9.4] | 22 of 3,520 |
| | | **NO-ING (0.80)** | 90.0 | 7.2 [4.5, 10.6] | 26 of 3,607 |

The strict target (at most 5.0 harmful per 1,000 answered) was met on development data (3.1) and on the **open test split (3.5,
interval [1.6, 6.0])** and on UD-EWT (2.6, 2.8 raw); it is **exceeded on gold v2 test (6.5, 5 errors of 769 answered, interval
[1.4, 13.3] contains 5.0)** and on the shape probe (11.9, a deliberately hard set). The balanced target (10.0) was met on every
set except the shape probe (37.1; the earlier model 42.4).

Shape probe by shape (harmful errors at 0.45; earlier, FULL, NO-ING): `n-obj-light` 1, 1, 1; `n-compound-head` 16, 17, **18**
(accuracy 76.25, 73.75, 73.75); `n-modifier` 0, 0, 0; `n-subject-initial` 12, 12, 10; `n-measure` 6, 6, 5; `v-imperative-initial`
12, 14, 12; `v-after-modal` 1, 2, 2. Noun-verb confusion on open test (verb as noun / noun as verb): earlier 59 / 32, FULL 60 / 32,
NO-ING 53 / 29 (n/c); on UD mapped 41 / 43 (FULL 45 / 41; earlier 36 / 43); on gold v2 17 / 11 (FULL 15 / 14; earlier 14 / 10).
The throughput of the new report (894,415 tokens per second, one thread) is a timing of the machine; the earliest record (315,213)
was measured on a busy machine and the three are not a comparison of the models.

**Reading, with what is worse stated first.**

- **Worse than the earlier model on the fair comparison sets (where harmful errors exist).** NO-ING's harmful noun-verb errors
  per 1,000 decisions at 0.45 are **higher than the earlier model's on gold v2 test (24.6 to 29.9; 23 to 28 errors)** and on
  **UD-EWT (mapped 16.5 to 17.9, 73 to 79; raw 18.0 to 19.7, 72 to 79)**, and **equal on the shape probe (86.3 and 48 errors)**;
  the challenge set has none in all three. Gold v2 accuracy (94.57 to 94.37), ambiguous accuracy, ECE (1.82 to 2.49) and NLL
  (0.2178 to 0.2497) are also worse than the earlier model's, the shape probe ECE is worse (2.38 to 3.53) and its
  `n-compound-head` errors are 18 against 16, and the strict point is worse than the earlier model's at the same 0.95 on gold
  v2 (6.5 against 3.9) and on the shape probe (11.9 against 9.1). Every old and new interval overlaps (small sets), so no
  single set proves a regression, but the direction is the same on all of them. So NO-ING **does not return to the earlier
  model's harmful rate**: it recovers part of FULL's rise.
- **Better than FULL on the same sets, in point estimates.** Harmful per 1,000 at 0.45: gold v2 31.0 to 29.9 (29 to 28), UD
  mapped 18.8 to 17.9 (83 to 79), UD raw 20.5 to 19.7 (82 to 79), shape probe 93.5 to 86.3 (52 to 48, the earlier model's figure);
  the shape-probe accuracy rises from 87.50 to 89.64 (the earlier 89.11) and the challenge set accuracy from 87.50 to 88.60. The
  intervals of FULL and NO-ING overlap almost completely and the differences are 1 to 4 errors per set, far inside what the
  sampling interval of a single record covers; the direction agrees with the dev selection (UD dev 23.4 to 22.5, open dev 23.1
  to 20.1) and with nothing stronger. It is **not** evidence that the `prenominal-ing` corrections were wrong.
- **Worse than FULL.** UD agreement is 0.12 points lower (mapped 92.97 to 92.85; raw 89.05 to 88.92), its ECE higher (2.06 to
  2.22 mapped; gold v2 2.23 to 2.49) and its NLL higher (0.2982 to 0.3025 mapped; gold v2 and the raw view too), the shape probe
  ECE is much higher (1.99 to 3.53) and `n-compound-head` has one more harmful error (17 to 18). The development cost found by the
  follow-up (verbal `-ing` VERB accuracy, compound-head NOUN accuracy) is a dev measure over frozen items and has no held-out
  counterpart here; the held-out sets are not designed to show it.
- **Better than the earlier model.** UD-EWT agreement (mapped 92.30 to 92.85, raw 88.40 to 88.92; ambiguous 91.71 to 92.43), with
  a lower ECE (2.36 to 2.22) and NLL (0.3175 to 0.3025) on the mapped view; shape probe accuracy (89.11 to 89.64) and NLL (0.2766 to
  0.2704); the balanced point on every set is lower (open test 9.3 to 7.3, gold v2 10.4 to 9.3, UD mapped 7.4 to 6.6, shape
  probe 42.4 to 37.1) and the strict point on open test and UD (4.1 to 3.5; 2.9 to 2.6). Open test accuracy, harmful rate, NLL
  and ECE are better
  (96.10 to 96.53; 20.6 to 18.7; 0.1394 to 0.1161; 0.28 to 0.20) but **not comparable** (labels changed twice).
- **Open test against FULL.** 91 to 82 harmful errors and 96.46 to 96.53 accuracy; 17 test labels differ between the two scorings
  (the withdrawn rows), so this difference mixes the model and the labels and is not a model comparison.
- **The thin primary evidence.** The adoption rests on a pooled dev difference of 0.974 per 1,000 (five seeds; the Welch interval
  excludes 0, the bootstrap interval contains it). The held-out figures do not add independent confirmation of an improvement
  over FULL: they are consistent with a small effect and equally with none; they do not show an improvement over the earlier
  model on the harmful measure.
- **What is not shown.** The held-out evaluation did not tune anything and no test figure was used to change a threshold, the
  model or a setting. The three stop conditions fired again as in both earlier records and are findings, not tuned.
  Any further held-out evaluation would be a third look and is not planned.

## Shapes development set: baseline of the shipped model

The shapes development set (`data/eval/shapes-dev/dev.tsv`, [README](../../data/eval/shapes-dev/README.md)) is the development
set for the context-feature work: sentence-level context features, a two-pass decoder and word clusters. This section is the
dev baseline of the shipped model for that work: the figures below are what every variant is compared with. They are
development figures only; the set is for selecting settings, nothing is trained on it, and none of them is a held-out result
(the shape probe stays the held-out measurement).

Command (run once from the repository root, below normal priority; it reads only the development file and the embedded
weights and appends no row to the run table):

```text
dotnet run -c Release --project tools/DemaConsulting.PartOfSpeech.Trainer -- eval-shapes --label "shipped model baseline"
```

| Input | SHA-256 |
| --- | --- |
| `data/eval/shapes-dev/dev.tsv` | `bbc94e42693d92a7e948598d5ea8a2c58274d3e4e5e6e4d0ed7d8a5c10963290` |
| `src/DemaConsulting.PartOfSpeech/Resources/weights.bin` (the file) | `439d4af5329b44d3a226b823b20b3afa54dab0ef8e7247f1333135bb96a14092` |
| shipped weights, uncompressed image (`imageSha256` of `training.json`) | `a0d45458308f78dbe74669e563ea236a0a7b4688638d690dd0995ac4de988da3` |

Output of the command (the heading level is lowered by one):

### Shapes development set: shipped model baseline

- Items: 600 target items and 100 control items; authored: 0 target items (0.0%) and 0 control items (0.0%).
- Development file SHA-256: `bbc94e42693d92a7e948598d5ea8a2c58274d3e4e5e6e4d0ed7d8a5c10963290`.
- Weights file SHA-256: `439d4af5329b44d3a226b823b20b3afa54dab0ef8e7247f1333135bb96a14092`.
- Scoring: the judged word of every item through the real decoder with all alternatives; a noun-verb decision is a target whose candidates include both classes; a harmful error is a swapped class with a confidence of at least 0.45.
- The set is for development: it selects settings and is never a held-out figure.

| shape | target items | target accuracy | 95% interval | noun-verb decisions | harmful errors | harmful per 1,000 decisions | control items | control accuracy |
| --- | --- | --- | --- | --- | --- | --- | --- | --- |
| n-compound-head | 120 | 93.3% | [87.4%, 96.6%] | 120 | 6 | 50.0 | 13 | 92.3% |
| n-subject-initial | 120 | 95.8% | [90.6%, 98.2%] | 120 | 3 | 25.0 | 12 | 75.0% |
| v-imperative-initial | 120 | 95.0% | [89.5%, 97.7%] | 120 | 6 | 50.0 | 31 | 100.0% |
| n-measure | 120 | 99.2% | [95.4%, 99.9%] | 120 | 0 | 0.0 | 14 | 100.0% |
| v-after-modal | 120 | 98.3% | [94.1%, 99.5%] | 120 | 2 | 16.7 | 30 | 100.0% |
| all shapes | 600 | 96.3% | [94.5%, 97.6%] | 600 | 17 | 28.3 | 100 | 96.0% |

The set was rebuilt without the two NIST handbooks (the project owner dropped them; see the
[README](../../data/eval/shapes-dev/README.md#excluded-sources)) and this baseline was measured again on the rebuilt file. The
baseline on the earlier file (which contained them) gave the same target figures; only the control items differ.

### Headroom

Headroom is what is left to gain on the set: 100% minus the target accuracy, and the same as a count of target errors. The
counts are the number of target items times the headroom (a figure derived from the table above, not a new measurement); the
harmful errors and the control errors are as above.

| shape | target items | headroom (100% minus target accuracy) | target errors | harmful errors | control items | control errors |
| --- | --- | --- | --- | --- | --- | --- |
| n-compound-head | 120 | 6.7% | 8 | 6 | 13 | 1 |
| n-subject-initial | 120 | 4.2% | 5 | 3 | 12 | 3 |
| v-imperative-initial | 120 | 5.0% | 6 | 6 | 31 | 0 |
| n-measure | 120 | 0.8% | 1 | 0 | 14 | 0 |
| v-after-modal | 120 | 1.7% | 2 | 2 | 30 | 0 |
| all shapes | 600 | 3.7% | 22 | 17 | 100 | 4 |

The shipped model scores higher on this set than on the held-out shape probe: 96.3% target accuracy here against 89.64%
on the probe (the figure in the [evaluation record](../../docs/evaluation/README.md), section "Earlier Models", column "Accuracy embedded"). So the room to show
a gain is small: a variant can win at most 22 target errors here (8 for `n-compound-head`, 6 for `v-imperative-initial`, 5 for
`n-subject-initial`, 2 for `v-after-modal`, and 1 for `n-measure`, which is saturated), and a difference of a few items is
inside the 95% intervals above. The set was intentionally **not** made harder: choosing items by the errors of this model
would bias the set against that model (and against any variant that shares its errors), so the comparison would no longer be
fair. A harder tranche that is chosen by model-independent criteria is built next; its selection rule is pre-registered in the
section below.

## Shapes development set: hard tranche, pre-registered selection rule

**The pre-registered selection rule (the inputs table and the numbered rule below) was written before any label of the hard
tranche existed and before its candidates were generated.** The frozen pre-registration is PR #11 commit `7a1aa4d`, amended once by PR #11
commit `7e3ecb0` (the `n-subject-initial` cap, rule 9, before labeling). Commits of a PR are retrievable from the PR's head ref
(`refs/pull/<n>/head`) after a squash merge: the PR #11 commits come from `git fetch origin pull/11/head` (the PR #10 commits from `pull/10/head`). The sections "Rounds actually run" and the baseline sections
after it were added after labeling; they do not change the rule. The hard tranche
(`data/eval/shapes-dev/hard.tsv`, [README](../../data/eval/shapes-dev/README.md#hard-tranche) has the same rule) holds the
contested cases of `n-compound-head`, `n-subject-initial`, `v-imperative-initial` and `v-after-modal` (`n-measure` has no hard
cases and stays at zero items). It is a development set: the figures that it produces select settings and are never held-out
results. The rule is model-independent: no PartOfSpeech weight file, prediction, error or score takes part in the selection.

| Input | Value |
| --- | --- |
| Text | 123 federal documents, 60,297 sentences (63 documents of the standard tranche without the two excluded NIST handbooks, plus 60 newly sourced documents) |
| Selection signal | the WordSense Basic classifier (HMM tagger plus cue model), another project's model family |
| WordSense commit | `6530de1928baf30f9a14446a54bfb5dd5f67b41c` (read only) |
| `DemaConsulting.WordSense.dll` SHA-256 | `b96e0055664896983e311f626d41e5251a2dcf1a236d86f6ec50beaae6a746bf` |
| `hmm.tsv` SHA-256 (noun and verb mass of every token) | `3014efd2044702c82c02c3557bb47f56ed985537f0d3154393455b110b176a8c` |
| `tokens.tsv` SHA-256 (harness input) | `197d68f161bb1e05357caa74ed0a53a85c38a675e491c82f2365450b9294c328` |

Rule (`python tools/data/shapes_dev.py --tranche hard`):

1. **Inputs:** real text, library tokens, the lexicon, the committed `dev.tsv` (exclusion only) and `hmm.tsv`.
2. **Candidate filters:** unchanged (cleanliness, length 3 to 30, duplicates, the held-out filter only through
   `leakage.held_out_overlap_keys`, excluded sources, document key hits), plus: not a standard-tranche sentence (exact key, or
   a sentence of at least 10 words with 80% or more of its word 6-grams in the standard sentences of the same document).
3. **Frame:** the unchanged `shape_hits` frame (`n-compound-head` strict); the lexicon ambiguity (noun and verb listed, not a
   function word) is part of it.
4. **Contested band:** q is the mass of the shape's class (Noun for `n-*`, Verb for `v-*`) that the Basic classifier gives the
   target. Eligible if 0.10 <= q < 0.97; q < 0.5 is the pure-disagreement part and is reported. Order by q ascending, ties by the
   seeded rank, at most 4 candidates per lemma. Budgets: `n-compound-head` 400, `v-after-modal` 300, `v-imperative-initial` and
   `n-subject-initial` all; a round `h2` takes the next block only for a shape below its floor.
5. **Why a band and not pure disagreement:** on the 1,825 labeled standard candidates, where the Basic classifier disagrees
   with the shape's class, the two blind labelers agree with the shape's class in only 1% to 21% of the cases
   (`n-compound-head` 8%, `n-subject-initial` 1%, `v-imperative-initial` 5%, `v-after-modal` 21%). Very confident
   disagreements are mostly not true instances of the shape, and filling a shape from them would need about 1,500 labeled
   sentences per shape; the real pools with q < 0.5 are 668, 275, 220 and 189 (same order). Disagreement (q < 0.5) is inside the
   band, very confident disagreement (q < 0.10) is outside it. Band and budgets were chosen after looking at how the standard
   tranche's labels distribute over the Basic classifier's q (yield only), never at a PartOfSpeech result, and are fixed before
   any label of this tranche exists (project owner's decision).
6. **Controls:** real other-class sentences of the lemmas of the chosen targets (hard or not, no band), at most 2 per lemma,
   50 candidates per shape, labeled blind.
7. **Gold:** unchanged: two blind labelers (`claude-opus-5.5`, `gpt-6.1-sol`); target disagreement and class mismatch are
   dropped; frame, base-form and nominal-gerund rules as in the standard tranche; q and the classifier's answer are never shown to a
   labeler.
8. **No authored sentences;** `n-measure` is omitted (zero items, `-` in reports).
9. **Quota gate** (floor and cap): `n-compound-head` 80 and 100, `v-imperative-initial` 70 and 100, `v-after-modal` 80 and 100,
   `n-subject-initial` 40 and 100, `n-measure` 0 and 0; controls 50 and 60; lemma cap 4 (controls 2). Below a floor `build`
   refuses unless `--allow-short`; a miss is disclosed, not padded. Amendment before any label existed: the
   `n-subject-initial` cap was raised from 60 to 100 after the new documents enlarged the pool (846 candidates against the
   254 expected); written before labeling started.
10. **No PartOfSpeech model in the selection** (a data test asserts it); voters (spaCy, NLTK) are not part of the rule and may
    only be descriptive diagnostics after the build.
11. **Hand check:** 60 targets (15 per shape, at least half with q < 0.5) by `claude-sonnet-5.5` (a model review), with a
    leniency control; gate 95% precision.
12. **Leftovers are not reused:** labeled standard-tranche candidates that are not in `dev.tsv` are not used, because their
    labels were seen.

### Rounds actually run

The main round labeled all 2,258 candidate sentences. Round `h2` (the next 200 `v-after-modal` candidates) ran because that
shape kept 77 targets, below its floor of 80. Round `h3` (a controls-only round, 149 sentences) ran because the controls were
45, below the floor of 50; the pre-registration did not name it, it applies rule 6 and the controls floor, adds no target, and was
exported before any `h3` label existed. Both blind labelers tagged every sentence; the hand check passed the 95% gate. The
outcome (counts, drops, q bands, documents, hand check, limits) is in the
[README](../../data/eval/shapes-dev/README.md#hard-tranche); the baseline is in the next section.

## Shapes development set: hard tranche baseline of the shipped model

The built hard tranche (`data/eval/shapes-dev/hard.tsv`, 400 target items and 60 controls, 0 authored; `n-measure` has zero
items) was scored once with the shipped model after the build. These are development figures: they select settings, nothing is
trained on the file and none is a held-out result. The standard-tranche figures in the section above are unchanged: the standard
part of this run's output equals that baseline (same file hash, same table).

Command (run once from the repository root, below normal priority; it reads only the two development files and the embedded
weights and appends no row to the run table):

```text
dotnet run --no-build -c Release --project tools/DemaConsulting.PartOfSpeech.Trainer -- eval-shapes --tranche both --label "shipped model baseline"
```

| Input | SHA-256 |
| --- | --- |
| `data/eval/shapes-dev/hard.tsv` | `c041ae3d8b4b948326d002176bdffa0e5eb2ac0a6fe7d12daed7c3d289db8b22` |
| `data/eval/shapes-dev/dev.tsv` | `bbc94e42693d92a7e948598d5ea8a2c58274d3e4e5e6e4d0ed7d8a5c10963290` |
| shipped weights, uncompressed image (`imageSha256` of `training.json`) | `a0d45458308f78dbe74669e563ea236a0a7b4688638d690dd0995ac4de988da3` |

Hard tranche (output of the command, the heading level is lowered by one):

| shape | target items | target accuracy | 95% interval | noun-verb decisions | harmful errors | harmful per 1,000 decisions | control items | control accuracy |
| --- | --- | --- | --- | --- | --- | --- | --- | --- |
| n-compound-head | 100 | 68.0% | [58.3%, 76.3%] | 100 | 28 | 280.0 | 15 | 100.0% |
| n-subject-initial | 100 | 73.0% | [63.6%, 80.7%] | 100 | 16 | 160.0 | 15 | 93.3% |
| v-imperative-initial | 100 | 45.0% | [35.6%, 54.8%] | 100 | 44 | 440.0 | 15 | 93.3% |
| n-measure | 0 | - | - | 0 | 0 | - | 0 | - |
| v-after-modal | 100 | 78.0% | [68.9%, 85.0%] | 100 | 19 | 190.0 | 15 | 100.0% |
| all shapes | 400 | 66.0% | [61.2%, 70.5%] | 400 | 107 | 267.5 | 60 | 96.7% |

Both tranches combined (standard `dev.tsv` plus `hard.tsv`):

| shape | target items | target accuracy | 95% interval | noun-verb decisions | harmful errors | harmful per 1,000 decisions | control items | control accuracy |
| --- | --- | --- | --- | --- | --- | --- | --- | --- |
| n-compound-head | 220 | 81.8% | [76.2%, 86.4%] | 220 | 34 | 154.5 | 28 | 96.4% |
| n-subject-initial | 220 | 85.5% | [80.2%, 89.5%] | 220 | 19 | 86.4 | 27 | 85.2% |
| v-imperative-initial | 220 | 72.3% | [66.0%, 77.8%] | 220 | 50 | 227.3 | 46 | 97.8% |
| n-measure | 120 | 99.2% | [95.4%, 99.9%] | 120 | 0 | 0.0 | 14 | 100.0% |
| v-after-modal | 220 | 89.1% | [84.3%, 92.6%] | 220 | 21 | 95.5 | 45 | 100.0% |
| all shapes | 1000 | 84.2% | [81.8%, 86.3%] | 1000 | 124 | 124.0 | 160 | 96.3% |

### Headroom of the hard tranche

Headroom is 100% minus the target accuracy (derived from the hard table above, not a new measurement): 32.0% for
`n-compound-head`, 27.0% for `n-subject-initial`, 55.0% for `v-imperative-initial` and 22.0% for `v-after-modal`; 34.0% over
the four shapes (136 of 400 target errors), against 3.7% on the standard tranche. The 95% intervals of a shape are 16 to 19
points wide, so a difference of a few items between variants is inside the interval.

## Context-feature experiment: pre-registration

This section is written and committed **before any candidate model of the experiment is trained or evaluated**; the git
history shows the order: this commit (plan, harness, analysis script, rule), then the feature code, then the arm runs, then a
results section. Only the shipped model (the BASE arm) has been run: the baselines below. The harness is in
`tools/DemaConsulting.PartOfSpeech.Trainer/context-features/` (`arms.json`, `run_arms.ps1`, `analyze_arms.py`) and is frozen by
the hashes at the end; the SHA of this commit is recorded by the commit that follows it (a commit cannot contain its own SHA).

### Goal

Test whether sentence-level context features, added to the shared feature extractor behind switches, lower the harmful
noun-verb errors of the tagger on the hard tranche of the shapes development set without costing the other development
measures, size or speed. The hard tranche holds the contested cases of four shapes (`n-compound-head`, `n-subject-initial`,
`v-imperative-initial`, `v-after-modal`) where the shipped model is weakest. Nothing is adopted unless the pre-registered rule
below selects it.

### Baselines (shipped model, seed 20260101; development data only)

| Measure | Value |
| --- | --- |
| Hard tranche, all shapes (400 target items) | 66.0% target accuracy, 107 harmful, **267.5 harmful per 1k** |
| `v-imperative-initial` | 45.0%, 440.0 per 1k |
| `n-compound-head` | 68.0%, 280.0 per 1k |
| `n-subject-initial` | 73.0%, 160.0 per 1k |
| `v-after-modal` | 78.0%, 190.0 per 1k |
| Standard tranche, all shapes (600 target items) | 96.3% target accuracy, 17 harmful, 28.3 per 1k |
| Open dev (the adopted model's own labels) | 96.38% accuracy, 90 harmful of 4,451 decisions = 20.2 per 1k |
| UD-EWT dev (mapped view, 25,148 tokens) | 92.71% agreement, 103 harmful of 4,570 decisions = 22.54 per 1k |
| Gold v2 dev (recorded, not gated) | 95.24% accuracy, 11 harmful of 611 = 18.0 per 1k |
| Weights | 478,835 bytes deflated, 1,080,650 raw, 116,662 rows |

These are the sections above (the open dev and UD-EWT dev figures are those of the adoption section). The experiment compares
arms by their own five-seed means: the BASE arm re-measures every figure at five seeds, and the guards below are relative to
that BASE mean, not to the single-seed figures in this table. Single-thread throughput of the default tagger was recorded at
315k to 894k tokens per second depending on machine load.

### Feature groups

The groups extend the 22 templates of the shipped extractor. Each is a switch (`FeatureGroups`, stored as a mask in the weights
header; mask 0 is the shipped format). New feature kinds are appended, so BASE hashes do not move. The definitions below are
binding; buffer sizes, hash seeds and similar details are not. A lexicon lookup is the one that the decoder already does (the
cached tag set of a token); tokens not in the lexicon have an empty set.

**Token class (used by F1 and F2).** First match in this order, from the lexicon tag set T of the token and its lower-cased form:
`Punct` (no letter or digit); `Modal` (one of can, could, may, might, must, shall, should, will, would, cannot); `To` (`to`);
`Num` (all digits, or NUM in T); `Det` (DET in T and none of NOUN, VERB, ADJ); `Pron` (PRON in T and none of NOUN, VERB, ADJ);
`Adp` (ADP in T and none of NOUN, VERB, ADJ); `Conj` (CCONJ or SCONJ in T and none of NOUN, VERB, ADJ); `NounVerb` (NOUN and
VERB in T); `NounOnly` (NOUN, no VERB); `VerbOnly` (VERB, no NOUN); `AdjAdv` (ADJ or ADV in T); `Other` (everything else,
including unknown spellings). A token is **function-only** when T is not empty and has only DET, PRON, ADP, CCONJ, SCONJ, PART,
AUX, PUNCT or SYM tags.

- **F1 `positionCues`** (5 static features). `PosIdx` = min(i, 3). `FirstContent`: the index of the first token that is not
  function-only (unknown spellings count as content), capped at 2, combined with whether the token is that token. `EndPos`:
  the sentence terminal (`.`, `!`, `?`, `:`, other or none; a final closing quote or bracket is skipped) combined with `PosIdx`.
  `LenBucket`: sentence length in tokens, buckets 1-3, 4-6, 7-10, 11-15, 16-25, 26+. `LenPos` = `LenBucket` x `PosIdx`.
- **F2 `clauseCues`** (7 features). Static: `PrevClass1` and `NextClass1` (token class of i-1 and i+1, with a boundary marker),
  `PrevNextClass` (the pair), `DetDist` (distance 1, 2, 3 or none to the nearest preceding Det, possessive or Num token within 3
  tokens), `ModalDist` (distance 1, 2 or none to the nearest preceding Modal or To token within 2 tokens). History:
  `ClauseState` (two bits from the predicted tags since the last clause boundary: a subject-like tag, DET, PRON, NOUN, PROPN or
  NUM, was seen; a verb tag, VERB or AUX, was seen; a punctuation token among `, ; : ( --` or a predicted SCONJ or CCONJ resets
  both bits) and `ClauseStatePrev` (`ClauseState` x the previous tag).
- **F3 `rightPreview`** (4 features; absent in pass 1). A first greedy pass without F3 gives preview tags; the decoder then
  decodes again with `Rt1` (preview tag of i+1), `Rt2` (preview tag of i+2), `RtPair` (both) and `RtPrev` (the previous tag of
  this pass x `Rt1`); an end-of-sentence value is used past the end. Training uses **jackknife** preview tags: K = 5 folds by
  hash of the document id (a document is never split, the same folds for every seed), fold models trained without F3 on the
  arm's other groups with the shipped training settings and the run seed, decoding their held-out fold exactly as the runtime
  pass 1 does. One weight table is trained on both modes (pass 1 without F3, pass 2 with the jackknife tags). Known and accepted
  mismatch: the runtime pass 1 uses the final model, the training preview tags come from the fold models.
- **F4 `inflection`** (2 features, only for tokens ending in `ing`, `ed`, `s`, `es` or `ies`). Stem candidates (at most 3
  lexicon lookups, minimum stem length 2): `ing`: stem, stem + `e`, the undoubled final consonant; `ed`: the same and `ied` to
  `y`; `s`: drop `s` (not after `ss`), drop `es`, `ies` to `y`. `StemKind` = suffix kind x whether some candidate has VERB in
  the lexicon x whether some candidate has NOUN; `StemKindPrev` = `StemKind` x the previous tag.

### F5 (word clusters): skipped

F5 is not built. It needs a new embedded resource, a new binary format with loader and tests, and a license review of the
derived data, and the only public-domain text available is about 0.5 million tokens, too little to add information beyond the
lexicon. The mask bit stays reserved and the loader rejects it.

### Arms and runs

Every arm uses the shipped `training.json` settings (sources open x2, MASC, silver >= 80, restriction `history`,
predicted history, 6 epochs, 19 hash bits, minimum count 1, pruning 8, scale 8); only the groups and the seed differ, and the
temperature is refit on open dev for every run (`--calibrate`) as for the shipped model. Seeds: **20260101, 11, 12, 13, 14**
(five per arm). **K = 5** jackknife folds for F3. Ten arms, 50 runs, run one at a time by `run_arms.ps1`:

| Arm | Groups |
| --- | --- |
| BASE | none (the shipped feature set) |
| F1, F2, F3, F4 | each alone |
| ALL | F1 + F2 + F3 + F4 |
| ALL-minus-F1, -F2, -F3, -F4 | ALL without the named group |

The driver trains with `train --seed S --features <groups|none> [--preview-folds 5] --calibrate --output <file>`, then scores
the file with the dev-only command `eval-arm`, which writes one JSON file per run (the interface contract and the schema are at
the top of `run_arms.ps1`). It runs below normal priority with at most 8 runtime threads, requires at least 6 GB free on C:,
and resumes after an interruption: a run is complete when its JSON exists. A run that fails without a result is repeated; no
run is repeated or replaced because of its result, and no arm is dropped.

### Metrics and the selection rule (fixed now; applied exactly as written; nothing is tuned afterwards)

A harmful error is a swapped noun-verb class at confidence >= 0.45 on a noun-verb decision; rates are per 1,000 decisions
(the definitions of E0 and of the shapes development set). All figures come from `analyze_arms.py` over the five seeds
of every arm; spreads are sample standard deviations.

- **Primary metric:** the mean over the five seeds of the pooled harmful per 1k on the hard tranche of the shapes development
  set (four shapes, 400 target items). Lower is better.
- **Guards** (arm against the BASE mean; all must hold): shapes-dev standard tranche harmful per 1k <= BASE + 5; open dev
  harmful per 1k <= BASE + 1.5; UD-EWT dev harmful per 1k <= BASE + 1.5; UD-EWT dev accuracy >= BASE - 0.15 points; deflated
  weights file size <= 2 x BASE; single-thread throughput >= 150,000 tokens per second (absolute; the median over the seeds).
- **Rule.** Q = the arms other than BASE that pass every guard. If Q is empty, BASE stays. Otherwise m = the lowest primary in
  Q, S = the arms of Q with primary <= 1.10 x m (within 10% relative), and the candidate is the arm of S with the fewest feature
  groups (ties: the lowest primary; a remaining tie: the order of `arms.json`). The candidate ships only if the Welch 95%
  t-interval (with the Welch degrees-of-freedom approximation) of its primary minus BASE lies **entirely below 0**; otherwise BASE stays
  and nothing ships. There is no fallback to another arm.
- **Reported, not gated:** hard and standard target and control accuracy, open dev and gold v2 dev accuracy, NLL and ECE,
  temperature, rows, raw bytes, training seconds, the per-shape hard breakdown (mean and sd of accuracy and harmful per 1k),
  and a paired bootstrap over the hard items of the candidate against BASE (2,000 resamples, seed 20261107, descriptive only).
- **Acceptance test before the rule is applied.** The BASE arm at seed 20260101 must reproduce the baselines above (open dev
  90 of 4,451, UD-EWT dev 103 of 4,570, hard 267.5 per 1k, standard 28.3 per 1k) and its weights must be byte-identical to the
  shipped `weights.bin`; if not, the experiment stops without a selection.

**BASE must be byte-identical with all switches off.** With every group off (no `--features` option, or `--features none`), the
code of the feature work must give weights that are byte-identical to the shipped file (`train --check` exit 0, image
`a0d45458...8da3`), and all existing tests must pass unchanged. A failure of this gate stops the work before any arm is run.

### Limits stated in advance

- Nine candidate arms are compared with a seed-only interval: it measures training noise, not the sampling of the 400 hard items
  (their item-level interval is far wider; the bootstrap above shows it), and the lowest of nine is subject to selection bias.
  The rule is applied as written regardless, and a win is described as development evidence of that kind.
- The shapes development set has been used for baselines before this experiment; the groups were designed knowing which
  shapes are weak, so the set is a development set for this selection and not an independent test of the idea.
- F1 uses sentence-end and sentence-length cues that may not carry over to fragments (headings, list items) of real text; the
  development sets contain few such inputs.

### Held-out sets

The held-out sets (open test, gold v2 test, shape probe, STE challenge set, UD-EWT test) are **never used for selection** and
are not read by this experiment: the harness reads only the development files, and `eval-arm` has no path to a held-out file.
The adoption section above says that a further held-out evaluation "would be a third look and is not planned". That sentence is
the state when it was written and stays as it is. This experiment is a new work item that the project owner authorized, and it
changes it only conditionally: **if and only if a winner qualifies under the rule**, one `evaluate` of the new model, with the
committed record of the current model kept for a side-by-side comparison, is run once, and it is a **disclosed third look** at
sets already seen for this model family (a new record, not an erratum, labeled as such in the disclosure and the evaluation
README). If BASE stays, no held-out command is run and that sentence remains true.

### Inputs frozen by this commit

SHA-256 of the files as committed (line endings LF) and of the data they read:

| File | SHA-256 |
| --- | --- |
| `context-features/analyze_arms.py` | `3d52825384b440c5786e1b241fc6f814e4e47a63f155414e0d6450107d33e494` |
| `context-features/run_arms.ps1` | `26ba1c6ed281bdc86ac88522aaab76fd6490f10a753c5aa1dae96d83ff5995db` |
| `context-features/arms.json` | `0aac43677f11b3cbfa37a020344b6276483edbf8af7eee840893a43520e9fad8` |
| `tools/data/cache/ud-ewt/ud_dev.tsv` | `0fc0b4a371ef6d0437e01e396762cdb600d0d7ff0d725ead00ac9f3c41f82037` |
| `data/eval/shapes-dev/dev.tsv` (standard tranche) | `bbc94e42693d92a7e948598d5ea8a2c58274d3e4e5e6e4d0ed7d8a5c10963290` |
| `data/eval/shapes-dev/hard.tsv` | `c041ae3d8b4b948326d002176bdffa0e5eb2ac0a6fe7d12daed7c3d289db8b22` |
| `src/DemaConsulting.PartOfSpeech/Resources/weights.bin` (the file) | `439d4af5329b44d3a226b823b20b3afa54dab0ef8e7247f1333135bb96a14092` |

`analyze_arms.py --selftest` passes (the rule logic is tested on synthetic runs: the t quantile against published values, the
guard boundaries, the 10% window, the fewest-groups tie-break, ties, the no-fallback case, missing runs). The repository code
base of this experiment is `main` at `4709395`; the trainer commit that produced each result is recorded in its JSON.

## Context-feature experiment: results

All 50 runs of the pre-registration above are complete (10 arms, five seeds each: 20260101, 11, 12, 13, 14; 0 runs repeated or
skipped, no arm dropped). Every run was made by the frozen driver `run_arms.ps1` with the trainer of commit `a674d50` (clean
tree in all 50 result files) and scored by `eval-arm` on development data only. The tables below are the output of the
committed `analyze_arms.py` over the per-run result files, copied unchanged. The selection rule was applied mechanically; the
decision was not reconsidered. No held-out set was read by this experiment.

Outputs (kept outside the repository; the 50 per-run JSON files are not copied into it):

| File | SHA-256 |
| --- | --- |
| `report.md` (the analysis below) | `CF5450F11B2961EF930D631745EA49EC97BF9CB10501E913FABA99DE3146C2B4` |
| `decision.json` (per-arm values, intervals, guards, decision) | `777B6450DB23C0AFFB737E47257FAD667734964A9755A3FE65DFEC443797B756` |

**Run machine.** The runs shared a machine with other sessions that kept its cores saturated. The frozen driver starts
`dotnet` at BelowNormal priority, which received almost no CPU, so it was run with the driver's `-Runner` option and a
one-line wrapper (`start /normal /wait /b dotnet`) that starts it at Normal priority (the owner allowed this); the frozen
files were not edited. Training times therefore are not comparable between arms or with earlier records (the load varied
during the run); throughput was measured single-threaded on the same loaded machine and varies strongly (see the spreads).

### Analysis report (`analyze_arms.py`)

Values are mean ± sample sd over the five seeds of an arm.

#### Shapes development set

| arm | hard harmful per 1k (primary) | hard target acc % | hard control acc % | standard harmful per 1k | standard target acc % | standard control acc % |
| --- | --- | --- | --- | --- | --- | --- |
| BASE | 257.0 ± 8.2 | 67.20 ± 1.02 | 96.33 ± 0.75 | 26.7 ± 3.9 | 96.37 ± 0.22 | 97.20 ± 0.84 |
| F1 | 267.0 ± 10.2 | 65.75 ± 1.19 | 96.00 ± 1.49 | 23.0 ± 4.5 | 96.70 ± 0.27 | 97.80 ± 1.10 |
| F2 | 234.0 ± 11.3 | 69.80 ± 1.39 | 98.00 ± 0.75 | 16.0 ± 3.5 | 97.23 ± 0.43 | 98.80 ± 0.45 |
| F3 | 252.0 ± 7.4 | 67.25 ± 1.00 | 95.67 ± 0.91 | 26.7 ± 4.2 | 96.60 ± 0.38 | 96.60 ± 0.55 |
| F4 | 247.5 ± 5.3 | 67.95 ± 0.74 | 95.33 ± 0.75 | 28.0 ± 1.8 | 96.30 ± 0.25 | 96.00 ± 1.00 |
| ALL | 225.5 ± 11.2 | 70.40 ± 0.42 | 98.33 ± 0.00 | 13.3 ± 2.0 | 97.80 ± 0.14 | 99.00 ± 0.00 |
| ALL-minus-F1 | 229.0 ± 12.1 | 69.65 ± 1.46 | 98.33 ± 0.00 | 13.3 ± 1.2 | 97.73 ± 0.19 | 98.60 ± 0.55 |
| ALL-minus-F2 | 248.0 ± 4.8 | 68.30 ± 1.05 | 96.67 ± 1.18 | 22.0 ± 5.5 | 96.60 ± 0.51 | 98.00 ± 1.22 |
| ALL-minus-F3 | 232.5 ± 12.5 | 69.95 ± 1.16 | 98.33 ± 0.00 | 12.7 ± 1.5 | 97.77 ± 0.38 | 99.00 ± 0.00 |
| ALL-minus-F4 | 242.0 ± 15.0 | 68.20 ± 1.45 | 98.33 ± 0.00 | 12.3 ± 1.9 | 97.90 ± 0.22 | 98.60 ± 0.55 |

#### Open dev and gold v2 dev

| arm | open acc % | open ambiguous acc % | open harmful per 1k | open NLL | open ECE % | gold acc % | gold harmful per 1k | gold NLL | gold ECE % |
| --- | --- | --- | --- | --- | --- | --- | --- | --- | --- |
| BASE | 96.36 ± 0.03 | 95.65 ± 0.04 | 21.30 ± 1.03 | 0.1217 ± 0.0009 | 0.25 ± 0.09 | 95.08 ± 0.21 | 20.9 ± 3.1 | 0.2582 ± 0.0020 | 2.13 ± 0.13 |
| F1 | 96.36 ± 0.02 | 95.66 ± 0.05 | 20.62 ± 0.75 | 0.1196 ± 0.0006 | 0.23 ± 0.05 | 94.98 ± 0.11 | 21.3 ± 4.0 | 0.2562 ± 0.0031 | 2.05 ± 0.23 |
| F2 | 96.64 ± 0.05 | 95.98 ± 0.05 | 20.67 ± 1.02 | 0.1123 ± 0.0012 | 0.26 ± 0.02 | 94.98 ± 0.13 | 23.2 ± 2.1 | 0.2571 ± 0.0031 | 2.36 ± 0.09 |
| F3 | 96.60 ± 0.05 | 95.99 ± 0.08 | 21.03 ± 0.59 | 0.1161 ± 0.0009 | 0.22 ± 0.05 | 95.14 ± 0.16 | 19.3 ± 0.7 | 0.2595 ± 0.0033 | 2.39 ± 0.27 |
| F4 | 96.38 ± 0.02 | 95.69 ± 0.03 | 20.35 ± 0.72 | 0.1224 ± 0.0009 | 0.32 ± 0.12 | 95.13 ± 0.21 | 18.7 ± 4.1 | 0.2596 ± 0.0019 | 2.23 ± 0.11 |
| ALL | 96.82 ± 0.03 | 96.24 ± 0.04 | 18.60 ± 0.40 | 0.1084 ± 0.0004 | 0.22 ± 0.04 | 95.27 ± 0.11 | 19.3 ± 2.4 | 0.2545 ± 0.0021 | 2.22 ± 0.14 |
| ALL-minus-F1 | 96.81 ± 0.06 | 96.21 ± 0.06 | 19.32 ± 1.04 | 0.1088 ± 0.0010 | 0.28 ± 0.04 | 95.06 ± 0.18 | 22.6 ± 3.5 | 0.2569 ± 0.0022 | 2.22 ± 0.14 |
| ALL-minus-F2 | 96.74 ± 0.04 | 96.20 ± 0.04 | 19.19 ± 0.76 | 0.1134 ± 0.0004 | 0.24 ± 0.05 | 95.24 ± 0.06 | 19.6 ± 2.0 | 0.2563 ± 0.0041 | 2.21 ± 0.19 |
| ALL-minus-F3 | 96.67 ± 0.03 | 96.02 ± 0.06 | 19.46 ± 0.91 | 0.1116 ± 0.0005 | 0.26 ± 0.02 | 95.09 ± 0.08 | 21.9 ± 1.5 | 0.2561 ± 0.0025 | 2.15 ± 0.21 |
| ALL-minus-F4 | 96.80 ± 0.02 | 96.22 ± 0.04 | 20.04 ± 0.73 | 0.1091 ± 0.0001 | 0.16 ± 0.06 | 95.08 ± 0.12 | 25.2 ± 2.5 | 0.2530 ± 0.0027 | 2.23 ± 0.27 |

#### UD-EWT dev

| arm | UD acc % | UD harmful per 1k |
| --- | --- | --- |
| BASE | 92.707 ± 0.063 | 22.19 ± 1.13 |
| F1 | 92.615 ± 0.022 | 23.24 ± 1.94 |
| F2 | 92.943 ± 0.059 | 19.91 ± 1.54 |
| F3 | 92.961 ± 0.084 | 22.80 ± 1.59 |
| F4 | 92.724 ± 0.041 | 20.74 ± 0.72 |
| ALL | 92.986 ± 0.030 | 20.00 ± 1.43 |
| ALL-minus-F1 | 93.007 ± 0.084 | 19.82 ± 0.86 |
| ALL-minus-F2 | 92.957 ± 0.069 | 20.83 ± 1.24 |
| ALL-minus-F3 | 92.885 ± 0.063 | 21.27 ± 0.97 |
| ALL-minus-F4 | 93.004 ± 0.084 | 20.88 ± 0.92 |

#### Model, speed and training

| arm | temperature | rows | raw bytes | deflated bytes | tokens/s (1 thread) | train wall s |
| --- | --- | --- | --- | --- | --- | --- |
| BASE | 6.584 ± 0.101 | 116453 ± 279 | 1079088 ± 2577 | 478352 ± 936 | 722824 ± 203951 | 46.1 ± 11.5 |
| F1 | 8.537 ± 0.000 | 126050 ± 292 | 1194629 ± 2083 | 534937 ± 885 | 699039 ± 88719 | 46.2 ± 16.7 |
| F2 | 9.473 ± 0.091 | 120981 ± 221 | 1146923 ± 1964 | 517282 ± 696 | 693014 ± 114518 | 48.3 ± 13.4 |
| F3 | 7.368 ± 0.072 | 124625 ± 268 | 1176037 ± 2089 | 524835 ± 775 | 523784 ± 103316 | 108.9 ± 46.8 |
| F4 | 6.699 ± 0.122 | 115005 ± 166 | 1069022 ± 1777 | 474880 ± 697 | 672601 ± 98652 | 13.7 ± 4.0 |
| ALL | 12.774 ± 0.151 | 132530 ± 239 | 1302897 ± 1388 | 599597 ± 581 | 333293 ± 55857 | 120.0 ± 39.3 |
| ALL-minus-F1 | 10.557 ± 0.102 | 126754 ± 316 | 1225977 ± 1578 | 559047 ± 657 | 377821 ± 100586 | 140.6 ± 14.1 |
| ALL-minus-F2 | 9.597 ± 0.114 | 132407 ± 150 | 1281749 ± 1383 | 580636 ± 463 | 480066 ± 104881 | 141.5 ± 37.7 |
| ALL-minus-F3 | 11.561 ± 0.000 | 124517 ± 182 | 1200819 ± 1694 | 547134 ± 795 | 624965 ± 98422 | 30.6 ± 1.7 |
| ALL-minus-F4 | 12.554 ± 0.121 | 134807 ± 195 | 1322271 ± 2182 | 606955 ± 970 | 435470 ± 63332 | 118.6 ± 6.1 |

#### Hard tranche per shape: target accuracy %

| arm | n-compound-head | n-subject-initial | v-imperative-initial | v-after-modal |
| --- | --- | --- | --- | --- |
| BASE | 65.2 ± 3.0 | 77.6 ± 3.9 | 45.8 ± 3.7 | 80.2 ± 2.8 |
| F1 | 64.6 ± 0.5 | 67.6 ± 6.1 | 50.2 ± 3.0 | 80.6 ± 1.7 |
| F2 | 62.2 ± 2.9 | 78.8 ± 2.3 | 51.6 ± 4.3 | 86.6 ± 1.5 |
| F3 | 66.4 ± 1.5 | 73.4 ± 3.0 | 47.0 ± 3.1 | 82.2 ± 2.4 |
| F4 | 66.6 ± 1.1 | 76.8 ± 2.2 | 47.6 ± 1.7 | 80.8 ± 1.5 |
| ALL | 65.4 ± 2.9 | 73.0 ± 2.5 | 54.0 ± 1.2 | 89.2 ± 1.1 |
| ALL-minus-F1 | 65.8 ± 1.5 | 75.0 ± 4.5 | 51.6 ± 2.6 | 86.2 ± 2.2 |
| ALL-minus-F2 | 64.8 ± 3.2 | 71.2 ± 0.8 | 51.6 ± 1.1 | 85.6 ± 2.3 |
| ALL-minus-F3 | 64.8 ± 3.9 | 73.6 ± 5.9 | 53.0 ± 3.7 | 88.4 ± 1.7 |
| ALL-minus-F4 | 63.4 ± 2.9 | 72.4 ± 2.6 | 51.2 ± 4.0 | 85.8 ± 1.5 |

#### Hard tranche per shape: harmful per 1k

| arm | n-compound-head | n-subject-initial | v-imperative-initial | v-after-modal |
| --- | --- | --- | --- | --- |
| BASE | 310.0 ± 30.0 | 126.0 ± 36.5 | 416.0 ± 48.3 | 176.0 ± 20.7 |
| F1 | 316.0 ± 11.4 | 194.0 ± 28.8 | 388.0 ± 23.9 | 170.0 ± 15.8 |
| F2 | 332.0 ± 27.7 | 114.0 ± 19.5 | 386.0 ± 30.5 | 104.0 ± 11.4 |
| F3 | 302.0 ± 13.0 | 136.0 ± 20.7 | 414.0 ± 25.1 | 156.0 ± 18.2 |
| F4 | 300.0 ± 14.1 | 120.0 ± 14.1 | 402.0 ± 22.8 | 168.0 ± 19.2 |
| ALL | 298.0 ± 28.6 | 162.0 ± 16.4 | 360.0 ± 20.0 | 82.0 ± 13.0 |
| ALL-minus-F1 | 296.0 ± 11.4 | 144.0 ± 33.6 | 370.0 ± 23.5 | 106.0 ± 20.7 |
| ALL-minus-F2 | 314.0 ± 39.1 | 174.0 ± 29.7 | 376.0 ± 15.2 | 128.0 ± 19.2 |
| ALL-minus-F3 | 308.0 ± 43.8 | 156.0 ± 37.1 | 368.0 ± 29.5 | 98.0 ± 14.8 |
| ALL-minus-F4 | 318.0 ± 27.7 | 164.0 ± 28.8 | 370.0 ± 36.7 | 116.0 ± 8.9 |

#### Welch 95% interval of (arm - BASE)

Difference of the means with the Welch t interval over the seeds (seeds measure training noise only).

| arm | shapes.hard.pooled.harmfulPer1k | shapes.standard.pooled.harmfulPer1k | openDev.harmfulPer1k | udDev.harmfulPer1k | udDev.accuracy |
| --- | --- | --- | --- | --- | --- |
| F1 | +10.00 [-3.61, +23.61] | -3.67 [-9.81, +2.48] | -0.67 [-2.02, +0.67] | +1.05 [-1.37, +3.47] | -0.09 [-0.17, -0.02] |
| F2 | -23.00 [-37.60, -8.40] | -10.67 [-16.06, -5.27] | -0.63 [-2.13, +0.87] | -2.28 [-4.28, -0.27] | +0.24 [+0.15, +0.32] |
| F3 | -5.00 [-16.38, +6.38] | +0.00 [-5.96, +5.96] | -0.27 [-1.55, +1.02] | +0.61 [-1.44, +2.66] | +0.25 [+0.14, +0.36] |
| F4 | -9.50 [-19.85, +0.85] | +1.33 [-3.46, +6.12] | -0.94 [-2.27, +0.38] | -1.44 [-2.87, -0.02] | +0.02 [-0.06, +0.10] |
| ALL | -31.50 [-46.07, -16.93] | -13.33 [-18.15, -8.51] | -2.70 [-3.96, -1.43] | -2.19 [-4.09, -0.29] | +0.28 [+0.20, +0.36] |
| ALL-minus-F1 | -28.00 [-43.40, -12.60] | -13.33 [-18.11, -8.56] | -1.98 [-3.49, -0.46] | -2.36 [-3.85, -0.88] | +0.30 [+0.19, +0.41] |
| ALL-minus-F2 | -9.00 [-19.20, +1.20] | -4.67 [-11.71, +2.38] | -2.11 [-3.45, -0.77] | -1.36 [-3.09, +0.38] | +0.25 [+0.15, +0.35] |
| ALL-minus-F3 | -24.50 [-40.35, -8.65] | -14.00 [-18.77, -9.23] | -1.84 [-3.27, -0.42] | -0.92 [-2.46, +0.63] | +0.18 [+0.09, +0.27] |
| ALL-minus-F4 | -15.00 [-33.61, +3.61] | -14.33 [-19.13, -9.54] | -1.26 [-2.59, +0.08] | -1.31 [-2.83, +0.20] | +0.30 [+0.19, +0.41] |

#### Guards against BASE

Delta guards use the mean over the seeds; the throughput guard uses the median of the seeds' medians.

| arm | standard harmful per 1k <= BASE + 5 | open dev harmful per 1k <= BASE + 1.5 | UD-EWT dev harmful per 1k <= BASE + 1.5 | UD-EWT dev accuracy >= BASE - 0.15 | deflated bytes <= 2 x BASE | tokens/s >= 150,000 | all guards |
| --- | --- | --- | --- | --- | --- | --- | --- |
| F1 | PASS -3.67 | PASS -0.67 | PASS +1.05 | PASS -0.09 | PASS 1.12x | PASS 675998 | PASS |
| F2 | PASS -10.67 | PASS -0.63 | PASS -2.28 | PASS +0.24 | PASS 1.08x | PASS 754719 | PASS |
| F3 | PASS +0.00 | PASS -0.27 | PASS +0.61 | PASS +0.25 | PASS 1.10x | PASS 556779 | PASS |
| F4 | PASS +1.33 | PASS -0.94 | PASS -1.44 | PASS +0.02 | PASS 0.99x | PASS 636980 | PASS |
| ALL | PASS -13.33 | PASS -2.70 | PASS -2.19 | PASS +0.28 | PASS 1.25x | PASS 302032 | PASS |
| ALL-minus-F1 | PASS -13.33 | PASS -1.98 | PASS -2.36 | PASS +0.30 | PASS 1.17x | PASS 421805 | PASS |
| ALL-minus-F2 | PASS -4.67 | PASS -2.11 | PASS -1.36 | PASS +0.25 | PASS 1.21x | PASS 527898 | PASS |
| ALL-minus-F3 | PASS -14.00 | PASS -1.84 | PASS -0.92 | PASS +0.18 | PASS 1.14x | PASS 659045 | PASS |
| ALL-minus-F4 | PASS -14.33 | PASS -1.26 | PASS -1.31 | PASS +0.30 | PASS 1.27x | PASS 459080 | PASS |

#### Selection rule

Primary: mean hard harmful per 1k (lower is better); BASE mean 257.00.

| arm | groups | primary | passes all guards |
| --- | --- | --- | --- |
| BASE | 0 | 257.00 ± 8.18 | - |
| F1 | 1 | 267.00 ± 10.22 | yes |
| F2 | 1 | 234.00 ± 11.26 | yes |
| F3 | 1 | 252.00 ± 7.37 | yes |
| F4 | 1 | 247.50 ± 5.30 | yes |
| ALL | 4 | 225.50 ± 11.24 | yes |
| ALL-minus-F1 | 3 | 229.00 ± 12.07 | yes |
| ALL-minus-F2 | 3 | 248.00 ± 4.81 | yes |
| ALL-minus-F3 | 3 | 232.50 ± 12.50 | yes |
| ALL-minus-F4 | 3 | 242.00 ± 15.04 | yes |

- Passing arms (Q): F1, F2, F3, F4, ALL, ALL-minus-F1, ALL-minus-F2, ALL-minus-F3, ALL-minus-F4
- Lowest primary in Q: ALL (225.50); within 10% (primary <= 248.05): F2, F4, ALL, ALL-minus-F1, ALL-minus-F2, ALL-minus-F3, ALL-minus-F4
- Candidate (fewest groups, then lowest primary): F2
- Welch 95% interval of (F2 - BASE): -23.00 [-37.60, -8.40], df 7.30, p 0.0072

**Decision: SHIP F2.** F2 ships: it has the fewest groups among the passing arms within 10% of the lowest primary, and the Welch
95% interval of (arm - BASE) is [-37.60, -8.40], entirely below 0.

#### Descriptive: paired item bootstrap of the candidate on the hard tranche (not part of the rule)

F2 - BASE harmful per 1k: -23.00, 95% percentile interval [-44.50, -1.49] (2000 resamples of the items, seed 20261107, items
averaged over the seeds).

### Decision

The pre-registered rule selected **F2 (`clauseCues`)**, one group of seven features. The rule is the only basis of the decision.
It follows the owner's instruction to ship it, which the pre-registration made conditional on exactly this outcome, and
the follow-up (adoption, thresholds, one held-out `evaluate` as a disclosed third look) is recorded in the sections after
this one. F5 (word clusters) was not built (see the pre-registration).

### Caveats (stated with the result)

- **The shipped seed was an unlucky draw for BASE.** The BASE five-seed mean of the primary is 257.0 and the shipped seed
  (20260101) gave 267.5, the highest of its five seeds. The gap between the old and the new model at the shipped seed
  therefore is not the gap between the means; the same applies to every other measure, and a single seed also differs from the
  mean in the other direction on some measures.
- **Seeds measure training noise only.** The Welch interval does not include the sampling noise of the 400 hard items (the
  descriptive paired bootstrap over the items is much wider and has an upper end close to 0).
- **Nine candidate arms were compared**, so the lowest primary values are subject to winner's curse; the decision describes
  development evidence only, on a set that was used for baselines before and that the groups were designed knowing.
- **F1 hurt the hard tranche on its own** (its primary is above BASE, mainly on `n-subject-initial`), and **F3 and F4 alone
  did not significantly improve** it (their Welch intervals contain 0).
- **ALL had the lowest primary** (four groups, with a lower open dev harmful rate than F2 and the largest model and training
  cost), but it is not selected: F2 is within 10% of it, has fewer groups, and the pre-registered tie-break takes the arm with
  the fewest groups.
- **The function-only set is lexicon-based**, as pre-registered, and that makes `FirstContent` coarse: many function words
  have rare content readings in the lexicon (for example `the`, `a`, `of`, `in`, `to`, `is`, `it` are not function-only), so
  the first token usually counts as content and the token classes are noisier than their names suggest. This affects F1 and F2
  (F2 uses the token classes). It was implemented literally and was not tuned.
- **The calibrated temperature of every arm with groups is higher than BASE** (F2 is the arm with 9.47 against 6.58; see the
  table), so confidence cuts such as the harmful limit 0.45 and the operating-point thresholds are not interchangeable between
  BASE and F2; the operating points are recomputed for the shipped model.
- The training times and the throughput figures were measured on a loaded shared machine (see above) and vary by arm and run.

## Context-feature experiment: adoption of the clause-cue model (written before any held-out command of this step)

The pre-registered rule selected F2, and the project owner decided to ship it. This section records the adoption **before the
held-out command of this step** (`evaluate` has not been run; git history shows the order: results, this adoption and the
development figures, the deletion of the previous held-out record on purpose, the one `evaluate`, the evaluation record). No
hyperparameter, source, corpus or seed changed: the shipped seed 20260101 stays.

### Disclosure: the third look

The held-out sets (open test, gold v2 test, shape probe, STE challenge set, UD-EWT test) were scored for the model before
the label corrections, for the first retrained model (FULL) and for the adopted NO-ING model, which the adoption section above
calls the second look of the family. The adoption section also said that "any further held-out evaluation would be a third
look and is not planned"; that sentence is the state when it was written and stays as it is. This step is a new work item that
the owner authorized, conditional on a winner of the pre-registered rule, and the evaluation after this section is a
**disclosed third look** at sets that are no longer unseen for this family. The selection of F2 used development data only.
The one `evaluate` of this step is run once; no second run is made after a result exists (an infrastructure failure without a
result may be rerun without looking at results to tune). A held-out result that is worse than the previous model's is
reported as it is, and nothing is selected, tuned or reverted because of it.

### What changed in the clause-cue adoption

- **Configuration.** `training.json` gains `"features": ["clauseCues"]` (the file form of the configuration omits the member
  while no group is selected, so earlier files kept their form). Then, as for every retrained model: `train --output <file>`
  (outside the repository), `calibrate --weights <file> --update-config` (the temperature is refit on the new open dev: **9.514**,
  NLL 0.11274, ECE 0.241%; it was 6.442), `train --update-config` and `train --check` (exit 0). 121,033 rows, **301,571
  weights, 1,146,827 bytes raw, 517,210 bytes deflated** (116,662 rows, 282,426 weights, 1,080,650 and 478,835 before),
  `imageSha256` `ec3f090eb318abc1bcf4dc8a88e401567e606932142178277d3ec387e0aa40f3` (it was `a0d45458...8da3`), weights file
  SHA-256 `72cc28cf10c556a617e8edb49d92d96148ed023874cfe834557ea5c6911b9672`. The header carries feature set 2 and the mask 2
  (clause cues). **The new `weights.bin` is byte-identical to the weights of the F2 run of seed 20260101 in the experiment**
  (same SHA-256), so the experiment's seed-20260101 figures are those of the shipped model.
- **Tests and pins.** The embedded-model requirement that said "the embedded model has no feature group" now says that the
  embedded model enables the clause-cue group (test renamed `ModelStorage_EmbeddedWeights_EnableTheClauseCueGroup`, which asserts
  feature set 2, the mask and the zero reserved bytes); the two tests that load the embedded file with the base feature set
  now use the mask-aware loader, and the configuration test checks that the groups of `training.json` equal the groups of the
  header. The size figures of the documents were re-pinned. The two constants `FrozenOpenHarmful` and `FrozenGoldHarmful` of
  the trainer (90 and 11) are set to the development figures of the shipped model (100 and 16), which `thresholds` reported
  before it wrote anything (it refused with the old constants and wrote nothing); they only guard that the 0.45 row
  reproduces the freeze figures.

### Development figures of the shipped model (dev data only)

Seed 20260101, scored by `eval-arm` through the real decoder (the old model is the weights file of commit `4709395`, SHA-256
`439d4af5...4092`). The shapes development set is a development set for this selection and not an independent test.

| Measure | Previous model (BASE) | Shipped model (F2) |
| --- | --- | --- |
| Hard tranche harmful per 1k (400 target items) | 267.5 (107) | 235.0 (94) |
| Hard tranche target / control accuracy % | 66.00 / 96.67 | 69.50 / 98.33 |
| Standard tranche harmful per 1k (600 target items) | 28.3 (17) | 16.7 (10) |
| Standard tranche target / control accuracy % | 96.33 / 96.00 | 96.83 / 99.00 |
| Open dev accuracy % (ambiguous %) | 96.38 (95.68) | 96.65 (95.98) |
| Open dev harmful per 1k (of 4,451 decisions) | 20.22 (90) | **22.47 (100)** |
| Open dev NLL, ECE % | 0.1222, 0.26 | 0.1127, 0.24 |
| Gold v2 dev accuracy % | 95.24 | 94.84 |
| Gold v2 dev harmful per 1k (of 611 decisions) | 18.00 (11) | **26.19 (16)** |
| Gold v2 dev NLL, ECE % | 0.2601, 1.99 | 0.2577, 2.38 |
| UD-EWT dev agreement % (25,148 tokens) | 92.711 | 92.950 |
| UD-EWT dev harmful per 1k (of 4,570 decisions) | 22.54 (103) | 19.91 (91) |
| Temperature | 6.442 | 9.514 |
| Rows, weights | 116,662, 282,426 | 121,033, 301,571 |
| Raw, deflated bytes | 1,080,650; 478,835 | 1,146,827; 517,210 |
| Single-thread throughput, tokens per second (three alternating runs, median of five passes each; machine shared) | 747,790 to 1,059,991, median 816,220 | 779,638 to 843,284, median 833,252 |

**What is worse (stated before the held-out evaluation).** (1) At the shipped seed the open dev harmful count is **10 higher**
(100 against 90) and the gold v2 dev one 5 higher (16 against 11), where the five-seed means of the experiment have F2 below
BASE on open dev (20.67 against 21.30, interval contains 0) and 2.3 above on gold v2 dev (23.2 against 20.9; not gated). The old
model's seed was a lucky draw on open dev (20.22 against its mean 21.30) and an unlucky one on the hard tranche (267.5 against
257.0), so the gaps at the shipped seed are not the gaps between the means. (2) The model is 38,375 bytes larger when deflated
(+8.0%) and its calibrated temperature is higher (9.514), so the operating points move (below). (3) Gold v2 dev accuracy is 0.4
points lower at this seed. (4) The evidence for the selection is the pre-registered rule on development data: nine arms, a seed
interval that excludes only training noise, and a paired item bootstrap with an upper end near 0 (see the caveats above).

### Clause-cue model: `thresholds` result (development data only; committed before the held-out run)

`thresholds` (rule `nv-pooled-dev-v1`, grid, targets and floors unchanged) gave, for the model image `ec3f090e...`, temperature
9.514 and the dev files unchanged: **strict 0.95** (coverage 81.3% [79.9, 82.8], 3.2 harmful per 1,000 answered [1.1, 5.6], 13 of
4,116) and **balanced 0.75** (coverage 94.1% [93.4, 94.8], 9.9 [6.6, 13.6], 47 of 4,761). The previous model had strict 0.95
(coverage 81.8%, 3.1 per 1,000, 13 of 4,141) and balanced 0.80 (coverage 92.4%, 9.0, 42 of 4,675). The pooled harmful count at 0.45 is 116 of
5,062 decisions (22.9 per 1,000). The disclosure paragraph of the evaluation record (`docs/evaluation/disclosure.md`) was
revised for the third look before `thresholds` was run again (the generated development report carries it); that second run
read the development files only and produced the same operating points.

### Clause-cue model: procedure of the held-out step (fixed now)

1. The committed previous record (`docs/evaluation/test-results.md` and `.json`, `operating-points.json`,
   `confidence-thresholds.md`, `README.md`, `disclosure.md`) and the previous weights are copied outside the repository for the
   comparison; they stay in git history (the previous record at `4709395`).
2. Delete the previous `test-results.json` and `.md` (`evaluate` refuses to run while one exists) in a commit of its own, then
   `evaluate --preflight` (scores nothing), then `evaluate --dump-dir <folder outside the repository>` **once**. The stop
   conditions that fired in the earlier records (gold v2 `to`/`not`, UD-EWT raw gold outside the candidates, the shape probe's
   harmful errors) may fire again; they are reported, not tuned.
3. The previous model is shown beside the new one in this log and in `docs/evaluation/README.md`, from the previous record
   (the old held-out figures are not recomputed: the previous model is not evaluated again). Open test is not strictly
   comparable with the oldest record (its labels changed), but between the previous model and the shipped one every held-out
   set has the same labels, so all five are fair comparisons. Intervals are the 95% percentile bootstrap over documents of each
   record (1,000 resamples, seed 1); no paired interval between the models is computed, so overlapping intervals are not a test.
   Nothing is claimed as an improvement unless the data show it.

## Context-feature experiment: held-out evaluation of the shipped clause-cue model (disclosed third look; a new record, not an erratum)

This is a **new** record for the shipped model. It does not correct or replace the earlier ones ("Outcome of the held-out run" and
its errata, "Corpus cleanup C: held-out evaluation of the retrained model" for FULL, and "Corpus cleanup C: held-out evaluation of
the adopted NO-ING model"), which stay as written. Order (git history): the results and the adoption record above, the shipped
model with its development operating points (`thresholds`), the deletion of the previous record on purpose (`evaluate` refuses to
run while a record exists; the previous record stays in git at `4709395` and was copied outside the repository first), the one
`evaluate`, and this section with the documents after it.

**This is the third look of the family at these held-out sets** (see the disclosure above; four models have now been scored on
them in all). `evaluate --preflight` passed first (it scores nothing); `evaluate --dump-dir <folder outside the repository>` then ran
**once** (Normal priority, at most 8 threads, the treebank from the git-ignored cache). It exited with 1 because of the stop
conditions below (findings, not failures of the run) and the committed files are the output as produced. **No second `evaluate`
run was made.** Nothing was selected, tuned or reverted because of any result. The previous model was not evaluated again: its
figures below are those of its committed record.

Stop conditions of the protocol: **the same three fired as in all earlier records** (previous model in brackets):

- `shape-probe`: 37 harmful errors at 0.45 against a limit of 28 (5% of 560 rows) (48).
- `gold-v2-test-to-not`: gold outside the candidates for 50.00% of the 8 targets (the same); the `to`/`not` convention difference.
- `ud-test-raw`: gold outside the candidates for 1.93% (484 of 25,094 words) (the same). The primary mapped view is at 0.55% (138
  words) (the same), under the limit.

All other stop conditions held (the open test accuracy 96.78% against 96.65% on development; harmful rates per 1,000 answered at the strict
(2.2) and balanced (8.5) points on open test below twice the pooled development rates (3.2 and 9.9); open test ECE 0.23%; open test gold
outside the candidates 41 of 26,185 tokens).

**What is comparable (stated plainly).** The previous model and the shipped one were scored on the same labels on all five held-out
sets (the open test labels changed with the NO-ING adoption, not since), so all five are fair comparisons between them. Intervals are
the 95% percentile bootstrap over documents of each record (1,000 resamples, seed 1); no paired interval between the models was
computed, so overlapping intervals are not a test: the intervals overlap on every row below, so no single row proves a difference.
The sets are small and the shipped model is one seed (see the caveats above: seeds are training noise, and the gaps at one seed are
not the gaps between means).

Accuracy and harmful errors at the harmful limit 0.45, previous model and shipped model:

| Set | Model | Accuracy % | Ambiguous accuracy % | Decisions | Harmful | Harmful per 1,000 decisions | ECE % | NLL | Gold outside candidates |
| --- | --- | --- | --- | ---: | ---: | --- | --- | --- | ---: |
| open-test | previous | 96.53 [96.12, 96.87] | 95.69 [95.30, 96.05] | 4,392 | 82 | 18.7 [14.0, 23.7] | 0.20 [0.17, 0.52] | 0.1161 [0.1013, 0.1320] | 41 |
| | **shipped** | 96.78 [96.42, 97.09] | 96.02 [95.69, 96.34] | 4,392 | 74 | 16.8 [12.4, 21.6] | 0.23 [0.19, 0.50] | 0.1076 [0.0939, 0.1222] | 41 |
| gold-v2-test | previous | 94.37 [93.55, 95.18] | 91.39 [90.11, 92.64] | 936 | 28 | 29.9 [19.0, 41.2] | 2.49 [2.02, 3.38] | 0.2497 [0.2052, 0.2989] | 12 |
| | **shipped** | 94.77 [93.94, 95.53] | 92.06 [90.76, 93.21] | 936 | 23 | 24.6 [15.1, 36.1] | 1.90 [1.55, 2.89] | 0.2342 [0.1918, 0.2807] | 12 |
| gold-v2-test-full-agreement | previous | 95.23 [94.36, 96.05] | 92.23 [90.85, 93.54] | 713 | 22 | 30.9 [18.9, 44.3] | 1.85 [1.43, 2.74] | 0.1971 [0.1557, 0.2424] | 5 |
| | **shipped** | 95.62 [94.88, 96.37] | 92.93 [91.71, 94.18] | 713 | 15 | 21.0 [11.1, 32.6] | 1.49 [1.24, 2.46] | 0.1827 [0.1435, 0.2272] | 5 |
| gold-v2-test-to-not (8 targets) | previous | 50.00 [12.50, 87.50] | 100.00 | 0 | 0 | 0.0 | 55.64 [24.45, 87.44] | 6.9910 [1.9506, 12.0940] | 4 |
| | **shipped** | 50.00 [12.50, 87.50] | 100.00 | 0 | 0 | 0.0 | 52.65 [21.94, 87.25] | 6.9560 [1.8584, 12.0911] | 4 |
| shape-probe (560 rows) | previous | 89.64 [87.14, 92.14] | 89.57 [87.07, 92.09] | 556 | 48 | 86.3 [63.1, 109.9] | 3.53 [2.84, 6.63] | 0.2704 [0.2143, 0.3291] | 0 |
| | **shipped** | 91.25 [88.93, 93.39] | 91.19 [88.79, 93.37] | 556 | 37 | 66.5 [46.8, 86.2] | 2.47 [2.16, 5.32] | 0.2122 [0.1649, 0.2617] | 0 |
| challenge-set (STE, 272 tokens) | previous | 88.60 [83.03, 93.65] | 89.90 [83.65, 94.79] | 86 | 0 | 0.0 | 6.25 [4.04, 11.47] | 0.3644 [0.2095, 0.5371] | 0 |
| | **shipped** | 86.76 [80.55, 92.42] | 87.88 [81.37, 93.14] | 86 | 0 | 0.0 | 8.33 [4.23, 13.79] | 0.3815 [0.2087, 0.5689] | 0 |
| ud-test-mapped (UD-EWT, primary view) | previous | 92.85 [92.33, 93.31] | 92.43 [91.83, 92.96] | 4,410 | 79 | 17.9 [13.9, 22.5] | 2.22 [1.90, 2.70] | 0.3025 [0.2770, 0.3373] | 138 |
| | **shipped** | 93.19 [92.69, 93.64] | 92.84 [92.25, 93.40] | 4,410 | 70 | 15.9 [12.1, 20.0] | 2.08 [1.78, 2.52] | 0.2877 [0.2634, 0.3210] | 138 |
| ud-test-raw (UD-EWT, raw tags) | previous | 88.92 [88.38, 89.40] | 88.18 [87.53, 88.74] | 4,007 | 79 | 19.7 [15.3, 24.8] | 6.15 [5.75, 6.67] | 0.6191 [0.5849, 0.6607] | 484 |
| | **shipped** | 89.25 [88.73, 89.72] | 88.56 [87.90, 89.15] | 4,007 | 70 | 17.5 [13.3, 22.1] | 5.99 [5.62, 6.48] | 0.6019 [0.5677, 0.6417] | 484 |

**What is worse (stated plainly).** On the **challenge set** (32 sentences, 272 tokens) the shipped model has a lower accuracy
(86.76 against 88.60), a higher calibration error and a higher log-loss; the set is a smoke test and the difference is five tokens. The **open test ECE** is 0.03
points higher (0.23 against 0.20), well inside the intervals. The previous model's **development** open and gold v2 harmful counts
were lower at this seed (see the adoption section), which the held-out sets do not repeat; nothing is concluded from that either way.
The shape probe still fires its stop condition (37 harmful errors against a limit of 28) and remains the weakest set (66.5 harmful
errors per 1,000 decisions): the model helps there, it does not remove the weakness.

Operating points on the held-out sets (the thresholds selected on development data: previous strict 0.95 and balanced 0.80; shipped
strict 0.95 and balanced 0.75; the reference point is 0.45):

| Set | Point | Previous: coverage % | Previous: harmful per 1,000 answered | Previous: harmful of answered | Shipped: coverage % | Shipped: harmful per 1,000 answered | Shipped: harmful of answered |
| --- | --- | ---: | --- | --- | ---: | --- | --- |
| open-test | reference 0.45 | 99.7 | 18.7 [14.1, 23.8] | 82 of 4,381 | 99.7 | 16.9 [12.4, 21.7] | 74 of 4,381 |
| open-test | strict | 83.8 | 3.5 [1.6, 6.0] | 13 of 3,682 | 83.9 | 2.2 [0.6, 4.3] | 8 of 3,687 |
| open-test | balanced | 93.3 | 7.3 [4.5, 10.4] | 30 of 4,097 | 94.1 | 8.5 [5.4, 11.4] | 35 of 4,135 |
| gold-v2-test | reference 0.45 | 99.8 | 30.0 [19.1, 41.3] | 28 of 934 | 99.9 | 24.6 [15.2, 36.1] | 23 of 935 |
| gold-v2-test | strict | 82.2 | 6.5 [1.4, 13.3] | 5 of 769 | 81.3 | 0.0 [0.0, 0.0] | 0 of 761 |
| gold-v2-test | balanced | 91.9 | 9.3 [3.4, 16.5] | 8 of 860 | 92.4 | 10.4 [4.4, 17.6] | 9 of 865 |
| gold-v2-test-full-agreement | reference 0.45 | 99.9 | 30.9 [18.9, 44.3] | 22 of 712 | 99.9 | 21.1 [11.2, 32.7] | 15 of 712 |
| gold-v2-test-full-agreement | strict | 82.5 | 5.1 [0.0, 10.9] | 3 of 588 | 80.8 | 0.0 [0.0, 0.0] | 0 of 576 |
| gold-v2-test-full-agreement | balanced | 91.4 | 7.7 [1.7, 15.0] | 5 of 652 | 92.7 | 10.6 [3.3, 19.0] | 7 of 661 |
| shape-probe | reference 0.45 | 98.0 | 88.1 [64.3, 112.1] | 48 of 545 | 98.2 | 67.8 [48.1, 87.3] | 37 of 546 |
| shape-probe | strict | 60.6 | 11.9 [2.8, 24.0] | 4 of 337 | 61.2 | 5.9 [0.0, 14.8] | 2 of 340 |
| shape-probe | balanced | 77.5 | 37.1 [19.0, 55.8] | 16 of 431 | 82.0 | 21.9 [8.8, 34.1] | 10 of 456 |
| challenge-set | reference 0.45 | 100.0 | 0.0 [0.0, 0.0] | 0 of 86 | 100.0 | 0.0 [0.0, 0.0] | 0 of 86 |
| challenge-set | strict | 70.9 | 0.0 [0.0, 0.0] | 0 of 61 | 67.4 | 0.0 [0.0, 0.0] | 0 of 58 |
| challenge-set | balanced | 80.2 | 0.0 [0.0, 0.0] | 0 of 69 | 84.9 | 0.0 [0.0, 0.0] | 0 of 73 |
| ud-test-mapped | reference 0.45 | 99.5 | 18.0 [14.1, 22.6] | 79 of 4,389 | 99.2 | 16.0 [12.2, 20.2] | 70 of 4,375 |
| ud-test-mapped | strict | 79.3 | 2.6 [1.1, 4.4] | 9 of 3,497 | 77.5 | 2.6 [1.1, 4.6] | 9 of 3,417 |
| ud-test-mapped | balanced | 89.8 | 6.6 [4.1, 9.5] | 26 of 3,961 | 91.3 | 8.2 [5.4, 11.5] | 33 of 4,028 |
| ud-test-raw | reference 0.45 | 99.5 | 19.8 [15.4, 24.9] | 79 of 3,986 | 99.1 | 17.6 [13.5, 22.3] | 70 of 3,972 |
| ud-test-raw | strict | 80.2 | 2.8 [1.2, 4.8] | 9 of 3,214 | 77.8 | 2.9 [1.2, 5.1] | 9 of 3,116 |
| ud-test-raw | balanced | 90.0 | 7.2 [4.5, 10.6] | 26 of 3,607 | 91.2 | 9.0 [5.9, 12.7] | 33 of 3,656 |

(The two balanced thresholds differ, so a balanced row compares two different operating points: the shipped balanced point answers
more and makes more harmful errors per 1,000 answered on every set that has any except the shape probe, the cost that its
development selection accepted within the development target of 10 per 1,000; on held-out data that point is at 10.4 and 10.6 on
the two gold v2 views, above 10. The shipped strict point is at or below its target of 5.0 per 1,000 answered on open test, gold v2
test and UD-EWT, and above it on the shape probe (5.9, two of 340; the previous model 11.9).)

Noun-verb confusion of the same decisions (previous | shipped; noun as other, noun as noun, noun as verb, verb as other, verb as
noun, verb as verb):

| Set | Previous | Shipped |
| --- | --- | --- |
| open-test | 51, 2,377, 29, 31, 53, 1,851 | 54, 2,373, 30, 38, 45, 1,852 |
| gold-v2-test | 2, 454, 11, 9, 17, 443 | 2, 454, 11, 9, 12, 448 |
| shape-probe | 6, 355, 35, 2, 15, 143 | 7, 361, 28, 1, 13, 146 |
| challenge-set | 3, 47, 0, 3, 0, 33 | 7, 43, 0, 4, 0, 32 |
| ud-test-mapped | 140, 2,131, 43, 66, 41, 1,989 | 150, 2,121, 43, 63, 32, 2,001 |
| ud-test-raw | 140, 2,131, 43, 53, 41, 1,599 | 150, 2,121, 43, 51, 32, 1,610 |

Shape probe by shape (accuracy % and harmful at 0.45; previous | shipped): `n-obj-light` 95.00, 1 | 96.25, 1;
`n-compound-head` 73.75, 18 | 77.50, 15; `n-modifier` 100.00, 0 | 97.50, 0; `n-subject-initial` 86.25, 10 | 90.00, 7; `n-measure`
93.75, 5 | 95.00, 4; `v-imperative-initial` 82.50, 12 | 83.75, 10; `v-after-modal` 96.25, 2 | 98.75, 0.

Inputs and run: model image `ec3f090e...aa40f3`, selection artifact SHA-256
`4087b928123d80eadbbdb5a08e49f941beee1564feffe7b7cc30c52ec87ca80b`, the six input hashes are those of the previous record (unchanged
data; see `docs/evaluation/test-results.md`). The throughput line of the report (571,504 tokens per second, one thread, open test)
is a timing of a shared machine of the moment, not a result of the model, and it is not comparable with the previous record's line
(894,415 on another load); the dev-side alternating measurement of the adoption section (medians 833,252 and 816,220) is the
better comparison.

**Reading.** On the held-out sets the shipped model has a higher accuracy than the previous one on four of the five (open test +0.25
points, gold v2 +0.40, shape probe +1.61, UD-EWT mapped +0.34 and raw +0.33) and a lower one on the challenge set (-1.84), and fewer
harmful errors at 0.45 on every set that has any (open test 74 against 82, gold v2 23 against 28, shape probe 37 against 48, UD-EWT 70
against 79). The direction agrees with the development evidence of the experiment (hard tranche, UD-EWT dev), and it is the
opposite of the seed-20260101 development open and gold v2 harmful counts. The intervals overlap everywhere, so none of these
differences is a significant one on its own; the experiment's own evidence is the pre-registered development rule, and this
record adds a third, weaker look.

## Run table

Every row is one run, scored on open dev and gold v2 dev only, at the run's own fitted temperature.

| Run | Settings | Open acc % | Open ambiguous acc % | Open harmful | Open harmful per 1k N/V | Gold acc % | Gold harmful | Pooled harmful per 1k N/V | NLL | ECE % | Rows | Weights | Raw KB | Deflated KB | Train s |
| --- | --- | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: |
| E0 baseline | sources=open:1 epochs=10 hashBits=21 minCount=1 prune=0 S=16 T=6.442 restriction=None history=Predicted seed=20260101 | 95.53 | 94.41 | 113 | 25.2 | 94.45 | 14 | 24.9 | 0.1477 | 0.36 | 99511 | 275313 | 1001 | 450 | 10.4 |
| E0 seed 11 | sources=open:1 epochs=10 hashBits=21 minCount=1 prune=0 S=16 T=6.442 restriction=None history=Predicted seed=11 | 95.51 | 94.36 | 109 | 24.3 | 94.15 | 18 | 24.9 | 0.1472 | 0.36 | 99125 | 275234 | 1000 | 450 | 2.9 |
| E0 seed 12 | sources=open:1 epochs=10 hashBits=21 minCount=1 prune=0 S=16 T=6.442 restriction=None history=Predicted seed=12 | 95.47 | 94.33 | 106 | 23.6 | 94.55 | 11 | 23.0 | 0.1491 | 0.31 | 99361 | 275436 | 1001 | 450 | 2.9 |
| E1 B history-restricted | sources=open:1 epochs=10 hashBits=21 minCount=1 prune=0 S=16 T=6.442 restriction=History history=Predicted seed=20260101 | 95.56 | 94.44 | 107 | 23.9 | 94.70 | 9 | 22.8 | 0.1486 | 0.30 | 97803 | 270560 | 984 | 443 | 2.9 |
| E1 C full-restricted | sources=open:1 epochs=10 hashBits=21 minCount=1 prune=0 S=16 T=6.874 restriction=Full history=Predicted seed=20260101 | 94.74 | 94.33 | 110 | 24.5 | 94.05 | 13 | 24.1 | 0.1823 | 0.80 | 72939 | 191443 | 704 | 315 | 3.3 |
| E1 A gold-history | sources=open:1 epochs=10 hashBits=21 minCount=1 prune=0 S=16 T=6.442 restriction=None history=Gold seed=20260101 | 95.36 | 94.20 | 113 | 25.2 | 93.95 | 11 | 24.3 | 0.1549 | 0.40 | 92007 | 255844 | 930 | 419 | 3.0 |
| E1 C full gold-history | sources=open:1 epochs=10 hashBits=21 minCount=1 prune=0 S=16 T=7.336 restriction=Full history=Gold seed=20260101 | 94.55 | 94.05 | 112 | 25.0 | 93.55 | 12 | 24.3 | 0.1890 | 0.95 | 68009 | 178513 | 657 | 295 | 4.9 |
| E2 open+masc | sources=open:1,masc:1 epochs=10 hashBits=21 minCount=1 prune=0 S=16 T=7.025 restriction=History history=Predicted seed=20260101 | 95.56 | 94.51 | 93 | 20.7 | 95.14 | 11 | 20.4 | 0.1447 | 0.37 | 221274 | 648499 | 2332 | 1069 | 66.9 |
| E2 open+silver | sources=open:1,silver:1 epochs=10 hashBits=21 minCount=1 prune=0 S=16 T=7.179 restriction=History history=Predicted seed=20260101 | 95.63 | 94.53 | 104 | 23.2 | 94.65 | 16 | 23.6 | 0.1463 | 0.42 | 151963 | 427951 | 1551 | 707 | 13.2 |
| E2 open+masc+silver | sources=open:1,masc:1,silver:1 epochs=10 hashBits=21 minCount=1 prune=0 S=16 T=7.336 restriction=History history=Predicted seed=20260101 | 95.83 | 94.89 | 97 | 21.6 | 95.44 | 10 | 21.0 | 0.1390 | 0.25 | 261656 | 774280 | 2779 | 1281 | 26.4 |
| E2 open2+masc+silver | sources=open:2,masc:1,silver:1 epochs=10 hashBits=21 minCount=1 prune=0 S=16 T=7.179 restriction=History history=Predicted seed=20260101 | 95.91 | 95.03 | 90 | 20.1 | 95.59 | 12 | 20.0 | 0.1369 | 0.25 | 262462 | 778835 | 2794 | 1294 | 60.0 |
| E2 open2+masc+silver disputed-included | sources=open:2,masc:1,silver:1 epochs=10 hashBits=21 minCount=1 prune=0 S=16 T=7.179 restriction=History history=Predicted seed=20260101 | 96.13 | 95.33 | 90 | 20.1 | 95.54 | 9 | 19.4 | 0.1299 | 0.24 | 270174 | 805713 | 2888 | 1342 | 23.1 |
| E2 open2+masc+silver silver>=80 | sources=open:2,masc:1,silver:1 epochs=10 hashBits=21 minCount=1 prune=0 S=16 T=7.336 restriction=History history=Predicted seed=20260101 | 95.98 | 95.09 | 89 | 19.8 | 95.44 | 12 | 19.8 | 0.1363 | 0.20 | 256837 | 757616 | 2721 | 1256 | 16.3 |
| E2 open2+masc+silver silver>=95 | sources=open:2,masc:1,silver:1 epochs=10 hashBits=21 minCount=1 prune=0 S=16 T=7.336 restriction=History history=Predicted seed=20260101 | 95.91 | 94.98 | 99 | 22.1 | 95.54 | 8 | 21.0 | 0.1357 | 0.25 | 250858 | 738234 | 2653 | 1221 | 28.3 |
| E2 open3+masc+silver | sources=open:3,masc:1,silver:1 epochs=10 hashBits=21 minCount=1 prune=0 S=16 T=7.336 restriction=History history=Predicted seed=20260101 | 96.03 | 95.15 | 87 | 19.4 | 95.49 | 10 | 19.0 | 0.1354 | 0.21 | 262222 | 778480 | 2793 | 1297 | 44.2 |
| E2 open2+masc | sources=open:2,masc:1 epochs=10 hashBits=21 minCount=1 prune=0 S=16 T=7.179 restriction=History history=Predicted seed=20260101 | 95.81 | 94.79 | 90 | 20.1 | 95.29 | 11 | 19.8 | 0.1396 | 0.31 | 222427 | 653924 | 2350 | 1083 | 340.6 |
| E2 open2+silver | sources=open:2,silver:1 epochs=10 hashBits=21 minCount=1 prune=0 S=16 T=7.497 restriction=History history=Predicted seed=20260101 | 95.84 | 94.82 | 103 | 23.0 | 94.65 | 13 | 22.8 | 0.1434 | 0.39 | 154390 | 437144 | 1582 | 725 | 13.3 |
| E2 open3+masc+silver disputed-included | sources=open:3,masc:1,silver:1 epochs=10 hashBits=21 minCount=1 prune=0 S=16 T=7.336 restriction=History history=Predicted seed=20260101 | 96.10 | 95.31 | 94 | 21.0 | 95.24 | 11 | 20.6 | 0.1298 | 0.30 | 270433 | 806949 | 2892 | 1349 | 45.5 |
| E2 open2+masc disputed-included | sources=open:2,masc:1 epochs=10 hashBits=21 minCount=1 prune=0 S=16 T=7.025 restriction=History history=Predicted seed=20260101 | 95.82 | 94.90 | 97 | 21.6 | 95.09 | 12 | 21.4 | 0.1340 | 0.29 | 230943 | 682473 | 2451 | 1132 | 24.0 |
| E2 open disputed-included | sources=open:1 epochs=10 hashBits=21 minCount=1 prune=0 S=16 T=6.037 restriction=History history=Predicted seed=20260101 | 95.81 | 94.82 | 116 | 25.9 | 94.55 | 12 | 25.1 | 0.1398 | 0.30 | 109551 | 307725 | 1116 | 507 | 3.6 |
| E2 open2+silver disputed-included | sources=open:2,silver:1 epochs=10 hashBits=21 minCount=1 prune=0 S=16 T=7.179 restriction=History history=Predicted seed=20260101 | 95.98 | 95.08 | 107 | 23.9 | 94.70 | 18 | 24.5 | 0.1355 | 0.32 | 164829 | 472029 | 1705 | 787 | 15.8 |
| E2 open+masc+silver disputed-included | sources=open:1,masc:1,silver:1 epochs=10 hashBits=21 minCount=1 prune=0 S=16 T=7.025 restriction=History history=Predicted seed=20260101 | 96.00 | 95.13 | 90 | 20.1 | 95.19 | 12 | 20.0 | 0.1346 | 0.31 | 268766 | 798748 | 2865 | 1323 | 13.9 |
| E2 open2+masc+silver silver>=80 disputed-included | sources=open:2,masc:1,silver:1 epochs=10 hashBits=21 minCount=1 prune=0 S=16 T=7.025 restriction=History history=Predicted seed=20260101 | 96.11 | 95.33 | 94 | 21.0 | 95.09 | 12 | 20.8 | 0.1310 | 0.32 | 265021 | 785784 | 2820 | 1304 | 13.1 |
| E2 open2+masc+silver2 disputed-included | sources=open:2,masc:1,silver:2 epochs=10 hashBits=21 minCount=1 prune=0 S=16 T=7.336 restriction=History history=Predicted seed=20260101 | 95.98 | 95.14 | 93 | 20.7 | 95.14 | 8 | 19.8 | 0.1327 | 0.32 | 273081 | 816155 | 2924 | 1365 | 14.6 |
| E3 epochs@ep3 | sources=open:2,masc:1,silver:1 epochs=15 hashBits=21 minCount=1 prune=0 S=16 T=6.304 restriction=History history=Predicted seed=20260101 epochs=3 | 96.00 | 95.14 | 101 | 22.5 | 94.84 | 13 | 22.4 | 0.1326 | 0.31 | 231608 | 666294 | 2404 | - | 12.1 |
| E3 epochs@ep4 | sources=open:2,masc:1,silver:1 epochs=15 hashBits=21 minCount=1 prune=0 S=16 T=6.442 restriction=History history=Predicted seed=20260101 epochs=4 | 96.04 | 95.18 | 103 | 23.0 | 94.89 | 15 | 23.2 | 0.1311 | 0.28 | 241840 | 702037 | 2529 | - | 18.6 |
| E3 epochs@ep6 | sources=open:2,masc:1,silver:1 epochs=15 hashBits=21 minCount=1 prune=0 S=16 T=6.727 restriction=History history=Predicted seed=20260101 epochs=6 | 96.08 | 95.27 | 97 | 21.6 | 95.04 | 13 | 21.6 | 0.1304 | 0.22 | 253979 | 745548 | 2680 | - | 27.8 |
| E3 epochs@ep8 | sources=open:2,masc:1,silver:1 epochs=15 hashBits=21 minCount=1 prune=0 S=16 T=6.874 restriction=History history=Predicted seed=20260101 epochs=8 | 96.04 | 95.21 | 97 | 21.6 | 95.14 | 12 | 21.4 | 0.1305 | 0.31 | 260611 | 769633 | 2764 | - | 37.5 |
| E3 epochs@ep10 | sources=open:2,masc:1,silver:1 epochs=15 hashBits=21 minCount=1 prune=0 S=16 T=7.025 restriction=History history=Predicted seed=20260101 epochs=10 | 96.11 | 95.33 | 94 | 21.0 | 95.09 | 12 | 20.8 | 0.1310 | 0.32 | 265021 | 785784 | 2820 | - | 50.2 |
| E3 epochs@ep12 | sources=open:2,masc:1,silver:1 epochs=15 hashBits=21 minCount=1 prune=0 S=16 T=7.179 restriction=History history=Predicted seed=20260101 epochs=12 | 96.11 | 95.34 | 98 | 21.9 | 95.24 | 12 | 21.6 | 0.1315 | 0.25 | 268117 | 797185 | 2859 | - | 64.4 |
| E3 epochs | sources=open:2,masc:1,silver:1 epochs=15 hashBits=21 minCount=1 prune=0 S=16 T=7.497 restriction=History history=Predicted seed=20260101 | 96.09 | 95.31 | 97 | 21.6 | 95.29 | 11 | 21.2 | 0.1325 | 0.19 | 271130 | 808353 | 2898 | 1351 | 70.4 |
| E3 6 epochs dedicated | sources=open:2,masc:1,silver:1 epochs=6 hashBits=21 minCount=1 prune=0 S=16 T=6.727 restriction=History history=Predicted seed=20260101 | 96.08 | 95.27 | 97 | 21.6 | 95.04 | 13 | 21.6 | 0.1304 | 0.22 | 253979 | 745548 | 2680 | 1222 | 7.2 |
| E4 hashBits 19 | sources=open:2,masc:1,silver:1 epochs=6 hashBits=19 minCount=1 prune=0 S=16 T=6.874 restriction=History history=Predicted seed=20260101 | 96.10 | 95.31 | 96 | 21.4 | 95.29 | 10 | 20.8 | 0.1307 | 0.24 | 212007 | 702532 | 2472 | 1114 | 14.4 |
| E4 hashBits 20 | sources=open:2,masc:1,silver:1 epochs=6 hashBits=20 minCount=1 prune=0 S=16 T=6.727 restriction=History history=Predicted seed=20260101 | 96.07 | 95.25 | 89 | 19.8 | 95.04 | 14 | 20.2 | 0.1308 | 0.31 | 238547 | 729975 | 2605 | 1175 | 7.1 |
| E4 hashBits 22 | sources=open:2,masc:1,silver:1 epochs=6 hashBits=22 minCount=1 prune=0 S=16 T=6.727 restriction=History history=Predicted seed=20260101 | 96.18 | 95.40 | 90 | 20.1 | 95.14 | 13 | 20.2 | 0.1291 | 0.31 | 261528 | 753003 | 2717 | 1258 | 7.0 |
| E4 hashBits 18 | sources=open:2,masc:1,silver:1 epochs=6 hashBits=18 minCount=1 prune=0 S=16 T=6.874 restriction=History history=Predicted seed=20260101 | 96.05 | 95.27 | 94 | 21.0 | 95.19 | 11 | 20.6 | 0.1318 | 0.31 | 169829 | 651940 | 2242 | 1021 | 6.2 |
| E5 minCount 2 | sources=open:2,masc:1,silver:1 epochs=6 hashBits=19 minCount=2 prune=0 S=16 T=6.727 restriction=History history=Predicted seed=20260101 | 96.06 | 95.23 | 101 | 22.5 | 95.24 | 11 | 22.0 | 0.1312 | 0.32 | 195841 | 667387 | 2338 | 1067 | 6.8 |
| E5 minCount 3 | sources=open:2,masc:1,silver:1 epochs=6 hashBits=19 minCount=3 prune=0 S=16 T=6.874 restriction=History history=Predicted seed=20260101 | 96.03 | 95.21 | 98 | 21.9 | 95.29 | 11 | 21.4 | 0.1307 | 0.22 | 176820 | 624281 | 2174 | 1008 | 8.1 |
| E5 minCount 5 | sources=open:2,masc:1,silver:1 epochs=6 hashBits=19 minCount=5 prune=0 S=16 T=6.874 restriction=History history=Predicted seed=20260101 | 96.03 | 95.22 | 97 | 21.6 | 94.94 | 13 | 21.6 | 0.1298 | 0.29 | 146464 | 548656 | 1894 | 897 | 9.4 |
| E5 prune 1 | sources=open:2,masc:1,silver:1 epochs=6 hashBits=19 minCount=1 prune=1 S=16 T=6.874 restriction=History history=Predicted seed=20260101 | 96.10 | 95.31 | 96 | 21.4 | 95.29 | 10 | 20.8 | 0.1307 | 0.25 | 210478 | 691151 | 2436 | 1096 | 11.3 |
| E5 prune 2 | sources=open:2,masc:1,silver:1 epochs=6 hashBits=19 minCount=1 prune=2 S=16 T=6.874 restriction=History history=Predicted seed=20260101 | 96.09 | 95.30 | 96 | 21.4 | 95.29 | 10 | 20.8 | 0.1307 | 0.24 | 208953 | 679965 | 2400 | 1078 | 9.9 |
| E5 prune 3 | sources=open:2,masc:1,silver:1 epochs=6 hashBits=19 minCount=1 prune=3 S=16 T=6.874 restriction=History history=Predicted seed=20260101 | 96.10 | 95.30 | 96 | 21.4 | 95.29 | 10 | 20.8 | 0.1307 | 0.26 | 207147 | 668176 | 2362 | 1059 | 29.7 |
| E5 prune 6 | sources=open:2,masc:1,silver:1 epochs=6 hashBits=19 minCount=1 prune=6 S=16 T=6.874 restriction=History history=Predicted seed=20260101 | 96.07 | 95.27 | 97 | 21.6 | 95.29 | 10 | 21.0 | 0.1310 | 0.29 | 200491 | 628631 | 2233 | 996 | 9.9 |
| E5 prune 10 | sources=open:2,masc:1,silver:1 epochs=6 hashBits=19 minCount=1 prune=10 S=16 T=6.874 restriction=History history=Predicted seed=20260101 | 96.07 | 95.26 | 101 | 22.5 | 95.19 | 10 | 21.8 | 0.1309 | 0.31 | 186987 | 559334 | 2004 | 889 | 9.9 |
| E5 prune 16 | sources=open:2,masc:1,silver:1 epochs=6 hashBits=19 minCount=1 prune=16 S=16 T=6.727 restriction=History history=Predicted seed=20260101 | 96.04 | 95.23 | 94 | 21.0 | 95.09 | 11 | 20.6 | 0.1325 | 0.28 | 120860 | 295275 | 1101 | 531 | 5.2 |
| E5 prune 24 | sources=open:2,masc:1,silver:1 epochs=6 hashBits=19 minCount=1 prune=24 S=16 T=6.727 restriction=History history=Predicted seed=20260101 | 95.91 | 95.03 | 102 | 22.7 | 95.14 | 13 | 22.6 | 0.1345 | 0.27 | 95023 | 213029 | 810 | 394 | 5.8 |
| E5 minCount 3 prune 10 | sources=open:2,masc:1,silver:1 epochs=6 hashBits=19 minCount=3 prune=10 S=16 T=6.874 restriction=History history=Predicted seed=20260101 | 96.02 | 95.20 | 99 | 22.1 | 95.39 | 10 | 21.4 | 0.1307 | 0.30 | 158205 | 497435 | 1766 | 809 | 7.0 |
| E5 minCount 5 prune 6 | sources=open:2,masc:1,silver:1 epochs=6 hashBits=19 minCount=5 prune=6 S=16 T=6.874 restriction=History history=Predicted seed=20260101 | 96.04 | 95.23 | 97 | 21.6 | 94.99 | 13 | 21.6 | 0.1301 | 0.29 | 139837 | 489531 | 1707 | 805 | 6.9 |
| E5 prune 20 | sources=open:2,masc:1,silver:1 epochs=6 hashBits=19 minCount=1 prune=20 S=16 T=6.727 restriction=History history=Predicted seed=20260101 | 95.97 | 95.14 | 98 | 21.9 | 95.19 | 10 | 21.2 | 0.1337 | 0.32 | 109623 | 257334 | 968 | 466 | 6.8 |
| E5 prune 32 | sources=open:2,masc:1,silver:1 epochs=6 hashBits=19 minCount=1 prune=32 S=16 T=6.583 restriction=History history=Predicted seed=20260101 | 95.83 | 94.91 | 111 | 24.8 | 95.09 | 15 | 24.7 | 0.1388 | 0.28 | 61548 | 125441 | 488 | 250 | 15.4 |
| E5 prune 48 | sources=open:2,masc:1,silver:1 epochs=6 hashBits=19 minCount=1 prune=48 S=16 T=6.583 restriction=History history=Predicted seed=20260101 | 95.27 | 94.09 | 124 | 27.7 | 94.55 | 13 | 26.9 | 0.1523 | 0.38 | 29242 | 52939 | 212 | 116 | 12.8 |
| E6 scale 8 prune 8 | sources=open:2,masc:1,silver:1 epochs=6 hashBits=19 minCount=1 prune=8 S=8 T=6.727 restriction=History history=Predicted seed=20260101 | 96.07 | 95.28 | 94 | 21.0 | 95.34 | 9 | 20.2 | 0.1328 | 0.35 | 119632 | 290862 | 1086 | 481 | 299.8 |
| E6 scale 32 prune 32 | sources=open:2,masc:1,silver:1 epochs=6 hashBits=19 minCount=1 prune=32 S=32 T=6.727 restriction=History history=Predicted seed=20260101 | 96.04 | 95.23 | 93 | 20.7 | 95.09 | 11 | 20.4 | 0.1325 | 0.30 | 121525 | 297690 | 1110 | 573 | 141.2 |
| E6 scale 64 prune 64 | sources=open:2,masc:1,silver:1 epochs=6 hashBits=19 minCount=1 prune=64 S=64 T=6.727 restriction=History history=Predicted seed=20260101 | 96.04 | 95.24 | 93 | 20.7 | 95.09 | 11 | 20.4 | 0.1324 | 0.30 | 121814 | 298792 | 1113 | 607 | 13.6 |
| E6 scale 4 prune 4 | sources=open:2,masc:1,silver:1 epochs=6 hashBits=19 minCount=1 prune=4 S=4 T=6.727 restriction=History history=Predicted seed=20260101 | 96.01 | 95.20 | 98 | 21.9 | 95.09 | 11 | 21.4 | 0.1330 | 0.23 | 116928 | 281604 | 1053 | 425 | 14.8 |
| E7 final-candidate (S=8 prune 8 h19 6ep, T fitted) | sources=open:2,masc:1,silver:1 epochs=6 hashBits=19 minCount=1 prune=8 S=8 T=6.727 restriction=History history=Predicted seed=20260101 | 96.07 | 95.28 | 94 | 21.0 | 95.34 | 9 | 20.2 | 0.1328 | 0.35 | 119632 | 290862 | 1086 | 481 | 7.2 |
| E8 M1 (glossary features, hints unused) | eval g_m1.bin | 95.85 | 94.94 | 111 | 24.8 | 95.19 | 9 | 23.6 | 0.1339 | 0.35 | 111046 | 266201 | 997 | - | 0.0 |
| F final (frozen config, T=6.727) | sources=open:2,masc:1,silver:1 epochs=6 hashBits=19 minCount=1 prune=8 S=8 T=6.727 restriction=History history=Predicted seed=20260101 | 96.07 | 95.28 | 94 | 21.0 | 95.34 | 9 | 20.2 | 0.1328 | 0.35 | 119632 | 290862 | 1086 | 481 | 7.8 |
| C retrained (frozen) | sources=open:2,masc:1,silver:1 epochs=6 hashBits=19 minCount=1 prune=8 S=8 T=6.583 restriction=History history=Predicted seed=20260101 | 96.37 | 95.64 | 103 | 23.1 | 94.99 | 14 | 23.0 | 0.1223 | 0.22 | 116448 | 281682 | 1053 | 467 | 5.4 |

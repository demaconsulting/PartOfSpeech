<!-- markdownlint-disable MD013 MD060 -->

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

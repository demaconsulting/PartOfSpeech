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
of each (the results were produced once; a second run after the erratum below regenerated the report only):

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

Interpretation of the last two (the orchestrator's, flagged for confirmation by the user): the stop-condition rule on gold
outside the candidates is meant for the headline views of a held-out set; it applies to the `to`/`not` convention view
and to the raw UD view only as views that are reported separately, and the firing of those two is not a model defect. The
protocol text above applies the 1% limit to every view; it was not changed after seeing the result, and the two findings are
reported as fired.

All other stop conditions held: open test accuracy 96.10% against 96.07% on development; the harmful rates per 1,000
answered on open test at the strict (4.1) and balanced (9.3) points are below twice the pooled development rates (3.9 and
9.9); open test ECE 0.28%; open test gold outside the candidates 0.27%.

### Erratum: column labels of the noun-verb confusion table (presentational writer fix, report regenerated)

The first `test-results.md` (commit `e760c29`) labelled the six cells of the table "Noun-verb confusion" in the order noun,
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

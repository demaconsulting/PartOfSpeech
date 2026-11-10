<!-- markdownlint-disable MD013 MD060 -->
<!-- cspell:ignore gamify mkabl mkarm worktree worktrees isalpha scorings numpy scipy selftest nltk allimp Satterthwaite montanafinalstat unitrich usfs safehandlingofbo NCRP -->

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

## Shapes development set: standard tranche, build record

The standard tranche (`data/eval/shapes-dev/dev.tsv`, [README](../../data/eval/shapes-dev/README.md)) states what the data is; this section holds the order in which it was
built.

- **Commits.** The builder ran as `python tools/data/shapes_dev.py --work <work-folder> build --shape-lemma-cap n-measure=12` at PR #10 commit
  `43300948ecc5c76360e9284c951b119e8a63ff36` (squashed into PR #10 merge commit `17a9cf9` on main); the labeling rounds ran at PR #10 commit
  `57420f577b9f81ea25304475af85ee9bdae12f94`. A squash merge makes the commits of a PR unreachable from main, but they stay retrievable from the PR's head
  ref (`refs/pull/<n>/head`), for example `git fetch origin pull/10/head`.
- **Order of the rule.** Rules 1 and 2 and the treatment of disputed tokens were committed in the docstring of `tools/data/shapes_dev.py` (PR #10 commit `88bafc2`) before any
  label was seen. Rules 3, 4 and 5 and the cleaning filters were added later, after labels of an earlier round had been seen, because the frame-mismatch and gerund cases showed
  up there. They only remove items; no label was changed, and every change is in the history of `shapes_dev.py`.
- **Labeling rounds.** The labeling ran in three rounds of candidates: round 1 was **superseded** and archived (OCR noise and weak shape fidelity; its pool had only 57 controls),
  round 2 rebuilt the pool with the cleaning filter, and round 3 added `n-compound-head` candidates only. Sentences were never labeled twice.
- **Exclusion of the two NIST handbooks.** The project owner decided on 2026-11-06 to drop NIST Handbook 44 and NIST Handbook 130 after they had been used. The sentences that were
  removed (26 targets and 6 controls) were replaced from the other, already blind-labeled documents of the same pool, by the same method and decision rule; no new labeling was
  needed, and no label was changed. The baseline below was measured again on the rebuilt file.
- **Hand-check passes.** The final pass (D, on the committed data) is in the README. The earlier passes are kept as history; none of them describes the committed data:

  | Pass | Data | Correct | Incorrect | Unclear |
  | --- | --- | ---: | ---: | ---: |
  | A | superseded round-1 data | 59 | 0 | 1 |
  | B | before the nominal-gerund rule (rule 5) | 57 | 0 | 3 |
  | C | the data that still contained the two NIST documents | 59 | 0 | 1 |

  Pass B found three nominal gerunds, which motivated rule 5. Pass C's one unclear item was a garbled title-like fragment (OCR residue). The replacement sentences of the rebuilt
  set were not in any earlier sample, which is why pass D drew a fresh sample.

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

The main round labeled all 2,258 candidate sentences (2,320 candidates: 400, 846, 574 and 300 targets for `n-compound-head`,
`n-subject-initial`, `v-imperative-initial` and `v-after-modal`, plus 50 control candidates per shape; 16 blind batches). Round `h2` (the next 200 `v-after-modal` candidates) ran because that
shape kept 77 targets, below its floor of 80; rule 4 provides for it. Round `h3` (a controls-only round, 149 sentences, 152 control candidates, 38 per shape) ran because the controls were
45, below the floor of 50; the pre-registration did not name it, it applies rule 6 (the other-class sentences of the chosen target lemmas, at most 2 per lemma) and the controls floor, adds no target, and was
prepared and exported before any `h3` label existed. Both blind labelers tagged every sentence; the hand check passed the 95% gate. No other round ran, and no candidate was
relabeled or added after the labels were seen. The build ran at PR #11 commit `5d3abb5`. The
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

**Superseded in part.** The rows of the five arms that contain F3 (F3, ALL, ALL-minus-F1, ALL-minus-F2 and ALL-minus-F4) in the
tables below, and the selection set that follows from them, are superseded by the section "Context-feature experiment:
amendment 1 results" (after the amendment), which gives the corrected figures beside these ones. The rows of BASE, F1, F2,
F4 and ALL-minus-F3 are not affected. The tables are kept unchanged as the record of the first runs.

**Pre-registration commit.** The pre-registration section was committed as `3a7623a1bf2e792b95f33a1d60fb809615d5f58f` (the
pre-registration said that its SHA would be recorded after the fact; this is that record).

**Pipeline smoke runs.** After the pre-registration and before the arm runs, one real-corpus ALL (all four groups) training run
with seed 20260101 and its `eval-arm` scoring were made, for pipeline validation only: the timing and the memory use were read,
the metrics were not read, and the output is excluded from the tables. It is not one of the 50 runs of the table.
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
- **The F3 jackknife folds by document only.** A sentence whose text appears in more than one document or corpus (for example
  silver against open) can fall into different folds, so a small optimistic leak is possible for the arms with F3. F3 was not
  selected and is not part of the shipped model.

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

## Context-feature experiment: amendment 1 (jackknife grouping)

Dated 2026-11-07. This section is appended after the results, the adoption and the held-out record above, which are not
edited, and it is committed **before any re-run it describes**; the git history shows the order: the fix of the jackknife
(commit `f8ee48c`), this amendment, then the re-runs, then a results section for them. It changes the grouping of the
jackknife folds and the set of arms that are run again. It does not change the arms, the seeds, the primary metric, the
guards, the selection rule or the frozen harness files (`arms.json`, `run_arms.ps1`, `analyze_arms.py`), which keep the
hashes of the pre-registration.

### The defect

The jackknife of the `rightPreview` group (F3) formed its five folds from the hash of the document id (`source_id`) only. An
exact duplicate sentence under two document ids could therefore sit in two folds, and the fold model of the fold that held
one copy out was trained on the other copy. The preview tags of that sentence were then better than the tags of new text,
which is the optimism that the jackknife exists to remove. The results section above records this as a small optimistic
leak of the F3-containing arms; the formal review of the pull request found that this does not meet the requirement that a
fold model never sees the sentences that it tags. That caveat is superseded by this section: the defect is fixed and the
affected arms are run again.

Measurement of the training sources of `training.json` (open twice, MASC, silver at probability 80 or more), loaded by the
trainer's own reader, with a sentence key of the lower-case letters and numbers of the tokens (the key of the leakage
check; the corrected key, see "Sentence key parity" below: the key as first computed here used the letters and digits of
the UTF-16 code units, and the corrected key gives the same groups and folds):

| Measure | Value |
| --- | --- |
| Sentences of one epoch, with the source weights | 60,780 |
| Distinct sentences (a repeat of the weight is one sentence) | 48,790 |
| Distinct non-empty sentence keys | 47,999 |
| Documents | 9,576 |
| Sentences with an exact duplicate in another document | 608 (216 keys) |
| Of those, in another fold than a duplicate (document-id folds) | 399 (113 keys) |

Held-out sentences whose key occurs in the training set of their fold model, by fold (document-id folds): 43, 81, 134, 67
and 74 (399 in all). Fold sizes in distinct sentences, folds 0 to 4: 8,329, 12,167, 8,768, 11,331 and 8,195.

Connected components of the documents (two documents are joined when they share any sentence key): 9,471 components, of
which 9,446 are one document and 25 hold two or more documents (130 documents). The largest holds 26 documents and 8,393
sentences (17.2% of all distinct sentences); the next largest multi-document components hold 623 sentences (2 documents) and
513 sentences (24 documents). The largest component is below the 25% bound that was set before the fix to decide that
grouping is workable (above it, removing the duplicates of the held-out fold from the fold model training would have been
proposed instead).

### The fix

Documents are joined by union-find when they share any non-empty normalized sentence key, so the folds are made from the
connected components. The fold of a component is the FNV-1a hash of its smallest document id (ordinal order) modulo 5, the
same for every seed, every order of the files and every set of feature groups. K = 5 and the rest of the jackknife (fold
models without F3, on the other groups, the settings and the seed of the run, tagging their own fold like the first pass of
the decoder) are unchanged. A document that shares no key with another one keeps the fold of its own id. After the fix no
held-out sentence key occurs in the training set of its fold model (0 of 48,790), and the fold sizes in distinct sentences are
15,765, 9,698, 7,424, 8,315 and 7,588: the largest component falls into fold 0, so the fold model of fold 0 is trained on
33,025 sentences and the others on 39,092 to 41,366. That imbalance is accepted.

### Scope of the re-runs

- **Run again:** the five arms that contain F3: F3, ALL, ALL-minus-F1, ALL-minus-F2 and ALL-minus-F4. Five seeds each
  (20260101, 11, 12, 13, 14), 25 runs, with the fixed jackknife and with everything else unchanged: the trainer settings,
  the driver, the scoring, the primary metric, the guards and the selection rule of the pre-registration.
- **Stand:** BASE, F1, F2, F4 and ALL-minus-F3. They use no jackknife, so their runs are not affected. The shipped model
  (the F2 weights) and the held-out record of the shipped model are not affected either.
- **Selection:** the corrected table (the five re-run arms and the five standing arms) is passed through the same rule,
  unchanged. If an arm other than F2 would be selected, nothing ships and the decision goes to the project owner before any
  held-out data is touched. If F2 is selected again, the shipped model stands.
- **Superseded runs:** the earlier runs of the five F3-containing arms are kept outside the repository, and their figures
  stay in the results section above as superseded; the corrected figures are recorded in a results section that follows
  the re-runs.
- **No held-out set** is read by the re-runs.

## Context-feature experiment: amendment 1 results

All 25 re-runs of the amendment above are complete (the five arms F3, ALL, ALL-minus-F1, ALL-minus-F2 and ALL-minus-F4, five
seeds each: 20260101, 11, 12, 13, 14; 0 runs repeated or skipped). Every run was made by the frozen driver `run_arms.ps1` at
commit `ac5c20e` (clean tree in all 25 result files), which contains the jackknife fix `f8ee48c`, and scored by `eval-arm` on
development data only. The 25 runs of BASE, F1, F2, F4 and ALL-minus-F3 of the first results section stand (commit
`a674d50`), so the analysis covers all 10 arms. The unchanged, frozen `analyze_arms.py` was applied to the 50 result files and
the selection rule was applied mechanically. No held-out set was read.

The earlier runs of the five arms (the document-id folds) are superseded, not deleted: they are kept outside the repository
with their per-run files, and their figures stay in the results section above. Below, each cell of the five re-run arms shows
the superseded figure (document-id folds), an arrow, and the corrected one (sentence-component folds); a cell with one figure
did not change. Values are mean ± sample sd over the five seeds. The other columns of the pre-registered report (the hard
tranche per shape, the Welch intervals of the other measures and the guard table) are in the analysis report; every arm
passes every guard, as before.

| File | SHA-256 |
| --- | --- |
| `report2.md` (the corrected analysis) | `7D281A6A4A013CBADEF6719D724643CF8E788048D056B253074D12950CD0BF0C` |
| `decision2.json` (per-arm values, intervals, guards, decision) | `8223A3AB4023BBE2C16DDF5E73218053C130819D2AB6F652B989C89969F82DBE` |

### Shapes development set (superseded → corrected)

| arm | hard harmful per 1k (primary) | hard target acc % | hard control acc % | standard harmful per 1k | standard target acc % | standard control acc % |
| --- | --- | --- | --- | --- | --- | --- |
| F3 | 252.0 ± 7.4 → 253.0 ± 20.3 | 67.25 ± 1.00 → 67.90 ± 2.15 | 95.67 ± 0.91 → 95.33 ± 0.75 | 26.7 ± 4.2 → 27.0 ± 3.6 | 96.60 ± 0.38 → 96.40 ± 0.48 | 96.60 ± 0.55 → 96.80 ± 0.84 |
| ALL | 225.5 ± 11.2 → 221.0 ± 14.4 | 70.40 ± 0.42 → 71.35 ± 1.63 | 98.33 ± 0.00 | 13.3 ± 2.0 → 13.0 ± 1.4 | 97.80 ± 0.14 → 97.77 ± 0.19 | 99.00 ± 0.00 |
| ALL-minus-F1 | 229.0 ± 12.1 → 228.0 ± 11.1 | 69.65 ± 1.46 → 70.35 ± 0.65 | 98.33 ± 0.00 | 13.3 ± 1.2 → 14.7 ± 1.8 | 97.73 ± 0.19 → 97.60 ± 0.28 | 98.60 ± 0.55 → 98.80 ± 0.45 |
| ALL-minus-F2 | 248.0 ± 4.8 → 247.0 ± 14.1 | 68.30 ± 1.05 → 67.90 ± 0.95 | 96.67 ± 1.18 | 22.0 ± 5.5 → 21.7 ± 1.7 | 96.60 ± 0.51 → 96.67 ± 0.26 | 98.00 ± 1.22 → 97.80 ± 1.10 |
| ALL-minus-F4 | 242.0 ± 15.0 → 235.5 ± 3.3 | 68.20 ± 1.45 → 68.65 ± 0.74 | 98.33 ± 0.00 | 12.3 ± 1.9 → 13.0 ± 1.8 | 97.90 ± 0.22 → 97.63 ± 0.22 | 98.60 ± 0.55 |

### Open dev and gold v2 dev (superseded → corrected)

| arm | open acc % | open ambiguous acc % | open harmful per 1k | open NLL | open ECE % | gold acc % | gold harmful per 1k | gold NLL | gold ECE % |
| --- | --- | --- | --- | --- | --- | --- | --- | --- | --- |
| F3 | 96.60 ± 0.05 → 96.59 ± 0.05 | 95.99 ± 0.08 → 95.99 ± 0.07 | 21.03 ± 0.59 → 21.25 ± 1.43 | 0.1161 ± 0.0009 → 0.1165 ± 0.0011 | 0.22 ± 0.05 → 0.21 ± 0.05 | 95.14 ± 0.16 → 95.13 ± 0.04 | 19.3 ± 0.7 → 20.3 ± 1.9 | 0.2595 ± 0.0033 → 0.2561 ± 0.0017 | 2.39 ± 0.27 → 2.29 ± 0.08 |
| ALL | 96.82 ± 0.03 → 96.77 ± 0.05 | 96.24 ± 0.04 → 96.18 ± 0.06 | 18.60 ± 0.40 → 19.19 ± 0.85 | 0.1084 ± 0.0004 → 0.1088 ± 0.0008 | 0.22 ± 0.04 → 0.29 ± 0.05 | 95.27 ± 0.11 → 95.11 ± 0.14 | 19.3 ± 2.4 → 21.3 ± 2.0 | 0.2545 ± 0.0021 → 0.2549 ± 0.0021 | 2.22 ± 0.14 → 2.26 ± 0.18 |
| ALL-minus-F1 | 96.81 ± 0.06 → 96.80 ± 0.05 | 96.21 ± 0.06 → 96.19 ± 0.07 | 19.32 ± 1.04 → 18.92 ± 0.66 | 0.1088 ± 0.0010 | 0.28 ± 0.04 → 0.20 ± 0.06 | 95.06 ± 0.18 → 95.29 ± 0.12 | 22.6 ± 3.5 → 18.3 ± 1.8 | 0.2569 ± 0.0022 → 0.2549 ± 0.0010 | 2.22 ± 0.14 → 2.36 ± 0.15 |
| ALL-minus-F2 | 96.74 ± 0.04 → 96.69 ± 0.02 | 96.20 ± 0.04 → 96.12 ± 0.04 | 19.19 ± 0.76 → 20.00 ± 0.32 | 0.1134 ± 0.0004 → 0.1134 ± 0.0007 | 0.24 ± 0.05 → 0.18 ± 0.04 | 95.24 ± 0.06 → 95.21 ± 0.11 | 19.6 ± 2.0 → 18.3 ± 0.7 | 0.2563 ± 0.0041 → 0.2550 ± 0.0024 | 2.21 ± 0.19 → 2.30 ± 0.23 |
| ALL-minus-F4 | 96.80 ± 0.02 → 96.77 ± 0.04 | 96.22 ± 0.04 → 96.19 ± 0.04 | 20.04 ± 0.73 → 20.09 ± 0.80 | 0.1091 ± 0.0001 → 0.1093 ± 0.0008 | 0.16 ± 0.06 → 0.20 ± 0.03 | 95.08 ± 0.12 → 95.17 ± 0.20 | 25.2 ± 2.5 → 23.9 ± 2.7 | 0.2530 ± 0.0027 → 0.2527 ± 0.0017 | 2.23 ± 0.27 → 2.25 ± 0.09 |

### UD-EWT dev (superseded → corrected)

| arm | UD acc % | UD harmful per 1k |
| --- | --- | --- |
| F3 | 92.961 ± 0.084 → 92.926 ± 0.055 | 22.80 ± 1.59 → 22.19 ± 1.03 |
| ALL | 92.986 ± 0.030 → 93.013 ± 0.086 | 20.00 ± 1.43 → 19.65 ± 1.22 |
| ALL-minus-F1 | 93.007 ± 0.084 → 93.015 ± 0.061 | 19.82 ± 0.86 → 20.26 ± 0.72 |
| ALL-minus-F2 | 92.957 ± 0.069 → 92.902 ± 0.072 | 20.83 ± 1.24 → 21.62 ± 1.18 |
| ALL-minus-F4 | 93.004 ± 0.084 → 93.024 ± 0.040 | 20.88 ± 0.92 → 20.18 ± 1.66 |

### Model, speed and training (superseded → corrected)

The training times and the throughput were measured on a shared machine under changing load and are not comparable between
the two sets of runs.

| arm | temperature | rows | raw bytes | deflated bytes | tokens/s (1 thread) | train wall s |
| --- | --- | --- | --- | --- | --- | --- |
| F3 | 7.368 ± 0.072 → 7.400 ± 0.088 | 124625 ± 268 → 124779 ± 269 | 1176037 ± 2089 → 1177673 ± 2175 | 524835 ± 775 → 525510 ± 799 | 523784 ± 103316 → 469563 ± 81442 | 108.9 ± 46.8 → 57.0 ± 16.3 |
| ALL | 12.774 ± 0.151 → 12.663 ± 0.123 | 132530 ± 239 → 132396 ± 114 | 1302897 ± 1388 → 1302049 ± 1044 | 599597 ± 581 → 599190 ± 650 | 333293 ± 55857 → 316067 ± 59981 | 120.0 ± 39.3 → 85.4 ± 13.1 |
| ALL-minus-F1 | 10.557 ± 0.102 → 10.648 ± 0.104 | 126754 ± 316 → 126675 ± 270 | 1225977 ± 1578 → 1225906 ± 2299 | 559047 ± 657 → 559037 ± 884 | 377821 ± 100586 → 456963 ± 13113 | 140.6 ± 14.1 → 54.3 ± 2.7 |
| ALL-minus-F2 | 9.597 ± 0.114 → 9.680 ± 0.093 | 132407 ± 150 → 132521 ± 78 | 1281749 ± 1383 → 1283025 ± 1340 | 580636 ± 463 → 581137 ± 387 | 480066 ± 104881 → 485474 ± 105604 | 141.5 ± 37.7 → 96.3 ± 17.7 |
| ALL-minus-F4 | 12.554 ± 0.121 → 12.608 ± 0.000 | 134807 ± 195 → 134698 ± 143 | 1322271 ± 2182 → 1321542 ± 1716 | 606955 ± 970 → 606839 ± 706 | 435470 ± 63332 → 395685 ± 65424 | 118.6 ± 6.1 → 68.7 ± 9.5 |

### Welch 95% interval of (arm - BASE) for the primary (superseded → corrected)

| arm | shapes.hard.pooled.harmfulPer1k |
| --- | --- |
| F3 | -5.00 [-16.38, +6.38] → -4.00 [-28.83, +20.83] |
| ALL | -31.50 [-46.07, -16.93] → -36.00 [-53.92, -18.08] |
| ALL-minus-F1 | -28.00 [-43.40, -12.60] → -29.00 [-43.43, -14.57] |
| ALL-minus-F2 | -9.00 [-19.20, +1.20] → -10.00 [-27.53, +7.53] |
| ALL-minus-F4 | -15.00 [-33.61, +3.61] → -21.50 [-31.48, -11.52] |

### Selection rule on the corrected table

Primary: mean hard harmful per 1k (lower is better); BASE mean 257.00. The arms BASE, F1, F2, F4 and ALL-minus-F3 are as in
the first results section.

| arm | groups | primary (superseded → corrected) | passes all guards |
| --- | --- | --- | --- |
| BASE | 0 | 257.00 ± 8.18 | - |
| F1 | 1 | 267.00 ± 10.22 | yes |
| F2 | 1 | 234.00 ± 11.26 | yes |
| F3 | 1 | 252.00 ± 7.37 → 253.00 ± 20.34 | yes |
| F4 | 1 | 247.50 ± 5.30 | yes |
| ALL | 4 | 225.50 ± 11.24 → 221.00 ± 14.43 | yes |
| ALL-minus-F1 | 3 | 229.00 ± 12.07 → 228.00 ± 11.10 | yes |
| ALL-minus-F2 | 3 | 248.00 ± 4.81 → 247.00 ± 14.08 | yes |
| ALL-minus-F3 | 3 | 232.50 ± 12.50 | yes |
| ALL-minus-F4 | 3 | 242.00 ± 15.04 → 235.50 ± 3.26 | yes |

- Passing arms (Q): F1, F2, F3, F4, ALL, ALL-minus-F1, ALL-minus-F2, ALL-minus-F3, ALL-minus-F4 (unchanged)
- Lowest primary in Q: ALL, 225.50 → 221.00; within 10% of it: primary <= 248.05 → <= 243.10
- Within 10%: F2, F4, ALL, ALL-minus-F1, ALL-minus-F2, ALL-minus-F3, ALL-minus-F4 → F2, ALL, ALL-minus-F1, ALL-minus-F3,
  ALL-minus-F4 (F4 at 247.50 and ALL-minus-F2 at 247.00 are now above the bound)
- Candidate (fewest groups, then lowest primary): F2 (unchanged)
- Welch 95% interval of (F2 - BASE): -23.00 [-37.60, -8.40], df 7.30, p 0.0072 (unchanged); the descriptive paired item
  bootstrap of F2 on the hard tranche, [-44.50, -1.49], is unchanged as well

**Decision: SHIP F2 (unchanged).** The corrected table selects the same arm by the same rule. The shipped model (the F2
weights) and the held-out record of the shipped model stand unchanged, and no arm other than F2 was selected, so nothing
went to the project owner for a decision. The caveat of the first results section about the document-id folds is closed by
this section: the arms with F3 now have leakage-safe folds, F3 is still not part of the shipped model, and the conclusions
about F3 do not change (F3 alone does not significantly improve the hard tranche; its Welch interval contains 0).

### Order of the re-runs (audit)

The amendment (commit `58bdfd2`, committed 2026-11-08 05:21:56 UTC; the fix `f8ee48c` at 05:07:47 UTC) precedes every re-run:
the first run started at 14:48:20 UTC the same day, 9 h 26 min after the amendment, and `58bdfd2` is an ancestor of the commit
`ac5c20e` that every result file records (`git merge-base --is-ancestor 58bdfd2 ac5c20e`). The 25 runs went one after the other,
in the order of the table, in one invocation of the driver, which takes the arms with `-Arms F3,ALL,ALL-minus-F1,ALL-minus-F2,ALL-minus-F4`
and the runner with `-Runner dotnet-normal.cmd`; there was no earlier attempt that produced a result. The driver starts
`dotnet` at BelowNormal priority by default (the frozen script of the pre-registration, with at most 8 threads); the runner
`dotnet-normal.cmd` (`start "" /normal /wait /b dotnet %*`, kept in the session folder outside the repository) starts it at
Normal priority instead, as for the first 50 runs, because BelowNormal received almost no CPU on a machine that other sessions
saturated (the owner allowed this deviation, see the first results section). The driver script itself is unchanged.

Sources of the times: the **end** of a run is the `driver.utc` field that the driver wrote into the result file `<run>.json` (the
same instant as its line in `progress.log`); the driver records no start time, so the **start** is the file-system creation time
of `<run>.train.txt`, which the driver creates when it starts the train process (file-system time, not recorded metadata; it
agrees with end - train - eval seconds to within 1 s for all 25 runs). All times are UTC, 2026-11-08. The run folder is outside
the repository; its files are not part of the review set, so the table is the committed evidence of the order.

| Run | Start (UTC) | End (UTC) | Train s | Eval s |
| --- | --- | --- | ---: | ---: |
| `F3-s20260101` | 14:48:20 | 14:49:47 | 84.1 | 2.9 |
| `F3-s11` | 14:49:47 | 14:50:32 | 42.0 | 2.9 |
| `F3-s12` | 14:50:32 | 14:51:23 | 48.0 | 2.5 |
| `F3-s13` | 14:51:23 | 14:52:23 | 57.8 | 2.4 |
| `F3-s14` | 14:52:23 | 14:53:20 | 53.1 | 3.5 |
| `ALL-s20260101` | 14:53:20 | 14:55:02 | 99.1 | 3.4 |
| `ALL-s11` | 14:55:02 | 14:56:31 | 86.1 | 3.3 |
| `ALL-s12` | 14:56:31 | 14:58:12 | 97.0 | 3.1 |
| `ALL-s13` | 14:58:12 | 14:59:30 | 76.2 | 2.4 |
| `ALL-s14` | 14:59:30 | 15:00:41 | 68.7 | 2.1 |
| `ALL-minus-F1-s20260101` | 15:00:41 | 15:01:34 | 51.2 | 2.0 |
| `ALL-minus-F1-s11` | 15:01:34 | 15:02:33 | 56.8 | 2.0 |
| `ALL-minus-F1-s12` | 15:02:33 | 15:03:29 | 53.5 | 2.0 |
| `ALL-minus-F1-s13` | 15:03:29 | 15:04:28 | 57.5 | 2.0 |
| `ALL-minus-F1-s14` | 15:04:28 | 15:05:23 | 52.8 | 2.0 |
| `ALL-minus-F2-s20260101` | 15:05:23 | 15:06:55 | 90.1 | 1.9 |
| `ALL-minus-F2-s11` | 15:06:55 | 15:08:43 | 106.3 | 2.0 |
| `ALL-minus-F2-s12` | 15:08:43 | 15:10:40 | 115.1 | 2.0 |
| `ALL-minus-F2-s13` | 15:10:40 | 15:11:52 | 69.2 | 1.9 |
| `ALL-minus-F2-s14` | 15:11:52 | 15:13:35 | 100.7 | 2.9 |
| `ALL-minus-F4-s20260101` | 15:13:35 | 15:14:57 | 80.0 | 2.0 |
| `ALL-minus-F4-s11` | 15:14:57 | 15:15:57 | 57.4 | 2.4 |
| `ALL-minus-F4-s12` | 15:15:57 | 15:17:07 | 67.5 | 2.4 |
| `ALL-minus-F4-s13` | 15:17:07 | 15:18:11 | 62.3 | 2.0 |
| `ALL-minus-F4-s14` | 15:18:11 | 15:19:30 | 76.4 | 2.1 |

### Sentence key parity with the leakage normalizer (review follow-up)

The formal re-review found that the first version of the jackknife sentence key used .NET `char.IsLetterOrDigit` on UTF-16
code units, while the leakage check of the data tools (`key` in `tools/data/common.py`) works on code points: it keeps the
characters for which Python's `str.isalnum()` is true after NFC and `lower()`. The key is now equivalent: it iterates by code
point, keeps the letters and numbers (the categories `L*` and `N*`, so the superscripts, fractions and Roman numerals as well as
the supplementary-plane letters and numbers), applies the same lower case (including the final sigma and the dotted capital I)
and lists the supplementary-plane case mapping itself, because the runtimes differ in it. Parity is pinned by the fixture
`tools/data/tests/sentence-key-cases.tsv` (44 cases with astral characters, combining marks, case folding and the sigma rules; the
keys were written once by the Python function and are checked by both `test_common.py` and `ModelTrainerTests.cs`). A one-off
check of every Unicode code point (in four contexts, including the sigma rules, 4,448,256 strings) against Python 3.13
(Unicode 15.1) found one assigned code point with a different result on .NET 10, U+1171E (Python files it under the nonspacing
marks, .NET 10 under the spacing marks; neither is kept in a key, so only the final-sigma rule next to it differed). The key
now has an explicit category override for it (the mechanism for any such code point; the list is derived from that comparison)
and the fixture has the two sigma cases around it. After the override the check shows no difference except the 17,604 strings
whose code point Unicode 15.1 does not assign (parity targets the Unicode version of the Python normalizer; no sentence of the
corpora has such a code point). The corpus measurement below was repeated with the override: the keys of all 48,790 sentences
are unchanged and still equal the Python keys.

Effect on the training corpus (the 48,790 distinct sentences of the sources of `training.json`; the same measurement as in
amendment 1):

- No sentence has a supplementary-plane character, so the astral defect of the review did not occur in the corpus.
- 16 sentence keys differ between the first and the corrected key, because they contain a number that is no decimal digit
  (the superscripts `²` `¹` `₂`, the fractions `½` `⅓` `¾` and the Roman numeral `Ⅱ`); the corrected key keeps those characters,
  as the Python key does. The corrected key equals the Python key for all 48,790 sentences.
- **No document group and no fold assignment changes** (0 of 9,576 documents change group, 0 of 48,790 sentences change fold,
  and the grouping of the library equals the independent computation). The jackknife preview tags, the trained weights and all
  results of the experiment are therefore identical with the corrected key, so nothing was re-run.

## Imperative training data: pre-registration

This section is written and committed **before any model of this experiment is trained or evaluated**. No `train --output`, no
`eval-arm` and no other run has been made for any arm below: the only trainer command run since the data were registered is
`train --check` (exit 0, image `ec3f090e...`, which shows that the weight-0 entry leaves the shipped model unchanged). The git
history shows the order: the builder and the data rule (`100e747`, committed 2026-11-08 15:29 -0500, **before any label of the
source existed**), the data and their registration with weight 0 (`3a703f5`, 17:32 -0500), this commit (the experiment design, the
analysis script and the two runners), then the arm runs, then a results section. The SHA of this commit is recorded by the
commit that follows it (a commit cannot contain its own SHA). The code base of the experiment is `main` at `5f9a9f0` plus those two
commits; no source file of the trainer or the library changed in them (only `training.json`, the trainer README, data, tools of
`tools/data`, tests and documents).

### Imperative data: goal

Test whether a targeted training source of 900 imperative-initial sentences (the source `imperative`, `data/imperative/`) lowers the
harmful noun-verb errors of the tagger on the hard tranche of the shapes development set without costing the guarded measures,
size or speed. Motive: **scarcity** (the imperative-initial frame is 15.8 per 1,000 sentences of the training mix and 55.2 per 1,000
sentences of instructional federal text, a factor of 3.5; the count report `shape-count/REPORT.md` is kept outside the repository and
its numbers are listed here) and **weakness** (the hard `v-imperative-initial` shape has 51.6% target accuracy at F2, the five-seed
mean of the context-feature experiment, the weakest shape of the tranche). Nothing is adopted unless the rule below selects it.

### The data (frozen)

`data/imperative/imperative.tsv`: 900 sentences, 11,946 tokens, 351 distinct lemmas of the first word, 122 documents of US federal
instructional text (army 69 documents / 643 sentences, USDA 36 / 166, navy 12 / 68, Forest Service 3 / 11, Bureau of Mines 1 / 7,
Public Health Service 1 / 5; at most 6 sentences per lemma, at most 30 per document). Every sentence starts with an imperative verb
in its base form (token 0 is VERB in every sentence); every token has a tag on which two blind model labelers (`claude-opus-5.5` and
`gpt-6.1-sol`) agree; no human reviewed a label. The rule is the docstring of `tools/data/imperative_data.py` at `100e747` (a later commit changed only the wording of the source
families in the docstring and made the fix-tier lint of `build` take its resources from the correctable corpora only, as
`check_conventions.py` does; the output was verified byte-identical).

| Fact | Value |
| --- | --- |
| Candidates / kept by the decision / in the file | 2,300 / 1,595 / 900 (693 class mismatch and 12 target disagreement dropped; 143 for a disputed token, 1 fix-tier violation, caps 58 + 131 + 362) |
| Hand check (model review by `claude-sonnet-5.5`, seed 20261110, one draw) | target token 60/60 = 100.0% (gate 95%); whole sentence 59/60; token precision 99.87%; leniency controls accepted 0 of 4 |
| Leakage | 0 document overlaps, 0 exact sentence overlaps and 0 near duplicates against every evaluation set (`check_leakage.py`, `leakage-report.tsv`) |
| Lexicon | not rebuilt (`lexicon/lexicon.tsv` unchanged); 97.86% of the word tokens and 94.46% of the (spelling, tag) pairs are listed |
| Configuration | `training.json` has the source `imperative` with `"weight": 0`; `train --check` exit 0, `imageSha256` unchanged |

### Imperative data: arms and runs

| Arm | Configuration | Driver arm | Runner |
| --- | --- | --- | --- |
| **F2** | the shipped configuration: feature group `clauseCues`, the current training mix (open x2, MASC, silver >= 80) | `F2` | `runner-base.cmd` |
| **F2+IMP** | F2 plus the `imperative` source at weight 1 | `F2` | `runner-imp.cmd` |
| **ALL+IMP** | all four feature groups plus the `imperative` source at weight 1 | `ALL` | `runner-imp.cmd` |
| ALL (reference, reported, **not gated, never selected**) | all four feature groups, current mix (the ALL arm of the context-feature experiment, re-run on this code base) | `ALL` | `runner-base.cmd` |

Seeds: **20260101, 11, 12, 13, 14** (the seeds and the order of `context-features/arms.json`), five runs per arm: 15 runs for the
three arms, 20 with the reference arm. Every other setting is the shipped `training.json`; the temperature is refit on open dev
for every run (`--calibrate`); `previewFolds` is 5 (the `ALL` arms train the jackknife of `rightPreview`). The mechanism is the
frozen harness `context-features/run_arms.ps1`, unedited, with its `-Arms` and `-Runner` options, one folder per arm (the folders lie
on C: outside the repository), one invocation at a time, in this order:

```text
pwsh tools/DemaConsulting.PartOfSpeech.Trainer/context-features/run_arms.ps1 -Out <runs>\f2     -Arms F2  -Runner <repo>\tools\DemaConsulting.PartOfSpeech.Trainer\imperative-data\runner-base.cmd
pwsh tools/DemaConsulting.PartOfSpeech.Trainer/context-features/run_arms.ps1 -Out <runs>\f2imp  -Arms F2  -Runner <repo>\tools\DemaConsulting.PartOfSpeech.Trainer\imperative-data\runner-imp.cmd
pwsh tools/DemaConsulting.PartOfSpeech.Trainer/context-features/run_arms.ps1 -Out <runs>\allimp -Arms ALL -Runner <repo>\tools\DemaConsulting.PartOfSpeech.Trainer\imperative-data\runner-imp.cmd
pwsh tools/DemaConsulting.PartOfSpeech.Trainer/context-features/run_arms.ps1 -Out <runs>\all-ref -Arms ALL -Runner <repo>\tools\DemaConsulting.PartOfSpeech.Trainer\imperative-data\runner-base.cmd
```

The harness takes the runner as the command that replaces `dotnet`, so the **only** difference between a base and an imperative run
is the option `--source imperative:1`, which `runner-imp.cmd` appends to the `train` command (the driver calls the runner as
`<runner> <trainer.dll> train ...`; the runner tests its second argument; `build` and `eval-arm` pass through unchanged). The driver
trains with `train --seed S --features G [--preview-folds 5] --calibrate --output FILE [--source imperative:1]`, then scores the file
with the dev-only command `eval-arm`; it is resumable (a run is complete when its JSON exists), requires at least 6 GB free on C:
and uses at most 8 threads. A run that fails without a result is repeated; no run is repeated or replaced because of its result,
and no arm is dropped. The reference arm is run last and does not influence the rule.

**Deviation record (Normal priority).** The frozen driver starts `dotnet` at BelowNormal priority, which received almost no CPU
on this machine in the earlier experiment (other sessions keep its cores saturated). As in that experiment, the runners start
`dotnet` at Normal priority (`start "" /normal /wait /b dotnet %*`), which the project owner allowed; it changes the speed of the
runs and nothing else (the driver script is unchanged; the runners propagate the exit code of `dotnet`, which was tested with a
failing command). Consequence: the training times and the single-thread throughput are measured on a shared, loaded machine and
are not comparable between arms or with earlier records; the throughput guard is an absolute floor far below the speeds recorded
earlier (315k to 894k tokens per second), and the `ALL` arms are slower than `F2` by design.

### Why a new analysis script

The frozen `context-features/analyze_arms.py` cannot judge these arms: it requires a baseline arm without feature groups, it
hard-codes the arm set of `arms.json` and it hard-codes a size guard of 2 x BASE, whereas this experiment has the baseline F2 (one
group), the arms F2+IMP and ALL+IMP, and a size guard of 1.5 x F2. The frozen script and its `arms.json` are **not** edited. The new,
versioned `imperative-data/analyze_imperative.py` is part of this pre-registration (its SHA-256 is below, `--selftest` passes). Its only
use of the frozen script is the function `welch_diff` (the Welch-Satterthwaite 95% interval), loaded from the frozen file by path
after the file's SHA-256 has been checked; every other function is its own. The self-test checks the t quantile of the frozen
function against published values, the Welch case by hand, the six guards at their boundaries and just beyond (including the size
guard at exactly 1.5 x the F2 mean, 1.5 x + 1 byte and 1.6 x), the 10% window (an arm at exactly 1.10 x the lowest is inside,
at 1.105 x it is outside), the preference of F2+IMP over ALL+IMP, the case that only ALL+IMP passes, the nothing-qualifies case, a
qualifying candidate whose interval contains 0, the no-fallback case, the reference arm that never enters the rule, missing runs and
a missing arm, each acceptance test failing when its condition is broken, and the paired bootstrap. A mutation check (size limit,
window and significance test changed) made the self-test fail as intended.

### Imperative data: metrics and the selection rule (fixed now; applied exactly as written; nothing is tuned afterwards)

A harmful error is a swapped noun-verb class at confidence >= 0.45 on a noun-verb decision; rates are per 1,000 decisions (the
definitions of E0 and of the shapes development set). All figures come from `analyze_imperative.py` over the five seeds of every
arm; spreads are sample standard deviations.

- **Primary metric:** the mean over the five seeds of the pooled harmful per 1k on the **hard tranche** of the shapes development
  set (`shapes.hard.pooled.harmfulPer1k`; four shapes, 400 target items). Lower is better.
- **Secondary metric (reported, not gated):** the hard `v-imperative-initial` target accuracy
  (`shapes.hard.shapes.v-imperative-initial.targetAccuracy`).
- **Guards** (arm against the **F2 five-seed mean**; all must hold): shapes-dev standard tranche harmful per 1k <= F2 + 5; open dev
  harmful per 1k <= F2 + 1.5; UD-EWT dev harmful per 1k <= F2 + 1.5; UD-EWT dev accuracy >= F2 - 0.15 points; deflated weights file size
  <= **1.5 x F2**; single-thread throughput >= 150,000 tokens per second (absolute; the median over the seeds of the runs' medians).
- **Rule.** Q = the arms F2+IMP and ALL+IMP that pass every guard. If Q is empty, F2 stays. Otherwise m = the lowest primary in Q,
  S = the arms of Q with primary <= 1.10 x m (within 10% relative), and the candidate is the arm of S with the fewest feature groups
  (so F2+IMP is preferred over ALL+IMP; ties: the lowest primary). The candidate ships only if the Welch 95% interval (Welch-Satterthwaite
  degrees of freedom) of (candidate - F2) on the primary lies **entirely below 0** (its upper bound is below 0); otherwise F2 stays and
  nothing ships. **There is no fallback** to the other arm. The reference arm ALL is never in Q.
- **Reported, not gated:** hard and standard target and control accuracy, open dev and gold v2 dev accuracy, NLL and ECE, temperature,
  rows, raw bytes, training seconds, the per-shape breakdown of both tranches (target accuracy and harmful per 1k: the hard shapes
  `n-compound-head`, `n-subject-initial`, `v-imperative-initial`, `v-after-modal` and the shapes of the standard tranche, so a shift of
  harm from `n-subject-initial` to `v-imperative-initial` is visible), the Welch interval of every arm against F2 for the secondary
  metric, and a paired bootstrap over the hard items of each candidate against F2 (2,000 resamples, seed 20261108, descriptive only).

### Acceptance tests (run before the rule is applied; a failure stops without a selection)

`analyze_imperative.py` runs them first and makes no selection (exit code 1, no `decision.json`) if one fails:

1. **F2 seed 20260101 is the shipped model.** Its weights SHA-256 (the `weights.sha256` of its result file, and the `.bin` file of the
   run when present) equals that of `src/DemaConsulting.PartOfSpeech/Resources/weights.bin`, and its figures equal those of the
   shipped model: hard tranche 235.0 harmful per 1k (94 of 400), standard tranche 16.7 (10 of 600), open dev 100 harmful of 4,451
   decisions, UD-EWT dev 91 of 4,570 (the development figures of the clause-cue adoption section).
2. **The source was really added.** From the line `Training on N sentences (...), sources=...` of the `<run>.train.txt` of every run:
   every run of F2+IMP and ALL+IMP trained on exactly **900 more** sentences than the F2 run of the same seed (900 = the sentences of
   `imperative.tsv` at weight 1) and lists `imperative:1` among its sources; every run of F2 and of the reference arm trained on the same
   number of sentences as the F2 run of its seed and lists no `imperative` source.
3. **Same inputs.** The `inputs.*Sha256` fields of all runs agree and equal the frozen hashes of the five development files
   (the table below).

The analysis script also checks the SHA-256 of every frozen file below (with CRLF normalized to LF) and stops if one differs.

### What happens on each outcome

- **F2 stays** (Q is empty, or the candidate's interval is not entirely below 0). The data source stays committed with weight 0, and
  the results section records the analysis tables unchanged, the decision "F2 stays" and the caveats. No weights change, no
  `thresholds`, no `evaluate`, no README accuracy change; the run log states that no held-out command was run (the held-out sets
  stay unread by this experiment).
- **A candidate ships.** An adoption section is written in this log **before any held-out command**: `training.json` gets
  `imperative` at weight 1 (and, for ALL+IMP, the four feature groups and the preview folds), with the steps of the clause-cue adoption
  (`train --output <file outside the repository>`, `calibrate --update-config`, `train --update-config`, `train --check`, copy to
  `weights.bin`; the new `weights.bin` must be byte-identical to the winning run at seed 20260101); then `thresholds` (development data
  only), and **one** `evaluate` of the new model as a **disclosed fourth look** at sets already seen by this model family (a new
  record, not an erratum; no second run; results reported as produced), with the documents, tables and drift tests updated.
- **An acceptance test fails.** The experiment stops without a selection; the cause is investigated and recorded, and any change
  to the protocol is an amendment written before the affected runs are repeated, never a choice made on a result.
- **A run is missing** (a command failed without a result): it is repeated; the analysis refuses to select with a missing run.

### Imperative data: limits stated in advance

- The seeds measure training noise only; the interval over the 400 hard items (the bootstrap) is far wider, so a win is development
  evidence of that kind. Two candidate arms are compared (less selection bias than the nine arms of the earlier experiment, but
  some).
- The hard tranche is a development set that this project has used for baselines and for the earlier selection; it is not an
  independent test of this idea. The source was built from the frame of the `v-imperative-initial` shape, so the shape is the
  direct target of the data.
- ALL+IMP also adds the three other feature groups; only F2 against F2+IMP isolates the effect of the data, which is why the
  reference arm ALL is run and reported.
- The data keep only sentences whose first word both labelers read as a verb: the source gives no counter-example, so a model that
  trains on it can lean toward the verb reading of an ambiguous first word (`n-subject-initial` is watched through the diagnostics
  and, indirectly, through the standard-tranche guard).
- The text is instructional federal text of another register and era (mostly scans of 1940 to 1969 manuals, 71% Army) than the
  development sets; the labels are model-written without human review (known residual error about 0.1% of tokens in the model review).
- Throughput and training time are measured on a loaded shared machine at Normal priority (the deviation record above).

### Imperative data: held-out sets

The held-out sets (open test, gold v2 test, shape probe, STE challenge set, UD-EWT test) are never used for selection and are not
read by this experiment: the harness reads only the development files, and `eval-arm` has no path to a held-out file. A held-out
command is run if and only if a candidate ships (above).

### Imperative data: inputs frozen by this commit

SHA-256 of the files as committed (line endings LF; the two `.cmd` files are checked out with CRLF by `.gitattributes` and are
hashed after normalizing to LF) and of the data they read:

| File | SHA-256 |
| --- | --- |
| `context-features/run_arms.ps1` (frozen driver, unchanged) | `26ba1c6ed281bdc86ac88522aaab76fd6490f10a753c5aa1dae96d83ff5995db` |
| `context-features/arms.json` (frozen arms and seeds, unchanged) | `0aac43677f11b3cbfa37a020344b6276483edbf8af7eee840893a43520e9fad8` |
| `context-features/analyze_arms.py` (frozen; imported for `welch_diff` only) | `3d52825384b440c5786e1b241fc6f814e4e47a63f155414e0d6450107d33e494` |
| `imperative-data/analyze_imperative.py` (new, versioned) | `c8a0a553ecdb636b0e28dea80a59d88aa7928a0dfaf61539bdcbae142065e0e1` |
| `imperative-data/runner-base.cmd` | `549a2c6ed8d1eaf00b30551fae14e546c51023dd79fe3a77810f39a1fe79ff3e` |
| `imperative-data/runner-imp.cmd` | `c60a7eac3ba369406710f7737926ac743c16346b7a9edd8d98bd8fea08b05f4b` |
| `data/imperative/imperative.tsv` (900 sentences) | `218246df0c4ce739e6631a066e5296194d6773e09fc849cd64c1eb93516185ad` |
| `data/imperative/sources.tsv` (122 documents) | `46ed6556224f2adcd73da7f8c8b026597a3a72f85dc7f6f8284231f003699df5` |
| `data/imperative/excluded-documents.tsv` | `e3a6f73dda9a089f9c04f99fd5b8d674b68cddf80c7aefc6e493394dd240bfe8` |
| `data/imperative/dropped-sentences.tsv` | `ff7667dbee716ed01fe2182c4497e301f48006ac63a18b560d347aeb22f2fddf` |
| `data/eval/shapes-dev/dev.tsv` (standard tranche) | `bbc94e42693d92a7e948598d5ea8a2c58274d3e4e5e6e4d0ed7d8a5c10963290` |
| `data/eval/shapes-dev/hard.tsv` (hard tranche) | `c041ae3d8b4b948326d002176bdffa0e5eb2ac0a6fe7d12daed7c3d289db8b22` |
| `data/open/dev.tsv` | `8caa822515cee179093f068ff4ea7fa62714830d4c50de696bd4403037fc05c7` |
| `data/eval/gold-v2/dev.tsv` | `ffb2f3afd3460523656f5528a66094bad507982b15eb2b4a9341cdb78ff117f5` |
| `tools/data/cache/ud-ewt/ud_dev.tsv` | `0fc0b4a371ef6d0437e01e396762cdb600d0d7ff0d725ead00ac9f3c41f82037` |
| `src/DemaConsulting.PartOfSpeech/Resources/weights.bin` (the shipped model; image `ec3f090eb318abc1bcf4dc8a88e401567e606932142178277d3ec387e0aa40f3`) | `72cc28cf10c556a617e8edb49d92d96148ed023874cfe834557ea5c6911b9672` |
| `tools/DemaConsulting.PartOfSpeech.Trainer/training.json` (with the weight-0 entry) | `3fbdd3dad34b86a422a2be854c6b9174842160d42b47d7d3bfc2d854e452652b` |

The data-side commits are `100e747cb0b31deff4f10d89b426001bf8c95f16` (builder, rule and tests, before any label) and
`3a703f52ad978ca040fc21252b67d1180d3b5019` (data, checks, documents and the weight-0 registration). `analyze_imperative.py --selftest`
passes. The trainer commit that produced each result is recorded in its JSON (`inputs.commit`, with the dirty flag), and the folders of
the runs lie outside the repository, as in the earlier experiment; their files are not part of the review set.

## Imperative training data: results

Written after the 20 runs and the analysis of the pre-registration commit `ca0c2d431e9b167e7ef7208add28cf0169a2f445` (the tree
was clean and HEAD was that commit when the runs started and when the analysis ran; every result file records
`inputs.commit` = `ca0c2d4...` with `dirty` = false). Four arms, five seeds each (20260101, 11, 12, 13, 14), 20 runs; the commands
were exactly those of the pre-registration (the frozen driver `run_arms.ps1` with `runner-base.cmd` or `runner-imp.cmd`, one
invocation at a time, in the registered order F2, F2+IMP, ALL+IMP, ALL); no run was repeated, replaced or dropped (every run
produced its result at the first attempt), and no frozen file was edited. The unchanged, frozen `analyze_imperative.py` was
applied to the 20 result files with the reference arm, and the selection rule was applied mechanically. No held-out set was read
and no held-out command (`evaluate`, `thresholds`, `train --update-config`) was run. The labeling disclosure of the data (the
refusals and retries of the labeler `gpt-6.1-sol`, with the batches concerned) is in `data/imperative/README.md` ("Refusals of one
labeler").

**Reproduction note.** The frozen runners, `run_arms.ps1` and the analysis encode the pre-registered state, in which `training.json`
carries `imperative` at weight 0: `runner-base.cmd` passes `train` unchanged, so it trained F2 and `runner-imp.cmd` (which appends
`--source imperative:1`) trained F2+IMP. The published comparison is reproducible from the commands above only at the
pre-registration commit `ca0c2d4` (the registered `training.json`, whose hash the analysis checks). At the adopted state
(`training.json` with weight 1, the commits after the decision) `runner-base.cmd` trains F2+IMP as well, so base and imperative runs
would both train on the 900 sentences; the F2 arm at that state needs `--source imperative:0` on a direct `train` command, which
the frozen runners do not add. No frozen file was edited for this.

**Decision: the rule selects F2+IMP** (the arm F2 with the `imperative` source at weight 1). Both IMP arms pass every guard, so
Q = {F2+IMP, ALL+IMP}; the lowest primary is F2+IMP (179.0); ALL+IMP (179.5) is inside the 10% window (<= 196.9); the candidate is
the arm with the fewest feature groups, F2+IMP; the Welch 95% interval of (F2+IMP - F2) on the primary is -55.00 [-69.09, -40.91]
(df 6.55), entirely below 0. The adoption steps are not part of this section; they follow in an adoption section written before any
held-out command.

| File | SHA-256 |
| --- | --- |
| `report.md` (the analysis; 13,514 bytes) | `7a01287901eebc44468501a5900242d62c983dbec05e74431513e55acf88b6d3` |
| `decision.json` (per-arm values, intervals, guards, decision) | `23e366491dbfd6fc9c0633893796b8fc018baca4c36d495e8fc4b3518d9f0dbf` |

Both files lie outside the repository next to the run folders. The command was:

```text
python tools/DemaConsulting.PartOfSpeech.Trainer/imperative-data/analyze_imperative.py --run F2=<runs>\f2 --run F2+IMP=<runs>\f2imp --run ALL+IMP=<runs>\allimp --reference ALL=<runs>\all-ref --out report.md --decision decision.json
```

### Acceptance tests (all passed; the analysis made a selection only after them)

39 checks, 39 PASS: the SHA-256 of the six frozen files; (a) the F2 run at seed 20260101 has weights SHA-256
`72cc28cf10c556a617e8edb49d92d96148ed023874cfe834557ea5c6911b9672`, equal to the shipped `weights.bin` (file and result field, byte
identical), and the figures of the shipped model: hard tranche 235.0 harmful per 1k, standard 16.7, open dev 100 of 4,451, UD-EWT dev
91 of 4,570; (b) every F2 and every reference-arm run trained on 60,780 sentences with the sources `open:2,masc:1,silver:1`, and every
F2+IMP and ALL+IMP run on 61,680 sentences (exactly 900 more) with the sources `open:2,masc:1,silver:1,imperative:1`; (c) the five
`inputs.*Sha256` fields agree in all 20 runs and equal the frozen hashes. The F2 arm therefore reproduces the shipped model at seed
20260101, and the seed variation of the other seeds is the training noise.

### Imperative results: shapes development set

Values are mean ± sample sd over the five seeds.

| arm | hard harmful per 1k (primary) | hard v-imperative-initial target acc % (secondary) | hard target acc % | hard control acc % | standard harmful per 1k | standard target acc % | standard control acc % |
| --- | --- | --- | --- | --- | --- | --- | --- |
| F2 | 234.0 ± 11.3 | 51.6 ± 4.3 | 69.80 ± 1.39 | 98.00 ± 0.75 | 16.0 ± 3.5 | 97.23 ± 0.43 | 98.80 ± 0.45 |
| F2+IMP | 179.0 ± 6.8 | 77.4 ± 3.0 | 76.50 ± 0.92 | 98.00 ± 0.75 | 13.3 ± 3.5 | 97.73 ± 0.47 | 99.00 ± 0.00 |
| ALL+IMP | 179.5 ± 7.4 | 83.0 ± 1.2 | 77.10 ± 1.01 | 98.33 ± 0.00 | 15.0 ± 2.0 | 97.50 ± 0.12 | 99.00 ± 0.00 |
| ALL (reference) | 221.0 ± 14.4 | 57.6 ± 4.3 | 71.35 ± 1.63 | 98.33 ± 0.00 | 13.0 ± 1.4 | 97.77 ± 0.19 | 99.00 ± 0.00 |

### Imperative results: open dev and gold v2 dev

| arm | open acc % | open ambiguous acc % | open harmful per 1k | open NLL | open ECE % | gold acc % | gold harmful per 1k | gold NLL | gold ECE % |
| --- | --- | --- | --- | --- | --- | --- | --- | --- | --- |
| F2 | 96.64 ± 0.05 | 95.98 ± 0.05 | 20.67 ± 1.02 | 0.1123 ± 0.0012 | 0.26 ± 0.02 | 94.98 ± 0.13 | 23.2 ± 2.1 | 0.2571 ± 0.0031 | 2.36 ± 0.09 |
| F2+IMP | 96.67 ± 0.06 | 96.02 ± 0.08 | 20.09 ± 1.39 | 0.1117 ± 0.0005 | 0.25 ± 0.02 | 95.15 ± 0.10 | 22.3 ± 0.9 | 0.2546 ± 0.0045 | 2.23 ± 0.14 |
| ALL+IMP | 96.83 ± 0.05 | 96.24 ± 0.08 | 19.55 ± 0.94 | 0.1078 ± 0.0004 | 0.24 ± 0.04 | 95.22 ± 0.11 | 20.3 ± 1.9 | 0.2563 ± 0.0023 | 2.26 ± 0.12 |
| ALL (reference) | 96.77 ± 0.05 | 96.18 ± 0.06 | 19.19 ± 0.85 | 0.1088 ± 0.0008 | 0.29 ± 0.05 | 95.11 ± 0.14 | 21.3 ± 2.0 | 0.2549 ± 0.0021 | 2.26 ± 0.18 |

### Imperative results: UD-EWT dev

| arm | UD acc % | UD harmful per 1k |
| --- | --- | --- |
| F2 | 92.943 ± 0.059 | 19.91 ± 1.54 |
| F2+IMP | 92.988 ± 0.026 | 20.74 ± 0.32 |
| ALL+IMP | 93.077 ± 0.072 | 19.96 ± 0.78 |
| ALL (reference) | 93.013 ± 0.086 | 19.65 ± 1.22 |

### Imperative results: model, speed and training

The training times and the throughput were measured on a shared machine at Normal priority (the deviation record of the
pre-registration) and are not comparable between arms or with earlier records.

| arm | temperature | rows | raw bytes | deflated bytes | tokens/s (1 thread) | train wall s |
| --- | --- | --- | --- | --- | --- | --- |
| F2 | 9.473 ± 0.091 | 120981 ± 221 | 1146923 ± 1964 | 517282 ± 696 | 772197 ± 41861 | 23.6 ± 1.0 |
| F2+IMP | 9.392 ± 0.112 | 122214 ± 264 | 1158701 ± 2142 | 522272 ± 991 | 749742 ± 60155 | 24.2 ± 1.8 |
| ALL+IMP | 12.608 ± 0.000 | 133991 ± 128 | 1317341 ± 1921 | 605679 ± 759 | 429935 ± 4335 | 104.0 ± 28.2 |
| ALL (reference) | 12.663 ± 0.123 | 132396 ± 114 | 1302049 ± 1044 | 599190 ± 650 | 389661 ± 49616 | 133.7 ± 13.3 |

### Hard tranche per shape (diagnostics, not gated)

| hard: target accuracy % | n-compound-head | n-subject-initial | v-after-modal | v-imperative-initial |
| --- | --- | --- | --- | --- |
| F2 | 62.2 ± 2.9 | 78.8 ± 2.3 | 86.6 ± 1.5 | 51.6 ± 4.3 |
| F2+IMP | 66.2 ± 2.2 | 75.6 ± 3.1 | 86.8 ± 1.3 | 77.4 ± 3.0 |
| ALL+IMP | 67.0 ± 2.3 | 67.6 ± 1.1 | 90.8 ± 0.8 | 83.0 ± 1.2 |
| ALL (reference) | 66.0 ± 5.4 | 72.6 ± 4.2 | 89.2 ± 1.9 | 57.6 ± 4.3 |

| hard: harmful per 1k | n-compound-head | n-subject-initial | v-after-modal | v-imperative-initial |
| --- | --- | --- | --- | --- |
| F2 | 332.0 ± 27.7 | 114.0 ± 19.5 | 104.0 ± 11.4 | 386.0 ± 30.5 |
| F2+IMP | 296.0 ± 13.4 | 136.0 ± 30.5 | 98.0 ± 14.8 | 186.0 ± 20.7 |
| ALL+IMP | 284.0 ± 20.7 | 206.0 ± 11.4 | 78.0 ± 4.5 | 150.0 ± 14.1 |
| ALL (reference) | 296.0 ± 51.8 | 170.0 ± 30.8 | 88.0 ± 19.2 | 330.0 ± 32.4 |

### Standard tranche per shape (diagnostics, not gated)

| standard: target accuracy % | n-compound-head | n-measure | n-subject-initial | v-after-modal | v-imperative-initial |
| --- | --- | --- | --- | --- | --- |
| F2 | 96.7 ± 0.6 | 98.7 ± 1.0 | 96.0 ± 0.7 | 98.7 ± 1.0 | 96.2 ± 1.3 |
| F2+IMP | 96.8 ± 0.9 | 99.0 ± 1.1 | 95.5 ± 0.5 | 98.7 ± 0.7 | 98.7 ± 0.5 |
| ALL+IMP | 96.5 ± 0.4 | 98.7 ± 0.5 | 93.8 ± 0.5 | 99.5 ± 0.5 | 99.0 ± 0.4 |
| ALL (reference) | 96.7 ± 0.6 | 98.8 ± 0.7 | 96.2 ± 1.0 | 99.7 ± 0.5 | 97.5 ± 1.0 |

| standard: harmful per 1k | n-compound-head | n-measure | n-subject-initial | v-after-modal | v-imperative-initial |
| --- | --- | --- | --- | --- | --- |
| F2 | 16.7 ± 5.9 | 1.7 ± 3.7 | 20.0 ± 4.6 | 13.3 ± 9.5 | 28.3 ± 9.5 |
| F2+IMP | 15.0 ± 9.1 | 0.0 ± 0.0 | 26.7 ± 7.0 | 13.3 ± 7.5 | 11.7 ± 4.6 |
| ALL+IMP | 18.3 ± 3.7 | 0.0 ± 0.0 | 41.7 ± 10.2 | 5.0 ± 4.6 | 10.0 ± 3.7 |
| ALL (reference) | 18.3 ± 3.7 | 0.0 ± 0.0 | 23.3 ± 7.0 | 3.3 ± 4.6 | 20.0 ± 4.6 |

### Welch 95% interval of (arm - F2)

Difference of the means with the Welch-Satterthwaite t interval over the seeds (seeds measure training noise only).

| arm | shapes.hard.pooled.harmfulPer1k | shapes.hard.shapes.v-imperative-initial.targetAccuracy | shapes.standard.pooled.harmfulPer1k | openDev.harmfulPer1k | udDev.harmfulPer1k | udDev.accuracy |
| --- | --- | --- | --- | --- | --- | --- |
| F2+IMP | -55.00 [-69.09, -40.91] | +25.80 [+20.31, +31.29] | -2.67 [-7.77, +2.43] | -0.58 [-2.39, +1.22] | +0.83 [-1.06, +2.72] | +0.05 [-0.03, +0.12] |
| ALL+IMP | -54.50 [-68.78, -40.22] | +31.40 [+26.17, +36.63] | -1.00 [-5.31, +3.31] | -1.12 [-2.55, +0.31] | +0.04 [-1.85, +1.94] | +0.13 [+0.04, +0.23] |
| ALL (reference) | -13.00 [-32.07, +6.07] | +6.00 [-0.28, +12.28] | -3.00 [-7.22, +1.22] | -1.48 [-2.86, -0.11] | -0.26 [-2.31, +1.78] | +0.07 [-0.04, +0.18] |

### Guards against F2

Delta guards use the mean over the seeds; the size guard is 1.5 x the F2 mean; the throughput guard uses the median of the seeds'
medians. The reference arm is shown for information; it is never in Q.

| arm | standard harmful per 1k <= F2 + 5 | open dev harmful per 1k <= F2 + 1.5 | UD-EWT dev harmful per 1k <= F2 + 1.5 | UD-EWT dev accuracy >= F2 - 0.15 | deflated bytes <= 1.5 x F2 | tokens/s >= 150,000 | all guards |
| --- | --- | --- | --- | --- | --- | --- | --- |
| F2+IMP | PASS -2.67 | PASS -0.58 | PASS +0.83 | PASS +0.05 | PASS 1.010x | PASS 755888 | PASS |
| ALL+IMP | PASS -1.00 | PASS -1.12 | PASS +0.04 | PASS +0.13 | PASS 1.171x | PASS 431875 | PASS |
| ALL (reference) | PASS -3.00 | PASS -1.48 | PASS -0.26 | PASS +0.07 | PASS 1.158x | PASS 408856 | PASS |

### Selection rule applied

| arm | groups | primary | passes all guards | role |
| --- | --- | --- | --- | --- |
| F2 | 1 | 234.00 ± 11.26 | - | baseline |
| F2+IMP | 1 | 179.00 ± 6.75 | yes | candidate |
| ALL+IMP | 4 | 179.50 ± 7.37 | yes | candidate |
| ALL (reference) | 4 | 221.00 ± 14.43 | yes | reference (not gated) |

Q = {F2+IMP, ALL+IMP}; lowest primary in Q: F2+IMP (179.00); within 10% (primary <= 196.90): F2+IMP, ALL+IMP; candidate (fewest
groups, then lowest primary): F2+IMP; Welch 95% interval of (F2+IMP - F2): -55.00 [-69.09, -40.91], df 6.55, p < 0.0001.
**SHIP F2+IMP.**

### Descriptive: paired item bootstrap on the hard tranche (not part of the rule)

2,000 resamples of the 400 hard items, seed 20261108, items averaged over the seeds:

- F2+IMP - F2 harmful per 1k: -55.00, 95% percentile interval [-78.50, -32.00].
- ALL+IMP - F2 harmful per 1k: -54.50, 95% percentile interval [-84.00, -25.00].
- ALL (reference) - F2 harmful per 1k: -13.00, 95% percentile interval [-33.00, +7.51].

### Per-seed values (hard harmful per 1k / hard v-imperative-initial target accuracy % / hard n-subject-initial target accuracy %)

| arm | 20260101 | 11 | 12 | 13 | 14 |
| --- | --- | --- | --- | --- | --- |
| F2 | 235.0 / 45 / 82 | 240.0 / 53 / 80 | 217.5 / 56 / 78 | 230.0 / 54 / 78 | 247.5 / 50 / 76 |
| F2+IMP | 170.0 / 78 / 79 | 182.5 / 77 / 74 | 187.5 / 76 / 73 | 180.0 / 74 / 79 | 175.0 / 82 / 73 |
| ALL+IMP | 190.0 / 82 / 67 | 172.5 / 85 / 68 | 172.5 / 83 / 69 | 182.5 / 82 / 68 | 180.0 / 83 / 66 |
| ALL (reference) | 245.0 / 51 / 74 | 220.0 / 59 / 74 | 220.0 / 57 / 70 | 212.5 / 58 / 78 | 207.5 / 63 / 67 |

Every F2+IMP seed (170.0 to 187.5) is below every F2 seed (217.5 to 247.5), and so is every ALL+IMP seed (172.5 to 190.0).

### Run times

Training wall seconds per run (including the jackknife and the calibration): F2 22.5 to 25.1 s (whole arm with evaluation 2 min 9 s);
F2+IMP 23.0 to 27.4 s (2 min 15 s); ALL+IMP 64.5 to 134.3 s (8 min 54 s); ALL reference 120.4 to 150.0 s (11 min 23 s). The
`eval-arm` of every run took 1.6 to 2.7 s.

### Caveats (read with the limits stated in advance)

- The seeds measure training noise only. The interval of the paired item bootstrap over the 400 hard items is wider
  ([-78.5, -32.0] for F2+IMP) but also lies below 0. This is development evidence on a set that the project has used before, and the
  source was built from the frame of the `v-imperative-initial` shape, which is the shape that gains (target accuracy 51.6% to
  77.4%, harmful 386 to 186 per 1k); it is the direct target of the data, not an independent test of the idea.
- The gain on the other three hard shapes is small or none: `n-compound-head` harm 332 to 296, `v-after-modal` 104 to 98, and
  `n-subject-initial` target accuracy fell from 78.8 to 75.6 (harmful per 1k 114 to 136, within the seed spread). The data give
  no counter-example, as stated in advance, and the effect is larger in ALL+IMP (n-subject-initial target accuracy 67.6%, harmful
  206 per 1k; on the standard tranche 41.7 per 1k against 20.0 at F2), a shift of harm from `v-imperative-initial` to
  `n-subject-initial` that the rule did not gate (it entered only through the standard-tranche guard, which every arm passes).
  For F2+IMP the standard `n-subject-initial` harm is 26.7 per 1k against 20.0 (within about one seed sd).
- Only F2 against F2+IMP isolates the effect of the data; the reference arm ALL (221.0, interval [-32.07, +6.07] against F2) shows
  that the three other feature groups do not by themselves lower the primary below F2, so the gain of ALL+IMP is the gain of the data.
- The labels are model-written without human review (model review: 59/60 sentences, token precision 99.87%); the text is
  instructional federal text of another register and era than the development sets.
- Throughput and training time were measured on a loaded shared machine at Normal priority.
- The run folders, `report.md` and `decision.json` lie outside the repository; the figures above are copied unchanged from
  `report.md` (the per-seed table is read from the 20 result files).
- No held-out set was read; no weights, thresholds, `training.json` or documents were changed by this slice. The adoption (the
  `imperative` source at weight 1 in `training.json`, `train`/`calibrate` with `--update-config`, `weights.bin`, `thresholds`
  and the single held-out `evaluate`) is the next section and is written before any held-out command.

## Imperative training data: adoption (plan written before the retraining and before any held-out command)

The pre-registered rule selected **F2+IMP** (results commit `742258d46f8b15639d4a6cd2d613b9825f96baf1`), and the plan of the project
owner is to ship it. This section is committed **before the retraining of the shipped model**: no `train --output`,
`calibrate --update-config`, `train --update-config`, `thresholds` or `evaluate` has been run for it, and the held-out sets are not
read. Git history shows the order: this plan, then the adoption commit (the weights, the configuration, the operating points and the
re-pinned figures; development data only), then the deletion of the previous held-out record, the one `evaluate`, and the evaluation
record. No hyperparameter, corpus, feature group or seed changes: the shipped seed 20260101 stays, and the model is the arm F2 of
the experiment with one more training source.

### The arm and the exact steps

The adopted arm is **F2+IMP**: the F2 configuration (feature group `clauseCues`, the mix open x2, MASC, silver >= 80, 6 epochs, 19
hash bits, prune 8, quantization scale 8, restriction `history`, predicted history, seed 20260101) plus the source `imperative`
(`data/imperative/imperative.tsv`, 900 sentences) at **weight 1**. F2 features are unchanged. The steps, as in the clause-cue
adoption, from a clean tree whose HEAD is the commit of this plan:

1. In `tools/DemaConsulting.PartOfSpeech.Trainer/training.json` set the weight of the source `imperative` from 0 to 1 (nothing else).
2. `train --output <file outside the repository>` at the shipped seed (the trainer reads `training.json`).
3. `calibrate --weights <file> --update-config` (refits the temperature on the open dev set and writes it to `training.json`).
4. `train --update-config` (writes the image SHA-256 of the new model to `training.json`), then `train --check` (must exit 0).
5. Copy the file to `src/DemaConsulting.PartOfSpeech/Resources/weights.bin`.
6. `thresholds` (development data only; recomputes `docs/evaluation/operating-points.json` and `confidence-thresholds.md`; it
   refuses to write anything if the 0.45 row does not reproduce the frozen figures `FrozenOpenHarmful` and `FrozenGoldHarmful`;
   if it refuses, the two constants are set to the figures it reports for the new model, with the reason recorded, as in the
   clause-cue adoption, and it is run again).
7. Re-pin the figures and tests that depend on the weights (size figures, the embedded-model tests, the documents that describe the
   shipped model).

One heavy process runs at a time, at most 8 threads. `evaluate` is **not** run in the adoption commit.

### Byte-identity requirement (stated before the retraining)

The new `weights.bin` must be **byte-identical to the weights of the F2+IMP run of seed 20260101 of the experiment**
(`runs\f2imp\F2-s20260101.bin`, outside the repository). Its SHA-256 is
`48dd9188c37fb0ba01458e45a28d7b4b66d6a14b9f57eaf2a621bddc0e9b413b`, its size 521,695 bytes deflated (1,157,499 bytes raw, 122,088 rows,
image SHA-256 `6e1e4ade56aa277d79709c107000f10e99c6f4e55a097e872a9c38fa2e12e8d0`, per the run's training output). The model it
replaces has SHA-256 `72cc28cf10c556a617e8edb49d92d96148ed023874cfe834557ea5c6911b9672` (517,210 bytes; image `ec3f090e...`).
If the retrained file is **not** byte-identical, the adoption stops: nothing is committed except an account of the difference,
the cause is investigated and recorded, and no figure of the experiment is claimed for the shipped model.

### What stays the same and what will be written afterwards

- The data, the labels, the experiment's decision and the experiment's tables are not changed by the adoption.
- The seed-20260101 figures of the experiment (hard tranche 170.0 harmful per 1k, `v-imperative-initial` target accuracy 78%) are
  the figures of the shipped model if and only if the byte identity holds.
- The adoption commit adds the development figures of the shipped model, the `thresholds` result and the list of the re-pinned
  figures to this section (development data only). The held-out evaluation is a **disclosed fourth look** at sets already seen by this
  model family, run once, as a new record (not an erratum), reported as produced, and is the step that follows the adoption commit.

### Adoption result (written by the adoption commit; development data only)

The steps ran in the registered order from a clean tree at the commit of the plan above (`13e2b1a`), one process at a time at
8 threads at most. The trainer commands wrote their output under the working folder outside the repository.

- **`train --output <file>`** (seed 20260101; 61,680 sentences, sources `open:2,masc:1,silver:1,imperative:1`, 6,357,228 steps, 8.4 s):
  image 1,157,499 bytes, 122,088 rows, 304,425 weights, image SHA-256 `6e1e4ade56aa277d79709c107000f10e99c6f4e55a097e872a9c38fa2e12e8d0`,
  521,695 bytes deflated, file SHA-256 `48dd9188c37fb0ba01458e45a28d7b4b66d6a14b9f57eaf2a621bddc0e9b413b`.
- **`calibrate --weights <file> --update-config`**: best temperature on open dev **9.514** (NLL 0.11245, ECE 0.276%; gold v2 dev
  NLL 0.2498, ECE 2.44%), the same value as before, so `training.json` kept its temperature.
- **`train --update-config`** wrote `imageSha256` `6e1e4ade...e8d0` (it was `ec3f090e...`) and wrote `weights.bin` itself (the
  trainer's default output is the embedded file, so the copy of the plan's step 5 is this write); **`train --check` exit 0**.
- **Byte identity: confirmed.** `src/DemaConsulting.PartOfSpeech/Resources/weights.bin` has SHA-256
  `48dd9188c37fb0ba01458e45a28d7b4b66d6a14b9f57eaf2a621bddc0e9b413b` and 521,695 bytes, equal to the file written by
  `train --output`, to the weights of the F2+IMP run of seed 20260101 of the experiment and to the `weights.sha256` of that run's
  result file. The model it replaces had SHA-256 `72cc28cf10c556a617e8edb49d92d96148ed023874cfe834557ea5c6911b9672` (517,210 bytes).
  The seed-20260101 figures of the experiment are therefore the figures of the shipped model.

#### Imperative adoption: development figures of the shipped model (dev data only)

Seed 20260101, scored by `eval-arm` through the real decoder (the figures of the experiment's run `F2-s20260101` of the arm F2+IMP,
which is this model; the previous model is the F2 run of the same seed, which was the shipped model before this adoption). The shapes
development set is a development set for this selection and not an independent test.

| Measure | Previous model (F2) | Shipped model (F2+IMP) |
| --- | --- | --- |
| Hard tranche harmful per 1k (400 target items) | 235.0 (94) | 170.0 (68) |
| Hard tranche target / control accuracy % | 69.50 / 98.33 | 77.25 / 98.33 |
| Hard `v-imperative-initial` target accuracy %, harmful per 1k (100 target items) | 45.0, 430.0 | 78.0, 170.0 |
| Hard `n-subject-initial` target accuracy %, harmful per 1k (100 target items) | 82.0, 90.0 | 79.0, 110.0 |
| Standard tranche harmful per 1k (600 target items) | 16.7 (10) | 15.0 (9) |
| Standard tranche target / control accuracy % | 96.83 / 99.00 | 97.83 / 99.00 |
| Open dev accuracy % (ambiguous %) | 96.65 (95.98) | 96.57 (95.90) |
| Open dev harmful per 1k (of 4,451 decisions) | 22.47 (100) | **20.44 (91)** |
| Open dev NLL, ECE % | 0.1127, 0.24 | 0.1125, 0.28 |
| Gold v2 dev accuracy % | 94.84 | 95.04 |
| Gold v2 dev harmful per 1k (of 611 decisions) | 26.19 (16) | **22.91 (14)** |
| Gold v2 dev NLL, ECE % | 0.2577, 2.38 | 0.2498, 2.44 |
| UD-EWT dev agreement % (25,148 tokens) | 92.950 | 92.962 |
| UD-EWT dev harmful per 1k (of 4,570 decisions) | 19.91 (91) | 20.57 (94) |
| Temperature | 9.514 | 9.514 |
| Rows, weights | 121,033, 301,571 | 122,088, 304,425 |
| Raw, deflated bytes | 1,146,827; 517,210 | 1,157,499; 521,695 |

Throughput was not measured again for the adoption: the experiment recorded 771,457 tokens per second for this model's run (one
thread, a loaded shared machine; the five-seed median of the arm was 755,888), against the floor of 150,000. **What is worse
(stated before the held-out evaluation).** (1) The shipped model is 4,485 bytes larger when deflated (+0.9%). (2) UD-EWT dev harmful
errors are 3 higher (94 against 91; the five-seed difference of the experiment, +0.83 per 1k, has an interval that contains 0).
(3) `n-subject-initial` is worse in the experiment (hard harmful per 1k 114 to 136 and standard 20.0 to 26.7 as five-seed means; at
this seed the hard target accuracy is 79% against 82% and the harmful rate 110 against 90 per 1k): the source gives no counter-example, as the pre-registration said.
(4) Open dev accuracy is 0.08 points lower at this seed (the five-seed means are 96.64 and 96.67). The gain is on the hard shapes of
a development set that is the direct target of the data, so it is development evidence and not a held-out result.

#### Imperative adoption: `thresholds` result (development data only; committed before the held-out run)

`thresholds` (rule `nv-pooled-dev-v1`, grid, targets and floors unchanged) **refused to write** with the old constants
(`At 0.45: harmful 91 (open dev), 14 (gold v2 dev), 105 (pooled); frozen figures 100, 16, 116`; nothing was written). The reason
is that the model changed: the 0.45 row of the frozen figures is the development figure of the shipped model, and the new model's
figures equal those of the experiment's `eval-arm` (open 91, gold 14). The two constants `FrozenOpenHarmful` and `FrozenGoldHarmful`
of the trainer were set to the figures that `thresholds` reported (**91** and **14**; they were 100 and 16), the comment of each
constant records the history, and the command was run again: the 0.45 row reproduced the figures and the command wrote
`docs/evaluation/operating-points.json` and `docs/evaluation/confidence-thresholds.md`. The disclosure paragraph
(`docs/evaluation/disclosure.md`) was revised for the **fourth look** (five models in all scored on the held-out sets) before the
run that wrote the files, and the command was run a third time after that revision (the operating points are byte-identical to the
second run; only the disclosure paragraph of the generated report changed).

For the model image `6e1e4ade...`, temperature 9.514 and the dev files unchanged: **strict 0.95** (coverage 81.8% [80.4, 83.2], 1.9
harmful per 1,000 answered [0.5, 3.8], 8 of 4,139) and **balanced 0.75** (coverage 94.1% [93.2, 94.9], 9.9 [6.7, 13.3], 47 of
4,762). The previous model had strict 0.95 (coverage 81.3%, 3.2 per 1,000, 13 of 4,116) and balanced 0.75 (coverage 94.1%, 9.9, 47
of 4,761): **the thresholds are unchanged** (0.95 and 0.75), so the guidance lines of the documents keep their thresholds, and only
their development coverage and harmful figures changed. The pooled harmful count at 0.45 is 105 of 5,062 decisions (20.7 per 1,000;
it was 116, 22.9).

#### Pinned figures and documents re-pinned by the adoption commit

The size figures (521,695 bytes compressed, 1,157,499 uncompressed, 122,088 rows, 304,425 weights; about 0.5 MB and 1.2 MB) in the
user guide, the packed API reference (`NamespaceDoc.cs`), the trainer README, the design documents of the system, of
`ModelStorage`, `PerceptronModel` and `ModelTrainer`, and the system verification document; the development figures of the
shipped model in the "Selection of the Shipped Model" section of the `ModelTrainer` design (a row for the imperative source, open
and gold development accuracy, pooled harmful rate, calibration error, step count); the development coverage and harmful figures of
the two thresholds in the user guide and in the packed API reference; the weight of the source `imperative` (0 to 1) in
`training.json`, in the configuration test (`ConfigFile_Parse_CommittedConfiguration_ValidAndCanonical`: all four sources have a
positive weight) and in every document that described it as "weight 0" (data READMEs, the trainer README, the design and
verification documents of `ModelTrainer`, the requirements text of `PartOfSpeech-ModelTrainer-SourceWeights` and
`PartOfSpeech-ModelTrainer-Configuration`); the third-party notice, the package license and the root README, which now name the
imperative source among the corpora the weights were learned from (and the notice test, which expects "weight 1" and "were learned
from it"). Nothing in the experiment's frozen files (`analyze_imperative.py`, the runners, the pre-registration, the results
section) changed: the analysis script's acceptance test (a), which compares the F2 run with the shipped weights, holds only at the
pre-registration commit, as the trainer README now says.

#### Pending for the held-out step (not changed by the adoption commit)

The committed held-out record (`docs/evaluation/test-results.json` and `.md`) is still the record of the previous model, and the
README tables are still its figures. These tests and figures follow the one `evaluate` of the next step and are not edited by
hand: `PartOfSpeech_Evaluation_Results_RecordTheShippedModelAndInputs` (the record names the previous image and selection hash),
`DataGuard_CommittedReports_CarryCommittedDisclosureWhereGeneratorPutsIt` for `test-results.md` (it still carries the third-look
paragraph; `confidence-thresholds.md` already carries the fourth-look one), the held-out figures of the two README tables, the
held-out figures of the guidance in the user guide and in `NamespaceDoc.cs` (test coverage and harmful rate of the two points, the
rate without a threshold, the interval, the gold v2 and probe sentences), the prose of `docs/evaluation/README.md` (the third-look
statement, the previous-model comparison, every held-out figure) and the statement of `docs/design/part-of-speech.md` about the
record.

## Imperative training data: held-out look (plan written before the held-out command)

The project owner authorized **exactly one** held-out `evaluate` for the shipped F2+IMP model, to be disclosed as a **further look**
(the fourth look of the family at these sets; five models in all have been scored on them), as a **new record and not an erratum**.
This section is committed **before** the command: no `evaluate` (not even `--preflight`) has been run for this model, and no held-out
file has been read. The adoption section above is the state of the development evidence (HEAD `e969afa`).

Plan (the procedure of the clause-cue held-out step, followed exactly):

1. The committed previous record (`docs/evaluation/test-results.md` and `.json`, `operating-points.json`,
   `confidence-thresholds.md`, `README.md`, `disclosure.md`, all as of `e969afa`; `test-results.*` is the record of the clause-cue
   model, last written by commit `5f9a9f0`) is copied to a folder outside the repository for the comparison. It stays in git history.
2. `test-results.json` and `.md` are deleted in a commit of their own (`evaluate` refuses to run while one exists).
3. `evaluate --preflight` (scores nothing), then `evaluate --dump-dir <folder outside the repository>` **once** (at most 8
   threads, the treebank from the git-ignored cache). If the command fails for an infrastructure reason before it produces a result,
   the failure is reported precisely and nothing is retried to tune anything; **no second run is made after a result exists and
   there is no re-roll**. The stop conditions that fired in the earlier records (gold v2 `to`/`not`, UD-EWT raw gold outside the
   candidates, the shape probe's harmful errors) may fire again; they are reported, not tuned.
4. The results are committed **as produced**, whatever they are, including any regression against the previous model. The previous
   model is shown beside the new one from its committed record (it is not evaluated again). Between these two models every held-out
   set has the same labels, so all five are fair comparisons; intervals are the 95% percentile bootstrap over documents of each
   record, no paired interval is computed, so overlapping intervals are not a test.
5. What is disclosed: `docs/evaluation/disclosure.md` (the fourth look; already revised in the adoption commit), the overview
   `docs/evaluation/README.md` (status, procedure, comparison with the previous and earlier models), the README tables and the
   guidance of the user guide and of the packed API reference (generated figures copied by tests-guarded rules), and the record
   section that follows this one. Nothing is selected, tuned or reverted because of the result. The known weaker shape,
   `n-subject-initial`, which the pre-registration and the adoption listed as worse on development data, is reported as it appears
   in the shape-probe rows.

## Imperative training data: held-out evaluation of the shipped F2+IMP model (disclosed fourth look; a new record, not an erratum)

This is a **new** record for the shipped model. It does not correct or replace the earlier ones ("Outcome of the held-out run" and
its errata, "Corpus cleanup C: held-out evaluation of the retrained model" for FULL, "Corpus cleanup C: held-out evaluation of the
adopted NO-ING model" and "Context-feature experiment: held-out evaluation of the shipped clause-cue model"), which stay as written.
Order (git history): the pre-registration, the results, the adoption plan and the adoption (`e969afa`, development data only), the
plan of this look (`349377e`), the deletion of the previous record on purpose (`2ed2bd4`; `evaluate` refuses to run while a record
exists; the previous record stays in git at `5f9a9f0` and was copied outside the repository first), the one `evaluate` and its
generated record (`e1a23f7`), and this section with the documents after it.

**This is the fourth look of the family at these held-out sets** (five models have now been scored on them; see the disclosure
`docs/evaluation/disclosure.md`). `evaluate --preflight` passed first (it scores nothing); `evaluate --dump-dir <folder outside the
repository>` then ran **once** (`DOTNET_PROCESSOR_COUNT=8`, the treebank from the git-ignored cache). It exited with 1 because of the
stop conditions below (findings, not failures of the run) and the committed files are the output as produced. **No second `evaluate`
run was made and nothing was re-rolled.** Nothing was selected, tuned or reverted because of any result. The previous model (the
clause-cue model, the record at `5f9a9f0`) was not evaluated again: its figures below are those of its committed record.

Stop conditions of the protocol: **two fired** (the same two as in all earlier records; the third, the shape probe, did not fire
this time):

- `gold-v2-test-to-not`: gold outside the candidates for 50.00% of the 8 targets (the same); the `to`/`not` convention difference.
- `ud-test-raw`: gold outside the candidates for 1.93% (484 of 25,094 words) (the same). The primary mapped view is at 0.55% (138
  words) (the same), under the limit.
- `shape-probe`: **did not fire**: 27 harmful errors at 0.45 against a limit of 28 (5% of 560 rows); the previous model had 37 and the
  earlier models 48 and more. The margin is one error.

All other stop conditions held (the open test accuracy 96.75% against 96.65% on development; harmful rates per 1,000 answered at the
strict (2.4) and balanced (7.5) points on open test below twice the pooled development rates (1.9 and 9.9); open test ECE 0.25%; open
test gold outside the candidates 41 of 26,185 tokens).

**What is comparable (stated plainly).** The previous model and the shipped one were scored on the same labels on all five held-out
sets and the six input hashes are identical in the two records (the data did not change), so all five are fair comparisons between
them. Intervals are the 95% percentile bootstrap over documents of each record (1,000 resamples, seed 1); no paired interval between
the models was computed, so overlapping intervals are not a test: the intervals overlap on every row below. The sets are small and
the shipped model is one seed.

Accuracy and harmful errors at the harmful limit 0.45, previous model (clause-cue) and shipped model (F2+IMP):

| Set | Model | Accuracy % | Ambiguous accuracy % | Decisions | Harmful | Harmful per 1,000 decisions | ECE % | NLL | Gold outside candidates |
| --- | --- | --- | --- | ---: | ---: | --- | --- | --- | ---: |
| open-test | previous | 96.78 [96.42, 97.09] | 96.02 [95.69, 96.34] | 4,392 | 74 | 16.8 [12.4, 21.6] | 0.23 [0.19, 0.50] | 0.1076 [0.0939, 0.1222] | 41 |
| | **shipped** | 96.75 [96.39, 97.08] | 96.00 [95.66, 96.33] | 4,392 | 81 | 18.4 [14.1, 22.7] | 0.25 [0.20, 0.56] | 0.1068 [0.0928, 0.1217] | 41 |
| gold-v2-test | previous | 94.77 [93.94, 95.53] | 92.06 [90.76, 93.21] | 936 | 23 | 24.6 [15.1, 36.1] | 1.90 [1.55, 2.89] | 0.2342 [0.1918, 0.2807] | 12 |
| | **shipped** | 94.87 [93.99, 95.62] | 92.21 [90.85, 93.39] | 936 | 25 | 26.7 [15.7, 38.7] | 1.95 [1.53, 2.89] | 0.2307 [0.1897, 0.2787] | 12 |
| gold-v2-test-full-agreement | previous | 95.62 [94.88, 96.37] | 92.93 [91.71, 94.18] | 713 | 15 | 21.0 [11.1, 32.6] | 1.49 [1.24, 2.46] | 0.1827 [0.1435, 0.2272] | 5 |
| | **shipped** | 95.70 [94.92, 96.47] | 93.06 [91.79, 94.31] | 713 | 17 | 23.8 [12.6, 36.2] | 1.59 [1.21, 2.41] | 0.1802 [0.1409, 0.2201] | 5 |
| gold-v2-test-to-not (8 targets) | previous | 50.00 [12.50, 87.50] | 100.00 | 0 | 0 | 0.0 | 52.65 [21.94, 87.25] | 6.9560 [1.8584, 12.0911] | 4 |
| | **shipped** | 50.00 [12.50, 87.50] | 100.00 | 0 | 0 | 0.0 | 52.78 [22.38, 87.31] | 6.9578 [1.8650, 12.0905] | 4 |
| shape-probe (560 rows) | previous | 91.25 [88.93, 93.39] | 91.19 [88.79, 93.37] | 556 | 37 | 66.5 [46.8, 86.2] | 2.47 [2.16, 5.32] | 0.2122 [0.1649, 0.2617] | 0 |
| | **shipped** | 93.21 [91.07, 95.36] | 93.17 [91.06, 95.28] | 556 | 27 | 48.6 [30.5, 66.4] | 2.66 [2.16, 5.21] | 0.1841 [0.1383, 0.2331] | 0 |
| challenge-set (STE, 272 tokens) | previous | 86.76 [80.55, 92.42] | 87.88 [81.37, 93.14] | 86 | 0 | 0.0 | 8.33 [4.23, 13.79] | 0.3815 [0.2087, 0.5689] | 0 |
| | **shipped** | 89.34 [83.86, 94.30] | 91.41 [85.78, 95.45] | 86 | 0 | 0.0 | 6.50 [3.68, 11.45] | 0.3033 [0.1563, 0.4681] | 0 |
| ud-test-mapped (UD-EWT, primary view) | previous | 93.19 [92.69, 93.64] | 92.84 [92.25, 93.40] | 4,410 | 70 | 15.9 [12.1, 20.0] | 2.08 [1.78, 2.52] | 0.2877 [0.2634, 0.3210] | 138 |
| | **shipped** | 93.03 [92.48, 93.51] | 92.74 [92.12, 93.32] | 4,410 | 82 | 18.6 [14.7, 23.1] | 2.23 [1.92, 2.66] | 0.2915 [0.2674, 0.3249] | 138 |
| ud-test-raw (UD-EWT, raw tags) | previous | 89.25 [88.73, 89.72] | 88.56 [87.90, 89.15] | 4,007 | 70 | 17.5 [13.3, 22.1] | 5.99 [5.62, 6.48] | 0.6019 [0.5677, 0.6417] | 484 |
| | **shipped** | 89.08 [88.51, 89.57] | 88.46 [87.79, 89.08] | 4,007 | 82 | 20.5 [16.1, 25.6] | 6.17 [5.77, 6.66] | 0.6066 [0.5722, 0.6461] | 484 |

**What is worse (stated plainly; these are regressions against the previous model, reported as produced).**

1. **Harmful errors at 0.45 are higher on the open test split, gold v2 and the treebank**: open test **81 against 74**
   (18.4 against 16.8 per 1,000; accuracy 96.75 against 96.78), gold v2 **25 against 23** (26.7 against 24.6; the full-agreement view
   17 against 15) and the treebank **82 against 70** on both the mapped and the raw view (18.6 against 15.9 and 20.5 against 17.5;
   accuracy 93.03 against 93.19 and 89.08 against 89.25). On the treebank, noun-as-verb decisions rose from 43 to 52.
2. **Calibration**: the ECE is higher on the open test split (0.25 against 0.23), gold v2 (1.95 against 1.90), the shape probe
   (2.66 against 2.47) and the treebank (2.23 against 2.08 mapped, 6.17 against 5.99 raw); the log-loss is higher on the treebank
   (0.2915 against 0.2877 mapped; 0.6066 against 0.6019 raw).
3. **Operating points (below)**: the strict point has one harmful error on gold v2 (1 of 759; the previous model 0 of 761) and 9 of
   3,677 on open test (8 of 3,687); the balanced point on gold v2 is at **12.7** (11 of 868) per 1,000 answered and on the
   full-agreement view at **12.1** (8 of 661), above the development target of 10 (10.4 and 10.6 for the previous model, also
   above); on the treebank the balanced point is at 8.6 (35 of 4,049) against 8.2 (33 of 4,028). The strict point on the shape probe
   is at 8.1 (3 of 372) against 5.9 (2 of 340), above its target of 5.0 (the previous model also above it), at a higher coverage
   (66.9% against 61.2%).
4. **Development counts did not predict the held-out direction on the open test split and gold v2.** At the shipped
   seed the development open and gold v2 harmful counts were lower than the previous model's (91 against 100 and 14 against 16);
   the held-out counts are higher on both. The UD-EWT development count was higher (94 against 91), as the held-out treebank
   counts are. Nothing is concluded from the
   difference either way (the sets are small and these are a few errors each), and nothing was changed because of it.

**What is better.** The **shape probe**: accuracy 93.21 against 91.25, 27 harmful errors against 37 (48.6 against 66.5 per 1,000
decisions), NLL 0.1841 against 0.2122, and its stop condition no longer fires. The **challenge set**: accuracy 89.34 against 86.76
(seven of 272 tokens), no harmful error in either, NLL 0.3033 against 0.3815. The probe's shape `v-imperative-initial` (the target
of the new source) rose from 83.75% to 95.00% and from 10 harmful errors to 3 (table below). The open test, gold v2 and gold v2
full-agreement NLL are slightly lower (0.1068 against 0.1076, 0.2307 against 0.2342, 0.1802 against 0.1827) and the open test and gold v2
accuracies equal the previous ones within 0.1 points.

Operating points on the held-out sets (the thresholds selected on development data are the same for both models, strict 0.95 and
balanced 0.75; the reference point is 0.45):

| Set | Point | Previous: coverage % | Previous: harmful per 1,000 answered | Previous: harmful of answered | Shipped: coverage % | Shipped: harmful per 1,000 answered | Shipped: harmful of answered |
| --- | --- | ---: | --- | --- | ---: | --- | --- |
| open-test | reference 0.45 | 99.7 | 16.9 [12.4, 21.7] | 74 of 4,381 | 99.8 | 18.5 [14.1, 22.8] | 81 of 4,385 |
| open-test | strict | 83.9 | 2.2 [0.6, 4.3] | 8 of 3,687 | 83.7 | 2.4 [0.8, 4.6] | 9 of 3,677 |
| open-test | balanced | 94.1 | 8.5 [5.4, 11.4] | 35 of 4,135 | 94.7 | 7.5 [4.5, 10.6] | 31 of 4,159 |
| gold-v2-test | reference 0.45 | 99.9 | 24.6 [15.2, 36.1] | 23 of 935 | 99.7 | 26.8 [15.8, 38.8] | 25 of 933 |
| gold-v2-test | strict | 81.3 | 0.0 [0.0, 0.0] | 0 of 761 | 81.1 | 1.3 [0.0, 4.3] | 1 of 759 |
| gold-v2-test | balanced | 92.4 | 10.4 [4.4, 17.6] | 9 of 865 | 92.7 | 12.7 [5.3, 21.9] | 11 of 868 |
| gold-v2-test-full-agreement | reference 0.45 | 99.9 | 21.1 [11.2, 32.7] | 15 of 712 | 99.6 | 23.9 [12.6, 36.4] | 17 of 710 |
| gold-v2-test-full-agreement | strict | 80.8 | 0.0 [0.0, 0.0] | 0 of 576 | 80.6 | 1.7 [0.0, 6.2] | 1 of 575 |
| gold-v2-test-full-agreement | balanced | 92.7 | 10.6 [3.3, 19.0] | 7 of 661 | 92.7 | 12.1 [4.0, 22.7] | 8 of 661 |
| shape-probe | reference 0.45 | 98.2 | 67.8 [48.1, 87.3] | 37 of 546 | 98.2 | 49.5 [31.1, 67.5] | 27 of 546 |
| shape-probe | strict | 61.2 | 5.9 [0.0, 14.8] | 2 of 340 | 66.9 | 8.1 [0.0, 18.2] | 3 of 372 |
| shape-probe | balanced | 82.0 | 21.9 [8.8, 34.1] | 10 of 456 | 87.2 | 24.7 [12.2, 38.9] | 12 of 485 |
| challenge-set | reference 0.45 | 100.0 | 0.0 [0.0, 0.0] | 0 of 86 | 100.0 | 0.0 [0.0, 0.0] | 0 of 86 |
| challenge-set | strict | 67.4 | 0.0 [0.0, 0.0] | 0 of 58 | 76.7 | 0.0 [0.0, 0.0] | 0 of 66 |
| challenge-set | balanced | 84.9 | 0.0 [0.0, 0.0] | 0 of 73 | 88.4 | 0.0 [0.0, 0.0] | 0 of 76 |
| ud-test-mapped | reference 0.45 | 99.2 | 16.0 [12.2, 20.2] | 70 of 4,375 | 99.2 | 18.8 [14.8, 23.3] | 82 of 4,373 |
| ud-test-mapped | strict | 77.5 | 2.6 [1.1, 4.6] | 9 of 3,417 | 77.4 | 2.3 [0.9, 4.1] | 8 of 3,414 |
| ud-test-mapped | balanced | 91.3 | 8.2 [5.4, 11.5] | 33 of 4,028 | 91.8 | 8.6 [5.8, 12.2] | 35 of 4,049 |
| ud-test-raw | reference 0.45 | 99.1 | 17.6 [13.5, 22.3] | 70 of 3,972 | 99.1 | 20.7 [16.2, 25.9] | 82 of 3,970 |
| ud-test-raw | strict | 77.8 | 2.9 [1.2, 5.1] | 9 of 3,116 | 77.9 | 2.6 [1.0, 4.5] | 8 of 3,120 |
| ud-test-raw | balanced | 91.2 | 9.0 [5.9, 12.7] | 33 of 3,656 | 91.6 | 9.5 [6.4, 13.4] | 35 of 3,671 |

(The strict point is at or below its target of 5.0 per 1,000 answered on open test, gold v2 test and the treebank for both models,
and above it on the shape probe for both; the balanced point is below its target of 10 on open test and the treebank and above it on
both gold v2 views for both models, the shipped one by more.)

Noun-verb confusion of the same decisions (previous | shipped; noun as other, noun as noun, noun as verb, verb as other, verb as
noun, verb as verb):

| Set | Previous | Shipped |
| --- | --- | --- |
| open-test | 54, 2,373, 30, 38, 45, 1,852 | 52, 2,373, 32, 31, 51, 1,853 |
| gold-v2-test | 2, 454, 11, 9, 12, 448 | 1, 454, 12, 7, 14, 448 |
| shape-probe | 7, 361, 28, 1, 13, 146 | 7, 363, 26, 1, 4, 155 |
| challenge-set | 7, 43, 0, 4, 0, 32 | 5, 45, 0, 1, 0, 35 |
| ud-test-mapped | 150, 2,121, 43, 63, 32, 2,001 | 155, 2,107, 52, 65, 34, 1,997 |
| ud-test-raw | 150, 2,121, 43, 51, 32, 1,610 | 155, 2,107, 52, 52, 34, 1,607 |

Shape probe by shape (accuracy % and harmful at 0.45; previous | shipped): `n-obj-light` 96.25, 1 | 95.00, 1;
`n-compound-head` 77.50, 15 | 78.75, 14; `n-modifier` 97.50, 0 | 98.75, 0; `n-subject-initial` 90.00, 7 | 90.00, 6; `n-measure`
95.00, 4 | 96.25, 3; `v-imperative-initial` 83.75, 10 | **95.00, 3**; `v-after-modal` 98.75, 0 | 98.75, 0. **The n-subject-initial
caveat** of the adoption (worse on the development shapes set: hard harmful 114 to 136 and standard 20.0 to 26.7 per 1,000 as five-seed
means) **does not show on the probe**: 90.00% and six harmful errors against 90.00% and seven (80 rows; one row is 1.25 points).
The probe has too few rows to show or exclude the development effect.

Inputs and run: model image `6e1e4ade...e8d0`, selection artifact SHA-256
`6e34aaa4ad67e6b448ef84bb44d271db72e72d785c4db51be95826dfb43f8ebd`, the six input hashes are those of the previous record
(unchanged data; see `docs/evaluation/test-results.md`; the six rows of the two records are identical). The throughput line of the
report (528,578 tokens per second, one thread, open test) is a timing of a shared machine of the moment, not a result of the model,
and it is not comparable with the previous record's line (571,504 on another load); the experiment's own measurement (the
five-seed median 755,888 against the floor of 150,000) is the better comparison.

**Reading.** On the held-out sets the shipped model is clearly better only where its source aims: the shape probe (accuracy +1.96
points, harmful errors 37 to 27, the imperative shape from 10 to 3 harmful errors and 83.75% to 95.00% accuracy) and, by a smaller and
less reliable margin on a 32-sentence smoke test, the challenge set (+2.58 points). On the three general sets it is **not better**: it
has more harmful errors at 0.45 than the previous model on open test (+7), gold v2 (+2) and the treebank (+12, mapped and raw), a
slightly lower accuracy on open test (-0.03) and on the treebank (-0.16 mapped), and a higher calibration error on all but the
challenge set. The intervals overlap on every row, so none of these differences is a significant one on its own, and the direction
differs from the development counts at the shipped seed on open test and gold v2 (lower there) and agrees on the treebank
(higher there). The recommended thresholds did not change; the strict point still meets its target on open test, gold v2 and the
treebank and not on the probe, and the balanced point is above its target of 10 on both gold v2 views by more than before. The
selection rule had its evidence on development data (the hard tranche, five seeds, a Welch interval that excludes 0), and this
record is the fourth, weaker look at held-out sets whose result is reported as produced: no setting, threshold, weight or document
claim was changed to improve it.

**Owner decision (2026-11-08).** The owner reviewed this mixed held-out result and accepted it for the gains on instructional
text: the model is better on the shape probe, the challenge set and the imperative shape, slightly worse on open test (+7), gold v2
(+2) and the treebank (+12 harmful errors at 0.45), and has a higher calibration error on several sets. F2+IMP ships. The decision
changes no figure of this record.

## Shapes development set: new tranches, pre-registered selection rule

**The rule and the inputs of the three tranches were written before any candidate or label of them existed.** They are stated once, as a rule, in
`data/eval/shapes-dev/README.md` (sections "The Rule" and "Inputs of the Selection", with the shape definitions above them); this section holds the order in which they
were fixed and run. The three tranches (`compound-head.tsv`, `headings.tsv`, `named-state.tsv` in `data/eval/shapes-dev/`) are development sets: the figures that they
produce select settings and are never held-out results. The rule is model-independent: no PartOfSpeech weight file, prediction, error or
score takes part in the selection.

### Order of the pre-registration

| Commit | Time (2026-11-09) | Content |
| --- | --- | --- |
| `426876f` | 11:45 | the frozen pre-registration: the rule and the inputs in the README, `tools/data/shapes_dev_extra.py` and its tests, the two convention rules, the trainer support |
| `65ee3c3` | 12:31 | amendment 1 (below): top-up round `h2` of `headings`, with its code and tests, before any label of the round |
| `41423fd` | 12:35 | amendment 2 (below): controls-only round `h3` of `compound-head`, with its code and tests, before any label of the round |
| `5c81c67` | 12:44 | the data: the three tranches, their sources and leakage reports |

The selection and build code is identical between `41423fd` and `5c81c67`: nothing was tuned after labeling. If the pull request is squash-merged, these commits stay
retrievable from the pull request's head ref (`refs/pull/<n>/head`). The README, the verification record and this log were written after labeling and do not change the rule.

### Pool sizes before the caps

Counts only (no label, no model), from the pre-flight of the tool on the pinned inputs (the pins are in the README):

| Tranche | Pool before the caps |
| --- | --- |
| `compound-head` | 8,009 candidates in the contested band (of 21,152 frame hits) |
| `headings` | 636 imperative, 1,666 noun and 52 proper frames (296, 621 and 37 lemmas) |
| `named-state` | 189 frames (94 lemmas, 61 documents) |

### Amendments of the rule

Each amendment was written down and committed after the main round of its tranche was decided and before any label of the round that it names existed. The README rule
states the rule as amended.

1. **Amendment 1, `headings`, top-up round `h2`** (`65ee3c3`): the main round kept 25 imperative targets, below the floor of 40 (366 of 400 imperative frames were read by both
   labelers as compound nouns). Round `h2` labels more imperative frames under the same rule, from the units that are not candidates of the main round, with one widening of the frame
   (the first word may also be a base form that the lexicon lists as a verb and not as a noun, proper noun, adjective or auxiliary), because the pool of noun and verb words was nearly used up.
2. **Amendment 2, `compound-head`, controls-only round `h3`** (`41423fd`): the controls of the main round were chosen for the lemmas of all 600 candidate targets, so only 14 fitted the
   lemmas of the 100 built targets, below the floor of 15. Round `h3` labels more controls under the same rule, for the lemmas of the targets that the build takes; it adds no target.
   The hard tranche needed the same round.

`named-state` needed no top-up round. The pool of its frames was below the 250 that the pre-flight asked for, and the floor of that tranche was fixed at 40 before the candidates were
written.

### Labeling rounds and runs

| Tranche | Rounds | Candidate units | Batches |
| --- | --- | ---: | ---: |
| `compound-head` | main round (600 targets, 50 controls); round `h3` (50 controls) | 700 | 6 |
| `headings` | main round (400 noun, 400 imperative, 46 proper); round `h2` (155 imperative) | 1,001 candidates in 941 lines | 3 |
| `named-state` | main round (153 targets, 43 controls) | 196 | 2 |

Both blind labelers tagged every batch of every round (11 batches, 22 agent runs, one agent per labeler and batch), every output was valid on the first run, and neither model refused
(0 refusals, 0 retries). The imperative yield of `headings` was low because most `Check Fluid Level`-like frames were read as compound nouns (47 of 555 imperative frames were kept).
The files were written by `python tools/data/shapes_dev_extra.py --work <work-folder> build` from the tool as of commit `41423fd`. The committed counts, the drops, the documents, the hand
check and the limits are in the [README](../../data/eval/shapes-dev/README.md#new-tranches); the baseline is in the next section.

### Note: the lemma cap of the `headings` build

The build counted the 4 targets per lemma separately for the noun and the imperative kind, while the rule (README "The Rule", item 6) caps them over both kinds together. The committed
`headings.tsv` has no lemma with more than 4 targets (the largest, `check`, `plan`, `replace` and `suggest`, have 4 imperative targets each), so a rebuild with one shared counter into a
separate folder from the same work folder is byte-identical to the committed `headings.tsv` and `headings-sources.tsv` (SHA-256 `de6a46fe...` and `30754b95...`); no committed data changed.
The build now uses one shared counter, and `lemma_cap_problems` refuses a lemma over its cap even with `--allow-short`.

### Note: correction of the trailing page number rule of the `headings` build

The pre-registered line rule refuses a table-of-contents line, including a line that ends in a page number (README "The Shape of `headings`"). The implementation tested for a final
number only (`\s\d{1,4}$`), so a line that ends in a page range (`4-8`, `4-19`, `4-4`, `19-21`) passed it. This is a defect of the implementation against the rule, not a change of the rule: the
rule text is unchanged, and the implementation now refuses a final number or a final range written with a hyphen, an en dash or an em dash (`has_trailing_page_number`; refused at
the line stage, as the heading filter rule `page-number`, and at the build). Forms that the rule does not name (a letter before the number such as `A-12`, a dotted number such as `4.8`)
are not refused.

The build was rerun from the same work folder and the same labels, with no new candidate and no new label. Nine kept items fail the corrected rule; four of them were in the committed
file (`shapes-dev-headings-0006` and `-0032` noun targets, `-0090` an imperative target, `-0111` a proper control), and the next kept items in the seeded order of the same labeled pool
fill the strata (two noun targets and one control; no imperative item was left). Result: `headings.tsv` has 115 lines (50 noun and 45 imperative targets, 20 controls; was 116: 50, 46 and
20), 507 tokens (was 520), and the quota floors hold (noun 40, imperative 40, controls 12). None of the four removed items and none of the three added items is among the 60 items of
the hand check, so the hand-check figure is unchanged (57 correct, 1 incorrect, 2 unclear of 60). `compound-head.tsv` and `named-state.tsv` are cut by other frames and the rule is for
heading lines only: they are unchanged. `shapes-dev-headings-0004` (`Fig, No, Title Page`, OCR junk) has no trailing number, so the rule does not reject it, and it stays.

New SHA-256: `headings.tsv` `45a62a71...`, `headings-sources.tsv` `aa3a14f1...`; the leakage reports, `MANIFEST.tsv` and the baseline below are regenerated.

### Note: the standard and hard builders see the newer tranches

`shapes_dev.py` filtered its candidates through the hashed sentence keys of the evaluation sets without the five tranches of the development set, so a rebuild of the standard or the hard tranche
could have taken a sentence of `compound-head`, `headings` or `named-state`. Its filter (`LeakageFilter`) now adds the sentence keys of the three newer tranches that exist
(`leakage.held_out_overlap_keys(new_tranches=True)`); the builder of the newer tranches (`shapes_dev_extra.py`) passes `new_tranches=False`, because it must not see its own earlier build. The standard
and the hard tranche have 0 exact sentences in common with each of the three newer tranches (`check_leakage.py`, the leakage reports), so the data is unchanged: a rebuild of the standard tranche
(`StandardRebuildTests`, from its work folder) and of the hard tranche (from its work folder, into a separate folder) is byte-identical to the committed `dev.tsv`, `sources.tsv`,
`excluded-sources.tsv`, `hard.tsv` and `hard-sources.tsv` (SHA-256 `bbc94e42...` and `c041ae3d...`).

## Shapes development set: new tranches baseline of the shipped model

The built tranches `compound-head.tsv`, `headings.tsv` and `named-state.tsv` were scored once with the shipped model after the build. These are development figures: they select settings,
nothing is trained on the files and none is a held-out result. The standard and hard tranche files are unchanged by this work (`eval-shapes --tranche both` reads the same two
files, SHA-256 `bbc94e42...` and `c041ae3d...`). The `heading-titlecase` rows are scored on the `headings.tsv` of the correction note above (the earlier file scored 96 targets at 84.4%); the other two shapes are
scored on the same files as before and their rows are unchanged.

Command (run once from the repository root, below normal priority; it reads only the three development files and the embedded weights and appends no row to the run table):

```text
dotnet run --no-build -c Release --project tools/DemaConsulting.PartOfSpeech.Trainer -- eval-shapes --tranche new --label "shipped model baseline"
```

| Input | SHA-256 |
| --- | --- |
| `data/eval/shapes-dev/compound-head.tsv` | `537e0be3923f585b8f084de4e88f4064624e4f5dc484f84ccd303fed3db924f5` |
| `data/eval/shapes-dev/headings.tsv` | `45a62a71837f02d1e76f0ad55a71a1770cb628ea8ca8765c0d4583f49b9da72a` |
| `data/eval/shapes-dev/named-state.tsv` | `e08472387630368b7fb3fbaa5cd7b6187d0760608c93b55f19250c98f2d06d6e` |
| shipped weights file (embedded resource) | `48dd9188c37fb0ba01458e45a28d7b4b66d6a14b9f57eaf2a621bddc0e9b413b` |

Scoring: by class for `n-compound-head-ambiguous` and `n-named-state` (Noun: NOUN and PROPN), by exact tag for `heading-titlecase` (a common-noun heading word answered PROPN is wrong
but not harmful). A harmful error is a swapped noun/verb decision at a confidence of at least 0.45 (`harmful per 1,000` is per noun-verb decision, as before); **wrong at confidence >= 0.95**
counts the target items answered wrongly with a confidence of at least 0.95.

| shape | target items | target accuracy | 95% interval | noun-verb decisions | harmful errors | harmful per 1,000 decisions | wrong at confidence >= 0.95 | control items | control accuracy |
| --- | --- | --- | --- | --- | --- | --- | --- | --- | --- |
| n-compound-head-ambiguous | 100 | 60.0% | [50.2%, 69.1%] | 100 | 39 | 390.0 | 5 | 20 | 100.0% |
| heading-titlecase | 95 | 84.2% | [75.6%, 90.2%] | 78 | 4 | 51.3 | 2 | 20 | 100.0% |
| heading-titlecase: noun targets | 50 | 80.0% | [67.0%, 88.8%] | 50 | 1 | 20.0 | 2 | - | - |
| heading-titlecase: imperative targets | 45 | 88.9% | [76.5%, 95.2%] | 28 | 3 | 107.1 | 0 | - | - |
| heading-titlecase: PROPN controls | - | - | - | - | - | - | - | 20 | 100.0% |
| n-named-state | 100 | 90.0% | [82.6%, 94.5%] | 37 | 3 | 81.1 | 0 | 12 | 100.0% |

Wrong answers at a confidence of at least 0.95: 5 (`n-compound-head-ambiguous`), 2 (`heading-titlecase`, both on noun targets) and 0 (`n-named-state`), 7 in all.

### Headroom of the new tranches

Headroom is 100% minus the target accuracy (derived from the table above, not a new measurement): 40.0% for `n-compound-head-ambiguous` (40 of 100 target errors), 15.8% for `heading-titlecase`
(15 of 95; 20.0% of the noun targets and 11.1% of the imperative targets) and 10.0% for `n-named-state` (10 of 100). The 95% intervals are 15 to 19 points wide for the first two shapes
and 12 points for `n-named-state`, so a difference of a few items between variants is inside the interval.

## Compound and heading training data: pre-registered selection rule

This section is written and committed **before any label of the two new sources exists**. At this commit no candidate row has been written, no batch has been exported, no model has tagged a token of
the new text, and no model of this experiment has been trained or evaluated; the only commands run on the new text are the document gate (`exclusions`) and the counts-only `preflight` of the builder
(clean sentences, frame hits, lemmas and lines per stratum, nothing else). The git history shows the order: this commit (the builder `tools/data/compound_heading_data.py`, its tests and this rule), then
the data and their registration (weight 0), then the optional feature, then the pre-registration of the retraining, then the runs, then the results. The SHA of this commit is recorded by the commit that
follows it (a commit cannot contain its own SHA). The code base is `main` at `f91f25c` (PR #18); nothing outside `tools/data` and this file changed in this commit.

### Goal and motive

Add two targeted training sources, `compound` (about 800 sentences with a noun compound whose head can also be a verb) and `headings` (about 600 Title Case heading, caption and step-title lines), to
`DemaConsulting.PartOfSpeech`, and test them in a pre-registered retraining. Motive: a consumer of the library reported confident errors on noun + noun compounds whose head can also be a verb
(`[noun] split`, `[noun] check`, `[noun] stand`) and on Title Case headings and fragments tagged as proper nouns. The development tranches of PR #18 measure exactly these shapes; the shipped model
(F2 plus the `imperative` source) scores, on the development files (`eval-shapes --tranche new`, reproduced at the start of this work on the unchanged `main`):

| Shape | Target accuracy | Harmful per 1,000 decisions | Wrong at confidence >= 0.95 |
| --- | --- | --- | --- |
| `n-compound-head-ambiguous` | 60.0% | 390.0 | 5 |
| `heading-titlecase` | 84.2% | 51.3 | 2 (both on noun targets) |
| `n-named-state` | 90.0% | 81.1 | 0 |

No text of the consumer report is used or was read for this work: **every sentence of the new sources comes from the recorded US federal public-domain documents below**, and none is supplied by any consumer.
Nothing is adopted unless the retraining rule (a later section) selects it; both sources are committed with weight 0 first.

### The documents (frozen)

One shared pool of new US federal documents feeds both sources: 224 documents (1862 to 2008; 176,759 clean sentences), sourced from archive.org OCR text. The public-domain basis of every document
is written in the `license_basis` cell of its row (17 U.S.C. 105, the agency named in the record, the Internet Archive record's own rights statement) and was checked one by one: the creator field of the
record names a US federal agency, no copyright notice, claim or third-party permission statement was found in the text (the Google-scan boilerplate of the older scans, a mention of the word and the copyright policy
that one manual describes are not claims), no FAA material, nothing in `data/eval/shapes-dev/excluded-sources.tsv`, and a document with a state, foreign, university, contractor or private co-author was dropped by hand.

| Agency family | Documents | Clean sentences of the draft |
| --- | ---: | ---: |
| US Department of Agriculture (`usda`) | 60 | 35,549 |
| US Forest Service | 28 | 20,566 |
| US Geological Survey (`usgs`) | 24 | 36,213 |
| US Public Health Service and Children's Bureau (`phs`) | 20 | 10,826 |
| US War Department and Army (`army`) | 19 | 15,069 |
| US Navy and Marine Corps (`navy`) | 19 | 13,246 |
| Bureau of Land Management, Bureau of Fisheries, Office of Education, Bureau of Education and National Park Service (`other`) | 15 | 18,283 |
| US Weather Bureau (`wxb`) | 14 | 8,043 |
| US Bureau of Reclamation | 13 | 9,808 |
| US National Bureau of Standards (`nbs`) | 6 | 3,820 |
| US Bureau of Mines | 6 | 5,336 |
| **All** | **224** | **176,759** |

By decade of publication: 1860s 2, 1870s 3, 1880s 8, 1890s 7, 1900s 16, 1910s 17, 1920s 5, 1930s 4, 1940s 39, 1950s 24, 1960s 29, 1970s 27, 1980s 25, 1990s 13, 2000s 5. No NASA document was needed: both pool budgets were met
without the NASA Technical Reports Server.

How the pool was drawn (no model, no predicted tag, no label): archive.org advanced searches by federal creator name and by title words (manual, handbook, bulletin, circular, leaflet, instructions, guide,
report, paper, ...) gave 7,709 candidate records; a record was dropped before any download when its id, normalized URL, archive.org identifier or Gutenberg number is in the used index (training and
evaluation sources, all five shapes-dev tables, the imperative source and its exclusions, and every earlier sourcing draft of the shapes development set and of the imperative data, unused documents
included) or when its title matches a closed list of non-expository words (index, catalog, hearing, regulations, ...). The remaining records were fetched in a round-robin over the agency families, inside a family in the
order of a seeded hash of the identifier, at most 3 per title stem (a series). **One steer was introduced during the sourcing and is stated here**: a pilot over the first 95 documents showed that the headings pool was
the binding budget (798 clean heading lines of 95 documents) and that the OCR of Google scans and of scans before 1940 holds almost no stand-alone heading lines, so from the 243rd record on, records dated 1940 or later
and not a Google scan were fetched first. The steer is a property of the OCR text structure, was chosen before any label and used no model. 550 records were fetched: 274 passed the document checks, 242 were rejected
(the reasons overlap, a record can have several: 92 downloads answered an HTTP error, 79 shared 3 or more sentences with a training or evaluation set or an earlier sourcing, 44 carried a copyright notice, 26 had too few sentences, 25 named a non-federal publisher or creator,
9 showed no federal publisher, 1 was FAA material) and 34 were left for review (not used). Of the 92 HTTP answers, 71 were 404, 13 were 401, 7 were 500 and 1 was 403; a host that refuses is skipped, never worked around. 44 of the 274
were then excluded by hand (state, foreign, university, contractor or private co-authors, 39; duplicate scans of a document already taken, 5), 3 had no usable publication year and 3 were left with fewer than 40 sentences after cross-document
duplicates were removed. The list of the 44 exclusions and their reasons, the fetch records and the per-document hashes are in the work folder; the committed `sources.tsv` of each source lists only the documents that it uses,
with `raw_sha256`, `text_sha256` and the retrieval date.

| Frozen input | SHA-256 |
| --- | --- |
| `sources-draft.tsv` of the work folder (224 documents, 12 columns of `data/eval/gold-v2/sources.tsv`) | `81401c8328ac6335faf8aa8e5711079d07ce96aead587cae5fa5a894506a78de` |
| `tokens.tsv` of the work folder (365,570 units: the sentences and the heading lines, tokenized by the library tokenizer through the trainer's `shapes-tokenize`) | `00c8d98e77cc538dc70b9e86959d3e56aa92e38804e53ba4ad61b452c8f28760` |
| the labeler prompt template (the fenced block below, LF, 1,903 bytes) | `9f1da5238dbcd196ca95b0795194245846955d2bb3a95a70bf0540d112d17db5` |

Independence from the other sets is enforced before any label: the document gate (`exclusions`) reports 0 problems for the draft (no used or excluded id, URL or archive.org identifier, a federal basis in every row, sound hashes and counts), every
sentence goes through the unchanged candidate filters of `shapes_dev.py` (the held-out sets enter only as the opaque hash set of `LeakageFilter`: equal training or evaluation sentence, probe 5-gram) and is checked against all five tranches
of the shapes development set, against `data/imperative/imperative.tsv` and against the cleaned text of every earlier sourcing (`equals-shapes-dev`, `near-shapes-dev`), and a document with 3 or more such sentences is rejected as a whole.
No document was rejected as a whole in the final draft (the 79 that were had been removed at the fetch step by the same sentence-key test).

### The rule (fixed now; applied as written; nothing is tuned after a label exists)

The normative text is the docstring of `tools/data/compound_heading_data.py` at this commit; this is the same rule in short.

1. **Frames (position and lexicon only; no PartOfSpeech or WordSense score; every frame hit is in the pool).** `compound`: a sentence of 3 to 30 tokens that passes the cleanliness filter and the equal and near-duplicate filters, with a hit of
   `shapes_dev_extra.compound_ambiguous_hit` (the frame of `n-compound-head-ambiguous`, unchanged); a sentence whose key equals a heading line of the pool is dropped (`equals-a-heading-line`). `headings`: a line found by `shapes_dev_extra.heading_lines`
   that passes `heading_noise_reason` (2 to 10 tokens, Title Case, no all-capital word, no unknown word, and the corrected trailing page number and page range rule) and has a hit of `heading_hits`; its stratum is the rarest hit (`proper`,
   then `imperative`, then `noun`) and a line is one candidate.
2. **Candidates.** `compound`: the seeded order `rank_of(20261110, "compound", sentence key)`, at most 6 per head lemma and 1,500 in all (gate: at least 1,200 candidates and 250 head lemmas). `headings`: strata in the order proper, imperative, noun, inside a
   stratum the frame strength and then the seeded rank, at most 4 per lemma over all strata, 25 per document and 1,500 lines in all (gate: at least 1,000 lines and all three strata present).
3. **Labels.** Two blind labelers, `claude-opus-5.5` and `gpt-6.1-sol` (general-purpose sub-agents, at most 4 at a time, one agent per labeler and batch), tag every token from scratch following `data/CONVENTIONS.md`, through the unchanged
   `shapes_dev.py export`, `check-batches`, `validate-output`, `status` and `decide` commands (`--round compound`, `--round headings`). Each unit is labeled once per model; no re-roll. A refusal is requested again from a fresh agent and disclosed.
4. **Kept items.** Both: every token agreed (`disputed_tokens == 0`) and no fix-tier violation of the convention linter (the item is dropped, no label is changed by hand). `compound`: the head read as NOUN or PROPN by both (kind `noun`), or read as VERB by both
   with an agreed noun right before it (kind `verb`, a counter-example); any other combination is dropped and counted. `headings`: the stratum word has the exact tag of the convention on which both agree (initial imperative VERB in its base form, noun word NOUN,
   proper noun PROPN); a line on which both agree on another tag is a `class-mismatch` and is dropped, never corrected.
5. **Caps and quotas (seeded order).** `compound`: 4 per head lemma, 20 per document, 680 of kind `noun` and 120 of kind `verb`, 800 in all, the verb share at most 15 percent; floor 600 sentences and 200 head lemmas. `headings`: 4 per lemma, 25 per document, noun 340,
   imperative 200, proper 60 (600 in all); floors noun 150, imperative 100, proper 40. `build` refuses (exit 1, nothing written) below a floor; `--allow-short` waives a floor or the minimum (the miss is disclosed, never padded), never a cap.
6. **Hand check (a model review, not a human review).** 60 kept items per source (`headings`: noun 25, imperative 20, proper 15 by quota) and 4 leniency controls with a swapped tag, drawn with the fixed seeds 20261111 (`compound`) and 20261112 (`headings`), one draw each,
   shown token by token to a third model (`claude-sonnet-5.5`). Gate per source: marked-word precision `correct / (correct + incorrect)` at least 95 percent (3 incorrect of 60 meets it), at most 1 of the 4 controls accepted, no unanswered item; below it, stop and report.
7. **Not part of the rule:** no lexicon rebuild (the coverage of the new sources is reported); no weight tuning (a source is added at weight 1 or not at all).

**Choices made while writing the builder, stated openly.** The `headings` source has no fourth stratum of "other Title Case lines" without a frame word: the shared decision tools need a judged word with an exact tag, so every kept line is a `noun`, `imperative` or `proper`
line, and the noun cap is 340 (the 280 of the plan plus the 60 that the fourth stratum would have taken). The `compound` source keeps verb counter-examples (up to 15 percent) because the `imperative` source taught one reading only, which its README lists as a limit.

### Pool sizes before the labeling (counts only; no label, no model)

From `preflight` on the frozen inputs above (the budgets are met, so no stop condition of the plan applies):

| Source | Pool and candidates |
| --- | --- |
| `compound` | 126,725 clean sentences; 17,783 frame hits over 1,935 head lemmas; 1,500 candidates over 694 head lemmas in 192 documents (budget: at least 1,200 candidates and 250 lemmas) |
| `headings` | 2,976 clean heading lines; frame hits 1,424 noun, 505 imperative and 100 proper (505, 224 and 68 lemmas); 1,012 candidate lines over 560 lemmas in 124 documents: noun 560, imperative 356, proper 96 (budget: at least 1,000 lines, all three strata) |

### Labeler prompt (frozen)

The agent gets this text with `{ROUND}`, `{BATCH}` and `{MODEL}` filled in and nothing else (no stratum, source, lemma or intended class, no mention of compounds, headings or imperatives; the private folder of the round is never named):

```text
You are a part-of-speech tagger. Tag every token of every text unit from scratch with a Universal POS tag.

Read exactly two files and nothing else:
1. C:\work\DemaConsulting\Libraries\PartOfSpeech\data\CONVENTIONS.md  (read it in full first; it is the tagging convention you must follow)
2. <W>\rounds\{ROUND}\batches\{BATCH}.tsv  (3 tab-separated columns with a header: id, token_index, token; one row per token, rows of one text unit are
   consecutive and token_index counts from 0; a text unit is a sentence or a short line)

Tag set (exactly these 17): ADJ ADP ADV AUX CCONJ DET INTJ NOUN NUM PART PRON PROPN PUNCT SCONJ SYM VERB X.
For every row give a tag, a confidence (high | medium | low) and flags (one of: -  mentioned  foreign  foreign+mentioned; `-` when none applies).
Decide each token from its context and the convention; the token column is what the tokenizer produced, keep it as it is.

Write the result as 6-column tab-separated parts with the header line `id<TAB>token_index<TAB>token<TAB>tag<TAB>confidence<TAB>flags`,
the first three cells copied byte for byte from the batch row (same order, every row, nothing added or dropped). Use parts of at most 400 rows,
each with its own header line, in the folder <W>\rounds\{ROUND}\batches\out\{MODEL}\ named {BATCH}.p01.tsv, {BATCH}.p02.tsv, ... (two digits, consecutive,
no gap). Create the files with your file-creation tool (no shell redirection).

Rules: do not read, list or search any other file or folder (no repository search, no web, no other batch, no other tagger's output, no
private folder); do not repair or post-process the output by script; do not ask questions; finish by listing the part files you wrote.
```

`<W>` is the work folder (outside the repository). The template differs from the one of the imperative data only in the round folder of the paths and in the words "text unit" for "sentence".

### Compound and heading data: limits stated in advance

- The pool is every frame hit and is not selected by a model, so the `compound` training data are on average easier than the development tranche (which kept only the contested band of a classifier); the data teach the frame, not the hard cases.
- The documents are US federal text, mostly 1940 to 1990 (the register and the era differ from the consumer report); 58 of the 224 documents date from before 1930, and older scans yield few usable heading lines.
- The labels are written by two models, without human review; the hand check is a model review (the error found by the same kind of review for the imperative data was about 0.1% of tokens).
- A `headings` line has few tokens; a Title Case imperative that both labelers read as a compound noun is dropped (the yield of imperative lines is expected to be low), and the proper-noun stratum is small.
- No NASA documents and no FAA documents are in the pool.

## Compound and heading training data: labeling, build and registration (results; written after the pre-registered rule)

This section records what happened after the commit `e776f5693739900d91e63fd2a12451da1f4ba90b` (the builder, its tests and the rule above). The order of events, all on 2026-11-09:
the candidates were written and exported as blind batches, both labelers tagged every batch, the decisions and the builds followed, the model review of a sample was run on the built data,
a license check of the records found 7 documents to remove, and the data were registered at weight 0 (the commit that follows this one in the git history). No model of the retraining has been
trained or evaluated yet, and no held-out command has been run.

### Labeling

`compound`: 1,500 candidates in 10 batches (b001 to b010). `headings`: 1,012 candidate lines in 7 batches (b001 to b007). Every batch was tagged by `claude-opus-5.5` and by `gpt-6.1-sol` (17 batches times two labelers, 34 runs),
as general-purpose sub-agents, at most 4 at a time, with the frozen prompt above (SHA-256 of the template `9f1da5238dbcd196ca95b0795194245846955d2bb3a95a70bf0540d112d17db5`, no word added). **Refusals: none.** Every one of the 34 runs
produced a valid output at the first request (`validate-output` passed), no batch was skipped, no run was requested again, and no run was repeated to change a result. `decide` ran once per round and `build` once per source before the removal described below.

### Decisions and builds

| Quantity | `compound` | `headings` |
| --- | ---: | ---: |
| candidates | 1,500 (694 lemmas, 192 documents) | 1,012 (noun 560, imperative 356, proper 96; 560 lemmas, 124 documents) |
| dropped: class mismatch | 106 | 427 |
| dropped: target disagreement | 6 | 12 |
| dropped: frame mismatch | 80 | 0 |
| dropped: a disputed token | 163 | 31 |
| dropped by the caps | lemma 55, document 6, noun 107, verb 177 | noun 108, proper 26 |
| built (before the removal below) | 800 (noun 680, verb 120; 477 lemmas; 161 documents; 15,889 tokens) | 408 (noun 340, proper 60, imperative 8; 289 lemmas; 95 documents; 1,816 tokens) |
| removed with 7 documents (below) | 54, then 4 verb-share drops | 33 |
| **committed** | **742** (noun 631, verb 111; 462 lemmas; 154 documents; 14,692 tokens) | **375** (noun 315, proper 52, imperative 8; 273 lemmas; 90 documents; 1,668 tokens) |

The `compound` class mismatches are heads that both labelers read as another class than noun or verb (adjective 48, adverb 36, adposition 13, auxiliary 6, one each numeral, subordinating and coordinating conjunction); the build keeps a head both read as VERB
after an agreed noun (kind `verb`) and a head read as a noun (kind `noun`). Candidate sentences dropped alone because of an overlap: 3 equal and 2 near duplicates of a shapes development sentence (`compound`), 118 heading lines equal to one (`headings`).

### The imperative stratum of the headings source is almost empty

The registered floors of `headings` are noun 150, imperative 100 and proper 40. 346 of the 356 initial-imperative candidates (the first word read as NOUN by both labelers 275 times, as ADJ 44 and as PROPN 27) were dropped by the class rule
(an initial imperative must be VERB), 2 by a disagreement and 8 were kept. The rule of the pre-registration drops such a line, never keeps it with another tag and never corrects it, and `--allow-short` waives a floor with the miss disclosed. The build therefore ran with `--allow-short`:
the floor of the imperative stratum is not met (8 lines of 100), and the noun and proper floors are met. The spot-check sample is smaller for the same reason (48 items: noun 25, imperative 8, which is all of them, proper 15). **Consequence for the retraining:** the `headings`
source has almost no imperative training mass, so an arm that adds it can only test the noun and proper-noun readings of Title Case words, not the initial-imperative reading.

### A license-basis finding and the removal of 7 documents

The hand exclusions of the sourcing (44 documents) removed documents whose record shows a state, foreign, university, contractor or private publisher. A review of the author field of every used document after the build found 7 documents that also list a state or local government body as an author
(a state environmental agency, a state natural-resources department, a state resources agency, a town and a county conservation district): `other-montanafinalstat02unitrich`, `usda-CAT75656463`, `usda-CAT76668505`, `usda-CAT88893596`, `usda-CAT88912096`, `usda-CAT93999438` and `usfs-CAT77678911`.
Rule 1 of the pre-registration admits only documents with a verifiable US-federal basis, and the sourcing dropped a document without one, so these 7 were removed. The builder got the option `build --omit-document ID` (a change after the labeling; the labels, the decisions and the caps are untouched): it removes the selected items of the listed documents after the caps and before the numbering, so nothing is added, nothing is relabeled
and the ids have no gap; the verb share of rule 6 is applied again to what is left (4 verb items were dropped for it); the documents are listed in `excluded-documents.tsv` with a reason. Both sources were built again with the 7 ids (`compound`: 54 items removed, `headings`: 33), which gave the counts above. The pre-removal files are not committed.

### Hand check (model review, not a human review)

One sample of each source was drawn from the data as built before the removal (seeds 20261111 and 20261112) and reviewed once by `claude-sonnet-5.5` against `data/CONVENTIONS.md`; no sample was drawn again.

| Measure | `compound` (60 items, 4 controls) | `headings` (48 items, 4 controls) |
| --- | ---: | ---: |
| judged word correct | 60 of 60 (100.0%) | 48 of 48 (100.0%) |
| whole item correct (no other token marked wrong) | 58 of 60 (96.7%) | 47 of 48 (97.9%) |
| token precision (reported, not gated) | 99.76% (3 wrong of 1,255) | 99.56% (1 wrong of 227) |
| controls accepted | 0 of 4 | 0 of 4 |
| gate (judged word at least 95%, at most 1 control accepted) | passed | passed |
| items that belong to a removed document | 2 | 3 |
| the remaining reviewed items (judged word; whole item; token precision) | 58 of 58; 56 of 58; 99.75% (3 of 1,217) | 45 of 45; 44 of 45; 99.54% (1 of 219) |

### Registration checks (on the committed files)

- `check_leakage.py --write-report`: 0 document overlaps, 0 exact sentence overlaps and 0 near duplicates of both sources against every evaluation set (open dev and test, gold v2, the probe, all five tranches of the shapes development set, the STE challenge set, UD English-EWT train, dev and test), and none against the other training sets;
  the two new sources share 73 documents (one pool, reported and not gated) and no sentence. The pairwise section is new (`data/training-leakage-report.tsv`).
- `check_licenses.py` clean (both source tables are scanned for restricted text and FAA material, and against the exclusion record). `check_conventions.py --tier fix --enforce --apply`: 0 violations in all columns; the check tier reports 2 hits in `compound` (`be-participle-adj-not-gradable` and `language-name-token`) and none in `headings`.
- Lexicon (not rebuilt, `lexicon.tsv` unchanged; `check_lexicon.py` gate G1 passed): the tag of 12,823 of 13,265 word tokens of `compound` (96.67%) and 3,971 of 4,332 distinct (spelling, tag) pairs (91.67%) are listed; for `headings` 1,512 of 1,569 tokens (96.37%) and 898 of 951 pairs (94.43%).
- `train --check` exit 0, image `6e1e4ade56aa277d79709c107000f10e99c6f4e55a097e872a9c38fa2e12e8d0` unchanged, with both sources in `training.json` at weight 0.

| Frozen file | SHA-256 |
| --- | --- |
| `data/compound/compound.tsv` | `eb8c4708ba95fb370c885110e10eda0f1d826c5922876bd66e69389894bd5e3b` |
| `data/compound/sources.tsv` | `8b861dd3ff2d5a3fd5e741f5fe2a906807f99afa562d7e294d4011d93ef15e02` |
| `data/headings/headings.tsv` | `5931072928829e1911468173a181761ba001c4d7f16a8492391404395649124c` |
| `data/headings/sources.tsv` | `428a73f0581ec168cdef63d885cd6e001e925813b0a0d0dd1148f79636935aff` |

The README of each folder is the description of the data (`data/compound/README.md`, `data/headings/README.md`).

## Compound and heading training data: pre-registered retrain

This section is written and committed **before any model of this retraining is trained or evaluated**. No `train --output`, no `eval-arm` for an arm and no other run of an arm exists:
since the data were registered the trainer commands run were `train --check` (exit 0, image `6e1e4ade...`, unchanged, which shows that the weight-0 entries leave the shipped model unchanged),
one **byte-identity proof** of the shipped configuration (`train --seed 20260101 --features clauseCues --calibrate`, written outside the repository, which reproduces `weights.bin` byte for byte; it is the BASE arm's seed-20260101 run
done once as a build gate, and its result is not used as an arm result) and one smoke scoring of the **shipped** `weights.bin` with the new `eval-arm` (a baseline, not an arm). The held-out sets are **not used by this experiment** and no held-out command was run.

### Order of the commits

The git history shows the order. Each step is a commit, and a commit cannot contain its own SHA: the SHA of this commit is recorded by the next one.

| Step | Commit | What it contains | Written |
| --- | --- | --- | --- |
| C1 | `e776f5693739900d91e63fd2a12451da1f4ba90b` | the builder `tools/data/compound_heading_data.py`, its tests and the selection rule (section "pre-registered selection rule"), **before any label of the sources existed** | 2026-11-09 21:22 -0500 |
| C2 | `7e82e2d75e676aae2477d46131159b24cdd5673c` | the labeled data, their documents and checks, and the registration in `training.json` at weight 0 (section "labeling, build and registration") | 2026-11-09 23:44 -0500 |
| C3 | `9a6d10b4e7519c36bbd6c42a728f3858214a818e` | the feature group F5 (`titleCaseLine`), off by default, and the `shapesNew` block of `eval-arm` (code, tests, documents) | 2026-11-10 00:15 -0500 |
| **C4** | this commit | this pre-registration: the arms, the driver, the analysis script, the metrics, the guards and the selection rule | after C3 |

After C4 come the runs (a driver invocation per arm), then a results section, and then, only if the rule selects an arm, an adoption. The code base of the experiment is `main` at `f91f25c` plus C1 to C4.
The trainer used for the runs is built from C4 (no source file changes after it before the runs; the driver records the commit and a dirty flag in every result).

### Why this retraining

Two shapes produce **confident errors** of the shipped model: noun + noun compounds whose head can also be a verb (the head is then tagged VERB), and Title Case headings, captions and step titles, in which a capital letter
is read as a proper noun. The development tranches built for them (`n-compound-head-ambiguous`, `heading-titlecase`, with `n-named-state` as a watched neighbor) give the shipped model 60.0% target accuracy with 390 harmful errors per 1,000
noun-verb decisions and 5 answers wrong at confidence >= 0.95 on the compound tranche, 84.2% with 2 answers wrong at >= 0.95 on the heading tranche, and 90.0% on named-state
(section "new tranches baseline of the shipped model"; reproduced by the smoke scoring noted above: 43 harmful errors of 178 decisions on the two tranches pooled, 241.57 per 1,000, 7 answers wrong at >= 0.95).
The motive is **weakness**, not a tuned fix: the tagger is weakest on these two frames, and the hypothesis is that targeted real text (and a feature that tells the model that a line is in Title Case) lowers the harmful errors. The experiment tests that hypothesis
against the guarded measures, size and speed. Nothing is adopted unless the rule below selects it.

### Retrain: the data (frozen)

`data/compound/compound.tsv`: 742 sentences (noun 631, verb 111 kinds of the head; 14,692 tokens; 462 head lemmas; 154 US federal documents). `data/headings/headings.tsv`: 375 heading lines (noun 315, proper 52, imperative 8; 1,668 tokens; 273 lemmas; 90 documents).
Every token has a tag on which two blind model labelers (`claude-opus-5.5` and `gpt-6.1-sol`) agree; no human reviewed a label; the sources are disjoint from every training and evaluation source (leakage 0/0/0, see the registration section).
Hand check (model review by `claude-sonnet-5.5`): judged word 60 of 60 (compound) and 48 of 48 (headings) before the removal of 7 documents, 58 of 58 and 45 of 45 on the items that remain. Both are registered in `training.json` at weight 0; the arms train them at weight 1 with `--source`.
At weight 1 the sources add 742 and 375 sentences to the 61,680 sentences of the shipped mix (1.2% and 0.6%).

### The feature group F5 (`titleCaseLine`): definition and build evidence

F5 was built (commit C3) because it passed the "simple and deterministic" test. The definition, **fixed before any arm was trained**: a line is Title Case when, among its word tokens (a token with a letter and no digit) with the first word token left out,
there are at least 2 and at least 60% start with an upper-case letter (the integer test `5 x capitalized >= 3 x words`), and the last token of the line is not `.`, `!` or `?`. The last clause keeps name-heavy ordinary sentences
(`John Smith met Mary Jones.`) from being marked; it is part of the registered definition. Two tag-independent features per token: `TitleLine` (0 off the line; on it, 4 plus the capitalization shape bits upper-first or all-upper)
and `TitleLineFirst` (0 off the line, 1 on it, 2 for token 0), so a capital in a Title Case line stops implying a proper noun by itself. `FeatureGroups.TitleCaseLine` = 16 (the next free bit), `FeatureKind.TitleLine` = 40 and `TitleLineFirst` = 41,
the static stride grows by 2 and the bucket capacity is 42; trainer name `titleCaseLine`, short name `F5`. The group is off unless named, and then the model is byte-identical to one trained without it.

Build evidence (the report of the build, gates D1d): the extractor change is 98 added and 9 removed lines in one file, about 50 of them production code, and no feature, bucket order or constant of another group changed; one integer pass per sentence; the
allocation test (0 bytes after warm-up) includes the group; of 366 existing tests of the extractor, decoder, perceptron model, model storage, embedded model and tagger, 363 passed unchanged and 3 had to change
(the two unknown-group-bit cases for 16 and 17, which are now 32 and 33, and the capacity test, which asserts that a token with every feature has exactly the bucket capacity); **`train --check` exit 0 with the image `6e1e4ade...` unchanged**; the byte-identity proof
(`train --seed 20260101 --features clauseCues --calibrate` gives 521,695 bytes with SHA-256 `48dd9188c37fb0ba01458e45a28d7b4b66d6a14b9f57eaf2a621bddc0e9b413b`, equal to the shipped `weights.bin`; 61,680 sentences, 6,357,228 steps);
the smoke `eval-arm` of the shipped weights reproduces the baseline figures above; the full build (4,799 tests, 4 skipped for link resolution on .NET Framework, 0 failed) and the lint were green.
The definition has one tunable constant (60%) and one clause; neither was fitted to any result, because no arm had been trained.

### Retrain: arms and runs

| Arm | Feature groups | Sources added to the shipped mix | Additions |
| --- | --- | --- | --- |
| **BASE** | `clauseCues` | none (the shipped mix: open x2, MASC, silver >= 80, **imperative at weight 1**, exactly `training.json`) | 0 |
| **CMP** | `clauseCues` | `compound:1` | 1 source |
| **CMP-HDG** | `clauseCues` | `compound:1`, `headings:1` | 2 sources |
| **CMP-HDG-F5** | `clauseCues`, `titleCaseLine` | `compound:1`, `headings:1` | 2 sources and 1 feature group |

BASE is the shipped model (the imperative data and the clause cues were adopted by the earlier experiments), so the retraining is judged against the model that ships. The data effect alone is CMP and CMP-HDG against BASE; the effect of F5 alone is CMP-HDG-F5 against CMP-HDG
(reported, not gated). Seeds: **20260101, 11, 12, 13, 14** (the seeds and the order of the earlier experiments), five runs per arm, 20 runs. Every other setting is the shipped `training.json`; the temperature is refit on open dev for every run (`--calibrate`).
The mechanism is the new driver `compound-heading/run_arms.ps1`, which is the frozen driver of the context-feature experiment with four changes only: it reads its own `arms.json`; it appends `--source <name>:1` for each source of the arm after
`--calibrate --output`; it checks the PAUSE file of the session folder before the build and before every run (if it exists the driver stops cleanly with exit code 4 and writes a resume record `resume.txt`); and it has its own documentation comment. The runner is `imperative-data/runner-base.cmd`, **unchanged**.
The arms are run in this order, one invocation at a time, into one folder on C: outside the repository (`<runs>`):

```text
$runner = "<repo>\tools\DemaConsulting.PartOfSpeech.Trainer\imperative-data\runner-base.cmd"
pwsh tools/DemaConsulting.PartOfSpeech.Trainer/compound-heading/run_arms.ps1 -Out <runs> -Arms BASE       -Runner $runner -PyCache <pycache>
pwsh tools/DemaConsulting.PartOfSpeech.Trainer/compound-heading/run_arms.ps1 -Out <runs> -Arms CMP        -Runner $runner -PyCache <pycache>
pwsh tools/DemaConsulting.PartOfSpeech.Trainer/compound-heading/run_arms.ps1 -Out <runs> -Arms CMP-HDG    -Runner $runner -PyCache <pycache>
pwsh tools/DemaConsulting.PartOfSpeech.Trainer/compound-heading/run_arms.ps1 -Out <runs> -Arms CMP-HDG-F5 -Runner $runner -PyCache <pycache>
```

Each run is `train --seed S --features <groups> --calibrate --output <arm>-sS.bin [--source compound:1 [--source headings:1]]`, then the dev-only command `eval-arm`, which scores the weights on the five development sets (open dev, gold v2 dev, UD-EWT dev, both tranches of the shapes development set)
and the three newer tranches (compound-head, headings, named-state), reads no held-out file and writes only its JSON file. The driver is resumable (a run is complete when its JSON exists), requires at least 6 GB free on C: before every run and uses at most 8 threads.
A run that fails without a result is repeated; no run is repeated or replaced because of its result, and no arm is dropped. A dry run of the driver (`-DryRun`) printed the commands of two arms and two seeds and ran nothing; the pause path was tested with a temporary PAUSE file and a temporary output folder.

**Deviation record (Normal priority).** As in the earlier experiments, the runner starts `dotnet` at Normal priority (`start "" /normal /wait /b dotnet %*`), which the project owner allowed, because BelowNormal receives almost no CPU on this shared machine.
It changes the speed of the runs and nothing else. Consequence: the training times and the single-thread throughput are measured on a loaded machine and are not comparable between arms or with earlier records;
the throughput guard is an absolute floor far below the speeds recorded earlier (the smoke scoring of the shipped weights gave 822,427 tokens per second).

### Metrics and where each comes from

A harmful error is a swapped noun-verb class at confidence >= 0.45 on a noun-verb decision; rates are per 1,000 decisions. Every figure is read from the per-run `eval-arm` JSON; the paths below were confirmed against the code that writes the JSON
(`Evaluation/ArmEvaluation.cs`) and against a real result of the shipped weights, and the self-test of the analysis script builds its synthetic results with exactly these paths. All figures are the mean over the five seeds unless stated; spreads are sample standard deviations.

| Role | Quantity | JSON path (per run) |
| --- | --- | --- |
| **Primary** | pooled harmful per 1k over `n-compound-head-ambiguous` and `heading-titlecase` = 1000 x (harmful_c + harmful_h) / (decisions_c + decisions_h); the script re-derives it from the counts and a difference is an invalid run | `shapesNew.pooledPrimary.harmfulPer1k` |
| **Co-primary** | wrong at confidence >= 0.95 summed over the two tranches; an arm's mean must be **<= the BASE mean** (it must not increase) | `shapesNew.compoundHead.shape.confidentWrong` + `shapesNew.headings.shape.confidentWrong` (the script also checks `shapesNew.pooledPrimary.confidentWrongTotal`) |
| Guard | hard tranche harmful per 1k <= BASE + 10 | `shapes.hard.pooled.harmfulPer1k` |
| Guard | standard tranche harmful per 1k <= BASE + 5 | `shapes.standard.pooled.harmfulPer1k` |
| Guard | open dev harmful per 1k <= BASE + 1.5 | `openDev.harmfulPer1k` |
| Guard | UD-EWT dev harmful per 1k <= BASE + 1.5 | `udDev.harmfulPer1k` |
| Guard | UD-EWT dev accuracy >= BASE - 0.15 points | `udDev.accuracy` |
| Guard | `n-named-state` target accuracy >= BASE - 3 points | `shapesNew.namedState.shape.targetAccuracy` |
| Guard | deflated weights bytes <= 1.5 x BASE | `weights.bytesDeflated` |
| Guard | throughput >= 150,000 tokens per second (absolute): the median over the seeds of the runs' medians (each run's value is the median of five timed single-thread passes after one warm-up) | `throughput.tokensPerSecond` |

Reported, not gated: each new tranche's accuracy, harmful rate, wrong at >= 0.95 and control accuracy, the heading kinds (noun, imperative, proper-noun controls), hard and standard accuracy, open dev and gold v2 dev accuracy, NLL, ECE, temperature, rows, bytes,
training seconds, the Welch interval of every arm against BASE on the primary, the co-primary and every guarded measure, the pairwise intervals CMP-HDG against CMP and CMP-HDG-F5 against CMP-HDG, and the per-seed table.
(`eval-arm` writes per-item outcomes only for the older hard and standard tranches, so the paired item bootstrap of the earlier experiments is not part of this analysis.)

### The selection rule (fixed now; applied exactly as written; nothing is tuned afterwards)

1. **Primary** = the five-seed mean of the pooled harmful per 1k above. Lower is better.
2. **Guards and co-primary**, each arm against the **BASE five-seed mean**, all must hold (the table above).
3. **Q** = the arms CMP, CMP-HDG and CMP-HDG-F5 that pass every guard and the co-primary. If Q is empty, BASE stays.
4. **m** = the lowest primary mean in Q. **S** = the arms of Q within 10% relative of m (primary <= 1.10 x m).
5. **Candidate** = the arm of S with the fewest additions in the order CMP (1 source) < CMP-HDG (2 sources) < CMP-HDG-F5 (2 sources and 1 feature group); ties: the lowest primary.
6. The candidate **wins only if the upper bound of the Welch 95% interval** (Welch-Satterthwaite degrees of freedom) **of (candidate - BASE) on the primary is below 0**. **There is no fallback** to another arm. Otherwise BASE stays and nothing ships.

### The analysis script and its self-test

The new, versioned `compound-heading/analyze_compound_heading.py` applies the rule mechanically (`--runs <runs> --out <report.md> --decision <decision.json>`; the report is a Markdown table per arm with the primary mean and standard deviation, the mean wrong at >= 0.95,
each guard's delta with PASS or FAIL, the size ratio and the throughput, then the verdict; it is printed and written to the path given). Its only use of the frozen `context-features/analyze_arms.py` is `welch_diff`, loaded by path after the file's SHA-256 has been checked. `--selftest` passes. It checks the t quantile against
published values, the Welch case by hand, every one of the nine guards (the co-primary included) exactly at its limit and just beyond it (including the size guard at exactly 1.5 x the BASE mean, 1.5 x + 1 byte and 1.6 x, and the 150,000 floor), the 10% window
(an arm at exactly 1.10 x the lowest is inside, at 1.105 x it is outside), the preference order CMP < CMP-HDG < CMP-HDG-F5 and the tie rule, a guard failure or a co-primary failure that removes the arm with the lowest primary, the nothing-qualifies case, BASE staying when every arm equals BASE,
a candidate whose interval contains 0, a worse candidate, the no-fallback case, the strict inequality of the Welch rule (an upper bound of exactly 0 does not win), the pairwise intervals, each acceptance test failing when its condition is broken, invalid runs (missing run, wrong feature groups, a primary that does not
match its counts), a missing arm and a missing training log, and the exit codes. A mutation check (each of 14 changes of a limit, the window, the strict inequality, the preference order or a sentence count applied to the source in memory) made the self-test fail every time.

### Retrain: acceptance tests (run before the rule is applied; a failure stops without a selection)

The script runs them first and makes no selection (exit code 1, no decision file) if one fails:

1. **BASE seed 20260101 is the shipped model.** Its weights SHA-256 (the `weights.sha256` of its result file and the `.bin` of the run when present) equals that of `src/DemaConsulting.PartOfSpeech/Resources/weights.bin`, and its figures equal those of the shipped model:
   compound-head 60.0% with 39 harmful of 100 decisions and 5 wrong at >= 0.95; headings 84.2% with 4 of 78 and 2; named-state 90.0% with 3 of 37; pooled primary 241.57 per 1k and 7 wrong at >= 0.95;
   hard tranche 170.0, standard tranche 15.0; open dev 91 harmful of 4,451 decisions; UD-EWT dev 94 of 4,570.
2. **The sources were really added.** From the line `Training on N sentences (...), sources=...` of every `<run>.train.txt`: BASE lists exactly the shipped mix (`open:2,masc:1,silver:1,imperative:1`) and neither new source; CMP lists the mix plus `compound:1` and trained on exactly **742 more** sentences than the BASE run of the same seed;
   CMP-HDG and CMP-HDG-F5 list the mix plus `compound:1,headings:1` and trained on exactly **1,117 more** (742 + 375). Every result names the feature groups of its arm (the `groups` of the weights header, so the F5 arm's header names both groups, the driver's `features` and `sources`).
3. **Same inputs.** The `inputs.*Sha256` fields of all runs agree and equal the frozen hashes of the eight development files (the five older sets and the three newer tranches).
4. **Frozen files.** The files of the table below (with CRLF normalized to LF for the text files) have their frozen SHA-256; the analysis refuses to run if one differs.

### Retrain: what happens on each outcome

- **BASE stays** (Q is empty, or the candidate's interval is not entirely below 0). The data stay committed at weight 0, F5 stays in the code as an off-by-default group, and the results section records the analysis tables unchanged, the decision "BASE stays" and the caveats.
  No weights change, no `thresholds`, no `evaluate`, no README accuracy change; the log states that no held-out command was run.
- **An arm wins.** An adoption section is written in this log **before any held-out command**: `training.json` gets the winning sources at weight 1 (and, for CMP-HDG-F5, the feature group `titleCaseLine`), the steps of the earlier adoptions follow
  (`train --output <file outside the repository>`, `calibrate --update-config`, `train --update-config`, `train --check`, copy to `weights.bin`; the new `weights.bin` must be byte-identical to the winning arm's run at seed 20260101), then `thresholds`
  (development data only), and **one** `evaluate` of the new model as a **disclosed further look** at sets already seen by this model family (a new record, not an erratum; no second run), with the documents, tables and drift tests updated.
- **An acceptance test fails.** The experiment stops without a selection; the cause is recorded, and any change to the protocol is an amendment written before the affected runs are repeated, never a choice made on a result.
- **A run is missing** (a command failed without a result): it is repeated; the analysis refuses to select with a missing run.

### Retrain: limits stated in advance

- The seeds measure training noise only. Three candidate arms are compared against one baseline, which is less selection than the earlier nine-arm experiment but not none (some winner's curse).
- The two tranches are development sets that were also used for the baseline, and the training data are built from the same frames, so the frames are the direct target of the data; a gain is development evidence of that kind, not an independent test.
- **The `headings` source has almost no imperative mass** (8 of 375 lines: the initial-imperative candidates were mostly read as nouns by both labelers and dropped by the rule). It can therefore teach the noun and proper-noun readings of Title Case words but not the initial-imperative reading,
  and the heading tranche has initial-imperative targets (45 of 95).
- **The compound sample has no contested-band selection**: it is a capped sample of frame hits, not of the cases that the tagger finds hard, so it is easier text than the development tranche, and the effect on the contested items may be smaller than the size of the source suggests.
- The compound source keeps verb-head counter-examples (111 of 742); `n-named-state` and the standard tranche are the watched neighbors for a shift of harm, through the guards.
- The text is federal text of other registers and eras than the development sets (documents digitized by the Internet Archive); the labels are model-written without human review (token error in the model review: 0.24% for `compound`, 0.44% for `headings`).
- Throughput and training time are measured on a loaded shared machine at Normal priority (the deviation record above).
- F5's definition (including the 60% threshold and the final-mark clause) was fixed before the runs; if F5 does not win, it stays off.

### Retrain: held-out sets

The held-out sets (open test, gold v2 test, shape probe, STE challenge set, UD-EWT test) are **not used by this experiment**: the driver reads only the development files, and `eval-arm` has no path to a held-out file. A held-out command is run if and only if an arm wins (above).

### Retrain: inputs frozen by this commit

SHA-256 of the files as committed (line endings LF; `runner-base.cmd` is checked out with CRLF by `.gitattributes` and is hashed after normalizing to LF; `weights.bin` is binary and hashed as bytes):

| File | SHA-256 |
| --- | --- |
| `compound-heading/arms.json` (new) | `7fd58517b64127fe3892e928939d6896d8a0ab5e499ce696f6cb781a01bcc4cf` |
| `compound-heading/run_arms.ps1` (new driver) | `3d20022cc7a6d7e1ddefcc5abaeb0a740fb8f186c63ff25e73d50187b57cbcbd` |
| `compound-heading/analyze_compound_heading.py` (new, versioned) | `1265e23196f38eb2fc0cab642007a646582f947ffbb54326d6f6ef4e75048137` |
| `context-features/analyze_arms.py` (frozen; imported for `welch_diff` only) | `3d52825384b440c5786e1b241fc6f814e4e47a63f155414e0d6450107d33e494` |
| `imperative-data/runner-base.cmd` (unchanged) | `549a2c6ed8d1eaf00b30551fae14e546c51023dd79fe3a77810f39a1fe79ff3e` |
| `data/compound/compound.tsv` (742 sentences) | `eb8c4708ba95fb370c885110e10eda0f1d826c5922876bd66e69389894bd5e3b` |
| `data/compound/sources.tsv` | `8b861dd3ff2d5a3fd5e741f5fe2a906807f99afa562d7e294d4011d93ef15e02` |
| `data/headings/headings.tsv` (375 lines) | `5931072928829e1911468173a181761ba001c4d7f16a8492391404395649124c` |
| `data/headings/sources.tsv` | `428a73f0581ec168cdef63d885cd6e001e925813b0a0d0dd1148f79636935aff` |
| `tools/DemaConsulting.PartOfSpeech.Trainer/training.json` (the two new sources at weight 0; image `6e1e4ade56aa277d79709c107000f10e99c6f4e55a097e872a9c38fa2e12e8d0`) | `96ad8178b5f0bb621ee0723bc1f32da7ef5859218c14509ce6005d8a8354ca3f` |
| `src/DemaConsulting.PartOfSpeech/Resources/weights.bin` (the shipped model) | `48dd9188c37fb0ba01458e45a28d7b4b66d6a14b9f57eaf2a621bddc0e9b413b` |
| `data/eval/shapes-dev/dev.tsv` (standard tranche) | `bbc94e42693d92a7e948598d5ea8a2c58274d3e4e5e6e4d0ed7d8a5c10963290` |
| `data/eval/shapes-dev/hard.tsv` (hard tranche) | `c041ae3d8b4b948326d002176bdffa0e5eb2ac0a6fe7d12daed7c3d289db8b22` |
| `data/eval/shapes-dev/compound-head.tsv` | `537e0be3923f585b8f084de4e88f4064624e4f5dc484f84ccd303fed3db924f5` |
| `data/eval/shapes-dev/headings.tsv` | `45a62a71837f02d1e76f0ad55a71a1770cb628ea8ca8765c0d4583f49b9da72a` |
| `data/eval/shapes-dev/named-state.tsv` | `e08472387630368b7fb3fbaa5cd7b6187d0760608c93b55f19250c98f2d06d6e` |
| `data/open/dev.tsv` | `8caa822515cee179093f068ff4ea7fa62714830d4c50de696bd4403037fc05c7` |
| `data/eval/gold-v2/dev.tsv` | `ffb2f3afd3460523656f5528a66094bad507982b15eb2b4a9341cdb78ff117f5` |
| `tools/data/cache/ud-ewt/ud_dev.tsv` | `0fc0b4a371ef6d0437e01e396762cdb600d0d7ff0d725ead00ac9f3c41f82037` |

(The paths of the first five rows are relative to `tools/DemaConsulting.PartOfSpeech.Trainer/`.) `analyze_compound_heading.py --selftest` passes. The trainer commit that produced each result is recorded in its JSON (`inputs.commit`, with the dirty flag), and the folders of the runs lie outside
the repository; their files are not part of the review set.

## Compound and heading training data: results (decision: BASE stays)

Written after the 20 runs and the analysis of the pre-registration commit `9377ff933e9aeda4be99b04a97b44e7eabb7a58e` (C4). The tree was clean and HEAD was C4 when the first run started and when the analysis ran;
every result file records `inputs.commit` = `9377ff9...` with `dirty` = false. Four arms, five seeds each (20260101, 11, 12, 13, 14), 20 runs, on 2026-11-10; the commands were exactly those of the pre-registration (the frozen driver
`run_arms.ps1` with `runner-base.cmd`, one invocation per arm, in the registered order BASE, CMP, CMP-HDG, CMP-HDG-F5). No run was repeated, replaced or dropped, and no frozen file was edited. The frozen `analyze_compound_heading.py` was applied
to the 20 result files and the selection rule was applied mechanically. **No held-out set was read and no held-out command (`evaluate`, `thresholds`, `train --update-config`) was run.** No weights, operating points or README accuracy table changed.

### Retrain results: order of the commits

| Step | Commit | What it contains |
| --- | --- | --- |
| C1 | `e776f5693739900d91e63fd2a12451da1f4ba90b` | the builder, its tests and the selection rule, before any label existed |
| C2 | `7e82e2d75e676aae2477d46131159b24cdd5673c` | the labeled data and their registration at weight 0 |
| C3 | `9a6d10b4e7519c36bbd6c42a728f3858214a818e` | the feature group F5 (`titleCaseLine`, off by default) and the `shapesNew` block of `eval-arm` |
| C4 | `9377ff933e9aeda4be99b04a97b44e7eabb7a58e` | the pre-registration: arms, driver, analysis script, metrics, guards, selection rule |
| **C5** | this commit | this results section and the documents of the outcome (the SHA is recorded by the pull request, because a commit cannot contain its own SHA) |

### Retrain results: acceptance tests (all passed; the analysis made a selection only after them)

All checks of the pre-registration passed. (a) The BASE run at seed 20260101 has weights SHA-256 `48dd9188c37fb0ba01458e45a28d7b4b66d6a14b9f57eaf2a621bddc0e9b413b`, equal to the shipped `weights.bin` (file and result field, byte identical), and the figures of the shipped model
(compound-head 60.0% with 39 harmful of 100 decisions and 5 wrong at >= 0.95; headings 84.2% with 4 of 78 and 2; named-state 90.0% with 3 of 37; pooled primary 241.57 per 1k and 7 wrong at >= 0.95; hard tranche 170.0, standard 15.0; open dev 91 harmful of 4,451; UD-EWT dev 94 of 4,570).
(b) Every BASE run trained on 61,680 sentences with the sources `open:2,masc:1,silver:1,imperative:1`; every CMP run on exactly 742 more sentences (`compound:1`); every CMP-HDG and CMP-HDG-F5 run on exactly 1,117 more (`compound:1,headings:1`).
(c) The eight `inputs.*Sha256` fields agree in all 20 runs and equal the frozen hashes, and the frozen files have their frozen SHA-256.

### Results: the arms

Values are the mean ± sample standard deviation over the five seeds; the baseline is BASE. Primary = pooled harmful per 1,000 noun-verb decisions over `n-compound-head-ambiguous` (100 decisions) and `heading-titlecase` (78 decisions); co-primary = wrong at confidence >= 0.95 summed over the two tranches.

| Arm | Feature groups | Sources added | Primary | Co-primary (wrong at >= 0.95) | Pooled target accuracy % |
| --- | --- | --- | ---: | ---: | ---: |
| BASE | `clauseCues` | none | 239.33 ± 10.21 | 6.80 ± 1.48 | 71.08 ± 1.18 |
| CMP | `clauseCues` | compound | 221.35 ± 21.61 | 4.80 ± 1.48 | 73.33 ± 1.78 |
| CMP-HDG | `clauseCues` | compound, headings | 237.08 ± 20.79 | 4.80 ± 1.92 | 73.64 ± 1.94 |
| CMP-HDG-F5 | `clauseCues`, `titleCaseLine` | compound, headings | 243.82 ± 18.89 | 3.00 ± 2.00 | 74.15 ± 1.94 |

Per tranche (target accuracy %, harmful per 1k, wrong at >= 0.95, control accuracy %; the controls are 100.0% in every arm):

| Arm | Compound-head: acc | harmful | wrong >= 0.95 | Headings: acc | harmful | wrong >= 0.95 |
| --- | ---: | ---: | ---: | ---: | ---: | ---: |
| BASE | 59.8 ± 0.8 | 392.0 ± 8.4 | 5.20 ± 1.48 | 82.9 ± 1.7 | 43.6 ± 14.6 | 1.60 ± 0.55 |
| CMP | 63.2 ± 3.9 | 360.0 ± 36.7 | 3.80 ± 1.30 | 84.0 ± 1.6 | 43.6 ± 14.6 | 1.00 ± 0.71 |
| CMP-HDG | 61.8 ± 3.0 | 374.0 ± 27.0 | 4.20 ± 1.92 | 86.1 ± 1.4 | 61.5 ± 16.7 | 0.60 ± 0.55 |
| CMP-HDG-F5 | 64.6 ± 3.4 | 344.0 ± 33.6 | 3.00 ± 2.00 | 84.2 ± 0.7 | 115.4 ± 0.0 | 0.00 ± 0.00 |

Heading kinds (diagnostics, not gated; the heading tranche has 45 initial-imperative targets of 95, and the proper-noun controls are 100.0% in every arm):

| Arm | Noun: acc | harmful per 1k | wrong >= 0.95 | Imperative: acc | harmful per 1k | wrong >= 0.95 |
| --- | ---: | ---: | ---: | ---: | ---: | ---: |
| BASE | 75.6 ± 3.3 | 28.0 ± 17.9 | 1.60 ± 0.55 | 91.1 ± 1.6 | 71.4 ± 25.3 | 0.00 |
| CMP | 77.2 ± 1.1 | 24.0 ± 8.9 | 1.00 ± 0.71 | 91.6 ± 2.4 | 78.6 ± 29.9 | 0.00 |
| CMP-HDG | 83.2 ± 1.1 | 28.0 ± 11.0 | 0.60 ± 0.55 | 89.3 ± 2.9 | 121.4 ± 54.2 | 0.00 |
| CMP-HDG-F5 | 89.2 ± 2.3 | 40.0 ± 0.0 | 0.00 | 78.7 ± 2.5 | 250.0 ± 0.0 | 0.00 |

Per-seed values (primary / wrong at >= 0.95):

| Arm | 20260101 | 11 | 12 | 13 | 14 |
| --- | ---: | ---: | ---: | ---: | ---: |
| BASE | 241.6 / 7 | 224.7 / 9 | 236.0 / 5 | 252.8 / 7 | 241.6 / 6 |
| CMP | 258.4 / 7 | 219.1 / 5 | 213.5 / 5 | 213.5 / 4 | 202.2 / 3 |
| CMP-HDG | 230.3 / 2 | 269.7 / 6 | 230.3 / 7 | 241.6 / 4 | 213.5 / 5 |
| CMP-HDG-F5 | 264.0 / 4 | 264.0 / 2 | 230.3 / 1 | 224.7 / 2 | 236.0 / 6 |

### Results: guards against BASE

Each cell is the arm's delta against the BASE mean and the verdict (BASE: hard harmful 179.0, standard harmful 13.3, open dev harmful 20.09, UD-EWT dev harmful 20.74, UD-EWT dev accuracy 92.99, named-state target accuracy 92.2, deflated 522,272 bytes).
Every arm passes every guard and the co-primary.

| Guard (limit) | CMP | CMP-HDG | CMP-HDG-F5 |
| --- | --- | --- | --- |
| wrong at >= 0.95 (<= BASE) | PASS -2.00 | PASS -2.00 | PASS -3.80 |
| hard harmful per 1k (<= BASE + 10) | PASS -9.50 | PASS -13.00 | PASS -13.00 |
| standard harmful per 1k (<= BASE + 5) | PASS +0.67 | PASS -0.67 | PASS -2.67 |
| open dev harmful per 1k (<= BASE + 1.5) | PASS +0.49 | PASS -0.04 | PASS -0.58 |
| UD-EWT dev harmful per 1k (<= BASE + 1.5) | PASS -1.01 | PASS -0.88 | PASS -0.53 |
| UD-EWT dev accuracy (>= BASE - 0.15) | PASS -0.03 | PASS +0.02 | PASS -0.05 |
| named-state target accuracy (>= BASE - 3) | PASS +1.60 | PASS +2.80 | PASS +0.80 |
| deflated bytes (<= 1.5 x BASE) | PASS 1.011x | PASS 1.016x | PASS 1.043x |
| tokens per second (>= 150,000, absolute) | PASS 789,206 | PASS 765,740 | PASS 753,145 |

Reported, not gated (open dev, gold v2 dev, model and training):

| Arm | Open acc % | Open harmful per 1k | Open NLL | Open ECE % | Gold acc % | Gold harmful per 1k | Temperature | Rows | Raw bytes | Train s |
| --- | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: |
| BASE | 96.67 ± 0.06 | 20.09 ± 1.39 | 0.1117 ± 0.0005 | 0.25 ± 0.02 | 95.15 ± 0.10 | 22.3 ± 0.9 | 9.392 ± 0.112 | 122,214 ± 264 | 1,158,701 ± 2,142 | 11.1 ± 0.1 |
| CMP | 96.67 ± 0.02 | 20.58 ± 1.10 | 0.1109 ± 0.0003 | 0.21 ± 0.02 | 95.17 ± 0.10 | 19.0 ± 4.4 | 9.514 ± 0.000 | 123,546 ± 155 | 1,172,423 ± 760 | 11.1 ± 0.1 |
| CMP-HDG | 96.71 ± 0.06 | 20.04 ± 0.93 | 0.1109 ± 0.0009 | 0.30 ± 0.08 | 95.20 ± 0.08 | 20.3 ± 1.5 | 9.432 ± 0.112 | 124,319 ± 186 | 1,179,160 ± 1,815 | 11.2 ± 0.2 |
| CMP-HDG-F5 | 96.73 ± 0.03 | 19.50 ± 1.14 | 0.1110 ± 0.0004 | 0.23 ± 0.04 | 95.15 ± 0.07 | 21.6 ± 2.9 | 10.241 ± 0.122 | 126,183 ± 257 | 1,204,322 ± 2,568 | 11.3 ± 0.1 |

Hard and standard tranche accuracy (reported): hard 76.50, 77.30, 79.10, 78.50 and standard 97.73, 97.80, 97.93, 98.27 for BASE, CMP, CMP-HDG and CMP-HDG-F5. Named-state wrong at >= 0.95: 0.60, 0.00, 0.20 and 0.20 in the same order.
The training times and the single-thread throughput were measured on a loaded shared machine at Normal priority (the deviation record of the pre-registration); the training times do not separate the arms.

### Results: Welch 95% intervals of (arm - BASE)

Difference of the means with the Welch-Satterthwaite t interval over the seeds (seeds measure training noise only). The primary is the only interval the rule uses; the others are reported, and with eight measures for each of three arms
an interval that excludes 0 in one cell is not evidence by itself.

| Arm | Primary | Wrong at >= 0.95 | Hard harmful | Standard harmful | Open dev harmful | UD-EWT dev harmful | UD-EWT dev accuracy | Named-state accuracy |
| --- | --- | --- | --- | --- | --- | --- | --- | --- |
| CMP | -17.98 [-44.47, +8.52] | -2.00 [-4.16, +0.16] | -9.50 [-20.84, +1.84] | +0.67 [-3.79, +5.12] | +0.49 [-1.35, +2.34] | -1.01 [-1.48, -0.53] | -0.03 [-0.07, +0.01] | +1.60 [-1.05, +4.25] |
| CMP-HDG | -2.25 [-27.78, +23.29] | -2.00 [-4.53, +0.53] | -13.00 [-23.19, -2.81] | -0.67 [-5.00, +3.67] | -0.04 [-1.82, +1.73] | -0.88 [-1.79, +0.04] | +0.02 [-0.06, +0.10] | +2.80 [+0.30, +5.30] |
| CMP-HDG-F5 | +4.49 [-18.86, +27.84] | -3.80 [-6.41, -1.19] | -13.00 [-29.64, +3.64] | -2.67 [-8.20, +2.86] | -0.58 [-2.45, +1.28] | -0.53 [-1.60, +0.55] | -0.05 [-0.11, +0.01] | +0.80 [-1.76, +3.36] |

Pairwise contrasts (reported, not gated): CMP-HDG minus CMP on the primary +15.73 [-15.21, +46.67] and on wrong at >= 0.95 +0.00 [-2.53, +2.53];
CMP-HDG-F5 minus CMP-HDG on the primary +6.74 [-22.27, +35.76] and on wrong at >= 0.95 -1.80 [-4.66, +1.06]. The effect of the headings source (CMP-HDG against CMP) and of F5 (CMP-HDG-F5 against CMP-HDG) on the primary is therefore
not separable from training noise either, and the sign of both point estimates is toward more harm.

### Decision: BASE stays

The rule of the pre-registration, applied step by step:

1. Primary = the five-seed mean (table above).
2. Guards and the co-primary against the BASE mean: all three arms pass all nine.
3. Q = {CMP, CMP-HDG, CMP-HDG-F5}; Q is not empty.
4. m = the lowest primary in Q = CMP (221.35); S = the arms of Q with primary <= 1.10 x m = 243.48: CMP and CMP-HDG (CMP-HDG-F5 at 243.82 is just outside).
5. Candidate = the arm of S with the fewest additions = CMP (1 source).
6. Welch 95% interval of (CMP - BASE) on the primary: -17.98 [-44.47, +8.52], df 5.70, p 0.146. The upper bound is not below 0, so CMP does not win; there is no fallback to another arm.

**BASE stays and nothing ships.** The shipped model, its operating points, the confidence-threshold document and the README accuracy tables are unchanged, and no held-out command was run. The two data sources stay in the repository at weight 0
(they are not in the shipped mix and `train --check` reproduces the shipped image `6e1e4ade...`), and F5 stays in the code as a group that is off in the shipped model; the model trained without it is byte-identical to the one trained before F5 existed.

| File | SHA-256 |
| --- | --- |
| `report.md` (the analysis; 16,403 bytes) | `147a2a9868f4b90183beb768522b39aa6596e2d9a0677f0e35cf83779c237a0b` |
| `decision.json` (per-arm values, intervals, guards, decision; 12,845 bytes) | `1df109994fd35504b0454f5071ed5cafa2282d8fb2449404aea9663f3bfddb25` |

Both files lie outside the repository next to the run folders. The command was:

```text
python tools/DemaConsulting.PartOfSpeech.Trainer/compound-heading/analyze_compound_heading.py --runs <runs> --out report.md --decision decision.json
```

### Reading of the result (what can and cannot be said)

- **What the figures show.** All three arms lower the number of answers wrong at confidence >= 0.95 against BASE (the point estimates are the co-primary column; the interval of CMP-HDG-F5, -3.80 [-6.41, -1.19], excludes 0, those of CMP and CMP-HDG do not),
  and every arm passes every guard. The point estimate of the primary is lower for CMP and CMP-HDG and higher for CMP-HDG-F5, and none of the three intervals excludes 0. The data are therefore **not shown to help** on the primary measure, and also not shown to hurt anywhere that is guarded.
- **What the figures cannot show.** The seeds measure training noise only, not the choice of the 178 development decisions: the compound tranche has 100 noun-verb decisions and the heading tranche 78, so one decision moves the pooled primary by 5.6 per 1,000.
  The sample of seeds is five per arm, which gives the wide intervals above. The tranches are the frames the data were built to target, so a gain would have been development evidence only, and the experiment gives no evidence of the effect on text that is not of these frames.
- **A trade that the aggregate hides.** The `headings` source holds 8 initial-imperative lines of 375, and the heading-imperative diagnostic worsens as the heading data and F5 are added (imperative harmful per 1k 71.4 in BASE, 121.4 in CMP-HDG, 250.0 in CMP-HDG-F5; imperative accuracy 91.1, 89.3, 78.7)
  while the noun-heading accuracy improves (75.6, 83.2, 89.2). A plausible reading, not tested here, is that the model learns that a capitalized word in a Title Case line is mostly a noun, which helps the noun headings and costs the initial imperatives, for which the source has almost no examples. F5 lowers the confident errors (3.00) but raises the temperature of the calibrated model (10.24 against 9.43) and the harmful rate on the heading tranche
  (115.4 per 1k against 61.5 and 43.6 for the arms without it), so the confidence of its answers is lower. The aggregate primary averages these effects.
- **No winner is a statement about this data and this rule,** not about the idea: the rule asked for an interval entirely below 0 on a small development measure, and the best arm, CMP, has an upper bound of +8.52.

### Deviations of the whole experiment (all disclosed above and in the earlier sections)

- 7 documents were removed from both sources after the labels were decided, because their author field lists a state or local government body (compound 800 to 742 sentences, headings 408 to 375 lines); the builder option `--omit-document` removes without adding anything, and the 5 hand-checked items of those documents were all judged correct.
- The heading floors were waived by the pre-registered `--allow-short`: the imperative stratum has 8 lines of the floor 100 (the labelers read 346 of 356 initial-imperative candidates as nouns, adjectives or proper nouns and the class rule dropped them).
- The hand check of the headings source holds 48 items instead of 60 because the imperative stratum has only 8 lines.
- Normal process priority for the runs (the owner's allowance, recorded in the pre-registration).
- No held-out look: the held-out sets were never used by this experiment, so it adds no entry to the list of looks.
- The driver `compound-heading/run_arms.ps1` is frozen by SHA-256, and its parameter defaults `$PyCache` and `$PauseFile` are paths of the session that ran the experiment (a folder under the session state of that machine). They are overridden by `-PyCache` and `-PauseFile` (the recorded commands pass `-PyCache`), and a reproduction must pass its own values. The file is not edited because that would change its frozen hash.
- One heading line of the data of the arms (1 of the 375 lines, `headings-0150`, from the National Bureau of Standards Handbook 65, a document whose record also lists a private non-profit organization as a co-author) was later removed by the project owner; see "Amendment after the result: removal of one heading line" below. The arms were run with that line included.

### Open questions and possible follow-ups

- **More initial-imperative heading data.** A source with real initial imperatives in Title Case headings (step titles, procedure headings) would test whether the loss in the imperative diagnostic is a property of the thin stratum. It needs a candidate rule that does not rely on the first word being read as a noun by the labelers.
- **A larger paired tranche.** A development tranche with several hundred compound and heading decisions would shrink the intervals; the 178 decisions cannot separate effects of about 10% from noise.
- **A later mix decision.** The data stay at weight 0 and the arms are reproducible from the pre-registration commit (`--source compound:1 --source headings:1`); the choice to include a source in the shipped mix can be taken later on a larger measure, with a new pre-registration.
- **F5 on its own.** F5 with the compound data only (not run here) would separate its effect from that of the headings source.

## Compound and heading training data: amendment after the result (2026-11-10)

### Amendment after the result: removal of one heading line

On 2026-11-10, after the results above were recorded, the project owner removed one document from `data/headings/`: `nbs-safehandlingofbo65unit`, *Safe handling of bodies containing radioactive isotopes*, National Bureau of Standards Handbook 65 (1958). Its record lists the National Committee on Radiation Protection and Measurements, a private non-profit organization, as a co-author, so the public-domain basis of the text (17 U.S.C. 105 covers a work of the US government, not the text of a private co-author) is uncertain. The document gave 1 of the 375 lines (`headings-0150`, noun stratum); it is not in `compound` and it was not in the hand-check sample of either source. The earlier review of the author fields had removed the 7 documents with a state or local government body (see above) and had not caught this one.

- **What was done.** The document is recorded with the reason "private non-profit (NCRP) co-author; public-domain basis uncertain; removed by owner decision" in `data/eval/shapes-dev/excluded-sources.tsv` (the exclusion record that every data check and builder reads; the checks fail when a used source table or sentence document matches it) and in `data/headings/excluded-documents.tsv`. `data/headings/` was built again with `build --source headings --allow-short` and the 8 `--omit-document` options (the 7 earlier ids and this one), from the unchanged decisions: nothing was labeled, decided, sampled or re-drawn again. The removal only takes the line out after the caps; the other 374 lines, their order and their content are identical to the 375 of the experiment (checked line by line, ignoring the renumbered `sent_id`), and the ids `headings-0001` to `headings-0374` have no gap. To make this possible the builder got an optional reason per omitted document (`--omit-document ID=REASON`), it leaves the omitted documents out of the draft gate, and `shapes_dev.read_decided` takes a `keep` list, so that an omitted document that is also excluded is removed after the caps like the others and no other line takes its place (without it the exclusion alone dropped the 3 candidates of the document before the caps and another line took the place of the kept one).
- **The data now.** `headings`: 374 lines (noun 314, proper 52, imperative 8), 1,664 tokens, 272 lemmas, 89 documents (family `nbs`: 1 document, 5 lines). SHA-256 of `data/headings/headings.tsv` `cae45ff12cb4e7e9531a007beb8278b4014a8bdab9ae3e52b06ba46df4d48cde` and of `data/headings/sources.tsv` `26ca020469ee3a396ba71bd7845030a516e612c44e887933b1fe8450445fbad7`. Lexicon coverage: 1,508 of 1,565 tokens (96.36%) and 895 of 948 pairs (94.41%). The spot-check figures of the retained lines are unchanged (45 of 45), because the removed line was not in the sample. `compound` (742 sentences) is unchanged.
- **The arms were run with the line.** The results above (the 20 runs, the analysis, the decision "BASE stays") were computed with the data as they were: the headings source of the arms CMP-HDG and CMP-HDG-F5 had 375 lines, 1 of them from this document (1,117 added sentences in total). No arm shipped, and the source has weight 0 in `training.json`, so no weights, operating points or tables depend on it; no run was repeated. The removed line is 1 of 375 (0.27%) of the added sentences, and the conclusion that no arm qualified does not rest on it.
- **The frozen hashes describe the data as it was at the retrain.** The table "Retrain: inputs frozen by this commit" lists the hashes of `data/headings/headings.tsv` and `data/headings/sources.tsv` as they were when the arms were trained; they are not the hashes of the committed files any more, and they are not edited. The frozen analysis script and the arm configuration were not changed: `analyze_compound_heading.py` run on the runs against the current `data/headings/` reports its frozen-file acceptance test as failed (by design: those two hashes differ), and its `--selftest` reports the same two frozen-file checks (the two hashes and the line count of the headings file) as failed while every other check (the synthetic runs) passes. The earlier decision is reproduced from the stored `report.md` and `decision.json` (hashes above), not from a new run of the analysis on the current files. `data/MANIFEST.tsv`, `data/training-leakage-report.tsv` and the documents with the counts carry the new figures.
- **Checks after the removal.** `check_leakage.py --write-report` 0 / 0 / 0 against every evaluation set and none against the other training sets (the pairwise section shows 374 headings lines; the 73 shared documents of `compound` and `headings` are unchanged); `check_licenses.py` clean and, before the rebuild, finding both the source row and the sentences of the document through the new exclusion row (the fail-closed path); `check_conventions.py --tier fix --enforce --apply` 0 and `--tier check` 0 hits in `headings`; `make_manifest.py --verify` 59 files; `train --check` unchanged image, because the source has weight 0.

## Follow-up: pre-registered rule (before any fetch)

This section is written and committed **before any document of the follow-up is fetched**. At this commit no new document exists, no candidate row has been written, no batch has been exported, no model has tagged a token of new text, and no model of any experiment has been trained or evaluated. The only things run on the new work are the Python unit tests of the builder and the unchanged `train --check` (exit 0, image `6e1e4ade...`, the baseline). The code base is `main` at `7dba3c4` (PR #19); the work is on the branch `feature/compound-heading-followup`. The git history shows the order: this commit (R0: the builder `tools/data/followup_data.py`, its tests and this rule), then the frozen inputs (R1), the pilot record (R2), the pool sizes after the scaling (R3), the data and their registration, and only then, in a separate pull request, the pre-registration of the re-test (E1). A commit cannot contain its own SHA: the next commit records the SHA of this one. The normative text of the rule is the docstring of `tools/data/followup_data.py` at this commit; this section is the same rule in short, plus the choices that are not code.

### Follow-up goal and motive

PR #19 kept BASE: the compound development measure had 100 targets, the arms moved by amounts inside the noise, and the real Title Case imperative headings were almost absent (the diagnosis of that run: 353 of the 356 initial-imperative candidates had the old *strength 1* frame, a capitalized noun-capable word after the first word, and the labelers read 348 of those as nouns, adjectives or names; only 3 *strength 0* lines existed in 196,019 stand-alone lines). The follow-up asks four things, each with a gate stated here: (1) a bigger compound development measure (about 300 targets in all, controls about 20%); (2) more compound training data (toward about 1,500 sentences); (3) real Title Case imperative headings from procedural text (a pilot of about 150 lines first, about 300 training lines, a development measure of 100 or more); (4) a pre-registered 8-seed re-test of BASE, `+CMP2`, `+CMP2+HDG2`, `+CMP2+HDG2+F5` and the optional `+CMP2+F5`; (5) adoption, retraining, `thresholds` and one `evaluate` only if a winner qualifies, otherwise the data stay at weight 0 and the record is committed. **No sentence supplied by any consumer is read, quoted or used**: every sentence comes from a recorded US federal public-domain document, and nothing of the earlier consumer report is consulted.

### Owner decisions (all taken by default, recorded here; the owner can overrule before the corresponding commit)

| # | Decision | Taken as |
| --- | --- | --- |
| U1 | Selection rule wording | **Rule B** (literal reading of the request): Q = the arms that pass every guard and whose Welch 95% interval of (arm - BASE) on the primary has an upper bound below 0; m = the lowest primary in Q; S = the arms of Q within 10% of m; the winner has the fewest additions in S (ties: the lowest primary); no arm in Q means BASE stays. The re-test commit (E1) states it again with its self-test. |
| U2 | Casing of step titles | **Title Case only** (shape `heading-titlecase`). ALL-CAPS and sentence-case step titles are out of scope (a stated limit). |
| U3 | Labeler prompt and the PAUSE rule | The frozen prompt of PR #19 is unchanged; the sub-agent appendix (the PAUSE rule verbatim, the IP rule and the carve-out for the PAUSE path) comes after it with its own hash; the **orchestrator performs the PAUSE check before every launch** and updates `STATE.md`; a launched labeler writes nothing and replies "PAUSED" if the file exists. The template hash therefore covers the frozen block only. |
| U4 | Distribution statements of military documents | **Reject** a distribution statement other than public release, FOUO or an export-control or classification marking (conservative public-release rule, not a copyright rule). |
| U5 | "Toward about 1,500" | `compound` (742, unchanged) + `compound2` (**at least 760 new**; floor 600 with `--allow-short`), **no top-up from unused sentences of already vetted documents**: the shortage is disclosed, never padded. |
| U6 | Exclusion record, contrast stratum, fifth arm | **Approved by default**: add the 7 state or local documents of `data/compound/` and `data/headings/` (`excluded-documents.tsv`) to `data/eval/shapes-dev/excluded-sources.tsv` (done in the data commit, with reason text and date; until then the used-document index of the new tool already holds them through `excluded-documents.tsv` and the old sourcing draft); the `initial-noun` contrast stratum of `headings2` (capped at half of the kept imperative lines); the optional fifth arm `CMP2-F5`. |

### The products (frozen by this commit)

| Product | Kind | Files | Frame and selection | Floors and caps |
| --- | --- | --- | --- | --- |
| `compound-head-2` | development tranche | `data/eval/shapes-dev/compound-head-2.tsv`, `-sources.tsv`, `-leakage-report.tsv`; ids `shapes-dev-compound-head-2-NNNN`; shape `n-compound-head-ambiguous` | the frame `compound_ambiguous_hit` unchanged; the contested band 0.10 <= q < 0.97 of the pinned WordSense Basic mass, q ascending (the Basic mass is another project's model, as in PR #18; no PartOfSpeech model); at most 4 targets per head lemma **over the union with the 100 old targets**, 800 target candidates, controls (no band, 2 per lemma) 50 in a first round and at most 30 in a second round (`chd2c`) for the lemmas that have room | targets 150 to 210, controls 30 to 45, `-ing` heads at most 25% of the union of 310 |
| `headings-2` | development tranche | `data/eval/shapes-dev/headings-2.tsv`, `-sources.tsv`, `-leakage-report.tsv`; ids `shapes-dev-headings-2-NNNN`; shape `heading-titlecase`, kinds imperative, noun, proper (controls) | imperative frame F-IMP (below), noun and proper frames of `heading_hits` unchanged; candidates imperative 300, noun 120, proper 60, 4 per lemma over imperative and noun, 20 per document; a line near a training heading line is dropped (Jaccard 0.8) | imperative 80 to 130, noun 30 to 50, proper controls 12 to 20, 4 per lemma and 12 per document |
| `compound2` | training source | `data/compound/compound-2.tsv`, `-sources.tsv`, `-excluded-documents.tsv`, `-dropped-sentences.tsv`; ids `compound2-NNNN` | every frame hit in the seeded order, no band; at most 6 candidates per head lemma, 3,400 in all (gate: at least 2,000 candidates and 400 head lemmas) | 600 to 1,100 sentences, at least 200 head lemmas, 4 per lemma, 20 per document, verb items at most 15% |
| `headings2` | training source | `data/headings/headings-2.tsv`, `-sources.tsv`, `-excluded-documents.tsv`, `-dropped-sentences.tsv`; ids `headings2-NNNN` | strata `imperative` (frame F-IMP) and the contrast stratum `initial-noun`; candidates imperative at most 900 (gate: at least 300; 6 per lemma, 40 per document), initial-noun at most 450 | imperative 150 to 400, initial-noun at most half of the kept imperative lines, 4 per lemma and 25 per document over both strata |

`compound.tsv` and `headings.tsv` stay byte-identical (PR #19 stays reproducible). The arm `CMP2` adds `compound:1` and `compound2:1`; `HDG2` adds `headings:1` and `headings2:1`; all four sources keep weight 0 in `training.json` until a pre-registered rule selects them.

### Follow-up rule (fixed now; applied as written; nothing is tuned after a label exists)

The normative text is the docstring of `tools/data/followup_data.py` at this commit.

1. **Documents.** US federal authorship with a public-domain basis checked one by one and written in `license_basis` (17 U.S.C. 105; the agency named in the record; the record's own rights statement; the NTRS rights record for NASA). No FAA material; nothing in either `excluded-sources.tsv` (the committed one and `W/excluded-sources.tsv`); nothing in any `excluded-documents.tsv`; **no document with a state, local, foreign, university, contractor or private creator, publisher or co-author (the author, creator and publisher fields of every record are read, not only the title)**; no copyright notice or third-party credit; a distribution statement other than public release, FOUO or an export or classification marking is a reason to reject (U4). A host that refuses is skipped, never worked around. The used-document index (`followup_data.used_index`) holds **all** earlier tables: open, gold v2, silver, MASC, all five shapes-dev tables, the imperative source, `data/compound` and `data/headings` (the sources and the exclusion records of both, which the builder of PR #19 left out so that a rebuild would not reject itself) and every `--extra-table`; a document with 3 or more sentence keys equal to a training, evaluation or shapes-dev sentence is rejected as a whole (the held-out sets enter only as the opaque hash set of `LeakageFilter` and as probe 5-grams). `followup_data.Reference` adds the sentences of `compound.tsv` and `headings.tsv`.
2. **Partition (before any label).** The family key is the lower-case title without parentheses, years, volume, edition, change, `skill level` and series numbers. The side of a family is `dev` when `rank_of(20261110, "partition", family key)` read as a fraction of 16^12 is below the DEV share of its stream: **stream C 0.35, stream H 0.30**; the H share is provisional until the R1 commit and is never changed after it; a family with a member of stream H counts as stream H. All members of a family are on one side; DEV documents feed only `compound-head-2` and `headings-2`, TRAIN documents only `compound2` and `headings2`; no document is on both sides (a build refuses a document that is in a product of the other side).
3. **Heading lines.** (a) a stand-alone line (`heading_lines`), (b) a table-of-contents line whose dot leaders (three or more dots) or OCR residue of them (`ccceeee`) and page number or range are removed, (c) a labeled line (`Task`, `Step`, `Procedure`, `Operation`, `Exercise`, `Job` plus a number or letter, a decimal number, or `(a)`), the label removed; no paragraph needs to follow (b) and (c); then the unchanged `heading_noise_reason` filter and the unchanged equal and near-duplicate filters; the same title in the table of contents, on its task page and in a cross reference is one unit. Tiers are reported only: `T1` (stand-alone or table of contents), `T2` (labeled).
4. **Frames (position and lexicon only).** *F-IMP*, strength 0 only: token 0 capitalized, its own base form, listed with VERB (a verb-only word, or noun and verb), token 1 a determiner, an object pronoun, an adverb of `FOLLOWING_ADVERBS` or a particle (`up down out off back away`); this is exactly the old strength-0 set with verb-only first words added. **Reading of the plan stated here:** the plan's example `Get Ready to Paint` (`to` + base verb) is a strength-1 shape of PR #19 and is not part of the old strength-0 set; the frame is exactly that set, so the evidence of the pilot (3 of 3) is about the same frame. The old strength-1 case (token 1 a capitalized noun-capable word) is never an imperative candidate; it is the `initial-noun` frame of the training contrast. A line is one candidate; the rarest frame takes it (proper, imperative, noun; imperative, initial-noun). Compound: `compound_ambiguous_hit` unchanged on sentences of 3 to 30 tokens.
5. **Pilot and gate.** The first 150 imperative candidate lines of the wave-1 documents (30 to 60 documents of at least 4 families; wave 2 adds up to 60 more, at most 120 in all) in the seeded order `rank_of(20261110, "pilot", line key)`, at most 4 per first-word lemma, 20 per document and 50 per agency family, labeled by both blind labelers in one batch (round `hdg-pilot`). A line is **confirmed** when both labelers tag token 0 VERB (a disagreement, or agreement on NOUN, ADJ, PROPN or anything else, is not confirmed). **Gate: confirmed / 150 >= 0.50, that is at least 75 of 150, proceeds. Fewer than 75 of 150 (74 stops) stops the work before any scaling**: the implementation writes the pilot record (counts per agency family, tier and first-word type and the distribution of the tags of the non-confirmed token 0; ids and counts only), commits it locally and returns INCOMPLETE; nothing else is fetched, labeled, built or trained. Fewer than 150 candidate lines after wave 2 is also a stop (scarcity), not a silent downsizing. The pilot lines keep the side of their document, are not drawn or labeled again, and count against the caps of their side. Report-only numbers: kept after the all-token agreement, distinct verb lemmas, the projected yield at the observed rates. The expectation, stated honestly: the old strength-0 lines confirmed 3 of 3, so the pilot is expected to pass if the frame supplies enough lines; the risk is supply, not labeling.
6. **Labels.** Two blind labelers, `claude-opus-5.5` and `gpt-6.1-sol`, from scratch, general-purpose sub-agents, one agent per labeler and batch, at most 4 at a time, no re-roll (a refusal is requested again from a fresh agent and disclosed), through the unchanged `shapes_dev.py export`, `check-batches`, `validate-output`, `status` and `decide --round R` (rounds `hdg-pilot`, `chd2`, `chd2c`, `hdg2dev`, `cmp2`, `hdg2`). The labelers see only opaque ids, token indexes and tokens. Training: every token agreed and no fix-tier violation of the convention linter; the judged word has the exact tag of its stratum (VERB in base form for `imperative`; NOUN for `initial-noun`, read as a class-mismatch of the imperative-frame item on which both agree on NOUN; NOUN or PROPN, or VERB after an agreed noun, for `compound2`); anything else is dropped and counted, nothing is edited by hand. Development: the target agreed, the intended class, the frame after labeling, the judged word never disputed.
7. **Caps and floors** are the table above; `build` refuses below a floor unless `--allow-short` (the miss is disclosed, never padded) and never waives a cap.
8. **Hand check (a model review, not a human review).** `claude-sonnet-5.5`, 60 kept items and 4 swapped-tag leniency controls per product, one draw per product with the fixed seeds, never repeated: `compound-head-2` 20261121 (60 targets), `headings-2` 20261122 (imperative 30, noun 15, proper 15), `compound2` 20261123 (60), `headings2` 20261124 (imperative 40, initial-noun 20). Gate: marked-word precision `correct / (correct + incorrect)` at least 95% (3 incorrect of 60 passes, 4 fail), at most 1 of the 4 controls accepted, no unanswered item; below it the product is not registered and the work stops with a report.
9. **Selection seeds.** Every seeded order is `rank_of(20261110, label, ...)` with the labels fixed in the code of `followup_data.py` (for example `partition`, `pilot`, `pilot-order`, `compound2`, `compound-2-order`, `headings2-build`, `compound-head-2`, `build`, `build-control`, `order`, and the product and stratum names); `20261110` is the `shapes_dev.SEED` of PR #19; the only other seeds are the four sample seeds of item 8.
10. **Pins.** `DRAFT_SHA256`, `TOKENS_SHA256` and (for `compound-head-2`) `HMM_SHA256` are `None` in this commit; **`pilot` and `candidates` refuse until the R1 commit sets them**, and they refuse any input whose bytes differ.

### Earlier-sourcing tables used as `--extra-table` inputs (frozen copies, read only)

The used-document index must also hold the unused documents of every earlier sourcing. The copies live in the work folder (`extra-tables/`); their SHA-256 values:

| File | SHA-256 |
| --- | --- |
| `compound-heading-draft.tsv` (224 documents of PR #19, the 7 omitted and the NBS one included) | `81401c8328ac6335faf8aa8e5711079d07ce96aead587cae5fa5a894506a78de` |
| `compound-heading-hand-excluded.tsv` (44 hand exclusions of PR #19, ids and reasons) | `950120cc3caeb87d6141b980c5ddafa14e195282d3c66291cd0e47f466b99725` |
| `imperative-draft.tsv` | `bf42c15a811c2a8edb7388b4c4d0bcff39f592d7be39902e9b776ddf350d5551` |
| `shapes-dev-standard-draft.tsv` | `e4b98441f7824c8fb4a2253bfd2e98fd5b2aee41c8feaf5de45de728e8e7cf42` |
| `shapes-dev-hard-draft.tsv` | `39d26883fa156128d3d5759d9892cefa6aba3b9b7f5c05f71a67bddb91f6d352` |
| `shapes-dev-hard-draft-new.tsv` | `f0604944690d8ceea542e7cba09880110c4b81f89038f5181683db2aa94b6133` |
| `shapes-dev-headings-draft.tsv` | `8447e076e43100ca9937be92dc0e84f2231e22db89896d9f4b432bbf925b1337` |
| `shapes-dev-work-excluded.tsv` (the work-folder exclusions of the first tranches) | `8471ae2905e4cd7c55bcc2beefaaf1fc1f56a1c122a386a0234db1a9b9fbb84f` |

### Follow-up labeler prompt (frozen)

The labeler prompt is the frozen block of PR #19 (section "Labeler prompt (frozen)" above), word for word; it is `followup_data.LABELER_TEMPLATE`, and a test proves that it is the same block: with the work folder of PR #19 filled in for `<W>` it is 1,903 bytes with SHA-256 `9f1da5238dbcd196ca95b0795194245846955d2bb3a95a70bf0540d112d17db5`, the value recorded then. Nothing was added to or removed from it, and it names no stratum, source, lemma, shape or intended class.

| Frozen input | SHA-256 |
| --- | --- |
| the labeler template with the placeholder `<W>` kept (1,693 bytes) | `6440937e52beb54e6d1e4192160ef18a85d17da46296902bc0d7970462b31c35` |
| the labeler template with `<W>` = the work folder of this follow-up (`...\files\compound-heading-followup\work`, 1,911 bytes; `{ROUND}`, `{BATCH}` and `{MODEL}` kept) | `7b5a24f2c5acfd540e571cedd18983632cddfc961a75608ec68b49e836b84ce7` |
| the sub-agent appendix (758 bytes: the PAUSE rule verbatim, the IP rule, the carve-out) | `27ba85551969b1ea3225c2417f6b1a66215c3a12c77b3cbe62c6af8b3c49ed19` |

The appendix, appended after the frozen block (the `prompt` command prints both), reads: the PAUSE rule verbatim ("Before each heavy step (fetch, build, training run, labeling launch), check whether `...\files\PAUSE` exists. **If it does:** don't start the step; update `STATE.md` with the resume point, make a local WIP commit (no push), end the turn and reply "PAUSED"."), then: a labeler cannot update `STATE.md` or commit, so it writes nothing and replies "PAUSED" if that file exists, and testing the existence of that one path is the only other file access it is allowed; and: never open anything under the session folder of the earlier consumer-report work and never read or use any sentence supplied by a consumer. The orchestrator performs the check before every launch.

### Contingencies (pre-registered)

| Situation | Action |
| --- | --- |
| The pilot passes but the full supply is short | `build` refuses below a floor unless `--allow-short`; the miss is disclosed, never padded. If the training imperative lines are below 150, the arms with `HDG2` are **not run** (the arm list shrinks to BASE, CMP2, CMP2-F5, stated at R3); if the development imperative targets are below 80, the shortage is stated and the imperative-heading guard keeps its limit on the pooled items. |
| The pilot is below 75 of 150, or fewer than 150 candidate lines exist | STOP: pilot record, local commit, INCOMPLETE; no scaling. |
| A hand check is below 95%, or more than 1 of 4 controls is accepted | Stop for that product, do not register it, report the figures; no second draw to repeat a failed review. |
| Compound supply short of 1,500 combined | Register what exists (floor 600 new); the arm is still `CMP2`; the shortfall is disclosed; no top-up from vetted documents (U5). |
| A document is found to have a non-federal author after the build | `--omit-document ID=REASON` as in PR #19 (after the caps, nothing added, ids gap-free), recorded in `excluded-documents.tsv` and in `excluded-sources.tsv`. |

### Follow-up: limits stated in advance

- The Title Case frame excludes ALL-CAPS and sentence-case step titles, the common forms in maintenance manuals (U2); the supply of strength-0 Title Case lines is the main risk and is exactly what the pilot measures.
- `compound-head-2` stays a set contested for the Basic classifier (the Basic mass is another project's model), not an intrinsically hard set; the development tranches are the frames the data were built from, so a gain on them is development evidence of that kind.
- The labels are written by two models without human review; the hand check is a model review.
- The lemma cap of `compound2` counts inside `compound2` only (4 per lemma; the combined training set can hold 8 per lemma).
- The experiment of the second pull request has four arms against one baseline without a multiplicity correction (disclosed there); seeds measure training noise only.

## Follow-up: documents and pins (stream H, waves 1 and 2): scarcity record

This section follows the R0 commit `8c9f17fc5cb7e5c35f91827fc242980f407bd42d` (a commit cannot contain its own SHA). **No label exists, no batch was exported, no candidate row was written, and no model of any experiment was trained or evaluated.** Only ids and counts are recorded; the work folder and the one-off fetch scripts stay outside the repository by precedent.

### Documents (stream H)

- **Sourcing.** Archive.org records of US federal procedural publications (Army soldier training publications, training circulars and field manuals; Navy rate training manuals and first aid; Marine Corps; Air Force; Coast Guard; National Park Service), found by searching the archive.org metadata index by creator and title words. **Wave 1** (44 accepted documents, from the first 66 records) was chosen from record metadata only, before any line count existed. **Wave 2** (26 more accepted documents, from the next 46 records) was added after the first counts-only `preflight` showed few imperative lines (the plan allows wave 2 when the population is below 150). 112 records were evaluated in all: 70 documents accepted in `sources-draft.tsv` (army 52, marine 7, navy 5, airforce 4, coastguard 1, interior 1); 42 were refused.
- **Public-domain basis, per document.** The creator, publisher, contributor, title, rights and license-URL fields of every record were read (not only the title); the OCR text was scanned for copyright and permission notices, third-party credit lines and distribution statements. The `license_basis` cell of every accepted row names 17 U.S.C. 105, the agency, the creator/publisher/contributor fields read, the record's own rights statement (57 of the 70 records carry the Public Domain Mark, the others no rights field), and the distribution check (U4: only public release, FOUO or export markings are accepted; any other distribution statement is a reason to reject). No FAA material; nothing in either `excluded-sources.tsv` or any `excluded-documents.tsv` or earlier sourcing table (the `exclusions` command: 70 documents, 0 problems); no state, local, foreign, university, contractor or private creator, publisher or co-author named in any accepted record.
- **Refused (42 records).** By the automatic checks (26 records; some have more than one reason): a distribution statement other than public release (12 records, for example the restricted soldier's manuals), third-party credit or permission phrases (9), a reprint of earlier text (1), too few sentences (1), HTTP refusals (4: two lending-only records answered 401 and two server errors 500, skipped, never worked around). Copyright-like notice flags were read by hand: on records refused for other reasons, and on three accepted records where the context was OCR noise (a map scale, a page ornament, a table-of-contents ornament). By hand (16 records, `hexclude.tsv`): a Creative Commons BY-NC-ND license URL on the record, a rights field saying "In copyright", records that name Readex Microprint Corporation (a private reproduction company; 7 records), a manual with third-party copyright notices, and records without a year that the front matter does not give (the year is a column of the sources table). A fetch-helper bug of this work (a wrong file name for archive.org text files, HTTP 404) produced no accepted record: its 11 records were discarded and fetched again with the file names of the item metadata; a later change of the reprint check (10 percent of the clean sentences instead of 3 sentences) re-evaluated the 12 records fetched before it from the stored text.
- **Family-level partition** (`partition`, seed 20261110, DEV share of stream H 0.30, **not frozen**: see below): 70 documents in 61 title families, DEV 19 documents, TRAIN 51 documents; no family on both sides.
- **Earlier-source tables.** Two of the eight copies in `extra-tables` (the hand-excluded ids and the first work-folder exclusions) had CRLF line ends and the tool refuses a carriage return: the originals are kept unchanged in `extra-tables-original` (SHA-256 `950120cc3caeb87d6141b980c5ddafa14e195282d3c66291cd0e47f466b99725` and `8471ae2905e4cd7c55bcc2beefaaf1fc1f56a1c122a386a0234db1a9b9fbb84f`, the values of the table above) and LF copies with the same ids and reasons are used (SHA-256 `13f41c9221bcfa83a779eff702b21b9b306ed59dd1595abea58cb6500d0bc6a2` and `b9ad2c84d8ed16e9ebc23b36d7aa7bef4da1f764606d7a8740c4b60e4ddf20f3`).

### Amendments of the R0 extraction (before any label; counts of lines motivated them, no label did)

The first counts-only run showed that the table-of-contents lines of the soldier's manuals were almost all lost: the OCR leaders are dots followed by letter residue (`InjurieS.......... eee 3-64`, `Resuscitation.......000..... 3-83`) and many entries carry an Army skill-task number before the title (`071-311-2027 Load an M16-Series Rifle`). Two **extraction** amendments of `followup_data.py` (tests added; the frame F-IMP itself is unchanged, the noise filter is unchanged, nothing depends on a model):

1. `toc_title`: the title is the text before the first leader (three or more dots, or the residue `ccceeee`); what follows the leader up to the page number or range is dropped; trailing OCR residue tokens `0`, `O`, `<<` before the leader are removed. (R0 accepted a leader or its residue only directly before the page number.)
2. `labeled_title`: an Army skill-task number `NNN-NNN-NNNN` before a title is removed like a label (tier T2).

A bug of the R0 `preflight` (the tier map was recomputed for every line, so the command did not finish) was also fixed. The frame needed no extension (`to` + base verb is still a strength-1 shape and is not added).

### Pool sizes after wave 2 (counts only; `preflight`, `lines`, `tokenize` with the amended extraction)

| Quantity | Value |
| --- | --- |
| documents / title families / agency families | 70 / 61 / 6 |
| clean sentences (both sides) | 99,418 (DEV 25,260, TRAIN 74,158) |
| clean heading lines | 2,158 (tiers: T1 1,998, T2 160); DEV 673, TRAIN 1,485 |
| imperative frame hits (strength 0) | **234** (DEV 67, TRAIN 167), 57 first-word lemmas, 233 from the Army and 1 from the Air Force |
| initial-noun frame hits (the old strength 1) | 426 (DEV 134, TRAIN 292) |
| compound frame hits | 17,737 (DEV 4,226 over 855 lemmas, TRAIN 13,511 over 1,500 lemmas) |

Pilot sample size by the pre-registered caps (4 per first-word lemma, 20 per document, 50 per family; a sample below 150 is a SCARCITY stop):

| Reading of the family cap | Sampled lines |
| --- | --- |
| agency family (as coded in R0 and written in rule 5) | **51** (the Army supplies 50, the cap) |
| title family (as in rule 2, the family key) | 124 |
| title family and lemma cap 6 / 8 (not registered) | 146 / 150 |

### Verdict

**SCARCITY**: with the frame as registered, 70 documents give at most 234 strength-0 imperative lines, of which 51 (agency-family cap) or 124 (title-family cap) can enter the pilot sample, fewer than the 150 required. The imperative-heading work cannot go on as pre-registered: the pilot did not run, the pins `DRAFT_SHA256`, `TOKENS_SHA256` and `HMM_SHA256` stay `None` in the tool (so `pilot` and `candidates` refuse), and the DEV share of stream H stays provisional. The same supply would also be short of the later targets (DEV 80 to 130, TRAIN 150 to 400 imperative lines). The compound work does not depend on this result. Inputs as they are in the work folder (informational, not pins): `sources-draft.tsv` `7f05b400e33b10eef46b02645d05f60d7d0107b9d8a041cdc4ea4560361d49ca` (70 rows), `tokens.tsv` `bb7df8426cc1ed847ba89e74fa747c28992302b2590fa517a9098b313b7a35e0`, `streams.tsv` `c9469a82136d319003c9eceed981f3ddea16b96e763a46ba63cd0e8561fbed70`, `partition.tsv` `5a9852d87cc8a15a67a0414a014779034dd1549c949831e27d6948db2754111e`, `hexclude.tsv` `fcd3e3ef00aa44b63b6d47c9347c07dfaa972b2038c389b99a3f06dda428ac0d`, `hacc.jsonl` (all 112 records, the fetch record) `031f663a6f0d7cdec158f0a25a76cb60d6f559619a9131dafcbeeb60f1e7c239`.

## Follow-up: pre-label relaxation (amendment A1)

This section follows the scarcity commit `920e5c4` (its hash is recorded here because a commit cannot contain its own SHA). **No label of any kind exists, no batch was exported, no candidate row was written, no pin is set, and no model of any experiment was trained or evaluated**: the amendment cannot have been chosen on a result. It is approved by the owner through the orchestrator after the scarcity stop above (the owner's choice (b): a documented relaxation of the pilot caps and a smaller gate). Only ids and counts appear here.

### Cause

**Scarcity.** Under the registered caps the pilot sample has 51 lines (agency-family cap 50), fewer than the 150 required; 234 strength-0 imperative lines exist in the 70 accepted documents (DEV 67 in 24 lemmas and 3 documents, TRAIN 167 in 49 lemmas and 18 documents; 233 Army, 1 Air Force). Nothing else changes: the frame F-IMP, the noise filter, the labelers, the labeler prompt and the confirmation rule (both labelers tag token 0 VERB) are as registered.

### What is amended (and what is not)

| Item | Registered | Amended (A1) |
| --- | --- | --- |
| Level of the pilot family cap (50 lines per family) | agency family (as coded in R0) | **title family** (`family_key`, the family of the partition, as in rule 2) |
| Pilot lemma cap | 4 per first-word lemma | **6** (the pilot only) |
| Pilot size | exactly 150; fewer is a scarcity stop | **about 150, as the owner specified: however many lines the amended caps give**, at most 150, scarcity stop below 140 |
| Pilot gate | confirmed >= 75 of 150 | **confirmed >= ceil(n/2) of the n lines piloted** (n = 140 needs 70; one fewer stops the work and writes the INCOMPLETE record) |
| Pilot document cap (20) and per-lemma ordering (seeded order) | | unchanged |
| Final build: lemma cap of `headings-2` (candidates and build), `headings2` (build) | 4 (`headings2` candidates 6) | **6** |
| Final build: build document cap of `headings-2` | 12 | **20** (the pilot cap; the candidate cap was already 20) |
| Floors, `--allow-short`, the contingencies, the hand-check gate | | **unchanged** |

### Supply numbers (counts only; the amended tool on the frozen inputs of the scarcity record)

| Pilot sample, title-family cap 50 | Lines | of which DEV / TRAIN |
| --- | --- | --- |
| lemma cap 4 (as registered, title-family reading) | 124 | 35 / 89 |
| lemma cap 5 | 133 | 38 / 95 |
| **lemma cap 6 (chosen)** | **140** | **38 / 102** |
| lemma cap 7 | 147 | 39 / 108 |
| lemma cap 8 | 152 (150 at the maximum) | 41 / 111 |

The family cap of 50 (or 60) does not bind at title-family level (the sample is the same); the document cap of 20 binds (20 documents, 14 title families in the sample). **Choice of the lemma cap:** diversity of first words is the intent of the cap, so the lowest cap that supplies a pilot of about 150 (140 to 150) is taken: 5 gives 133 (below the floor of 140), 6 gives 140. The pilot size is whatever the tool gives; the figure of the scarcity record for lemma cap 6 (146) came from an earlier diagnostic, the value of this table is that of the amended tool, and the actual pilot count is stated by the R1 record from the `pilot` command itself.

**Final build (recorded now, before any label).** The pilot lines are the first candidates of their side (they are not drawn again and count against the caps); after a pilot of 140 lines (DEV 38, TRAIN 102) 29 DEV and 65 TRAIN strength-0 lines remain for the later candidate rounds. Supply per side at the build caps (a line is kept at most once; counts before any labeling loss):

| Side | Lines | At lemma cap 4 / 6 | At document cap 12 / 20 / 25 / 40 |
| --- | --- | --- | --- |
| DEV (3 documents, 24 lemmas) | 67 | 46 / 55 | 36 / 51 / 56 / 67 |
| TRAIN (18 documents, 49 lemmas) | 167 | 99 / 116 | 124 / 152 / 165 / 167 |

The lemma cap is therefore 6 in every heading build, the same value as the pilot; the lemma cap of `compound-head-2` (4) and of `compound2` (4) is not touched. The DEV build document cap is 20 because 3 documents supply the whole DEV side (at 12 per document at most 36 lines could ever be kept); the TRAIN document cap (25) and every candidate cap and budget of the table "The products" are unchanged. **Floors are not changed** and cannot be met at these supplies: the DEV imperative floor of 80 (at most 55 at lemma cap 6, 67 without caps) and the TRAIN imperative floor of 150 (at most 116 at lemma cap 6, 167 without caps; a floor of 150 would need a lemma cap of about 12, which defeats the diversity intent). The registered path stands: `build` refuses below a floor unless `--allow-short`, the shortage is disclosed and never padded, and **if the training imperative lines are below 150, the arms with `HDG2` are not run** (the arm list shrinks to BASE, CMP2, CMP2-F5, stated at R3); the imperative-heading guard keeps its limit on the pooled items when the DEV imperative targets are below 80. The expected outcome at these numbers is therefore a small imperative-heading set and no `HDG2` arm; that is a consequence of supply, stated here, not a hidden relaxation.

### Deviations from the plan (all disclosed; none depends on a label)

1. The pilot family cap is read at title-family level, not agency level (R0 coded the agency level; the plan's rule 2 uses the title family key).
2. The pilot lemma cap is 6, not 4.
3. The pilot size is however many lines the amended caps give (140 to 150), not exactly 150.
4. The pilot gate is ceil(n/2) of n, not 75 of 150.
5. The lemma cap (6) of the heading builds and the DEV build document cap (20) are raised for the final build (numbers above).
6. The 7 state or local documents of `data/compound/` and `data/headings/` (`excluded-documents.tsv`) enter `data/eval/shapes-dev/excluded-sources.tsv` **now**, not in the data commit (owner decision U6 put it in the data commit); the file grew from 3 to 10 rows (ids, URLs, a reason text and the date 2026-11-10; `data/MANIFEST.tsv` regenerated, `make_manifest.py --verify` 59 files, `check_licenses.py` passes, the used-document index already excluded the 7 through `excluded-documents.tsv`).

### Tool and tests (this commit)

`followup_data.py`: `PILOT_MIN_SIZE = 140`, `PILOT_LEMMA_CAP = 6`, `pilot_confirmed_min` (ceil(n/2)) replacing `PILOT_CONFIRMED_MIN`, `_title_family_of` (the family map of the pilot caps; the pilot record still reports the agency family), `DEV_HEADING_LEMMA_CAP = 6`, `HDG2_LEMMA_CAP = 6`, `DEV_BUILD_DOCUMENT_CAP = 20`, the docstring (rule 6, amendment A1). `test_followup_data.py`: 71 tests (new: gate arithmetic for odd and even n, the title-family map, a binding family cap, the 140 scarcity floor); the whole Python suite is 687 tests OK (1 skipped).

## Follow-up: frozen inputs (R1, stream H pilot stage)

This section follows the amendment commit `5e0a03a` (A1; the scarcity commit is `920e5c4`, R0 is `8c9f17fc5cb7e5c35f91827fc242980f407bd42d`). **It is committed before any label exists**: no batch is exported, no candidate row is written, no model was trained or evaluated. It freezes the inputs of the pilot; after this commit `pilot` and `candidates` refuse any input whose bytes differ.

| Frozen input | Value |
| --- | --- |
| `sources-draft.tsv` (70 documents; pin `DRAFT_SHA256`) | `7f05b400e33b10eef46b02645d05f60d7d0107b9d8a041cdc4ea4560361d49ca` |
| `tokens.tsv` (210,821 units; pin `TOKENS_SHA256`) | `bb7df8426cc1ed847ba89e74fa747c28992302b2590fa517a9098b313b7a35e0` |
| `streams.tsv` (70 rows, all stream H) | `c9469a82136d319003c9eceed981f3ddea16b96e763a46ba63cd0e8561fbed70` |
| `partition.tsv` (70 rows) | `5a9852d87cc8a15a67a0414a014779034dd1549c949831e27d6948db2754111e` |
| `hexclude.tsv` (the 16 by-hand refusals) | `fcd3e3ef00aa44b63b6d47c9347c07dfaa972b2038c389b99a3f06dda428ac0d` |
| `hacc.jsonl` (the fetch record of 112 archive.org records) | `031f663a6f0d7cdec158f0a25a76cb60d6f559619a9131dafcbeeb60f1e7c239` |
| DEV share of stream H | **0.30, frozen now and never changed** (stream C 0.35 stays provisional until its R3) |
| `HMM_SHA256` | not set at R1: `hmm.tsv` exists only for the documents of `compound-head-2` (stream C, not yet sourced) and no pilot or heading command reads it; it is set, together with new `DRAFT_SHA256` and `TOKENS_SHA256` for the extended document list, at the R3 commit (the stream H documents, their text, their partition and the pilot rows do not change by it) |

Documents by agency family and side (counts only): Army DEV 13 / TRAIN 39, Air Force 2 / 2, Marine Corps 2 / 5, Navy 1 / 4, Coast Guard 0 / 1, Interior 1 / 0 (DEV 19 documents, TRAIN 51 documents; 61 title families, none on both sides). Pool sizes are those of the scarcity record (clean heading lines 2,158; strength-0 imperative lines 234, DEV 67 and TRAIN 167; initial-noun lines 426; the amendment A1 changes none of them). The pilot command is `followup_data.py --work W pilot --extra-table ...` (no `--documents` restriction: all 70 documents are stream H, and the amended caps of A1 apply); its candidate count, the gate threshold and the export are recorded in the pilot record (R2), after the labels.

## Follow-up: pilot record (R2, stream H)

This section follows the R1 commit `90ecd27` (a commit cannot contain its own SHA). Round `hdg-pilot`: 1 batch (`b001`, 140 sentences, 3 parts per labeler), both labelers wrote all parts; `validate-output` passed for both, `status` showed both `ok`, `decide --round hdg-pilot` and `pilot-result` were run unchanged. Nothing was edited by hand, no line was relabeled. Counts only.

**Result: confirmed 139 of 140 (99.3 percent; both labelers tag token 0 VERB). Gate (confirmed >= ceil(140/2) = 70 proceeds; 69 stops): PROCEED.**

| Breakdown (sampled / confirmed) | Sampled | Confirmed |
| --- | --- | --- |
| DEV side (3 documents) | 38 | 38 |
| TRAIN side (17 documents) | 102 | 101 |
| Agency family: Army / Air Force | 139 / 1 | 139 / 0 |
| Tier T1 / T2 | 114 / 26 | 113 / 26 |
| First word noun-verb / verb-only | 70 / 70 | 69 / 70 |
| Lemmas (55 distinct, at most 6 lines each) | 55 | 54 lemmas have at least one confirmed line |

The single non-confirmed line (TRAIN, first-word lemma `cruise`, noun-verb word) has token 0 tagged NOUN by both labelers (class mismatch, dropped by the registered rule). Report-only numbers (rule 6): after the training rule (every token agreed) **95 TRAIN lines** are kept (101 confirmed, 6 of them have a disputed token elsewhere in the line; one more is the class mismatch); after the development rule (target agreed, judged word never disputed) **38 DEV lines** are kept (7 of them carry a disputed non-target token, which the development rule tolerates). Distinct verb lemmas among the confirmed lines: 54 (DEV 19, TRAIN 48).

**Use of the pilot lines.** By the registered rule the pilot lines keep the side of their document, are not drawn or labeled again, and count against the caps of their side; the round `hdg-pilot` is listed in the rounds of both `headings-2` (DEV) and `headings-2-train` (TRAIN), so the pilot labels are the first part of those products (kept by the same keep rules, the document and lemma caps and the Jaccard rule applied afterwards in the build).

**Supply after the pilot** (dry run of `heading_candidates` on the frozen inputs, nothing written; strength-0 pools left: DEV 29, TRAIN 65 lines): further imperative candidates at the build caps (lemma cap 6, document caps 20 and 25, pilot lines counted): **DEV 9 (pilot 38 + 9 = 47 before the build), TRAIN 14 (pilot 102 + 14 = 116 before the build)**. Expected kept after the labeling rules, at the observed rates: DEV about 45 to 47 (floor 80), TRAIN about 105 to 108 (floor 150). The floors are not reachable from these 70 documents; the registered path stands (`--allow-short` with disclosure, or the `HDG2` arms not run).

## Follow-up: stream H extension, wave 3 (amendment R3-H, document list pins; counts only)

This section follows the R2 commit `395f9c8` (a commit cannot contain its own SHA). It extends the **document list** of stream H after the pilot (the owner's "wave 3"): more Army soldier-training and collective-task publications (soldier's manuals of MOS and skill levels, training circulars and training programs, task lists, field manuals with task appendices). **No label was made in this step and none was read**; the pilot rows, the partition rule (seed 20261110, `rank_of(..."partition"...)`), the DEV share of stream H (0.30), the frame, the caps, the floors and the gates are unchanged. The HMM pin (`HMM_SHA256`) is not touched here: it is set at the R3 commit with stream C, together with the new `DRAFT_SHA256` and `TOKENS_SHA256` of the extended list (the values below are the inputs for that commit; the R1 pins in the tool still name the 70-document list, so `pilot` and `candidates` refuse until R3, by design). Only ids, hashes and counts appear here.

### Selection (metadata and title family first, before any line was counted)

- **Method.** Archive.org advanced-search queries by creator and title words (soldier training publication, STP, training circular, collective task, soldier's manual, skill level, task list, training program), stored as metadata only (`hcands.jsonl`, 710 stored records). A record was a candidate only if its creator or publisher field is a US federal body (not a university, press, corporation, state or local body, and not Readex), the title carries no FAA wording, the **title family key is new to every earlier table** (the 3,601 family keys of the open, gold, silver, MASC, all shapes-dev, imperative, `data/compound`, `data/headings`, extra-table and current draft titles; a title family already in any of them is skipped), and the title names a soldier-training or collective-task publication. Organization guides, hybrid-threat and OPFOR books, reference handbooks, and a later edition of a publication already in the list (TC 3-23.30 of 2021, the Ranger Handbook of 2025) were not taken. The two batches (22 records each, 44 in all) were fixed from that list before any text was fetched: batch 1 the MOS soldier's manuals and the training programs of the TC 21-305 series, batch 2 other training circulars, the task list and field manuals.
- **Document gates** are those of waves 1 and 2, unchanged (`hget.py`): the used-document index before any fetch; creator, publisher, contributor, title, rights and license-URL fields read (FAA or Civil Aeronautics rejects; a non-federal entry flags); the OCR text scanned for copyright notices, third-party credits, permission phrases and distribution statements (U4: only public release, FOUO or export marks accepted, any other statement rejects); reprint check against earlier text (10 percent of the clean sentences or 3 sentences); at least 20 sentences. Notice flags were read by hand: FM 7-11B5 and FM 3-19.4 each had one flag, both OCR noise (a form and a table ornament), accepted. `hexclude.tsv` is unchanged (16 rows, no new by-hand refusal).
- **Extraction order.** `hfinal.py` now processes the 70 documents of the R1 draft first (same order as before) and then the new ones, so that no sentence of an old document moves (the cross-document duplicate removal trims only the new documents against the old ones). Proof: all 70 rows of the R1 `sources-draft.tsv` (including `text_sha256`) are byte-identical in the extended draft, all rows of the R1 `partition.tsv` are unchanged, all R1 `tokens.tsv` rows are present, the first 112 lines of `hacc.jsonl` hash to the R1 value `031f663a...c1e7c239`, and the pilot round files (`rounds/hdg-pilot/candidates.tsv` SHA-256 `277d2618193e51ac34f428e4c43ac2ffb93c50c9d373c44d50040ea2f92f8fc8`) were not touched.

### Result of the two batches

| | Batch 1 | Batch 2 | Total |
| --- | --- | --- | --- |
| records fetched | 22 | 22 | 44 |
| accepted (new documents) | 18 | 15 | **33** |
| refused | 4 | 7 | **11** |
| draft documents after the batch (title families) | 88 (79) | 103 (94) | 103 (94) |
| strength-0 imperative lines DEV / TRAIN after the batch | 77 / 180 | 80 / 184 | 80 / 184 |

**Refusals (11 records, by the automatic checks, all recorded in `hacc.jsonl`):** an empty OCR text (0 sentences, 4 records of batch 1), a distribution statement other than public release (5: three training circulars, the Radio Operator's Handbook and the pistol manual, the last also a reprint), a third-party credit or permission phrase (1: the Soldier's Guide of 2004, with 12 notice flags), a reprint of earlier text (1 more: the Ranger training manual of 1962; 2 reprint refusals in all). Every record of the 44 had a US federal creator field; no non-federal entry flag arose; no FAA wording; no Readex record.

**Public-domain basis of the 33 accepted documents (one `license_basis` cell each, same form as before):** US federal government work (17 U.S.C. 105), agency named in the record (all 33 US Department of the Army; FM 21-10 names the Departments of the Army and of the Air Force as creators), creator, publisher and contributor fields read (no state, local, foreign, university, contractor or private author or co-author named), the record's own rights statement (31 of the 33 carry the Public Domain Mark; the other 2 carry none, which the cell says), the distribution check (20 documents carry a public-release statement; 2 contain the term FOUO or classified only inside task titles and references, read by hand, no marking and no other distribution statement; 11 are older or unmarked publications with no distribution statement). No FAA; `exclusions`: 103 documents, **0 problems** (nothing in either `excluded-sources.tsv`, any `excluded-documents.tsv` or any earlier sourcing table); the used-document and reprint gates mean no document shares 3 or more sentence keys with a training, evaluation or shapes-dev set (held-out sets used only as the opaque key set).

**Partition (unchanged rule, new families only).** 33 new documents, 33 new title families (every new document is its own family): DEV 10 documents, TRAIN 23. Totals: 103 documents in 94 families, **DEV 29 documents, TRAIN 74 documents**; no family on both sides. Note (disclosed, not a change): the training programs of the TC 21-305 series are different publications with different title keys, so two of them (21-305-8 and 21-305-9) fall on the DEV side and six on the TRAIN side; their task titles overlap in part, and the registered near-duplicate rule (Jaccard 0.8 against any training heading line) applies to the DEV lines and dropped 7 of them in the dry run below.

### Pools and projection (counts only)

`lines`: 148,563 lines (T1 119,799, T2 28,764); `tokenize`: 287,463 units (39,091 new); `preflight`:

| Quantity | R1 list (70 documents) | After wave 3 (103 documents) |
| --- | --- | --- |
| clean sentences | 99,418 | 137,391 (DEV 36,976, TRAIN 100,415) |
| clean heading lines | 2,158 | 2,915 (tiers: T1 2,733, T2 182) |
| strength-0 imperative lines DEV / TRAIN | 67 / 167 | **80 / 184** |
| initial-noun lines DEV / TRAIN | 134 / 292 | 211 / 403 |
| compound frame hits DEV / TRAIN | 4,226 / 13,511 | 6,464 / 18,705 |

Projected imperative totals at the registered build caps (dry run of `heading_candidates`, nothing written: the 38 DEV and 102 TRAIN pilot rows counted and not drawn again, lemma cap 6, document caps 20 (DEV) and 25 (TRAIN)), before any labeling loss: **DEV 38 + 20 = 58 lines (floor 80), TRAIN 102 + 26 = 128 lines (floor 150)**, up from 47 and 116 before wave 3 (a gain of 11 and 12 lines from 33 documents; further imperative candidates DEV 20, TRAIN 26; 7 DEV lines dropped as near a training line). At the observed pilot rates (DEV 100 percent, TRAIN about 93 percent kept) the expected kept lines are DEV about 56 to 58 and TRAIN about 118 to 120. **Both floors stay out of reach** (shortfalls of about 22 DEV and 22 TRAIN lines at the caps); the yield of the new documents is about 0.7 further capped lines per document (23 lines from 33 documents), so about 60 more documents would be needed to close the 44-line gap, which is beyond the plan's 120-document cap (103 now). The registered path stands: `build --allow-short` with disclosure, and the `HDG2` arms are not run when the training imperative lines are below 150; the guard keeps its limit on the pooled items when the development targets are below 80.

### Pins for the R3 commit (inputs as they stand in the work folder; the tool pins are still the R1 values)

| Input | SHA-256 |
| --- | --- |
| `sources-draft.tsv` (103 rows; becomes `DRAFT_SHA256` at R3) | `22a89643a9cb735dd319adf3c66ff6e290f1cf72b1fa85ebcecc041a10daaf15` |
| `tokens.tsv` (287,463 units; becomes `TOKENS_SHA256` at R3) | `ece0f12bc2764135ad9f57d615b8049bb634651ef1b748c80c540d97c18e8028` |
| `streams.tsv` (103 rows, all stream H) | `0c1a672dde49eb3d6bdd8e0941ae6832baa684aa13d516bf83b9babc360f5fff` |
| `partition.tsv` (103 rows) | `729cb3c3736b51a7522ba3d4bd6c9ea0db95e2a2d0a0504466f3460b450b5f70` |
| `hexclude.tsv` (unchanged, 16 rows) | `fcd3e3ef00aa44b63b6d47c9347c07dfaa972b2038c389b99a3f06dda428ac0d` |
| `hacc.jsonl` (156 fetch records: 112 of waves 1 and 2, 44 of wave 3) | `1f14e3289b9a96a5803a9fe4f1848f58e7d95bce754448c2c0da47fd0528309f` |

**The 33 new documents (`source_id`, side).** DEV (10): `army-atsc-mos-45-b-small-arms-repairer-skill-level-1-1984`, `army-fm-31-11-c-s-special-forces-soldiers-manual-for-mos-11-c-sqi-s-1980`, `army-fm-7-15-the-army-universal-task-list-2003`, `army-fm-7-22-army-physical-readiness-training-2012`, `army-stp-31-18-e-34-sm-tg-special-forces-communications-sergeant-skill-levels-3-4-2003`, `army-tc-21-24-rappelling-2008`, `army-tc-21-305-8-training-program-for-medium-vehicles-1992`, `army-tc-21-305-9-training-program-for-the-het-system-1997`, `army-tc-7-9-infantry-live-fire-training-1993`, `army-tc-90-1-training-for-urban-operations`. TRAIN (23): `army-FM21-10_201212`, `army-fm-17-19-d-4-cavalry-scout-1979`, `army-fm-21-75-combat-training-of-the-individual-soldier-and-patrolling-1962`, `army-fm-23-10-sniper-training-1994`, `army-fm-3-19.4-military-police-leaders-haandbook-2002`, `army-fm-3-21.75-the-warrior-ethos-and-soldier-combat-skills-2008`, `army-fm-3-7-nbc-field-handbook-1994`, `army-fm-7-11-b-3-infantryman-sl-3-1978`, `army-fm-7-11-b-4-infantryman-11-b-sl-4-1978`, `army-fm-7-11-b-5-senior-infantry-sergeant-sl-5-1977`, `army-fm-7-11-c-4-indirect-fire-infantryman-sl-4-1976`, `army-fm-7-11-h-12-heavy-antiarmor-weapons-crewman-sl-1-2-1979`, `army-fm-7-41-mine-and-tunnel-dog-training-and-employment-1973`, `army-fm-9-55-x-34-ammunition-inspector-sl-3-4-1977`, `army-military-mountaineering-tc-3-97.61-november-2025`, `army-tc-1-251-aircrew-training-manual-ah-64-d`, `army-tc-21-305-1-training-program-for-the-hemtt-1995`, `army-tc-21-305-11-training-program-for-the-fmtv-operator-1999`, `army-tc-21-305-2-training-program-for-nvg-driving-ops-1998`, `army-tc-21-305-3-training-program-for-the-m-939-series-5-ton-tactical-cargo-truck-1997`, `army-tc-21-305-5-training-program-for-equipment-transporters-c-het-met-and-let-1991`, `army-tc-21-305-6-training-program-for-the-tractor-and-semitrailer-m-915-m-931-and-m-932-1991`, `army-tc-7-21-stryker-driver-training-2006`.

### What the next commit (R3) must do

Set `DRAFT_SHA256` and `TOKENS_SHA256` to the values above (and `HMM_SHA256` with stream C), run `candidates --product headings-2` and `headings-2-train` (the pilot rows are the prior of each side), and record the candidate counts. Nothing else of this step is a pin.

## Follow-up: amendment A2 (the HDG2 arms run with a short imperative supply; written before any further label)

This section follows the commit `fd2ad25` (R3-H). **The only label that exists in the follow-up is the pilot (round `hdg-pilot`, 140 lines, R2); no other label was made or read, no candidate round beyond the pilot was written, no model of any experiment was trained or evaluated.** The amendment cannot have been chosen on a result. It is a decision of the owner, passed on by the orchestrator.

| Item | Registered (R0 contingency table) | Amended (A2) |
| --- | --- | --- |
| Training imperative lines below 150 | the arms with `HDG2` are **not run** (the arm list shrinks to BASE, CMP2, CMP2-F5, stated at R3) | the arms `CMP2+HDG2` and `CMP2+HDG2+F5` **are run**, because the owner's request names them; the training source `headings2` and the development tranche `headings-2` are built with `build --allow-short` |
| Disclosure of the shortfall | stated in R3 and in the results | stated **plainly** in the data README of `data/headings/` and of `data/eval/shapes-dev/`, in this RUN-LOG (the build and results sections) and in the pull request, against the owner's target of about 300 training and about 100 development imperative lines |
| Every other floor, cap, gate, contingency and rule | as registered | **unchanged** (for example the compound floors: if a compound target is missed it is disclosed, never padded; the hand-check gate; the contingency for a failed hand check; the imperative-heading guard keeps its limit on the pooled items) |

Basis (counts only, from the R3-H record above): the imperative supply is short and the sourcing of new Army soldier-training families is exhausted. Projected at the registered build caps, before any labeling loss, the development imperative lines are about **58** (floor 80, owner target about 100) and the training imperative lines about **128** (floor 150, owner target about 300). Wave 3 of stream H stands as it is (103 documents). No new label exists that could have informed this decision; no new selection rule is introduced; the floors are not lowered, they are waived only through the registered `--allow-short` path and disclosed. The consequence for the experiment is that the HDG2 arms train on a small imperative-heading source (about 120 lines against the owner's 300), so a null result for HDG2 is a result for that size of data, a limit to state in the E1 pre-registration and in the results.

## Follow-up: stream C sourcing (R3-C record; counts only, updated per fetch wave)

This section follows the A2 commit `ff2ec14` (a commit cannot contain its own SHA). **No label exists beyond the pilot; no candidate round other than the pilot was written; no model of any experiment was trained or evaluated; no consumer text was read.** Only ids, counts and reasons appear here; the work folder and the one-off fetch scripts stay outside the repository by precedent.

**Method (fixed before any stream C fetch, from record metadata only).** The candidates are the unfetched part of the archive.org metadata search of PR #19 (the stored records of the earlier sourcing; nothing was searched again). Before ordering: records already fetched then (accepted or not), records of the used-document index (ids, URLs, archive.org identifiers), FAA wording, Readex or non-federal-looking creator or publisher entries, titles of indexes, catalogs, hearings and similar (the earlier title rule), titles without an expository word, items under 100 KB, and every record whose **title family** is already in any earlier table (so no edition or second scan of an earlier publication is fetched). The fetch order is a family round robin with the weights of the earlier sourcing (army 3, usda 3, navy 2, usfs 2, usbm 2, usgs 2, others 1), inside a family records dated 1940 or later and not Google scans first (the OCR-structure steer of the earlier sourcing, stated again), then the seeded hash of the identifier; at most 2 records per title family and 3 per title stem. The whole order (1,141 records) was written to a file **before the first fetch** and each wave takes the next unprocessed records; no line or sentence count enters the choice. Document gates are those of stream H (`hget`): the used-document index first, the record fields creator, publisher, contributor, title, rights and license URL read, FAA and Civil Aeronautics reject, copyright notices, third-party credit and permission phrases and distribution statements other than public release reject, a reprint check (10 percent of the clean sentences or 3 sentences against earlier text), at least 20 sentences. **By hand:** a record whose creator or publisher fields name a person, a state, a university, a company or a society is not accepted (the employment of a named co-author cannot be verified: the lesson of NBS Handbook 65), except federal bodies and places of publication; the contexts of every notice, credit and permission wording in the OCR text of the accepted records were read, and records with third-party credits or copyright claims were excluded by hand (recorded as ids and reasons in the work folder). The public-domain basis of every accepted document is one `license_basis` cell (17 U.S.C. 105, the agency named in the record, the record's rights statement, the distribution check).

| Wave | Records fetched | ok | review | reject | Cumulative accepted documents (after the by-hand rules) |
| --- | --- | --- | --- | --- | --- |
| 1 | 99 | 38 | 42 | 19 | not counted |
| 2 | 150 (249 in all) | 71 (109) | 58 (100) | 21 (40) | 117 |
| 3 | 210 (459 in all) | 89 (198) | 89 (189) | 32 (72) | 200 |

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

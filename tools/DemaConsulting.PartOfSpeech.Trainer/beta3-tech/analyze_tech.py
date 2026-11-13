#!/usr/bin/env python3
"""beta.3 phase 2 technical-mix experiment (E2): acceptance tests, analysis and selection (standard library only; pre-registered, frozen with the RUN-LOG).

usage:
    python analyze_tech.py --runs DIR [--shipped-weights FILE] [--out report.md] [--decision decision.json]
    python analyze_tech.py --selftest

`DIR` is the folder of the runs of the driver beta3-tech/run_arms.ps1 (all six arms in one folder, eight seeds each). The driver writes one
result file per run (`<DIR>\\<arm>-s<seed>.json`, schema 1; the interface contract is in context-features/run_arms.ps1 plus the members
`shapesFollowup` and `shapesTechnical` of `eval-arm`, with the confidence curve of the technical calibration) and the training log next to it
(`<arm>-s<seed>.train.txt`). The arms (arms.json of this folder; every arm trains the shipped mix of `training.json`, open x2, MASC, silver,
imperative, compound and compound2, with the feature group clauseCues, and adds the weight-0 technical sources at the weights shown):

    arm        added sources (weight)                                  weighted sentences added   role
    BASE       none (the shipped model; baseline)                                           0     baseline
    TECH       deverbal 1, terms 1, steps 1, clauses 1                                  1,851     candidate
    TECH-NT    deverbal 1, steps 1, clauses 1                                           1,478     attribution only (never selected)
    TECH-ND    terms 1, steps 1, clauses 1                                              1,214     candidate
    TECH-T2    deverbal 1, terms 2, steps 1, clauses 1                                  2,224     candidate
    TECH-W2    deverbal 2, terms 2, steps 2, clauses 2                                  3,702     candidate

The only thing taken from the frozen script of the context-feature experiment (context-features/analyze_arms.py) is `welch_diff` (the
Welch-Satterthwaite t interval, 95 percent); the file is loaded from its place by path, without editing it, and only after its SHA-256 has
been checked against the frozen value. Every other function of this script is its own. The self-test checks the frozen t quantile against
published values.

Acceptance tests (run before the rule is applied; a failure stops without a selection, exit code 1, no decision file):
  (a)  BASE at seed 20260101 has weights byte-identical to the shipped weights.bin (the weights SHA-256 of its result file, and its .bin file
       when present) and every figure of shipped_figures.json (the shipped model on every development measure, measured before any arm was
       trained);
  (a2) the BASE weights of all eight seeds equal the eight CMP2 weights of the compound and heading follow-up (BASE_WEIGHT_SHA256; training is
       deterministic and the shipped mix is that experiment's CMP2 arm);
  (b)  from the line `Training on N sentences (...), sources=...` of every `<arm>-s<seed>.train.txt`: BASE lists exactly the shipped mix
       (open:2, masc:1, silver:1, imperative:1, compound:1, compound2:1) and trained 63,522 sentences; every other arm lists exactly the mix
       plus its own sources at its weights and trained exactly the weighted sentences of those sources more than the BASE run of the same
       seed; the result of every run names the feature groups of its arm (the `groups` of the weights header, the driver's `features`, and
       `sources`);
  (c)  the `inputs.*Sha256` fields of all runs agree and equal the frozen hashes of the fourteen development files; all runs name one trainer
       commit and none was made from a dirty working tree;
  (d)  the files named in FROZEN_FILES (the driver, arms.json, the table of shipped figures, the frozen Welch script, the four training sources
       and their source tables, the shared conflict ids, the four technical development tranches with their source tables and leakage
       reports, the two leakage reports of the data, the conventions, the compound sources) and the development files have their frozen
       SHA-256; and the files of the state before any adoption (training.json, the shipped weights.bin) too;
  (e)  leakage re-derived from the pinned files: no document id (`# source_id`) and no exact sentence (`# text`) is shared between any of the
       six training sources of the mix (deverbal, terms, steps, clauses, compound, compound2) and any of the nine single-shape development
       tranches (independent of tools/data/check_leakage.py).

Metrics (per run, from the `eval-arm` JSON; a harmful error is a swapped noun/verb class at confidence >= 0.45 on a noun-verb decision):
  PRIMARY    = 1000 x (harmful of S1+S3+S4 + harmful of the four compound and heading tranches) / (decisions of the same seven tranches), where
               S1+S3+S4 are deverbalHeads, stepLines and clauseVerbs of `shapesTechnical` and the four are compoundHead, compoundHead2, headings
               and headings2 of `shapesFollowup`; the script derives it from the seven tranche rows, checks that the two pooled blocks
               (`shapesTechnical.pooledTechnical`, `shapesFollowup.pooledPrimary`) are the sums of their tranches, and a mismatch is an invalid
               run. Lower is better.
  CO-PRIMARY = S2 target accuracy = 100 x termError.targetCorrect / termError.targetItems (the defined-terms tranche, exact tag); checked
               against `definedTerms.shape.targetAccuracy`. Higher is better.
  CALIBRATION = `shapesTechnical.calibration.technical.thresholds[0|1].errorPer1k`: wrong answers per 1,000 answers at confidence >= 0.90 and
               >= 0.95 over the judged words (targets and controls) of the four technical tranches; checked against its answers and wrong
               counts and against the curve rows at 0.90 and 0.95.

Selection rule T (the literal pre-registered text; nothing is tuned afterwards):

1. Invalid or missing runs or a failed acceptance test: stop (exit 1), no selection.
2. Candidates = TECH, TECH-ND, TECH-T2, TECH-W2. TECH-NT is attribution only: reported, never selected, never part of Q.
3. Guards, each arm against the BASE mean over the eight seeds (the throughput guard is absolute; it uses the median over the seeds of each
   run's median): see GUARDS (20 guards, among them the calibration condition: the mean wrong per 1,000 at >= 0.90 and at >= 0.95 must each be <=
   the BASE mean).
4. Q = the candidates that pass every guard **and** whose Welch 95 percent interval (Welch-Satterthwaite degrees of freedom) of (arm - BASE) has an
   upper bound strictly below 0 on the primary **and** a lower bound strictly above 0 on the S2 target accuracy. If Q is empty, BASE stays.
5. m = the lowest primary mean in Q. S = the arms of Q within 10 percent relative of m (primary <= 1.10 x m).
6. The winner = the arm of S with the fewest weighted added sentences (TECH-ND 1,214, TECH 1,851, TECH-T2 2,224, TECH-W2 3,702); ties: the lowest
   primary, then the order of arms.json. Nothing is applied afterwards.

Reported, not gated: the means and standard deviations of every arm, the Welch interval of every arm against BASE for every guarded metric, the
pairwise intervals TECH-NT, TECH-ND, TECH-T2 and TECH-W2 against TECH on the primary, the S2 accuracy and the two calibration figures, the
per-seed table, the per-tranche diagnostics, open, gold v2 and UD-EWT dev, temperature, rows, bytes, training seconds.

Exit codes: 0 done (also when BASE stays), 1 missing or invalid runs, a frozen-file mismatch or a failed acceptance test (no selection is
made) or a failed self-test, 2 usage.
"""

import argparse
import hashlib
import importlib.util
import json
import math
import os
import re
import statistics
import sys
import tempfile

sys.dont_write_bytecode = True  # importing the frozen script must not write a __pycache__ into the repository

HERE = os.path.dirname(os.path.abspath(__file__))
TRAINER = os.path.dirname(HERE)
REPO = os.path.abspath(os.path.join(TRAINER, "..", ".."))
CONTEXT_DIR = os.path.join(TRAINER, "context-features")
FROZEN_ANALYZER = os.path.join(CONTEXT_DIR, "analyze_arms.py")
ARMS_JSON = os.path.join(HERE, "arms.json")
SHIPPED_FIGURES_FILE = os.path.join(HERE, "shipped_figures.json")
DEFAULT_SHIPPED = os.path.join(REPO, "src", "DemaConsulting.PartOfSpeech", "Resources", "weights.bin")

T = "tools/DemaConsulting.PartOfSpeech.Trainer/"
B3 = T + "beta3-tech/"
REL_FROZEN_ANALYZER = T + "context-features/analyze_arms.py"
SD = "data/eval/shapes-dev/"
TD = "data/technical/"

# Text files frozen by the pre-registration commit: repository-relative path -> SHA-256 of the committed text (line endings LF; a
# working-tree copy with CRLF is normalized before hashing). This script cannot hold its own hash, and technical_points.py is not an input
# of the analysis; the RUN-LOG section of the pre-registration records both.
# FROZEN-HASHES-BEGIN
FROZEN_FILES = {
    B3 + "run_arms.ps1": "3fac2cb1961642ca34404d6418447f88e29a3b5709861f9ca1b1cde844cf1135",
    B3 + "arms.json": "de2b4da2d52a5067bbed20e8c3b03c99372c259684eecdd0b4223478834c60c0",
    B3 + "shipped_figures.json": "a29c890d9a053e4aaf486d8e41ecef7c19f035a6891279b91f8c8e0caeb8582c",
    REL_FROZEN_ANALYZER: "3d52825384b440c5786e1b241fc6f814e4e47a63f155414e0d6450107d33e494",
    TD + "deverbal.tsv": "e8f8484c0f27fe47d1df08cfa71fd3340ebed71feb5b0877398b0c7d562b886b",
    TD + "deverbal-sources.tsv": "fe5a67dbdc84e08e6a1c5dce04dd509132fcfb472430db09f272e937a3e3c4e7",
    TD + "terms.tsv": "12b8e37a6fab492e50457cfd6985388597ae3879242b4172aa778dfaca089a91",
    TD + "terms-sources.tsv": "6f374876a2fb9134badb4eef434ca4a305bdcf562635e8a724d5730fb33c1e3b",
    TD + "steps.tsv": "ca54a39f9ae7a1d964662d46533cba1a4b0754553c1e3b5b18f49f386e8fb519",
    TD + "steps-sources.tsv": "bd7356734882906cb4d8d6d3710fcaafc7da405b78220c48f87cb40b91a6ae80",
    TD + "clauses.tsv": "b28ae036f51e49417c43d750aa3bfc809175e69ab86d36f0e81078be3360cef2",
    TD + "clauses-sources.tsv": "2617694c907c45c7f9fb9b2eb9c942347aaf61b4f761d20c3a4de3fdac9b1e3e",
    TD + "term-conflict-ids.tsv": "515276862c7205e931f454d37326269b1dfb0dbc6416d69bfa59f428be532f77",
    SD + "deverbal-heads-sources.tsv": "a39dec4fc0fffd822b2ecbdb7244f34a99090bcf121f1c5e83fe146f29636da2",
    SD + "deverbal-heads-leakage-report.tsv": "c366505d3f19dad21d2b0893e9d34fa14dde2ad810f47d441f45ee5692af1619",
    SD + "defined-terms-sources.tsv": "807f556b238eb3407a2bf115aa095361d643bb338f28e358a5336c34b053aef1",
    SD + "defined-terms-leakage-report.tsv": "06037c354b0d37115a3fe4a1cb455ea634598daccdd55b70b4b6cf6228042ea7",
    SD + "step-lines-sources.tsv": "64254a5da71cfbbe1ac7d2b54db513803d20e560e92e00957fdae20973840ae4",
    SD + "step-lines-leakage-report.tsv": "a34ee813186ce026fac9012a2962dd49c71305f7f62f44e6fb2cb80c64fa3223",
    SD + "clause-verbs-sources.tsv": "505da20c8ad9d46854e6b04fa70f73676e3b88ff9d42871e12e823212dc8decd",
    SD + "clause-verbs-leakage-report.tsv": "25d45235f1db02300f5ea1c7cb7d501852f0e4f54686a700309ca46fbc9c81da",
    "data/leakage-report.tsv": "3fc2362ef40e422b10fc8ea50b27ddf542b9830ff788beaa8adf56204eb0747b",
    "data/training-leakage-report.tsv": "4919d20dcb3a792863729e73172781a1ad444232fd38857be2ed1c1e7ab85f7b",
    "data/CONVENTIONS.md": "42da154c60272dc9a105c5496fa3c8797cffa81cfb55d34505497b33520e5bb2",
    "data/compound/compound.tsv": "eb8c4708ba95fb370c885110e10eda0f1d826c5922876bd66e69389894bd5e3b",
    "data/compound/sources.tsv": "8b861dd3ff2d5a3fd5e741f5fe2a906807f99afa562d7e294d4011d93ef15e02",
    "data/compound/compound-2.tsv": "2a9a98492afb1248623b50111ca62d8abd57e4569b1a5e52b3cff544bc0746cc",
    "data/compound/compound-2-sources.tsv": "c979850dd60921d209b7d918d953b53bee7af05f736a77896637665ace823cf4",
}
# FROZEN-HASHES-END

# Files of the state before any adoption: an adoption changes both by design, so the analysis is made before it (and the self-test does not
# check them). The weights file is hashed as bytes (it is binary), training.json as text.
PRE_ADOPTION_TEXT = {
    T + "training.json": "7ecdcaec1ebf2511a17f0445dbd2e5e27df849a75572031f748e14dab0c9fd6c",
}
PRE_ADOPTION_RAW = {
    "src/DemaConsulting.PartOfSpeech/Resources/weights.bin": "f384a877756dd2a646ce0e087ad034014f3bdfd2395b5f3751af152d2208ddad",
}

# The development files that every run scored (inputs.* of the per-run JSON) and their frozen SHA-256 (of the file bytes).
FROZEN_INPUTS = {
    "devSha256": "bbc94e42693d92a7e948598d5ea8a2c58274d3e4e5e6e4d0ed7d8a5c10963290",           # data/eval/shapes-dev/dev.tsv
    "hardSha256": "c041ae3d8b4b948326d002176bdffa0e5eb2ac0a6fe7d12daed7c3d289db8b22",          # data/eval/shapes-dev/hard.tsv
    "udDevSha256": "0fc0b4a371ef6d0437e01e396762cdb600d0d7ff0d725ead00ac9f3c41f82037",         # tools/data/cache/ud-ewt/ud_dev.tsv
    "openDevSha256": "8caa822515cee179093f068ff4ea7fa62714830d4c50de696bd4403037fc05c7",       # data/open/dev.tsv
    "goldDevSha256": "ffb2f3afd3460523656f5528a66094bad507982b15eb2b4a9341cdb78ff117f5",       # data/eval/gold-v2/dev.tsv
    "compoundHeadSha256": "537e0be3923f585b8f084de4e88f4064624e4f5dc484f84ccd303fed3db924f5",  # data/eval/shapes-dev/compound-head.tsv
    "headingsSha256": "45a62a71837f02d1e76f0ad55a71a1770cb628ea8ca8765c0d4583f49b9da72a",      # data/eval/shapes-dev/headings.tsv
    "namedStateSha256": "e08472387630368b7fb3fbaa5cd7b6187d0760608c93b55f19250c98f2d06d6e",    # data/eval/shapes-dev/named-state.tsv
    "compoundHead2Sha256": "ffa7e5aef0cff05e6ef9227bce3544e4372109768685fcff8c0395e18220c527",  # data/eval/shapes-dev/compound-head-2.tsv
    "headings2Sha256": "6c84419851bceebcb825fff6bc530fc3a3dc5bc5306da152bda349db00c4b4a7",     # data/eval/shapes-dev/headings-2.tsv
    "deverbalHeadsSha256": "6159099f5fc59187f9486070439e552358abf19fb95742e20c54dbcf17400c5a",  # data/eval/shapes-dev/deverbal-heads.tsv
    "definedTermsSha256": "6bca83a8e63589152cf3a37661e328a187323612b06344c6c402921f1c2478c8",   # data/eval/shapes-dev/defined-terms.tsv
    "stepLinesSha256": "c06bc3138b92cd3baafec1a9a210110daafee69846dd83c77ba70b4fc09f4ad8",      # data/eval/shapes-dev/step-lines.tsv
    "clauseVerbsSha256": "d35e7a259140614d6f0057fe7f3fd5d545cdf478971ac7f77088dc2789db4e06",     # data/eval/shapes-dev/clause-verbs.tsv
}
# The inputs that lie in the repository (the UD-EWT cache does not): checked on disk too.
INPUT_FILES = {
    "devSha256": SD + "dev.tsv", "hardSha256": SD + "hard.tsv", "openDevSha256": "data/open/dev.tsv",
    "goldDevSha256": "data/eval/gold-v2/dev.tsv", "compoundHeadSha256": SD + "compound-head.tsv", "headingsSha256": SD + "headings.tsv",
    "namedStateSha256": SD + "named-state.tsv", "compoundHead2Sha256": SD + "compound-head-2.tsv",
    "headings2Sha256": SD + "headings-2.tsv", "deverbalHeadsSha256": SD + "deverbal-heads.tsv",
    "definedTermsSha256": SD + "defined-terms.tsv", "stepLinesSha256": SD + "step-lines.tsv",
    "clauseVerbsSha256": SD + "clause-verbs.tsv",
}

# The weights SHA-256 of the eight CMP2 runs of the compound and heading follow-up (the shipped mix, clauseCues, --calibrate), per seed.
BASE_WEIGHT_SHA256 = {
    20260101: "f384a877756dd2a646ce0e087ad034014f3bdfd2395b5f3751af152d2208ddad",
    11: "ad200b097ea153e58291a9875c406fd2ae1c3921f779c3b24205045578810598",
    12: "48b90cb86abd93dfa25989a6011f100d0406adfd152f5e6c14a1cbbf743b5c6a",
    13: "e7e171bad0dcb3f675606c4f3d5fff96f819080385a71218f72f6bec31367b86",
    14: "c1a40e27966b9878dd64e5a4518d3703b59a7162091d221270168124c432e5fa",
    15: "de33b5bc5f92e4d822c9bfd9e00337d74d3eb690fa388dfd2c43492457aea776",
    16: "eadae0d67d2e9a2e591e32300b63792a905f655630535f8b65e8b2a7b0c43d2a",
    17: "93476821a44884f021f0553a99ca735479cda83ca767931dc99dc767d16e58b0",
}

SHIPPED_SEED = 20260101
EXPECTED_SEEDS = [20260101, 11, 12, 13, 14, 15, 16, 17]
TOLERANCE = 1e-9
WINDOW = 1.10

# The shipped training mix (training.json before any adoption) and the sentences that each technical source adds at weight 1 (the sentence
# blocks of the data files; the self-test checks the counts against the files).
BASE_MIX = ["open:2", "masc:1", "silver:1", "imperative:1", "compound:1", "compound2:1"]
BASE_SENTENCES = 63522
ADDED_SENTENCES = {"deverbal": 637, "terms": 373, "steps": 531, "clauses": 310}
DATA_FILES = {"deverbal": TD + "deverbal.tsv", "terms": TD + "terms.tsv", "steps": TD + "steps.tsv", "clauses": TD + "clauses.tsv"}

# id, feature groups, added sources (name -> weight), role.
ARMS = [
    {"id": "BASE", "features": ["clauseCues"], "sources": {}, "role": "baseline"},
    {"id": "TECH", "features": ["clauseCues"], "sources": {"deverbal": 1, "terms": 1, "steps": 1, "clauses": 1}, "role": "candidate"},
    {"id": "TECH-NT", "features": ["clauseCues"], "sources": {"deverbal": 1, "steps": 1, "clauses": 1}, "role": "attribution"},
    {"id": "TECH-ND", "features": ["clauseCues"], "sources": {"terms": 1, "steps": 1, "clauses": 1}, "role": "candidate"},
    {"id": "TECH-T2", "features": ["clauseCues"], "sources": {"deverbal": 1, "terms": 2, "steps": 1, "clauses": 1}, "role": "candidate"},
    {"id": "TECH-W2", "features": ["clauseCues"], "sources": {"deverbal": 2, "terms": 2, "steps": 2, "clauses": 2}, "role": "candidate"},
]
BASE_ID = "BASE"
CANDIDATES = [a["id"] for a in ARMS if a["role"] == "candidate"]
PAIRS = [("TECH-NT", "TECH"), ("TECH-ND", "TECH"), ("TECH-T2", "TECH"), ("TECH-W2", "TECH")]

# The tranches of the primary and the paths of its counts.
TECH_TRANCHES = ["deverbalHeads", "stepLines", "clauseVerbs"]          # S1, S3, S4
ALL_TECH_TRANCHES = ["deverbalHeads", "definedTerms", "stepLines", "clauseVerbs"]
OLD_TRANCHES = ["compoundHead", "compoundHead2", "headings", "headings2"]
HEADING_TRANCHES = ["headings", "headings2"]
THRESHOLD_INDEX = {"0.90": 9, "0.95": 10}  # positions of 0.90 and 0.95 on the 15 thresholds of the curve

PRIMARY = "derived.primary"
S2 = "derived.s2Accuracy"
CAL90 = "derived.cal90"
CAL95 = "derived.cal95"
OLD_POOL = "shapesFollowup.pooledPrimary.harmfulPer1k"
S1_RATE = "derived.s1HarmfulPer1k"
S3_RATE = "derived.s3HarmfulPer1k"
S4_RATE = "derived.s4HarmfulPer1k"
TECH_CONTROLS = "derived.techControlAccuracy"
S2_PROPER = "derived.s2ProperControlAccuracy"
IMPERATIVE = "shapesFollowup.imperativeHeadings.harmfulPer1k"

# name, label, metric path, kind, limit, aggregate
GUARDS = [
    ("old-pool", "old pool (compound, headings) harmful per 1k <= BASE + 25", OLD_POOL, "max_delta", 25.0, "mean"),
    ("s1-harmful", "S1 deverbal-heads harmful per 1k <= BASE + 25", S1_RATE, "max_delta", 25.0, "mean"),
    ("s3-harmful", "S3 step-lines harmful per 1k <= BASE + 35", S3_RATE, "max_delta", 35.0, "mean"),
    ("s4-harmful", "S4 clause-verbs harmful per 1k <= BASE + 25", S4_RATE, "max_delta", 25.0, "mean"),
    ("hard-harmful", "hard tranche harmful per 1k <= BASE + 22", "shapes.hard.pooled.harmfulPer1k", "max_delta", 22.0, "mean"),
    ("standard-harmful", "standard tranche harmful per 1k <= BASE + 5", "shapes.standard.pooled.harmfulPer1k", "max_delta", 5.0, "mean"),
    ("open-harmful", "open dev harmful per 1k <= BASE + 1.5", "openDev.harmfulPer1k", "max_delta", 1.5, "mean"),
    ("open-accuracy", "open dev accuracy >= BASE - 0.10", "openDev.accuracy", "min_delta", -0.10, "mean"),
    ("gold-harmful", "gold v2 dev harmful per 1k <= BASE + 4.5", "goldDev.harmfulPer1k", "max_delta", 4.5, "mean"),
    ("gold-accuracy", "gold v2 dev accuracy >= BASE - 0.25", "goldDev.accuracy", "min_delta", -0.25, "mean"),
    ("ud-harmful", "UD-EWT dev harmful per 1k <= BASE + 1.5", "udDev.harmfulPer1k", "max_delta", 1.5, "mean"),
    ("ud-accuracy", "UD-EWT dev accuracy >= BASE - 0.15", "udDev.accuracy", "min_delta", -0.15, "mean"),
    ("named-state", "named-state target acc >= BASE - 3", "shapesNew.namedState.shape.targetAccuracy", "min_delta", -3.0, "mean"),
    ("imperative-harmful", "imperative-heading harmful per 1k <= BASE + 25", IMPERATIVE, "max_delta", 25.0, "mean"),
    ("tech-controls", "technical controls acc >= BASE - 3", TECH_CONTROLS, "min_delta", -3.0, "mean"),
    ("s2-proper-control", "S2 proper-noun control acc >= BASE - 5", S2_PROPER, "min_delta", -5.0, "mean"),
    ("size", "deflated bytes <= 1.5 x BASE", "weights.bytesDeflated", "max_ratio", 1.5, "mean"),
    ("throughput", "tokens/s >= 150,000", "throughput.tokensPerSecond", "min_abs", 150000.0, "median"),
    ("cal90", "wrong per 1k at >= 0.90 <= BASE", CAL90, "max_delta", 0.0, "mean"),
    ("cal95", "wrong per 1k at >= 0.95 <= BASE", CAL95, "max_delta", 0.0, "mean"),
]
WELCH_METRICS = [PRIMARY, S2, CAL90, CAL95, OLD_POOL, S1_RATE, S3_RATE, S4_RATE, "shapes.hard.pooled.harmfulPer1k",
                 "shapes.standard.pooled.harmfulPer1k", "openDev.harmfulPer1k", "openDev.accuracy", "goldDev.harmfulPer1k", "goldDev.accuracy",
                 "udDev.harmfulPer1k", "udDev.accuracy", "shapesNew.namedState.shape.targetAccuracy", IMPERATIVE, TECH_CONTROLS, S2_PROPER]
PAIR_METRICS = [PRIMARY, S2, CAL90, CAL95]


def tranche_path(tranche, key):
    block = "shapesTechnical" if tranche in ALL_TECH_TRANCHES else "shapesFollowup"
    return "%s.%s.shape.%s" % (block, tranche, key)


# (path, label, decimals, required)
GROUPS = [
    ("Primary, co-primary (S2) and calibration", [
        (PRIMARY, "pooled harmful per 1k (primary)", 1, True),
        (S1_RATE, "S1 deverbal-heads harmful per 1k", 1, True),
        (S3_RATE, "S3 step-lines harmful per 1k", 1, True),
        (S4_RATE, "S4 clause-verbs harmful per 1k", 1, True),
        (OLD_POOL, "old pool harmful per 1k", 1, True),
        (S2, "S2 defined-terms target acc % (co-primary)", 2, True),
        ("shapesTechnical.termError.errorPer1k", "S2 errors per 1k targets", 1, False),
        ("shapesTechnical.termError.confidentWrong", "S2 wrong at >= 0.95", 2, False),
        ("shapesTechnical.termError.wrong90", "S2 wrong at >= 0.90", 2, False),
        (CAL90, "wrong per 1k answers at >= 0.90 (technical)", 1, True),
        (CAL95, "wrong per 1k answers at >= 0.95 (technical)", 1, True),
        ("shapesTechnical.calibration.technical.thresholds.0.answers", "answers at >= 0.90", 1, False),
        ("shapesTechnical.calibration.technical.thresholds.1.answers", "answers at >= 0.95", 1, False),
        ("shapesTechnical.calibration.allSingle.thresholds.0.errorPer1k", "wrong per 1k at >= 0.90 (all nine tranches)", 1, False),
        ("shapesTechnical.calibration.allSingle.thresholds.1.errorPer1k", "wrong per 1k at >= 0.95 (all nine tranches)", 1, False),
    ]),
    ("The four technical tranches", [
        ("shapesTechnical.deverbalHeads.shape.targetAccuracy", "S1 target acc %", 1, False),
        ("shapesTechnical.deverbalHeads.shape.confidentWrong", "S1 wrong at >= 0.95", 2, False),
        ("shapesTechnical.deverbalHeads.shape.controlAccuracy", "S1 control acc %", 1, False),
        ("shapesTechnical.stepLines.shape.targetAccuracy", "S3 target acc %", 1, False),
        ("shapesTechnical.stepLines.shape.confidentWrong", "S3 wrong at >= 0.95", 2, False),
        ("shapesTechnical.stepLines.shape.controlAccuracy", "S3 control acc %", 1, False),
        ("shapesTechnical.clauseVerbs.shape.targetAccuracy", "S4 target acc %", 1, False),
        ("shapesTechnical.clauseVerbs.shape.confidentWrong", "S4 wrong at >= 0.95", 2, False),
        ("shapesTechnical.clauseVerbs.shape.controlAccuracy", "S4 control acc %", 1, False),
        ("shapesTechnical.definedTerms.shape.controlAccuracy", "S2 control acc %", 1, False),
        (S2_PROPER, "S2 proper-noun control acc %", 1, True),
        (TECH_CONTROLS, "technical controls acc % (320)", 2, True),
        ("shapesTechnical.pooledTechnical.targetAccuracy", "S1+S3+S4 target acc %", 2, False),
        ("shapesTechnical.pooledTechnical.confidentWrongTotal", "S1+S3+S4 wrong at >= 0.95", 2, False),
        ("shapesTechnical.pooledTechnical.wrong90Total", "S1+S3+S4 wrong at >= 0.90", 2, False),
    ]),
    ("Old tranches and guarded measures", [
        ("shapesFollowup.pooledPrimary.targetAccuracy", "old pool target acc %", 2, False),
        ("shapesFollowup.pooledPrimary.confidentWrongTotal", "old pool wrong at >= 0.95", 2, False),
        (IMPERATIVE, "imperative harmful per 1k", 1, True),
        ("shapesFollowup.compoundControls.controlAccuracy", "compound controls acc %", 1, False),
        ("shapes.hard.pooled.harmfulPer1k", "hard harmful per 1k", 1, True),
        ("shapes.standard.pooled.harmfulPer1k", "standard harmful per 1k", 1, True),
        ("shapesNew.namedState.shape.targetAccuracy", "named-state target acc %", 1, True),
    ]),
    ("Open dev, gold v2 dev and UD-EWT dev", [
        ("openDev.accuracy", "open acc %", 3, True),
        ("openDev.harmfulPer1k", "open harmful per 1k", 2, True),
        ("openDev.nll", "open NLL", 4, False),
        ("openDev.ece", "open ECE %", 2, False),
        ("goldDev.accuracy", "gold acc %", 3, True),
        ("goldDev.harmfulPer1k", "gold harmful per 1k", 1, True),
        ("udDev.accuracy", "UD acc %", 3, True),
        ("udDev.harmfulPer1k", "UD harmful per 1k", 2, True),
    ]),
    ("Model, speed and training", [
        ("temperature", "temperature", 3, False),
        ("weights.rows", "rows", 0, False),
        ("weights.bytesRaw", "raw bytes", 0, False),
        ("weights.bytesDeflated", "deflated bytes", 0, True),
        ("throughput.tokensPerSecond", "tokens/s (1 thread)", 0, True),
        ("trainWallSeconds", "train wall s", 1, False),
    ]),
]


class RunsError(Exception):
    """Missing or invalid inputs, a frozen-file mismatch or a failed acceptance test: no selection is made."""


# ---------------------------------------------------------------------------------------------------------------------
# Frozen files and the one function taken from the frozen script
# ---------------------------------------------------------------------------------------------------------------------

def sha256_lf(data):
    """SHA-256 of the bytes with CRLF turned into LF (the form in which the text files are committed)."""
    return hashlib.sha256(data.replace(b"\r\n", b"\n")).hexdigest()


def sha256_file(path):
    h = hashlib.sha256()
    with open(path, "rb") as f:
        for block in iter(lambda: f.read(1 << 20), b""):
            h.update(block)
    return h.hexdigest()


def verify_frozen(repo=REPO, table=None, raw_table=None):
    """Returns the list of problems (empty when every file has its frozen SHA-256). Text files are LF-normalized, raw files are not."""
    problems = []
    text = FROZEN_FILES if table is None else table
    raw = {} if raw_table is None else raw_table
    for rel, expected, normalize in [(r, e, True) for r, e in text.items()] + [(r, e, False) for r, e in raw.items()]:
        path = os.path.join(repo, *rel.split("/"))
        try:
            with open(path, "rb") as f:
                data = f.read()
        except OSError as e:
            problems.append("frozen file %s cannot be read: %s" % (rel, e))
            continue
        actual = sha256_lf(data) if normalize else hashlib.sha256(data).hexdigest()
        if actual != expected:
            problems.append("frozen file %s has SHA-256 %s, frozen value %s" % (rel, actual, expected))
    return problems


def input_files_table():
    """The development files of the repository with their frozen SHA-256 (the bytes; the files are LF text)."""
    return {INPUT_FILES[key]: FROZEN_INPUTS[key] for key in INPUT_FILES}


_FROZEN_MODULE = None


def frozen_module():
    """Loads context-features/analyze_arms.py by path (read only), after checking its SHA-256."""
    global _FROZEN_MODULE
    if _FROZEN_MODULE is None:
        problems = verify_frozen(table={REL_FROZEN_ANALYZER: FROZEN_FILES[REL_FROZEN_ANALYZER]})
        if problems:
            raise RunsError("; ".join(problems))
        spec = importlib.util.spec_from_file_location("analyze_arms_frozen", FROZEN_ANALYZER)
        module = importlib.util.module_from_spec(spec)
        spec.loader.exec_module(module)
        _FROZEN_MODULE = module
    return _FROZEN_MODULE


_WELCH_CACHE = {}


def welch_diff(arm, base):
    """Welch interval of mean(arm) - mean(base) at 95 percent: the function of the frozen script, unchanged (results are kept per pair of samples)."""
    key = (tuple(arm), tuple(base))
    if key not in _WELCH_CACHE:
        _WELCH_CACHE[key] = frozen_module().welch_diff(arm, base)
    return _WELCH_CACHE[key]


def arms_spec():
    """The seeds and the arms of arms.json."""
    with open(ARMS_JSON, encoding="utf-8") as f:
        spec = json.load(f)
    return [int(s) for s in spec["seeds"]], spec["arms"]


def load_shipped_figures(path=SHIPPED_FIGURES_FILE):
    """The weights SHA-256 and the figures (path, value, decimals) of the shipped model."""
    with open(path, encoding="utf-8") as f:
        doc = json.load(f)
    return doc["weightsSha256"], [(x["path"], x["value"], x["decimals"]) for x in doc["figures"]]


# ---------------------------------------------------------------------------------------------------------------------
# Loading
# ---------------------------------------------------------------------------------------------------------------------

def get_path(obj, path):
    """The number at a dotted path (list members by index), or None when it is absent or not a number."""
    cur = obj
    for part in path.split("."):
        if isinstance(cur, list):
            if not part.isdigit() or int(part) >= len(cur):
                return None
            cur = cur[int(part)]
        elif isinstance(cur, dict) and part in cur:
            cur = cur[part]
        else:
            return None
    return cur if isinstance(cur, (int, float)) and not isinstance(cur, bool) else None


def arm_by_id(arm_id):
    return next(a for a in ARMS if a["id"] == arm_id)


def run_stem(directory, arm_id, seed):
    return os.path.join(directory, "%s-s%s" % (arm_id, seed))


def weighted_added(arm):
    """The sentences that an arm trains over BASE: the sentences of each added source times its weight."""
    return sum(weight * ADDED_SENTENCES[name] for name, weight in arm["sources"].items())


def rate(harmful, decisions):
    return 1000.0 * harmful / decisions if decisions else 0.0


def validate_run(run, name, arm, seed, required):
    """Returns the list of problems of one loaded run (and adds the derived members)."""
    errors = []
    if run.get("schema") != 1:
        errors.append("%s: schema is not 1" % name)
    if "arm" in run and run["arm"] != arm["id"]:
        errors.append("%s: arm field is %r" % (name, run["arm"]))
    if "seed" in run and str(run["seed"]) != str(seed):
        errors.append("%s: seed field is %r" % (name, run["seed"]))
    for key, expected in (("features", arm["features"]), ("groups", arm["features"])):
        if key not in run or not isinstance(run[key], list) or sorted(run[key]) != sorted(expected):
            errors.append("%s: %s are %r, expected %r" % (name, key, run.get(key), expected))
    if "sources" in run and run["sources"] != arm["sources"]:
        errors.append("%s: sources are %r, expected %r" % (name, run["sources"], arm["sources"]))

    needed = ["shapesTechnical.pooledTechnical.decisions", "shapesTechnical.pooledTechnical.harmful",
              "shapesFollowup.pooledPrimary.decisions", "shapesFollowup.pooledPrimary.harmful", OLD_POOL,
              "shapesTechnical.termError.targetItems", "shapesTechnical.termError.targetCorrect",
              "shapesTechnical.definedTerms.shape.targetItems", "shapesTechnical.definedTerms.shape.targetAccuracy",
              "shapesTechnical.definedTerms.kinds.proper.controlItems", "shapesTechnical.definedTerms.kinds.proper.controlCorrect"]
    for t in TECH_TRANCHES + OLD_TRANCHES:
        needed += [tranche_path(t, "decisions"), tranche_path(t, "harmful")]
    for t in ALL_TECH_TRANCHES:
        needed += [tranche_path(t, "controlItems"), tranche_path(t, "controlCorrect")]
    cal = "shapesTechnical.calibration.technical."
    for i in (0, 1):
        needed += [cal + "thresholds.%d.%s" % (i, k) for k in ("threshold", "answers", "wrong", "errorPer1k")]
    for i in THRESHOLD_INDEX.values():
        needed += [cal + "curve.%d.%s" % (i, k) for k in ("threshold", "answers", "wrong")]
    needed = list(dict.fromkeys(required + needed))
    derived_names = {PRIMARY, S2, CAL90, CAL95, S1_RATE, S3_RATE, S4_RATE, TECH_CONTROLS, S2_PROPER}
    missing = [m for m in needed if m not in derived_names and get_path(run, m) is None]
    for m in missing:
        errors.append("%s: required metric %s is missing" % (name, m))
    if missing:
        return errors

    def g(path):
        return get_path(run, path)

    # the primary: re-derived from the seven tranche rows; both pooled blocks must be the sums of their tranches
    tech_decisions = sum(g(tranche_path(t, "decisions")) for t in TECH_TRANCHES)
    tech_harmful = sum(g(tranche_path(t, "harmful")) for t in TECH_TRANCHES)
    old_decisions = sum(g(tranche_path(t, "decisions")) for t in OLD_TRANCHES)
    old_harmful = sum(g(tranche_path(t, "harmful")) for t in OLD_TRANCHES)
    if abs(tech_decisions - g("shapesTechnical.pooledTechnical.decisions")) > TOLERANCE or \
            abs(tech_harmful - g("shapesTechnical.pooledTechnical.harmful")) > TOLERANCE:
        errors.append("%s: pooledTechnical decisions/harmful %r/%r are not the sums %r/%r of deverbalHeads, stepLines and clauseVerbs" %
                      (name, g("shapesTechnical.pooledTechnical.decisions"), g("shapesTechnical.pooledTechnical.harmful"),
                       tech_decisions, tech_harmful))
    if abs(old_decisions - g("shapesFollowup.pooledPrimary.decisions")) > TOLERANCE or \
            abs(old_harmful - g("shapesFollowup.pooledPrimary.harmful")) > TOLERANCE:
        errors.append("%s: pooledPrimary decisions/harmful %r/%r are not the sums %r/%r of the four compound and heading tranches" %
                      (name, g("shapesFollowup.pooledPrimary.decisions"), g("shapesFollowup.pooledPrimary.harmful"),
                       old_decisions, old_harmful))
    if abs(rate(old_harmful, old_decisions) - g(OLD_POOL)) > 1e-6:
        errors.append("%s: the old pool rate %r is not the value %r re-derived from the counts" % (name, g(OLD_POOL), rate(old_harmful, old_decisions)))
    primary = rate(tech_harmful + old_harmful, tech_decisions + old_decisions)

    # the co-primary: S2 target accuracy from the counts, checked against the tranche row
    s2_items, s2_correct = g("shapesTechnical.termError.targetItems"), g("shapesTechnical.termError.targetCorrect")
    s2 = 100.0 * s2_correct / s2_items if s2_items else 0.0
    if abs(s2_items - g("shapesTechnical.definedTerms.shape.targetItems")) > TOLERANCE or \
            abs(s2 - g("shapesTechnical.definedTerms.shape.targetAccuracy")) > 1e-6:
        errors.append("%s: termError %r of %r is not the defined-terms row (accuracy %r)" %
                      (name, s2_correct, s2_items, g("shapesTechnical.definedTerms.shape.targetAccuracy")))

    # the calibration: the two thresholds, their rates from their counts, and the curve rows at 0.90 and 0.95
    cal_rates = []
    for i, (label, want) in enumerate((("0.90", 0.90), ("0.95", 0.95))):
        row = cal + "thresholds.%d." % i
        answers, wrong = g(row + "answers"), g(row + "wrong")
        if abs(g(row + "threshold") - want) > TOLERANCE:
            errors.append("%s: calibration threshold %d is %r, expected %s" % (name, i, g(row + "threshold"), label))
        if abs(rate(wrong, answers) - g(row + "errorPer1k")) > 1e-6:
            errors.append("%s: calibration errorPer1k at %s is %r, re-derived %r" % (name, label, g(row + "errorPer1k"), rate(wrong, answers)))
        curve = cal + "curve.%d." % THRESHOLD_INDEX[label]
        if abs(g(curve + "threshold") - want) > TOLERANCE or g(curve + "answers") != answers or g(curve + "wrong") != wrong:
            errors.append("%s: the curve row at %s differs from the threshold row (answers/wrong %r/%r against %r/%r)" %
                          (name, label, g(curve + "answers"), g(curve + "wrong"), answers, wrong))
        cal_rates.append(rate(wrong, answers))

    # the controls: the pooled accuracy of the 320 technical controls and the proper-noun controls of S2
    items = sum(g(tranche_path(t, "controlItems")) for t in ALL_TECH_TRANCHES)
    correct = sum(g(tranche_path(t, "controlCorrect")) for t in ALL_TECH_TRANCHES)
    proper_items = g("shapesTechnical.definedTerms.kinds.proper.controlItems")
    proper = 100.0 * g("shapesTechnical.definedTerms.kinds.proper.controlCorrect") / proper_items if proper_items else 0.0
    if not items:
        errors.append("%s: the technical tranches have no control items" % name)
    if not proper_items:
        errors.append("%s: the defined-terms tranche has no proper-noun control items" % name)
    run["derived"] = {
        "primary": primary, "s2Accuracy": s2, "cal90": cal_rates[0], "cal95": cal_rates[1],
        "s1HarmfulPer1k": rate(g(tranche_path("deverbalHeads", "harmful")), g(tranche_path("deverbalHeads", "decisions"))),
        "s3HarmfulPer1k": rate(g(tranche_path("stepLines", "harmful")), g(tranche_path("stepLines", "decisions"))),
        "s4HarmfulPer1k": rate(g(tranche_path("clauseVerbs", "harmful")), g(tranche_path("clauseVerbs", "decisions"))),
        "techControlAccuracy": 100.0 * correct / items if items else 0.0, "s2ProperControlAccuracy": proper,
        "techDecisions": tech_decisions, "techHarmful": tech_harmful, "oldDecisions": old_decisions, "oldHarmful": old_harmful,
    }
    return errors


def required_metrics():
    """The metrics (other than the derived ones) that every run must carry because a table, a guard or the rule reads them."""
    return [m[0] for g in GROUPS for m in g[1] if m[3] and not m[0].startswith("derived.")]


def load_runs(directory, seeds, arms=None):
    """Returns {arm id: [run dict per seed]}; raises RunsError on any missing or invalid run (no selection is made)."""
    required = required_metrics()
    runs, errors = {}, []
    for arm in (arms or ARMS):
        runs[arm["id"]] = []
        for seed in seeds:
            path = run_stem(directory, arm["id"], seed) + ".json"
            name = os.path.basename(path)
            if not os.path.isfile(path):
                errors.append("missing run %s" % name)
                continue
            try:
                with open(path, encoding="utf-8") as f:
                    run = json.load(f)
            except (OSError, ValueError) as e:
                errors.append("unreadable run %s: %s" % (name, e))
                continue
            errors += validate_run(run, name, arm, seed, required)
            runs[arm["id"]].append(run)
    if errors:
        raise RunsError("; ".join(errors))
    return runs


def column(arm_runs, path):
    return [get_path(r, path) for r in arm_runs]


def aggregate(values, how):
    return statistics.median(values) if how == "median" else statistics.fmean(values)


# ---------------------------------------------------------------------------------------------------------------------
# Leakage re-derived from the pinned files (acceptance test e)
# ---------------------------------------------------------------------------------------------------------------------

LEAK_SOURCES = {"deverbal": TD + "deverbal.tsv", "terms": TD + "terms.tsv", "steps": TD + "steps.tsv", "clauses": TD + "clauses.tsv",
                "compound": "data/compound/compound.tsv", "compound2": "data/compound/compound-2.tsv"}
LEAK_DEV = {name: SD + name + ".tsv" for name in (
    "deverbal-heads", "defined-terms", "step-lines", "clause-verbs", "compound-head", "compound-head-2", "headings", "headings-2",
    "named-state")}


def read_documents_and_sentences(path):
    """The set of document ids (`# source_id`) and the set of sentence texts (`# text`) of a corpus file."""
    docs, texts = set(), set()
    with open(path, encoding="utf-8") as f:
        for line in f:
            line = line.rstrip("\r\n")
            if line.startswith("# source_id = "):
                docs.add(line[len("# source_id = "):])
            elif line.startswith("# text = "):
                texts.add(line[len("# text = "):])
    return docs, texts


def leakage_problems(repo=REPO, sources=None, dev=None):
    """Returns (problems, counts): a problem for every source/tranche pair that shares a document id or an exact sentence."""
    sources = LEAK_SOURCES if sources is None else sources
    dev = LEAK_DEV if dev is None else dev
    problems, pairs = [], 0
    read = {}
    for group in (sources, dev):
        for name, rel in group.items():
            try:
                read[rel] = read_documents_and_sentences(os.path.join(repo, *rel.split("/")))
            except OSError as e:
                problems.append("leakage input %s cannot be read: %s" % (rel, e))
    if problems:
        return problems, 0
    for s_name, s_rel in sources.items():
        for d_name, d_rel in dev.items():
            pairs += 1
            shared_docs = read[s_rel][0] & read[d_rel][0]
            shared_texts = read[s_rel][1] & read[d_rel][1]
            if shared_docs or shared_texts:
                problems.append("%s and %s share %d document id(s) and %d exact sentence(s)" % (s_name, d_name, len(shared_docs), len(shared_texts)))
    return problems, pairs


# ---------------------------------------------------------------------------------------------------------------------
# Acceptance tests (a), (a2), (b), (c), (e)
# ---------------------------------------------------------------------------------------------------------------------

TRAINING_LINE = re.compile(r"^Training on ([0-9][0-9,.\s\u00a0\u202f]*?) sentences \(.*?\bsources=(\S*)", re.MULTILINE)


def parse_training_line(text):
    """Returns (sentence count, [source:weight, ...]) of the first `Training on N sentences (...), sources=...` line, or None."""
    m = TRAINING_LINE.search(text)
    if not m:
        return None
    digits = re.sub(r"[^0-9]", "", m.group(1))
    return int(digits), [s for s in m.group(2).split(",") if s]


def figure_results(run, figures):
    """The comparison of a run with the table of shipped figures: (path, expected value, equal after rounding, the run's value) for each figure."""
    results = []
    for path, expected, decimals in figures:
        value = get_path(run, path)
        results.append((path, expected, value is not None and round(value, decimals) == expected, value))
    return results


def acceptance(runs, directory, seeds, shipped_path, shipped=None, added=None, base_hashes=None, leakage=None):
    """Runs the acceptance tests (a), (a2), (b), (c) and, when `leakage` is not None, (e); returns a list of {name, ok, detail}.

    `shipped` is (weights sha, figures) as load_shipped_figures returns it; `leakage` is a (problems, pairs) tuple as leakage_problems returns it.
    """
    shipped_sha_pinned, shipped_figures = load_shipped_figures() if shipped is None else shipped
    added = ADDED_SENTENCES if added is None else added
    base_hashes = BASE_WEIGHT_SHA256 if base_hashes is None else base_hashes
    checks = []

    def add(name, ok, detail):
        checks.append({"name": name, "ok": bool(ok), "detail": detail})

    # (a) the BASE run of the shipped seed is the shipped model
    base_run = runs[BASE_ID][seeds.index(SHIPPED_SEED)] if SHIPPED_SEED in seeds else None
    if base_run is None:
        add("a: BASE seed %d present" % SHIPPED_SEED, False, "the seed is not in the seed list")
    else:
        try:
            shipped_sha = sha256_file(shipped_path)
        except OSError as e:
            shipped_sha = None
            add("a: shipped weights readable", False, str(e))
        if shipped_sha:
            run_sha = (base_run.get("weights") or {}).get("sha256")
            add("a: BASE seed %d weights SHA-256 equals the shipped weights.bin" % SHIPPED_SEED, run_sha == shipped_sha,
                "run %s, shipped %s" % (run_sha, shipped_sha))
            add("a: the shipped weights.bin is the one that the figures were measured on", shipped_sha == shipped_sha_pinned,
                "file %s, figures %s" % (shipped_sha, shipped_sha_pinned))
            bin_path = run_stem(directory, BASE_ID, SHIPPED_SEED) + ".bin"
            if os.path.isfile(bin_path):
                bin_sha = sha256_file(bin_path)
                add("a: BASE seed %d weights file is byte-identical to the shipped weights.bin" % SHIPPED_SEED, bin_sha == shipped_sha,
                    "file %s, shipped %s" % (bin_sha, shipped_sha))
        for path, expected, ok, value in figure_results(base_run, shipped_figures):
            add("a: BASE seed %d %s = %s" % (SHIPPED_SEED, path, expected), ok, "run %s" % value)

    # (a2) the BASE weights of every seed are the CMP2 weights of the earlier experiment
    for seed, run in zip(seeds, runs[BASE_ID]):
        run_sha = (run.get("weights") or {}).get("sha256")
        add("a2: BASE seed %s weights SHA-256 equals the CMP2 weights of the follow-up" % seed, run_sha == base_hashes.get(seed),
            "run %s, pinned %s" % (run_sha, base_hashes.get(seed)))

    # (b) the training lines, the sources and the sentence counts
    counts = {}
    for arm in ARMS:
        counts[arm["id"]] = {}
        for seed in seeds:
            path = run_stem(directory, arm["id"], seed) + ".train.txt"
            try:
                with open(path, encoding="utf-8", errors="replace") as f:
                    parsed = parse_training_line(f.read())
            except OSError:
                parsed = None
            if parsed is None:
                add("b: %s seed %s training line" % (arm["id"], seed), False, "no readable `Training on N sentences` line in %s" % path)
            else:
                counts[arm["id"]][seed] = parsed
    for arm in ARMS:
        for seed in seeds:
            if seed not in counts[arm["id"]] or seed not in counts[BASE_ID]:
                continue
            count, sources = counts[arm["id"]][seed]
            base_count = counts[BASE_ID][seed][0]
            more = sum(weight * added[name] for name, weight in arm["sources"].items())
            want_sources = BASE_MIX + ["%s:%d" % (name, weight) for name, weight in arm["sources"].items()]
            ok = count == base_count + more and sorted(sources) == sorted(want_sources)
            if arm["id"] == BASE_ID:
                ok = ok and count == BASE_SENTENCES
            what = "the shipped mix only (%s sentences)" % format(BASE_SENTENCES, ",") if not arm["sources"] else \
                "BASE + %d sentences (%s)" % (more, ", ".join("%s:%d" % (n, w) for n, w in arm["sources"].items()))
            add("b: %s seed %s trained on %s" % (arm["id"], seed, what), ok,
                "log: %d sentences (BASE %d), sources %s" % (count, base_count, ",".join(sources)))

    # (c) every run scored the frozen development files, with one clean trainer commit
    seen = {}
    commits, dirty = set(), set()
    for items in runs.values():
        for run in items:
            inputs = run.get("inputs") or {}
            for key in FROZEN_INPUTS:
                seen.setdefault(key, set()).add(inputs.get(key))
            commits.add(inputs.get("commit"))
            dirty.add(inputs.get("dirty"))
    for key, expected in FROZEN_INPUTS.items():
        values = seen.get(key, {None})
        add("c: inputs.%s agrees in all runs and equals the frozen value" % key, values == {expected},
            "values %s, frozen %s" % (sorted(str(v) for v in values), expected))
    add("c: all runs were made by one trainer commit", len(commits) == 1 and None not in commits, "commits %s" % sorted(str(c) for c in commits))
    add("c: no run was made from a dirty working tree", dirty == {False}, "dirty flags %s" % sorted(str(d) for d in dirty))

    # (e) leakage re-derived from the pinned files
    if leakage is not None:
        problems, pairs = leakage
        add("e: no shared document id or exact sentence between the six training sources and the nine development tranches",
            not problems and pairs == len(LEAK_SOURCES) * len(LEAK_DEV),
            "%d pairs checked; %s" % (pairs, "; ".join(problems) if problems else "no overlap"))
    return checks


# ---------------------------------------------------------------------------------------------------------------------
# Analysis and the selection rule
# ---------------------------------------------------------------------------------------------------------------------

def check_guard(guard, arm_values, base_values):
    name, label, path, kind, limit, how = guard
    arm_v, base_v = aggregate(arm_values, how), aggregate(base_values, how)
    if kind == "max_delta":
        ok, shown = arm_v - base_v <= limit + TOLERANCE, arm_v - base_v
    elif kind == "min_delta":
        ok, shown = arm_v - base_v >= limit - TOLERANCE, arm_v - base_v
    elif kind == "max_ratio":
        ok, shown = arm_v <= limit * base_v * (1.0 + TOLERANCE), arm_v / base_v if base_v else float("inf")
    else:
        ok, shown = arm_v >= limit - TOLERANCE, arm_v
    return {"name": name, "label": label, "metric": path, "armValue": arm_v, "baseValue": base_v, "shown": shown, "limit": limit,
            "pass": bool(ok)}


def pick_winner(within, arms):
    """The arm of `within` with the fewest weighted added sentences; ties: the lowest primary mean; then the order of the arms."""
    order = [a["id"] for a in ARMS]
    return min(within, key=lambda a: (arms[a]["added"], arms[a]["primaryMean"], order.index(a) if a in order else 99))


def interval(entry):
    d, lo, hi, df, p = entry
    return {"mean": d, "lo": lo, "hi": hi, "df": df, "p": p}


def decide(runs, welch=None):
    """Applies the pre-registered rule T to the loaded runs (a pure function of the numbers). Returns the decision dictionary.

    `welch` is the interval function (the frozen one by default); the self-test injects others to test the strict inequalities.
    """
    welch = welch or welch_diff
    base_primary = column(runs[BASE_ID], PRIMARY)
    base_s2 = column(runs[BASE_ID], S2)
    arms = {}
    for arm in ARMS:
        label = arm["id"]
        if label not in runs:
            continue
        primary, s2 = column(runs[label], PRIMARY), column(runs[label], S2)
        info = {"role": arm["role"], "added": weighted_added(arm), "primaryMean": statistics.fmean(primary),
                "primarySd": statistics.stdev(primary), "primaryValues": primary, "s2Mean": statistics.fmean(s2),
                "s2Sd": statistics.stdev(s2), "s2Values": s2}
        for key, path in (("cal90", CAL90), ("cal95", CAL95)):
            values = column(runs[label], path)
            info[key + "Mean"], info[key + "Sd"] = statistics.fmean(values), statistics.stdev(values)
        if label != BASE_ID:
            info["diffPrimary"] = interval(welch(primary, base_primary))
            info["diffS2"] = interval(welch(s2, base_s2))
            info["guards"] = [check_guard(g, column(runs[label], g[2]), column(runs[BASE_ID], g[2])) for g in GUARDS]
            info["passesAllGuards"] = all(g["pass"] for g in info["guards"])
            info["primaryBelowZero"] = bool(info["diffPrimary"]["hi"] < 0.0)  # strict: an upper bound of exactly 0 is not an improvement
            info["s2AboveZero"] = bool(info["diffS2"]["lo"] > 0.0)             # strict: a lower bound of exactly 0 is not a gain
            info["eligible"] = arm["role"] == "candidate"
            info["inQ"] = info["eligible"] and info["passesAllGuards"] and info["primaryBelowZero"] and info["s2AboveZero"]
        arms[label] = info
    pairs = {}
    for a, b in PAIRS:
        if a in runs and b in runs:
            pairs["%s - %s" % (a, b)] = {m: interval(welch(column(runs[a], m), column(runs[b], m))) for m in PAIR_METRICS}

    qualifying = [a for a in CANDIDATES if a in arms and arms[a]["inQ"]]
    decision = {"schema": 1, "rule": "T", "baseline": BASE_ID, "primaryMetric": PRIMARY, "coPrimaryMetric": S2, "seeds": len(base_primary),
                "arms": arms, "pairs": pairs, "passingGuards": [a for a in CANDIDATES if a in arms and arms[a]["passesAllGuards"]],
                "qualifying": qualifying, "lowestPrimary": None, "withinTenPercent": [], "candidate": None, "candidateInterval": None,
                "wins": False, "winner": BASE_ID}
    if not qualifying:
        decision["reason"] = ("No candidate arm passes every guard and has a Welch 95%% interval of (arm - BASE) entirely below 0 on the "
                              "primary and entirely above 0 on the S2 target accuracy (Q is empty): BASE stays, nothing ships.")
        return decision

    lowest = min(qualifying, key=lambda a: (arms[a]["primaryMean"], CANDIDATES.index(a)))
    m = arms[lowest]["primaryMean"]
    within = [a for a in qualifying if arms[a]["primaryMean"] <= WINDOW * m + TOLERANCE]
    winner = pick_winner(within, arms)
    d_p, d_s = arms[winner]["diffPrimary"], arms[winner]["diffS2"]
    decision.update({"lowestPrimary": {"arm": lowest, "value": m}, "withinTenPercent": within, "candidate": winner,
                     "candidateInterval": d_p, "candidateIntervalS2": d_s, "wins": True, "winner": winner})
    decision["reason"] = ("%s wins: it has the fewest weighted added sentences (%s) among the arms of Q within 10%% of the lowest primary "
                          "(%s, %.2f); the Welch 95%% interval of (%s - BASE) is [%.2f, %.2f] on the primary (entirely below 0) and "
                          "[%.2f, %.2f] on the S2 target accuracy (entirely above 0)." %
                          (winner, format(arms[winner]["added"], ","), lowest, m, winner, d_p["lo"], d_p["hi"], d_s["lo"], d_s["hi"]))
    return decision


# ---------------------------------------------------------------------------------------------------------------------
# Report
# ---------------------------------------------------------------------------------------------------------------------

def cell(values, decimals):
    if any(v is None for v in values):
        return "n/a"
    return "%.*f ± %.*f" % (decimals, statistics.fmean(values), decimals, statistics.stdev(values) if len(values) > 1 else 0.0)


def table(header, rows):
    lines = ["| " + " | ".join(header) + " |", "| " + " | ".join(["---"] * len(header)) + " |"]
    lines += ["| " + " | ".join(str(c) for c in r) + " |" for r in rows]
    return "\n".join(lines)


def guard_cell(g):
    kind = next(x[3] for x in GUARDS if x[0] == g["name"])
    shown = {"max_delta": "%+.2f", "min_delta": "%+.2f", "max_ratio": "%.3fx", "min_abs": "%.0f"}[kind] % g["shown"]
    return "%s %s" % ("PASS" if g["pass"] else "FAIL", shown)


def yes(flag):
    return "yes" if flag else "no"


def build_report(runs, decision, checks, directory, seeds):
    order = [a["id"] for a in ARMS if a["id"] in runs]
    out = ["# beta.3 phase 2 technical-mix experiment: analysis", "",
           "Arms: %s; seeds: %s (%d per arm); folder `%s`. Values are mean ± sample sd over the seeds. Baseline: BASE (the shipped model)." %
           (", ".join(order), ", ".join(str(s) for s in seeds), len(seeds), directory), ""]
    out += table(["arm", "sources added (weight)", "weighted sentences added", "role"],
                 [[a, ", ".join("%s %d" % (n, w) for n, w in arm_by_id(a)["sources"].items()) or "none",
                   format(weighted_added(arm_by_id(a)), ","), arm_by_id(a)["role"]] for a in order]).split("\n") + [""]
    out += ["## Acceptance tests", "", table(["test", "result", "detail"],
                                              [[c["name"], "PASS" if c["ok"] else "FAIL", c["detail"]] for c in checks]), ""]
    if decision is None:
        out += ["**An acceptance test failed: no selection is made.**", ""]
        return "\n".join(out)

    base = decision["arms"][BASE_ID]
    out += ["## Selection table (rule T)", "",
            "Primary = pooled harmful per 1k over S1+S3+S4 and the four compound and heading tranches (lower is better; BASE mean %.2f); "
            "co-primary = S2 target accuracy (higher is better; BASE mean %.2f); calibration = wrong per 1,000 answers at >= 0.90 and >= 0.95 "
            "over the judged words of the four technical tranches (BASE means %.2f and %.2f; guards: an arm's mean must be <= the BASE mean). "
            "Delta guards use the mean over the seeds against BASE; the size guard is 1.5 x the BASE mean; the throughput guard is absolute "
            "(median over the seeds of each run's median). Q = the candidates that pass every guard and whose Welch interval of (arm - BASE) "
            "lies entirely below 0 on the primary and entirely above 0 on the S2 accuracy. TECH-NT is attribution only (it is never in Q)." %
            (base["primaryMean"], base["s2Mean"], base["cal90Mean"], base["cal95Mean"]), ""]
    for start in range(0, len(GUARDS), 7):
        chunk = GUARDS[start:start + 7]
        rows = []
        for a in order:
            if a == BASE_ID:
                continue
            info = decision["arms"][a]
            by_name = {g["name"]: g for g in info["guards"]}
            rows.append([a] + [guard_cell(by_name[g[0]]) for g in chunk])
        out += [table(["arm"] + [g[1] for g in chunk], rows), ""]
    rows = [[BASE_ID, "%.2f ± %.2f" % (base["primaryMean"], base["primarySd"]), "%.2f ± %.2f" % (base["s2Mean"], base["s2Sd"]),
             "-", "-", "-", "-", "baseline"]]
    for a in order:
        if a == BASE_ID:
            continue
        info = decision["arms"][a]
        rows.append([a, "%.2f ± %.2f" % (info["primaryMean"], info["primarySd"]), "%.2f ± %.2f" % (info["s2Mean"], info["s2Sd"]),
                     yes(info["passesAllGuards"]), yes(info["primaryBelowZero"]), yes(info["s2AboveZero"]),
                     yes(info["inQ"]), info["role"]])
    out += [table(["arm", "primary", "S2 acc %", "passes every guard", "primary interval below 0", "S2 interval above 0", "in Q", "role"], rows), ""]

    out += ["## Welch 95% interval of (arm - BASE)", "",
            "Difference of the means with the Welch-Satterthwaite t interval over the seeds (seeds measure training noise only; five arms against "
            "one baseline, no multiplicity correction).", ""]
    rows = []
    for a in order:
        if a == BASE_ID:
            continue
        row = [a]
        for m in WELCH_METRICS:
            d, lo, hi, df, p = welch_diff(column(runs[a], m), column(runs[BASE_ID], m))
            row.append("%+.2f [%+.2f, %+.2f]" % (d, lo, hi))
        rows.append(row)
    out += [table(["arm"] + WELCH_METRICS, rows), ""]

    out += ["## Pairwise Welch 95% interval (reported, not gated)", ""]
    rows = [[name] + ["%+.2f [%+.2f, %+.2f]" % (e[m]["mean"], e[m]["lo"], e[m]["hi"]) for m in PAIR_METRICS] for name, e in decision["pairs"].items()]
    out += [table(["pair"] + PAIR_METRICS, rows), ""]

    out += ["## Selection rule", ""]
    rows = []
    for a in order:
        info = decision["arms"][a]
        rows.append([a, format(info["added"], ","), "%.2f ± %.2f" % (info["primaryMean"], info["primarySd"]),
                     "-" if a == BASE_ID else yes(info["passesAllGuards"]), "-" if a == BASE_ID else yes(info["primaryBelowZero"]),
                     "-" if a == BASE_ID else yes(info["s2AboveZero"]), "-" if a == BASE_ID else yes(info["inQ"])])
    out += [table(["arm", "weighted sentences added", "primary", "passes every guard", "primary interval below 0", "S2 interval above 0", "in Q"], rows), ""]
    out += ["- Arms in Q: %s" % (", ".join(decision["qualifying"]) or "none")]
    if decision["lowestPrimary"]:
        lp = decision["lowestPrimary"]
        out += ["- Lowest primary in Q: %s (%.2f); within 10%% (primary <= %.2f): %s" %
                (lp["arm"], lp["value"], WINDOW * lp["value"], ", ".join(decision["withinTenPercent"])),
                "- Winner (fewest weighted added sentences, then lowest primary): %s" % decision["winner"]]
        ci, cs = decision["candidateInterval"], decision["candidateIntervalS2"]
        out += ["- Welch 95%% interval of (%s - BASE) on the primary: %+.2f [%+.2f, %+.2f], df %.2f, p %.4f" %
                (decision["winner"], ci["mean"], ci["lo"], ci["hi"], ci["df"], ci["p"]),
                "- Welch 95%% interval of (%s - BASE) on the S2 accuracy: %+.2f [%+.2f, %+.2f], df %.2f, p %.4f" %
                (decision["winner"], cs["mean"], cs["lo"], cs["hi"], cs["df"], cs["p"])]
    out += ["", "**Decision: %s.** %s" % (("%s WINS" % decision["winner"]) if decision["wins"] else "BASE STAYS", decision["reason"]), ""]

    out += ["## Per-seed primary / S2 accuracy / wrong per 1k at >= 0.90 / at >= 0.95", ""]
    out += [table(["arm"] + [str(s) for s in seeds],
                  [[a] + ["%.1f / %.1f / %.1f / %.1f" % (get_path(r, PRIMARY), get_path(r, S2), get_path(r, CAL90), get_path(r, CAL95))
                          for r in runs[a]] for a in order]), ""]

    out += ["## Full table (reported, not gated)", ""]
    for title, metrics in GROUPS:
        out += ["### %s" % title, ""]
        out += [table(["arm"] + [m[1] for m in metrics], [[a] + [cell(column(runs[a], m[0]), m[2]) for m in metrics] for a in order]), ""]

    commits = sorted({str((r.get("inputs") or {}).get("commit")) for items in runs.values() for r in items})
    dirty = sorted({str((r.get("inputs") or {}).get("dirty")) for items in runs.values() for r in items})
    out += ["Trainer commit of the runs (`inputs.commit`): %s; dirty flag: %s." % (", ".join(commits), ", ".join(dirty)), ""]
    return "\n".join(out)


def analyze(directory, shipped_path=DEFAULT_SHIPPED, verify=True, shipped=None, added=None, base_hashes=None):
    """Loads the runs, runs the frozen-file check, the acceptance tests and the leakage check, and applies the rule.

    Returns (runs, checks, decision, seeds); the decision is None when an acceptance test failed (no selection is made).
    Raises RunsError for missing or invalid runs or a frozen-file mismatch.
    """
    problems = []
    if verify:
        problems = verify_frozen(table={**FROZEN_FILES, **PRE_ADOPTION_TEXT, **input_files_table()}, raw_table=PRE_ADOPTION_RAW)
    if problems:
        raise RunsError("; ".join(problems))
    seeds, spec_arms = arms_spec()
    if seeds != EXPECTED_SEEDS:
        raise RunsError("arms.json seeds %r are not the registered seeds %r" % (seeds, EXPECTED_SEEDS))
    if [a["id"] for a in spec_arms] != [a["id"] for a in ARMS]:
        raise RunsError("arms.json does not list the arms of this script in order")
    for arm in ARMS:
        found = next((a for a in spec_arms if a["id"] == arm["id"]), None)
        if found is None or sorted(found["features"]) != sorted(arm["features"]) or found["sources"] != arm["sources"] or found["role"] != arm["role"]:
            raise RunsError("arms.json does not define the arm %s as this script expects" % arm["id"])
    runs = load_runs(directory, seeds)
    checks = acceptance(runs, directory, seeds, shipped_path, shipped, added, base_hashes, leakage_problems() if verify else None)
    if verify:
        checks.insert(0, {"name": "frozen files have their frozen SHA-256 (%d text files, %d development files, %d raw)" %
                          (len(FROZEN_FILES) + len(PRE_ADOPTION_TEXT), len(INPUT_FILES), len(PRE_ADOPTION_RAW)), "ok": True,
                          "detail": "checked"})
    decision = decide(runs) if all(c["ok"] for c in checks) else None
    return runs, checks, decision, seeds


# ---------------------------------------------------------------------------------------------------------------------
# Self-test with synthetic run results
# ---------------------------------------------------------------------------------------------------------------------

# Eight seeds: offsets -3.5 .. 3.5 have sum 0 and sum of squares 42 = 7 x 6, so value = mean + offset / sqrt(6) x sd has an exact mean and
# an exact sample sd.
SQ = math.sqrt(6.0)
OFFSETS = [-3.5, -2.5, -1.5, -0.5, 0.5, 1.5, 2.5, 3.5]
TECH_DECISIONS = {"deverbalHeads": 360, "stepLines": 155, "clauseVerbs": 302}
OLD_DECISIONS = {"compoundHead": 100, "compoundHead2": 210, "headings": 78, "headings2": 82}
CONTROL_ITEMS = {"deverbalHeads": 90, "definedTerms": 70, "stepLines": 70, "clauseVerbs": 90}
S2_TARGETS = 215
RATE_KEYS = {"s1": "deverbalHeads", "s3": "stepLines", "s4": "clauseVerbs"}
BASE_SPEC = {
    # BASE means and sds (the seed offsets give an exact mean and sample sd). The "rate.*" entries are the harmful rates of the groups of
    # tranches (all equal, so the primary has the same mean and sd); the others are spread over the seeds as they are.
    "rate.s1": (225.0, 6.0), "rate.s3": (225.0, 6.0), "rate.s4": (225.0, 6.0), "rate.old": (225.0, 6.0),
    "s2": (30.7, 2.0), "cal90": (117.3, 6.0), "cal95": (57.1, 5.0), "techctrl": (95.0, 1.0), "s2proper": (95.7, 1.5),
    "shapes.hard.pooled.harmfulPer1k": (160.0, 8.0), "shapes.standard.pooled.harmfulPer1k": (12.0, 1.0),
    "shapesNew.namedState.shape.targetAccuracy": (92.9, 1.2), IMPERATIVE: (47.9, 8.0),
    "openDev.accuracy": (96.70, 0.04), "openDev.harmfulPer1k": (21.0, 0.5), "openDev.nll": (0.11, 0.001), "openDev.ece": (0.24, 0.02),
    "goldDev.accuracy": (95.30, 0.14), "goldDev.harmfulPer1k": (18.0, 1.5), "udDev.accuracy": (92.95, 0.03),
    "udDev.harmfulPer1k": (20.0, 0.4), "temperature": (9.5, 0.1), "weights.rows": (125773, 100), "weights.bytesRaw": (1157499, 500),
    "weights.bytesDeflated": (521695, 200), "throughput.tokensPerSecond": (400000.0, 10000.0), "trainWallSeconds": (60.0, 1.0),
}
SYNTH_BASE_SENTENCES = BASE_SENTENCES
SPECIAL_KEYS = {"s2", "cal90", "cal95", "techctrl", "s2proper"}  # the synthetic values that are realized as counts, not set as they are


def set_path(obj, path, value):
    """Sets the member at a dotted path, creating dictionaries on the way; list members are addressed by index and must exist."""
    parts = path.split(".")
    for p in parts[:-1]:
        obj = obj[int(p)] if isinstance(obj, list) else obj.setdefault(p, {})
    if isinstance(obj, list):
        obj[int(parts[-1])] = value
    else:
        obj[parts[-1]] = value


def synthetic_run(label, seed, si, ai, values):
    """One synthetic run: the counts of the seven tranches, S2, the calibration and the controls realize the values of this seed."""
    arm = arm_by_id(label)
    run = {"schema": 1, "arm": label, "seed": seed, "features": list(arm["features"]), "groups": list(arm["features"]),
           "sources": dict(arm["sources"])}
    v = {k: m + OFFSETS[(si + ai) % 8] / SQ * sd for k, (m, sd) in values.items()}
    for key, tranche in RATE_KEYS.items():
        n = TECH_DECISIONS[tranche]
        set_path(run, tranche_path(tranche, "decisions"), n)
        set_path(run, tranche_path(tranche, "harmful"), v["rate." + key] * n / 1000.0)
    for tranche, n in OLD_DECISIONS.items():
        set_path(run, tranche_path(tranche, "decisions"), n)
        set_path(run, tranche_path(tranche, "harmful"), v["rate.old"] * n / 1000.0)
    t_n = sum(TECH_DECISIONS.values())
    t_h = sum(get_path(run, tranche_path(t, "harmful")) for t in TECH_DECISIONS)
    o_n = sum(OLD_DECISIONS.values())
    o_h = sum(get_path(run, tranche_path(t, "harmful")) for t in OLD_DECISIONS)
    set_path(run, "shapesTechnical.pooledTechnical.decisions", t_n)
    set_path(run, "shapesTechnical.pooledTechnical.harmful", t_h)
    set_path(run, "shapesFollowup.pooledPrimary.decisions", o_n)
    set_path(run, "shapesFollowup.pooledPrimary.harmful", o_h)
    set_path(run, OLD_POOL, rate(o_h, o_n))
    set_path(run, "shapesTechnical.termError.targetItems", S2_TARGETS)
    set_path(run, "shapesTechnical.termError.targetCorrect", v["s2"] * S2_TARGETS / 100.0)
    set_path(run, "shapesTechnical.definedTerms.shape.targetItems", S2_TARGETS)
    set_path(run, "shapesTechnical.definedTerms.shape.targetAccuracy", v["s2"])
    cal = "shapesTechnical.calibration.technical."
    thresholds = []
    for key, answers, want in (("cal90", 733, 0.90), ("cal95", 595, 0.95)):
        wrong = v[key] * answers / 1000.0
        thresholds.append({"threshold": want, "answers": answers, "wrong": wrong, "errorPer1k": rate(wrong, answers)})
    set_path(run, cal + "thresholds", thresholds)
    grid = [0.45, 0.50, 0.55, 0.60, 0.65, 0.70, 0.75, 0.80, 0.85, 0.90, 0.95, 0.96, 0.97, 0.98, 0.99]
    curve = []
    for i, threshold in enumerate(grid):
        row = {"threshold": threshold, "answers": 1000 - 20 * i, "wrong": 10 + i}
        if i in (9, 10):
            row.update({"answers": thresholds[i - 9]["answers"], "wrong": thresholds[i - 9]["wrong"]})
        curve.append(row)
    set_path(run, cal + "curve", curve)
    # the controls: S2 proper controls at the proper accuracy, the other 250 at the accuracy that makes the pool v["techctrl"]
    total_items = sum(CONTROL_ITEMS.values())
    proper_correct = v["s2proper"] * CONTROL_ITEMS["definedTerms"] / 100.0
    rest_correct = v["techctrl"] * total_items / 100.0 - proper_correct
    rest_items = total_items - CONTROL_ITEMS["definedTerms"]
    for tranche, n in CONTROL_ITEMS.items():
        set_path(run, tranche_path(tranche, "controlItems"), n)
        set_path(run, tranche_path(tranche, "controlCorrect"), proper_correct if tranche == "definedTerms" else rest_correct * n / rest_items)
    set_path(run, "shapesTechnical.definedTerms.kinds.proper.controlItems", CONTROL_ITEMS["definedTerms"])
    set_path(run, "shapesTechnical.definedTerms.kinds.proper.controlCorrect", proper_correct)
    for path in values:
        if path not in SPECIAL_KEYS and not path.startswith("rate."):
            set_path(run, path, v[path])
    return run


def synthetic_runs(overrides, labels=None, seeds=None):
    """overrides: {arm id: {key: (mean, sd)}}. Values are mean + sd x offset / sqrt(6): an exact mean and sample sd over 8 seeds."""
    labels = labels or [a["id"] for a in ARMS]
    seeds = seeds or arms_spec()[0]
    runs = {}
    for ai, label in enumerate(labels):
        values = dict(BASE_SPEC)
        values.update(overrides.get(label, {}))
        runs[label] = [synthetic_run(label, seed, si, ai, values) for si, seed in enumerate(seeds)]
        for seed, run in zip(seeds, runs[label]):
            problems = validate_run(run, "synthetic %s-s%s" % (label, seed), arm_by_id(label), seed, required_metrics())  # adds the derived members
            assert not problems, problems
    return runs


def synthetic_hash(label, seed):
    return hashlib.sha256(("%s-%s" % (label, seed)).encode()).hexdigest()


def write_synthetic(tmp, runs, shipped_bytes, tweak=None, more=None):
    """Writes the run files, training logs and the shipped weights of a synthetic experiment; returns (directory, shipped path)."""
    seeds = arms_spec()[0]
    more = dict(ADDED_SENTENCES, **(more or {}))
    shipped = os.path.join(tmp, "shipped-weights.bin")
    with open(shipped, "wb") as f:
        f.write(shipped_bytes)
    directory = os.path.join(tmp, "runs")
    os.makedirs(directory)
    for label, items in runs.items():
        arm = arm_by_id(label)
        for seed, run in zip(seeds, items):
            run = json.loads(json.dumps(run))
            run.pop("derived", None)  # the analyzer derives it from the result file, as for a real run
            shipped_run = label == BASE_ID and seed == SHIPPED_SEED
            run["weights"]["sha256"] = hashlib.sha256(shipped_bytes).hexdigest() if shipped_run else synthetic_hash(label, seed)
            run["inputs"] = dict(FROZEN_INPUTS, commit="0123456789abcdef", dirty=False)
            n = SYNTH_BASE_SENTENCES + sum(weight * more[name] for name, weight in arm["sources"].items())
            line = "Training on %s sentences (1,000,000 tokens per epoch), sources=%s epochs=6 seed=%s\n" % (
                format(n, ","), ",".join(BASE_MIX + ["%s:%d" % (name, w) for name, w in arm["sources"].items()]), seed)
            if tweak:
                holder = [line]
                tweak(label, seed, run, holder)
                line = holder[0]
            stem = run_stem(directory, label, seed)
            with open(stem + ".json", "w", encoding="utf-8") as f:
                json.dump(run, f)
            with open(stem + ".train.txt", "w", encoding="utf-8") as f:
                f.write("some jackknife line\n" + line + "Trained 1 steps in 1.0 s\n")
    return directory, shipped


def synthetic_base_hashes(shipped_bytes):
    """The pinned BASE weights hashes of the synthetic experiment: the shipped bytes for the shipped seed, a hash of the run name otherwise."""
    return {seed: hashlib.sha256(shipped_bytes).hexdigest() if seed == SHIPPED_SEED else synthetic_hash(BASE_ID, seed) for seed in arms_spec()[0]}


SYNTH_FIGURE_PATHS = ["shapesTechnical.pooledTechnical.decisions", "shapesTechnical.termError.targetItems", "shapesFollowup.pooledPrimary.decisions",
                      "shapesTechnical.calibration.technical.thresholds.0.answers", "shapesTechnical.calibration.technical.curve.3.answers",
                      "openDev.harmfulPer1k"]


def synthetic_shipped(run, shipped_bytes):
    """The (weights sha, figures) pair of a synthetic shipped model: a few figures of the synthetic BASE run of the shipped seed."""
    return hashlib.sha256(shipped_bytes).hexdigest(), [(p, round(get_path(run, p), 3), 3) for p in SYNTH_FIGURE_PATHS]


def count_sentences(path):
    with open(path, encoding="utf-8") as f:
        return sum(1 for line in f if line.startswith("# sent_id = "))


def selftest():
    failures = []

    def check(name, condition, detail=""):
        if not condition:
            failures.append("%s %s" % (name, detail))

    frozen = frozen_module()
    seeds, spec_arms = arms_spec()

    # Student t (the frozen function) against published 0.975 quantiles.
    for df, q in {1: 12.7062047, 2: 4.3026527, 4: 2.7764451, 5: 2.5705818, 8: 2.3060041, 10: 2.2281389, 30: 2.0422725,
                  120: 1.9799304}.items():
        check("t(0.975,%d)" % df, abs(frozen.t_quantile(0.975, df) - q) < 1e-6, "got %.7f" % frozen.t_quantile(0.975, df))
    # Welch: hand-computed case a = 2,4,..,10 and b = 1..5 (variances 10 and 2.5, df = 6.25 / 1.0625).
    d, lo, hi, df, p = welch_diff([2, 4, 6, 8, 10], [1, 2, 3, 4, 5])
    check("welch d", abs(d - 3.0) < 1e-12)
    check("welch df", abs(df - 6.25 / 1.0625) < 1e-9, "got %r" % df)
    check("welch interval", abs((hi - lo) / 2.0 - frozen.t_quantile(0.975, df) * math.sqrt(2.5)) < 1e-9)
    check("welch p", abs(p - frozen.t_two_sided_p(d / math.sqrt(2.5), df)) < 1e-12)
    check("offsets mean", abs(statistics.fmean(OFFSETS)) < 1e-12 and abs(statistics.stdev([o / SQ for o in OFFSETS]) - 1.0) < 1e-12)

    # Frozen files: the real files match; a change is found; CRLF is normalized; the raw table is not.
    check("frozen files", verify_frozen() == [], str(verify_frozen()))
    check("frozen files are listed", len(FROZEN_FILES) == 28 and REL_FROZEN_ANALYZER in FROZEN_FILES and B3 + "run_arms.ps1" in FROZEN_FILES
          and B3 + "arms.json" in FROZEN_FILES and B3 + "shipped_figures.json" in FROZEN_FILES, str(len(FROZEN_FILES)))
    check("frozen development files", verify_frozen(table=input_files_table()) == [], str(verify_frozen(table=input_files_table())))
    check("frozen mismatch", len(verify_frozen(table={REL_FROZEN_ANALYZER: "0" * 64})) == 1)
    check("frozen missing", len(verify_frozen(table={"no/such/file": "0" * 64})) == 1)
    check("frozen raw mismatch", len(verify_frozen(table={}, raw_table={REL_FROZEN_ANALYZER: "0" * 64})) == 1)
    check("crlf normalized", sha256_lf(b"a\r\nb\n") == sha256_lf(b"a\nb\n") and sha256_lf(b"a\nb") != sha256_lf(b"a\nc"))
    check("pre-adoption state is pinned", list(PRE_ADOPTION_TEXT) == [T + "training.json"]
          and list(PRE_ADOPTION_RAW) == ["src/DemaConsulting.PartOfSpeech/Resources/weights.bin"], str(PRE_ADOPTION_TEXT))
    check("shipped weights hash is the pinned BASE seed hash", PRE_ADOPTION_RAW.get("src/DemaConsulting.PartOfSpeech/Resources/weights.bin")
          == BASE_WEIGHT_SHA256[SHIPPED_SEED])
    check("input files are inputs", set(INPUT_FILES) <= set(FROZEN_INPUTS) and len(FROZEN_INPUTS) == 14 and len(INPUT_FILES) == 13,
          str(sorted(FROZEN_INPUTS)))
    check("eight BASE weight hashes", sorted(BASE_WEIGHT_SHA256) == sorted(EXPECTED_SEEDS) and len(set(BASE_WEIGHT_SHA256.values())) == 8)
    check("arms.json seeds", seeds == EXPECTED_SEEDS == [20260101, 11, 12, 13, 14, 15, 16, 17], str(seeds))
    for arm in ARMS:
        found = next((a for a in spec_arms if a["id"] == arm["id"]), None)
        check("arms.json %s" % arm["id"], found is not None and found["features"] == arm["features"] and found["sources"] == arm["sources"]
              and found["role"] == arm["role"], str(found))
    check("arms.json has no other arm and keeps the order", [a["id"] for a in spec_arms] == [a["id"] for a in ARMS])
    check("weighted additions", [weighted_added(a) for a in ARMS] == [0, 1851, 1478, 1214, 2224, 3702], str([weighted_added(a) for a in ARMS]))
    check("candidates and attribution", CANDIDATES == ["TECH", "TECH-ND", "TECH-T2", "TECH-W2"] and arm_by_id("TECH-NT")["role"] == "attribution")
    for source, rel in DATA_FILES.items():
        n = count_sentences(os.path.join(REPO, *rel.split("/")))
        check("sentence count of %s" % source, n == ADDED_SENTENCES[source], "%d" % n)
    check("BASE mix", BASE_MIX == ["open:2", "masc:1", "silver:1", "imperative:1", "compound:1", "compound2:1"] and BASE_SENTENCES == 63522)
    check("training line parse", parse_training_line("x\nTraining on 63,522 sentences (1,234 tokens per epoch), sources=open:2,masc:1 epochs=6\n")
          == (63522, ["open:2", "masc:1"]))
    check("training line parse (no separator)", parse_training_line("Training on 62422 sentences (1 tokens per epoch), sources=open:2")
          == (62422, ["open:2"]))
    check("training line parse (none)", parse_training_line("trained on 5 sentences") is None)
    check("guards: twenty, calibration last", len(GUARDS) == 20 and [g[0] for g in GUARDS][-2:] == ["cal90", "cal95"]
          and len({g[0] for g in GUARDS}) == 20, str([g[0] for g in GUARDS]))
    check("primary and co-primary paths", PRIMARY == "derived.primary" and S2 == "derived.s2Accuracy")
    check("curve positions of 0.90 and 0.95", THRESHOLD_INDEX == {"0.90": 9, "0.95": 10})

    # The real table of shipped figures: it names the shipped weights, is self-consistent, and every figure is enforced.
    shipped_sha, figures = load_shipped_figures()
    check("shipped figures name the shipped weights", shipped_sha == BASE_WEIGHT_SHA256[SHIPPED_SEED])
    check("shipped figures: 200 or more", len(figures) >= 200 and len({p for p, _, _ in figures}) == len(figures), str(len(figures)))
    fig = {p: v for p, v, _ in figures}

    def f(path):
        return fig[path]

    check("shipped figures: pooledTechnical is the sum of S1, S3 and S4",
          all(f("shapesTechnical.pooledTechnical.%s" % k) == sum(f(tranche_path(t, k)) for t in TECH_TRANCHES)
              for k in ("targetItems", "targetCorrect", "decisions", "harmful"))
          and f("shapesTechnical.pooledTechnical.confidentWrongTotal") == sum(f(tranche_path(t, "confidentWrong")) for t in TECH_TRANCHES)
          and f("shapesTechnical.pooledTechnical.wrong90Total") == sum(f(tranche_path(t, "wrong90")) for t in TECH_TRANCHES))
    check("shipped figures: pooledPrimary is the sum of the four tranches",
          all(f("shapesFollowup.pooledPrimary.%s" % k) == sum(f(tranche_path(t, k)) for t in OLD_TRANCHES)
              for k in ("targetItems", "targetCorrect", "decisions", "harmful"))
          and f("shapesFollowup.pooledPrimary.confidentWrongTotal") == sum(f(tranche_path(t, "confidentWrong")) for t in OLD_TRANCHES))
    check("shipped figures: the rates follow from the counts",
          round(rate(f("shapesTechnical.pooledTechnical.harmful"), f("shapesTechnical.pooledTechnical.decisions")), 3)
          == f("shapesTechnical.pooledTechnical.harmfulPer1k")
          and round(rate(f("shapesFollowup.pooledPrimary.harmful"), f("shapesFollowup.pooledPrimary.decisions")), 3) == f(OLD_POOL))
    shipped_primary = rate(f("shapesTechnical.pooledTechnical.harmful") + f("shapesFollowup.pooledPrimary.harmful"),
                           f("shapesTechnical.pooledTechnical.decisions") + f("shapesFollowup.pooledPrimary.decisions"))
    check("shipped figures: the shipped primary is 289 of 1,287", f("shapesTechnical.pooledTechnical.harmful") + f("shapesFollowup.pooledPrimary.harmful") == 289
          and f("shapesTechnical.pooledTechnical.decisions") + f("shapesFollowup.pooledPrimary.decisions") == 1287 and round(shipped_primary, 2) == 224.55,
          str(shipped_primary))
    check("shipped figures: S2 is 66 of 215", f("shapesTechnical.termError.targetCorrect") == 66 and f("shapesTechnical.termError.targetItems") == 215
          and f("shapesTechnical.termError.targetItems") == f(tranche_path("definedTerms", "targetItems"))
          and f("shapesTechnical.termError.targetCorrect") == f(tranche_path("definedTerms", "targetCorrect")))
    cal = "shapesTechnical.calibration.technical."
    check("shipped figures: calibration 733/86 at 0.90 and 595/34 at 0.95 (117.3 and 57.1 per 1,000), 1,417 judged",
          (f(cal + "thresholds.0.answers"), f(cal + "thresholds.0.wrong"), f(cal + "thresholds.1.answers"), f(cal + "thresholds.1.wrong")) == (733, 86, 595, 34)
          and round(f(cal + "thresholds.0.errorPer1k"), 1) == 117.3 and round(f(cal + "thresholds.1.errorPer1k"), 1) == 57.1 and f(cal + "judged") == 1417)
    check("shipped figures: the curve rows at 0.90 and 0.95 are the threshold rows",
          f(cal + "curve.9.answers") == 733 and f(cal + "curve.9.wrong") == 86 and f(cal + "curve.10.answers") == 595 and f(cal + "curve.10.wrong") == 34)
    check("shipped figures: the judged words are the targets and controls of the four tranches",
          f(cal + "judged") == sum(f(tranche_path(t, "targetItems")) + f(tranche_path(t, "controlItems")) for t in ALL_TECH_TRANCHES)
          and sum(f("shapesTechnical.calibration.tranches.%s.judged" % t) for t in ALL_TECH_TRANCHES) == f(cal + "judged"))
    check("shipped figures: the curve answers fall with the threshold and are bounded by the judged words",
          all(f(cal + "curve.%d.answers" % i) >= f(cal + "curve.%d.answers" % (i + 1)) for i in range(14)) and f(cal + "curve.0.answers") <= f(cal + "judged"))
    nv = "shapesTechnical.calibration.nounVerbPooled."
    check("shipped figures: the noun-verb pool is S1+S3+S4 (882 targets, 817 decisions) and its harmful errors at 0.45 are the 197 of the pool",
          f(nv + "judged") == f("shapesTechnical.pooledTechnical.targetItems") and f(nv + "decisions") == f("shapesTechnical.pooledTechnical.decisions")
          and f(nv + "curve.0.harmful") == f("shapesTechnical.pooledTechnical.harmful") and f(nv + "curve.0.nounVerbAnswers") <= f(nv + "decisions"))
    check("shipped figures: 320 technical controls and 70 proper-noun controls",
          sum(f(tranche_path(t, "controlItems")) for t in ALL_TECH_TRANCHES) == 320 and f("shapesTechnical.definedTerms.kinds.proper.controlItems") == 70)

    with tempfile.TemporaryDirectory() as tmp:
        shipped_file = os.path.join(tmp, "shipped-weights.bin")
        with open(shipped_file, "wb") as handle:
            handle.write(b"shipped")

        # the real figures are satisfied by a run that has those values and fail one by one when a value is changed
        run_with_figures = build_nested(figures)
        ok = figure_results(run_with_figures, figures)
        check("real shipped figures are checked (%d)" % len(figures), len(ok) == len(figures) and all(r[2] for r in ok),
              str([r[0] for r in ok if not r[2]][:3]))
        for path, value, decimals in figures:
            broken = json.loads(json.dumps(run_with_figures))
            cur = broken
            parts = path.split(".")
            for part in parts[:-1]:
                cur = cur[int(part)] if part.isdigit() else cur[part]
            cur[int(parts[-1]) if parts[-1].isdigit() else parts[-1]] = value + (1 if decimals == 0 else 0.01)
            bad = [r[0] for r in figure_results(broken, figures) if not r[2]]
            if bad != [path]:
                check("real shipped figure %s is enforced" % path, False, str(bad[:2]))
        # the acceptance test reports each figure by name for the BASE run of the shipped seed and for no other run
        runs_for_figures = {a["id"]: [json.loads(json.dumps(run_with_figures)) for _ in seeds] for a in ARMS}
        runs_for_figures[BASE_ID][seeds.index(SHIPPED_SEED)]["weights"]["sha256"] = hashlib.sha256(b"shipped").hexdigest()
        named = [c for c in acceptance(runs_for_figures, tmp, seeds, shipped_file, (hashlib.sha256(b"shipped").hexdigest(), figures), base_hashes={})
                 if c["name"].startswith("a: BASE seed") and " = " in c["name"]]
        check("acceptance a names every figure (%d)" % len(figures), len(named) == len(figures) and all(c["ok"] for c in named), str(len(named)))

    B = {k: m for k, (m, _) in BASE_SPEC.items()}

    def P(x, sd=4.0):
        return {"rate.s1": (x, sd), "rate.s3": (x, sd), "rate.s4": (x, sd), "rate.old": (x, sd)}

    def S(x, sd=1.5):
        return {"s2": (x, sd)}

    def good(primary, s2=40.0, extra=None):
        values = dict(P(primary), **S(s2))
        values.update(extra or {})
        return values

    def all_ok(overrides, welch=None):
        return decide(synthetic_runs(overrides), welch)

    # R0: the synthetic runs are valid and have the intended exact means
    probe = all_ok({"TECH": good(150.0)})
    check("R0 primary mean of BASE and TECH", abs(probe["arms"]["BASE"]["primaryMean"] - 225.0) < 1e-9 and abs(probe["arms"]["TECH"]["primaryMean"] - 150.0) < 1e-9
          and abs(probe["arms"]["BASE"]["primarySd"] - 6.0) < 1e-9, str(probe["arms"]["BASE"]["primaryMean"]))
    check("R0 S2 and calibration of BASE", abs(probe["arms"]["BASE"]["s2Mean"] - 30.7) < 1e-9 and abs(probe["arms"]["BASE"]["cal90Mean"] - 117.3) < 1e-9
          and abs(probe["arms"]["BASE"]["cal95Mean"] - 57.1) < 1e-9)
    # the primary pools the three technical tranches and the four old ones by their decisions (360, 155, 302 and 470)
    unequal = all_ok({"TECH": {"rate.s1": (100.0, 4.0), "rate.s3": (200.0, 4.0), "rate.s4": (300.0, 4.0), "rate.old": (250.0, 4.0)}})
    want = (360 * 100.0 + 155 * 200.0 + 302 * 300.0 + 470 * 250.0) / 1287.0
    check("R0 the primary is the pool of the seven tranches", abs(unequal["arms"]["TECH"]["primaryMean"] - want) < 1e-9, "%r, expected %r" % (unequal["arms"]["TECH"]["primaryMean"], want))
    check("R0 the rates of the three technical tranches and of the old pool are derived from their own counts",
          abs(statistics.fmean([r["derived"]["s1HarmfulPer1k"] for r in synthetic_runs({"TECH": {"rate.s1": (100.0, 4.0), "rate.s3": (200.0, 4.0), "rate.s4": (300.0, 4.0), "rate.old": (250.0, 4.0)}})["TECH"]])
              - 100.0) < 1e-9)

    # R1: Q needs the primary below 0 AND the S2 above 0; the winner has the fewest weighted added sentences within the window.
    d = all_ok({"TECH": good(150.0), "TECH-ND": good(160.0), "TECH-T2": good(140.0), "TECH-W2": good(135.0)})
    check("R1 qualifying", d["qualifying"] == CANDIDATES, str(d["qualifying"]))
    check("R1 window", d["withinTenPercent"] == ["TECH-T2", "TECH-W2"] and d["lowestPrimary"]["arm"] == "TECH-W2", str(d["withinTenPercent"]))
    check("R1 fewest weighted added sentences wins", d["winner"] == "TECH-T2" and d["wins"] and d["candidate"] == "TECH-T2", str(d["winner"]))
    d = all_ok({"TECH": good(112.0), "TECH-ND": good(110.0), "TECH-T2": good(105.0), "TECH-W2": good(100.0)})
    check("R2 boundary inside (110 = 1.10 x 100)", d["withinTenPercent"] == ["TECH-ND", "TECH-T2", "TECH-W2"] and d["winner"] == "TECH-ND", str(d["withinTenPercent"]))
    d = all_ok({"TECH": good(112.0), "TECH-ND": good(110.0 + 5e-10), "TECH-T2": good(105.0), "TECH-W2": good(100.0)})
    check("R2 the window has the tolerance of 1e-9 (110 + 5e-10 is inside)", d["withinTenPercent"] == ["TECH-ND", "TECH-T2", "TECH-W2"], str(d["withinTenPercent"]))
    d = all_ok({"TECH": good(112.0), "TECH-ND": good(110.0 + 5e-9), "TECH-T2": good(105.0), "TECH-W2": good(100.0)})
    check("R2 beyond the tolerance (110 + 5e-9 is outside)", d["withinTenPercent"] == ["TECH-T2", "TECH-W2"], str(d["withinTenPercent"]))
    d = all_ok({"TECH": good(112.0), "TECH-ND": good(110.5), "TECH-T2": good(105.0), "TECH-W2": good(100.0)})
    check("R2 outside the window", d["withinTenPercent"] == ["TECH-T2", "TECH-W2"] and d["winner"] == "TECH-T2", str(d["withinTenPercent"]))
    d = all_ok({"TECH": good(130.0), "TECH-ND": good(125.0), "TECH-T2": good(120.0), "TECH-W2": good(100.0)})
    check("R2 all but the lowest outside", d["withinTenPercent"] == ["TECH-W2"] and d["winner"] == "TECH-W2")
    d = all_ok({"TECH": good(100.0), "TECH-ND": good(105.0)})
    check("R3 TECH-ND has fewer sentences than TECH", d["winner"] == "TECH-ND" and d["withinTenPercent"] == ["TECH", "TECH-ND"])
    d = all_ok({"TECH": good(105.0), "TECH-W2": good(100.0)})
    check("R3 TECH has fewer sentences than TECH-W2", d["winner"] == "TECH")
    arms_pick = {"TECH": {"added": 5, "primaryMean": 5.0}, "TECH-ND": {"added": 5, "primaryMean": 4.0}, "TECH-T2": {"added": 7, "primaryMean": 1.0}}
    check("R3 tie of sentences: the lowest primary", pick_winner(["TECH", "TECH-ND", "TECH-T2"], arms_pick) == "TECH-ND")
    arms_pick = {"TECH": {"added": 5, "primaryMean": 4.0}, "TECH-ND": {"added": 5, "primaryMean": 4.0}}
    check("R3 equal sentences and primary: the order of arms.json", pick_winner(["TECH-ND", "TECH"], arms_pick) == "TECH")
    arms_pick = {"TECH": {"added": 9, "primaryMean": 1.0}, "TECH-ND": {"added": 1, "primaryMean": 9.0}}
    check("R3 fewer sentences beat a lower primary", pick_winner(["TECH", "TECH-ND"], arms_pick) == "TECH-ND")

    # R4: TECH-NT is attribution only: it never enters Q, never sets m, never wins; it is still reported.
    d = all_ok({"TECH-NT": good(100.0), "TECH": good(150.0)})
    check("R4 attribution arm is not in Q and does not set m", "TECH-NT" not in d["qualifying"] and d["lowestPrimary"]["arm"] == "TECH"
          and d["winner"] == "TECH" and d["arms"]["TECH-NT"]["passesAllGuards"] and d["arms"]["TECH-NT"]["primaryBelowZero"]
          and d["arms"]["TECH-NT"]["s2AboveZero"] and not d["arms"]["TECH-NT"]["inQ"], str(d["qualifying"]))
    d = all_ok({"TECH-NT": good(100.0)})
    check("R4 attribution arm alone: BASE stays", d["qualifying"] == [] and d["winner"] == "BASE" and not d["wins"])

    # R5: the S2 requirement: a low primary without an S2 gain is not in Q; an S2 gain without a primary gain neither.
    d = all_ok({"TECH": dict(P(100.0), **S(30.7, 1.5)), "TECH-ND": good(150.0)})
    check("R5 no S2 gain: not in Q", d["arms"]["TECH"]["primaryBelowZero"] and not d["arms"]["TECH"]["s2AboveZero"] and "TECH" not in d["qualifying"]
          and d["winner"] == "TECH-ND", str(d["arms"]["TECH"]["diffS2"]))
    d = all_ok({"TECH": dict(P(225.0, 40.0), **S(45.0))})
    check("R5 no primary gain: not in Q", d["arms"]["TECH"]["s2AboveZero"] and not d["arms"]["TECH"]["primaryBelowZero"] and d["qualifying"] == [])
    d = all_ok({"TECH": dict(P(150.0), **S(25.0))})
    check("R5 S2 loss: not in Q", not d["arms"]["TECH"]["s2AboveZero"] and d["qualifying"] == [])

    # R6: each guard passes exactly at its limit and fails just beyond it (the other guards stay satisfied).
    def flags(arm_overrides):
        dec = all_ok({"TECH": dict(good(150.0), **arm_overrides)})
        return {g["name"]: g["pass"] for g in dec["arms"]["TECH"]["guards"]}

    cases = [
        ("hard-harmful", "shapes.hard.pooled.harmfulPer1k", (B["shapes.hard.pooled.harmfulPer1k"] + 22.0, 8.0), (B["shapes.hard.pooled.harmfulPer1k"] + 22.2, 8.0)),
        ("standard-harmful", "shapes.standard.pooled.harmfulPer1k", (B["shapes.standard.pooled.harmfulPer1k"] + 5.0, 1.0),
         (B["shapes.standard.pooled.harmfulPer1k"] + 5.2, 1.0)),
        ("open-harmful", "openDev.harmfulPer1k", (B["openDev.harmfulPer1k"] + 1.5, 0.5), (B["openDev.harmfulPer1k"] + 1.6, 0.5)),
        ("open-accuracy", "openDev.accuracy", (B["openDev.accuracy"] - 0.10, 0.04), (B["openDev.accuracy"] - 0.11, 0.04)),
        ("gold-harmful", "goldDev.harmfulPer1k", (B["goldDev.harmfulPer1k"] + 4.5, 1.5), (B["goldDev.harmfulPer1k"] + 4.6, 1.5)),
        ("gold-accuracy", "goldDev.accuracy", (B["goldDev.accuracy"] - 0.25, 0.14), (B["goldDev.accuracy"] - 0.26, 0.14)),
        ("ud-harmful", "udDev.harmfulPer1k", (B["udDev.harmfulPer1k"] + 1.5, 0.4), (B["udDev.harmfulPer1k"] + 1.6, 0.4)),
        ("ud-accuracy", "udDev.accuracy", (B["udDev.accuracy"] - 0.15, 0.03), (B["udDev.accuracy"] - 0.16, 0.03)),
        ("named-state", "shapesNew.namedState.shape.targetAccuracy", (B["shapesNew.namedState.shape.targetAccuracy"] - 3.0, 1.2),
         (B["shapesNew.namedState.shape.targetAccuracy"] - 3.2, 1.2)),
        ("imperative-harmful", IMPERATIVE, (B[IMPERATIVE] + 25.0, 8.0), (B[IMPERATIVE] + 25.2, 8.0)),
        ("size", "weights.bytesDeflated", (1.5 * B["weights.bytesDeflated"], 200), (1.5 * B["weights.bytesDeflated"] + 1.0, 200)),
        ("throughput", "throughput.tokensPerSecond", (150000.0, 10000.0), (149990.0, 10000.0)),
        ("tech-controls", "techctrl", (B["techctrl"] - 3.0, 1.0), (B["techctrl"] - 3.2, 1.0)),
        ("s2-proper-control", "s2proper", (B["s2proper"] - 5.0, 1.5), (B["s2proper"] - 5.2, 1.5)),
        ("cal90", "cal90", (B["cal90"], 6.0), (B["cal90"] + 0.2, 6.0)),
        ("cal95", "cal95", (B["cal95"], 5.0), (B["cal95"] + 0.2, 5.0)),
    ]
    for name, path, good_value, bad_value in cases:
        flags_good, flags_bad = flags({path: good_value}), flags({path: bad_value})
        check("R6 %s passes at the limit" % name, flags_good[name] and all(flags_good.values()), str(flags_good))
        check("R6 %s fails beyond the limit" % name, not flags_bad[name] and all(v for k, v in flags_bad.items() if k != name), str(flags_bad))
    # the four guards on harmful rates of tranche groups: the rate of one group moves, the others stay at the primary
    for guard, key, limit in (("old-pool", "old", 25.0), ("s1-harmful", "s1", 25.0), ("s3-harmful", "s3", 35.0), ("s4-harmful", "s4", 25.0)):
        at = flags({"rate." + key: (225.0 + limit, 6.0)})
        beyond = flags({"rate." + key: (225.0 + limit + 0.2, 6.0)})
        check("R6 %s passes at the limit" % guard, at[guard] and all(at.values()), str(at))
        check("R6 %s fails beyond the limit" % guard, not beyond[guard] and all(v for k, v in beyond.items() if k != guard), str(beyond))
    check("R6 covers every guard", sorted([c[0] for c in cases] + ["old-pool", "s1-harmful", "s3-harmful", "s4-harmful"]) == sorted(g[0] for g in GUARDS))
    check("R6 size 1.4x passes, 1.6x fails", flags({"weights.bytesDeflated": (1.4 * B["weights.bytesDeflated"], 200)})["size"]
          and not flags({"weights.bytesDeflated": (1.6 * B["weights.bytesDeflated"], 200)})["size"])
    check("R6 throughput below the floor fails", not flags({"throughput.tokensPerSecond": (100000.0, 5000.0)})["throughput"])
    check("R6 calibration: lower than BASE is allowed", flags({"cal90": (B["cal90"] - 5.0, 6.0), "cal95": (B["cal95"] - 5.0, 5.0)})["cal90"])
    dec = all_ok({"BASE": {"weights.bytesDeflated": (400000.0, 200)}, "TECH": dict(good(150.0), **{"weights.bytesDeflated": (600000.0, 200)})})
    check("R6 size against the BASE mean (1.5 x 400000 = 600000)", dec["arms"]["TECH"]["passesAllGuards"])
    dec = all_ok({"BASE": {"cal90": (100.0, 6.0)}, "TECH": dict(good(150.0), **{"cal90": (100.0, 6.0)})})
    check("R6 calibration against the BASE mean", dec["arms"]["TECH"]["passesAllGuards"])

    # R7: an arm that fails a guard never enters Q, even with the lowest primary; an arm whose interval is not below 0 never sets m.
    d = all_ok({"TECH-W2": dict(good(100.0), **{"cal95": (B["cal95"] + 1.0, 5.0)}), "TECH": good(150.0)})
    check("R7 calibration failure excludes the best arm", "TECH-W2" not in d["qualifying"] and d["lowestPrimary"]["arm"] == "TECH" and d["winner"] == "TECH", str(d["qualifying"]))
    d = all_ok({"TECH-W2": dict(good(100.0), **{"openDev.accuracy": (B["openDev.accuracy"] - 0.2, 0.04)}), "TECH": good(150.0)})
    check("R7 open accuracy excludes the best arm", "TECH-W2" not in d["qualifying"] and d["winner"] == "TECH")
    d = all_ok({"TECH-W2": dict(good(100.0), **{"s2proper": (B["s2proper"] - 8.0, 1.5)}), "TECH": good(150.0)})
    check("R7 proper-control failure excludes the best arm", "TECH-W2" not in d["qualifying"] and d["winner"] == "TECH")
    d = all_ok({"TECH-W2": dict(P(150.0, 150.0), **S(45.0)), "TECH": good(200.0)})
    check("R7 an arm with an interval containing 0 does not set m", d["arms"]["TECH-W2"]["passesAllGuards"] and not d["arms"]["TECH-W2"]["primaryBelowZero"]
          and d["qualifying"] == ["TECH"] and d["lowestPrimary"]["arm"] == "TECH" and d["winner"] == "TECH")
    d = all_ok({"TECH-W2": dict(P(150.0, 150.0), **S(45.0)), "TECH": good(200.0), "TECH-ND": good(215.0)})
    check("R7 window is taken over Q only", d["withinTenPercent"] == ["TECH", "TECH-ND"] and d["winner"] == "TECH-ND", str(d["withinTenPercent"]))

    # R8: nothing qualifies: BASE stays, no winner.
    bad = {"udDev.harmfulPer1k": (B["udDev.harmfulPer1k"] + 3.0, 0.4)}
    d = all_ok({a: dict(good(100.0), **bad) for a in CANDIDATES})
    check("R8 nothing qualifies (a guard fails everywhere)", d["qualifying"] == [] and d["candidate"] is None and not d["wins"] and d["winner"] == "BASE"
          and d["passingGuards"] == [], str(d["qualifying"]))
    d = all_ok({})
    check("R8 BASE stays when every arm equals BASE", d["qualifying"] == [] and not d["wins"] and d["winner"] == "BASE" and d["passingGuards"] == CANDIDATES
          and "BASE stays" in d["reason"])
    d = all_ok({"TECH": good(260.0), "TECH-ND": good(280.0), "TECH-T2": good(290.0), "TECH-W2": good(300.0)})
    check("R8 worse than BASE", d["qualifying"] == [] and d["winner"] == "BASE" and d["arms"]["TECH"]["diffPrimary"]["hi"] > 0.0)
    d = all_ok({"TECH-T2": good(100.0)})
    check("R8 a single qualifying arm wins", d["qualifying"] == ["TECH-T2"] and d["winner"] == "TECH-T2" and d["wins"])

    # R9: the Welch rules are strict (an upper bound of exactly 0 on the primary and a lower bound of exactly 0 on S2 do not qualify).
    def stub(primary_interval, s2_interval):
        def welch(arm, base):
            lo_hi = primary_interval if statistics.fmean(base) > 100.0 else s2_interval
            return (0.0, lo_hi[0], lo_hi[1], 8.0, 0.1)
        return welch

    for name, pi, si, expect in (
            ("primary hi 0", (-2.0, 0.0), (1.0, 2.0), False), ("primary hi -1e-9", (-2.0, -1e-9), (1.0, 2.0), True),
            ("primary hi +1e-9", (-2.0, 1e-9), (1.0, 2.0), False), ("S2 lo 0", (-2.0, -1.0), (0.0, 2.0), False),
            ("S2 lo 1e-9", (-2.0, -1.0), (1e-9, 2.0), True), ("S2 lo -1e-9", (-2.0, -1.0), (-1e-9, 2.0), False)):
        d = all_ok({"TECH": good(150.0)}, welch=stub(pi, si))
        check("R9 %s" % name, ("TECH" in d["qualifying"]) == expect, str(d["qualifying"]))
    d = all_ok({"TECH": good(150.0)})
    check("R9 frozen welch by default", abs(d["arms"]["TECH"]["diffPrimary"]["mean"] - (150.0 - 225.0)) < 1e-9 and abs(d["arms"]["TECH"]["diffS2"]["mean"] - (40.0 - 30.7)) < 1e-9)
    check("R9 pairs reported", sorted(d["pairs"]) == ["TECH-ND - TECH", "TECH-NT - TECH", "TECH-T2 - TECH", "TECH-W2 - TECH"]
          and all(m in d["pairs"]["TECH-NT - TECH"] for m in PAIR_METRICS))

    # A1: files, acceptance tests, the report and the command line; missing runs stop before any selection.
    shipped_bytes = b"shipped weights bytes"
    overrides = {"TECH": good(150.0), "TECH-NT": good(170.0), "TECH-ND": good(152.0), "TECH-T2": good(145.0), "TECH-W2": good(140.0)}
    syn = synthetic_runs(overrides)
    synth_shipped = synthetic_shipped(syn[BASE_ID][seeds.index(SHIPPED_SEED)], shipped_bytes)
    hashes = synthetic_base_hashes(shipped_bytes)

    def run_analysis(directory_, shipped_, shipped_arg=synth_shipped, hashes_arg=hashes, added=None):
        return analyze(directory_, shipped_, verify=False, shipped=shipped_arg, added=added, base_hashes=hashes_arg)

    with tempfile.TemporaryDirectory() as tmp:
        directory, shipped = write_synthetic(tmp, syn, shipped_bytes)
        runs, checks, decision, _ = run_analysis(directory, shipped)
        bad_checks = [c for c in checks if not c["ok"]]
        check("A1 acceptance passes", not bad_checks, str(bad_checks[:3]))
        check("A1 acceptance covers 6 arms x 8 seeds and the 8 BASE hashes", len([c for c in checks if c["name"].startswith("b: ")]) == 48
              and len([c for c in checks if c["name"].startswith("a2: ")]) == 8, str(len(checks)))
        check("A1 decision", decision is not None and decision["winner"] == "TECH-ND" and decision["wins"], str(decision and decision["winner"]))
        report = build_report(runs, decision, checks, directory, seeds)
        check("A1 report", "| TECH-W2 |" in report and "Decision: TECH-ND WINS" in report and "## Selection table (rule T)" in report
              and "## Acceptance tests" in report and "## Pairwise Welch" in report and "tokens/s >= 150,000" in report
              and "wrong per 1k at >= 0.95 <= BASE" in report and "S2 interval above 0" in report, report[:400])
        out, dec_path = os.path.join(tmp, "report.md"), os.path.join(tmp, "decision.json")
        stdout, sys.stdout = sys.stdout, open(os.devnull, "w", encoding="utf-8")
        try:
            code = main(["--runs", directory, "--shipped-weights", shipped, "--out", out, "--decision", dec_path],
                        _test={"verify": False, "shipped": synth_shipped, "hashes": hashes})
        finally:
            sys.stdout.close()
            sys.stdout = stdout
        check("A1 main exit", code == 0, str(code))
        check("A1 outputs", os.path.isfile(out) and os.path.isfile(dec_path) and json.load(open(dec_path, encoding="utf-8"))["winner"] == "TECH-ND")
        os.remove(run_stem(directory, "TECH-W2", 15) + ".json")
        try:
            run_analysis(directory, shipped)
            check("A1 missing run", False, "no error raised")
        except RunsError as e:
            check("A1 missing run", "TECH-W2-s15.json" in str(e), str(e))

    # A2: each acceptance test fails when its condition is broken, and no decision is made.
    def acceptance_case(name, tweak=None, more=None, shipped_check=shipped_bytes, hashes_arg=hashes):
        with tempfile.TemporaryDirectory() as tmp:
            directory_, shipped_ = write_synthetic(tmp, synthetic_runs(overrides), shipped_bytes, tweak, more)
            with open(shipped_, "wb") as handle:
                handle.write(shipped_check)
            _, checks_, decision_, _ = run_analysis(directory_, shipped_, hashes_arg=hashes_arg)
            failed = [c["name"] for c in checks_ if not c["ok"]]
            check("A2 %s fails" % name, decision_ is None and len(failed) > 0, str(failed))
            return failed

    failed = acceptance_case("a: shipped weights differ", shipped_check=b"other bytes")
    check("A2 a names the hash", any(n.startswith("a: BASE seed 20260101 weights SHA-256") for n in failed), str(failed))
    failed = acceptance_case("a: figure differs", tweak=lambda l, s, r, h: set_path(r, "openDev.harmfulPer1k", 22.5) if (l, s) == ("BASE", SHIPPED_SEED) else None)
    check("A2 a names the figure", any("openDev.harmfulPer1k" in n for n in failed), str(failed))
    failed = acceptance_case("a2: one BASE seed differs", hashes_arg={**hashes, 13: "0" * 64})
    check("A2 a2 names the seed", failed == ["a2: BASE seed 13 weights SHA-256 equals the CMP2 weights of the follow-up"], str(failed))
    failed = acceptance_case("b: terms adds 372", more={"terms": 372})
    check("A2 b terms count", all(any(n.startswith("b: %s seed" % a) for n in failed) for a in ("TECH", "TECH-ND", "TECH-T2", "TECH-W2"))
          and not any(n.startswith("b: BASE") or n.startswith("b: TECH-NT") for n in failed), str(failed[:3]))
    failed = acceptance_case("b: deverbal adds 636", more={"deverbal": 636})
    check("A2 b deverbal count", any(n.startswith("b: TECH-NT seed") for n in failed) and not any(n.startswith("b: TECH-ND seed") for n in failed), str(failed[:3]))
    failed = acceptance_case("b: the dose of terms is not written in the log",
                             tweak=lambda l, s, r, h: h.__setitem__(0, h[0].replace("terms:2", "terms:1")) if (l, s) == ("TECH-T2", 13) else None)
    check("A2 b dose", any(n.startswith("b: TECH-T2 seed 13") for n in failed), str(failed))
    failed = acceptance_case("b: BASE lists a technical source",
                             tweak=lambda l, s, r, h: h.__setitem__(0, h[0].replace("compound2:1", "compound2:1,steps:1")) if (l, s) == ("BASE", 14) else None)
    check("A2 b BASE lists the source", any(n.startswith("b: BASE seed 14") for n in failed), str(failed))
    failed = acceptance_case("b: the mix differs (compound2 missing)",
                             tweak=lambda l, s, r, h: h.__setitem__(0, h[0].replace(",compound2:1", "", 1)) if (l, s) == ("TECH", 11) else None)
    check("A2 b mix", any(n.startswith("b: TECH seed 11") for n in failed), str(failed))
    failed = acceptance_case("b: BASE trained another sentence count",
                             tweak=lambda l, s, r, h: h.__setitem__(0, h[0].replace("63,522", "63,521")) if (l, s) == ("BASE", 12) else None)
    check("A2 b BASE count", any(n.startswith("b: BASE seed 12") for n in failed), str(failed))
    failed = acceptance_case("c: one run has another input hash",
                             tweak=lambda l, s, r, h: r["inputs"].__setitem__("stepLinesSha256", "f" * 64) if (l, s) == ("TECH-ND", 14) else None)
    check("A2 c names the input", any(n.startswith("c: inputs.stepLinesSha256") for n in failed), str(failed))
    failed = acceptance_case("c: every run has the same but not the frozen hash",
                             tweak=lambda l, s, r, h: r["inputs"].__setitem__("definedTermsSha256", "e" * 64))
    check("A2 c frozen value", any(n.startswith("c: inputs.definedTermsSha256") for n in failed), str(failed))
    failed = acceptance_case("c: an input hash is absent", tweak=lambda l, s, r, h: r["inputs"].pop("deverbalHeadsSha256") if (l, s) == ("BASE", 11) else None)
    check("A2 c absent", any(n.startswith("c: inputs.deverbalHeadsSha256") for n in failed), str(failed))
    failed = acceptance_case("c: a run was made by another commit",
                             tweak=lambda l, s, r, h: r["inputs"].__setitem__("commit", "fedcba") if (l, s) == ("TECH", 16) else None)
    check("A2 c commit", "c: all runs were made by one trainer commit" in failed, str(failed))
    failed = acceptance_case("c: a run was made from a dirty tree",
                             tweak=lambda l, s, r, h: r["inputs"].__setitem__("dirty", True) if (l, s) == ("TECH-W2", 17) else None)
    check("A2 c dirty", "c: no run was made from a dirty working tree" in failed, str(failed))
    def break_curve(label, seed, run, holder):
        if (label, seed) == ("BASE", SHIPPED_SEED):
            run["shapesTechnical"]["calibration"]["technical"]["curve"][3]["answers"] = 999

    failed = acceptance_case("a: a figure of the calibration curve differs", tweak=break_curve)
    check("A2 a names the curve figure", any("curve.3.answers" in n for n in failed), str(failed))

    # A3: invalid runs raise before any selection.
    def invalid_case(name, tweak, needle):
        with tempfile.TemporaryDirectory() as tmp:
            directory_, shipped_ = write_synthetic(tmp, synthetic_runs(overrides), shipped_bytes, tweak)
            try:
                run_analysis(directory_, shipped_)
                check("A3 %s" % name, False, "no error raised")
            except RunsError as e:
                check("A3 %s" % name, needle in str(e), str(e))

    invalid_case("groups lack clauseCues", lambda l, s, r, h: r.__setitem__("groups", []) if (l, s) == ("TECH", 11) else None, "TECH-s11.json: groups")
    invalid_case("groups name titleCaseLine", lambda l, s, r, h: r.__setitem__("groups", ["clauseCues", "titleCaseLine"]) if (l, s) == ("BASE", 12) else None, "BASE-s12.json: groups")
    invalid_case("pooledTechnical is not the sum of its tranches",
                 lambda l, s, r, h: set_path(r, "shapesTechnical.pooledTechnical.harmful", 5.0) if (l, s) == ("TECH", 13) else None, "TECH-s13.json: pooledTechnical decisions/harmful")
    invalid_case("pooledPrimary is not the sum of its tranches",
                 lambda l, s, r, h: set_path(r, "shapesFollowup.pooledPrimary.decisions", 471) if (l, s) == ("TECH-NT", 14) else None, "TECH-NT-s14.json: pooledPrimary decisions/harmful")
    invalid_case("the old pool rate does not match the counts", lambda l, s, r, h: set_path(r, OLD_POOL, 1.0) if (l, s) == ("TECH-ND", 15) else None,
                 "old pool rate")
    invalid_case("termError is not the defined-terms row",
                 lambda l, s, r, h: set_path(r, "shapesTechnical.termError.targetCorrect", 100) if (l, s) == ("TECH-T2", 16) else None, "termError")
    invalid_case("the calibration rate does not match its counts",
                 lambda l, s, r, h: set_path(r, "shapesTechnical.calibration.technical.thresholds.0.errorPer1k", 1.0) if (l, s) == ("TECH-W2", 17) else None,
                 "calibration errorPer1k at 0.90")
    invalid_case("the calibration threshold is not 0.95",
                 lambda l, s, r, h: set_path(r, "shapesTechnical.calibration.technical.thresholds.1.threshold", 0.96) if (l, s) == ("TECH", 12) else None,
                 "calibration threshold 1 is")
    invalid_case("the curve row at 0.90 differs from the threshold row",
                 lambda l, s, r, h: set_path(r, "shapesTechnical.calibration.technical.curve.9.wrong", 1) if (l, s) == ("TECH", 14) else None,
                 "the curve row at 0.90 differs")
    invalid_case("the curve is absent", lambda l, s, r, h: r["shapesTechnical"]["calibration"]["technical"].pop("curve") if (l, s) == ("BASE", 15) else None,
                 "required metric shapesTechnical.calibration.technical.curve.9.threshold is missing")
    invalid_case("a required metric is absent", lambda l, s, r, h: r["shapesNew"].pop("namedState") if (l, s) == ("TECH-ND", 20260101) else None,
                 "shapesNew.namedState.shape.targetAccuracy is missing")
    invalid_case("the technical block is absent", lambda l, s, r, h: r.pop("shapesTechnical") if (l, s) == ("TECH-W2", 11) else None,
                 "required metric shapesTechnical.pooledTechnical.decisions is missing")
    invalid_case("wrong seed field", lambda l, s, r, h: r.__setitem__("seed", 99) if (l, s) == ("BASE", 12) else None, "seed field")
    invalid_case("wrong sources field", lambda l, s, r, h: r.__setitem__("sources", {"terms": 1}) if (l, s) == ("TECH-NT", 11) else None, "sources are")
    invalid_case("wrong dose in the sources field", lambda l, s, r, h: r.__setitem__("sources", {"deverbal": 1, "terms": 1, "steps": 1, "clauses": 1})
                 if (l, s) == ("TECH-T2", 13) else None, "TECH-T2-s13.json: sources are")

    # A4: a missing training log fails the acceptance test (it is not skipped); an acceptance failure writes the report, no decision, exit 1;
    # no winner is exit 0 with a decision file that says BASE stays.
    with tempfile.TemporaryDirectory() as tmp:
        directory_, shipped_ = write_synthetic(tmp, synthetic_runs(overrides), shipped_bytes)
        os.remove(run_stem(directory_, "TECH", 11) + ".train.txt")
        _, checks_, decision_, _ = run_analysis(directory_, shipped_)
        check("A4 missing log", decision_ is None and any("TECH seed 11 training line" in c["name"] and not c["ok"] for c in checks_))
        out, dec_path = os.path.join(tmp, "r.md"), os.path.join(tmp, "d.json")
        stdout, stderr = sys.stdout, sys.stderr
        sys.stdout, sys.stderr = open(os.devnull, "w", encoding="utf-8"), open(os.devnull, "w", encoding="utf-8")
        try:
            code = main(["--runs", directory_, "--shipped-weights", shipped_, "--out", out, "--decision", dec_path],
                        _test={"verify": False, "shipped": synth_shipped, "hashes": hashes})
        finally:
            sys.stdout.close()
            sys.stderr.close()
            sys.stdout, sys.stderr = stdout, stderr
        check("A4 exit 1 without a decision", code == 1 and os.path.isfile(out) and not os.path.exists(dec_path), str(code))
    with tempfile.TemporaryDirectory() as tmp:
        directory_, shipped_ = write_synthetic(tmp, synthetic_runs({}), shipped_bytes)
        out, dec_path = os.path.join(tmp, "r.md"), os.path.join(tmp, "d.json")
        stdout, sys.stdout = sys.stdout, open(os.devnull, "w", encoding="utf-8")
        try:
            code = main(["--runs", directory_, "--shipped-weights", shipped_, "--out", out, "--decision", dec_path],
                        _test={"verify": False, "shipped": synth_shipped, "hashes": hashes})
        finally:
            sys.stdout.close()
            sys.stdout = stdout
        check("A4 no winner is exit 0 and BASE stays", code == 0 and json.load(open(dec_path, encoding="utf-8"))["winner"] == "BASE", str(code))
        check("A4 no-winner report", "Decision: BASE STAYS" in open(out, encoding="utf-8").read())

    # L: the leakage re-derivation finds a shared document id or an exact sentence and passes on disjoint files; the real files are disjoint.
    with tempfile.TemporaryDirectory() as tmp:
        def corpus(name, docs, texts):
            path = os.path.join(tmp, name)
            with open(path, "w", encoding="utf-8") as handle:
                for doc, text in zip(docs, texts):
                    handle.write("# sent_id = x\n# source_id = %s\n# text = %s\n\nform\tNOUN\tunanimous\n\n" % (doc, text))
            return name

        corpus("s1.tsv", ["d1", "d2"], ["one a", "two b"])
        corpus("v1.tsv", ["d3", "d4"], ["three c", "four d"])
        corpus("v2.tsv", ["d2", "d5"], ["five e", "six f"])
        corpus("v3.tsv", ["d6", "d7"], ["two b", "seven g"])
        p, n = leakage_problems(tmp, {"s": "s1.tsv"}, {"v": "v1.tsv"})
        check("L disjoint files pass", p == [] and n == 1, str(p))
        p, n = leakage_problems(tmp, {"s": "s1.tsv"}, {"v": "v2.tsv"})
        check("L a shared document id is found", len(p) == 1 and "1 document id(s) and 0 exact sentence(s)" in p[0], str(p))
        p, n = leakage_problems(tmp, {"s": "s1.tsv"}, {"v": "v3.tsv"})
        check("L a shared sentence is found", len(p) == 1 and "0 document id(s) and 1 exact sentence(s)" in p[0], str(p))
        p, n = leakage_problems(tmp, {"s": "s1.tsv"}, {"v": "missing.tsv"})
        check("L a missing file is a problem", len(p) == 1 and "cannot be read" in p[0], str(p))
    p, n = leakage_problems()
    check("L the six training sources and the nine development tranches are disjoint (%d pairs)" % (len(LEAK_SOURCES) * len(LEAK_DEV)),
          p == [] and n == 54, str(p))
    checks_l = acceptance({BASE_ID: [], **{a["id"]: [] for a in ARMS if a["id"] != BASE_ID}}, ".", [], DEFAULT_SHIPPED, shipped=("0", []), base_hashes={},
                          leakage=(["x and y share 1 document id(s) and 0 exact sentence(s)"], 54))
    check("L acceptance e fails on a problem", any(c["name"].startswith("e: ") and not c["ok"] for c in checks_l))

    print("selftest: %s" % ("ok" if not failures else "FAILED"))
    for line in failures:
        print("  - " + line)
    return 1 if failures else 0


def build_nested(figures):
    """Builds a nested dict/list structure that has every figure at its path (list members by index)."""
    root = {"weights": {}}
    for path, value, _ in figures:
        parts = path.split(".")
        cur = root
        for i, part in enumerate(parts):
            last = i == len(parts) - 1
            nxt = None if last else parts[i + 1]
            key = int(part) if isinstance(cur, list) else part
            if last:
                if isinstance(cur, list):
                    while len(cur) <= key:
                        cur.append(None)
                    cur[key] = value
                else:
                    cur[key] = value
                break
            child = [] if nxt.isdigit() else {}
            if isinstance(cur, list):
                while len(cur) <= key:
                    cur.append(None)
                if cur[key] is None:
                    cur[key] = child
                cur = cur[key]
            else:
                cur = cur.setdefault(key, child)
    return root


# ---------------------------------------------------------------------------------------------------------------------

def main(argv=None, _test=None):
    """The command line. `_test` ({"verify", "shipped", "hashes"}) is for the self-test only: it lets the self-test drive this function with
    synthetic runs (no frozen-file or leakage check, the figures and the BASE hashes of the synthetic runs); the command line has no such option."""
    ap = argparse.ArgumentParser(description="beta.3 phase 2 technical-mix experiment: acceptance tests, analysis and selection "
                                             "(standard library only).")
    ap.add_argument("--runs", help="the folder of the driver runs (all six arms)")
    ap.add_argument("--shipped-weights", default=DEFAULT_SHIPPED, help="the shipped weights.bin (default: the repository's)")
    ap.add_argument("--out", help="markdown report (also printed)")
    ap.add_argument("--decision", help="machine-readable decision")
    ap.add_argument("--selftest", action="store_true", help="run the self-test with synthetic runs and exit")
    args = ap.parse_args(argv)
    if hasattr(sys.stdout, "reconfigure"):
        sys.stdout.reconfigure(encoding="utf-8")
    if args.selftest:
        return selftest()
    if not args.runs:
        ap.error("--runs is required (or --selftest)")
    test = _test or {}
    try:
        runs, checks, decision, seeds = analyze(args.runs, args.shipped_weights, verify=test.get("verify", True), shipped=test.get("shipped"),
                                                base_hashes=test.get("hashes"))
    except RunsError as e:
        print("analyze_tech: cannot select: %s" % e, file=sys.stderr)
        return 1
    report = build_report(runs, decision, checks, args.runs, seeds)
    print(report)
    if args.out:
        with open(args.out, "w", encoding="utf-8", newline="\n") as f:
            f.write(report + "\n")
    if decision is None:
        print("analyze_tech: an acceptance test failed: no selection is made", file=sys.stderr)
        return 1
    if args.decision:
        with open(args.decision, "w", encoding="utf-8", newline="\n") as f:
            json.dump(decision, f, indent=2, sort_keys=True)
            f.write("\n")
    return 0


if __name__ == "__main__":
    sys.exit(main())

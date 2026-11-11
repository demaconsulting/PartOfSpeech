#!/usr/bin/env python3
"""Compound-head and heading follow-up experiment (PR-B): acceptance tests, analysis and selection (standard library only; pre-registered, frozen with the RUN-LOG).

usage:
    python analyze_followup.py --runs DIR [--shipped-weights FILE] [--out report.md] [--decision decision.json]
    python analyze_followup.py --selftest

`DIR` is the folder of the runs of the driver compound-heading-2/run_arms.ps1 (all five arms in one folder, eight seeds each). The driver
writes one result file per run (`<DIR>\\<arm>-s<seed>.json`, schema 1; the interface contract is in context-features/run_arms.ps1 plus the
members `shapesNew` and `shapesFollowup` of `eval-arm`) and the training log next to it (`<arm>-s<seed>.train.txt`). The arms (arms.json of
this folder):

    arm            feature groups              sources added to the shipped mix (open x2, MASC, silver, imperative; weight 1 each)
    BASE           clauseCues                  none (the shipped model; baseline)
    CMP2           clauseCues                  compound, compound2                                   (1 addition)
    CMP2-F5        clauseCues, titleCaseLine   compound, compound2                                   (2 additions)
    CMP2-HDG2      clauseCues                  compound, compound2, headings, headings2              (2 additions)
    CMP2-HDG2-F5   clauseCues, titleCaseLine   compound, compound2, headings, headings2              (3 additions)

The only thing taken from the frozen script of the context-feature experiment (context-features/analyze_arms.py) is `welch_diff` (the
Welch-Satterthwaite t interval, 95 percent); the file is loaded from its place by path, without editing it, and only after its SHA-256 has
been checked against the frozen value. Every other function of this script is its own. The self-test checks the frozen t quantile against
published values.

Acceptance tests (run before the rule is applied; a failure stops without a selection, exit code 1, no decision file):
  (a) BASE at seed 20260101 has weights byte-identical to the shipped weights.bin (the weights SHA-256 of its result file, and its .bin file
      when present) and the figures of the shipped model on every development measure (the figures of the older experiment and the figures of
      the shipped model on the two new tranches, measured before any arm was trained, RUN-LOG section "Follow-up: baseline of the shipped
      model on the new tranches");
  (b) from the line `Training on N sentences (...), sources=...` of every `<arm>-s<seed>.train.txt`: BASE trains on the shipped mix
      (open:2, masc:1, silver:1, imperative:1) and no new source; every other arm lists exactly the mix plus its own sources at weight 1 and
      trained on exactly the sentences of those sources more than the BASE run of the same seed; the result of every run names the feature
      groups of its arm (the `groups` of the weights header, the driver's `features`, and `sources`);
  (c) the `inputs.*Sha256` fields of all runs agree and equal the frozen hashes of the ten development files;
  (d) the files named in FROZEN_FILES (the driver, arms.json, the frozen analyzer, the runner, the four training sources and their source
      tables, the two source tables of the new development tranches) and the development files of data/eval/shapes-dev have their frozen
      SHA-256; and the files of the state before any adoption (training.json, the shipped weights.bin) too.

Metrics (per run, from the `eval-arm` JSON; a harmful error is a swapped noun/verb class at confidence >= 0.45 on a noun-verb decision):
  PRIMARY      = shapesFollowup.pooledPrimary.harmfulPer1k = 1000 x harmful / decisions summed over the four development tranches
                 (compound-head, compound-head-2, headings, headings-2); the script re-derives it from the counts of the four tranches and
                 checks that it is equal (a mismatch is an invalid run);
  CO-PRIMARY   = wrong at confidence >= 0.95 summed over the four tranches (the four `shape.confidentWrong`; the script also checks
                 `shapesFollowup.pooledPrimary.confidentWrongTotal`); the arm's mean over the seeds must be <= the BASE mean;
  IMPERATIVE   = shapesFollowup.imperativeHeadings.harmfulPer1k (kind `imperative` of `headings` and `headings-2`, pooled), re-derived from
                 the counts of the two kinds (a mismatch is an invalid run).

Selection rule B (the literal pre-registered text; nothing is tuned afterwards):

1. Primary = the mean over the eight seeds of the pooled harmful per 1k. Lower is better.
2. Guards, each arm against the BASE mean over the seeds (the throughput guard is absolute; it uses the median over the seeds of each run's
   median): the co-primary (wrong at >= 0.95, four tranches) <= BASE; hard tranche harmful per 1k <= BASE + 10; standard tranche <= BASE + 5;
   open dev harmful per 1k <= BASE + 1.5; UD-EWT dev harmful per 1k <= BASE + 1.5; UD-EWT dev accuracy >= BASE - 0.15 points;
   `n-named-state` target accuracy >= BASE - 3 points; deflated weights bytes <= 1.5 x BASE; throughput >= 150,000 tokens/s; imperative
   heading harmful per 1k <= BASE + 25.
3. Q = the arms CMP2, CMP2-F5, CMP2-HDG2 and CMP2-HDG2-F5 that pass every guard and whose Welch 95 percent interval (Welch-Satterthwaite
   degrees of freedom) of (arm - BASE) on the primary has an upper bound **strictly below 0**. If Q is empty, BASE stays.
4. m = the lowest primary mean in Q. S = the arms of Q within 10 percent relative of m (primary <= 1.10 x m).
5. The winner = the arm of S with the fewest additions (CMP2 1, CMP2-F5 2, CMP2-HDG2 2, CMP2-HDG2-F5 3; an addition is the enlarged compound
   data, the heading data, or the feature group F5); ties: the lowest primary (then the order of arms.json).

Reported, not gated: the means and standard deviations of every arm, the Welch interval of every arm against BASE for every guarded metric, the
pairwise intervals CMP2-F5 against CMP2, CMP2-HDG2 against CMP2 and CMP2-HDG2-F5 against CMP2-HDG2 on the primary and the co-primary, the
per-seed table, the per-tranche diagnostics (accuracy, harmful, wrong at >= 0.95, controls, heading kinds), the old-tranche-only primary
(`shapesNew.pooledPrimary`, comparable with PR #19), open and gold v2 dev, NLL, ECE, temperature, rows, bytes, training seconds.

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
DEFAULT_SHIPPED = os.path.join(REPO, "src", "DemaConsulting.PartOfSpeech", "Resources", "weights.bin")

T = "tools/DemaConsulting.PartOfSpeech.Trainer/"
REL_FROZEN_ANALYZER = T + "context-features/analyze_arms.py"
SD = "data/eval/shapes-dev/"

# Text files frozen by the pre-registration commit: repository-relative path -> SHA-256 of the committed text (line endings LF; a
# working-tree copy with CRLF, as .gitattributes gives the .cmd files, is normalized before hashing). This script cannot hold its own
# hash; the RUN-LOG section of the pre-registration records it.
FROZEN_FILES = {
    T + "compound-heading-2/run_arms.ps1": "20a4b921045a422784abf9db18ff37ddaff631d247d19de4bc034b81f3175e52",
    T + "compound-heading-2/arms.json": "88572609ce234dd6e4b33080cf5f61c9e28c353ca38d06d92d2df65c91f25440",
    REL_FROZEN_ANALYZER: "3d52825384b440c5786e1b241fc6f814e4e47a63f155414e0d6450107d33e494",
    T + "imperative-data/runner-base.cmd": "549a2c6ed8d1eaf00b30551fae14e546c51023dd79fe3a77810f39a1fe79ff3e",
    "data/compound/compound.tsv": "eb8c4708ba95fb370c885110e10eda0f1d826c5922876bd66e69389894bd5e3b",
    "data/compound/sources.tsv": "8b861dd3ff2d5a3fd5e741f5fe2a906807f99afa562d7e294d4011d93ef15e02",
    "data/compound/compound-2.tsv": "2a9a98492afb1248623b50111ca62d8abd57e4569b1a5e52b3cff544bc0746cc",
    "data/compound/compound-2-sources.tsv": "9c7ec7014ff115e745501706126b4637382c058783fdea43021b9f6cbfbd0b4f",
    "data/headings/headings.tsv": "cae45ff12cb4e7e9531a007beb8278b4014a8bdab9ae3e52b06ba46df4d48cde",
    "data/headings/sources.tsv": "26ca020469ee3a396ba71bd7845030a516e612c44e887933b1fe8450445fbad7",
    "data/headings/headings-2.tsv": "3f1fca247aa625c6f46269f5db5034540e4685fc55918dff5068da6ba591fca5",
    "data/headings/headings-2-sources.tsv": "accd6790b8226e962d8d192b7658375f1bafb7e1721a11779ee55e05024b682d",
    SD + "compound-head-2-sources.tsv": "c183effb24a273307e710e0b82bbf45cbc7a43d4b9bf071521c20c5e4c88a31f",
    SD + "headings-2-sources.tsv": "62754e46c3306983ffd6ece5360257e023c8a7bc268668644d2edc4df5452edd",
}

# Files of the state before any adoption: an adoption changes both by design, so the analysis is made before it (and the self-test does
# not check them). The weights file is hashed as bytes (it is binary), training.json as text.
PRE_ADOPTION_TEXT = {
    T + "training.json": "fe7e2d93b82cfdab53cd019350903b544005d3eecc6e9942315dce2f7d9f2973",
}
PRE_ADOPTION_RAW = {
    "src/DemaConsulting.PartOfSpeech/Resources/weights.bin": "48dd9188c37fb0ba01458e45a28d7b4b66d6a14b9f57eaf2a621bddc0e9b413b",
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
}
# The inputs that lie in the repository (the other three are caches or other folders): checked on disk too, as bytes.
INPUT_FILES = {
    "devSha256": SD + "dev.tsv", "hardSha256": SD + "hard.tsv", "compoundHeadSha256": SD + "compound-head.tsv",
    "headingsSha256": SD + "headings.tsv", "namedStateSha256": SD + "named-state.tsv",
    "compoundHead2Sha256": SD + "compound-head-2.tsv", "headings2Sha256": SD + "headings-2.tsv",
}

SHIPPED_SEED = 20260101
PRIMARY = "shapesFollowup.pooledPrimary.harmfulPer1k"
IMPERATIVE = "shapesFollowup.imperativeHeadings.harmfulPer1k"
OLD_PRIMARY = "shapesNew.pooledPrimary.harmfulPer1k"
CONFIDENT = "derived.confidentWrong"  # derived per run by load_runs: the sum over the four tranches
TRANCHES = ["compoundHead", "compoundHead2", "headings", "headings2"]
HEADING_TRANCHES = ["headings", "headings2"]
TOLERANCE = 1e-9
WINDOW = 1.10


def cw_path(tranche):
    return "shapesFollowup.%s.shape.confidentWrong" % tranche


# The figures of the shipped model (BASE, seed 20260101) that the BASE run of the shipped seed must reproduce (acceptance test a):
# (path, expected value, decimals). The older figures are those of the smoke scoring of the shipped weights before PR #19 trained any arm;
# the figures of `shapesFollowup` are the baseline of the shipped model on the new tranches measured in PR-A (no training).
SHIPPED_FIGURES = [
    ("shapesNew.compoundHead.shape.targetAccuracy", 60.0, 1),
    ("shapesNew.compoundHead.shape.decisions", 100, 0),
    ("shapesNew.compoundHead.shape.harmful", 39, 0),
    ("shapesNew.compoundHead.shape.confidentWrong", 5, 0),
    ("shapesNew.headings.shape.targetAccuracy", 84.2, 1),
    ("shapesNew.headings.shape.decisions", 78, 0),
    ("shapesNew.headings.shape.harmful", 4, 0),
    ("shapesNew.headings.shape.confidentWrong", 2, 0),
    ("shapesNew.namedState.shape.targetAccuracy", 90.0, 1),
    ("shapesNew.namedState.shape.decisions", 37, 0),
    ("shapesNew.namedState.shape.harmful", 3, 0),
    (OLD_PRIMARY, 241.57, 2),
    ("shapes.hard.pooled.harmfulPer1k", 170.0, 1),
    ("shapes.standard.pooled.harmfulPer1k", 15.0, 1),
    ("openDev.harmful", 91, 0),
    ("openDev.nounVerbDecisions", 4451, 0),
    ("udDev.harmful", 94, 0),
    ("udDev.decisions", 4570, 0),
    # the shipped model on the whole follow-up measure (PR-A baseline)
    ("shapesFollowup.compoundHead.shape.targetItems", 100, 0),
    ("shapesFollowup.compoundHead.shape.targetAccuracy", 60.0, 1),
    ("shapesFollowup.compoundHead.shape.decisions", 100, 0),
    ("shapesFollowup.compoundHead.shape.harmful", 39, 0),
    (cw_path("compoundHead"), 5, 0),
    ("shapesFollowup.compoundHead2.shape.targetItems", 210, 0),
    ("shapesFollowup.compoundHead2.shape.targetAccuracy", 62.9, 1),
    ("shapesFollowup.compoundHead2.shape.decisions", 210, 0),
    ("shapesFollowup.compoundHead2.shape.harmful", 71, 0),
    ("shapesFollowup.compoundHead2.shape.harmfulPer1k", 338.1, 1),
    (cw_path("compoundHead2"), 13, 0),
    ("shapesFollowup.compoundHead2.shape.controlItems", 45, 0),
    ("shapesFollowup.compoundHead2.shape.controlCorrect", 45, 0),
    ("shapesFollowup.headings.shape.targetItems", 95, 0),
    ("shapesFollowup.headings.shape.targetAccuracy", 84.2, 1),
    ("shapesFollowup.headings.shape.decisions", 78, 0),
    ("shapesFollowup.headings.shape.harmful", 4, 0),
    (cw_path("headings"), 2, 0),
    ("shapesFollowup.headings2.shape.targetItems", 109, 0),
    ("shapesFollowup.headings2.shape.targetAccuracy", 81.7, 1),
    ("shapesFollowup.headings2.shape.decisions", 82, 0),
    ("shapesFollowup.headings2.shape.harmful", 2, 0),
    (cw_path("headings2"), 1, 0),
    ("shapesFollowup.headings2.kinds.imperative.targetItems", 59, 0),
    ("shapesFollowup.headings2.kinds.imperative.targetCorrect", 59, 0),
    ("shapesFollowup.headings2.kinds.imperative.decisions", 32, 0),
    ("shapesFollowup.headings2.kinds.imperative.harmful", 0, 0),
    ("shapesFollowup.headings.kinds.imperative.decisions", 28, 0),
    ("shapesFollowup.headings.kinds.imperative.harmful", 3, 0),
    ("shapesFollowup.headings2.kinds.noun.targetItems", 50, 0),
    ("shapesFollowup.headings2.kinds.noun.targetCorrect", 30, 0),
    ("shapesFollowup.headings2.kinds.noun.harmful", 2, 0),
    ("shapesFollowup.headings2.kinds.proper.controlItems", 20, 0),
    ("shapesFollowup.headings2.kinds.proper.controlCorrect", 20, 0),
    ("shapesFollowup.pooledPrimary.targetItems", 514, 0),
    ("shapesFollowup.pooledPrimary.targetCorrect", 361, 0),
    ("shapesFollowup.pooledPrimary.decisions", 470, 0),
    ("shapesFollowup.pooledPrimary.harmful", 116, 0),
    (PRIMARY, 246.81, 2),
    ("shapesFollowup.pooledPrimary.confidentWrongTotal", 21, 0),
    (CONFIDENT, 21, 0),
    ("shapesFollowup.imperativeHeadings.targetItems", 104, 0),
    ("shapesFollowup.imperativeHeadings.targetCorrect", 99, 0),
    ("shapesFollowup.imperativeHeadings.decisions", 60, 0),
    ("shapesFollowup.imperativeHeadings.harmful", 3, 0),
    (IMPERATIVE, 50.0, 1),
    ("shapesFollowup.imperativeHeadings.confidentWrong", 0, 0),
    ("shapesFollowup.compoundControls.controlItems", 65, 0),
    ("shapesFollowup.compoundControls.controlCorrect", 65, 0),
]

# The shipped training mix (training.json before any adoption) and the sentences each new source adds at weight 1 (the sentence blocks of
# the data files; the self-test checks the counts against the files). `headings` has 374 sentences since the removal of one line after PR #19.
BASE_MIX = ["open:2", "masc:1", "silver:1", "imperative:1"]
ADDED_SENTENCES = {"compound": 742, "compound2": 1100, "headings": 374, "headings2": 247}
DATA_FILES = {"compound": "data/compound/compound.tsv", "compound2": "data/compound/compound-2.tsv",
              "headings": "data/headings/headings.tsv", "headings2": "data/headings/headings-2.tsv"}

# id, feature groups, added sources, role. Additions (the preference order of the rule) = the enlarged compound data (1 if any compound
# source), the heading data (1 if any heading source) and every feature group beyond BASE's.
ARMS = [
    {"id": "BASE", "features": ["clauseCues"], "sources": [], "role": "baseline"},
    {"id": "CMP2", "features": ["clauseCues"], "sources": ["compound", "compound2"], "role": "candidate"},
    {"id": "CMP2-F5", "features": ["clauseCues", "titleCaseLine"], "sources": ["compound", "compound2"], "role": "candidate"},
    {"id": "CMP2-HDG2", "features": ["clauseCues"], "sources": ["compound", "compound2", "headings", "headings2"], "role": "candidate"},
    {"id": "CMP2-HDG2-F5", "features": ["clauseCues", "titleCaseLine"], "sources": ["compound", "compound2", "headings", "headings2"],
     "role": "candidate"},
]
BASE_ID = "BASE"
CANDIDATES = [a["id"] for a in ARMS if a["role"] == "candidate"]
EXPECTED_SEEDS = [20260101, 11, 12, 13, 14, 15, 16, 17]


def additions(arm):
    base = ARMS[0]
    sources = set(arm["sources"])
    return (1 if sources & {"compound", "compound2"} else 0) + (1 if sources & {"headings", "headings2"} else 0) + \
        len(set(arm["features"]) - set(base["features"]))


# (path, label, decimals, required)
GROUPS = [
    ("Primary, co-primary and the four tranches", [
        (PRIMARY, "pooled harmful per 1k (primary)", 1, True),
        (CONFIDENT, "wrong at >= 0.95, four tranches (co-primary)", 2, True),
        ("shapesFollowup.pooledPrimary.targetAccuracy", "pooled target acc %", 2, False),
        ("shapesFollowup.compoundHead.shape.targetAccuracy", "compound-head target acc %", 1, False),
        ("shapesFollowup.compoundHead.shape.harmfulPer1k", "compound-head harmful per 1k", 1, False),
        (cw_path("compoundHead"), "compound-head wrong at >= 0.95", 2, True),
        ("shapesFollowup.compoundHead2.shape.targetAccuracy", "compound-head-2 target acc %", 1, False),
        ("shapesFollowup.compoundHead2.shape.harmfulPer1k", "compound-head-2 harmful per 1k", 1, False),
        (cw_path("compoundHead2"), "compound-head-2 wrong at >= 0.95", 2, True),
        ("shapesFollowup.compoundControls.controlAccuracy", "compound controls acc % (both)", 1, False),
        ("shapesFollowup.headings.shape.targetAccuracy", "headings target acc %", 1, False),
        ("shapesFollowup.headings.shape.harmfulPer1k", "headings harmful per 1k", 1, False),
        (cw_path("headings"), "headings wrong at >= 0.95", 2, True),
        ("shapesFollowup.headings2.shape.targetAccuracy", "headings-2 target acc %", 1, False),
        ("shapesFollowup.headings2.shape.harmfulPer1k", "headings-2 harmful per 1k", 1, False),
        (cw_path("headings2"), "headings-2 wrong at >= 0.95", 2, True),
        ("shapesFollowup.headings.shape.controlAccuracy", "headings control acc %", 1, False),
        ("shapesFollowup.headings2.shape.controlAccuracy", "headings-2 control acc %", 1, False),
    ]),
    ("Heading kinds (diagnostics) and the imperative-heading guard", [
        ("shapesFollowup.headings.kinds.noun.targetAccuracy", "headings noun target acc %", 1, False),
        ("shapesFollowup.headings2.kinds.noun.targetAccuracy", "headings-2 noun target acc %", 1, False),
        ("shapesFollowup.headings2.kinds.noun.harmfulPer1k", "headings-2 noun harmful per 1k", 1, False),
        ("shapesFollowup.headings.kinds.imperative.targetAccuracy", "headings imperative target acc %", 1, False),
        ("shapesFollowup.headings2.kinds.imperative.targetAccuracy", "headings-2 imperative target acc %", 1, False),
        ("shapesFollowup.imperativeHeadings.targetAccuracy", "imperative target acc % (pooled)", 1, False),
        (IMPERATIVE, "imperative harmful per 1k (pooled)", 1, True),
        ("shapesFollowup.imperativeHeadings.confidentWrong", "imperative wrong at >= 0.95 (pooled)", 2, False),
        ("shapesFollowup.headings2.kinds.proper.controlAccuracy", "headings-2 proper-noun control acc %", 1, False),
    ]),
    ("Guarded measures, named-state and the old-tranche primary", [
        ("shapes.hard.pooled.harmfulPer1k", "hard harmful per 1k", 1, True),
        ("shapes.hard.pooled.targetAccuracy", "hard target acc %", 2, False),
        ("shapes.standard.pooled.harmfulPer1k", "standard harmful per 1k", 1, True),
        ("shapes.standard.pooled.targetAccuracy", "standard target acc %", 2, False),
        ("shapesNew.namedState.shape.targetAccuracy", "named-state target acc %", 1, True),
        ("shapesNew.namedState.shape.harmfulPer1k", "named-state harmful per 1k", 1, False),
        (OLD_PRIMARY, "old-tranche primary (PR #19 measure)", 1, False),
    ]),
    ("Open dev, gold v2 dev and UD-EWT dev", [
        ("openDev.accuracy", "open acc %", 2, False),
        ("openDev.harmfulPer1k", "open harmful per 1k", 2, True),
        ("openDev.nll", "open NLL", 4, False),
        ("openDev.ece", "open ECE %", 2, False),
        ("goldDev.accuracy", "gold acc %", 2, False),
        ("goldDev.harmfulPer1k", "gold harmful per 1k", 1, False),
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

# name, label, metric path, kind, limit, aggregate. The co-primary comes first; it is judged with the guards.
GUARDS = [
    ("co-primary", "wrong at >= 0.95 <= BASE", CONFIDENT, "max_delta", 0.0, "mean"),
    ("hard-harmful", "hard harmful per 1k <= BASE + 10", "shapes.hard.pooled.harmfulPer1k", "max_delta", 10.0, "mean"),
    ("standard-harmful", "standard harmful per 1k <= BASE + 5", "shapes.standard.pooled.harmfulPer1k", "max_delta", 5.0, "mean"),
    ("open-harmful", "open dev harmful per 1k <= BASE + 1.5", "openDev.harmfulPer1k", "max_delta", 1.5, "mean"),
    ("ud-harmful", "UD-EWT dev harmful per 1k <= BASE + 1.5", "udDev.harmfulPer1k", "max_delta", 1.5, "mean"),
    ("ud-accuracy", "UD-EWT dev accuracy >= BASE - 0.15", "udDev.accuracy", "min_delta", -0.15, "mean"),
    ("named-state", "named-state target acc >= BASE - 3", "shapesNew.namedState.shape.targetAccuracy", "min_delta", -3.0, "mean"),
    ("size", "deflated bytes <= 1.5 x BASE", "weights.bytesDeflated", "max_ratio", 1.5, "mean"),
    ("throughput", "tokens/s >= 150,000", "throughput.tokensPerSecond", "min_abs", 150000.0, "median"),
    ("imperative-harmful", "imperative-heading harmful per 1k <= BASE + 25", IMPERATIVE, "max_delta", 25.0, "mean"),
]
WELCH_METRICS = [PRIMARY, CONFIDENT, "shapes.hard.pooled.harmfulPer1k", "shapes.standard.pooled.harmfulPer1k", "openDev.harmfulPer1k",
                 "udDev.harmfulPer1k", "udDev.accuracy", "shapesNew.namedState.shape.targetAccuracy", IMPERATIVE]
PAIRS = [("CMP2-F5", "CMP2"), ("CMP2-HDG2", "CMP2"), ("CMP2-HDG2-F5", "CMP2-HDG2")]


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


def welch_diff(arm, base):
    """Welch interval of mean(arm) - mean(base) at 95 percent: the function of the frozen script, unchanged."""
    return frozen_module().welch_diff(arm, base)


def arms_spec():
    """The seeds and the arms of arms.json."""
    with open(ARMS_JSON, encoding="utf-8") as f:
        spec = json.load(f)
    return [int(s) for s in spec["seeds"]], spec["arms"]


# ---------------------------------------------------------------------------------------------------------------------
# Loading
# ---------------------------------------------------------------------------------------------------------------------

def get_path(obj, path):
    cur = obj
    for part in path.split("."):
        if not isinstance(cur, dict) or part not in cur:
            return None
        cur = cur[part]
    return cur if isinstance(cur, (int, float)) and not isinstance(cur, bool) else None


def arm_by_id(arm_id):
    return next(a for a in ARMS if a["id"] == arm_id)


def run_stem(directory, arm_id, seed):
    return os.path.join(directory, "%s-s%s" % (arm_id, seed))


def count_paths():
    """The counts from which the primary, the co-primary and the imperative rate are re-derived."""
    paths = []
    for t in TRANCHES:
        paths += ["shapesFollowup.%s.shape.%s" % (t, k) for k in ("decisions", "harmful", "confidentWrong")]
    for t in HEADING_TRANCHES:
        paths += ["shapesFollowup.%s.kinds.imperative.%s" % (t, k) for k in ("decisions", "harmful")]
    paths += ["shapesFollowup.pooledPrimary.decisions", "shapesFollowup.pooledPrimary.harmful",
              "shapesFollowup.pooledPrimary.confidentWrongTotal", "shapesFollowup.imperativeHeadings.decisions",
              "shapesFollowup.imperativeHeadings.harmful"]
    return paths


def validate_run(run, name, arm, seed, required):
    """Returns the list of problems of one loaded run (and adds the derived member)."""
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
    if "sources" in run and sorted(run["sources"]) != sorted(arm["sources"]):
        errors.append("%s: sources are %r, expected %r" % (name, run["sources"], arm["sources"]))
    needed = list(dict.fromkeys(required + count_paths() + [PRIMARY, IMPERATIVE]))
    missing = [m for m in needed if m != CONFIDENT and get_path(run, m) is None]
    for m in missing:
        errors.append("%s: required metric %s is missing" % (name, m))
    if missing:
        return errors
    # the derived members: the co-primary, and the re-derivation of the primary and of the imperative rate from the counts
    cw = sum(get_path(run, cw_path(t)) for t in TRANCHES)
    if abs(cw - get_path(run, "shapesFollowup.pooledPrimary.confidentWrongTotal")) > TOLERANCE:
        errors.append("%s: pooledPrimary.confidentWrongTotal %r is not the sum %r of the four tranches" %
                      (name, get_path(run, "shapesFollowup.pooledPrimary.confidentWrongTotal"), cw))
    decisions = sum(get_path(run, "shapesFollowup.%s.shape.decisions" % t) for t in TRANCHES)
    harmful = sum(get_path(run, "shapesFollowup.%s.shape.harmful" % t) for t in TRANCHES)
    if abs(decisions - get_path(run, "shapesFollowup.pooledPrimary.decisions")) > TOLERANCE or \
            abs(harmful - get_path(run, "shapesFollowup.pooledPrimary.harmful")) > TOLERANCE:
        errors.append("%s: pooledPrimary decisions/harmful %r/%r are not the sums %r/%r of the four tranches" %
                      (name, get_path(run, "shapesFollowup.pooledPrimary.decisions"),
                       get_path(run, "shapesFollowup.pooledPrimary.harmful"), decisions, harmful))
    rederived = 1000.0 * harmful / decisions if decisions else 0.0
    if abs(rederived - get_path(run, PRIMARY)) > 1e-6:
        errors.append("%s: the primary %r is not the value %r re-derived from the counts" % (name, get_path(run, PRIMARY), rederived))
    i_decisions = sum(get_path(run, "shapesFollowup.%s.kinds.imperative.decisions" % t) for t in HEADING_TRANCHES)
    i_harmful = sum(get_path(run, "shapesFollowup.%s.kinds.imperative.harmful" % t) for t in HEADING_TRANCHES)
    if abs(i_decisions - get_path(run, "shapesFollowup.imperativeHeadings.decisions")) > TOLERANCE or \
            abs(i_harmful - get_path(run, "shapesFollowup.imperativeHeadings.harmful")) > TOLERANCE:
        errors.append("%s: imperativeHeadings decisions/harmful are not the sums %r/%r of the imperative kinds" % (name, i_decisions, i_harmful))
    i_rederived = 1000.0 * i_harmful / i_decisions if i_decisions else 0.0
    if abs(i_rederived - get_path(run, IMPERATIVE)) > 1e-6:
        errors.append("%s: the imperative rate %r is not the value %r re-derived from the counts" % (name, get_path(run, IMPERATIVE), i_rederived))
    run["derived"] = {"confidentWrong": cw, "primaryRederived": rederived, "imperativeRederived": i_rederived}
    return errors


def load_runs(directory, seeds, arms=None):
    """Returns {arm id: [run dict per seed]}; raises RunsError on any missing or invalid run (no selection is made)."""
    required = [m[0] for g in GROUPS for m in g[1] if m[3]]
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
# Acceptance tests (a), (b), (c)
# ---------------------------------------------------------------------------------------------------------------------

TRAINING_LINE = re.compile(r"^Training on ([0-9][0-9,.\s\u00a0\u202f]*?) sentences \(.*?\bsources=(\S*)", re.MULTILINE)


def parse_training_line(text):
    """Returns (sentence count, [source:weight, ...]) of the first `Training on N sentences (...), sources=...` line, or None."""
    m = TRAINING_LINE.search(text)
    if not m:
        return None
    digits = re.sub(r"[^0-9]", "", m.group(1))
    return int(digits), [s for s in m.group(2).split(",") if s]


def acceptance(runs, directory, seeds, shipped_path, shipped_figures=None, added=None):
    """Runs the acceptance tests (a), (b), (c); returns a list of {name, ok, detail}."""
    shipped_figures = SHIPPED_FIGURES if shipped_figures is None else shipped_figures
    added = ADDED_SENTENCES if added is None else added
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
            bin_path = run_stem(directory, BASE_ID, SHIPPED_SEED) + ".bin"
            if os.path.isfile(bin_path):
                bin_sha = sha256_file(bin_path)
                add("a: BASE seed %d weights file is byte-identical to the shipped weights.bin" % SHIPPED_SEED, bin_sha == shipped_sha,
                    "file %s, shipped %s" % (bin_sha, shipped_sha))
        for path, expected, decimals in shipped_figures:
            value = get_path(base_run, path)
            ok = value is not None and round(value, decimals) == expected
            add("a: BASE seed %d %s = %s" % (SHIPPED_SEED, path, expected), ok, "run %s" % value)

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
            more = sum(added[s] for s in arm["sources"])
            want_sources = BASE_MIX + ["%s:1" % s for s in arm["sources"]]
            ok = count == base_count + more and sorted(sources) == sorted(want_sources)
            what = "the shipped mix only" if not arm["sources"] else "BASE + %d sentences (%s)" % (more, ", ".join("%s:1" % s for s in arm["sources"]))
            add("b: %s seed %s trained on %s" % (arm["id"], seed, what), ok,
                "log: %d sentences (BASE %d), sources %s" % (count, base_count, ",".join(sources)))

    # (c) every run scored the frozen development files
    seen = {}
    for items in runs.values():
        for run in items:
            inputs = run.get("inputs") or {}
            for key in FROZEN_INPUTS:
                seen.setdefault(key, set()).add(inputs.get(key))
    for key, expected in FROZEN_INPUTS.items():
        values = seen.get(key, {None})
        add("c: inputs.%s agrees in all runs and equals the frozen value" % key, values == {expected},
            "values %s, frozen %s" % (sorted(str(v) for v in values), expected))
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
    """The arm of `within` with the fewest additions; ties: the lowest primary mean; then the order of the arms."""
    return min(within, key=lambda a: (arms[a]["additions"], arms[a]["primaryMean"], CANDIDATES.index(a) if a in CANDIDATES else 99))


def decide(runs, welch=None):
    """Applies the pre-registered rule B to the loaded runs (a pure function of the numbers). Returns the decision dictionary.

    `welch` is the interval function (the frozen one by default); the self-test injects others to test the strict inequality.
    """
    welch = welch or welch_diff
    base_primary = column(runs[BASE_ID], PRIMARY)
    arms = {}
    for arm in ARMS:
        label = arm["id"]
        if label not in runs:
            continue
        primary = column(runs[label], PRIMARY)
        confident = column(runs[label], CONFIDENT)
        info = {"role": arm["role"], "additions": additions(arm), "primaryMean": statistics.fmean(primary),
                "primarySd": statistics.stdev(primary), "primaryValues": primary,
                "confidentWrongMean": statistics.fmean(confident), "confidentWrongSd": statistics.stdev(confident),
                "confidentWrongValues": confident}
        if label != BASE_ID:
            d, lo, hi, df, p = welch(primary, base_primary)
            info["diffVsBase"] = {"mean": d, "lo": lo, "hi": hi, "df": df, "p": p}
            info["guards"] = [check_guard(g, column(runs[label], g[2]), column(runs[BASE_ID], g[2])) for g in GUARDS]
            info["passesAllGuards"] = all(g["pass"] for g in info["guards"])  # the co-primary is the first guard
            info["significant"] = bool(hi < 0.0)  # strict: an upper bound of exactly 0 is not an improvement
            info["inQ"] = info["passesAllGuards"] and info["significant"]
        arms[label] = info
    pairs = {}
    for a, b in PAIRS:
        if a in runs and b in runs:
            entry = {}
            for metric in (PRIMARY, CONFIDENT):
                d, lo, hi, df, p = welch(column(runs[a], metric), column(runs[b], metric))
                entry[metric] = {"mean": d, "lo": lo, "hi": hi, "df": df, "p": p}
            pairs["%s - %s" % (a, b)] = entry

    qualifying = [a for a in CANDIDATES if a in arms and arms[a]["inQ"]]
    decision = {"schema": 1, "rule": "B", "baseline": BASE_ID, "primaryMetric": PRIMARY, "seeds": len(base_primary), "arms": arms,
                "pairs": pairs, "passingGuards": [a for a in CANDIDATES if a in arms and arms[a]["passesAllGuards"]],
                "qualifying": qualifying, "lowestPrimary": None, "withinTenPercent": [], "candidate": None,
                "candidateInterval": None, "wins": False, "winner": BASE_ID}
    if not qualifying:
        decision["reason"] = ("No candidate arm passes every guard and has a Welch 95%% interval of (arm - BASE) entirely below 0 on the "
                              "primary (Q is empty): BASE stays, nothing ships.")
        return decision

    lowest = min(qualifying, key=lambda a: (arms[a]["primaryMean"], CANDIDATES.index(a)))
    m = arms[lowest]["primaryMean"]
    within = [a for a in qualifying if arms[a]["primaryMean"] <= WINDOW * m + TOLERANCE]
    winner = pick_winner(within, arms)
    interval = arms[winner]["diffVsBase"]
    decision.update({"lowestPrimary": {"arm": lowest, "value": m}, "withinTenPercent": within, "candidate": winner,
                     "candidateInterval": interval, "wins": True, "winner": winner})
    decision["reason"] = ("%s wins: it has the fewest additions (%d) among the arms of Q within 10%% of the lowest primary (%s, %.2f); "
                          "the Welch 95%% interval of (%s - BASE) on the primary is [%.2f, %.2f], entirely below 0." %
                          (winner, arms[winner]["additions"], lowest, m, winner, interval["lo"], interval["hi"]))
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


def build_report(runs, decision, checks, directory, seeds):
    order = [a["id"] for a in ARMS if a["id"] in runs]
    out = ["# Compound-head and heading follow-up: analysis", "",
           "Arms: %s; seeds: %s (%d per arm); folder `%s`. Values are mean ± sample sd over the seeds. Baseline: BASE (the shipped model)." %
           (", ".join(order), ", ".join(str(s) for s in seeds), len(seeds), directory), ""]
    out += table(["arm", "feature groups", "sources added", "additions", "role"],
                 [[a, "+".join(arm_by_id(a)["features"]), ", ".join(arm_by_id(a)["sources"]) or "none", additions(arm_by_id(a)),
                   arm_by_id(a)["role"]] for a in order]).split("\n") + [""]

    out += ["## Acceptance tests", "", table(["test", "result", "detail"],
                                              [[c["name"], "PASS" if c["ok"] else "FAIL", c["detail"]] for c in checks]), ""]
    if decision is None:
        out += ["**An acceptance test failed: no selection is made.**", ""]
        return "\n".join(out)

    base = decision["arms"][BASE_ID]
    out += ["## Selection table (rule B)", "",
            "Primary = pooled harmful per 1k over the four development tranches (`compound-head`, `compound-head-2`, `headings`, `headings-2`; "
            "lower is better); co-primary = wrong at >= 0.95 summed over the four tranches. Delta guards use the mean over the seeds against BASE "
            "(primary mean %.2f, wrong at >= 0.95 mean %.2f); the size guard is 1.5 x the BASE mean; the throughput guard is absolute (median over "
            "the seeds of each run's median). Q = the arms that pass every guard and whose Welch interval of (arm - BASE) on the primary lies "
            "entirely below 0." % (base["primaryMean"], base["confidentWrongMean"]), ""]
    rows = [[BASE_ID, "%.2f ± %.2f" % (base["primaryMean"], base["primarySd"]),
             "%.2f ± %.2f" % (base["confidentWrongMean"], base["confidentWrongSd"])] + ["-"] * len(GUARDS) + ["-", "baseline"]]
    for a in order:
        if a == BASE_ID:
            continue
        info = decision["arms"][a]
        rows.append([a, "%.2f ± %.2f" % (info["primaryMean"], info["primarySd"]),
                     "%.2f ± %.2f" % (info["confidentWrongMean"], info["confidentWrongSd"])] + [guard_cell(g) for g in info["guards"]] +
                    ["PASS" if info["passesAllGuards"] else "FAIL", "yes" if info["inQ"] else "no"])
    out += [table(["arm", "primary (mean ± sd)", "wrong at >= 0.95 (mean ± sd)"] + [g[1] for g in GUARDS] + ["all guards", "in Q"], rows), ""]

    out += ["## Welch 95% interval of (arm - BASE)", "",
            "Difference of the means with the Welch-Satterthwaite t interval over the seeds (seeds measure training noise only; four arms against "
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
    rows = []
    for name, entry in decision["pairs"].items():
        rows.append([name] + ["%+.2f [%+.2f, %+.2f]" % (entry[m]["mean"], entry[m]["lo"], entry[m]["hi"]) for m in (PRIMARY, CONFIDENT)])
    out += [table(["pair", "primary", "wrong at >= 0.95"], rows), ""]

    out += ["## Selection rule", ""]
    rows = []
    for a in order:
        info = decision["arms"][a]
        rows.append([a, info["additions"], "%.2f ± %.2f" % (info["primaryMean"], info["primarySd"]),
                     "%.2f" % info["confidentWrongMean"],
                     "-" if a == BASE_ID else ("yes" if info["passesAllGuards"] else "no"),
                     "-" if a == BASE_ID else ("yes" if info["significant"] else "no"),
                     "-" if a == BASE_ID else ("yes" if info["inQ"] else "no")])
    out += [table(["arm", "additions", "primary", "wrong at >= 0.95 (mean)", "passes every guard", "interval below 0", "in Q"], rows), ""]
    out += ["- Arms in Q: %s" % (", ".join(decision["qualifying"]) or "none")]
    if decision["lowestPrimary"]:
        lp = decision["lowestPrimary"]
        out += ["- Lowest primary in Q: %s (%.2f); within 10%% (primary <= %.2f): %s" %
                (lp["arm"], lp["value"], WINDOW * lp["value"], ", ".join(decision["withinTenPercent"])),
                "- Winner (fewest additions, then lowest primary): %s" % decision["winner"]]
        ci = decision["candidateInterval"]
        out += ["- Welch 95%% interval of (%s - BASE) on the primary: %+.2f [%+.2f, %+.2f], df %.2f, p %.4f" %
                (decision["winner"], ci["mean"], ci["lo"], ci["hi"], ci["df"], ci["p"])]
    out += ["", "**Decision: %s.** %s" % (("%s WINS" % decision["winner"]) if decision["wins"] else "BASE STAYS", decision["reason"]), ""]

    out += ["## Per-seed primary and wrong at >= 0.95 (primary / wrong at >= 0.95)", ""]
    out += [table(["arm"] + [str(s) for s in seeds],
                  [[a] + ["%.1f / %d" % (get_path(r, PRIMARY), get_path(r, CONFIDENT)) for r in runs[a]] for a in order]), ""]

    out += ["## Full table (reported, not gated)", ""]
    for title, metrics in GROUPS:
        out += ["### %s" % title, ""]
        out += [table(["arm"] + [m[1] for m in metrics], [[a] + [cell(column(runs[a], m[0]), m[2]) for m in metrics] for a in order]), ""]

    commits = sorted({str((r.get("inputs") or {}).get("commit")) for items in runs.values() for r in items})
    dirty = sorted({str((r.get("inputs") or {}).get("dirty")) for items in runs.values() for r in items})
    out += ["Trainer commit of the runs (`inputs.commit`): %s; dirty flag: %s." % (", ".join(commits), ", ".join(dirty)), ""]
    return "\n".join(out)


def analyze(directory, shipped_path=DEFAULT_SHIPPED, verify=True, shipped_figures=None, added=None):
    """Loads the runs, runs the frozen-file check and the acceptance tests, and applies the rule.

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
        if found is None or sorted(found["features"]) != sorted(arm["features"]) or sorted(found["sources"]) != sorted(arm["sources"]):
            raise RunsError("arms.json does not define the arm %s as this script expects" % arm["id"])
    runs = load_runs(directory, seeds)
    checks = acceptance(runs, directory, seeds, shipped_path, shipped_figures, added)
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
TRANCHE_DECISIONS = {"compoundHead": 100, "compoundHead2": 210, "headings": 78, "headings2": 82}
IMPERATIVE_DECISIONS = {"headings": 28, "headings2": 32}
BASE_SPEC = {
    # BASE means (the seed offsets give an exact mean and sample sd); the primary, the co-primary and the imperative rate are spread over
    # the tranches by synthetic_runs
    PRIMARY: (240.0, 6.0), CONFIDENT: (20.0, 1.0), IMPERATIVE: (50.0, 5.0), "shapes.hard.pooled.harmfulPer1k": (170.0, 8.0),
    "shapes.hard.pooled.targetAccuracy": (77.0, 1.0), "shapes.standard.pooled.harmfulPer1k": (15.0, 1.0),
    "shapes.standard.pooled.targetAccuracy": (97.8, 0.2), "shapesNew.namedState.shape.targetAccuracy": (90.0, 2.0),
    "shapesNew.namedState.shape.harmfulPer1k": (80.0, 5.0), "openDev.accuracy": (96.65, 0.05), "openDev.harmfulPer1k": (20.0, 0.5),
    "openDev.nll": (0.11, 0.001), "openDev.ece": (0.24, 0.02), "goldDev.accuracy": (94.84, 0.2), "goldDev.harmfulPer1k": (26.0, 1.0),
    "udDev.accuracy": (92.95, 0.05), "udDev.harmfulPer1k": (20.5, 0.5), "temperature": (9.5, 0.1), "weights.rows": (122088, 100),
    "weights.bytesRaw": (1157499, 500), "weights.bytesDeflated": (521695, 200), "throughput.tokensPerSecond": (400000.0, 10000.0),
    "trainWallSeconds": (60.0, 1.0),
}
SYNTH_FIGURES = {"openDev.harmful": 91, "openDev.nounVerbDecisions": 4451, "udDev.harmful": 94, "udDev.decisions": 4570,
                 "shapesNew.namedState.shape.decisions": 37, "shapesNew.namedState.shape.harmful": 3,
                 "shapesFollowup.compoundHead2.shape.targetAccuracy": 62.9, "shapesFollowup.compoundControls.controlItems": 65}
SYNTH_BASE_SENTENCES = 61680


def set_path(obj, path, value):
    parts = path.split(".")
    for p in parts[:-1]:
        obj = obj.setdefault(p, {})
    obj[parts[-1]] = value


def synthetic_runs(overrides, labels=None, seeds=None):
    """overrides: {arm id: {metric path: (mean, sd)}}. Values are mean + sd x offset / sqrt(6): an exact mean and sample sd over 8 seeds."""
    labels = labels or [a["id"] for a in ARMS]
    seeds = seeds or arms_spec()[0]
    runs = {}
    for ai, label in enumerate(labels):
        runs[label] = []
        values = dict(BASE_SPEC)
        values.update(overrides.get(label, {}))
        for si, seed in enumerate(seeds):
            arm = arm_by_id(label)
            run = {"schema": 1, "arm": label, "seed": seed, "features": list(arm["features"]), "groups": list(arm["features"]),
                   "sources": list(arm["sources"])}
            for path, (mean, sd) in values.items():
                set_path(run, path, mean + OFFSETS[(si + ai) % 8] / SQ * sd)
            for path, value in SYNTH_FIGURES.items():
                set_path(run, path, value)
            p, c, q = run["shapesFollowup"]["pooledPrimary"]["harmfulPer1k"], run["derived"]["confidentWrong"], \
                run["shapesFollowup"]["imperativeHeadings"]["harmfulPer1k"]
            total = sum(TRANCHE_DECISIONS.values())
            for tranche, n in TRANCHE_DECISIONS.items():
                shape = run["shapesFollowup"].setdefault(tranche, {}).setdefault("shape", {})
                shape.update({"decisions": n, "harmful": p * n / 1000.0, "confidentWrong": c / 4.0})
            run["shapesFollowup"]["pooledPrimary"].update({"decisions": total, "harmful": p * total / 1000.0, "confidentWrongTotal": c})
            for tranche, n in IMPERATIVE_DECISIONS.items():
                run["shapesFollowup"][tranche].setdefault("kinds", {})["imperative"] = {"decisions": n, "harmful": q * n / 1000.0}
            n_imp = sum(IMPERATIVE_DECISIONS.values())
            run["shapesFollowup"]["imperativeHeadings"].update({"decisions": n_imp, "harmful": q * n_imp / 1000.0})
            runs[label].append(run)
    return runs


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
            run["weights"]["sha256"] = hashlib.sha256(shipped_bytes).hexdigest() if shipped_run else hashlib.sha256(
                ("%s-%s" % (label, seed)).encode()).hexdigest()
            run["inputs"] = dict(FROZEN_INPUTS, commit="0123456789abcdef", dirty=False)
            n = SYNTH_BASE_SENTENCES + sum(more[s] for s in arm["sources"])
            line = "Training on %s sentences (1,000,000 tokens per epoch), sources=%s epochs=6 seed=%s\n" % (
                format(n, ","), ",".join(BASE_MIX + ["%s:1" % s for s in arm["sources"]]), seed)
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


def figures_of(run):
    """The shipped-figure table of the synthetic BASE run (the real SHIPPED_FIGURES are tested separately)."""
    def value(path):
        return sum(get_path(run, cw_path(t)) for t in TRANCHES) if path == CONFIDENT else get_path(run, path)

    return [(path, round(value(path), 3), 3) for path, _, _ in SHIPPED_FIGURES if value(path) is not None]


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
    # eight seeds: the synthetic offsets give an exact mean and sample sd
    check("offsets mean", abs(statistics.fmean(OFFSETS)) < 1e-12 and abs(statistics.stdev([o / SQ for o in OFFSETS]) - 1.0) < 1e-12)

    # Frozen files: the real files match; a change is found; CRLF is normalized; the raw table is not.
    check("frozen files", verify_frozen() == [], str(verify_frozen()))
    check("frozen development files", verify_frozen(table=input_files_table()) == [], str(verify_frozen(table=input_files_table())))
    check("frozen mismatch", len(verify_frozen(table={REL_FROZEN_ANALYZER: "0" * 64})) == 1)
    check("frozen missing", len(verify_frozen(table={"no/such/file": "0" * 64})) == 1)
    check("frozen raw mismatch", len(verify_frozen(table={}, raw_table={REL_FROZEN_ANALYZER: "0" * 64})) == 1)
    check("crlf normalized", sha256_lf(b"a\r\nb\n") == sha256_lf(b"a\nb\n") and sha256_lf(b"a\nb") != sha256_lf(b"a\nc"))
    check("input files are inputs", set(INPUT_FILES) <= set(FROZEN_INPUTS) and len(FROZEN_INPUTS) == 10 and len(INPUT_FILES) == 7,
          str(sorted(FROZEN_INPUTS)))
    check("arms.json seeds", seeds == EXPECTED_SEEDS == [20260101, 11, 12, 13, 14, 15, 16, 17], str(seeds))
    for arm in ARMS:
        found = next((a for a in spec_arms if a["id"] == arm["id"]), None)
        check("arms.json %s" % arm["id"], found is not None and found["features"] == arm["features"] and found["sources"] == arm["sources"], str(found))
    check("arms.json has no other arm and keeps the order", [a["id"] for a in spec_arms] == [a["id"] for a in ARMS])
    check("additions order", [additions(a) for a in ARMS] == [0, 1, 2, 2, 3], str([additions(a) for a in ARMS]))
    for source, rel in DATA_FILES.items():
        n = count_sentences(os.path.join(REPO, *rel.split("/")))
        check("sentence count of %s" % source, n == ADDED_SENTENCES[source], "%d" % n)
    check("training line parse", parse_training_line("x\nTraining on 61,680 sentences (1,234 tokens per epoch), sources=open:2,masc:1 epochs=6\n")
          == (61680, ["open:2", "masc:1"]))
    check("training line parse (no separator)", parse_training_line("Training on 62422 sentences (1 tokens per epoch), sources=open:2")
          == (62422, ["open:2"]))
    check("training line parse (none)", parse_training_line("trained on 5 sentences") is None)
    check("guards: ten, imperative last", len(GUARDS) == 10 and GUARDS[0][0] == "co-primary" and GUARDS[-1][0] == "imperative-harmful"
          and GUARDS[-1][2] == "shapesFollowup.imperativeHeadings.harmfulPer1k" and GUARDS[-1][4] == 25.0, str(GUARDS[-1]))
    check("primary path", PRIMARY == "shapesFollowup.pooledPrimary.harmfulPer1k")

    B = {path: value[0] for path, value in BASE_SPEC.items()}

    def P(x, sd=4.0):
        return {PRIMARY: (x, sd)}

    def all_ok(overrides, welch=None):
        return decide(synthetic_runs(overrides), welch)

    # shipped figures: the real table is satisfied by a run that has those values and fails when one figure is wrong
    shipped_run = {"weights": {"sha256": hashlib.sha256(b"shipped").hexdigest()}}
    for path, value, _ in SHIPPED_FIGURES:
        set_path(shipped_run, path, value)
    with tempfile.TemporaryDirectory() as tmp:
        shipped = os.path.join(tmp, "shipped-weights.bin")
        with open(shipped, "wb") as f:
            f.write(b"shipped")
        runs = {a["id"]: [json.loads(json.dumps(shipped_run)) for _ in seeds] for a in ARMS}

        def figure_checks(candidate_runs):
            return [c for c in acceptance(candidate_runs, tmp, seeds, shipped) if c["name"].startswith("a: BASE seed") and " = " in c["name"]]

        ok = figure_checks(runs)
        check("real shipped figures are checked (%d)" % len(SHIPPED_FIGURES), len(ok) == len(SHIPPED_FIGURES) and all(c["ok"] for c in ok),
              str([c["name"] for c in ok if not c["ok"]]))
        for path, value, _ in SHIPPED_FIGURES:
            broken = json.loads(json.dumps(runs))
            set_path(broken[BASE_ID][seeds.index(SHIPPED_SEED)], path, value + 1)
            bad = [c["name"] for c in figure_checks(broken) if not c["ok"]]
            check("real shipped figure %s is enforced" % path, len(bad) == 1 and path in bad[0], str(bad))
    check("shipped figures cover the new tranches and the imperative block",
          all(any(p == q for q, _, _ in SHIPPED_FIGURES) for p in (PRIMARY, IMPERATIVE, cw_path("compoundHead2"), cw_path("headings2"),
                                                                      "shapesFollowup.pooledPrimary.confidentWrongTotal")))
    # the table is also checked against itself: the pooled figures are the sums of the tranche figures and the rates follow from the counts
    fig = {path: value for path, value, _ in SHIPPED_FIGURES}
    sums = {k: sum(fig["shapesFollowup.%s.shape.%s" % (t, k)] for t in TRANCHES) for k in ("decisions", "harmful")}
    check("shipped figures: pooled counts are the sums of the four tranches",
          sums == {"decisions": fig["shapesFollowup.pooledPrimary.decisions"], "harmful": fig["shapesFollowup.pooledPrimary.harmful"]}
          and sum(fig[cw_path(t)] for t in TRANCHES) == fig["shapesFollowup.pooledPrimary.confidentWrongTotal"] == fig[CONFIDENT],
          str(sums))
    check("shipped figures: the primary follows from the counts",
          round(1000.0 * fig["shapesFollowup.pooledPrimary.harmful"] / fig["shapesFollowup.pooledPrimary.decisions"], 2) == fig[PRIMARY])
    i_n = sum(fig["shapesFollowup.%s.kinds.imperative.decisions" % t] for t in HEADING_TRANCHES)
    i_h = sum(fig["shapesFollowup.%s.kinds.imperative.harmful" % t] for t in HEADING_TRANCHES)
    check("shipped figures: the imperative counts are the sums of the two kinds and the rate follows from them",
          i_n == fig["shapesFollowup.imperativeHeadings.decisions"] == 60 and i_h == fig["shapesFollowup.imperativeHeadings.harmful"] == 3
          and round(1000.0 * i_h / i_n, 1) == fig[IMPERATIVE])

    # S1: rule B: every arm qualifies; the lowest is CMP2-HDG2-F5 (105), the window is 115.5: CMP2 (120) is outside, CMP2-F5 and
    # CMP2-HDG2 have 2 additions each, the tie goes to the lower primary (CMP2-HDG2, 110).
    d = all_ok({"CMP2": P(120.0), "CMP2-F5": P(115.0), "CMP2-HDG2": P(110.0), "CMP2-HDG2-F5": P(105.0)})
    check("S1 qualifying", d["qualifying"] == CANDIDATES, str(d["qualifying"]))
    check("S1 window", d["withinTenPercent"] == ["CMP2-F5", "CMP2-HDG2", "CMP2-HDG2-F5"] and d["lowestPrimary"]["arm"] == "CMP2-HDG2-F5",
          str(d["withinTenPercent"]))
    check("S1 tie of additions goes to the lowest primary", d["winner"] == "CMP2-HDG2" and d["wins"], str(d["winner"]))
    d = all_ok({"CMP2": P(115.0), "CMP2-F5": P(115.0), "CMP2-HDG2": P(110.0), "CMP2-HDG2-F5": P(105.0)})
    check("S1 CMP2 inside the window has the fewest additions", d["winner"] == "CMP2" and "CMP2" in d["withinTenPercent"], str(d["winner"]))
    d = all_ok({"CMP2": P(130.0), "CMP2-F5": P(130.0), "CMP2-HDG2": P(110.0), "CMP2-HDG2-F5": P(105.0)})
    check("S1 preference order: 2 additions before 3", d["winner"] == "CMP2-HDG2" and d["withinTenPercent"] == ["CMP2-HDG2", "CMP2-HDG2-F5"])
    arms_equal = {"CMP2-HDG2": {"additions": 2, "primaryMean": 5.0}, "CMP2-F5": {"additions": 2, "primaryMean": 5.0}}
    check("S1 equal primary and equal additions: the order of arms.json", pick_winner(["CMP2-HDG2", "CMP2-F5"], arms_equal) == "CMP2-F5")

    # S2: the 10 percent window: 110.0 is exactly 1.10 x 100 (inside, CMP2 preferred); 110.5 is outside.
    base300 = {"BASE": P(300.0)}
    d = all_ok(dict(base300, **{"CMP2-HDG2-F5": P(100.0), "CMP2-HDG2": P(105.0), "CMP2-F5": P(108.0), "CMP2": P(110.0)}))
    check("S2 boundary inside", d["withinTenPercent"] == CANDIDATES and d["winner"] == "CMP2", str(d["withinTenPercent"]))
    d = all_ok(dict(base300, **{"CMP2-HDG2-F5": P(100.0), "CMP2-HDG2": P(105.0), "CMP2-F5": P(108.0), "CMP2": P(110.5)}))
    check("S2 outside", d["withinTenPercent"] == ["CMP2-F5", "CMP2-HDG2", "CMP2-HDG2-F5"] and d["winner"] == "CMP2-HDG2",
          str(d["withinTenPercent"]) + str(d["winner"]))
    d = all_ok(dict(base300, **{"CMP2-HDG2-F5": P(100.0), "CMP2-HDG2": P(110.5), "CMP2-F5": P(111.0), "CMP2": P(112.0)}))
    check("S2 all but the lowest outside", d["withinTenPercent"] == ["CMP2-HDG2-F5"] and d["winner"] == "CMP2-HDG2-F5")
    d = all_ok(dict(base300, **{"CMP2-HDG2-F5": P(100.0), "CMP2-HDG2": P(105.0), "CMP2": P(99.0)}))
    check("S2 CMP2 lowest", d["lowestPrimary"]["arm"] == "CMP2" and d["winner"] == "CMP2")
    arms_tie = {"X": {"additions": 2, "primaryMean": 5.0}, "Y": {"additions": 2, "primaryMean": 4.0}, "Z": {"additions": 3, "primaryMean": 1.0}}
    check("S2 tie of additions: the lowest primary", pick_winner(["X", "Y", "Z"], arms_tie) == "Y")
    arms_pref = {"X": {"additions": 3, "primaryMean": 1.0}, "Y": {"additions": 1, "primaryMean": 9.0}}
    check("S2 fewer additions beat a lower primary", pick_winner(["X", "Y"], arms_pref) == "Y")

    # S3: each guard (and the co-primary) passes exactly at its limit and fails just beyond it.
    def flags(arm_overrides):
        dec = all_ok({"CMP2": dict(P(150.0), **arm_overrides)})
        return {g["name"]: g["pass"] for g in dec["arms"]["CMP2"]["guards"]}

    cases = [
        ("co-primary", CONFIDENT, (B[CONFIDENT], 1.0), (B[CONFIDENT] + 0.2, 1.0)),
        ("hard-harmful", "shapes.hard.pooled.harmfulPer1k", (B["shapes.hard.pooled.harmfulPer1k"] + 10.0, 8.0),
         (B["shapes.hard.pooled.harmfulPer1k"] + 10.2, 8.0)),
        ("standard-harmful", "shapes.standard.pooled.harmfulPer1k", (B["shapes.standard.pooled.harmfulPer1k"] + 5.0, 1.0),
         (B["shapes.standard.pooled.harmfulPer1k"] + 5.2, 1.0)),
        ("open-harmful", "openDev.harmfulPer1k", (B["openDev.harmfulPer1k"] + 1.5, 0.5), (B["openDev.harmfulPer1k"] + 1.6, 0.5)),
        ("ud-harmful", "udDev.harmfulPer1k", (B["udDev.harmfulPer1k"] + 1.5, 0.5), (B["udDev.harmfulPer1k"] + 1.6, 0.5)),
        ("ud-accuracy", "udDev.accuracy", (B["udDev.accuracy"] - 0.15, 0.05), (B["udDev.accuracy"] - 0.16, 0.05)),
        ("named-state", "shapesNew.namedState.shape.targetAccuracy", (B["shapesNew.namedState.shape.targetAccuracy"] - 3.0, 2.0),
         (B["shapesNew.namedState.shape.targetAccuracy"] - 3.2, 2.0)),
        ("size", "weights.bytesDeflated", (1.5 * B["weights.bytesDeflated"], 200), (1.5 * B["weights.bytesDeflated"] + 1.0, 200)),
        ("throughput", "throughput.tokensPerSecond", (150000.0, 10000.0), (149990.0, 10000.0)),
        ("imperative-harmful", IMPERATIVE, (B[IMPERATIVE] + 25.0, 5.0), (B[IMPERATIVE] + 25.2, 5.0)),
    ]
    check("S3 covers every guard", sorted(c[0] for c in cases) == sorted(g[0] for g in GUARDS))
    for name, path, good, bad in cases:
        flags_good, flags_bad = flags({path: good}), flags({path: bad})
        check("S3 %s passes at the limit" % name, flags_good[name] and all(flags_good.values()), str(flags_good))
        check("S3 %s fails beyond the limit" % name, not flags_bad[name] and all(v for k, v in flags_bad.items() if k != name), str(flags_bad))
    check("S3 size 1.6x fails, 2x is not the limit", not flags({"weights.bytesDeflated": (1.6 * B["weights.bytesDeflated"], 200)})["size"])
    check("S3 size 1.4x passes", flags({"weights.bytesDeflated": (1.4 * B["weights.bytesDeflated"], 200)})["size"])
    check("S3 throughput below the floor fails", not flags({"throughput.tokensPerSecond": (100000.0, 5000.0)})["throughput"])
    check("S3 imperative: lower than BASE passes", flags({IMPERATIVE: (B[IMPERATIVE] - 20.0, 5.0)})["imperative-harmful"])
    check("S3 imperative: a large rise fails", not flags({IMPERATIVE: (B[IMPERATIVE] + 60.0, 5.0)})["imperative-harmful"])
    # the guards compare with the BASE mean, so a changed BASE moves the limits
    dec = all_ok({"BASE": {"weights.bytesDeflated": (400000.0, 200)}, "CMP2": dict(P(150.0), **{"weights.bytesDeflated": (600000.0, 200)})})
    check("S3 size against the BASE mean (1.5 x 400000 = 600000)", dec["arms"]["CMP2"]["passesAllGuards"])
    dec = all_ok({"BASE": {IMPERATIVE: (80.0, 5.0)}, "CMP2": dict(P(150.0), **{IMPERATIVE: (105.0, 5.0)})})
    check("S3 imperative against the BASE mean (80 + 25 = 105)", dec["arms"]["CMP2"]["passesAllGuards"])
    check("S3 co-primary: lower is allowed",
          all_ok({"CMP2": dict(P(150.0), **{CONFIDENT: (B[CONFIDENT] - 2.0, 1.0)})})["arms"]["CMP2"]["passesAllGuards"])

    # S4: an arm that fails a guard never enters Q, even with the lowest primary; an arm whose interval is not below 0 never enters Q, so it
    # does not set m either (rule B).
    d = all_ok({"CMP2-HDG2-F5": dict(P(100.0), **{"weights.bytesDeflated": (1.7 * B["weights.bytesDeflated"], 200)}), "CMP2": P(150.0)})
    check("S4 guard failure excludes the best arm", "CMP2-HDG2-F5" not in d["qualifying"] and d["lowestPrimary"]["arm"] == "CMP2"
          and d["winner"] == "CMP2" and d["wins"], str(d["qualifying"]))
    d = all_ok({"CMP2-HDG2-F5": dict(P(100.0), **{CONFIDENT: (B[CONFIDENT] + 1.0, 1.0)}), "CMP2-HDG2": P(130.0), "CMP2": P(150.0)})
    check("S4 co-primary excludes the best arm", "CMP2-HDG2-F5" not in d["qualifying"] and d["winner"] == "CMP2-HDG2", str(d["qualifying"]))
    d = all_ok({"CMP2-HDG2-F5": dict(P(100.0), **{IMPERATIVE: (B[IMPERATIVE] + 26.0, 5.0)}), "CMP2": P(150.0)})
    check("S4 imperative guard excludes the best arm", "CMP2-HDG2-F5" not in d["qualifying"] and d["winner"] == "CMP2")
    d = all_ok({"CMP2-HDG2-F5": P(150.0, 150.0), "CMP2": P(200.0)})
    check("S4 an arm with an interval containing 0 does not set m", d["arms"]["CMP2-HDG2-F5"]["passesAllGuards"]
          and not d["arms"]["CMP2-HDG2-F5"]["significant"] and d["qualifying"] == ["CMP2"] and d["lowestPrimary"]["arm"] == "CMP2"
          and d["winner"] == "CMP2", str(d["arms"]["CMP2-HDG2-F5"]["diffVsBase"]))
    d = all_ok({"CMP2-HDG2-F5": P(150.0, 150.0), "CMP2": P(200.0), "CMP2-F5": P(215.0)})
    check("S4 window is taken over Q only", d["withinTenPercent"] == ["CMP2", "CMP2-F5"] and d["winner"] == "CMP2",
          str(d["withinTenPercent"]))

    # S5: nothing qualifies: BASE stays, no winner.
    bad = {"udDev.harmfulPer1k": (B["udDev.harmfulPer1k"] + 3.0, 0.5)}
    d = all_ok({a: dict(P(100.0), **bad) for a in CANDIDATES})
    check("S5 nothing qualifies (every guard fails)", d["qualifying"] == [] and d["candidate"] is None and not d["wins"]
          and d["winner"] == "BASE" and d["passingGuards"] == [], str(d["qualifying"]))
    d = all_ok({})
    check("S5 BASE stays when every arm equals BASE", d["qualifying"] == [] and not d["wins"] and d["winner"] == "BASE"
          and d["passingGuards"] == CANDIDATES and "BASE stays" in d["reason"])
    d = all_ok({a: P(200.0, 80.0) for a in CANDIDATES})
    check("S5 BASE stays when no interval is below 0", d["qualifying"] == [] and d["winner"] == "BASE" and not d["wins"],
          str([d["arms"][a]["diffVsBase"] for a in CANDIDATES]))

    # S6: worse than BASE never wins; one qualifying arm among others that fail.
    d = all_ok({"CMP2": P(260.0), "CMP2-F5": P(280.0), "CMP2-HDG2": P(290.0), "CMP2-HDG2-F5": P(300.0)})
    check("S6 worse than BASE", d["qualifying"] == [] and d["winner"] == "BASE" and d["arms"]["CMP2"]["diffVsBase"]["hi"] > 0.0)
    d = all_ok({"CMP2-HDG2": P(100.0)})
    check("S6 a single qualifying arm wins", d["qualifying"] == ["CMP2-HDG2"] and d["winner"] == "CMP2-HDG2" and d["wins"])

    # S7: the Welch rule is strict (an upper bound of exactly 0 is not an improvement) and uses the frozen function by default.
    d = all_ok({"CMP2": P(200.0)}, welch=lambda a, b: (-1.0, -2.0, 0.0, 8.0, 0.1))
    check("S7 upper bound 0 does not qualify", d["qualifying"] == [] and not d["wins"] and d["winner"] == "BASE")
    d = all_ok({"CMP2": P(200.0)}, welch=lambda a, b: (-1.0, -2.0, -1e-9, 8.0, 0.01))
    check("S7 upper bound below 0 qualifies", d["qualifying"] == CANDIDATES and d["wins"] and d["winner"] in CANDIDATES, str(d["winner"]))
    d = all_ok({"CMP2": P(200.0)}, welch=lambda a, b: (-1.0, -2.0, 1e-9, 8.0, 0.01))
    check("S7 upper bound just above 0 does not qualify", d["qualifying"] == [] and d["winner"] == "BASE")
    d = all_ok({"CMP2": P(200.0)})
    check("S7 frozen welch by default", abs(d["arms"]["CMP2"]["diffVsBase"]["mean"] - (200.0 - 240.0)) < 1e-9)
    check("S7 pairs reported", sorted(d["pairs"]) == ["CMP2-F5 - CMP2", "CMP2-HDG2 - CMP2", "CMP2-HDG2-F5 - CMP2-HDG2"]
          and PRIMARY in d["pairs"]["CMP2-HDG2-F5 - CMP2-HDG2"] and CONFIDENT in d["pairs"]["CMP2-F5 - CMP2"])

    # S8: files, acceptance tests, the report and the command line; missing runs stop before any selection.
    shipped_bytes = b"shipped weights bytes"
    overrides = {"CMP2": P(138.0), "CMP2-F5": P(140.0), "CMP2-HDG2": P(135.0), "CMP2-HDG2-F5": P(130.0)}
    syn = synthetic_runs(overrides)
    figs = figures_of(syn[BASE_ID][seeds.index(SHIPPED_SEED)])
    with tempfile.TemporaryDirectory() as tmp:
        directory, shipped = write_synthetic(tmp, syn, shipped_bytes)
        runs, checks, decision, _ = analyze(directory, shipped, verify=False, shipped_figures=figs)
        bad_checks = [c for c in checks if not c["ok"]]
        check("S8 acceptance passes", not bad_checks, str(bad_checks[:3]))
        check("S8 acceptance covers 5 arms x 8 seeds", len([c for c in checks if c["name"].startswith("b: ")]) == 40, str(len(checks)))
        check("S8 decision", decision is not None and decision["winner"] == "CMP2" and decision["wins"], str(decision and decision["winner"]))
        report = build_report(runs, decision, checks, directory, seeds)
        check("S8 report", "| CMP2-HDG2-F5 |" in report and "Decision: CMP2 WINS" in report and "## Selection table (rule B)" in report
              and "## Acceptance tests" in report and "## Pairwise Welch" in report and "tokens/s >= 150,000" in report
              and "imperative-heading harmful per 1k <= BASE + 25" in report, report[:400])
        out, dec_path = os.path.join(tmp, "report.md"), os.path.join(tmp, "decision.json")
        stdout, sys.stdout = sys.stdout, open(os.devnull, "w", encoding="utf-8")
        try:
            code = main(["--runs", directory, "--shipped-weights", shipped, "--out", out, "--decision", dec_path],
                        _test={"verify": False, "figures": figs})
        finally:
            sys.stdout.close()
            sys.stdout = stdout
        check("S8 main exit", code == 0, str(code))
        check("S8 outputs", os.path.isfile(out) and os.path.isfile(dec_path) and json.load(open(dec_path, encoding="utf-8"))["winner"] == "CMP2")
        os.remove(run_stem(directory, "CMP2-HDG2-F5", 15) + ".json")
        try:
            analyze(directory, shipped, verify=False, shipped_figures=figs)
            check("S8 missing run", False, "no error raised")
        except RunsError as e:
            check("S8 missing run", "CMP2-HDG2-F5-s15.json" in str(e), str(e))

    # S9: each acceptance test fails when its condition is broken, and no decision is made.
    def acceptance_case(name, tweak=None, more=None, shipped_check=shipped_bytes):
        with tempfile.TemporaryDirectory() as tmp:
            directory_, shipped_ = write_synthetic(tmp, synthetic_runs(overrides), shipped_bytes, tweak, more)
            with open(shipped_, "wb") as f:
                f.write(shipped_check)
            _, checks_, decision_, _ = analyze(directory_, shipped_, verify=False, shipped_figures=figs)
            failed = [c["name"] for c in checks_ if not c["ok"]]
            check("S9 %s fails" % name, decision_ is None and len(failed) > 0, str(failed))
            return failed

    failed = acceptance_case("a: shipped weights differ", shipped_check=b"other bytes")
    check("S9 a names the hash", any(n.startswith("a: BASE seed 20260101 weights SHA-256") for n in failed), str(failed))
    failed = acceptance_case("a: figure differs", tweak=lambda l, s, r, h: set_path(r, "udDev.harmful", 95) if (l, s) == ("BASE", SHIPPED_SEED) else None)
    check("S9 a names the figure", any("udDev.harmful" in n for n in failed), str(failed))
    failed = acceptance_case("a: a figure on the new tranches differs",
                             tweak=lambda l, s, r, h: set_path(r, "shapesFollowup.compoundHead2.shape.targetAccuracy", 63.0) if (l, s) == ("BASE", SHIPPED_SEED) else None)
    check("S9 a names the new-tranche figure", any("compoundHead2.shape.targetAccuracy" in n for n in failed), str(failed))
    failed = acceptance_case("b: compound2 adds 1099", more={"compound2": 1099})
    check("S9 b compound2 count", all(any(n.startswith("b: %s seed" % a) for n in failed) for a in CANDIDATES) and not any(n.startswith("b: BASE") for n in failed),
          str(failed[:3]))
    failed = acceptance_case("b: headings2 adds 248", more={"headings2": 248})
    check("S9 b headings2 count", any(n.startswith("b: CMP2-HDG2 seed") for n in failed) and any(n.startswith("b: CMP2-HDG2-F5 seed") for n in failed)
          and not any(n.startswith("b: CMP2 seed") or n.startswith("b: CMP2-F5 seed") for n in failed), str(failed[:3]))
    failed = acceptance_case("b: source missing in the log",
                             tweak=lambda l, s, r, h: h.__setitem__(0, h[0].replace(",compound2:1", "")) if (l, s) == ("CMP2", 13) else None)
    check("S9 b source", any(n.startswith("b: CMP2 seed 13") for n in failed), str(failed))
    failed = acceptance_case("b: BASE lists a new source",
                             tweak=lambda l, s, r, h: h.__setitem__(0, h[0].replace("imperative:1", "imperative:1,headings2:1")) if (l, s) == ("BASE", 14) else None)
    check("S9 b BASE lists the source", any(n.startswith("b: BASE seed 14") for n in failed), str(failed))
    failed = acceptance_case("b: the mix differs (imperative missing)",
                             tweak=lambda l, s, r, h: h.__setitem__(0, h[0].replace(",imperative:1", "")) if (l, s) == ("CMP2-HDG2", 11) else None)
    check("S9 b mix", any(n.startswith("b: CMP2-HDG2 seed 11") for n in failed), str(failed))
    failed = acceptance_case("c: one run has another input hash",
                             tweak=lambda l, s, r, h: r["inputs"].__setitem__("headings2Sha256", "f" * 64) if (l, s) == ("CMP2-HDG2", 14) else None)
    check("S9 c names the input", any(n.startswith("c: inputs.headings2Sha256") for n in failed), str(failed))
    failed = acceptance_case("c: every run has the same but not the frozen hash",
                             tweak=lambda l, s, r, h: r["inputs"].__setitem__("compoundHead2Sha256", "e" * 64))
    check("S9 c frozen value", any(n.startswith("c: inputs.compoundHead2Sha256") for n in failed), str(failed))
    failed = acceptance_case("c: an input hash is absent", tweak=lambda l, s, r, h: r["inputs"].pop("namedStateSha256") if (l, s) == ("BASE", 11) else None)
    check("S9 c absent", any(n.startswith("c: inputs.namedStateSha256") for n in failed), str(failed))

    # S10: invalid runs raise before any selection (feature groups of the weights header, the primary re-derivation, the co-primary sum,
    # the imperative re-derivation, an absent member).
    def invalid_case(name, tweak, needle):
        with tempfile.TemporaryDirectory() as tmp:
            directory_, shipped_ = write_synthetic(tmp, synthetic_runs(overrides), shipped_bytes, tweak)
            try:
                analyze(directory_, shipped_, verify=False, shipped_figures=figs)
                check("S10 %s" % name, False, "no error raised")
            except RunsError as e:
                check("S10 %s" % name, needle in str(e), str(e))

    invalid_case("F5 weights header lacks titleCaseLine", lambda l, s, r, h: r.__setitem__("groups", ["clauseCues"]) if (l, s) == ("CMP2-HDG2-F5", 11) else None,
                 "CMP2-HDG2-F5-s11.json: groups")
    invalid_case("CMP2 weights header names titleCaseLine", lambda l, s, r, h: r.__setitem__("groups", ["clauseCues", "titleCaseLine"]) if (l, s) == ("CMP2", 12) else None,
                 "CMP2-s12.json: groups")
    invalid_case("primary does not match the counts", lambda l, s, r, h: set_path(r, PRIMARY, 1.0) if (l, s) == ("CMP2", 13) else None,
                 "re-derived from the counts")
    invalid_case("primary matches pooledPrimary but not the four tranches",
                 lambda l, s, r, h: (set_path(r, PRIMARY, 100.0), set_path(r, "shapesFollowup.pooledPrimary.harmful", 47.0)) if (l, s) == ("CMP2-F5", 14) else None,
                 "CMP2-F5-s14.json: pooledPrimary decisions/harmful")
    invalid_case("confident-wrong total does not match", lambda l, s, r, h: set_path(r, "shapesFollowup.pooledPrimary.confidentWrongTotal", 99) if (l, s) == ("BASE", 14) else None,
                 "confidentWrongTotal")
    invalid_case("the imperative rate does not match the counts", lambda l, s, r, h: set_path(r, IMPERATIVE, 1.0) if (l, s) == ("CMP2-HDG2", 15) else None,
                 "imperative rate")
    invalid_case("imperative counts do not match the kinds", lambda l, s, r, h: set_path(r, "shapesFollowup.imperativeHeadings.decisions", 61) if (l, s) == ("CMP2", 16) else None,
                 "imperativeHeadings decisions/harmful")
    invalid_case("a required metric is absent", lambda l, s, r, h: r["shapesNew"].pop("namedState") if (l, s) == ("CMP2-HDG2", 20260101) else None,
                 "shapesNew.namedState.shape.targetAccuracy is missing")
    invalid_case("the follow-up block is absent", lambda l, s, r, h: r.pop("shapesFollowup") if (l, s) == ("CMP2-F5", 17) else None,
                 "required metric shapesFollowup.pooledPrimary.harmfulPer1k is missing")
    invalid_case("wrong seed field", lambda l, s, r, h: r.__setitem__("seed", 99) if (l, s) == ("BASE", 12) else None, "seed field")
    invalid_case("wrong sources field", lambda l, s, r, h: r.__setitem__("sources", ["compound"]) if (l, s) == ("CMP2", 11) else None, "sources are")

    # S11: a missing training log fails the acceptance test (it is not skipped); an acceptance failure writes the report, no decision, exit 1.
    with tempfile.TemporaryDirectory() as tmp:
        directory_, shipped_ = write_synthetic(tmp, synthetic_runs(overrides), shipped_bytes)
        os.remove(run_stem(directory_, "CMP2", 11) + ".train.txt")
        _, checks_, decision_, _ = analyze(directory_, shipped_, verify=False, shipped_figures=figs)
        check("S11 missing log", decision_ is None and any("CMP2 seed 11 training line" in c["name"] and not c["ok"] for c in checks_))
        out, dec_path = os.path.join(tmp, "r.md"), os.path.join(tmp, "d.json")
        stdout, stderr = sys.stdout, sys.stderr
        sys.stdout, sys.stderr = open(os.devnull, "w", encoding="utf-8"), open(os.devnull, "w", encoding="utf-8")
        try:
            code = main(["--runs", directory_, "--shipped-weights", shipped_, "--out", out, "--decision", dec_path],
                        _test={"verify": False, "figures": figs})
        finally:
            sys.stdout.close()
            sys.stderr.close()
            sys.stdout, sys.stderr = stdout, stderr
        check("S11 exit 1 without a decision", code == 1 and os.path.isfile(out) and not os.path.exists(dec_path), str(code))
    # a no-winner outcome is exit 0 with a decision file that says BASE stays
    with tempfile.TemporaryDirectory() as tmp:
        directory_, shipped_ = write_synthetic(tmp, synthetic_runs({}), shipped_bytes)
        out, dec_path = os.path.join(tmp, "r.md"), os.path.join(tmp, "d.json")
        stdout, sys.stdout = sys.stdout, open(os.devnull, "w", encoding="utf-8")
        try:
            code = main(["--runs", directory_, "--shipped-weights", shipped_, "--out", out, "--decision", dec_path],
                        _test={"verify": False, "figures": figs})
        finally:
            sys.stdout.close()
            sys.stdout = stdout
        check("S11 no winner is exit 0 and BASE stays", code == 0 and json.load(open(dec_path, encoding="utf-8"))["winner"] == "BASE", str(code))
        check("S11 no-winner report", "Decision: BASE STAYS" in open(out, encoding="utf-8").read())

    print("selftest: %s" % ("ok" if not failures else "FAILED"))
    for f in failures:
        print("  - " + f)
    return 1 if failures else 0


# ---------------------------------------------------------------------------------------------------------------------

def main(argv=None, _test=None):
    """The command line. `_test` ({"verify", "figures"}) is for the self-test only: it lets the self-test drive this function with
    synthetic runs (no frozen-file check, the figures of the synthetic BASE run); the command line has no such option."""
    ap = argparse.ArgumentParser(description="Compound-head and heading follow-up experiment: acceptance tests, analysis and selection "
                                             "(standard library only).")
    ap.add_argument("--runs", help="the folder of the driver runs (all five arms)")
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
        runs, checks, decision, seeds = analyze(args.runs, args.shipped_weights, verify=test.get("verify", True),
                                                shipped_figures=test.get("figures"))
    except RunsError as e:
        print("analyze_followup: cannot select: %s" % e, file=sys.stderr)
        return 1
    report = build_report(runs, decision, checks, args.runs, seeds)
    print(report)
    if args.out:
        with open(args.out, "w", encoding="utf-8", newline="\n") as f:
            f.write(report + "\n")
    if decision is None:
        print("analyze_followup: an acceptance test failed: no selection is made", file=sys.stderr)
        return 1
    if args.decision:
        with open(args.decision, "w", encoding="utf-8", newline="\n") as f:
            json.dump(decision, f, indent=2, sort_keys=True)
            f.write("\n")
    return 0


if __name__ == "__main__":
    sys.exit(main())

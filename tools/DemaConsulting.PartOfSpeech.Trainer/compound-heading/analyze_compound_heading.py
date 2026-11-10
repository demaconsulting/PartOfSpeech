#!/usr/bin/env python3
"""Compound-head and heading data experiment: acceptance tests, analysis and selection (standard library only; pre-registered, frozen with the RUN-LOG).

usage:
    python analyze_compound_heading.py --runs DIR [--shipped-weights FILE] [--out report.md] [--decision decision.json]
    python analyze_compound_heading.py --selftest

`DIR` is the folder of one run of the driver compound-heading/run_arms.ps1 (all four arms in one folder). The driver writes one result
file per run (`<DIR>\\<arm>-s<seed>.json`, schema 1; the interface contract is in context-features/run_arms.ps1 plus the member
`shapesNew` of `eval-arm`) and the training log next to it (`<arm>-s<seed>.train.txt`). The arms (arms.json of this folder):

    arm          feature groups              sources added to the shipped mix (open x2, MASC, silver, imperative; weight 1 each)
    BASE         clauseCues                  none (the shipped model; baseline)
    CMP          clauseCues                  compound
    CMP-HDG      clauseCues                  compound, headings
    CMP-HDG-F5   clauseCues, titleCaseLine   compound, headings

The only thing taken from the frozen script of the context-feature experiment (context-features/analyze_arms.py) is `welch_diff` (the
Welch-Satterthwaite t interval, 95 percent); the file is loaded from its place by path, without editing it, and only after its SHA-256 has
been checked against the frozen value. Every other function of this script is its own. The self-test checks the frozen t quantile against
published values.

Acceptance tests (run before the rule is applied; a failure stops without a selection, exit code 1, no decision file):
  (a) BASE at seed 20260101 has weights byte-identical to the shipped weights.bin (the weights SHA-256 of its result file, and its .bin file
      when present) and the figures of the shipped model on the development sets (the same figures as the smoke scoring of the shipped
      weights before any arm was trained: compound-head 60.0% with 39 harmful of 100 decisions and 5 wrong at >= 0.95, headings 84.2%
      with 4 of 78 and 2, named-state 90.0% with 3 of 37, pooled primary 241.57 per 1k and 7 wrong at >= 0.95, hard tranche 170.0, standard
      tranche 15.0, open dev 91 harmful of 4,451 decisions, UD-EWT dev 94 of 4,570);
  (b) from the line `Training on N sentences (...), sources=...` of every `<arm>-s<seed>.train.txt`: BASE trains on the shipped mix
      (open:2, masc:1, silver:1, imperative:1) and neither new source; every other arm lists exactly the mix plus `compound:1` (CMP) or
      `compound:1,headings:1` (the two other arms) and trained on exactly 742 (compound) and 375 (headings) more sentences than the BASE run
      of the same seed; the result of every run names the feature groups of its arm (the `groups` of the weights header, the driver's
      `features`, and `sources`);
  (c) the `inputs.*Sha256` fields of all runs agree and equal the frozen hashes of the eight development files;
  (d) the files named in FROZEN_FILES (the driver, arms.json, the frozen analyzer, the runner, the two data files and their source tables)
      and the files of the state before any adoption (training.json, the shipped weights.bin) have their frozen SHA-256.

Metrics (per run, from the `eval-arm` JSON; a harmful error is a swapped noun/verb class at confidence >= 0.45 on a noun-verb decision):
  PRIMARY      = shapesNew.pooledPrimary.harmfulPer1k = 1000 x (harmful_compound + harmful_headings) / (decisions_compound +
                 decisions_headings); the script re-derives it from the counts and checks that it is equal (a mismatch is an invalid run);
  CO-PRIMARY   = shapesNew.compoundHead.shape.confidentWrong + shapesNew.headings.shape.confidentWrong (wrong at confidence >= 0.95,
                 summed over the two tranches); the arm's five-seed mean must be <= the BASE mean (it must not increase).

Selection rule (the literal pre-registered text; nothing is tuned afterwards):

1. Primary = the five-seed mean of the pooled harmful per 1k over `n-compound-head-ambiguous` and `heading-titlecase`. Lower is better.
2. Guards, each arm against the BASE five-seed mean (the throughput guard is absolute; it uses the median over the seeds of each run's median):
   hard tranche harmful per 1k <= BASE + 10; standard tranche <= BASE + 5; open dev harmful per 1k <= BASE + 1.5; UD-EWT dev harmful per 1k
   <= BASE + 1.5; UD-EWT dev accuracy >= BASE - 0.15 points; `n-named-state` target accuracy >= BASE - 3 points; deflated weights bytes
   <= 1.5 x BASE; throughput >= 150,000 tokens/s.
3. Q = the arms CMP, CMP-HDG and CMP-HDG-F5 that pass every guard and the co-primary. If Q is empty, BASE stays.
4. m = the lowest primary mean in Q. S = the arms of Q with primary <= 1.10 x m (within 10 percent relative).
5. Candidate = the arm of S with the fewest additions in the order CMP (1 source) < CMP-HDG (2 sources) < CMP-HDG-F5 (2 sources and 1 feature
   group); ties: the lowest primary.
6. The candidate wins only if the upper bound of the Welch 95 percent interval (Welch-Satterthwaite degrees of freedom) of (candidate - BASE)
   on the primary is below 0. There is no fallback to another arm. Otherwise BASE stays and nothing ships.

Reported, not gated: the means and standard deviations of every arm, the wrong-at->=0.95 counts, the pairwise Welch intervals CMP-HDG against
CMP and CMP-HDG-F5 against CMP-HDG on the primary and the co-primary, the tranche diagnostics (the heading kinds, named-state, hard and
standard accuracy, open and gold v2 dev, NLL, ECE, temperature, rows, bytes, training seconds).

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

# Text files frozen by the pre-registration commit: repository-relative path -> SHA-256 of the committed text (line endings LF; a
# working-tree copy with CRLF, as .gitattributes gives the .cmd files, is normalized before hashing).
FROZEN_FILES = {
    T + "compound-heading/run_arms.ps1": "3d20022cc7a6d7e1ddefcc5abaeb0a740fb8f186c63ff25e73d50187b57cbcbd",
    T + "compound-heading/arms.json": "7fd58517b64127fe3892e928939d6896d8a0ab5e499ce696f6cb781a01bcc4cf",
    REL_FROZEN_ANALYZER: "3d52825384b440c5786e1b241fc6f814e4e47a63f155414e0d6450107d33e494",
    T + "imperative-data/runner-base.cmd": "549a2c6ed8d1eaf00b30551fae14e546c51023dd79fe3a77810f39a1fe79ff3e",
    "data/compound/compound.tsv": "eb8c4708ba95fb370c885110e10eda0f1d826c5922876bd66e69389894bd5e3b",
    "data/compound/sources.tsv": "8b861dd3ff2d5a3fd5e741f5fe2a906807f99afa562d7e294d4011d93ef15e02",
    "data/headings/headings.tsv": "5931072928829e1911468173a181761ba001c4d7f16a8492391404395649124c",
    "data/headings/sources.tsv": "428a73f0581ec168cdef63d885cd6e001e925813b0a0d0dd1148f79636935aff",
}

# Files of the state before any adoption: an adoption changes both by design, so the analysis is made before it (and the self-test does
# not check them). The weights file is hashed as bytes (it is binary), training.json as text.
PRE_ADOPTION_TEXT = {
    T + "training.json": "96ad8178b5f0bb621ee0723bc1f32da7ef5859218c14509ce6005d8a8354ca3f",
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
}

SHIPPED_SEED = 20260101
PRIMARY = "shapesNew.pooledPrimary.harmfulPer1k"
CONFIDENT = "derived.confidentWrong"  # derived per run by load_runs: the sum over the two tranches
CW_COMPOUND = "shapesNew.compoundHead.shape.confidentWrong"
CW_HEADINGS = "shapesNew.headings.shape.confidentWrong"
TOLERANCE = 1e-9
WINDOW = 1.10

# The figures of the shipped model (BASE, seed 20260101) that the BASE run of the shipped seed must reproduce (acceptance test a):
# (path, expected value, decimals). From the smoke scoring of the shipped weights (eval-arm) before any arm was trained.
SHIPPED_FIGURES = [
    ("shapesNew.compoundHead.shape.targetAccuracy", 60.0, 1),
    ("shapesNew.compoundHead.shape.decisions", 100, 0),
    ("shapesNew.compoundHead.shape.harmful", 39, 0),
    (CW_COMPOUND, 5, 0),
    ("shapesNew.headings.shape.targetAccuracy", 84.2, 1),
    ("shapesNew.headings.shape.decisions", 78, 0),
    ("shapesNew.headings.shape.harmful", 4, 0),
    (CW_HEADINGS, 2, 0),
    ("shapesNew.namedState.shape.targetAccuracy", 90.0, 1),
    ("shapesNew.namedState.shape.decisions", 37, 0),
    ("shapesNew.namedState.shape.harmful", 3, 0),
    (PRIMARY, 241.57, 2),
    (CONFIDENT, 7, 0),
    ("shapes.hard.pooled.harmfulPer1k", 170.0, 1),
    ("shapes.standard.pooled.harmfulPer1k", 15.0, 1),
    ("openDev.harmful", 91, 0),
    ("openDev.nounVerbDecisions", 4451, 0),
    ("udDev.harmful", 94, 0),
    ("udDev.decisions", 4570, 0),
]

# The shipped training mix (training.json before any adoption) and the sentences each new source adds at weight 1 (the sentence blocks
# of the data files; the self-test checks the counts against the files).
BASE_MIX = ["open:2", "masc:1", "silver:1", "imperative:1"]
ADDED_SENTENCES = {"compound": 742, "headings": 375}
DATA_FILES = {"compound": "data/compound/compound.tsv", "headings": "data/headings/headings.tsv"}

# id, feature groups, added sources, role. Additions = sources + feature groups beyond BASE (the preference order of the rule).
ARMS = [
    {"id": "BASE", "features": ["clauseCues"], "sources": [], "role": "baseline"},
    {"id": "CMP", "features": ["clauseCues"], "sources": ["compound"], "role": "candidate"},
    {"id": "CMP-HDG", "features": ["clauseCues"], "sources": ["compound", "headings"], "role": "candidate"},
    {"id": "CMP-HDG-F5", "features": ["clauseCues", "titleCaseLine"], "sources": ["compound", "headings"], "role": "candidate"},
]
BASE_ID = "BASE"
CANDIDATES = ["CMP", "CMP-HDG", "CMP-HDG-F5"]


def additions(arm):
    base = ARMS[0]
    return len(arm["sources"]) + len(set(arm["features"]) - set(base["features"]))


# (path, label, decimals, required)
GROUPS = [
    ("Primary, co-primary and the two new tranches", [
        (PRIMARY, "pooled harmful per 1k (primary)", 1, True),
        (CONFIDENT, "wrong at >= 0.95, both tranches (co-primary)", 2, True),
        ("shapesNew.pooledPrimary.targetAccuracy", "pooled target acc %", 2, False),
        ("shapesNew.compoundHead.shape.targetAccuracy", "compound-head target acc %", 1, False),
        ("shapesNew.compoundHead.shape.harmfulPer1k", "compound-head harmful per 1k", 1, False),
        (CW_COMPOUND, "compound-head wrong at >= 0.95", 2, True),
        ("shapesNew.compoundHead.shape.controlAccuracy", "compound-head control acc %", 1, False),
        ("shapesNew.headings.shape.targetAccuracy", "headings target acc %", 1, False),
        ("shapesNew.headings.shape.harmfulPer1k", "headings harmful per 1k", 1, False),
        (CW_HEADINGS, "headings wrong at >= 0.95", 2, True),
        ("shapesNew.headings.shape.controlAccuracy", "headings control acc %", 1, False),
    ]),
    ("Heading kinds (diagnostics)", [
        ("shapesNew.headings.kinds.noun.targetAccuracy", "noun target acc %", 1, False),
        ("shapesNew.headings.kinds.noun.harmfulPer1k", "noun harmful per 1k", 1, False),
        ("shapesNew.headings.kinds.noun.confidentWrong", "noun wrong at >= 0.95", 2, False),
        ("shapesNew.headings.kinds.imperative.targetAccuracy", "imperative target acc %", 1, False),
        ("shapesNew.headings.kinds.imperative.harmfulPer1k", "imperative harmful per 1k", 1, False),
        ("shapesNew.headings.kinds.imperative.confidentWrong", "imperative wrong at >= 0.95", 2, False),
        ("shapesNew.headings.kinds.proper.controlAccuracy", "proper-noun control acc %", 1, False),
    ]),
    ("Guarded measures and named-state", [
        ("shapes.hard.pooled.harmfulPer1k", "hard harmful per 1k", 1, True),
        ("shapes.hard.pooled.targetAccuracy", "hard target acc %", 2, False),
        ("shapes.standard.pooled.harmfulPer1k", "standard harmful per 1k", 1, True),
        ("shapes.standard.pooled.targetAccuracy", "standard target acc %", 2, False),
        ("shapesNew.namedState.shape.targetAccuracy", "named-state target acc %", 1, True),
        ("shapesNew.namedState.shape.harmfulPer1k", "named-state harmful per 1k", 1, False),
        ("shapesNew.namedState.shape.confidentWrong", "named-state wrong at >= 0.95", 2, False),
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
]
WELCH_METRICS = [PRIMARY, CONFIDENT, "shapes.hard.pooled.harmfulPer1k", "shapes.standard.pooled.harmfulPer1k", "openDev.harmfulPer1k",
                 "udDev.harmfulPer1k", "udDev.accuracy", "shapesNew.namedState.shape.targetAccuracy"]
PAIRS = [("CMP-HDG", "CMP"), ("CMP-HDG-F5", "CMP-HDG")]


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
    needed = list(dict.fromkeys(required + [CW_COMPOUND, CW_HEADINGS, "shapesNew.pooledPrimary.confidentWrongTotal",
                                             "shapesNew.compoundHead.shape.harmful", "shapesNew.compoundHead.shape.decisions",
                                             "shapesNew.headings.shape.harmful", "shapesNew.headings.shape.decisions"]))
    missing = [m for m in needed if m != CONFIDENT and get_path(run, m) is None]
    for m in missing:
        errors.append("%s: required metric %s is missing" % (name, m))
    if missing:
        return errors
    # the derived members: the co-primary, and the re-derivation of the primary from the counts
    cw = get_path(run, CW_COMPOUND) + get_path(run, CW_HEADINGS)
    if abs(cw - get_path(run, "shapesNew.pooledPrimary.confidentWrongTotal")) > TOLERANCE:
        errors.append("%s: pooledPrimary.confidentWrongTotal %r is not the sum %r of the two tranches" %
                      (name, get_path(run, "shapesNew.pooledPrimary.confidentWrongTotal"), cw))
    decisions = get_path(run, "shapesNew.compoundHead.shape.decisions") + get_path(run, "shapesNew.headings.shape.decisions")
    harmful = get_path(run, "shapesNew.compoundHead.shape.harmful") + get_path(run, "shapesNew.headings.shape.harmful")
    rederived = 1000.0 * harmful / decisions if decisions else 0.0
    if abs(rederived - get_path(run, PRIMARY)) > 1e-6:
        errors.append("%s: the primary %r is not the value %r re-derived from the counts" % (name, get_path(run, PRIMARY), rederived))
    run["derived"] = {"confidentWrong": cw, "primaryRederived": rederived}
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


def pick_candidate(within, arms):
    """The arm of `within` with the fewest additions; ties: the lowest primary mean; then the order of the arms."""
    return min(within, key=lambda a: (arms[a]["additions"], arms[a]["primaryMean"], CANDIDATES.index(a) if a in CANDIDATES else 99))


def decide(runs, welch=None):
    """Applies the pre-registered rule to the loaded runs (a pure function of the numbers). Returns the decision dictionary.

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
        arms[label] = info
    pairs = {}
    for a, b in PAIRS:
        if a in runs and b in runs:
            entry = {}
            for metric in (PRIMARY, CONFIDENT):
                d, lo, hi, df, p = welch(column(runs[a], metric), column(runs[b], metric))
                entry[metric] = {"mean": d, "lo": lo, "hi": hi, "df": df, "p": p}
            pairs["%s - %s" % (a, b)] = entry

    qualifying = [a for a in CANDIDATES if arms[a]["passesAllGuards"]]
    decision = {"schema": 1, "baseline": BASE_ID, "primaryMetric": PRIMARY, "seeds": len(base_primary), "arms": arms, "pairs": pairs,
                "qualifying": qualifying, "lowestPrimary": None, "withinTenPercent": [], "candidate": None,
                "candidateInterval": None, "wins": False, "winner": BASE_ID}
    if not qualifying:
        decision["reason"] = "No candidate arm passes every guard and the co-primary: BASE stays, nothing ships."
        return decision

    lowest = min(qualifying, key=lambda a: (arms[a]["primaryMean"], CANDIDATES.index(a)))
    m = arms[lowest]["primaryMean"]
    within = [a for a in qualifying if arms[a]["primaryMean"] <= WINDOW * m + TOLERANCE]
    candidate = pick_candidate(within, arms)
    interval = arms[candidate]["diffVsBase"]
    wins = interval["hi"] < 0.0
    decision.update({"lowestPrimary": {"arm": lowest, "value": m}, "withinTenPercent": within, "candidate": candidate,
                     "candidateInterval": interval, "wins": wins, "winner": candidate if wins else BASE_ID})
    if wins:
        decision["reason"] = ("%s wins: it has the fewest additions among the passing arms within 10%% of the lowest primary, and the "
                              "Welch 95%% interval of (arm - BASE) is [%.2f, %.2f], entirely below 0." % (candidate, interval["lo"], interval["hi"]))
    else:
        decision["reason"] = ("BASE stays, nothing ships: the candidate %s has the interval [%.2f, %.2f] of (arm - BASE), which does not "
                              "lie entirely below 0." % (candidate, interval["lo"], interval["hi"]))
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
    out = ["# Compound-head and heading data: analysis", "",
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
    out += ["## Selection table", "",
            "Primary = pooled harmful per 1k over `n-compound-head-ambiguous` and `heading-titlecase` (lower is better); co-primary = wrong at "
            ">= 0.95 summed over the two tranches. Delta guards use the mean over the seeds against BASE (primary mean %.2f, wrong at >= 0.95 "
            "mean %.2f); the size guard is 1.5 x the BASE mean; the throughput guard is absolute (median over the seeds of each run's median)." %
            (base["primaryMean"], base["confidentWrongMean"]), ""]
    rows = [[BASE_ID, "%.2f ± %.2f" % (base["primaryMean"], base["primarySd"]),
             "%.2f ± %.2f" % (base["confidentWrongMean"], base["confidentWrongSd"])] + ["-"] * len(GUARDS) + ["baseline"]]
    for a in order:
        if a == BASE_ID:
            continue
        info = decision["arms"][a]
        rows.append([a, "%.2f ± %.2f" % (info["primaryMean"], info["primarySd"]),
                     "%.2f ± %.2f" % (info["confidentWrongMean"], info["confidentWrongSd"])] + [guard_cell(g) for g in info["guards"]] +
                    ["PASS" if info["passesAllGuards"] else "FAIL"])
    out += [table(["arm", "primary (mean ± sd)", "wrong at >= 0.95 (mean ± sd)"] + [g[1] for g in GUARDS] + ["all pass"], rows), ""]

    out += ["## Welch 95% interval of (arm - BASE)", "",
            "Difference of the means with the Welch-Satterthwaite t interval over the seeds (seeds measure training noise only).", ""]
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
                     "baseline" if a == BASE_ID else "candidate"])
    out += [table(["arm", "additions", "primary", "wrong at >= 0.95 (mean)", "passes every guard and the co-primary (Q)", "role"], rows), ""]
    out += ["- Arms in Q: %s" % (", ".join(decision["qualifying"]) or "none")]
    if decision["lowestPrimary"]:
        lp = decision["lowestPrimary"]
        out += ["- Lowest primary in Q: %s (%.2f); within 10%% (primary <= %.2f): %s" %
                (lp["arm"], lp["value"], WINDOW * lp["value"], ", ".join(decision["withinTenPercent"])),
                "- Candidate (fewest additions, then lowest primary): %s" % decision["candidate"]]
        ci = decision["candidateInterval"]
        out += ["- Welch 95%% interval of (%s - BASE) on the primary: %+.2f [%+.2f, %+.2f], df %.2f, p %.4f" %
                (decision["candidate"], ci["mean"], ci["lo"], ci["hi"], ci["df"], ci["p"])]
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
    problems = verify_frozen(table={**FROZEN_FILES, **PRE_ADOPTION_TEXT}, raw_table=PRE_ADOPTION_RAW) if verify else []
    if problems:
        raise RunsError("; ".join(problems))
    seeds, spec_arms = arms_spec()
    for arm in ARMS:
        found = next((a for a in spec_arms if a["id"] == arm["id"]), None)
        if found is None or sorted(found["features"]) != sorted(arm["features"]) or sorted(found["sources"]) != sorted(arm["sources"]):
            raise RunsError("arms.json does not define the arm %s as this script expects" % arm["id"])
    runs = load_runs(directory, seeds)
    checks = acceptance(runs, directory, seeds, shipped_path, shipped_figures, added)
    if verify:
        checks.insert(0, {"name": "frozen files have their frozen SHA-256 (%d text files, %d raw)" %
                          (len(FROZEN_FILES) + len(PRE_ADOPTION_TEXT), len(PRE_ADOPTION_RAW)), "ok": True, "detail": "checked"})
    decision = decide(runs) if all(c["ok"] for c in checks) else None
    return runs, checks, decision, seeds


# ---------------------------------------------------------------------------------------------------------------------
# Self-test with synthetic run results
# ---------------------------------------------------------------------------------------------------------------------

SQ = math.sqrt(2.5)
BASE_SPEC = {
    # BASE means (the seed offsets give an exact mean and sample sd); the primary and the co-primary are spread over the two tranches
    PRIMARY: (240.0, 6.0), CONFIDENT: (7.0, 1.0), "shapes.hard.pooled.harmfulPer1k": (170.0, 8.0),
    "shapes.hard.pooled.targetAccuracy": (77.0, 1.0), "shapes.standard.pooled.harmfulPer1k": (15.0, 1.0),
    "shapes.standard.pooled.targetAccuracy": (97.8, 0.2), "shapesNew.namedState.shape.targetAccuracy": (90.0, 2.0),
    "shapesNew.namedState.shape.harmfulPer1k": (80.0, 5.0), "shapesNew.namedState.shape.confidentWrong": (0.0, 0.0),
    "openDev.accuracy": (96.65, 0.05), "openDev.harmfulPer1k": (20.0, 0.5), "openDev.nll": (0.11, 0.001), "openDev.ece": (0.24, 0.02),
    "goldDev.accuracy": (94.84, 0.2), "goldDev.harmfulPer1k": (26.0, 1.0), "udDev.accuracy": (92.95, 0.05), "udDev.harmfulPer1k": (20.5, 0.5),
    "temperature": (9.5, 0.1), "weights.rows": (122088, 100), "weights.bytesRaw": (1157499, 500), "weights.bytesDeflated": (521695, 200),
    "throughput.tokensPerSecond": (400000.0, 10000.0), "trainWallSeconds": (60.0, 1.0),
}
SYNTH_FIGURES = {"openDev.harmful": 91, "openDev.nounVerbDecisions": 4451, "udDev.harmful": 94, "udDev.decisions": 4570,
                 "shapesNew.compoundHead.shape.targetAccuracy": 60.0, "shapesNew.headings.shape.targetAccuracy": 84.2,
                 "shapesNew.namedState.shape.decisions": 37, "shapesNew.namedState.shape.harmful": 3}
SYNTH_BASE_SENTENCES = 61680


def set_path(obj, path, value):
    parts = path.split(".")
    for p in parts[:-1]:
        obj = obj.setdefault(p, {})
    obj[parts[-1]] = value


def synthetic_runs(overrides, labels=("BASE", "CMP", "CMP-HDG", "CMP-HDG-F5"), seeds=None):
    """overrides: {arm id: {metric path: (mean, sd)}}. Values are mean + sd * (-2..2)/sqrt(2.5): an exact mean and sample sd."""
    seeds = seeds or arms_spec()[0]
    offsets = [-2.0, -1.0, 0.0, 1.0, 2.0]
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
                set_path(run, path, mean + offsets[(si + ai) % 5] / SQ * sd)
            for path, value in SYNTH_FIGURES.items():
                set_path(run, path, value)
            # spread the primary and the co-primary over the two tranches (500 decisions each) so that the re-derivation holds
            p, c = run["shapesNew"]["pooledPrimary"]["harmfulPer1k"], run["derived"]["confidentWrong"]
            for tranche in ("compoundHead", "headings"):
                shape = run["shapesNew"].setdefault(tranche, {}).setdefault("shape", {})
                shape.update({"decisions": 500, "harmful": p / 2.0, "confidentWrong": c / 2.0})
            run["shapesNew"]["pooledPrimary"].update({"decisions": 1000, "harmful": p, "confidentWrongTotal": c})
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
        return get_path(run, CW_COMPOUND) + get_path(run, CW_HEADINGS) if path == CONFIDENT else get_path(run, path)

    return [(path, round(value(path), 3), 3) for path, _, _ in SHIPPED_FIGURES]


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

    # Frozen files: the real files match; a change is found; CRLF is normalized; the raw table is not.
    check("frozen files", verify_frozen() == [], str(verify_frozen()))
    check("frozen mismatch", len(verify_frozen(table={REL_FROZEN_ANALYZER: "0" * 64})) == 1)
    check("frozen missing", len(verify_frozen(table={"no/such/file": "0" * 64})) == 1)
    check("frozen raw mismatch", len(verify_frozen(table={}, raw_table={REL_FROZEN_ANALYZER: "0" * 64})) == 1)
    check("crlf normalized", sha256_lf(b"a\r\nb\n") == sha256_lf(b"a\nb\n") and sha256_lf(b"a\nb") != sha256_lf(b"a\nc"))
    check("arms.json seeds", seeds == [20260101, 11, 12, 13, 14], str(seeds))
    for arm in ARMS:
        found = next((a for a in spec_arms if a["id"] == arm["id"]), None)
        check("arms.json %s" % arm["id"], found is not None and found["features"] == arm["features"] and found["sources"] == arm["sources"], str(found))
    check("arms.json has no other arm", [a["id"] for a in spec_arms] == [a["id"] for a in ARMS])
    check("additions order", [additions(a) for a in ARMS] == [0, 1, 2, 3], str([additions(a) for a in ARMS]))
    for source, rel in DATA_FILES.items():
        check("sentence count of %s" % source, count_sentences(os.path.join(REPO, *rel.split("/"))) == ADDED_SENTENCES[source],
              "%d" % count_sentences(os.path.join(REPO, *rel.split("/"))))
    check("training line parse", parse_training_line("x\nTraining on 61,680 sentences (1,234 tokens per epoch), sources=open:2,masc:1 epochs=6\n")
          == (61680, ["open:2", "masc:1"]))
    check("training line parse (no separator)", parse_training_line("Training on 62422 sentences (1 tokens per epoch), sources=open:2")
          == (62422, ["open:2"]))
    check("training line parse (none)", parse_training_line("trained on 5 sentences") is None)

    B = {path: value[0] for path, value in BASE_SPEC.items()}

    def P(x, sd=4.0):
        return {PRIMARY: (x, sd)}

    def all_ok(overrides, labels=("BASE", "CMP", "CMP-HDG", "CMP-HDG-F5")):
        return decide(synthetic_runs(overrides, labels))

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

    # S1: all three pass; the lowest is CMP-HDG-F5, CMP is within 10 percent and has the fewest additions: CMP wins when significant.
    d = all_ok({"CMP": P(120.0), "CMP-HDG": P(115.0), "CMP-HDG-F5": P(110.0)})
    check("S1 qualifying", d["qualifying"] == ["CMP", "CMP-HDG", "CMP-HDG-F5"], str(d["qualifying"]))
    check("S1 window", d["withinTenPercent"] == ["CMP", "CMP-HDG", "CMP-HDG-F5"] and d["lowestPrimary"]["arm"] == "CMP-HDG-F5")
    check("S1 candidate CMP wins", d["candidate"] == "CMP" and d["wins"] and d["winner"] == "CMP", str(d["candidate"]))

    # S2: the 10 percent window: 110.0 is exactly 1.10 x 100 (inside, CMP preferred); 110.5 is outside (the next in order inside wins).
    base300 = {"BASE": P(300.0)}
    d = all_ok(dict(base300, **{"CMP-HDG-F5": P(100.0), "CMP-HDG": P(105.0), "CMP": P(110.0)}))
    check("S2 boundary inside", d["withinTenPercent"] == ["CMP", "CMP-HDG", "CMP-HDG-F5"] and d["candidate"] == "CMP", str(d["withinTenPercent"]))
    d = all_ok(dict(base300, **{"CMP-HDG-F5": P(100.0), "CMP-HDG": P(105.0), "CMP": P(110.5)}))
    check("S2 outside", d["withinTenPercent"] == ["CMP-HDG", "CMP-HDG-F5"] and d["candidate"] == "CMP-HDG" and d["wins"], str(d["withinTenPercent"]))
    d = all_ok(dict(base300, **{"CMP-HDG-F5": P(100.0), "CMP-HDG": P(110.5), "CMP": P(111.0)}))
    check("S2 both outside", d["withinTenPercent"] == ["CMP-HDG-F5"] and d["candidate"] == "CMP-HDG-F5" and d["wins"], str(d["withinTenPercent"]))
    d = all_ok(dict(base300, **{"CMP-HDG-F5": P(100.0), "CMP-HDG": P(105.0), "CMP": P(99.0)}))
    check("S2 CMP lowest", d["lowestPrimary"]["arm"] == "CMP" and d["candidate"] == "CMP")
    arms_tie = {"X": {"additions": 2, "primaryMean": 5.0}, "Y": {"additions": 2, "primaryMean": 4.0}, "Z": {"additions": 3, "primaryMean": 1.0}}
    check("S2 tie of additions: the lowest primary", pick_candidate(["X", "Y", "Z"], arms_tie) == "Y")

    # S3: each guard (and the co-primary) passes exactly at its limit and fails just beyond it.
    def flags(arm_overrides):
        dec = all_ok({"CMP": dict(P(150.0), **arm_overrides)})
        return {g["name"]: g["pass"] for g in dec["arms"]["CMP"]["guards"]}

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
    ]
    check("S3 covers every guard", sorted(c[0] for c in cases) == sorted(g[0] for g in GUARDS))
    for name, path, good, bad in cases:
        flags_good, flags_bad = flags({path: good}), flags({path: bad})
        check("S3 %s passes at the limit" % name, flags_good[name] and all(flags_good.values()), str(flags_good))
        check("S3 %s fails beyond the limit" % name, not flags_bad[name] and all(v for k, v in flags_bad.items() if k != name), str(flags_bad))
    check("S3 size 1.6x fails, 2x is not the limit", not flags({"weights.bytesDeflated": (1.6 * B["weights.bytesDeflated"], 200)})["size"])
    check("S3 size 1.4x passes", flags({"weights.bytesDeflated": (1.4 * B["weights.bytesDeflated"], 200)})["size"])
    # the throughput guard uses the median over the seeds: a slow arm fails
    check("S3 throughput below the floor fails", not flags({"throughput.tokensPerSecond": (100000.0, 5000.0)})["throughput"])
    # the guards compare with the BASE mean, so a changed BASE moves the limits
    dec = all_ok({"BASE": {"weights.bytesDeflated": (400000.0, 200)}, "CMP": dict(P(150.0), **{"weights.bytesDeflated": (600000.0, 200)})})
    check("S3 size against the BASE mean (1.5 x 400000 = 600000)", dec["arms"]["CMP"]["passesAllGuards"])

    # S4: an arm that fails a guard or the co-primary never enters Q, even with the lowest primary; the next arm can then win.
    d = all_ok({"CMP-HDG-F5": dict(P(100.0), **{"weights.bytesDeflated": (1.7 * B["weights.bytesDeflated"], 200)}), "CMP": P(150.0)})
    check("S4 guard failure excludes the best arm", "CMP-HDG-F5" not in d["qualifying"] and d["lowestPrimary"]["arm"] == "CMP"
          and d["candidate"] == "CMP" and d["wins"], str(d["qualifying"]))
    d = all_ok({"CMP-HDG-F5": dict(P(100.0), **{CONFIDENT: (B[CONFIDENT] + 1.0, 1.0)}), "CMP-HDG": P(130.0), "CMP": P(150.0)})
    check("S4 co-primary excludes the best arm", "CMP-HDG-F5" not in d["qualifying"] and d["candidate"] == "CMP-HDG" and d["wins"], str(d["qualifying"]))
    check("S4 co-primary: lower is allowed",
          all_ok({"CMP": dict(P(150.0), **{CONFIDENT: (B[CONFIDENT] - 2.0, 1.0)})})["arms"]["CMP"]["passesAllGuards"])

    # S5: nothing qualifies: BASE stays, no candidate.
    bad = {"udDev.harmfulPer1k": (B["udDev.harmfulPer1k"] + 3.0, 0.5)}
    d = all_ok({"CMP": dict(P(100.0), **bad), "CMP-HDG": dict(P(100.0), **bad), "CMP-HDG-F5": dict(P(100.0), **bad)})
    check("S5 nothing qualifies", d["qualifying"] == [] and d["candidate"] is None and not d["wins"] and d["winner"] == "BASE", str(d))
    d = all_ok({})
    check("S5 BASE stays when every arm equals BASE", d["candidate"] == "CMP" and not d["wins"] and d["winner"] == "BASE")

    # S6: the candidate qualifies but its interval contains 0: BASE stays; a worse arm never wins.
    d = all_ok({"BASE": P(240.0, 20.0), "CMP": P(236.0, 20.0), "CMP-HDG": P(300.0, 20.0), "CMP-HDG-F5": P(300.0, 20.0)})
    check("S6 not significant", d["candidate"] == "CMP" and not d["wins"] and d["winner"] == "BASE", str(d["candidateInterval"]))
    d = all_ok({"CMP": P(260.0), "CMP-HDG": P(280.0), "CMP-HDG-F5": P(290.0)})
    check("S6 worse than BASE", d["candidate"] == "CMP" and not d["wins"] and d["candidateInterval"]["hi"] > 0.0)

    # S7: no fallback: the preferred arm (CMP, fewest additions) is not significant, CMP-HDG-F5 alone would win: BASE stays.
    d = all_ok({"BASE": P(130.0), "CMP-HDG-F5": P(100.0, 1.0), "CMP-HDG": P(105.0, 1.0), "CMP": P(108.0, 60.0)})
    check("S7 CMP-HDG-F5 alone would win", d["arms"]["CMP-HDG-F5"]["diffVsBase"]["hi"] < 0.0)
    check("S7 candidate", d["candidate"] == "CMP", str(d["candidate"]))
    check("S7 no fallback", not d["wins"] and d["winner"] == "BASE", "%s %s" % (d["wins"], d["candidateInterval"]))

    # S8: the Welch rule is strict (an upper bound of exactly 0 is not an improvement) and uses the frozen function by default.
    runs = synthetic_runs({"CMP": P(200.0)})
    d = decide(runs, welch=lambda a, b: (-1.0, -2.0, 0.0, 8.0, 0.1))
    check("S8 upper bound 0 does not win", not d["wins"] and d["winner"] == "BASE")
    d = decide(runs, welch=lambda a, b: (-1.0, -2.0, -1e-9, 8.0, 0.01))
    check("S8 upper bound below 0 wins", d["wins"] and d["winner"] in CANDIDATES, str(d["winner"]))
    d = decide(synthetic_runs({"CMP": P(200.0)}))
    check("S8 frozen welch by default", abs(d["arms"]["CMP"]["diffVsBase"]["mean"] - (200.0 - 240.0)) < 1e-9)
    check("S8 pairs reported", sorted(d["pairs"]) == ["CMP-HDG - CMP", "CMP-HDG-F5 - CMP-HDG"] and PRIMARY in d["pairs"]["CMP-HDG-F5 - CMP-HDG"])

    # S9: files, acceptance tests, the report and the command line; missing runs stop before any selection.
    shipped_bytes = b"shipped weights bytes"
    overrides = {"CMP": P(145.0), "CMP-HDG": P(140.0), "CMP-HDG-F5": P(135.0)}
    syn = synthetic_runs(overrides)
    figs = figures_of(syn[BASE_ID][seeds.index(SHIPPED_SEED)])
    with tempfile.TemporaryDirectory() as tmp:
        directory, shipped = write_synthetic(tmp, syn, shipped_bytes)
        runs, checks, decision, _ = analyze(directory, shipped, verify=False, shipped_figures=figs)
        bad_checks = [c for c in checks if not c["ok"]]
        check("S9 acceptance passes", not bad_checks, str(bad_checks))
        check("S9 decision", decision is not None and decision["winner"] == "CMP" and decision["wins"], str(decision and decision["winner"]))
        report = build_report(runs, decision, checks, directory, seeds)
        check("S9 report", "| CMP-HDG-F5 |" in report and "Decision: CMP WINS" in report and "## Selection table" in report
              and "## Acceptance tests" in report and "## Pairwise Welch" in report and "tokens/s >= 150,000" in report, report[:400])
        out, dec_path = os.path.join(tmp, "report.md"), os.path.join(tmp, "decision.json")
        stdout, sys.stdout = sys.stdout, open(os.devnull, "w", encoding="utf-8")
        try:
            code = main(["--runs", directory, "--shipped-weights", shipped, "--out", out, "--decision", dec_path],
                        _test={"verify": False, "figures": figs})
        finally:
            sys.stdout.close()
            sys.stdout = stdout
        check("S9 main exit", code == 0, str(code))
        check("S9 outputs", os.path.isfile(out) and os.path.isfile(dec_path) and json.load(open(dec_path, encoding="utf-8"))["winner"] == "CMP")
        os.remove(run_stem(directory, "CMP-HDG-F5", 12) + ".json")
        try:
            analyze(directory, shipped, verify=False, shipped_figures=figs)
            check("S9 missing run", False, "no error raised")
        except RunsError as e:
            check("S9 missing run", "CMP-HDG-F5-s12.json" in str(e), str(e))

    # S10: each acceptance test fails when its condition is broken, and no decision is made.
    def acceptance_case(name, tweak=None, more=None, shipped_check=shipped_bytes):
        with tempfile.TemporaryDirectory() as tmp:
            directory_, shipped_ = write_synthetic(tmp, synthetic_runs(overrides), shipped_bytes, tweak, more)
            with open(shipped_, "wb") as f:
                f.write(shipped_check)
            _, checks_, decision_, _ = analyze(directory_, shipped_, verify=False, shipped_figures=figs)
            failed = [c["name"] for c in checks_ if not c["ok"]]
            check("S10 %s fails" % name, decision_ is None and len(failed) > 0, str(failed))
            return failed

    failed = acceptance_case("a: shipped weights differ", shipped_check=b"other bytes")
    check("S10 a names the hash", any(n.startswith("a: BASE seed 20260101 weights SHA-256") for n in failed), str(failed))
    failed = acceptance_case("a: figure differs", tweak=lambda l, s, r, h: set_path(r, "udDev.harmful", 95) if (l, s) == ("BASE", SHIPPED_SEED) else None)
    check("S10 a names the figure", any("udDev.harmful" in n for n in failed), str(failed))
    failed = acceptance_case("b: compound adds 741", more={"compound": 741})
    check("S10 b CMP count", any(n.startswith("b: CMP seed") for n in failed) and any(n.startswith("b: CMP-HDG-F5 seed") for n in failed), str(failed))
    failed = acceptance_case("b: headings adds 376", more={"headings": 376})
    check("S10 b headings count", any(n.startswith("b: CMP-HDG seed") for n in failed) and not any(n.startswith("b: CMP seed") for n in failed), str(failed))
    failed = acceptance_case("b: source missing in the log",
                             tweak=lambda l, s, r, h: h.__setitem__(0, h[0].replace(",compound:1", "")) if (l, s) == ("CMP", 13) else None)
    check("S10 b source", any(n.startswith("b: CMP seed 13") for n in failed), str(failed))
    failed = acceptance_case("b: BASE lists a new source",
                             tweak=lambda l, s, r, h: h.__setitem__(0, h[0].replace("imperative:1", "imperative:1,headings:1")) if (l, s) == ("BASE", 14) else None)
    check("S10 b BASE lists the source", any(n.startswith("b: BASE seed 14") for n in failed), str(failed))
    failed = acceptance_case("b: the mix differs (imperative missing)",
                             tweak=lambda l, s, r, h: h.__setitem__(0, h[0].replace(",imperative:1", "")) if (l, s) == ("CMP-HDG", 11) else None)
    check("S10 b mix", any(n.startswith("b: CMP-HDG seed 11") for n in failed), str(failed))
    failed = acceptance_case("c: one run has another input hash",
                             tweak=lambda l, s, r, h: r["inputs"].__setitem__("headingsSha256", "f" * 64) if (l, s) == ("CMP-HDG", 14) else None)
    check("S10 c names the input", any(n.startswith("c: inputs.headingsSha256") for n in failed), str(failed))
    failed = acceptance_case("c: every run has the same but not the frozen hash",
                             tweak=lambda l, s, r, h: r["inputs"].__setitem__("compoundHeadSha256", "e" * 64))
    check("S10 c frozen value", any(n.startswith("c: inputs.compoundHeadSha256") for n in failed), str(failed))
    failed = acceptance_case("c: an input hash is absent", tweak=lambda l, s, r, h: r["inputs"].pop("namedStateSha256") if (l, s) == ("BASE", 11) else None)
    check("S10 c absent", any(n.startswith("c: inputs.namedStateSha256") for n in failed), str(failed))

    # S11: invalid runs raise before any selection (feature groups of the weights header, the primary re-derivation, the co-primary sum).
    def invalid_case(name, tweak, needle):
        with tempfile.TemporaryDirectory() as tmp:
            directory_, shipped_ = write_synthetic(tmp, synthetic_runs(overrides), shipped_bytes, tweak)
            try:
                analyze(directory_, shipped_, verify=False, shipped_figures=figs)
                check("S11 %s" % name, False, "no error raised")
            except RunsError as e:
                check("S11 %s" % name, needle in str(e), str(e))

    invalid_case("F5 weights header lacks titleCaseLine", lambda l, s, r, h: r.__setitem__("groups", ["clauseCues"]) if (l, s) == ("CMP-HDG-F5", 11) else None,
                 "CMP-HDG-F5-s11.json: groups")
    invalid_case("CMP weights header names titleCaseLine", lambda l, s, r, h: r.__setitem__("groups", ["clauseCues", "titleCaseLine"]) if (l, s) == ("CMP", 12) else None,
                 "CMP-s12.json: groups")
    invalid_case("primary does not match the counts", lambda l, s, r, h: set_path(r, PRIMARY, 1.0) if (l, s) == ("CMP", 13) else None,
                 "re-derived from the counts")
    invalid_case("confident-wrong total does not match", lambda l, s, r, h: set_path(r, "shapesNew.pooledPrimary.confidentWrongTotal", 99) if (l, s) == ("BASE", 14) else None,
                 "confidentWrongTotal")
    invalid_case("a required metric is absent", lambda l, s, r, h: r["shapesNew"].pop("namedState") if (l, s) == ("CMP-HDG", 20260101) else None,
                 "shapesNew.namedState.shape.targetAccuracy is missing")
    invalid_case("wrong seed field", lambda l, s, r, h: r.__setitem__("seed", 99) if (l, s) == ("BASE", 12) else None, "seed field")

    # S12: a missing training log fails the acceptance test (it is not skipped); an acceptance failure writes the report, no decision, exit 1.
    with tempfile.TemporaryDirectory() as tmp:
        directory_, shipped_ = write_synthetic(tmp, synthetic_runs(overrides), shipped_bytes)
        os.remove(run_stem(directory_, "CMP", 11) + ".train.txt")
        _, checks_, decision_, _ = analyze(directory_, shipped_, verify=False, shipped_figures=figs)
        check("S12 missing log", decision_ is None and any("CMP seed 11 training line" in c["name"] and not c["ok"] for c in checks_))
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
        check("S12 exit 1 without a decision", code == 1 and os.path.isfile(out) and not os.path.exists(dec_path), str(code))

    print("selftest: %s" % ("ok" if not failures else "FAILED"))
    for f in failures:
        print("  - " + f)
    return 1 if failures else 0


# ---------------------------------------------------------------------------------------------------------------------

def main(argv=None, _test=None):
    """The command line. `_test` ({"verify", "figures"}) is for the self-test only: it lets the self-test drive this function with
    synthetic runs (no frozen-file check, the figures of the synthetic BASE run); the command line has no such option."""
    ap = argparse.ArgumentParser(description="Compound-head and heading data experiment: acceptance tests, analysis and selection "
                                             "(standard library only).")
    ap.add_argument("--runs", help="the folder of the driver run (all four arms)")
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
        print("analyze_compound_heading: cannot select: %s" % e, file=sys.stderr)
        return 1
    report = build_report(runs, decision, checks, args.runs, seeds)
    print(report)
    if args.out:
        with open(args.out, "w", encoding="utf-8", newline="\n") as f:
            f.write(report + "\n")
    if decision is None:
        print("analyze_compound_heading: an acceptance test failed: no selection is made", file=sys.stderr)
        return 1
    if args.decision:
        with open(args.decision, "w", encoding="utf-8", newline="\n") as f:
            json.dump(decision, f, indent=2, sort_keys=True)
            f.write("\n")
    return 0


if __name__ == "__main__":
    sys.exit(main())

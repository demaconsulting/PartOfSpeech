#!/usr/bin/env python3
"""Imperative-data experiment: acceptance tests, analysis and selection (standard library only; pre-registered, frozen with the RUN-LOG).

usage:
    python analyze_imperative.py --run F2=DIR --run F2+IMP=DIR --run ALL+IMP=DIR [--reference ALL=DIR]
                                 [--shipped-weights FILE] [--imperative-sentences N] [--out report.md] [--decision decision.json]
    python analyze_imperative.py --selftest

The frozen analysis script of the context-feature experiment (context-features/analyze_arms.py) cannot judge these arms: it needs a
baseline without feature groups and it hard-codes a size guard of 2 x BASE. This script is the versioned replacement. It reads the
per-run result files that the frozen driver run_arms.ps1 writes (`<DIR>\\<arm>-s<seed>.json`, schema 1; the interface contract is at the
top of run_arms.ps1) and the training logs next to them (`<arm>-s<seed>.train.txt`). One folder holds one run of the driver:

    arm label   driver arm (-Arms)   runner            groups
    F2          F2                   runner-base.cmd   clauseCues (the shipped configuration, the current training mix)
    F2+IMP      F2                   runner-imp.cmd    clauseCues + the `imperative` source at weight 1
    ALL+IMP     ALL                  runner-imp.cmd    all four groups + the `imperative` source at weight 1
    ALL         ALL                  runner-base.cmd   all four groups (reference arm: reported, never gated, never selected)

The only thing taken from the frozen script is `welch_diff` (Welch-Satterthwaite t interval, 95 percent); the file is loaded from its
place by path, without editing it, and only after its SHA-256 has been checked against the frozen value. (The self-test also checks the
frozen t quantile against published values.) Every other function of this script is its own.

Acceptance tests (run before the rule is applied; a failure stops without a selection, exit code 1):
  (a) F2 at seed 20260101 has weights byte-identical to the shipped weights.bin (SHA-256 of the file) and the figures of the shipped
      model: hard tranche 235.0 harmful per 1k, standard tranche 16.7, open dev 100 harmful of 4,451 decisions, UD-EWT dev 91 of 4,570;
  (b) every run of an arm with the imperative source trained on exactly N more sentences than the F2 run of the same seed (N = the
      sentence count of imperative.tsv, 900) and lists `imperative:1` among its sources; every run of an arm without it trained on the
      same number of sentences as F2 and lists no `imperative` source (read from the `Training on N sentences (...), sources=...` line of
      `<arm>-s<seed>.train.txt`);
  (c) the `inputs.*Sha256` fields of all runs agree and equal the frozen hashes of the five development files;
  and the SHA-256 of the frozen files (run_arms.ps1, arms.json, analyze_arms.py, both runners, imperative.tsv) equal the frozen values.

Selection rule (the literal pre-registered text; nothing is tuned afterwards):

1. Primary metric = the mean over the seeds of shapes.hard.pooled.harmfulPer1k (harmful errors per 1,000 noun-verb decisions on the
   hard tranche of the shapes development set). Lower is better. Secondary (reported, not gated): hard v-imperative-initial target accuracy.
2. Guards, each arm against the F2 five-seed mean (the throughput guard is absolute; it uses the median over the seeds of each run's
   median): shapes-dev standard harmful per 1k <= F2 + 5; open dev harmful per 1k <= F2 + 1.5; UD-EWT dev harmful per 1k <= F2 + 1.5;
   UD-EWT dev accuracy >= F2 - 0.15 points; deflated weights bytes <= 1.5 x F2; throughput >= 150,000 tokens/s.
3. Q = {F2+IMP, ALL+IMP} that pass all guards. If Q is empty, F2 stays and nothing ships.
4. m = the lowest primary in Q. S = the arms of Q with primary <= 1.10 x m (within 10 percent relative).
5. Candidate = the arm of S with the fewest feature groups (F2+IMP before ALL+IMP); ties: the lowest primary; then the order above.
6. The candidate ships only if the Welch 95 percent interval (Welch-Satterthwaite degrees of freedom) of (candidate - F2) on the primary
   lies entirely below 0 (upper bound < 0). Otherwise F2 stays and nothing ships. There is no fallback to the other arm.

Reported, not gated: accuracies, NLL, ECE, temperature, rows, bytes, the per-shape breakdown of both tranches (target accuracy and
harmful per 1k, including n-subject-initial), the secondary metric with its Welch interval, the reference arm, and a paired item
bootstrap over the hard items of each candidate against F2 (2,000 resamples, seed 20261108, descriptive only).

Exit codes: 0 done (also when F2 stays), 1 missing or invalid runs, a frozen-file mismatch or a failed acceptance test (no selection is
made) or a failed self-test, 2 usage.
"""

import argparse
import hashlib
import importlib.util
import json
import math
import os
import random
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
ARMS_JSON = os.path.join(CONTEXT_DIR, "arms.json")
DEFAULT_SHIPPED = os.path.join(REPO, "src", "DemaConsulting.PartOfSpeech", "Resources", "weights.bin")

REL_FROZEN_ANALYZER = "tools/DemaConsulting.PartOfSpeech.Trainer/context-features/analyze_arms.py"

# Files frozen by the pre-registration commit: repository-relative path -> SHA-256 of the committed text (line endings LF; a
# working-tree copy with CRLF, as .gitattributes gives the .cmd files, is normalized before hashing).
FROZEN_FILES = {
    "tools/DemaConsulting.PartOfSpeech.Trainer/context-features/run_arms.ps1":
        "26ba1c6ed281bdc86ac88522aaab76fd6490f10a753c5aa1dae96d83ff5995db",
    "tools/DemaConsulting.PartOfSpeech.Trainer/context-features/arms.json":
        "0aac43677f11b3cbfa37a020344b6276483edbf8af7eee840893a43520e9fad8",
    REL_FROZEN_ANALYZER: "3d52825384b440c5786e1b241fc6f814e4e47a63f155414e0d6450107d33e494",
    "tools/DemaConsulting.PartOfSpeech.Trainer/imperative-data/runner-base.cmd":
        "549a2c6ed8d1eaf00b30551fae14e546c51023dd79fe3a77810f39a1fe79ff3e",
    "tools/DemaConsulting.PartOfSpeech.Trainer/imperative-data/runner-imp.cmd":
        "c60a7eac3ba369406710f7737926ac743c16346b7a9edd8d98bd8fea08b05f4b",
    "data/imperative/imperative.tsv": "218246df0c4ce739e6631a066e5296194d6773e09fc849cd64c1eb93516185ad",
}

# The development files that every run scored (inputs.* of the per-run JSON) and their frozen SHA-256.
FROZEN_INPUTS = {
    "devSha256": "bbc94e42693d92a7e948598d5ea8a2c58274d3e4e5e6e4d0ed7d8a5c10963290",       # data/eval/shapes-dev/dev.tsv
    "hardSha256": "c041ae3d8b4b948326d002176bdffa0e5eb2ac0a6fe7d12daed7c3d289db8b22",      # data/eval/shapes-dev/hard.tsv
    "udDevSha256": "0fc0b4a371ef6d0437e01e396762cdb600d0d7ff0d725ead00ac9f3c41f82037",     # tools/data/cache/ud-ewt/ud_dev.tsv
    "openDevSha256": "8caa822515cee179093f068ff4ea7fa62714830d4c50de696bd4403037fc05c7",   # data/open/dev.tsv
    "goldDevSha256": "ffb2f3afd3460523656f5528a66094bad507982b15eb2b4a9341cdb78ff117f5",   # data/eval/gold-v2/dev.tsv
}

# The figures of the shipped model (F2, seed 20260101) that the F2 run of the same seed must reproduce (acceptance test a).
SHIPPED_FIGURES = [
    ("shapes.hard.pooled.harmfulPer1k", 235.0, 1),
    ("shapes.standard.pooled.harmfulPer1k", 16.7, 1),
    ("openDev.harmful", 100, 0),
    ("openDev.nounVerbDecisions", 4451, 0),
    ("udDev.harmful", 91, 0),
    ("udDev.decisions", 4570, 0),
]

IMPERATIVE_SENTENCES = 900
SHIPPED_SEED = 20260101
PRIMARY = "shapes.hard.pooled.harmfulPer1k"
SECONDARY = "shapes.hard.shapes.v-imperative-initial.targetAccuracy"
BOOTSTRAP_SEED = 20261108
BOOTSTRAP_RESAMPLES = 2000
TOLERANCE = 1e-9
WINDOW = 1.10

# label, driver arm (file prefix), feature groups of the arm, with the imperative source, role
ARMS = [
    {"id": "F2", "file": "F2", "groups": 1, "imp": False, "role": "baseline"},
    {"id": "F2+IMP", "file": "F2", "groups": 1, "imp": True, "role": "candidate"},
    {"id": "ALL+IMP", "file": "ALL", "groups": 4, "imp": True, "role": "candidate"},
    {"id": "ALL", "file": "ALL", "groups": 4, "imp": False, "role": "reference"},
]
BASE_ID = "F2"
CANDIDATES = ["F2+IMP", "ALL+IMP"]
REFERENCE_ID = "ALL"

# (path, label, decimals, required)
GROUPS = [
    ("Shapes development set", [
        ("shapes.hard.pooled.harmfulPer1k", "hard harmful per 1k (primary)", 1, True),
        (SECONDARY, "hard v-imperative-initial target acc % (secondary)", 1, False),
        ("shapes.hard.pooled.targetAccuracy", "hard target acc %", 2, False),
        ("shapes.hard.pooled.controlAccuracy", "hard control acc %", 2, False),
        ("shapes.standard.pooled.harmfulPer1k", "standard harmful per 1k", 1, True),
        ("shapes.standard.pooled.targetAccuracy", "standard target acc %", 2, False),
        ("shapes.standard.pooled.controlAccuracy", "standard control acc %", 2, False),
    ]),
    ("Open dev and gold v2 dev", [
        ("openDev.accuracy", "open acc %", 2, False),
        ("openDev.ambiguousAccuracy", "open ambiguous acc %", 2, False),
        ("openDev.harmfulPer1k", "open harmful per 1k", 2, True),
        ("openDev.nll", "open NLL", 4, False),
        ("openDev.ece", "open ECE %", 2, False),
        ("goldDev.accuracy", "gold acc %", 2, False),
        ("goldDev.harmfulPer1k", "gold harmful per 1k", 1, False),
        ("goldDev.nll", "gold NLL", 4, False),
        ("goldDev.ece", "gold ECE %", 2, False),
    ]),
    ("UD-EWT dev", [
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

# name, label, metric path, kind, limit, aggregate
GUARDS = [
    ("standard-harmful", "standard harmful per 1k <= F2 + 5", "shapes.standard.pooled.harmfulPer1k", "max_delta", 5.0, "mean"),
    ("open-harmful", "open dev harmful per 1k <= F2 + 1.5", "openDev.harmfulPer1k", "max_delta", 1.5, "mean"),
    ("ud-harmful", "UD-EWT dev harmful per 1k <= F2 + 1.5", "udDev.harmfulPer1k", "max_delta", 1.5, "mean"),
    ("ud-accuracy", "UD-EWT dev accuracy >= F2 - 0.15", "udDev.accuracy", "min_delta", -0.15, "mean"),
    ("size", "deflated bytes <= 1.5 x F2", "weights.bytesDeflated", "max_ratio", 1.5, "mean"),
    ("throughput", "tokens/s >= 150,000", "throughput.tokensPerSecond", "min_abs", 150000.0, "median"),
]
WELCH_METRICS = [PRIMARY, SECONDARY, "shapes.standard.pooled.harmfulPer1k", "openDev.harmfulPer1k", "udDev.harmfulPer1k",
                 "udDev.accuracy"]


class RunsError(Exception):
    """Missing or invalid inputs, a frozen-file mismatch or a failed acceptance test: no selection is made."""


# ---------------------------------------------------------------------------------------------------------------------
# Frozen files and the one function taken from the frozen script
# ---------------------------------------------------------------------------------------------------------------------

def sha256_lf(data):
    """SHA-256 of the bytes with CRLF turned into LF (the form in which the files are committed)."""
    return hashlib.sha256(data.replace(b"\r\n", b"\n")).hexdigest()


def verify_frozen(repo=REPO, table=None):
    """Returns the list of problems (empty when every frozen file has its frozen SHA-256)."""
    problems = []
    for rel, expected in (FROZEN_FILES if table is None else table).items():
        path = os.path.join(repo, *rel.split("/"))
        try:
            with open(path, "rb") as f:
                actual = sha256_lf(f.read())
        except OSError as e:
            problems.append("frozen file %s cannot be read: %s" % (rel, e))
            continue
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


def frozen_arm_spec():
    """The seeds and the feature groups of the arms F2 and ALL of the frozen arms.json."""
    with open(ARMS_JSON, encoding="utf-8") as f:
        spec = json.load(f)
    features = {a["id"]: list(a["features"]) for a in spec["arms"]}
    return [int(s) for s in spec["seeds"]], features


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
    return os.path.join(directory, "%s-s%s" % (arm_by_id(arm_id)["file"], seed))


def load_runs(dirs, seeds, features):
    """Returns {arm label: [run dict per seed]}; raises RunsError on any missing or invalid run (no selection is made)."""
    required = [m[0] for g in GROUPS for m in g[1] if m[3]]
    runs, errors = {}, []
    for label, directory in dirs.items():
        runs[label] = []
        expected = sorted(features[arm_by_id(label)["file"]])
        for seed in seeds:
            path = run_stem(directory, label, seed) + ".json"
            name = "%s/%s" % (label, os.path.basename(path))
            if not os.path.isfile(path):
                errors.append("missing run %s" % name)
                continue
            try:
                with open(path, encoding="utf-8") as f:
                    run = json.load(f)
            except (OSError, ValueError) as e:
                errors.append("unreadable run %s: %s" % (name, e))
                continue
            if run.get("schema") != 1:
                errors.append("%s: schema is not 1" % name)
            if "arm" in run and run["arm"] != arm_by_id(label)["file"]:
                errors.append("%s: arm field is %r" % (name, run["arm"]))
            if "seed" in run and str(run["seed"]) != str(seed):
                errors.append("%s: seed field is %r" % (name, run["seed"]))
            if "features" in run and sorted(run["features"]) != expected:
                errors.append("%s: features are %r, expected %r" % (name, run["features"], expected))
            for m in required:
                if get_path(run, m) is None:
                    errors.append("%s: required metric %s is missing" % (name, m))
            runs[label].append(run)
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


def sha256_file(path):
    h = hashlib.sha256()
    with open(path, "rb") as f:
        for block in iter(lambda: f.read(1 << 20), b""):
            h.update(block)
    return h.hexdigest()


def acceptance(runs, dirs, seeds, shipped_path, imperative_sentences):
    """Runs the acceptance tests; returns a list of {name, ok, detail}."""
    checks = []

    def add(name, ok, detail):
        checks.append({"name": name, "ok": bool(ok), "detail": detail})

    # (a) the F2 run of the shipped seed is the shipped model
    base_run = runs[BASE_ID][seeds.index(SHIPPED_SEED)] if SHIPPED_SEED in seeds else None
    if base_run is None:
        add("a: F2 seed %d present" % SHIPPED_SEED, False, "the seed is not in the seed list")
    else:
        try:
            shipped_sha = sha256_file(shipped_path)
        except OSError as e:
            shipped_sha = None
            add("a: shipped weights readable", False, str(e))
        if shipped_sha:
            run_sha = (base_run.get("weights") or {}).get("sha256")
            add("a: F2 seed %d weights SHA-256 equals the shipped weights.bin" % SHIPPED_SEED, run_sha == shipped_sha,
                "run %s, shipped %s" % (run_sha, shipped_sha))
            bin_path = run_stem(dirs[BASE_ID], BASE_ID, SHIPPED_SEED) + ".bin"
            if os.path.isfile(bin_path):
                bin_sha = sha256_file(bin_path)
                add("a: F2 seed %d weights file is byte-identical to the shipped weights.bin" % SHIPPED_SEED, bin_sha == shipped_sha,
                    "file %s, shipped %s" % (bin_sha, shipped_sha))
        for path, expected, decimals in SHIPPED_FIGURES:
            value = get_path(base_run, path)
            ok = value is not None and round(value, decimals) == expected
            add("a: F2 seed %d %s = %s" % (SHIPPED_SEED, path, expected), ok, "run %s" % value)

    # (b) the sentence counts of the training logs
    counts = {}
    for label in runs:
        counts[label] = {}
        for seed in seeds:
            path = run_stem(dirs[label], label, seed) + ".train.txt"
            try:
                with open(path, encoding="utf-8", errors="replace") as f:
                    parsed = parse_training_line(f.read())
            except OSError:
                parsed = None
            if parsed is None:
                add("b: %s seed %s training line" % (label, seed), False, "no readable `Training on N sentences` line in %s" % path)
            else:
                counts[label][seed] = parsed
    for label in runs:
        imp = arm_by_id(label)["imp"]
        for seed in seeds:
            if seed not in counts[label] or seed not in counts[BASE_ID]:
                continue
            count, sources = counts[label][seed]
            base_count = counts[BASE_ID][seed][0]
            want = base_count + (imperative_sentences if imp else 0)
            has_source = any(s.split(":")[0] == "imperative" for s in sources)
            ok = count == want and (("imperative:1" in sources) if imp else not has_source)
            add("b: %s seed %s trained on %s sentences%s" % (label, seed, "%d" % want,
                                                          " (F2 + %d, imperative:1)" % imperative_sentences if imp else " (as F2, no imperative)"),
                ok, "log: %d sentences, sources %s" % (count, ",".join(sources)))

    # (c) every run scored the frozen development files
    seen = {}
    for label, items in runs.items():
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


def decide(runs):
    """Applies the pre-registered rule to the loaded runs (a pure function of the numbers). Returns the decision dictionary.

    `runs` has the arms F2, F2+IMP and ALL+IMP and may have the reference arm ALL (reported, never in Q).
    """
    base_primary = column(runs[BASE_ID], PRIMARY)
    arms = {}
    for arm in ARMS:
        label = arm["id"]
        if label not in runs:
            continue
        primary = column(runs[label], PRIMARY)
        info = {"role": arm["role"], "groups": arm["groups"], "imperative": arm["imp"], "primaryMean": statistics.fmean(primary),
                "primarySd": statistics.stdev(primary), "primaryValues": primary}
        if label != BASE_ID:
            d, lo, hi, df, p = welch_diff(primary, base_primary)
            info["diffVsBase"] = {"mean": d, "lo": lo, "hi": hi, "df": df, "p": p}
            info["guards"] = [check_guard(g, column(runs[label], g[2]), column(runs[BASE_ID], g[2])) for g in GUARDS]
            info["passesAllGuards"] = all(g["pass"] for g in info["guards"])
        arms[label] = info

    qualifying = [a for a in CANDIDATES if arms[a]["passesAllGuards"]]
    decision = {"schema": 1, "baseline": BASE_ID, "primaryMetric": PRIMARY, "seeds": len(base_primary), "arms": arms,
                "qualifying": qualifying, "lowestPrimary": None, "withinTenPercent": [], "candidate": None,
                "candidateInterval": None, "ships": False, "winner": BASE_ID}
    if not qualifying:
        decision["reason"] = "No candidate arm passes all guards: F2 stays, nothing ships."
        return decision

    lowest = min(qualifying, key=lambda a: (arms[a]["primaryMean"], CANDIDATES.index(a)))
    m = arms[lowest]["primaryMean"]
    within = [a for a in qualifying if arms[a]["primaryMean"] <= WINDOW * m + TOLERANCE]
    candidate = min(within, key=lambda a: (arms[a]["groups"], arms[a]["primaryMean"], CANDIDATES.index(a)))
    interval = arms[candidate]["diffVsBase"]
    ships = interval["hi"] < 0.0
    decision.update({"lowestPrimary": {"arm": lowest, "value": m}, "withinTenPercent": within, "candidate": candidate,
                     "candidateInterval": interval, "ships": ships, "winner": candidate if ships else BASE_ID})
    if ships:
        decision["reason"] = ("%s ships: it has the fewest groups among the passing arms within 10%% of the lowest primary, and the "
                              "Welch 95%% interval of (arm - F2) is [%.2f, %.2f], entirely below 0." % (candidate, interval["lo"], interval["hi"]))
    else:
        decision["reason"] = ("F2 stays, nothing ships: the candidate %s has the interval [%.2f, %.2f] of (arm - F2), which does not "
                              "lie entirely below 0." % (candidate, interval["lo"], interval["hi"]))
    return decision


def percentile(values, q):
    ordered = sorted(values)
    pos = q / 100.0 * (len(ordered) - 1)
    lo = int(math.floor(pos))
    hi = min(lo + 1, len(ordered) - 1)
    return ordered[lo] + (ordered[hi] - ordered[lo]) * (pos - lo)


def item_bootstrap(arm_runs, base_runs, tranche="hard"):
    """Paired bootstrap over the items of a tranche of the harmful rate per 1k (arm - F2), items averaged over the seeds.

    Returns (difference, lower, upper) or None when a run has no `items.<tranche>` map.
    """
    def per_item(runs):
        tables = [(r.get("items") or {}).get(tranche) for r in runs]
        if any(not t for t in tables):
            return None
        ids = set(tables[0])
        for t in tables[1:]:
            ids &= set(t)
        return {i: (statistics.fmean(t[i]["h"] for t in tables), statistics.fmean(t[i]["d"] for t in tables)) for i in ids}

    arm_items, base_items = per_item(arm_runs), per_item(base_runs)
    if arm_items is None or base_items is None:
        return None
    ids = sorted(set(arm_items) & set(base_items))
    if not ids:
        return None
    rng = random.Random(BOOTSTRAP_SEED)
    diffs = []
    for _ in range(BOOTSTRAP_RESAMPLES):
        pick = [ids[rng.randrange(len(ids))] for _ in ids]
        ah, ad = sum(arm_items[i][0] for i in pick), sum(arm_items[i][1] for i in pick)
        bh, bd = sum(base_items[i][0] for i in pick), sum(base_items[i][1] for i in pick)
        if ad == 0 or bd == 0:
            continue
        diffs.append(1000.0 * ah / ad - 1000.0 * bh / bd)
    if not diffs:
        return None
    ah, ad = sum(arm_items[i][0] for i in ids), sum(arm_items[i][1] for i in ids)
    bh, bd = sum(base_items[i][0] for i in ids), sum(base_items[i][1] for i in ids)
    if ad == 0 or bd == 0:
        return None
    return (1000.0 * ah / ad - 1000.0 * bh / bd, percentile(diffs, 2.5), percentile(diffs, 97.5))


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


def shape_ids(runs, tranche):
    found = set()
    for items in runs.values():
        for run in items:
            found |= set((((run.get("shapes") or {}).get(tranche) or {}).get("shapes") or {}).keys())
    return sorted(found)


def build_report(runs, decision, checks, dirs, seeds):
    order = [a["id"] for a in ARMS if a["id"] in runs]
    out = ["# Imperative training data: analysis", "",
           "Arms: %s; seeds: %s (%d per arm); values are mean ± sample sd over the seeds. Baseline: F2 (the shipped configuration)." %
           (", ".join(order), ", ".join(str(s) for s in seeds), len(seeds)), ""]
    out += table(["arm", "folder", "driver arm", "groups", "imperative source", "role"],
                 [[a, "`%s`" % dirs[a], arm_by_id(a)["file"], arm_by_id(a)["groups"], "weight 1" if arm_by_id(a)["imp"] else "none",
                   arm_by_id(a)["role"]] for a in order]).split("\n") + [""]

    out += ["## Acceptance tests", "", table(["test", "result", "detail"],
                                              [[c["name"], "PASS" if c["ok"] else "FAIL", c["detail"]] for c in checks]), ""]
    if decision is None:
        out += ["**An acceptance test failed: no selection is made.**", ""]
        return "\n".join(out)

    out += ["## Full table", ""]
    for title, metrics in GROUPS:
        out += ["### %s" % title, ""]
        out += [table(["arm"] + [m[1] for m in metrics], [[a] + [cell(column(runs[a], m[0]), m[2]) for m in metrics] for a in order]), ""]

    for tranche in ("hard", "standard"):
        ids = shape_ids(runs, tranche)
        if not ids:
            continue
        out += ["## %s tranche per shape (diagnostics, not gated)" % tranche.capitalize(), ""]
        for key, label, decimals in (("targetAccuracy", "target accuracy %", 1), ("harmfulPer1k", "harmful per 1k", 1)):
            out += ["### %s: %s" % (tranche, label), ""]
            out += [table(["arm"] + ids, [[a] + [cell(column(runs[a], "shapes.%s.shapes.%s.%s" % (tranche, s, key)), decimals) for s in ids]
                                          for a in order]), ""]

    out += ["## Welch 95% interval of (arm - F2)", "",
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

    out += ["## Guards against F2", "",
            "Delta guards use the mean over the seeds; the size guard is 1.5 x the F2 mean; the throughput guard uses the median of the "
            "seeds' medians. The reference arm ALL is shown for information; it is never in Q.", ""]
    rows = []
    for a in order:
        if a == BASE_ID:
            continue
        row = [a]
        for g in decision["arms"][a]["guards"]:
            shown = {"max_delta": "%+.2f", "min_delta": "%+.2f", "max_ratio": "%.3fx", "min_abs": "%.0f"}[
                next(x[3] for x in GUARDS if x[0] == g["name"])] % g["shown"]
            row.append("%s %s" % ("PASS" if g["pass"] else "FAIL", shown))
        row.append("PASS" if decision["arms"][a]["passesAllGuards"] else "FAIL")
        rows.append(row)
    out += [table(["arm"] + [g[1] for g in GUARDS] + ["all guards"], rows), ""]

    out += ["## Selection rule", "", "Primary: mean hard harmful per 1k (lower is better); F2 mean %.2f." % decision["arms"][BASE_ID]["primaryMean"], ""]
    rows = []
    for a in order:
        info = decision["arms"][a]
        rows.append([a, info["groups"], "%.2f ± %.2f" % (info["primaryMean"], info["primarySd"]),
                     "-" if a == BASE_ID else ("yes" if info["passesAllGuards"] else "no"),
                     "reference (not gated)" if info["role"] == "reference" else ("baseline" if a == BASE_ID else "candidate")])
    out += [table(["arm", "groups", "primary", "passes all guards", "role"], rows), ""]
    out += ["- Passing candidate arms (Q): %s" % (", ".join(decision["qualifying"]) or "none")]
    if decision["lowestPrimary"]:
        lp = decision["lowestPrimary"]
        out += ["- Lowest primary in Q: %s (%.2f); within 10%% (primary <= %.2f): %s" %
                (lp["arm"], lp["value"], WINDOW * lp["value"], ", ".join(decision["withinTenPercent"])),
                "- Candidate (fewest groups, then lowest primary): %s" % decision["candidate"]]
        ci = decision["candidateInterval"]
        out += ["- Welch 95%% interval of (%s - F2): %+.2f [%+.2f, %+.2f], df %.2f, p %.4f" %
                (decision["candidate"], ci["mean"], ci["lo"], ci["hi"], ci["df"], ci["p"])]
    out += ["", "**Decision: %s.** %s" % ("SHIP " + decision["winner"] if decision["ships"] else "F2 STAYS", decision["reason"]), ""]

    out += ["## Descriptive: paired item bootstrap on the hard tranche (not part of the rule)", ""]
    for a in order:
        if a == BASE_ID:
            continue
        boot = item_bootstrap(runs[a], runs[BASE_ID])
        if boot is None:
            out += ["- %s: not available (the runs carry no `items.hard` maps)." % a]
        else:
            out += ["- %s - F2 harmful per 1k: %+.2f, 95%% percentile interval [%+.2f, %+.2f] (%d resamples of the items, seed %d, items "
                    "averaged over the seeds)." % (a, boot[0], boot[1], boot[2], BOOTSTRAP_RESAMPLES, BOOTSTRAP_SEED)]
    out += [""]
    commits = sorted({str((r.get("inputs") or {}).get("commit")) for items in runs.values() for r in items})
    dirty = sorted({str((r.get("inputs") or {}).get("dirty")) for items in runs.values() for r in items})
    out += ["Trainer commit of the runs (`inputs.commit`): %s; dirty flag: %s." % (", ".join(commits), ", ".join(dirty)), ""]
    return "\n".join(out)


def analyze(dirs, shipped_path=DEFAULT_SHIPPED, imperative_sentences=IMPERATIVE_SENTENCES, verify=True):
    """Loads the runs, runs the frozen-file check and the acceptance tests, and applies the rule.

    Returns (runs, checks, decision, seeds); the decision is None when an acceptance test failed (no selection is made).
    Raises RunsError for missing or invalid runs or a frozen-file mismatch.
    """
    needed = [BASE_ID] + CANDIDATES
    absent = [a for a in needed if a not in dirs]
    if absent:
        raise RunsError("no folder given for the arm(s) %s" % ", ".join(absent))
    for label in dirs:
        if label not in [a["id"] for a in ARMS]:
            raise RunsError("unknown arm %s" % label)
    if verify:
        problems = verify_frozen()
        if problems:
            raise RunsError("; ".join(problems))
    seeds, features = frozen_arm_spec()
    runs = load_runs(dirs, seeds, features)
    checks = acceptance(runs, dirs, seeds, shipped_path, imperative_sentences)
    if verify:
        checks.insert(0, {"name": "frozen files have their frozen SHA-256 (%d files)" % len(FROZEN_FILES), "ok": True, "detail": "checked"})
    decision = decide(runs) if all(c["ok"] for c in checks) else None
    return runs, checks, decision, seeds


# ---------------------------------------------------------------------------------------------------------------------
# Self-test with synthetic run results
# ---------------------------------------------------------------------------------------------------------------------

SQ = math.sqrt(2.5)
BASE_SPEC = {
    # the F2 values at the offset -2 (seed 20260101, arm F2) equal the shipped figures 235.0 and 16.7
    PRIMARY: (235.0 + 2.0 / SQ * 4.0, 4.0), SECONDARY: (50.0, 2.0), "shapes.hard.pooled.targetAccuracy": (69.5, 1.0),
    "shapes.hard.pooled.controlAccuracy": (98.3, 1.0), "shapes.standard.pooled.harmfulPer1k": (16.7 + 2.0 / SQ * 1.0, 1.0),
    "shapes.standard.pooled.targetAccuracy": (96.8, 0.2), "shapes.standard.pooled.controlAccuracy": (99.0, 0.5),
    "openDev.accuracy": (96.65, 0.05), "openDev.ambiguousAccuracy": (95.98, 0.05), "openDev.harmfulPer1k": (22.0, 0.5),
    "openDev.nll": (0.1127, 0.001), "openDev.ece": (0.24, 0.02), "goldDev.accuracy": (94.84, 0.2), "goldDev.harmfulPer1k": (26.0, 1.0),
    "goldDev.nll": (0.2577, 0.01), "goldDev.ece": (2.38, 0.1), "udDev.accuracy": (92.95, 0.05), "udDev.harmfulPer1k": (19.9, 0.5),
    "temperature": (9.514, 0.1), "weights.rows": (121033, 100), "weights.bytesRaw": (1146827, 500),
    "weights.bytesDeflated": (517210, 200), "throughput.tokensPerSecond": (400000.0, 10000.0), "trainWallSeconds": (60.0, 1.0),
}
SYNTH_FIGURES = {"openDev.harmful": 100, "openDev.nounVerbDecisions": 4451, "udDev.harmful": 91, "udDev.decisions": 4570}
SYNTH_BASE_SENTENCES = 48790


def set_path(obj, path, value):
    parts = path.split(".")
    for p in parts[:-1]:
        obj = obj.setdefault(p, {})
    obj[parts[-1]] = value


def synthetic_runs(overrides, labels=("F2", "F2+IMP", "ALL+IMP", "ALL"), seeds=None):
    """overrides: {arm label: {metric path: (mean, sd)}}. Values are mean + sd * (-2..2)/sqrt(2.5): an exact mean and sample sd."""
    seeds, features = (seeds or frozen_arm_spec()[0]), frozen_arm_spec()[1]
    offsets = [-2.0, -1.0, 0.0, 1.0, 2.0]
    runs = {}
    for ai, label in enumerate(labels):
        runs[label] = []
        values = dict(BASE_SPEC)
        values.update(overrides.get(label, {}))
        for si, seed in enumerate(seeds):
            run = {"schema": 1, "arm": arm_by_id(label)["file"], "seed": seed, "features": features[arm_by_id(label)["file"]]}
            for path, (mean, sd) in values.items():
                set_path(run, path, mean + offsets[(si + ai) % 5] / SQ * sd)
            for path, value in SYNTH_FIGURES.items():
                set_path(run, path, value)
            runs[label].append(run)
    return runs


def write_synthetic(tmp, runs, shipped_bytes, imp_delta=IMPERATIVE_SENTENCES, tweak=None):
    """Writes the run files, training logs and the shipped weights of a synthetic experiment; returns (dirs, shipped path)."""
    seeds = frozen_arm_spec()[0]
    shipped = os.path.join(tmp, "shipped-weights.bin")
    with open(shipped, "wb") as f:
        f.write(shipped_bytes)
    dirs = {}
    for label, items in runs.items():
        directory = os.path.join(tmp, label.replace("+", "-plus-"))
        os.makedirs(directory)
        dirs[label] = directory
        for seed, run in zip(seeds, items):
            run = json.loads(json.dumps(run))
            shipped_run = label == BASE_ID and seed == SHIPPED_SEED
            run["weights"]["sha256"] = hashlib.sha256(shipped_bytes).hexdigest() if shipped_run else hashlib.sha256(
                ("%s-%s" % (label, seed)).encode()).hexdigest()
            run["inputs"] = dict(FROZEN_INPUTS, commit="0123456789abcdef", dirty=False)
            imp = arm_by_id(label)["imp"]
            line = "Training on %s sentences (1,000,000 tokens per epoch), sources=open:2,masc:1,silver:1%s epochs=6 seed=%s\n" % (
                format(SYNTH_BASE_SENTENCES + (imp_delta if imp else 0), ","), ",imperative:1" if imp else "", seed)
            if tweak:
                line_holder = [line]
                tweak(label, seed, run, line_holder)
                line = line_holder[0]
            stem = run_stem(directory, label, seed)
            with open(stem + ".json", "w", encoding="utf-8") as f:
                json.dump(run, f)
            with open(stem + ".train.txt", "w", encoding="utf-8") as f:
                f.write("some jackknife line\n" + line + "Trained 1 steps in 1.0 s\n")
    return dirs, shipped


def selftest():
    failures = []

    def check(name, condition, detail=""):
        if not condition:
            failures.append("%s %s" % (name, detail))

    frozen = frozen_module()

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

    # Frozen files: the real files match; a change of one byte is found; CRLF is normalized.
    check("frozen files", verify_frozen() == [], str(verify_frozen()))
    check("frozen mismatch", len(verify_frozen(table={REL_FROZEN_ANALYZER: "0" * 64})) == 1)
    check("frozen missing", len(verify_frozen(table={"no/such/file": "0" * 64})) == 1)
    check("crlf normalized", sha256_lf(b"a\r\nb\n") == sha256_lf(b"a\nb\n") and sha256_lf(b"a\nb") != sha256_lf(b"a\nc"))
    seeds, features = frozen_arm_spec()
    check("arms.json seeds", seeds == [20260101, 11, 12, 13, 14], str(seeds))
    check("arms.json F2", features["F2"] == ["clauseCues"], str(features["F2"]))
    check("arms.json ALL", sorted(features["ALL"]) == ["clauseCues", "inflection", "positionCues", "rightPreview"])
    check("training line parse", parse_training_line("x\nTraining on 48,790 sentences (1,234 tokens per epoch), sources=open:2,masc:1 epochs=6\n")
          == (48790, ["open:2", "masc:1"]))
    check("training line parse (no separator)", parse_training_line("Training on 49690 sentences (1 tokens per epoch), sources=open:2")
          == (49690, ["open:2"]))
    check("training line parse (none)", parse_training_line("trained on 5 sentences") is None)

    B = {path: value[0] for path, value in BASE_SPEC.items()}  # the F2 means (nothing overrides F2 unless a scenario says so)

    def P(x, sd=4.0):
        return {PRIMARY: (x, sd)}

    # S1: both pass; F2+IMP is lower and significant: it ships (fewest groups, both within 10 percent).
    d = decide(synthetic_runs({"F2+IMP": P(200.0), "ALL+IMP": P(195.0)}))
    check("S1 qualifying", d["qualifying"] == ["F2+IMP", "ALL+IMP"], str(d["qualifying"]))
    check("S1 candidate", d["candidate"] == "F2+IMP" and d["ships"] and d["winner"] == "F2+IMP", str(d["candidate"]))
    check("S1 window", d["withinTenPercent"] == ["F2+IMP", "ALL+IMP"] and d["lowestPrimary"]["arm"] == "ALL+IMP")

    # S2: the 10 percent window: 110.0 is exactly 1.10 x 100 (within, F2+IMP preferred); 110.5 is outside (ALL+IMP is the candidate).
    base300 = {"F2": P(300.0)}
    d = decide(synthetic_runs(dict(base300, **{"ALL+IMP": P(100.0), "F2+IMP": P(110.0)})))
    check("S2 boundary within", d["withinTenPercent"] == ["F2+IMP", "ALL+IMP"] and d["candidate"] == "F2+IMP", str(d["withinTenPercent"]))
    d = decide(synthetic_runs(dict(base300, **{"ALL+IMP": P(100.0), "F2+IMP": P(110.5)})))
    check("S2 outside", d["withinTenPercent"] == ["ALL+IMP"] and d["candidate"] == "ALL+IMP" and d["ships"], str(d["withinTenPercent"]))
    d = decide(synthetic_runs(dict(base300, **{"ALL+IMP": P(100.0), "F2+IMP": P(99.0)})))
    check("S2 F2+IMP lowest", d["lowestPrimary"]["arm"] == "F2+IMP" and d["candidate"] == "F2+IMP")
    d = decide(synthetic_runs(dict(base300, **{"ALL+IMP": P(100.0), "F2+IMP": P(100.0)})))
    check("S2 tie of primaries: F2+IMP (fewer groups)", d["candidate"] == "F2+IMP")

    # S3: guard boundaries of F2+IMP: each passes exactly at the limit and fails just beyond it.
    def guard_flags(arm_overrides):
        dec = decide(synthetic_runs({"F2+IMP": dict(P(150.0), **arm_overrides)}))
        return {g["name"]: g["pass"] for g in dec["arms"]["F2+IMP"]["guards"]}

    cases = [
        ("standard-harmful", "shapes.standard.pooled.harmfulPer1k", (B["shapes.standard.pooled.harmfulPer1k"] + 5.0, 1.0),
         (B["shapes.standard.pooled.harmfulPer1k"] + 5.2, 1.0)),
        ("open-harmful", "openDev.harmfulPer1k", (B["openDev.harmfulPer1k"] + 1.5, 0.5), (B["openDev.harmfulPer1k"] + 1.6, 0.5)),
        ("ud-harmful", "udDev.harmfulPer1k", (B["udDev.harmfulPer1k"] + 1.5, 0.5), (B["udDev.harmfulPer1k"] + 1.6, 0.5)),
        ("ud-accuracy", "udDev.accuracy", (B["udDev.accuracy"] - 0.15, 0.05), (B["udDev.accuracy"] - 0.16, 0.05)),
        ("size", "weights.bytesDeflated", (1.5 * B["weights.bytesDeflated"], 200), (1.5 * B["weights.bytesDeflated"] + 1.0, 200)),
        ("throughput", "throughput.tokensPerSecond", (150000.0, 10000.0), (149990.0, 10000.0)),
    ]
    for name, path, good, bad in cases:
        flags_good, flags_bad = guard_flags({path: good}), guard_flags({path: bad})
        check("S3 %s passes at the limit" % name, flags_good[name] and all(flags_good.values()), str(flags_good))
        check("S3 %s fails beyond the limit" % name, not flags_bad[name] and all(v for k, v in flags_bad.items() if k != name), str(flags_bad))
    flags = guard_flags({"weights.bytesDeflated": (1.6 * B["weights.bytesDeflated"], 200)})
    check("S3 size 1.6x fails, 2x is no longer the limit", not flags["size"])
    flags = guard_flags({"weights.bytesDeflated": (1.4 * B["weights.bytesDeflated"], 200)})
    check("S3 size 1.4x passes", flags["size"])
    # the guards compare with the F2 mean, so a changed F2 moves the limits
    dec = decide(synthetic_runs({"F2": {"weights.bytesDeflated": (400000.0, 200)}, "F2+IMP": {"weights.bytesDeflated": (600000.0, 200)}}))
    check("S3 size against the F2 mean (1.5 x 400000 = 600000)", dec["arms"]["F2+IMP"]["guards"][4]["pass"])
    dec = decide(synthetic_runs({"F2": {"weights.bytesDeflated": (400000.0, 200)}, "F2+IMP": {"weights.bytesDeflated": (600001.0, 200)}}))
    check("S3 size just beyond 1.5 x F2", not dec["arms"]["F2+IMP"]["guards"][4]["pass"])

    # S4: only ALL+IMP passes (F2+IMP fails the size guard); the lone arm is the candidate and ships if its interval is below 0.
    d = decide(synthetic_runs({"F2+IMP": dict(P(150.0), **{"weights.bytesDeflated": (1.7 * B["weights.bytesDeflated"], 200)}), "ALL+IMP": P(160.0)}))
    check("S4 only ALL+IMP", d["qualifying"] == ["ALL+IMP"] and d["candidate"] == "ALL+IMP" and d["ships"] and d["winner"] == "ALL+IMP")

    # S5: nothing qualifies: F2 stays, no candidate.
    bad = {"udDev.harmfulPer1k": (B["udDev.harmfulPer1k"] + 3.0, 0.5)}
    d = decide(synthetic_runs({"F2+IMP": dict(P(100.0), **bad), "ALL+IMP": dict(P(100.0), **bad)}))
    check("S5 nothing qualifies", d["qualifying"] == [] and d["candidate"] is None and not d["ships"] and d["winner"] == "F2", str(d))

    # S6: the candidate qualifies but its interval contains 0: F2 stays (noisy arm); a worse arm never ships.
    d = decide(synthetic_runs({"F2": P(240.0, 20.0), "F2+IMP": P(236.0, 20.0), "ALL+IMP": P(300.0, 20.0)}))
    check("S6 not significant", d["candidate"] == "F2+IMP" and not d["ships"] and d["winner"] == "F2", str(d["candidateInterval"]))
    d = decide(synthetic_runs({"F2+IMP": P(260.0), "ALL+IMP": P(280.0)}))
    check("S6 worse than F2", d["candidate"] == "F2+IMP" and not d["ships"] and d["candidateInterval"]["hi"] > 0.0)

    # S7: no fallback: the preferred arm (F2+IMP, fewest groups) is not significant, ALL+IMP alone would ship: F2 stays.
    d = decide(synthetic_runs({"F2": P(130.0), "ALL+IMP": P(100.0, 1.0), "F2+IMP": P(108.0, 60.0)}))
    check("S7 ALL+IMP alone would ship", d["arms"]["ALL+IMP"]["diffVsBase"]["hi"] < 0.0)
    check("S7 candidate", d["candidate"] == "F2+IMP", str(d["candidate"]))
    check("S7 no fallback", not d["ships"] and d["winner"] == "F2", "%s %s" % (d["ships"], d["candidateInterval"]))

    # S8: the reference arm ALL is reported and never selected, even when it is the best arm and passes every guard.
    d = decide(synthetic_runs({"F2+IMP": P(200.0), "ALL+IMP": P(210.0), "ALL": P(50.0)}))
    check("S8 reference not in Q", "ALL" not in d["qualifying"] and d["candidate"] == "F2+IMP" and d["arms"]["ALL"]["role"] == "reference")
    labels3 = ("F2", "F2+IMP", "ALL+IMP")
    d = decide(synthetic_runs({"F2+IMP": P(200.0)}, labels=labels3))
    check("S8 without the reference arm", "ALL" not in d["arms"] and d["ships"])

    # S9: files, acceptance tests, the report and the command line; missing runs stop before any selection.
    shipped_bytes = b"shipped weights bytes"
    base_overrides = {"F2+IMP": P(200.0)}
    with tempfile.TemporaryDirectory() as tmp:
        runs = synthetic_runs(base_overrides)
        dirs, shipped = write_synthetic(tmp, runs, shipped_bytes)
        _, checks, decision, _ = analyze(dirs, shipped)
        bad_checks = [c for c in checks if not c["ok"]]
        check("S9 acceptance passes", not bad_checks, str(bad_checks))
        check("S9 decision", decision is not None and decision["winner"] == "F2+IMP" and decision["ships"], str(decision and decision["winner"]))
        report = build_report(load_runs(dirs, seeds, features), decision, checks, dirs, seeds)
        check("S9 report", "| F2+IMP |" in report and "Decision: SHIP F2+IMP" in report and "## Guards against F2" in report
              and "## Acceptance tests" in report and "frozen files have their frozen SHA-256" in report)
        out, dec_path = os.path.join(tmp, "report.md"), os.path.join(tmp, "decision.json")
        argv = ["--run", "F2=" + dirs["F2"], "--run", "F2+IMP=" + dirs["F2+IMP"], "--run", "ALL+IMP=" + dirs["ALL+IMP"],
                "--reference", "ALL=" + dirs["ALL"], "--shipped-weights", shipped, "--out", out, "--decision", dec_path]
        stdout, sys.stdout = sys.stdout, open(os.devnull, "w", encoding="utf-8")
        try:
            code = main(argv)
        finally:
            sys.stdout.close()
            sys.stdout = stdout
        check("S9 main exit", code == 0, str(code))
        check("S9 outputs", os.path.isfile(out) and os.path.isfile(dec_path) and json.load(open(dec_path, encoding="utf-8"))["winner"] == "F2+IMP")
        os.remove(run_stem(dirs["ALL+IMP"], "ALL+IMP", 12) + ".json")
        try:
            analyze(dirs, shipped)
            check("S9 missing run", False, "no error raised")
        except RunsError as e:
            check("S9 missing run", "ALL+IMP/ALL-s12.json" in str(e), str(e))
        try:
            analyze({k: v for k, v in dirs.items() if k != "F2"}, shipped)
            check("S9 missing arm", False, "no error raised")
        except RunsError as e:
            check("S9 missing arm", "F2" in str(e), str(e))

    # S10: each acceptance test fails when its condition is broken, and no decision is made.
    def acceptance_case(name, tweak=None, imp_delta=IMPERATIVE_SENTENCES, shipped_bytes_run=shipped_bytes, shipped_check=shipped_bytes):
        with tempfile.TemporaryDirectory() as tmp:
            dirs_, shipped_ = write_synthetic(tmp, synthetic_runs(base_overrides), shipped_bytes_run, imp_delta, tweak)
            with open(shipped_, "wb") as f:
                f.write(shipped_check)
            _, checks_, decision_, _ = analyze(dirs_, shipped_)
            failed = [c["name"] for c in checks_ if not c["ok"]]
            check("S10 %s fails" % name, decision_ is None and len(failed) > 0, str(failed))
            return failed

    failed = acceptance_case("a: shipped weights differ", shipped_check=b"other bytes")
    check("S10 a names the hash", any(n.startswith("a: F2 seed 20260101 weights SHA-256") for n in failed), str(failed))
    failed = acceptance_case("a: figure differs", tweak=lambda l, s, r, h: set_path(r, "udDev.harmful", 92) if (l, s) == ("F2", SHIPPED_SEED) else None)
    check("S10 a names the figure", any("udDev.harmful" in n for n in failed), str(failed))
    failed = acceptance_case("a: hard figure differs", tweak=lambda l, s, r, h: set_path(r, PRIMARY, 235.1) if (l, s) == ("F2", SHIPPED_SEED) else None)
    check("S10 a hard figure", any("shapes.hard.pooled.harmfulPer1k" in n for n in failed), str(failed))
    failed = acceptance_case("b: 899 more sentences", imp_delta=899)
    check("S10 b names the count", any(n.startswith("b: F2+IMP") for n in failed) and any(n.startswith("b: ALL+IMP") for n in failed), str(failed))
    failed = acceptance_case("b: 901 more sentences", imp_delta=901)
    check("S10 b 901", any(n.startswith("b: F2+IMP") for n in failed), str(failed))
    failed = acceptance_case("b: reference trained on other count",
                             tweak=lambda l, s, r, h: h.__setitem__(0, h[0].replace("48,790", "48,791")) if (l, s) == ("ALL", 11) else None)
    check("S10 b reference", any(n.startswith("b: ALL seed 11") for n in failed), str(failed))
    failed = acceptance_case("b: source missing in the log",
                             tweak=lambda l, s, r, h: h.__setitem__(0, h[0].replace(",imperative:1", "")) if (l, s) == ("F2+IMP", 13) else None)
    check("S10 b source", any(n.startswith("b: F2+IMP seed 13") for n in failed), str(failed))
    failed = acceptance_case("b: base run lists the source",
                             tweak=lambda l, s, r, h: h.__setitem__(0, h[0].replace("silver:1", "silver:1,imperative:1")) if (l, s) == ("F2", 14) else None)
    check("S10 b base lists the source", any(n.startswith("b: F2 seed 14") for n in failed), str(failed))
    failed = acceptance_case("c: one run has another input hash",
                             tweak=lambda l, s, r, h: r["inputs"].__setitem__("hardSha256", "f" * 64) if (l, s) == ("ALL+IMP", 14) else None)
    check("S10 c names the input", any(n.startswith("c: inputs.hardSha256") for n in failed), str(failed))
    failed = acceptance_case("c: every run has the same but not the frozen hash",
                             tweak=lambda l, s, r, h: r["inputs"].__setitem__("devSha256", "e" * 64))
    check("S10 c frozen value", any(n.startswith("c: inputs.devSha256") for n in failed), str(failed))
    failed = acceptance_case("c: an input hash is absent", tweak=lambda l, s, r, h: r["inputs"].pop("udDevSha256") if (l, s) == ("F2", 11) else None)
    check("S10 c absent", any(n.startswith("c: inputs.udDevSha256") for n in failed), str(failed))

    # S11: a missing training log fails the acceptance test (it is not skipped).
    with tempfile.TemporaryDirectory() as tmp:
        dirs_, shipped_ = write_synthetic(tmp, synthetic_runs(base_overrides), shipped_bytes)
        os.remove(run_stem(dirs_["F2+IMP"], "F2+IMP", 11) + ".train.txt")
        _, checks_, decision_, _ = analyze(dirs_, shipped_)
        check("S11 missing log", decision_ is None and any("F2+IMP seed 11 training line" in c["name"] and not c["ok"] for c in checks_))
        # an acceptance failure writes the report and no decision and the command line exits 1
        out, dec_path = os.path.join(tmp, "r.md"), os.path.join(tmp, "d.json")
        stdout, stderr = sys.stdout, sys.stderr
        sys.stdout, sys.stderr = open(os.devnull, "w", encoding="utf-8"), open(os.devnull, "w", encoding="utf-8")
        try:
            code = main(["--run", "F2=" + dirs_["F2"], "--run", "F2+IMP=" + dirs_["F2+IMP"], "--run", "ALL+IMP=" + dirs_["ALL+IMP"],
                         "--shipped-weights", shipped_, "--out", out, "--decision", dec_path])
        finally:
            sys.stdout.close()
            sys.stderr.close()
            sys.stdout, sys.stderr = stdout, stderr
        check("S11 exit 1 without a decision", code == 1 and os.path.isfile(out) and not os.path.exists(dec_path), str(code))

    # S12: the paired bootstrap is deterministic and has the right sign.
    def items(h_of):
        return [{"items": {"hard": {"i%d" % k: {"d": 1, "c": 1 - h_of(k), "h": h_of(k)} for k in range(40)}}} for _ in range(5)]

    boot = item_bootstrap(items(lambda k: 1 if k < 10 else 0), items(lambda k: 1 if k < 20 else 0))
    check("S12 bootstrap", boot is not None and abs(boot[0] - (-250.0)) < 1e-9 and boot[1] < boot[0] < boot[2] and boot[2] < 0.0, str(boot))
    check("S12 bootstrap deterministic", boot == item_bootstrap(items(lambda k: 1 if k < 10 else 0), items(lambda k: 1 if k < 20 else 0)))
    check("S12 bootstrap without items", item_bootstrap([{}] * 5, [{}] * 5) is None)

    print("selftest: %s" % ("ok" if not failures else "FAILED"))
    for f in failures:
        print("  - " + f)
    return 1 if failures else 0


# ---------------------------------------------------------------------------------------------------------------------

def parse_assignment(text, what):
    if "=" not in text:
        raise argparse.ArgumentTypeError("%s expects LABEL=DIR, got %r" % (what, text))
    label, directory = text.split("=", 1)
    return label, directory


def main(argv=None):
    ap = argparse.ArgumentParser(description="Imperative-data experiment: acceptance tests, analysis and selection (standard library only).")
    ap.add_argument("--run", action="append", default=[], type=lambda t: parse_assignment(t, "--run"),
                    help="LABEL=DIR of one driver run; the labels F2, F2+IMP and ALL+IMP are required")
    ap.add_argument("--reference", type=lambda t: parse_assignment(t, "--reference"), help="ALL=DIR of the optional reference arm")
    ap.add_argument("--shipped-weights", default=DEFAULT_SHIPPED, help="the shipped weights.bin (default: the repository's)")
    ap.add_argument("--imperative-sentences", type=int, default=IMPERATIVE_SENTENCES,
                    help="sentence count of imperative.tsv (default %d)" % IMPERATIVE_SENTENCES)
    ap.add_argument("--out", help="markdown report")
    ap.add_argument("--decision", help="machine-readable decision")
    ap.add_argument("--selftest", action="store_true", help="run the self-test with synthetic runs and exit")
    args = ap.parse_args(argv)
    if hasattr(sys.stdout, "reconfigure"):
        sys.stdout.reconfigure(encoding="utf-8")
    if args.selftest:
        return selftest()
    dirs = dict(args.run)
    if len(dirs) != len(args.run):
        ap.error("a label is given twice")
    if args.reference:
        if args.reference[0] != REFERENCE_ID:
            ap.error("--reference takes the label %s" % REFERENCE_ID)
        dirs[REFERENCE_ID] = args.reference[1]
    if not dirs:
        ap.error("--run is required (or --selftest)")
    try:
        runs, checks, decision, seeds = analyze(dirs, args.shipped_weights, args.imperative_sentences)
    except RunsError as e:
        print("analyze_imperative: cannot select: %s" % e, file=sys.stderr)
        return 1
    report = build_report(runs, decision, checks, dirs, seeds)
    print(report)
    if args.out:
        with open(args.out, "w", encoding="utf-8", newline="\n") as f:
            f.write(report + "\n")
    if decision is None:
        print("analyze_imperative: an acceptance test failed: no selection is made", file=sys.stderr)
        return 1
    if args.decision:
        with open(args.decision, "w", encoding="utf-8", newline="\n") as f:
            json.dump(dict(decision, acceptance=checks), f, indent=2)
            f.write("\n")
    return 0


if __name__ == "__main__":
    sys.exit(main())

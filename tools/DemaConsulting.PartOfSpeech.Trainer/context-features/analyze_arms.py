#!/usr/bin/env python3
"""Context-feature experiment: analysis and selection (standard library only; pre-registered, frozen with the RUN-LOG).

usage:
    python analyze_arms.py --runs DIR [--arms arms.json] [--out report.md] [--decision decision.json]
    python analyze_arms.py --selftest

Reads the per-run result files written by run_arms.ps1 (`<DIR>\\<arm>-s<seed>.json`, schema 1, see the interface contract at
the top of run_arms.ps1), prints and writes (1) the full table (mean +- sample sd over the seeds per arm for every metric),
(2) the per-shape breakdown of the hard tranche, (3) the Welch 95% interval of (arm - BASE) for the primary metric and the
guarded metrics, (4) the guard checks against BASE, (5) the selection rule applied exactly as pre-registered, and (6) a
descriptive paired item-level bootstrap of the chosen arm against BASE. The decision is also written as machine-readable JSON.

Selection rule (the literal pre-registered text; nothing is tuned afterwards):

1. Primary metric = the mean over the seeds of shapes.hard.pooled.harmfulPer1k (harmful errors per 1,000 noun-verb decisions
   on the hard tranche of the shapes development set). Lower is better.
2. Guards, each arm against BASE (BASE and arm values are the means over the seeds unless stated):
   shapes-dev standard harmful per 1k <= BASE + 5; open dev harmful per 1k <= BASE + 1.5; UD-EWT dev harmful per 1k <= BASE + 1.5;
   UD-EWT dev accuracy >= BASE - 0.15 points; deflated weights bytes <= 2 x BASE; single-thread throughput >= 150,000 tokens/s
   (absolute; the median over the seeds of each run's median).
3. Q = arms other than BASE that pass all guards. If Q is empty, BASE stays.
4. m = the lowest primary in Q. S = the arms of Q with primary <= 1.10 x m (within 10% relative).
5. Candidate = the arm of S with the fewest feature groups; ties: the lowest primary; a remaining tie: the order of arms.json.
6. The candidate ships only if the Welch 95% t-interval (Welch-Satterthwaite degrees of freedom) of (candidate - BASE) on the
   primary lies entirely below 0 (upper bound < 0). Otherwise BASE stays and nothing ships. There is no fallback to another arm.

Exit codes: 0 done (also when BASE stays), 1 missing or invalid runs (no selection is made) or a failed self-test, 2 usage.
"""

import argparse
import json
import math
import os
import random
import statistics
import sys
import tempfile

HERE = os.path.dirname(os.path.abspath(__file__))
DEFAULT_ARMS = os.path.join(HERE, "arms.json")

PRIMARY = "shapes.hard.pooled.harmfulPer1k"
SHAPES = ["n-compound-head", "n-subject-initial", "v-imperative-initial", "v-after-modal"]
BOOTSTRAP_SEED = 20261107
BOOTSTRAP_RESAMPLES = 2000
TOLERANCE = 1e-9

# (path, label, decimals, required). The required metrics are the primary and the guarded ones; the others print "n/a" if absent.
GROUPS = [
    ("Shapes development set", [
        ("shapes.hard.pooled.harmfulPer1k", "hard harmful per 1k (primary)", 1, True),
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
    ("standard-harmful", "standard harmful per 1k <= BASE + 5", "shapes.standard.pooled.harmfulPer1k", "max_delta", 5.0, "mean"),
    ("open-harmful", "open dev harmful per 1k <= BASE + 1.5", "openDev.harmfulPer1k", "max_delta", 1.5, "mean"),
    ("ud-harmful", "UD-EWT dev harmful per 1k <= BASE + 1.5", "udDev.harmfulPer1k", "max_delta", 1.5, "mean"),
    ("ud-accuracy", "UD-EWT dev accuracy >= BASE - 0.15", "udDev.accuracy", "min_delta", -0.15, "mean"),
    ("size", "deflated bytes <= 2 x BASE", "weights.bytesDeflated", "max_ratio", 2.0, "mean"),
    ("throughput", "tokens/s >= 150,000", "throughput.tokensPerSecond", "min_abs", 150000.0, "median"),
]
WELCH_METRICS = [PRIMARY, "shapes.standard.pooled.harmfulPer1k", "openDev.harmfulPer1k", "udDev.harmfulPer1k", "udDev.accuracy"]


class RunsError(Exception):
    """Missing or invalid run results: no selection is made."""


# ---------------------------------------------------------------------------------------------------------------------
# Student t and Welch (no scipy): the regularized incomplete beta function by Lentz's continued fraction.
# ---------------------------------------------------------------------------------------------------------------------

def _betacf(a, b, x):
    tiny = 1e-300
    qab, qap, qam = a + b, a + 1.0, a - 1.0
    c = 1.0
    d = 1.0 - qab * x / qap
    d = 1.0 / (d if abs(d) > tiny else tiny)
    h = d
    for m in range(1, 1000):
        m2 = 2 * m
        aa = m * (b - m) * x / ((qam + m2) * (a + m2))
        d = 1.0 + aa * d
        d = 1.0 / (d if abs(d) > tiny else tiny)
        c = 1.0 + aa / c
        c = c if abs(c) > tiny else tiny
        h *= d * c
        aa = -(a + m) * (qab + m) * x / ((a + m2) * (qap + m2))
        d = 1.0 + aa * d
        d = 1.0 / (d if abs(d) > tiny else tiny)
        c = 1.0 + aa / c
        c = c if abs(c) > tiny else tiny
        delta = d * c
        h *= delta
        if abs(delta - 1.0) < 1e-15:
            break
    return h


def betainc(a, b, x):
    """Regularized incomplete beta function I_x(a, b)."""
    if x <= 0.0:
        return 0.0
    if x >= 1.0:
        return 1.0
    bt = math.exp(math.lgamma(a + b) - math.lgamma(a) - math.lgamma(b) + a * math.log(x) + b * math.log1p(-x))
    if x < (a + 1.0) / (a + b + 2.0):
        return bt * _betacf(a, b, x) / a
    return 1.0 - bt * _betacf(b, a, 1.0 - x) / b


def t_two_sided_p(t, df):
    """P(|T| >= |t|) of Student's t distribution with df degrees of freedom."""
    return betainc(df / 2.0, 0.5, df / (df + t * t))


def t_cdf(t, df):
    """Cumulative distribution function of Student's t distribution."""
    tail = 0.5 * t_two_sided_p(t, df)
    return 1.0 - tail if t >= 0 else tail


def t_quantile(p, df):
    """The p quantile (0.5 < p < 1) of Student's t distribution: inverse of t_cdf by bisection (200 steps)."""
    target_two_sided = 2.0 * (1.0 - p)
    lo, hi = 0.0, 2.0
    while t_two_sided_p(hi, df) > target_two_sided:
        hi *= 2.0
    for _ in range(200):
        mid = (lo + hi) / 2.0
        if t_two_sided_p(mid, df) > target_two_sided:
            lo = mid
        else:
            hi = mid
    return (lo + hi) / 2.0


def welch_diff(arm, base):
    """Welch t interval of mean(arm) - mean(base): (difference, lower, upper, df, two-sided p) at 95%.

    Degrees of freedom by the Welch-Satterthwaite formula. If both samples have no variance the interval degenerates to the
    difference itself (df and p are NaN).
    """
    d = statistics.fmean(arm) - statistics.fmean(base)
    va = statistics.variance(arm) / len(arm)
    vb = statistics.variance(base) / len(base)
    se = math.sqrt(va + vb)
    if se == 0.0:
        return d, d, d, float("nan"), float("nan")
    df = (va + vb) ** 2 / (va ** 2 / (len(arm) - 1) + vb ** 2 / (len(base) - 1))
    t = t_quantile(0.975, df)
    return d, d - t * se, d + t * se, df, t_two_sided_p(d / se, df)


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


def load_arms(path):
    with open(path, encoding="utf-8") as f:
        spec = json.load(f)
    arms = spec["arms"]
    ids = [a["id"] for a in arms]
    if len(set(ids)) != len(ids):
        raise RunsError("duplicate arm ids in %s" % path)
    if spec["baseline"] not in ids:
        raise RunsError("the baseline arm %s is not in %s" % (spec["baseline"], path))
    base = next(a for a in arms if a["id"] == spec["baseline"])
    if base["features"]:
        raise RunsError("the baseline arm must have no feature groups")
    return spec


def run_path(runs_dir, arm, seed):
    return os.path.join(runs_dir, "%s-s%s.json" % (arm, seed))


def load_runs(runs_dir, spec, seeds):
    """Returns {arm id: [run dict per seed, in the order of seeds]}; raises RunsError on any missing or invalid run."""
    runs, errors = {}, []
    required = [m[0] for g in GROUPS for m in g[1] if m[3]]
    for arm in spec["arms"]:
        runs[arm["id"]] = []
        for seed in seeds:
            path = run_path(runs_dir, arm["id"], seed)
            if not os.path.isfile(path):
                errors.append("missing run %s" % os.path.basename(path))
                continue
            try:
                with open(path, encoding="utf-8") as f:
                    run = json.load(f)
            except (OSError, ValueError) as e:
                errors.append("unreadable run %s: %s" % (os.path.basename(path), e))
                continue
            if run.get("schema") != 1:
                errors.append("%s: schema is not 1" % os.path.basename(path))
            if "arm" in run and run["arm"] != arm["id"]:
                errors.append("%s: arm field is %r" % (os.path.basename(path), run["arm"]))
            if "seed" in run and str(run["seed"]) != str(seed):
                errors.append("%s: seed field is %r" % (os.path.basename(path), run["seed"]))
            for m in required:
                if get_path(run, m) is None:
                    errors.append("%s: required metric %s is missing" % (os.path.basename(path), m))
            runs[arm["id"]].append(run)
    if errors:
        raise RunsError("; ".join(errors))
    return runs


def check_inputs(runs):
    """Every run must have scored the same development files: returns a list of problems (empty when consistent)."""
    seen = {}
    for arm, items in runs.items():
        for run in items:
            for key, value in (run.get("inputs") or {}).items():
                if key.endswith("Sha256"):
                    seen.setdefault(key, set()).add(value)
    return ["development input %s differs between runs: %s" % (k, sorted(v)) for k, v in seen.items() if len(v) > 1]


def column(arm_runs, path):
    return [get_path(r, path) for r in arm_runs]


# ---------------------------------------------------------------------------------------------------------------------
# Analysis and the selection rule
# ---------------------------------------------------------------------------------------------------------------------

def aggregate(values, how):
    return statistics.median(values) if how == "median" else statistics.fmean(values)


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


def decide(spec, runs):
    """Applies the pre-registered rule to the loaded runs (pure function of the numbers). Returns the decision dictionary."""
    base_id = spec["baseline"]
    order = [a["id"] for a in spec["arms"]]
    base_primary = column(runs[base_id], PRIMARY)
    arms = {}
    for arm in spec["arms"]:
        arm_id = arm["id"]
        primary = column(runs[arm_id], PRIMARY)
        info = {"features": arm["features"], "groups": len(arm["features"]), "primaryMean": statistics.fmean(primary),
                "primarySd": statistics.stdev(primary), "primaryValues": primary}
        if arm_id != base_id:
            d, lo, hi, df, p = welch_diff(primary, base_primary)
            info["diffVsBase"] = {"mean": d, "lo": lo, "hi": hi, "df": df, "p": p}
            info["guards"] = [check_guard(g, column(runs[arm_id], g[2]), column(runs[base_id], g[2])) for g in GUARDS]
            info["passesAllGuards"] = all(g["pass"] for g in info["guards"])
        arms[arm_id] = info

    qualifying = [a for a in order if a != base_id and arms[a]["passesAllGuards"]]
    decision = {"schema": 1, "baseline": base_id, "primaryMetric": PRIMARY, "seeds": len(base_primary), "arms": arms,
                "qualifying": qualifying, "lowestPrimary": None, "withinTenPercent": [], "candidate": None,
                "candidateInterval": None, "ships": False, "winner": base_id}
    if not qualifying:
        decision["reason"] = "No arm passes all guards: BASE stays, nothing ships."
        return decision

    lowest = min(qualifying, key=lambda a: (arms[a]["primaryMean"], order.index(a)))
    m = arms[lowest]["primaryMean"]
    within = [a for a in qualifying if arms[a]["primaryMean"] <= 1.10 * m + TOLERANCE]
    candidate = min(within, key=lambda a: (arms[a]["groups"], arms[a]["primaryMean"], order.index(a)))
    interval = arms[candidate]["diffVsBase"]
    ships = interval["hi"] < 0.0
    decision.update({"lowestPrimary": {"arm": lowest, "value": m}, "withinTenPercent": within, "candidate": candidate,
                     "candidateInterval": interval, "ships": ships, "winner": candidate if ships else base_id})
    if ships:
        decision["reason"] = ("%s ships: it has the fewest groups among the passing arms within 10%% of the lowest primary, and the "
                              "Welch 95%% interval of (arm - BASE) is [%.2f, %.2f], entirely below 0." % (candidate, interval["lo"], interval["hi"]))
    else:
        decision["reason"] = ("BASE stays, nothing ships: the candidate %s has the interval [%.2f, %.2f] of (arm - BASE), "
                              "which does not lie entirely below 0." % (candidate, interval["lo"], interval["hi"]))
    return decision


# ---------------------------------------------------------------------------------------------------------------------
# Descriptive item-level bootstrap (not part of the rule)
# ---------------------------------------------------------------------------------------------------------------------

def item_bootstrap(runs, arm_id, base_id):
    """Paired bootstrap over hard-tranche items of the harmful rate per 1k (arm - BASE), items averaged over the seeds.

    Returns (difference, lower, upper) or None when a run has no `items.hard` map.
    """
    def per_item(arm_runs):
        tables = [(r.get("items") or {}).get("hard") for r in arm_runs]
        if any(not t for t in tables):
            return None
        ids = set(tables[0])
        for t in tables[1:]:
            ids &= set(t)
        return {i: (statistics.fmean(t[i]["h"] for t in tables), statistics.fmean(t[i]["d"] for t in tables)) for i in ids}

    arm_items, base_items = per_item(runs[arm_id]), per_item(runs[base_id])
    if arm_items is None or base_items is None:
        return None
    ids = sorted(set(arm_items) & set(base_items))
    if not ids:
        return None
    rng = random.Random(BOOTSTRAP_SEED)
    diffs = []
    for _ in range(BOOTSTRAP_RESAMPLES):
        pick = [ids[rng.randrange(len(ids))] for _ in ids]
        ah = sum(arm_items[i][0] for i in pick)
        ad = sum(arm_items[i][1] for i in pick)
        bh = sum(base_items[i][0] for i in pick)
        bd = sum(base_items[i][1] for i in pick)
        if ad == 0 or bd == 0:
            continue
        diffs.append(1000.0 * ah / ad - 1000.0 * bh / bd)
    if not diffs:
        return None
    ah = sum(arm_items[i][0] for i in ids)
    ad = sum(arm_items[i][1] for i in ids)
    bh = sum(base_items[i][0] for i in ids)
    bd = sum(base_items[i][1] for i in ids)
    return (1000.0 * ah / ad - 1000.0 * bh / bd, percentile(diffs, 2.5), percentile(diffs, 97.5))


def percentile(values, q):
    ordered = sorted(values)
    pos = q / 100.0 * (len(ordered) - 1)
    lo = int(math.floor(pos))
    hi = min(lo + 1, len(ordered) - 1)
    return ordered[lo] + (ordered[hi] - ordered[lo]) * (pos - lo)


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


def build_report(spec, runs, decision, runs_dir, seeds):
    order = [a["id"] for a in spec["arms"]]
    base_id = spec["baseline"]
    out = ["# Context-feature experiment: analysis", "",
           "Runs: `%s`; arms: %d; seeds: %s (%d per arm); values are mean ± sample sd over the seeds." %
           (runs_dir, len(order), ", ".join(str(s) for s in seeds), len(seeds)), ""]

    out += ["## Full table", ""]
    for title, metrics in GROUPS:
        out += ["### %s" % title, ""]
        rows = [[a] + [cell(column(runs[a], m[0]), m[2]) for m in metrics] for a in order]
        out += [table(["arm"] + [m[1] for m in metrics], rows), ""]

    out += ["## Hard tranche per shape (target accuracy %, then harmful per 1k)", ""]
    for key, label, decimals in (("targetAccuracy", "target accuracy %", 1), ("harmfulPer1k", "harmful per 1k", 1)):
        out += ["### %s" % label, ""]
        rows = [[a] + [cell(column(runs[a], "shapes.hard.shapes.%s.%s" % (s, key)), decimals) for s in SHAPES] for a in order]
        out += [table(["arm"] + SHAPES, rows), ""]

    out += ["## Welch 95% interval of (arm - BASE)", "",
            "Difference of the means with the Welch-Satterthwaite t interval over the seeds (seeds measure training noise only).", ""]
    rows = []
    for a in order:
        if a == base_id:
            continue
        row = [a]
        for m in WELCH_METRICS:
            d, lo, hi, df, p = welch_diff(column(runs[a], m), column(runs[base_id], m))
            row.append("%+.2f [%+.2f, %+.2f]" % (d, lo, hi))
        rows.append(row)
    out += [table(["arm"] + WELCH_METRICS, rows), ""]

    out += ["## Guards against BASE", "",
            "Delta guards use the mean over the seeds; the throughput guard uses the median of the seeds' medians.", ""]
    rows = []
    for a in order:
        if a == base_id:
            continue
        row = [a]
        for g in decision["arms"][a]["guards"]:
            shown = {"max_delta": "%+.2f", "min_delta": "%+.2f", "max_ratio": "%.2fx", "min_abs": "%.0f"}[
                next(x[3] for x in GUARDS if x[0] == g["name"])] % g["shown"]
            row.append("%s %s" % ("PASS" if g["pass"] else "FAIL", shown))
        row.append("PASS" if decision["arms"][a]["passesAllGuards"] else "FAIL")
        rows.append(row)
    out += [table(["arm"] + [g[1] for g in GUARDS] + ["all guards"], rows), ""]

    out += ["## Selection rule", "",
            "Primary: mean hard harmful per 1k (lower is better); BASE mean %.2f." % decision["arms"][base_id]["primaryMean"], ""]
    rows = []
    for a in order:
        info = decision["arms"][a]
        rows.append([a, info["groups"], "%.2f ± %.2f" % (info["primaryMean"], info["primarySd"]),
                     "-" if a == base_id else ("yes" if info["passesAllGuards"] else "no")])
    out += [table(["arm", "groups", "primary", "passes all guards"], rows), ""]
    out += ["- Passing arms (Q): %s" % (", ".join(decision["qualifying"]) or "none")]
    if decision["lowestPrimary"]:
        lp = decision["lowestPrimary"]
        out += ["- Lowest primary in Q: %s (%.2f); within 10%% (primary <= %.2f): %s" %
                (lp["arm"], lp["value"], 1.10 * lp["value"], ", ".join(decision["withinTenPercent"])),
                "- Candidate (fewest groups, then lowest primary): %s" % decision["candidate"]]
        ci = decision["candidateInterval"]
        out += ["- Welch 95%% interval of (%s - BASE): %+.2f [%+.2f, %+.2f], df %.2f, p %.4f" %
                (decision["candidate"], ci["mean"], ci["lo"], ci["hi"], ci["df"], ci["p"])]
    out += ["", "**Decision: %s.** %s" % ("SHIP " + decision["winner"] if decision["ships"] else "BASE STAYS", decision["reason"]), ""]

    if decision["candidate"]:
        boot = item_bootstrap(runs, decision["candidate"], base_id)
        out += ["## Descriptive: paired item bootstrap of the candidate on the hard tranche (not part of the rule)", ""]
        if boot is None:
            out += ["Not available: the runs carry no `items.hard` maps.", ""]
        else:
            out += ["%s - BASE harmful per 1k: %+.2f, 95%% percentile interval [%+.2f, %+.2f] (%d resamples of the items, seed %d, "
                    "items averaged over the seeds)." % (decision["candidate"], boot[0], boot[1], boot[2], BOOTSTRAP_RESAMPLES,
                                                         BOOTSTRAP_SEED), ""]
    return "\n".join(out)


def analyze(runs_dir, arms_path, seeds_override=None):
    spec = load_arms(arms_path)
    seeds = seeds_override or spec["seeds"]
    runs = load_runs(runs_dir, spec, seeds)
    problems = check_inputs(runs)
    if problems:
        raise RunsError("; ".join(problems))
    decision = decide(spec, runs)
    return spec, runs, decision, seeds


# ---------------------------------------------------------------------------------------------------------------------
# Self-test with synthetic run results
# ---------------------------------------------------------------------------------------------------------------------

BASE_SPEC = {
    PRIMARY: (267.5, 4.0), "shapes.hard.pooled.targetAccuracy": (66.0, 1.0), "shapes.hard.pooled.controlAccuracy": (96.7, 1.0),
    "shapes.standard.pooled.harmfulPer1k": (28.3, 1.0), "shapes.standard.pooled.targetAccuracy": (96.3, 0.2),
    "shapes.standard.pooled.controlAccuracy": (96.0, 0.5), "openDev.accuracy": (96.38, 0.05),
    "openDev.ambiguousAccuracy": (95.68, 0.05), "openDev.harmfulPer1k": (20.2, 0.5), "openDev.nll": (0.1222, 0.001),
    "openDev.ece": (0.26, 0.02), "goldDev.accuracy": (95.24, 0.2), "goldDev.harmfulPer1k": (18.0, 1.0), "goldDev.nll": (0.26, 0.01),
    "goldDev.ece": (1.99, 0.1), "udDev.accuracy": (92.71, 0.05), "udDev.harmfulPer1k": (22.54, 0.5), "temperature": (6.442, 0.1),
    "weights.rows": (116662, 100), "weights.bytesRaw": (1080650, 500), "weights.bytesDeflated": (478835, 200),
    "throughput.tokensPerSecond": (400000.0, 10000.0), "trainWallSeconds": (25.0, 1.0),
}


def set_path(obj, path, value):
    parts = path.split(".")
    for p in parts[:-1]:
        obj = obj.setdefault(p, {})
    obj[parts[-1]] = value


def synthetic_runs(spec, overrides, seeds=(20260101, 11, 12, 13, 14)):
    """overrides: {arm id: {metric path: (mean, sd)}}. Values are mean + sd * (-2..2)/sqrt(2.5), i.e. an exact sample sd."""
    offsets = [-2.0, -1.0, 0.0, 1.0, 2.0]
    runs = {}
    for ai, arm in enumerate(spec["arms"]):
        runs[arm["id"]] = []
        values = dict(BASE_SPEC)
        values.update(overrides.get(arm["id"], {}))
        for si, seed in enumerate(seeds):
            run = {"schema": 1, "arm": arm["id"], "seed": seed}
            for path, (mean, sd) in values.items():
                shift = offsets[(si + ai) % 5] / math.sqrt(2.5) * sd
                set_path(run, path, mean + shift)
            runs[arm["id"]].append(run)
    return runs


def selftest():
    failures = []

    def check(name, condition, detail=""):
        if not condition:
            failures.append("%s %s" % (name, detail))

    # Student t against published 0.975 quantiles, and the cdf inverse.
    for df, q in {1: 12.7062047, 2: 4.3026527, 4: 2.7764451, 5: 2.5705818, 8: 2.3060041, 10: 2.2281389, 30: 2.0422725,
                  120: 1.9799304}.items():
        check("t(0.975,%d)" % df, abs(t_quantile(0.975, df) - q) < 1e-6, "got %.7f" % t_quantile(0.975, df))
    for df, p, q in [(5, 0.95, 2.0150484), (10, 0.99, 2.7637694), (7, 0.9, 1.4149239)]:
        check("t(%.2f,%d)" % (p, df), abs(t_quantile(p, df) - q) < 1e-6, "got %.7f" % t_quantile(p, df))
    for t, df, p in [(2.0, 10, 0.0733880), (1.0, 1, 0.5), (3.0, 5, 0.0300992)]:
        check("p(%g,%d)" % (t, df), abs(t_two_sided_p(t, df) - p) < 1e-6)
    check("cdf symmetric", abs(t_cdf(1.3, 6) + t_cdf(-1.3, 6) - 1.0) < 1e-12)
    check("cdf(quantile)", abs(t_cdf(t_quantile(0.975, 5.88), 5.88) - 0.975) < 1e-9)

    # Welch: hand-computed case a = 1..5, b = 2,4,..,10 (variances 2.5 and 10, df = 6.25 / 1.0625).
    d, lo, hi, df, p = welch_diff([2, 4, 6, 8, 10], [1, 2, 3, 4, 5])
    check("welch d", abs(d - 3.0) < 1e-12)
    check("welch df", abs(df - 6.25 / 1.0625) < 1e-9, "got %r" % df)
    check("welch interval", abs((hi - lo) / 2.0 - t_quantile(0.975, df) * math.sqrt(2.5)) < 1e-9)
    check("welch p vs interval", (p < 0.05) == (lo > 0.0) or abs(lo) < 1e-6, "p %g lo %g" % (p, lo))
    d0 = welch_diff([5, 5, 5], [5, 5, 5])
    check("welch degenerate", d0[0] == 0.0 and d0[1] == 0.0 and d0[2] == 0.0)

    spec = load_arms(DEFAULT_ARMS)
    ids = [a["id"] for a in spec["arms"]]
    check("arms.json arm count", len(ids) == 10 and ids[0] == "BASE")
    check("arms.json seeds", spec["seeds"] == [20260101, 11, 12, 13, 14])
    check("arms.json ALL", len(next(a for a in spec["arms"] if a["id"] == "ALL")["features"]) == 4)
    for a in spec["arms"]:
        if a["id"].startswith("ALL-minus-"):
            check("arms.json %s" % a["id"], len(a["features"]) == 3)

    def winner(overrides):
        decision = decide(spec, synthetic_runs(spec, overrides))
        return decision

    # S1: guards remove ALL; boundary: F2 exactly 10% above F1 is still within; F1 wins on lowest primary (same group count).
    d = winner({"F1": {PRIMARY: (200.0, 4.0)}, "F2": {PRIMARY: (220.0, 4.0)},
                "ALL": {PRIMARY: (190.0, 4.0), "weights.bytesDeflated": (2.5 * 478835, 200)}})
    check("S1 qualifying", "ALL" not in d["qualifying"] and "F1" in d["qualifying"], str(d["qualifying"]))
    check("S1 ALL fails size", not d["arms"]["ALL"]["passesAllGuards"])
    check("S1 winner", d["candidate"] == "F1" and d["ships"] and d["winner"] == "F1", str(d["candidate"]))

    # S2: simplicity: ALL 100 (4 groups), ALL-minus-F1 105 (3), F2 109 (1) within 10%; F3 112 is outside; F2 wins (fewest groups).
    others = {a: {PRIMARY: (300.0, 4.0)} for a in ids if a not in ("BASE", "ALL", "ALL-minus-F1", "F2", "F3")}
    d = winner(dict(others, **{"ALL": {PRIMARY: (100.0, 4.0)}, "ALL-minus-F1": {PRIMARY: (105.0, 4.0)}, "F2": {PRIMARY: (109.0, 4.0)},
                               "F3": {PRIMARY: (112.0, 4.0)}}))
    check("S2 within", sorted(d["withinTenPercent"]) == ["ALL", "ALL-minus-F1", "F2"], str(d["withinTenPercent"]))
    check("S2 winner", d["candidate"] == "F2" and d["ships"], str(d["candidate"]))

    # S3: tie in group counts: lowest primary wins.
    d = winner(dict(others, **{"F1": {PRIMARY: (105.0, 4.0)}, "F4": {PRIMARY: (100.0, 4.0)}, "F2": {PRIMARY: (300.0, 4.0)},
                               "F3": {PRIMARY: (300.0, 4.0)}, "ALL": {PRIMARY: (300.0, 4.0)}, "ALL-minus-F1": {PRIMARY: (300.0, 4.0)}}))
    check("S3 winner", d["candidate"] == "F4", str(d["candidate"]))

    # S4: guards at their boundaries and just beyond: standard +5.0 passes, +5.2 fails; UD accuracy -0.15 passes, -0.16 fails;
    # open +1.5 passes, +1.6 fails; throughput 150,000 passes, 149,990 fails (the five values have the same median).
    good = {PRIMARY: (200.0, 4.0)}
    d = winner({"F1": dict(good, **{"shapes.standard.pooled.harmfulPer1k": (33.3, 1.0)}),
                "F2": dict(good, **{"shapes.standard.pooled.harmfulPer1k": (33.5, 1.0)}),
                "F3": dict(good, **{"udDev.accuracy": (92.56, 0.05)}),
                "F4": dict(good, **{"udDev.accuracy": (92.55, 0.05)}),
                "ALL": dict(good, **{"openDev.harmfulPer1k": (21.7, 0.5)}),
                "ALL-minus-F1": dict(good, **{"openDev.harmfulPer1k": (21.8, 0.5)}),
                "ALL-minus-F2": dict(good, **{"throughput.tokensPerSecond": (150000.0, 10000.0)}),
                "ALL-minus-F3": dict(good, **{"throughput.tokensPerSecond": (149990.0, 10000.0)}),
                "ALL-minus-F4": dict(good, **{"udDev.harmfulPer1k": (24.1, 0.5)})})
    passing = {a: d["arms"][a]["passesAllGuards"] for a in ids if a != "BASE"}
    check("S4 boundaries", passing == {"F1": True, "F2": False, "F3": True, "F4": False, "ALL": True, "ALL-minus-F1": False,
                                       "ALL-minus-F2": True, "ALL-minus-F3": False, "ALL-minus-F4": False}, str(passing))

    # S5: no arm passes: BASE stays.
    d = winner({a: {PRIMARY: (150.0, 4.0), "udDev.harmfulPer1k": (30.0, 0.5)} for a in ids if a != "BASE"})
    check("S5 none pass", d["qualifying"] == [] and not d["ships"] and d["winner"] == "BASE" and d["candidate"] is None)

    # S6: the candidate qualifies but its interval contains 0: BASE stays (noisy arm).
    d = winner({"F1": {PRIMARY: (262.0, 20.0)}, "BASE": {PRIMARY: (267.5, 20.0)}})
    check("S6 not significant", d["candidate"] is not None and not d["ships"] and d["winner"] == "BASE", str(d["candidate"]))

    # S7: no fallback: the simplest arm within 10% (F2) is not significant, a more complex arm (ALL) is: BASE stays.
    d = winner(dict(others, **{"BASE": {PRIMARY: (130.0, 4.0)}, "ALL": {PRIMARY: (100.0, 1.0)},
                               "F2": {PRIMARY: (108.0, 60.0)}, "F3": {PRIMARY: (300.0, 4.0)}, "ALL-minus-F1": {PRIMARY: (300.0, 4.0)}}))
    check("S7 ALL alone would ship", d["arms"]["ALL"]["diffVsBase"]["hi"] < 0.0, str(d["arms"]["ALL"]["diffVsBase"]))
    check("S7 candidate", d["candidate"] == "F2", str(d["candidate"]))
    check("S7 no fallback", not d["ships"] and d["winner"] == "BASE", "%s %s" % (d["ships"], d["candidateInterval"]))

    # S8: an arm that is better by less than the guard noise but significant ships; its interval upper bound is below 0.
    d = winner({"F4": {PRIMARY: (255.0, 2.0)}, "BASE": {PRIMARY: (267.5, 2.0)}})
    check("S8 ships", d["candidate"] == "F4" and d["ships"] and d["candidateInterval"]["hi"] < 0.0)

    # S9: the file path: write synthetic runs, analyze, and a missing run stops before any selection.
    with tempfile.TemporaryDirectory() as tmp:
        runs = synthetic_runs(spec, {"F1": {PRIMARY: (200.0, 4.0)}})
        for arm_id, items in runs.items():
            for run in items:
                with open(run_path(tmp, arm_id, run["seed"]), "w", encoding="utf-8") as f:
                    json.dump(run, f)
        _, _, decision, _ = analyze(tmp, DEFAULT_ARMS)
        check("S9 file path", decision["winner"] == "F1" and decision["ships"], str(decision["winner"]))
        report = build_report(spec, runs, decision, tmp, spec["seeds"])
        check("S9 report", "| F1 |" in report and "Decision: SHIP F1" in report and "## Guards against BASE" in report)
        os.remove(run_path(tmp, "F3", 12))
        try:
            analyze(tmp, DEFAULT_ARMS)
            check("S9 missing run", False, "no error raised")
        except RunsError as e:
            check("S9 missing run", "F3-s12.json" in str(e), str(e))

    print("selftest: %s" % ("ok" if not failures else "FAILED"))
    for f in failures:
        print("  - " + f)
    sys.exit(1 if failures else 0)


# ---------------------------------------------------------------------------------------------------------------------

def main():
    ap = argparse.ArgumentParser(description="Context-feature experiment: analysis and selection (standard library only).")
    ap.add_argument("--runs", help="folder of the per-run result files <arm>-s<seed>.json")
    ap.add_argument("--arms", default=DEFAULT_ARMS, help="arms.json (default: next to this script)")
    ap.add_argument("--out", help="markdown report (default: <runs>/report.md)")
    ap.add_argument("--decision", help="machine-readable decision (default: <runs>/decision.json)")
    ap.add_argument("--selftest", action="store_true", help="run the self-test with synthetic runs and exit")
    args = ap.parse_args()
    if hasattr(sys.stdout, "reconfigure"):
        sys.stdout.reconfigure(encoding="utf-8")
    if args.selftest:
        selftest()
    if not args.runs:
        ap.error("--runs is required (or --selftest)")
    out = args.out or os.path.join(args.runs, "report.md")
    decision_path = args.decision or os.path.join(args.runs, "decision.json")
    try:
        spec, runs, decision, seeds = analyze(args.runs, args.arms)
    except RunsError as e:
        print("analyze_arms: cannot select: %s" % e, file=sys.stderr)
        sys.exit(1)
    report = build_report(spec, runs, decision, args.runs, seeds)
    print(report)
    with open(out, "w", encoding="utf-8", newline="\n") as f:
        f.write(report + "\n")
    with open(decision_path, "w", encoding="utf-8", newline="\n") as f:
        json.dump(decision, f, indent=2)
        f.write("\n")
    print("\nwrote %s and %s" % (out, decision_path))


if __name__ == "__main__":
    main()

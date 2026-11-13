#!/usr/bin/env python3
"""Independent, fail-closed audit of the run inputs of the beta.3 phase 2 technical-mix experiment (E2).

This script is NOT part of the pre-registered, hash-pinned analysis set (beta3-tech/). It was written after the 48 runs and after the
analysis, it reads the same files the analyzer reads, and it changes no rule, no number and no decision. It exists because the frozen
analyzer (beta3-tech/analyze_tech.py) validates some inputs only weakly; see README.md in this folder. A future harness must make these
checks fail closed inside its own analyzer.

Checks (every check prints one PASS or FAIL line; any FAIL makes the exit code 1):

  files            all 48 result files <arm>-s<seed>.json (6 arms x 8 seeds of arms.json) exist, parse as JSON objects, and no other
                   result file is present
  fields           every result carries `arm`, `seed`, `sources` and `inputs.commit`; arm and seed agree with the file name
  grid             the (arm, seed) pairs found in the results are unique and complete
  sources          the `sources` of every result equal those of beta3-tech/arms.json for that arm (sets of NAME:WEIGHT; BASE has none)
  features         the `features` of every result equal those of arms.json for that arm
  provenance       one repository commit in all results, never a dirty tree, driver and inputs agree, identical input hashes
  counts           per run: decisions > 0 for every pooled or per-shape block, harmful <= wrong, harmful <= decisions, wrong <= answers
                   for every calibration point, token blocks of the dev sets, pooled blocks equal the sum of their parts
  tranches         tranche totals equal the sizes pinned in beta3-tech/shipped_figures.json (S1 450, S2 285, S3 236, S4 446 ...)
  bins             every <arm>-s<seed>.bin exists and its SHA-256 equals the hash recorded in the result (weights.sha256)
  base-shipped     BASE-s20260101.bin, its recorded hash and the shipped main weights are one and the same file hash
  shipped-pin      the shipped main weights (git show <commit>:<path>) hash to the pinned value

Interpretation of "wrong" (a documented choice of this script, not of the experiment): a shape block of the result schema has
targetItems, targetCorrect, decisions and harmful. Its `wrong` is targetItems - targetCorrect. It is NOT required to be <= decisions
there, because the defined-terms block counts adjective target items that are never decided (a legitimate wrong > decisions in 17 of the
48 real results). The rule `wrong <= decisions` is applied where the schema defines wrong over decided items: the `answers` of the
calibration thresholds and curve points.

Usage:
    python -B audit_runs.py --runs <folder with the 48 results> [--repo <repository root>] [--arms <arms.json>] [--figures <json>]
    python -B audit_runs.py --selftest [--selftest-dir <folder on C: for the temporary mutated copies>]

There is deliberately no default for --runs. Python standard library only. Exit codes: 0 every check passed, 1 at least one check failed
(or the self-test failed), 2 usage error.
"""

import argparse
import hashlib
import json
import os
import re
import shutil
import subprocess
import sys
import tempfile

PINNED_SHIPPED_SHA256 = "f384a877756dd2a646ce0e087ad034014f3bdfd2395b5f3751af152d2208ddad"
SHIPPED_COMMIT = "b68307f"
SHIPPED_PATH = "src/DemaConsulting.PartOfSpeech/Resources/weights.bin"
BASE_ARM = "BASE"
BASE_SEED = 20260101
EXPECTED_ARMS = 6
EXPECTED_SEEDS = 8
MAX_LINES = 12  # error lines printed per check

TRANCHES = ("deverbalHeads", "definedTerms", "stepLines", "clauseVerbs")
# Paths of the shape blocks that must exist in every result (each needs decisions > 0).
REQUIRED_SHAPES = (
    ["shapesTechnical.%s.shape" % t for t in TRANCHES]
    + ["shapesTechnical.pooledTechnical"]
    + ["shapesFollowup.%s.shape" % t for t in ("compoundHead", "compoundHead2", "headings", "headings2")]
    + ["shapesFollowup.pooledPrimary"]
)
# Dev-set token blocks: (path, name of the decisions field).
TOKEN_BLOCKS = (("openDev", "nounVerbDecisions"), ("goldDev", "nounVerbDecisions"), ("udDev", "decisions"))
SHAPE_SECTIONS = ("shapes", "shapesNew", "shapesFollowup", "shapesTechnical")
SHAPE_KEYS = ("targetItems", "targetCorrect", "decisions", "harmful")
POOLED_TECHNICAL_PARTS = ("deverbalHeads", "stepLines", "clauseVerbs")
POOLED_PRIMARY_PARTS = ("compoundHead", "compoundHead2", "headings", "headings2")
FIGURE_PATHS = (
    ["shapesTechnical.calibration.tranches.%s.judged" % t for t in TRANCHES]
    + ["shapesTechnical.calibration.technical.judged", "shapesTechnical.calibration.allSingle.judged",
       "shapesTechnical.calibration.nounVerbPooled.judged"]
)

MISSING = object()


class CheckResult:
    """The outcome of one named check: a list of error strings (empty means PASS) and a one-line detail."""

    def __init__(self, name, errors, detail):
        self.name = name
        self.errors = errors
        self.detail = detail

    @property
    def ok(self):
        """True when the check found no error."""
        return not self.errors


def sha256_bytes(data):
    """Return the lowercase hex SHA-256 of a bytes object."""
    return hashlib.sha256(data).hexdigest()


def sha256_file(path):
    """Return the lowercase hex SHA-256 of a file, read in chunks."""
    digest = hashlib.sha256()
    with open(path, "rb") as handle:
        for chunk in iter(lambda: handle.read(1 << 20), b""):
            digest.update(chunk)
    return digest.hexdigest()


def get_path(obj, dotted):
    """Return the value at a dotted path of nested dicts, or MISSING when any step is absent."""
    cur = obj
    for part in dotted.split("."):
        if not isinstance(cur, dict) or part not in cur:
            return MISSING
        cur = cur[part]
    return cur


def is_int(value):
    """True for a real integer (a bool is not accepted)."""
    return isinstance(value, int) and not isinstance(value, bool)


def is_num(value):
    """True for an int or a float (a bool is not accepted)."""
    return isinstance(value, (int, float)) and not isinstance(value, bool)


def git_shipped_sha256(repo):
    """Return (sha256 hex or None, error text or None) of the shipped main weights, read from git without touching the working tree."""
    try:
        proc = subprocess.run(["git", "-C", repo, "show", "%s:%s" % (SHIPPED_COMMIT, SHIPPED_PATH)],
                              stdout=subprocess.PIPE, stderr=subprocess.PIPE, check=False)
    except OSError as exc:
        return None, "cannot run git: %s" % exc
    if proc.returncode != 0 or not proc.stdout:
        return None, "git show %s:%s failed (exit %d): %s" % (SHIPPED_COMMIT, SHIPPED_PATH, proc.returncode,
                                                               proc.stderr.decode("utf-8", "replace").strip())
    return sha256_bytes(proc.stdout), None


def load_json_file(path):
    """Return (object, error text); the object is None when the file is missing or does not parse."""
    if not os.path.isfile(path):
        return None, "missing file %s" % os.path.basename(path)
    try:
        with open(path, "r", encoding="utf-8-sig") as handle:
            return json.load(handle), None
    except (OSError, ValueError) as exc:
        return None, "cannot parse %s: %s" % (os.path.basename(path), exc)


def normalize_sources(sources):
    """Return (set of 'NAME:WEIGHT', error) for a sources object, normalized the way the driver passes them (--source NAME:WEIGHT)."""
    if not isinstance(sources, dict):
        return None, "sources is not an object"
    out = set()
    for name, weight in sources.items():
        if not is_int(weight):
            return None, "weight of %s is not an integer (%r)" % (name, weight)
        out.add("%s:%d" % (name, weight))
    return out, None


class Context:
    """The inputs of an audit: the arms definition, the pinned figures, the results, and the hashes of the shipped weights."""

    def __init__(self, runs, arms_path, figures_path, shipped_sha256, shipped_error, pinned_sha256):
        self.runs = runs
        self.arms_path = arms_path
        self.figures_path = figures_path
        self.shipped_sha256 = shipped_sha256
        self.shipped_error = shipped_error
        self.pinned_sha256 = pinned_sha256
        self.arms = {}
        self.seeds = []
        self.arm_order = []
        self.setup_errors = []
        self.figures = {}
        self.data = {}  # (arm, seed) -> parsed result
        self.load_errors = []
        self.unexpected = []

    def load(self):
        """Read arms.json, the pinned figures and every expected result file; collect problems instead of raising."""
        spec, err = load_json_file(self.arms_path)
        if err:
            self.setup_errors.append("arms.json: " + err)
        elif not isinstance(spec, dict) or not isinstance(spec.get("arms"), list) or not isinstance(spec.get("seeds"), list):
            self.setup_errors.append("arms.json has no arms list or no seeds list")
        else:
            for arm in spec["arms"]:
                self.arms[arm["id"]] = arm
                self.arm_order.append(arm["id"])
            self.seeds = list(spec["seeds"])
            if len(self.arm_order) != EXPECTED_ARMS or len(self.seeds) != EXPECTED_SEEDS:
                self.setup_errors.append("arms.json defines %d arms x %d seeds, expected %d x %d"
                                         % (len(self.arm_order), len(self.seeds), EXPECTED_ARMS, EXPECTED_SEEDS))
            if len(set(self.seeds)) != len(self.seeds) or len(set(self.arm_order)) != len(self.arm_order):
                self.setup_errors.append("arms.json repeats an arm or a seed")
        figs, err = load_json_file(self.figures_path)
        if err:
            self.setup_errors.append("shipped_figures.json: " + err)
        elif isinstance(figs, dict) and isinstance(figs.get("figures"), list):
            for entry in figs["figures"]:
                if isinstance(entry, dict) and "path" in entry:
                    self.figures[entry["path"]] = entry.get("value")
        else:
            self.setup_errors.append("shipped_figures.json has no figures list")
        for arm in self.arm_order:
            for seed in self.seeds:
                path = os.path.join(self.runs, "%s-s%s.json" % (arm, seed))
                obj, err = load_json_file(path)
                if err:
                    self.load_errors.append(err)
                elif not isinstance(obj, dict):
                    self.load_errors.append("%s-s%s.json is not a JSON object" % (arm, seed))
                else:
                    self.data[(arm, seed)] = obj
        expected_names = {"%s-s%s.json" % (a, s) for a in self.arm_order for s in self.seeds}
        if os.path.isdir(self.runs):
            for name in sorted(os.listdir(self.runs)):
                if re.match(r"^.+-s\d+\.json$", name) and name not in expected_names:
                    self.unexpected.append(name)

    def keys(self):
        """The loaded (arm, seed) keys in grid order."""
        return [(a, s) for a in self.arm_order for s in self.seeds if (a, s) in self.data]


def check_files(ctx):
    """All 48 result files exist and parse; no unexpected result file is present."""
    errors = list(ctx.setup_errors) + list(ctx.load_errors)
    errors += ["unexpected result file %s" % n for n in ctx.unexpected]
    total = len(ctx.arm_order) * len(ctx.seeds)
    return CheckResult("files", errors, "%d of %d result files present and parsed" % (len(ctx.data), total))


def check_fields(ctx):
    """Every result carries arm, seed, sources and inputs.commit, and arm/seed agree with the file name."""
    errors = []
    for key in ctx.keys():
        res = ctx.data[key]
        label = "%s-s%s" % key
        arm, seed, sources = res.get("arm", MISSING), res.get("seed", MISSING), res.get("sources", MISSING)
        commit = get_path(res, "inputs.commit")
        if arm is MISSING:
            errors.append("%s: field `arm` is missing" % label)
        elif arm != key[0]:
            errors.append("%s: `arm` is %r, the file name says %s" % (label, arm, key[0]))
        if seed is MISSING:
            errors.append("%s: field `seed` is missing" % label)
        elif not is_int(seed) or seed != key[1]:
            errors.append("%s: `seed` is %r, the file name says %s" % (label, seed, key[1]))
        if sources is MISSING:
            errors.append("%s: field `sources` is missing" % label)
        elif not isinstance(sources, dict):
            errors.append("%s: `sources` is not an object" % label)
        if commit is MISSING:
            errors.append("%s: field `inputs.commit` is missing" % label)
        elif not isinstance(commit, str) or not re.match(r"^[0-9a-f]{40}$", commit):
            errors.append("%s: `inputs.commit` is not a 40-digit hex commit (%r)" % (label, commit))
    return CheckResult("fields", errors, "arm, seed, sources and inputs.commit present in %d results" % len(ctx.data))


def check_grid(ctx):
    """The (arm, seed) pairs recorded inside the results are unique and complete."""
    errors = []
    seen = {}
    for key in ctx.keys():
        res = ctx.data[key]
        if "arm" not in res or "seed" not in res:
            continue  # reported by `fields`
        pair = (res["arm"], res["seed"])
        seen.setdefault(pair, []).append("%s-s%s.json" % key)
    for pair, files in sorted(seen.items(), key=lambda kv: str(kv[0])):
        if len(files) > 1:
            errors.append("duplicate (arm, seed) %s in %s" % (pair, ", ".join(files)))
    expected = {(a, s) for a in ctx.arm_order for s in ctx.seeds}
    for pair in sorted(expected - set(seen), key=str):
        errors.append("missing (arm, seed) %s" % (pair,))
    for pair in sorted(set(seen) - expected, key=str):
        errors.append("unexpected (arm, seed) %s in %s" % (pair, ", ".join(seen[pair])))
    return CheckResult("grid", errors, "%d unique (arm, seed) pairs, %d expected" % (len(seen), len(expected)))


def check_sources(ctx):
    """The sources of each result equal the arms.json sources of its arm (sets of NAME:WEIGHT)."""
    errors = []
    for key in ctx.keys():
        res = ctx.data[key]
        label = "%s-s%s" % key
        if "sources" not in res:
            continue  # reported by `fields`
        got, err = normalize_sources(res["sources"])
        want, werr = normalize_sources(ctx.arms[key[0]].get("sources", {}))
        if err or werr:
            errors.append("%s: %s" % (label, err or "arms.json: " + werr))
        elif got != want:
            errors.append("%s: sources %s differ from arms.json %s" % (label, sorted(got), sorted(want)))
    return CheckResult("sources", errors, "sources match arms.json for %d results (BASE has none)" % len(ctx.data))


def check_features(ctx):
    """The features of each result equal the arms.json features of its arm."""
    errors = []
    for key in ctx.keys():
        res = ctx.data[key]
        want = ctx.arms[key[0]].get("features")
        if res.get("features", MISSING) != want:
            errors.append("%s-s%s: features %r differ from arms.json %r" % (key[0], key[1], res.get("features", MISSING), want))
    return CheckResult("features", errors, "features match arms.json for %d results" % len(ctx.data))


def check_provenance(ctx):
    """One commit for all runs, never dirty, driver and inputs agree, and identical input hashes across all runs."""
    errors = []
    commits, reference = set(), None
    for key in ctx.keys():
        res = ctx.data[key]
        label = "%s-s%s" % key
        inputs = res.get("inputs")
        driver = res.get("driver")
        if not isinstance(inputs, dict) or not isinstance(driver, dict):
            errors.append("%s: `inputs` or `driver` is missing" % label)
            continue
        commits.add(inputs.get("commit"))
        if inputs.get("dirty") is not False:
            errors.append("%s: inputs.dirty is %r, not false" % (label, inputs.get("dirty")))
        if driver.get("dirty") is not False:
            errors.append("%s: driver.dirty is %r, not false" % (label, driver.get("dirty")))
        if driver.get("commit") != inputs.get("commit"):
            errors.append("%s: driver.commit %r differs from inputs.commit %r" % (label, driver.get("commit"), inputs.get("commit")))
        hashes = {k: v for k, v in inputs.items() if k not in ("commit", "dirty")}
        if reference is None:
            reference = hashes
        elif hashes != reference:
            diff = sorted(k for k in set(hashes) | set(reference) if hashes.get(k) != reference.get(k))
            errors.append("%s: input hashes differ from the first result in %s" % (label, ", ".join(diff)))
    if len(commits) > 1:
        errors.append("more than one repository commit in the results: %s" % sorted(str(c) for c in commits))
    return CheckResult("provenance", errors, "one commit %s, clean tree, %d identical input hashes"
                       % (", ".join(sorted(str(c)[:10] for c in commits)) or "none", len(reference or {})))


def shape_blocks(node, path, out):
    """Collect every dict that has the four shape keys (targetItems, targetCorrect, decisions, harmful), with its dotted path."""
    if isinstance(node, dict):
        if all(k in node for k in SHAPE_KEYS):
            out.append((path, node))
        for key, value in node.items():
            shape_blocks(value, path + "." + key if path else key, out)


def check_shape_block(label, path, block, errors):
    """Apply the count invariants to one shape block; kinds blocks may legitimately have zero decisions."""
    where = "%s %s" % (label, path)
    for key in SHAPE_KEYS:
        if not is_int(block[key]) or block[key] < 0:
            errors.append("%s: %s is not a non-negative integer (%r)" % (where, key, block[key]))
            return
    wrong = block["targetItems"] - block["targetCorrect"]
    if wrong < 0:
        errors.append("%s: targetCorrect %d > targetItems %d" % (where, block["targetCorrect"], block["targetItems"]))
    if ".kinds." not in path and block["decisions"] <= 0:
        errors.append("%s: decisions is %d (must be > 0)" % (where, block["decisions"]))
    if block["decisions"] > block["targetItems"]:
        errors.append("%s: decisions %d > targetItems %d" % (where, block["decisions"], block["targetItems"]))
    if block["harmful"] > wrong:
        errors.append("%s: harmful %d > wrong %d" % (where, block["harmful"], wrong))
    if block["harmful"] > block["decisions"]:
        errors.append("%s: harmful %d > decisions %d" % (where, block["harmful"], block["decisions"]))
    if "controlItems" in block and "controlCorrect" in block:
        if not (is_int(block["controlItems"]) and is_int(block["controlCorrect"]) and 0 <= block["controlCorrect"] <= block["controlItems"]):
            errors.append("%s: controlCorrect %r outside 0..controlItems %r" % (where, block["controlCorrect"], block["controlItems"]))


def check_token_block(label, path, decisions_key, block, errors):
    """Apply the invariants to a dev-set token block (openDev, goldDev, udDev)."""
    where = "%s %s" % (label, path)
    if not isinstance(block, dict) or not all(k in block for k in ("tokens", "accuracy", decisions_key, "harmful")):
        errors.append("%s: block or one of tokens, accuracy, %s, harmful is missing" % (where, decisions_key))
        return
    tokens, acc, dec, harm = block["tokens"], block["accuracy"], block[decisions_key], block["harmful"]
    if not (is_int(tokens) and is_int(dec) and is_int(harm) and is_num(acc)) or tokens <= 0:
        errors.append("%s: tokens, %s or harmful is not a valid number" % (where, decisions_key))
        return
    if dec <= 0:
        errors.append("%s: %s is %d (must be > 0)" % (where, decisions_key, dec))
    if harm > dec:
        errors.append("%s: harmful %d > %s %d" % (where, harm, decisions_key, dec))
    wrong = tokens * (1 - acc / 100.0)
    if harm > wrong + 0.5:
        errors.append("%s: harmful %d > wrong tokens %.1f (tokens %d, accuracy %.4f)" % (where, harm, wrong, tokens, acc))


def check_calibration_set(label, name, node, errors):
    """Apply the invariants to one calibration set: points (thresholds, curve) and bins against `judged`."""
    where = "%s calibration.%s" % (label, name)
    if not isinstance(node, dict) or not is_int(node.get("judged")) or node["judged"] <= 0:
        errors.append("%s: judged is missing or not a positive integer" % where)
        return
    judged = node["judged"]
    for series in ("thresholds", "curve"):
        for point in node.get(series, []):
            if not (is_int(point.get("answers")) and is_int(point.get("wrong"))):
                errors.append("%s %s: `answers` or `wrong` missing or not an integer in %r" % (where, series, point))
                continue
            at = "%s %s@%s" % (where, series, point.get("threshold"))
            if not 0 < point["answers"] <= judged:
                errors.append("%s: answers %d outside 1..judged %d" % (at, point["answers"], judged))
            if point["wrong"] > point["answers"]:
                errors.append("%s: wrong %d > answers %d" % (at, point["wrong"], point["answers"]))
            if is_int(point.get("harmful")):
                if point["harmful"] > point["wrong"]:
                    errors.append("%s: harmful %d > wrong %d" % (at, point["harmful"], point["wrong"]))
                if is_int(point.get("nounVerbAnswers")) and point["harmful"] > point["nounVerbAnswers"]:
                    errors.append("%s: harmful %d > nounVerbAnswers %d" % (at, point["harmful"], point["nounVerbAnswers"]))
    if "bins" in node:
        total = 0
        for index, item in enumerate(node["bins"]):
            if not (is_int(item.get("count")) and is_int(item.get("harmful")) and is_num(item.get("correctPercent"))):
                errors.append("%s bin %d: count, harmful or correctPercent missing" % (where, index))
                continue
            total += item["count"]
            if item["harmful"] > item["count"]:
                errors.append("%s bin %d: harmful %d > count %d" % (where, index, item["harmful"], item["count"]))
            if item["harmful"] > item["count"] * (1 - item["correctPercent"] / 100.0) + 0.51:
                errors.append("%s bin %d: harmful %d > wrong in the bin" % (where, index, item["harmful"]))
        if total != judged:
            errors.append("%s: bin counts sum to %d, judged is %d" % (where, total, judged))


def check_pooled_sums(res, label, errors):
    """A pooled block equals the sum of its parts, and the calibration decisions equal the pooled technical decisions."""
    for pooled_path, root, parts in (("shapesTechnical.pooledTechnical", "shapesTechnical", POOLED_TECHNICAL_PARTS),
                                     ("shapesFollowup.pooledPrimary", "shapesFollowup", POOLED_PRIMARY_PARTS)):
        pooled = get_path(res, pooled_path)
        part_blocks = [get_path(res, "%s.%s.shape" % (root, p)) for p in parts]
        if isinstance(pooled, dict) and all(isinstance(p, dict) for p in part_blocks):
            for fld in SHAPE_KEYS:
                total = sum(p.get(fld, 0) for p in part_blocks)
                if pooled.get(fld) != total:
                    errors.append("%s: %s.%s is %r but its parts sum to %r" % (label, pooled_path, fld, pooled.get(fld), total))
    nvp = get_path(res, "shapesTechnical.calibration.nounVerbPooled.decisions")
    pooled_dec = get_path(res, "shapesTechnical.pooledTechnical.decisions")
    if nvp != pooled_dec:
        errors.append("%s: calibration.nounVerbPooled.decisions %r differs from pooledTechnical.decisions %r" % (label, nvp, pooled_dec))


def check_counts(ctx):
    """Count invariants for every result: shape blocks, token blocks, calibration sets and pooled sums."""
    errors = []
    blocks = 0
    for key in ctx.keys():
        res = ctx.data[key]
        label = "%s-s%s" % key
        for path in REQUIRED_SHAPES:
            if not isinstance(get_path(res, path), dict):
                errors.append("%s: required block %s is missing" % (label, path))
        found = []
        for section in SHAPE_SECTIONS:
            if section in res:
                shape_blocks(res[section], section, found)
        for path, block in found:
            check_shape_block(label, path, block, errors)
        blocks += len(found)
        for path, dec_key in TOKEN_BLOCKS:
            check_token_block(label, path, dec_key, res.get(path), errors)
        calibration = get_path(res, "shapesTechnical.calibration")
        if not isinstance(calibration, dict):
            errors.append("%s: shapesTechnical.calibration is missing" % label)
            continue
        for name in ("technical", "allSingle", "nounVerbPooled"):
            check_calibration_set(label, name, calibration.get(name), errors)
        for name in TRANCHES:
            check_calibration_set(label, "tranches." + name, get_path(calibration, "tranches." + name), errors)
        check_pooled_sums(res, label, errors)
    return CheckResult("counts", errors, "decisions > 0, harmful <= wrong, wrong <= answers over %d shape blocks in %d results"
                       % (blocks, len(ctx.data)))


def check_tranches(ctx):
    """Tranche totals equal the pinned sizes; the tranche sums equal the technical total; sizes agree with target + control items."""
    errors = []
    pinned = {}
    for path in FIGURE_PATHS:
        value = ctx.figures.get(path)
        if not is_int(value):
            errors.append("shipped_figures.json has no integer figure for %s" % path)
        else:
            pinned[path] = value
    if not errors:
        tranche_sum = sum(pinned["shapesTechnical.calibration.tranches.%s.judged" % t] for t in TRANCHES)
        if tranche_sum != pinned["shapesTechnical.calibration.technical.judged"]:
            errors.append("pinned tranche sizes sum to %d, pinned technical total is %d"
                          % (tranche_sum, pinned["shapesTechnical.calibration.technical.judged"]))
        for key in ctx.keys():
            res = ctx.data[key]
            label = "%s-s%s" % key
            for path, want in pinned.items():
                got = get_path(res, path)
                if got != want:
                    errors.append("%s: %s is %r, pinned size is %d" % (label, path, got, want))
            for tranche in TRANCHES:
                judged = get_path(res, "shapesTechnical.calibration.tranches.%s.judged" % tranche)
                shape = get_path(res, "shapesTechnical.%s.shape" % tranche)
                if isinstance(shape, dict) and is_int(shape.get("targetItems")) and is_int(shape.get("controlItems")):
                    if judged != shape["targetItems"] + shape["controlItems"]:
                        errors.append("%s: tranche %s judged %r != targetItems %d + controlItems %d"
                                      % (label, tranche, judged, shape["targetItems"], shape["controlItems"]))
                else:
                    errors.append("%s: shape block of tranche %s lacks targetItems or controlItems" % (label, tranche))
    sizes = ", ".join("%s %s" % (t, ctx.figures.get("shapesTechnical.calibration.tranches.%s.judged" % t)) for t in TRANCHES)
    return CheckResult("tranches", errors, "tranche totals equal the pinned sizes (%s)" % sizes)


def check_bins(ctx):
    """Every <arm>-s<seed>.bin exists and its SHA-256 equals the hash recorded in the result."""
    errors = []
    checked = 0
    for arm in ctx.arm_order:
        for seed in ctx.seeds:
            label = "%s-s%s" % (arm, seed)
            path = os.path.join(ctx.runs, label + ".bin")
            if not os.path.isfile(path):
                errors.append("%s: missing weights file %s.bin" % (label, label))
                continue
            recorded = get_path(ctx.data.get((arm, seed), {}), "weights.sha256")
            if not isinstance(recorded, str):
                errors.append("%s: weights.sha256 is missing in the result" % label)
                continue
            actual = sha256_file(path)
            checked += 1
            if actual != recorded.lower():
                errors.append("%s: file SHA-256 %s differs from the recorded %s" % (label, actual, recorded))
    return CheckResult("bins", errors, "%d weights files hash to the SHA-256 recorded in their results" % checked)


def check_base_shipped(ctx):
    """BASE-s20260101: file hash, recorded hash, git hash of the shipped weights and the pinned value are one value."""
    errors = []
    label = "%s-s%d" % (BASE_ARM, BASE_SEED)
    path = os.path.join(ctx.runs, label + ".bin")
    recorded = get_path(ctx.data.get((BASE_ARM, BASE_SEED), {}), "weights.sha256")
    actual = sha256_file(path) if os.path.isfile(path) else None
    if actual is None:
        errors.append("%s.bin is missing" % label)
    if not isinstance(recorded, str):
        errors.append("%s: weights.sha256 is not recorded in the result" % label)
        recorded = None
    if ctx.shipped_sha256 is None:
        errors.append("shipped weights hash unavailable: %s" % ctx.shipped_error)
    if actual and ctx.pinned_sha256 and actual != ctx.pinned_sha256:
        errors.append("%s.bin SHA-256 %s != pinned %s" % (label, actual, ctx.pinned_sha256))
    if recorded and ctx.pinned_sha256 and recorded.lower() != ctx.pinned_sha256:
        errors.append("recorded hash %s != pinned %s" % (recorded, ctx.pinned_sha256))
    if recorded and ctx.shipped_sha256 and recorded.lower() != ctx.shipped_sha256:
        errors.append("recorded hash %s != shipped weights %s (git %s)" % (recorded, ctx.shipped_sha256, SHIPPED_COMMIT))
    if actual and ctx.shipped_sha256 and actual != ctx.shipped_sha256:
        errors.append("%s.bin SHA-256 %s != shipped weights %s (git %s)" % (label, actual, ctx.shipped_sha256, SHIPPED_COMMIT))
    return CheckResult("base-shipped", errors, "%s.bin == recorded == shipped (git %s) == pinned %s" % (label, SHIPPED_COMMIT, ctx.pinned_sha256))


def check_shipped_pin(ctx):
    """The shipped main weights, read from git, hash to the pinned value."""
    errors = []
    if ctx.shipped_sha256 is None:
        errors.append("cannot read the shipped weights: %s" % ctx.shipped_error)
    elif ctx.shipped_sha256 != ctx.pinned_sha256:
        errors.append("git %s:%s hashes to %s, pinned %s" % (SHIPPED_COMMIT, SHIPPED_PATH, ctx.shipped_sha256, ctx.pinned_sha256))
    return CheckResult("shipped-pin", errors, "git %s:%s hashes to the pinned %s" % (SHIPPED_COMMIT, SHIPPED_PATH, ctx.pinned_sha256))


CHECKS = (check_files, check_fields, check_grid, check_sources, check_features, check_provenance, check_counts, check_tranches,
          check_bins, check_base_shipped, check_shipped_pin)


def run_audit(runs, arms_path, figures_path, shipped_sha256, shipped_error, pinned_sha256):
    """Run every check and return the list of CheckResult; nothing is printed and nothing is raised for bad input data."""
    ctx = Context(runs, arms_path, figures_path, shipped_sha256, shipped_error, pinned_sha256)
    ctx.load()
    results = []
    for check in CHECKS:
        try:
            results.append(check(ctx))
        except Exception as exc:  # fail closed: a crashing check is a failed check
            results.append(CheckResult(check.__name__.replace("check_", "").replace("_", "-"),
                                       ["check crashed on unexpected data: %s: %s" % (type(exc).__name__, exc)], ""))
    return results


def print_results(results, out=sys.stdout):
    """Print one PASS or FAIL line per check (with up to MAX_LINES error lines) and a summary; return True when all passed."""
    for res in results:
        if res.ok:
            print("PASS  %-13s %s" % (res.name, res.detail), file=out)
        else:
            print("FAIL  %-13s %d problem(s)" % (res.name, len(res.errors)), file=out)
            for line in res.errors[:MAX_LINES]:
                print("        - %s" % line, file=out)
            if len(res.errors) > MAX_LINES:
                print("        - ... and %d more" % (len(res.errors) - MAX_LINES), file=out)
    failed = [r.name for r in results if not r.ok]
    print("SUMMARY: %d checks, %d passed, %d failed -> %s" % (len(results), len(results) - len(failed), len(failed),
                                                           "PASS" if not failed else "FAIL (" + ", ".join(failed) + ")"), file=out)
    return not failed


# ----------------------------------------------------------------------------------------------------------------------------------
# Self-test: synthetic results (same field names as the real eval-arm schema, only the fields this audit reads), tampered copies.
# ----------------------------------------------------------------------------------------------------------------------------------

SYN_COMMIT = "0123456789abcdef0123456789abcdef01234567"
SYN_ARMS = [
    {"id": "BASE", "role": "baseline", "features": ["clauseCues"], "sources": {}},
    {"id": "TECH", "role": "candidate", "features": ["clauseCues"], "sources": {"deverbal": 1, "terms": 1, "steps": 1, "clauses": 1}},
    {"id": "TECH-NT", "role": "attribution", "features": ["clauseCues"], "sources": {"deverbal": 1, "steps": 1, "clauses": 1}},
    {"id": "TECH-ND", "role": "candidate", "features": ["clauseCues"], "sources": {"terms": 1, "steps": 1, "clauses": 1}},
    {"id": "TECH-T2", "role": "candidate", "features": ["clauseCues"], "sources": {"deverbal": 1, "terms": 2, "steps": 1, "clauses": 1}},
    {"id": "TECH-W2", "role": "candidate", "features": ["clauseCues"], "sources": {"deverbal": 2, "terms": 2, "steps": 2, "clauses": 2}},
]
SYN_SEEDS = [20260101, 11, 12, 13, 14, 15, 16, 17]
SYN_SIZES = {"deverbalHeads": (360, 90), "definedTerms": (215, 70), "stepLines": (166, 70), "clauseVerbs": (356, 90)}
SYN_DECISIONS = {"deverbalHeads": 360, "definedTerms": 99, "stepLines": 155, "clauseVerbs": 302}


def syn_shape(target, decisions, harmful, correct, control):
    """A synthetic shape block with consistent counts."""
    return {"targetItems": target, "targetCorrect": correct, "decisions": decisions, "harmful": harmful,
            "controlItems": control, "controlCorrect": control}


def syn_calibration(judged):
    """A synthetic calibration set: one threshold, one curve point and two bins whose counts sum to `judged`."""
    return {"judged": judged,
            "thresholds": [{"threshold": 0.9, "answers": judged // 2, "wrong": judged // 20}],
            "curve": [{"threshold": 0.45, "answers": judged - 1, "wrong": judged // 4, "nounVerbAnswers": judged // 2, "harmful": judged // 10}],
            "bins": [{"count": 10, "harmful": 2, "correctPercent": 50.0}, {"count": judged - 10, "harmful": 3, "correctPercent": 90.0}]}


def syn_pooled(names, section):
    """The pooled block of the named synthetic shape blocks (sum of the four count fields)."""
    blocks = [section[n]["shape"] for n in names]
    return {k: sum(b[k] for b in blocks) for k in SHAPE_KEYS}


def syn_result(arm, seed, sources, features, bin_sha):
    """Build a synthetic result that satisfies every check."""
    technical = {}
    for name, (target, control) in SYN_SIZES.items():
        technical[name] = {"shape": syn_shape(target, SYN_DECISIONS[name], 5, target - 8, control), "kinds": {}}
    technical["pooledTechnical"] = syn_pooled(POOLED_TECHNICAL_PARTS, technical)
    followup = {n: {"shape": syn_shape(100, 90, 4, 92, 20), "kinds": {}} for n in POOLED_PRIMARY_PARTS}
    followup["pooledPrimary"] = syn_pooled(POOLED_PRIMARY_PARTS, followup)
    judged = {n: sum(SYN_SIZES[n]) for n in SYN_SIZES}
    technical["calibration"] = {
        "technical": syn_calibration(sum(judged.values())),
        "allSingle": syn_calibration(2148),
        "nounVerbPooled": {"judged": 882, "decisions": technical["pooledTechnical"]["decisions"]},
        "tranches": {n: syn_calibration(j) for n, j in judged.items()},
    }
    return {
        "schema": 1, "label": "%s-s%s" % (arm, seed), "weights": {"sha256": bin_sha},
        "openDev": {"tokens": 26232, "accuracy": 96.7, "nounVerbDecisions": 4451, "harmful": 90},
        "goldDev": {"tokens": 2017, "accuracy": 95.4, "nounVerbDecisions": 611, "harmful": 8},
        "udDev": {"tokens": 25148, "accuracy": 93.0, "decisions": 4570, "harmful": 92},
        "shapesFollowup": followup, "shapesTechnical": technical,
        "inputs": {"devSha256": "aa" * 32, "commit": SYN_COMMIT, "dirty": False},
        "arm": arm, "seed": seed, "features": list(features), "sources": dict(sources),
        "driver": {"commit": SYN_COMMIT, "dirty": False, "priority": "Normal"},
    }


def write_json(path, obj):
    """Write a JSON file (UTF-8, no byte order mark)."""
    with open(path, "w", encoding="utf-8") as handle:
        json.dump(obj, handle)


def build_synthetic(base):
    """Create the passing synthetic experiment under `base`; return (runs, arms, figures, shipped_sha256)."""
    runs = os.path.join(base, "runs")
    os.makedirs(runs)
    arms = os.path.join(base, "arms.json")
    write_json(arms, {"seeds": SYN_SEEDS, "arms": SYN_ARMS})
    judged = {n: sum(SYN_SIZES[n]) for n in SYN_SIZES}
    figs = [{"path": "shapesTechnical.calibration.tranches.%s.judged" % n, "value": j} for n, j in judged.items()]
    figs += [{"path": "shapesTechnical.calibration.technical.judged", "value": sum(judged.values())},
             {"path": "shapesTechnical.calibration.allSingle.judged", "value": 2148},
             {"path": "shapesTechnical.calibration.nounVerbPooled.judged", "value": 882}]
    figures = os.path.join(base, "shipped_figures.json")
    write_json(figures, {"figures": figs})
    shipped = b"synthetic shipped weights"
    for arm in SYN_ARMS:
        for seed in SYN_SEEDS:
            data = shipped if (arm["id"], seed) == (BASE_ARM, BASE_SEED) else ("%s-%s" % (arm["id"], seed)).encode()
            with open(os.path.join(runs, "%s-s%s.bin" % (arm["id"], seed)), "wb") as handle:
                handle.write(data)
            write_json(os.path.join(runs, "%s-s%s.json" % (arm["id"], seed)),
                       syn_result(arm["id"], seed, arm["sources"], arm["features"], sha256_bytes(data)))
    return runs, arms, figures, sha256_bytes(shipped)


def edit_result(runs, arm, seed, mutate):
    """Load one synthetic result, apply `mutate(dict)` and write it back."""
    path = os.path.join(runs, "%s-s%s.json" % (arm, seed))
    with open(path, "r", encoding="utf-8") as handle:
        data = json.load(handle)
    mutate(data)
    write_json(path, data)


def drop(path_parts):
    """Return a mutation that deletes the nested key given by path_parts."""
    def apply(data):
        cur = data
        for part in path_parts[:-1]:
            cur = cur[part]
        del cur[path_parts[-1]]
    return apply


def setter(path_parts, value):
    """Return a mutation that sets the nested key given by path_parts."""
    def apply(data):
        cur = data
        for part in path_parts[:-1]:
            cur = cur[part]
        cur[path_parts[-1]] = value
    return apply


def selftest_scenarios():
    """Return the tamper scenarios: (description, expected failing check, mutate(runs, shipped) -> shipped hash override or None)."""
    def result_edit(arm, seed, mutation):
        def apply(runs, shipped):
            edit_result(runs, arm, seed, mutation)
        return apply

    def remove(name):
        def apply(runs, shipped):
            os.remove(os.path.join(runs, name))
        return apply

    def duplicate(runs, shipped):
        edit_result(runs, "TECH", 12, lambda d: d.update({"seed": 11}))

    def missing_pair(runs, shipped):
        edit_result(runs, "TECH", 12, lambda d: d.update({"seed": 99}))

    def altered_bin(runs, shipped):
        with open(os.path.join(runs, "BASE-s20260101.bin"), "ab") as handle:
            handle.write(b"x")

    def recorded_not_shipped(runs, shipped):
        other = b"another set of weights"
        with open(os.path.join(runs, "BASE-s20260101.bin"), "wb") as handle:
            handle.write(other)
        edit_result(runs, "BASE", BASE_SEED, setter(["weights", "sha256"], sha256_bytes(other)))

    def shipped_not_pinned(runs, shipped):
        return "0" * 64

    def extra_file(runs, shipped):
        write_json(os.path.join(runs, "TECH-s99.json"), {"arm": "TECH", "seed": 99})

    tech = ("TECH", 11)
    return [
        ("missing result file", "files", remove("TECH-s11.json")),
        ("unexpected extra result file", "files", extra_file),
        ("missing arm", "fields", result_edit(*tech, drop(["arm"]))),
        ("missing seed", "fields", result_edit(*tech, drop(["seed"]))),
        ("missing sources", "fields", result_edit(*tech, drop(["sources"]))),
        ("missing inputs.commit", "fields", result_edit(*tech, drop(["inputs", "commit"]))),
        ("duplicate (arm, seed)", "grid", duplicate),
        ("missing (arm, seed)", "grid", missing_pair),
        ("wrong sources for an arm", "sources", result_edit(*tech, setter(["sources", "terms"], 2))),
        ("wrong features for an arm", "features", result_edit(*tech, setter(["features"], []))),
        ("dirty tree", "provenance", result_edit(*tech, setter(["inputs", "dirty"], True))),
        ("mixed commits", "provenance", result_edit(*tech, setter(["inputs", "commit"], "f" * 40))),
        ("decisions == 0", "counts", result_edit(*tech, setter(["shapesTechnical", "stepLines", "shape", "decisions"], 0))),
        ("wrong > decisions (calibration answers)", "counts",
         result_edit(*tech, setter(["shapesTechnical", "calibration", "technical", "thresholds"], [{"threshold": 0.9, "answers": 10, "wrong": 11}]))),
        ("harmful > wrong", "counts", result_edit(*tech, setter(["shapesTechnical", "stepLines", "shape", "harmful"], 90))),
        ("harmful > decisions", "counts", result_edit(*tech, setter(["openDev", "harmful"], 5000))),
        ("pooled block differs from its parts", "counts", result_edit(*tech, setter(["shapesTechnical", "pooledTechnical", "harmful"], 1))),
        ("tranche total != size", "tranches", result_edit(*tech, setter(["shapesTechnical", "calibration", "tranches", "stepLines", "judged"], 235))),
        ("missing BASE bin", "base-shipped", remove("BASE-s20260101.bin")),
        ("missing other bin", "bins", remove("TECH-s11.bin")),
        ("BASE bin altered (hash differs from recorded)", "base-shipped", altered_bin),
        ("BASE bin altered (bins check)", "bins", altered_bin),
        ("recorded hash != shipped hash", "base-shipped", recorded_not_shipped),
        ("shipped hash (git) != pinned hash", "shipped-pin", shipped_not_pinned),
    ]


def run_selftest(work_base):
    """Prove that the passing synthetic set passes and that every tampered copy fails the check it targets; return True on success."""
    root = tempfile.mkdtemp(prefix="audit-selftest-", dir=work_base)
    good = True
    try:
        base_dir = os.path.join(root, "baseline")
        runs, arms, figures, shipped = build_synthetic(base_dir)
        pinned = shipped
        results = run_audit(runs, arms, figures, shipped, None, pinned)
        passed = all(r.ok for r in results)
        print("%s  baseline: untampered synthetic set passes all %d checks" % ("PASS" if passed else "FAIL", len(results)))
        if not passed:
            good = False
            print_results(results)
        for index, (description, expected, mutate) in enumerate(selftest_scenarios()):
            case = os.path.join(root, "case-%02d" % index)
            shutil.copytree(base_dir, case)
            case_runs = os.path.join(case, "runs")
            override = mutate(case_runs, shipped)
            case_shipped = override if override is not None else shipped
            case_results = run_audit(case_runs, os.path.join(case, "arms.json"), os.path.join(case, "shipped_figures.json"),
                                     case_shipped, None, pinned)
            by_name = {r.name: r for r in case_results}
            hit = expected in by_name and not by_name[expected].ok
            others = [r.name for r in case_results if not r.ok and r.name != expected]
            print("%s  tamper: %-48s -> check `%s` fails%s" % ("PASS" if hit else "FAIL", description, expected,
                                                               (" (also failing: %s)" % ", ".join(others)) if others else ""))
            if not hit:
                good = False
        results = run_audit(runs, arms, figures, None, "git unavailable", pinned)
        hit = any(r.name == "shipped-pin" and not r.ok for r in results) and any(r.name == "base-shipped" and not r.ok for r in results)
        print("%s  tamper: %-48s -> checks `shipped-pin` and `base-shipped` fail" % ("PASS" if hit else "FAIL", "shipped weights unreadable from git"))
        good = good and hit
        empty = os.path.join(root, "empty")
        os.makedirs(empty)
        results = run_audit(empty, arms, figures, shipped, None, pinned)
        hit = not all(r.ok for r in results) and any(r.name == "files" and not r.ok for r in results)
        print("%s  tamper: %-48s -> check `files` fails" % ("PASS" if hit else "FAIL", "empty runs folder"))
        good = good and hit
    finally:
        shutil.rmtree(root, ignore_errors=True)
    print("SELFTEST: %s" % ("PASS (the audit accepts the untampered set and rejects every tampered copy)" if good else "FAIL"))
    return good


def main(argv=None):
    """Command line entry point; returns the process exit code."""
    here = os.path.dirname(os.path.abspath(__file__))
    tech_dir = os.path.join(os.path.dirname(here), "beta3-tech")
    parser = argparse.ArgumentParser(description="Independent fail-closed audit of the E2 run inputs (see the module docstring).")
    parser.add_argument("--runs", help="folder with the 48 result files and weights files (required; there is no default)")
    parser.add_argument("--repo", default=os.path.abspath(os.path.join(here, "..", "..", "..")), help="repository root used for git show")
    parser.add_argument("--arms", default=os.path.join(tech_dir, "arms.json"), help="arms.json of the experiment")
    parser.add_argument("--figures", default=os.path.join(tech_dir, "shipped_figures.json"), help="shipped_figures.json (pinned tranche sizes)")
    parser.add_argument("--selftest", action="store_true", help="run the tamper self-test on synthetic data and exit")
    parser.add_argument("--selftest-dir", default=None, help="folder for the temporary mutated copies (default: the system temp folder)")
    args = parser.parse_args(argv)
    if args.selftest:
        return 0 if run_selftest(args.selftest_dir) else 1
    if not args.runs:
        parser.error("--runs is required")
    shipped, shipped_error = git_shipped_sha256(args.repo)
    results = run_audit(args.runs, args.arms, args.figures, shipped, shipped_error, PINNED_SHIPPED_SHA256)
    return 0 if print_results(results) else 1


if __name__ == "__main__":
    sys.exit(main())

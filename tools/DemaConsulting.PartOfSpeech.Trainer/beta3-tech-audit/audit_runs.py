#!/usr/bin/env python3
"""Independent, fail-closed audit of the run inputs of the beta.3 phase 2 technical-mix experiment (E2).

This script is NOT part of the pre-registered, hash-pinned analysis set (beta3-tech/). It was written after the 48 runs and after the
analysis, it reads the same files the analyzer reads, and it changes no rule, no number and no decision. It exists because the frozen
analyzer (beta3-tech/analyze_tech.py) validates some inputs only weakly; see README.md in this folder. A future harness must make these
checks fail closed inside its own analyzer.

Checks (every check prints one PASS or FAIL line; any FAIL makes the exit code 1). Every check is fail closed: a missing field, series,
row, block or file is an error, never a default, and a check with nothing to check fails.

  files            the runs folder holds exactly the 48 result files <arm>-s<seed>.json (6 arms x 8 seeds of arms.json), their 48 weights
                   files, and optionally the files of the documented allowlist (ALLOWED_FILES and the four per-run logs of each run);
                   every other entry (file, directory, link) is an error; every result parses as strict JSON (no NaN, no duplicate key)
  fields           every result carries `arm`, `seed`, `sources` and `inputs.commit`; arm and seed agree with the file name
  grid             the (arm, seed) pairs found in the results are unique and complete
  sources          the `sources` of every result equal those of beta3-tech/arms.json for that arm (sets of NAME:WEIGHT; BASE has none)
  features         the `features` of every result equal those of arms.json for that arm
  provenance       `inputs` has exactly the expected 16 keys (the 14 *Sha256 fields of the frozen development files, `commit`, `dirty`);
                   every hash is a 64-digit lowercase hex string and equals the frozen value (FROZEN_INPUTS), so it is identical in all
                   runs; one repository commit, never a dirty tree, driver and inputs agree
  counts           the exact set of 49 shape blocks, per run: decisions > 0 for every pooled or per-shape block, harmful <= wrong,
                   harmful <= decisions; the dev-set token blocks (and their token counts across runs); every calibration series
                   (thresholds, curve, bins of the technical, allSingle, nounVerbPooled and four tranche sets) present with the expected
                   rows, thresholds and fields, wrong <= answers, bin counts equal judged; pooled blocks equal the sum of their parts
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
import math
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

HEX64 = re.compile(r"^[0-9a-f]{64}$")

# Frozen SHA-256 of the fourteen development files that every run scored (inputs.*Sha256 of each result). These are the values of
# FROZEN_INPUTS of the frozen analyzer (beta3-tech/analyze_tech.py) and of the E2 pre-registration in RUN-LOG.md, copied here (read only).
FROZEN_INPUTS = {
    "devSha256": "bbc94e42693d92a7e948598d5ea8a2c58274d3e4e5e6e4d0ed7d8a5c10963290",
    "hardSha256": "c041ae3d8b4b948326d002176bdffa0e5eb2ac0a6fe7d12daed7c3d289db8b22",
    "udDevSha256": "0fc0b4a371ef6d0437e01e396762cdb600d0d7ff0d725ead00ac9f3c41f82037",
    "openDevSha256": "8caa822515cee179093f068ff4ea7fa62714830d4c50de696bd4403037fc05c7",
    "goldDevSha256": "ffb2f3afd3460523656f5528a66094bad507982b15eb2b4a9341cdb78ff117f5",
    "compoundHeadSha256": "537e0be3923f585b8f084de4e88f4064624e4f5dc484f84ccd303fed3db924f5",
    "headingsSha256": "45a62a71837f02d1e76f0ad55a71a1770cb628ea8ca8765c0d4583f49b9da72a",
    "namedStateSha256": "e08472387630368b7fb3fbaa5cd7b6187d0760608c93b55f19250c98f2d06d6e",
    "compoundHead2Sha256": "ffa7e5aef0cff05e6ef9227bce3544e4372109768685fcff8c0395e18220c527",
    "headings2Sha256": "6c84419851bceebcb825fff6bc530fc3a3dc5bc5306da152bda349db00c4b4a7",
    "deverbalHeadsSha256": "6159099f5fc59187f9486070439e552358abf19fb95742e20c54dbcf17400c5a",
    "definedTermsSha256": "6bca83a8e63589152cf3a37661e328a187323612b06344c6c402921f1c2478c8",
    "stepLinesSha256": "c06bc3138b92cd3baafec1a9a210110daafee69846dd83c77ba70b4fc09f4ad8",
    "clauseVerbsSha256": "d35e7a259140614d6f0057fe7f3fd5d545cdf478971ac7f77088dc2789db4e06",
}
# The exact key set of `inputs` in every result: the fourteen hashes plus the repository commit and the dirty flag.
EXPECTED_INPUT_KEYS = frozenset(FROZEN_INPUTS) | {"commit", "dirty"}
DRIVER_KEYS = ("commit", "dirty")

# Files that the driver (beta3-tech/run_arms.ps1) and the analyzer (beta3-tech/analyze_tech.py) legitimately leave in the runs folder,
# besides the result and weights files: the driver writes progress.log, build.txt, build.err.txt and, for every run, <label>.train.txt,
# <label>.train.err.txt, <label>.eval.txt and <label>.eval.err.txt; the analyzer is run with --out report.md --decision decision.json.
# The driver's transient files <label>.json.eval and <label>.json.part are removed on completion; one left in the folder is an
# unfinished run and is NOT allowlisted. Anything not named here (or a result or weights file) is an error.
ALLOWED_FILES = ("progress.log", "build.txt", "build.err.txt", "report.md", "decision.json")
PER_RUN_LOG_SUFFIXES = (".train.txt", ".train.err.txt", ".eval.txt", ".eval.err.txt")

# The exact set of shape blocks (dotted paths, a block has targetItems, targetCorrect, decisions and harmful) of every result.
EXPECTED_SHAPE_BLOCKS = frozenset("""
shapes.hard.pooled shapes.hard.shapes.n-compound-head shapes.hard.shapes.n-subject-initial shapes.hard.shapes.v-after-modal
shapes.hard.shapes.v-imperative-initial shapes.standard.pooled shapes.standard.shapes.n-compound-head shapes.standard.shapes.n-measure
shapes.standard.shapes.n-subject-initial shapes.standard.shapes.v-after-modal shapes.standard.shapes.v-imperative-initial
shapesFollowup.compoundHead.shape shapesFollowup.compoundHead2.shape shapesFollowup.headings.kinds.imperative
shapesFollowup.headings.kinds.noun shapesFollowup.headings.kinds.proper shapesFollowup.headings.shape
shapesFollowup.headings2.kinds.imperative shapesFollowup.headings2.kinds.noun shapesFollowup.headings2.kinds.proper
shapesFollowup.headings2.shape shapesFollowup.imperativeHeadings shapesFollowup.pooledPrimary shapesNew.compoundHead.shape
shapesNew.headings.kinds.imperative shapesNew.headings.kinds.noun shapesNew.headings.kinds.proper shapesNew.headings.shape
shapesNew.namedState.shape shapesNew.pooledPrimary shapesTechnical.clauseVerbs.kinds.adj shapesTechnical.clauseVerbs.kinds.main
shapesTechnical.clauseVerbs.kinds.noun shapesTechnical.clauseVerbs.kinds.sub shapesTechnical.clauseVerbs.shape
shapesTechnical.definedTerms.kinds.adj shapesTechnical.definedTerms.kinds.noun shapesTechnical.definedTerms.kinds.proper
shapesTechnical.definedTerms.shape shapesTechnical.deverbalHeads.kinds.ing shapesTechnical.deverbalHeads.kinds.num
shapesTechnical.deverbalHeads.kinds.other shapesTechnical.deverbalHeads.kinds.pred shapesTechnical.deverbalHeads.kinds.verb
shapesTechnical.deverbalHeads.shape shapesTechnical.pooledTechnical shapesTechnical.stepLines.kinds.imperative
shapesTechnical.stepLines.kinds.noun shapesTechnical.stepLines.shape
""".split())

# Calibration series (under shapesTechnical.calibration): the grids and the row fields of the result schema. A threshold row has the
# fields below; a curve row has the threshold-row fields plus coverage, nounVerbAnswers, harmful and harmfulPer1k; a bin has count, harmful,
# correctPercent, meanConfidence, low and high. Every grid value, every row and every field is required.
THRESHOLD_GRID = (0.9, 0.95)
CURVE_GRID = (0.45, 0.5, 0.55, 0.6, 0.65, 0.7, 0.75, 0.8, 0.85, 0.9, 0.95, 0.96, 0.97, 0.98, 0.99)
BIN_COUNT = 7
SERIES_SPEC = {
    # series -> (expected threshold grid or None, integer fields, number fields)
    "thresholds": (THRESHOLD_GRID, ("answers", "wrong"), ("threshold", "errorPer1k", "intervalLowPer1k", "intervalHighPer1k")),
    "curve": (CURVE_GRID, ("answers", "wrong", "nounVerbAnswers", "harmful"),
              ("threshold", "coverage", "errorPer1k", "intervalLowPer1k", "intervalHighPer1k", "harmfulPer1k")),
    "bins": (None, ("count", "harmful"), ("correctPercent", "meanConfidence", "low", "high")),
}
# (set name relative to calibration, series that the set must have)
CALIBRATION_SETS = (
    [("technical", ("thresholds", "curve", "bins")), ("allSingle", ("thresholds", "bins")), ("nounVerbPooled", ("curve",))]
    + [("tranches." + t, ("thresholds", "bins")) for t in TRANCHES]
)


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
    """True for a finite int or float (a bool, NaN and infinity are not accepted)."""
    if isinstance(value, bool):
        return False
    if isinstance(value, int):
        return True
    return isinstance(value, float) and math.isfinite(value)


def is_sha256(value):
    """True for a 64-digit lowercase hexadecimal string."""
    return isinstance(value, str) and HEX64.match(value) is not None


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


def reject_constant(name):
    """json.load hook: NaN, Infinity and -Infinity are not valid JSON numbers and are refused."""
    raise ValueError("non-finite number %s" % name)


def reject_duplicate_keys(pairs):
    """json.load hook: build an object but refuse a duplicate key (the last one would silently win otherwise)."""
    out = {}
    for key, value in pairs:
        if key in out:
            raise ValueError("duplicate key %r" % key)
        out[key] = value
    return out


def load_json_file(path):
    """Return (object, error text); the object is None when the file is missing or is not strict JSON (NaN or a duplicate key fails)."""
    if not os.path.isfile(path):
        return None, "missing file %s" % os.path.basename(path)
    try:
        with open(path, "r", encoding="utf-8-sig") as handle:
            return json.load(handle, parse_constant=reject_constant, object_pairs_hook=reject_duplicate_keys), None
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
        self.allowlisted = []
        self.weights_files = 0

    def load(self):
        """Read arms.json, the pinned figures and every expected result file; collect problems instead of raising."""
        spec, err = load_json_file(self.arms_path)
        if err:
            self.setup_errors.append("arms.json: " + err)
        elif not isinstance(spec, dict) or not isinstance(spec.get("arms"), list) or not isinstance(spec.get("seeds"), list):
            self.setup_errors.append("arms.json has no arms list or no seeds list")
        else:
            for arm in spec["arms"]:
                if not (isinstance(arm, dict) and isinstance(arm.get("id"), str) and isinstance(arm.get("features"), list)
                        and isinstance(arm.get("sources"), dict)):
                    self.setup_errors.append("arms.json: an arm lacks a string `id`, a `features` list or a `sources` object: %r" % (arm,))
                    continue
                self.arms[arm["id"]] = arm
                self.arm_order.append(arm["id"])
            self.seeds = list(spec["seeds"])
            if not all(is_int(s) for s in self.seeds):
                self.setup_errors.append("arms.json: a seed is not an integer")
            if len(self.arm_order) != EXPECTED_ARMS or len(self.seeds) != EXPECTED_SEEDS:
                self.setup_errors.append("arms.json defines %d valid arms x %d seeds, expected %d x %d"
                                         % (len(self.arm_order), len(self.seeds), EXPECTED_ARMS, EXPECTED_SEEDS))
            if len(set(self.seeds)) != len(self.seeds) or len(set(self.arm_order)) != len(self.arm_order):
                self.setup_errors.append("arms.json repeats an arm or a seed")
        figs, err = load_json_file(self.figures_path)
        if err:
            self.setup_errors.append("shipped_figures.json: " + err)
        elif isinstance(figs, dict) and isinstance(figs.get("figures"), list):
            for entry in figs["figures"]:
                if isinstance(entry, dict) and "path" in entry:
                    if entry["path"] in self.figures:
                        self.setup_errors.append("shipped_figures.json repeats the figure %s" % entry["path"])
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
        self.scan_folder()

    def scan_folder(self):
        """Reject every entry of the runs folder that is not a result file, a weights file or an allowlisted file (see ALLOWED_FILES)."""
        labels = ["%s-s%s" % (a, s) for a in self.arm_order for s in self.seeds]
        results = {label + ".json" for label in labels}
        weights = {label + ".bin" for label in labels}
        allowed = set(ALLOWED_FILES) | {label + suffix for label in labels for suffix in PER_RUN_LOG_SUFFIXES}
        self.allowlisted = []
        self.weights_files = 0
        if not os.path.isdir(self.runs):
            self.unexpected.append("the runs folder %s does not exist or is not a folder" % self.runs)
            return
        try:
            names = sorted(os.listdir(self.runs))
        except OSError as exc:
            self.unexpected.append("cannot list the runs folder: %s" % exc)
            return
        for name in names:
            full = os.path.join(self.runs, name)
            known = name in results or name in weights or name in allowed
            if not known:
                kind = "directory" if os.path.isdir(full) else "file"
                self.unexpected.append("unexpected %s %s" % (kind, name))
            elif os.path.islink(full) or not os.path.isfile(full):
                self.unexpected.append("%s is not a regular file" % name)
            elif name in weights:
                self.weights_files += 1
            elif name in allowed:
                self.allowlisted.append(name)

    def keys(self):
        """The loaded (arm, seed) keys in grid order."""
        return [(a, s) for a in self.arm_order for s in self.seeds if (a, s) in self.data]


def require_results(ctx):
    """Return an error list that is non-empty when no result was loaded: a check over zero results must fail, not pass."""
    return [] if ctx.data else ["no result file was loaded, nothing was checked"]


def check_files(ctx):
    """The runs folder holds exactly the 48 results, their weights files and allowlisted files; every result is strict JSON."""
    errors = list(ctx.setup_errors) + list(ctx.load_errors) + list(ctx.unexpected)
    total = len(ctx.arm_order) * len(ctx.seeds)
    return CheckResult("files", errors, "%d of %d result files parsed; %d weights files and %d allowlisted files; no other entry"
                       % (len(ctx.data), total, ctx.weights_files, len(ctx.allowlisted)))


def check_fields(ctx):
    """Every result carries arm, seed, sources and inputs.commit, and arm/seed agree with the file name."""
    errors = require_results(ctx)
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
    errors = require_results(ctx)
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
    errors = require_results(ctx)
    for key in ctx.keys():
        res = ctx.data[key]
        label = "%s-s%s" % key
        if "sources" not in res:
            continue  # reported by `fields`
        got, err = normalize_sources(res["sources"])
        want, werr = normalize_sources(ctx.arms[key[0]]["sources"])
        if err or werr:
            errors.append("%s: %s" % (label, err or "arms.json: " + werr))
        elif got != want:
            errors.append("%s: sources %s differ from arms.json %s" % (label, sorted(got), sorted(want)))
    return CheckResult("sources", errors, "sources match arms.json for %d results (BASE has none)" % len(ctx.data))


def check_features(ctx):
    """The features of each result equal the arms.json features of its arm."""
    errors = require_results(ctx)
    for key in ctx.keys():
        res = ctx.data[key]
        want = ctx.arms[key[0]]["features"]
        if res.get("features", MISSING) != want:
            errors.append("%s-s%s: features %r differ from arms.json %r" % (key[0], key[1], res.get("features", MISSING), want))
    return CheckResult("features", errors, "features match arms.json for %d results" % len(ctx.data))


def check_provenance(ctx):
    """Exact input schema, every input hash equal to its frozen value, one commit, never dirty, driver and inputs agree."""
    errors = require_results(ctx)
    commits = set()
    for key in ctx.keys():
        res = ctx.data[key]
        label = "%s-s%s" % key
        inputs = res.get("inputs", MISSING)
        driver = res.get("driver", MISSING)
        if not isinstance(inputs, dict):
            errors.append("%s: `inputs` is missing or not an object" % label)
        else:
            for name in sorted(EXPECTED_INPUT_KEYS - set(inputs)):
                errors.append("%s: inputs.%s is missing" % (label, name))
            for name in sorted(set(inputs) - EXPECTED_INPUT_KEYS):
                errors.append("%s: unexpected key inputs.%s" % (label, name))
            for name, frozen in FROZEN_INPUTS.items():
                if name not in inputs:
                    continue  # reported above
                value = inputs[name]
                if not is_sha256(value):
                    errors.append("%s: inputs.%s is not a 64-digit lowercase hex string (%r)" % (label, name, value))
                elif value != frozen:
                    errors.append("%s: inputs.%s %s differs from the frozen value %s" % (label, name, value, frozen))
            commit = inputs.get("commit", MISSING)
            commits.add(commit if isinstance(commit, str) else repr(commit))
            if inputs.get("dirty", MISSING) is not False:
                errors.append("%s: inputs.dirty is %r, not false" % (label, inputs.get("dirty", MISSING)))
        if not isinstance(driver, dict):
            errors.append("%s: `driver` is missing or not an object" % label)
        else:
            for name in DRIVER_KEYS:
                if name not in driver:
                    errors.append("%s: driver.%s is missing" % (label, name))
            if driver.get("dirty", MISSING) is not False:
                errors.append("%s: driver.dirty is %r, not false" % (label, driver.get("dirty", MISSING)))
            if isinstance(inputs, dict) and driver.get("commit", MISSING) != inputs.get("commit", MISSING):
                errors.append("%s: driver.commit %r differs from inputs.commit %r"
                              % (label, driver.get("commit", MISSING), inputs.get("commit", MISSING)))
    if len(commits) != 1:
        errors.append("expected exactly one repository commit in the results, found %d: %s" % (len(commits), sorted(commits)))
    return CheckResult("provenance", errors, "inputs has exactly the %d expected keys in every result; %d input hashes equal the frozen "
                       "values (so identical in all runs); one commit %s, clean tree"
                       % (len(EXPECTED_INPUT_KEYS), len(FROZEN_INPUTS), ", ".join(sorted(c[:10] for c in commits)) or "none"))


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
    if dec > tokens:
        errors.append("%s: %s %d > tokens %d" % (where, decisions_key, dec, tokens))
    if not 0 <= acc <= 100:
        errors.append("%s: accuracy %r outside 0..100" % (where, acc))
    if harm > dec:
        errors.append("%s: harmful %d > %s %d" % (where, harm, decisions_key, dec))
    wrong = tokens * (1 - acc / 100.0)
    if harm > wrong + 0.5:
        errors.append("%s: harmful %d > wrong tokens %.1f (tokens %d, accuracy %.4f)" % (where, harm, wrong, tokens, acc))


def check_series_rows(where, series, rows, judged, errors):
    """Apply the schema and the invariants to one series (thresholds, curve or bins) of a calibration set; every row and field is required."""
    grid, int_fields, num_fields = SERIES_SPEC[series]
    expected_rows = len(grid) if grid is not None else BIN_COUNT
    if not isinstance(rows, list):
        errors.append("%s: series `%s` is missing or not a list" % (where, series))
        return
    if len(rows) != expected_rows:
        errors.append("%s: series `%s` has %d rows, expected %d" % (where, series, len(rows), expected_rows))
    total = 0
    for index, row in enumerate(rows):
        at = "%s %s[%d]" % (where, series, index)
        if not isinstance(row, dict):
            errors.append("%s: row is not an object" % at)
            continue
        missing = [f for f in int_fields + num_fields if f not in row]
        if missing:
            errors.append("%s: row lacks the field(s) %s" % (at, ", ".join(missing)))
            continue
        bad = [f for f in int_fields if not is_int(row[f]) or row[f] < 0] + [f for f in num_fields if not is_num(row[f])]
        if bad:
            errors.append("%s: field(s) %s are not valid numbers in %r" % (at, ", ".join(bad), row))
            continue
        if grid is not None:
            if index < len(grid) and abs(row["threshold"] - grid[index]) > 1e-9:
                errors.append("%s: threshold is %r, expected %s" % (at, row["threshold"], grid[index]))
            if not 0 < row["answers"] <= judged:
                errors.append("%s: answers %d outside 1..judged %d" % (at, row["answers"], judged))
            if row["wrong"] > row["answers"]:
                errors.append("%s: wrong %d > answers %d" % (at, row["wrong"], row["answers"]))
            if series == "curve":
                if row["harmful"] > row["wrong"]:
                    errors.append("%s: harmful %d > wrong %d" % (at, row["harmful"], row["wrong"]))
                if row["harmful"] > row["nounVerbAnswers"]:
                    errors.append("%s: harmful %d > nounVerbAnswers %d" % (at, row["harmful"], row["nounVerbAnswers"]))
                if row["nounVerbAnswers"] > row["answers"]:
                    errors.append("%s: nounVerbAnswers %d > answers %d" % (at, row["nounVerbAnswers"], row["answers"]))
                if not 0 <= row["coverage"] <= 1:
                    errors.append("%s: coverage %r outside 0..1" % (at, row["coverage"]))
        else:
            total += row["count"]
            if row["harmful"] > row["count"]:
                errors.append("%s: harmful %d > count %d" % (at, row["harmful"], row["count"]))
            if row["harmful"] > row["count"] * (1 - row["correctPercent"] / 100.0) + 0.51:
                errors.append("%s: harmful %d > wrong in the bin" % (at, row["harmful"]))
    if grid is None and total != judged:
        errors.append("%s: bin counts sum to %d, judged is %d" % (where, total, judged))


def check_calibration_set(label, name, series_names, node, errors):
    """Apply the schema and invariants to one calibration set: `judged`, every required series, and the bins against `judged`."""
    where = "%s calibration.%s" % (label, name)
    if not isinstance(node, dict):
        errors.append("%s: the calibration set is missing or not an object" % where)
        return
    if not is_int(node.get("judged", MISSING)) or node["judged"] <= 0:
        errors.append("%s: judged is missing or not a positive integer" % where)
        return
    if name == "nounVerbPooled" and (not is_int(node.get("decisions", MISSING)) or node["decisions"] <= 0):
        errors.append("%s: decisions is missing or not a positive integer" % where)
    for series in series_names:
        check_series_rows(where, series, node.get(series, MISSING), node["judged"], errors)


def check_pooled_sums(res, label, errors):
    """A pooled block equals the sum of its parts, and the calibration decisions equal the pooled technical decisions."""
    for pooled_path, root, parts in (("shapesTechnical.pooledTechnical", "shapesTechnical", POOLED_TECHNICAL_PARTS),
                                     ("shapesFollowup.pooledPrimary", "shapesFollowup", POOLED_PRIMARY_PARTS)):
        pooled = get_path(res, pooled_path)
        part_blocks = [get_path(res, "%s.%s.shape" % (root, p)) for p in parts]
        if not isinstance(pooled, dict) or not all(isinstance(p, dict) for p in part_blocks):
            errors.append("%s: cannot compare %s with the sum of its parts (a block is missing)" % (label, pooled_path))
            continue
        for fld in SHAPE_KEYS:
            if fld not in pooled or not all(fld in p for p in part_blocks):
                errors.append("%s: field %s is missing in %s or in one of its parts" % (label, fld, pooled_path))
                continue
            total = sum(p[fld] for p in part_blocks)
            if pooled[fld] != total:
                errors.append("%s: %s.%s is %r but its parts sum to %r" % (label, pooled_path, fld, pooled[fld], total))
    nvp = get_path(res, "shapesTechnical.calibration.nounVerbPooled.decisions")
    pooled_dec = get_path(res, "shapesTechnical.pooledTechnical.decisions")
    if nvp is MISSING or nvp != pooled_dec:
        errors.append("%s: calibration.nounVerbPooled.decisions %r differs from pooledTechnical.decisions %r" % (label, nvp, pooled_dec))


def check_counts(ctx):
    """Schema and count invariants for every result: shape blocks, token blocks, calibration series and pooled sums."""
    errors = require_results(ctx)
    blocks = 0
    constants = {}  # what the data fixes and no run may change: dev-set token counts and the item counts of every shape block
    for key in ctx.keys():
        res = ctx.data[key]
        label = "%s-s%s" % key
        found = []
        for section in SHAPE_SECTIONS:
            node = res.get(section, MISSING)
            if not isinstance(node, dict):
                errors.append("%s: section %s is missing or not an object" % (label, section))
            else:
                shape_blocks(node, section, found)
        paths = {path for path, _ in found}
        for path in sorted(EXPECTED_SHAPE_BLOCKS - paths):
            errors.append("%s: shape block %s is missing" % (label, path))
        for path in sorted(paths - EXPECTED_SHAPE_BLOCKS):
            errors.append("%s: unexpected shape block %s" % (label, path))
        for path, block in found:
            check_shape_block(label, path, block, errors)
            for fld in ("targetItems", "controlItems"):
                if fld in block:
                    constants.setdefault("%s.%s" % (path, fld), {}).setdefault(repr(block[fld]), []).append(label)
        blocks += len(found)
        for path, dec_key in TOKEN_BLOCKS:
            block = res.get(path, MISSING)
            check_token_block(label, path, dec_key, block, errors)
            if isinstance(block, dict) and "tokens" in block:
                constants.setdefault("%s.tokens" % path, {}).setdefault(repr(block["tokens"]), []).append(label)
        calibration = get_path(res, "shapesTechnical.calibration")
        if not isinstance(calibration, dict):
            errors.append("%s: shapesTechnical.calibration is missing" % label)
            continue
        for name, series_names in CALIBRATION_SETS:
            check_calibration_set(label, name, series_names, get_path(calibration, name), errors)
        check_pooled_sums(res, label, errors)
    for name, values in sorted(constants.items()):
        if len(values) > 1:
            errors.append("%s differs between runs: %s" % (name, "; ".join("%s in %d run(s)" % (v, len(l)) for v, l in sorted(values.items()))))
    return CheckResult("counts", errors, "%d shape blocks, %d calibration sets with all series, rows and thresholds; decisions > 0, harmful <= "
                       "wrong, wrong <= answers over %d shape blocks in %d results"
                       % (len(EXPECTED_SHAPE_BLOCKS), len(CALIBRATION_SETS), blocks, len(ctx.data)))


def check_tranches(ctx):
    """Tranche totals equal the pinned sizes; the tranche sums equal the technical total; sizes agree with target + control items."""
    errors = require_results(ctx)
    pinned = {}
    figure_errors = []
    for path in FIGURE_PATHS:
        value = ctx.figures.get(path)
        if not is_int(value):
            figure_errors.append("shipped_figures.json has no integer figure for %s" % path)
        else:
            pinned[path] = value
    errors += figure_errors
    if not figure_errors:
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
            if not is_sha256(recorded):
                errors.append("%s: weights.sha256 is missing or not a 64-digit lowercase hex string in the result (%r)" % (label, recorded))
                continue
            actual = sha256_file(path)
            checked += 1
            if actual != recorded:
                errors.append("%s: file SHA-256 %s differs from the recorded %s" % (label, actual, recorded))
    if checked != EXPECTED_ARMS * EXPECTED_SEEDS:
        errors.append("%d weights files were checked, expected %d" % (checked, EXPECTED_ARMS * EXPECTED_SEEDS))
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
    if not is_sha256(recorded):
        errors.append("%s: weights.sha256 is not recorded (as a 64-digit lowercase hex string) in the result" % label)
        recorded = None
    if ctx.shipped_sha256 is None:
        errors.append("shipped weights hash unavailable: %s" % ctx.shipped_error)
    if not is_sha256(ctx.pinned_sha256):
        errors.append("the pinned hash is not a 64-digit lowercase hex string")
    if actual and ctx.pinned_sha256 and actual != ctx.pinned_sha256:
        errors.append("%s.bin SHA-256 %s != pinned %s" % (label, actual, ctx.pinned_sha256))
    if recorded and ctx.pinned_sha256 and recorded != ctx.pinned_sha256:
        errors.append("recorded hash %s != pinned %s" % (recorded, ctx.pinned_sha256))
    if recorded and ctx.shipped_sha256 and recorded != ctx.shipped_sha256:
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
    try:
        ctx.load()
    except Exception as exc:  # fail closed: an unreadable input is a failed `files` check, not a traceback
        ctx.setup_errors.append("loading the inputs crashed: %s: %s" % (type(exc).__name__, exc))
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
# Self-test: synthetic results (the full field names and series of the real eval-arm schema that this audit checks), tampered copies.
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


def syn_calibration(judged, series_names):
    """A synthetic calibration set with every required series: threshold rows, the 15 curve rows, and seven bins that sum to `judged`."""
    node = {"judged": judged}
    if "thresholds" in series_names:
        node["thresholds"] = [{"threshold": t, "answers": judged // 2, "wrong": judged // 20, "errorPer1k": 50.0,
                               "intervalLowPer1k": 40.0, "intervalHighPer1k": 60.0} for t in THRESHOLD_GRID]
    if "curve" in series_names:
        node["curve"] = [{"threshold": t, "answers": judged - 1, "coverage": 0.9, "wrong": judged // 4, "errorPer1k": 200.0,
                          "intervalLowPer1k": 180.0, "intervalHighPer1k": 220.0, "nounVerbAnswers": judged // 2,
                          "harmful": judged // 10, "harmfulPer1k": 100.0} for t in CURVE_GRID]
    if "bins" in series_names:
        bins = [{"count": 1, "harmful": 0, "correctPercent": 100.0, "meanConfidence": 0.5, "low": 0.0, "high": 0.5}
                for _ in range(BIN_COUNT - 1)]
        bins.append({"count": judged - (BIN_COUNT - 1), "harmful": 3, "correctPercent": 90.0, "meanConfidence": 0.9, "low": 0.5, "high": 1.0})
        node["bins"] = bins
    return node


def syn_pooled(names, section):
    """The pooled block of the named synthetic shape blocks (sum of the four count fields)."""
    blocks = [section[n]["shape"] for n in names]
    return {k: sum(b[k] for b in blocks) for k in SHAPE_KEYS}


def syn_sections():
    """The four shape sections with every expected shape block (generic counts); sizes and pooled blocks are fixed up by the caller."""
    sections = {}
    for path in sorted(EXPECTED_SHAPE_BLOCKS):
        parts = path.split(".")
        cur = sections
        for part in parts[:-1]:
            cur = cur.setdefault(part, {})
        cur[parts[-1]] = syn_shape(100, 90, 4, 92, 20)
    return sections


def syn_result(arm, seed, sources, features, bin_sha):
    """Build a synthetic result that satisfies every check."""
    sections = syn_sections()
    technical, followup = sections["shapesTechnical"], sections["shapesFollowup"]
    for name, (target, control) in SYN_SIZES.items():
        technical[name]["shape"] = syn_shape(target, SYN_DECISIONS[name], 5, target - 8, control)
    technical["pooledTechnical"] = syn_pooled(POOLED_TECHNICAL_PARTS, technical)
    followup["pooledPrimary"] = syn_pooled(POOLED_PRIMARY_PARTS, followup)
    judged = {n: sum(SYN_SIZES[n]) for n in SYN_SIZES}
    series = dict(CALIBRATION_SETS)
    technical["calibration"] = {
        "technical": syn_calibration(sum(judged.values()), series["technical"]),
        "allSingle": syn_calibration(2148, series["allSingle"]),
        "nounVerbPooled": dict(syn_calibration(882, series["nounVerbPooled"]), decisions=technical["pooledTechnical"]["decisions"]),
        "tranches": {n: syn_calibration(j, series["tranches." + n]) for n, j in judged.items()},
    }
    result = {
        "schema": 1, "label": "%s-s%s" % (arm, seed), "weights": {"sha256": bin_sha},
        "openDev": {"tokens": 26232, "accuracy": 96.7, "nounVerbDecisions": 4451, "harmful": 90},
        "goldDev": {"tokens": 2017, "accuracy": 95.4, "nounVerbDecisions": 611, "harmful": 8},
        "udDev": {"tokens": 25148, "accuracy": 93.0, "decisions": 4570, "harmful": 92},
        "inputs": dict(FROZEN_INPUTS, commit=SYN_COMMIT, dirty=False),
        "arm": arm, "seed": seed, "features": list(features), "sources": dict(sources),
        "driver": {"commit": SYN_COMMIT, "dirty": False, "priority": "Normal"},
    }
    result.update(sections)
    return result


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


def edit_raw(runs, name, transform):
    """Apply `transform(text) -> text` to the raw text of a file of the runs folder (for tampering that JSON objects cannot express)."""
    path = os.path.join(runs, name)
    with open(path, "r", encoding="utf-8") as handle:
        text = handle.read()
    with open(path, "w", encoding="utf-8") as handle:
        handle.write(transform(text))


def all_arm_seeds():
    """Every (arm id, seed) of the synthetic grid."""
    return [(arm["id"], seed) for arm in SYN_ARMS for seed in SYN_SEEDS]


def selftest_scenarios():
    """Return the tamper scenarios: (description, expected failing check or None for must-pass, [(variant, mutate(runs, shipped))]).

    A mutation returns a replacement for the shipped hash or None. A scenario with several variants must fail the targeted check in every
    variant (for example: every one of the fourteen input hashes removed in turn).
    """
    def result_edit(arm, seed, mutation):
        def apply(runs, shipped):
            edit_result(runs, arm, seed, mutation)
        return apply

    def every_result(mutation):
        def apply(runs, shipped):
            for arm, seed in all_arm_seeds():
                edit_result(runs, arm, seed, mutation)
        return apply

    def remove(name):
        def apply(runs, shipped):
            os.remove(os.path.join(runs, name))
        return apply

    def add_file(name, content=b"x"):
        def apply(runs, shipped):
            with open(os.path.join(runs, name), "wb") as handle:
                handle.write(content)
        return apply

    def add_dir(name):
        def apply(runs, shipped):
            os.makedirs(os.path.join(runs, name))
        return apply

    def add_allowlisted(runs, shipped):
        names = list(ALLOWED_FILES) + ["%s-s%s%s" % (a, s, suffix) for a, s in all_arm_seeds() for suffix in PER_RUN_LOG_SUFFIXES]
        for name in names:
            add_file(name)(runs, shipped)

    def arms_edit(mutation):
        def apply(runs, shipped):
            path = os.path.join(os.path.dirname(runs), "arms.json")
            with open(path, "r", encoding="utf-8") as handle:
                data = json.load(handle)
            mutation(data)
            write_json(path, data)
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

    def extra_result_file(runs, shipped):
        write_json(os.path.join(runs, "TECH-s99.json"), {"arm": "TECH", "seed": 99})

    def missing_and_extra(runs, shipped):
        os.remove(os.path.join(runs, "TECH-s11.json"))
        add_file("notes.json")(runs, shipped)

    def nan_literal(runs, shipped):
        edit_raw(runs, "TECH-s11.json", lambda t: t.replace('"accuracy": 96.7', '"accuracy": NaN', 1))

    def duplicate_key(runs, shipped):
        edit_raw(runs, "TECH-s11.json", lambda t: t.replace('"schema": 1,', '"schema": 1, "schema": 1,', 1))

    tech = ("TECH", 11)
    calibration = ["shapesTechnical", "calibration"]
    hash_keys = sorted(FROZEN_INPUTS)
    set_series = [(name, series) for name, series_names in CALIBRATION_SETS for series in series_names]

    def series_path(name, series):
        return calibration + name.split(".") + [series]

    def row_count(series):
        return len(SERIES_SPEC[series][0] or range(BIN_COUNT))

    def row_fields(series):
        _, ints, nums = SERIES_SPEC[series]
        return ints + nums

    sh = ["shapesTechnical", "stepLines", "shape"]
    return [
        # ---- result files and fields
        ("missing result file", "files", [("", remove("TECH-s11.json"))]),
        ("unexpected extra result file", "files", [("", extra_result_file)]),
        ("missing arm", "fields", [("", result_edit(*tech, drop(["arm"])))]),
        ("missing seed", "fields", [("", result_edit(*tech, drop(["seed"])))]),
        ("missing sources", "fields", [("", result_edit(*tech, drop(["sources"])))]),
        ("missing inputs.commit", "fields", [("", result_edit(*tech, drop(["inputs", "commit"])))]),
        ("duplicate (arm, seed)", "grid", [("", duplicate)]),
        ("missing (arm, seed)", "grid", [("", missing_pair)]),
        ("wrong sources for an arm", "sources", [("", result_edit(*tech, setter(["sources", "terms"], 2)))]),
        ("wrong features for an arm", "features", [("", result_edit(*tech, setter(["features"], [])))]),
        # ---- provenance and the input schema (finding 1)
        ("dirty tree", "provenance", [("", result_edit(*tech, setter(["inputs", "dirty"], True)))]),
        ("mixed commits", "provenance", [("", result_edit(*tech, setter(["inputs", "commit"], "f" * 40)))]),
        ("one input key removed from one run (each of the 14 hashes and dirty)", "provenance",
         [(k, result_edit(*tech, drop(["inputs", k]))) for k in hash_keys + ["dirty"]]),
        ("one input key removed from ALL runs (identical set; each of the 14 hashes and dirty)", "provenance",
         [(k, every_result(drop(["inputs", k]))) for k in hash_keys + ["dirty"]]),
        ("inputs.commit removed from ALL runs", "fields", [("", every_result(drop(["inputs", "commit"])))]),
        ("inputs.commit removed from ALL runs (provenance)", "provenance", [("", every_result(drop(["inputs", "commit"])))]),
        ("whole `inputs` removed from one run", "provenance", [("", result_edit(*tech, drop(["inputs"])))]),
        ("whole `driver` removed from one run", "provenance", [("", result_edit(*tech, drop(["driver"])))]),
        ("driver.commit removed from one run", "provenance", [("", result_edit(*tech, drop(["driver", "commit"])))]),
        ("driver.dirty removed from one run", "provenance", [("", result_edit(*tech, drop(["driver", "dirty"])))]),
        ("extra unexpected inputs key in one run / in all runs", "provenance",
         [("one run", result_edit(*tech, setter(["inputs", "extraSha256"], "ab" * 32))),
          ("all runs", every_result(setter(["inputs", "extraSha256"], "ab" * 32)))]),
        ("malformed input hash (one run / all runs; short, uppercase, not hex, integer)", "provenance",
         [(label + " " + kind, edit)
          for label, wrap in (("one run", lambda m: result_edit(*tech, m)), ("all runs", every_result))
          for kind, edit in (("short", wrap(setter(["inputs", "devSha256"], "ab" * 31 + "a"))),
                             ("uppercase", wrap(setter(["inputs", "devSha256"], FROZEN_INPUTS["devSha256"].upper()))),
                             ("not hex", wrap(setter(["inputs", "devSha256"], "zz" * 32))),
                             ("integer", wrap(setter(["inputs", "devSha256"], 12345))))]),
        ("input hash differs from the frozen value (one run / all runs, well-formed)", "provenance",
         [("one run", result_edit(*tech, setter(["inputs", "stepLinesSha256"], "cd" * 32))),
          ("all runs", every_result(setter(["inputs", "stepLinesSha256"], "cd" * 32)))]),
        # ---- counts (earlier cases)
        ("decisions == 0", "counts", [("", result_edit(*tech, setter(["shapesTechnical", "stepLines", "shape", "decisions"], 0)))]),
        ("wrong > decisions (calibration answers)", "counts",
         [("", result_edit(*tech, setter(calibration + ["technical", "thresholds", 0, "wrong"], 10**6)))]),
        ("harmful > wrong", "counts", [("", result_edit(*tech, setter(sh + ["harmful"], 90)))]),
        ("harmful > decisions", "counts", [("", result_edit(*tech, setter(["openDev", "harmful"], 5000)))]),
        ("pooled block differs from its parts", "counts",
         [("", result_edit(*tech, setter(["shapesTechnical", "pooledTechnical", "harmful"], 1)))]),
        ("tranche total != size", "tranches", [("", result_edit(*tech, setter(calibration + ["tranches", "stepLines", "judged"], 235)))]),
        # ---- calibration series (finding 2)
        ("calibration series missing (each series of each set)", "counts",
         [("%s.%s" % ns, result_edit(*tech, drop(series_path(*ns)))) for ns in set_series]),
        ("calibration series missing from ALL runs (each series of each set)", "counts",
         [("%s.%s" % ns, every_result(drop(series_path(*ns)))) for ns in set_series]),
        ("whole calibration set missing (each set)", "counts",
         [(name, result_edit(*tech, drop(calibration + name.split(".")))) for name, _ in CALIBRATION_SETS]),
        ("one row missing (first, middle, last row of each series)", "counts",
         [("%s.%s[%d]" % (ns + (i,)), result_edit(*tech, drop(series_path(*ns) + [i])))
          for ns in set_series for i in sorted({0, 1, row_count(ns[1]) - 1})]),
        ("one extra row appended (each series of each set)", "counts",
         [("%s.%s" % ns, result_edit(*tech, lambda d, ns=ns: _append_copy(d, series_path(*ns)))) for ns in set_series]),
        ("row not an object", "counts", [("", result_edit(*tech, setter(series_path("technical", "curve") + [3], 7)))]),
        ("wrong threshold value in a row (every row of the grids)", "counts",
         [("%s.%s[%d]" % (ns + (i,)), result_edit(*tech, setter(series_path(*ns) + [i, "threshold"], 0.61)))
          for ns in set_series if SERIES_SPEC[ns[1]][0] for i in range(len(SERIES_SPEC[ns[1]][0]))]),
        ("threshold rows swapped (wrong order)", "counts",
         [("", result_edit(*tech, lambda d: _swap_rows(d, series_path("technical", "curve"), 2, 3)))]),
        ("row field missing (every field of every series kind, in each set)", "counts",
         [("%s.%s.%s" % (ns + (f,)), result_edit(*tech, drop(series_path(*ns) + [0, f])))
          for ns in set_series for f in row_fields(ns[1])]),
        ("row field not a number (null, text, boolean, fractional integer)", "counts",
         [(kind, result_edit(*tech, setter(series_path("technical", "curve") + [4, "answers"], value)))
          for kind, value in (("null", None), ("text", "12"), ("boolean", True), ("float", 12.5))]),
        ("calibration judged missing", "counts", [("", result_edit(*tech, drop(calibration + ["allSingle", "judged"])))]),
        ("nounVerbPooled decisions missing", "counts", [("", result_edit(*tech, drop(calibration + ["nounVerbPooled", "decisions"])))]),
        ("bin counts do not sum to judged", "counts", [("", result_edit(*tech, setter(series_path("allSingle", "bins") + [0, "count"], 2)))]),
        ("curve nounVerbAnswers > answers", "counts",
         [("", result_edit(*tech, setter(series_path("technical", "curve") + [0, "nounVerbAnswers"], 10**6)))]),
        # ---- shape blocks and dev-set blocks
        ("shape block missing (one per section)", "counts",
         [(p, result_edit(*tech, drop(p.split(".")))) for p in
          ("shapes.hard.pooled", "shapesNew.namedState.shape", "shapesFollowup.imperativeHeadings",
           "shapesTechnical.deverbalHeads.kinds.ing")]),
        ("shape section missing (each of the four)", "counts", [(s, result_edit(*tech, drop([s]))) for s in SHAPE_SECTIONS]),
        ("unexpected extra shape block", "counts",
         [("", result_edit(*tech, setter(["shapesNew", "extraBlock"], syn_shape(100, 90, 4, 92, 20))))]),
        ("dev-set block missing (each of the three)", "counts", [(p, result_edit(*tech, drop([p]))) for p, _ in TOKEN_BLOCKS]),
        ("accuracy above 100", "counts", [("", result_edit(*tech, setter(["openDev", "accuracy"], 120.0)))]),
        ("dev-set token count differs between runs", "counts", [("", result_edit(*tech, setter(["openDev", "tokens"], 26233)))]),
        ("shape-block target count differs between runs", "counts",
         [("", result_edit(*tech, lambda d: d["shapesNew"]["compoundHead"]["shape"].update({"targetItems": 101})))]),
        # ---- strict JSON
        ("NaN literal in a result", "files", [("", nan_literal)]),
        ("duplicate object key in a result", "files", [("", duplicate_key)]),
        # ---- arms.json
        ("arms.json: an arm without `features` / `sources`", "files",
         [("features", arms_edit(lambda d: d["arms"][1].pop("features"))), ("sources", arms_edit(lambda d: d["arms"][1].pop("sources")))]),
        # ---- the runs folder (finding 3)
        ("extra JSON file whose name does not match the result pattern", "files",
         [(n, add_file(n)) for n in ("notes.json", "result.json", "TECH-s11.backup.json", "TECH-s11.json.bak", "TECH-S99.json")]),
        ("extra non-JSON file", "files",
         [(n, add_file(n)) for n in ("notes.txt", "TECH-s99.bin", "TECH-s11.json.part", "TECH-s11.json.eval", "extra.log", "Thumbs.db")]),
        ("extra subdirectory", "files", [(n, add_dir(n)) for n in ("pycache", "old", "TECH-s11.eval.txt.d")]),
        ("allowlisted name that is a directory", "files", [(n, add_dir(n)) for n in ("progress.log", "TECH-s11.train.txt")]),
        ("per-run log of a run that is not in the grid", "files", [("", add_file("TECH-s99.train.txt"))]),
        ("missing result and an extra file together", "files", [("", missing_and_extra)]),
        # ---- weights and shipped hash
        ("missing BASE bin", "base-shipped", [("", remove("BASE-s20260101.bin"))]),
        ("missing other bin", "bins", [("", remove("TECH-s11.bin"))]),
        ("BASE bin altered (hash differs from recorded)", "base-shipped", [("", altered_bin)]),
        ("BASE bin altered (bins check)", "bins", [("", altered_bin)]),
        ("recorded hash != shipped hash", "base-shipped", [("", recorded_not_shipped)]),
        ("recorded weights hash malformed", "bins", [("", result_edit(*tech, setter(["weights", "sha256"], "xyz")))]),
        ("shipped hash (git) != pinned hash", "shipped-pin", [("", shipped_not_pinned)]),
        # ---- must pass
        ("ACCEPT: every allowlisted file present (5 named + 4 logs of each of the 48 runs)", None, [("", add_allowlisted)]),
    ]


def _append_copy(data, path):
    """Append a copy of the last element of the list at `path` (a series gets one row too many)."""
    cur = data
    for part in path:
        cur = cur[part]
    cur.append(dict(cur[-1]))


def _swap_rows(data, path, i, j):
    """Swap two rows of the list at `path`."""
    cur = data
    for part in path:
        cur = cur[part]
    cur[i], cur[j] = cur[j], cur[i]


def run_selftest(work_base):
    """Prove that the passing synthetic set passes and that every tampered copy fails the check it targets; return True on success."""
    root = tempfile.mkdtemp(prefix="audit-selftest-", dir=work_base)
    good = True
    tampered = 0
    accepted = 0
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
        counter = 0
        for description, expected, variants in selftest_scenarios():
            hits = 0
            problems = []
            for variant, mutate in variants:
                counter += 1
                case = os.path.join(root, "case-%03d" % counter)
                shutil.copytree(base_dir, case)
                case_runs = os.path.join(case, "runs")
                override = mutate(case_runs, shipped)
                case_results = run_audit(case_runs, os.path.join(case, "arms.json"), os.path.join(case, "shipped_figures.json"),
                                         override if override is not None else shipped, None, pinned)
                by_name = {r.name: r for r in case_results}
                if expected is None:
                    hit = all(r.ok for r in case_results)
                    if not hit:
                        problems.append("%s: still failing %s" % (variant, ", ".join(r.name for r in case_results if not r.ok)))
                        print_results(case_results)
                else:
                    hit = expected in by_name and not by_name[expected].ok
                    if not hit:
                        problems.append("%s: check `%s` did NOT fail" % (variant, expected))
                hits += hit
                shutil.rmtree(case, ignore_errors=True)
            if expected is None:
                accepted += len(variants)
            else:
                tampered += len(variants)
            ok = hits == len(variants)
            good = good and ok
            detail = "check `%s` fails" % expected if expected else "all checks pass"
            print("%s  %s: %-70s -> %s (%d of %d)" % ("PASS" if ok else "FAIL", "tamper" if expected else "accept", description, detail,
                                                       hits, len(variants)))
            for line in problems[:MAX_LINES]:
                print("        - %s" % line)
        results = run_audit(runs, arms, figures, None, "git unavailable", pinned)
        hit = any(r.name == "shipped-pin" and not r.ok for r in results) and any(r.name == "base-shipped" and not r.ok for r in results)
        print("%s  tamper: %-70s -> checks `shipped-pin` and `base-shipped` fail (1 of 1)" % ("PASS" if hit else "FAIL", "shipped weights unreadable from git"))
        good = good and hit
        tampered += 1
        empty = os.path.join(root, "empty")
        os.makedirs(empty)
        results = run_audit(empty, arms, figures, shipped, None, pinned)
        hit = not all(r.ok for r in results) and all(not r.ok for r in results if r.name in ("files", "fields", "grid", "counts", "bins"))
        print("%s  tamper: %-70s -> checks `files`, `fields`, `grid`, `counts` and `bins` fail (1 of 1)" % ("PASS" if hit else "FAIL", "empty runs folder"))
        good = good and hit
        tampered += 1
    finally:
        shutil.rmtree(root, ignore_errors=True)
    print("SELFTEST: %s; %d tamper case(s) and %d accept case(s)" % (
        "PASS (the audit accepts the untampered set and the allowlisted files, and rejects every tampered copy)" if good else "FAIL",
        tampered, accepted))
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

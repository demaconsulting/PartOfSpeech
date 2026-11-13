#!/usr/bin/env python3
"""beta.3 phase 2: the technical operating point of the shipped model (rule `nv-technical-dev-v1`; standard library only; frozen with the RUN-LOG).

usage:
    python technical_points.py --arm-json eval-arm.json [--out technical-operating-points.json] [--markdown section.md]
    python technical_points.py --selftest

`eval-arm.json` is the result of `eval-arm` on the weights that ship (the `shapesTechnical.calibration` block with the confidence curve of the
trainer commit T0). The script turns that curve into the technical operating point and writes it as JSON (and, with --markdown, as a section
for docs/evaluation/confidence-thresholds.md). It reads no held-out file and chooses nothing but what the rule below says.

The rule (pre-registered in RUN-LOG.md, section "beta.3 phase 2 (E2)", before any arm was trained; every number is an implementing-agent
decision, not an owner decision):

* scope: the judged words (targets and controls) of the four technical development tranches pooled (deverbal-heads by class, the other three by
  exact tag); an answer is wrong when it is not the gold label, whatever the kind of error;
* measure: wrong answers per 1,000 answered judged words at confidence >= threshold; beside it, the consumer's costly error: harmful NOUN<->VERB
  errors per 1,000 answered noun-verb decisions of the target items of deverbal-heads, step-lines and clause-verbs (817 decisions for the shipped
  model);
* grid: the 15 thresholds of `nv-pooled-dev-v1` (0.45 to 0.95 in steps of 0.05, then 0.96 to 0.99);
* the point: the first (lowest) grid threshold with an error of at most 10 wrong answers per 1,000 answers (1%) **and** a coverage of at least
  25% of the judged words (the floor keeps the point from being a handful of answers);
* if no grid threshold has both, the point is `none`: the achievable minimum is reported (the lowest error rate over the grid thresholds with a
  coverage of at least 25%, with its 95% Wilson interval and its coverage; a tie goes to the lowest threshold) together with the whole curve.
  No threshold, floor, grid or definition is changed toward 1%.

The comparisons use integers (`wrong x 1000 <= 10 x answers`, `answers x 100 >= 25 x judged`), so a point at exactly 10 per 1,000 or exactly
25% qualifies and there is no rounding.

Exit codes: 0 done, 1 an invalid result file or a failed self-test, 2 usage.
"""

import argparse
import json
import math
import os
import sys
from fractions import Fraction

RULE_ID = "nv-technical-dev-v1"
GRID = [0.45, 0.50, 0.55, 0.60, 0.65, 0.70, 0.75, 0.80, 0.85, 0.90, 0.95, 0.96, 0.97, 0.98, 0.99]
TARGET_ERRORS_PER_1000 = 10
COVERAGE_FLOOR_PERCENT = 25
Z95 = 1.959964
INPUT_HASHES = {
    "deverbalHeadsSha256": "6159099f5fc59187f9486070439e552358abf19fb95742e20c54dbcf17400c5a",
    "definedTermsSha256": "6bca83a8e63589152cf3a37661e328a187323612b06344c6c402921f1c2478c8",
    "stepLinesSha256": "c06bc3138b92cd3baafec1a9a210110daafee69846dd83c77ba70b4fc09f4ad8",
    "clauseVerbsSha256": "d35e7a259140614d6f0057fe7f3fd5d545cdf478971ac7f77088dc2789db4e06",
}
CAVEATS = [
    "The figures are development figures: the tranches are the data the model was compared on, so they are optimistic for any other text.",
    "The tranches are band-selected contested words in technical sentences (deverbal heads, defined terms, step lines, clause verbs), not "
    "running text, so the rates are not the error rates on ordinary text.",
    "No held-out set carries these shapes, and the held-out evaluation applies the general operating points only.",
    "The measure counts every wrong answer of a judged word (targets and controls) under the scoring of its tranche, not the harmful "
    "NOUN<->VERB errors of the general operating points; the harmful rate is reported beside it.",
]


class ResultError(Exception):
    """The eval-arm result is not usable."""


def wilson(successes, total):
    """The 95% Wilson score interval of a share, as numbers between 0 and 1 ((0, 0) without trials)."""
    if total <= 0:
        return 0.0, 0.0
    n = float(total)
    p = successes / n
    z2 = Z95 * Z95
    center = (p + z2 / (2 * n)) / (1 + z2 / n)
    half = Z95 * math.sqrt(p * (1 - p) / n + z2 / (4 * n * n)) / (1 + z2 / n)
    return max(0.0, center - half), min(1.0, center + half)


def error_row(row, judged):
    """One row of the curve with its derived members (the interval is recomputed from the counts)."""
    answers, wrong = row["answers"], row["wrong"]
    low, high = wilson(wrong, answers)
    return {"threshold": row["threshold"], "answers": answers, "coveragePercent": 100.0 * answers / judged if judged else 0.0,
            "wrong": wrong, "errorPer1k": 1000.0 * wrong / answers if answers else 0.0,
            "intervalLowPer1k": 1000.0 * low, "intervalHighPer1k": 1000.0 * high,
            "meetsError": answers > 0 and wrong * 1000 <= TARGET_ERRORS_PER_1000 * answers,
            "meetsCoverage": answers * 100 >= COVERAGE_FLOOR_PERCENT * judged}


def harmful_row(row):
    """One row of the noun-verb curve: the harmful errors per 1,000 answered noun-verb decisions with a Wilson interval."""
    decisions, harmful = row["nounVerbAnswers"], row["harmful"]
    low, high = wilson(harmful, decisions)
    return {"threshold": row["threshold"], "answeredDecisions": decisions, "harmful": harmful,
            "harmfulPer1k": 1000.0 * harmful / decisions if decisions else 0.0,
            "intervalLowPer1k": 1000.0 * low, "intervalHighPer1k": 1000.0 * high}


def choose(rows):
    """The point and the achievable minimum of the error rows. Returns (point row or None, minimum row or None)."""
    point = next((r for r in rows if r["meetsError"] and r["meetsCoverage"]), None)
    eligible = [r for r in rows if r["meetsCoverage"] and r["answers"] > 0]
    minimum = min(eligible, key=lambda r: (Fraction(r["wrong"], r["answers"]), r["threshold"])) if eligible else None
    return point, minimum


def validate(arm):
    """Checks the part of an eval-arm result that the rule reads; returns (calibration scope, noun-verb pool, inputs)."""
    try:
        scope = arm["shapesTechnical"]["calibration"]["technical"]
        pool = arm["shapesTechnical"]["calibration"]["nounVerbPooled"]
        inputs = arm["inputs"]
    except (KeyError, TypeError) as e:
        raise ResultError("the result has no shapesTechnical.calibration curve (%s): it was not written by the trainer with the curve" % e)
    for key, expected in INPUT_HASHES.items():
        if inputs.get(key) != expected:
            raise ResultError("inputs.%s is %r, expected the frozen %s" % (key, inputs.get(key), expected))
    judged = scope.get("judged")
    if not isinstance(judged, int) or judged <= 0:
        raise ResultError("calibration.technical.judged is %r" % (judged,))
    for name, curve in (("technical", scope.get("curve")), ("nounVerbPooled", pool.get("curve"))):
        if not isinstance(curve, list) or [r.get("threshold") for r in curve] != GRID:
            raise ResultError("%s.curve is not the 15 thresholds of the grid" % name)
    rows = scope["curve"]
    for i, r in enumerate(rows):
        if not (0 <= r["wrong"] <= r["answers"] <= judged):
            raise ResultError("curve row %s: wrong %r, answers %r, judged %r are not consistent" % (r["threshold"], r["wrong"], r["answers"], judged))
        if i and r["answers"] > rows[i - 1]["answers"]:
            raise ResultError("curve row %s has more answers than the row below it" % r["threshold"])
    for i, key in ((9, 0), (10, 1)):
        t = scope["thresholds"][key]
        if (t["threshold"], t["answers"], t["wrong"]) != (rows[i]["threshold"], rows[i]["answers"], rows[i]["wrong"]):
            raise ResultError("the curve row at %s differs from the threshold row" % rows[i]["threshold"])
    decisions = pool.get("decisions")
    for r in pool["curve"]:
        if not (0 <= r["harmful"] <= r["nounVerbAnswers"] <= decisions):
            raise ResultError("noun-verb curve row %s is not consistent" % r["threshold"])
    return scope, pool, inputs


def operating_point(arm):
    """The technical operating point of an eval-arm result as a dictionary (the JSON artifact)."""
    scope, pool, inputs = validate(arm)
    judged = scope["judged"]
    rows = [error_row(r, judged) for r in scope["curve"]]
    point, minimum = choose(rows)

    def brief(r):
        return None if r is None else {k: r[k] for k in ("threshold", "answers", "coveragePercent", "wrong", "errorPer1k", "intervalLowPer1k", "intervalHighPer1k")}

    return {
        "schema": 1, "rule": RULE_ID,
        "ruleText": "The first threshold of the grid of nv-pooled-dev-v1 (0.45 to 0.95 in steps of 0.05, then 0.96 to 0.99) with at most %d wrong answers per "
                    "1,000 answers and a coverage of at least %d%% of the judged words, on the pooled judged words of the four technical development "
                    "tranches; none when no threshold has both." % (TARGET_ERRORS_PER_1000, COVERAGE_FLOOR_PERCENT),
        "weightsSha256": (arm.get("weights") or {}).get("sha256"), "temperature": arm.get("temperature"),
        "inputs": {k: inputs[k] for k in INPUT_HASHES},
        "scope": "the judged words (targets and controls) of deverbal-heads, defined-terms, step-lines and clause-verbs, pooled",
        "judged": judged, "grid": GRID, "targetErrorsPer1000": TARGET_ERRORS_PER_1000, "coverageFloorPercent": COVERAGE_FLOOR_PERCENT,
        "curve": rows,
        "nounVerbDecisions": pool["decisions"], "nounVerbTargets": pool["judged"], "nounVerbCurve": [harmful_row(r) for r in pool["curve"]],
        "status": "found" if point else "none", "point": brief(point),
        "achievableMinimum": brief(minimum), "caveats": CAVEATS,
    }


def render_markdown(doc):
    """The section for confidence-thresholds.md as a list of lines."""
    lines = ["## Technical text (rule `%s`, development figures)" % RULE_ID, "",
             "Judged words: %d (targets and controls of the four technical development tranches). Target: at most %d wrong answers per 1,000 answers "
             "(%.0f%%) and a coverage of at least %d%% of the judged words, at the first grid threshold that has both." %
             (doc["judged"], doc["targetErrorsPer1000"], doc["targetErrorsPer1000"] / 10.0, doc["coverageFloorPercent"]), ""]
    if doc["status"] == "found":
        p = doc["point"]
        lines += ["**Technical point: %.2f** (%.1f wrong per 1,000 answers, 95%% interval [%.1f, %.1f], coverage %.1f%%)." %
                  (p["threshold"], p["errorPer1k"], p["intervalLowPer1k"], p["intervalHighPer1k"], p["coveragePercent"]), ""]
    else:
        m = doc["achievableMinimum"]
        lines += ["**Technical point: none.** No grid threshold has at most %d wrong answers per 1,000 answers with a coverage of at least %d%%." %
                  (doc["targetErrorsPer1000"], doc["coverageFloorPercent"])]
        if m:
            lines += ["The lowest error rate at a coverage of at least %d%% is %.1f wrong per 1,000 answers at threshold %.2f (95%% interval [%.1f, %.1f], "
                      "coverage %.1f%%)." % (doc["coverageFloorPercent"], m["errorPer1k"], m["threshold"], m["intervalLowPer1k"], m["intervalHighPer1k"],
                                             m["coveragePercent"])]
        lines += [""]
    lines += ["| threshold | answers | coverage | wrong | wrong per 1,000 answers | 95% interval | answered noun-verb decisions | harmful | harmful per 1,000 decisions |",
              "| --- | --- | --- | --- | --- | --- | --- | --- | --- |"]
    for r, h in zip(doc["curve"], doc["nounVerbCurve"]):
        lines.append("| %.2f | %d | %.1f%% | %d | %.1f | [%.1f, %.1f] | %d | %d | %.1f |" %
                     (r["threshold"], r["answers"], r["coveragePercent"], r["wrong"], r["errorPer1k"], r["intervalLowPer1k"], r["intervalHighPer1k"],
                      h["answeredDecisions"], h["harmful"], h["harmfulPer1k"]))
    lines += [""] + ["- " + c for c in doc["caveats"]]
    return lines


# ---------------------------------------------------------------------------------------------------------------------
# Self-test
# ---------------------------------------------------------------------------------------------------------------------

def synthetic_arm(judged, answers, wrong, decisions=800, nv_answers=None, nv_harmful=None):
    """A minimal eval-arm result with the given curve (lists of 15 values)."""
    curve = [{"threshold": t, "answers": a, "wrong": w} for t, a, w in zip(GRID, answers, wrong)]
    nv_answers = nv_answers or [min(decisions, a) for a in answers]
    nv_harmful = nv_harmful or [0] * 15
    nv = [{"threshold": t, "answers": a, "nounVerbAnswers": n, "harmful": h} for t, a, n, h in zip(GRID, answers, nv_answers, nv_harmful)]
    return {"weights": {"sha256": "ab" * 32}, "temperature": 9.5, "inputs": dict(INPUT_HASHES),
            "shapesTechnical": {"calibration": {
                "technical": {"judged": judged, "thresholds": [{"threshold": 0.9, "answers": answers[9], "wrong": wrong[9]},
                                                               {"threshold": 0.95, "answers": answers[10], "wrong": wrong[10]}], "curve": curve},
                "nounVerbPooled": {"judged": 700, "decisions": decisions, "curve": nv}}}}


def selftest():
    failures = []

    def check(name, condition, detail=""):
        if not condition:
            failures.append("%s %s" % (name, detail))

    judged = 1000
    # the point is the first threshold with an error of at most 1% and a coverage of at least 25% (250 answers of 1,000, exactly 25%)
    descending = [1000, 900, 800, 700, 600, 500, 400, 350, 300, 280, 260, 255, 250, 249, 240]
    wrong = [200, 150, 100, 80, 60, 50, 40, 35, 30, 28, 26, 25, 2, 2, 2]       # 250 answers with 2 wrong at 0.97 (0.8%)
    doc = operating_point(synthetic_arm(judged, descending, wrong))
    check("point is the first threshold with both", doc["status"] == "found" and doc["point"]["threshold"] == 0.97, str(doc["point"]))
    check("point values", doc["point"]["answers"] == 250 and doc["point"]["wrong"] == 2 and abs(doc["point"]["errorPer1k"] - 8.0) < 1e-9
          and abs(doc["point"]["coveragePercent"] - 25.0) < 1e-9, str(doc["point"]))
    # exactly 10 per 1,000 qualifies (4 wrong of 400 answers, 40% coverage)
    wide = [1000, 900, 800, 700, 600, 550, 500, 480, 460, 440, 430, 420, 400, 390, 380]
    doc = operating_point(synthetic_arm(judged, wide, [200, 150, 100, 80, 60, 50, 40, 35, 30, 28, 26, 25, 4, 2, 2]))
    check("exactly 1% qualifies", doc["point"] is not None and doc["point"]["threshold"] == 0.97 and doc["point"]["wrong"] == 4
          and abs(doc["point"]["errorPer1k"] - 10.0) < 1e-9, str(doc["point"]))
    # 5 wrong of 400 is 12.5 per 1,000: it does not qualify and the next threshold (2 of 390, 5.1 per 1,000) does
    doc = operating_point(synthetic_arm(judged, wide, [200, 150, 100, 80, 60, 50, 40, 35, 30, 28, 26, 25, 5, 2, 2]))
    check("just above 1% does not qualify", doc["point"]["threshold"] == 0.98 and doc["point"]["answers"] == 390, str(doc["point"]))
    # 249 answers of 1,000 (24.9%) with no wrong answer is below the coverage floor: no point; the minimum is at 0.96 (25 of 255)
    below = [1000, 900, 800, 700, 600, 500, 400, 350, 300, 280, 260, 255, 249, 248, 240]
    doc = operating_point(synthetic_arm(judged, below, [200, 150, 100, 80, 60, 50, 40, 35, 30, 28, 26, 25, 0, 0, 0]))
    m = doc["achievableMinimum"]
    check("coverage 24.9% does not qualify", doc["status"] == "none" and doc["point"] is None, str(doc["point"]))
    check("none: the minimum is the lowest error at coverage >= 25%", m["threshold"] == 0.96 and (m["answers"], m["wrong"]) == (255, 25)
          and abs(m["errorPer1k"] - 1000.0 * 25 / 255) < 1e-9, str(m))
    check("none: the minimum has its Wilson interval", m["intervalLowPer1k"] < m["errorPer1k"] < m["intervalHighPer1k"]
          and abs(m["intervalLowPer1k"] - 1000.0 * wilson(25, 255)[0]) < 1e-9, str(m))
    check("the whole curve is reported", len(doc["curve"]) == 15 and [r["threshold"] for r in doc["curve"]] == GRID and len(doc["nounVerbCurve"]) == 15)
    # a tie goes to the lowest threshold: 50 of 500 at 0.70 and at 0.75
    ties = [1000, 900, 800, 700, 600, 500, 500, 400, 350, 300, 280, 270, 260, 255, 250]
    wrong_ties = [300, 250, 200, 150, 100, 50, 50, 60, 70, 80, 90, 100, 110, 120, 130]
    doc = operating_point(synthetic_arm(judged, ties, wrong_ties))
    check("a tie goes to the lowest threshold", doc["achievableMinimum"]["threshold"] == 0.70 and doc["achievableMinimum"]["wrong"] == 50, str(doc["achievableMinimum"]))
    # no threshold reaches the coverage floor: no point and no minimum, however few the errors
    low_cov = [240, 230, 220, 210, 200, 190, 180, 170, 160, 150, 140, 130, 120, 110, 100]
    doc = operating_point(synthetic_arm(judged, low_cov, [0] * 15))
    check("no coverage: none and no minimum", doc["status"] == "none" and doc["achievableMinimum"] is None and doc["point"] is None, str(doc["achievableMinimum"]))
    # the harmful rate beside the error
    nv_answers = [800, 780, 760, 740, 720, 700, 650, 600, 550, 500, 450, 400, 350, 300, 250]
    nv_harmful = [200, 190, 180, 170, 150, 130, 110, 90, 70, 50, 30, 20, 10, 5, 0]
    doc = operating_point(synthetic_arm(judged, descending, [200, 150, 100, 80, 60, 50, 40, 35, 30, 28, 26, 25, 2, 2, 2], 800, nv_answers, nv_harmful))
    check("harmful rate beside the error", doc["nounVerbCurve"][0]["harmfulPer1k"] == 250.0 and doc["nounVerbCurve"][13]["harmfulPer1k"] == 1000.0 * 5 / 300
          and doc["nounVerbCurve"][14]["harmful"] == 0 and doc["nounVerbDecisions"] == 800 and doc["nounVerbCurve"][0]["answeredDecisions"] == 800)
    # Wilson: the values of the trainer's tests
    check("wilson", (round(wilson(0, 10)[0], 3), round(wilson(0, 10)[1], 3)) == (0.0, 0.278) and (round(wilson(8, 10)[0], 3), round(wilson(8, 10)[1], 3)) == (0.49, 0.943)
          and wilson(0, 0) == (0.0, 0.0))
    # invalid results are refused
    bad = synthetic_arm(judged, descending, [200, 150, 100, 80, 60, 50, 40, 35, 30, 28, 26, 25, 2, 2, 2])
    for name, mutate, needle in (
            ("a changed input hash", lambda a: a["inputs"].__setitem__("stepLinesSha256", "0" * 64), "inputs.stepLinesSha256"),
            ("a missing curve", lambda a: a["shapesTechnical"]["calibration"]["technical"].pop("curve"), "15 thresholds"),
            ("a missing block", lambda a: a["shapesTechnical"]["calibration"].pop("nounVerbPooled"), "no shapesTechnical.calibration curve"),
            ("a wrong grid", lambda a: a["shapesTechnical"]["calibration"]["technical"]["curve"][0].__setitem__("threshold", 0.4), "15 thresholds"),
            ("more wrong than answers", lambda a: a["shapesTechnical"]["calibration"]["technical"]["curve"][3].__setitem__("wrong", 9999), "not consistent"),
            ("rising answers", lambda a: a["shapesTechnical"]["calibration"]["technical"]["curve"][5].__setitem__("answers", 999), "more answers"),
            ("a curve row that differs from the threshold row", lambda a: a["shapesTechnical"]["calibration"]["technical"]["thresholds"][0].__setitem__("wrong", 1),
             "differs from the threshold row"),
            ("harmful above the decisions", lambda a: a["shapesTechnical"]["calibration"]["nounVerbPooled"]["curve"][2].__setitem__("harmful", 9999), "noun-verb curve row")):
        copy = json.loads(json.dumps(bad))
        mutate(copy)
        try:
            operating_point(copy)
            check("refused: %s" % name, False, "no error raised")
        except ResultError as e:
            check("refused: %s" % name, needle in str(e), str(e))
    # the markdown and the JSON are deterministic and carry the rule and the caveats
    doc = operating_point(bad)
    text = "\n".join(render_markdown(doc))
    check("markdown", text == "\n".join(render_markdown(operating_point(bad))) and "nv-technical-dev-v1" in text and "Technical point: 0.97" in text
          and "development figures" in text and "| 0.45 |" in text and "| 0.99 |" in text and text.count("\n| 0.") == 15, text[:300])
    none_text = "\n".join(render_markdown(operating_point(synthetic_arm(judged, descending, [200, 150, 100, 80, 60, 50, 40, 35, 30, 28, 26, 25, 24, 2, 2]))))
    check("markdown (none)", "Technical point: none." in none_text and "lowest error rate at a coverage of at least 25%" in none_text, none_text[:300])
    check("json", json.loads(json.dumps(doc)) == doc and doc["rule"] == RULE_ID and doc["weightsSha256"] == "ab" * 32 and len(doc["caveats"]) == 4)

    # the shipped model (the figures of beta3-tech/shipped_figures.json: the curve of the CMP2 weights.bin): none, the minimum at 0.98
    here = os.path.dirname(os.path.abspath(__file__))
    with open(os.path.join(here, "shipped_figures.json"), encoding="utf-8") as f:
        figures = {x["path"]: x["value"] for x in json.load(f)["figures"]}
    cal = "shapesTechnical.calibration."
    answers = [figures[cal + "technical.curve.%d.answers" % i] for i in range(15)]
    wrongs = [figures[cal + "technical.curve.%d.wrong" % i] for i in range(15)]
    nva = [figures[cal + "nounVerbPooled.curve.%d.nounVerbAnswers" % i] for i in range(15)]
    nvh = [figures[cal + "nounVerbPooled.curve.%d.harmful" % i] for i in range(15)]
    shipped = operating_point(synthetic_arm(figures[cal + "technical.judged"], answers, wrongs, figures[cal + "nounVerbPooled.decisions"], nva, nvh))
    check("shipped model: no technical point", shipped["status"] == "none" and shipped["point"] is None, str(shipped["point"]))
    m = shipped["achievableMinimum"]
    check("shipped model: the minimum at coverage >= 25% is 11 wrong of 427 at 0.98", m is not None and (m["threshold"], m["answers"], m["wrong"]) == (0.98, 427, 11), str(m))
    row = {r["threshold"]: r for r in shipped["curve"]}
    check("shipped model: 0.99 has 2 wrong of 333 (6.0 per 1,000) but only 23.5% coverage",
          row[0.99]["wrong"] == 2 and row[0.99]["answers"] == 333 and row[0.99]["meetsError"] and not row[0.99]["meetsCoverage"]
          and abs(row[0.99]["coveragePercent"] - 100.0 * 333 / 1417) < 1e-9)
    check("shipped model: 117.3 and 57.1 per 1,000 at 0.90 and 0.95", round(row[0.90]["errorPer1k"], 1) == 117.3 and round(row[0.95]["errorPer1k"], 1) == 57.1)
    check("the rule constants", TARGET_ERRORS_PER_1000 == 10 and COVERAGE_FLOOR_PERCENT == 25 and len(GRID) == 15 and GRID[0] == 0.45 and GRID[-1] == 0.99)

    print("selftest: %s" % ("ok" if not failures else "FAILED"))
    for line in failures:
        print("  - " + line)
    return 1 if failures else 0


def main(argv=None):
    ap = argparse.ArgumentParser(description="The technical operating point (rule %s) of an eval-arm result (standard library only)." % RULE_ID)
    ap.add_argument("--arm-json", help="the eval-arm result of the weights that ship")
    ap.add_argument("--out", help="write the operating point as JSON")
    ap.add_argument("--markdown", help="write the section for confidence-thresholds.md")
    ap.add_argument("--selftest", action="store_true", help="run the self-test and exit")
    args = ap.parse_args(argv)
    if args.selftest:
        return selftest()
    if not args.arm_json:
        ap.error("--arm-json is required (or --selftest)")
    try:
        with open(args.arm_json, encoding="utf-8") as f:
            doc = operating_point(json.load(f))
    except (OSError, ValueError, ResultError) as e:
        print("technical_points: %s" % e, file=sys.stderr)
        return 1
    text = json.dumps(doc, indent=2)
    print(text)
    if args.out:
        with open(args.out, "w", encoding="utf-8", newline="\n") as f:
            f.write(text + "\n")
    if args.markdown:
        with open(args.markdown, "w", encoding="utf-8", newline="\n") as f:
            f.write("\n".join(render_markdown(doc)) + "\n")
    return 0


if __name__ == "__main__":
    sys.exit(main())

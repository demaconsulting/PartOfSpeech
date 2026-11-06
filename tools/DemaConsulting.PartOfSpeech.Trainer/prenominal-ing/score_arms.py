"""Corpus cleanup C, `prenominal-ing` follow-up: score the arms and apply the pre-registered selection rule (development data only).

  python score_arms.py --scratch DIR --repo DIR [--diag DIR] [--json FILE]

Reads, and nothing else: the committed open dev (FULL's relabeled labels), the mapped UD-EWT dev (git-ignored cache), the frozen item list
`items-open-dev.tsv`, and the prediction files `DIR/runs/<arm>-s<seed>-{open,ud}.tsv` of the runtime decoder (Dump tool: sent_id, index,
form, chosen tag, confidence, alternatives). No held-out file is read.

Definitions (RUN-LOG, "Corpus cleanup C: pre-registered `prenominal-ing` follow-up"): decision = gold NOUN or VERB with both tags among the
decoder's candidates (chosen tag and alternatives); harmful = the chosen tag is the other of NOUN and VERB with confidence >= 0.45; per 1,000
decisions; pooled harmful rate of a run = harmful over both sets / decisions over both sets; compound-head NOUN accuracy = share of the
compound items whose chosen tag is NOUN; verbal-ing VERB accuracy = share of the verbal-ing items whose chosen tag is VERB.
Statistics: sample sd (n = 5); Welch t 95% interval of the difference of means; combined seed + document bootstrap (2,000 replicates, seed
20261104: documents shared by the arms, the 5 seeds resampled with replacement within each arm). Descriptive only; the rule is the selection.
"""
import argparse
import hashlib
import itertools
import json
import math
import os
import sys

import numpy as np
from scipy import stats

HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, os.path.join(os.path.dirname(os.path.dirname(os.path.dirname(HERE))), "tools", "data"))
import common  # noqa: E402

SEEDS = [20260101, 11, 12, 13, 14]
ARMS = ["full", "noing", "noctrl"]
LABEL = {"full": "FULL", "noing": "NO-ING", "noctrl": "NO-CTRL"}
HARM_AT = 0.45
BOOT = 2000
BSEED = 20261104
# columns of the per-document arrays
COLS = ["n", "ok", "nv", "harm", "cn", "cok", "in_", "iok", "zn", "zok", "vn", "vok"]
IX = {c: i for i, c in enumerate(COLS)}


def read_pred(path):
    pred = {}
    with open(path, encoding="utf-8") as handle:
        for line in handle:
            sid, idx, _, tag, conf, alts = line.rstrip("\n").split("\t")
            pred[(sid, int(idx))] = (tag, float(conf), {a.split(":")[0] for a in alts.split(" ") if a})
    return pred


def read_gold(path, doc_of, strict):
    gold, doc = {}, {}
    for s in common.read_corpus(path, strict=strict)[1]:
        for i, r in enumerate(s["rows"]):
            gold[(s["sent_id"], i)] = r[1]
            doc[(s["sent_id"], i)] = doc_of(s)
    return gold, doc


def read_items(path):
    items = {}
    for line in common.read_lines(path):
        if line and not line.startswith("#"):
            sid, idx, _, kind, _, _ = line.split("\t")
            items[(sid, int(idx))] = kind
    return items


class Sets:
    def __init__(self, repo):
        self.og, self.od = read_gold(os.path.join(repo, "data", "open", "dev.tsv"), lambda s: s["source_id"], True)
        self.ug, self.ud = read_gold(os.path.join(repo, "tools", "data", "cache", "ud-ewt", "ud_dev.tsv"), lambda s: s["sent_id"].rsplit("-", 1)[0], False)
        self.items = read_items(os.path.join(HERE, "items-open-dev.tsv"))
        self.docs = {"open": sorted(set(self.od.values())), "ud": sorted(set(self.ud.values()))}


def per_doc(sets, setname, pred):
    gold, doc = (sets.og, sets.od) if setname == "open" else (sets.ug, sets.ud)
    index = {d: i for i, d in enumerate(sets.docs[setname])}
    arr = np.zeros((len(index), len(COLS)))
    for k, g in gold.items():
        tag, conf, alts = pred[k]
        row = arr[index[doc[k]]]
        row[IX["n"]] += 1
        row[IX["ok"]] += tag == g
        cands = alts | {tag}
        if g in ("NOUN", "VERB") and "NOUN" in cands and "VERB" in cands:
            row[IX["nv"]] += 1
            if tag in ("NOUN", "VERB") and tag != g and conf >= HARM_AT:
                row[IX["harm"]] += 1
        if setname == "open":
            kind = sets.items.get(k)
            if kind in ("compound-ing", "compound-zero"):
                row[IX["cn"]] += 1
                row[IX["cok"]] += tag == "NOUN"
                key = ("i" if kind == "compound-ing" else "z")
                row[IX[key + "n" if key == "z" else "in_"]] += 1
                row[IX[key + "ok" if key == "z" else "iok"]] += tag == "NOUN"
            elif kind == "verbal-ing":
                row[IX["vn"]] += 1
                row[IX["vok"]] += tag == "VERB"
    return arr


METRICS = [
    ("pooled", "Pooled harmful / 1k (open dev + UD dev)"),
    ("open", "Open dev harmful / 1k (relabeled)"),
    ("ud", "UD-EWT dev harmful / 1k"),
    ("agree", "UD-EWT dev agreement %"),
    ("comp", "Compound-head NOUN accuracy % (248 items)"),
    ("comp_ing", "  compound-ing NOUN accuracy % (41)"),
    ("comp_zero", "  compound-zero NOUN accuracy % (207)"),
    ("verb", "Verbal-ing VERB accuracy % (384 items)"),
    ("oacc", "Open dev accuracy % (context)"),
]


def value(metric, o, u):
    """o, u: summed (or resampled-summed) arrays over the documents of open dev / UD dev. Works on the last axis."""
    g = lambda a, c: a[..., IX[c]]
    if metric == "pooled":
        return 1000.0 * (g(o, "harm") + g(u, "harm")) / (g(o, "nv") + g(u, "nv"))
    if metric == "open":
        return 1000.0 * g(o, "harm") / g(o, "nv")
    if metric == "ud":
        return 1000.0 * g(u, "harm") / g(u, "nv")
    if metric == "agree":
        return 100.0 * g(u, "ok") / g(u, "n")
    if metric == "comp":
        return 100.0 * g(o, "cok") / g(o, "cn")
    if metric == "comp_ing":
        return 100.0 * g(o, "iok") / g(o, "in_")
    if metric == "comp_zero":
        return 100.0 * g(o, "zok") / g(o, "zn")
    if metric == "verb":
        return 100.0 * g(o, "vok") / g(o, "vn")
    if metric == "oacc":
        return 100.0 * g(o, "ok") / g(o, "n")
    raise KeyError(metric)


def welch(a, b):
    """Difference of the means b - a, Welch t 95% interval, df, p."""
    d = b.mean() - a.mean()
    va, vb = a.var(ddof=1) / len(a), b.var(ddof=1) / len(b)
    se = math.sqrt(va + vb)
    if se == 0:
        return d, d, d, float("nan"), float("nan")
    df = (va + vb) ** 2 / (va ** 2 / (len(a) - 1) + vb ** 2 / (len(b) - 1))
    t = stats.t.ppf(0.975, df)
    return d, d - t * se, d + t * se, df, 2 * (1 - stats.t.cdf(abs(d) / se, df))


def sha(path):
    return hashlib.sha256(open(path, "rb").read()).hexdigest()


def main():
    parser = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    parser.add_argument("--scratch", required=True)
    parser.add_argument("--repo", required=True)
    parser.add_argument("--diag", default=None, help="folder of the diagnosis predictions (new-s*, abl-s*), for the acceptance comparison")
    parser.add_argument("--json", default=None)
    args = parser.parse_args()
    sets = Sets(args.repo)
    runs = os.path.join(args.scratch, "runs")
    data = {a: {s: {k: per_doc(sets, k, read_pred(os.path.join(runs, "%s-s%d-%s.tsv" % (a, s, k)))) for k in ("open", "ud")} for s in SEEDS} for a in ARMS}
    out = []
    p = out.append
    totals = lambda a, s: (data[a][s]["open"].sum(axis=0), data[a][s]["ud"].sum(axis=0))
    vals = {a: {m: np.array([value(m, *totals(a, s)) for s in SEEDS]) for m, _ in METRICS} for a in ARMS}
    counts = {a: {s: {k: data[a][s][k].sum(axis=0).astype(int).tolist() for k in ("open", "ud")} for s in SEEDS} for a in ARMS}

    # ----------------------------------------------------------- acceptance
    p("## Acceptance")
    o, u = totals("full", SEEDS[0])
    p("FULL shipped seed: open harmful %d of %d decisions; UD harmful %d of %d; UD agreement %d of %d (%.3f%%)"
      % (o[IX["harm"]], o[IX["nv"]], u[IX["harm"]], u[IX["nv"]], u[IX["ok"]], u[IX["n"]], 100 * u[IX["ok"]] / u[IX["n"]]))
    expected_full = [92.803, 92.711, 92.655, 92.695, 92.747]
    p("FULL UD agreement per seed: %s; diagnosis NEW: %s; match: %s" % (
        ", ".join("%.3f" % v for v in vals["full"]["agree"]), ", ".join("%.3f" % v for v in expected_full),
        all(abs(a - b) < 5e-4 for a, b in zip(vals["full"]["agree"], expected_full))))
    p("shipped-seed acceptance (103/4,467; 107/4,570; 23,338): %s" % (
        int(o[IX["harm"]]) == 103 and int(o[IX["nv"]]) == 4467 and int(u[IX["harm"]]) == 107 and int(u[IX["nv"]]) == 4570 and int(u[IX["ok"]]) == 23338))
    exp_abl = {"agree": [92.711, 92.779, 92.683, 92.616, 92.747], "open_h": [90, 98, 96, 100, 90], "ud_h": [103, 109, 95, 99, 101]}
    got_h = [counts["noing"][s]["open"][IX["harm"]] for s in SEEDS]
    got_u = [counts["noing"][s]["ud"][IX["harm"]] for s in SEEDS]
    p("NO-ING vs diagnosis ablation: agreement %s; open harmful %s (expected %s); UD harmful %s (expected %s); match: %s" % (
        ", ".join("%.3f" % v for v in vals["noing"]["agree"]), got_h, exp_abl["open_h"], got_u, exp_abl["ud_h"],
        got_h == exp_abl["open_h"] and got_u == exp_abl["ud_h"] and all(abs(a - b) < 5e-4 for a, b in zip(vals["noing"]["agree"], exp_abl["agree"]))))
    if args.diag:
        for arm, dname in (("full", "new"), ("noing", "abl")):
            same = []
            for s in SEEDS:
                a = os.path.join(runs, "%s-s%d.bin" % (arm, s))
                b = os.path.join(args.diag, "%s-s%d.bin" % (dname, s))
                same.append(os.path.exists(b) and sha(a) == sha(b))
            p("weights byte-identical to the diagnosis %s models per seed: %s" % (dname, same))
    shipped = os.path.join(args.repo, "src", "DemaConsulting.PartOfSpeech", "Resources", "weights.bin")
    p("FULL shipped-seed weights byte-identical to shipped weights.bin: %s" % (sha(os.path.join(runs, "full-s%d.bin" % SEEDS[0])) == sha(shipped)))

    # ----------------------------------------------------------- tables
    p("\n## Per-seed values and mean +- sd (seeds %s)" % ", ".join(str(s) for s in SEEDS))
    for m, label in METRICS:
        p("\n### %s" % label.strip())
        p("| Arm | " + " | ".join(str(s) for s in SEEDS) + " | mean +- sd |")
        p("| --- | " + " | ".join("---:" for _ in SEEDS) + " | ---: |")
        for a in ARMS:
            v = vals[a][m]
            cells = []
            for i, s in enumerate(SEEDS):
                o, u = totals(a, s)
                if m in ("open", "ud", "pooled"):
                    h = (o[IX["harm"]] if m != "ud" else 0) + (u[IX["harm"]] if m != "open" else 0)
                    d = (o[IX["nv"]] if m != "ud" else 0) + (u[IX["nv"]] if m != "open" else 0)
                    cells.append("%.3f (%d/%d)" % (v[i], h, d))
                else:
                    cells.append("%.3f" % v[i])
            p("| %s | %s | %.3f +- %.3f |" % (LABEL[a], " | ".join(cells), v.mean(), v.std(ddof=1)))

    # ----------------------------------------------------------- differences vs FULL
    rng = np.random.default_rng(BSEED)
    nd = {k: len(sets.docs[k]) for k in ("open", "ud")}
    wo = rng.multinomial(nd["open"], [1.0 / nd["open"]] * nd["open"], size=BOOT).astype(np.float64)
    wu = rng.multinomial(nd["ud"], [1.0 / nd["ud"]] * nd["ud"], size=BOOT).astype(np.float64)
    boot = {}
    for a in ARMS:
        boot[a] = {}
        per_seed = {m: np.stack([value(m, wo @ data[a][s]["open"], wu @ data[a][s]["ud"]) for s in SEEDS], axis=1) for m, _ in METRICS}
        idx = rng.integers(0, len(SEEDS), size=(BOOT, len(SEEDS)))
        for m, _ in METRICS:
            boot[a][m] = np.take_along_axis(per_seed[m], idx, axis=1).mean(axis=1)
    p("\n## Differences against FULL (descriptive; the selection is the rule below)")
    p("| Metric | Arm - FULL | mean difference | Welch t 95% (df, p) | seed + document bootstrap 95% | shipped-seed difference |")
    p("| --- | --- | ---: | --- | --- | ---: |")
    diffs = {}
    for m, label in METRICS:
        for a in ("noing", "noctrl"):
            d, lo, hi, df, pv = welch(vals["full"][m], vals[a][m])
            bd = boot[a][m] - boot["full"][m]
            blo, bhi = np.percentile(bd, [2.5, 97.5])
            diffs[(a, m)] = (d, lo, hi, blo, bhi)
            p("| %s | %s | %+.3f | [%+.3f, %+.3f] (%.1f, p = %.3f) | [%+.3f, %+.3f] | %+.3f |" % (
                label.strip(), LABEL[a], d, lo, hi, df, pv, blo, bhi, vals[a][m][0] - vals["full"][m][0]))
    p("\nNO-ING - NO-CTRL (pooled harmful / 1k): mean difference %+.3f, Welch 95%% [%+.3f, %+.3f]" % welch(vals["noctrl"]["pooled"], vals["noing"]["pooled"])[:3])

    # ----------------------------------------------------------- the rule
    p("\n## Application of the pre-registered selection rule")
    mean = {a: {m: vals[a][m].mean() for m, _ in METRICS} for a in ARMS}
    cand = ["full", "noing"]
    prim = {a: mean[a]["pooled"] for a in cand}
    best_agree = max(mean[a]["agree"] for a in cand)
    best_comp = max(mean[a]["comp"] for a in cand)
    lowest = min(cand, key=lambda a: prim[a])
    diff = prim["full"] - prim[lowest]
    p("primary (mean pooled harmful / 1k): FULL %.3f, NO-ING %.3f; lowest candidate %s; difference FULL - lowest = %.3f per 1k (tie-break threshold 0.5)"
      % (prim["full"], prim["noing"], LABEL[lowest], diff))
    c1 = {a: best_agree - mean[a]["agree"] for a in cand}
    c2 = {a: best_comp - mean[a]["comp"] for a in cand}
    for a in cand:
        p("%s: UD agreement mean %.3f (best %.3f, shortfall %.3f; C1 limit 0.15: %s); compound NOUN accuracy mean %.3f (best %.3f, shortfall %.3f; C2 limit 2.0: %s)"
          % (LABEL[a], mean[a]["agree"], best_agree, c1[a], "holds" if c1[a] <= 0.15 else "FAILS", mean[a]["comp"], best_comp, c2[a], "holds" if c2[a] <= 2.0 else "FAILS"))
    if lowest == "full":
        selected, why = "full", "FULL has the lowest primary mean"
    elif diff < 0.5:
        selected, why = "full", "tie-break: the primary difference %.3f is under 0.5 per 1k" % diff
    elif c1["noing"] <= 0.15 and c2["noing"] <= 2.0:
        selected, why = "noing", "NO-ING has the lowest primary by %.3f (>= 0.5) and C1 and C2 hold" % diff
    else:
        selected, why = "full", "NO-ING has the lowest primary by %.3f but fails a constraint" % diff
    p("**SELECTED ARM: %s** (%s)" % (LABEL[selected], why))
    red_ing = prim["full"] - prim["noing"]
    red_ctrl = mean["full"]["pooled"] - mean["noctrl"]["pooled"]
    p("negative control (context only, not a candidate): NO-CTRL pooled mean %.3f, FULL minus NO-CTRL = %+.3f; NO-ING reduction %+.3f; half of it %+.3f; reading aid: %s" % (
        mean["noctrl"]["pooled"], red_ctrl, red_ing, red_ing / 2,
        "the NO-ING reduction is NOT specific to -ing rows (NO-CTRL reduction >= half of it)" if red_ctrl >= red_ing / 2 and red_ing > 0 else "a comparable removal does not match the NO-ING reduction"))
    print("\n".join(out))
    if args.json:
        with open(args.json, "w", encoding="utf-8") as handle:
            json.dump({"seeds": SEEDS, "values": {a: {m: vals[a][m].tolist() for m, _ in METRICS} for a in ARMS},
                       "counts": {a: {str(s): counts[a][s] for s in SEEDS} for a in ARMS}, "selected": selected}, handle, indent=1)


if __name__ == "__main__":
    main()

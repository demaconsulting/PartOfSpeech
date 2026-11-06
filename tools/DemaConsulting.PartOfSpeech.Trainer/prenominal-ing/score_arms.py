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

Standard library only (like every other script of the repository's tools): the Student t quantile and p-value are computed with the
regularized incomplete beta function (continued fraction); `--selftest` checks them against published values. Reproducibility note:
the committed bootstrap intervals of the RUN-LOG ("Results and selection (dev only)") were produced by the first version of this script
with numpy `default_rng` seed 20261104 (numpy 2.2, scipy 1.18). This standard-library version reproduces the means, standard deviations,
Welch intervals, degrees of freedom and p-values, the acceptance test and the rule outcome exactly, but `random.Random(20261104)` is
another stream than numpy's generator, so its bootstrap intervals agree with the committed ones only statistically (Monte-Carlo
error), not digit for digit; the committed figures are not altered.

Historical record: the script is tied to the state of the branch at the pre-registration (commit 476a51d; FULL data, shipped weights = FULL).
After the adoption of NO-ING the committed data and weights differ, so re-run it with `--repo` pointing to a `git worktree` of 476a51d (or
0d7a0b3), where the frozen item list is current too. An acceptance failure (any check below false) exits 1 before the tables and the selection.
"""
import argparse
import hashlib
import json
import math
import os
import random
import statistics
import sys

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
    """One list of counts (COLS) per document of the set, in the order of sets.docs[setname]."""
    gold, doc = (sets.og, sets.od) if setname == "open" else (sets.ug, sets.ud)
    index = {d: i for i, d in enumerate(sets.docs[setname])}
    arr = [[0] * len(COLS) for _ in index]
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
                n_col, ok_col = ("in_", "iok") if kind == "compound-ing" else ("zn", "zok")
                row[IX[n_col]] += 1
                row[IX[ok_col]] += tag == "NOUN"
            elif kind == "verbal-ing":
                row[IX["vn"]] += 1
                row[IX["vok"]] += tag == "VERB"
    return arr


def col_sums(rows):
    """Column sums of a list of count rows."""
    return [sum(col) for col in zip(*rows)]


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
    """o, u: summed (or resampled-summed) count lists over the documents of open dev / UD dev."""
    g = lambda a, c: a[IX[c]]
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


def _betacf(a, b, x):
    """Continued fraction of the incomplete beta function (modified Lentz's method)."""
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


def t_quantile_975(df):
    """The 0.975 quantile of Student's t distribution (bisection on the two-sided p = 0.05)."""
    lo, hi = 0.0, 2.0
    while t_two_sided_p(hi, df) > 0.05:
        hi *= 2.0
    for _ in range(200):
        mid = (lo + hi) / 2.0
        if t_two_sided_p(mid, df) > 0.05:
            lo = mid
        else:
            hi = mid
    return (lo + hi) / 2.0


def selftest():
    """Check the t routines against published table values; exits 1 on a mismatch."""
    quantiles = {1: 12.7062047, 2: 4.3026527, 5: 2.5705818, 6: 2.4469119, 10: 2.2281389, 30: 2.0422725}
    bad = [df for df, q in quantiles.items() if abs(t_quantile_975(df) - q) > 1e-6]
    pvalues = [(2.0, 10, 0.0733880), (1.0, 1, 0.5), (2.2281389, 10, 0.05), (3.0, 5, 0.0300992)]
    bad += [(t, df) for t, df, p in pvalues if abs(t_two_sided_p(t, df) - p) > 1e-6]
    print("selftest: %s" % ("ok" if not bad else "FAILED for %s" % bad))
    sys.exit(1 if bad else 0)


def percentile(values, q):
    """Linear-interpolation percentile (the default definition of numpy.percentile)."""
    ordered = sorted(values)
    pos = q / 100.0 * (len(ordered) - 1)
    lo = int(math.floor(pos))
    hi = min(lo + 1, len(ordered) - 1)
    return ordered[lo] + (ordered[hi] - ordered[lo]) * (pos - lo)


def mean(values):
    return sum(values) / len(values)


def welch(a, b):
    """Difference of the means b - a, Welch t 95% interval, df, p."""
    d = mean(b) - mean(a)
    va, vb = statistics.variance(a) / len(a), statistics.variance(b) / len(b)
    se = math.sqrt(va + vb)
    if se == 0:
        return d, d, d, float("nan"), float("nan")
    df = (va + vb) ** 2 / (va ** 2 / (len(a) - 1) + vb ** 2 / (len(b) - 1))
    t = t_quantile_975(df)
    return d, d - t * se, d + t * se, df, t_two_sided_p(d / se, df)


def sha(path):
    return hashlib.sha256(open(path, "rb").read()).hexdigest()


def main():
    parser = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    parser.add_argument("--scratch")
    parser.add_argument("--repo")
    parser.add_argument("--diag", default=None, help="folder of the diagnosis predictions (new-s*, abl-s*), for the acceptance comparison")
    parser.add_argument("--json", default=None)
    parser.add_argument("--selftest", action="store_true", help="check the Student t routines against published values and exit")
    args = parser.parse_args()
    if args.selftest:
        selftest()
    if not args.scratch or not args.repo:
        parser.error("--scratch and --repo are required")
    sets = Sets(args.repo)
    runs = os.path.join(args.scratch, "runs")
    data = {a: {s: {k: per_doc(sets, k, read_pred(os.path.join(runs, "%s-s%d-%s.tsv" % (a, s, k)))) for k in ("open", "ud")} for s in SEEDS} for a in ARMS}
    out = []
    p = out.append
    totals = lambda a, s: (col_sums(data[a][s]["open"]), col_sums(data[a][s]["ud"]))
    vals = {a: {m: [value(m, *totals(a, s)) for s in SEEDS] for m, _ in METRICS} for a in ARMS}
    counts = {a: {s: {k: col_sums(data[a][s][k]) for k in ("open", "ud")} for s in SEEDS} for a in ARMS}

    # ----------------------------------------------------------- acceptance
    p("## Acceptance")
    o, u = totals("full", SEEDS[0])
    p("FULL shipped seed: open harmful %d of %d decisions; UD harmful %d of %d; UD agreement %d of %d (%.3f%%)"
      % (o[IX["harm"]], o[IX["nv"]], u[IX["harm"]], u[IX["nv"]], u[IX["ok"]], u[IX["n"]], 100 * u[IX["ok"]] / u[IX["n"]]))
    checks = []  # every acceptance result; any False stops the script before the tables and the selection
    expected_full = [92.803, 92.711, 92.655, 92.695, 92.747]
    match_full = all(abs(a - b) < 5e-4 for a, b in zip(vals["full"]["agree"], expected_full))
    checks.append(match_full)
    p("FULL UD agreement per seed: %s; diagnosis NEW: %s; match: %s" % (
        ", ".join("%.3f" % v for v in vals["full"]["agree"]), ", ".join("%.3f" % v for v in expected_full), match_full))
    shipped_ok = (int(o[IX["harm"]]) == 103 and int(o[IX["nv"]]) == 4467 and int(u[IX["harm"]]) == 107
                  and int(u[IX["nv"]]) == 4570 and int(u[IX["ok"]]) == 23338)
    checks.append(shipped_ok)
    p("shipped-seed acceptance (103/4,467; 107/4,570; 23,338): %s" % shipped_ok)
    exp_abl = {"agree": [92.711, 92.779, 92.683, 92.616, 92.747], "open_h": [90, 98, 96, 100, 90], "ud_h": [103, 109, 95, 99, 101]}
    got_h = [counts["noing"][s]["open"][IX["harm"]] for s in SEEDS]
    got_u = [counts["noing"][s]["ud"][IX["harm"]] for s in SEEDS]
    match_abl = (got_h == exp_abl["open_h"] and got_u == exp_abl["ud_h"]
                 and all(abs(a - b) < 5e-4 for a, b in zip(vals["noing"]["agree"], exp_abl["agree"])))
    checks.append(match_abl)
    p("NO-ING vs diagnosis ablation: agreement %s; open harmful %s (expected %s); UD harmful %s (expected %s); match: %s" % (
        ", ".join("%.3f" % v for v in vals["noing"]["agree"]), got_h, exp_abl["open_h"], got_u, exp_abl["ud_h"], match_abl))
    if args.diag:
        for arm, dname in (("full", "new"), ("noing", "abl")):
            same = []
            for s in SEEDS:
                a = os.path.join(runs, "%s-s%d.bin" % (arm, s))
                b = os.path.join(args.diag, "%s-s%d.bin" % (dname, s))
                same.append(os.path.exists(b) and sha(a) == sha(b))
            checks.extend(same)
            p("weights byte-identical to the diagnosis %s models per seed: %s" % (dname, same))
    shipped = os.path.join(args.repo, "src", "DemaConsulting.PartOfSpeech", "Resources", "weights.bin")
    shipped_same = sha(os.path.join(runs, "full-s%d.bin" % SEEDS[0])) == sha(shipped)
    checks.append(shipped_same)
    p("FULL shipped-seed weights byte-identical to shipped weights.bin: %s" % shipped_same)
    if not all(checks):
        print("\n".join(out))
        print("ACCEPTANCE FAILED: no tables and no selection are produced (pre-registration: stop on an acceptance failure)")
        sys.exit(1)

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
            p("| %s | %s | %.3f +- %.3f |" % (LABEL[a], " | ".join(cells), mean(v), statistics.stdev(v)))

    # ----------------------------------------------------------- differences vs FULL
    # combined seed + document bootstrap: one document resample per replicate, shared by the arms; the 5 seeds are resampled per arm
    rng = random.Random(BSEED)
    nd = {k: len(sets.docs[k]) for k in ("open", "ud")}

    def resample(n):
        """Sparse document weights of one replicate: [(document index, times drawn)] (a multinomial draw)."""
        w = {}
        for _ in range(n):
            i = rng.randrange(n)
            w[i] = w.get(i, 0) + 1
        return list(w.items())

    weights = [(resample(nd["open"]), resample(nd["ud"])) for _ in range(BOOT)]

    def weighted(rows, w):
        return [sum(c * rows[i][j] for i, c in w) for j in range(len(COLS))]

    boot = {}
    for a in ARMS:
        # per_seed[s][r] = every metric of seed s on the documents resampled by replicate r
        per_seed = []
        for s in SEEDS:
            per_seed.append([{m: value(m, weighted(data[a][s]["open"], wo), weighted(data[a][s]["ud"], wu)) for m, _ in METRICS}
                             for wo, wu in weights])
        idx = [[rng.randrange(len(SEEDS)) for _ in SEEDS] for _ in range(BOOT)]
        boot[a] = {m: [mean([per_seed[j][r][m] for j in idx[r]]) for r in range(BOOT)] for m, _ in METRICS}
    p("\n## Differences against FULL (descriptive; the selection is the rule below)")
    p("| Metric | Arm - FULL | mean difference | Welch t 95% (df, p) | seed + document bootstrap 95% | shipped-seed difference |")
    p("| --- | --- | ---: | --- | --- | ---: |")
    diffs = {}
    for m, label in METRICS:
        for a in ("noing", "noctrl"):
            d, lo, hi, df, pv = welch(vals["full"][m], vals[a][m])
            bd = [x - y for x, y in zip(boot[a][m], boot["full"][m])]
            blo, bhi = percentile(bd, 2.5), percentile(bd, 97.5)
            diffs[(a, m)] = (d, lo, hi, blo, bhi)
            p("| %s | %s | %+.3f | [%+.3f, %+.3f] (%.1f, p = %.3f) | [%+.3f, %+.3f] | %+.3f |" % (
                label.strip(), LABEL[a], d, lo, hi, df, pv, blo, bhi, vals[a][m][0] - vals["full"][m][0]))
    p("\nNO-ING - NO-CTRL (pooled harmful / 1k): mean difference %+.3f, Welch 95%% [%+.3f, %+.3f]" % welch(vals["noctrl"]["pooled"], vals["noing"]["pooled"])[:3])

    # ----------------------------------------------------------- the rule
    p("\n## Application of the pre-registered selection rule")
    avg = {a: {m: mean(vals[a][m]) for m, _ in METRICS} for a in ARMS}
    cand = ["full", "noing"]
    prim = {a: avg[a]["pooled"] for a in cand}
    best_agree = max(avg[a]["agree"] for a in cand)
    best_comp = max(avg[a]["comp"] for a in cand)
    lowest = min(cand, key=lambda a: prim[a])
    diff = prim["full"] - prim[lowest]
    p("primary (mean pooled harmful / 1k): FULL %.3f, NO-ING %.3f; lowest candidate %s; difference FULL - lowest = %.3f per 1k (tie-break threshold 0.5)"
      % (prim["full"], prim["noing"], LABEL[lowest], diff))
    c1 = {a: best_agree - avg[a]["agree"] for a in cand}
    c2 = {a: best_comp - avg[a]["comp"] for a in cand}
    for a in cand:
        p("%s: UD agreement mean %.3f (best %.3f, shortfall %.3f; C1 limit 0.15: %s); compound NOUN accuracy mean %.3f (best %.3f, shortfall %.3f; C2 limit 2.0: %s)"
          % (LABEL[a], avg[a]["agree"], best_agree, c1[a], "holds" if c1[a] <= 0.15 else "FAILS", avg[a]["comp"], best_comp, c2[a], "holds" if c2[a] <= 2.0 else "FAILS"))
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
    red_ctrl = avg["full"]["pooled"] - avg["noctrl"]["pooled"]
    p("negative control (context only, not a candidate): NO-CTRL pooled mean %.3f, FULL minus NO-CTRL = %+.3f; NO-ING reduction %+.3f; half of it %+.3f; reading aid: %s" % (
        avg["noctrl"]["pooled"], red_ctrl, red_ing, red_ing / 2,
        "the NO-ING reduction is NOT specific to -ing rows (NO-CTRL reduction >= half of it)" if red_ctrl >= red_ing / 2 and red_ing > 0 else "a comparable removal does not match the NO-ING reduction"))
    print("\n".join(out))
    if args.json:
        with open(args.json, "w", encoding="utf-8") as handle:
            json.dump({"seeds": SEEDS, "values": {a: {m: vals[a][m] for m, _ in METRICS} for a in ARMS},
                       "counts": {a: {str(s): counts[a][s] for s in SEEDS} for a in ARMS}, "selected": selected}, handle, indent=1)


if __name__ == "__main__":
    main()

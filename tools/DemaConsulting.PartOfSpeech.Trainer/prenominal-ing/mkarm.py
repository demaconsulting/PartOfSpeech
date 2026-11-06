"""Corpus cleanup C, `prenominal-ing` follow-up: build the correction tables of an ablation arm (scratch worktree only).

  python tools/DemaConsulting.PartOfSpeech.Trainer/prenominal-ing/mkarm.py --worktree DIR --detector ID [--count-only]

An arm is FULL (the committed branch data) minus the effective rows of `data/corrections/adjudicated.tsv` that carry one detector.
A row carries detector X when a row of `data/corrections/candidates.tsv` with `candidate_rule` X covers its token (the candidate
row's token_index up to token_index + span_length - 1); a row covered by several candidate rows carries every one of their rules and
belongs to each of them. The rows of `open` / `test` are left out of the set (they are never read or reversed: the held-out file is
neither needed nor touched), so the arm differs from FULL in the open train, open dev, MASC and silver files only.

Given --worktree (a scratch checkout of the branch HEAD, never the main working tree) the script rewrites, in that worktree only,
`data/corrections/adjudicated.tsv` to hold only the rows of the set and `data/corrections/mechanical.tsv` to its header (no
mechanical row is touched; none is superseded by a row of the set, which the script checks). The caller then runs
`python tools/data/corrections.py apply --write --reverse` in the worktree, which restores the pre-cleanup labels of exactly
those tokens, rebuilds the lexicon from the arm's corpora (`tools/data/build_lexicon.py`, `LexiconCompiler`) and the trainer.
--count-only prints the counts and writes nothing.
"""
import argparse
import collections
import os
import sys


def read(path):
    head, rows = [], []
    with open(path, encoding="utf-8", newline="") as handle:
        for line in handle:
            line = line.rstrip("\n")
            if line.startswith("#"):
                head.append(line)
            elif line:
                rows.append(line.split("\t"))
    return head, rows


def select(cdir, detector):
    """(adjudicated header, all rows, selected rows without open test, selected rows including open test, mechanical header, mechanical keys)."""
    _, candidates = read(os.path.join(cdir, "candidates.tsv"))
    covered = set()
    for c in candidates:
        if c[10] == detector:
            for j in range(int(c[4]), int(c[4]) + int(c[5])):
                covered.add((c[0], c[1], c[3], j))
    head, adjudicated = read(os.path.join(cdir, "adjudicated.tsv"))
    mhead, mechanical = read(os.path.join(cdir, "mechanical.tsv"))
    keys = {(r[0], r[1], r[3], int(r[4])) for r in mechanical}
    with_test = [r for r in adjudicated if (r[0], r[1], r[3], int(r[4])) in covered]
    kept = [r for r in with_test if not (r[0] == "open" and r[1] == "test")]
    return head, adjudicated, kept, with_test, mhead, keys


def main():
    parser = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    parser.add_argument("--worktree", required=True)
    parser.add_argument("--detector", required=True)
    parser.add_argument("--count-only", action="store_true")
    args = parser.parse_args()
    cdir = os.path.join(args.worktree, "data", "corrections")
    head, _, kept, with_test, mhead, keys = select(cdir, args.detector)
    split = lambda rows: dict(sorted(collections.Counter((r[0] if r[0] != "open" else "open-" + r[1]) for r in rows).items()))
    superseding = sum(1 for r in kept if (r[0], r[1], r[3], int(r[4])) in keys)
    print("detector %s: rows with open test %d %s; rows removed (open test left out) %d %s; superseding mechanical %d"
          % (args.detector, len(with_test), split(with_test), len(kept), split(kept), superseding))
    if superseding:
        print("a row of the set supersedes a mechanical row: stop")
        return 1
    if args.count_only:
        return 0
    with open(os.path.join(cdir, "adjudicated.tsv"), "w", encoding="utf-8", newline="") as handle:
        handle.write("\n".join(head) + "\n" + "".join("\t".join(r) + "\n" for r in kept))
    with open(os.path.join(cdir, "mechanical.tsv"), "w", encoding="utf-8", newline="") as handle:
        handle.write("\n".join(mhead) + "\n")
    return 0


if __name__ == "__main__":
    sys.exit(main())

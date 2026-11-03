"""Leakage check: no training document or sentence may appear in an evaluation set.

  python tools/data/check_leakage.py [--no-ud] [--write-report]

Training sets: data/open/train.tsv, data/masc/masc.tsv, data/silver/silver.tsv.
Evaluation sets: data/open/{dev,test}.tsv, data/eval/gold-v2, data/eval/shapes/probe.tsv, data/eval/ste-challenge,
and UD English-EWT train/dev/test when the git-ignored cache is present (fetch it with fetch_ud_ewt.py; nothing
from UD is ever committed or written to the report except counts).

Rules (the same two rules the C# test applies):
  1. documents: the source ids of a training set are disjoint from the document ids of every evaluation set
     (open dev/test source_id, gold v2 doc_id, STE source_id);
  2. sentences: key = lower-case alphanumerics of the sentence; no training key may equal an evaluation key, of any
     length (a short sentence such as "Thank you." is an exact sentence as well; the importers drop it from the
     training data, see leakage.py).
Near duplicates are reported, not gated: an evaluation sentence of at least 10 words whose word 6-grams are at least 80%
contained in the training word 6-grams of a set. Exit code 1 on any document or sentence overlap.

Data removal, not evaluation removal, is the remedy: the importers (import_masc.py, import_silver.py) drop training
documents (long matches) or sentences (short matches) that overlap an evaluation set and list them in their
excluded-documents.tsv and dropped-sentences.tsv.
"""
import argparse
import os
import re
import sys

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import common
import leakage

SHINGLE = 6
NEAR_MIN_WORDS = 10
NEAR_THRESHOLD = 0.8


def training_sets():
    sets = {}
    for label, parts in (("open-train", ("open", "train.tsv")), ("masc", ("masc", "masc.tsv")),
                         ("silver", ("silver", "silver.tsv"))):
        path = os.path.join(common.DATA, *parts)
        if os.path.exists(path):
            sets[label] = [(s["source_id"], s["text"]) for s in common.read_corpus(path)[1]]
    leakage.require_sets(sets, ("open-train", "masc", "silver"), "training")
    return sets


def words(text):
    return re.findall(r"[a-z0-9]+", common.nfc(text).lower())


def shingles(text):
    w = words(text)
    return {hash(" ".join(w[i:i + SHINGLE])) for i in range(len(w) - SHINGLE + 1)}


def check(include_ud):
    trains = training_sets()
    evals = leakage.evaluation_sets(include_ud)
    rows, failures = [], 0
    eval_docs = {}
    for label, items in evals.items():
        if not label.startswith("ud-ewt"):
            for doc, _ in items:
                eval_docs.setdefault(label, set()).add(doc)
    for train_label, train_items in trains.items():
        train_docs = {doc for doc, _ in train_items}
        train_keys = {common.key(text) for _, text in train_items} - {""}
        train_shingles = set()
        for _, text in train_items:
            train_shingles |= shingles(text)
        for eval_label, eval_items in evals.items():
            doc_overlap = len(train_docs & eval_docs.get(eval_label, set()))
            exact = near = 0
            for _, text in eval_items:
                if common.key(text) in train_keys:
                    exact += 1
                sh = shingles(text)
                if len(words(text)) >= NEAR_MIN_WORDS and sh and len(sh & train_shingles) / len(sh) >= NEAR_THRESHOLD:
                    near += 1
            failures += doc_overlap + exact
            rows.append((train_label, eval_label, len(eval_items), doc_overlap, exact, near))
    return rows, failures, bool(leakage.ud_sets()) if include_ud else False


def main():
    parser = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    parser.add_argument("--no-ud", action="store_true", help="ignore the UD cache even when present")
    parser.add_argument("--write-report", action="store_true", help="write data/leakage-report.tsv")
    args = parser.parse_args()
    rows, failures, ud_checked = check(not args.no_ud)
    lines = ["# Leakage report (tools/data/check_leakage.py); rules in data/README.md section Leakage check",
             "# UD English-EWT checked in this run: %s (counts only; nothing from UD is stored)" % ("yes" if ud_checked else "no"),
             "# columns = training_set, evaluation_set, evaluation_sentences, document_overlaps, exact_sentence_overlaps, near_duplicate_sentences"]
    for row in rows:
        lines.append("\t".join(str(c) for c in row))
    for line in lines[1:2] + [""] + lines[3:]:
        print(line)
    if args.write_report:
        common.write_text(os.path.join(common.DATA, "leakage-report.tsv"), lines)
    if failures:
        print("LEAKAGE FOUND: %d document or exact sentence overlaps" % failures)
        sys.exit(1)
    print("no document or exact sentence overlap between training and evaluation sets")


if __name__ == "__main__":
    main()

"""Leakage check: no training document or sentence may appear in an evaluation set.

  python tools/data/check_leakage.py [--no-ud] [--write-report]

Training sets: data/open/train.tsv, data/masc/masc.tsv, data/silver/silver.tsv, data/imperative/imperative.tsv.
Evaluation sets: data/open/{dev,test}.tsv, data/eval/gold-v2, data/eval/shapes/probe.tsv, data/eval/ste-challenge,
data/eval/shapes-dev/dev.tsv (the hard-shape development set; required), and UD English-EWT train/dev/test when the
git-ignored cache is present (fetch it with fetch_ud_ewt.py; nothing from UD is ever committed or written to the report
except counts).

Rules (the same two rules the C# test applies):
  1. documents: the source ids of a training set are disjoint from the document ids of every evaluation set
     (open dev/test source_id, gold v2 doc_id, STE source_id);
  2. sentences: key = lower-case alphanumerics of the sentence; no training key may equal an evaluation key, of any
     length (a short sentence such as "Thank you." is an exact sentence as well; the importers drop it from the
     training data, see leakage.py).
Near duplicates are reported, not gated: an evaluation sentence of at least 10 words whose word 6-grams are at least 80%
contained in the training word 6-grams of a set. Exit code 1 on any document or sentence overlap.

Second section (the hard-shape development set against every other evaluation set; written to
data/eval/shapes-dev/leakage-report.tsv, counts only): document ids, exact sentence keys and (for the shape probe only)
shared word 5-grams are gated; near duplicates (the same 10-word, 80% 6-gram rule) are reported. The development set
exists to select settings, so it must share neither text nor wording with the held-out sets. Only counts and the ids of
the failing development sentences are printed; no text of any evaluation set is ever printed or stored.

Third section (the hard tranche data/eval/shapes-dev/hard.tsv, when the file exists; required once leakage.HARD_TRANCHE_REQUIRED is True;
written to data/eval/shapes-dev/hard-leakage-report.tsv): the same checks against every evaluation set that is not a tranche of the
development set, plus a `shapes-dev` row for the standard tranche. The two tranches are cut from the same federal documents, so a shared
document is reported there and not gated; an exact sentence and a same-document near duplicate (at least 10 words, at least 80% of the word
6-grams in the standard sentences of the same document) are gated. The training sets are checked against hard.tsv in the first section like
against any evaluation set (label `shapes-dev-hard`).

Data removal, not evaluation removal, is the remedy: the importers (import_masc.py, import_silver.py) and the builder of
the imperative source (imperative_data.py) drop training documents (long matches) or sentences (short matches) that overlap an
evaluation set and list them in their excluded-documents.tsv and dropped-sentences.tsv.
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
                         ("silver", ("silver", "silver.tsv")), ("imperative", ("imperative", "imperative.tsv"))):
        path = os.path.join(common.DATA, *parts)
        if os.path.exists(path):
            sets[label] = [(s["source_id"], s["text"]) for s in common.read_corpus(path)[1]]
    leakage.require_sets(sets, ("open-train", "masc", "silver", "imperative"), "training")
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


def dev_check(include_ud, tranche="standard"):
    """A tranche of the hard-shape development set against every other evaluation set: (rows, failures, failing sentence ids).

    A row is (evaluation_set, dev_sentences, document_overlaps, exact_sentence_overlaps, near_duplicate_sentences,
    probe_5gram_sentences). Documents, exact keys and (for the shape probe) shared word 5-grams are gated; near
    duplicates are reported. The 5-gram column is `-` for every set but the probe. Only counts and the ids of the
    development sentences that fail (a shared document, an exact sentence or a shared probe 5-gram) are returned; nothing of an evaluation set (the probe especially) is returned.

    The hard tranche (`tranche="hard"`, data/eval/shapes-dev/hard.tsv) is checked the same way against every set that is not a tranche of
    the development set, and gets one more row, `shapes-dev` (the standard tranche): both tranches are cut from the same federal documents, so
    a shared document is allowed (counted in the row, not gated), but an exact sentence and a near duplicate (a sentence of at least 10 words
    with at least 80% of its word 6-grams in the standard sentences of its own document) are gated and name the hard sentence.
    """
    name = "hard.tsv" if tranche == "hard" else "dev.tsv"
    path = os.path.join(common.DATA, "eval", "shapes-dev", name)
    if not os.path.exists(path):
        raise SystemExit("missing committed development set: data/eval/shapes-dev/" + name)
    dev = [(s["sent_id"], s["source_id"], s["text"]) for s in common.read_corpus(path)[1]]  # (sentence id, document id, text)
    dev_docs = {doc for _, doc, _ in dev}
    probe_grams = leakage.held_out_overlap_keys(include_ud, leakage.PROBE_GRAM_WORDS)
    rows, failing = [], []
    for label, items in leakage.evaluation_sets(include_ud, shapes_dev=False).items():
        docs = set() if label.startswith("ud-ewt") else {doc for doc, _ in items}
        keys = {common.key(text) for _, text in items} - {""}
        grams = set()
        for _, text in items:
            grams |= shingles(text)
        exact = near = probe = 0
        for sent_id, doc, text in dev:
            hit = common.key(text) in keys
            shared_doc = doc in docs
            exact += hit
            sh = shingles(text)
            if len(words(text)) >= NEAR_MIN_WORDS and sh and len(sh & grams) / len(sh) >= NEAR_THRESHOLD:
                near += 1
            shared = label == "shapes-probe" and leakage.gram_overlap(text, probe_grams, leakage.PROBE_GRAM_WORDS)
            probe += shared
            if hit or shared or shared_doc:
                failing.append(sent_id)
        rows.append((label, len(dev), len(dev_docs & docs), exact, near, probe if label == "shapes-probe" else "-"))
    failures = sum(row[2] + row[3] + (row[5] if row[5] != "-" else 0) for row in rows)
    if tranche == "hard":
        standard_path = os.path.join(common.DATA, "eval", "shapes-dev", "dev.tsv")
        if not os.path.exists(standard_path):
            raise SystemExit("missing committed development set: data/eval/shapes-dev/dev.tsv")
        standard = [(s["source_id"], s["text"]) for s in common.read_corpus(standard_path)[1]]
        standard_keys = {common.key(text) for _, text in standard} - {""}
        standard_grams = {}
        for doc, text in standard:
            standard_grams.setdefault(doc, set()).update(shingles(text))
        exact = near = 0
        for sent_id, doc, text in dev:
            hit = common.key(text) in standard_keys
            sh = shingles(text)
            close = len(words(text)) >= NEAR_MIN_WORDS and bool(sh) and len(sh & standard_grams.get(doc, set())) / len(sh) >= NEAR_THRESHOLD
            exact += hit
            near += close
            if hit or close:
                failing.append(sent_id)
        rows.append((leakage.SHAPES_DEV_LABEL, len(dev), len(dev_docs & {doc for doc, _ in standard}), exact, near, "-"))
        failures += exact + near
    return rows, failures, sorted(set(failing))


def hard_tranche_present():
    """True when the hard tranche is checked: its file exists, or HARD_TRANCHE_REQUIRED (then a missing file fails loudly in dev_check)."""
    return leakage.HARD_TRANCHE_REQUIRED or os.path.exists(os.path.join(common.DATA, "eval", "shapes-dev", "hard.tsv"))


def tranche_lines(title, rows, ud_checked, extra=()):
    lines = ["# Leakage report of %s (tools/data/check_leakage.py); counts only" % title,
             "# UD English-EWT checked in this run: %s (counts only; nothing from UD is stored)" % ("yes" if ud_checked else "no"),
             "# probe_5gram_sentences = development sentences that share a word 5-gram with a shape probe sentence (gated, no probe text is stored)"]
    lines += list(extra)
    lines.append("# columns = evaluation_set, dev_sentences, document_overlaps, exact_sentence_overlaps, near_duplicate_sentences, probe_5gram_sentences")
    return lines + ["\t".join(str(c) for c in row) for row in rows]


def main():
    parser = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    parser.add_argument("--no-ud", action="store_true", help="ignore the UD cache even when present")
    parser.add_argument("--write-report", action="store_true",
                        help="write data/leakage-report.tsv, data/eval/shapes-dev/leakage-report.tsv and (hard tranche present) "
                             "data/eval/shapes-dev/hard-leakage-report.tsv")
    args = parser.parse_args()
    rows, failures, ud_checked = check(not args.no_ud)
    lines = ["# Leakage report (tools/data/check_leakage.py); rules in data/README.md section Leakage check",
             "# UD English-EWT checked in this run: %s (counts only; nothing from UD is stored)" % ("yes" if ud_checked else "no"),
             "# columns = training_set, evaluation_set, evaluation_sentences, document_overlaps, exact_sentence_overlaps, near_duplicate_sentences"]
    for row in rows:
        lines.append("\t".join(str(c) for c in row))
    for line in lines[1:2] + [""] + lines[3:]:
        print(line)
    dev_rows, dev_failures, failing = dev_check(not args.no_ud)
    dev_lines = tranche_lines("the hard-shape development set", dev_rows, ud_checked)
    print()
    for line in dev_lines[1:2] + dev_lines[3:]:
        print(line)
    hard_lines = []
    if hard_tranche_present():
        hard_rows, hard_failures, hard_failing = dev_check(not args.no_ud, "hard")
        hard_lines = tranche_lines("the hard tranche of the hard-shape development set", hard_rows, ud_checked,
                                   ["# shapes-dev row = the standard tranche: documents are shared on purpose (reported, not gated); exact sentences and "
                                    "same-document near duplicates are gated"])
        print()
        for line in hard_lines[1:2] + hard_lines[4:]:
            print(line)
        dev_failures += hard_failures
        failing += ["hard:" + sent_id for sent_id in hard_failing]
    if args.write_report:
        common.write_text(os.path.join(common.DATA, "leakage-report.tsv"), lines)
        common.write_text(os.path.join(common.DATA, "eval", "shapes-dev", "leakage-report.tsv"), dev_lines)
        if hard_lines:
            common.write_text(os.path.join(common.DATA, "eval", "shapes-dev", "hard-leakage-report.tsv"), hard_lines)
    if dev_failures:
        print("DEV LEAKAGE FOUND: %d overlaps; remove these development sentences and rebuild: %s" % (dev_failures, ", ".join(failing[:50])))
    if failures:
        print("LEAKAGE FOUND: %d document or exact sentence overlaps" % failures)
    if failures or dev_failures:
        sys.exit(1)
    print("no document or exact sentence overlap between training and evaluation sets, "
          "and none between the development set and the other evaluation sets")


if __name__ == "__main__":
    main()

"""Leakage universe shared by the importers and check_leakage.py (Python 3, standard library only).

Evaluation sets whose sentences and documents no training file may contain:

  open-dev, open-test        data/open/{dev,test}.tsv
  gold-v2                    data/eval/gold-v2/{dev,test}.tsv   (column `sentence`; document column `doc_id`)
  shapes-probe               data/eval/shapes/probe.tsv         (column `sentence`; target braces are ignored by the key)
  ste-challenge              data/eval/ste-challenge/test.tsv
  ud-ewt-dev, ud-ewt-test, ud-ewt-train   tools/data/cache/ud-ewt/*.conllu, only when the git-ignored cache is present
                                          (never committed; only the match result is reported)

Keys are lower-case alphanumerics of the sentence text, compared only when at least 25 characters long.
"""
import csv
import os
import sys

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import common

csv.field_size_limit(1 << 24)


def _tsv_rows(path):
    """Rows as dicts. Columns come from a `# columns = a b c` comment line, else from the first non-comment line."""
    all_lines = common.read_lines(path)
    lines = [line for line in all_lines if line and not line.startswith("#")]
    names = None
    for line in all_lines:
        if line.startswith("# columns = "):
            names = line[len("# columns = "):].split(" ")
    if names is not None:
        reader = csv.DictReader(lines, fieldnames=names, delimiter="\t", quoting=csv.QUOTE_NONE)
    else:
        reader = csv.DictReader(lines, delimiter="\t", quoting=csv.QUOTE_NONE)
    return list(reader)


def corpus_sets():
    """{label: [(source_id, sentence text)]} of the committed evaluation sets that use the shared corpus format."""
    sets = {}
    for label, parts in (("open-dev", ("open", "dev.tsv")), ("open-test", ("open", "test.tsv")),
                         ("ste-challenge", ("eval", "ste-challenge", "test.tsv"))):
        path = os.path.join(common.DATA, *parts)
        if os.path.exists(path):
            sets[label] = [(s["source_id"], s["text"]) for s in common.read_corpus(path)[1]]
    return sets


def table_sets():
    """{label: [(document id, sentence text)]} of the committed evaluation sets that are plain tables."""
    sets = {}
    gold = []
    for name in ("dev.tsv", "test.tsv"):
        path = os.path.join(common.DATA, "eval", "gold-v2", name)
        if os.path.exists(path):
            gold += [(row["doc_id"], row["sentence"]) for row in _tsv_rows(path)]
    if gold:
        sets["gold-v2"] = gold
    path = os.path.join(common.DATA, "eval", "shapes", "probe.tsv")
    if os.path.exists(path):
        sets["shapes-probe"] = [(row["id"], row["sentence"]) for row in _tsv_rows(path)]
    return sets


def ud_sets():
    """{label: [(sent_id, text)]} of the UD English-EWT files in the git-ignored cache (empty when not fetched)."""
    sets = {}
    folder = os.path.join(common.CACHE, "ud-ewt")
    for split in ("dev", "test", "train"):
        path = os.path.join(folder, "en_ewt-ud-%s.conllu" % split)
        if not os.path.exists(path):
            continue
        items, current = [], None
        for line in common.read_lines(path):
            if line.startswith("# sent_id = "):
                current = line[12:]
            elif line.startswith("# text = ") and current is not None:
                items.append((current, line[9:]))
        sets["ud-ewt-" + split] = items
    return sets


REQUIRED_EVALUATION_SETS = ("open-dev", "open-test", "ste-challenge", "gold-v2", "shapes-probe")


def require_sets(sets, required, kind):
    """Fail loudly when a committed data set is missing; a silent skip would let a leakage or coverage check pass."""
    missing = [label for label in required if label not in sets]
    if missing:
        raise SystemExit("missing committed %s sets: %s" % (kind, ", ".join(missing)))


def evaluation_sets(include_ud=True):
    sets = {}
    sets.update(corpus_sets())
    sets.update(table_sets())
    require_sets(sets, REQUIRED_EVALUATION_SETS, "evaluation")
    if include_ud:
        sets.update(ud_sets())
    return sets


def evaluation_keys(include_ud=True):
    """({key: label}, {document id: label}) over all evaluation sets."""
    keys, documents = {}, {}
    for label, items in evaluation_sets(include_ud).items():
        for doc, text in items:
            k = common.usable_key(text)
            if k is not None and k not in keys:
                keys[k] = label
            if not label.startswith("ud-ewt"):
                documents.setdefault(doc, label)
    return keys, documents

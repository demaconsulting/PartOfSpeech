"""Leakage universe shared by the importers and check_leakage.py (Python 3, standard library only).

Evaluation sets whose sentences and documents no training file may contain:

  open-dev, open-test        data/open/{dev,test}.tsv
  gold-v2                    data/eval/gold-v2/{dev,test}.tsv   (column `sentence`; document column `doc_id`)
  shapes-probe               data/eval/shapes/probe.tsv         (held out; only the `sentence` column is read, by `sentence_column`; the
                                                                 braces around the target word are removed; labels, shapes, lemmas and
                                                                 targets are skipped unread)
  ste-challenge              data/eval/ste-challenge/test.tsv
  shapes-dev                 data/eval/shapes-dev/dev.tsv       (hard-shape development set; corpus format; required, so a
                                                                 missing file fails loudly; `shapes_dev.py` builds it and passes
                                                                 `shapes_dev=False` because the set does not exist yet)
  shapes-dev-hard            data/eval/shapes-dev/hard.tsv      (the hard tranche of that set, same format; required once
                                                                 HARD_TRANCHE_REQUIRED is True, which the commit that adds the file sets;
                                                                 `shapes_dev=False` leaves both tranches out)
  ud-ewt-dev, ud-ewt-test, ud-ewt-train   tools/data/cache/ud-ewt/*.conllu, only when the git-ignored cache is present
                                          (never committed; only the match result is reported)

Keys are the lower-case alphanumerics of the sentence text. Every key is compared, whatever its length: no training
sentence may equal an evaluation sentence ("Thank you." included). What the importers do about a match depends on the
length of the key (DOCUMENT_KEY_LENGTH): a long match means the two corpora share source text, so the whole training
document is excluded; a short match is a sentence that merely recurs in unrelated texts, so only that sentence is
dropped from its training document. Either way the evaluation data is never touched.
"""
import csv
import hashlib
import os
import re
import sys

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import common

csv.field_size_limit(1 << 24)

# A matching sentence key of at least this many alphanumeric characters excludes its whole training document;
# a shorter one drops only the sentence.
DOCUMENT_KEY_LENGTH = 25

# The label of the hard-shape development set among the evaluation sets, and the word n-gram length of the probe gate.
SHAPES_DEV_LABEL = "shapes-dev"
SHAPES_DEV_HARD_LABEL = "shapes-dev-hard"
SHAPES_DEV_LABELS = (SHAPES_DEV_LABEL, SHAPES_DEV_HARD_LABEL)
# False until data/eval/shapes-dev/hard.tsv is committed: the hard tranche is checked when its file exists, and a missing file is
# not a failure yet. The commit that adds hard.tsv sets this to True, from then on a missing file fails loudly like every other set.
HARD_TRANCHE_REQUIRED = False
PROBE_GRAM_WORDS = 5


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
                         ("ste-challenge", ("eval", "ste-challenge", "test.tsv")),
                         (SHAPES_DEV_LABEL, ("eval", "shapes-dev", "dev.tsv")),
                         (SHAPES_DEV_HARD_LABEL, ("eval", "shapes-dev", "hard.tsv"))):
        path = os.path.join(common.DATA, *parts)
        if os.path.exists(path):
            sets[label] = [(s["source_id"], s["text"]) for s in common.read_corpus(path)[1]]
    return sets


def sentence_column(path, name="sentence"):
    """The texts of the column `name` of a tab-separated table, and nothing else of the table.

    Used for the held-out shape probe, whose labels, shapes, lemmas and targets must never be read: the table is read as bytes,
    the header line is the only other line that is decoded, and the fields before and after the column are skipped by position
    (never decoded, never parsed, whatever they contain). A comment line starts with `# `. The braces that mark the target
    word are removed from the sentence. A row without the column, or an invalid sentence, is an error, never a silent skip.
    """
    with open(path, "rb") as handle:
        lines = [line.rstrip(b"\r") for line in handle.read().split(b"\n")]
    names, header_seen, texts = None, False, []
    for line in lines:
        if line.startswith(b"# columns = "):
            names = line[len(b"# columns = "):].decode("utf-8").split(" ")
            header_seen = True
        elif line == b"" or line.startswith(b"# "):
            continue
        elif not header_seen:
            names = line.decode("utf-8").split("\t")
            header_seen = True
        else:
            if names is None or name not in names:
                raise ValueError("%s: no column %s" % (os.path.basename(path), name))
            start = 0
            for _ in range(names.index(name)):
                tab = line.find(b"\t", start)
                if tab < 0:
                    raise ValueError("%s: a row has no column %s" % (os.path.basename(path), name))
                start = tab + 1
            end = line.find(b"\t", start)
            text = line[start:] if end < 0 else line[start:end]
            texts.append(text.decode("utf-8").replace("{", "").replace("}", ""))
    return texts


def probe_path():
    return os.path.join(common.DATA, "eval", "shapes", "probe.tsv")


def probe_sentences():
    """The sentence texts of the held-out shape probe (target braces removed); [] when the file is missing.

    Fails closed: a probe with no data row, or with a sentence that has no letter or digit (an empty cell), is a SystemExit (exit code 1),
    because an empty or truncated probe would register as present while contributing no key to the filter.
    """
    path = probe_path()
    if not os.path.exists(path):
        return []
    texts = sentence_column(path)
    if not texts:
        raise SystemExit("shapes probe has no data rows: " + os.path.basename(path))
    empty = [n for n, text in enumerate(texts, 1) if not common.key(text)]
    if empty:
        raise SystemExit("shapes probe has an empty sentence in row(s) %s: %s" % (", ".join(map(str, empty[:10])), os.path.basename(path)))
    return texts


def table_sets():
    """{label: [(document id, sentence text)]} of the committed evaluation sets that are plain tables.

    The probe has no document ids that matter here; its items get the synthetic ids `shapes-probe:N`.
    """
    sets = {}
    gold = []
    for name in ("dev.tsv", "test.tsv"):
        path = os.path.join(common.DATA, "eval", "gold-v2", name)
        if os.path.exists(path):
            gold += [(row["doc_id"], row["sentence"]) for row in _tsv_rows(path)]
    if gold:
        sets["gold-v2"] = gold
    if os.path.exists(probe_path()):
        sets["shapes-probe"] = [("shapes-probe:%d" % n, text) for n, text in enumerate(probe_sentences(), 1)]
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


REQUIRED_EVALUATION_SETS = (("open-dev", "open-test", "ste-challenge", "gold-v2", "shapes-probe", SHAPES_DEV_LABEL)
                            + ((SHAPES_DEV_HARD_LABEL,) if HARD_TRANCHE_REQUIRED else ()))


def require_sets(sets, required, kind):
    """Fail loudly when a committed data set is missing; a silent skip would let a leakage or coverage check pass."""
    missing = [label for label in required if label not in sets]
    if missing:
        raise SystemExit("missing committed %s sets: %s" % (kind, ", ".join(missing)))


def evaluation_sets(include_ud=True, shapes_dev=True):
    """{label: [(document id, text)]} of every evaluation set; a missing required set is a loud failure.

    `shapes_dev=False` leaves the hard-shape development set (both tranches) out (and does not require them): the builder of that set
    (shapes_dev.py) checks its candidates against all the other sets through `held_out_overlap_keys` only, and a stale earlier build must not count.
    """
    sets = {}
    sets.update(corpus_sets())
    sets.update(table_sets())
    required = REQUIRED_EVALUATION_SETS
    if not shapes_dev:
        for label in SHAPES_DEV_LABELS:
            sets.pop(label, None)
        required = tuple(label for label in required if label not in SHAPES_DEV_LABELS)
    require_sets(sets, required, "evaluation")
    if include_ud:
        sets.update(ud_sets())
    return sets


def words(text):
    """Lower-case alphanumeric words of a text (the units of the word n-grams)."""
    return re.findall(r"[a-z0-9]+", common.nfc(text).lower())


def stable_hash(kind, text):
    """A 64-bit hash of `kind` and `text` that is the same in every process (the built-in hash() is salted per process)."""
    return int.from_bytes(hashlib.blake2b((kind + ":" + text).encode("utf-8"), digest_size=8).digest(), "big")


def key_hash(text):
    """The hash of the sentence key of a text, or None when the text has no letter or digit (never a match)."""
    k = common.key(text)
    return stable_hash("key", k) if k else None


def gram_hashes(text, n=PROBE_GRAM_WORDS):
    """The set of hashes of the word n-grams of a text (hashes, so that a set of them never holds readable text)."""
    w = words(text)
    return {stable_hash("gram", " ".join(w[i:i + n])) for i in range(len(w) - n + 1)}


def held_out_overlap_keys(include_ud=True, n=PROBE_GRAM_WORDS):
    """The only view of the held-out sets that building a development set may have: an opaque frozen set of hashes.

    It holds the hashed sentence keys of every evaluation set but the hard-shape development set, and the hashed word
    `n`-grams of the shape probe sentences. It holds no label, shape, lemma, target, document id or readable text: the probe is
    read through `sentence_column` (the sentence column only). The set is an exclusion filter for candidates, tested with
    `key_overlap` and `gram_overlap`; it is never used to choose, weight or frame a candidate. A probe sentence with fewer
    than `n` words has no n-gram (its sentence key covers it).
    """
    found = set()
    for label, items in evaluation_sets(include_ud, shapes_dev=False).items():
        for _, text in items:
            h = key_hash(text)
            if h is not None:
                found.add(h)
            if label == "shapes-probe":
                found |= gram_hashes(text, n)
    return frozenset(found)


def key_overlap(text, keys):
    """True when the sentence key of a text is in the opaque key set."""
    h = key_hash(text)
    return h is not None and h in keys


def gram_overlap(text, keys, n=PROBE_GRAM_WORDS):
    """True when a word n-gram of a text is in the opaque key set."""
    return not gram_hashes(text, n).isdisjoint(keys)


def evaluation_keys(include_ud=True, shapes_dev=True):
    """({key: label}, {document id: label}) over all evaluation sets (keys of every length, never the empty key)."""
    keys, documents = {}, {}
    for label, items in evaluation_sets(include_ud, shapes_dev).items():
        for doc, text in items:
            k = common.key(text)
            if k and k not in keys:
                keys[k] = label
            if not label.startswith("ud-ewt"):
                documents.setdefault(doc, label)
    return keys, documents


def match(text, keys):
    """(evaluation set, scope) when a training sentence equals an evaluation sentence, else None.

    scope is "document" for a key of DOCUMENT_KEY_LENGTH characters or more and "sentence" for a shorter one.
    """
    k = common.key(text)
    if not k or k not in keys:
        return None
    return keys[k], ("document" if len(k) >= DOCUMENT_KEY_LENGTH else "sentence")

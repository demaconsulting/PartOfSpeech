"""Adjudication of the staged judgment candidates by two blind taggers (Python 3, standard library only).

The candidates of data/corrections/candidates.tsv (and the tokens on which four off-the-shelf taggers disagree with the label) are
decided by two language models that never see the corpus label: each tags the sentences from scratch, following data/CONVENTIONS.md.
This tool does everything around the models that needs no model; it never calls one. The work folder (`--work`) is outside the
repository: it holds the voter flag files, the batches the taggers read, their outputs and the private key.

  python tools/data/adjudicate.py --work W select                 # choose the sentences (volumes first), write private/selection*.tsv|md
  python tools/data/adjudicate.py --work W export [--size 150]    # blind batches batches/bNNN.tsv, private/key.tsv, batches/status.tsv
  python tools/data/adjudicate.py --work W check-batches          # re-read every batch: 3 columns, tokens equal the corpus, no leak
  python tools/data/adjudicate.py --work W validate-output --batch b001 --model opus   # (or --all); updates batches/status.tsv
  python tools/data/adjudicate.py --work W status                 # state of every batch and model
  python tools/data/adjudicate.py --work W decide [--drop A,B] [--out FILE]   # decision rule -> adjudicated.tsv, reports
  python tools/data/adjudicate.py --work W sample [--seed N]      # the review sample for the precision gate (+ 8 control items)
  python tools/data/adjudicate.py --work W review-result --verdicts FILE ...   # precision per class, the gate, classes to drop

Blindness: a batch file has exactly three columns `id, token_index, token` (an opaque id per sentence, the token index, the escaped
token form) and nothing else: no label, no status, no source, no sentence id, no flag. The key that maps an opaque id back to the
corpus lives in `private/` and the taggers are never pointed to it. Cells are written with the csv module in the no-quoting
dialect (`quotechar=None`) after `escape_cell`, which keeps a token that contains `"`, a tab or a backslash in exactly one
cell; `unescape_cell` is its exact inverse.

The decision rule (see data/corrections/README.md, "Adjudication") is `decide_token`; the precision gate is `gate`.
"""
import argparse
import collections
import csv
import hashlib
import io
import os
import random
import re
import sys

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import common
import convention_rules
import corrections
import make_corrections

# ---------------------------------------------------------------- constants

SEED = 20261109                      # selection, export, sample
CAP = 14000                          # sentences
BATCH_SENTENCES = 150                # sentences per batch (at most)
BATCH_ROWS = 3500                    # token rows per batch (at most)
BANDS = ("P1", "P2", "P3", "P4")
MODELS = ("opus", "sol")
MAX_ATTEMPTS = 2                     # a tagger run per batch and model: the first one and one retry

# Detectors whose decision is about NOUN, VERB, ADJ, AUX or PROPN against NOUN/ADJ: band P1.
NV_DETECTORS = frozenset(("prenominal-ing", "prenominal-ed", "be-participle-gradable", "be-participle-adj-not-gradable",
                          "be-aux-passive", "be-passive-agent", "modal-aux-other", "zero-derived-degree", "propn-boundary",
                          "language-name", "title-alone", "title-before-name"))
# Detectors about a flag (CONVENTIONS, "Mentioned Words", test 6): a disagreement about a flag there is a `disputed` token.
FLAG_DETECTORS = frozenset(("mention-quoted", "mention-the-word", "mention-in-language", "foreign-token"))
SPLIT_FLAG_TAGS = frozenset(("NOUN", "VERB", "ADJ", "AUX"))

BATCH_HEADER = ("id", "token_index", "token")
OUTPUT_HEADER = ("id", "token_index", "token", "tag", "confidence", "flags")
CONFIDENCES = ("high", "medium", "low")
FLAG_CELLS = {"-": (), "mentioned": ("mentioned",), "foreign": ("foreign",), "foreign+mentioned": ("foreign", "mentioned")}
ID_RE = re.compile(r"^s[0-9a-f]{6}$")
BATCH_RE = re.compile(r"^b[0-9]{3,}$")
STATUS_COLUMNS = ("batch", "opus_state", "sol_state", "attempts_opus", "attempts_sol", "note")
KEY_COLUMNS = ("opaque_id", "batch", "source", "split", "sent_id", "source_id", "band", "ntokens", "candidate_tokens", "flag_tokens",
               "dependent_tokens")
SELECTION_COLUMNS = ("rank", "band", "source", "split", "sent_id", "ntokens")
DECISION_COLUMNS = ("source", "split", "source_id", "sent_id", "token_index", "token", "class", "classes", "band", "batch",
                    "current_tag", "current_status", "mech_rule", "mech_new_tag", "opus_tag", "opus_conf", "opus_flags", "sol_tag",
                    "sol_conf", "sol_flags", "voters", "outcome", "row_kind", "new_tag", "new_status", "final")
SAMPLE_KEY_COLUMNS = ("item", "kind", "random60", "source", "split", "sent_id", "token_index", "token", "class", "batch",
                      "class_total", "shown_current", "shown_proposed")
VERDICTS = ("correct", "incorrect", "unclear")
PRECISION_MIN = 0.90
UNCLEAR_MAX = 0.30
ROUND_ONE_LIMIT = 10                 # a class with exactly one incorrect among fewer rows gets a second round
KINDS_REVIEWED = ("tag", "flags")    # the rows the precision gate looks at; disputed marks are not reviewed


class Work:
    """The folders of the work area (`--work`)."""

    def __init__(self, root):
        self.root = root
        self.batches = os.path.join(root, "batches")
        self.private = os.path.join(root, "private")
        self.flags = os.path.join(root, "flags")

    def out(self, model):
        return os.path.join(self.batches, "out", model)

    def batch_file(self, batch):
        return os.path.join(self.batches, batch + ".tsv")

    def private_file(self, name):
        return os.path.join(self.private, name)


# ---------------------------------------------------------------- cells: escape and unescape

_ESCAPE_RE = re.compile("[\\\\\x00-\x1f\x7f-\x9f\u2028\u2029]")
_SIMPLE = {"\\": "\\\\", "\t": "\\t", "\n": "\\n", "\r": "\\r"}
_UNSIMPLE = {"\\": "\\", "t": "\t", "n": "\n", "r": "\r"}


def escape_cell(text):
    """The text of a cell so that it holds no tab, line break, other control character or lone backslash.

    Backslash, tab, LF and CR become `\\\\`, `\\t`, `\\n`, `\\r`; every other C0 or C1 control character, DEL, U+2028 and U+2029
    become `\\uXXXX` (lower-case hex). Everything else, including `"`, stays. `unescape_cell` is the exact inverse.
    """
    return _ESCAPE_RE.sub(lambda match: _SIMPLE.get(match.group(0)) or "\\u%04x" % ord(match.group(0)), text)


def unescape_cell(text):
    """The exact inverse of `escape_cell`; a backslash that starts no escape is a ValueError."""
    out = []
    position = 0
    while position < len(text):
        char = text[position]
        if char != "\\":
            out.append(char)
            position += 1
            continue
        following = text[position + 1:position + 2]
        if following in _UNSIMPLE and following:
            out.append(_UNSIMPLE[following])
            position += 2
        elif following == "u" and re.fullmatch("[0-9a-f]{4}", text[position + 2:position + 6]):
            out.append(chr(int(text[position + 2:position + 6], 16)))
            position += 6
        else:
            raise ValueError("bad escape at position %d of %r" % (position, text[:40]))
    return "".join(out)


# ---------------------------------------------------------------- tab separated files (csv module, no quoting)

def _writer(handle):
    return csv.writer(handle, delimiter="\t", quoting=csv.QUOTE_NONE, quotechar=None, escapechar=None, lineterminator="\n")


def write_rows(path, header, rows):
    """Write a TSV file: the header, then the rows, every cell escaped (UTF-8, LF)."""
    os.makedirs(os.path.dirname(path), exist_ok=True)
    with open(path, "w", encoding="utf-8", newline="") as handle:
        writer = _writer(handle)
        writer.writerow([escape_cell(str(cell)) for cell in header])
        for row in rows:
            writer.writerow([escape_cell(str(cell)) for cell in row])


def read_raw_rows(path):
    """(rows) of a TSV file as lists of cells exactly as written (still escaped); the first row is the header.

    A BOM is an error, a CRLF line end is accepted (it is not part of a cell), a lone CR or a blank line is an error.
    """
    with open(path, "r", encoding="utf-8", newline="") as handle:
        text = handle.read()
    if text.startswith("\ufeff"):
        raise ValueError(path + ": BOM")
    text = text.replace("\r\n", "\n")
    if "\r" in text:
        raise ValueError(path + ": carriage return inside a line")
    if text == "":
        raise ValueError(path + ": empty file")
    rows = []
    for number, cells in enumerate(csv.reader(io.StringIO(text, newline=""), delimiter="\t", quoting=csv.QUOTE_NONE, quotechar=None,
                                              escapechar=None, strict=True), 1):
        if not cells:
            raise ValueError("%s, line %d: blank line" % (path, number))
        rows.append(cells)
    return rows


def read_records(path):
    """(columns, [dict]) of a TSV file written by `write_rows`: every cell unescaped, every row has the header's cell count."""
    rows = read_raw_rows(path)
    columns = [unescape_cell(cell) for cell in rows[0]]
    records = []
    for number, cells in enumerate(rows[1:], 2):
        if len(cells) != len(columns):
            raise ValueError("%s, line %d: %d cells, expected %d" % (path, number, len(cells), len(columns)))
        records.append(dict(zip(columns, (unescape_cell(cell) for cell in cells))))
    return columns, records


def read_batch(path):
    """Rows of a batch file as (id, token_index, escaped token) tuples; raises ValueError unless it is a three column batch."""
    rows = read_raw_rows(path)
    if tuple(rows[0]) != BATCH_HEADER:
        raise ValueError("%s: header is %r, expected %r" % (path, tuple(rows[0]), BATCH_HEADER))
    for number, cells in enumerate(rows[1:], 2):
        if len(cells) != 3:
            raise ValueError("%s, line %d: %d cells, a batch has exactly 3" % (path, number, len(cells)))
    return [tuple(cells) for cells in rows[1:]]


# ---------------------------------------------------------------- inputs

class Inputs:
    """What the selection and the decision read from the data folder: corpora, candidates, mechanical rows, class ranks."""

    def __init__(self, corpora, candidates, mechanical):
        self.corpora = corpora
        self.candidates = candidates
        self.mechanical = mechanical
        self.by_sentence = collections.defaultdict(list)
        for row in candidates:
            self.by_sentence[(row.source, row.split, row.sent_id)].append(row)
        self.mech_by_address = {corrections.address(row): row for row in mechanical}
        counts = collections.Counter(row.candidate_rule for row in candidates)
        # rare detectors first, ties alphabetical: the primary class of a token with several candidate detectors
        self.class_rank = {name: rank for rank, name in enumerate(sorted(counts, key=lambda name: (counts[name], name)))}

    def sentence(self, key):
        """The corpus sentence dict of a (source, split, sent_id) key."""
        return self.corpora[(key[0], key[1])].by_id[key[2]]


def load_inputs(data_dir=None):
    """`Inputs` of a data folder (the committed corpora, candidates.tsv and mechanical.tsv)."""
    folder = corrections.corrections_dir(data_dir)
    candidates = corrections.read_table(os.path.join(folder, corrections.CANDIDATES_TABLE), "candidates")[1]
    mechanical = corrections.read_table(os.path.join(folder, corrections.MECHANICAL_TABLE), "corrections")[1]
    return Inputs(corrections.load_corpora(data_dir), candidates, mechanical)


FlagRow = collections.namedtuple("FlagRow", "token label voters flag")


def read_flags(directory, corpora):
    """{(split, sent_id): {token_index: FlagRow}} of the `flags_<split>.tsv` files of the voter run (open splits only).

    Every row is compared with the corpus (sentence, token form and label): a flags file of another corpus version is an error.
    """
    flags = {}
    for split in corrections.SOURCES["open"]:
        path = os.path.join(directory, "flags_%s.tsv" % split)
        if not os.path.exists(path):
            continue
        columns, records = read_records(path)
        if columns != ["sent_id", "token_index", "token", "label", "trf", "lg", "sm", "nltk", "flag"]:
            raise ValueError("%s: unexpected columns %r" % (path, columns))
        corpus = corpora.get(("open", split))
        if corpus is None:
            raise ValueError("no open/%s corpus is loaded for %s" % (split, path))
        for record in records:
            sentence = corpus.by_id.get(record["sent_id"])
            index = int(record["token_index"])
            if sentence is None or index >= len(sentence["rows"]) or sentence["rows"][index][0] != record["token"] \
                    or sentence["rows"][index][1] != record["label"]:
                raise ValueError("%s: %s#%s does not match the corpus (stale flags file?)" % (path, record["sent_id"], record["token_index"]))
            voters = (record["trf"], record["lg"], record["sm"], record["nltk"])
            flags.setdefault((split, record["sent_id"]), {})[index] = FlagRow(record["token"], record["label"], voters, record["flag"])
    return flags


def flag_kind(item):
    """`unanimous` (all four voters against the label), `split` (a split-flag token) or None for a `FlagRow`.

    A split-flag token is a `split-major` or `split` token where the label and the voters' tags together contain NOUN, VERB, ADJ or AUX.
    """
    if item.flag == "unanimous-against":
        return "unanimous"
    if item.flag in ("split-major", "split") and SPLIT_FLAG_TAGS & ({item.label} | set(item.voters)):
        return "split"
    return None


# ---------------------------------------------------------------- selection (bands and the cap)

Entry = collections.namedtuple("Entry", "key band ntokens detectors unanimous split split_major")


def sentence_hash(key, seed):
    """Deterministic pseudo-random sort key of a sentence (seeded SHA-256 of its key)."""
    return hashlib.sha256(("%d|%s|%s|%s" % (seed, key[0], key[1], key[2])).encode("utf-8")).hexdigest()


def band_of(detectors, unanimous, split):
    """The best priority band of a sentence: P1 N/V-relevant candidate, P2 unanimous-against token, P3 other candidate, P4 split token."""
    if detectors & NV_DETECTORS:
        return "P1"
    if unanimous:
        return "P2"
    if detectors:
        return "P3"
    return "P4" if split else None


def build_pool(inputs, flags):
    """The union before the cap: {key: Entry} of every sentence with a candidate or a voter flag.

    A voter-flag token that has a mechanical row does not pull its sentence into the pool (the rule decides it).
    """
    detectors = collections.defaultdict(set)
    for key, rows in inputs.by_sentence.items():
        detectors[key].update(row.candidate_rule for row in rows)
    unanimous, split, major = collections.Counter(), collections.Counter(), collections.Counter()
    for (split_name, sent_id), tokens in flags.items():
        key = ("open", split_name, sent_id)
        for index, item in tokens.items():
            if ("open", split_name, sent_id, index) in inputs.mech_by_address:
                continue
            kind = flag_kind(item)
            if kind == "unanimous":
                unanimous[key] += 1
            elif kind == "split":
                split[key] += 1
                major[key] += item.flag == "split-major"
    pool = {}
    for key in set(detectors) | set(unanimous) | set(split):
        band = band_of(detectors.get(key, frozenset()), unanimous[key], split[key])
        ntokens = len(inputs.sentence(key)["rows"])
        pool[key] = Entry(key, band, ntokens, frozenset(detectors.get(key, ())), unanimous[key], split[key], major[key])
    return pool


def priority_key(entry, seed=SEED):
    """Order inside a band: P4 by split-major tokens, then split-flag tokens (more first), everything by the seeded hash."""
    if entry.band == "P4":
        return (-entry.split_major, -entry.split, sentence_hash(entry.key, seed))
    return (0, 0, sentence_hash(entry.key, seed))


def apply_cap(pool, cap=CAP, seed=SEED):
    """(selected, cut): the entries in band order and priority order; the cap cuts the end of the lowest band that overflows."""
    selected, cut = [], []
    for band in BANDS:
        members = sorted((entry for entry in pool.values() if entry.band == band), key=lambda entry: priority_key(entry, seed))
        room = max(0, cap - len(selected))
        selected.extend(members[:room])
        cut.extend(members[room:])
    return selected, cut


def _table(headers, rows):
    lines = ["| " + " | ".join(headers) + " |", "| " + " | ".join("---" if index == 0 else "---:" for index in range(len(headers))) + " |"]
    lines.extend("| " + " | ".join(str(cell) for cell in row) + " |" for row in rows)
    return lines


def _label(key):
    return key[0] if key[1] == "-" else "%s/%s" % (key[0], key[1])


def selection_report(pool, selected, cut, inputs, cap=CAP):
    """Markdown lines with the volumes: bands, sources, detectors, voter flags, the cap and what it cuts (printed before anything is exported)."""
    lines = ["# Selection report", "", "Union before the cap: %d sentences, %d tokens. Cap: %d sentences. Selected: %d sentences, %d tokens."
             " Cut: %d sentences, %d tokens." % (len(pool), sum(e.ntokens for e in pool.values()), cap, len(selected),
                                                 sum(e.ntokens for e in selected), len(cut), sum(e.ntokens for e in cut)), ""]
    lines += ["## Bands (union before the cap)", ""]
    rows, cumulative_s, cumulative_t = [], 0, 0
    for band in BANDS:
        members = [e for e in pool.values() if e.band == band]
        cumulative_s += len(members)
        cumulative_t += sum(e.ntokens for e in members)
        chosen = [e for e in selected if e.band == band]
        rows.append((band, len(members), sum(e.ntokens for e in members), cumulative_s, cumulative_t, len(chosen), len(members) - len(chosen)))
    lines += _table(("band", "sentences", "tokens", "cumulative sentences", "cumulative tokens", "selected", "cut"), rows)
    lines += ["", "## Sources and splits (union before the cap, selected, cut)", ""]
    keys = sorted({(e.key[0], e.key[1]) for e in pool.values()})
    rows = []
    for source in keys:
        members = [e for e in pool.values() if (e.key[0], e.key[1]) == source]
        chosen = [e for e in selected if (e.key[0], e.key[1]) == source]
        rows.append((_label(source), len(members), sum(e.ntokens for e in members), len(chosen), sum(e.ntokens for e in chosen),
                     len(members) - len(chosen)))
    lines += _table(("source", "union sentences", "union tokens", "selected sentences", "selected tokens", "cut sentences"), rows)
    lines += ["", "## Cut by split and band", ""]
    rows = []
    for source in keys:
        for band in BANDS:
            members = [e for e in pool.values() if (e.key[0], e.key[1]) == source and e.band == band]
            lost = [e for e in cut if (e.key[0], e.key[1]) == source and e.band == band]
            if members:
                rows.append((_label(source), band, len(members), len(members) - len(lost), len(lost)))
    lines += _table(("source", "band", "union", "selected", "cut"), rows)
    lines += ["", "## Candidate detectors (rows in candidates.tsv, sentences in the union, selected, cut)", ""]
    chosen_keys = {e.key for e in selected}
    rows = []
    for name in sorted(inputs.class_rank, key=lambda n: inputs.class_rank[n]):
        sentences = {key for key, items in inputs.by_sentence.items() if any(r.candidate_rule == name for r in items)}
        count = sum(1 for r in inputs.candidates if r.candidate_rule == name)
        rows.append((name, count, len(sentences), len(sentences & chosen_keys), len(sentences - chosen_keys),
                     "P1" if name in NV_DETECTORS else "P3"))
    lines += _table(("detector", "rows", "sentences", "selected", "cut", "band when alone"), rows)
    lines += ["", "## Voter flags of the open splits (union before the cap; tokens with a mechanical row do not count)", ""]
    rows = []
    for split in corrections.SOURCES["open"]:
        members = [e for e in pool.values() if e.key[:2] == ("open", split)]
        rows.append((split, sum(1 for e in members if e.unanimous), sum(e.unanimous for e in members), sum(1 for e in members if e.split),
                     sum(e.split for e in members), sum(e.split_major for e in members)))
    lines += _table(("split", "sentences with a unanimous-against token", "unanimous-against tokens", "sentences with a split-flag token",
                     "split-flag tokens", "of which split-major"), rows)
    return lines


def selection_rows(selected):
    """Rows of private/selection.tsv: the selected sentences in band and priority order."""
    return [(rank, e.band, e.key[0], e.key[1], e.key[2], e.ntokens) for rank, e in enumerate(selected, 1)]


def read_selection(work):
    """[(key, band, ntokens)] of private/selection.tsv in the order written."""
    _, records = read_records(work.private_file("selection.tsv"))
    return [((r["source"], r["split"], r["sent_id"]), r["band"], int(r["ntokens"])) for r in records]


# ---------------------------------------------------------------- decision tokens

DecisionToken = collections.namedtuple("DecisionToken", "index classes primary")


def dependents(detector, index, s):
    """Indices of the tokens whose tag decides a candidate token with the same sentence `s` (a `convention_rules.Sentence`)."""
    if detector in ("prenominal-ing", "prenominal-ed", "modal-aux-other"):
        return [index + 1]
    if detector in ("be-participle-gradable", "be-participle-adj-not-gradable", "be-passive-agent"):
        return [k for j, k, kind, degree, agent in convention_rules._participle_contexts(s) if j == index]
    if detector == "be-aux-passive":
        return [j for j, k, kind, degree, agent in convention_rules._participle_contexts(s) if k == index]
    if detector == "zero-derived-degree":
        return [index - 1]
    if detector == "out-of-after-verb":
        return [index + 1, index - 1]
    return []


def decision_tokens(inputs, key, flags):
    """The tokens of a selected sentence that the taggers decide: {index: DecisionToken}.

    The union of the candidate tokens (every token of the span of a `candidates.tsv` row), the voter-flag tokens (open only:
    unanimous-against and split-flag tokens) and the dependents of the candidates (the tokens whose tag is what makes the
    candidate token a noun, adjective or verb); masked tokens are never decided. Tokens with a mechanical row are included.
    The primary class is the rarest candidate detector, else `voter-unanimous`, `voter-split`, `dependent:<detector>`.
    """
    sentence = inputs.sentence(key)
    n = len(sentence["rows"])
    s = convention_rules.Sentence(key[0], key[1], sentence)
    classes = collections.defaultdict(list)
    for row in inputs.by_sentence.get(key, ()):
        index, span = int(row.token_index), int(row.span_length)
        for position in range(index, index + span):
            classes[position].append(row.candidate_rule)
        for other in dependents(row.candidate_rule, index, s):
            classes[other].append("dependent:" + row.candidate_rule)
    for index, item in flags.get((key[1], key[2]), {}).items() if key[0] == "open" else ():
        kind = flag_kind(item)
        if kind:
            classes[index].append("voter-" + kind)
    rank = inputs.class_rank
    found = {}
    for index, names in classes.items():
        if not 0 <= index < n or sentence["rows"][index][1] == "_":
            continue

        def order(name):
            if name in rank:
                return (0, rank[name], name)
            if name == "voter-unanimous":
                return (1, 0, name)
            if name == "voter-split":
                return (2, 0, name)
            return (3, rank.get(name[len("dependent:"):], 0), name)

        ordered = tuple(sorted(set(names), key=order))
        found[index] = DecisionToken(index, ordered, ordered[0])
    return found


# ---------------------------------------------------------------- batches

def assign_ids(keys, seed=SEED):
    """{key: opaque id}: `s` and six hex digits from a seeded permutation, unrelated to the sentence id, the order or the batch."""
    keys = sorted(keys)
    numbers = random.Random(seed).sample(range(1 << 24), len(keys))
    return {key: "s%06x" % number for key, number in zip(keys, numbers)}


Batch = collections.namedtuple("Batch", "name band keys")


def make_batches(selected, ntokens, seed=SEED, size=BATCH_SENTENCES, budget=BATCH_ROWS):
    """[Batch] in band order: each band shuffled with the seed, then cut into batches of at most `size` sentences and `budget` token rows.

    A batch holds one band. `ntokens` is {key: token rows}; a sentence longer than the budget gets a batch of its own.
    """
    rng = random.Random(seed)
    batches = []
    for band in BANDS:
        keys = [e.key for e in selected if e.band == band]
        rng.shuffle(keys)
        current, rows = [], 0
        for key in keys:
            if current and (len(current) >= size or rows + ntokens[key] > budget):
                batches.append(Batch("b%03d" % (len(batches) + 1), band, current))
                current, rows = [], 0
            current.append(key)
            rows += ntokens[key]
        if current:
            batches.append(Batch("b%03d" % (len(batches) + 1), band, current))
    return batches


def batch_rows(batch, ids, inputs):
    """The rows of a batch file: (opaque id, token index, escaped token) for every token of every sentence, in order."""
    rows = []
    for key in batch.keys:
        for index, cells in enumerate(inputs.sentence(key)["rows"]):
            rows.append((ids[key], str(index), escape_cell(cells[0])))
    return rows


def write_batch(path, rows):
    """Write a batch file. The cells are already escaped, so they are written as they are (escape is idempotent on them only through this helper)."""
    os.makedirs(os.path.dirname(path), exist_ok=True)
    with open(path, "w", encoding="utf-8", newline="") as handle:
        writer = _writer(handle)
        writer.writerow(BATCH_HEADER)
        writer.writerows(rows)


def key_rows(batches, ids, inputs, flags):
    """Rows of private/key.tsv: the opaque id and everything the blind batch does not say."""
    rows = []
    for batch in batches:
        for key in batch.keys:
            tokens = decision_tokens(inputs, key, flags)
            candidate = sorted(i for i, t in tokens.items() if any(c in inputs.class_rank for c in t.classes))
            voter = sorted(i for i, t in tokens.items() if any(c.startswith("voter-") for c in t.classes))
            dependent = sorted(i for i, t in tokens.items() if any(c.startswith("dependent:") for c in t.classes))
            sentence = inputs.sentence(key)
            rows.append((ids[key], batch.name, key[0], key[1], key[2], sentence["source_id"], batch.band, len(sentence["rows"]),
                         ",".join(map(str, candidate)), ",".join(map(str, voter)), ",".join(map(str, dependent))))
    return rows


KeyRow = collections.namedtuple("KeyRow", KEY_COLUMNS)


def read_key(work):
    """[KeyRow] of private/key.tsv."""
    columns, records = read_records(work.private_file("key.tsv"))
    if tuple(columns) != KEY_COLUMNS:
        raise ValueError("private/key.tsv: unexpected columns %r" % columns)
    return [KeyRow(*(record[name] for name in KEY_COLUMNS)) for record in records]


def check_batches(work, inputs, budget=BATCH_ROWS):
    """Errors of the exported batches against the key and the corpora; an empty list when the export is sound and blind."""
    errors = []
    key = read_key(work)
    by_batch = collections.defaultdict(list)
    for row in key:
        by_batch[row.batch].append(row)
    seen_ids = set()
    files = sorted(name for name in os.listdir(work.batches) if name.endswith(".tsv") and BATCH_RE.match(name[:-4]))
    if [name[:-4] for name in files] != sorted(by_batch):
        errors.append("the batch files and the batches of the key differ")
    for name in files:
        batch = name[:-4]
        path = work.batch_file(batch)
        try:
            rows = read_batch(path)
        except ValueError as error:
            errors.append(str(error))
            continue
        if len(rows) > budget:
            errors.append("%s: %d rows exceed the budget %d" % (batch, len(rows), budget))
        expected = []
        for item in by_batch.get(batch, ()):
            sentence = inputs.sentence((item.source, item.split, item.sent_id))
            expected.extend((item.opaque_id, str(index), escape_cell(cells[0])) for index, cells in enumerate(sentence["rows"]))
            if item.opaque_id in seen_ids:
                errors.append("%s: id %s is used twice" % (batch, item.opaque_id))
            seen_ids.add(item.opaque_id)
        if any(not ID_RE.match(row[0]) for row in rows):
            errors.append("%s: an id is not an opaque id" % batch)
        if rows != expected:
            first = next((i for i, (a, b) in enumerate(zip(rows, expected)) if a != b), min(len(rows), len(expected)))
            errors.append("%s: the rows differ from the key and the corpus (%d rows, expected %d, first difference at row %d)"
                          % (batch, len(rows), len(expected), first + 1))
    return errors


# ---------------------------------------------------------------- tagger outputs

def output_files(directory, batch):
    """(files, errors): the output of a batch is `<batch>.tsv` or the parts `<batch>.p01.tsv`, `<batch>.p02.tsv`, ... (not both, no gap)."""
    if not os.path.isdir(directory):
        return [], []
    whole = batch + ".tsv"
    parts = sorted(name for name in os.listdir(directory) if re.fullmatch(re.escape(batch) + r"\.p[0-9]{2}\.tsv", name))
    present = os.path.exists(os.path.join(directory, whole))
    if present and parts:
        return [], ["both %s and parts exist" % whole]
    if present:
        return [os.path.join(directory, whole)], []
    if not parts:
        return [], []
    numbers = [int(name[len(batch) + 2:len(batch) + 4]) for name in parts]
    if numbers != list(range(1, len(parts) + 1)):
        return [], ["the parts %s are not numbered 01, 02, ... without a gap" % ", ".join(parts)]
    return [os.path.join(directory, name) for name in parts], []


def load_output(directory, batch):
    """(rows, errors): the data rows (lists of 6 raw cells) of the output files of a batch; `rows` is None when nothing was written."""
    files, errors = output_files(directory, batch)
    if errors:
        return None, errors
    if not files:
        return None, []
    rows = []
    for path in files:
        try:
            raw = read_raw_rows(path)
        except (ValueError, csv.Error, UnicodeDecodeError) as error:
            return None, ["%s: %s" % (os.path.basename(path), error)]
        if tuple(raw[0]) != OUTPUT_HEADER:
            return None, ["%s: header is %r, expected %r" % (os.path.basename(path), tuple(raw[0]), OUTPUT_HEADER)]
        rows.extend(raw[1:])
    return rows, []


def validate_output_rows(expected, rows, limit=10):
    """Errors of an output against its batch (`expected` rows of 3 cells): same rows in the same order, valid tag, confidence, flags."""
    errors = []

    def note(message):
        if len(errors) < limit:
            errors.append(message)

    if len(rows) != len(expected):
        note("%d rows, the batch has %d (%s)" % (len(rows), len(expected), "rows are missing" if len(rows) < len(expected) else "extra rows"))
    for number, (want, got) in enumerate(zip(expected, rows), 1):
        if len(got) != 6:
            note("row %d: %d cells, expected 6" % (number, len(got)))
            continue
        if tuple(got[:3]) != tuple(want):
            note("row %d: (id, token_index, token) %r is not the batch row %r" % (number, tuple(got[:3]), tuple(want)))
            continue
        if got[3] not in common.UPOS_SET:
            note("row %d: tag %r is not one of the 17 tags" % (number, got[3]))
        if got[4] not in CONFIDENCES:
            note("row %d: confidence %r is not high, medium or low" % (number, got[4]))
        if got[5] not in FLAG_CELLS:
            note("row %d: flags %r is not -, mentioned, foreign or foreign+mentioned" % (number, got[5]))
    return errors


def check_output(work, model, batch):
    """(rows, state, errors): `state` is `ok`, `invalid` or `missing`; `rows` the output rows when ok."""
    expected = read_batch(work.batch_file(batch))
    rows, errors = load_output(work.out(model), batch)
    if errors:
        return None, "invalid", errors
    if rows is None:
        return None, "missing", []
    errors = validate_output_rows(expected, rows)
    return (rows, "ok", []) if not errors else (None, "invalid", errors)


def read_status(work):
    """{batch: {column: value}} of batches/status.tsv, in file order."""
    path = os.path.join(work.batches, "status.tsv")
    if not os.path.exists(path):
        return collections.OrderedDict()
    _, records = read_records(path)
    return collections.OrderedDict((record["batch"], record) for record in records)


def write_status(work, status):
    write_rows(os.path.join(work.batches, "status.tsv"), STATUS_COLUMNS, [[record[c] for c in STATUS_COLUMNS] for record in status.values()])


def new_status_record(batch):
    return {"batch": batch, "opus_state": "pending", "sol_state": "pending", "attempts_opus": "0", "attempts_sol": "0", "note": ""}


def validate_and_record(work, model, batch, count=True):
    """Validate one output, record the state in status.tsv and return (state, errors).

    `attempts_<model>` counts the validations of an existing output (call once per tagger run; `count=False` re-checks). The retry
    limit is `MAX_ATTEMPTS`: an invalid output is asked for again once; the invalid output of the last allowed attempt makes the state
    `skipped` (the batch is removed, its sentences are not decided). A `skipped` state is final: no further attempt is recorded,
    the state and the counter stay, and the call returns ("skipped", [message]) without looking at the output.
    """
    status = read_status(work)
    record = status.setdefault(batch, new_status_record(batch))
    if record[model + "_state"] == "skipped":
        return "skipped", ["the attempt limit of %d is reached for %s %s: the batch is skipped, no further attempt is recorded"
                           % (MAX_ATTEMPTS, batch, model)]
    _, state, errors = check_output(work, model, batch)
    if count and state != "missing":
        attempts = int(record["attempts_" + model])
        if attempts < MAX_ATTEMPTS:                      # the counter never passes the limit
            attempts += 1
            record["attempts_" + model] = str(attempts)
        if state == "invalid" and attempts >= MAX_ATTEMPTS:
            state = "skipped"
            errors = list(errors) + ["the attempt limit of %d is reached: the batch is skipped" % MAX_ATTEMPTS]
    record[model + "_state"] = state
    notes = [item for item in record["note"].split(" | ") if item and not item.startswith(model + ":")]
    if errors:
        notes.append(model + ": " + "; ".join(errors[:3]).replace("\t", " "))
    record["note"] = " | ".join(notes)
    write_status(work, status)
    return state, errors


# ---------------------------------------------------------------- the decision rule

Vote = collections.namedtuple("Vote", "tag confidence flags")
Decision = collections.namedtuple("Decision", "outcome new_tag new_status kind note")


def vote_of(cells):
    """`Vote` of the raw cells (tag, confidence, flags) of an output row."""
    return Vote(cells[3], cells[4], FLAG_CELLS[cells[5]])


def effective_flags(current, opus, sol):
    """The flags a row sets: per flag the value both models agree on, else the current value."""
    return tuple(flag for flag in common.STATUS_FLAGS
                 if ((flag in opus.flags) if (flag in opus.flags) == (flag in sol.flags) else (flag in current)))


def decide_token(tag, status, opus, sol, primary, mech=None):
    """Decision of one token from the two blind votes: `Decision(outcome, new_tag, new_status, kind, note)`.

    `tag` and `status` are the corpus values, `primary` the primary class and `mech` the mechanical row of the token or None.
    `kind` is `tag`, `flags` or `disputed` when the outcome stages a row, else None.

    Without a mechanical row: the models agree on another tag -> `correction` (flags per `effective_flags`, the base stays); they agree
    on the tag -> `flags` when the agreed flags differ from the current ones, `disputed-flag` when they disagree about a flag and the
    class is a flag detector, else `no-change`; they disagree -> `disputed` (the base becomes disputed; no row for a token that is
    already disputed). With a mechanical row (tag M, the corpus tag P): both models give T. T = M -> no row (`agree-rule`), or
    `replace-flags` when the flags change; T is neither M nor P -> `replace` (the row P -> T replaces the mechanical row); T = P ->
    `dissent-pristine`, nothing is staged (the rule stays, the case is reported); the models disagree -> `rule-kept`, nothing is staged.
    """
    base, current = common.parse_status(status)
    flags = effective_flags(current, opus, sol)
    new_status = common.format_status(base, flags)
    if mech is not None:
        if opus.tag != sol.tag:
            return Decision("rule-kept", tag, status, None, "the models disagree; the rule row stays")
        found = opus.tag
        if found == mech.new_tag:
            if new_status != status:
                return Decision("replace-flags", found, new_status, "tag", "agrees with the rule tag, the flags change")
            return Decision("agree-rule", tag, status, None, "agrees with the rule")
        if found == tag:
            return Decision("dissent-pristine", tag, status, None, "both models keep the corpus tag against the rule")
        return Decision("replace", found, new_status, "tag", "replaces the mechanical row")
    if opus.tag == sol.tag:
        if opus.tag != tag:
            return Decision("correction", opus.tag, new_status, "tag", "")
        if new_status != status:
            return Decision("flags", tag, new_status, "flags", "")
        if (opus.flags != sol.flags) and primary in FLAG_DETECTORS:
            if base == "disputed":
                return Decision("already-disputed", tag, status, None, "")
            return Decision("disputed-flag", tag, common.format_status("disputed", current), "disputed", "")
        return Decision("no-change", tag, status, None, "")
    if base == "disputed":
        return Decision("already-disputed", tag, status, None, "")
    return Decision("disputed", tag, common.format_status("disputed", current), "disputed", "")


# ---------------------------------------------------------------- decide

def read_outputs_by_id(work, batch):
    """{model: {opaque_id: {token_index: Vote}}} of a complete batch, or None when a model's output is missing or invalid."""
    result = {}
    for model in MODELS:
        rows, state, _ = check_output(work, model, batch)
        if state != "ok":
            return None
        votes = collections.defaultdict(dict)
        for cells in rows:
            votes[cells[0]][int(cells[1])] = vote_of(cells)
        result[model] = votes
    return result


def context_of(sentence, index, size=6):
    """A short readable window of a sentence with the target in brackets."""
    words = [cells[0] for cells in sentence["rows"]]
    return " ".join(words[max(0, index - size):index] + ["[" + words[index] + "]"] + words[index + 1:index + size + 1])


def run_decisions(work, inputs, flags, drop=frozenset()):
    """All decisions of the complete batches: (records, rows, others, incomplete).

    `records` are the dicts of private/decisions.tsv (one per decision token), `rows` the staged `Correction` rows in the order of
    the records, `others` the tokens outside the decision set on which both models agree against the corpus tag, `incomplete` the
    [(batch, reason)] of the batches that are not used. The class drop is applied to the staged rows (`final` = dropped-class).
    """
    key = read_key(work)
    status = read_status(work)
    by_batch = collections.defaultdict(list)
    for item in key:
        by_batch[item.batch].append(item)
    records, rows, others, incomplete = [], [], [], []
    for batch in sorted(by_batch):
        skipped = [model for model in MODELS if status.get(batch, {}).get(model + "_state") == "skipped"]
        if skipped:
            incomplete.append((batch, "skipped after %d attempts (%s)" % (MAX_ATTEMPTS, ", ".join(skipped))))
            continue
        outputs = read_outputs_by_id(work, batch)
        if outputs is None:
            incomplete.append((batch, "an output is missing or invalid"))
            continue
        for item in by_batch[batch]:
            skey = (item.source, item.split, item.sent_id)
            sentence = inputs.sentence(skey)
            tokens = decision_tokens(inputs, skey, flags)
            for index, cells in enumerate(sentence["rows"]):
                opus, sol = outputs["opus"][item.opaque_id].get(index), outputs["sol"][item.opaque_id].get(index)
                if opus is None or sol is None:
                    raise ValueError("batch %s: no vote for %s#%d" % (batch, item.opaque_id, index))
                if index not in tokens:
                    if cells[1] != "_" and opus.tag == sol.tag and opus.tag != cells[1]:
                        others.append((item.source, item.split, item.sent_id, index, cells[0], cells[1], opus.tag, item.band, batch))
                    continue
                decision_token = tokens[index]
                mech = inputs.mech_by_address.get((item.source, item.split, item.sent_id, index))
                verdict = decide_token(cells[1], cells[2], opus, sol, decision_token.primary, mech)
                voter = flags.get((item.split, item.sent_id), {}).get(index) if item.source == "open" else None
                final = ""
                row = None
                if verdict.kind:
                    row = corrections.Correction(item.source, item.split, item.source_id, item.sent_id, str(index), cells[0], cells[1],
                                                 verdict.new_tag, cells[2], verdict.new_status, "llm:opus+sol:" + batch)
                    final = "staged"
                    if verdict.kind in KINDS_REVIEWED and decision_token.primary in drop:
                        final = "dropped-class"
                records.append({
                    "source": item.source, "split": item.split, "source_id": item.source_id, "sent_id": item.sent_id,
                    "token_index": str(index), "token": cells[0], "class": decision_token.primary,
                    "classes": ",".join(decision_token.classes), "band": item.band, "batch": batch,
                    "current_tag": cells[1], "current_status": cells[2], "mech_rule": mech.provenance[len("rule:"):] if mech else "",
                    "mech_new_tag": mech.new_tag if mech else "", "opus_tag": opus.tag, "opus_conf": opus.confidence,
                    "opus_flags": "+".join(opus.flags) or "-", "sol_tag": sol.tag, "sol_conf": sol.confidence,
                    "sol_flags": "+".join(sol.flags) or "-", "voters": "/".join(voter.voters) if voter else "",
                    "outcome": verdict.outcome, "row_kind": verdict.kind or "", "new_tag": verdict.new_tag,
                    "new_status": verdict.new_status, "final": final})
                rows.append(row)
    return records, rows, others, incomplete


def staged_rows(records, rows):
    """The `Correction` rows whose record is staged (not dropped), in the order of the records."""
    return [row for record, row in zip(records, rows) if row is not None and record["final"] == "staged"]


def find_fix_conflicts(data_dir, effective, touched, expected=frozenset()):
    """([(key, rule, index, token)], [same]): fix-tier rule hits in the touched sentences after the effective rows are applied in memory.

    The first list holds the real conflicts; the second the `expected` hits, (key, index, rule) of the mechanical rows that an
    adjudicated row replaces (the rule fires again on the replaced token by construction, see `check_conventions.superseded_hits`).
    """
    corpora = corrections.load_corpora(data_dir)
    corrections.apply_in_memory(corpora, effective)
    sentences = [convention_rules.Sentence(source, split, sentence) for (source, split), corpus in corpora.items()
                 for sentence in corpus.sentences]
    resources = convention_rules.build_resources(sentences)
    found, residue = [], []
    for s in sentences:
        if (s.source, s.split, s.sent_id) in touched:
            for proposal in convention_rules.evaluate(s, resources, (convention_rules.FIX,)):
                hit = ((s.source, s.split, s.sent_id), proposal.rule, proposal.index, s.f[proposal.index])
                key = ((s.source, s.split, s.sent_id), proposal.index, proposal.rule)
                (residue if key in expected else found).append(hit)
    return found, residue


def drop_fix_conflicts(records, rows, mechanical, data_dir, passes=4):
    """Drop the tag rows of every sentence where a fix-tier rule fires on the corrected corpus; returns (conflicts, replaced rule hits).

    A hit of the rule of a replaced mechanical row on the replaced token is expected and only counted (the adjudication outranks
    the rule); every other hit is a conflict and all tag rows of its sentence are dropped (`final` = fix-conflict), then the check is
    repeated until it is clean.
    """
    log, residue = [], []
    for _ in range(passes):
        staged = staged_rows(records, rows)
        effective, superseded, _ = corrections.combine_tables([(corrections.MECHANICAL_TABLE, mechanical),
                                                               (corrections.ADJUDICATED_TABLE, staged)])
        touched = {(row.source, row.split, row.sent_id) for row in staged if row.old_tag != row.new_tag}
        expected = {((row.source, row.split, row.sent_id), int(row.token_index), row.provenance[len("rule:"):]) for row in superseded}
        conflicts, residue = find_fix_conflicts(data_dir, effective, touched, expected)
        if not conflicts:
            return log, residue
        log.extend(conflicts)
        bad = {item[0] for item in conflicts}
        for record, row in zip(records, rows):
            if row is not None and record["final"] == "staged" and row.old_tag != row.new_tag \
                    and (record["source"], record["split"], record["sent_id"]) in bad:
                record["final"] = "fix-conflict"
    raise RuntimeError("fix-tier conflicts remain after %d passes" % passes)


def table_lines(rows, corpora, data_dir=None):
    """The lines of adjudicated.tsv: the comment header (with the corpus hashes), the columns line and the sorted rows."""
    header = [make_corrections.CORPUS_LINE % "adjudicated corrections (two blind taggers), not yet applied", make_corrections.LICENSE_LINE,
              "# kind = corrections", "# columns = " + ", ".join(corrections.COLUMNS["corrections"]),
              "# generator = tools/data/adjudicate.py (decisions of two blind taggers; see data/corrections/README.md)"]
    header += make_corrections.input_lines(corpora, data_dir)
    return corrections.format_table("corrections", header, rows)


def table_problems(lines, mechanical, data_dir=None):
    """Everything wrong with an adjudicated table about to be written: format, rows, uniqueness, supersession, corpora, findings."""
    header, rows = corrections.parse_table("corrections", lines, "adjudicated.tsv")
    problems = corrections.table_errors(rows)
    effective, superseded, across = corrections.combine_tables([(corrections.MECHANICAL_TABLE, mechanical),
                                                                (corrections.ADJUDICATED_TABLE, rows)])
    problems += across
    errors, state = corrections.validate_against_corpora([r for r in effective if corrections.row_error(r) is None],
                                                         corrections.load_corpora(data_dir))
    problems += errors
    if rows and state != "pending":
        problems.append("the corrections are in the %s state, expected pending" % state)
    problems += corrections.adjudicated_findings(data_dir, rows, "pending", header)
    return problems


def count_lines(records, others, mechanical_dissent, incomplete, conflicts, dropped, inputs, residue=0):
    """Markdown lines with the counts of a decision run: agreement per class, outcomes per source and split, and the side lists."""
    lines = ["# Decision report", ""]
    by_class = collections.defaultdict(lambda: collections.Counter())
    for record in records:
        counter = by_class[record["class"]]
        counter["tokens"] += 1
        counter["agree"] += record["opus_tag"] == record["sol_tag"]
        counter[record["outcome"]] += 1
        counter["staged-" + (record["row_kind"] or "none")] += record["final"] == "staged"
        voters = record["voters"].split("/") if record["voters"] else []
        if record["row_kind"] == "tag" and record["final"] == "staged" and len(voters) == 4 and len(set(voters)) == 1:
            counter["voter-match"] += voters[0] == record["new_tag"]
            counter["voter-unanimous"] += 1
    lines += ["## Agreement of the two models per class (a token counts as agreed when both give the same tag)", ""]
    rows = []
    for name in sorted(by_class, key=lambda n: (-by_class[n]["tokens"], n)):
        c = by_class[name]
        rows.append((name, c["tokens"], "%.1f%%" % (100.0 * c["agree"] / c["tokens"]), c["correction"] + c["replace"], c["flags"] + c["replace-flags"],
                     c["disputed"] + c["disputed-flag"], c["no-change"] + c["agree-rule"], c["rule-kept"] + c["dissent-pristine"],
                     "%d/%d" % (c["voter-match"], c["voter-unanimous"])))
    lines += _table(("class", "decision tokens", "tag agreement", "corrections", "flag rows", "disputed marks", "no change", "rule kept",
                     "new tag = unanimous voter tag"), rows)
    lines += ["", "## Outcomes per source and split", ""]
    per_source = collections.defaultdict(collections.Counter)
    for record in records:
        c = per_source[(record["source"], record["split"])]
        c["tokens"] += 1
        c["corrections"] += record["row_kind"] == "tag"
        c["flag rows"] += record["row_kind"] == "flags"
        c["disputed"] += record["row_kind"] == "disputed"
        c["no change"] += record["outcome"] in ("no-change", "agree-rule", "already-disputed")
        c["rule kept or dissent"] += record["outcome"] in ("rule-kept", "dissent-pristine")
        c["staged"] += record["final"] == "staged"
    heads = ("tokens", "corrections", "flag rows", "disputed", "no change", "rule kept or dissent", "staged")
    lines += _table(("source",) + heads, [(_label(k),) + tuple(per_source[k][h] for h in heads) for k in sorted(per_source)])
    outcomes = collections.Counter(record["outcome"] for record in records)
    lines += ["", "## Outcomes", ""] + _table(("outcome", "tokens"), sorted(outcomes.items()))
    lines += ["", "## Mechanical rows met by the decision (the rule row is replaced only by an agreed different tag)", ""]
    per_rule = collections.defaultdict(collections.Counter)
    for item in mechanical_dissent:
        per_rule[item["mech_rule"]][item["outcome"]] += 1
    kinds = ("replace", "replace-flags", "dissent-pristine", "rule-kept", "agree-rule")
    lines += _table(("rule",) + kinds, [(rule,) + tuple(per_rule[rule][k] for k in kinds) for rule in sorted(per_rule)])
    lines += ["", "## Side lists", "",
              "- tokens outside the decision set on which both models agree against the label (information only): %d" % len(others),
              "- batches not used (an output is missing or invalid): %d" % len(incomplete),
              "- tag rows dropped by the fix-tier conflict filter: %d (conflict hits: %d)" % (
                  sum(1 for r in records if r["final"] == "fix-conflict"), len(conflicts)),
              "- hits of a replaced mechanical row's own rule on the replaced token (expected, not conflicts): %d" % residue,
              "- classes dropped by the gate: %s" % (", ".join(sorted(dropped)) or "none")]
    return lines


def run_decide(work, data_dir, drop, out_path):
    """The `decide` command: write adjudicated.tsv and the private lists; returns (exit code, printed lines)."""
    inputs = load_inputs(data_dir)
    flags = read_flags(work.flags, inputs.corpora)
    records, rows, others, incomplete = run_decisions(work, inputs, flags, drop)
    log, residue = drop_fix_conflicts(records, rows, inputs.mechanical, data_dir)
    staged = staged_rows(records, rows)
    lines = table_lines(staged, inputs.corpora, data_dir)
    problems = table_problems(lines, inputs.mechanical, data_dir)
    if problems:
        return 1, ["refusing to write the table: %d problems, first: %s" % (len(problems), problems[0])]
    common.write_text(out_path, lines)
    write_rows(work.private_file("decisions.tsv"), DECISION_COLUMNS, [[r[c] for c in DECISION_COLUMNS] for r in records])
    write_rows(work.private_file("outside-agreed.tsv"), ("source", "split", "sent_id", "token_index", "token", "label", "both_models", "band",
                                                         "batch"), others)
    dissent = [r for r in records if r["mech_rule"]]
    write_rows(work.private_file("mechanical-dissent.tsv"),
               ("source", "split", "sent_id", "token_index", "token", "mech_rule", "pristine_tag", "mech_new_tag", "opus_tag", "sol_tag",
                "outcome", "staged"),
               [(r["source"], r["split"], r["sent_id"], r["token_index"], r["token"], r["mech_rule"], r["current_tag"], r["mech_new_tag"],
                 r["opus_tag"], r["sol_tag"], r["outcome"], "yes" if r["final"] == "staged" else "no") for r in dissent
                if r["outcome"] not in ("agree-rule",)])
    write_rows(work.private_file("fix-conflicts.tsv"), ("source", "split", "sent_id", "rule", "token_index", "token"),
               [(k[0], k[1], k[2], rule, index, token) for k, rule, index, token in log])
    write_rows(work.private_file("incomplete-batches.tsv"), ("batch", "reason"), incomplete)
    write_rows(work.private_file("superseded-rule-hits.tsv"), ("source", "split", "sent_id", "rule", "token_index", "token"),
               [(k[0], k[1], k[2], rule, index, token) for k, rule, index, token in residue])
    disputed = [r for r in records if r["row_kind"] == "disputed" and r["final"] == "staged"]
    write_rows(os.path.join(work.root, "disputed.tsv"),
               ("source", "split", "sent_id", "token_index", "token", "current_tag", "tag_opus", "tag_sol", "class", "batch", "context"),
               [(r["source"], r["split"], r["sent_id"], r["token_index"], r["token"], r["current_tag"], r["opus_tag"], r["sol_tag"], r["class"],
                 r["batch"], context_of(inputs.sentence((r["source"], r["split"], r["sent_id"])), int(r["token_index"]))) for r in disputed])
    report = count_lines(records, others, dissent, incomplete, log, drop, inputs, len(residue))
    common.write_text(work.private_file("decide-report.md"), report)
    digest = common.sha256_file(work.private_file("decisions.tsv"))
    return 0, report + ["", "wrote %s (%d rows), decisions.tsv sha256 %s" % (out_path, len(staged), digest)]


# ---------------------------------------------------------------- the review sample and the precision gate

SampleItem = collections.namedtuple("SampleItem", "record kind random60")


def reviewable(records):
    """The staged tag and flag rows (the precision gate looks at these; a disputed mark is not a correction)."""
    return [r for r in records if r["final"] == "staged" and r["row_kind"] in KINDS_REVIEWED]


def build_sample(records, seed=SEED, n_random=60, per_class=8, min_total=150, n_controls=8, only=None, exclude=frozenset()):
    """[SampleItem] in a shuffled order: a seeded random subset, a per-class top-up, a random fill and the control items.

    The random subset (`n_random` rows, flagged `random60`) is drawn over all reviewable rows; every class then gets rows until it has
    `per_class` of them (all of a smaller class), the total is filled up to `min_total` at random, and `n_controls` control items
    (a real tag correction with its current and proposed value swapped, so the right verdict is `incorrect`) are drawn from rows
    that are not in the sample. `only` restricts the classes, `exclude` is a set of (sent_id, token_index) already reviewed.
    """
    rng = random.Random(seed)

    def ident(record):
        return (record["sent_id"], record["token_index"])

    pool = sorted((r for r in reviewable(records) if ident(r) not in exclude and (only is None or r["class"] in only)),
                  key=lambda r: (r["source"], r["split"], r["sent_id"].encode("utf-8"), int(r["token_index"])))
    chosen = rng.sample(pool, min(n_random, len(pool))) if n_random else []
    flagged = {id(r) for r in chosen}
    picked = {id(r) for r in chosen}
    by_class = collections.defaultdict(list)
    for record in pool:
        by_class[record["class"]].append(record)
    for name in sorted(by_class):
        members = by_class[name]
        need = min(per_class, len(members)) - sum(1 for r in members if id(r) in picked)
        free = [r for r in members if id(r) not in picked]
        for record in rng.sample(free, max(0, min(need, len(free)))):
            chosen.append(record)
            picked.add(id(record))
    if len(chosen) < min_total:
        free = [r for r in pool if id(r) not in picked]
        for record in rng.sample(free, min(min_total - len(chosen), len(free))):
            chosen.append(record)
            picked.add(id(record))
    items = [SampleItem(r, "real", id(r) in flagged) for r in chosen]
    controls = [r for r in pool if id(r) not in picked and r["row_kind"] == "tag"]
    items += [SampleItem(r, "control", False) for r in rng.sample(controls, min(n_controls, len(controls)))]
    rng.shuffle(items)
    return items


def shown_values(item):
    """(current, proposed) as the reviewer sees them: `TAG (status)`; a control item swaps the two."""
    record = item.record
    current = "%s (%s)" % (record["current_tag"], record["current_status"])
    proposed = "%s (%s)" % (record["new_tag"], record["new_status"])
    return (proposed, current) if item.kind == "control" else (current, proposed)


REVIEW_INSTRUCTIONS = [
    "# Review items", "",
    "Each item shows one token of a sentence with the tag and status it has now and a proposed tag and status. Judge ONLY whether the",
    "proposed value is correct according to data/CONVENTIONS.md (read it in full first). The tags shown for the other tokens of the",
    "sentence are the current ones and are not under review. The status is `base[+flag]`: a flag `mentioned` or `foreign` never changes",
    "the tag; the base (`unanimous`, `unchecked`, `disputed`, `ptb`, `silver`) is not under review.", "",
    "Answer with one line per item, tab separated, and nothing else: `item<TAB>verdict<TAB>cite` where the verdict is `correct` (the",
    "proposed value is right), `incorrect` (it is wrong) or `unclear` (the conventions do not decide it), and the cite names the rule of",
    "CONVENTIONS you applied in at most 20 words. Do not look at any other file and do not search for the sentences.", "",
]


def sample_lines(items, inputs, population, prefix="R"):
    """(markdown lines for the reviewer, key rows): the blocks show the sentence, the target, the current and the proposed value only.

    `population` is {class: rows staged in that class}, recorded in the (private) key for the weighted overall figure.
    """
    lines = list(REVIEW_INSTRUCTIONS)
    keys = []
    for number, item in enumerate(items, 1):
        record = item.record
        name = "%s%03d" % (prefix, number)
        sentence = inputs.sentence((record["source"], record["split"], record["sent_id"]))
        index = int(record["token_index"])
        current, proposed = shown_values(item)
        words = []
        for position, cells in enumerate(sentence["rows"]):
            words.append(("[[%s/%s]]" if position == index else "%s/%s") % (cells[0], current.split(" ")[0] if position == index else cells[1]))
        lines += ["## " + name, "", "Sentence: " + " ".join(words), "Token: %s (index %d)" % (record["token"], index),
                  "Current: " + current, "Proposed: " + proposed, ""]
        keys.append((name, item.kind, "yes" if item.random60 else "no", record["source"], record["split"], record["sent_id"],
                     record["token_index"], record["token"], record["class"], record["batch"], population[record["class"]], current, proposed))
    return lines, keys


Verdict = collections.namedtuple("Verdict", "item verdict cite")


def parse_verdicts(text):
    """[Verdict] from the reviewer's answer: lines `R017<TAB>correct<TAB>cite` (other lines are ignored; a repeated item is an error)."""
    found, seen = [], set()
    for line in text.replace("\r\n", "\n").split("\n"):
        cells = [cell.strip() for cell in line.strip().strip("`").split("\t")]
        if len(cells) >= 2 and re.fullmatch(r"[A-Z][0-9]{3,}", cells[0]) and cells[1].lower() in VERDICTS:
            if cells[0] in seen:
                raise ValueError("item %s is answered twice" % cells[0])
            seen.add(cells[0])
            found.append(Verdict(cells[0], cells[1].lower(), cells[2] if len(cells) > 2 else ""))
    return found


def class_statistics(keys, verdicts):
    """{class: Counter(correct, incorrect, unclear, total, population)} over the reviewed real items, plus the controls and random-60 counters."""
    answers = {v.item: v.verdict for v in verdicts}
    per_class = collections.defaultdict(collections.Counter)
    controls, random60 = collections.Counter(), collections.Counter()
    for row in keys:
        verdict = answers.get(row["item"])
        if verdict is None:
            continue
        if row["kind"] == "control":
            controls[verdict] += 1
            continue
        counter = per_class[row["class"]]
        counter[verdict] += 1
        counter["total"] += 1
        counter["population"] = int(row["class_total"])
        if row["random60"] == "yes":
            random60[verdict] += 1
    return per_class, controls, random60


def precision(counter):
    """correct / (correct + incorrect), or None when no item was judged correct or incorrect."""
    judged = counter["correct"] + counter["incorrect"]
    return counter["correct"] / judged if judged else None


def gate(per_class):
    """{class: decision}: `drop` (precision < 90% with at least 2 incorrect, or more than 30% unclear), `second-round` (exactly one
    incorrect among fewer than 10 rows, not dropped) or `keep`."""
    decisions = {}
    for name, counter in per_class.items():
        value = precision(counter)
        unclear = counter["unclear"] / counter["total"] if counter["total"] else 0.0
        if (counter["incorrect"] >= 2 and value is not None and value < PRECISION_MIN) or unclear > UNCLEAR_MAX:
            decisions[name] = "drop"
        elif counter["incorrect"] == 1 and counter["total"] < ROUND_ONE_LIMIT and (value is None or value < PRECISION_MIN):
            decisions[name] = "second-round"
        else:
            decisions[name] = "keep"
    return decisions


def review_lines(per_class, controls, random60, decisions):
    """Markdown lines of the gate result: per class, overall (all rows, weighted by class size, random 60) and the controls."""
    rows = []
    for name in sorted(per_class):
        c = per_class[name]
        value = precision(c)
        rows.append((name, c["total"], c["correct"], c["incorrect"], c["unclear"], "-" if value is None else "%.1f%%" % (100 * value),
                     "%.0f%%" % (100.0 * c["unclear"] / c["total"]) if c["total"] else "-", c["population"], decisions[name]))
    lines = ["# Review result", ""] + _table(("class", "reviewed", "correct", "incorrect", "unclear", "precision", "unclear share",
                                              "class size", "decision"), rows)
    total = collections.Counter()
    for c in per_class.values():
        for field in ("correct", "incorrect", "unclear", "total"):
            total[field] += c[field]
    weighted_num = sum(precision(c) * c["population"] for c in per_class.values() if precision(c) is not None)
    weighted_den = sum(c["population"] for c in per_class.values() if precision(c) is not None)
    judged60 = random60["correct"] + random60["incorrect"]
    lines += ["", "- reviewed rows: %d (correct %d, incorrect %d, unclear %d)" % (total["total"], total["correct"], total["incorrect"], total["unclear"]),
              "- overall precision of all reviewed rows: %s" % ("-" if precision(total) is None else "%.1f%%" % (100 * precision(total))),
              "- overall precision weighted by class size: %s" % ("-" if not weighted_den else "%.1f%%" % (100 * weighted_num / weighted_den)),
              "- random subset: %d reviewed, precision %s (unclear %d)" % (sum(random60.values()),
                                                                            "-" if not judged60 else "%.1f%%" % (100.0 * random60["correct"] / judged60),
                                                                            random60["unclear"]),
              "- control items accepted as correct: %d of %d%s" % (controls["correct"], sum(controls.values()),
                                                                  " (TOO LENIENT: more than 1 control accepted, repeat the review)"
                                                                  if controls["correct"] > 1 else "")]
    return lines


# ---------------------------------------------------------------- command line

def _print(lines):
    for line in lines:
        print(line)


def _data_dir(args):
    return args.data_dir


def _command_select(args):
    work = Work(args.work)
    inputs = load_inputs(args.data_dir)
    flags = read_flags(work.flags, inputs.corpora)
    pool = build_pool(inputs, flags)
    selected, cut = apply_cap(pool, args.cap, args.seed)
    report = selection_report(pool, selected, cut, inputs, args.cap)
    _print(report)
    common.write_text(work.private_file("selection-report.md"), report)
    write_rows(work.private_file("selection.tsv"), SELECTION_COLUMNS, selection_rows(selected))
    write_rows(work.private_file("selection-cut.tsv"), SELECTION_COLUMNS, [(0, e.band, e.key[0], e.key[1], e.key[2], e.ntokens) for e in cut])
    return 0


def _command_export(args):
    work = Work(args.work)
    inputs = load_inputs(args.data_dir)
    flags = read_flags(work.flags, inputs.corpora)
    selection = read_selection(work)
    pool = build_pool(inputs, flags)
    selected = [pool[key] for key, band, ntokens in selection]
    if any(entry.band != band for entry, (key, band, ntokens) in zip(selected, selection)):
        raise ValueError("private/selection.tsv does not match the current inputs: run select again")
    ntokens = {entry.key: entry.ntokens for entry in selected}
    stale = [os.path.join(model, name) for model in MODELS if os.path.isdir(work.out(model)) for name in os.listdir(work.out(model))]
    if stale and not args.force:
        print("refusing to export: %d tagger output files exist under batches/out (they belong to the previous export); "
              "remove them or use --force" % len(stale))
        return 1
    ids = assign_ids(ntokens, args.seed)
    batches = make_batches(selected, ntokens, args.seed, args.size, args.rows)
    for name in os.listdir(work.batches) if os.path.isdir(work.batches) else ():
        if name.endswith(".tsv") and BATCH_RE.match(name[:-4]):
            os.remove(os.path.join(work.batches, name))
    for batch in batches:
        write_batch(work.batch_file(batch.name), batch_rows(batch, ids, inputs))
    write_rows(work.private_file("key.tsv"), KEY_COLUMNS, key_rows(batches, ids, inputs, flags))
    write_status(work, collections.OrderedDict((batch.name, new_status_record(batch.name)) for batch in batches))
    for band in BANDS:
        members = [b for b in batches if b.band == band]
        print("%s: %d batches, %d sentences, %d tokens" % (band, len(members), sum(len(b.keys) for b in members),
                                                         sum(ntokens[k] for b in members for k in b.keys)))
    print("%d batches, %d sentences; sentences per batch %d..%d, token rows per batch %d..%d" % (
        len(batches), sum(len(b.keys) for b in batches), min(len(b.keys) for b in batches), max(len(b.keys) for b in batches),
        min(sum(ntokens[k] for k in b.keys) for b in batches), max(sum(ntokens[k] for k in b.keys) for b in batches)))
    return 0


def _command_check_batches(args):
    work = Work(args.work)
    errors = check_batches(work, load_inputs(args.data_dir), args.rows)
    for message in errors[:args.limit]:
        print("  " + message)
    key = read_key(work)
    print("%d batch files checked, %d sentences in the key: %s" % (len({k.batch for k in key}), len(key), "FAILED" if errors else "passed"))
    return 1 if errors else 0


def _command_validate_output(args):
    work = Work(args.work)
    if not args.batch and not args.all:
        raise ValueError("give --batch BNNN or --all")
    models = (args.model,) if args.model else MODELS
    targets = [args.batch] if args.batch else sorted(read_status(work))
    failed = 0
    for batch in targets:
        for model in models:
            if args.all:
                files, problems = output_files(work.out(model), batch)
                if not files and not problems:
                    continue
            state, errors = validate_and_record(work, model, batch, count=not args.no_count)
            print("%s %s: %s" % (batch, model, state))
            for message in errors[:args.limit]:
                print("  " + message)
            failed += state == "invalid"
    return 1 if failed else 0


def _command_status(args):
    status = read_status(Work(args.work))
    counts = collections.Counter()
    for record in status.values():
        counts["opus " + record["opus_state"]] += 1
        counts["sol " + record["sol_state"]] += 1
    print("%d batches" % len(status))
    for name, count in sorted(counts.items()):
        print("  %-16s %d" % (name, count))
    return 0


def _command_decide(args):
    work = Work(args.work)
    drop = frozenset(item for item in (args.drop or "").split(",") if item)
    out = args.out or work.private_file("adjudicated.tsv")
    code, lines = run_decide(work, args.data_dir, drop, out)
    _print(lines)
    return code


def _command_sample(args):
    work = Work(args.work)
    inputs = load_inputs(args.data_dir)
    _, records = read_records(work.private_file("decisions.tsv"))
    exclude = set()
    for name in args.exclude or ():
        exclude.update((r["sent_id"], r["token_index"]) for r in read_records(name)[1])
    only = frozenset(args.only.split(",")) if args.only else None
    items = build_sample(records, args.seed, args.random, args.per_class, args.min_total, args.controls, only, frozenset(exclude))
    lines, keys = sample_lines(items, inputs, collections.Counter(r["class"] for r in reviewable(records)), args.prefix)
    common.write_text(work.private_file("review-sample%s.md" % args.suffix), lines)
    write_rows(work.private_file("review-sample-key%s.tsv" % args.suffix), SAMPLE_KEY_COLUMNS, keys)
    real = [i for i in items if i.kind == "real"]
    print("%d items: %d real (%d random), %d control; %d classes" % (len(items), len(real), sum(i.random60 for i in real),
                                                                    len(items) - len(real), len({i.record["class"] for i in real})))
    return 0


def _command_review_result(args):
    work = Work(args.work)
    keys = []
    for name in args.keys or [work.private_file("review-sample-key.tsv")]:
        keys.extend(read_records(name)[1])
    verdicts = []
    for name in args.verdicts:
        with open(name, "r", encoding="utf-8") as handle:
            verdicts.extend(parse_verdicts(handle.read()))
    unknown = {v.item for v in verdicts} - {k["item"] for k in keys}
    if unknown:
        raise ValueError("verdicts for unknown items: " + ", ".join(sorted(unknown)[:5]))
    missing = {k["item"] for k in keys} - {v.item for v in verdicts}
    per_class, controls, random60 = class_statistics(keys, verdicts)
    decisions = gate(per_class)
    lines = review_lines(per_class, controls, random60, decisions)
    reviewed = sum(c["total"] for c in per_class.values())
    lines.append("- unanswered items: %d%s" % (len(missing), "" if not missing else " (" + ", ".join(sorted(missing)[:5]) + " ...)"))
    if reviewed < 60:
        lines.append("- WARNING: fewer than 60 reviewed rows; the gate figures are not enough")
    lines.append("- drop: " + (",".join(sorted(n for n, d in decisions.items() if d == "drop")) or "none"))
    lines.append("- second round: " + (",".join(sorted(n for n, d in decisions.items() if d == "second-round")) or "none"))
    _print(lines)
    common.write_text(work.private_file("review-result.md"), lines)
    rows = [(n, c["total"], c["correct"], c["incorrect"], c["unclear"], "" if precision(c) is None else "%.4f" % precision(c), decisions[n])
            for n, c in sorted(per_class.items())]
    write_rows(work.private_file("review-result.tsv"), ("class", "reviewed", "correct", "incorrect", "unclear", "precision", "decision"), rows)
    return 1 if controls["correct"] > 1 or reviewed < 60 else 0


def main(argv=None):
    parser = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    parser.add_argument("--work", required=True, help="work folder (flags/, batches/, private/); outside the repository")
    parser.add_argument("--data-dir", default=None, help="data folder (default: the repository's data/)")
    sub = parser.add_subparsers(dest="command", required=True)
    select = sub.add_parser("select", help="choose the sentences; prints the volumes first")
    select.add_argument("--cap", type=int, default=CAP)
    select.add_argument("--seed", type=int, default=SEED)
    export = sub.add_parser("export", help="write the blind batches, the key and status.tsv")
    export.add_argument("--size", type=int, default=BATCH_SENTENCES, help="sentences per batch (at most)")
    export.add_argument("--rows", type=int, default=BATCH_ROWS, help="token rows per batch (at most)")
    export.add_argument("--seed", type=int, default=SEED)
    export.add_argument("--force", action="store_true", help="export although tagger outputs of a previous export exist")
    check = sub.add_parser("check-batches", help="re-read the batches: 3 columns, tokens equal the corpus, no leak")
    check.add_argument("--rows", type=int, default=BATCH_ROWS)
    check.add_argument("--limit", type=int, default=20)
    validate = sub.add_parser("validate-output", help="validate tagger outputs and record the state in status.tsv")
    validate.add_argument("--batch", default=None)
    validate.add_argument("--model", choices=MODELS, default=None)
    validate.add_argument("--all", action="store_true", help="every batch that has an output")
    validate.add_argument("--no-count", action="store_true", help="do not count an attempt (a re-check)")
    validate.add_argument("--limit", type=int, default=5)
    sub.add_parser("status", help="counts of the batch states")
    decide = sub.add_parser("decide", help="apply the decision rule; write adjudicated.tsv and the reports")
    decide.add_argument("--drop", default=None, help="comma separated primary classes whose tag and flag rows are left out")
    decide.add_argument("--out", default=None, help="adjudicated table to write (default: private/adjudicated.tsv)")
    sample = sub.add_parser("sample", help="the review sample for the precision gate")
    sample.add_argument("--seed", type=int, default=SEED)
    sample.add_argument("--random", type=int, default=60, help="random rows (the first stratum)")
    sample.add_argument("--per-class", type=int, default=8)
    sample.add_argument("--min-total", type=int, default=150)
    sample.add_argument("--controls", type=int, default=8)
    sample.add_argument("--only", default=None, help="comma separated classes (a second round)")
    sample.add_argument("--exclude", nargs="*", default=None, help="sample key files whose rows are not drawn again")
    sample.add_argument("--prefix", default="R", help="letter of the item names (a second round uses another)")
    sample.add_argument("--suffix", default="", help="suffix of the output file names (a second round: -r2)")
    review = sub.add_parser("review-result", help="precision per class, the gate and the classes to drop")
    review.add_argument("--verdicts", nargs="+", required=True, help="files with the reviewer's lines item<TAB>verdict<TAB>cite")
    review.add_argument("--keys", nargs="*", default=None, help="sample key files (default: private/review-sample-key.tsv)")
    args = parser.parse_args(argv)
    commands = {"select": _command_select, "export": _command_export, "check-batches": _command_check_batches,
                "validate-output": _command_validate_output, "status": _command_status, "decide": _command_decide,
                "sample": _command_sample, "review-result": _command_review_result}
    return commands[args.command](args)


if __name__ == "__main__":
    sys.exit(main())

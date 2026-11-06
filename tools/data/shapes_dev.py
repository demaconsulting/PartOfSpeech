"""Builder of the hard-shape development set data/eval/shapes-dev/ (Python 3, standard library only).

The set holds sentences with one judged word each (a word that is both a noun and a verb in the lexicon), chosen for five
hard shapes, plus controls (the same spelling in the other class). Every sentence is labeled blind by two models that
follow data/CONVENTIONS.md; this tool does everything around the models that needs no model and never calls one.
The work folder (`--work`) is outside the repository. It holds what sourcing and authoring hand over, the blind batches
the taggers read and their outputs, and the private key.

Input layout of the work folder W (`--work W`):

  W/sources-draft.tsv          one row per source document, the 12 columns of data/eval/gold-v2/sources.tsv:
                               source_id family title url author year license_basis terms_url retrieved raw_sha256
                               text_sha256 sentences        (tab separated, a header row; `sentences` is recomputed by build)
  W/text/<source_id>.txt       the extracted text of a document, one sentence per line, UTF-8 (headers, footers, step
                               labels and tables of contents already stripped; the document id is the file name)
  W/authored/<shape>.tsv       authored top-up sentences of a shape: header `lemma<TAB>role<TAB>sentence`, role `target` or
                               `control` (a control uses the lemma in the class other than the shape's class)
  W/tokens.tsv                 written by `tokenize`: the tokens of every sentence (the corpus tokenization of the library)

Commands (all take `--work W`; `--data-dir D` replaces the repository's data folder, for tests):

  python tools/data/shapes_dev.py --work W exclusions [--check ID_OR_URL ...]   # used-document index W/exclusions.tsv; checks the draft
  python tools/data/shapes_dev.py --work W tokenize [--cmd "COMMAND PREFIX"]    # shells out to the C# trainer command shapes-tokenize
  python tools/data/shapes_dev.py --work W candidates [--no-ud] [--target 120] [--over 1.6] [--lemma-cap 4]
                                                                                # position and lexicon pre-selection + leakage filters
  python tools/data/shapes_dev.py --work W export [--size 150] [--rows 3500]    # blind batches, private/key.tsv, batches/status.tsv
  python tools/data/shapes_dev.py --work W check-batches                        # 3 columns, tokens equal the key, no leak
  python tools/data/shapes_dev.py --work W validate-output --batch b001 --model opus   # (or --all); updates batches/status.tsv
  python tools/data/shapes_dev.py --work W status
  python tools/data/shapes_dev.py --work W decide                               # the pre-registered decision rule
  python tools/data/shapes_dev.py --work W sample [--per-shape 12] [--controls 4]   # review sample for the precision gate
  python tools/data/shapes_dev.py --work W review-result --verdicts FILE ...     # precision of the model review; STOP below 95%
  python tools/data/shapes_dev.py --work W build [--out-dir D]                  # dev.tsv and sources.tsv
  python tools/data/shapes_dev.py --work W report [--out-dir D]                 # counts for the README

The tokenizer command is `dotnet run -c Release --project tools/DemaConsulting.PartOfSpeech.Trainer -- shapes-tokenize --in FILE
--out FILE` (the default of `tokenize`): one sentence per input line; one line `text<TAB>sentences<TAB>tokens` (tokens
separated by single spaces) per output line, in the same order. The tokens therefore follow the corpus convention by
construction. The data test of the repository asserts that the library tokenizer reproduces every stored token.

Decision rule (pre-registered in data/eval/shapes-dev/README.md before any label was seen; see `decide_item`). For the judged
word (the target): different tags -> drop the sentence (counted per shape); the same tag but a class other than the intended one
(the shape's class for a `target`, the other class for a `control`) -> drop, counted as `class-mismatch`; otherwise keep. Every
other token: the same tag -> that tag, `unanimous`; different tags -> `disputed`, the tag written is the one of the first labeler
(the `opus` tagger), a fixed tie-break; flags only where both agree. The target is never disputed. Confidence is recorded and unused.

Blindness: a batch file has exactly three columns `id, token_index, token`; the opaque id is unrelated to the sentence, its shape,
its source or its order. The key lives in private/ and the taggers are never pointed to it. The shape probe is read only inside
`leakage.probe_ngrams` to build the hashed 5-gram set of the leakage gate; no probe text is ever printed or stored.
"""
import argparse
import collections
import hashlib
import math
import os
import random
import re
import shlex
import subprocess
import sys

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import adjudicate
import common
import leakage

# ---------------------------------------------------------------- constants

SEED = 20261110
SHAPES = ("n-compound-head", "n-subject-initial", "v-imperative-initial", "n-measure", "v-after-modal")
SHAPE_CLASS = {shape: ("Noun" if shape.startswith("n-") else "Verb") for shape in SHAPES}
OTHER_CLASS = {"Noun": "Verb", "Verb": "Noun"}
CLASS_TAG = {"Noun": "NOUN", "Verb": "VERB"}
ROLES = ("target", "control")
ORIGINS = ("real", "authored")
AUTHORED_ID = "shapes-dev-authored"
AUTHORED_FAMILY = "project-written"
AUTHORED_LICENSE = "Written by the project; the project owner dedicates the sentences to the public domain under CC0 1.0"
AUTHORED_TERMS = "https://creativecommons.org/publicdomain/zero/1.0/"
TARGET_PER_SHAPE = 120
OVER_COLLECT = 1.6
LEMMA_CAP = 4
CONTROL_LEMMA_CAP = 2
AUTHORED_SHARE = 0.30
CONTROLS = 100
MIN_TOKENS, MAX_TOKENS = 3, 30
DOCUMENT_KEY_HITS = 3          # a document with this many sentences that are also in a training or evaluation set is rejected
PRECISION_MIN = 0.95
CONTROLS_ACCEPTED_MAX = 1

SOURCE_COLUMNS = ("source_id", "family", "title", "url", "author", "year", "license_basis", "terms_url", "retrieved", "raw_sha256",
                  "text_sha256", "sentences")
CANDIDATE_COLUMNS = ("cand_id", "shape", "role", "target", "lemma", "origin", "source_id", "line", "text", "tokens")
KEY_COLUMNS = ("opaque_id", "batch", "sent_key", "source_id", "ntokens")
ITEM_COLUMNS = ("cand_id", "sent_key", "shape", "role", "target", "lemma", "origin", "source_id", "outcome", "reason", "opus_tag",
                "sol_tag", "agreed_tag", "disputed_tokens")
ROW_COLUMNS = ("sent_key", "index", "form", "tag", "status")
SAMPLE_KEY_COLUMNS = ("item", "kind", "cand_id", "shape", "target", "shown_tag", "agreed_tag")
USED_SOURCE_TABLES = ("open/sources.tsv", "eval/gold-v2/sources.tsv", "silver/sources.tsv", "masc/sources.tsv",
                      "masc/excluded-documents.tsv", "silver/excluded-documents.tsv")
EXTRA_DOCUMENTS = ("wordsense-ste-probe",)
HEADER = [
    "# corpus = DemaConsulting.PartOfSpeech hard-shape development set (UPOS), dev split",
    "# license = CC0-1.0 for annotations and selection; sentences are US federal government works, US public domain or CC0, see sources.tsv",
    "# columns = form, upos, status",
    "# status = unanimous | disputed (two blind model labelers; disputed = they differ, the tag is the first labeler's)",
    "# judged word = per sentence: shape (one of the five hard shapes), role (target | control), target (zero-based token index), "
    "lemma, origin (real | authored); the judged word is never disputed",
    "# use = development only: select settings here, never train on it, never report it as a held-out figure",
]

# Closed word lists of the position heuristics. They only pre-select candidates; the class of the judged word always comes
# from the two blind labelers.
DETERMINERS = frozenset("the a an this that these those each every any some no all both its his her their our my your whose "
                        "another either neither".split())
PREPOSITIONS = frozenset("of in on at by for with from into onto over under between through during before after against along "
                         "around within without about above below across behind beside beyond near off per than via toward "
                         "towards upon".split())
MODALS = frozenset("can could may might must shall should will would".split())
AUXILIARIES = MODALS | frozenset("is are was were be been being am has have had do does did".split())
OBJECT_PRONOUNS = frozenset("it them him her us you me one".split())
CONJUNCTIONS = frozenset("and or but nor if when while that which because although".split())
FOLLOWING_ADVERBS = frozenset("not never always only all both each any also then".split())
NUMBER_WORDS = frozenset("one two three four five six seven eight nine ten eleven twelve fifteen twenty thirty forty fifty sixty "
                         "seventy eighty ninety hundred thousand million".split())
QUANTITY_WORDS = frozenset("another several few many couple dozen half".split())
UNITS = frozenset("pound ounce cup drop pinch load round turn step inch foot yard mile gallon pint quart bushel degree stone "
                  "dose pack bar piece slice sheet roll box bag can jar head bunch handful teaspoon tablespoon".split())
BASE_FORM_SHAPES = ("v-imperative-initial", "v-after-modal")


# ---------------------------------------------------------------- small helpers

def normalize_text(text):
    """A sentence as stored: NFC, any white space run (also a non-breaking space) one space, no outer space."""
    return " ".join(common.nfc(text).split())


def sent_key_of(text):
    """The id of a sentence in the work folder: a hash of its leakage key (so two spellings of one sentence are one)."""
    return "k" + hashlib.sha256(common.key(text).encode("utf-8")).hexdigest()[:12]


def rank_of(seed, *parts):
    """A deterministic pseudo-random rank: the same inputs always sort the same way, whatever the platform."""
    return hashlib.sha256((str(seed) + ":" + ":".join(str(p) for p in parts)).encode("utf-8")).hexdigest()


def tag_class(tag):
    """`Noun` (NOUN, PROPN), `Verb` (VERB) or `Other` for the tag of a judged word (the classes of data/CONVENTIONS.md)."""
    if tag in ("NOUN", "PROPN"):
        return "Noun"
    return "Verb" if tag == "VERB" else "Other"


def intended_class(shape, role):
    """The class that the judged word of an item has when the sentence is an instance: the shape's class, or the other for a control."""
    return SHAPE_CLASS[shape] if role == "target" else OTHER_CLASS[SHAPE_CLASS[shape]]


def work_path(work, *parts):
    return os.path.join(work.root, *parts)


def read_plain_tsv(path, columns):
    """Rows (dicts) of a hand-written TSV: a header row naming `columns`, then one row per line (no escaping; blank lines ignored)."""
    lines = [line for line in common.read_lines(path) if line.strip() != ""]
    if not lines or tuple(lines[0].split("\t")) != tuple(columns):
        raise ValueError("%s: the header must be %s" % (path, "\\t".join(columns)))
    rows = []
    for number, line in enumerate(lines[1:], 2):
        cells = line.split("\t")
        if len(cells) != len(columns):
            raise ValueError("%s, line %d: %d cells, expected %d" % (path, number, len(cells), len(columns)))
        rows.append(dict(zip(columns, cells)))
    return rows


# ---------------------------------------------------------------- exclusions (documents that training or evaluation already used)

def normalize_url(url):
    """host (lower case, no `www.`) and path (no query, fragment or trailing slash); the empty string for a text that is no URL."""
    match = re.match(r"^[a-z][a-z0-9+.-]*://([^/?#]+)([^?#]*)", url.strip(), re.I)
    if not match:
        return ""
    host = match.group(1).lower()
    host = host[4:] if host.startswith("www.") else host
    return host + match.group(2).rstrip("/")


def gutenberg_ids(text):
    """The canonical ids `gutenberg-<n>` that a text names (an id, or a gutenberg.org ebook, file or cache URL)."""
    found = {"gutenberg-" + n for n in re.findall(r"gutenberg-(\d+)", text)}
    found |= {"gutenberg-" + n for n in re.findall(r"gutenberg\.org/(?:ebooks|files|cache/epub)/(\d+)", text)}
    return found


def exclusion_index(data_dir):
    """{(kind, value): origin} over the documents of the sources and excluded-documents tables (kind `id` or `url`)."""
    index = {}
    for table in USED_SOURCE_TABLES:
        path = os.path.join(data_dir, *table.split("/"))
        if not os.path.exists(path):
            continue
        for line in common.read_lines(path):
            if not line or line.startswith("#"):
                continue
            cells = line.split("\t")
            if cells[0] in ("source_id", "doc_id", "document_id"):
                continue
            index.setdefault(("id", cells[0]), table)
            for cell in cells:
                if cell.startswith("http"):
                    normalized = normalize_url(cell)
                    if normalized:
                        index.setdefault(("url", normalized), table)
            for found in gutenberg_ids(line):
                index.setdefault(("id", found), table)
    for document in EXTRA_DOCUMENTS:
        index.setdefault(("id", document), "built-in")
    return index


def document_rejection(index, source_id, url=""):
    """The reason (a table name) why a document was already used, or None when its id, Gutenberg number and URL are all new."""
    for found in {source_id} | gutenberg_ids(source_id + " " + url):
        if ("id", found) in index:
            return "id %s is listed in %s" % (found, index[("id", found)])
    normalized = normalize_url(url) if url else ""
    if normalized and ("url", normalized) in index:
        return "url is listed in " + index[("url", normalized)]
    return None


def read_draft(work):
    """[source row dict] of W/sources-draft.tsv (empty when the file is missing); `doc_id` is accepted for the first column header."""
    path = work_path(work, "sources-draft.tsv")
    if not os.path.exists(path):
        return []
    lines = [line for line in common.read_lines(path) if line.strip() != ""]
    header = lines[0].split("\t") if lines else []
    if header and header[0] == "doc_id":
        header[0] = "source_id"
    if tuple(header) != SOURCE_COLUMNS:
        raise ValueError("%s: the header must be %s" % (path, "\\t".join(SOURCE_COLUMNS)))
    rows = []
    for number, line in enumerate(lines[1:], 2):
        cells = line.split("\t")
        if len(cells) != len(SOURCE_COLUMNS):
            raise ValueError("%s, line %d: %d cells, expected %d" % (path, number, len(cells), len(SOURCE_COLUMNS)))
        rows.append(dict(zip(SOURCE_COLUMNS, cells)))
    return rows


def _command_exclusions(args):
    work = adjudicate.Work(args.work)
    index = exclusion_index(args.data_dir)
    rows = sorted((kind, value, origin) for (kind, value), origin in index.items())
    adjudicate.write_rows(work_path(work, "exclusions.tsv"), ("kind", "value", "origin"), rows)
    print("%d entries (%d ids, %d urls) written to exclusions.tsv" % (len(rows), sum(r[0] == "id" for r in rows), sum(r[0] == "url" for r in rows)))
    rejected = 0
    for value in args.check or ():
        reason = document_rejection(index, value, value if value.startswith("http") else "")
        print("%s: %s" % ("REJECT" if reason else "ACCEPT", value) + ((" (" + reason + ")") if reason else ""))
        rejected += reason is not None
    for row in read_draft(work):
        reason = document_rejection(index, row["source_id"], row["url"])
        if reason:
            print("REJECT draft document %s: %s" % (row["source_id"], reason))
            rejected += 1
    return 1 if rejected else 0


# ---------------------------------------------------------------- inputs: text, authored sentences, tokens

def read_documents(work):
    """{source_id: [(line number, sentence)]} of W/text/*.txt (one sentence per line; empty lines skipped)."""
    folder = work_path(work, "text")
    documents = {}
    if os.path.isdir(folder):
        for name in sorted(os.listdir(folder)):
            if name.endswith(".txt"):
                lines = common.read_lines(os.path.join(folder, name))
                documents[name[:-4]] = [(number, normalize_text(line)) for number, line in enumerate(lines, 1) if line.strip()]
    return documents


def read_authored(work):
    """[(shape, lemma, role, sentence)] of W/authored/<shape>.tsv in shape and file order."""
    folder = work_path(work, "authored")
    found = []
    for shape in SHAPES:
        path = os.path.join(folder, shape + ".tsv")
        if not os.path.exists(path):
            continue
        for number, row in enumerate(read_plain_tsv(path, ("lemma", "role", "sentence")), 1):
            if row["role"] not in ROLES or not row["lemma"].strip() or not row["sentence"].strip():
                raise ValueError("%s: row %d needs a lemma, a role of %s and a sentence" % (path, number, "|".join(ROLES)))
            found.append((shape, row["lemma"].strip().lower(), row["role"], normalize_text(row["sentence"])))
    return found


def read_tokens(work):
    """{sentence text: (sentence count, [tokens])} of W/tokens.tsv (empty when `tokenize` has not run)."""
    path = work_path(work, "tokens.tsv")
    if not os.path.exists(path):
        return {}
    _, records = adjudicate.read_records(path)
    return {r["text"]: (int(r["sentences"]), r["tokens"].split(" ") if r["tokens"] else []) for r in records}


def default_tokenizer_command():
    project = os.path.join(common.ROOT, "tools", "DemaConsulting.PartOfSpeech.Trainer")
    return ["dotnet", "run", "-c", "Release", "--project", project, "--"]


def run_tokenizer(texts, command):
    """{text: (sentence count, [tokens])}: shells out to `command shapes-tokenize --in FILE --out FILE` (below normal priority)."""
    import tempfile
    result = {}
    with tempfile.TemporaryDirectory() as folder:
        source, target = os.path.join(folder, "in.txt"), os.path.join(folder, "out.tsv")
        common.write_text(source, texts)
        flags = getattr(subprocess, "BELOW_NORMAL_PRIORITY_CLASS", 0)
        completed = subprocess.run(list(command) + ["shapes-tokenize", "--in", source, "--out", target], creationflags=flags, timeout=1800)
        if completed.returncode != 0:
            raise RuntimeError("the tokenizer command failed with exit code %d" % completed.returncode)
        lines = common.read_lines(target)
    if len(lines) != len(texts):
        raise RuntimeError("the tokenizer wrote %d lines for %d sentences" % (len(lines), len(texts)))
    for text, line in zip(texts, lines):
        cells = line.split("\t")
        if len(cells) != 3 or cells[0] != text:
            raise RuntimeError("the tokenizer output does not match its input at %r" % text[:40])
        result[text] = (int(cells[1]), cells[2].split(" ") if cells[2] else [])
    return result


def _command_tokenize(args):
    work = adjudicate.Work(args.work)
    texts = set()
    drafted = {row["source_id"] for row in read_draft(work)}
    for source_id, sentences in read_documents(work).items():
        if source_id in drafted:
            texts.update(text for _, text in sentences)
    texts.update(sentence for _, _, _, sentence in read_authored(work))
    known = read_tokens(work)
    missing = sorted(texts - set(known))
    if missing:
        command = [part.strip('"') for part in shlex.split(args.cmd, posix=os.name != "nt")] if args.cmd else default_tokenizer_command()
        known.update(run_tokenizer(missing, command))
    rows = [(text, count, " ".join(tokens)) for text, (count, tokens) in sorted(known.items())]
    adjudicate.write_rows(work_path(work, "tokens.tsv"), ("text", "sentences", "tokens"), rows)
    print("%d sentences, %d newly tokenized; written to tokens.tsv" % (len(texts), len(missing)))
    return 0


# ---------------------------------------------------------------- lexicon and shape heuristics

def lexicon_of(data_dir):
    return common.read_lexicon(os.path.join(data_dir, "lexicon", "lexicon.tsv"))


def has_tag(lex, token, tag):
    return tag in lex.get(token.lower(), ())


def is_noun_verb(lex, token):
    """The spelling is listed with both NOUN and VERB in the lexicon (the words the shapes are about)."""
    tags = lex.get(token.lower())
    return bool(tags) and "NOUN" in tags and "VERB" in tags


def is_word(token):
    return common.has_letter_or_digit(token)


def lemma_of(token, lex):
    """The lower-case lemma of a token for the lemma cap: the plural or third person form is reduced to a listed noun-verb base."""
    low = token.lower()
    if low.endswith("ss") or (is_noun_verb(lex, low) and not low.endswith("s")):
        return low
    for suffix, replacement in (("ies", "y"), ("es", ""), ("s", "")):
        if low.endswith(suffix) and len(low) > len(suffix) + 1:
            stem = low[:-len(suffix)] + replacement
            if is_noun_verb(lex, stem):
                return stem
    return low


def is_quantity(token):
    low = token.lower()
    return common.is_number(token) or low in NUMBER_WORDS or low in QUANTITY_WORDS


def follows_like_verb(lex, token):
    """What may stand right after the head of a compound or a subject: the end, a mark, an auxiliary, a preposition, a conjunction or a verb."""
    low = token.lower()
    return (not is_word(token) or low in AUXILIARIES or low in PREPOSITIONS or low in CONJUNCTIONS or low == "to"
            or has_tag(lex, low, "VERB") or has_tag(lex, low, "AUX"))


def shape_hits(tokens, lex):
    """[(shape, index)]: where the tokens match the position and lexicon pattern of a shape (at most one index per shape).

    The patterns only pre-select candidates (plan 2.4); the class of the judged word is decided by the two labelers.
    """
    hits = {}
    n = len(tokens)
    for i, token in enumerate(tokens):
        low = token.lower()
        if not is_noun_verb(lex, low) or not is_word(token):
            continue
        prev = tokens[i - 1] if i >= 1 else None
        nxt = tokens[i + 1] if i + 1 < n else None
        plow = prev.lower() if prev is not None else ""
        nlow = nxt.lower() if nxt is not None else ""
        if i == 0 and token[:1].isupper() and low not in DETERMINERS | PREPOSITIONS | AUXILIARIES and nxt is not None:
            if nlow in AUXILIARIES or (has_tag(lex, nlow, "VERB") and nlow not in DETERMINERS | PREPOSITIONS):
                hits.setdefault("n-subject-initial", i)
            if (nlow in DETERMINERS or nlow in PREPOSITIONS or nlow in OBJECT_PRONOUNS or nlow in FOLLOWING_ADVERBS
                    or is_quantity(nxt)) and nlow not in AUXILIARIES and lemma_of(token, lex) == low:
                hits.setdefault("v-imperative-initial", i)
        if i >= 1 and (is_quantity(prev) or (lemma_of(token, lex) in UNITS and plow in ("a", "an", "per", "each", "every"))):
            hits.setdefault("n-measure", i)
        if i >= 1 and (plow in MODALS or plow == "to" or (i >= 2 and plow in FOLLOWING_ADVERBS
                                                           and (tokens[i - 2].lower() in MODALS or tokens[i - 2].lower() == "to"))):
            if lemma_of(token, lex) == low:
                hits.setdefault("v-after-modal", i)
        if (i >= 1 and has_tag(lex, plow, "NOUN") and is_word(prev) and plow not in DETERMINERS | PREPOSITIONS | AUXILIARIES | OBJECT_PRONOUNS
                and (nxt is None or follows_like_verb(lex, nxt))):
            before = tokens[i - 2].lower() if i >= 2 else None
            if before is not None and (before in DETERMINERS or before in PREPOSITIONS or before in CONJUNCTIONS
                                       or before in AUXILIARIES or not is_word(tokens[i - 2])):
                hits.setdefault("n-compound-head", i)
    return sorted(((shape, index) for shape, index in hits.items()), key=lambda item: SHAPES.index(item[0]))


def control_hits(tokens, lex, shape, lemmas):
    """[index] where a lemma of `lemmas` stands in the class other than the shape's class (a pre-selection like `shape_hits`).

    For a noun shape the lemma is looked for after a modal or `to`, or as the first word of an instruction; for a verb shape
    after a determiner.
    """
    found = []
    for i, token in enumerate(tokens):
        if lemma_of(token, lex) not in lemmas or not is_noun_verb(lex, token) or not is_word(token):
            continue
        prev = tokens[i - 1].lower() if i >= 1 else ""
        nxt = tokens[i + 1].lower() if i + 1 < len(tokens) else ""
        if SHAPE_CLASS[shape] == "Noun":
            if prev in MODALS or prev == "to" or (i == 0 and token[:1].isupper() and (nxt in DETERMINERS or nxt in OBJECT_PRONOUNS)):
                found.append(i)
        elif prev in DETERMINERS and nxt != "":
            found.append(i)
    return found


# ---------------------------------------------------------------- candidates

class LeakageFilter:
    """Keys of every training and evaluation set (and the hashed probe 5-grams) that a sentence of the new set must not equal or share."""

    def __init__(self, data_dir, include_ud=True):
        self.keys = set(leakage.evaluation_keys(include_ud, shapes_dev=False)[0])
        trains = {}
        for label, parts in (("open-train", ("open", "train.tsv")), ("masc", ("masc", "masc.tsv")), ("silver", ("silver", "silver.tsv"))):
            path = os.path.join(data_dir, *parts)
            if os.path.exists(path):
                trains[label] = [(s["source_id"], s["text"]) for s in common.read_corpus(path)[1]]
        leakage.require_sets(trains, ("open-train", "masc", "silver"), "training")
        for items in trains.values():
            self.keys |= {common.key(text) for _, text in items} - {""}
        self.probe = leakage.probe_ngrams(leakage.PROBE_NGRAM)

    def key_hit(self, text):
        return common.key(text) in self.keys

    def probe_hit(self, text):
        return bool(leakage.ngrams(text, leakage.PROBE_NGRAM) & self.probe)


def collect_candidates(work, data_dir, include_ud=True, target=TARGET_PER_SHAPE, over=OVER_COLLECT, lemma_cap=LEMMA_CAP,
                       seed=SEED, tokens_override=None, filt=None):
    """(candidate rows, report lines): the candidates of every shape from the real text and the authored sentences.

    Real sentences are scanned in document and line order; a sentence that is too short or too long, a duplicate, equal to a
    training or evaluation sentence, or sharing a word 5-gram with the probe is dropped, and a document with
    DOCUMENT_KEY_HITS sentences that are also in a training or evaluation set is rejected as a whole (a mirror or a reprint).
    """
    tokens_of = tokens_override if tokens_override is not None else read_tokens(work)
    lex = lexicon_of(data_dir)
    filt = filt or LeakageFilter(data_dir, include_ud)
    draft = {row["source_id"]: row for row in read_draft(work)}
    index = exclusion_index(data_dir)
    documents = read_documents(work)
    drops = collections.Counter()
    rejected = {}
    problems = []
    seen_keys = set()
    pools = {shape: [] for shape in SHAPES}          # (rank, row dict) of the targets
    all_sentences = []                               # (source_id, line, text, tokens) of the accepted real sentences
    for source_id in sorted(documents):
        if source_id not in draft:
            problems.append("text/%s.txt has no row in sources-draft.tsv" % source_id)
            continue
        reason = document_rejection(index, source_id, draft[source_id]["url"])
        if reason:
            rejected[source_id] = reason
            continue
        hits = sum(filt.key_hit(text) for _, text in documents[source_id])
        if hits >= DOCUMENT_KEY_HITS:
            rejected[source_id] = "%d sentences are also in a training or evaluation set" % hits
            continue
        for number, text in documents[source_id]:
            if text not in tokens_of or tokens_of[text][0] != 1:
                drops["not-one-sentence-or-not-tokenized"] += 1
                continue
            toks = tokens_of[text][1]
            key = sent_key_of(text)
            if not MIN_TOKENS <= len(toks) <= MAX_TOKENS:
                drops["length"] += 1
            elif key in seen_keys:
                drops["duplicate"] += 1
            elif filt.key_hit(text):
                drops["equals-a-set-sentence"] += 1
            elif filt.probe_hit(text):
                drops["probe-5gram"] += 1
            else:
                seen_keys.add(key)
                all_sentences.append((source_id, number, text, toks))
    for source_id, number, text, toks in all_sentences:
        for shape, i in shape_hits(toks, lex):
            pools[shape].append((rank_of(seed, shape, sent_key_of(text)), dict(shape=shape, role="target", target=i, lemma=lemma_of(toks[i], lex),
                                                                                origin="real", source_id=source_id, line=number, text=text,
                                                                                tokens=toks)))
    rows = []
    limit = int(math.ceil(target * over))
    chosen_lemmas = {shape: set() for shape in SHAPES}
    for shape in SHAPES:
        counts = collections.Counter()
        for _, row in sorted(pools[shape], key=lambda item: item[0]):
            if sum(counts.values()) >= limit:
                break
            if counts[row["lemma"]] < lemma_cap:
                counts[row["lemma"]] += 1
                rows.append(row)
        chosen_lemmas[shape] = set(counts)
    control_pool = {shape: [] for shape in SHAPES}
    for source_id, number, text, toks in all_sentences:
        for shape in SHAPES:
            for i in control_hits(toks, lex, shape, chosen_lemmas[shape]):
                control_pool[shape].append((rank_of(seed, "control", shape, sent_key_of(text)),
                                            dict(shape=shape, role="control", target=i, lemma=lemma_of(toks[i], lex), origin="real",
                                                 source_id=source_id, line=number, text=text, tokens=toks)))
    control_limit = int(math.ceil(CONTROLS * over / len(SHAPES)))
    for shape in SHAPES:
        counts = collections.Counter()
        for _, row in sorted(control_pool[shape], key=lambda item: item[0]):
            if sum(counts.values()) >= control_limit:
                break
            if counts[row["lemma"]] < CONTROL_LEMMA_CAP:
                counts[row["lemma"]] += 1
                rows.append(row)
    authored_rows, authored_problems = authored_candidates(work, lex, tokens_of, filt)
    problems += authored_problems
    rows += authored_rows
    rows.sort(key=lambda r: (SHAPES.index(r["shape"]), ROLES.index(r["role"]), ORIGINS.index(r["origin"]),
                             rank_of(seed, r["shape"], r["role"], sent_key_of(r["text"]))))
    for number, row in enumerate(rows, 1):
        row["cand_id"] = "c%05d" % number
    return rows, candidate_report(rows, drops, rejected, problems, target, draft, documents)


def authored_candidates(work, lex, tokens_of, filt):
    """(rows, problems) of the authored sentences: the target is the first token whose lemma is the lemma of the row."""
    rows, problems, seen = [], [], set()
    for shape, lemma, role, sentence in read_authored(work):
        if sentence not in tokens_of or tokens_of[sentence][0] != 1:
            problems.append("authored %s: a sentence is not tokenized as one sentence (run tokenize)" % shape)
            continue
        toks = tokens_of[sentence][1]
        key = sent_key_of(sentence)
        if not MIN_TOKENS <= len(toks) <= MAX_TOKENS or key in seen or filt.key_hit(sentence) or filt.probe_hit(sentence):
            problems.append("authored %s: sentence %s is too short or long, a duplicate, or equals or shares wording with an existing set"
                            % (shape, key))
            continue
        index = next((i for i, token in enumerate(toks) if token.lower() == lemma or lemma_of(token, lex) == lemma), None)
        if index is None:
            problems.append("authored %s: sentence %s does not contain the lemma '%s'" % (shape, key, lemma))
            continue
        seen.add(key)
        rows.append(dict(shape=shape, role=role, target=index, lemma=lemma, origin="authored", source_id=AUTHORED_ID, line=len(rows) + 1,
                         text=sentence, tokens=toks))
    return rows, problems


def candidate_report(rows, drops, rejected, problems, target, draft, documents):
    lines = ["# Candidates", "", "Counts only; no text of any evaluation set.", ""]
    lines += ["documents with text: %d, listed in the draft: %d, rejected: %d" % (len(documents), len(draft), len(rejected))]
    for source_id, reason in sorted(rejected.items()):
        lines.append("- rejected %s: %s" % (source_id, reason))
    lines += ["", "sentences dropped before the shape rules: " + (", ".join("%s %d" % item for item in sorted(drops.items())) or "none"), ""]
    lines += ["| shape | target real | target authored | control real | control authored | shortfall of real targets |",
              "| --- | --- | --- | --- | --- | --- |"]
    for shape in SHAPES:
        count = collections.Counter((r["role"], r["origin"]) for r in rows if r["shape"] == shape)
        short = max(0, int(math.ceil(target * OVER_COLLECT)) - count[("target", "real")])
        lines.append("| %s | %d | %d | %d | %d | %d |" % (shape, count[("target", "real")], count[("target", "authored")],
                                                         count[("control", "real")], count[("control", "authored")], short))
    if problems:
        lines += [""] + ["- PROBLEM: " + p for p in problems]
    return lines


def _command_candidates(args):
    work = adjudicate.Work(args.work)
    rows, report = collect_candidates(work, args.data_dir, not args.no_ud, args.target, args.over, args.lemma_cap, args.seed)
    for line in report:
        print(line)
    common.write_text(work.private_file("candidates-report.md"), report)
    write_candidates(work, rows)
    print("%d candidates written to candidates.tsv" % len(rows))
    return 0


def write_candidates(work, rows):
    adjudicate.write_rows(work_path(work, "candidates.tsv"), CANDIDATE_COLUMNS,
                          [(r["cand_id"], r["shape"], r["role"], r["target"], r["lemma"], r["origin"], r["source_id"], r["line"], r["text"],
                            " ".join(r["tokens"])) for r in rows])


def read_candidates(work):
    """[candidate dict] of W/candidates.tsv (target as int, tokens as a list)."""
    columns, records = adjudicate.read_records(work_path(work, "candidates.tsv"))
    if tuple(columns) != CANDIDATE_COLUMNS:
        raise ValueError("candidates.tsv: unexpected columns %r" % columns)
    for record in records:
        record["target"] = int(record["target"])
        record["tokens"] = record["tokens"].split(" ")
        record["sent_key"] = sent_key_of(record["text"])
    return records


# ---------------------------------------------------------------- blind batches

def unique_sentences(candidates):
    """{sent_key: (source_id, text, tokens)} in first-seen order (one entry per sentence, whatever number of items it carries)."""
    found = collections.OrderedDict()
    for row in candidates:
        found.setdefault(row["sent_key"], (row["source_id"], row["text"], row["tokens"]))
    return found


def make_batches(sentences, seed=SEED, size=adjudicate.BATCH_SENTENCES, budget=adjudicate.BATCH_ROWS):
    """[(batch name, [sent_key])]: the sentences shuffled with the seed, cut into batches of at most `size` sentences and `budget` rows."""
    keys = sorted(sentences)
    random.Random(seed).shuffle(keys)
    batches, current, rows = [], [], 0
    for key in keys:
        count = len(sentences[key][2])
        if current and (len(current) >= size or rows + count > budget):
            batches.append(("b%03d" % (len(batches) + 1), current))
            current, rows = [], 0
        current.append(key)
        rows += count
    if current:
        batches.append(("b%03d" % (len(batches) + 1), current))
    return batches


def _command_export(args):
    work = adjudicate.Work(args.work)
    sentences = unique_sentences(read_candidates(work))
    stale = [m for m in adjudicate.MODELS if os.path.isdir(work.out(m)) and os.listdir(work.out(m))]
    if stale and not args.force:
        print("refusing to export: tagger outputs exist under batches/out (they belong to the previous export); remove them or use --force")
        return 1
    ids = adjudicate.assign_ids(sentences, args.seed)
    batches = make_batches(sentences, args.seed, args.size, args.rows)
    if os.path.isdir(work.batches):
        for name in os.listdir(work.batches):
            if name.endswith(".tsv") and adjudicate.BATCH_RE.match(name[:-4]):
                os.remove(os.path.join(work.batches, name))
    key_rows = []
    for name, keys in batches:
        rows = []
        for key in keys:
            for position, token in enumerate(sentences[key][2]):
                rows.append((ids[key], str(position), adjudicate.escape_cell(token)))
            key_rows.append((ids[key], name, key, sentences[key][0], len(sentences[key][2])))
        adjudicate.write_batch(work.batch_file(name), rows)
    adjudicate.write_rows(work.private_file("key.tsv"), KEY_COLUMNS, key_rows)
    adjudicate.write_status(work, collections.OrderedDict((name, adjudicate.new_status_record(name)) for name, _ in batches))
    print("%d batches, %d sentences, %d token rows" % (len(batches), len(sentences), sum(len(s[2]) for s in sentences.values())))
    return 0


def read_key(work):
    columns, records = adjudicate.read_records(work.private_file("key.tsv"))
    if tuple(columns) != KEY_COLUMNS:
        raise ValueError("private/key.tsv: unexpected columns %r" % (columns,))
    return records


def check_batches(work, sentences, budget=adjudicate.BATCH_ROWS):
    """Errors of the exported batches against the key and the sentences; an empty list when the export is sound and blind."""
    errors = []
    key = read_key(work)
    by_batch = collections.defaultdict(list)
    for record in key:
        by_batch[record["batch"]].append(record)
    files = sorted(name for name in os.listdir(work.batches) if name.endswith(".tsv") and adjudicate.BATCH_RE.match(name[:-4]))
    if [name[:-4] for name in files] != sorted(by_batch):
        errors.append("the batch files and the batches of the key differ")
    seen = set()
    for name in files:
        batch = name[:-4]
        try:
            rows = adjudicate.read_batch(work.batch_file(batch))
        except ValueError as error:
            errors.append(str(error))
            continue
        if len(rows) > budget:
            errors.append("%s: %d rows exceed the budget %d" % (batch, len(rows), budget))
        expected = []
        for record in by_batch.get(batch, ()):
            if record["sent_key"] not in sentences:
                errors.append("%s: the key names a sentence that is not a candidate" % batch)
                continue
            expected.extend((record["opaque_id"], str(i), adjudicate.escape_cell(t)) for i, t in enumerate(sentences[record["sent_key"]][2]))
            if record["opaque_id"] in seen:
                errors.append("%s: id %s is used twice" % (batch, record["opaque_id"]))
            seen.add(record["opaque_id"])
        if any(not adjudicate.ID_RE.match(row[0]) for row in rows):
            errors.append("%s: an id is not an opaque id" % batch)
        if rows != expected:
            errors.append("%s: the rows differ from the key and the sentences (%d rows, expected %d)" % (batch, len(rows), len(expected)))
    return errors


def _command_check_batches(args):
    work = adjudicate.Work(args.work)
    errors = check_batches(work, unique_sentences(read_candidates(work)), args.rows)
    for message in errors[:args.limit]:
        print("  " + message)
    print("%d batch files checked: %s" % (len({k["batch"] for k in read_key(work)}), "FAILED" if errors else "passed"))
    return 1 if errors else 0


def _command_validate_output(args):
    work = adjudicate.Work(args.work)
    if not args.batch and not args.all:
        raise ValueError("give --batch BNNN or --all")
    models = (args.model,) if args.model else adjudicate.MODELS
    targets = [args.batch] if args.batch else sorted(adjudicate.read_status(work))
    failed = 0
    for batch in targets:
        for model in models:
            if args.all:
                files, problems = adjudicate.output_files(work.out(model), batch)
                if not files and not problems:
                    continue
            state, errors = adjudicate.validate_and_record(work, model, batch, count=not args.no_count)
            print("%s %s: %s" % (batch, model, state))
            for message in errors[:args.limit]:
                print("  " + message)
            failed += state == "invalid"
    return 1 if failed else 0


def _command_status(args):
    work = adjudicate.Work(args.work)
    status = adjudicate.read_status(work)
    counts = collections.Counter()
    for record in status.values():
        counts["opus " + record["opus_state"]] += 1
        counts["sol " + record["sol_state"]] += 1
    print("%d batches" % len(status))
    for name, count in sorted(counts.items()):
        print("  %-16s %d" % (name, count))
    for label, name in (("sources-draft", "sources-draft.tsv"), ("tokens", "tokens.tsv"), ("candidates", "candidates.tsv"),
                        ("decided items", os.path.join("private", "decided.tsv"))):
        print("  %-16s %s" % (label, "present" if os.path.exists(work_path(work, name)) else "missing"))
    return 0


# ---------------------------------------------------------------- decide

def decide_item(item, opus, sol):
    """(outcome, reason, agreed tag) of the judged word of an item from the two votes on it: keep, or drop with a reason.

    `item` has `shape` and `role`; `opus` and `sol` are the `Vote`s of the judged word. Different tags -> `target-disagreement`;
    the same tag in a class other than the intended one (also a tag that is neither noun nor verb) -> `class-mismatch`.
    """
    if opus.tag != sol.tag:
        return "drop", "target-disagreement", ""
    if tag_class(opus.tag) != intended_class(item["shape"], item["role"]):
        return "drop", "class-mismatch", opus.tag
    return "keep", "", opus.tag


def decide_token(opus, sol):
    """(tag, status cell) of a token that is not the judged word: agreed -> `unanimous`; else `disputed` with the first labeler's tag."""
    flags = adjudicate.effective_flags((), opus, sol)
    base = "unanimous" if opus.tag == sol.tag else "disputed"
    return opus.tag, common.format_status(base, flags)


def run_decisions(work, candidates):
    """(items, rows, incomplete): `items` is [dict of ITEM_COLUMNS], `rows` the token rows of every decided sentence.

    Only sentences of batches that both models completed and validated are decided; the others are `incomplete`
    (sentence keys). An item whose sentence was decided is `keep` or `drop` with a reason.
    """
    key = read_key(work)
    by_key = {record["sent_key"]: record for record in key}
    sentences = unique_sentences(candidates)
    votes = {}
    for batch in sorted({record["batch"] for record in key}):
        votes[batch] = adjudicate.read_outputs_by_id(work, batch)
    items, rows, incomplete = [], {}, set()
    for cand in candidates:
        record = by_key.get(cand["sent_key"])
        batch_votes = votes.get(record["batch"]) if record else None
        if batch_votes is None:
            incomplete.add(cand["sent_key"])
            continue
        opaque = record["opaque_id"]
        opus, sol = (batch_votes[model][opaque] for model in adjudicate.MODELS)
        target = cand["target"]
        outcome, reason, agreed = decide_item(cand, opus[target], sol[target])
        if cand["sent_key"] not in rows:
            rows[cand["sent_key"]] = [(i,) + decide_token(opus[i], sol[i]) for i in range(len(sentences[cand["sent_key"]][2]))]
        disputed = sum(1 for _, _, status in rows[cand["sent_key"]] if common.base_status(status) == "disputed")
        items.append(dict(cand_id=cand["cand_id"], sent_key=cand["sent_key"], shape=cand["shape"], role=cand["role"], target=cand["target"],
                          lemma=cand["lemma"], origin=cand["origin"], source_id=cand["source_id"], outcome=outcome, reason=reason,
                          opus_tag=opus[target].tag, sol_tag=sol[target].tag, agreed_tag=agreed, disputed_tokens=disputed))
    row_list = []
    for sent_key, tagged in rows.items():
        forms = sentences[sent_key][2]
        row_list += [(sent_key, i, forms[i], tag, status) for i, tag, status in tagged]
    return items, row_list, sorted(incomplete)


def decide_report(items, incomplete):
    lines = ["# Decisions", "", "| shape | role | items | kept | target-disagreement | class-mismatch | incomplete sentences |",
             "| --- | --- | --- | --- | --- | --- | --- |"]
    for shape in SHAPES:
        for role in ROLES:
            members = [i for i in items if i["shape"] == shape and i["role"] == role]
            count = collections.Counter(i["reason"] or "kept" for i in members)
            lines.append("| %s | %s | %d | %d | %d | %d | |" % (shape, role, len(members), count["kept"], count["target-disagreement"],
                                                               count["class-mismatch"]))
    lines += ["", "incomplete sentences (not decided): %d" % len(incomplete),
              "disputed non-target tokens in kept items: %d" % sum(i["disputed_tokens"] for i in items if i["outcome"] == "keep")]
    return lines


def _command_decide(args):
    work = adjudicate.Work(args.work)
    items, rows, incomplete = run_decisions(work, read_candidates(work))
    report = decide_report(items, incomplete)
    adjudicate.write_rows(work.private_file("decided.tsv"), ITEM_COLUMNS, [[i[c] for c in ITEM_COLUMNS] for i in items])
    adjudicate.write_rows(work.private_file("decided-rows.tsv"), ROW_COLUMNS, rows)
    common.write_text(work.private_file("decide-report.md"), report)
    adjudicate._print(report)
    return 0


def read_decided(work):
    """([item dict], {sent_key: [(form, tag, status)]}) of private/decided.tsv and decided-rows.tsv."""
    _, records = adjudicate.read_records(work.private_file("decided.tsv"))
    for record in records:
        record["target"] = int(record["target"])
        record["disputed_tokens"] = int(record["disputed_tokens"])
    _, row_records = adjudicate.read_records(work.private_file("decided-rows.tsv"))
    rows = collections.defaultdict(list)
    for record in row_records:
        rows[record["sent_key"]].append((int(record["index"]), record["form"], record["tag"], record["status"]))
    return records, {key: [cells[1:] for cells in sorted(cells_list)] for key, cells_list in rows.items()}


# ---------------------------------------------------------------- review sample (the precision gate)

REVIEW_INSTRUCTIONS = [
    "# Review items", "",
    "Each item shows a sentence and one marked word with a proposed UPOS tag. Judge ONLY whether the proposed tag is correct for the",
    "marked word in this sentence according to data/CONVENTIONS.md (read it in full first). Answer with one line per item, tab",
    "separated, and nothing else: `item<TAB>verdict<TAB>cite` where the verdict is `correct`, `incorrect` or `unclear` (the conventions",
    "do not decide it), and the cite names the rule of CONVENTIONS you applied in at most 20 words. Do not look at any other file and",
    "do not search for the sentences.", "",
]


def build_sample(items, seed=SEED, per_shape=12, controls=4):
    """[(kind, item, shown tag)] shuffled: `per_shape` kept target items of every shape, plus `controls` items whose tag is swapped.

    A control item shows the other class of the agreed tag (NOUN for VERB and the reverse), so the right verdict is `incorrect`;
    a reviewer that accepts more than CONTROLS_ACCEPTED_MAX of them is too lenient.
    """
    rng = random.Random(seed)
    kept = [i for i in items if i["outcome"] == "keep" and i["role"] == "target"]
    chosen, rest = [], []
    for shape in SHAPES:
        members = sorted((i for i in kept if i["shape"] == shape), key=lambda i: i["cand_id"])
        picked = rng.sample(members, min(per_shape, len(members)))
        chosen += picked
        rest += [i for i in members if i not in picked]
    result = [("real", i, i["agreed_tag"]) for i in chosen]
    swapped = rng.sample(sorted(rest, key=lambda i: i["cand_id"]), min(controls, len(rest)))
    result += [("control", i, "VERB" if tag_class(i["agreed_tag"]) == "Noun" else "NOUN") for i in swapped]
    rng.shuffle(result)
    return result


def sample_lines(sample, sentences, prefix="R"):
    lines = list(REVIEW_INSTRUCTIONS)
    keys = []
    for number, (kind, item, shown) in enumerate(sample, 1):
        name = "%s%03d" % (prefix, number)
        words = ["[[%s]]" % w if i == item["target"] else w for i, w in enumerate(sentences[item["sent_key"]][2])]
        lines += ["## " + name, "", "Sentence: " + " ".join(words), "Word: %s (index %d)" % (sentences[item["sent_key"]][2][item["target"]],
                                                                                           item["target"]),
                  "Proposed tag: " + shown, ""]
        keys.append((name, kind, item["cand_id"], item["shape"], item["target"], shown, item["agreed_tag"]))
    return lines, keys


def _command_sample(args):
    work = adjudicate.Work(args.work)
    items, _ = read_decided(work)
    sentences = unique_sentences(read_candidates(work))
    sample = build_sample(items, args.seed, args.per_shape, args.controls)
    lines, keys = sample_lines(sample, sentences)
    common.write_text(work.private_file("review-sample.md"), lines)
    adjudicate.write_rows(work.private_file("review-sample-key.tsv"), SAMPLE_KEY_COLUMNS, keys)
    real = sum(1 for kind, _, _ in sample if kind == "real")
    print("%d items: %d real, %d control" % (len(sample), real, len(sample) - real))
    return 0


def review_statistics(keys, verdicts):
    """(per-shape Counter, control Counter) of the verdicts over the sample key rows."""
    answers = {v.item: v.verdict for v in verdicts}
    per_shape, controls = collections.defaultdict(collections.Counter), collections.Counter()
    for row in keys:
        verdict = answers.get(row["item"])
        if verdict is None:
            continue
        if row["kind"] == "control":
            controls[verdict] += 1
        else:
            per_shape[row["shape"]][verdict] += 1
    return per_shape, controls


def review_precision(counter):
    judged = counter["correct"] + counter["incorrect"]
    return counter["correct"] / judged if judged else None


def review_lines(per_shape, controls):
    """(lines, passed): the Markdown result of the model review and whether the gate (precision of at least 95%) holds."""
    total = collections.Counter()
    lines = ["# Review result", "", "| shape | correct | incorrect | unclear | precision |", "| --- | --- | --- | --- | --- |"]
    for shape in SHAPES:
        counter = per_shape.get(shape, collections.Counter())
        value = review_precision(counter)
        lines.append("| %s | %d | %d | %d | %s |" % (shape, counter["correct"], counter["incorrect"], counter["unclear"],
                                                    "-" if value is None else "%.1f%%" % (100 * value)))
        total.update(counter)
    overall = review_precision(total)
    lenient = controls["correct"] > CONTROLS_ACCEPTED_MAX
    passed = overall is not None and overall >= PRECISION_MIN and not lenient
    lines += ["", "- reviewed target items: %d (correct %d, incorrect %d, unclear %d)" % (sum(total.values()), total["correct"],
                                                                                         total["incorrect"], total["unclear"]),
              "- overall precision: %s (gate: at least %.0f%%)" % ("-" if overall is None else "%.1f%%" % (100 * overall), 100 * PRECISION_MIN),
              "- control items accepted as correct: %d of %d%s" % (controls["correct"], sum(controls.values()),
                                                                  " (TOO LENIENT, repeat the review)" if lenient else ""),
              "- gate: " + ("PASSED" if passed else "STOP: do not publish; report the result")]
    return lines, passed


def _command_review_result(args):
    work = adjudicate.Work(args.work)
    columns, keys = adjudicate.read_records(work.private_file("review-sample-key.tsv"))
    verdicts = []
    for name in args.verdicts:
        with open(name, "r", encoding="utf-8") as handle:
            verdicts += adjudicate.parse_verdicts(handle.read())
    unknown = {v.item for v in verdicts} - {k["item"] for k in keys}
    if unknown:
        raise ValueError("verdicts for unknown items: " + ", ".join(sorted(unknown)[:5]))
    per_shape, controls = review_statistics(keys, verdicts)
    lines, passed = review_lines(per_shape, controls)
    missing = {k["item"] for k in keys} - {v.item for v in verdicts}
    lines.append("- unanswered items: %d" % len(missing))
    adjudicate._print(lines)
    common.write_text(work.private_file("review-result.md"), lines)
    return 0 if passed and not missing else 1


# ---------------------------------------------------------------- build

class Taker:
    """Takes items in rank order while a per-lemma cap, a total and a set of already used sentences allow."""

    def __init__(self, lemma_cap, used):
        self.lemma_cap = lemma_cap
        self.used = used
        self.counts = collections.Counter()

    def take(self, pool, limit):
        taken = []
        for item in pool:
            if len(taken) >= limit:
                break
            if item["sent_key"] in self.used or self.counts[item["lemma"]] >= self.lemma_cap:
                continue
            self.used.add(item["sent_key"])
            self.counts[item["lemma"]] += 1
            taken.append(item)
        return taken


def authored_room(real, share):
    """The most authored items that keep their share of (real + authored) at or below `share`."""
    return int(math.floor(real * share / (1.0 - share) + 1e-9))


def select_items(items, seed=SEED, per_shape=TARGET_PER_SHAPE, lemma_cap=LEMMA_CAP, share=AUTHORED_SHARE, controls=CONTROLS):
    """(targets, controls): the kept items chosen for the set.

    Per shape: real targets in seeded rank order up to `per_shape` (at most `lemma_cap` per lemma), then authored ones up to the
    free places and up to the authored share of the shape; a shape that real text cannot fill is not padded. Controls: only for a
    lemma of a chosen target of the same shape, at most CONTROL_LEMMA_CAP per lemma, real first (the quota per shape, then any
    shape), then authored up to the authored share of the control stratum. A sentence is used once.
    """
    used = set()
    kept = [i for i in items if i["outcome"] == "keep"]

    def ordered(shape, role, origin):
        pool = [i for i in kept if i["shape"] == shape and i["role"] == role and i["origin"] == origin]
        return sorted(pool, key=lambda i: rank_of(seed, "select", i["cand_id"]))

    targets = []
    for shape in SHAPES:
        taker = Taker(lemma_cap, used)
        real = taker.take(ordered(shape, "target", "real"), per_shape)
        authored = taker.take(ordered(shape, "target", "authored"), min(per_shape - len(real), authored_room(len(real), share)))
        targets += real + authored
    lemmas = {shape: {i["lemma"] for i in targets if i["shape"] == shape} for shape in SHAPES}

    def control_pool(shape, origin):
        return [i for i in ordered(shape, "control", origin) if i["lemma"] in lemmas[shape]]

    chosen = []
    quota = int(math.ceil(controls / float(len(SHAPES))))
    takers = {shape: Taker(CONTROL_LEMMA_CAP, used) for shape in SHAPES}
    for shape in SHAPES:
        chosen += takers[shape].take(control_pool(shape, "real"), quota)
    for shape in SHAPES:                      # fill from any shape when a shape had fewer real controls than its quota
        chosen += takers[shape].take(control_pool(shape, "real"), max(0, controls - len(chosen)))
    real_count = len(chosen)
    room = min(controls - len(chosen), authored_room(real_count, share))
    pools = {shape: control_pool(shape, "authored") for shape in SHAPES}
    added = 0
    progress = True
    while added < room and progress:           # one authored control per shape in turn, so no shape is favored
        progress = False
        for shape in SHAPES:
            if added < room:
                taken = takers[shape].take(pools[shape], 1)
                chosen += taken
                added += len(taken)
                progress = progress or bool(taken)
    return targets, chosen


def final_sentence(item, tagged_rows, number):
    """The sentence dict of an item for common.write_corpus (tokens, tags, status, and the extra comments of the judged word)."""
    return {"sent_id": "shapes-dev-%04d" % number, "source_id": item["source_id"], "text": item["text"],
            "rows": [[form, tag, status] for form, tag, status in tagged_rows],
            "extra": ["# shape = " + item["shape"], "# role = " + item["role"], "# target = %d" % item["target"],
                      "# lemma = " + item["lemma"], "# origin = " + item["origin"]]}


def sentence_problems(sentence, extra=None):
    """Problems of one built sentence: the rules of the data test (target in range and in the right class, never disputed, text rejoins)."""
    values = dict(part[2:].split(" = ", 1) for part in (extra if extra is not None else sentence["extra"]))
    problems = []
    rows = sentence["rows"]
    shape, role = values.get("shape"), values.get("role")
    if shape not in SHAPES or role not in ROLES or values.get("origin") not in ORIGINS:
        return ["%s: shape, role or origin is missing or unknown" % sentence["sent_id"]]
    if (values["origin"] == "authored") != (sentence["source_id"] == AUTHORED_ID):
        problems.append("%s: origin %s does not match the document %s" % (sentence["sent_id"], values["origin"], sentence["source_id"]))
    if not values.get("target", "").isdigit() or int(values["target"]) >= len(rows):
        return problems + ["%s: the target is not a token index" % sentence["sent_id"]]
    target = rows[int(values["target"])]
    if tag_class(target[1]) != intended_class(shape, role):
        problems.append("%s: the judged word is tagged %s, not %s" % (sentence["sent_id"], target[1], intended_class(shape, role)))
    if common.base_status(target[2]) == "disputed":
        problems.append("%s: the judged word is disputed" % sentence["sent_id"])
    if "".join(r[0] for r in rows) != sentence["text"].replace(" ", ""):
        problems.append("%s: the tokens do not rejoin to the text" % sentence["sent_id"])
    return problems


def sources_rows(draft, built):
    """The rows of the final sources.tsv: the used draft documents with their sentence counts, and the authored pseudo-document."""
    counts = collections.Counter(s["source_id"] for s in built)
    rows = []
    for row in sorted(draft, key=lambda r: r["source_id"]):
        if counts[row["source_id"]]:
            row = dict(row)
            row["sentences"] = str(counts[row["source_id"]])
            rows.append([row[c] for c in SOURCE_COLUMNS])
    authored = sorted(s["text"] for s in built if s["source_id"] == AUTHORED_ID)
    if authored:
        text_hash = common.sha256_bytes("\n".join(authored).encode("utf-8") + b"\n")
        rows.append([AUTHORED_ID, AUTHORED_FAMILY, "Authored top-up sentences of the shapes development set", "",
                     "Shapes development set (written by the project, labeled by two model labelers)", "2026", AUTHORED_LICENSE,
                     AUTHORED_TERMS, "", "", text_hash, str(counts[AUTHORED_ID])])
    return rows


def build(work, out_dir, seed=SEED, per_shape=TARGET_PER_SHAPE, lemma_cap=LEMMA_CAP, share=AUTHORED_SHARE, controls=CONTROLS):
    """Write dev.tsv and sources.tsv into `out_dir`; returns (sentences, report lines)."""
    items, tagged = read_decided(work)
    draft = read_draft(work)
    targets, chosen = select_items(items, seed, per_shape, lemma_cap, share, controls)
    order = {shape: n for n, shape in enumerate(SHAPES)}
    selected = sorted(targets + chosen, key=lambda i: (order[i["shape"]], ROLES.index(i["role"]), rank_of(seed, "order", i["cand_id"])))
    text_of = {c["cand_id"]: c["text"] for c in read_candidates(work)}
    built = []
    for number, item in enumerate(selected, 1):
        item = dict(item, text=text_of[item["cand_id"]])
        built.append(final_sentence(item, tagged[item["sent_key"]], number))
    problems = [p for s in built for p in sentence_problems(s)]
    if problems:
        raise ValueError("the built set is invalid:\n  " + "\n  ".join(problems[:20]))
    unknown = sorted({s["source_id"] for s in built} - {r["source_id"] for r in draft} - {AUTHORED_ID})
    if unknown:
        raise ValueError("no source row for: " + ", ".join(unknown))
    common.write_corpus(os.path.join(out_dir, "dev.tsv"), common.with_flag_documentation(HEADER, built), built)
    adjudicate.write_rows(os.path.join(out_dir, "sources.tsv"), SOURCE_COLUMNS, sources_rows(draft, built))
    report = ["# Build", "", "%d sentences written to dev.tsv" % len(built)]
    return built, report


def _command_build(args):
    work = adjudicate.Work(args.work)
    out_dir = args.out_dir or os.path.join(args.data_dir, "eval", "shapes-dev")
    built, report = build(work, out_dir, args.seed, args.per_shape, args.lemma_cap, args.share, args.controls)
    adjudicate._print(report)
    common.write_text(work.private_file("build-report.md"), report)
    return 0


# ---------------------------------------------------------------- report

def report_lines(out_dir, work=None):
    """Markdown counts of the built set for the README: per shape real and authored targets and controls, documents, drops."""
    _, sentences = common.read_corpus(os.path.join(out_dir, "dev.tsv"))
    count = collections.Counter()
    for sentence in sentences:
        comments = sentence["comments"]
        count[(comments["shape"], comments["role"], comments["origin"])] += 1
    lines = ["| shape | target items | of which authored | controls | of which authored |", "| --- | --- | --- | --- | --- |"]
    for shape in SHAPES:
        targets = count[(shape, "target", "real")] + count[(shape, "target", "authored")]
        controls = count[(shape, "control", "real")] + count[(shape, "control", "authored")]
        lines.append("| %s | %d | %d | %d | %d |" % (shape, targets, count[(shape, "target", "authored")], controls,
                                                     count[(shape, "control", "authored")]))
    disputed = sum(1 for s in sentences for r in s["rows"] if common.base_status(r[2]) == "disputed")
    tokens = sum(len(s["rows"]) for s in sentences)
    documents = {s["source_id"] for s in sentences}
    lines += ["", "- sentences: %d, tokens: %d, disputed tokens: %d, source documents: %d (authored pseudo-document included)"
              % (len(sentences), tokens, disputed, len(documents))]
    if work is not None and os.path.exists(work.private_file("decided.tsv")):
        items, _ = read_decided(work)
        drops = collections.Counter((i["shape"], i["reason"]) for i in items if i["outcome"] == "drop")
        lines += ["", "| shape | dropped: target disagreement | dropped: class mismatch |", "| --- | --- | --- |"]
        for shape in SHAPES:
            lines.append("| %s | %d | %d |" % (shape, drops[(shape, "target-disagreement")], drops[(shape, "class-mismatch")]))
    return lines


def _command_report(args):
    out_dir = args.out_dir or os.path.join(args.data_dir, "eval", "shapes-dev")
    adjudicate._print(report_lines(out_dir, adjudicate.Work(args.work)))
    return 0


# ---------------------------------------------------------------- command line

def main(argv=None):
    parser = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    parser.add_argument("--work", required=True, help="work folder (outside the repository)")
    parser.add_argument("--data-dir", default=common.DATA, help="data folder (default: the repository's data/)")
    sub = parser.add_subparsers(dest="command", required=True)
    exclusions = sub.add_parser("exclusions", help="index of the documents training and evaluation used; checks the draft")
    exclusions.add_argument("--check", nargs="*", default=None, help="document ids or URLs to check")
    tokenize = sub.add_parser("tokenize", help="tokens of every sentence through the C# command shapes-tokenize")
    tokenize.add_argument("--cmd", default=None, help="command prefix that is followed by shapes-tokenize --in F --out F")
    candidates = sub.add_parser("candidates", help="pre-select the candidates of every shape; apply the leakage filters")
    candidates.add_argument("--no-ud", action="store_true", help="ignore the UD cache even when present")
    candidates.add_argument("--target", type=int, default=TARGET_PER_SHAPE)
    candidates.add_argument("--over", type=float, default=OVER_COLLECT)
    candidates.add_argument("--lemma-cap", type=int, default=LEMMA_CAP)
    candidates.add_argument("--seed", type=int, default=SEED)
    export = sub.add_parser("export", help="write the blind batches, the key and status.tsv")
    export.add_argument("--size", type=int, default=adjudicate.BATCH_SENTENCES)
    export.add_argument("--rows", type=int, default=adjudicate.BATCH_ROWS)
    export.add_argument("--seed", type=int, default=SEED)
    export.add_argument("--force", action="store_true")
    check = sub.add_parser("check-batches", help="re-read the batches: 3 columns, tokens equal the key, no leak")
    check.add_argument("--rows", type=int, default=adjudicate.BATCH_ROWS)
    check.add_argument("--limit", type=int, default=20)
    validate = sub.add_parser("validate-output", help="validate tagger outputs and record the state in status.tsv")
    validate.add_argument("--batch", default=None)
    validate.add_argument("--model", choices=adjudicate.MODELS, default=None)
    validate.add_argument("--all", action="store_true")
    validate.add_argument("--no-count", action="store_true")
    validate.add_argument("--limit", type=int, default=5)
    sub.add_parser("status", help="batch states and the presence of the pipeline files")
    sub.add_parser("decide", help="apply the decision rule; write private/decided.tsv and decided-rows.tsv")
    sample = sub.add_parser("sample", help="the review sample for the precision gate")
    sample.add_argument("--seed", type=int, default=SEED)
    sample.add_argument("--per-shape", type=int, default=12)
    sample.add_argument("--controls", type=int, default=4)
    review = sub.add_parser("review-result", help="precision of the review; exit 1 below 95%")
    review.add_argument("--verdicts", nargs="+", required=True)
    build_parser = sub.add_parser("build", help="write dev.tsv and sources.tsv")
    build_parser.add_argument("--out-dir", default=None, help="default: <data dir>/eval/shapes-dev")
    build_parser.add_argument("--seed", type=int, default=SEED)
    build_parser.add_argument("--per-shape", type=int, default=TARGET_PER_SHAPE)
    build_parser.add_argument("--lemma-cap", type=int, default=LEMMA_CAP)
    build_parser.add_argument("--share", type=float, default=AUTHORED_SHARE, help="largest authored share of a shape and of the controls")
    build_parser.add_argument("--controls", type=int, default=CONTROLS)
    report = sub.add_parser("report", help="counts of the built set for the README")
    report.add_argument("--out-dir", default=None)
    args = parser.parse_args(argv)
    commands = {"exclusions": _command_exclusions, "tokenize": _command_tokenize, "candidates": _command_candidates,
                "export": _command_export, "check-batches": _command_check_batches, "validate-output": _command_validate_output,
                "status": _command_status, "decide": _command_decide, "sample": _command_sample, "review-result": _command_review_result,
                "build": _command_build, "report": _command_report}
    if args.data_dir != common.DATA:
        common.DATA = args.data_dir
    return commands[args.command](args)


if __name__ == "__main__":
    sys.exit(main())

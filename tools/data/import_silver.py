"""BERT silver data: sentences from public-domain / CC0 text, tagged by a model, mapped to UPOS by documented rules.

  python tools/data/import_silver.py export-spike --spike-dir DIR [--include-faa]
  python tools/data/import_silver.py derive            # recompute upos/status of data/silver/silver.tsv from the source_tag column
  python tools/data/import_silver.py verify            # silver.tsv is exactly what `derive` produces; ids and sources are consistent
  python tools/data/import_silver.py verify-sources    # optional, needs network: Tatoeba ids against the CC0 export, Gutenberg landing pages

`export-spike` is a ONE-OFF step that reads the committed per-token labels of the perceptron spike (silver text plus the
per-token class and class probability of the BertBaseCased model, WordSense commit 6530de19, model file SHA-256 dcc10a64...),
which cannot be reproduced without WordSense and its model; everything after it (`derive`, `verify`) is deterministic.
Leakage rule (leakage.py): a sentence whose normalized key equals an evaluation sentence key of at least 25 characters, or a
document whose id is an evaluation document id, excludes the whole document (excluded-documents.tsv); a shorter equal
sentence ("Thank you.") is dropped alone (dropped-sentences.tsv): the sentences of a book are independent, and a
Tatoeba document is one sentence anyway. Order of regeneration: import_masc.py, build_lexicon.py (attests tags from MASC),
then export-spike (the capitalized-noun rule reads the lexicon).
--spike-dir is the spike folder that holds data/silver/chunks/silver{0..3}.tok, out/dump/Bsilver{0..3}.tsv and
data/silver/{gutenberg-books.json,silver-text-stats.json}. FAA text is never committed: --include-faa writes the
git-ignored data/silver/silver-faa.tsv for local experiments and leaves silver.tsv alone (FAA text is excluded until the
project owner approves it).

The source_tag column holds what `derive` needs: `L:PP` (the model's class letter and its probability in percent;
class letters N R V J B P C I D A, T = infinitive `to`, O = not/n't), `?` (the model gave no answer) or `-` (the token has
no letter or contains a digit: decided by shape alone); a token that was split off a contraction carries `@s`
(stem) or `@c` (clitic) after the tag of the joined token.
"""
import argparse
import collections
import csv
import json
import os
import re
import sys
import time

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import common
import leakage

LETTERS = "NRVJBPCIDA"
SYMBOL_CHARS = set("%$+=<>#@&")
CCONJ_WORDS = {"and", "or", "but", "nor"}
MASKED_C_WORDS = {"for", "yet", "so", "either", "neither", "both", "plus"}
BE_HAVE_STEMS = {"is", "are", "was", "were", "ai", "has", "have", "had", "am", "be"}
CLITIC_AUX = {"'ve", "'ll", "'d"}
THAT_WHAT = {"that", "what"}
MIN_PERCENT = 50
SILVER_HEADER = [
    "# corpus = DemaConsulting.PartOfSpeech BERT silver (tools/data/import_silver.py); sentences: Tatoeba (CC0) and Project Gutenberg (public domain in the USA)",
    "# license = CC0-1.0 for labels and selection; sentences CC0 (Tatoeba export) or public domain in the USA (Gutenberg), see sources.tsv",
    "# columns = form, upos, status, source_tag",
    "# status = silver (UPOS derived by rule from the model label) | masked (no training signal; upos is _)",
    "# source_tag = model class letter and probability in percent (L:PP), ? (no model answer) or - (decided by token shape); @s stem or @c clitic of a split contraction",
    "# tokens = the sentence text is the tokens joined by single spaces (the original text is not kept); tokenization = corpus convention (clitics split)",
]


# ---------------------------------------------------------------- derivation (deterministic, unit tested)

def parse_tag(source_tag):
    """(letter or '?' or '-', percent or None, role '' | 's' | 'c')"""
    role = ""
    if "@" in source_tag:
        source_tag, role = source_tag.split("@")
    if source_tag in ("?", "-"):
        return source_tag, None, role
    letter, percent = source_tag.split(":")
    return letter, int(percent), role


def class_to_upos(letter, low):
    """Table of data/CONVENTIONS.md section 'BERT silver': model class -> UPOS (None = masked)."""
    if letter == "N":
        return "NOUN"
    simple = {"R": "PRON", "V": "VERB", "J": "ADJ", "B": "ADV", "P": "ADP", "I": "INTJ", "D": "DET", "A": "AUX",
              "T": "PART", "O": "PART"}
    if letter in simple:
        return simple[letter]
    if letter == "C":
        if low in CCONJ_WORDS:
            return "CCONJ"
        return None if low in MASKED_C_WORDS else "SCONJ"
    return None


def derive_sentence(forms, tags, lexicon):
    """UPOS and status of every token of a sentence from forms and source tags (lexicon: common.read_lexicon)."""
    first = next((i for i, f in enumerate(forms) if common.has_letter_or_digit(f)), 0)
    out = []
    for index, (form, tag) in enumerate(zip(forms, tags)):
        letter, percent, role = parse_tag(tag)
        low = form.lower()
        capital = form[:1].isupper()
        upos = None
        if role == "c":
            if low == "n't" or low == "not":
                upos = "PART"
            elif low in CLITIC_AUX:
                upos = "AUX"
        elif letter == "-":
            if not common.has_letter_or_digit(form):
                upos = "SYM" if form in SYMBOL_CHARS else "PUNCT"
            elif common.is_number(form):
                upos = "NUM"
        elif letter == "?":
            upos = "PROPN" if capital and index > first else None
        elif percent is not None and percent >= MIN_PERCENT:
            upos = stem_or_word(form, low, letter, role, capital, index > first, lexicon, forms, index)
        out.append((upos, "silver") if upos else ("_", "masked"))
    return out


def stem_or_word(form, low, letter, role, capital, mid_sentence, lexicon, forms, index):
    if role == "s":
        following = forms[index + 1].lower() if index + 1 < len(forms) else ""
        if following == "n't":
            if low in BE_HAVE_STEMS:
                return None  # helper or main verb cannot be told apart without a parse
            return "AUX" if letter == "A" else None
        if low in THAT_WHAT and letter == "D":
            return "PRON"
    if letter == "N" and capital:
        entry = lexicon.get(low)
        if entry is not None and "NOUN" in entry:
            return None  # sentence-initial or archaic capital: name or noun cannot be told apart
        return "PROPN"
    return class_to_upos(letter, low)


# ---------------------------------------------------------------- export of the spike labels

def read_dump(path):
    """Per-token (doc, sid, form, sel, probabilities) of a model dump (tab separated, no header)."""
    rows = []
    with open(path, "r", encoding="utf-8", newline="") as handle:
        for line in handle:
            cells = line.rstrip("\n").split("\t")
            rows.append((cells[0], cells[1], cells[3], int(cells[6]), [float(x) for x in cells[8:18]]))
    return rows


def read_tok(path):
    """Sentences of a spike token file as (doc, sid, [forms])."""
    sentences, key, current = [], None, None
    with open(path, "r", encoding="utf-8", newline="") as handle:
        for line in handle:
            cells = line.rstrip("\n").split("\t")
            if (cells[0], cells[1]) != key:
                key = (cells[0], cells[1])
                current = (cells[0], cells[1], [])
                sentences.append(current)
            current[2].append(cells[2])
    return sentences


def joined_labels(forms, dump):
    """Class label and probability per token of the reference tokenization (rules of the spike's silver_label.py)."""
    sel = [row[3] for row in dump]
    labels = []
    for i, form in enumerate(forms):
        low = form.lower()
        s = sel[i]
        if not any(c.isalnum() for c in form) or any(c.isdigit() for c in form):
            labels.append("-")
        elif s >= 0:
            probability = int(round(dump[i][4][s] * 100))
            letter = LETTERS[s]
            if low in ("not", "n't"):
                letter = "O"
            elif low == "to" and s == 5:
                j = i + 1
                while j < len(forms) and sel[j] == 4:
                    j += 1
                letter = "T" if j < len(forms) and sel[j] in (2, 9) else "P"
            labels.append("%s:%d" % (letter, probability))
        else:
            labels.append("?")
    return labels


def split_sentence(forms, labels):
    """Apply the corpus clitic convention; returns (forms, tags)."""
    out_forms, out_tags = [], []
    for form, label in zip(forms, labels):
        pieces = common.split_clitics(form)
        if len(pieces) == 1 or label == "-":
            out_forms.append(form)
            out_tags.append(label)
        else:
            out_forms.extend(pieces)
            out_tags.extend([label + "@s", label + "@c"])
    return out_forms, out_tags


def doc_name(doc):
    return doc[len("silver:"):] if doc.startswith("silver:") else doc


def export_spike(spike_dir, include_faa):
    lexicon = common.read_lexicon(os.path.join(common.DATA, "lexicon", "lexicon.tsv"))
    keys, doc_ids = leakage.evaluation_keys(include_ud=True)
    train_keys = {common.key(s["text"]) for s in common.read_corpus(os.path.join(common.DATA, "open", "train.tsv"))[1]}
    train_keys = {k for k in train_keys if len(k) >= leakage.DOCUMENT_KEY_LENGTH}
    kept, faa = [], []
    removed = collections.defaultdict(collections.Counter)    # documents dropped whole (evaluation overlap)
    duplicates = collections.defaultdict(collections.Counter)  # sentences dropped alone (duplicate of open train)
    short = []                                                  # (sentence id, set, document) dropped alone (short evaluation duplicate)
    for chunk in range(4):
        sentences = read_tok(os.path.join(spike_dir, "data", "silver", "chunks", "silver%d.tok" % chunk))
        dump = read_dump(os.path.join(spike_dir, "out", "dump", "Bsilver%d.tsv" % chunk))
        position = 0
        for doc, sid, forms in sentences:
            part = dump[position:position + len(forms)]
            position += len(forms)
            if [p[2] for p in part] != forms:
                raise ValueError("dump and token file differ at " + sid)
            name = doc_name(doc)
            split_forms, tags = split_sentence(forms, joined_labels(forms, part))
            sentence = {"sent_id": sid, "source_id": name, "text": " ".join(split_forms),
                        "rows": [[f, "_", "silver", t] for f, t in zip(split_forms, tags)]}
            if name.startswith("faa-"):
                faa.append(sentence)
                continue
            found = leakage.match(sentence["text"], keys)
            k = common.key(sentence["text"])
            if name in doc_ids:
                removed[name]["document-id:" + doc_ids[name]] += 1
            elif found is not None and found[1] == "document":
                removed[name][found[0]] += 1
            elif found is not None:
                short.append((sid, found[0], name))
            elif k in train_keys:
                duplicates[name]["open-train"] += 1
            else:
                kept.append(sentence)
        if position != len(dump):
            raise ValueError("dump has more tokens than the token file")
    final = [s for s in kept if s["source_id"] not in removed]
    write_silver(os.path.join(common.DATA, "silver", "silver.tsv"), final, lexicon)
    write_sources(spike_dir, final, removed, duplicates, [(sid, label) for sid, label, name in short if name not in removed])
    if include_faa:
        write_silver(os.path.join(common.DATA, "silver", "silver-faa.tsv"), faa, lexicon)
    print("sentences: %d, documents: %d, tokens: %d; documents removed by this run's leakage check: %d; "
          "sentences dropped as open-train duplicates: %d; short sentences dropped as evaluation duplicates: %d"
          % (len(final), len({s["source_id"] for s in final}), sum(len(s["rows"]) for s in final), len(removed),
             sum(sum(c.values()) for c in duplicates.values()), len([1 for _, _, n in short if n not in removed])))


def write_silver(path, sentences, lexicon):
    for sentence in sentences:
        forms = [r[0] for r in sentence["rows"]]
        tags = [r[3] for r in sentence["rows"]]
        for row, (upos, status) in zip(sentence["rows"], derive_sentence(forms, tags, lexicon)):
            # the derivation decides the status; the flags of the row (mentioned, foreign) are token facts and are kept
            row[1], row[2] = upos, common.format_status(status, common.parse_status(row[2])[1])
    common.write_corpus(path, common.with_flag_documentation(SILVER_HEADER, sentences), sentences)


def clean(text):
    return re.sub(r"\s+", " ", text).strip()


def write_sources(spike_dir, sentences, removed, duplicates, dropped):
    counts = collections.Counter(s["source_id"] for s in sentences)
    books = {"gutenberg-" + b["id"]: b for b in json.load(open(os.path.join(spike_dir, "data", "silver", "gutenberg-books.json"), encoding="utf-8"))}
    lines = ["# Sources of silver.tsv: one row per document (Tatoeba sentence or Project Gutenberg book)",
             "# columns = source_id, family, title, author, url, license_basis, sentences"]
    for name in sorted(counts):
        if name.startswith("tatoeba-"):
            lines.append("\t".join((name, "tatoeba", "Tatoeba sentence " + name[8:], "Tatoeba contributors",
                                    "https://tatoeba.org/en/sentences/show/" + name[8:],
                                    "CC0-1.0 (sentence in the official Tatoeba CC0 export)", str(counts[name]))))
        else:
            book = books[name]
            lines.append("\t".join((name, "gutenberg", clean(book["title"]), clean(book["authors"]),
                                    "https://www.gutenberg.org/ebooks/" + name[10:],
                                    "US public domain: author born 1790 or later and dead by 1930, no translator, landing page states "
                                    "'Public domain in the USA', no year after 1930 in the front matter; only the body text is used",
                                    str(counts[name]))))
    common.write_text(os.path.join(common.DATA, "silver", "sources.tsv"), lines)
    stats = json.load(open(os.path.join(spike_dir, "data", "silver", "silver-text-stats.json"), encoding="utf-8"))["stats"]
    excl = ["# Silver documents left out because a sentence of the document is also in an evaluation set or in the open training split",
            "# (only ids, never text). Part 1: the selection run of the spike (sets of the WordSense corpus folder, informational);",
            "# part 2: this repository's own check (evaluation sets of data/eval, open dev/test, UD English-EWT when fetched, open train).",
            "# columns = document_id, set, matching_sentences, part"]
    for name in sorted(stats["excluded_docs_by_leakage"]):
        if not doc_name(name).startswith("faa-"):
            for label in stats["excluded_docs_by_leakage"][name]:
                excl.append("%s\t%s\t-\tspike-selection" % (doc_name(name), label))
    for name in sorted(removed):
        for label in sorted(removed[name]):
            excl.append("%s\t%s\t%d\tthis-repository" % (name, label, removed[name][label]))
    for name in sorted(duplicates):
        if name not in removed:
            excl.append("%s\topen-train\t%d\tthis-repository-sentences-only" % (name, duplicates[name]["open-train"]))
    common.write_text(os.path.join(common.DATA, "silver", "excluded-documents.tsv"), excl)
    drop = ["# Silver sentences left out of silver.tsv because the sentence equals a sentence of an evaluation set and its key",
            "# (lower-case alphanumerics) is shorter than 25 characters; longer matches exclude the whole document",
            "# (excluded-documents.tsv). Only sentence ids, never text. Sentences of excluded documents are not listed.",
            "# columns = sentence_id, evaluation_set"]
    drop += ["%s\t%s" % item for item in dropped]
    common.write_text(os.path.join(common.DATA, "silver", "dropped-sentences.tsv"), drop)


# ---------------------------------------------------------------- derive / verify

def derive_file():
    path = os.path.join(common.DATA, "silver", "silver.tsv")
    lexicon = common.read_lexicon(os.path.join(common.DATA, "lexicon", "lexicon.tsv"))
    sentences = common.read_corpus(path)[1]
    write_silver(path, sentences, lexicon)
    print("derived", len(sentences), "sentences")


def verify():
    path = os.path.join(common.DATA, "silver", "silver.tsv")
    lexicon = common.read_lexicon(os.path.join(common.DATA, "lexicon", "lexicon.tsv"))
    sentences = common.read_corpus(path)[1]
    problems = 0
    for sentence in sentences:
        forms = [r[0] for r in sentence["rows"]]
        expected = derive_sentence(forms, [r[3] for r in sentence["rows"]], lexicon)
        for row, (upos, status) in zip(sentence["rows"], expected):
            if (row[1], common.parse_status(row[2])[0]) != (upos, status):
                problems += 1
        if " ".join(forms) != sentence["text"]:
            problems += 1
    sources = {line.split("\t")[0] for line in common.read_lines(os.path.join(common.DATA, "silver", "sources.tsv"))
               if line and not line.startswith("#")}
    used = {s["source_id"] for s in sentences}
    if used != sources:
        problems += len(used ^ sources)
    print("sentences:", len(sentences), "problems:", problems)
    if problems:
        sys.exit(1)


def verify_sources():
    """Optional network check: Tatoeba sentences against the CC0 export, Gutenberg landing pages."""
    import bz2
    import tarfile
    import urllib.request
    path = common.fetch("https://downloads.tatoeba.org/exports/sentences_CC0.tar.bz2",
                        os.path.join(common.CACHE, "tatoeba", "sentences_CC0.tar.bz2"))
    wanted = {}
    for sentence in common.read_corpus(os.path.join(common.DATA, "silver", "silver.tsv"))[1]:
        if sentence["source_id"].startswith("tatoeba-"):
            wanted[sentence["source_id"][8:]] = common.key(sentence["text"])
    found = {}
    with tarfile.open(path, "r:bz2") as archive:
        for member in archive:
            if member.isfile() and member.name.endswith(".csv"):
                for raw in archive.extractfile(member):
                    cells = raw.decode("utf-8").rstrip("\n").split("\t")
                    if len(cells) >= 3 and cells[1] == "eng" and cells[0] in wanted:
                        found[cells[0]] = common.key(cells[2].replace("\u2019", "'"))
    missing = [i for i in wanted if i not in found]
    mismatch = [i for i in found if found[i] != wanted[i]]
    print("tatoeba ids:", len(wanted), "in the CC0 export:", len(found), "missing:", len(missing), "text mismatch:", len(mismatch))
    books = [r[0][10:] for r in csv.reader(open(os.path.join(common.DATA, "silver", "sources.tsv"), encoding="utf-8"), delimiter="\t")
             if r and r[0].startswith("gutenberg-")]
    bad = []
    for book in books:
        with urllib.request.urlopen(urllib.request.Request("https://www.gutenberg.org/ebooks/" + book,
                                                           headers={"User-Agent": "PartOfSpeech-data-tools"}), timeout=120) as response:
            page = response.read().decode("utf-8", errors="replace")
        if "Public domain in the USA" not in page:
            bad.append(book)
        time.sleep(0.5)
    print("gutenberg books:", len(books), "without the 'Public domain in the USA' statement:", len(bad), bad)
    if missing or bad or mismatch:
        sys.exit(1)


def main():
    parser = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    parser.add_argument("command", choices=("export-spike", "derive", "verify", "verify-sources"))
    parser.add_argument("--spike-dir", help="spike folder with the committed silver text and model labels (export-spike)")
    parser.add_argument("--include-faa", action="store_true", help="also write the git-ignored data/silver/silver-faa.tsv")
    args = parser.parse_args()
    if args.command == "export-spike":
        if not args.spike_dir:
            parser.error("--spike-dir is required")
        export_spike(args.spike_dir, args.include_faa)
    elif args.command == "derive":
        derive_file()
    elif args.command == "verify":
        verify()
    else:
        verify_sources()


if __name__ == "__main__":
    main()

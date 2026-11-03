"""Import MASC (written genres) Penn Treebank trees as UPOS training data.

  python tools/data/import_masc.py [--archive PATH_TO_penn-treebank.tgz] [--exclude-genre GENRE ...]

Input: the MASC Penn Treebank syntax archive released by the Open American National Corpus
(CC BY 3.0 US), pinned by SHA-256 in tools/data/pins/masc.tsv. Without --archive the archive is
downloaded into the git-ignored cache. The archive is read in place (no extraction); resource-fork files
(`._*`) and the spoken genre are skipped.

Output (deterministic): data/masc/masc.tsv, data/masc/excluded-documents.tsv, data/masc/dropped-sentences.tsv,
data/masc/mapping-audit.tsv, data/masc/sources.tsv (one row per document that contributes a sentence: genre, license, counts)

The tag mapping is the new UPOS function `ptb_to_upos` below (normative table: data/CONVENTIONS.md). It reuses
the spike's tree walker idea but fixes its root handling, keeps the clitics split (corpus tokenization) and
emits the 17 UPOS tags. Leakage rule (leakage.py): a sentence whose normalized key equals an evaluation sentence key of
at least 25 characters excludes its whole document (listed in excluded-documents.tsv); a shorter equal sentence
("Thank you.", "What should I do ?") is dropped alone (listed in dropped-sentences.tsv), because every sentence of a
treebank file is independent and a short sentence recurs in unrelated texts. Only ids are listed, never evaluation text.
"""
import argparse
import collections
import os
import re
import sys
import tarfile

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import common
import leakage

ARCHIVE_URL = "https://github.com/nancyide/anc-website/releases/download/datasets-2026-09-25/penn-treebank.tgz"
MIN_TOKENS, MAX_TOKENS = 3, 70
MASC_LICENSE = "CC BY 3.0 US"

BE = {"be", "am", "is", "are", "was", "were", "been", "being", "'s", "'m", "'re", "ai"}
HAVE = {"have", "has", "had", "having", "'ve", "'d"}
DO = {"do", "does", "did", "doing", "done"}
AUX_FORMS = BE | HAVE | DO
ARTICLES = {"a", "an", "the"}
NO_SUBORDINATOR = {"of", "with", "by", "about", "for", "in", "on", "at", "from", "to", "into", "over", "under"}
INDEFINITE = {"something", "anything", "nothing", "everything", "someone", "anyone", "everyone", "somebody",
              "anybody", "everybody", "nobody", "none", "one", "ones", "others"}
SYMBOL_FORMS = {"%", "+", "=", "<", ">", "@", "~", "^", "|", "\u00b0"}
PUNCT_LABELS = {".", ",", ":", "-LRB-", "-RRB-", "-LSB-", "-RSB-", "-LCB-", "-RCB-", "``", "''", '"', "'", "HYPH", "NFP"}
CLAUSE_SCONJ = {"when", "whenever", "while", "where", "wherever", "once"}
ADVERBIAL_SBAR = ("-ADV", "-TMP", "-LOC", "-MNR", "-PRP", "-CND", "-DIR")
# Verbs that take an indirect question: "asked when he left" (ADV), unlike "occurs when it rains" (SCONJ).
QUESTION_VERBS = {"ask", "asks", "asked", "asking", "know", "knows", "knew", "known", "knowing", "wonder", "wonders",
                  "wondered", "wondering", "tell", "tells", "told", "telling", "learn", "learns", "learned", "learnt",
                  "decide", "decides", "decided", "understand", "understands", "understood", "see", "sees", "saw",
                  "seen", "seeing", "remember", "remembers", "remembered", "explain", "explains", "explained",
                  "show", "shows", "showed", "shown", "determine", "determines", "determined", "realize", "realizes",
                  "realized", "discover", "discovers", "discovered", "find", "finds", "found", "say", "says", "said",
                  "matter", "matters", "mattered", "care", "cares", "cared", "depend", "depends", "depended",
                  "figure", "figured", "check", "checks", "checked", "guess", "guessed", "imagine", "imagined",
                  "forget", "forgot", "forgotten", "mean", "means", "meant"}
TEMPORAL_NOUNS = {"today", "tomorrow", "yesterday", "tonight"}


# ---------------------------------------------------------------- trees

class Node:
    __slots__ = ("label", "kids", "word")

    def __init__(self, label, kids=None, word=None):
        self.label = label
        self.kids = kids or []
        self.word = word


def parse_trees(text):
    """Bracketed trees of a .mrg file. The root `( (S ...) )` has an empty label."""
    tokens = re.findall(r"\(|\)|[^\s()]+", text)
    position = 0
    trees = []

    def node():
        nonlocal position
        position += 1  # the opening bracket
        label = ""
        if tokens[position] not in "()":
            label = tokens[position]
            position += 1
        kids, word = [], None
        while tokens[position] != ")":
            if tokens[position] == "(":
                kids.append(node())
            else:
                word = tokens[position]
                position += 1
        position += 1
        return Node(label, kids, word)

    while position < len(tokens):
        if tokens[position] == "(":
            trees.append(node())
        else:
            position += 1
    return trees


def base(label):
    """Label without function tags and indices: NP-SBJ-1 -> NP (traces and bracket labels are kept)."""
    if label.startswith("-"):
        return label
    return re.split(r"[-=]", label)[0]


def leaves(tree):
    """Pre-terminals in order as (node, ancestors) with ancestors from the root down to the parent."""
    out = []

    def walk(current, ancestors):
        if current.word is not None and not current.kids:
            out.append((current, ancestors))
        for kid in current.kids:
            walk(kid, ancestors + (current,))

    walk(tree, ())
    return out


def is_trace(node):
    return node.label == "-NONE-"


def real_kids(node):
    """Children that are not empty-category traces (a node with only traces below it does not count)."""
    def has_word(n):
        if n.word is not None and not n.kids:
            return not is_trace(n)
        return any(has_word(k) for k in n.kids)
    return [k for k in node.kids if has_word(k)]


def norm_form(word):
    word = word.replace("\\/", "/").replace("\\*", "*")
    word = word.replace("\u2019", "'").replace("\u2018", "'").replace("\u201c", '"').replace("\u201d", '"')
    word = word.replace("\u2014", "--").replace("\u2013", "--")
    if word in ("``", "''"):
        return '"'
    return {"-LRB-": "(", "-RRB-": ")", "-LCB-": "{", "-RCB-": "}", "-LSB-": "[", "-RSB-": "]"}.get(word, word)


# ---------------------------------------------------------------- the UPOS tag function

def adverbial_clause(sbar):
    """True for an SBAR that the treebank marks as an adverbial clause (-ADV, -TMP, -LOC, -MNR, -PRP, -CND, -DIR)."""
    return any(tag in sbar.label for tag in ADVERBIAL_SBAR)


def embedded_question_verb(vp):
    """True when the verb phrase's own verb takes an indirect question (ask, know, wonder, tell, ...)."""
    for kid in vp.kids:
        if kid.label.startswith("VB") and kid.word is not None:
            return kid.word.lower() in QUESTION_VERBS
    return False


def ptb_to_upos(node, ancestors, following):
    """UPOS of one Penn Treebank pre-terminal; None for an empty-category trace.

    node: the pre-terminal (label = Penn tag, word); ancestors: nodes from the root down to its parent;
    following: Penn tags of the following pre-terminals of the sentence (for lookahead rules).
    """
    label = "PRP$" if node.label == "PRO$" else base(node.label)
    word = node.word
    low = word.lower()
    parent = ancestors[-1] if ancestors else None
    grand = ancestors[-2] if len(ancestors) > 1 else None
    pl = base(parent.label) if parent else ""
    siblings = real_kids(parent) if parent else []
    alone = len(siblings) == 1
    if label == "-NONE-":
        return None
    if label in ("$", "#", "SYM") or (low in SYMBOL_FORMS and label != "POS"):
        return "SYM"
    if label in PUNCT_LABELS:
        return "PUNCT"
    if label in ("FW", "XX", "GW", "ADD", "AFX", "LS"):
        return "X"
    if common.is_number(word) and label.startswith(("NN", "JJ")):
        return "NUM"
    if label.startswith("NNP"):
        return "PROPN"
    if label.startswith("NN"):
        if (label == "NN" and low in INDEFINITE) or (label == "NNS" and low == "others"):
            return "PRON"
        if label == "NN" and low in TEMPORAL_NOUNS and parent is not None and "-TMP" in parent.label:
            return "ADV"
        return "NOUN"
    if label in ("PRP", "WP", "EX"):
        return "PRON"
    if label in ("PRP$", "WP$", "PDT"):
        return "DET"
    if label in ("DT", "WDT"):
        if low in ARTICLES:
            return "DET"
        if pl in ("ADVP", "ADJP") and low in ("all", "no", "the"):
            return "ADV"
        if label == "WDT":
            return "PRON" if (pl == "WHNP" and alone) else "DET"
        return "PRON" if (pl == "NP" and alone) else "DET"
    if label == "IN":
        if pl == "SBAR" and low not in NO_SUBORDINATOR:
            return "SCONJ"
        return "ADP"
    if label == "CC":
        return "CCONJ"
    if label == "TO":
        return "ADP" if pl == "PP" else "PART"
    if label == "POS":
        return "PART"
    if label in ("RB", "RBR", "RBS"):
        return "PART" if low in ("not", "n't") else "ADV"
    if label == "WRB":
        if low in CLAUSE_SCONJ and pl == "WHADVP" and grand is not None and base(grand.label) == "SBAR":
            if low in ("while", "once", "whenever", "wherever") or adverbial_clause(grand):
                return "SCONJ"
            clause_parent = ancestors[-3] if len(ancestors) > 2 else None
            if clause_parent is None:
                return "SCONJ"
            kind = base(clause_parent.label)
            if kind == "NP" or (kind == "VP" and embedded_question_verb(clause_parent)):
                return "ADV"
            return "SCONJ"
        return "ADV"
    if label == "RP":
        return "ADV"
    if label.startswith("JJ"):
        return "ADJ"
    if label == "MD":
        return "AUX"
    if label.startswith("VB"):
        if low in AUX_FORMS and parent is not None and pl in ("VP", "SQ", "SINV", "S", "SBARQ"):
            if any(base(k.label) == "VP" for k in parent.kids):
                return "AUX"
        if label in ("VBG", "VBN"):
            if pl == "PP":
                return "ADP"  # including, regarding, according (to), given: participial prepositions
            if pl == "ADJP":
                return "ADJ"
            if pl == "NP" and any(f.startswith("NN") for f in following[:3]):
                return "ADJ"
        return "VERB"
    if label == "UH":
        return "INTJ"
    if label == "CD":
        return "NUM"
    return "X"


def convert_tree(tree):
    """(forms, upos, penn tags) of a tree, traces removed, or None when the tree is not a sentence."""
    items = leaves(tree)
    if any(n.label == "CODE" for n, _ in items):
        return None
    forms, tags, penn = [], [], []
    for index, (node, ancestors) in enumerate(items):
        following = [n.label for n, _ in items[index + 1:index + 4] if not is_trace(n)]
        upos = ptb_to_upos(node, ancestors, following)
        if upos is None:
            continue
        forms.append(common.nfc(norm_form(node.word)))
        tags.append(upos)
        penn.append("PRP$" if node.label == "PRO$" else base(node.label))
    return forms, tags, penn


# ---------------------------------------------------------------- archive

def archive_path(arg):
    pins = common.read_pins("masc.tsv")
    sha = pins["penn-treebank.tgz"][0]
    if arg:
        actual = common.sha256_file(arg)
        if actual != sha:
            raise RuntimeError("penn-treebank.tgz: SHA-256 %s does not match the pin %s" % (actual, sha))
        return arg
    return common.fetch(ARCHIVE_URL, os.path.join(common.CACHE, "masc", "penn-treebank.tgz"), sha)


def read_documents(path):
    """Yield (document id, genre, text) for every written .mrg file, in name order."""
    with tarfile.open(path, "r:gz") as archive:
        members = sorted((m for m in archive.getmembers() if m.isfile() and m.name.endswith(".mrg")),
                         key=lambda m: m.name)
        for member in members:
            parts = member.name.split("/")
            if parts[-1].startswith("._") or "written" not in parts:
                continue
            index = parts.index("written")
            genre = parts[index + 1]
            document = "masc:" + "/".join(parts[index:])[:-4]
            yield document, genre, archive.extractfile(member).read().decode("utf-8", errors="replace")


def open_train_majority():
    """{lower-case word: Counter of UPOS} of the open training split (for the audit)."""
    table = collections.defaultdict(collections.Counter)
    for sentence in common.read_corpus(os.path.join(common.DATA, "open", "train.tsv"))[1]:
        for row in sentence["rows"]:
            table[row[0].lower()][row[1]] += 1
    return table


def main():
    parser = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    parser.add_argument("--archive", help="local penn-treebank.tgz (default: download into the cache)")
    parser.add_argument("--exclude-genre", action="append", default=[], help="genre to leave out, e.g. newspaper")
    args = parser.parse_args()
    path = archive_path(args.archive)
    keys, _ = leakage.evaluation_keys(include_ud=True)
    ud_present = bool(leakage.ud_sets())
    sentences, excluded, dropped = [], {}, []
    confusion = collections.Counter()
    genre_tokens = collections.Counter()
    genres = {}
    for document, genre, text in read_documents(path):
        if genre in args.exclude_genre:
            continue
        doc_sentences, hit, short = [], {}, []
        for number, tree in enumerate(parse_trees(text)):
            converted = convert_tree(tree)
            if converted is None:
                continue
            forms, tags, penn = converted
            if not MIN_TOKENS <= len(forms) <= MAX_TOKENS:
                continue
            sentence_text = " ".join(forms)
            sentence_id = "%s#%d" % (document, number)
            found = leakage.match(sentence_text, keys)
            if found is not None and found[1] == "document":
                hit[found[0]] = hit.get(found[0], 0) + 1
            elif found is not None:
                short.append((sentence_id, found[0]))  # a short sentence that recurs: only the sentence is dropped
                continue
            doc_sentences.append({
                "sent_id": sentence_id, "source_id": document, "text": sentence_text,
                "extra": ["# genre = " + genre],
                "rows": [[f, t, "ptb", p] for f, t, p in zip(forms, tags, penn)]})
        if hit:
            excluded[document] = hit
            continue
        dropped.extend(short)
        for sentence in doc_sentences:
            genres[document] = genre
            for row in sentence["rows"]:
                confusion[(row[3], row[1])] += 1
            genre_tokens[genre] += len(sentence["rows"])
            sentences.append(sentence)
    header = [
        "# corpus = DemaConsulting.PartOfSpeech MASC written genres, Penn Treebank trees mapped to UPOS (tools/data/import_masc.py)",
        "# license = CC-BY-3.0-US (Open American National Corpus, MASC); converted data, see data/masc/ATTRIBUTION.md",
        "# columns = form, upos, status, source_tag",
        "# status = ptb (tag derived from the Penn Treebank bracketing by the rules in data/CONVENTIONS.md)",
        "# source_tag = the Penn Treebank tag of the token",
        "# origin = penn-treebank.tgz, sha256 %s (MASC release 2026-09-25)" % common.sha256_file(path),
    ]
    common.write_corpus(os.path.join(common.DATA, "masc", "masc.tsv"), header, sentences)
    excl_lines = ["# MASC documents left out of masc.tsv because a sentence of the document (key of 25 or more characters) is also in an evaluation set",
                  "# (open dev/test, gold v2, shape probe, STE challenge set, UD English-EWT when fetched; only ids, never text)",
                  "# columns = document_id, evaluation_set, matching_sentences"]
    for document in sorted(excluded):
        for label in sorted(excluded[document]):
            excl_lines.append("%s\t%s\t%d" % (document, label, excluded[document][label]))
    common.write_text(os.path.join(common.DATA, "masc", "excluded-documents.tsv"), excl_lines)
    drop_lines = ["# MASC sentences left out of masc.tsv because the sentence equals a sentence of an evaluation set and its key",
                  "# (lower-case alphanumerics) is shorter than 25 characters; longer matches exclude the whole document",
                  "# (excluded-documents.tsv). Only sentence ids, never text. Sentences of excluded documents are not listed.",
                  "# columns = sentence_id, evaluation_set"]
    drop_lines += ["%s\t%s" % item for item in dropped]
    common.write_text(os.path.join(common.DATA, "masc", "dropped-sentences.tsv"), drop_lines)
    write_sources(sentences, genres)
    write_audit(confusion, sentences)
    tokens = sum(len(s["rows"]) for s in sentences)
    print("documents kept:", len({s["source_id"] for s in sentences}), "excluded:", len(excluded),
          "(UD cache present: %s)" % ud_present)
    print("short sentences dropped:", len(dropped))
    print("sentences:", len(sentences), "tokens:", tokens)
    print("tokens by genre:", dict(sorted(genre_tokens.items())))


def write_sources(sentences, genres):
    """data/masc/sources.tsv: one row per document that contributes at least one sentence to masc.tsv."""
    counts = collections.OrderedDict()
    for sentence in sentences:
        entry = counts.setdefault(sentence["source_id"], [0, 0])
        entry[0] += 1
        entry[1] += len(sentence["rows"])
    lines = ["# Sources of masc.tsv: one row per MASC document that contributes at least one sentence (written by tools/data/import_masc.py)",
             "# columns = source_id, genre, license_basis, sentences, tokens",
             "# license_basis = the license of the whole corpus (Creative Commons Attribution 3.0 United States, see ATTRIBUTION.md);"
             " sentences and tokens are the counts in masc.tsv"]
    for document in sorted(counts):
        lines.append("%s\t%s\t%s\t%d\t%d" % (document, genres[document], MASC_LICENSE, counts[document][0], counts[document][1]))
    common.write_text(os.path.join(common.DATA, "masc", "sources.tsv"), lines)


def write_audit(confusion, sentences):
    lines = ["# Mapping audit of masc.tsv (written by tools/data/import_masc.py)",
             "# section confusion: kind, penn_tag, upos, tokens  (Penn tag by UPOS tag counts)",
             "# section disagree: kind, word, masc_upos, open_train_majority_upos, tokens, open_majority_share, open_tokens",
             "#   the 30 most frequent words whose masc.tsv tag differs from the majority tag in open/train.tsv",
             "#   (words with at least 5 open-train tokens and a majority share of at least 0.7)"]
    for (penn, upos), count in sorted(confusion.items()):
        lines.append("confusion\t%s\t%s\t%d" % (penn, upos, count))
    table = open_train_majority()
    disagree = collections.Counter()
    for sentence in sentences:
        for row in sentence["rows"]:
            counts = table.get(row[0].lower())
            if not counts:
                continue
            total = sum(counts.values())
            top, top_count = max(sorted(counts.items()), key=lambda item: item[1])
            if total >= 5 and top_count / total >= 0.7 and top != row[1]:
                disagree[(row[0].lower(), row[1], top, "%.2f" % (top_count / total), total)] += 1
    ranked = sorted(disagree.items(), key=lambda item: (-item[1], item[0]))[:30]
    for (word, masc, top, share, total), count in ranked:
        lines.append("disagree\t%s\t%s\t%s\t%d\t%s\t%d" % (word, masc, top, count, share, total))
    common.write_text(os.path.join(common.DATA, "masc", "mapping-audit.tsv"), lines)


if __name__ == "__main__":
    main()

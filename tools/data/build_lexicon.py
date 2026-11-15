"""Build data/lexicon/lexicon.tsv (spelling -> the UPOS tags the spelling may carry) and data/lexicon/analyses.tsv
(spelling -> base word, UPOS tag and features of every form row behind it).

  python tools/data/build_lexicon.py [--wordsense-dir DIR] [--k 1] [--masc-k 2] [--write-pins]
  python tools/data/build_lexicon.py --analyses-only [--wordsense-dir DIR]    (analyses.tsv from the committed lexicon.tsv)
  python tools/data/build_lexicon.py --check-analyses [--wordsense-dir DIR]   (exit 1 if analyses.tsv is not current)

Inputs (all reusable licenses, see data/lexicon/README.md):
  1. WordSense data/form/*.tsv at the pinned commit (Open English WordNet 2025 forms, rule-generated inflections,
     Moby Part-of-Speech II reverse forms, project additions), SHA-256 pinned per file in
     tools/data/pins/wordsense-form.manifest.tsv. Read from --wordsense-dir (a checkout at the pinned commit) or
     downloaded into the git-ignored cache.
  2. data/lexicon/closed-class.tsv (function words; mode exact replaces, mode add extends the dictionary tags).
  3. data/lexicon/overrides.tsv (a short list of evidenced corrections).
  4. Corpus attestation: every (lower-case spelling, UPOS) seen at least K times in data/open/train.tsv (tokens whose
     status is not `disputed`) and at least M times in data/masc/masc.tsv; tokens that contain a digit are not added.

Rules of the dictionary part:
  * the spelling is lower-cased (NFC); spellings with a space or without a letter are skipped;
  * the part of speech is read from the end of the entry id (`oewn-wind-n-1` is a noun; the number is a homograph
    index), the `pos` column when the entry id is empty;
  * n -> NOUN, and PROPN when the cased spelling starts with an upper-case letter; v -> VERB; a, s -> ADJ; r -> ADV;
    u -> PRON; p -> ADP; d -> DET; x -> AUX; i -> INTJ; c -> CCONJ if closed-class.tsv lists the word with CCONJ, else SCONJ;
  * a verb row with VerbForm=Part or Tense=Past also adds ADJ; VerbForm=Ger adds ADJ and NOUN (participles and gerunds).

Output rows are sorted by UTF-8 bytes, tags alphabetical; spellings that are not in the file are unknown (all 17 tags allowed).
`--write-pins` (with --wordsense-dir) writes the pin manifest of the form files instead of building.

Rules of the analyses part (analyses.tsv; the same form rows, the same filters, the same spelling key as the dictionary part):
  * an analysis is (lemma, UPOS, features) of one form row; the UPOS is the mapping of the entry's own part of speech
    only (n NOUN, v VERB, a and s ADJ, r ADV, u PRON, p ADP, d DET, x AUX, i INTJ, c CCONJ or SCONJ as above); the tags
    that the dictionary part adds by rule (PROPN for a capitalized noun, ADJ for participles, ADJ and NOUN for
    gerunds) get no analysis;
  * the lemma is the entry id without the source prefix (up to the first hyphen) and without the part-of-speech suffix
    (`oewn-test-v` is `test`; the three escapes of the entry ids are decoded: `-ap-` is `'`, `-sl-` is `/`, `-pl-` is `+`),
    the `lemma` column when the entry id is empty; it is NFC and lower case, never empty, and
    never contains white space or an underscore (anything else is an error);
  * the features are the source features without `Capitalization` (a project feature of the cased spelling, not UD) in
    UD notation: names in ascending order joined by `|` (the source joins by `+`); only the names and values of
    FEATURE_VOCABULARY are accepted, a new name, a new value or another order is an error so that new vocabulary is a
    reviewed decision;
  * an analysis is kept only if its UPOS is in the final tag set of the spelling (a closed-class entry of mode exact
    replaces the dictionary tags); duplicates collapse; within a spelling they are ordered by (tag, lemma, features) in
    UTF-8 byte order;
  * rows are `spelling TAB analysis TAB analysis ...` with `analysis = lemma TAG features` (features omitted when empty),
    ascending by the UTF-8 bytes of the spelling, only spellings of the lexicon.
`--analyses-only` reads the committed lexicon.tsv for the tag sets (it does not rebuild the lexicon); the default run
writes lexicon.tsv and then analyses.tsv from the freshly built tag sets; `--check-analyses` rebuilds the analyses from
the pinned form tables and the committed lexicon.tsv and compares them with the committed analyses.tsv.
"""
import argparse
import collections
import os
import re
import sys

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import common

POS_RE = re.compile(r"-([nvasrupcidx])(?:-\d+)?$")
POS_TAG = {"n": "NOUN", "v": "VERB", "a": "ADJ", "s": "ADJ", "r": "ADV", "u": "PRON", "p": "ADP", "d": "DET",
           "x": "AUX", "i": "INTJ"}
FORM_DIR = "data/form/"


def form_files(source_dir):
    """[(name, path)] of the pinned form tables (hash verified)."""
    pins = common.read_pins("wordsense-form.manifest.tsv")
    files = []
    for name in sorted(pins):
        files.append((name, common.wordsense_file(FORM_DIR + name, source_dir, pins[name][0])))
    return files


def write_pins(source_dir):
    folder = os.path.join(source_dir, "data", "form")
    lines = ["# SHA-256 of every WordSense data/form/*.tsv file (Open English WordNet forms and derived inflections).",
             "# Pinned to WordSense commit %s." % common.WORDSENSE_COMMIT,
             "# Columns: file name <TAB> SHA-256"]
    for name in sorted(os.listdir(folder)):
        if name.endswith(".tsv"):
            lines.append("%s\t%s" % (name, common.sha256_file(os.path.join(folder, name))))
    common.write_text(os.path.join(common.PINS, "wordsense-form.manifest.tsv"), lines)
    print("wrote", len(lines) - 3, "pins")


def entry_pos(entry_id, pos_column):
    """Part-of-speech letter of a form row: the suffix of the entry id (homograph number allowed), else the pos column."""
    if entry_id:
        match = POS_RE.search(entry_id)
        return match.group(1) if match else ""
    return pos_column


def read_closed_class(path=None):
    """{spelling: (tags frozenset, mode)}"""
    path = path or os.path.join(common.DATA, "lexicon", "closed-class.tsv")
    table = {}
    for line in common.read_lines(path):
        if not line or line.startswith("#"):
            continue
        cells = line.split("\t")
        spelling, tags, mode = cells[0], frozenset(cells[1].split(" ")), cells[2]
        if spelling in table:
            raise ValueError("duplicate closed-class entry " + spelling)
        if mode not in ("exact", "add") or not tags <= common.UPOS_SET:
            raise ValueError("bad closed-class entry " + line)
        table[spelling] = (tags, mode)
    return table


def read_overrides(path=None):
    """{spelling: (tags frozenset, mode)} from overrides.tsv (spelling, tags, mode, evidence)."""
    path = path or os.path.join(common.DATA, "lexicon", "overrides.tsv")
    table = {}
    if not os.path.exists(path):
        return table
    for line in common.read_lines(path):
        if not line or line.startswith("#"):
            continue
        cells = line.split("\t")
        tags = frozenset(cells[1].split(" "))
        if cells[2] not in ("exact", "add") or not tags <= common.UPOS_SET or len(cells) < 4 or not cells[3]:
            raise ValueError("bad override entry (needs tags, mode and evidence): " + line)
        table[cells[0]] = (tags, cells[2])
    return table


def dictionary_tags(files, closed):
    """{lower-case spelling: set of UPOS} from the form tables."""
    lexicon = collections.defaultdict(set)
    conj_cc = {w for w, (tags, _) in closed.items() if "CCONJ" in tags}
    for _, path in files:
        for number, line in enumerate(common.read_lines(path)):
            if number == 0:
                continue
            cells = line.split("\t")
            spelling, entry_id, features, pos_column = cells[0], cells[2], cells[3], cells[7]
            letter = entry_pos(entry_id, pos_column)
            if " " in spelling or not any(ch.isalpha() for ch in spelling) or not letter:
                continue
            if "Contraction=Yes" in features:
                continue  # contractions are split by the corpus tokenization (do + n't); they are never tokens
            key = common.nfc(spelling).lower()
            tags = lexicon[key]
            if letter in POS_TAG:
                tags.add(POS_TAG[letter])
                if letter == "n" and spelling[:1].isupper():
                    tags.add("PROPN")
                if letter == "v":
                    if "VerbForm=Part" in features or "Tense=Past" in features:
                        tags.add("ADJ")
                    if "VerbForm=Ger" in features:
                        tags.update(("ADJ", "NOUN"))
            elif letter == "c":
                tags.add("CCONJ" if key in conj_cc else "SCONJ")
    return lexicon


def attested(path, minimum, skip_disputed):
    """{(lower-case spelling, upos): count} over the tokens of a corpus file (tokens with a digit are skipped)."""
    counts = collections.Counter()
    for sentence in common.read_corpus(path)[1]:
        for row in sentence["rows"]:
            form, upos, status = row[0], row[1], row[2]
            status = common.parse_status(status)[0]  # refuses a malformed status or flag; flags do not change what a row attests
            if (skip_disputed and status == "disputed") or status == "masked" or upos == "_":
                continue
            if any(ch.isdigit() for ch in form):
                continue
            counts[(common.nfc(form).lower(), upos)] += 1
    return {k: v for k, v in counts.items() if v >= minimum}


def build(files, k, masc_k, closed=None, overrides=None, train=None, masc=None):
    closed = closed if closed is not None else read_closed_class()
    overrides = overrides if overrides is not None else read_overrides()
    lexicon = dictionary_tags(files, closed)
    for table in (closed, overrides):
        for spelling, (tags, mode) in table.items():
            if mode == "exact":
                lexicon[spelling] = set(tags)
            else:
                lexicon[spelling].update(tags)
    sources = []
    if k:
        sources.append(train if train is not None else attested(os.path.join(common.DATA, "open", "train.tsv"), k, True))
    if masc_k:
        sources.append(masc if masc is not None else attested(os.path.join(common.DATA, "masc", "masc.tsv"), masc_k, False))
    for table in sources:
        for (spelling, upos), _ in table.items():
            if upos in common.UPOS_SET:
                lexicon[spelling].add(upos)
    return {spelling: tags for spelling, tags in lexicon.items() if tags}


def write_lexicon(lexicon, k, masc_k, path):
    tags_total = sum(len(t) for t in lexicon.values())
    header = [
        "# DemaConsulting.PartOfSpeech lexicon: lower-case spelling -> UPOS tags the spelling may carry (space separated, alphabetical)",
        "# license = MIT AND CC-BY-4.0 AND CC-BY-3.0-US AND CC0-1.0 AND Public-Domain (see data/lexicon/README.md and THIRD-PARTY-NOTICES.md)",
        "# generated by tools/data/build_lexicon.py; do not edit; pinned WordSense commit %s" % common.WORDSENSE_COMMIT,
        "# parameters: open-train attestation K=%d (non-disputed tokens), MASC attestation M=%d; spellings=%d, tag entries=%d"
        % (k, masc_k, len(lexicon), tags_total),
        "# a spelling that is not listed is unknown: all 17 tags are allowed; token-shape rules: data/lexicon/README.md",
    ]
    rows = sorted(lexicon, key=lambda s: s.encode("utf-8"))
    common.write_text(path, header + ["%s\t%s" % (s, " ".join(sorted(lexicon[s]))) for s in rows])


# The only feature names and values of the form tables that an analysis may carry (UD names and values); anything else is an
# error so that new vocabulary is a reviewed decision. Capitalization (Exact, Title, Upper) describes the cased spelling.
FEATURE_VOCABULARY = {
    "Degree": ("Cmp", "Pos", "Sup"),
    "Number": ("Plur", "Sing"),
    "Person": ("3",),
    "Tense": ("Past", "Pres"),
    "VerbForm": ("Ger", "Inf", "Part"),
}
DROPPED_FEATURES = ("Capitalization",)
# The Open English WordNet entry ids escape these characters of the word between hyphens (`oewn--ap-hood-n` is `'hood`,
# `oewn-20-sl-20-n` is `20/20`, `oewn-LGBT-pl--n` is `LGBT+`); every base-form row of the form tables whose spelling has no
# space equals its decoded lemma (checked when this table was added), so no other escape occurs in the rows an analysis reads.
ENTRY_ID_ESCAPES = {"ap": "'", "sl": "/", "pl": "+"}
ENTRY_ID_ESCAPE_RE = re.compile(r"-(ap|sl|pl)-")
ANALYSES_FILE = "analyses.tsv"
LEXICON_FILE = "lexicon.tsv"


def entry_lemma(entry_id, lemma_column):
    """Base word of a form row: the entry id without its source prefix and part-of-speech suffix, else the lemma column.

    `oewn-test-v` is `test`, `oewn-wind-n-1` is `wind`, `dema-to-p` is `to`. The entry ids escape three characters of the
    word (ENTRY_ID_ESCAPES): `oewn--ap-hood-n` is `'hood`. The result is NFC and lower case; an empty lemma or one with
    white space is an error (nothing is dropped silently).
    """
    if entry_id:
        match = POS_RE.search(entry_id)
        if not match:
            raise ValueError("entry id without a part-of-speech suffix: " + entry_id)
        source, dash, lemma = entry_id[:match.start()].partition("-")
        if not dash or not source:
            raise ValueError("entry id without a source prefix: " + entry_id)
        lemma = ENTRY_ID_ESCAPE_RE.sub(lambda found: ENTRY_ID_ESCAPES[found.group(1)], lemma)
    else:
        lemma = lemma_column
    lemma = common.nfc(lemma).lower()
    if not lemma or "_" in lemma or any(ch.isspace() for ch in lemma):
        raise ValueError("empty lemma, or a lemma with white space or an underscore (a multiword entry): " + repr(lemma) + " (entry id " + repr(entry_id) + ")")
    return lemma


def analysis_features(features):
    """UD notation of a source feature cell: `Capitalization` dropped, names ascending, joined by `|` (the source uses `+`).

    A feature without `=`, a name or value outside FEATURE_VOCABULARY, a repeated name or names out of order are errors.
    """
    names = []
    kept = []
    for part in features.split("+") if features else []:
        name, equals, value = part.partition("=")
        if not equals:
            raise ValueError("malformed feature " + repr(part) + " in " + repr(features))
        if name in DROPPED_FEATURES:
            continue
        if name not in FEATURE_VOCABULARY or value not in FEATURE_VOCABULARY[name]:
            raise ValueError("feature outside the analyses vocabulary: " + repr(part) + " in " + repr(features))
        if names and name <= names[-1]:
            raise ValueError("features not in ascending name order: " + repr(features))
        names.append(name)
        kept.append(part)
    return "|".join(kept)


def build_analyses(files, lexicon_tags, closed):
    """{lower-case spelling: [(tag, lemma, features), ...]} from the form tables.

    Same rows, filters and spelling key as dictionary_tags. Only the tag of the entry's own part of speech is used (derived
    tags such as PROPN for a capitalized noun get no analysis), and an analysis is kept only if its tag is in the final tag
    set of the spelling (lexicon_tags: {spelling: set of UPOS}); spellings that are not in lexicon_tags are skipped. Each list
    is distinct and ordered by (tag, lemma, features) in UTF-8 byte order; spellings without an analysis are not in the result.
    """
    conj_cc = {w for w, (tags, _) in closed.items() if "CCONJ" in tags}
    found = collections.defaultdict(set)
    for _, path in files:
        for number, line in enumerate(common.read_lines(path)):
            if number == 0:
                continue
            cells = line.split("\t")
            spelling, entry_id, features, lemma_column, pos_column = cells[0], cells[2], cells[3], cells[6], cells[7]
            letter = entry_pos(entry_id, pos_column)
            if " " in spelling or not any(ch.isalpha() for ch in spelling) or not letter:
                continue
            if "Contraction=Yes" in features:
                continue
            key = common.nfc(spelling).lower()
            if key not in lexicon_tags:
                continue
            if letter in POS_TAG:
                tag = POS_TAG[letter]
            elif letter == "c":
                tag = "CCONJ" if key in conj_cc else "SCONJ"
            else:
                continue
            if tag not in lexicon_tags[key]:
                continue
            found[key].add((tag, entry_lemma(entry_id, lemma_column), analysis_features(features)))
    return {key: sorted(items, key=lambda a: (a[0], a[1].encode("utf-8"), a[2].encode("utf-8")))
            for key, items in found.items()}


def analyses_lines(analyses):
    """The lines of analyses.tsv (header comments, then one row per spelling) for a build_analyses result."""
    total = sum(len(items) for items in analyses.values())
    header = [
        "# DemaConsulting.PartOfSpeech analyses: lower-case spelling -> base word, UPOS tag and features of each form it can be (TAB separated)",
        "# an analysis is: lemma, a space, the UPOS tag and, when the form tables record any, a space and the features (UD notation: names ascending, joined by |)",
        "# license = MIT AND CC-BY-4.0 AND CC-BY-3.0-US AND CC0-1.0 AND Public-Domain (see data/lexicon/README.md and THIRD-PARTY-NOTICES.md)",
        "# generated by tools/data/build_lexicon.py; do not edit; pinned WordSense commit %s" % common.WORDSENSE_COMMIT,
        "# only spellings of lexicon.tsv; an analysis is kept only if its tag is a tag of the spelling in lexicon.tsv; spellings=%d, analyses=%d"
        % (len(analyses), total),
    ]
    rows = sorted(analyses, key=lambda s: s.encode("utf-8"))
    body = []
    for spelling in rows:
        cells = [spelling]
        for tag, lemma, features in analyses[spelling]:
            cells.append(" ".join((lemma, tag, features)) if features else " ".join((lemma, tag)))
        body.append("\t".join(cells))
    return header + body


def write_analyses(analyses, path):
    common.write_text(path, analyses_lines(analyses))


def read_analyses(path):
    """(analyses, (spellings, analyses) declared in the header) of an analyses.tsv; same shape as build_analyses."""
    analyses = {}
    declared = None
    for line in common.read_lines(path):
        if not line:
            continue
        if line.startswith("# "):
            match = re.search(r"spellings=(\d+), analyses=(\d+)", line)
            if match:
                declared = (int(match.group(1)), int(match.group(2)))
            continue
        cells = line.split("\t")
        items = []
        for cell in cells[1:]:
            parts = cell.split(" ")
            if len(parts) not in (2, 3):
                raise ValueError("malformed analysis " + repr(cell) + " of " + repr(cells[0]))
            items.append((parts[1], parts[0], parts[2] if len(parts) == 3 else ""))
        if not items or cells[0] in analyses:
            raise ValueError("empty or duplicate row of " + repr(cells[0]))
        analyses[cells[0]] = items
    return analyses, declared


def analyses_differences(expected_lines, actual_lines, limit=10):
    """The first differences between two line lists as readable messages (empty when they are equal)."""
    if expected_lines == actual_lines:
        return []
    messages = []
    for number, (expected, actual) in enumerate(zip(expected_lines, actual_lines), 1):
        if expected != actual:
            messages.append("line %d: expected %r, committed %r" % (number, expected[:200], actual[:200]))
            if len(messages) >= limit:
                break
    if len(expected_lines) != len(actual_lines):
        messages.append("expected %d lines, committed %d" % (len(expected_lines), len(actual_lines)))
    return messages


def main():
    parser = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    parser.add_argument("--wordsense-dir", help="local WordSense checkout at the pinned commit (default: download into the cache)")
    parser.add_argument("--k", type=int, default=1, help="minimum count of a (spelling, tag) in open/train.tsv (0 = no attestation)")
    parser.add_argument("--masc-k", type=int, default=2, help="minimum count of a (spelling, tag) in masc.tsv (0 = not used)")
    parser.add_argument("--write-pins", action="store_true", help="write the form pin manifest from --wordsense-dir and exit")
    parser.add_argument("--analyses-only", action="store_true", help="write only analyses.tsv, from the committed lexicon.tsv")
    parser.add_argument("--check-analyses", action="store_true", help="rebuild the analyses and compare with the committed analyses.tsv (exit 1 if different)")
    args = parser.parse_args()
    if args.write_pins:
        write_pins(args.wordsense_dir)
        return
    lexicon_path = os.path.join(common.DATA, "lexicon", LEXICON_FILE)
    analyses_path = os.path.join(common.DATA, "lexicon", ANALYSES_FILE)
    if args.analyses_only or args.check_analyses:
        analyses = build_analyses(form_files(args.wordsense_dir), common.read_lexicon(lexicon_path), read_closed_class())
        if args.check_analyses:
            differences = analyses_differences(analyses_lines(analyses), common.read_lines(analyses_path))
            if differences:
                print("%s is not current; regenerate it with: python tools/data/build_lexicon.py --analyses-only" % ANALYSES_FILE)
                print("\n".join(differences))
                sys.exit(1)
            print("%s is current: spellings: %d, analyses: %d" % (ANALYSES_FILE, len(analyses), sum(len(i) for i in analyses.values())))
            return
        write_analyses(analyses, analyses_path)
        print("spellings with analyses: %d, analyses: %d" % (len(analyses), sum(len(i) for i in analyses.values())))
        return
    files = form_files(args.wordsense_dir)
    closed = read_closed_class()
    lexicon = build(files, args.k, args.masc_k, closed=closed)
    write_lexicon(lexicon, args.k, args.masc_k, lexicon_path)
    print("spellings: %d, tag entries: %d, mean tags per spelling: %.3f"
          % (len(lexicon), sum(len(t) for t in lexicon.values()), sum(len(t) for t in lexicon.values()) / len(lexicon)))
    analyses = build_analyses(files, lexicon, closed)
    write_analyses(analyses, analyses_path)
    print("spellings with analyses: %d, analyses: %d" % (len(analyses), sum(len(i) for i in analyses.values())))


if __name__ == "__main__":
    main()

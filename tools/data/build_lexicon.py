"""Build data/lexicon/lexicon.tsv: spelling (lower case) -> the UPOS tags the spelling may carry.

  python tools/data/build_lexicon.py [--wordsense-dir DIR] [--k 1] [--masc-k 2] [--write-pins]

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


def main():
    parser = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    parser.add_argument("--wordsense-dir", help="local WordSense checkout at the pinned commit (default: download into the cache)")
    parser.add_argument("--k", type=int, default=1, help="minimum count of a (spelling, tag) in open/train.tsv (0 = no attestation)")
    parser.add_argument("--masc-k", type=int, default=2, help="minimum count of a (spelling, tag) in masc.tsv (0 = not used)")
    parser.add_argument("--write-pins", action="store_true", help="write the form pin manifest from --wordsense-dir and exit")
    args = parser.parse_args()
    if args.write_pins:
        write_pins(args.wordsense_dir)
        return
    lexicon = build(form_files(args.wordsense_dir), args.k, args.masc_k)
    out = os.path.join(common.DATA, "lexicon", "lexicon.tsv")
    write_lexicon(lexicon, args.k, args.masc_k, out)
    print("spellings: %d, tag entries: %d, mean tags per spelling: %.3f"
          % (len(lexicon), sum(len(t) for t in lexicon.values()), sum(len(t) for t in lexicon.values()) / len(lexicon)))


if __name__ == "__main__":
    main()

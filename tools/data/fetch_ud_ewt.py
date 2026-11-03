"""Fetch UD English-EWT (evaluation only, CC BY-SA 4.0) into the git-ignored cache and map it to the corpus conventions.

NOTHING from UD English-EWT is ever committed: the files go to tools/data/cache/ud-ewt/ (git-ignored together with
every *.conllu file) and this script never writes under data/.

  python tools/data/fetch_ud_ewt.py fetch               # download the pinned files, verify SHA-256 (also used by check_leakage.py)
  python tools/data/fetch_ud_ewt.py map [--lexicon data/lexicon/lexicon.tsv]
                                                        # write cache/ud-ewt/ud_{dev,test}.tsv with the four convention rules

Pinned to UD_English-EWT commit 4a4d77f599ea53cc405f85d0cec4b2f14f81d42b (tools/data/pins/ud-ewt.tsv). A hash
mismatch aborts. The conventions of the corpus differ from UD in four documented ways, so the reference tags are
mapped before they are compared with a tagger (the raw tag is kept next to the mapped one):

  1. PRON with Poss=Yes and deprel nmod:poss -> DET                        (possessive determiners)
  2. AUX with lemma be and deprel cop -> VERB                              (copula and main-verb be)
  3. ADP with deprel compound:prt -> ADV                                   (phrasal-verb particles)
  4. SCONJ -> ADP when the lexicon has the word but no SCONJ analysis for it, or when deprel is mark and the
     head is a gerund (VerbForm=Ger)                                       (of, with, by, about; "after seeing")
"""
import argparse
import os
import sys

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import common

BASE = "https://raw.githubusercontent.com/UniversalDependencies/UD_English-EWT/4a4d77f599ea53cc405f85d0cec4b2f14f81d42b/"
FILES = ("en_ewt-ud-train.conllu", "en_ewt-ud-dev.conllu", "en_ewt-ud-test.conllu")
UD_CACHE = os.path.join(common.CACHE, "ud-ewt")


def fetch():
    pins = common.read_pins("ud-ewt.tsv")
    for name in FILES:
        path = common.fetch(BASE + name, os.path.join(UD_CACHE, name), pins[name][0])
        print("ok", common.rel(path), pins[name][0])


def read_conllu(path):
    """Sentences as dicts: sent_id, text, words (id, form, lemma, upos, feats, head, deprel); ranges are skipped."""
    sentences, current = [], None
    for line in common.read_lines(path) + [""]:
        if line.startswith("# sent_id = "):
            current = {"sent_id": line[12:], "text": "", "words": []}
        elif line.startswith("# text = ") and current is not None:
            current["text"] = line[9:]
        elif line.startswith("#"):
            continue
        elif line == "":
            if current is not None and current["words"]:
                sentences.append(current)
            current = None
        else:
            cells = line.split("\t")
            if "-" in cells[0] or "." in cells[0]:
                continue
            current["words"].append({"id": int(cells[0]), "form": cells[1], "lemma": cells[2], "upos": cells[3],
                                     "feats": cells[5], "head": int(cells[6]), "deprel": cells[7]})
    return sentences


def map_word(word, head_feats, lexicon):
    upos = word["upos"]
    if upos == "PRON" and "Poss=Yes" in word["feats"] and word["deprel"] == "nmod:poss":
        return "DET", "possessive-det"
    if upos == "AUX" and word["lemma"].lower() == "be" and word["deprel"] == "cop":
        return "VERB", "copula-verb"
    if upos == "ADP" and word["deprel"] == "compound:prt":
        return "ADV", "particle-adv"
    if upos == "SCONJ":
        entry = lexicon.get(word["form"].lower())
        if (entry is not None and "SCONJ" not in entry) or (word["deprel"] == "mark" and "VerbForm=Ger" in head_feats):
            return "ADP", "preposition-adp"
    return upos, "_"


def map_files(lexicon_path):
    lexicon = common.read_lexicon(lexicon_path)
    for split in ("dev", "test"):
        sentences = read_conllu(os.path.join(UD_CACHE, "en_ewt-ud-%s.conllu" % split))
        lines = ["# cache file, never commit: UD English-EWT (CC BY-SA 4.0) %s, convention-mapped" % split,
                 "# columns = form, mapped_upos, raw_upos, rule", ""]
        counts = {}
        for sentence in sentences:
            by_id = {w["id"]: w for w in sentence["words"]}
            lines.append("# sent_id = " + sentence["sent_id"])
            lines.append("# text = " + sentence["text"])
            for word in sentence["words"]:
                head = by_id.get(word["head"])
                mapped, rule = map_word(word, head["feats"] if head else "_", lexicon)
                counts[rule] = counts.get(rule, 0) + 1
                lines.append("\t".join((word["form"], mapped, word["upos"], rule)))
            lines.append("")
        out = os.path.join(UD_CACHE, "ud_%s.tsv" % split)
        common.write_text(out, lines)
        print(common.rel(out), len(sentences), "sentences", dict(sorted(counts.items())))


def main():
    parser = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    parser.add_argument("command", choices=("fetch", "map"))
    parser.add_argument("--lexicon", default=os.path.join(common.DATA, "lexicon", "lexicon.tsv"))
    args = parser.parse_args()
    fetch()
    if args.command == "map":
        map_files(args.lexicon)


if __name__ == "__main__":
    main()

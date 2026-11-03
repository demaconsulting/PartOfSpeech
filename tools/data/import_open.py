"""Import the CC0 open English POS corpus (WordSense) as UPOS-only training and evaluation files.

The open corpus already carries UPOS. The import is a lossless projection of the columns
`id form lemma upos collapsed feats consensus` to `form upos status` (status = the consensus column), written in
the shared corpus block format of tools/data/common.py. `sources.tsv` and `LICENSE` are copied unchanged.

Inputs are pinned (tools/data/pins/wordsense-inputs.tsv, WordSense commit 6530de19...). Give a local WordSense
checkout at that commit with --source-dir, or omit it to download the pinned files into the git-ignored cache.

  python tools/data/import_open.py [--source-dir PATH_TO_WORDSENSE_CHECKOUT]

Output (deterministic, bit-identical on a re-run): data/open/{train,dev,test}.tsv, data/open/sources.tsv, data/open/LICENSE
"""
import argparse
import os
import shutil
import sys

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import common

SPLITS = {"train": (11990, 209224), "dev": (1489, 26232), "test": (1501, 26185)}
STATUSES = ("unanimous", "disputed", "unchecked")


def read_projected(path):
    """Sentences of an open-corpus file projected to form, upos, status (extra comment lines kept in `extra`)."""
    header, sentences = common.read_corpus(path)
    out = []
    for sentence in sentences:
        rows = []
        for cells in sentence["rows"]:
            if len(cells) != 7:
                raise ValueError(sentence["sent_id"] + ": expected 7 columns, got " + str(len(cells)))
            _, form, _, upos, _, _, status = cells
            if upos not in common.UPOS_SET:
                raise ValueError(sentence["sent_id"] + ": unknown UPOS " + upos)
            if status not in STATUSES:
                raise ValueError(sentence["sent_id"] + ": unknown status " + status)
            rows.append([common.nfc(form), upos, status])
        text = common.nfc(sentence["text"])
        if "".join(r[0] for r in rows) != text.replace(" ", ""):
            raise ValueError(sentence["sent_id"] + ": tokens do not rejoin to the text")
        extra = ["# %s = %s" % (k, v) for k, v in sentence["comments"].items() if k not in ("sent_id", "source_id", "text")]
        out.append({"sent_id": sentence["sent_id"], "source_id": sentence["source_id"], "text": text, "rows": rows,
                    "extra": extra})
    return out


def project(path, split, sha):
    out = read_projected(path)
    sentences_expected, tokens_expected = SPLITS[split]
    tokens = sum(len(s["rows"]) for s in out)
    if (len(out), tokens) != (sentences_expected, tokens_expected):
        raise ValueError("%s: expected %d sentences and %d tokens, got %d and %d"
                         % (split, sentences_expected, tokens_expected, len(out), tokens))
    head = [
        "# corpus = DemaConsulting.PartOfSpeech open corpus, %s split (UPOS projection of the WordSense open English POS corpus)" % split,
        "# license = CC0-1.0 for annotations and selection; sentences are public domain or CC0 per source, see sources.tsv",
        "# columns = form, upos, status",
        "# status = unanimous | disputed | unchecked (consensus of the two annotators; unchecked = no second annotator)",
        "# origin = WordSense commit %s, corpus/open/representative/data/%s.tsv, sha256 %s" % (common.WORDSENSE_COMMIT, split, sha),
    ]
    common.write_corpus(os.path.join(common.DATA, "open", split + ".tsv"), head, out)
    return len(out), tokens


def main():
    parser = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    parser.add_argument("--source-dir", help="local WordSense checkout at the pinned commit (default: download into the cache)")
    args = parser.parse_args()
    pins = common.read_pins("wordsense-inputs.tsv")
    for split in SPLITS:
        relpath = "corpus/open/representative/data/%s.tsv" % split
        sha = pins[relpath][0]
        path = common.wordsense_file(relpath, args.source_dir, sha)
        sentences, tokens = project(path, split, sha)
        print("%s: %d sentences, %d tokens" % (split, sentences, tokens))
    for name in ("sources.tsv", "LICENSE"):
        relpath = "corpus/open/" + name
        path = common.wordsense_file(relpath, args.source_dir, pins[relpath][0])
        shutil.copyfile(path, os.path.join(common.DATA, "open", name))
        print("copied", name)


if __name__ == "__main__":
    main()

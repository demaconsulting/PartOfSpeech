"""Import the evaluation sets (WordSense, pinned commit): shape probe, gold v2, simplified-technical-English challenge set.

  python tools/data/import_eval.py [--source-dir PATH_TO_WORDSENSE_CHECKOUT]

- data/eval/shapes/probe.tsv                copied unchanged
- data/eval/gold-v2/{dev,test,sources}.tsv  copied unchanged (labels.tsv and manifest.tsv of the source are not imported)
- data/eval/ste-challenge/test.tsv          open-corpus file projected to form, upos, status (32 sentences)

All inputs are SHA-256 verified against tools/data/pins/wordsense-inputs.tsv. Evaluation data is never used for training.
"""
import argparse
import os
import shutil
import sys

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import common
import import_open

COPIES = (
    ("corpus/shapes/probe.tsv", ("eval", "shapes", "probe.tsv")),
    ("corpus/allwords/v2/dev.tsv", ("eval", "gold-v2", "dev.tsv")),
    ("corpus/allwords/v2/test.tsv", ("eval", "gold-v2", "test.tsv")),
    ("corpus/allwords/v2/sources.tsv", ("eval", "gold-v2", "sources.tsv")),
)
STE = "corpus/open/targeted/data/test.tsv"


def main():
    parser = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    parser.add_argument("--source-dir", help="local WordSense checkout at the pinned commit (default: download into the cache)")
    args = parser.parse_args()
    pins = common.read_pins("wordsense-inputs.tsv")
    for relpath, target in COPIES:
        path = common.wordsense_file(relpath, args.source_dir, pins[relpath][0])
        shutil.copyfile(path, os.path.join(common.DATA, *target))
        print("copied", "/".join(target))
    sha = pins[STE][0]
    path = common.wordsense_file(STE, args.source_dir, sha)
    sentences = import_open.read_projected(path)
    if (len(sentences), sum(len(s["rows"]) for s in sentences)) != (32, 272):
        raise ValueError("ste-challenge: expected 32 sentences and 272 tokens")
    header = [
        "# corpus = DemaConsulting.PartOfSpeech simplified-technical-English challenge set (UPOS projection)",
        "# license = CC0-1.0 (project-written sentences dedicated to the public domain by the project owner)",
        "# columns = form, upos, status",
        "# status = unanimous | disputed | unchecked",
        "# origin = WordSense commit %s, %s, sha256 %s" % (common.WORDSENSE_COMMIT, STE, sha),
        "# use = evaluation only; report separately, never merge into a headline number",
    ]
    common.write_corpus(os.path.join(common.DATA, "eval", "ste-challenge", "test.tsv"), header, sentences)
    print("ste-challenge: %d sentences" % len(sentences))


if __name__ == "__main__":
    main()

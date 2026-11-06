"""Corpus cleanup C, `prenominal-ing` follow-up: the frozen item list of the dev compound-head measure.

  python tools/DemaConsulting.PartOfSpeech.Trainer/prenominal-ing/build_items.py [--write | --check]

Inputs (the committed files of the branch, nothing else; no model, no prediction, no held-out file is read):
  data/open/dev.tsv           the open dev corpus with the corrected (v2) labels of corpus cleanup C
  data/lexicon/lexicon.tsv    the committed lexicon of the FULL arm (spelling -> tags); the item list is built with this lexicon
                              for every arm, so all arms are scored on the same items

Rule (deterministic; a token is classified by gold labels, the lower-cased NFC form and the lexicon only).
  form    = NFC form of the token, lower-cased; `plain` means the form is purely alphabetic (str.isalpha) and has at least 4
            letters (this drops the digits, the clitics and the short words)
  tags    = the lexicon tags of the form (a form that is not listed is not an item)
  next    = the gold tag of the next token of the same sentence (none at the end of the sentence)

  compound item (class compound-ing or compound-zero):
      plain, gold NOUN, next in {NOUN, PROPN}, tags contain NOUN and VERB;
      class compound-ing if the form ends in "ing", else compound-zero (a zero-derived noun/verb form such as `report`, `water`)
  verbal item (class verbal-ing):
      plain, gold VERB, the form ends in "ing", tags contain ADJ, NOUN and VERB (the signature the lexicon builder gives
      to the gerund/participle of a verb, so base verbs such as `bring` or `sing` are not items)
      (no condition on the next token)

Metrics (defined with the follow-up pre-registration in RUN-LOG.md): the NOUN accuracy on the compound items and the VERB accuracy
on the verbal items, with the chosen tag of the runtime decoder.

`--write` writes items-open-dev.tsv next to this script; `--check` rebuilds in memory and compares byte for byte.
"""
import argparse
import collections
import os
import sys

HERE = os.path.dirname(os.path.abspath(__file__))
ROOT = os.path.dirname(os.path.dirname(os.path.dirname(HERE)))
sys.path.insert(0, os.path.join(ROOT, "tools", "data"))
import common  # noqa: E402

DEV = os.path.join(ROOT, "data", "open", "dev.tsv")
LEXICON = os.path.join(ROOT, "data", "lexicon", "lexicon.tsv")
OUT = os.path.join(HERE, "items-open-dev.tsv")
MIN_LETTERS = 4


def plain(form):
    return form.isalpha() and len(form) >= MIN_LETTERS


def classify(form, gold, next_gold, tags):
    """The class of a token (compound-ing, compound-zero, verbal-ing) or None."""
    low = common.nfc(form).lower()
    if not plain(low):
        return None
    if gold == "NOUN" and next_gold in ("NOUN", "PROPN") and {"NOUN", "VERB"} <= tags:
        return "compound-ing" if low.endswith("ing") else "compound-zero"
    if gold == "VERB" and low.endswith("ing") and {"ADJ", "NOUN", "VERB"} <= tags:
        return "verbal-ing"
    return None


def build():
    lexicon = common.read_lexicon(LEXICON)
    _, sentences = common.read_corpus(DEV, strict=True)
    items = []
    for sentence in sentences:
        rows = sentence["rows"]
        for i, row in enumerate(rows):
            low = common.nfc(row[0]).lower()
            tags = lexicon.get(low)
            if tags is None:
                continue
            next_gold = rows[i + 1][1] if i + 1 < len(rows) else "-"
            kind = classify(row[0], row[1], next_gold, tags)
            if kind:
                next_form = rows[i + 1][0] if i + 1 < len(rows) else "-"
                items.append((sentence["sent_id"], i, row[0], kind, row[1], next_form))
    return items


def render(items):
    counts = collections.Counter(item[3] for item in items)
    compound = counts["compound-ing"] + counts["compound-zero"]
    lines = [
        "# Frozen item list of the dev compound-head measure (corpus cleanup C, `prenominal-ing` follow-up)",
        "# built by tools/DemaConsulting.PartOfSpeech.Trainer/prenominal-ing/build_items.py from gold labels, the lexicon and nothing else",
        "# input = data/open/dev.tsv sha256 " + common.sha256_file(DEV),
        "# input = data/lexicon/lexicon.tsv sha256 " + common.sha256_file(LEXICON),
        "# counts = compound items %d (compound-ing %d, compound-zero %d), verbal-ing items %d, total %d"
        % (compound, counts["compound-ing"], counts["compound-zero"], counts["verbal-ing"], len(items)),
        "# columns = sent_id, token_index, form, class, gold, next_form",
    ]
    lines.extend("\t".join((sid, str(i), form, kind, gold, nxt)) for sid, i, form, kind, gold, nxt in items)
    return lines


def main():
    parser = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    parser.add_argument("--write", action="store_true")
    parser.add_argument("--check", action="store_true")
    args = parser.parse_args()
    lines = render(build())
    if args.write:
        common.write_text(OUT, lines)
        print("wrote", common.rel(OUT), len(lines))
    elif args.check:
        with open(OUT, "r", encoding="utf-8", newline="") as handle:
            same = handle.read() == "\n".join(lines) + "\n"
        print("item list is current" if same else "item list is STALE")
        return 0 if same else 1
    else:
        print("\n".join(lines[:6]))
    return 0


if __name__ == "__main__":
    sys.exit(main())

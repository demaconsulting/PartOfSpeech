"""Writes tools/data/tests/sentence-key-cases.tsv: the parity cases of the sentence key (the leakage rule).

The expected keys are the output of `common.key`, the authoritative sentence key of the leakage check, taken once (run:
`python tools/data/make_sentence_key_cases.py`); the file is committed, and both the Python tests (test_common.py) and the trainer
tests (ModelTrainerTests.cs, the jackknife `SentenceKey`) must reproduce every key from every input. Add cases at the end; a
changed key of an existing case means that the rule changed.

Inputs and keys are lists of hexadecimal code points, so the file has no character that an editor can normalize or drop.
"""
import os
import sys

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import common

OUT = os.path.join(os.path.dirname(os.path.abspath(__file__)), "tests", "sentence-key-cases.tsv")

# (id, description, text). The cases avoid code points that the .NET Framework 4.8 case tables lack (the titlecase digraphs,
# the letters added after Unicode 5), because the trainer tests run on every target framework.
CASES = [
    ("empty", "the empty text", ""),
    ("punctuation-only", "no letter and no digit: the empty key", "... !? -- ()"),
    ("ascii-sentence", "case, spaces and punctuation are dropped", "Don't  PANIC, 42!"),
    ("typographic-apostrophe", "the typographic apostrophe is dropped like the plain one", "Don\u2019t panic"),
    ("latin-1-case", "lower case of the Latin-1 letters", "\u00c9COLE \u00e9cole \u00d1"),
    ("sharp-s", "the sharp s stays one letter", "Stra\u00dfe"),
    ("composed-e-acute", "the precomposed letter", "caf\u00e9"),
    ("decomposed-e-acute", "e and the combining acute compose to the same key", "cafe\u0301"),
    ("combining-mark-left-over", "a mark that does not compose is dropped", "a\u0300\u0301b"),
    ("combining-mark-alone", "a mark without a base is dropped", "\u0301\u0302"),
    ("dotted-capital-i", "the capital I with dot above lower-cases to i and a combining dot", "\u0130stanbul"),
    ("dotless-i", "the dotless i is a letter of its own", "\u0131stanbul ISTANBUL"),
    ("greek-final-sigma", "the capital sigma at the end of a word lower-cases to the final sigma", "\u039f\u0394\u03a5\u03a3\u03a3\u0395\u03a5\u03a3"),
    ("greek-medial-sigma", "the capital sigma inside a word lower-cases to the small sigma", "\u0391\u03a3\u0391"),
    ("greek-lone-sigma", "a capital sigma without a letter before it", "\u03a3 \u03a3\u0391"),
    ("greek-sigma-after-space", "a space is not case-ignorable, so the sigma after it is no final sigma", "\u0391 \u03a3"),
    ("greek-sigma-after-full-stop", "a full stop is case-ignorable, so the sigma behind it is final", "\u0391.\u03a3"),
    ("greek-sigma-before-full-stop", "the sigma before a full stop and a letter is no final sigma", "\u0391\u03a3.\u0391"),
    ("greek-sigma-before-mark", "a combining mark behind the sigma is skipped, so the sigma is final", "\u0391\u03a3\u0301"),
    ("greek-final-sigma-lower", "an already lower-case final sigma stays", "\u03bf\u03b4\u03c5\u03c3\u03c3\u03ad\u03b1\u03c2"),
    ("cyrillic", "Cyrillic case", "\u041f\u0420\u0418\u0412\u0415\u0422 \u043f\u0440\u0438\u0432\u0435\u0442"),
    ("fullwidth", "fullwidth Latin letters and digits lower-case and stay", "\uff21\uff22\uff23\uff11\uff12\uff13"),
    ("roman-numeral", "the Roman numeral four is a letter number and lower-cases", "\u2163 \u2173"),
    ("superscript-and-fraction", "the superscript two and the vulgar fraction are numbers", "x\u00b2 \u00bd"),
    ("arabic-indic-digits", "decimal digits of another script", "\u0661\u0662\u0663"),
    ("cjk-and-hangul", "ideographs and syllables are letters", "\u65e5\u672c\u8a9e \ud55c\uad6d\uc5b4"),
    ("symbols-and-spaces", "currency, math and no-break space are dropped", "\u20ac 5 + 3 = 8\u00a0\u00a3"),
    ("zero-width-joiner", "format characters are dropped", "a\u200db\u200bc"),
    ("emoji-astral", "an emoji of the supplementary plane is no letter", "hi \U0001f600 there"),
    ("musical-symbol-astral", "a musical symbol of the supplementary plane is no letter", "\U0001d11e clef"),
    ("deseret-capital-astral", "supplementary-plane letters with a lower case: Deseret", "\U00010400\U00010401\U00010427"),
    ("deseret-small-astral", "the lower-case Deseret letters give the same key", "\U00010428\U00010429\U0001044f"),
    ("osage-astral", "Osage", "\U000104b0\U000104d3"),
    ("vithkuqi-astral", "Vithkuqi (with the gaps of the block)", "\U00010570\U0001057a\U0001057c\U0001058a"),
    ("old-hungarian-astral", "Old Hungarian", "\U00010c80\U00010cb2"),
    ("warang-citi-astral", "Warang Citi", "\U000118a0\U000118bf"),
    ("medefaidrin-astral", "Medefaidrin", "\U00016e40\U00016e5f"),
    ("adlam-astral", "Adlam", "\U0001e900\U0001e921"),
    ("math-bold-astral", "mathematical bold letters are letters without a case mapping", "\U0001d400\U0001d401 \U0001d7ce\U0001d7cf"),
    ("cjk-extension-b-astral", "an ideograph of the supplementary plane is a letter", "\U00020000 x"),
    ("astral-with-latin", "supplementary-plane and Latin letters in one sentence", "The \U00010400 cat sat. \U0001f600 Done!"),
    ("astral-digits", "mathematical digits are decimal digits", "\U0001d7d8\U0001d7d9 7"),
]


def hexes(text):
    return " ".join("%X" % ord(ch) for ch in text)


# The Unicode version that a case needs when it is newer than the data of the .NET Framework 4.8 (Unicode 8): the trainer tests
# skip such cases on that framework only, the trainer itself is a .NET 10 tool.
SINCE = {
    "osage-astral": "9.0",
    "adlam-astral": "9.0",
    "medefaidrin-astral": "11.0",
    "vithkuqi-astral": "14.0",
}


def main():
    with open(OUT, "w", encoding="ascii", newline="\n") as out:
        out.write("# Parity cases of the sentence key of the leakage rule: the Python function (tools/data/common.py, key) wrote the\n")
        out.write("# keys; the Python tests (test_common.py) and the trainer (Jackknife.SentenceKey, ModelTrainerTests.cs) must reproduce\n")
        out.write("# them. Generated once by tools/data/make_sentence_key_cases.py; do not edit by hand.\n")
        out.write("# columns = id, input (hexadecimal code points), key (hexadecimal code points; empty = the empty key),\n")
        out.write("# since (Unicode version that the case needs when newer than 8.0, else empty; skipped on .NET Framework), description\n")
        for case_id, description, text in CASES:
            out.write("\t".join((case_id, hexes(text), hexes(common.key(text)), SINCE.get(case_id, ""), description)) + "\n")
    print("wrote", len(CASES), "cases to", OUT)


if __name__ == "__main__":
    sys.exit(main())

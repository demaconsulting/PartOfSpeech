"""Unit tests of tools/data/common.py (run: python -m unittest discover -s tools/data/tests)."""
import os
import sys
import tempfile
import unittest

sys.path.insert(0, os.path.dirname(os.path.dirname(os.path.abspath(__file__))))
import common


class KeyTests(unittest.TestCase):
    def test_key_keeps_lowercase_alphanumerics_only(self):
        self.assertEqual(common.key("Don't  PANIC, 42!"), "dontpanic42")

    def test_usable_key_ignores_short_sentences(self):
        self.assertIsNone(common.usable_key("Thank you."))
        self.assertIsNotNone(common.usable_key("The quick brown fox jumps over the lazy dog."))


class CliticTests(unittest.TestCase):
    def test_negative_contractions(self):
        self.assertEqual(common.split_clitics("don't"), ["do", "n't"])
        self.assertEqual(common.split_clitics("can't"), ["ca", "n't"])
        self.assertEqual(common.split_clitics("won't"), ["wo", "n't"])
        self.assertEqual(common.split_clitics("Ain't"), ["Ai", "n't"])
        self.assertEqual(common.split_clitics("cannot"), ["can", "not"])

    def test_clitics(self):
        self.assertEqual(common.split_clitics("I've"), ["I", "'ve"])
        self.assertEqual(common.split_clitics("John's"), ["John", "'s"])
        self.assertEqual(common.split_clitics("they're"), ["they", "'re"])

    def test_other_tokens_unchanged(self):
        for token in ("rock-n-roll", "o'clock", "dog", "'tis", "."):
            self.assertEqual(common.split_clitics(token), [token])

    def test_pieces_concatenate_to_token(self):
        for token in ("shouldn't", "she'll", "Tom's", "isn't"):
            self.assertEqual("".join(common.split_clitics(token)), token)


class ShapeRuleTests(unittest.TestCase):
    LEXICON = {"dog": frozenset({"NOUN"}), "run": frozenset({"NOUN", "VERB"}), ",": frozenset({"PUNCT"})}

    def test_unknown_spelling_allows_everything(self):
        self.assertEqual(common.allowed_tags("zzyzx", self.LEXICON), common.UPOS_SET)

    def test_known_spelling_allows_its_tags(self):
        self.assertEqual(common.allowed_tags("dog", self.LEXICON), {"NOUN"})
        self.assertEqual(common.allowed_tags("Dog", self.LEXICON), {"NOUN", "PROPN", "X"})

    def test_symbol_only_token_allows_punct_sym_x(self):
        self.assertEqual(common.allowed_tags(",", self.LEXICON), {"PUNCT", "SYM", "X"})

    def test_digit_token_allows_numeric_tags(self):
        lexicon = {"3d": frozenset({"ADJ"})}
        self.assertTrue({"NUM", "SYM", "X", "NOUN", "PROPN", "ADJ"} <= common.allowed_tags("3d", lexicon))

    def test_is_number(self):
        for token in ("3", "1,000", "3.5", "10:30"):
            self.assertTrue(common.is_number(token), token)
        for token in ("three", "1st", "a1"):
            self.assertFalse(common.is_number(token), token)


class FileFormatTests(unittest.TestCase):
    def test_corpus_roundtrip_is_lf_without_bom(self):
        sentences = [{"sent_id": "a-1", "source_id": "a", "text": "Hi #1", "extra": ["# genre = x"],
                      "rows": [["Hi", "INTJ", "ptb"], ["#", "SYM", "ptb"], ["1", "NUM", "ptb"]]}]
        with tempfile.TemporaryDirectory() as folder:
            path = os.path.join(folder, "c.tsv")
            common.write_corpus(path, ["# corpus = test"], sentences)
            with open(path, "rb") as handle:
                payload = handle.read()
            self.assertNotIn(b"\r", payload)
            self.assertFalse(payload.startswith(b"\xef\xbb\xbf"))
            self.assertTrue(payload.endswith(b"\n"))
            header, back = common.read_corpus(path)
        self.assertEqual(header, ["# corpus = test"])
        self.assertEqual(back[0]["rows"], sentences[0]["rows"])
        self.assertEqual(back[0]["comments"]["genre"], "x")

    def test_form_hash_is_a_row_not_a_comment(self):
        with tempfile.TemporaryDirectory() as folder:
            path = os.path.join(folder, "c.tsv")
            common.write_text(path, ["# sent_id = s", "# source_id = d", "# text = # a", "#\tSYM\tptb", "a\tDET\tptb", ""])
            rows = common.read_corpus(path)[1][0]["rows"]
        self.assertEqual(len(rows), 2)

    def test_write_text_rejects_embedded_newline(self):
        with tempfile.TemporaryDirectory() as folder:
            with self.assertRaises(ValueError):
                common.write_text(os.path.join(folder, "x.tsv"), ["a\nb"])


if __name__ == "__main__":
    unittest.main()

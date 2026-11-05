"""Unit tests of tools/data/common.py (run: python -m unittest discover -s tools/data/tests)."""
import os
import sys
import tempfile
import unittest

sys.path.insert(0, os.path.dirname(os.path.dirname(os.path.abspath(__file__))))
import common
import leakage


class KeyTests(unittest.TestCase):
    def test_key_keeps_lowercase_alphanumerics_only(self):
        self.assertEqual(common.key("Don't  PANIC, 42!"), "dontpanic42")

    def test_empty_key_for_text_without_letters_or_digits(self):
        self.assertEqual(common.key("... !?"), "")

    def test_short_sentences_keep_their_key(self):
        self.assertEqual(common.key("Thank you."), "thankyou")


class LeakageMatchTests(unittest.TestCase):
    KEYS = {"thankyou": "gold-v2", "thequickbrownfoxjumpsoverthelazydog": "open-dev"}

    def test_short_exact_sentence_matches_at_sentence_scope(self):
        self.assertEqual(leakage.match("Thank you.", self.KEYS), ("gold-v2", "sentence"))

    def test_long_exact_sentence_matches_at_document_scope(self):
        self.assertEqual(leakage.match("The quick brown fox jumps over the lazy dog.", self.KEYS), ("open-dev", "document"))

    def test_other_sentences_and_symbol_only_text_do_not_match(self):
        self.assertIsNone(leakage.match("Thank you very much.", self.KEYS))
        self.assertIsNone(leakage.match("...", {"": "open-dev"}))


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


class StatusFlagTests(unittest.TestCase):
    def test_plain_status_has_no_flags(self):
        for status in common.STATUS_BASES:
            self.assertEqual(common.parse_status(status), (status, ()))
            self.assertEqual(common.base_status(status), status)

    def test_flags_follow_the_status_in_canonical_order(self):
        self.assertEqual(common.parse_status("unanimous+mentioned"), ("unanimous", ("mentioned",)))
        self.assertEqual(common.parse_status("disputed+foreign+mentioned"), ("disputed", ("foreign", "mentioned")))
        self.assertEqual(common.base_status("disputed+foreign+mentioned"), "disputed")

    def test_format_status_orders_and_roundtrips(self):
        self.assertEqual(common.format_status("silver"), "silver")
        self.assertEqual(common.format_status("unchecked", ("mentioned", "foreign")), "unchecked+foreign+mentioned")
        self.assertEqual(common.parse_status(common.format_status("ptb", ("mentioned",))), ("ptb", ("mentioned",)))

    def test_malformed_cells_are_refused(self):
        for cell in ("", "bogus", "unanimous+", "unanimous+shouted", "unanimous+mentioned+mentioned",
                     "unanimous+mentioned+foreign", "+mentioned", "mentioned"):
            with self.assertRaises(ValueError, msg=cell):
                common.parse_status(cell)
        with self.assertRaises(ValueError):
            common.format_status("unanimous", ("mentioned", "mentioned"))

    def test_flagged_rows_roundtrip_through_the_corpus_files(self):
        sentences = [{"sent_id": "a-1", "source_id": "a", "text": "Sierra is a Spanish word",
                      "rows": [["Sierra", "NOUN", "unanimous+foreign+mentioned"], ["is", "VERB", "unanimous"],
                               ["a", "DET", "unanimous"], ["Spanish", "PROPN", "unchecked"], ["word", "NOUN", "unanimous"]]}]
        with tempfile.TemporaryDirectory() as folder:
            path = os.path.join(folder, "c.tsv")
            common.write_corpus(path, ["# corpus = test"], sentences)
            back = common.read_corpus(path)[1]
        self.assertEqual(back[0]["rows"], sentences[0]["rows"])


class ReaderParityTests(unittest.TestCase):
    """The strict reader gives the verdicts of the trainer's reader (shared cases: tests/status-cases.tsv)."""

    CASES = os.path.join(os.path.dirname(os.path.abspath(__file__)), "status-cases.tsv")

    def cases(self):
        rows = [line.split("\t") for line in common.read_lines(self.CASES) if line and not line.startswith("# ")]
        self.assertGreater(len(rows), 20)
        return rows

    def test_every_shared_case_has_the_agreed_verdict(self):
        for tag, cell, result, status, flags in self.cases():
            with self.subTest(tag=tag, cell=cell):
                row = ["a", tag, cell]
                if result == "accept":
                    common.check_row("fixture", 2, row)
                    self.assertEqual(common.parse_status(cell), (status, () if flags == "-" else tuple(flags.split(","))))
                else:
                    with self.assertRaises(ValueError) as caught:
                        common.check_row("fixture", 2, row)
                    expected = "invalid status '%s'" % cell if result == "invalid-status" else "unknown tag '%s'" % tag
                    self.assertIn(expected, str(caught.exception))
                    self.assertIn("fixture, line 2: ", str(caught.exception))

    def test_a_row_with_fewer_than_three_cells_is_refused(self):
        with self.assertRaises(ValueError) as caught:
            common.check_row("fixture", 2, ["a", "NOUN"])
        self.assertIn("a token row needs at least form, tag and status", str(caught.exception))

    def test_read_corpus_refuses_malformed_rows_with_their_line_number(self):
        for tag, cell, result, status, flags in self.cases():
            if result == "accept":
                continue
            with self.subTest(tag=tag, cell=cell), tempfile.TemporaryDirectory() as folder:
                path = os.path.join(folder, "c.tsv")
                common.write_text(path, ["# corpus = test", "", "# sent_id = s1", "# source_id = d", "# text = a", "a\t%s\t%s" % (tag, cell)])
                with self.assertRaises(ValueError) as caught:
                    common.read_corpus(path)
                self.assertIn(", line 6: ", str(caught.exception))
                common.read_corpus(path, strict=False)  # the lenient reader of the open source format still reads it

    def test_read_corpus_accepts_flagged_rows(self):
        with tempfile.TemporaryDirectory() as folder:
            path = os.path.join(folder, "c.tsv")
            common.write_text(path, ["# corpus = test", "", "# sent_id = s1", "# source_id = d", "# text = Sierra",
                                     "Sierra\tNOUN\tunanimous+foreign+mentioned"])
            rows = common.read_corpus(path)[1][0]["rows"]
        self.assertEqual(rows, [["Sierra", "NOUN", "unanimous+foreign+mentioned"]])


class HeaderDocumentationTests(unittest.TestCase):
    HEADER = ["# corpus = test", "# columns = form, upos, status", "# status = unanimous | disputed | unchecked", "# origin = x"]

    def test_header_is_unchanged_without_flags(self):
        sentences = [{"rows": [["a", "DET", "unanimous"], ["b", "NOUN", "disputed"]]}]
        self.assertEqual(common.with_flag_documentation(self.HEADER, sentences), self.HEADER)

    def test_header_documents_the_flags_when_a_row_has_one(self):
        sentences = [{"rows": [["a", "DET", "unanimous"], ["b", "NOUN", "disputed+mentioned"]]}]
        header = common.with_flag_documentation(self.HEADER, sentences)
        self.assertEqual(len(header), len(self.HEADER) + 1)
        self.assertTrue(header[3].startswith("# status flags = "))
        self.assertIn("+foreign", header[3])
        self.assertIn("+mentioned", header[3])


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
            self.assertFalse(payload.endswith(b"\n\n"))  # no blank line at the end of the file
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

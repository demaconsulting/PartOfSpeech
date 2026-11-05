"""Tests of the corrections overlay (tools/data/corrections.py): rows, tables, state-aware validation, apply and reverse."""
import contextlib
import io
import os
import sys
import tempfile
import unittest

sys.path.insert(0, os.path.dirname(os.path.dirname(os.path.abspath(__file__))))
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import common
import corrections
import corrections_testdata

CASES = os.path.join(os.path.dirname(os.path.abspath(__file__)), "corrections-cases.tsv")
HEADER = ["# corpus = test", "# columns = " + ", ".join(corrections.COLUMNS["corrections"])]

OUT_OF = corrections.Correction("open", "train", "d1", "t1", "2", "out", "ADV", "ADP", "unanimous", "unanimous", "rule:out-of")
ALL_OF = corrections.Correction("open", "train", "d1", "t2", "0", "All", "PRON", "DET", "unchecked", "unchecked", "rule:partitive-det")
SUCH_AS = corrections.Correction("masc", "-", "masc:written/blog/x", "masc:written/blog/x#1", "1", "such", "ADP", "ADJ", "ptb", "ptb",
                                 "rule:multiword-prep")
CAN_NOT = corrections.Correction("silver", "-", "gutenberg-1", "silver-1", "1", "can", "VERB", "AUX", "silver", "silver", "rule:modal-aux")
MENTION = corrections.Correction("open", "train", "d2", "t3", "3", "snowball", "NOUN", "NOUN", "disputed", "disputed+mentioned",
                                 "llm:model-a:run1")


def case_rows():
    """The shared row cases: (case_id, row, expected result)."""
    cases = []
    for line in common.read_lines(CASES):
        if line and not line.startswith("# "):
            cells = line.split("\t")
            cases.append((cells[0], corrections.parse_row("corrections", cells[1:12], cells[0]), cells[12]))
    return cases


def run(function, *args):
    """Run a command-line function, returning (exit code, printed text)."""
    buffer = io.StringIO()
    with contextlib.redirect_stdout(buffer):
        code = function(*args)
    return code, buffer.getvalue()


class RowCaseTests(unittest.TestCase):
    def test_shared_cases_give_the_documented_verdict(self):
        # Arrange: the shared fixture of valid and invalid rows
        cases = case_rows()
        # Act and Assert: the first error code of every row is the one the fixture names
        self.assertGreater(len(cases), 25)
        for case_id, row, expected in cases:
            with self.subTest(case=case_id):
                problem = corrections.row_error(row)
                self.assertEqual(expected, "valid" if problem is None else problem[0])

    def test_every_error_code_is_exercised_by_the_fixture(self):
        # Arrange
        codes = {expected for _, _, expected in case_rows()}
        # Act and Assert
        for code in ("bad-source", "bad-split", "bad-index", "bad-token", "bad-tag", "masked-tag", "bad-status", "no-change",
                     "status-base-change", "mechanical-status-change", "bad-provenance", "valid"):
            self.assertIn(code, codes)


class TableTests(unittest.TestCase):
    def test_serialize_then_parse_is_the_identity(self):
        # Arrange: rows in the wrong order
        rows = [ALL_OF, OUT_OF, MENTION]
        # Act
        lines = corrections.format_table("corrections", HEADER, rows)
        header, parsed = corrections.parse_table("corrections", lines)
        # Assert: sorted by the key, header kept, a second serialization is identical
        self.assertEqual(HEADER, header)
        self.assertEqual(sorted(rows, key=corrections.sort_key), parsed)
        self.assertEqual(lines, corrections.format_table("corrections", header, parsed))

    def test_sort_key_orders_token_indices_numerically(self):
        # Arrange
        early = OUT_OF._replace(token_index="9")
        late = OUT_OF._replace(token_index="10")
        # Act and Assert
        self.assertLess(corrections.sort_key(early), corrections.sort_key(late))

    def test_a_table_without_the_columns_line_or_with_other_columns_is_refused(self):
        # Arrange, Act and Assert
        with self.assertRaises(ValueError):
            corrections.parse_table("corrections", ["# corpus = test"])
        with self.assertRaises(ValueError):
            corrections.parse_table("corrections", ["# columns = source, split"])
        with self.assertRaises(ValueError):
            corrections.parse_table("corrections", HEADER + ["open\ttrain"])
        with self.assertRaises(ValueError):
            corrections.parse_table("corrections", HEADER + ["\t".join(OUT_OF), "# late comment"])
        with self.assertRaises(ValueError):
            corrections.parse_table("corrections", HEADER + [""])

    def test_duplicates_conflicts_and_unsorted_rows_are_reported(self):
        # Arrange
        other = OUT_OF._replace(old_tag="ADV", new_tag="INTJ")
        # Act
        duplicate = corrections.table_errors([OUT_OF, OUT_OF])
        conflict = corrections.table_errors([OUT_OF, other])
        unsorted_rows = corrections.table_errors([ALL_OF, OUT_OF])
        clean = corrections.table_errors(sorted([OUT_OF, ALL_OF, SUCH_AS], key=corrections.sort_key))
        # Assert
        self.assertTrue(any(error.startswith("duplicate") for error in duplicate))
        self.assertTrue(any(error.startswith("conflict") for error in conflict))
        self.assertTrue(any(error.startswith("unsorted") for error in unsorted_rows))
        self.assertEqual([], clean)

    def test_a_masked_or_invalid_row_is_an_error_of_the_table(self):
        # Arrange
        masked = CAN_NOT._replace(old_tag="_")
        # Act
        errors = corrections.table_errors([masked])
        # Assert
        self.assertTrue(errors[0].startswith("masked-tag"))


class CorpusValidationTests(unittest.TestCase):
    def setUp(self):
        self.temp = tempfile.TemporaryDirectory()
        self.addCleanup(self.temp.cleanup)
        self.data = corrections_testdata.write_data_dir(self.temp.name)
        self.corpora = corrections.load_corpora(self.data)

    def test_rows_that_match_the_old_values_are_pending(self):
        errors, state = corrections.validate_against_corpora([OUT_OF, ALL_OF, SUCH_AS, CAN_NOT, MENTION], self.corpora)
        self.assertEqual(([], "pending"), (errors, state))

    def test_rows_that_match_the_new_values_are_applied(self):
        # Arrange
        corrections.apply_in_memory(self.corpora, [OUT_OF, ALL_OF])
        # Act
        errors, state = corrections.validate_against_corpora([OUT_OF, ALL_OF], self.corpora)
        # Assert
        self.assertEqual(([], "applied"), (errors, state))

    def test_rows_of_both_states_are_a_mixed_state_error(self):
        # Arrange
        corrections.apply_in_memory(self.corpora, [OUT_OF])
        # Act
        errors, state = corrections.validate_against_corpora([OUT_OF, ALL_OF], self.corpora)
        # Assert
        self.assertEqual("mixed", state)
        self.assertTrue(any(error.startswith("mixed-state") for error in errors))

    def test_an_empty_table_is_valid_and_empty(self):
        self.assertEqual(([], "empty"), corrections.validate_against_corpora([], self.corpora))

    def test_every_kind_of_mismatch_is_named(self):
        # Arrange
        cases = {
            "unknown-sentence": OUT_OF._replace(sent_id="nope"),
            "source-id-mismatch": OUT_OF._replace(source_id="other"),
            "token-index-out-of-range": OUT_OF._replace(token_index="99"),
            "token-mismatch": OUT_OF._replace(token="in"),
            "state-mismatch": OUT_OF._replace(old_tag="NOUN"),
            "no-corpus": OUT_OF._replace(split="test"),
        }
        # Act and Assert
        for code, row in cases.items():
            with self.subTest(code=code):
                errors, _ = corrections.validate_against_corpora([row], self.corpora)
                self.assertTrue(errors and errors[0].startswith(code), errors)

    def test_candidates_are_checked_for_address_token_and_span(self):
        # Arrange
        row = corrections.ROW_TYPES["candidates"]("open", "train", "d2", "t3", "3", "1", "snowball", "NOUN", "disputed", "",
                                                  "mention-quoted", "quote=\" \"", "The word [snowball] is fun")
        # Act and Assert
        self.assertEqual([], corrections.validate_candidates_against_corpora([row], self.corpora))
        self.assertTrue(corrections.validate_candidates_against_corpora([row._replace(span_length="9")], self.corpora))
        self.assertTrue(corrections.validate_candidates_against_corpora([row._replace(token="x")], self.corpora))
        self.assertTrue(corrections.validate_candidates_against_corpora([row._replace(current_tag="VERB")], self.corpora))
        self.assertEqual([], corrections.validate_candidates_against_corpora([row._replace(current_tag="VERB")], self.corpora,
                                                                              check_tags=False))


class ApplyTests(unittest.TestCase):
    def setUp(self):
        self.temp = tempfile.TemporaryDirectory()
        self.addCleanup(self.temp.cleanup)
        self.data = corrections_testdata.write_data_dir(self.temp.name)

    def lines(self, *parts):
        return common.read_lines(os.path.join(self.data, *parts))

    def test_apply_changes_only_cells_one_and_two_and_reverse_restores_every_byte(self):
        # Arrange
        old = self.lines("open", "train.tsv")
        # Act
        new = corrections.apply_to_lines(old, [OUT_OF, ALL_OF])
        back = corrections.apply_to_lines(new, [OUT_OF, ALL_OF], reverse=True)
        # Assert
        changed = [(a, b) for a, b in zip(old, new) if a != b]
        self.assertEqual([("out\tADV\tunanimous", "out\tADP\tunanimous"), ("All\tPRON\tunchecked", "All\tDET\tunchecked")], changed)
        self.assertEqual(len(old), len(new))
        self.assertEqual(old, back)

    def test_masc_comment_lines_and_the_penn_column_survive(self):
        # Arrange
        old = self.lines("masc", "masc.tsv")
        # Act
        new = corrections.apply_to_lines(old, [SUCH_AS])
        # Assert
        self.assertEqual(old.count("# genre = blog"), new.count("# genre = blog"))
        self.assertIn("such\tADJ\tptb\tIN", new)
        self.assertEqual(old, corrections.apply_to_lines(new, [SUCH_AS], reverse=True))

    def test_the_first_flag_adds_the_header_note_and_the_last_flag_removes_it(self):
        # Arrange
        old = self.lines("open", "train.tsv")
        status_line = "# status = unanimous | disputed | unchecked (consensus of the two annotators)"
        # Act
        flagged = corrections.apply_to_lines(old, [MENTION])
        back = corrections.apply_to_lines(flagged, [MENTION], reverse=True)
        # Assert
        self.assertEqual(old.index(status_line) + 1, flagged.index(corrections.flag_note()))
        self.assertIn("snowball\tNOUN\tdisputed+mentioned", flagged)
        self.assertNotIn(corrections.flag_note(), back)
        self.assertEqual(old, back)

    def test_the_flag_note_is_written_once_for_two_flagged_rows(self):
        # Arrange
        second = MENTION._replace(sent_id="t2", token_index="3", token="left", old_tag="VERB", new_tag="VERB",
                                  old_status="unchecked", new_status="unchecked+mentioned")
        # Act
        flagged = corrections.apply_to_lines(self.lines("open", "train.tsv"), [MENTION, second])
        # Assert
        self.assertEqual(1, flagged.count(corrections.flag_note()))

    def test_a_row_that_does_not_match_the_file_is_refused(self):
        # Arrange
        lines = self.lines("open", "train.tsv")
        # Act and Assert
        with self.assertRaises(ValueError):
            corrections.apply_to_lines(lines, [OUT_OF._replace(old_tag="NOUN")])
        with self.assertRaises(ValueError):
            corrections.apply_to_lines(lines, [OUT_OF._replace(token_index="40")])
        with self.assertRaises(ValueError):
            corrections.apply_to_lines(lines, [OUT_OF._replace(sent_id="missing")])
        with self.assertRaises(ValueError):
            corrections.apply_to_lines(lines, [OUT_OF], reverse=True)

    def test_a_masked_row_cannot_be_addressed_by_a_valid_correction(self):
        # Arrange: the masked token `go` of the silver fixture
        masked = CAN_NOT._replace(token_index="3", token="go", old_tag="_", new_tag="VERB", old_status="masked", new_status="masked")
        # Act
        problem = corrections.row_error(masked)
        # Assert
        self.assertEqual("masked-tag", problem[0])

    def test_in_memory_application_equals_the_application_to_the_file(self):
        # Arrange
        corpora = corrections.load_corpora(self.data)
        rows = [OUT_OF, ALL_OF, SUCH_AS, CAN_NOT, MENTION]
        # Act
        count = corrections.apply_in_memory(corpora, rows)
        files = corrections.apply_to_files(rows, self.data)
        # Assert: apply_to_files did not write, and its result parses to the same rows as the in-memory corpora
        self.assertEqual(5, count)
        for key, (old, new) in files.items():
            self.assertEqual(old, self.lines(*corrections.CORPUS_FILES[key].split("/")))
            path = os.path.join(self.temp.name, "applied-%s-%s.tsv" % key[:2] if key[1] != "-" else "applied-%s.tsv" % key[0])
            common.write_text(path, new)
            self.assertEqual([sentence["rows"] for sentence in common.read_corpus(path)[1]],
                             [sentence["rows"] for sentence in corpora[key].sentences])

    def test_write_replaces_the_files_atomically_and_reverse_restores_the_bytes(self):
        # Arrange
        path = os.path.join(self.data, "open", "train.tsv")
        with open(path, "rb") as handle:
            before = handle.read()
        rows = [OUT_OF, ALL_OF, MENTION]
        # Act
        corrections.apply_to_files(rows, self.data, write=True)
        with open(path, "rb") as handle:
            after = handle.read()
        corrections.apply_to_files(rows, self.data, reverse=True, write=True)
        with open(path, "rb") as handle:
            restored = handle.read()
        # Assert
        self.assertNotEqual(before, after)
        self.assertNotIn(b"\r", after)
        self.assertEqual(before, restored)
        self.assertEqual([], [name for name in os.listdir(os.path.dirname(path)) if name.endswith(".part")])


class CommandLineTests(unittest.TestCase):
    def setUp(self):
        self.temp = tempfile.TemporaryDirectory()
        self.addCleanup(self.temp.cleanup)
        self.data = corrections_testdata.write_data_dir(self.temp.name)
        self.folder = corrections.corrections_dir(self.data)
        self.rows = [OUT_OF, ALL_OF, SUCH_AS, CAN_NOT]

    def write_mechanical(self, rows):
        corrections.write_table(os.path.join(self.folder, "mechanical.tsv"), "corrections", HEADER, rows)

    def test_validate_accepts_a_matching_table_and_names_the_state(self):
        # Arrange
        self.write_mechanical(self.rows)
        # Act
        code, text = run(corrections.main, ["--data-dir", self.data, "validate"])
        # Assert
        self.assertEqual(0, code)
        self.assertIn("pending", text)

    def test_validate_fails_on_a_row_that_does_not_match(self):
        # Arrange
        self.write_mechanical([OUT_OF._replace(token="in")])
        # Act
        code, text = run(corrections.main, ["--data-dir", self.data, "validate"])
        # Assert
        self.assertEqual(1, code)
        self.assertIn("token-mismatch", text)

    def test_validate_reports_a_conflict_between_two_tables(self):
        # Arrange
        self.write_mechanical([OUT_OF])
        other = OUT_OF._replace(new_tag="INTJ", provenance="llm:model-a:run1")
        corrections.write_table(os.path.join(self.folder, "llm.tsv"), "corrections", HEADER, [other])
        # Act
        code, text = run(corrections.main, ["--data-dir", self.data, "validate"])
        # Assert
        self.assertEqual(1, code)
        self.assertIn("across tables: conflict", text)

    def test_apply_is_a_dry_run_unless_write_is_given_and_reverse_undoes_it(self):
        # Arrange
        self.write_mechanical(self.rows)
        path = os.path.join(self.data, "open", "train.tsv")
        with open(path, "rb") as handle:
            before = handle.read()
        # Act
        code_dry, text_dry = run(corrections.main, ["--data-dir", self.data, "apply"])
        with open(path, "rb") as handle:
            after_dry = handle.read()
        code_write, _ = run(corrections.main, ["--data-dir", self.data, "apply", "--write"])
        with open(path, "rb") as handle:
            after_write = handle.read()
        code_valid, text_valid = run(corrections.main, ["--data-dir", self.data, "validate"])
        code_back, _ = run(corrections.main, ["--data-dir", self.data, "apply", "--write", "--reverse"])
        with open(path, "rb") as handle:
            restored = handle.read()
        # Assert
        self.assertEqual((0, 0, 0, 0), (code_dry, code_write, code_valid, code_back))
        self.assertIn("nothing written", text_dry)
        self.assertEqual(before, after_dry)
        self.assertNotEqual(before, after_write)
        self.assertIn("applied", text_valid)
        self.assertEqual(before, restored)

    def test_apply_refuses_when_the_corpora_are_in_the_other_state(self):
        # Arrange
        self.write_mechanical(self.rows)
        run(corrections.main, ["--data-dir", self.data, "apply", "--write"])
        # Act
        code, text = run(corrections.main, ["--data-dir", self.data, "apply", "--write"])
        # Assert
        self.assertEqual(1, code)
        self.assertIn("refusing", text)

    def test_show_prints_the_rows_in_context(self):
        # Arrange
        self.write_mechanical(self.rows)
        # Act
        code, text = run(corrections.main, ["--data-dir", self.data, "show", "--sent-id", "t1"])
        # Assert
        self.assertEqual(0, code)
        self.assertIn("It is [out] of order .", text)


if __name__ == "__main__":
    unittest.main()

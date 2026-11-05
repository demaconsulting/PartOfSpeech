"""Tests of the convention linter (tools/data/check_conventions.py) on the miniature data folder."""
import contextlib
import io
import os
import sys
import tempfile
import unittest

sys.path.insert(0, os.path.dirname(os.path.dirname(os.path.abspath(__file__))))
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import check_conventions
import common
import convention_rules as R
import corrections
import corrections_testdata
import make_corrections


def run(argv):
    """Run the linter's main, returning (exit code, printed text)."""
    buffer = io.StringIO()
    with contextlib.redirect_stdout(buffer):
        code = check_conventions.main(argv)
    return code, buffer.getvalue()


class LinterTests(unittest.TestCase):
    def setUp(self):
        self.temp = tempfile.TemporaryDirectory()
        self.addCleanup(self.temp.cleanup)
        self.data = corrections_testdata.write_data_dir(self.temp.name)

    def lint(self, **options):
        _, sentences = check_conventions.load_sentences(self.data, options.pop("apply_overlay", False))
        resources = R.build_resources(s for s in sentences if s.source != "ste")
        return check_conventions.lint(sentences, resources, **options)

    def stage_corrections(self):
        files, _, _, _, _ = make_corrections.generate_all(self.data)
        make_corrections.write_files(files, self.data)

    def test_the_fixture_violations_are_counted_per_rule_and_source(self):
        # Arrange and Act
        violations, suppressed = self.lint()
        # Assert
        counts = check_conventions.counts_table(violations)
        self.assertEqual(0, suppressed)
        self.assertEqual({("open", "train"): 1}, dict(counts["out-of"]))
        self.assertEqual({("open", "train"): 1, ("ste", "-"): 1}, dict(counts["partitive-det"]))
        self.assertEqual({("open", "dev"): 1}, dict(counts["partitive-adj"]))
        self.assertEqual({("masc", "-"): 1}, dict(counts["multiword-prep"]))
        self.assertEqual({("silver", "-"): 1}, dict(counts["modal-aux"]))

    def test_the_report_lists_the_rules_with_their_counts_and_a_total(self):
        # Arrange
        violations, _ = self.lint()
        # Act
        report = check_conventions.format_report(violations, suppressed=2)
        # Assert
        text = "\n".join(report)
        self.assertIn("out-of", text)
        self.assertIn("suppressed by exceptions: 2", text)
        self.assertTrue(report[-2].startswith("total"))

    def test_an_exception_suppresses_exactly_its_violation(self):
        # Arrange
        exception = frozenset([("open", "train", "t1", 2, "out-of")])
        # Act
        violations, suppressed = self.lint(exceptions=exception)
        # Assert
        self.assertEqual(1, suppressed)
        self.assertNotIn("out-of", {item.rule for item in violations})
        self.assertIn("partitive-det", {item.rule for item in violations})

    def test_the_tier_and_rule_filters_select_what_is_reported(self):
        # Arrange and Act
        only_check, _ = self.lint(tiers=(R.CHECK,))
        only_rule, _ = self.lint(only={"modal-aux"})
        # Assert
        self.assertEqual([], [item for item in only_check if item.tier == R.FIX])
        self.assertEqual({"modal-aux"}, {item.rule for item in only_rule})

    def test_enforce_fails_while_violations_are_left_and_report_only_does_not(self):
        # Arrange and Act
        report_code, report_text = run(["--data-dir", self.data])
        enforce_code, enforce_text = run(["--data-dir", self.data, "--enforce"])
        # Assert
        self.assertEqual(0, report_code)
        self.assertNotIn("FAILED", report_text)
        self.assertEqual(1, enforce_code)
        self.assertIn("FAILED", enforce_text)

    def test_apply_lints_the_corpora_as_they_are_after_the_staged_corrections(self):
        # Arrange: stage the generated corrections
        self.stage_corrections()
        # Act
        before, _ = self.lint()
        after, _ = self.lint(apply_overlay=True)
        # Assert: only the evaluation-only STE violation is left, the committed corpus files are unchanged
        self.assertGreater(len([item for item in before if item.source != "ste"]), 0)
        self.assertEqual([("ste", "partitive-det")], [(item.source, item.rule) for item in after])
        self.assertEqual("PRON", common.read_corpus(os.path.join(self.data, "open", "train.tsv"))[1][1]["rows"][0][1])

    def test_enforce_with_apply_passes_when_only_the_evaluation_only_set_is_left(self):
        # Arrange
        self.stage_corrections()
        # Act
        code, text = run(["--data-dir", self.data, "--apply", "--enforce"])
        steered_code, steered_text = run(["--data-dir", self.data, "--apply", "--source", "ste"])
        # Assert
        self.assertEqual(0, code)
        self.assertNotIn("FAILED", text)
        self.assertEqual(0, steered_code)
        self.assertIn("partitive-det", steered_text)

    def test_the_tsv_output_has_one_row_per_violation(self):
        # Arrange
        path = os.path.join(self.temp.name, "violations.tsv")
        # Act
        run(["--data-dir", self.data, "--tsv", path])
        lines = common.read_lines(path)
        # Assert
        self.assertTrue(lines[0].startswith("# columns = rule, tier, source, split"))
        self.assertEqual(len(self.lint()[0]), len(lines) - 1)
        self.assertTrue(any(line.startswith("out-of\tfix\topen\ttrain\td1\tt1\t2\tout\tADV\tADP\t") for line in lines))

    def test_list_prints_the_violations_of_one_rule_in_context(self):
        # Arrange and Act
        code, text = run(["--data-dir", self.data, "--list", "out-of"])
        # Assert
        self.assertEqual(0, code)
        self.assertIn("It is [out] of order .", text)
        self.assertIn("1 violations of out-of", text)

    def test_an_unknown_rule_id_is_refused(self):
        # Arrange, Act and Assert
        with contextlib.redirect_stderr(io.StringIO()), self.assertRaises(SystemExit):
            run(["--data-dir", self.data, "--rule", "no-such-rule"])

    def test_a_table_that_does_not_match_the_corpora_cannot_be_applied(self):
        # Arrange
        self.stage_corrections()
        path = os.path.join(corrections.corrections_dir(self.data), "mechanical.tsv")
        lines = common.read_lines(path)
        common.write_text(path, [line.replace("\tADV\tADP\t", "\tNOUN\tADP\t") for line in lines])
        # Act and Assert
        with self.assertRaises(ValueError):
            check_conventions.load_sentences(self.data, apply_overlay=True)


if __name__ == "__main__":
    unittest.main()

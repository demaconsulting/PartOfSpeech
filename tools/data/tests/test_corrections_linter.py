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


IMPERATIVE_CLEAN = """# corpus = test imperative source
# columns = form, upos, status
# status = unanimous
# use = training

# sent_id = imperative-0001
# source_id = army-doc
# text = Check the oil level .
Check\tVERB\tunanimous
the\tDET\tunanimous
oil\tNOUN\tunanimous
level\tNOUN\tunanimous
.\tPUNCT\tunanimous
"""

IMPERATIVE_VIOLATING = IMPERATIVE_CLEAN + """
# sent_id = imperative-0002
# source_id = army-doc
# text = It is out of order .
It\tPRON\tunanimous
is\tVERB\tunanimous
out\tADV\tunanimous
of\tADP\tunanimous
order\tNOUN\tunanimous
.\tPUNCT\tunanimous
"""

IMPERATIVE_GRADABLE = IMPERATIVE_CLEAN + """
# sent_id = imperative-0002
# source_id = army-doc
# text = They were very daubed .
They\tPRON\tunanimous
were\tVERB\tunanimous
very\tADV\tunanimous
daubed\tADJ\tunanimous
.\tPUNCT\tunanimous
"""

OPEN_DAUBED = """# corpus = test open corpus, train split
# columns = form, upos, status

# sent_id = t9
# source_id = d9
# text = He was daubed .
He\tPRON\tunanimous
was\tVERB\tunanimous
daubed\tADJ\tunanimous
.\tPUNCT\tunanimous
"""


class ImperativeSourceTests(unittest.TestCase):
    """The imperative training source is linted (its own column), counts for `--enforce`, is never corrected and does not feed the corpus resources."""

    def setUp(self):
        self.temp = tempfile.TemporaryDirectory()
        self.addCleanup(self.temp.cleanup)
        self.data = corrections_testdata.write_data_dir(self.temp.name)
        self.path = os.path.join(self.data, "imperative", "imperative.tsv")

    def write(self, text):
        common.write_text(self.path, text.rstrip("\n").split("\n"))

    def test_a_fix_tier_violation_in_the_source_is_reported_in_its_column_and_fails_enforce_after_the_corrections(self):
        # Arrange: stage the corrections of the correctable corpora, then add a source with a violation
        files, _, _, _, _ = make_corrections.generate_all(self.data)
        make_corrections.write_files(files, self.data)
        self.write(IMPERATIVE_VIOLATING)
        # Act
        code, text = run(["--data-dir", self.data, "--apply", "--tier", "fix", "--enforce"])
        listed_code, listed = run(["--data-dir", self.data, "--apply", "--source", "imperative", "--tier", "fix"])
        # Assert: only the imperative source is left, it counts, and the column is shown
        self.assertEqual(1, code)
        self.assertIn("FAILED: 1 convention violations", text)
        self.assertIn("imperative", text.splitlines()[0])
        self.assertEqual(0, listed_code)
        self.assertIn("out-of", listed)

    def test_a_clean_source_passes_enforce_and_the_staged_corrections_never_address_it(self):
        # Arrange
        before, _, _, _, _ = make_corrections.generate_all(self.data)
        self.write(IMPERATIVE_CLEAN)
        after, _, corpora, _, _ = make_corrections.generate_all(self.data)
        files, _, _, _, _ = make_corrections.generate_all(self.data)
        make_corrections.write_files(files, self.data)
        # Act
        code, text = run(["--data-dir", self.data, "--apply", "--tier", "fix", "--enforce"])
        # Assert: the generated tables are the same with and without the source, and the lint passes
        self.assertEqual(before, after)
        self.assertNotIn(("imperative", "-"), corpora)
        self.assertEqual(0, code)
        self.assertNotIn("FAILED", text)

    def test_the_source_does_not_feed_the_corpus_resources_of_the_rules(self):
        # Arrange: an open sentence with an agentless participle that no degree word shows to be gradable
        common.write_text(os.path.join(self.data, "open", "train.tsv"), OPEN_DAUBED.rstrip("\n").split("\n"))
        without = run(["--data-dir", self.data, "--tier", "check", "--source", "open"])[1]
        # Act: add a source that shows the participle with a degree word
        self.write(IMPERATIVE_GRADABLE)
        with_source = run(["--data-dir", self.data, "--tier", "check", "--source", "open"])[1]
        # Assert: the violation of the open sentence is the same, so the resources came from the correctable corpora only
        self.assertIn("be-participle-adj-not-gradable", without)
        self.assertEqual(without, with_source)
        self.assertEqual(("ste", "imperative", "compound", "headings", "compound2", "headings2"), check_conventions.RESOURCE_EXEMPT)
        self.assertEqual((("imperative", "-"), ("compound", "-"), ("headings", "-"), ("compound2", "-"), ("headings2", "-")), check_conventions.COLUMN_ORDER[-5:])
        self.assertIn(("imperative", "-"), corrections.LINT_ONLY_FILES)
        self.assertNotIn(("imperative", "-"), corrections.CORPUS_FILES)


class TargetedSourcesTests(unittest.TestCase):
    """The compound and headings training sources are linted like the imperative source: own columns, counted by `--enforce`, never corrected, no corpus resources."""

    SOURCES = ("compound", "headings")

    def setUp(self):
        self.temp = tempfile.TemporaryDirectory()
        self.addCleanup(self.temp.cleanup)
        self.data = corrections_testdata.write_data_dir(self.temp.name)

    def write(self, source, text):
        common.write_text(os.path.join(self.data, source, source + ".tsv"), text.replace("imperative", source).rstrip("\n").split("\n"))

    def test_the_sources_are_registered_as_lint_only_and_exempt_from_the_resources(self):
        for source in self.SOURCES:
            self.assertIn((source, "-"), check_conventions.COLUMN_ORDER)
            self.assertIn(source, check_conventions.RESOURCE_EXEMPT)
            self.assertEqual("%s/%s.tsv" % (source, source), corrections.LINT_ONLY_FILES[(source, "-")])
            self.assertNotIn((source, "-"), corrections.CORPUS_FILES)

    def test_the_follow_up_sources_are_registered_like_the_old_ones_and_a_fix_tier_violation_fails_enforce(self):
        files = {"compound2": "compound/compound-2.tsv", "headings2": "headings/headings-2.tsv"}
        for source, relative in files.items():
            self.assertIn((source, "-"), check_conventions.COLUMN_ORDER)
            self.assertIn(source, check_conventions.RESOURCE_EXEMPT)
            self.assertEqual(relative, corrections.LINT_ONLY_FILES[(source, "-")])
            self.assertNotIn((source, "-"), corrections.CORPUS_FILES)
        generated, _, _, _, _ = make_corrections.generate_all(self.data)
        make_corrections.write_files(generated, self.data)
        for source, relative in files.items():
            common.write_text(os.path.join(self.data, *relative.split("/")), IMPERATIVE_VIOLATING.replace("imperative", source).rstrip("\n").split("\n"))
            code, text = run(["--data-dir", self.data, "--apply", "--tier", "fix", "--enforce"])
            self.assertEqual(1, code, source)
            self.assertIn(source, text.splitlines()[0])
            os.remove(os.path.join(self.data, *relative.split("/")))

    def test_a_fix_tier_violation_is_reported_in_the_column_of_the_source_and_fails_enforce(self):
        files, _, _, _, _ = make_corrections.generate_all(self.data)
        make_corrections.write_files(files, self.data)
        for source in self.SOURCES:
            self.write(source, IMPERATIVE_VIOLATING)
            code, text = run(["--data-dir", self.data, "--apply", "--tier", "fix", "--enforce"])
            listed_code, listed = run(["--data-dir", self.data, "--apply", "--source", source, "--tier", "fix"])
            self.assertEqual(1, code, source)
            self.assertIn("FAILED: 1 convention violations", text)
            self.assertIn(source, text.splitlines()[0])
            self.assertEqual(0, listed_code)
            self.assertIn("out-of", listed)
            os.remove(os.path.join(self.data, source, source + ".tsv"))

    def test_clean_sources_pass_enforce_and_the_staged_corrections_never_address_them(self):
        before, _, _, _, _ = make_corrections.generate_all(self.data)
        for source in self.SOURCES:
            self.write(source, IMPERATIVE_CLEAN)
        after, _, corpora, _, _ = make_corrections.generate_all(self.data)
        files, _, _, _, _ = make_corrections.generate_all(self.data)
        make_corrections.write_files(files, self.data)
        code, text = run(["--data-dir", self.data, "--apply", "--tier", "fix", "--enforce"])
        self.assertEqual(before, after)
        for source in self.SOURCES:
            self.assertNotIn((source, "-"), corpora)
        self.assertEqual(0, code)
        self.assertNotIn("FAILED", text)


if __name__ == "__main__":
    unittest.main()

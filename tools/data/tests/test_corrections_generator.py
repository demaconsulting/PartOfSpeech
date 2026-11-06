"""Tests of the corrections generator (tools/data/make_corrections.py): determinism, --check, fixed point, gates, committed tables."""
import contextlib
import io
import os
import sys
import tempfile
import unittest

sys.path.insert(0, os.path.dirname(os.path.dirname(os.path.abspath(__file__))))
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import common
import convention_candidates
import convention_rules as R
import corrections
import corrections_testdata
import make_corrections


def run(argv):
    """Run the generator's main, returning (exit code, printed text)."""
    buffer = io.StringIO()
    with contextlib.redirect_stdout(buffer):
        code = make_corrections.main(argv)
    return code, buffer.getvalue()


class GeneratorFixtureTests(unittest.TestCase):
    def setUp(self):
        self.temp = tempfile.TemporaryDirectory()
        self.addCleanup(self.temp.cleanup)
        self.data = corrections_testdata.write_data_dir(self.temp.name)

    def test_two_runs_produce_identical_lines(self):
        # Arrange and Act
        first = make_corrections.generate_all(self.data)[0]
        second = make_corrections.generate_all(self.data)[0]
        # Assert
        self.assertEqual(first, second)
        self.assertEqual({"mechanical.tsv", "candidates.tsv", "exceptions.tsv"}, set(first))

    def test_the_mechanical_rows_are_the_fixture_corrections_in_key_order(self):
        # Arrange and Act
        files, result, _, _, _ = make_corrections.generate_all(self.data)
        # Assert
        rows = [(row.source, row.split, row.sent_id, row.token_index, row.token, row.old_tag, row.new_tag, row.provenance)
                for row in result.rows]
        self.assertEqual([("masc", "-", "masc:written/blog/x#1", "1", "such", "ADP", "ADJ", "rule:multiword-prep"),
                          ("open", "dev", "v1", "0", "Most", "PRON", "ADJ", "rule:partitive-adj"),
                          ("open", "train", "t1", "2", "out", "ADV", "ADP", "rule:out-of"),
                          ("open", "train", "t2", "0", "All", "PRON", "DET", "rule:partitive-det"),
                          ("silver", "-", "silver-1", "1", "can", "VERB", "AUX", "rule:modal-aux")], rows)
        self.assertTrue(all(row.old_status == row.new_status for row in result.rows))
        self.assertEqual(corrections.format_table("corrections", [line for line in files["mechanical.tsv"]
                                                                  if line.startswith("# ")], result.rows), files["mechanical.tsv"])

    def test_the_header_names_the_generator_the_columns_and_the_corpus_hashes(self):
        # Arrange and Act
        lines = make_corrections.generate_all(self.data)[0]["mechanical.tsv"]
        # Assert
        self.assertIn("# kind = corrections", lines)
        self.assertIn("# columns = " + ", ".join(corrections.COLUMNS["corrections"]), lines)
        self.assertIn("# generator = tools/data/make_corrections.py (rule set %s)" % R.RULE_SET_VERSION, lines)
        digest = common.sha256_file(os.path.join(self.data, "open", "train.tsv"))
        self.assertIn("# input = open/train.tsv sha256 " + digest, lines)

    def test_exceptions_are_written_header_only(self):
        # Arrange and Act
        lines = make_corrections.generate_all(self.data)[0]["exceptions.tsv"]
        # Assert
        self.assertTrue(all(line.startswith("# ") for line in lines))

    def test_the_mention_cases_become_candidates_with_a_span_and_context(self):
        # Arrange and Act
        _, result, _, _, _ = make_corrections.generate_all(self.data)
        mention = [row for row in result.candidates if row.candidate_rule == "mention-the-word"]
        # Assert
        self.assertEqual(1, len(mention))
        self.assertEqual(("t3", "3", "1", "snowball", "NOUN", "disputed"),
                         (mention[0].sent_id, mention[0].token_index, mention[0].span_length, mention[0].token,
                          mention[0].current_tag, mention[0].current_status))
        self.assertIn("[snowball]", mention[0].context)

    def test_a_token_with_a_correction_row_is_not_a_candidate(self):
        # Arrange and Act
        _, result, _, _, _ = make_corrections.generate_all(self.data)
        corrected = {(row.sent_id, row.token_index) for row in result.rows}
        candidates = {(row.sent_id, row.token_index) for row in result.candidates}
        # Assert
        self.assertEqual(set(), corrected & candidates)

    def test_a_check_violation_becomes_a_candidate_under_its_rule_id_unless_an_exception_covers_it(self):
        # Arrange: a fixture sentence with a check violation
        path = os.path.join(self.data, "open", "dev.tsv")
        lines = common.read_lines(path) + ["", "# sent_id = v2", "# source_id = d3", "# text = I want to go .",
                                           "I\tPRON\tunanimous", "want\tVERB\tunanimous", "to\tADP\tunanimous",
                                           "go\tVERB\tunanimous", ".\tPUNCT\tunanimous"]
        common.write_text(path, lines)
        # Act
        _, result, _, _, _ = make_corrections.generate_all(self.data)
        found = [row for row in result.candidates if row.candidate_rule == "infinitive-to"]
        exceptions = corrections.ROW_TYPES["exceptions"]("open", "dev", "d3", "v2", "2", "to", "infinitive-to", "kept", "llm:m:r")
        corrections.write_table(os.path.join(corrections.corrections_dir(self.data), "exceptions.tsv"), "exceptions",
                                ["# columns = " + ", ".join(corrections.COLUMNS["exceptions"])], [exceptions])
        _, covered, _, _, _ = make_corrections.generate_all(self.data)
        # Assert
        self.assertEqual([("v2", "2", "check-rule")], [(row.sent_id, row.token_index, row.hint) for row in found])
        self.assertEqual([], [row for row in covered.candidates if row.candidate_rule == "infinitive-to"])

    def test_the_generated_tables_validate_against_the_fixture_corpora(self):
        # Arrange
        files = make_corrections.generate_all(self.data)[0]
        make_corrections.write_files(files, self.data)
        # Act
        errors, state, counts = corrections.validate_overlay(self.data)
        # Assert
        self.assertEqual(([], "pending"), (errors, state))
        self.assertEqual(5, counts["mechanical.tsv"])

    def test_check_detects_a_changed_byte_and_a_missing_file(self):
        # Arrange
        files = make_corrections.generate_all(self.data)[0]
        make_corrections.write_files(files, self.data)
        path = os.path.join(corrections.corrections_dir(self.data), "mechanical.tsv")
        # Act
        clean = make_corrections.check_files(files, self.data)
        lines = common.read_lines(path)
        common.write_text(path, lines[:-1] + [lines[-1][:-1] + "!"])
        changed = make_corrections.check_files(files, self.data)
        os.remove(os.path.join(corrections.corrections_dir(self.data), "candidates.tsv"))
        missing = make_corrections.check_files(files, self.data)
        # Assert
        self.assertEqual([], clean)
        self.assertEqual(["mechanical.tsv"], changed)
        self.assertEqual(["mechanical.tsv", "candidates.tsv"], missing)

    def test_the_command_line_writes_checks_and_samples(self):
        # Arrange and Act
        write_code, _ = run(["--data-dir", self.data])
        check_code, check_text = run(["--data-dir", self.data, "--check"])
        sample_code, sample_text = run(["--data-dir", self.data, "sample", "--n", "2", "--seed", "1", "--per-rule", "1"])
        # Assert
        self.assertEqual((0, 0, 0), (write_code, check_code, sample_code))
        self.assertIn("up to date", check_text)
        self.assertIn("[[", sample_text)

    def test_check_fails_after_a_table_was_edited(self):
        # Arrange
        run(["--data-dir", self.data])
        path = os.path.join(corrections.corrections_dir(self.data), "mechanical.tsv")
        common.write_text(path, common.read_lines(path)[:-1])
        # Act
        code, text = run(["--data-dir", self.data, "--check"])
        # Assert
        self.assertEqual(1, code)
        self.assertIn("FAILED", text)

    def apply_corrections(self):
        """Write the generated tables, then apply the effective rows to the fixture corpora (the applied state)."""
        run(["--data-dir", self.data])
        corrections.apply_to_files(corrections.load_overlay(self.data)["corrections"], self.data, write=True)

    def test_check_passes_in_the_applied_state_by_reversing_a_temporary_copy(self):
        # Arrange
        self.apply_corrections()
        train = os.path.join(self.data, "open", "train.tsv")
        before = common.read_lines(train)
        # Act
        code, text = run(["--data-dir", self.data, "--check"])
        # Assert
        self.assertEqual(0, code)
        self.assertIn("up to date", text)
        self.assertIn("corrections state: applied", text)
        self.assertEqual(before, common.read_lines(train))
        self.assertEqual("applied", corrections.validate_overlay(self.data)[1])

    def test_check_fails_in_the_applied_state_after_a_table_was_edited(self):
        # Arrange
        self.apply_corrections()
        path = os.path.join(corrections.corrections_dir(self.data), "candidates.tsv")
        common.write_text(path, common.read_lines(path)[:-1])
        # Act
        code, text = run(["--data-dir", self.data, "--check"])
        # Assert
        self.assertEqual(1, code)
        self.assertIn("FAILED", text)
        self.assertIn("candidates.tsv", text)

    def test_the_generator_refuses_to_rewrite_the_tables_in_the_applied_state(self):
        # Arrange
        self.apply_corrections()
        path = os.path.join(corrections.corrections_dir(self.data), "mechanical.tsv")
        before = common.read_lines(path)
        # Act
        code, text = run(["--data-dir", self.data])
        # Assert
        self.assertEqual(1, code)
        self.assertIn("refusing to write", text)
        self.assertEqual(before, common.read_lines(path))

    def test_the_generator_refuses_a_partly_applied_corpus(self):
        # Arrange: apply every correction, then put one corrected row of the training corpus back (an interrupted apply)
        run(["--data-dir", self.data])
        train = os.path.join(self.data, "open", "train.tsv")
        before = common.read_lines(train)
        self.apply_corrections()
        after = common.read_lines(train)
        changed = [i for i, (old, new) in enumerate(zip(before, after)) if old != new]
        self.assertTrue(changed)
        mixed = list(after)
        mixed[changed[0]] = before[changed[0]]
        common.write_text(train, mixed)
        tables = {name: common.read_lines(os.path.join(corrections.corrections_dir(self.data), name))
                  for name in ("mechanical.tsv", "candidates.tsv")}
        # Act
        code, text = run(["--data-dir", self.data])
        code_check, text_check = run(["--data-dir", self.data, "--check"])
        # Assert
        self.assertEqual((1, 1), (code, code_check), text + text_check)
        self.assertIn("partly corrected", text)
        self.assertIn("partly corrected", text_check)
        for name, lines in tables.items():
            self.assertEqual(lines, common.read_lines(os.path.join(corrections.corrections_dir(self.data), name)))

    def test_check_still_compares_a_stale_table_in_the_pending_state(self):
        # Arrange: the tables were generated, then a corpus label changed (the situation a regeneration fixes)
        run(["--data-dir", self.data])
        train = os.path.join(self.data, "open", "train.tsv")
        common.write_text(train, [line.replace("order\tNOUN", "order\tVERB") for line in common.read_lines(train)])
        # Act
        code, text = run(["--data-dir", self.data, "--check"])
        # Assert
        self.assertEqual(1, code)
        self.assertIn("differs from the committed file(s)", text)

    def test_the_fixture_reaches_the_fixed_point(self):
        # Arrange and Act
        applied, leftover, conflicts = make_corrections.fixed_point(self.data)
        # Assert
        self.assertEqual(5, applied)
        self.assertEqual(([], []), (leftover, conflicts))

    def test_an_oversized_candidates_table_is_refused_with_the_largest_class(self):
        # Arrange
        original = make_corrections.MAX_CANDIDATES_BYTES
        make_corrections.MAX_CANDIDATES_BYTES = 100
        self.addCleanup(setattr, make_corrections, "MAX_CANDIDATES_BYTES", original)
        # Act and Assert
        with self.assertRaises(RuntimeError) as error:
            make_corrections.generate_all(self.data)
        self.assertIn("limit 100", str(error.exception))

    def test_a_rule_conflict_stops_the_generator(self):
        # Arrange: two fix rules that disagree about the same token
        def to_adverb(s, res):
            return [(i, "ADV") for i in s.where("order")]

        def to_adjective(s, res):
            return [(i, "ADJ") for i in s.where("order")]

        for name, detect in (("test-conflict-a", to_adverb), ("test-conflict-b", to_adjective)):
            rule = R.Rule(name, R.FIX, "Test", "disagrees on `order`", None, detect)
            R.RULES.append(rule)
            self.addCleanup(R.RULES.remove, rule)
        # Act and Assert
        with self.assertRaises(RuntimeError) as error:
            make_corrections.generate_all(self.data)
        self.assertIn("conflict", str(error.exception))

    def test_draw_sample_is_seeded_and_takes_all_rows_of_small_rules(self):
        # Arrange
        rows = make_corrections.generate_all(self.data)[1].rows
        # Act
        first = make_corrections.draw_sample(rows, 3, 7, per_rule=1)
        second = make_corrections.draw_sample(rows, 3, 7, per_rule=1)
        other = make_corrections.draw_sample(rows, 3, 8)
        restricted = make_corrections.draw_sample(rows, 0, 7, per_rule=5, only={"out-of"})
        # Assert
        self.assertEqual(first, second)
        self.assertEqual(3 + 5, len(first))
        self.assertEqual(3, len(other))
        self.assertEqual(["rule:out-of"], [row.provenance for _, row in restricted])

    def test_the_lexicon_check_counts_new_tags_the_lexicon_does_not_allow(self):
        # Arrange
        rows = make_corrections.generate_all(self.data)[1].rows
        # Act
        rejected = make_corrections.lexicon_rejects(rows, self.data)
        # Assert: the fixture lexicon lists `out` as ADP or ADV only and knows no other word
        self.assertEqual([], [row for row in rejected if row.token == "out"])

    def test_the_catalog_lists_every_rule_and_candidate(self):
        # Arrange and Act
        text = "\n".join(make_corrections.catalog_markdown())
        # Assert
        for rule_id in R.rule_ids():
            self.assertIn("`%s`" % rule_id, text)
        for candidate_id in convention_candidates.candidate_ids():
            self.assertIn("`%s`" % candidate_id, text)


class CommittedDataTests(unittest.TestCase):
    """The committed tables against the committed corpora (the generator's contract with the real data)."""

    @classmethod
    def setUpClass(cls):
        cls.files, cls.result, cls.corpora, cls.sentences, cls.resources = make_corrections.generate_all()

    def test_the_committed_tables_validate_against_the_committed_corpora(self):
        # Arrange and Act
        errors, state, counts = corrections.validate_overlay()
        # Assert
        self.assertEqual([], errors[:5])
        self.assertIn(state, ("pending", "applied"))
        self.assertGreater(counts["mechanical.tsv"], 0)

    def test_no_token_of_the_committed_corpora_is_proposed_two_different_tags(self):
        self.assertEqual([], self.result.conflicts[:5])

    def test_the_committed_tables_are_the_generator_output_in_the_pending_and_in_the_applied_state(self):
        # Arrange and Act: `--check` regenerates from the corpora as they are (pending) or reversed in a temporary copy (applied)
        code, text = run(["--check"])
        # Assert
        self.assertEqual((0, True), (code, "up to date" in text), text[-400:])

    def test_the_committed_corpora_have_no_fix_tier_violation_in_either_state(self):
        # Arrange
        import check_conventions
        results = []
        # Act: without and with the in-memory application of the staged corrections (the same in the applied state)
        for extra in ([], ["--apply"]):
            buffer = io.StringIO()
            with contextlib.redirect_stdout(buffer):
                results.append((check_conventions.main(["--tier", "fix", "--enforce"] + extra), buffer.getvalue()))
        # Assert
        self.assertEqual(0, results[1][0], results[1][1][-300:])
        if corrections.validate_overlay()[1] == "applied":
            self.assertEqual(0, results[0][0], results[0][1][-300:])

    def test_applying_the_rows_in_memory_leaves_nothing_for_the_rules_to_fix(self):
        # Arrange
        corpora = corrections.load_corpora()
        # Act
        applied = corrections.apply_in_memory(corpora, self.result.rows)
        fresh = [R.Sentence(source, split, sentence) for (source, split), corpus in corpora.items() for sentence in corpus.sentences]
        second = make_corrections.build(fresh, R.build_resources(fresh))
        # Assert
        self.assertEqual(len(self.result.rows), applied)
        self.assertEqual([], second.rows[:5])
        self.assertEqual([], second.conflicts[:5])

    def test_the_candidates_table_stays_below_the_size_gate_and_addresses_existing_tokens(self):
        # Arrange
        size = sum(len(line.encode("utf-8")) + 1 for line in self.files["candidates.tsv"])
        # Act
        errors = corrections.validate_candidates_against_corpora(self.result.candidates, self.corpora)
        # Assert
        self.assertLess(size, make_corrections.MAX_CANDIDATES_BYTES)
        self.assertEqual([], errors[:5])


if __name__ == "__main__":
    unittest.main()

"""Tests of term_divergence.py: the frozen detector of conflicts with the convention "Capitalized Defined Terms" and its two counters, on SYNTHETIC fixtures only.

No committed corpus, no held-out set and no UD cache is read: every folder here is built in a temporary directory.

Run: python -m unittest discover -s tools/data/tests -p "test_term_divergence.py"   (developer-run; no model, no network)
"""
import ast
import contextlib
import io
import os
import re
import shutil
import sys
import tempfile
import unittest

sys.path.insert(0, os.path.dirname(os.path.dirname(os.path.abspath(__file__))))
import common
import term_divergence as td

WORDS = {"quibble": "NOUN VERB", "override": "NOUN VERB", "mode": "NOUN", "wiring": "NOUN ADJ VERB", "schematic": "ADJ NOUN", "defense": "NOUN VERB",
         "logistics": "NOUN", "agency": "NOUN", "normal": "ADJ NOUN", "operating": "ADJ NOUN VERB", "range": "NOUN VERB", "general": "ADJ NOUN",
         "standby": "ADJ NOUN", "position": "NOUN VERB", "the": "DET", "select": "VERB ADJ", "see": "VERB", "zorbix": "PROPN", "panel": "NOUN"}
LEX = {word: frozenset(tags.split()) for word, tags in WORDS.items()}

# (text, tags) of the fixture sentences; every word of the sentences that is not in the lexicon above is a plain token
CONFLICT = ("Select the Quibble Override mode .", "VERB DET PROPN PROPN NOUN PUNCT")          # class A (T1), last word PROPN
AGREEMENT = ("See the Wiring Schematic .", "VERB DET NOUN NOUN PUNCT")                         # class A (T2), last word NOUN
ADJECTIVE = ("See the Wiring Schematic .", "VERB DET ADJ ADJ PUNCT")                           # class A, last word ADJ
ORGANIZATION = ("The shop called the Defense Logistics Agency today .", "DET NOUN VERB DET PROPN PROPN PROPN NOUN PUNCT")   # org-like
BROAD = ("The pilot reviewed the Normal Operating Range yesterday .", "DET NOUN VERB DET ADJ NOUN NOUN NOUN PUNCT")             # class B
INITIAL = ("Quibble Override mode is active .", "PROPN PROPN NOUN AUX ADJ PUNCT")             # sentence initial: never a signal
STYLED = ("Select The Quibble Override mode .", "VERB DET PROPN PROPN NOUN PUNCT")            # Title Case styled sentence
FIXTURE_WORDS = ("quibble", "override", "wiring", "schematic", "defense", "logistics", "agency", "normal", "operating", "select", "pilot", "reviewed")


def pairs(item):
    """[(form, upos)] of a fixture item (text, tags)."""
    forms, tags = item[0].split(), item[1].split()
    assert len(forms) == len(tags), item
    return list(zip(forms, tags))


def corpus_lines(items, prefix):
    """The lines of a corpus file of fixture items: a header block, then one block per sentence."""
    lines = ["# corpus = synthetic fixture", ""]
    for number, item in enumerate(items, 1):
        lines += ["# sent_id = %s-%d" % (prefix, number), "# source_id = %s-%d" % (prefix, number), "# text = " + item[0].replace(" ", "")]
        lines += ["%s\t%s\tunanimous" % (form, tag) for form, tag in pairs(item)]
        lines.append("")
    return lines[:-1]


def conllu_lines(items, prefix):
    """The lines of a CoNLL-U file of fixture items (ten columns, a blank line after each sentence)."""
    lines = []
    for number, item in enumerate(items, 1):
        lines += ["# sent_id = %s-%d" % (prefix, number), "# text = " + item[0]]
        lines += ["%d\t%s\t%s\t%s\t_\t_\t0\troot\t_\t_" % (i, form, form.lower(), tag) for i, (form, tag) in enumerate(pairs(item), 1)]
        lines.append("")
    return lines


class DetectorTests(unittest.TestCase):
    """The frozen detector: classes, verdicts and exclusions."""

    def found(self, item):
        return td.defined_term_conflicts(pairs(item), LEX)

    def test_a_proper_noun_tag_on_a_cued_run_is_a_class_a_conflict(self):
        # Arrange and act
        found = self.found(CONFLICT)
        # Assert
        self.assertEqual([("a", 2, 4, 3, "PROPN", "conflict")], [tuple(c) for c in found])

    def test_a_noun_or_adjective_tag_is_an_agreement(self):
        # Arrange and act and assert
        self.assertEqual(["agreement"], [c.verdict for c in self.found(AGREEMENT)])
        self.assertEqual(["agreement"], [c.verdict for c in self.found(ADJECTIVE)])

    def test_any_other_tag_is_other(self):
        # Arrange and act and assert
        self.assertEqual(["other", "other", "other", "other"], [td.verdict_of(tag) for tag in ("VERB", "X", "_", "SYM")])
        self.assertEqual(["conflict", "agreement", "agreement"], [td.verdict_of(tag) for tag in ("PROPN", "NOUN", "ADJ")])

    def test_an_uncued_run_of_two_words_is_class_b(self):
        # Arrange and act
        found = self.found(BROAD)
        # Assert
        self.assertEqual([("b", "agreement")], [(c.cls, c.verdict) for c in found])

    def test_an_organization_like_run_is_reported_apart_from_the_conflicts(self):
        # Arrange and act
        found = self.found(ORGANIZATION)
        # Assert
        self.assertEqual([("org", "conflict")], [(c.cls, c.verdict) for c in found])

    def test_a_single_word_run_without_a_cue_is_not_counted(self):
        # Arrange
        item = ("The shop kept the Standby .", "DET NOUN VERB DET PROPN PUNCT")
        # Act and assert
        self.assertEqual([], self.found(item))

    def test_a_single_word_run_with_a_right_cue_is_class_a(self):
        # Arrange
        item = ("The shop kept the Standby position .", "DET NOUN VERB DET PROPN NOUN PUNCT")
        # Act and assert
        self.assertEqual([("a", "conflict")], [(c.cls, c.verdict) for c in self.found(item)])

    def test_the_first_token_a_title_case_styled_sentence_and_a_name_are_never_counted(self):
        # Arrange: a coined label (not ordinary) and a title word
        coined = ("Select the Zorbix mode .", "VERB DET PROPN NOUN PUNCT")
        titled = ("Select the General mode .", "VERB DET PROPN NOUN PUNCT")
        # Act and assert
        for item in (INITIAL, STYLED, coined, titled):
            self.assertEqual([], self.found(item), item[0])

    def test_the_conflicts_and_agreements_are_counted_by_class_and_tag(self):
        # Arrange
        counts = td.empty_counts()
        # Act
        for item in (CONFLICT, AGREEMENT, ADJECTIVE, ORGANIZATION, BROAD, INITIAL, STYLED):
            td.add_sentence(counts, self.found(item))
        # Assert
        self.assertEqual(7, counts["sentences"])
        self.assertEqual((3, 1, 1, 1, 0), (counts["a_runs"], counts["a_propn"], counts["a_noun"], counts["a_adj"], counts["a_other"]))
        self.assertEqual((1, 1), (counts["b_runs"], counts["b_noun"]))
        self.assertEqual((1, 1), (counts["org_runs"], counts["org_propn"]))

    def test_the_default_lexicon_is_the_repository_lexicon_of_the_data_dir(self):
        # Arrange
        folder = tempfile.mkdtemp()
        self.addCleanup(shutil.rmtree, folder, True)
        common.write_text(os.path.join(folder, "lexicon", "lexicon.tsv"), ["# fixture", "quibble\tNOUN", "override\tNOUN"])
        # Act
        lex = td.default_lexicon(folder)
        # Assert
        self.assertEqual({"quibble": frozenset(["NOUN"]), "override": frozenset(["NOUN"])}, lex)


class CounterTests(unittest.TestCase):
    """The counters on a synthetic data folder and a synthetic UD cache."""

    def setUp(self):
        self.folder = tempfile.mkdtemp()
        self.addCleanup(shutil.rmtree, self.folder, True)
        self.data = os.path.join(self.folder, "data")
        self.cache = os.path.join(self.folder, "cache")
        common.write_text(os.path.join(self.data, "lexicon", "lexicon.tsv"), ["# fixture"] + ["%s\t%s" % (w, t) for w, t in sorted(WORDS.items())])
        common.write_text(os.path.join(self.data, "open", "dev.tsv"), corpus_lines([CONFLICT, AGREEMENT], "dev"))
        common.write_text(os.path.join(self.data, "open", "test.tsv"), corpus_lines([ORGANIZATION, BROAD, INITIAL, STYLED], "test"))
        common.write_text(os.path.join(self.cache, "ud-ewt", "en_ewt-ud-dev.conllu"), conllu_lines([CONFLICT, CONFLICT, ADJECTIVE], "ud"))
        common.write_text(os.path.join(self.data, "open", "train.tsv"), corpus_lines([CONFLICT] * 40 + [BROAD], "train"))
        common.write_text(os.path.join(self.data, "compound", "compound.tsv"), corpus_lines([AGREEMENT], "cmp"))

    def run_tool(self, *args):
        out, err = io.StringIO(), io.StringIO()
        with contextlib.redirect_stdout(out), contextlib.redirect_stderr(err):
            code = td.main(["--data-dir", self.data] + list(args))
        return code, out.getvalue(), err.getvalue()

    def parse(self, text):
        """{label: {key: int}} of the output lines; fails when a line holds anything else than a fixed label and `key=int` fields."""
        result = {}
        for line in text.splitlines():
            label, *fields = line.split(" ")
            result[label] = {}
            for field in fields:
                self.assertRegex(field, r"^[a-z_]+=[0-9]+$", line)
                key, value = field.split("=")
                result[label][key] = int(value)
        return result

    def test_count_held_out_prints_integers_only(self):
        # Arrange and act
        code, out, err = self.run_tool("count-held-out", "--cache-dir", self.cache)
        # Assert
        self.assertEqual((0, ""), (code, err))
        for line in out.splitlines():
            self.assertRegex(line, r"^[a-z-]+( [a-z_]+=[0-9]+)+$", line)
        for word in FIXTURE_WORDS + ("dev-1", "test-1", "ud-1", "extra"):
            self.assertNotIn(word, out.lower(), word)

    def test_count_held_out_counts_each_set_and_marks_an_absent_file(self):
        # Arrange and act
        _, out, _ = self.run_tool("count-held-out", "--cache-dir", self.cache)
        counts = self.parse(out)
        # Assert
        self.assertEqual(["open-dev", "open-test", "ud-ewt-dev", "ud-ewt-test"], list(counts))
        self.assertEqual((1, 2, 2, 1, 1), tuple(counts["open-dev"][k] for k in ("present", "sentences", "a_runs", "a_propn", "a_noun")))
        self.assertEqual((4, 1, 1, 1, 1), (counts["open-test"]["sentences"], counts["open-test"]["org_runs"], counts["open-test"]["org_propn"],
                                           counts["open-test"]["b_runs"], counts["open-test"]["b_noun"]))
        self.assertEqual((1, 3, 3, 2, 1), (counts["ud-ewt-dev"]["present"], counts["ud-ewt-dev"]["sentences"], counts["ud-ewt-dev"]["a_runs"],
                                           counts["ud-ewt-dev"]["a_propn"], counts["ud-ewt-dev"]["a_adj"]))
        self.assertEqual((0, 0), (counts["ud-ewt-test"]["present"], counts["ud-ewt-test"]["sentences"]))

    def test_count_held_out_has_no_list_or_output_option(self):
        # Arrange and act and assert
        for option in ("--list", "--show", "--out", "--ids", "--verbose"):
            with contextlib.redirect_stderr(io.StringIO()), self.assertRaises(SystemExit):
                td.main(["--data-dir", self.data, "count-held-out", option])

    def test_count_held_out_writes_no_file(self):
        # Arrange
        before = sorted(os.path.join(root, name) for root, _, names in os.walk(self.folder) for name in names)
        # Act
        self.run_tool("count-held-out", "--cache-dir", self.cache)
        after = sorted(os.path.join(root, name) for root, _, names in os.walk(self.folder) for name in names)
        # Assert
        self.assertEqual(before, after)

    def test_count_training_counts_each_present_source_and_writes_ids_only(self):
        # Arrange
        ids = os.path.join(self.folder, "ids.tsv")
        # Act
        code, out, err = self.run_tool("count-training", "--out", ids)
        counts = self.parse(out)
        # Assert
        self.assertEqual((0, ""), (code, err))
        self.assertEqual(["open", "compound", "ids"], list(counts))
        self.assertEqual((41, 40, 40, 1), (counts["open"]["sentences"], counts["open"]["a_runs"], counts["open"]["a_propn"], counts["open"]["b_noun"]))
        self.assertEqual({"written": 25}, counts["ids"])

    def test_the_conflict_id_sample_is_seeded_capped_and_free_of_text(self):
        # Arrange
        first, second = os.path.join(self.folder, "one.tsv"), os.path.join(self.folder, "two.tsv")
        # Act
        self.run_tool("count-training", "--out", first)
        self.run_tool("count-training", "--out", second)
        lines = common.read_lines(first)
        rows = [line.split("\t") for line in lines if not line.startswith("# ")]
        # Assert
        self.assertEqual(lines, common.read_lines(second))
        self.assertEqual(25, len(rows))
        for source, cls, sent_id, index in rows:
            self.assertEqual(("open", "a", "3"), (source, cls, index))
            self.assertRegex(sent_id, r"^train-[0-9]+$")
        self.assertEqual(25, len({row[2] for row in rows}))
        for word in FIXTURE_WORDS:
            self.assertNotIn(word, "\n".join(lines).lower(), word)

    def test_a_sample_smaller_than_the_cap_holds_every_conflict_once(self):
        # Arrange
        conflicts = [("a", "s%d" % i, 3) for i in range(5)] + [("b", "t1", 2)]
        # Act
        rows = td.id_sample("open", conflicts, size=25)
        # Assert
        self.assertEqual(6, len(rows))
        self.assertEqual({"a": 5, "b": 1}, {cls: sum(1 for r in rows if r[1] == cls) for cls in ("a", "b")})


class SourceScanTests(unittest.TestCase):
    """The held-out guard: integers only, one function opens the held-out files, no list option."""

    def parsed(self):
        with open(td.__file__, encoding="utf-8") as handle:
            return ast.parse(handle.read())

    def test_every_print_prints_a_formatted_count_line(self):
        # Arrange
        prints = [n for n in ast.walk(self.parsed()) if isinstance(n, ast.Call) and isinstance(n.func, ast.Name) and n.func.id == "print"]
        # Act and assert
        self.assertGreaterEqual(len(prints), 1)
        for call in prints:
            self.assertEqual(1, len(call.args))
            self.assertIsInstance(call.args[0], ast.Call)
            self.assertEqual("format_counts", call.args[0].func.id)

    def test_format_counts_refuses_a_value_that_is_no_integer(self):
        # Arrange and act and assert
        self.assertEqual("label a=1 b=2", td.format_counts("label", {"a": 1, "b": 2}))
        for value in ("word", 1.5, None, True, ("a",)):
            with self.assertRaises(TypeError):
                td.format_counts("label", {"a": value})

    def test_only_the_held_out_function_names_a_held_out_set(self):
        # Arrange
        tree = self.parsed()
        users = set()
        for function in (n for n in ast.walk(tree) if isinstance(n, ast.FunctionDef)):
            names = {n.id for n in ast.walk(function) if isinstance(n, ast.Name)}
            if names & {"HELD_OUT_CORPORA", "HELD_OUT_UD"}:
                users.add(function.name)
        # Act and assert
        self.assertEqual({"held_out_counts"}, users)

    def test_the_module_opens_no_file_itself_and_names_no_listing_option(self):
        # Arrange
        tree = self.parsed()
        calls = [n.func.id for n in ast.walk(tree) if isinstance(n, ast.Call) and isinstance(n.func, ast.Name)]
        strings = [n.value for n in ast.walk(tree) if isinstance(n, ast.Constant) and isinstance(n.value, str)]
        options = [s for s in strings if s.startswith("--")]
        # Act and assert
        self.assertNotIn("open", calls)
        self.assertEqual(["--cache-dir", "--data-dir", "--out"], sorted(options))

    def test_the_commands_are_the_two_counters(self):
        # Arrange
        strings = {n.value for n in ast.walk(self.parsed()) if isinstance(n, ast.Constant) and isinstance(n.value, str)}
        # Act and assert
        self.assertTrue({"count-training", "count-held-out"} <= strings)
        self.assertEqual(set(), {s for s in strings if re.fullmatch(r"(?:list|show|print|dump)[a-z-]*", s)})

    def test_only_the_data_tools_are_imported_and_no_name_scores_or_predicts(self):
        # Arrange
        tree = self.parsed()
        imported = set()
        for node in ast.walk(tree):
            if isinstance(node, ast.Import):
                imported |= {a.name.split(".")[0] for a in node.names}
            elif isinstance(node, ast.ImportFrom):
                imported.add((node.module or "").split(".")[0])
        names = {n.id for n in ast.walk(tree) if isinstance(n, ast.Name)} | {n.attr for n in ast.walk(tree) if isinstance(n, ast.Attribute)}
        names |= {n.name for n in ast.walk(tree) if isinstance(n, ast.FunctionDef)}
        # Act and assert
        self.assertEqual(set(), imported - {"argparse", "collections", "os", "sys", "common", "fetch_ud_ewt", "shapes_dev", "technical_frames"})
        for forbidden in ("tagger", "predict", "decode", "score", "weights", "classifier"):
            self.assertEqual([], [n for n in names if forbidden in n.lower()], forbidden)

    def test_the_docstring_states_the_detector_and_the_counters(self):
        # Arrange
        doc = " ".join(td.__doc__.split())
        # Act and assert
        for part in ("THE DETECTOR", "THE COUNTERS", "INTEGERS ONLY", "ONLY code path", "class A", "class B", "org-like", "ids only"):
            self.assertIn(part, doc, part)


if __name__ == "__main__":
    unittest.main()

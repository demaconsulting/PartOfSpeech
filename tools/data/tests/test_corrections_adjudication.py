"""Tests of the adjudication tooling (tools/data/adjudicate.py) and of the supersession of mechanical rows by adjudicated rows.

Everything runs on the miniature data folder of corrections_testdata.py (never the committed data) except the round trip of
every committed token through the cell escape. The models are never called: the tests write the tagger outputs themselves.
"""
import collections
import contextlib
import io
import os
import sys
import tempfile
import unittest

sys.path.insert(0, os.path.dirname(os.path.dirname(os.path.abspath(__file__))))
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import adjudicate
import check_conventions
import common
import convention_rules
import corrections
import corrections_testdata
import make_corrections

HEADER = ["# corpus = test", "# columns = " + ", ".join(corrections.COLUMNS["corrections"])]
OUT = corrections.Correction("open", "train", "d1", "t1", "2", "out", "ADV", "ADP", "unanimous", "unanimous", "rule:out-of")
SUCH = corrections.Correction("masc", "-", "masc:written/blog/x", "masc:written/blog/x#1", "1", "such", "ADP", "ADJ", "ptb", "ptb",
                              "rule:multiword-prep")
MASC1, MASC2 = "masc:written/blog/x#1", "masc:written/blog/x#2"


def run(function, argv):
    """Run a command-line main, returning (exit code, printed text)."""
    buffer = io.StringIO()
    with contextlib.redirect_stdout(buffer):
        code = function(argv)
    return code, buffer.getvalue()


def cand(source, split, source_id, sent_id, index, token, tag, status, rule, span=1):
    """A candidates row."""
    return corrections.ROW_TYPES["candidates"](source, split, source_id, sent_id, str(index), str(span), token, tag, status, "", rule,
                                               "hint", "context")


def flag(token, label, voters, kind):
    return adjudicate.FlagRow(token, label, tuple(voters), kind)


def vote(tag, flags=(), confidence="high"):
    return adjudicate.Vote(tag, confidence, tuple(flags))


class Fixture(unittest.TestCase):
    """A fixture data folder with the generated mechanical table and a work folder."""

    def setUp(self):
        self.temp = tempfile.TemporaryDirectory()
        self.addCleanup(self.temp.cleanup)
        self.data = corrections_testdata.write_data_dir(self.temp.name)
        make_corrections.write_files(make_corrections.generate_all(self.data)[0], self.data)
        self.folder = corrections.corrections_dir(self.data)
        self.work = adjudicate.Work(os.path.join(self.temp.name, "work"))

    def inputs(self, candidates=()):
        mechanical = corrections.read_table(os.path.join(self.folder, "mechanical.tsv"), "corrections")[1]
        return adjudicate.Inputs(corrections.load_corpora(self.data), list(candidates), mechanical)

    def write_candidates(self, rows):
        corrections.write_table(os.path.join(self.folder, "candidates.tsv"), "candidates",
                                ["# columns = " + ", ".join(corrections.COLUMNS["candidates"])], rows)


# ---------------------------------------------------------------- cells

class EscapeTests(unittest.TestCase):
    ADVERSARIAL = ['"', '""', '"""', "\\", "\\\\", "\\n", "\\u0041", "a\tb", "x\ny", "x\r\ny", "\u2028", "\u2029", "\x00", "\x1f", "\x7f",
                   "\x85", "a\"b", "'s", "a,b", "n't", "\u00e9", "\U0001F600", "#", "\\\"", "tab\t", "\t"]

    def test_escape_then_unescape_is_the_identity_and_leaves_no_separator(self):
        # Arrange: tokens that break a naive TSV or CSV writer
        for token in self.ADVERSARIAL:
            with self.subTest(token=token):
                # Act
                escaped = adjudicate.escape_cell(token)
                # Assert
                self.assertNotRegex(escaped, "[\t\n\r\x00-\x1f\x7f-\x9f\u2028\u2029]")
                self.assertEqual(token, adjudicate.unescape_cell(escaped))

    def test_known_escapes(self):
        # Arrange, Act and Assert
        self.assertEqual('"', adjudicate.escape_cell('"'))
        self.assertEqual("\\\\", adjudicate.escape_cell("\\"))
        self.assertEqual("a\\tb", adjudicate.escape_cell("a\tb"))
        self.assertEqual("\\u2028", adjudicate.escape_cell("\u2028"))
        self.assertEqual("\\u0085", adjudicate.escape_cell("\x85"))
        self.assertEqual("snowball", adjudicate.escape_cell("snowball"))

    def test_unescape_rejects_a_backslash_that_starts_no_escape(self):
        # Arrange
        for bad in ("\\", "a\\x", "\\u12", "\\u00ZZ", "\\u00E9", "\\U0041", "tab\\"):
            with self.subTest(text=bad):
                # Act and Assert
                with self.assertRaises(ValueError):
                    adjudicate.unescape_cell(bad)

    def test_every_token_of_the_committed_corpora_round_trips(self):
        # Arrange
        tokens = set(self.ADVERSARIAL)
        for corpus in corrections.load_corpora().values():
            for sentence in corpus.sentences:
                tokens.update(cells[0] for cells in sentence["rows"])
        # Act
        broken = [token for token in tokens if adjudicate.unescape_cell(adjudicate.escape_cell(token)) != token
                  or "\t" in adjudicate.escape_cell(token)]
        # Assert
        self.assertGreater(len(tokens), 10000)
        self.assertEqual([], broken)

    def test_the_csv_dialect_keeps_a_quote_token_in_exactly_one_unquoted_cell(self):
        # Arrange
        temp = tempfile.TemporaryDirectory()
        self.addCleanup(temp.cleanup)
        path = os.path.join(temp.name, "b001.tsv")
        rows = [("s000001", str(i), adjudicate.escape_cell(token)) for i, token in enumerate(self.ADVERSARIAL)]
        # Act
        adjudicate.write_batch(path, rows)
        with open(path, "r", encoding="utf-8", newline="") as handle:
            lines = handle.read().split("\n")
        back = adjudicate.read_batch(path)
        # Assert
        self.assertEqual("s000001\t0\t\"", lines[1])
        self.assertEqual("s000001\t1\t\"\"", lines[2])
        self.assertTrue(all(line.count("\t") == 2 for line in lines[:-1]))
        self.assertEqual([tuple(row) for row in rows], back)
        self.assertEqual(self.ADVERSARIAL, [adjudicate.unescape_cell(row[2]) for row in back])


# ---------------------------------------------------------------- selection

class SelectionTests(Fixture):
    def setUp(self):
        super().setUp()
        self.candidates = [cand("open", "train", "d2", "t3", 3, "snowball", "NOUN", "disputed", "mention-the-word"),
                           cand("masc", "-", "masc:written/blog/x", MASC2, 2, "fine", "ADJ", "ptb", "prenominal-ed")]
        self.flags = {
            ("dev", "v1"): {3: flag("came", "VERB", ["ADJ"] * 4, "unanimous-against")},
            ("train", "t2"): {2: flag("them", "PRON", ["NOUN", "NOUN", "PRON", "PRON"], "split"),
                              0: flag("All", "PRON", ["DET"] * 4, "unanimous-against")},      # a token with a mechanical row
            ("train", "t1"): {2: flag("out", "ADV", ["ADP"] * 4, "unanimous-against")},        # only a mechanical token
            ("train", "t3"): {0: flag("The", "DET", ["DET", "DET", "PRON", "PRON"], "split")}}  # no content tag: not a split flag

    def test_bands_follow_the_best_reason_and_a_mechanical_token_pulls_nothing_in(self):
        # Arrange
        inputs = self.inputs(self.candidates)
        # Act
        pool = adjudicate.build_pool(inputs, self.flags)
        bands = {key[2]: entry.band for key, entry in pool.items()}
        # Assert
        self.assertEqual({MASC2: "P1", "t3": "P3", "v1": "P2", "t2": "P4"}, bands)
        self.assertNotIn(("open", "train", "t1"), pool)
        self.assertEqual((0, 1), (pool[("open", "train", "t2")].unanimous, pool[("open", "train", "t2")].split))

    def test_a_split_flag_needs_a_content_tag_among_the_label_and_the_voters(self):
        # Arrange
        plain = flag("of", "ADP", ["ADP", "ADP", "ADV", "ADV"], "split")
        content = flag("of", "ADP", ["ADP", "ADP", "VERB", "VERB"], "split-major")
        # Act and Assert
        self.assertIsNone(adjudicate.flag_kind(plain))
        self.assertEqual("split", adjudicate.flag_kind(content))
        self.assertEqual("unanimous", adjudicate.flag_kind(flag("of", "ADP", ["ADV"] * 4, "unanimous-against")))
        self.assertIsNone(adjudicate.flag_kind(flag("of", "ADP", ["ADP"] * 4, "agree")))

    def test_a_p1_candidate_beats_a_unanimous_token_and_p3_beats_p4(self):
        # Arrange
        self.assertEqual("P1", adjudicate.band_of({"prenominal-ing", "mention-quoted"}, 3, 2))
        self.assertEqual("P2", adjudicate.band_of({"mention-quoted"}, 1, 2))
        self.assertEqual("P3", adjudicate.band_of({"mention-quoted"}, 0, 2))
        self.assertEqual("P4", adjudicate.band_of(frozenset(), 0, 2))
        self.assertIsNone(adjudicate.band_of(frozenset(), 0, 0))

    def entries(self, count, band="P4"):
        return {("open", "train", "x%03d" % i): adjudicate.Entry(("open", "train", "x%03d" % i), band, 10, frozenset(), 0, 1, i % 3)
                for i in range(count)}

    def test_the_cap_keeps_the_higher_bands_and_cuts_inside_the_lowest_one(self):
        # Arrange: 2 + 1 + 1 sentences in P1..P3 and 6 in P4
        pool = self.entries(6)
        pool[("open", "train", "p1a")] = adjudicate.Entry(("open", "train", "p1a"), "P1", 5, frozenset({"prenominal-ing"}), 0, 0, 0)
        pool[("open", "train", "p1b")] = adjudicate.Entry(("open", "train", "p1b"), "P1", 5, frozenset({"prenominal-ing"}), 0, 0, 0)
        pool[("open", "train", "p2")] = adjudicate.Entry(("open", "train", "p2"), "P2", 5, frozenset(), 1, 0, 0)
        pool[("open", "train", "p3")] = adjudicate.Entry(("open", "train", "p3"), "P3", 5, frozenset({"mention-quoted"}), 0, 0, 0)
        # Act
        selected, cut = adjudicate.apply_cap(pool, cap=7)
        again = adjudicate.apply_cap(pool, cap=7)[0]
        # Assert
        self.assertEqual(7, len(selected))
        self.assertEqual(3, len(cut))
        self.assertEqual({"P4"}, {entry.band for entry in cut})
        self.assertEqual(["P1", "P1", "P2", "P3"], [entry.band for entry in selected[:4]])
        self.assertEqual([entry.key for entry in selected], [entry.key for entry in again])
        # the P4 sentences with the most split-major tokens come first
        self.assertEqual([2, 2, 1], sorted((entry.split_major for entry in selected[4:]), reverse=True))

    def test_the_cap_cuts_inside_p3_when_the_bands_before_it_leave_no_room(self):
        # Arrange
        pool = {("open", "train", "a%d" % i): adjudicate.Entry(("open", "train", "a%d" % i), "P3", 5, frozenset({"mention-quoted"}), 0, 0, 0)
                for i in range(5)}
        pool[("open", "train", "p1")] = adjudicate.Entry(("open", "train", "p1"), "P1", 5, frozenset({"prenominal-ing"}), 0, 0, 0)
        # Act
        selected, cut = adjudicate.apply_cap(pool, cap=3)
        # Assert
        self.assertEqual(("P1", 3), (selected[0].band, len(selected)))
        self.assertEqual(3, len(cut))

    def test_the_report_lists_the_volumes_the_cap_and_the_cut(self):
        # Arrange
        inputs = self.inputs(self.candidates)
        pool = adjudicate.build_pool(inputs, self.flags)
        selected, cut = adjudicate.apply_cap(pool, cap=3)
        # Act
        text = "\n".join(adjudicate.selection_report(pool, selected, cut, inputs, cap=3))
        # Assert
        self.assertIn("Union before the cap: 4 sentences", text)
        self.assertIn("Cap: 3 sentences", text)
        for heading in ("## Bands", "## Sources and splits", "## Cut by split and band", "## Candidate detectors", "## Voter flags"):
            self.assertIn(heading, text)
        self.assertIn("| P4 | 1 | 5 | 4 | 22 | 0 | 1 |", text)
        self.assertIn("| mention-the-word | 1 | 1 | 1 | 0 | P3 |", text)

    def test_the_selection_is_a_pure_function_of_the_inputs(self):
        # Arrange
        inputs = self.inputs(self.candidates)
        # Act
        first = adjudicate.apply_cap(adjudicate.build_pool(inputs, self.flags))[0]
        second = adjudicate.apply_cap(adjudicate.build_pool(inputs, self.flags))[0]
        # Assert
        self.assertEqual([e.key for e in first], [e.key for e in second])

    def test_flags_that_do_not_match_the_corpus_are_refused(self):
        # Arrange
        os.makedirs(self.work.flags)
        columns = ["sent_id", "token_index", "token", "label", "trf", "lg", "sm", "nltk", "flag"]
        adjudicate.write_rows(os.path.join(self.work.flags, "flags_train.tsv"), columns,
                              [("t2", "2", "them", "NOUN", "PRON", "PRON", "PRON", "PRON", "split")])
        # Act and Assert: the label of the flags file is not the corpus label
        with self.assertRaises(ValueError):
            adjudicate.read_flags(self.work.flags, corrections.load_corpora(self.data))


# ---------------------------------------------------------------- decision tokens

def sentence(*cells):
    return {"sent_id": "x", "source_id": "d", "text": "", "comments": {}, "rows": [list(c) for c in cells]}


class DecisionTokenTests(Fixture):
    def test_dependents_of_the_candidate_detectors(self):
        # Arrange
        modes = convention_rules.Sentence("open", "train", sentence(("The", "DET", "u"), ("operating", "VERB", "u"), ("modes", "NOUN", "u"),
                                                                    ("are", "VERB", "u"), ("fine", "ADJ", "u")))
        fascinated = convention_rules.Sentence("open", "train", sentence(("He", "PRON", "u"), ("was", "VERB", "u"), ("fascinated", "ADJ", "u"),
                                                                         ("by", "ADP", "u"), ("it", "PRON", "u")))
        written = convention_rules.Sentence("open", "train", sentence(("It", "PRON", "u"), ("was", "VERB", "u"), ("written", "VERB", "u"),
                                                                      ("by", "ADP", "u"), ("him", "PRON", "u")))
        # Act and Assert
        self.assertEqual([2], adjudicate.dependents("prenominal-ing", 1, modes))
        self.assertEqual([2], adjudicate.dependents("prenominal-ed", 1, modes))
        self.assertEqual([2], adjudicate.dependents("modal-aux-other", 1, modes))
        self.assertEqual([1], adjudicate.dependents("be-participle-gradable", 2, fascinated))
        self.assertEqual([1], adjudicate.dependents("be-passive-agent", 2, fascinated))
        self.assertEqual([2], adjudicate.dependents("be-aux-passive", 1, written))
        self.assertEqual([1], adjudicate.dependents("zero-derived-degree", 2, modes))
        self.assertEqual([3, 1], adjudicate.dependents("out-of-after-verb", 2, modes))
        self.assertEqual([], adjudicate.dependents("mention-quoted", 1, modes))
        self.assertEqual([], adjudicate.dependents("language-name", 1, modes))

    def test_the_decision_set_is_candidates_voter_flags_and_dependents_without_masked_tokens(self):
        # Arrange: a candidate on `as` (its dependent is `such`, which has a mechanical row), a masked token and a voter flag
        rows = [cand("masc", "-", "masc:written/blog/x", MASC1, 2, "as", "ADP", "ptb", "zero-derived-degree"),
                cand("silver", "-", "gutenberg-1", "silver-1", 3, "go", "_", "masked", "half-ambiguous"),
                cand("open", "train", "d2", "t3", 3, "snowball", "NOUN", "disputed", "mention-the-word", span=2)]
        inputs = self.inputs(rows)
        flags = {("train", "t3"): {5: flag("is", "VERB", ["AUX"] * 4, "unanimous-against"),
                                   3: flag("snowball", "NOUN", ["NOUN", "VERB", "NOUN", "NOUN"], "split-major")}}
        # Act
        masc = adjudicate.decision_tokens(inputs, ("masc", "-", MASC1), flags)
        silver = adjudicate.decision_tokens(inputs, ("silver", "-", "silver-1"), flags)
        quoted = adjudicate.decision_tokens(inputs, ("open", "train", "t3"), flags)
        # Assert
        self.assertEqual({1: ("dependent:zero-derived-degree",), 2: ("zero-derived-degree",)}, {i: t.classes for i, t in masc.items()})
        self.assertEqual({}, silver)
        self.assertEqual([3, 4, 5], sorted(quoted))
        self.assertEqual(("mention-the-word", "voter-split"), quoted[3].classes)
        self.assertEqual("mention-the-word", quoted[3].primary)
        self.assertEqual("mention-the-word", quoted[4].primary)
        self.assertEqual("voter-unanimous", quoted[5].primary)

    def test_the_rarest_candidate_detector_is_the_primary_class(self):
        # Arrange: `rare` occurs once, `common` three times in the candidates table
        rows = [cand("open", "train", "d2", "t3", 3, "snowball", "NOUN", "disputed", "mention-the-word"),
                cand("open", "train", "d2", "t3", 3, "snowball", "NOUN", "disputed", "prenominal-ing"),
                cand("open", "train", "d1", "t1", 0, "It", "PRON", "unanimous", "prenominal-ing"),
                cand("open", "train", "d1", "t2", 0, "All", "PRON", "unchecked", "prenominal-ing")]
        inputs = self.inputs(rows)
        # Act
        found = adjudicate.decision_tokens(inputs, ("open", "train", "t3"), {})
        # Assert
        self.assertEqual("mention-the-word", found[3].primary)
        self.assertEqual(("mention-the-word", "prenominal-ing"), found[3].classes)


# ---------------------------------------------------------------- batches

class BatchTests(Fixture):
    def setUp(self):
        super().setUp()
        self.candidates = [cand("open", "train", "d2", "t3", 3, "snowball", "NOUN", "disputed", "mention-the-word"),
                           cand("masc", "-", "masc:written/blog/x", MASC2, 2, "fine", "ADJ", "ptb", "prenominal-ed"),
                           cand("masc", "-", "masc:written/blog/x", MASC1, 2, "as", "ADP", "ptb", "zero-derived-degree"),
                           cand("silver", "-", "gutenberg-1", "silver-1", 2, "not", "PART", "silver", "half-ambiguous")]
        self.write_candidates(self.candidates)
        self.inputs_ = self.inputs(self.candidates)
        self.pool = adjudicate.build_pool(self.inputs_, {})
        self.selected, _ = adjudicate.apply_cap(self.pool)

    def export(self, size=150, rows=3500):
        code, text = run(adjudicate.main, ["--work", self.work.root, "--data-dir", self.data, "select"])
        self.assertEqual(0, code, text)
        code, text = run(adjudicate.main, ["--work", self.work.root, "--data-dir", self.data, "export", "--size", str(size), "--rows",
                                           str(rows)])
        self.assertEqual(0, code, text)
        return text

    def test_the_ids_are_unique_opaque_and_deterministic(self):
        # Arrange
        keys = [e.key for e in self.selected]
        # Act
        ids = adjudicate.assign_ids(keys)
        again = adjudicate.assign_ids(list(reversed(keys)))
        # Assert
        self.assertEqual(ids, again)
        self.assertEqual(len(keys), len(set(ids.values())))
        self.assertTrue(all(adjudicate.ID_RE.match(value) for value in ids.values()))
        for key, value in ids.items():
            self.assertNotIn(key[2], value)

    def test_batches_hold_one_band_obey_the_limits_and_are_deterministic(self):
        # Arrange
        ntokens = {e.key: e.ntokens for e in self.selected}
        # Act
        batches = adjudicate.make_batches(self.selected, ntokens, size=1, budget=100)
        again = adjudicate.make_batches(self.selected, ntokens, size=1, budget=100)
        packed = adjudicate.make_batches(self.selected, ntokens, size=10, budget=13)
        # Assert
        self.assertEqual(4, len(batches))
        self.assertEqual(batches, again)
        self.assertEqual(["b001", "b002", "b003", "b004"], [b.name for b in batches])
        self.assertEqual({"P1", "P3"}, {b.band for b in batches})
        self.assertEqual(sorted(e.key for e in self.selected), sorted(k for b in packed for k in b.keys))
        for batch in packed:
            self.assertLessEqual(sum(ntokens[k] for k in batch.keys), 13 if len(batch.keys) > 1 else 100)
            self.assertEqual(1, len({self.pool[k].band for k in batch.keys}))
        self.assertEqual(["P1", "P1", "P3", "P3"], [b.band for b in batches])

    def test_a_batch_file_has_exactly_three_columns_and_nothing_that_identifies_a_label_or_a_sentence(self):
        # Arrange and Act
        self.export()
        names = sorted(name for name in os.listdir(self.work.batches) if adjudicate.BATCH_RE.match(name[:-4]) and name.endswith(".tsv"))
        text = ""
        for name in names:
            with open(os.path.join(self.work.batches, name), "r", encoding="utf-8", newline="") as handle:
                content = handle.read()
            text += content
            lines = content.split("\n")[:-1]
            # Assert: per file
            self.assertEqual("id\ttoken_index\ttoken", lines[0])
            self.assertTrue(all(len(line.split("\t")) == 3 for line in lines), name)
        # Assert: no label, status, sentence id, document id, source or detector anywhere
        for forbidden in ("unanimous", "unchecked", "disputed", "ptb", "silver\t", "masc", "t1\t", "t3\t", "gutenberg", "d2",
                          "prenominal", "mention", "label", "upos"):
            self.assertNotIn(forbidden, text)
        cells = [line.split("\t") for line in text.split("\n") if line and not line.startswith("id\t")]
        self.assertTrue(all(adjudicate.ID_RE.match(row[0]) and row[1].isdigit() for row in cells))
        self.assertFalse([row for row in cells if len(row) != 3])
        self.assertTrue({"snowball", "fine", "Birds"} <= {row[2] for row in cells})

    def test_the_key_stays_outside_the_batches_folder_and_names_the_corpus_sentence(self):
        # Arrange and Act
        self.export()
        key = adjudicate.read_key(self.work)
        # Assert
        self.assertTrue(os.path.exists(self.work.private_file("key.tsv")))
        self.assertFalse(os.path.exists(os.path.join(self.work.batches, "key.tsv")))
        self.assertEqual({"t3", MASC1, MASC2, "silver-1"}, {k.sent_id for k in key})
        self.assertEqual(["b001", "b002"], list(adjudicate.read_status(self.work)))

    def test_check_batches_passes_and_finds_a_fourth_column_a_changed_token_and_a_missing_row(self):
        # Arrange
        self.export(size=2)
        inputs = adjudicate.load_inputs(self.data)
        # Act
        clean = adjudicate.check_batches(self.work, inputs)
        path = self.work.batch_file("b001")
        with open(path, "r", encoding="utf-8", newline="") as handle:
            original = handle.read()
        with open(path, "w", encoding="utf-8", newline="") as handle:
            lines = original.split("\n")
            lines[1] += "\tNOUN"
            handle.write("\n".join(lines))
        four = adjudicate.check_batches(self.work, inputs)
        with open(path, "w", encoding="utf-8", newline="") as handle:
            handle.write(original.replace("snowball", "snowballs").replace("fine", "finer").replace("Birds", "Bird"))
        changed = adjudicate.check_batches(self.work, inputs)
        with open(path, "w", encoding="utf-8", newline="") as handle:
            handle.write("\n".join(original.split("\n")[:-3]) + "\n")
        missing = adjudicate.check_batches(self.work, inputs)
        # Assert
        self.assertEqual([], clean)
        self.assertTrue(any("exactly 3" in message for message in four))
        self.assertTrue(any("differ" in message for message in changed))
        self.assertTrue(any("differ" in message for message in missing))

    def test_export_refuses_to_overwrite_after_outputs_exist_unless_forced(self):
        # Arrange
        self.export()
        os.makedirs(self.work.out("opus"), exist_ok=True)
        with open(os.path.join(self.work.out("opus"), "b001.tsv"), "w", encoding="utf-8") as handle:
            handle.write("x\n")
        # Act
        code, text = run(adjudicate.main, ["--work", self.work.root, "--data-dir", self.data, "export"])
        forced, _ = run(adjudicate.main, ["--work", self.work.root, "--data-dir", self.data, "export", "--force"])
        # Assert
        self.assertEqual(1, code)
        self.assertIn("refusing", text)
        self.assertEqual(0, forced)

    def test_a_token_with_a_quote_survives_the_export_in_one_cell(self):
        # Arrange: the third sentence of the fixture gets quote and backslash tokens
        path = os.path.join(self.data, "open", "train.tsv")
        lines = common.read_lines(path) + ["", "# sent_id = q1", "# source_id = d9", "# text = a",
                                           "\"\tPUNCT\tunanimous", "''\tPUNCT\tunanimous", "a\"b\tX\tunanimous", "\\\tSYM\tunanimous"]
        common.write_text(path, lines)
        inputs = self.inputs([cand("open", "train", "d9", "q1", 0, "\"", "PUNCT", "unanimous", "mention-quoted")])
        selected, _ = adjudicate.apply_cap(adjudicate.build_pool(inputs, {}))
        batch = adjudicate.make_batches(selected, {e.key: e.ntokens for e in selected})[0]
        ids = adjudicate.assign_ids([e.key for e in selected])
        target = os.path.join(self.temp.name, "q.tsv")
        # Act
        adjudicate.write_batch(target, adjudicate.batch_rows(batch, ids, inputs))
        rows = adjudicate.read_batch(target)
        # Assert
        self.assertEqual(['"', "''", 'a"b', "\\\\"], [row[2] for row in rows])
        self.assertEqual(["\"", "''", "a\"b", "\\"], [adjudicate.unescape_cell(row[2]) for row in rows])


# ---------------------------------------------------------------- tagger outputs

class OutputTests(Fixture):
    EXPECTED = [("s000001", "0", "It"), ("s000001", "1", "is"), ("s000002", "0", "\"")]

    def good(self):
        return [["s000001", "0", "It", "PRON", "high", "-"], ["s000001", "1", "is", "VERB", "low", "mentioned"],
                ["s000002", "0", "\"", "PUNCT", "medium", "foreign+mentioned"]]

    def test_a_correct_output_has_no_error(self):
        # Arrange, Act and Assert
        self.assertEqual([], adjudicate.validate_output_rows(self.EXPECTED, self.good()))

    def test_each_kind_of_defect_is_found(self):
        # Arrange
        def change(index, cell, value):
            rows = self.good()
            rows[index][cell] = value
            return rows
        cases = {
            "missing row": self.good()[:2],
            "extra row": self.good() + [["s000002", "1", "x", "NOUN", "high", "-"]],
            "wrong order": [self.good()[1], self.good()[0], self.good()[2]],
            "token mismatch": change(0, 2, "Its"),
            "id mismatch": change(0, 0, "s000009"),
            "index mismatch": change(1, 1, "5"),
            "bad tag": change(0, 3, "NOUNS"),
            "masked tag": change(0, 3, "_"),
            "lower case tag": change(0, 3, "pron"),
            "bad confidence": change(0, 4, "certain"),
            "bad flags": change(0, 5, "foreign+foreign"),
            "flags out of order": change(0, 5, "mentioned+foreign"),
            "empty flags": change(0, 5, ""),
            "five cells": [row[:5] for row in self.good()],
            "seven cells": [row + ["x"] for row in self.good()]}
        for name, rows in cases.items():
            with self.subTest(defect=name):
                # Act and Assert
                self.assertTrue(adjudicate.validate_output_rows(self.EXPECTED, rows), name)

    def write(self, directory, name, header, rows):
        os.makedirs(directory, exist_ok=True)
        with open(os.path.join(directory, name), "w", encoding="utf-8", newline="") as handle:
            handle.write("\n".join(["\t".join(header)] + ["\t".join(row) for row in rows]) + "\n")

    def test_the_output_is_one_file_or_numbered_parts_but_not_both_and_without_gaps(self):
        # Arrange
        folder = os.path.join(self.temp.name, "out")
        header = adjudicate.OUTPUT_HEADER
        rows = self.good()
        # Act and Assert: nothing written
        self.assertEqual((None, []), adjudicate.load_output(folder, "b001"))
        self.write(folder, "b001.p01.tsv", header, rows[:2])
        self.write(folder, "b001.p02.tsv", header, rows[2:])
        parts, errors = adjudicate.load_output(folder, "b001")
        self.assertEqual((rows, []), (parts, errors))
        self.write(folder, "b001.tsv", header, rows)
        self.assertTrue(adjudicate.load_output(folder, "b001")[1])
        os.remove(os.path.join(folder, "b001.p01.tsv"))
        os.remove(os.path.join(folder, "b001.p02.tsv"))
        self.assertEqual((rows, []), adjudicate.load_output(folder, "b001"))
        os.remove(os.path.join(folder, "b001.tsv"))
        self.write(folder, "b001.p01.tsv", header, rows[:1])
        self.write(folder, "b001.p03.tsv", header, rows[1:])
        self.assertTrue(adjudicate.load_output(folder, "b001")[1])

    def test_a_wrong_header_a_blank_line_and_a_bom_are_refused(self):
        # Arrange
        folder = os.path.join(self.temp.name, "out")
        self.write(folder, "b001.tsv", ("id", "token_index", "token", "tag", "confidence"), [])
        self.write(folder, "b002.tsv", adjudicate.OUTPUT_HEADER, self.good()[:1] + [[]] + self.good()[1:])
        with open(os.path.join(folder, "b003.tsv"), "w", encoding="utf-8-sig") as handle:
            handle.write("\t".join(adjudicate.OUTPUT_HEADER) + "\n")
        # Act and Assert
        for batch in ("b001", "b002", "b003"):
            with self.subTest(batch=batch):
                rows, errors = adjudicate.load_output(folder, batch)
                self.assertIsNone(rows)
                self.assertTrue(errors)

    def test_a_crlf_line_end_is_accepted_as_a_line_end(self):
        # Arrange
        folder = os.path.join(self.temp.name, "out")
        os.makedirs(folder)
        with open(os.path.join(folder, "b001.tsv"), "w", encoding="utf-8", newline="") as handle:
            handle.write("\r\n".join(["\t".join(adjudicate.OUTPUT_HEADER)] + ["\t".join(row) for row in self.good()]) + "\r\n")
        # Act
        rows, errors = adjudicate.load_output(folder, "b001")
        # Assert
        self.assertEqual(([], self.good()), (errors, rows))


# ---------------------------------------------------------------- the decision rule

class DecisionRuleTests(unittest.TestCase):
    MECH = corrections.Correction("open", "train", "d", "s", "0", "w", "ADV", "ADP", "unanimous", "unanimous", "rule:out-of")

    CASES = [
        # name, tag, status, opus, sol, primary, mechanical row, expected (outcome, new tag, new status, kind)
        ("agree on another tag", "NOUN", "unchecked", vote("VERB"), vote("VERB"), "prenominal-ing", None, ("correction", "VERB", "unchecked", "tag")),
        ("agree on another tag keeps the disputed base", "NOUN", "disputed+mentioned", vote("VERB", ["mentioned"]), vote("VERB", ["mentioned"]),
         "x", None, ("correction", "VERB", "disputed+mentioned", "tag")),
        ("agree on another tag with an agreed flag", "NOUN", "unchecked", vote("ADJ", ["foreign"]), vote("ADJ", ["foreign"]), "x", None,
         ("correction", "ADJ", "unchecked+foreign", "tag")),
        ("agree on the tag, an agreed flag is new", "NOUN", "disputed", vote("NOUN", ["mentioned"]), vote("NOUN", ["mentioned"]), "x", None,
         ("flags", "NOUN", "disputed+mentioned", "flags")),
        ("agree on the tag, an agreed flag is removed", "NOUN", "unchecked+mentioned", vote("NOUN"), vote("NOUN"), "x", None,
         ("flags", "NOUN", "unchecked", "flags")),
        ("flag disagreement outside a flag detector", "NOUN", "unchecked", vote("NOUN", ["mentioned"]), vote("NOUN"), "prenominal-ing", None,
         ("no-change", "NOUN", "unchecked", None)),
        ("flag disagreement at a flag detector", "NOUN", "unchecked", vote("NOUN", ["mentioned"]), vote("NOUN"), "mention-quoted", None,
         ("disputed-flag", "NOUN", "disputed", "disputed")),
        ("flag disagreement keeps the current flags in the disputed status", "NOUN", "unchecked+mentioned", vote("NOUN"),
         vote("NOUN", ["mentioned"]), "foreign-token", None, ("disputed-flag", "NOUN", "disputed+mentioned", "disputed")),
        ("flag disagreement at a flag detector on a disputed token", "NOUN", "disputed", vote("NOUN", ["foreign"]), vote("NOUN"),
         "mention-the-word", None, ("already-disputed", "NOUN", "disputed", None)),
        ("both keep the label", "NOUN", "unanimous", vote("NOUN"), vote("NOUN"), "x", None, ("no-change", "NOUN", "unanimous", None)),
        ("tag disagreement", "NOUN", "unchecked+foreign", vote("NOUN"), vote("VERB"), "x", None,
         ("disputed", "NOUN", "disputed+foreign", "disputed")),
        ("tag disagreement on a disputed token", "NOUN", "disputed", vote("ADJ"), vote("VERB"), "x", None,
         ("already-disputed", "NOUN", "disputed", None)),
        ("confidence is not used", "NOUN", "unchecked", vote("VERB", confidence="low"), vote("VERB", confidence="high"), "x", None,
         ("correction", "VERB", "unchecked", "tag")),
        ("mechanical row: both agree with the rule", "ADV", "unanimous", vote("ADP"), vote("ADP"), "x", MECH,
         ("agree-rule", "ADV", "unanimous", None)),
        ("mechanical row: agree with the rule, flags change", "ADV", "unanimous", vote("ADP", ["mentioned"]), vote("ADP", ["mentioned"]), "x", MECH,
         ("replace-flags", "ADP", "unanimous+mentioned", "tag")),
        ("mechanical row: agree on another tag replaces it", "ADV", "unanimous", vote("INTJ"), vote("INTJ"), "x", MECH,
         ("replace", "INTJ", "unanimous", "tag")),
        ("mechanical row: both keep the pristine tag", "ADV", "unanimous", vote("ADV"), vote("ADV"), "x", MECH,
         ("dissent-pristine", "ADV", "unanimous", None)),
        ("mechanical row: the models disagree", "ADV", "unanimous", vote("ADV"), vote("ADP"), "x", MECH,
         ("rule-kept", "ADV", "unanimous", None)),
    ]

    def test_the_decision_table(self):
        for name, tag, status, opus, sol, primary, mech, expected in self.CASES:
            with self.subTest(case=name):
                # Arrange and Act
                decision = adjudicate.decide_token(tag, status, opus, sol, primary, mech)
                # Assert
                self.assertEqual(expected, (decision.outcome, decision.new_tag, decision.new_status, decision.kind))

    def test_every_staged_row_is_valid_and_one_of_the_three_kinds(self):
        for name, tag, status, opus, sol, primary, mech, expected in self.CASES:
            decision = adjudicate.decide_token(tag, status, opus, sol, primary, mech)
            if decision.kind is None:
                continue
            with self.subTest(case=name):
                # Arrange
                row = corrections.Correction("open", "train", "d", "s", "0", "w", tag, decision.new_tag, status, decision.new_status,
                                             "llm:opus+sol:b001")
                # Act and Assert
                self.assertIsNone(corrections.row_error(row))
                self.assertIn(corrections.adjudicated_row_kind(row), ("tag", "disputed", "flags"))
                self.assertIn(decision.kind, ("tag", "disputed", "flags"))

    def test_the_effective_flags_follow_the_agreement_per_flag(self):
        # Arrange, Act and Assert
        self.assertEqual(("foreign",), adjudicate.effective_flags((), vote("X", ["foreign"]), vote("X", ["foreign", "mentioned"])))
        self.assertEqual(("mentioned",), adjudicate.effective_flags(("mentioned",), vote("X", []), vote("X", ["mentioned"])))
        self.assertEqual((), adjudicate.effective_flags(("mentioned",), vote("X", []), vote("X", [])))


# ---------------------------------------------------------------- supersession in the tables

class SupersessionTests(Fixture):
    def adjudicated(self, **changes):
        base = corrections.Correction("open", "train", "d1", "t1", "2", "out", "ADV", "INTJ", "unanimous", "unanimous", "llm:opus+sol:b001")
        return base._replace(**changes)

    def test_an_adjudicated_row_replaces_the_mechanical_row_of_the_same_token(self):
        # Arrange
        tables = [("mechanical.tsv", [OUT, SUCH]), ("adjudicated.tsv", [self.adjudicated()])]
        # Act
        effective, superseded, errors = corrections.combine_tables(tables)
        # Assert
        self.assertEqual([], errors)
        self.assertEqual([SUCH, self.adjudicated()], effective)
        self.assertEqual([OUT], superseded)

    def test_every_other_second_row_for_a_token_is_an_error(self):
        # Arrange
        cases = {
            "different old tag": ([OUT], [self.adjudicated(old_tag="ADP")], "conflict"),
            "different old status": ([OUT], [self.adjudicated(old_status="disputed", new_status="disputed")], "conflict"),
            "the same change": ([OUT], [self.adjudicated(new_tag="ADP")], "duplicate"),
            "two mechanical rows": ([OUT, OUT._replace(new_tag="INTJ")], [], "conflict"),
            "two adjudicated rows": ([], [self.adjudicated(), self.adjudicated(new_tag="NOUN")], "conflict"),
            "adjudicated row without a mechanical row, twice": ([OUT], [self.adjudicated(), self.adjudicated(new_tag="NOUN")], "conflict"),
        }
        for name, (mechanical, adjudicated, word) in cases.items():
            with self.subTest(case=name):
                # Act
                effective, superseded, errors = corrections.combine_tables([("mechanical.tsv", mechanical), ("adjudicated.tsv", adjudicated)])
                # Assert
                self.assertTrue(any(word in message for message in errors), (name, errors))
                self.assertEqual([], superseded)

    def test_validate_applies_and_the_linter_use_the_effective_rows_and_the_files_stay_as_they_are(self):
        # Arrange: the mechanical table is untouched, the adjudicated table replaces the row of `out`
        mechanical_path = os.path.join(self.folder, "mechanical.tsv")
        before = common.sha256_file(mechanical_path)
        lines = adjudicate.table_lines([self.adjudicated()], corrections.load_corpora(self.data), self.data)
        common.write_text(os.path.join(self.folder, "adjudicated.tsv"), lines)
        # Act
        errors, state, counts = corrections.validate_overlay(self.data)
        overlay = corrections.load_overlay(self.data)
        code, text = run(corrections.main, ["--data-dir", self.data, "validate"])
        applied_code, _ = run(corrections.main, ["--data-dir", self.data, "apply", "--write"])
        after_apply_errors, after_state, _ = corrections.validate_overlay(self.data)
        # Assert
        self.assertEqual(([], "pending"), (errors, state))
        self.assertEqual(1, counts["superseded"])
        self.assertEqual(0, code)
        self.assertIn("superseded", text)
        self.assertEqual(before, common.sha256_file(mechanical_path))
        self.assertEqual(["t1"], [r.sent_id for r in overlay["superseded"]])
        self.assertEqual(4 + 1, len(overlay["corrections"]))
        self.assertEqual(0, applied_code)
        self.assertEqual(([], "applied"), (after_apply_errors, after_state))
        with open(os.path.join(self.data, "open", "train.tsv"), encoding="utf-8") as handle:
            self.assertIn("out\tINTJ\tunanimous", handle.read())

    def test_a_replacement_that_starts_from_another_value_fails_validate(self):
        # Arrange
        lines = adjudicate.table_lines([self.adjudicated(old_tag="ADP")], corrections.load_corpora(self.data), self.data)
        common.write_text(os.path.join(self.folder, "adjudicated.tsv"), lines)
        # Act
        code, text = run(corrections.main, ["--data-dir", self.data, "validate"])
        # Assert
        self.assertEqual(1, code)
        self.assertIn("across tables: conflict", text)

    def test_the_linter_applies_the_effective_rows_and_does_not_flag_the_replaced_rule_hit(self):
        # Arrange: `such` is replaced by NOUN, which the rule multiword-prep would flag again
        row = corrections.Correction("masc", "-", "masc:written/blog/x", MASC1, "1", "such", "ADP", "NOUN", "ptb", "ptb", "llm:opus+sol:b001")
        common.write_text(os.path.join(self.folder, "adjudicated.tsv"), adjudicate.table_lines([row], corrections.load_corpora(self.data),
                                                                                              self.data))
        # Act
        code, text = run(check_conventions.main, ["--data-dir", self.data, "--apply", "--tier", "fix", "--enforce"])
        _, sentences = check_conventions.load_sentences(self.data, True)
        found = [s for s in sentences if s.sent_id == MASC1][0]
        # Assert
        self.assertEqual(0, code, text)
        self.assertIn("suppressed by exceptions: 1", text)
        self.assertEqual("NOUN", found.t[1])

    def test_findings_for_the_adjudicated_table(self):
        # Arrange
        corpora = corrections.load_corpora(self.data)
        good = self.adjudicated()
        header = adjudicate.table_lines([good], corpora, self.data)
        rule_row = self.adjudicated(provenance="rule:out-of")
        no_kind = self.adjudicated(new_tag="ADV", new_status="unanimous")  # not even a valid row: no-change
        base_change = good._replace(new_tag="INTJ", new_status="disputed")  # a tag change that also makes the base disputed
        # Act
        clean = corrections.adjudicated_findings(self.data, [good], "pending", header[:-1])
        rule = corrections.adjudicated_findings(self.data, [rule_row], "pending", header[:-1])
        stale_header = [line[:-1] + ("0" if line[-1] != "0" else "1") if "open/train" in line else line for line in header]
        stale = corrections.adjudicated_findings(self.data, [good], "pending", stale_header[:-1])
        applied = corrections.adjudicated_findings(self.data, [good], "applied", stale_header[:-1])
        bad_kind = corrections.adjudicated_findings(self.data, [base_change], "pending", header[:-1])
        # Assert
        self.assertEqual([], clean)
        self.assertTrue(any("provenance" in message for message in rule))
        self.assertTrue(any("stale" in message for message in stale))
        self.assertEqual([], applied)
        self.assertTrue(any("neither a tag change" in message for message in bad_kind))
        self.assertIsNone(corrections.adjudicated_row_kind(no_kind))

    def test_malformed_metadata_headers_of_the_adjudicated_table_are_findings(self):
        # Arrange
        corpora = corrections.load_corpora(self.data)
        comments = adjudicate.table_lines([self.adjudicated()], corpora, self.data)[:-1]
        train = next(line for line in comments if line.startswith("# input = open/train.tsv"))
        cases = {
            "no corpus line": ([line for line in comments if not line.startswith("# corpus = ")], "corpus"),
            "no license line": ([line for line in comments if not line.startswith("# license = ")], "license"),
            "no kind line": ([line for line in comments if not line.startswith("# kind = ")], "kind"),
            "no generator line": ([line for line in comments if not line.startswith("# generator = ")], "generator"),
            "repeated generator line": (comments + [line for line in comments if line.startswith("# generator = ")], "generator"),
            "wrong kind": ([line.replace("= corrections", "= candidates") if line.startswith("# kind = ") else line for line in comments], "kind"),
            "foreign generator": ([line.replace("adjudicate.py", "other.py") if line.startswith("# generator = ") else line for line in comments],
                                  "generator"),
            "wrong license": ([line.replace("CC0-1.0", "MIT") if line.startswith("# license = ") else line for line in comments], "license"),
            "no input line for an existing corpus": ([line for line in comments if line != train], "no '# input' line"),
            "malformed input line": ([train[:-5] if line == train else line for line in comments], "malformed"),
            "unknown corpus file": (comments + ["# input = open/other.tsv sha256 " + "0" * 64], "unknown corpus file"),
            "repeated input line": (comments + [train], "repeated"),
            "unknown metadata line": (comments + ["# note = something"], "unknown metadata line"),
        }
        # Act and Assert
        self.assertEqual([], corrections.adjudicated_findings(self.data, [self.adjudicated()], "pending", comments))
        for name, (header, word) in cases.items():
            with self.subTest(case=name):
                findings = corrections.adjudicated_findings(self.data, [self.adjudicated()], "pending", header)
                self.assertTrue(any(word in message for message in findings), (name, findings))

    def test_validate_reports_a_table_file_whose_metadata_lines_were_removed(self):
        # Arrange
        lines = adjudicate.table_lines([self.adjudicated()], corrections.load_corpora(self.data), self.data)
        common.write_text(os.path.join(self.folder, "adjudicated.tsv"),
                          [line for line in lines if not line.startswith(("# license = ", "# generator = "))])
        # Act
        code, text = run(corrections.main, ["--data-dir", self.data, "validate"])
        # Assert
        self.assertEqual(1, code)
        self.assertIn("'# license' line is missing", text)
        self.assertIn("'# generator' line is missing", text)

    def test_make_corrections_check_validates_an_existing_adjudicated_table(self):
        # Arrange: the regenerated tables are current, so only the adjudicated table can fail the check
        common.write_text(os.path.join(self.folder, "adjudicated.tsv"),
                          adjudicate.table_lines([self.adjudicated()], corrections.load_corpora(self.data), self.data))
        # Act
        good_code, good_text = run(make_corrections.main, ["--data-dir", self.data, "--check"])
        common.write_text(os.path.join(self.folder, "adjudicated.tsv"),
                          adjudicate.table_lines([self.adjudicated(provenance="rule:out-of")], corrections.load_corpora(self.data), self.data))
        bad_code, bad_text = run(make_corrections.main, ["--data-dir", self.data, "--check"])
        # Assert
        self.assertEqual(0, good_code, good_text)
        self.assertIn("adjudicated.tsv is valid", good_text)
        self.assertEqual(1, bad_code)
        self.assertIn("adjudicated.tsv", bad_text)

    def test_apply_refuses_an_adjudicated_table_with_findings_and_leaves_the_corpora_alone(self):
        # Arrange: a rule row in the adjudicated table that matches the corpus, so only the adjudication findings can refuse it
        corpus_path = os.path.join(self.data, "open", "train.tsv")
        before = common.sha256_file(corpus_path)
        common.write_text(os.path.join(self.folder, "adjudicated.tsv"),
                          adjudicate.table_lines([self.adjudicated(provenance="rule:out-of")], corrections.load_corpora(self.data), self.data))
        # Act
        code, text = run(corrections.main, ["--data-dir", self.data, "apply", "--write"])
        # Assert
        self.assertEqual(1, code)
        self.assertIn("refusing to apply", text)
        self.assertIn("adjudicated.tsv", text)
        self.assertEqual(before, common.sha256_file(corpus_path))

    def test_make_corrections_check_reports_a_conflict_between_the_tables(self):
        # Arrange: an adjudicated row that starts from another old tag than the mechanical row of the same token
        common.write_text(os.path.join(self.folder, "adjudicated.tsv"),
                          adjudicate.table_lines([self.adjudicated(old_tag="ADP")], corrections.load_corpora(self.data), self.data))
        # Act
        code, text = run(make_corrections.main, ["--data-dir", self.data, "--check"])
        # Assert
        self.assertEqual(1, code)
        self.assertIn("conflict", text)

    def test_the_generator_never_writes_the_adjudicated_table(self):
        # Arrange
        files = make_corrections.generate_all(self.data)[0]
        # Act
        make_corrections.write_files(files, self.data)
        # Assert
        self.assertEqual({"mechanical.tsv", "candidates.tsv", "exceptions.tsv"}, set(files))
        self.assertFalse(os.path.exists(os.path.join(self.folder, "adjudicated.tsv")))


# ---------------------------------------------------------------- withdrawn rows

class WithdrawTests(Fixture):
    """`corrections.py withdraw`: the rows of one detector leave adjudicated.tsv and stay as provenance."""

    SNOWBALL = corrections.Correction("open", "train", "d2", "t3", "3", "snowball", "NOUN", "NOUN", "disputed", "disputed+mentioned",
                                      "llm:opus+sol:b001")
    LEFT = corrections.Correction("open", "train", "d1", "t2", "3", "left", "VERB", "VERB", "unchecked", "disputed", "llm:opus+sol:b002")
    REASON = "the follow-up selected the arm without these rows"

    def setUp(self):
        super().setUp()
        self.adjudicated_path = os.path.join(self.folder, "adjudicated.tsv")
        self.withdrawn_path = os.path.join(self.folder, "withdrawn-mention-the-word.tsv")
        self.write_adjudicated([self.SNOWBALL, self.LEFT])

    def write_adjudicated(self, rows):
        common.write_text(self.adjudicated_path, adjudicate.table_lines(rows, corrections.load_corpora(self.data), self.data))

    def withdraw(self, *extra, detector="mention-the-word", reason=None):
        return run(corrections.main, ["--data-dir", self.data, "withdraw", "--detector", detector,
                                      "--reason", self.REASON if reason is None else reason, *extra])

    def test_a_dry_run_reports_the_counts_and_writes_nothing(self):
        # Arrange
        before = common.sha256_file(self.adjudicated_path)
        # Act
        code, text = self.withdraw()
        # Assert
        self.assertEqual(0, code, text)
        self.assertIn("1 rows leave adjudicated.tsv (open/train 1); 1 stay", text)
        self.assertIn("nothing written", text)
        self.assertEqual(before, common.sha256_file(self.adjudicated_path))
        self.assertFalse(os.path.exists(self.withdrawn_path))

    def test_write_moves_the_rows_byte_for_byte_and_the_overlay_stays_valid(self):
        # Arrange
        original = common.read_lines(self.adjudicated_path)
        row_line = "\t".join(self.SNOWBALL)
        # Act
        code, text = self.withdraw("--write")
        code_valid, text_valid = run(corrections.main, ["--data-dir", self.data, "validate"])
        # Assert
        self.assertEqual((0, 0), (code, code_valid), text + text_valid)
        kept = common.read_lines(self.adjudicated_path)
        withdrawn = common.read_lines(self.withdrawn_path)
        self.assertEqual([line for line in original if line != row_line], kept)
        self.assertEqual([row_line], [line for line in withdrawn if not line.startswith("# ")])
        self.assertIn("# detector = mention-the-word", withdrawn)
        self.assertIn("# reason = " + self.REASON, withdrawn)
        self.assertIn("# kind = withdrawn corrections", withdrawn)
        self.assertEqual([line for line in original if line.startswith("# input = ")],
                         [line for line in withdrawn if line.startswith("# input = ")])
        self.assertIn("withdrawn-mention-the-word.tsv 1 rows", text_valid)

    def test_the_withdrawn_tokens_keep_the_old_value_when_the_remaining_rows_are_applied(self):
        # Arrange
        self.withdraw("--write")
        # Act
        code_apply, _ = run(corrections.main, ["--data-dir", self.data, "apply", "--write"])
        code_valid, text_valid = run(corrections.main, ["--data-dir", self.data, "validate"])
        sentences = common.read_corpus(os.path.join(self.data, "open", "train.tsv"))[1]
        cells = {s["sent_id"]: s["rows"] for s in sentences}
        # Assert
        self.assertEqual((0, 0), (code_apply, code_valid), text_valid)
        self.assertEqual("disputed", cells["t3"][3][2])        # the snowball row stays unapplied
        self.assertEqual("disputed", cells["t2"][3][2])        # the remaining row is applied
        self.assertIn("applied", text_valid)

    def test_withdraw_refuses_in_the_applied_state_and_when_the_table_exists(self):
        # Arrange
        run(corrections.main, ["--data-dir", self.data, "apply", "--write"])
        # Act
        code_applied, text_applied = self.withdraw("--write")
        run(corrections.main, ["--data-dir", self.data, "apply", "--write", "--reverse"])
        self.withdraw("--write")
        code_again, text_again = self.withdraw("--write")
        # Assert
        self.assertEqual(1, code_applied)
        self.assertIn("applied state", text_applied)
        self.assertEqual(1, code_again)
        self.assertIn("exists already", text_again)

    def test_withdraw_refuses_a_bad_detector_a_blank_reason_and_a_detector_without_rows(self):
        # Arrange and Act
        bad_detector = self.withdraw(detector="Not A Detector")
        blank = self.withdraw(reason=" ")
        none = self.withdraw(detector="infinitive-to")
        # Assert
        for (code, text), word in ((bad_detector, "not a detector"), (blank, "reason"), (none, "no adjudicated row")):
            self.assertEqual(1, code)
            self.assertIn(word, text)
        self.assertFalse(os.path.exists(self.withdrawn_path))

    def test_withdraw_refuses_rows_that_replace_a_mechanical_row(self):
        # Arrange: an adjudicated row for `out` (the mechanical row of rule out-of) that a candidate of another detector covers
        replacing = corrections.Correction("open", "train", "d1", "t1", "2", "out", "ADV", "INTJ", "unanimous", "unanimous", "llm:opus+sol:b003")
        self.write_adjudicated([replacing, self.LEFT])
        corrections.write_table(os.path.join(self.folder, "candidates.tsv"), "candidates",
                                ["# columns = " + ", ".join(corrections.COLUMNS["candidates"])],
                                [cand("open", "train", "d1", "t1", 2, "out", "ADV", "unanimous", "out-of-after-verb")])
        # Act
        code, text = self.withdraw(detector="out-of-after-verb", reason=self.REASON)
        # Assert
        self.assertEqual(1, code)
        self.assertIn("replace mechanical rows", text)

    def test_a_row_of_a_detector_with_a_span_belongs_to_every_token_of_the_span(self):
        # Arrange
        rows = [self.SNOWBALL._replace(token_index=str(index)) for index in (3, 4)]
        candidates = [cand("open", "train", "d2", "t3", 3, "snowball", "NOUN", "disputed", "mention-quoted", span=2),
                      cand("open", "train", "d2", "t3", 3, "snowball", "NOUN", "disputed", "mention-the-word")]
        # Act and Assert
        self.assertEqual(rows, corrections.detector_rows(rows, candidates, "mention-quoted"))
        self.assertEqual(rows[:1], corrections.detector_rows(rows, candidates, "mention-the-word"))
        self.assertEqual([], corrections.detector_rows(rows, candidates, "infinitive-to"))

    def test_validate_reports_the_defects_of_a_withdrawn_table(self):
        # Arrange
        self.withdraw("--write")
        original = common.read_lines(self.withdrawn_path)
        row_line = "\t".join(self.SNOWBALL)
        cases = {
            "no reason": ([line for line in original if not line.startswith("# reason = ")], "'# reason' line is missing"),
            "empty reason": ([("# reason = " if line.startswith("# reason = ") else line) for line in original], "'# reason' line is empty"),
            "wrong detector": ([line.replace("= mention-the-word", "= other") for line in original], "'# detector' is"),
            "wrong kind": ([line.replace("withdrawn corrections", "corrections") for line in original], "'# kind' is"),
            "unknown metadata": (original[:-1] + ["# note = x", original[-1]], "unknown metadata line"),
            "foreign provenance": ([line.replace("llm:opus+sol:b001", "llm:other:run") for line in original], "provenance"),
            "no candidate of the detector": ([("\t".join(self.LEFT) if line == row_line else line) for line in original],
                                             "no candidate of the detector"),
            "new value in the corpus": ([line.replace("NOUN\tNOUN\tdisputed\tdisputed+mentioned", "NOUN\tNOUN\tdisputed+mentioned\tdisputed")
                                         if line == row_line else line for line in original], "hold the new value"),
        }
        # Act and Assert
        for name, (lines, word) in cases.items():
            with self.subTest(case=name):
                common.write_text(self.withdrawn_path, lines)
                code, text = run(corrections.main, ["--data-dir", self.data, "validate"])
                self.assertEqual(1, code, (name, text))
                self.assertIn(word, text)

    def test_validate_reports_a_withdrawn_token_that_is_also_in_a_current_table(self):
        # Arrange
        self.withdraw("--write")
        self.write_adjudicated([self.SNOWBALL._replace(provenance="llm:opus+sol:b009"), self.LEFT])
        # Act
        code, text = run(corrections.main, ["--data-dir", self.data, "validate"])
        # Assert
        self.assertEqual(1, code)
        self.assertIn("also in a current correction table", text)


# ---------------------------------------------------------------- the whole pipeline on the fixture

class PipelineTests(Fixture):
    def setUp(self):
        super().setUp()
        self.candidates = [
            cand("open", "train", "d2", "t3", 3, "snowball", "NOUN", "disputed", "mention-the-word"),
            cand("masc", "-", "masc:written/blog/x", MASC2, 2, "fine", "ADJ", "ptb", "prenominal-ed"),
            cand("masc", "-", "masc:written/blog/x", MASC1, 2, "as", "ADP", "ptb", "zero-derived-degree"),
            cand("silver", "-", "gutenberg-1", "silver-1", 2, "not", "PART", "silver", "half-ambiguous")]
        self.write_candidates(self.candidates)
        columns = ["sent_id", "token_index", "token", "label", "trf", "lg", "sm", "nltk", "flag"]
        os.makedirs(self.work.flags)
        adjudicate.write_rows(os.path.join(self.work.flags, "flags_train.tsv"), columns, [
            ("t1", "2", "out", "ADV", "ADP", "ADP", "ADP", "ADP", "unanimous-against"),
            ("t1", "4", "order", "NOUN", "VERB", "VERB", "NOUN", "NOUN", "split"),
            ("t2", "2", "them", "PRON", "NOUN", "NOUN", "PRON", "PRON", "split")])
        adjudicate.write_rows(os.path.join(self.work.flags, "flags_dev.tsv"), columns, [
            ("v1", "0", "Most", "PRON", "ADJ", "ADJ", "ADJ", "ADJ", "unanimous-against"),
            ("v1", "3", "came", "VERB", "ADJ", "ADJ", "ADJ", "ADJ", "unanimous-against")])
        self.votes = {("t3", 3): ("NOUN", "mentioned"), (MASC2, 2): ("ADV", "-"), (MASC1, 1): ("NOUN", "-"),
                      ("silver-1", 2): ("ADV", "-"), ("t1", 2): ("ADP", "-"), ("v1", 0): ("PRON", "-"), ("t2", 0): ("DET", "-")}

    def call(self, *argv):
        return run(adjudicate.main, ["--work", self.work.root, "--data-dir", self.data] + list(argv))

    def tag_everything(self, per_model, models=adjudicate.MODELS):
        """Write the outputs of the models: the corpus tags except `per_model[model]` = {(sent_id, index): (tag, flags)}."""
        inputs = adjudicate.load_inputs(self.data)
        key = {item.opaque_id: item for item in adjudicate.read_key(self.work)}
        for model in models:
            wanted = per_model.get(model, {})
            for batch in adjudicate.read_status(self.work):
                rows = adjudicate.read_batch(self.work.batch_file(batch))
                lines = ["\t".join(adjudicate.OUTPUT_HEADER)]
                for opaque, index, token in rows:
                    item = key[opaque]
                    cells = inputs.sentence((item.source, item.split, item.sent_id))["rows"][int(index)]
                    tag, flags = wanted.get((item.sent_id, int(index)), ("VERB" if cells[1] == "_" else cells[1], "-"))
                    lines.append("\t".join((opaque, index, token, tag, "high", flags)))
                os.makedirs(self.work.out(model), exist_ok=True)
                with open(os.path.join(self.work.out(model), batch + ".tsv"), "w", encoding="utf-8", newline="") as handle:
                    handle.write("\n".join(lines) + "\n")

    def prepare(self):
        code, text = self.call("select")
        self.assertEqual(0, code, text)
        code, text = self.call("export")
        self.assertEqual(0, code, text)
        self.assertEqual(0, self.call("check-batches")[0])
        overrides = dict(self.votes)
        split = {"opus": {**overrides, ("t1", 4): ("NOUN", "-")}, "sol": {**overrides, ("t1", 4): ("VERB", "-")}}
        self.tag_everything(split)

    def test_select_prints_the_volumes_first_and_writes_the_selection_and_the_report(self):
        # Arrange and Act
        code, text = self.call("select")
        selection = adjudicate.read_selection(self.work)
        # Assert
        self.assertEqual(0, code)
        self.assertTrue(text.startswith("# Selection report"))
        self.assertIn("Union before the cap: 7 sentences", text)
        self.assertEqual(["P1", "P1", "P2", "P3", "P3", "P4", "P4"], [band for _, band, _ in selection])
        self.assertTrue(os.path.exists(self.work.private_file("selection-report.md")))

    def test_export_status_and_validate_output(self):
        # Arrange
        self.call("select")
        self.call("export")
        self.tag_everything({})
        os.remove(os.path.join(self.work.out("sol"), "b002.tsv"))
        with open(os.path.join(self.work.out("sol"), "b001.tsv"), "a", encoding="utf-8") as handle:
            handle.write("s000000\t0\tx\tNOUN\thigh\t-\n")
        # Act
        code, text = self.call("validate-output", "--all")
        status = adjudicate.read_status(self.work)
        again_code, _ = self.call("validate-output", "--batch", "b003", "--model", "opus", "--no-count")
        # Assert
        self.assertEqual(1, code)
        self.assertIn("b001 sol: invalid", text)
        self.assertIn("b001 opus: ok", text)
        self.assertNotIn("b002 sol", text)
        self.assertEqual("ok", status["b001"]["opus_state"])
        self.assertEqual("invalid", status["b001"]["sol_state"])
        self.assertIn("sol:", status["b001"]["note"])
        self.assertEqual(("1", "1"), (status["b001"]["attempts_opus"], status["b001"]["attempts_sol"]))
        self.assertEqual("pending", status["b002"]["sol_state"])
        self.assertEqual("1", status["b003"]["attempts_opus"])
        self.assertEqual(0, again_code)
        self.assertEqual("1", adjudicate.read_status(self.work)["b003"]["attempts_opus"])

    def test_an_invalid_output_is_asked_for_once_more_and_the_second_failure_skips_the_batch(self):
        # Arrange: the sol output of b001 has an extra row, so it is invalid on every attempt
        self.call("select")
        self.call("export")
        self.tag_everything({})
        path = os.path.join(self.work.out("sol"), "b001.tsv")
        with open(path, "a", encoding="utf-8") as handle:
            handle.write("s000000\t0\tx\tNOUN\thigh\t-\n")
        # Act
        first_code, first_text = self.call("validate-output", "--batch", "b001", "--model", "sol")
        first = dict(adjudicate.read_status(self.work)["b001"])
        second_code, second_text = self.call("validate-output", "--batch", "b001", "--model", "sol")
        second = dict(adjudicate.read_status(self.work)["b001"])
        third_code, third_text = self.call("validate-output", "--batch", "b001", "--model", "sol")
        third = dict(adjudicate.read_status(self.work)["b001"])
        # Assert
        self.assertEqual((1, "invalid", "1"), (first_code, first["sol_state"], first["attempts_sol"]))
        self.assertIn("b001 sol: invalid", first_text)
        self.assertEqual((0, "skipped", str(adjudicate.MAX_ATTEMPTS)), (second_code, second["sol_state"], second["attempts_sol"]))
        self.assertIn("b001 sol: skipped", second_text)
        self.assertIn("attempt limit", second["note"])
        self.assertEqual((0, "skipped", str(adjudicate.MAX_ATTEMPTS)), (third_code, third["sol_state"], third["attempts_sol"]))
        self.assertIn("attempt limit", third_text)
        self.assertEqual(second["note"], third["note"])

    def test_a_skipped_state_is_final_even_when_a_valid_output_appears_later(self):
        # Arrange
        self.call("select")
        self.call("export")
        self.tag_everything({})
        path = os.path.join(self.work.out("sol"), "b001.tsv")
        good = common.read_lines(path)
        with open(path, "a", encoding="utf-8") as handle:
            handle.write("s000000\t0\tx\tNOUN\thigh\t-\n")
        self.call("validate-output", "--batch", "b001", "--model", "sol")
        self.call("validate-output", "--batch", "b001", "--model", "sol")
        with open(path, "w", encoding="utf-8", newline="") as handle:
            handle.write("\n".join(good) + "\n")
        # Act
        code, text = self.call("validate-output", "--batch", "b001", "--model", "sol")
        status = adjudicate.read_status(self.work)["b001"]
        # Assert
        self.assertEqual(0, code)
        self.assertIn("skipped", text)
        self.assertEqual(("skipped", str(adjudicate.MAX_ATTEMPTS)), (status["sol_state"], status["attempts_sol"]))

    def test_the_attempt_counter_never_passes_the_limit_and_a_valid_retry_is_accepted(self):
        # Arrange: first attempt invalid, the retry valid, then more re-validations
        self.call("select")
        self.call("export")
        self.tag_everything({})
        path = os.path.join(self.work.out("opus"), "b001.tsv")
        good = common.read_lines(path)
        with open(path, "a", encoding="utf-8") as handle:
            handle.write("s000000\t0\tx\tNOUN\thigh\t-\n")
        self.call("validate-output", "--batch", "b001", "--model", "opus")
        with open(path, "w", encoding="utf-8", newline="") as handle:
            handle.write("\n".join(good) + "\n")
        # Act
        for _ in range(3):
            code, _ = self.call("validate-output", "--batch", "b001", "--model", "opus")
        status = adjudicate.read_status(self.work)["b001"]
        # Assert
        self.assertEqual(0, code)
        self.assertEqual(("ok", str(adjudicate.MAX_ATTEMPTS)), (status["opus_state"], status["attempts_opus"]))

    def test_a_skipped_batch_is_not_decided_and_is_listed_as_incomplete(self):
        # Arrange
        self.prepare()
        for _ in range(adjudicate.MAX_ATTEMPTS):
            with open(os.path.join(self.work.out("sol"), "b001.tsv"), "a", encoding="utf-8") as handle:
                handle.write("s000000\t0\tx\tNOUN\thigh\t-\n")
            self.call("validate-output", "--batch", "b001", "--model", "sol")
        out = os.path.join(self.temp.name, "adjudicated.tsv")
        # Act
        key = [item for item in adjudicate.read_key(self.work) if item.batch == "b001"]
        code, text = self.call("decide", "--out", out)
        incomplete = adjudicate.read_records(self.work.private_file("incomplete-batches.tsv"))[1]
        _, records = adjudicate.read_records(self.work.private_file("decisions.tsv"))
        # Assert
        self.assertEqual(0, code, text)
        self.assertEqual([("b001", "skipped after %d attempts (sol)" % adjudicate.MAX_ATTEMPTS)], [(r["batch"], r["reason"]) for r in incomplete])
        self.assertEqual(set(), {r["sent_id"] for r in records} & {item.sent_id for item in key})

    def test_decide_writes_the_table_the_reports_and_the_superseded_note(self):
        # Arrange
        self.prepare()
        out = os.path.join(self.temp.name, "adjudicated.tsv")
        # Act
        code, text = self.call("decide", "--out", out)
        # Assert
        self.assertEqual(0, code, text)
        header, rows = corrections.parse_table("corrections", common.read_lines(out))
        got = {(r.sent_id, r.token_index): (r.old_tag, r.new_tag, r.old_status, r.new_status) for r in rows}
        self.assertEqual({("t3", "3"): ("NOUN", "NOUN", "disputed", "disputed+mentioned"),
                          (MASC2, "2"): ("ADJ", "ADV", "ptb", "ptb"),
                          (MASC1, "1"): ("ADP", "NOUN", "ptb", "ptb"),
                          ("t1", "4"): ("NOUN", "NOUN", "unanimous", "disputed")}, got)
        self.assertTrue(all(r.provenance.startswith("llm:opus+sol:b") for r in rows))
        _, records = adjudicate.read_records(self.work.private_file("decisions.tsv"))
        outcome = {(r["sent_id"], r["token_index"]): (r["outcome"], r["final"]) for r in records}
        self.assertEqual(("replace", "staged"), outcome[(MASC1, "1")])
        self.assertEqual(("correction", "fix-conflict"), outcome[("silver-1", "2")])
        self.assertEqual(("agree-rule", ""), outcome[("t1", "2")])
        self.assertEqual(("dissent-pristine", ""), outcome[("v1", "0")])
        self.assertEqual(("no-change", ""), outcome[("v1", "3")])
        self.assertEqual(("disputed", "staged"), outcome[("t1", "4")])
        outside = adjudicate.read_records(self.work.private_file("outside-agreed.tsv"))[1]
        self.assertEqual([("t2", "0", "PRON", "DET")], [(r["sent_id"], r["token_index"], r["label"], r["both_models"]) for r in outside])
        dissent = adjudicate.read_records(self.work.private_file("mechanical-dissent.tsv"))[1]
        self.assertEqual({("masc", "multiword-prep", "replace", "yes"), ("open", "partitive-adj", "dissent-pristine", "no")},
                         {(r["source"], r["mech_rule"], r["outcome"], r["staged"]) for r in dissent})
        conflicts = adjudicate.read_records(self.work.private_file("fix-conflicts.tsv"))[1]
        self.assertEqual([("silver-1", "negation-part")], [(r["sent_id"], r["rule"]) for r in conflicts])
        hits = adjudicate.read_records(self.work.private_file("superseded-rule-hits.tsv"))[1]
        self.assertEqual([("such", "multiword-prep")], [(r["token"], r["rule"]) for r in hits])
        disputed = adjudicate.read_records(os.path.join(self.work.root, "disputed.tsv"))[1]
        self.assertEqual([("t1", "4", "order", "NOUN", "NOUN", "VERB")],
                         [(r["sent_id"], r["token_index"], r["token"], r["current_tag"], r["tag_opus"], r["tag_sol"]) for r in disputed])
        self.assertIn("Agreement of the two models per class", text)
        self.assertIn("decisions.tsv sha256", text)

    def test_the_written_table_validates_with_the_untouched_mechanical_table_and_the_linter(self):
        # Arrange
        self.prepare()
        mechanical = common.sha256_file(os.path.join(self.folder, "mechanical.tsv"))
        out = os.path.join(self.folder, "adjudicated.tsv")
        self.assertEqual(0, self.call("decide", "--out", out)[0])
        # Act
        errors, state, counts = corrections.validate_overlay(self.data)
        code, text = run(check_conventions.main, ["--data-dir", self.data, "--apply", "--tier", "fix", "--enforce"])
        # Assert
        self.assertEqual(([], "pending"), (errors, state))
        self.assertEqual({"mechanical.tsv": 5, "adjudicated.tsv": 4, "superseded": 1}, {k: v for k, v in counts.items() if k != "candidates.tsv"
                                                                                       and k != "exceptions.tsv"})
        self.assertEqual(mechanical, common.sha256_file(os.path.join(self.folder, "mechanical.tsv")))
        self.assertEqual(0, code, text)

    def test_a_dropped_class_loses_its_tag_and_flag_rows_but_keeps_the_disputed_marks(self):
        # Arrange
        self.prepare()
        out = os.path.join(self.temp.name, "adjudicated.tsv")
        # Act
        code, _ = self.call("decide", "--out", out, "--drop", "prenominal-ed,mention-the-word,zero-derived-degree,dependent:zero-derived-degree")
        rows = corrections.parse_table("corrections", common.read_lines(out))[1]
        _, records = adjudicate.read_records(self.work.private_file("decisions.tsv"))
        # Assert
        self.assertEqual(0, code)
        self.assertEqual([("t1", "4", "disputed")], [(r.sent_id, r.token_index, r.new_status) for r in rows])
        self.assertEqual({"dropped-class"}, {r["final"] for r in records if r["sent_id"] in ("t3", MASC2) and r["row_kind"]})

    def test_a_batch_without_both_valid_outputs_is_left_out_and_listed(self):
        # Arrange
        self.prepare()
        os.remove(os.path.join(self.work.out("sol"), "b001.tsv"))
        out = os.path.join(self.temp.name, "adjudicated.tsv")
        # Act
        code, text = self.call("decide", "--out", out)
        # Assert
        self.assertEqual(0, code, text)
        incomplete = adjudicate.read_records(self.work.private_file("incomplete-batches.tsv"))[1]
        self.assertEqual(["b001"], [r["batch"] for r in incomplete])

    def test_the_sample_is_blind_has_controls_and_the_random_subset_only_in_the_key(self):
        # Arrange
        self.prepare()
        self.call("decide", "--out", os.path.join(self.temp.name, "adjudicated.tsv"))
        # Act
        code, text = self.call("sample", "--random", "1", "--per-class", "1", "--min-total", "0", "--controls", "0")
        markdown = "\n".join(common.read_lines(self.work.private_file("review-sample.md")))
        keys = adjudicate.read_records(self.work.private_file("review-sample-key.tsv"))[1]
        # Assert
        self.assertEqual(0, code, text)
        self.assertEqual(3, len(keys))
        self.assertEqual(1, sum(k["random60"] == "yes" for k in keys))
        for forbidden in ("prenominal-ed", "mention-the-word", "zero-derived-degree", "b00", "opus", "sol", "voter", "random60", "unanimous-against"):
            self.assertNotIn(forbidden, markdown)
        self.assertIn("Current: ADJ (ptb)", markdown)
        self.assertIn("Proposed: ADV (ptb)", markdown)
        self.assertIn("[[fine/ADJ]]", markdown)

    def test_review_result_reads_the_verdicts_and_names_the_classes_to_drop(self):
        # Arrange
        self.prepare()
        self.call("decide", "--out", os.path.join(self.temp.name, "adjudicated.tsv"))
        self.call("sample", "--random", "0", "--per-class", "1", "--min-total", "0", "--controls", "0")
        keys = adjudicate.read_records(self.work.private_file("review-sample-key.tsv"))[1]
        verdicts = os.path.join(self.temp.name, "verdicts.tsv")
        lines = []
        for k in keys:
            lines.append("%s\t%s\tcite" % (k["item"], "incorrect" if k["class"] == "prenominal-ed" else "correct"))
        with open(verdicts, "w", encoding="utf-8") as handle:
            handle.write("\n".join(lines) + "\n")
        # Act
        code, text = self.call("review-result", "--verdicts", verdicts)
        # Assert: three items are fewer than 60, so the exit code asks for more, but the figures and the gate are there
        self.assertEqual(1, code)
        self.assertIn("WARNING: fewer than 60", text)
        self.assertIn("| prenominal-ed | 1 | 0 | 1 | 0 | 0.0% |", text)
        self.assertIn("second round: prenominal-ed", text)


# ---------------------------------------------------------------- the sample and the gate

def record(name, number, kind="tag", final="staged", **extra):
    base = {"source": "open", "split": "train", "source_id": "d1", "sent_id": "t%d" % number, "token_index": "0", "token": "w", "class": name,
            "classes": name, "band": "P1", "batch": "b001", "current_tag": "NOUN", "current_status": "unchecked", "new_tag": "VERB",
            "new_status": "unchecked", "row_kind": kind, "final": final}
    base.update(extra)
    return base


class SampleAndGateTests(unittest.TestCase):
    def records(self):
        rows, number = [], 0
        for name, size in (("a", 30), ("b", 30), ("c", 5), ("d", 1), ("e", 200)):
            for _ in range(size):
                number += 1
                rows.append(record(name, number))
        rows.append(record("a", 900, kind="disputed"))
        rows.append(record("a", 901, final="dropped-class"))
        return rows

    def test_the_sample_has_the_random_subset_the_class_top_up_the_fill_and_the_controls(self):
        # Arrange
        rows = self.records()
        # Act
        items = adjudicate.build_sample(rows, seed=1, n_random=60, per_class=8, min_total=150, n_controls=8)
        again = adjudicate.build_sample(rows, seed=1, n_random=60, per_class=8, min_total=150, n_controls=8)
        # Assert
        real = [i for i in items if i.kind == "real"]
        controls = [i for i in items if i.kind == "control"]
        self.assertEqual(8, len(controls))
        self.assertEqual(60, sum(i.random60 for i in real))
        self.assertEqual(150, len(real))                      # 60 random, the class top-up, then a random fill up to the minimum
        counts = collections.Counter(i.record["class"] for i in real)
        self.assertTrue(all(counts[name] >= min(8, size) for name, size in (("a", 30), ("b", 30), ("c", 5), ("d", 1), ("e", 200))))
        self.assertEqual([(i.kind, i.record["sent_id"]) for i in items], [(i.kind, i.record["sent_id"]) for i in again])
        self.assertTrue(all(i.record["row_kind"] in ("tag", "flags") and i.record["final"] == "staged" for i in items))
        self.assertEqual(set(), {i.record["sent_id"] for i in real} & {i.record["sent_id"] for i in controls})

    def test_the_per_class_top_up_reaches_eight_rows_per_class_and_the_total_reaches_the_minimum(self):
        # Arrange: a population large enough that the random 60 cannot cover every small class
        rows, number = [], 0
        for name, size in (("big", 400), ("s1", 9), ("s2", 8), ("s3", 3)):
            for _ in range(size):
                number += 1
                rows.append(record(name, number))
        # Act
        items = adjudicate.build_sample(rows, seed=7, n_random=60, per_class=8, min_total=150, n_controls=8)
        counts = collections.Counter(i.record["class"] for i in items if i.kind == "real")
        # Assert
        self.assertGreaterEqual(counts["s1"], 8)
        self.assertGreaterEqual(counts["s2"], 8)
        self.assertEqual(3, counts["s3"])
        self.assertEqual(150, sum(counts.values()))
        self.assertEqual(60, sum(i.random60 for i in items))

    def test_a_control_item_shows_the_correction_swapped(self):
        # Arrange
        row = record("a", 1)
        control = adjudicate.SampleItem(row, "control", False)
        real = adjudicate.SampleItem(row, "real", False)
        # Act and Assert
        self.assertEqual(("NOUN (unchecked)", "VERB (unchecked)"), adjudicate.shown_values(real))
        self.assertEqual(("VERB (unchecked)", "NOUN (unchecked)"), adjudicate.shown_values(control))

    def test_a_second_round_draws_only_the_asked_classes_and_not_the_rows_already_reviewed(self):
        # Arrange
        rows = self.records()
        done = {(r["sent_id"], r["token_index"]) for r in rows[:10]}
        # Act
        items = adjudicate.build_sample(rows, seed=2, n_random=0, per_class=12, min_total=0, n_controls=0, only={"a"}, exclude=done)
        # Assert
        self.assertEqual({"a"}, {i.record["class"] for i in items})
        self.assertEqual(12, len(items))
        self.assertEqual(set(), {(i.record["sent_id"], i.record["token_index"]) for i in items} & done)

    def test_verdicts_are_parsed_and_a_repeated_item_is_an_error(self):
        # Arrange
        text = "R001\tcorrect\tPrenominal Modifiers, test 1\nnoise\nR002\tIncorrect\t\nR003\tunclear\n`R004\tcorrect\tx`\n"
        # Act
        found = adjudicate.parse_verdicts(text)
        # Assert
        self.assertEqual([("R001", "correct"), ("R002", "incorrect"), ("R003", "unclear"), ("R004", "correct")], [(v.item, v.verdict) for v in found])
        with self.assertRaises(ValueError):
            adjudicate.parse_verdicts("R001\tcorrect\nR001\tincorrect\n")

    def counter(self, correct, incorrect, unclear, population=100):
        c = collections.Counter(correct=correct, incorrect=incorrect, unclear=unclear, population=population)
        c["total"] = correct + incorrect + unclear
        return c

    def test_the_gate_drops_a_class_below_ninety_percent_with_two_incorrect_or_over_thirty_percent_unclear(self):
        # Arrange
        stats = {"ok": self.counter(8, 0, 0), "nine of ten": self.counter(9, 1, 0), "two bad": self.counter(6, 2, 0),
                 "one bad of eight": self.counter(7, 1, 0), "high with two bad": self.counter(18, 2, 0),
                 "unclear": self.counter(5, 0, 3), "unclear edge": self.counter(7, 0, 3), "all unclear": self.counter(0, 0, 2)}
        # Act
        decisions = adjudicate.gate(stats)
        # Assert
        self.assertEqual({"ok": "keep", "nine of ten": "keep", "two bad": "drop", "one bad of eight": "second-round",
                          "high with two bad": "keep", "unclear": "drop", "unclear edge": "keep", "all unclear": "drop"}, decisions)

    def test_the_review_lines_report_precision_per_class_the_weighted_figure_and_lenient_controls(self):
        # Arrange
        keys = []
        for number in range(1, 7):
            keys.append({"item": "R%03d" % number, "kind": "real", "random60": "yes" if number <= 3 else "no", "class": "a" if number <= 4 else "b",
                         "class_total": "40" if number <= 4 else "10"})
        for number in (7, 8, 9):
            keys.append({"item": "R%03d" % number, "kind": "control", "random60": "no", "class": "a", "class_total": "40"})
        verdicts = [adjudicate.Verdict("R001", "correct", ""), adjudicate.Verdict("R002", "correct", ""), adjudicate.Verdict("R003", "incorrect", ""),
                    adjudicate.Verdict("R004", "correct", ""), adjudicate.Verdict("R005", "incorrect", ""), adjudicate.Verdict("R006", "incorrect", ""),
                    adjudicate.Verdict("R007", "incorrect", ""), adjudicate.Verdict("R008", "correct", ""), adjudicate.Verdict("R009", "correct", "")]
        # Act
        per_class, controls, random60 = adjudicate.class_statistics(keys, verdicts)
        text = "\n".join(adjudicate.review_lines(per_class, controls, random60, adjudicate.gate(per_class)))
        # Assert
        self.assertEqual(4, per_class["a"]["correct"] + per_class["a"]["incorrect"] + per_class["a"]["unclear"])
        self.assertEqual(("2", "1"), (str(controls["correct"]), str(controls["incorrect"])))
        self.assertIn("TOO LENIENT", text)
        self.assertIn("random subset: 3 reviewed, precision 66.7%", text)
        self.assertIn("| b | 2 | 0 | 2 | 0 | 0.0% |", text)
        self.assertIn("weighted by class size", text)


if __name__ == "__main__":
    unittest.main()

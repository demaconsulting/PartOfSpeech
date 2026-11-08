"""Tests of the hard tranche of shapes_dev.py (`--tranche hard`) and of the checks that know it, on a miniature data and work folder.

Run: python -m unittest discover -s tools/data/tests -p "test_shapes_dev_hard.py"   (developer-run; no model, no network)
"""
import contextlib
import hashlib
import io
import os
import re
import shutil
import sys
import unittest
from unittest import mock
sys.path.insert(0, os.path.dirname(os.path.dirname(os.path.abspath(__file__))))
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import adjudicate
import check_leakage
import check_licenses
import common
import leakage
import shapes_dev as sd
import test_shapes_dev as base

PRODUCTION_HMM_SHA256 = sd.HARD_HMM_SHA256        # read at import, before any test patches the pin
NEAR_LONG = "Workers must check the gauge before they open the main supply valve and then close the relief line ."
NEAR_CHANGED = "Workers must check the gauge before they open the main supply valve and then close the relief pipe ."

FRAME_DOC = [
    "The pressure switch opens when the tank is full .",      # compound head `switch` (also in the standard tranche: exact)
    "Flow stops at the valve .",                              # subject-initial `Flow`
    "Check the pressure valve before use .",                  # imperative `Check`
    "You must check the oil level daily .",                   # modal `check`
    "The oil pump opens the valve .",                         # compound head `pump`
    "The light switch opens the valve .",                     # `light` is an adjective too: no strict compound
]
MODAL_DOC = [
    "They must check the gauge .",       # q 0.30
    "They must stop the pump .",         # q 0.05: below the band
    "They must test the seal .",         # q 0.10: the lower edge, inside
    "They must mix the oil .",           # q 0.969: inside
    "They must check the pump .",        # q 0.20
    "They must stop the flow .",         # q 0.50
    "They must leak ." ,                 # q 0.97: the upper edge, outside
    NEAR_CHANGED,                        # a near duplicate of a standard sentence of this document
]
CONTROL_DOC = ["The check was quick at the valve .", "The test was long at the tank .", "The stop was loud at the pump ."]
Q_OF = {                                   # (sentence, token index) -> (p_noun, p_verb) of the Basic model; everything else is 0.5 / 0.5 for a
    (FRAME_DOC[0], 2): (0.60, 0.40),       # noun-verb word of the fixture lexicon and 0 for the rest
    (FRAME_DOC[1], 0): (0.20, 0.80),
    (FRAME_DOC[2], 0): (0.40, 0.40),
    (FRAME_DOC[3], 2): (0.0, 0.99),
    (MODAL_DOC[0], 2): (0.0, 0.30),
    (MODAL_DOC[1], 2): (0.0, 0.05),
    (MODAL_DOC[2], 2): (0.0, 0.10),
    (MODAL_DOC[3], 2): (0.0, 0.969),
    (MODAL_DOC[4], 2): (0.0, 0.20),
    (MODAL_DOC[5], 2): (0.0, 0.50),
    (MODAL_DOC[6], 2): (0.0, 0.97),
}


class HardFolder(base.MiniFolder):
    """The miniature folder plus a committed standard tranche, hmm.tsv and two documents for the hard tranche."""

    EXTRA_TAGS = {"they": "PRON", "workers": "NOUN", "gauge": "NOUN", "quick": "ADJ", "loud": "ADJ", "long": "ADJ", "before": "ADP",
                  "supply": "NOUN", "main": "ADJ", "then": "ADV", "close": "VERB", "line": "NOUN", "pipe": "NOUN", "open": "VERB",
                  "you": "PRON", "at": "ADP", "when": "SCONJ", "use": "NOUN", "oil": "NOUN"}

    def write_data(self):
        super().write_data()
        words = {w for text in FRAME_DOC + MODAL_DOC + CONTROL_DOC + [NEAR_LONG] for w in re.findall(r"[a-z]+", text.lower())}
        extra = {w: self.EXTRA_TAGS.get(w, "NOUN") for w in sorted(words - set(base.LEXICON))}
        path = os.path.join(self.data, "lexicon", "lexicon.tsv")
        common.write_text(path, common.read_lines(path) + ["%s\t%s" % item for item in sorted(extra.items())])

    def setUp(self):
        super().setUp()
        pin = mock.patch.object(sd, "HARD_HMM_SHA256", "0" * 64)       # the fixture's own hash is injected by pin_hmm(); the production pin is untouched
        pin.start()
        self.addCleanup(pin.stop)
        self.standard = [("doc-a", FRAME_DOC[0]), ("doc-b", NEAR_LONG)]
        extra = [["# shape = n-compound-head", "# role = target", "# target = 2", "# lemma = switch", "# origin = real"] for _ in self.standard]
        base.corpus_file(os.path.join(self.data, "eval", "shapes-dev", "dev.tsv"), self.standard, extra)
        self.write_draft(["doc-a", "doc-b", "doc-c"])
        self.write_text_doc("doc-a", FRAME_DOC)
        self.write_text_doc("doc-b", MODAL_DOC + CONTROL_DOC)
        self.write_text_doc("doc-c", [NEAR_CHANGED])
        self.texts = FRAME_DOC + MODAL_DOC + CONTROL_DOC
        self.write_tokens(self.texts)
        self.write_hmm(self.texts)

    def write_tokens(self, texts):
        common.write_text(os.path.join(self.root, "tokens.tsv"),
                          ["text\tsentences\ttokens"] + ["%s\t1\t%s" % (t, " ".join(base.simple_tokens(t))) for t in texts])

    def mass(self, text, index, token):
        found = Q_OF.get((text, index))
        if found:
            return found
        return (0.5, 0.5) if base.LEXICON.get(token.lower()) == "NOUN VERB" else (0.0, 0.0)

    def write_hmm(self, texts, sentences=None, meta=True, commit=None, dll=None, input_sha=None):
        """hmm.tsv of the fixture; a header value left None is the pre-registered pin / the SHA-256 of the current tokens.tsv."""
        lines = []
        if meta:
            if input_sha is None:
                with open(os.path.join(self.root, "tokens.tsv"), "rb") as handle:
                    input_sha = hashlib.sha256(handle.read()).hexdigest()
            lines = [sd.HMM_MAGIC + ": fixture", "# wordsense_commit = " + (commit or sd.HARD_WORDSENSE_COMMIT),
                     "# wordsense_dll_sha256 = " + (dll or sd.HARD_WORDSENSE_DLL_SHA256), "# input_sha256 = " + input_sha,
                     "# sentences = %d" % len(sentences if sentences is not None else texts)]
        for text in (sentences if sentences is not None else texts):
            cells = []
            for i, token in enumerate(base.simple_tokens(text)):
                noun, verb = self.mass(text, i, token)
                cells.append("%s|Noun|%.3f|%.3f" % (token, noun, verb))
            lines.append("\t".join(cells))
        common.write_text(os.path.join(self.root, sd.HMM_FILE), lines)
        self.pin_hmm()

    def pin_hmm(self):
        """Make the current bytes of the fixture's hmm.tsv the pinned ones (the test constant; the production pin is not used by the fixture)."""
        with open(os.path.join(self.root, sd.HMM_FILE), "rb") as handle:
            sd.HARD_HMM_SHA256 = hashlib.sha256(handle.read()).hexdigest()
    def rule(self, budgets=None):
        return sd.HardRule(sd.read_hmm(self.work), sd.StandardTranche(self.data), budgets)

    def collect(self, hard=None, **options):
        options = dict(dict(include_ud=False, tokens_override=base.MiniFolder.tokens_for(self, self.texts), clean=False), **options)
        return sd.collect_candidates(self.work, self.data, hard=hard or self.rule(), **options)

    def targets(self, rows, shape):
        return [r for r in rows if r["shape"] == shape and r["role"] == "target"]


class HmmFileTests(HardFolder):
    def test_the_header_and_the_cells_are_read_and_a_token_may_contain_a_pipe(self):
        table = sd.read_hmm(self.work)
        self.assertEqual(sd.HARD_WORDSENSE_COMMIT, table.meta["wordsense_commit"])
        tokens = base.simple_tokens(FRAME_DOC[0])
        self.assertAlmostEqual(0.6, table.q(tokens, 2, "Noun"))
        self.assertAlmostEqual(0.4, table.q(tokens, 2, "Verb"))
        self.assertEqual(("a|b", "Noun", 0.25, 0.5), sd.parse_hmm_cell("a|b|Noun|0.250|0.500"))
        for bad in ("a|Noun|0.1", "|Noun|0.1|0.2", "a||0.1|0.2", "a|Noun|x|0.2"):
            with self.assertRaises(ValueError):
                sd.parse_hmm_cell(bad)

    def test_the_bytes_of_hmm_are_pinned_and_an_edited_cell_is_refused(self):
        self.assertEqual("3014efd2044702c82c02c3557bb47f56ed985537f0d3154393455b110b176a8c", PRODUCTION_HMM_SHA256)
        readme = os.path.join(os.path.dirname(os.path.abspath(__file__)), "..", "..", "..", "data", "eval", "shapes-dev", "README.md")
        with open(readme, encoding="utf-8") as handle:
            self.assertIn(PRODUCTION_HMM_SHA256, handle.read())                              # the pin is the pre-registered value
        sd.read_hmm(self.work)                                                              # the fixture's own hash (the test constant): accepted
        path = os.path.join(self.root, sd.HMM_FILE)
        with open(path, "rb") as handle:
            original = handle.read()
        edited = original.replace(b"|Noun|0.600|0.400", b"|Noun|0.100|0.900", 1)             # an edited pNoun / pVerb cell, header untouched
        self.assertNotEqual(original, edited)
        with open(path, "wb") as handle:
            handle.write(edited)
        with self.assertRaises(ValueError) as caught:
            sd.read_hmm(self.work)
        self.assertIn("SHA-256", str(caught.exception))
        self.assertIn(hashlib.sha256(original).hexdigest(), str(caught.exception))
        with self.assertRaises(ValueError):
            self.collect()
        self.assertEqual(1, self.run_candidates_cli())
        sd.read_hmm(self.work, expected_sha256=hashlib.sha256(edited).hexdigest())          # the parameter is the injection point of a fixture
        with open(path, "wb") as handle:
            handle.write(original)
        sd.read_hmm(self.work)

    def test_the_fixture_is_refused_by_the_production_pin(self):
        sd.HARD_HMM_SHA256 = PRODUCTION_HMM_SHA256                                           # what a real run uses
        with self.assertRaises(ValueError) as caught:
            sd.read_hmm(self.work)
        self.assertIn(PRODUCTION_HMM_SHA256, str(caught.exception))

    def run_candidates_cli(self):
        err = io.StringIO()
        with contextlib.redirect_stderr(err):
            try:
                return sd.main(["--work", self.root, "--data-dir", self.data, "--tranche", "hard", "candidates", "--no-ud"])
            except ValueError:
                return 1

    def test_a_missing_file_a_wrong_header_and_a_wrong_count_are_errors(self):
        path = os.path.join(self.root, sd.HMM_FILE)
        lines = common.read_lines(path)
        lines[4] = "# sentences = %d" % (len(self.texts) + 5)
        common.write_text(path, lines)
        self.pin_hmm()
        with self.assertRaises(ValueError) as caught:
            sd.read_hmm(self.work)
        self.assertIn("sentences", str(caught.exception))
        self.write_hmm(self.texts, meta=False)
        with self.assertRaises(ValueError):
            sd.read_hmm(self.work)
        os.remove(path)
        with self.assertRaises(ValueError) as caught:
            sd.read_hmm(self.work)
        self.assertIn("hmm-tags", str(caught.exception))

    def test_a_sentence_without_tags_is_an_error_and_never_a_default(self):
        with self.assertRaises(ValueError) as caught:
            sd.read_hmm(self.work).mass(["never", "tagged", "here"])
        self.assertIn("no tags", str(caught.exception))
        self.write_hmm(self.texts, sentences=self.texts[:1] + self.texts[2:])      # a file with fewer sentences than tokens.tsv: refused
        with self.assertRaises(ValueError) as caught:
            self.collect()
        self.assertIn("sentences", str(caught.exception))

    def test_a_sentence_that_starts_with_a_hash_token_is_data_not_a_header(self):
        self.write_tokens(self.texts + ["# 2 ."])
        self.write_hmm(self.texts + ["# 2 ."])
        table = sd.read_hmm(self.work)
        self.assertEqual(0.0, table.q(["#", "2", "."], 0, "Noun"))

    def test_the_pinned_classifier_is_required_and_there_is_no_override(self):
        for options, name in (({"commit": "c" * 40}, "wordsense_commit"), ({"dll": "d" * 64}, "wordsense_dll_sha256"),
                              ({"commit": sd.HARD_WORDSENSE_COMMIT[:-1] + "0"}, "wordsense_commit")):
            self.write_hmm(self.texts, **options)
            with self.assertRaises(ValueError) as caught:
                sd.read_hmm(self.work)
            self.assertIn(name, str(caught.exception))
            self.assertIn("pre-registered", str(caught.exception))
            with self.assertRaises(ValueError):
                self.collect()
        self.write_hmm(self.texts)
        self.assertEqual(sd.HARD_WORDSENSE_DLL_SHA256, sd.read_hmm(self.work).meta["wordsense_dll_sha256"])

    def test_the_input_hash_must_be_that_of_the_current_tokens_file(self):
        self.write_hmm(self.texts, input_sha="0" * 64)
        with self.assertRaises(ValueError) as caught:
            sd.read_hmm(self.work)
        self.assertIn("input_sha256", str(caught.exception))
        self.write_hmm(self.texts)
        sd.read_hmm(self.work)                                                      # the matching hash is accepted
        self.write_tokens(self.texts + ["Extra words here ."])                       # tokens.tsv grew after hmm-tags ran: tag again
        with self.assertRaises(ValueError) as caught:
            sd.read_hmm(self.work)
        self.assertIn("input_sha256", str(caught.exception))
        with self.assertRaises(ValueError):
            self.collect()
        os.remove(os.path.join(self.root, "tokens.tsv"))
        with self.assertRaises(ValueError) as caught:
            sd.read_hmm(self.work)
        self.assertIn("tokens.tsv", str(caught.exception))

    def test_a_header_without_the_input_hash_is_refused(self):
        path = os.path.join(self.root, sd.HMM_FILE)
        common.write_text(path, [line for line in common.read_lines(path) if not line.startswith("# input_sha256")])
        self.pin_hmm()
        with self.assertRaises(ValueError) as caught:
            sd.read_hmm(self.work)
        self.assertIn("input_sha256", str(caught.exception))

    def test_the_sentence_count_is_that_of_the_single_sentence_rows_of_tokens(self):
        header = "text\tsentences\ttokens\n"
        raw = (header + "a b\t1\ta b\nc d. E f.\t2\tc d . E f .\nempty\t1\t\ng\t1\tg\n").encode("utf-8")
        self.assertEqual(2, sd.tagged_sentence_count(raw))
        with self.assertRaises(ValueError):
            sd.tagged_sentence_count(b"a\tb\n")


class SelectionRuleTests(HardFolder):
    def test_the_band_edges_are_inclusive_below_and_exclusive_above(self):
        rows, _ = self.collect(lemma_cap=9)
        modal = {r["text"]: r["q"] for r in self.targets(rows, "v-after-modal")}
        self.assertNotIn(MODAL_DOC[1], modal)            # q 0.05
        self.assertNotIn(MODAL_DOC[6], modal)            # q 0.970
        self.assertNotIn(FRAME_DOC[3], modal)            # q 0.99
        self.assertEqual("0.100", modal[MODAL_DOC[2]])
        self.assertEqual("0.969", modal[MODAL_DOC[3]])
        self.assertTrue(sd.in_band(0.10) and sd.in_band(0.969) and not sd.in_band(0.0999) and not sd.in_band(0.97))

    def test_targets_are_written_hardest_first_with_the_lemma_cap_and_the_budget(self):
        rows, _ = self.collect(lemma_cap=1, hard=self.rule({"v-after-modal": 3}))
        modal = self.targets(rows, "v-after-modal")
        self.assertEqual(3, len(modal))
        self.assertEqual({"0.100", "0.200", "0.500"}, {r["q"] for r in modal})          # test, the second check (the first is over the lemma cap), stop
        self.assertEqual({"test", "check", "stop"}, {r["lemma"] for r in modal})
        every = self.targets(self.collect(lemma_cap=9, hard=self.rule({"v-after-modal": None}))[0], "v-after-modal")
        self.assertEqual(6, len(every))
        self.assertEqual({"0.100", "0.200", "0.300", "0.500", "0.969"}, {r["q"] for r in every})

    def test_ties_are_broken_by_a_seeded_rank_and_the_run_is_deterministic(self):
        first, _ = self.collect(lemma_cap=9)
        second, _ = self.collect(lemma_cap=9)
        self.assertEqual([(r["cand_id"], r["text"], r["q"]) for r in first], [(r["cand_id"], r["text"], r["q"]) for r in second])

    def test_every_hard_target_carries_q_and_the_default_shapes_are_the_four_hard_shapes(self):
        rows, _ = self.collect(lemma_cap=9)
        targets = [r for r in rows if r["role"] == "target"]
        self.assertTrue(targets and all(re.fullmatch(r"\d\.\d{3}", r["q"]) for r in targets))
        self.assertTrue(all(sd.BAND_MIN <= float(r["q"]) < sd.BAND_MAX for r in targets))
        self.assertEqual(set(), {r["shape"] for r in rows} - set(sd.HARD_SHAPES))
        self.assertNotIn("n-measure", {r["shape"] for r in rows})

    def test_the_compound_frame_is_strict_in_the_hard_tranche(self):
        self.assertEqual(["The oil pump opens the valve ."],
                         [r["text"] for r in self.targets(self.collect(lemma_cap=9)[0], "n-compound-head")])
        loose, _ = sd.collect_candidates(self.work, self.data, include_ud=False, tokens_override=base.MiniFolder.tokens_for(self, self.texts),
                                         clean=False, only_shapes=("n-compound-head",), hard=None)
        self.assertIn("The light switch opens the valve .", {r["text"] for r in loose})          # the standard profile without --compound-strict

    def test_controls_have_no_q_no_band_and_only_lemmas_of_the_chosen_targets_of_the_shape(self):
        rows, _ = self.collect(lemma_cap=9, hard=self.rule({"v-after-modal": None}))
        controls = [r for r in rows if r["role"] == "control" and r["shape"] == "v-after-modal"]
        self.assertTrue(controls)
        self.assertTrue(all(r["q"] == "" for r in controls))
        chosen = {r["lemma"] for r in self.targets(rows, "v-after-modal")}
        self.assertEqual(set(), {r["lemma"] for r in controls} - chosen)
        self.assertEqual({"check", "test", "stop"}, {r["lemma"] for r in controls})

    def test_the_report_lists_the_provenance_and_the_band_table(self):
        _, report = self.collect(lemma_cap=9)
        text = "\n".join(report)
        self.assertIn("WordSense commit " + sd.HARD_WORDSENSE_COMMIT, text)
        self.assertIn("DLL sha256 " + sd.HARD_WORDSENSE_DLL_SHA256, text)
        self.assertRegex(text, r"\| v-after-modal \| \d+ \| \d+ \| 300 \| ")
        self.assertRegex(text, r"\| n-subject-initial \| \d+ \| \d+ \| all \| ")

    def test_authored_sentences_are_ignored_with_a_problem_line(self):
        self.write_authored("v-after-modal", [("check", "target", "Workers must check the gauge twice .")])
        rows, report = self.collect(lemma_cap=9)
        self.assertEqual({"real"}, {r["origin"] for r in rows})
        self.assertTrue(any("no authored sentences" in line for line in report))


class StandardTrancheExclusionTests(HardFolder):
    def test_an_exact_standard_sentence_and_a_same_document_near_duplicate_are_dropped_with_their_reasons(self):
        rows, report = self.collect(lemma_cap=9, hard=self.rule({"v-after-modal": None}))
        texts = {r["text"] for r in rows}
        self.assertNotIn(FRAME_DOC[0], texts)                         # equals the standard sentence (same document)
        self.assertNotIn(NEAR_CHANGED, {r["text"] for r in rows if r["source_id"] == "doc-b"})
        self.assertIn(NEAR_CHANGED, {r["text"] for r in rows if r["source_id"] == "doc-c"})   # another document: only an exact match counts
        text = "\n".join(report)
        self.assertIn(sd.EQUALS_STANDARD + " 1", text)
        self.assertIn(sd.NEAR_STANDARD + " 1", text)

    def test_the_exact_key_ignores_case_spacing_and_punctuation_and_the_near_rule_needs_ten_words(self):
        standard = sd.StandardTranche(self.data)
        self.assertEqual(sd.EQUALS_STANDARD, standard.reason("doc-z", "the pressure SWITCH opens when the tank is full!"))
        self.assertEqual(sd.NEAR_STANDARD, standard.reason("doc-b", NEAR_CHANGED))
        self.assertIsNone(standard.reason("doc-a", NEAR_CHANGED))
        self.assertIsNone(standard.reason("doc-b", "Workers must check the gauge before they open the ."))                 # 9 words only
        self.assertIsNone(standard.reason("doc-b", "A brand new sentence that shares no wording with any standard sentence at all ."))

    def test_the_standard_tranche_is_required(self):
        os.remove(os.path.join(self.data, "eval", "shapes-dev", "dev.tsv"))
        with self.assertRaises(ValueError):
            sd.StandardTranche(self.data)


class HardPipelineTests(HardFolder):
    """candidates, export, decide, sample, build and report through the command line, with fake labelers."""

    def setUp(self):
        super().setUp()
        self.write_text_doc("doc-b", [t for t in MODAL_DOC if t != NEAR_CHANGED] + CONTROL_DOC)
        self.options = ("--tranche", "hard")

    def hard_cli(self, *args):
        return self.cli(*(self.options + args))

    def label(self, rule=None):
        """Fake labelers (both alike): `rule(model, token, previous token)`; the default tags a noun-verb word after `the` as NOUN."""
        rule = rule or self.contextual_rule
        for model in adjudicate.MODELS:
            for name in sorted(adjudicate.read_status(self.work)):
                out, previous, last = [], "", None
                for sid, index, token in adjudicate.read_batch(self.work.batch_file(name)):
                    previous = previous if sid == last else ""
                    word = adjudicate.unescape_cell(token)
                    out.append((sid, index, token, rule(model, word, previous), "high", "-"))
                    previous, last = word, sid
                adjudicate.write_rows(os.path.join(self.work.out(model), name + ".tsv"), adjudicate.OUTPUT_HEADER, out)

    @staticmethod
    def contextual_rule(model, token, previous):
        if previous.lower() == "the" and token.lower() in ("check", "test", "stop", "flow", "level"):
            return "NOUN"
        return base.PipelineTests.basic_rule(model, "", 0, token)

    def prepare(self):
        code, output = self.hard_cli("candidates", "--no-ud", "--lemma-cap", "9", "--budget", "v-after-modal=all")
        self.assertEqual(0, code, output)
        self.assertIn("| v-after-modal |", output)
        self.assertEqual(0, self.hard_cli("export")[0])
        self.assertEqual(0, self.hard_cli("check-batches")[0])
        self.label()
        self.assertEqual(0, self.hard_cli("validate-output", "--all")[0])
        self.assertEqual(0, self.hard_cli("decide")[0])

    def test_the_hard_profile_refuses_a_missing_or_empty_committed_record_and_accepts_a_header_only_one(self):
        record = os.path.join(self.data, "eval", "shapes-dev", "excluded-sources.tsv")
        out = os.path.join(self.temp, "out")
        self.prepare()                                                                       # header-only record: every command runs
        self.assertEqual(0, self.hard_cli("build", "--out-dir", out, "--allow-short")[0])
        shutil.rmtree(out)
        for content in (None, "", "doc-b\thttps://example.gov/doc-b\tnot public domain\t2026-11-06\n"):    # missing, zero-byte, headerless
            if content is None:
                os.remove(record)
            else:
                with open(record, "w", encoding="utf-8", newline="") as handle:
                    handle.write(content)
            for command in ("candidates", "decide", "build"):
                err = io.StringIO()
                with contextlib.redirect_stderr(err):
                    code, _ = self.hard_cli(command, *(("--allow-short",) if command == "build" else ()), *(("--out-dir", out) if command == "build" else ()))
                self.assertEqual(1, code, (command, content))
                self.assertIn("%s refused" % command, err.getvalue())
            self.assertFalse(os.path.exists(os.path.join(out, "hard.tsv")))
        self.assertFalse(os.path.exists(os.path.join(out, "excluded-sources.tsv")))                  # the hard build never writes the record

    def test_candidates_carry_q_and_the_batches_never_show_it(self):
        self.assertEqual(0, self.hard_cli("candidates", "--no-ud", "--lemma-cap", "9")[0])
        columns, rows = adjudicate.read_records(os.path.join(self.root, "candidates.tsv"))
        self.assertEqual(sd.HARD_CANDIDATE_COLUMNS, tuple(columns))
        self.assertTrue(any(r["q"] for r in rows) and any(r["role"] == "control" and r["q"] == "" for r in rows))
        self.assertEqual(0, self.hard_cli("export")[0])
        batches = os.path.join(self.root, "batches")
        for name in os.listdir(batches):
            if name.endswith(".tsv") and adjudicate.BATCH_RE.match(name[:-4]):
                with open(os.path.join(batches, name), encoding="utf-8") as handle:
                    self.assertTrue(all(len(line.split("\t")) == 3 for line in handle.read().split("\n") if line))
        self.assertEqual(0, self.hard_cli("check-batches")[0])

    def test_decided_items_keep_q_and_the_floor_gate_refuses_a_short_build_unless_allowed(self):
        self.prepare()
        columns, items = adjudicate.read_records(os.path.join(self.root, "private", "decided.tsv"))
        self.assertEqual(sd.HARD_ITEM_COLUMNS, tuple(columns))
        out = os.path.join(self.temp, "out")
        code, output = self.hard_cli("build", "--out-dir", out)
        self.assertEqual(1, code)
        self.assertFalse(os.path.exists(os.path.join(out, "hard.tsv")))
        code, output = self.hard_cli("build", "--out-dir", out, "--allow-short")
        self.assertEqual(0, code, output)
        self.assertIn("WARNING (allowed by --allow-short): v-after-modal: ", output)
        self.assertTrue(os.path.exists(os.path.join(out, "hard.tsv")) and os.path.exists(os.path.join(out, "hard-sources.tsv")))
        self.assertFalse(os.path.exists(os.path.join(out, "dev.tsv")))
        self.assertFalse(os.path.exists(os.path.join(out, sd.EXCLUDED_OUTPUT_FILE)))        # the hard build never writes the exclusion record

    def test_floors_and_caps_can_be_set_and_a_built_file_has_the_hard_header_ids_and_no_authored_items(self):
        self.prepare()
        out = os.path.join(self.temp, "out")
        floors = ["--shape-min", "n-compound-head=0", "--shape-min", "n-subject-initial=0", "--shape-min", "v-imperative-initial=0",
                  "--shape-min", "v-after-modal=2", "--control-min", "1", "--shape-max", "v-after-modal=3"]
        code, output = self.hard_cli("build", "--out-dir", out, *floors)
        self.assertEqual(0, code, output)
        header, sentences = common.read_corpus(os.path.join(out, "hard.tsv"))
        self.assertTrue(any(line.startswith("# corpus") and "hard tranche" in line and "dev split" in line for line in header))
        self.assertTrue(all(re.fullmatch(r"shapes-dev-hard-\d{4}", s["sent_id"]) for s in sentences))
        self.assertEqual({"real"}, {s["comments"]["origin"] for s in sentences})
        modal = [s for s in sentences if s["comments"]["shape"] == "v-after-modal" and s["comments"]["role"] == "target"]
        self.assertEqual(3, len(modal))                                            # the cap
        self.assertEqual(["0.200", "0.300", "0.500"],
                         sorted(next(c["q"] for c in sd.read_all_decided(self.work)[0] if c["sent_key"] == sd.sent_key_of(s["text"])
                                     and c["role"] == "target" and c["shape"] == "v-after-modal") for s in modal))
        self.assertTrue(all(sd.sentence_problems(s, ["# %s = %s" % (k, v) for k, v in s["comments"].items()
                                                     if k in ("shape", "role", "target", "lemma", "origin")]) == [] for s in sentences))
        _, sources = adjudicate.read_records(os.path.join(out, "hard-sources.tsv"))
        self.assertEqual({s["source_id"] for s in sentences}, {r["source_id"] for r in sources})
        self.assertEqual(0, self.hard_cli("build", "--out-dir", out, *floors, "--shape-max", "v-after-modal=1", "--shape-min", "v-after-modal=1")[0])
        code, output = self.hard_cli("build", "--out-dir", out, *floors, "--shape-min", "v-after-modal=6")
        self.assertEqual(1, code)

    def test_the_hard_build_drops_an_excluded_source_of_the_committed_record_and_never_touches_the_record(self):
        self.prepare()
        record = os.path.join(self.data, "eval", "shapes-dev", "excluded-sources.tsv")
        common.write_text(record, ["\t".join(common.EXCLUDED_SOURCES_COLUMNS), "doc-b\thttps://example.gov/doc-b\tnot public domain\t2026-11-06"])
        before = common.read_lines(record)
        out = os.path.join(self.temp, "out")
        self.assertFalse(os.path.exists(os.path.join(self.root, sd.EXCLUDED_SOURCES_FILE)))        # the work file is absent
        code, output = self.hard_cli("build", "--out-dir", out, "--allow-short")
        self.assertEqual(0, code, output)
        _, sentences = common.read_corpus(os.path.join(out, "hard.tsv"))
        self.assertFalse(any(s["source_id"] == "doc-b" for s in sentences))
        self.assertTrue(all(i["outcome"] == "drop" and i["reason"] == sd.EXCLUDED_REASON
                            for i in sd.read_all_decided(self.work, self.data)[0] if i["source_id"] == "doc-b"))
        self.assertEqual(before, common.read_lines(record))

    def write_work_exclusions(self, rows):
        common.write_text(os.path.join(self.root, sd.EXCLUDED_SOURCES_FILE), ["source_id\treason\tdecided"] + ["\t".join(r) for r in rows])

    def test_the_hard_build_needs_no_draft_row_or_url_for_an_excluded_source_that_is_not_used(self):
        self.prepare()
        record = os.path.join(self.data, "eval", "shapes-dev", "excluded-sources.tsv")
        common.write_text(record, ["\t".join(common.EXCLUDED_SOURCES_COLUMNS), "nist-gone\thttps://example.gov/nist-gone\tnot public domain\t2026-11-06"])
        self.write_work_exclusions([("nist-gone", "not public domain", "2026-11-06"), ("nist-other", "not public domain", "2026-11-06")])
        before = common.read_lines(record)
        out = os.path.join(self.temp, "out")
        code, output = self.hard_cli("build", "--out-dir", out, "--allow-short")
        self.assertEqual(0, code, output)
        self.assertTrue(os.path.exists(os.path.join(out, "hard.tsv")))
        self.assertEqual(before, common.read_lines(record))
        self.assertEqual(0, self.hard_cli("report", "--out-dir", out)[0])

    def test_the_hard_build_refuses_one_id_with_two_urls_in_the_committed_and_the_work_table_even_when_it_is_not_selected(self):
        self.prepare()
        record = os.path.join(self.data, "eval", "shapes-dev", "excluded-sources.tsv")
        out = os.path.join(self.temp, "out")
        self.write_work_exclusions([("doc-c", "not public domain", "2026-11-06")])        # doc-c is in the draft (https://example.gov/doc-c) and has no item
        common.write_text(record, ["\t".join(common.EXCLUDED_SOURCES_COLUMNS), "doc-c\thttps://www.example.gov/doc-c/\tolder\t2026-11-01"])
        code, output = self.hard_cli("build", "--out-dir", out, "--allow-short")             # same id, same normalized URL: the union
        self.assertEqual(0, code, output)
        shutil.rmtree(out)
        common.write_text(record, ["\t".join(common.EXCLUDED_SOURCES_COLUMNS), "doc-c\thttps://example.gov/another-document\tolder\t2026-11-01"])
        err = io.StringIO()
        with contextlib.redirect_stderr(err):
            code, _ = self.hard_cli("build", "--out-dir", out, "--allow-short")
        self.assertEqual(1, code)
        for text in ("conflicting", "doc-c", "another-document"):
            self.assertIn(text, err.getvalue())
        self.assertFalse(os.path.exists(os.path.join(out, "hard.tsv")))                      # nothing is written
        self.write_work_exclusions([("doc-gone", "not public domain", "2026-11-06")])      # a work row without a draft URL: id only, no conflict
        common.write_text(record, ["\t".join(common.EXCLUDED_SOURCES_COLUMNS), "doc-gone\thttps://example.gov/doc-gone\tolder\t2026-11-01"])
        self.assertEqual(0, self.hard_cli("build", "--out-dir", out, "--allow-short")[0])

    def test_only_shapes_of_the_hard_tranche_must_be_hard_shapes(self):
        self.assertEqual(("v-after-modal",), sd.parse_only_shapes("v-after-modal", sd.HARD_SHAPES))
        self.assertEqual(("n-measure",), sd.parse_only_shapes("n-measure"))                  # a standard-tranche shape
        for bad in ("n-measure", "v-after-modal,n-measure", "no-such-shape"):
            with self.assertRaises(ValueError) as caught:
                sd.parse_only_shapes(bad, sd.HARD_SHAPES)
            self.assertIn("--only-shapes", str(caught.exception))
        err = io.StringIO()
        with contextlib.redirect_stderr(err):
            code, _ = self.hard_cli("candidates", "--no-ud", "--only-shapes", "n-measure")
        self.assertEqual(1, code)
        self.assertIn("candidates refused", err.getvalue())
        self.assertIn("--only-shapes", err.getvalue())
        self.assertFalse(os.path.exists(os.path.join(self.root, "candidates.tsv")))
        with self.assertRaises(ValueError):                                                  # the collector refuses it too
            self.collect(only_shapes=("n-measure",))
        self.assertEqual(0, self.cli("candidates", "--no-ud", "--only-shapes", "n-measure")[0])    # the standard tranche still takes it

    def test_the_hard_build_refuses_a_chosen_source_excluded_by_the_committed_url_or_the_work_rows_without_a_url(self):
        self.prepare()
        out = os.path.join(self.temp, "out")
        draft = sd.read_draft(self.work)
        built = [{"source_id": "doc-b"}]
        id_only = sd.Exclusions([("doc-b", "", "not public domain", "")], [], [], {"doc-b": "not public domain"})      # an id-only row of the work folder
        self.assertEqual(1, len(id_only.problems(draft, built)))
        self.assertEqual([], id_only.problems(draft, [{"source_id": "doc-a"}]))
        self.write_work_exclusions([("doc-b", "not public domain", "2026-11-06")])        # decided items of it are dropped, never built
        code, output = self.hard_cli("build", "--out-dir", out, "--allow-short")
        self.assertEqual(0, code, output)
        _, sentences = common.read_corpus(os.path.join(out, "hard.tsv"))
        self.assertFalse(any(s["source_id"] == "doc-b" for s in sentences))
        os.remove(os.path.join(self.root, sd.EXCLUDED_SOURCES_FILE))
        os.remove(os.path.join(out, "hard.tsv"))
        record = os.path.join(self.data, "eval", "shapes-dev", "excluded-sources.tsv")      # another id, the URL of the chosen document
        common.write_text(record, ["\t".join(common.EXCLUDED_SOURCES_COLUMNS), "other-id\thttps://www.example.gov/doc-b/\tr\t2026-11-06"])
        code, output = self.hard_cli("build", "--out-dir", out, "--allow-short")
        self.assertEqual(0, code, output)                                                   # the committed URL excludes it by the draft URL
        _, sentences = common.read_corpus(os.path.join(out, "hard.tsv"))
        self.assertFalse(any(s["source_id"] == "doc-b" for s in sentences))
        self.assertEqual(1, len(sd.load_exclusions(self.work, self.data).problems(draft, built)))   # the backstop

    def test_sample_takes_at_least_half_of_each_shape_from_low_q_and_report_has_the_band_table(self):
        self.prepare()
        out = os.path.join(self.temp, "out")
        floors = ["--shape-min", "n-compound-head=0", "--shape-min", "n-subject-initial=0", "--shape-min", "v-imperative-initial=0",
                  "--shape-min", "v-after-modal=0", "--control-min", "0"]
        self.assertEqual(0, self.hard_cli("build", "--out-dir", out, *floors)[0])
        code, output = self.hard_cli("sample", "--per-shape", "4", "--controls", "1")
        self.assertEqual(0, code, output)
        _, keys = adjudicate.read_records(os.path.join(self.root, "private", "review-sample-key.tsv"))
        decided = {i["cand_id"]: i for i in sd.read_all_decided(self.work)[0]}
        modal = [decided[k["cand_id"]] for k in keys if k["kind"] == "real" and k["shape"] == "v-after-modal"]
        self.assertEqual(4, len(modal))
        self.assertGreaterEqual(sum(1 for i in modal if float(i["q"]) < 0.5), 2)
        code, report = self.hard_cli("report", "--out-dir", out)
        self.assertEqual(0, code, report)
        self.assertIn("| shape | q band | labeled | kept by both labelers | built |", report)
        self.assertIn("| v-after-modal | [0.10, 0.30) |", report)
        self.assertIn("built targets with q < 0.5", report)

    def test_options_of_the_other_profile_are_refused(self):
        self.assertEqual(0, self.hard_cli("candidates", "--no-ud")[0])
        for args in (("candidates", "--shape-over", "n-measure=2"),):
            with self.assertRaises(ValueError):
                self.hard_cli(*args)
        with self.assertRaises(ValueError):
            self.cli("candidates", "--no-ud", "--budget", "v-after-modal=3")
        code, _ = self.hard_cli("build", "--per-shape", "5")
        self.assertEqual(1, code)
        code, _ = self.cli("build", "--shape-min", "v-after-modal=1")
        self.assertEqual(1, code)

    def test_budget_option_parsing(self):
        self.assertEqual({"v-after-modal": 5, "n-compound-head": None}, sd.parse_budget(["v-after-modal=5", "n-compound-head=all"]))
        for bad in ("v-after-modal", "n-measure=3", "v-after-modal=0", "v-after-modal=x", "v-after-modal=-1"):
            with self.assertRaises(ValueError):
                sd.parse_budget([bad])
        self.assertEqual(None, sd.HARD_BUDGETS["n-subject-initial"])
        self.assertEqual((400, 300), (sd.HARD_BUDGETS["n-compound-head"], sd.HARD_BUDGETS["v-after-modal"]))

    def test_a_round_takes_the_next_hardest_block_and_skips_the_candidates_of_the_root(self):
        code, output = self.hard_cli("candidates", "--no-ud", "--lemma-cap", "9", "--only-shapes", "v-after-modal", "--budget", "v-after-modal=2",
                                     "--no-controls")
        self.assertEqual(0, code, output)
        first = {r["text"] for r in sd.read_candidates(self.work)}
        code, output = self.hard_cli("candidates", "--no-ud", "--lemma-cap", "9", "--only-shapes", "v-after-modal", "--budget", "v-after-modal=2",
                                     "--no-controls", "--round", "h2")
        self.assertEqual(0, code, output)
        rows = sd.read_candidates(sd.RoundWork(self.root, "h2"))
        self.assertEqual(2, len(rows))
        self.assertEqual(set(), first & {r["text"] for r in rows})
        self.assertTrue(all(r["cand_id"].startswith("h2-c") for r in rows))
        self.assertTrue(min(float(r["q"]) for r in rows) >= max(float(r["q"]) for r in sd.read_candidates(self.work)))

    def test_a_controls_only_round_reads_the_lemmas_and_the_room_of_the_current_build(self):
        self.prepare()
        items, _ = sd.read_all_decided(self.work)
        targets, chosen = sd.select_items_hard(items)
        room = sd.build_control_room(sd.RoundWork(self.root, "h3"))
        self.assertEqual(set(sd.HARD_SHAPES), set(room))
        taken = {}
        for item in chosen:
            taken[(item["shape"], item["lemma"])] = taken.get((item["shape"], item["lemma"]), 0) + 1
        for shape in sd.HARD_SHAPES:
            self.assertEqual({i["lemma"] for i in targets if i["shape"] == shape}, set(room[shape]))
            for lemma, n in room[shape].items():
                self.assertEqual(max(0, sd.CONTROL_LEMMA_CAP - taken.get((shape, lemma), 0)), n)
        self.assertTrue(room["v-after-modal"])

    def test_the_controls_only_collector_writes_no_target_and_obeys_the_lemma_room(self):
        room = {"v-after-modal": {"check": 1, "test": 0, "stop": 2}}
        rows, report = self.collect(control_room=room, control_pool=10, only_shapes=sd.HARD_SHAPES)
        self.assertTrue(rows)
        self.assertEqual({"control"}, {r["role"] for r in rows})
        self.assertEqual({("v-after-modal", "check"), ("v-after-modal", "stop")}, {(r["shape"], r["lemma"]) for r in rows})
        self.assertTrue(all(r["q"] == "" for r in rows))
        self.assertIn("| v-after-modal | 0 | 0 | 0 | 0 | 0 | - | - | 0 | 2 |", "\n".join(report))
        rows, _ = self.collect(control_room=room, control_pool=1, only_shapes=sd.HARD_SHAPES)
        self.assertEqual(1, len(rows))                                                   # the pool per shape
        self.assertEqual([], self.collect(control_room={}, control_pool=10)[0])          # no lemma, no control

    def test_the_controls_only_round_repeats_no_earlier_candidate_and_adds_no_target(self):
        self.prepare()
        before = sd.read_all_decided(self.work)[0]
        code, output = self.hard_cli("candidates", "--no-ud", "--round", "h3", "--controls-only", "--control-pool", "5", "--lemma-cap", "9")
        self.assertEqual(0, code, output)
        rows = sd.read_candidates(sd.RoundWork(self.root, "h3"))
        self.assertEqual(set(), {r["role"] for r in rows} - {"control"})
        self.assertEqual(set(), {r["sent_key"] for r in rows} & {r["sent_key"] for r in sd.read_candidates(self.work)})
        self.assertTrue(all(r["cand_id"].startswith("h3-c") for r in rows))
        self.assertEqual(len(before), len(sd.read_all_decided(self.work)[0]))

    def test_a_url_exclusion_under_another_id_is_honored_by_every_hard_command(self):
        self.prepare()
        out = os.path.join(self.temp, "out")
        record = os.path.join(self.data, "eval", "shapes-dev", "excluded-sources.tsv")
        controls = ("candidates", "--no-ud", "--round", "h3", "--controls-only", "--control-pool", "5", "--lemma-cap", "9")
        self.assertEqual(0, self.hard_cli(*controls)[0])
        before = {i["cand_id"] for i in sd.read_all_decided(self.work, self.data)[0] if i["source_id"] == "doc-b"}
        self.assertTrue(before)
        common.write_text(record, ["\t".join(common.EXCLUDED_SOURCES_COLUMNS), "other-id\thttps://www.example.gov/doc-b/\tr\t2026-11-06"])
        code, output = self.hard_cli(*controls)                                                            # controls-only round
        self.assertEqual(0, code, output)
        self.assertNotIn("doc-b", {r["source_id"] for r in sd.read_candidates(sd.RoundWork(self.root, "h3"))})
        self.assertRegex(output, r"excluded-source [1-9]\d*")
        code, output = self.hard_cli("candidates", "--no-ud", "--lemma-cap", "9", "--only-shapes", "v-after-modal", "--budget", "v-after-modal=all",
                                     "--no-controls", "--round", "h2")                                      # a target round
        self.assertEqual(0, code, output)
        self.assertNotIn("doc-b", {r["source_id"] for r in sd.read_candidates(sd.RoundWork(self.root, "h2"))})
        self.assertRegex(output, r"excluded-source [1-9]\d*")
        mine = [i for i in sd.read_decided(self.work, self.data)[0] if i["source_id"] == "doc-b"]           # the stored `keep` is overridden
        self.assertEqual(before, {i["cand_id"] for i in mine})
        self.assertTrue(all((i["outcome"], i["reason"]) == ("drop", sd.EXCLUDED_REASON) for i in mine))
        self.assertEqual(0, self.hard_cli("decide")[0])
        mine = [i for i in sd.read_decided(self.work, self.data)[0] if i["source_id"] == "doc-b"]
        self.assertTrue(mine and all((i["outcome"], i["reason"]) == ("drop", sd.EXCLUDED_REASON) for i in mine))
        self.assertEqual(0, self.hard_cli("sample", "--per-shape", "4", "--controls", "1")[0])
        _, keys = adjudicate.read_records(os.path.join(self.root, "private", "review-sample-key.tsv"))
        self.assertFalse({k["cand_id"] for k in keys} & before)
        code, output = self.hard_cli("build", "--out-dir", out, "--allow-short")
        self.assertEqual(0, code, output)
        _, sentences = common.read_corpus(os.path.join(out, "hard.tsv"))
        self.assertTrue(sentences and "doc-b" not in {s["source_id"] for s in sentences})
        code, report = self.hard_cli("report", "--out-dir", out)
        self.assertEqual(0, code)
        self.assertIn("dropped: excluded source", report)
        code, output = self.hard_cli("candidates", "--no-ud", "--lemma-cap", "9", "--budget", "v-after-modal=all")      # the root
        self.assertEqual(0, code, output)
        self.assertNotIn("doc-b", {r["source_id"] for r in sd.read_candidates(self.work)})
        self.assertRegex(output, r"excluded-source [1-9]\d*")

    def test_the_controls_only_collector_drops_a_document_excluded_by_its_url_under_another_id(self):
        room = {"v-after-modal": {"check": 1, "test": 0, "stop": 2}}
        rows, _ = self.collect(control_room=room, control_pool=10, only_shapes=sd.HARD_SHAPES)
        self.assertIn("doc-b", {r["source_id"] for r in rows})                           # the controls come from doc-b without the exclusion
        record = os.path.join(self.data, "eval", "shapes-dev", "excluded-sources.tsv")
        common.write_text(record, ["\t".join(common.EXCLUDED_SOURCES_COLUMNS), "other-id\thttps://www.example.gov/doc-b/\tr\t2026-11-06"])
        rows, report = self.collect(control_room=room, control_pool=10, only_shapes=sd.HARD_SHAPES)
        self.assertEqual([], rows)
        self.assertTrue(any("excluded-source" in line for line in report))

    def test_a_same_id_different_url_conflict_is_refused_by_every_hard_command_and_by_both_profiles(self):
        self.prepare()
        out = os.path.join(self.temp, "out")
        self.write_work_exclusions([("doc-b", "not public domain", "2026-11-06")])
        record = os.path.join(self.data, "eval", "shapes-dev", "excluded-sources.tsv")
        common.write_text(record, ["\t".join(common.EXCLUDED_SOURCES_COLUMNS), "doc-b\thttps://example.gov/another-document\tolder\t2026-11-01"])
        commands = [("exclusions",), ("candidates", "--no-ud", "--lemma-cap", "9"), ("candidates", "--no-ud", "--round", "h2"),
                    ("candidates", "--no-ud", "--round", "h3", "--controls-only", "--control-pool", "5"), ("decide",), ("sample",),
                    ("report",), ("build", "--out-dir", out, "--allow-short")]
        for tranche in ("hard", "standard"):
            for command in commands:
                self.assert_refused(("--tranche", tranche) + command)
        self.assertFalse(os.path.exists(out))
        self.assertFalse(os.path.exists(os.path.join(self.root, "rounds")))
        self.assertEqual(0, self.hard_cli("status")[0])                                    # a command without exclusions is not blocked

    def test_controls_only_needs_the_hard_tranche_a_round_and_nothing_else_that_selects_targets(self):
        with self.assertRaises(ValueError):
            self.hard_cli("candidates", "--no-ud", "--controls-only")
        with self.assertRaises(ValueError):
            self.cli("candidates", "--no-ud", "--round", "h3", "--controls-only")
        with self.assertRaises(ValueError):
            self.hard_cli("candidates", "--no-ud", "--round", "h3", "--controls-only", "--no-controls")
        with self.assertRaises(ValueError):
            self.hard_cli("candidates", "--no-ud", "--round", "h3", "--controls-only", "--budget", "v-after-modal=3")


class QuotaGateTests(unittest.TestCase):
    @staticmethod
    def items(shape, role, lemmas, origin="real"):
        return [dict(shape=shape, role=role, lemma=lemma, origin=origin) for lemma in lemmas]

    def meets(self):
        floors = {shape: 2 for shape in sd.SHAPES}
        floors["n-measure"] = 0
        targets = [i for shape in sd.HARD_SHAPES for i in self.items(shape, "target", ["a", "b"])]
        return targets, self.items("v-after-modal", "control", ["x", "y"]), floors

    def test_the_pre_registered_numbers(self):
        self.assertEqual({"n-compound-head": 80, "n-subject-initial": 40, "v-imperative-initial": 70, "n-measure": 0, "v-after-modal": 80},
                         sd.HARD_FLOORS)
        self.assertEqual({"n-compound-head": 100, "n-subject-initial": 100, "v-imperative-initial": 100, "n-measure": 0, "v-after-modal": 100},
                         sd.HARD_CAPS)
        self.assertEqual((50, 60), (sd.HARD_CONTROL_MIN, sd.HARD_CONTROL_MAX))
        self.assertEqual((0.10, 0.97), (sd.BAND_MIN, sd.BAND_MAX))

    def test_a_set_within_the_floors_and_caps_has_no_problem_and_n_measure_may_be_empty(self):
        targets, controls, floors = self.meets()
        self.assertEqual([], sd.quota_problems_hard(targets, controls, floors, control_min=2, control_max=3, lemma_cap=1))

    def test_below_a_floor_above_a_cap_and_a_lemma_over_the_cap_are_reported(self):
        targets, controls, floors = self.meets()
        found = sd.quota_problems_hard(targets[1:], controls[:1], floors, control_min=2, control_max=3, lemma_cap=1)
        self.assertTrue(any(p.startswith("n-compound-head: 1 targets, the floor is 2") for p in found))
        self.assertTrue(any(p.startswith("controls: 1, the floor is 2") for p in found))
        found = sd.quota_problems_hard(targets, controls, floors, caps=dict(sd.HARD_CAPS, **{"n-compound-head": 1}), control_min=2, control_max=3,
                                       lemma_cap=1)
        self.assertTrue(any(p.startswith("n-compound-head: 2 targets, the cap is 1") for p in found))
        found = sd.quota_problems_hard(targets, controls, floors, control_min=2, control_max=1, lemma_cap=1)
        self.assertTrue(any(p.startswith("controls: 2, the cap is 1") for p in found))
        targets[1]["lemma"] = "a"
        self.assertTrue(any("lemma 'a' has 2 targets, the cap is 1" in p for p in
                            sd.quota_problems_hard(targets, controls, floors, control_min=2, control_max=3, lemma_cap=1)))

    def test_an_authored_item_and_an_n_measure_item_are_problems(self):
        targets, controls, floors = self.meets()
        targets[0]["origin"] = "authored"
        self.assertTrue(any("authored items: the hard tranche has none" in p
                            for p in sd.quota_problems_hard(targets, controls, floors, control_min=2, control_max=3, lemma_cap=1)))
        targets, controls, floors = self.meets()
        targets += self.items("n-measure", "target", ["a"])
        self.assertTrue(any(p.startswith("n-measure: 1 targets, the cap is 0") for p in
                            sd.quota_problems_hard(targets, controls, floors, control_min=2, control_max=3, lemma_cap=1)))

    def test_control_quotas_split_the_cap_over_the_hard_shapes(self):
        self.assertEqual({"n-compound-head": 15, "n-subject-initial": 15, "v-imperative-initial": 15, "v-after-modal": 15},
                         sd.hard_control_quotas(60))
        self.assertEqual(7, sum(sd.hard_control_quotas(7).values()))


class SelectionOrderTests(unittest.TestCase):
    @staticmethod
    def item(cand_id, shape, role, lemma, q, outcome="keep"):
        return dict(cand_id=cand_id, sent_key="k" + cand_id, shape=shape, role=role, lemma=lemma, origin="real", outcome=outcome, q=q)

    def test_the_build_takes_the_hardest_kept_targets_first_up_to_the_cap_and_controls_only_for_chosen_lemmas(self):
        items = [self.item("c1", "v-after-modal", "target", "a", "0.900"), self.item("c2", "v-after-modal", "target", "b", "0.200"),
                 self.item("c3", "v-after-modal", "target", "c", "0.500"), self.item("c4", "v-after-modal", "target", "d", "0.100", "drop"),
                 self.item("c5", "v-after-modal", "control", "b", ""), self.item("c6", "v-after-modal", "control", "z", ""),
                 self.item("c7", "v-after-modal", "control", "c", "")]
        targets, controls = sd.select_items_hard(items, caps={"v-after-modal": 2}, control_max=5)
        self.assertEqual(["c2", "c3"], [i["cand_id"] for i in targets])
        self.assertEqual({"c5", "c7"}, {i["cand_id"] for i in controls})

    def test_the_lemma_cap_keeps_the_hardest_of_a_lemma(self):
        items = [self.item("c1", "n-compound-head", "target", "a", "0.300"), self.item("c2", "n-compound-head", "target", "a", "0.200"),
                 self.item("c3", "n-compound-head", "target", "a", "0.400")]
        targets, _ = sd.select_items_hard(items, caps={"n-compound-head": 9}, lemma_cap=2)
        self.assertEqual(["c2", "c1"], [i["cand_id"] for i in targets])


class HardLeakageAndLicenseTests(HardFolder):
    def write_hard(self, items):
        extra = [["# shape = v-after-modal", "# role = target", "# target = 2", "# lemma = check", "# origin = real"] for _ in items]
        base.corpus_file(os.path.join(self.data, "eval", "shapes-dev", "hard.tsv"), items, extra)

    def test_the_hard_file_is_an_evaluation_set_and_the_builder_view_leaves_both_tranches_out(self):
        self.write_hard([("doc-q", "They must tighten the loose bolt before use .")])
        self.assertIn("shapes-dev-hard", leakage.evaluation_sets(include_ud=False))
        self.assertIn("shapes-dev", leakage.evaluation_sets(include_ud=False))
        kept = leakage.evaluation_sets(include_ud=False, shapes_dev=False)
        self.assertEqual(set(), {"shapes-dev", "shapes-dev-hard"} & set(kept))
        self.assertEqual(("shapes-dev", "shapes-dev-hard"), leakage.SHAPES_DEV_LABELS)

    def test_a_missing_hard_file_fails_loudly_because_the_hard_tranche_is_required(self):
        self.assertTrue(leakage.HARD_TRANCHE_REQUIRED)
        self.assertIn("shapes-dev-hard", leakage.REQUIRED_EVALUATION_SETS)
        self.assertTrue(check_leakage.hard_tranche_present())
        with self.assertRaises(SystemExit):
            check_leakage.dev_check(include_ud=False, tranche="hard")

    def test_a_clean_hard_tranche_passes_and_shares_documents_with_the_standard_tranche_by_design(self):
        self.write_hard([("doc-a", "They must tighten the loose bolt before use ."), ("doc-b", "Never open the supply line while the pump runs .")])
        rows, failures, failing = check_leakage.dev_check(include_ud=False, tranche="hard")
        self.assertEqual(0, failures, failing)
        by_set = {row[0]: row for row in rows}
        self.assertEqual(2, by_set["shapes-dev"][2])              # both documents are shared with the standard tranche: reported, not gated
        self.assertEqual("-", by_set["shapes-dev"][5])
        self.assertEqual({"open-dev", "open-test", "ste-challenge", "gold-v2", "shapes-probe", "shapes-dev"}, set(by_set))

    def test_an_exact_standard_sentence_a_same_document_near_duplicate_and_the_strict_sets_fail(self):
        self.write_hard([("doc-a", "the pressure switch OPENS when the tank is full"),          # exact (key) in the same document
                         ("doc-b", NEAR_CHANGED),                                               # near duplicate in the same document
                         ("doc-c", NEAR_CHANGED),                                               # another document: not a near duplicate
                         ("doc-z", "Remove the cover before you start the work .")])            # equals a sentence of another evaluation set
        rows, failures, failing = check_leakage.dev_check(include_ud=False, tranche="hard")
        self.assertEqual(["s1", "s2", "s4"], failing)
        by_set = {row[0]: row for row in rows}
        self.assertEqual((1, 1), (by_set["shapes-dev"][3], by_set["shapes-dev"][4]))
        self.assertEqual(1, by_set["ste-challenge"][3])
        self.assertEqual(3, failures)

    def test_a_hard_sentence_that_shares_a_probe_5gram_fails_and_the_standard_check_ignores_the_hard_file(self):
        self.write_hard([("doc-z", "First seal the pump housing with sealant then rest .")])
        rows, failures, failing = check_leakage.dev_check(include_ud=False, tranche="hard")
        self.assertEqual((["s1"], 1), (failing, {r[0]: r for r in rows}["shapes-probe"][5]))
        base.LeakageExtensionTests.write_dev(self, [("doc-y", "A brand new dev sentence about nothing in particular .")])
        self.assertEqual(0, check_leakage.dev_check(include_ud=False)[1])

    def test_the_license_check_scans_the_hard_tables_for_excluded_sources(self):
        folder = os.path.join(self.data, "eval", "shapes-dev")
        self.write_hard([("doc-x", "They must tighten the loose bolt before use .")])
        row = ["doc-x", "us-government", "T", "https://example.gov/doc-x", "A", "1990", "US federal government work (17 U.S.C. 105): test",
               "https://example.gov/terms", "2026-11-01", "a" * 64, "b" * 64, "1"]
        base.table_file(os.path.join(folder, "hard-sources.tsv"), sd.SOURCE_COLUMNS, [row])
        base.table_file(os.path.join(folder, "excluded-sources.tsv"), common.EXCLUDED_SOURCES_COLUMNS,
                        [["doc-x", "https://example.gov/doc-x", "not public domain", "2026-11-06"]])
        found = check_licenses.excluded_source_findings(self.data)
        self.assertTrue(any(f.startswith("eval/shapes-dev/hard.tsv:") and "doc-x" in f for f in found), found)
        self.assertTrue(any(f.startswith("eval/shapes-dev/hard-sources.tsv:") and "doc-x" in f for f in found), found)
        self.assertEqual([], [f for f in found if f.startswith("eval/shapes-dev/dev.tsv") or f.startswith("eval/shapes-dev/sources.tsv")])
        self.assertIn("eval/shapes-dev/hard-sources.tsv", check_licenses.SOURCE_TABLES)
        self.assertIn("eval/shapes-dev/hard-sources.tsv", check_licenses.FAA_TABLES)


class RequiredFilesFailClosedTests(base.MiniFolder):
    """check_licenses.main fails closed: every required table and corpus that is missing is a FINDING (exit 1, no traceback); the hard
    tranche's two files are optional only while leakage.HARD_TRANCHE_REQUIRED is False."""

    STANDARD = ("open/sources.tsv", "eval/gold-v2/sources.tsv", "silver/sources.tsv", "eval/shapes-dev/sources.tsv", "masc/sources.tsv",
                "eval/shapes-dev/dev.tsv", "imperative/sources.tsv")
    HARD = ("eval/shapes-dev/hard-sources.tsv", "eval/shapes-dev/hard.tsv")

    def write_data(self):
        super().write_data()
        folder = os.path.join(self.data, "eval", "shapes-dev")
        row = ["doc-a", "us-government", "T", "https://example.gov/doc-a", "A", "1990", "US federal government work (17 U.S.C. 105): test",
               "https://example.gov/terms", "2026-11-01", "a" * 64, "b" * 64, "1"]
        for path in (os.path.join(self.data, "eval", "gold-v2", "sources.tsv"), os.path.join(self.data, "silver", "sources.tsv"),
                     os.path.join(self.data, "imperative", "sources.tsv"),
                     os.path.join(folder, "sources.tsv"), os.path.join(folder, "hard-sources.tsv")):
            base.table_file(path, sd.SOURCE_COLUMNS, [row])
        extra = [["# shape = n-measure", "# role = target", "# target = 0", "# lemma = x", "# origin = real"]]
        for name in ("dev.tsv", "hard.tsv"):
            base.corpus_file(os.path.join(folder, name), [("doc-a", "A brand new dev sentence about nothing in particular .")], extra)
        base.table_file(os.path.join(folder, "excluded-sources.tsv"), common.EXCLUDED_SOURCES_COLUMNS, [])

    def run_main(self, required=True):
        out = io.StringIO()
        with mock.patch.object(leakage, "HARD_TRANCHE_REQUIRED", required), contextlib.redirect_stdout(out):
            with self.assertRaises(SystemExit) as raised:                      # any other exception (a traceback) fails the test
                check_licenses.main()
        self.assertEqual(1, raised.exception.code)                             # the fixture has no READMEs: other findings always exist
        return [line[len("FINDING: "):] for line in out.getvalue().splitlines() if line.startswith("FINDING: ")]

    def missing(self, findings):
        return sorted(f for f in findings if f.startswith("missing required file"))

    def remove(self, relative):
        os.remove(os.path.join(self.data, *relative.split("/")))

    def test_the_complete_fixture_has_no_missing_file_finding_in_either_flag_state(self):
        for required in (True, False):
            self.assertEqual([], self.missing(self.run_main(required)))
        self.assertEqual([], check_licenses.required_file_findings(self.data))

    def test_each_missing_required_file_is_one_finding_and_never_a_traceback(self):
        for relative in self.STANDARD + self.HARD:
            for required in (True, False):
                if relative in self.HARD and not required:
                    continue
                with self.subTest(relative=relative, required=required):
                    path = os.path.join(self.data, *relative.split("/"))
                    with open(path, "rb") as handle:
                        content = handle.read()
                    os.remove(path)
                    try:
                        self.assertEqual(["missing required file: data/" + relative], self.missing(self.run_main(required)))
                    finally:
                        with open(path, "wb") as handle:
                            handle.write(content)

    def test_a_missing_hard_file_is_skipped_silently_while_the_tranche_is_optional(self):
        for relative in self.HARD:
            self.remove(relative)
        self.assertEqual([], self.missing(self.run_main(False)))
        self.assertEqual(sorted("missing required file: data/" + r for r in self.HARD), self.missing(self.run_main(True)))

    def test_every_required_file_missing_at_once_yields_findings_only(self):
        for relative in self.STANDARD + self.HARD:
            self.remove(relative)
        expected = sorted("missing required file: data/" + r for r in self.STANDARD + self.HARD)
        self.assertEqual(expected, self.missing(self.run_main(True)))
        self.assertEqual(sorted("missing required file: data/" + r for r in self.STANDARD), self.missing(self.run_main(False)))

    def test_a_present_hard_table_is_scanned_even_while_the_tranche_is_optional(self):
        base.table_file(os.path.join(self.data, "eval", "shapes-dev", "hard-sources.tsv"), sd.SOURCE_COLUMNS,
                        [["doc-a", "us-government", "T", "https://example.gov/doc-a", "A", "1990", "CC BY-SA 4.0", "https://example.gov/terms",
                          "2026-11-01", "a" * 64, "b" * 64, "1"]])
        found = self.run_main(False)
        self.assertTrue(any(f.startswith("eval/shapes-dev/hard-sources.tsv: row mentions") for f in found), found)


class IndependenceTests(unittest.TestCase):
    """The selection of the hard tranche uses no PartOfSpeech model: no weights file, decoder, prediction or score."""

    FORBIDDEN = ("weights.bin", "Resources/weights", "PerceptronModel", "FeatureExtractor", "PartOfSpeechTagger", "ITagger", "eval-shapes",
                 "ShapesDevEvaluator")

    def sources(self):
        tools = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
        paths = [os.path.join(tools, "shapes_dev.py")]
        harness = os.path.join(tools, "hmm-tags")
        paths += [os.path.join(harness, name) for name in sorted(os.listdir(harness)) if name.endswith((".cs", ".csproj"))]
        return paths

    def test_the_builder_and_the_harness_never_name_a_model_file_decoder_or_the_evaluator(self):
        for path in self.sources():
            with open(path, encoding="utf-8") as handle:
                text = handle.read()
            for word in self.FORBIDDEN:
                self.assertNotIn(word, text, "%s mentions %s" % (os.path.basename(path), word))

    def test_the_harness_has_no_reference_to_the_partofspeech_library_and_is_not_in_the_solution(self):
        tools = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
        with open(os.path.join(tools, "hmm-tags", "WordSenseBasicTags.csproj"), encoding="utf-8") as handle:
            project = handle.read()
        self.assertNotIn("ProjectReference", project)
        self.assertNotIn("DemaConsulting.PartOfSpeech", project)
        with open(os.path.join(common.ROOT, "PartOfSpeech.slnx"), encoding="utf-8") as handle:
            self.assertNotIn("hmm-tags", handle.read())

    def test_the_builder_reads_only_the_numbers_of_hmm_tsv(self):
        self.assertEqual("hmm.tsv", sd.HMM_FILE)
        self.assertTrue(sd.HMM_MAGIC.startswith("# hmm-tags"))


if __name__ == "__main__":
    unittest.main()

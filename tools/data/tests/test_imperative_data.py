"""Tests of imperative_data.py (the builder of the training source data/imperative/) on a miniature data and work folder.

Run: python -m unittest discover -s tools/data/tests -p "test_imperative_data.py"   (developer-run; the pipeline needs no model or network)
"""
import ast
import contextlib
import io
import os
import re
import sys
import unittest
from unittest import mock

sys.path.insert(0, os.path.dirname(os.path.dirname(os.path.abspath(__file__))))
import adjudicate
import check_leakage
import common
import imperative_data as idata
import shapes_dev as sd
import test_shapes_dev as base

GOOD = [
    "Check the pressure valve before use .",
    "Seal the pump housing daily .",
    "Test it twice .",
    "Drop the oil and the water into the tank .",
    "Mix 2 pounds of oil with the water .",
]
NOT_FRAME = [
    "The pressure switch opens when the tank is full .",
    "Flow stops at the valve .",
    "You must check the oil level daily .",
]


def tags_of(tokens):
    """Fixture tags: token 0 VERB, the closed words and numbers by shape, every other token NOUN."""
    out = []
    for i, token in enumerate(tokens):
        if i == 0:
            out.append("VERB")
        elif token.lower() in ("the", "a"):
            out.append("DET")
        elif token == ".":
            out.append("PUNCT")
        elif token.isdigit():
            out.append("NUM")
        else:
            out.append("NOUN")
    return out


class Mini(base.MiniFolder):
    """The miniature folders of test_shapes_dev plus helpers for the imperative pipeline."""

    def write_data(self):
        super().write_data()
        base.corpus_file(os.path.join(self.data, "eval", "shapes-dev", "dev.tsv"),
                         [("army-dev-doc", "Open the access door and inspect the seal before each flight today .")])
        base.corpus_file(os.path.join(self.data, "eval", "shapes-dev", "hard.tsv"),
                         [("army-hard-doc", "Drain the reservoir completely when the pump has stopped turning at idle .")])
        base.table_file(os.path.join(self.data, "eval", "shapes-dev", "sources.tsv"), sd.SOURCE_COLUMNS,
                        [self.source_row("army-dev-doc", "https://archive.org/download/DevDoc/DevDoc_djvu.txt")])

    def collect(self, documents, **options):
        options.setdefault("clean", False)         # the fixture sentences are token-spaced, not real text: the noise filter has its own tests
        self.write_draft(sorted(documents))
        for source_id, sentences in documents.items():
            self.write_text_doc(source_id, sentences)
        texts = [t for sentences in documents.values() for t in sentences]
        return idata.collect_candidates(self.work, self.data, include_ud=False, tokens_override=self.tokens_for(texts), **options)

    def run_tool(self, *args):
        out, err = io.StringIO(), io.StringIO()
        with contextlib.redirect_stdout(out), contextlib.redirect_stderr(err):
            code = idata.main(["--work", self.root, "--data-dir", self.data] + list(args))
        return code, out.getvalue(), err.getvalue()

    def decided(self, specs, draft_ids=None):
        """Write W/candidates.tsv, private/decided.tsv and decided-rows.tsv from specs: dicts with text, lemma, source and optional
        outcome, reason, tags, statuses, disputed."""
        ids = draft_ids or sorted({s["source"] for s in specs})
        self.write_draft(ids)
        rows, items, token_rows = [], [], []
        for number, spec in enumerate(specs, 1):
            tokens = base.simple_tokens(spec["text"])
            cand = dict(cand_id="c%05d" % number, shape=idata.SHAPE, role="target", target=0, lemma=spec["lemma"], origin="real",
                        source_id=spec["source"], line=number, text=spec["text"], tokens=tokens)
            rows.append(cand)
            key = sd.sent_key_of(spec["text"])
            tags = spec.get("tags") or tags_of(tokens)
            statuses = spec.get("statuses") or ["unanimous"] * len(tokens)
            disputed = sum(1 for s in statuses if s == "disputed")
            items.append([cand["cand_id"], key, idata.SHAPE, "target", "0", spec["lemma"], "real", spec["source"], spec.get("outcome", "keep"),
                          spec.get("reason", ""), tags[0], tags[0], tags[0], str(spec.get("disputed", disputed))])
            token_rows += [(key, i, tokens[i], tags[i], statuses[i]) for i in range(len(tokens))]
        sd.write_candidates(self.work, rows)
        adjudicate.write_rows(self.work.private_file("decided.tsv"), sd.ITEM_COLUMNS, items)
        adjudicate.write_rows(self.work.private_file("decided-rows.tsv"), sd.ROW_COLUMNS, token_rows)

    def build(self, specs, **options):
        self.decided(specs)
        out = os.path.join(self.temp, "out")
        built, report = idata.build(self.work, out, data_dir=self.data, **options)
        return built, report, out


def drops_of(report):
    """{reason: count} of the table `| dropped because | sentences |` of a build report."""
    found = {}
    for line in report:
        match = re.match(r"\| ([a-z-]+) \| ([0-9]+) \|$", line)
        if match:
            found[match.group(1)] = int(match.group(2))
    return found


def spec(text, lemma, source="army-a", **extra):
    return dict(text=text, lemma=lemma, source=source, **extra)


def many(lemma, word, count, source="army-a", start=1):
    return [spec("%s the valve %d ." % (word, start + n), lemma, source) for n in range(count)]


class IndexTests(Mini):
    def test_index_has_the_shapes_dev_tables_and_extra_tables_and_archive_identifiers(self):
        extra = os.path.join(self.temp, "draft.tsv")
        base.table_file(extra, sd.SOURCE_COLUMNS, [self.source_row("army-extra", "https://archive.org/download/ExtraId/ExtraId_djvu.txt")])
        index = idata.used_index(self.data, sd.load_exclusions(self.work, self.data), [extra])
        self.assertIsNotNone(idata.document_rejection(index, "army-dev-doc"))
        self.assertIsNotNone(idata.document_rejection(index, "x", "https://archive.org/download/DevDoc/DevDoc_djvu.txt"))
        self.assertIsNotNone(idata.document_rejection(index, "other-id", "https://archive.org/details/devdoc"))       # same identifier, another URL
        self.assertIn("extra:draft.tsv", idata.document_rejection(index, "army-extra"))
        self.assertIsNotNone(idata.document_rejection(index, "y", "http://www.archive.org/stream/EXTRAID/x.txt"))
        self.assertIsNotNone(idata.document_rejection(index, "gutenberg-1023"))                                        # a training document
        self.assertIsNone(idata.document_rejection(index, "army-new", "https://archive.org/download/NewOne/NewOne_djvu.txt"))

    def test_ia_ident(self):
        self.assertEqual("abc-1", idata.ia_ident("https://archive.org/download/ABC-1/x.txt"))
        self.assertIsNone(idata.ia_ident("https://example.gov/abc"))

    def problems_of(self, row_changes, text=("Check the valve 1 .",), documents=None):
        row = self.source_row("army-new", "https://archive.org/download/NewOne/NewOne_djvu.txt", sentences=str(len(text)))
        self.write_text_doc("army-new", list(text))
        row[10] = common.sha256_file(os.path.join(self.root, "text", "army-new.txt"))
        for column, value in row_changes.items():
            row[sd.SOURCE_COLUMNS.index(column)] = value
        common.write_text(os.path.join(self.root, "sources-draft.tsv"), ["\t".join(sd.SOURCE_COLUMNS), "\t".join(row)])
        exclusions = sd.load_exclusions(self.work, self.data)
        index = idata.used_index(self.data, exclusions)
        return idata.draft_problems(self.work, sd.read_draft(self.work), index, exclusions)

    def test_a_sound_draft_has_no_problem(self):
        self.assertEqual([], self.problems_of({}))

    def test_draft_problems_name_each_defect(self):
        self.assertTrue(any("license_basis" in p for p in self.problems_of({"license_basis": "CC BY 4.0"})))
        self.assertTrue(any("FAA" in p for p in self.problems_of({"title": "FAA Handbook of Flying"})))
        self.assertTrue(any("FAA" in p for p in self.problems_of({"url": "https://www.faa.gov/x.pdf"})))
        self.assertTrue(any("share-alike" in p for p in self.problems_of({"license_basis": "US public domain; CC BY-SA text"})))
        self.assertTrue(any("already used" in p for p in self.problems_of({"url": "https://archive.org/download/DevDoc/DevDoc_djvu.txt"})))
        self.assertTrue(any("already used" in p for p in self.problems_of({"source_id": "gutenberg-1023"})))
        self.assertTrue(any("sentence count" in p for p in self.problems_of({"sentences": "9"})))
        self.assertTrue(any("text_sha256" in p for p in self.problems_of({"text_sha256": "z" * 64})))
        self.assertTrue(any("SHA-256" in p for p in self.problems_of({"raw_sha256": "abc"})))
        self.assertTrue(any("not an http" in p for p in self.problems_of({"url": "ftp://x/y"})))

    def test_a_missing_text_file_and_a_tab_are_problems(self):
        self.write_draft(["army-new"])
        exclusions = sd.load_exclusions(self.work, self.data)
        found = idata.draft_problems(self.work, sd.read_draft(self.work), idata.used_index(self.data, exclusions), exclusions)
        self.assertTrue(any("text/army-new.txt is missing" in p for p in found))

    def test_exclusions_command_exit_codes(self):
        self.write_draft(["army-dev-doc"])
        self.write_text_doc("army-dev-doc", ["Check the valve 1 ."])
        code, out, _ = self.run_tool("exclusions")
        self.assertEqual(1, code)
        self.assertIn("REJECT army-dev-doc", out)
        self.assertTrue(os.path.exists(os.path.join(self.root, "exclusions.tsv")))
        code, out, _ = self.run_tool("exclusions", "--check", "new-doc", "https://archive.org/details/DevDoc")
        self.assertIn("ACCEPT: new-doc", out)
        self.assertIn("REJECT: https://archive.org/details/DevDoc", out)

    def test_an_excluded_source_record_rejects_the_document(self):
        common.write_text(os.path.join(self.data, "eval", "shapes-dev", "excluded-sources.tsv"),
                          ["\t".join(common.EXCLUDED_SOURCES_COLUMNS), "nist-hb44\thttps://nvlpubs.nist.gov/hb44.pdf\tlicense doubt\t2026-11-06"])
        found = self.problems_of({"source_id": "nist-hb44-copy", "url": "https://nvlpubs.nist.gov/hb44.pdf"})
        self.assertTrue(any("excluded as nist-hb44" in p for p in found))


class CandidateTests(Mini):
    def test_only_the_imperative_frame_is_a_candidate_with_unique_ordered_ids(self):
        rows, report, rejected, counts = self.collect({"army-a": GOOD + NOT_FRAME})
        self.assertEqual({idata.SHAPE}, {r["shape"] for r in rows})
        self.assertEqual({"target"}, {r["role"] for r in rows})
        self.assertEqual({"real"}, {r["origin"] for r in rows})
        self.assertEqual(sorted(GOOD), sorted(r["text"] for r in rows))
        self.assertEqual(["c%05d" % n for n in range(1, len(rows) + 1)], [r["cand_id"] for r in rows])
        self.assertTrue(all(r["target"] == 0 for r in rows))
        self.assertEqual(len(GOOD), counts["candidates"])
        self.assertEqual(len(GOOD), counts["frame_hits"])
        self.assertEqual({"check", "seal", "test", "drop", "mix"}, {r["lemma"] for r in rows})

    def test_candidates_are_reproducible_and_ordered_by_the_seeded_rank_not_by_document_order(self):
        first = self.collect({"army-a": GOOD})[0]
        second = self.collect({"army-a": list(reversed(GOOD))})[0]
        self.assertEqual([r["text"] for r in first], [r["text"] for r in second])
        other = self.collect({"army-a": GOOD}, seed=7)[0]
        self.assertEqual(sorted(r["text"] for r in first), sorted(r["text"] for r in other))

    def test_lemma_cap_takes_at_most_n_per_lemma_in_seeded_order(self):
        sentences = ["Check the valve %d ." % n for n in range(1, 8)] + ["Seal the valve 1 ."]
        rows, _, _, counts = self.collect({"army-a": sentences}, lemma_cap=3)
        self.assertEqual(3, sum(1 for r in rows if r["lemma"] == "check"))
        self.assertEqual(1, sum(1 for r in rows if r["lemma"] == "seal"))
        self.assertEqual(2, counts["lemmas"])
        again = self.collect({"army-a": sentences}, lemma_cap=3)[0]
        self.assertEqual([r["text"] for r in rows], [r["text"] for r in again])
        expected = sorted(("Check the valve %d ." % n for n in range(1, 8)), key=lambda t: sd.rank_of(sd.SEED, "imperative", sd.sent_key_of(t)))[:3]
        self.assertEqual(sorted(expected), sorted(r["text"] for r in rows if r["lemma"] == "check"))

    def test_a_gerund_or_third_person_first_word_is_not_a_candidate_and_is_counted(self):
        extra = dict(base.LEXICON, checking="NOUN VERB", dropped="NOUN VERB")
        common.write_text(os.path.join(self.data, "lexicon", "lexicon.tsv"), ["# fixture lexicon"] + ["%s\t%s" % (k, v) for k, v in sorted(extra.items())])
        rows, report, _, counts = self.collect({"army-a": GOOD + ["Checking the valve 1 .", "Dropped the valve 3 ."]})
        self.assertEqual(sorted(GOOD), sorted(r["text"] for r in rows))
        self.assertEqual(2, counts["not_base_form"])
        self.assertTrue(any("first word is not its base form" in line for line in report))

    def test_the_total_cap_takes_the_first_candidates_in_the_seeded_order(self):
        sentences = ["%s the valve %d ." % (word, n) for n, word in enumerate(["Check", "Seal", "Test", "Drop", "Mix", "Check", "Seal", "Test"], 1)]
        rows, report, _, counts = self.collect({"army-a": sentences}, lemma_cap=2, maximum=4)
        self.assertEqual(4, len(rows))
        self.assertEqual(4, counts["beyond_total"])
        ranked = sorted(sentences, key=lambda t: sd.rank_of(sd.SEED, "imperative", sd.sent_key_of(t)))
        self.assertEqual(sorted(ranked[:4]), sorted(r["text"] for r in rows))
        self.assertTrue(any("4 beyond the total cap of 4" in line for line in report))

    def test_sentences_of_the_shapes_dev_set_are_dropped_equal_and_near(self):
        equal = "Open the access door and inspect the seal before each flight today ."
        near = "Drain the reservoir completely when the pump has stopped turning at idle now ."
        rows, report, _, _ = self.collect({"army-a": GOOD + [equal, near]})
        self.assertNotIn(equal, [r["text"] for r in rows])
        self.assertNotIn(near, [r["text"] for r in rows])
        text = "\n".join(report)
        self.assertIn(idata.EQUALS_SHAPES_DEV + " 1", text)
        self.assertIn(idata.NEAR_SHAPES_DEV + " 1", text)

    def test_a_short_sentence_is_not_a_near_duplicate_and_a_different_sentence_stays(self):
        reference = idata.ShapesDevSentences(self.data)
        self.assertIsNone(reference.reason("Open the access door ."))
        self.assertIsNone(reference.reason("Close the door and secure the latch before the next flight begins ."))
        self.assertEqual(idata.EQUALS_SHAPES_DEV, reference.reason("open the ACCESS door and inspect the seal before each flight today"))

    def test_extra_text_folder_feeds_the_equal_rule_and_the_reprint_rule(self):
        folder = os.path.join(self.temp, "sd-text")
        common.write_text(os.path.join(folder, "army-old.txt"), ["Weld the bracket to the frame at once ."])
        rows, report, rejected, _ = self.collect({"army-a": GOOD + ["Weld the bracket to the frame at once ."]}, extra_text=[folder])
        self.assertNotIn("Weld the bracket to the frame at once .", [r["text"] for r in rows])
        reprint = ["Weld the bracket to the frame at once ."] + ["Test the circuit %d ." % n for n in range(2)]
        common.write_text(os.path.join(folder, "army-old.txt"), reprint + ["Check the weld again ."])
        rows, report, rejected, _ = self.collect({"army-b": reprint + ["Check the weld again ."]}, extra_text=[folder])
        self.assertIn("army-b", rejected)
        self.assertEqual([], rows)
        with self.assertRaises(ValueError):
            idata.ShapesDevSentences(self.data, [os.path.join(self.temp, "missing")])

    def test_training_and_evaluation_sentences_are_dropped_and_a_mirror_document_is_rejected(self):
        copy = "Mix the oil and the water in the tank daily ."
        rows, _, _, _ = self.collect({"army-a": GOOD + [copy]})
        self.assertNotIn(copy, [r["text"] for r in rows])
        mirror = [copy, "Remove the cover before you start the work .", "Gold sentence one is here ."]
        rows, report, rejected, _ = self.collect({"army-m": mirror})
        self.assertIn("army-m", rejected)
        self.assertEqual([], rows)

    def test_a_probe_5gram_is_dropped_without_printing_probe_text(self):
        copy = "Check that the pump housing with the gasket compound before fitting is sound ."
        rows, report, _, _ = self.collect({"army-a": GOOD + [copy]})
        self.assertNotIn(copy, [r["text"] for r in rows])
        self.assertTrue(any("probe-5gram 1" in line for line in report))
        self.assertFalse(any("gasket" in line for line in report))

    def test_a_used_document_and_an_unlisted_text_are_not_used(self):
        self.write_draft(["army-dev-doc"])
        self.write_text_doc("army-dev-doc", GOOD)
        self.write_text_doc("army-ghost", GOOD)
        tokens = self.tokens_for(GOOD)
        rows, report, rejected, _ = idata.collect_candidates(self.work, self.data, include_ud=False, tokens_override=tokens, clean=False)
        self.assertEqual([], rows)
        self.assertIn("army-dev-doc", rejected)
        self.assertTrue(any("army-ghost.txt has no row" in line for line in report))

    def test_the_noise_filter_applies_to_real_text(self):
        good = "Check the pressure valve before use."
        bad = "Check the pressure valve before use"
        rows, report, _, _ = self.collect({"army-a": [good, bad]}, clean=True, vocab=set(sd.lexicon_of(self.data)) | {"before", "use"})
        self.assertEqual([good], [r["text"] for r in rows])
        self.assertTrue(any("noise-start-end 1" in line for line in report))

    def test_gate_messages(self):
        self.assertEqual([], idata.gate_messages(dict(candidates=1500, lemmas=250)))
        self.assertEqual(2, len(idata.gate_messages(dict(candidates=10, lemmas=3))))

    def test_command_writes_candidates_in_the_shared_columns_and_reports_the_gate(self):
        self.write_draft(["army-a"])
        self.write_text_doc("army-a", GOOD)
        common.write_text(os.path.join(self.root, "tokens.tsv"), ["text\tsentences\ttokens"] + ["%s\t1\t%s" % (t, " ".join(base.simple_tokens(t))) for t in GOOD])
        with mock.patch.object(sd, "noise_reason", return_value=None):          # the fixture words are not a real vocabulary
            code, out, err = self.run_tool("candidates", "--no-ud", "--min-candidates", "5", "--min-lemmas", "5")
        self.assertEqual(0, code, err)
        columns, records = adjudicate.read_records(os.path.join(self.root, "candidates.tsv"))
        self.assertEqual(sd.CANDIDATE_COLUMNS, tuple(columns))
        self.assertEqual(5, len(records))
        self.assertTrue(os.path.exists(os.path.join(self.root, "private", "candidates-report.md")))
        with mock.patch.object(sd, "noise_reason", return_value=None):
            code, out, err = self.run_tool("candidates", "--no-ud")
        self.assertEqual(1, code)
        self.assertIn("GATE G2 FAILED", err)


class BuildTests(Mini):
    def test_kept_sentences_are_written_in_the_corpus_format_with_sources(self):
        built, report, out = self.build(many("check", "Check", 3) + many("seal", "Seal", 2, "army-b"), minimum=1)
        self.assertEqual(5, len(built))
        header, sentences = common.read_corpus(os.path.join(out, "imperative.tsv"))
        self.assertEqual(["imperative-%04d" % n for n in range(1, 6)], [s["sent_id"] for s in sentences])
        self.assertTrue(all(s["rows"][0][1] == "VERB" and s["rows"][0][2] == "unanimous" for s in sentences))
        self.assertTrue(any(line == "# use = training" for line in header))
        self.assertTrue(any(line.startswith("# corpus = ") for line in header))
        _, records = adjudicate.read_records(os.path.join(out, "sources.tsv"))
        self.assertEqual(["army-a", "army-b"], [r["source_id"] for r in records])
        self.assertEqual(["3", "2"], [r["sentences"] for r in records])
        self.assertTrue(os.path.exists(os.path.join(out, "excluded-documents.tsv")))
        self.assertTrue(os.path.exists(os.path.join(out, "dropped-sentences.tsv")))
        self.assertIn("| us-government | 2 | 5 |", report)

    def test_drop_reasons_disputed_noun_gerund_and_frame(self):
        specs = [spec("Check the valve 1 .", "check"),
                 spec("Check the valve 2 .", "check", statuses=["unanimous", "disputed", "unanimous", "unanimous", "unanimous"]),
                 spec("Check the valve 3 .", "check", outcome="drop", reason="class-mismatch"),
                 spec("Check the valve 4 .", "check", outcome="drop", reason="frame-mismatch"),
                 spec("Checking the valve 5 .", "check", tags=["VERB", "DET", "NOUN", "NUM", "PUNCT"]),
                 spec("Checks the valve 6 .", "check", tags=["VERB", "DET", "NOUN", "NUM", "PUNCT"]),
                 spec("Seal the valve 7 .", "seal", tags=["NOUN", "DET", "NOUN", "NUM", "PUNCT"], outcome="keep")]
        built, report, _ = self.build(specs, minimum=1)
        self.assertEqual(["Check the valve 1 ."], [s["text"] for s in built])
        self.assertEqual({"disputed-token": 1, "class-mismatch": 1, "frame-mismatch": 1, "not-base-form": 2, "not-verb": 1}, drops_of(report))

    def test_a_gerund_or_third_person_form_is_not_a_base_form(self):
        lex = sd.lexicon_of(self.data)
        item = dict(outcome="keep", reason="", disputed_tokens=0, target=0)
        self.assertIsNone(idata.keep_reason(item, [["Check", "VERB", "unanimous"]], lex))
        self.assertEqual("not-base-form", idata.keep_reason(item, [["Checks", "VERB", "unanimous"]], lex))
        self.assertEqual("not-base-form", idata.keep_reason(item, [["Dropped", "VERB", "unanimous"]], lex))
        self.assertEqual("not-initial", idata.keep_reason(dict(item, target=2), [["Check", "VERB", "unanimous"]], lex))
        self.assertEqual("disputed-token", idata.keep_reason(dict(item, disputed_tokens=1), [["Check", "VERB", "unanimous"]], lex))
        self.assertEqual("class-mismatch", idata.keep_reason(dict(item, outcome="drop", reason="class-mismatch"), [], lex))

    def test_a_fix_tier_violation_drops_the_sentence_and_nothing_is_changed_by_hand(self):
        bad = spec("Check the valve not the tank .", "check", tags=["VERB", "DET", "NOUN", "ADV", "DET", "NOUN", "PUNCT"])
        built, report, _ = self.build([bad, spec("Check the valve 1 .", "check")], minimum=1)
        self.assertEqual(["Check the valve 1 ."], [s["text"] for s in built])
        self.assertEqual({"fix-tier": 1}, drops_of(report))

    def test_an_existing_imperative_source_in_the_data_folder_does_not_change_the_fix_tier_drops(self):
        bad = spec("Check the valve not the tank .", "check", tags=["VERB", "DET", "NOUN", "ADV", "DET", "NOUN", "PUNCT"])
        specs = [bad, spec("Check the valve 1 .", "check")]
        path = os.path.join(self.data, "imperative", "imperative.tsv")
        os.remove(path)
        without, report_without, _ = self.build(specs, minimum=1)
        previous = [dict(sent_id="imperative-0001", source_id="army-old", text="The valve is very closed .",
                         rows=[["The", "DET", "unanimous"], ["valve", "NOUN", "unanimous"], ["is", "AUX", "unanimous"],
                               ["very", "ADV", "unanimous"], ["closed", "ADJ", "unanimous"], [".", "PUNCT", "unanimous"]])]
        common.write_corpus(path, ["# corpus = a previous output"], previous)
        seen = []
        real = idata.convention_rules.build_resources

        def spy(sentences):
            sentences = list(sentences)
            seen.extend(sentences)
            return real(sentences)

        with mock.patch.object(idata.convention_rules, "build_resources", spy):
            with_previous, report_with, _ = self.build(specs, minimum=1)
        self.assertEqual([s["text"] for s in without], [s["text"] for s in with_previous])
        self.assertEqual(drops_of(report_without), drops_of(report_with))
        self.assertEqual({"fix-tier": 1}, drops_of(report_with))
        self.assertTrue(seen)
        self.assertEqual(set(), {s.source for s in seen} & {"imperative", "ste"})     # RESOURCE_EXEMPT of check_conventions.py
        self.assertNotIn("closed", {w for s in seen for w in s.w})

    def test_caps_in_seeded_order_lemma_document_and_total(self):
        specs = many("check", "Check", 8) + many("seal", "Seal", 8, "army-b", 20) + many("test", "Test", 4, "army-c", 40)
        built, report, _ = self.build(specs, minimum=1, lemma_cap=6, document_cap=5, maximum=100)
        by_lemma = {}
        for s in built:
            by_lemma[s["rows"][0][0]] = by_lemma.get(s["rows"][0][0], 0) + 1
        self.assertEqual({"Check": 5, "Seal": 5, "Test": 4}, by_lemma)
        self.assertEqual({"document-cap": 6}, drops_of(report))
        built, report, _ = self.build(specs, minimum=1, lemma_cap=3, document_cap=30, maximum=7)
        self.assertEqual(7, len(built))
        drops = drops_of(report)
        self.assertEqual(13, sum(drops.values()))                    # 20 kept sentences, 7 built
        self.assertGreater(drops["total-cap"], 0)
        self.assertGreater(drops["lemma-cap"], 0)

    def test_which_sentences_a_cap_keeps_depends_only_on_the_seeded_rank(self):
        specs = many("check", "Check", 8)
        first, _, _ = self.build(specs, minimum=1, lemma_cap=3)
        second, _, _ = self.build(list(reversed(specs)), minimum=1, lemma_cap=3)
        self.assertEqual(sorted(s["text"] for s in first), sorted(s["text"] for s in second))
        expected = sorted((s["text"] for s in specs), key=lambda t: sd.rank_of(sd.SEED, "imperative-build", sd.sent_key_of(t)))[:3]
        self.assertEqual(sorted(expected), sorted(s["text"] for s in first))

    def test_the_quota_gate_refuses_below_the_minimum_and_writes_nothing(self):
        self.decided(many("check", "Check", 3))
        out = os.path.join(self.temp, "out")
        with self.assertRaises(idata.BuildError):
            idata.build(self.work, out, data_dir=self.data)
        self.assertFalse(os.path.exists(out))
        code, _, err = self.run_tool("build", "--out-dir", out)
        self.assertEqual(1, code)
        self.assertIn("build refused", err)
        self.assertFalse(os.path.exists(out))
        code, _, err = self.run_tool("build", "--out-dir", out, "--allow-short")
        self.assertEqual(0, code, err)
        self.assertTrue(os.path.exists(os.path.join(out, "imperative.tsv")))

    def test_the_quota_problems_list_every_miss(self):
        lex = sd.lexicon_of(self.data)
        good = dict(sent_id="imperative-0001", source_id="d", text="Check it .", rows=[["Check", "VERB", "unanimous"], ["it", "PRON", "unanimous"], [".", "PUNCT", "unanimous"]])
        bad = dict(sent_id="imperative-0002", source_id="d", text="Checks it .", rows=[["Checks", "NOUN", "unanimous"], ["it", "PRON", "disputed"], [".", "PUNCT", "unanimous"]])
        self.assertEqual([], idata.quota_problems([good], lex, 1, 5, 6, 30))
        found = "\n".join(idata.quota_problems([good, bad], lex, 5, 1, 1, 1))
        for part in ("at least 5", "at most 1 allowed", "document d has 2", "token 0 is not tagged VERB", "is not unanimous"):
            self.assertIn(part, found)
        rejoin = dict(good, text="Check them .")
        self.assertIn("do not rejoin", "\n".join(idata.quota_problems([rejoin], lex, 1, 5, 6, 30)))

    def test_an_excluded_document_cannot_be_built_in(self):
        common.write_text(os.path.join(self.data, "eval", "shapes-dev", "excluded-sources.tsv"),
                          ["\t".join(common.EXCLUDED_SOURCES_COLUMNS), "army-a\thttps://example.gov/army-a\tlicense doubt\t2026-11-06"])
        self.decided(many("check", "Check", 2))
        out = os.path.join(self.temp, "out")
        built, report = idata.build(self.work, out, data_dir=self.data, minimum=0)
        self.assertEqual([], built)                       # the items of an excluded document are dropped by the decision reader

    def read_all(self, folder):
        found = {}
        for name in os.listdir(folder):
            with open(os.path.join(folder, name), "rb") as handle:
                found[name] = handle.read()
        return found

    def test_the_build_is_deterministic(self):
        specs = many("check", "Check", 4) + many("seal", "Seal", 4, "army-b", 10)
        _, _, out = self.build(specs, minimum=1)
        first = self.read_all(out)
        _, _, out = self.build(specs, minimum=1)
        self.assertEqual(first, self.read_all(out))
        self.assertTrue(first["imperative.tsv"].endswith(b"\n") and b"\r" not in first["imperative.tsv"] and not first["imperative.tsv"].startswith(b"\xef\xbb\xbf"))

    def test_flags_that_both_labelers_agree_on_are_kept_with_their_documentation(self):
        statuses = ["unanimous", "unanimous", "unanimous", "unanimous+mentioned", "unanimous"]
        built, _, out = self.build([spec("Check the valve 1 .", "check", statuses=statuses)], minimum=1)
        header, sentences = common.read_corpus(os.path.join(out, "imperative.tsv"))
        self.assertEqual("unanimous+mentioned", sentences[0]["rows"][3][2])
        self.assertTrue(any(line.startswith("# status flags = ") for line in header))

    def test_report_lists_families_lexicon_coverage_and_counts(self):
        built, _, out = self.build(many("check", "Check", 3), minimum=1)
        lines = idata.report_lines(out, self.data)
        text = "\n".join(lines)
        self.assertIn("sentences: 3", text)
        self.assertIn("lexicon coverage", text)
        code, printed, _ = self.run_tool("report", "--out-dir", out)
        self.assertEqual(0, code)
        self.assertIn("| us-government | 1 | 3 |", printed)


class ReviewTests(Mini):
    def sentences(self, count):
        out = []
        for n in range(1, count + 1):
            tokens = base.simple_tokens("Check the valve %d ." % n)
            out.append(dict(sent_id="imperative-%04d" % n, source_id="army-a", text="Check the valve %d ." % n,
                            rows=[[t, g, "unanimous"] for t, g in zip(tokens, tags_of(tokens))]))
        return out

    def test_the_sample_has_sixty_real_items_and_four_swapped_controls_and_is_seeded(self):
        sample = idata.build_sample(self.sentences(100))
        self.assertEqual(60, sum(1 for kind, _, _ in sample if kind == "real"))
        controls = [(s, shown) for kind, s, shown in sample if kind == "control"]
        self.assertEqual(4, len(controls))
        self.assertTrue(all(shown == "NOUN" for _, shown in controls))
        self.assertEqual(64, len({s["sent_id"] for _, s, _ in sample}))
        again = idata.build_sample(self.sentences(100))
        self.assertEqual([s["sent_id"] for _, s, _ in sample], [s["sent_id"] for _, s, _ in again])
        other = idata.build_sample(self.sentences(100), seed=1)
        self.assertNotEqual([s["sent_id"] for _, s, _ in sample], [s["sent_id"] for _, s, _ in other])
        self.assertEqual(10, len(idata.build_sample(self.sentences(10), controls=0)))

    def test_the_sample_file_shows_every_token_with_its_tag_and_the_key_holds_the_truth(self):
        sample = idata.build_sample(self.sentences(6), size=4, controls=2)
        lines, keys = idata.sample_lines(sample)
        text = "\n".join(lines)
        self.assertIn("Tagged: 0:Check/", text)
        self.assertIn("4:./PUNCT", text)
        self.assertIn("others", text)
        self.assertEqual(6, len(keys))
        for (name, kind, sent_id, shape, target, shown, agreed, ntokens), (k, _, shown_tag) in zip(keys, sample):
            self.assertEqual(k, kind)
            self.assertEqual(idata.SHAPE, shape)
            self.assertEqual("VERB", agreed)
            self.assertEqual("VERB" if kind == "real" else "NOUN", shown)
            self.assertEqual(5, ntokens)

    def keys_for(self, kinds):
        return [dict(item="R%03d" % n, kind=kind, ntokens="5") for n, kind in enumerate(kinds, 1)]

    def test_parse_answers(self):
        text = "R001\tcorrect\t-\tconventions: imperative\nnoise line\nR002\tIncorrect\t1, 3\tx\n`R003\tunclear\t\t`\n"
        self.assertEqual([("R001", "correct", [], "conventions: imperative"), ("R002", "incorrect", [1, 3], "x"), ("R003", "unclear", [], "")],
                         idata.parse_answers(text))
        with self.assertRaises(ValueError):
            idata.parse_answers("R001\tcorrect\t-\tx\nR001\tcorrect\t-\tx\n")
        with self.assertRaises(ValueError):
            idata.parse_answers("R001\tcorrect\tthree\tx\n")

    def evaluate(self, kinds, answers):
        parsed = [(f"R{n:03d}", verdict, others, "") for n, (verdict, others) in enumerate(answers, 1)]
        return idata.review_lines(*idata.review_numbers(self.keys_for(kinds), parsed))

    def test_the_gate_passes_at_95_percent_and_fails_below(self):
        kinds = ["real"] * 60 + ["control"] * 4
        right = [("correct", [])] * 58 + [("incorrect", [])] * 2 + [("incorrect", [])] * 4
        lines, passed = self.evaluate(kinds, right)
        self.assertTrue(passed, lines)
        self.assertIn("96.7%", "\n".join(lines))
        lines, passed = self.evaluate(kinds, [("correct", [])] * 57 + [("incorrect", [])] * 3 + [("incorrect", [])] * 4)
        self.assertTrue(passed, "3 incorrect of 60 is exactly 95.0%, which meets `at least 95 percent`")
        self.assertIn("95.0%", "\n".join(lines))
        lines, passed = self.evaluate(kinds, [("correct", [])] * 56 + [("incorrect", [])] * 4 + [("incorrect", [])] * 4)
        self.assertFalse(passed)
        self.assertIn("93.3%", "\n".join(lines))
        self.assertIn("STOP", "\n".join(lines))

    def test_unclear_is_not_counted_in_the_precision_and_is_reported(self):
        kinds = ["real"] * 60 + ["control"] * 4
        lines, passed = self.evaluate(kinds, [("correct", [])] * 50 + [("unclear", [])] * 10 + [("incorrect", [])] * 4)
        self.assertTrue(passed)
        self.assertIn("unclear 10", "\n".join(lines))

    def test_a_lenient_reviewer_and_unanswered_items_fail_the_gate(self):
        kinds = ["real"] * 60 + ["control"] * 4
        lines, passed = self.evaluate(kinds, [("correct", [])] * 60 + [("correct", [])] * 2 + [("incorrect", [])] * 2)
        self.assertFalse(passed)
        self.assertIn("TOO LENIENT", "\n".join(lines))
        lines, passed = self.evaluate(kinds, [("correct", [])] * 60 + [("incorrect", [])] * 3 + [("correct", [])])
        self.assertTrue(passed)
        lines, passed = self.evaluate(kinds, [("correct", [])] * 60 + [("incorrect", [])] * 3)
        self.assertFalse(passed)
        self.assertIn("unanswered items: 1", "\n".join(lines))

    def test_whole_sentence_and_token_precision_are_reported_not_gated(self):
        kinds = ["real"] * 4
        lines, passed = self.evaluate(kinds, [("correct", []), ("correct", [2]), ("correct", [1, 3]), ("correct", [])])
        text = "\n".join(lines)
        self.assertTrue(passed)
        self.assertIn("whole-sentence precision (target correct and no other token marked wrong): 50.0%", text)
        self.assertIn("3 wrong of 20 tokens", text)

    def test_an_others_index_outside_the_sentence_is_an_error(self):
        with self.assertRaises(ValueError):
            self.evaluate(["real"], [("correct", [9])])

    def test_commands_sample_and_review_result_round_trip(self):
        out = os.path.join(self.temp, "out")
        common.write_corpus(os.path.join(out, "imperative.tsv"), idata.HEADER, self.sentences(12))
        code, printed, err = self.run_tool("sample", "--out-dir", out, "--size", "8", "--controls", "4")
        self.assertEqual(0, code, err)
        self.assertIn("12 items: 8 real, 4 control", printed)
        _, keys = adjudicate.read_records(os.path.join(self.root, "private", "review-sample-key.tsv"))
        verdicts = os.path.join(self.temp, "answers.txt")
        common.write_text(verdicts, ["%s\t%s\t-\tcite" % (k["item"], "incorrect" if k["kind"] == "control" else "correct") for k in keys])
        code, printed, err = self.run_tool("review-result", "--verdicts", verdicts)
        self.assertEqual(0, code, err)
        self.assertIn("gate: PASSED", printed)
        common.write_text(verdicts, ["%s\tcorrect\t-\tcite" % k["item"] for k in keys])
        code, printed, _ = self.run_tool("review-result", "--verdicts", verdicts)
        self.assertEqual(1, code)
        self.assertIn("TOO LENIENT", printed)
        common.write_text(verdicts, ["R999\tcorrect\t-\tcite"])
        code, _, err = self.run_tool("review-result", "--verdicts", verdicts)
        self.assertEqual(1, code)
        self.assertIn("unknown items", err)


class NoModelTests(unittest.TestCase):
    """The tool decides nothing with any model: it imports no tagger, weights or decoder, and names none in code."""

    def parsed(self):
        with open(idata.__file__, encoding="utf-8") as handle:
            return ast.parse(handle.read())

    def test_only_the_data_tools_are_imported(self):
        imported = set()
        for node in ast.walk(self.parsed()):
            if isinstance(node, ast.Import):
                imported |= {a.name.split(".")[0] for a in node.names}
            elif isinstance(node, ast.ImportFrom):
                imported.add((node.module or "").split(".")[0])
        allowed = {"argparse", "collections", "os", "random", "re", "sys", "adjudicate", "check_licenses", "common", "convention_rules", "corrections", "shapes_dev"}
        self.assertEqual(set(), imported - allowed)

    def test_no_string_of_the_code_names_a_model_file_or_runner(self):
        tree = self.parsed()
        docstring = ast.get_docstring(tree, clean=False)
        strings = [node.value for node in ast.walk(tree) if isinstance(node, ast.Constant) and isinstance(node.value, str) and node.value != docstring]
        joined = "\n".join(strings).lower()
        for forbidden in ("weights.bin", "dotnet", "partofspeech.trainer", "partofspeech.dll", "hmm", "eval-arm", ".dll", "wordsense", "probe.tsv", "decoder"):
            self.assertNotIn(forbidden, joined, forbidden)

    def test_the_held_out_sets_are_seen_only_through_the_builder_of_shapes_dev(self):
        with open(idata.__file__, encoding="utf-8") as handle:
            source = handle.read()
        self.assertNotIn("import leakage", source)
        self.assertNotIn("probe.tsv", source)

    def test_the_docstring_states_the_pre_registered_rule(self):
        doc = idata.__doc__
        for part in ("PRE-REGISTERED", "9)", "600", "900", "95 percent", "disputed_tokens == 0", "fix-tier", "80 percent", "No FAA", "model review, not a human review"):
            self.assertIn(part, doc)
        self.assertEqual((9, 6, 30, 600, 900), (idata.LEMMA_CAP_CANDIDATES, idata.LEMMA_CAP_BUILD, idata.DOCUMENT_CAP, idata.MIN_SENTENCES, idata.MAX_SENTENCES))
        self.assertEqual((1500, 250, 60, 4, 2300), (idata.MIN_CANDIDATES, idata.MIN_LEMMAS, idata.SAMPLE_SIZE, idata.SAMPLE_CONTROLS, idata.MAX_CANDIDATES))
        self.assertEqual(0.95, idata.PRECISION_MIN)


class NearDuplicateGateTests(unittest.TestCase):
    """check_leakage.check gates a near duplicate for the imperative training source only (requirement PartOfSpeech-Data-ImperativeIndependent)."""

    EVAL = "Remove the cover and then check the pressure valve before you start the work today"
    NEAR = EVAL.rsplit(" ", 1)[0] + " tomorrow"        # 15 words, one 6-gram of 10 differs: 90 percent contained, not an exact key

    def run_check(self, label, text):
        trains = {label: [("train-doc", text)]}
        evals = {"eval-set": [("eval-doc", self.EVAL)]}
        with mock.patch.object(check_leakage, "training_sets", return_value=trains), \
                mock.patch.object(check_leakage.leakage, "evaluation_sets", return_value=evals):
            return check_leakage.check(False)

    def test_an_imperative_near_duplicate_fails(self):
        rows, failures, _ = self.run_check("imperative", self.NEAR)
        self.assertEqual([("imperative", "eval-set", 1, 0, 0, 1)], rows)
        self.assertEqual(1, failures)

    def test_a_near_duplicate_of_another_training_source_is_only_reported(self):
        for label in ("open-train", "masc", "silver"):
            rows, failures, _ = self.run_check(label, self.NEAR)
            self.assertEqual([(label, "eval-set", 1, 0, 0, 1)], rows)
            self.assertEqual(0, failures, label)

    def test_an_exact_sentence_still_fails_for_every_training_source(self):
        self.assertEqual(1, self.run_check("open-train", self.EVAL)[1])
        self.assertGreaterEqual(self.run_check("imperative", self.EVAL)[1], 1)      # an exact sentence is also a near duplicate

    def test_an_unrelated_imperative_sentence_passes(self):
        rows, failures, _ = self.run_check("imperative", "Stop the pump when the oil level drops below the line on the tank gauge")
        self.assertEqual([("imperative", "eval-set", 1, 0, 0, 0)], rows)
        self.assertEqual(0, failures)


if __name__ == "__main__":
    unittest.main()

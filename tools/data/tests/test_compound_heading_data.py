"""Tests of compound_heading_data.py (the builder of the training sources data/compound/ and data/headings/) on a miniature data and work folder.

Run: python -m unittest discover -s tools/data/tests -p "test_compound_heading_data.py"   (developer-run; the pipeline needs no model or network)
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
import compound_heading_data as chd
import imperative_data as idata
import shapes_dev as sd
import shapes_dev_extra as sx
import test_shapes_dev as base

LEXICON = dict(base.LEXICON, washington="PROPN", nile="PROPN", gasket="NOUN", housing="NOUN", fuel="NOUN VERB", filter="NOUN VERB")
FRAME = [
    "Tank oil check 3 .",
    "Tank oil level 4 .",
    "Fuel tank seal 5 .",
    "Tank oil pump 6 .",
    "Oil tank test 7 .",
]
NOT_FRAME = [
    "The valve is full .",
    "Check the oil daily .",
    "The tank must stop the flow .",
]


def tags_of(tokens, head=None, head_tag="NOUN"):
    """Fixture tags: closed words and numbers by shape, token `head` the given tag, every other token NOUN."""
    out = []
    for i, token in enumerate(tokens):
        if i == head:
            out.append(head_tag)
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
    """The miniature folders of test_shapes_dev plus helpers for the compound and heading pipelines."""

    def write_data(self):
        super().write_data()
        d = self.data
        common.write_text(os.path.join(d, "lexicon", "lexicon.tsv"), ["# fixture lexicon"] + ["%s\t%s" % (k, v) for k, v in sorted(LEXICON.items())])
        base.corpus_file(os.path.join(d, "eval", "shapes-dev", "dev.tsv"), [("army-dev-doc", "Open the access door and inspect the seal before each flight today .")])
        base.corpus_file(os.path.join(d, "eval", "shapes-dev", "compound-head.tsv"), [("army-ch-doc", "Fuel filter check was done twice before the long flight started .")])
        base.corpus_file(os.path.join(d, "eval", "shapes-dev", "headings.tsv"), [("army-hd-doc", "Pump Seal Inspection")])
        base.table_file(os.path.join(d, "eval", "shapes-dev", "sources.tsv"), sd.SOURCE_COLUMNS,
                        [self.source_row("army-dev-doc", "https://archive.org/download/DevDoc/DevDoc_djvu.txt")])
        base.table_file(os.path.join(d, "eval", "shapes-dev", "compound-head-sources.tsv"), sd.SOURCE_COLUMNS,
                        [self.source_row("army-ch-doc", "https://archive.org/download/ChDoc/ChDoc_djvu.txt")])
        base.table_file(os.path.join(d, "imperative", "sources.tsv"), sd.SOURCE_COLUMNS,
                        [self.source_row("army-imp-doc", "https://archive.org/download/ImpDoc/ImpDoc_djvu.txt")])

    def sound_draft(self, documents, lines=None):
        """Write a sound draft (text files with their hashes and counts), heading lines and tokens.tsv for `documents` {id: [sentences]} and `lines` {id: [lines]}."""
        rows = []
        for number, (source_id, sentences) in enumerate(sorted(documents.items()), 1):
            self.write_text_doc(source_id, sentences)
            row = self.source_row(source_id, "https://archive.org/download/New%d/New%d_djvu.txt" % (number, number), sentences=str(len(sentences)))
            row[10] = common.sha256_file(os.path.join(self.root, "text", source_id + ".txt"))
            rows.append("\t".join(row))
        for source_id in sorted(set(lines or {}) - set(documents)):
            self.write_text_doc(source_id, ["Check the valve 1 ."])
            row = self.source_row(source_id, "https://archive.org/download/Line%s/Line%s_djvu.txt" % (source_id, source_id), sentences="1")
            row[10] = common.sha256_file(os.path.join(self.root, "text", source_id + ".txt"))
            rows.append("\t".join(row))
        for source_id, heads in (lines or {}).items():
            common.write_text(os.path.join(self.root, "lines", source_id + ".txt"), heads)
        common.write_text(os.path.join(self.root, "sources-draft.tsv"), ["\t".join(sd.SOURCE_COLUMNS)] + rows)
        texts = [t for sentences in documents.values() for t in sentences] + [t for heads in (lines or {}).values() for t in heads]
        texts += ["Check the valve 1 ."] * 0
        common.write_text(os.path.join(self.root, "tokens.tsv"), ["text\tsentences\ttokens"]
                          + ["%s\t1\t%s" % (t, " ".join(base.simple_tokens(t))) for t in sorted(set(texts) | {"Check the valve 1 ."})])

    def run_tool(self, *args):
        out, err = io.StringIO(), io.StringIO()
        with contextlib.redirect_stdout(out), contextlib.redirect_stderr(err):
            with mock.patch.object(sd, "noise_reason", return_value=None), mock.patch.object(sx, "heading_noise_reason", return_value=None):
                code = chd.main(["--work", self.root, "--data-dir", self.data] + list(args))
        return code, out.getvalue(), err.getvalue()

    def decided(self, source, specs):
        """Write the candidates and the decided items and rows of the round of `source` from specs: dicts with text, lemma, source, target and optional
        shape, outcome, reason, agreed, tags, statuses, disputed."""
        self.write_draft(sorted({s["source"] for s in specs}))
        rw = sd.RoundWork(self.root, source)
        os.makedirs(rw.private, exist_ok=True)
        rows, items, token_rows = [], [], []
        for number, spec in enumerate(specs, 1):
            tokens = base.simple_tokens(spec["text"])
            shape = spec.get("shape", chd.COMPOUND_SHAPE)
            target = spec["target"]
            cand = dict(cand_id="%sc%05d" % (chd.CAND_PREFIX[source], number), shape=shape, role="target", target=target, lemma=spec["lemma"],
                        origin="real", source_id=spec["source"], line=number, text=spec["text"], tokens=tokens)
            rows.append(cand)
            key = sd.sent_key_of(spec["text"])
            tags = spec.get("tags") or tags_of(tokens, target, spec.get("head_tag", "NOUN"))
            statuses = spec.get("statuses") or ["unanimous"] * len(tokens)
            disputed = sum(1 for s in statuses if s == "disputed")
            outcome = spec.get("outcome", "keep")
            agreed = spec.get("agreed", tags[target] if outcome == "keep" else "")
            items.append([cand["cand_id"], key, shape, "target", str(target), spec["lemma"], "real", spec["source"], outcome, spec.get("reason", ""),
                          tags[target], tags[target], agreed, str(spec.get("disputed", disputed))])
            token_rows += [(key, i, tokens[i], tags[i], statuses[i]) for i in range(len(tokens))]
        sd.write_candidates(rw, rows)
        adjudicate.write_rows(rw.private_file("decided.tsv"), sd.ITEM_COLUMNS, items)
        adjudicate.write_rows(rw.private_file("decided-rows.tsv"), sd.ROW_COLUMNS, token_rows)

    def build(self, source, specs, **options):
        self.decided(source, specs)
        out = os.path.join(self.temp, "out")
        with mock.patch.object(chd, "draft_gate"):
            built, report = chd.build(self.work, source, out, data_dir=self.data, **options)
        return built, report, out


def drops_of(report):
    """{reason: count} of the table `| dropped because | items |` of a build report."""
    found = {}
    for line in report:
        match = re.match(r"\| ([a-z-]+) \| ([0-9]+) \|$", line)
        if match:
            found[match.group(1)] = int(match.group(2))
    return found


def cspec(text, lemma, source="army-a", target=2, **extra):
    return dict(text=text, lemma=lemma, source=source, target=target, **extra)


def cmany(lemma, count, source="army-a", start=1, **extra):
    """`count` compound specs of one head lemma: `Tank oil <lemma> N .` with the head at index 2."""
    return [cspec("Tank oil %s %d ." % (lemma, start + n), lemma, source, **extra) for n in range(count)]


def hspec(text, lemma, shape, source="army-a", target=0, tags=None, **extra):
    return dict(text=text, lemma=lemma, source=source, target=target, shape=shape, **dict(extra, **({"tags": tags} if tags else {})))


def htags(tokens, shape, target):
    """Fixture tags of a heading: the stratum word has its exact tag, the rest NOUN."""
    tag = {"heading-noun": "NOUN", "heading-imperative": "VERB", "heading-proper": "PROPN"}[shape]
    return [tag if i == target else "NOUN" for i, _ in enumerate(tokens)]


def hmany(lemma, word, shape, count, source="army-a", start=1, target=0):
    out = []
    for n in range(count):
        text = "%s Valve %d" % (word, start + n)
        out.append(hspec(text, lemma, shape, source, target, htags(base.simple_tokens(text), shape, target)))
    return out


class IndexTests(Mini):
    def test_index_has_the_imperative_and_all_shapes_dev_tables_and_extra_tables_but_not_its_own_output(self):
        extra = os.path.join(self.temp, "draft.tsv")
        base.table_file(extra, sd.SOURCE_COLUMNS, [self.source_row("army-extra", "https://archive.org/download/ExtraId/ExtraId_djvu.txt")])
        os.makedirs(os.path.join(self.data, "compound"), exist_ok=True)
        base.table_file(os.path.join(self.data, "compound", "sources.tsv"), sd.SOURCE_COLUMNS,
                        [self.source_row("army-own", "https://archive.org/download/OwnId/OwnId_djvu.txt")])
        index = chd.used_index(self.data, sd.load_exclusions(self.work, self.data), [extra])
        self.assertIsNotNone(idata.document_rejection(index, "army-dev-doc"))
        self.assertIsNotNone(idata.document_rejection(index, "army-ch-doc"))                                          # a newer tranche
        self.assertIsNotNone(idata.document_rejection(index, "army-imp-doc"))                                         # the imperative source
        self.assertIsNotNone(idata.document_rejection(index, "x", "https://archive.org/details/impdoc"))
        self.assertIn("extra:draft.tsv", idata.document_rejection(index, "army-extra"))
        self.assertIsNotNone(idata.document_rejection(index, "gutenberg-1023"))                                        # a training document
        self.assertIsNone(idata.document_rejection(index, "army-own", "https://archive.org/download/OwnId/OwnId_djvu.txt"))   # a rebuild never rejects itself
        self.assertIsNone(idata.document_rejection(index, "army-new", "https://archive.org/download/NewOne/NewOne_djvu.txt"))

    def test_the_draft_gate_is_the_source_and_license_gate(self):
        self.sound_draft({"army-a": FRAME})
        chd.draft_gate(self.work, self.data)
        self.sound_draft({"army-imp-doc": FRAME})
        with self.assertRaises(sx.DraftError):
            chd.draft_gate(self.work, self.data)
        self.sound_draft({"army-a": FRAME})
        rows = common.read_lines(os.path.join(self.root, "sources-draft.tsv"))
        common.write_text(os.path.join(self.root, "sources-draft.tsv"), [rows[0], rows[1].replace("US federal government work", "CC BY 4.0")])
        with self.assertRaises(sx.DraftError):
            chd.draft_gate(self.work, self.data)

    def test_exclusions_command_exit_codes(self):
        self.sound_draft({"army-imp-doc": FRAME})
        code, out, _ = self.run_tool("exclusions")
        self.assertEqual(1, code)
        self.assertIn("REJECT army-imp-doc", out)
        self.assertTrue(os.path.exists(os.path.join(self.root, "exclusions.tsv")))
        code, out, _ = self.run_tool("exclusions", "--check", "new-doc", "https://archive.org/details/ChDoc")
        self.assertIn("ACCEPT: new-doc", out)
        self.assertIn("REJECT: https://archive.org/details/ChDoc", out)

    def test_the_reference_holds_all_tranches_and_the_imperative_source(self):
        reference = chd.Reference(self.data)
        self.assertEqual(idata.EQUALS_SHAPES_DEV, reference.reason("Fuel filter check was done twice before the long flight started ."))      # newer tranche
        self.assertEqual(idata.EQUALS_SHAPES_DEV, reference.reason("Inspect the fuel strainer before each long trip ."))                          # imperative.tsv
        self.assertEqual(idata.EQUALS_SHAPES_DEV, reference.reason("Open the access door and inspect the seal before each flight today ."))
        self.assertIsNone(reference.reason("Tank oil check 3 ."))


class CandidateTests(Mini):
    def candidates(self, documents, lines=None, source="compound", *options):
        self.sound_draft(documents, lines)
        return self.run_tool("candidates", "--source", source, "--no-ud", "--min-candidates", "1", "--min-lemmas", "1", *options)

    def read(self, source):
        _, records = adjudicate.read_records(os.path.join(self.root, "rounds", source, "candidates.tsv"))
        return records

    def test_only_the_compound_frame_is_a_candidate_with_unique_ids(self):
        code, out, err = self.candidates({"army-a": FRAME + NOT_FRAME})
        self.assertEqual(0, code, err)
        rows = self.read("compound")
        self.assertEqual(sorted(FRAME), sorted(r["text"] for r in rows))
        self.assertEqual({chd.COMPOUND_SHAPE}, {r["shape"] for r in rows})
        self.assertEqual({"target"}, {r["role"] for r in rows})
        self.assertEqual({"2"}, {r["target"] for r in rows})
        self.assertEqual(["cm-c%05d" % n for n in range(1, len(rows) + 1)], [r["cand_id"] for r in rows])
        self.assertEqual({"check", "level", "seal", "pump", "test"}, {r["lemma"] for r in rows})
        self.assertTrue(os.path.exists(os.path.join(self.root, "rounds", "compound", "private", "candidates-report.md")))

    def test_the_seeded_order_decides_the_lemma_cap_not_the_document_order(self):
        sentences = ["Tank oil check %d ." % n for n in range(1, 11)] + ["Tank oil seal 1 ."]
        self.candidates({"army-a": sentences}, None, "compound", "--lemma-cap", "3")
        taken = sorted(r["text"] for r in self.read("compound") if r["lemma"] == "check")
        self.assertEqual(3, len(taken))
        expected = sorted(("Tank oil check %d ." % n for n in range(1, 11)), key=lambda t: sd.rank_of(sd.SEED, "compound", sd.sent_key_of(t)))[:3]
        self.assertEqual(sorted(expected), taken)
        first = [r["text"] for r in self.read("compound")]
        self.candidates({"army-a": list(reversed(sentences))}, None, "compound", "--lemma-cap", "3")
        self.assertEqual(first, [r["text"] for r in self.read("compound")])

    def test_the_total_cap_takes_the_first_candidates_in_the_seeded_order(self):
        sentences = ["Tank oil %s %d ." % (word, n) for n, word in enumerate(["check", "level", "seal", "pump", "test", "check", "level", "seal"], 1)]
        self.candidates({"army-a": sentences}, None, "compound", "--max-candidates", "4")
        ranked = sorted(sentences, key=lambda t: sd.rank_of(sd.SEED, "compound", sd.sent_key_of(t)))
        self.assertEqual(sorted(ranked[:4]), sorted(r["text"] for r in self.read("compound")))

    def test_a_sentence_that_is_a_heading_line_of_the_pool_is_dropped(self):
        self.candidates({"army-a": FRAME}, {"army-a": ["Tank Oil Check 3"]})
        texts = [r["text"] for r in self.read("compound")]
        self.assertNotIn("Tank oil check 3 .", texts)           # only by key: the heading has no final mark, so the key differs and the sentence stays
        self.candidates({"army-a": FRAME}, {"army-a": ["Tank Oil Check 3 ."]})
        self.assertNotIn("Tank oil check 3 .", [r["text"] for r in self.read("compound")])
        self.assertEqual(4, len(self.read("compound")))

    def test_sentences_of_the_shapes_dev_tranches_and_the_imperative_source_are_dropped_and_counted(self):
        equal_new = "Fuel filter check was done twice before the long flight started ."
        equal_imp = "Inspect the fuel strainer before each long trip ."
        self.candidates({"army-a": FRAME + [equal_new, equal_imp]})
        texts = [r["text"] for r in self.read("compound")]
        self.assertNotIn(equal_new, texts)
        report = "\n".join(common.read_lines(os.path.join(self.root, "rounds", "compound", "private", "candidates-report.md")))
        self.assertIn(idata.EQUALS_SHAPES_DEV + " 2", report)

    def test_a_reprint_of_an_earlier_text_is_rejected_as_a_whole(self):
        folder = os.path.join(self.temp, "earlier")
        common.write_text(os.path.join(folder, "old.txt"), FRAME[:3])
        code, out, err = self.candidates({"army-a": FRAME[:3] + ["Tank oil flow 9 ."]}, None, "compound", "--extra-text", folder)
        self.assertEqual(1, code)                       # the document is rejected, so no candidate remains and the gate fails
        self.assertEqual([], self.read("compound"))
        self.assertIn("army-a", "\n".join(common.read_lines(os.path.join(self.root, "rounds", "compound", "private", "rejected-documents.tsv"))))

    def test_a_probe_5gram_is_dropped_without_printing_probe_text(self):
        copy = "Tank oil check with the gasket compound before fitting 3 ."
        self.candidates({"army-a": FRAME + ["Seal the pump housing with the gasket compound before fitting tank oil check 3 ."]})
        report = "\n".join(common.read_lines(os.path.join(self.root, "rounds", "compound", "private", "candidates-report.md")))
        self.assertIn("probe-5gram 1", report)
        self.assertNotIn("gasket", report)
        self.assertNotIn(copy, [r["text"] for r in self.read("compound")])

    def test_the_size_gate_fails_closed(self):
        self.sound_draft({"army-a": FRAME})
        code, out, err = self.run_tool("candidates", "--source", "compound", "--no-ud")
        self.assertEqual(1, code)
        self.assertIn("GATE G3 FAILED", err)
        self.assertEqual([], chd.gate_messages("compound", dict(candidates=1200, lemmas=250)))
        self.assertEqual(2, len(chd.gate_messages("compound", dict(candidates=10, lemmas=3))))
        self.assertEqual([], chd.gate_messages("headings", dict(candidates=1000, strata=dict(noun=1, imperative=1, proper=1))))
        self.assertEqual(3, len(chd.gate_messages("headings", dict(candidates=10, strata=dict(noun=1, imperative=0, proper=0)))))

    def test_an_unsound_draft_refuses_every_command(self):
        self.write_draft(["army-a"])
        for command in (("candidates", "--source", "compound"), ("preflight",)):
            code, _, err = self.run_tool(*command)
            self.assertEqual(1, code)
            self.assertIn("not sound", err)

    def test_heading_strata_one_candidate_per_line_rarest_stratum_first(self):
        heads = ["Check Pressure Valve", "Pump Seal", "Valve Seal", "Valve Washington", "Check Valve Washington", "Valve Nile"]
        code, out, err = self.candidates({"army-a": FRAME[:1]}, {"army-a": heads}, "headings")
        self.assertEqual(0, code, err)
        got = {r["text"]: r["shape"] for r in self.read("headings")}
        self.assertEqual({"Check Pressure Valve": "heading-imperative", "Pump Seal": "heading-imperative", "Valve Seal": "heading-noun",
                          "Valve Washington": "heading-proper", "Check Valve Washington": "heading-proper", "Valve Nile": "heading-proper"}, got)
        self.assertEqual({"target"}, {r["role"] for r in self.read("headings")})
        self.assertEqual(["hd-c%05d" % n for n in range(1, 7)], [r["cand_id"] for r in self.read("headings")])
        report = "\n".join(common.read_lines(os.path.join(self.root, "rounds", "headings", "private", "candidates-report.md")))
        self.assertIn("| proper | 3 |", report)
        self.assertIn("line-in-a-rarer-stratum", report)

    def test_heading_caps_lemma_document_and_total(self):
        heads = ["Check Valve %d Seal" % n for n in range(1, 7)] + ["Test Valve %d Seal" % n for n in range(1, 4)]
        self.candidates({"army-a": FRAME[:1]}, {"army-a": heads}, "headings", "--lemma-cap", "2")
        rows = self.read("headings")
        counts = {}
        for r in rows:
            counts[r["lemma"]] = counts.get(r["lemma"], 0) + 1
        self.assertTrue(all(n <= 2 for n in counts.values()), counts)
        self.candidates({"army-a": FRAME[:1]}, {"army-a": heads}, "headings", "--document-cap", "3")
        self.assertEqual(3, len(self.read("headings")))
        self.candidates({"army-a": FRAME[:1], "army-b": FRAME[:1]}, {"army-a": heads[:5], "army-b": heads[5:]}, "headings", "--max-candidates", "4", "--lemma-cap", "9")
        self.assertEqual(4, len(self.read("headings")))

    def test_preflight_prints_counts_only(self):
        self.sound_draft({"army-a": FRAME + NOT_FRAME}, {"army-a": ["Check Pressure Valve", "Valve Seal", "Valve Washington"]})
        code, out, err = self.run_tool("preflight", "--no-ud")
        self.assertEqual(0, code, err)
        self.assertIn("frame hits: 5 over 5 head lemmas", out)
        self.assertIn("frame hits (noun, imperative, proper): 1, 1, 1", out)
        self.assertIn("BUDGET NOT MET", out)
        for text in FRAME + ["Valve Seal"]:
            self.assertNotIn(text, out)


class BuildCompoundTests(Mini):
    def test_kept_sentences_are_written_in_the_corpus_format_with_sources(self):
        specs = cmany("check", 3) + cmany("seal", 2, "army-b", 10)
        built, report, out = self.build("compound", specs, allow_short=True)
        self.assertEqual(5, len(built))
        header, sentences = common.read_corpus(os.path.join(out, "compound.tsv"))
        self.assertEqual(["compound-%04d" % n for n in range(1, 6)], [s["sent_id"] for s in sentences])
        self.assertTrue(all(s["rows"][int(s["comments"]["head"])][1] == "NOUN" and s["comments"]["kind"] == "noun" for s in sentences))
        self.assertTrue(all(row[2] == "unanimous" for s in sentences for row in s["rows"]))
        self.assertIn("# use = training", header)
        self.assertTrue(any(line.startswith("# corpus = ") for line in header))
        _, records = adjudicate.read_records(os.path.join(out, "sources.tsv"))
        self.assertEqual(["army-a", "army-b"], [r["source_id"] for r in records])
        self.assertEqual(["3", "2"], [r["sentences"] for r in records])
        self.assertTrue(os.path.exists(os.path.join(out, "excluded-documents.tsv")))
        self.assertTrue(os.path.exists(os.path.join(out, "dropped-sentences.tsv")))
        self.assertIn("| us-government | 2 | 5 |", report)

    def test_drop_reasons_disputed_class_mismatch_frame_and_other_readings(self):
        specs = [cspec("Tank oil check 1 .", "check"),
                 cspec("Tank oil check 2 .", "check", statuses=["unanimous", "disputed", "unanimous", "unanimous", "unanimous"]),
                 cspec("Tank oil check 3 .", "check", outcome="drop", reason="class-mismatch", agreed="ADJ"),
                 cspec("Tank oil check 4 .", "check", outcome="drop", reason="frame-mismatch"),
                 cspec("Tank oil check 5 .", "check", outcome="drop", reason="target-disagreement"),
                 cspec("Tank oil check 6 .", "check", head_tag="ADJ")]
        built, report, _ = self.build("compound", specs, allow_short=True)
        self.assertEqual(["Tank oil check 1 ."], [s["text"] for s in built])
        self.assertEqual({"disputed-token": 1, "class-mismatch": 2, "frame-mismatch": 1, "target-disagreement": 1}, drops_of(report))

    def test_a_head_that_both_labelers_read_as_a_verb_after_an_agreed_noun_is_a_verb_counter_example(self):
        item = dict(outcome="drop", reason="class-mismatch", agreed_tag="VERB", disputed_tokens=0, target=2)
        rows = [("Tank", "NOUN", "unanimous"), ("oil", "NOUN", "unanimous"), ("check", "VERB", "unanimous"), (".", "PUNCT", "unanimous")]
        self.assertEqual(("verb", None), chd.compound_kind(item, rows))
        self.assertEqual((None, "frame-mismatch"), chd.compound_kind(item, [rows[0], ("oil", "DET", "unanimous")] + rows[2:]))       # the word before is no noun
        self.assertEqual((None, "frame-mismatch"), chd.compound_kind(item, [rows[0], ("oil", "NOUN", "disputed")] + rows[2:]))        # they disagree on it
        self.assertEqual((None, "disputed-token"), chd.compound_kind(dict(item, disputed_tokens=1), rows))
        self.assertEqual((None, "frame-mismatch"), chd.compound_kind(dict(item, target=0), rows))
        self.assertEqual((None, "class-mismatch"), chd.compound_kind(dict(item, agreed_tag="ADJ"), rows))
        self.assertEqual(("noun", None), chd.compound_kind(dict(item, outcome="keep", reason=""), [rows[0], rows[1], ("check", "PROPN", "unanimous"), rows[3]]))

    def test_the_verb_share_is_at_most_fifteen_percent_and_the_surplus_is_dropped(self):
        specs = [cspec("Tank oil seal %d ." % n, "seal%d" % n, "army-%d" % (n % 4)) for n in range(1, 18)]
        specs += [cspec("Tank oil check %d ." % n, "check%d" % n, "army-%d" % (n % 4), outcome="drop", reason="class-mismatch", agreed="VERB", head_tag="VERB")
                  for n in range(1, 8)]
        built, report, _ = self.build("compound", specs, allow_short=True)
        kinds = [s["comments"]["kind"] if "comments" in s else dict(p[2:].split(" = ", 1) for p in s["extra"])["kind"] for s in built]
        self.assertEqual(17, kinds.count("noun"))
        self.assertEqual(17 * 15 // 85, kinds.count("verb"))                       # 3 of 20 = 15.0 percent
        self.assertLessEqual(100 * kinds.count("verb"), 15 * len(built))
        self.assertEqual({"verb-share": 4}, drops_of(report))
        chosen, drops = chd.select_compound([dict(sent_key="k%d" % n, lemma="l%d" % n, source_id="d", kind="noun" if n < 5 else "verb") for n in range(10)],
                                            maximum=100)
        self.assertEqual(5 * 15 // 85, sum(1 for c in chosen if c["kind"] == "verb"))

    def test_a_fix_tier_violation_drops_the_sentence_and_nothing_is_changed_by_hand(self):
        bad = cspec("Check the valve not the tank .", "check", target=2, tags=["VERB", "DET", "NOUN", "ADV", "DET", "NOUN", "PUNCT"])
        built, report, _ = self.build("compound", [bad, cspec("Tank oil check 1 .", "check")], allow_short=True)
        self.assertEqual(["Tank oil check 1 ."], [s["text"] for s in built])
        self.assertEqual({"fix-tier": 1}, drops_of(report))

    def test_the_lint_resources_never_come_from_the_new_sources_themselves(self):
        specs = [cspec("Check the valve not the tank .", "check", target=2, tags=["VERB", "DET", "NOUN", "ADV", "DET", "NOUN", "PUNCT"]),
                 cspec("Tank oil check 1 .", "check")]
        _, without, _ = self.build("compound", specs, allow_short=True)
        previous = [dict(sent_id="compound-0001", source_id="army-old", text="The valve is very closed .",
                         rows=[["The", "DET", "unanimous"], ["valve", "NOUN", "unanimous"], ["is", "AUX", "unanimous"],
                               ["very", "ADV", "unanimous"], ["closed", "ADJ", "unanimous"], [".", "PUNCT", "unanimous"]])]
        os.makedirs(os.path.join(self.data, "compound"), exist_ok=True)
        common.write_corpus(os.path.join(self.data, "compound", "compound.tsv"), ["# corpus = a previous output"], previous)
        seen = []
        real = chd.convention_rules.build_resources

        def spy(sentences):
            sentences = list(sentences)
            seen.extend(sentences)
            return real(sentences)

        with mock.patch.object(chd.convention_rules, "build_resources", spy):
            _, with_previous, _ = self.build("compound", specs, allow_short=True)
        self.assertEqual(drops_of(without), drops_of(with_previous))
        self.assertTrue(seen)
        self.assertEqual(set(), {s.source for s in seen} & {"imperative", "compound", "headings", "ste"})
        self.assertNotIn("closed", {w for s in seen for w in s.w})

    def test_caps_in_seeded_order_lemma_document_noun_and_total(self):
        specs = cmany("check", 8) + cmany("seal", 8, "army-b", 20) + cmany("level", 4, "army-c", 40)
        built, report, _ = self.build("compound", specs, allow_short=True, seed=chd.SEED)
        self.assertEqual(4 + 4 + 4, len(built))                                     # 4 per lemma
        self.assertEqual({"lemma-cap": 8}, drops_of(report))
        specs = [cspec("Tank oil seal %d ." % n, "seal%d" % n, "army-a") for n in range(30)]
        chosen, drops = chd.select_compound([dict(sent_key="k%d" % n, lemma="l%d" % n, source_id="d", kind="noun") for n in range(30)], document_cap=7)
        self.assertEqual((7, 23), (len(chosen), drops["document-cap"]))
        self.assertEqual(7, len({id(c) for c in chosen}))
        chosen, drops = chd.select_compound([dict(sent_key="k%d" % n, lemma="l%d" % n, source_id="d%d" % n, kind="noun") for n in range(30)],
                                            maximum=10, noun_cap=20)
        self.assertEqual((10, 20), (len(chosen), drops["total-cap"]))
        chosen, drops = chd.select_compound([dict(sent_key="k%d" % n, lemma="l%d" % n, source_id="d%d" % n, kind="noun") for n in range(30)], noun_cap=12)
        self.assertEqual((12, 18), (len(chosen), drops["noun-cap"]))

    def test_which_sentences_a_cap_keeps_depends_only_on_the_seeded_rank(self):
        specs = cmany("check", 8)
        first, _, _ = self.build("compound", specs, allow_short=True)
        second, _, _ = self.build("compound", list(reversed(specs)), allow_short=True)
        self.assertEqual(sorted(s["text"] for s in first), sorted(s["text"] for s in second))
        expected = sorted((s["text"] for s in specs), key=lambda t: sd.rank_of(sd.SEED, "compound-build", sd.sent_key_of(t)))[:chd.CMP_LEMMA_CAP]
        self.assertEqual(sorted(expected), sorted(s["text"] for s in first))

    def test_the_quota_gate_refuses_below_the_minimum_and_writes_nothing(self):
        self.decided("compound", cmany("check", 3))
        out = os.path.join(self.temp, "out")
        with mock.patch.object(chd, "draft_gate"):
            with self.assertRaises(chd.BuildError):
                chd.build(self.work, "compound", out, data_dir=self.data)
            self.assertFalse(os.path.exists(out))
            code, _, err = self.run_tool("build", "--source", "compound", "--out-dir", out)
            self.assertEqual(1, code)
            self.assertIn("build refused", err)
            self.assertFalse(os.path.exists(out))
            code, _, err = self.run_tool("build", "--source", "compound", "--out-dir", out, "--allow-short")
            self.assertEqual(0, code, err)
        self.assertTrue(os.path.exists(os.path.join(out, "compound.tsv")))

    def test_the_quota_problems_list_every_miss(self):
        def sentence(number, source_id, lemma, kind, tag):
            return dict(sent_id="compound-%04d" % number, source_id=source_id, text="Oil check .",
                        rows=[["Oil", "NOUN", "unanimous"], ["check", tag, "unanimous"], [".", "PUNCT", "disputed"]],
                        extra=["# head = 1", "# lemma = " + lemma, "# kind = " + kind])
        good = [sentence(n, "d%d" % n, "l%d" % n, "noun", "NOUN") for n in range(1, 4)]
        self.assertEqual([p for p in chd.quota_problems("compound", good, 1) if "not unanimous" not in p and "head lemmas" not in p], [])
        bad = [sentence(1, "d", "a", "noun", "VERB"), sentence(2, "d", "a", "verb", "VERB"), sentence(3, "d", "a", "verb", "VERB")]
        found = "\n".join(chd.quota_problems("compound", bad, 5, document_cap=2, lemma_cap=2))
        for part in ("at least 5", "at least 200 needed", "verb items of 3", "document d has 3", "lemma a has 3", "tagged VERB for kind noun", "is not unanimous"):
            self.assertIn(part, found)
        self.assertIn("do not rejoin", "\n".join(chd.quota_problems("compound", [dict(good[0], text="Oil checks .")], 1)))
        self.assertIn("at most 800", "\n".join(chd.quota_problems("compound", good * 300, 1, document_cap=900, lemma_cap=900)))

    def test_an_omitted_document_is_removed_after_the_caps_nothing_is_added_and_the_ids_have_no_gap(self):
        specs = cmany("check", 3) + cmany("seal", 3, "army-b", 10) + cmany("level", 2, "army-c", 20)
        plain, _, _ = self.build("compound", specs, allow_short=True)
        built, report, out = self.build("compound", specs, allow_short=True, omit=["army-b"])
        self.assertEqual(sorted(s["text"] for s in plain if s["source_id"] != "army-b"), sorted(s["text"] for s in built))   # removal only
        self.assertEqual(5, len(built))
        self.assertEqual({"omitted-document": 3}, drops_of(report))
        _, sentences = common.read_corpus(os.path.join(out, "compound.tsv"))
        self.assertEqual(["compound-%04d" % n for n in range(1, 6)], [s["sent_id"] for s in sentences])
        with open(os.path.join(out, "sources.tsv"), encoding="utf-8") as handle:
            self.assertNotIn("army-b", handle.read())
        rejected = common.read_lines(os.path.join(out, "excluded-documents.tsv"))
        self.assertIn("army-b\t" + chd.OMIT_REASON, rejected)

    def test_omitting_an_unknown_document_is_refused_and_the_command_line_passes_the_ids(self):
        self.decided("compound", cmany("check", 3))
        out = os.path.join(self.temp, "out")
        with mock.patch.object(chd, "draft_gate"):
            with self.assertRaises(chd.BuildError):
                chd.build(self.work, "compound", out, data_dir=self.data, allow_short=True, omit=["no-such-doc"])
            self.assertFalse(os.path.exists(out))
            code, _, err = self.run_tool("build", "--source", "compound", "--out-dir", out, "--allow-short", "--omit-document", "no-such-doc")
            self.assertEqual(1, code)
            self.assertIn("cannot omit", err)
        self.assertFalse(os.path.exists(os.path.join(out, "compound.tsv")))

    def test_omitting_documents_trims_the_verb_share_again_and_never_adds_an_item(self):
        chosen = [dict(sent_key="k%d" % n, kind="noun" if n < 17 else "verb", source_id="d") for n in range(20)]
        kept, dropped = chd.trim_verb_share(chosen)                       # 17 nouns allow 17 * 15 // 85 = 3 verbs
        self.assertEqual((20, 0), (len(kept), dropped))
        kept, dropped = chd.trim_verb_share([c for c in chosen if c["sent_key"] != "k0"])        # 16 nouns allow 2 verbs: the last verb goes
        self.assertEqual((18, 1), (len(kept), dropped))
        self.assertEqual(["k17", "k18"], [c["sent_key"] for c in kept if c["kind"] == "verb"])

    def test_an_excluded_document_cannot_be_built_in(self):
        common.write_text(os.path.join(self.data, "eval", "shapes-dev", "excluded-sources.tsv"),
                          ["\t".join(common.EXCLUDED_SOURCES_COLUMNS), "army-a\thttps://example.gov/army-a\tlicense doubt\t2026-11-06"])
        built, report, _ = self.build("compound", cmany("check", 2), allow_short=True)
        self.assertEqual([], built)                                    # the items of an excluded document are dropped by the decision reader

    def read_all(self, folder):
        found = {}
        for name in os.listdir(folder):
            with open(os.path.join(folder, name), "rb") as handle:
                found[name] = handle.read()
        return found

    def test_the_build_is_deterministic(self):
        specs = cmany("check", 4) + cmany("seal", 4, "army-b", 10)
        _, _, out = self.build("compound", specs, allow_short=True)
        first = self.read_all(out)
        _, _, out = self.build("compound", specs, allow_short=True)
        self.assertEqual(first, self.read_all(out))
        data = first["compound.tsv"]
        self.assertTrue(data.endswith(b"\n") and b"\r" not in data and not data.startswith(b"\xef\xbb\xbf"))

    def test_flags_that_both_labelers_agree_on_are_kept_with_their_documentation(self):
        statuses = ["unanimous", "unanimous", "unanimous", "unanimous+mentioned", "unanimous"]
        built, _, out = self.build("compound", [cspec("Tank oil check 1 .", "check", statuses=statuses)], allow_short=True)
        header, sentences = common.read_corpus(os.path.join(out, "compound.tsv"))
        self.assertEqual("unanimous+mentioned", sentences[0]["rows"][3][2])
        self.assertTrue(any(line.startswith("# status flags = ") for line in header))

    def test_report_lists_families_kinds_lexicon_coverage_and_counts(self):
        _, _, out = self.build("compound", cmany("check", 3), allow_short=True)
        text = "\n".join(chd.report_lines(out, self.data, "compound"))
        self.assertIn("sentences: 3", text)
        self.assertIn("kinds: noun 3", text)
        self.assertIn("lexicon coverage", text)
        code, printed, _ = self.run_tool("report", "--source", "compound", "--out-dir", out)
        self.assertEqual(0, code)
        self.assertIn("| us-government | 1 | 3 |", printed)


class BuildHeadingTests(Mini):
    def test_kept_lines_carry_their_stratum_and_the_exact_tags(self):
        specs = (hmany("check", "Check", "heading-imperative", 2) + hmany("seal", "Seal", "heading-noun", 2, "army-b", 10, 1)
                 + hmany("nile", "Nile", "heading-proper", 1, "army-b", 20, 1))
        built, report, out = self.build("headings", specs, allow_short=True)
        self.assertEqual(5, len(built))
        _, sentences = common.read_corpus(os.path.join(out, "headings.tsv"))
        self.assertEqual(["headings-%04d" % n for n in range(1, 6)], [s["sent_id"] for s in sentences])
        by_stratum = {s["comments"]["stratum"]: s["rows"][int(s["comments"]["word"])][1] for s in sentences}
        self.assertEqual({"imperative": "VERB", "noun": "NOUN", "proper": "PROPN"}, by_stratum)
        self.assertEqual(["Heading" if False else "headings"], ["headings"])
        self.assertIn("strata: imperative 2, noun 2, proper 1", "\n".join(report))

    def test_drop_reasons_class_mismatch_disputed_and_disagreement(self):
        good = hmany("check", "Check", "heading-imperative", 1)[0]
        specs = [good,
                 dict(hspec("Check Valve 2", "check", "heading-imperative", outcome="drop", reason="class-mismatch", agreed="NOUN")),
                 dict(hspec("Check Valve 3", "check", "heading-imperative", outcome="drop", reason="target-disagreement")),
                 dict(hspec("Check Valve 4", "check", "heading-imperative", statuses=["unanimous", "disputed", "unanimous"],
                            tags=["VERB", "NOUN", "NUM"])),
                 dict(hspec("Check Valve 5", "check", "heading-imperative", outcome="drop", reason="frame-mismatch"))]
        built, report, _ = self.build("headings", specs, allow_short=True)
        self.assertEqual(["Check Valve 1"], [s["text"] for s in built])
        self.assertEqual({"class-mismatch": 1, "target-disagreement": 1, "disputed-token": 1, "frame-mismatch": 1}, drops_of(report))

    def test_caps_per_stratum_lemma_and_document(self):
        specs = hmany("check", "Check", "heading-imperative", 6) + hmany("seal", "Seal", "heading-noun", 6, "army-b", 10, 1)
        built, report, _ = self.build("headings", specs, allow_short=True)
        self.assertEqual(chd.HDG_LEMMA_CAP * 2, len(built))
        self.assertEqual({"lemma-cap": 4}, drops_of(report))
        kept = [dict(sent_key="k%d" % n, lemma="l%d" % n, source_id="d%d" % (n % 3), kind=kind) for n, kind in enumerate(["noun"] * 12 + ["proper"] * 6 + ["imperative"] * 9)]
        chosen, drops = chd.select_headings(kept, caps={"noun": 5, "imperative": 4, "proper": 3}, document_cap=2)
        counts = {k: sum(1 for c in chosen if c["kind"] == k) for k in ("noun", "imperative", "proper")}
        self.assertTrue(all(counts[k] <= cap for k, cap in {"noun": 5, "imperative": 4, "proper": 3}.items()), counts)
        per_doc = {}
        for c in chosen:
            per_doc[c["source_id"]] = per_doc.get(c["source_id"], 0) + 1
        self.assertTrue(all(n <= 2 for n in per_doc.values()), per_doc)
        self.assertGreater(drops["document-cap"], 0)

    def test_the_quota_gate_has_floors_per_stratum_and_never_pads(self):
        self.decided("headings", hmany("check", "Check", "heading-imperative", 3))
        out = os.path.join(self.temp, "out")
        with mock.patch.object(chd, "draft_gate"):
            with self.assertRaises(chd.BuildError) as raised:
                chd.build(self.work, "headings", out, data_dir=self.data)
            self.assertIn("stratum noun: 0 lines, the floor is 150", str(raised.exception))
            self.assertIn("stratum imperative: 3 lines, the floor is 100", str(raised.exception))
            self.assertFalse(os.path.exists(out))
            code, _, err = self.run_tool("build", "--source", "headings", "--out-dir", out, "--allow-short")
            self.assertEqual(0, code, err)
        self.assertEqual(3, len(common.read_corpus(os.path.join(out, "headings.tsv"))[1]))

    def test_the_quota_problems_check_the_exact_tags(self):
        def line(number, kind, tag, idx=0):
            return dict(sent_id="headings-%04d" % number, source_id="d%d" % number, text="Check Valve",
                        rows=[["Check", tag, "unanimous"], ["Valve", "NOUN", "unanimous"]],
                        extra=["# word = %d" % idx, "# lemma = l%d" % number, "# stratum = " + kind])
        self.assertEqual([], chd.quota_problems("headings", [line(1, "imperative", "VERB")], floors={}))
        found = "\n".join(chd.quota_problems("headings", [line(1, "imperative", "NOUN"), line(2, "proper", "NOUN"), line(3, "imperative", "VERB", 1)], floors={}))
        self.assertIn("tagged NOUN for stratum imperative", found)
        self.assertIn("tagged NOUN for stratum proper", found)
        self.assertIn("an imperative line must be judged at token 0", found)

    def read_all(self, folder):
        found = {}
        for name in os.listdir(folder):
            with open(os.path.join(folder, name), "rb") as handle:
                found[name] = handle.read()
        return found

    def test_the_build_is_deterministic_and_has_a_sources_table(self):
        specs = hmany("check", "Check", "heading-imperative", 3) + hmany("seal", "Seal", "heading-noun", 3, "army-b", 10, 1)
        _, _, out = self.build("headings", specs, allow_short=True)
        first = self.read_all(out)
        _, _, out = self.build("headings", list(reversed(specs)), allow_short=True)
        self.assertEqual(first, self.read_all(out))
        _, records = adjudicate.read_records(os.path.join(out, "sources.tsv"))
        self.assertEqual(["army-a", "army-b"], [r["source_id"] for r in records])
        self.assertEqual(["3", "3"], [r["sentences"] for r in records])


class ReviewTests(Mini):
    def sentences(self, source, count):
        out = []
        for n in range(1, count + 1):
            if source == "compound":
                text, idx, kind = "Tank oil check %d ." % n, 2, ("verb" if n % 10 == 0 else "noun")
                tag = "VERB" if kind == "verb" else "NOUN"
                extra = ["# head = 2", "# lemma = check", "# kind = " + kind]
            else:
                text, idx, kind = "Check Valve %d" % n, 0 if n % 3 else 1, ("imperative", "noun", "proper")[n % 3]
                tag = {"imperative": "VERB", "noun": "NOUN", "proper": "PROPN"}[kind]
                idx = 0 if kind == "imperative" else 1
                extra = ["# word = %d" % idx, "# lemma = valve", "# stratum = " + kind]
            tokens = base.simple_tokens(text)
            rows = [[t, "NOUN", "unanimous"] for t in tokens]
            rows[idx][1] = tag
            comments = dict(part[2:].split(" = ", 1) for part in extra)
            out.append(dict(sent_id="%s-%04d" % (source, n), source_id="army-a", text=text, rows=rows, extra=extra, comments=comments))
        return out

    def test_the_compound_sample_has_sixty_real_items_and_four_swapped_controls_and_is_seeded(self):
        sample = chd.build_sample("compound", self.sentences("compound", 100))
        self.assertEqual(60, sum(1 for kind, _, _ in sample if kind == "real"))
        controls = [(s, shown) for kind, s, shown in sample if kind == "control"]
        self.assertEqual(4, len(controls))
        self.assertTrue(all(shown == ("NOUN" if s["comments"]["kind"] == "verb" else "VERB") for s, shown in controls))
        self.assertEqual(64, len({s["sent_id"] for _, s, _ in sample}))
        again = chd.build_sample("compound", self.sentences("compound", 100))
        self.assertEqual([s["sent_id"] for _, s, _ in sample], [s["sent_id"] for _, s, _ in again])
        other = chd.build_sample("compound", self.sentences("compound", 100), seed=1)
        self.assertNotEqual([s["sent_id"] for _, s, _ in sample], [s["sent_id"] for _, s, _ in other])
        self.assertEqual((20261111, 20261112), (chd.SAMPLE_SEEDS["compound"], chd.SAMPLE_SEEDS["headings"]))

    def test_the_heading_sample_follows_the_stratum_quota_and_takes_fewer_when_a_stratum_is_short(self):
        sample = chd.build_sample("headings", self.sentences("headings", 300))
        real = [s for kind, s, _ in sample if kind == "real"]
        counts = {k: sum(1 for s in real if s["comments"]["stratum"] == k) for k in ("noun", "imperative", "proper")}
        self.assertEqual({"noun": 25, "imperative": 20, "proper": 15}, counts)
        small = chd.build_sample("headings", self.sentences("headings", 30))
        real = [s for kind, s, _ in small if kind == "real"]
        self.assertEqual(30, len(real))                                          # 10 per stratum, every item drawn, nothing is left for a control
        self.assertEqual([], [s for kind, s, _ in small if kind == "control"])

    def test_the_sample_file_shows_every_token_with_its_tag_and_the_key_holds_the_truth(self):
        sample = chd.build_sample("compound", self.sentences("compound", 8), size=4, controls=2)
        lines, keys = chd.sample_lines("compound", sample)
        text = "\n".join(lines)
        self.assertIn("2:check/", text)
        self.assertIn("Marked word: check (index 2)", text)
        self.assertEqual(6, len(keys))
        for (name, kind, sent_id, stratum, target, shown, agreed, ntokens), (k, s, _) in zip(keys, sample):
            self.assertEqual(k, kind)
            self.assertEqual(2, target)
            self.assertEqual(5, ntokens)
            self.assertEqual(s["comments"]["kind"], stratum)
            self.assertEqual(s["rows"][2][1], agreed)
            self.assertEqual(agreed if kind == "real" else ("NOUN" if agreed == "VERB" else "VERB"), shown)

    def keys_for(self, kinds, target=2):
        return [dict(item="R%03d" % n, kind=kind, ntokens="5", target=str(target)) for n, kind in enumerate(kinds, 1)]

    def evaluate(self, kinds, answers, target=2):
        parsed = [("R%03d" % n, verdict, others, "") for n, (verdict, others) in enumerate(answers, 1)]
        return chd.review_lines(*chd.review_numbers(self.keys_for(kinds, target), parsed))

    def test_the_gate_passes_at_95_percent_and_fails_below(self):
        kinds = ["real"] * 60 + ["control"] * 4
        lines, passed = self.evaluate(kinds, [("correct", [])] * 57 + [("incorrect", [])] * 3 + [("incorrect", [])] * 4)
        self.assertTrue(passed, "3 incorrect of 60 is exactly 95.0%, which meets `at least 95 percent`")
        self.assertIn("95.0%", "\n".join(lines))
        lines, passed = self.evaluate(kinds, [("correct", [])] * 56 + [("incorrect", [])] * 4 + [("incorrect", [])] * 4)
        self.assertFalse(passed)
        self.assertIn("93.3%", "\n".join(lines))
        self.assertIn("STOP", "\n".join(lines))

    def test_unclear_is_reported_not_gated_and_a_lenient_or_incomplete_review_fails(self):
        kinds = ["real"] * 60 + ["control"] * 4
        lines, passed = self.evaluate(kinds, [("correct", [])] * 50 + [("unclear", [])] * 10 + [("incorrect", [])] * 4)
        self.assertTrue(passed)
        self.assertIn("unclear 10", "\n".join(lines))
        lines, passed = self.evaluate(kinds, [("correct", [])] * 60 + [("correct", [])] * 2 + [("incorrect", [])] * 2)
        self.assertFalse(passed)
        self.assertIn("TOO LENIENT", "\n".join(lines))
        lines, passed = self.evaluate(kinds, [("correct", [])] * 60 + [("incorrect", [])] * 3)
        self.assertFalse(passed)
        self.assertIn("unanswered items: 1", "\n".join(lines))

    def test_the_marked_word_is_not_counted_as_another_wrong_token_wherever_it_stands(self):
        lines, passed = self.evaluate(["real"] * 2, [("correct", [2]), ("correct", [0, 4])], target=2)
        text = "\n".join(lines)
        self.assertIn("whole-item precision (marked word correct and no other token marked wrong): 0.0%", text)
        self.assertIn("2 wrong of 10 tokens", text)                              # token 2 is the marked word in the first item: not counted
        with self.assertRaises(ValueError):
            self.evaluate(["real"], [("correct", [9])])

    def test_commands_sample_and_review_result_round_trip(self):
        out = os.path.join(self.temp, "out")
        sentences = self.sentences("compound", 12)
        common.write_corpus(os.path.join(out, "compound.tsv"), chd.HEADERS["compound"], sentences)
        code, printed, err = self.run_tool("sample", "--source", "compound", "--out-dir", out, "--size", "8", "--controls", "4")
        self.assertEqual(0, code, err)
        self.assertIn("12 items: 8 real, 4 control", printed)
        _, keys = adjudicate.read_records(os.path.join(self.root, "rounds", "compound", "private", "review-sample-key.tsv"))
        verdicts = os.path.join(self.temp, "answers.txt")
        common.write_text(verdicts, ["%s\t%s\t-\tcite" % (k["item"], "incorrect" if k["kind"] == "control" else "correct") for k in keys])
        code, printed, err = self.run_tool("review-result", "--source", "compound", "--verdicts", verdicts)
        self.assertEqual(0, code, err)
        self.assertIn("gate: PASSED", printed)
        common.write_text(verdicts, ["%s\tcorrect\t-\tcite" % k["item"] for k in keys])
        code, printed, _ = self.run_tool("review-result", "--source", "compound", "--verdicts", verdicts)
        self.assertEqual(1, code)
        self.assertIn("TOO LENIENT", printed)
        common.write_text(verdicts, ["R999\tcorrect\t-\tcite"])
        code, _, err = self.run_tool("review-result", "--source", "compound", "--verdicts", verdicts)
        self.assertEqual(1, code)
        self.assertIn("unknown items", err)


class NoModelTests(unittest.TestCase):
    """The tool decides nothing with any model: it imports no tagger, weights or decoder, and names none in code."""

    def parsed(self):
        with open(chd.__file__, encoding="utf-8") as handle:
            return ast.parse(handle.read())

    def test_only_the_data_tools_are_imported(self):
        imported = set()
        for node in ast.walk(self.parsed()):
            if isinstance(node, ast.Import):
                imported |= {a.name.split(".")[0] for a in node.names}
            elif isinstance(node, ast.ImportFrom):
                imported.add((node.module or "").split(".")[0])
        allowed = {"argparse", "collections", "os", "random", "sys", "adjudicate", "common", "convention_rules", "corrections", "imperative_data", "shapes_dev",
                   "shapes_dev_extra"}
        self.assertEqual(set(), imported - allowed)

    def test_no_string_of_the_code_names_a_model_file_or_runner(self):
        tree = self.parsed()
        docstring = ast.get_docstring(tree, clean=False)
        strings = [node.value for node in ast.walk(tree) if isinstance(node, ast.Constant) and isinstance(node.value, str) and node.value != docstring]
        joined = "\n".join(strings).lower()
        for forbidden in ("weights.bin", "dotnet", "partofspeech.trainer", "partofspeech.dll", "hmm", "eval-arm", ".dll", "wordsense", "probe.tsv", "decoder",
                          "eval-shapes", "shapes-tokenize"):
            self.assertNotIn(forbidden, joined, forbidden)

    def test_the_held_out_sets_are_seen_only_through_the_builder_of_shapes_dev(self):
        with open(chd.__file__, encoding="utf-8") as handle:
            source = handle.read()
        self.assertNotIn("import leakage", source)
        self.assertNotIn("probe.tsv", source)

    def test_no_name_of_the_code_scores_or_predicts(self):
        names = {node.id for node in ast.walk(self.parsed()) if isinstance(node, ast.Name)} | {node.attr for node in ast.walk(self.parsed()) if isinstance(node, ast.Attribute)}
        for forbidden in ("tagger", "predict", "decode", "score", "weights", "hmm", "classifier"):
            self.assertEqual([], [n for n in names if forbidden in n.lower()], forbidden)

    def test_the_docstring_states_the_pre_registered_rule_and_the_constants_match_it(self):
        doc = chd.__doc__
        for part in ("PRE-REGISTERED", "9.", "1,500", "1,200", "250", "600", "800", "680", "120", "15 percent", "340", "disputed_tokens == 0", "fix-tier", "80 percent",
                     "No FAA", "model review, not a human review", "20261111", "20261112", "no sentence supplied by any consumer", "equals-a-heading-line"):
            self.assertIn(part, doc)
        self.assertEqual((6, 1500, 1200, 250, 4, 20, 600, 800, 200), (chd.CMP_CANDIDATE_LEMMA_CAP, chd.CMP_MAX_CANDIDATES, chd.CMP_MIN_CANDIDATES, chd.CMP_MIN_LEMMAS,
                                                                    chd.CMP_LEMMA_CAP, chd.CMP_DOCUMENT_CAP, chd.CMP_MIN, chd.CMP_MAX, chd.CMP_MIN_BUILD_LEMMAS))
        self.assertEqual((680, 120, 15), (chd.CMP_NOUN_CAP, chd.CMP_VERB_CAP, chd.CMP_VERB_PERCENT))
        self.assertEqual((1500, 1000, 4, 25), (chd.HDG_MAX_CANDIDATES, chd.HDG_MIN_CANDIDATES, chd.HDG_LEMMA_CAP, chd.HDG_DOCUMENT_CAP))
        self.assertEqual(({"noun": 340, "imperative": 200, "proper": 60}, {"noun": 150, "imperative": 100, "proper": 40}), (chd.HDG_CAPS, chd.HDG_FLOORS))
        self.assertEqual((60, 4, 0.95, 1), (chd.SAMPLE_SIZE, chd.SAMPLE_CONTROLS, chd.PRECISION_MIN, chd.CONTROLS_ACCEPTED_MAX))
        self.assertEqual(800, chd.CMP_NOUN_CAP + chd.CMP_VERB_CAP)
        self.assertEqual(600, sum(chd.HDG_CAPS.values()))


class TrainingLeakageGateTests(unittest.TestCase):
    """check_leakage gates the near duplicates of the compound and headings sources against the evaluation sets and compares them with the other training sets."""

    EVAL = "Remove the cover and then check the pressure valve before you start the work today"
    NEAR = EVAL.rsplit(" ", 1)[0] + " tomorrow"        # 15 words, one 6-gram of 10 differs: 90 percent contained, not an exact key

    def run_check(self, label, text):
        trains = {label: [("train-doc", text)]}
        evals = {"eval-set": [("eval-doc", self.EVAL)]}
        with mock.patch.object(check_leakage, "training_sets", return_value=trains), \
                mock.patch.object(check_leakage.leakage, "evaluation_sets", return_value=evals):
            return check_leakage.check(False)

    def test_a_near_duplicate_of_a_new_source_fails_and_one_of_the_older_sources_is_only_reported(self):
        for label in ("compound", "headings", "imperative"):
            rows, failures, _ = self.run_check(label, self.NEAR)
            self.assertEqual([(label, "eval-set", 1, 0, 0, 1)], rows)
            self.assertEqual(1, failures, label)
        for label in ("open-train", "masc", "silver"):
            self.assertEqual(0, self.run_check(label, self.NEAR)[1], label)
        self.assertEqual(("imperative", "compound", "headings"), check_leakage.NEAR_GATED_TRAINING)

    def test_the_training_files_include_the_two_new_sources(self):
        self.assertEqual(["open-train", "masc", "silver", "imperative", "compound", "headings"], [label for label, _ in check_leakage.TRAINING_FILES])
        self.assertEqual(("compound", "headings"), check_leakage.NEW_TRAINING)

    def trains(self, **changes):
        sets = {"open-train": [("open-1", "Open the door before the long flight starts today")], "masc": [("masc-1", "Masc sentence one is here")],
                "silver": [("silver-1", "Silver sentence one is here")], "imperative": [("imp-1", "Inspect the seal before each flight")],
                "compound": [("army-a", "Fuel tank seal check"), ("army-b", "Oil level reading")], "headings": [("army-a", "Pump Seal Inspection")]}
        sets.update(changes)
        return sets

    def test_each_new_source_is_compared_with_every_other_training_set_and_the_pair_once(self):
        rows, failures = check_leakage.training_check(self.trains())
        self.assertEqual([("compound", "open-train"), ("compound", "masc"), ("compound", "silver"), ("compound", "imperative"), ("compound", "headings"),
                          ("headings", "open-train"), ("headings", "masc"), ("headings", "silver"), ("headings", "imperative")], [row[:2] for row in rows])
        self.assertEqual(0, failures)
        shared = [row for row in rows if row[:2] == ("compound", "headings")][0]
        self.assertEqual(1, shared[4])                                                   # the shared document is counted but is not a failure

    def test_a_shared_document_or_an_exact_sentence_with_another_training_set_fails(self):
        rows, failures = check_leakage.training_check(self.trains(masc=[("army-b", "Another sentence")]))
        self.assertEqual(1, failures)
        self.assertEqual(1, [row for row in rows if row[:2] == ("compound", "masc")][0][4])
        rows, failures = check_leakage.training_check(self.trains(silver=[("silver-1", "Pump seal inspection!")]))
        self.assertEqual(1, failures)                                                    # the key of the heading line is the key of a silver sentence
        self.assertEqual(1, [row for row in rows if row[:2] == ("headings", "silver")][0][5])
        rows, failures = check_leakage.training_check(self.trains(headings=[("army-c", "Fuel tank seal check")]))
        self.assertEqual(1, failures)                                                    # an exact sentence shared by the two new sources fails

    def test_a_near_duplicate_between_training_sets_is_reported_not_gated(self):
        near = self.NEAR
        rows, failures = check_leakage.training_check(self.trains(compound=[("army-a", near)], silver=[("silver-1", self.EVAL)]))
        self.assertEqual(0, failures)
        self.assertEqual(1, [row for row in rows if row[:2] == ("compound", "silver")][0][6])


if __name__ == "__main__":
    unittest.main()

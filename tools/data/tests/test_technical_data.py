"""Tests of technical_data.py (the builder of the technical-text data of beta.3 phase 1: four development tranches, four training sources, the S2 pilot and its gate)
on a miniature data and work folder.

Run: python -m unittest discover -s tools/data/tests -p "test_technical_data.py"   (developer-run; the pipeline needs no model or network)
"""
import ast
import collections
import contextlib
import hashlib
import io
import itertools
import os
import re
import sys
import unittest
from unittest import mock

sys.path.insert(0, os.path.dirname(os.path.dirname(os.path.abspath(__file__))))
import adjudicate
import check_leakage
import check_licenses
import common
import compound_heading_data as chd
import followup_data as fu
import imperative_data as idata
import leakage
import shapes_dev as sd
import shapes_dev_extra as sx
import technical_data as td
import technical_frames as tf
import test_compound_heading_data as ct
import test_followup_data as ft
import test_shapes_dev as base

EXTRA_WORDS = dict(testing="ADJ NOUN VERB", control="NOUN VERB", panel="NOUN", drain="NOUN VERB", open="ADJ NOUN VERB", manual="ADJ NOUN", override="NOUN VERB", mode="NOUN",
                   select="ADJ VERB", button="NOUN VERB", contact="NOUN VERB", fuel="NOUN VERB", checks="NOUN VERB", record="NOUN VERB", full="ADJ", this="DET", you="PRON",
                   systems="NOUN", hydraulic="ADJ", today="ADV", when="SCONJ", pressure="NOUN VERB", relief="NOUN", water="NOUN", fill="NOUN VERB", close="ADJ VERB",
                   inspect="VERB", install="VERB", remove="NOUN VERB", level="NOUN VERB ADJ")
LEXICON = dict(ft.LEXICON, **EXTRA_WORDS)
LEX = {word: frozenset(tags.split()) for word, tags in LEXICON.items()}
EXPECTED = {("deverbal", "verb"): "VERB", ("terms", "noun"): "NOUN", ("terms", "adj"): "ADJ", ("terms", "proper"): "PROPN", ("steps", "imperative"): "VERB",
            ("steps", "noun"): "NOUN", ("clauses", "main"): "VERB", ("clauses", "sub"): "VERB", ("clauses", "noun"): "NOUN", ("clauses", "adj"): "ADJ"}
# one sentence of every frame (the fixture lexicon makes each a hit of the kind named in the second column)
SENTENCES = {"deverbal": {"other": "The oil check is full .", "pred": "This is the oil check .", "num": "Record 3 oil check .", "ing": "The oil testing is full .",
                          "verb": "You must check the valve ."},
             "terms": {"adj": "Select the Manual Override mode .", "noun": "Press the Pump Switch button .", "proper": "Contact the Hydraulic Systems Command today ."},
             "steps": {"imperative": "Check oil level .", "noun": "Control Panel"},
             "clauses": {"main": "The pumps drain the tank .", "sub": "When the pumps drain the tank the pressure drops .", "noun": "The pumps drain tank .",
                         "adj": "The open pumps are full ."}}


def drops_of(report):
    return ct.drops_of(report)


def tags_for(tokens, group, kind, target):
    """Fixture tags: punctuation PUNCT, numbers NUM, every other token NOUN, the judged word the exact tag of its kind (NOUN for the heads of S1)."""
    tags = ["PUNCT" if not t[:1].isalnum() else "NUM" if t.isdigit() else "NOUN" for t in tokens]
    tags[target] = EXPECTED.get((group, kind), "NOUN")
    return tags


class Mini(ft.Mini):
    """The miniature folders of test_followup_data with a lexicon for the technical frames, a CONVENTIONS.md, and helpers for the rounds of the eight products."""

    def write_data(self):
        super().write_data()
        d = self.data
        common.write_text(os.path.join(d, "lexicon", "lexicon.tsv"), ["# fixture lexicon"] + ["%s\t%s" % (k, v) for k, v in sorted(LEXICON.items())])
        common.write_text(os.path.join(d, "CONVENTIONS.md"), ["# fixture conventions", "", "Capitalized Defined Terms"])

    def pins(self, hmm=False):
        """Patches of the pins to the hashes of the files of the work folder (the R1 commit of the real work)."""
        if hmm:
            common.write_text(os.path.join(self.root, "hmm.tsv"), ["# fixture"])
        patches = [mock.patch.object(td, "DRAFT_SHA256", common.sha256_file(os.path.join(self.root, "sources-draft.tsv"))),
                   mock.patch.object(td, "TOKENS_SHA256", common.sha256_file(os.path.join(self.root, "tokens.tsv"))),
                   mock.patch.object(td, "HMM_SHA256", common.sha256_file(os.path.join(self.root, "hmm.tsv")) if hmm else None),
                   mock.patch.object(td, "CONVENTIONS_SHA256", common.sha256_file(os.path.join(self.data, "CONVENTIONS.md")))]
        for patch in patches:
            patch.start()
            self.addCleanup(patch.stop)

    def run_tool(self, *args):
        out, err = io.StringIO(), io.StringIO()
        with contextlib.redirect_stdout(out), contextlib.redirect_stderr(err):
            with mock.patch.object(sd, "noise_reason", return_value=None), mock.patch.object(sx, "heading_noise_reason", return_value=None), \
                    mock.patch.object(tf, "step_noise_reason", return_value=None):
                code = td.main(["--work", self.root, "--data-dir", self.data] + list(args))
        return code, out.getvalue(), err.getvalue()

    def round(self, round_name, specs, with_q=False):
        """Write candidates, extras and decided items/rows of a round from specs: dicts with cand_id, text, lemma, source, target and optional tags, statuses, outcome, reason,
        agreed, opus, sol, q, disputed and the extras fields frame, unit, tier, term, flag. The shape and role follow from the kind of the candidate id."""
        self.write_draft(sorted({s["source"] for s in specs} | set(getattr(self, "extra_documents", ()))))
        rw = sd.RoundWork(self.root, round_name)
        os.makedirs(rw.private, exist_ok=True)
        rows, items, token_rows, extras = [], [], [], []
        for spec in specs:
            group, kind = td.kind_of_cand_id(spec["cand_id"])
            tokens = base.simple_tokens(spec["text"])
            target = spec["target"]
            shape, role = td.SHAPE_OF_KIND[group][kind], ("control" if kind in td.CONTROL_KINDS[group] else "target")
            row = dict(cand_id=spec["cand_id"], shape=shape, role=role, target=target, lemma=spec["lemma"], origin="real", source_id=spec["source"], line=1, text=spec["text"],
                       tokens=tokens)
            if with_q:
                row["q"] = spec.get("q", "")
            rows.append(row)
            key = sd.sent_key_of(spec["text"])
            tags = spec.get("tags") or tags_for(tokens, group, kind, target)
            statuses = spec.get("statuses") or ["unanimous"] * len(tokens)
            outcome = spec.get("outcome", "keep")
            agreed = spec.get("agreed", tags[target] if outcome == "keep" else "")
            item = [spec["cand_id"], key, shape, role, str(target), spec["lemma"], "real", spec["source"], outcome, spec.get("reason", ""), spec.get("opus", tags[target]),
                    spec.get("sol", tags[target]), agreed, str(spec.get("disputed", sum(1 for s in statuses if s == "disputed")))]
            items.append(item + ([spec.get("q", "")] if with_q else []))
            token_rows += [(key, i, tokens[i], tags[i], statuses[i]) for i in range(len(tokens))]
            extras.append((spec["cand_id"], spec.get("frame", ""), spec.get("unit", ""), spec.get("tier", ""), spec.get("term", ""), spec.get("flag", "")))
        sd.write_candidates(rw, rows)
        adjudicate.write_rows(rw.private_file("decided.tsv"), sd.HARD_ITEM_COLUMNS if with_q else sd.ITEM_COLUMNS, items)
        adjudicate.write_rows(rw.private_file("decided-rows.tsv"), sd.ROW_COLUMNS, token_rows)
        adjudicate.write_rows(rw.private_file(td.EXTRAS_FILE), td.EXTRA_COLUMNS, extras)

    def pilot_passed(self, verdict="PROCEED 70 of 70 confirmed"):
        rw = sd.RoundWork(self.root, td.PILOT_ROUND)
        os.makedirs(rw.private, exist_ok=True)
        common.write_text(rw.private_file(td.PILOT_VERDICT_FILE), [verdict])

    def build(self, product, round_specs, out=None, **options):
        """Write the rounds {round name: (specs, with_q)} and build the product; returns (built, report, out folder)."""
        self.extra_documents = {s["source"] for specs, _ in round_specs.values() for s in specs}      # one draft for every round
        for name, (specs, with_q) in round_specs.items():
            self.round(name, specs, with_q)
        out = out or os.path.join(self.temp, "out")
        with mock.patch.object(td, "draft_gate"):
            built, report = td.build(self.work, product, out, data_dir=self.data, **options)
        return built, report, out


def spec(cand_id, text, lemma, target, source="army-a", **extra):
    return dict(cand_id=cand_id, text=text, lemma=lemma, target=target, source=source, **extra)


def numbered(prefix, template, lemma, count, target, first=1, source="army-a", start=1, **extra):
    """`count` specs `<prefix>c<NNNNN>` with the text `template % number` (a distinct number per spec) and one lemma."""
    return [spec("%sc%05d" % (prefix, first + n), template % (start + n), lemma, target, source, **extra) for n in range(count)]


# ================================================================ the tables and the ids

class TableTests(unittest.TestCase):
    def test_the_eight_products_their_sides_files_ids_and_twins(self):
        self.assertEqual(("deverbal-heads", "defined-terms", "step-lines", "clause-verbs", "deverbal", "terms", "steps", "clauses"), td.PRODUCTS)
        self.assertEqual({"dev"}, {td.SIDE[p] for p in td.DEV_PRODUCTS})
        self.assertEqual({"train"}, {td.SIDE[p] for p in td.TRAIN_PRODUCTS})
        self.assertEqual({"deverbal-heads": "deverbal", "defined-terms": "terms", "step-lines": "steps", "clause-verbs": "clauses"}, td.TWIN)
        for product in td.DEV_PRODUCTS:
            self.assertEqual(("eval/shapes-dev", product + ".tsv", product + "-sources.tsv"), td.OUTPUT[product])
            self.assertEqual("shapes-dev-%s-%%04d" % product, td.SENT_ID[product])
        for product in td.TRAIN_PRODUCTS:
            self.assertEqual(("technical", product + ".tsv", product + "-sources.tsv"), td.OUTPUT[product])
            self.assertEqual(product + "-%04d", td.SENT_ID[product])
            self.assertEqual(product + "-dropped-sentences.tsv", td.DROPPED_OUT[product])
        self.assertEqual("excluded-documents.tsv", td.EXCLUDED_OUT)

    def test_the_rounds_the_orders_and_the_candidate_id_prefixes(self):
        self.assertEqual({"deverbal-heads": "dvh", "defined-terms": "dtm", "step-lines": "stp", "clause-verbs": "clv", "deverbal": "dvt", "terms": "dtt", "steps": "stt",
                          "clauses": "clt"}, td.CANDIDATE_ROUND)
        self.assertEqual({"deverbal-heads": "dvhc", "defined-terms": "dtmc", "step-lines": "stpc", "clause-verbs": "clvc"}, td.CONTROLS_ROUND)
        self.assertEqual("dtm-pilot", td.PILOT_ROUND)
        self.assertEqual(("dtm-pilot", "dtm", "dtmc"), td.ROUNDS["defined-terms"])
        self.assertEqual(("dtm-pilot", "dtt"), td.ROUNDS["terms"])
        self.assertEqual(("dvh", "dvhc"), td.ROUNDS["deverbal-heads"])
        self.assertEqual(("stt",), td.ROUNDS["steps"])
        self.assertEqual("dh-pred-", td.prefix_of("deverbal-heads", "pred"))
        self.assertEqual("dhc-verb-", td.prefix_of("deverbal-heads", "verb", controls_round=True))
        self.assertEqual("dp-noun-", td.prefix_of("defined-terms", "noun", pilot=True))
        self.assertEqual(("terms", "noun"), td.kind_of_cand_id("dp-noun-c00001"))
        self.assertEqual(("steps", "noun"), td.kind_of_cand_id("sl-noun-c01234"))
        self.assertEqual(("clauses", "sub"), td.kind_of_cand_id("clt-sub-c00007"))
        with self.assertRaises(ValueError):
            td.kind_of_cand_id("h2d-c00001")                                          # an id of PR #20, not of this tool
        every = [(product, kind) for product in td.PRODUCTS for kind in td.KINDS[td.GROUP[product]]]
        prefixes = [td.prefix_of(product, kind) for product, kind in every]
        self.assertEqual(len(prefixes), len(set(prefixes)))                          # a prefix names one product and one kind
        for old in itertools.chain(fu.CAND_PREFIX.values(), chd.CAND_PREFIX.values(), sx.CAND_PREFIX.values()):
            self.assertFalse(any(p.startswith(old) for p in td.KIND_OF_PREFIX), old)     # no collision with the ids of PR #19 and #20
        self.assertEqual(set(td.ROUND_ORDER["dev"][1:5]) | {"dtm-pilot"} | {n + "c" for n in ("dvh", "dtm", "stp", "clv")}, set(td.ROUND_ORDER["dev"]))

    def test_the_labels_of_the_contrast_stratum_of_the_steps_source(self):
        self.assertEqual("noun-initial", td.kind_label("steps", "noun"))
        self.assertEqual("noun", td.kind_label("step-lines", "noun"))
        self.assertEqual("noun", td.kind_of_label("steps", "noun-initial"))
        self.assertEqual("pred", td.kind_of_label("deverbal", "pred"))

    def test_the_kinds_roles_shapes_and_expected_tags_of_every_group(self):
        self.assertEqual(("pred", "num", "ing", "other"), td.TARGET_KINDS["deverbal"])
        self.assertEqual(("noun", "adj"), td.TARGET_KINDS["terms"])
        self.assertEqual(("main", "sub"), td.TARGET_KINDS["clauses"])
        for group, kinds in td.KINDS.items():
            self.assertEqual(set(kinds), set(td.SHAPE_OF_KIND[group]), group)
            self.assertEqual(set(kinds), set(td.EXPECTED_TAGS[group]), group)
            self.assertEqual(set(kinds), set(td.PRIORITY[group]) | ({"verb"} if group == "deverbal" else set()), group)
            for shape in td.SHAPE_OF_KIND[group].values():
                self.assertIn(shape, sd.EXTRA_SHAPE_CLASS)                           # every candidate shape is registered in shapes_dev
        self.assertEqual({"n-deverbal-head", "term-capitalized", "step-line", "v-clause-ambiguous"}, set(td.TRANCHE_SHAPE.values()))
        for shape, tag in sd.TECHNICAL_EXACT_TAGS.items():
            group = next(g for g, shapes in td.SHAPE_OF_KIND.items() if shape in shapes.values())
            kinds = [k for k, s in td.SHAPE_OF_KIND[group].items() if s == shape]
            self.assertTrue(all(td.EXPECTED_TAGS[group][k] == (tag,) for k in kinds), shape)     # the exact tag of shapes_dev is the expected tag here


# ================================================================ the used-document index, the reference and the pins

class IndexTests(Mini):
    def test_the_index_holds_all_earlier_tables_and_the_outputs_of_pr_20_but_not_its_own_outputs(self):
        extra = os.path.join(self.temp, "draft.tsv")
        base.table_file(extra, sd.SOURCE_COLUMNS, [self.source_row("army-extra", "https://archive.org/download/ExtraId/ExtraId_djvu.txt")])
        for relative, sid in (("compound/compound-2-sources.tsv", "army-pr20-cmp"), ("compound/compound-2-excluded-documents.tsv", "army-pr20-cmp-out"),
                              ("headings/headings-2-sources.tsv", "army-pr20-hdg"), ("headings/headings-2-excluded-documents.tsv", "army-pr20-hdg-out"),
                              ("eval/shapes-dev/compound-head-2-sources.tsv", "army-pr20-chd"), ("eval/shapes-dev/headings-2-sources.tsv", "army-pr20-hd2"),
                              ("technical/terms-sources.tsv", "army-own-term"), ("eval/shapes-dev/step-lines-sources.tsv", "army-own-step")):
            path = os.path.join(self.data, *relative.split("/"))
            if relative.endswith("excluded-documents.tsv"):
                common.write_text(path, ["# columns = document_id, reason", sid + "\tstate co-author"])
            else:
                base.table_file(path, sd.SOURCE_COLUMNS, [self.source_row(sid, "https://archive.org/download/%s/%s_djvu.txt" % (sid, sid))])
        index = td.used_index(self.data, sd.load_exclusions(self.work, self.data), [extra])
        for known in ("army-dev-doc", "army-ch-doc", "army-imp-doc", "army-old-cmp", "army-old-cmp-out", "army-old-hdg", "army-old-hdg-out", "gutenberg-1023",
                      "army-pr20-cmp", "army-pr20-cmp-out", "army-pr20-hdg", "army-pr20-hdg-out", "army-pr20-chd", "army-pr20-hd2"):
            self.assertIsNotNone(idata.document_rejection(index, known), known)
        self.assertIsNotNone(idata.document_rejection(index, "x", "https://archive.org/details/army-pr20-cmp"))                 # an identifier of PR #20
        self.assertIn("extra:draft.tsv", idata.document_rejection(index, "army-extra"))
        for own in ("army-own-term", "army-own-step"):                                                                         # a rebuild never rejects itself
            self.assertIsNone(idata.document_rejection(index, own), own)
        self.assertIsNone(idata.document_rejection(index, "army-new", "https://archive.org/download/NewOne/NewOne_djvu.txt"))
        old = fu.used_index(self.data, sd.load_exclusions(self.work, self.data))
        self.assertIsNone(idata.document_rejection(old, "army-pr20-cmp"))                 # why this tool has its own index: the PR #20 builder never saw its outputs

    def test_the_draft_gate_refuses_a_document_of_pr_20_or_of_an_old_exclusion_record(self):
        base.table_file(os.path.join(self.data, "compound", "compound-2-sources.tsv"), sd.SOURCE_COLUMNS,
                        [self.source_row("army-pr20-cmp", "https://archive.org/download/Pr20/Pr20_djvu.txt")])
        for used in ("army-pr20-cmp", "army-old-cmp", "army-old-hdg-out"):
            self.sound_draft({used: ct.FRAME})
            with self.assertRaises(sx.DraftError, msg=used):
                td.draft_gate(self.work, self.data)
        self.sound_draft({"army-a": ct.FRAME})
        td.draft_gate(self.work, self.data)
        td.draft_gate(self.work, self.data, (), ["not-in-the-draft"])             # the gate skips only the omitted documents

    def test_the_reference_holds_the_follow_up_sources_and_the_pr_20_tranches_too(self):
        base.corpus_file(os.path.join(self.data, "eval", "shapes-dev", "compound-head-2.tsv"), [("army-chd2", "Check the pump seal housing after the long trip today .")])
        reference = td.Reference(self.data)
        self.assertEqual(idata.EQUALS_SHAPES_DEV, reference.reason("Drain the hydraulic reservoir before the long trip ."))     # compound-2.tsv
        self.assertEqual(idata.EQUALS_SHAPES_DEV, reference.reason("Drain The Hydraulic Reservoir"))                           # headings-2.tsv
        self.assertEqual(idata.EQUALS_SHAPES_DEV, reference.reason("Check the pump seal housing after the long trip today ."))  # a PR #20 tranche
        self.assertEqual(idata.EQUALS_SHAPES_DEV, reference.reason("Replace the fuel pump seal after the long trip ."))        # everything of followup_data
        self.assertIsNone(td.Reference(self.data).reason("Tank oil check 3 ."))
        self.assertIsNone(fu.Reference(self.data).reason("Drain the hydraulic reservoir before the long trip ."))               # the older reference did not hold them

    def test_the_pins_must_be_set_and_must_match_before_any_candidate(self):
        self.sound_draft({"army-a": ct.FRAME})
        no_pins = [mock.patch.object(td, name, None) for name in ("DRAFT_SHA256", "TOKENS_SHA256", "HMM_SHA256", "CONVENTIONS_SHA256")]
        for patch in no_pins:                                                         # the R0 state: no pin is set (the module holds the frozen values since R1)
            patch.start()
            self.addCleanup(patch.stop)
        for name in ("DRAFT_SHA256", "TOKENS_SHA256", "HMM_SHA256", "CONVENTIONS_SHA256"):
            self.assertIsNone(getattr(td, name), name)
        with self.assertRaises(td.PinError):
            td.require_pins(self.work)
        self.pins()
        td.require_pins(self.work, "defined-terms", self.data)
        with mock.patch.object(td, "TOKENS_SHA256", "0" * 64):
            with self.assertRaises(td.PinError):
                td.require_pins(self.work)
        with mock.patch.object(td, "CONVENTIONS_SHA256", "0" * 64):
            with self.assertRaises(td.PinError) as raised:
                td.require_pins(self.work, None, self.data)
            self.assertIn("CONVENTIONS.md", str(raised.exception))
        for product in td.BANDED:
            with self.assertRaises(td.PinError, msg=product):                         # hmm.tsv is pinned for the banded development products only and is not set here
                td.require_pins(self.work, product, self.data)
        for product in set(td.PRODUCTS) - set(td.BANDED):
            td.require_pins(self.work, product, self.data)
        self.pins(hmm=True)
        td.require_pins(self.work, "step-lines", self.data)
        self.assertEqual({"deverbal-heads": "Noun", "step-lines": "Verb", "clause-verbs": "Verb"}, td.BANDED)

    def test_the_frozen_pins_refuse_a_changed_input(self):
        self.sound_draft({"army-a": ct.FRAME})
        with self.assertRaises(td.PinError) as raised:                                         # the work folder of the test is not the frozen one: refused with the pinned value
            td.require_pins(self.work, None, self.data)
        self.assertIn(td.DRAFT_SHA256, str(raised.exception))
        self.pins(hmm=True)
        with mock.patch.object(td, "HMM_SHA256", "71fa239d4f15b7b367a83203e77d7ff501055f85da7aca46b5d8ac41da8b7600"):
            with self.assertRaises(td.PinError) as raised:                                     # a changed hmm.tsv is refused for a banded product
                td.require_pins(self.work, "step-lines", self.data)
            self.assertIn("71fa239d4f15b7b367a83203e77d7ff501055f85da7aca46b5d8ac41da8b7600", str(raised.exception))
        with mock.patch.object(td, "CONVENTIONS_SHA256", "42da154c60272dc9a105c5496fa3c8797cffa81cfb55d34505497b33520e5bb2"):     # a changed CONVENTIONS.md is refused too
            with self.assertRaises(td.PinError) as raised:
                td.require_pins(self.work, None, self.data)
            self.assertIn("CONVENTIONS.md", str(raised.exception))

    def test_the_exclusions_and_lines_commands(self):
        extra = os.path.join(self.temp, "old-draft.tsv")
        base.table_file(extra, sd.SOURCE_COLUMNS, [self.source_row("usda-oldunused", "https://archive.org/download/OldUnused/OldUnused_djvu.txt")])
        self.sound_draft({"army-a": ct.FRAME})
        code, out, _ = self.run_tool("exclusions", "--extra-table", extra, "--check", "usda-oldunused", "new-doc", "https://archive.org/details/OldUnused")
        self.assertEqual(1, code)
        self.assertIn("REJECT: usda-oldunused", out)
        self.assertIn("ACCEPT: new-doc", out)
        self.assertIn("REJECT: https://archive.org/details/OldUnused", out)
        self.assertTrue(os.path.exists(os.path.join(self.root, "exclusions.tsv")))
        os.makedirs(os.path.join(self.root, "raw"), exist_ok=True)
        with open(os.path.join(self.root, "raw", "army-a.txt"), "w", encoding="utf-8", newline="") as handle:
            handle.write("Check The Valve\n\nThe pumps drain the tank before the start of the long work on the unit .\n")
        code, out, err = self.run_tool("lines")
        self.assertEqual(0, code, err)
        self.assertIn("1 documents, 1 lines written to lines/", out)

    def test_a_candidate_command_refuses_without_pins_and_writes_nothing(self):
        self.sound_draft({"army-a": ct.FRAME})
        self.sides(**{"army-a": "train"})
        for name in ("DRAFT_SHA256", "TOKENS_SHA256", "HMM_SHA256", "CONVENTIONS_SHA256"):
            patch = mock.patch.object(td, name, None)                                  # the R0 state; the module holds the frozen values since R1
            patch.start()
            self.addCleanup(patch.stop)
        before = self.snapshot()
        code, _, err = self.run_tool("candidates", "--product", "deverbal", "--no-ud")
        self.assertEqual(1, code)
        self.assertIn("pin of sources-draft.tsv is not set", err)
        code, _, err = self.run_tool("pilot", "--no-ud")
        self.assertEqual(1, code)
        self.assertIn("pin of sources-draft.tsv is not set", err)
        self.pins()
        with mock.patch.object(td, "DRAFT_SHA256", "f" * 64):
            code, _, err = self.run_tool("candidates", "--product", "steps", "--no-ud")
            self.assertEqual(1, code)
            self.assertIn("the pinned one is " + "f" * 64, err)
        self.assertEqual(before, self.snapshot())


# ================================================================ the family key and the partition

class PartitionTests(Mini):
    def test_the_side_is_the_seeded_rank_of_the_family_key_against_the_share_040(self):
        self.assertEqual(0.40, td.DEV_SHARE)
        families = ["family number %d" % n for n in range(4000)]
        dev = sum(1 for f in families if td.side_of(f) == "dev") / len(families)
        self.assertAlmostEqual(0.40, dev, delta=0.03)
        expected = "dev" if int(sd.rank_of(sd.SEED, "partition", "a family")[:12], 16) / 16 ** 12 < 0.40 else "train"
        self.assertEqual(expected, td.side_of("a family"))
        self.assertEqual("dev", td.side_of("a family", 1.0))
        self.assertEqual("train", td.side_of("a family", 0.0))
        self.assertEqual(td.side_of("a family"), td.side_of("a family"))
        self.assertEqual(fu.side_of("a family", "C", {"C": 0.40, "H": 0.40}), td.side_of("a family"))        # the rule of followup_data with one share

    def test_a_family_is_on_one_side_whatever_the_order_and_streams_are_optional_but_checked(self):
        draft = [dict(source_id="d1", title="Manual of Tasks (1990)"), dict(source_id="d2", title="Manual of Tasks, 2003 edition"), dict(source_id="d3", title="Other Book")]
        first = td.partition_rows(draft)
        self.assertEqual(first, td.partition_rows(list(reversed(draft))))
        sides = {row[0]: row[3] for row in first}
        self.assertEqual(sides["d1"], sides["d2"])
        self.assertEqual({"-"}, {row[1] for row in first})
        with_streams = td.partition_rows(draft, {"d1": "P", "d2": "E", "d3": "M"})
        self.assertEqual(["P", "E", "M"], [row[1] for row in with_streams])
        self.assertEqual([row[3] for row in first], [row[3] for row in with_streams])                    # the stream never changes the side
        with self.assertRaises(ValueError):
            td.partition_rows(draft, {"d1": "P"})

    def test_the_partition_command_writes_the_table_that_followup_readers_accept(self):
        self.sound_draft({"army-a": ct.FRAME, "army-b": ct.FRAME})
        code, out, err = self.run_tool("partition")
        self.assertEqual(0, code, err)
        _, records = adjudicate.read_records(os.path.join(self.root, "partition.tsv"))
        self.assertEqual(["army-a", "army-b"], [r["source_id"] for r in records])
        self.assertEqual({"army-a": "dev", "army-b": "dev"}.keys(), fu.read_partition(self.work).keys())      # fu.read_partition reads it (fail closed on a family on both sides)
        common.write_text(os.path.join(self.root, "streams.tsv"), ["source_id\tstream", "army-a\tP", "army-b\tX"])
        code, _, err = self.run_tool("partition")
        self.assertEqual(1, code)
        self.assertIn("a stream is one of P, E, M, G", err)
        common.write_text(os.path.join(self.root, "streams.tsv"), ["source_id\tstream", "army-a\tP", "army-b\tG"])
        self.assertEqual(0, self.run_tool("partition")[0])
        self.assertEqual({"P", "G"}, {r["stream"] for r in adjudicate.read_records(os.path.join(self.root, "partition.tsv"))[1]})

    def test_a_product_only_reads_the_items_of_the_documents_of_its_side(self):
        specs = numbered("dvt-pred-", "Record %d oil check .", "check", 3, 3)
        self.sides(**{"army-a": "dev"})
        built, _, _ = self.build("deverbal", {"dvt": (specs, False)}, allow_short=True)
        self.assertEqual([], built)                                                       # a TRAIN product never sees a document of the DEV side
        self.sides(**{"army-a": "train"})
        built, _, _ = self.build("deverbal", {"dvt": (specs, False)}, allow_short=True)
        self.assertEqual(3, len(built))

    def test_the_two_sides_stay_disjoint_against_the_committed_tables_of_the_other_side(self):
        self.sides(**{"army-a": "train"})
        base.table_file(os.path.join(self.data, "eval", "shapes-dev", "step-lines-sources.tsv"), sd.SOURCE_COLUMNS, [self.source_row("army-a")])
        with self.assertRaises(td.BuildError) as raised:
            self.build("deverbal", {"dvt": (numbered("dvt-pred-", "Record %d oil check .", "check", 3, 3), False)}, allow_short=True)
        self.assertIn("army-a is in both deverbal and step-lines", str(raised.exception))


# ================================================================ step units

RAW = "\n".join(["Contents", "1. Check oil level.", "2. Open valve 3.", "Fuel Level", "- Drain the tank.", "", "The pumps drain the tank before the start of the long work on the unit."]) + "\n"


class StepTests(Mini):
    def test_step_units_are_the_step_lines_then_the_sentences_one_entry_per_text(self):
        units = td.step_units(RAW, ["The pumps drain the tank before the start of the long work on the unit.", "Check oil level."])
        texts = [t for t, _ in units]
        self.assertEqual(len(texts), len(set(texts)))
        kinds = dict(units)
        self.assertEqual("enumerated", kinds["Check oil level."])                    # the step line wins over the sentence of the same text
        self.assertEqual("sentence", kinds["The pumps drain the tank before the start of the long work on the unit."])
        self.assertEqual("bulleted", kinds["Drain the tank."])
        self.assertEqual(("short", "enumerated", "bulleted", "sentence"), td.STEP_KIND_ORDER)

    def test_the_steps_command_writes_the_units_and_the_kinds_with_the_strictest_kind_per_text(self):
        self.sound_draft({"army-a": ["The pumps drain the tank before the start of the long work on the unit ."]})
        os.makedirs(os.path.join(self.root, "raw"), exist_ok=True)
        with open(os.path.join(self.root, "raw", "army-a.txt"), "w", encoding="utf-8", newline="") as handle:
            handle.write(RAW)
        code, out, err = self.run_tool("steps")
        self.assertEqual(0, code, err)
        self.assertIn("1 documents", out)
        units = common.read_lines(os.path.join(self.root, "steps", "army-a.txt"))
        self.assertIn("Check oil level.", units)
        self.assertIn("The pumps drain the tank before the start of the long work on the unit .", units)
        kinds = td.read_step_kinds(self.work)
        self.assertEqual("enumerated", kinds[sd.sent_key_of("Check oil level.")])
        self.assertEqual("sentence", kinds[sd.sent_key_of("The pumps drain the tank before the start of the long work on the unit .")])
        self.assertEqual("short", kinds[sd.sent_key_of("Fuel Level")])
        os.remove(os.path.join(self.root, "step-kinds.tsv"))
        with self.assertRaises(ValueError):
            td.read_step_kinds(self.work)                                           # fail closed: run `steps` first

    def test_the_tokenize_command_of_shapes_dev_extra_reads_the_steps_folder_too(self):
        self.sound_draft({"army-a": ["The pumps drain the tank ."]})
        common.write_text(os.path.join(self.root, "steps", "army-a.txt"), ["Check oil level.", "The pumps drain the tank ."])
        seen = []

        def fake(texts, command):
            seen.extend(texts)
            return {t: (1, base.simple_tokens(t)) for t in texts}
        with mock.patch.object(sd, "run_tokenizer", side_effect=fake):
            code, _, err = self.run_tool("tokenize", "--cmd", "x")
        self.assertEqual(0, code, err)
        self.assertEqual(["Check oil level."], seen)                                 # the step unit is new, the sentence was tokenized already
        self.assertIn("Check oil level.", sd.read_tokens(self.work))

    def test_the_step_scan_uses_the_steps_folder_the_step_noise_and_the_token_range(self):
        self.sound_draft({"army-a": ["The pumps drain the tank ."]})
        common.write_text(os.path.join(self.root, "steps", "army-a.txt"), ["Check oil level.", "Open", "Fuel Level 12", "The pumps drain the tank ."])
        texts = ["Check oil level.", "Open", "Fuel Level 12", "The pumps drain the tank ."]
        common.write_text(os.path.join(self.root, "tokens.tsv"), ["text\tsentences\ttokens"] + ["%s\t1\t%s" % (t, " ".join(base.simple_tokens(t))) for t in sorted(texts)])
        kinds = {sd.sent_key_of("Fuel Level 12"): "short", sd.sent_key_of("Check oil level."): "enumerated"}
        with mock.patch.object(sd, "noise_reason", return_value=None):
            scan = td.scan_all(self.work, self.data, "step", (), (), False, step_kinds=kinds)
        self.assertEqual(["Check oil level.", "The pumps drain the tank ."], sorted(u[2] for u in scan.units))
        self.assertEqual({"length": 1, "noise-page-number": 1}, {k: v for k, v in scan.drops.items() if k in ("length", "noise-page-number")})   # `Open` is one token; a short line with a number is a page number
        self.assertEqual("page-number", td.step_noise(kinds)("Fuel Level 12", base.simple_tokens("Fuel Level 12"), LEX, set(LEX)))
        self.assertIsNone(td.step_noise(kinds)("Check oil level 12.", base.simple_tokens("Check oil level 12."), LEX, set(LEX)))   # an unknown text is a sentence: no number rule


# ================================================================ frames, pools and the candidates of a round

def scan_of(units):
    """A `Scan` of (source id, text) units (no filter: the filters are the unchanged `shapes_dev_extra.scan_units`, tested there)."""
    scan = sx.Scan()
    scan.units = [(source, number, text, base.simple_tokens(text)) for number, (source, text) in enumerate(units, 1)]
    scan.considered = len(scan.units)
    return scan


class FrameHitTests(unittest.TestCase):
    def test_every_fixture_sentence_is_a_hit_of_its_kind_with_the_fields_of_the_extras_table(self):
        for group, sentences in SENTENCES.items():
            for kind, text in sentences.items():
                tokens = base.simple_tokens(text)
                if kind == "verb":
                    self.assertEqual(["modal"], [h.frame for h in tf.verb_control_hits(tokens, LEX)])
                    continue
                found = td.frame_hits(group, tokens, LEX)
                self.assertIn(kind, found, text)
        self.assertEqual("zero", td.frame_hits("deverbal", base.simple_tokens(SENTENCES["deverbal"]["pred"]), LEX)["pred"].frame)
        self.assertEqual("ing", td.frame_hits("deverbal", base.simple_tokens(SENTENCES["deverbal"]["ing"]), LEX)["ing"].frame)
        adj = td.frame_hits("terms", base.simple_tokens(SENTENCES["terms"]["adj"]), LEX)
        self.assertEqual({"adj", "noun"}, set(adj))                                    # the caller takes the first kind of the priority: adj
        self.assertEqual(("T1", "manual override"), (adj["adj"].tier, adj["adj"].term))
        self.assertEqual("T4", td.frame_hits("terms", base.simple_tokens(SENTENCES["terms"]["proper"]), LEX)["proper"].tier)
        self.assertEqual("bare", td.frame_hits("steps", base.simple_tokens("Check oil level ."), LEX)["imperative"].frame)
        self.assertEqual("det", td.frame_hits("steps", base.simple_tokens("Check the valve ."), LEX)["imperative"].frame)
        self.assertEqual("caption", td.frame_hits("steps", base.simple_tokens("Control Panel"), LEX)["noun"].frame)

    def test_a_line_without_a_label_or_a_period_is_a_caption_and_a_labeled_one_is_an_imperative(self):
        tokens = base.simple_tokens("Check oil level")
        self.assertEqual({"noun"}, set(td.frame_hits("steps", tokens, LEX, labeled=False)))
        self.assertEqual({"imperative"}, set(td.frame_hits("steps", tokens, LEX, labeled=True)))
        self.assertEqual("coordinated", td.frame_hits("clauses", base.simple_tokens("The oil and pumps drain the tank ."), LEX)["main"].flag)


class PoolTests(Mini):
    def test_one_candidate_per_sentence_and_the_rarest_kind_takes_it(self):
        scan = scan_of([("army-a", SENTENCES["terms"]["adj"]), ("army-a", SENTENCES["terms"]["noun"]), ("army-a", SENTENCES["terms"]["proper"])])
        pool, stats, drops = td.build_pool(scan, "defined-terms", LEX, sd.SEED)
        self.assertEqual({"adj": 1, "noun": 1, "proper": 1}, {k: len(v) for k, v in pool.items()})
        self.assertEqual(3, stats["frame"])
        self.assertEqual("manual override", pool["adj"][0][1]["term"])
        pool, _, _ = td.build_pool(scan, "defined-terms", LEX, sd.SEED, kinds=("noun",))                      # the controls are left out of a round without them
        self.assertEqual({"noun"}, set(pool))

    def test_banded_targets_lie_in_the_band_and_are_ordered_by_q_with_empty_q_for_controls(self):
        texts = ["The oil check is full 1 .", "The oil check is full 2 .", "The oil check is full 3 .", "The oil check is full 4 .", "You must check the valve 5 ."]
        scan = scan_of([("army-a", t) for t in texts])
        hmm = ft.FakeHmm({" ".join(base.simple_tokens(t)): q for t, q in zip(texts, (0.5, 0.1, 0.05, 0.97, 0.3))})
        pool, stats, _ = td.build_pool(scan, "deverbal-heads", LEX, sd.SEED, hmm)
        self.assertEqual(4, stats["frame"])
        self.assertEqual(2, stats["band"])                                                                  # 0.05 is below the band, 0.97 is not inside it
        self.assertEqual(["0.100", "0.500"], [row["q"] for _, row in pool["other"]])                         # q ascending, hardest first
        scan = scan_of([("army-a", "Check oil level ."), ("army-a", "Control Panel")])
        pool, _, _ = td.build_pool(scan, "step-lines", LEX, sd.SEED, ft.FakeHmm({"Check oil level .": 0.4, "Control Panel": 0.4}), step_kinds={})
        self.assertEqual("", pool["noun"][0][1]["q"])                                                       # a control has no band and an empty q
        self.assertEqual("0.400", pool["noun"][0][1]["q"] or "0.400")
        pool, _, _ = td.build_pool(scan, "steps", LEX, sd.SEED, None, step_kinds={})                         # a training product has no band and no q
        self.assertNotIn("q", pool["noun"][0][1])

    def test_sentences_of_earlier_rounds_are_excluded_and_counted(self):
        scan = scan_of([("army-a", SENTENCES["clauses"]["main"]), ("army-a", SENTENCES["clauses"]["sub"])])
        pool, _, drops = td.build_pool(scan, "clauses", LEX, sd.SEED, exclude_keys={sd.sent_key_of(SENTENCES["clauses"]["main"])})
        self.assertEqual({"sub"}, set(pool))
        self.assertEqual(1, drops["earlier-round"])

    def test_s2_candidates_rank_the_tiers_t1_to_t3_before_t4(self):
        scan = scan_of([("army-a", SENTENCES["terms"]["proper"]), ("army-a", "Contact the Hydraulic Systems Laboratory today .")])
        scan.units.append(("army-a", 9, "Press the Pump Switch button .", base.simple_tokens("Press the Pump Switch button .")))
        pool, _, _ = td.build_pool(scan, "defined-terms", LEX, sd.SEED)
        self.assertEqual(["noun"], list(k for k in pool if k == "noun"))
        self.assertTrue(all(row["tier"] == "T4" for _, row in pool["proper"]))
        ranked = [(True, 1), (False, 5), (False, 2)]
        self.assertEqual([(False, 2), (False, 5), (True, 1)], sorted(ranked))                                 # the rank tuple of S2: (is T4, seeded rank)

    def test_the_cap_function_applies_lemma_document_term_family_and_tier_limits(self):
        def row(n, lemma="check", doc="d1", term="t", tier="T1"):
            return (n, dict(lemma=lemma, source_id=doc, term=term, tier=tier))
        ranked = [row(n) for n in range(10)]
        counts = [collections.Counter() for _ in range(4)]
        self.assertEqual(3, len(td.cap_rows(ranked, 10, 3, 99, counts[0], counts[1])))                            # 3 per lemma
        counts = [collections.Counter() for _ in range(4)]
        self.assertEqual(2, len(td.cap_rows(ranked, 10, 99, 2, counts[0], counts[1])))                            # 2 per document
        counts = [collections.Counter() for _ in range(4)]
        self.assertEqual(2, len(td.cap_rows(ranked, 10, 99, 99, counts[0], counts[1], counts[2], 2)))             # 2 per term
        counts = [collections.Counter() for _ in range(4)]
        self.assertEqual(4, len(td.cap_rows(ranked, 4, 99, 99, counts[0], counts[1])))                            # the limit
        counts = [collections.Counter() for _ in range(4)]
        tiers = [row(n, lemma="l%d" % n, tier="T4" if n % 2 else "T1") for n in range(10)]
        self.assertEqual(3, sum(r["tier"] == "T4" for r in td.cap_rows(tiers, 10, 99, 99, counts[0], counts[1], tier_limit=3)))
        counts = [collections.Counter() for _ in range(4)]
        families = {"d1": "f", "d2": "f", "d3": "g"}
        found = td.cap_rows([row(n, lemma="l%d" % n, doc="d%d" % (n % 3 + 1)) for n in range(9)], 99, 99, 99, counts[0], counts[1], family_cap=2, family_of=families, families=counts[3])
        self.assertEqual({"f": 2, "g": 2}, dict(counts[3]))
        self.assertEqual(4, len(found))
        counts = [collections.Counter({"check": 1}), collections.Counter()]
        self.assertEqual(1, len(td.cap_rows(ranked, 10, lambda lemma: 2, 99, counts[0], counts[1])))             # a function of the lemma (the room of a top-up round)


class CandidateRowTests(Mini):
    def units(self, group, kind, count, source="army-a"):
        """`count` distinct units of one frame (a number inside the text keeps them apart)."""
        template = {("deverbal", "pred"): "This is the oil check on unit %d .", ("deverbal", "other"): "The oil check is full %d .", ("terms", "adj"): "Select the Manual Override mode %d .",
                    ("clauses", "main"): "The pumps drain the tank %d .", ("clauses", "sub"): "When the pumps drain the tank the pressure drops %d .",
                    ("terms", "noun"): "Press the Pump Switch button %d .", ("deverbal", "verb"): "You must check the valve %d .",
                    ("clauses", "noun"): "The pumps drain tank %d ."}[(group, kind)]
        return [(source, template % n) for n in range(1, count + 1)]

    def test_development_s1_candidates_have_prefixes_roles_q_and_controls_for_the_lemmas_of_the_targets(self):
        units = self.units("deverbal", "pred", 3) + self.units("deverbal", "other", 2) + self.units("deverbal", "verb", 3)
        hmm = ft.FakeHmm({" ".join(base.simple_tokens(t)): 0.2 + 0.01 * n for n, (_, t) in enumerate(units)})
        rows, report, counts = td.candidate_rows(scan_of(units), "deverbal-heads", LEX, hmm=hmm)
        by_kind = collections.Counter(r["kind"] for r in rows)
        self.assertEqual({"pred": 3, "other": 1, "verb": 2}, dict(by_kind))                                     # 4 per lemma over the targets (pred first), 2 controls per lemma
        self.assertEqual({"dh-pred-", "dh-other-", "dh-verb-"}, {r["cand_id"].rsplit("c", 1)[0] for r in rows})
        self.assertEqual({("n-deverbal-head", "target")} | {("n-deverbal-head", "control")}, {(r["shape"], r["role"]) for r in rows})
        self.assertTrue(all(r["q"] == "" for r in rows if r["role"] == "control") and all(r["q"] != "" for r in rows if r["role"] == "target"))
        self.assertEqual(len(rows), len({r["cand_id"] for r in rows}))
        self.assertEqual(7 - 1, counts["candidates"])
        self.assertIn("candidates: 6 over 1 lemmas in 1 documents", "\n".join(report))
        rows, _, _ = td.candidate_rows(scan_of(units), "deverbal-heads", LEX, hmm=hmm, old_counts={"check": 3})
        self.assertEqual(1, sum(r["role"] == "target" for r in rows))                                          # 4 per lemma over the union with the 3 old targets

    def test_a_control_of_s1_never_shares_a_sentence_with_a_target_candidate(self):
        text = "You must check the valve 1 ."
        scan = scan_of([("army-a", text), ("army-a", "The oil check is full 1 .")])
        hmm = ft.FakeHmm({" ".join(base.simple_tokens(t)): 0.3 for _, t in [("", text), ("", "The oil check is full 1 .")]})
        rows, _, _ = td.candidate_rows(scan, "deverbal-heads", LEX, hmm=hmm)
        self.assertEqual(2, len(rows))
        self.assertEqual(len(rows), len({r["text"] for r in rows}))

    def test_training_candidates_use_no_band_no_q_and_every_listed_lemma_as_a_verb_control(self):
        units = self.units("deverbal", "pred", 2) + self.units("deverbal", "verb", 2)
        rows, _, counts = td.candidate_rows(scan_of(units), "deverbal", LEX)
        self.assertEqual({"dvt-pred-", "dvt-verb-"}, {r["cand_id"].rsplit("c", 1)[0] for r in rows})
        self.assertTrue(all("q" not in r for r in rows))
        self.assertEqual({"pred": 2, "verb": 2}, {k: v for k, v in counts["kinds"].items() if v})
        self.assertEqual(["dvt-pred-c", "dvt-verb-c"], sorted({r["cand_id"][:10] for r in rows}))

    def test_the_budgets_and_the_caps_of_the_candidates(self):
        units = self.units("clauses", "main", 12) + self.units("clauses", "sub", 12) + self.units("clauses", "noun", 5)
        with mock.patch.dict(td.CANDIDATE_BUDGET, {"clause-verbs": {"main": 4, "sub": 3, "noun": 2, "adj": 1}}):
            rows, _, counts = td.candidate_rows(scan_of(units), "clause-verbs", LEX, hmm=ft.FakeHmm({" ".join(base.simple_tokens(t)): 0.5 for _, t in units}))
        self.assertEqual({"sub": 3, "main": 1, "noun": 2}, {k: v for k, v in counts["kinds"].items() if v})   # 4 per lemma (`drain`) is the cap of the targets of S4, shared by sub and main; 2 controls
        self.assertEqual(4, td.CANDIDATE_LEMMA_CAP["clause-verbs"])
        self.assertEqual(6, td.CANDIDATE_LEMMA_CAP["clauses"])
        self.assertEqual({"dev": 20, "train": 40}, td.CANDIDATE_DOCUMENT_CAP)
        units = [("army-a", t) for _, t in self.units("terms", "noun", 6)]
        with mock.patch.dict(td.CANDIDATE_BUDGET, {"terms": {"noun": 6, "adj": 1, "proper": 1}}), mock.patch.dict(td.CANDIDATE_LEMMA_CAP, {"terms": 99}):
            rows, _, _ = td.candidate_rows(scan_of(units), "terms", LEX)
        self.assertEqual(3, len(rows))                                                                     # 3 per distinct term in a training source: `pump switch` is one term

    def test_the_t4_share_limits_the_low_cue_terms_of_s2(self):
        proper = [("army-a", "Contact the Hydraulic Systems Command %s today ." % w) for w in ("Alpha", "Bravo")]
        scan = scan_of([("army-a", t) for _, t in proper])
        scan.units += [("army-a", 7, "Press the Pump Switch button %d ." % n, base.simple_tokens("Press the Pump Switch button %d ." % n)) for n in range(5)]
        self.assertEqual(20, td.TIER4_SHARE_PERCENT)
        with mock.patch.dict(td.CANDIDATE_BUDGET, {"defined-terms": {"noun": 5, "adj": 5, "proper": 5}}), mock.patch.dict(td.LEMMA_CAP, {}):
            rows, _, _ = td.candidate_rows(scan, "defined-terms", LEX)
        self.assertEqual({"noun"}, {r["kind"] for r in rows if r["kind"] != "proper"})

    def test_the_control_top_up_round_draws_only_controls_for_the_lemmas_with_room_and_never_an_earlier_sentence(self):
        units = self.units("clauses", "noun", 1) + [("army-a", "The pumps fill tank %d ." % n) for n in range(1, 4)]
        scan = scan_of(units)
        first = sd.sent_key_of(units[0][1])
        rows, _, counts = td.candidate_rows(scan, "clause-verbs", LEX, hmm=None, exclude_keys={first}, controls_only=True, room={"fill": 1})
        self.assertEqual({"noun": 1}, {k: v for k, v in counts["kinds"].items() if v})                         # `fill` has room for 1, `drain` is excluded as an earlier sentence
        self.assertEqual(["cvc-noun-"], sorted({r["cand_id"].rsplit("c", 1)[0] for r in rows}))
        self.assertEqual("fill", rows[0]["lemma"])
        rows, _, _ = td.candidate_rows(scan, "clause-verbs", LEX, controls_only=True, room={"fill": 0})
        self.assertEqual(["drain"], sorted({r["lemma"] for r in rows}))                                         # an unseen lemma has the full cap; a lemma with no room is closed
        units = self.units("deverbal", "verb", 3)
        rows, _, _ = td.candidate_rows(scan_of(units), "deverbal-heads", LEX, controls_only=True, room={"check": 1})
        self.assertEqual(1, len(rows))
        rows, _, _ = td.candidate_rows(scan_of(units), "deverbal-heads", LEX, controls_only=True, room={})
        self.assertEqual([], rows)                                                                              # S1 draws only the lemmas of the room

    def test_pilot_rows_are_not_drawn_again_and_count_against_the_caps_of_their_side(self):
        units = self.units("terms", "noun", 3)
        pilot = [dict(cand_id="dp-noun-c00001", text=units[0][1], lemma="switch", source_id="army-a", target=3, kind="noun", term="pump switch", role="target")]
        rows, _, counts = td.candidate_rows(scan_of(units), "terms", LEX, prior_rows=pilot)
        self.assertEqual(2, len(rows))
        self.assertNotIn(units[0][1], {r["text"] for r in rows})
        self.assertEqual({"noun": 1}, counts["prior"])
        self.assertEqual([], td.gate_messages("terms", dict(kinds=dict(noun=1000, adj=0, proper=100), prior={})))

    def test_the_top_up_round_of_s2_does_not_count_the_pilot_controls_twice(self):
        units = [("army-a", "Contact the Hydraulic Systems Command today %d ." % n) for n in range(1, 4)]
        pilot = [dict(cand_id="dp-proper-c00001", text="Another pilot sentence .", lemma="command", source_id="army-b", target=4, kind="proper", term="some other term", role="control")]
        rows, _, counts = td.candidate_rows(scan_of(units), "defined-terms", LEX, prior_rows=pilot, controls_only=True, room={"command": 1})
        self.assertEqual(1, len(rows))                                                                         # the room (2 per lemma minus the kept pilot control) is the whole limit
        self.assertEqual({}, counts["prior"])

    def test_the_size_gates_of_every_product_fail_closed(self):
        enough = {"deverbal-heads": dict(pred=100, num=50, ing=150, other=150, verb=60), "defined-terms": dict(noun=300, adj=200, proper=40),
                  "step-lines": dict(imperative=300, noun=50), "clause-verbs": dict(main=300, sub=200, noun=40, adj=20),
                  "deverbal": dict(pred=150, num=80, ing=300, other=200, verb=100), "terms": dict(noun=700, adj=200, proper=100), "steps": dict(imperative=700, noun=100),
                  "clauses": dict(main=400, sub=100, noun=60, adj=40)}
        for product, kinds in enough.items():
            self.assertEqual([], td.gate_messages(product, dict(kinds=kinds, prior={})), product)
            for label, gated, minimum in td.MIN_CANDIDATES[product]:
                short = dict(kinds, **{k: 0 for k in gated})
                self.assertTrue(td.gate_messages(product, dict(kinds=short, prior={})), (product, label))
        self.assertEqual([], td.gate_messages("defined-terms", dict(kinds=dict(noun=290, adj=200, proper=40), prior={"noun": 10})))     # the pilot rows count

    def test_the_candidate_command_writes_the_round_files_the_extras_and_refuses_s2_without_the_gate(self):
        self.sound_draft({"army-a": [t for _, t in self.units("clauses", "main", 3)] + [SENTENCES["terms"]["noun"]]})
        self.sides(**{"army-a": "train"})
        self.pins()
        code, _, err = self.run_tool("candidates", "--product", "clauses", "--no-ud")
        self.assertEqual(1, code)                                                                               # the size gate (600 candidates) is not met in a fixture
        self.assertIn("GATE FAILED: only 3 candidates candidates (at least 600 needed)", err)
        rows = self.read_candidates("clt")
        self.assertEqual(3, len(rows))
        self.assertEqual({"clt-main-"}, {r["cand_id"].rsplit("c", 1)[0] for r in rows})
        extras = adjudicate.read_records(os.path.join(self.root, "rounds", "clt", "private", "extras.tsv"))[1]
        self.assertEqual(sorted(r["cand_id"] for r in rows), sorted(r["cand_id"] for r in extras))
        self.assertTrue(os.path.exists(os.path.join(self.root, "rounds", "clt", "private", "rejected-documents.tsv")))
        code, _, err = self.run_tool("candidates", "--product", "terms", "--no-ud")
        self.assertEqual(1, code)
        self.assertIn("the S2 pilot gate has not been run", err)
        self.assertFalse(os.path.exists(os.path.join(self.root, "rounds", "dtt")))
        code, _, err = self.run_tool("candidates", "--product", "clauses", "--round", "clvc", "--no-ud")
        self.assertEqual(1, code)
        self.assertIn("is not the control top-up round of clauses", err)

    def test_the_banded_development_command_reads_the_pinned_table_and_writes_both_control_rounds(self):
        texts = [t for _, t in self.units("deverbal", "other", 4)] + [t for _, t in self.units("deverbal", "verb", 3)]
        self.sound_draft({"army-a": texts})
        self.sides(**{"army-a": "dev"})
        self.pins(hmm=True)
        masses = {" ".join(base.simple_tokens(t)): 0.3 for t in texts}
        with mock.patch.object(td, "read_hmm_pinned", return_value=ft.FakeHmm(masses)):
            code, _, err = self.run_tool("candidates", "--product", "deverbal-heads", "--no-ud")
            self.assertEqual(1, code)
            self.assertIn("GATE FAILED", err)
            first = self.read_candidates("dvh")
            self.assertEqual({"check"}, {r["lemma"] for r in first})
            self.assertEqual({"target": 3, "control": 2}, dict(collections.Counter(r["role"] for r in first)))    # 4 per lemma over the union with the one old target of `check`, 2 controls
            self.assertTrue(all("q" in r for r in first))
            code, _, err = self.run_tool("candidates", "--product", "deverbal-heads", "--round", "dvhc", "--no-ud")
            self.assertEqual(1, code)
            self.assertIn("decide the round dvh first", err)
            self.round("dvh", [dict(cand_id=r["cand_id"], text=r["text"], lemma=r["lemma"], source="army-a", target=int(r["target"]), q=r["q"]) for r in first], with_q=True)
            self.sound_draft({"army-a": texts})                                                                  # `round` rewrote the draft: restore the sound one and its pins
            self.pins(hmm=True)
            code, _, err = self.run_tool("candidates", "--product", "deverbal-heads", "--round", "dvhc", "--no-ud")
            self.assertEqual(0, code, err)
            second = self.read_candidates("dvhc")
            self.assertTrue(all(r["role"] == "control" and r["cand_id"].startswith("dhc-verb-") for r in second))
            self.assertEqual(0, len(second))                                                                      # 2 per lemma and 2 kept controls of the first round: no room is left
            self.assertTrue(os.path.exists(os.path.join(self.root, "rounds", "dvhc", "candidates.tsv")))
            self.assertEqual(set(), {r["text"] for r in second} & {r["text"] for r in first})


# ================================================================ the order of the rounds

class RoundOrderTests(Mini):
    def candidates_file(self, name, texts):
        rw = sd.RoundWork(self.root, name)
        os.makedirs(rw.private, exist_ok=True)
        sd.write_candidates(rw, [dict(cand_id="x-c%05d" % n, shape="clause-verb", role="target", target=1, lemma="drain", origin="real", source_id="army-a", line=n, text=t,
                                      tokens=base.simple_tokens(t)) for n, t in enumerate(texts, 1)])

    def test_a_round_draws_neither_the_sentences_of_an_earlier_round_of_its_side_nor_those_of_a_later_one(self):
        self.candidates_file("dtm-pilot", ["Pilot sentence one ."])
        self.candidates_file("dvh", ["Dev sentence one ."])
        self.candidates_file("dvt", ["Train sentence one ."])
        self.assertEqual({sd.sent_key_of("Pilot sentence one .")}, td.earlier_keys(self.work, "dvh", "dev"))
        self.assertEqual({sd.sent_key_of("Pilot sentence one ."), sd.sent_key_of("Dev sentence one .")}, td.earlier_keys(self.work, "dtm", "dev"))
        self.assertEqual({sd.sent_key_of("Pilot sentence one ."), sd.sent_key_of("Train sentence one .")}, td.earlier_keys(self.work, "dtt", "train"))   # never a dev round
        self.assertEqual(set(), td.earlier_keys(self.work, td.PILOT_ROUND, "dev"))
        self.assertEqual(["dvh"], td.later_rounds(self.work, "dtm-pilot", ("dev",)))
        self.assertEqual(["dvh", "dvt"], td.later_rounds(self.work, "dtm-pilot", ("dev", "train")))
        self.assertEqual([], td.later_rounds(self.work, "dvh", ("dev",)))
        self.assertEqual(["dvh"], td.later_rounds(self.work, "dtm-pilot", ("dev",)))
        self.assertEqual([], td.later_rounds(self.work, "clv", ("dev",)))

    def test_the_command_refuses_a_round_while_a_later_one_has_candidates(self):
        self.sound_draft({"army-a": [t for t in SENTENCES["clauses"].values()]})
        self.sides(**{"army-a": "dev"})
        self.pins(hmm=True)
        self.candidates_file("clv", ["Some later sentence here ."])
        with mock.patch.object(td, "read_hmm_pinned", return_value=ft.FakeHmm({})):
            code, _, err = self.run_tool("candidates", "--product", "deverbal-heads", "--no-ud")
        self.assertEqual(1, code)
        self.assertIn("round dvh cannot run while the later round(s) clv already have candidates", err)
        self.assertFalse(os.path.exists(os.path.join(self.root, "rounds", "dvh", "candidates.tsv")))
        code, _, err = self.run_tool("pilot", "--no-ud")
        self.assertEqual(1, code)
        self.assertIn("the pilot cannot run while the later round(s) clv", err)

    def test_the_second_round_never_draws_a_sentence_of_the_first(self):
        texts = [SENTENCES["clauses"]["main"], SENTENCES["clauses"]["adj"], SENTENCES["deverbal"]["pred"]]
        self.sound_draft({"army-a": texts})
        self.sides(**{"army-a": "train"})
        self.pins()
        self.run_tool("candidates", "--product", "deverbal", "--no-ud")
        self.run_tool("candidates", "--product", "clauses", "--no-ud")
        first = {r["text"] for r in self.read_candidates("dvt")}
        second = {r["text"] for r in self.read_candidates("clt")}
        self.assertEqual({SENTENCES["deverbal"]["pred"]}, first)
        self.assertEqual({SENTENCES["clauses"]["main"], SENTENCES["clauses"]["adj"]}, second)
        self.assertEqual(set(), first & second)


# ================================================================ the S2 pilot and its gate

class PilotTests(Mini):
    def test_the_gate_arithmetic_is_40_percent_of_the_noun_stratum_with_at_least_60_candidates(self):
        self.assertEqual((100, 60, 40), (td.PILOT_SIZE, td.PILOT_NOUN_MIN, td.PILOT_GATE_PERCENT))
        self.assertEqual((("noun", 70), ("adj", 20), ("proper", 10)), td.PILOT_QUOTA)
        self.assertEqual(100, sum(n for _, n in td.PILOT_QUOTA))
        self.assertEqual("PROCEED", td.pilot_verdict(28, 70)[0])
        self.assertEqual("STOP", td.pilot_verdict(27, 70)[0])
        self.assertEqual("PROCEED", td.pilot_verdict(24, 60)[0])
        self.assertEqual("STOP", td.pilot_verdict(23, 60)[0])
        self.assertEqual("SCARCITY", td.pilot_verdict(59, 59)[0])
        self.assertIn("fewer than 28 (40 percent)", td.pilot_verdict(27, 70)[1])

    def test_the_pilot_sample_takes_the_quotas_with_the_caps_and_the_t4_limit_and_counts_only(self):
        units = ([("army-%s" % "ab"[n % 2], "Press the %s %s button %d ." % (a, b, n)) for n, (a, b) in enumerate(itertools.product("Pump Switch Control".split(), "Pump Switch Control Panel".split()))]
                 + [("army-a", "Select the Manual Override mode %d ." % n) for n in range(6)] + [("army-b", "Contact the Hydraulic Systems Command today %d ." % n) for n in range(3)])
        scan = scan_of(units)
        family = {"army-a": "fa", "army-b": "fb"}
        with mock.patch.object(td, "PILOT_QUOTA", (("noun", 6), ("adj", 4), ("proper", 2))), mock.patch.object(td, "PILOT_LEMMA_CAP", 2):
            rows, report, counts = td.pilot_candidates(scan, LEX, family)
        by_kind = collections.Counter(r["kind"] for r in rows)
        self.assertEqual({"noun": 6, "adj": 2, "proper": 1}, dict(by_kind))                                       # 2 per lemma (the judged word), 2 per term: `manual override` is one term
        self.assertEqual({"dp-noun-", "dp-adj-", "dp-proper-"}, {r["cand_id"].rsplit("c", 1)[0] for r in rows})
        self.assertEqual(len(rows), len({r["cand_id"] for r in rows}))
        self.assertEqual({"noun": 6, "adj": 2, "proper": 1}, counts["kinds"])
        self.assertNotIn("Press the", "\n".join(report))                                                          # counts only
        with mock.patch.object(td, "PILOT_QUOTA", (("noun", 6), ("adj", 4), ("proper", 2))), mock.patch.object(td, "PILOT_LEMMA_CAP", 9):
            rows, _, _ = td.pilot_candidates(scan, LEX, family, documents={"army-b"})
        self.assertEqual({"army-b"}, {r["source_id"] for r in rows})                                              # the first-wave documents only
        with mock.patch.object(td, "PILOT_QUOTA", (("noun", 6), ("adj", 4), ("proper", 2))), mock.patch.object(td, "PILOT_FAMILY_CAP", 2), mock.patch.object(td, "PILOT_LEMMA_CAP", 9):
            rows, _, _ = td.pilot_candidates(scan, LEX, family)
        self.assertEqual({"fa": 2, "fb": 2}, {f: sum(1 for r in rows if family[r["source_id"]] == f) for f in ("fa", "fb")} if len(rows) == 4 else {"fa": 2, "fb": 2})
        self.assertLessEqual(max(collections.Counter(family[r["source_id"]] for r in rows).values()), 2)           # the family cap is read at title-family level

    def pilot_round(self, noun_tags, adj_tag="ADJ"):
        """Write the decided pilot round: one noun-stratum candidate per element of `noun_tags` ((opus, sol) tags of the judged word) and one adj candidate."""
        specs = []
        for n, (opus, sol) in enumerate(noun_tags, 1):
            specs.append(spec("dp-noun-c%05d" % n, "Press the Pump Switch button %d ." % n, "switch", 3, tier="T1" if n % 2 else "T3", term="pump switch", opus=opus, sol=sol))
        specs.append(spec("dp-adj-c%05d" % (len(noun_tags) + 1), "Select the Manual Override mode 1 .", "manual", 2, tier="T1", term="manual override", opus=adj_tag, sol=adj_tag))
        self.round(td.PILOT_ROUND, specs)

    def test_the_pilot_numbers_confirm_a_noun_only_when_both_labelers_tag_it_noun_and_report_counts(self):
        self.pilot_round([("NOUN", "NOUN")] * 3 + [("PROPN", "PROPN"), ("NOUN", "PROPN")])
        items, extras = td.pilot_items(self.work, self.data)
        confirmed, nouns, lines = td.pilot_numbers(items, extras)
        self.assertEqual((3, 5), (confirmed, nouns))
        text = "\n".join(lines)
        self.assertIn("noun-stratum candidates 5, confirmed 3", text)
        self.assertIn("| PROPN | 1 |", text)
        self.assertIn("| disagreement | 1 |", text)
        self.assertIn("| T1 | 3 | 2 |", text)
        self.assertIn("| adj | 1 | 1 |", text)
        self.assertNotIn("Pump Switch", text)                                                                         # counts only

    def test_the_pilot_result_command_writes_the_verdict_and_exits_1_on_a_stop(self):
        with mock.patch.object(td, "PILOT_NOUN_MIN", 5):
            self.pilot_round([("NOUN", "NOUN")] * 2 + [("PROPN", "PROPN")] * 3)
            code, out, err = self.run_tool("pilot-result")
            self.assertEqual(0, code, err)                                                                        # 2 of 5 is exactly 40 percent
            self.assertIn("gate: PROCEED (2 of 5 confirmed (at least 2): proceed)", out)
            self.assertTrue(common.read_lines(os.path.join(self.root, "rounds", "dtm-pilot", "private", "pilot-verdict.txt"))[0].startswith("PROCEED"))
            self.pilot_round([("NOUN", "NOUN")] * 1 + [("PROPN", "PROPN")] * 4)
            code, out, _ = self.run_tool("pilot-result")
            self.assertEqual(1, code)
            self.assertIn("gate: STOP", out)
            self.assertIn("the other three shapes do not depend on this gate (decision O2)", out)
            self.assertTrue(common.read_lines(os.path.join(self.root, "rounds", "dtm-pilot", "private", "pilot-verdict.txt"))[0].startswith("STOP"))
        self.pilot_round([("NOUN", "NOUN")] * 3)
        code, out, _ = self.run_tool("pilot-result")
        self.assertEqual(1, code)
        self.assertIn("gate: SCARCITY", out)

    def test_a_failed_or_missing_gate_refuses_s2_but_not_the_other_shapes(self):
        self.sound_draft({"army-a": [SENTENCES["terms"]["noun"], SENTENCES["clauses"]["main"]]})
        self.sides(**{"army-a": "train"})
        self.pins()
        with self.assertRaises(td.GateError):
            td.require_pilot(self.work)
        self.pilot_passed("STOP 1 of 5 confirmed")
        with self.assertRaises(td.GateError) as raised:
            td.require_pilot(self.work)
        self.assertIn("nothing of S2", str(raised.exception))
        self.sound_draft({"army-a": [SENTENCES["terms"]["noun"], SENTENCES["clauses"]["main"]]})
        self.pins()
        code, _, err = self.run_tool("candidates", "--product", "terms", "--no-ud")
        self.assertEqual(1, code)
        self.assertIn("the S2 pilot gate did not pass", err)
        code, out, err = self.run_tool("build", "--product", "terms")
        self.assertEqual(1, code)
        self.assertIn("the S2 pilot gate did not pass", err)
        code, out, err = self.run_tool("candidates", "--product", "clauses", "--no-ud")                              # decision O2: the other shapes go on
        self.assertNotIn("refused", err)
        self.assertEqual(["The pumps drain the tank ."], [r["text"] for r in self.read_candidates("clt")])
        self.pilot_passed("PROCEED 28 of 70 confirmed (at least 28): proceed")
        td.require_pilot(self.work)

    def test_the_pilot_command_stops_with_scarcity_and_a_new_pilot_drops_the_old_verdict(self):
        self.sound_draft({"army-a": [SENTENCES["terms"]["noun"]]})
        self.sides(**{"army-a": "train"})
        self.pins()
        self.pilot_passed()
        common.write_text(os.path.join(self.root, "wave1.txt"), ["army-a"])
        code, out, err = self.run_tool("pilot", "--no-ud", "--documents", os.path.join(self.root, "wave1.txt"))
        self.assertEqual(1, code)
        self.assertIn("PILOT SCARCITY: only 1 of at least 60", err)
        self.assertFalse(os.path.exists(os.path.join(self.root, "rounds", "dtm-pilot", "private", "pilot-verdict.txt")))
        self.assertEqual(["dp-noun-c00001"], [r["cand_id"] for r in self.read_candidates("dtm-pilot")])
        self.assertIn("1 candidates written", out)
        code, _, err = self.run_tool("pilot", "--no-ud", "--documents", os.path.join(self.root, "nothing.txt"))
        self.assertEqual(1, code)
        common.write_text(os.path.join(self.root, "wave1.txt"), ["army-z"])
        code, _, err = self.run_tool("pilot", "--no-ud", "--documents", os.path.join(self.root, "wave1.txt"))
        self.assertEqual(1, code)
        self.assertIn("not in the draft", err)

    def test_the_pilot_rows_of_a_side_feed_that_side_and_count_against_its_caps(self):
        self.sound_draft({"army-a": [SENTENCES["terms"]["noun"]], "army-b": [SENTENCES["terms"]["adj"]]})
        self.sides(**{"army-a": "train", "army-b": "dev"})
        self.pilot_round([("NOUN", "NOUN")])
        sides = fu.read_partition(self.work, [dict(source_id="army-a"), dict(source_id="army-b")])
        rows = td.pilot_rows_of_side(self.work, sides, "train")
        self.assertEqual([("noun", "pump switch"), ("adj", "manual override")], [(r["kind"], r["term"]) for r in rows])
        self.assertEqual([], td.pilot_rows_of_side(self.work, sides, "dev"))


# ================================================================ selection

def entry(kind, n, lemma="check", source="army-a", role=None, group="deverbal", **extra):
    """A kept entry of a selection: the decided item with its kind and the fields of the extras table."""
    role = role or ("control" if kind in td.CONTROL_KINDS[group] else "target")
    base_entry = dict(cand_id="x-c%05d" % n, sent_key="k%05d" % n, role=role, kind=kind, lemma=lemma, source_id=source, frame="", unit="", tier="T1", term="", flag="", q="")
    base_entry.update(extra)
    return base_entry


class SelectionTests(unittest.TestCase):
    def test_s1_development_caps_over_the_union_and_the_kinds_and_controls_follow_the_target_lemmas(self):
        entries = ([entry("pred", n, "check", q="0.%03d" % n) for n in range(1, 8)] + [entry("ing", n, "testing") for n in range(20, 23)] + [entry("verb", 40, "check"), entry("verb", 41, "check"),
                   entry("verb", 42, "check"), entry("verb", 43, "pump")])
        targets, controls, drops = td.select_dev("deverbal-heads", entries, old_counts=collections.Counter({"check": 1}))
        self.assertEqual(["x-c00001", "x-c00002", "x-c00003"], [t["cand_id"] for t in targets[:3]])                  # 4 per lemma over the union (1 old), hardest (q) first
        self.assertEqual({"x-c00020", "x-c00021", "x-c00022"}, {t["cand_id"] for t in targets[3:]})
        self.assertEqual(5, drops["lemma-cap"])                                                                     # 4 targets of `check` beyond the cap and the third control of `check`
        self.assertEqual(2, len(controls))                                                                          # 2 per lemma, `pump` is no target lemma
        self.assertEqual({"check"}, {c["lemma"] for c in controls})
        self.assertEqual(1, drops["no-target-lemma"])
        self.assertEqual(360, td.quota_cap("deverbal-heads", "targets"))
        self.assertEqual(90, td.quota_cap("deverbal-heads", "controls"))
        self.assertIsNone(td.quota_cap("defined-terms", "targets"))

    def test_the_stratum_caps_and_the_total_cap_of_development_targets(self):
        entries = [entry("pred", n, "l%d" % n, source="d%d" % (n % 30), q="0.%03d" % n) for n in range(1, 140)]
        with mock.patch.dict(td.DEV_QUOTAS, {"deverbal-heads": (("targets", td.TARGET_KINDS["deverbal"], 0, 50), ("pred", ("pred",), 0, 100), ("controls", ("verb",), 0, 90))}):
            targets, _, drops = td.select_dev("deverbal-heads", entries)
        self.assertEqual(50, len(targets))
        self.assertGreater(drops["cap"], 0)
        self.assertEqual(15, td.DOCUMENT_CAP["dev"])
        entries = [entry("pred", n, "l%d" % n, source="army-a", q="0.%03d" % n) for n in range(1, 40)]
        targets, _, drops = td.select_dev("deverbal-heads", entries)
        self.assertEqual(15, len(targets))                                                                          # 15 per document
        self.assertEqual(24, drops["document-cap"])

    def test_s2_development_takes_adj_and_noun_with_a_term_cap_and_t4_last(self):
        entries = ([entry("noun", n, "l%d" % n, group="terms", term="same term") for n in range(1, 5)] + [entry("noun", 10, "x", group="terms", term="t4 term", tier="T4"),
                   entry("noun", 11, "y", group="terms", term="t1 term", tier="T1"), entry("adj", 20, "manual", group="terms", term="m o"), entry("proper", 30, "laboratory", group="terms", term="a b")])
        targets, controls, drops = td.select_dev("defined-terms", entries)
        self.assertEqual(5, len(targets))
        self.assertEqual("x-c00020", targets[0]["cand_id"])                                                         # adj first (BUILD_ORDER), then noun
        self.assertEqual("x-c00010", targets[-1]["cand_id"])                                                        # T4 candidates come last in the order
        self.assertEqual(2, drops["term-cap"])                                                                      # `same term`: 2 per distinct term (two of the four are dropped)
        self.assertEqual(["laboratory"], [c["lemma"] for c in controls])
        self.assertEqual(2, sum(1 for t in targets if t["term"] == "same term"))

    def test_s3_prefers_bare_object_and_labeled_lines_and_caps_a_title_family(self):
        entries = ([entry("imperative", n, "l%d" % n, group="steps", frame="det", unit="sentence") for n in range(1, 11)]
                   + [entry("imperative", n, "m%d" % n, group="steps", frame="bare", unit="sentence") for n in range(20, 24)]
                   + [entry("imperative", n, "n%d" % n, group="steps", frame="det", unit="enumerated") for n in range(30, 36)])
        with mock.patch.dict(td.DEV_QUOTAS, {"step-lines": (("imperative", ("imperative",), 0, 10), ("controls", ("noun",), 0, 70))}):
            targets, _, _ = td.select_dev("step-lines", entries)
        self.assertEqual(10, len(targets))
        self.assertGreaterEqual(sum(t["frame"] == "bare" for t in targets), 3)                                       # 30 percent of 10
        self.assertGreaterEqual(sum(t["unit"] in td.LABELED_UNITS for t in targets), 5)                              # 50 percent of 10
        family = {"a%d" % n: "f1" for n in range(4)}
        family.update({"c%d" % n: "f2" for n in range(4)})
        entries = [entry("imperative", n, "l%d" % n, group="steps", source=("a%d" if n % 2 else "c%d") % ((n // 2) % 4), frame="det", unit="sentence") for n in range(1, 121)]
        targets, _, drops = td.select_dev("step-lines", entries, family_of=family)
        self.assertEqual(30, sum(1 for t in targets if family[t["source_id"]] == "f2"))
        self.assertEqual(30, sum(1 for t in targets if family[t["source_id"]] == "f1"))                              # 30 per title family
        self.assertGreater(drops["family-cap"], 0)

    def test_s4_development_takes_sub_first_then_main_and_the_controls_per_kind(self):
        entries = ([entry("main", n, "l%d" % n, group="clauses", source="d%d" % (n % 9), q="0.%03d" % n) for n in range(1, 30)]
                   + [entry("sub", n, "s%d" % n, group="clauses", source="e%d" % (n % 9), q="0.9%02d" % n) for n in range(40, 50)]
                   + [entry("noun", 60 + n, "c%d" % n, group="clauses") for n in range(5)] + [entry("adj", 70 + n, "a%d" % n, group="clauses") for n in range(5)])
        targets, controls, _ = td.select_dev("clause-verbs", entries)
        self.assertEqual(["sub"] * 10 + ["main"] * 29, [t["kind"] for t in targets])
        self.assertEqual({"noun": 5, "adj": 5}, dict(collections.Counter(c["kind"] for c in controls)))
        with mock.patch.dict(td.DEV_QUOTAS, {"clause-verbs": (("targets", td.TARGET_KINDS["clauses"], 0, 12), ("sub", ("sub",), 0, None), ("controls", ("noun", "adj"), 0, 6),
                                                              ("noun", ("noun",), 0, 4), ("adj", ("adj",), 0, 3))}):
            targets, controls, drops = td.select_dev("clause-verbs", entries)
        self.assertEqual((12, ["sub"] * 10 + ["main"] * 2), (len(targets), [t["kind"] for t in targets]))
        self.assertEqual({"noun": 4, "adj": 2}, dict(collections.Counter(c["kind"] for c in controls)))              # the cap of the kind (noun 4) and the total of the controls (6)

    def test_training_selection_caps_the_contrast_share_the_strata_and_the_lemmas(self):
        kept = ([entry("pred", n, "l%d" % (n % 80), group="deverbal", source="d%d" % (n % 40)) for n in range(1, 300)]
                + [entry("verb", 500 + n, "v%d" % (n % 80), group="deverbal", source="d%d" % (n % 40)) for n in range(60)])
        chosen, drops = td.select_train("deverbal", kept)
        kinds = collections.Counter(c["kind"] for c in chosen)
        self.assertLessEqual(len(chosen), 800)
        self.assertLessEqual(kinds["pred"], 250)                                                                    # the stratum cap
        self.assertLessEqual(kinds["verb"] * 100, 15 * len(chosen))                                                 # the contrast share
        self.assertGreater(kinds["verb"], 0)
        self.assertLessEqual(max(collections.Counter(c["lemma"] for c in chosen).values()), 4)                      # 4 per lemma over both strata
        self.assertLessEqual(max(collections.Counter(c["source_id"] for c in chosen).values()), 20)
        kept = [entry("proper", n, "p%d" % n, group="terms", source="d%d" % (n % 9), term="t%d" % n) for n in range(1, 100)] + [entry("noun", 200 + n, "n%d" % n, group="terms", term="u%d" % n,
                                                                                                                       source="d%d" % (n % 9)) for n in range(1, 50)]
        chosen, _ = td.select_train("terms", kept)
        kinds = collections.Counter(c["kind"] for c in chosen)
        self.assertEqual((49, 16), (kinds["noun"], kinds["proper"]))                                                # proper at most 25 percent: 49 * 25 // 75 = 16
        self.assertEqual({"terms": (15, 25)}, {"terms": td.TRAIN_SHARES["terms"]})

    def test_a_sentence_is_taken_once_and_the_selector_counts(self):
        selector = td.Selector(4, 15)
        first, second = entry("pred", 1, "check"), entry("verb", 2, "check", sent_key="k00001")
        self.assertIsNone(selector.refusal(first))
        selector.take(first)
        self.assertEqual("same-sentence", selector.refusal(second))
        derived = selector.derived(2)
        self.assertEqual("same-sentence", derived.refusal(second))                                                  # the controls share the sentence set
        self.assertEqual(0, derived.lemmas["check"])
        self.assertEqual(1, derived.documents["army-a"])
        drops = collections.Counter()
        taken = td.choose(td.Selector(4, 15), [entry("pred", n, "l%d" % n) for n in range(5)], 3, drops)
        self.assertEqual((3, 2), (len(taken), drops["cap"]))


# ================================================================ the build

def many(prefix, template, count, target, lemma="w%d", first=1, source="army-a", **extra):
    """`count` specs `<prefix>c<NNNNN>` with the text `template % number` and a lemma of their own (`lemma % number`, or one lemma when it has no placeholder)."""
    return [spec("%sc%05d" % (prefix, first + n), template % (first + n), (lemma % (first + n)) if "%" in lemma else lemma, target, source, **extra) for n in range(count)]


def built_sentence(product, kind, number, lemma="w", doc="army-a", term="", frame="", unit=""):
    """A built sentence of `kind` (the judged word at index 2 with the exact tag of its kind) for the quota checks."""
    group = td.GROUP[product]
    tokens = ["Tank", "oil", "check", "%d" % number, "."]
    target = 0 if group == "steps" else 2
    tags = tags_for(tokens, group, kind, target)
    entry_ = dict(kind=kind, role="control" if kind in td.CONTROL_KINDS[group] else "target", target=target, lemma=lemma, term=term, frame=frame, unit=unit)
    sentence = dict(sent_id="", source_id=doc, text=" ".join(tokens), rows=[[t, g, "unanimous"] for t, g in zip(tokens, tags)])
    return td.final_sentence(product, entry_, sentence, number)


class BuildDevTests(Mini):
    def test_s1_orders_by_q_caps_the_union_and_takes_controls_for_the_target_lemmas(self):
        specs = (many("dh-pred-", "This is the oil check on unit %d .", 6, 4, lemma="check", q="0.300") + many("dh-other-", "The oil check is full %d .", 2, 2, lemma="seal", first=20, q="0.200")
                 + many("dh-verb-", "You must check the valve %d .", 3, 2, lemma="check", first=40, q="") + many("dh-verb-", "You must pump the valve %d .", 1, 2, lemma="pump", first=50, q=""))
        for n, s in enumerate(s_ for s_ in specs if s_["cand_id"].startswith("dh-pred-")):
            s["q"] = "0.%03d" % (500 - n)
        self.sides(**{"army-a": "dev"})
        built, report, out = self.build("deverbal-heads", {"dvh": (specs, True)}, allow_short=True)
        _, sentences = common.read_corpus(os.path.join(out, "deverbal-heads.tsv"))
        self.assertEqual(["shapes-dev-deverbal-heads-%04d" % n for n in range(1, len(built) + 1)], [s["sent_id"] for s in sentences])
        targets = [s for s in sentences if s["comments"]["role"] == "target"]
        # the fixture compound-head.tsv holds one target of `check`: 3 more fit (4 over the union); the hardest (lowest q) first
        self.assertEqual(["This is the oil check on unit %d ." % n for n in (6, 5, 4)], [s["text"] for s in targets if s["comments"]["lemma"] == "check"])
        self.assertEqual({"pred": 3, "other": 2}, dict(collections.Counter(s["comments"]["kind"] for s in targets)))
        controls = [s for s in sentences if s["comments"]["role"] == "control"]
        self.assertEqual(2, len(controls))                                                                 # `check` 2 per lemma; `pump` is no target lemma
        self.assertEqual({"check"}, {s["comments"]["lemma"] for s in controls})
        self.assertEqual({"n-deverbal-head"}, {s["comments"]["shape"] for s in sentences})
        self.assertEqual({"real"}, {s["comments"]["origin"] for s in sentences})
        counts = drops_of(report)
        self.assertEqual({"lemma-cap": 4, "no-target-lemma": 1}, {k: v for k, v in counts.items() if k in ("lemma-cap", "no-target-lemma")})
        lines = common.read_lines(os.path.join(out, "deverbal-heads.tsv"))
        self.assertEqual("# use = development only: select settings here, never train on it, never report it as a held-out figure", lines[6])
        self.assertIn("kind (pred | num | ing | other | verb)", lines[4])
        self.assertEqual({"deverbal-heads.tsv", "deverbal-heads-sources.tsv"}, set(os.listdir(out)))

    def test_s2_needs_the_pilot_gate_and_writes_the_term_and_the_exact_kinds(self):
        specs = (many("dt-noun-", "Press the Pump Switch button %d .", 3, 3, lemma="switch", term="pump switch") + many("dt-adj-", "Select the Manual Override mode %d .", 2, 2, lemma="manual",
                                                                                                                       first=10, term="manual override")
                 + many("dt-proper-", "Contact the Hydraulic Systems Command today %d .", 2, 4, lemma="command", first=20, term="hydraulic systems command"))
        self.sides(**{"army-a": "dev"})
        with self.assertRaises(td.GateError):
            self.build("defined-terms", {"dtm": (specs, False)}, allow_short=True)
        self.pilot_passed()
        built, report, out = self.build("defined-terms", {"dtm": (specs, False)}, allow_short=True)
        _, sentences = common.read_corpus(os.path.join(out, "defined-terms.tsv"))
        by_kind = collections.defaultdict(list)
        for s in sentences:
            by_kind[s["comments"]["kind"]].append(s)
        # 2 per distinct term over the whole tranche: one `pump switch` sentence and no more than two of each term
        self.assertEqual({"noun": 2, "adj": 2, "proper": 2}, {k: len(v) for k, v in by_kind.items()})
        self.assertEqual(1, drops_of(report)["term-cap"])
        self.assertEqual(["adj", "adj", "noun", "noun", "proper", "proper"], [s["comments"]["kind"] for s in sorted(sentences, key=lambda s: (s["comments"]["role"] == "control", s["comments"]["kind"]))])
        self.assertEqual({"term-capitalized"}, {s["comments"]["shape"] for s in sentences})
        self.assertEqual({"control"}, {s["comments"]["role"] for s in by_kind["proper"]})
        self.assertEqual({"manual override"}, {s["comments"]["term"] for s in by_kind["adj"]})
        self.assertEqual({"PROPN"}, {s["rows"][int(s["comments"]["target"])][1] for s in by_kind["proper"]})
        self.assertEqual({"ADJ"}, {s["rows"][int(s["comments"]["target"])][1] for s in by_kind["adj"]})

    def test_s3_writes_the_frame_and_the_unit_and_gates_the_shares_of_bare_and_labeled_lines(self):
        specs = (many("sl-imperative-", "Check oil level %d .", 4, 0, frame="bare", unit="enumerated") + many("sl-imperative-", "Check the valve %d .", 6, 0, first=10, frame="det", unit="sentence")
                 + many("sl-noun-", "Control Panel %d", 2, 0, first=30, lemma="control", frame="caption", unit="short"))
        self.sides(**{"army-a": "dev"})
        built, _, out = self.build("step-lines", {"stp": (specs, True)}, allow_short=True)
        _, sentences = common.read_corpus(os.path.join(out, "step-lines.tsv"))
        self.assertEqual({"step-line"}, {s["comments"]["shape"] for s in sentences})
        self.assertEqual(["bare", "det"], sorted({s["comments"]["frame"] for s in sentences if s["comments"]["kind"] == "imperative"}))
        self.assertEqual({"enumerated", "sentence"}, {s["comments"]["unit"] for s in sentences if s["comments"]["kind"] == "imperative"})
        self.assertEqual({"noun"}, {s["comments"]["kind"] for s in sentences if s["comments"]["role"] == "control"})
        poor = [built_sentence("step-lines", "imperative", n, lemma="l%d" % n, frame="det", unit="sentence") for n in range(1, 11)]
        found = "\n".join(td.quota_problems("step-lines", poor, floors={}))
        self.assertEqual("", found)                                                                          # --allow-short waives the two shares
        found = "\n".join(td.quota_problems("step-lines", poor))
        self.assertIn("bare-object lines: 0 of 10 imperatives, at least 30 percent needed", found)
        self.assertIn("enumerated or bulleted lines: 0 of 10 imperatives, at least 50 percent needed", found)
        self.assertIn("imperative: 10, the floor is 180", found)

    def test_s4_writes_main_and_sub_targets_and_noun_and_adj_controls(self):
        specs = (many("cv-main-", "The pumps drain the tank %d .", 2, 2, lemma="drain") + many("cv-sub-", "When the pumps drain the tank the pressure drops %d .", 2, 3, lemma="fill", first=10)
                 + many("cv-noun-", "The pumps drain tank %d .", 2, 2, lemma="drain", first=20) + many("cv-adj-", "The open pumps are full %d .", 2, 1, lemma="open", first=30))
        self.sides(**{"army-a": "dev"})
        built, report, out = self.build("clause-verbs", {"clv": (specs, False)}, allow_short=True)
        _, sentences = common.read_corpus(os.path.join(out, "clause-verbs.tsv"))
        self.assertEqual(["main", "main", "sub", "sub", "noun", "noun", "adj", "adj"], [s["comments"]["kind"] for s in sentences])        # targets in the order of the kinds, then the controls
        self.assertEqual({"v-clause-ambiguous"}, {s["comments"]["shape"] for s in sentences})
        kinds = collections.Counter(s["comments"]["kind"] for s in sentences)
        self.assertEqual({"main": 2, "sub": 2, "noun": 2, "adj": 2}, dict(kinds))
        self.assertEqual({"VERB"}, {s["rows"][int(s["comments"]["target"])][1] for s in sentences if s["comments"]["role"] == "target"})
        self.assertEqual({"ADJ"}, {s["rows"][int(s["comments"]["target"])][1] for s in sentences if s["comments"]["kind"] == "adj"})
        out2 = os.path.join(self.temp, "out2")
        with mock.patch.object(td, "draft_gate"):
            td.build(self.work, "clause-verbs", out2, data_dir=self.data, allow_short=True)
        self.assertEqual(ft.read_bytes_of(out), ft.read_bytes_of(out2))                                      # deterministic

    def test_the_development_quota_gates_have_floors_and_caps_for_every_product(self):
        self.sides(**{"army-a": "dev"})
        self.round("dvh", many("dh-pred-", "This is the oil check on unit %d .", 2, 4, lemma="l%d", q="0.3"), with_q=True)
        out = os.path.join(self.temp, "out")
        with mock.patch.object(td, "draft_gate"):
            with self.assertRaises(td.BuildError) as raised:
                td.build(self.work, "deverbal-heads", out, data_dir=self.data)
        for part in ("targets: 2, the floor is 240", "pred: 2, the floor is 40", "num: 0, the floor is 30", "ing: 0, the floor is 80", "controls: 0, the floor is 50"):
            self.assertIn(part, str(raised.exception))
        self.assertFalse(os.path.exists(out))                                                                  # nothing is written
        self.assertEqual({"deverbal-heads": (("targets", td.TARGET_KINDS["deverbal"], 240, 360), ("pred", ("pred",), 40, 100), ("num", ("num",), 30, 60), ("ing", ("ing",), 80, 140),
                                            ("other", ("other",), 0, 100), ("controls", ("verb",), 50, 90)),
                          "defined-terms": (("noun", ("noun",), 120, 200), ("adj", ("adj",), 40, 80), ("controls", ("proper",), 40, 70)),
                          "step-lines": (("imperative", ("imperative",), 180, 300), ("controls", ("noun",), 40, 70)),
                          "clause-verbs": (("targets", ("main", "sub"), 240, 360), ("sub", ("sub",), 60, None), ("controls", ("noun", "adj"), 50, 90), ("noun", ("noun",), 35, 60),
                                           ("adj", ("adj",), 15, 30))}, td.DEV_QUOTAS)
        over = [built_sentence("deverbal-heads", "pred", n, lemma="l%d" % n, doc="d%d" % n) for n in range(1, 102)]
        found = "\n".join(td.quota_problems("deverbal-heads", over, floors={}))
        self.assertIn("pred: 101, the cap is 100", found)
        self.assertNotIn("floor", found)
        found = "\n".join(td.quota_problems("deverbal-heads", [built_sentence("deverbal-heads", "pred", n, lemma="check") for n in range(1, 4)], floors={}, old_counts={"check": 2}))
        self.assertIn("target lemma check has 5 items, at most 4 allowed", found)
        found = "\n".join(td.quota_problems("deverbal-heads", [built_sentence("deverbal-heads", "verb", n, lemma="check") for n in range(1, 4)], floors={}))
        self.assertIn("control lemma check has 3 items, at most 2 allowed", found)
        found = "\n".join(td.quota_problems("defined-terms", [built_sentence("defined-terms", "noun", n, lemma="l%d" % n, term="same", doc="d%d" % n) for n in range(1, 4)], floors={}))
        self.assertIn("term same has 3 items, at most 2 allowed", found)
        found = "\n".join(td.quota_problems("step-lines", [built_sentence("step-lines", "imperative", n, lemma="l%d" % n, frame="bare", unit="enumerated") for n in range(1, 17)], floors={}))
        self.assertIn("document army-a has 16 items, at most 15 allowed", found)
        found = "\n".join(td.quota_problems("step-lines", [built_sentence("step-lines", "imperative", n, lemma="l%d" % n, doc="d%d" % n, frame="bare", unit="enumerated") for n in range(1, 32)],
                                           floors={}, family_of={"d%d" % n: "one family" for n in range(1, 32)}))
        self.assertIn("title family one family has 31 items, at most 30 allowed", found)
        found = "\n".join(td.quota_problems("clause-verbs", [built_sentence("clause-verbs", "main", n, lemma="l%d" % n, doc="d%d" % n) for n in range(1, 5)]))
        self.assertIn("sub: 0, the floor is 60", found)

    def test_a_sentence_that_another_product_of_the_side_holds_refuses_the_build(self):
        self.sides(**{"army-a": "dev"})
        s4 = many("cv-main-", "The pumps drain the tank %d .", 2, 2, lemma="w%d")
        out = os.path.join(self.temp, "out")
        self.build("clause-verbs", {"clv": (s4, False)}, out=out, allow_short=True)
        shared = many("sl-imperative-", "The pumps drain the tank %d .", 1, 0, first=1, lemma="the", frame="det", unit="sentence")
        with self.assertRaises(td.BuildError) as raised:
            self.build("step-lines", {"stp": (shared, True)}, out=out, allow_short=True)
        self.assertIn("1 sentences of step-lines are also in clause-verbs", str(raised.exception))
        self.assertFalse(os.path.exists(os.path.join(out, "step-lines.tsv")))
        other_out = os.path.join(self.temp, "other")
        built, _, _ = self.build("step-lines", {"stp": (shared, True)}, out=other_out, allow_short=True)       # another folder has no such sentence
        self.assertEqual(1, len(built))


class BuildTrainTests(Mini):
    def test_terms_noun_adj_and_the_proper_contrast_with_unanimous_tokens_and_the_shared_records(self):
        self.pilot_passed()
        specs = (many("dtt-noun-", "Press the Pump Switch button %d .", 6, 3, lemma="w%d", term="t%d") + many("dtt-adj-", "Select the Manual Override mode %d .", 3, 2, first=20, term="m%d")
                 + many("dtt-proper-", "Contact the Hydraulic Systems Command today %d .", 2, 4, first=40, term="p%d")
                 + [dict(many("dtt-noun-", "Press the Pump Switch button %d .", 1, 3, first=60, term="x")[0], statuses=["unanimous", "unanimous", "unanimous", "disputed", "unanimous", "unanimous", "unanimous"])])
        for s in specs:
            s["term"] = s["term"] % int(s["cand_id"][-5:]) if "%d" in s["term"] else s["term"]
        self.sides(**{"army-a": "train"})
        built, report, out = self.build("terms", {"dtt": (specs, False)}, allow_short=True)
        _, sentences = common.read_corpus(os.path.join(out, "terms.tsv"))
        self.assertEqual(["terms-%04d" % n for n in range(1, len(sentences) + 1)], [s["sent_id"] for s in sentences])
        kinds = collections.Counter(s["comments"]["stratum"] for s in sentences)
        self.assertEqual({"noun": 6, "adj": 3, "proper": 2}, dict(kinds))
        self.assertEqual(1, drops_of(report)["disputed-token"])
        self.assertTrue(all(row[2] == "unanimous" for s in sentences for row in s["rows"]))
        self.assertEqual({"word", "lemma", "stratum", "term"}, {k for s in sentences for k in s["comments"] if k in ("word", "lemma", "stratum", "term")})
        self.assertEqual({"terms.tsv", "terms-sources.tsv", "terms-dropped-sentences.tsv", "excluded-documents.tsv"}, set(os.listdir(out)))
        self.assertNotIn("PRODUCT", "\n".join(common.read_lines(os.path.join(out, "terms.tsv"))[:7]))
        self.assertEqual("# use = training", common.read_lines(os.path.join(out, "terms.tsv"))[6])

    def test_steps_use_the_noun_initial_label_cap_it_at_30_percent_and_record_frame_and_unit(self):
        specs = (many("stt-imperative-", "Check oil level %d .", 10, 0, lemma="l%d", frame="bare", unit="enumerated") + many("stt-noun-", "Control Panel %d", 8, 0, first=30, lemma="c%d", frame="caption",
                                                                                                                       unit="short"))
        self.sides(**{"army-a": "train"})
        built, report, out = self.build("steps", {"stt": (specs, False)}, allow_short=True)
        _, sentences = common.read_corpus(os.path.join(out, "steps.tsv"))
        kinds = collections.Counter(s["comments"]["stratum"] for s in sentences)
        self.assertEqual({"imperative": 10, "noun-initial": 4}, dict(kinds))                                         # at most 30 percent of the file: 4 of 14
        self.assertEqual(4, drops_of(report)["cap"])
        self.assertEqual({"bare"}, {s["comments"]["frame"] for s in sentences if s["comments"]["stratum"] == "imperative"})
        self.assertIn("stratum (imperative | noun-initial)", "\n".join(common.read_lines(os.path.join(out, "steps.tsv"))[:7]))
        self.assertEqual(["noun"], sorted({td.kind_of_label("steps", s["comments"]["stratum"]) for s in sentences if s["comments"]["stratum"] != "imperative"}))

    def test_deverbal_and_clauses_builds_report_the_kinds_and_cap_the_contrast(self):
        self.sides(**{"army-a": "train"})
        specs = many("dvt-pred-", "This is the oil check on unit %d .", 12, 4, lemma="w%d") + many("dvt-verb-", "You must check the valve %d .", 4, 2, first=40, lemma="v%d")
        built, report, out = self.build("deverbal", {"dvt": (specs, False)}, allow_short=True)
        kinds = collections.Counter(td.kind_of_label("deverbal", s["comments"]["stratum"]) for s in common.read_corpus(os.path.join(out, "deverbal.tsv"))[1])
        self.assertEqual({"pred": 12, "verb": 2}, dict(kinds))                                                       # at most 15 percent: 12 * 15 // 85 = 2
        self.assertIn("- kinds: pred 12, verb 2", report)
        specs = many("clt-main-", "The pumps drain the tank %d .", 8, 2, lemma="w%d") + many("clt-noun-", "The pumps drain tank %d .", 3, 2, first=20, lemma="n%d")
        built, _, out = self.build("clauses", {"clt": (specs, False)}, allow_short=True)
        kinds = collections.Counter(s["comments"]["stratum"] for s in common.read_corpus(os.path.join(out, "clauses.tsv"))[1])
        self.assertEqual({"main": 8, "noun": 2}, dict(kinds))                                                        # at most 20 percent: 8 * 20 // 80 = 2

    def test_the_excluded_documents_record_is_shared_and_holds_the_omissions_and_the_rejections(self):
        self.sides(**{"army-a": "train", "army-b": "train"})
        for name in ("dvt", "dtt", "stt", "clt"):
            rw = sd.RoundWork(self.root, name)
            os.makedirs(rw.private, exist_ok=True)
            adjudicate.write_rows(rw.private_file("rejected-documents.tsv"), ("source_id", "reason"), [("army-rej", "3 sentences are also in a set")])
        specs = many("dvt-pred-", "This is the oil check on unit %d .", 4, 4, lemma="w%d") + many("dvt-pred-", "This is the oil check on unit %d .", 4, 4, first=10, lemma="x%d", source="army-b")
        built, report, out = self.build("deverbal", {"dvt": (specs, False)}, allow_short=True, omit=["army-b=state co-author"])
        self.assertEqual(4, len(built))
        self.assertEqual({"army-a"}, {s["source_id"] for s in built})
        self.assertEqual(4, drops_of(report)["omitted-document"])
        lines = common.read_lines(os.path.join(out, "excluded-documents.tsv"))
        self.assertEqual(["army-rej\t3 sentences are also in a set", "army-b\tstate co-author"], lines[3:])
        self.assertEqual(["deverbal-%04d" % n for n in range(1, 5)], [s["sent_id"] for s in common.read_corpus(os.path.join(out, "deverbal.tsv"))[1]])        # no gap
        with self.assertRaises(td.BuildError):
            self.build("deverbal", {"dvt": (specs, False)}, allow_short=True, omit=["army-zz"])
        sources = adjudicate.read_records(os.path.join(out, "deverbal-sources.tsv"))[1]
        self.assertEqual(["army-a"], [r["source_id"] for r in sources])


# ================================================================ the hand check

class ReviewTests(Mini):
    def population(self, product, extra=5):
        """Built sentences of every kind of the plan of `product` (`extra` more than the plan draws), one document each so that no cap matters."""
        out, number = [], 0
        for kind, count in td.SAMPLE_PLAN[product]:
            for _ in range(count + extra):
                number += 1
                out.append(built_sentence(product, kind, number, lemma="w%d" % number, doc="army-a", term="t%d" % number, frame="bare", unit="enumerated"))
        return out

    def test_the_plan_of_every_product_and_the_seeds(self):
        self.assertEqual(dict(zip(td.PRODUCTS, range(20261201, 20261209))), td.SAMPLE_SEEDS)
        self.assertEqual({p: 60 for p in td.PRODUCTS}, {p: sum(n for _, n in plan) for p, plan in td.SAMPLE_PLAN.items()})
        self.assertEqual((60, 4, 0.95, 1), (td.SAMPLE_SIZE, td.SAMPLE_CONTROLS, td.PRECISION_MIN, td.CONTROLS_ACCEPTED_MAX))
        for product, plan in td.SAMPLE_PLAN.items():
            self.assertEqual(set(td.KINDS[td.GROUP[product]]), {kind for kind, _ in plan}, product)             # every kind (controls included) is reviewed

    def test_each_product_draws_its_quota_with_four_swapped_controls_and_is_seeded(self):
        for product in td.PRODUCTS:
            sentences = self.population(product)
            sample = td.build_sample(product, sentences)
            real = [s for kind, s, _ in sample if kind == "real"]
            self.assertEqual(60, len(real), product)
            self.assertEqual(4, sum(1 for kind, _, _ in sample if kind == "control"), product)
            counts = collections.Counter(td.judged(product, s)[1] for s in real)
            self.assertEqual(dict(td.SAMPLE_PLAN[product]), dict(counts), product)
            for kind, s, shown in sample:
                tag = s["rows"][td.judged(product, s)[0]][1]
                self.assertEqual(tag if kind == "real" else fu.swapped_tag(tag), shown)
            again = td.build_sample(product, self.population(product))
            self.assertEqual([s["sent_id"] for _, s, _ in sample], [s["sent_id"] for _, s, _ in again])
            self.assertNotEqual([s["sent_id"] for _, s, _ in sample], [s["sent_id"] for _, s, _ in td.build_sample(product, sentences, seed=1)])

    def test_a_short_kind_gives_fewer_items_and_a_smaller_size_scales_every_kind(self):
        sentences = [s for s in self.population("clause-verbs") if td.judged("clause-verbs", s)[1] != "adj"]
        sample = td.build_sample("clause-verbs", sentences)
        self.assertEqual(52, sum(1 for kind, _, _ in sample if kind == "real"))
        small = td.build_sample("clause-verbs", self.population("clause-verbs"), size=10, controls=2)
        self.assertEqual(10, sum(1 for kind, _, _ in small if kind == "real"))                                  # 24, 18, 10 and 8 scaled by 10/60 and rounded

    def test_the_sample_file_and_the_review_round_trip(self):
        out = os.path.join(self.temp, "out")
        sentences = self.population("steps")
        common.write_corpus(os.path.join(out, "steps.tsv"), td.header_lines("steps"), sentences)
        base.table_file(os.path.join(out, "steps-sources.tsv"), sd.SOURCE_COLUMNS, [self.source_row("army-a", sentences=str(len(sentences)))])
        code, printed, err = self.run_tool("sample", "--product", "steps", "--out-dir", out, "--size", "8", "--controls", "4")
        self.assertEqual(0, code, err)
        self.assertIn("12 items: 8 real, 4 control (seed 20261207)", printed)
        folder = os.path.join(self.root, "review", "steps")
        text = "\n".join(common.read_lines(os.path.join(folder, "review-sample.md")))
        self.assertIn("Marked word: Tank (index 0)", text)
        _, keys = adjudicate.read_records(os.path.join(folder, "review-sample-key.tsv"))
        verdicts = os.path.join(self.temp, "answers.txt")
        common.write_text(verdicts, ["%s\t%s\t-\tcite" % (k["item"], "incorrect" if k["kind"] == "control" else "correct") for k in keys])
        code, printed, err = self.run_tool("review-result", "--product", "steps", "--verdicts", verdicts)
        self.assertEqual(0, code, err)
        self.assertIn("gate: PASSED", printed)
        common.write_text(verdicts, ["%s\tcorrect\t-\tcite" % k["item"] for k in keys])
        self.assertEqual(1, self.run_tool("review-result", "--product", "steps", "--verdicts", verdicts)[0])           # every control accepted: too lenient
        common.write_text(verdicts, ["R999\tcorrect\t-\tcite"])
        code, _, err = self.run_tool("review-result", "--product", "steps", "--verdicts", verdicts)
        self.assertEqual(1, code)
        self.assertIn("unknown items", err)
        code, printed, _ = self.run_tool("report", "--product", "steps", "--out-dir", out)
        self.assertEqual(0, code)
        self.assertIn("kinds: imperative 45, noun-initial 25", printed)


# ================================================================ counts-only preflight

class PreflightTests(Mini):
    def test_the_preflight_prints_counts_only_per_side(self):
        dev_texts = [SENTENCES["deverbal"]["pred"], SENTENCES["terms"]["adj"], SENTENCES["clauses"]["main"]]
        train_texts = [SENTENCES["deverbal"]["other"], SENTENCES["terms"]["noun"], SENTENCES["clauses"]["sub"]]
        self.sound_draft({"army-a": dev_texts, "army-b": train_texts})
        self.sides(**{"army-a": "dev", "army-b": "train"})
        code, out, err = self.run_tool("preflight", "--no-ud")
        self.assertEqual(0, code, err)
        self.assertIn("documents in the draft: 2 (dev 1, train 1; 1 agency families)", out)
        self.assertIn("clean sentences: 6; clean step units: not tokenized yet (run `steps`)", out)
        self.assertIn("dev: 3 clean sentences; deverbal pred 1, num 0, ing 0, other 0; terms proper 0, adj 1, noun 0; clauses adj 0, noun 0, sub 0, main 1", out)
        self.assertIn("train: 3 clean sentences; deverbal pred 0, num 0, ing 0, other 1; terms proper 0, adj 0, noun 1; clauses adj 0, noun 0, sub 1, main 0", out)
        for text in dev_texts + train_texts:
            self.assertNotIn(text, out)                                                                          # counts only
        self.assertEqual({"exclusions.tsv"} & set(os.listdir(self.root)), set())                                  # nothing is written, nothing is labeled
        common.write_text(os.path.join(self.root, "steps", "army-a.txt"), ["Check oil level ."])
        common.write_text(os.path.join(self.root, "steps", "army-b.txt"), ["Check the valve ."])
        common.write_text(os.path.join(self.root, "tokens.tsv"), common.read_lines(os.path.join(self.root, "tokens.tsv")) + ["Check oil level .\t1\tCheck oil level .", "Check the valve .\t1\tCheck the valve ."])
        adjudicate.write_rows(os.path.join(self.root, "step-kinds.tsv"), ("sent_key", "kind"), [(sd.sent_key_of("Check oil level ."), "enumerated"), (sd.sent_key_of("Check the valve ."), "sentence")])
        code, out, err = self.run_tool("preflight", "--no-ud")
        self.assertEqual(0, code, err)
        self.assertIn("clean step units: 2", out)
        self.assertIn("steps noun 0, imperative 1", out)


# ================================================================ the frozen prompt and the registered constants

OLD_WORK = ft.OLD_WORK
NEW_WORK = ft.NEW_WORK


class PromptTests(unittest.TestCase):
    def test_the_template_and_the_appendix_are_the_frozen_ones_of_followup_data(self):
        self.assertIs(fu.LABELER_TEMPLATE, td.LABELER_TEMPLATE)
        self.assertIs(fu.LABELER_APPENDIX, td.LABELER_APPENDIX)
        self.assertEqual(fu.PR19_TEMPLATE_SHA256, td.template_sha256(OLD_WORK))
        self.assertEqual("9f1da5238dbcd196ca95b0795194245846955d2bb3a95a70bf0540d112d17db5", td.template_sha256(OLD_WORK))
        self.assertEqual(1903, len(td.LABELER_TEMPLATE.replace("<W>", OLD_WORK).encode("utf-8")))
        self.assertEqual("7b5a24f2c5acfd540e571cedd18983632cddfc961a75608ec68b49e836b84ce7", td.template_sha256(NEW_WORK))
        self.assertEqual("27ba85551969b1ea3225c2417f6b1a66215c3a12c77b3cbe62c6af8b3c49ed19", hashlib.sha256(td.LABELER_APPENDIX.encode("utf-8")).hexdigest())
        for word in ("stratum", "source", "lemma", "compound", "heading", "imperative", "intended", "defined term", "deverbal", "clause"):
            self.assertNotIn(word, td.LABELER_TEMPLATE.lower(), word)                                           # no hint of the shape reaches the labeler

    def test_the_run_prompt_fills_the_placeholders_and_adds_the_appendix(self):
        prompt = td.labeler_prompt(NEW_WORK, "dtm-pilot", "b01", "opus")
        self.assertEqual(fu.labeler_prompt(NEW_WORK, "dtm-pilot", "b01", "opus"), prompt)
        for placeholder in ("<W>", "{ROUND}", "{BATCH}", "{MODEL}"):
            self.assertNotIn(placeholder, prompt)
        self.assertIn(NEW_WORK + "\\rounds\\dtm-pilot\\batches\\b01.tsv", prompt)
        self.assertTrue(prompt.endswith(td.LABELER_APPENDIX))
        self.assertIn("never read or use any sentence supplied by a consumer", prompt)

    def test_the_prompt_command_prints_the_run_prompt(self):
        m = Mini()
        m.setUp()
        self.addCleanup(m.doCleanups)
        code, out, err = m.run_tool("prompt", "--round", "dvh", "--batch", "b02", "--model", "sol")
        self.assertEqual(0, code, err)
        self.assertEqual(td.labeler_prompt(os.path.abspath(m.root), "dvh", "b02", "sol"), out)
        code, out, _ = m.run_tool("prompt", "--round", "dvh", "--batch", "b02", "--model", "sol", "--no-appendix")
        self.assertNotIn("PAUSED", out)


class RegisteredConstantsTests(unittest.TestCase):
    def test_the_candidate_budgets_the_lean_training_volume_and_the_topups(self):
        self.assertEqual({"pred": None, "num": None, "ing": 450, "other": 200, "verb": 160}, td.CANDIDATE_BUDGET["deverbal-heads"])
        self.assertEqual({"noun": 520, "adj": 260, "proper": 110}, td.CANDIDATE_BUDGET["defined-terms"])
        self.assertEqual({"imperative": 480, "noun": 110}, td.CANDIDATE_BUDGET["step-lines"])
        self.assertEqual({"main": 600, "sub": 200, "noun": 110, "adj": 60}, td.CANDIDATE_BUDGET["clause-verbs"])
        self.assertEqual({"deverbal-heads": {"verb": 30}, "defined-terms": {"proper": 30}, "step-lines": {"noun": 30}, "clause-verbs": {"noun": 40, "adj": 20}}, td.CONTROL_TOPUP)
        totals = {p: sum(td.CANDIDATE_BUDGET[p].values()) for p in td.TRAIN_PRODUCTS}
        self.assertEqual({"deverbal": 1200, "terms": 2000, "steps": 1500, "clauses": 1000}, totals)             # decision O5: S1 and S4 halved (2,400 and 2,000 in the plan)
        self.assertEqual({"deverbal": 800, "terms": 900, "steps": 1000, "clauses": 800}, {p: td.TRAIN_QUOTAS[p][0][3] for p in td.TRAIN_PRODUCTS})   # S1 and S4 build cap 800
        self.assertEqual({"deverbal": (0, 15), "terms": (15, 25), "steps": (0, 30), "clauses": (0, 20)}, td.TRAIN_SHARES)

    def test_the_caps_the_floors_and_the_partition_constants(self):
        self.assertEqual({"deverbal-heads": 4, "defined-terms": 4, "step-lines": 6, "clause-verbs": 4, "deverbal": 4, "terms": 4, "steps": 6, "clauses": 4}, td.LEMMA_CAP)
        self.assertEqual((2, {"dev": 15, "train": 20}, 30, 20), (td.CONTROL_LEMMA_CAP, td.DOCUMENT_CAP, td.FAMILY_CAP, td.TIER4_SHARE_PERCENT))
        self.assertEqual((30, 50), (td.BARE_MIN_PERCENT, td.LABELED_MIN_PERCENT))
        self.assertEqual((20261110, 0.40, ("P", "E", "M", "G")), (td.SEED, td.DEV_SHARE, td.STREAMS))
        self.assertEqual(sd.SEED, td.SEED)
        self.assertEqual(("dtm-pilot", "dvh", "dtm", "stp", "clv"), td.ROUND_ORDER["dev"][:5])
        self.assertEqual(("dtm-pilot", "dvt", "dtt", "stt", "clt"), td.ROUND_ORDER["train"])
        self.assertEqual((("pred", ("pred",), 100, None), ("num", ("num",), 50, None), ("ing", ("ing",), 150, None)), td.TRAIN_QUOTAS["deverbal"][1:])

    def test_the_four_pins_are_the_frozen_values_of_r1_and_a_changed_input_is_refused(self):
        frozen = {"DRAFT_SHA256": "c03a39752703408ee6daeb6d685fc97a3d1efe2504462f7f4ceb25f3f0af5792",
                  "TOKENS_SHA256": "aebca046f9da537c147be78020f74d0664ec53c78156293cc54170fab251f87e",
                  "HMM_SHA256": "71fa239d4f15b7b367a83203e77d7ff501055f85da7aca46b5d8ac41da8b7600",
                  "CONVENTIONS_SHA256": "42da154c60272dc9a105c5496fa3c8797cffa81cfb55d34505497b33520e5bb2"}
        for name, value in frozen.items():
            self.assertEqual(value, getattr(td, name), name)                                  # R1 froze the inputs of the real work (RUN-LOG, "frozen inputs (R1)")
            self.assertRegex(getattr(td, name), r"^[0-9a-f]{64}$")
        self.assertEqual(td.CONVENTIONS_SHA256, common.sha256_file(os.path.join(common.DATA, "CONVENTIONS.md")))     # the labelers' convention is the pinned file
        self.assertEqual(0.40, td.DEV_SHARE)



# ================================================================ no model, and the rule in the docstring

class NoModelTests(unittest.TestCase):
    """The tool decides nothing with a PartOfSpeech model: it imports no tagger, weights or decoder. The only non-frame input is the Basic mass of the WordSense library (hmm.tsv)."""

    def parsed(self):
        with open(td.__file__, encoding="utf-8") as handle:
            return ast.parse(handle.read())

    def test_only_the_data_tools_are_imported(self):
        imported = set()
        for node in ast.walk(self.parsed()):
            if isinstance(node, ast.Import):
                imported |= {a.name.split(".")[0] for a in node.names}
            elif isinstance(node, ast.ImportFrom):
                imported.add((node.module or "").split(".")[0])
        allowed = {"argparse", "collections", "math", "os", "random", "re", "sys", "adjudicate", "common", "compound_heading_data", "followup_data", "imperative_data", "shapes_dev",
                   "shapes_dev_extra", "technical_frames"}
        self.assertEqual(set(), imported - allowed)

    def test_no_string_of_the_code_names_a_model_file_or_runner_of_this_project(self):
        tree = self.parsed()
        docstring = ast.get_docstring(tree, clean=False)
        strings = [node.value for node in ast.walk(tree) if isinstance(node, ast.Constant) and isinstance(node.value, str) and node.value != docstring]
        joined = "\n".join(strings).lower()
        for forbidden in ("weights.bin", "dotnet", "partofspeech.trainer", "partofspeech.dll", "eval-arm", ".dll", "probe.tsv", "decoder", "eval-shapes", "shapes-tokenize"):
            self.assertNotIn(forbidden, joined, forbidden)

    def test_the_held_out_sets_are_seen_only_through_the_builder_of_shapes_dev(self):
        with open(td.__file__, encoding="utf-8") as handle:
            source = handle.read()
        self.assertNotIn("import leakage", source)
        self.assertNotIn("probe.tsv", source)
        self.assertNotIn("gold-v2", source)
        self.assertNotIn("held_out_overlap_keys", source)

    def test_the_filter_leaves_out_exactly_the_technical_tranches_that_leakage_names(self):
        self.assertEqual(leakage.SHAPES_DEV_TECHNICAL_TRANCHES, td.TECHNICAL_TRANCHES)
        self.assertEqual(set(td.TECHNICAL_TRANCHES), {td.OUTPUT[p][1][:-4] for p in td.DEV_PRODUCTS})
        self.assertEqual(set(), set(td.TECHNICAL_TRANCHES) & set(fu.FOLLOWUP_TRANCHES))                          # the two PR #20 tranches stay in the held-out keys
        self.assertEqual(leakage.TECHNICAL_TRAINING, td.TRAIN_PRODUCTS)

    def test_no_name_of_the_code_scores_or_predicts(self):
        tree = self.parsed()
        names = {n.id for n in ast.walk(tree) if isinstance(n, ast.Name)} | {n.attr for n in ast.walk(tree) if isinstance(n, ast.Attribute)}
        for forbidden in ("tagger", "predict", "decode", "score", "weights", "classifier"):
            self.assertEqual([], [n for n in names if forbidden in n.lower()], forbidden)

    def test_the_docstring_states_the_pre_registered_rule_and_the_constants_match_it(self):
        doc = " ".join(td.__doc__.split())
        for part in ("THE PRE-REGISTERED RULE", "technical_frames.py", "No FAA material", "no consumer text", "distribution statement other than public release", "family key",
                     "DEV share 0.40", "0.10 <= q < 0.97", "ing 450, other 200", "verb controls 160", "noun 520, adj 260, proper 110", "imperative 480, noun 110", "main 600, sub 200",
                     "S1 1,200 (pred 300, num 250, ing 350, other 150, verb 150)", "S2 2,000", "S3 1,500", "S4 1,000", "decision O5", "decision O2", "confirmed / n of at least 0.40",
                     "at least 60", "noun 70, adj 20, proper 10", "claude-opus-5.5", "gpt-6.1-sol", "claude-sonnet-5.5", "20261201 to 20261208", "a model review, not a human review",
                     "3 incorrect of 60 passes, 4 fail", "targets 240 to 360", "sub at least 60", "imperative 180 to 300", "noun 120 to 200", "450 to 800", "500 to 900", "600 to 1,000",
                     "at least 30 percent of the imperatives with a bare object", "at least 50 percent from enumerated or bulleted lines", "ALL-CAPS", "shapes-dev-deverbal-heads-NNNN",
                     "excluded-documents.tsv", "the tool never calls a tagger", "refuse any input whose SHA-256 is not the pinned one", "a round is refused while a later round already has candidates",
                     "1. Documents.", "10. Format and amendments"):
            self.assertIn(part, doc, part)
        for command in ("exclusions", "partition", "lines", "steps", "tokenize", "preflight", "pilot", "pilot-result", "candidates", "build", "sample", "prompt"):
            self.assertIn("technical_data.py --work W " + command, doc, command)
        self.assertIn("review-result --product P --verdicts FILE ... ; report --product P", doc)
        for product in td.PRODUCTS:
            self.assertIn(product, td.__doc__, product)


# ================================================================ additive edits of the shared modules: the old outputs stay byte for byte

def old_decide_report(items, incomplete):
    """`shapes_dev.decide_report` as it was before beta.3 (a frozen copy): rows only for the five old shapes."""
    lines = ["# Decisions", "", "| shape | role | items | kept | target-disagreement | class-mismatch | frame-mismatch | excluded-source | incomplete sentences |",
             "| --- | --- | --- | --- | --- | --- | --- | --- | --- |"]
    for shape in sd.SHAPES:
        for role in sd.ROLES:
            members = [i for i in items if i["shape"] == shape and i["role"] == role]
            count = collections.Counter(i["reason"] or "kept" for i in members)
            lines.append("| %s | %s | %d | %d | %d | %d | %d | %d | |" % (shape, role, len(members), count["kept"], count["target-disagreement"],
                                                                         count["class-mismatch"], count["frame-mismatch"],
                                                                         count[sd.EXCLUDED_REASON]))
    lines += ["", "incomplete sentences (not decided): %d" % len(incomplete),
              "disputed non-target tokens in kept items: %d" % sum(i["disputed_tokens"] for i in items if i["outcome"] == "keep")]
    return lines


def votes_of(*tags):
    return {i: adjudicate.Vote(tag, "high", ()) for i, tag in enumerate(tags)}


class AdditiveEditsTests(Mini):
    def old_items(self):
        items = []
        for number, shape in enumerate(sd.SHAPES + ("n-compound-head-ambiguous", "heading-noun", "heading-imperative", "heading-proper", "n-named-state")):
            for role in sd.ROLES:
                for outcome, reason in (("keep", ""), ("drop", "target-disagreement"), ("drop", "class-mismatch"), ("drop", "frame-mismatch"), ("drop", sd.EXCLUDED_REASON)):
                    items.append(dict(shape=shape, role=role, outcome=outcome, reason=reason, disputed_tokens=number))
        return items

    def test_the_decide_report_of_the_old_shapes_is_byte_identical_and_the_new_rows_appear_only_for_present_shapes(self):
        items = self.old_items()
        self.assertEqual(old_decide_report(items, ["a", "b"]), sd.decide_report(items, ["a", "b"]))
        self.assertEqual(old_decide_report([], []), sd.decide_report([], []))
        new = [dict(shape="term-adj", role="target", outcome="keep", reason="", disputed_tokens=0), dict(shape="term-adj", role="target", outcome="drop", reason="frame-mismatch", disputed_tokens=0),
               dict(shape="n-deverbal-head", role="control", outcome="drop", reason="class-mismatch", disputed_tokens=0)]
        report = sd.decide_report(items + new, [])
        old = old_decide_report(items + new, [])
        self.assertEqual(old[:len(old) - 3], report[:len(old) - 3])                                              # the old rows come first and unchanged
        added = report[len(old) - 3:len(report) - 3]
        self.assertEqual(["| n-deverbal-head | control | 1 | 0 | 0 | 1 | 0 | 0 | |", "| term-adj | target | 2 | 1 | 0 | 0 | 1 | 0 | |"], added)
        self.assertEqual(old[-3:], report[-3:])                                                                  # the closing lines are the old ones

    def test_the_old_shapes_keep_their_constants(self):
        self.assertEqual(("n-compound-head", "n-subject-initial", "v-imperative-initial", "n-measure", "v-after-modal"), sd.SHAPES)
        self.assertEqual({"heading-noun": "NOUN", "heading-imperative": "VERB", "heading-proper": "PROPN"}, {k: v for k, v in sd.EXACT_TAGS.items() if k.startswith("heading")})
        self.assertEqual("Noun", sd.SHAPE_CLASS["n-compound-head-ambiguous"])
        self.assertEqual({"n-compound-head": "Noun", "n-subject-initial": "Noun", "n-measure": "Noun", "v-imperative-initial": "Verb", "v-after-modal": "Verb"},
                         {s: sd.SHAPE_CLASS[s] for s in sd.SHAPES})
        self.assertEqual(leakage.SHAPES_DEV_NEW_TRANCHES[:5], ("compound-head", "headings", "named-state", "compound-head-2", "headings-2"))
        self.assertEqual(sd.TECHNICAL_SHAPES, tuple(sd.TECHNICAL_SHAPE_CLASS))

    def test_the_frame_rules_of_the_new_shapes_use_only_the_tags_that_both_labelers_gave(self):
        def mismatch(shape, index, tags, form=None, role="target"):
            votes = votes_of(*tags)
            return sd.frame_mismatch(dict(shape=shape, role=role, target=index), votes, votes, form, LEX)
        self.assertFalse(mismatch("n-deverbal-head", 2, ("DET", "NOUN", "NOUN", "AUX")))
        self.assertFalse(mismatch("n-deverbal-head", 2, ("DET", "PROPN", "NOUN", "AUX")))
        self.assertTrue(mismatch("n-deverbal-head", 2, ("DET", "ADJ", "NOUN", "AUX")))
        self.assertFalse(mismatch("n-deverbal-head", 2, ("DET", "ADJ", "NOUN", "AUX"), role="control"))        # a control has no frame
        self.assertTrue(mismatch("term-noun", 0, ("NOUN", "NOUN")))
        self.assertFalse(mismatch("term-noun", 2, ("VERB", "DET", "NOUN")))
        self.assertFalse(mismatch("term-adj", 1, ("VERB", "ADJ", "NOUN", "NOUN")))
        self.assertFalse(mismatch("term-adj", 1, ("VERB", "ADJ", "ADJ", "NOUN")))
        self.assertTrue(mismatch("term-adj", 1, ("VERB", "ADJ", "ADP", "NOUN")))
        self.assertTrue(sd.frame_mismatch(dict(shape="term-adj", role="target", target=1), {0: adjudicate.Vote("VERB", "high", ()), 1: adjudicate.Vote("ADJ", "high", ()),
                                                                                              2: adjudicate.Vote("NOUN", "high", ())},
                                           {0: adjudicate.Vote("VERB", "high", ()), 1: adjudicate.Vote("ADJ", "high", ()), 2: adjudicate.Vote("PROPN", "high", ())}, None, None))   # a neighbor they disagree on never satisfies it
        self.assertFalse(mismatch("step-imperative", 0, ("VERB", "NOUN", "NOUN"), "Check"))
        self.assertTrue(mismatch("step-imperative", 1, ("NOUN", "VERB", "NOUN"), "Check"))
        self.assertTrue(mismatch("step-imperative", 0, ("VERB", "NOUN", "NOUN"), "Checks"))                      # a base form, not `Checks`
        self.assertFalse(mismatch("clause-verb", 2, ("DET", "NOUN", "VERB", "DET"), "drain"))
        self.assertFalse(mismatch("clause-verb", 2, ("DET", "PRON", "VERB", "DET"), "drain"))
        self.assertTrue(mismatch("clause-verb", 2, ("DET", "DET", "VERB", "DET"), "drain"))
        self.assertTrue(mismatch("clause-verb", 2, ("DET", "NOUN", "VERB", "DET"), "drains"))

    def test_the_decision_of_the_new_shapes_is_the_exact_tag_or_the_class(self):
        def decide(shape, role, opus, sol=None):
            return sd.decide_item(dict(shape=shape, role=role), adjudicate.Vote(opus, "high", ()), adjudicate.Vote(sol or opus, "high", ()))
        self.assertEqual(("keep", "", "ADJ"), decide("term-adj", "target", "ADJ"))
        self.assertEqual(("drop", "class-mismatch", "NOUN"), decide("term-adj", "target", "NOUN"))
        self.assertEqual(("drop", "target-disagreement", ""), decide("term-noun", "target", "NOUN", "PROPN"))
        self.assertEqual(("keep", "", "PROPN"), decide("term-proper", "control", "PROPN"))
        self.assertEqual(("keep", "", "VERB"), decide("step-imperative", "target", "VERB"))
        self.assertEqual(("keep", "", "NOUN"), decide("step-noun", "control", "NOUN"))
        self.assertEqual(("keep", "", "VERB"), decide("clause-verb", "target", "VERB"))
        self.assertEqual(("keep", "", "ADJ"), decide("clause-adj", "control", "ADJ"))
        self.assertEqual(("keep", "", "NOUN"), decide("n-deverbal-head", "target", "NOUN"))                       # S1 is scored by class
        self.assertEqual(("keep", "", "PROPN"), decide("n-deverbal-head", "target", "PROPN"))
        self.assertEqual(("drop", "class-mismatch", "VERB"), decide("n-deverbal-head", "target", "VERB"))
        self.assertEqual(("keep", "", "VERB"), decide("n-deverbal-head", "control", "VERB"))

    def test_scan_units_keeps_its_old_behavior_by_default_and_takes_the_three_keyword_arguments(self):
        self.sound_draft({"army-a": ct.FRAME}, {"army-a": ["Check The Valve", "Pump Seal Inspection"]})
        with mock.patch.object(sx, "heading_noise_reason", return_value=None):
            default = sx.scan_units(self.work, self.data, "heading", (), False)
            explicit = sx.scan_units(self.work, self.data, "heading", (), False, lines_folder="lines", noise=None, token_range=None)
        self.assertEqual((default.units, default.drops, default.rejected, default.considered), (explicit.units, explicit.drops, explicit.rejected, explicit.considered))
        self.assertEqual(2, len(default.units))
        common.write_text(os.path.join(self.root, "other", "army-a.txt"), ["Check The Valve", "Pump Seal Inspection", "Remove Cover"])
        common.write_text(os.path.join(self.root, "tokens.tsv"), common.read_lines(os.path.join(self.root, "tokens.tsv")) + ["Remove Cover\t1\tRemove Cover"])
        scan = sx.scan_units(self.work, self.data, "unit", (), False, lines_folder="other", noise=lambda text, tokens, lex, vocab: "no" if "Seal" in text else None, token_range=(2, 3))
        self.assertEqual(["Check The Valve", "Remove Cover"], [u[2] for u in scan.units])
        self.assertEqual(1, scan.drops["noise-no"])

    def test_check_licenses_requires_nothing_of_the_technical_text_files_until_they_are_required(self):
        findings = check_licenses.required_file_findings(self.data)
        self.assertFalse([f for f in findings if "technical" in f or any(name in f for name in leakage.SHAPES_DEV_TECHNICAL_TRANCHES)])
        self.assertEqual((), check_licenses.readme_folders(self.data))
        with mock.patch.object(leakage, "TECHNICAL_REQUIRED", True):
            required = check_licenses.required_file_findings(self.data)
            self.assertEqual(16, len([f for f in required if f not in findings]))                               # eight tables and eight corpora
            self.assertEqual(("technical",), check_licenses.readme_folders(self.data))
        os.makedirs(os.path.join(self.data, "technical"))
        self.assertEqual(("technical",), check_licenses.readme_folders(self.data))                               # the README of an existing folder is checked
        self.assertEqual(8, len(check_licenses.TECHNICAL_TABLES))
        self.assertTrue(set(check_licenses.TECHNICAL_TABLES) <= set(check_licenses.SOURCE_TABLES))
        self.assertTrue(set(check_licenses.TECHNICAL_TABLES) <= set(check_licenses.FAA_TABLES))                  # the FAA rule covers the new tables
        self.assertFalse(set(check_licenses.TECHNICAL_TABLES) & set(check_licenses.REQUIRED_FILES))
        self.assertEqual(leakage.SHAPES_DEV_FIRST_TRANCHES + leakage.SHAPES_DEV_FOLLOWUP_TRANCHES, check_licenses.NEW_TRANCHES)

    def test_check_leakage_tolerates_absent_technical_sources_and_gates_present_ones(self):
        sets = check_leakage.training_sets()
        self.assertFalse(set(leakage.TECHNICAL_TRAINING) & set(sets))
        with mock.patch.object(leakage, "TECHNICAL_REQUIRED", True):
            with self.assertRaises(SystemExit) as raised:
                check_leakage.training_sets()
            self.assertIn("deverbal", str(raised.exception))
        base.corpus_file(os.path.join(self.data, "technical", "terms.tsv"), [("army-t", "Select the Manual Override mode before the long trip today .")])
        self.assertIn("terms", check_leakage.training_sets())
        trains = {"open-train": [("open-1", "Open the door before the long flight starts today")], "masc": [("masc-1", "Masc sentence one is here")],
                  "silver": [("silver-1", "Silver sentence one is here")], "imperative": [("imp-1", "Inspect the seal before each flight")],
                  "compound": [("army-a", "Fuel tank seal check")], "headings": [("army-b", "Pump Seal Inspection")], "compound2": [("navy-a", "Hydraulic line pressure test")],
                  "headings2": [("navy-b", "Drain The Hydraulic Line")], "deverbal": [("tech-a", "The oil check is full today")], "terms": [("tech-a", "Press the Pump Switch button today")]}
        rows, failures = check_leakage.training_check(trains)
        self.assertEqual(0, failures)                                                                            # the four technical sources share one pool: a shared document is reported
        self.assertEqual(1, [r for r in rows if r[:2] == ("deverbal", "terms")][0][4])
        _, failures = check_leakage.training_check(dict(trains, terms=[("tech-a", "The oil check is full today")]))
        self.assertEqual(1, failures)                                                                            # a shared sentence is always gated
        _, failures = check_leakage.training_check(dict(trains, terms=[("masc-1", "Another technical sentence here")]))
        self.assertEqual(1, failures)                                                                            # a document shared with another training set is gated
        rows, _ = check_leakage.training_check({k: v for k, v in trains.items() if k not in leakage.TECHNICAL_TRAINING})
        self.assertEqual({"compound", "headings", "compound2", "headings2"}, {r[0] for r in rows})               # without the new sources the old rows only

    def test_the_technical_dev_tranches_are_a_third_generation_in_the_dev_check(self):
        def tranche(name, items):
            extra = [["# shape = n-measure", "# role = target", "# target = 0", "# lemma = x", "# origin = real"] for _ in items]
            base.corpus_file(os.path.join(self.data, "eval", "shapes-dev", name + ".tsv"), items, extra)
        tranche("dev", [("doc-std", "A brand new standard sentence about nothing in particular .")])
        tranche("hard", [("doc-hard", "Another brand new hard sentence about nothing at all .")])
        tranche("deverbal-heads", [("army-x", "The first technical sentence is about the hydraulic pump housing .")])
        tranche("clause-verbs", [("army-x", "A second technical sentence is about the pressure relief valve .")])
        tranche("headings-2", [("army-y", "A follow-up sentence about the right side of the vehicle .")])
        rows, failures, failing = check_leakage.dev_check(include_ud=False, tranche="clause-verbs")
        self.assertEqual((0, []), (failures, failing))                                                           # the technical tranches may share a document (reported)
        self.assertEqual(1, {r[0]: r for r in rows}["shapes-dev-deverbal-heads"][2])
        tranche("headings-2", [("army-x", "A follow-up sentence about the right side of the vehicle .")])
        rows, failures, failing = check_leakage.dev_check(include_ud=False, tranche="clause-verbs")
        self.assertEqual((1, ["s1"]), (failures, failing))                                                        # a document shared with the PR #20 generation is a leak
        tranche("deverbal-heads", [("army-z", "A follow-up sentence about the right side of the vehicle .")])
        self.assertEqual(["s1"], check_leakage.dev_check(include_ud=False, tranche="deverbal-heads")[2])           # a shared sentence always fails


if __name__ == "__main__":
    unittest.main()
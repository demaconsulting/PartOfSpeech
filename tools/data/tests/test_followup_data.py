"""Tests of followup_data.py (the builder of the follow-up data of PR #19: two development tranches, two training sources, the pilot and its gate) on a miniature data and work folder.

Run: python -m unittest discover -s tools/data/tests -p "test_followup_data.py"   (developer-run; the pipeline needs no model or network)
"""
import ast
import collections
import contextlib
import hashlib
import io
import os
import re
import sys
import unittest
from unittest import mock

sys.path.insert(0, os.path.dirname(os.path.dirname(os.path.abspath(__file__))))
import adjudicate
import common
import compound_heading_data as chd
import followup_data as fd
import imperative_data as idata
import shapes_dev as sd
import shapes_dev_extra as sx
import test_compound_heading_data as ct
import test_shapes_dev as base

LEXICON = dict(ct.LEXICON, remove="VERB", install="VERB", clean="VERB ADJ", mass="NOUN VERB", characteristics="NOUN", report="NOUN VERB",
               cover="NOUN VERB", checking="NOUN VERB")
OLD_WORK = r"C:\Users\mnixon\.copilot\session-state\217fb8fb-495d-4da7-89a1-b33f6a7805ad\files\compound-heading-data\work"
NEW_WORK = r"C:\Users\mnixon\.copilot\session-state\217fb8fb-495d-4da7-89a1-b33f6a7805ad\files\compound-heading-followup\work"


def drops_of(report):
    return ct.drops_of(report)


class FakeHmm:
    """The Basic mass of a fixture: q by sentence text (the real table is read from the pinned hmm.tsv)."""

    def __init__(self, masses):
        self.masses = masses

    def q(self, tokens, index, word_class):
        return self.masses[" ".join(tokens)]


class Mini(ct.Mini):
    """The miniature folders of test_compound_heading_data plus the old product tables, the old compound-head targets and helpers for the follow-up rounds."""

    def write_data(self):
        super().write_data()
        d = self.data
        common.write_text(os.path.join(d, "lexicon", "lexicon.tsv"), ["# fixture lexicon"] + ["%s\t%s" % (k, v) for k, v in sorted(LEXICON.items())])
        base.table_file(os.path.join(d, "compound", "sources.tsv"), sd.SOURCE_COLUMNS, [self.source_row("army-old-cmp", "https://archive.org/download/OldCmp/OldCmp_djvu.txt")])
        common.write_text(os.path.join(d, "compound", "excluded-documents.tsv"), ["# columns = document_id, reason", "army-old-cmp-out\tstate co-author"])
        base.table_file(os.path.join(d, "headings", "sources.tsv"), sd.SOURCE_COLUMNS, [self.source_row("army-old-hdg", "https://archive.org/download/OldHdg/OldHdg_djvu.txt")])
        common.write_text(os.path.join(d, "headings", "excluded-documents.tsv"), ["# columns = document_id, reason", "army-old-hdg-out\tnon-federal author"])
        items = [("army-ch-doc", "Fuel filter check was done twice before the long flight started ."),
                 ("army-ch-doc", "Oil filter reporting is done twice before the long flight started ."),
                 ("army-ch-doc", "Tank pump seal is done twice before the long flight started .")]
        extra = [["# shape = n-compound-head-ambiguous", "# role = target", "# target = 2", "# lemma = check", "# origin = real"],
                 ["# shape = n-compound-head-ambiguous", "# role = target", "# target = 2", "# lemma = report", "# origin = real"],
                 ["# shape = n-compound-head-ambiguous", "# role = control", "# target = 2", "# lemma = seal", "# origin = real"]]
        base.corpus_file(os.path.join(d, "eval", "shapes-dev", "compound-head.tsv"), items, extra)
        self.old_work_names = []

    # ---- the work folder

    def sides(self, **sides):
        """Write W/partition.tsv: {source_id: side}; every document is its own family."""
        rows = [(source_id.replace("_", "-"), "C", "family " + source_id, side) for source_id, side in sorted((k.replace("_", "-"), v) for k, v in sides.items())]
        adjudicate.write_rows(os.path.join(self.root, fd.PARTITION_FILE), ("source_id", "stream", "family_key", "side"), rows)

    def pins(self, hmm=False):
        """Patches of the pins to the hashes of the files of the work folder (R1 of the real work)."""
        if hmm:
            common.write_text(os.path.join(self.root, "hmm.tsv"), ["# fixture"])
        patches = [mock.patch.object(fd, "DRAFT_SHA256", common.sha256_file(os.path.join(self.root, "sources-draft.tsv"))),
                   mock.patch.object(fd, "TOKENS_SHA256", common.sha256_file(os.path.join(self.root, "tokens.tsv"))),
                   mock.patch.object(fd, "HMM_SHA256", common.sha256_file(os.path.join(self.root, "hmm.tsv")) if hmm else None)]
        for patch in patches:
            patch.start()
            self.addCleanup(patch.stop)

    def run_tool(self, *args):
        out, err = io.StringIO(), io.StringIO()
        with contextlib.redirect_stdout(out), contextlib.redirect_stderr(err):
            with mock.patch.object(sd, "noise_reason", return_value=None), mock.patch.object(sx, "heading_noise_reason", return_value=None):
                code = fd.main(["--work", self.root, "--data-dir", self.data] + list(args))
        return code, out.getvalue(), err.getvalue()

    def read_candidates(self, round_name):
        _, records = adjudicate.read_records(os.path.join(self.root, "rounds", round_name, "candidates.tsv"))
        return records

    def round(self, round_name, specs, with_q=False):
        """Write candidates and decided items/rows of a round from specs: dicts with cand_id, text, lemma, source, target, shape, role and optional tags, statuses,
        outcome, reason, agreed, opus, sol, q, disputed."""
        self.write_draft(sorted({s["source"] for s in specs} | set(getattr(self, "extra_documents", ()))))
        rw = sd.RoundWork(self.root, round_name)
        os.makedirs(rw.private, exist_ok=True)
        rows, items, token_rows = [], [], []
        for spec in specs:
            tokens = base.simple_tokens(spec["text"])
            target, shape, role = spec["target"], spec.get("shape", chd.COMPOUND_SHAPE), spec.get("role", "target")
            row = dict(cand_id=spec["cand_id"], shape=shape, role=role, target=target, lemma=spec["lemma"], origin="real", source_id=spec["source"], line=1,
                       text=spec["text"], tokens=tokens)
            if with_q:
                row["q"] = spec.get("q", "")
            rows.append(row)
            key = sd.sent_key_of(spec["text"])
            tags = spec.get("tags") or ct.tags_of(tokens, target, spec.get("head_tag", "NOUN"))
            statuses = spec.get("statuses") or ["unanimous"] * len(tokens)
            outcome = spec.get("outcome", "keep")
            agreed = spec.get("agreed", tags[target] if outcome == "keep" else "")
            items.append([spec["cand_id"], key, shape, role, str(target), spec["lemma"], "real", spec["source"], outcome, spec.get("reason", ""),
                          spec.get("opus", tags[target]), spec.get("sol", tags[target]), agreed,
                          str(spec.get("disputed", sum(1 for s in statuses if s == "disputed")))])
            token_rows += [(key, i, tokens[i], tags[i], statuses[i]) for i in range(len(tokens))]
        sd.write_candidates(rw, rows)
        adjudicate.write_rows(rw.private_file("decided.tsv"), sd.ITEM_COLUMNS, items)
        adjudicate.write_rows(rw.private_file("decided-rows.tsv"), sd.ROW_COLUMNS, token_rows)

    def build(self, product, round_specs, out=None, **options):
        """Write the rounds {round name: (specs, with_q)} and build the product; returns (built, report, out folder)."""
        self.extra_documents = {s["source"] for specs, _ in round_specs.values() for s in specs}      # one draft for every round
        for name, (specs, with_q) in round_specs.items():
            self.round(name, specs, with_q)
        out = out or os.path.join(self.temp, "out")
        with mock.patch.object(fd, "draft_gate"):
            built, report = fd.build(self.work, product, out, data_dir=self.data, **options)
        return built, report, out


def cmp_specs(lemma, count, source="army-a", start=1, first=1, **extra):
    """`count` compound-2 specs of one head lemma (`Tank oil <lemma> N .`, head at index 2), candidate ids from `first`."""
    return [dict(cand_id="cm2-c%05d" % (first + n), text="Tank oil %s %d ." % (lemma, start + n), lemma=lemma, source=source, target=2, **extra) for n in range(count)]


def imp_specs(lemma, word, count, source="army-a", start=1, first=1, prefix="h2i-", **extra):
    """`count` imperative heading specs `<Word> Valve N` (token 0 VERB)."""
    out = []
    for n in range(count):
        text = "%s Valve %s" % (word, NAMES[(start + n - 1) % len(NAMES)])
        tokens = base.simple_tokens(text)
        out.append(dict(cand_id="%sc%05d" % (prefix, first + n), text=text, lemma=lemma, source=source, target=0, shape=sd_imperative(),
                        tags=["VERB"] + ["NOUN"] * (len(tokens) - 1), **extra))
    return out


def sd_imperative():
    return fd.IMPERATIVE_SHAPE


NAMES = ["Alpha", "Bravo", "Charlie", "Delta", "Echo", "Foxtrot", "Golf", "Hotel", "India", "Juliet", "Kilo", "Lima", "Mike", "Oscar", "Papa", "Quebec", "Romeo", "Sierra", "Tango", "Uniform"]


def read_bytes_of(folder):
    """{name: bytes} of the files of a folder."""
    found = {}
    for name in os.listdir(folder):
        with open(os.path.join(folder, name), "rb") as handle:
            found[name] = handle.read()
    return found


def noun_start_specs(lemma, word, count, source="army-a", start=1, first=1):
    """Initial-noun specs: both labelers agree on NOUN at token 0 (the imperative-frame item drops as a class mismatch)."""
    out = []
    for n in range(count):
        text = "%s Valve %d" % (word, start + n)
        out.append(dict(cand_id="h2n-c%05d" % (first + n), text=text, lemma=lemma, source=source, target=0, shape=fd.IMPERATIVE_SHAPE, outcome="drop",
                        reason="class-mismatch", agreed="NOUN", tags=["NOUN"] * len(base.simple_tokens(text)), opus="NOUN", sol="NOUN"))
    return out


def dev_heading_specs(kind, lemma, word, count, source="army-a", start=1, first=1, target=None):
    shape = {"imperative": fd.IMPERATIVE_SHAPE, "noun": fd.NOUN_SHAPE, "proper": fd.PROPER_SHAPE}[kind]
    tag = {"imperative": "VERB", "noun": "NOUN", "proper": "PROPN"}[kind]
    target = (0 if kind == "imperative" else 1) if target is None else target
    out = []
    for n in range(count):
        name = NAMES[(start + n - 1) % len(NAMES)]
        text = "%s Valve %s" % (word, name) if kind == "imperative" else "Pump %s %s" % (word, name)
        tokens = base.simple_tokens(text)
        tags = ["NOUN"] * len(tokens)
        tags[target] = tag
        out.append(dict(cand_id="h2d-c%05d" % (first + n), text=text, lemma=lemma, source=source, target=target, shape=shape,
                        role="control" if kind == "proper" else "target", tags=tags))
    return out


# ================================================================ the used-document index, the reference and the pins

class IndexTests(Mini):
    def test_the_index_holds_all_earlier_tables_but_not_the_outputs_of_the_four_products(self):
        extra = os.path.join(self.temp, "draft.tsv")
        base.table_file(extra, sd.SOURCE_COLUMNS, [self.source_row("army-extra", "https://archive.org/download/ExtraId/ExtraId_djvu.txt")])
        for relative, sid in (("compound/compound-2-sources.tsv", "army-own-cmp"), ("headings/headings-2-sources.tsv", "army-own-hdg"),
                              ("eval/shapes-dev/compound-head-2-sources.tsv", "army-own-chd"), ("eval/shapes-dev/headings-2-sources.tsv", "army-own-hd2")):
            base.table_file(os.path.join(self.data, *relative.split("/")), sd.SOURCE_COLUMNS, [self.source_row(sid, "https://archive.org/download/%s/%s_djvu.txt" % (sid, sid))])
        index = fd.used_index(self.data, sd.load_exclusions(self.work, self.data), [extra])
        for known in ("army-dev-doc", "army-ch-doc", "army-imp-doc", "army-old-cmp", "army-old-cmp-out", "army-old-hdg", "army-old-hdg-out", "gutenberg-1023"):
            self.assertIsNotNone(idata.document_rejection(index, known), known)
        self.assertIsNotNone(idata.document_rejection(index, "x", "https://archive.org/details/oldcmp"))               # the identifier of an old compound source
        self.assertIsNotNone(idata.document_rejection(index, "x", "https://archive.org/details/OldHdg"))
        self.assertIn("extra:draft.tsv", idata.document_rejection(index, "army-extra"))
        for own in ("army-own-cmp", "army-own-hdg", "army-own-chd", "army-own-hd2"):                                  # a rebuild never rejects itself
            self.assertIsNone(idata.document_rejection(index, own), own)
        self.assertIsNone(idata.document_rejection(index, "army-new", "https://archive.org/download/NewOne/NewOne_djvu.txt"))

    def test_the_old_compound_and_heading_tables_are_in_the_index_of_the_old_builder_never(self):
        old = chd.used_index(self.data, sd.load_exclusions(self.work, self.data))
        self.assertIsNone(idata.document_rejection(old, "army-old-cmp"))                 # why the follow-up needs its own index (F8 of the plan)
        self.assertIsNotNone(idata.document_rejection(fd.used_index(self.data, sd.load_exclusions(self.work, self.data)), "army-old-cmp"))

    def test_the_draft_gate_refuses_a_document_of_an_old_source_or_of_an_old_exclusion_record(self):
        for used in ("army-old-cmp", "army-old-hdg-out", "army-old-cmp-out"):
            self.sound_draft({used: ct.FRAME})
            with self.assertRaises(sx.DraftError, msg=used):
                fd.draft_gate(self.work, self.data)
        self.sound_draft({"army-a": ct.FRAME})
        fd.draft_gate(self.work, self.data)
        fd.draft_gate(self.work, self.data, (), ["not-in-the-draft"])             # the gate skips only the omitted documents

    def test_an_old_work_draft_is_an_extra_table_and_excludes_its_documents(self):
        extra = os.path.join(self.temp, "old-draft.tsv")
        base.table_file(extra, sd.SOURCE_COLUMNS, [self.source_row("usda-oldunused", "https://archive.org/download/OldUnused/OldUnused_djvu.txt")])
        self.sound_draft({"usda-oldunused": ct.FRAME})
        with self.assertRaises(sx.DraftError):
            fd.draft_gate(self.work, self.data, [extra])
        code, out, _ = self.run_tool("exclusions", "--extra-table", extra, "--check", "usda-oldunused", "new-doc", "https://archive.org/details/OldUnused")
        self.assertEqual(1, code)
        self.assertIn("REJECT: usda-oldunused", out)
        self.assertIn("ACCEPT: new-doc", out)
        self.assertIn("REJECT: https://archive.org/details/OldUnused", out)

    def test_the_reference_holds_the_old_training_sentences_too(self):
        reference = fd.Reference(self.data)
        self.assertEqual(idata.EQUALS_SHAPES_DEV, reference.reason("Replace the fuel pump seal after the long trip ."))   # compound.tsv
        self.assertEqual(idata.EQUALS_SHAPES_DEV, reference.reason("Fuel Pump Seal"))                                      # headings.tsv and a tranche
        self.assertEqual(idata.EQUALS_SHAPES_DEV, reference.reason("Inspect the fuel strainer before each long trip ."))   # imperative.tsv
        self.assertIsNone(reference.reason("Tank oil check 3 ."))
        self.assertIsNone(chd.Reference(self.data).reason("Replace the fuel pump seal after the long trip ."))              # the old reference did not hold them

    def test_the_pins_must_be_set_and_must_match_before_any_candidate(self):
        self.sound_draft({"army-a": ct.FRAME})
        with self.assertRaises(fd.PinError):
            fd.require_pins(self.work)                                                  # none is set in the committed tool before R1
        self.pins()
        fd.require_pins(self.work)
        with mock.patch.object(fd, "TOKENS_SHA256", "0" * 64):
            with self.assertRaises(fd.PinError):
                fd.require_pins(self.work)
        with self.assertRaises(fd.PinError):
            fd.require_pins(self.work, "compound-head-2")                               # hmm.tsv is pinned for compound-head-2 only and is not set here
        self.pins(hmm=True)
        fd.require_pins(self.work, "compound-head-2")
        code, _, err = self.run_tool("candidates", "--product", "compound-2", "--no-ud")
        self.assertEqual(1, code)
        self.assertIn("refused", err)

    def test_a_candidate_command_refuses_without_pins_and_writes_nothing(self):
        self.sound_draft({"army-a": ct.FRAME})
        self.sides(**{"army-a": "train"})
        before = self.snapshot()
        with mock.patch.object(fd, "DRAFT_SHA256", None), mock.patch.object(fd, "TOKENS_SHA256", None):      # the pins as they were before R1
            code, _, err = self.run_tool("candidates", "--product", "compound-2", "--no-ud")
            self.assertEqual(1, code)
            self.assertIn("pin of sources-draft.tsv is not set", err)
            code, _, err = self.run_tool("pilot", "--no-ud")
            self.assertEqual(1, code)
        code, _, err = self.run_tool("pilot", "--no-ud")                                                      # the R1 pins: this fixture's bytes differ, so the pilot refuses
        self.assertEqual(1, code)
        self.assertIn("the pinned one is " + fd.DRAFT_SHA256, err)
        self.assertEqual(before, self.snapshot())


# ================================================================ the family key and the partition

class PartitionTests(Mini):
    def test_editions_and_scans_of_one_publication_share_a_family_key(self):
        keys = {fd.family_key(t) for t in ("STP 21-1-SMCT Soldier's Manual of Common Tasks, Skill Level 1 (2003)",
                                           "STP 21-1-SMCT Soldier's Manual of Common Tasks Skill Level 1, 1994 edition",
                                           "STP 21-1-SMCT Soldier's Manual of Common Tasks, Skill Level 2",
                                           "STP 21-1-SMCT Soldier's Manual of Common Tasks, Volume 1, Revised 1990")}
        self.assertEqual(1, len(keys), keys)
        self.assertNotEqual(fd.family_key("First Aid for Soldiers"), fd.family_key("Survival Skills for Soldiers"))
        self.assertEqual("", fd.family_key("1990 (2) 21-1"))

    def test_the_side_is_the_seeded_rank_of_the_family_key_against_the_share_of_the_stream(self):
        families = ["family number %d" % n for n in range(4000)]
        for stream, share in (("C", 0.35), ("H", 0.30)):
            dev = sum(1 for f in families if fd.side_of(f, stream) == "dev") / len(families)
            self.assertAlmostEqual(share, dev, delta=0.03, msg=stream)
        self.assertEqual(fd.side_of("a family", "C"), fd.side_of("a family", "C"))
        self.assertEqual("dev", fd.side_of("a family", "C", {"C": 1.0, "H": 1.0}))
        self.assertEqual("train", fd.side_of("a family", "C", {"C": 0.0, "H": 0.0}))
        expected = "dev" if int(sd.rank_of(sd.SEED, "partition", "a family")[:12], 16) / 16 ** 12 < 0.35 else "train"
        self.assertEqual(expected, fd.side_of("a family", "C"))

    def test_a_family_is_on_one_side_whatever_the_order_and_counts_as_stream_h_with_one_h_member(self):
        draft = [dict(source_id="d1", title="Manual of Tasks (1990)"), dict(source_id="d2", title="Manual of Tasks, 2003 edition"), dict(source_id="d3", title="Other Book")]
        first = fd.partition_rows(draft, {"d1": "C", "d2": "H", "d3": "C"})
        again = fd.partition_rows(list(reversed(draft)), {"d1": "C", "d2": "H", "d3": "C"})
        self.assertEqual(first, again)
        sides = {row[0]: row[3] for row in first}
        self.assertEqual(sides["d1"], sides["d2"])
        self.assertEqual(fd.side_of("manual of tasks", "H"), sides["d1"])
        with self.assertRaises(ValueError):
            fd.partition_rows(draft, {"d1": "C"})

    def test_the_partition_command_writes_the_table_and_the_readers_fail_closed(self):
        self.sound_draft({"army-a": ct.FRAME, "army-b": ct.FRAME})
        code, _, err = self.run_tool("partition")
        self.assertEqual(1, code)
        self.assertIn("streams.tsv is missing", err)
        common.write_text(os.path.join(self.root, "streams.tsv"), ["source_id\tstream", "army-a\tC", "army-b\tH"])
        code, out, err = self.run_tool("partition")
        self.assertEqual(0, code, err)
        _, records = adjudicate.read_records(os.path.join(self.root, "partition.tsv"))
        self.assertEqual(["army-a", "army-b"], [r["source_id"] for r in records])
        self.assertEqual({"dev", "train"} & {r["side"] for r in records}, {r["side"] for r in records})
        self.assertEqual({"army-a", "army-b"}, set(fd.read_partition(self.work)))
        adjudicate.write_rows(os.path.join(self.root, "partition.tsv"), ("source_id", "stream", "family_key", "side"),
                              [("army-a", "C", "one family", "dev"), ("army-b", "C", "one family", "train")])
        with self.assertRaises(ValueError) as raised:
            fd.read_partition(self.work)
        self.assertIn("both sides", str(raised.exception))
        adjudicate.write_rows(os.path.join(self.root, "partition.tsv"), ("source_id", "stream", "family_key", "side"), [("army-a", "C", "f", "dev")])
        with self.assertRaises(ValueError) as raised:
            fd.read_partition(self.work)
        self.assertIn("army-b", str(raised.exception))
        common.write_text(os.path.join(self.root, "streams.tsv"), ["source_id\tstream", "army-a\tX", "army-b\tH"])
        with self.assertRaises(ValueError):
            fd.read_streams(self.work)

    def test_a_scan_is_restricted_to_the_documents_of_one_side(self):
        scan = sx.Scan()
        scan.units = [("d1", 1, "A", ["A"]), ("d2", 1, "B", ["B"])]
        scan.rejected = {"d1": "x", "d2": "y"}
        dev = fd.restrict(scan, {"d1": "dev", "d2": "train"}, "dev")
        self.assertEqual(["d1"], [u[0] for u in dev.units])
        self.assertEqual({"d1": "x"}, dev.rejected)

    def test_a_product_only_reads_the_items_of_the_documents_of_its_side(self):
        specs = cmp_specs("check", 3)
        self.sides(**{"army-a": "dev"})
        built, _, _ = self.build("compound-2", {"cmp2": (specs, False)}, allow_short=True)
        self.assertEqual([], built)                                                       # a TRAIN product never sees a document of the DEV side
        self.sides(**{"army-a": "train"})
        built, _, _ = self.build("compound-2", {"cmp2": (specs, False)}, allow_short=True)
        self.assertEqual(3, len(built))

    def test_the_two_sides_stay_disjoint_against_the_committed_tables_of_the_other_side(self):
        self.sides(**{"army-a": "train"})
        base.table_file(os.path.join(self.data, "eval", "shapes-dev", "compound-head-2-sources.tsv"), sd.SOURCE_COLUMNS, [self.source_row("army-a")])
        with self.assertRaises(fd.BuildError) as raised:
            self.build("compound-2", {"cmp2": (cmp_specs("check", 3), False)}, allow_short=True)
        self.assertIn("army-a is in both compound-2 and compound-head-2", str(raised.exception))


# ================================================================ heading, table-of-contents and step-title lines

class LineTests(unittest.TestCase):
    def test_a_table_of_contents_line_loses_its_leaders_and_page_number(self):
        for line, title in (("Unload an M60 Machine Gun ........ 3-67", "Unload an M60 Machine Gun"),
                            ("Unload an M60 Machine Gun . . . . . 12", "Unload an M60 Machine Gun"),
                            ("Maintain a Caliber .50 M2 Machine Gun ccceeee 3-67", "Maintain a Caliber .50 M2 Machine Gun"),
                            ("Prepare an M136 Launcher for Firing eeeee 4 - 9", "Prepare an M136 Launcher for Firing"),
                            ("Decontaminate Yourself and Individual Equipment ..... 5", "Decontaminate Yourself and Individual Equipment"),
                            ("Perform First Aid for Heat InjurieS.......... eee 3-64", "Perform First Aid for Heat InjurieS"),
                            ("Perform Mouth-to-Mouth Resuscitation.......000..... 3-83", "Perform Mouth-to-Mouth Resuscitation"),
                            ("Evaluate a Casualty ............:ccccccccceceeceeeeeeeeseeeeneeeeees 3-52", "Evaluate a Casualty"),
                            ("Load an M16-Series Rifle 00... eeeeeeeeeeeeeeeenees 3-303", "Load an M16-Series Rifle"),
                            ("071-311-2027 Load an M16-Series Rifle ..........ecceeeeeereeeerees 3-311", "071-311-2027 Load an M16-Series Rifle")):
            self.assertEqual(title, fd.toc_title(line), line)
        for line in ("Unload an M60 Machine Gun", "Remove the cover and check the seal 3", "Page 3 of 67", "Use the Ice Pack 12", "...... 5", "Load an M16-Series Rifle ..... Part"):
            self.assertIsNone(fd.toc_title(line), line)

    def test_a_labeled_line_loses_its_label(self):
        for line, title in (("Task 3. Clean The Valve", "Clean The Valve"), ("Step 2: Check the Seal", "Check the Seal"), ("Procedure 4-1 Replace the Filter", "Replace the Filter"),
                            ("Operation B Install the Cover", "Install the Cover"), ("2.1 Remove the Cover", "Remove the Cover"), ("(a) Check the Seal", "Check the Seal"),
                            ("Exercise 5 Drain the Tank", "Drain the Tank"), ("071-311-2027 Load an M16-Series Rifle", "Load an M16-Series Rifle")):
            self.assertEqual(title, fd.labeled_title(line), line)
        for line in ("Clean The Valve", "3 Clean The Valve", "Tasks are listed below", "Step " + "x" * 100, "071-311 Load an M16-Series Rifle"):
            self.assertIsNone(fd.labeled_title(line), line)

    def test_a_toc_line_with_a_skill_task_number_gives_the_bare_title(self):
        found = fd.step_title_lines("081-831-1000 Evaluate a Casualty ......:ccceeee 3-52\n071-311-2027 Load an M16-Series Rifle ... eee 3-303\n", tiers=True)
        self.assertEqual([("Evaluate a Casualty", "T1"), ("Load an M16-Series Rifle", "T1")], found)

    def test_step_titles_come_from_headings_toc_lines_and_labeled_lines_deduplicated(self):
        raw = "\n".join([
            "Contents", "",
            "Task 1. Clean The Valve ..... 3", "Perform First Aid for an Open Abdominal Wound ...... 3-67", "Unload an M60 Machine Gun ccceeee 3-68", "",
            "Clean The Valve", "Remove the cover and check the seal before you start the work today.", "",
            "Step 2: Check the Seal", "Check the Seal", "",
            "Unload an M60 Machine Gun", "Remove the cover and check the seal before you start the work today.", ""])
        found = fd.step_title_lines(raw, tiers=True)
        self.assertEqual(["Clean The Valve", "Unload an M60 Machine Gun", "Perform First Aid for an Open Abdominal Wound", "Check the Seal"], [t for t, _ in found])      # headings first
        tiers = dict(found)
        self.assertEqual({"Clean The Valve": "T1", "Perform First Aid for an Open Abdominal Wound": "T1", "Unload an M60 Machine Gun": "T1", "Check the Seal": "T2"}, tiers)
        self.assertEqual([t for t, _ in found], fd.step_title_lines(raw))

    def test_no_paragraph_is_needed_for_a_toc_or_labeled_line_but_running_headers_are_refused(self):
        raw = "\n".join(["Task 5. Drain the Tank", "Task 5. Drain the Tank", "Task 5. Drain the Tank", "Task 6. Fill the Tank", "Mount the Pump ..... 9"])
        self.assertEqual(["Fill the Tank", "Mount the Pump"], fd.step_title_lines(raw))              # a line that occurs three times is a running header
        self.assertEqual([], fd.step_title_lines(""))
        self.assertEqual([], fd.step_title_lines("Step 2 Fill the Tank 7"))                         # a trailing page number is no heading
        self.assertEqual([], fd.step_title_lines("Step 2 Fill the Tank \t7"))

    def test_the_lines_command_writes_the_lines_of_every_document(self):
        m = Mini()
        m.setUp()
        self.addCleanup(m.doCleanups)
        m.sound_draft({"army-a": ct.FRAME})
        common.write_text(os.path.join(m.root, "raw", "army-a.txt"), ["Contents", "", "Task 1. Clean The Valve ..... 3", "Install the Cover ..... 4"])
        code, out, err = m.run_tool("lines")
        self.assertEqual(0, code, err)
        self.assertEqual(["Clean The Valve", "Install the Cover"], common.read_lines(os.path.join(m.root, "lines", "army-a.txt")))
        self.assertIn("T1 2", out)
        self.assertEqual({sd.sent_key_of("Clean The Valve"): "T1", sd.sent_key_of("Install the Cover"): "T1"}, fd.tier_map(m.work))


# ================================================================ frames

class FrameTests(unittest.TestCase):
    lex = {k: frozenset(v.split()) for k, v in LEXICON.items()}

    def hit(self, text, frame=fd.fimp_hit):
        return frame(base.simple_tokens(text), self.lex)

    def test_the_imperative_frame_is_the_old_strength_0_set_with_verb_only_words(self):
        for text in ("Check The Valve", "Remove The Cover", "Check It", "Seal Up", "Stop All Leaks", "Install Up"):
            self.assertEqual(0, self.hit(text), text)
        for text in ("Remove", "Mass Characteristics", "Install Pump Seal", "Pump Seal Check", "Washington Valve", "Fuel Pump Seal"):
            self.assertIsNone(self.hit(text), text)

    def test_strength_1_lines_are_the_contrast_frame_and_never_imperative_candidates(self):
        self.assertIsNone(self.hit("Mass Characteristics"))
        self.assertEqual(0, self.hit("Mass Characteristics", fd.initial_noun_hit))
        self.assertEqual(0, self.hit("Stop Pump Seal", fd.initial_noun_hit))
        self.assertIsNone(self.hit("Check The Valve", fd.initial_noun_hit))
        self.assertIsNone(self.hit("Install Pump Seal", fd.initial_noun_hit))          # a verb-only word has no noun reading
        self.assertIsNone(self.hit("Mass The Valve", fd.initial_noun_hit))

    def test_first_word_types(self):
        self.assertEqual("verb-only", fd.first_word_type("Remove", self.lex))
        self.assertEqual("noun-verb", fd.first_word_type("Check", self.lex))


# ================================================================ candidates

class CandidateTests(Mini):
    def headings(self, documents, lines, product, *options):
        self.sound_draft(documents, lines)
        self.pins()
        return self.run_tool("candidates", "--product", product, "--no-ud", *options)

    def test_headings_2_train_candidates_are_imperative_and_initial_noun_lines_with_their_prefixes(self):
        lines = ["Check The Valve", "Remove The Cover", "Mass Characteristics", "Stop Pump Seal", "Pump Seal Check", "Install Pump Seal"]
        self.sound_draft({"army-a": ct.FRAME}, {"army-a": lines})
        self.sides(**{"army-a": "train"})
        self.pins()
        code, out, err = self.run_tool("candidates", "--product", "headings-2-train", "--no-ud")
        self.assertEqual(1, code)                                                       # the size gate (300 imperative candidates) is not met in a fixture
        self.assertIn("only 2 imperative candidates", err)
        rows = self.read_candidates("hdg2")
        by_text = {r["text"]: r for r in rows}
        self.assertEqual({"Check The Valve", "Remove The Cover", "Mass Characteristics", "Stop Pump Seal", "Pump Seal Check"}, set(by_text))      # `Install Pump Seal` is neither frame
        self.assertEqual({"h2i-"}, {r["cand_id"][:4] for r in rows if r["text"] in ("Check The Valve", "Remove The Cover")})
        self.assertEqual({"h2n-"}, {r["cand_id"][:4] for r in rows if r["text"] in ("Mass Characteristics", "Stop Pump Seal", "Pump Seal Check")})
        self.assertEqual({fd.IMPERATIVE_SHAPE}, {r["shape"] for r in rows})                 # both frames are labeled as the intended-VERB shape; the labelers see no shape
        self.assertEqual({"0"}, {r["target"] for r in rows})
        self.assertEqual({"target"}, {r["role"] for r in rows})
        self.assertEqual(len(rows), len({r["cand_id"] for r in rows}))

    def test_only_the_documents_of_the_side_of_the_product_give_candidates(self):
        self.sound_draft({"army-a": ct.FRAME, "army-b": ct.FRAME}, {"army-a": ["Check The Valve"], "army-b": ["Remove The Cover"]})
        self.sides(**{"army-a": "train", "army-b": "dev"})
        self.pins()
        self.run_tool("candidates", "--product", "headings-2-train", "--no-ud")
        self.assertEqual(["Check The Valve"], [r["text"] for r in self.read_candidates("hdg2")])
        self.run_tool("candidates", "--product", "headings-2", "--no-ud")
        self.assertEqual(["Remove The Cover"], [r["text"] for r in self.read_candidates("hdg2dev")])

    def test_the_development_heading_strata_one_candidate_per_line_rarest_first(self):
        lines = ["Check The Valve", "Pump Seal Check", "Tank Washington", "Check Valve Washington", "Remove The Cover"]
        self.sound_draft({"army-a": ct.FRAME}, {"army-a": lines})
        self.sides(**{"army-a": "dev"})
        self.pins()
        self.run_tool("candidates", "--product", "headings-2", "--no-ud")
        rows = {r["text"]: r for r in self.read_candidates("hdg2dev")}
        self.assertEqual(fd.IMPERATIVE_SHAPE, rows["Check The Valve"]["shape"])
        self.assertEqual("target", rows["Check The Valve"]["role"])
        self.assertEqual((fd.NOUN_SHAPE, "target", "2"), (rows["Pump Seal Check"]["shape"], rows["Pump Seal Check"]["role"], rows["Pump Seal Check"]["target"]))
        self.assertEqual((fd.PROPER_SHAPE, "control"), (rows["Tank Washington"]["shape"], rows["Tank Washington"]["role"]))
        self.assertEqual(len(rows), len(self.read_candidates("hdg2dev")))                  # one candidate per line
        self.assertEqual("h2d-", rows["Check The Valve"]["cand_id"][:4])

    def test_a_development_line_near_a_training_heading_line_is_dropped_and_counted(self):
        self.sound_draft({"army-a": ct.FRAME, "army-b": ct.FRAME}, {"army-a": ["Check The Valve Seal", "Remove The Cover"], "army-b": ["Check The Valve Seal Pump"]})
        self.sides(**{"army-a": "dev", "army-b": "train"})
        self.pins()
        base.corpus_file(os.path.join(self.data, "headings", "headings.tsv"), [("army-headings-1", "Remove The Cover Seal")])
        self.run_tool("candidates", "--product", "headings-2", "--no-ud")
        # Jaccard of {check, the, valve, seal} with the TRAIN-side line of 5 tokens is 4/5 = 0.8 (dropped); {remove, the, cover} with the old line is 3/4 = 0.75 (kept)
        self.assertEqual(["Remove The Cover"], [r["text"] for r in self.read_candidates("hdg2dev")])
        report = "\n".join(common.read_lines(os.path.join(self.root, "rounds", "hdg2dev", "private", "candidates-report.md")))
        self.assertIn("jaccard-train 1", report)
        self.assertEqual(0.8, fd.JACCARD_DROP)
        self.assertTrue(fd.near_training_line(["Remove", "The", "Cover"], [frozenset({"remove", "the", "cover"})]))
        self.assertTrue(fd.near_training_line(["a", "b", "c", "d", "e"], [frozenset({"a", "b", "c", "d"})]))
        self.assertFalse(fd.near_training_line(["a", "b", "c", "d"], [frozenset({"a", "b", "c"})]))

    def test_pilot_lines_are_not_drawn_again_and_count_against_the_caps_of_their_side(self):
        lines = ["Check The Valve", "Remove The Cover", "Seal The Pump"]
        self.sound_draft({"army-a": ct.FRAME}, {"army-a": lines})
        self.sides(**{"army-a": "train"})
        self.pins()
        pilot = [dict(cand_id="hp-c00001", text="Check The Valve", lemma="check", source="army-a", target=0, shape=fd.IMPERATIVE_SHAPE)]
        self.round("hdg-pilot", pilot)
        scan = sx.Scan()
        scan.units = [("army-a", n, t, base.simple_tokens(t)) for n, t in enumerate(lines, 1)]
        lex = {k: frozenset(v.split()) for k, v in LEXICON.items()}
        prior = sd.read_candidates(sd.RoundWork(self.root, "hdg-pilot"))
        rows, _, counts = fd.heading_candidates(scan, "headings-2-train", lex, prior_rows=prior)
        self.assertEqual({"Remove The Cover", "Seal The Pump"}, {r["text"] for r in rows})
        self.assertEqual(1, counts["prior"])
        with mock.patch.object(fd, "HDG2_CANDIDATE_LEMMA_CAP", 1):
            rows, _, _ = fd.heading_candidates(scan, "headings-2-train", lex, prior_rows=prior)
            self.assertEqual({"Remove The Cover", "Seal The Pump"}, {r["text"] for r in rows})          # `check` is at its cap through the pilot line
            scan.units.append(("army-a", 9, "Check It", ["Check", "It"]))
            rows, _, _ = fd.heading_candidates(scan, "headings-2-train", lex, prior_rows=prior)
            self.assertNotIn("Check It", {r["text"] for r in rows})
        rows, _, _ = fd.heading_candidates(scan, "headings-2-train", lex, prior_rows=[], seed=1)
        self.assertEqual(len(rows), 3 + 1)

    def test_the_heading_candidate_caps_lemma_and_document(self):
        lex = {k: frozenset(v.split()) for k, v in LEXICON.items()}
        scan = sx.Scan()
        scan.units = [("d%d" % (n % 2), n, "Check The Valve %d" % n, base.simple_tokens("Check The Valve %d" % n)) for n in range(20)]
        rows, _, counts = fd.heading_candidates(scan, "headings-2-train", lex)
        self.assertEqual(fd.HDG2_CANDIDATE_LEMMA_CAP, len(rows))
        rows, _, _ = fd.heading_candidates(scan, "headings-2", lex)
        self.assertEqual(fd.DEV_HEADING_LEMMA_CAP, len(rows))
        with mock.patch.object(fd, "DEV_HEADING_LEMMA_CAP", 100), mock.patch.object(fd, "DEV_CANDIDATE_DOCUMENT_CAP", 3):
            rows, _, _ = fd.heading_candidates(scan, "headings-2", lex)
            self.assertEqual(6, len(rows))                                                                  # 3 per document in 2 documents

    def test_compound_2_candidates_are_every_frame_hit_in_the_seeded_order_with_a_lemma_cap(self):
        sentences = ["Tank oil check %d ." % n for n in range(1, 11)] + ["Tank oil seal 1 ."]
        self.sound_draft({"army-a": sentences + ct.NOT_FRAME})
        self.sides(**{"army-a": "train"})
        self.pins()
        code, _, err = self.run_tool("candidates", "--product", "compound-2", "--no-ud")
        self.assertEqual(1, code)
        self.assertIn("only 7 candidates (at least 2000 needed)", err)
        rows = self.read_candidates("cmp2")
        taken = sorted(r["text"] for r in rows if r["lemma"] == "check")
        self.assertEqual(fd.CMP2_CANDIDATE_LEMMA_CAP, len(taken))
        expected = sorted(("Tank oil check %d ." % n for n in range(1, 11)), key=lambda t: sd.rank_of(sd.SEED, "compound2", sd.sent_key_of(t)))[:6]
        self.assertEqual(sorted(expected), taken)
        self.assertEqual({"cm2-"}, {r["cand_id"][:4] for r in rows})
        self.assertEqual({chd.COMPOUND_SHAPE}, {r["shape"] for r in rows})

    def test_the_compound_head_2_band_order_union_lemma_cap_and_controls(self):
        lex = {k: frozenset(v.split()) for k, v in LEXICON.items()}
        sentences = {"Tank oil check %d ." % n: q for n, q in enumerate([0.05, 0.10, 0.20, 0.50, 0.96, 0.97, 0.99], 1)}
        sentences.update({"Tank oil seal 1 .": 0.4, "Tank oil pump 1 .": 0.3})
        scan = sx.Scan()
        scan.units = [("army-a", n, t, base.simple_tokens(t)) for n, t in enumerate(sorted(sentences), 1)] + [
            ("army-a", 20, "You must check the valve .", base.simple_tokens("You must check the valve ."))]
        hmm = FakeHmm({" ".join(base.simple_tokens(t)): q for t, q in sentences.items()})
        rows, _, counts = fd.compound_head_candidates(scan, lex, hmm)
        targets = [r for r in rows if r["role"] == "target"]
        self.assertEqual(sorted(["0.100", "0.200", "0.500", "0.960"][:fd.LEMMA_CAP]), sorted(r["q"] for r in targets if r["lemma"] == "check"))        # band [0.10, 0.97), 4 per lemma
        self.assertEqual({"check", "seal", "pump"}, {r["lemma"] for r in targets})
        self.assertEqual((counts["frame"], counts["band"]), (9, 6))                                 # the three outside the band (0.05, 0.97, 0.99) are no candidate
        controls = [r for r in rows if r["role"] == "control"]
        self.assertEqual(["You must check the valve ."], [r["text"] for r in controls])
        self.assertEqual({""}, {r["q"] for r in controls})
        self.assertEqual(len(rows), len({r["cand_id"] for r in rows}))
        old = {"check": 3}                                                                         # the union with the old targets: only one more `check` target
        rows, _, _ = fd.compound_head_candidates(scan, lex, hmm, old_counts=old)
        self.assertEqual(["0.100"], [r["q"] for r in rows if r["role"] == "target" and r["lemma"] == "check"])
        rows, _, _ = fd.compound_head_candidates(scan, lex, hmm, old_counts={"check": 4})
        self.assertEqual([], [r for r in rows if r["role"] == "target" and r["lemma"] == "check"])

    def test_the_second_control_round_has_controls_only_for_the_lemmas_with_room(self):
        lex = {k: frozenset(v.split()) for k, v in LEXICON.items()}
        scan = sx.Scan()
        texts = ["You must check the valve .", "You must seal the valve .", "You must check the pump ."]
        scan.units = [("army-a", n, t, base.simple_tokens(t)) for n, t in enumerate(texts, 1)]
        rows, _, counts = fd.compound_head_candidates(scan, lex, FakeHmm({}), controls_only=True, room={"check": 1, "seal": 0})
        self.assertEqual(["You must check the pump ."], sorted(r["text"] for r in rows))              # one control of `check` has room (the order of the seeded rank decides which)
        self.assertEqual({"check"}, {r["lemma"] for r in rows})
        self.assertEqual(1, len(rows))
        self.assertEqual({"k2-"}, {r["cand_id"][:3] for r in rows})
        self.assertEqual(0, counts["targets"])

    def test_the_candidate_command_of_compound_head_2_reads_the_pinned_table_and_writes_both_control_rounds(self):
        sentences = ["Tank oil check %d ." % n for n in range(1, 6)] + ["You must check the valve .", "You must seal the pump ."]
        self.sound_draft({"army-a": sentences})
        self.sides(**{"army-a": "dev"})
        self.pins(hmm=True)
        masses = {" ".join(base.simple_tokens(t)): 0.3 + 0.05 * n for n, t in enumerate(sentences)}
        with mock.patch.object(fd, "read_hmm_pinned", return_value=FakeHmm(masses)):
            code, _, err = self.run_tool("candidates", "--product", "compound-head-2", "--no-ud")
            self.assertEqual(1, code)
            self.assertIn("target candidates (at least 450 needed)", err)
            first = self.read_candidates("chd2")
            self.assertEqual({"check"}, {r["lemma"] for r in first if r["role"] == "target"})
            self.assertEqual(3, len([r for r in first if r["role"] == "target"]))              # 4 per lemma minus the old target of `check` (the fixture has 1)
            self.assertEqual({"c2-"}, {r["cand_id"][:3] for r in first})
            self.round("chd2", [dict(cand_id="c2-c%05d" % (n + 1), text=r["text"], lemma=r["lemma"], source="army-a", target=int(r["target"]), role=r["role"], q=r["q"])
                                for n, r in enumerate(first)], with_q=True)
            self.sound_draft({"army-a": sentences})                                                # `round` rewrote the draft: restore the sound one and its pins
            self.pins(hmm=True)
            code, _, err = self.run_tool("candidates", "--product", "compound-head-2", "--round", "chd2c", "--no-ud")
            self.assertEqual(0, code, err)
            second = self.read_candidates("chd2c")
            self.assertTrue(all(r["role"] == "control" and r["cand_id"].startswith("k2-") for r in second))
            code, _, err = self.run_tool("candidates", "--product", "compound-2", "--round", "chd2c", "--no-ud")
            self.assertEqual(1, code)
            self.assertIn("only for compound-head-2", err)

    def test_the_preflight_prints_counts_only_per_side(self):
        self.sound_draft({"army-a": ct.FRAME + ct.NOT_FRAME, "army-b": ["Tank oil flow 9 .", "Fuel tank seal 9 ."]},
                         {"army-a": ["Check The Valve", "Mass Characteristics"], "army-b": ["Remove The Cover"]})
        self.sides(**{"army-a": "dev", "army-b": "train"})
        code, out, err = self.run_tool("preflight", "--no-ud")
        self.assertEqual(0, code, err)
        self.assertIn("documents in the draft: 2 (dev 1, train 1; 1 agency families)", out)
        self.assertIn("dev: 8 clean sentences", out)
        self.assertIn("1 imperative (strength 0), 1 initial-noun", out)
        self.assertIn("train: 2 clean sentences, 2 compound frame hits", out)
        for text in ct.FRAME + ["Check The Valve", "Mass Characteristics"]:
            self.assertNotIn(text, out)                                                         # counts only

    def test_the_size_gates_of_every_product_fail_closed(self):
        self.assertEqual([], fd.gate_messages("compound-2", dict(candidates=2000, lemmas=400)))
        self.assertEqual(2, len(fd.gate_messages("compound-2", dict(candidates=1999, lemmas=399))))
        self.assertEqual([], fd.gate_messages("compound-head-2", dict(targets=450)))
        self.assertEqual(1, len(fd.gate_messages("compound-head-2", dict(targets=449))))
        self.assertEqual([], fd.gate_messages("headings-2", dict(strata=dict(imperative=150, noun=60, proper=24), prior=10)))
        self.assertEqual(3, len(fd.gate_messages("headings-2", dict(strata=dict(imperative=1, noun=1, proper=1), prior=0))))
        self.assertEqual([], fd.gate_messages("headings-2-train", dict(strata=dict(imperative=290), prior=10)))
        self.assertEqual(1, len(fd.gate_messages("headings-2-train", dict(strata=dict(imperative=289), prior=10))))


# ================================================================ the pilot and its gate

class PilotTests(Mini):
    lex = dict({k: frozenset(v.split()) for k, v in LEXICON.items()}, **{"v" + a + b: frozenset({"VERB"}) for a in "abcdefg" for b in "abcdefg"})

    def units(self, count, lemmas=("check",), documents=("army-a",), start=1):
        out = []
        for n in range(count):
            text = "%s The Valve %d" % (lemmas[n % len(lemmas)].capitalize(), start + n)
            out.append((documents[n % len(documents)], n + 1, text, base.simple_tokens(text)))
        return out

    def test_the_gate_arithmetic_is_half_of_the_lines_piloted_rounded_up(self):
        self.assertEqual("STOP", fd.pilot_verdict(74, 150)[0])
        self.assertEqual("PROCEED", fd.pilot_verdict(75, 150)[0])
        self.assertEqual("PROCEED", fd.pilot_verdict(150, 150)[0])
        self.assertEqual("STOP", fd.pilot_verdict(0, 150)[0])
        self.assertEqual("STOP", fd.pilot_verdict(69, 140)[0])                                     # amendment A1: ceil(n / 2), one fewer stops
        self.assertEqual("PROCEED", fd.pilot_verdict(70, 140)[0])
        self.assertEqual("STOP", fd.pilot_verdict(70, 141)[0])
        self.assertEqual("PROCEED", fd.pilot_verdict(71, 141)[0])
        self.assertEqual("PROCEED", fd.pilot_verdict(73, 145)[0])
        self.assertEqual("STOP", fd.pilot_verdict(72, 145)[0])
        self.assertEqual((70, 71, 75), (fd.pilot_confirmed_min(140), fd.pilot_confirmed_min(141), fd.pilot_confirmed_min(150)))
        self.assertEqual("SCARCITY", fd.pilot_verdict(139, 139)[0])
        self.assertEqual("SCARCITY", fd.pilot_verdict(100, 100)[0])
        self.assertIn("INCOMPLETE", fd.pilot_verdict(10, 150)[1])
        self.assertIn("fewer than 71", fd.pilot_verdict(70, 141)[1])
        self.assertEqual((150, 140, 20, 6, 50), (fd.PILOT_SIZE, fd.PILOT_MIN_SIZE, fd.PILOT_DOCUMENT_CAP, fd.PILOT_LEMMA_CAP, fd.PILOT_FAMILY_CAP))

    def test_the_pilot_family_cap_is_read_at_title_family_level(self):
        draft = [dict(source_id="a1", family="army", title="TC 3-04.1 Skill Level 1 Soldier Task Book"), dict(source_id="a2", family="army", title="TC 3-04.1 Soldier Task Book Skill Level 2"),
                 dict(source_id="a3", family="army", title="FM 5-34 Engineer Field Data")]
        title = fd._title_family_of(draft)
        self.assertEqual(title["a1"], title["a2"])                                                 # two scans of one publication share the key
        self.assertNotEqual(title["a1"], title["a3"])
        self.assertEqual({"army"}, set(fd._family_of(draft).values()))                             # the agency level is a different (coarser) map
        self.assertEqual(title["a1"], fd.family_key("TC 3-04.1 Skill Level 1 Soldier Task Book"))

    def test_the_pilot_sample_is_the_first_150_in_the_seeded_order_with_the_caps(self):
        scan = sx.Scan()
        words = ["v" + a + b for a in "abcdefg" for b in "abcdefg"][:45]
        scan.units = [(d, n, t, k) for n, (d, _, t, k) in enumerate(self.units(900, words, ["d%d" % i for i in range(60)]), 1)]
        family = {"d%d" % i: "fam%d" % (i % 3) for i in range(60)}
        rows, report, counts = fd.pilot_candidates(scan, self.lex, family)
        self.assertEqual(150, len(rows))
        self.assertTrue(all(v <= fd.PILOT_LEMMA_CAP for v in collections.Counter(r["lemma"] for r in rows).values()))
        self.assertTrue(max(collections.Counter(family[r["source_id"]] for r in rows).values()) <= 50)
        self.assertTrue(max(collections.Counter(r["source_id"] for r in rows).values()) <= 20)
        self.assertEqual(["hp-c%05d" % n for n in range(1, 151)], sorted(r["cand_id"] for r in rows))
        again, _, _ = fd.pilot_candidates(scan, self.lex, family)
        self.assertEqual([r["text"] for r in rows], [r["text"] for r in again])
        # the sample is the first 150 of the seeded order that the caps allow: the first line of that order is always in it
        first = min(((sd.rank_of(sd.SEED, "pilot", sd.sent_key_of(u[2])), u[2]) for u in scan.units))[1]
        self.assertIn(first, [r["text"] for r in rows])
        narrow = fd.pilot_candidates(scan, self.lex, family, documents={"d0", "d1"})            # 2 documents of at most 20 lines: fewer than 140, and the verdict says so
        self.assertLess(narrow[2]["sampled"], 140)
        self.assertEqual("SCARCITY", fd.pilot_verdict(narrow[2]["sampled"], narrow[2]["sampled"])[0])
        with mock.patch.object(fd, "PILOT_FAMILY_CAP", 20):                                         # a family cap that binds: the sample is however many lines the caps give
            capped = fd.pilot_candidates(scan, self.lex, family)
            self.assertEqual(60, len(capped[0]))
            self.assertTrue(max(collections.Counter(family[r["source_id"]] for r in capped[0]).values()) <= 20)

    def test_the_pilot_command_stops_with_scarcity_when_fewer_than_140_lines_exist(self):
        self.sound_draft({"army-a": ct.FRAME}, {"army-a": ["Check The Valve", "Remove The Cover"]})
        self.sides(**{"army-a": "train"})
        self.pins()
        code, out, err = self.run_tool("pilot", "--no-ud")
        self.assertEqual(1, code)
        self.assertIn("PILOT SCARCITY", err)
        self.assertIn("only 2 of at least 140 pilot lines exist", err)
        self.assertEqual(2, len(self.read_candidates("hdg-pilot")))
        known = os.path.join(self.temp, "wave1.txt")
        common.write_text(known, ["# wave 1", "army-zzz"])
        code, _, err = self.run_tool("pilot", "--no-ud", "--documents", known)
        self.assertEqual(1, code)
        self.assertIn("not in the draft", err)

    def pilot_round(self, confirmed, total, others="NOUN"):
        specs = []
        for n in range(total):
            tag = "VERB" if n < confirmed else others
            text = "%s The Valve %d" % (("Check", "Seal", "Stop")[n % 3], n)
            tokens = base.simple_tokens(text)
            specs.append(dict(cand_id="hp-c%05d" % (n + 1), text=text, lemma=("check", "seal", "stop")[n % 3], source=("army-a", "army-b")[n % 2], target=0,
                              shape=fd.IMPERATIVE_SHAPE, tags=[tag] + ["NOUN"] * (len(tokens) - 1), opus=tag, sol=tag if n % 7 else "ADJ",
                              outcome="keep" if tag == "VERB" else "drop", reason="" if tag == "VERB" else "class-mismatch"))
        for spec in specs:
            if spec["tags"][0] == "VERB":
                spec["sol"] = "VERB"
        self.round("hdg-pilot", specs)

    def test_the_pilot_result_gate_and_the_counts_only_record(self):
        self.sound_draft({"army-a": ct.FRAME, "army-b": ct.FRAME})
        self.sides(**{"army-a": "train", "army-b": "dev"})
        self.pilot_round(75, 150)
        self.extra_documents = {"army-a", "army-b"}
        code, out, err = self.run_tool("pilot-result")
        self.assertEqual(0, code, err)
        self.assertIn("gate: PROCEED", out)
        record = "\n".join(common.read_lines(os.path.join(self.root, "rounds", "hdg-pilot", "private", "pilot-record.md")))
        self.assertIn("sampled 150, confirmed 75", record)
        self.assertIn("| noun-verb | 150 | 75 |", record)
        self.assertIn("| NOUN | ", record)
        self.assertNotIn("The Valve", record)                                                 # ids and counts only, no text
        self.pilot_round(74, 150)
        code, out, _ = self.run_tool("pilot-result")
        self.assertEqual(1, code)
        self.assertIn("gate: STOP (74 of 150 confirmed, fewer than 75", out)
        self.pilot_round(10, 100)
        code, out, _ = self.run_tool("pilot-result")
        self.assertEqual(1, code)
        self.assertIn("gate: SCARCITY", out)

    def test_a_line_is_confirmed_only_when_both_labelers_tag_token_0_verb(self):
        self.sound_draft({"army-a": ct.FRAME})
        self.sides(**{"army-a": "train"})
        specs = [dict(cand_id="hp-c0000%d" % n, text="Check Valve %d" % n, lemma="check", source="army-a", target=0, shape=fd.IMPERATIVE_SHAPE,
                      tags=["VERB", "NOUN", "NOUN"], opus=o, sol=s, outcome="keep" if o == s == "VERB" else "drop")
                 for n, (o, s) in enumerate([("VERB", "VERB"), ("VERB", "NOUN"), ("NOUN", "NOUN"), ("PROPN", "PROPN")], 1)]
        self.round("hdg-pilot", specs)
        items, candidates = fd.pilot_items(self.work, self.data)
        confirmed, sampled, lines = fd.pilot_numbers(items, candidates, {"army-a": "army"}, {}, {k: frozenset(v.split()) for k, v in LEXICON.items()})
        self.assertEqual((1, 4), (confirmed, sampled))
        text = "\n".join(lines)
        self.assertIn("| NOUN | 1 |", text)
        self.assertIn("| PROPN | 1 |", text)
        self.assertIn("| disagreement | 1 |", text)
        self.assertIn("| army | 4 | 1 |", text)


# ================================================================ builds

class BuildCompoundTests(Mini):
    def test_compound_2_kept_sentences_kinds_ids_files_and_drop_reasons(self):
        specs = (cmp_specs("check", 4, first=1) + cmp_specs("level", 4, first=21, source="army-b")
                 + [dict(cand_id="cm2-c00010", text="Tank oil seal 1 .", lemma="seal", source="army-a", target=2, head_tag="VERB", outcome="drop", reason="class-mismatch",
                         agreed="VERB")]
                 + [dict(cand_id="cm2-c00011", text="Tank oil pump 1 .", lemma="pump", source="army-a", target=2, outcome="drop", reason="class-mismatch", agreed="ADJ")]
                 + [dict(cand_id="cm2-c00012", text="Tank oil flow 1 .", lemma="flow", source="army-a", target=2, outcome="drop", reason="class-mismatch", agreed="VERB",
                         tags=["NOUN", "ADJ", "VERB", "NUM", "PUNCT"])]
                 + [dict(cand_id="cm2-c00013", text="Tank oil test 1 .", lemma="test", source="army-a", target=2, statuses=["unanimous", "disputed", "unanimous", "unanimous", "unanimous"])])
        self.sides(**{"army-a": "train", "army-b": "train"})
        built, report, out = self.build("compound-2", {"cmp2": (specs, False)}, allow_short=True)
        self.assertEqual(["compound2-%04d" % n for n in range(1, len(built) + 1)], [s["sent_id"] for s in built])
        _, sentences = common.read_corpus(os.path.join(out, "compound-2.tsv"))
        kinds = sorted(s["comments"]["kind"] for s in sentences)
        self.assertEqual(["noun"] * 8 + ["verb"], kinds)                                             # `seal` is read as a VERB after an agreed noun: a counter-example
        self.assertEqual({"class-mismatch": 1, "frame-mismatch": 1, "disputed-token": 1},
                         {k: v for k, v in drops_of(report).items() if k in ("class-mismatch", "frame-mismatch", "disputed-token")})
        self.assertEqual("# use = training", common.read_lines(os.path.join(out, "compound-2.tsv"))[6])
        self.assertTrue(all(os.path.exists(os.path.join(out, name)) for name in ("compound-2-sources.tsv", "compound-2-excluded-documents.tsv", "compound-2-dropped-sentences.tsv")))
        self.assertEqual({"compound-2-sources.tsv", "compound-2.tsv", "compound-2-excluded-documents.tsv", "compound-2-dropped-sentences.tsv"}, set(os.listdir(out)))

    def test_caps_lemma_document_and_the_verb_share_of_compound_2(self):
        specs = cmp_specs("check", 6, first=1) + cmp_specs("seal", 3, first=11, source="army-b")
        self.sides(**{"army-a": "train", "army-b": "train"})
        built, report, _ = self.build("compound-2", {"cmp2": (specs, False)}, allow_short=True)
        self.assertEqual(fd.CMP2_LEMMA_CAP + 3, len(built))
        self.assertEqual({"lemma-cap": 2}, {k: v for k, v in drops_of(report).items() if k == "lemma-cap"})
        kept = [dict(sent_key="k%d" % n, lemma="l%d" % n, source_id="d%d" % (n % 2), kind="noun") for n in range(60)]
        chosen, drops = fd.select_compound_train(kept)
        self.assertEqual(40, len(chosen))
        self.assertEqual(20, drops["document-cap"])
        kept = [dict(sent_key="k%d" % n, lemma="l%d" % n, source_id="d%d" % n, kind="noun" if n % 2 else "verb") for n in range(40)]
        chosen, drops = fd.select_compound_train(kept)
        verbs = sum(1 for c in chosen if c["kind"] == "verb")
        self.assertLessEqual(verbs * 100, 15 * len(chosen))
        self.assertEqual((935, 165, 15, 1100), (fd.CMP2_NOUN_CAP, fd.CMP2_VERB_CAP, fd.CMP2_VERB_PERCENT, fd.CMP2_MAX))

    def test_the_quota_gate_of_compound_2_refuses_below_the_minimum_and_never_waives_a_cap(self):
        self.sides(**{"army-a": "train"})
        self.round("cmp2", cmp_specs("check", 2))
        out = os.path.join(self.temp, "out")
        with mock.patch.object(fd, "draft_gate"):
            with self.assertRaises(fd.BuildError) as raised:
                fd.build(self.work, "compound-2", out, data_dir=self.data)
            self.assertIn("2 sentences, at least 600 needed", str(raised.exception))
            self.assertFalse(os.path.exists(out))
            code, _, err = self.run_tool("build", "--product", "compound-2", "--out-dir", out, "--allow-short")
            self.assertEqual(0, code, err)

        def sentence(n, lemma="check", kind="noun", doc="d"):
            tag = "VERB" if kind == "verb" else "NOUN"
            return dict(sent_id="compound2-%04d" % n, source_id=doc, text="Tank oil check %d ." % n, rows=[["Tank", "NOUN", "unanimous"], ["oil", "NOUN", "unanimous"],
                                                                                                           ["check", tag, "unanimous"], [str(n), "NUM", "unanimous"], [".", "PUNCT", "unanimous"]],
                        extra=["# head = 2", "# lemma = " + lemma, "# kind = " + kind])
        over = [sentence(n) for n in range(1, 6)]
        found = "\n".join(fd.quota_problems("compound-2", over, 0))
        self.assertIn("lemma check has 5 items, at most 4 allowed", found)
        self.assertIn("lemma check has 5 items", found)
        verbs = "\n".join(fd.quota_problems("compound-2", [sentence(n, "l%d" % n, "verb", "d%d" % n) for n in range(1, 5)], 0))
        self.assertIn("4 verb items of 4, at most 15 percent allowed", verbs)
        over_documents = "\n".join(fd.quota_problems("compound-2", [sentence(n, "l%d" % n) for n in range(1, 22)], 0))
        self.assertIn("document d has 21 items, at most 20 allowed", over_documents)
        self.assertNotIn("sentences, at least", "\n".join(fd.quota_problems("compound-2", [sentence(1)], 0)))

    def test_an_omitted_document_is_removed_after_the_caps_and_the_ids_have_no_gap(self):
        specs = cmp_specs("check", 3, first=1) + cmp_specs("seal", 3, first=11, source="army-b")
        self.sides(**{"army-a": "train", "army-b": "train"})
        everything, _, _ = self.build("compound-2", {"cmp2": (specs, False)}, allow_short=True)
        built, report, out = self.build("compound-2", {"cmp2": (specs, False)}, allow_short=True, omit=["army-b=co-author is a state agency"])
        self.assertEqual({"army-a"}, {s["source_id"] for s in built})
        self.assertEqual(["compound2-%04d" % n for n in range(1, 4)], [s["sent_id"] for s in built])
        self.assertEqual(3, drops_of(report)["omitted-document"])
        self.assertIn("army-b\tco-author is a state agency", common.read_lines(os.path.join(out, "compound-2-excluded-documents.tsv")))
        with self.assertRaises(fd.BuildError):
            self.build("compound-2", {"cmp2": (specs, False)}, allow_short=True, omit=["army-zzz"])

    def test_an_excluded_document_cannot_be_built_in_and_the_build_is_deterministic(self):
        specs = cmp_specs("check", 3, first=1) + cmp_specs("seal", 3, first=11, source="army-b")
        self.sides(**{"army-a": "train", "army-b": "train"})
        _, _, out = self.build("compound-2", {"cmp2": (specs, False)}, allow_short=True)
        first = read_bytes_of(out)
        _, _, out = self.build("compound-2", {"cmp2": (list(reversed(specs)), False)}, allow_short=True)
        self.assertEqual(first, read_bytes_of(out))
        common.write_text(os.path.join(self.data, "eval", "shapes-dev", "excluded-sources.tsv"),
                          ["\t".join(common.EXCLUDED_SOURCES_COLUMNS), "army-b\thttps://example.gov/army-b\tlicense doubt\t2026-11-10"])
        built, report, _ = self.build("compound-2", {"cmp2": (specs, False)}, allow_short=True)
        self.assertEqual({"army-a"}, {s["source_id"] for s in built})
        self.assertEqual(3, drops_of(report)["excluded-source"])

    def test_only_the_items_of_the_documents_of_the_train_side_enter(self):
        specs = cmp_specs("check", 3, first=1) + cmp_specs("seal", 3, first=11, source="army-b")
        self.sides(**{"army-a": "train", "army-b": "dev"})
        built, _, _ = self.build("compound-2", {"cmp2": (specs, False)}, allow_short=True)
        self.assertEqual({"army-a"}, {s["source_id"] for s in built})


class BuildHeadingTrainTests(Mini):
    def test_imperative_and_initial_noun_lines_with_exact_tags_and_the_pilot_items(self):
        pilot = imp_specs("seal", "Seal", 2, prefix="hp-", start=50)
        specs = imp_specs("check", "Check", 3) + noun_start_specs("mass", "Mass", 1, start=20)
        self.sides(**{"army-a": "train"})
        built, report, out = self.build("headings-2-train", {"hdg-pilot": (pilot, False), "hdg2": (specs, False)}, allow_short=True)
        self.assertEqual(6, len(built))
        _, sentences = common.read_corpus(os.path.join(out, "headings-2.tsv"))
        self.assertEqual(["headings2-%04d" % n for n in range(1, 7)], [s["sent_id"] for s in sentences])
        by_stratum = {}
        for s in sentences:
            by_stratum.setdefault(s["comments"]["stratum"], set()).add(s["rows"][int(s["comments"]["word"])][1])
        self.assertEqual({"imperative": {"VERB"}, "initial-noun": {"NOUN"}}, by_stratum)
        self.assertIn("kinds: imperative 5, initial-noun 1", "\n".join(report))
        self.assertEqual({"headings-2.tsv", "headings-2-sources.tsv", "headings-2-excluded-documents.tsv", "headings-2-dropped-sentences.tsv"}, set(os.listdir(out)))

    def test_drop_reasons_and_the_contrast_stratum_rules(self):
        specs = (imp_specs("check", "Check", 1)
                 + [dict(cand_id="h2i-c00002", text="Check Valve 2", lemma="check", source="army-a", target=0, shape=fd.IMPERATIVE_SHAPE, outcome="drop", reason="class-mismatch",
                         agreed="NOUN", tags=["NOUN"] * 3),                                                       # not a contrast candidate (an imperative-frame line read as a noun)
                    dict(cand_id="h2i-c00003", text="Check Valve 3", lemma="check", source="army-a", target=0, shape=fd.IMPERATIVE_SHAPE, outcome="drop",
                         reason="target-disagreement"),
                    dict(cand_id="h2i-c00004", text="Check Valve 4", lemma="check", source="army-a", target=0, shape=fd.IMPERATIVE_SHAPE, tags=["VERB", "NOUN", "NUM"],
                         statuses=["unanimous", "disputed", "unanimous"]),
                    dict(cand_id="h2n-c00005", text="Mass Valve 5", lemma="mass", source="army-a", target=0, shape=fd.IMPERATIVE_SHAPE, outcome="drop", reason="class-mismatch",
                         agreed="ADJ", tags=["ADJ", "NOUN", "NUM"]),                                              # a contrast line read as an adjective
                    dict(cand_id="h2n-c00006", text="Mass Valve 6", lemma="mass", source="army-a", target=0, shape=fd.IMPERATIVE_SHAPE, outcome="drop", reason="class-mismatch",
                         agreed="NOUN", tags=["NOUN", "NOUN", "NUM"], statuses=["unanimous", "disputed", "unanimous"])])
        self.sides(**{"army-a": "train"})
        built, report, _ = self.build("headings-2-train", {"hdg2": (specs, False)}, allow_short=True)
        self.assertEqual(["Check Valve Alpha"], [s["text"] for s in built])
        self.assertEqual({"class-mismatch": 2, "target-disagreement": 1, "disputed-token": 2}, {k: v for k, v in drops_of(report).items() if k in ("class-mismatch", "target-disagreement", "disputed-token")})

    def test_initial_noun_lines_are_capped_at_half_of_the_kept_imperative_lines_and_lemmas_at_4(self):
        kept = ([dict(sent_key="i%d" % n, lemma="v%d" % n, source_id="d%d" % (n % 9), kind="imperative", target=0) for n in range(10)]
                + [dict(sent_key="n%d" % n, lemma="m%d" % n, source_id="d%d" % (n % 9), kind="initial-noun", target=0) for n in range(10)])
        chosen, drops = fd.select_headings_train(kept)
        self.assertEqual((10, 5), (sum(c["kind"] == "imperative" for c in chosen), sum(c["kind"] == "initial-noun" for c in chosen)))
        self.assertEqual(5, drops["initial-noun-cap"])
        chosen, _ = fd.select_headings_train(kept, imperative_cap=4)
        self.assertEqual((4, 2), (sum(c["kind"] == "imperative" for c in chosen), sum(c["kind"] == "initial-noun" for c in chosen)))
        same = [dict(sent_key="s%d" % n, lemma="mass", source_id="d%d" % n, kind="imperative" if n < 6 else "initial-noun", target=0) for n in range(8)]
        chosen, drops = fd.select_headings_train(same)
        self.assertEqual(fd.HDG2_LEMMA_CAP, len(chosen))                                                    # 6 per lemma over both strata (amendment A1)
        self.assertGreater(drops["lemma-cap"], 0)
        self.assertEqual((6, 40, 900, 450, 300), (fd.HDG2_CANDIDATE_LEMMA_CAP, fd.HDG2_CANDIDATE_DOCUMENT_CAP, fd.HDG2_MAX_IMPERATIVE, fd.HDG2_MAX_INITIAL_NOUN, fd.HDG2_MIN_IMPERATIVE))
        self.assertEqual((150, 400, 6, 25), (fd.HDG2_IMPERATIVE_FLOOR, fd.HDG2_IMPERATIVE_CAP, fd.HDG2_LEMMA_CAP, fd.HDG2_DOCUMENT_CAP))

    def test_the_floor_refuses_and_allow_short_waives_only_the_floor(self):
        self.sides(**{"army-a": "train"})
        self.round("hdg2", imp_specs("check", "Check", 3))
        out = os.path.join(self.temp, "out")
        with mock.patch.object(fd, "draft_gate"):
            with self.assertRaises(fd.BuildError) as raised:
                fd.build(self.work, "headings-2-train", out, data_dir=self.data)
            self.assertIn("imperative: 3 lines, the floor is 150", str(raised.exception))
            self.assertFalse(os.path.exists(out))
            code, _, err = self.run_tool("build", "--product", "headings-2-train", "--out-dir", out, "--allow-short")
            self.assertEqual(0, code, err)

        def line(n, kind, tag, doc="d", lemma=None):
            return dict(sent_id="headings2-%04d" % n, source_id=doc, text="Check Valve", rows=[["Check", tag, "unanimous"], ["Valve", "NOUN", "unanimous"]],
                        extra=["# word = 0", "# lemma = " + (lemma or "l%d" % n), "# stratum = " + kind])
        found = "\n".join(fd.quota_problems("headings-2-train", [line(1, "imperative", "VERB"), line(2, "initial-noun", "NOUN"), line(3, "initial-noun", "NOUN")], floors={}))
        self.assertIn("initial-noun: 2 lines, at most 1/2 of the 1 imperative lines", found)
        found = "\n".join(fd.quota_problems("headings-2-train", [line(1, "imperative", "NOUN"), line(2, "initial-noun", "VERB")], floors={}))
        self.assertIn("tagged NOUN for kind imperative", found)
        self.assertIn("tagged VERB for kind initial-noun", found)

    def test_a_dev_pilot_line_never_enters_a_training_source(self):
        pilot = imp_specs("seal", "Seal", 2, prefix="hp-", source="army-b", start=50)
        self.sides(**{"army-a": "train", "army-b": "dev"})
        built, _, _ = self.build("headings-2-train", {"hdg-pilot": (pilot, False), "hdg2": (imp_specs("check", "Check", 2), False)}, allow_short=True)
        self.assertEqual({"army-a"}, {s["source_id"] for s in built})
        built, _, out = self.build("headings-2", {"hdg-pilot": (pilot, False), "hdg2dev": (dev_heading_specs("noun", "valve", "Valve", 1, first=10), False)}, allow_short=True)
        self.assertEqual(["Seal Valve Juliet", "Seal Valve Kilo"], sorted(s["text"] for s in built if "Seal" in s["text"]))


class BuildDevTests(Mini):
    def test_compound_head_2_orders_by_q_caps_the_union_and_takes_controls_for_the_target_lemmas(self):
        qs = [0.12, 0.15, 0.2, 0.3, 0.4, 0.5, 0.6]
        specs = [dict(cand_id="c2-c%05d" % n, text="Tank oil check %d ." % n, lemma="check", source="army-a", target=2, q="%.3f" % q) for n, q in enumerate(qs, 1)]
        specs += [dict(cand_id="c2-c%05d" % n, text="Tank oil seal %d ." % n, lemma="seal", source="army-a", target=2, q="0.700") for n in range(10, 12)]
        controls = [dict(cand_id="c2-c%05d" % n, text="You must %s the valve %d ." % (word, n), lemma=word, source="army-a", target=2, role="control", q="",
                         head_tag="VERB") for n, word in ((20, "check"), (21, "check"), (22, "check"), (23, "pump"))]
        self.sides(**{"army-a": "dev"})
        built, report, out = self.build("compound-head-2", {"chd2": (specs + controls, True)}, allow_short=True)
        _, sentences = common.read_corpus(os.path.join(out, "compound-head-2.tsv"))
        self.assertEqual(["shapes-dev-compound-head-2-%04d" % n for n in range(1, len(built) + 1)], [s["sent_id"] for s in sentences])
        targets = [s for s in sentences if s["comments"]["role"] == "target"]
        # the old compound-head.tsv of the fixture holds one target of `check`: 3 more fit (4 over the union), the hardest first
        self.assertEqual(["Tank oil check 1 .", "Tank oil check 2 .", "Tank oil check 3 ."], [s["text"] for s in targets if s["comments"]["lemma"] == "check"])
        self.assertEqual(2, len([s for s in targets if s["comments"]["lemma"] == "seal"]))
        self.assertEqual(5, drops_of(report)["lemma-cap"])                                                   # 4 `check` targets over the union and 1 control beyond 2 per lemma
        ctl = [s for s in sentences if s["comments"]["role"] == "control"]
        self.assertEqual(2, len(ctl))                                                                       # `check` 2 per lemma; `pump` is no target lemma
        self.assertEqual({"check"}, {s["comments"]["lemma"] for s in ctl})
        self.assertEqual("# use = development only: select settings here, never train on it, never report it as a held-out figure", common.read_lines(os.path.join(out, "compound-head-2.tsv"))[6])
        self.assertEqual({"compound-head-2.tsv", "compound-head-2-sources.tsv"}, set(os.listdir(out)))

    def test_the_ing_share_is_at_most_a_quarter_of_the_union(self):
        self.assertEqual((100, 4, 2, 0.25), (fd.OLD_TARGETS, fd.LEMMA_CAP, fd.CONTROL_LEMMA_CAP, fd.ING_SHARE))
        limit = 210
        specs = [dict(cand_id="c2-c%05d" % n, text="Tank oil checking%s %d ." % ("x" * 0, n), lemma="l%d" % n, source="army-a", target=2, q="0.200") for n in range(1, 4)]
        items = [dict(cand_id=s["cand_id"], lemma=s["lemma"], role="target", outcome="keep", target=2) for s in specs]
        cands = {s["cand_id"]: dict(q=s["q"], tokens=base.simple_tokens(s["text"])) for s in specs}
        targets, _, _ = fd.select_compound_head(items, cands, {}, 0)
        self.assertEqual(3, len(targets))
        targets, _, drops = fd.select_compound_head(items, cands, {}, 78)                                  # the old tranche already uses the whole -ing share of 78
        self.assertEqual(0, len(targets))
        self.assertEqual(3, drops["ing-share"])
        self.assertEqual(78, -(-(fd.OLD_TARGETS + limit) // 4))

    def test_the_control_room_of_the_second_round(self):
        items = [dict(cand_id="a", lemma="check", role="target", outcome="keep", target=2), dict(cand_id="b", lemma="seal", role="target", outcome="keep", target=2),
                 dict(cand_id="c", lemma="check", role="control", outcome="keep", target=2), dict(cand_id="d", lemma="seal", role="control", outcome="keep", target=2),
                 dict(cand_id="e", lemma="seal", role="control", outcome="keep", target=2)]
        cands = {k: dict(q="0.3", tokens=["Tank", "oil", "x"]) for k in "abcde"}
        self.assertEqual({"check": 1}, fd.control_room(items, cands, {}, 0))

    def test_headings_2_imperative_first_caps_kinds_and_the_page_number_rule(self):
        specs = (dev_heading_specs("imperative", "check", "Check", 8, first=1) + dev_heading_specs("noun", "seal", "Seal", 3, first=20)
                 + dev_heading_specs("proper", "nile", "Nile", 3, first=40, source="army-b")
                 + [dict(dev_heading_specs("noun", "level", "Level", 1, first=60)[0], text="Pump Level 12", cand_id="h2d-c00060")])
        specs[-1]["tags"] = ["NOUN", "NOUN", "NUM"]
        self.sides(**{"army-a": "dev", "army-b": "dev"})
        built, report, out = self.build("headings-2", {"hdg2dev": (specs, False)}, allow_short=True)
        _, sentences = common.read_corpus(os.path.join(out, "headings-2.tsv"))
        kinds = [(s["comments"]["kind"], s["comments"]["role"]) for s in sentences]
        self.assertEqual([("imperative", "target")] * 6 + [("noun", "target")] * 3 + [("proper", "control")] * 2, kinds)       # 6 per lemma (amendment A1); controls 2 per lemma
        self.assertEqual({"lemma-cap": 3, "page-number": 1}, {k: v for k, v in drops_of(report).items() if k in ("lemma-cap", "page-number")})
        self.assertEqual(["shapes-dev-headings-2-%04d" % n for n in range(1, 12)], [s["sent_id"] for s in sentences])
        self.assertEqual({"headings-2.tsv", "headings-2-sources.tsv"}, set(os.listdir(out)))

    def test_the_development_quota_gates_have_floors_and_caps(self):
        self.sides(**{"army-a": "dev"})
        self.round("hdg2dev", dev_heading_specs("imperative", "check", "Check", 2))
        out = os.path.join(self.temp, "out")
        with mock.patch.object(fd, "draft_gate"):
            with self.assertRaises(fd.BuildError) as raised:
                fd.build(self.work, "headings-2", out, data_dir=self.data)
        for part in ("imperative: 2, the floor is 80", "noun: 0, the floor is 30", "controls: 0, the floor is 12"):
            self.assertIn(part, str(raised.exception))
        self.assertEqual({"imperative": (80, 130), "noun": (30, 50), "controls": (12, 20)}, fd.DEV_QUOTAS["headings-2"])
        self.assertEqual({"targets": (150, 210), "controls": (30, 45)}, fd.DEV_QUOTAS["compound-head-2"])
        self.round("chd2", [dict(cand_id="c2-c00001", text="Tank oil seal 1 .", lemma="seal", source="army-a", target=2, q="0.2")], with_q=True)
        with mock.patch.object(fd, "draft_gate"):
            with self.assertRaises(fd.BuildError) as raised:
                fd.build(self.work, "compound-head-2", out, data_dir=self.data)
        self.assertIn("targets: 1, the floor is 150", str(raised.exception))
        self.assertIn("controls: 0, the floor is 30", str(raised.exception))

        def sentence(n, role, lemma, doc="d"):
            return dict(sent_id="s%d" % n, source_id=doc, text="Tank oil check", rows=[["Tank", "NOUN", "unanimous"], ["oil", "NOUN", "unanimous"], ["check", "NOUN" if role == "target" else "VERB", "unanimous"]],
                        extra=["# shape = n-compound-head-ambiguous", "# role = " + role, "# target = 2", "# lemma = " + lemma, "# origin = real"])
        found = "\n".join(fd.quota_problems("compound-head-2", [sentence(n, "target", "check") for n in range(1, 4)], floors={}, old_counts={"check": 2}))
        self.assertIn("target lemma check has 5 items, at most 4 allowed", found)
        found = "\n".join(fd.quota_problems("compound-head-2", [sentence(n, "control", "check") for n in range(1, 4)], floors={}))
        self.assertIn("control lemma check has 3 items, at most 2 allowed", found)


# ================================================================ the hand check

class ReviewTests(Mini):
    def sentences(self, product, count):
        out = []
        for n in range(1, count + 1):
            if product == "compound-2":
                text, idx, kind = "Tank oil check %d ." % n, 2, ("verb" if n % 10 == 0 else "noun")
                tag, extra = ("VERB" if kind == "verb" else "NOUN"), ["# head = 2", "# lemma = check", "# kind = " + kind]
            elif product == "headings-2-train":
                text, idx, kind = "Check Valve %d" % n, 0, ("imperative", "initial-noun")[n % 3 == 0]
                tag, extra = ("VERB" if kind == "imperative" else "NOUN"), ["# word = 0", "# lemma = check", "# stratum = " + kind]
            elif product == "headings-2":
                kind = ("imperative", "noun", "proper")[n % 3]
                text, idx = "Check Valve %d" % n, (0 if kind == "imperative" else 1)
                tag = {"imperative": "VERB", "noun": "NOUN", "proper": "PROPN"}[kind]
                extra = ["# shape = heading-titlecase", "# role = " + ("control" if kind == "proper" else "target"), "# target = %d" % idx, "# lemma = valve", "# origin = real",
                         "# kind = " + kind]
            else:
                text, idx, tag = "Tank oil check %d ." % n, 2, "NOUN"
                extra = ["# shape = n-compound-head-ambiguous", "# role = target", "# target = 2", "# lemma = check", "# origin = real"]
            rows = [[t, "NOUN", "unanimous"] for t in base.simple_tokens(text)]
            rows[idx][1] = tag
            out.append(dict(sent_id="%s-%04d" % (product, n), source_id="army-a", text=text, rows=rows, extra=extra,
                            comments=dict(part[2:].split(" = ", 1) for part in extra)))
        return out

    def test_the_plan_of_every_product_and_the_seeds(self):
        self.assertEqual({"compound-head-2": 20261121, "headings-2": 20261122, "compound-2": 20261123, "headings-2-train": 20261124}, fd.SAMPLE_SEEDS)
        self.assertEqual({"compound-head-2": 60, "headings-2": 60, "compound-2": 60, "headings-2-train": 60}, {p: sum(n for _, n in plan) for p, plan in fd.SAMPLE_PLAN.items()})
        self.assertEqual((60, 4, 0.95, 1), (fd.SAMPLE_SIZE, fd.SAMPLE_CONTROLS, fd.PRECISION_MIN, fd.CONTROLS_ACCEPTED_MAX))
        self.assertEqual((fd.SAMPLE_SEEDS["compound-head-2"], fd.SAMPLE_SEEDS["headings-2"]), (20261121, 20261122))

    def test_each_product_draws_its_quota_with_four_swapped_controls_and_is_seeded(self):
        for product, plan, size in (("compound-head-2", {"target": 60}, 200), ("headings-2", {"imperative": 30, "noun": 15, "proper": 15}, 300),
                                    ("compound-2", None, 200), ("headings-2-train", {"imperative": 40, "initial-noun": 20}, 300)):
            sample = fd.build_sample(product, self.sentences(product, size))
            real = [s for kind, s, _ in sample if kind == "real"]
            self.assertEqual(60, len(real), product)
            self.assertEqual(4, sum(1 for kind, _, _ in sample if kind == "control"), product)
            if plan:
                counts = {k: sum(1 for s in real if fd.judged(product, s)[1] == k) for k in plan}
                self.assertEqual(plan, counts, product)
            for kind, s, shown in sample:
                tag = s["rows"][fd.judged(product, s)[0]][1]
                self.assertEqual(tag if kind == "real" else ("NOUN" if tag == "VERB" else "VERB"), shown)
            again = fd.build_sample(product, self.sentences(product, size))
            self.assertEqual([s["sent_id"] for _, s, _ in sample], [s["sent_id"] for _, s, _ in again])
            self.assertNotEqual([s["sent_id"] for _, s, _ in sample], [s["sent_id"] for _, s, _ in fd.build_sample(product, self.sentences(product, size), seed=1)])

    def test_a_short_stratum_gives_fewer_items(self):
        sample = fd.build_sample("headings-2", self.sentences("headings-2", 30))
        self.assertEqual(30, sum(1 for kind, _, _ in sample if kind == "real"))

    def test_the_sample_file_and_the_review_round_trip(self):
        out = os.path.join(self.temp, "out")
        common.write_corpus(os.path.join(out, "headings-2.tsv"), fd.header_lines("headings-2-train"), self.sentences("headings-2-train", 40))
        base.table_file(os.path.join(out, "headings-2-sources.tsv"), sd.SOURCE_COLUMNS, [self.source_row("army-a", sentences="40")])
        code, printed, err = self.run_tool("sample", "--product", "headings-2-train", "--out-dir", out, "--size", "8", "--controls", "4")
        self.assertEqual(0, code, err)
        self.assertIn("12 items: 8 real, 4 control (seed 20261124)", printed)
        folder = os.path.join(self.root, "review", "headings-2-train")
        text = "\n".join(common.read_lines(os.path.join(folder, "review-sample.md")))
        self.assertIn("Marked word: Check (index 0)", text)
        _, keys = adjudicate.read_records(os.path.join(folder, "review-sample-key.tsv"))
        verdicts = os.path.join(self.temp, "answers.txt")
        common.write_text(verdicts, ["%s\t%s\t-\tcite" % (k["item"], "incorrect" if k["kind"] == "control" else "correct") for k in keys])
        code, printed, err = self.run_tool("review-result", "--product", "headings-2-train", "--verdicts", verdicts)
        self.assertEqual(0, code, err)
        self.assertIn("gate: PASSED", printed)
        common.write_text(verdicts, ["%s\tcorrect\t-\tcite" % k["item"] for k in keys])
        self.assertEqual(1, self.run_tool("review-result", "--product", "headings-2-train", "--verdicts", verdicts)[0])
        common.write_text(verdicts, ["R999\tcorrect\t-\tcite"])
        code, _, err = self.run_tool("review-result", "--product", "headings-2-train", "--verdicts", verdicts)
        self.assertEqual(1, code)
        self.assertIn("unknown items", err)
        code, printed, _ = self.run_tool("report", "--product", "headings-2-train", "--out-dir", out)
        self.assertEqual(0, code)
        self.assertIn("kinds: imperative 27, initial-noun 13", printed)


# ================================================================ the frozen prompt and the registration data

class PromptTests(unittest.TestCase):
    def test_the_template_is_the_frozen_block_of_pr_19(self):
        self.assertEqual(fd.PR19_TEMPLATE_SHA256, fd.template_sha256(OLD_WORK))
        self.assertEqual("9f1da5238dbcd196ca95b0795194245846955d2bb3a95a70bf0540d112d17db5", fd.PR19_TEMPLATE_SHA256)
        self.assertEqual(1903, len(fd.LABELER_TEMPLATE.replace("<W>", OLD_WORK).encode("utf-8")))
        self.assertEqual("7b5a24f2c5acfd540e571cedd18983632cddfc961a75608ec68b49e836b84ce7", fd.template_sha256(NEW_WORK))
        self.assertEqual(2, fd.LABELER_TEMPLATE.count("<W>"))
        self.assertTrue(fd.LABELER_TEMPLATE.startswith("You are a part-of-speech tagger."))
        self.assertIn("Read exactly two files and nothing else:", fd.LABELER_TEMPLATE)
        for word in ("stratum", "source", "lemma", "compound", "heading", "imperative", "intended"):
            self.assertNotIn(word, fd.LABELER_TEMPLATE.lower(), word)                                    # no hint of the shape reaches the labeler

    def test_the_run_prompt_fills_the_placeholders_and_adds_the_appendix(self):
        prompt = fd.labeler_prompt(NEW_WORK, "hdg-pilot", "b01", "opus")
        self.assertTrue(prompt.startswith(fd.LABELER_TEMPLATE.replace("<W>", NEW_WORK).replace("{ROUND}", "hdg-pilot").replace("{BATCH}", "b01").replace("{MODEL}", "opus")))
        for placeholder in ("<W>", "{ROUND}", "{BATCH}", "{MODEL}"):
            self.assertNotIn(placeholder, prompt)
        self.assertIn(NEW_WORK + "\\rounds\\hdg-pilot\\batches\\b01.tsv", prompt)
        self.assertEqual(fd.labeler_prompt(NEW_WORK, "r", "b", "m", appendix=False) + fd.LABELER_APPENDIX, fd.labeler_prompt(NEW_WORK, "r", "b", "m"))

    def test_the_appendix_has_the_pause_rule_verbatim_and_the_ip_rule(self):
        rule = ("Before each heavy step (fetch, build, training run, labeling launch), check whether "
                "`C:\\Users\\mnixon\\.copilot\\session-state\\217fb8fb-495d-4da7-89a1-b33f6a7805ad\\files\\PAUSE` exists. **If it does:** don't start the step; "
                "update `STATE.md` with the resume point, make a local WIP commit (no push), end the turn and reply \"PAUSED\".")
        self.assertEqual(rule, fd.PAUSE_RULE)
        self.assertIn(rule, fd.LABELER_APPENDIX)
        self.assertIn("b6862325-7069-4ff9-a4fc-abb8ecb60acc", fd.LABELER_APPENDIX)
        self.assertIn("never read or use any sentence supplied by a consumer", fd.LABELER_APPENDIX)
        self.assertIn("write nothing and reply \"PAUSED\"", fd.LABELER_APPENDIX)
        self.assertEqual(hashlib.sha256(fd.LABELER_APPENDIX.encode("utf-8")).hexdigest(), "27ba85551969b1ea3225c2417f6b1a66215c3a12c77b3cbe62c6af8b3c49ed19")

    def test_the_prompt_command_prints_the_run_prompt(self):
        m = Mini()
        m.setUp()
        self.addCleanup(m.doCleanups)
        code, out, err = m.run_tool("prompt", "--round", "cmp2", "--batch", "b02", "--model", "sol")
        self.assertEqual(0, code, err)
        self.assertEqual(fd.labeler_prompt(os.path.abspath(m.root), "cmp2", "b02", "sol"), out)
        code, out, _ = m.run_tool("prompt", "--round", "cmp2", "--batch", "b02", "--model", "sol", "--no-appendix")
        self.assertNotIn("PAUSED", out)

    def test_the_pre_registered_constants(self):
        self.assertEqual((20261110, {"C": 0.35, "H": 0.30}), (fd.SEED, fd.DEV_SHARE))
        self.assertEqual(sd.SEED, fd.SEED)
        self.assertEqual(("chd2", "chd2c", "hdg-pilot", "hdg2dev", "cmp2", "hdg2"), (fd.CANDIDATE_ROUND["compound-head-2"], fd.CONTROLS_ROUND, fd.PILOT_ROUND,
                                                                                    fd.CANDIDATE_ROUND["headings-2"], fd.CANDIDATE_ROUND["compound-2"], fd.CANDIDATE_ROUND["headings-2-train"]))
        self.assertEqual((800, 50, 30, 450), (fd.COMPOUND_HEAD_BUDGET, fd.CONTROL_POOL_FIRST, fd.CONTROL_POOL_SECOND, fd.COMPOUND_HEAD_MIN_CANDIDATES))
        self.assertEqual({"imperative": 300, "noun": 120, "proper": 60}, fd.DEV_HEADING_BUDGET)
        self.assertEqual((20, 20, 0.8, 6), (fd.DEV_CANDIDATE_DOCUMENT_CAP, fd.DEV_BUILD_DOCUMENT_CAP, fd.JACCARD_DROP, fd.DEV_HEADING_LEMMA_CAP))
        self.assertEqual((6, 3400, 2000, 400), (fd.CMP2_CANDIDATE_LEMMA_CAP, fd.CMP2_MAX_CANDIDATES, fd.CMP2_MIN_CANDIDATES, fd.CMP2_MIN_LEMMAS))
        self.assertEqual((4, 20, 600, 1100, 200), (fd.CMP2_LEMMA_CAP, fd.CMP2_DOCUMENT_CAP, fd.CMP2_MIN, fd.CMP2_MAX, fd.CMP2_MIN_BUILD_LEMMAS))
        self.assertEqual(1100, fd.CMP2_NOUN_CAP + fd.CMP2_VERB_CAP)
        self.assertEqual({"compound-head-2": "dev", "headings-2": "dev", "compound-2": "train", "headings-2-train": "train"}, fd.SIDE)
        self.assertEqual({"compound-head-2": "shapes-dev-compound-head-2-%04d", "headings-2": "shapes-dev-headings-2-%04d", "compound-2": "compound2-%04d",
                          "headings-2-train": "headings2-%04d"}, fd.SENT_ID)
        for pin in (fd.DRAFT_SHA256, fd.TOKENS_SHA256, fd.HMM_SHA256):
            self.assertTrue(pin is None or re.fullmatch(r"[0-9a-f]{64}", pin))
        self.assertEqual(("77e6a5250877694ae67e36a5a133d6cff6f8e245864ed1434f9fab6114d2f2c2", "4387679a975ee8ca005f8d9267ab38e2bc414ee888d907701e25cff2cd406e98",
                          "42c7588b9c01632a96749a1a14248e8bdf2cb1be64999e177bd657f288276edb"),
                         (fd.DRAFT_SHA256, fd.TOKENS_SHA256, fd.HMM_SHA256))                                  # the R3 pins (416 documents, tokens, the pinned Basic mass)


# ================================================================ no model, and the rule in the docstring

class NoModelTests(unittest.TestCase):
    """The tool decides nothing with a PartOfSpeech model: it imports no tagger, weights or decoder. The only non-frame input is the Basic mass of the WordSense library (hmm.tsv)."""

    def parsed(self):
        with open(fd.__file__, encoding="utf-8") as handle:
            return ast.parse(handle.read())

    def test_only_the_data_tools_are_imported(self):
        imported = set()
        for node in ast.walk(self.parsed()):
            if isinstance(node, ast.Import):
                imported |= {a.name.split(".")[0] for a in node.names}
            elif isinstance(node, ast.ImportFrom):
                imported.add((node.module or "").split(".")[0])
        allowed = {"argparse", "collections", "hashlib", "math", "os", "random", "re", "sys", "adjudicate", "common", "compound_heading_data", "imperative_data", "shapes_dev",
                   "shapes_dev_extra"}
        self.assertEqual(set(), imported - allowed)

    def test_no_string_of_the_code_names_a_model_file_or_runner_of_this_project(self):
        tree = self.parsed()
        docstring = ast.get_docstring(tree, clean=False)
        strings = [node.value for node in ast.walk(tree) if isinstance(node, ast.Constant) and isinstance(node.value, str) and node.value != docstring]
        joined = "\n".join(strings).lower()
        for forbidden in ("weights.bin", "dotnet", "partofspeech.trainer", "partofspeech.dll", "eval-arm", ".dll", "probe.tsv", "decoder", "eval-shapes", "shapes-tokenize"):
            self.assertNotIn(forbidden, joined, forbidden)

    def test_the_held_out_sets_are_seen_only_through_the_builder_of_shapes_dev(self):
        with open(fd.__file__, encoding="utf-8") as handle:
            source = handle.read()
        self.assertNotIn("import leakage", source)
        self.assertNotIn("probe.tsv", source)

    def test_no_name_of_the_code_scores_or_predicts(self):
        tree = self.parsed()
        names = {n.id for n in ast.walk(tree) if isinstance(n, ast.Name)} | {n.attr for n in ast.walk(tree) if isinstance(n, ast.Attribute)}
        for forbidden in ("tagger", "predict", "decode", "score", "weights", "classifier"):
            self.assertEqual([], [n for n in names if forbidden in n.lower()], forbidden)

    def test_the_docstring_states_the_pre_registered_rule_and_the_constants_match_it(self):
        doc = " ".join(fd.__doc__.split())
        for part in ("THE PRE-REGISTERED RULE", "FEWER THAN 75 OF 150 STOPS", "strength 0 only", "initial-noun", "Jaccard similarity 0.8", "stream C 0.35; stream H 0.30",
                     "family key", "800 target candidates", "3,400 in all", "at least 2,000 candidates and 400 head lemmas", "900 in all (gate: at least 300)", "initial-noun at most 450",
                     "imperative 150 to 400", "600 to 1,100 sentences", "20261110", "claude-opus-5.5", "gpt-6.1-sol", "claude-sonnet-5.5", "No FAA", "no consumer text",
                     "distribution statement other than public release", "excluded-documents.tsv", "a model review, not a human review", "ALL-CAPS", "the tool never calls a tagger",
                     "1. Documents.", "10. Format and amendments"):
            self.assertIn(part, doc, part)
        self.assertEqual((150, 140), (fd.PILOT_SIZE, fd.PILOT_MIN_SIZE))
        for part in ("AMENDMENT A1", "TITLE-family level", "ceil(n/2)"):
            self.assertIn(part, doc, part)


if __name__ == "__main__":
    unittest.main()

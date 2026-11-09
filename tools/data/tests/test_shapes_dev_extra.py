"""Tests of shapes_dev_extra.py (the compound-head, headings and named-state tranches of the shapes development set) and of its hooks in
shapes_dev.py, on small in-memory fixtures: frames, heading filter and extraction, exact-tag decisions, selection, quota gates, the
model-independence scan and the opacity of the held-out sets.

Run: python -m unittest discover -s tools/data/tests -p "test_shapes_dev_extra.py"   (developer-run; no model, no network)
"""
import collections
import os
import re
import sys
import unittest
sys.path.insert(0, os.path.dirname(os.path.dirname(os.path.abspath(__file__))))
import adjudicate
import shapes_dev as sd
import shapes_dev_extra as sx

NV = frozenset({"NOUN", "VERB"})
LEX = {
    "check": NV, "test": NV, "stand": NV, "split": NV, "reach": NV, "finding": NV, "register": NV, "estimate": NV, "hold": NV, "fluid": NV,
    "oil": NV, "level": NV, "valve": frozenset({"NOUN"}), "engine": frozenset({"NOUN"}), "pressure": frozenset({"NOUN", "VERB"}),
    "brake": NV, "pad": NV, "cover": NV, "switch": NV, "selector": frozenset({"NOUN"}), "position": NV, "mode": frozenset({"NOUN"}),
    "off": frozenset({"ADJ", "ADP", "ADV", "NOUN", "SCONJ", "VERB"}), "standby": frozenset({"ADJ", "NOUN"}), "auto": frozenset({"NOUN"}),
    "closed": frozenset({"ADJ", "VERB"}), "washington": frozenset({"PROPN"}), "apollo": frozenset({"NOUN", "PROPN"}),
    "remove": frozenset({"VERB"}), "inspect": frozenset({"VERB"}), "tank": frozenset({"NOUN"}), "bleed": NV, "wear": NV,
    "removal": frozenset({"NOUN"}), "repair": NV, "program": NV, "set": NV, "turn": NV, "move": NV, "moved": frozenset({"VERB"}),
    "knob": frozenset({"NOUN"}), "the": frozenset({"DET"}), "of": frozenset({"ADP"}), "and": frozenset({"CCONJ"}), "is": frozenset({"AUX"}),
    "to": frozenset({"PART", "ADP"}), "in": frozenset({"ADP"}), "was": frozenset({"AUX"}), "then": frozenset({"ADV"}), "quickly": frozenset({"ADV"}),
    "made": frozenset({"VERB"}), "daily": frozenset({"ADJ", "ADV"}),
}
VOCAB = set(LEX) | {"main", "plan", "section", "operation", "operator", "manual"}


def tokens(text):
    return re.findall(r"[A-Za-z0-9'\-]+|[.,();:]", text)


class CompoundFrame(unittest.TestCase):
    def test_head_after_two_nouns_is_a_hit(self):
        t = tokens("The engine oil check was made daily .")
        self.assertEqual(3, sx.compound_ambiguous_hit(t, LEX))

    def test_complement_of_the_existing_frame(self):
        """A hit of `n-compound-head` at the token is never a hit of the new frame: the two do not overlap by construction."""
        for text in ("They found the oil level and left .", "The brake pad is worn .", "Remove the oil check .", "Engine oil check was made ."):
            t = tokens(text)
            existing = {i for shape, i in sd.shape_hits(t, LEX, compound_strict=True) if shape == "n-compound-head"}
            hit = sx.compound_ambiguous_hit(t, LEX)
            self.assertTrue(hit is None or hit not in existing, text)

    def test_head_at_the_start_of_a_sentence_is_covered(self):
        t = tokens("Engine check was made daily .")
        self.assertEqual(1, sx.compound_ambiguous_hit(t, LEX))

    def test_head_followed_by_a_noun_is_not_the_last_noun(self):
        self.assertIsNone(sx.compound_ambiguous_hit(tokens("The oil check valve was made ."), LEX))

    def test_capitalized_head_or_function_word_is_refused(self):
        self.assertIsNone(sx.compound_ambiguous_hit(tokens("The engine Check was made ."), LEX))
        self.assertIsNone(sx.compound_ambiguous_hit(tokens("The valve will check ."), LEX))

    def test_ing_head_listed_as_a_noun_is_a_candidate(self):
        t = tokens("Engine oil finding was made daily .")
        self.assertEqual(2, sx.compound_ambiguous_hit(t, LEX))


class NamedStateFrame(unittest.TestCase):
    def test_all_caps_state_after_to(self):
        t = tokens("Set the selector switch to OFF .")
        self.assertEqual(5, sx.named_state_hit(t, LEX))

    def test_state_after_preposition_and_determiner(self):
        t = tokens("The knob is moved to the STANDBY .")
        self.assertEqual(6, sx.named_state_hit(t, LEX))

    def test_state_that_modifies_a_noun_is_refused(self):
        self.assertIsNone(sx.named_state_hit(tokens("Turn the knob to the OFF position ."), LEX))

    def test_a_name_and_a_label_are_no_state(self):
        self.assertIsNone(sx.named_state_hit(tokens("Move the knob to Washington ."), LEX))
        self.assertIsNone(sx.named_state_hit(tokens("Set the knob to Table 3 ."), LEX))
        self.assertIsNone(sx.named_state_hit(tokens("Set the knob to III ."), LEX))

    def test_a_cue_word_is_needed(self):
        self.assertIsNone(sx.named_state_hit(tokens("The oil was made to OFF ."), LEX))

    def test_capitalized_ordinary_word_is_a_state(self):
        self.assertEqual(4, sx.named_state_hit(tokens("Move the knob to Closed ."), LEX))

    def test_not_sentence_initial(self):
        self.assertIsNone(sx.named_state_hit(tokens("OFF to ON ."), LEX))

    def test_cue_inflections(self):
        for cue in ("sets", "setting", "turned", "moves", "placed", "returning"):
            self.assertTrue(sx.cue_stem(cue), cue)
        self.assertFalse(sx.cue_stem("water"))


class HeadingFrames(unittest.TestCase):
    def kinds(self, text):
        return sorted((s, i) for s, i, _ in sx.heading_hits(tokens(text), LEX))

    def test_noun_imperative_and_proper(self):
        self.assertEqual([("heading-imperative", 0), ("heading-noun", 2)], self.kinds("Check Fluid Level"))
        self.assertEqual([("heading-imperative", 0), ("heading-noun", 2)], self.kinds("Check the Cover"))
        self.assertEqual([("heading-noun", 2)], self.kinds("Engine Oil Check"))
        self.assertEqual([("heading-proper", 1)], self.kinds("Engine Washington"))

    def test_a_noun_that_a_capitalized_word_continues_is_no_target(self):
        self.assertEqual([("heading-imperative", 0), ("heading-noun", 2)], self.kinds("Brake Pad Check"))     # Check ends the phrase; Pad is followed by a capital
        self.assertNotIn(("heading-noun", 1), self.kinds("Brake Pad Check"))

    def test_proper_noun_needs_a_proper_only_lexicon_entry(self):
        self.assertEqual([], [h for h in self.kinds("Engine Apollo") if h[0] == "heading-proper"])

    def test_imperative_needs_the_base_form(self):
        self.assertEqual([], [h for h in self.kinds("Testing the Valve") if h[0] == "heading-imperative"])


class HeadingFilter(unittest.TestCase):
    def reason(self, text):
        return sx.heading_noise_reason(text, tokens(text), LEX, VOCAB)

    def test_clean_title_case_headings(self):
        for text in ("Check Fluid Level", "Removal of the Brake Pad", "Engine Oil Check and Repair", "Operation Manual"):
            self.assertIsNone(self.reason(text), text)

    def test_rules(self):
        self.assertEqual("length", self.reason("Check"))
        self.assertEqual("start-end", self.reason("Check Fluid Level."))
        self.assertEqual("start-end", self.reason("check Fluid Level"))
        self.assertEqual("chars", self.reason("Check Fluid/Level"))
        self.assertEqual("caps", self.reason("Check FLUID Level"))
        self.assertEqual("case", self.reason("Check fluid level"))
        self.assertEqual("oov", self.reason("Check Fluid Levle"))
        self.assertEqual("brackets", self.reason("Check Fluid (Level"))
        self.assertEqual("repeat", self.reason("Check Check Level"))
        self.assertEqual("start-end", self.reason("Check Fluid Level,"))

    def test_all_caps_heading_is_out_of_scope(self):
        self.assertEqual("caps", self.reason("CHECK FLUID LEVEL"))


class HeadingExtraction(unittest.TestCase):
    RAW = ("Section 2. Engine Oil Check\n\nThe oil is checked daily by the operator when the engine is running at the speed given.\n\n"
           "3.1 Remove the Cover\n\nUnscrew the four bolts that hold the cover in place and lift the cover away.\n\n"
           "Pressure Test .......... 12\n\nRunning Header\n\ntext of page one is a longer paragraph of text for the document.\n\nRunning Header\n\n"
           "more text of page two is a longer paragraph of text for the document.\n\nRunning Header\n\nShort item\nsecond short item\n\n"
           "Check Fluid Level\nthe fluid level is checked with the dipstick before every flight of the aircraft.\n\n")

    def test_isolated_lines_without_labels_and_without_leaders(self):
        found = sx.heading_lines(self.RAW)
        self.assertIn("Engine Oil Check", found)
        self.assertIn("Remove the Cover", found)
        self.assertIn("Check Fluid Level", found)
        self.assertNotIn("Running Header", found)                 # printed on three pages
        self.assertFalse([h for h in found if "....." in h or h.endswith("12")])
        self.assertNotIn("Short item", found)                     # a list: the next line is short

    def test_deterministic(self):
        self.assertEqual(sx.heading_lines(self.RAW), sx.heading_lines(self.RAW))

    def test_label_stripping(self):
        for label, text in (("Step 3. ", "Remove the Cover"), ("Table 2. ", "Pressure Test"), ("2.1 ", "Engine Oil"), ("(a) ", "Engine Oil"),
                            ("Figure 4-1. ", "Engine Oil")):
            self.assertEqual(text, sx.strip_heading_label(label + text))
        self.assertEqual("Part Number Check", sx.strip_heading_label("Part Number Check"))


class ExactTagDecisions(unittest.TestCase):
    def vote(self, tag):
        return adjudicate.vote_of(["x", tag, "high", "-"]) if hasattr(adjudicate, "vote_of") else None

    class V:
        def __init__(self, tag):
            self.tag = tag

    def decide(self, shape, role, a, b):
        return sd.decide_item({"shape": shape, "role": role}, self.V(a), self.V(b))

    def test_heading_noun_needs_noun_exactly(self):
        self.assertEqual(("keep", "", "NOUN"), self.decide("heading-noun", "target", "NOUN", "NOUN"))
        self.assertEqual(("drop", "class-mismatch", "PROPN"), self.decide("heading-noun", "target", "PROPN", "PROPN"))
        self.assertEqual(("drop", "target-disagreement", ""), self.decide("heading-noun", "target", "NOUN", "PROPN"))

    def test_heading_imperative_and_proper_control(self):
        self.assertEqual(("keep", "", "VERB"), self.decide("heading-imperative", "target", "VERB", "VERB"))
        self.assertEqual(("drop", "class-mismatch", "NOUN"), self.decide("heading-imperative", "target", "NOUN", "NOUN"))
        self.assertEqual(("keep", "", "PROPN"), self.decide("heading-proper", "control", "PROPN", "PROPN"))
        self.assertEqual(("drop", "class-mismatch", "NOUN"), self.decide("heading-proper", "control", "NOUN", "NOUN"))

    def test_class_level_shapes_accept_noun_and_propn(self):
        self.assertEqual(("keep", "", "PROPN"), self.decide("n-named-state", "target", "PROPN", "PROPN"))
        self.assertEqual(("keep", "", "NOUN"), self.decide("n-compound-head-ambiguous", "target", "NOUN", "NOUN"))
        self.assertEqual(("keep", "", "VERB"), self.decide("n-compound-head-ambiguous", "control", "VERB", "VERB"))
        self.assertEqual(("drop", "class-mismatch", "VERB"), self.decide("n-named-state", "target", "VERB", "VERB"))

    def test_the_original_shapes_are_unchanged(self):
        self.assertEqual(sd.SHAPES, ("n-compound-head", "n-subject-initial", "v-imperative-initial", "n-measure", "v-after-modal"))
        self.assertEqual("Noun", sd.SHAPE_CLASS["n-compound-head"])


class FrameValidation(unittest.TestCase):
    class Vote:
        def __init__(self, tag):
            self.tag = tag

    def votes(self, tags):
        return {i: self.Vote(t) for i, t in enumerate(tags)}

    def mismatch(self, shape, target, tags, form=None):
        v = self.votes(tags)
        item = {"shape": shape, "role": "target", "target": target}
        return sd.frame_mismatch(item, v, v, form, LEX)

    def test_compound_needs_a_noun_before(self):
        self.assertFalse(self.mismatch("n-compound-head-ambiguous", 2, ["DET", "NOUN", "NOUN"]))
        self.assertTrue(self.mismatch("n-compound-head-ambiguous", 2, ["DET", "ADJ", "NOUN"]))

    def test_compound_ambiguous_does_not_apply_the_gerund_rule(self):
        self.assertFalse(self.mismatch("n-compound-head-ambiguous", 2, ["DET", "NOUN", "NOUN"], "finding"))

    def test_named_state_needs_a_preposition_before(self):
        self.assertFalse(self.mismatch("n-named-state", 3, ["VERB", "DET", "ADP", "NOUN"]))
        self.assertFalse(self.mismatch("n-named-state", 4, ["VERB", "DET", "NOUN", "ADP", "DET", "NOUN"][:3] + ["ADP", "DET", "NOUN"][:0] + ["ADP"]))
        self.assertTrue(self.mismatch("n-named-state", 2, ["VERB", "DET", "NOUN"]))

    def test_heading_imperative_is_initial_and_base_form(self):
        self.assertFalse(self.mismatch("heading-imperative", 0, ["VERB", "NOUN"], "Check"))
        self.assertTrue(self.mismatch("heading-imperative", 1, ["NOUN", "VERB"], "Check"))
        self.assertTrue(self.mismatch("heading-imperative", 0, ["VERB", "NOUN"], "Checking"))

    def test_heading_noun_is_never_initial(self):
        self.assertFalse(self.mismatch("heading-noun", 2, ["VERB", "NOUN", "NOUN"]))
        self.assertTrue(self.mismatch("heading-noun", 0, ["NOUN", "NOUN"]))


def item(cand, shape, role, lemma, source, q=None, tag="NOUN", target=1, outcome="keep"):
    row = {"cand_id": cand, "sent_key": "k" + cand, "shape": shape, "role": role, "target": target, "lemma": lemma, "origin": "real",
           "source_id": source, "outcome": outcome, "reason": "", "opus_tag": tag, "sol_tag": tag, "agreed_tag": tag, "disputed_tokens": 0}
    if q is not None:
        row["q"] = q
    return row


def candidates_of(items, form="check"):
    return {i["cand_id"]: {"cand_id": i["cand_id"], "text": "x y", "tokens": ["x", form]} for i in items}


class Selection(unittest.TestCase):
    def test_compound_order_lemma_cap_and_ing_share(self):
        items = [item("c%03d" % n, "n-compound-head-ambiguous", "target", "w%d" % (n // 5), "d", q="%.3f" % (n / 1000)) for n in range(1, 200)]
        cands = candidates_of(items)
        for n, i in enumerate(items):
            cands[i["cand_id"]]["tokens"] = ["x", "word%d" % n + ("ing" if n % 2 == 0 else "")]
        targets, controls, drops = sx.select("compound-head", items, cands)
        self.assertEqual(100, len(targets))
        self.assertEqual(sorted(t["q"] for t in targets), [t["q"] for t in targets])           # hardest first
        self.assertLessEqual(max(collections.Counter(t["lemma"] for t in targets).values()), 4)
        self.assertLessEqual(sum(1 for t in targets if cands[t["cand_id"]]["tokens"][1].endswith("ing")), 25)
        self.assertGreater(drops["ing-share"], 0)

    def test_controls_need_a_chosen_lemma_and_two_per_lemma(self):
        targets = [item("t1", "n-named-state", "target", "off", "d")]
        controls = [item("c%d" % n, "n-named-state", "control", "off" if n < 4 else "zzz", "d", tag="VERB") for n in range(8)]
        chosen, ctl, _ = sx.select("named-state", targets + controls, candidates_of(targets + controls))
        self.assertEqual(2, len(ctl))
        self.assertEqual({"off"}, {c["lemma"] for c in ctl})

    def test_headings_document_cap_and_kinds(self):
        items = [item("n%03d" % n, "heading-noun", "target", "l%d" % n, "docA" if n < 30 else "docB") for n in range(60)]
        items += [item("i%03d" % n, "heading-imperative", "target", "m%d" % n, "docC", tag="VERB", target=0) for n in range(20)]
        items += [item("p%03d" % n, "heading-proper", "control", "q%d" % (n // 2), "docD", tag="PROPN") for n in range(30)]
        targets, controls, _ = sx.select("headings", items, candidates_of(items))
        by_doc = collections.Counter(t["source_id"] for t in targets)
        self.assertLessEqual(max(by_doc.values()), sx.HEADING_DOCUMENT_CAP)
        self.assertEqual(sx.HEADING_DOCUMENT_CAP, by_doc["docC"])
        self.assertEqual(20, len(controls))

    def test_dropped_items_are_never_taken(self):
        items = [item("c1", "n-named-state", "target", "off", "d", outcome="drop")]
        targets, _, _ = sx.select("named-state", items, candidates_of(items))
        self.assertEqual([], targets)


class QuotaGate(unittest.TestCase):
    def members(self, count, shape="n-named-state", role="target", prefix="t"):
        return [item("%s%d" % (prefix, n), shape, role, "lemma%d" % (n // 2), "d") for n in range(count)]

    def test_floor_and_cap(self):
        controls = self.members(12, role="control", prefix="c")
        self.assertEqual([], sx.quota_problems("named-state", self.members(40), controls))
        self.assertTrue(any("floor" in p for p in sx.quota_problems("named-state", self.members(39), controls)))
        self.assertTrue(any("cap" in p for p in sx.quota_problems("named-state", self.members(101), controls)))
        self.assertTrue(any("controls" in p for p in sx.quota_problems("named-state", self.members(40), controls[:11])))

    def test_heading_strata_are_checked_separately(self):
        nouns = [item("n%d" % n, "heading-noun", "target", "a%d" % n, "d") for n in range(50)]
        imps = [item("i%d" % n, "heading-imperative", "target", "b%d" % n, "d", tag="VERB") for n in range(39)]
        controls = [item("p%d" % n, "heading-proper", "control", "p%d" % n, "d", tag="PROPN") for n in range(15)]
        problems = sx.quota_problems("headings", nouns + imps, controls)
        self.assertEqual(1, len(problems))
        self.assertIn("imperative", problems[0])

    def test_lemma_cap(self):
        many = [item("t%d" % n, "n-named-state", "target", "same", "d") for n in range(70)]
        self.assertTrue(any("lemma same" in p for p in sx.quota_problems("named-state", many, self.members(12, role="control", prefix="c"))))


class BuiltSentences(unittest.TestCase):
    def sentence(self, tranche, tag, kind=None, role="target"):
        extra = ["# shape = " + sx.SHAPE_OF[tranche], "# role = " + role, "# target = 0", "# lemma = x", "# origin = real"]
        if kind:
            extra.append("# kind = " + kind)
        return {"sent_id": "shapes-dev-%s-0001" % tranche, "source_id": "d", "text": "Check Level",
                "rows": [["Check", tag, "unanimous"], ["Level", "NOUN", "unanimous"]], "extra": extra}

    def test_heading_gold_tag_must_match_the_kind(self):
        self.assertEqual([], sx.sentence_problems("headings", self.sentence("headings", "VERB", "imperative")))
        self.assertTrue(sx.sentence_problems("headings", self.sentence("headings", "NOUN", "imperative")))
        self.assertTrue(sx.sentence_problems("headings", self.sentence("headings", "NOUN", "proper", "control")))
        self.assertEqual([], sx.sentence_problems("headings", self.sentence("headings", "PROPN", "proper", "control")))

    def test_class_shapes(self):
        self.assertEqual([], sx.sentence_problems("named-state", self.sentence("named-state", "PROPN")))
        self.assertTrue(sx.sentence_problems("named-state", self.sentence("named-state", "VERB")))
        self.assertEqual([], sx.sentence_problems("compound-head", self.sentence("compound-head", "VERB", role="control")))

    def test_header_marks_the_tranche_and_the_dev_split(self):
        for tranche in sx.TRANCHES:
            lines = sx.header_lines(tranche)
            self.assertIn("%s tranche" % tranche, lines[0])
            self.assertIn("dev split", lines[0])


class SampleAndReview(unittest.TestCase):
    def test_sample_plan_and_swapped_controls(self):
        selected = []
        for shape, tranche in (("n-compound-head-ambiguous", "compound-head"), ("heading-noun", "headings"), ("heading-imperative", "headings"),
                               ("n-named-state", "named-state")):
            for n in range(30):
                tag = "VERB" if shape == "heading-imperative" else "NOUN"
                selected.append((tranche, item("%s-%d" % (shape, n), shape, "target", "lem", "d", tag=tag), {"tokens": ["a", "b"]}))
        sample = sx.build_sample(selected)
        real = [s for s in sample if s[0] == "real"]
        self.assertEqual(60, len(real))
        self.assertEqual(4, len(sample) - len(real))
        counts = collections.Counter(s[2]["shape"] for s in real)
        self.assertEqual({"n-compound-head-ambiguous": 20, "heading-noun": 10, "heading-imperative": 10, "n-named-state": 20}, dict(counts))
        for kind, _, entry, _, shown in sample:
            if kind == "control":
                self.assertNotEqual(shown, entry["agreed_tag"])
        self.assertEqual(sample, sx.build_sample(selected))                                 # a fixed seed

    def test_review_gate(self):
        keys = [{"item": "R%03d" % n, "kind": "real", "shape": "n-named-state"} for n in range(60)]
        keys += [{"item": "R%03d" % (60 + n), "kind": "control", "shape": "n-named-state"} for n in range(4)]

        class Verdict:
            def __init__(self, item, verdict):
                self.item, self.verdict = item, verdict

        def verdicts(wrong):
            out = [Verdict(k["item"], "incorrect" if n < wrong else "correct") for n, k in enumerate(keys[:60])]
            return out + [Verdict(k["item"], "incorrect") for k in keys[60:]]

        for wrong, passed in ((3, True), (4, False)):
            per_shape, controls = sx.review_numbers(keys, verdicts(wrong))
            self.assertEqual(passed, sx.review_result_lines(per_shape, controls)[1], wrong)


class Independence(unittest.TestCase):
    """The selection uses no PartOfSpeech model and the held-out sets only through their opaque keys."""

    def source(self):
        with open(os.path.join(os.path.dirname(os.path.dirname(os.path.abspath(__file__))), "shapes_dev_extra.py"), encoding="utf-8") as handle:
            return handle.read()

    def test_no_model_in_the_code(self):
        code = "\n".join(line for line in self.source().splitlines() if not line.lstrip().startswith("#"))
        body = code.split('"""', 2)[2]
        for forbidden in ("weights.bin", "PerceptronModel", "FeatureExtractor", "PartOfSpeechTagger", "eval-shapes", "decoder.", ".dll"):
            self.assertNotIn(forbidden, body, forbidden)

    def test_held_out_sets_are_never_read_directly(self):
        source = self.source()
        for forbidden in ("probe.tsv", "gold-v2", "ste-challenge", "held_out_overlap_keys("):
            self.assertNotIn(forbidden, source, forbidden)

    def test_tranches_and_shapes_are_registered(self):
        self.assertEqual({"n-compound-head-ambiguous", "n-named-state", "heading-noun", "heading-imperative", "heading-proper"},
                         set(sd.EXTRA_SHAPE_CLASS))
        self.assertEqual({"heading-noun": "NOUN", "heading-imperative": "VERB", "heading-proper": "PROPN"}, sd.EXACT_TAGS)


if __name__ == "__main__":
    unittest.main()

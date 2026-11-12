"""Tests of technical_frames.py: the position and lexicon frames of the shapes S1 to S4 of the beta.3 data work, on a small fixture lexicon.

Run: python -m unittest discover -s tools/data/tests -p "test_technical_frames.py"   (developer-run; the frames need no model, data folder or network)
"""
import ast
import os
import re
import sys
import unittest

sys.path.insert(0, os.path.dirname(os.path.dirname(os.path.abspath(__file__))))
import technical_frames as tf

WORDS = {
    # function words
    "the": "DET", "a": "DET", "an": "DET", "this": "DET", "these": "DET", "it": "PRON", "they": "PRON", "is": "AUX NOUN", "are": "AUX", "was": "AUX",
    "will": "AUX NOUN", "to": "PART ADP", "and": "CCONJ", "or": "CCONJ", "when": "SCONJ", "if": "SCONJ", "that": "SCONJ DET", "of": "ADP", "for": "ADP",
    "by": "ADP", "before": "ADP SCONJ", "after": "ADP SCONJ", "in": "ADP", "not": "PART", "two": "NUM NOUN", "three": "NUM", "there": "PRON ADV",
    "off": "ADP ADV VERB", "up": "ADV ADP", "only": "ADV ADJ", "with": "ADP", "while": "SCONJ", "because": "SCONJ", "keep": "VERB NOUN",
    # nouns and noun-verbs
    "range": "NOUN VERB", "check": "NOUN VERB", "checks": "NOUN VERB", "data": "NOUN", "dump": "NOUN VERB", "dumps": "NOUN VERB",
    "leak": "NOUN VERB", "leaks": "NOUN VERB", "testing": "NOUN VERB ADJ", "test": "NOUN VERB", "results": "NOUN VERB", "pressure": "NOUN",
    "valve": "NOUN", "valves": "NOUN", "pump": "NOUN VERB", "pumps": "NOUN VERB", "oil": "NOUN", "level": "NOUN ADJ VERB", "open": "ADJ NOUN VERB",
    "control": "NOUN VERB", "panel": "NOUN", "flow": "NOUN VERB", "drops": "NOUN VERB", "technician": "NOUN", "shop": "NOUN VERB",
    "complete": "ADJ VERB", "red": "ADJ", "new": "ADJ", "manual": "ADJ NOUN", "override": "NOUN VERB", "mode": "NOUN", "wiring": "NOUN ADJ VERB",
    "schematic": "ADJ NOUN", "standby": "ADJ NOUN", "position": "NOUN VERB", "normal": "ADJ NOUN", "operating": "ADJ NOUN VERB", "operate": "VERB",
    "hydraulic": "ADJ", "parts": "NOUN", "list": "NOUN VERB", "defense": "NOUN VERB", "logistics": "NOUN", "agency": "NOUN",
    "washington": "PROPN", "apollo": "PROPN", "park": "NOUN VERB", "select": "VERB ADJ", "see": "VERB", "set": "NOUN VERB ADJ", "switch": "NOUN VERB",
    "press": "NOUN VERB", "table": "NOUN VERB", "program": "NOUN VERB PROPN", "turn": "NOUN VERB", "fill": "NOUN VERB", "cap": "NOUN VERB",
    "remove": "VERB", "cover": "NOUN VERB", "stop": "NOUN VERB", "work": "NOUN VERB", "drive": "NOUN VERB", "gas": "NOUN VERB", "daily": "ADJ ADV",
    "required": "ADJ VERB", "march": "NOUN VERB PROPN", "general": "ADJ NOUN", "start": "NOUN VERB", "close": "VERB ADJ", "second": "ADJ NOUN",
    "quibble": "NOUN VERB", "flight": "NOUN", "pilot": "NOUN VERB", "very": "ADV", "long": "ADJ VERB", "assembly": "NOUN",
}
LEX = {word: frozenset(tags.split()) for word, tags in WORDS.items()}
VOCAB = set(LEX) | set("proposed action hello 3 an tighten bolt".split())


def tok(text):
    """The tokens of a fixture sentence: words (with inner hyphens and apostrophes), numbers and single marks."""
    return re.findall(r"[\w'-]+|[^\w\s]", text)


class DeverbalHeadTests(unittest.TestCase):
    """S1: strata pred, num, ing and other, plus the verb controls."""

    def hit(self, text):
        return tf.deverbal_head_hit(tok(text), LEX)

    def test_predicate_position_with_a_determiner(self):
        # Arrange
        sentences = ("This is the range check .", "It will remain a data dump .", "That was not the leak test .")
        # Act and assert
        for text in sentences:
            found = self.hit(text)
            self.assertEqual("pred", found.stratum, text)
        self.assertEqual("check", self.hit("This is the range check .").lemma)

    def test_predicate_position_of_a_plural_or_an_ing_head_needs_no_determiner(self):
        # Arrange and act
        plural = self.hit("These are range checks .")
        gerund = self.hit("This is leak testing .")
        # Assert
        self.assertEqual(("pred", "plural"), (plural.stratum, plural.kind))
        self.assertEqual(("pred", "ing"), (gerund.stratum, gerund.kind))

    def test_a_singular_zero_head_without_a_determiner_is_no_predicate(self):
        # Arrange and act and assert
        self.assertIsNone(self.hit("This is range check ."))

    def test_after_a_numeral_the_stratum_is_num(self):
        # Arrange
        sentences = ("The shop completed two range checks .", "The shop completed 3 data dumps .", "The shop completed the three range checks .")
        # Act and assert
        for text in sentences:
            self.assertEqual("num", self.hit(text).stratum, text)

    def test_there_is_a_numeral_phrase_is_no_predicate_and_one_is_no_numeral(self):
        # Arrange and act
        existential = self.hit("There are two range checks .")
        one = self.hit("The shop completed one range check .")
        # Assert
        self.assertEqual("num", existential.stratum)
        self.assertEqual("other", one.stratum)

    def test_a_label_before_the_number_is_no_numeral(self):
        # Arrange and act and assert
        self.assertIsNone(self.hit("The shop completed Table 3 data dumps ."))

    def test_gerund_heads_in_any_other_position(self):
        # Arrange
        sentences = ("The leak testing is complete .", "Pressure testing is complete .", "The shop did leak testing .", "The shop stopped by pressure testing of the valve .")
        # Act and assert
        for text in sentences:
            found = self.hit(text)
            self.assertEqual(("ing", "ing"), (found.stratum, found.kind), text)

    def test_a_zero_head_after_a_determiner_is_the_other_stratum(self):
        # Arrange and act
        found = self.hit("The shop did the data dump .")
        subject = self.hit("The data dump is complete .")
        # Assert
        self.assertEqual(("other", "zero", 5), (found.stratum, found.kind, found.index))
        self.assertEqual("other", subject.stratum)

    def test_the_rarest_stratum_of_the_sentence_wins(self):
        # Arrange: an other head first and a predicate head later
        text = "The data dump is the range check ."
        # Act
        found = self.hit(text)
        # Assert
        self.assertEqual(("pred", 6), (found.stratum, found.index))

    def test_the_head_must_end_its_compound(self):
        # Arrange and act and assert: a noun-capable word after the head makes it a modifier; a determiner or pronoun after it makes it a verb
        self.assertIsNone(self.hit("The range check valve is red ."))
        self.assertIsNone(self.hit("The shop did the range check the pressure ."))
        self.assertIsNone(self.hit("By pressure testing the valve the shop stopped ."))

    def test_a_verb_use_after_a_modal_or_without_a_determiner_is_no_target(self):
        # Arrange and act and assert
        self.assertIsNone(self.hit("The pump will range check the pressure ."))
        self.assertIsNone(self.hit("The shop will data dump ."))

    def test_the_modifier_must_be_a_noun_and_the_head_a_listed_noun_verb(self):
        # Arrange and act and assert: `red` is an adjective, `technician` is no verb, `required` ends in -ed
        self.assertIsNone(self.hit("The shop did the red check ."))
        self.assertIsNone(self.hit("The shop did the range technician ."))
        self.assertIsNone(self.hit("The shop did the range required ."))

    def test_a_plural_head_is_only_allowed_after_a_numeral_or_as_a_predicate(self):
        # Arrange and act and assert
        self.assertIsNone(self.hit("The shop did the range checks ."))

    def test_controls_are_the_verb_frames_of_the_lemmas(self):
        # Arrange
        modal = "The pump will check the valve ."
        imperative = "Check the valve ."
        ing = "The shop started by testing the valve ."
        # Act
        frames = {text: tf.verb_control_hits(tok(text), LEX) for text in (modal, imperative, ing)}
        # Assert
        self.assertEqual([("modal", "check")], [(c.frame, c.lemma) for c in frames[modal] if c.lemma == "check"])
        self.assertEqual([("imperative", 0)], [(c.frame, c.index) for c in frames[imperative]])
        self.assertEqual([("ing", "testing")], [(c.frame, c.lemma) for c in frames[ing]])

    def test_controls_respect_the_lemma_set_and_need_the_frame(self):
        # Arrange and act and assert
        self.assertEqual([], tf.verb_control_hits(tok("The pump will check the valve ."), LEX, lemmas={"dump"}))
        self.assertEqual([], tf.verb_control_hits(tok("The check is complete ."), LEX))
        self.assertEqual([], tf.verb_control_hits(tok("The shop started by testing valves ."), LEX))
        self.assertEqual([], tf.verb_control_hits(tok("The shop started the pressure testing of the valve ."), LEX))


class DefinedTermTests(unittest.TestCase):
    """S2: runs, tiers and the strata noun, adj and proper."""

    def hits(self, text):
        return tf.defined_term_hits(tok(text), LEX)

    def test_a_defined_term_with_a_right_cue_has_a_noun_and_an_adjective_hit(self):
        # Arrange and act
        found = self.hits("Select the Manual Override mode .")
        # Assert
        self.assertEqual([("adj", "T1", 2), ("noun", "T1", 3)], [(h.stratum, h.tier, h.judged) for h in found])
        self.assertEqual("manual override", found[0].term)

    def test_a_document_type_head_is_tier_two(self):
        # Arrange and act
        found = self.hits("The shop kept the Wiring Schematic .")
        # Assert
        self.assertEqual([("noun", "T2", 5)], [(h.stratum, h.tier, h.judged) for h in found])

    def test_a_left_verb_cue_is_tier_three_with_an_optional_determiner(self):
        # Arrange and act
        with_determiner = self.hits("Open the Parts List .")
        without = self.hits("Then open Parts List .")
        # Assert
        self.assertEqual([("noun", "T3")], [(h.stratum, h.tier) for h in with_determiner])
        self.assertEqual([("noun", "T3")], [(h.stratum, h.tier) for h in without])

    def test_a_single_word_term_with_a_right_cue(self):
        # Arrange and act
        found = self.hits("Set the switch to the Standby position .")
        # Assert
        self.assertEqual([("noun", "T1", 5)], [(h.stratum, h.tier, h.judged) for h in found])

    def test_any_other_run_of_ordinary_words_is_tier_four_and_needs_two_words(self):
        # Arrange and act
        multi = self.hits("The pilot reviewed the Normal Operating Range yesterday .")
        single = self.hits("The pilot reviewed the Standby yesterday .")
        # Assert
        self.assertEqual([("adj", "T4", 4), ("noun", "T4", 6)], [(h.stratum, h.tier, h.judged) for h in multi])
        self.assertEqual([], single)

    def test_an_organization_or_a_name_is_the_proper_control(self):
        # Arrange and act
        organization = self.hits("The shop called the Defense Logistics Agency today .")
        place = self.hits("The pilot flew to Washington Park .")
        single = self.hits("The pilot flew to Washington .")
        # Assert
        self.assertEqual([("proper", 6)], [(h.stratum, h.judged) for h in organization])
        self.assertEqual([("proper", 5)], [(h.stratum, h.judged) for h in place])
        self.assertEqual([], single)

    def test_a_single_name_with_a_right_cue_is_a_proper_control(self):
        # Arrange and act
        found = self.hits("The shop kept the Apollo mode .")
        # Assert
        self.assertEqual([("proper", "T1")], [(h.stratum, h.tier) for h in found])

    def test_a_capital_at_the_start_or_after_a_colon_or_quote_is_never_a_signal(self):
        # Arrange
        sentences = ("Manual Override mode is active .", "Note : Standby position is set .", 'The shop said " Standby position is set " .')
        # Act and assert
        for text in sentences:
            self.assertEqual([], self.hits(text), text)

    def test_a_label_a_title_case_sentence_a_long_run_or_all_capitals_has_no_hit(self):
        # Arrange
        sentences = ("The shop read Table 3 .", "Select The Manual Override mode .", "The shop kept the Very Long Hydraulic Pump Assembly mode .",
                     "Set the switch to STANDBY position .")
        # Act and assert
        for text in sentences:
            self.assertEqual([], self.hits(text), text)

    def test_a_coined_label_or_a_title_or_a_month_is_not_an_ordinary_word(self):
        # Arrange
        sentences = ("Select the Zorbix mode .", "The shop kept the General mode .", "The shop kept the March mode .")
        # Act and assert
        for text in sentences:
            self.assertEqual([], [h for h in self.hits(text) if h.stratum != "proper"], text)

    def test_the_runs_do_not_start_the_sentence(self):
        # Arrange and act
        runs = tf.title_runs(tok("Select the Manual Override mode ."))
        # Assert
        self.assertEqual([(2, 4)], runs)

    def test_organization_like_runs(self):
        # Arrange and act and assert
        self.assertTrue(tf.organization_like(["Defense", "Logistics", "Agency"]))
        self.assertFalse(tf.organization_like(["Manual", "Override"]))
        self.assertFalse(tf.organization_like([]))


class StepLineTests(unittest.TestCase):
    """S3: the target frame, the caption control, the step lines and the noise filter."""

    def line(self, text, labeled=False):
        return tf.step_line_hit(tok(text), LEX, labeled)

    def test_every_frame_of_token_one(self):
        # Arrange
        cases = {"det": "Check the valve .", "pron": "Check it .", "num": "Check 3 valves .", "prep": "Check for leaks .", "particle": "Turn off the pump .",
                 "adv": "Check only the valve .", "bare": "Check oil level ."}
        # Act and assert
        for frame, text in cases.items():
            found = self.line(text)
            self.assertEqual((0, frame), (found.index, found.frame), text)
        self.assertEqual(set(cases), set(tf.STEP_FRAMES) - {"end"})

    def test_the_end_frame_is_a_labeled_single_word_or_a_word_and_a_period(self):
        # Arrange and act and assert
        self.assertEqual("end", self.line("Stop .").frame)
        self.assertEqual("end", tf.step_line_hit(["Stop"], LEX, True).frame)
        self.assertIsNone(tf.step_line_hit(["Stop"], LEX, False))

    def test_a_bare_object_needs_an_imperative_context(self):
        # Arrange and act and assert
        self.assertIsNone(self.line("Check oil level"))
        self.assertEqual("bare", self.line("Check oil level", labeled=True).frame)
        self.assertEqual("bare", self.line("Open oil level 3 .").frame)

    def test_a_bare_object_followed_by_an_auxiliary_or_a_verb_is_a_clause_not_an_instruction(self):
        # Arrange and act and assert
        self.assertIsNone(self.line("Check valves are required ."))
        self.assertIsNone(self.line("Check oil level is required .", labeled=True))
        self.assertIsNone(self.line("Check valves remove ."))

    def test_a_target_needs_an_ambiguous_base_form_first_word(self):
        # Arrange and act and assert: `remove` is only a verb, `valve` only a noun, `Checks` is no base form, `the` is a function word
        self.assertIsNone(self.line("Remove the cover ."))
        self.assertIsNone(self.line("Valve the pump ."))
        self.assertIsNone(self.line("Checks the valve ."))
        self.assertIsNone(self.line("The valve ."))

    def test_a_capitalized_second_word_a_subject_pronoun_or_an_auxiliary_is_no_instruction(self):
        # Arrange and act and assert
        self.assertIsNone(self.line("Check Valve ."))
        self.assertIsNone(self.line("Check is required ."))
        self.assertIsNone(self.line("Check I think ."))

    def test_the_caption_control(self):
        # Arrange and act and assert
        for text in ("Check valve", "Control Panel", "Test results"):
            self.assertEqual(0, tf.step_caption_hit(tok(text), LEX), text)

    def test_the_caption_control_has_no_label_period_verb_object_or_long_line(self):
        # Arrange and act and assert
        self.assertIsNone(tf.step_caption_hit(tok("Check the valve ."), LEX))
        self.assertIsNone(tf.step_caption_hit(tok("Check valve ."), LEX))
        self.assertIsNone(tf.step_caption_hit(tok("Check valve"), LEX, labeled=True))
        self.assertIsNone(tf.step_caption_hit(tok("Check valve pressure data panel level list"), LEX))
        self.assertIsNone(tf.step_caption_hit(tok("Remove cover"), LEX))
        self.assertIsNone(tf.step_caption_hit(tok("Check"), LEX))

    RAW = """Figure 4

1. Check the oil level and
   fill as needed. Then close the cap.
2) Open valve 3
(3) Remove the cover
a. Turn off the pump
Step 4: Press the switch
Open valve 5
- Press the switch.
Control Panel

Wiring Diagram
Header text here
Header text here
Header text here
This line is part of a long wrapped paragraph that goes on and on and on
Press it
and then the paragraph continues here.
"""

    def test_step_lines_remove_every_label_and_join_the_wrapped_lines(self):
        # Arrange and act
        found = tf.step_lines(self.RAW)
        texts = dict(found)
        # Assert
        self.assertEqual("enumerated", texts["Check the oil level and fill as needed."])
        for text in ("Open valve 3", "Remove the cover", "Turn off the pump", "Press the switch"):
            self.assertEqual("enumerated", texts[text], text)

    def test_the_wrapped_continuation_is_cut_at_the_first_sentence_end(self):
        # Arrange and act
        texts = [text for text, _ in tf.step_lines(self.RAW)]
        # Assert
        self.assertNotIn("Check the oil level and fill as needed. Then close the cap.", texts)
        self.assertFalse(any("Then close" in text for text in texts))

    def test_bulleted_and_short_stand_alone_lines(self):
        # Arrange and act
        found = tf.step_lines(self.RAW)
        # Assert
        self.assertIn(("Open valve 5", "short"), found)
        self.assertIn(("Press the switch.", "bulleted"), found)
        self.assertIn(("Control Panel", "short"), found)
        self.assertIn(("Wiring Diagram", "short"), found)

    def test_a_repeated_header_and_a_wrapped_paragraph_give_no_unit(self):
        # Arrange and act
        texts = [text for text, _ in tf.step_lines(self.RAW)]
        # Assert
        self.assertFalse(any(text.startswith("Header") for text in texts))
        self.assertNotIn("Press it", texts)           # followed by a lower-case line: the wrapped tail of a paragraph

    def test_a_capital_line_after_an_enumerated_line_is_not_a_continuation(self):
        # Arrange
        raw = "1. Open valve 3\nRemove the cover\n"
        # Act
        found = tf.step_lines(raw)
        # Assert
        self.assertEqual([("Open valve 3", "enumerated"), ("Remove the cover", "short")], found)

    def test_a_line_with_a_tab_or_dot_leaders_gives_no_unit(self):
        # Arrange and act and assert
        self.assertEqual([], tf.step_lines("Check the valve\t12\nOpen valve 3 .... 12\n"))

    def noise(self, text, kind="short"):
        return tf.step_noise_reason(text, tok(text), LEX, VOCAB, kind)

    def test_a_clean_step_is_accepted_with_sentence_case_title_case_and_a_trailing_number(self):
        # Arrange and act and assert
        self.assertIsNone(self.noise("Check the oil level.", "enumerated"))
        self.assertIsNone(self.noise("Control Panel"))
        self.assertIsNone(self.noise("Open valve 3.", "enumerated"))
        self.assertIsNone(self.noise("Open valve 3", "enumerated"))

    def test_every_noise_rule(self):
        # Arrange
        cases = {"start-end": ["check the oil level.", "Check the oil level,", "Check the oil level for"], "chars": ["Check the oil @ level."],
                 "page-number": ["Proposed Action 12", "Proposed Action 4-8"], "junk": ["Check the oil ,, level."], "brackets": ["Check (the oil level."],
                 "enumeration": ["Check the oil (a) level.", "Check M. Smith level."], "repeat": ["Check the the oil level."],
                 "caps": ["PRESS the oil level.", "PRESS THE OIL."], "oov": ["Check the xyzzy level."], "length": ["Check"]}
        # Act and assert
        for rule, texts in cases.items():
            for text in texts:
                self.assertEqual(rule, self.noise(text), text)

    def test_a_trailing_number_is_a_page_number_only_for_a_short_stand_alone_line(self):
        # Arrange and act and assert
        self.assertEqual("page-number", self.noise("Proposed Action 12", "short"))
        self.assertIsNone(self.noise("Proposed Action 12", "bulleted"))
        self.assertEqual("page-number", self.noise("Proposed Action 4-8", "bulleted"))

    def test_a_short_line_that_ends_in_a_number_and_a_period_is_a_step(self):
        # Arrange and act and assert
        for text in ("Open valve 3.", "Tighten bolt 4."):
            self.assertIsNone(self.noise(text, "short"), text)

    def test_a_short_line_that_ends_in_a_number_without_a_period_stays_a_page_number(self):
        # Arrange and act and assert
        for text in ("Open valve 3", "Tighten bolt 4", "Control Panel 12"):
            self.assertEqual("page-number", self.noise(text, "short"), text)

    def test_a_page_range_with_a_period_is_still_a_page_number(self):
        # Arrange and act and assert
        self.assertEqual("page-number", self.noise("Proposed Action 4-8.", "short"))
        self.assertEqual("page-number", self.noise("Proposed Action 4-8", "short"))


class ClauseVerbTests(unittest.TestCase):
    """S4: strata main and sub, the coordinated subject and the noun and adjective controls."""

    def hit(self, text):
        return tf.clause_verb_hit(tok(text), LEX)

    def control(self, text):
        return tf.clause_control_hit(tok(text), LEX)

    def test_a_plural_subject_and_a_base_form_verb_in_the_main_clause(self):
        # Arrange and act
        found = self.hit("The pumps control the flow .")
        # Assert
        self.assertEqual((2, "main", False, "control"), tuple(found))

    def test_a_coordinated_subject_is_flagged(self):
        # Arrange and act
        found = self.hit("The pump and valve open when the pressure drops .")
        with_determiner = self.hit("The pump and the valve open when the pressure drops .")
        # Assert
        self.assertEqual(("main", True), (found.stratum, found.coordinated))
        self.assertEqual(("main", True), (with_determiner.stratum, with_determiner.coordinated))

    def test_a_subordinate_clause_after_its_subordinator(self):
        # Arrange
        sentences = ("When the valves open the pressure drops .", "The shop checks that the pumps control the flow .", "The shop stops if pumps and valves open .")
        # Act and assert
        for text in sentences:
            self.assertEqual("sub", self.hit(text).stratum, text)

    def test_a_subordinator_across_a_mark_belongs_to_the_earlier_clause(self):
        # Arrange and act
        found = self.hit("When the shop starts , valves open when the pressure drops .")
        # Assert
        self.assertEqual("main", found.stratum)

    def test_the_verb_may_end_the_clause_or_precede_a_preposition_a_quantity_or_a_conjunction(self):
        # Arrange
        sentences = ("The valves open .", "The valves open in the shop .", "The valves open 3 times .", "The valves open and the pressure drops .",
                     "The valves open to the shop .", "The valves control it .", "The valves open not .")
        # Act and assert
        for text in sentences:
            self.assertIsNotNone(self.hit(text), text)

    def test_a_verb_before_its_own_clause_or_a_noun_after_the_word_is_no_target(self):
        # Arrange and act and assert: a verb before the plural noun phrase (a small clause), a noun after the word (a compound), a singular subject
        self.assertIsNone(self.hit("The shop keeps the valves open ."))
        self.assertIsNone(self.hit("The valves control panel is red ."))
        self.assertIsNone(self.hit("The valve control the flow ."))
        self.assertIsNone(self.hit("The gas control the flow ."))
        self.assertIsNone(self.hit("The pumps controls the flow ."))

    def test_a_preposition_before_the_subject_is_no_clause(self):
        # Arrange and act and assert
        self.assertIsNone(self.hit("The shop works in the pumps control ."))

    def test_the_noun_control(self):
        # Arrange and act
        found = self.control("The valves control panel is red .")
        # Assert
        self.assertEqual((2, "noun", False, "control"), tuple(found))

    def test_the_adjective_control(self):
        # Arrange and act
        found = self.control("The shop did the open valves .")
        # Assert
        self.assertEqual((4, "adj", False, "open"), tuple(found))

    def test_a_target_sentence_has_no_control_hit(self):
        # Arrange and act and assert
        self.assertIsNone(self.control("The pumps control the flow ."))
        self.assertIsNone(self.control("The shop did the new valves ."))


class SharedHelperTests(unittest.TestCase):
    def test_plural_stem(self):
        # Arrange and act and assert
        self.assertEqual("valve", tf.plural_stem("valves", LEX))
        self.assertIsNone(tf.plural_stem("gas", LEX))
        self.assertIsNone(tf.plural_stem("valve", LEX))
        self.assertIsNone(tf.plural_stem("is", LEX))

    def test_the_word_lists_are_the_documented_constants(self):
        # Arrange and act and assert
        self.assertEqual(4, tf.MAX_RUN)
        self.assertIn("mode", tf.RIGHT_CUES)
        self.assertIn("Schematic", tf.DOCUMENT_HEADS)
        self.assertIn("see", tf.LEFT_CUES)
        self.assertIn("Agency", tf.ORGANIZATION_HEADS)
        self.assertIn("when", tf.SUBORDINATORS)
        self.assertEqual(("proper", "adj", "noun"), tf.TERM_STRATA)
        self.assertEqual(("pred", "num", "ing", "other"), tf.DEVERBAL_STRATA)

    def test_the_docstring_describes_every_frame(self):
        # Arrange
        doc = " ".join(tf.__doc__.split())
        # Act and assert
        for part in ("S1 deverbal compound heads", "S2 capitalized defined terms", "S3 capitalized imperatives at line start",
                     "S4 base-form ambiguity in a clause", "NORMATIVE TEXT", "No PartOfSpeech model"):
            self.assertIn(part, doc, part)


class NoModelTests(unittest.TestCase):
    """The frames decide nothing with a PartOfSpeech model: no tagger, weights, decoder, prediction or score is read, and nothing is read from a file."""

    def parsed(self):
        with open(tf.__file__, encoding="utf-8") as handle:
            return ast.parse(handle.read())

    def test_only_the_standard_library_and_the_word_helpers_are_imported(self):
        # Arrange
        imported = set()
        for node in ast.walk(self.parsed()):
            if isinstance(node, ast.Import):
                imported |= {a.name.split(".")[0] for a in node.names}
            elif isinstance(node, ast.ImportFrom):
                imported.add((node.module or "").split(".")[0])
        # Act and assert
        self.assertEqual(set(), imported - {"collections", "re", "common", "convention_rules", "shapes_dev", "shapes_dev_extra"})

    def test_no_string_names_a_model_file_or_runner(self):
        # Arrange
        tree = self.parsed()
        docstring = ast.get_docstring(tree, clean=False)
        strings = [n.value for n in ast.walk(tree) if isinstance(n, ast.Constant) and isinstance(n.value, str) and n.value != docstring]
        joined = "\n".join(strings).lower()
        # Act and assert
        for forbidden in ("weights.bin", "dotnet", "partofspeech.trainer", "partofspeech.dll", ".dll", "probe.tsv", "decoder", "eval-shapes", "hmm"):
            self.assertNotIn(forbidden, joined, forbidden)

    def test_no_name_scores_or_predicts(self):
        # Arrange
        tree = self.parsed()
        names = {n.id for n in ast.walk(tree) if isinstance(n, ast.Name)} | {n.attr for n in ast.walk(tree) if isinstance(n, ast.Attribute)}
        names |= {n.name for n in ast.walk(tree) if isinstance(n, ast.FunctionDef)}
        # Act and assert
        for forbidden in ("tagger", "predict", "decode", "score", "weights", "classifier", "hmm"):
            self.assertEqual([], [n for n in names if forbidden in n.lower()], forbidden)

    def test_the_module_opens_no_file(self):
        # Arrange
        calls = [n.func.id for n in ast.walk(self.parsed()) if isinstance(n, ast.Call) and isinstance(n.func, ast.Name)]
        attributes = [n.attr for n in ast.walk(self.parsed()) if isinstance(n, ast.Attribute)]
        # Act and assert
        self.assertNotIn("open", calls)
        self.assertEqual([], [a for a in attributes if a in ("read_corpus", "read_lines", "read_lexicon", "write_text")])


if __name__ == "__main__":
    unittest.main()

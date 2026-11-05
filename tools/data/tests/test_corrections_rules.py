"""Tests of the convention rules (tools/data/convention_rules.py and convention_candidates.py) on fixture sentences."""
import os
import sys
import unittest

sys.path.insert(0, os.path.dirname(os.path.dirname(os.path.abspath(__file__))))
import common
import convention_candidates
import convention_rules as R

CASES = os.path.join(os.path.dirname(os.path.abspath(__file__)), "convention-cases.tsv")
RESOURCES = R.Resources(R.GRADABLE_SEED)


def make_sentence(source, tokens):
    """A `Sentence` from `form/TAG` (MASC: `form/TAG/PENN`) tokens separated by blanks."""
    rows = []
    for token in tokens.split(" "):
        parts = token.split("/")
        if source == "masc":
            rows.append([parts[0], parts[1], "ptb", parts[2]])
        elif source == "silver":
            rows.append([parts[0], parts[1], "masked" if parts[1] == "_" else "silver", "-"])
        else:
            rows.append([parts[0], parts[1], "unanimous"])
    return R.Sentence(source, "-" if source != "open" else "train", {"sent_id": "case", "source_id": "doc", "rows": rows})


def read_cases():
    """(case_id, rule, source, tokens, expected) of the fixture."""
    cases = []
    for line in common.read_lines(CASES):
        if line and not line.startswith("# "):
            cases.append(tuple(line.split("\t")))
    return cases


def expected_set(text):
    """The expected proposals of a case: a set of (index, tag or None)."""
    if text == "none":
        return set()
    result = set()
    for item in text.split(","):
        index, _, tag = item.partition(":")
        result.add((int(index), None if tag == "?" else tag))
    return result


class RuleCaseTests(unittest.TestCase):
    def test_every_case_of_the_fixture_gives_the_expected_proposals(self):
        # Arrange
        cases = read_cases()
        # Act and Assert
        self.assertGreater(len(cases), 120)
        for case_id, rule_id, source, tokens, expected in cases:
            with self.subTest(case=case_id):
                sentence = make_sentence(source, tokens)
                proposals = R.evaluate(sentence, RESOURCES, only={rule_id})
                self.assertEqual(expected_set(expected), {(item.index, item.new_tag) for item in proposals})
                self.assertTrue(all(item.rule == rule_id for item in proposals))

    def test_every_rule_has_a_positive_and_a_negative_case(self):
        # Arrange
        positive, negative = set(), set()
        for _, rule_id, _, _, expected in read_cases():
            (negative if expected == "none" else positive).add(rule_id)
        # Act and Assert
        for rule_id in R.rule_ids():
            with self.subTest(rule=rule_id):
                self.assertIn(rule_id, positive)
                self.assertIn(rule_id, negative)

    def test_fix_proposals_carry_a_tag_of_the_tag_set_and_check_violations_none(self):
        # Arrange
        by_id = {item.id: item for item in R.RULES}
        # Act and Assert
        for case_id, rule_id, source, tokens, _ in read_cases():
            for item in R.evaluate(make_sentence(source, tokens), RESOURCES, only={rule_id}):
                self.assertEqual(by_id[rule_id].tier, item.tier, case_id)
                if item.tier == R.FIX:
                    self.assertIn(item.new_tag, common.UPOS_SET, case_id)
                else:
                    self.assertIsNone(item.new_tag, case_id)

    def test_a_masked_token_is_never_proposed(self):
        # Arrange: every case sentence with each token masked in turn must produce no proposal at the masked token
        for case_id, rule_id, source, tokens, _ in read_cases():
            if source == "masc":
                continue
            parts = tokens.split(" ")
            for position in range(len(parts)):
                masked = list(parts)
                masked[position] = parts[position].split("/")[0] + "/_"
                # Act
                sentence = make_sentence("silver", " ".join(masked))
                found = R.evaluate(sentence, RESOURCES, only={rule_id})
                # Assert
                self.assertTrue(all(item.index != position for item in found), (case_id, position))


class RegistryTests(unittest.TestCase):
    def test_rule_ids_are_unique_lower_case_and_documented(self):
        ids = [item.id for item in R.RULES]
        self.assertEqual(len(ids), len(set(ids)))
        for item in R.RULES:
            self.assertRegex(item.id, r"^[a-z0-9]+(?:-[a-z0-9]+)*$")
            self.assertIn(item.tier, (R.FIX, R.CHECK))
            self.assertTrue(item.doc and item.section)

    def test_candidate_ids_do_not_clash_with_the_rules_that_become_candidates(self):
        # Arrange: a check violation becomes a candidate under the id of its rule, so a candidate detector must not reuse one
        # Act
        clash = convention_candidates.candidate_ids() & R.rule_ids()
        # Assert
        self.assertEqual(set(), clash)

    def test_the_trigger_words_are_lower_case(self):
        for item in R.RULES:
            for word in item.words or ():
                self.assertEqual(word, word.lower(), item.id)
        for item in convention_candidates.CANDIDATES:
            for word in item.words or ():
                self.assertEqual(word, word.lower(), item.id)

    def test_rule_set_version_is_a_number(self):
        self.assertRegex(R.RULE_SET_VERSION, r"^[0-9]+$")


class ResolutionTests(unittest.TestCase):
    def test_identical_proposals_keep_the_first_rule(self):
        # Arrange
        proposals = [R.Proposal(2, "ADP", "out-of", R.FIX), R.Proposal(2, "ADP", "multiword-prep", R.FIX)]
        # Act
        accepted, conflicts = R.resolve_fixes(proposals)
        # Assert
        self.assertEqual("out-of", accepted[2].rule)
        self.assertEqual([], conflicts)

    def test_different_tags_for_one_token_are_a_conflict(self):
        # Arrange
        proposals = [R.Proposal(2, "ADP", "out-of", R.FIX), R.Proposal(2, "ADV", "other", R.FIX)]
        # Act
        accepted, conflicts = R.resolve_fixes(proposals)
        # Assert
        self.assertEqual(1, len(conflicts))
        self.assertEqual("out-of", accepted[2].rule)

    def test_check_violations_do_not_enter_the_fixes(self):
        # Arrange
        proposals = [R.Proposal(2, None, "infinitive-to", R.CHECK)]
        # Act
        accepted, conflicts = R.resolve_fixes(proposals)
        # Assert
        self.assertEqual(({}, []), (accepted, conflicts))

    def test_a_sentence_without_trigger_words_is_skipped_but_a_rule_without_triggers_still_runs(self):
        # Arrange
        sentence = make_sentence("open", "In/ADP the/DET 1990s/NUM ./PUNCT")
        # Act
        found = R.evaluate(sentence, RESOURCES)
        # Assert: `decade` has no trigger words, the word rules have none of theirs
        self.assertEqual(["decade"], [item.rule for item in found])


class ResourcesTests(unittest.TestCase):
    def test_a_participle_after_a_strong_degree_word_becomes_gradable(self):
        # Arrange
        sentences = [make_sentence("open", "It/PRON was/VERB very/ADV relocated/ADJ ./PUNCT"),
                     make_sentence("open", "It/PRON was/VERB so/ADV amended/ADJ ./PUNCT")]
        # Act
        resources = R.build_resources(sentences)
        # Assert: `very` proves gradability, `so` alone does not
        self.assertIn("relocated", resources.gradable)
        self.assertNotIn("amended", resources.gradable)
        self.assertIn("tired", resources.gradable)


class CandidateTests(unittest.TestCase):
    def found(self, source, tokens, candidate_id):
        sentence = make_sentence(source, tokens)
        return [(index, span, hint) for rule_id, index, span, hint in convention_candidates.evaluate(sentence, RESOURCES)
                if rule_id == candidate_id]

    def test_every_candidate_detector_has_a_positive_fixture(self):
        # Arrange: one sentence per detector
        fixtures = {
            "prenominal-ing": ("open", "The/DET operating/VERB modes/NOUN"),
            "prenominal-ed": ("open", "A/DET broken/VERB window/NOUN"),
            "be-participle-gradable": ("open", "It/PRON was/AUX surprised/VERB ./PUNCT"),
            "zero-derived-degree": ("open", "It/PRON is/VERB very/ADV London/NOUN ./PUNCT"),
            "mention-the-word": ("open", "The/DET word/NOUN snowball/NOUN is/VERB fun/ADJ"),
            "mention-quoted": ("open", "Say/VERB the/DET word/NOUN \"/PUNCT it/PRON \"/PUNCT again/ADV"),
            "mention-in-language": ("open", "Hello/INTJ in/ADP French/PROPN is/VERB nice/ADJ"),
            "foreign-token": ("open", "Un/X terremoto/X grande/X"),
            "propn-boundary": ("open", "He/PRON saluted/VERB the/DET Colonel/NOUN again/ADV"),
            "language-name": ("open", "She/PRON speaks/VERB English/PROPN"),
            "title-alone": ("open", "He/PRON saluted/VERB the/DET Colonel/NOUN again/ADV"),
            "title-before-name": ("open", "Vice/ADJ President/NOUN Charles/PROPN spoke/VERB"),
            "possessive-her": ("open", "He/PRON gave/VERB her/PRON flowers/NOUN"),
            "out-of-after-verb": ("open", "Get/VERB out/ADV of/ADP here/ADV"),
            "as-well-as-sconj": ("open", "Cats/NOUN as/ADV well/ADV as/SCONJ the/DET dog/NOUN knew/VERB"),
            "for-subject-to-ambiguous": ("open", "Thanks/NOUN for/ADP his/DET efforts/NOUN to/PART register/VERB"),
            "degree-word-ambiguous": ("open", "It/PRON is/VERB more/ADJ serious/ADJ problems/NOUN"),
            "half-ambiguous": ("open", "Half/NUM was/VERB left/VERB"),
            "list-label": ("open", "1/NOUN )/PUNCT Buy/VERB milk/NOUN"),
        }
        # Act and Assert
        self.assertEqual(convention_candidates.candidate_ids(), set(fixtures))
        for candidate_id, (source, tokens) in fixtures.items():
            with self.subTest(candidate=candidate_id):
                self.assertTrue(self.found(source, tokens, candidate_id), candidate_id)

    def test_the_mention_candidate_names_the_span_inside_the_quotes(self):
        # Arrange and Act
        found = self.found("open", "The/DET word/NOUN \"/PUNCT snow/NOUN ball/NOUN \"/PUNCT is/VERB fun/ADJ", "mention-the-word")
        # Assert
        self.assertEqual([(3, 2, "head=word; quote=\"")], found)

    def test_speech_in_quotes_is_not_a_mention(self):
        # Arrange and Act
        found = self.found("open", "He/PRON said/VERB \"/PUNCT hello/INTJ \"/PUNCT loudly/ADV", "mention-quoted")
        # Assert
        self.assertEqual([], found)

    def test_a_capitalized_noun_inside_a_title_case_run_is_not_a_proper_noun_candidate(self):
        # Arrange and Act
        found = self.found("open", "It/PRON is/VERB in/ADP the/DET Public/ADJ Domain/NOUN now/ADV", "propn-boundary")
        # Assert
        self.assertEqual([], found)

    def test_a_masked_token_is_never_a_candidate(self):
        # Arrange
        sentence = make_sentence("silver", "He/PRON saluted/VERB the/DET Colonel/_ again/ADV")
        # Act
        found = convention_candidates.evaluate(sentence, RESOURCES)
        # Assert
        self.assertTrue(all(index != 3 for _, index, _, _ in found))


if __name__ == "__main__":
    unittest.main()

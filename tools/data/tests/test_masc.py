"""Unit tests of the MASC Penn Treebank to UPOS tag function (tools/data/import_masc.py)."""
import os
import sys
import unittest

sys.path.insert(0, os.path.dirname(os.path.dirname(os.path.abspath(__file__))))
import import_masc as masc


def tag(tree_text):
    """(form, upos) pairs of one bracketed tree."""
    forms, tags, _ = masc.convert_tree(masc.parse_trees(tree_text)[0])
    return list(zip(forms, tags))


class TreeTests(unittest.TestCase):
    def test_empty_label_root_is_handled(self):
        trees = masc.parse_trees("( (S (NP (PRP It)) (VP (VBZ works))) )")
        self.assertEqual(len(trees), 1)
        self.assertEqual(trees[0].label, "")
        self.assertEqual(trees[0].kids[0].label, "S")

    def test_traces_are_dropped(self):
        pairs = tag("( (S (NP-SBJ (-NONE- *PRO*)) (VP (VB go))) )")
        self.assertEqual(pairs, [("go", "VERB")])

    def test_code_trees_are_not_sentences(self):
        self.assertIsNone(masc.convert_tree(masc.parse_trees("( (CODE <LU_ANNOTATE>))")[0]))


class MappingTests(unittest.TestCase):
    def test_helper_be_is_aux_and_copula_is_verb(self):
        self.assertEqual(tag("( (S (NP (PRP He)) (VP (VBZ is) (VP (VBG running)))) )")[1], ("is", "AUX"))
        self.assertEqual(tag("( (S (NP (PRP He)) (VP (VBZ is) (ADJP (JJ tall)))) )")[1], ("is", "VERB"))

    def test_main_verb_do_and_have(self):
        self.assertEqual(tag("( (S (NP (PRP I)) (VP (VBP have) (NP (DT a) (NN car)))) )")[1], ("have", "VERB"))
        self.assertEqual(tag("( (S (NP (PRP I)) (VP (VBP have) (VP (VBN gone)))) )")[1], ("have", "AUX"))

    def test_infinitive_to_is_part_and_preposition_to_is_adp(self):
        self.assertEqual(tag("( (S (VP (TO to) (VP (VB go)))) )")[0], ("to", "PART"))
        self.assertEqual(tag("( (PP (TO to) (NP (NNP Rome))) )")[0], ("to", "ADP"))

    def test_possessive_clitic_and_negation_are_part(self):
        pairs = tag("( (NP (NP (NNP Maria) (POS 's)) (NN report)) )")
        self.assertEqual(pairs[1], ("'s", "PART"))
        self.assertEqual(tag("( (VP (VBP do) (RB n't) (VP (VB go))) )")[1], ("n't", "PART"))

    def test_determiners_and_pronouns(self):
        self.assertEqual(tag("( (NP (PRP$ my) (NN dog)) )")[0], ("my", "DET"))
        self.assertEqual(tag("( (S (NP (DT That)) (VP (VBZ works))) )")[0], ("That", "PRON"))
        self.assertEqual(tag("( (NP (DT That) (NN day)) )")[0], ("That", "DET"))
        self.assertEqual(tag("( (NP (NN something)) )")[0], ("something", "PRON"))

    def test_subordinators_and_conjunctions(self):
        self.assertEqual(tag("( (SBAR (IN because) (S (NP (PRP he)) (VP (VBD left)))) )")[0], ("because", "SCONJ"))
        self.assertEqual(tag("( (PP (IN of) (NP (NN time))) )")[0], ("of", "ADP"))
        self.assertEqual(tag("( (NP (NN a) (CC and) (NN b)) )")[1], ("and", "CCONJ"))

    def test_when_is_sconj_in_an_adverbial_clause_and_adv_in_a_question(self):
        adverbial = "( (S (SBAR-TMP (WHADVP (WRB When)) (S (NP (PRP he)) (VP (VBD left)))) (VP (VBD cried))) )"
        question = "( (SBARQ (WHADVP (WRB When)) (SQ (VBD did) (NP (PRP he)) (VP (VB leave)))) )"
        self.assertEqual(tag(adverbial)[0], ("When", "SCONJ"))
        self.assertEqual(tag(question)[0], ("When", "ADV"))

    def test_participial_preposition_and_prenominal_participle(self):
        self.assertEqual(tag("( (PP (VBG including) (NP (NNS dogs))) )")[0], ("including", "ADP"))
        self.assertEqual(tag("( (NP (DT the) (VBG running) (NN water)) )")[1], ("running", "ADJ"))

    def test_symbols_punctuation_and_numbers(self):
        self.assertEqual(tag("( (NP (NN 50) (NN %) (. .)) )"), [("50", "NUM"), ("%", "SYM"), (".", "PUNCT")])
        self.assertEqual(tag("( (NP (-LRB- -LRB-) (CD 3) (-RRB- -RRB-)) )"), [("(", "PUNCT"), ("3", "NUM"), (")", "PUNCT")])

    def test_every_tag_is_one_of_the_seventeen(self):
        import common
        tree = "( (S (NP (NNP Tom)) (VP (VBD ran) (ADVP (RB fast)) (PP (IN to) (NP (DT the) (NN shop))))) )"
        self.assertTrue({t for _, t in tag(tree)} <= common.UPOS_SET)


if __name__ == "__main__":
    unittest.main()

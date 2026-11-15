"""Unit tests of the silver derivation and the lexicon build rules."""
import os
import sys
import tempfile
import unittest

sys.path.insert(0, os.path.dirname(os.path.dirname(os.path.abspath(__file__))))
import build_lexicon
import check_lexicon
import common
import import_open
import import_silver as silver

LEXICON = {"tom": frozenset({"NOUN"}), "paris": frozenset({"PROPN"}), "dog": frozenset({"NOUN"})}


def derive(pairs):
    forms = [f for f, _ in pairs]
    tags = [t for _, t in pairs]
    return [u if s == "silver" else "_" for u, s in silver.derive_sentence(forms, tags, LEXICON)]


class SilverDerivationTests(unittest.TestCase):
    def test_class_mapping(self):
        pairs = [("the", "D:100"), ("dog", "N:99"), ("barks", "V:97"), ("loudly", "B:80"), ("in", "P:99"),
                 ("red", "J:90"), ("oh", "I:70"), ("it", "R:99"), ("will", "A:99"), ("to", "T:99"), (".", "-")]
        self.assertEqual(derive(pairs), ["DET", "NOUN", "VERB", "ADV", "ADP", "ADJ", "INTJ", "PRON", "AUX", "PART", "PUNCT"])

    def test_low_confidence_is_masked(self):
        self.assertEqual(derive([("dog", "N:49")]), ["_"])

    def test_conjunction_split(self):
        self.assertEqual(derive([("and", "C:99"), ("because", "C:99"), ("for", "C:99")]), ["CCONJ", "SCONJ", "_"])

    def test_capitalized_nouns(self):
        self.assertEqual(derive([("Tom", "N:99"), ("saw", "V:99"), ("Paris", "N:99"), ("Berlin", "N:99")]),
                         ["_", "VERB", "PROPN", "PROPN"])

    def test_unknown_words(self):
        self.assertEqual(derive([("The", "D:99"), ("zzyzx", "?"), ("saw", "V:99"), ("Zzyzx", "?")]),
                         ["DET", "_", "VERB", "PROPN"])
        self.assertEqual(derive([("Zzyzx", "?"), ("fell", "V:99")]), ["_", "VERB"])

    def test_shape_rules(self):
        self.assertEqual(derive([("3.5", "-"), ("%", "-"), ("--", "-"), ("3rd", "-")]), ["NUM", "SYM", "PUNCT", "_"])

    def test_contractions(self):
        stem_do = [("do", "A:99@s"), ("n't", "A:99@c")]
        self.assertEqual(derive(stem_do), ["AUX", "PART"])
        self.assertEqual(derive([("is", "A:99@s"), ("n't", "A:99@c")]), ["_", "PART"])
        self.assertEqual(derive([("I", "R:99@s"), ("'ve", "R:99@c")]), ["PRON", "AUX"])
        self.assertEqual(derive([("it", "R:99@s"), ("'s", "R:99@c")]), ["PRON", "_"])
        self.assertEqual(derive([("that", "D:99@s"), ("'s", "D:99@c")]), ["PRON", "_"])

    def test_clitic_split_keeps_tags(self):
        forms, tags = silver.split_sentence(["I", "can't", "go", "."], ["R:99", "A:99", "V:99", "-"])
        self.assertEqual(forms, ["I", "ca", "n't", "go", "."])
        self.assertEqual(tags, ["R:99", "A:99@s", "A:99@c", "V:99", "-"])

    def test_every_result_is_a_valid_tag_or_masked(self):
        for letter in "NRVJBPCIDATO":
            for word in ("The", "dog", "and", "so", "Paris"):
                result = silver.derive_sentence(["x", word], ["-", letter + ":90"], LEXICON)[1]
                self.assertTrue(result[0] == "_" or result[0] in common.UPOS_SET)


class FlagPreservationTests(unittest.TestCase):
    """The importers and the lexicon tools accept status flags (`+mentioned`, `+foreign`) and keep them."""

    def test_derive_keeps_the_flags_and_recomputes_the_status(self):
        sentences = [{"sent_id": "s-1", "source_id": "s", "text": "the verb Sierra Tom .",
                      "rows": [["the", "_", "silver", "D:99"], ["verb", "_", "silver", "N:99"],
                               ["Sierra", "_", "silver+foreign+mentioned", "N:99"],
                               ["Tom", "_", "silver+mentioned", "N:99"], [".", "_", "silver", "-"]]}]
        with tempfile.TemporaryDirectory() as folder:
            path = os.path.join(folder, "silver.tsv")
            silver.write_silver(path, sentences, LEXICON)
            header, back = common.read_corpus(path)
            rows = back[0]["rows"]
        self.assertTrue(any(line.startswith("# status flags = ") for line in header))
        self.assertEqual([r[1] for r in rows], ["DET", "NOUN", "PROPN", "_", "PUNCT"])
        self.assertEqual([r[2] for r in rows], ["silver", "silver", "silver+foreign+mentioned", "masked+mentioned", "silver"])

    def test_open_import_accepts_flagged_consensus_values(self):
        lines = ["# sent_id = s1", "# source_id = d1", "# text = the verb snowball"]
        for index, (form, upos, status) in enumerate((("the", "DET", "unanimous"), ("verb", "NOUN", "unchecked+mentioned"),
                                                      ("snowball", "VERB", "disputed+mentioned"))):
            lines.append("\t".join((str(index + 1), form, form, upos, upos, "_", status)))
        with tempfile.TemporaryDirectory() as folder:
            path = os.path.join(folder, "open.tsv")
            common.write_text(path, ["# corpus = test", ""] + lines)
            rows = import_open.read_projected(path)[0]["rows"]
        self.assertEqual([r[2] for r in rows], ["unanimous", "unchecked+mentioned", "disputed+mentioned"])

    def test_open_import_refuses_a_bad_flag_or_status(self):
        for status in ("unanimous+shouted", "unanimous+mentioned+foreign", "silver", "mentioned"):
            lines = ["# corpus = test", "", "# sent_id = s1", "# source_id = d1", "# text = a",
                     "\t".join(("1", "a", "a", "DET", "DET", "_", status))]
            with tempfile.TemporaryDirectory() as folder:
                path = os.path.join(folder, "open.tsv")
                common.write_text(path, lines)
                with self.assertRaises(ValueError, msg=status):
                    import_open.read_projected(path)

    def test_lexicon_attestation_ignores_flags_when_deciding_disputed_and_masked(self):
        rows = [("snowball", "VERB", "disputed+mentioned"), ("snowball", "VERB", "unanimous+mentioned"),
                ("snowball", "VERB", "unanimous"), ("it", "_", "masked+mentioned")]
        lines = ["# corpus = test", "", "# sent_id = s1", "# source_id = d1", "# text = x"]
        lines += ["%s\t%s\t%s" % row for row in rows]
        with tempfile.TemporaryDirectory() as folder:
            path = os.path.join(folder, "c.tsv")
            common.write_text(path, lines)
            self.assertEqual(build_lexicon.attested(path, 1, skip_disputed=True), {("snowball", "VERB"): 2})
            self.assertEqual(build_lexicon.attested(path, 1, skip_disputed=False), {("snowball", "VERB"): 3})


    def test_lexicon_tools_refuse_a_malformed_status(self):
        for status in ("unanimous+bogus", "unanimous+mentioned+foreign", "unanimous+mentioned+mentioned", "bogus"):
            lines = ["# corpus = test", "", "# sent_id = s1", "# source_id = d1", "# text = x", "snowball\tVERB\t" + status]
            with tempfile.TemporaryDirectory() as folder:
                path = os.path.join(folder, "c.tsv")
                common.write_text(path, lines)
                with self.assertRaises(ValueError, msg=status):
                    build_lexicon.attested(path, 1, skip_disputed=True)
                with self.assertRaises(ValueError, msg=status):
                    check_lexicon.measure(LEXICON, path)

    def test_lexicon_coverage_ignores_flags_when_deciding_disputed(self):
        lines = ["# corpus = test", "", "# sent_id = s1", "# source_id = d1", "# text = x",
                 "dog\tNOUN\tunanimous+mentioned", "dog\tVERB\tdisputed+mentioned"]
        with tempfile.TemporaryDirectory() as folder:
            path = os.path.join(folder, "c.tsv")
            common.write_text(path, lines)
            stats, _ = check_lexicon.measure(LEXICON, path)
        self.assertEqual((stats["a_tokens"], stats["n_tokens"]), (2, 1))


class LexiconBuildTests(unittest.TestCase):
    def test_entry_pos_parses_homograph_numbers(self):
        self.assertEqual(build_lexicon.entry_pos("oewn-wind-n-1", ""), "n")
        self.assertEqual(build_lexicon.entry_pos("oewn-wind-v-2", ""), "v")
        self.assertEqual(build_lexicon.entry_pos("dema-to-p", ""), "p")
        self.assertEqual(build_lexicon.entry_pos("oewn--ap-hood-n", ""), "n")
        self.assertEqual(build_lexicon.entry_pos("", "a"), "a")
        self.assertEqual(build_lexicon.entry_pos("oewn-xyz", ""), "")

    def test_dictionary_rules(self):
        header = "spelling\trank\tentry_id\tfeatures\tsource_id\treviewed\tlemma\tpos\tprovenance"
        rows = [
            "wind\t0\toewn-wind-n-1\tNumber=Sing\toewn\t0\t\t\toewn_form",
            "wind\t0\toewn-wind-v-1\tVerbForm=Inf\toewn\t0\t\t\toewn_form",
            "Paris\t0\toewn-Paris-n\tNumber=Sing\toewn\t0\t\t\toewn_form",
            "broken\t0\toewn-break-v\tTense=Past+VerbForm=Part\toewn\t0\t\t\trule",
            "running\t0\toewn-run-v\tVerbForm=Ger\toewn\t0\t\t\trule",
            "new york\t0\toewn-new_york-n\tNumber=Sing\toewn\t0\t\t\toewn_form",
            "he'd\t0\tdema-he-u\tContraction=Yes\tdema\t0\t\t\tcontraction",
            "abo\t0\t\tNumber=Sing\toewn\t0\t\tn\tmoby_reverse",
        ]
        with tempfile.TemporaryDirectory() as folder:
            path = os.path.join(folder, "a.tsv")
            common.write_text(path, [header] + rows)
            lexicon = build_lexicon.dictionary_tags([("a.tsv", path)], {})
        self.assertEqual(lexicon["wind"], {"NOUN", "VERB"})
        self.assertEqual(lexicon["paris"], {"NOUN", "PROPN"})
        self.assertEqual(lexicon["broken"], {"VERB", "ADJ"})
        self.assertEqual(lexicon["running"], {"VERB", "ADJ", "NOUN"})
        self.assertEqual(lexicon["abo"], {"NOUN"})
        self.assertNotIn("new york", lexicon)
        self.assertNotIn("he'd", lexicon)

    def test_closed_class_file_is_valid(self):
        table = build_lexicon.read_closed_class()
        self.assertIn("the", table)
        self.assertEqual(table["the"], (frozenset({"DET"}), "exact"))


HEADER = "spelling\trank\tentry_id\tfeatures\tsource_id\treviewed\tlemma\tpos\tprovenance"


def form_row(spelling, entry_id, features="", lemma="", pos=""):
    return "\t".join((spelling, "0", entry_id, features, "oewn", "0", lemma, pos, "rule"))


def analyses_of(rows, lexicon_tags, closed=None):
    with tempfile.TemporaryDirectory() as folder:
        path = os.path.join(folder, "a.tsv")
        common.write_text(path, [HEADER] + rows)
        return build_lexicon.build_analyses([("a.tsv", path)], lexicon_tags, closed or {})


class AnalysesBuildTests(unittest.TestCase):
    def test_entry_lemma_from_entry_ids(self):
        self.assertEqual(build_lexicon.entry_lemma("oewn-test-v", ""), "test")
        self.assertEqual(build_lexicon.entry_lemma("oewn-wind-n-1", ""), "wind")
        self.assertEqual(build_lexicon.entry_lemma("dema-to-p", ""), "to")
        self.assertEqual(build_lexicon.entry_lemma("oewn-Paris-n", ""), "paris")

    def test_entry_lemma_decodes_the_three_escapes(self):
        self.assertEqual(build_lexicon.entry_lemma("oewn--ap-hood-n", ""), "'hood")
        self.assertEqual(build_lexicon.entry_lemma("oewn-20-sl-20-n", ""), "20/20")
        self.assertEqual(build_lexicon.entry_lemma("oewn-LGBT-pl--n", ""), "lgbt+")
        self.assertEqual(build_lexicon.entry_lemma("oewn-nor-ap--nor-ap--east-n", ""), "nor'-nor'-east")
        self.assertEqual(build_lexicon.entry_lemma("oewn-air-to-air-a", ""), "air-to-air")

    def test_entry_lemma_from_the_lemma_column_when_the_entry_id_is_empty(self):
        self.assertEqual(build_lexicon.entry_lemma("", "Run"), "run")
        self.assertEqual(build_lexicon.entry_lemma("", "caf\u0065\u0301"), "caf\u00e9")

    def test_entry_lemma_refuses_bad_input(self):
        for entry_id, lemma in (("", ""), ("", "two words"), ("oewn-xyz", ""), ("oewn-n", ""), ("test-v", ""), ("oewn-a_b-n", "")):
            with self.assertRaises(ValueError, msg=(entry_id, lemma)):
                build_lexicon.entry_lemma(entry_id, lemma)

    def test_features_drop_capitalization_and_use_the_ud_notation(self):
        self.assertEqual(build_lexicon.analysis_features("Person=3+Tense=Pres"), "Person=3|Tense=Pres")
        self.assertEqual(build_lexicon.analysis_features("Capitalization=Title+Number=Sing"), "Number=Sing")
        self.assertEqual(build_lexicon.analysis_features("Capitalization=Upper"), "")
        self.assertEqual(build_lexicon.analysis_features(""), "")
        self.assertEqual(build_lexicon.analysis_features("Number=Sing+VerbForm=Ger"), "Number=Sing|VerbForm=Ger")

    def test_features_refuse_new_vocabulary_and_wrong_order(self):
        for features in ("Mood=Ind", "Number=Dual", "Tense=Past+Number=Sing", "Number=Sing+Number=Plur", "Plural", "Person=1"):
            with self.assertRaises(ValueError, msg=features):
                build_lexicon.analysis_features(features)

    def test_analyses_of_a_form_row_use_its_own_part_of_speech_only(self):
        # Arrange: a verb form that the dictionary build also tags ADJ (participle) and a capitalized noun that gets PROPN
        rows = [form_row("tested", "oewn-test-v", "Tense=Past+VerbForm=Part"), form_row("Tom", "oewn-Tom-n", "Capitalization=Title+Number=Sing")]
        tags = {"tested": {"VERB", "ADJ"}, "tom": {"NOUN", "PROPN"}}
        # Act
        analyses = analyses_of(rows, tags)
        # Assert: no analysis for the tags that a rule added
        self.assertEqual(analyses["tested"], [("VERB", "test", "Tense=Past|VerbForm=Part")])
        self.assertEqual(analyses["tom"], [("NOUN", "tom", "Number=Sing")])

    def test_an_analysis_whose_tag_is_not_in_the_final_tag_set_is_dropped(self):
        rows = [form_row("a", "oewn-a-n", "Number=Sing"), form_row("a", "dema-a-d")]
        self.assertEqual(analyses_of(rows, {"a": {"DET"}}), {"a": [("DET", "a", "")]})

    def test_a_spelling_that_is_not_in_the_lexicon_has_no_analysis(self):
        self.assertEqual(analyses_of([form_row("zzz", "oewn-zzz-n", "Number=Sing")], {"other": {"NOUN"}}), {})

    def test_duplicates_collapse_and_each_list_is_ordered_by_tag_lemma_features(self):
        rows = [form_row("tests", "oewn-test-v", "Person=3+Tense=Pres"), form_row("tests", "oewn-test-n", "Number=Plur"),
                form_row("tests", "oewn-test-n", "Number=Plur"), form_row("tests", "oewn-test-n-2", "Number=Plur"),
                form_row("tests", "oewn-tests-n", "Number=Plur")]
        analyses = analyses_of(rows, {"tests": {"NOUN", "VERB"}})
        self.assertEqual(analyses["tests"], [("NOUN", "test", "Number=Plur"), ("NOUN", "tests", "Number=Plur"),
                                             ("VERB", "test", "Person=3|Tense=Pres")])

    def test_rows_the_lexicon_build_skips_have_no_analysis(self):
        rows = [form_row("he'd", "dema-he-u", "Contraction=Yes"), form_row("new york", "oewn-new_york-n", "Number=Sing"),
                form_row("123", "oewn-123-n", "Number=Sing"), form_row("abo", "", "Number=Sing", lemma="Abo", pos="n"),
                form_row("odd", "oewn-odd-q", "")]
        tags = {"he'd": {"PRON"}, "new york": {"NOUN"}, "123": {"NUM"}, "abo": {"NOUN"}, "odd": {"ADJ"}}
        self.assertEqual(analyses_of(rows, tags), {"abo": [("NOUN", "abo", "Number=Sing")]})

    def test_a_conjunction_is_cconj_only_when_the_closed_class_list_says_so(self):
        rows = [form_row("and", "dema-and-c"), form_row("because", "dema-because-c")]
        closed = {"and": (frozenset({"CCONJ"}), "exact")}
        analyses = analyses_of(rows, {"and": {"CCONJ"}, "because": {"SCONJ"}}, closed)
        self.assertEqual(analyses, {"and": [("CCONJ", "and", "")], "because": [("SCONJ", "because", "")]})

    def test_a_new_feature_value_in_a_used_row_is_a_build_error(self):
        with self.assertRaises(ValueError):
            analyses_of([form_row("run", "oewn-run-v", "Mood=Ind")], {"run": {"VERB"}})

    def test_the_table_lines_round_trip_and_state_the_counts_and_the_pinned_commit(self):
        analyses = {"tests": [("NOUN", "test", "Number=Plur"), ("VERB", "test", "Person=3|Tense=Pres")], "the": [("DET", "the", "")]}
        lines = build_lexicon.analyses_lines(analyses)
        self.assertIn("tests\ttest NOUN Number=Plur\ttest VERB Person=3|Tense=Pres", lines)
        self.assertIn("the\tthe DET", lines)
        self.assertTrue(any("spellings=2, analyses=3" in line for line in lines))
        self.assertTrue(any(common.WORDSENSE_COMMIT in line for line in lines))
        with tempfile.TemporaryDirectory() as folder:
            path = os.path.join(folder, "analyses.tsv")
            build_lexicon.write_analyses(analyses, path)
            back, declared = build_lexicon.read_analyses(path)
        self.assertEqual(back, analyses)
        self.assertEqual(declared, (2, 3))

    def test_rows_ascend_by_utf8_bytes(self):
        lines = build_lexicon.analyses_lines({"\u00e9": [("NOUN", "e", "")], "z": [("NOUN", "z", "")], "a": [("NOUN", "a", "")]})
        self.assertEqual([line.split("\t")[0] for line in lines if not line.startswith("# ")], ["a", "z", "\u00e9"])

    def test_a_difference_between_the_rebuilt_and_the_committed_lines_is_reported(self):
        expected = build_lexicon.analyses_lines({"a": [("NOUN", "a", "")]})
        changed = build_lexicon.analyses_lines({"a": [("NOUN", "b", "")]})
        self.assertEqual(build_lexicon.analyses_differences(expected, expected), [])
        differences = build_lexicon.analyses_differences(expected, changed)
        self.assertEqual(1, len(differences))
        self.assertIn("a NOUN", differences[0])
        self.assertTrue(build_lexicon.analyses_differences(expected, expected[:-1]))


if __name__ == "__main__":
    unittest.main()

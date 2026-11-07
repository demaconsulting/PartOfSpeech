"""Tests of shapes_dev.py (the builder of the hard-shape development set) on a miniature data and work folder.

Run: python -m unittest discover -s tools/data/tests -p "test_shapes_dev.py"   (developer-run; the pipeline needs no model or network)
"""
import contextlib
import collections
import hashlib
import io
import os
import re
import shutil
import sys
import tempfile
import unittest

sys.path.insert(0, os.path.dirname(os.path.dirname(os.path.abspath(__file__))))
import adjudicate
import check_leakage
import check_licenses
import common
import leakage
import shapes_dev as sd

LEXICON = {
    "valve": "NOUN", "pressure": "NOUN", "relief": "NOUN", "oil": "NOUN", "tank": "NOUN", "level": "NOUN VERB", "daily": "ADV",
    "switch": "NOUN VERB", "check": "NOUN VERB", "drop": "NOUN VERB", "drops": "NOUN VERB", "flow": "NOUN VERB", "seal": "NOUN VERB",
    "stop": "NOUN VERB", "stops": "NOUN VERB", "pound": "NOUN VERB", "pounds": "NOUN VERB", "pump": "NOUN VERB", "pumps": "NOUN VERB",
    "mix": "NOUN VERB", "leak": "NOUN VERB", "test": "NOUN VERB", "tests": "NOUN VERB", "opens": "VERB", "add": "VERB", "fails": "VERB",
    "the": "DET", "a": "DET", "of": "ADP", "to": "ADP PART", "and": "CCONJ", "must": "AUX", "is": "AUX VERB", "full": "ADJ",
    "machine": "NOUN VERB", "guns": "NOUN", "forest": "NOUN VERB", "planting": "NOUN VERB", "long": "ADJ", "in": "ADP",
    "light": "ADJ NOUN",
}


def simple_tokens(text):
    return re.findall(r"n't|\w+|[^\w\s]", text)


def corpus_file(path, items, extra=None):
    """A corpus file of (source_id, text) items; every token is a NOUN (the leakage check reads only ids and texts)."""
    sentences = []
    for number, (source_id, text) in enumerate(items, 1):
        sentence = {"sent_id": "s%d" % number, "source_id": source_id, "text": text,
                    "rows": [[w, "NOUN", "unanimous"] for w in text.split(" ")]}
        if extra:
            sentence["extra"] = extra[number - 1]
        sentences.append(sentence)
    common.write_corpus(path, ["# corpus = fixture"], sentences)


def table_file(path, header, rows):
    common.write_text(path, ["\t".join(header)] + ["\t".join(row) for row in rows])


class MiniFolder(unittest.TestCase):
    """A miniature data folder and work folder; `common.DATA` points to the data folder while a test runs."""

    def setUp(self):
        self.temp = tempfile.mkdtemp()
        self.addCleanup(shutil.rmtree, self.temp, True)
        self.data = os.path.join(self.temp, "data")
        self.root = os.path.join(self.temp, "work")
        os.makedirs(self.root)
        self.work = adjudicate.Work(self.root)
        self.saved = common.DATA
        common.DATA = self.data
        self.addCleanup(setattr, common, "DATA", self.saved)
        self.write_data()

    def write_data(self):
        d = self.data
        common.write_text(os.path.join(d, "lexicon", "lexicon.tsv"),
                          ["# fixture lexicon"] + ["%s\t%s" % (k, v) for k, v in sorted(LEXICON.items())])
        corpus_file(os.path.join(d, "open", "train.tsv"), [("train-1", "Mix the oil and the water in the tank daily .")])
        corpus_file(os.path.join(d, "open", "dev.tsv"), [("dev-1", "The relief valve opens at forty psi under load .")])
        corpus_file(os.path.join(d, "open", "test.tsv"), [("test-1", "A completely different held out sentence here .")])
        corpus_file(os.path.join(d, "masc", "masc.tsv"), [("masc:written/x", "Another unrelated training sentence is here .")])
        corpus_file(os.path.join(d, "silver", "silver.tsv"), [("gutenberg-1", "Yet another silver training sentence appears here .")])
        corpus_file(os.path.join(d, "eval", "ste-challenge", "test.tsv"), [("ste-1", "Remove the cover before you start the work .")])
        table_file(os.path.join(d, "eval", "gold-v2", "dev.tsv"), ("id", "doc_id", "sentence"), [("g1", "gutenberg-99", "Gold sentence one is here .")])
        table_file(os.path.join(d, "eval", "gold-v2", "test.tsv"), ("id", "doc_id", "sentence"), [("g2", "gutenberg-98", "Gold sentence two is here .")])
        table_file(os.path.join(d, "eval", "shapes", "probe.tsv"), ("id", "shape", "lemma", "gold", "target_index", "sentence"),
                   [("p1", "x", "seal", "Verb", "0", "Seal the pump housing with the gasket compound before fitting .")])
        common.write_text(os.path.join(d, "open", "sources.tsv"), ["\t".join(sd.SOURCE_COLUMNS),
                          "gutenberg-1023\tgutenberg\tT\thttps://www.gutenberg.org/cache/epub/1023/pg1023.txt\tA\t1850\tUS public domain\t"
                          "https://www.gutenberg.org/policy/license.html\t2026-10-29\taa\tbb\t5"])
        common.write_text(os.path.join(d, "masc", "sources.tsv"), ["# columns = source_id, genre, license_basis, sentences, tokens",
                          "masc:written/x\tblog\tCC BY 3.0 US\t1\t7"])
        common.write_text(os.path.join(d, "silver", "excluded-documents.tsv"), ["# columns = document_id, set, matching_sentences, part",
                          "gutenberg-7194\topen-build\t-\tspike-selection"])
        common.write_text(os.path.join(d, "eval", "shapes-dev", "excluded-sources.tsv"), ["\t".join(common.EXCLUDED_SOURCES_COLUMNS)])      # header only: nothing excluded

    def source_row(self, source_id, url="https://example.gov/%s", sentences="0"):
        return [source_id, "us-government", "Manual " + source_id, url % source_id if "%s" in url else url, "Agency", "1990",
                "US federal government work (17 U.S.C. 105): test", "https://example.gov/terms", "2026-11-01", "a" * 64, "b" * 64, sentences]

    def write_draft(self, ids):
        rows = ["\t".join(self.source_row(i)) for i in ids]
        common.write_text(os.path.join(self.root, "sources-draft.tsv"), ["\t".join(sd.SOURCE_COLUMNS)] + rows)

    def write_text_doc(self, source_id, sentences):
        common.write_text(os.path.join(self.root, "text", source_id + ".txt"), sentences)

    def write_authored(self, shape, rows):
        common.write_text(os.path.join(self.root, "authored", shape + ".tsv"), ["lemma\trole\tsentence"] + ["\t".join(r) for r in rows])

    def tokens_for(self, *sentence_lists):
        return {text: (1, simple_tokens(text)) for texts in sentence_lists for text in texts}

    def cli(self, *args):
        out = io.StringIO()
        with contextlib.redirect_stdout(out):
            code = sd.main(["--work", self.root, "--data-dir", self.data] + list(args))
        return code, out.getvalue()

    def snapshot(self):
        """{relative path: SHA-256} of every file of the work folder."""
        found = {}
        for folder, _, names in os.walk(self.root):
            for name in names:
                path = os.path.join(folder, name)
                with open(path, "rb") as handle:
                    found[os.path.relpath(path, self.root)] = hashlib.sha256(handle.read()).hexdigest()
        return found

    def assert_refused(self, args, text="conflicting"):
        """The command line `args` exits 1 with `text` on stderr and leaves the work folder byte for byte as it was."""
        before = self.snapshot()
        err = io.StringIO()
        with contextlib.redirect_stderr(err):
            code, _ = self.cli(*args)
        self.assertEqual(1, code, args)
        self.assertIn("refused", err.getvalue(), args)
        self.assertIn(text, err.getvalue(), args)
        self.assertEqual(before, self.snapshot(), args)


# One real document with sentences for every shape and a few that nothing matches.
DOCUMENT = [
    "The pressure switch opens when the tank is full .",
    "Flow stops at the valve .",
    "Check the pressure valve before use .",
    "Add 2 pounds of oil to the tank .",
    "You must check the oil level daily .",
    "The tank is full .",
    "Do not leak .",
]


class ExclusionTests(MiniFolder):
    def test_index_rejects_known_ids_and_urls_and_accepts_new_documents(self):
        index = sd.exclusion_index(self.data, sd.load_exclusions(self.work, self.data))
        self.assertIn("listed", sd.document_rejection(index, "gutenberg-1023"))
        self.assertIsNotNone(sd.document_rejection(index, "other-id", "http://gutenberg.org/ebooks/1023/"))      # the same book, another URL
        self.assertIsNotNone(sd.document_rejection(index, "x", "https://www.gutenberg.org/cache/epub/1023/pg1023.txt?x=1#y"))
        self.assertIsNotNone(sd.document_rejection(index, "gutenberg-7194"))                                     # an excluded document
        self.assertIsNotNone(sd.document_rejection(index, "masc:written/x"))
        self.assertIsNotNone(sd.document_rejection(index, "wordsense-ste-probe"))
        self.assertIsNone(sd.document_rejection(index, "army-fm-21-76", "https://armypubs.army.mil/x/fm21-76.pdf"))

    def test_normalize_url_drops_scheme_www_query_fragment_and_trailing_slash(self):
        self.assertEqual("nist.gov/a/b", sd.normalize_url("HTTPS://WWW.NIST.gov/a/b/?q=1#f"))
        self.assertEqual("", sd.normalize_url("not a url"))

    def test_only_http_and_https_urls_are_valid_record_urls_while_matching_stays_scheme_agnostic(self):
        for url in ("http://example.gov/a", "HTTPS://Example.gov/a/", "  https://www.example.gov/a  "):
            self.assertTrue(common.is_http_url(url), url)
        for url in ("ftp://example.gov/a", "file://example.gov/a", "mailto:a@example.gov", "https://", "example.gov/a", ""):
            self.assertFalse(common.is_http_url(url), url)
        self.assertEqual("example.gov/a", sd.normalize_url("ftp://example.gov/a"))                  # a match ignores the scheme

    def test_command_writes_the_index_and_rejects_a_draft_document(self):
        self.write_draft(["gutenberg-1023", "army-fm"])
        code, output = self.cli("exclusions")
        self.assertEqual(1, code)
        self.assertIn("REJECT draft document gutenberg-1023", output)
        self.assertTrue(os.path.exists(os.path.join(self.root, "exclusions.tsv")))
        code, output = self.cli("exclusions", "--check", "new-doc", "https://www.gutenberg.org/ebooks/1023")
        self.assertIn("ACCEPT: new-doc", output)
        self.assertIn("REJECT: https://www.gutenberg.org/ebooks/1023", output)


class HeuristicTests(MiniFolder):
    def hits(self, text):
        return dict(sd.shape_hits(simple_tokens(text), common.read_lexicon(os.path.join(self.data, "lexicon", "lexicon.tsv"))))

    def test_every_shape_is_found_by_its_position_pattern(self):
        self.assertEqual(2, self.hits(DOCUMENT[0])["n-compound-head"])
        self.assertEqual(0, self.hits(DOCUMENT[1])["n-subject-initial"])
        self.assertEqual(0, self.hits(DOCUMENT[2])["v-imperative-initial"])
        self.assertEqual(2, self.hits(DOCUMENT[3])["n-measure"])
        self.assertEqual(2, self.hits(DOCUMENT[4])["v-after-modal"])

    def test_unmatched_sentences_have_no_hit_and_a_subject_is_not_an_imperative(self):
        self.assertEqual({}, self.hits(DOCUMENT[5]))
        self.assertNotIn("v-imperative-initial", self.hits(DOCUMENT[1]))

    def test_lemma_reduces_plural_and_third_person_to_a_listed_noun_verb_base(self):
        lex = common.read_lexicon(os.path.join(self.data, "lexicon", "lexicon.tsv"))
        self.assertEqual("pound", sd.lemma_of("Pounds", lex))
        self.assertEqual("switch", sd.lemma_of("switch", lex))
        self.assertEqual("unknowns", sd.lemma_of("unknowns", lex))

    def test_control_hits_look_for_the_other_class(self):
        lex = common.read_lexicon(os.path.join(self.data, "lexicon", "lexicon.tsv"))
        noun_shape = sd.control_hits(simple_tokens("You must check the oil ."), lex, "n-compound-head", {"check"})
        verb_shape = sd.control_hits(simple_tokens("The check failed ."), lex, "v-after-modal", {"check"})
        self.assertEqual([2], noun_shape)
        self.assertEqual([1], verb_shape)

    def test_measure_needs_a_quantity_frame_and_is_not_a_modifier_or_a_label_target(self):
        self.assertEqual(2, self.hits("Add 2 pounds of oil to the tank .")["n-measure"])
        self.assertEqual(2, self.hits("Add two pounds ." )["n-measure"])
        self.assertEqual(2, self.hits("Add half pound .")["n-measure"])
        self.assertNotIn("n-measure", self.hits("Two machine guns fired ."))                # a compound: `machine` modifies `guns`
        self.assertNotIn("n-measure", self.hits("In 1860 forest planting began ."))
        self.assertNotIn("n-measure", self.hits("Several pounds of oil are needed ."))      # `several` is not a number
        self.assertNotIn("n-measure", self.hits("Replace the Pound with 2 Pounds ."))       # a capitalized word is no measure
        lex = common.read_lexicon(os.path.join(self.data, "lexicon", "lexicon.tsv"))
        self.assertFalse(sd.measure_frame(["See", "Type", "2", "pounds", "now"], 3, lex))   # a label before the number
        self.assertFalse(sd.measure_frame(["Use", "No.", "2", "pounds", "now"], 3, lex))
        self.assertFalse(sd.measure_frame(["Use", "the", "2", "pounds", "now"], 3, lex))    # a determiner before the number
        self.assertFalse(sd.measure_frame(["Use", "2", "pumps", "now"], 2, lex))            # neither a unit nor followed by `of`/`per`
        self.assertTrue(sd.measure_frame(["Use", "2", "pumps", "of", "oil"], 2, lex))
        self.assertTrue(sd.measure_frame(["Use", "2", "pumps", "per", "tank"], 2, lex))
        self.assertFalse(sd.measure_frame(["Use", "2", "pounds", "full", "tank"], 2, lex))  # adjective + noun after
        self.assertTrue(sd.measure_frame(["Use", "2", "pounds", "full", "."], 2, lex))
        self.assertTrue(sd.measure_frame(["Use", "1/2", "pound"], 2, lex))

    def test_modal_needs_the_modal_or_to_right_before_the_word(self):
        self.assertEqual(2, self.hits("They must check it .")["v-after-modal"])
        self.assertEqual(3, self.hits("They want to check it .")["v-after-modal"])
        self.assertNotIn("v-after-modal", self.hits("They must always check it ."))


class NoiseFilterTests(MiniFolder):
    """The cleanliness filter of the candidate stage."""

    def setUp(self):
        super().setUp()
        self.lex = common.read_lexicon(os.path.join(self.data, "lexicon", "lexicon.tsv"))
        self.vocab = set(self.lex) | set("they it open close while then keep are not water supply from sealed some oil rown want".split())

    def reason(self, text):
        return sd.noise_reason(text, simple_tokens(text), self.lex, self.vocab)

    def test_a_clean_sentence_passes(self):
        self.assertIsNone(self.reason("They check the oil level daily while the pump is full."))
        self.assertIsNone(self.reason("They open the valve (not the tank) and check it."))
        self.assertIsNone(self.reason("Check the tank."))                                   # an imperative is a clause

    def test_start_end_and_characters(self):
        self.assertEqual("start-end", self.reason("they check the oil level daily while the pump is full."))
        self.assertEqual("start-end", self.reason("They check the oil level daily while the pump is full"))
        self.assertIsNone(self.reason('They check "the oil level daily while the pump is full."'))
        self.assertEqual("chars", self.reason("They check the oil level daily while the pump is 90\u00b0."))
        self.assertEqual("chars", self.reason("They check the oil level daily while the pump is \ufffdfull."))
        self.assertEqual("chars", self.reason("They check the oil level \u2014 daily while the pump is full."))

    def test_junk_brackets_enumerations_citations_repeats_and_capitals(self):
        self.assertEqual("junk", self.reason("They check the oil level daily while the pump is full!/ ."))
        self.assertEqual("junk", self.reason("They check the oil level daily while the pump is 2!/, full."))
        self.assertEqual("junk", self.reason("They check the 'oil level daily while the pump is full."))
        self.assertEqual("junk", self.reason("They check the oil level daily while the pump is full ,and open."))
        self.assertEqual("junk", self.reason("They check the oil -level daily while the pump is full."))
        self.assertEqual("junk", self.reason("They check the oil level x daily while the pump is full."))
        self.assertEqual("brackets", self.reason("They check the oil level daily while (the pump is full."))
        self.assertEqual("brackets", self.reason('They check the "oil level daily while the pump is full.'))
        self.assertEqual("start-end", self.reason("(a) They check the oil level daily while the pump is full."))
        self.assertEqual("enumeration", self.reason("A. They check the oil level daily while the pump is full."))
        self.assertEqual("enumeration", self.reason("II. They check the oil level daily while the pump is full."))
        self.assertEqual("enumeration", self.reason("They check (a) the oil level daily while the pump is full."))
        self.assertEqual("citation", self.reason("They check the oil level daily, J. Smith, while the pump is full."))
        self.assertEqual("repeat", self.reason("They check the the oil level daily while the pump is full."))
        self.assertEqual("repeat", self.reason("They check the oil, check the oil, check the oil level while the pump is full."))
        self.assertEqual("caps", self.reason("THE OIL LEVEL DAILY WHILE the PUMP IS FULL."))

    def test_titles_captions_and_missing_verbs(self):
        self.assertEqual("title", self.reason("Pressure Relief Valve Oil Tank Level Check."))
        self.assertEqual("short", self.reason("Oil tank level."))
        self.assertEqual("no-verb", self.reason("The oil tank and the pressure valve of the daily relief."))

    def test_unknown_lower_case_words_reject_but_names_numbers_and_hyphen_parts_do_not(self):
        self.assertEqual("oov", self.reason("They check the oil level daily while the rowwn pump is full."))
        self.assertIsNone(self.reason("They check the oil level daily while the Xqzvt pump is full."))     # capitalized, not initial
        self.assertIsNone(self.reason("They check the oil level daily while the pump is full of 12qz."))   # a digit token
        self.assertIsNone(self.reason("They check the oil-tank level daily while the pump is full."))      # every part is known
        self.assertEqual(["rowwn"], sd.oov_tokens(simple_tokens("They check the rowwn pump."), self.vocab))
        self.assertEqual(["Zzzz"], sd.oov_tokens(["Zzzz", "pump"], self.vocab))                            # initial position is checked
        self.assertEqual([], sd.oov_tokens(["pump", "Zzzz"], self.vocab))

    def test_vocabulary_holds_the_word_forms_of_the_training_sets_and_the_lexicon(self):
        vocab = sd.vocabulary_of(self.data)
        self.assertIn("water", vocab)                  # open/train.tsv
        self.assertIn("unrelated", vocab)              # masc/masc.tsv
        self.assertIn("valve", vocab)                  # lexicon
        self.assertNotIn("gasket", vocab)


class CandidateTests(MiniFolder):
    def collect(self, documents, authored=None, **options):
        options.setdefault("clean", False)         # the fixture sentences are token-spaced, not real text: the noise filter has its own tests
        self.write_draft(sorted(documents))
        for source_id, sentences in documents.items():
            self.write_text_doc(source_id, sentences)
        for shape, rows in (authored or {}).items():
            self.write_authored(shape, rows)
        texts = [t for s in documents.values() for t in s] + [r[2] for rows in (authored or {}).values() for r in rows]
        return sd.collect_candidates(self.work, self.data, include_ud=False, tokens_override=self.tokens_for(texts), **options)

    def test_candidates_cover_every_shape_with_ids_and_target_index(self):
        rows, report = self.collect({"doc-a": DOCUMENT})
        shapes = {r["shape"] for r in rows if r["role"] == "target"}
        self.assertEqual(set(sd.SHAPES), shapes)
        self.assertEqual(["c%05d" % n for n in range(1, len(rows) + 1)], [r["cand_id"] for r in rows])
        for row in rows:
            self.assertTrue(0 <= row["target"] < len(row["tokens"]))
        self.assertTrue(any("| n-measure |" in line for line in report))

    def test_sentence_equal_to_a_set_sentence_is_dropped_and_a_mirror_document_is_rejected(self):
        rows, report = self.collect({"doc-a": DOCUMENT + ["The relief valve opens at forty psi under load ."]})
        self.assertNotIn("The relief valve opens at forty psi under load .", [r["text"] for r in rows])
        mirror = ["The relief valve opens at forty psi under load .", "Remove the cover before you start the work .",
                  "Mix the oil and the water in the tank daily .", "Add 2 pounds of oil to the tank ."]
        rows, report = self.collect({"doc-m": mirror})
        self.assertEqual([], rows)
        self.assertTrue(any("rejected doc-m" in line for line in report))

    def test_sentence_sharing_a_probe_5gram_is_dropped_without_printing_probe_text(self):
        copy = "Then seal the pump housing with sealant before use ."
        rows, report = self.collect({"doc-a": DOCUMENT + [copy]})
        self.assertNotIn(copy, [r["text"] for r in rows])
        self.assertTrue(any("probe-5gram 1" in line for line in report))
        self.assertFalse(any("gasket" in line for line in report))

    PROBE_SENTENCE = b"Seal the pump housing with the gasket compound before fitting ."

    def write_probe_bytes(self, id_cell, shape_cell, lemma_cell, gold_cell, target_cell, sentence=PROBE_SENTENCE):
        path = os.path.join(self.data, "eval", "shapes", "probe.tsv")
        row = b"\t".join((id_cell, shape_cell, lemma_cell, gold_cell, target_cell, sentence))
        with open(path, "wb") as handle:
            handle.write(b"id\tshape\tlemma\tgold\ttarget_index\tsentence\n" + row + b"\n")

    def test_probe_labels_shapes_lemmas_and_targets_are_never_read_so_garbage_columns_change_nothing(self):
        documents = {"doc-a": DOCUMENT + ["Then seal the pump housing with sealant before use ."]}
        self.write_probe_bytes(b"p1", b"x", b"seal", b"Verb", b"0")
        keys_a = leakage.held_out_overlap_keys(include_ud=False)
        rows_a, report_a = self.collect(documents)
        self.write_probe_bytes(b"\xff\xfe\x00'\"#{", b"shape\x80\xfe", b"\xc3\x28 lemma", b"\x00\x01\x02", b"not-a-number")
        self.assertEqual(keys_a, leakage.held_out_overlap_keys(include_ud=False))
        rows_b, report_b = self.collect(documents)
        self.assertEqual(rows_a, rows_b)
        self.assertEqual(report_a, report_b)
        self.assertTrue(any("probe-5gram 1" in line for line in report_b))

    def test_braces_around_the_probe_target_do_not_change_the_keys(self):
        self.write_probe_bytes(b"p1", b"x", b"seal", b"Verb", b"0")
        plain = leakage.held_out_overlap_keys(include_ud=False)
        self.write_probe_bytes(b"p1", b"x", b"seal", b"Verb", b"0", b"{Seal} the pump housing with the {gasket} compound before fitting .")
        self.assertEqual(plain, leakage.held_out_overlap_keys(include_ud=False))

    def test_a_probe_row_without_the_sentence_column_is_an_error_not_a_skip(self):
        path = os.path.join(self.data, "eval", "shapes", "probe.tsv")
        with open(path, "wb") as handle:
            handle.write(b"id\tshape\tlemma\tgold\ttarget_index\tsentence\np1\tx\n")
        with self.assertRaises(ValueError):
            leakage.held_out_overlap_keys(include_ud=False)

    def test_an_empty_or_header_only_probe_or_an_empty_sentence_cell_fails_closed_in_every_path(self):
        path = os.path.join(self.data, "eval", "shapes", "probe.tsv")
        header = b"id\tshape\tlemma\tgold\ttarget_index\tsentence\n"
        good = b"p1\tx\tseal\tVerb\t0\tSeal the pump housing with the gasket compound before fitting .\n"
        fixtures = {"empty file": b"", "header only": header, "empty sentence cell": header + good + b"p2\tx\tseal\tVerb\t0\t\n",
                    "braces only": header + good + b"p2\tx\tseal\tVerb\t0\t{}\n", "blank sentence": header + b"p1\tx\tseal\tVerb\t0\t  \n"}
        for name, content in fixtures.items():
            with open(path, "wb") as handle:
                handle.write(content)
            for call in (lambda: leakage.held_out_overlap_keys(include_ud=False), lambda: leakage.probe_sentences(),
                         lambda: leakage.table_sets(), lambda: leakage.evaluation_sets(include_ud=False),
                         lambda: leakage.evaluation_sets(include_ud=False, shapes_dev=False), lambda: check_leakage.check(False)):
                with self.subTest(fixture=name), self.assertRaises(SystemExit) as raised:
                    call()
                self.assertIn("probe", str(raised.exception.code))
        with open(path, "wb") as handle:                                                    # a probe with a sentence in every row is fine
            handle.write(header + good)
        self.assertTrue(leakage.held_out_overlap_keys(include_ud=False))

    def test_the_builder_sees_the_held_out_sets_only_through_the_opaque_key_function(self):
        import ast
        with open(sd.__file__, encoding="utf-8") as handle:
            source = handle.read()
        used = {node.attr for node in ast.walk(ast.parse(source)) if isinstance(node, ast.Attribute)
                and isinstance(node.value, ast.Name) and node.value.id == "leakage"}
        self.assertEqual({"held_out_overlap_keys", "key_overlap", "gram_overlap", "require_sets", "PROBE_GRAM_WORDS"}, used)
        self.assertNotIn("probe.tsv", source)
        filt = sd.LeakageFilter(self.data, include_ud=False)
        self.assertIsInstance(filt.held_out, frozenset)
        self.assertTrue(all(isinstance(k, int) for k in filt.held_out))

    def test_lemma_cap_and_duplicates(self):
        sentences = ["The oil pump %s the tank ." % w for w in ("opens", "fails", "opens")]       # one duplicate sentence
        sentences += ["The tank pump opens ." , "The oil pump opens ." , "The oil pump opens .", "The big pump opens now .",
                      "The tank pump opens here .", "The tank pump opens there ."]
        rows, report = self.collect({"doc-a": sentences}, lemma_cap=2)
        heads = [r for r in rows if r["shape"] == "n-compound-head" and r["role"] == "target"]
        self.assertLessEqual(len(heads), 2)

    def test_authored_rows_need_their_lemma_and_stay_labeled_authored(self):
        authored = {"v-after-modal": [("check", "target", "Workers must check the gauge twice ."),
                                      ("seal", "target", "Nobody mentioned the weather .")]}
        rows, report = self.collect({"doc-a": DOCUMENT}, authored)
        mine = [r for r in rows if r["origin"] == "authored"]
        self.assertEqual(1, len(mine))
        self.assertEqual(sd.AUTHORED_ID, mine[0]["source_id"])
        self.assertEqual(2, mine[0]["target"])
        self.assertTrue(any("does not contain the lemma 'seal'" in line for line in report))

    def test_noise_filter_drops_are_counted_and_the_survivors_reported(self):
        vocab = set(LEXICON) | {"when"}
        good = "The pressure switch opens when the tank is full."
        bad = "The pressure switch opens when the rown tank is full."
        rows, report = self.collect({"doc-a": [good, bad]}, clean=True, vocab=vocab)
        self.assertEqual({good}, {r["text"] for r in rows})
        self.assertTrue(any("noise-oov 1" in line for line in report))
        self.assertTrue(any("clean and kept for the shape rules: 1" in line for line in report))
        rows, report = self.collect({"doc-a": [good, bad]}, clean=False)
        self.assertEqual({good, bad}, {r["text"] for r in rows})

    def test_shape_over_replaces_the_factor_of_one_shape_and_the_control_pool_caps_controls(self):
        sentences = ["Add %d pounds of oil ." % n for n in range(2, 14)]
        rows, _ = self.collect({"doc-a": sentences}, target=2, over=1.0, lemma_cap=100)
        self.assertEqual(2, sum(1 for r in rows if r["shape"] == "n-measure" and r["role"] == "target"))
        rows, _ = self.collect({"doc-a": sentences}, target=2, over=1.0, lemma_cap=100, shape_over={"n-measure": 3.0})
        self.assertEqual(6, sum(1 for r in rows if r["shape"] == "n-measure" and r["role"] == "target"))
        sentences = (["Add %d pounds of oil ." % n for n in (2, 3, 4)] + ["Add %d drops of oil ." % n for n in (2, 3, 4)]
                     + ["They must pound the nail %d ." % n for n in range(4)] + ["They must drop the nail %d ." % n for n in range(4)])
        controls = lambda **o: [r for r in self.collect({"doc-a": sentences}, lemma_cap=100, **o)[0]
                                if r["shape"] == "n-measure" and r["role"] == "control"]
        self.assertEqual(sd.CONTROL_POOL, 100)
        self.assertEqual(4, len(controls()))                         # the whole pool would be 8; at most 2 per lemma (pound, drop)
        self.assertEqual(3, len(controls(control_pool=3)))
        self.assertEqual({"pound", "drop"}, {r["lemma"] for r in controls()})

    def test_shape_lemma_cap_replaces_the_lemma_cap_of_one_shape(self):
        sentences = ["Add %d pounds of oil ." % n for n in range(2, 10)]
        rows, _ = self.collect({"doc-a": sentences}, lemma_cap=2, shape_over={"n-measure": 9.0})
        self.assertEqual(2, sum(1 for r in rows if r["shape"] == "n-measure" and r["role"] == "target"))
        rows, _ = self.collect({"doc-a": sentences}, lemma_cap=2, shape_over={"n-measure": 9.0}, shape_lemma_cap={"n-measure": 5})
        self.assertEqual(5, sum(1 for r in rows if r["shape"] == "n-measure" and r["role"] == "target"))
        self.assertEqual({"n-measure": 5}, sd.parse_shape_lemma_cap(["n-measure=5"]))
        with self.assertRaises(ValueError):
            sd.parse_shape_lemma_cap(["n-measure=0"])

    def test_shape_over_option_parsing(self):
        self.assertEqual({"n-measure": 3.0, "n-subject-initial": 99.0}, sd.parse_shape_over(["n-measure=3", "n-subject-initial=99"]))
        for bad in ("n-measure", "x=3", "n-measure=0.5"):
            with self.assertRaises(ValueError):
                sd.parse_shape_over([bad])

    def test_candidates_command_accepts_the_new_options(self):
        self.write_draft(["doc-a"])
        self.write_text_doc("doc-a", DOCUMENT)
        common.write_text(os.path.join(self.root, "tokens.tsv"), ["text\tsentences\ttokens"] + ["%s\t1\t%s" % (t, " ".join(simple_tokens(t)))
                                                                                          for t in DOCUMENT])
        code, output = self.cli("candidates", "--no-ud", "--shape-over", "n-measure=3", "--shape-over", "v-after-modal=2", "--control-pool", "10")
        self.assertEqual(0, code, output)
        self.assertIn("| n-measure |", output)
        self.assertIn("target limit", output)

    def test_text_without_a_draft_row_is_a_problem(self):
        self.write_draft(["doc-a"])
        self.write_text_doc("doc-a", DOCUMENT)
        self.write_text_doc("doc-b", DOCUMENT)
        rows, report = sd.collect_candidates(self.work, self.data, include_ud=False, tokens_override=self.tokens_for(DOCUMENT), clean=False)
        self.assertTrue(any("doc-b.txt has no row" in line for line in report))

    def test_a_listed_document_is_skipped(self):
        rows, report = self.collect({"gutenberg-1023": DOCUMENT})
        self.assertEqual([], rows)
        self.assertTrue(any("rejected gutenberg-1023" in line for line in report))

    def loaded(self):
        return sd.load_exclusions(self.work, self.data)

    def test_an_excluded_source_is_read_from_its_file_and_a_bad_file_is_an_error(self):
        self.assertEqual({}, self.loaded().reasons)
        path = os.path.join(self.root, sd.EXCLUDED_SOURCES_FILE)
        common.write_text(path, ["source_id\treason", "doc-b\tnot clearly public domain", "", "doc-c\tother reason"])
        self.assertEqual({"doc-b": "not clearly public domain", "doc-c": "other reason"}, self.loaded().reasons)
        common.write_text(path, ["id\treason", "doc-b\tx"])
        with self.assertRaises(ValueError):
            self.loaded()
        common.write_text(path, ["source_id\treason", "doc-b\t "])
        with self.assertRaises(ValueError):
            self.loaded()
        with open(path, "w", encoding="utf-8", newline="") as handle:
            handle.write("source_id\treason\r\ndoc-b\tCRLF file\r\n")
        self.assertEqual({"doc-b": "CRLF file"}, self.loaded().reasons)
        common.write_text(path, ["source_id\treason\tdecided", "doc-c\tr\t2026-11-06", "doc-b\tq\t2026-11-05"])
        self.assertEqual([("doc-c", "r", "2026-11-06"), ("doc-b", "q", "2026-11-05")], self.loaded().work_rows)
        self.assertEqual({"doc-c": "r", "doc-b": "q"}, self.loaded().reasons)
        common.write_text(path, ["source_id\treason\tdecided", "doc-b\tq\t"])
        with self.assertRaises(ValueError):
            self.loaded()

    def test_the_excluded_output_rows_carry_the_draft_url_and_need_a_known_document_and_a_date(self):
        path = os.path.join(self.root, sd.EXCLUDED_SOURCES_FILE)
        self.write_draft(["doc-a", "doc-b"])
        draft = sd.read_draft(self.work)
        self.assertEqual([], self.loaded().output_rows(draft))
        common.write_text(path, ["source_id\treason\tdecided", "doc-b\tr b\t2026-11-06", "doc-a\tr a\t2026-11-05"])
        self.assertEqual([["doc-a", "https://example.gov/doc-a", "r a", "2026-11-05"], ["doc-b", "https://example.gov/doc-b", "r b", "2026-11-06"]],
                         self.loaded().output_rows(draft))
        common.write_text(path, ["source_id\treason\tdecided", "doc-z\tr\t2026-11-06"])
        with self.assertRaises(ValueError):                                         # no draft row, so no url
            self.loaded().output_rows(draft)
        common.write_text(path, ["source_id\treason", "doc-b\tr"])
        with self.assertRaises(ValueError):                                         # no decided date
            self.loaded().output_rows(draft)
        common.write_text(path, ["source_id\treason\tdecided", "doc-b\tr\t2026-11-06"])
        common.write_text(os.path.join(self.root, "sources-draft.tsv"),
                          ["\t".join(sd.SOURCE_COLUMNS), "\t".join(self.source_row("doc-b", "ftp://example.gov/doc-b"))])
        with self.assertRaises(sd.ExcludedSourceError):                             # a url that is no http(s) URL is no record
            self.loaded()

    def test_candidates_skip_every_sentence_of_an_excluded_document_and_count_the_drop(self):
        common.write_text(os.path.join(self.root, sd.EXCLUDED_SOURCES_FILE), ["source_id\treason", "doc-b\tnot clearly public domain"])
        rows, report = self.collect({"doc-a": DOCUMENT, "doc-b": ["The oil pump fails the test .", "Flow stops at the tank ."]})
        self.assertEqual({"doc-a"}, {r["source_id"] for r in rows})
        self.assertTrue(any("excluded-source 2" in line for line in report))
        self.assertFalse(any("rejected doc-b" in line for line in report))
        self.assertTrue(any("sentences considered: %d" % (len(DOCUMENT) + 2) in line for line in report))


class TokenizerInterfaceTests(MiniFolder):
    def test_tokenize_command_calls_the_tokenizer_and_caches_the_tokens(self):
        script = os.path.join(self.temp, "fake_tokenizer.py")
        common.write_text(script, [
            "import re, sys",
            "args = sys.argv[1:]",
            "assert args[0] == 'shapes-tokenize'",
            "src, dst = args[args.index('--in') + 1], args[args.index('--out') + 1]",
            "lines = open(src, encoding='utf-8').read().split('\\n')[:-1]",
            "out = ['%s\\t1\\t%s' % (t, ' '.join(re.findall(r\"n't|\\w+|[^\\w\\s]\", t))) for t in lines]",
            "open(dst, 'w', encoding='utf-8', newline='\\n').write('\\n'.join(out) + '\\n')",
        ])
        self.write_draft(["doc-a"])
        self.write_text_doc("doc-a", ["The pressure switch opens ."])
        self.write_authored("n-measure", [("pound", "target", "Use 2 pounds of flour .")])
        code, output = self.cli("tokenize", "--cmd", "%s %s" % (shlex_quote(sys.executable), shlex_quote(script)))
        self.assertEqual(0, code)
        self.assertIn("2 sentences, 2 newly tokenized", output)
        tokens = sd.read_tokens(self.work)
        self.assertEqual((1, ["The", "pressure", "switch", "opens", "."]), tokens["The pressure switch opens ."])
        code, output = self.cli("tokenize", "--cmd", "false-command-never-run")
        self.assertIn("0 newly tokenized", output)


def shlex_quote(text):
    import shlex
    return shlex.quote(text) if os.name != "nt" else '"%s"' % text


GOLDEN_STANDARD_HASHES = {
    "candidates": "20a5d4ff2c2442e3dc3530ec45e2bf08881e68c580590476ce7c09f0ed44e6fc",
    "dev": "0651494b10b68dd5bf8e0b680ee7e06521516162cd7ff3e091b0875f292c8e7a",
    "sources": "068d811ff14cca28890685e3daf6599d461ce5f20e7b6a64dc7c7057977b5782",
    "decided": "54f33af7fe7311d9db5399895ef44aa1f4ad7d0aac7023652067df2ae419c6ee",
}


class PipelineTests(MiniFolder):
    """export, check-batches, validate-output, decide, sample, build and report on the fixture, with fake taggers."""

    def setUp(self):
        super().setUp()
        authored = {"v-after-modal": [("check", "target", "Workers must check the gauge twice ."),
                                      ("stop", "control", "The stop was loud ."),
                                      ("level", "target", "They must level the floor first .")]}
        self.write_draft(["doc-a"])
        self.write_text_doc("doc-a", DOCUMENT)
        for shape, rows in authored.items():
            self.write_authored(shape, rows)
        texts = DOCUMENT + [r[2] for rows in authored.values() for r in rows]
        self.candidates, _ = sd.collect_candidates(self.work, self.data, include_ud=False, tokens_override=self.tokens_for(texts), clean=False)
        sd.write_candidates(self.work, self.candidates)

    def tag_batches(self, model, rule):
        """Write the output of a fake tagger: rule(token) -> tag (or a tuple tag, confidence, flags)."""
        for name in sorted(adjudicate.read_status(self.work)):
            rows = adjudicate.read_batch(self.work.batch_file(name))
            out = []
            for sid, index, token in rows:
                value = rule(model, sid, int(index), adjudicate.unescape_cell(token))
                tag, flags = (value if isinstance(value, tuple) else (value, "-"))
                out.append((sid, index, token, tag, "high", flags))
            path = os.path.join(self.work.out(model), name + ".tsv")
            adjudicate.write_rows(path, adjudicate.OUTPUT_HEADER, out)

    @staticmethod
    def basic_rule(model, sid, index, token):
        low = token.lower()
        if re.fullmatch(r"\d+", token):
            return "NUM"
        if not token.isalnum():
            return "PUNCT"
        if low in ("of", "to", "in", "at"):
            return "ADP"
        if low in ("the", "a"):
            return "DET"
        if low in ("must", "is", "do", "not"):
            return "AUX"
        if low in ("check", "stop", "flow", "level") or low in ("add", "opens", "fails"):
            return "VERB"
        return "NOUN"

    def export_and_tag(self, rule=None, sol_rule=None):
        self.assertEqual(0, self.cli("export")[0])
        self.tag_batches("opus", rule or self.basic_rule)
        self.tag_batches("sol", sol_rule or rule or self.basic_rule)
        self.assertEqual(0, self.cli("validate-output", "--all")[0])

    def test_export_is_blind_three_columns_with_opaque_ids_and_checks_pass(self):
        self.assertEqual(0, self.cli("export")[0])
        batch = os.path.join(self.root, "batches", "b001.tsv")
        with open(batch, encoding="utf-8") as handle:
            lines = handle.read().split("\n")
        self.assertEqual("id\ttoken_index\ttoken", lines[0])
        self.assertTrue(all(len(line.split("\t")) == 3 for line in lines if line))
        text = "\n".join(lines)
        for secret in ("n-measure", "target", "doc-a", "authored", "shapes-dev"):
            self.assertNotIn(secret, text)
        ids = {line.split("\t")[0] for line in lines[1:] if line}
        self.assertTrue(all(re.fullmatch(r"s[0-9a-f]{6}", i) for i in ids))
        code, output = self.cli("check-batches")
        self.assertEqual(0, code, output)
        with open(batch, "a", encoding="utf-8", newline="") as handle:
            handle.write("s000001\t0\tleak\n")
        self.assertEqual(1, self.cli("check-batches")[0])

    def test_export_refuses_to_overwrite_outputs_of_a_previous_export(self):
        self.export_and_tag()
        code, output = self.cli("export")
        self.assertEqual(1, code)
        self.assertIn("refusing to export", output)
        self.assertEqual(0, self.cli("export", "--force")[0])

    def test_validate_output_rejects_a_bad_tag_and_status_lists_states(self):
        self.assertEqual(0, self.cli("export")[0])
        self.tag_batches("opus", lambda *a: "NOUN")
        self.tag_batches("sol", lambda *a: "NOT-A-TAG")
        code, output = self.cli("validate-output", "--all")
        self.assertEqual(1, code)
        self.assertIn("b001 sol: invalid", output)
        code, output = self.cli("status")
        self.assertIn("opus ok", output)
        self.assertIn("sol invalid", output)

    def decided(self):
        self.assertEqual(0, self.cli("decide")[0])
        return sd.read_decided(self.work)

    def test_decide_keeps_agreed_class_and_drops_target_disagreement_and_class_mismatch(self):
        def opus_rule(model, sid, index, token):
            return "VERB" if token == "switch" else self.basic_rule(model, sid, index, token)   # both call the compound head a verb

        def sol_rule(model, sid, index, token):
            return "NOUN" if token == "Flow" else opus_rule(model, sid, index, token)           # the labelers differ on `Flow`
        self.export_and_tag(opus_rule, sol_rule)
        items, rows = self.decided()
        by_shape = {(i["shape"], i["role"], i["lemma"]): i for i in items}
        self.assertEqual("class-mismatch", by_shape[("n-compound-head", "target", "switch")]["reason"])
        flow = by_shape[("n-subject-initial", "target", "flow")]
        self.assertEqual(("drop", "target-disagreement"), (flow["outcome"], flow["reason"]))
        kept = by_shape[("v-after-modal", "target", "check")]
        self.assertEqual(("keep", "VERB"), (kept["outcome"], kept["agreed_tag"]))
        control = by_shape[("v-after-modal", "control", "stop")]
        self.assertEqual("class-mismatch", control["reason"])           # the fake taggers call every `stop` a VERB: not the intended noun

    def test_frame_mismatch_drops_an_item_whose_neighbor_tag_is_outside_the_frame_and_is_reported(self):
        def rule(model, sid, index, token):
            return "ADJ" if token == "pressure" else self.basic_rule(model, sid, index, token)    # `pressure` is no noun: no compound
        self.export_and_tag(rule)
        items, rows = self.decided()
        by_shape = {(i["shape"], i["role"], i["lemma"]): i for i in items}
        switch = by_shape[("n-compound-head", "target", "switch")]
        self.assertEqual(("drop", "frame-mismatch", "NOUN"), (switch["outcome"], switch["reason"], switch["agreed_tag"]))
        pound = by_shape[("n-measure", "target", "pound")]                                       # `2` is a NUM, `of` follows
        self.assertEqual("keep", pound["outcome"])
        self.assertEqual("keep", by_shape[("v-after-modal", "target", "check")]["outcome"])      # `must` is an AUX
        output = self.cli("decide")[1]
        self.assertIn("frame-mismatch", output)
        self.assertRegex(output, r"\| n-compound-head \| target \| \d+ \| \d+ \| \d+ \| \d+ \| [1-9]\d* \|")

    def test_non_target_disagreement_is_disputed_with_the_first_labelers_tag_and_flags_need_both(self):
        def opus_rule(model, sid, index, token):
            if token == "gauge":
                return ("NOUN", "mentioned")
            return self.basic_rule(model, sid, index, token)

        def sol_rule(model, sid, index, token):
            if token == "gauge":
                return ("PROPN", "mentioned")
            if token == "twice":
                return "ADV"
            return self.basic_rule(model, sid, index, token)
        self.export_and_tag(opus_rule, sol_rule)
        items, rows = self.decided()
        item = next(i for i in items if i["lemma"] == "check" and i["origin"] == "authored")
        self.assertEqual("keep", item["outcome"])
        tagged = rows[item["sent_key"]]
        by_form = {form: (tag, status) for form, tag, status in tagged}
        self.assertEqual(("NOUN", "disputed+mentioned"), by_form["gauge"])
        self.assertEqual(("NOUN", "disputed"), by_form["twice"])          # first labeler's tag (opus gives NOUN to unknown words)
        self.assertEqual(("VERB", "unanimous"), by_form["check"])
        self.assertEqual(2, item["disputed_tokens"])

    def test_sentences_of_a_batch_without_both_outputs_are_incomplete(self):
        self.assertEqual(0, self.cli("export")[0])
        self.tag_batches("opus", self.basic_rule)
        code, output = self.cli("decide")
        self.assertEqual(0, code)
        items, _ = sd.read_decided(self.work)
        self.assertEqual([], items)
        self.assertIn("incomplete sentences (not decided): %d" % len(sd.unique_sentences(sd.read_candidates(self.work))), output)

    def test_build_writes_a_valid_dev_file_and_sources_and_report_counts(self):
        self.export_and_tag()
        self.decided()
        out = os.path.join(self.temp, "out")
        code, output = self.cli("build", "--out-dir", out, "--controls", "4", "--share", "0.5", "--allow-short")
        self.assertEqual(0, code, output)
        header, sentences = common.read_corpus(os.path.join(out, "dev.tsv"))
        self.assertTrue(any(line.startswith("# corpus") and "dev split" in line for line in header))
        self.assertGreater(len(sentences), 0)
        for sentence in sentences:
            self.assertEqual([], sd.sentence_problems(sentence, ["# %s = %s" % (k, v) for k, v in sentence["comments"].items()
                                                                  if k in ("shape", "role", "target", "lemma", "origin")]))
            self.assertEqual("".join(r[0] for r in sentence["rows"]), sentence["text"].replace(" ", ""))
        _, source_rows = adjudicate.read_records(os.path.join(out, "sources.tsv"))
        ids = {r["source_id"] for r in source_rows}
        self.assertEqual({s["source_id"] for s in sentences}, ids)
        self.assertEqual([], adjudicate.read_records(os.path.join(out, sd.EXCLUDED_OUTPUT_FILE))[1])        # nothing excluded: a header-only record file
        authored = next(r for r in source_rows if r["source_id"] == sd.AUTHORED_ID)
        self.assertEqual("project-written", authored["family"])
        self.assertTrue(authored["license_basis"].startswith("Written by the project"))
        self.assertEqual(str(sum(1 for s in sentences if s["source_id"] == sd.AUTHORED_ID)), authored["sentences"])
        doc = next(r for r in source_rows if r["source_id"] == "doc-a")
        self.assertEqual(str(sum(1 for s in sentences if s["source_id"] == "doc-a")), doc["sentences"])
        code, report = self.cli("report", "--out-dir", out)
        self.assertEqual(0, code)
        self.assertIn("| v-after-modal |", report)
        self.assertIn("dropped: class mismatch", report)

    def test_standard_profile_outputs_are_byte_identical_to_the_golden_hashes(self):
        """Regression guard of the hard-tranche work: the standard profile (the default) writes exactly the bytes it wrote before.

        The hashes were taken from the code of main (17a9cf9), before the `--tranche hard` profile existed.
        """
        self.export_and_tag()
        self.decided()
        out = os.path.join(self.temp, "golden-out")
        self.assertEqual(0, self.cli("build", "--out-dir", out, "--controls", "4", "--share", "0.5", "--allow-short")[0])
        found = {}
        for label, path in (("candidates", os.path.join(self.root, "candidates.tsv")), ("dev", os.path.join(out, "dev.tsv")),
                            ("sources", os.path.join(out, "sources.tsv")), ("decided", os.path.join(self.root, "private", "decided.tsv"))):
            with open(path, "rb") as handle:
                found[label] = common.sha256_bytes(handle.read())
        self.assertEqual(GOLDEN_STANDARD_HASHES, found)

    def test_decided_items_of_an_excluded_source_are_dropped_everywhere_and_never_built(self):
        second = ["Check the oil tank before use .", "Flow stops at the tank ."]
        self.write_draft(["doc-a", "doc-b"])
        self.write_text_doc("doc-b", second)
        texts = DOCUMENT + second
        self.candidates, _ = sd.collect_candidates(self.work, self.data, include_ud=False, tokens_override=self.tokens_for(texts), clean=False)
        sd.write_candidates(self.work, self.candidates)
        self.export_and_tag()
        items, _ = self.decided()
        before = {i["cand_id"]: i for i in items if i["source_id"] == "doc-b"}
        self.assertTrue(any(i["outcome"] == "keep" for i in before.values()))
        out = os.path.join(self.temp, "out")
        self.assertEqual(0, self.cli("build", "--out-dir", out, "--controls", "4", "--share", "0.5", "--allow-short")[0])
        self.assertIn("doc-b", {s["source_id"] for s in common.read_corpus(os.path.join(out, "dev.tsv"))[1]})
        common.write_text(os.path.join(self.root, sd.EXCLUDED_SOURCES_FILE),
                          ["source_id\treason\tdecided", "doc-b\tnot clearly public domain\t2026-11-06"])
        items, _ = sd.read_decided(self.work)                                       # the stored `keep` is overridden when read
        mine = [i for i in items if i["source_id"] == "doc-b"]
        self.assertTrue(mine and all((i["outcome"], i["reason"]) == ("drop", sd.EXCLUDED_REASON) for i in mine))
        self.assertEqual(sorted(before), sorted(i["cand_id"] for i in mine))
        self.assertTrue(any(i["outcome"] == "keep" for i in items if i["source_id"] == "doc-a"))
        self.assertEqual(0, sum(1 for i in sd.read_all_decided(self.work)[0] if i["source_id"] == "doc-b" and i["outcome"] == "keep"))
        self.assertEqual(0, sum(n for (shape, lemma), n in sd.lemma_counts_of([self.work]).items() if lemma in ("stops", "tank")))
        self.assertEqual(0, self.cli("build", "--out-dir", out, "--controls", "4", "--share", "0.5", "--allow-short")[0])
        _, sentences = common.read_corpus(os.path.join(out, "dev.tsv"))
        self.assertTrue(sentences and "doc-b" not in {s["source_id"] for s in sentences})
        _, sources = adjudicate.read_records(os.path.join(out, "sources.tsv"))
        self.assertNotIn("doc-b", {r["source_id"] for r in sources})
        header, excluded = adjudicate.read_records(os.path.join(out, sd.EXCLUDED_OUTPUT_FILE))        # the record of the exclusion
        self.assertEqual(list(sd.EXCLUDED_OUTPUT_COLUMNS), list(header))
        self.assertEqual([{"source_id": "doc-b", "url": "https://example.gov/doc-b", "reason": "not clearly public domain",
                           "decided": "2026-11-06"}], [dict(r) for r in excluded])
        self.assertEqual(0, self.cli("sample", "--per-shape", "12", "--controls", "0")[0])
        with open(os.path.join(self.root, "private", "review-sample.md"), encoding="utf-8") as handle:
            self.assertNotIn("oil tank before use", handle.read())
        code, report = self.cli("report", "--out-dir", out)
        self.assertEqual(0, code)
        self.assertIn("dropped: excluded source", report)
        counted = collections.Counter()
        for line in report.splitlines():
            cells = [c.strip() for c in line.strip("|").split("|")]
            if len(cells) == 3 and cells[1] in sd.ROLES and cells[2].isdigit():
                counted[(cells[0], cells[1])] += int(cells[2])
        self.assertEqual(len(mine), sum(counted.values()))
        self.assertEqual(sum(1 for i in mine if i["shape"] == "n-subject-initial" and i["role"] == "target"),
                         counted[("n-subject-initial", "target")])
        code, output = self.cli("decide")                                           # a new decision run writes them as dropped
        self.assertEqual(0, code)
        self.assertIn("excluded-source |", output)
        self.assertTrue(all(i["reason"] == sd.EXCLUDED_REASON for i in sd.read_decided(self.work)[0] if i["source_id"] == "doc-b"))

    def add_second_document(self):
        second = ["Check the oil tank before use .", "Flow stops at the tank ."]
        self.write_draft(["doc-a", "doc-b"])
        self.write_text_doc("doc-b", second)
        self.candidates, _ = sd.collect_candidates(self.work, self.data, include_ud=False, tokens_override=self.tokens_for(DOCUMENT + second),
                                                   clean=False)
        sd.write_candidates(self.work, self.candidates)
        self.export_and_tag()
        self.decided()

    def write_committed_exclusions(self, *rows):
        path = os.path.join(self.data, "eval", "shapes-dev", "excluded-sources.tsv")
        common.write_text(path, ["\t".join(common.EXCLUDED_SOURCES_COLUMNS)] + ["\t".join(r) for r in rows])

    def run_build(self, out, *options):
        err = io.StringIO()
        with contextlib.redirect_stderr(err):
            code, output = self.cli("build", "--out-dir", out, "--controls", "4", "--share", "0.5", "--allow-short", *options)
        return code, output, err.getvalue()

    def refused(self, command, *options):
        err = io.StringIO()
        with contextlib.redirect_stderr(err):
            code, _ = self.cli(*(list(options) + [command]))
        return code, err.getvalue()

    def test_a_missing_committed_record_is_refused_by_every_command_that_loads_it(self):
        self.add_second_document()
        record = os.path.join(self.data, "eval", "shapes-dev", "excluded-sources.tsv")
        out = os.path.join(self.temp, "out")
        os.remove(record)
        with self.assertRaises(ValueError) as caught:
            sd.load_exclusions(self.work, self.data)
        self.assertIn("missing exclusion table", str(caught.exception))
        with self.assertRaises(ValueError):
            sd.build(self.work, out, controls=4, share=0.5, allow_short=True)
        for tranche in ("standard", "hard"):
            for command in sd.EXCLUSION_COMMANDS:
                code, error = self.refused(command, "--tranche", tranche)
                self.assertEqual(1, code, (tranche, command))
                self.assertIn("%s refused" % command, error)
                self.assertIn("missing exclusion table", error)
        self.assertFalse(os.path.exists(out))
        self.assertFalse(os.path.exists(record))                                            # a refusal never creates the record
        self.assertEqual(0, self.cli("status")[0])                                          # commands that read no exclusion are not blocked

    def test_a_header_only_record_is_accepted_and_zero_byte_blank_or_headerless_records_are_refused(self):
        self.add_second_document()
        record = os.path.join(self.data, "eval", "shapes-dev", "excluded-sources.tsv")
        out = os.path.join(self.temp, "out")
        self.assertEqual([], sd.load_exclusions(self.work, self.data).rows)                 # the fixture's record is header only
        self.assertEqual(0, self.run_build(out)[0])
        _, excluded = adjudicate.read_records(os.path.join(out, sd.EXCLUDED_OUTPUT_FILE))   # the standard build writes it, header only, never deletes
        self.assertEqual([], excluded)
        shutil.rmtree(out)
        for content in ("", "\n", "doc-b\thttps://example.gov/doc-b\tnot public domain\t2026-11-06\n", "source_id\treason\tdecided\n"):
            with open(record, "w", encoding="utf-8", newline="") as handle:
                handle.write(content)
            with self.assertRaises(ValueError, msg=repr(content)):
                sd.load_exclusions(self.work, self.data)
            for command in sd.EXCLUSION_COMMANDS:
                self.assertEqual(1, self.refused(command)[0], (command, content))
            code, _, error = self.run_build(out)
            self.assertEqual(1, code)
            self.assertIn("refused", error)
            self.assertFalse(os.path.exists(os.path.join(out, "dev.tsv")))

    def test_the_work_folder_exclusions_are_optional_when_the_committed_record_exists(self):
        self.assertFalse(os.path.exists(os.path.join(self.root, sd.EXCLUDED_SOURCES_FILE)))
        self.assertEqual({}, sd.load_exclusions(self.work, self.data).reasons)
        self.assertEqual([], sd.load_exclusions(self.work, self.data).rows)
        self.assertEqual(0, self.cli("exclusions")[0])
        common.write_text(os.path.join(self.root, sd.EXCLUDED_SOURCES_FILE), [])           # a zero-byte work file is still refused
        with self.assertRaises(ValueError):
            sd.load_exclusions(self.work, self.data)

    def test_a_source_listed_in_the_committed_exclusion_table_by_id_or_by_url_is_never_built(self):
        self.add_second_document()
        out = os.path.join(self.temp, "out")
        self.assertEqual(0, self.run_build(out)[0])                                        # doc-b is used without an exclusion
        self.assertIn("doc-b", {s["source_id"] for s in common.read_corpus(os.path.join(out, "dev.tsv"))[1]})
        shutil.rmtree(out)
        for row in (["doc-b", "https://example.gov/other", "not public domain", "2026-11-06"],
                    ["doc-z", "https://www.example.gov/doc-b/", "same document, other id", "2026-11-06"]):
            self.write_committed_exclusions(row)
            self.assertEqual({"doc-b": row[2]}, {k: v for k, v in sd.load_exclusions(self.work, self.data).reasons.items() if k == "doc-b"})
            self.assertEqual(0, self.run_build(out)[0])                                     # the decided doc-b items are dropped, not built
            _, sentences = common.read_corpus(os.path.join(out, "dev.tsv"))
            self.assertTrue(sentences and "doc-b" not in {s["source_id"] for s in sentences})
            shutil.rmtree(out)
        # the refusal of the build stays as the backstop for a built sentence of an excluded source
        built = [{"source_id": "doc-b"}]
        draft = sd.read_draft(self.work)
        problems = sd.load_exclusions(self.work, self.data).problems(draft, built)
        self.assertTrue(any("doc-b" in p and "excluded" in p for p in problems))

    def test_the_committed_record_alone_excludes_a_source_in_decide_sample_report_lemma_counts_and_build(self):
        second = ["Check the oil tank before use .", "Flow stops at the tank ."]
        self.write_draft(["doc-a", "doc-b"])
        self.write_text_doc("doc-b", second)
        self.candidates, _ = sd.collect_candidates(self.work, self.data, include_ud=False, tokens_override=self.tokens_for(DOCUMENT + second),
                                                   clean=False)
        sd.write_candidates(self.work, self.candidates)
        self.export_and_tag()
        before_all = {i["cand_id"] for i in self.decided()[0] if i["source_id"] == "doc-b"}
        before = {i["cand_id"] for i in self.decided()[0] if i["source_id"] == "doc-b" and i["outcome"] == "keep"}
        self.assertTrue(before)
        out = os.path.join(self.temp, "out")
        self.assertEqual(0, self.run_build(out)[0])
        self.assertIn("doc-b", {s["source_id"] for s in common.read_corpus(os.path.join(out, "dev.tsv"))[1]})
        shutil.rmtree(out)
        self.write_committed_exclusions(["doc-b", "https://example.gov/doc-b", "committed only", "2026-11-06"])
        self.assertFalse(os.path.exists(os.path.join(self.root, sd.EXCLUDED_SOURCES_FILE)))       # the work file is absent
        self.assertEqual({"doc-b": "committed only"}, sd.load_exclusions(self.work, self.data).reasons)
        self.assertEqual({}, {k: v for k, v in sd.load_exclusions(self.work, self.data).reasons.items() if k != "doc-b"})
        code, output = self.cli("decide")                                                           # decide drops them
        self.assertEqual(0, code)
        self.assertIn("excluded-source |", output)
        items, _ = sd.read_decided(self.work)
        mine = [i for i in items if i["source_id"] == "doc-b"]
        self.assertEqual(sorted(before_all), sorted(i["cand_id"] for i in mine if i["reason"] == sd.EXCLUDED_REASON and i["outcome"] == "drop"))
        self.assertEqual(0, sum(n for (shape, lemma), n in sd.lemma_counts_of([self.work]).items() if lemma in ("stops", "tank")))
        self.assertEqual(0, self.cli("sample", "--per-shape", "12", "--controls", "0")[0])
        with open(os.path.join(self.root, "private", "review-sample.md"), encoding="utf-8") as handle:
            self.assertNotIn("oil tank before use", handle.read())
        self.assertEqual(0, self.run_build(out)[0])
        _, sentences = common.read_corpus(os.path.join(out, "dev.tsv"))
        self.assertTrue(sentences and "doc-b" not in {s["source_id"] for s in sentences})
        code, report = self.cli("report", "--out-dir", out)
        self.assertEqual(0, code)
        counted = 0
        for line in report.splitlines():
            cells = [c.strip() for c in line.strip("|").split("|")]
            if len(cells) == 3 and cells[1] in sd.ROLES and cells[2].isdigit():
                counted += int(cells[2])
        self.assertEqual(len(mine), counted)
        for tranche in ("standard", "hard"):                                                       # a missing record refuses sample and report
            for command in ("sample", "report"):
                os.remove(os.path.join(self.data, "eval", "shapes-dev", "excluded-sources.tsv"))
                code, error = self.refused(command, "--tranche", tranche)
                self.assertEqual(1, code, (tranche, command))
                self.assertIn("%s refused" % command, error)
                self.write_committed_exclusions(["doc-b", "https://example.gov/doc-b", "committed only", "2026-11-06"])

    def test_the_builder_indexes_the_committed_exclusion_table_and_keeps_its_rows(self):
        self.add_second_document()
        self.write_committed_exclusions(["doc-b", "https://example.gov/doc-b", "not public domain", "2026-11-06"])
        exclusions = sd.load_exclusions(self.work, self.data)
        self.assertIn(("id", "doc-b"), sd.exclusion_index(self.data, exclusions))
        self.assertIn(("url", "example.gov/doc-b"), sd.exclusion_index(self.data, exclusions))
        rows, report = sd.collect_candidates(self.work, self.data, include_ud=False, tokens_override=self.tokens_for(
            DOCUMENT + ["Check the oil tank before use .", "Flow stops at the tank ."]), clean=False)
        self.assertNotIn("doc-b", {r["source_id"] for r in rows})                            # not a candidate source, W lists nothing
        self.assertTrue(any("excluded-source 2" in line for line in report))                  # the committed exclusion drops its sentences
        self.assertFalse(any("rejected doc-b" in line for line in report))
        common.write_text(os.path.join(self.root, sd.EXCLUDED_SOURCES_FILE), ["source_id\treason\tdecided"])      # W lists nothing
        out = os.path.join(self.temp, "out")
        self.assertEqual(0, self.run_build(out)[0])                                         # doc-b items are decided `keep` but dropped when read
        self.assertNotIn("doc-b", {s["source_id"] for s in common.read_corpus(os.path.join(out, "dev.tsv"))[1]})
        common.write_text(os.path.join(self.data, "eval", "shapes-dev", "excluded-sources.tsv"), ["bad header"])
        with self.assertRaises(ValueError):
            sd.load_exclusions(self.work, self.data)

    def test_the_written_exclusion_record_keeps_the_committed_rows_that_the_work_folder_lacks(self):
        self.write_committed_exclusions(["doc-gone", "https://example.gov/doc-gone", "dropped earlier", "2026-11-01"])
        self.export_and_tag()
        self.decided()
        out = os.path.join(self.temp, "out")
        self.assertEqual(0, self.run_build(out)[0])
        _, excluded = adjudicate.read_records(os.path.join(out, sd.EXCLUDED_OUTPUT_FILE))
        self.assertEqual(["doc-gone"], [r["source_id"] for r in excluded])

    def test_the_written_exclusion_record_is_the_union_and_a_conflicting_url_for_one_id_fails_the_build(self):
        self.add_second_document()
        out = os.path.join(self.temp, "out")
        w_path = os.path.join(self.root, sd.EXCLUDED_SOURCES_FILE)
        common.write_text(w_path, ["source_id\treason\tdecided", "doc-b\tnot clearly public domain\t2026-11-06"])
        self.write_committed_exclusions(["doc-gone", "https://example.gov/doc-gone", "dropped earlier", "2026-11-01"],
                                        ["doc-b", "https://www.example.gov/doc-b/", "older reason", "2026-11-01"])
        self.assertEqual(0, self.run_build(out)[0])                                         # same id, same normalized URL: the union
        _, excluded = adjudicate.read_records(os.path.join(out, sd.EXCLUDED_OUTPUT_FILE))
        self.assertEqual([("doc-b", "https://www.example.gov/doc-b/", "older reason", "2026-11-01"),
                          ("doc-gone", "https://example.gov/doc-gone", "dropped earlier", "2026-11-01")],
                         [(r["source_id"], r["url"], r["reason"], r["decided"]) for r in excluded])
        shutil.rmtree(out)
        self.write_committed_exclusions(["doc-b", "https://example.gov/another-document", "committed URL exclusion", "2026-11-01"])
        code, _, error = self.run_build(out)                                                # same id, other URL: both would be lost
        self.assertEqual(1, code)
        self.assertIn("conflicting", error)
        self.assertIn("doc-b", error)
        self.assertIn("another-document", error)
        self.assertFalse(os.path.exists(os.path.join(out, "dev.tsv")))                      # nothing is written
        with self.assertRaises(sd.ExcludedSourceError):
            sd.load_exclusions(self.work, self.data)

    def test_a_url_exclusion_under_another_id_is_honored_by_decide_sample_report_and_build(self):
        self.add_second_document()
        before = {i["cand_id"] for i in sd.read_decided(self.work)[0] if i["source_id"] == "doc-b"}
        self.assertTrue(before)
        out = os.path.join(self.temp, "out")
        self.assertEqual(0, self.run_build(out)[0])                                         # doc-b is used without an exclusion
        self.assertIn("doc-b", {s["source_id"] for s in common.read_corpus(os.path.join(out, "dev.tsv"))[1]})
        shutil.rmtree(out)
        self.write_committed_exclusions(["doc-z", "https://www.example.gov/doc-b/", "same document, other id", "2026-11-06"])
        mine = [i for i in sd.read_decided(self.work)[0] if i["source_id"] == "doc-b"]      # the stored `keep` is overridden when read
        self.assertEqual(before, {i["cand_id"] for i in mine})
        self.assertTrue(all((i["outcome"], i["reason"]) == ("drop", sd.EXCLUDED_REASON) for i in mine))
        self.assertEqual(0, self.cli("decide")[0])
        mine = [i for i in sd.read_decided(self.work)[0] if i["source_id"] == "doc-b"]
        self.assertEqual(before, {i["cand_id"] for i in mine})
        self.assertTrue(all((i["outcome"], i["reason"]) == ("drop", sd.EXCLUDED_REASON) for i in mine))
        self.assertEqual(0, self.cli("sample", "--per-shape", "12", "--controls", "0")[0])
        with open(os.path.join(self.root, "private", "review-sample.md"), encoding="utf-8") as handle:
            self.assertNotIn("oil tank before use", handle.read())
        self.assertEqual(0, self.run_build(out)[0])
        _, sentences = common.read_corpus(os.path.join(out, "dev.tsv"))
        self.assertTrue(sentences and "doc-b" not in {s["source_id"] for s in sentences})
        _, excluded = adjudicate.read_records(os.path.join(out, sd.EXCLUDED_OUTPUT_FILE))
        self.assertEqual(["doc-z"], [r["source_id"] for r in excluded])
        code, report = self.cli("report", "--out-dir", out)
        self.assertEqual(0, code)
        counted = 0
        for line in report.splitlines():
            cells = [c.strip() for c in line.strip("|").split("|")]
            if len(cells) == 3 and cells[1] in sd.ROLES and cells[2].isdigit():
                counted += int(cells[2])
        self.assertEqual(len(mine), counted)

    def test_a_same_id_different_url_conflict_is_refused_by_every_command_in_both_profiles(self):
        self.add_second_document()
        out = os.path.join(self.temp, "out")
        common.write_text(os.path.join(self.root, sd.EXCLUDED_SOURCES_FILE), ["source_id\treason\tdecided", "doc-b\tnot public domain\t2026-11-06"])
        self.write_committed_exclusions(["doc-b", "https://example.gov/another-document", "committed URL exclusion", "2026-11-01"])
        commands = [("exclusions",), ("candidates", "--no-ud"), ("candidates", "--no-ud", "--round", "r2"), ("decide",), ("decide", "--round", "r2"),
                    ("sample",), ("report",), ("build", "--out-dir", out, "--allow-short"),
                    ("candidates", "--no-ud", "--round", "h3", "--controls-only")]
        for tranche in ("standard", "hard"):
            for command in commands:
                self.assert_refused(("--tranche", tranche) + command)
        self.assertFalse(os.path.exists(out))
        self.assertFalse(os.path.exists(os.path.join(self.root, "rounds")))
        self.assertFalse(os.path.exists(os.path.join(self.root, "exclusions.tsv")))
        with self.assertRaises(sd.ExcludedSourceError):                                     # the function itself fails closed, for any consumer
            sd.load_exclusions(self.work, self.data)
        with self.assertRaises(sd.ExcludedSourceError):
            sd.read_decided(self.work, self.data)
        with self.assertRaises(sd.ExcludedSourceError):
            sd.lemma_counts_of([self.work], data_dir=self.data)
        with self.assertRaises(sd.ExcludedSourceError):
            sd.collect_candidates(self.work, self.data, include_ud=False, tokens_override={}, clean=False)

    def test_a_duplicate_id_within_one_exclusion_table_is_an_error(self):
        self.add_second_document()
        out = os.path.join(self.temp, "out")
        common.write_text(os.path.join(self.root, sd.EXCLUDED_SOURCES_FILE),
                          ["source_id\treason\tdecided", "doc-b\tone\t2026-11-06", "doc-b\ttwo\t2026-11-06"])
        with self.assertRaises(ValueError):
            sd.load_exclusions(self.work, self.data)
        code, _, error = self.run_build(out)
        self.assertEqual(1, code)
        self.assertIn("duplicate source_id doc-b", error)
        os.remove(os.path.join(self.root, sd.EXCLUDED_SOURCES_FILE))
        self.write_committed_exclusions(["doc-b", "https://example.gov/doc-b", "one", "2026-11-06"],
                                        ["doc-b", "https://example.gov/doc-c", "two", "2026-11-06"])
        with self.assertRaises(ValueError):
            sd.load_exclusions(self.work, self.data)
        code, _, error = self.run_build(out)
        self.assertEqual(1, code)
        self.assertIn("duplicate source_id doc-b", error)
        self.assertFalse(os.path.exists(os.path.join(out, "dev.tsv")))

    def test_build_refuses_a_missed_quota_unless_short_is_allowed(self):
        self.export_and_tag()
        self.decided()
        out = os.path.join(self.temp, "out")
        err = io.StringIO()
        with contextlib.redirect_stderr(err):
            code, _ = self.cli("build", "--out-dir", out, "--controls", "4", "--share", "0.5")
        self.assertEqual(1, code)
        self.assertIn("--allow-short", err.getvalue())
        self.assertIn("targets, requested 120", err.getvalue())
        self.assertFalse(os.path.exists(os.path.join(out, "dev.tsv")))
        code, output, _ = self.run_build(out)
        self.assertEqual(0, code)
        self.assertIn("WARNING (allowed by --allow-short)", output)
        self.assertTrue(os.path.exists(os.path.join(out, "dev.tsv")))

    def test_build_is_deterministic(self):
        self.export_and_tag()
        self.decided()
        first, second = os.path.join(self.temp, "o1"), os.path.join(self.temp, "o2")
        self.assertEqual(0, self.cli("build", "--out-dir", first, "--controls", "4", "--share", "0.5", "--allow-short")[0])
        self.assertEqual(0, self.cli("build", "--out-dir", second, "--controls", "4", "--share", "0.5", "--allow-short")[0])
        for name in ("dev.tsv", "sources.tsv"):
            with open(os.path.join(first, name), "rb") as a, open(os.path.join(second, name), "rb") as b:
                self.assertEqual(a.read(), b.read())

    def test_sample_has_twelve_per_shape_where_available_and_swapped_controls_and_the_gate(self):
        self.export_and_tag()
        self.decided()
        code, output = self.cli("sample", "--per-shape", "1", "--controls", "2")
        self.assertEqual(0, code)
        _, keys = adjudicate.read_records(os.path.join(self.root, "private", "review-sample-key.tsv"))
        real = [k for k in keys if k["kind"] == "real"]
        controls = [k for k in keys if k["kind"] == "control"]
        self.assertEqual(len({k["shape"] for k in real}), len(real))        # at most one per shape
        for control in controls:
            self.assertNotEqual(sd.tag_class(control["shown_tag"]), sd.tag_class(control["agreed_tag"]))
        with open(os.path.join(self.root, "private", "review-sample.md"), encoding="utf-8") as handle:
            text = handle.read()
        self.assertNotIn("n-measure", text)                                   # the reviewer is not told the shape
        names = [k["item"] for k in keys]
        good = os.path.join(self.temp, "good.txt")
        with open(good, "w", encoding="utf-8") as handle:
            handle.write("\n".join("%s\t%s\tcite" % (k["item"], "correct" if k["kind"] == "real" else "incorrect") for k in keys) + "\n")
        self.assertEqual(0, self.cli("review-result", "--verdicts", good)[0])
        bad = os.path.join(self.temp, "bad.txt")
        with open(bad, "w", encoding="utf-8") as handle:
            handle.write("\n".join("%s\tincorrect\tcite" % n for n in names) + "\n")
        code, output = self.cli("review-result", "--verdicts", bad)
        self.assertEqual(1, code)
        self.assertIn("STOP", output)


def decided_item(cand_id, shape, role, lemma, origin, key=None):
    return dict(cand_id=cand_id, sent_key=key or "k" + cand_id, shape=shape, role=role, target=0, lemma=lemma, origin=origin,
                source_id="doc" if origin == "real" else sd.AUTHORED_ID, outcome="keep", reason="", opus_tag="NOUN", sol_tag="NOUN",
                agreed_tag="NOUN", disputed_tokens=0)


class SelectionTests(unittest.TestCase):
    def pool(self):
        items, n = [], 0
        for shape in sd.SHAPES:
            for k in range(10):                       # real targets: 10 lemmas x one sentence each... lemma "l0" repeated for the cap
                n += 1
                items.append(decided_item("c%04d" % n, shape, "target", "l0" if k < 6 else "l%d" % k, "real"))
            for k in range(10):
                n += 1
                items.append(decided_item("c%04d" % n, shape, "target", "a%d" % k, "authored"))
            for k in range(6):
                n += 1
                items.append(decided_item("c%04d" % n, shape, "control", "l%d" % (k + 6), "real"))
            for k in range(6):
                n += 1
                items.append(decided_item("c%04d" % n, shape, "control", "l%d" % (k + 6), "authored"))
        return items

    def test_lemma_cap_authored_share_and_no_padding_beyond_the_cap(self):
        targets, controls = sd.select_items(self.pool(), per_shape=20, lemma_cap=4, share=0.30, controls=10)
        for shape in sd.SHAPES:
            mine = [i for i in targets if i["shape"] == shape]
            real = [i for i in mine if i["origin"] == "real"]
            authored = [i for i in mine if i["origin"] == "authored"]
            self.assertLessEqual(max(sum(1 for i in mine if i["lemma"] == lemma) for lemma in {i["lemma"] for i in mine}), 4)
            self.assertEqual(8, len(real))                       # l0 four times + l6..l9 once each
            self.assertLessEqual(len(authored) / float(len(mine)), 0.30)
            self.assertEqual(3, len(authored))                   # floor(8 * 0.3 / 0.7)

    def test_controls_pair_with_target_lemmas_share_cap_and_use_each_sentence_once(self):
        targets, controls = sd.select_items(self.pool(), per_shape=20, lemma_cap=4, share=0.30, controls=10)
        self.assertEqual(10, len(controls))
        for control in controls:
            lemmas = {i["lemma"] for i in targets if i["shape"] == control["shape"]}
            self.assertIn(control["lemma"], lemmas)
        authored = [c for c in controls if c["origin"] == "authored"]
        self.assertLessEqual(len(authored) / float(len(controls)), 0.30)
        keys = [i["sent_key"] for i in targets + controls]
        self.assertEqual(len(keys), len(set(keys)))

    def test_control_quotas_sum_to_the_requested_total_with_the_remainder_first(self):
        self.assertEqual({shape: 20 for shape in sd.SHAPES}, sd.control_quotas(100))
        for controls in (0, 1, 4, 6, 7, 99, 101):
            quotas = sd.control_quotas(controls)
            self.assertEqual(controls, sum(quotas.values()))
            self.assertLessEqual(max(quotas.values()) - min(quotas.values()), 1)
        self.assertEqual([1, 0, 0, 0, 0], [sd.control_quotas(1)[shape] for shape in sd.SHAPES])
        self.assertEqual([2, 1, 1, 1, 1], [sd.control_quotas(6)[shape] for shape in sd.SHAPES])

    def test_the_controls_never_exceed_the_requested_total(self):
        for controls in (1, 6, 7):
            _, chosen = sd.select_items(self.pool(), per_shape=20, lemma_cap=4, share=0.30, controls=controls)
            self.assertLessEqual(len(chosen), controls)
        _, chosen = sd.select_items(self.pool(), per_shape=20, lemma_cap=4, share=0.30, controls=1)
        self.assertEqual(1, len(chosen))
        self.assertEqual(sd.SHAPES[0], chosen[0]["shape"])

    def test_the_number_words_are_the_documented_set(self):
        documented = set("one two three four five six seven eight nine ten eleven twelve fifteen twenty thirty forty fifty sixty "
                         "seventy eighty ninety hundred thousand million".split())
        self.assertEqual(documented, set(sd.NUMBER_WORDS))
        for omitted in "thirteen fourteen sixteen seventeen eighteen nineteen".split():    # a known limit, documented in the README
            self.assertNotIn(omitted, sd.NUMBER_WORDS)

    def test_authored_room(self):
        self.assertEqual(3, sd.authored_room(8, 0.30))

    def test_a_shape_lemma_cap_replaces_the_lemma_cap_for_that_shape_only(self):
        targets, _ = sd.select_items(self.pool(), per_shape=20, lemma_cap=4, share=0.30, controls=10, shape_lemma_cap={"n-measure": 6})
        real = lambda shape: [i for i in targets if i["shape"] == shape and i["origin"] == "real"]
        self.assertEqual(10, len(real("n-measure")))                        # l0 six times + l6..l9 once each
        self.assertEqual(8, len(real("n-compound-head")))
        self.assertEqual(0, sd.authored_room(2, 0.30))
        self.assertEqual(51, sd.authored_room(120, 0.30))

    def test_dropped_items_are_never_selected(self):
        items = self.pool()
        for item in items:
            item["outcome"], item["reason"] = "drop", "class-mismatch"
        targets, controls = sd.select_items(items)
        self.assertEqual(([], []), (targets, controls))


class SentenceRuleTests(unittest.TestCase):
    def sentence(self, tag="NOUN", status="unanimous", target="1", source="doc", origin="real", shape="n-measure", role="target"):
        return {"sent_id": "shapes-dev-0001", "source_id": source, "text": "Use pounds now",
                "rows": [["Use", "VERB", "unanimous"], ["pounds", tag, status], ["now", "ADV", "unanimous"]],
                "extra": ["# shape = " + shape, "# role = " + role, "# target = " + target, "# lemma = pound", "# origin = " + origin]}

    def test_a_valid_sentence_has_no_problem(self):
        self.assertEqual([], sd.sentence_problems(self.sentence()))
        self.assertEqual([], sd.sentence_problems(self.sentence(tag="VERB", role="control")))

    def test_wrong_class_disputed_target_bad_index_and_origin_mismatch_are_problems(self):
        self.assertTrue(sd.sentence_problems(self.sentence(tag="VERB")))
        self.assertTrue(sd.sentence_problems(self.sentence(tag="ADJ")))
        self.assertTrue(sd.sentence_problems(self.sentence(status="disputed")))
        self.assertTrue(sd.sentence_problems(self.sentence(target="7")))
        self.assertTrue(sd.sentence_problems(self.sentence(origin="authored")))
        self.assertTrue(sd.sentence_problems(self.sentence(shape="n-other")))

    def test_decision_rule_functions(self):
        vote = adjudicate.Vote
        item = {"shape": "v-after-modal", "role": "target"}
        self.assertEqual(("keep", "", "VERB"), sd.decide_item(item, vote("VERB", "high", ()), vote("VERB", "low", ())))
        self.assertEqual(("drop", "target-disagreement", ""), sd.decide_item(item, vote("VERB", "high", ()), vote("NOUN", "high", ())))
        self.assertEqual(("drop", "class-mismatch", "NOUN"), sd.decide_item(item, vote("NOUN", "high", ()), vote("NOUN", "high", ())))
        self.assertEqual(("drop", "class-mismatch", "ADJ"), sd.decide_item(item, vote("ADJ", "high", ()), vote("ADJ", "high", ())))
        control = {"shape": "v-after-modal", "role": "control"}
        self.assertEqual(("keep", "", "PROPN"), sd.decide_item(control, vote("PROPN", "high", ()), vote("PROPN", "high", ())))
        self.assertEqual(("NOUN", "disputed+mentioned"), sd.decide_token(vote("NOUN", "high", ("mentioned",)), vote("ADJ", "low", ("mentioned",))))
        self.assertEqual(("NOUN", "unanimous"), sd.decide_token(vote("NOUN", "high", ("mentioned",)), vote("NOUN", "low", ())))


class FrameTests(unittest.TestCase):
    """The post-label frame validation (`frame_mismatch`): only the tags both labelers gave the neighbor tokens count."""

    @staticmethod
    def votes(tags, other=None):
        vote = adjudicate.Vote
        first = {i: vote(t, "high", ()) for i, t in enumerate(tags)}
        second = {i: vote(t, "high", ()) for i, t in enumerate(other or tags)}
        return first, second

    def mismatch(self, shape, target, tags, other=None, role="target"):
        opus, sol = self.votes(tags, other)
        return sd.frame_mismatch({"shape": shape, "role": role, "target": target}, opus, sol)

    def test_compound_head_needs_a_noun_or_name_before(self):
        self.assertFalse(self.mismatch("n-compound-head", 1, ["NOUN", "NOUN", "VERB"]))
        self.assertFalse(self.mismatch("n-compound-head", 1, ["PROPN", "NOUN", "VERB"]))
        self.assertTrue(self.mismatch("n-compound-head", 1, ["ADJ", "NOUN", "VERB"]))
        self.assertTrue(self.mismatch("n-compound-head", 0, ["NOUN", "VERB"]))                 # nothing before

    def test_measure_needs_a_number_before_and_no_modifier_use(self):
        self.assertFalse(self.mismatch("n-measure", 1, ["NUM", "NOUN", "ADP", "NOUN"]))
        self.assertFalse(self.mismatch("n-measure", 1, ["NUM", "NOUN", "PUNCT"]))
        self.assertFalse(self.mismatch("n-measure", 1, ["NUM", "NOUN"]))                       # at the end
        self.assertFalse(self.mismatch("n-measure", 1, ["NUM", "NOUN", "ADJ", "PUNCT"]))       # `4 feet long`
        self.assertTrue(self.mismatch("n-measure", 1, ["DET", "NOUN", "ADP"]))
        self.assertTrue(self.mismatch("n-measure", 1, ["NUM", "NOUN", "NOUN"]))                # `two machine guns`
        self.assertTrue(self.mismatch("n-measure", 1, ["NUM", "NOUN", "PROPN"]))
        self.assertTrue(self.mismatch("n-measure", 1, ["NUM", "NOUN", "ADJ", "NOUN"]))
        self.assertTrue(self.mismatch("n-measure", 0, ["NOUN", "ADP"]))

    def test_initial_shapes_need_index_zero_and_the_modal_shape_an_aux_or_part_before(self):
        self.assertFalse(self.mismatch("n-subject-initial", 0, ["NOUN", "VERB"]))
        self.assertTrue(self.mismatch("n-subject-initial", 1, ["DET", "NOUN", "VERB"]))
        self.assertFalse(self.mismatch("v-imperative-initial", 0, ["VERB", "DET", "NOUN"]))
        self.assertTrue(self.mismatch("v-imperative-initial", 2, ["ADV", "ADV", "VERB"]))
        self.assertFalse(self.mismatch("v-after-modal", 1, ["AUX", "VERB"]))
        self.assertFalse(self.mismatch("v-after-modal", 1, ["PART", "VERB"]))
        self.assertTrue(self.mismatch("v-after-modal", 1, ["ADP", "VERB"]))
        self.assertTrue(self.mismatch("v-after-modal", 1, ["PRON", "VERB"]))

    def test_a_neighbor_the_labelers_disagree_on_never_satisfies_a_frame_and_controls_have_none(self):
        self.assertTrue(self.mismatch("n-compound-head", 1, ["NOUN", "NOUN"], ["ADJ", "NOUN"]))
        self.assertTrue(self.mismatch("v-after-modal", 1, ["AUX", "VERB"], ["ADP", "VERB"]))
        self.assertTrue(self.mismatch("n-measure", 1, ["NUM", "NOUN", "NOUN"], ["NUM", "NOUN", "ADP"]))
        self.assertTrue(self.mismatch("n-measure", 1, ["NUM", "NOUN", "ADJ", "NOUN"], ["NUM", "NOUN", "ADJ", "ADV"]))
        for shape in sd.SHAPES:
            self.assertFalse(self.mismatch(shape, 1, ["ADP", "NOUN", "NOUN"], role="control"))

    LEX = {w: tuple(t.split()) for w, t in {
        "avoid": "VERB", "operate": "VERB NOUN", "stand": "VERB NOUN", "open": "VERB ADJ", "stop": "VERB NOUN", "try": "VERB NOUN",
        "check": "VERB NOUN", "checks": "NOUN VERB", "pass": "VERB NOUN", "process": "VERB NOUN", "focus": "VERB NOUN", "be": "AUX VERB",
        "bring": "VERB", "wring": "VERB", "ring": "VERB NOUN", "feed": "VERB NOUN", "fee": "NOUN", "need": "VERB NOUN", "r": "NOUN",
        "br": "NOUN", "wr": "NOUN", "ne": "NOUN", "fe": "NOUN", "focu": "NOUN", "name": "NOUN VERB", "named": "VERB", "clear": "VERB ADJ",
    }.items()}

    def base_mismatch(self, shape, forms, target, tags):
        opus, sol = self.votes(tags)
        return sd.frame_mismatch({"shape": shape, "role": "target", "target": target}, opus, sol, forms[target], self.LEX)

    def test_base_form_of_reduces_ing_ed_and_s_forms_to_a_listed_stem_and_keeps_real_base_forms(self):
        def base(word):
            return sd.base_form_of(word, self.LEX)
        self.assertEqual("avoid", base("Avoiding"))
        self.assertEqual("operate", base("Operating"))             # stem + e
        self.assertEqual("stop", base("stopped"))                  # doubled consonant
        self.assertEqual("try", base("tried"))
        self.assertEqual("name", base("named"))
        self.assertEqual("check", base("Checks"))                  # lemma_of: third person form
        self.assertEqual("be", base("being"))
        for word in ("Wring", "Bring", "Ring", "Process", "Pass", "Focus", "Feed", "Need", "be", "Check"):
            self.assertEqual(word.lower(), base(word), word)

    def test_the_base_form_rule_drops_gerund_past_and_third_person_targets_of_the_imperative_and_modal_shapes(self):
        for word in ("Wring", "Bring", "Ring", "Process", "Pass", "Focus", "Check"):
            self.assertFalse(self.base_mismatch("v-imperative-initial", [word, "the", "valve"], 0, ["VERB", "DET", "NOUN"]), word)
        for word in ("Avoiding", "Operating", "Standing", "Opened", "Checks"):
            self.assertTrue(self.base_mismatch("v-imperative-initial", [word, "the", "valve"], 0, ["VERB", "DET", "NOUN"]), word)
        for word in ("be", "check", "pass", "focus"):
            self.assertFalse(self.base_mismatch("v-after-modal", ["must", word], 1, ["AUX", "VERB"]), word)
        for word in ("avoiding", "opened", "checks", "being"):
            self.assertTrue(self.base_mismatch("v-after-modal", ["must", word], 1, ["AUX", "VERB"]), word)
        self.assertTrue(self.base_mismatch("v-after-modal", ["to", "avoiding"], 1, ["PART", "VERB"]))

    def test_the_nominal_gerund_rule_drops_ing_forms_of_a_verb_in_the_noun_shapes_only(self):
        cases = (("n-subject-initial", ["Operating", "is", "x"], 0, ["NOUN", "AUX", "ADJ"]),
                 ("n-compound-head", ["land", "clearing"], 1, ["NOUN", "NOUN"]),
                 ("n-measure", ["2", "stopping"], 1, ["NUM", "NOUN"]))
        for shape, forms, target, tags in cases:
            self.assertTrue(self.base_mismatch(shape, forms, target, tags), shape)
            plain = list(forms)
            plain[target] = "Ring" if target == 0 else "ring"          # no verb stem behind the -ing ending: kept
            self.assertFalse(self.base_mismatch(shape, plain, target, tags), shape)
        opus, sol = self.votes(["NOUN", "NOUN"])
        control = {"shape": "n-compound-head", "role": "control", "target": 1}
        self.assertFalse(sd.frame_mismatch(control, opus, sol, "clearing", self.LEX))              # controls are untouched
        self.assertFalse(self.base_mismatch("v-after-modal", ["must", "Wring"], 1, ["AUX", "VERB"]))

    def test_the_base_form_rule_is_only_for_the_two_verb_shapes_targets_and_given_a_form_and_a_lexicon(self):
        opus, sol = self.votes(["NOUN", "VERB"])
        noun_shape = {"shape": "n-subject-initial", "role": "target", "target": 0}
        self.assertFalse(sd.frame_mismatch(noun_shape, opus, sol, "Operation", self.LEX))
        self.assertFalse(sd.frame_mismatch(noun_shape, opus, sol, "Operating"))                    # skipped without a lexicon
        opus, sol = self.votes(["VERB", "DET"])
        control = {"shape": "v-imperative-initial", "role": "control", "target": 0}
        self.assertFalse(sd.frame_mismatch(control, opus, sol, "Avoiding", self.LEX))
        target = {"shape": "v-imperative-initial", "role": "target", "target": 0}
        self.assertFalse(sd.frame_mismatch(target, opus, sol))
        self.assertTrue(sd.frame_mismatch(target, opus, sol, "Avoiding", self.LEX))


class RoundTests(MiniFolder):
    """Incremental rounds: W/rounds/NAME next to the root, shared inputs in W, merged by build, sample and report."""

    ROOT_LINES = ["The oil pump opens the valve.", "The tank seal fails the test."]
    MORE_LINES = ["The oil pump opens the valve.",            # line 1: the same sentence as in the root
                  "The tank seal fails the check.",           # line 2: another sentence on a line the root used
                  "The oil pump fails the test.",             # lemma `pump` has a candidate in the root
                  "The oil check opens the valve.",
                  "The light switch opens the valve."]        # `light` has an adjective reading
    ROUND_OPTIONS = ("candidates", "--no-ud", "--round", "r2", "--only-shapes", "n-compound-head", "--no-controls")

    def setUp(self):
        super().setUp()
        self.write_draft(["doc-a"])
        self.write_text_doc("doc-a", self.ROOT_LINES)
        self.write_tokens(self.ROOT_LINES)
        self.assertEqual(0, self.cli("candidates", "--no-ud")[0])
        self.root_rows = sd.read_candidates(self.work)
        self.write_text_doc("doc-a", self.MORE_LINES)
        self.write_tokens(self.MORE_LINES)
        self.r2 = sd.RoundWork(self.root, "r2")

    def write_tokens(self, texts):
        common.write_text(os.path.join(self.root, "tokens.tsv"), ["text\tsentences\ttokens"] + ["%s\t1\t%s" % (t, " ".join(simple_tokens(t)))
                                                                                          for t in texts])

    def round_texts(self, *options):
        code, output = self.cli(*(self.ROUND_OPTIONS + options))
        self.assertEqual(0, code, output)
        return {r["text"] for r in sd.read_candidates(self.r2)}, output

    def tag(self, work):
        for name in sorted(adjudicate.read_status(work)):
            for model in adjudicate.MODELS:
                out = []
                for sid, index, token in adjudicate.read_batch(work.batch_file(name)):
                    tag = PipelineTests.basic_rule(model, sid, int(index), adjudicate.unescape_cell(token))
                    out.append((sid, index, token, "VERB" if tag == "VERB" else tag, "high", "-"))
                adjudicate.write_rows(os.path.join(work.out(model), name + ".tsv"), adjudicate.OUTPUT_HEADER, out)

    def test_a_round_skips_sentences_and_lines_of_the_root_and_writes_into_its_own_folder(self):
        before = common.read_lines(os.path.join(self.root, "candidates.tsv"))
        texts, output = self.round_texts()
        self.assertEqual({"The oil pump fails the test.", "The oil check opens the valve.", "The light switch opens the valve."}, texts)
        self.assertIn("already-in-earlier-round 2", output)                      # line 1 by its key, line 2 by its (document, line)
        self.assertIn("candidates written to rounds/r2/candidates.tsv", output)
        self.assertEqual(before, common.read_lines(os.path.join(self.root, "candidates.tsv")))
        rows = sd.read_candidates(self.r2)
        self.assertEqual({"n-compound-head"}, {r["shape"] for r in rows})
        self.assertEqual({"target"}, {r["role"] for r in rows})
        self.assertTrue(all(re.fullmatch(r"r2-c\d{5}", r["cand_id"]) for r in rows))
        self.assertTrue(os.path.exists(os.path.join(self.root, "rounds", "r2", "private", "candidates-report.md")))

    def test_new_lemmas_come_before_lemmas_that_the_earlier_rounds_have_and_a_lemma_at_the_cap_gets_none(self):
        texts, _ = self.round_texts("--target", "2", "--over", "1")
        self.assertEqual({"The oil check opens the valve.", "The light switch opens the valve."}, texts)    # `pump` is left for later
        texts, _ = self.round_texts("--lemma-cap", "1")
        self.assertNotIn("The oil pump fails the test.", texts)                  # the root already has one `pump` target
        self.assertEqual(2, len(texts))

    def test_kept_targets_of_a_decided_round_count_against_the_lemma_cap_not_its_candidates(self):
        self.assertEqual(0, self.cli("export")[0])
        self.tag(self.work)
        self.assertEqual(0, self.cli("validate-output", "--all")[0])
        self.assertEqual(0, self.cli("decide")[0])
        counts = sd.lemma_counts_of([self.work])
        self.assertEqual(1, counts[("n-compound-head", "pump")])
        self.assertEqual(1, counts[("n-compound-head", "seal")])

    def test_a_round_skips_an_excluded_document_and_its_decided_kept_targets_no_longer_count_against_the_lemma_cap(self):
        self.assertEqual(0, self.cli("export")[0])
        self.tag(self.work)
        self.assertEqual(0, self.cli("validate-output", "--all")[0])
        self.assertEqual(0, self.cli("decide")[0])
        self.assertEqual(1, sd.lemma_counts_of([self.work])[("n-compound-head", "pump")])
        common.write_text(os.path.join(self.root, sd.EXCLUDED_SOURCES_FILE), ["source_id\treason", "doc-a\tnot clearly public domain"])
        self.assertEqual(0, sum(sd.lemma_counts_of([self.work]).values()))
        code, output = self.cli(*self.ROUND_OPTIONS)
        self.assertEqual(0, code, output)
        self.assertIn("excluded-source %d" % len(self.MORE_LINES), output)
        self.assertEqual([], sd.read_candidates(self.r2))

    def test_a_url_exclusion_under_another_id_drops_the_document_of_every_candidate_round_and_a_conflict_is_refused(self):
        path = os.path.join(self.data, "eval", "shapes-dev", "excluded-sources.tsv")
        common.write_text(path, ["\t".join(common.EXCLUDED_SOURCES_COLUMNS), "doc-z\thttps://www.example.gov/doc-a/\tsame document, other id\t2026-11-06"])
        code, output = self.cli(*self.ROUND_OPTIONS)                                # a round
        self.assertEqual(0, code, output)
        self.assertIn("excluded-source %d" % len(self.MORE_LINES), output)
        self.assertEqual([], sd.read_candidates(self.r2))
        self.assertNotIn("rejected doc-a", output)                                  # the exclusion, not the used-documents index, drops it
        code, output = self.cli("candidates", "--no-ud")                            # the root
        self.assertEqual(0, code, output)
        self.assertIn("excluded-source %d" % len(self.MORE_LINES), output)
        self.assertEqual([], sd.read_candidates(self.work))
        common.write_text(os.path.join(self.root, sd.EXCLUDED_SOURCES_FILE), ["source_id\treason\tdecided", "doc-a\tr\t2026-11-06"])
        common.write_text(path, ["\t".join(common.EXCLUDED_SOURCES_COLUMNS), "doc-a\thttps://example.gov/another-document\tr\t2026-11-06"])
        before = common.read_lines(os.path.join(self.root, "candidates.tsv"))
        self.assert_refused(self.ROUND_OPTIONS)                                     # same id, other URL: refused, nothing written
        self.assert_refused(("candidates", "--no-ud"))
        self.assertEqual(before, common.read_lines(os.path.join(self.root, "candidates.tsv")))

    def test_compound_strict_needs_a_noun_like_modifier(self):
        texts, _ = self.round_texts()
        self.assertIn("The light switch opens the valve.", texts)
        texts, _ = self.round_texts("--compound-strict")
        self.assertEqual({"The oil pump fails the test.", "The oil check opens the valve."}, texts)
        lex = common.read_lexicon(os.path.join(self.data, "lexicon", "lexicon.tsv"))
        tokens = simple_tokens("The light switch opens the valve.")
        self.assertEqual(2, dict(sd.shape_hits(tokens, lex))["n-compound-head"])
        self.assertNotIn("n-compound-head", dict(sd.shape_hits(tokens, lex, compound_strict=True)))
        self.assertEqual(2, dict(sd.shape_hits(simple_tokens("The oil pump opens the valve."), lex, compound_strict=True))["n-compound-head"])

    def test_noun_like_modifier_rule(self):
        lex = {"oil": frozenset(["NOUN"]), "level": frozenset(["NOUN", "VERB"]), "light": frozenset(["ADJ", "NOUN"]),
               "firing": frozenset(["NOUN"]), "building": frozenset(["NOUN", "VERB"]), "heated": frozenset(["NOUN", "VERB"]),
               "one": frozenset(["NOUN", "NUM"]), "can": frozenset(["NOUN", "AUX", "VERB"]), "far": frozenset(["NOUN", "ADV"]),
               "this": frozenset(["NOUN", "DET"]), "run": frozenset(["VERB"]), "the": frozenset(["DET"]), "2": frozenset(["NOUN"]),
               "Army": frozenset(["PROPN"])}
        for good in ("oil", "Oil", "level", "firing"):
            self.assertTrue(sd.noun_like_modifier(lex, good), good)
        for bad in ("light", "building", "heated", "one", "can", "far", "this", "run", "the", "2", "unknown", "Army"):
            self.assertFalse(sd.noun_like_modifier(lex, bad), bad)

    def test_round_option_parsing_names_and_order(self):
        self.assertEqual(("n-measure", "v-after-modal"), sd.parse_only_shapes("n-measure,v-after-modal"))
        self.assertIsNone(sd.parse_only_shapes(None))
        for bad in ("x", "n-measure,x", ","):
            with self.assertRaises(ValueError):
                sd.parse_only_shapes(bad)
        with self.assertRaises(ValueError):
            sd.RoundWork(self.root, "../x")
        names = ["r10", "r2", "r1", "b"]
        self.assertEqual(["b", "r1", "r2", "r10"], sorted(names, key=sd.natural_key))
        for name in ("r1", "r2", "r10"):
            os.makedirs(os.path.join(self.root, "rounds", name, "private"))
        self.assertEqual(["r1", "r2", "r10"], sd.round_names(self.root))
        for name in ("r1", "r2", "r10"):                                             # `candidates.tsv` makes a round count as earlier
            common.write_text(os.path.join(self.root, "rounds", name, "candidates.tsv"), ["x"])
        self.assertEqual(["r1", "r2"], [getattr(w, "name", "root") for w in sd.earlier_works(sd.RoundWork(self.root, "r10"))][1:])
        self.assertEqual([], sd.earlier_works(self.work))

    def test_round_ids_never_use_an_earlier_id_and_are_deterministic(self):
        keys = ["k%d" % n for n in range(50)]
        first = sd.assign_round_ids(keys, 7, set())
        again = sd.assign_round_ids(keys, 7, set())
        self.assertEqual(first, again)
        avoid = set(sorted(first.values())[:20])
        second = sd.assign_round_ids(keys, 7, avoid)
        self.assertEqual(50, len(set(second.values())))
        self.assertFalse(avoid & set(second.values()))
        self.assertNotEqual(sd.round_seed(5, "r2"), sd.round_seed(5, "r3"))
        self.assertEqual(5, sd.round_seed(5, None))

    def test_round_pipeline_exports_checks_decides_and_leaves_the_root_alone(self):
        self.assertEqual(0, self.cli("export")[0])
        root_batches = sorted(os.listdir(self.work.batches))
        self.assertEqual(0, self.cli(*self.ROUND_OPTIONS)[0])
        code, output = self.cli("export", "--round", "r2")
        self.assertEqual(0, code, output)
        for model in adjudicate.MODELS:
            self.assertTrue(os.path.isdir(self.r2.out(model)) and not os.listdir(self.r2.out(model)))
        self.assertEqual(root_batches, sorted(os.listdir(self.work.batches)))
        root_ids = {k["opaque_id"] for k in sd.read_key(self.work)}
        round_ids = {k["opaque_id"] for k in sd.read_key(self.r2)}
        self.assertTrue(round_ids and not root_ids & round_ids)
        self.assertEqual(0, self.cli("check-batches", "--round", "r2")[0])
        self.assertEqual(0, self.cli("check-batches")[0])
        self.tag(self.work)
        self.tag(self.r2)
        code, output = self.cli("validate-output", "--all", "--round", "r2")
        self.assertEqual(0, code, output)
        self.assertIn("opus ok", self.cli("status", "--round", "r2")[1])
        self.assertEqual(0, self.cli("validate-output", "--all")[0])
        code, output = self.cli("export", "--round", "r2")
        self.assertEqual(1, code)                                                    # tagger outputs of the round are protected
        self.assertIn("refusing to export", output)
        self.assertEqual(0, self.cli("decide", "--round", "r2")[0])
        self.assertTrue(os.path.exists(self.r2.private_file("decided.tsv")))
        self.assertFalse(os.path.exists(self.work.private_file("decided.tsv")))
        items, _ = sd.read_decided(self.r2)
        by_lemma = {i["lemma"]: i for i in items}
        self.assertEqual("keep", by_lemma["pump"]["outcome"])
        self.assertEqual("class-mismatch", by_lemma["check"]["reason"])             # the fake taggers call `check` a verb

    def test_build_sample_and_report_merge_the_root_and_the_rounds(self):
        self.assertEqual(0, self.cli("export")[0])
        self.assertEqual(0, self.cli(*self.ROUND_OPTIONS)[0])
        self.assertEqual(0, self.cli("export", "--round", "r2")[0])
        self.tag(self.work)
        self.tag(self.r2)
        self.assertEqual(0, self.cli("decide")[0])
        self.assertEqual(0, self.cli("decide", "--round", "r2")[0])
        out = os.path.join(self.temp, "out")
        code, output = self.cli("build", "--out-dir", out, "--controls", "4", "--share", "0.5", "--lemma-cap", "1", "--allow-short")
        self.assertEqual(0, code, output)
        _, sentences = common.read_corpus(os.path.join(out, "dev.tsv"))
        lemmas = collections.Counter(s["comments"]["lemma"] for s in sentences if s["comments"]["role"] == "target")
        self.assertEqual({"pump": 1, "seal": 1, "switch": 1}, dict(lemmas))         # the lemma cap runs across the root and the round
        self.assertIn("The light switch opens the valve.", {s["text"] for s in sentences})
        code, output = self.cli("sample", "--per-shape", "12", "--controls", "0")
        self.assertEqual(0, code)
        _, keys = adjudicate.read_records(os.path.join(self.root, "private", "review-sample-key.tsv"))
        self.assertEqual(4, len(keys))
        self.assertEqual(2, sum(1 for k in keys if k["cand_id"].startswith("r2-")))   # the sample draws from both folders
        with open(os.path.join(self.root, "private", "review-sample.md"), encoding="utf-8") as handle:
            self.assertIn("The light [[switch]] opens the valve .", handle.read())
        code, report = self.cli("report", "--out-dir", out)
        self.assertEqual(0, code)
        self.assertIn("| n-compound-head | 0 | 1 | 0 |", report)                       # the class mismatch of `check` counts in the report

    def test_without_rounds_the_union_is_the_root_alone(self):
        self.assertEqual(0, self.cli("export")[0])
        self.tag(self.work)
        self.assertEqual(0, self.cli("decide")[0])
        items, rows = sd.read_all_decided(self.work)
        self.assertEqual(sd.read_decided(self.work), (items, rows))
        self.assertEqual([r["cand_id"] for r in self.root_rows], [r["cand_id"] for r in sd.read_all_candidates(self.work)])
        self.assertTrue(all(re.fullmatch(r"c\d{5}", r["cand_id"]) for r in self.root_rows))


class LeakageExtensionTests(MiniFolder):
    def write_dev(self, items):
        extra = [["# shape = n-measure", "# role = target", "# target = 0", "# lemma = x", "# origin = real"] for _ in items]
        corpus_file(os.path.join(self.data, "eval", "shapes-dev", "dev.tsv"), items, extra)

    def test_required_dev_set_missing_fails_loudly_and_the_builder_can_skip_it(self):
        with self.assertRaises(SystemExit):
            leakage.evaluation_sets(include_ud=False)
        self.assertNotIn("shapes-dev", leakage.evaluation_sets(include_ud=False, shapes_dev=False))
        self.write_dev([("doc-z", "A brand new dev sentence about nothing in particular .")])
        with self.assertRaises(SystemExit):                                  # the hard tranche file is required as well
            leakage.evaluation_sets(include_ud=False)
        corpus_file(os.path.join(self.data, "eval", "shapes-dev", "hard.tsv"), [("doc-h", "Another brand new hard sentence about nothing .")],
                    [["# shape = v-after-modal", "# role = target", "# target = 0", "# lemma = x", "# origin = real"]])
        self.assertIn("shapes-dev", leakage.evaluation_sets(include_ud=False))

    def test_held_out_keys_are_an_opaque_set_of_hashes_and_short_text_has_no_gram(self):
        keys = leakage.held_out_overlap_keys(include_ud=False)
        self.assertIsInstance(keys, frozenset)
        self.assertTrue(keys and all(isinstance(k, int) for k in keys))
        self.assertEqual(set(), leakage.gram_hashes("too short here", 5))
        self.assertTrue(leakage.gram_overlap("then SEAL the pump housing with sealant", keys, 5))
        self.assertTrue(leakage.key_overlap("Remove the cover, before you start the work!", keys))       # the key ignores case and punctuation
        self.assertFalse(leakage.key_overlap("A brand new sentence .", keys))
        self.assertFalse(leakage.key_overlap("...", keys))                                              # no letter or digit: never a match
        self.assertEqual(leakage.stable_hash("gram", "a b c d e"), leakage.stable_hash("gram", "a b c d e"))

    def test_dev_check_counts_overlaps_and_names_only_the_failing_dev_sentence(self):
        self.write_dev([("doc-z", "A brand new dev sentence about nothing in particular ."),
                        ("doc-y", "Remove the cover before you start the work ."),
                        ("doc-x", "First seal the pump housing with sealant then rest .")])
        rows, failures, failing = check_leakage.dev_check(include_ud=False)
        self.assertEqual(["s2", "s3"], failing)
        by_set = {row[0]: row for row in rows}
        self.assertEqual(1, by_set["ste-challenge"][3])          # exact sentence
        self.assertEqual(1, by_set["shapes-probe"][5])           # shared 5-gram
        self.assertEqual("-", by_set["open-dev"][5])
        self.assertEqual(2, failures)

    def test_a_clean_dev_set_passes_and_a_shared_document_id_fails(self):
        self.write_dev([("doc-z", "A brand new dev sentence about nothing in particular .")])
        self.assertEqual(0, check_leakage.dev_check(include_ud=False)[1])
        self.write_dev([("gutenberg-99", "A brand new dev sentence about nothing in particular .")])
        self.assertEqual(1, check_leakage.dev_check(include_ud=False)[1])

    def test_a_shared_document_id_alone_names_the_failing_dev_sentence(self):
        self.write_dev([("doc-z", "A brand new dev sentence about nothing in particular ."),
                        ("gutenberg-99", "Another brand new dev sentence with no hit at all .")])
        rows, failures, failing = check_leakage.dev_check(include_ud=False)
        self.assertEqual(["s2"], failing)
        self.assertEqual(1, failures)


class StandardRebuildTests(unittest.TestCase):
    """Optional regression check: rebuild the committed standard tranche from its original work folder and compare the bytes.

    Needs the work folder of the standard tranche (outside the repository): set SHAPES_DEV_STANDARD_WORK to it; skipped otherwise.
    """

    @unittest.skipUnless(os.environ.get("SHAPES_DEV_STANDARD_WORK"), "set SHAPES_DEV_STANDARD_WORK to the standard tranche's work folder")
    def test_the_standard_profile_rebuilds_the_committed_files_byte_for_byte(self):
        committed = os.path.join(common.DATA, "eval", "shapes-dev")
        with tempfile.TemporaryDirectory() as out:
            with contextlib.redirect_stdout(io.StringIO()):
                code = sd.main(["--work", os.environ["SHAPES_DEV_STANDARD_WORK"], "build", "--out-dir", out, "--shape-lemma-cap", "n-measure=12"])
            self.assertEqual(0, code)
            for name in ("dev.tsv", "sources.tsv", "excluded-sources.tsv"):
                with open(os.path.join(out, name), "rb") as made, open(os.path.join(committed, name), "rb") as stored:
                    self.assertEqual(stored.read(), made.read(), name)


class QuotaUnitTests(unittest.TestCase):
    @staticmethod
    def items(shape, role, lemmas, origin="real"):
        return [dict(shape=shape, role=role, lemma=lemma, origin=origin) for lemma in lemmas]

    def full(self):
        targets = [i for n, shape in enumerate(sd.SHAPES) for i in self.items(shape, "target", ["a%d" % n, "b%d" % n, "c%d" % n, "d%d" % n])]
        controls = self.items(sd.SHAPES[0], "control", ["x", "x", "y", "y"])
        return targets, controls

    def problems(self, targets, controls, **options):
        options = dict(dict(per_shape=4, lemma_cap=1, share=0.25, controls=4), **options)
        return sd.quota_problems(targets, controls, **options)

    def test_a_set_that_meets_every_quota_has_no_problem(self):
        targets, controls = self.full()
        self.assertEqual([], self.problems(targets, controls, lemma_cap=1))

    def test_a_short_shape_and_a_short_control_total_are_reported(self):
        targets, controls = self.full()
        found = self.problems(targets[1:], controls[:3])
        self.assertTrue(any(p.startswith("n-compound-head: 3 targets, requested 4") for p in found))
        self.assertTrue(any(p.startswith("controls: 3, requested 4") for p in found))

    def test_a_lemma_over_its_cap_and_a_shape_cap_are_reported(self):
        targets, controls = self.full()
        targets[1]["lemma"] = targets[0]["lemma"]
        self.assertTrue(any("lemma 'a0' has 2 targets, the cap is 1" in p for p in self.problems(targets, controls)))
        self.assertEqual([], self.problems(targets, controls, shape_lemma_cap={"n-compound-head": 2}))
        controls[2]["lemma"] = "x"
        self.assertTrue(any("controls: n-compound-head lemma 'x' has 3 controls, the cap is 2" in p for p in self.problems(targets, controls)))

    def test_the_authored_share_of_a_shape_and_of_the_controls_is_capped(self):
        targets, controls = self.full()
        targets[0]["origin"] = "authored"                                  # 1 of 4 = 25%: allowed at share 0.25
        self.assertEqual([], self.problems(targets, controls))
        targets[1]["origin"] = "authored"
        self.assertTrue(any("n-compound-head: 2 of 4 targets are authored" in p for p in self.problems(targets, controls)))
        targets[1]["origin"] = "real"
        controls[0]["origin"] = controls[1]["origin"] = "authored"
        self.assertTrue(any("controls: 2 of 4 are authored" in p for p in self.problems(targets, controls)))


class ExclusionRecordGateTests(MiniFolder):
    """check_licenses.excluded_source_findings on a fixture shapes-dev folder."""

    def write_set(self, source_id="doc-a", url="https://example.gov/doc-a", excluded=(("doc-x", "https://example.gov/doc-x"),)):
        folder = os.path.join(self.data, "eval", "shapes-dev")
        extra = [["# shape = n-measure", "# role = target", "# target = 0", "# lemma = x", "# origin = real"]]
        corpus_file(os.path.join(folder, "dev.tsv"), [(source_id, "A brand new dev sentence about nothing in particular .")], extra)
        row = [source_id, "us-government", "T", url, "A", "1990", "US federal government work (17 U.S.C. 105): test", "https://example.gov/terms",
               "2026-11-01", "a" * 64, "b" * 64, "1"]
        table_file(os.path.join(folder, "sources.tsv"), sd.SOURCE_COLUMNS, [row])
        table_file(os.path.join(folder, "excluded-sources.tsv"), common.EXCLUDED_SOURCES_COLUMNS,
                   [[i, u, "not public domain", "2026-11-06"] for i, u in excluded])

    def test_a_clean_set_has_no_finding_and_a_header_only_record_is_valid(self):
        self.assertEqual([], check_licenses.excluded_source_findings(self.data))                 # the fixture's record is header only
        self.write_set()
        self.assertEqual([], check_licenses.excluded_source_findings(self.data))

    def test_an_excluded_id_in_dev_or_sources_is_a_finding(self):
        self.write_set("doc-x", "https://example.gov/doc-y")
        found = check_licenses.excluded_source_findings(self.data)
        self.assertTrue(any(f.startswith("eval/shapes-dev/dev.tsv:") and "doc-x" in f for f in found))
        self.assertTrue(any(f.startswith("eval/shapes-dev/sources.tsv:") and "doc-x" in f for f in found))

    def test_an_excluded_url_in_the_sources_is_a_finding_even_under_another_id(self):
        self.write_set("doc-a", "https://www.example.gov/doc-x/")
        found = check_licenses.excluded_source_findings(self.data)
        self.assertEqual(1, len(found))
        self.assertIn("sources.tsv: row doc-a uses the excluded source doc-x", found[0])

    def test_a_missing_or_malformed_record_is_a_finding_when_the_folder_exists(self):
        self.write_set()
        path = os.path.join(self.data, "eval", "shapes-dev", "excluded-sources.tsv")
        for lines in (["source_id\turl\treason"], ["\t".join(common.EXCLUDED_SOURCES_COLUMNS), "doc-x\tnot a url\tr\t2026-11-06"],
                      ["\t".join(common.EXCLUDED_SOURCES_COLUMNS), "doc-x\tftp://example.gov/x\tr\t2026-11-06"],
                      ["\t".join(common.EXCLUDED_SOURCES_COLUMNS), "doc-x\tfile://example.gov/x\tr\t2026-11-06"],
                      ["\t".join(common.EXCLUDED_SOURCES_COLUMNS), "doc-x\thttps://\tr\t2026-11-06"],
                      ["\t".join(common.EXCLUDED_SOURCES_COLUMNS), "doc-x\thttps://example.gov/x\tr\tyesterday"],
                      ["\t".join(common.EXCLUDED_SOURCES_COLUMNS), "doc-x\thttps://example.gov/x\t\t2026-11-06"]):
            common.write_text(path, lines)
            found = check_licenses.excluded_source_findings(self.data)
            self.assertEqual(1, len(found), lines)
            self.assertIn("excluded-sources.tsv", found[0])
        os.remove(path)
        self.assertIn("missing exclusion table", check_licenses.excluded_source_findings(self.data)[0])

    def test_a_zero_byte_blank_or_headerless_record_is_a_finding_and_a_missing_folder_too(self):
        self.write_set()
        path = os.path.join(self.data, "eval", "shapes-dev", "excluded-sources.tsv")
        for content in ("", "\n\n", "doc-x\thttps://example.gov/x\tr\t2026-11-06\n"):
            with open(path, "w", encoding="utf-8", newline="") as handle:
                handle.write(content)
            found = check_licenses.excluded_source_findings(self.data)
            self.assertEqual(1, len(found), repr(content))
            self.assertIn("excluded-sources.tsv", found[0])
        shutil.rmtree(os.path.join(self.data, "eval", "shapes-dev"))
        self.assertIn("missing exclusion table", check_licenses.excluded_source_findings(self.data)[0])


if __name__ == "__main__":
    unittest.main()

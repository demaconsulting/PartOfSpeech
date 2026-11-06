"""Tests of shapes_dev.py (the builder of the hard-shape development set) on a miniature data and work folder.

Run: python -m unittest discover -s tools/data/tests -p "test_shapes_dev.py"   (developer-run; the pipeline needs no model or network)
"""
import contextlib
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
import common
import leakage
import shapes_dev as sd

LEXICON = {
    "valve": "NOUN", "pressure": "NOUN", "relief": "NOUN", "oil": "NOUN", "tank": "NOUN", "level": "NOUN VERB", "daily": "ADV",
    "switch": "NOUN VERB", "check": "NOUN VERB", "drop": "NOUN VERB", "drops": "NOUN VERB", "flow": "NOUN VERB", "seal": "NOUN VERB",
    "stop": "NOUN VERB", "stops": "NOUN VERB", "pound": "NOUN VERB", "pounds": "NOUN VERB", "pump": "NOUN VERB", "pumps": "NOUN VERB",
    "mix": "NOUN VERB", "leak": "NOUN VERB", "test": "NOUN VERB", "tests": "NOUN VERB", "opens": "VERB", "add": "VERB", "fails": "VERB",
    "the": "DET", "a": "DET", "of": "ADP", "to": "ADP PART", "and": "CCONJ", "must": "AUX", "is": "AUX VERB", "full": "ADJ",
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
        index = sd.exclusion_index(self.data)
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


class CandidateTests(MiniFolder):
    def collect(self, documents, authored=None, **options):
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

    def test_text_without_a_draft_row_is_a_problem(self):
        self.write_draft(["doc-a"])
        self.write_text_doc("doc-a", DOCUMENT)
        self.write_text_doc("doc-b", DOCUMENT)
        rows, report = sd.collect_candidates(self.work, self.data, include_ud=False, tokens_override=self.tokens_for(DOCUMENT))
        self.assertTrue(any("doc-b.txt has no row" in line for line in report))

    def test_a_listed_document_is_skipped(self):
        rows, report = self.collect({"gutenberg-1023": DOCUMENT})
        self.assertEqual([], rows)
        self.assertTrue(any("rejected gutenberg-1023" in line for line in report))


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
        self.candidates, _ = sd.collect_candidates(self.work, self.data, include_ud=False, tokens_override=self.tokens_for(texts))
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
        if not token.isalnum():
            return "PUNCT"
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
        code, output = self.cli("build", "--out-dir", out, "--controls", "4", "--share", "0.5")
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

    def test_build_is_deterministic(self):
        self.export_and_tag()
        self.decided()
        first, second = os.path.join(self.temp, "o1"), os.path.join(self.temp, "o2")
        self.assertEqual(0, self.cli("build", "--out-dir", first, "--controls", "4", "--share", "0.5")[0])
        self.assertEqual(0, self.cli("build", "--out-dir", second, "--controls", "4", "--share", "0.5")[0])
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

    def test_authored_room(self):
        self.assertEqual(3, sd.authored_room(8, 0.30))
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


class LeakageExtensionTests(MiniFolder):
    def write_dev(self, items):
        extra = [["# shape = n-measure", "# role = target", "# target = 0", "# lemma = x", "# origin = real"] for _ in items]
        corpus_file(os.path.join(self.data, "eval", "shapes-dev", "dev.tsv"), items, extra)

    def test_required_dev_set_missing_fails_loudly_and_the_builder_can_skip_it(self):
        with self.assertRaises(SystemExit):
            leakage.evaluation_sets(include_ud=False)
        self.assertNotIn("shapes-dev", leakage.evaluation_sets(include_ud=False, shapes_dev=False))
        self.write_dev([("doc-z", "A brand new dev sentence about nothing in particular .")])
        self.assertIn("shapes-dev", leakage.evaluation_sets(include_ud=False))

    def test_probe_ngrams_are_hashes_of_the_probe_and_short_text_has_none(self):
        grams = leakage.probe_ngrams(5)
        self.assertTrue(grams)
        self.assertTrue(all(isinstance(g, int) for g in grams))
        self.assertEqual(set(), leakage.ngrams("too short here", 5))
        self.assertTrue(leakage.ngrams("then SEAL the pump housing with sealant", 5) & grams)

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


if __name__ == "__main__":
    unittest.main()

"""Builder of the two training sources data/compound/ and data/headings/ (Python 3, standard library only; imports the repository's data tools read-only).

`compound` holds about 800 sentences with a noun compound whose head can also be a verb (`[noun] check`, `[noun] split`, `[noun] stand`), and
`headings` about 600 Title Case heading, caption and step-title lines (common-noun content, initial imperatives and a few true proper nouns), both from
US federal public-domain text. They are TRAINING sources (use = training), written for one purpose: a consumer reported confident errors on exactly these
shapes, and the shapes development set measures them (`n-compound-head-ambiguous` and `heading-titlecase`). Everything around the labeling is done by this
tool; it never calls a model, reads no weights and no prediction: the tool imports no tagger, decoder, weights file or score, and a data test scans its
source for that (test_compound_heading_data.py). No score of the WordSense Basic classifier is used either (the development tranche of the compound shape
selected by that "contested band"; this source does not: the pool is every frame hit, in a seeded order).

THE PRE-REGISTERED RULE (committed before any label of these sources exists; the commit is recorded in data/compound/README.md and data/headings/README.md)

1. Documents. Only documents of US federal authorship whose public-domain basis was checked one by one and is written in the `license_basis` cell of
   sources.tsv (17 U.S.C. 105; the archive.org record's own rights statement; NTRS rights record for NASA). No FAA material, nothing in
   data/eval/shapes-dev/excluded-sources.tsv, no share-alike or restricted text, no Wikipedia, no UD text, no sentence supplied by any consumer. A document
   must be disjoint from every training source and every evaluation set: its id, its normalized URL and its archive.org identifier are not in any table of
   `used_index` (training and evaluation sources, all five shapes-dev tables, the imperative source and its exclusions, and every `--extra-table`: the
   earlier sourcing drafts), and the document is rejected as a whole when 3 or more of its sentences (DOCUMENT_KEY_HITS) equal a training sentence, an
   evaluation sentence (the held-out sets enter only as the opaque hash set of `shapes_dev.LeakageFilter`), a sentence of the shapes development set (all five
   tranches), a sentence of data/imperative/imperative.tsv or a sentence of the cleaned text of an earlier sourcing (`--extra-text`): a reprint under another
   id is caught that way. One shared pool of new documents feeds both sources; a document may contribute to both, a sentence to one only (a `compound`
   sentence whose key equals a heading line of the pool is dropped, `equals-a-heading-line`).
2. The pool and the frames (no model; position and lexicon only). `compound`: every sentence of 3 to 30 tokens that passes the unchanged candidate filters of
   shapes_dev.py (`noise_reason` cleanliness, no duplicate, no equal training or evaluation sentence, no 5-gram of the held-out probe) and is not a sentence of
   the shapes development set or of imperative.tsv (`equals-shapes-dev`; a sentence of at least 10 words with at least 80 percent of its word 6-grams in those
   sentences is `near-shapes-dev`), and that has a hit of `shapes_dev_extra.compound_ambiguous_hit` (the frame of `n-compound-head-ambiguous`, unchanged: the
   head is lower case, listed as NOUN and VERB, stands right after a noun-like modifier, is the last noun of the compound and is outside the frame of
   `n-compound-head`). `headings`: every line that `shapes_dev_extra.heading_lines` finds in the OCR text of the documents (a line that stands alone above a
   paragraph) and that passes `shapes_dev_extra.heading_noise_reason`, the full heading filter (2 to 10 tokens, Title Case, no all-capital word, characters,
   no unknown word, and the corrected trailing page number and page range rule `has_trailing_page_number`), the same equal and near-duplicate filters, and that
   has a hit of `shapes_dev_extra.heading_hits`: stratum `proper` (a non-initial capitalized word the lexicon lists as a proper noun only), `imperative` (token
   0 capitalized, listed NOUN and VERB, its own base form, followed by a determiner, an object pronoun, an adverb, a particle or a capitalized noun-capable word) or
   `noun` (a non-initial capitalized word listed NOUN and VERB that ends its noun phrase). A line that has hits of several strata takes the rarest first
   (proper, imperative, noun) and is one candidate only.
3. Candidates (`candidates --source`). `compound`: the frame hits in the seeded order `rank_of(SEED, "compound", sentence key)`, at most 6 per head lemma
   (`--lemma-cap`) and at most 1,500 in all (`--max-candidates`); gate: at least 1,200 candidates and at least 250 head lemmas. `headings`: per stratum in the
   order proper, imperative, noun (inside a stratum the strength of the frame, then the seeded rank `rank_of(SEED, "headings", stratum shape, line key)`), at
   most 4 per lemma over all strata (`--lemma-cap`), at most 25 per document (`--document-cap`) and 1,500 lines in all (`--max-candidates`); gate: at least 1,000 lines
   and every one of the three strata present. No candidate is chosen, ordered or weighted by any model.
4. Labels. Every candidate is tagged from scratch, token by token, by two blind model labelers that follow data/CONVENTIONS.md (including its heading rule), through the
   unchanged commands `shapes_dev.py export`, `check-batches`, `validate-output`, `status` and `decide` with `--round compound` and `--round headings` (the
   batch files carry only an opaque id, a token index and the token; the labelers see no stratum, source, lemma or intended class).
5. Kept items (`build`). `compound`: both labelers agree on every token (`disputed_tokens == 0`), and the unchanged decision of `shapes_dev.decide_item` /
   `frame_mismatch` holds with the head read as a noun (NOUN or PROPN: kind `noun`), OR both labelers read the head as VERB while the token before it is
   a noun they agree on (kind `verb`, a counter-example: the head is an ambiguous word and is a verb here); any other combination is dropped and counted. `headings`:
   every token is agreed, and the stratum word has the tag of the CONVENTIONS rule on which both labelers agree (`shapes_dev.decide_item` with the exact tags: initial imperative
   VERB in its base form, noun word NOUN, proper noun PROPN; a line whose labelers agree on another tag is `class-mismatch` and is dropped, never corrected). Both:
   the convention linter reports no fix-tier violation on the sentence (the item is dropped, no label is changed by hand).
6. Caps (the seeded order `rank_of(SEED, "compound-build" | "headings-build", sentence key)`). `compound`: at most 4 sentences per head lemma over both kinds,
   at most 20 per document, at most 680 of kind `noun` and 120 of kind `verb`, 800 in all, and the share of kind `verb` is at most 15 percent of the file
   (surplus `verb` items, last in the seeded order, are dropped). `headings`: at most 4 per lemma over all strata, at most 25 per document, per stratum
   at most noun 340, imperative 200, proper 60 (600 in all); the strata are taken in the order proper, imperative, noun.
7. Quota gate. `build` refuses (exit code 1, nothing written) unless: `compound` has at least 600 (`--min`, lowered only by `--allow-short`) and at most 800
   sentences, at least 200 head lemmas, at most 4 per lemma and 20 per document, a verb share of at most 15 percent, every token `unanimous` (flags only where both labelers
   agree), the head tagged NOUN or PROPN (kind noun) or VERB (kind verb) and the tokens rejoin to the text; `headings` has at least 150 noun, 100 imperative and 40 proper lines
   (floors lowered only by `--allow-short`, the miss disclosed and never padded), at most the caps of rule 6, at most 4 per lemma and 25 per document, the exact
   tags of rule 5 and every token `unanimous`. Below the floors: stop and report.
8. Hand check (`sample`, `review-result`). 60 kept items per source (`headings`: noun 25, imperative 20, proper 15 by quota, fewer when a stratum is short) and 4 controls whose judged word
   has a swapped tag are drawn at random with a fixed seed (compound 20261111, headings 20261112, one draw each, never repeated) and shown, every token
   with its tag, to a third model (a model review, not a human review). The gate per source: precision of the marked word `correct / (correct + incorrect)` of at least 95 percent
   (3 incorrect of 60 is exactly 95.0 percent and meets it; 4 do not), at most 1 of the 4 controls accepted, no unanswered item. The whole-item precision, the token precision
   and the number of `unclear` verdicts are reported, not gated. A sample is drawn again only for a changed data set, never to repeat a failed review.
9. Format. Ordered by the seeded rank, UTF-8 without BOM, LF, NFC; ids `compound-NNNN` and `headings-NNNN`; the block format of data/open/train.tsv (form, upos,
   status) with the per-sentence lines `# head`, `# lemma`, `# kind` (compound) and `# word`, `# lemma`, `# stratum` (headings); sources.tsv lists only the used documents (12 columns
   of data/eval/gold-v2/sources.tsv, `sentences` recomputed).
10. Amendment after the build (the labels, the decisions and the caps are unchanged; recorded in the RUN-LOG): `build --omit-document ID` (repeatable) removes the
   selected items of a document whose public-domain basis is found not to be verifiable after all (rule 1; for example a record that lists a state or local government body
   as an author). The removal happens after the caps and before the numbering, so nothing is added, nothing is relabeled and the ids stay without a gap; the document is
   listed with the reason `OMIT_REASON` in excluded-documents.tsv and the items are counted as `omitted-document` in the build report. The verb share of rule 6 is applied
   again to what is left (the last verb items in the seeded order are dropped, counted as `verb-share`). An unknown id is refused.

Work folder W (`--work W`, outside the repository), the layout of shapes_dev_extra.py: W/sources-draft.tsv (12 columns), W/text/<source_id>.txt (one sentence per line),
W/raw/<source_id>.txt (OCR text), W/lines/<source_id>.txt (heading lines, `shapes_dev_extra.py lines`), W/tokens.tsv (`shapes_dev_extra.py tokenize`), W/rounds/compound and
W/rounds/headings (candidates.tsv, batches/, private/; the labelers never see private/).

Commands (`--data-dir D` replaces the repository's data folder, for tests; --extra-table and --extra-text as in imperative_data.py):

  python tools/data/compound_heading_data.py --work W exclusions [--extra-table PATH ...] [--check ID_OR_URL ...]
  python tools/data/compound_heading_data.py --work W preflight [--extra-table PATH ...] [--extra-text DIR ...] [--no-ud]      # counts only
  python tools/data/compound_heading_data.py --work W candidates --source compound|headings [--extra-table PATH ...] [--extra-text DIR ...] [--no-ud]
                                                           [--lemma-cap N] [--document-cap N] [--max-candidates N] [--min-candidates N] [--min-lemmas N]
  (then: shapes_dev.py --work W export / check-batches / validate-output / status / decide --round compound|headings, the unchanged blind labeling)
  python tools/data/compound_heading_data.py --work W build --source compound|headings [--out-dir D] [--extra-table PATH ...] [--allow-short] [--omit-document ID ...]
  python tools/data/compound_heading_data.py --work W sample --source compound|headings [--out-dir D] [--seed N]
  python tools/data/compound_heading_data.py --work W review-result --source compound|headings --verdicts FILE ...
  python tools/data/compound_heading_data.py --work W report --source compound|headings [--out-dir D]
"""
import argparse
import collections
import os
import random
import sys

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import adjudicate
import common
import convention_rules
import corrections
import imperative_data
import shapes_dev
import shapes_dev_extra

SOURCES = ("compound", "headings")
SEED = shapes_dev.SEED
FILES = {"compound": "compound.tsv", "headings": "headings.tsv"}
SENT_ID = {"compound": "compound-%04d", "headings": "headings-%04d"}
CAND_PREFIX = {"compound": "cm-", "headings": "hd-"}
SOURCES_NAME, EXCLUDED_NAME, DROPPED_NAME = "sources.tsv", "excluded-documents.tsv", "dropped-sentences.tsv"
COMPOUND_SHAPE = "n-compound-head-ambiguous"
HEADING_SHAPES = ("heading-proper", "heading-imperative", "heading-noun")       # the order in which a line takes its stratum (rarest first)
STRATUM_OF = {"heading-noun": "noun", "heading-imperative": "imperative", "heading-proper": "proper"}
STRATA = ("noun", "imperative", "proper")
EQUALS_SHAPES_DEV, NEAR_SHAPES_DEV = imperative_data.EQUALS_SHAPES_DEV, imperative_data.NEAR_SHAPES_DEV
HEADING_LINE_DROP = "equals-a-heading-line"
OMIT_REASON = "the record lists a state or local government body as an author; a federal-only basis is not verifiable"
OMITTED = "omitted-document"
DOCUMENT_KEY_HITS = shapes_dev.DOCUMENT_KEY_HITS
PRECISION_MIN = shapes_dev.PRECISION_MIN
CONTROLS_ACCEPTED_MAX = shapes_dev.CONTROLS_ACCEPTED_MAX
# compound
CMP_CANDIDATE_LEMMA_CAP, CMP_MAX_CANDIDATES, CMP_MIN_CANDIDATES, CMP_MIN_LEMMAS = 6, 1500, 1200, 250
CMP_LEMMA_CAP, CMP_DOCUMENT_CAP = 4, 20
CMP_MIN, CMP_MAX, CMP_MIN_BUILD_LEMMAS = 600, 800, 200
CMP_NOUN_CAP, CMP_VERB_CAP, CMP_VERB_PERCENT = 680, 120, 15
# headings
HDG_MAX_CANDIDATES, HDG_MIN_CANDIDATES = 1500, 1000
HDG_LEMMA_CAP, HDG_DOCUMENT_CAP = 4, 25
HDG_CAPS = {"noun": 340, "imperative": 200, "proper": 60}
HDG_FLOORS = {"noun": 150, "imperative": 100, "proper": 40}
SAMPLE_SIZE, SAMPLE_CONTROLS = 60, 4
SAMPLE_SEEDS = {"compound": 20261111, "headings": 20261112}
HEADING_SAMPLE_PLAN = (("noun", 25), ("imperative", 20), ("proper", 15))
SAMPLE_KEY_COLUMNS = ("item", "kind", "sent_id", "stratum", "target", "shown_tag", "agreed_tag", "ntokens")
NEW_TRANCHE_TABLES = ("eval/shapes-dev/compound-head-sources.tsv", "eval/shapes-dev/headings-sources.tsv", "eval/shapes-dev/named-state-sources.tsv")
REVIEW_INSTRUCTIONS = [
    "# Review items", "",
    "Each item shows a sentence or heading line with a proposed UPOS tag for every token and one marked word whose tag you judge first.",
    "Read data/CONVENTIONS.md in full first. For each item answer with one line, four tab separated cells, and nothing else:",
    "`item<TAB>verdict<TAB>others<TAB>cite`. The verdict is `correct`, `incorrect` or `unclear` (the conventions do not decide it) for the",
    "MARKED word only. `others` lists the zero-based indexes of any OTHER token whose proposed tag is wrong, separated by",
    "commas, or `-` when none is wrong. The cite names the rule of CONVENTIONS you applied in at most 20 words. Do not look at any other",
    "file and do not search for the sentences.", "",
]
HEADERS = {
    "compound": [
        "# corpus = DemaConsulting.PartOfSpeech compound-head training source (UPOS)",
        "# license = CC0-1.0 for annotations and selection; sentences are US federal government works, see sources.tsv",
        "# columns = form, upos, status",
        "# status = unanimous (two blind model labelers agree on the tag of every token of the sentence)",
        "# judged word = per sentence: head (zero-based index of the head of a noun compound whose head is also a verb), lemma, kind (noun | verb: the head is a noun or a verb here)",
        "# selection = sentences with a noun compound whose head can also be a verb, from US federal text; see README.md and tools/data/compound_heading_data.py",
        "# use = training"],
    "headings": [
        "# corpus = DemaConsulting.PartOfSpeech Title Case heading training source (UPOS)",
        "# license = CC0-1.0 for annotations and selection; lines are US federal government works, see sources.tsv",
        "# columns = form, upos, status",
        "# status = unanimous (two blind model labelers agree on the tag of every token of the line)",
        "# judged word = per line: word (zero-based index of the stratum word), lemma, stratum (noun | imperative | proper)",
        "# selection = Title Case heading, caption and step-title lines from US federal text; see README.md and tools/data/compound_heading_data.py",
        "# use = training"],
}


class BuildError(ValueError):
    """The build or a gate refuses (nothing is written)."""


# ---------------------------------------------------------------- the used-document index and the sentence reference

def used_tables(data_dir, extra_tables=()):
    """The extra tables of the used-document index: `extra_tables` and the three newer shapes-dev tranches' source tables
    (`shapes_dev_extra.used_tables` adds the imperative source's own tables; never the tables of the two sources built here)."""
    tables = list(extra_tables)
    for relative in NEW_TRANCHE_TABLES:
        path = os.path.join(data_dir, *relative.split("/"))
        if os.path.exists(path) and path not in tables:
            tables.append(path)
    return tables


def used_index(data_dir, exclusions, extra_tables=()):
    """{(kind, value): origin} of every document that training, evaluation, the shapes development set (all five tranches), the imperative source
    and the extra tables used or excluded. The tables of data/compound and data/headings are not in it, so that a rebuild does not reject itself."""
    return imperative_data.used_index(data_dir, exclusions, shapes_dev_extra.used_tables(data_dir, used_tables(data_dir, extra_tables)))


class Reference(imperative_data.ShapesDevSentences):
    """The sentences that the new sources must stay disjoint from: every shapes-dev tranche (all five), the imperative source and every `extra_text`
    folder (cleaned text of the earlier sourcing): sentence keys and word 6-grams (`equals-shapes-dev`, `near-shapes-dev`)."""

    def __init__(self, data_dir, extra_text=()):
        super().__init__(data_dir, extra_text)
        path = os.path.join(data_dir, "imperative", "imperative.tsv")
        if os.path.exists(path):
            for sentence in common.read_corpus(path)[1]:
                self.add(sentence["text"])
        self.keys.discard("")


def draft_gate(work, data_dir, extra_tables=()):
    """Fail closed (`shapes_dev_extra.DraftError`) unless W/sources-draft.tsv is a sound list of new documents (the source and license gate of the data builders)."""
    exclusions = shapes_dev.load_exclusions(work, data_dir)
    shapes_dev_extra.require_sound_draft(work, shapes_dev.read_draft(work), used_index(data_dir, exclusions, extra_tables), exclusions)


def _command_exclusions(args):
    work = adjudicate.Work(args.work)
    exclusions = shapes_dev.load_exclusions(work, args.data_dir)
    index = used_index(args.data_dir, exclusions, args.extra_table or ())
    rows = sorted((kind, value, origin) for (kind, value), origin in index.items())
    adjudicate.write_rows(shapes_dev.shared_path(work, "exclusions.tsv"), ("kind", "value", "origin"), rows)
    print("%d entries (%d ids, %d urls, %d archive.org identifiers) written to exclusions.tsv"
          % (len(rows), sum(r[0] == "id" for r in rows), sum(r[0] == "url" for r in rows), sum(r[0] == "ident" for r in rows)))
    problems = []
    for value in args.check or ():
        reason = imperative_data.document_rejection(index, value, value if value.startswith("http") else "")
        print("%s: %s" % ("REJECT" if reason else "ACCEPT", value) + ((" (" + reason + ")") if reason else ""))
        problems += [value] if reason else []
    draft = shapes_dev.read_draft(work)
    problems += imperative_data.draft_problems(work, draft, index, exclusions)
    for message in problems:
        print("REJECT " + message)
    print("%d documents in the draft, %d problems" % (len(draft), len(problems)))
    return 1 if problems else 0


# ---------------------------------------------------------------- the scan and the candidates

def scan_of(work, data_dir, unit, extra_tables=(), extra_text=(), include_ud=True, filt=None, reference=None, lex=None, vocab=None):
    """The `shapes_dev_extra.Scan` of the clean units (`unit` is `sentence` or `heading`) of the new documents: every filter of the sentence candidates of
    shapes_dev.py (cleanliness, duplicates, equal training or evaluation sentences through the opaque held-out keys only, the probe 5-gram rule, used or
    excluded documents, reprints) plus the sentences of all five shapes-dev tranches, of imperative.tsv and of every `extra_text` folder."""
    lex = lex or shapes_dev.lexicon_of(data_dir)
    vocab = vocab or shapes_dev.vocabulary_of(data_dir, lex)
    filt = filt or shapes_dev.LeakageFilter(data_dir, include_ud, new_tranches=True)
    reference = reference or Reference(data_dir, extra_text)
    return shapes_dev_extra.scan_units(work, data_dir, unit, used_tables(data_dir, extra_tables), include_ud, filt=filt, reference=reference,
                                       vocab=vocab, lex=lex)


def heading_line_keys(work):
    """The sentence keys of every heading line of the pool (W/lines), clean or not: a sentence with such a key is never a `compound` candidate."""
    return {shapes_dev.sent_key_of(text) for units in shapes_dev_extra.read_folder(work, "lines").values() for _, text in units}


def compound_pool(scan, lex, seed=SEED, line_keys=frozenset()):
    """([(rank, row)], Counter of drops): the frame hits of the clean sentences in `scan`, one per sentence, as candidate rows (role target)."""
    drops, pool = collections.Counter(), []
    for source_id, number, text, tokens in scan.units:
        i = shapes_dev_extra.compound_ambiguous_hit(tokens, lex)
        if i is None:
            continue
        key = shapes_dev.sent_key_of(text)
        if key in line_keys:
            drops[HEADING_LINE_DROP] += 1
            continue
        pool.append((shapes_dev.rank_of(seed, "compound", key),
                     dict(shape=COMPOUND_SHAPE, role="target", target=i, lemma=shapes_dev.lemma_of(tokens[i], lex), origin="real",
                          source_id=source_id, line=number, text=text, tokens=tokens)))
    return pool, drops


def heading_pool(scan, lex, seed=SEED):
    """{shape: [(sort key, row)]} of the heading hits of the clean lines in `scan` (one per shape and line; the key is (strength, seeded rank))."""
    pool = collections.defaultdict(list)
    for source_id, number, text, tokens in scan.units:
        key = shapes_dev.sent_key_of(text)
        for shape, i, strength in shapes_dev_extra.heading_hits(tokens, lex):
            pool[shape].append(((strength, shapes_dev.rank_of(seed, "headings", shape, key)),
                                dict(shape=shape, role="target", target=i, lemma=shapes_dev.lemma_of(tokens[i], lex), origin="real",
                                     source_id=source_id, line=number, text=text, tokens=tokens)))
    return pool


def cap_compound(pool, lemma_cap=CMP_CANDIDATE_LEMMA_CAP, maximum=CMP_MAX_CANDIDATES, seed=SEED):
    """(rows, per-lemma counts, drops): rule 3 for `compound`: the seeded order, at most `lemma_cap` per lemma and `maximum` in all; ids `cm-cNNNNN` in a second seeded order."""
    rows, lemmas, drops = [], collections.Counter(), collections.Counter()
    for _, row in sorted(pool, key=lambda item: item[0]):
        if lemmas[row["lemma"]] >= lemma_cap:
            drops["beyond-lemma-cap"] += 1
        elif len(rows) >= maximum:
            drops["beyond-total-cap"] += 1
        else:
            lemmas[row["lemma"]] += 1
            rows.append(row)
    rows.sort(key=lambda r: shapes_dev.rank_of(seed, "compound-order", shapes_dev.sent_key_of(r["text"])))
    for number, row in enumerate(rows, 1):
        row["cand_id"] = "%sc%05d" % (CAND_PREFIX["compound"], number)
    return rows, lemmas, drops


def cap_headings(pools, lemma_cap=HDG_LEMMA_CAP, document_cap=HDG_DOCUMENT_CAP, maximum=HDG_MAX_CANDIDATES, seed=SEED):
    """(rows, per-stratum counts, drops): rule 3 for `headings`: strata in the order proper, imperative, noun; a line is one candidate (the rarest stratum
    takes it); at most `lemma_cap` per lemma over all strata, `document_cap` per document and `maximum` lines in all; ids `hd-cNNNNN` in a second seeded order."""
    rows, taken = [], set()
    lemmas, documents, strata, drops = collections.Counter(), collections.Counter(), collections.Counter(), collections.Counter()
    for shape in HEADING_SHAPES:
        for _, row in sorted(pools.get(shape, ()), key=lambda item: item[0]):
            key = shapes_dev.sent_key_of(row["text"])
            if key in taken:
                drops["line-in-a-rarer-stratum"] += 1
            elif len(rows) >= maximum:
                drops["beyond-total-cap"] += 1
            elif lemmas[row["lemma"]] >= lemma_cap:
                drops["beyond-lemma-cap"] += 1
            elif documents[row["source_id"]] >= document_cap:
                drops["beyond-document-cap"] += 1
            else:
                taken.add(key)
                lemmas[row["lemma"]] += 1
                documents[row["source_id"]] += 1
                strata[STRATUM_OF[shape]] += 1
                rows.append(row)
    rows.sort(key=lambda r: shapes_dev.rank_of(seed, "headings-order", shapes_dev.sent_key_of(r["text"])))
    for number, row in enumerate(rows, 1):
        row["cand_id"] = "%sc%05d" % (CAND_PREFIX["headings"], number)
    return rows, strata, drops


def scan_lines(scan, unit):
    """Report lines of a scan: counts only, never the text of any evaluation set."""
    lines = ["%ss considered: %d, clean and kept for the frame: %d" % (unit, scan.considered, len(scan.units)),
             "%ss dropped before the frame: " % unit + (", ".join("%s %d" % item for item in sorted(scan.drops.items())) or "none")]
    lines += ["- rejected %s: %s" % item for item in sorted(scan.rejected.items())]
    lines += ["- PROBLEM: " + p for p in scan.problems]
    return lines


def compound_numbers(scan, lex, seed=SEED, line_keys=frozenset(), lemma_cap=CMP_CANDIDATE_LEMMA_CAP, maximum=CMP_MAX_CANDIDATES):
    """(rows, report lines, counts dict) of the `compound` candidates of a scan."""
    pool, drops = compound_pool(scan, lex, seed, line_keys)
    rows, lemmas, capped = cap_compound(pool, lemma_cap, maximum, seed)
    documents = collections.Counter(r["source_id"] for r in rows)
    counts = dict(clean=len(scan.units), frame_hits=len(pool), pool_lemmas=len({r["lemma"] for _, r in pool}), candidates=len(rows),
                  lemmas=len(lemmas), documents=len(documents), beyond_lemma_cap=capped["beyond-lemma-cap"], beyond_total=capped["beyond-total-cap"],
                  heading_line_drops=drops[HEADING_LINE_DROP])
    report = ["# Candidates (compound)", "", "Counts only; no text of any evaluation set.", ""] + scan_lines(scan, "sentence")
    report += ["", "frame hits: %d over %d head lemmas (%d dropped because the sentence is a heading line of the pool); %d beyond the lemma cap of %d, "
               "%d beyond the total cap of %d; candidates: %d over %d head lemmas in %d documents (lemmas at the cap: %d)"
               % (counts["frame_hits"], counts["pool_lemmas"], counts["heading_line_drops"], counts["beyond_lemma_cap"], lemma_cap,
                  counts["beyond_total"], maximum, counts["candidates"], counts["lemmas"], counts["documents"],
                  sum(1 for n in lemmas.values() if n >= lemma_cap)), "",
               "| head lemma | candidates |", "| --- | --- |"]
    report += ["| %s | %d |" % item for item in sorted(lemmas.items(), key=lambda item: (-item[1], item[0]))[:25]]
    return rows, report, counts


def heading_numbers(scan, lex, seed=SEED, lemma_cap=HDG_LEMMA_CAP, document_cap=HDG_DOCUMENT_CAP, maximum=HDG_MAX_CANDIDATES):
    """(rows, report lines, counts dict) of the `headings` candidates of a scan."""
    pools = heading_pool(scan, lex, seed)
    rows, strata, capped = cap_headings(pools, lemma_cap, document_cap, maximum, seed)
    documents = collections.Counter(r["source_id"] for r in rows)
    counts = dict(clean=len(scan.units), candidates=len(rows), lemmas=len({r["lemma"] for r in rows}), documents=len(documents),
                  pools={STRATUM_OF[s]: len(pools.get(s, ())) for s in HEADING_SHAPES}, strata={s: strata[s] for s in STRATA},
                  pool_lemmas={STRATUM_OF[s]: len({r["lemma"] for _, r in pools.get(s, ())}) for s in HEADING_SHAPES},
                  capped=dict(capped))
    report = ["# Candidates (headings)", "", "Counts only; no text of any evaluation set.", ""] + scan_lines(scan, "line")
    report += ["", "frame hits per stratum (before the caps): " + ", ".join("%s %d (%d lemmas)" % (s, counts["pools"][s], counts["pool_lemmas"][s]) for s in STRATA),
               "dropped at the caps: " + (", ".join("%s %d" % item for item in sorted(capped.items())) or "none"),
               "candidates: %d lines over %d lemmas in %d documents" % (counts["candidates"], counts["lemmas"], counts["documents"]), "",
               "| stratum | candidates |", "| --- | --- |"] + ["| %s | %d |" % (s, strata[s]) for s in STRATA]
    return rows, report, counts


def gate_messages(source, counts, min_candidates=None, min_lemmas=None):
    """[message] for each miss of the size gate (Gate G3): too few candidates or head lemmas (`compound`), too few lines or a missing stratum (`headings`)."""
    found = []
    if source == "compound":
        floor, lemma_floor = min_candidates or CMP_MIN_CANDIDATES, min_lemmas or CMP_MIN_LEMMAS
        if counts["candidates"] < floor:
            found.append("only %d candidates (at least %d needed)" % (counts["candidates"], floor))
        if counts["lemmas"] < lemma_floor:
            found.append("only %d head lemmas (at least %d needed)" % (counts["lemmas"], lemma_floor))
    else:
        floor = min_candidates or HDG_MIN_CANDIDATES
        if counts["candidates"] < floor:
            found.append("only %d lines (at least %d needed)" % (counts["candidates"], floor))
        found += ["stratum %s has no candidate" % s for s in STRATA if not counts["strata"].get(s)]
    return found


def _round_work(args, source):
    round_work = shapes_dev.RoundWork(args.work, source)
    os.makedirs(round_work.private, exist_ok=True)
    return round_work


def _command_preflight(args):
    """Counts only: the pool sizes against the candidate budgets (no candidate is written, nothing is labeled)."""
    work = adjudicate.Work(args.work)
    draft_gate(work, args.data_dir, args.extra_table or ())
    lex = shapes_dev.lexicon_of(args.data_dir)
    vocab = shapes_dev.vocabulary_of(args.data_dir, lex)
    filt = shapes_dev.LeakageFilter(args.data_dir, not args.no_ud, new_tranches=True)
    reference = Reference(args.data_dir, args.extra_text or ())
    sentences = scan_of(work, args.data_dir, "sentence", args.extra_table or (), (), not args.no_ud, filt, reference, lex, vocab)
    headings = scan_of(work, args.data_dir, "heading", args.extra_table or (), (), not args.no_ud, filt, reference, lex, vocab)
    rows, _, c = compound_numbers(sentences, lex, args.seed, heading_line_keys(work))
    hrows, _, h = heading_numbers(headings, lex, args.seed)
    draft = shapes_dev.read_draft(work)
    families = collections.Counter(r["family"] for r in draft)
    print("documents in the draft: %d (%s); rejected as a whole: %d" % (len(draft), ", ".join("%s %d" % f for f in sorted(families.items())),
                                                                        len(sentences.rejected)))
    print("clean sentences: %d; frame hits: %d over %d head lemmas; candidates: %d over %d head lemmas in %d documents"
          % (c["clean"], c["frame_hits"], c["pool_lemmas"], c["candidates"], c["lemmas"], c["documents"]))
    print("clean heading lines: %d; frame hits (noun, imperative, proper): %d, %d, %d (lemmas %d, %d, %d); candidates: %d lines over %d lemmas in %d documents "
          "(noun %d, imperative %d, proper %d)"
          % (h["clean"], h["pools"]["noun"], h["pools"]["imperative"], h["pools"]["proper"], h["pool_lemmas"]["noun"], h["pool_lemmas"]["imperative"],
             h["pool_lemmas"]["proper"], h["candidates"], h["lemmas"], h["documents"], h["strata"]["noun"], h["strata"]["imperative"], h["strata"]["proper"]))
    for message in gate_messages("compound", c) + gate_messages("headings", h):
        print("BUDGET NOT MET: " + message)
    return 0


def _command_candidates(args):
    source = args.source
    work = adjudicate.Work(args.work)
    draft_gate(work, args.data_dir, args.extra_table or ())
    lex = shapes_dev.lexicon_of(args.data_dir)
    if source == "compound":
        scan = scan_of(work, args.data_dir, "sentence", args.extra_table or (), args.extra_text or (), not args.no_ud, lex=lex)
        rows, report, counts = compound_numbers(scan, lex, args.seed, heading_line_keys(work), args.lemma_cap or CMP_CANDIDATE_LEMMA_CAP,
                                                args.max_candidates or CMP_MAX_CANDIDATES)
    else:
        scan = scan_of(work, args.data_dir, "heading", args.extra_table or (), args.extra_text or (), not args.no_ud, lex=lex)
        rows, report, counts = heading_numbers(scan, lex, args.seed, args.lemma_cap or HDG_LEMMA_CAP, args.document_cap or HDG_DOCUMENT_CAP,
                                               args.max_candidates or HDG_MAX_CANDIDATES)
    round_work = _round_work(args, source)
    common.write_text(round_work.private_file("candidates-report.md"), report)
    adjudicate.write_rows(round_work.private_file("rejected-documents.tsv"), ("source_id", "reason"), sorted(scan.rejected.items()))
    shapes_dev.write_candidates(round_work, rows)
    print("\n".join(report[:12]))
    print("%d candidates written to rounds/%s/candidates.tsv" % (len(rows), source))
    missed = gate_messages(source, counts, args.min_candidates, args.min_lemmas)
    for message in missed:
        print("GATE G3 FAILED: " + message, file=sys.stderr)
    return 1 if missed else 0


# ---------------------------------------------------------------- build

def fix_tier_rules(sentences, data_dir, source):
    """{sent_id: [rule id]} of the sentences that the convention linter reports at the fix tier (rule 5).

    The resources of the participle rules come from the correctable corpora of the data folder only, as in `check_conventions.py`: neither these
    sentences nor the lint-only sources (imperative, compound, headings, STE) feed them, so a rebuild gives the same result whether or not an earlier
    output is present; nothing is changed."""
    lint_only = {"imperative", "compound", "headings", "ste"}
    corpora = corrections.load_corpora(data_dir)
    known = [convention_rules.Sentence(name, split, s) for (name, split), corpus in corpora.items() if name not in lint_only for s in corpus.sentences]
    resources = convention_rules.build_resources(known)
    found = {}
    for s in (convention_rules.Sentence(source, "-", t) for t in sentences):
        rules = [p.rule for p in convention_rules.evaluate(s, resources, (convention_rules.FIX,), None)]
        if rules:
            found[s.sent_id] = rules
    return found


def compound_kind(item, rows):
    """(kind, reason): `noun` or `verb` for a decided `compound` item that is kept (rule 5), else (None, the reason it is dropped).

    `rows` are the agreed token rows [form, tag, status] of the sentence; the head is `item["target"]`."""
    head = item["target"]
    disputed = item["disputed_tokens"] or any(common.base_status(row[2]) == "disputed" for row in rows)
    if item["outcome"] == "keep":
        if disputed:
            return None, "disputed-token"
        return ("noun", None) if rows[head][1] in ("NOUN", "PROPN") else (None, "class-mismatch")
    if item["reason"] == "class-mismatch" and item["agreed_tag"] == "VERB":
        before = rows[head - 1] if head > 0 else None
        if before is None or before[1] not in ("NOUN", "PROPN") or common.base_status(before[2]) != "unanimous":
            return None, "frame-mismatch"
        return (None, "disputed-token") if disputed else ("verb", None)
    return None, item["reason"] or "dropped"


def heading_kind(item, rows):
    """(stratum, reason) of a decided `headings` item: the stratum when kept (rule 5), else (None, the reason)."""
    if item["outcome"] != "keep":
        return None, item["reason"] or "dropped"
    if item["disputed_tokens"] or any(common.base_status(row[2]) == "disputed" for row in rows):
        return None, "disputed-token"
    return STRATUM_OF[item["shape"]], None


def select_compound(kept, seed=SEED, lemma_cap=CMP_LEMMA_CAP, document_cap=CMP_DOCUMENT_CAP, maximum=CMP_MAX, noun_cap=CMP_NOUN_CAP,
                    verb_cap=CMP_VERB_CAP, verb_percent=CMP_VERB_PERCENT):
    """(chosen, drops Counter): rule 6 for `compound`. `kept` are dicts with sent_key, lemma, source_id and kind (noun | verb); the seeded order decides.

    The verb items that exceed `verb_percent` of the file (the last ones in the seeded order) are dropped: with N noun items at most
    N * verb_percent // (100 - verb_percent) verb items stay, so that the share of the file is at most `verb_percent`."""
    drops, per_lemma, per_document, per_kind, chosen = collections.Counter(), collections.Counter(), collections.Counter(), collections.Counter(), []
    for item in sorted(kept, key=lambda c: shapes_dev.rank_of(seed, "compound-build", c["sent_key"])):
        kind = item["kind"]
        if per_lemma[item["lemma"]] >= lemma_cap:
            drops["lemma-cap"] += 1
        elif per_document[item["source_id"]] >= document_cap:
            drops["document-cap"] += 1
        elif per_kind[kind] >= (noun_cap if kind == "noun" else verb_cap):
            drops[kind + "-cap"] += 1
        elif len(chosen) >= maximum:
            drops["total-cap"] += 1
        else:
            per_lemma[item["lemma"]] += 1
            per_document[item["source_id"]] += 1
            per_kind[kind] += 1
            chosen.append(item)
    chosen, surplus = trim_verb_share(chosen, verb_percent)
    if surplus:
        drops["verb-share"] += surplus
    return chosen, drops


def trim_verb_share(chosen, verb_percent=CMP_VERB_PERCENT):
    """(chosen, number dropped): the verb items of `chosen` (kept in the seeded order) that exceed `verb_percent` of the file are dropped, the last ones first."""
    noun = sum(1 for c in chosen if c["kind"] == "noun")
    limit = noun * verb_percent // (100 - verb_percent)
    verbs = [c for c in chosen if c["kind"] == "verb"]
    if len(verbs) <= limit:
        return chosen, 0
    surplus = {id(c) for c in verbs[limit:]}
    return [c for c in chosen if id(c) not in surplus], len(surplus)


def select_headings(kept, seed=SEED, lemma_cap=HDG_LEMMA_CAP, document_cap=HDG_DOCUMENT_CAP, caps=None):
    """(chosen, drops Counter): rule 6 for `headings`. `kept` are dicts with sent_key, lemma, source_id and kind (the stratum); strata in the order
    proper, imperative, noun, each in the seeded order, at most `lemma_cap` per lemma over all strata, `document_cap` per document and the cap of the stratum."""
    caps = dict(caps or HDG_CAPS)
    drops, per_lemma, per_document, per_kind, chosen = collections.Counter(), collections.Counter(), collections.Counter(), collections.Counter(), []
    for kind in ("proper", "imperative", "noun"):
        for item in sorted((k for k in kept if k["kind"] == kind), key=lambda c: shapes_dev.rank_of(seed, "headings-build", c["sent_key"])):
            if per_kind[kind] >= caps[kind]:
                drops[kind + "-cap"] += 1
            elif per_lemma[item["lemma"]] >= lemma_cap:
                drops["lemma-cap"] += 1
            elif per_document[item["source_id"]] >= document_cap:
                drops["document-cap"] += 1
            else:
                per_lemma[item["lemma"]] += 1
                per_document[item["source_id"]] += 1
                per_kind[kind] += 1
                chosen.append(item)
    return chosen, drops


def sentence_problems(source, sentence):
    """[message] for one built sentence (the per-sentence rules of rules 5 and 7)."""
    values = sentence["comments"] if "comments" in sentence else dict(part[2:].split(" = ", 1) for part in sentence["extra"])
    rows, name = sentence["rows"], sentence["sent_id"]
    index_name, kind_name = ("head", "kind") if source == "compound" else ("word", "stratum")
    problems = []
    if not values.get(index_name, "").isdigit() or int(values[index_name]) >= len(rows):
        return ["%s: `%s` is not a token index" % (name, index_name)]
    tag, kind = rows[int(values[index_name])][1], values.get(kind_name)
    expected = {"noun": ("NOUN", "PROPN"), "verb": ("VERB",)} if source == "compound" else {"noun": ("NOUN",), "imperative": ("VERB",), "proper": ("PROPN",)}
    if kind not in expected or tag not in expected[kind]:
        problems.append("%s: the judged word is tagged %s for %s %s" % (name, tag, kind_name, kind))
    if any(common.base_status(row[2]) != "unanimous" for row in rows):
        problems.append("%s: a token is not unanimous" % name)
    if "".join(row[0] for row in rows) != sentence["text"].replace(" ", ""):
        problems.append("%s: the tokens do not rejoin to the text" % name)
    if source == "headings" and kind == "imperative" and int(values[index_name]) != 0:
        problems.append("%s: an imperative line must be judged at token 0" % name)
    return problems


def quota_problems(source, sentences, minimum=None, floors=None, document_cap=None, lemma_cap=None):
    """[message] for every miss of the quota gate (rule 7) of built sentences (dicts of `read_corpus` / `final_sentence`); `minimum` and `floors` are the waivable ones."""
    found = []
    comments = [s["comments"] if "comments" in s else dict(part[2:].split(" = ", 1) for part in s["extra"]) for s in sentences]
    lemmas = collections.Counter(c["lemma"] for c in comments)
    documents = collections.Counter(s["source_id"] for s in sentences)
    if source == "compound":
        minimum = CMP_MIN if minimum is None else minimum
        lemma_cap, document_cap = lemma_cap or CMP_LEMMA_CAP, document_cap or CMP_DOCUMENT_CAP
        if len(sentences) < minimum:
            found.append("%d sentences, at least %d needed" % (len(sentences), minimum))
        if len(sentences) > CMP_MAX:
            found.append("%d sentences, at most %d allowed" % (len(sentences), CMP_MAX))
        if minimum and len(lemmas) < CMP_MIN_BUILD_LEMMAS:
            found.append("%d head lemmas, at least %d needed" % (len(lemmas), CMP_MIN_BUILD_LEMMAS))
        verbs = sum(1 for c in comments if c["kind"] == "verb")
        if verbs * 100 > CMP_VERB_PERCENT * len(sentences):
            found.append("%d verb items of %d, at most %d percent allowed" % (verbs, len(sentences), CMP_VERB_PERCENT))
    else:
        floors = HDG_FLOORS if floors is None else floors
        lemma_cap, document_cap = lemma_cap or HDG_LEMMA_CAP, document_cap or HDG_DOCUMENT_CAP
        strata = collections.Counter(c["stratum"] for c in comments)
        for kind in STRATA:
            if strata[kind] < floors.get(kind, 0):
                found.append("stratum %s: %d lines, the floor is %d" % (kind, strata[kind], floors[kind]))
            if strata[kind] > HDG_CAPS[kind]:
                found.append("stratum %s: %d lines, the cap is %d" % (kind, strata[kind], HDG_CAPS[kind]))
    found += ["lemma %s has %d sentences, at most %d allowed" % (lemma, n, lemma_cap) for lemma, n in sorted(lemmas.items()) if n > lemma_cap]
    found += ["document %s has %d sentences, at most %d allowed" % (doc, n, document_cap) for doc, n in sorted(documents.items()) if n > document_cap]
    for s in sentences:
        found += sentence_problems(source, s)
    return found


def final_sentence(source, entry, sentence, number):
    """The built sentence of a kept entry: the agreed token rows, the id and the per-sentence lines."""
    if source == "compound":
        extra = ["# head = %d" % entry["target"], "# lemma = " + entry["lemma"], "# kind = " + entry["kind"]]
    else:
        extra = ["# word = %d" % entry["target"], "# lemma = " + entry["lemma"], "# stratum = " + entry["kind"]]
    return dict(sentence, sent_id=SENT_ID[source] % number, extra=extra)


def _drop_rows(round_work):
    """The `reason<TAB>count` rows of the sentence-level drops of the candidate stage that the report of the round lists."""
    path = round_work.private_file("candidates-report.md")
    if not os.path.exists(path):
        return []
    for line in common.read_lines(path):
        for label in ("sentences dropped before the frame: ", "lines dropped before the frame: "):
            if line.startswith(label) and not line.endswith(": none"):
                pairs = [part.rsplit(" ", 1) for part in line.split(": ", 1)[1].split(", ")]
                return ["%s\t%s" % (reason, count) for reason, count in pairs
                        if reason in (EQUALS_SHAPES_DEV, NEAR_SHAPES_DEV, "equals-a-set-sentence", "probe-5gram")]
    return []


def build(work, source, out_dir, seed=SEED, allow_short=False, data_dir=None, extra_tables=(), omit=()):
    """Write <source>.tsv, sources.tsv, excluded-documents.tsv and dropped-sentences.tsv into `out_dir`; returns (sentences, report lines).

    Fail closed: W/sources-draft.tsv must pass the source and license gate (DraftError, nothing written), a missed quota (`BuildError`, nothing written;
    `allow_short` lowers only the minimum or the floors) and an excluded or unknown document. `omit` lists documents (rule 10) whose selected items are removed after
    the caps; an id that is not in the draft is refused."""
    data_dir = data_dir or common.DATA
    round_work = shapes_dev.RoundWork(getattr(work, "base", work.root), source)
    draft_gate(work, data_dir, extra_tables)
    items, tagged = shapes_dev.read_decided(round_work, data_dir)
    candidates = {c["cand_id"]: c for c in shapes_dev.read_candidates(round_work)}
    draft = shapes_dev.read_draft(work)
    omit = sorted(set(omit))
    missing = sorted(set(omit) - {row["source_id"] for row in draft})
    if missing:
        raise BuildError("cannot omit a document that is not in the draft: " + ", ".join(missing))
    exclusions = shapes_dev.load_exclusions(work, data_dir)
    drops, kept, sentences = collections.Counter(), [], {}
    for item in items:
        rows = tagged.get(item["sent_key"])
        if rows is None:
            drops["undecided"] += 1
            continue
        kind, reason = (compound_kind if source == "compound" else heading_kind)(item, rows)
        if reason:
            drops[reason] += 1
            continue
        cand = candidates[item["cand_id"]]
        sentences[item["sent_key"]] = dict(sent_id="", source_id=item["source_id"], text=cand["text"], rows=[list(r) for r in rows])
        kept.append(dict(sent_key=item["sent_key"], lemma=item["lemma"], source_id=item["source_id"], kind=kind, target=item["target"]))
    labeled = sum(drops.values()) + len(kept)
    temporary = {"tmp-%d" % n: entry for n, entry in enumerate(kept, 1)}
    fixed = fix_tier_rules([dict(sentences[e["sent_key"]], sent_id=name) for name, e in temporary.items()], data_dir, source)
    after_lint = []
    for name, entry in temporary.items():
        if name in fixed:
            drops["fix-tier"] += 1
        else:
            after_lint.append(entry)
    chosen, cap_drops = (select_compound if source == "compound" else select_headings)(after_lint, seed)
    drops.update(cap_drops)
    omitted = [entry for entry in chosen if entry["source_id"] in omit]
    if omitted:
        drops[OMITTED] += len(omitted)
        chosen = [entry for entry in chosen if entry["source_id"] not in omit]
        if source == "compound":
            chosen, surplus = trim_verb_share(chosen)                    # rule 6 again: the share of the verb items stays at most 15 percent
            if surplus:
                drops["verb-share"] += surplus
    ordered = sorted(chosen, key=lambda c: shapes_dev.rank_of(seed, source + "-order", c["sent_key"]))
    built = [final_sentence(source, entry, sentences[entry["sent_key"]], number) for number, entry in enumerate(ordered, 1)]
    unknown = sorted({s["source_id"] for s in built} - {row["source_id"] for row in draft})
    if unknown:
        raise BuildError("no source row for: " + ", ".join(unknown))
    refused = exclusions.problems(draft, built)
    if refused:
        raise shapes_dev.ExcludedSourceError("excluded sources are used:\n  " + "\n  ".join(refused[:20]))
    problems = quota_problems(source, built, 0 if allow_short and source == "compound" else None, {} if allow_short else None)
    if problems:
        raise BuildError("the quota gate is not met (--allow-short lowers only the minimum or the floors):\n  " + "\n  ".join(problems[:20]))
    common.write_corpus(os.path.join(out_dir, FILES[source]), common.with_flag_documentation(HEADERS[source], built), built)
    adjudicate.write_rows(os.path.join(out_dir, SOURCES_NAME), shapes_dev.SOURCE_COLUMNS, shapes_dev.sources_rows(draft, built))
    rejected_path = round_work.private_file("rejected-documents.tsv")
    rejected = sorted(adjudicate.read_records(rejected_path)[1], key=lambda r: r["source_id"]) if os.path.exists(rejected_path) else []
    common.write_text(os.path.join(out_dir, EXCLUDED_NAME), [
        "# Documents left out of %s because the document is a reprint of, or shares sentences with, a training or evaluation set, was already used, or has no verifiable federal-only basis" % FILES[source],
        "# (only ids and reasons, never text)", "# columns = document_id, reason"] + ["%s\t%s" % (r["source_id"], r["reason"]) for r in rejected]
        + ["%s\t%s" % (document, OMIT_REASON) for document in omit])
    common.write_text(os.path.join(out_dir, DROPPED_NAME), [
        "# Candidate sentences dropped alone because they equal or nearly equal a sentence of a training or evaluation set or of the shapes development set",
        "# (counted in private/candidates-report.md of the round folder; only counts are published, never text)", "# columns = reason, sentences"]
        + _drop_rows(round_work))
    return built, build_report(source, len(built), labeled, drops, built, draft)


def build_report(source, count, labeled, drops, built, draft):
    """Markdown counts of a build: drops per reason, per family, per kind, lemma and document statistics."""
    family = {row["source_id"]: row["family"] for row in draft}
    comments = [s["comments"] if "comments" in s else dict(part[2:].split(" = ", 1) for part in s["extra"]) for s in built]
    lemmas = collections.Counter(c["lemma"] for c in comments)
    documents = collections.Counter(s["source_id"] for s in built)
    kinds = collections.Counter(c["kind" if source == "compound" else "stratum"] for c in comments)
    lines = ["# Build (%s)" % source, "", "%d sentences written to %s (labeled candidates with a decision: %d)" % (count, FILES[source], labeled), "",
             "| dropped because | items |", "| --- | --- |"]
    lines += ["| %s | %d |" % item for item in sorted(drops.items())]
    families = collections.Counter(family[s["source_id"]] for s in built)
    lines += ["", "| family | documents | sentences |", "| --- | --- | --- |"]
    for name in sorted(families):
        lines.append("| %s | %d | %d |" % (name, sum(1 for d in documents if family[d] == name), families[name]))
    lines += ["", "- %s: %s" % ("kinds" if source == "compound" else "strata", ", ".join("%s %d" % item for item in sorted(kinds.items()))),
              "- lemmas: %d (most frequent: %s)" % (len(lemmas), ", ".join("%s %d" % item for item in sorted(lemmas.items(), key=lambda i: (-i[1], i[0]))[:8])),
              "- documents: %d" % len(documents), "- tokens: %d" % sum(len(s["rows"]) for s in built)]
    return lines


def _command_build(args):
    work = adjudicate.Work(args.work)
    out_dir = args.out_dir or os.path.join(args.data_dir, args.source)
    try:
        built, report = build(work, args.source, out_dir, args.seed, args.allow_short, args.data_dir, args.extra_table or (), args.omit_document or ())
    except (BuildError, shapes_dev.ExcludedSourceError, ValueError) as error:    # DraftError is a ValueError
        print("build refused: %s" % error, file=sys.stderr)
        return 1
    adjudicate._print(report)
    common.write_text(shapes_dev.RoundWork(args.work, args.source).private_file("build-report.md"), report)
    return 0


# ---------------------------------------------------------------- hand check (the precision gate)

def read_built(out_dir, source):
    """The sentences of <source>.tsv of `out_dir`."""
    return common.read_corpus(os.path.join(out_dir, FILES[source]))[1]


def judged(source, sentence):
    """(index, kind, lemma) of the judged word of a built sentence."""
    c = sentence["comments"]
    return (int(c["head"]), c["kind"], c["lemma"]) if source == "compound" else (int(c["word"]), c["stratum"], c["lemma"])


def swapped_tag(source, kind):
    """The tag shown for a leniency control: the wrong class of the judged word."""
    return "NOUN" if (source == "compound" and kind == "verb") or (source == "headings" and kind == "imperative") else "VERB"


def build_sample(source, sentences, seed=None, size=SAMPLE_SIZE, controls=SAMPLE_CONTROLS):
    """[(kind, sentence, shown tag)] shuffled: `size` random items with the agreed tag of the judged word (`headings`: by the quota of each stratum, fewer
    when a stratum is short), and `controls` other items whose judged word is shown with the wrong class (the right verdict is `incorrect`; a reviewer that
    accepts more than CONTROLS_ACCEPTED_MAX of them is too lenient)."""
    rng = random.Random(SAMPLE_SEEDS[source] if seed is None else seed)
    ordered = sorted(sentences, key=lambda s: s["sent_id"])
    if source == "compound":
        picked = rng.sample(ordered, min(size, len(ordered)))
    else:
        picked = []
        for stratum, count in HEADING_SAMPLE_PLAN:
            members = [s for s in ordered if judged(source, s)[1] == stratum]
            picked += rng.sample(members, min(count, len(members)))
    rest = [s for s in ordered if s not in picked]
    swapped = rng.sample(rest, min(controls, len(rest)))
    result = [("real", s, s["rows"][judged(source, s)[0]][1]) for s in picked]
    result += [("control", s, swapped_tag(source, judged(source, s)[1])) for s in swapped]
    rng.shuffle(result)
    return result


def sample_lines(source, sample, prefix="R"):
    """(Markdown lines of the review file, key rows): every token is shown with its tag, the judged word with the proposed tag."""
    lines = list(REVIEW_INSTRUCTIONS)
    keys = []
    for number, (kind, sentence, shown) in enumerate(sample, 1):
        name = "%s%03d" % (prefix, number)
        index, stratum, _ = judged(source, sentence)
        shown_rows = [(row[0], shown if i == index else row[1]) for i, row in enumerate(sentence["rows"])]
        lines += ["## " + name, "", "Text: " + sentence["text"],
                  "Tagged: " + " ".join("%d:%s/%s" % (i, form, tag) for i, (form, tag) in enumerate(shown_rows)),
                  "Marked word: %s (index %d)" % (sentence["rows"][index][0], index), "Proposed tag: " + shown, ""]
        keys.append((name, kind, sentence["sent_id"], stratum, index, shown, sentence["rows"][index][1], len(sentence["rows"])))
    return lines, keys


def _command_sample(args):
    out_dir = args.out_dir or os.path.join(args.data_dir, args.source)
    round_work = _round_work(args, args.source)
    sample = build_sample(args.source, read_built(out_dir, args.source), args.seed, args.size, args.controls)
    lines, keys = sample_lines(args.source, sample)
    common.write_text(round_work.private_file("review-sample.md"), lines)
    adjudicate.write_rows(round_work.private_file("review-sample-key.tsv"), SAMPLE_KEY_COLUMNS, keys)
    real = sum(1 for kind, _, _ in sample if kind == "real")
    print("%d items: %d real, %d control (seed %d)" % (len(sample), real, len(sample) - real, args.seed or SAMPLE_SEEDS[args.source]))
    return 0


def review_numbers(keys, answers):
    """(real-item Counter, control Counter, whole-item correct, reviewed real items, tokens, incorrect tokens, unanswered items)."""
    given = {item: (verdict, others) for item, verdict, others, _ in answers}
    target, controls = collections.Counter(), collections.Counter()
    whole = reviewed = tokens = wrong = 0
    unanswered = []
    for row in keys:
        if row["item"] not in given:
            unanswered.append(row["item"])
            continue
        verdict, others = given[row["item"]]
        if any(index >= int(row["ntokens"]) for index in others):
            raise ValueError("%s: an `others` index is outside the sentence" % row["item"])
        if row["kind"] == "control":
            controls[verdict] += 1
            continue
        target[verdict] += 1
        reviewed += 1
        tokens += int(row["ntokens"])
        wrong += len([i for i in others if i != int(row["target"])]) + (verdict == "incorrect")
        whole += verdict == "correct" and not others
    return target, controls, whole, reviewed, tokens, wrong, unanswered


def review_lines(target, controls, whole, reviewed, tokens, wrong, unanswered):
    """(lines, passed): the Markdown result of the model review and whether the gate (rule 8) holds."""
    judged_count = target["correct"] + target["incorrect"]
    precision = target["correct"] / judged_count if judged_count else None
    lenient = controls["correct"] > CONTROLS_ACCEPTED_MAX
    passed = precision is not None and precision >= PRECISION_MIN and not lenient and not unanswered
    lines = ["# Review result", "",
             "- reviewed items: %d (marked word correct %d, incorrect %d, unclear %d)" % (reviewed, target["correct"], target["incorrect"], target["unclear"]),
             "- marked-word precision: %s (gate: at least %.0f%%)" % ("-" if precision is None else "%.1f%%" % (100 * precision), 100 * PRECISION_MIN),
             "- whole-item precision (marked word correct and no other token marked wrong): %s" % ("-" if not reviewed else "%.1f%%" % (100 * whole / reviewed)),
             "- token precision (reported, not gated): %s (%d wrong of %d tokens)" % ("-" if not tokens else "%.2f%%" % (100 * (1 - wrong / tokens)), wrong, tokens),
             "- control items accepted as correct: %d of %d%s" % (controls["correct"], sum(controls.values()), " (TOO LENIENT, repeat the review once)" if lenient else ""),
             "- unanswered items: %d" % len(unanswered),
             "- gate: " + ("PASSED" if passed else "STOP: nothing else proceeds; report the result")]
    return lines, passed


def _command_review_result(args):
    round_work = _round_work(args, args.source)
    _, keys = adjudicate.read_records(round_work.private_file("review-sample-key.tsv"))
    answers = []
    for name in args.verdicts:
        with open(name, "r", encoding="utf-8") as handle:
            answers += imperative_data.parse_answers(handle.read())
    unknown = {a[0] for a in answers} - {k["item"] for k in keys}
    if unknown:
        raise ValueError("verdicts for unknown items: " + ", ".join(sorted(unknown)[:5]))
    lines, passed = review_lines(*review_numbers(keys, answers))
    adjudicate._print(lines)
    common.write_text(round_work.private_file("review-result.md"), lines)
    return 0 if passed else 1


# ---------------------------------------------------------------- report

def report_lines(out_dir, data_dir, source):
    """Markdown counts of the built source for the README: sizes, per family, kinds, lemmas, documents, lexicon coverage (no rebuild of the lexicon)."""
    lex = shapes_dev.lexicon_of(data_dir)
    sentences = read_built(out_dir, source)
    draft = {}
    with open(os.path.join(out_dir, SOURCES_NAME), encoding="utf-8") as handle:
        for line in handle.read().split("\n")[1:]:
            cells = line.split("\t")
            if len(cells) == len(shapes_dev.SOURCE_COLUMNS):
                draft[cells[0]] = dict(zip(shapes_dev.SOURCE_COLUMNS, cells))
    tokens = known_tokens = pairs = known_pairs = 0
    seen = set()
    for s in sentences:
        for form, tag, _ in s["rows"]:
            if not common.has_letter_or_digit(form):
                continue
            tokens += 1
            known_tokens += tag in lex.get(form.lower(), ())
            if (form.lower(), tag) not in seen:
                seen.add((form.lower(), tag))
                pairs += 1
                known_pairs += tag in lex.get(form.lower(), ())
    families = collections.Counter(draft[s["source_id"]]["family"] for s in sentences)
    documents = collections.Counter(s["source_id"] for s in sentences)
    kinds = collections.Counter(judged(source, s)[1] for s in sentences)
    lemmas = collections.Counter(judged(source, s)[2] for s in sentences)
    lines = ["| family | documents | sentences |", "| --- | --- | --- |"]
    for name in sorted(families):
        lines.append("| %s | %d | %d |" % (name, sum(1 for d in documents if draft[d]["family"] == name), families[name]))
    lines += ["", "- sentences: %d, tokens: %d, lemmas: %d, documents: %d" % (len(sentences), sum(len(s["rows"]) for s in sentences), len(lemmas), len(documents)),
              "- %s: %s" % ("kinds" if source == "compound" else "strata", ", ".join("%s %d" % item for item in sorted(kinds.items()))),
              "- most sentences per lemma: %d, per document: %d" % (max(lemmas.values(), default=0), max(documents.values(), default=0)),
              "- lexicon coverage: %d of %d word tokens have their tag listed for their spelling in lexicon.tsv; %d of %d distinct (spelling, tag) pairs"
              % (known_tokens, tokens, known_pairs, pairs)]
    return lines


def _command_report(args):
    adjudicate._print(report_lines(args.out_dir or os.path.join(args.data_dir, args.source), args.data_dir, args.source))
    return 0


# ---------------------------------------------------------------- command line

def main(argv=None):
    parser = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    parser.add_argument("--work", required=True, help="work folder (outside the repository)")
    parser.add_argument("--data-dir", default=common.DATA, help="data folder (default: the repository's data/)")
    sub = parser.add_subparsers(dest="command", required=True)

    def common_options(p, tables=True, source=True):
        if source:
            p.add_argument("--source", required=True, choices=SOURCES)
        p.add_argument("--seed", type=int, default=None)
        if tables:
            p.add_argument("--extra-table", action="append", default=None, metavar="PATH", help="a further table of used documents (repeatable)")

    exclusions = sub.add_parser("exclusions", help="index of the documents already used; checks the draft")
    exclusions.add_argument("--extra-table", action="append", default=None, metavar="PATH")
    exclusions.add_argument("--check", nargs="*", default=None, help="document ids or URLs to check")
    preflight = sub.add_parser("preflight", help="pool sizes against the candidate budgets (counts only)")
    common_options(preflight, source=False)
    preflight.add_argument("--extra-text", action="append", default=None, metavar="DIR", help="a folder of cleaned text of an earlier sourcing (repeatable)")
    preflight.add_argument("--no-ud", action="store_true", help="ignore the UD cache even when present")
    candidates = sub.add_parser("candidates", help="the candidates of the pool; apply the leakage filters; writes W/rounds/<source>/candidates.tsv")
    common_options(candidates)
    candidates.add_argument("--extra-text", action="append", default=None, metavar="DIR")
    candidates.add_argument("--no-ud", action="store_true")
    candidates.add_argument("--lemma-cap", type=int, default=None)
    candidates.add_argument("--document-cap", type=int, default=None, help="headings only")
    candidates.add_argument("--max-candidates", type=int, default=None)
    candidates.add_argument("--min-candidates", type=int, default=None)
    candidates.add_argument("--min-lemmas", type=int, default=None, help="compound only")
    build_parser = sub.add_parser("build", help="write <source>.tsv, sources.tsv, excluded-documents.tsv and dropped-sentences.tsv")
    common_options(build_parser)
    build_parser.add_argument("--out-dir", default=None, help="default: <data dir>/<source>")
    build_parser.add_argument("--allow-short", action="store_true", help="build below the minimum or the floors (the caps and the other gates stay)")
    build_parser.add_argument("--omit-document", action="append", default=None, metavar="ID",
                              help="remove the selected items of a document whose public-domain basis is not verifiable (rule 10; repeatable)")
    sample = sub.add_parser("sample", help="the review sample for the precision gate")
    common_options(sample, tables=False)
    sample.add_argument("--out-dir", default=None)
    sample.add_argument("--size", type=int, default=SAMPLE_SIZE)
    sample.add_argument("--controls", type=int, default=SAMPLE_CONTROLS)
    review = sub.add_parser("review-result", help="precision of the review; exit 1 when the gate is not met")
    common_options(review, tables=False)
    review.add_argument("--verdicts", nargs="+", required=True)
    report = sub.add_parser("report", help="counts of the built source for the README")
    common_options(report, tables=False)
    report.add_argument("--out-dir", default=None)
    args = parser.parse_args(argv)
    if getattr(args, "seed", None) is None and args.command in ("preflight", "candidates", "build"):
        args.seed = SEED
    handlers = {"exclusions": _command_exclusions, "preflight": _command_preflight, "candidates": _command_candidates, "build": _command_build,
                "sample": _command_sample, "review-result": _command_review_result, "report": _command_report}
    try:
        return handlers[args.command](args)
    except (ValueError, shapes_dev.ExcludedSourceError) as error:        # DraftError is a ValueError
        print("%s refused: %s" % (args.command, error), file=sys.stderr)
        return 1


if __name__ == "__main__":
    sys.exit(main())

"""Builder of the training source data/imperative/ (Python 3, standard library only; imports shapes_dev.py and the repository's data tools read-only).

The source holds about 900 sentences that start with an imperative verb, from US federal public-domain instructional text (US Army and
Navy manuals, USDA bulletins, Forest Service, Bureau of Mines and Public Health Service leaflets and the like). It is a TRAINING source
(use = training), written for one purpose: the library's training mix has far fewer imperative-initial sentences than instructional text
has (the scarcity measured by the shape count of the earlier experiment), and the hard tranche of the shapes development set shows the
weakness. Everything around the labeling is done by this tool; it never calls a model, reads no weights and no prediction: the tool
imports no tagger, decoder, weights file or score, and a data test scans its source for that (test_imperative_data.py).

THE PRE-REGISTERED RULE (committed before any label of this source exists; the commit is recorded in data/imperative/README.md)

1. Documents. Only documents of US federal authorship whose public-domain basis was checked one by one and is written in the
   `license_basis` cell of sources.tsv (17 U.S.C. 105; the archive.org record's own rights statement; NTRS rights record for NASA). No FAA
   material, nothing in data/eval/shapes-dev/excluded-sources.tsv, no share-alike or restricted text, no Wikipedia, no UD text. A document
   must be disjoint from every evaluation set: its id, its normalized URL and its archive.org identifier are not in any table of
   `used_index` (training and evaluation sources, the shapes-dev tables, and every `--extra-table`, for example the shapes-dev sourcing
   drafts), and the document is rejected as a whole when 3 or more of its sentences (DOCUMENT_KEY_HITS) equal a training sentence, an
   evaluation sentence (the held-out sets enter only as the opaque hash set `leakage.held_out_overlap_keys`), a sentence of the shapes
   development set (dev.tsv, hard.tsv) or a sentence of the full cleaned text of a shapes-dev document (`--extra-text`): a reprint
   under another id is caught that way.
2. Candidates (`candidates`). A sentence is a candidate when it passes the unchanged candidate filters of shapes_dev.py (3 to 30
   tokens, one sentence, `noise_reason` cleanliness, no duplicate, no equal training or evaluation sentence, no 5-gram of the held-out
   probe) and is not a shapes-dev sentence: its key is not a sentence of dev.tsv, hard.tsv or an `--extra-text` document (`equals-shapes-dev`),
   and a sentence of at least 10 words is dropped when at least 80 percent of its word 6-grams are word 6-grams of those sentences
   (`near-shapes-dev`). The frame is the `v-imperative-initial` frame of `shapes_dev.shape_hits`, unchanged (token 0 capitalized, listed in
   the lexicon with both NOUN and VERB, its own base form, no determiner, preposition or auxiliary; the next token a determiner,
   preposition, object pronoun, a following adverb or a quantity word) and, as the decision rule below requires, its lower-cased form equals
   `base_form_of` (no gerund, past or third person form; tested before labeling so that no labeling is spent on such a word, and counted).
   Per lemma at most `--lemma-cap` (9) candidates and in all at most
   `--max-candidates` (2,300, the labeling budget), taken in the seeded order `rank_of(SEED, "imperative", sentence key)`: a sentence is taken
   unless its lemma already has 9 candidates or 2,300 are taken. No candidate is chosen, ordered or weighted by any model. Gate: at least 1,500
   candidates and at least 250 lemmas (`--min-candidates`, `--min-lemmas`).
3. Labels. Every candidate sentence is tagged from scratch, token by token, by two blind model labelers that follow data/CONVENTIONS.md,
   through the unchanged commands `shapes_dev.py export`, `check-batches`, `validate-output` and `decide` (the batch files carry only an
   opaque id, a token index and the token; the labelers see no shape, source, lemma or intended class).
4. Kept sentences (`build`). A candidate is kept when ALL of these hold: (a) the unchanged decision of `shapes_dev.decide_item` and
   `shapes_dev.frame_mismatch`: both labelers give token 0 the same tag, VERB, token 0 is the judged word and its lower-cased form
   equals `base_form_of` (no gerund, past or third person form); (b) every other token is agreed too (`disputed_tokens == 0`: the
   whole sentence is unanimous); (c) the convention linter reports no fix-tier violation on the sentence (the sentence is dropped, no
   label is changed by hand). Dropped sentences are counted per reason. A frame hit that both labelers read as a noun is dropped.
5. Caps. In the seeded order `rank_of(SEED, "imperative-build", sentence key)` the build takes a kept sentence unless its lemma already has
   6 sentences (`--lemma-cap`), its document already has 30 (`--document-cap`) or 900 sentences are taken (`--max`).
6. Quota gate. `build` refuses (exit code 1, nothing written) unless there are at least 600 (`--min`, lowered only by `--allow-short`)
   and at most 900 sentences, at most 6 per lemma, at most 30 per document, every sentence has token 0 tagged VERB in the base form,
   every token `unanimous` (flags only where both labelers agree) and the tokens rejoin to the text. Below 600: stop and report.
7. Hand check (`sample`, `review-result`). 60 kept sentences (and 4 controls whose target tag is swapped) are drawn at random with a
   fixed seed and shown, every token with its tag, to a third model (a model review, not a human review). The gate: precision of the target
   token `correct / (correct + incorrect)` of at least 95 percent (3 incorrect of 60 is exactly 95.0 percent and meets it; 4 do not), at most 1 of the
   4 controls accepted, no unanswered item.
   The whole-sentence precision, the token precision and the number of `unclear` verdicts are reported, not gated. A sample is drawn
   again only for a changed data set, never to repeat a failed review.
8. Format. Sorted by the seeded order, UTF-8 without BOM, LF, NFC; ids `imperative-NNNN`; the block format of data/open/train.tsv
   (form, upos, status); sources.tsv lists only the used documents (12 columns of data/eval/gold-v2/sources.tsv, `sentences` recomputed).

Work folder W (`--work W`, outside the repository), the layout of shapes_dev.py: W/sources-draft.tsv (12 columns), W/text/<source_id>.txt (one
sentence per line), W/tokens.tsv (written by `shapes_dev.py tokenize`), W/candidates.tsv, W/batches/ and W/private/ (the labelers never see it).

Commands (`--data-dir D` replaces the repository's data folder, for tests):

  python tools/data/imperative_data.py --work W exclusions [--extra-table PATH ...] [--check ID_OR_URL ...]
  python tools/data/imperative_data.py --work W candidates [--lemma-cap 9] [--max-candidates 2300] [--extra-table PATH ...] [--extra-text DIR ...] [--no-ud]
                                                           [--min-candidates 1500] [--min-lemmas 250]
  (then: shapes_dev.py --work W export / check-batches / validate-output / decide, the unchanged blind labeling)
  python tools/data/imperative_data.py --work W build [--out-dir D] [--lemma-cap 6] [--document-cap 30] [--max 900] [--min 600] [--allow-short]
  python tools/data/imperative_data.py --work W sample [--out-dir D] [--size 60] [--controls 4]
  python tools/data/imperative_data.py --work W review-result --verdicts FILE ...
  python tools/data/imperative_data.py --work W report [--out-dir D]
"""
import argparse
import collections
import os
import random
import re
import sys

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import adjudicate
import check_licenses
import common
import convention_rules
import corrections
import shapes_dev

SHAPE = "v-imperative-initial"
SEED = shapes_dev.SEED
CORPUS_NAME = "imperative.tsv"
SOURCES_NAME = "sources.tsv"
EXCLUDED_NAME = "excluded-documents.tsv"
DROPPED_NAME = "dropped-sentences.tsv"
SENT_ID = "imperative-%04d"
LEMMA_CAP_CANDIDATES = 9
LEMMA_CAP_BUILD = 6
DOCUMENT_CAP = 30
MIN_SENTENCES, MAX_SENTENCES = 600, 900
MIN_CANDIDATES, MIN_LEMMAS = 1500, 250
MAX_CANDIDATES = 2300
SAMPLE_SIZE, SAMPLE_CONTROLS = 60, 4
PRECISION_MIN = shapes_dev.PRECISION_MIN
CONTROLS_ACCEPTED_MAX = shapes_dev.CONTROLS_ACCEPTED_MAX
DOCUMENT_KEY_HITS = shapes_dev.DOCUMENT_KEY_HITS
NEAR_WORDS, NEAR_SHARE = shapes_dev.NEAR_STANDARD_WORDS, shapes_dev.NEAR_STANDARD_SHARE
EQUALS_SHAPES_DEV = "equals-shapes-dev"
NEAR_SHAPES_DEV = "near-shapes-dev"
SHAPES_DEV_TABLES = ("eval/shapes-dev/sources.tsv", "eval/shapes-dev/hard-sources.tsv", "eval/shapes-dev/excluded-sources.tsv")
SHAPES_DEV_CORPORA = ("eval/shapes-dev/dev.tsv", "eval/shapes-dev/hard.tsv")
LICENSE_PREFIXES = ("US federal government work", "US public domain")
SAMPLE_KEY_COLUMNS = ("item", "kind", "sent_id", "shape", "target", "shown_tag", "agreed_tag", "ntokens")
REVIEW_INSTRUCTIONS = [
    "# Review items", "",
    "Each item shows a sentence with a proposed UPOS tag for every token and one marked word (token index 0) whose tag you judge first.",
    "Read data/CONVENTIONS.md in full first. For each item answer with one line, four tab separated cells, and nothing else:",
    "`item<TAB>verdict<TAB>others<TAB>cite`. The verdict is `correct`, `incorrect` or `unclear` (the conventions do not decide it) for the",
    "MARKED word only. `others` lists the zero-based indexes of any OTHER token of the sentence whose proposed tag is wrong, separated by",
    "commas, or `-` when none is wrong. The cite names the rule of CONVENTIONS you applied in at most 20 words. Do not look at any other",
    "file and do not search for the sentences.", "",
]
HEADER = [
    "# corpus = DemaConsulting.PartOfSpeech imperative-initial training source (UPOS)",
    "# license = CC0-1.0 for annotations and selection; sentences are US federal government works, see sources.tsv",
    "# columns = form, upos, status",
    "# status = unanimous (two blind model labelers agree on the tag of every token of the sentence)",
    "# selection = sentences that start with an imperative verb, from US federal instructional text; see README.md and tools/data/imperative_data.py",
    "# use = training",
]


class BuildError(ValueError):
    """The build or a gate refuses (nothing is written)."""


# ---------------------------------------------------------------- the used-document index

def ia_ident(url):
    """The archive.org identifier of a download, details or stream URL (lower case), else None."""
    match = re.search(r"archive\.org/(?:download|details|stream)/([^/?#]+)", url or "")
    return match.group(1).lower() if match else None


def _add_table(index, path, origin):
    """Add every id, normalized URL, Gutenberg number and archive.org identifier of a source table (a header row is skipped)."""
    for line in common.read_lines(path):
        if not line.strip() or line.startswith("#"):
            continue
        cells = line.split("\t")
        if cells[0] in ("source_id", "doc_id", "document_id"):
            continue
        index.setdefault(("id", cells[0]), origin)
        for cell in cells:
            if cell.startswith("http"):
                normalized = shapes_dev.normalize_url(cell)
                if normalized:
                    index.setdefault(("url", normalized), origin)
                ident = ia_ident(cell)
                if ident:
                    index.setdefault(("ident", ident), origin)
        for found in shapes_dev.gutenberg_ids(line):
            index.setdefault(("id", found), origin)


def used_index(data_dir, exclusions, extra_tables=()):
    """{(kind, value): origin} of every document training, evaluation, the shapes development set and the extra tables used or excluded.

    It is `shapes_dev.exclusion_index` plus the shapes-dev source tables (both tranches) and every `extra_tables` path (for example the
    shapes-dev sourcing drafts), which also contribute archive.org identifiers (kind `ident`)."""
    index = shapes_dev.exclusion_index(data_dir, exclusions)
    for table in SHAPES_DEV_TABLES:
        path = os.path.join(data_dir, *table.split("/"))
        if os.path.exists(path):
            _add_table(index, path, table)
    for path in extra_tables:
        _add_table(index, path, "extra:" + os.path.basename(path))
    for (kind, value), origin in list(index.items()):          # the committed tables name archive.org documents too
        if kind == "url":
            ident = ia_ident("https://" + value)
            if ident:
                index.setdefault(("ident", ident), origin)
    return index


def document_rejection(index, source_id, url=""):
    """The reason why a document was already used (id, Gutenberg number, URL or archive.org identifier), or None when it is new."""
    reason = shapes_dev.document_rejection(index, source_id, url)
    if reason:
        return reason
    ident = ia_ident(url)
    if ident and ("ident", ident) in index:
        return "archive.org identifier %s is listed in %s" % (ident, index[("ident", ident)])
    return None


def draft_problems(work, draft, index, exclusions):
    """[message] for a draft that is not a sound list of documents: duplicate or malformed ids, a license basis that is not a federal
    or public-domain basis, a forbidden or FAA source, a used or excluded document, a missing or changed text file."""
    problems = []
    seen = set()
    for row in draft:
        source_id = row["source_id"]
        if not re.fullmatch(r"[A-Za-z0-9][A-Za-z0-9._-]*", source_id):
            problems.append("%s: the source_id has characters outside letters, digits, '.', '_' and '-'" % source_id)
        if source_id in seen:
            problems.append("%s: duplicate source_id" % source_id)
        seen.add(source_id)
        if not row["license_basis"].startswith(LICENSE_PREFIXES):
            problems.append("%s: the license_basis must start with one of: %s" % (source_id, "; ".join(LICENSE_PREFIXES)))
        if not common.is_http_url(row["url"]):
            problems.append("%s: the url is not an http(s) URL" % source_id)
        if not re.fullmatch(r"[0-9a-f]{64}", row["raw_sha256"]) or not re.fullmatch(r"[0-9a-f]{64}", row["text_sha256"]):
            problems.append("%s: raw_sha256 and text_sha256 must be SHA-256 values" % source_id)
        joined = "\t".join(row[c] for c in shapes_dev.SOURCE_COLUMNS if c not in ("raw_sha256", "text_sha256"))
        if check_licenses.FORBIDDEN_TEXT.search(joined):
            problems.append("%s: the row mentions a share-alike, restricted or Wikipedia source" % source_id)
        if check_licenses.FAA_TEXT.search(joined):
            problems.append("%s: the row mentions FAA material" % source_id)
        reason = document_rejection(index, source_id, row["url"])
        if reason:
            problems.append("%s: already used: %s" % (source_id, reason))
        hit = exclusions.match(source_id, row["url"])
        if hit:
            problems.append("%s: excluded as %s" % (source_id, hit))
        path = shapes_dev.shared_path(work, "text", source_id + ".txt")
        if not os.path.exists(path):
            problems.append("%s: text/%s.txt is missing" % (source_id, source_id))
            continue
        if common.sha256_file(path) != row["text_sha256"]:
            problems.append("%s: the text file does not match text_sha256" % source_id)
        lines = common.read_lines(path)
        if str(len([line for line in lines if line.strip()])) != row["sentences"]:
            problems.append("%s: the sentence count of the draft does not match the text file" % source_id)
        if any("\t" in line or line != common.nfc(line) for line in lines):
            problems.append("%s: the text holds a tab or a character that is not NFC" % source_id)
    return problems


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
        reason = document_rejection(index, value, value if value.startswith("http") else "")
        print("%s: %s" % ("REJECT" if reason else "ACCEPT", value) + ((" (" + reason + ")") if reason else ""))
        problems += [value] if reason else []
    problems += draft_problems(work, shapes_dev.read_draft(work), index, exclusions)
    for message in problems:
        print("REJECT " + message)
    print("%d documents in the draft, %d problems" % (len(shapes_dev.read_draft(work)), len(problems)))
    return 1 if problems else 0


# ---------------------------------------------------------------- the shapes development set (sentences to stay disjoint from)

class ShapesDevSentences:
    """The sentences of the shapes development set (both tranches, committed) and of every `extra_text` folder (cleaned document text of
    the shapes-dev sourcing): sentence keys and word 6-grams, to keep the candidates disjoint from them."""

    def __init__(self, data_dir, extra_text=()):
        self.keys = set()
        self.grams = set()
        for relative in SHAPES_DEV_CORPORA:
            path = os.path.join(data_dir, *relative.split("/"))
            if os.path.exists(path):
                for sentence in common.read_corpus(path)[1]:
                    self.add(sentence["text"])
        for folder in extra_text:
            if not os.path.isdir(folder):
                raise ValueError("--extra-text is not a folder: " + folder)
            for name in sorted(os.listdir(folder)):
                if name.endswith(".txt"):
                    for line in common.read_lines(os.path.join(folder, name)):
                        if line.strip():
                            self.add(line)
        self.keys.discard("")

    def add(self, text):
        self.keys.add(common.key(text))
        self.grams |= shapes_dev.StandardTranche.word_grams(text)

    def key_hit(self, text):
        return common.key(text) in self.keys

    def reason(self, text):
        """`equals-shapes-dev`, `near-shapes-dev` (at least NEAR_WORDS words and at least NEAR_SHARE of the word 6-grams are shared) or None."""
        if self.key_hit(text):
            return EQUALS_SHAPES_DEV
        grams = shapes_dev.StandardTranche.word_grams(text)
        if len(shapes_dev.alnum_words(text)) >= NEAR_WORDS and grams and len(grams & self.grams) / len(grams) >= NEAR_SHARE:
            return NEAR_SHAPES_DEV
        return None


# ---------------------------------------------------------------- candidates

def collect_candidates(work, data_dir, include_ud=True, lemma_cap=LEMMA_CAP_CANDIDATES, seed=SEED, extra_tables=(), extra_text=(),
                       tokens_override=None, filt=None, clean=True, vocab=None, reference=None, maximum=MAX_CANDIDATES):
    """(rows, report lines, rejected documents {id: reason}, counts dict): the candidates of the v-imperative-initial frame.

    Scans every document of W/text in id order; see rule 2 of the module docstring. The rows are dicts in the form of
    `shapes_dev.write_candidates` (role `target`, origin `real`), ids `cNNNNN` in the seeded order."""
    tokens_of = tokens_override if tokens_override is not None else shapes_dev.read_tokens(work)
    lex = shapes_dev.lexicon_of(data_dir)
    if clean and vocab is None:
        vocab = shapes_dev.vocabulary_of(data_dir, lex)
    filt = filt or shapes_dev.LeakageFilter(data_dir, include_ud)
    reference = reference or ShapesDevSentences(data_dir, extra_text)
    draft = {row["source_id"]: row for row in shapes_dev.read_draft(work)}
    exclusions = shapes_dev.load_exclusions(work, data_dir)
    index = used_index(data_dir, exclusions, extra_tables)
    documents = shapes_dev.read_documents(work)
    drops = collections.Counter()
    rejected = {}
    problems = []
    seen = set()
    pool = []
    clean_count = considered = not_base_form = 0
    per_document = collections.defaultdict(collections.Counter)
    for source_id in sorted(documents):
        if source_id not in draft:
            problems.append("text/%s.txt has no row in sources-draft.tsv" % source_id)
            continue
        if source_id in exclusions.reasons:
            drops["excluded-source"] += len(documents[source_id])
            considered += len(documents[source_id])
            continue
        reason = document_rejection(index, source_id, draft[source_id]["url"])
        if reason:
            rejected[source_id] = reason
            continue
        hits = sum(1 for _, text in documents[source_id] if filt.key_hit(text) or reference.key_hit(text))
        if hits >= DOCUMENT_KEY_HITS:
            rejected[source_id] = "%d sentences are also in a training or evaluation set or in the shapes development set" % hits
            continue
        for number, text in documents[source_id]:
            considered += 1
            per_document[source_id]["considered"] += 1
            if text not in tokens_of or tokens_of[text][0] != 1:
                drops["not-one-sentence-or-not-tokenized"] += 1
                continue
            tokens = tokens_of[text][1]
            key = shapes_dev.sent_key_of(text)
            if not shapes_dev.MIN_TOKENS <= len(tokens) <= shapes_dev.MAX_TOKENS:
                drops["length"] += 1
                continue
            noise = shapes_dev.noise_reason(text, tokens, lex, vocab) if clean else None
            if noise:
                drops["noise-" + noise] += 1
                continue
            if key in seen:
                drops["duplicate"] += 1
                continue
            close = reference.reason(text)
            if close:
                drops[close] += 1
                continue
            if filt.key_hit(text):
                drops["equals-a-set-sentence"] += 1
                continue
            if filt.probe_hit(text):
                drops["probe-5gram"] += 1
                continue
            seen.add(key)
            clean_count += 1
            per_document[source_id]["clean"] += 1
            for shape, i in shapes_dev.shape_hits(tokens, lex):
                if shape != SHAPE:
                    continue
                if shapes_dev.base_form_of(tokens[i], lex) != tokens[i].lower():
                    per_document[source_id]["not-base-form"] += 1          # the base-form test of the decision rule, before any labeling is spent
                    not_base_form += 1
                    continue
                per_document[source_id]["frame"] += 1
                pool.append((shapes_dev.rank_of(seed, "imperative", key),
                             dict(shape=SHAPE, role="target", target=i, lemma=shapes_dev.lemma_of(tokens[i], lex), origin="real",
                                  source_id=source_id, line=number, text=text, tokens=tokens)))
    rows, lemma_counts, capped = [], collections.Counter(), collections.Counter()
    beyond_total = 0
    for _, row in sorted(pool, key=lambda item: item[0]):
        if lemma_counts[row["lemma"]] >= lemma_cap:
            capped[row["lemma"]] += 1
        elif len(rows) >= maximum:
            beyond_total += 1
        else:
            lemma_counts[row["lemma"]] += 1
            rows.append(row)
    rows.sort(key=lambda r: shapes_dev.rank_of(seed, "imperative-order", shapes_dev.sent_key_of(r["text"])))
    for number, row in enumerate(rows, 1):
        row["cand_id"] = "c%05d" % number
    for row in rows:
        per_document[row["source_id"]]["candidates"] += 1
    counts = dict(considered=considered, clean=clean_count, frame_hits=len(pool), candidates=len(rows), lemmas=len(lemma_counts),
                  pool_lemmas=len({r["lemma"] for _, r in pool}), documents=len(documents), rejected=len(rejected),
                  beyond_lemma_cap=sum(capped.values()), beyond_total=beyond_total, not_base_form=not_base_form)
    report = candidate_report(counts, drops, rejected, problems, lemma_cap, maximum, lemma_counts, capped, per_document, draft)
    return rows, report, rejected, counts


def candidate_report(counts, drops, rejected, problems, lemma_cap, maximum, lemma_counts, capped, per_document, draft):
    """The report lines (counts only; no text of any evaluation set)."""
    lines = ["# Candidates (imperative-initial)", "", "Counts only; no text of any evaluation set.", "",
             "documents with text: %d, listed in the draft: %d, rejected: %d" % (counts["documents"], len(draft), counts["rejected"])]
    lines += ["- rejected %s: %s" % item for item in sorted(rejected.items())]
    noise = sum(count for reason, count in drops.items() if reason.startswith("noise-"))
    lines += ["", "sentences considered: %d, clean and kept for the frame: %d" % (counts["considered"], counts["clean"]),
              "sentences dropped before the frame: " + (", ".join("%s %d" % item for item in sorted(drops.items())) or "none"),
              "of which noise (cleanliness filter): %d" % noise, "",
              "frame hits: %d over %d lemmas (not counted: %d frame hits whose first word is not its base form: gerund, past or third person form); "
              "%d beyond the lemma cap of %d, %d beyond the total cap of %d; candidates: %d over %d lemmas (lemmas at the cap: %d)"
              % (counts["frame_hits"], counts["pool_lemmas"], counts["not_base_form"], counts["beyond_lemma_cap"], lemma_cap, counts["beyond_total"],
                 maximum, counts["candidates"], counts["lemmas"], sum(1 for n in lemma_counts.values() if n >= lemma_cap)), ""]
    families = collections.Counter()
    for source_id, stats in per_document.items():
        families[draft[source_id]["family"]] += stats["candidates"]
    lines += ["| family | candidates |", "| --- | --- |"] + ["| %s | %d |" % item for item in sorted(families.items())]
    lines += ["", "| lemma | candidates | beyond the cap |", "| --- | --- | --- |"]
    lines += ["| %s | %d | %d |" % (lemma, n, capped[lemma]) for lemma, n in sorted(lemma_counts.items(), key=lambda item: (-item[1], item[0]))[:25]]
    lines += ["", "| document | clean | frame hits | candidates |", "| --- | --- | --- | --- |"]
    lines += ["| %s | %d | %d | %d |" % (source_id, s["clean"], s["frame"], s["candidates"]) for source_id, s in sorted(per_document.items())]
    if problems:
        lines += [""] + ["- PROBLEM: " + p for p in problems]
    return lines


def gate_messages(counts, min_candidates=MIN_CANDIDATES, min_lemmas=MIN_LEMMAS):
    """[message] for each miss of the size gate (Gate G2): too few candidates or too few lemmas."""
    found = []
    if counts["candidates"] < min_candidates:
        found.append("only %d candidates (at least %d needed)" % (counts["candidates"], min_candidates))
    if counts["lemmas"] < min_lemmas:
        found.append("only %d lemmas (at least %d needed)" % (counts["lemmas"], min_lemmas))
    return found


def _command_candidates(args):
    work = adjudicate.Work(args.work)
    rows, report, rejected, counts = collect_candidates(work, args.data_dir, not args.no_ud, args.lemma_cap, args.seed,
                                                        args.extra_table or (), args.extra_text or (), maximum=args.max_candidates)
    for line in report[:24]:
        print(line)
    common.write_text(work.private_file("candidates-report.md"), report)
    adjudicate.write_rows(work.private_file("rejected-documents.tsv"), ("source_id", "reason"), sorted(rejected.items()))
    shapes_dev.write_candidates(work, rows)
    print("%d candidates (%d lemmas) written to candidates.tsv" % (counts["candidates"], counts["lemmas"]))
    missed = gate_messages(counts, args.min_candidates, args.min_lemmas)
    for message in missed:
        print("GATE G2 FAILED: " + message, file=sys.stderr)
    return 1 if missed else 0


# ---------------------------------------------------------------- build

def keep_reason(item, rows, lex):
    """None when the decided item of a candidate is a kept sentence (rule 4a and 4b), else the reason it is dropped.

    `rows` are the agreed token rows [form, tag, status] of the sentence."""
    if item["outcome"] != "keep":
        return item["reason"] or "dropped"
    if item["disputed_tokens"] or any(common.base_status(row[2]) == "disputed" for row in rows):
        return "disputed-token"
    if item["target"] != 0:
        return "not-initial"
    if rows[0][1] != "VERB":
        return "not-verb"
    if rows[0][0].lower() != shapes_dev.base_form_of(rows[0][0], lex):
        return "not-base-form"
    return None


def fix_tier_rules(sentences, data_dir):
    """{sent_id: [rule id]} of the sentences that the convention linter reports at the fix tier (rule 4c).

    The resources of the participle rules come from the correctable corpora of the data folder only, as in `check_conventions.py`
    (its `RESOURCE_EXEMPT`): neither these sentences nor an imperative.tsv already in the data folder nor the STE set feed them,
    so a rebuild gives the same result whether or not a previous output is present; nothing is changed."""
    corpora = corrections.load_corpora(data_dir)
    known = [convention_rules.Sentence(source, split, s) for (source, split), corpus in corpora.items() for s in corpus.sentences]
    own = [convention_rules.Sentence("imperative", "-", s) for s in sentences]
    resources = convention_rules.build_resources(known)
    found = {}
    for s in own:
        rules = [p.rule for p in convention_rules.evaluate(s, resources, (convention_rules.FIX,), None)]
        if rules:
            found[s.sent_id] = rules
    return found


def select_sentences(kept, seed=SEED, lemma_cap=LEMMA_CAP_BUILD, document_cap=DOCUMENT_CAP, maximum=MAX_SENTENCES):
    """(chosen, drops Counter): rule 5. `kept` are dicts with sent_key, lemma and source_id; the seeded order decides which are taken."""
    drops = collections.Counter()
    per_lemma, per_document, chosen = collections.Counter(), collections.Counter(), []
    for item in sorted(kept, key=lambda c: shapes_dev.rank_of(seed, "imperative-build", c["sent_key"])):
        if per_lemma[item["lemma"]] >= lemma_cap:
            drops["lemma-cap"] += 1
        elif per_document[item["source_id"]] >= document_cap:
            drops["document-cap"] += 1
        elif len(chosen) >= maximum:
            drops["total-cap"] += 1
        else:
            per_lemma[item["lemma"]] += 1
            per_document[item["source_id"]] += 1
            chosen.append(item)
    return chosen, drops


def quota_problems(sentences, lex, minimum=MIN_SENTENCES, maximum=MAX_SENTENCES, lemma_cap=LEMMA_CAP_BUILD, document_cap=DOCUMENT_CAP):
    """[message] for every miss of the quota gate (rule 6) of built sentences (dicts of `common.write_corpus`)."""
    found = []
    if len(sentences) < minimum:
        found.append("%d sentences, at least %d needed" % (len(sentences), minimum))
    if len(sentences) > maximum:
        found.append("%d sentences, at most %d allowed" % (len(sentences), maximum))
    lemmas = collections.Counter(shapes_dev.lemma_of(s["rows"][0][0], lex) for s in sentences if s["rows"])
    documents = collections.Counter(s["source_id"] for s in sentences)
    found += ["lemma %s has %d sentences, at most %d allowed" % (lemma, n, lemma_cap) for lemma, n in sorted(lemmas.items()) if n > lemma_cap]
    found += ["document %s has %d sentences, at most %d allowed" % (doc, n, document_cap) for doc, n in sorted(documents.items()) if n > document_cap]
    for s in sentences:
        rows = s["rows"]
        if not rows or rows[0][1] != "VERB":
            found.append("%s: token 0 is not tagged VERB" % s["sent_id"])
        elif rows[0][0].lower() != shapes_dev.base_form_of(rows[0][0], lex):
            found.append("%s: token 0 is not in the base form" % s["sent_id"])
        if any(common.base_status(row[2]) != "unanimous" for row in rows):
            found.append("%s: a token is not unanimous" % s["sent_id"])
        if "".join(row[0] for row in rows) != s["text"].replace(" ", ""):
            found.append("%s: the tokens do not rejoin to the text" % s["sent_id"])
    return found


def build(work, out_dir, seed=SEED, lemma_cap=LEMMA_CAP_BUILD, document_cap=DOCUMENT_CAP, maximum=MAX_SENTENCES, minimum=MIN_SENTENCES,
          allow_short=False, data_dir=None):
    """Write imperative.tsv, sources.tsv, excluded-documents.tsv and dropped-sentences.tsv into `out_dir`; returns (sentences, report lines).

    Fail closed: a missed quota (`BuildError`, nothing written; `allow_short` lowers only the minimum) and an excluded or unknown document."""
    data_dir = data_dir or common.DATA
    lex = shapes_dev.lexicon_of(data_dir)
    items, tagged = shapes_dev.read_all_decided(work, data_dir)
    candidates = {c["cand_id"]: c for c in shapes_dev.read_all_candidates(work)}
    draft = shapes_dev.read_draft(work)
    exclusions = shapes_dev.load_exclusions(work, data_dir)
    drops = collections.Counter()
    kept, sentences = [], {}
    for item in items:
        if item["shape"] != SHAPE or item["role"] != "target":
            continue
        rows = tagged.get(item["sent_key"])
        reason = "undecided" if rows is None else keep_reason(item, rows, lex)
        if reason:
            drops[reason] += 1
            continue
        cand = candidates[item["cand_id"]]
        sentences[item["sent_key"]] = dict(sent_id="", source_id=item["source_id"], text=cand["text"], rows=[list(r) for r in rows])
        kept.append(dict(sent_key=item["sent_key"], lemma=item["lemma"], source_id=item["source_id"]))
    labeled = sum(drops.values()) + len(kept)
    temporary = {}
    for number, entry in enumerate(kept, 1):
        sentence = sentences[entry["sent_key"]]
        temporary["tmp-%d" % number] = (entry, dict(sentence, sent_id="tmp-%d" % number))
    fixed = fix_tier_rules([pair[1] for pair in temporary.values()], data_dir)
    after_lint = []
    for name, (entry, _) in temporary.items():
        if name in fixed:
            drops["fix-tier"] += 1
        else:
            after_lint.append(entry)
    chosen, cap_drops = select_sentences(after_lint, seed, lemma_cap, document_cap, maximum)
    drops.update(cap_drops)
    built = []
    for number, entry in enumerate(sorted(chosen, key=lambda c: shapes_dev.rank_of(seed, "imperative-order", c["sent_key"])), 1):
        built.append(dict(sentences[entry["sent_key"]], sent_id=SENT_ID % number))
    problems = quota_problems(built, lex, 0 if allow_short else minimum, maximum, lemma_cap, document_cap)
    unknown = sorted({s["source_id"] for s in built} - {row["source_id"] for row in draft})
    if unknown:
        raise BuildError("no source row for: " + ", ".join(unknown))
    refused = exclusions.problems(draft, built)
    if refused:
        raise shapes_dev.ExcludedSourceError("excluded sources are used:\n  " + "\n  ".join(refused[:20]))
    if problems:
        raise BuildError("the quota gate is not met (--allow-short lowers only the minimum):\n  " + "\n  ".join(problems[:20]))
    common.write_corpus(os.path.join(out_dir, CORPUS_NAME), common.with_flag_documentation(HEADER, built), built)
    adjudicate.write_rows(os.path.join(out_dir, SOURCES_NAME), shapes_dev.SOURCE_COLUMNS, shapes_dev.sources_rows(draft, built))
    rejected_path = work.private_file("rejected-documents.tsv")
    rejected = sorted(adjudicate.read_records(rejected_path)[1], key=lambda r: r["source_id"]) if os.path.exists(rejected_path) else []
    common.write_text(os.path.join(out_dir, EXCLUDED_NAME), [
        "# Documents left out of imperative.tsv because the document is a reprint of, or shares sentences with, a training or evaluation set, or was already used",
        "# (only ids and reasons, never text)", "# columns = document_id, reason"] + ["%s\t%s" % (r["source_id"], r["reason"]) for r in rejected])
    common.write_text(os.path.join(out_dir, DROPPED_NAME), [
        "# Candidate sentences dropped alone because they equal or nearly equal a sentence of a training or evaluation set or of the shapes development set",
        "# (counted in private/candidates-report.md of the work folder; only counts are published, never text)", "# columns = reason, sentences"]
        + _drop_rows(work))
    report = build_report(len(built), labeled, drops, built, draft, lex, lemma_cap, document_cap)
    return built, report


def _drop_rows(work):
    """The `reason<TAB>count` rows of the sentence-level drops of the candidate stage that the report of the work folder lists."""
    path = work.private_file("candidates-report.md")
    if not os.path.exists(path):
        return []
    for line in common.read_lines(path):
        if line.startswith("sentences dropped before the frame: ") and not line.endswith(": none"):
            pairs = [part.rsplit(" ", 1) for part in line.split(": ", 1)[1].split(", ")]
            return ["%s\t%s" % (reason, count) for reason, count in pairs if reason in (EQUALS_SHAPES_DEV, NEAR_SHAPES_DEV, "equals-a-set-sentence", "probe-5gram")]
    return []


def build_report(count, labeled, drops, built, draft, lex, lemma_cap, document_cap):
    """Markdown counts of a build: drops per reason, per family, lemma and document statistics."""
    family = {row["source_id"]: row["family"] for row in draft}
    lemmas = collections.Counter(shapes_dev.lemma_of(s["rows"][0][0], lex) for s in built)
    documents = collections.Counter(s["source_id"] for s in built)
    lines = ["# Build", "", "%d sentences written to %s (labeled candidates with a decision: %d)" % (count, CORPUS_NAME, labeled), "",
             "| dropped because | sentences |", "| --- | --- |"]
    lines += ["| %s | %d |" % item for item in sorted(drops.items())]
    families = collections.Counter(family[s["source_id"]] for s in built)
    lines += ["", "| family | documents | sentences |", "| --- | --- | --- |"]
    for name in sorted(families):
        lines.append("| %s | %d | %d |" % (name, sum(1 for d in documents if family[d] == name), families[name]))
    lines += ["", "- lemmas: %d (at most %d per lemma; most frequent: %s)" % (
        len(lemmas), lemma_cap, ", ".join("%s %d" % item for item in sorted(lemmas.items(), key=lambda i: (-i[1], i[0]))[:8])),
        "- documents: %d (at most %d per document)" % (len(documents), document_cap),
        "- tokens: %d" % sum(len(s["rows"]) for s in built)]
    return lines


def _command_build(args):
    work = adjudicate.Work(args.work)
    out_dir = args.out_dir or os.path.join(args.data_dir, "imperative")
    try:
        built, report = build(work, out_dir, args.seed, args.lemma_cap, args.document_cap, args.max, args.min, args.allow_short, args.data_dir)
    except (BuildError, shapes_dev.ExcludedSourceError, ValueError) as error:
        print("build refused: %s" % error, file=sys.stderr)
        return 1
    adjudicate._print(report)
    common.write_text(work.private_file("build-report.md"), report)
    return 0


# ---------------------------------------------------------------- hand check (the precision gate)

def read_built(out_dir):
    """The sentences of imperative.tsv of `out_dir`."""
    return common.read_corpus(os.path.join(out_dir, CORPUS_NAME))[1]


def build_sample(sentences, seed=SEED, size=SAMPLE_SIZE, controls=SAMPLE_CONTROLS):
    """[(kind, sentence, shown tag)] shuffled: `size` random sentences with the agreed tag of token 0, and `controls` other sentences
    whose token 0 is shown with the swapped tag NOUN (the right verdict is `incorrect`; a reviewer that accepts more than
    CONTROLS_ACCEPTED_MAX of them is too lenient)."""
    rng = random.Random(seed)
    ordered = sorted(sentences, key=lambda s: s["sent_id"])
    picked = rng.sample(ordered, min(size, len(ordered)))
    rest = [s for s in ordered if s not in picked]
    swapped = rng.sample(rest, min(controls, len(rest)))
    result = [("real", s, s["rows"][0][1]) for s in picked] + [("control", s, "NOUN") for s in swapped]
    rng.shuffle(result)
    return result


def sample_lines(sample, prefix="R"):
    """(Markdown lines of the review file, key rows): every token is shown with its tag, token 0 with the proposed tag."""
    lines = list(REVIEW_INSTRUCTIONS)
    keys = []
    for number, (kind, sentence, shown) in enumerate(sample, 1):
        name = "%s%03d" % (prefix, number)
        shown_rows = [(row[0], shown if i == 0 else row[1]) for i, row in enumerate(sentence["rows"])]
        lines += ["## " + name, "", "Sentence: " + sentence["text"],
                  "Tagged: " + " ".join("%d:%s/%s" % (i, form, tag) for i, (form, tag) in enumerate(shown_rows)),
                  "Marked word: %s (index 0)" % sentence["rows"][0][0], "Proposed tag: " + shown, ""]
        keys.append((name, kind, sentence["sent_id"], SHAPE, 0, shown, sentence["rows"][0][1], len(sentence["rows"])))
    return lines, keys


def _command_sample(args):
    work = adjudicate.Work(args.work)
    out_dir = args.out_dir or os.path.join(args.data_dir, "imperative")
    sample = build_sample(read_built(out_dir), args.seed, args.size, args.controls)
    lines, keys = sample_lines(sample)
    common.write_text(work.private_file("review-sample.md"), lines)
    adjudicate.write_rows(work.private_file("review-sample-key.tsv"), SAMPLE_KEY_COLUMNS, keys)
    real = sum(1 for kind, _, _ in sample if kind == "real")
    print("%d items: %d real, %d control (seed %d)" % (len(sample), real, len(sample) - real, args.seed))
    return 0


ANSWER_RE = re.compile(r"^(R[0-9]{3,})\t(correct|incorrect|unclear)\t([^\t]*)(?:\t(.*))?$", re.I)


def parse_answers(text):
    """[(item, verdict, [other incorrect token indexes], cite)] from the reviewer's answer (other lines are ignored; a repeated item is an error)."""
    found, seen = [], set()
    for line in text.replace("\r\n", "\n").split("\n"):
        match = ANSWER_RE.match(line.strip().strip("`"))
        if not match:
            continue
        item = match.group(1).upper()
        if item in seen:
            raise ValueError("item %s is answered twice" % item)
        seen.add(item)
        cell = match.group(3).strip().lower()
        if cell in ("", "-", "none"):
            others = []
        elif re.fullmatch(r"[0-9]+(\s*,\s*[0-9]+)*", cell):
            others = sorted({int(part) for part in cell.split(",")})
        else:
            raise ValueError("item %s: the `others` cell must be `-` or token indexes separated by commas: %r" % (item, match.group(3)))
        found.append((item, match.group(2).lower(), others, (match.group(4) or "").strip()))
    return found


def review_numbers(keys, answers):
    """(target Counter of real items, control Counter, whole-sentence correct, reviewed real items, tokens, incorrect tokens, unanswered items)."""
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
        wrong += len([i for i in others if i != 0]) + (verdict == "incorrect")
        whole += verdict == "correct" and not others
    return target, controls, whole, reviewed, tokens, wrong, unanswered


def review_lines(target, controls, whole, reviewed, tokens, wrong, unanswered):
    """(lines, passed): the Markdown result of the model review and whether the gate (rule 7) holds."""
    judged = target["correct"] + target["incorrect"]
    precision = target["correct"] / judged if judged else None
    lenient = controls["correct"] > CONTROLS_ACCEPTED_MAX
    passed = precision is not None and precision >= PRECISION_MIN and not lenient and not unanswered
    lines = ["# Review result", "",
             "- reviewed sentences: %d (target correct %d, incorrect %d, unclear %d)" % (reviewed, target["correct"], target["incorrect"], target["unclear"]),
             "- target-token precision: %s (gate: at least %.0f%%)" % ("-" if precision is None else "%.1f%%" % (100 * precision), 100 * PRECISION_MIN),
             "- whole-sentence precision (target correct and no other token marked wrong): %s" % ("-" if not reviewed else "%.1f%%" % (100 * whole / reviewed)),
             "- token precision (reported, not gated): %s (%d wrong of %d tokens)" % ("-" if not tokens else "%.2f%%" % (100 * (1 - wrong / tokens)), wrong, tokens),
             "- control items accepted as correct: %d of %d%s" % (controls["correct"], sum(controls.values()), " (TOO LENIENT, repeat the review once)" if lenient else ""),
             "- unanswered items: %d" % len(unanswered),
             "- gate: " + ("PASSED" if passed else "STOP: nothing else proceeds; report the result")]
    return lines, passed


def _command_review_result(args):
    work = adjudicate.Work(args.work)
    _, keys = adjudicate.read_records(work.private_file("review-sample-key.tsv"))
    answers = []
    for name in args.verdicts:
        with open(name, "r", encoding="utf-8") as handle:
            answers += parse_answers(handle.read())
    unknown = {a[0] for a in answers} - {k["item"] for k in keys}
    if unknown:
        raise ValueError("verdicts for unknown items: " + ", ".join(sorted(unknown)[:5]))
    lines, passed = review_lines(*review_numbers(keys, answers))
    adjudicate._print(lines)
    common.write_text(work.private_file("review-result.md"), lines)
    return 0 if passed else 1


# ---------------------------------------------------------------- report

def report_lines(out_dir, data_dir):
    """Markdown counts of the built source for the README: sizes, per family, lemmas, documents, lexicon coverage (no rebuild of the lexicon)."""
    lex = shapes_dev.lexicon_of(data_dir)
    sentences = read_built(out_dir)
    draft = {}
    with open(os.path.join(out_dir, SOURCES_NAME), encoding="utf-8") as handle:
        for line in handle.read().split("\n")[1:]:
            cells = line.split("\t")
            if len(cells) == len(shapes_dev.SOURCE_COLUMNS):
                draft[cells[0]] = dict(zip(shapes_dev.SOURCE_COLUMNS, cells))
    tokens = pairs = known_tokens = known_pairs = 0
    unattested = collections.Counter()
    seen_pairs = set()
    for s in sentences:
        for form, tag, _ in s["rows"]:
            if not common.has_letter_or_digit(form):
                continue
            tokens += 1
            if tag in lex.get(form.lower(), ()):
                known_tokens += 1
            if (form.lower(), tag) not in seen_pairs:
                seen_pairs.add((form.lower(), tag))
                pairs += 1
                if tag in lex.get(form.lower(), ()):
                    known_pairs += 1
                else:
                    unattested[(form.lower(), tag)] += 1
    families = collections.Counter(draft[s["source_id"]]["family"] for s in sentences)
    lemmas = collections.Counter(shapes_dev.lemma_of(s["rows"][0][0], lex) for s in sentences)
    documents = collections.Counter(s["source_id"] for s in sentences)
    lines = ["| family | documents | sentences |", "| --- | --- | --- |"]
    for name in sorted(families):
        lines.append("| %s | %d | %d |" % (name, sum(1 for d in documents if draft[d]["family"] == name), families[name]))
    lines += ["", "- sentences: %d, tokens: %d, lemmas: %d, documents: %d" % (len(sentences), sum(len(s["rows"]) for s in sentences), len(lemmas), len(documents)),
              "- most sentences per lemma: %d, per document: %d" % (max(lemmas.values(), default=0), max(documents.values(), default=0)),
              "- lexicon coverage: %d of %d word tokens have their tag listed for their spelling in lexicon.tsv; %d of %d distinct (spelling, tag) pairs"
              % (known_tokens, tokens, known_pairs, pairs),
              "- first unattested pairs: " + ", ".join("%s/%s" % pair for pair in sorted(unattested)[:12])]
    return lines


def _command_report(args):
    adjudicate._print(report_lines(args.out_dir or os.path.join(args.data_dir, "imperative"), args.data_dir))
    return 0


# ---------------------------------------------------------------- command line

def main(argv=None):
    parser = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    parser.add_argument("--work", required=True, help="work folder (outside the repository)")
    parser.add_argument("--data-dir", default=common.DATA, help="data folder (default: the repository's data/)")
    sub = parser.add_subparsers(dest="command", required=True)
    exclusions = sub.add_parser("exclusions", help="index of the documents already used; checks the draft")
    exclusions.add_argument("--extra-table", action="append", default=None, metavar="PATH", help="a further table of used documents (repeatable)")
    exclusions.add_argument("--check", nargs="*", default=None, help="document ids or URLs to check")
    candidates = sub.add_parser("candidates", help="the v-imperative-initial candidates of the sourced text; apply the leakage filters")
    candidates.add_argument("--lemma-cap", type=int, default=LEMMA_CAP_CANDIDATES)
    candidates.add_argument("--max-candidates", type=int, default=MAX_CANDIDATES, help="total cap of the candidates, taken in the seeded order")
    candidates.add_argument("--seed", type=int, default=SEED)
    candidates.add_argument("--no-ud", action="store_true", help="ignore the UD cache even when present")
    candidates.add_argument("--extra-table", action="append", default=None, metavar="PATH")
    candidates.add_argument("--extra-text", action="append", default=None, metavar="DIR", help="a folder of cleaned shapes-dev document text (repeatable)")
    candidates.add_argument("--min-candidates", type=int, default=MIN_CANDIDATES)
    candidates.add_argument("--min-lemmas", type=int, default=MIN_LEMMAS)
    build_parser = sub.add_parser("build", help="write imperative.tsv, sources.tsv, excluded-documents.tsv and dropped-sentences.tsv")
    build_parser.add_argument("--out-dir", default=None, help="default: <data dir>/imperative")
    build_parser.add_argument("--seed", type=int, default=SEED)
    build_parser.add_argument("--lemma-cap", type=int, default=LEMMA_CAP_BUILD)
    build_parser.add_argument("--document-cap", type=int, default=DOCUMENT_CAP)
    build_parser.add_argument("--max", type=int, default=MAX_SENTENCES)
    build_parser.add_argument("--min", type=int, default=MIN_SENTENCES)
    build_parser.add_argument("--allow-short", action="store_true", help="build below the minimum (the other gates stay)")
    sample = sub.add_parser("sample", help="the review sample for the precision gate")
    sample.add_argument("--out-dir", default=None)
    sample.add_argument("--seed", type=int, default=SEED)
    sample.add_argument("--size", type=int, default=SAMPLE_SIZE)
    sample.add_argument("--controls", type=int, default=SAMPLE_CONTROLS)
    review = sub.add_parser("review-result", help="precision of the review; exit 1 when the gate is not met")
    review.add_argument("--verdicts", nargs="+", required=True)
    report = sub.add_parser("report", help="counts of the built source for the README")
    report.add_argument("--out-dir", default=None)
    args = parser.parse_args(argv)
    handlers = {"exclusions": _command_exclusions, "candidates": _command_candidates, "build": _command_build, "sample": _command_sample,
                "review-result": _command_review_result, "report": _command_report}
    try:
        return handlers[args.command](args)
    except (ValueError, shapes_dev.ExcludedSourceError) as error:
        print("%s refused: %s" % (args.command, error), file=sys.stderr)
        return 1


if __name__ == "__main__":
    sys.exit(main())

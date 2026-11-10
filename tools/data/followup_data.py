"""Builder of the follow-up data of PR #19 (Python 3, standard library only; imports the repository's data tools read-only).

PR #19 added two training sources (`compound`, `headings`) and two development tranches (`compound-head`, `headings`), retrained once and kept BASE: the compound
development measure had 100 targets, and the real Title Case imperative headings were almost absent (3 strong-frame lines in 196,019). This tool builds the
follow-up, four products from NEW US federal public-domain documents, none of them used before, and a pilot that decides whether the imperative-heading work is
worth scaling:

  compound-head-2    development tranche (files data/eval/shapes-dev/compound-head-2*.tsv): shape n-compound-head-ambiguous, the contested band of the pinned WordSense Basic mass
  headings-2         development tranche (data/eval/shapes-dev/headings-2*.tsv): shape heading-titlecase, kinds imperative | noun | proper (controls)
  compound-2         training source `compound2` (data/compound/compound-2*.tsv): every frame hit, a seeded order, no band
  headings-2-train   training source `headings2` (data/headings/headings-2*.tsv): strata imperative and the contrast stratum initial-noun

No model of this project is used: the tool never calls a tagger, reads no weights and no prediction, and a data test scans its source for that (the only
non-frame input is the Basic mass of the WordSense library, the pinned `hmm.tsv`, for the development band of compound-head-2, as in PR #18). It reuses the
frozen blind-labeling commands of shapes_dev.py (`export`, `check-batches`, `validate-output`, `status`, `decide` with `--round`).

THE PRE-REGISTERED RULE (committed before any document of this follow-up is fetched; the normative text is this docstring, the short form is in the trainer RUN-LOG.md,
section "Follow-up: pre-registered rule (before any fetch)")

1. Documents. Only US federal authorship with a public-domain basis checked one by one and written in the `license_basis` cell (17 U.S.C. 105, the agency named in the record,
   the record's own rights statement, NTRS rights record for NASA). No FAA material, nothing in either excluded-sources.tsv (the committed one and W/excluded-sources.tsv),
   nothing in any excluded-documents.tsv, no document with a state, local, foreign, university, contractor or private creator, publisher or co-author (the author, creator and
   publisher fields of every record are read, not only the title), no copyright notice or third-party credit, no share-alike or restricted text, no Wikipedia, no UD text, no
   sentence supplied by any consumer (no consumer text is read, quoted or used). Military manuals: a distribution statement other than public release, FOUO or an export-control or
   classification marking is a reason to reject. The used-document index holds ALL earlier tables: the open, gold v2, silver and MASC tables, all five shapes-dev tables, the
   imperative source, data/compound and data/headings (sources.tsv and excluded-documents.tsv of both) and every `--extra-table` (the earlier sourcing drafts); a document is rejected
   as a whole when 3 or more of its sentences equal a training, evaluation or shapes-dev sentence (the held-out sets enter only as the opaque hash set of `shapes_dev.LeakageFilter`).
2. Partition (before any label). Every document gets a family key (the lower-case title without volume, edition, year, series number and `skill level`; two scans of one
   publication share it). The side of a family is `dev` when the seeded rank `rank_of(20261110, "partition", family key)` read as a fraction of 16**12 is below the DEV share of its stream
   (stream C 0.35; stream H 0.30, fixed in the R1 commit and never changed afterwards; a family with a member of stream H counts as stream H). All members of a family are on one side. DEV documents
   feed compound-head-2 and headings-2 only, TRAIN documents compound-2 and headings-2-train only; no document is on both sides. The result is W/partition.tsv.
3. Heading lines. A line comes from (a) `shapes_dev_extra.heading_lines` (stands alone above a paragraph), (b) a table-of-contents line: the dot leaders (three or more dots) or the OCR residue of
   dot leaders (`ccceeee`) and the page number or range at the end are removed, or (c) a labeled line: `Task`, `Step`, `Procedure`, `Operation`, `Exercise` or `Job` followed by a number or
   letter, a decimal number (`2.1`) or `(a)`, removed with the label (no paragraph needs to follow (b) and (c)). Then the unchanged `shapes_dev_extra.heading_noise_reason` filter (2 to 10 tokens,
   Title Case, no all-capital word, characters, no unknown word, no page number), and the same equal and near-duplicate filters as the sentences. A title that stands in the table of
   contents, on its task page and in a cross reference is one unit (duplicate by sentence key). ALL-CAPS and sentence-case step titles are out of scope.
4. Frames (position and lexicon only). imperative (F-IMP, strength 0 only): token 0 capitalized, its own base form, listed with VERB (a verb-only word, or noun and verb), and token 1 a
   determiner, an object pronoun, an adverb of `shapes_dev.FOLLOWING_ADVERBS` or a particle (`up down out off back away`); this is exactly the old strength-0 set, with verb-only words added.
   The old strength-1 case (token 1 a capitalized noun-capable word, `Mass Characteristics`) is NOT an imperative candidate: it is the `initial-noun` frame (training only, token 0 listed noun and
   verb, its own base form, token 1 a capitalized noun-capable word). The noun and proper frames of the development headings are `shapes_dev_extra.heading_hits` unchanged. A line is one candidate; the
   rarest frame takes it (proper, imperative, noun). Compound: `shapes_dev_extra.compound_ambiguous_hit` unchanged, sentences of 3 to 30 tokens.
5. Candidates (seeded order `rank_of(20261110, ...)`, product and frame in the label). compound-head-2: the band 0.10 <= q < 0.97 of the pinned Basic mass, q ascending, at most 4 targets per head lemma
   counted over the union with the 100 old targets of compound-head.tsv, 800 target candidates, controls (no band, 2 per lemma) 50 in the first round for the lemmas of the targets and 30 in a second
   round for the lemmas with room left. headings-2: imperative 300, noun 120, proper controls 60, 4 per lemma over imperative and noun, 20 per document, controls 2 per lemma; a development line
   whose lower-case token set has Jaccard similarity 0.8 or more with any training heading line (old, new, or on the TRAIN side of the pool) is dropped and counted. compound-2: every frame hit,
   at most 6 per head lemma, 3,400 in all (gate: at least 2,000 candidates and 400 head lemmas). headings-2-train: imperative at most 6 per lemma, 40 per document, 900 in all (gate: at least 300);
   initial-noun at most 450. The pilot lines (rule 6) are not drawn again and count against the caps of their side.
6. Pilot and gate. The pilot is the first 150 imperative candidate lines of the wave-1 documents in the seeded order `rank_of(20261110, "pilot", line key)`, at most 4 per first-word lemma, 20 per
   document and 50 per family, both labelers, blind. A line is CONFIRMED when both labelers tag token 0 VERB. Gate: confirmed / 150 of at least 0.50 (75 of 150) proceeds; FEWER THAN 75 OF 150 STOPS
   the work before any scaling and writes the pilot record (counts only); fewer than 150 candidate lines is also a stop (scarcity). The pilot lines keep the side of their document and are not
   labeled again.
   AMENDMENT A1 (after the scarcity stop, before any label; approved by the owner through the orchestrator; RUN-LOG section "Follow-up: pre-label relaxation (amendment A1)"). Cause: scarcity (51 lines
   under the registered caps; 234 strength-0 lines exist). (a) The family cap of 50 is read at TITLE-family level (`family_key`), not agency level. (b) The pilot lemma cap is 6 instead of 4 (the
   pilot only). (c) The pilot is about 150 lines, as the owner specified: it is however many lines these caps give, at most PILOT_SIZE (150) and at least PILOT_MIN_SIZE (140); fewer than 140 is
   a scarcity stop. (d) The gate is confirmed >= ceil(n/2) of the n lines piloted (one fewer stops: INCOMPLETE). (e) For the final build of headings-2 and headings-2-train the lemma cap is 6 and
   the development document cap is 20 (counts in the RUN-LOG); the floors are not changed and `--allow-short` and the registered contingencies stand.
7. Labels. Two blind labelers, `claude-opus-5.5` and `gpt-6.1-sol`, from scratch, the frozen prompt of PR #19 (see LABELER_TEMPLATE), one agent per labeler and batch, at most 4 at a time, no re-roll,
   through the unchanged shapes_dev.py commands. Training: every token agreed, no fix-tier violation, the judged word with the exact tag of its stratum (VERB in base form for imperative, NOUN for
   initial-noun, head NOUN or PROPN or VERB after an agreed noun for compound-2); anything else is dropped and counted, nothing is edited by hand. Development: the target agreed, the intended
   class, the frame after labeling, the judged word never disputed.
8. Caps and floors (seeded order). compound-head-2: targets 150 to 210, controls 30 to 45, 4 per lemma over the union with the old targets, 2 per control lemma, `-ing` heads at most 25 percent of the union
   of 310. headings-2: imperative 80 to 130, noun 30 to 50, proper controls 12 to 20, 4 per lemma and 12 per document over imperative and noun. compound-2: 600 to 1,100 sentences, at least 200 head lemmas,
   4 per lemma, 20 per document, verb items at most 15 percent. headings-2-train: imperative 150 to 400, initial-noun at most half of the kept imperative lines, 4 per lemma and 25 per document over both strata.
   `build` refuses below a floor unless `--allow-short` (the miss is disclosed, never padded) and never waives a cap.
9. Hand check (a model review, not a human review). `claude-sonnet-5.5`, 60 kept items and 4 swapped-tag controls per product, one draw with the seeds in SAMPLE_SEEDS (compound-head-2 60 targets;
   headings-2 imperative 30, noun 15, proper 15; compound-2 60; headings-2-train imperative 40, initial-noun 20). Gate: marked-word precision at least 95 percent (3 incorrect of 60 passes, 4 fail), at most 1 of
   4 controls accepted, no unanswered item; below it the product is not registered.
10. Format and amendments as in compound_heading_data.py (block format of data/open/train.tsv; ids `shapes-dev-compound-head-2-NNNN`, `shapes-dev-headings-2-NNNN`, `compound2-NNNN`, `headings2-NNNN`;
   `--omit-document ID[=REASON]` removes a document found unsound after the caps, nothing is added and the ids stay without a gap).

Work folder W (`--work W`, outside the repository): the layout of compound_heading_data.py plus W/streams.tsv (source_id, stream C|H), W/partition.tsv and the rounds hdg-pilot, chd2, chd2c,
hdg2dev, cmp2 and hdg2.

Commands (`--data-dir D` replaces the repository's data folder, for tests; --extra-table and --extra-text as in imperative_data.py):

  python tools/data/followup_data.py --work W exclusions [--extra-table PATH ...] [--check ID_OR_URL ...]
  python tools/data/followup_data.py --work W partition
  python tools/data/followup_data.py --work W lines                                  # heading, table-of-contents and labeled step-title lines
  python tools/data/followup_data.py --work W tokenize [--cmd COMMAND]
  python tools/data/followup_data.py --work W preflight [--extra-table PATH ...] [--extra-text DIR ...] [--no-ud]      # counts only
  python tools/data/followup_data.py --work W pilot [--documents FILE] [--extra-table PATH ...] [--no-ud]            # round hdg-pilot
  python tools/data/followup_data.py --work W pilot-result                                                            # the gate; exit 1 = STOP
  python tools/data/followup_data.py --work W candidates --product P [--round chd2c] [--extra-table PATH ...] [--no-ud]
  (then: shapes_dev.py --work W export / check-batches / validate-output / status / decide --round R)
  python tools/data/followup_data.py --work W build --product P [--out-dir D] [--allow-short] [--omit-document ID[=REASON] ...]
  python tools/data/followup_data.py --work W sample --product P [--out-dir D] ; review-result --product P --verdicts FILE ... ; report --product P
  python tools/data/followup_data.py --work W prompt --round R --batch B --model M                                    # the frozen labeler prompt and its appendix
"""
import argparse
import collections
import hashlib
import math
import os
import random
import re
import sys

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import adjudicate
import common
import compound_heading_data as chd
import imperative_data
import shapes_dev
import shapes_dev_extra as sx

SEED = shapes_dev.SEED
PRODUCTS = ("compound-head-2", "headings-2", "compound-2", "headings-2-train")
SIDE = {"compound-head-2": "dev", "headings-2": "dev", "compound-2": "train", "headings-2-train": "train"}
KIND_OF_PRODUCT = {"compound-head-2": "compound", "headings-2": "headings", "compound-2": "compound", "headings-2-train": "headings"}
# (output folder under the data folder, corpus, sources table) of every product
OUTPUT = {"compound-head-2": ("eval/shapes-dev", "compound-head-2.tsv", "compound-head-2-sources.tsv"),
          "headings-2": ("eval/shapes-dev", "headings-2.tsv", "headings-2-sources.tsv"),
          "compound-2": ("compound", "compound-2.tsv", "compound-2-sources.tsv"),
          "headings-2-train": ("headings", "headings-2.tsv", "headings-2-sources.tsv")}
EXCLUDED_OUT = {"compound-2": "compound-2-excluded-documents.tsv", "headings-2-train": "headings-2-excluded-documents.tsv"}
DROPPED_OUT = {"compound-2": "compound-2-dropped-sentences.tsv", "headings-2-train": "headings-2-dropped-sentences.tsv"}
SENT_ID = {"compound-head-2": "shapes-dev-compound-head-2-%04d", "headings-2": "shapes-dev-headings-2-%04d", "compound-2": "compound2-%04d",
           "headings-2-train": "headings2-%04d"}
ROUNDS = {"compound-head-2": ("chd2", "chd2c"), "headings-2": ("hdg-pilot", "hdg2dev"), "compound-2": ("cmp2",), "headings-2-train": ("hdg-pilot", "hdg2")}
PILOT_ROUND, CONTROLS_ROUND = "hdg-pilot", "chd2c"
CANDIDATE_ROUND = {"compound-head-2": "chd2", "headings-2": "hdg2dev", "compound-2": "cmp2", "headings-2-train": "hdg2"}
CAND_PREFIX = {"compound-head-2": "c2-", "compound-head-2-controls": "k2-", "headings-2": "h2d-", "compound-2": "cm2-", "headings-2-train": "h2i-",
               "initial-noun": "h2n-", "pilot": "hp-"}
COMPOUND_SHAPE = chd.COMPOUND_SHAPE
IMPERATIVE_SHAPE, NOUN_SHAPE, PROPER_SHAPE = "heading-imperative", "heading-noun", "heading-proper"
HEADING_KIND = {NOUN_SHAPE: "noun", IMPERATIVE_SHAPE: "imperative", PROPER_SHAPE: "proper"}
INITIAL_NOUN = "initial-noun"
MODEL_FREE = "no PartOfSpeech model, prediction or score is read"

# ---- pre-registered pins of the inputs (set by the R1 commit; `require_pins` refuses any candidate command while one is None)
# R1 (stream H pilot stage): sources-draft.tsv (70 documents), tokens.tsv. HMM_SHA256 stays None until R3: hmm.tsv exists only for the compound-head-2 documents (stream C,
# not yet sourced) and no pilot or heading command reads it. R3 re-pins the draft and tokens files for the extended document list (the stream H documents and their partition
# are unchanged by it); the DEV share of stream H below is frozen at R1 and never changed.
DRAFT_SHA256 = "7f05b400e33b10eef46b02645d05f60d7d0107b9d8a041cdc4ea4560361d49ca"
TOKENS_SHA256 = "bb7df8426cc1ed847ba89e74fa747c28992302b2590fa517a9098b313b7a35e0"
HMM_SHA256 = None

# ---- partition
DEV_SHARE = {"C": 0.35, "H": 0.30}
STREAMS = ("C", "H")
PARTITION_FILE, STREAMS_FILE = "partition.tsv", "streams.tsv"
SERIES_WORDS = re.compile(r"\b(?:skill\s+levels?|volumes?|vol|editions?|ed|revised|rev|changes?|no|number|nos)\b\.?\s*[0-9ivx]*", re.I)

# ---- development products
OLD_TARGETS = 100                                           # the targets of compound-head.tsv (the union for the lemma cap and the -ing share)
LEMMA_CAP, CONTROL_LEMMA_CAP, ING_SHARE = 4, 2, 0.25
DEV_HEADING_LEMMA_CAP = 6                                   # headings-2 (amendment A1, final build): 6 per lemma over imperative and noun (compound-head-2 keeps LEMMA_CAP)
COMPOUND_HEAD_BUDGET, CONTROL_POOL_FIRST, CONTROL_POOL_SECOND = 800, 50, 30
COMPOUND_HEAD_MIN_CANDIDATES = 450
DEV_QUOTAS = {"compound-head-2": {"targets": (150, 210), "controls": (30, 45)},
              "headings-2": {"imperative": (80, 130), "noun": (30, 50), "controls": (12, 20)}}
DEV_HEADING_BUDGET = {"imperative": 300, "noun": 120, "proper": 60}
DEV_HEADING_MIN_CANDIDATES = {"imperative": 160, "noun": 60, "proper": 24}
DEV_CANDIDATE_DOCUMENT_CAP, DEV_BUILD_DOCUMENT_CAP = 20, 20                  # amendment A1: the build cap of headings-2 is 20 (was 12), the pilot cap
JACCARD_DROP = 0.8

# ---- training products
CMP2_CANDIDATE_LEMMA_CAP, CMP2_MAX_CANDIDATES, CMP2_MIN_CANDIDATES, CMP2_MIN_LEMMAS = 6, 3400, 2000, 400
CMP2_LEMMA_CAP, CMP2_DOCUMENT_CAP, CMP2_MIN, CMP2_MAX, CMP2_MIN_BUILD_LEMMAS = 4, 20, 600, 1100, 200
CMP2_NOUN_CAP, CMP2_VERB_CAP, CMP2_VERB_PERCENT = 935, 165, 15
HDG2_CANDIDATE_LEMMA_CAP, HDG2_CANDIDATE_DOCUMENT_CAP, HDG2_MAX_IMPERATIVE, HDG2_MAX_INITIAL_NOUN, HDG2_MIN_IMPERATIVE = 6, 40, 900, 450, 300
HDG2_LEMMA_CAP, HDG2_DOCUMENT_CAP = 6, 25                   # amendment A1: the build lemma cap of headings2 is 6 (was 4)
HDG2_IMPERATIVE_FLOOR, HDG2_IMPERATIVE_CAP = 150, 400
HDG2_INITIAL_NOUN_SHARE_NUM, HDG2_INITIAL_NOUN_SHARE_DEN = 1, 2          # initial-noun lines at most 1/2 of the kept imperative lines

# ---- pilot
# Amendment A1 (before any label): PILOT_SIZE is the maximum ("about 150"), PILOT_MIN_SIZE the scarcity floor, the lemma cap 6 (was 4), the family cap is read at title-family level.
PILOT_SIZE, PILOT_MIN_SIZE, PILOT_LEMMA_CAP, PILOT_DOCUMENT_CAP, PILOT_FAMILY_CAP = 150, 140, 6, 20, 50


def pilot_confirmed_min(sampled):
    """The gate of the pilot: ceil(sampled / 2) lines must be confirmed (one fewer stops the work)."""
    return (sampled + 1) // 2

# ---- hand check
SAMPLE_SIZE, SAMPLE_CONTROLS = 60, 4
SAMPLE_SEEDS = {"compound-head-2": 20261121, "headings-2": 20261122, "compound-2": 20261123, "headings-2-train": 20261124}
SAMPLE_PLAN = {"compound-head-2": (("target", 60),), "headings-2": (("imperative", 30), ("noun", 15), ("proper", 15)),
               "compound-2": (("*", 60),), "headings-2-train": (("imperative", 40), (INITIAL_NOUN, 20))}
PRECISION_MIN, CONTROLS_ACCEPTED_MAX = shapes_dev.PRECISION_MIN, shapes_dev.CONTROLS_ACCEPTED_MAX

# ---- old tables and corpora that a new product must stay disjoint from
OLD_PRODUCT_TABLES = ("compound/sources.tsv", "compound/excluded-documents.tsv", "headings/sources.tsv", "headings/excluded-documents.tsv")
OLD_TRAINING_CORPORA = ("compound/compound.tsv", "headings/headings.tsv")
OLD_HEADING_CORPUS = "headings/headings.tsv"
OLD_COMPOUND_HEAD = "eval/shapes-dev/compound-head.tsv"

# ---- the frozen labeler prompt of PR #19 (RUN-LOG, "Labeler prompt (frozen)"); <W> is the work folder, {ROUND}, {BATCH} and {MODEL} are filled in per run
LABELER_TEMPLATE = r"""You are a part-of-speech tagger. Tag every token of every text unit from scratch with a Universal POS tag.

Read exactly two files and nothing else:
1. C:\work\DemaConsulting\Libraries\PartOfSpeech\data\CONVENTIONS.md  (read it in full first; it is the tagging convention you must follow)
2. <W>\rounds\{ROUND}\batches\{BATCH}.tsv  (3 tab-separated columns with a header: id, token_index, token; one row per token, rows of one text unit are
   consecutive and token_index counts from 0; a text unit is a sentence or a short line)

Tag set (exactly these 17): ADJ ADP ADV AUX CCONJ DET INTJ NOUN NUM PART PRON PROPN PUNCT SCONJ SYM VERB X.
For every row give a tag, a confidence (high | medium | low) and flags (one of: -  mentioned  foreign  foreign+mentioned; `-` when none applies).
Decide each token from its context and the convention; the token column is what the tokenizer produced, keep it as it is.

Write the result as 6-column tab-separated parts with the header line `id<TAB>token_index<TAB>token<TAB>tag<TAB>confidence<TAB>flags`,
the first three cells copied byte for byte from the batch row (same order, every row, nothing added or dropped). Use parts of at most 400 rows,
each with its own header line, in the folder <W>\rounds\{ROUND}\batches\out\{MODEL}\ named {BATCH}.p01.tsv, {BATCH}.p02.tsv, ... (two digits, consecutive,
no gap). Create the files with your file-creation tool (no shell redirection).

Rules: do not read, list or search any other file or folder (no repository search, no web, no other batch, no other tagger's output, no
private folder); do not repair or post-process the output by script; do not ask questions; finish by listing the part files you wrote.
"""
PR19_TEMPLATE_SHA256 = "9f1da5238dbcd196ca95b0795194245846955d2bb3a95a70bf0540d112d17db5"       # the template with the work folder of PR #19 filled in (1,903 bytes)
PAUSE_FILE = r"C:\Users\mnixon\.copilot\session-state\217fb8fb-495d-4da7-89a1-b33f6a7805ad\files\PAUSE"
IP_PATH = r"C:\Users\mnixon\.copilot\session-state\b6862325-7069-4ff9-a4fc-abb8ecb60acc"
# Appended after the frozen block (hash recorded separately). The PAUSE rule is quoted verbatim; a labeler cannot update STATE.md or commit, so the
# orchestrator does the check before every launch and the labeler is told to write nothing and reply "PAUSED" when the file exists.
PAUSE_RULE = ("Before each heavy step (fetch, build, training run, labeling launch), check whether `" + PAUSE_FILE + "` exists. **If it does:** don't start the step; "
              "update `STATE.md` with the resume point, make a local WIP commit (no push), end the turn and reply \"PAUSED\".")
LABELER_APPENDIX = ("\nAdditional rules for this run (they add to the rules above and never relax them):\n"
                    "- " + PAUSE_RULE + "\n"
                    "- You cannot update STATE.md or commit: if that file exists, write nothing and reply \"PAUSED\". Testing whether that one path exists is the only other file access you are allowed.\n"
                    "- Never open anything under `" + IP_PATH + "` and never read or use any sentence supplied by a consumer.\n")


class BuildError(ValueError):
    """The build or a gate refuses (nothing is written)."""


class PinError(ValueError):
    """An input of the pre-registered selection is not the pinned one (or the pins are not set yet)."""


def labeler_prompt(work_folder, round_name, batch, model, appendix=True):
    """The prompt of one labeler run: the frozen template with the work folder, round, batch and model filled in (and the appendix)."""
    text = LABELER_TEMPLATE.replace("<W>", work_folder).replace("{ROUND}", round_name).replace("{BATCH}", batch).replace("{MODEL}", model)
    return text + (LABELER_APPENDIX if appendix else "")


def template_sha256(work_folder):
    """SHA-256 of the frozen template (with the placeholders {ROUND}, {BATCH} and {MODEL} kept) for a work folder, as recorded in the RUN-LOG."""
    return hashlib.sha256(LABELER_TEMPLATE.replace("<W>", work_folder).encode("utf-8")).hexdigest()


# ---------------------------------------------------------------- the used-document index (ALL earlier tables) and the sentence reference

def used_tables(data_dir, extra_tables=()):
    """The extra tables of the used-document index: `extra_tables`, the newer shapes-dev tranches' source tables and ALL tables of data/compound and data/headings
    (sources.tsv and excluded-documents.tsv); never the tables of the four products built here (a rebuild does not reject itself)."""
    tables = list(chd.used_tables(data_dir, extra_tables))
    for relative in OLD_PRODUCT_TABLES:
        path = os.path.join(data_dir, *relative.split("/"))
        if os.path.exists(path) and path not in tables:
            tables.append(path)
    return tables


def used_index(data_dir, exclusions, extra_tables=()):
    """{(kind, value): origin} of every document that training, evaluation, all shapes-dev tranches, the imperative source, `compound`, `headings`, their
    exclusion records and the extra tables used or excluded."""
    return imperative_data.used_index(data_dir, exclusions, sx.used_tables(data_dir, used_tables(data_dir, extra_tables)))


class Reference(chd.Reference):
    """The sentences that the new products stay disjoint from: all shapes-dev tranches, the imperative source, every `extra_text` folder and the
    two older training sources `compound` and `headings` (sentence keys and word 6-grams)."""

    def __init__(self, data_dir, extra_text=()):
        super().__init__(data_dir, extra_text)
        for relative in OLD_TRAINING_CORPORA:
            path = os.path.join(data_dir, *relative.split("/"))
            if os.path.exists(path):
                for sentence in common.read_corpus(path)[1]:
                    self.add(sentence["text"])
        self.keys.discard("")


def draft_gate(work, data_dir, extra_tables=(), omit=()):
    """Fail closed (`shapes_dev_extra.DraftError`) unless W/sources-draft.tsv is a sound list of new documents (the source and license gate, the index of ALL earlier tables)."""
    exclusions = shapes_dev.load_exclusions(work, data_dir)
    draft = [row for row in shapes_dev.read_draft(work) if row["source_id"] not in set(omit)]
    sx.require_sound_draft(work, draft, used_index(data_dir, exclusions, extra_tables), exclusions)


def require_pins(work, product=None):
    """Refuse (PinError) unless the pins are set and the inputs match them: sources-draft.tsv, tokens.tsv and, for compound-head-2, hmm.tsv."""
    needed = [("sources-draft.tsv", DRAFT_SHA256), ("tokens.tsv", TOKENS_SHA256)] + ([("hmm.tsv", HMM_SHA256)] if product == "compound-head-2" else [])
    for name, pinned in needed:
        if pinned is None:
            raise PinError("the pin of %s is not set: the R1 commit (frozen inputs) must come before any candidate" % name)
        actual = common.sha256_file(shapes_dev.shared_path(work, name))
        if actual != pinned:
            raise PinError("%s has SHA-256 %s, the pinned one is %s" % (name, actual, pinned))


# ---------------------------------------------------------------- family key and partition

def family_key(title):
    """The family of a document: its title in lower case without parentheses, years, volume, edition, change, `skill level` and series numbers (two scans or editions
    of one publication share it)."""
    text = re.sub(r"\([^)]*\)", " ", title.lower())
    text = SERIES_WORDS.sub(" ", text)
    text = re.sub(r"\b(?:1[5-9]|20)[0-9]{2}\b", " ", text)
    text = re.sub(r"\b[0-9]+(?:-[0-9]+)*\b", " ", text)
    return " ".join(re.findall(r"[a-z]+", text))


def side_of(family, stream, shares=None):
    """`dev` or `train`: the seeded rank of the family key read as a fraction of 16**12 against the DEV share of the stream."""
    share = (shares or DEV_SHARE)[stream]
    return "dev" if int(shapes_dev.rank_of(SEED, "partition", family)[:12], 16) / 16 ** 12 < share else "train"


def partition_rows(draft, streams, shares=None):
    """[(source_id, stream, family key, side)] of every draft document; a family is on one side, and counts as stream H when one member is."""
    keys = {row["source_id"]: family_key(row["title"]) for row in draft}
    missing = sorted(set(keys) - set(streams))
    if missing:
        raise ValueError("no stream (C or H) for: " + ", ".join(missing[:10]))
    family_stream = {}
    for source_id, key in keys.items():
        family_stream[key] = "H" if "H" in (family_stream.get(key), streams[source_id]) else "C"
    return [(source_id, streams[source_id], keys[source_id], side_of(keys[source_id], family_stream[keys[source_id]], shares))
            for source_id in sorted(keys)]


def read_streams(work):
    """{source_id: stream} of W/streams.tsv (header `source_id<TAB>stream`, stream C or H)."""
    path = shapes_dev.shared_path(work, STREAMS_FILE)
    if not os.path.exists(path):
        raise ValueError("W/%s is missing (one line per document: source_id, stream C or H)" % STREAMS_FILE)
    _, records = adjudicate.read_records(path)
    streams = {}
    for record in records:
        if record["stream"] not in STREAMS or record["source_id"] in streams:
            raise ValueError("%s: a stream is C or H and a document has one row: %s" % (STREAMS_FILE, record["source_id"]))
        streams[record["source_id"]] = record["stream"]
    return streams


def read_partition(work, draft=None):
    """{source_id: side} of W/partition.tsv; fail closed: every draft document has a side and no family is on both sides."""
    path = shapes_dev.shared_path(work, PARTITION_FILE)
    if not os.path.exists(path):
        raise ValueError("W/%s is missing: run `partition` first" % PARTITION_FILE)
    _, records = adjudicate.read_records(path)
    sides = {r["source_id"]: r["side"] for r in records}
    if any(side not in ("dev", "train") for side in sides.values()):
        raise ValueError("%s: a side is dev or train" % PARTITION_FILE)
    families = collections.defaultdict(set)
    for r in records:
        families[r["family_key"]].add(r["side"])
    split = sorted(key for key, found in families.items() if len(found) > 1)
    if split:
        raise ValueError("a family is on both sides: " + ", ".join(split[:5]))
    missing = sorted({row["source_id"] for row in (draft if draft is not None else shapes_dev.read_draft(work))} - set(sides))
    if missing:
        raise ValueError("no side for: " + ", ".join(missing[:10]) + " (run `partition` again)")
    return sides


def _command_partition(args):
    work = adjudicate.Work(args.work)
    draft = shapes_dev.read_draft(work)
    rows = partition_rows(draft, read_streams(work))
    adjudicate.write_rows(shapes_dev.shared_path(work, PARTITION_FILE), ("source_id", "stream", "family_key", "side"), rows)
    count = collections.Counter((stream, side) for _, stream, _, side in rows)
    families = {(stream, key, side) for _, stream, key, side in rows}
    print("%d documents in %d families; " % (len(rows), len({f[1] for f in families})) + ", ".join(
        "stream %s %s: %d documents" % (stream, side, count[(stream, side)]) for stream in STREAMS for side in ("dev", "train")))
    return 0


# ---------------------------------------------------------------- heading lines: headings, table-of-contents lines and labeled step titles

STEP_LABEL = re.compile(
    r"^(?:(?:Task|TASK|Step|STEP|Procedure|PROCEDURE|Operation|OPERATION|Exercise|EXERCISE|Job|JOB)\s+(?:[0-9]+[A-Za-z]?|[A-Z]|[IVXL]+)(?:[.\-][0-9A-Za-z]+)*[.:\-]?\s+"
    r"|[0-9]{1,2}(?:\.[0-9]{1,3})+\.?\s+|\([a-z0-9]{1,3}\)\s+)(?P<title>\S.*)$")
TOC_LINE = re.compile(
    r"^(?P<title>\w(?:.*?\S)?)(?:\s*(?:\.\s*){3,}|\s+(?:[ceCE]\s?){3,}).*?(?<![0-9A-Za-z])(?P<page>[0-9]{1,3}(?:\s?[-\u2013]\s?[0-9]{1,3})?)\s*$")
TASK_NUMBER = re.compile(r"^[0-9]{3}-[0-9]{3}-[0-9]{4}\s+(?P<title>\S.*)$")           # Army skill-task numbers (`071-311-2027 Load an M16-Series Rifle`)
RAW_LINE_MAX_CHARS = 140


def toc_title(line):
    """The title of a table-of-contents line, else None: the title is the text before the first leader (three or more dots, or the OCR residue `ccceeee`); what follows the leader
    (more dots, OCR residue) is dropped; the line must end with a page number or range. A skill-task number before the title stays and is removed by `labeled_title`."""
    match = TOC_LINE.match(line.strip())
    return re.sub(r"(?:\s+[0O\u00ab\u00bb]{1,4})+$", "", match.group("title")).strip() if match else None


def labeled_title(line):
    """The title of a labeled line (`Task 3.`, `Step 2:`, `2.1`, `(a)`, or an Army skill-task number `071-311-2027` removed), else None."""
    if len(line.strip()) > sx.HEADING_MAX_CHARS:
        return None
    match = STEP_LABEL.match(line.strip()) or TASK_NUMBER.match(line.strip())
    return match.group("title").strip() if match else None


def step_title_lines(raw, tiers=False):
    """[title] (with `tiers`: [(title, tier)]) of the OCR text `raw`: the stand-alone heading lines (tier T1), the table-of-contents lines (T1) and the labeled lines (T2), deduplicated.

    Position and text only (no model, no lexicon). A line that occurs three or more times in the document is a running header or footer and is refused; no paragraph needs to follow a
    table-of-contents line or a labeled line. The unchanged `shapes_dev_extra.heading_noise_reason` filter applies later, in the scan."""
    found = [(heading, "T1") for heading in sx.heading_lines(raw)]
    seen = {heading for heading, _ in found}
    lines = raw.replace("\r\n", "\n").replace("\r", "\n").replace("\x0c", "\n\n").split("\n")
    counts = collections.Counter(sx._line_key(line) for line in lines if line.strip())
    for line in lines:
        stripped = line.strip()
        if not stripped or len(stripped) > RAW_LINE_MAX_CHARS or "\t" in stripped or counts[sx._line_key(stripped)] >= 3:
            continue
        title, tier = toc_title(stripped), "T1"
        if title is None:
            title, tier = labeled_title(stripped), "T2"
        elif labeled_title(title):                      # a table-of-contents line may carry a label too (`Task 3. Title ..... 12`)
            title = labeled_title(title)
        if not title:
            continue
        text = shapes_dev.normalize_text(sx.strip_heading_label(title))
        if not text or text in seen or sx.has_trailing_page_number(text):
            continue
        seen.add(text)
        found.append((text, tier))
    return found if tiers else [text for text, _ in found]


def _command_lines(args):
    work = adjudicate.Work(args.work)
    draft = shapes_dev.read_draft(work)
    folder = shapes_dev.shared_path(work, "lines")
    os.makedirs(folder, exist_ok=True)
    total = collections.Counter()
    for row in draft:
        path = os.path.join(args.work, "raw", row["source_id"] + ".txt")
        if not os.path.exists(path):
            print("no raw text for " + row["source_id"], file=sys.stderr)
            continue
        with open(path, "r", encoding="utf-8", errors="replace", newline="") as handle:
            found = step_title_lines(handle.read(), tiers=True)
        common.write_text(os.path.join(folder, row["source_id"] + ".txt"), [text for text, _ in found])
        total.update(tier for _, tier in found)
    print("%d documents, %d lines written to lines/ (T1 %d, T2 %d)" % (len(draft), sum(total.values()), total["T1"], total["T2"]))
    return 0


def tier_map(work):
    """{sentence key: tier} of the lines of the draft documents, recomputed from W/raw (report only: the tier of a line is `T1` or `T2`)."""
    found = {}
    for row in shapes_dev.read_draft(work):
        path = os.path.join(getattr(work, "base", work.root), "raw", row["source_id"] + ".txt")
        if os.path.exists(path):
            with open(path, "r", encoding="utf-8", errors="replace", newline="") as handle:
                for text, tier in step_title_lines(handle.read(), tiers=True):
                    found.setdefault(shapes_dev.sent_key_of(text), tier)
    return found


def _command_tokenize(args):
    return sx._command_tokenize(args)


# ---------------------------------------------------------------- frames

def fimp_hit(tokens, lex):
    """The index (0) of the imperative of frame F-IMP (strength 0 only; verb-only first words count), or None."""
    for shape, i, strength in sx.heading_hits(tokens, lex, verb_only=True):
        if shape == IMPERATIVE_SHAPE and strength == 0:
            return i
    return None


def initial_noun_hit(tokens, lex):
    """The index (0) of the initial noun of the contrast frame (the old strength 1: token 0 listed noun and verb, token 1 a capitalized noun-capable word), or None."""
    for shape, i, strength in sx.heading_hits(tokens, lex):
        if shape == IMPERATIVE_SHAPE and strength == 1:
            return i
    return None


def first_word_type(token, lex):
    """`verb-only` or `noun-verb` (report only)."""
    return "noun-verb" if "NOUN" in lex.get(token.lower(), ()) else "verb-only"


# ---------------------------------------------------------------- scans

def scan_all(work, data_dir, unit, extra_tables=(), extra_text=(), include_ud=True, filt=None, reference=None, lex=None, vocab=None):
    """The `shapes_dev_extra.Scan` of the clean units of ALL new documents (both sides), with the index and the reference of the follow-up."""
    lex = lex or shapes_dev.lexicon_of(data_dir)
    vocab = vocab or shapes_dev.vocabulary_of(data_dir, lex)
    filt = filt or shapes_dev.LeakageFilter(data_dir, include_ud, new_tranches=True)
    reference = reference or Reference(data_dir, extra_text)
    return sx.scan_units(work, data_dir, unit, used_tables(data_dir, extra_tables), include_ud, filt=filt, reference=reference, vocab=vocab, lex=lex)


def restrict(scan, sides, side):
    """A `Scan` with only the units (and rejections) of the documents on `side`; the drop counts stay those of the whole pool."""
    result = sx.Scan()
    result.units = [unit for unit in scan.units if sides.get(unit[0]) == side]
    result.drops, result.considered = scan.drops, scan.considered
    result.rejected = {k: v for k, v in scan.rejected.items() if sides.get(k) == side}
    result.problems = list(scan.problems)
    return result


def comments_of(sentence):
    return sentence["comments"] if "comments" in sentence else dict(part[2:].split(" = ", 1) for part in sentence["extra"])


def read_corpus_if_present(data_dir, relative):
    path = os.path.join(data_dir, *relative.split("/"))
    return common.read_corpus(path)[1] if os.path.exists(path) else []


def old_target_counts(data_dir):
    """(Counter of head lemmas, number with an -ing form) of the targets of the old compound-head.tsv: the union of the lemma cap and the -ing share."""
    lemmas, ing = collections.Counter(), 0
    for sentence in read_corpus_if_present(data_dir, OLD_COMPOUND_HEAD):
        c = comments_of(sentence)
        if c.get("role") == "target":
            lemmas[c["lemma"]] += 1
            ing += sentence["rows"][int(c["target"])][0].lower().endswith("ing")
    return lemmas, ing


def training_heading_token_sets(data_dir, scan_train=None):
    """The lower-case token sets of every training heading line: the old and new headings sources and the clean heading lines of the TRAIN side of the pool."""
    sets = [frozenset(row[0].lower() for row in s["rows"]) for s in read_corpus_if_present(data_dir, OLD_HEADING_CORPUS)
            + read_corpus_if_present(data_dir, "/".join(("headings", OUTPUT["headings-2-train"][1])))]
    sets += [frozenset(t.lower() for t in unit[3]) for unit in (scan_train.units if scan_train is not None else ())]
    return sets


def jaccard(a, b):
    return len(a & b) / len(a | b) if a | b else 0.0


def near_training_line(tokens, training_sets, limit=JACCARD_DROP):
    """True when the lower-case token set of a development line has Jaccard similarity of at least `limit` with a training heading line."""
    mine = frozenset(t.lower() for t in tokens)
    return any(jaccard(mine, other) >= limit for other in training_sets)


# ---------------------------------------------------------------- candidates

def cap_by_lemma(ranked, cap_of, limit, document_cap=None, counts=None, documents=None):
    """The first `limit` rows of `ranked` ((rank, row) in order) with at most `cap_of(lemma)` per lemma and `document_cap` per document; `counts`/`documents` start the counters."""
    counts, documents, kept = collections.Counter(counts or ()), collections.Counter(documents or ()), []
    for _, row in ranked:
        if len(kept) >= limit:
            break
        if counts[row["lemma"]] >= cap_of(row["lemma"]) or (document_cap is not None and documents[row["source_id"]] >= document_cap):
            continue
        counts[row["lemma"]] += 1
        documents[row["source_id"]] += 1
        kept.append(row)
    return kept


def number_rows(rows, prefix, order_label, seed=SEED, key_of=None):
    """Sort `rows` by a second seeded order and number them `<prefix>cNNNNN` (a row may set its own prefix in `row["prefix"]`)."""
    rows.sort(key=lambda r: shapes_dev.rank_of(seed, order_label, shapes_dev.sent_key_of(r["text"])))
    for number, row in enumerate(rows, 1):
        row["cand_id"] = "%sc%05d" % (row.get("prefix", prefix), number)
    return rows


def heading_pool(scan, product, lex, seed=SEED, exclude_keys=frozenset(), training_sets=()):
    """({stratum: [(rank, row)]}, Counter of drops) of the frame hits of the heading lines of a scan (one candidate per line, the rarest frame takes it).

    Strata: development `proper` (control), `imperative`, `noun`; training `imperative`, `initial-noun`. A development line that is near a training heading line is dropped (`jaccard-train`)."""
    dev = SIDE[product] == "dev"
    pool, drops, taken = collections.defaultdict(list), collections.Counter(), set()
    hits = []
    for source_id, number, text, tokens in scan.units:
        key = shapes_dev.sent_key_of(text)
        if key in exclude_keys:
            drops["pilot-line"] += 1
            continue
        found = {}
        i = fimp_hit(tokens, lex)
        if i is not None:
            found["imperative"] = (IMPERATIVE_SHAPE, i)
        if dev:
            for shape, j, _ in sx.heading_hits(tokens, lex):
                if shape == NOUN_SHAPE:
                    found["noun"] = (shape, j)
                elif shape == PROPER_SHAPE:
                    found["proper"] = (shape, j)
        else:
            j = initial_noun_hit(tokens, lex)
            if j is not None and "imperative" not in found:
                found[INITIAL_NOUN] = (IMPERATIVE_SHAPE, j)
        if found:
            hits.append((source_id, number, text, tokens, key, found))
    for stratum in (("proper", "imperative", "noun") if dev else ("imperative", INITIAL_NOUN)):
        for source_id, number, text, tokens, key, found in hits:
            if stratum not in found or key in taken:
                continue
            if dev and training_sets and near_training_line(tokens, training_sets):
                drops["jaccard-train"] += 1
                taken.add(key)
                continue
            shape, i = found[stratum]
            taken.add(key)
            row = dict(shape=shape, role="control" if stratum == "proper" else "target", target=i, lemma=shapes_dev.lemma_of(tokens[i], lex), origin="real",
                       source_id=source_id, line=number, text=text, tokens=tokens, stratum=stratum,
                       prefix=CAND_PREFIX[INITIAL_NOUN] if stratum == INITIAL_NOUN else CAND_PREFIX[product])
            pool[stratum].append((shapes_dev.rank_of(seed, product, stratum, key), row))
    for stratum in pool:
        pool[stratum].sort(key=lambda item: item[0])
    return pool, drops


def heading_candidates(scan, product, lex, seed=SEED, prior_rows=(), training_sets=()):
    """(rows, report lines, counts) of the heading candidates of a side (see rule 5). `prior_rows` are the pilot rows of the side: not drawn again, counted against the caps."""
    exclude = {shapes_dev.sent_key_of(r["text"]) for r in prior_rows}
    pool, drops = heading_pool(scan, product, lex, seed, exclude, training_sets)
    dev = SIDE[product] == "dev"
    lemma_counts, doc_counts = collections.Counter(r["lemma"] for r in prior_rows), collections.Counter(r["source_id"] for r in prior_rows)
    rows, per_stratum = [], {}
    if dev:
        limits = {"imperative": DEV_HEADING_BUDGET["imperative"] - len(prior_rows), "noun": DEV_HEADING_BUDGET["noun"], "proper": DEV_HEADING_BUDGET["proper"]}
        order = ("imperative", "noun")
        document_cap = DEV_CANDIDATE_DOCUMENT_CAP
    else:
        limits = {"imperative": HDG2_MAX_IMPERATIVE - len(prior_rows), INITIAL_NOUN: HDG2_MAX_INITIAL_NOUN}
        order = ("imperative", INITIAL_NOUN)
        document_cap = HDG2_CANDIDATE_DOCUMENT_CAP
    lemma_cap = DEV_HEADING_LEMMA_CAP if dev else HDG2_CANDIDATE_LEMMA_CAP
    for stratum in order:
        taken = cap_by_lemma(pool.get(stratum, ()), lambda lemma: lemma_cap, max(0, limits[stratum]), document_cap, lemma_counts, doc_counts)
        lemma_counts.update(r["lemma"] for r in taken)
        doc_counts.update(r["source_id"] for r in taken)
        per_stratum[stratum] = len(taken)
        rows += taken
    if dev:
        taken = cap_by_lemma(pool.get("proper", ()), lambda lemma: CONTROL_LEMMA_CAP, limits["proper"], document_cap)
        per_stratum["proper"] = len(taken)
        rows += taken
    number_rows(rows, CAND_PREFIX[product], product + "-order", seed)
    counts = dict(clean=len(scan.units), candidates=len(rows), strata=per_stratum, pools={s: len(v) for s, v in pool.items()},
                  lemmas=len({r["lemma"] for r in rows}), documents=len({r["source_id"] for r in rows}), drops=dict(drops), prior=len(prior_rows))
    report = ["# Candidates (%s)" % product, "", "Counts only; no text of any evaluation set.", ""] + chd.scan_lines(scan, "line")
    report += ["", "frame hits per stratum (before the caps): " + (", ".join("%s %d" % item for item in sorted(counts["pools"].items())) or "none"),
               "dropped at the frame stage: " + (", ".join("%s %d" % item for item in sorted(drops.items())) or "none"),
               "pilot lines already drawn on this side: %d" % len(prior_rows),
               "candidates: %d lines over %d lemmas in %d documents (%s)" % (counts["candidates"], counts["lemmas"], counts["documents"],
                                                                           ", ".join("%s %d" % item for item in sorted(per_stratum.items())))]
    return rows, report, counts


def compound_head_candidates(scan, lex, hmm_table, seed=SEED, old_counts=None, exclude_keys=frozenset(), controls_only=False, room=None):
    """(rows, report lines, counts) of compound-head-2: targets in the contested band, hardest first, with the lemma cap over the union with the old targets, and controls.

    `controls_only` (the second control round): only controls, for the lemmas of `room` ({lemma: controls still taken})."""
    old_counts = collections.Counter(old_counts or ())
    target_pool, stats = [], collections.Counter()
    for source_id, number, text, tokens in scan.units:
        key = shapes_dev.sent_key_of(text)
        if key in exclude_keys or controls_only:
            continue
        i = sx.compound_ambiguous_hit(tokens, lex)
        if i is None:
            continue
        stats["frame"] += 1
        q = hmm_table.q(tokens, i, "Noun")
        if not sx.BAND_MIN <= q < sx.BAND_MAX:
            continue
        stats["band"] += 1
        target_pool.append((sx.rank_key(seed, "compound-head-2", key, q),
                            dict(shape=COMPOUND_SHAPE, role="target", target=i, lemma=shapes_dev.lemma_of(tokens[i], lex), origin="real", source_id=source_id,
                                 line=number, text=text, tokens=tokens, q=shapes_dev.format_q(q))))
    target_pool.sort(key=lambda item: item[0])
    targets = [] if controls_only else cap_by_lemma(target_pool, lambda lemma: max(0, LEMMA_CAP - old_counts[lemma]), COMPOUND_HEAD_BUDGET)
    lemmas = {r["lemma"] for r in targets} if not controls_only else {lemma for lemma, n in (room or {}).items() if n > 0}
    control_pool = []
    for source_id, number, text, tokens in scan.units:
        key = shapes_dev.sent_key_of(text)
        if key in exclude_keys:
            continue
        for i in shapes_dev.control_hits(tokens, lex, COMPOUND_SHAPE, lemmas):
            control_pool.append((shapes_dev.rank_of(seed, "control", "compound-head-2", key),
                                 dict(shape=COMPOUND_SHAPE, role="control", target=i, lemma=shapes_dev.lemma_of(tokens[i], lex), origin="real", source_id=source_id,
                                      line=number, text=text, tokens=tokens, q="")))
    control_pool.sort(key=lambda item: item[0])
    if controls_only:
        controls = cap_by_lemma(control_pool, lambda lemma: (room or {}).get(lemma, 0), CONTROL_POOL_SECOND)
    else:
        controls = cap_by_lemma(control_pool, lambda lemma: CONTROL_LEMMA_CAP, CONTROL_POOL_FIRST)
    rows = targets + controls
    prefix = CAND_PREFIX["compound-head-2-controls"] if controls_only else CAND_PREFIX["compound-head-2"]
    rows.sort(key=lambda r: (r["role"] != "target", shapes_dev.rank_of(seed, "order", "compound-head-2", shapes_dev.sent_key_of(r["text"]))))
    for number, row in enumerate(rows, 1):
        row["cand_id"] = "%sc%05d" % (prefix, number)
    counts = dict(clean=len(scan.units), frame=stats["frame"], band=stats["band"], targets=len(targets), controls=len(controls),
                  lemmas=len({r["lemma"] for r in targets}), documents=len({r["source_id"] for r in rows}))
    report = ["# Candidates (compound-head-2%s)" % (", controls round" if controls_only else ""), "", "Counts only; no text of any evaluation set.", ""] + chd.scan_lines(scan, "sentence")
    report += ["", "frame hits: %d, in the band: %d; target candidates: %d over %d lemmas; control candidates: %d; documents: %d"
               % (counts["frame"], counts["band"], counts["targets"], counts["lemmas"], counts["controls"], counts["documents"])]
    return rows, report, counts


def compound_candidates(scan, lex, seed=SEED, line_keys=frozenset(), lemma_cap=CMP2_CANDIDATE_LEMMA_CAP, maximum=CMP2_MAX_CANDIDATES):
    """(rows, report lines, counts) of compound-2: every frame hit in the seeded order `rank_of(SEED, "compound2", key)`, at most `lemma_cap` per head lemma."""
    pool, drops = [], collections.Counter()
    for source_id, number, text, tokens in scan.units:
        i = sx.compound_ambiguous_hit(tokens, lex)
        if i is None:
            continue
        key = shapes_dev.sent_key_of(text)
        if key in line_keys:
            drops[chd.HEADING_LINE_DROP] += 1
            continue
        pool.append((shapes_dev.rank_of(seed, "compound2", key),
                     dict(shape=COMPOUND_SHAPE, role="target", target=i, lemma=shapes_dev.lemma_of(tokens[i], lex), origin="real", source_id=source_id,
                          line=number, text=text, tokens=tokens)))
    pool.sort(key=lambda item: item[0])
    rows = cap_by_lemma(pool, lambda lemma: lemma_cap, maximum)
    number_rows(rows, CAND_PREFIX["compound-2"], "compound2-order", seed)
    lemmas = collections.Counter(r["lemma"] for r in rows)
    counts = dict(clean=len(scan.units), frame_hits=len(pool), pool_lemmas=len({r["lemma"] for _, r in pool}), candidates=len(rows), lemmas=len(lemmas),
                  documents=len({r["source_id"] for r in rows}), heading_line_drops=drops[chd.HEADING_LINE_DROP])
    report = ["# Candidates (compound-2)", "", "Counts only; no text of any evaluation set.", ""] + chd.scan_lines(scan, "sentence")
    report += ["", "frame hits: %d over %d head lemmas (%d dropped as a heading line of the pool); candidates: %d over %d head lemmas in %d documents"
               % (counts["frame_hits"], counts["pool_lemmas"], counts["heading_line_drops"], counts["candidates"], counts["lemmas"], counts["documents"])]
    return rows, report, counts


def gate_messages(product, counts):
    """[message] for each miss of the size gate of the candidates of a product."""
    found = []
    if product == "compound-2":
        if counts["candidates"] < CMP2_MIN_CANDIDATES:
            found.append("only %d candidates (at least %d needed)" % (counts["candidates"], CMP2_MIN_CANDIDATES))
        if counts["lemmas"] < CMP2_MIN_LEMMAS:
            found.append("only %d head lemmas (at least %d needed)" % (counts["lemmas"], CMP2_MIN_LEMMAS))
    elif product == "compound-head-2":
        if counts["targets"] < COMPOUND_HEAD_MIN_CANDIDATES:
            found.append("only %d target candidates (at least %d needed)" % (counts["targets"], COMPOUND_HEAD_MIN_CANDIDATES))
    elif product == "headings-2":
        total = {s: counts["strata"].get(s, 0) + (counts["prior"] if s == "imperative" else 0) for s in DEV_HEADING_MIN_CANDIDATES}
        found += ["only %d %s candidates (at least %d needed)" % (total[s], s, DEV_HEADING_MIN_CANDIDATES[s]) for s in DEV_HEADING_MIN_CANDIDATES
                  if total[s] < DEV_HEADING_MIN_CANDIDATES[s]]
    else:
        total = counts["strata"].get("imperative", 0) + counts["prior"]
        if total < HDG2_MIN_IMPERATIVE:
            found.append("only %d imperative candidates (at least %d needed)" % (total, HDG2_MIN_IMPERATIVE))
    return found


# ---------------------------------------------------------------- the pilot and its gate

def pilot_candidates(scan, lex, family_of, seed=SEED, documents=None):
    """(rows, report lines, counts): the pilot sample of rule 6 from the clean heading lines of `scan` (both sides; `documents` restricts it to the wave-1 documents)."""
    pool = []
    for source_id, number, text, tokens in scan.units:
        if documents is not None and source_id not in documents:
            continue
        i = fimp_hit(tokens, lex)
        if i is None:
            continue
        key = shapes_dev.sent_key_of(text)
        pool.append((shapes_dev.rank_of(seed, "pilot", key),
                     dict(shape=IMPERATIVE_SHAPE, role="target", target=i, lemma=shapes_dev.lemma_of(tokens[i], lex), origin="real", source_id=source_id,
                          line=number, text=text, tokens=tokens)))
    pool.sort(key=lambda item: item[0])
    lemmas, docs, families, rows = collections.Counter(), collections.Counter(), collections.Counter(), []
    for _, row in pool:
        if len(rows) >= PILOT_SIZE:
            break
        family = family_of.get(row["source_id"], "")
        if lemmas[row["lemma"]] >= PILOT_LEMMA_CAP or docs[row["source_id"]] >= PILOT_DOCUMENT_CAP or families[family] >= PILOT_FAMILY_CAP:
            continue
        lemmas[row["lemma"]] += 1
        docs[row["source_id"]] += 1
        families[family] += 1
        rows.append(row)
    rows.sort(key=lambda r: shapes_dev.rank_of(seed, "pilot-order", shapes_dev.sent_key_of(r["text"])))
    for number, row in enumerate(rows, 1):
        row["cand_id"] = "%sc%05d" % (CAND_PREFIX["pilot"], number)
    counts = dict(population=len(pool), sampled=len(rows), lemmas=len(lemmas), documents=len(docs), families=len(families))
    report = ["# Pilot candidates", "", "Counts only; no text of any evaluation set.", "",
              "population (imperative frame, strength 0): %d lines; sampled: %d lines over %d lemmas, %d documents, %d families" %
              (counts["population"], counts["sampled"], counts["lemmas"], counts["documents"], counts["families"])]
    return rows, report, counts


def pilot_verdict(confirmed, sampled):
    """(verdict, message): PROCEED when at least ceil(sampled / 2) of the sampled lines are confirmed, STOP when fewer, SCARCITY when fewer than PILOT_MIN_SIZE lines were sampled."""
    if sampled < PILOT_MIN_SIZE:
        return "SCARCITY", "only %d of at least %d pilot lines exist: the pilot cannot run (INCOMPLETE)" % (sampled, PILOT_MIN_SIZE)
    minimum = pilot_confirmed_min(sampled)
    if confirmed < minimum:
        return "STOP", "%d of %d confirmed, fewer than %d: the implementation stops before scaling (INCOMPLETE)" % (confirmed, sampled, minimum)
    return "PROCEED", "%d of %d confirmed (at least %d): proceed" % (confirmed, sampled, minimum)


def pilot_numbers(items, candidates, family_of, tiers, lex):
    """(confirmed, sampled, report lines) of the decided pilot items: a line is CONFIRMED when both labelers tag token 0 VERB. Counts only (ids and counts, no text)."""
    confirmed = sum(1 for i in items if i["opus_tag"] == "VERB" and i["sol_tag"] == "VERB")
    by_family, by_tier, by_type, others = (collections.defaultdict(lambda: [0, 0]) for _ in range(4))
    for item in items:
        candidate = candidates[item["cand_id"]]
        yes = item["opus_tag"] == "VERB" and item["sol_tag"] == "VERB"
        for table, label in ((by_family, family_of.get(item["source_id"], "?")), (by_tier, tiers.get(item["sent_key"], "?")),
                             (by_type, first_word_type(candidate["tokens"][0], lex))):
            table[label][0] += 1
            table[label][1] += yes
        if not yes:
            others[item["opus_tag"] if item["opus_tag"] == item["sol_tag"] else "disagreement"][0] += 1
    lines = ["# Pilot record (counts only)", "", "sampled %d, confirmed %d (both labelers tag token 0 VERB)" % (len(items), confirmed), ""]
    for title, table in (("family", by_family), ("tier", by_tier), ("first-word type", by_type)):
        lines += ["| %s | sampled | confirmed |" % title, "| --- | --- | --- |"] + ["| %s | %d | %d |" % (k, v[0], v[1]) for k, v in sorted(table.items())] + [""]
    lines += ["| token 0 when not confirmed | lines |", "| --- | --- |"] + ["| %s | %d |" % (k, v[0]) for k, v in sorted(others.items())]
    return confirmed, len(items), lines


def pilot_items(work, data_dir=None):
    """([item], {cand_id: candidate}) of the decided pilot round."""
    round_work = shapes_dev.RoundWork(getattr(work, "base", work.root), PILOT_ROUND)
    items, _ = shapes_dev.read_decided(round_work, data_dir)
    return items, {c["cand_id"]: c for c in shapes_dev.read_candidates(round_work)}


def pilot_rows_of_side(work, sides, side):
    """[candidate row] of the pilot round whose document is on `side` (empty when the round has no candidates)."""
    round_work = shapes_dev.RoundWork(getattr(work, "base", work.root), PILOT_ROUND)
    if not os.path.exists(shapes_dev.work_path(round_work, "candidates.tsv")):
        return []
    return [c for c in shapes_dev.read_candidates(round_work) if sides.get(c["source_id"]) == side]


def _round_work(args, name):
    round_work = shapes_dev.RoundWork(args.work, name)
    os.makedirs(round_work.private, exist_ok=True)
    return round_work


def _family_of(draft):
    return {row["source_id"]: row["family"] for row in draft}


def _title_family_of(draft):
    """{source_id: title family key}: the family of the partition (amendment A1: the pilot family cap is read at this level, not at agency level)."""
    return {row["source_id"]: family_key(row["title"]) for row in draft}


def _command_pilot(args):
    work = adjudicate.Work(args.work)
    require_pins(work)
    draft = shapes_dev.read_draft(work)
    draft_gate(work, args.data_dir, args.extra_table or ())
    lex = shapes_dev.lexicon_of(args.data_dir)
    scan = scan_all(work, args.data_dir, "heading", args.extra_table or (), args.extra_text or (), not args.no_ud, lex=lex)
    documents = None
    if args.documents:
        documents = {line.strip() for line in common.read_lines(args.documents) if line.strip() and not line.startswith("#")}
        unknown = documents - {row["source_id"] for row in draft}
        if unknown:
            raise ValueError("--documents names documents that are not in the draft: " + ", ".join(sorted(unknown)[:5]))
    rows, report, counts = pilot_candidates(scan, lex, _title_family_of(draft), args.seed, documents)
    round_work = _round_work(args, PILOT_ROUND)
    common.write_text(round_work.private_file("candidates-report.md"), report)
    shapes_dev.write_candidates(round_work, rows)
    print("\n".join(report))
    print("%d candidates written to rounds/%s/candidates.tsv" % (len(rows), PILOT_ROUND))
    if counts["sampled"] < PILOT_MIN_SIZE:
        print("PILOT SCARCITY: " + pilot_verdict(0, counts["sampled"])[1], file=sys.stderr)
        return 1
    return 0


def _command_pilot_result(args):
    work = adjudicate.Work(args.work)
    draft = shapes_dev.read_draft(work)
    items, candidates = pilot_items(work, args.data_dir)
    lex = shapes_dev.lexicon_of(args.data_dir)
    confirmed, sampled, lines = pilot_numbers(items, candidates, _family_of(draft), tier_map(work), lex)
    verdict, message = pilot_verdict(confirmed, sampled)
    lines += ["", "gate: " + verdict + " (" + message + ")"]
    adjudicate._print(lines)
    common.write_text(_round_work(args, PILOT_ROUND).private_file("pilot-record.md"), lines)
    return 0 if verdict == "PROCEED" else 1


# ---------------------------------------------------------------- the rounds of a product, kinds, selection

def read_product_rounds(work, product, data_dir=None, sides=None, keep=()):
    """(items, tagged rows, candidates) of the decided rounds of a product, restricted to the documents on the side of the product."""
    base = getattr(work, "base", work.root)
    sides = sides if sides is not None else read_partition(work)
    items, rows, candidates = [], {}, {}
    for name in ROUNDS[product]:
        round_work = shapes_dev.RoundWork(base, name)
        if not os.path.exists(round_work.private_file("decided.tsv")):
            continue
        part_items, part_rows = shapes_dev.read_decided(round_work, data_dir, keep=keep)
        items += [i for i in part_items if sides.get(i["source_id"]) == SIDE[product]]
        for key, tagged in part_rows.items():
            rows.setdefault(key, tagged)
        candidates.update({c["cand_id"]: c for c in shapes_dev.read_candidates(round_work)})
    return items, rows, candidates


def heading_stratum_train(item, rows):
    """(stratum, reason) of a decided headings-2-train item: `imperative` (agreed VERB), `initial-noun` (agreed NOUN, the contrast stratum) or (None, the reason)."""
    if item["outcome"] == "keep":
        if item["disputed_tokens"] or any(common.base_status(row[2]) == "disputed" for row in rows):
            return None, "disputed-token"
        return ("imperative", None) if rows[item["target"]][1] == "VERB" else (None, "class-mismatch")
    if item["cand_id"].startswith(CAND_PREFIX[INITIAL_NOUN]):
        if item["reason"] == "class-mismatch" and item["agreed_tag"] == "NOUN":
            if item["disputed_tokens"] or any(common.base_status(row[2]) == "disputed" for row in rows):
                return None, "disputed-token"
            return INITIAL_NOUN, None
    return None, item["reason"] or "dropped"


def select_headings_train(kept, seed=SEED, lemma_cap=HDG2_LEMMA_CAP, document_cap=HDG2_DOCUMENT_CAP, imperative_cap=HDG2_IMPERATIVE_CAP):
    """(chosen, drops): imperative lines first (seeded order, `imperative_cap`), then initial-noun lines up to half of the kept imperative lines; HDG2_LEMMA_CAP (6 after amendment A1) per lemma and 25 per document over both."""
    drops, per_lemma, per_document, chosen = collections.Counter(), collections.Counter(), collections.Counter(), []
    counts = collections.Counter()
    for stratum in ("imperative", INITIAL_NOUN):
        limit = (imperative_cap if stratum == "imperative"
                 else counts["imperative"] * HDG2_INITIAL_NOUN_SHARE_NUM // HDG2_INITIAL_NOUN_SHARE_DEN)
        for item in sorted((k for k in kept if k["kind"] == stratum), key=lambda c: shapes_dev.rank_of(seed, "headings2-build", c["sent_key"])):
            if counts[stratum] >= limit:
                drops[stratum + "-cap"] += 1
            elif per_lemma[item["lemma"]] >= lemma_cap:
                drops["lemma-cap"] += 1
            elif per_document[item["source_id"]] >= document_cap:
                drops["document-cap"] += 1
            else:
                per_lemma[item["lemma"]] += 1
                per_document[item["source_id"]] += 1
                counts[stratum] += 1
                chosen.append(item)
    return chosen, drops


def select_compound_train(kept, seed=SEED):
    """(chosen, drops) of compound-2: the rule of compound_heading_data.select_compound with the caps of this follow-up."""
    return chd.select_compound(kept, seed, CMP2_LEMMA_CAP, CMP2_DOCUMENT_CAP, CMP2_MAX, CMP2_NOUN_CAP, CMP2_VERB_CAP, CMP2_VERB_PERCENT)


def keep_item(item):
    return item["outcome"] == "keep"


def select_compound_head(items, candidates, old_counts, old_ing, seed=SEED):
    """(targets, controls, drops) of compound-head-2: targets by q ascending with 4 per lemma over the union with the old targets and `-ing` heads at most 25 percent of the union
    (OLD_TARGETS + the cap); controls for the lemmas of the chosen targets, 2 per lemma, seeded order."""
    quota = DEV_QUOTAS["compound-head-2"]
    limit = quota["targets"][1]
    ing_limit = int(math.ceil((OLD_TARGETS + limit) * ING_SHARE)) - old_ing
    drops, counts, ing, targets = collections.Counter(), collections.Counter(old_counts), 0, []
    members = sorted((i for i in items if keep_item(i) and i["role"] == "target"),
                     key=lambda i: (float(candidates[i["cand_id"]]["q"]), shapes_dev.rank_of(seed, "build", i["cand_id"])))
    for item in members:
        form = candidates[item["cand_id"]]["tokens"][item["target"]].lower()
        if len(targets) >= limit:
            drops["cap"] += 1
        elif counts[item["lemma"]] >= LEMMA_CAP:
            drops["lemma-cap"] += 1
        elif form.endswith("ing") and ing >= ing_limit:
            drops["ing-share"] += 1
        else:
            counts[item["lemma"]] += 1
            ing += form.endswith("ing")
            targets.append(item)
    lemmas = {i["lemma"] for i in targets}
    per_lemma, controls = collections.Counter(), []
    for item in sorted((i for i in items if keep_item(i) and i["role"] == "control" and i["lemma"] in lemmas),
                       key=lambda i: shapes_dev.rank_of(seed, "build-control", i["cand_id"])):
        if len(controls) >= quota["controls"][1]:
            drops["cap"] += 1
        elif per_lemma[item["lemma"]] >= CONTROL_LEMMA_CAP:
            drops["lemma-cap"] += 1
        else:
            per_lemma[item["lemma"]] += 1
            controls.append(item)
    return targets, controls, drops


def control_room(items, candidates, old_counts, old_ing, seed=SEED):
    """{lemma: controls that the build still takes} of the lemmas of the chosen compound-head-2 targets (the input of the second control round)."""
    targets, _, _ = select_compound_head(items, candidates, old_counts, old_ing, seed)
    have = collections.Counter(i["lemma"] for i in items if keep_item(i) and i["role"] == "control")
    return {lemma: CONTROL_LEMMA_CAP - have[lemma] for lemma in {t["lemma"] for t in targets} if have[lemma] < CONTROL_LEMMA_CAP}


def select_headings_dev(items, candidates, seed=SEED):
    """(targets, controls, drops) of headings-2: imperative then noun targets in the seeded order, 4 per lemma and 12 per document over both, the caps of each kind; proper controls 2 per lemma."""
    quota = DEV_QUOTAS["headings-2"]
    drops = collections.Counter()
    kept = [i for i in items if keep_item(i)]
    numbered = [i for i in kept if sx.has_trailing_page_number(candidates[i["cand_id"]]["text"])]
    drops["page-number"] += len(numbered)
    kept = [i for i in kept if i not in numbered]
    per_lemma, per_document, targets = collections.Counter(), collections.Counter(), []
    for shape in (IMPERATIVE_SHAPE, NOUN_SHAPE):
        limit, taken = quota[HEADING_KIND[shape]][1], 0
        for item in sorted((i for i in kept if i["shape"] == shape and i["role"] == "target"), key=lambda i: shapes_dev.rank_of(seed, "build", i["cand_id"])):
            if taken >= limit:
                drops["cap"] += 1
            elif per_lemma[item["lemma"]] >= DEV_HEADING_LEMMA_CAP:
                drops["lemma-cap"] += 1
            elif per_document[item["source_id"]] >= DEV_BUILD_DOCUMENT_CAP:
                drops["document-cap"] += 1
            else:
                per_lemma[item["lemma"]] += 1
                per_document[item["source_id"]] += 1
                targets.append(item)
                taken += 1
    per_lemma, controls = collections.Counter(), []
    for item in sorted((i for i in kept if i["shape"] == PROPER_SHAPE), key=lambda i: shapes_dev.rank_of(seed, "build-control", i["cand_id"])):
        if len(controls) >= quota["controls"][1]:
            drops["cap"] += 1
        elif per_lemma[item["lemma"]] >= CONTROL_LEMMA_CAP:
            drops["lemma-cap"] += 1
        else:
            per_lemma[item["lemma"]] += 1
            controls.append(item)
    return targets, controls, drops


# ---------------------------------------------------------------- sentences, quota gates, headers

def header_lines(product):
    folder, corpus, sources = OUTPUT[product]
    if SIDE[product] == "dev":
        selection = {"compound-head-2": "the contested band of the WordSense Basic classifier, hardest first",
                     "headings-2": "Title Case heading, table-of-contents and step-title lines of federal documents, by position and lexicon only"}[product]
        shape = {"compound-head-2": COMPOUND_SHAPE, "headings-2": "heading-titlecase"}[product]
        return ["# corpus = DemaConsulting.PartOfSpeech shapes development set, %s tranche (UPOS), dev split" % product,
                "# license = CC0-1.0 for annotations and selection; sentences are US federal government works, US public domain, see %s" % sources,
                "# columns = form, upos, status",
                "# status = unanimous | disputed (two blind model labelers; disputed = they differ, the tag is the first labeler's)",
                "# judged word = per sentence: shape (%s), role (target | control), target (zero-based token index), lemma, origin (real)%s; the judged word is never disputed"
                % (shape, ", kind (noun | imperative | proper)" if product == "headings-2" else ""),
                "# selection = %s; the gold is the agreement of two blind labelers (see README.md)" % selection,
                "# use = development only: select settings here, never train on it, never report it as a held-out figure", "# file = %s" % corpus]
    if product == "compound-2":
        return ["# corpus = DemaConsulting.PartOfSpeech compound-head training source 2 (UPOS)",
                "# license = CC0-1.0 for annotations and selection; sentences are US federal government works, see compound-2-sources.tsv",
                "# columns = form, upos, status",
                "# status = unanimous (two blind model labelers agree on the tag of every token of the sentence)",
                "# judged word = per sentence: head (zero-based index of the head of a noun compound whose head is also a verb), lemma, kind (noun | verb: the head is a noun or a verb here)",
                "# selection = sentences with a noun compound whose head can also be a verb, from new US federal text; see README.md and tools/data/followup_data.py",
                "# use = training"]
    return ["# corpus = DemaConsulting.PartOfSpeech Title Case heading training source 2 (UPOS)",
            "# license = CC0-1.0 for annotations and selection; lines are US federal government works, see headings-2-sources.tsv",
            "# columns = form, upos, status",
            "# status = unanimous (two blind model labelers agree on the tag of every token of the line)",
            "# judged word = per line: word (zero-based index of the stratum word), lemma, stratum (imperative | initial-noun)",
            "# selection = Title Case step-title and table-of-contents lines with an initial imperative, and the contrast stratum of initial nouns, from new US federal text; "
            "see README.md and tools/data/followup_data.py",
            "# use = training"]


def final_sentence(product, entry, sentence, number):
    """The built sentence of a kept entry: the agreed token rows, the id and the per-sentence lines (entry: target, lemma, kind, role, shape)."""
    if product == "compound-2":
        extra = ["# head = %d" % entry["target"], "# lemma = " + entry["lemma"], "# kind = " + entry["kind"]]
    elif product == "headings-2-train":
        extra = ["# word = %d" % entry["target"], "# lemma = " + entry["lemma"], "# stratum = " + entry["kind"]]
    else:
        extra = ["# shape = " + (COMPOUND_SHAPE if product == "compound-head-2" else "heading-titlecase"), "# role = " + entry["role"],
                 "# target = %d" % entry["target"], "# lemma = " + entry["lemma"], "# origin = real"]
        if product == "headings-2":
            extra.append("# kind = " + entry["kind"])
    return dict(sentence, sent_id=SENT_ID[product] % number, extra=extra)


def judged(product, sentence):
    """(index, kind, lemma) of the judged word of a built sentence (kind: noun | verb, imperative | initial-noun, target | control, imperative | noun | proper)."""
    c = comments_of(sentence)
    if product == "compound-2":
        return int(c["head"]), c["kind"], c["lemma"]
    if product == "headings-2-train":
        return int(c["word"]), c["stratum"], c["lemma"]
    return int(c["target"]), (c["kind"] if product == "headings-2" else c["role"]), c["lemma"]


def sentence_problems(product, sentence):
    """[message] for one built sentence (the per-sentence rules of the quota gate)."""
    rows, name = sentence["rows"], sentence["sent_id"]
    c = comments_of(sentence)
    index_name = {"compound-2": "head", "headings-2-train": "word"}.get(product, "target")
    if not c.get(index_name, "").isdigit() or int(c[index_name]) >= len(rows):
        return ["%s: `%s` is not a token index" % (name, index_name)]
    i, kind, _ = judged(product, sentence)
    tag = rows[i][1]
    expected = {"compound-2": {"noun": ("NOUN", "PROPN"), "verb": ("VERB",)}, "headings-2-train": {"imperative": ("VERB",), INITIAL_NOUN: ("NOUN",)},
                "headings-2": {"imperative": ("VERB",), "noun": ("NOUN",), "proper": ("PROPN",)}}.get(product)
    problems = []
    if expected is not None and (kind not in expected or tag not in expected[kind]):
        problems.append("%s: the judged word is tagged %s for kind %s" % (name, tag, kind))
    if product == "compound-head-2" and shapes_dev.tag_class(tag) != ("Noun" if c.get("role") == "target" else "Verb"):
        problems.append("%s: the judged word is tagged %s for a %s" % (name, tag, c.get("role")))
    if product in ("compound-head-2", "headings-2") and (c.get("shape") is None or c.get("role") not in ("target", "control") or c.get("origin") != "real"):
        problems.append("%s: shape, role or origin is wrong" % name)
    if product == "headings-2" and (c.get("role") == "control") != (kind == "proper"):
        problems.append("%s: a proper line is the control and only it" % name)
    if SIDE[product] == "train" and any(common.base_status(row[2]) != "unanimous" for row in rows):
        problems.append("%s: a token is not unanimous" % name)
    if SIDE[product] == "dev" and common.base_status(rows[i][2]) == "disputed":
        problems.append("%s: the judged word is disputed" % name)
    if "".join(row[0] for row in rows) != sentence["text"].replace(" ", ""):
        problems.append("%s: the tokens do not rejoin to the text" % name)
    if product in ("headings-2-train", "headings-2") and kind == "imperative" and i != 0:
        problems.append("%s: an imperative line is judged at token 0" % name)
    return problems


def quota_problems(product, sentences, minimum=None, floors=None, old_counts=None):
    """[message] for every miss of the quota gate (floors and the minimum are waivable by `--allow-short`, caps and the per-sentence rules never).
    `old_counts` are the head lemmas of the old compound-head targets (the union of the lemma cap of compound-head-2)."""
    found, comments = [], [comments_of(s) for s in sentences]
    kinds = collections.Counter(judged(product, s)[1] for s in sentences)
    lemmas = collections.Counter(c["lemma"] for c in comments)
    documents = collections.Counter(s["source_id"] for s in sentences)
    if product == "compound-2":
        minimum = CMP2_MIN if minimum is None else minimum
        if len(sentences) < minimum:
            found.append("%d sentences, at least %d needed" % (len(sentences), minimum))
        if len(sentences) > CMP2_MAX:
            found.append("%d sentences, at most %d allowed" % (len(sentences), CMP2_MAX))
        if minimum and len(lemmas) < CMP2_MIN_BUILD_LEMMAS:
            found.append("%d head lemmas, at least %d needed" % (len(lemmas), CMP2_MIN_BUILD_LEMMAS))
        if kinds["verb"] * 100 > CMP2_VERB_PERCENT * len(sentences):
            found.append("%d verb items of %d, at most %d percent allowed" % (kinds["verb"], len(sentences), CMP2_VERB_PERCENT))
        lemma_cap, document_cap = CMP2_LEMMA_CAP, CMP2_DOCUMENT_CAP
    elif product == "headings-2-train":
        floors = {"imperative": HDG2_IMPERATIVE_FLOOR} if floors is None else floors
        if kinds["imperative"] < floors.get("imperative", 0):
            found.append("imperative: %d lines, the floor is %d" % (kinds["imperative"], floors["imperative"]))
        if kinds["imperative"] > HDG2_IMPERATIVE_CAP:
            found.append("imperative: %d lines, the cap is %d" % (kinds["imperative"], HDG2_IMPERATIVE_CAP))
        if kinds[INITIAL_NOUN] * HDG2_INITIAL_NOUN_SHARE_DEN > kinds["imperative"] * HDG2_INITIAL_NOUN_SHARE_NUM:
            found.append("initial-noun: %d lines, at most %d/%d of the %d imperative lines" % (kinds[INITIAL_NOUN], HDG2_INITIAL_NOUN_SHARE_NUM,
                                                                                               HDG2_INITIAL_NOUN_SHARE_DEN, kinds["imperative"]))
        lemma_cap, document_cap = HDG2_LEMMA_CAP, HDG2_DOCUMENT_CAP
    else:
        quota = DEV_QUOTAS[product]
        waive = floors == {}
        names = ((("targets", "target"), ("controls", "control")) if product == "compound-head-2"
                 else (("imperative", "imperative"), ("noun", "noun"), ("controls", "proper")))
        for name, kind in names:
            floor, cap = quota[name]
            if kinds[kind] < floor and not waive:
                found.append("%s: %d, the floor is %d" % (name, kinds[kind], floor))
            if kinds[kind] > cap:
                found.append("%s: %d, the cap is %d" % (name, kinds[kind], cap))
        is_target = [comments_of(s).get("role") == "target" for s in sentences]
        target_lemmas = collections.Counter(c["lemma"] for c, t in zip(comments, is_target) if t)
        control_lemmas = collections.Counter(c["lemma"] for c, t in zip(comments, is_target) if not t)
        union = collections.Counter(old_counts or ())
        union.update(target_lemmas)
        target_cap = DEV_HEADING_LEMMA_CAP if product == "headings-2" else LEMMA_CAP
        found += ["target lemma %s has %d items, at most %d allowed" % (lemma, n, target_cap) for lemma, n in sorted(union.items())
                  if n > target_cap and (lemma in target_lemmas)]
        found += ["control lemma %s has %d items, at most %d allowed" % (lemma, n, CONTROL_LEMMA_CAP) for lemma, n in sorted(control_lemmas.items())
                  if n > CONTROL_LEMMA_CAP]
        if product == "headings-2":
            per_document = collections.Counter(s["source_id"] for s, t in zip(sentences, is_target) if t)
            found += ["document %s has %d targets, at most %d allowed" % (doc, n, DEV_BUILD_DOCUMENT_CAP) for doc, n in sorted(per_document.items())
                      if n > DEV_BUILD_DOCUMENT_CAP]
        lemma_cap = document_cap = None
    if lemma_cap:
        found += ["lemma %s has %d items, at most %d allowed" % (lemma, n, lemma_cap) for lemma, n in sorted(lemmas.items()) if n > lemma_cap]
    if document_cap:
        found += ["document %s has %d items, at most %d allowed" % (doc, n, document_cap) for doc, n in sorted(documents.items()) if n > document_cap]
    for s in sentences:
        found += sentence_problems(product, s)
    return found


def cross_side_problems(data_dir, product, built):
    """[message] for a document or URL of the built product that is also in the source table of a product of the other side (DEV and TRAIN stay disjoint)."""
    own_ids = {s["source_id"] for s in built}
    found = []
    for other in PRODUCTS:
        if SIDE[other] == SIDE[product]:
            continue
        path = os.path.join(data_dir, *OUTPUT[other][0].split("/"), OUTPUT[other][2])
        if os.path.exists(path):
            _, records = adjudicate.read_records(path)
            shared = sorted(own_ids & {r["source_id"] for r in records})
            found += ["document %s is in both %s and %s" % (d, product, other) for d in shared]
    return found


def _drop_rows(round_work):
    return chd._drop_rows(round_work)


def build(work, product, out_dir, seed=SEED, allow_short=False, data_dir=None, extra_tables=(), omit=()):
    """Write the corpus and the sources table (and, for a training product, excluded-documents and dropped-sentences) of `product` into `out_dir`; returns (sentences, report lines).

    Fail closed: the draft must pass the source and license gate (DraftError), a missed quota (`BuildError`, nothing written; `allow_short` lowers only a floor or the minimum), an excluded
    or unknown document, a document that is in a product of the other side. `omit` lists documents removed after the caps (rule 10)."""
    data_dir = data_dir or common.DATA
    omit_reasons = chd.parse_omissions(omit)
    omit = sorted(omit_reasons)
    draft = shapes_dev.read_draft(work)
    missing = sorted(set(omit) - {row["source_id"] for row in draft})
    if missing:
        raise BuildError("cannot omit a document that is not in the draft: " + ", ".join(missing))
    draft_gate(work, data_dir, extra_tables, omit)
    sides = read_partition(work, draft)
    items, tagged, candidates = read_product_rounds(work, product, data_dir, sides, keep=omit)
    exclusions = shapes_dev.load_exclusions(work, data_dir)
    drops, chosen_items, sentences = collections.Counter(), [], {}
    train = SIDE[product] == "train"
    if train:
        kept = []
        for item in items:
            rows = tagged.get(item["sent_key"])
            if rows is None:
                drops["undecided"] += 1
                continue
            kind, reason = (chd.compound_kind if product == "compound-2" else heading_stratum_train)(item, rows)
            if reason:
                drops[reason] += 1
                continue
            sentences[item["sent_key"]] = dict(sent_id="", source_id=item["source_id"], text=candidates[item["cand_id"]]["text"], rows=[list(r) for r in rows])
            kept.append(dict(sent_key=item["sent_key"], lemma=item["lemma"], source_id=item["source_id"], kind=kind, target=item["target"], role="target"))
        labeled = sum(drops.values()) + len(kept)
        temporary = {"tmp-%d" % n: entry for n, entry in enumerate(kept, 1)}
        fixed = chd.fix_tier_rules([dict(sentences[e["sent_key"]], sent_id=name) for name, e in temporary.items()], data_dir,
                                   "compound" if product == "compound-2" else "headings")
        after_lint = []
        for name, entry in temporary.items():
            if name in fixed:
                drops["fix-tier"] += 1
            else:
                after_lint.append(entry)
        chosen, cap_drops = (select_compound_train if product == "compound-2" else select_headings_train)(after_lint, seed)
        drops.update(cap_drops)
        omitted = [entry for entry in chosen if entry["source_id"] in omit]
        if omitted:
            drops[chd.OMITTED] += len(omitted)
            chosen = [entry for entry in chosen if entry["source_id"] not in omit]
            if product == "compound-2":
                chosen, surplus = chd.trim_verb_share(chosen)
                if surplus:
                    drops["verb-share"] += surplus
        ordered = sorted(chosen, key=lambda c: shapes_dev.rank_of(seed, product + "-order", c["sent_key"]))
        built = [final_sentence(product, entry, sentences[entry["sent_key"]], number) for number, entry in enumerate(ordered, 1)]
    else:
        labeled = len(items)
        for item in items:
            if item["source_id"] in omit:
                drops[chd.OMITTED] += 1
        items = [i for i in items if i["source_id"] not in omit]
        if product == "compound-head-2":
            old_counts, old_ing = old_target_counts(data_dir)
            targets, controls, cap_drops = select_compound_head(items, candidates, old_counts, old_ing, seed)
        else:
            targets, controls, cap_drops = select_headings_dev(items, candidates, seed)
        drops.update(cap_drops)
        shape_order = (COMPOUND_SHAPE,) if product == "compound-head-2" else (IMPERATIVE_SHAPE, NOUN_SHAPE, PROPER_SHAPE)
        ordered = sorted(targets + controls, key=lambda i: (shape_order.index(i["shape"]), i["role"] != "target", shapes_dev.rank_of(seed, "order", i["cand_id"])))
        built = [final_sentence(product, dict(i, kind=HEADING_KIND.get(i["shape"], "")), dict(sent_id="", source_id=i["source_id"], text=candidates[i["cand_id"]]["text"],
                                                                                             rows=[list(r) for r in tagged[i["sent_key"]]]), number)
                 for number, i in enumerate(ordered, 1)]
    unknown = sorted({s["source_id"] for s in built} - {row["source_id"] for row in draft})
    if unknown:
        raise BuildError("no source row for: " + ", ".join(unknown))
    refused = exclusions.problems(draft, built)
    if refused:
        raise shapes_dev.ExcludedSourceError("excluded sources are used:\n  " + "\n  ".join(refused[:20]))
    wrong_side = sorted({s["source_id"] for s in built if sides.get(s["source_id"]) != SIDE[product]})
    cross = cross_side_problems(data_dir, product, built) + ["document %s is not on the %s side" % (d, SIDE[product]) for d in wrong_side]
    if cross:
        raise BuildError("DEV and TRAIN documents must be disjoint:\n  " + "\n  ".join(cross[:20]))
    old_counts = old_target_counts(data_dir)[0] if product == "compound-head-2" else None
    problems = quota_problems(product, built, 0 if allow_short and product == "compound-2" else None, {} if allow_short else None, old_counts)
    if problems:
        raise BuildError("the quota gate is not met (--allow-short lowers only a floor or the minimum):\n  " + "\n  ".join(problems[:20]))
    folder, corpus, sources = OUTPUT[product]
    common.write_corpus(os.path.join(out_dir, corpus), common.with_flag_documentation(header_lines(product), built), built)
    adjudicate.write_rows(os.path.join(out_dir, sources), shapes_dev.SOURCE_COLUMNS, shapes_dev.sources_rows(draft, built))
    if train:
        round_work = shapes_dev.RoundWork(getattr(work, "base", work.root), CANDIDATE_ROUND[product])
        rejected_path = round_work.private_file("rejected-documents.tsv")
        rejected = sorted(adjudicate.read_records(rejected_path)[1], key=lambda r: r["source_id"]) if os.path.exists(rejected_path) else []
        common.write_text(os.path.join(out_dir, EXCLUDED_OUT[product]), [
            "# Documents left out of %s because the document is a reprint of, or shares sentences with, a training or evaluation set, was already used, or has no verifiable federal-only basis" % corpus,
            "# (only ids and reasons, never text)", "# columns = document_id, reason"] + ["%s\t%s" % (r["source_id"], r["reason"]) for r in rejected]
            + ["%s\t%s" % (document, omit_reasons[document]) for document in omit])
        common.write_text(os.path.join(out_dir, DROPPED_OUT[product]), [
            "# Candidate sentences dropped alone because they equal or nearly equal a sentence of a training or evaluation set or of the shapes development set",
            "# (counted in private/candidates-report.md of the round folder; only counts are published, never text)", "# columns = reason, sentences"]
            + _drop_rows(round_work))
    return built, build_report(product, built, labeled, drops, draft)


def build_report(product, built, labeled, drops, draft):
    """Markdown counts of a build: drops per reason, per family, per kind, lemma and document statistics."""
    family = {row["source_id"]: row["family"] for row in draft}
    kinds = collections.Counter(judged(product, s)[1] for s in built)
    lemmas = collections.Counter(judged(product, s)[2] for s in built)
    documents = collections.Counter(s["source_id"] for s in built)
    lines = ["# Build (%s)" % product, "", "%d sentences written to %s (labeled items with a decision: %d)" % (len(built), OUTPUT[product][1], labeled), "",
             "| dropped because | items |", "| --- | --- |"] + ["| %s | %d |" % item for item in sorted(drops.items())]
    families = collections.Counter(family[s["source_id"]] for s in built)
    lines += ["", "| family | documents | sentences |", "| --- | --- | --- |"]
    lines += ["| %s | %d | %d |" % (name, sum(1 for d in documents if family[d] == name), families[name]) for name in sorted(families)]
    lines += ["", "- kinds: " + ", ".join("%s %d" % item for item in sorted(kinds.items())),
              "- lemmas: %d (most frequent: %s)" % (len(lemmas), ", ".join("%s %d" % item for item in sorted(lemmas.items(), key=lambda i: (-i[1], i[0]))[:8])),
              "- documents: %d" % len(documents), "- tokens: %d" % sum(len(s["rows"]) for s in built)]
    return lines


# ---------------------------------------------------------------- hand check

def swapped_tag(tag):
    """The tag shown for a leniency control: the other class of the judged word's tag."""
    return "NOUN" if tag == "VERB" else "VERB"


def build_sample(product, sentences, seed=None, size=SAMPLE_SIZE, controls=SAMPLE_CONTROLS):
    """[(kind, sentence, shown tag)] shuffled: the items of SAMPLE_PLAN (fewer when a stratum is short) with the agreed tag, and `controls` other items with the other class shown."""
    rng = random.Random(SAMPLE_SEEDS[product] if seed is None else seed)
    ordered = sorted(sentences, key=lambda s: s["sent_id"])
    picked = []
    for stratum, count in SAMPLE_PLAN[product]:
        wanted = count if size == SAMPLE_SIZE else max(1, round(count * size / SAMPLE_SIZE))          # a smaller `--size` scales every stratum
        members = [s for s in ordered if stratum == "*" or judged(product, s)[1] == stratum]
        picked += rng.sample(members, min(wanted, len(members)))
    rest = [s for s in ordered if s not in picked]
    swapped = rng.sample(rest, min(controls, len(rest)))
    result = [("real", s, s["rows"][judged(product, s)[0]][1]) for s in picked]
    result += [("control", s, swapped_tag(s["rows"][judged(product, s)[0]][1])) for s in swapped]
    rng.shuffle(result)
    return result


def sample_lines(product, sample, prefix="R"):
    """(Markdown lines of the review file, key rows): every token with its tag, the judged word with the proposed tag."""
    lines, keys = list(chd.REVIEW_INSTRUCTIONS), []
    for number, (kind, sentence, shown) in enumerate(sample, 1):
        name = "%s%03d" % (prefix, number)
        index, stratum, _ = judged(product, sentence)
        shown_rows = [(row[0], shown if i == index else row[1]) for i, row in enumerate(sentence["rows"])]
        lines += ["## " + name, "", "Text: " + sentence["text"], "Tagged: " + " ".join("%d:%s/%s" % (i, form, tag) for i, (form, tag) in enumerate(shown_rows)),
                  "Marked word: %s (index %d)" % (sentence["rows"][index][0], index), "Proposed tag: " + shown, ""]
        keys.append((name, kind, sentence["sent_id"], stratum, index, shown, sentence["rows"][index][1], len(sentence["rows"])))
    return lines, keys


def read_built(out_dir, product):
    return common.read_corpus(os.path.join(out_dir, OUTPUT[product][1]))[1]


def _out_dir(args):
    return args.out_dir or os.path.join(args.data_dir, *OUTPUT[args.product][0].split("/"))


def _review_folder(args):
    path = os.path.join(args.work, "review", args.product)
    os.makedirs(path, exist_ok=True)
    return path


def _command_sample(args):
    sample = build_sample(args.product, read_built(_out_dir(args), args.product), args.seed, args.size, args.controls)
    lines, keys = sample_lines(args.product, sample)
    folder = _review_folder(args)
    common.write_text(os.path.join(folder, "review-sample.md"), lines)
    adjudicate.write_rows(os.path.join(folder, "review-sample-key.tsv"), chd.SAMPLE_KEY_COLUMNS, keys)
    real = sum(1 for kind, _, _ in sample if kind == "real")
    print("%d items: %d real, %d control (seed %d)" % (len(sample), real, len(sample) - real, args.seed or SAMPLE_SEEDS[args.product]))
    return 0


def _command_review_result(args):
    folder = _review_folder(args)
    _, keys = adjudicate.read_records(os.path.join(folder, "review-sample-key.tsv"))
    answers = []
    for name in args.verdicts:
        with open(name, "r", encoding="utf-8") as handle:
            answers += imperative_data.parse_answers(handle.read())
    unknown = {a[0] for a in answers} - {k["item"] for k in keys}
    if unknown:
        raise ValueError("verdicts for unknown items: " + ", ".join(sorted(unknown)[:5]))
    lines, passed = chd.review_lines(*chd.review_numbers(keys, answers))
    adjudicate._print(lines)
    common.write_text(os.path.join(folder, "review-result.md"), lines)
    return 0 if passed else 1


def report_lines(out_dir, product):
    """Markdown counts of a built product for the README."""
    sentences = read_built(out_dir, product)
    draft = {}
    with open(os.path.join(out_dir, OUTPUT[product][2]), encoding="utf-8") as handle:
        for line in handle.read().split("\n")[1:]:
            cells = line.split("\t")
            if len(cells) == len(shapes_dev.SOURCE_COLUMNS):
                draft[cells[0]] = dict(zip(shapes_dev.SOURCE_COLUMNS, cells))
    families = collections.Counter(draft[s["source_id"]]["family"] for s in sentences)
    documents = collections.Counter(s["source_id"] for s in sentences)
    kinds = collections.Counter(judged(product, s)[1] for s in sentences)
    lemmas = collections.Counter(judged(product, s)[2] for s in sentences)
    lines = ["| family | documents | sentences |", "| --- | --- | --- |"]
    lines += ["| %s | %d | %d |" % (name, sum(1 for d in documents if draft[d]["family"] == name), families[name]) for name in sorted(families)]
    lines += ["", "- sentences: %d, tokens: %d, lemmas: %d, documents: %d" % (len(sentences), sum(len(s["rows"]) for s in sentences), len(lemmas), len(documents)),
              "- kinds: " + ", ".join("%s %d" % item for item in sorted(kinds.items())),
              "- most items per lemma: %d, per document: %d" % (max(lemmas.values(), default=0), max(documents.values(), default=0))]
    return lines


# ---------------------------------------------------------------- the other commands

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


def _command_preflight(args):
    """Counts only: documents, families and sides, the clean units and the frame hits of every product (no candidate is written, nothing is labeled)."""
    work = adjudicate.Work(args.work)
    draft = shapes_dev.read_draft(work)
    draft_gate(work, args.data_dir, args.extra_table or ())
    sides = read_partition(work, draft)
    lex = shapes_dev.lexicon_of(args.data_dir)
    vocab = shapes_dev.vocabulary_of(args.data_dir, lex)
    filt = shapes_dev.LeakageFilter(args.data_dir, not args.no_ud, new_tranches=True)
    reference = Reference(args.data_dir, args.extra_text or ())
    sentences = scan_all(work, args.data_dir, "sentence", args.extra_table or (), (), not args.no_ud, filt, reference, lex, vocab)
    headings = scan_all(work, args.data_dir, "heading", args.extra_table or (), (), not args.no_ud, filt, reference, lex, vocab)
    count = collections.Counter(sides[row["source_id"]] for row in draft)
    print("documents in the draft: %d (dev %d, train %d; %d agency families); rejected as a whole: %d"
          % (len(draft), count["dev"], count["train"], len({r["family"] for r in draft}), len(sentences.rejected)))
    tier_of = tier_map(work)
    tiers = collections.Counter(tier_of.get(shapes_dev.sent_key_of(text), "?") for _, _, text, _ in headings.units)
    print("clean sentences: %d; clean heading lines: %d (tiers: %s)" % (len(sentences.units), len(headings.units), ", ".join("%s %d" % t for t in sorted(tiers.items())) or "none"))
    for side in ("dev", "train"):
        sc, hd = restrict(sentences, sides, side), restrict(headings, sides, side)
        hits = [sx.compound_ambiguous_hit(tokens, lex) for _, _, _, tokens in sc.units]
        imperative = sum(1 for _, _, _, tokens in hd.units if fimp_hit(tokens, lex) is not None)
        initial = sum(1 for _, _, _, tokens in hd.units if initial_noun_hit(tokens, lex) is not None)
        print("%s: %d clean sentences, %d compound frame hits (%d lemmas); %d clean lines, %d imperative (strength 0), %d initial-noun"
              % (side, len(sc.units), sum(h is not None for h in hits),
                 len({shapes_dev.lemma_of(tokens[i], lex) for (_, _, _, tokens), i in zip(sc.units, hits) if i is not None}), len(hd.units), imperative, initial))
    return 0


def read_hmm_pinned(work):
    """The HmmTable of the pinned W/hmm.tsv (compound-head-2 only; `require_pins` already checked the hashes)."""
    return shapes_dev.read_hmm(work, expected_sha256=HMM_SHA256)


def _command_candidates(args):
    product = args.product
    work = adjudicate.Work(args.work)
    require_pins(work, product)
    draft = shapes_dev.read_draft(work)
    draft_gate(work, args.data_dir, args.extra_table or ())
    sides = read_partition(work, draft)
    lex = shapes_dev.lexicon_of(args.data_dir)
    unit = "sentence" if KIND_OF_PRODUCT[product] == "compound" else "heading"
    full = scan_all(work, args.data_dir, unit, args.extra_table or (), args.extra_text or (), not args.no_ud, lex=lex)
    scan = restrict(full, sides, SIDE[product])
    controls_round = args.round == CONTROLS_ROUND
    if args.round and not (product == "compound-head-2" and controls_round):
        raise ValueError("--round %s is only for compound-head-2 (the second control round)" % CONTROLS_ROUND)
    name = CONTROLS_ROUND if controls_round else CANDIDATE_ROUND[product]
    if product == "compound-head-2":
        old_counts, old_ing = old_target_counts(args.data_dir)
        exclude, room = frozenset(), None
        if controls_round:
            main = shapes_dev.RoundWork(args.work, CANDIDATE_ROUND[product])
            exclude = frozenset(c["sent_key"] for c in shapes_dev.read_candidates(main))
            items, _ = shapes_dev.read_decided(main, args.data_dir)
            items = [i for i in items if sides.get(i["source_id"]) == "dev"]
            room = control_room(items, {c["cand_id"]: c for c in shapes_dev.read_candidates(main)}, old_counts, old_ing, args.seed)
        rows, report, counts = compound_head_candidates(scan, lex, read_hmm_pinned(work), args.seed, old_counts, exclude, controls_round, room)
    elif product == "compound-2":
        rows, report, counts = compound_candidates(scan, lex, args.seed, chd.heading_line_keys(work))
    else:
        prior = pilot_rows_of_side(work, sides, SIDE[product])
        training = training_heading_token_sets(args.data_dir, restrict(full, sides, "train")) if SIDE[product] == "dev" else ()
        rows, report, counts = heading_candidates(scan, product, lex, args.seed, prior, training)
    round_work = _round_work(args, name)
    common.write_text(round_work.private_file("candidates-report.md"), report)
    adjudicate.write_rows(round_work.private_file("rejected-documents.tsv"), ("source_id", "reason"), sorted(scan.rejected.items()))
    shapes_dev.write_candidates(round_work, rows)
    print("\n".join(report[:14]))
    print("%d candidates written to rounds/%s/candidates.tsv" % (len(rows), name))
    missed = [] if controls_round else gate_messages(product, counts)
    for message in missed:
        print("GATE FAILED: " + message, file=sys.stderr)
    return 1 if missed else 0


def _command_build(args):
    work = adjudicate.Work(args.work)
    out_dir = _out_dir(args)
    try:
        built, report = build(work, args.product, out_dir, args.seed, args.allow_short, args.data_dir, args.extra_table or (), args.omit_document or ())
    except (BuildError, shapes_dev.ExcludedSourceError, ValueError) as error:    # DraftError is a ValueError
        print("build refused: %s" % error, file=sys.stderr)
        return 1
    adjudicate._print(report)
    common.write_text(os.path.join(_review_folder(args), "build-report.md"), report)
    return 0


def _command_report(args):
    adjudicate._print(report_lines(_out_dir(args), args.product))
    return 0


def _command_prompt(args):
    print(labeler_prompt(os.path.abspath(args.work), args.round, args.batch, args.model, not args.no_appendix), end="")
    return 0


# ---------------------------------------------------------------- command line

def main(argv=None):
    parser = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    parser.add_argument("--work", required=True, help="work folder (outside the repository)")
    parser.add_argument("--data-dir", default=common.DATA, help="data folder (default: the repository's data/)")
    sub = parser.add_subparsers(dest="command", required=True)

    def options(p, product=True, tables=True):
        if product:
            p.add_argument("--product", required=True, choices=PRODUCTS)
        p.add_argument("--seed", type=int, default=None)
        if tables:
            p.add_argument("--extra-table", action="append", default=None, metavar="PATH", help="a further table of used documents (repeatable)")

    exclusions = sub.add_parser("exclusions", help="index of the documents already used; checks the draft")
    exclusions.add_argument("--extra-table", action="append", default=None, metavar="PATH")
    exclusions.add_argument("--check", nargs="*", default=None, help="document ids or URLs to check")
    sub.add_parser("partition", help="assign every document to the DEV or the TRAIN side by family (W/partition.tsv)")
    sub.add_parser("lines", help="heading, table-of-contents and labeled step-title lines of every document (W/lines)")
    tokenize = sub.add_parser("tokenize", help="tokenize every sentence and line with the library tokenizer")
    tokenize.add_argument("--cmd", default=None)
    for name, help_text in (("preflight", "counts only"), ("pilot", "the pilot round hdg-pilot"), ("candidates", "the candidates of a product")):
        p = sub.add_parser(name, help=help_text)
        options(p, product=name == "candidates")
        p.add_argument("--extra-text", action="append", default=None, metavar="DIR")
        p.add_argument("--no-ud", action="store_true")
        if name == "pilot":
            p.add_argument("--documents", default=None, help="a file with the ids of the wave-1 documents (one per line)")
        if name == "candidates":
            p.add_argument("--round", default=None, help="chd2c: the second control round of compound-head-2")
    sub.add_parser("pilot-result", help="the pilot gate; exit 1 when the work must stop")
    build_parser = sub.add_parser("build", help="write the corpus and the sources table of a product")
    options(build_parser)
    build_parser.add_argument("--out-dir", default=None)
    build_parser.add_argument("--allow-short", action="store_true")
    build_parser.add_argument("--omit-document", action="append", default=None, metavar="ID[=REASON]")
    sample = sub.add_parser("sample", help="the review sample for the precision gate")
    options(sample, tables=False)
    sample.add_argument("--out-dir", default=None)
    sample.add_argument("--size", type=int, default=SAMPLE_SIZE)
    sample.add_argument("--controls", type=int, default=SAMPLE_CONTROLS)
    review = sub.add_parser("review-result", help="precision of the review; exit 1 when the gate is not met")
    options(review, tables=False)
    review.add_argument("--verdicts", nargs="+", required=True)
    review.add_argument("--out-dir", default=None)
    report = sub.add_parser("report", help="counts of a built product for the README")
    options(report, tables=False)
    report.add_argument("--out-dir", default=None)
    prompt = sub.add_parser("prompt", help="the frozen labeler prompt (and the appendix) of one run")
    prompt.add_argument("--round", required=True)
    prompt.add_argument("--batch", required=True)
    prompt.add_argument("--model", required=True)
    prompt.add_argument("--no-appendix", action="store_true")
    args = parser.parse_args(argv)
    if getattr(args, "seed", None) is None and args.command in ("preflight", "pilot", "candidates", "build"):
        args.seed = SEED
    handlers = {"exclusions": _command_exclusions, "partition": _command_partition, "lines": _command_lines, "tokenize": _command_tokenize,
                "preflight": _command_preflight, "pilot": _command_pilot, "pilot-result": _command_pilot_result, "candidates": _command_candidates,
                "build": _command_build, "sample": _command_sample, "review-result": _command_review_result, "report": _command_report, "prompt": _command_prompt}
    try:
        return handlers[args.command](args)
    except (ValueError, shapes_dev.ExcludedSourceError) as error:        # DraftError and PinError are ValueErrors
        print("%s refused: %s" % (args.command, error), file=sys.stderr)
        return 1


if __name__ == "__main__":
    sys.exit(main())

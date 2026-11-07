"""Builder of the hard-shape development set data/eval/shapes-dev/ (Python 3, standard library only).

The set holds sentences with one judged word each (a word that is both a noun and a verb in the lexicon), chosen for five
hard shapes, plus controls (the same spelling in the other class). Every sentence is labeled blind by two models that
follow data/CONVENTIONS.md; this tool does everything around the models that needs no model and never calls one.
The work folder (`--work`) is outside the repository. It holds what sourcing and authoring hand over, the blind batches
the taggers read and their outputs, and the private key.

Input layout of the work folder W (`--work W`):

  W/sources-draft.tsv          one row per source document, the 12 columns of data/eval/gold-v2/sources.tsv:
                               source_id family title url author year license_basis terms_url retrieved raw_sha256
                               text_sha256 sentences        (tab separated, a header row; `sentences` is recomputed by build)
  W/text/<source_id>.txt       the extracted text of a document, one sentence per line, UTF-8 (headers, footers, step
                               labels and tables of contents already stripped; the document id is the file name)
  W/authored/<shape>.tsv       authored top-up sentences of a shape: header `lemma<TAB>role<TAB>sentence`, role `target` or
                               `control` (a control uses the lemma in the class other than the shape's class)
  W/tokens.tsv                 written by `tokenize`: the tokens of every sentence (the corpus tokenization of the library)
  W/excluded-sources.tsv       optional; documents that must not be in the set: header `source_id<TAB>reason<TAB>decided`, one row per
                               document (a document the owner dropped after its text was sourced or even labeled; `decided` is the
                               date of the decision, YYYY-MM-DD, required by `build`; the two-column header is read by the other commands)

Excluded sources (W/excluded-sources.tsv). `candidates` (every round) skips every sentence of an excluded document (drop reason
`excluded-source`, one count per sentence; the document is not a rejected document, it is no candidate source at all). Items of an excluded
document that are already decided (in the root or in a round folder) are treated as dropped with the reason `excluded-source`, whatever
their labels were: `decide` writes them so, and `read_decided` (the reader that `sample`, `build`, `report` and the lemma counts of later
rounds use) overrides a stored `keep`, so they never reach dev.tsv or sources.tsv, no review sample, and no lemma cap. `decide` reports them
in its `excluded-source` column and `report` in the table `dropped: excluded source` per shape and role. Replacement candidates come from
another round (`candidates --round NAME`) that skips the excluded documents. `build` also writes the committed record
data/eval/shapes-dev/excluded-sources.tsv (source_id, url from the draft, reason, decided; one row per excluded document), so
that sources.tsv, which lists only the used documents, stays a table of the shared 12-column format.

Fail-closed exclusions. The committed record is required for both profiles and is read by the tool itself (`read_committed_exclusions`,
strictly): a missing file, a zero-byte file, a file without the exact header and a malformed row are errors, and `exclusions`, `candidates`,
`decide`, `sample`, `build` and `report` refuse (exit code 1) before they do anything else. A file that holds only the header is valid: it records that nothing is
excluded (a first standard build starts from such a file; `build` writes the record, header only when nothing is excluded, and never
deletes it). W/excluded-sources.tsv stays optional, but only because the committed record must exist: it adds rows and never replaces
the record. Every consumer of the exclusions (`decide`, `read_decided`, the lemma counts, `sample`, `build`, `report`) uses the merged
exclusions (`merged_excluded_sources`: the committed record united with W/excluded-sources.tsv when it exists, by id and by normalized URL
through the URL of the draft row), so a work folder without the optional file still drops the items of a committed exclusion. The record is
indexed with the used documents, so `candidates` rejects a document whose id or normalized URL it lists even when
W/excluded-sources.tsv lacks it. `build` refuses (exit code 1, nothing written) when a chosen sentence or a used source row has an id or
normalized URL listed in the committed record or in W/excluded-sources.tsv. The written record is the union of the committed rows and the
W rows; a committed row is never dropped or changed, an id in both tables with a different URL, or a duplicate id within one table, is an
error (exit code 1, nothing written).

Quota gate. `build` refuses (exit code 1, nothing written) unless every shape has exactly `--per-shape` targets (default 120), there are
exactly `--controls` controls (default 100), no lemma exceeds its cap (`--lemma-cap` 4, `--shape-lemma-cap`, controls 2 per lemma) and no
shape or the control stratum exceeds the authored share (`--share`, 30%). `--allow-short` builds anyway and lists the misses as warnings.

Commands (all take `--work W`; `--data-dir D` replaces the repository's data folder, for tests; `--tranche standard|hard` selects the profile, see
"Hard tranche" below, and goes before the command):

  python tools/data/shapes_dev.py --work W exclusions [--check ID_OR_URL ...]   # used-document index W/exclusions.tsv; checks the draft
  python tools/data/shapes_dev.py --work W tokenize [--cmd "COMMAND PREFIX"]    # shells out to the C# trainer command shapes-tokenize
  python tools/data/shapes_dev.py --work W candidates [--no-ud] [--target 120] [--over 1.6] [--shape-over SHAPE=FACTOR ...]
                                                     [--lemma-cap 4] [--shape-lemma-cap SHAPE=N ...] [--control-pool 100]
                                                     [--round NAME] [--only-shapes SHAPE[,SHAPE]] [--no-controls] [--compound-strict]
                                                     [--controls-only [--shape-max SHAPE=N ...]]       # hard tranche, with --round
                                                     # candidates = cleanliness, position and lexicon pre-selection + leakage filters
  python tools/data/shapes_dev.py --work W export [--size 150] [--rows 3500] [--round NAME]   # blind batches, private/key.tsv, batches/status.tsv
  python tools/data/shapes_dev.py --work W check-batches [--round NAME]         # 3 columns, tokens equal the key, no leak
  python tools/data/shapes_dev.py --work W validate-output --batch b001 --model opus [--round NAME]   # (or --all); updates batches/status.tsv
  python tools/data/shapes_dev.py --work W status [--round NAME]
  python tools/data/shapes_dev.py --work W decide [--round NAME]                # the decision rule
  python tools/data/shapes_dev.py --work W sample [--per-shape 12] [--controls 4]   # review sample for the precision gate (all rounds)
  python tools/data/shapes_dev.py --work W review-result --verdicts FILE ...     # precision of the model review; STOP below 95%
  python tools/data/shapes_dev.py --work W build [--out-dir D] [--shape-lemma-cap SHAPE=N ...] [--allow-short]   # dev.tsv and sources.tsv (root and rounds)
  python tools/data/shapes_dev.py --work W report [--out-dir D]                 # counts for the README (the root and every round)

The tokenizer command is `dotnet run -c Release --project tools/DemaConsulting.PartOfSpeech.Trainer -- shapes-tokenize --in FILE
--out FILE` (the default of `tokenize`): one sentence per input line; one line `text<TAB>sentences<TAB>tokens` (tokens
separated by single spaces) per output line, in the same order. The tokens therefore follow the corpus convention by
construction. The data test of the repository asserts that the library tokenizer reproduces every stored token.

Hard tranche (`--tranche hard`, default `standard`: the standard profile and its committed outputs are unchanged, byte for byte). A second,
harder development file data/eval/shapes-dev/hard.tsv (with hard-sources.tsv and hard-leakage-report.tsv; ids `shapes-dev-hard-NNNN`) holds
the contested cases of four shapes (n-compound-head, n-subject-initial, v-imperative-initial, v-after-modal; n-measure has no hard cases
and stays valid with zero items). It has its own work folder W (for example W/hard) with the same layout as above, plus

  W/hmm.tsv                    written by the harness tools/data/hmm-tags (see its README): the part-of-speech probability mass of every
                               token from the WordSense Basic classifier (HMM tagger plus cue model), with the WordSense commit and the SHA-256 of
                               the DLL that ran in its header; a sentence is found by its token sequence, a missing sentence is an error.
                               `read_hmm` refuses a file whose commit or DLL SHA-256 is not the pinned one (6530de19..., b96e0055...), whose
                               input_sha256 is not the SHA-256 of the current W/tokens.tsv (exact: a tokens.tsv that gained rows needs a new
                               hmm-tags run), or whose sentence count is not that of the single-sentence rows of tokens.tsv; no override

Selection rule (pre-registered; it is model-independent: no PartOfSpeech model file, decoder, prediction, error or score is read by this
tool or by the harness, only the lexicon, the text and the WordSense Basic numbers). A sentence is a candidate when it passes the unchanged
candidate filters (cleanliness, length, duplicates, document key hits, the held-out filter through `leakage.held_out_overlap_keys` only, the
excluded sources) and is not a standard-tranche sentence: its sentence key is not one of data/eval/shapes-dev/dev.tsv (drop reason
`equals-standard-tranche`) and, for a sentence of at least 10 words, less than 80% of its word 6-grams are contained in the standard
sentences of the same document (`near-standard-tranche`). For every shape:

  1. frame: `shape_hits` (the unchanged position and lexicon pre-selection; n-compound-head always with `--compound-strict`; the
     lexicon ambiguity of the target word, NOUN and VERB listed, is part of every frame);
  2. contested band: q = the WordSense Basic mass of the shape's class (Noun for `n-*`, Verb for `v-*`) at the target;
     eligible iff 0.10 <= q < 0.97 (BAND_MIN, BAND_MAX). q < 0.5 is the pure-disagreement part of the band. The floor spares labeling on
     candidates the model is nearly sure are the other class (mostly no instance of the shape); the ceiling drops near-certain easy cases;
  3. order: q ascending (hardest first), ties by `rank_of(seed, "hard", shape, sentence key)`; at most `--lemma-cap` (4) candidates per
     lemma; the first B candidates are written: n-compound-head 400, v-after-modal 300, v-imperative-initial and n-subject-initial all
     (`--budget SHAPE=N|all` replaces a budget, for a later round);
  4. controls (blind like the targets, no band): for each shape the other-class real sentences of the lemmas of its candidate targets
     (`control_hits`), at most 2 per lemma, `--control-pool` (50) per shape;
  5. gold: two blind labelers (decision rule below, unchanged); the model's answer and q are never shown to a labeler (the batch files
     carry only id, index and token) and never change a label. No authored sentences.

`build --tranche hard` takes the kept real targets in the same order (q ascending, lemma cap 4) up to the cap per shape, and controls (other
class, 2 per lemma, lemma of a chosen target of the shape, by seeded rank) up to the control cap. Quota gate of the hard tranche (floor / cap
per shape: n-compound-head 80/100, v-imperative-initial 70/100, v-after-modal 80/100, n-subject-initial 40/100, n-measure 0/0; controls 50/60; no
authored sentence): a miss below a floor refuses the build (exit code 1, nothing written) unless `--allow-short`. A round (`--round h2`) can add
the next-hardest block of a shape that is below its floor; it works like the rounds of the standard tranche. A controls-only round
(`candidates --tranche hard --round NAME --controls-only --control-pool N`, with `--only-shapes` the shapes wanted) adds more controls without
any target: the lemmas are those of the targets that `build` selects from the decided earlier work areas today (`build_control_room`:
`select_items_hard` itself, same caps and lemma cap) and a lemma gets at most the controls (2 per lemma) that the build still takes for it,
so the target selection of the build cannot change; the usual exclusions apply (earlier candidates, standard tranche, held-out filter, excluded
sources). The hard build reads the exclusion
record fail-closed and never rewrites it. `sample --tranche hard` draws the hand-check sample from the targets the build would take, at least half
of each shape's items with q < 0.5 (`--low-q-share`). `report --tranche hard` adds the q-band table (labeled, kept by both labelers and built
items per band and shape).

Cleanliness filter (candidate stage, real sentences only; see `noise_reason` for the exact rules; counts are reported per rule as
`noise-<rule>` in private/candidates-report.md). A sentence is dropped when it does not start with an upper-case letter or end in
`.`, `!` or `?`; has a character outside ASCII letters, digits, space and `. , ; : ' " ( ) ? ! $ % & / # + = -`; has slash or doubled
punctuation junk, a broken hyphen word, a stray apostrophe or a lone lower-case letter; has unbalanced brackets or quotes; is a list
item or citation; repeats a word or a token trigram three times; is all capitals or a title (more than half of the words capitalized);
is a short caption without a clause; or has a lower-case alphabetic word that is in no word list (every lower-cased word form of
open/train.tsv and masc/masc.tsv, read only as a spelling list, plus the lexicon spellings; capitalized non-initial tokens, tokens with
a digit and abbreviations are exempt).

Shape pre-selection (positions and lexicon only, never the class): n-measure needs the target (lower case) right after a digit number,
`half` or a number word (one to twelve, fifteen, twenty, thirty to ninety, hundred, thousand, million; not `several`, and not thirteen,
fourteen or sixteen to nineteen), no label word (`No.`, `Type`, `Section` ...) before the number, and no noun-capable word (or
adjective plus noun-capable word) after the target, so `two machine guns` and `1860 forest planting` do not qualify; units
and quantity nouns come first. v-after-modal needs a modal or `to` right before the target. Per shape at most ceil(target * over)
targets (`--shape-over SHAPE=FACTOR` replaces the factor of a thin shape) with at most `--lemma-cap` per lemma; per shape `--control-pool`
control candidates from the lemmas of that shape's targets, at most 2 per lemma.

Compound modifier rule (`--compound-strict`, only for the n-compound-head pattern; off by default so that the candidates of round 1 and 2
stay reproducible; round 3 uses it). The token before the target must be noun-like (see `noun_like_modifier`): the lexicon lists a NOUN
reading and no ADJ, ADV, DET, PRON, NUM, ADP, AUX, PART, CCONJ, SCONJ or INTJ reading; it is no number word, digit number or function word; and
a word ending in `-ing` or `-ed` must be listed as a noun (NOUN or PROPN) only, since a participle modifies the head like an adjective.
Rationale: in round 2 the labelers called the word before the target an adjective in most of the frame-mismatch drops.

Rounds (incremental labeling, so extra real candidates of chosen shapes can be added without touching labeled items or ids). The root of
the work folder is round 0; `--round NAME` (letters, digits, `-`, `_`) selects the folder W/rounds/NAME with its own candidates.tsv, batches/
(with out/opus and out/sol), and private/ (key.tsv, decided.tsv, ...); tokens.tsv, text/, authored/ and sources-draft.tsv stay shared in W.
`candidates --round NAME` drops every sentence whose key, and every (source_id, line), is already in the candidates of the root and of the
earlier rounds (natural name order: r2 before r10), so no sentence is labeled twice; takes `--only-shapes A,B` (only those shapes) and
`--no-controls`; skips authored rows of other shapes; and prefers new lemmas: a lemma that already has `--lemma-cap` kept targets (decided
items; candidates when the earlier round is not decided) of the shape in earlier rounds gets no candidate, a lemma that has some comes after
the new lemmas. Candidate ids carry the round name (`r3-c00001`) and the opaque batch ids and the batch shuffle use a seed derived from the
round name and avoid every id already in an earlier key, so ids never collide. `export`, `check-batches`, `validate-output`, `status` and
`decide` take `--round NAME` and work on that folder only. `sample`, `build` and `report` ignore `--round` and merge the decided items of the
root and of every round folder under W/rounds (a folder without decided.tsv is skipped; the root keeps its old behavior when no round exists):
the lemma cap, the authored cap and the selection run across the union. Hand check: generate the sample after every round is decided.

Decision rule (the core was committed in this docstring, PR #10 commit 88bafc2, before any label existed: target disagreement drops the sentence, class-mismatch, and a disputed non-target token takes the tag of the first labeler; the cleanliness filters, frame validation, base-form and nominal-gerund rules were added later and only remove items; see "Honest record of the order" in data/eval/shapes-dev/README.md; see `decide_item` and `frame_mismatch`). For the judged
word (the target): different tags -> drop the sentence (counted per shape); the same tag but a class other than the intended one
(the shape's class for a `target`, the other class for a `control`) -> drop, counted as `class-mismatch`; the intended class but a
target outside the frame of its shape (only for a `target`, by the tags both labelers gave the neighbor tokens: n-compound-head needs
NOUN or PROPN before; n-measure needs NUM before and no NOUN, PROPN or ADJ+noun after; n-subject-initial and v-imperative-initial need
token 0, where the class Noun is enough because the shape tests the noun reading in a verb-biased position; v-after-modal needs AUX or
PART before; v-imperative-initial and v-after-modal also need the base form: the lower-cased surface form of the target equals its
`base_form_of` (a gerund such as `Avoiding`, a past form or a third person form is no base form); the noun shapes n-subject-initial,
n-compound-head and n-measure drop a target whose lower-cased surface form ends in `ing` and has a verb stem by `base_form_of` that
differs from it (a possible nominal gerund, which data/CONVENTIONS.md does not decide)) -> drop, counted as `frame-mismatch`; otherwise keep. Every
other token: the same tag -> that tag, `unanimous`; different tags -> `disputed`, the tag written is the one of the first labeler
(the `opus` tagger), a fixed tie-break; flags only where both agree. The target is never disputed. Confidence is recorded and unused.

Blindness: a batch file has exactly three columns `id, token_index, token`; the opaque id is unrelated to the sentence, its shape,
its source or its order. The key lives in private/ and the taggers are never pointed to it. The held-out sets, the shape probe
among them, are seen only as the opaque hash set `leakage.held_out_overlap_keys()` (hashed sentence keys of the evaluation sets and hashed
word 5-grams of the probe sentences; the probe is read through its sentence column only, never its labels, shapes, lemmas or targets).
The set is used only to exclude exact or near-duplicate overlaps, never to choose, weight or frame a candidate; no probe text is printed or stored.
"""
import argparse
import collections
import hashlib
import math
import os
import random
import re
import shlex
import subprocess
import sys

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import adjudicate
import common
import leakage

# ---------------------------------------------------------------- constants

SEED = 20261110
ROUNDS_DIR = "rounds"
ROUND_NAME_RE = re.compile(r"[A-Za-z0-9_-]+")
EXCLUDED_SOURCES_FILE = "excluded-sources.tsv"
EXCLUDED_COLUMNS = ("source_id", "reason")
EXCLUDED_DECIDED_COLUMN = "decided"
EXCLUDED_OUTPUT_COLUMNS = ("source_id", "url", "reason", "decided")
EXCLUDED_OUTPUT_FILE = "excluded-sources.tsv"
EXCLUDED_OUTPUT_PATH = "data/eval/shapes-dev/excluded-sources.tsv"
EXCLUDED_REASON = "excluded-source"
EXCLUSION_COMMANDS = ("exclusions", "candidates", "decide", "sample", "build", "report")      # the commands that load the committed exclusion record (required)
SHAPES = ("n-compound-head", "n-subject-initial", "v-imperative-initial", "n-measure", "v-after-modal")
SHAPE_CLASS = {shape: ("Noun" if shape.startswith("n-") else "Verb") for shape in SHAPES}
OTHER_CLASS = {"Noun": "Verb", "Verb": "Noun"}
CLASS_TAG = {"Noun": "NOUN", "Verb": "VERB"}
ROLES = ("target", "control")
ORIGINS = ("real", "authored")
AUTHORED_ID = "shapes-dev-authored"
AUTHORED_FAMILY = "project-written"
AUTHORED_LICENSE = "Written by the project; the project owner dedicates the sentences to the public domain under CC0 1.0"
AUTHORED_TERMS = "https://creativecommons.org/publicdomain/zero/1.0/"
TARGET_PER_SHAPE = 120
OVER_COLLECT = 1.6
LEMMA_CAP = 4
CONTROL_LEMMA_CAP = 2
CONTROL_POOL = 100             # control candidates per shape (the final set keeps those whose lemma a kept target of the shape has)
AUTHORED_SHARE = 0.30
CONTROLS = 100
MIN_TOKENS, MAX_TOKENS = 3, 30
DOCUMENT_KEY_HITS = 3          # a document with this many sentences that are also in a training or evaluation set is rejected
PRECISION_MIN = 0.95
CONTROLS_ACCEPTED_MAX = 1

# The hard tranche (`--tranche hard`): file names, ids, shapes, the pre-registered contested band, budgets and quota floors and caps.
TRANCHES = ("standard", "hard")
TRANCHE_FILES = {"standard": ("dev.tsv", "sources.tsv"), "hard": ("hard.tsv", "hard-sources.tsv")}
TRANCHE_ID = {"standard": "shapes-dev-%04d", "hard": "shapes-dev-hard-%04d"}
HMM_FILE = "hmm.tsv"
HMM_MAGIC = "# hmm-tags 1"
HARD_WORDSENSE_COMMIT = "6530de1928baf30f9a14446a54bfb5dd5f67b41c"    # the classifier the hard-tranche rule is pre-registered on (README)
HARD_WORDSENSE_DLL_SHA256 = "b96e0055664896983e311f626d41e5251a2dcf1a236d86f6ec50beaae6a746bf"
HARD_SHAPES = ("n-compound-head", "n-subject-initial", "v-imperative-initial", "v-after-modal")   # n-measure has no hard cases
BAND_MIN, BAND_MAX = 0.10, 0.97                  # eligible iff BAND_MIN <= q < BAND_MAX (q = Basic model mass of the shape's class)
DISAGREE_Q = 0.5                                 # q below this is the pure-disagreement part of the band
BAND_EDGES = (BAND_MIN, 0.30, 0.50, 0.70, 0.90, BAND_MAX)
HARD_BUDGETS = {"n-compound-head": 400, "n-subject-initial": None, "v-imperative-initial": None, "v-after-modal": 300}   # None = all
HARD_FLOORS = {"n-compound-head": 80, "n-subject-initial": 40, "v-imperative-initial": 70, "n-measure": 0, "v-after-modal": 80}
HARD_CAPS = {"n-compound-head": 100, "n-subject-initial": 100, "v-imperative-initial": 100, "n-measure": 0, "v-after-modal": 100}
HARD_CONTROL_MIN, HARD_CONTROL_MAX = 50, 60
HARD_CONTROL_POOL = 50                           # control candidates per shape
NEAR_STANDARD_WORDS = 10                         # a sentence of at least this many words ...
NEAR_STANDARD_SHARE = 0.8                        # ... with at least this share of its word 6-grams in the standard sentences of its document
NEAR_STANDARD_GRAM = 6
EQUALS_STANDARD = "equals-standard-tranche"
NEAR_STANDARD = "near-standard-tranche"

SOURCE_COLUMNS = ("source_id", "family", "title", "url", "author", "year", "license_basis", "terms_url", "retrieved", "raw_sha256",
                  "text_sha256", "sentences")
CANDIDATE_COLUMNS = ("cand_id", "shape", "role", "target", "lemma", "origin", "source_id", "line", "text", "tokens")
HARD_CANDIDATE_COLUMNS = CANDIDATE_COLUMNS + ("q",)       # the hard tranche adds q (3 decimals; empty for a control)
KEY_COLUMNS = ("opaque_id", "batch", "sent_key", "source_id", "ntokens")
ITEM_COLUMNS = ("cand_id", "sent_key", "shape", "role", "target", "lemma", "origin", "source_id", "outcome", "reason", "opus_tag",
                "sol_tag", "agreed_tag", "disputed_tokens")
HARD_ITEM_COLUMNS = ITEM_COLUMNS + ("q",)
ROW_COLUMNS = ("sent_key", "index", "form", "tag", "status")
SAMPLE_KEY_COLUMNS = ("item", "kind", "cand_id", "shape", "target", "shown_tag", "agreed_tag")
USED_SOURCE_TABLES = ("open/sources.tsv", "eval/gold-v2/sources.tsv", "silver/sources.tsv", "masc/sources.tsv",
                      "masc/excluded-documents.tsv", "silver/excluded-documents.tsv")
EXTRA_DOCUMENTS = ("wordsense-ste-probe",)
HEADER = [
    "# corpus = DemaConsulting.PartOfSpeech hard-shape development set (UPOS), dev split",
    "# license = CC0-1.0 for annotations and selection; sentences are US federal government works, US public domain or CC0, see sources.tsv",
    "# columns = form, upos, status",
    "# status = unanimous | disputed (two blind model labelers; disputed = they differ, the tag is the first labeler's)",
    "# judged word = per sentence: shape (one of the five hard shapes), role (target | control), target (zero-based token index), "
    "lemma, origin (real | authored); the judged word is never disputed",
    "# use = development only: select settings here, never train on it, never report it as a held-out figure",
]
HARD_HEADER = [
    "# corpus = DemaConsulting.PartOfSpeech hard-shape development set, hard tranche (UPOS), dev split",
    "# license = CC0-1.0 for annotations and selection; sentences are US federal government works, US public domain or CC0, see hard-sources.tsv",
    "# columns = form, upos, status",
    "# status = unanimous | disputed (two blind model labelers; disputed = they differ, the tag is the first labeler's)",
    "# judged word = per sentence: shape (one of the hard shapes), role (target | control), target (zero-based token index), "
    "lemma, origin (real); the judged word is never disputed",
    "# selection = the contested band of the WordSense Basic classifier, hardest first (see README.md); the gold is the agreement of two blind labelers",
    "# use = development only: select settings here, never train on it, never report it as a held-out figure",
]
HEADERS = {"standard": HEADER, "hard": HARD_HEADER}

# Closed word lists of the position heuristics. They only pre-select candidates; the class of the judged word always comes
# from the two blind labelers.
DETERMINERS = frozenset("the a an this that these those each every any some no all both its his her their our my your whose "
                        "another either neither".split())
PREPOSITIONS = frozenset("of in on at by for with from into onto over under between through during before after against along "
                         "around within without about above below across behind beside beyond near off per than via toward "
                         "towards upon".split())
MODALS = frozenset("can could may might must shall should will would".split())
AUXILIARIES = MODALS | frozenset("is are was were be been being am has have had do does did".split())
OBJECT_PRONOUNS = frozenset("it them him her us you me one".split())
CONJUNCTIONS = frozenset("and or but nor if when while that which because although".split())
FOLLOWING_ADVERBS = frozenset("not never always only all both each any also then".split())
# The number words of the n-measure pre-selection: one to twelve, fifteen, twenty, the tens thirty to ninety, hundred, thousand and
# million. thirteen, fourteen and sixteen to nineteen are NOT in the set (a known limit: the labeled candidates were generated with
# this vocabulary, so changing it would make the built set irreproducible from `candidates`).
NUMBER_WORDS = frozenset("one two three four five six seven eight nine ten eleven twelve fifteen twenty thirty forty fifty sixty "
                         "seventy eighty ninety hundred thousand million".split())
QUANTITY_WORDS = frozenset(["half"])        # `several`, `few`, `many`, `another` are not numbers: no measure frame
LABEL_WORDS = frozenset("no no. type section figure fig fig. table step para paragraph chapter part item number article page rule class "
                        "grade form title appendix model mark mil series volume vol vol. line group".split())
UNITS = frozenset("pound ounce cup drop pinch load round turn step inch foot yard mile gallon pint quart bushel degree stone "
                  "dose pack bar piece slice sheet roll box bag can jar head bunch handful teaspoon tablespoon second minute point "
                  "shot pass cut count bolt strip stroke grain pair set bundle batch bale block case chain rod link mark mil dash "
                  "stick tray pan sack cord ton gram barrel share bit lot fold strand span lap tier layer coat".split())
QUANTITY_OF = frozenset("of per each apiece long wide deep high thick apart".split())   # a counted noun right before these is a quantity
BASE_FORM_SHAPES = ("v-imperative-initial", "v-after-modal")
FUNCTION_WORDS = DETERMINERS | PREPOSITIONS | AUXILIARIES | CONJUNCTIONS | OBJECT_PRONOUNS | frozenset("not to as so than too only also".split())
MODIFIER_BLOCKING_TAGS = frozenset("ADJ ADV DET PRON NUM ADP AUX PART CCONJ SCONJ INTJ".split())   # a reading that makes a word no plain noun modifier
NOUN_ONLY_TAGS = frozenset(("NOUN", "PROPN"))


# ---------------------------------------------------------------- small helpers

def normalize_text(text):
    """A sentence as stored: NFC, any white space run (also a non-breaking space) one space, no outer space."""
    return " ".join(common.nfc(text).split())


def sent_key_of(text):
    """The id of a sentence in the work folder: a hash of its leakage key (so two spellings of one sentence are one)."""
    return "k" + hashlib.sha256(common.key(text).encode("utf-8")).hexdigest()[:12]


def rank_of(seed, *parts):
    """A deterministic pseudo-random rank: the same inputs always sort the same way, whatever the platform."""
    return hashlib.sha256((str(seed) + ":" + ":".join(str(p) for p in parts)).encode("utf-8")).hexdigest()


def tag_class(tag):
    """`Noun` (NOUN, PROPN), `Verb` (VERB) or `Other` for the tag of a judged word (the classes of data/CONVENTIONS.md)."""
    if tag in ("NOUN", "PROPN"):
        return "Noun"
    return "Verb" if tag == "VERB" else "Other"


def intended_class(shape, role):
    """The class that the judged word of an item has when the sentence is an instance: the shape's class, or the other for a control."""
    return SHAPE_CLASS[shape] if role == "target" else OTHER_CLASS[SHAPE_CLASS[shape]]


def work_path(work, *parts):
    """A path of the folder the work object stands for (the root, or one round folder)."""
    return os.path.join(work.root, *parts)


def shared_path(work, *parts):
    """A path of the shared inputs (sources-draft.tsv, text/, authored/, tokens.tsv): always the root of the work folder."""
    return os.path.join(getattr(work, "base", work.root), *parts)


class RoundWork(adjudicate.Work):
    """The work area of one labeling round: W/rounds/NAME holds its candidates.tsv, batches/ and private/ (`base` is W)."""

    def __init__(self, base, name):
        if not ROUND_NAME_RE.fullmatch(name):
            raise ValueError("a round name has letters, digits, '-' and '_' only: %r" % name)
        super().__init__(os.path.join(base, ROUNDS_DIR, name))
        self.base = base
        self.name = name


def open_work(args):
    """The Work of `--work`, or the RoundWork of `--round NAME` when the command line names a round."""
    name = getattr(args, "round", None)
    return RoundWork(args.work, name) if name else adjudicate.Work(args.work)


def natural_key(name):
    """Sort key of a round name that orders numbers by value (r2 before r10)."""
    return [(0, int(part), "") if part.isdigit() else (1, 0, part) for part in re.split(r"(\d+)", name) if part]


def round_names(base):
    """The names of the round folders under W/rounds, in natural order."""
    folder = os.path.join(base, ROUNDS_DIR)
    if not os.path.isdir(folder):
        return []
    return sorted((n for n in os.listdir(folder) if os.path.isdir(os.path.join(folder, n)) and ROUND_NAME_RE.fullmatch(n)), key=natural_key)


def all_works(work, only_existing=None):
    """[Work]: the root of the work folder, then every round folder in natural order; `only_existing` is a file name in private/
    that a work must have (for example `decided.tsv`)."""
    base = getattr(work, "base", work.root)
    works = [adjudicate.Work(base)] + [RoundWork(base, n) for n in round_names(base)]
    if only_existing:
        works = [w for w in works if os.path.exists(w.private_file(only_existing))]
    return works


def earlier_works(work):
    """[Work] whose candidates a new round must not repeat: the root and the rounds before this one (natural order).

    Empty for the root itself (no earlier round) and when a folder has no candidates.tsv.
    """
    name = getattr(work, "name", None)
    if not name:
        return []
    base = work.base
    found = [adjudicate.Work(base)] + [RoundWork(base, other) for other in round_names(base) if natural_key(other) < natural_key(name)]
    return [w for w in found if os.path.exists(work_path(w, "candidates.tsv"))]


def round_seed(seed, name):
    """The seed of the batch shuffle and the opaque ids of a round: the plain seed for the root, a hash of seed and name otherwise."""
    return seed if not name else int(rank_of(seed, "round", name)[:12], 16)


def assign_round_ids(keys, seed, avoid):
    """{key: opaque id} like adjudicate.assign_ids, but never an id of `avoid` (the ids of the earlier rounds)."""
    ordered = sorted(keys)
    numbers = random.Random(seed).sample(range(1 << 24), len(ordered) + len(avoid))
    free = ["s%06x" % n for n in numbers]
    free = [i for i in free if i not in avoid][:len(ordered)]
    return dict(zip(ordered, free))


def read_plain_tsv(path, columns):
    """Rows (dicts) of a hand-written TSV: a header row naming `columns`, then one row per line (no escaping; blank lines ignored)."""
    lines = [line for line in common.read_lines(path) if line.strip() != ""]
    if not lines or tuple(lines[0].split("\t")) != tuple(columns):
        raise ValueError("%s: the header must be %s" % (path, "\\t".join(columns)))
    rows = []
    for number, line in enumerate(lines[1:], 2):
        cells = line.split("\t")
        if len(cells) != len(columns):
            raise ValueError("%s, line %d: %d cells, expected %d" % (path, number, len(cells), len(columns)))
        rows.append(dict(zip(columns, cells)))
    return rows


# ---------------------------------------------------------------- exclusions (documents that training or evaluation already used)

def normalize_url(url):
    """host (lower case, no `www.`) and path (no query, fragment or trailing slash); the empty string for a text that is no URL."""
    return common.normalize_url(url)


def gutenberg_ids(text):
    """The canonical ids `gutenberg-<n>` that a text names (an id, or a gutenberg.org ebook, file or cache URL)."""
    found = {"gutenberg-" + n for n in re.findall(r"gutenberg-(\d+)", text)}
    found |= {"gutenberg-" + n for n in re.findall(r"gutenberg\.org/(?:ebooks|files|cache/epub)/(\d+)", text)}
    return found


def exclusion_index(data_dir):
    """{(kind, value): origin} over the documents of the sources and excluded-documents tables (kind `id` or `url`)."""
    index = {}
    for table in USED_SOURCE_TABLES:
        path = os.path.join(data_dir, *table.split("/"))
        if not os.path.exists(path):
            continue
        for line in common.read_lines(path):
            if not line or line.startswith("#"):
                continue
            cells = line.split("\t")
            if cells[0] in ("source_id", "doc_id", "document_id"):
                continue
            index.setdefault(("id", cells[0]), table)
            for cell in cells:
                if cell.startswith("http"):
                    normalized = normalize_url(cell)
                    if normalized:
                        index.setdefault(("url", normalized), table)
            for found in gutenberg_ids(line):
                index.setdefault(("id", found), table)
    for document in EXTRA_DOCUMENTS:
        index.setdefault(("id", document), "built-in")
    for source_id, url, _, _ in read_committed_exclusions(data_dir):
        index.setdefault(("id", source_id), EXCLUDED_OUTPUT_PATH)
        index.setdefault(("url", normalize_url(url)), EXCLUDED_OUTPUT_PATH)
    return index


def read_committed_exclusions(data_dir):
    """[(source_id, url, reason, decided)] of the committed data/eval/shapes-dev/excluded-sources.tsv, for both profiles.

    Fail closed: a missing file, a zero-byte file, a file without the exact header and a malformed row are a ValueError (see
    `common.read_excluded_table`); a file that holds only the header is the valid record of "nothing is excluded" ([]). The record is
    never read leniently and a missing record is never taken as an empty one (a first standard build starts from a header-only file)."""
    path = os.path.join(data_dir, *EXCLUDED_OUTPUT_PATH.split("/")[1:])
    return common.read_excluded_table(path)


def document_rejection(index, source_id, url=""):
    """The reason (a table name) why a document was already used, or None when its id, Gutenberg number and URL are all new."""
    for found in {source_id} | gutenberg_ids(source_id + " " + url):
        if ("id", found) in index:
            return "id %s is listed in %s" % (found, index[("id", found)])
    normalized = normalize_url(url) if url else ""
    if normalized and ("url", normalized) in index:
        return "url is listed in " + index[("url", normalized)]
    return None


def read_excluded_rows(work):
    """[(source_id, reason, decided)] of W/excluded-sources.tsv (empty when the file is missing), in file order.

    The work-folder file is optional because the committed record (`read_committed_exclusions`, required) is the fail-closed one:
    every command that uses the exclusions (`exclusions`, `candidates`, `decide`, `build`) refuses first when that record is missing.

    The header is `source_id<TAB>reason` with an optional third column `decided` (the date of the owner's decision, `YYYY-MM-DD`);
    `decided` is "" when the column is absent. `build` requires it, because it is written to data/eval/shapes-dev/excluded-sources.tsv."""
    path = shared_path(work, EXCLUDED_SOURCES_FILE)
    if not os.path.exists(path):
        return []
    with open(path, "r", encoding="utf-8", newline="") as handle:        # hand-written: CRLF (a Windows editor) is accepted
        lines = [line for line in handle.read().replace("\r\n", "\n").split("\n") if line.strip() != ""]
    header = tuple(lines[0].split("\t")) if lines else ()
    dated = header == EXCLUDED_COLUMNS + (EXCLUDED_DECIDED_COLUMN,)
    if header != EXCLUDED_COLUMNS and not dated:
        raise ValueError("%s: the header must be %s, optionally followed by \\t%s"
                         % (path, "\\t".join(EXCLUDED_COLUMNS), EXCLUDED_DECIDED_COLUMN))
    rows = []
    seen = set()
    for number, line in enumerate(lines[1:], 2):
        cells = [cell.strip() for cell in line.split("\t")]
        if len(cells) != len(header) or not all(cells):
            raise ValueError("%s, line %d: every row needs a source_id, a reason%s" % (path, number, " and a decided date" if dated else ""))
        if cells[0] in seen:
            raise ValueError("%s, line %d: duplicate source_id %s" % (path, number, cells[0]))
        seen.add(cells[0])
        rows.append((cells[0], cells[1], cells[2] if dated else ""))
    return rows


def read_excluded_sources(work):
    """{source_id: reason} of W/excluded-sources.tsv (empty when the file is missing)."""
    return {source_id: reason for source_id, reason, _ in read_excluded_rows(work)}


def merged_excluded_sources(work, data_dir=None):
    """{source_id: reason} of every excluded document: the committed record (fail closed: `read_committed_exclusions` raises ValueError
    when it is missing or malformed) united with W/excluded-sources.tsv when that file exists.

    The union is by id and by normalized URL: a source of the work folder's sources-draft.tsv whose URL is listed in either table is
    excluded too, under the reason of the row that lists the URL. Every consumer of the exclusions (`decide`, `read_decided`, the lemma
    counts, `sample`, `report`) uses this union, so a work folder without excluded-sources.tsv still drops the committed exclusions.
    `data_dir` is the data folder of the committed record (`common.DATA` when None)."""
    committed = read_committed_exclusions(data_dir or common.DATA)
    draft_urls = {row["source_id"]: row["url"] for row in read_draft(work)}
    rows = [(source_id, url, reason, decided) for source_id, url, reason, decided in committed]
    known = {row[0] for row in rows}
    for source_id, reason, decided in read_excluded_rows(work):
        if source_id not in known:
            rows.append((source_id, draft_urls.get(source_id, ""), reason, decided))
    excluded = {}
    for source_id, _, reason, _ in rows:
        excluded[source_id] = reason
    lookup = {("id", source_id): source_id for source_id in excluded}
    for source_id, url, _, _ in rows:
        if url:
            lookup[("url", common.normalize_url(url))] = source_id
    for source_id, url in draft_urls.items():
        hit = common.excluded_match(lookup, source_id, url)
        if hit and source_id not in excluded:
            excluded[source_id] = excluded[hit]
    return excluded


def excluded_output_rows(work, draft, tranche="standard"):
    """The rows of the committed excluded-sources.tsv: source_id, url (from the draft), reason, decided; [] when nothing is excluded.

    Raises ValueError for an excluded document that has no draft row (no URL) or no decided date. The hard tranche never writes the
    record, so it needs neither: its rows only feed the refusal lookup, with the draft URL when there is one and "" (match by id
    only) when the excluded document was removed from the hard inputs."""
    urls = {row["source_id"]: row["url"] for row in draft}
    rows = []
    for source_id, reason, decided in sorted(read_excluded_rows(work)):
        if tranche == "hard":
            rows.append([source_id, urls.get(source_id, ""), reason, decided])
            continue
        if source_id not in urls or not urls[source_id]:
            raise ValueError("excluded source %s has no url in sources-draft.tsv" % source_id)
        if not common.is_http_url(urls[source_id]):
            raise ValueError("excluded source %s needs an http(s) url in sources-draft.tsv" % source_id)
        if not re.fullmatch(r"\d{4}-\d{2}-\d{2}", decided):
            raise ValueError("excluded source %s needs a `decided` date (YYYY-MM-DD) in excluded-sources.tsv" % source_id)
        rows.append([source_id, urls[source_id], reason, decided])
    return rows


def read_draft(work):
    """[source row dict] of W/sources-draft.tsv (empty when the file is missing); `doc_id` is accepted for the first column header."""
    path = shared_path(work, "sources-draft.tsv")
    if not os.path.exists(path):
        return []
    lines = [line for line in common.read_lines(path) if line.strip() != ""]
    header = lines[0].split("\t") if lines else []
    if header and header[0] == "doc_id":
        header[0] = "source_id"
    if tuple(header) != SOURCE_COLUMNS:
        raise ValueError("%s: the header must be %s" % (path, "\\t".join(SOURCE_COLUMNS)))
    rows = []
    for number, line in enumerate(lines[1:], 2):
        cells = line.split("\t")
        if len(cells) != len(SOURCE_COLUMNS):
            raise ValueError("%s, line %d: %d cells, expected %d" % (path, number, len(cells), len(SOURCE_COLUMNS)))
        rows.append(dict(zip(SOURCE_COLUMNS, cells)))
    return rows


def _command_exclusions(args):
    work = adjudicate.Work(args.work)
    index = exclusion_index(args.data_dir)
    rows = sorted((kind, value, origin) for (kind, value), origin in index.items())
    adjudicate.write_rows(shared_path(work, "exclusions.tsv"), ("kind", "value", "origin"), rows)
    print("%d entries (%d ids, %d urls) written to exclusions.tsv" % (len(rows), sum(r[0] == "id" for r in rows), sum(r[0] == "url" for r in rows)))
    rejected = 0
    for value in args.check or ():
        reason = document_rejection(index, value, value if value.startswith("http") else "")
        print("%s: %s" % ("REJECT" if reason else "ACCEPT", value) + ((" (" + reason + ")") if reason else ""))
        rejected += reason is not None
    for row in read_draft(work):
        reason = document_rejection(index, row["source_id"], row["url"])
        if reason:
            print("REJECT draft document %s: %s" % (row["source_id"], reason))
            rejected += 1
    return 1 if rejected else 0


# ---------------------------------------------------------------- inputs: text, authored sentences, tokens

def read_documents(work):
    """{source_id: [(line number, sentence)]} of W/text/*.txt (one sentence per line; empty lines skipped)."""
    folder = shared_path(work, "text")
    documents = {}
    if os.path.isdir(folder):
        for name in sorted(os.listdir(folder)):
            if name.endswith(".txt"):
                lines = common.read_lines(os.path.join(folder, name))
                documents[name[:-4]] = [(number, normalize_text(line)) for number, line in enumerate(lines, 1) if line.strip()]
    return documents


def read_authored(work):
    """[(shape, lemma, role, sentence)] of W/authored/<shape>.tsv in shape and file order."""
    folder = shared_path(work, "authored")
    found = []
    for shape in SHAPES:
        path = os.path.join(folder, shape + ".tsv")
        if not os.path.exists(path):
            continue
        for number, row in enumerate(read_plain_tsv(path, ("lemma", "role", "sentence")), 1):
            if row["role"] not in ROLES or not row["lemma"].strip() or not row["sentence"].strip():
                raise ValueError("%s: row %d needs a lemma, a role of %s and a sentence" % (path, number, "|".join(ROLES)))
            found.append((shape, row["lemma"].strip().lower(), row["role"], normalize_text(row["sentence"])))
    return found


def read_tokens(work):
    """{sentence text: (sentence count, [tokens])} of W/tokens.tsv (empty when `tokenize` has not run)."""
    path = shared_path(work, "tokens.tsv")
    if not os.path.exists(path):
        return {}
    _, records = adjudicate.read_records(path)
    return {r["text"]: (int(r["sentences"]), r["tokens"].split(" ") if r["tokens"] else []) for r in records}


def default_tokenizer_command():
    project = os.path.join(common.ROOT, "tools", "DemaConsulting.PartOfSpeech.Trainer")
    return ["dotnet", "run", "-c", "Release", "--project", project, "--"]


def run_tokenizer(texts, command):
    """{text: (sentence count, [tokens])}: shells out to `command shapes-tokenize --in FILE --out FILE` (below normal priority)."""
    import tempfile
    result = {}
    with tempfile.TemporaryDirectory() as folder:
        source, target = os.path.join(folder, "in.txt"), os.path.join(folder, "out.tsv")
        common.write_text(source, texts)
        flags = getattr(subprocess, "BELOW_NORMAL_PRIORITY_CLASS", 0)
        completed = subprocess.run(list(command) + ["shapes-tokenize", "--in", source, "--out", target], creationflags=flags, timeout=1800)
        if completed.returncode != 0:
            raise RuntimeError("the tokenizer command failed with exit code %d" % completed.returncode)
        lines = common.read_lines(target)
    if len(lines) != len(texts):
        raise RuntimeError("the tokenizer wrote %d lines for %d sentences" % (len(lines), len(texts)))
    for text, line in zip(texts, lines):
        cells = line.split("\t")
        if len(cells) != 3 or cells[0] != text:
            raise RuntimeError("the tokenizer output does not match its input at %r" % text[:40])
        result[text] = (int(cells[1]), cells[2].split(" ") if cells[2] else [])
    return result


def _command_tokenize(args):
    work = adjudicate.Work(args.work)
    texts = set()
    drafted = {row["source_id"] for row in read_draft(work)}
    for source_id, sentences in read_documents(work).items():
        if source_id in drafted:
            texts.update(text for _, text in sentences)
    texts.update(sentence for _, _, _, sentence in read_authored(work))
    known = read_tokens(work)
    missing = sorted(texts - set(known))
    if missing:
        command = [part.strip('"') for part in shlex.split(args.cmd, posix=os.name != "nt")] if args.cmd else default_tokenizer_command()
        known.update(run_tokenizer(missing, command))
    rows = [(text, count, " ".join(tokens)) for text, (count, tokens) in sorted(known.items())]
    adjudicate.write_rows(shared_path(work, "tokens.tsv"), ("text", "sentences", "tokens"), rows)
    print("%d sentences, %d newly tokenized; written to tokens.tsv" % (len(texts), len(missing)))
    return 0


# ---------------------------------------------------------------- hard tranche: Basic-model numbers and the standard tranche

class HmmTable:
    """The part-of-speech mass of the tokens of W/hmm.tsv (written by tools/data/hmm-tags): `meta` is its header, `rows` maps a token
    sequence to [(POS, p_noun, p_verb)] per token. Only these numbers of the WordSense Basic classifier are read; no PartOfSpeech model."""

    def __init__(self, meta, rows):
        self.meta = meta
        self.rows = rows

    def mass(self, tokens):
        """[(POS, p_noun, p_verb)] of a token sequence; a sequence the harness did not tag is an error (never a default)."""
        found = self.rows.get(tuple(tokens))
        if found is None:
            raise ValueError("hmm.tsv has no tags for a sentence of %d tokens (starting %r): run tools/data/hmm-tags again on tokens.tsv"
                             % (len(tokens), " ".join(tokens[:6])))
        return found

    def q(self, tokens, index, word_class):
        """The mass of `word_class` (`Noun` or `Verb`) of the token at `index`."""
        entry = self.mass(tokens)[index]
        return entry[1] if word_class == "Noun" else entry[2]


def parse_hmm_cell(cell):
    """(form, POS, p_noun, p_verb) of a `form|POS|pNoun|pVerb` cell, split from the right (a form may contain `|`)."""
    parts = cell.rsplit("|", 3)
    if len(parts) != 4 or not parts[0] or not parts[1]:
        raise ValueError("a malformed hmm.tsv cell: %r" % cell[:40])
    return parts[0], parts[1], float(parts[2]), float(parts[3])


def read_hmm(work):
    """The HmmTable of W/hmm.tsv. The hard rule is pre-registered on one classifier and one input, so the header is checked, not just read:

    - `# hmm-tags 1` magic; `wordsense_commit` == HARD_WORDSENSE_COMMIT (6530de1928baf30f9a14446a54bfb5dd5f67b41c) and
      `wordsense_dll_sha256` == HARD_WORDSENSE_DLL_SHA256 (b96e0055664896983e311f626d41e5251a2dcf1a236d86f6ec50beaae6a746bf): a different
      classifier build can give a different tranche and is refused (there is no override);
    - `input_sha256` == SHA-256 of the bytes of the current W/tokens.tsv, which is exactly what tools/data/hmm-tags hashes (its --in file). The
      check is exact on purpose: when tokenize adds rows to tokens.tsv, hmm-tags has to run again on the new file, so the tags and the tokens
      the selector reads are always of one input;
    - `sentences` == the number of lines of the file == the number of single-sentence rows with tokens in tokens.tsv (what hmm-tags tags)."""
    path = shared_path(work, HMM_FILE)
    if not os.path.exists(path):
        raise ValueError("%s is missing: run tools/data/hmm-tags on tokens.tsv (see its README)" % path)
    meta, rows = {}, {}
    lines = common.read_lines(path)
    if not lines or not lines[0].startswith(HMM_MAGIC):
        raise ValueError("%s: not a `%s` file" % (path, HMM_MAGIC))
    count = 0
    for line in lines:
        if line.startswith("# "):                 # a cell never starts with `# `: a token has no space
            if " = " in line:
                name, _, value = line[2:].partition(" = ")
                meta[name] = value
        elif line:
            cells = [parse_hmm_cell(cell) for cell in line.split("\t")]
            rows[tuple(c[0] for c in cells)] = [(c[1], c[2], c[3]) for c in cells]
            count += 1
    for needed in ("wordsense_commit", "wordsense_dll_sha256", "input_sha256", "sentences"):
        if needed not in meta:
            raise ValueError("%s: the header has no %s" % (path, needed))
    if meta["sentences"] != str(count):
        raise ValueError("%s: the header says %s sentences, the file has %d" % (path, meta["sentences"], count))
    for name, pinned in (("wordsense_commit", HARD_WORDSENSE_COMMIT), ("wordsense_dll_sha256", HARD_WORDSENSE_DLL_SHA256)):
        if meta[name] != pinned:
            raise ValueError("%s: %s is %s, the hard tranche is pre-registered on %s: run tools/data/hmm-tags with the pinned WordSense build"
                             % (path, name, meta[name], pinned))
    tokens_path = shared_path(work, "tokens.tsv")
    if not os.path.exists(tokens_path):
        raise ValueError("%s is missing: hmm.tsv cannot be checked against its input" % tokens_path)
    with open(tokens_path, "rb") as handle:
        raw = handle.read()
    digest = hashlib.sha256(raw).hexdigest()
    if meta["input_sha256"] != digest:
        raise ValueError("%s: input_sha256 %s is not the SHA-256 of the current tokens.tsv (%s): run tools/data/hmm-tags again on tokens.tsv"
                         % (path, meta["input_sha256"], digest))
    tokenized = tagged_sentence_count(raw)
    if tokenized != count:
        raise ValueError("%s: %d sentences, tokens.tsv has %d single-sentence rows with tokens" % (path, count, tokenized))
    return HmmTable(meta, rows)


def tagged_sentence_count(raw):
    """The number of sentences tools/data/hmm-tags tags from the bytes of a tokens.tsv: rows with a sentence count of 1 and at least one token."""
    lines = re.split(r"\r\n|\r|\n", raw.decode("utf-8"))
    if lines and lines[-1] == "":
        lines.pop()                                  # File.ReadAllLines has no empty last line
    if not lines or lines[0] != "\t".join(("text", "sentences", "tokens")):
        raise ValueError("tokens.tsv: not a tokens table (header `text<TAB>sentences<TAB>tokens` expected)")
    count = 0
    for line in lines[1:]:
        cells = line.split("\t")
        if len(cells) == 3 and cells[1] == "1" and cells[2].replace(" ", ""):
            count += 1
    return count


def alnum_words(text):
    """Lower-case alphanumeric words of a text (the units of the word 6-grams of the standard-tranche near-duplicate rule)."""
    return re.findall(r"[a-z0-9]+", common.nfc(text).lower())


class StandardTranche:
    """The sentences of the committed standard tranche, to keep the hard tranche disjoint from it: sentence keys (any document) and, per
    document, the word 6-grams of its standard sentences (a near duplicate shares most of them)."""

    def __init__(self, data_dir):
        path = os.path.join(data_dir, "eval", "shapes-dev", TRANCHE_FILES["standard"][0])
        if not os.path.exists(path):
            raise ValueError("the hard tranche needs the committed standard tranche to stay disjoint from it: missing %s" % path)
        self.keys = set()
        self.grams = collections.defaultdict(set)
        for sentence in common.read_corpus(path)[1]:
            self.keys.add(common.key(sentence["text"]))
            self.grams[sentence["source_id"]] |= self.word_grams(sentence["text"])
        self.keys.discard("")

    @staticmethod
    def word_grams(text):
        words = alnum_words(text)
        return {" ".join(words[i:i + NEAR_STANDARD_GRAM]) for i in range(len(words) - NEAR_STANDARD_GRAM + 1)}

    def reason(self, source_id, text):
        """`equals-standard-tranche`, `near-standard-tranche` (the same document, at least NEAR_STANDARD_WORDS words, at least NEAR_STANDARD_SHARE
        of the word 6-grams in its standard sentences) or None."""
        if common.key(text) in self.keys:
            return EQUALS_STANDARD
        grams = self.word_grams(text)
        if len(alnum_words(text)) >= NEAR_STANDARD_WORDS and grams and len(grams & self.grams.get(source_id, set())) / len(grams) >= NEAR_STANDARD_SHARE:
            return NEAR_STANDARD
        return None


class HardRule:
    """What `collect_candidates` needs of the hard profile: the Basic numbers, the standard tranche, the budgets (None = all)."""

    def __init__(self, hmm, standard, budgets=None):
        self.hmm = hmm
        self.standard = standard
        self.budgets = dict(HARD_BUDGETS, **(budgets or {}))


def in_band(q):
    return BAND_MIN <= q < BAND_MAX


def format_q(q):
    return "%.3f" % q


def parse_budget(values):
    """{shape: N or None} of repeated `--budget SHAPE=N|all` options (None = every eligible candidate)."""
    result = {}
    for value in values or ():
        shape, _, number = value.partition("=")
        if shape not in HARD_SHAPES or not number or (number != "all" and not number.isdigit()) or number == "0":
            raise ValueError("--budget needs SHAPE=N (N at least 1, or all) with a shape of %s: %r" % (", ".join(HARD_SHAPES), value))
        result[shape] = None if number == "all" else int(number)
    return result


# ---------------------------------------------------------------- sentence cleanliness (candidate stage)

# Rules of the cleanliness filter (added after the first labels were seen; they only remove items: they drop OCR and extraction noise, titles, fragments and list items before any
# shape rule or labeler sees a sentence). The first rule that fires names the drop reason (`noise-<rule>`).
NOISE_RULES = ("start-end", "chars", "junk", "brackets", "enumeration", "citation", "repeat", "caps", "title", "short", "no-verb", "oov")
ALLOWED_CHARS = re.compile(r"[A-Za-z0-9 .,;:'\"()?!$%&/#+=\-]*")            # printable ASCII without `* [ ] { } < > @ ^ _ ~ | \ `` and controls
JUNK_PUNCT = ("'/", "!/", ":'", "./", "/.", ",,", "..", ";;", "::", " ,", " ;", " :", "( ", " )", "--", "/ ", " /")
ENUMERATION = re.compile(r"^(?:\(?[A-Za-z0-9]{1,3}\)|[0-9]{1,3}[.)]|[A-Za-z]\.|[IVXivx]{2,4}\.)\s")
HYPHEN_JUNK = re.compile(r"(?:(?<=\s)-\S|\S-(?=\s))")                         # ` -nitrogen`, `Husband- Callers` (a broken word)
INNER_LABEL = re.compile(r"(?:^|\s)\((?:[a-z]|\d{1,2}|[ivx]{2,4})\)(?:\s|$)|\s\d{1,2}\.\s+[a-z]|\s\d{1,3}\.$")
INITIALS = re.compile(r"(?<![A-Za-z.])[A-Z]\.\s")
END_RE = re.compile(r"[.!?][\"')]?$")
TITLE_SHARE = 0.5
TITLE_MIN_WORDS = 4
MIN_WORDS = 5


def vocabulary_of(data_dir, lex=None):
    """The spelling list of the OOV rule: every lower-cased word form in open/train.tsv and masc/masc.tsv plus the lexicon spellings.

    The two training sets are read only as a word list (never as sentences to select from).
    """
    vocab = set(lex if lex is not None else lexicon_of(data_dir))
    for parts in (("open", "train.tsv"), ("masc", "masc.tsv")):
        path = os.path.join(data_dir, *parts)
        if os.path.exists(path):
            for sentence in common.read_corpus(path)[1]:
                vocab.update(row[0].lower() for row in sentence["rows"])
    return vocab


def is_alpha_word(token):
    """A token of letters only, possibly with inner hyphens or apostrophes (`water-tight`, `n't` stripped of its apostrophe)."""
    return re.fullmatch(r"[A-Za-z]+(?:['-][A-Za-z]+)*", token.lstrip("'")) is not None


def oov_tokens(tokens, vocab):
    """The tokens that make a sentence suspect: alphabetic, spelled lower case in no word list.

    Skipped: tokens with a digit or other non-letter, capitalized tokens that are not sentence initial (names, acronyms), and
    abbreviations that end with a period. A hyphenated token is fine when it, or every part of it, is a known word.
    """
    found = []
    seen_word = False
    for token in tokens:
        first, seen_word = not seen_word, seen_word or is_word(token)
        if not is_alpha_word(token):
            continue
        if token[:1].isupper() and not first:
            continue
        low = token.lower().lstrip("'")
        if low in vocab or all(part in vocab for part in re.split(r"[-']", low) if part):
            continue
        found.append(token)
    return found


def clause_evidence(tokens, lex):
    """A short sentence is a clause, not a caption: an auxiliary, a word listed only as a verb, or an imperative start (a word
    listed as a verb followed by a determiner, a pronoun or a preposition)."""
    for i, token in enumerate(tokens):
        low = token.lower()
        if low in AUXILIARIES or (has_tag(lex, low, "VERB") and not has_tag(lex, low, "NOUN") and not has_tag(lex, low, "ADJ")):
            return True
    if len(tokens) >= 2 and has_tag(lex, tokens[0], "VERB"):
        following = tokens[1].lower()
        return following in DETERMINERS or following in PREPOSITIONS or following in OBJECT_PRONOUNS
    return False


def noise_reason(text, tokens, lex, vocab):
    """The rule of NOISE_RULES (a string) that rejects a real sentence as noisy, or None when it is clean.

    start-end    does not start with an upper-case letter, or does not end with `.`, `!` or `?` (a closing quote or bracket may follow)
    chars        a character outside ASCII letters, digits, space and `. , ; : ' " ( ) ? ! $ % & / # + = -` (so also non-ASCII text,
                 replacement characters and the degree sign: ASCII only keeps the OCR damage out; en and em dashes are dropped too)
    junk         slash-punctuation junk (`'/`, `!/`, `:'`, `./`, `/.`), doubled or space-separated punctuation, a broken hyphen word
                 (` -x`, `x- `), an apostrophe that does not follow a letter or digit, a lone lower-case letter other than `a`
    brackets     unbalanced brackets or an odd number of double quotes
    enumeration  starts as a list label (`(a)`, `1.`, `a.`, `ii.`) or holds an inner label such as ` (a) `, ` (2) `, ` (iv) `, ` 2. x`,
                 or ends in a number and a period (a list item or a page number)
    citation     an initial such as `M. ` (a reference list entry or a name line)
    repeat       the same word twice in a row, or one token trigram three times
    caps         at least three all-capital words that are more than half of the words
    title        at least TITLE_MIN_WORDS words and more than TITLE_SHARE of them capitalized
    short        fewer than MIN_WORDS alphabetic words and no clause evidence (see `clause_evidence`: a caption or a fragment)
    no-verb      none of the tokens is listed as a verb or an auxiliary in the lexicon (a title or caption)
    oov          a lower-case alphabetic token in no word list (see `oov_tokens`)
    """
    if not text[:1].isupper() or not END_RE.search(text):
        return "start-end"
    if not ALLOWED_CHARS.fullmatch(text):
        return "chars"
    if (any(junk in text for junk in JUNK_PUNCT) or re.search(r"(?<![A-Za-z0-9])'", text) or HYPHEN_JUNK.search(text)
            or any(len(t) == 1 and t.islower() and t != "a" and t.isalpha() for t in tokens)):
        return "junk"
    depth = 0
    for ch in text:
        depth += (ch == "(") - (ch == ")")
        if depth < 0:
            break
    if depth != 0 or text.count('"') % 2:
        return "brackets"
    if ENUMERATION.match(text) or INNER_LABEL.search(text):
        return "enumeration"
    if INITIALS.search(text):
        return "citation"
    lows = [t.lower() for t in tokens]
    if any(a == b and is_alpha_word(a) for a, b in zip(lows, lows[1:])):
        return "repeat"
    if any(count >= 3 for count in collections.Counter(zip(lows, lows[1:], lows[2:])).values()):
        return "repeat"
    alpha = [t for t in tokens if is_alpha_word(t)]
    upper = [t for t in alpha if len(t) >= 2 and t.isupper()]
    if len(upper) >= 3 and 2 * len(upper) > len(alpha):
        return "caps"
    if len(alpha) >= TITLE_MIN_WORDS and sum(t[:1].isupper() for t in alpha) > TITLE_SHARE * len(alpha):
        return "title"
    if len(alpha) < MIN_WORDS and not clause_evidence(tokens, lex):
        return "short"
    if not any(has_tag(lex, t, "VERB") or has_tag(lex, t, "AUX") for t in alpha):
        return "no-verb"
    if oov_tokens(tokens, vocab):
        return "oov"
    return None


# ---------------------------------------------------------------- lexicon and shape heuristics

def lexicon_of(data_dir):
    return common.read_lexicon(os.path.join(data_dir, "lexicon", "lexicon.tsv"))


def has_tag(lex, token, tag):
    return tag in lex.get(token.lower(), ())


def is_noun_verb(lex, token):
    """The spelling is listed with both NOUN and VERB in the lexicon (the words the shapes are about)."""
    tags = lex.get(token.lower())
    return bool(tags) and "NOUN" in tags and "VERB" in tags


def is_target_word(lex, token):
    """A word the shapes can be about: listed with both NOUN and VERB, and no function word (`a`, `must`, `will`, `no` are listed so)."""
    return is_noun_verb(lex, token) and token.lower() not in FUNCTION_WORDS and is_word(token)


def is_word(token):
    return common.has_letter_or_digit(token)


def lemma_of(token, lex):
    """The lower-case lemma of a token for the lemma cap: the plural or third person form is reduced to a listed noun-verb base."""
    low = token.lower()
    if low.endswith("ss") or (is_noun_verb(lex, low) and not low.endswith("s")):
        return low
    for suffix, replacement in (("ies", "y"), ("es", ""), ("s", "")):
        if low.endswith(suffix) and len(low) > len(suffix) + 1:
            stem = low[:-len(suffix)] + replacement
            if is_noun_verb(lex, stem):
                return stem
    return low


def base_form_of(token, lex):
    """The lower-case base form of a token: `lemma_of` (plural and third person to a listed noun-verb base), and an `-ing` or `-ed`
    form reduced to a stem the lexicon lists as a VERB (`Operating` -> operate, `stopped` -> stop, `tried` -> try); a word that is
    none of these is its own base form (`Wring`, `Bring`, `Ring`: the stem `wr`, `br`, `r` is no word; `Process`, `Pass`, `Focus`).
    A stem has at least two letters; a past form of a verb in `-ee` (`agreed`) is not recognized, so that `feed`, `need`, `seed` are bases.
    Irregular forms (`ran`, `wrote`) have no lemma in the lexicon and count as base forms."""
    low = token.lower()
    lemma = lemma_of(token, lex)
    if lemma != low:
        return lemma
    stems = []
    if low.endswith("ing") and len(low) > 4:
        stem = low[:-3]
        stems = [stem, stem + "e"] + ([stem[:-1]] if stem[-1] == stem[-2] else [])
    elif low.endswith("ed") and len(low) > 3:
        stem = low[:-2]
        stems = [stem] + ([stem + "e"] if not stem.endswith("e") else []) + ([stem[:-1]] if stem[-1] == stem[-2] else [])
        stems += [stem[:-1] + "y"] if stem.endswith("i") else []
    for stem in stems:
        if len(stem) >= 2 and has_tag(lex, stem, "VERB"):
            return stem
    return low


def is_quantity(token):
    """A number written with digits (a fraction, a decimal) or a number word of NUMBER_WORDS or `half`."""
    low = token.lower()
    return common.is_number(token) or low in NUMBER_WORDS or low in QUANTITY_WORDS


def noun_capable(lex, token):
    """The token may be a noun or a name: listed as a noun in the lexicon, or capitalized (a proper-looking word), and a real word."""
    return is_word(token) and (has_tag(lex, token, "NOUN") or has_tag(lex, token, "PROPN") or token[:1].isupper())


def measure_frame(tokens, i, lex):
    """True when the word at `i` stands in a quantity frame: right after a number or number word, and not itself a modifier.

    Required: a digit number, `half` or a number word (not `several`) right before the word, with no determiner before the number
    (`The four functions`, `these five forces` are counted nouns of a definite phrase, no measure); a lower-case word that is a unit
    or quantity noun (UNITS) or is followed by one of QUANTITY_OF (`2 sets of`, `3 per`, `4 feet long`; `one` only counts before a
    unit, since `one end of` is no measure). Not a measure: a label before the number (`No. 2`,
    `Type 3`, `Section 4`), a word that a following noun-capable word (or adjective plus noun-capable word) would continue as a
    compound (`two machine guns`, `1860 forest planting`).
    """
    token = tokens[i]
    if i < 1 or not is_quantity(tokens[i - 1]) or token != token.lower():
        return False
    if i >= 2 and (tokens[i - 2].lower() in LABEL_WORDS or tokens[i - 2].lower() in DETERMINERS):
        return False
    nxt = tokens[i + 1] if i + 1 < len(tokens) else ""
    if lemma_of(token, lex) not in UNITS and (nxt.lower() not in QUANTITY_OF or tokens[i - 1].lower() == "one"):
        return False
    if nxt:
        if noun_capable(lex, nxt):
            return False
        if has_tag(lex, nxt, "ADJ") and i + 2 < len(tokens) and noun_capable(lex, tokens[i + 2]):
            return False
    return True


def follows_like_verb(lex, token):
    """What may stand right after the head of a compound or a subject: the end, a mark, an auxiliary, a preposition, a conjunction or a verb."""
    low = token.lower()
    return (not is_word(token) or low in AUXILIARIES or low in PREPOSITIONS or low in CONJUNCTIONS or low == "to"
            or has_tag(lex, low, "VERB") or has_tag(lex, low, "AUX"))


def noun_like_modifier(lex, token):
    """True when `token` can be the noun modifier of a compound (the `--compound-strict` rule on the token before the head).

    Lexicon only: a NOUN reading; no reading in MODIFIER_BLOCKING_TAGS (adjective, adverb, determiner, pronoun, number, preposition,
    auxiliary or modal, particle, conjunction, interjection); not a digit number, a number word or a function word; a word that ends in
    `-ing` or `-ed` only when it is listed as a noun (NOUN, PROPN) and nothing else (a participle modifies like an adjective).
    """
    low = token.lower()
    tags = lex.get(low, frozenset())
    if "NOUN" not in tags or tags & MODIFIER_BLOCKING_TAGS or common.is_number(token) or low in NUMBER_WORDS or low in FUNCTION_WORDS:
        return False
    return not (low.endswith(("ing", "ed")) and not tags <= NOUN_ONLY_TAGS)


def shape_hits(tokens, lex, compound_strict=False):
    """[(shape, index)]: where the tokens match the position and lexicon pattern of a shape (at most one index per shape).

    The patterns only pre-select candidates (plan 2.4); the class of the judged word is decided by the two labelers.
    `compound_strict` adds the `noun_like_modifier` test on the token before the head of an n-compound-head hit.
    """
    hits = {}
    n = len(tokens)
    for i, token in enumerate(tokens):
        low = token.lower()
        if not is_target_word(lex, low):
            continue
        prev = tokens[i - 1] if i >= 1 else None
        nxt = tokens[i + 1] if i + 1 < n else None
        plow = prev.lower() if prev is not None else ""
        nlow = nxt.lower() if nxt is not None else ""
        if i == 0 and token[:1].isupper() and low not in DETERMINERS | PREPOSITIONS | AUXILIARIES and nxt is not None:
            if nlow in AUXILIARIES or (has_tag(lex, nlow, "VERB") and nlow not in DETERMINERS | PREPOSITIONS):
                hits.setdefault("n-subject-initial", i)
            if (nlow in DETERMINERS or nlow in PREPOSITIONS or nlow in OBJECT_PRONOUNS or nlow in FOLLOWING_ADVERBS
                    or is_quantity(nxt)) and nlow not in AUXILIARIES and lemma_of(token, lex) == low:
                hits.setdefault("v-imperative-initial", i)
        if measure_frame(tokens, i, lex):
            hits.setdefault("n-measure", i)
        if i >= 1 and (plow in MODALS or plow == "to") and lemma_of(token, lex) == low:
            hits.setdefault("v-after-modal", i)
        if (i >= 1 and has_tag(lex, plow, "NOUN") and is_word(prev) and plow not in DETERMINERS | PREPOSITIONS | AUXILIARIES | OBJECT_PRONOUNS
                and (nxt is None or follows_like_verb(lex, nxt)) and (not compound_strict or noun_like_modifier(lex, prev))):
            before = tokens[i - 2].lower() if i >= 2 else None
            if before is not None and (before in DETERMINERS or before in PREPOSITIONS or before in CONJUNCTIONS
                                       or before in AUXILIARIES or not is_word(tokens[i - 2])):
                hits.setdefault("n-compound-head", i)
    return sorted(((shape, index) for shape, index in hits.items()), key=lambda item: SHAPES.index(item[0]))


def control_hits(tokens, lex, shape, lemmas):
    """[index] where a lemma of `lemmas` stands in the class other than the shape's class (a pre-selection like `shape_hits`).

    For a noun shape the lemma is looked for after a modal or `to`, or as the first word of an instruction; for a verb shape
    after a determiner.
    """
    found = []
    for i, token in enumerate(tokens):
        if lemma_of(token, lex) not in lemmas or not is_target_word(lex, token):
            continue
        prev = tokens[i - 1].lower() if i >= 1 else ""
        nxt = tokens[i + 1].lower() if i + 1 < len(tokens) else ""
        if SHAPE_CLASS[shape] == "Noun":
            if prev in MODALS or prev == "to" or (i == 0 and token[:1].isupper() and (nxt in DETERMINERS or nxt in OBJECT_PRONOUNS)):
                found.append(i)
        elif prev in DETERMINERS and nxt != "":
            found.append(i)
    return found


# ---------------------------------------------------------------- candidates

class LeakageFilter:
    """The exclusion filter of the candidates: sentences that equal a training or evaluation sentence, or share a word 5-gram with the probe.

    The held-out sets (the shape probe included) are seen only as the opaque hash set `leakage.held_out_overlap_keys()`; the filter
    removes exact and near-duplicate overlaps and never chooses, weights or frames a candidate.
    """

    def __init__(self, data_dir, include_ud=True):
        self.held_out = leakage.held_out_overlap_keys(include_ud)
        self.train = set()
        trains = {}
        for label, parts in (("open-train", ("open", "train.tsv")), ("masc", ("masc", "masc.tsv")), ("silver", ("silver", "silver.tsv"))):
            path = os.path.join(data_dir, *parts)
            if os.path.exists(path):
                trains[label] = [(s["source_id"], s["text"]) for s in common.read_corpus(path)[1]]
        leakage.require_sets(trains, ("open-train", "masc", "silver"), "training")
        for items in trains.values():
            self.train |= {common.key(text) for _, text in items} - {""}

    def key_hit(self, text):
        return common.key(text) in self.train or leakage.key_overlap(text, self.held_out)

    def probe_hit(self, text):
        return leakage.gram_overlap(text, self.held_out, leakage.PROBE_GRAM_WORDS)


def lemma_counts_of(works, shapes=SHAPES, data_dir=None):
    """Counter {(shape, lemma): n} of the targets of earlier work areas: the kept targets when decided.tsv exists, else the candidates.

    Sources of the merged exclusions (`merged_excluded_sources`: the committed record and the work file) do not count."""
    counts = collections.Counter()
    for earlier in works:
        excluded = merged_excluded_sources(earlier, data_dir)
        if os.path.exists(earlier.private_file("decided.tsv")):
            items, _ = read_decided(earlier, data_dir)
            members = [i for i in items if i["outcome"] == "keep"]
        elif os.path.exists(work_path(earlier, "candidates.tsv")):
            members = [i for i in read_candidates(earlier) if i["source_id"] not in excluded]
        else:
            continue
        counts.update((i["shape"], i["lemma"]) for i in members if i["role"] == "target" and i["shape"] in shapes)
    return counts


def collect_candidates(work, data_dir, include_ud=True, target=TARGET_PER_SHAPE, over=OVER_COLLECT, lemma_cap=LEMMA_CAP,
                       seed=SEED, tokens_override=None, filt=None, shape_over=None, control_pool=CONTROL_POOL, clean=True, vocab=None,
                       shape_lemma_cap=None, only_shapes=None, controls=True, compound_strict=False, exclude_keys=None,
                       exclude_lines=None, lemma_existing=None, prefix="", hard=None, control_room=None):
    """(candidate rows, report lines): the candidates of every shape from the real text and the authored sentences.

    Real sentences are scanned in document and line order; a sentence that is too short or too long, noisy (`noise_reason`, when
    `clean`), a duplicate, equal to a training or evaluation sentence, or sharing a word 5-gram with the probe is dropped, and a
    document with DOCUMENT_KEY_HITS sentences that are also in a training or evaluation set is rejected as a whole (a mirror or a
    reprint). Per shape at most ceil(target * factor) targets are taken (`over`, or the factor of `shape_over[shape]`) and, per lemma,
    `lemma_cap`; controls: `control_pool` per shape from the lemmas of the shape's targets, at most CONTROL_LEMMA_CAP per lemma.

    Rounds: `only_shapes` limits the shapes; `controls=False` writes no controls; `compound_strict` applies `noun_like_modifier`;
    `exclude_keys` (sentence keys) and `exclude_lines` ((source_id, line)) are sentences of earlier work areas that are dropped
    (`already-in-earlier-round`); `lemma_existing` {(shape, lemma): n} are the kept targets of earlier work areas: a lemma at the cap
    gets no candidate and a lemma below it comes after the new lemmas; `prefix` (`r3-`) starts every candidate id.

    Hard tranche (`hard` is a HardRule; see "Hard tranche" in the module docstring): the four HARD_SHAPES unless `only_shapes` says
    otherwise, n-compound-head always strict, a standard-tranche sentence is dropped (`equals-standard-tranche`, `near-standard-tranche`),
    a target is eligible only when the Basic model mass q of the shape's class at the target is in [BAND_MIN, BAND_MAX), the eligible
    targets are ordered by q ascending (ties by the seeded rank), at most `lemma_cap` per lemma and the budget of the shape (None = all);
    every candidate row carries q (3 decimals; a control has none); the authored sentences are not used. Standard-profile behavior and
    output are untouched when `hard` is None.

    Controls-only round (hard tranche; `control_room` = {shape: {lemma: n}}, see `build_control_room`): no target candidate is written;
    the controls come from the lemmas of `control_room` of the shape, at most n per lemma (n = the controls that the build still takes for
    the lemma), `control_pool` per shape, by the seeded rank. The target selection of the build cannot change: no target is added.
    """
    shapes = tuple(only_shapes) if only_shapes else (HARD_SHAPES if hard else SHAPES)
    compound_strict = compound_strict or hard is not None
    exclude_keys = exclude_keys or frozenset()
    exclude_lines = exclude_lines or frozenset()
    lemma_existing = lemma_existing or {}
    shape_over = shape_over or {}
    shape_lemma_cap = shape_lemma_cap or {}
    tokens_of = tokens_override if tokens_override is not None else read_tokens(work)
    lex = lexicon_of(data_dir)
    if clean and vocab is None:
        vocab = vocabulary_of(data_dir, lex)
    filt = filt or LeakageFilter(data_dir, include_ud)
    draft = {row["source_id"]: row for row in read_draft(work)}
    index = exclusion_index(data_dir)
    documents = read_documents(work)
    excluded = read_excluded_sources(work)
    drops = collections.Counter()
    rejected = {}
    problems = []
    seen_keys = set()
    pools = {shape: [] for shape in SHAPES}          # (rank, row dict) of the targets
    all_sentences = []                               # (source_id, line, text, tokens) of the accepted real sentences
    considered = 0
    for source_id in sorted(documents):
        if source_id not in draft:
            problems.append("text/%s.txt has no row in sources-draft.tsv" % source_id)
            continue
        if source_id in excluded:
            drops["excluded-source"] += len(documents[source_id])
            considered += len(documents[source_id])
            continue
        reason = document_rejection(index, source_id, draft[source_id]["url"])
        if reason:
            rejected[source_id] = reason
            continue
        hits = sum(filt.key_hit(text) for _, text in documents[source_id])
        if hits >= DOCUMENT_KEY_HITS:
            rejected[source_id] = "%d sentences are also in a training or evaluation set" % hits
            continue
        for number, text in documents[source_id]:
            considered += 1
            if text not in tokens_of or tokens_of[text][0] != 1:
                drops["not-one-sentence-or-not-tokenized"] += 1
                continue
            toks = tokens_of[text][1]
            key = sent_key_of(text)
            noise = noise_reason(text, toks, lex, vocab) if clean and MIN_TOKENS <= len(toks) <= MAX_TOKENS else None
            standard_hit = hard.standard.reason(source_id, text) if hard is not None else None
            if not MIN_TOKENS <= len(toks) <= MAX_TOKENS:
                drops["length"] += 1
            elif key in exclude_keys or (source_id, number) in exclude_lines:
                drops["already-in-earlier-round"] += 1
            elif noise:
                drops["noise-" + noise] += 1
            elif key in seen_keys:
                drops["duplicate"] += 1
            elif standard_hit:
                drops[standard_hit] += 1
            elif filt.key_hit(text):
                drops["equals-a-set-sentence"] += 1
            elif filt.probe_hit(text):
                drops["probe-5gram"] += 1
            else:
                if hard is not None:
                    hard.hmm.mass(toks)                      # a sentence the harness did not tag is an error
                seen_keys.add(key)
                all_sentences.append((source_id, number, text, toks))
    band_sizes = {shape: 0 for shape in shapes}
    frame_sizes = {shape: 0 for shape in shapes}
    for source_id, number, text, toks in (all_sentences if control_room is None else ()):
        for shape, i in shape_hits(toks, lex, compound_strict):
            if shape not in shapes:
                continue
            row = dict(shape=shape, role="target", target=i, lemma=lemma_of(toks[i], lex), origin="real", source_id=source_id, line=number,
                       text=text, tokens=toks)
            frame_sizes[shape] += 1
            if hard is not None:
                q = hard.hmm.q(toks, i, SHAPE_CLASS[shape])
                if not in_band(q):
                    continue
                band_sizes[shape] += 1
                row["q"] = format_q(q)
            pools[shape].append((rank_of(seed, "hard", shape, sent_key_of(text)) if hard is not None else rank_of(seed, shape, sent_key_of(text)),
                                 row))
    rows = []
    if control_room is not None:
        limits = {shape: 0 for shape in shapes}
    elif hard is not None:
        limits = {shape: hard.budgets[shape] for shape in shapes}
    else:
        limits = {shape: int(math.ceil(target * shape_over.get(shape, over))) for shape in shapes}
    pool_sizes = {shape: len(pools[shape]) for shape in shapes}
    chosen_lemmas = {shape: set() for shape in SHAPES}
    for shape in shapes:
        counts = collections.Counter()
        cap = shape_lemma_cap.get(shape, lemma_cap)
        if hard is not None:      # hardest first: q ascending, ties by the seeded rank
            ranked = sorted(pools[shape], key=lambda item: (float(item[1]["q"]), item[0]))
        else:
            ranked = sorted(pools[shape], key=lambda item: (preference(shape, item[1], lex), lemma_existing.get((shape, item[1]["lemma"]), 0) > 0,
                                                            item[0]))
        for _, row in ranked:
            if limits[shape] is not None and sum(counts.values()) >= limits[shape]:
                break
            if lemma_existing.get((shape, row["lemma"]), 0) >= cap:
                continue
            if counts[row["lemma"]] < cap:
                counts[row["lemma"]] += 1
                rows.append(row)
        chosen_lemmas[shape] = set(counts)
    if control_room is not None:
        chosen_lemmas = {shape: {lemma for lemma, room in control_room.get(shape, {}).items() if room > 0} for shape in SHAPES}

    control_rows = {shape: [] for shape in shapes}
    for source_id, number, text, toks in (all_sentences if controls else ()):
        for shape in shapes:
            for i in control_hits(toks, lex, shape, chosen_lemmas[shape]):
                control_rows[shape].append((rank_of(seed, "control", shape, sent_key_of(text)),
                                            dict(shape=shape, role="control", target=i, lemma=lemma_of(toks[i], lex), origin="real",
                                                 source_id=source_id, line=number, text=text, tokens=toks, **({"q": ""} if hard is not None else {}))))
    for shape in shapes:
        counts = collections.Counter()
        for _, row in sorted(control_rows[shape], key=lambda item: item[0]):
            if sum(counts.values()) >= control_pool:
                break
            if counts[row["lemma"]] < (CONTROL_LEMMA_CAP if control_room is None else min(CONTROL_LEMMA_CAP, control_room[shape][row["lemma"]])):
                counts[row["lemma"]] += 1
                rows.append(row)
    if hard is not None:
        if read_authored(work):
            problems.append("W/authored holds sentences, but the hard tranche has no authored sentences: they are ignored")
    else:
        authored_rows, authored_problems = authored_candidates(work, lex, tokens_of, filt, exclude_keys)
        authored_rows = [r for r in authored_rows if r["shape"] in shapes and (controls or r["role"] == "target")]
        problems += authored_problems
        rows += authored_rows
    rows.sort(key=lambda r: (SHAPES.index(r["shape"]), ROLES.index(r["role"]), ORIGINS.index(r["origin"]),
                             rank_of(seed, r["shape"], r["role"], sent_key_of(r["text"]))))
    for number, row in enumerate(rows, 1):
        row["cand_id"] = "%sc%05d" % (prefix, number)
    if hard is not None:
        return rows, hard_candidate_report(rows, drops, rejected, problems, limits, draft, documents, considered, len(all_sentences), frame_sizes,
                                           band_sizes, shapes, hard)
    report = candidate_report(rows, drops, rejected, problems, limits, draft, documents, considered, len(all_sentences), pool_sizes, target,
                              shapes)
    return rows, report


def authored_candidates(work, lex, tokens_of, filt, exclude_keys=frozenset()):
    """(rows, problems) of the authored sentences: the target is the first token whose lemma is the lemma of the row.

    A sentence whose key is in `exclude_keys` (already a candidate of an earlier round) is skipped without a problem.
    """
    rows, problems, seen = [], [], set()
    for shape, lemma, role, sentence in read_authored(work):
        if sentence not in tokens_of or tokens_of[sentence][0] != 1:
            problems.append("authored %s: a sentence is not tokenized as one sentence (run tokenize)" % shape)
            continue
        toks = tokens_of[sentence][1]
        key = sent_key_of(sentence)
        if key in exclude_keys:
            continue
        if not MIN_TOKENS <= len(toks) <= MAX_TOKENS or key in seen or filt.key_hit(sentence) or filt.probe_hit(sentence):
            problems.append("authored %s: sentence %s is too short or long, a duplicate, or equals or shares wording with an existing set"
                            % (shape, key))
            continue
        index = next((i for i, token in enumerate(toks) if token.lower() == lemma or lemma_of(token, lex) == lemma), None)
        if index is None:
            problems.append("authored %s: sentence %s does not contain the lemma '%s'" % (shape, key, lemma))
            continue
        seen.add(key)
        rows.append(dict(shape=shape, role=role, target=index, lemma=lemma, origin="authored", source_id=AUTHORED_ID, line=len(rows) + 1,
                         text=sentence, tokens=toks))
    return rows, problems


def preference(shape, row, lex):
    """Sort class of a target candidate before its seeded rank: a measure target whose lemma is a unit or quantity noun comes first."""
    return 0 if shape != "n-measure" or row["lemma"] in UNITS else 1


def hard_candidate_report(rows, drops, rejected, problems, limits, draft, documents, considered, clean, frame_sizes, band_sizes, shapes, hard):
    """The report lines of the hard-tranche candidates: counts only (no text of any evaluation set), the Basic-model provenance, the band."""
    lines = ["# Candidates (hard tranche)", "", "Counts only; no text of any evaluation set.", "",
             "Basic model numbers: WordSense commit %s, DLL sha256 %s, %s sentences tagged" % (
                 hard.hmm.meta["wordsense_commit"], hard.hmm.meta["wordsense_dll_sha256"], hard.hmm.meta["sentences"]),
             "contested band: %.2f <= q < %.2f (q = Basic model mass of the shape's class); q < %.1f is the pure-disagreement part"
             % (BAND_MIN, BAND_MAX, DISAGREE_Q), ""]
    lines += ["documents with text: %d, listed in the draft: %d, rejected: %d" % (len(documents), len(draft), len(rejected))]
    for source_id, reason in sorted(rejected.items()):
        lines.append("- rejected %s: %s" % (source_id, reason))
    noise = sum(count for reason, count in drops.items() if reason.startswith("noise-"))
    lines += ["", "sentences considered: %d, clean and kept for the shape rules: %d" % (considered, clean),
              "sentences dropped before the shape rules: " + (", ".join("%s %d" % item for item in sorted(drops.items())) or "none"),
              "of which noise (cleanliness filter): %d, standard-tranche sentences: %d" % (noise, drops[EQUALS_STANDARD] + drops[NEAR_STANDARD]), ""]
    lines += ["| shape | frame hits | in the band | budget | target candidates | lemmas | q min | q max | of which q < %.1f | control candidates |" % DISAGREE_Q,
              "| --- | --- | --- | --- | --- | --- | --- | --- | --- | --- |"]
    for shape in shapes:
        targets = [r for r in rows if r["shape"] == shape and r["role"] == "target"]
        qs = [float(r["q"]) for r in targets]
        lines.append("| %s | %d | %d | %s | %d | %d | %s | %s | %d | %d |" % (
            shape, frame_sizes[shape], band_sizes[shape], "all" if limits[shape] is None else limits[shape], len(targets),
            len({r["lemma"] for r in targets}), format_q(min(qs)) if qs else "-", format_q(max(qs)) if qs else "-",
            sum(1 for q in qs if q < DISAGREE_Q), sum(1 for r in rows if r["shape"] == shape and r["role"] == "control")))
    if problems:
        lines += [""] + ["- PROBLEM: " + p for p in problems]
    return lines


def candidate_report(rows, drops, rejected, problems, limits, draft, documents, considered=0, clean=0, pool_sizes=None, target=TARGET_PER_SHAPE,
                     shapes=SHAPES):
    lines = ["# Candidates", "", "Counts only; no text of any evaluation set.", ""]
    lines += ["documents with text: %d, listed in the draft: %d, rejected: %d" % (len(documents), len(draft), len(rejected))]
    for source_id, reason in sorted(rejected.items()):
        lines.append("- rejected %s: %s" % (source_id, reason))
    noise = sum(count for reason, count in drops.items() if reason.startswith("noise-"))
    lines += ["", "sentences considered: %d, clean and kept for the shape rules: %d" % (considered, clean),
              "sentences dropped before the shape rules: " + (", ".join("%s %d" % item for item in sorted(drops.items())) or "none"),
              "of which noise (cleanliness filter): %d" % noise, ""]
    lines += ["| shape | pool (real hits) | target limit | target real | target authored | control real | control authored | "
              "real targets below the target of %d |" % target, "| --- | --- | --- | --- | --- | --- | --- | --- |"]
    for shape in shapes:
        count = collections.Counter((r["role"], r["origin"]) for r in rows if r["shape"] == shape)
        short = max(0, min(limits[shape], target) - count[("target", "real")])
        lines.append("| %s | %s | %d | %d | %d | %d | %d | %d |" % (shape, (pool_sizes or {}).get(shape, "-"), limits[shape], count[("target", "real")],
                                                                    count[("target", "authored")], count[("control", "real")],
                                                                    count[("control", "authored")], short))
    if problems:
        lines += [""] + ["- PROBLEM: " + p for p in problems]
    return lines


def parse_shape_option(values, option, cast, minimum):
    """{shape: value} of repeated `SHAPE=VALUE` options; an unknown shape or a value below `minimum` is an error."""
    result = {}
    for value in values or ():
        shape, _, number = value.partition("=")
        if shape not in SHAPES or not number:
            raise ValueError("%s needs SHAPE=VALUE with a shape of %s: %r" % (option, ", ".join(SHAPES), value))
        result[shape] = cast(number)
        if result[shape] < minimum:
            raise ValueError("%s value must be at least %s: %r" % (option, minimum, value))
    return result


def parse_shape_over(values):
    """{shape: factor} of repeated `--shape-over SHAPE=FACTOR` options (factor of at least 1)."""
    return parse_shape_option(values, "--shape-over", float, 1)


def parse_shape_lemma_cap(values):
    """{shape: cap} of repeated `--shape-lemma-cap SHAPE=N` options (at least 1)."""
    return parse_shape_option(values, "--shape-lemma-cap", int, 1)


def parse_only_shapes(value):
    """The tuple of shapes of `--only-shapes A,B` (None when not given); an unknown shape is an error."""
    if not value:
        return None
    shapes = tuple(part.strip() for part in value.split(",") if part.strip())
    unknown = [s for s in shapes if s not in SHAPES]
    if unknown or not shapes:
        raise ValueError("--only-shapes needs shapes of %s: %r" % (", ".join(SHAPES), value))
    return tuple(dict.fromkeys(shapes))


def earlier_exclusions(work, data_dir=None):
    """(sentence keys, {(source_id, line)}, Counter {(shape, lemma): kept}) of the root and the earlier rounds of `work`."""
    keys, lines = set(), set()
    works = earlier_works(work)
    for earlier in works:
        for row in read_candidates(earlier):
            keys.add(row["sent_key"])
            if row["origin"] == "real":
                lines.add((row["source_id"], int(row["line"])))
    return keys, lines, lemma_counts_of(works, data_dir=data_dir)


def build_control_room(work, seed=SEED, caps=None, lemma_cap=LEMMA_CAP, shape_lemma_cap=None, data_dir=None):
    """{shape: {lemma: n}} for a controls-only round: the lemmas of the targets that the build selects from the decided items of the
    earlier work areas (the root and the rounds before this one), and n = the controls (at most CONTROL_LEMMA_CAP per lemma) that the
    build can still take for the lemma, which is CONTROL_LEMMA_CAP less the controls it already takes. The selection is `select_items_hard`
    itself (same caps, lemma cap and seed as `build`), so the lemma sets are those of the current build; a work area without decided.tsv
    contributes nothing."""
    items = []
    for earlier in earlier_works(work):
        if os.path.exists(earlier.private_file("decided.tsv")):
            items += read_decided(earlier, data_dir)[0]
    targets, chosen = select_items_hard(items, seed, caps, lemma_cap, HARD_CONTROL_MAX, shape_lemma_cap)
    taken = collections.Counter((i["shape"], i["lemma"]) for i in chosen)
    room = {shape: {} for shape in HARD_SHAPES}
    for item in targets:
        room[item["shape"]][item["lemma"]] = max(0, CONTROL_LEMMA_CAP - taken[(item["shape"], item["lemma"])])
    return room


def _command_candidates(args):
    work = open_work(args)
    only = parse_only_shapes(args.only_shapes)
    keys, lines, existing = earlier_exclusions(work, args.data_dir)
    name = getattr(work, "name", None)
    hard = None
    room = None
    if args.tranche == "hard":
        if args.shape_over:
            raise ValueError("--shape-over is a standard-tranche option; the hard tranche uses --budget")
        hard = HardRule(read_hmm(work), StandardTranche(args.data_dir), parse_budget(args.budget))
    elif args.budget:
        raise ValueError("--budget needs --tranche hard")
    if args.controls_only:
        if hard is None or not name:
            raise ValueError("--controls-only needs --tranche hard and --round NAME")
        if args.no_controls or args.budget:
            raise ValueError("--controls-only writes controls only: it takes neither --no-controls nor --budget")
        room = build_control_room(work, args.seed, hard_caps(args), args.lemma_cap, parse_shape_lemma_cap(args.shape_lemma_cap), args.data_dir)
    pool = args.control_pool if args.control_pool is not None else (HARD_CONTROL_POOL if hard else CONTROL_POOL)
    rows, report = collect_candidates(work, args.data_dir, not args.no_ud, args.target, args.over, args.lemma_cap, args.seed,
                                      shape_over=parse_shape_over(args.shape_over), control_pool=pool,
                                      shape_lemma_cap=parse_shape_lemma_cap(args.shape_lemma_cap), only_shapes=only,
                                      controls=not args.no_controls, compound_strict=args.compound_strict, exclude_keys=keys,
                                      exclude_lines=lines, lemma_existing=existing, prefix=name + "-" if name else "", hard=hard,
                                      control_room=room)
    for line in report:
        print(line)
    common.write_text(work.private_file("candidates-report.md"), report)
    write_candidates(work, rows)
    print("%d candidates written to %scandidates.tsv" % (len(rows), "rounds/%s/" % name if name else ""))
    return 0


def write_candidates(work, rows):
    """Write candidates.tsv: the standard columns, plus q when the rows come from the hard profile (they all carry it)."""
    hard = bool(rows) and "q" in rows[0]
    adjudicate.write_rows(work_path(work, "candidates.tsv"), HARD_CANDIDATE_COLUMNS if hard else CANDIDATE_COLUMNS,
                          [(r["cand_id"], r["shape"], r["role"], r["target"], r["lemma"], r["origin"], r["source_id"], r["line"], r["text"],
                            " ".join(r["tokens"])) + ((r["q"],) if hard else ()) for r in rows])


def read_candidates(work):
    """[candidate dict] of W/candidates.tsv (target as int, tokens as a list; `q` is a string, "" for a control, only in the hard profile)."""
    columns, records = adjudicate.read_records(work_path(work, "candidates.tsv"))
    if tuple(columns) not in (CANDIDATE_COLUMNS, HARD_CANDIDATE_COLUMNS):
        raise ValueError("candidates.tsv: unexpected columns %r" % columns)
    for record in records:
        record["target"] = int(record["target"])
        record["tokens"] = record["tokens"].split(" ")
        record["sent_key"] = sent_key_of(record["text"])
    return records


# ---------------------------------------------------------------- blind batches

def unique_sentences(candidates):
    """{sent_key: (source_id, text, tokens)} in first-seen order (one entry per sentence, whatever number of items it carries)."""
    found = collections.OrderedDict()
    for row in candidates:
        found.setdefault(row["sent_key"], (row["source_id"], row["text"], row["tokens"]))
    return found


def make_batches(sentences, seed=SEED, size=adjudicate.BATCH_SENTENCES, budget=adjudicate.BATCH_ROWS):
    """[(batch name, [sent_key])]: the sentences shuffled with the seed, cut into batches of at most `size` sentences and `budget` rows."""
    keys = sorted(sentences)
    random.Random(seed).shuffle(keys)
    batches, current, rows = [], [], 0
    for key in keys:
        count = len(sentences[key][2])
        if current and (len(current) >= size or rows + count > budget):
            batches.append(("b%03d" % (len(batches) + 1), current))
            current, rows = [], 0
        current.append(key)
        rows += count
    if current:
        batches.append(("b%03d" % (len(batches) + 1), current))
    return batches


def _command_export(args):
    work = open_work(args)
    name = getattr(work, "name", None)
    sentences = unique_sentences(read_candidates(work))
    stale = [m for m in adjudicate.MODELS if os.path.isdir(work.out(m)) and os.listdir(work.out(m))]
    if stale and not args.force:
        print("refusing to export: tagger outputs exist under batches/out (they belong to the previous export); remove them or use --force")
        return 1
    seed = round_seed(args.seed, name)
    if name:
        avoid = set()
        for earlier in all_works(work):
            if earlier.root != work.root and os.path.exists(earlier.private_file("key.tsv")):
                avoid.update(record["opaque_id"] for record in read_key(earlier))
        ids = assign_round_ids(sentences, seed, avoid)
    else:
        ids = adjudicate.assign_ids(sentences, seed)
    batches = make_batches(sentences, seed, args.size, args.rows)
    if os.path.isdir(work.batches):
        for name in os.listdir(work.batches):
            if name.endswith(".tsv") and adjudicate.BATCH_RE.match(name[:-4]):
                os.remove(os.path.join(work.batches, name))
    key_rows = []
    for name, keys in batches:
        rows = []
        for key in keys:
            for position, token in enumerate(sentences[key][2]):
                rows.append((ids[key], str(position), adjudicate.escape_cell(token)))
            key_rows.append((ids[key], name, key, sentences[key][0], len(sentences[key][2])))
        adjudicate.write_batch(work.batch_file(name), rows)
    adjudicate.write_rows(work.private_file("key.tsv"), KEY_COLUMNS, key_rows)
    adjudicate.write_status(work, collections.OrderedDict((name, adjudicate.new_status_record(name)) for name, _ in batches))
    if getattr(work, "name", None):
        for model in adjudicate.MODELS:
            os.makedirs(work.out(model), exist_ok=True)
    print("%d batches, %d sentences, %d token rows" % (len(batches), len(sentences), sum(len(s[2]) for s in sentences.values())))
    return 0


def read_key(work):
    columns, records = adjudicate.read_records(work.private_file("key.tsv"))
    if tuple(columns) != KEY_COLUMNS:
        raise ValueError("private/key.tsv: unexpected columns %r" % (columns,))
    return records


def check_batches(work, sentences, budget=adjudicate.BATCH_ROWS):
    """Errors of the exported batches against the key and the sentences; an empty list when the export is sound and blind."""
    errors = []
    key = read_key(work)
    by_batch = collections.defaultdict(list)
    for record in key:
        by_batch[record["batch"]].append(record)
    files = sorted(name for name in os.listdir(work.batches) if name.endswith(".tsv") and adjudicate.BATCH_RE.match(name[:-4]))
    if [name[:-4] for name in files] != sorted(by_batch):
        errors.append("the batch files and the batches of the key differ")
    seen = set()
    for name in files:
        batch = name[:-4]
        try:
            rows = adjudicate.read_batch(work.batch_file(batch))
        except ValueError as error:
            errors.append(str(error))
            continue
        if len(rows) > budget:
            errors.append("%s: %d rows exceed the budget %d" % (batch, len(rows), budget))
        expected = []
        for record in by_batch.get(batch, ()):
            if record["sent_key"] not in sentences:
                errors.append("%s: the key names a sentence that is not a candidate" % batch)
                continue
            expected.extend((record["opaque_id"], str(i), adjudicate.escape_cell(t)) for i, t in enumerate(sentences[record["sent_key"]][2]))
            if record["opaque_id"] in seen:
                errors.append("%s: id %s is used twice" % (batch, record["opaque_id"]))
            seen.add(record["opaque_id"])
        if any(not adjudicate.ID_RE.match(row[0]) for row in rows):
            errors.append("%s: an id is not an opaque id" % batch)
        if rows != expected:
            errors.append("%s: the rows differ from the key and the sentences (%d rows, expected %d)" % (batch, len(rows), len(expected)))
    return errors


def _command_check_batches(args):
    work = open_work(args)
    errors = check_batches(work, unique_sentences(read_candidates(work)), args.rows)
    for message in errors[:args.limit]:
        print("  " + message)
    print("%d batch files checked: %s" % (len({k["batch"] for k in read_key(work)}), "FAILED" if errors else "passed"))
    return 1 if errors else 0


def _command_validate_output(args):
    work = open_work(args)
    if not args.batch and not args.all:
        raise ValueError("give --batch BNNN or --all")
    models = (args.model,) if args.model else adjudicate.MODELS
    targets = [args.batch] if args.batch else sorted(adjudicate.read_status(work))
    failed = 0
    for batch in targets:
        for model in models:
            if args.all:
                files, problems = adjudicate.output_files(work.out(model), batch)
                if not files and not problems:
                    continue
            state, errors = adjudicate.validate_and_record(work, model, batch, count=not args.no_count)
            print("%s %s: %s" % (batch, model, state))
            for message in errors[:args.limit]:
                print("  " + message)
            failed += state == "invalid"
    return 1 if failed else 0


def _command_status(args):
    work = open_work(args)
    status = adjudicate.read_status(work)
    counts = collections.Counter()
    for record in status.values():
        counts["opus " + record["opus_state"]] += 1
        counts["sol " + record["sol_state"]] += 1
    print("%d batches" % len(status))
    for name, count in sorted(counts.items()):
        print("  %-16s %d" % (name, count))
    for label, path in (("sources-draft", shared_path(work, "sources-draft.tsv")), ("tokens", shared_path(work, "tokens.tsv")),
                        ("candidates", work_path(work, "candidates.tsv")), ("decided items", work.private_file("decided.tsv"))):
        print("  %-16s %s" % (label, "present" if os.path.exists(path) else "missing"))
    return 0


# ---------------------------------------------------------------- decide

def decide_item(item, opus, sol):
    """(outcome, reason, agreed tag) of the judged word of an item from the two votes on it: keep, or drop with a reason.

    `item` has `shape` and `role`; `opus` and `sol` are the `Vote`s of the judged word. Different tags -> `target-disagreement`;
    the same tag in a class other than the intended one (also a tag that is neither noun nor verb) -> `class-mismatch`.
    """
    if opus.tag != sol.tag:
        return "drop", "target-disagreement", ""
    if tag_class(opus.tag) != intended_class(item["shape"], item["role"]):
        return "drop", "class-mismatch", opus.tag
    return "keep", "", opus.tag


def vote_tag(votes, index):
    """The tag of the token at `index` when both labelers agree on it: '' when there is no such token, '?' when they differ."""
    opus, sol = votes
    if index < 0 or index not in opus:
        return ""
    return opus[index].tag if opus[index].tag == sol[index].tag else "?"


NOMINAL_GERUND_SHAPES = ("n-subject-initial", "n-compound-head", "n-measure")


def possible_gerund(form, lex):
    """True when the lower-cased `form` ends in `ing` and `base_form_of` finds a verb stem that differs from it (an -ing form of a verb)."""
    low = form.lower()
    return low.endswith("ing") and base_form_of(form, lex) != low


def frame_mismatch(item, opus, sol, form=None, lex=None):
    """True when the judged word of a kept target item is not in the frame of its shape (a later, remove-only rule; uses only the tags
    that BOTH labelers gave the neighbor tokens; a neighbor they disagree on never satisfies a frame).

    n-compound-head       the token before is NOUN or PROPN (the target is the head of a compound)
    n-measure             the token before is NUM; the token after is not NOUN or PROPN, nor an ADJ that a NOUN or PROPN follows
                          (the target is the measure noun, not a modifier of what follows)
    n-subject-initial     the target is token 0 (the class Noun is then enough: a sentence-initial word that both labelers call a
                          noun is a noun where the position favors a verb, the confusion this shape tests)
    v-imperative-initial  the target is token 0 (a verb at the start of the sentence is an instruction) and is in the base form
    v-after-modal         the token before is AUX (a modal) or PART (`to`) and the target is in the base form

    Base form rule (v-imperative-initial and v-after-modal only; deterministic, from the surface form `form` and the lexicon `lex`): the
    lower-cased surface form must equal its base form (`base_form_of`: no listed noun-verb stem behind an `-s` or `-es` ending, no
    listed VERB stem behind an `-ing` or `-ed` ending). `Wring`, `Bring`, `Ring`, `Process`, `Pass`, `Focus` pass; `Avoiding`,
    `Operating`, `Standing`, `Opened`, `Checks` fail. An instruction starts with the base form, not a gerund subject; after a modal
    or `to` the base form (`be`, `check`) is the only verb form. The rule is skipped when `form` or `lex` is not given.

    Nominal gerund rule (n-subject-initial, n-compound-head and n-measure only; same inputs): the target is dropped when its lower-cased
    surface form ends in `ing` and `base_form_of` finds a verb stem that differs from the surface form (`possible_gerund`: `Reloading`,
    `Testing`, `clearing`). data/CONVENTIONS.md has no rule for a nominal gerund, so the gold of such a target is not decidable. A genuine
    noun that is also an -ing form of a listed verb (`building`) is lost too, accepted. `Wring`, `Ring`, `Spring` pass (no verb stem).

    Controls have no frame, only the class. `opus` and `sol` map a token index to its Vote.
    """
    if item["role"] != "target":
        return False
    shape, i = item["shape"], item["target"]
    votes = (opus, sol)
    if shape in ("v-imperative-initial", "v-after-modal") and form is not None and lex is not None and form.lower() != base_form_of(form, lex):
        return True
    if shape in NOMINAL_GERUND_SHAPES and form is not None and lex is not None and possible_gerund(form, lex):
        return True
    if shape in ("n-subject-initial", "v-imperative-initial"):
        return i != 0
    if shape == "n-compound-head":
        return vote_tag(votes, i - 1) not in ("NOUN", "PROPN")
    if shape == "v-after-modal":
        return vote_tag(votes, i - 1) not in ("AUX", "PART")
    if shape == "n-measure":
        if vote_tag(votes, i - 1) != "NUM":
            return True
        after = vote_tag(votes, i + 1)
        if after in ("NOUN", "PROPN", "?"):
            return True
        return after == "ADJ" and vote_tag(votes, i + 2) in ("NOUN", "PROPN", "?")
    return False


def decide_token(opus, sol):
    """(tag, status cell) of a token that is not the judged word: agreed -> `unanimous`; else `disputed` with the first labeler's tag."""
    flags = adjudicate.effective_flags((), opus, sol)
    base = "unanimous" if opus.tag == sol.tag else "disputed"
    return opus.tag, common.format_status(base, flags)


def run_decisions(work, candidates, lex=None, data_dir=None):
    """(items, rows, incomplete): `items` is [dict of ITEM_COLUMNS], `rows` the token rows of every decided sentence.

    Only sentences of batches that both models completed and validated are decided; the others are `incomplete`
    (sentence keys). An item whose sentence was decided is `keep` or `drop` with a reason; an item of a source of the merged exclusions
    (`merged_excluded_sources`: the committed record and the work file) is a `drop`. `lex` is the lexicon for the base form
    rule of `frame_mismatch` (v-imperative-initial, v-after-modal).
    """
    key = read_key(work)
    excluded = merged_excluded_sources(work, data_dir)
    by_key = {record["sent_key"]: record for record in key}
    sentences = unique_sentences(candidates)
    votes = {}
    for batch in sorted({record["batch"] for record in key}):
        votes[batch] = adjudicate.read_outputs_by_id(work, batch)
    items, rows, incomplete = [], {}, set()
    for cand in candidates:
        record = by_key.get(cand["sent_key"])
        batch_votes = votes.get(record["batch"]) if record else None
        if batch_votes is None:
            incomplete.add(cand["sent_key"])
            continue
        opaque = record["opaque_id"]
        opus, sol = (batch_votes[model][opaque] for model in adjudicate.MODELS)
        target = cand["target"]
        outcome, reason, agreed = decide_item(cand, opus[target], sol[target])
        if outcome == "keep" and frame_mismatch(cand, opus, sol, sentences[cand["sent_key"]][2][target], lex):
            outcome, reason = "drop", "frame-mismatch"
        if cand["source_id"] in excluded:
            outcome, reason = "drop", EXCLUDED_REASON
        if cand["sent_key"] not in rows:
            rows[cand["sent_key"]] = [(i,) + decide_token(opus[i], sol[i]) for i in range(len(sentences[cand["sent_key"]][2]))]
        disputed = sum(1 for _, _, status in rows[cand["sent_key"]] if common.base_status(status) == "disputed")
        items.append(dict(cand_id=cand["cand_id"], sent_key=cand["sent_key"], shape=cand["shape"], role=cand["role"], target=cand["target"],
                          lemma=cand["lemma"], origin=cand["origin"], source_id=cand["source_id"], outcome=outcome, reason=reason,
                          opus_tag=opus[target].tag, sol_tag=sol[target].tag, agreed_tag=agreed, disputed_tokens=disputed,
                          **({"q": cand["q"]} if "q" in cand else {})))
    row_list = []
    for sent_key, tagged in rows.items():
        forms = sentences[sent_key][2]
        row_list += [(sent_key, i, forms[i], tag, status) for i, tag, status in tagged]
    return items, row_list, sorted(incomplete)


def decide_report(items, incomplete):
    lines = ["# Decisions", "", "| shape | role | items | kept | target-disagreement | class-mismatch | frame-mismatch | excluded-source | incomplete sentences |",
             "| --- | --- | --- | --- | --- | --- | --- | --- | --- |"]
    for shape in SHAPES:
        for role in ROLES:
            members = [i for i in items if i["shape"] == shape and i["role"] == role]
            count = collections.Counter(i["reason"] or "kept" for i in members)
            lines.append("| %s | %s | %d | %d | %d | %d | %d | %d | |" % (shape, role, len(members), count["kept"], count["target-disagreement"],
                                                                         count["class-mismatch"], count["frame-mismatch"],
                                                                         count[EXCLUDED_REASON]))
    lines += ["", "incomplete sentences (not decided): %d" % len(incomplete),
              "disputed non-target tokens in kept items: %d" % sum(i["disputed_tokens"] for i in items if i["outcome"] == "keep")]
    return lines


def _command_decide(args):
    work = open_work(args)
    items, rows, incomplete = run_decisions(work, read_candidates(work), lexicon_of(args.data_dir), args.data_dir)
    report = decide_report(items, incomplete)
    columns = HARD_ITEM_COLUMNS if items and "q" in items[0] else ITEM_COLUMNS
    adjudicate.write_rows(work.private_file("decided.tsv"), columns, [[i[c] for c in columns] for i in items])
    adjudicate.write_rows(work.private_file("decided-rows.tsv"), ROW_COLUMNS, rows)
    common.write_text(work.private_file("decide-report.md"), report)
    adjudicate._print(report)
    return 0


def read_decided(work, data_dir=None):
    """([item dict], {sent_key: [(form, tag, status)]}) of private/decided.tsv and decided-rows.tsv.

    An item of a source of the merged exclusions (`merged_excluded_sources`) is a `drop` whatever the stored outcome."""
    _, records = adjudicate.read_records(work.private_file("decided.tsv"))
    excluded = merged_excluded_sources(work, data_dir)
    for record in records:
        record["target"] = int(record["target"])
        record["disputed_tokens"] = int(record["disputed_tokens"])
        if record["source_id"] in excluded:
            record["outcome"], record["reason"] = "drop", EXCLUDED_REASON
    _, row_records = adjudicate.read_records(work.private_file("decided-rows.tsv"))
    rows = collections.defaultdict(list)
    for record in row_records:
        rows[record["sent_key"]].append((int(record["index"]), record["form"], record["tag"], record["status"]))
    return records, {key: [cells[1:] for cells in sorted(cells_list)] for key, cells_list in rows.items()}


def read_all_candidates(work):
    """[candidate dict] of the root and of every round under W/rounds (the root alone when no round has candidates)."""
    works = [w for w in all_works(work) if os.path.exists(work_path(w, "candidates.tsv"))]
    if not works:
        return read_candidates(adjudicate.Work(getattr(work, "base", work.root)))
    found, seen = [], set()
    for member in works:
        for row in read_candidates(member):
            if row["cand_id"] in seen:
                raise ValueError("candidate id %s is used in two rounds" % row["cand_id"])
            seen.add(row["cand_id"])
            found.append(row)
    return found


def read_all_decided(work, data_dir=None):
    """([item], {sent_key: rows}) merged over the root and every round that has decided items (the root alone when none has)."""
    works = all_works(work, "decided.tsv")
    if not works:
        return read_decided(adjudicate.Work(getattr(work, "base", work.root)), data_dir)
    items, rows, seen = [], {}, set()
    for member in works:
        member_items, member_rows = read_decided(member, data_dir)
        for item in member_items:
            if item["cand_id"] in seen:
                raise ValueError("candidate id %s is decided in two rounds" % item["cand_id"])
            seen.add(item["cand_id"])
            items.append(item)
        for key, tagged in member_rows.items():
            rows.setdefault(key, tagged)
    return items, rows


# ---------------------------------------------------------------- review sample (the precision gate)

REVIEW_INSTRUCTIONS = [
    "# Review items", "",
    "Each item shows a sentence and one marked word with a proposed UPOS tag. Judge ONLY whether the proposed tag is correct for the",
    "marked word in this sentence according to data/CONVENTIONS.md (read it in full first). Answer with one line per item, tab",
    "separated, and nothing else: `item<TAB>verdict<TAB>cite` where the verdict is `correct`, `incorrect` or `unclear` (the conventions",
    "do not decide it), and the cite names the rule of CONVENTIONS you applied in at most 20 words. Do not look at any other file and",
    "do not search for the sentences.", "",
]


def build_sample(items, seed=SEED, per_shape=12, controls=4):
    """[(kind, item, shown tag)] shuffled: `per_shape` kept target items of every shape, plus `controls` items whose tag is swapped.

    A control item shows the other class of the agreed tag (NOUN for VERB and the reverse), so the right verdict is `incorrect`;
    a reviewer that accepts more than CONTROLS_ACCEPTED_MAX of them is too lenient.
    """
    rng = random.Random(seed)
    kept = [i for i in items if i["outcome"] == "keep" and i["role"] == "target"]
    chosen, rest = [], []
    for shape in SHAPES:
        members = sorted((i for i in kept if i["shape"] == shape), key=lambda i: i["cand_id"])
        picked = rng.sample(members, min(per_shape, len(members)))
        chosen += picked
        rest += [i for i in members if i not in picked]
    result = [("real", i, i["agreed_tag"]) for i in chosen]
    swapped = rng.sample(sorted(rest, key=lambda i: i["cand_id"]), min(controls, len(rest)))
    result += [("control", i, "VERB" if tag_class(i["agreed_tag"]) == "Noun" else "NOUN") for i in swapped]
    rng.shuffle(result)
    return result


def build_sample_hard(targets, seed=SEED, per_shape=15, controls=4, low_q_share=0.5):
    """[(kind, item, shown tag)] shuffled, for the hard tranche: `per_shape` target items of every shape (from `targets`, the items the
    build takes), at least ceil(per_shape * low_q_share) of them with q < DISAGREE_Q (as far as the shape has them), plus `controls`
    items with a swapped tag (right verdict `incorrect`, a leniency control)."""
    rng = random.Random(seed)
    chosen, rest = [], []
    for shape in SHAPES:
        members = sorted((i for i in targets if i["shape"] == shape), key=lambda i: i["cand_id"])
        low = [i for i in members if float(i["q"]) < DISAGREE_Q]
        picked = rng.sample(low, min(len(low), int(math.ceil(per_shape * low_q_share))))
        others = [i for i in members if i not in picked]
        picked += rng.sample(others, min(per_shape - len(picked), len(others)))
        chosen += picked
        rest += [i for i in members if i not in picked]
    result = [("real", i, i["agreed_tag"]) for i in chosen]
    swapped = rng.sample(sorted(rest, key=lambda i: i["cand_id"]), min(controls, len(rest)))
    result += [("control", i, "VERB" if tag_class(i["agreed_tag"]) == "Noun" else "NOUN") for i in swapped]
    rng.shuffle(result)
    return result


def sample_lines(sample, sentences, prefix="R"):
    lines = list(REVIEW_INSTRUCTIONS)
    keys = []
    for number, (kind, item, shown) in enumerate(sample, 1):
        name = "%s%03d" % (prefix, number)
        words = ["[[%s]]" % w if i == item["target"] else w for i, w in enumerate(sentences[item["sent_key"]][2])]
        lines += ["## " + name, "", "Sentence: " + " ".join(words), "Word: %s (index %d)" % (sentences[item["sent_key"]][2][item["target"]],
                                                                                           item["target"]),
                  "Proposed tag: " + shown, ""]
        keys.append((name, kind, item["cand_id"], item["shape"], item["target"], shown, item["agreed_tag"]))
    return lines, keys


def _command_sample(args):
    work = adjudicate.Work(args.work)
    items, _ = read_all_decided(work, args.data_dir)
    sentences = unique_sentences(read_all_candidates(work))
    if args.tranche == "hard":
        targets, _ = select_items_hard(items, args.seed, hard_caps(args), args.lemma_cap, HARD_CONTROL_MAX,
                                       shape_lemma_cap=parse_shape_lemma_cap(args.shape_lemma_cap))
        sample = build_sample_hard(targets, args.seed, args.per_shape if args.per_shape is not None else 15, args.controls, args.low_q_share)
    else:
        sample = build_sample(items, args.seed, args.per_shape if args.per_shape is not None else 12, args.controls)
    lines, keys = sample_lines(sample, sentences)
    common.write_text(work.private_file("review-sample.md"), lines)
    adjudicate.write_rows(work.private_file("review-sample-key.tsv"), SAMPLE_KEY_COLUMNS, keys)
    real = sum(1 for kind, _, _ in sample if kind == "real")
    print("%d items: %d real, %d control" % (len(sample), real, len(sample) - real))
    return 0


def review_statistics(keys, verdicts):
    """(per-shape Counter, control Counter) of the verdicts over the sample key rows."""
    answers = {v.item: v.verdict for v in verdicts}
    per_shape, controls = collections.defaultdict(collections.Counter), collections.Counter()
    for row in keys:
        verdict = answers.get(row["item"])
        if verdict is None:
            continue
        if row["kind"] == "control":
            controls[verdict] += 1
        else:
            per_shape[row["shape"]][verdict] += 1
    return per_shape, controls


def review_precision(counter):
    judged = counter["correct"] + counter["incorrect"]
    return counter["correct"] / judged if judged else None


def review_lines(per_shape, controls):
    """(lines, passed): the Markdown result of the model review and whether the gate (precision of at least 95%) holds."""
    total = collections.Counter()
    lines = ["# Review result", "", "| shape | correct | incorrect | unclear | precision |", "| --- | --- | --- | --- | --- |"]
    for shape in SHAPES:
        counter = per_shape.get(shape, collections.Counter())
        value = review_precision(counter)
        lines.append("| %s | %d | %d | %d | %s |" % (shape, counter["correct"], counter["incorrect"], counter["unclear"],
                                                    "-" if value is None else "%.1f%%" % (100 * value)))
        total.update(counter)
    overall = review_precision(total)
    lenient = controls["correct"] > CONTROLS_ACCEPTED_MAX
    passed = overall is not None and overall >= PRECISION_MIN and not lenient
    lines += ["", "- reviewed target items: %d (correct %d, incorrect %d, unclear %d)" % (sum(total.values()), total["correct"],
                                                                                         total["incorrect"], total["unclear"]),
              "- overall precision: %s (gate: at least %.0f%%)" % ("-" if overall is None else "%.1f%%" % (100 * overall), 100 * PRECISION_MIN),
              "- control items accepted as correct: %d of %d%s" % (controls["correct"], sum(controls.values()),
                                                                  " (TOO LENIENT, repeat the review)" if lenient else ""),
              "- gate: " + ("PASSED" if passed else "STOP: do not publish; report the result")]
    return lines, passed


def _command_review_result(args):
    work = adjudicate.Work(args.work)
    columns, keys = adjudicate.read_records(work.private_file("review-sample-key.tsv"))
    verdicts = []
    for name in args.verdicts:
        with open(name, "r", encoding="utf-8") as handle:
            verdicts += adjudicate.parse_verdicts(handle.read())
    unknown = {v.item for v in verdicts} - {k["item"] for k in keys}
    if unknown:
        raise ValueError("verdicts for unknown items: " + ", ".join(sorted(unknown)[:5]))
    per_shape, controls = review_statistics(keys, verdicts)
    lines, passed = review_lines(per_shape, controls)
    missing = {k["item"] for k in keys} - {v.item for v in verdicts}
    lines.append("- unanswered items: %d" % len(missing))
    adjudicate._print(lines)
    common.write_text(work.private_file("review-result.md"), lines)
    return 0 if passed and not missing else 1


# ---------------------------------------------------------------- build

class Taker:
    """Takes items in rank order while a per-lemma cap, a total and a set of already used sentences allow."""

    def __init__(self, lemma_cap, used):
        self.lemma_cap = lemma_cap
        self.used = used
        self.counts = collections.Counter()

    def take(self, pool, limit):
        taken = []
        for item in pool:
            if len(taken) >= limit:
                break
            if item["sent_key"] in self.used or self.counts[item["lemma"]] >= self.lemma_cap:
                continue
            self.used.add(item["sent_key"])
            self.counts[item["lemma"]] += 1
            taken.append(item)
        return taken


def authored_room(real, share):
    """The most authored items that keep their share of (real + authored) at or below `share`."""
    return int(math.floor(real * share / (1.0 - share) + 1e-9))


def control_quotas(controls):
    """The control quota of each shape: they sum to `controls` exactly, the remainder goes to the first shapes in SHAPES order."""
    base, extra = divmod(max(0, controls), len(SHAPES))
    return {shape: base + (1 if n < extra else 0) for n, shape in enumerate(SHAPES)}


def select_items(items, seed=SEED, per_shape=TARGET_PER_SHAPE, lemma_cap=LEMMA_CAP, share=AUTHORED_SHARE, controls=CONTROLS,
                 shape_lemma_cap=None):
    """(targets, controls): the kept items chosen for the set.

    Per shape: real targets in seeded rank order up to `per_shape` (at most `lemma_cap` per lemma), then authored ones up to the
    free places and up to the authored share of the shape; a shape that real text cannot fill is not padded. Controls: only for a
    lemma of a chosen target of the same shape, at most CONTROL_LEMMA_CAP per lemma, real first (the quota per shape, then any
    shape), then authored up to the authored share of the control stratum. A sentence is used once.
    """
    used = set()
    kept = [i for i in items if i["outcome"] == "keep"]

    def ordered(shape, role, origin):
        pool = [i for i in kept if i["shape"] == shape and i["role"] == role and i["origin"] == origin]
        return sorted(pool, key=lambda i: rank_of(seed, "select", i["cand_id"]))

    targets = []
    for shape in SHAPES:
        taker = Taker((shape_lemma_cap or {}).get(shape, lemma_cap), used)
        real = taker.take(ordered(shape, "target", "real"), per_shape)
        authored = taker.take(ordered(shape, "target", "authored"), min(per_shape - len(real), authored_room(len(real), share)))
        targets += real + authored
    lemmas = {shape: {i["lemma"] for i in targets if i["shape"] == shape} for shape in SHAPES}

    def control_pool(shape, origin):
        return [i for i in ordered(shape, "control", origin) if i["lemma"] in lemmas[shape]]

    chosen = []
    quotas = control_quotas(controls)
    takers = {shape: Taker(CONTROL_LEMMA_CAP, used) for shape in SHAPES}
    for shape in SHAPES:
        chosen += takers[shape].take(control_pool(shape, "real"), quotas[shape])
    for shape in SHAPES:                      # fill from any shape when a shape had fewer real controls than its quota
        chosen += takers[shape].take(control_pool(shape, "real"), max(0, controls - len(chosen)))
    real_count = len(chosen)
    room = min(controls - len(chosen), authored_room(real_count, share))
    pools = {shape: control_pool(shape, "authored") for shape in SHAPES}
    added = 0
    progress = True
    while added < room and progress:           # one authored control per shape in turn, so no shape is favored
        progress = False
        for shape in SHAPES:
            if added < room:
                taken = takers[shape].take(pools[shape], 1)
                chosen += taken
                added += len(taken)
                progress = progress or bool(taken)
    return targets, chosen


def hard_control_quotas(total, shapes=HARD_SHAPES):
    """The control quota of each hard shape: they sum to `total`, the remainder goes to the first shapes in order."""
    base, extra = divmod(max(0, total), len(shapes))
    return {shape: base + (1 if n < extra else 0) for n, shape in enumerate(shapes)}


def select_items_hard(items, seed=SEED, caps=None, lemma_cap=LEMMA_CAP, control_max=HARD_CONTROL_MAX, shape_lemma_cap=None):
    """(targets, controls): the kept items chosen for the hard tranche (real items only).

    Per shape (HARD_SHAPES): the kept targets in the order of the candidate stage (q ascending, hardest first; ties by the seeded rank), at
    most `lemma_cap` per lemma, up to the cap of the shape (`caps`, default HARD_CAPS). Controls: only for a lemma of a chosen target of the
    same shape, at most CONTROL_LEMMA_CAP per lemma, by seeded rank, the quota per shape first (`hard_control_quotas`), then any shape,
    up to `control_max`. A sentence is used once. A shape that the labels cannot fill is not padded.
    """
    caps = caps or HARD_CAPS
    used = set()
    kept = [i for i in items if i["outcome"] == "keep" and i["origin"] == "real"]
    targets = []
    for shape in HARD_SHAPES:
        pool = sorted((i for i in kept if i["shape"] == shape and i["role"] == "target"),
                      key=lambda i: (float(i["q"]), rank_of(seed, "hard", shape, i["sent_key"])))
        targets += Taker((shape_lemma_cap or {}).get(shape, lemma_cap), used).take(pool, caps.get(shape, 0))
    lemmas = {shape: {i["lemma"] for i in targets if i["shape"] == shape} for shape in HARD_SHAPES}

    def control_pool(shape):
        pool = [i for i in kept if i["shape"] == shape and i["role"] == "control" and i["lemma"] in lemmas[shape]]
        return sorted(pool, key=lambda i: rank_of(seed, "select", i["cand_id"]))

    chosen = []
    takers = {shape: Taker(CONTROL_LEMMA_CAP, used) for shape in HARD_SHAPES}
    for shape, quota in hard_control_quotas(control_max).items():
        chosen += takers[shape].take(control_pool(shape), quota)
    for shape in HARD_SHAPES:                 # fill from any shape when a shape had fewer controls than its quota
        chosen += takers[shape].take(control_pool(shape), max(0, control_max - len(chosen)))
    return targets, chosen


def final_sentence(item, tagged_rows, number, tranche="standard"):
    """The sentence dict of an item for common.write_corpus (tokens, tags, status, and the extra comments of the judged word)."""
    return {"sent_id": TRANCHE_ID[tranche] % number, "source_id": item["source_id"], "text": item["text"],
            "rows": [[form, tag, status] for form, tag, status in tagged_rows],
            "extra": ["# shape = " + item["shape"], "# role = " + item["role"], "# target = %d" % item["target"],
                      "# lemma = " + item["lemma"], "# origin = " + item["origin"]]}


def sentence_problems(sentence, extra=None):
    """Problems of one built sentence: the rules of the data test (target in range and in the right class, never disputed, text rejoins)."""
    values = dict(part[2:].split(" = ", 1) for part in (extra if extra is not None else sentence["extra"]))
    problems = []
    rows = sentence["rows"]
    shape, role = values.get("shape"), values.get("role")
    if shape not in SHAPES or role not in ROLES or values.get("origin") not in ORIGINS:
        return ["%s: shape, role or origin is missing or unknown" % sentence["sent_id"]]
    if (values["origin"] == "authored") != (sentence["source_id"] == AUTHORED_ID):
        problems.append("%s: origin %s does not match the document %s" % (sentence["sent_id"], values["origin"], sentence["source_id"]))
    if not values.get("target", "").isdigit() or int(values["target"]) >= len(rows):
        return problems + ["%s: the target is not a token index" % sentence["sent_id"]]
    target = rows[int(values["target"])]
    if tag_class(target[1]) != intended_class(shape, role):
        problems.append("%s: the judged word is tagged %s, not %s" % (sentence["sent_id"], target[1], intended_class(shape, role)))
    if common.base_status(target[2]) == "disputed":
        problems.append("%s: the judged word is disputed" % sentence["sent_id"])
    if "".join(r[0] for r in rows) != sentence["text"].replace(" ", ""):
        problems.append("%s: the tokens do not rejoin to the text" % sentence["sent_id"])
    return problems


def sources_rows(draft, built):
    """The rows of the final sources.tsv: the used draft documents with their sentence counts, and the authored pseudo-document."""
    counts = collections.Counter(s["source_id"] for s in built)
    rows = []
    for row in sorted(draft, key=lambda r: r["source_id"]):
        if counts[row["source_id"]]:
            row = dict(row)
            row["sentences"] = str(counts[row["source_id"]])
            rows.append([row[c] for c in SOURCE_COLUMNS])
    authored = sorted(s["text"] for s in built if s["source_id"] == AUTHORED_ID)
    if authored:
        text_hash = common.sha256_bytes("\n".join(authored).encode("utf-8") + b"\n")
        rows.append([AUTHORED_ID, AUTHORED_FAMILY, "Authored top-up sentences of the shapes development set", "",
                     "Shapes development set (written by the project, labeled by two model labelers)", "2026", AUTHORED_LICENSE,
                     AUTHORED_TERMS, "", "", text_hash, str(counts[AUTHORED_ID])])
    return rows


class QuotaError(ValueError):
    """The built set does not meet the requested quotas (and `--allow-short` is not given)."""


class ExcludedSourceError(ValueError):
    """A chosen sentence or a used source row is a document listed in an exclusion table."""


def quota_problems(targets, controls_chosen, per_shape=TARGET_PER_SHAPE, lemma_cap=LEMMA_CAP, share=AUTHORED_SHARE, controls=CONTROLS,
                   shape_lemma_cap=None):
    """[message] of every quota the chosen items miss: the target total per shape, the control total, the per-lemma caps and the authored share."""
    problems = []
    caps = shape_lemma_cap or {}
    for shape in SHAPES:
        members = [i for i in targets if i["shape"] == shape]
        if len(members) != per_shape:
            problems.append("%s: %d targets, requested %d" % (shape, len(members), per_shape))
        cap = caps.get(shape, lemma_cap)
        for lemma, n in sorted(collections.Counter(i["lemma"] for i in members).items()):
            if n > cap:
                problems.append("%s: lemma '%s' has %d targets, the cap is %d" % (shape, lemma, n, cap))
        authored = sum(1 for i in members if i["origin"] == "authored")
        if members and authored > share * len(members) + 1e-9:
            problems.append("%s: %d of %d targets are authored, the share cap is %.0f%%" % (shape, authored, len(members), 100 * share))
    if len(controls_chosen) != controls:
        problems.append("controls: %d, requested %d" % (len(controls_chosen), controls))
    for (shape, lemma), n in sorted(collections.Counter((i["shape"], i["lemma"]) for i in controls_chosen).items()):
        if n > CONTROL_LEMMA_CAP:
            problems.append("controls: %s lemma '%s' has %d controls, the cap is %d" % (shape, lemma, n, CONTROL_LEMMA_CAP))
    authored = sum(1 for i in controls_chosen if i["origin"] == "authored")
    if controls_chosen and authored > share * len(controls_chosen) + 1e-9:
        problems.append("controls: %d of %d are authored, the share cap is %.0f%%" % (authored, len(controls_chosen), 100 * share))
    return problems


def quota_problems_hard(targets, controls_chosen, floors=None, caps=None, control_min=HARD_CONTROL_MIN, control_max=HARD_CONTROL_MAX,
                        lemma_cap=LEMMA_CAP, shape_lemma_cap=None):
    """[message] of every pre-registered hard-tranche quota the chosen items miss: per shape below the floor or above the cap, the control
    total below the floor or above the cap, the per-lemma caps, and any authored item (the hard tranche has none)."""
    floors = floors or HARD_FLOORS
    caps = caps or HARD_CAPS
    problems = []
    for shape in SHAPES:
        members = [i for i in targets if i["shape"] == shape]
        if len(members) < floors.get(shape, 0):
            problems.append("%s: %d targets, the floor is %d" % (shape, len(members), floors.get(shape, 0)))
        if len(members) > caps.get(shape, 0):
            problems.append("%s: %d targets, the cap is %d" % (shape, len(members), caps.get(shape, 0)))
        cap = (shape_lemma_cap or {}).get(shape, lemma_cap)
        for lemma, n in sorted(collections.Counter(i["lemma"] for i in members).items()):
            if n > cap:
                problems.append("%s: lemma '%s' has %d targets, the cap is %d" % (shape, lemma, n, cap))
    if len(controls_chosen) < control_min:
        problems.append("controls: %d, the floor is %d" % (len(controls_chosen), control_min))
    if len(controls_chosen) > control_max:
        problems.append("controls: %d, the cap is %d" % (len(controls_chosen), control_max))
    for (shape, lemma), n in sorted(collections.Counter((i["shape"], i["lemma"]) for i in controls_chosen).items()):
        if n > CONTROL_LEMMA_CAP:
            problems.append("controls: %s lemma '%s' has %d controls, the cap is %d" % (shape, lemma, n, CONTROL_LEMMA_CAP))
    authored = sum(1 for i in list(targets) + list(controls_chosen) if i["origin"] == "authored")
    if authored:
        problems.append("%d authored items: the hard tranche has none" % authored)
    return problems


def merge_excluded_rows(committed, w_rows):
    """The sorted union of the committed exclusion rows and the work-folder rows; a committed row is never dropped or changed.

    An id in both tables with the same normalized URL keeps the committed row (the committed reason and decided date stand). An id
    in both with a different URL is a conflict, and it raises ExcludedSourceError: keeping one URL would silently erase the
    exclusion of the other, so the owner must resolve it (use another id for the second document).
    """
    merged = {row[0]: list(row) for row in committed}
    conflicts = []
    for row in w_rows:
        kept = merged.get(row[0])
        if kept is None:
            merged[row[0]] = list(row)
        elif common.normalize_url(kept[1]) != common.normalize_url(row[1]):
            conflicts.append("%s is excluded as %s in the committed table but as %s in W/excluded-sources.tsv" % (row[0], kept[1], row[1]))
    if conflicts:
        raise ExcludedSourceError("conflicting exclusion rows (same id, different URL):\n  " + "\n  ".join(conflicts[:20]))
    return sorted(merged.values())


def excluded_problems(work, draft, built, committed, w_rows):
    """[message] for every built source or used source row whose id or URL is in the committed or the work-folder exclusion table."""
    lookup = common.excluded_lookup(list(committed) + [tuple(r) for r in w_rows])
    urls = {row["source_id"]: row["url"] for row in draft}
    problems = []
    for source_id in sorted({s["source_id"] for s in built} - {AUTHORED_ID}):
        hit = common.excluded_match(lookup, source_id, urls.get(source_id, ""))
        if hit:
            problems.append("sentences of %s are chosen, but %s is excluded" % (source_id, hit))
    for row in sources_rows(draft, built):
        hit = common.excluded_match(lookup, row[0], row[3])
        if hit and not any(p.startswith("sentences of %s " % row[0]) for p in problems):
            problems.append("source row %s is used, but %s is excluded" % (row[0], hit))
    return problems


def build(work, out_dir, seed=SEED, per_shape=TARGET_PER_SHAPE, lemma_cap=LEMMA_CAP, share=AUTHORED_SHARE, controls=CONTROLS,
          shape_lemma_cap=None, allow_short=False, data_dir=None, tranche="standard", hard=None):
    """Write dev.tsv and sources.tsv (hard.tsv and hard-sources.tsv for the hard tranche) into `out_dir`; returns (sentences, report lines).

    Fail-closed gates, before anything is written: a chosen sentence or used source row whose id or URL is in the committed
    data/eval/shapes-dev/excluded-sources.tsv or in W/excluded-sources.tsv raises ExcludedSourceError; a missed quota (targets per
    shape, controls, lemma caps, authored share) raises QuotaError unless `allow_short` (then the report lists it as a warning).
    The committed exclusion table can only grow: its rows are kept in the written table. The hard tranche reads the exclusion record
    fail-closed and never rewrites it; its quotas are the pre-registered floors and caps (`hard` = dict with optional floors, caps,
    control_min, control_max; see `quota_problems_hard`) instead of the exact per-shape counts, and `per_shape`, `share` and `controls` are unused.
    """
    items, tagged = read_all_decided(work, data_dir)
    draft = read_draft(work)
    hard = hard or {}
    if tranche == "hard":
        targets, chosen = select_items_hard(items, seed, hard.get("caps"), lemma_cap, hard.get("control_max", HARD_CONTROL_MAX), shape_lemma_cap)
    else:
        targets, chosen = select_items(items, seed, per_shape, lemma_cap, share, controls, shape_lemma_cap)
    order = {shape: n for n, shape in enumerate(SHAPES)}
    selected = sorted(targets + chosen, key=lambda i: (order[i["shape"]], ROLES.index(i["role"]), rank_of(seed, "order", i["cand_id"])))
    text_of = {c["cand_id"]: c["text"] for c in read_all_candidates(work)}
    built = []
    for number, item in enumerate(selected, 1):
        item = dict(item, text=text_of[item["cand_id"]])
        built.append(final_sentence(item, tagged[item["sent_key"]], number, tranche))
    problems = [p for s in built for p in sentence_problems(s)]
    if problems:
        raise ValueError("the built set is invalid:\n  " + "\n  ".join(problems[:20]))
    unknown = sorted({s["source_id"] for s in built} - {r["source_id"] for r in draft} - {AUTHORED_ID})
    if unknown:
        raise ValueError("no source row for: " + ", ".join(unknown))
    committed = read_committed_exclusions(data_dir or common.DATA)
    w_rows = excluded_output_rows(work, draft, tranche)
    refused = excluded_problems(work, draft, built, committed, w_rows)
    if refused:
        raise ExcludedSourceError("excluded sources are used:\n  " + "\n  ".join(refused[:20]))
    if tranche == "hard":
        short = quota_problems_hard(targets, chosen, hard.get("floors"), hard.get("caps"), hard.get("control_min", HARD_CONTROL_MIN),
                                    hard.get("control_max", HARD_CONTROL_MAX), lemma_cap, shape_lemma_cap)
    else:
        short = quota_problems(targets, chosen, per_shape, lemma_cap, share, controls, shape_lemma_cap)
    if short and not allow_short:
        raise QuotaError("quotas not met (pass --allow-short to build anyway):\n  " + "\n  ".join(short[:20]))
    excluded = merge_excluded_rows(committed, w_rows) if tranche != "hard" else []        # a conflict refuses before anything is written
    dev_name, sources_name = TRANCHE_FILES[tranche]
    common.write_corpus(os.path.join(out_dir, dev_name), common.with_flag_documentation(HEADERS[tranche], built), built)
    adjudicate.write_rows(os.path.join(out_dir, sources_name), SOURCE_COLUMNS, sources_rows(draft, built))
    report = ["# Build", "", "%d sentences written to %s" % (len(built), dev_name)]
    report += ["WARNING (allowed by --allow-short): " + p for p in short]
    if tranche == "hard":
        return built, report
    target = os.path.join(out_dir, EXCLUDED_OUTPUT_FILE)
    adjudicate.write_rows(target, EXCLUDED_OUTPUT_COLUMNS, excluded)                    # a header-only file when nothing is excluded: never absent
    report.append("%d excluded documents written to %s" % (len(excluded), EXCLUDED_OUTPUT_FILE))
    return built, report


def hard_floors(args):
    """{shape: floor} of the hard tranche: HARD_FLOORS with the `--shape-min SHAPE=N` replacements."""
    return dict(HARD_FLOORS, **parse_shape_option(getattr(args, "shape_min", None), "--shape-min", int, 0))


def hard_caps(args):
    """{shape: cap} of the hard tranche: HARD_CAPS with the `--shape-max SHAPE=N` replacements."""
    return dict(HARD_CAPS, **parse_shape_option(getattr(args, "shape_max", None), "--shape-max", int, 0))


def _command_build(args):
    work = adjudicate.Work(args.work)
    out_dir = args.out_dir or os.path.join(args.data_dir, "eval", "shapes-dev")
    try:
        if args.tranche == "hard":
            if args.per_shape is not None or args.controls is not None or args.share is not None:
                raise ValueError("--per-shape, --controls and --share are standard-tranche options; the hard tranche has --shape-min, "
                                 "--shape-max, --control-min and --control-max")
            options = dict(floors=hard_floors(args), caps=hard_caps(args),
                           control_min=HARD_CONTROL_MIN if args.control_min is None else args.control_min,
                           control_max=HARD_CONTROL_MAX if args.control_max is None else args.control_max)
            built, report = build(work, out_dir, args.seed, lemma_cap=args.lemma_cap, shape_lemma_cap=parse_shape_lemma_cap(args.shape_lemma_cap),
                                  allow_short=args.allow_short, data_dir=args.data_dir, tranche="hard", hard=options)
        else:
            if args.shape_min or args.shape_max or args.control_min is not None or args.control_max is not None:
                raise ValueError("--shape-min, --shape-max, --control-min and --control-max need --tranche hard")
            built, report = build(work, out_dir, args.seed, TARGET_PER_SHAPE if args.per_shape is None else args.per_shape, args.lemma_cap,
                                  AUTHORED_SHARE if args.share is None else args.share, CONTROLS if args.controls is None else args.controls,
                                  parse_shape_lemma_cap(args.shape_lemma_cap), args.allow_short, args.data_dir)
    except (QuotaError, ExcludedSourceError, ValueError) as error:
        print("build refused: %s" % error, file=sys.stderr)
        return 1
    adjudicate._print(report)
    common.write_text(work.private_file("build-report.md"), report)
    return 0


# ---------------------------------------------------------------- report

def band_lines(sentences, items):
    """The q-band table of the hard tranche: per shape and band of q, the target items labeled, kept by both labelers (outcome keep) and
    built into hard.tsv (matched by sentence key and target), and the share of the built items with q < DISAGREE_Q."""
    built = {(s["comments"]["shape"], sent_key_of(s["text"]), int(s["comments"]["target"])) for s in sentences if s["comments"]["role"] == "target"}
    edges = BAND_EDGES
    lines = ["| shape | q band | labeled | kept by both labelers | built |", "| --- | --- | --- | --- | --- |"]
    shares = []
    for shape in HARD_SHAPES:
        members = [i for i in items if i["shape"] == shape and i["role"] == "target" and i.get("q")]
        for low, high in zip(edges, edges[1:]):
            band = [i for i in members if low <= float(i["q"]) < high]
            kept = [i for i in band if i["outcome"] == "keep"]
            lines.append("| %s | [%.2f, %.2f) | %d | %d | %d |" % (shape, low, high, len(band), len(kept),
                                                                  sum(1 for i in kept if (shape, i["sent_key"], i["target"]) in built)))
        built_items = [i for i in members if i["outcome"] == "keep" and (shape, i["sent_key"], i["target"]) in built]
        shares.append("%s %d of %d" % (shape, sum(1 for i in built_items if float(i["q"]) < DISAGREE_Q), len(built_items)))
    lines += ["", "- built targets with q < %.1f (pure disagreement of the Basic model with the shape's class): %s" % (DISAGREE_Q, "; ".join(shares))]
    return lines


def report_lines(out_dir, work=None, tranche="standard", data_dir=None):
    """Markdown counts of the built set for the README: per shape real and authored targets and controls, documents, drops.

    The hard tranche reads hard.tsv, lists the shapes of HARD_SHAPES and n-measure, and adds the q-band table (`band_lines`).
    The drop counts use the merged exclusions (`merged_excluded_sources`, `data_dir` is the data folder of the committed record)."""
    _, sentences = common.read_corpus(os.path.join(out_dir, TRANCHE_FILES[tranche][0]))
    count = collections.Counter()
    for sentence in sentences:
        comments = sentence["comments"]
        count[(comments["shape"], comments["role"], comments["origin"])] += 1
    lines = ["| shape | target items | of which authored | controls | of which authored |", "| --- | --- | --- | --- | --- |"]
    for shape in SHAPES:
        targets = count[(shape, "target", "real")] + count[(shape, "target", "authored")]
        controls = count[(shape, "control", "real")] + count[(shape, "control", "authored")]
        lines.append("| %s | %d | %d | %d | %d |" % (shape, targets, count[(shape, "target", "authored")], controls,
                                                     count[(shape, "control", "authored")]))
    disputed = sum(1 for s in sentences for r in s["rows"] if common.base_status(r[2]) == "disputed")
    tokens = sum(len(s["rows"]) for s in sentences)
    documents = {s["source_id"] for s in sentences}
    lines += ["", "- sentences: %d, tokens: %d, disputed tokens: %d, source documents: %d (authored pseudo-document included)"
              % (len(sentences), tokens, disputed, len(documents))]
    if work is not None and all_works(work, "decided.tsv"):
        items, _ = read_all_decided(work, data_dir)
        drops = collections.Counter((i["shape"], i["reason"]) for i in items if i["outcome"] == "drop")
        lines += ["", "| shape | dropped: target disagreement | dropped: class mismatch | dropped: frame mismatch |", "| --- | --- | --- | --- |"]
        for shape in SHAPES:
            lines.append("| %s | %d | %d | %d |" % (shape, drops[(shape, "target-disagreement")], drops[(shape, "class-mismatch")],
                                                   drops[(shape, "frame-mismatch")]))
        lines += ["", "| shape | role | dropped: excluded source |", "| --- | --- | --- |"]
        excluded_drops = collections.Counter((i["shape"], i["role"]) for i in items if i["reason"] == EXCLUDED_REASON)
        for shape in SHAPES:
            for role in ROLES:
                lines.append("| %s | %s | %d |" % (shape, role, excluded_drops[(shape, role)]))
        if tranche == "hard":
            lines += [""] + band_lines(sentences, items)
    return lines


def _command_report(args):
    out_dir = args.out_dir or os.path.join(args.data_dir, "eval", "shapes-dev")
    adjudicate._print(report_lines(out_dir, adjudicate.Work(args.work), args.tranche, args.data_dir))
    return 0


# ---------------------------------------------------------------- command line

def main(argv=None):
    parser = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    parser.add_argument("--work", required=True, help="work folder (outside the repository)")
    parser.add_argument("--data-dir", default=common.DATA, help="data folder (default: the repository's data/)")
    parser.add_argument("--tranche", choices=TRANCHES, default="standard",
                        help="profile: standard (data/eval/shapes-dev/dev.tsv, default) or hard (hard.tsv; the contested band of the Basic model)")
    sub = parser.add_subparsers(dest="command", required=True)
    exclusions = sub.add_parser("exclusions", help="index of the documents training and evaluation used; checks the draft")
    exclusions.add_argument("--check", nargs="*", default=None, help="document ids or URLs to check")
    tokenize = sub.add_parser("tokenize", help="tokens of every sentence through the C# command shapes-tokenize")
    tokenize.add_argument("--cmd", default=None, help="command prefix that is followed by shapes-tokenize --in F --out F")
    candidates = sub.add_parser("candidates", help="pre-select the candidates of every shape; apply the leakage filters")
    candidates.add_argument("--no-ud", action="store_true", help="ignore the UD cache even when present")
    candidates.add_argument("--target", type=int, default=TARGET_PER_SHAPE)
    candidates.add_argument("--over", type=float, default=OVER_COLLECT)
    candidates.add_argument("--lemma-cap", type=int, default=LEMMA_CAP)
    candidates.add_argument("--shape-over", action="append", default=None, metavar="SHAPE=FACTOR",
                            help="over-collection factor of one shape (repeatable); a thin shape takes its whole pool with a large factor")
    candidates.add_argument("--shape-lemma-cap", action="append", default=None, metavar="SHAPE=N",
                            help="lemma cap of one shape (repeatable), replacing --lemma-cap for a thin shape")
    candidates.add_argument("--control-pool", type=int, default=None,
                            help="control candidates per shape (default %d; hard tranche %d)" % (CONTROL_POOL, HARD_CONTROL_POOL))
    candidates.add_argument("--seed", type=int, default=SEED)
    candidates.add_argument("--only-shapes", default=None, metavar="SHAPE[,SHAPE]", help="pre-select only these shapes")
    candidates.add_argument("--no-controls", action="store_true", help="write no control candidates")
    candidates.add_argument("--controls-only", action="store_true",
                            help="hard tranche, with --round: write only controls, for the lemmas of the targets the build selects today "
                                 "(from the decided earlier rounds; uses --shape-max and --shape-lemma-cap like build); no target is added")
    candidates.add_argument("--shape-max", action="append", default=None, metavar="SHAPE=N", help="with --controls-only: as in the build")
    candidates.add_argument("--compound-strict", action="store_true",
                            help="n-compound-head: the token before the head must be a noun-like modifier (see noun_like_modifier); always on in the hard tranche")
    candidates.add_argument("--budget", action="append", default=None, metavar="SHAPE=N|all",
                            help="hard tranche only: number of candidates of a shape (repeatable; default n-compound-head 400, v-after-modal 300, "
                                 "the other shapes all)")
    export = sub.add_parser("export", help="write the blind batches, the key and status.tsv")
    export.add_argument("--size", type=int, default=adjudicate.BATCH_SENTENCES)
    export.add_argument("--rows", type=int, default=adjudicate.BATCH_ROWS)
    export.add_argument("--seed", type=int, default=SEED)
    export.add_argument("--force", action="store_true")
    check = sub.add_parser("check-batches", help="re-read the batches: 3 columns, tokens equal the key, no leak")
    check.add_argument("--rows", type=int, default=adjudicate.BATCH_ROWS)
    check.add_argument("--limit", type=int, default=20)
    validate = sub.add_parser("validate-output", help="validate tagger outputs and record the state in status.tsv")
    validate.add_argument("--batch", default=None)
    validate.add_argument("--model", choices=adjudicate.MODELS, default=None)
    validate.add_argument("--all", action="store_true")
    validate.add_argument("--no-count", action="store_true")
    validate.add_argument("--limit", type=int, default=5)
    status_parser = sub.add_parser("status", help="batch states and the presence of the pipeline files")
    decide_parser = sub.add_parser("decide", help="apply the decision rule; write private/decided.tsv and decided-rows.tsv")
    sample = sub.add_parser("sample", help="the review sample for the precision gate")
    sample.add_argument("--seed", type=int, default=SEED)
    sample.add_argument("--per-shape", type=int, default=None, help="items per shape (default 12; hard tranche 15)")
    sample.add_argument("--controls", type=int, default=4)
    sample.add_argument("--low-q-share", type=float, default=0.5, help="hard tranche: least share of a shape's items with q < 0.5 (default %(default)s)")
    sample.add_argument("--lemma-cap", type=int, default=LEMMA_CAP, help="hard tranche: the lemma cap of the build")
    sample.add_argument("--shape-lemma-cap", action="append", default=None, metavar="SHAPE=N", help="hard tranche: as in the build")
    sample.add_argument("--shape-max", action="append", default=None, metavar="SHAPE=N", help="hard tranche: as in the build")
    review = sub.add_parser("review-result", help="precision of the review; exit 1 below 95%")
    review.add_argument("--verdicts", nargs="+", required=True)
    build_parser = sub.add_parser("build", help="write dev.tsv and sources.tsv (hard.tsv and hard-sources.tsv with --tranche hard)")
    build_parser.add_argument("--out-dir", default=None, help="default: <data dir>/eval/shapes-dev")
    build_parser.add_argument("--seed", type=int, default=SEED)
    build_parser.add_argument("--per-shape", type=int, default=None, help="standard tranche: targets per shape (default %d)" % TARGET_PER_SHAPE)
    build_parser.add_argument("--lemma-cap", type=int, default=LEMMA_CAP)
    build_parser.add_argument("--shape-lemma-cap", action="append", default=None, metavar="SHAPE=N",
                              help="lemma cap of one shape (repeatable); use the same values as the candidates command")
    build_parser.add_argument("--share", type=float, default=None,
                              help="standard tranche: largest authored share of a shape and of the controls (default %s)" % AUTHORED_SHARE)
    build_parser.add_argument("--controls", type=int, default=None, help="standard tranche: number of controls (default %d)" % CONTROLS)
    build_parser.add_argument("--shape-min", action="append", default=None, metavar="SHAPE=N",
                              help="hard tranche: floor of a shape (default n-compound-head 80, n-subject-initial 40, v-imperative-initial 70, "
                                   "n-measure 0, v-after-modal 80)")
    build_parser.add_argument("--shape-max", action="append", default=None, metavar="SHAPE=N",
                              help="hard tranche: cap of a shape (default 100, n-measure 0)")
    build_parser.add_argument("--control-min", type=int, default=None, help="hard tranche: floor of the controls (default %d)" % HARD_CONTROL_MIN)
    build_parser.add_argument("--control-max", type=int, default=None, help="hard tranche: cap of the controls (default %d)" % HARD_CONTROL_MAX)
    build_parser.add_argument("--allow-short", action="store_true",
                              help="build although a quota (floors and caps, lemma caps; standard: counts and authored share) is not met; the report lists it")
    report = sub.add_parser("report", help="counts of the built set for the README")
    report.add_argument("--out-dir", default=None)
    for round_parser in (candidates, export, check, validate, status_parser, decide_parser):
        round_parser.add_argument("--round", default=None, metavar="NAME",
                                  help="work in W/rounds/NAME instead of the root of the work folder (shared inputs stay in W)")
    args = parser.parse_args(argv)
    commands = {"exclusions": _command_exclusions, "tokenize": _command_tokenize, "candidates": _command_candidates,
                "export": _command_export, "check-batches": _command_check_batches, "validate-output": _command_validate_output,
                "status": _command_status, "decide": _command_decide, "sample": _command_sample, "review-result": _command_review_result,
                "build": _command_build, "report": _command_report}
    if args.data_dir != common.DATA:
        common.DATA = args.data_dir
    if args.command in EXCLUSION_COMMANDS:
        try:
            read_committed_exclusions(args.data_dir)                       # fail closed: a missing or malformed committed record refuses
        except ValueError as error:
            print("%s refused: %s" % (args.command, error), file=sys.stderr)
            return 1
    return commands[args.command](args)


if __name__ == "__main__":
    sys.exit(main())

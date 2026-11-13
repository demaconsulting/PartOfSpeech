"""Builder of the technical-text data of beta.3 phase 1 (Python 3, standard library only; imports the repository's data tools read-only).

Phase 1 of beta.3 adds four failure shapes of technical English, each as one development tranche and one weight-0 training source, built from NEW US federal
public-domain documents, none of them used before. It extends the pipeline of PR #20 (followup_data.py) and does not edit it: this module imports it and reuses its
frozen labeler template, partition key, caps, hand-check helpers and the data tools of PR #19 and #20, and adds the products below.

  S1 deverbal compound heads (a noun spelled like a verb heads a noun compound: `the range check`, `the leak testing`)
       development  `deverbal-heads`   data/eval/shapes-dev/deverbal-heads*.tsv   shape n-deverbal-head (class scoring), kinds pred | num | ing | other (targets), verb (control)
       training     `deverbal`         data/technical/deverbal*.tsv               strata pred | num | ing | other, contrast stratum verb
  S2 capitalized defined terms in the middle of a sentence (`select the Manual Override mode`; ordinary words NOUN, ADJ for a modifier; names PROPN)
       development  `defined-terms`    data/eval/shapes-dev/defined-terms*.tsv    shape term-capitalized (exact tags), kinds noun | adj (targets), proper (control)
       training     `terms`            data/technical/terms*.tsv                  strata noun | adj, contrast stratum proper
  S3 capitalized imperatives at the start of a step line (`Check oil level`, `Open valve 3`)
       development  `step-lines`       data/eval/shapes-dev/step-lines*.tsv       shape step-line (exact tags), kind imperative (target), noun (control)
       training     `steps`            data/technical/steps*.tsv                  strata imperative, contrast stratum noun-initial
  S4 base-form ambiguity in a clause (`the valves open when the pressure drops`)
       development  `clause-verbs`     data/eval/shapes-dev/clause-verbs*.tsv     shape v-clause-ambiguous (exact tags), kinds main | sub (targets), noun | adj (controls)
       training     `clauses`          data/technical/clauses*.tsv                strata main | sub, contrast strata noun | adj

No model of this project is used: the tool never calls a tagger, reads no weights and no prediction, and a data test scans its source for that. The only non-frame
input is the Basic mass of the WordSense library (the pinned `hmm.tsv`), for the development band of S1, S3 and S4 (S4 without an upper bound: amendment A1, rule item 5), as in PR #18 and #20. Frames are position and lexicon
only (technical_frames.py, which holds the definition of every frame). The blind labeling is done by the frozen commands of shapes_dev.py (`export`, `check-batches`,
`validate-output`, `status`, `decide` with `--round`); `decide` adds one report row per technical shape that is present.

THE PRE-REGISTERED RULE (committed with the tool before any document of this phase is fetched; the normative text is this docstring, the short form is in the trainer
RUN-LOG.md, section "beta.3 phase 1: pre-registered rule (before any fetch)")

1. Documents. Only US federal authorship with a public-domain basis checked one by one and written in the `license_basis` cell (17 U.S.C. 105, the agency named in the record,
   the record's own rights statement, NTRS rights record for NASA). No FAA material, nothing in either excluded-sources.tsv (the committed one and W/excluded-sources.tsv),
   nothing in any excluded-documents.tsv, no document with a state, local, foreign, university, contractor or private creator, publisher or co-author (the author, creator and
   publisher fields of every record are read, not only the title), no copyright notice or third-party credit, no share-alike or restricted text, no Wikipedia, no UD text, no
   sentence supplied by any consumer (no consumer text is read, quoted or used). Military manuals: a distribution statement other than public release, FOUO or an export-control or
   classification marking is a reason to reject. The used-document index holds ALL earlier tables: the open, gold v2, silver and MASC tables, all five shapes-dev tables, the
   imperative source, data/compound and data/headings (sources.tsv, excluded-documents.tsv and, for PR #20, compound-2-sources.tsv, compound-2-excluded-documents.tsv,
   headings-2-sources.tsv, headings-2-excluded-documents.tsv), the tables of the two PR #20 tranches compound-head-2 and headings-2, and every `--extra-table` (the earlier sourcing
   drafts, the frozen copies of PR #20, the beta.3 by-hand exclusions); a document is rejected as a whole when 3 or more of its sentences equal a training, evaluation or
   shapes-dev sentence (the held-out sets enter only as the opaque hash set of `shapes_dev.LeakageFilter`). The four new products never reject their own earlier output.
2. Partition (before any label). Every document gets a family key (the lower-case title without volume, edition, year, series number and `skill level`; two scans of one
   publication share it). The side of a family is `dev` when the seeded rank `rank_of(20261110, "partition", family key)` read as a fraction of 16**12 is below the DEV share
   0.40 (fixed in the R1 commit and never changed afterwards). All members of a family are on one side. DEV documents feed the four development tranches only, TRAIN documents the
   four training sources only; no document is on both sides. The result is W/partition.tsv (W/streams.tsv, the sourcing stream P, E, M or G of each document, is optional and
   only reported).
3. Units. Sentences are the cleaned sentences of W/text of 3 to 30 tokens through the unchanged cleanliness, duplicate, leakage and probe filters of
   `shapes_dev_extra.scan_units`. Step units (S3) are `technical_frames.step_lines(raw)` (enumerated lines, bulleted lines and short stand-alone lines of the OCR text) and the
   sentence units, written by `steps` to W/steps with the unit kind in W/step-kinds.tsv, of 2 to 30 tokens, through the same filters with the noise rules of
   `technical_frames.step_noise_reason` (sentence case and Title Case allowed, a capital first letter required). ALL-CAPS state labels and ALL-CAPS lines are out of scope.
4. Frames (position and lexicon only; the word lists and strata are fixed in technical_frames.py). One candidate per sentence, the rarest stratum takes it: S1 pred, num, ing, other
   (and the verb control frames); S2 proper, adj, noun; S3 noun (caption control), imperative; S4 adj, noun (controls), sub, main. The judged word is the candidate token.
   AMENDMENT A2 (before any label other than the S2 pilot; an implementing-agent decision, not the owner's): the S4 target frame also takes a verb V (same word, subject and context conditions)
   that is followed by a lower-case noun or by an adjective and noun phrase, a direct object without a determiner (`Engineers check pressure gauges.`, `The valves control flow.`), with the
   reported flag `np` (`technical_frames.clause_verb_hit`, `ClauseHit.np`), because the original frame refused every verb followed by a noun-capable word (to keep it apart from the noun control
   frame) and so refused the most typical verb-object clauses. Still position and lexicon only, one candidate per sentence, strata main and sub unchanged. The controls are unchanged (the noun
   and adjective control frames stay; a control is kept only when both labelers tag V NOUN or ADJ); a sentence of the noun control frame that is also an `np` frame is offered to both kinds and
   `candidate_rows` draws the controls first and never gives one sentence to both. An `np` target that both labelers tag NOUN is a class-mismatch drop, never a control. The labelers' exact tag
   alone decides the kind, so the wider frame only widens the pool of candidates. Pins: none covers a frame (DRAFT, TOKENS, HMM and CONVENTIONS are unchanged).
5. Candidates (seeded order `rank_of(20261110, ...)`; the product and the stratum are in the label and in the candidate id prefix, for example `dh-pred-c00001`). Development targets
   of S1, S3 and S4 lie in the contested band 0.10 <= q < 0.97 of the pinned WordSense Basic mass (class Noun for S1, Verb for S3 and S4), q ascending, hardest first; S2, all
   controls and all training candidates use no band.
   AMENDMENT A1 (before any label other than the S2 pilot; an implementing-agent decision, not the owner's): for the S4 development targets ONLY the band has no upper bound
   (0.10 <= q; `in_band`, `LOWER_BOUND_ONLY`), because the upper bound kept 78 of 1,034 frame hits (the verb frame after a plural subject is mostly easy for the Basic model). The
   candidates stay in q-ascending order, hardest first, so the contested cases still come first and the budgets and caps decide how many easier ones follow. S1 and S3 keep
   0.10 <= q < 0.97. The counts that motivated it (`bands`, counts only) are in the RUN-LOG.
   AMENDMENT A2 (item 4) leaves the budgets, caps and the band rule of S4 as they are; the `np` candidates count in the stratum budgets main and sub, and the controls of the round are drawn first.
   Candidate budgets (None = every candidate that the caps allow): development S1 pred None, num None, ing 450, other 200, verb controls 160
   (first round, for the lemmas of the target candidates) and 30 (round dvhc); S2 noun 520, adj 260, proper 110 and 30 (dtmc), the right-cue, document-type and left-cue tiers T1 to T3 before
   T4, T4 at most 20 percent of a stratum; S3 imperative 480, noun 110 and 30 (stpc); S4 main 600, sub 200, noun 110, adj 60 and 40 + 20 (clvc). Training (lean volume, decision O5):
   S1 1,200 (pred 300, num 250, ing 350, other 150, verb 150), S2 2,000 (noun 1,300, adj 450, proper 250), S3 1,500 (imperative 1,100, noun 400), S4 1,000 (main 500, sub 250, noun 150, adj 100).
   Candidate lemma caps equal the build caps for the development products (S1 4 over the union with the targets of compound-head.tsv and compound-head-2.tsv, S2 4, S3 6, S4 4; controls 2) and 6 for
   the training products; documents 20 (development) or 40 (training); S2 2 (development) or 3 (training) per distinct lower-case term. A sentence that is a candidate of an earlier round on its side
   (fixed order: dtm-pilot, dvh, dtm, stp, clv, then the control rounds dvhc, dtmc, stpc, clvc; for the training side dtm-pilot, dvt, dtt, stt, clt) is not drawn again, a round is refused while a
   later round already has candidates, and a build refuses a sentence that another built product of its side holds: the four tranches (and the four sources) share no sentence.
6. S2 pilot and gate. The pilot (round dtm-pilot) is 100 candidates of the S2 frames drawn from the documents named by `--documents` (the first wave) on both sides in the seeded order
   `rank_of(20261110, "dtm-pilot", ...)`: noun 70, adj 20, proper 10, at most 4 per lemma, 2 per term, 15 per document and 20 per title family, T4 at most 20 percent of the noun stratum. Both blind
   labelers tag it. Let n be the pilot candidates of stratum noun; a candidate is CONFIRMED when both labelers tag the judged word NOUN. Gate: n of at least 60 and confirmed / n of at least 0.40 (2 of 5)
   proceeds; fewer than 60 is a scarcity stop, a lower ratio STOPS S2 (the record is counts only: shares of ADJ and PROPN and the tiers are reported, not gated). A failed or missing gate refuses every
   S2 command that draws or builds (`candidates`, `build` of defined-terms and terms); the other three shapes do not depend on it (decision O2: S2 is then a disclosed shortfall, nothing is registered for
   it). The pilot lines keep the side of their document, are not labeled again and count against the caps of their side.
7. Labels. Two blind labelers, `claude-opus-5.5` and `gpt-6.1-sol`, from scratch, the frozen prompt of PR #19 (fu.LABELER_TEMPLATE and its appendix, `prompt` command), reading the pinned
   data/CONVENTIONS.md and their batch only, one agent per labeler and batch, at most 4 at a time, no re-roll, through the unchanged shapes_dev.py commands. Training: every token agreed, no
   fix-tier violation, the judged word with the exact tag of its stratum (class Noun or Verb for S1), the frame still holding after labeling; anything else is dropped and counted, nothing is edited by
   hand. Development: the judged word agreed with the exact tag of its kind (the class for S1), the frame after labeling (`frame_mismatch`), the judged word never disputed.
8. Caps and floors (seeded order). Development (floor to cap): S1 targets 240 to 360 (pred 40 to 100, num 30 to 60, ing 80 to 140, other at most 100), controls 50 to 90; S2 noun 120 to 200, adj 40 to 80,
   proper controls 40 to 70; S3 imperative 180 to 300, noun controls 40 to 70; S4 targets 240 to 360 (sub at least 60), controls 50 to 90 (noun 35 to 60, adj 15 to 30). 4 per lemma (S1 over the union with the old
   targets; S3 6), 2 per control lemma, 15 per document, S2 2 per term, S3 30 per title family. Training: S1 450 to 800 sentences (pred at least 100, num at least 50, ing at least 150, verb at most 15 percent,
   4 per lemma, 20 per document); S2 500 to 900 (proper 15 to 25 percent, 4 per lemma, 2 per term); S3 600 to 1,000 (noun-initial at most 30 percent, 6 per lemma, at least 30 percent of the imperatives with a bare
   object and at least 50 percent from enumerated or bulleted lines); S4 450 to 800 (noun and adj together at most 20 percent, 4 per lemma). The totals of S1 and S4 follow the lean volume (decision O5).
   `build` refuses below a floor unless `--allow-short` (the miss is disclosed, never padded) and never waives a cap.
   AMENDMENT A2 (item 4): the kept S4 targets with the flag `np` are at most 60 percent of the kept targets of the product (development and training; `NP_SHARE_MAX_PERCENT`, `with_np_cap`, drop
   reason `np-cap`: the np targets are taken in the order of the selection up to the largest number that keeps the share, the others are never removed) so that the original frame stays
   represented; the share is reported by `candidates` and `build`. The S4 floors and caps above are unchanged.
9. Hand check (a model review, not a human review). `claude-sonnet-5.5`, 60 kept items and 4 swapped-tag controls per product, one draw with the seeds 20261201 to 20261208 in the order of the eight products
   (SAMPLE_PLAN holds the split by kind). Gate: marked-word precision at least 95 percent (3 incorrect of 60 passes, 4 fail), at most 1 of 4 controls accepted, no unanswered item; below it the product is not registered.
10. Format and amendments as in followup_data.py (block format of data/open/train.tsv; ids `shapes-dev-deverbal-heads-NNNN`, `shapes-dev-defined-terms-NNNN`, `shapes-dev-step-lines-NNNN`,
   `shapes-dev-clause-verbs-NNNN`, `deverbal-NNNN`, `terms-NNNN`, `steps-NNNN`, `clauses-NNNN`; `--omit-document ID[=REASON]` removes a document found unsound after the caps, nothing is added and the ids stay
   without a gap; the four training builds take the same `--omit-document` options because they write one shared excluded-documents.tsv). The pins DRAFT_SHA256, TOKENS_SHA256, HMM_SHA256 and CONVENTIONS_SHA256 are
   set (the R1 commit): `candidates` and `pilot` refuse any input whose SHA-256 is not the pinned one (the pin of hmm.tsv for the banded development products only).

Work folder W (`--work W`, outside the repository): the layout of followup_data.py plus W/steps/<id>.txt and W/step-kinds.tsv (the `steps` command) and the rounds dtm-pilot, dvh, dtm, stp, clv, dvhc, dtmc,
stpc, clvc, dvt, dtt, stt and clt; each round folder has private/extras.tsv (frame, unit kind, tier, term and flag of every candidate, written with the candidates and read by `build`).

Commands (`--data-dir D` replaces the repository's data folder, for tests; --extra-table and --extra-text as in imperative_data.py):

  python tools/data/technical_data.py --work W exclusions [--extra-table PATH ...] [--check ID_OR_URL ...]
  python tools/data/technical_data.py --work W partition
  python tools/data/technical_data.py --work W lines                                     # heading lines (unused by the four shapes, kept for the layout)
  python tools/data/technical_data.py --work W steps                                     # step units of every document
  python tools/data/technical_data.py --work W tokenize [--cmd COMMAND]
  python tools/data/technical_data.py --work W preflight [--extra-table PATH ...] [--extra-text DIR ...] [--no-ud]       # counts only
  python tools/data/technical_data.py --work W bands [--extra-table PATH ...] [--extra-text DIR ...] [--no-ud]           # counts only: S1, S3, S4 development target hits by band of q
  python tools/data/technical_data.py --work W pilot [--documents FILE] [--extra-table PATH ...] [--no-ud]               # round dtm-pilot (S2 only)
  python tools/data/technical_data.py --work W pilot-result                              # the S2 gate; exit 1 = S2 stops (the other products go on)
  python tools/data/technical_data.py --work W candidates --product P [--round dvhc|dtmc|stpc|clvc] [--extra-table PATH ...] [--no-ud]
  (then: shapes_dev.py --work W export / check-batches / validate-output / status / decide --round R)
  python tools/data/technical_data.py --work W build --product P [--out-dir D] [--allow-short] [--omit-document ID[=REASON] ...]
  python tools/data/technical_data.py --work W sample --product P [--out-dir D] ; review-result --product P --verdicts FILE ... ; report --product P
  python tools/data/technical_data.py --work W prompt --round R --batch B --model M      # the frozen labeler prompt and its appendix
"""
import argparse
import collections
import math
import os
import random
import re
import sys

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import adjudicate
import common
import compound_heading_data as chd
import followup_data as fu
import imperative_data
import shapes_dev
import shapes_dev_extra as sx
import technical_frames as tf

SEED = shapes_dev.SEED
DEV_PRODUCTS = ("deverbal-heads", "defined-terms", "step-lines", "clause-verbs")
TRAIN_PRODUCTS = ("deverbal", "terms", "steps", "clauses")
PRODUCTS = DEV_PRODUCTS + TRAIN_PRODUCTS
SIDE = {product: ("dev" if product in DEV_PRODUCTS else "train") for product in PRODUCTS}
GROUP = dict(zip(DEV_PRODUCTS + TRAIN_PRODUCTS, TRAIN_PRODUCTS + TRAIN_PRODUCTS))     # the shape of a product: the name of its training source
TWIN = dict(zip(DEV_PRODUCTS, TRAIN_PRODUCTS))
OUTPUT = {product: ("eval/shapes-dev", product + ".tsv", product + "-sources.tsv") for product in DEV_PRODUCTS}
OUTPUT.update({product: ("technical", product + ".tsv", product + "-sources.tsv") for product in TRAIN_PRODUCTS})
EXCLUDED_OUT = "excluded-documents.tsv"                                          # one record shared by the four training sources
DROPPED_OUT = {product: product + "-dropped-sentences.tsv" for product in TRAIN_PRODUCTS}
SENT_ID = {product: ("shapes-dev-%s-%%04d" % product if SIDE[product] == "dev" else product + "-%04d") for product in PRODUCTS}
# The four development tranches that this tool builds: its filter leaves them out of its held-out keys so that a rebuild never sees its own earlier output
# (a test pins the value to leakage.SHAPES_DEV_TECHNICAL_TRANCHES; this module never imports `leakage`). The two PR #20 tranches stay in the keys.
TECHNICAL_TRANCHES = DEV_PRODUCTS
MODEL_FREE = "no PartOfSpeech model, prediction or score is read"

# ---- the kinds (strata) of every shape, in the order of the candidate priority, and what they are
KINDS = {"deverbal": ("pred", "num", "ing", "other", "verb"), "terms": ("noun", "adj", "proper"), "steps": ("imperative", "noun"),
         "clauses": ("main", "sub", "noun", "adj")}
CONTROL_KINDS = {"deverbal": ("verb",), "terms": ("proper",), "steps": ("noun",), "clauses": ("noun", "adj")}
TARGET_KINDS = {group: tuple(k for k in kinds if k not in CONTROL_KINDS[group]) for group, kinds in KINDS.items()}
PRIORITY = {"deverbal": ("pred", "num", "ing", "other"), "terms": ("proper", "adj", "noun"), "steps": ("noun", "imperative"), "clauses": ("adj", "noun", "sub", "main")}
BUILD_ORDER = {"deverbal": ("pred", "num", "ing", "other"), "terms": ("adj", "noun"), "steps": ("imperative",), "clauses": ("sub", "main")}
SHAPE_OF_KIND = {"deverbal": {kind: "n-deverbal-head" for kind in KINDS["deverbal"]},
                 "terms": {"noun": "term-noun", "adj": "term-adj", "proper": "term-proper"},
                 "steps": {"imperative": "step-imperative", "noun": "step-noun"},
                 "clauses": {"main": "clause-verb", "sub": "clause-verb", "noun": "clause-noun", "adj": "clause-adj"}}
TRANCHE_SHAPE = {"deverbal": "n-deverbal-head", "terms": "term-capitalized", "steps": "step-line", "clauses": "v-clause-ambiguous"}
NOUN_TAGS = ("NOUN", "PROPN")
EXPECTED_TAGS = {"deverbal": {"pred": NOUN_TAGS, "num": NOUN_TAGS, "ing": NOUN_TAGS, "other": NOUN_TAGS, "verb": ("VERB",)},
                 "terms": {"noun": ("NOUN",), "adj": ("ADJ",), "proper": ("PROPN",)},
                 "steps": {"imperative": ("VERB",), "noun": ("NOUN",)},
                 "clauses": {"main": ("VERB",), "sub": ("VERB",), "noun": ("NOUN",), "adj": ("ADJ",)}}
STRATUM_LABEL = {"steps": {"noun": "noun-initial"}}                               # the name of a kind in the `# stratum` line of a TRAINING source
LABELED_UNITS = ("enumerated", "bulleted")                                        # step units that carry a label (an imperative context)
BANDED = {"deverbal-heads": "Noun", "step-lines": "Verb", "clause-verbs": "Verb"}   # the development products with the Basic band, and the class of the mass
LOWER_BOUND_ONLY = ("clause-verbs",)        # AMENDMENT A1: the development band of these products has the lower bound 0.10 only (no upper bound); S1 and S3 keep 0.10 <= q < 0.97
Q_BAND_EDGES = (sx.BAND_MIN, 0.50, 0.90, sx.BAND_MAX, 0.995)                       # the counting bands of `bands`: below 0.10, [0.10, 0.50), [0.50, 0.90), [0.90, 0.97), [0.97, 0.995), 0.995 and more
Q_BAND_LABELS = ("<0.10", "0.10-0.50", "0.50-0.90", "0.90-0.97", "0.97-0.995", ">=0.995")


def in_band(product, q):
    """True when the Basic mass `q` of a development target of `product` is eligible (rule 5): 0.10 <= q < 0.97, and for the products of LOWER_BOUND_ONLY (amendment A1) 0.10 <= q."""
    return sx.BAND_MIN <= q and (product in LOWER_BOUND_ONLY or q < sx.BAND_MAX)


def q_band(q):
    """The index (into Q_BAND_LABELS) of the counting band of a mass `q` (counts only; the bands do not decide anything)."""
    return sum(1 for edge in Q_BAND_EDGES if q >= edge)

# ---- rounds, candidate ids
PILOT_ROUND = "dtm-pilot"
CANDIDATE_ROUND = {"deverbal-heads": "dvh", "defined-terms": "dtm", "step-lines": "stp", "clause-verbs": "clv",
                   "deverbal": "dvt", "terms": "dtt", "steps": "stt", "clauses": "clt"}
CONTROLS_ROUND = {product: CANDIDATE_ROUND[product] + "c" for product in DEV_PRODUCTS}
ROUNDS = {product: (CANDIDATE_ROUND[product], CONTROLS_ROUND[product]) for product in DEV_PRODUCTS}
ROUNDS.update({product: (CANDIDATE_ROUND[product],) for product in TRAIN_PRODUCTS})
ROUNDS["defined-terms"] = (PILOT_ROUND,) + ROUNDS["defined-terms"]
ROUNDS["terms"] = (PILOT_ROUND,) + ROUNDS["terms"]
# the fixed order of the rounds of a side: a round never draws a sentence of an earlier one, and is refused while a later one has candidates
ROUND_ORDER = {"dev": (PILOT_ROUND, "dvh", "dtm", "stp", "clv", "dvhc", "dtmc", "stpc", "clvc"), "train": (PILOT_ROUND, "dvt", "dtt", "stt", "clt")}
PREFIX_BASE = {"deverbal-heads": "dh", "defined-terms": "dt", "step-lines": "sl", "clause-verbs": "cv", "deverbal": "dvt", "terms": "dtt", "steps": "stt", "clauses": "clt"}
PILOT_PREFIX_BASE = "dp"
PILOT_KINDS = ("noun", "adj", "proper")


def prefix_of(product, kind, controls_round=False, pilot=False):
    """The candidate id prefix of a kind: `<base>-<kind>-` (`dh-pred-`), `<base>c-<kind>-` in a control top-up round (`dhc-verb-`), `dp-<kind>-` in the pilot."""
    base = PILOT_PREFIX_BASE if pilot else PREFIX_BASE[product] + ("c" if controls_round else "")
    return "%s-%s-" % (base, kind)


def _prefix_table():
    """{prefix: (group, kind)} of every candidate id prefix (a candidate id is `<prefix>c<number>`)."""
    table = {}
    for product in PRODUCTS:
        for kind in KINDS[GROUP[product]]:
            for controls in (False, True):
                if controls and not (SIDE[product] == "dev" and kind in CONTROL_KINDS[GROUP[product]]):
                    continue
                table[prefix_of(product, kind, controls)] = (GROUP[product], kind)
    for kind in PILOT_KINDS:
        table[prefix_of("defined-terms", kind, pilot=True)] = ("terms", kind)
    return table


KIND_OF_PREFIX = _prefix_table()
CAND_ID = re.compile(r"^(?P<prefix>.+-)c[0-9]{5,}$")


def kind_of_cand_id(cand_id):
    """(group, kind) of a candidate id; ValueError for an id that this tool did not write."""
    match = CAND_ID.match(cand_id)
    if not match or match.group("prefix") not in KIND_OF_PREFIX:
        raise ValueError("not a candidate id of the technical-text rounds: " + cand_id)
    return KIND_OF_PREFIX[match.group("prefix")]


def kind_label(product, kind):
    """The name of a kind as written in a built file (the contrast stratum of the steps source is `noun-initial`)."""
    return STRATUM_LABEL.get(GROUP[product], {}).get(kind, kind) if SIDE[product] == "train" else kind


def kind_of_label(product, label):
    """The kind of a name written in a built file (inverse of `kind_label`)."""
    for kind in KINDS[GROUP[product]]:
        if kind_label(product, kind) == label:
            return kind
    return label


# ---- pins of the inputs (set by the R1 commit; `require_pins` refuses candidate and pilot commands unless every pinned input still has these bytes)
# R0 (the pre-registered rule, before any fetch) had no pin set. R1 froze the sources-draft.tsv of the sourced documents (451 documents), its tokens.tsv (373,490 units, with the step units),
# the pinned WordSense Basic mass (`hmm.tsv`, WordSense commit 6530de19..., DLL b96e0055..., made by tools/data/hmm-tags from tokens.tsv: 357,270 tagged sentences) and data/CONVENTIONS.md
# as the labelers read it (the section "Capitalized Defined Terms" of the commit 67d7dc2); the convention file must not change after the pin.
# R1 pins (commit 68fd57c): DRAFT c03a3975..., TOKENS aebca046..., HMM 71fa239d... . The commit C6 (amendment A1 and the DEV top-up of 121 documents) re-froze the three inputs below before any
# label other than the S2 pilot: 572 documents (the 451 old rows and texts byte-identical, 121 DEV documents appended), 459,396 units, 439,507 tagged sentences; CONVENTIONS_SHA256 is unchanged.
DRAFT_SHA256 = "b48d463364275182f4e704625d069b9c9ef06f48ca068405925c06ab0be142ae"
TOKENS_SHA256 = "dfe0ba191496d53727c1dcea8d2d5e0841a7885a4ad9c489e7f8b81f6f7ef4aa"
HMM_SHA256 = "43432b1a2522bf6e85b7a6e380dfaa4d94a5b26a066e94fa0b8c127c9bf602f7"
CONVENTIONS_SHA256 = "42da154c60272dc9a105c5496fa3c8797cffa81cfb55d34505497b33520e5bb2"

# ---- partition
DEV_SHARE = 0.40
STREAMS = ("P", "E", "M", "G")                  # procedural, engineering, modern manuals, general (sourcing streams; reported only)
PARTITION_FILE, STREAMS_FILE, STEP_KINDS_FILE, EXTRAS_FILE = fu.PARTITION_FILE, fu.STREAMS_FILE, "step-kinds.tsv", "extras.tsv"
EXTRA_COLUMNS = ("cand_id", "frame", "unit", "tier", "term", "flag")

# ---- candidate budgets (None = every candidate that the caps allow), caps and size gates
CANDIDATE_BUDGET = {
    "deverbal-heads": {"pred": None, "num": None, "ing": 450, "other": 200, "verb": 160},
    "defined-terms": {"noun": 520, "adj": 260, "proper": 110},
    "step-lines": {"imperative": 480, "noun": 110},
    "clause-verbs": {"main": 600, "sub": 200, "noun": 110, "adj": 60},
    # training, lean volume (decision O5): S1 and S4 budgets halved
    "deverbal": {"pred": 300, "num": 250, "ing": 350, "other": 150, "verb": 150},
    "terms": {"noun": 1300, "adj": 450, "proper": 250},
    "steps": {"imperative": 1100, "noun": 400},
    "clauses": {"main": 500, "sub": 250, "noun": 150, "adj": 100}}
CONTROL_TOPUP = {"deverbal-heads": {"verb": 30}, "defined-terms": {"proper": 30}, "step-lines": {"noun": 30}, "clause-verbs": {"noun": 40, "adj": 20}}
LEMMA_CAP = {"deverbal-heads": 4, "defined-terms": 4, "step-lines": 6, "clause-verbs": 4, "deverbal": 4, "terms": 4, "steps": 6, "clauses": 4}
CANDIDATE_LEMMA_CAP = dict(LEMMA_CAP, **{product: 6 for product in TRAIN_PRODUCTS})
CONTROL_LEMMA_CAP = 2
DOCUMENT_CAP = {"dev": 15, "train": 20}
CANDIDATE_DOCUMENT_CAP = {"dev": 20, "train": 40}
TERM_CAP = {"dev": 2, "train": 2}
CANDIDATE_TERM_CAP = {"dev": 2, "train": 3}
FAMILY_CAP = 30                                  # S3 development: items per title family
TIER4_SHARE_PERCENT = 20                         # S2: T4 candidates at most this share of a stratum budget
NP_SHARE_MAX_PERCENT = 60                        # S4, AMENDMENT A2: kept targets with the flag `np` (a direct object without a determiner) at most this share per product
BARE_MIN_PERCENT, LABELED_MIN_PERCENT = 30, 50   # S3: shares of the imperatives with a bare object and from enumerated or bulleted lines
MIN_CANDIDATES = {                               # size gate of the candidates: (label, kinds, minimum) (counted with the pilot rows on the same side)
    "deverbal-heads": (("pred+num", ("pred", "num"), 150), ("targets", ("pred", "num", "ing", "other"), 450), ("verb", ("verb",), 60)),
    "defined-terms": (("noun+adj", ("noun", "adj"), 500), ("proper", ("proper",), 40)),
    "step-lines": (("imperative", ("imperative",), 300), ("noun", ("noun",), 50)),
    "clause-verbs": (("main+sub", ("main", "sub"), 500), ("sub", ("sub",), 135), ("noun+adj", ("noun", "adj"), 60)),
    "deverbal": (("candidates", KINDS["deverbal"], 700), ("pred", ("pred",), 150), ("num", ("num",), 80)),
    "terms": (("candidates", KINDS["terms"], 1000), ("proper", ("proper",), 100)),
    "steps": (("candidates", KINDS["steps"], 800), ("noun", ("noun",), 100)),
    "clauses": (("candidates", KINDS["clauses"], 600), ("sub", ("sub",), 100))}

# ---- build quotas: (name, kinds, floor, cap) over the kept items; cap None = no cap of its own
DEV_QUOTAS = {
    "deverbal-heads": (("targets", TARGET_KINDS["deverbal"], 240, 360), ("pred", ("pred",), 40, 100), ("num", ("num",), 30, 60), ("ing", ("ing",), 80, 140),
                       ("other", ("other",), 0, 100), ("controls", ("verb",), 50, 90)),
    "defined-terms": (("noun", ("noun",), 120, 200), ("adj", ("adj",), 40, 80), ("controls", ("proper",), 40, 70)),
    "step-lines": (("imperative", ("imperative",), 180, 300), ("controls", ("noun",), 40, 70)),
    "clause-verbs": (("targets", TARGET_KINDS["clauses"], 240, 360), ("sub", ("sub",), 60, None), ("controls", ("noun", "adj"), 50, 90), ("noun", ("noun",), 35, 60),
                     ("adj", ("adj",), 15, 30))}
TRAIN_QUOTAS = {
    "deverbal": (("sentences", KINDS["deverbal"], 450, 800), ("pred", ("pred",), 100, None), ("num", ("num",), 50, None), ("ing", ("ing",), 150, None)),
    "terms": (("sentences", KINDS["terms"], 500, 900),),
    "steps": (("sentences", KINDS["steps"], 600, 1000),),
    "clauses": (("sentences", KINDS["clauses"], 450, 800),)}
TRAIN_SHARES = {"deverbal": (0, 15), "terms": (15, 25), "steps": (0, 30), "clauses": (0, 20)}   # the contrast strata (CONTRAST_KINDS): minimum and maximum percent of the file
CONTRAST_KINDS = {"deverbal": ("verb",), "terms": ("proper",), "steps": ("noun",), "clauses": ("noun", "adj")}
TRAIN_STRATUM_CAP = {"deverbal": {"pred": 250, "num": 200, "ing": 300, "other": 200}, "terms": {"noun": 550, "adj": 250}, "steps": {}, "clauses": {"main": 450, "sub": 300}}

# ---- pilot (S2)
PILOT_QUOTA = (("noun", 70), ("adj", 20), ("proper", 10))
PILOT_SIZE, PILOT_NOUN_MIN, PILOT_GATE_PERCENT = 100, 60, 40
PILOT_LEMMA_CAP, PILOT_TERM_CAP, PILOT_DOCUMENT_CAP, PILOT_FAMILY_CAP = 4, 2, 15, 20
PILOT_VERDICT_FILE = "pilot-verdict.txt"

# ---- hand check
SAMPLE_SIZE, SAMPLE_CONTROLS = 60, 4
SAMPLE_SEEDS = dict(zip(PRODUCTS, range(20261201, 20261209)))
SAMPLE_PLAN = {"deverbal-heads": (("pred", 14), ("num", 12), ("ing", 16), ("other", 12), ("verb", 6)),
               "defined-terms": (("noun", 30), ("adj", 15), ("proper", 15)),
               "step-lines": (("imperative", 45), ("noun", 15)),
               "clause-verbs": (("main", 24), ("sub", 18), ("noun", 10), ("adj", 8)),
               "deverbal": (("pred", 14), ("num", 12), ("ing", 16), ("other", 10), ("verb", 8)),
               "terms": (("noun", 30), ("adj", 15), ("proper", 15)),
               "steps": (("imperative", 40), ("noun", 20)),
               "clauses": (("main", 24), ("sub", 18), ("noun", 10), ("adj", 8))}
PRECISION_MIN, CONTROLS_ACCEPTED_MAX = shapes_dev.PRECISION_MIN, shapes_dev.CONTROLS_ACCEPTED_MAX

# ---- the tables and corpora of PR #20 that a new product must stay disjoint from (on top of everything followup_data.py holds)
PR20_TABLES = ("compound/compound-2-sources.tsv", "compound/compound-2-excluded-documents.tsv", "headings/headings-2-sources.tsv",
               "headings/headings-2-excluded-documents.tsv", "eval/shapes-dev/compound-head-2-sources.tsv", "eval/shapes-dev/headings-2-sources.tsv")
PR20_CORPORA = ("compound/compound-2.tsv", "headings/headings-2.tsv", "eval/shapes-dev/compound-head-2.tsv", "eval/shapes-dev/headings-2.tsv")
OLD_TARGET_FILES = ("eval/shapes-dev/compound-head.tsv", "eval/shapes-dev/compound-head-2.tsv")      # the union of the S1 lemma cap

# the frozen labeler prompt of PR #19 and its appendix are those of followup_data.py (the tests assert the hashes there and here)
LABELER_TEMPLATE, LABELER_APPENDIX = fu.LABELER_TEMPLATE, fu.LABELER_APPENDIX
labeler_prompt, template_sha256 = fu.labeler_prompt, fu.template_sha256

BuildError = fu.BuildError


class PinError(ValueError):
    """An input of the pre-registered selection is not the pinned one (or the pins are not set yet)."""


class GateError(ValueError):
    """The S2 pilot gate has not been passed (not run, scarcity or stop): nothing of S2 is drawn or built (decision O2)."""


# ---------------------------------------------------------------- pins, the used-document index and the sentence reference

def require_pins(work, product=None, data_dir=None):
    """Refuse (PinError) unless the pins are set and the inputs match them: sources-draft.tsv, tokens.tsv, hmm.tsv (for the development products with the Basic band) and
    data/CONVENTIONS.md (the file the blind labelers read). It is the R1 gate: in the R0 commit no pin is set, so every candidate and pilot command refuses.

    `data_dir` is the data folder whose CONVENTIONS.md is hashed (default: the repository's data/). Raises FileNotFoundError when a pinned input is missing."""
    needed = [("sources-draft.tsv", DRAFT_SHA256), ("tokens.tsv", TOKENS_SHA256)] + ([("hmm.tsv", HMM_SHA256)] if product in BANDED else [])
    for name, pinned in needed:
        if pinned is None:
            raise PinError("the pin of %s is not set: the R1 commit (frozen inputs) must come before any candidate" % name)
        actual = common.sha256_file(shapes_dev.shared_path(work, name))
        if actual != pinned:
            raise PinError("%s has SHA-256 %s, the pinned one is %s" % (name, actual, pinned))
    if CONVENTIONS_SHA256 is None:
        raise PinError("the pin of CONVENTIONS.md is not set: the R1 commit (frozen inputs) must come before any candidate")
    actual = common.sha256_file(os.path.join(data_dir or common.DATA, "CONVENTIONS.md"))
    if actual != CONVENTIONS_SHA256:
        raise PinError("CONVENTIONS.md has SHA-256 %s, the pinned one is %s" % (actual, CONVENTIONS_SHA256))


def used_tables(data_dir, extra_tables=()):
    """The extra tables of the used-document index: everything `followup_data.used_tables` holds plus the outputs of PR #20 (compound-2, headings-2, their exclusion records and
    the two tranche tables); never the tables of the eight products built here (a rebuild does not reject itself)."""
    tables = list(fu.used_tables(data_dir, extra_tables))
    for relative in PR20_TABLES:
        path = os.path.join(data_dir, *relative.split("/"))
        if os.path.exists(path) and path not in tables:
            tables.append(path)
    return tables


def used_index(data_dir, exclusions, extra_tables=()):
    """{(kind, value): origin} of every document that training, evaluation, all shapes-dev tranches, the imperative source, `compound`, `headings`, the outputs of PR #20, their exclusion
    records and the extra tables used or excluded."""
    return imperative_data.used_index(data_dir, exclusions, sx.used_tables(data_dir, used_tables(data_dir, extra_tables)))


class Reference(fu.Reference):
    """The sentences that the new products stay disjoint from: everything `followup_data.Reference` holds plus the follow-up sources compound-2.tsv and headings-2.tsv and the two
    PR #20 tranches (sentence keys and word 6-grams)."""

    def __init__(self, data_dir, extra_text=()):
        super().__init__(data_dir, extra_text)
        for relative in PR20_CORPORA:
            path = os.path.join(data_dir, *relative.split("/"))
            if os.path.exists(path):
                for sentence in common.read_corpus(path)[1]:
                    self.add(sentence["text"])
        self.keys.discard("")


def draft_gate(work, data_dir, extra_tables=(), omit=()):
    """Fail closed (`shapes_dev_extra.DraftError`) unless W/sources-draft.tsv is a sound list of new documents (the source and license gate, the index of ALL earlier tables).
    The rows of the documents in `omit` are not part of the gate (a document excluded after the sourcing can still be omitted; `build` refuses an unknown id)."""
    exclusions = shapes_dev.load_exclusions(work, data_dir)
    draft = [row for row in shapes_dev.read_draft(work) if row["source_id"] not in set(omit)]
    sx.require_sound_draft(work, draft, used_index(data_dir, exclusions, extra_tables), exclusions)


# ---------------------------------------------------------------- partition

def side_of(family, share=DEV_SHARE):
    """`dev` or `train`: the seeded rank of the family key read as a fraction of 16**12 against the DEV share (the rule of followup_data.side_of with one share)."""
    return "dev" if int(shapes_dev.rank_of(SEED, "partition", family)[:12], 16) / 16 ** 12 < share else "train"


def partition_rows(draft, streams=None, share=DEV_SHARE):
    """[(source_id, stream, family key, side)] of every draft document; a family is on one side. `streams` ({source_id: P|E|M|G}) is optional (the stream is then `-`)."""
    keys = {row["source_id"]: fu.family_key(row["title"]) for row in draft}
    if streams is not None:
        missing = sorted(set(keys) - set(streams))
        if missing:
            raise ValueError("no stream (P, E, M or G) for: " + ", ".join(missing[:10]))
    return [(source_id, (streams or {}).get(source_id, "-"), keys[source_id], side_of(keys[source_id], share)) for source_id in sorted(keys)]


def read_streams(work):
    """{source_id: stream} of W/streams.tsv (header `source_id<TAB>stream`, stream P, E, M or G), or None when the optional file is missing."""
    path = shapes_dev.shared_path(work, STREAMS_FILE)
    if not os.path.exists(path):
        return None
    _, records = adjudicate.read_records(path)
    streams = {}
    for record in records:
        if record["stream"] not in STREAMS or record["source_id"] in streams:
            raise ValueError("%s: a stream is one of %s and a document has one row: %s" % (STREAMS_FILE, ", ".join(STREAMS), record["source_id"]))
        streams[record["source_id"]] = record["stream"]
    return streams


def _command_partition(args):
    work = adjudicate.Work(args.work)
    draft = shapes_dev.read_draft(work)
    rows = partition_rows(draft, read_streams(work))
    adjudicate.write_rows(shapes_dev.shared_path(work, PARTITION_FILE), ("source_id", "stream", "family_key", "side"), rows)
    count = collections.Counter(side for _, _, _, side in rows)
    print("%d documents in %d families; dev: %d documents, train: %d documents" % (len(rows), len({key for _, _, key, _ in rows}), count["dev"], count["train"]))
    return 0


# ---------------------------------------------------------------- step units (S3)

STEP_KIND_ORDER = ("short", "enumerated", "bulleted", "sentence")       # the strictest rule first: the kind of a text that occurs as several kinds


def step_units(raw, sentences):
    """[(text, kind)] of one document: the step lines of the OCR text `raw` (`technical_frames.step_lines`) and then its sentence units (kind `sentence`), one entry per text."""
    found, seen = [], set()
    for text, kind in list(tf.step_lines(raw)) + [(sentence, "sentence") for sentence in sentences]:
        if text not in seen:
            seen.add(text)
            found.append((text, kind))
    return found


def _command_steps(args):
    work = adjudicate.Work(args.work)
    draft = shapes_dev.read_draft(work)
    documents = shapes_dev.read_documents(work)
    folder = shapes_dev.shared_path(work, "steps")
    os.makedirs(folder, exist_ok=True)
    kinds, total = {}, collections.Counter()
    for row in draft:
        source_id = row["source_id"]
        raw_path = os.path.join(args.work, "raw", source_id + ".txt")
        raw = ""
        if os.path.exists(raw_path):
            with open(raw_path, "r", encoding="utf-8", errors="replace", newline="") as handle:
                raw = handle.read()
        else:
            print("no raw text for " + source_id + " (its sentences are the only step units)", file=sys.stderr)
        units = step_units(raw, [text for _, text in documents.get(source_id, ())])
        common.write_text(os.path.join(folder, source_id + ".txt"), [text for text, _ in units])
        for text, kind in units:
            key = shapes_dev.sent_key_of(text)
            total[kind] += 1
            if key not in kinds or STEP_KIND_ORDER.index(kind) < STEP_KIND_ORDER.index(kinds[key]):
                kinds[key] = kind
    adjudicate.write_rows(shapes_dev.shared_path(work, STEP_KINDS_FILE), ("sent_key", "kind"), sorted(kinds.items()))
    print("%d documents, %d step units written to steps/ (%s)" % (len(draft), sum(total.values()), ", ".join("%s %d" % (k, total[k]) for k in STEP_KIND_ORDER)))
    return 0


def read_step_kinds(work):
    """{sentence key: unit kind} of W/step-kinds.tsv (written by `steps`); a missing file is an error."""
    path = shapes_dev.shared_path(work, STEP_KINDS_FILE)
    if not os.path.exists(path):
        raise ValueError("W/%s is missing: run `steps` first" % STEP_KINDS_FILE)
    return {record["sent_key"]: record["kind"] for record in adjudicate.read_records(path)[1]}


def step_noise(kinds):
    """The noise function of the step units for `shapes_dev_extra.scan_units`: `technical_frames.step_noise_reason` with the unit kind of the text (`sentence` when unknown)."""
    def noise(text, tokens, lex, vocab):
        return tf.step_noise_reason(text, tokens, lex, vocab, kinds.get(shapes_dev.sent_key_of(text), "sentence"))
    return noise


# ---------------------------------------------------------------- scans

def scan_all(work, data_dir, unit, extra_tables=(), extra_text=(), include_ud=True, filt=None, reference=None, lex=None, vocab=None, step_kinds=None):
    """The `shapes_dev_extra.Scan` of the clean units of ALL new documents (both sides): `unit` is `sentence` (W/text) or `step` (W/steps, with the step noise rules)."""
    lex = lex or shapes_dev.lexicon_of(data_dir)
    vocab = vocab or shapes_dev.vocabulary_of(data_dir, lex)
    filt = filt or shapes_dev.LeakageFilter(data_dir, include_ud, new_tranches=True, skip_tranches=TECHNICAL_TRANCHES)
    reference = reference or Reference(data_dir, extra_text)
    if unit == "step":
        kinds = step_kinds if step_kinds is not None else read_step_kinds(work)
        return sx.scan_units(work, data_dir, "step", used_tables(data_dir, extra_tables), include_ud, filt=filt, reference=reference, vocab=vocab, lex=lex,
                             lines_folder="steps", noise=step_noise(kinds), token_range=(tf.STEP_MIN_WORDS, tf.STEP_MAX_TOKENS))
    return sx.scan_units(work, data_dir, "sentence", used_tables(data_dir, extra_tables), include_ud, filt=filt, reference=reference, vocab=vocab, lex=lex)


# ---------------------------------------------------------------- frames, pools and candidates

Hit = collections.namedtuple("Hit", "index lemma frame tier term flag")


def frame_hits(group, tokens, lex, labeled=False):
    """{kind: Hit} of the frames of `group` that a unit has (position and lexicon only; `labeled` = the step unit carries a label). The caller takes the first kind in PRIORITY."""
    found = {}
    if group == "deverbal":
        hit = tf.deverbal_head_hit(tokens, lex)
        if hit is not None:
            found[hit.stratum] = Hit(hit.index, hit.lemma, hit.kind, "", "", "")
    elif group == "terms":
        for hit in tf.defined_term_hits(tokens, lex):
            found.setdefault(hit.stratum, Hit(hit.judged, shapes_dev.lemma_of(tokens[hit.judged], lex), "", hit.tier, hit.term, ""))
    elif group == "steps":
        lemma = shapes_dev.lemma_of(tokens[0], lex) if tokens else ""
        hit = tf.step_line_hit(tokens, lex, labeled)
        if hit is not None:
            found["imperative"] = Hit(0, lemma, hit.frame, "", "", "")
        if tf.step_caption_hit(tokens, lex, labeled) is not None:
            found["noun"] = Hit(0, lemma, "caption", "", "", "")
    else:
        for hit in (tf.clause_verb_hit(tokens, lex), tf.clause_control_hit(tokens, lex)):
            if hit is not None:
                found[hit.stratum] = Hit(hit.index, hit.lemma, "", "", "", ",".join(flag for flag, on in (("coordinated", hit.coordinated), ("np", hit.np)) if on))
    return found


def is_np(row):
    """True for an S4 row or entry that carries the flag `np` (AMENDMENT A2: the target frame with a direct object without a determiner after the word)."""
    return "np" in (row.get("flag") or "").split(",")


def np_second_kind(group, kind, found, wanted):
    """AMENDMENT A2: the target stratum (`main` or `sub`) that an `np` hit also offers when the noun control frame took the sentence first, else None. Such a sentence is offered to both
    kinds; `candidate_rows` draws the controls first and never gives a sentence to both."""
    if group != "clauses" or kind != "noun":
        return None
    return next((k for k in tf.CLAUSE_STRATA if k in wanted and k in found and is_np(found[k]._asdict())), None)


def _row(product, kind, hit, source_id, number, text, tokens, unit="sentence"):
    """The candidate row (the columns of `shapes_dev.write_candidates` plus the fields of the extras table) of a hit."""
    group = GROUP[product]
    return dict(shape=SHAPE_OF_KIND[group][kind], role="control" if kind in CONTROL_KINDS[group] else "target", target=hit.index, lemma=hit.lemma, origin="real",
                source_id=source_id, line=number, text=text, tokens=tokens, kind=kind, frame=hit.frame, unit=unit, tier=hit.tier, term=hit.term, flag=hit.flag)


def build_pool(scan, product, lex, seed, hmm=None, exclude_keys=frozenset(), step_kinds=None, kinds=None, label=None):
    """({kind: [(rank, row)]}, Counter of frame counts, Counter of drops): one candidate per sentence, the rarest kind of PRIORITY that is wanted takes it.

    Development targets of the banded products (S1, S3, S4) must lie in the band of `hmm` (`in_band`: 0.10 <= q < 0.97; S4 0.10 <= q, amendment A1) and are ranked by q ascending; S2 candidates rank T1 to T3 before T4; everything
    else is in the seeded order. `exclude_keys` are the sentences of earlier rounds. The verb control frames of S1 are not here (`verb_control_pool`)."""
    group, band = GROUP[product], BANDED.get(product)
    label = label or product
    wanted = tuple(kinds) if kinds is not None else tuple(k for k in KINDS[group] if not (group == "deverbal" and k == "verb"))
    pool, stats, drops = collections.defaultdict(list), collections.Counter(), collections.Counter()
    for source_id, number, text, tokens in scan.units:
        key = shapes_dev.sent_key_of(text)
        if key in exclude_keys:
            drops["earlier-round"] += 1
            continue
        unit = (step_kinds or {}).get(key, "sentence") if group == "steps" else "sentence"
        found = frame_hits(group, tokens, lex, unit in LABELED_UNITS)
        kind = next((k for k in PRIORITY[group] if k in wanted and k in found), None)
        if kind is None:
            continue
        stats["frame"] += 1
        for taken in (kind, np_second_kind(group, kind, found, wanted)):
            if taken is None:
                continue
            hit = found[taken]
            row = _row(product, taken, hit, source_id, number, text, tokens, unit)
            if band is not None and taken in CONTROL_KINDS[group]:
                row["q"], rank = "", (0, shapes_dev.rank_of(seed, "control", label, taken, key))
            elif band is not None:
                q = hmm.q(tokens, hit.index, band)
                if not in_band(product, q):
                    continue
                stats["band"] += 1
                row["q"], rank = shapes_dev.format_q(q), sx.rank_key(seed, label, key, q)
            elif group == "terms":
                rank = (hit.tier == "T4", shapes_dev.rank_of(seed, label, taken, key))
            else:
                rank = (0, shapes_dev.rank_of(seed, label, taken, key))
            pool[taken].append((rank, row))
    for ranked in pool.values():
        ranked.sort(key=lambda item: item[0])
    return pool, stats, drops


def verb_control_pool(scan, product, lex, seed, lemmas, exclude_keys=frozenset(), label=None):
    """[(rank, row)] of the verb controls of S1 (the first control frame of a sentence; `lemmas` None = every listed noun-verb word), in the seeded order."""
    pool = []
    for source_id, number, text, tokens in scan.units:
        key = shapes_dev.sent_key_of(text)
        if key in exclude_keys:
            continue
        hits = tf.verb_control_hits(tokens, lex, lemmas)
        if not hits:
            continue
        row = _row(product, "verb", Hit(hits[0].index, hits[0].lemma, hits[0].frame, "", "", ""), source_id, number, text, tokens)
        if product in BANDED:
            row["q"] = ""
        pool.append(((0, shapes_dev.rank_of(seed, "control", label or product, "verb", key)), row))
    pool.sort(key=lambda item: item[0])
    return pool


def cap_rows(ranked, limit, lemma_cap, document_cap, lemmas, documents, terms=None, term_cap=None, tier_limit=None, family_cap=None, family_of=None, families=None):
    """The first `limit` rows of `ranked` ((rank, row) in order) with at most `lemma_cap` per lemma (an int, or a function of the lemma), `document_cap` per document, `term_cap`
    per distinct term (S2), `family_cap` per title family and `tier_limit` rows of tier T4; the Counters `lemmas`, `documents`, `terms` and `families` start the counts and are updated."""
    kept, tier4 = [], 0
    for _, row in ranked:
        if limit is not None and len(kept) >= limit:
            break
        cap = lemma_cap(row["lemma"]) if callable(lemma_cap) else lemma_cap
        family = family_of.get(row["source_id"], "") if family_of else ""
        if (lemmas[row["lemma"]] >= cap or documents[row["source_id"]] >= document_cap or (term_cap is not None and terms[row["term"]] >= term_cap)
                or (family_cap is not None and families[family] >= family_cap) or (tier_limit is not None and row["tier"] == "T4" and tier4 >= tier_limit)):
            continue
        lemmas[row["lemma"]] += 1
        documents[row["source_id"]] += 1
        if term_cap is not None:
            terms[row["term"]] += 1
        if family_cap is not None:
            families[family] += 1
        tier4 += row["tier"] == "T4"
        kept.append(row)
    return kept


def union_target_counts(data_dir):
    """Counter of the head lemmas of the targets of compound-head.tsv and compound-head-2.tsv: the union that the S1 lemma cap is counted over."""
    lemmas = collections.Counter()
    for relative in OLD_TARGET_FILES:
        for sentence in fu.read_corpus_if_present(data_dir, relative):
            c = fu.comments_of(sentence)
            if c.get("role") == "target":
                lemmas[c["lemma"]] += 1
    return lemmas


def candidate_rows(scan, product, lex, seed=SEED, hmm=None, old_counts=None, prior_rows=(), exclude_keys=frozenset(), step_kinds=None, controls_only=False, room=None):
    """(rows, report lines, counts) of the candidates of one round of a product (rule 5).

    `prior_rows` are the pilot rows of the side (S2): not drawn again, counted against the caps; `exclude_keys` the sentences of earlier rounds; `controls_only` the control
    top-up round, with `room` ({lemma: controls still wanted}; S1 draws only those lemmas, the other shapes any lemma at CONTROL_LEMMA_CAP)."""
    group, side = GROUP[product], SIDE[product]
    exclude = set(exclude_keys) | {shapes_dev.sent_key_of(r["text"]) for r in prior_rows}
    budgets = CONTROL_TOPUP[product] if controls_only else CANDIDATE_BUDGET[product]
    kinds = [k for k in KINDS[group] if k in budgets]
    pool, stats, drops = build_pool(scan, product, lex, seed, hmm, exclude, step_kinds, [k for k in kinds if not (group == "deverbal" and k == "verb")])
    control = lambda row: row["kind"] in CONTROL_KINDS[group]
    target_lemmas = collections.Counter(old_counts or ())
    target_lemmas.update(r["lemma"] for r in prior_rows if not control(r))
    control_lemmas = collections.Counter() if controls_only else collections.Counter(r["lemma"] for r in prior_rows if control(r))      # the room of a top-up round counts them already
    documents, terms = collections.Counter(r["source_id"] for r in prior_rows), collections.Counter(r["term"] for r in prior_rows if r.get("term"))
    prior_kinds = collections.Counter() if controls_only else collections.Counter(r["kind"] for r in prior_rows)
    document_cap, term_cap = CANDIDATE_DOCUMENT_CAP[side], (CANDIDATE_TERM_CAP[side] if group == "terms" else None)
    rows, per_kind = [], {}

    def draw_targets():
        for kind in [k for k in PRIORITY[group] if k in kinds and k not in CONTROL_KINDS[group]]:
            limit = None if budgets[kind] is None else max(0, budgets[kind] - prior_kinds[kind])
            tier_limit = None if limit is None or group != "terms" else math.ceil(TIER4_SHARE_PERCENT * limit / 100)
            ranked = pool.get(kind, ())
            if group == "clauses" and control_keys:                          # AMENDMENT A2: a sentence of the control draw is never a target candidate too
                ranked = [item for item in ranked if shapes_dev.sent_key_of(item[1]["text"]) not in control_keys]
            per_kind[kind] = cap_rows(ranked, limit, CANDIDATE_LEMMA_CAP[product], document_cap, target_lemmas, documents, terms, term_cap, tier_limit)

    def draw_controls():
        for kind in [k for k in KINDS[group] if k in kinds and k in CONTROL_KINDS[group]]:
            if group == "deverbal":
                lemmas = ({lemma for lemma in (room or {})} if controls_only else {r["lemma"] for taken in per_kind.values() for r in taken} if side == "dev" else None)
                targeted = {shapes_dev.sent_key_of(r["text"]) for kind_taken in per_kind.values() for r in kind_taken}     # a control never shares a sentence with a target candidate
                ranked = verb_control_pool(scan, product, lex, seed, lemmas, exclude | targeted)
            else:
                ranked = pool.get(kind, ())
            if controls_only:
                default = 0 if group == "deverbal" else CONTROL_LEMMA_CAP
                lemma_cap = lambda lemma, room=room, default=default: (room or {}).get(lemma, default)
            else:
                lemma_cap = CONTROL_LEMMA_CAP if side == "dev" else CANDIDATE_LEMMA_CAP[product]
            limit = None if budgets[kind] is None else max(0, budgets[kind] - prior_kinds[kind])
            per_kind[kind] = cap_rows(ranked, limit, lemma_cap, document_cap, control_lemmas, documents, terms, term_cap)

    control_keys = set()
    if group == "clauses":                    # AMENDMENT A2: the control draw comes first (its frame is also the `np` target frame), the target draw skips its sentences
        draw_controls()
        control_keys = {shapes_dev.sent_key_of(r["text"]) for taken in per_kind.values() for r in taken}
        draw_targets()
        per_kind = {k: per_kind[k] for k in [k for k in PRIORITY[group] if k in per_kind and k not in CONTROL_KINDS[group]] + [k for k in KINDS[group] if k in per_kind and k in CONTROL_KINDS[group]]}
    else:
        draw_targets()
        draw_controls()
    for kind, taken in per_kind.items():
        for row in taken:
            row["prefix"] = prefix_of(product, kind, controls_only)
        rows += taken
    fu.number_rows(rows, "", product + ("-controls" if controls_only else "") + "-order", seed)
    np_rows = sum(1 for r in rows if r["role"] == "target" and is_np(r))
    counts = dict(clean=len(scan.units), frame=stats["frame"], band=stats["band"], candidates=len(rows), kinds={k: len(v) for k, v in per_kind.items()},
                  pools={k: len(v) for k, v in pool.items()}, lemmas=len({r["lemma"] for r in rows}), documents=len({r["source_id"] for r in rows}), drops=dict(drops),
                  prior=dict(prior_kinds), np=np_rows)
    report = ["# Candidates (%s%s)" % (product, ", control top-up round" if controls_only else ""), "", "Counts only; no text of any evaluation set.", ""] + chd.scan_lines(scan, "sentence")
    report += ["", "frame hits: %d, in the band: %d (a development target of S1, S3 or S4 must lie in it: 0.10 <= q < 0.97; S4 0.10 <= q, amendment A1)" % (counts["frame"], counts["band"]),
               "frame hits per kind (before the caps): " + (", ".join("%s %d" % item for item in sorted(counts["pools"].items())) or "none"),
               "dropped at the frame stage: " + (", ".join("%s %d" % item for item in sorted(drops.items())) or "none"),
               "pilot rows already drawn on this side: %d" % len(prior_rows),
               "candidates: %d over %d lemmas in %d documents (%s)" % (counts["candidates"], counts["lemmas"], counts["documents"],
                                                                     ", ".join("%s %d" % item for item in sorted(counts["kinds"].items())) or "none")]
    if group == "clauses":
        targets = sum(n for k, n in counts["kinds"].items() if k not in CONTROL_KINDS[group])
        report.append("np target candidates (amendment A2, a direct object without a determiner after the word): %d of %d target candidates" % (counts["np"], targets))
    return rows, report, counts


def band_counts(scan, product, lex, hmm, step_kinds=None):
    """Counter {band index of Q_BAND_LABELS: hits} (counts only) of the development target hits of a banded product: the units that `build_pool` would take as a target kind
    (the rarest wanted kind of a unit takes it; the control kinds are not counted; an `np` hit of S4 that the noun control frame took first counts as a target hit, amendment A2),
    by the Basic mass of the judged word. No label, no PartOfSpeech model, nothing is written."""
    group, band = GROUP[product], BANDED[product]
    wanted = tuple(k for k in KINDS[group] if k not in CONTROL_KINDS[group])
    kinds = tuple(k for k in KINDS[group] if not (group == "deverbal" and k == "verb"))
    counts = collections.Counter()
    for _source_id, _number, text, tokens in scan.units:
        unit = (step_kinds or {}).get(shapes_dev.sent_key_of(text), "sentence") if group == "steps" else "sentence"
        found = frame_hits(group, tokens, lex, unit in LABELED_UNITS)
        kind = next((k for k in PRIORITY[group] if k in kinds and k in found), None)
        if kind is not None and kind not in wanted:
            kind = np_second_kind(group, kind, found, wanted)
        if kind is None:
            continue
        counts[q_band(hmm.q(tokens, found[kind].index, band))] += 1
    return counts


def gate_messages(product, counts):
    """[message] for each miss of the size gate of the candidates of a product (the pilot rows of the side count)."""
    have = collections.Counter(counts["kinds"])
    have.update(counts.get("prior", {}))
    return ["only %d %s candidates (at least %d needed)" % (sum(have[k] for k in kinds), label, minimum)
            for label, kinds, minimum in MIN_CANDIDATES[product] if sum(have[k] for k in kinds) < minimum]


def write_extras(round_work, rows):
    """Write private/extras.tsv: the frame, unit kind, tier, term and flag of every candidate (read back by `build`)."""
    adjudicate.write_rows(round_work.private_file(EXTRAS_FILE), EXTRA_COLUMNS, [(r["cand_id"], r["frame"], r["unit"], r["tier"], r["term"], r["flag"]) for r in rows])


def read_extras(round_work):
    """{cand_id: record} of private/extras.tsv of a round (empty when the file is missing)."""
    path = round_work.private_file(EXTRAS_FILE)
    return {record["cand_id"]: record for record in adjudicate.read_records(path)[1]} if os.path.exists(path) else {}


# ---------------------------------------------------------------- the S2 pilot and its gate

def pilot_candidates(scan, lex, family_of, seed=SEED, documents=None):
    """(rows, report lines, counts): the pilot sample of rule 6 from the clean sentences of `scan` (both sides; `documents` restricts it to the first-wave documents)."""
    product = "defined-terms"
    pool, stats, _ = build_pool(scan, product, lex, seed, None, frozenset(), None, PILOT_KINDS, label=PILOT_ROUND)
    lemmas, documents_count, terms, families = (collections.Counter() for _ in range(4))
    rows = []
    for kind, quota in PILOT_QUOTA:
        ranked = [(rank, row) for rank, row in pool.get(kind, ()) if documents is None or row["source_id"] in documents]
        taken = cap_rows(ranked, quota, PILOT_LEMMA_CAP, PILOT_DOCUMENT_CAP, lemmas, documents_count, terms, PILOT_TERM_CAP, math.ceil(TIER4_SHARE_PERCENT * quota / 100),
                         PILOT_FAMILY_CAP, family_of, families)
        for row in taken:
            row["prefix"] = prefix_of(product, kind, pilot=True)
        rows += taken
    fu.number_rows(rows, "", PILOT_ROUND + "-order", seed)
    kinds = collections.Counter(r["kind"] for r in rows)
    counts = dict(population=sum(len(v) for v in pool.values()), sampled=len(rows), kinds=dict(kinds), lemmas=len(lemmas), documents=len(documents_count), families=len(families))
    report = ["# Pilot candidates", "", "Counts only; no text of any evaluation set.", "",
              "population (S2 frames): %d sentences; sampled: %d (%s) over %d lemmas, %d documents, %d families"
              % (counts["population"], counts["sampled"], ", ".join("%s %d" % item for item in sorted(kinds.items())) or "none", counts["lemmas"], counts["documents"],
                 counts["families"])]
    return rows, report, counts


def pilot_verdict(confirmed, noun_candidates):
    """(verdict, message): SCARCITY when fewer than PILOT_NOUN_MIN noun-stratum candidates were piloted, STOP when fewer than PILOT_GATE_PERCENT percent of them are confirmed,
    else PROCEED. The arithmetic is integer: confirmed * 100 >= PILOT_GATE_PERCENT * noun_candidates."""
    if noun_candidates < PILOT_NOUN_MIN:
        return "SCARCITY", "only %d of at least %d noun-stratum pilot candidates exist: the pilot cannot run (S2 stops)" % (noun_candidates, PILOT_NOUN_MIN)
    minimum = (PILOT_GATE_PERCENT * noun_candidates + 99) // 100
    if confirmed < minimum:
        return "STOP", "%d of %d confirmed, fewer than %d (%d percent): S2 stops" % (confirmed, noun_candidates, minimum, PILOT_GATE_PERCENT)
    return "PROCEED", "%d of %d confirmed (at least %d): proceed" % (confirmed, noun_candidates, minimum)


def pilot_numbers(items, extras):
    """(confirmed, noun-stratum candidates, report lines) of the decided pilot items. A noun-stratum candidate is CONFIRMED when both labelers tag the judged word NOUN.
    Counts only (no text): per tier, the other strata and the tags of the noun candidates that are not confirmed are reported, not gated."""
    expected = {"noun": "NOUN", "adj": "ADJ", "proper": "PROPN"}
    by_kind = collections.defaultdict(list)
    for item in items:
        by_kind[kind_of_cand_id(item["cand_id"])[1]].append(item)

    def agreed(item, kind):
        return item["opus_tag"] == item["sol_tag"] == expected[kind]

    nouns = by_kind["noun"]
    confirmed = sum(agreed(i, "noun") for i in nouns)
    by_tier = collections.defaultdict(lambda: [0, 0])
    others = collections.Counter()
    for item in nouns:
        tier = extras.get(item["cand_id"], {}).get("tier", "?")
        by_tier[tier][0] += 1
        by_tier[tier][1] += agreed(item, "noun")
        if not agreed(item, "noun"):
            others[item["opus_tag"] if item["opus_tag"] == item["sol_tag"] else "disagreement"] += 1
    lines = ["# Pilot record (counts only)", "", "noun-stratum candidates %d, confirmed %d (both labelers tag the judged word NOUN)" % (len(nouns), confirmed), "",
             "| tier | noun candidates | confirmed |", "| --- | --- | --- |"] + ["| %s | %d | %d |" % (k, v[0], v[1]) for k, v in sorted(by_tier.items())]
    lines += ["", "| judged word of an unconfirmed noun candidate | candidates |", "| --- | --- |"] + ["| %s | %d |" % item for item in sorted(others.items())]
    lines += ["", "| other stratum | candidates | both labelers agree with the stratum tag |", "| --- | --- | --- |"]
    lines += ["| %s | %d | %d |" % (kind, len(by_kind[kind]), sum(agreed(i, kind) for i in by_kind[kind])) for kind in ("adj", "proper")]
    return confirmed, len(nouns), lines


def pilot_items(work, data_dir=None):
    """([decided item], {cand_id: extras record}) of the decided pilot round."""
    round_work = shapes_dev.RoundWork(getattr(work, "base", work.root), PILOT_ROUND)
    items, _ = shapes_dev.read_decided(round_work, data_dir)
    return items, read_extras(round_work)


def pilot_rows_of_side(work, sides, side):
    """[candidate row] of the pilot round whose document is on `side`, with `kind` and `term` (empty when the pilot has no candidates)."""
    round_work = shapes_dev.RoundWork(getattr(work, "base", work.root), PILOT_ROUND)
    if not os.path.exists(shapes_dev.work_path(round_work, "candidates.tsv")):
        return []
    extras = read_extras(round_work)
    rows = []
    for candidate in shapes_dev.read_candidates(round_work):
        if sides.get(candidate["source_id"]) == side:
            candidate["kind"] = kind_of_cand_id(candidate["cand_id"])[1]
            candidate["term"] = extras.get(candidate["cand_id"], {}).get("term", "")
            rows.append(candidate)
    return rows


def require_pilot(work):
    """Refuse (GateError) unless `pilot-result` recorded PROCEED for the S2 pilot (decision O2: nothing of S2 is drawn or built after a stop)."""
    path = shapes_dev.RoundWork(getattr(work, "base", work.root), PILOT_ROUND).private_file(PILOT_VERDICT_FILE)
    lines = common.read_lines(path) if os.path.exists(path) else []
    if not lines:
        raise GateError("the S2 pilot gate has not been run: label the round %s and run `pilot-result` first" % PILOT_ROUND)
    if lines[0].split(" ", 1)[0] != "PROCEED":
        raise GateError("the S2 pilot gate did not pass (%s): nothing of S2 (defined-terms, terms) is drawn or built" % lines[0])


# ---------------------------------------------------------------- the order of the rounds

def round_has_candidates(work, name):
    return os.path.exists(shapes_dev.work_path(shapes_dev.RoundWork(getattr(work, "base", work.root), name), "candidates.tsv"))


def earlier_keys(work, name, side):
    """The sentence keys of the candidates of the rounds that come before `name` on `side` in ROUND_ORDER (the pilot included): a round never draws them again."""
    order = ROUND_ORDER[side]
    keys = set()
    for other in order[:order.index(name)]:
        if round_has_candidates(work, other):
            keys |= {c["sent_key"] for c in shapes_dev.read_candidates(shapes_dev.RoundWork(getattr(work, "base", work.root), other))}
    return keys


def later_rounds(work, name, sides):
    """[round] that come after `name` in the order of one of `sides` and already have candidates (a round is refused while one exists)."""
    found = []
    for side in sides:
        order = ROUND_ORDER[side]
        found += [other for other in order[order.index(name) + 1:] if other not in found and round_has_candidates(work, other)]
    return found


# ---------------------------------------------------------------- the rounds of a product, entries, selection

def read_product_rounds(work, product, data_dir=None, sides=None, keep=(), rounds=None):
    """(items, tagged rows, candidates, extras) of the decided rounds of a product (`rounds`, default ROUNDS), restricted to the documents on the side of the product."""
    base = getattr(work, "base", work.root)
    sides = sides if sides is not None else fu.read_partition(work)
    items, rows, candidates, extras = [], {}, {}, {}
    for name in rounds or ROUNDS[product]:
        round_work = shapes_dev.RoundWork(base, name)
        if not os.path.exists(round_work.private_file("decided.tsv")):
            continue
        part_items, part_rows = shapes_dev.read_decided(round_work, data_dir, keep=keep)
        items += [i for i in part_items if sides.get(i["source_id"]) == SIDE[product]]
        for key, tagged in part_rows.items():
            rows.setdefault(key, tagged)
        candidates.update({c["cand_id"]: c for c in shapes_dev.read_candidates(round_work)})
        extras.update(read_extras(round_work))
    return items, rows, candidates, extras


def entry_of(item, candidates, extras):
    """The decided item with its kind (from the candidate id prefix) and the fields of the extras table of its round."""
    entry = dict(item)
    extra = extras.get(item["cand_id"], {})
    entry.update(kind=kind_of_cand_id(item["cand_id"])[1], frame=extra.get("frame", ""), unit=extra.get("unit", ""), tier=extra.get("tier", ""), term=extra.get("term", ""),
                 flag=extra.get("flag", ""), text=candidates[item["cand_id"]]["text"])
    return entry


def dev_entries(items, candidates, extras):
    """[entry] of the kept items of a development product."""
    return [entry_of(i, candidates, extras) for i in items if i["outcome"] == "keep"]


def title_family_of(draft):
    """{source_id: title family key} (the family of the partition; the S3 family cap and the pilot family cap are read at this level)."""
    return {row["source_id"]: fu.family_key(row["title"]) for row in draft}


class Selector:
    """The counters of one selection (rule 8): per lemma, document, term (S2), title family (S3) and sentence. A sentence is taken once."""

    def __init__(self, lemma_cap, document_cap, lemmas=None, term_cap=None, family_cap=None, family_of=None, np_limit=None):
        self.lemma_cap, self.document_cap, self.term_cap, self.family_cap, self.family_of = lemma_cap, document_cap, term_cap, family_cap, family_of or {}
        self.np_limit, self.np = np_limit, 0
        self.lemmas, self.documents, self.terms, self.families, self.keys = collections.Counter(lemmas or ()), collections.Counter(), collections.Counter(), collections.Counter(), set()

    def derived(self, lemma_cap):
        """A selector with a new lemma cap and fresh lemma counts that shares the document, term, family and sentence counts with this one (the controls)."""
        other = Selector(lemma_cap, self.document_cap, None, self.term_cap, self.family_cap, self.family_of)
        other.documents, other.terms, other.families, other.keys = self.documents, self.terms, self.families, self.keys
        return other

    def refusal(self, entry):
        """The drop reason of an entry that a cap refuses, else None."""
        if entry["sent_key"] in self.keys:
            return "same-sentence"
        if self.lemmas[entry["lemma"]] >= self.lemma_cap:
            return "lemma-cap"
        if self.documents[entry["source_id"]] >= self.document_cap:
            return "document-cap"
        if self.term_cap is not None and entry["term"] and self.terms[entry["term"]] >= self.term_cap:
            return "term-cap"
        if self.family_cap is not None and self.families[self.family_of.get(entry["source_id"], "")] >= self.family_cap:
            return "family-cap"
        if self.np_limit is not None and is_np(entry) and self.np >= self.np_limit:
            return "np-cap"
        return None

    def take(self, entry):
        self.keys.add(entry["sent_key"])
        self.lemmas[entry["lemma"]] += 1
        self.documents[entry["source_id"]] += 1
        self.np += is_np(entry)
        if entry["term"]:
            self.terms[entry["term"]] += 1
        self.families[self.family_of.get(entry["source_id"], "")] += 1


def choose(selector, members, limit, drops, taken=None):
    """The members (in order) that the caps allow, at most `limit` (None = no limit); the others are counted in `drops` (`cap` or the reason of the selector)."""
    taken = [] if taken is None else taken
    start = len(taken)
    for entry in members:
        if limit is not None and len(taken) - start >= limit:
            drops["cap"] += 1
            continue
        reason = selector.refusal(entry)
        if reason:
            drops[reason] += 1
            continue
        selector.take(entry)
        taken.append(entry)
    return taken


def choose_steps(selector, members, limit, drops):
    """`choose` for the imperatives of S3 (limit an int): bare-object lines first up to BARE_MIN_PERCENT of the limit, then enumerated or bulleted units up to LABELED_MIN_PERCENT,
    then the rest in order (the two under-represented cases; a refusal is counted once, in the last pass)."""
    chosen, ids = [], set()
    for test, percent in ((lambda e: e["frame"] == "bare", BARE_MIN_PERCENT), (lambda e: e["unit"] in LABELED_UNITS, LABELED_MIN_PERCENT)):
        goal = math.ceil(percent * limit / 100)
        for entry in members:
            if sum(1 for c in chosen if test(c)) >= goal or len(chosen) >= limit:
                break
            if entry["cand_id"] in ids or not test(entry) or selector.refusal(entry):
                continue
            selector.take(entry)
            chosen.append(entry)
            ids.add(entry["cand_id"])
    rest = [e for e in members if e["cand_id"] not in ids]
    chosen += choose(selector, rest, limit - len(chosen), drops)
    return chosen


def order_key(product, seed, label):
    """The sort key of the entries of one selection: q ascending for the banded development targets, T4 last for S2, else the seeded rank."""
    banded, terms = product in BANDED, GROUP[product] == "terms"

    def key(entry):
        rank = shapes_dev.rank_of(seed, label, entry["cand_id"] if SIDE[product] == "dev" else entry["sent_key"])
        if banded and entry["role"] == "target" and entry.get("q", "") != "":
            return (float(entry["q"]), rank)
        return (entry["tier"] == "T4", rank) if terms else (0, rank)
    return key


def quota_cap(product, name):
    """The cap of the development quota `name` of a product (None when it has no cap)."""
    return next((cap for quota_name, _, _, cap in DEV_QUOTAS[product] if quota_name == name), None)


def np_over_cap(targets):
    """True when the `np` targets are more than NP_SHARE_MAX_PERCENT of the targets (AMENDMENT A2)."""
    return sum(1 for t in targets if is_np(t)) * 100 > NP_SHARE_MAX_PERCENT * len(targets)


def with_np_cap(select, targets_of):
    """The result of `select(np_limit)` for the largest `np_limit` (None = no limit, tried first) that keeps the `np` share of `targets_of(result)` within NP_SHARE_MAX_PERCENT: the np
    entries come in the order of the selection and the others are never removed, so the original frame stays represented (AMENDMENT A2)."""
    result = select(None)
    targets = targets_of(result)
    if not np_over_cap(targets):
        return result
    for limit in range(sum(1 for t in targets if is_np(t)) - 1, -1, -1):
        result = select(limit)
        if not np_over_cap(targets_of(result)):
            return result
    return result


def select_dev(product, entries, family_of=None, old_counts=None, seed=SEED):
    """(targets, controls, drops) of a development product (rule 8): the targets kind by kind in BUILD_ORDER (q ascending for S1, S3, S4; S3 with the bare and labeled preference) with the
    cap of the kind and the total, 4 or 6 per lemma (S1 over the union `old_counts`), 15 per document, S2 2 per term, S3 30 per title family; the controls (S1: only for the lemmas of the
    chosen targets) in the seeded order, 2 per lemma, with the cap of the kind and the total. S4: the `np` targets are at most NP_SHARE_MAX_PERCENT of the targets (AMENDMENT A2; reason `np-cap`)."""
    return with_np_cap(lambda np_limit: _select_dev(product, entries, family_of, old_counts, seed, np_limit), lambda result: result[0])


def _select_dev(product, entries, family_of, old_counts, seed, np_limit):
    group = GROUP[product]
    drops = collections.Counter()
    selector = Selector(LEMMA_CAP[product], DOCUMENT_CAP["dev"], old_counts, TERM_CAP["dev"] if group == "terms" else None, FAMILY_CAP if group == "steps" else None, family_of, np_limit)
    total = quota_cap(product, "targets")
    targets = []
    for kind in BUILD_ORDER[group]:
        members = sorted((e for e in entries if e["role"] == "target" and e["kind"] == kind), key=order_key(product, seed, "build"))
        limits = [n for n in (quota_cap(product, kind), None if total is None else total - len(targets)) if n is not None]
        limit = max(0, min(limits)) if limits else None
        if group == "steps":
            targets += choose_steps(selector, members, limit, drops)
        else:
            choose(selector, members, limit, drops, targets)
    lemmas = {t["lemma"] for t in targets}
    control_selector = selector.derived(CONTROL_LEMMA_CAP)
    controls_total = quota_cap(product, "controls")
    controls = []
    for kind in CONTROL_KINDS[group]:
        members = sorted((e for e in entries if e["role"] == "control" and e["kind"] == kind), key=lambda e: shapes_dev.rank_of(seed, "build-control", e["cand_id"]))
        if group == "deverbal":
            drops["no-target-lemma"] += sum(1 for e in members if e["lemma"] not in lemmas)
            members = [e for e in members if e["lemma"] in lemmas]
        limits = [n for n in (quota_cap(product, kind), None if controls_total is None else controls_total - len(controls)) if n is not None]
        choose(control_selector, members, max(0, min(limits)) if limits else None, drops, controls)
    return targets, controls, drops


def select_train(product, kept, seed=SEED):
    """(chosen, drops) of a training product (rule 8): the main strata in BUILD_ORDER (the cap of the stratum, S3 with the bare and labeled preference) up to the total cap minus the
    goal of the contrast strata, then the contrast strata up to their maximum share of the file; lemma, document and term caps over both. S4: the `np` sentences are at most
    NP_SHARE_MAX_PERCENT of the main strata (AMENDMENT A2; reason `np-cap`)."""
    group = GROUP[product]
    return with_np_cap(lambda np_limit: _select_train(product, kept, seed, np_limit), lambda result: [e for e in result[0] if e["kind"] not in CONTRAST_KINDS[group]])


def _select_train(product, kept, seed, np_limit):
    group = GROUP[product]
    drops = collections.Counter()
    total = TRAIN_QUOTAS[product][0][3]
    _, share_max = TRAIN_SHARES[group]
    selector = Selector(LEMMA_CAP[product], DOCUMENT_CAP["train"], None, TERM_CAP["train"] if group == "terms" else None, np_limit=np_limit)
    key = order_key(product, seed, "technical-build:" + product)
    contrast = sorted((e for e in kept if e["kind"] in CONTRAST_KINDS[group]), key=key)
    goal = min(len(contrast), total * share_max // 100)
    main = []
    for kind in BUILD_ORDER[group]:
        members = sorted((e for e in kept if e["kind"] == kind), key=key)
        limits = [n for n in (TRAIN_STRATUM_CAP[group].get(kind), total - goal - len(main)) if n is not None]
        limit = max(0, min(limits))
        if group == "steps":
            main += choose_steps(selector, members, limit, drops)
        else:
            choose(selector, members, limit, drops, main)
    chosen_contrast = choose(selector, contrast, min(goal, len(main) * share_max // (100 - share_max)), drops)
    return main + chosen_contrast, drops


def control_room(product, items, candidates, extras, old_counts=None, family_of=None, seed=SEED):
    """{lemma: controls that the build still takes} for the control top-up round of a development product: S1 for the lemmas of the chosen targets that have room, the other shapes
    for every lemma with controls already (an unseen lemma has the full CONTROL_LEMMA_CAP)."""
    entries = dev_entries(items, candidates, extras)
    have = collections.Counter(e["lemma"] for e in entries if e["role"] == "control")
    if GROUP[product] == "deverbal":
        targets, _, _ = select_dev(product, entries, family_of, old_counts, seed)
        return {lemma: CONTROL_LEMMA_CAP - have[lemma] for lemma in {t["lemma"] for t in targets} if have[lemma] < CONTROL_LEMMA_CAP}
    return {lemma: max(0, CONTROL_LEMMA_CAP - n) for lemma, n in have.items()}


# ---------------------------------------------------------------- built sentences, quota gates, headers

HEADER_SELECTION = {
    "deverbal": "sentences with a noun compound whose head is spelled like a verb (zero-derived or an -ing gerund), by position and lexicon only",
    "terms": "sentences with a capitalized term in the middle of the sentence, by position and lexicon only",
    "steps": "step lines, list items and short instructions that start with a capitalized word, by position and lexicon only",
    "clauses": "sentences with a plural or coordinated subject and a base-form word that can also be a noun or an adjective, by position and lexicon only"}
HEADER_FIELDS = {"deverbal": "", "terms": ", term (the lower-case words of the capitalized term)", "steps": ", frame, unit (the frame and the unit kind of the line)", "clauses": ""}


def header_lines(product):
    """The header comment lines of the built corpus of a product."""
    folder, corpus, sources = OUTPUT[product]
    group = GROUP[product]
    kinds = " | ".join(kind_label(product, k) for k in KINDS[group])
    if SIDE[product] == "dev":
        return ["# corpus = DemaConsulting.PartOfSpeech shapes development set, %s tranche (UPOS), dev split" % product,
                "# license = CC0-1.0 for annotations and selection; sentences are US federal government works, US public domain, see %s" % sources,
                "# columns = form, upos, status",
                "# status = unanimous | disputed (two blind model labelers; disputed = they differ, the tag is the first labeler's)",
                "# judged word = per sentence: shape (%s), role (target | control), target (zero-based token index), lemma, origin (real), kind (%s)%s; the judged word is never disputed"
                % (TRANCHE_SHAPE[group], kinds, HEADER_FIELDS[group]),
                "# selection = %s; the gold is the agreement of two blind labelers (see README.md)" % HEADER_SELECTION[group],
                "# use = development only: select settings here, never train on it, never report it as a held-out figure", "# file = %s" % corpus]
    return ["# corpus = DemaConsulting.PartOfSpeech technical-text training source `%s` (UPOS)" % product,
            "# license = CC0-1.0 for annotations and selection; sentences are US federal government works, see %s" % sources,
            "# columns = form, upos, status",
            "# status = unanimous (two blind model labelers agree on the tag of every token)",
            "# judged word = per sentence: word (zero-based index of the judged word), lemma, stratum (%s)%s" % (kinds, HEADER_FIELDS[group]),
            "# selection = %s; see README.md and tools/data/technical_data.py" % HEADER_SELECTION[group],
            "# use = training"]


def final_sentence(product, entry, sentence, number):
    """The built sentence of a kept entry: the agreed token rows, the id and the per-sentence lines."""
    group, kind = GROUP[product], entry["kind"]
    if SIDE[product] == "train":
        extra = ["# word = %d" % entry["target"], "# lemma = " + entry["lemma"], "# stratum = " + kind_label(product, kind)]
    else:
        extra = ["# shape = " + TRANCHE_SHAPE[group], "# role = " + entry["role"], "# target = %d" % entry["target"], "# lemma = " + entry["lemma"], "# origin = real",
                 "# kind = " + kind]
    if group == "terms":
        extra.append("# term = " + entry["term"])
    if group == "steps":
        extra += ["# frame = " + (entry["frame"] or "-"), "# unit = " + (entry["unit"] or "-")]
    return dict(sentence, sent_id=SENT_ID[product] % number, extra=extra)


def judged(product, sentence):
    """(index, kind, lemma) of the judged word of a built sentence."""
    c = fu.comments_of(sentence)
    if SIDE[product] == "train":
        return int(c["word"]), kind_of_label(product, c["stratum"]), c["lemma"]
    return int(c["target"]), c["kind"], c["lemma"]


def sentence_problems(product, sentence):
    """[message] for one built sentence (the per-sentence rules of the quota gate)."""
    rows, name = sentence["rows"], sentence["sent_id"]
    c = fu.comments_of(sentence)
    group, train = GROUP[product], SIDE[product] == "train"
    index_name = "word" if train else "target"
    if not c.get(index_name, "").isdigit() or int(c[index_name]) >= len(rows):
        return ["%s: `%s` is not a token index" % (name, index_name)]
    i, kind, _ = judged(product, sentence)
    if kind not in KINDS[group]:
        return ["%s: `%s` is not a kind of %s" % (name, kind, product)]
    problems = []
    if rows[i][1] not in EXPECTED_TAGS[group][kind]:
        problems.append("%s: the judged word is tagged %s for kind %s" % (name, rows[i][1], kind))
    if not train and (c.get("shape") != TRANCHE_SHAPE[group] or c.get("role") != ("control" if kind in CONTROL_KINDS[group] else "target") or c.get("origin") != "real"):
        problems.append("%s: shape, role or origin is wrong" % name)
    if train and any(common.base_status(row[2]) != "unanimous" for row in rows):
        problems.append("%s: a token is not unanimous" % name)
    if not train and common.base_status(rows[i][2]) == "disputed":
        problems.append("%s: the judged word is disputed" % name)
    if "".join(row[0] for row in rows) != sentence["text"].replace(" ", ""):
        problems.append("%s: the tokens do not rejoin to the text" % name)
    if group == "steps" and i != 0:
        problems.append("%s: a step line is judged at token 0" % name)
    if group == "terms" and (i == 0 or not c.get("term")):
        problems.append("%s: a defined term stands after token 0 and names its term" % name)
    return problems


def quota_problems(product, sentences, floors=None, old_counts=None, family_of=None):
    """[message] for every miss of the quota gate (floors are waivable by `floors={}`, caps and the per-sentence rules never).

    `old_counts` are the head lemmas of the old compound targets (the union of the S1 lemma cap); `family_of` {source_id: title family} for the S3 family cap."""
    group, side = GROUP[product], SIDE[product]
    waive = floors == {}
    found, comments = [], [fu.comments_of(s) for s in sentences]
    judgments = [judged(product, s) for s in sentences]
    kinds = collections.Counter(kind for _, kind, _ in judgments)
    for name, names, floor, cap in (DEV_QUOTAS if side == "dev" else TRAIN_QUOTAS)[product]:
        count = sum(kinds[k] for k in names)
        if count < floor and not waive:
            found.append("%s: %d, the floor is %d" % (name, count, floor))
        if cap is not None and count > cap:
            found.append("%s: %d, the cap is %d" % (name, count, cap))
    if side == "train":
        low, high = TRAIN_SHARES[group]
        contrast = sum(kinds[k] for k in CONTRAST_KINDS[group])
        if contrast * 100 > high * len(sentences):
            found.append("contrast strata: %d of %d sentences, at most %d percent allowed" % (contrast, len(sentences), high))
        if contrast * 100 < low * len(sentences) and not waive:
            found.append("contrast strata: %d of %d sentences, at least %d percent needed" % (contrast, len(sentences), low))
    is_control = [kind in CONTROL_KINDS[group] for _, kind, _ in judgments]
    if side == "dev":
        target_lemmas = collections.Counter(lemma for (_, _, lemma), control in zip(judgments, is_control) if not control)
        control_lemmas = collections.Counter(lemma for (_, _, lemma), control in zip(judgments, is_control) if control)
        union = collections.Counter(old_counts or ())
        union.update(target_lemmas)
        found += ["target lemma %s has %d items, at most %d allowed" % (lemma, n, LEMMA_CAP[product]) for lemma, n in sorted(union.items())
                  if n > LEMMA_CAP[product] and lemma in target_lemmas]
        found += ["control lemma %s has %d items, at most %d allowed" % (lemma, n, CONTROL_LEMMA_CAP) for lemma, n in sorted(control_lemmas.items()) if n > CONTROL_LEMMA_CAP]
    else:
        found += ["lemma %s has %d items, at most %d allowed" % (lemma, n, LEMMA_CAP[product])
                  for lemma, n in sorted(collections.Counter(lemma for _, _, lemma in judgments).items()) if n > LEMMA_CAP[product]]
    found += ["document %s has %d items, at most %d allowed" % (doc, n, DOCUMENT_CAP[side])
              for doc, n in sorted(collections.Counter(s["source_id"] for s in sentences).items()) if n > DOCUMENT_CAP[side]]
    if group == "terms":
        found += ["term %s has %d items, at most %d allowed" % (term, n, TERM_CAP[side]) for term, n in sorted(collections.Counter(c.get("term", "") for c in comments).items())
                  if n > TERM_CAP[side]]
    if group == "steps":
        if side == "dev" and family_of:
            found += ["title family %s has %d items, at most %d allowed" % (family, n, FAMILY_CAP)
                      for family, n in sorted(collections.Counter(family_of.get(s["source_id"], "") for s in sentences).items()) if n > FAMILY_CAP]
        imperatives = [c for c, (_, kind, _) in zip(comments, judgments) if kind == "imperative"]
        for label, count, percent in (("bare-object", sum(c.get("frame") == "bare" for c in imperatives), BARE_MIN_PERCENT),
                                      ("enumerated or bulleted", sum(c.get("unit") in LABELED_UNITS for c in imperatives), LABELED_MIN_PERCENT)):
            if imperatives and count * 100 < percent * len(imperatives) and not waive:
                found.append("%s lines: %d of %d imperatives, at least %d percent needed" % (label, count, len(imperatives), percent))
    for sentence in sentences:
        found += sentence_problems(product, sentence)
    return found


def cross_side_problems(data_dir, product, built):
    """[message] for a document of the built product that is also in the source table of a product of the other side (DEV and TRAIN stay disjoint)."""
    own_ids = {s["source_id"] for s in built}
    found = []
    for other in PRODUCTS:
        if SIDE[other] == SIDE[product]:
            continue
        path = os.path.join(data_dir, *OUTPUT[other][0].split("/"), OUTPUT[other][2])
        if os.path.exists(path):
            found += ["document %s is in both %s and %s" % (d, product, other) for d in sorted(own_ids & {r["source_id"] for r in adjudicate.read_records(path)[1]})]
    return found


def cross_product_problems(out_dir, product, built):
    """[message] for a sentence of the built product that another built product of the same side (a corpus already in `out_dir`) also holds: the four tranches share no sentence."""
    keys = {shapes_dev.sent_key_of(s["text"]) for s in built}
    found = []
    for other in PRODUCTS:
        path = os.path.join(out_dir, OUTPUT[other][1])
        if other == product or SIDE[other] != SIDE[product] or not os.path.exists(path):
            continue
        shared = keys & {shapes_dev.sent_key_of(s["text"]) for s in common.read_corpus(path)[1]}
        if shared:
            found.append("%d sentences of %s are also in %s" % (len(shared), product, other))
    return found


# ---------------------------------------------------------------- build

def build(work, product, out_dir, seed=SEED, allow_short=False, data_dir=None, extra_tables=(), omit=()):
    """Write the corpus and the sources table (and, for a training product, excluded-documents.tsv and the dropped-sentences table) of `product` into `out_dir`; returns (sentences, report lines).

    Fail closed: the draft must pass the source and license gate (DraftError), the S2 pilot gate must have passed (GateError), a missed quota (`BuildError`, nothing written; `allow_short`
    lowers only a floor), an excluded or unknown document, a document of the other side, a sentence that another product of the side holds. `omit` lists documents removed after the caps
    (rule 10); the four training builds take the same omissions because they share one excluded-documents.tsv."""
    data_dir = data_dir or common.DATA
    omit_reasons = chd.parse_omissions(omit)
    omit = sorted(omit_reasons)
    draft = shapes_dev.read_draft(work)
    missing = sorted(set(omit) - {row["source_id"] for row in draft})
    if missing:
        raise BuildError("cannot omit a document that is not in the draft: " + ", ".join(missing))
    draft_gate(work, data_dir, extra_tables, omit)
    group, train = GROUP[product], SIDE[product] == "train"
    if group == "terms":
        require_pilot(work)
    sides = fu.read_partition(work, draft)
    items, tagged, candidates, extras = read_product_rounds(work, product, data_dir, sides, keep=omit)
    exclusions = shapes_dev.load_exclusions(work, data_dir)
    family_of = title_family_of(draft)
    drops, old_counts = collections.Counter(), None
    labeled = len(items)
    if train:
        sentences, kept = {}, []
        for item in items:
            rows = tagged.get(item["sent_key"])
            if rows is None:
                drops["undecided"] += 1
            elif item["outcome"] != "keep":
                drops[item["reason"] or "dropped"] += 1
            elif item["disputed_tokens"] or any(common.base_status(row[2]) == "disputed" for row in rows):
                drops["disputed-token"] += 1
            else:
                sentences[item["sent_key"]] = dict(sent_id="", source_id=item["source_id"], text=candidates[item["cand_id"]]["text"], rows=[list(r) for r in rows])
                kept.append(entry_of(item, candidates, extras))
        temporary = {"tmp-%d" % n: entry for n, entry in enumerate(kept, 1)}
        fixed = chd.fix_tier_rules([dict(sentences[e["sent_key"]], sent_id=name) for name, e in temporary.items()], data_dir, group)
        after_lint = []
        for name, entry in temporary.items():
            if name in fixed:
                drops["fix-tier"] += 1
            else:
                after_lint.append(entry)
        chosen, cap_drops = select_train(product, after_lint, seed)
        drops.update(cap_drops)
        omitted = [entry for entry in chosen if entry["source_id"] in omit]
        if omitted:
            drops[chd.OMITTED] += len(omitted)
            chosen = [entry for entry in chosen if entry["source_id"] not in omit]
        ordered = sorted(chosen, key=lambda c: shapes_dev.rank_of(seed, product + "-order", c["sent_key"]))
        built = [final_sentence(product, entry, sentences[entry["sent_key"]], number) for number, entry in enumerate(ordered, 1)]
    else:
        for item in items:
            if item["source_id"] in omit:
                drops[chd.OMITTED] += 1
        entries = dev_entries([i for i in items if i["source_id"] not in omit], candidates, extras)
        old_counts = union_target_counts(data_dir) if group == "deverbal" else None
        targets, controls, cap_drops = select_dev(product, entries, family_of, old_counts, seed)
        drops.update(cap_drops)
        ordered = sorted(targets + controls, key=lambda e: (e["role"] != "target", KINDS[group].index(e["kind"]), shapes_dev.rank_of(seed, "order", e["cand_id"])))
        built = [final_sentence(product, entry, dict(sent_id="", source_id=entry["source_id"], text=entry["text"], rows=[list(r) for r in tagged[entry["sent_key"]]]), number)
                 for number, entry in enumerate(ordered, 1)]
    unknown = sorted({s["source_id"] for s in built} - {row["source_id"] for row in draft})
    if unknown:
        raise BuildError("no source row for: " + ", ".join(unknown))
    refused = exclusions.problems(draft, built)
    if refused:
        raise shapes_dev.ExcludedSourceError("excluded sources are used:\n  " + "\n  ".join(refused[:20]))
    kept_targets = [e for e in ordered if e["kind"] not in CONTROL_KINDS[group]]
    np_kept = (sum(1 for e in kept_targets if is_np(e)), len(kept_targets))
    wrong_side = sorted({s["source_id"] for s in built if sides.get(s["source_id"]) != SIDE[product]})
    cross = (cross_side_problems(data_dir, product, built) + ["document %s is not on the %s side" % (d, SIDE[product]) for d in wrong_side]
             + cross_product_problems(out_dir, product, built))
    if cross:
        raise BuildError("DEV and TRAIN documents must be disjoint, and so must the products of a side:\n  " + "\n  ".join(cross[:20]))
    problems = quota_problems(product, built, {} if allow_short else None, old_counts, family_of)
    if problems:
        raise BuildError("the quota gate is not met (--allow-short lowers only a floor):\n  " + "\n  ".join(problems[:20]))
    folder, corpus, sources = OUTPUT[product]
    common.write_corpus(os.path.join(out_dir, corpus), common.with_flag_documentation(header_lines(product), built), built)
    adjudicate.write_rows(os.path.join(out_dir, sources), shapes_dev.SOURCE_COLUMNS, shapes_dev.sources_rows(draft, built))
    if train:
        base = getattr(work, "base", work.root)
        rejected = {}
        for other in TRAIN_PRODUCTS:                       # one record for the four sources: the documents refused at the candidate stage of any of the four rounds
            path = shapes_dev.RoundWork(base, CANDIDATE_ROUND[other]).private_file("rejected-documents.tsv")
            if os.path.exists(path):
                for record in adjudicate.read_records(path)[1]:
                    rejected.setdefault(record["source_id"], record["reason"])
        common.write_text(os.path.join(out_dir, EXCLUDED_OUT), [
            "# Documents left out of the technical-text training sources because the document is a reprint of, or shares sentences with, a training or evaluation set, was already used, "
            "or has no verifiable federal-only basis",
            "# (only ids and reasons, never text)", "# columns = document_id, reason"] + ["%s\t%s" % (d, rejected[d]) for d in sorted(rejected)]
            + ["%s\t%s" % (document, omit_reasons[document]) for document in omit])
        common.write_text(os.path.join(out_dir, DROPPED_OUT[product]), [
            "# Candidate sentences dropped alone because they equal or nearly equal a sentence of a training or evaluation set or of the shapes development set",
            "# (counted in private/candidates-report.md of the round folder; only counts are published, never text)", "# columns = reason, sentences"]
            + chd._drop_rows(shapes_dev.RoundWork(base, CANDIDATE_ROUND[product])))
    return built, build_report(product, built, labeled, drops, draft, np_kept)


def build_report(product, built, labeled, drops, draft, np_kept=None):
    """Markdown counts of a build: drops per reason, per family, per kind, lemma and document statistics (S4: `np_kept` = (np targets, targets), AMENDMENT A2)."""
    family = {row["source_id"]: row["family"] for row in draft}
    kinds = collections.Counter(kind_label(product, judged(product, s)[1]) for s in built)
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
    if np_kept is not None and GROUP[product] == "clauses":
        lines.append("- np targets (a direct object without a determiner after the word, amendment A2): %d of %d targets (at most %d percent)" % (np_kept[0], np_kept[1], NP_SHARE_MAX_PERCENT))
    return lines


# ---------------------------------------------------------------- hand check

def build_sample(product, sentences, seed=None, size=SAMPLE_SIZE, controls=SAMPLE_CONTROLS):
    """[(kind, sentence, shown tag)] shuffled: the items of SAMPLE_PLAN (fewer when a kind is short) with the agreed tag, and `controls` other items with the other class shown."""
    rng = random.Random(SAMPLE_SEEDS[product] if seed is None else seed)
    ordered = sorted(sentences, key=lambda s: s["sent_id"])
    picked = []
    for kind, count in SAMPLE_PLAN[product]:
        wanted = count if size == SAMPLE_SIZE else max(1, round(count * size / SAMPLE_SIZE))          # a smaller `--size` scales every kind
        members = [s for s in ordered if judged(product, s)[1] == kind]
        picked += rng.sample(members, min(wanted, len(members)))
    rest = [s for s in ordered if s not in picked]
    swapped = rng.sample(rest, min(controls, len(rest)))
    result = [("real", s, s["rows"][judged(product, s)[0]][1]) for s in picked]
    result += [("control", s, fu.swapped_tag(s["rows"][judged(product, s)[0]][1])) for s in swapped]
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
    kinds = collections.Counter(kind_label(product, judged(product, s)[1]) for s in sentences)
    lemmas = collections.Counter(judged(product, s)[2] for s in sentences)
    lines = ["| family | documents | sentences |", "| --- | --- | --- |"]
    lines += ["| %s | %d | %d |" % (name, sum(1 for d in documents if draft[d]["family"] == name), families[name]) for name in sorted(families)]
    lines += ["", "- sentences: %d, tokens: %d, lemmas: %d, documents: %d" % (len(sentences), sum(len(s["rows"]) for s in sentences), len(lemmas), len(documents)),
              "- kinds: " + ", ".join("%s %d" % item for item in sorted(kinds.items())),
              "- most items per lemma: %d, per document: %d" % (max(lemmas.values(), default=0), max(documents.values(), default=0))]
    return lines


# ---------------------------------------------------------------- the commands

def _round_work(args, name):
    round_work = shapes_dev.RoundWork(args.work, name)
    os.makedirs(round_work.private, exist_ok=True)
    return round_work


def _out_dir(args):
    return args.out_dir or os.path.join(args.data_dir, *OUTPUT[args.product][0].split("/"))


def _review_folder(args):
    path = os.path.join(args.work, "review", args.product)
    os.makedirs(path, exist_ok=True)
    return path


def read_hmm_pinned(work):
    """The HmmTable of the pinned W/hmm.tsv (S1, S3 and S4 development products; `require_pins` already checked the hashes)."""
    return shapes_dev.read_hmm(work, expected_sha256=HMM_SHA256)


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


def _command_lines(args):
    return fu._command_lines(args)


def _command_tokenize(args):
    return sx._command_tokenize(args)


def _command_preflight(args):
    """Counts only: documents, sides, the clean units and the frame hits (first kind of a unit) of every shape on each side; nothing is written or labeled."""
    work = adjudicate.Work(args.work)
    draft = shapes_dev.read_draft(work)
    draft_gate(work, args.data_dir, args.extra_table or ())
    sides = fu.read_partition(work, draft)
    lex = shapes_dev.lexicon_of(args.data_dir)
    vocab = shapes_dev.vocabulary_of(args.data_dir, lex)
    filt = shapes_dev.LeakageFilter(args.data_dir, not args.no_ud, new_tranches=True, skip_tranches=TECHNICAL_TRANCHES)
    reference = Reference(args.data_dir, args.extra_text or ())
    sentences = scan_all(work, args.data_dir, "sentence", args.extra_table or (), (), not args.no_ud, filt, reference, lex, vocab)
    kinds_path = shapes_dev.shared_path(work, STEP_KINDS_FILE)
    step_kinds = read_step_kinds(work) if os.path.exists(kinds_path) else None
    steps = scan_all(work, args.data_dir, "step", args.extra_table or (), (), not args.no_ud, filt, reference, lex, vocab, step_kinds) if step_kinds is not None else None
    count = collections.Counter(sides[row["source_id"]] for row in draft)
    print("documents in the draft: %d (dev %d, train %d; %d agency families); rejected as a whole: %d"
          % (len(draft), count["dev"], count["train"], len({r["family"] for r in draft}), len(sentences.rejected)))
    print("clean sentences: %d; clean step units: %s" % (len(sentences.units), "not tokenized yet (run `steps`)" if steps is None else str(len(steps.units))))
    for side in ("dev", "train"):
        parts = ["%s: %d clean sentences" % (side, len(fu.restrict(sentences, sides, side).units))]
        for group in KINDS:
            scan = fu.restrict(steps, sides, side) if group == "steps" and steps is not None else fu.restrict(sentences, sides, side)
            if group == "steps" and steps is None:
                continue
            first = collections.Counter()
            for _, _, text, tokens in scan.units:
                unit = (step_kinds or {}).get(shapes_dev.sent_key_of(text), "sentence") if group == "steps" else "sentence"
                found = frame_hits(group, tokens, lex, unit in LABELED_UNITS)
                kind = next((k for k in PRIORITY[group] if k in found), None)
                first[kind] += kind is not None
                if group == "clauses":                                          # AMENDMENT A2: the np target frame (counted apart; the noun control frame takes most of these first)
                    both = np_second_kind(group, kind, found, TARGET_KINDS[group])
                    first["np"] += both is not None or (kind in TARGET_KINDS[group] and is_np(found[kind]._asdict()))
                    first["np+noun"] += both is not None
            parts.append("%s %s" % (group, ", ".join("%s %d" % (k, first[k]) for k in PRIORITY[group])
                                    + ((" (np target frame %d, of them in the noun control frame %d)" % (first["np"], first["np+noun"])) if group == "clauses" else "")))
        print("; ".join(parts))
    return 0


def _command_bands(args):
    """Counts only: the development target hits of S1, S3 and S4 by band of the Basic mass (pinned hmm.tsv; no label, no PartOfSpeech model); nothing is written."""
    work = adjudicate.Work(args.work)
    require_pins(work, "step-lines", args.data_dir)
    draft = shapes_dev.read_draft(work)
    draft_gate(work, args.data_dir, args.extra_table or ())
    sides = fu.read_partition(work, draft)
    lex = shapes_dev.lexicon_of(args.data_dir)
    vocab = shapes_dev.vocabulary_of(args.data_dir, lex)
    filt = shapes_dev.LeakageFilter(args.data_dir, not args.no_ud, new_tranches=True, skip_tranches=TECHNICAL_TRANCHES)
    reference = Reference(args.data_dir, args.extra_text or ())
    hmm = read_hmm_pinned(work)
    step_kinds = read_step_kinds(work)
    sentences = fu.restrict(scan_all(work, args.data_dir, "sentence", args.extra_table or (), (), not args.no_ud, filt, reference, lex, vocab), sides, "dev")
    steps = fu.restrict(scan_all(work, args.data_dir, "step", args.extra_table or (), (), not args.no_ud, filt, reference, lex, vocab, step_kinds), sides, "dev")
    print("development target hits by band of the Basic mass q of the judged word (counts only)")
    print("| product | " + " | ".join(Q_BAND_LABELS) + " | all | in the band 0.10 <= q < 0.97 | kept by the rule in force |")
    print("| --- | " + " | ".join("---:" for _ in Q_BAND_LABELS) + " | ---: | ---: | ---: |")
    for product in BANDED:
        counts = band_counts(steps if GROUP[product] == "steps" else sentences, product, lex, hmm, step_kinds)
        kept = sum(counts[i] for i in range(len(Q_BAND_LABELS))) - counts[0] - (0 if product in LOWER_BOUND_ONLY else counts[4] + counts[5])
        print("| %s | %s | %d | %d | %d |" % (product, " | ".join(str(counts[i]) for i in range(len(Q_BAND_LABELS))), sum(counts.values()), counts[1] + counts[2] + counts[3], kept))
    return 0


def _command_pilot(args):
    work = adjudicate.Work(args.work)
    require_pins(work, None, args.data_dir)
    later = later_rounds(work, PILOT_ROUND, ("dev", "train"))
    if later:
        raise ValueError("the pilot cannot run while the later round(s) %s already have candidates" % ", ".join(later))
    draft = shapes_dev.read_draft(work)
    draft_gate(work, args.data_dir, args.extra_table or ())
    lex = shapes_dev.lexicon_of(args.data_dir)
    scan = scan_all(work, args.data_dir, "sentence", args.extra_table or (), args.extra_text or (), not args.no_ud, lex=lex)
    documents = None
    if args.documents:
        if not os.path.exists(args.documents):
            raise ValueError("--documents is not a file: " + args.documents)
        documents = {line.strip() for line in common.read_lines(args.documents) if line.strip() and not line.startswith("#")}
        unknown = documents - {row["source_id"] for row in draft}
        if unknown:
            raise ValueError("--documents names documents that are not in the draft: " + ", ".join(sorted(unknown)[:5]))
    rows, report, counts = pilot_candidates(scan, lex, title_family_of(draft), args.seed, documents)
    round_work = _round_work(args, PILOT_ROUND)
    common.write_text(round_work.private_file("candidates-report.md"), report)
    shapes_dev.write_candidates(round_work, rows)
    write_extras(round_work, rows)
    if os.path.exists(round_work.private_file(PILOT_VERDICT_FILE)):
        os.remove(round_work.private_file(PILOT_VERDICT_FILE))              # a new pilot sample invalidates an earlier verdict
    print("\n".join(report))
    print("%d candidates written to rounds/%s/candidates.tsv" % (len(rows), PILOT_ROUND))
    if counts["kinds"].get("noun", 0) < PILOT_NOUN_MIN:
        print("PILOT SCARCITY: " + pilot_verdict(0, counts["kinds"].get("noun", 0))[1], file=sys.stderr)
        return 1
    return 0


def _command_pilot_result(args):
    work = adjudicate.Work(args.work)
    items, extras = pilot_items(work, args.data_dir)
    confirmed, nouns, lines = pilot_numbers(items, extras)
    verdict, message = pilot_verdict(confirmed, nouns)
    lines += ["", "gate: " + verdict + " (" + message + ")"]
    if verdict != "PROCEED":
        lines += ["", "S2 (defined terms) stops here: nothing is drawn or built for defined-terms and terms; the other three shapes do not depend on this gate (decision O2)"]
    adjudicate._print(lines)
    round_work = _round_work(args, PILOT_ROUND)
    common.write_text(round_work.private_file("pilot-record.md"), lines)
    common.write_text(round_work.private_file(PILOT_VERDICT_FILE), ["%s %s" % (verdict, message)])
    return 0 if verdict == "PROCEED" else 1


def _command_candidates(args):
    product = args.product
    work = adjudicate.Work(args.work)
    group, side = GROUP[product], SIDE[product]
    controls_round = args.round is not None
    if controls_round and (side != "dev" or args.round != CONTROLS_ROUND[product]):
        raise ValueError("--round %s is not the control top-up round of %s (%s)" % (args.round, product, CONTROLS_ROUND.get(product, "none: a training product has none")))
    require_pins(work, product, args.data_dir)
    if group == "terms":
        require_pilot(work)
    name = CONTROLS_ROUND[product] if controls_round else CANDIDATE_ROUND[product]
    later = later_rounds(work, name, (side,))
    if later:
        raise ValueError("round %s cannot run while the later round(s) %s already have candidates: run the rounds in order" % (name, ", ".join(later)))
    draft = shapes_dev.read_draft(work)
    draft_gate(work, args.data_dir, args.extra_table or ())
    sides = fu.read_partition(work, draft)
    lex = shapes_dev.lexicon_of(args.data_dir)
    step_kinds = read_step_kinds(work) if group == "steps" else None
    full = scan_all(work, args.data_dir, "step" if group == "steps" else "sentence", args.extra_table or (), args.extra_text or (), not args.no_ud, lex=lex, step_kinds=step_kinds)
    scan = fu.restrict(full, sides, side)
    prior = pilot_rows_of_side(work, sides, side) if group == "terms" else []
    old_counts = union_target_counts(args.data_dir) if group == "deverbal" and side == "dev" else None
    room = None
    if controls_round:
        main = shapes_dev.RoundWork(args.work, CANDIDATE_ROUND[product])
        if not os.path.exists(main.private_file("decided.tsv")):
            raise ValueError("decide the round %s first (the control top-up round needs its kept items)" % CANDIDATE_ROUND[product])
        items, _, candidates, extras = read_product_rounds(work, product, args.data_dir, sides, rounds=ROUNDS[product][:-1])
        room = control_room(product, items, candidates, extras, old_counts, title_family_of(draft), args.seed)
    hmm = read_hmm_pinned(work) if product in BANDED and not controls_round else None
    rows, report, counts = candidate_rows(scan, product, lex, args.seed, hmm, old_counts, prior, earlier_keys(work, name, side), step_kinds, controls_round, room)
    round_work = _round_work(args, name)
    common.write_text(round_work.private_file("candidates-report.md"), report)
    adjudicate.write_rows(round_work.private_file("rejected-documents.tsv"), ("source_id", "reason"), sorted(scan.rejected.items()))
    shapes_dev.write_candidates(round_work, rows)
    write_extras(round_work, rows)
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
    except (BuildError, GateError, shapes_dev.ExcludedSourceError, ValueError) as error:    # DraftError is a ValueError
        print("build refused: %s" % error, file=sys.stderr)
        return 1
    adjudicate._print(report)
    common.write_text(os.path.join(_review_folder(args), "build-report.md"), report)
    return 0


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
    sub.add_parser("lines", help="heading lines of every document (W/lines)")
    sub.add_parser("steps", help="step units of every document (W/steps and W/step-kinds.tsv)")
    tokenize = sub.add_parser("tokenize", help="tokenize every sentence and unit with the library tokenizer")
    tokenize.add_argument("--cmd", default=None)
    for name, help_text in (("preflight", "counts only"), ("bands", "counts only: the development target hits of S1, S3 and S4 by band of the Basic mass"), ("pilot", "the S2 pilot round dtm-pilot"),
                            ("candidates", "the candidates of a product")):
        p = sub.add_parser(name, help=help_text)
        options(p, product=name == "candidates")
        p.add_argument("--extra-text", action="append", default=None, metavar="DIR")
        p.add_argument("--no-ud", action="store_true")
        if name == "pilot":
            p.add_argument("--documents", default=None, help="a file with the ids of the first-wave documents (one per line)")
        if name == "candidates":
            p.add_argument("--round", default=None, help="dvhc, dtmc, stpc or clvc: the control top-up round of a development product")
    sub.add_parser("pilot-result", help="the S2 pilot gate; exit 1 when S2 must stop")
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
    if getattr(args, "seed", None) is None and args.command in ("preflight", "bands", "pilot", "candidates", "build"):
        args.seed = SEED
    handlers = {"exclusions": _command_exclusions, "partition": _command_partition, "lines": _command_lines, "steps": _command_steps, "tokenize": _command_tokenize,
                "preflight": _command_preflight, "bands": _command_bands, "pilot": _command_pilot, "pilot-result": _command_pilot_result, "candidates": _command_candidates,
                "build": _command_build, "sample": _command_sample, "review-result": _command_review_result, "report": _command_report, "prompt": _command_prompt}
    try:
        return handlers[args.command](args)
    except (ValueError, shapes_dev.ExcludedSourceError) as error:        # DraftError, PinError and GateError are ValueErrors
        print("%s refused: %s" % (args.command, error), file=sys.stderr)
        return 1


if __name__ == "__main__":
    sys.exit(main())

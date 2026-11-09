"""Builder of the three newer tranches of the shapes development set data/eval/shapes-dev/ (Python 3, standard library only).

The set of data/eval/shapes-dev/ (dev.tsv, hard.tsv) measures the shapes of the earlier tranches. Three more development tranches, each its own
file, measure shapes that those files do not hold. This tool does everything around the blind model labelers that needs no model, and never
calls one. It imports `shapes_dev` (frames, cleanliness filter, leakage filter, exclusions, decision rule) and `imperative_data` (the used-document
index) read-only, and no PartOfSpeech tagger, decoder, weights file, prediction or score: a test scans its source for that.

THE PRE-REGISTERED RULE (written and committed before any candidate or label of these tranches exists; the commit is recorded in
data/eval/shapes-dev/README.md and in the trainer RUN-LOG.md)

The three shapes (tranche ids `compound-head`, `headings`, `named-state`; files `<id>.tsv`, `<id>-sources.tsv`, `<id>-leakage-report.tsv`; ids
`shapes-dev-<id>-NNNN`; the header of a file says `<id> tranche` and `dev split`; the shape ids are in the `# shape` comment):

  n-compound-head-ambiguous (class Noun). The judged word is the head, the last noun, of a noun + noun compound, in a spelling that the lexicon
      lists as NOUN and VERB (`check`, `split`, `stand`, `reach`, `finding`, `register`, `estimate`, ...). The existing shape `n-compound-head`
      tests the head after a modifier noun in the frame of `shapes_dev.shape_hits` (token before the modifier is a determiner, a preposition, a
      conjunction, an auxiliary or a mark; next token the end or verb-like). The new shape is the COMPLEMENT of that frame at the target token:
      compounds of three or more nouns, a compound after an adjective or another noun-adjective chain, a compound at the start of the
      sentence (head at index 1), a head that an adverb or mark ends, and `-ing` heads that the lexicon lists as nouns. The two shapes
      therefore do not overlap at a token, and no sentence of dev.tsv or hard.tsv is a candidate (exact key, or a sentence of at least 10 words
      with at least 80% of its word 6-grams in the sentences of those files of the same document). Frame (`compound_ambiguous_hit`): target
      lower case, NOUN and VERB listed, no function word; the token before passes `noun_like_modifier` (strict) and is lower case or the first
      token; the next token is the end, a mark or no noun-capable word; the existing frame does not hit at the target. The nominal-gerund
      rule of the older noun shapes is NOT applied (`finding` is wanted), `-ing` heads are at most 25% of the built targets, and the
      README reports them separately. Selection: the contested band of the WordSense Basic classifier as in the hard tranche: q = the mass of the
      class Noun at the target in hmm.tsv; eligible iff 0.10 <= q < 0.97; order q ascending (hardest first), ties by the seeded rank, at most 4
      per lemma; budget 600 candidates. Controls: the same lemma as a verb (`control_hits` noun branch: after a modal or `to`, or an imperative
      start), no band, at most 2 per lemma, 50 candidates. Quota gate: targets floor 80 and cap 100, controls floor 15 and cap 20.

  heading-titlecase (exact tags). Title Case headings, captions, table headers and step titles: a LINE of the source text, not a sentence, 2 to 10
      tokens, no final period, usually without final punctuation. The line is stored without its numbering or label (`2.1`, `Step 3.`,
      `Table 2.`). Extraction (`heading_lines`, text only): a line preceded by a blank line whose next non-blank line is a paragraph line (40
      characters, 6 words), at most 90 characters, not printed three or more times in the document (running header), no dot leaders or trailing page
      number. Filter (`heading_noise_reason`): ASCII, no junk, balanced brackets, not ALL CAPS, every content word capitalized, no broken last word,
      every word in a word list (capitalized words included), one sentence by the library tokenizer, not equal to or a 5-gram of any training or
      evaluation sentence. Frames (`heading_hits`, position and lexicon only; the capitalization itself is the stress, so there is no band):
      noun targets: a non-initial capitalized token whose lower-case spelling the lexicon lists as NOUN and VERB, ending its noun phrase (next token
      the end, a mark, a preposition or a conjunction); gold NOUN exactly (a common noun tagged PROPN is wrong for this shape). Imperative targets:
      token 0, capitalized, NOUN and VERB listed, its own base form, token 1 a determiner, object pronoun, adverb or particle (stronger) or a
      capitalized noun-capable word; gold VERB. Controls: a non-initial capitalized token whose lower-case spelling the lexicon lists as PROPN only
      (`Washington`-like names); gold PROPN; at most 2 per lemma. Candidate budgets 400 per target kind and 60 controls, at most 4 per lemma, at most 20 per
      document. The built set takes at most 4 per lemma and 12 per document over both target kinds. Quota gate: noun targets floor 40 and cap 50,
      imperative targets floor 40 and cap 50, controls floor 12 and cap 20. The file carries `# kind = noun | imperative | proper` per item.

  n-named-state (class Noun). A capitalized or ALL-CAPS state, position or setting name after a preposition, for example a switch set to a
      position or a mechanism moved to a named position (`set to OFF`, `moved to STANDBY`). Frame (`named_state_hit`): a single token of 2 to 12
      letters, ALL CAPS (not a Roman numeral) or capitalized and then an ordinary word (its lower-case spelling is in the lexicon and not only a proper
      noun); not the first token; directly after `to`, `in`, `at`, `from`, `into` or `on` with an optional determiner between; a device cue word
      (`set`, `switch`, `selector`, `position`, `mode`, `setting`, `move`, `turn`, `lever`, `knob`, `valve`, `control`, ... see STATE_CUES, inflected forms
      too) among the 6 tokens before the preposition; and no modifier of what follows (the next token is the end, a mark, a preposition, a conjunction,
      `to`, an auxiliary or a verb-only word). Selection: the seeded order of all frame hits (no band: the pool is thin, a band would shrink it
      further), at most 4 per lemma, budget 500 candidates. Controls: the same lemma (lower case) as a verb after a modal or `to`, at most 2 per
      lemma, 50 candidates. Quota gate: targets floor 40 and cap 100, controls floor 12 and cap 20; a miss below the floor is disclosed, never padded
      (no sentence is authored).

Common rules. Inputs: real text of federal documents that are new (no id, URL or archive.org identifier in any table of
`imperative_data.used_index`, the shapes-dev tables and the imperative source; no FAA; nothing in excluded-sources.tsv; a document with 3 sentences that are
also in a training or evaluation set, in dev.tsv, hard.tsv or imperative.tsv is rejected as a reprint); the held-out sets enter only through
`leakage.held_out_overlap_keys` (through `shapes_dev.LeakageFilter`). Cleanliness, duplicates and the length of a sentence (3 to 30 tokens) are those of
`shapes_dev`. No model of the project takes part in the selection. The WordSense Basic numbers (hmm.tsv) are used only for `compound-head`; the pins are
`HMM_SHA256` and `TOKENS_SHA256` below (and the WordSense commit and DLL SHA-256 of `shapes_dev`): `read_hmm_pinned` refuses any other bytes. Gold: two blind
labelers (`claude-opus-5.5`, whose tag breaks ties for non-target tokens, and `gpt-6.1-sol`) tag every token of every unit blind (the batches hold an
opaque id, an index and a token) through the unchanged commands `shapes_dev.py export`, `check-batches`, `validate-output` and `decide` with `--round <tranche>`;
the decision rule is that of `shapes_dev` (a target disagreement drops the item, as does a class mismatch; the frame rules of `frame_mismatch` for the new
shapes; the exact tag for the heading shapes). No label is edited by hand. Hand check: 60 targets (20 `n-compound-head-ambiguous`, 10 + 10 heading nouns
and imperatives, 20 `n-named-state`, fewer where a shape has fewer) and 4 swapped controls, shown with all tags to a third model (`claude-sonnet-5.5`; a
model review, not a human one); the gate is a precision of 95% (3 incorrect of 60 is exactly 95.0%). Build: `build` writes a tranche only when its quota gate is
met (`--allow-short` writes it anyway and lists the misses; the exclusion record is read through `shapes_dev.load_exclusions` and never rewritten).

Work folder W (`--work W`, outside the repository): the layout of shapes_dev.py (W/sources-draft.tsv, W/text/<id>.txt one sentence per line, W/tokens.tsv, W/hmm.tsv,
W/excluded-sources.tsv) plus W/raw/<id>.txt (the OCR or PDF text of a document), W/lines/<id>.txt (heading lines, written by `lines`) and one round folder
W/rounds/<tranche> per tranche (candidates.tsv, batches/, private/).

Commands:

  python tools/data/shapes_dev_extra.py --work W lines                         # heading lines of every document of the draft
  python tools/data/shapes_dev_extra.py --work W tokenize [--cmd COMMAND]       # tokens of every sentence and heading line (the C# shapes-tokenize command)
  python tools/data/shapes_dev_extra.py --work W preflight [--tranche T ...] [--hmm-sha256 SHA] [--extra-table PATH ...]   # pool sizes, counts only
  python tools/data/shapes_dev_extra.py --work W candidates --tranche T [--extra-table PATH ...] [--budget N]   # W/rounds/T/candidates.tsv
  (then, for each round: shapes_dev.py --work W export|check-batches|validate-output|status|decide --round T, the unchanged blind labeling)
  python tools/data/shapes_dev_extra.py --work W report [--tranche T]           # decisions per shape and role
  python tools/data/shapes_dev_extra.py --work W sample                          # the hand-check sample (the items that the build takes)
  python tools/data/shapes_dev_extra.py --work W review-result --verdicts FILE ...
  python tools/data/shapes_dev_extra.py --work W build [--tranche T] [--out-dir D] [--allow-short]
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
import imperative_data
import shapes_dev

SEED = shapes_dev.SEED
TRANCHES = ("compound-head", "headings", "named-state")
FILES = {name: (name + ".tsv", name + "-sources.tsv", name + "-leakage-report.tsv") for name in TRANCHES}
SHAPE_OF = {"compound-head": "n-compound-head-ambiguous", "headings": "heading-titlecase", "named-state": "n-named-state"}
SENT_ID = {name: "shapes-dev-%s-%%04d" % name for name in TRANCHES}
CAND_PREFIX = {"compound-head": "ch-", "headings": "hd-", "named-state": "ns-"}
# candidate shapes (the internal `shape` of candidates.tsv and decided.tsv): the headings tranche has three, see KIND_OF
CANDIDATE_SHAPES = {"compound-head": ("n-compound-head-ambiguous",), "headings": ("heading-noun", "heading-imperative", "heading-proper"),
                    "named-state": ("n-named-state",)}
KIND_OF = {"heading-noun": "noun", "heading-imperative": "imperative", "heading-proper": "proper"}
HEADING_ROLE = {"heading-noun": "target", "heading-imperative": "target", "heading-proper": "control"}
SAMPLE_KEY_COLUMNS = ("item", "kind", "cand_id", "shape", "target", "shown_tag", "agreed_tag", "tranche")

# pre-registered pins (see the module docstring and data/eval/shapes-dev/README.md): the SHA-256 of the bytes of the work folder's tokens.tsv
# (the harness input) and hmm.tsv (the WordSense Basic mass of every token; used by `compound-head` only)
TOKENS_SHA256 = "5b800937b439b53a54c76d270c23ca510a26967716c45f748aa31cd658a8b055"
HMM_SHA256 = "f20d0952ab09b838c32fa56d4c14f45743db60d74db0fefaabea5150499ed6f1"

# pre-registered selection constants
BAND_MIN, BAND_MAX = shapes_dev.BAND_MIN, shapes_dev.BAND_MAX
COMPOUND_BUDGET = 600
CONTROL_POOL = 50
NAMED_STATE_BUDGET = 500
HEADING_BUDGET = 400
HEADING_CONTROL_POOL = 60
LEMMA_CAP = 4
CONTROL_LEMMA_CAP = 2
HEADING_DOCUMENT_CAP_CANDIDATES = 20
HEADING_DOCUMENT_CAP = 12
ING_SHARE = 0.25
HEADING_MIN_TOKENS, HEADING_MAX_TOKENS = 2, 10
SENTENCE_MIN_TOKENS, SENTENCE_MAX_TOKENS = shapes_dev.MIN_TOKENS, shapes_dev.MAX_TOKENS
# (floor, cap) per tranche and stratum; a miss below a floor refuses the build unless --allow-short (disclosed, never padded)
QUOTAS = {
    "compound-head": {"targets": (80, 100), "controls": (15, 20)},
    "headings": {"noun": (40, 50), "imperative": (40, 50), "controls": (12, 20)},
    "named-state": {"targets": (40, 100), "controls": (12, 20)},
}
PRECISION_MIN = shapes_dev.PRECISION_MIN
CONTROLS_ACCEPTED_MAX = shapes_dev.CONTROLS_ACCEPTED_MAX
SAMPLE_PLAN = (("n-compound-head-ambiguous", 20), ("heading-noun", 10), ("heading-imperative", 10), ("n-named-state", 20))
SAMPLE_CONTROLS = 4
MODEL_FREE = "no PartOfSpeech model, decoder, weights file, prediction or score is read"

STATE_PREPOSITIONS = frozenset("to in at from into on".split())
STATE_CUES = frozenset(
    "set switch selector position mode setting move turn place return lever knob valve control dial handle key button latch gear flap "
    "throttle select pull push hold release rotate shift adjust change toggle indicator breaker rheostat gate door shutter".split())
STATE_STOP_CAPS = frozenset("THE AN TO OF AND OR IN AT BY FOR IS ARE IT AS IF SO BE WE HE".split())
HEADING_FUNCTION_WORDS = shapes_dev.DETERMINERS | shapes_dev.PREPOSITIONS | shapes_dev.CONJUNCTIONS | frozenset(
    "to is are as vs per not nor".split())
HEADING_ALLOWED = re.compile(r"[A-Za-z0-9 ,'()\-]*")
HEADING_LABEL = re.compile(
    r"^(?:(?:Step|STEP|Chapter|CHAPTER|Section|SECTION|Table|TABLE|Figure|FIGURE|Fig\.?|Paragraph|Part|PART|Appendix|APPENDIX|Para\.?)"
    r"\s+(?:[0-9]+[A-Za-z]?|[IVXL]+|[A-Z])(?:[.\-][0-9A-Za-z]+)*[.:\-]?\s+|[0-9]{1,2}(?:[.\-][0-9]{1,3})*\.?\s+|\([a-z0-9]{1,3}\)\s+)")
HEADING_NOISE_RULES = ("length", "start-end", "chars", "junk", "brackets", "enumeration", "repeat", "caps", "case", "oov")
HEADING_MAX_CHARS = 90


# ---------------------------------------------------------------- heading lines (line-level extraction of OCR text)

def _line_key(line):
    return re.sub(r"[^a-z0-9]", "", line.lower())


def strip_heading_label(text):
    """The heading without its numbering or label (`2.1`, `Step 3.`, `Table 2.`, `(a)`); the text itself when it has none."""
    return HEADING_LABEL.sub("", text.strip(), count=1).strip()


def heading_lines(raw):
    """[heading text] of the OCR text `raw`: the lines that stand alone, in the order of the document.

    A line is a candidate when it is preceded by a blank line (or starts the text), is at most HEADING_MAX_CHARS characters long and the next
    non-blank line (looked for within the next two lines) is a paragraph line of at least 40 characters and 6 words (a paragraph follows a heading; a
    table row, a list item or a parts list does not). Refused: a line that occurs three or more times in the document (a running header or footer),
    a table-of-contents line (dot leaders, or a trailing page number) and a line with a tab. The numbering or label in front is removed
    (`strip_heading_label`).
    Only these light, text-only rules apply here; `heading_noise_reason` is the full filter of the candidate stage. Deterministic."""
    text = raw.replace("\r\n", "\n").replace("\r", "\n").replace("\x0c", "\n\n")
    lines = text.split("\n")
    counts = collections.Counter(_line_key(line) for line in lines if line.strip())
    found, seen = [], set()
    for number, line in enumerate(lines):
        stripped = line.strip()
        if not stripped or len(stripped) > HEADING_MAX_CHARS or "\t" in stripped:
            continue
        if number > 0 and lines[number - 1].strip():
            continue
        following = next((l.strip() for l in lines[number + 1:number + 3] if l.strip()), "")
        if len(following) < 40 or len(following.split()) < 6:
            continue
        if counts[_line_key(stripped)] >= 3 or re.search(r"\.{2,}|(?:\s\.){2,}", stripped):
            continue
        heading = shapes_dev.normalize_text(strip_heading_label(stripped))
        if not heading or re.search(r"\s\d{1,4}$", heading) or heading in seen:
            continue
        seen.add(heading)
        found.append(heading)
    return found


def heading_oov(tokens, vocab):
    """The alphabetic tokens of a heading whose lower-case spelling is in no word list (every word is checked, capitalized ones too)."""
    found = []
    for token in tokens:
        if not shapes_dev.is_alpha_word(token) or token.startswith("'"):
            continue
        low = token.lower()
        if low in vocab or all(part in vocab for part in re.split(r"[-']", low) if part):
            continue
        found.append(token)
    return found


def heading_noise_reason(text, tokens, lex, vocab):
    """The rule of HEADING_NOISE_RULES that rejects a heading line, or None when it is clean (the pre-registered heading filter).

    length       fewer than HEADING_MIN_TOKENS or more than HEADING_MAX_TOKENS tokens
    start-end    does not start with an upper-case letter, ends in `.`, `;`, `,`, `:` or `-`, does not end in a letter, digit or `)`, or ends in
                 a function word (a determiner, preposition or conjunction: a broken line)
    chars        a character outside ASCII letters, digits, space and `, ' ( ) -`
    junk         slash-punctuation junk, doubled or space-separated punctuation, a broken hyphen word, a lone lower-case letter other than `a`
    brackets     unbalanced parentheses
    enumeration  an inner list label (` (a) `, ` 2. x`) or an initial such as `M. `
    repeat       the same word twice in a row
    caps         an alphabetic token of two or more letters that is all capitals (ALL-CAPS headings are out of scope)
    case         a content word (not a function word of HEADING_FUNCTION_WORDS) that is not capitalized, fewer than two content words, or
                 no content word with a lower-case tail (Title Case: every content word starts with a capital)
    oov          an alphabetic word (capitalized ones too) whose lower-case spelling is in no word list (`heading_oov`)
    """
    if not HEADING_MIN_TOKENS <= len(tokens) <= HEADING_MAX_TOKENS:
        return "length"
    if not text[:1].isupper() or text[-1:] in ".;,:-" or not re.search(r"[A-Za-z0-9)]$", text) or tokens[-1].lower() in HEADING_FUNCTION_WORDS:
        return "start-end"
    if not HEADING_ALLOWED.fullmatch(text):
        return "chars"
    if (any(junk in text for junk in shapes_dev.JUNK_PUNCT) or shapes_dev.HYPHEN_JUNK.search(text) or re.search(r"(?<![A-Za-z0-9])'", text)
            or any(len(t) == 1 and t.islower() and t != "a" and t.isalpha() for t in tokens)):
        return "junk"
    if text.count("(") != text.count(")") or text.find(")") < text.find("(") and "(" in text:
        return "brackets"
    if shapes_dev.INNER_LABEL.search(text) or shapes_dev.INITIALS.search(text):
        return "enumeration"
    lows = [t.lower() for t in tokens]
    if any(a == b and shapes_dev.is_alpha_word(a) for a, b in zip(lows, lows[1:])):
        return "repeat"
    alpha = [t for t in tokens if shapes_dev.is_alpha_word(t) and not t.startswith("'")]
    if any(len(t) >= 2 and t.isupper() for t in alpha):
        return "caps"
    content = [t for t in alpha if t.lower() not in HEADING_FUNCTION_WORDS]
    if (len(content) < 2 or not all(t[:1].isupper() for t in content) or not any(len(t) >= 2 and t[1:].islower() for t in content)):
        return "case"
    if heading_oov(tokens, vocab):
        return "oov"
    return None


# ---------------------------------------------------------------- frames (position and lexicon only; never a class, never a model)

def compound_ambiguous_hit(tokens, lex):
    """The index of the head in the complement frame of `n-compound-head`, or None.

    The head (a word the lexicon lists as NOUN and VERB, no function word, lower case) stands right after a noun-like modifier
    (`noun_like_modifier`, strict: the modifier is lower case, or the first token of the sentence), and is the last noun of the compound:
    the next token is the end, a mark or no noun-capable word. The shape is the complement of the frame of `n-compound-head` at that
    token: the token is not the index at which `shape_hits` finds an `n-compound-head` hit (with `compound_strict`). So it covers a
    compound of three or more nouns, a compound after another modifier, a compound at the start of the sentence, a head that a
    following adverb or mark ends, and an `-ing` head that the lexicon lists as a noun. At most one hit per sentence (the first)."""
    existing = {i for shape, i in shapes_dev.shape_hits(tokens, lex, compound_strict=True) if shape == "n-compound-head"}
    for i in range(1, len(tokens)):
        token = tokens[i]
        if token != token.lower() or i in existing or not shapes_dev.is_target_word(lex, token):
            continue
        prev = tokens[i - 1]
        if not shapes_dev.is_word(prev) or not shapes_dev.noun_like_modifier(lex, prev) or (prev != prev.lower() and i - 1 != 0):
            continue
        nxt = tokens[i + 1] if i + 1 < len(tokens) else None
        if nxt is not None and shapes_dev.is_word(nxt) and shapes_dev.noun_capable(lex, nxt):
            continue
        return i
    return None


def cue_stem(low):
    """True when `low` or its plain inflection (`-s`, `-es`, `-ed`, `-d`, `-ing`) is a device cue word of STATE_CUES."""
    forms = {low}
    for suffix in ("s", "es", "ed", "d", "ing"):
        if low.endswith(suffix) and len(low) > len(suffix) + 2:
            stem = low[:-len(suffix)]
            forms |= {stem, stem + "e"}
            if len(stem) > 2 and stem[-1] == stem[-2]:
                forms.add(stem[:-1])
    return bool(forms & STATE_CUES)


def is_state_form(token, lex):
    """The spelling of a state name: ALL CAPS (2 to 12 letters) or capitalized (a capital and 1 to 11 lower-case letters). A capitalized
    word must be an ordinary word (its lower-case spelling is in the lexicon and not only a proper noun); a name is no state name here."""
    low = token.lower()
    if re.fullmatch(r"[A-Z]{2,12}", token):                  # ALL CAPS: ON and OFF are function words in the lexicon but the commonest states
        return token not in STATE_STOP_CAPS and not re.fullmatch(r"[IVXLC]+", token) and low not in shapes_dev.LABEL_WORDS
    if low in shapes_dev.FUNCTION_WORDS or low in shapes_dev.LABEL_WORDS or low in shapes_dev.NUMBER_WORDS:
        return False
    if re.fullmatch(r"[A-Z][a-z]{1,11}", token):
        tags = lex.get(low)
        return bool(tags) and tags != {"PROPN"} and tags != frozenset({"PROPN"})
    return False


def named_state_hit(tokens, lex):
    """The index of a state, position or setting name in the frame of `n-named-state`, or None (the first hit of the sentence).

    The name (`is_state_form`) is not the first token; right before it stand a preposition of STATE_PREPOSITIONS (`to`, `in`, `at`,
    `from`, `into`, `on`), with an optional determiner between; a device cue word of STATE_CUES (`set`, `switch`, `position`, ...,
    inflected forms too) is among the 6 tokens before the preposition; and the name is no modifier of what follows: the next token is
    the end, a mark, a preposition, a conjunction, `to`, an auxiliary, or a word that is only a verb (and is not capitalized)."""
    for i in range(1, len(tokens)):
        if not is_state_form(tokens[i], lex):
            continue
        j = i - 1
        if tokens[j].lower() in shapes_dev.DETERMINERS and j >= 1:
            j -= 1
        if tokens[j].lower() not in STATE_PREPOSITIONS:
            continue
        if not any(cue_stem(t.lower()) for t in tokens[max(0, j - 6):j]):
            continue
        nxt = tokens[i + 1] if i + 1 < len(tokens) else None
        if nxt is not None:
            nlow = nxt.lower()
            ok = (not shapes_dev.is_word(nxt) or nlow in shapes_dev.PREPOSITIONS or nlow in shapes_dev.CONJUNCTIONS or nlow == "to"
                  or nlow in shapes_dev.AUXILIARIES
                  or (nxt == nlow and shapes_dev.has_tag(lex, nlow, "VERB") and not shapes_dev.has_tag(lex, nlow, "NOUN")))
            if not ok:
                continue
        return i
    return None


def heading_hits(tokens, lex):
    """[(candidate shape, index, strength)] of a heading: `heading-noun`, `heading-imperative` (at most one each) and `heading-proper`.

    heading-noun        a non-initial token that is capitalized (a capital and lower-case letters only), whose lower-case spelling the
                        lexicon lists as NOUN and VERB (no function word), and that ends its noun phrase: the next token is the end, a
                        mark, a preposition or a conjunction (so a following capitalized word does not continue it as a name)
    heading-imperative  token 0, capitalized, listed NOUN and VERB, its own base form (`base_form_of`), and token 1 a determiner, an
                        object pronoun, a following adverb or a particle (strength 0) or a capitalized word that can be a noun (strength 1)
    heading-proper      a non-initial capitalized token whose lower-case spelling the lexicon lists as a proper noun only (a name)"""
    hits = []
    n = len(tokens)
    for i, token in enumerate(tokens):
        low = token.lower()
        if not re.fullmatch(r"[A-Z][a-z]+", token):
            continue
        nxt = tokens[i + 1] if i + 1 < n else None
        nlow = nxt.lower() if nxt is not None else ""
        if i == 0:
            if (nxt is not None and shapes_dev.is_target_word(lex, low) and low not in shapes_dev.FUNCTION_WORDS
                    and shapes_dev.base_form_of(token, lex) == low and not any(h[0] == "heading-imperative" for h in hits)):
                if (nlow in shapes_dev.DETERMINERS or nlow in shapes_dev.OBJECT_PRONOUNS or nlow in shapes_dev.FOLLOWING_ADVERBS
                        or nlow in ("up", "down", "out", "off", "back", "away")):
                    hits.append(("heading-imperative", i, 0))
                elif re.fullmatch(r"[A-Z][a-z]+", nxt) and shapes_dev.noun_capable(lex, nxt):
                    hits.append(("heading-imperative", i, 1))
            continue
        if shapes_dev.is_target_word(lex, low) and low not in HEADING_FUNCTION_WORDS:
            if nxt is None or not shapes_dev.is_word(nxt) or nlow in shapes_dev.PREPOSITIONS or nlow in shapes_dev.CONJUNCTIONS:
                if not any(h[0] == "heading-noun" for h in hits):
                    hits.append(("heading-noun", i, 0))
        elif lex.get(low) == frozenset({"PROPN"}) and not any(h[0] == "heading-proper" for h in hits):
            hits.append(("heading-proper", i, 0))
    return hits


# ---------------------------------------------------------------- the candidate stage

class Reference:
    """The sentences of the shapes development set (dev.tsv, hard.tsv) and of the imperative training source (sentence keys and word
    6-grams), to keep every candidate disjoint from them: `equals-reference`, `near-reference`."""

    def __init__(self, data_dir):
        self.inner = imperative_data.ShapesDevSentences(data_dir)
        path = os.path.join(data_dir, "imperative", "imperative.tsv")
        if os.path.exists(path):
            for sentence in common.read_corpus(path)[1]:
                self.inner.add(sentence["text"])
        self.inner.keys.discard("")

    def key_hit(self, text):
        return self.inner.key_hit(text)

    def reason(self, text):
        found = self.inner.reason(text)
        return None if found is None else found.replace("shapes-dev", "reference")


def read_folder(work, folder):
    """{source_id: [(line number, text)]} of W/<folder>/*.txt (one unit per line)."""
    path = shapes_dev.shared_path(work, folder)
    found = {}
    if os.path.isdir(path):
        for name in sorted(os.listdir(path)):
            if name.endswith(".txt"):
                lines = common.read_lines(os.path.join(path, name))
                found[name[:-4]] = [(number, shapes_dev.normalize_text(line)) for number, line in enumerate(lines, 1) if line.strip()]
    return found


def used_tables(data_dir, extra_tables=()):
    tables = list(extra_tables)
    for relative in ("imperative/sources.tsv", "imperative/excluded-documents.tsv"):
        path = os.path.join(data_dir, *relative.split("/"))
        if os.path.exists(path):
            tables.append(path)
    return tables


class Scan:
    """The clean units (sentences of W/text, or heading lines of W/lines) of the new documents, with the counts of what was dropped."""

    def __init__(self):
        self.units = []                       # (source_id, line number, text, tokens)
        self.drops = collections.Counter()
        self.rejected = {}
        self.problems = []
        self.considered = 0


def scan_units(work, data_dir, unit, extra_tables=(), include_ud=True, filt=None, reference=None, vocab=None, lex=None):
    """The `Scan` of the real units. `unit` is `sentence` or `heading`. Every filter of the sentence candidates of `shapes_dev` applies (the
    cleanliness filter, duplicates, equal training or evaluation sentences through `leakage.held_out_overlap_keys` only, the probe 5-gram
    rule, excluded and used documents, reprints through DOCUMENT_KEY_HITS) plus the sentences of dev.tsv, hard.tsv and imperative.tsv."""
    scan = Scan()
    lex = lex or shapes_dev.lexicon_of(data_dir)
    vocab = vocab or shapes_dev.vocabulary_of(data_dir, lex)
    filt = filt or shapes_dev.LeakageFilter(data_dir, include_ud)
    reference = reference or Reference(data_dir)
    tokens_of = shapes_dev.read_tokens(work)
    draft = {row["source_id"]: row for row in shapes_dev.read_draft(work)}
    exclusions = shapes_dev.load_exclusions(work, data_dir)
    index = imperative_data.used_index(data_dir, exclusions, used_tables(data_dir, extra_tables))
    sentences = shapes_dev.read_documents(work)
    documents = sentences if unit == "sentence" else read_folder(work, "lines")
    low_tokens, high_tokens = ((SENTENCE_MIN_TOKENS, SENTENCE_MAX_TOKENS) if unit == "sentence"
                               else (HEADING_MIN_TOKENS, HEADING_MAX_TOKENS))
    seen = set()
    for source_id in sorted(documents):
        if source_id not in draft:
            scan.problems.append("%s has no row in sources-draft.tsv" % source_id)
            continue
        if source_id in exclusions.reasons:
            scan.drops["excluded-source"] += len(documents[source_id])
            continue
        reason = imperative_data.document_rejection(index, source_id, draft[source_id]["url"])
        if reason:
            scan.rejected[source_id] = reason
            continue
        hits = sum(1 for _, text in sentences.get(source_id, ()) if filt.key_hit(text) or reference.key_hit(text))
        if hits >= shapes_dev.DOCUMENT_KEY_HITS:
            scan.rejected[source_id] = "%d sentences are also in a training or evaluation set or in the shapes development set" % hits
            continue
        for number, text in documents[source_id]:
            scan.considered += 1
            if text not in tokens_of or tokens_of[text][0] != 1:
                scan.drops["not-one-sentence-or-not-tokenized"] += 1
                continue
            tokens = tokens_of[text][1]
            if not low_tokens <= len(tokens) <= high_tokens:
                scan.drops["length"] += 1
                continue
            noise = (shapes_dev.noise_reason(text, tokens, lex, vocab) if unit == "sentence"
                     else heading_noise_reason(text, tokens, lex, vocab))
            key = shapes_dev.sent_key_of(text)
            if noise:
                scan.drops["noise-" + noise] += 1
            elif key in seen:
                scan.drops["duplicate"] += 1
            elif reference.reason(text):
                scan.drops[reference.reason(text)] += 1
            elif filt.key_hit(text):
                scan.drops["equals-a-set-sentence"] += 1
            elif filt.probe_hit(text):
                scan.drops["probe-5gram"] += 1
            else:
                seen.add(key)
                scan.units.append((source_id, number, text, tokens))
    return scan


def _cap_by_lemma(ranked, cap, limit, extra_cap=None):
    """The first `limit` rows of `ranked` ((rank, row) in order) with at most `cap` per lemma; `extra_cap` = (field, n) caps a second field."""
    counts, extra, kept = collections.Counter(), collections.Counter(), []
    for _, row in ranked:
        if limit is not None and len(kept) >= limit:
            break
        if counts[row["lemma"]] >= cap or (extra_cap and extra[row[extra_cap[0]]] >= extra_cap[1]):
            continue
        counts[row["lemma"]] += 1
        if extra_cap:
            extra[row[extra_cap[0]]] += 1
        kept.append(row)
    return kept


def candidate_rows(work, data_dir, tranche, hmm=None, seed=SEED, include_ud=True, extra_tables=(), budget=None, scans=None):
    """(rows, report lines, stats): the candidates of one tranche (see the module docstring for the pre-registered rule).

    `hmm` is the HmmTable of the pinned W/hmm.tsv (compound-head only: the contested band of the WordSense Basic classifier)."""
    lex = shapes_dev.lexicon_of(data_dir)
    unit = "heading" if tranche == "headings" else "sentence"
    scan = scans if scans is not None else scan_units(work, data_dir, unit, extra_tables, include_ud, lex=lex)
    shape_pool = collections.defaultdict(list)
    controls = []
    stats = collections.Counter()
    for source_id, number, text, tokens in scan.units:
        key = shapes_dev.sent_key_of(text)
        base = dict(role="target", origin="real", source_id=source_id, line=number, text=text, tokens=tokens)
        if tranche == "compound-head":
            i = compound_ambiguous_hit(tokens, lex)
            if i is None:
                continue
            stats["frame"] += 1
            if hmm is None:
                continue
            q = hmm.q(tokens, i, "Noun")
            if not BAND_MIN <= q < BAND_MAX:
                continue
            stats["band"] += 1
            shape_pool["n-compound-head-ambiguous"].append(
                (rank_key(seed, tranche, key, q), dict(base, shape="n-compound-head-ambiguous", target=i, lemma=shapes_dev.lemma_of(tokens[i], lex),
                                                     q=shapes_dev.format_q(q))))
        elif tranche == "named-state":
            i = named_state_hit(tokens, lex)
            if i is None:
                continue
            stats["frame"] += 1
            shape_pool["n-named-state"].append(
                (shapes_dev.rank_of(seed, tranche, key), dict(base, shape="n-named-state", target=i, lemma=tokens[i].lower())))
        else:
            for shape, i, strength in heading_hits(tokens, lex):
                stats["frame-" + shape] += 1
                row = dict(base, shape=shape, role=HEADING_ROLE[shape], target=i, lemma=shapes_dev.lemma_of(tokens[i], lex))
                shape_pool[shape].append(((strength, shapes_dev.rank_of(seed, tranche, shape, key)), row))
    rows = []
    if tranche == "compound-head":
        ranked = sorted(shape_pool["n-compound-head-ambiguous"], key=lambda item: item[0])
        chosen = _cap_by_lemma(ranked, LEMMA_CAP, budget if budget is not None else COMPOUND_BUDGET)
        rows += chosen
        lemmas = {r["lemma"] for r in chosen}
        pool = []
        for source_id, number, text, tokens in scan.units:
            for i in shapes_dev.control_hits(tokens, lex, "n-compound-head-ambiguous", lemmas):
                pool.append((shapes_dev.rank_of(seed, "control", tranche, shapes_dev.sent_key_of(text)),
                             dict(shape="n-compound-head-ambiguous", role="control", target=i, lemma=shapes_dev.lemma_of(tokens[i], lex),
                                  origin="real", source_id=source_id, line=number, text=text, tokens=tokens, q="")))
        rows += _cap_by_lemma(sorted(pool, key=lambda item: item[0]), CONTROL_LEMMA_CAP, CONTROL_POOL)
    elif tranche == "named-state":
        chosen = _cap_by_lemma(sorted(shape_pool["n-named-state"], key=lambda item: item[0]), LEMMA_CAP,
                               budget if budget is not None else NAMED_STATE_BUDGET)
        rows += chosen
        lemmas = {r["lemma"] for r in chosen}
        pool = []
        for source_id, number, text, tokens in scan.units:
            for i in shapes_dev.control_hits(tokens, lex, "n-named-state", lemmas):
                pool.append((shapes_dev.rank_of(seed, "control", tranche, shapes_dev.sent_key_of(text)),
                             dict(shape="n-named-state", role="control", target=i, lemma=shapes_dev.lemma_of(tokens[i], lex), origin="real",
                                  source_id=source_id, line=number, text=text, tokens=tokens)))
        rows += _cap_by_lemma(sorted(pool, key=lambda item: item[0]), CONTROL_LEMMA_CAP, CONTROL_POOL)
    else:
        limit = budget if budget is not None else HEADING_BUDGET
        for shape in ("heading-noun", "heading-imperative"):
            rows += _cap_by_lemma(sorted(shape_pool[shape], key=lambda item: item[0]), LEMMA_CAP, limit,
                                  ("source_id", HEADING_DOCUMENT_CAP_CANDIDATES))
        rows += _cap_by_lemma(sorted(shape_pool["heading-proper"], key=lambda item: item[0]), CONTROL_LEMMA_CAP, HEADING_CONTROL_POOL,
                              ("source_id", HEADING_DOCUMENT_CAP_CANDIDATES))
    order = {shape: n for n, shape in enumerate(CANDIDATE_SHAPES[tranche])}
    rows.sort(key=lambda r: (order[r["shape"]], r["role"] != "target", shapes_dev.rank_of(seed, "order", tranche, shapes_dev.sent_key_of(r["text"]))))
    for number, row in enumerate(rows, 1):
        row["cand_id"] = "%sc%05d" % (CAND_PREFIX[tranche], number)
    pools = {shape: len(items) for shape, items in shape_pool.items()}
    report = candidate_report(tranche, rows, scan, stats, pools)
    return rows, report, dict(stats=stats, pools=pools, scan=scan)


def rank_key(seed, tranche, key, q):
    """The order of the compound-head candidates: q ascending (hardest first), ties by the seeded rank."""
    return (round(q, 3), shapes_dev.rank_of(seed, tranche, key))


def candidate_report(tranche, rows, scan, stats, pools):
    """The report lines of a candidate run (counts only; no text of any evaluation set)."""
    lines = ["# Candidates (%s)" % tranche, "", "Counts only; no text of any evaluation set.", "",
             "units considered: %d, clean units kept for the frame: %d" % (scan.considered, len(scan.units)),
             "units dropped before the frame: " + (", ".join("%s %d" % item for item in sorted(scan.drops.items())) or "none"), ""]
    lines += ["- rejected %s: %s" % item for item in sorted(scan.rejected.items())]
    lines += ["- PROBLEM: " + p for p in scan.problems]
    lines += ["frame counts: " + (", ".join("%s %d" % item for item in sorted(stats.items())) or "none"),
              "pool sizes (before the caps): " + (", ".join("%s %d" % item for item in sorted(pools.items())) or "none"), ""]
    counts = collections.Counter((r["shape"], r["role"]) for r in rows)
    lines += ["| shape | role | candidates | lemmas | documents |", "| --- | --- | --- | --- | --- |"]
    for (shape, role), count in sorted(counts.items()):
        members = [r for r in rows if r["shape"] == shape and r["role"] == role]
        lines.append("| %s | %s | %d | %d | %d |" % (shape, role, count, len({r["lemma"] for r in members}), len({r["source_id"] for r in members})))
    return lines


def read_hmm_pinned(work):
    """The HmmTable of the pinned W/hmm.tsv (the tool refuses any other bytes, and a tokens.tsv other than the pinned one)."""
    if HMM_SHA256 is None or TOKENS_SHA256 is None:
        raise ValueError("the pins of hmm.tsv and tokens.tsv are not set: the pre-registration has not been committed")
    path = shapes_dev.shared_path(work, "tokens.tsv")
    actual = common.sha256_file(path)
    if actual != TOKENS_SHA256:
        raise ValueError("tokens.tsv has SHA-256 %s, the pre-registered one is %s" % (actual, TOKENS_SHA256))
    return shapes_dev.read_hmm(work, expected_sha256=HMM_SHA256)


# ---------------------------------------------------------------- commands: lines, tokenize, preflight, candidates

def _command_lines(args):
    work = adjudicate.Work(args.work)
    draft = shapes_dev.read_draft(work)
    folder = shapes_dev.shared_path(work, "lines")
    os.makedirs(folder, exist_ok=True)
    total = 0
    for row in draft:
        path = os.path.join(args.work, "raw", row["source_id"] + ".txt")
        if not os.path.exists(path):
            print("no raw text for " + row["source_id"], file=sys.stderr)
            continue
        with open(path, "rb") as handle:
            heads = heading_lines(handle.read().decode("utf-8", errors="replace"))
        common.write_text(os.path.join(folder, row["source_id"] + ".txt"), heads)
        total += len(heads)
    print("%d documents, %d heading lines written to lines/" % (len(draft), total))
    return 0


def _command_tokenize(args):
    import shlex
    work = adjudicate.Work(args.work)
    drafted = {row["source_id"] for row in shapes_dev.read_draft(work)}
    texts = set()
    for folder in ("text", "lines"):
        for source_id, units in read_folder(work, folder).items():
            if source_id in drafted:
                texts.update(text for _, text in units)
    known = shapes_dev.read_tokens(work)
    missing = sorted(texts - set(known))
    if missing:
        command = [p.strip('"') for p in shlex.split(args.cmd, posix=os.name != "nt")] if args.cmd else shapes_dev.default_tokenizer_command()
        known.update(shapes_dev.run_tokenizer(missing, command))
    rows = [(text, known[text][0], " ".join(known[text][1])) for text in sorted(texts)]      # exactly the units of the draft: nothing of a dropped document
    adjudicate.write_rows(shapes_dev.shared_path(work, "tokens.tsv"), ("text", "sentences", "tokens"), rows)
    print("%d units, %d newly tokenized; written to tokens.tsv" % (len(texts), len(missing)))
    return 0


def _hmm_for(args, work, needed):
    if not needed:
        return None
    if getattr(args, "hmm_sha256", None):
        return shapes_dev.read_hmm(work, expected_sha256=args.hmm_sha256)
    return read_hmm_pinned(work)


def _command_preflight(args):
    """Pool sizes before pre-registration (counts only; no label, no model). The band needs --hmm-sha256 (the actual SHA-256 of hmm.tsv)."""
    work = adjudicate.Work(args.work)
    data_dir = args.data_dir
    lex = shapes_dev.lexicon_of(data_dir)
    vocab = shapes_dev.vocabulary_of(data_dir, lex)
    filt = shapes_dev.LeakageFilter(data_dir, not args.no_ud)
    reference = Reference(data_dir)
    scans = {unit: scan_units(work, data_dir, unit, args.extra_table or (), not args.no_ud, filt, reference, vocab, lex)
             for unit in ("sentence", "heading")}
    hmm = shapes_dev.read_hmm(work, expected_sha256=args.hmm_sha256) if args.hmm_sha256 else None
    for tranche in args.tranche or TRANCHES:
        rows, report, info = candidate_rows(work, data_dir, tranche, hmm, args.seed, not args.no_ud, args.extra_table or (),
                                            scans=scans["heading" if tranche == "headings" else "sentence"])
        print("\n".join(report[:12]))
        print()
        if tranche == "named-state":
            frame = [(s, n, t, tk) for s, n, t, tk in scans["sentence"].units if named_state_hit(tk, lex) is not None]
            print("named-state frame hits: %d sentences, %d lemmas, %d documents" % (
                len(frame), len({tk[named_state_hit(tk, lex)].lower() for _, _, _, tk in frame}), len({f[0] for f in frame})))
        if tranche == "headings":
            kinds = collections.Counter()
            lemmas = collections.defaultdict(set)
            for _, _, _, tk in scans["heading"].units:
                for shape, i, _ in heading_hits(tk, lex):
                    kinds[shape] += 1
                    lemmas[shape].add(shapes_dev.lemma_of(tk[i], lex))
            print("heading frame hits: " + ", ".join("%s %d (%d lemmas)" % (s, kinds[s], len(lemmas[s])) for s in sorted(kinds)))
    return 0


def _command_candidates(args):
    work = adjudicate.Work(args.work)
    tranche = args.tranche
    hmm = _hmm_for(args, work, tranche == "compound-head")
    rows, report, info = candidate_rows(work, args.data_dir, tranche, hmm, args.seed, not args.no_ud, args.extra_table or (), args.budget)
    round_work = shapes_dev.RoundWork(args.work, tranche)
    os.makedirs(round_work.private, exist_ok=True)
    common.write_text(round_work.private_file("candidates-report.md"), report)
    shapes_dev.write_candidates(round_work, rows)
    print("\n".join(report))
    print("%d candidates written to rounds/%s/candidates.tsv" % (len(rows), tranche))
    return 0


# ---------------------------------------------------------------- selection and build

def read_round(work, tranche, data_dir=None):
    """(items, rows, candidates {cand_id: dict}) of the decided round of a tranche (shapes_dev.read_decided over W/rounds/<tranche>)."""
    round_work = shapes_dev.RoundWork(work.root if not hasattr(work, "base") else work.base, tranche)
    items, rows = shapes_dev.read_decided(round_work, data_dir)
    candidates = {c["cand_id"]: c for c in shapes_dev.read_candidates(round_work)}
    return items, rows, candidates


def keep_item(item):
    return item["outcome"] == "keep"


def select(tranche, items, candidates, seed=SEED, caps=None):
    """(targets, controls, drops Counter): the kept items that the build takes, in the order of the pre-registered rule.

    compound-head   targets in the order q ascending (the candidates carry q), at most 4 per lemma, at most 25 percent whose form ends in
                    `ing`, up to the cap; controls: lemma of a chosen target, 2 per lemma, seeded order, up to the cap
    named-state     targets in the seeded order, at most 4 per lemma, up to the cap; controls like compound-head
    headings        noun targets and imperative targets in the seeded order, at most 4 per lemma, at most HEADING_DOCUMENT_CAP per
                    document over both kinds, up to the cap of each kind; controls (`heading-proper`): 2 per lemma, up to the cap"""
    quota = QUOTAS[tranche]
    caps = dict(caps or {})
    drops = collections.Counter()
    kept = [i for i in items if keep_item(i)]
    targets, controls = [], []
    if tranche == "headings":
        per_doc = collections.Counter()
        for shape in ("heading-noun", "heading-imperative"):
            limit = caps.get(KIND_OF[shape], quota[KIND_OF[shape]][1])
            counts = collections.Counter()
            members = sorted((i for i in kept if i["shape"] == shape), key=lambda i: shapes_dev.rank_of(seed, "build", i["cand_id"]))
            taken = 0
            for item in members:
                if taken >= limit:
                    drops["cap"] += 1
                elif counts[item["lemma"]] >= LEMMA_CAP:
                    drops["lemma-cap"] += 1
                elif per_doc[item["source_id"]] >= HEADING_DOCUMENT_CAP:
                    drops["document-cap"] += 1
                else:
                    counts[item["lemma"]] += 1
                    per_doc[item["source_id"]] += 1
                    targets.append(item)
                    taken += 1
        pool = sorted((i for i in kept if i["shape"] == "heading-proper"), key=lambda i: shapes_dev.rank_of(seed, "build-control", i["cand_id"]))
        counts = collections.Counter()
        for item in pool:
            if len(controls) >= caps.get("controls", quota["controls"][1]):
                drops["cap"] += 1
            elif counts[item["lemma"]] >= CONTROL_LEMMA_CAP:
                drops["lemma-cap"] += 1
            else:
                counts[item["lemma"]] += 1
                controls.append(item)
        return targets, controls, drops
    limit = caps.get("targets", quota["targets"][1])
    ing_limit = int(math.ceil(limit * ING_SHARE)) if tranche == "compound-head" else None
    members = [i for i in kept if i["role"] == "target"]
    if tranche == "compound-head":
        members.sort(key=lambda i: (float(i["q"]), shapes_dev.rank_of(seed, "build", i["cand_id"])))
    else:
        members.sort(key=lambda i: shapes_dev.rank_of(seed, "build", i["cand_id"]))
    counts, ing = collections.Counter(), 0
    for item in members:
        form = candidates[item["cand_id"]]["tokens"][item["target"]].lower()
        if len(targets) >= limit:
            drops["cap"] += 1
        elif counts[item["lemma"]] >= LEMMA_CAP:
            drops["lemma-cap"] += 1
        elif ing_limit is not None and form.endswith("ing") and ing >= ing_limit:
            drops["ing-share"] += 1
        else:
            counts[item["lemma"]] += 1
            ing += form.endswith("ing")
            targets.append(item)
    lemmas = {i["lemma"] for i in targets}
    counts = collections.Counter()
    pool = sorted((i for i in kept if i["role"] == "control" and i["lemma"] in lemmas),
                  key=lambda i: shapes_dev.rank_of(seed, "build-control", i["cand_id"]))
    for item in pool:
        if len(controls) >= caps.get("controls", quota["controls"][1]):
            drops["cap"] += 1
        elif counts[item["lemma"]] >= CONTROL_LEMMA_CAP:
            drops["lemma-cap"] += 1
        else:
            counts[item["lemma"]] += 1
            controls.append(item)
    return targets, controls, drops


def quota_problems(tranche, targets, controls, caps=None):
    """[message] for each miss of the quota gate of a tranche (a floor or a cap per stratum; the lemma caps)."""
    quota = QUOTAS[tranche]
    found = []
    strata = ([(kind, [t for t in targets if KIND_OF[t["shape"]] == kind]) for kind in ("noun", "imperative")] if tranche == "headings"
              else [("targets", targets)])
    for name, members in strata:
        floor, cap = quota[name]
        if len(members) < floor:
            found.append("%s: %d targets, the floor is %d" % (name, len(members), floor))
        if len(members) > cap:
            found.append("%s: %d targets, the cap is %d" % (name, len(members), cap))
    floor, cap = quota["controls"]
    if len(controls) < floor:
        found.append("controls: %d, the floor is %d" % (len(controls), floor))
    if len(controls) > cap:
        found.append("controls: %d, the cap is %d" % (len(controls), cap))
    for label, members, limit in (("target", targets, LEMMA_CAP), ("control", controls, CONTROL_LEMMA_CAP)):
        for lemma, n in sorted(collections.Counter(m["lemma"] for m in members).items()):
            if n > limit:
                found.append("%s lemma %s has %d items, at most %d allowed" % (label, lemma, n, limit))
    return found


def header_lines(tranche):
    name = FILES[tranche][0]
    return [
        "# corpus = DemaConsulting.PartOfSpeech shapes development set, %s tranche (UPOS), dev split" % tranche,
        "# license = CC0-1.0 for annotations and selection; sentences are US federal government works, US public domain, see %s" % FILES[tranche][1],
        "# columns = form, upos, status",
        "# status = unanimous | disputed (two blind model labelers; disputed = they differ, the tag is the first labeler's)",
        "# judged word = per sentence: shape (%s), role (target | control), target (zero-based token index), lemma, origin (real)%s; "
        "the judged word is never disputed" % (SHAPE_OF[tranche], ", kind (noun | imperative | proper)" if tranche == "headings" else ""),
        "# selection = %s; the gold is the agreement of two blind labelers (see README.md)" % {
            "compound-head": "the contested band of the WordSense Basic classifier, hardest first",
            "headings": "Title Case heading lines of federal documents, by position and lexicon only",
            "named-state": "state, position and setting names after a preposition, by position and lexicon only"}[tranche],
        "# use = development only: select settings here, never train on it, never report it as a held-out figure",
        "# file = %s" % name,
    ]


def final_sentence(tranche, item, candidate, tagged_rows, number):
    extra = ["# shape = " + SHAPE_OF[tranche], "# role = " + item["role"], "# target = %d" % item["target"], "# lemma = " + item["lemma"],
             "# origin = real"]
    if tranche == "headings":
        extra.append("# kind = " + KIND_OF[item["shape"]])
    return {"sent_id": SENT_ID[tranche] % number, "source_id": item["source_id"], "text": candidate["text"],
            "rows": [[form, tag, status] for form, tag, status in tagged_rows], "extra": extra}


def sentence_problems(tranche, sentence):
    """Problems of one built sentence of a tranche (the rules of the data test)."""
    values = dict(part[2:].split(" = ", 1) for part in sentence["extra"])
    rows = sentence["rows"]
    name = sentence["sent_id"]
    problems = []
    if values.get("shape") != SHAPE_OF[tranche] or values.get("role") not in ("target", "control") or values.get("origin") != "real":
        return ["%s: shape, role or origin is wrong" % name]
    if not values.get("target", "").isdigit() or int(values["target"]) >= len(rows):
        return ["%s: the target is not a token index" % name]
    tag = rows[int(values["target"])][1]
    if tranche == "headings":
        expected = {"noun": "NOUN", "imperative": "VERB", "proper": "PROPN"}.get(values.get("kind"))
        if expected is None or tag != expected or (values["role"] == "control") != (values["kind"] == "proper"):
            problems.append("%s: the judged word is tagged %s for kind %s" % (name, tag, values.get("kind")))
    elif shapes_dev.tag_class(tag) != ("Noun" if values["role"] == "target" else "Verb"):
        problems.append("%s: the judged word is tagged %s for a %s" % (name, tag, values["role"]))
    if common.base_status(rows[int(values["target"])][2]) == "disputed":
        problems.append("%s: the judged word is disputed" % name)
    if "".join(r[0] for r in rows) != sentence["text"].replace(" ", ""):
        problems.append("%s: the tokens do not rejoin to the text" % name)
    return problems


class QuotaError(ValueError):
    """The quota gate is not met (nothing is written)."""


def build(work, tranche, out_dir, seed=SEED, allow_short=False, data_dir=None, caps=None):
    """Write <tranche>.tsv and <tranche>-sources.tsv into `out_dir`; returns (sentences, report lines, problems).

    Fail closed: the exclusion record is read through `shapes_dev.load_exclusions` (never rewritten); a chosen sentence or a used source row
    that it lists refuses the build; a quota miss (QuotaError, nothing written) unless `allow_short` (the misses become warnings)."""
    items, tagged, candidates = read_round(work, tranche, data_dir)
    draft = shapes_dev.read_draft(work)
    targets, controls, drops = select(tranche, items, candidates, seed, caps)
    chosen = targets + controls
    chosen.sort(key=lambda i: (CANDIDATE_SHAPES[tranche].index(i["shape"]), i["role"] != "target", shapes_dev.rank_of(seed, "order", i["cand_id"])))
    built = [final_sentence(tranche, item, candidates[item["cand_id"]], tagged[item["sent_key"]], number) for number, item in enumerate(chosen, 1)]
    problems = [p for s in built for p in sentence_problems(tranche, s)]
    if problems:
        raise ValueError("the built set is invalid:\n  " + "\n  ".join(problems[:20]))
    unknown = sorted({s["source_id"] for s in built} - {r["source_id"] for r in draft})
    if unknown:
        raise ValueError("no source row for: " + ", ".join(unknown))
    exclusions = shapes_dev.load_exclusions(work, data_dir)
    refused = exclusions.problems(draft, built)
    if refused:
        raise shapes_dev.ExcludedSourceError("excluded sources are used:\n  " + "\n  ".join(refused[:20]))
    short = quota_problems(tranche, targets, controls, caps)
    if short and not allow_short:
        raise QuotaError("quotas not met (pass --allow-short to build anyway):\n  " + "\n  ".join(short))
    corpus, sources, _ = FILES[tranche]
    common.write_corpus(os.path.join(out_dir, corpus), common.with_flag_documentation(header_lines(tranche), built), built)
    adjudicate.write_rows(os.path.join(out_dir, sources), shapes_dev.SOURCE_COLUMNS, shapes_dev.sources_rows(draft, built))
    report = ["# Build (%s)" % tranche, "", "%d sentences written to %s (%d targets, %d controls)" % (len(built), corpus, len(targets), len(controls))]
    report += ["WARNING (allowed by --allow-short): " + p for p in short]
    report += ["dropped at the caps: " + (", ".join("%s %d" % d for d in sorted(drops.items())) or "none")]
    return built, report, short


def _command_build(args):
    work = adjudicate.Work(args.work)
    out_dir = args.out_dir or os.path.join(args.data_dir, "eval", "shapes-dev")
    for tranche in (args.tranche,) if args.tranche else TRANCHES:
        try:
            built, report, _ = build(work, tranche, out_dir, args.seed, args.allow_short, args.data_dir)
        except (QuotaError, shapes_dev.ExcludedSourceError, ValueError) as error:
            print("build refused (%s): %s" % (tranche, error), file=sys.stderr)
            return 1
        adjudicate._print(report)
    return 0


def report_lines(work, tranche, data_dir=None):
    """Markdown counts of one decided round: per candidate shape and role, the items, the kept ones and the drops per reason."""
    items, _, _ = read_round(work, tranche, data_dir)
    lines = ["# Decisions (%s)" % tranche, "", "| shape | role | items | kept | target-disagreement | class-mismatch | frame-mismatch | excluded-source |",
             "| --- | --- | --- | --- | --- | --- | --- | --- |"]
    for shape in CANDIDATE_SHAPES[tranche]:
        for role in ("target", "control"):
            members = [i for i in items if i["shape"] == shape and i["role"] == role]
            if not members:
                continue
            count = collections.Counter(i["reason"] or "kept" for i in members)
            lines.append("| %s | %s | %d | %d | %d | %d | %d | %d |" % (shape, role, len(members), count["kept"], count["target-disagreement"],
                                                                        count["class-mismatch"], count["frame-mismatch"], count["excluded-source"]))
    lines += ["", "disputed non-target tokens in kept items: %d" % sum(i["disputed_tokens"] for i in items if keep_item(i))]
    return lines


def _command_report(args):
    work = adjudicate.Work(args.work)
    for tranche in (args.tranche,) if args.tranche else TRANCHES:
        adjudicate._print(report_lines(work, tranche, args.data_dir))
    return 0


# ---------------------------------------------------------------- hand check

def build_sample(selected, seed=SEED, plan=SAMPLE_PLAN, controls=SAMPLE_CONTROLS):
    """[(kind, tranche, item, candidate, shown tag)] shuffled: per candidate shape of `plan` the stated number of target items that the build
    takes (fewer when the tranche has fewer), plus `controls` swapped-tag items (right verdict: incorrect; a leniency control).

    `selected` is [(tranche, item, candidate)] of the items that the build takes."""
    rng = random.Random(seed)
    chosen, rest = [], []
    for shape, count in plan:
        members = sorted((s for s in selected if s[1]["shape"] == shape and s[1]["role"] == "target"), key=lambda s: s[1]["cand_id"])
        picked = rng.sample(members, min(count, len(members)))
        chosen += picked
        rest += [s for s in members if s not in picked]
    result = [("real", t, i, c, i["agreed_tag"]) for t, i, c in chosen]
    swapped = rng.sample(sorted(rest, key=lambda s: s[1]["cand_id"]), min(controls, len(rest)))
    result += [("control", t, i, c, "VERB" if shapes_dev.tag_class(i["agreed_tag"]) == "Noun" else "NOUN") for t, i, c in swapped]
    rng.shuffle(result)
    return result


def _command_sample(args):
    work = adjudicate.Work(args.work)
    selected = []
    for tranche in TRANCHES:
        items, _, candidates = read_round(work, tranche, args.data_dir)
        targets, controls, _ = select(tranche, items, candidates, args.seed)
        selected += [(tranche, i, candidates[i["cand_id"]]) for i in targets]
    sample = build_sample(selected, args.seed)
    lines = list(shapes_dev.REVIEW_INSTRUCTIONS)
    keys = []
    for number, (kind, tranche, item, candidate, shown) in enumerate(sample, 1):
        name = "R%03d" % number
        words = ["[[%s]]" % w if i == item["target"] else w for i, w in enumerate(candidate["tokens"])]
        lines += ["## " + name, "", "Sentence: " + " ".join(words), "Word: %s (index %d)" % (candidate["tokens"][item["target"]], item["target"]),
                  "Proposed tag: " + shown, ""]
        keys.append((name, kind, item["cand_id"], item["shape"], item["target"], shown, item["agreed_tag"], tranche))
    common.write_text(work.private_file("review-sample.md"), lines)
    adjudicate.write_rows(work.private_file("review-sample-key.tsv"), SAMPLE_KEY_COLUMNS, keys)
    real = sum(1 for k in sample if k[0] == "real")
    print("%d items: %d real, %d control" % (len(sample), real, len(sample) - real))
    return 0


def review_numbers(keys, verdicts):
    """(per-shape Counter, control Counter) of the verdicts over the sample key rows."""
    answers = {v.item: v.verdict for v in verdicts}
    per_shape, controls = collections.defaultdict(collections.Counter), collections.Counter()
    for row in keys:
        verdict = answers.get(row["item"])
        if verdict is None:
            continue
        (controls if row["kind"] == "control" else per_shape[row["shape"]])[verdict] += 1
    return per_shape, controls


def review_result_lines(per_shape, controls):
    """(lines, passed): the Markdown result of the model review; the gate is a precision of at least 95 percent and a strict reviewer."""
    total = collections.Counter()
    lines = ["# Review result", "", "| shape | correct | incorrect | unclear | precision |", "| --- | --- | --- | --- | --- |"]
    for shape in sorted(per_shape):
        counter = per_shape[shape]
        judged = counter["correct"] + counter["incorrect"]
        lines.append("| %s | %d | %d | %d | %s |" % (shape, counter["correct"], counter["incorrect"], counter["unclear"],
                                                    "-" if not judged else "%.1f%%" % (100 * counter["correct"] / judged)))
        total.update(counter)
    judged = total["correct"] + total["incorrect"]
    overall = total["correct"] / judged if judged else None
    lenient = controls["correct"] > CONTROLS_ACCEPTED_MAX
    passed = overall is not None and overall >= PRECISION_MIN and not lenient
    lines += ["", "- reviewed target items: %d (correct %d, incorrect %d, unclear %d)" % (sum(total.values()), total["correct"], total["incorrect"],
                                                                                       total["unclear"]),
              "- overall precision: %s (gate: at least %.0f%%; unclear items count against it only through the strict figure %d of %d)" % (
                  "-" if overall is None else "%.1f%%" % (100 * overall), 100 * PRECISION_MIN, total["correct"], sum(total.values())),
              "- control items accepted as correct: %d of %d%s" % (controls["correct"], sum(controls.values()), " (TOO LENIENT)" if lenient else ""),
              "- gate: " + ("PASSED" if passed else "STOP: do not publish; report the result")]
    return lines, passed


def _command_review_result(args):
    work = adjudicate.Work(args.work)
    _, keys = adjudicate.read_records(work.private_file("review-sample-key.tsv"))
    verdicts = []
    for name in args.verdicts:
        with open(name, "r", encoding="utf-8") as handle:
            verdicts += adjudicate.parse_verdicts(handle.read())
    unknown = {v.item for v in verdicts} - {k["item"] for k in keys}
    if unknown:
        raise ValueError("verdicts for unknown items: " + ", ".join(sorted(unknown)[:5]))
    per_shape, controls = review_numbers(keys, verdicts)
    lines, passed = review_result_lines(per_shape, controls)
    missing = {k["item"] for k in keys} - {v.item for v in verdicts}
    lines.append("- unanswered items: %d" % len(missing))
    adjudicate._print(lines)
    common.write_text(work.private_file("review-result.md"), lines)
    return 0 if passed and not missing else 1


# ---------------------------------------------------------------- entry point

def main(argv=None):
    parser = argparse.ArgumentParser(description=__doc__.split("\n")[0])
    parser.add_argument("--work", required=True, help="the work folder (outside the repository)")
    parser.add_argument("--data-dir", default=common.DATA)
    parser.add_argument("--seed", type=int, default=SEED)
    sub = parser.add_subparsers(dest="command", required=True)
    sub.add_parser("lines", help="extract the heading lines of W/raw/<id>.txt into W/lines/<id>.txt").set_defaults(func=_command_lines)
    p = sub.add_parser("tokenize", help="tokenize W/text and W/lines units into W/tokens.tsv")
    p.add_argument("--cmd")
    p.set_defaults(func=_command_tokenize)
    p = sub.add_parser("preflight", help="pool sizes (counts only; no label, no model)")
    p.add_argument("--tranche", action="append", choices=TRANCHES)
    p.add_argument("--extra-table", action="append")
    p.add_argument("--hmm-sha256")
    p.add_argument("--no-ud", action="store_true")
    p.set_defaults(func=_command_preflight)
    p = sub.add_parser("candidates", help="write W/rounds/<tranche>/candidates.tsv")
    p.add_argument("--tranche", required=True, choices=TRANCHES)
    p.add_argument("--extra-table", action="append")
    p.add_argument("--budget", type=int)
    p.add_argument("--hmm-sha256")
    p.add_argument("--no-ud", action="store_true")
    p.set_defaults(func=_command_candidates)
    p = sub.add_parser("build", help="write the tranche files")
    p.add_argument("--tranche", choices=TRANCHES)
    p.add_argument("--out-dir")
    p.add_argument("--allow-short", action="store_true")
    p.set_defaults(func=_command_build)
    p = sub.add_parser("report", help="counts of the decided rounds")
    p.add_argument("--tranche", choices=TRANCHES)
    p.set_defaults(func=_command_report)
    sub.add_parser("sample", help="the hand-check sample of the targets that the build takes").set_defaults(func=_command_sample)
    p = sub.add_parser("review-result", help="precision of the model review")
    p.add_argument("--verdicts", nargs="+", required=True)
    p.set_defaults(func=_command_review_result)
    args = parser.parse_args(argv)
    return args.func(args)


if __name__ == "__main__":
    sys.exit(main())

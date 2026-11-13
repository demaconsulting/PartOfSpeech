"""The frames of the technical-text shapes S1 to S4 of the beta.3 data work: pure functions on tokens and the lexicon.

This module is the NORMATIVE TEXT of the position and lexicon rules that pre-select candidates for four failure shapes. A frame only
PRE-SELECTS. The class and the tag of the judged word always come from the two blind labelers and the unchanged adjudication; a frame
never decides a label. No PartOfSpeech model, decoder, weights file, prediction or score is read here (the standard library, the lexicon
argument and the word helpers of `shapes_dev` and `shapes_dev_extra` are the only inputs). Every function is deterministic and has no
side effect; no file is read or written. The word lists and numbers below are constants chosen from the lexicon and the definition of
each shape (never from a consumer report, no consumer text exists here); once the pre-registration commit is made they are frozen, and a
change is a new, disclosed version of the rule.

Arguments: `tokens` is the token list of one unit exactly as tokenized for the work folder (words and marks, no clitic merging), `lex`
is the lexicon `{spelling: frozenset of tags}` of `data/lexicon/lexicon.tsv`.

S1  deverbal compound heads (`deverbal_head_hit`, `verb_control_hits`)
    A noun-noun compound whose head is spelled like a verb: kind `zero` (the head is its own base form, listed NOUN and VERB, no
    `-ing`/`-ed` ending), kind `ing` (an `-ing` form of a listed verb that the lexicon lists as NOUN: a gerund noun) or kind `plural`
    (the `-s` form of a `zero` head, allowed only after a numeral or as a predicate). The head is lower case; the token before it is a
    strict noun modifier (`shapes_dev.noun_like_modifier`, lower case, or the first token of the sentence); the next token is the end,
    a mark or a word that is not noun-capable (a function word never counts as noun-capable, since the lexicon lists `is` also as NOUN)
    and is neither a determiner, an object pronoun nor a quantity (so the head ends its
    compound and is not the verb of a clause). The modifier run is walked to the left over noun modifiers, adjectives and ordinals; ONE
    determiner (or `one`) or ONE numeral that stands before the run is consumed; the token before that (skipping `not`, `never`, `also`,
    `still`, `only`, `always`) is the governor. A sentence takes its rarest stratum: `pred` (governor is a form of be, become or remain,
    and not `there is`; the head follows a determiner or numeral, or is a plural or an `-ing` head without one), `num` (the consumed
    element is a number or number word other than `one`, and no label word such as `Table` or `Step` stands before it), `ing` (a gerund
    head in any other position, with or without a determiner) and `other` (a `zero` head after a determiner: subject or object
    position). A `plural` head is allowed in `pred` and `num` only. Controls (`verb`): the lemma of a target used as a verb in a
    similar frame, `verb_control_hits`: after a modal or `to`, as the first word of an instruction followed by a determiner or an object
    pronoun, or as an `-ing` verb after `by`, `before`, `after`, `while` or a form of be and before a determiner or an object pronoun.

S2  capitalized defined terms (`defined_term_hits`, `title_runs`)
    A run is 1 to 4 consecutive Title Case tokens (`[A-Z][a-z]+`) that does not start the sentence, does not follow `:`, a quote or
    sentence-final punctuation, and is not followed by a number or a one-letter label (`Table 3`, `Step B`). A sentence with a
    capitalized function word after its first token (`In The Standby Position`) is Title Case styled and has no run. ALL CAPS state
    labels are out of scope (they do not match the Title Case token). A word is `ordinary` when its lower-case spelling is in the
    lexicon with NOUN or ADJ, is not only PROPN, is no function or number word, no month or day name and no title of `TITLES`. Cue tiers
    (pre-selection and report only): T1 right cue (the next token is a lower-case kind noun of `RIGHT_CUES`), T2 document-type head (the
    last word of the run is in `DOCUMENT_HEADS`), T3 left verb cue (a verb of `LEFT_CUES`, with at most one determiner between it and
    the run), T4 any other run of two to four ordinary words. A single-word run needs T1, T2 or T3. Strata, one hit per stratum and run:
    `proper` (control; every run that holds a word the lexicon lists only as PROPN, or ends in an organization or place head of
    `ORGANIZATION_HEADS`; it has two or more words or the T1 tier; judged word = last token, exact PROPN), `adj` (all words ordinary, two
    or more words, first word listed ADJ and without `-ing`/`-ed`; judged word = the first token, exact ADJ) and `noun` (all words
    ordinary, the last word listed NOUN; judged word = the last token, exact NOUN).

S3  capitalized imperatives at line start (`step_lines`, `step_noise_reason`, `step_line_hit`, `step_caption_hit`)
    `step_lines(raw)` takes the enumerated lines (`1.`, `1)`, `(1)`, `a.`, `(a)`, `Step 3:`, with the wrapped continuation lines up to
    the next enumerator or blank line, cut at the first sentence end), the bulleted lines and the short stand-alone lines (2 to 12
    words, a capital first letter, no final `,;:-`, not wrapped text) of the OCR text. The target frame `step_line_hit`: token 0 is
    `[A-Z][a-z]+`, listed VERB and also NOUN or ADJ, its own base form, no function word; token 1 is absent or a mark (`end`), a
    determiner (`det`), an object pronoun (`pron`), a number (`num`), a particle (`particle`), a preposition (`prep`), a following
    adverb (`adv`) or a lower-case noun-capable word (`bare`: `Check oil level`, `Open valve 3`); never an auxiliary, a subject pronoun
    or a capitalized word (that is the caption frame). `bare` and `end` need an imperative context (the line has a label or ends in a
    period) and the next two tokens after a bare object are no auxiliary and the first is no word listed only as a verb. The control frame
    `step_caption_hit`: token 0 capitalized, listed NOUN and VERB (or NOUN and ADJ), 2 to 6 tokens, no label, no sentence mark, token 1
    a noun-capable word (lower case or capitalized): `Check valve`, `Control Panel`, `Test results`.

S4  base-form ambiguity in a clause (`clause_verb_hit`, `clause_control_hit`)
    The target word V is lower case, listed NOUN and VERB (or VERB and ADJ), its own base form, ends in no `s` (except `ss`), and is no
    function word. The subject stands right before V: a plural noun (`-s` form of a listed noun, not `-ss`) or a coordination `X and|or
    Y` whose Y (optionally with a determiner) is noun-capable. The subject noun phrase (walked left over noun modifiers, adjectives and
    one determiner or numeral) opens a clause: before it stand the start of the sentence, a mark, a conjunction, a subordinator or a
    sentence adverb (never a verb or a preposition: `keep the valves open` is no clause). V is followed by the end, a mark, a determiner,
    an object pronoun, a preposition, a quantity, `that`, `to`, `not`, a following adverb or a conjunction (the original frame), or, since
    AMENDMENT A2 (before any label other than the S2 pilot; an implementing-agent decision, not the owner's), by a lower-case noun that is
    no function word or by an adjective and noun phrase without a determiner (a direct object: `Engineers check pressure gauges.`,
    `The valves control flow.`, `Pumps supply cold water.`); such a hit carries the flag `np` (`ClauseHit.np`). The amendment only widens
    the pool of candidates: the tag of V (VERB for a kept target) always comes from the two blind labelers, so an `np` candidate that both
    label NOUN or ADJ is a class-mismatch drop, never a control. Stratum `sub` when a subordinator of `SUBORDINATORS` stands within 5 tokens
    before the subject with no mark between, otherwise `main`. Still one candidate per sentence. Controls (unchanged): `noun` (the same
    subject frame, V followed by a lower-case noun: `valves control panel`) and `adj` (a determiner, V listed ADJ, a plural noun: `the open
    valves`). A sentence of the noun-control frame that is also an `np` target frame is offered to both kinds; the control draw comes first
    and the target draw never takes the same sentence (`technical_data.candidate_rows`).
"""
import collections
import re

import common
import convention_rules
import shapes_dev
import shapes_dev_extra

# ---------------------------------------------------------------- shared word lists and helpers

BE_FORMS = frozenset("is are was were be been being am become becomes became becoming remain remains remained remaining".split())
GOVERNOR_ADVERBS = frozenset("not never also still only always".split())
ORDINAL_WORDS = frozenset("first second third fourth fifth sixth seventh eighth ninth tenth next last final".split())
SUBJECT_PRONOUNS = frozenset("i we they he she".split())
SENTENCE_MARKS = frozenset(". ! ? ; :".split())
QUOTES = frozenset(['"', "'", "`", "\u201c", "\u201d", "\u2018", "\u2019"])
TITLE_TOKEN = re.compile(r"[A-Z][a-z]+")


def is_mark(token):
    """True for a token without a letter or digit (a mark)."""
    return not shapes_dev.is_word(token)


def has_tags(lex, token, *tags):
    """True when the lower-case spelling of `token` is listed with at least one of `tags`."""
    return bool(lex.get(token.lower(), frozenset()) & frozenset(tags))


def is_lower_word(token):
    """A lower-case word (an alphabetic token with no capital): the form of a head, a modifier or a verb in a frame."""
    return shapes_dev.is_word(token) and token == token.lower()


def adjective_like(token, lex):
    """A word that may stand between a determiner and the modifier of a compound: listed only as ADJ, or an ordinal."""
    low = token.lower()
    tags = lex.get(low, frozenset())
    return low in ORDINAL_WORDS or (bool(tags) and tags <= frozenset(["ADJ"]))


def modifier_like(token, lex, index):
    """A word of the modifier run of a compound: a strict noun modifier (lower case, or the first token of the sentence) or an adjective."""
    if not shapes_dev.is_word(token) or (token != token.lower() and index != 0):
        return False
    return shapes_dev.noun_like_modifier(lex, token) or adjective_like(token, lex)


def plural_stem(token, lex):
    """The singular noun of a plural noun, or None: the lower-case token ends in `s` (not `ss`), is listed NOUN, is no function word, and
    removing `ies` (+ `y`), `es` or `s` leaves a stem of at least two letters that is listed NOUN (`valves` -> valve, `gas` -> None)."""
    low = token.lower()
    if (not low.endswith("s") or low.endswith("ss") or not has_tags(lex, low, "NOUN") or low in shapes_dev.FUNCTION_WORDS
            or not shapes_dev.is_word(token)):
        return None
    for suffix, replacement in (("ies", "y"), ("es", ""), ("s", "")):
        if low.endswith(suffix) and len(low) > len(suffix) + 1:
            stem = low[:-len(suffix)] + replacement
            if len(stem) >= 2 and has_tags(lex, stem, "NOUN"):
                return stem
    return None


def run_start(tokens, lex, head):
    """(start, consumed, element, governor): the modifier run that ends right before `head`, the consumed element and the governor.

    The run is walked left over modifiers (`modifier_like`). Then ONE determiner or `one` (element `det`) or ONE numeral that is
    no `one` and has no label word before it (element `num`) is consumed; the governor is the token before that (a determiner before
    a numeral is skipped, and so are the adverbs of GOVERNOR_ADVERBS), or -1 when there is none. `start` is the index of the first
    token of the run (after the walk, before the consumed element), `element` is `det`, `num` or `none`."""
    j = head - 1
    while j >= 0 and modifier_like(tokens[j], lex, j):
        j -= 1
    start, element = j + 1, "none"
    if j >= 0:
        low = tokens[j].lower()
        if low in shapes_dev.DETERMINERS or low == "one":
            element, j = "det", j - 1
        elif shapes_dev.is_quantity(tokens[j]) and low not in ("one", "half") and not (j >= 1 and tokens[j - 1].lower() in shapes_dev.LABEL_WORDS):
            element, j = "num", j - 1
            if j >= 0 and tokens[j].lower() in shapes_dev.DETERMINERS:
                j -= 1
    while j >= 0 and tokens[j].lower() in GOVERNOR_ADVERBS:
        j -= 1
    return start, element, j


# ---------------------------------------------------------------- S1 deverbal compound heads

DEVERBAL_STRATA = ("pred", "num", "ing", "other")          # the order in which a sentence takes its stratum (rarest first)
DeverbalHit = collections.namedtuple("DeverbalHit", "index stratum kind lemma")
ControlHit = collections.namedtuple("ControlHit", "index frame lemma")
ING_CONTROL_GOVERNORS = frozenset("by before after while".split()) | BE_FORMS


def head_kind(token, lex):
    """`zero`, `ing`, `plural` or None: what kind of deverbal head the lower-case `token` can be (see the module docstring, S1)."""
    if not is_lower_word(token):
        return None
    low = token.lower()
    if low.endswith("ing") and len(low) > 4:
        return "ing" if shapes_dev.possible_gerund(low, lex) and has_tags(lex, low, "NOUN") else None
    if low.endswith("ed"):
        return None
    if shapes_dev.is_target_word(lex, low):
        if shapes_dev.lemma_of(low, lex) == low and not (low.endswith("s") and not low.endswith("ss")):
            return "zero"
        stem = shapes_dev.lemma_of(low, lex)
        if stem != low and low.endswith("s") and not low.endswith("ss") and shapes_dev.is_target_word(lex, stem):
            return "plural"
    return None


def deverbal_stratum(tokens, lex, head):
    """(stratum, kind) of the head at `head`, or None when the token is no deverbal head in a compound frame (see the module docstring)."""
    kind = head_kind(tokens[head], lex)
    if kind is None or head < 1 or not shapes_dev.noun_like_modifier(lex, tokens[head - 1]) or not modifier_like(tokens[head - 1], lex, head - 1):
        return None
    following = tokens[head + 1] if head + 1 < len(tokens) else None
    if following is not None and shapes_dev.is_word(following):
        flow = following.lower()
        if (flow not in shapes_dev.FUNCTION_WORDS and shapes_dev.noun_capable(lex, following)) or flow in shapes_dev.DETERMINERS \
                or flow in shapes_dev.OBJECT_PRONOUNS or shapes_dev.is_quantity(following):
            return None
    start, element, governor = run_start(tokens, lex, head)
    predicate = (governor >= 0 and tokens[governor].lower() in BE_FORMS and not (governor >= 1 and tokens[governor - 1].lower() == "there")
                 and (element != "none" or kind != "zero"))
    if predicate:
        return "pred", kind
    if kind == "plural":
        return ("num", kind) if element == "num" else None
    if element == "num":
        return "num", kind
    if kind == "ing":
        return "ing", kind
    if element == "det":
        return "other", kind
    return None


def deverbal_head_hit(tokens, lex):
    """The hit of the rarest stratum of the sentence (`DEVERBAL_STRATA`), as `DeverbalHit(index, stratum, kind, lemma)`, or None.

    Ties go to the first head of the sentence. The lemma is `shapes_dev.lemma_of` of the head (the lemma of the lemma cap that the
    earlier compound tranches use). The result only pre-selects a candidate; the labelers decide the class of the head."""
    best = None
    for i in range(1, len(tokens)):
        found = deverbal_stratum(tokens, lex, i)
        if found is None:
            continue
        rank = DEVERBAL_STRATA.index(found[0])
        if best is None or rank < best[0]:
            best = (rank, i, found)
    if best is None:
        return None
    _, i, (stratum, kind) = best
    return DeverbalHit(i, stratum, kind, shapes_dev.lemma_of(tokens[i], lex))


def verb_control_hits(tokens, lex, lemmas=None):
    """[ControlHit(index, frame, lemma)] of the control frames of S1: a word of `lemmas` (any listed noun-verb word when None) as a verb.

    Frames: `modal` (the token before is a modal or `to`), `imperative` (the first token, capitalized, followed by a determiner or
    an object pronoun) and `ing` (an `-ing` form of a listed verb that the lexicon also lists as NOUN, after `by`, `before`, `after`,
    `while` or a form of be, before a determiner or an object pronoun: `by testing the valve`). The first two reuse
    `shapes_dev.control_hits`; the lemma of an `ing` control is the lower-case form itself (like the lemma of an `ing` target)."""
    if lemmas is None:
        lemmas = {shapes_dev.lemma_of(t, lex) for t in tokens if shapes_dev.is_target_word(lex, t) or head_kind(t.lower(), lex) == "ing"}
    found = {}
    for i in shapes_dev.control_hits(tokens, lex, "n-compound-head", lemmas):
        frame = "imperative" if i == 0 else "modal"
        found[i] = ControlHit(i, frame, shapes_dev.lemma_of(tokens[i], lex))
    for i in range(1, len(tokens) - 1):
        low = tokens[i].lower()
        if i in found or low not in lemmas or head_kind(tokens[i], lex) != "ing":
            continue
        following = tokens[i + 1].lower()
        if tokens[i - 1].lower() in ING_CONTROL_GOVERNORS and (following in shapes_dev.DETERMINERS or following in shapes_dev.OBJECT_PRONOUNS):
            found[i] = ControlHit(i, "ing", low)
    return [found[i] for i in sorted(found)]


# ---------------------------------------------------------------- S2 capitalized defined terms

TERM_STRATA = ("proper", "adj", "noun")                    # the order in which a sentence takes its stratum (rarest first)
TERM_TIERS = ("T1", "T2", "T3", "T4")
MAX_RUN = 4
RIGHT_CUES = frozenset(
    "mode position setting state screen menu window button switch key indicator display page tab field option panel status level range "
    "condition list bar icon prompt function command".split())
DOCUMENT_HEADS = frozenset(
    "Schematic Diagram Drawing Chart Checklist Manual Handbook Procedure Plan Report Specification Requirements Table Matrix Sheet".split())
LEFT_CUES = frozenset("select press set enter choose open click see refer use activate engage per".split())
ORGANIZATION_HEADS = frozenset(
    "Agency Administration Command Corps Laboratory Center Department Office Bureau Service Institute University Council Committee Board "
    "Commission Association Society Company Corporation Division Directorate Branch Station "
    "River Lake Mountain Street Bay Island Valley Park Forest County City Ocean Canal Fort".split())
NAME_WORDS = frozenset("January February March April May June July August September October November December "
                       "Monday Tuesday Wednesday Thursday Friday Saturday Sunday".split())
TermHit = collections.namedtuple("TermHit", "start end stratum tier judged term")


def is_title_case(token):
    """True for a Title Case token: a capital followed by lower-case letters only (`Manual`; not `MANUAL`, `PLC`, `X7`)."""
    return TITLE_TOKEN.fullmatch(token) is not None


def title_cased_sentence(tokens):
    """True when a capitalized function word stands after the first token (`In The Standby Position`): a Title Case styled line or heading."""
    function = shapes_dev.DETERMINERS | shapes_dev.PREPOSITIONS | shapes_dev.CONJUNCTIONS | frozenset("to is are".split())
    return any(is_title_case(t) and t.lower() in function for t in tokens[1:])


def title_runs(tokens):
    """[(start, end)] of the Title Case runs of a sentence (`end` exclusive): 1 to MAX_RUN consecutive Title Case tokens.

    A maximal run longer than MAX_RUN is no run. A run does not start at index 0, does not follow `:`, a quote or sentence-final
    punctuation (SENTENCE_MARKS), and is not followed by a number, a number word or a one-letter label (`Table 3`, `Step B`)."""
    runs, i, n = [], 0, len(tokens)
    while i < n:
        if not is_title_case(tokens[i]):
            i += 1
            continue
        j = i
        while j < n and is_title_case(tokens[j]):
            j += 1
        after = tokens[j] if j < n else ""
        label = (common.is_number(after) or after.lower() in shapes_dev.NUMBER_WORDS or re.fullmatch(r"[A-Za-z]", after) is not None)
        if (i >= 1 and j - i <= MAX_RUN and tokens[i - 1] not in QUOTES and tokens[i - 1] not in SENTENCE_MARKS and not label):
            runs.append((i, j))
        i = j
    return runs


def ordinary(token, lex):
    """True when a Title Case word is an ordinary English word: its lower-case spelling is listed with NOUN or ADJ and not only as PROPN,
    and it is no function word, number word, month or day name, or title (`convention_rules.TITLES`)."""
    low = token.lower()
    tags = lex.get(low, frozenset())
    return (bool(tags & frozenset(["NOUN", "ADJ"])) and tags != frozenset(["PROPN"]) and low not in shapes_dev.FUNCTION_WORDS
            and low not in shapes_dev.NUMBER_WORDS and token not in NAME_WORDS and token not in convention_rules.TITLES)


def organization_like(words):
    """True when the last word of a run is an organization or place head (`ORGANIZATION_HEADS`: `Defense Logistics Agency`, `Mojave River`)."""
    return bool(words) and words[-1] in ORGANIZATION_HEADS


def run_tier(tokens, start, end):
    """`T1` to `T4`: the cue tier of the run `tokens[start:end]` (the first that applies; see the module docstring)."""
    following = tokens[end] if end < len(tokens) else ""
    if following != "" and is_lower_word(following) and following in RIGHT_CUES:
        return "T1"
    if tokens[end - 1] in DOCUMENT_HEADS:
        return "T2"
    before = start - 1
    if before >= 0 and tokens[before].lower() in shapes_dev.DETERMINERS:
        before -= 1
    if before >= 0 and tokens[before].lower() in LEFT_CUES:
        return "T3"
    return "T4"


def defined_term_hits(tokens, lex):
    """[TermHit(start, end, stratum, tier, judged, term)] of a sentence, in the order of the runs, per run in the order proper, adj, noun.

    `judged` is the token index of the judged word, `term` the lower-case words of the run joined by a space (the key of the per-term
    cap). See the module docstring (S2) for the run, `ordinary`, the tiers and the three strata. A sentence that is Title Case styled
    (`title_cased_sentence`) has no hit. The result only pre-selects; the labelers tag every token under the convention of
    `data/CONVENTIONS.md`, "Capitalized Defined Terms"."""
    if title_cased_sentence(tokens):
        return []
    hits = []
    for start, end in title_runs(tokens):
        words = tokens[start:end]
        tier = run_tier(tokens, start, end)
        term = " ".join(w.lower() for w in words)
        proper = organization_like(words) or any(lex.get(w.lower()) == frozenset(["PROPN"]) for w in words)
        if proper:
            if len(words) >= 2 or tier == "T1":
                hits.append(TermHit(start, end, "proper", tier, end - 1, term))
            continue
        if not all(ordinary(w, lex) for w in words) or (len(words) == 1 and tier == "T4"):
            continue
        if len(words) >= 2 and has_tags(lex, words[0], "ADJ") and not words[0].lower().endswith(("ing", "ed")):
            hits.append(TermHit(start, end, "adj", tier, start, term))
        if has_tags(lex, words[-1], "NOUN"):
            hits.append(TermHit(start, end, "noun", tier, end - 1, term))
    return hits


# ---------------------------------------------------------------- S3 capitalized imperatives at line start

STEP_FRAMES = ("det", "pron", "num", "prep", "particle", "adv", "bare", "end")
PARTICLES = frozenset("up down out off back away".split())
STEP_MIN_WORDS, STEP_SHORT_MAX_WORDS, STEP_MAX_CHARS = 2, 12, 300
STEP_MAX_TOKENS, STEP_WRAPPED_LINES, STEP_CAPTION_MAX_TOKENS = 30, 4, 6
STEP_REPEAT_LINES = 3
STEP_KINDS = ("enumerated", "bulleted", "short")
ENUMERATOR = re.compile(
    r"^(?:(?:Step|STEP)\s+[0-9]{1,3}[A-Za-z]?\s*[.:)\-]?\s+|\((?:[0-9]{1,3}|[a-z]|[A-Z]|[ivx]{1,4})\)\s+"
    r"|(?:[0-9]{1,3}(?:\.[0-9]{1,3})*|[a-z]|[A-Z])[.)]\s+)")
BULLET = re.compile(r"^[-*\u2022\u2023\u25e6\u2043\u2013\u2014]\s+")
SENTENCE_END = re.compile(r"[.!?](?=\s+[A-Z(]|\s*$)")
STEP_ALLOWED = re.compile(r"[A-Za-z0-9 ,'()\-./:;\"%]*")
# The inner list labels of a step unit: `(a)`, `(2)`, `(iv)` and a number with a period before a lower-case word. Unlike
# `shapes_dev.INNER_LABEL` it does not refuse a unit that ends in a number and a period (`Open valve 3.` is a step).
STEP_INNER_LABEL = re.compile(r"(?:^|\s)\((?:[a-z]|\d{1,2}|[ivx]{2,4})\)(?:\s|$)|\s\d{1,2}\.\s+[a-z]")
STEP_NOISE_RULES = ("length", "start-end", "chars", "page-number", "junk", "brackets", "enumeration", "repeat", "caps", "oov")
StepHit = collections.namedtuple("StepHit", "index frame")


def _line_key(line):
    return re.sub(r"[^a-z0-9]", "", line.lower())


def _cut_at_sentence_end(text):
    """The text up to and including its first sentence end (a period, `!` or `?` before a capital or the end), or the whole text."""
    match = SENTENCE_END.search(text)
    return text[:match.end()] if match else text


def _step_label(stripped):
    """(kind, text) of an enumerated or bulleted line without its label, or None; the label of `Step 3:` and `1.` is removed."""
    match = ENUMERATOR.match(stripped)
    if match:
        rest = stripped[match.end():].strip()
        return ("enumerated", shapes_dev_extra.strip_heading_label(rest) if ENUMERATOR.match(rest) else rest) if rest else None
    match = BULLET.match(stripped)
    if match and stripped[match.end():].strip():
        return "bulleted", stripped[match.end():].strip()
    return None


def step_lines(raw):
    """[(text, kind)] of the step units of the OCR text `raw`, in document order; kind is `enumerated`, `bulleted` or `short`.

    Enumerated and bulleted lines (`ENUMERATOR`, `BULLET`) lose their label; the following non-blank lines that carry no label are
    their wrapped continuation (at most STEP_WRAPPED_LINES; a continuation line does not start with a capital and the unit so far does not
    end a sentence) and are joined with a space; the unit is cut at its first sentence end and
    refused above STEP_MAX_CHARS. A short stand-alone line has STEP_MIN_WORDS to STEP_SHORT_MAX_WORDS words, starts with a capital, does
    not end in `,`, `;`, `:` or `-`, is not a label line, is not the wrapped tail of a long unterminated line before it and is not followed by a
    line that starts in lower case (a wrapped paragraph). A line that occurs STEP_REPEAT_LINES or more times in the document (a running
    header or footer) and a line with dot leaders or a tab give no unit. Duplicate units are kept once. Only the light line rules apply
    here; `step_noise_reason` is the full filter of the candidate stage. Deterministic."""
    lines = raw.replace("\r\n", "\n").replace("\r", "\n").replace("\x0c", "\n\n").split("\n")
    counts = collections.Counter(_line_key(line) for line in lines if line.strip())
    found, seen = [], set()

    def add(text, kind):
        text = shapes_dev.normalize_text(text)
        if text and len(text) <= STEP_MAX_CHARS and (text, kind) not in seen:
            seen.add((text, kind))
            found.append((text, kind))

    def usable(line):
        return bool(line) and counts[_line_key(line)] < STEP_REPEAT_LINES and "\t" not in line and not re.search(r"\.{2,}|(?:\s\.){2,}", line)

    number = 0
    while number < len(lines):
        stripped = lines[number].strip()
        labeled = _step_label(stripped) if usable(stripped) else None
        if labeled is not None:
            kind, text = labeled
            following = number + 1
            while following < len(lines) and following - number <= STEP_WRAPPED_LINES:
                follow = lines[following].strip()
                if (not follow or _step_label(follow) is not None or not usable(follow) or follow[:1].isupper()
                        or text[-1:] in ".!?"):
                    break
                text += " " + follow
                following += 1
            add(_cut_at_sentence_end(text), kind)
            number = following
            continue
        words = stripped.split()
        if usable(stripped) and STEP_MIN_WORDS <= len(words) <= STEP_SHORT_MAX_WORDS and stripped[:1].isupper() and stripped[-1] not in ",;:-":
            before = lines[number - 1].strip() if number > 0 else ""
            after = next((l.strip() for l in lines[number + 1:number + 2] if l.strip()), "")
            wrapped_tail = bool(before) and len(before.split()) > STEP_SHORT_MAX_WORDS and before[-1] not in ".!?:"
            if not wrapped_tail and not (after and after[:1].islower()):
                add(stripped, "short")
        number += 1
    return found


def step_noise_reason(text, tokens, lex, vocab, kind="short"):
    """The rule of STEP_NOISE_RULES that rejects a step unit, or None when it is clean (the filter of the S3 candidate stage).

    It keeps the character, page-number, junk, bracket, enumeration, repeat and OOV rules of the heading filter
    (`shapes_dev_extra.heading_noise_reason`) and allows sentence case and Title Case. A unit without a capital first letter is refused.

    length       fewer than STEP_MIN_WORDS words (alphabetic or numeric tokens) or more than STEP_MAX_TOKENS tokens
    start-end    does not start with an upper-case letter, ends in `,`, `;`, `:` or `-`, or ends in a function word (a broken line)
    chars        a character outside ASCII letters, digits, space and `, ' ( ) - . / : ; " %`
    page-number  a trailing page range (`4-8`); for a `short` unit that does not end in a period also a trailing number
                 (`Proposed Action 12`, a contents line); a `short` unit that ends in a number and a sentence-final period
                 (`Open valve 3.`) and an enumerated or bulleted unit that ends in a number (`Open valve 3`) are steps
    junk         slash-punctuation junk, doubled or space-separated punctuation, a broken hyphen word, a lone lower-case letter other than `a`
    brackets     unbalanced parentheses
    enumeration  an inner list label (` (a) `, ` 2. x`) or an initial such as `M. `
    repeat       the same word twice in a row
    caps         token 0 is all capitals, or all-capital words of two or more letters are more than half of the alphabetic tokens
    oov          an alphabetic word whose lower-case spelling is in no word list (`shapes_dev_extra.heading_oov`)
    """
    words = [t for t in tokens if shapes_dev.is_word(t)]
    if len(words) < STEP_MIN_WORDS or len(tokens) > STEP_MAX_TOKENS:
        return "length"
    if (not text[:1].isupper() or text[-1:] in ",;:-" or tokens[-1].lower() in shapes_dev_extra.HEADING_FUNCTION_WORDS
            or not re.search(r"[A-Za-z0-9).]$", text)):
        return "start-end"
    if not STEP_ALLOWED.fullmatch(text):
        return "chars"
    trailing_range = re.search(r"\s\d{1,4}\s?[-\u2013\u2014]\s?\d{1,4}\.?$", text) is not None
    if trailing_range or (kind == "short" and not text.endswith(".") and shapes_dev_extra.has_trailing_page_number(text)):
        return "page-number"
    if (any(junk in text for junk in shapes_dev.JUNK_PUNCT) or shapes_dev.HYPHEN_JUNK.search(text) or re.search(r"(?<![A-Za-z0-9])'", text)
            or any(len(t) == 1 and t.islower() and t != "a" and t.isalpha() for t in tokens)):
        return "junk"
    if text.count("(") != text.count(")") or (text.find(")") < text.find("(") and "(" in text):
        return "brackets"
    if STEP_INNER_LABEL.search(text) or shapes_dev.INITIALS.search(text):
        return "enumeration"
    lows = [t.lower() for t in tokens]
    if any(a == b and shapes_dev.is_alpha_word(a) for a, b in zip(lows, lows[1:])):
        return "repeat"
    alpha = [t for t in tokens if shapes_dev.is_alpha_word(t) and not t.startswith("'")]
    upper = [t for t in alpha if len(t) >= 2 and t.isupper()]
    if (tokens and len(tokens[0]) >= 2 and tokens[0].isupper()) or 2 * len(upper) > len(alpha):
        return "caps"
    if shapes_dev_extra.heading_oov(tokens, vocab):
        return "oov"
    return None


def imperative_context(tokens, labeled):
    """True when the unit reads as an instruction line: it carries a label (enumerated or bulleted) or it ends in a period."""
    return labeled or (bool(tokens) and tokens[-1] == ".")


def step_line_hit(tokens, lex, labeled=False):
    """`StepHit(0, frame)` of the target frame of S3, or None (`labeled` = the unit is an enumerated or bulleted line; module docstring, S3)."""
    if len(tokens) < 1 or not is_title_case(tokens[0]):
        return None
    low = tokens[0].lower()
    tags = lex.get(low, frozenset())
    if ("VERB" not in tags or not tags & frozenset(["NOUN", "ADJ"]) or low in shapes_dev.FUNCTION_WORDS
            or shapes_dev.base_form_of(tokens[0], lex) != low):
        return None
    following = tokens[1] if len(tokens) > 1 else None
    if following is None:
        return StepHit(0, "end") if labeled else None
    if is_mark(following):
        return StepHit(0, "end") if following == "." and len(tokens) == 2 else None
    flow = following.lower()
    if flow in shapes_dev.AUXILIARIES or flow in SUBJECT_PRONOUNS or (following[:1].isupper() and not shapes_dev.is_quantity(following)):
        return None
    if flow in PARTICLES:
        return StepHit(0, "particle")
    if flow in shapes_dev.DETERMINERS:
        return StepHit(0, "det")
    if flow in shapes_dev.OBJECT_PRONOUNS:
        return StepHit(0, "pron")
    if shapes_dev.is_quantity(following):
        return StepHit(0, "num")
    if flow in shapes_dev.PREPOSITIONS:
        return StepHit(0, "prep")
    if flow in shapes_dev.FOLLOWING_ADVERBS:
        return StepHit(0, "adv")
    if (is_lower_word(following) and has_tags(lex, flow, "NOUN") and flow not in shapes_dev.FUNCTION_WORDS
            and imperative_context(tokens, labeled)):
        after = tokens[2].lower() if len(tokens) > 2 else ""
        after_tags = lex.get(after, frozenset())
        verb_only = "VERB" in after_tags and not after_tags - frozenset(["VERB", "AUX"])
        if verb_only or any(t.lower() in shapes_dev.AUXILIARIES for t in tokens[2:4]):
            return None
        return StepHit(0, "bare")
    return None


def step_caption_hit(tokens, lex, labeled=False):
    """0 (the index of token 0) for the control frame of S3 (a noun-initial caption or line), or None (module docstring, S3)."""
    if not 2 <= len(tokens) <= STEP_CAPTION_MAX_TOKENS or labeled or not is_title_case(tokens[0]):
        return None
    low = tokens[0].lower()
    tags = lex.get(low, frozenset())
    if "NOUN" not in tags or not tags & frozenset(["VERB", "ADJ"]) or low in shapes_dev.FUNCTION_WORDS:
        return None
    if any(t in SENTENCE_MARKS or t == "," for t in tokens):
        return None
    following = tokens[1]
    if (not shapes_dev.is_word(following) or following.lower() in shapes_dev.FUNCTION_WORDS or not shapes_dev.noun_capable(lex, following)
            or not (following[:1].isupper() and is_title_case(following) or is_lower_word(following))):
        return None
    return 0


# ---------------------------------------------------------------- S4 base-form ambiguity in a clause

CLAUSE_STRATA = ("main", "sub")
CLAUSE_CONTROL_STRATA = ("noun", "adj")
SUBORDINATORS = frozenset("when if because that which where after before while although since unless until whether once".split())
SENTENCE_ADVERBS = frozenset("then thus therefore also so however".split())
SUBORDINATOR_WINDOW = 5
CLAUSE_FOLLOWERS = frozenset("that to not then again".split())
ClauseHit = collections.namedtuple("ClauseHit", "index stratum coordinated lemma np", defaults=(False,))      # np: AMENDMENT A2, V is followed by a direct object without a determiner


def clause_word(token, lex):
    """True when the lower-case `token` can be the ambiguous base-form word of S4: listed NOUN and VERB (or VERB and ADJ), its own base form,
    ending in no `s` (except `ss`), no function word."""
    low = token.lower()
    tags = lex.get(low, frozenset())
    return (is_lower_word(token) and "VERB" in tags and bool(tags & frozenset(["NOUN", "ADJ"])) and low not in shapes_dev.FUNCTION_WORDS
            and shapes_dev.lemma_of(token, lex) == low and shapes_dev.base_form_of(token, lex) == low
            and not (low.endswith("s") and not low.endswith("ss")))


def clause_subject(tokens, lex, verb):
    """(start, coordinated) of the subject noun phrase right before `verb`, or None.

    The subject is a plural noun (`plural_stem`) or a coordination `X and|or Y` with a noun-capable Y (and an optional determiner before
    Y). The phrase is walked left over modifiers and one determiner or numeral; before it stands the start of the sentence, a mark, a
    conjunction, a subordinator or a sentence adverb. Returns None otherwise (`keep the valves open` has a verb before the phrase)."""
    subject = verb - 1
    if subject < 0 or not shapes_dev.is_word(tokens[subject]):
        return None
    coordinated, head = False, subject
    k = subject - 1
    if k >= 0 and tokens[k].lower() in shapes_dev.DETERMINERS:
        k -= 1
    if (k >= 1 and tokens[k].lower() in ("and", "or") and shapes_dev.noun_capable(lex, tokens[subject])
            and tokens[subject].lower() not in shapes_dev.FUNCTION_WORDS and shapes_dev.noun_capable(lex, tokens[k - 1])):
        coordinated, head = True, k - 1
    elif plural_stem(tokens[subject], lex) is None:
        return None
    start, _, governor = run_start(tokens, lex, head)
    if governor >= 0 and shapes_dev.is_word(tokens[governor]):
        low = tokens[governor].lower()
        if low not in shapes_dev.CONJUNCTIONS and low not in SUBORDINATORS and low not in SENTENCE_ADVERBS:
            return None
    return start, coordinated


def clause_follower_ok(tokens, verb):
    """True when the token after `verb` fits the target frame: the end, a mark, a determiner, an object pronoun, a preposition, a quantity,
    `that`, `to`, `not`, a following adverb, a conjunction; it is never a noun-capable word (see `clause_noun_follower`)."""
    if verb + 1 >= len(tokens):
        return True
    following = tokens[verb + 1]
    flow = following.lower()
    return (is_mark(following) or flow in shapes_dev.DETERMINERS or flow in shapes_dev.OBJECT_PRONOUNS or flow in shapes_dev.PREPOSITIONS
            or shapes_dev.is_quantity(following) or flow in CLAUSE_FOLLOWERS or flow in shapes_dev.FOLLOWING_ADVERBS
            or flow in shapes_dev.CONJUNCTIONS)


def clause_noun_follower(tokens, lex, verb):
    """True when `verb` is followed by a lower-case noun-capable word that is no function word (the frame of the `noun` control)."""
    if verb + 1 >= len(tokens):
        return False
    following = tokens[verb + 1]
    return is_lower_word(following) and has_tags(lex, following, "NOUN") and following.lower() not in shapes_dev.FUNCTION_WORDS


def clause_np_follower(tokens, lex, verb):
    """True (AMENDMENT A2) when `verb` starts a direct object without a determiner: the next token is a lower-case noun that is no function word (`clause_noun_follower`),
    or one or more adjectives (listed only as ADJ, or an ordinal) and then such a noun (`cold water`). A token of the original follower classes never counts."""
    if verb + 1 >= len(tokens) or clause_follower_ok(tokens, verb):
        return False
    if clause_noun_follower(tokens, lex, verb):
        return True
    j = verb + 1
    while j < len(tokens) - 1 and is_lower_word(tokens[j]) and adjective_like(tokens[j], lex):
        j += 1
    return j > verb + 1 and clause_noun_follower(tokens, lex, j - 1)


def clause_stratum(tokens, verb):
    """`sub` when a subordinator stands within SUBORDINATOR_WINDOW tokens before the subject head with no mark between, else `main`."""
    head = verb - 1
    for j in range(head - 1, max(-1, head - 1 - SUBORDINATOR_WINDOW), -1):
        if is_mark(tokens[j]):
            return "main"
        if tokens[j].lower() in SUBORDINATORS:
            return "sub"
    return "main"


def clause_verb_hit(tokens, lex):
    """`ClauseHit(index, stratum, coordinated, lemma, np)` of the first target of S4 (stratum `main` or `sub`), or None. The module docstring has the rule; `np` marks the frame of AMENDMENT A2."""
    for i in range(1, len(tokens)):
        if not clause_word(tokens[i], lex):
            continue
        original = clause_follower_ok(tokens, i) and not clause_noun_follower(tokens, lex, i)
        if not original and not clause_np_follower(tokens, lex, i):
            continue
        subject = clause_subject(tokens, lex, i)
        if subject is None:
            continue
        return ClauseHit(i, clause_stratum(tokens, i), subject[1], tokens[i].lower(), not original)
    return None


def clause_control_hit(tokens, lex):
    """`ClauseHit(index, stratum, coordinated, lemma)` of the first control of S4 (stratum `adj`, else `noun`), or None.

    `adj`: a determiner, a word listed ADJ (and NOUN and VERB, its own base form), a plural noun. `noun`: the subject frame of the target
    with a lower-case noun after the word (`valves control panel`)."""
    for i in range(1, len(tokens) - 1):
        token = tokens[i]
        if (clause_word(token, lex) and has_tags(lex, token, "ADJ") and tokens[i - 1].lower() in shapes_dev.DETERMINERS
                and plural_stem(tokens[i + 1], lex) is not None):
            return ClauseHit(i, "adj", False, token.lower())
    for i in range(1, len(tokens) - 1):
        token = tokens[i]
        if clause_word(token, lex) and clause_noun_follower(tokens, lex, i):
            subject = clause_subject(tokens, lex, i)
            if subject is not None:
                return ClauseHit(i, "noun", subject[1], token.lower())
    return None

"""Judgment candidates: the cases that the staged mechanical corrections leave to the adjudication (Python 3, stdlib only).

A candidate is a token (or a short span of tokens) where the conventions in data/CONVENTIONS.md decide by meaning, not by
the neighbors alone: is `operating` in `operating modes` a noun or a verb, is `it` in quotation marks a mentioned word, is
`English` a name or an adjective. The detectors here find the places and attach machine evidence in `hint`, so that
the adjudication (a separate change) can decide each case from a self-contained row (`context` shows the sentence window).
Nothing here changes a label and a candidate is never a linter failure.

Each detector gets a `Sentence` (see convention_rules.py) and the corpus `Resources` and returns `(token_index, span_length,
hint)` triples. The generator drops the tokens that already have a mechanical correction.
"""
import collections
import os
import sys
import unicodedata

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import convention_rules as R

Candidate = collections.namedtuple("Candidate", "id doc words detect")

CANDIDATES = []


def candidate(candidate_id, doc, words=None):
    """Register a candidate detector (`words`: lower-case trigger words, None for a detector that scans every token)."""
    def register(function):
        CANDIDATES.append(Candidate(candidate_id, doc, frozenset(words) if words else None, function))
        return function
    return register


def candidate_ids():
    """Ids of the candidate detectors."""
    return {item.id for item in CANDIDATES}


def catalog():
    """(id, one-line description) of every candidate detector."""
    return [(item.id, item.doc) for item in CANDIDATES]


PRENOMINAL_PREVIOUS = ("DET", "ADJ", "ADP", "NOUN", "VERB", "CCONJ", "NUM", "PUNCT")


@candidate("prenominal-ing", "an -ing word before a noun: noun, adjective or verb (tests 1, 3 and 5 of Prenominal Modifiers)")
def prenominal_ing(s, res):
    out = []
    for i in range(1, s.n - 1):
        if s.t[i] in ("VERB", "ADJ", "NOUN") and s.w[i].endswith("ing") and len(s.w[i]) > 4 \
                and s.t[i + 1] in ("NOUN", "PROPN") and s.t[i - 1] in PRENOMINAL_PREVIOUS:
            out.append((i, 1, "penn=%s; head=%s" % (s.p[i] or "-", s.w[i + 1])))
    return out


@candidate("prenominal-ed", "an -ed or irregular participle before a noun: adjective or verb (tests 2 and 3 of Prenominal Modifiers)")
def prenominal_ed(s, res):
    out = []
    for i in range(1, s.n - 1):
        if s.t[i] in ("VERB", "ADJ") and R.is_participle(s.w[i]) and s.t[i + 1] in ("NOUN", "PROPN") \
                and s.t[i - 1] in ("DET", "ADJ", "ADP"):
            out.append((i, 1, "penn=%s; form-gradable=%s; head=%s" % (s.p[i] or "-", "yes" if s.w[i] in res.gradable else "no",
                                                                     s.w[i + 1])))
    return out


@candidate("be-participle-gradable",
           "`be` or a linking verb + a participle whose gradability or agent leaves the tag open (test 2 against test 4)")
def be_participle_gradable(s, res):
    out = []
    for j, k, kind, degree, agent in R._participle_contexts(s):
        gradable = s.w[j] in res.gradable
        if s.t[j] == "VERB" and gradable and not degree and kind == "be":
            hint = "tag=VERB; form-gradable=yes; agent=%s" % ("by" if agent else "none")
        elif s.t[j] == "ADJ" and agent and gradable and not degree:
            hint = "tag=ADJ; form-gradable=yes; agent=by"
        else:
            continue
        out.append((j, 1, hint))
    return out


@candidate("zero-derived-degree", "a noun under `very`, `so`, `too`, `quite` (`very London`): ADJ for that token (test 6)",
           words={"very", "so", "too", "quite"})
def zero_derived_degree(s, res):
    out = []
    for i in s.where("very", "so", "too", "quite"):
        if s.tag(i + 1) == "NOUN" and s.tag(i + 2) not in ("NOUN", "PROPN"):
            out.append((i + 1, 1, "degree-before=%s" % s.w[i]))
    return out


MENTION_HEADS = frozenset("word verb noun adjective adverb term phrase name letter prefix suffix expression acronym".split())
OPEN_QUOTES = frozenset(("\"", "``", "\u201c", "\u2018"))
CLOSE_QUOTES = frozenset(("\"", "''", "\u201d", "\u2019"))


@candidate("mention-the-word", "`the word X`, `the verb X`: the following word or quoted phrase may be a mentioned word",
           words=MENTION_HEADS)
def mention_the_word(s, res):
    out = []
    for i in s.where(*MENTION_HEADS):
        if s.word(i - 1) not in ("the", "a", "this", "that"):
            continue
        start, quote = i + 1, ""
        if s.form(start) in OPEN_QUOTES:
            quote = s.form(start)
            start += 1
        elif s.tag(start) in ("PUNCT", "ADP", "VERB", "AUX", "PART", "CCONJ", "SCONJ", "", "PRON"):
            continue
        if start >= s.n:
            continue
        span = 1
        if quote:
            span = 0
            while start + span < s.n and s.f[start + span] not in CLOSE_QUOTES and span < 4:
                span += 1
            if span == 0:
                continue
        out.append((start, span, "head=%s%s" % (s.w[i], "; quote=" + quote if quote else "")))
    return out


SPEECH_OR_PUNCT = R.SPEECH_VERBS | {":", ",", ";"}


@candidate("mention-quoted", "one to three words between quotation marks that do not quote speech: a possible mention")
def mention_quoted(s, res):
    out = []
    straight = [i for i in range(s.n) if s.f[i] == "\""]
    pairs = list(zip(straight[::2], straight[1::2]))
    for i in range(s.n):
        close = None
        if s.f[i] in ("``", "\u201c"):
            close = next((j for j in range(i + 1, min(s.n, i + 5)) if s.f[j] in ("''", "\u201d")), None)
        elif s.f[i] == "'" and s.form(i + 2) == "'":
            close = i + 2
        if close is not None:
            pairs.append((i, close))
    for a, b in pairs:
        inner = b - a - 1
        if not 1 <= inner <= 3 or a == 0 or s.w[a - 1] in SPEECH_OR_PUNCT or s.t[a + 1] == "PUNCT" or s.t[b - 1] == "PUNCT":
            continue
        out.append((a + 1, inner, "quote=%s %s" % (s.f[a], s.f[b])))
    return out


LANGUAGES_LOWER = frozenset(item.lower() for item in R.LANGUAGES)


@candidate("mention-in-language", "`X in English`, `a Spanish word`: the word may be mentioned for its form or meaning",
           words=LANGUAGES_LOWER)
def mention_in_language(s, res):
    out = []
    for i in s.where(*LANGUAGES_LOWER):
        if s.f[i] not in R.LANGUAGES:
            continue
        before = s.w[i - 2] if i >= 2 else ""
        if s.word(i - 1) == "in" and s.tag(i + 1) != "PROPN" and s.tag(i - 2) in ("NOUN", "VERB", "ADJ", "INTJ", "X", "PROPN", "ADV", "PRON", "NUM") \
                and before not in R.SPEECH_VERBS and not before.endswith(("ed", "ing")) \
                and before not in ("speak", "talk", "write", "sing", "read", "pray", "think", "live"):
            out.append((i - 2, 1, "pattern=X in %s" % s.f[i]))
        if s.word(i + 1) in ("word", "phrase", "term", "expression", "name") and s.word(i - 1) in ("a", "the", "this", "that"):
            out.append((i, 2, "pattern=%s %s" % (s.f[i], s.w[i + 1])))
    return out


@candidate("foreign-token", "a token that is not English: X or a diacritic outside names (+foreign, own-language category)")
def foreign_token(s, res):
    out = []
    for i in range(s.n):
        word = s.f[i]
        if "\ufffd" in word or s.penn(i) in ("ADD", "XX", "GW", "LS"):
            continue  # damaged encoding, addresses, unclassified tokens and list markers are not foreign words
        if s.t[i] == "X" and word.isalpha():
            out.append((i, 1, "tag=X"))
        elif s.t[i] not in ("PROPN", "X", "_") and word.isalpha() \
                and any(ord(ch) > 127 and unicodedata.category(ch).startswith("L") for ch in word):
            out.append((i, 1, "non-ascii-letter"))
    return out


def _capitalized(word):
    return word[:1].isupper()


@candidate("propn-boundary",
           "capitalization and tag disagree: a capitalized NOUN inside a sentence, a lower-case PROPN, `the City`, `AM`/`FM`")
def propn_boundary(s, res):
    out = []
    for i in range(s.n):
        word = s.f[i]
        if i > 0 and _capitalized(word) and s.t[i] == "NOUN" and s.t[i - 1] != "PUNCT" and len(word) > 1 and not word.isupper():
            # a capitalized word inside a run of capitalized words is title case, and a plural is mostly a demonym or a title
            if _capitalized(s.f[i - 1]) or _capitalized(s.form(i + 1)) or (word.endswith("s") and len(word) > 4):
                continue
            out.append((i, 1, "capitalized-noun"))
        elif word[:1].islower() and s.t[i] == "PROPN":
            out.append((i, 1, "lowercase-propn"))
        elif word == "City" and s.word(i - 1) == "the" and s.t[i] != "PROPN":
            out.append((i, 1, "the-City"))
        elif word in ("AM", "FM") and s.t[i] != "PROPN" and not (i > 0 and R.clock_hour(s.f[i - 1]) is not None):
            out.append((i, 1, "radio-band"))
    return out


@candidate("language-name",
           "a language name as PROPN, ADJ or NOUN: the name of the language or of the people or place (Names and Labels)")
def language_name(s, res):
    out = []
    for i in range(s.n):
        if s.f[i] in R.LANGUAGES and s.t[i] in ("PROPN", "ADJ", "NOUN"):
            out.append((i, 1, "next=%s" % (s.tag(i + 1) or "end")))
    return out


@candidate("title-alone", "a capitalized title that is not directly before a name: PROPN (this particular person) or NOUN",
           words={item.lower() for item in R.TITLES} | {item.lower() + "." for item in R.TITLES})
def title_alone(s, res):
    out = []
    for i in range(s.n):
        if s.f[i].rstrip(".") in R.TITLES and s.t[i] in ("NOUN", "ADJ") and s.tag(i + 1) != "PROPN":
            out.append((i, 1, "position=%s" % ("start" if i == 0 else "inside")))
    return out


@candidate("possessive-her", "`her` after a verb and before a noun, adjective or number: indirect object (PRON) or possessive (DET)", words={"her"})
def possessive_her(s, res):
    out = []
    for i in s.where("her"):
        if s.t[i] == "PRON" and s.tag(i + 1) in ("NOUN", "ADJ", "NUM", "PROPN") and s.tag(i - 1) in ("VERB", "AUX"):
            out.append((i, 1, "verb=%s; next=%s" % (s.word(i - 1), s.tag(i + 1))))
    return out


@candidate("title-before-name",
           "a capitalized title directly before a name: PROPN when the name is a person (`President Bush`), not before an organization",
           words={item.lower() for item in R.TITLES} | {item.lower() + "." for item in R.TITLES})
def title_before_name(s, res):
    out = []
    for i in range(s.n - 1):
        if s.f[i].rstrip(".") in R.TITLES and s.t[i + 1] == "PROPN" and s.t[i] in ("NOUN", "ADJ", "X"):
            out.append((i, 1, "name=%s" % s.f[i + 1]))
    return out


@candidate("out-of-after-verb", "`out of` after a verb other than `run` or `be`: phrasal ADV ADP or the preposition ADP ADP",
           words={"out"})
def out_of_after_verb(s, res):
    out = []
    for i in s.where("out"):
        previous = s.word(i - 1)
        if s.word(i + 1) == "of" and s.tag(i - 1) in ("VERB", "AUX") and previous not in R.BE \
                and previous not in ("run", "runs", "ran", "running"):
            out.append((i, 1, "verb=%s" % previous))
    return out


@candidate("for-subject-to-ambiguous", "`for` + subject + `to` + verb outside the sure cases: SCONJ or ADP", words={"for"})
def for_subject_to_ambiguous(s, res):
    out = []
    for i in s.where("for"):
        found = R.for_subject_to(s, i)
        if found and not R.for_subject_whitelisted(s, i, found[0]):
            out.append((i, 1, "before=%s; subject=%s" % (s.word(i - 1) or "-", s.w[found[0]])))
    return out


@candidate("degree-word-ambiguous",
           "`more, most, less, least` before an adjective or adverb without a clue: ADV (degree) or ADJ (quantifier)",
           words={"more", "most", "less", "least"})
def degree_word_ambiguous(s, res):
    out = []
    for i in s.where("more", "most", "less", "least"):
        if s.t[i] in ("ADJ", "PRON") and s.tag(i + 1) in ("ADJ", "ADV") and s.word(i + 1) != "than" and s.word(i - 1) != "at":
            out.append((i, 1, "next=%s; after=%s; before=%s" % (s.tag(i + 1), s.tag(i + 2) or "end", s.word(i - 1) or "-")))
    return out


@candidate("half-ambiguous", "`half` tagged NUM or PRON outside the patterns of the mechanical rules", words={"half"})
def half_ambiguous(s, res):
    out = []
    for i in s.where("half"):
        if s.t[i] in ("NUM", "PRON"):
            out.append((i, 1, "next=%s" % (s.word(i + 1) or "end")))
    return out


@candidate("list-label", "an item label at the start of a sentence (`1)`, `(a)`, `A.`): digits NUM, letters X, speaker label PROPN")
def list_label(s, res):
    out = []
    if s.n >= 2 and s.f[1] in (")", ".") and (s.f[0].isdigit() or (len(s.f[0]) == 1 and s.f[0].isalpha())) \
            and s.t[0] not in ("NUM", "X", "PROPN"):
        out.append((0, 1, "label=%s%s" % (s.f[0], s.f[1])))
    if s.n >= 3 and s.f[0] == "(" and s.f[2] == ")" and len(s.f[1]) <= 2 and s.t[1] not in ("NUM", "X"):
        out.append((1, 1, "label=(%s)" % s.f[1]))
    return out


def evaluate(s, res, only=None):
    """All candidates of one sentence as `(candidate_id, index, span, hint)` tuples, detectors in registration order."""
    found = []
    for item in CANDIDATES:
        if only is not None and item.id not in only:
            continue
        if item.words is not None and item.words.isdisjoint(s.wordset):
            continue
        for index, span, hint in item.detect(s, res):
            if s.t[index] != "_":
                found.append((item.id, index, span, hint))
    return found

"""Convention rules: the detectors shared by the corrections generator and the convention linter (Python 3, stdlib only).

Every rule of data/CONVENTIONS.md that can be checked by looking at a token and its neighbors is a small function here.
One registry feeds both consumers, so what the linter reports is exactly what the generator stages:

* tier `fix` (mechanical): the rule knows the correct tag. The generator writes one correction row per proposal and
  the linter reports a proposal that is still pending as a violation. After the corrections are applied the proposals
  must be gone (the fixed point; the generator's `fixed-point` command and the tests prove it on the real corpora).
* tier `check`: a violation that needs judgment or a human decision. The linter reports it, the adjudication resolves it
  (a correction row or an entry in exceptions.tsv), and `--enforce` demands zero.

A detector gets a `Sentence` and the corpus `Resources` and returns `(token_index, new_tag)` pairs (`new_tag` is None for
a violation without a fix). Detectors read the tags as they are in the file; the guards inside them are the precision
of the rule (see the precision record in data/corrections/README.md). Masked tokens (`_`) are never touched.

The module has no side effects at import time except filling the registry.
"""
import collections
import re

RULE_SET_VERSION = "1"

FIX, CHECK = "fix", "check"

Rule = collections.namedtuple("Rule", "id tier section doc words detect")
Proposal = collections.namedtuple("Proposal", "index new_tag rule tier")

RULES = []  # registration order is the priority order of the fix rules


def rule(rule_id, tier, section, doc, words=None):
    """Register a detector. `words` (lower-case trigger words) lets the dispatcher skip sentences without any of them."""
    def register(function):
        RULES.append(Rule(rule_id, tier, section, doc, frozenset(words) if words else None, function))
        return function
    return register


def rule_ids(tier=None):
    """Ids of the registered rules (optionally of one tier)."""
    return {item.id for item in RULES if tier is None or item.tier == tier}


def catalog():
    """(id, tier, CONVENTIONS section, one-line description) of every rule in priority order."""
    return [(item.id, item.tier, item.section, item.doc) for item in RULES]


# ---------------------------------------------------------------- sentence context

class Sentence:
    """A sentence as the rules see it: parallel lists of forms, lower-case forms, tags, status cells and Penn tags.

    `p` is the Penn Treebank tag of a MASC token (the `source_tag` column) and "" elsewhere; `q` is the raw `source_tag`
    column of any source (the model class of the silver data). All index accessors are safe at the borders.
    """

    def __init__(self, source, split, sentence):
        rows = sentence["rows"]
        self.source, self.split = source, split
        self.sent_id, self.source_id = sentence["sent_id"], sentence["source_id"]
        self.f = [cells[0] for cells in rows]
        self.w = [cells[0].lower() for cells in rows]
        self.t = [cells[1] for cells in rows]
        self.st = [cells[2] for cells in rows]
        self.q = [cells[3] if len(cells) > 3 else "" for cells in rows]
        self.p = self.q if source == "masc" else [""] * len(rows)
        self.n = len(rows)
        self.wordset = frozenset(self.w)
        self._where = None
        self._participles = None

    def tag(self, i):
        return self.t[i] if 0 <= i < self.n else ""

    def word(self, i):
        return self.w[i] if 0 <= i < self.n else ""

    def form(self, i):
        return self.f[i] if 0 <= i < self.n else ""

    def penn(self, i):
        return self.p[i] if 0 <= i < self.n else ""

    def where(self, *words):
        """Indices of the tokens whose lower-case form is one of the words, in sentence order."""
        if self._where is None:
            self._where = collections.defaultdict(list)
            for index, word in enumerate(self.w):
                self._where[word].append(index)
        found = []
        for word in words:
            found.extend(self._where.get(word, ()))
        return sorted(found) if len(words) > 1 else found

    def change(self, out, i, new):
        """Append a proposal for token i unless it is masked or already has the tag (`new` None: a violation without fix)."""
        old = self.t[i]
        if old != "_" and old != new:
            out.append((i, new))

    def window(self, i, span=1, size=6):
        """Readable context: the tokens around i with the target tokens in brackets."""
        left = self.f[max(0, i - size):i]
        right = self.f[i + span:i + span + size]
        return " ".join(left + ["[" + " ".join(self.f[i:i + span]) + "]"] + right)


# ---------------------------------------------------------------- word lists and helpers

QUANT_DET = frozenset("all any both each either some".split())
QUANT_ADJ = frozenset("many much several few more most less enough".split())
# Degree words that prove gradability: the participle accepts them with its meaning unchanged (`very confused`, `more surprised`).
DEGREE_STRONG = frozenset("very more less too quite extremely how incredibly terribly awfully".split())
# `so`, `really`, `rather`, `pretty` also mean `thus`, `truly`, `fairly` (`so characterized`, `rather hinted than told`, `really
# engaged at Camp David`): they count only for a participle of the curated list of psychological and evaluative forms (GRADABLE_SEED).
DEGREE_WEAK = frozenset("so really rather pretty".split())
# Adverbs that grade, focus or complete a verb as well (`somewhat amended`, `completely reassured`, `well established`,
# `most reserved for`, `particularly seen`, `increasingly emphasised`): they show that a degree word is present, but not that the
# participle is a gradable adjective.
DEGREE_BLOCKING = frozenset(("most least particularly especially increasingly somewhat slightly partly greatly deeply highly truly "
                             "mildly bitterly painfully utterly completely fully entirely totally thoroughly perfectly wholly "
                             "absolutely well far much fairly").split())
DEGREE = DEGREE_STRONG | DEGREE_WEAK | DEGREE_BLOCKING
DEGREE_NEAR = DEGREE_STRONG | DEGREE_WEAK
# Words whose tag (ADJ or ADV) the degree-word rule may change; the participle contexts treat them as adverbs either way, so
# that applying a correction cannot create a new context.
DEGREE_FLIP = frozenset("more most less least much".split())
BE = frozenset(("is are was were be been being am 's 're 'm \u2019s \u2019re \u2019m").split())
LINK_STEMS = ("become", "feel", "look", "remain", "seem", "sound")
LINK = frozenset([stem + ending for stem in LINK_STEMS for ending in ("", "s", "ed", "ing")]
                 + ["became", "becomes", "becoming"])  # not `felt`: `what they felt belonged to them` is a clause
GET_FORMS = frozenset("get gets got gotten getting".split())
IRREGULAR_PARTICIPLES = frozenset(("known shown done made given taken seen found held told built brought kept left lost paid sold set "
                                   "put cut hit read said got gotten gone become begun drawn grown thrown worn torn born chosen written "
                                   "broken spoken forgotten hidden stolen driven eaten fallen frozen woken risen bitten beaten ridden "
                                   "shaken forgiven proven sworn sung sunk flown lain laid led met sent spent lent bent caught taught "
                                   "thought bought fought sought heard stood understood won shot hung dug stuck struck swept slept wept "
                                   "felt dealt meant fed fled bled lit slid").split())
NOT_PARTICIPLES = frozenset(("need indeed speed feed seed bleed breed proceed succeed exceed hundred naked wicked sacred wretched "
                             "crooked rugged ragged red bed shed weed creed deed greed steed tweed hatred kindred").split())
# Participles that name a state after `get` (get depressed: the main verb and an adjective).
GET_STATE = frozenset(("used involved depressed scared confused tired excited worried lost drunk stuck bored annoyed upset interested "
                       "pleased offended embarrassed frightened ashamed stressed surprised shocked disappointed done gone set left fed "
                       "determined").split())
# Participles that are always gradable (very scared, so tired); the corpora add the forms they show after a degree word.
GRADABLE_SEED = frozenset(("surprised shocked amazed astonished annoyed interested pleased delighted worried confused excited "
                           "disappointed frightened scared tired bored satisfied embarrassed ashamed upset depressed disgusted "
                           "concerned irritated offended impressed convinced determined married experienced educated skilled "
                           "talented limited advanced complicated crowded detailed organized prepared qualified sophisticated "
                           "terrified thrilled relieved stressed exhausted frustrated amused puzzled troubled touched "
                           "disturbed alarmed shaken overwhelmed obsessed addicted inspired motivated dedicated committed "
                           "involved devoted attached related known respected renowned unexpected").split())
TITLES = frozenset(("President Marshal King Queen General Emperor Pope Duke Archduke Prince Captain Colonel Judge Senator Doctor "
                    "Professor Father Master Lord Lady Madame Mr Mrs Ms Dr").split())
LANGUAGES = frozenset(("English Spanish French German Italian Latin Greek Russian Chinese Japanese Arabic Hebrew Portuguese Dutch "
                       "Hindi Korean Swedish Polish Turkish Esperanto Irish Welsh Norwegian Danish Finnish Hungarian Czech").split())
SPEECH_VERBS = frozenset(("say says said saying ask asks asked asking tell tells told reply replied shout shouted whisper whispered "
                          "cry cried exclaim exclaimed answer answered write wrote").split())
POSSESSIVES = frozenset("my your his her its our their whose".split())


def is_participle(word):
    """A past-participle shape: an irregular form or a word in -ed (a few -ed nouns and adjectives are excluded)."""
    return word in IRREGULAR_PARTICIPLES or (word.endswith("ed") and len(word) >= 4 and word not in NOT_PARTICIPLES)


def clock_hour(token):
    """The hour of a clock numeral (`5`, `10:30`, `7.45`) or None."""
    match = re.fullmatch(r"(\d{1,2})(?::\d{2}|\.\d{2})?", token)
    return int(match.group(1)) if match else None


class Resources:
    """Lexical knowledge derived from the corpora that the participle rules need.

    `gradable`: forms that accept a degree word (the corpus shows `very X`, `more X`, `too X`, ... with a participle shape,
    tagged VERB or ADJ) plus a seed list of psychological and evaluative participles.
    """

    def __init__(self, gradable):
        self.gradable = frozenset(gradable)


def build_resources(sentences):
    """Derive `Resources` from an iterable of `Sentence` objects (all corpora)."""
    gradable = set(GRADABLE_SEED)
    for s in sentences:
        for i in range(1, s.n):
            if s.t[i] in ("VERB", "ADJ") and s.w[i - 1] in DEGREE_STRONG and is_participle(s.w[i]):
                gradable.add(s.w[i])
    return Resources(gradable)


TIME_AFTER_BY = frozenset(("tomorrow today tonight next then now noon midnight monday tuesday wednesday thursday friday saturday "
                           "sunday january february march april may june july august september october november december").split())


def _agent(s, j):
    """True when `by` or `from` with an agent follows the participle at j directly (adverbs may stand between).

    The scan stops at punctuation, a conjunction, a verb or any other preposition, so `based on insights from seismic
    studies` has no agent; `by 5 pm`, `by the time`, `by the way` and `by` + gerund (a means, not an agent) do not count.
    """
    for k in range(j + 1, min(s.n, j + 6)):
        if s.t[k] in ("PUNCT", "CCONJ", "SCONJ", "VERB", "AUX"):
            return False
        if s.t[k] == "ADP":
            if s.w[k] not in ("by", "from"):
                return False
            after = s.word(k + 1)
            if s.tag(k + 1) in ("NUM", "VERB", "AUX") or after in TIME_AFTER_BY \
                    or (after == "the" and s.word(k + 2) in ("end", "time", "way", "middle", "beginning")):
                return False
            return True
    return False


def _participle_contexts(s):
    """Participles after a form of `be` or a linking verb: tuples (j, k, kind, degree, agent), memoized per sentence.

    j is the participle, k the `be` or linking verb in front of it (up to three adverbs or particles may stand between,
    `was not killed`), `kind` is `be` or `link`, `degree` says that a degree word stands in front of the participle, and
    `agent` that an overt `by`/`from` agent follows within six tokens (not at punctuation, not a time such as `by 5 pm`).
    """
    if s._participles is not None:
        return s._participles
    found = []
    for j in range(1, s.n):
        word = s.w[j]
        if s.t[j] not in ("VERB", "ADJ") or not is_participle(word) or s.tag(j + 1) in ("NOUN", "PROPN", "ADJ"):
            continue  # a participle in front of a noun is a modifier (`is just organised superstition`), not a predicate
        k, steps, degree = j - 1, 0, False
        while k >= 0 and steps < 3 and (s.t[k] == "ADV" or (s.t[k] == "PART" and s.w[k] in ("not", "n't"))
                                        or (s.w[k] in DEGREE_FLIP and s.t[k] in ("ADJ", "PRON"))):
            degree |= s.w[k] in DEGREE
            k -= 1
            steps += 1
        if k < 0:
            continue
        if s.w[k] in BE and s.t[k] in ("AUX", "VERB"):
            kind = "be"
        elif s.w[k] in LINK and s.t[k] == "VERB":
            kind = "link"
        else:
            continue
        found.append((j, k, kind, degree, _agent(s, j)))
    s._participles = found
    return found


# ---------------------------------------------------------------- mechanical rules, in priority order

MULTIWORD = {("due", "to"): ("ADJ", "ADP"), ("prior", "to"): ("ADJ", "ADP"), ("such", "as"): ("ADJ", "ADP"),
             ("instead", "of"): ("ADV", "ADP"), ("next", "to"): ("ADV", "ADP"), ("rather", "than"): ("ADV", "ADP"),
             ("because", "of"): ("ADP", "ADP"), ("along", "with"): ("ADP", "ADP"), ("thanks", "to"): ("NOUN", "ADP")}
MULTIWORD_FIRST = frozenset(first for first, _ in MULTIWORD)


@rule("multiword-prep", FIX, "Multiword Prepositions",
      "`due to`, `prior to`, `such as` ADJ ADP; `instead of`, `next to`, `rather than` ADV ADP; `because of`, `along with` "
      "ADP ADP; `thanks to` NOUN ADP; the last word is ADP (SCONJ before a clause is kept)", words=MULTIWORD_FIRST)
def multiword_prep(s, res):
    out = []
    for i in s.where(*MULTIWORD_FIRST):
        pair = (s.w[i], s.word(i + 1))
        if pair not in MULTIWORD or s.t[i] in ("VERB", "PUNCT") or s.tag(i + 1) in ("PART", "PUNCT", ""):
            continue  # `due to arrive`, `next to nothing` as an infinitive are not the expression
        if pair[0] == "along" and s.tag(i - 1) in ("VERB", "AUX", "ADV", "ADP"):
            continue  # `come along with me`, `get along with`: the phrasal verb keeps its particle ADV
        first, last = MULTIWORD[pair]
        s.change(out, i, first)
        if s.tag(i + 1) in ("SCONJ", "ADP"):
            continue
        if pair[1] == "than" and s.tag(i + 2) == "PRON":
            continue  # `rather than he went`: a clause, left for the check
        s.change(out, i + 1, last)
    return out


NOMINATIVE = frozenset("i he she we they you it who".split())


def starts_clause(s, j):
    """True when token j is a subject pronoun that a finite verb follows (`he could`, `I went`): a clause with its own subject."""
    return s.tag(j) == "PRON" and s.word(j) in NOMINATIVE and s.tag(j + 1) in ("VERB", "AUX", "PART")


@rule("in-front-of", FIX, "Multiword Prepositions",
      "`in front of`: ADP NOUN ADP (`front` is a noun, never ADJ; SCONJ before a clause is kept)", words={"front"})
def in_front_of(s, res):
    out = []
    for i in s.where("front"):
        if s.word(i - 1) != "in" or s.word(i + 1) != "of" or s.tag(i - 1) != "ADP" or s.tag(i + 2) in ("PUNCT", ""):
            continue  # `in front of .` is cut off: the expression is not complete
        s.change(out, i, "NOUN")
        if s.tag(i + 1) not in ("SCONJ", "ADP") and not starts_clause(s, i + 2):
            s.change(out, i + 1, "ADP")
    return out


@rule("as-well-as", FIX, "Multiword Prepositions",
      "`as well as`: ADV ADV ADP (the first `as` and `well` are adverbs; the last `as` stays SCONJ before a clause and is not "
      "changed before a bare verb or `possible`)", words={"well"})
def as_well_as(s, res):
    out = []
    for i in s.where("well"):
        if s.word(i - 1) != "as" or s.word(i + 1) != "as" or s.t[i] not in ("ADV", "CCONJ", "ADP", "SCONJ") \
                or s.tag(i + 2) in ("PUNCT", ""):
            continue
        s.change(out, i - 1, "ADV")
        s.change(out, i, "ADV")
        after = i + 2
        noun_phrase = s.tag(after) in ("NOUN", "PROPN", "ADJ", "DET", "NUM", "SYM") or is_gerund(s, after) \
            or (s.tag(after) == "PRON" and not starts_clause(s, after))
        if s.t[i + 1] not in ("SCONJ", "ADP") and noun_phrase and s.word(after) != "possible":
            s.change(out, i + 1, "ADP")
    return out


@rule("according-to", FIX, "Multiword Prepositions", "`according to`: both words ADP (`according` is not VERB or ADJ)",
      words={"according"})
def according_to(s, res):
    out = []
    for i in s.where("according"):
        if s.word(i + 1) == "to":
            s.change(out, i, "ADP")
            s.change(out, i + 1, "ADP")
    return out


@rule("out-of", FIX, "Multiword Prepositions",
      "`out of` ADP ADP after a noun phrase, an adverb or a form of `be` (not a phrasal verb such as `get out of`)", words={"out"})
def out_of(s, res):
    out = []
    for i in s.where("out"):
        if s.word(i + 1) != "of" or s.t[i] != "ADV":
            continue
        previous = s.word(i - 1)
        if previous in ("run", "runs", "ran", "running"):
            continue
        if s.tag(i - 1) in ("VERB", "AUX") and previous not in BE:
            continue
        s.change(out, i, "ADP")
        s.change(out, i + 1, "ADP")
    return out


@rule("at-least", FIX, "Degree and Quantity Words", "`at least`, `at most`: ADP ADJ (`at` is ADP, never ADV)", words={"least", "most"})
def at_least(s, res):
    out = []
    for i in s.where("least", "most"):
        if s.word(i - 1) == "at" and s.tag(i - 1) in ("ADP", "ADV") and s.t[i] in ("ADV", "PRON"):
            s.change(out, i - 1, "ADP")
            s.change(out, i, "ADJ")
    return out


NUMBER_WORDS = frozenset(("two three four five six seven eight nine ten eleven twelve thirteen fourteen fifteen sixteen seventeen "
                          "eighteen nineteen twenty thirty forty fifty sixty seventy eighty ninety hundred thousand million "
                          "billion dozen").split())


def is_number_word(word):
    """A spelled-out number (`ten`, `fifty-four`, `hundred`)."""
    return word in NUMBER_WORDS or (word.count("-") == 1 and all(part in NUMBER_WORDS for part in word.split("-")))


MEASURE_NOUNS = frozenset(("year day hour minute second week month century decade mile foot inch quarter fraction handful dozen "
                           "couple few bit nickel dime dollar cent penny pound ton gallon third half percent score pair lot").split())


def is_measure_after_article(s, j):
    """True when the token at j after `a`/`an` starts a measure (`a year`, `a few`, `a hundred`, `a 3 % drop`, `a six-month`), not a plain noun."""
    word = s.word(j)
    return s.tag(j) == "NUM" or word in MEASURE_NOUNS or is_number_word(word) or is_number_word(word.split("-")[0])


def is_quantity(s, j):
    """True when the token at j starts a quantity: a number, a spelled number, a currency sign before a number, `half`, `one`, `twice`, or
    `a`/`an` and a measure (`a year`, `a dozen`; not `a ghost`, `a disgrace`: `little more than a ghost` compares degree)."""
    word = s.word(j)
    if word in ("a", "an"):
        return is_measure_after_article(s, j + 1)
    return s.tag(j) == "NUM" or word in ("half", "one", "twice") \
        or (s.tag(j) == "SYM" and s.tag(j + 1) == "NUM") or (s.tag(j) == "ADJ" and is_number_word(word))


@rule("more-than", FIX, "Degree and Quantity Words",
      "`more than`, `less than` before a quantity (number, spelled number, `$` + number, `half`, `one`, `twice`, `a`/`an` + a "
      "measure such as `a year`, `a dozen`; not `little more than a ghost`): `more`/`less` ADJ and `than` ADP",
      words={"more", "less"})
def more_than(s, res):
    out = []
    for i in s.where("more", "less"):
        if s.word(i + 1) != "than" or s.word(i - 1) == "any":
            continue  # `any more than a cloud is`: a comparison of clauses
        after = s.word(i + 2)
        if not is_quantity(s, i + 2):
            continue
        if after in ("a", "an") and s.tag(i + 4) in ("VERB", "AUX"):
            continue  # `more than a cloud is ...`: the noun phrase is the subject of a clause
        if s.t[i] in ("ADV", "PRON"):
            s.change(out, i, "ADJ")
        if s.tag(i + 1) in ("SCONJ", "CCONJ", "ADV"):
            s.change(out, i + 1, "ADP")
    return out


@rule("a-little", FIX, "Degree and Quantity Words", "`a little`: DET ADJ (`little` is never ADV, PRON or NOUN after `a`)",
      words={"little"})
def a_little(s, res):
    out = []
    for i in s.where("little"):
        if s.word(i - 1) == "a" and s.tag(i - 1) == "DET" and s.t[i] in ("ADV", "PRON", "NOUN") and not s.f[i][0].isupper():
            s.change(out, i, "ADJ")
    return out


PARTITIVE_NOUN = frozenset(("plenty", "lots"))


@rule("partitive-det", FIX, "Degree and Quantity Words",
      "`all, any, both, each, either, some` before `of`: DET (never PRON, ADV, NOUN or ADJ); not `at all of`, `all of a sudden`; "
      "`both`/`either` as CCONJ (`either of guidance or of`) are correlatives", words=QUANT_DET)
def partitive_det(s, res):
    out = []
    for i in s.where(*QUANT_DET):
        if s.word(i + 1) == "of" and s.t[i] in ("PRON", "ADV", "NOUN", "ADJ") and s.word(i + 2) != "course" \
                and not (s.w[i] == "all" and (s.word(i - 1) == "at" or (s.word(i + 2) == "a" and s.word(i + 3) == "sudden"))):
            s.change(out, i, "DET")
    return out


@rule("partitive-adj", FIX, "Degree and Quantity Words",
      "`many, much, several, few, more, most, less, enough` before `of`: ADJ (never PRON, ADV, NOUN or DET); not `more of a`, "
      "`most of all`",
      words=QUANT_ADJ)
def partitive_adj(s, res):
    out = []
    for i in s.where(*QUANT_ADJ):
        if s.word(i + 1) != "of" or s.t[i] not in ("PRON", "ADV", "NOUN", "DET"):
            continue
        after = s.word(i + 2)
        if after in ("course", "all", "late") or (s.w[i] in ("more", "less", "much", "most") and after in ("a", "an")):
            continue
        s.change(out, i, "ADJ")
    return out


@rule("partitive-noun", FIX, "Degree and Quantity Words", "`plenty of`, `lots of`: NOUN", words=PARTITIVE_NOUN)
def partitive_noun(s, res):
    out = []
    for i in s.where(*PARTITIVE_NOUN):
        if s.word(i + 1) == "of" and s.t[i] in ("PRON", "ADV", "ADJ", "DET", "NUM"):
            s.change(out, i, "NOUN")
    return out


ONE_OF_BEFORE = frozenset("the this that which what no any every each another a an some such other same last next first".split())
ONE_OF_DET = frozenset("the these those his her its their our my your whose each all both some any a an many several few".split())
ONE_OF_PRON = frozenset("them us you him her these those themselves ourselves yourselves which whom each".split())
ONE_OF_ADJ = frozenset("many several few numerous various countless certain".split())


def _plural_noun(word):
    """A regular plural shape: ends in -s but not in -ss, -us or -is (`accomplishments`, not `prominence`, `success`, `Paris`)."""
    return len(word) > 3 and word.endswith("s") and not word.endswith(("ss", "us", "is"))


def one_of_noun_phrase(s, j):
    """True when a noun phrase or pronoun that `one of` can partition starts at j, judged by tags and a few closed classes.

    That is a determiner that makes the noun phrase definite or quantified (`the`, `these`, a possessive), an object or
    demonstrative pronoun (`them`, `which`), a number, a quantifier adjective (`many`) or adjectives before a regular plural
    noun, and a name with a possessive (`Africa 's most ...`). Not a bare singular noun (`one of style`), a name without
    possessive, `a kind`, `this`/`that`/`no` (`one of that brand`, `one of no importance`) or `another`.
    """
    tag, word = s.tag(j), s.word(j)
    if tag == "DET":
        return word in ONE_OF_DET and not (word in ("a", "an") and s.word(j + 1) == "kind")
    if tag == "PRON":
        return word in ONE_OF_PRON
    if tag == "NUM":
        return True
    if tag == "ADJ":
        if word in ONE_OF_ADJ:
            return True
        k = j
        while s.tag(k) == "ADJ" and k < j + 3:
            k += 1
        return s.tag(k) == "NOUN" and _plural_noun(s.word(k))
    if tag == "PROPN":
        k = j
        while s.tag(k) == "PROPN" and k < j + 4:
            k += 1
        return s.word(k) in ("'s", "\u2019s", "'", "\u2019") and s.tag(k) in ("PART", "PUNCT")
    return False


@rule("one-of", FIX, "Numbers and Dates",
      "`one of` + noun phrase or pronoun (determiner, pronoun, number, plural noun phrase, name with `'s`): `one` NUM "
      "(not after `the`, `which`, `any`, a determiner or an adjective; `that` as SCONJ is fine)", words={"one"})
def one_of(s, res):
    out = []
    for i in s.where("one"):
        if s.word(i + 1) != "of" or s.t[i] != "PRON" or not one_of_noun_phrase(s, i + 2):
            continue
        before = s.word(i - 1)
        if s.tag(i - 1) in ("ADJ", "DET") or (before in ONE_OF_BEFORE and s.tag(i - 1) != "SCONJ"):
            continue
        s.change(out, i, "NUM")
    return out


@rule("half-noun", FIX, "Degree and Quantity Words", "`half of`: `half` NOUN", words={"half"})
def half_noun(s, res):
    out = []
    for i in s.where("half"):
        if s.word(i + 1) == "of" and s.word(i - 1) not in ("a", "an") and s.t[i] in ("NUM", "PRON", "ADJ", "DET", "ADV"):
            s.change(out, i, "NOUN")
    return out


HALF_DET_AFTER = frozenset("the a an his her their my your our its this that these those".split())


@rule("half-det", FIX, "Degree and Quantity Words",
      "`half` before `the`, `a`, `an` or a possessive (not after `a`/`an`): DET", words={"half"})
def half_det(s, res):
    out = []
    for i in s.where("half"):
        if s.word(i + 1) in HALF_DET_AFTER and s.tag(i + 1) == "DET" and s.word(i - 1) not in ("a", "an") \
                and s.t[i] in ("ADJ", "NUM", "PRON", "NOUN", "ADV"):
            s.change(out, i, "DET")
    return out


@rule("half-adj", FIX, "Degree and Quantity Words",
      "`half` after `a`/`an` before a noun or number: ADJ (`a half century`)", words={"half"})
def half_adj(s, res):
    out = []
    for i in s.where("half"):
        if s.word(i - 1) in ("a", "an") and s.tag(i + 1) in ("NOUN", "NUM") and s.t[i] in ("NUM", "PRON", "ADV", "DET"):
            s.change(out, i, "ADJ")
    return out


@rule("half-adv", FIX, "Degree and Quantity Words",
      "`half` before an adjective or adverb (`half naked`): ADV", words={"half"})
def half_adv(s, res):
    out = []
    for i in s.where("half"):
        if s.word(i - 1) not in ("a", "an") and s.tag(i - 1) != "ADP" and s.tag(i + 1) in ("ADJ", "ADV") \
                and s.t[i] in ("ADJ", "NUM", "NOUN", "PRON", "DET"):
            s.change(out, i, "ADV")
    return out


ADV_AFTER_DEGREE = frozenset("often well soon far fast early late long".split())
NOT_COMPARATIVE = frozenset("other over under after never ever either neither another together however whatever whoever rather "
                            "later water number matter order paper power river user server letter center former latter inner "
                            "outer upper".split())


@rule("degree-word", FIX, "Degree and Quantity Words",
      "`more, most, less, least, much` before an adjective or adverb that is not followed by a noun: ADV; `enough` after an "
      "adjective or adverb, and `else`: ADV", words={"more", "most", "less", "least", "much", "enough", "else"})
def degree_word(s, res):
    out = []
    for i in s.where("more", "most", "less", "least", "much"):
        word = s.w[i]
        if s.t[i] not in ("ADJ", "PRON"):
            continue
        next_tag, next_word = s.tag(i + 1), s.word(i + 1)
        if next_tag not in ("ADJ", "ADV") or next_word == "than" or s.word(i - 1) == "at":
            continue
        if next_tag == "ADV" and not (next_word.endswith("ly") or next_word in ADV_AFTER_DEGREE
                                      or next_word in ("more", "less", "too")):
            continue
        if word == "much" and not (next_word in ("more", "less", "better", "worse", "too") or
                                   (next_word.endswith("er") and next_word not in NOT_COMPARATIVE)):
            continue
        # A noun, name or adjective after the pair means the first word quantifies the noun phrase (`more LGBTQ Americans`),
        # unless `a`/`an` in front proves the adverb (`a more important role`).
        open_end = s.tag(i + 2) not in ("NOUN", "PROPN", "ADJ")
        if open_end or s.word(i - 1) in ("a", "an"):
            s.change(out, i, "ADV")
    for i in s.where("enough"):
        previous, previous_tag = s.word(i - 1), s.tag(i - 1)
        if s.t[i] != "ADJ":
            continue
        adjective = previous_tag == "ADJ" and previous not in QUANT_ADJ and previous not in ("other", "least")
        adverb = previous_tag == "ADV" and previous in ADV_AFTER_DEGREE  # not `nearly enough`, `barely enough`
        if (adjective or adverb) and s.tag(i + 1) not in ("NOUN", "PROPN"):
            s.change(out, i, "ADV")
    for i in s.where("else"):
        if s.t[i] == "ADJ":
            s.change(out, i, "ADV")
    return out


@rule("superlative-the", FIX, "Degree and Quantity Words",
      "`the` before a superlative (`the most`, `the longest`): DET, never ADV", words={"the"})
def superlative_the(s, res):
    out = []
    for i in s.where("the"):
        nxt = s.word(i + 1)
        if s.t[i] == "ADV" and (nxt in ("most", "least") or (nxt.endswith("est") and len(nxt) > 4 and s.tag(i + 1) in ("ADJ", "ADV"))):
            s.change(out, i, "DET")
    return out


def _comparative(s, i):
    word = s.word(i)
    return s.tag(i) in ("ADJ", "ADV") and (word in ("more", "less", "better", "worse", "further", "farther") or
                                           (word.endswith("er") and len(word) > 3 and word not in NOT_COMPARATIVE))


@rule("correlative-the", FIX, "Degree and Quantity Words", "`the more ..., the more ...`: both `the` ADV", words={"the"})
def correlative_the(s, res):
    out = []
    positions = [i for i in s.where("the") if _comparative(s, i + 1)]
    for a, b in zip(positions, positions[1:]):
        if b - a > 12:
            continue
        # end of the first comparative phrase (`the more pregnant`), then a clause starts with its subject pronoun
        # (`the more you wait, the harder it gets`) or the second `the` follows after a comma (`the bigger, the better`)
        end = a + 2 if s.word(a + 1) in ("more", "less") and s.tag(a + 2) in ("ADJ", "ADV") else a + 1
        if (s.tag(end + 1) == "PRON" and b > end + 1) or (s.word(end + 1) == "," and b == end + 2):
            for i in (a, b):
                if s.t[i] in ("DET", "PRON"):
                    s.change(out, i, "ADV")
    return out


DECADE_RE = re.compile(r"^(?:mid-)?(?:\d{3}|['\u2019]?\d)0['\u2019]?s$", re.IGNORECASE)


@rule("decade", FIX, "Numbers and Dates", "a decade (`1990s`, `'90s`, `mid-1800s`) is a NOUN", words=None)
def decade(s, res):
    out = []
    for i in range(s.n):
        if s.t[i] in ("NUM", "ADJ", "PROPN", "X", "ADV") and DECADE_RE.match(s.f[i]):
            s.change(out, i, "NOUN")
    return out


ROMAN_RE = re.compile(r"^(?:II|III|IV|VI|VII|VIII|IX|XI|XII|XIII|XIV|XV|XVI|XVII|XVIII|XIX|XX|XXI|XXII)$")


@rule("regnal-numeral", FIX, "Numbers and Dates",
      "a Roman numeral of two or more letters directly after a name: NUM (`Henry II`)", words=None)
def regnal_numeral(s, res):
    out = []
    for i in range(1, s.n):
        if ROMAN_RE.match(s.f[i]) and s.t[i - 1] == "PROPN":
            s.change(out, i, "NUM")
    return out


@rule("big-number", FIX, "Numbers and Dates",
      "`million`, `billion`, `trillion`, `thousand` after a number: NUM; `some` before a number: DET",
      words={"million", "billion", "trillion", "thousand", "some"})
def big_number(s, res):
    out = []
    for i in s.where("million", "billion", "trillion", "thousand"):
        if s.tag(i - 1) == "NUM" and s.t[i] in ("NOUN", "ADJ"):
            s.change(out, i, "NUM")
    for i in s.where("some"):
        if s.tag(i + 1) == "NUM" and s.t[i] == "ADV":
            s.change(out, i, "DET")
    return out


@rule("as-possible", FIX, "Multiword Prepositions", "`as X as possible`: the second `as` SCONJ", words={"possible"})
def as_possible(s, res):
    out = []
    for i in s.where("possible"):
        if s.word(i - 1) == "as" and s.word(i - 3) == "as" and s.t[i - 1] == "ADP":
            s.change(out, i - 1, "SCONJ")
    return out


WAITING_VERBS = frozenset(stem + ending for stem in ("wait", "ask", "call", "hope", "arrange", "long", "wish")
                          for ending in ("", "s", "ed", "ing", "d", "es"))
FOR_NOUNS = frozenset("way ways time place room".split())
INFINITIVE_NOUNS = frozenset(("right rights ability chance chances need desire opportunity power freedom decision willingness "
                              "effort efforts attempt attempts plan plans permission courage obligation duty capacity tendency "
                              "urge reason reasons instance example millennia years months days weeks hours money tools something "
                              "anything nothing way ways time times").split())


def for_subject_to(s, i):
    """(subject_index, to_index) when `for` at i is followed by [DET] subject `to` and a verb, else None."""
    if s.w[i] != "for" or s.t[i] != "ADP":
        return None
    j = i + 1
    if s.tag(j) == "DET":
        j += 1
    if s.tag(j) in ("NOUN", "PRON", "PROPN") and s.word(j + 1) == "to" and s.tag(j + 1) == "PART" \
            and s.tag(j + 2) in ("VERB", "AUX"):
        return j, j + 1
    return None


def for_subject_whitelisted(s, i, subject):
    """True when the `for` + subject + `to` pattern at i is a sure SCONJ.

    Sure means: after an adjective, after `too`/`enough`/`so`, after `way`/`time`/`place`/`room`, or after a verb of waiting,
    asking, calling or hoping; and the subject is not a noun that takes its own infinitive (`the right to marry`).
    """
    if s.word(subject) in INFINITIVE_NOUNS:
        return False
    previous = s.word(i - 1)
    if s.tag(i - 1) == "ADJ" and previous not in QUANT_ADJ:
        return True
    if s.tag(i - 1) == "ADV" and previous in ("too", "enough", "so"):
        return True
    return previous in FOR_NOUNS or (previous in WAITING_VERBS and s.tag(i - 1) == "VERB")


@rule("for-subject-to", FIX, "Multiword Prepositions",
      "`for` + subject + `to` + verb after an adjective, `way`/`time`/`place`/`room` or a verb of waiting, asking, calling, "
      "hoping: `for` SCONJ", words={"for"})
def for_subject_to_rule(s, res):
    out = []
    for i in s.where("for"):
        found = for_subject_to(s, i)
        if found and for_subject_whitelisted(s, i, found[0]):
            s.change(out, i, "SCONJ")
    return out


@rule("get-passive", FIX, "The get-passive",
      "`get` + a past participle that asserts an event (`got fired`, `get married`): `get` AUX "
      "(not before a state participle such as `used`)", words=GET_FORMS)
def get_passive(s, res):
    out = []
    for i in s.where(*GET_FORMS):
        j = i + 1
        if s.t[i] != "VERB" or s.tag(j) != "VERB" or not is_participle(s.w[j]) or s.w[j] in GET_STATE:
            continue
        if s.source == "masc" and s.penn(j) != "VBN":
            continue
        s.change(out, i, "AUX")
    return out


@rule("be-copula-gradable", FIX, "Prenominal Modifiers and Predicate Participles",
      "`be` + a participle tagged VERB right after a degree word (`was very confused`): participle ADJ, `be` VERB (test 2)",
      words=None)
def be_copula_gradable(s, res):
    out = []
    for j, k, kind, degree, agent in _participle_contexts(s):
        near = s.word(j - 1)
        if kind != "be" or agent or s.t[j] != "VERB" or near not in DEGREE_NEAR or not (s.tag(j - 1) == "ADV" or near in DEGREE_FLIP):
            continue
        if near in DEGREE_WEAK and s.w[j] not in GRADABLE_SEED:
            continue  # `so discovered`, `rather hinted than told`, `really engaged at Camp David`: not a degree word
        s.change(out, j, "ADJ")
        s.change(out, k, "VERB")
    return out


@rule("link-participle", FIX, "Prenominal Modifiers and Predicate Participles",
      "after `become, feel, look, remain, seem, sound`: a participle tagged VERB without `by`/`from` agent is ADJ (test 4)",
      words=None)
def link_participle(s, res):
    out = []
    for j, k, kind, degree, agent in _participle_contexts(s):
        if kind == "link" and not agent and s.t[j] == "VERB":
            s.change(out, j, "ADJ")
    return out


CLOCK_WORDS = frozenset(("a.m.", "p.m.", "am", "pm", "a.m", "p.m"))


@rule("clock-suffix", FIX, "Names and Labels", "`a.m.`, `p.m.`, `am`, `pm` after a clock numeral: NOUN", words=CLOCK_WORDS)
def clock_suffix(s, res):
    out = []
    for i in s.where(*CLOCK_WORDS):
        hour = clock_hour(s.f[i - 1]) if i > 0 else None
        if hour is not None and 1 <= hour <= 12 and s.t[i] in ("X", "ADV", "VERB", "PROPN", "ADJ", "AUX", "SYM"):
            s.change(out, i, "NOUN")
    return out


@rule("do-your-best", FIX, "Degree and Quantity Words",
      "`do your best`, `all the best`: `best` ADJ (a superlative that stands for a noun)", words={"best"})
def do_your_best(s, res):
    out = []
    for i in s.where("best"):
        if s.t[i] != "NOUN" or s.tag(i + 1) in ("NOUN", "PROPN", "ADJ"):
            continue
        if (s.word(i - 1) in POSSESSIVES and s.word(i - 2) in ("do", "does", "did", "doing", "done", "try", "tries", "tried", "trying")) \
                or (s.word(i - 1) == "the" and s.word(i - 2) == "all"):
            s.change(out, i, "ADJ")
    return out


FISCAL_RE = re.compile(r"^FY\d{2,4}$")


@rule("fy-token", FIX, "Names and Labels", "a fiscal-year token (`FY2020`) is a NOUN", words=None)
def fy_token(s, res):
    out = []
    for i in range(s.n):
        if FISCAL_RE.match(s.f[i]):
            s.change(out, i, "NOUN")
    return out


@rule("negation-part", FIX, "Annotation Conventions", "`not`, `n't`: PART", words={"not", "n't"})
def negation_part(s, res):
    out = []
    for i in s.where("not", "n't"):
        s.change(out, i, "PART")
    return out


GERUND_NOT = frozenset("bring string spring sting swing cling fling sling wring thing king ring sing wing".split())


def is_gerund(s, i):
    """A verb token in -ing that is a gerund or participle (not `bring`, `string`, ...)."""
    word = s.word(i)
    return s.tag(i) in ("VERB", "AUX") and word.endswith("ing") and len(word) > 4 and word not in GERUND_NOT


CONTROL_BEFORE_TO = frozenset(("forget forgets forgot forgotten hope hopes hoped hoping want wants wanted need needs needed try tries "
                               "tried plan plans planned decide decides decided").split())


@rule("to-not-infinitive", FIX, "Annotation Conventions",
      "`to` tagged PART before a determiner, pronoun, number or gerund is the preposition: ADP (not after a verb that takes the "
      "infinitive such as `hopes to one day become`, not before `one` or a determiner and a bare verb)", words={"to"})
def to_not_infinitive(s, res):
    out = []
    for i in s.where("to"):
        if s.t[i] != "PART" or s.word(i - 1) in CONTROL_BEFORE_TO or s.word(i + 1) == "one":
            continue
        if s.tag(i + 1) == "DET" and s.tag(i + 2) == "VERB" and not is_gerund(s, i + 2):
            continue  # `used to a make a ...`: a slip of the keyboard, not a noun phrase
        if s.tag(i + 1) in ("DET", "PRON", "NUM") or is_gerund(s, i + 1):
            s.change(out, i, "ADP")
    return out


MODALS = frozenset("can could may might must shall should will would".split())


@rule("modal-aux", FIX, "Annotation Conventions",
      "a modal (`can, could, may, might, must, shall, should, will, would`) tagged VERB before a verb, `not` or an adverb: AUX "
      "(the stem of `can not`)", words=MODALS)
def modal_aux(s, res):
    out = []
    for i in s.where(*MODALS):
        if s.t[i] == "VERB" and s.f[i][0].islower() and s.tag(i + 1) in ("VERB", "AUX", "PART", "ADV"):
            s.change(out, i, "AUX")
    return out


@rule("possessive-det", FIX, "Annotation Conventions",
      "`my, your, his, its, our, their, whose` directly before a noun, adjective or number: DET; `her` the same after a "
      "preposition, conjunction or punctuation (after a verb it may be the indirect object)",
      words={"my", "your", "his", "its", "our", "their", "whose", "her"})
def possessive_det(s, res):
    out = []
    for i in s.where("my", "your", "his", "its", "our", "their", "whose"):
        if s.t[i] == "PRON" and s.tag(i + 1) in ("NOUN", "ADJ", "NUM", "PROPN"):
            s.change(out, i, "DET")
    for i in s.where("her"):
        if s.t[i] == "PRON" and s.tag(i - 1) in ("ADP", "CCONJ", "SCONJ", "PUNCT", "") \
                and (s.tag(i + 1) in ("NOUN", "NUM", "PROPN") or (s.tag(i + 1) == "ADJ" and s.word(i + 1) == "own")):
            s.change(out, i, "DET")
    return out


# ---------------------------------------------------------------- check rules (violations without an automatic fix)

@rule("be-participle-adj-not-gradable", CHECK, "Prenominal Modifiers and Predicate Participles",
      "agentless `be` + a participle tagged ADJ without a degree word that is not known to be gradable: passive VERB or "
      "gradable ADJ? (test 2 against test 4; the heuristic is not precise enough to correct)", words=None)
def be_participle_adj_not_gradable(s, res):
    out = []
    for j, k, kind, degree, agent in _participle_contexts(s):
        if kind == "be" and s.t[j] == "ADJ" and not degree and not agent and s.w[j] not in res.gradable:
            s.change(out, j, None)
    return out


@rule("be-passive-agent", CHECK, "Prenominal Modifiers and Predicate Participles",
      "`be` or a linking verb + a participle tagged ADJ with an overt `by`/`from` agent: VERB unless the participle is gradable "
      "(`fascinated by` is an adjective, `dominated by` a verb)", words=None)
def be_passive_agent(s, res):
    out = []
    for j, k, kind, degree, agent in _participle_contexts(s):
        if agent and not degree and s.t[j] == "ADJ" and s.w[j] not in res.gradable and not s.w[j].startswith("un"):
            s.change(out, j, None)
    return out


@rule("be-aux-passive", CHECK, "Prenominal Modifiers and Predicate Participles",
      "`be` tagged VERB before a participle tagged VERB: AUX when the participle is a passive, or the participle is a "
      "gradable ADJ (the two tags of one construction must agree)", words=None)
def be_aux_passive(s, res):
    out = []
    for j, k, kind, degree, agent in _participle_contexts(s):
        if kind == "be" and not degree and s.t[j] == "VERB" and s.t[k] == "VERB" and s.w[j] not in res.gradable:
            s.change(out, k, None)
    return out


@rule("get-state-participle", CHECK, "The get-passive",
      "`get` + a state participle (`get used to`) tagged AUX + VERB instead of VERB + ADJ", words=GET_FORMS)
def get_state_participle(s, res):
    out = []
    for i in s.where(*GET_FORMS):
        j = i + 1
        if s.tag(j) == "VERB" and s.w[j] in GET_STATE and is_participle(s.w[j]) and s.t[i] == "AUX":
            s.change(out, j, None)
    return out


@rule("out-of-phrasal", CHECK, "Multiword Prepositions",
      "`run out of`: `out` is a phrasal-verb particle (ADV), not ADP", words={"out"})
def out_of_phrasal(s, res):
    out = []
    for i in s.where("out"):
        if s.word(i + 1) == "of" and s.word(i - 1) in ("run", "runs", "ran", "running") and s.t[i] == "ADP":
            s.change(out, i, None)
    return out


@rule("infinitive-to", CHECK, "Annotation Conventions",
      "`to` is PART only as the infinitive marker: not ADP before a bare verb, not PART before a noun", words={"to"})
def infinitive_to(s, res):
    out = []
    for i in s.where("to"):
        nxt, tag = s.tag(i + 1), s.t[i]
        if tag == "ADP" and nxt == "VERB" and not is_gerund(s, i + 1):
            s.change(out, i, None)
        elif tag == "PART" and nxt in ("NOUN", "PROPN"):
            s.change(out, i, None)
    return out


@rule("modal-aux-other", CHECK, "Annotation Conventions",
      "a modal tagged ADV, PART, ADJ, ADP, SCONJ or X before a verb; `ca`, `wo`, `sha` before `n't` not AUX",
      words=MODALS | {"ca", "wo", "sha"})
def modal_aux_other(s, res):
    out = []
    for i in s.where(*MODALS):
        if s.t[i] in ("ADV", "PART", "ADJ", "ADP", "SCONJ", "X") and s.f[i][0].islower() \
                and s.tag(i + 1) in ("VERB", "AUX", "PART", "ADV", "PRON"):
            s.change(out, i, None)
    for i in s.where("ca", "wo", "sha"):
        if s.word(i + 1) == "n't" and s.t[i] != "AUX":
            s.change(out, i, None)
    return out


@rule("speaker-label", CHECK, "Numbers and Dates", "`A:` and `B:` at the start of a sentence label a speaker: PROPN", words=None)
def speaker_label(s, res):
    out = []
    if s.n > 1 and s.f[0] in ("A", "B") and s.f[1] == ":" and s.t[0] != "PROPN":
        s.change(out, 0, None)
    return out


NO_ALL_COMPARATIVES = frozenset(("more less better worse longer further farther bigger smaller higher lower greater easier harder "
                                 "faster different wiser safer older younger happier").split())


@rule("no-all-degree", CHECK, "Annotation Conventions",
      "`no`/`all` directly before an adjective or adverb (`no better`, `all alone`) is ADV, not DET", words={"no", "all"})
def no_all_degree(s, res):
    out = []
    for i in s.where("no"):
        if s.t[i] in ("DET", "PRON") and s.word(i + 1) in NO_ALL_COMPARATIVES and s.tag(i + 1) in ("ADJ", "ADV") \
                and s.tag(i + 2) not in ("NOUN", "PROPN", "ADJ"):
            s.change(out, i, None)
    for i in s.where("all"):
        if s.t[i] in ("DET", "PRON", "ADJ") and s.word(i + 1) in ("alone", "right", "together", "wrong", "ready", "set") \
                and s.tag(i + 1) in ("ADJ", "ADV"):
            s.change(out, i, None)
    return out


@rule("language-name-token", CHECK, "Names and Labels", "a language name is PROPN, ADJ or NOUN, never ADV, VERB or X", words=None)
def language_name_token(s, res):
    out = []
    for i in range(s.n):
        if s.f[i] in LANGUAGES and s.t[i] in ("ADV", "VERB", "X", "AUX", "PART"):
            s.change(out, i, None)
    return out


# ---------------------------------------------------------------- evaluation

def evaluate(s, res, tiers=(FIX, CHECK), only=None):
    """All proposals and violations of one sentence as `Proposal` tuples: rules in priority order, tokens in order.

    `tiers` selects fix and/or check rules, `only` an optional set of rule ids. Masked tokens never appear.
    """
    proposals = []
    for item in RULES:
        if item.tier not in tiers or (only is not None and item.id not in only):
            continue
        if item.words is not None and item.words.isdisjoint(s.wordset):
            continue
        for index, new_tag in sorted(set(item.detect(s, res)), key=lambda pair: (pair[0], pair[1] or "")):
            proposals.append(Proposal(index, new_tag, item.id, item.tier))
    return proposals


def resolve_fixes(proposals):
    """Reduce the fix proposals of a sentence to one per token: returns (accepted {index: Proposal}, conflicts [(a, b)]).

    Rules are in priority order; a later proposal with the same tag for a token is a harmless repeat, one with a
    different tag is a conflict that the generator refuses (a guard, not a silent priority, must resolve it).
    """
    accepted, conflicts = {}, []
    for proposal in proposals:
        if proposal.tier != FIX:
            continue
        first = accepted.get(proposal.index)
        if first is None:
            accepted[proposal.index] = proposal
        elif first.new_tag != proposal.new_tag:
            conflicts.append((first, proposal))
    return accepted, conflicts

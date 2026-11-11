"""The divergence of the labeling practice of UD English-EWT from the convention "Capitalized Defined Terms": a frozen detector and two counters.

`data/CONVENTIONS.md` ("Capitalized Defined Terms") labels a capitalized run of ordinary English words in the middle of a sentence NOUN (or ADJ
for an adjectival modifier) when it names a kind, mode, setting or document type, and PROPN only for one particular entity. UD English practice
is not uniform for such runs. This tool COUNTS how often the tags of a corpus conflict with the convention. Nothing is relabeled.

THE DETECTOR (`defined_term_conflicts`; pure, frozen by its commit, any change after a result is a new, disclosed version of the rule)

1. A run is a Title Case run of `technical_frames.title_runs` (1 to 4 tokens `[A-Z][a-z]+`; not the first token, not after `:`, a quote or
   sentence-final punctuation; not before a number or a one-letter label). A sentence with a capitalized function word after its first token
   has no run. A run is looked at only when every word is `ordinary` (lexicon NOUN or ADJ and not only PROPN; no function word, number word,
   month or day name or title of `convention_rules.TITLES`).
2. A run whose last word is an organization or place head (`technical_frames.ORGANIZATION_HEADS`) is `org-like` and is reported separately; it is
   never a conflict. Another run is class A (cue-based) when its cue tier (`technical_frames.run_tier`) is T1 (a lower-case kind noun follows),
   T2 (the last word names a document type) or T3 (a verb of `LEFT_CUES` before it); class B (broad) when the tier is T4 and the run has two or
   more words. Single-word runs without a cue are not counted.
3. The tag of the LAST word of the run decides: PROPN is a conflict with the convention, NOUN or ADJ an agreement, anything else `other`.

THE COUNTERS

`count-training` counts per training source (open train, MASC, silver, imperative, compound, compound2, headings, headings2) and writes a
seeded sample of at most 25 conflicting runs per source and class as ids only (`source`, `class`, `sent_id`, `token_index`; no text) to
`data/technical/term-conflict-ids.tsv`.

`count-held-out` is the ONLY code path of this tool that opens a held-out file (`data/open/dev.tsv`, `data/open/test.tsv` and the git-ignored
UD English-EWT dev and test files under `tools/data/cache/ud-ewt/` when present). It prints INTEGERS ONLY, per set: the sentences and, per class
(`a`, `b`, `org`), the runs and the tags PROPN, NOUN, ADJ and other of the last word. It has no list option and writes no file; no sentence, id,
token, form or context is ever printed, stored or written (`format_counts` takes the fixed label of the set and integers only; a test scans
the source for that). It is run once, after the commit of the detector.
"""
import argparse
import collections
import os
import sys

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import common
import fetch_ud_ewt
import shapes_dev
import technical_frames as tf

SEED = 20261110
SAMPLE_PER_CLASS = 25
CLASSES = ("a", "b", "org")
TAG_COLUMNS = ("propn", "noun", "adj", "other")
COUNT_KEYS = ("sentences",) + tuple("%s_%s" % (cls, kind) for cls in CLASSES for kind in ("runs",) + TAG_COLUMNS)
TRAINING_SOURCES = (
    ("open", ("open", "train.tsv")), ("masc", ("masc", "masc.tsv")), ("silver", ("silver", "silver.tsv")),
    ("imperative", ("imperative", "imperative.tsv")), ("compound", ("compound", "compound.tsv")),
    ("compound2", ("compound", "compound-2.tsv")), ("headings", ("headings", "headings.tsv")),
    ("headings2", ("headings", "headings-2.tsv")))
HELD_OUT_CORPORA = (("open-dev", ("open", "dev.tsv")), ("open-test", ("open", "test.tsv")))
HELD_OUT_UD = (("ud-ewt-dev", "en_ewt-ud-dev.conllu"), ("ud-ewt-test", "en_ewt-ud-test.conllu"))
IDS_FILE = os.path.join(common.DATA, "technical", "term-conflict-ids.tsv")
IDS_HEADER = ["# columns = source, class, sent_id, token_index",
              "# term-conflict ids: a seeded sample (at most %d per source and class) of the runs whose last word is tagged PROPN although the "
              "convention (CONVENTIONS.md, Capitalized Defined Terms) asks for NOUN or ADJ; ids only, no text; token_index is zero-based" % SAMPLE_PER_CLASS,
              "# rule = tools/data/term_divergence.py, seed %d" % SEED]
Conflict = collections.namedtuple("Conflict", "cls start end judged tag verdict")


def default_lexicon(data_dir=None):
    """The lexicon `{spelling: frozenset of tags}` of `<data_dir>/lexicon/lexicon.tsv` (the repository lexicon by default)."""
    return common.read_lexicon(os.path.join(data_dir or common.DATA, "lexicon", "lexicon.tsv"))


def verdict_of(tag):
    """`conflict` for PROPN, `agreement` for NOUN or ADJ, `other` for every other tag (the tag of the last word of a run)."""
    return "conflict" if tag == "PROPN" else "agreement" if tag in ("NOUN", "ADJ") else "other"


def defined_term_conflicts(sentence, lex=None):
    """[Conflict(cls, start, end, judged, tag, verdict)] of the runs of one sentence (the frozen detector of the module docstring).

    `sentence` is a sequence of (form, upos) pairs; `lex` the lexicon (default: the repository lexicon). `cls` is `a`, `b` or `org`
    (`org` runs never count as conflicts: their verdict is still reported, so a caller can count the PROPN share of organization-like
    runs). Pure and deterministic; no I/O except the default lexicon read."""
    lex = lex if lex is not None else default_lexicon()
    forms = [form for form, _ in sentence]
    if tf.title_cased_sentence(forms):
        return []
    found = []
    for start, end in tf.title_runs(forms):
        words = forms[start:end]
        if not all(tf.ordinary(w, lex) for w in words):
            continue
        if tf.organization_like(words):
            cls = "org"
        else:
            tier = tf.run_tier(forms, start, end)
            cls = "a" if tier != "T4" else "b" if len(words) >= 2 else None
        if cls is None:
            continue
        tag = sentence[end - 1][1]
        found.append(Conflict(cls, start, end, end - 1, tag, verdict_of(tag)))
    return found


def empty_counts():
    """A zero count for every key of COUNT_KEYS."""
    return collections.OrderedDict((key, 0) for key in COUNT_KEYS)


def add_sentence(counts, conflicts):
    """Add the runs of one sentence to `counts` (integers only)."""
    counts["sentences"] += 1
    for item in conflicts:
        counts[item.cls + "_runs"] += 1
        key = "%s_%s" % (item.cls, item.tag.lower() if item.tag.lower() in TAG_COLUMNS[:3] else "other")
        counts[key] += 1


def format_counts(label, counts):
    """`label key=int key=int ...`: the output line of a counter. `label` is a fixed name of a set (never data); every value is an integer."""
    for key, value in counts.items():
        if not isinstance(value, int) or isinstance(value, bool):
            raise TypeError("the counters print integers only")
    return label + " " + " ".join("%s=%d" % (key, value) for key, value in counts.items())


def pairs_of(rows):
    """[(form, upos)] of the token rows of a corpus sentence."""
    return [(row[0], row[1]) for row in rows]


def training_counts(data_dir, lex):
    """{source: (counts, [(cls, sent_id, judged)] of the conflicts)} of the training sources present under `data_dir`."""
    result = collections.OrderedDict()
    for name, parts in TRAINING_SOURCES:
        path = os.path.join(data_dir, *parts)
        if not os.path.exists(path):
            continue
        counts, conflicts = empty_counts(), []
        for sentence in common.read_corpus(path)[1]:
            found = defined_term_conflicts(pairs_of(sentence["rows"]), lex)
            add_sentence(counts, found)
            conflicts += [(item.cls, sentence["sent_id"], item.judged) for item in found if item.verdict == "conflict" and item.cls != "org"]
        result[name] = (counts, conflicts)
    return result


def id_sample(source, conflicts, size=SAMPLE_PER_CLASS, seed=SEED):
    """The seeded sample of the conflicts of one source: at most `size` per class, ordered by `rank_of(seed, "term-conflict", source, sent_id)`."""
    rows = []
    for cls in ("a", "b"):
        own = sorted((c for c in conflicts if c[0] == cls), key=lambda c: (shapes_dev.rank_of(seed, "term-conflict", source, c[1]), c[2]))
        rows += [(source, cls, sent_id, str(judged)) for _, sent_id, judged in own[:size]]
    return rows


def _command_count_training(args):
    lex = default_lexicon(args.data_dir)
    rows = []
    for name, (counts, conflicts) in training_counts(args.data_dir, lex).items():
        print(format_counts(name, counts))
        rows += id_sample(name, conflicts)
    common.write_text(args.out, IDS_HEADER + ["\t".join(row) for row in rows])
    print(format_counts("ids", collections.OrderedDict([("written", len(rows))])))
    return 0


def held_out_counts(data_dir, cache_dir, lex):
    """[(label, counts)] of the held-out sets. THE ONLY FUNCTION THAT OPENS A HELD-OUT FILE: a sentence is a local list of (form, upos) pairs
    that is counted at once and dropped; no id, text or token leaves this function, only the integers of `counts`."""
    results = []
    for label, parts in HELD_OUT_CORPORA:
        path = os.path.join(data_dir, *parts)
        counts = empty_counts()
        if os.path.exists(path):
            for sentence in common.read_corpus(path)[1]:
                add_sentence(counts, defined_term_conflicts(pairs_of(sentence["rows"]), lex))
        results.append((label, counts, int(os.path.exists(path))))
    for label, name in HELD_OUT_UD:
        path = os.path.join(cache_dir, "ud-ewt", name)
        counts = empty_counts()
        if os.path.exists(path):
            for sentence in fetch_ud_ewt.read_conllu(path):
                add_sentence(counts, defined_term_conflicts([(w["form"], w["upos"]) for w in sentence["words"]], lex))
        results.append((label, counts, int(os.path.exists(path))))
    return results


def _command_count_held_out(args):
    lex = default_lexicon(args.data_dir)
    for label, counts, present in held_out_counts(args.data_dir, args.cache_dir, lex):
        print(format_counts(label, collections.OrderedDict([("present", present)] + list(counts.items()))))
    return 0


def main(argv=None):
    """The command line: `count-training` and `count-held-out` (there is no command that lists a sentence, an id or a token of a held-out set)."""
    parser = argparse.ArgumentParser(description=__doc__.split("\n")[0])
    parser.add_argument("--data-dir", default=common.DATA)
    sub = parser.add_subparsers(dest="command", required=True)
    training = sub.add_parser("count-training", help="counts per training source and the conflict-id sample (ids only)")
    training.add_argument("--out", default=IDS_FILE)
    training.set_defaults(handler=_command_count_training)
    held_out = sub.add_parser("count-held-out", help="integers only, per held-out set")
    held_out.add_argument("--cache-dir", default=common.CACHE)
    held_out.set_defaults(handler=_command_count_held_out)
    args = parser.parse_args(argv)
    return args.handler(args)


if __name__ == "__main__":
    sys.exit(main())

"""Convention linter: report where the corpora break the rules of data/CONVENTIONS.md (Python 3, stdlib only).

  python tools/data/check_conventions.py                      # report only: counts per rule and source, exit code 0
  python tools/data/check_conventions.py --enforce            # exit code 1 when any violation is left
  python tools/data/check_conventions.py --apply              # lint the corpora as they would be after the staged corrections
  python tools/data/check_conventions.py --list RULE --limit 20 --source masc
  python tools/data/check_conventions.py --tsv violations.tsv # every violation, one row each

The rules live in convention_rules.py and are shared with the generator of the staged corrections (make_corrections.py), so a
`fix` rule is reported here exactly while its proposal has not been applied, and a `check` rule is reported until a correction
row or an exceptions.tsv entry resolves it. The STE challenge set is evaluation-only and never corrected: it is linted and
shown in its own column, and it does not count for the exit code of `--enforce`. The imperative, compound and headings training
sources (data/imperative/imperative.tsv, data/compound/compound.tsv, data/headings/headings.tsv) and the follow-up sources `compound2` and
`headings2` (data/compound/compound-2.tsv, data/headings/headings-2.tsv) and the four technical-text sources `deverbal`, `terms`, `steps`, `clauses`
(data/technical/<name>.tsv, optional files; their column appears in the report only when a violation of theirs exists) are linted and shown in their
own columns too, but they are never corrected, they do not feed the corpus resources of the participle rules, and they DO count
for `--enforce` (their fix tier must stay at zero).
"""
import argparse
import collections
import os
import sys

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import common
import convention_rules
import corrections

Violation = collections.namedtuple("Violation", "rule tier source split source_id sent_id index token tag new_tag context")

COLUMN_ORDER = (("open", "train"), ("open", "dev"), ("open", "test"), ("masc", "-"), ("silver", "-"), ("ste", "-"), ("imperative", "-"),
                ("compound", "-"), ("headings", "-"), ("compound2", "-"), ("headings2", "-"))
# The four technical-text sources (technical_data.py; data/technical/): linted like the other training sources, shown in their own column only when they have a
# violation (so the report of a data folder without them is unchanged), and counted by `--enforce`.
TECHNICAL_SOURCES = ("deverbal", "terms", "steps", "clauses")
TECHNICAL_COLUMNS = tuple((name, "-") for name in TECHNICAL_SOURCES)
RESOURCE_EXEMPT = ("ste", "imperative", "compound", "headings", "compound2", "headings2") + TECHNICAL_SOURCES   # linted only: these sources do not feed the corpus resources of the rules


def load_sentences(data_dir=None, apply_overlay=False):
    """(corpora, sentences): every corpus (STE included) and a `Sentence` for each of its sentences.

    With `apply_overlay` the staged corrections are validated and applied to the loaded corpora in memory first (nothing is
    written), which is how the corpora look after the corrections change is merged. A table that does not match raises ValueError.
    """
    corpora = corrections.load_corpora(data_dir, lint_only=True)
    if apply_overlay:
        rows = corrections.load_overlay(data_dir)["corrections"]
        errors = corrections.row_errors(rows)
        if not errors:
            errors, state = corrections.validate_against_corpora(rows, corpora)
        if errors:
            raise ValueError("the staged corrections do not match the corpora: " + errors[0])
        if state == "pending":
            corrections.apply_in_memory(corpora, rows)
    sentences = []
    for (source, split), corpus in corpora.items():
        sentences.extend(convention_rules.Sentence(source, split, sentence) for sentence in corpus.sentences)
    return corpora, sentences


def lint(sentences, resources, exceptions=frozenset(), tiers=(convention_rules.FIX, convention_rules.CHECK), only=None, keep=None):
    """Violations of the sentences as a list of `Violation` and the number suppressed by exceptions.

    `exceptions` is a set of (source, split, sent_id, token_index, rule); `keep` an optional predicate on a sentence that
    selects what is reported (the resources always come from all corpora).
    """
    found, suppressed = [], 0
    for s in sentences:
        if keep is not None and not keep(s):
            continue
        for proposal in convention_rules.evaluate(s, resources, tiers, only):
            if (s.source, s.split, s.sent_id, proposal.index, proposal.rule) in exceptions:
                suppressed += 1
                continue
            found.append(Violation(proposal.rule, proposal.tier, s.source, s.split, s.source_id, s.sent_id, proposal.index,
                                   s.f[proposal.index], s.t[proposal.index], proposal.new_tag or "", s.window(proposal.index, 1, 8)))
    return found, suppressed


def load_exceptions(data_dir=None):
    """Set of (source, split, sent_id, token_index, rule) of the exceptions table (rows reviewed and kept as they are)."""
    rows = corrections.load_overlay(data_dir)["exceptions"]
    bad = [row for row in rows if not corrections.has_valid_index(row)]
    if bad:
        raise ValueError("exception row with a malformed token index: %s#%s" % (bad[0].sent_id, bad[0].token_index))
    return frozenset((row.source, row.split, row.sent_id, int(row.token_index), row.rule) for row in rows)


def superseded_hits(data_dir=None):
    """Set of (source, split, sent_id, token_index, rule) of the mechanical rows that an adjudicated row replaces.

    The adjudication outranks the rule for these tokens (see corrections.combine_tables), so once the corrections are applied the
    rule that wrote the replaced row would fire on the token again by construction; that hit is not a violation.
    """
    rows = corrections.load_overlay(data_dir)["superseded"]
    return frozenset((row.source, row.split, row.sent_id, int(row.token_index), row.provenance[len("rule:"):]) for row in rows)


def kept_exceptions(data_dir=None, apply_overlay=False):
    """The exceptions the linter honors: the exceptions table and, when the corrections are applied, the replaced rule hits."""
    found = load_exceptions(data_dir)
    return found | superseded_hits(data_dir) if apply_overlay else found


def counts_table(violations):
    """{(rule): {(source, split): n}} counts."""
    table = collections.defaultdict(collections.Counter)
    for item in violations:
        table[item.rule][(item.source, item.split)] += 1
    return table


def format_report(violations, suppressed=0):
    """Text report: one line per rule with the counts per source and split, then the totals."""
    table = counts_table(violations)
    tiers = {item.id: item.tier for item in convention_rules.RULES}
    present = {key for counts in table.values() for key in counts}
    columns = COLUMN_ORDER + tuple(key for key in TECHNICAL_COLUMNS if key in present)
    heads = ["%s/%s" % key if key[1] != "-" else key[0] for key in columns]
    lines = ["%-32s %-5s " % ("rule", "tier") + " ".join("%10s" % head for head in heads) + " %8s" % "total"]
    grand = collections.Counter()
    for item in convention_rules.RULES:
        counts = table.get(item.id, collections.Counter())
        if not counts:
            continue
        row = [counts[key] for key in columns]
        grand.update(counts)
        lines.append("%-32s %-5s " % (item.id, tiers[item.id]) + " ".join("%10d" % value for value in row) + " %8d" % sum(row))
    lines.append("%-38s " % "total" + " ".join("%10d" % grand[key] for key in columns) + " %8d" % sum(grand.values()))
    if suppressed:
        lines.append("suppressed by exceptions: %d" % suppressed)
    return lines


def countable(violations):
    """Violations that count for `--enforce`: everything except the evaluation-only STE set."""
    return [item for item in violations if item.source != "ste"]


def write_tsv(path, violations):
    """Write every violation as a TSV file (columns in the header)."""
    lines = ["# columns = rule, tier, source, split, source_id, sent_id, token_index, token, tag, proposed_tag, context"]
    for item in violations:
        lines.append("\t".join((item.rule, item.tier, item.source, item.split, item.source_id, item.sent_id, str(item.index),
                                item.token, item.tag, item.new_tag, item.context)))
    common.write_text(path, lines)


def main(argv=None):
    parser = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    parser.add_argument("--data-dir", default=None, help="data folder (default: the repository's data/)")
    parser.add_argument("--enforce", action="store_true", help="exit code 1 when a violation is left (STE not counted)")
    parser.add_argument("--apply", action="store_true", help="lint the corpora as they would be after the staged corrections")
    parser.add_argument("--source", choices=("open", "masc", "silver", "ste", "imperative", "compound", "headings", "compound2", "headings2") + TECHNICAL_SOURCES,
                        default=None, help="report only this source")
    parser.add_argument("--split", choices=("train", "dev", "test"), default=None, help="report only this split (open)")
    parser.add_argument("--tier", choices=("fix", "check", "all"), default="all")
    parser.add_argument("--rule", action="append", default=None, help="only this rule id (repeatable)")
    parser.add_argument("--list", dest="list_rule", default=None, metavar="RULE", help="print the violations of a rule in context")
    parser.add_argument("--limit", type=int, default=20, help="violations printed by --list")
    parser.add_argument("--tsv", default=None, help="write every violation to this file")
    args = parser.parse_args(argv)

    try:
        _, sentences = load_sentences(args.data_dir, args.apply)
    except ValueError as error:
        parser.error(str(error))
    resources = convention_rules.build_resources(s for s in sentences if s.source not in RESOURCE_EXEMPT)
    tiers = (convention_rules.FIX, convention_rules.CHECK) if args.tier == "all" else (args.tier,)
    only = set(args.rule) if args.rule else ({args.list_rule} if args.list_rule else None)
    unknown = (only or set()) - convention_rules.rule_ids()
    if unknown:
        parser.error("unknown rule id(s): " + ", ".join(sorted(unknown)))

    def keep(s):
        return (args.source is None or s.source == args.source) and (args.split is None or s.split == args.split)

    violations, suppressed = lint(sentences, resources, kept_exceptions(args.data_dir, args.apply), tiers, only, keep)
    if args.list_rule:
        shown = [item for item in violations if item.rule == args.list_rule]
        for item in shown[:args.limit]:
            print("%s/%s %s#%d %s %s>%s | %s" % (item.source, item.split, item.sent_id, item.index, item.token, item.tag,
                                                 item.new_tag or "?", item.context))
        print("%d violations of %s" % (len(shown), args.list_rule))
    else:
        for line in format_report(violations, suppressed):
            print(line)
    if args.tsv:
        write_tsv(args.tsv, violations)
    left = countable(violations)
    if args.enforce and left:
        print("FAILED: %d convention violations" % len(left))
        return 1
    return 0


if __name__ == "__main__":
    sys.exit(main())

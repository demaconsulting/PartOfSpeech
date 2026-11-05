"""Generate the staged corpus corrections and judgment candidates (Python 3, standard library only).

  python tools/data/make_corrections.py                 # write data/corrections/{mechanical,candidates,exceptions}.tsv
  python tools/data/make_corrections.py --check         # regenerate in memory and compare with the committed files (exit code 1)
  python tools/data/make_corrections.py sample --n 50 --seed 20261104 [--per-rule 4] [--out FILE]
                                                         # draw correction rows with context for the hand-checked precision gate
  python tools/data/make_corrections.py fixed-point      # apply the rows in memory: the rules must find nothing left to fix
  python tools/data/make_corrections.py catalog          # the rule catalog as Markdown tables

The generator reads the corpora of open (train, dev, test), MASC and silver exactly as committed, runs the mechanical (`fix`)
rules of convention_rules.py and writes one correction row per proposal to mechanical.tsv (rows keep their status cell; MASC
decisions use the Penn tag of the `source_tag` column where it disambiguates). The cases that need judgment (the candidate
detectors of convention_candidates.py and every `check` rule violation) go to candidates.tsv for the adjudication. exceptions.tsv is
written header-only: it is where a reviewed "keep as is" decision is recorded later.

The output is a pure function of the committed corpora, the lexicon (for the informational count only) and the rules, in
deterministic order, so `--check` is exact. The tables describe the corpora as committed *before* the corrections are applied (the
header lists the SHA-256 of each corpus file the rows were derived from); `--check` is meaningful only until then.
"""
import argparse
import collections
import os
import random
import sys

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import common
import convention_candidates
import convention_rules
import corrections

MAX_CANDIDATES_BYTES = 5 * 1024 * 1024
LICENSE_LINE = ("# license = CC0-1.0 for the labels, addresses and rules; the token forms and context excerpts of MASC rows are MASC "
                "text (CC BY 3.0 US, see data/masc/ATTRIBUTION.md)")
CORPUS_LINE = "# corpus = DemaConsulting.PartOfSpeech staged corpus corrections: %s"
KINDS = {
    "corrections": ("mechanical.tsv", "mechanical rule rows (tier fix of tools/data/convention_rules.py), not yet applied"),
    "candidates": ("candidates.tsv", "judgment candidates for the adjudication, never changes"),
    "exceptions": ("exceptions.tsv", "reviewed cases that stay as they are (empty until the adjudication)"),
}


class Result:
    """Everything one generator run produces, kept together for the writer, the checker and the tests."""

    def __init__(self):
        self.rows = []               # correction rows (mechanical)
        self.candidates = []         # candidate rows
        self.conflicts = []          # (sent_id, token, first proposal, second proposal)
        self.checks = collections.Counter()  # check-tier violations per rule, informational
        self.sentences = 0
        self.tokens = 0


def load_sentences(data_dir=None):
    """(corpora, sentences): the correctable corpora and a `Sentence` per sentence (STE is not part of the overlay)."""
    corpora = corrections.load_corpora(data_dir)
    sentences = [convention_rules.Sentence(source, split, sentence)
                 for (source, split), corpus in corpora.items() for sentence in corpus.sentences]
    return corpora, sentences


def build(sentences, resources, exceptions=frozenset()):
    """Run all rules and candidate detectors over the sentences: returns a `Result`.

    A token has at most one correction row (priority order; two rules that propose different tags are a conflict, which the
    caller must treat as an error). Candidates never repeat a token that has a correction row; a violation of a `check` rule becomes a
    candidate whose `candidate_rule` is the rule id, unless an exception covers it.
    """
    result = Result()
    for s in sentences:
        result.sentences += 1
        result.tokens += s.n
        proposals = convention_rules.evaluate(s, resources)
        accepted, conflicts = convention_rules.resolve_fixes(proposals)
        for first, second in conflicts:
            result.conflicts.append((s.sent_id, s.f[first.index], first, second))
        for index in sorted(accepted):
            proposal = accepted[index]
            result.rows.append(corrections.Correction(s.source, s.split, s.source_id, s.sent_id, str(index), s.f[index], s.t[index],
                                                      proposal.new_tag, s.st[index], s.st[index], "rule:" + proposal.rule))
        seen = set()

        def add(rule_id, index, span, hint):
            key = (index, rule_id)
            if index in accepted or key in seen or (s.source, s.split, s.sent_id, index, rule_id) in exceptions:
                return
            seen.add(key)
            result.candidates.append(corrections.ROW_TYPES["candidates"](
                s.source, s.split, s.source_id, s.sent_id, str(index), str(span), s.f[index], s.t[index], s.st[index], s.q[index],
                rule_id, hint, s.window(index, span, 6)))

        for proposal in proposals:
            if proposal.tier == convention_rules.CHECK:
                result.checks[proposal.rule] += 1
                add(proposal.rule, proposal.index, 1, "check-rule")
        for rule_id, index, span, hint in convention_candidates.evaluate(s, resources):
            add(rule_id, index, span, hint)
    result.rows.sort(key=corrections.sort_key)
    result.candidates.sort(key=lambda row: corrections.sort_key(row) + (row.candidate_rule,))
    return result


def input_lines(corpora, data_dir=None):
    """Header lines naming the corpus files the rows were derived from, with their SHA-256."""
    base = data_dir or common.DATA
    lines = []
    for key in sorted(corpora):
        relative = corrections.CORPUS_FILES[key]
        lines.append("# input = %s sha256 %s" % (relative, common.sha256_file(os.path.join(base, *relative.split("/")))))
    return lines


def table_lines(kind, rows, corpora, data_dir=None):
    """The lines of one table file: header, columns line, sorted rows."""
    name, summary = KINDS[kind]
    header = [CORPUS_LINE % summary, LICENSE_LINE, "# kind = " + kind,
              "# columns = " + ", ".join(corrections.COLUMNS[kind]),
              "# generator = tools/data/make_corrections.py (rule set %s)" % convention_rules.RULE_SET_VERSION]
    header += input_lines(corpora, data_dir)
    return corrections.format_table(kind, header, rows)


def generate_all(data_dir=None):
    """{file name: lines} of the three tables, and the `Result`; raises RuntimeError on conflicts or an oversized table."""
    corpora, sentences = load_sentences(data_dir)
    resources = convention_rules.build_resources(sentences)
    from check_conventions import load_exceptions
    result = build(sentences, resources, load_exceptions(data_dir))
    if result.conflicts:
        raise RuntimeError("%d rule conflicts, first: %s %s %s" % (len(result.conflicts), result.conflicts[0][0],
                                                                  result.conflicts[0][2], result.conflicts[0][3]))
    files = {
        KINDS["corrections"][0]: table_lines("corrections", result.rows, corpora, data_dir),
        KINDS["candidates"][0]: table_lines("candidates", result.candidates, corpora, data_dir),
        KINDS["exceptions"][0]: table_lines("exceptions", [], corpora, data_dir),
    }
    size = sum(len(line.encode("utf-8")) + 1 for line in files[KINDS["candidates"][0]])
    if size > MAX_CANDIDATES_BYTES:
        by_rule = collections.Counter(row.candidate_rule for row in result.candidates)
        raise RuntimeError("candidates.tsv would be %d bytes (limit %d); largest class: %s" % (size, MAX_CANDIDATES_BYTES, by_rule.most_common(1)))
    return files, result, corpora, sentences, resources


def count_table(rows, rule_of):
    """{rule: Counter((source, split))} of rows."""
    table = collections.defaultdict(collections.Counter)
    for row in rows:
        table[rule_of(row)][(row.source, row.split)] += 1
    return table


def print_counts(title, table, order, keys=(("open", "train"), ("open", "dev"), ("open", "test"), ("masc", "-"), ("silver", "-"))):
    """Print a rule x source/split table in the order of `order` (rule ids)."""
    print(title)
    heads = ["%s/%s" % key if key[1] != "-" else key[0] for key in keys]
    print("%-32s " % "rule" + " ".join("%10s" % head for head in heads) + " %8s" % "total")
    totals = collections.Counter()
    for rule_id in order:
        counts = table.get(rule_id)
        if not counts:
            continue
        totals.update(counts)
        print("%-32s " % rule_id + " ".join("%10d" % counts[key] for key in keys) + " %8d" % sum(counts[key] for key in keys))
    print("%-32s " % "total" + " ".join("%10d" % totals[key] for key in keys) + " %8d" % sum(totals[key] for key in keys))


def lexicon_rejects(rows, data_dir=None):
    """Correction rows whose new tag the shipped lexicon does not allow for the token (informational, the lexicon is not changed here)."""
    lexicon = common.read_lexicon(os.path.join(data_dir or common.DATA, "lexicon", "lexicon.tsv"))
    return [row for row in rows if row.new_tag not in common.allowed_tags(row.token, lexicon)]


def write_files(files, data_dir=None):
    """Write the table files into <data>/corrections."""
    folder = corrections.corrections_dir(data_dir)
    for name, lines in files.items():
        common.write_text(os.path.join(folder, name), lines)


def check_files(files, data_dir=None):
    """Names of the committed table files that differ from the regenerated lines (a missing file counts as different)."""
    folder = corrections.corrections_dir(data_dir)
    different = []
    for name, lines in files.items():
        path = os.path.join(folder, name)
        if not os.path.exists(path) or common.read_lines(path) != lines:
            different.append(name)
    return different


def draw_sample(rows, n, seed, per_rule=0, only=None):
    """Seeded precision sample: `n` random rows over everything, plus `per_rule` random rows of every rule (all rows of a rule with fewer).

    `only` is an optional set of rule ids that restricts the per-rule part. Returns a list of (label, row): the label is
    `random` or `rule`. Rows come out in a stable order.
    """
    rng = random.Random(seed)
    ordered = sorted(rows, key=corrections.sort_key)
    chosen = [("random", row) for row in rng.sample(ordered, min(n, len(ordered)))] if n else []
    if per_rule:
        by_rule = collections.defaultdict(list)
        for row in ordered:
            if only is None or row.provenance[len("rule:"):] in only:
                by_rule[row.provenance].append(row)
        for provenance in sorted(by_rule):
            group = by_rule[provenance]
            picks = group if len(group) <= per_rule else rng.sample(group, per_rule)
            chosen.extend(("rule", row) for row in picks)
    return chosen


def sample_text(chosen, corpora):
    """Human-readable blocks of a sample: header line, the full sentence with tags, the target in brackets."""
    lines = []
    for number, (label, row) in enumerate(chosen, 1):
        sentence = corpora[(row.source, row.split)].by_id[row.sent_id]
        index = int(row.token_index)
        cells = sentence["rows"]
        start, stop = max(0, index - 10), min(len(cells), index + 11)
        words = []
        for position in range(start, stop):
            word = "%s/%s" % (cells[position][0], cells[position][1])
            if len(cells[position]) > 3 and row.source == "masc":
                word += "/" + cells[position][3]
            words.append("[[%s>%s]]" % (word, row.new_tag) if position == index else word)
        lines.append("#%d %s %s %s/%s %s#%s %s %s>%s" % (number, label, row.provenance, row.source, row.split, row.sent_id,
                                                          row.token_index, row.token, row.old_tag, row.new_tag))
        lines.append("   " + ("... " if start else "") + " ".join(words) + (" ..." if stop < len(cells) else ""))
    return lines


def catalog_markdown():
    """Markdown tables of the rule catalog (mechanical, check and candidate rules)."""
    lines = ["| Id | Tier | CONVENTIONS section | Detection and change |", "| --- | --- | --- | --- |"]
    for rule_id, tier, section, doc in convention_rules.catalog():
        lines.append("| `%s` | %s | %s | %s |" % (rule_id, "mechanical" if tier == convention_rules.FIX else "check", section, doc))
    lines += ["", "| Candidate id | Detection |", "| --- | --- |"]
    for candidate_id, doc in convention_candidates.catalog():
        lines.append("| `%s` | %s |" % (candidate_id, doc))
    return lines


def fixed_point(data_dir=None):
    """Apply the generated rows in memory and run the rules again: returns (applied, leftover fix proposals, conflicts).

    The second run recomputes the corpus resources from the corrected data, the situation the linter will be in once the
    corrections are merged. Zero leftovers and zero conflicts is the fixed point.
    """
    files, result, corpora, sentences, resources = generate_all(data_dir)
    applied = corrections.apply_in_memory(corpora, result.rows)
    fresh = [convention_rules.Sentence(source, split, sentence)
             for (source, split), corpus in corpora.items() for sentence in corpus.sentences]
    second = build(fresh, convention_rules.build_resources(fresh))
    return applied, second.rows, second.conflicts


def main(argv=None):
    parser = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    parser.add_argument("command", nargs="?", default="generate", choices=("generate", "sample", "fixed-point", "catalog"))
    parser.add_argument("--data-dir", default=None, help="data folder (default: the repository's data/)")
    parser.add_argument("--check", action="store_true", help="compare the regenerated tables with the committed ones, write nothing")
    parser.add_argument("--n", type=int, default=50, help="sample: random rows")
    parser.add_argument("--seed", type=int, default=20261104, help="sample: random seed")
    parser.add_argument("--per-rule", type=int, default=0, help="sample: rows per rule (all rows of a rule with fewer)")
    parser.add_argument("--only", default=None, help="sample: comma separated rule ids that restrict the per-rule part")
    parser.add_argument("--out", default=None, help="sample: write the blocks to this file instead of printing them")
    args = parser.parse_args(argv)

    if args.command == "catalog":
        print("\n".join(catalog_markdown()))
        return 0
    if args.command == "fixed-point":
        applied, leftover, conflicts = fixed_point(args.data_dir)
        print("applied %d corrections in memory; second pass: %d rows left, %d conflicts" % (applied, len(leftover), len(conflicts)))
        for row in leftover[:20]:
            print("  left:", "\t".join(row))
        return 1 if leftover or conflicts else 0

    files, result, corpora, sentences, resources = generate_all(args.data_dir)
    if args.command == "sample":
        only = set(args.only.split(",")) if args.only else None
        text = sample_text(draw_sample(result.rows, args.n, args.seed, args.per_rule, only), corpora)
        if args.out:
            common.write_text(args.out, text)
            print("wrote %d blocks to %s" % (len(text) // 2, args.out))
        else:
            print("\n".join(text))
        return 0

    print("%d sentences, %d tokens" % (result.sentences, result.tokens))
    order = [item.id for item in convention_rules.RULES]
    print_counts("mechanical corrections (rows)", count_table(result.rows, lambda row: row.provenance[len("rule:"):]), order)
    print_counts("candidates (rows)", count_table(result.candidates, lambda row: row.candidate_rule),
                 order + [item.id for item in convention_candidates.CANDIDATES])
    rejects = lexicon_rejects(result.rows, args.data_dir)
    print("new tag not allowed by the current lexicon: %d of %d rows" % (len(rejects), len(result.rows)))
    candidate_size = sum(len(line.encode("utf-8")) + 1 for line in files[KINDS["candidates"][0]])
    print("candidates.tsv: %d bytes" % candidate_size)
    if args.check:
        different = check_files(files, args.data_dir)
        if different:
            print("FAILED: differs from the committed file(s): " + ", ".join(different))
            return 1
        print("committed tables are up to date")
        return 0
    write_files(files, args.data_dir)
    print("wrote %s" % ", ".join(files))
    return 0


if __name__ == "__main__":
    sys.exit(main())

"""Corrections overlay: read, validate, serialize and apply the staged label corrections (Python 3, standard library only).

The corpus files under data/ are never edited by hand. A correction is one row of an overlay table in data/corrections/:
the address of one token (source, split, sentence id, token index), what the token is now (old tag, old status) and what
it must become (new tag, new status), and who decided it (provenance: `rule:<id>` of the generator, `llm:<model>:<run>`
of the adjudication). This module is the only code that knows the table formats; the generator, the linter and the tests
all go through it.

  python tools/data/corrections.py validate            # every table against the committed corpora (exit code 1 on an error)
  python tools/data/corrections.py apply [--write] [--reverse]
                                                       # show (or, with --write, perform) the corrections on the corpus files
  python tools/data/corrections.py show --sent-id ID   # the rows of a sentence, in context
  python tools/data/corrections.py withdraw --detector ID --reason TEXT [--write]
                                                       # take the adjudicated rows of one detector out of adjudicated.tsv
                                                       # into withdrawn-<ID>.tsv (provenance; pending state only)

Four tables share the module (see data/corrections/README.md and data/CONVENTIONS.md, "Other Formats"):

* `corrections` (`mechanical.tsv` of the rules, `adjudicated.tsv` of the two blind taggers): the changes themselves;
* `candidates` (`candidates.tsv`): judgment cases for the adjudication, never changes;
* `exceptions` (`exceptions.tsv`): "reviewed, keep as is" decisions that the linter honors.

State-aware validation: a corrections table is valid when EVERY row matches the corpus in the pending state (token,
`old_tag`, `old_status`) or EVERY row matches in the applied state (`new_*`); a mix of both is an error. The same files stay
valid after the corrections are applied to the corpora.

One row per token, with one exception (supersession, see `combine_tables`): an `adjudicated.tsv` row replaces the
`mechanical.tsv` row of the same token when both rows start from the same corpus value (`old_tag`, `old_status`). The
mechanical row stays in its file, byte for byte, and is only ignored by everything that validates, applies or simulates the
corrections (the "effective" rows); `validate` reports the number of superseded rows as a note.

Withdrawn rows (`withdrawn-<detector>.tsv`): adjudicated rows that were taken out of `adjudicated.tsv` on purpose (`withdraw`,
pending state only) stay in their own table as provenance. A withdrawn table is never applied; `validate` checks that its
tokens carry the OLD value in the corpora, whatever the state of the other tables.
"""
import argparse
import collections
import difflib
import os
import re
import sys
import tempfile

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import common

# ---------------------------------------------------------------- table formats

COLUMNS = {
    "corrections": ("source", "split", "source_id", "sent_id", "token_index", "token", "old_tag", "new_tag", "old_status",
                    "new_status", "provenance"),
    "candidates": ("source", "split", "source_id", "sent_id", "token_index", "span_length", "token", "current_tag",
                   "current_status", "source_tag", "candidate_rule", "hint", "context"),
    "exceptions": ("source", "split", "source_id", "sent_id", "token_index", "token", "rule", "reason", "provenance"),
}
ROW_TYPES = {kind: collections.namedtuple(kind.capitalize() + "Row", names) for kind, names in COLUMNS.items()}
Correction = ROW_TYPES["corrections"]

# The sources the overlay can address and the splits each has (`-` where the source has no splits).
SOURCES = {"open": ("train", "dev", "test"), "masc": ("-",), "silver": ("-",)}

# Corpus file of every (source, split), relative to the data folder. The STE challenge set and the imperative, compound and headings training
# sources are linted but never corrected (the staged corrections address the three correctable training sources and the open dev and test
# splits only).
CORPUS_FILES = {("open", "train"): "open/train.tsv", ("open", "dev"): "open/dev.tsv", ("open", "test"): "open/test.tsv",
                ("masc", "-"): "masc/masc.tsv", ("silver", "-"): "silver/silver.tsv"}
LINT_ONLY_FILES = {("ste", "-"): "eval/ste-challenge/test.tsv", ("imperative", "-"): "imperative/imperative.tsv",
                   ("compound", "-"): "compound/compound.tsv", ("headings", "-"): "headings/headings.tsv"}

CORRECTIONS_FOLDER = "corrections"
MECHANICAL_TABLE = "mechanical.tsv"
ADJUDICATED_TABLE = "adjudicated.tsv"
CORRECTION_TABLES = (MECHANICAL_TABLE, ADJUDICATED_TABLE)  # tables of kind `corrections`, in this order; adjudicated.tsv appears with the adjudication
ADJUDICATED_PROVENANCE_RE = re.compile(r"^llm:opus\+sol:[a-z0-9]+$")
CANDIDATES_TABLE = "candidates.tsv"
EXCEPTIONS_TABLE = "exceptions.tsv"

PROVENANCE_RE = re.compile(r"^(?:rule:[a-z0-9]+(?:-[a-z0-9]+)*|llm:[^:\s]+:[^:\s]+)$")
INDEX_RE = re.compile(r"^(?:0|[1-9][0-9]*)$")
SPAN_RE = re.compile(r"^[1-9][0-9]*$")
RULE_ID_RE = re.compile(r"^[a-z0-9]+(?:-[a-z0-9]+)*$")


def corrections_dir(data_dir=None):
    """Folder of the overlay tables (`<data>/corrections`)."""
    return os.path.join(data_dir or common.DATA, CORRECTIONS_FOLDER)


def has_valid_index(row):
    """True when the `token_index` of a row is a non-negative integer without leading zeros (safe to convert with `int`)."""
    return bool(INDEX_RE.match(row.token_index))


def sort_key(row):
    """Ordering of every table: source, split, sentence id by ordinal UTF-8 bytes, token index numerically."""
    return (row.source, row.split, row.sent_id.encode("utf-8"), int(row.token_index))


def address(row):
    """The unique address of the token a row refers to: (source, split, sent_id, token_index)."""
    return (row.source, row.split, row.sent_id, int(row.token_index))


# ---------------------------------------------------------------- reading and writing tables

def parse_row(kind, cells, where="row"):
    """Row of a table kind from its tab separated cells; the cell count must match the column list exactly."""
    if len(cells) != len(COLUMNS[kind]):
        raise ValueError("%s: %d cells, expected %d (%s)" % (where, len(cells), len(COLUMNS[kind]), ", ".join(COLUMNS[kind])))
    return ROW_TYPES[kind](*cells)


def parse_table(kind, lines, origin="table"):
    """(header_lines, rows) of the lines of a table: comment lines first, one `# columns = ...` line among them, then rows."""
    header, rows = [], []
    columns_seen = False
    for number, line in enumerate(lines, 1):
        where = "%s, line %d" % (origin, number)
        if line.startswith("# "):
            if rows:
                raise ValueError(where + ": comment line after the first row")
            header.append(line)
            if line.startswith("# columns = "):
                columns_seen = True
                if line[len("# columns = "):] != ", ".join(COLUMNS[kind]):
                    raise ValueError(where + ": the column list is not " + ", ".join(COLUMNS[kind]))
        elif line == "":
            raise ValueError(where + ": blank line")
        else:
            rows.append(parse_row(kind, line.split("\t"), where))
    if not columns_seen:
        raise ValueError(origin + ": no '# columns = ' line")
    return header, rows


def read_table(path, kind):
    """(header_lines, rows) of a table file."""
    return parse_table(kind, common.read_lines(path), common.rel(path) if path.startswith(common.ROOT) else path)


def format_table(kind, header, rows):
    """Lines of a table: the header (which must carry the columns line), then the rows sorted by `sort_key`."""
    lines = list(header)
    if "# columns = " + ", ".join(COLUMNS[kind]) not in lines:
        raise ValueError("header without the columns line of " + kind)
    for row in sorted(rows, key=sort_key):
        lines.append("\t".join(row))
    return lines


def write_table(path, kind, header, rows):
    """Write a table file (UTF-8, LF, sorted)."""
    common.write_text(path, format_table(kind, header, rows))


# ---------------------------------------------------------------- row validation (shared by the cases fixture)

def row_error(row):
    """First error of one corrections row as (code, message), or None when the row is valid on its own.

    The checks run in this order, so a fixture row that breaks several rules has one well-defined verdict (the shared
    cases in tools/data/tests/corrections-cases.tsv list the code; a second implementation can be compared to it):
    bad-source, bad-split, bad-index, bad-token, bad-tag, masked-tag, bad-status, no-change, status-base-change,
    mechanical-status-change, bad-provenance.
    """
    if row.source not in SOURCES:
        return "bad-source", "source " + repr(row.source) + " is not one of " + ", ".join(SOURCES)
    if row.split not in SOURCES[row.source]:
        return "bad-split", "split " + repr(row.split) + " is not legal for source " + row.source
    if not INDEX_RE.match(row.token_index):
        return "bad-index", "token_index " + repr(row.token_index) + " is not a non-negative integer"
    if row.token == "" or any(ch.isspace() for ch in row.token) or row.sent_id == "" or row.source_id == "":
        return "bad-token", "empty token, sentence id or document id, or white space in the token"
    for name in ("old_tag", "new_tag"):
        value = getattr(row, name)
        if value == "_":
            return "masked-tag", name + " is _ (masked rows are not addressable)"
        if value not in common.UPOS_SET:
            return "bad-tag", name + " " + repr(value) + " is not one of the 17 tags"
    try:
        old_base, _ = common.parse_status(row.old_status)
        new_base, _ = common.parse_status(row.new_status)
    except ValueError as error:
        return "bad-status", str(error)
    if row.old_tag == row.new_tag and row.old_status == row.new_status:
        return "no-change", "the row changes neither the tag nor the status"
    is_llm = row.provenance.startswith("llm:")
    if old_base != new_base:
        if not (is_llm and new_base == "disputed"):
            return "status-base-change", "the base status may change only to disputed, and only by an llm row"
    if row.provenance.startswith("rule:") and row.old_status != row.new_status:
        return "mechanical-status-change", "a rule row keeps the status cell"
    if not PROVENANCE_RE.match(row.provenance):
        return "bad-provenance", "provenance " + repr(row.provenance) + " is neither rule:<id> nor llm:<model>:<run>"
    return None


def candidate_error(row, check_registry=False):
    """First error of one candidate row as (code, message), or None when its fields are valid on their own.

    Independent of the corpus state: the syntax of every field is checked in the pending and in the applied state alike
    (only the comparison of `current_tag` and `current_status` with the corpus is state dependent, see
    `validate_candidates_against_corpora`). The codes and their order are shared with the C# data tests through
    tools/data/tests/candidate-cases.tsv: bad-source, bad-split, bad-index, bad-token, bad-span, bad-current-state,
    bad-rule-or-hint. With `check_registry` a last check, which only this module can make, follows: unknown-rule (the id is
    neither a rule nor a candidate detector).
    """
    if row.source not in SOURCES:
        return "bad-source", "source " + repr(row.source) + " is not one of " + ", ".join(SOURCES)
    if row.split not in SOURCES[row.source]:
        return "bad-split", "split " + repr(row.split) + " is not legal for source " + row.source
    if not INDEX_RE.match(row.token_index):
        return "bad-index", "token_index " + repr(row.token_index) + " is not a non-negative integer"
    if row.token == "" or any(ch.isspace() for ch in row.token) or row.sent_id == "" or row.source_id == "":
        return "bad-token", "empty token, sentence id or document id, or white space in the token"
    if not SPAN_RE.match(row.span_length):
        return "bad-span", "span_length " + repr(row.span_length) + " is not a positive integer"
    try:
        common.parse_status(row.current_status)
    except ValueError as error:
        return "bad-current-state", str(error)
    if row.current_tag not in common.UPOS_SET:
        return "bad-current-state", "current_tag " + repr(row.current_tag) + " is not one of the 17 tags"
    if not RULE_ID_RE.match(row.candidate_rule) or row.hint == "" or row.context == "":
        return "bad-rule-or-hint", "candidate_rule is not an id, or the hint or the context is empty"
    if check_registry:
        import convention_candidates
        import convention_rules
        if row.candidate_rule not in convention_candidates.candidate_ids() | convention_rules.rule_ids():
            return "unknown-rule", "candidate_rule " + repr(row.candidate_rule) + " is neither a rule nor a candidate detector"
    return None


def row_errors(rows):
    """Messages for the rows of a corrections table (or several) that are not valid on their own (no ordering or uniqueness check)."""
    errors = []
    for row in rows:
        problem = row_error(row)
        if problem:
            errors.append("%s %s#%s: %s (%s)" % (problem[0], row.sent_id, row.token_index, problem[1], row.token))
    return errors


def table_errors(rows, kind="corrections"):
    """Errors of a whole table (or of several tables concatenated): row errors, ordering and unique addresses.

    A duplicate address with the same change is a duplicate; with a different change it is a conflict. The result is
    a list of messages, empty when the rows are valid. A row with an invalid field is reported and then skipped, so a
    malformed index never reaches the ordering or the address (which convert it to a number).
    """
    errors = []
    seen = {}
    previous = None
    for row in rows:
        if kind == "corrections":
            problem = row_error(row)
            if problem:
                errors.append("%s %s#%s: %s (%s)" % (problem[0], row.sent_id, row.token_index, problem[1], row.token))
                continue
        elif kind == "candidates":
            problem = candidate_error(row, check_registry=True)
            if problem:
                errors.append("%s %s#%s: %s (%s)" % (problem[0], row.sent_id, row.token_index, problem[1], row.token))
                continue
        elif not INDEX_RE.match(row.token_index) or row.source not in SOURCES or row.split not in SOURCES[row.source]:
            errors.append("bad-address %s#%s" % (row.sent_id, row.token_index))
            continue
        key = address(row)
        if kind == "candidates":
            key = key + (row.candidate_rule,)
        elif kind == "exceptions":
            key = key + (row.rule,)
        if previous is not None and sort_key(row) < previous:
            errors.append("unsorted %s#%s" % (row.sent_id, row.token_index))
        previous = sort_key(row)
        if key in seen:
            same = seen[key][5:10] == row[5:10] if kind == "corrections" else seen[key] == row
            errors.append("%s %s#%s" % ("duplicate" if same else "conflict", row.sent_id, row.token_index))
        else:
            seen[key] = row
    return errors


def combine_tables(tables):
    """Merge the rows of the correction tables into the effective rows: returns (effective, superseded, errors).

    `tables` is a list of `(table name, rows)` in `CORRECTION_TABLES` order. The address of a token is unique across the
    tables, with one exception: an `adjudicated.tsv` row REPLACES the `mechanical.tsv` row of the same token (the owner's
    decision: the adjudication of two blind taggers outranks a rule). The replacement is legal only when both rows start from
    the same corpus value (equal `old_tag` and `old_status`) and the adjudicated row is not the same change as the mechanical
    one; the superseded mechanical row is returned in `superseded` and is not part of `effective`.

    Every other second row for an address (two mechanical rows, two adjudicated rows, a replacement that starts from a
    different value, a replacement that repeats the change) is an error ("duplicate" for the same change, "conflict" for a
    different one) and both rows stay in `effective`, so that the corpora comparison reports them too. Rows that are not valid on
    their own are reported elsewhere and are left out of the address check.
    """
    owners = collections.defaultdict(list)
    for name, rows in tables:
        for row in rows:
            if row_error(row) is None:
                owners[address(row)].append((name, row))
    superseded_ids, errors = set(), []
    for rows in owners.values():
        if len(rows) < 2:
            continue
        first, second = rows[0], rows[1]
        clean = (len(rows) == 2 and first[0] == MECHANICAL_TABLE and second[0] == ADJUDICATED_TABLE
                 and first[1].old_tag == second[1].old_tag and first[1].old_status == second[1].old_status
                 and (first[1].new_tag, first[1].new_status) != (second[1].new_tag, second[1].new_status))
        if clean:
            superseded_ids.add(id(first[1]))
            continue
        row = second[1]
        same = first[1][5:10] == row[5:10]  # token, old and new tag, old and new status
        errors.append("across tables: %s %s#%s" % ("duplicate" if same else "conflict", row.sent_id, row.token_index))
    effective, superseded = [], []
    for _, rows in tables:
        for row in rows:
            (superseded if id(row) in superseded_ids else effective).append(row)
    return effective, superseded, errors


def adjudicated_row_kind(row):
    """What an adjudicated row does: `tag` (the tag changes, the base status stays, flags may change), `disputed` (same tag,
    the base becomes disputed, the flags stay), `flags` (same tag and base, the flags change), or None for a row that is none of these.
    """
    try:
        old_base, old_flags = common.parse_status(row.old_status)
        new_base, new_flags = common.parse_status(row.new_status)
    except ValueError:
        return None
    if row.old_tag != row.new_tag:
        return "tag" if old_base == new_base else None
    if old_base != new_base:
        return "disputed" if new_base == "disputed" and old_flags == new_flags else None
    return "flags" if old_flags != new_flags else None


def adjudicated_findings(data_dir=None, rows=None, state=None, header=None):
    """Messages for `adjudicated.tsv` beyond the generic table rules; an empty list when the table is absent or sound.

    Every row is a decision of the two blind taggers (provenance `llm:opus+sol:<batch>`, a `rule:` row does not belong here), is
    exactly one of the kinds of `adjudicated_row_kind`, and the `# input = <file> sha256 <hash>` header lines name the corpus
    files as they are (a stale table is a finding), the last check only in the pending state: once the corrections are
    applied the corpus files have, by design, other hashes. The table is read from `<data>/corrections/adjudicated.tsv`
    unless the caller passes its `rows` and `header` (the adjudication tool checks a table before it writes it); `state` may be
    given when the caller has it already.
    """
    if rows is None:
        path = os.path.join(corrections_dir(data_dir), ADJUDICATED_TABLE)
        if not os.path.exists(path):
            return []
        try:
            header, rows = read_table(path, "corrections")
        except ValueError as error:
            return [ADJUDICATED_TABLE + ": " + str(error)]
    findings = []
    for row in rows:
        where = "%s %s#%s" % (ADJUDICATED_TABLE, row.sent_id, row.token_index)
        if not ADJUDICATED_PROVENANCE_RE.match(row.provenance):
            findings.append("%s: provenance %r is not llm:opus+sol:<batch>" % (where, row.provenance))
        if row_error(row) is None and adjudicated_row_kind(row) is None:
            findings.append("%s: the row is neither a tag change, a disputed mark nor a flag change" % where)
    if state is None:
        state = validate_against_corpora([row for row in rows if row_error(row) is None], load_corpora(data_dir))[1]
    listed, header_findings = adjudicated_header(header)
    findings.extend(header_findings)
    findings.extend(input_state_findings(ADJUDICATED_TABLE, listed, data_dir, state))
    return findings


def input_state_findings(table, listed, data_dir, state):
    """Messages for the `# input` lines of a table (`listed` = {corpus file: SHA-256}) against the corpus files of a data folder.

    There must be one line for every corpus file that exists; and, only in the pending state (`state` is not "applied"), its hash is
    that of the corpus file as it is (a stale table is a finding): once the corrections are applied the corpus files have, by
    design, other hashes. Used for `adjudicated.tsv` and for the `withdrawn-<detector>.tsv` tables.
    """
    findings = []
    base = data_dir or common.DATA
    for key, relative in CORPUS_FILES.items():
        corpus_path = os.path.join(base, *relative.split("/"))
        if not os.path.exists(corpus_path):
            continue
        if relative not in listed:
            findings.append("%s: no '# input' line for the corpus file %s" % (table, relative))
        elif state != "applied" and listed[relative] != common.sha256_file(corpus_path):
            # Once the corrections are applied the corpus files have, by design, other hashes.
            findings.append("%s: stale, the corpus file %s has another SHA-256 than the '# input' line" % (table, relative))
    return findings


ADJUDICATED_HEADER_KEYS = ("corpus", "license", "kind", "columns", "generator")
INPUT_LINE_RE = re.compile(r"^# input = (\S+) sha256 ([0-9a-f]{64})$")


def is_input_line(line):
    """True for a metadata line that claims to be an `# input` line (well-formed or not)."""
    return line.startswith("# input")


def input_lines(table, lines):
    """(listed, findings) of the `# input = <file> sha256 <hash>` lines among `lines`: `listed` is {corpus file: SHA-256}.

    A line must be well formed and name a known corpus file (see `CORPUS_FILES`) once; the missing and the stale lines are
    `input_state_findings`. Used for `adjudicated.tsv` and for the `withdrawn-<detector>.tsv` tables.
    """
    findings = []
    listed = {}
    known = {relative for relative in CORPUS_FILES.values()}
    for line in lines:
        match = INPUT_LINE_RE.match(line)
        if not match:
            findings.append("%s: malformed line %r, expected '# input = <file> sha256 <64 hex digits>'" % (table, line))
        elif match.group(1) not in known:
            findings.append("%s: '# input' names an unknown corpus file %s" % (table, match.group(1)))
        elif match.group(1) in listed:
            findings.append("%s: '# input' line for %s is repeated" % (table, match.group(1)))
        else:
            listed[match.group(1)] = match.group(2)
    return listed, findings


def adjudicated_header(header):
    """(listed, findings) of the metadata lines of `adjudicated.tsv`: `listed` is {corpus file: SHA-256} of the `# input` lines.

    The header is the five lines `# corpus = ...`, `# license = CC0-1.0 ...`, `# kind = corrections`, `# columns = ...` and
    `# generator = tools/data/adjudicate.py ...`, each exactly once, then one well-formed `# input = <file> sha256 <hash>` line per
    corpus file (a known file, no repeat); any other comment line is a finding.
    """
    findings = []
    seen = collections.Counter()
    inputs = []
    for line in header or []:
        key = line[2:].split(" = ", 1)[0] if line.startswith("# ") and " = " in line else None
        if key in ADJUDICATED_HEADER_KEYS:
            seen[key] += 1
            value = line[len("# %s = " % key):]
            if key == "kind" and value != "corrections":
                findings.append("%s: '# kind' is %r, expected corrections" % (ADJUDICATED_TABLE, value))
            if key == "corpus" and not value.startswith("DemaConsulting.PartOfSpeech staged corpus corrections: "):
                findings.append("%s: the '# corpus' line is not 'DemaConsulting.PartOfSpeech staged corpus corrections: ...'" % ADJUDICATED_TABLE)
            if key == "license" and not value.startswith("CC0-1.0 "):
                findings.append("%s: the '# license' line does not start with 'CC0-1.0 '" % ADJUDICATED_TABLE)
            if key == "generator" and not value.startswith("tools/data/adjudicate.py"):
                findings.append("%s: the '# generator' line does not name tools/data/adjudicate.py" % ADJUDICATED_TABLE)
        elif key == "input" or is_input_line(line):
            inputs.append(line)
        else:
            findings.append("%s: unknown metadata line %r" % (ADJUDICATED_TABLE, line))
    listed, input_findings = input_lines(ADJUDICATED_TABLE, inputs)
    findings.extend(input_findings)
    for key in ADJUDICATED_HEADER_KEYS:
        if seen[key] != 1:
            findings.append("%s: the '# %s' line is %s" % (ADJUDICATED_TABLE, key, "missing" if not seen[key] else "repeated"))
    return listed, findings


# ---------------------------------------------------------------- corpora

class Corpus:
    """One corpus file read with the shared reader: header lines, sentences and an index by sentence id."""

    def __init__(self, source, split, path):
        self.source, self.split, self.path = source, split, path
        self.header, self.sentences = common.read_corpus(path)
        self.by_id = {sentence["sent_id"]: sentence for sentence in self.sentences}


def load_corpora(data_dir=None, lint_only=False, only=None):
    """{(source, split): Corpus} of the correctable corpora (and the lint-only STE set and the imperative, compound and headings sources with `lint_only`), optionally filtered.

    `only` is an iterable of (source, split) keys to read; the others are skipped (they cost the reading time).
    """
    base = data_dir or common.DATA
    files = dict(CORPUS_FILES)
    if lint_only:
        files.update(LINT_ONLY_FILES)
    corpora = {}
    for key, relative in files.items():
        if only is not None and key not in only:
            continue
        path = os.path.join(base, *relative.split("/"))
        if os.path.exists(path):
            corpora[key] = Corpus(key[0], key[1], path)
    return corpora



def validate_against_corpora(rows, corpora):
    """Compare corrections rows with the corpora: returns (errors, state).

    Each row must name an existing sentence (id and document id), an existing token (index and form) and match the
    corpus either in the pending state (`old_tag`, `old_status`) or in the applied state (`new_tag`, `new_status`).
    `state` is `pending`, `applied`, `empty` (no rows) or `mixed` (an error: rows of both states).
    """
    errors = []
    states = collections.Counter()
    for row in rows:
        if not has_valid_index(row):
            errors.append("bad-index %s#%s" % (row.sent_id, row.token_index))
            continue
        corpus = corpora.get((row.source, row.split))
        if corpus is None:
            errors.append("no-corpus %s/%s" % (row.source, row.split))
            continue
        sentence = corpus.by_id.get(row.sent_id)
        if sentence is None:
            errors.append("unknown-sentence %s" % row.sent_id)
            continue
        if sentence["source_id"] != row.source_id:
            errors.append("source-id-mismatch %s: %s" % (row.sent_id, sentence["source_id"]))
            continue
        index = int(row.token_index)
        if index >= len(sentence["rows"]):
            errors.append("token-index-out-of-range %s#%d" % (row.sent_id, index))
            continue
        cells = sentence["rows"][index]
        if cells[0] != row.token:
            errors.append("token-mismatch %s#%d: %s != %s" % (row.sent_id, index, cells[0], row.token))
            continue
        if cells[1] == row.old_tag and cells[2] == row.old_status:
            states["pending"] += 1
        elif cells[1] == row.new_tag and cells[2] == row.new_status:
            states["applied"] += 1
        else:
            errors.append("state-mismatch %s#%d: corpus has %s %s, row says %s %s -> %s %s" % (
                row.sent_id, index, cells[1], cells[2], row.old_tag, row.old_status, row.new_tag, row.new_status))
    if states["pending"] and states["applied"]:
        errors.append("mixed-state: %d rows pending and %d applied" % (states["pending"], states["applied"]))
        return errors, "mixed"
    state = "pending" if states["pending"] else "applied" if states["applied"] else "empty"
    return errors, state


def validate_candidates_against_corpora(rows, corpora, check_tags=True):
    """Errors of candidate rows against the corpora: address and token always, `current_tag/status` when `check_tags`.

    The tag columns describe the corpus before the corrections; once they are applied they are history, so the caller
    passes `check_tags=False` for the applied state. Only this state-dependent comparison is skipped: the syntax of every
    field is validated by `candidate_error` in both states. A row with a malformed index or span is reported and skipped.
    """
    errors = []
    for row in rows:
        if not has_valid_index(row) or not SPAN_RE.match(row.span_length):
            errors.append("bad-index-or-span %s#%s+%s" % (row.sent_id, row.token_index, row.span_length))
            continue
        corpus = corpora.get((row.source, row.split))
        sentence = corpus.by_id.get(row.sent_id) if corpus else None
        if sentence is None or sentence["source_id"] != row.source_id:
            errors.append("unknown-sentence %s" % row.sent_id)
            continue
        index, span = int(row.token_index), int(row.span_length)
        if span < 1 or index + span > len(sentence["rows"]):
            errors.append("span-out-of-range %s#%d+%d" % (row.sent_id, index, span))
            continue
        cells = sentence["rows"][index]
        if cells[0] != row.token:
            errors.append("token-mismatch %s#%d: %s != %s" % (row.sent_id, index, cells[0], row.token))
        elif check_tags and (cells[1], cells[2]) != (row.current_tag, row.current_status):
            errors.append("state-mismatch %s#%d" % (row.sent_id, index))
    return errors


def validate_exceptions_against_corpora(rows, corpora):
    """Errors of exception rows: address and token, and a rule id that exists in the registry."""
    import convention_rules
    known = convention_rules.rule_ids()
    errors = []
    for row in rows:
        corpus = corpora.get((row.source, row.split))
        sentence = corpus.by_id.get(row.sent_id) if corpus else None
        if not has_valid_index(row):
            errors.append("bad-index %s#%s" % (row.sent_id, row.token_index))
        elif sentence is None or sentence["source_id"] != row.source_id:
            errors.append("unknown-sentence %s" % row.sent_id)
        elif int(row.token_index) >= len(sentence["rows"]) or sentence["rows"][int(row.token_index)][0] != row.token:
            errors.append("token-mismatch %s#%s" % (row.sent_id, row.token_index))
        if row.rule not in known:
            errors.append("unknown-rule %s" % row.rule)
        if not row.reason or not PROVENANCE_RE.match(row.provenance):
            errors.append("bad-exception %s#%s" % (row.sent_id, row.token_index))
    return errors


# ---------------------------------------------------------------- applying corrections

def group_rows(rows):
    """{(source, split): [rows]} in table order."""
    groups = collections.defaultdict(list)
    for row in rows:
        groups[(row.source, row.split)].append(row)
    return groups


def apply_to_sentences(sentences, rows, reverse=False):
    """Apply (or reverse) the rows of one corpus to parsed sentences, in place; returns the number of changed tokens.

    Used for the linter's `--apply` and the fixed-point test, which look at the corpora as they would be after the
    corrections without touching a file. Raises ValueError when a row does not match (the caller validates first).
    """
    by_id = {sentence["sent_id"]: sentence for sentence in sentences}
    changed = 0
    for row in rows:
        sentence = by_id.get(row.sent_id)
        if not has_valid_index(row) or sentence is None or int(row.token_index) >= len(sentence["rows"]):
            raise ValueError("row addresses no token (unknown sentence, or a token index that is malformed or out of range): "
                             + "\t".join(row))
        cells = sentence["rows"][int(row.token_index)]
        before = (row.new_tag, row.new_status) if reverse else (row.old_tag, row.old_status)
        after = (row.old_tag, row.old_status) if reverse else (row.new_tag, row.new_status)
        if cells[0] != row.token or (cells[1], cells[2]) != before:
            raise ValueError("row does not match the corpus: " + "\t".join(row))
        cells[1], cells[2] = after
        changed += 1
    return changed


def apply_in_memory(corpora, rows, reverse=False):
    """Apply (or reverse) corrections rows to the loaded corpora, in place; returns the number of changed tokens.

    Raises ValueError for a row whose corpus is not loaded or whose address is malformed, never KeyError or IndexError.
    """
    groups = group_rows(rows)
    for key in groups:
        if key not in corpora:
            raise ValueError("no corpus %s/%s is loaded for the rows" % key)
    return sum(apply_to_sentences(corpora[key].sentences, group, reverse) for key, group in groups.items())


def flag_note():
    """The header line that documents the status flags (the one `common.with_flag_documentation` inserts)."""
    probe = common.with_flag_documentation(["# status = x"], [{"rows": [["a", "X", "unchecked+mentioned"]]}])
    return probe[1]


def apply_to_lines(lines, rows, reverse=False):
    """Apply (or reverse) the rows of one corpus file to its raw lines; returns the new list of lines.

    The committed files cannot be round-tripped through `common.read_corpus` and `common.write_corpus` (the writer
    drops comment lines such as `# genre = ...`), so the token rows are edited in place: only cells 1 and 2 of the
    addressed rows change, every other byte stays. A flag appears in a file for the first time: the header gains the
    documenting `# status flags = ...` line after the `# status = ` line, exactly as `common.with_flag_documentation`
    writes it; when the last flag disappears (`reverse`) that line is removed again, so apply then reverse is
    byte-identical.
    """
    out = list(lines)
    positions = {}
    sent_id = None
    for number, line in enumerate(out):
        if line.startswith("# sent_id = "):
            sent_id = line[len("# sent_id = "):]
            positions[sent_id] = []
        elif line == "":
            sent_id = None
        elif sent_id is not None and not line.startswith("# "):
            positions[sent_id].append(number)
    for row in rows:
        where = positions.get(row.sent_id)
        if where is None or not has_valid_index(row) or int(row.token_index) >= len(where):
            raise ValueError("row addresses no token (unknown sentence, or a token index that is malformed or out of range): "
                             + "\t".join(row))
        number = where[int(row.token_index)]
        cells = out[number].split("\t")
        before = (row.new_tag, row.new_status) if reverse else (row.old_tag, row.old_status)
        after = (row.old_tag, row.old_status) if reverse else (row.new_tag, row.new_status)
        if cells[0] != row.token or len(cells) < 3 or (cells[1], cells[2]) != before:
            raise ValueError("row does not match the file: " + "\t".join(row))
        cells[1], cells[2] = after
        out[number] = "\t".join(cells)
    return _fix_flag_note(out)


def _fix_flag_note(lines):
    """Insert or remove the header line documenting the status flags so that it is present exactly when a flag is."""
    try:
        end = lines.index("")
    except ValueError:
        end = len(lines)
    note = flag_note()
    flagged = any("\t" in line and not line.startswith("# ") and "+" in line.split("\t")[2]
                  for line in lines[end:] if len(line.split("\t")) > 2)
    header = lines[:end]
    if flagged and note not in header:
        for position, line in enumerate(header):
            if line.startswith("# status = "):
                return header[:position + 1] + [note] + header[position + 1:] + lines[end:]
    if not flagged and note in header:
        header.remove(note)
        return header + lines[end:]
    return lines


def apply_to_files(rows, data_dir=None, reverse=False, write=False):
    """Apply (or reverse) corrections rows to the corpus files; returns {(source, split): (old_lines, new_lines)}.

    Nothing is written unless `write` is set; a written file is replaced atomically (temp file, then rename), in the
    same format as `common.write_text`. A corpus of a row that is not on disk is an error.
    """
    base = data_dir or common.DATA
    result = {}
    groups = group_rows(rows)
    for key in groups:
        if key not in CORPUS_FILES:
            raise ValueError("no corpus file for %s/%s" % key)
    for key, group in groups.items():
        path = os.path.join(base, *CORPUS_FILES[key].split("/"))
        old = common.read_lines(path)
        result[key] = (old, apply_to_lines(old, group, reverse))  # every corpus is checked before the first file is written
    if write:
        for key, (old, new) in result.items():
            path = os.path.join(base, *CORPUS_FILES[key].split("/"))
            if new != old:
                handle, temp = tempfile.mkstemp(dir=os.path.dirname(path), suffix=".part")
                os.close(handle)
                try:
                    common.write_text(temp, new)
                    os.replace(temp, path)
                finally:
                    if os.path.exists(temp):
                        os.remove(temp)
    return result


# ---------------------------------------------------------------- loading the overlay of a data folder

def load_overlay(data_dir=None):
    """The tables of `<data>/corrections`: {"corrections": [effective rows of all correction tables], "superseded": [...],
    "candidates": [...], "exceptions": [...]}.

    "corrections" is what validation, `apply` and the linter's `--apply` use: the rows of `mechanical.tsv` without the ones that
    an `adjudicated.tsv` row replaces (see `combine_tables`), which are listed in "superseded". A table that does not exist is
    empty (adjudicated.tsv appears only with the adjudication). Problems of the format raise ValueError; the unresolvable
    duplicates of an address are left in the rows for `validate_overlay` to report.
    """
    folder = corrections_dir(data_dir)
    tables = {"corrections": [], "superseded": [], "candidates": [], "exceptions": []}
    found = []
    for name in CORRECTION_TABLES:
        path = os.path.join(folder, name)
        if os.path.exists(path):
            found.append((name, read_table(path, "corrections")[1]))
    tables["corrections"], tables["superseded"], _ = combine_tables(found)
    for kind, name in (("candidates", CANDIDATES_TABLE), ("exceptions", EXCEPTIONS_TABLE)):
        path = os.path.join(folder, name)
        if os.path.exists(path):
            tables[kind] = read_table(path, kind)[1]
    return tables


def validate_overlay(data_dir=None):
    """Validate every table of a data folder against its corpora: returns (errors, state, counts).

    The corrections are compared with the corpora as the effective rows (an adjudicated row replaces the mechanical row of
    its token, see `combine_tables`); the number of superseded rows is returned in `counts["superseded"]` and is a note, not an error.
    `adjudicated.tsv` is also checked with `adjudicated_findings`.
    """
    errors = []
    tables = {"corrections": [], "candidates": [], "exceptions": []}
    folder = corrections_dir(data_dir)
    counts = {}
    found = []
    for name in CORRECTION_TABLES:
        path = os.path.join(folder, name)
        if os.path.exists(path):
            try:
                rows = read_table(path, "corrections")[1]
            except ValueError as error:
                errors.append(str(error))
                continue
            errors.extend(name + ": " + problem for problem in table_errors(rows))
            found.append((name, rows))
            counts[name] = len(rows)
    for kind, name in (("candidates", CANDIDATES_TABLE), ("exceptions", EXCEPTIONS_TABLE)):
        path = os.path.join(folder, name)
        if os.path.exists(path):
            try:
                rows = read_table(path, kind)[1]
            except ValueError as error:
                errors.append(str(error))
                continue
            errors.extend(name + ": " + problem for problem in table_errors(rows, kind))
            tables[kind] = rows
            counts[name] = len(rows)
    # The address of a token is unique across all correction tables, except that an adjudicated row replaces a mechanical one.
    tables["corrections"], superseded, across = combine_tables(found)
    errors.extend(across)
    if superseded:
        counts["superseded"] = len(superseded)
    corpora = load_corpora(data_dir)
    # Rows that are malformed on their own are already reported above; only well-formed rows are compared with the corpora
    # (a malformed index must not reach the code that converts it to a number).
    valid_corrections = [row for row in tables["corrections"] if row_error(row) is None]
    valid_candidates = [row for row in tables["candidates"] if candidate_error(row) is None]
    valid_exceptions = [row for row in tables["exceptions"]
                        if has_valid_index(row) and row.source in SOURCES and row.split in SOURCES[row.source]]
    row_errors, state = validate_against_corpora(valid_corrections, corpora)
    errors.extend(row_errors)
    errors.extend(validate_candidates_against_corpora(valid_candidates, corpora, check_tags=state != "applied"))
    errors.extend(validate_exceptions_against_corpora(valid_exceptions, corpora))
    if ADJUDICATED_TABLE in counts:
        errors.extend(adjudicated_findings(data_dir, state=state))
    withdrawn_errors, withdrawn_counts = withdrawn_findings(data_dir, [row for _, rows in found for row in rows], corpora, state)
    errors.extend(withdrawn_errors)
    counts.update(withdrawn_counts)
    return errors, state, counts


# ---------------------------------------------------------------- withdrawn rows (provenance of rows taken out of adjudicated.tsv)

WITHDRAWN_NAME_RE = re.compile(r"^withdrawn-([a-z0-9]+(?:-[a-z0-9]+)*)\.tsv$")
WITHDRAWN_HEADER_KEYS = ("corpus", "license", "kind", "columns", "generator", "detector", "reason")
WITHDRAWN_KIND = "withdrawn corrections"


def withdrawn_names(data_dir=None):
    """Names of the `withdrawn-<detector>.tsv` tables of a data folder, sorted."""
    folder = corrections_dir(data_dir)
    return sorted(name for name in os.listdir(folder) if WITHDRAWN_NAME_RE.match(name)) if os.path.isdir(folder) else []


def detector_rows(rows, candidates, detector):
    """The rows whose token is covered by a candidate row of `detector`.

    A candidate covers its `token_index` up to `token_index + span_length - 1`; a row covered by candidates of several detectors
    belongs to each of them. This is the definition that the trainer follow-up used for "the rows of a detector".
    """
    covered = set()
    for candidate in candidates:
        if candidate.candidate_rule == detector and has_valid_index(candidate) and SPAN_RE.match(candidate.span_length):
            start = int(candidate.token_index)
            covered.update((candidate.source, candidate.split, candidate.sent_id, index)
                           for index in range(start, start + int(candidate.span_length)))
    return [row for row in rows if has_valid_index(row) and address(row) in covered]


def withdrawn_findings(data_dir=None, current_rows=None, corpora=None, state=None):
    """(findings, counts) of the `withdrawn-<detector>.tsv` tables: rows taken out of `adjudicated.tsv` that stay as provenance.

    A withdrawn table is never applied. Its header names each of `corpus`, `license` (CC0-1.0), `kind`
    (`withdrawn corrections`), `columns`, `generator`, `detector` (the detector of the file name) and a non-empty `reason` once,
    then one well-formed `# input = <file> sha256 <hash>` line per corpus file, as `adjudicated.tsv` does (no unknown or repeated
    file, none missing, and in the pending state the hash of the corpus file as it is: see `input_state_findings`);
    its rows are valid, sorted decisions of the adjudication, covered by a candidate of the detector, and their address is in
    no current correction table; and the corpora hold the OLD value of every token (`old_tag`, `old_status`) in the pending and
    in the applied state alike, because a withdrawn row is not a correction of the corpora. `current_rows` are the rows of the
    correction tables (read from the folder when not given); `state` is the state of the corpora against those rows (computed
    when not given).
    """
    names = withdrawn_names(data_dir)
    if not names:
        return [], {}
    folder = corrections_dir(data_dir)
    if current_rows is None:
        current_rows = []
        for name in CORRECTION_TABLES:
            path = os.path.join(folder, name)
            if os.path.exists(path):
                current_rows.extend(read_table(path, "corrections")[1])
    corpora = corpora if corpora is not None else load_corpora(data_dir)
    if state is None:
        state = validate_against_corpora([row for row in current_rows if row_error(row) is None], corpora)[1]
    current = {address(row) for row in current_rows if has_valid_index(row)}
    candidates_path = os.path.join(folder, CANDIDATES_TABLE)
    candidates = read_table(candidates_path, "candidates")[1] if os.path.exists(candidates_path) else []
    findings, counts = [], {}
    for name in names:
        detector = WITHDRAWN_NAME_RE.match(name).group(1)
        try:
            header, rows = read_table(os.path.join(folder, name), "corrections")
        except ValueError as error:
            findings.append(name + ": " + str(error))
            continue
        counts[name] = len(rows)
        seen = collections.Counter()
        inputs = []
        for line in header:
            key = line[2:].split(" = ", 1)[0] if line.startswith("# ") and " = " in line else None
            if key in WITHDRAWN_HEADER_KEYS:
                seen[key] += 1
                value = line[len("# %s = " % key):]
                if key == "kind" and value != WITHDRAWN_KIND:
                    findings.append("%s: '# kind' is %r, expected %s" % (name, value, WITHDRAWN_KIND))
                if key == "license" and not value.startswith("CC0-1.0 "):
                    findings.append("%s: the '# license' line does not start with 'CC0-1.0 '" % name)
                if key == "detector" and value != detector:
                    findings.append("%s: '# detector' is %r, expected %s" % (name, value, detector))
                if key == "reason" and not value.strip():
                    findings.append("%s: the '# reason' line is empty" % name)
            elif key == "input" or is_input_line(line):
                inputs.append(line)
            else:
                findings.append("%s: unknown metadata line %r" % (name, line))
        findings.extend("%s: the '# %s' line is %s" % (name, key, "missing" if not seen[key] else "repeated")
                        for key in WITHDRAWN_HEADER_KEYS if seen[key] != 1)
        # The input provenance is checked as for adjudicated.tsv: one well-formed line per corpus file, no unknown or repeated
        # file, and (while the corpora are pending) the hash of the corpus file as it is.
        listed, input_findings = input_lines(name, inputs)
        findings.extend(input_findings)
        findings.extend(input_state_findings(name, listed, data_dir, state))
        findings.extend(name + ": " + problem for problem in table_errors(rows))
        valid = [row for row in rows if row_error(row) is None]
        covered = {address(row) for row in detector_rows(valid, candidates, detector)}
        for row in valid:
            where = "%s %s#%s" % (name, row.sent_id, row.token_index)
            if not ADJUDICATED_PROVENANCE_RE.match(row.provenance):
                findings.append("%s: provenance %r is not llm:opus+sol:<batch>" % (where, row.provenance))
            if adjudicated_row_kind(row) is None:
                findings.append("%s: the row is neither a tag change, a disputed mark nor a flag change" % where)
            if address(row) not in covered:
                findings.append("%s: no candidate of the detector %s covers the token" % (where, detector))
            if address(row) in current:
                findings.append("%s: the token is also in a current correction table" % where)
        errors, withdrawn_state = validate_against_corpora(valid, corpora)
        findings.extend(name + ": " + error for error in errors)
        if withdrawn_state not in ("pending", "empty"):
            findings.append("%s: the corpora hold the new value of the withdrawn tokens (state %s)" % (name, withdrawn_state))
    return findings, counts


def withdraw_detector(data_dir, detector, reason, write=False):
    """Move the `adjudicated.tsv` rows of one detector to `withdrawn-<detector>.tsv`: returns (kept, withdrawn) rows.

    The rows of the detector (see `detector_rows`) leave `adjudicated.tsv`, byte for byte and in key order, and become the rows
    of the new table whose header records the detector, the `reason` and the corpus hashes of `adjudicated.tsv`. Nothing else
    changes (`mechanical.tsv`, `candidates.tsv`, the header of `adjudicated.tsv`). The corpora must be in the pending state
    (apply `--reverse` first and apply again afterwards), the overlay valid, the new table must not exist, and no withdrawn
    row may replace a mechanical row (the rule would take the token back). Raises ValueError on any of these.
    """
    if not WITHDRAWN_NAME_RE.match("withdrawn-%s.tsv" % detector):
        raise ValueError("%r is not a detector id" % detector)
    if not reason.strip() or "\n" in reason or "\t" in reason:
        raise ValueError("the reason must be one non-empty line of text")
    folder = corrections_dir(data_dir)
    target = os.path.join(folder, "withdrawn-%s.tsv" % detector)
    if os.path.exists(target):
        raise ValueError("%s exists already" % os.path.basename(target))
    errors, state, _ = validate_overlay(data_dir)
    if errors:
        raise ValueError("the overlay is invalid (%d errors, first: %s)" % (len(errors), errors[0]))
    if state != "pending":
        raise ValueError("the corpora are in the %s state, expected pending (apply --reverse first)" % state)
    path = os.path.join(folder, ADJUDICATED_TABLE)
    header, rows = read_table(path, "corrections")
    candidates = read_table(os.path.join(folder, CANDIDATES_TABLE), "candidates")[1]
    withdrawn = detector_rows(rows, candidates, detector)
    chosen = {address(row) for row in withdrawn}
    kept = [row for row in rows if address(row) not in chosen]
    mechanical_path = os.path.join(folder, MECHANICAL_TABLE)
    mechanical = {address(row) for row in read_table(mechanical_path, "corrections")[1]} if os.path.exists(mechanical_path) else set()
    if chosen & mechanical:
        raise ValueError("%d withdrawn rows replace mechanical rows; the rules would take those tokens back" % len(chosen & mechanical))
    if not withdrawn:
        raise ValueError("no adjudicated row carries the detector " + detector)
    if write:
        license_line = next(line for line in header if line.startswith("# license = "))
        head = [CORPUS_LINE_WITHDRAWN % detector, license_line, "# kind = " + WITHDRAWN_KIND,
                "# columns = " + ", ".join(COLUMNS["corrections"]),
                "# generator = tools/data/corrections.py withdraw (rows taken out of adjudicated.tsv byte for byte; "
                "see data/corrections/README.md)", "# detector = " + detector, "# reason = " + reason.strip()]
        head += [line for line in header if line.startswith("# input = ")]
        write_table(target, "corrections", head, withdrawn)
        write_table(path, "corrections", header, kept)
    return kept, withdrawn


CORPUS_LINE_WITHDRAWN = ("# corpus = DemaConsulting.PartOfSpeech staged corpus corrections: adjudicated rows withdrawn for the detector %s "
                         "(provenance only, never applied)")


# ---------------------------------------------------------------- command line

def _command_validate(args):
    errors, state, counts = validate_overlay(args.data_dir)
    for name, count in counts.items():
        if name == "superseded":
            print("note: %d mechanical rows are superseded by adjudicated rows (the same token; ignored by validate and apply)" % count)
        else:
            print("%-16s %d rows" % (name, count))
    if errors:
        print("%d errors:" % len(errors))
        for message in errors[:args.limit]:
            print("  " + message)
        return 1
    print("overlay valid; corrections state: " + state)
    return 0


def _command_apply(args):
    try:
        tables = load_overlay(args.data_dir)
    except ValueError as error:
        print("refusing to apply: the overlay is malformed (%s)" % error)
        return 1
    problems = row_errors(tables["corrections"])
    if problems:
        print("refusing to apply: the correction rows are invalid (%d errors, first: %s)" % (len(problems), problems[0]))
        return 1
    errors, state = validate_against_corpora(tables["corrections"], load_corpora(args.data_dir))
    if errors:
        print("refusing to apply: the overlay does not match the corpora (%d errors, first: %s)" % (len(errors), errors[0]))
        return 1
    wanted = "applied" if args.reverse else "pending"
    if state not in (wanted, "empty"):
        print("refusing to %s: the corpora are in the %s state" % ("reverse" if args.reverse else "apply", state))
        return 1
    findings = adjudicated_findings(args.data_dir, state=state)
    if findings:
        print("refusing to %s: %s has %d findings (first: %s)" % ("reverse" if args.reverse else "apply", ADJUDICATED_TABLE, len(findings), findings[0]))
        return 1
    try:
        result = apply_to_files(tables["corrections"], args.data_dir, args.reverse, args.write)
    except ValueError as error:
        print("refusing to apply: %s" % error)
        return 1
    shown = 0
    for key, (old, new) in sorted(result.items()):
        changes = changed_lines(old, new)
        print("%s/%s: %d lines change" % (key[0], key[1], max(sum(line[0] == "+" for line in changes), sum(line[0] == "-" for line in changes))))
        for line in changes:
            if shown < args.diff:
                print(line)
                shown += 1
    print("written" if args.write else "dry run: nothing written (use --write)")
    return 0


def changed_lines(old, new):
    """The changed lines of an applied corpus file as diff lines (`-old`, `+new`), cheap on files of hundreds of thousands of lines.

    The corrections edit the token rows in place, so the body (everything after the first blank line) has the same number of
    lines and is compared line by line; only the header can gain or lose the documenting `# status flags` line, so only the
    header goes through `difflib`. A file whose body changed its length (never produced by `apply_to_lines`) is compared with
    `difflib` as a whole.
    """
    old_end = old.index("") if "" in old else len(old)
    new_end = new.index("") if "" in new else len(new)
    if len(old) - old_end != len(new) - new_end:
        return [line for line in difflib.unified_diff(old, new, "old", "new", n=0, lineterm="") if line[:1] in "+-" and line[:3] not in ("+++", "---")]
    changes = [line for line in difflib.unified_diff(old[:old_end], new[:new_end], "old", "new", n=0, lineterm="")
               if line[:1] in "+-" and line[:3] not in ("+++", "---")]
    for before, after in zip(old[old_end:], new[new_end:]):
        if before != after:
            changes.append("-" + before)
            changes.append("+" + after)
    return changes


def _command_show(args):
    tables = load_overlay(args.data_dir)
    corpora = load_corpora(args.data_dir)
    rows = [row for row in tables["corrections"] if (not args.sent_id or row.sent_id == args.sent_id)
            and (not args.rule or row.provenance == "rule:" + args.rule)]
    for row in rows[:args.limit]:
        corpus = corpora.get((row.source, row.split))
        sentence = corpus.by_id.get(row.sent_id) if corpus else None
        if sentence is None or not has_valid_index(row) or int(row.token_index) >= len(sentence["rows"]):
            print("%s/%s %s#%s %s: the row addresses no token (run `validate`)" % (row.source, row.split, row.sent_id, row.token_index, row.token))
            continue
        index = int(row.token_index)
        words = [cells[0] for cells in sentence["rows"]]
        window = words[max(0, index - 8):index] + ["[" + words[index] + "]"] + words[index + 1:index + 9]
        print("%s/%s %s#%d %s %s>%s %s | %s" % (row.source, row.split, row.sent_id, index, row.token, row.old_tag, row.new_tag,
                                                row.provenance, " ".join(window)))
    print(len(rows), "rows")
    return 0


def _command_withdraw(args):
    try:
        kept, withdrawn = withdraw_detector(args.data_dir, args.detector, args.reason, args.write)
    except ValueError as error:
        print("refusing to withdraw: %s" % error)
        return 1
    split = collections.Counter("%s/%s" % (row.source, row.split) for row in withdrawn)
    print("detector %s: %d rows leave %s (%s); %d stay" % (args.detector, len(withdrawn), ADJUDICATED_TABLE,
                                                          ", ".join("%s %d" % item for item in sorted(split.items())), len(kept)))
    print("written" if args.write else "dry run: nothing written (use --write)")
    return 0


def main(argv=None):
    parser = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    parser.add_argument("--data-dir", default=None, help="data folder (default: the repository's data/)")
    sub = parser.add_subparsers(dest="command", required=True)
    validate = sub.add_parser("validate", help="validate every overlay table against the corpora")
    validate.add_argument("--limit", type=int, default=20, help="errors to print")
    apply = sub.add_parser("apply", help="apply (or reverse) the corrections to the corpus files")
    apply.add_argument("--write", action="store_true", help="really write the corpus files (default: dry run)")
    apply.add_argument("--reverse", action="store_true", help="undo the corrections")
    apply.add_argument("--diff", type=int, default=20, help="diff lines to print")
    show = sub.add_parser("show", help="print correction rows in context")
    show.add_argument("--sent-id", default=None)
    show.add_argument("--rule", default=None)
    show.add_argument("--limit", type=int, default=20)
    withdraw = sub.add_parser("withdraw", help="move the adjudicated rows of one detector to withdrawn-<detector>.tsv (pending state only)")
    withdraw.add_argument("--detector", required=True, help="candidate rule id whose rows leave adjudicated.tsv")
    withdraw.add_argument("--reason", required=True, help="one line of text for the '# reason' header line of the withdrawn table")
    withdraw.add_argument("--write", action="store_true", help="really write the two tables (default: dry run)")
    args = parser.parse_args(argv)
    return {"validate": _command_validate, "apply": _command_apply, "show": _command_show,
            "withdraw": _command_withdraw}[args.command](args)


if __name__ == "__main__":
    sys.exit(main())

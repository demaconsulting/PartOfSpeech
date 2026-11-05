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

Three tables share the module (see data/corrections/README.md and data/CONVENTIONS.md, "Other Formats"):

* `corrections` (`mechanical.tsv`, later `llm.tsv`): the changes themselves;
* `candidates` (`candidates.tsv`): judgment cases for the adjudication, never changes;
* `exceptions` (`exceptions.tsv`): "reviewed, keep as is" decisions that the linter honors.

State-aware validation: a corrections table is valid when EVERY row matches the corpus in the pending state (token,
`old_tag`, `old_status`) or EVERY row matches in the applied state (`new_*`); a mix of both is an error. The same files stay
valid after the corrections are applied to the corpora.
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

# Corpus file of every (source, split), relative to the data folder. The STE challenge set is linted but never corrected.
CORPUS_FILES = {("open", "train"): "open/train.tsv", ("open", "dev"): "open/dev.tsv", ("open", "test"): "open/test.tsv",
                ("masc", "-"): "masc/masc.tsv", ("silver", "-"): "silver/silver.tsv"}
LINT_ONLY_FILES = {("ste", "-"): "eval/ste-challenge/test.tsv"}

CORRECTIONS_FOLDER = "corrections"
CORRECTION_TABLES = ("mechanical.tsv", "llm.tsv")  # tables of kind `corrections`, in this order; llm.tsv appears later
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


# ---------------------------------------------------------------- corpora

class Corpus:
    """One corpus file read with the shared reader: header lines, sentences and an index by sentence id."""

    def __init__(self, source, split, path):
        self.source, self.split, self.path = source, split, path
        self.header, self.sentences = common.read_corpus(path)
        self.by_id = {sentence["sent_id"]: sentence for sentence in self.sentences}


def load_corpora(data_dir=None, lint_only=False, only=None):
    """{(source, split): Corpus} of the correctable corpora (and the STE set with `lint_only`), optionally filtered.

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
    """The tables of `<data>/corrections`: {"corrections": [rows of all correction tables], "candidates": [...], "exceptions": [...]}.

    A table that does not exist is empty (llm.tsv appears only with the adjudication). Problems of the format raise ValueError.
    """
    folder = corrections_dir(data_dir)
    tables = {"corrections": [], "candidates": [], "exceptions": []}
    for name in CORRECTION_TABLES:
        path = os.path.join(folder, name)
        if os.path.exists(path):
            tables["corrections"].extend(read_table(path, "corrections")[1])
    for kind, name in (("candidates", CANDIDATES_TABLE), ("exceptions", EXCEPTIONS_TABLE)):
        path = os.path.join(folder, name)
        if os.path.exists(path):
            tables[kind] = read_table(path, kind)[1]
    return tables


def validate_overlay(data_dir=None):
    """Validate every table of a data folder against its corpora: returns (errors, state, counts)."""
    errors = []
    tables = {"corrections": [], "candidates": [], "exceptions": []}
    folder = corrections_dir(data_dir)
    counts = {}
    for name in CORRECTION_TABLES:
        path = os.path.join(folder, name)
        if os.path.exists(path):
            try:
                rows = read_table(path, "corrections")[1]
            except ValueError as error:
                errors.append(str(error))
                continue
            errors.extend(name + ": " + problem for problem in table_errors(rows))
            tables["corrections"].extend(rows)
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
    # The address of a token is unique across all correction tables (a second row for the same token is a conflict).
    owners = {}
    for row in tables["corrections"]:
        if row_error(row) is None:
            first = owners.setdefault(address(row), row)
            if first is not row:
                same = first[5:10] == row[5:10]  # token, old and new tag, old and new status
                errors.append("across tables: %s %s#%s" % ("duplicate" if same else "conflict", row.sent_id, row.token_index))
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
    return errors, state, counts


# ---------------------------------------------------------------- command line

def _command_validate(args):
    errors, state, counts = validate_overlay(args.data_dir)
    for name, count in counts.items():
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
    try:
        result = apply_to_files(tables["corrections"], args.data_dir, args.reverse, args.write)
    except ValueError as error:
        print("refusing to apply: %s" % error)
        return 1
    shown = 0
    for key, (old, new) in sorted(result.items()):
        changed = sum(1 for a, b in zip(old, new) if a != b) + abs(len(new) - len(old))
        print("%s/%s: %d lines change" % (key[0], key[1], changed))
        for line in difflib.unified_diff(old, new, "old", "new", n=0, lineterm=""):
            if shown < args.diff:
                print(line)
                shown += 1
    print("written" if args.write else "dry run: nothing written (use --write)")
    return 0


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
    args = parser.parse_args(argv)
    return {"validate": _command_validate, "apply": _command_apply, "show": _command_show}[args.command](args)


if __name__ == "__main__":
    sys.exit(main())

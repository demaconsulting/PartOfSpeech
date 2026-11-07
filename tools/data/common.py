"""Shared helpers of the data tools (Python 3, standard library only).

Everything the import, lexicon, coverage, leakage and manifest scripts have in common lives here:
repository paths, the 17 Universal POS tags, the shared corpus block format, writers that always produce
UTF-8 without BOM and LF newlines, SHA-256 helpers, a pinned download helper, the sentence key used by the
leakage check, the clitic splitter of the corpus tokenization, and the lexicon reader with the token-shape
rules that define "allowed tags" (the same rules the coverage check and the phase 2 decoder use).
"""
import hashlib
import os
import re
import unicodedata
import urllib.request

ROOT = os.path.dirname(os.path.dirname(os.path.dirname(os.path.abspath(__file__))))
DATA = os.path.join(ROOT, "data")
TOOLS = os.path.join(ROOT, "tools", "data")
PINS = os.path.join(TOOLS, "pins")
CACHE = os.path.join(TOOLS, "cache")

# The WordSense commit every WordSense-derived input is pinned to.
WORDSENSE_COMMIT = "6530de1928baf30f9a14446a54bfb5dd5f67b41c"
WORDSENSE_RAW = "https://raw.githubusercontent.com/demaconsulting/WordSense/" + WORDSENSE_COMMIT + "/"

UPOS = ("ADJ", "ADP", "ADV", "AUX", "CCONJ", "DET", "INTJ", "NOUN", "NUM", "PART", "PRON", "PROPN", "PUNCT",
        "SCONJ", "SYM", "VERB", "X")
UPOS_SET = frozenset(UPOS)


def rel(path):
    """Repository-relative path with forward slashes."""
    return os.path.relpath(path, ROOT).replace("\\", "/")


def sha256_file(path):
    digest = hashlib.sha256()
    with open(path, "rb") as handle:
        for chunk in iter(lambda: handle.read(1 << 20), b""):
            digest.update(chunk)
    return digest.hexdigest()


def sha256_bytes(data):
    return hashlib.sha256(data).hexdigest()


def write_text(path, lines):
    """Write lines (without newline characters) as UTF-8, no BOM, LF, with a final newline."""
    os.makedirs(os.path.dirname(path), exist_ok=True)
    with open(path, "w", encoding="utf-8", newline="\n") as handle:
        for line in lines:
            if "\n" in line or "\r" in line:
                raise ValueError("line contains a newline character: " + repr(line[:60]))
            handle.write(line)
            handle.write("\n")


def read_lines(path):
    """Lines of a UTF-8 file without newline characters (a BOM or a carriage return is an error)."""
    with open(path, "r", encoding="utf-8", newline="") as handle:
        text = handle.read()
    if text.startswith("\ufeff"):
        raise ValueError(path + ": BOM")
    if "\r" in text:
        raise ValueError(path + ": carriage return")
    lines = text.split("\n")
    if lines and lines[-1] == "":
        lines.pop()
    return lines


def nfc(text):
    return unicodedata.normalize("NFC", text)


def fetch(url, dest, sha256=None):
    """Download url to dest (a git-ignored cache path) unless dest exists with the right hash; verify the hash."""
    if os.path.exists(dest) and (sha256 is None or sha256_file(dest) == sha256):
        return dest
    os.makedirs(os.path.dirname(dest), exist_ok=True)
    request = urllib.request.Request(url, headers={"User-Agent": "PartOfSpeech-data-tools"})
    with urllib.request.urlopen(request, timeout=300) as response:
        payload = response.read()
    if sha256 is not None and sha256_bytes(payload) != sha256:
        raise RuntimeError("SHA-256 mismatch for " + url + ": got " + sha256_bytes(payload) + ", expected " + sha256)
    temp = dest + ".part"
    with open(temp, "wb") as handle:
        handle.write(payload)
    os.replace(temp, dest)
    return dest


def read_pins(name):
    """Pin table: tab separated, first column the key (a path), the other columns its values."""
    pins = {}
    for line in read_lines(os.path.join(PINS, name)):
        if line and not line.startswith("#"):
            cells = line.split("\t")
            pins[cells[0]] = cells[1:]
    return pins


def wordsense_file(relpath, source_dir, sha256):
    """Path of a pinned WordSense file: from a local checkout (hash verified) or downloaded into the cache."""
    if source_dir:
        path = os.path.join(source_dir, *relpath.split("/"))
        actual = sha256_file(path)
        if actual != sha256:
            raise RuntimeError(relpath + ": SHA-256 " + actual + " does not match the pin " + sha256
                               + " (is the checkout at commit " + WORDSENSE_COMMIT + "?)")
        return path
    return fetch(WORDSENSE_RAW + relpath, os.path.join(CACHE, "wordsense", *relpath.split("/")), sha256)


# ---------------------------------------------------------------- sentence keys (leakage check)

def key(text):
    """Lower-case alphanumerics of a text: the normalized form that sentences are compared in.

    Every key is compared, whatever its length; only a text without any letter or digit has an empty key, which
    is never a match (see leakage.py).
    """
    return "".join(ch for ch in nfc(text).lower() if ch.isalnum())


# ---------------------------------------------------------------- excluded sources (fail-closed licensing exclusions)

EXCLUDED_SOURCES_COLUMNS = ("source_id", "url", "reason", "decided")


def normalize_url(url):
    """host (lower case, no `www.`) and path (no query, fragment or trailing slash); the empty string for a text that is no URL."""
    match = re.match(r"^[a-z][a-z0-9+.-]*://([^/?#]+)([^?#]*)", url.strip(), re.I)
    if not match:
        return ""
    host = match.group(1).lower()
    host = host[4:] if host.startswith("www.") else host
    return host + match.group(2).rstrip("/")


def read_excluded_table(path):
    """[(source_id, url, reason, decided)] of a committed `excluded-sources.tsv`; a missing or malformed file is a ValueError.

    The header is `source_id url reason decided` (tab separated), every cell of every row is filled, `url` is an http(s) URL and
    `decided` is a `YYYY-MM-DD` date. The table is the fail-closed record of documents the project must not use.
    """
    if not os.path.exists(path):
        raise ValueError("missing exclusion table: " + os.path.basename(os.path.dirname(path)) + "/" + os.path.basename(path))
    lines = [line for line in read_lines(path) if line.strip() != ""]
    if not lines or tuple(lines[0].split("\t")) != EXCLUDED_SOURCES_COLUMNS:
        raise ValueError("%s: the header must be %s" % (path, "\\t".join(EXCLUDED_SOURCES_COLUMNS)))
    rows = []
    for number, line in enumerate(lines[1:], 2):
        cells = line.split("\t")
        if len(cells) != len(EXCLUDED_SOURCES_COLUMNS) or not all(c.strip() for c in cells):
            raise ValueError("%s, line %d: every row needs source_id, url, reason and decided" % (path, number))
        if not normalize_url(cells[1]) or not re.fullmatch(r"\d{4}-\d{2}-\d{2}", cells[3]):
            raise ValueError("%s, line %d: url must be an http(s) URL and decided a YYYY-MM-DD date" % (path, number))
        rows.append(tuple(cells))
    return rows


def excluded_lookup(rows):
    """{("id", source_id) | ("url", normalized url): source_id} over rows of `read_excluded_table`."""
    lookup = {}
    for source_id, url, _, _ in rows:
        lookup[("id", source_id)] = source_id
        lookup[("url", normalize_url(url))] = source_id
    return lookup


def excluded_match(lookup, source_id, url=""):
    """The excluded source id that a document's id or URL matches, else None."""
    if ("id", source_id) in lookup:
        return lookup[("id", source_id)]
    normalized = normalize_url(url) if url else ""
    return lookup.get(("url", normalized)) if normalized else None


# ---------------------------------------------------------------- corpus block format

def check_row(origin, line_number, cells):
    """Validate one token row of the shared corpus format exactly like the trainer's reader (CorpusReader.cs).

    The same grammar and the same checks in the same order: at least form, tag and status; a known status followed
    by the flags in canonical order; a known tag unless the tag is `_` or the status is `masked`. The messages carry
    the same wording as the C# reader (origin, line number, the offending value).
    """
    where = "%s, line %d: " % (origin, line_number)
    if len(cells) < 3:
        raise ValueError(where + "a token row needs at least form, tag and status.")
    try:
        base = parse_status(cells[2])[0]
    except ValueError as error:
        raise ValueError(where + "invalid status '" + cells[2] + "' (a known status, then the flags +foreign and "
                         "+mentioned, in that order, once).") from error
    if cells[1] != "_" and base != "masked" and cells[1] not in UPOS_SET:
        raise ValueError(where + "unknown tag '" + cells[1] + "'.")


def read_corpus(path, strict=True):
    """Read the shared corpus format: header comment lines, blank line, then one block per sentence.

    Returns (header_lines, sentences). A sentence is a dict with sent_id, source_id, text, comments (all
    comment key/value pairs) and rows (lists of the tab separated cells of every token). A comment line is a
    line that starts with "# " (a token whose form is "#" is followed by a tab, never by a space).

    With `strict` (the default) every token row is validated like the trainer's reader does (see `check_row`: tag,
    status and status flags), so a malformed corpus is an error in every consumer. Only the reader of the source
    format of the open corpus (seven columns, import_open.py) passes `strict=False` and validates its own columns.
    """
    header, sentences = [], []
    block = []
    for line_number, line in enumerate(read_lines(path) + [""], 1):
        if line != "":
            block.append((line_number, line))
            continue
        if not block:
            continue
        comments = {}
        rows = []
        for number, item in block:
            if item.startswith("# "):
                name, _, value = item[2:].partition(" = ")
                comments[name] = value
            else:
                cells = item.split("\t")
                if strict:
                    check_row(path, number, cells)
                rows.append(cells)
        if "sent_id" in comments:
            sentences.append({"sent_id": comments["sent_id"], "source_id": comments.get("source_id", ""),
                              "text": comments.get("text", ""), "comments": comments, "rows": rows})
        elif not sentences and not rows:
            header = [item for _, item in block]
        else:
            raise ValueError(path + ": block without sent_id")
        block = []
    return header, sentences


# ---------------------------------------------------------------- corpus status cell: status and flags

# The status cell of a token row is `status` or `status+flag+flag`: one status of the file's vocabulary, then the
# flags, each after a plus sign, in alphabetical order. A cell without a plus sign (every committed file today) has no
# flags. Flags are token-level facts about the word and never change the tag (see data/CONVENTIONS.md, "Mentioned Words").
STATUS_BASES = ("unanimous", "disputed", "unchecked", "ptb", "silver", "masked")
STATUS_FLAGS = ("foreign", "mentioned")  # canonical order


def parse_status(cell):
    """Split a status cell `status[+flag...]` into (status, flags tuple); the flags come back in canonical order.

    Raises ValueError for an unknown status, an unknown flag, a duplicate flag or flags out of canonical order, so
    that a file has exactly one spelling of every status cell.
    """
    base, *flags = cell.split("+")
    if base not in STATUS_BASES:
        raise ValueError("unknown status " + repr(cell))
    for flag in flags:
        if flag not in STATUS_FLAGS:
            raise ValueError("unknown status flag " + repr(flag) + " in " + repr(cell))
    if flags != sorted(set(flags), key=STATUS_FLAGS.index):
        raise ValueError("status flags must be unique and in the order " + "+".join(STATUS_FLAGS) + ": " + repr(cell))
    return base, tuple(flags)


def base_status(cell):
    """The status of a status cell without its flags (`disputed+mentioned` gives `disputed`)."""
    return cell.partition("+")[0]


def format_status(base, flags=()):
    """The status cell of a status and its flags (flags are put in canonical order, duplicates are an error)."""
    ordered = sorted(flags, key=STATUS_FLAGS.index)
    if len(set(ordered)) != len(ordered):
        raise ValueError("duplicate status flag in " + repr(flags))
    return "+".join([base] + ordered)


def with_flag_documentation(header, sentences):
    """The header lines, plus a line that documents the status flags when (and only when) a row carries one.

    Files without flags keep their header byte for byte (the committed files have no flags, and their hashes are
    pinned by the evaluation record); a file that does carry flags says what they are, right after its status line.
    """
    flagged = any("+" in row[2] for sentence in sentences for row in sentence["rows"] if len(row) > 2)
    if not flagged:
        return list(header)
    note = ("# status flags = a status may be followed by +foreign (word of another language) and +mentioned (word talked "
            "about, not used), in that order; a flag never changes the tag (data/CONVENTIONS.md)")
    out = []
    for line in header:
        out.append(line)
        if line.startswith("# status = "):
            out.append(note)
    return out


def write_corpus(path, header, sentences):
    """Write the shared corpus format; sentences are dicts with sent_id, source_id, text and rows."""
    lines = list(header) + [""]
    for sentence in sentences:
        lines.append("# sent_id = " + sentence["sent_id"])
        lines.append("# source_id = " + sentence["source_id"])
        lines.append("# text = " + sentence["text"])
        lines.extend(sentence.get("extra", []))
        for row in sentence["rows"]:
            lines.append("\t".join(row))
        lines.append("")
    if lines[-1] == "":
        lines.pop()  # blocks are separated by one blank line; the file ends with the last token row and one LF
    write_text(path, lines)


# ---------------------------------------------------------------- corpus tokenization: clitics

IRREGULAR_NT = {"can't": ("ca", "n't"), "won't": ("wo", "n't"), "shan't": ("sha", "n't"), "ain't": ("ai", "n't"),
                "cannot": ("can", "not")}
NT_RE = re.compile(r"^([A-Za-z]+)(n't)$", re.IGNORECASE)
CLITIC_RE = re.compile(r"^([A-Za-z][A-Za-z']*)('(?:s|re|ve|ll|d|m))$", re.IGNORECASE)


def split_clitics(token):
    """Split one token of the reference tokenization into the tokens of the corpus convention.

    `do` + `n't`, `ca` + `n't`, `I` + `'ve`, `John` + `'s`; tokens that are not contractions are returned unchanged.
    The pieces always concatenate back to the token.
    """
    low = token.lower()
    if low in IRREGULAR_NT:
        first, second = IRREGULAR_NT[low]
        return [token[:len(first)], token[len(first):]]
    match = NT_RE.match(token)
    if match:
        return [match.group(1), match.group(2)]
    match = CLITIC_RE.match(token)
    if match:
        return [match.group(1), match.group(2)]
    return [token]


# ---------------------------------------------------------------- lexicon and token-shape rules

def read_lexicon(path):
    """lexicon.tsv: spelling TAB TAG TAG ... (comment lines start with '# '). Returns {spelling: frozenset(tags)}."""
    lexicon = {}
    for line in read_lines(path):
        if not line or line.startswith("# "):
            continue
        spelling, _, tags = line.partition("\t")
        lexicon[spelling] = frozenset(tags.split(" "))
    return lexicon


def has_letter_or_digit(token):
    return any(ch.isalnum() for ch in token)


def allowed_tags(token, lexicon):
    """Tags the lexicon and the token-shape rules allow for a token.

    Normative rules (the phase 2 decoder must implement the same):
      1. the lower-case token is not in the lexicon: all 17 tags are allowed (unknown word);
      2. otherwise the tags listed for the lower-case token;
      3. a token with no letter or digit also allows PUNCT, SYM, X;
      4. a token containing a digit also allows NUM, SYM, X, NOUN, PROPN, ADJ;
      5. a token whose first character is an upper-case letter also allows PROPN and X.
    """
    entry = lexicon.get(token.lower())
    if entry is None:
        return UPOS_SET
    tags = set(entry)
    if not has_letter_or_digit(token):
        tags.update(("PUNCT", "SYM", "X"))
    if any(ch.isdigit() for ch in token):
        tags.update(("NUM", "SYM", "X", "NOUN", "PROPN", "ADJ"))
    if token[:1].isupper():
        tags.update(("PROPN", "X"))
    return tags


def is_number(token):
    """A cardinal number written with digits: 3, 1990, 3.5, 1,000, 10:30."""
    return re.fullmatch(r"[+-]?\d+(?:[.,:/]\d+)*", token) is not None

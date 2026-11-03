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


# ---------------------------------------------------------------- corpus block format

def read_corpus(path):
    """Read the shared corpus format: header comment lines, blank line, then one block per sentence.

    Returns (header_lines, sentences). A sentence is a dict with sent_id, source_id, text, comments (all
    comment key/value pairs) and rows (lists of the tab separated cells of every token). A comment line is a
    line that starts with "# " (a token whose form is "#" is followed by a tab, never by a space).
    """
    header, sentences = [], []
    block = []
    for line in read_lines(path) + [""]:
        if line != "":
            block.append(line)
            continue
        if not block:
            continue
        comments = {}
        rows = []
        for item in block:
            if item.startswith("# "):
                name, _, value = item[2:].partition(" = ")
                comments[name] = value
            else:
                rows.append(item.split("\t"))
        if "sent_id" in comments:
            sentences.append({"sent_id": comments["sent_id"], "source_id": comments.get("source_id", ""),
                              "text": comments.get("text", ""), "comments": comments, "rows": rows})
        elif not sentences and not rows:
            header = list(block)
        else:
            raise ValueError(path + ": block without sent_id")
        block = []
    return header, sentences


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

"""License hygiene check of data/: no share-alike or non-commercial material, no UD English-EWT, no SemCor or Brown.

  python tools/data/check_licenses.py

Checks (exit code 1 on any finding):
  1. no *.conllu file anywhere in the repository data and tools folders (outside the git-ignored cache), and no path under
     data/ containing semcor, brown, ewt or ud-ewt except the fetch-only documentation data/eval/ud-ewt/README.md;
  2. the source tables (open, gold v2, silver, shapes-dev and, when it exists, the hard tranche's hard-sources.tsv, and the imperative source) contain no row that mentions a share-alike, no-derivatives or
     non-commercial license, a Creative Commons BY-family license other than CC0, or Wikipedia; the MASC source table
     (a CC BY 3.0 US corpus) must record exactly that license for every document;
  3. every data folder README declares `License: <id>` with an id from the allowed set (the staged corrections folder included);
  4. the staged corrections (data/corrections/*.tsv) declare CC0-1.0 for the labels and name the MASC attribution; every row
     belongs to a document that a source table lists (for MASC: with the CC BY 3.0 US class, because the token forms and
     context excerpts of MASC rows are MASC text); and no label, id, reason or provenance cell mentions a restricted license
     (the token, hint and context cells are corpus text and are covered by the document check). The corrections README must link
     the MASC attribution file, which must exist;
  5. the hard-shape development set has a required, well-formed exclusion record eval/shapes-dev/excluded-sources.tsv (missing, zero-byte
     or headerless is a finding; header-only is valid), and neither its sources.tsv nor the source_id of any sentence of its dev.tsv
     matches an excluded id or normalized URL; the same for the hard tranche (hard-sources.tsv and hard.tsv), which shares the record.
  6. fail closed: every source table and corpus above must exist (missing is a finding, not a skip or a traceback); the two files of the hard
     tranche are required while leakage.HARD_TRANCHE_REQUIRED is True and skipped when missing while it is False.
"""
import os
import re
import sys

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import common
import leakage

FORBIDDEN_TEXT = re.compile(r"share-?alike|by-sa|by-nc|by-nd|non-?commercial|no-?derivat|cc by|wikipedia|creativecommons\.org/licenses/by", re.I)
FORBIDDEN_PATH = re.compile(r"semcor|brown|ewt", re.I)
ALLOWED_LICENSES = {"CC0-1.0", "CC-BY-3.0-US", "CC-BY-4.0", "Public-Domain", "MIT"}
SOURCE_TABLES = ("open/sources.tsv", "eval/gold-v2/sources.tsv", "silver/sources.tsv", "eval/shapes-dev/sources.tsv",
                 "eval/shapes-dev/hard-sources.tsv", "imperative/sources.tsv")
HARD_SOURCES_TABLE = "eval/shapes-dev/hard-sources.tsv"   # scanned when it exists; a missing one fails once leakage.HARD_TRANCHE_REQUIRED is True
HARD_CORPUS = "eval/shapes-dev/hard.tsv"                  # likewise
HARD_FILES = (HARD_SOURCES_TABLE, HARD_CORPUS)            # optional only while leakage.HARD_TRANCHE_REQUIRED is False
# Every table and corpus whose absence is a finding (the hard tranche's two files only while the tranche is required); each consumer below
# skips a file that is missing, because required_file_findings reports it once.
REQUIRED_FILES = SOURCE_TABLES + ("masc/sources.tsv", "eval/shapes-dev/dev.tsv", HARD_CORPUS)
MASC_LICENSE = "CC BY 3.0 US"
# The hard-shape development set (both tranches) and the imperative training source take no FAA material (host, publisher or handbook); see
# data/eval/shapes-dev/README.md and data/imperative/README.md.
FAA_TEXT = re.compile(r"faa\.gov|\bFAA\b|Federal Aviation", re.I)
FAA_TABLES = ("eval/shapes-dev/sources.tsv", HARD_SOURCES_TABLE, "imperative/sources.tsv")
READMES = ("open", "masc", "silver", "imperative", "eval/shapes", "eval/shapes-dev", "eval/gold-v2", "eval/ste-challenge", "lexicon", "corrections")
ALLOWED_PATHS = {"data/eval/ud-ewt/README.md"}
CORRECTION_SOURCES = {"open": "open/sources.tsv", "masc": "masc/sources.tsv", "silver": "silver/sources.tsv"}
CORRECTION_TEXT_COLUMNS = frozenset(("token", "hint", "context"))  # corpus text: covered by the check of the document
MASC_ATTRIBUTION_LINK = "](../masc/ATTRIBUTION.md)"
MASC_ATTRIBUTION_FILE = "masc/ATTRIBUTION.md"


def required_file_findings(base=None):
    """Fail closed: a missing required source table or corpus is a finding, never a skip and never a traceback.

    The two files of the hard tranche (hard-sources.tsv, hard.tsv) are required while leakage.HARD_TRANCHE_REQUIRED is True and optional
    (skipped when missing, scanned when present) while it is False. The READMEs are checked separately in main."""
    base = base or common.DATA
    findings = []
    for table in REQUIRED_FILES:
        if table in HARD_FILES and not leakage.HARD_TRANCHE_REQUIRED:
            continue
        if not os.path.isfile(os.path.join(base, *table.split("/"))):
            findings.append("missing required file: data/" + table)
    return findings


def masc_findings():
    """MASC is CC BY 3.0 US as a whole, so its source table is checked for exactly that license class (and for no
    other restricted text) instead of being scanned with FORBIDDEN_TEXT, which refuses every CC BY license."""
    findings = []
    rows = 0
    restricted = re.compile(r"share-?alike|by-sa|by-nc|by-nd|non-?commercial|no-?derivat|wikipedia", re.I)
    masc_table = os.path.join(common.DATA, "masc", "sources.tsv")
    if not os.path.isfile(masc_table):
        return findings                                           # reported by required_file_findings
    for line in common.read_lines(masc_table):
        if not line or line.startswith("#"):
            continue
        rows += 1
        cells = line.split("\t")
        if len(cells) != 5 or cells[2] != MASC_LICENSE or not cells[0].startswith("masc:written/"):
            findings.append("masc/sources.tsv: unexpected row: " + line[:80])
        elif restricted.search(line):
            findings.append("masc/sources.tsv: row mentions a restricted license: " + line[:80])
    print("%-28s %6d rows scanned" % ("masc/sources.tsv", rows))
    return findings


def listed_documents(base):
    """{source: set of document ids} from the source tables of open, masc and silver; a MASC document counts only with the
    corpus license class, so a MASC row of the corrections cannot name a document of another license."""
    documents = {}
    for source, table in CORRECTION_SOURCES.items():
        ids = set()
        table_path = os.path.join(base, *table.split("/"))
        for line in (common.read_lines(table_path) if os.path.isfile(table_path) else ()):   # a missing table: required_file_findings
            if not line or line.startswith("#"):
                continue
            cells = line.split("\t")
            if cells[0] == "source_id":  # the un-commented header row of open/sources.tsv
                continue
            if source != "masc" or (len(cells) > 2 and cells[2] == MASC_LICENSE):
                ids.add(cells[0])
        documents[source] = ids
    return documents


def corrections_findings(base=None):
    """License findings of the staged corrections: the declaration and attribution, and every row.

    The rows are labels and addresses (CC0-1.0) except the token forms and the context excerpts, which are corpus text. An
    excerpt is allowed only for a document that a source table lists with an allowed license (MASC: CC BY 3.0 US, attribution
    in masc/ATTRIBUTION.md, which the README of the folder links)."""
    base = base or common.DATA
    folder = os.path.join(base, "corrections")
    findings = []
    if not os.path.isdir(folder):
        return ["missing folder: data/corrections"]
    if not os.path.exists(os.path.join(base, *MASC_ATTRIBUTION_FILE.split("/"))):
        findings.append("missing MASC attribution: data/" + MASC_ATTRIBUTION_FILE)
    readme = os.path.join(folder, "README.md")
    if not os.path.exists(readme):
        findings.append("missing README: data/corrections/README.md")
    else:
        with open(readme, encoding="utf-8") as handle:
            if MASC_ATTRIBUTION_LINK not in handle.read():
                findings.append("data/corrections/README.md: does not link the MASC attribution " + MASC_ATTRIBUTION_LINK)
    documents = listed_documents(base)
    for name in sorted(os.listdir(folder)):
        if not name.endswith(".tsv"):
            continue
        label = "corrections/" + name
        columns = None
        licenses = []
        rows = 0
        for line in common.read_lines(os.path.join(folder, name)):
            if line.startswith("# license = "):
                licenses.append(line)
            elif line.startswith("# columns = "):
                columns = [cell.strip() for cell in line[len("# columns = "):].split(",")]
            elif line and not line.startswith("#"):
                if columns is None or "source" not in columns or "source_id" not in columns:
                    findings.append("%s: a row before the '# columns' line or without source columns" % label)
                    break
                rows += 1
                cells = line.split("\t")
                if len(cells) != len(columns):
                    findings.append("%s: row %d has %d cells, expected %d" % (label, rows, len(cells), len(columns)))
                    continue
                row = dict(zip(columns, cells))
                if row["source_id"] not in documents.get(row["source"], ()):
                    findings.append("%s: row %d: the document %s is not listed (with an allowed license) for source %s"
                                    % (label, rows, row["source_id"], row["source"]))
                for column, cell in row.items():
                    if column not in CORRECTION_TEXT_COLUMNS and FORBIDDEN_TEXT.search(cell):
                        findings.append("%s: row %d: column %s mentions a restricted license: %s" % (label, rows, column, cell[:60]))
        if len(licenses) != 1 or "CC0-1.0" not in licenses[0] or MASC_LICENSE not in licenses[0] or MASC_ATTRIBUTION_FILE not in licenses[0]:
            findings.append("%s: needs one '# license' line with CC0-1.0, %s and %s" % (label, MASC_LICENSE, MASC_ATTRIBUTION_FILE))
        print("%-28s %6d rows scanned" % (label, rows))
    return findings


def excluded_source_findings(base=None):
    """Findings of the fail-closed exclusion record data/eval/shapes-dev/excluded-sources.tsv.

    The record is required (the hard-shape development set is a required data set): a missing folder or file, a zero-byte file, a file
    without the header and a malformed row are findings; a file that holds only the header is valid (nothing excluded). No row of the
    sources.tsv and hard-sources.tsv and no `source_id` of the dev.tsv and hard.tsv may have an id or a normalized URL that the record lists."""
    base = base or common.DATA
    folder = os.path.join(base, "eval", "shapes-dev")
    try:
        lookup = common.excluded_lookup(common.read_excluded_table(os.path.join(folder, "excluded-sources.tsv")))
    except ValueError as error:
        return ["eval/shapes-dev/excluded-sources.tsv: " + str(error)]
    findings = []
    for name in ("sources.tsv", "hard-sources.tsv"):
        path = os.path.join(folder, name)
        if os.path.isfile(path):
            for line in common.read_lines(path):
                cells = line.split("\t")
                if not line or line.startswith("#") or cells[0] == "source_id":
                    continue
                hit = common.excluded_match(lookup, cells[0], cells[3] if len(cells) > 3 else "")
                if hit:
                    findings.append("eval/shapes-dev/%s: row %s uses the excluded source %s" % (name, cells[0], hit))
    for name in ("dev.tsv", "hard.tsv"):
        path = os.path.join(folder, name)
        if os.path.isfile(path):
            for source_id in sorted({s["source_id"] for s in common.read_corpus(path)[1]}):
                hit = common.excluded_match(lookup, source_id)
                if hit:
                    findings.append("eval/shapes-dev/%s: sentences of %s use the excluded source %s" % (name, source_id, hit))
    return findings


def main():
    findings = []
    for top in (common.DATA, common.TOOLS):
        for folder, folders, files in os.walk(top):
            folders[:] = [d for d in folders if d not in ("cache", "__pycache__")]
            for name in files:
                relative = common.rel(os.path.join(folder, name))
                if name.endswith(".conllu"):
                    findings.append("conllu file: " + relative)
                if top == common.DATA and relative not in ALLOWED_PATHS and FORBIDDEN_PATH.search(relative):
                    findings.append("restricted corpus name in path: " + relative)
    findings.extend(required_file_findings())
    for table in SOURCE_TABLES:
        path = os.path.join(common.DATA, *table.split("/"))
        if not os.path.isfile(path):
            continue        # required: reported by required_file_findings; optional (hard tranche while not required): skipped
        rows = 0
        for line in common.read_lines(path):
            if line and not line.startswith("#"):
                rows += 1
                match = FORBIDDEN_TEXT.search(line)
                if match:
                    findings.append("%s: row mentions '%s': %s" % (table, match.group(0), line[:80]))
                faa = FAA_TEXT.search(line) if table in FAA_TABLES else None
                if faa:
                    findings.append("%s: row mentions FAA material ('%s'): %s" % (table, faa.group(0), line[:80]))
        print("%-28s %6d rows scanned" % (table, rows))
    findings.extend(masc_findings())
    findings.extend(excluded_source_findings())
    findings.extend(corrections_findings())
    pattern = re.compile(r"^License: (\S+)", re.M)
    for folder in READMES:
        path = os.path.join(common.DATA, *folder.split("/"), "README.md")
        if not os.path.exists(path):
            findings.append("missing README: data/%s/README.md" % folder)
            continue
        with open(path, encoding="utf-8") as handle:
            found = pattern.findall(handle.read())
        if not found or not set(found) <= ALLOWED_LICENSES:
            findings.append("data/%s/README.md: missing or unknown 'License:' line %s" % (folder, found))
    for finding in findings:
        print("FINDING:", finding)
    if findings:
        sys.exit(1)
    print("license hygiene check passed")


if __name__ == "__main__":
    main()

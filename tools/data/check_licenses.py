"""License hygiene check of data/: no share-alike or non-commercial material, no UD English-EWT, no SemCor or Brown.

  python tools/data/check_licenses.py

Checks (exit code 1 on any finding):
  1. no *.conllu file anywhere in the repository data and tools folders (outside the git-ignored cache), and no path under
     data/ containing semcor, brown, ewt or ud-ewt except the fetch-only documentation data/eval/ud-ewt/README.md;
  2. the source tables (open, gold v2, silver) contain no row that mentions a share-alike, no-derivatives or
     non-commercial license, a Creative Commons BY-family license other than CC0, or Wikipedia;
  3. every data folder README declares `License: <id>` with an id from the allowed set.
"""
import os
import re
import sys

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import common

FORBIDDEN_TEXT = re.compile(r"share-?alike|by-sa|by-nc|by-nd|non-?commercial|no-?derivat|cc by|wikipedia|creativecommons\.org/licenses/by", re.I)
FORBIDDEN_PATH = re.compile(r"semcor|brown|ewt", re.I)
ALLOWED_LICENSES = {"CC0-1.0", "CC-BY-3.0-US", "CC-BY-4.0", "Public-Domain", "MIT"}
SOURCE_TABLES = ("open/sources.tsv", "eval/gold-v2/sources.tsv", "silver/sources.tsv")
READMES = ("open", "masc", "silver", "eval/shapes", "eval/gold-v2", "eval/ste-challenge", "lexicon")
ALLOWED_PATHS = {"data/eval/ud-ewt/README.md"}


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
    for table in SOURCE_TABLES:
        path = os.path.join(common.DATA, *table.split("/"))
        rows = 0
        for line in common.read_lines(path):
            if line and not line.startswith("#"):
                rows += 1
                match = FORBIDDEN_TEXT.search(line)
                if match:
                    findings.append("%s: row mentions '%s': %s" % (table, match.group(0), line[:80]))
        print("%-28s %6d rows scanned" % (table, rows))
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

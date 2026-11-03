"""Write (or verify) data/MANIFEST.tsv: size, SHA-256 and record counts of every committed data file.

  python tools/data/make_manifest.py            # write data/MANIFEST.tsv
  python tools/data/make_manifest.py --verify   # compare the files on disk with the manifest (exit code 1 on a difference)

Covered: every file under data/ except MANIFEST.tsv itself and Markdown files (documentation is not data and may be
reformatted by the repository's Markdown fixer). Files are hashed as committed: LF bytes, so run
normalize_eol.py first if a file was written with CRLF. Git-ignored files (silver-faa.tsv) are skipped.

Columns: path (relative to data/) <TAB> bytes <TAB> sha256 <TAB> lines <TAB> sentences
  lines     = number of lines that are neither empty nor comments ('# ' prefix) in a .tsv file, else -
  sentences = number of '# sent_id = ' lines in a corpus-format file, else -
"""
import argparse
import os
import sys

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import common

IGNORED = {"data/silver/silver-faa.tsv"}


def entries():
    rows = []
    for folder, folders, files in os.walk(common.DATA):
        folders.sort()
        for name in sorted(files):
            path = os.path.join(folder, name)
            relative = common.rel(path)
            if name == "MANIFEST.tsv" or name.endswith(".md") or relative in IGNORED:
                continue
            with open(path, "rb") as handle:
                payload = handle.read()
            if b"\r" in payload or payload.startswith(b"\xef\xbb\xbf"):
                raise ValueError(relative + ": CRLF or BOM (run normalize_eol.py)")
            lines, sentences = "-", "-"
            if name.endswith(".tsv"):
                text = payload.decode("utf-8").split("\n")
                lines = str(sum(1 for line in text if line and not line.startswith("# ")))
                count = sum(1 for line in text if line.startswith("# sent_id = "))
                sentences = str(count) if count else "-"
            rows.append((relative[len("data/"):], str(len(payload)), common.sha256_bytes(payload), lines, sentences))
    return sorted(rows, key=lambda row: row[0])


def main():
    parser = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    parser.add_argument("--verify", action="store_true")
    args = parser.parse_args()
    rows = entries()
    manifest = os.path.join(common.DATA, "MANIFEST.tsv")
    if args.verify:
        recorded = [tuple(line.split("\t")) for line in common.read_lines(manifest) if line and not line.startswith("# ")]
        if recorded != rows:
            print("MANIFEST.tsv differs from the files on disk")
            for row in set(recorded) ^ set(rows):
                print("  ", row[0])
            sys.exit(1)
        print("manifest verified:", len(rows), "files")
        return
    lines = ["# Checksums of the committed data files (tools/data/make_manifest.py); Markdown files are not listed",
             "# columns = path (relative to data/), bytes, sha256, lines, sentences"]
    lines += ["\t".join(row) for row in rows]
    common.write_text(manifest, lines)
    print("wrote", len(rows), "entries")


if __name__ == "__main__":
    main()

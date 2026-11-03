"""Convert CRLF to LF (and drop a BOM) in the text files of data/ and tools/data/.

Files written on Windows by an editor can end up with CRLF in the working tree; the repository checks files out
with LF (.gitattributes) and every checksum in data/MANIFEST.tsv is over LF bytes, so run this before
make_manifest.py. It touches only files that need it and never anything under tools/data/cache/.

  python tools/data/normalize_eol.py
"""
import os
import sys

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import common

EXTENSIONS = (".py", ".md", ".tsv")


def main():
    changed = 0
    for top in (common.DATA, common.TOOLS):
        for folder, folders, files in os.walk(top):
            folders[:] = sorted(d for d in folders if d not in ("cache", "__pycache__"))
            for name in sorted(files):
                if not (name.endswith(EXTENSIONS) or name == "LICENSE"):
                    continue
                path = os.path.join(folder, name)
                with open(path, "rb") as handle:
                    payload = handle.read()
                fixed = payload.replace(b"\r\n", b"\n")
                if fixed.startswith(b"\xef\xbb\xbf"):
                    fixed = fixed[3:]
                if fixed != payload:
                    with open(path, "wb") as handle:
                        handle.write(fixed)
                    changed += 1
                    print("normalized", common.rel(path))
    print("files changed:", changed)


if __name__ == "__main__":
    main()

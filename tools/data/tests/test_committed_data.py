"""Sanity tests of the committed data files (formats, tag sets, gates). Run after the import scripts."""
import os
import sys
import unittest

sys.path.insert(0, os.path.dirname(os.path.dirname(os.path.abspath(__file__))))
import check_lexicon
import common
import make_manifest

CORPORA = (("open", "train.tsv"), ("open", "dev.tsv"), ("open", "test.tsv"), ("masc", "masc.tsv"),
           ("silver", "silver.tsv"), ("eval", "ste-challenge", "test.tsv"))


class CommittedDataTests(unittest.TestCase):
    def test_corpora_use_only_the_seventeen_tags_and_rejoin_to_text(self):
        for parts in CORPORA:
            path = os.path.join(common.DATA, *parts)
            for sentence in common.read_corpus(path)[1]:
                for row in sentence["rows"]:
                    self.assertTrue(row[1] in common.UPOS_SET or (row[1] == "_" and row[2] == "masked"), (parts, sentence["sent_id"]))
                self.assertEqual("".join(r[0] for r in sentence["rows"]), sentence["text"].replace(" ", ""), sentence["sent_id"])

    def test_manifest_matches_the_files(self):
        manifest = os.path.join(common.DATA, "MANIFEST.tsv")
        recorded = [tuple(line.split("\t")) for line in common.read_lines(manifest) if line and not line.startswith("# ")]
        self.assertEqual(recorded, make_manifest.entries())

    def test_lexicon_gate_holds_on_train_and_dev(self):
        lexicon = common.read_lexicon(os.path.join(common.DATA, "lexicon", "lexicon.tsv"))
        for name in ("train.tsv", "dev.tsv"):
            stats, _ = check_lexicon.measure(lexicon, os.path.join(common.DATA, "open", name))
            self.assertGreaterEqual(check_lexicon.pct(stats["n_covered"], stats["n_tokens"]), check_lexicon.GATE, name)

    def test_lexicon_rows_are_sorted_and_valid(self):
        previous = None
        for line in common.read_lines(os.path.join(common.DATA, "lexicon", "lexicon.tsv")):
            if line.startswith("# "):
                continue
            spelling, tags = line.split("\t")
            self.assertTrue(set(tags.split(" ")) <= common.UPOS_SET)
            self.assertEqual(tags, " ".join(sorted(tags.split(" "))))
            if previous is not None:
                self.assertLess(previous.encode("utf-8"), spelling.encode("utf-8"))
            previous = spelling


if __name__ == "__main__":
    unittest.main()

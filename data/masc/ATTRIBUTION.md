# MASC Attribution

The file `masc.tsv` and the files derived with it in this folder are adapted from the following work, which is licensed
under the Creative Commons Attribution 3.0 United States License.

- **Title:** The Manually Annotated Sub-Corpus (MASC), Penn Treebank syntax annotations of the written texts
  (release of 2026-09-25)
- **Creators:** the MASC project: Nancy Ide (Vassar College, principal investigator), Collin Baker (International
  Computer Science Institute), Christiane Fellbaum (Princeton University) and Rebecca Passonneau (Columbia University);
  the texts are drawn from the Open American National Corpus (OANC)
- **Source:** <https://anc.org/data/masc/>; the archive used is
  <https://github.com/nancyide/anc-website/releases/download/datasets-2026-09-25/penn-treebank.tgz>
- **License:** Creative Commons Attribution 3.0 United States (CC BY 3.0 US),
  <https://creativecommons.org/licenses/by/3.0/us/>
- **Citation:** Nancy Ide, Collin Baker, Christiane Fellbaum, Charles Fillmore and Rebecca Passonneau, "MASC: the
  Manually Annotated Sub-Corpus of American English", Proceedings of the Sixth International Conference on Language
  Resources and Evaluation (LREC 2008), <https://aclanthology.org/L08-1105/>

## Changes Made

The work was adapted by DEMA Consulting for the PartOfSpeech project:

- the Penn Treebank part-of-speech tags and tree positions of every token were converted to Universal POS tags by the
  rules in [../CONVENTIONS.md](../CONVENTIONS.md);
- the empty-category traces were removed, the quotation marks and dashes were normalized, and the sentences were reduced
  to those of 3 to 70 tokens;
- the spoken genre, five documents and 104 short sentences that overlap evaluation data were left out;
- the sentences are kept as their tokens joined by single spaces, each token with the derived tag and the original
  Penn tag; the texts themselves are MASC's.

The authors of MASC do not endorse this project or the converted data.

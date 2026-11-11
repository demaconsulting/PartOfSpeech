# Third-Party Notices

PartOfSpeech is Copyright (c) DEMA Consulting and is licensed under the MIT License (see `LICENSE`). The code and the
data of this project build on the third-party works below. Their attribution and license notices are reproduced here, and
this file is packed at the root of the NuGet package so that the notices travel with every copy of the package. The
data and the files derived from it are kept in the `data/` folder of the repository; compiled forms of them are embedded
in the package. The package embeds a compiled form of the lexicon `data/lexicon/lexicon.tsv` (derived from Open
English WordNet and the Moby Part-of-Speech List, with tags attested in MASC and the open corpus) as the binary resource
that restricts the tags of known words, and the trained weights of the tagger (`Resources/weights.bin`), which the
Trainer tool learned from the corpora in `data/open/`, `data/masc/` and `data/silver/`. The file `PACKAGE-LICENSE.md`,
also packed at the root of the package, is the license of the package and states which license covers which part.

No Creative Commons Attribution-ShareAlike (CC BY-SA), NonCommercial or NoDerivatives material is part of this
repository or package. The one CC BY-SA corpus that the project evaluates against is described in the last section and
is never distributed.

## The Manually Annotated Sub-Corpus (MASC)

Part-of-speech training data in `data/masc/`, the lexicon in `data/lexicon/` and the trained weights that are embedded in
the package are adapted from MASC.

- **Title:** The Manually Annotated Sub-Corpus (MASC), Penn Treebank syntax annotations of the written texts (release of
  2026-09-25)
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
- **Changes:** the work was adapted by DEMA Consulting. The Penn Treebank part-of-speech tags and tree positions of every
  token were converted to Universal POS (UPOS) tags; the empty-category traces were removed, quotation marks and dashes
  were normalized, the sentences were reduced to those of 3 to 70 tokens, and the spoken genre and a few documents and
  sentences that overlap evaluation data were left out. The converted data is not the original MASC data. The trained
  weights of the tagger (`weights.bin`) were learned in part from the converted data: they are numbers that a training
  program computed from the tags and the text of the converted sentences, and they contain no MASC text. The weights are
  provided under the terms of this license. The authors of
  MASC do not endorse this project or the converted data. The full statement is `data/masc/ATTRIBUTION.md`.

## Open English WordNet

The spelling-to-UPOS lexicon in `data/lexicon/` is derived from the word forms of Open English WordNet.

- **Title:** Open English WordNet 2025 Edition
- **Creators:** the Open English WordNet team and contributors (John P. McCrae and others)
- **Source:** <https://en-word.net> and <https://github.com/globalwordnet/english-wordnet>
- **License:** Creative Commons Attribution 4.0 International (CC BY 4.0), <https://creativecommons.org/licenses/by/4.0/>
- **Changes:** only the spellings of the entries and their inflected forms were used. They were combined with other
  sources and reduced to a table that maps a lower-case spelling to the UPOS tags it may carry. The lexicon is not the
  original Open English WordNet data, and the glosses, examples and relations are not part of it. Open English WordNet
  and its contributors do not endorse this project.

### Princeton WordNet

Open English WordNet is derived from Princeton WordNet under the WordNet License and developed further under CC BY 4.0.
Its `LICENSE.md` says: "You may share and adapt this resource providing attribution is given to both Princeton WordNet
and the Open English Wordnet team." The notice that Open English WordNet distributes as `WNDB_License.txt` is
reproduced here, as the license requires (Princeton University, <https://wordnet.princeton.edu>):

```text
This software and database is being provided to you, the LICENSEE, by
the Open English Wordnet team under the Creative Commons Attribution 4.0
International License (CC-BY 4.0).

Open English Wordnet 2023 Copyright 2023 by the Open English Wordnet team.

Permission to use, copy, modify and distribute this software and
database and its documentation for any purpose and without fee or
royalty is hereby granted, provided that you agree to comply with
the following copyright notice and statements, including the disclaimer,
and that the same appear on ALL copies of the software, database and
documentation, including modifications that you make for internal
use or for distribution.

WordNet 3.1 Copyright 2011 by Princeton University.  All rights reserved.

THIS SOFTWARE AND DATABASE IS PROVIDED "AS IS" AND PRINCETON
UNIVERSITY MAKES NO REPRESENTATIONS OR WARRANTIES, EXPRESS OR
IMPLIED.  BY WAY OF EXAMPLE, BUT NOT LIMITATION, PRINCETON
UNIVERSITY MAKES NO REPRESENTATIONS OR WARRANTIES OF MERCHANT-
ABILITY OR FITNESS FOR ANY PARTICULAR PURPOSE OR THAT THE USE
OF THE LICENSED SOFTWARE, DATABASE OR DOCUMENTATION WILL NOT
INFRINGE ANY THIRD PARTY PATENTS, COPYRIGHTS, TRADEMARKS OR
OTHER RIGHTS.

The name of Princeton University or Princeton may not be used in
advertising or publicity pertaining to distribution of the software
and/or database.  Title to copyright in this software, database and
any associated documentation shall at all times remain with
Princeton University and LICENSEE agrees to preserve same.
```

## Embedded Weights

The weights embedded in the package (`Resources/weights.bin`) are the output of the Trainer tool of this repository
(`tools/DemaConsulting.PartOfSpeech.Trainer`), which learned them from the open corpus (CC0 1.0), the written texts of
MASC (CC BY 3.0 US, see the MASC section above), the silver corpus (CC0 1.0) and the imperative source (annotations and
selection CC0 1.0, sentences from US federal government works, see below). The tool, its settings
(`training.json`) and its run log (`RUN-LOG.md`) are in the repository, and the committed corpora and the tool
reproduce the model bit for bit. Because of the MASC part, the attribution of MASC applies to the weights.

## Courtesy Notices

These sources do not require attribution, and the notices below are given as a courtesy and for provenance.

### Moby Part-of-Speech List

The lexicon also uses the part-of-speech data of the Moby Part-of-Speech List (Moby Part-of-Speech II) by Grady Ward,
<https://www.gutenberg.org/ebooks/3203>, which the author dedicated to the public domain.

### The open English part-of-speech corpus (CC0 1.0)

`data/open/` and the evaluation sets in `data/eval/` other than `shapes-dev/` are copies, reduced to Universal POS tags,
of data of the WordSense project (<https://github.com/demaconsulting/WordSense>). The annotations and the selection are
dedicated to the public domain under CC0 1.0 (<https://creativecommons.org/publicdomain/zero/1.0/>). The sentences are
not CC0 as a whole: they
are public domain in the United States (Project Gutenberg books published 1930 or earlier, and works of US federal
agencies), come from the CC0 export of Tatoeba, or are project-written. The license basis of every document is in
`data/open/sources.tsv`.

The development set `data/eval/shapes-dev/` is not a copy of WordSense data: this project built it from text of US
federal government documents (Army, Navy, Bureau of Mines, Department of Agriculture, Forest Service and NASA
publications; 17 U.S.C. 105). Its annotations and its selection are dedicated to the public domain under CC0 1.0, and
the license basis of every document is quoted in `data/eval/shapes-dev/sources.tsv`. Two NIST handbooks that were used
first were dropped because their text is not clearly authored by the US government
(`data/eval/shapes-dev/excluded-sources.tsv`); none of their text is in the set.

The hard tranche of that set (`data/eval/shapes-dev/hard.tsv`) uses the same kind of text: 99 documents of the
federal families Army, Navy, Bureau of Mines, Department of Agriculture, Forest Service and NASA, plus Bureau of Standards
(NIST), Bureau of Reclamation, Federal Civil Defense Administration, Public Health Service and NOAA publications (17
U.S.C. 105; 43 documents are also in the standard tranche). The license basis of every document is quoted in
`data/eval/shapes-dev/hard-sources.tsv`. Numeric output of the WordSense Basic classifier (same owner, run unmodified)
helped to choose which sentences to label; no WordSense text, label or data is copied.

The three single-shape tranches of that set (`compound-head.tsv`, `headings.tsv` and `named-state.tsv` in
`data/eval/shapes-dev/`) are cut from new documents: 74, 51 and 59 documents of the federal families War
Department, Army and Army Air Forces, Navy, Department of Agriculture and Forest Service, plus one NASA handbook
(17 U.S.C. 105; scans on the Internet Archive whose records carry the Public Domain Mark or a statement that the
item is not in copyright, and a NASA Technical Reports Server record with the determination
`GOV_PUBLIC_USE_PERMITTED` and no third-party material). No document is in a training source or in any other evaluation
set; the three tranches may share documents among themselves, but no sentence. The license basis of every document is quoted
in `data/eval/shapes-dev/<tranche>-sources.tsv`. Numeric output
of the WordSense Basic classifier (same owner, run unmodified) helped to choose the `compound-head` sentences; no
WordSense text, label or data is copied.

The training source `data/imperative/` is built the same way from instructional text of US federal government
documents: 122 documents of the federal families War Department and Army, Navy, Department of Agriculture, Forest
Service, Bureau of Mines and Public Health Service (17 U.S.C. 105; scans and electronic copies on the Internet Archive
whose records carry the Public Domain Mark). Its annotations and its selection are dedicated to the public domain under
CC0 1.0, and the license basis of every document is quoted in `data/imperative/sources.tsv`. The source is registered
in the training configuration with weight 1, so the embedded weights were learned from it.

The two training sources `data/compound/` (sentences with a noun compound whose head can also be a verb) and
`data/headings/` (Title Case heading, caption and step-title lines) are built the same way from text of US federal
government documents: 154 and 89 documents of the federal families Department of Agriculture, Forest Service, Geological
Survey, War Department and Army, Navy, Bureau of Reclamation, Public Health Service, Bureau of Land Management, Bureau of
Mines, Weather Bureau and Bureau of Standards (17 U.S.C. 105; scans and electronic copies on the Internet Archive whose
records carry the Public Domain Mark or a statement that the item is not in copyright). A document whose record lists a
state or local government body as an author is not used, and neither is a document with a private non-profit
co-author whose public-domain basis is uncertain (`data/eval/shapes-dev/excluded-sources.tsv`). Their annotations and
their selection are dedicated to the
public domain under CC0 1.0, and the license basis of every document is quoted in `data/compound/sources.tsv` and
`data/headings/sources.tsv`. The compound source is registered in the training configuration with weight 1, so the
embedded weights were learned from it; the headings source is registered with weight 0: it is committed data, it is not
part of the mix that trained the embedded weights, and the weights were not learned from it.

The two follow-up tranches of that set (`compound-head-2.tsv` and `headings-2.tsv` in `data/eval/shapes-dev/`) and
the two follow-up training sources (`data/compound/compound-2.tsv` and `data/headings/headings-2.tsv`) are cut from
further US federal government documents of a pool of 416 (the tranches use 94 documents: 89 and 35, with 30 shared;
the sources use 211: 209 and 59, with 57 shared): scans and electronic copies on the Internet Archive of Army, Navy,
Marine Corps, Coast Guard, Air Force, Department of Agriculture, Forest Service, Bureau of Reclamation, Public Health
Service, Department of the Interior, Bureau of Standards and other federal publications (17 U.S.C. 105). The record
of every document names a federal body as its creator and no state, local, foreign, university, contractor or private
creator, publisher or co-author, and no document has a distribution statement other than public release, a copyright
notice or a third-party credit; nothing is in `data/eval/shapes-dev/excluded-sources.tsv`. The tranches are
development data; the compound source is training data registered with weight 1 (so the embedded weights were learned
from it) and the headings source is training data registered with weight 0 (so the embedded weights were not learned
from it). The annotations and the selection are dedicated to the public domain under CC0 1.0, and the license basis
of every document is quoted in `data/eval/shapes-dev/<tranche>-sources.tsv`, `data/compound/compound-2-sources.tsv`
and `data/headings/headings-2-sources.tsv`. Numeric output of the WordSense Basic classifier (same owner, run
unmodified) helped to choose the `compound-head-2` sentences; no WordSense text, label or data is copied.

Language models wrote the tags, and no human has reviewed them. In the owner's position, GitHub claims no ownership of
model outputs, so the repository owner, who prompted their creation, holds the rights in them and dedicates them to the
public domain under CC0 1.0. That is the owner's position, not legal advice or a warranty.

### Tatoeba

Some sentences come from Tatoeba, <https://tatoeba.org>. Only sentences that are in the official Tatoeba CC0 export are
used (CC0 1.0). Tatoeba's other sentences, which are licensed CC BY 2.0 FR, are not used.

### Project Gutenberg

The silver training data in `data/silver/` and part of the open corpus use texts from Project Gutenberg,
<https://www.gutenberg.org>, that are in the public domain in the United States. Only the book text is used. No Project
Gutenberg license text, trademark or other Project Gutenberg material is part of the data. Public-domain status is a
statement about the United States only.

## Evaluation-Only Data That Is Not Distributed

The Universal Dependencies English Web Treebank (UD English-EWT, <https://github.com/UniversalDependencies/UD_English-EWT>)
is licensed under CC BY-SA 4.0. The project uses it only to measure the accuracy of taggers and to check that no training
sentence appears in it. It is downloaded on demand into a git-ignored cache and is never committed, redistributed or
packaged, and nothing is trained on it.

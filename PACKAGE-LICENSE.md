SPDX-License-Identifier: MIT AND CC-BY-4.0 AND CC-BY-3.0-US

# License of the DemaConsulting.PartOfSpeech Package

The NuGet package `DemaConsulting.PartOfSpeech` contains the code of the library and two files of data that are embedded
in the assembly. The code is licensed under the MIT License. The data comes from third parties and is licensed under the
terms below. The first line of this file is the SPDX license expression that covers the whole package; it is a hint for
license scanners, and this file is the license. nuget.org accepts only license expressions that the Open Source
Initiative or the Free Software Foundation approve, and `CC-BY-3.0-US` is neither, so the package names this file
instead of an expression.

## Code: MIT License

Copyright (c) 2026 DEMA Consulting

Permission is hereby granted, free of charge, to any person obtaining a copy
of this software and associated documentation files (the "Software"), to deal
in the Software without restriction, including without limitation the rights
to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
copies of the Software, and to permit persons to whom the Software is
furnished to do so, subject to the following conditions:

The above copyright notice and this permission notice shall be included in all
copies or substantial portions of the Software.

THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE
SOFTWARE.

## Data Embedded in This Package

The assembly embeds two binary files. Each is derived from the third-party works named below, was adapted by DEMA
Consulting, and is not the original data.

### The lexicon (`lexicon.bin`): CC BY 4.0 and CC BY 3.0 US

The lexicon maps a lower-case English spelling to the Universal POS tags that it may carry. It is derived from the word
forms of Open English WordNet 2025 Edition, which is licensed under the Creative Commons Attribution 4.0 International
License (CC BY 4.0, <https://creativecommons.org/licenses/by/4.0/>), and which is derived from Princeton WordNet. The
tags that the lexicon lists are attested in the Manually Annotated Sub-Corpus (MASC), which is licensed under the
Creative Commons Attribution 3.0 United States License (CC BY 3.0 US,
<https://creativecommons.org/licenses/by/3.0/us/>), and in the open corpus of this project (CC0 1.0). It also uses the
Moby Part-of-Speech List, which is in the public domain.

### The weights (`weights.bin`): CC BY 3.0 US

The weights are the trained model of the tagger. They were learned from three corpora of this project, which are not
part of the package:

- the open corpus (CC0 1.0), with Universal POS tags that language models wrote;
- the written texts of MASC, whose Penn Treebank part-of-speech tags were converted to Universal POS tags (CC BY 3.0 US);
- a silver corpus of Tatoeba (CC0 1.0) and Project Gutenberg (public domain in the United States) sentences, tagged by a
  language model (CC0 1.0).

Because the weights were trained on data adapted from MASC, they are provided under the terms of its license. The
authors of Open English WordNet, Princeton University and the authors of MASC do not endorse this project, the lexicon
or the weights.

## Attribution and Where to Find It

The attribution, license links, citation and statement of changes that these licenses require are in the file
`THIRD-PARTY-NOTICES.md` at the root of this package, which also reproduces the Princeton WordNet license notice. This
file and `THIRD-PARTY-NOTICES.md` together are the complete notices of the package. Keep both with every copy of the
package that you redistribute. The source data and the tools that produce the embedded files are in the repository
<https://github.com/demaconsulting/PartOfSpeech> (folders `data/` and `tools/`); the repository `LICENSE` file is the
plain MIT License of the code.

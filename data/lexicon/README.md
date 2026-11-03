<!-- markdownlint-disable MD013 MD060 -->

# Lexicon

License: CC-BY-4.0

License: CC-BY-3.0-US

A lexicon that maps a lower-case spelling to the UPOS tags the spelling may carry: 262,667 spellings, 339,880
spelling-tag entries (1.294 tags per spelling), 4.2 MiB as TSV. A tagger uses it to restrict the tags it considers for
a known word. It is built reproducibly from open dictionary tables, a short list of closed-class words and the tags
attested in the training corpora. The files carry attribution obligations (CC BY 4.0 for the Open English WordNet
derived tables, CC BY 3.0 US for the MASC attestation); see [../../THIRD-PARTY-NOTICES.md](../../THIRD-PARTY-NOTICES.md).
The other inputs are public domain (Moby Part-of-Speech II), MIT (project additions to the WordSense tables) or CC0 1.0
(the closed-class list and the open corpus annotations).

## Files

| File | Content |
| --- | --- |
| `lexicon.tsv` | the lexicon: `spelling TAB TAG TAG ...` (tags alphabetical, rows sorted by UTF-8 bytes, comment lines start with a hash and a space); generated, do not edit |
| `closed-class.tsv` | input: function words, auxiliaries, number words and interjections with their tags and a mode (`exact` replaces the dictionary tags, `add` extends them) |
| `overrides.tsv` | input: evidenced corrections (empty in this build) |
| `coverage.tsv` | machine-readable coverage report (written by `check_lexicon.py`) |
| `COVERAGE.md` | readable coverage report including every miss on open dev and open test |
| `sweep.tsv` | the attestation threshold sweep on train and dev (`check_lexicon.py --sweep`) |

## Sources

| Input | License | Used for |
| --- | --- | --- |
| WordSense `data/form/*.tsv` (151 files) at commit `6530de1928baf30f9a14446a54bfb5dd5f67b41c`: Open English WordNet 2025 forms and rule-generated inflections, Moby Part-of-Speech II reverse forms, project additions | Open English WordNet CC BY 4.0 (with the Princeton WordNet notice), Moby public domain (Grady Ward), project additions MIT, authored inflections CC0 1.0 | open-class spellings |
| `closed-class.tsv` (project-authored from general knowledge of English) | CC0 1.0 | function words, auxiliaries, numbers, interjections |
| `../open/train.tsv` tokens whose status is not `disputed` | CC0 1.0 annotations | tags the dictionaries lack |
| `../masc/masc.tsv` | CC BY 3.0 US (derived) | tags the dictionaries lack, attested at least twice |

The SHA-256 of each form table is pinned in `tools/data/pins/wordsense-form.manifest.tsv`. The tables contain no UD,
SemCor, Brown or Wiktionary data: the WordSense provenance values are `oewn_form`, `rule`, `rule_variant`,
`moby_reverse`, `carried` (rows of the project's earlier reviewed table, frozen once before the generator existed), `llm`,
`hand`, `irregular_list` and `contraction`, and WordSense used UD English-EWT only to measure the precision of a model,
never as lexicon data.

## How It Is Built

`python tools/data/build_lexicon.py [--wordsense-dir PATH_TO_WORDSENSE_CHECKOUT] [--k 1] [--masc-k 2]`

1. Read every form row. The key is the lower-case NFC spelling; spellings with a space (multiword terms), spellings
   without a letter and contraction rows (`don't`; contractions are split by the tokenization) are skipped.
2. The part of speech is the suffix of the entry id, with an optional homograph number (`oewn-wind-n-1` is a noun,
   `dema-to-p` an adposition); the `pos` column is used when the entry id is empty. A naive split at the last hyphen
   mislabels every numbered homograph.
3. Mapping: `n` NOUN (and PROPN when the cased spelling starts with an upper-case letter); `v` VERB; `a`, `s` ADJ; `r` ADV;
   `u` PRON; `p` ADP; `d` DET; `x` AUX; `i` INTJ; `c` CCONJ when `closed-class.tsv` lists the word with CCONJ, else SCONJ.
   A verb row with `VerbForm=Part` or `Tense=Past` also adds ADJ; `VerbForm=Ger` adds ADJ and NOUN (participles and
   gerunds, from the features and not from suffixes).
4. Apply `closed-class.tsv` and `overrides.tsv` (`exact` replaces the dictionary tags, `add` extends them).
5. Attestation: add every (spelling, tag) that occurs at least K = 1 times in `open/train.tsv` tokens that are not
   `disputed` and at least M = 2 times in `masc/masc.tsv`. Tokens that contain a digit are not added.

The thresholds were chosen on the development split from the sweep below (train and dev only; test was never used).

| K (open train) | M (MASC) | Spellings | Tags per spelling | Dev coverage, all tokens % | Dev coverage, non-disputed % |
| ---: | ---: | ---: | ---: | ---: | ---: |
| 0 | 0 | 258,278 | 1.290 | 99.714 | 99.834 |
| 1 | 0 | 261,266 | 1.292 | 99.798 | 99.882 |
| 2 | 0 | 258,871 | 1.291 | 99.790 | 99.878 |
| 1 | 1 | 266,231 | 1.295 | 99.844 | 99.909 |
| **1** | **2** | **262,667** | **1.294** | **99.848** | **99.913** |
| 1 | 3 | 262,028 | 1.293 | 99.844 | 99.909 |
| 1 | 5 | 261,662 | 1.293 | 99.828 | 99.901 |

Rules alone (K = 0, M = 0) reach 99.714 percent on dev (99.834 on non-disputed tokens); attestation is what lifts the
non-disputed dev coverage above the 99.9 percent gate.

## Token-Shape Rules (normative)

A spelling that is not in `lexicon.tsv` is unknown. The tags allowed for a token are:

1. the lower-case token is not in the lexicon: **all 17 tags**;
2. otherwise the lexicon tags of the lower-case token;
3. a token with no letter or digit also allows PUNCT, SYM and X;
4. a token that contains a digit also allows NUM, SYM, X, NOUN, PROPN and ADJ;
5. a token whose first character is an upper-case letter also allows PROPN and X.

`tools/data/common.py` (`allowed_tags`) implements them; the C# decoder (`Lexicon.AllowedTags` in the library) and the C#
data tests implement them identically. The comparison is by `lower()` of the token (Python) and ordinal lower-casing in C#.

## Embedded Form

The package does not contain `lexicon.tsv`; it embeds a compiled binary form, `src/DemaConsulting.PartOfSpeech/Resources/lexicon.bin`
(format `POSL`, version 1, little endian, raw Deflate payload). It is 641,418 bytes for the committed lexicon (262,667
spellings, 339,880 entries, 230 distinct tag masks) against 4,452,746 bytes of TSV. The spellings are stored sorted by
UTF-8 bytes in blocks of 16: the first spelling of a block whole and the others front-coded against their predecessor,
each with the index of its tag mask in a palette. A lookup is exact (no hash collisions), searches the block heads by
binary search and scans one block; it allocates nothing. The assembly contains the resource once per target framework,
so the NuGet package is about 3.2 MB. The byte layout is specified in the design document of the `Lexicon` unit
(`docs/design/part-of-speech/model-storage/lexicon.md`).

The binary is generated and committed, so that a clean checkout builds with nothing to run:

- Regenerate it after any change of `lexicon.tsv`: `dotnet run --project tools/LexiconCompiler`
  (options `--input` and `--output` select other files).
- Check that it is current: `dotnet run --project tools/LexiconCompiler -- --check`. `build.ps1` runs this step, and a
  unit test makes the same comparison, so a stale binary fails the build and the tests. The check compares the
  uncompressed payloads because the Deflate bytes depend on the runtime.
- The compiler refuses a table that breaks a format rule (unsorted or duplicate spellings, a space in a spelling, an
  unknown or unordered tag) and a header whose counts do not match the rows, and names the line.

The C# decoder applies the Token-Shape Rules above to the tags that it reads from the embedded lexicon.

## Coverage

A token is **covered** when its gold tag is allowed. The gate G1 is: at least 99.9 percent of the non-`disputed` tokens of
open train and open dev are covered (`python tools/data/check_lexicon.py` exits with code 1 otherwise).

| File | Tokens | Covered % (all tokens) | Non-disputed tokens | Covered % (non-disputed) | Unknown-spelling tokens |
| --- | ---: | ---: | ---: | ---: | ---: |
| open train | 209,224 | 99.878 | 201,775 | 99.9995 | 1,566 |
| open dev | 26,232 | 99.848 | 25,365 | **99.913** | 593 |
| open test | 26,185 | 99.729 | 25,278 | 99.881 | 554 |
| masc | 386,445 | 99.853 | 386,445 | 99.853 | 9,074 |

- **Train is covered by construction** (every non-disputed training tag is added to the lexicon) except one token:
  `1st` as an adverb, which the digit rule does not cover. Dev is the gate, but the attestation thresholds were tuned on it, so test is the only unbiased report. The all-token
  figures are lower because the lexicon ignores `disputed` tokens, which are the annotators' hardest cases.
- **Unknown-spelling tokens count as covered** (all tags allowed); the last column shows how much of the figure that is.
  The restriction value of the lexicon is therefore smaller on texts with many unknown words (names, jargon).
- **Test misses:** 71 tokens of 26,185 (46 types; 30 tokens non-disputed) have a gold tag that the lexicon does not
  allow; all are listed in `COVERAGE.md`. Most are annotation noise or conventions rather than lexicon gaps: words
  mentioned as words and tagged NOUN (`which`, `avoid`, `prevent`, `kindly`, `of`, `they`, `to`; all `disputed`), foreign
  phrases tagged X (`culpa` in `mea culpa`, 7 tokens), noun uses of verbs (`abstracts`, `downloads`), noun-to-adjective
  conversions (`chocolate`, `commoner`, `steampunk`) and hyphenated numbers (`one-half`, `seventy-seven`).
- **MASC misses** are mapping noise of the Penn conversion; the lexicon adds a MASC tag only when it occurs twice.
- **Known weakness:** corpus attestation also admits annotation noise, so a function word can carry a noise tag (for
  example `the` is PROPN in names such as `The Hague`); the lexicon is a restriction, not a gold standard.

Run `python tools/data/check_lexicon.py` to regenerate `coverage.tsv` and `COVERAGE.md`.

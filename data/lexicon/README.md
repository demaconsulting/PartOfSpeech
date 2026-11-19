# Lexicon

License: CC-BY-4.0

License: CC-BY-3.0-US

A lexicon that maps a lower-case spelling to the UPOS tags the spelling may carry: 262,635 spellings, 339,819
spelling-tag entries (1.294 tags per spelling), 4.2 MiB as TSV. A tagger uses it to restrict the tags it considers for a
known word. For 258,255 of the spellings an analyses table (`analyses.tsv`, 290,365 analyses, 10.3 MiB as TSV) adds the
base word, the tag and the features of each form that the spelling can be; the library reports it as the analyses of a
word. The lexicon is built reproducibly from open dictionary tables, a short list of closed-class words and the tags
attested in the training corpora; the analyses come from the same dictionary tables and the closed-class list. The files
carry attribution obligations (CC BY 4.0 for the Open English WordNet derived tables, CC BY 3.0 US for the MASC
attestation); see [../../THIRD-PARTY-NOTICES.md](../../THIRD-PARTY-NOTICES.md). The other inputs are public domain (Moby
Part-of-Speech II), MIT (project additions to the WordSense tables) or CC0 1.0 (the closed-class list and the open
corpus annotations).

## Files

- `lexicon.tsv`: the lexicon: `spelling TAB TAG TAG ...` (tags alphabetical, rows sorted by UTF-8 bytes, comment lines
  start with a hash and a space); generated, do not edit
- `analyses.tsv`: the analyses: `spelling TAB analysis TAB analysis ...` where an analysis is `lemma TAG` or
  `lemma TAG features` (base word in lower-case NFC, features in Universal Dependencies notation: `Name=Value` pairs
  sorted by name and joined by `|`, from `Degree`, `Number`, `Person`, `Tense` and `VerbForm`; `Capitalization` is
  dropped); only spellings and tags of `lexicon.tsv`, analyses ordered by tag, base word and features; generated, do not
  edit
- `closed-class.tsv`: input: function words, auxiliaries, number words and interjections with their tags and a mode
  (`exact` replaces the dictionary tags, `add` extends them)
- `overrides.tsv`: input: evidenced corrections (empty in this build)
- `coverage.tsv`: machine-readable coverage report (written by `check_lexicon.py`)
- `COVERAGE.md`: readable coverage report including every miss on open dev and open test
- `sweep.tsv`: the attestation threshold sweep on train and dev (`check_lexicon.py --sweep`)

## Sources

- WordSense `data/form/*.tsv` (151 files) at commit `6530de1928baf30f9a14446a54bfb5dd5f67b41c`: Open English WordNet
  2025 forms and rule-generated inflections, Moby Part-of-Speech II reverse forms, project additions: **License**: Open
  English WordNet CC BY 4.0 (with the Princeton WordNet notice), Moby public domain (Grady Ward), project additions MIT,
  authored inflections CC0 1.0; **Used for**: open-class spellings
- `closed-class.tsv` (project-authored from general knowledge of English): **License**: CC0 1.0; **Used for**: function
  words, auxiliaries, numbers, interjections
- `../open/train.tsv` tokens whose status is not `disputed`: **License**: CC0 1.0 annotations; **Used for**: tags the
  dictionaries lack
- `../masc/masc.tsv`: **License**: CC BY 3.0 US (derived); **Used for**: tags the dictionaries lack, attested at least
  twice

The SHA-256 of each form table is pinned in `tools/data/pins/wordsense-form.manifest.tsv`. The analyses use the same
form tables, the closed-class list and the committed `lexicon.tsv` (the tags of each spelling) and no other input, so
they carry the licenses of those inputs. The tables contain no UD, SemCor, Brown or Wiktionary data: the WordSense
provenance values are `oewn_form`, `rule`, `rule_variant`, `moby_reverse`, `carried` (rows of the project's earlier
reviewed table, frozen once before the generator existed), `llm`, `hand`, `irregular_list` and `contraction`, and
WordSense used UD English-EWT only to measure the precision of a model, never as lexicon data.

## How It Is Built

`python tools/data/build_lexicon.py [--wordsense-dir PATH_TO_WORDSENSE_CHECKOUT] [--k 1] [--masc-k 2]`

1. Read every form row. The key is the lower-case NFC spelling; spellings with a space (multiword terms), spellings
   without a letter and contraction rows (`don't`; contractions are split by the tokenization) are skipped.
2. The part of speech is the suffix of the entry id, with an optional homograph number (`oewn-wind-n-1` is a noun,
   `dema-to-p` an adposition); the `pos` column is used when the entry id is empty. A naive split at the last hyphen
   mislabels every numbered homograph.
3. Mapping: `n` NOUN (and PROPN when the cased spelling starts with an upper-case letter); `v` VERB; `a`, `s` ADJ; `r`
   ADV; `u` PRON; `p` ADP; `d` DET; `x` AUX; `i` INTJ; `c` CCONJ when `closed-class.tsv` lists the word with CCONJ, else
   SCONJ. A verb row with `VerbForm=Part` or `Tense=Past` also adds ADJ; `VerbForm=Ger` adds ADJ and NOUN (participles
   and gerunds, from the features and not from suffixes).
4. Apply `closed-class.tsv` and `overrides.tsv` (`exact` replaces the dictionary tags, `add` extends them).
5. Attestation: add every (spelling, tag) that occurs at least K = 1 times in `open/train.tsv` tokens that are not
   `disputed` and at least M = 2 times in `masc/masc.tsv`. Tokens that contain a digit are not added.

The thresholds were chosen on the development split from the sweep below (train and dev only; test was never used).

| K (open train) | M (MASC) | Spellings | Tags per spelling | Dev cov. %, all tokens | Dev cov. %, non-disputed |
| ---: | ---: | ---: | ---: | ---: | ---: |
| 0 | 0 | 258,278 | 1.290 | 99.748 | 99.838 |
| 1 | 0 | 261,236 | 1.292 | 99.836 | 99.893 |
| 2 | 0 | 258,867 | 1.291 | 99.821 | 99.881 |
| 1 | 1 | 266,201 | 1.295 | 99.870 | 99.917 |
| **1** | **2** | **262,635** | **1.294** | **99.874** | **99.921** |
| 1 | 3 | 261,996 | 1.293 | 99.870 | 99.917 |
| 1 | 5 | 261,632 | 1.293 | 99.863 | 99.909 |

Rules alone (K = 0, M = 0) reach 99.748 percent on dev (99.838 on non-disputed tokens); attestation is what lifts the
non-disputed dev coverage above the 99.9 percent gate.

### The Analyses

`python tools/data/build_lexicon.py` writes `analyses.tsv` after `lexicon.tsv` from the same form rows. An analysis is
the base word (lemma), the UPOS tag and the features of one form row: the base word is the entry id without its source
prefix and part-of-speech suffix (`oewn-test-v` is `test`), the tag is the mapping of the entry's own part of speech,
and the features are the source features without `Capitalization`, in Universal Dependencies notation (names ascending,
joined by `|`). A tag that the dictionary part adds by rule or that the corpora attest has no analysis (`the` lists ADV,
DET, PRON and PROPN and has one analysis, `the DET`); an analysis is kept only when its tag is a tag of the spelling in
`lexicon.tsv`. 258,255 of the 262,635 spellings have at least one analysis (290,365 analyses in all). A new feature name
or value is a build error, so that new vocabulary is a reviewed decision.

- `python tools/data/build_lexicon.py --analyses-only` writes only `analyses.tsv`, from the committed `lexicon.tsv`.
- `python tools/data/build_lexicon.py --check-analyses` rebuilds the analyses from the pinned form tables and exits with
  code 1 when the committed file differs.

## Token-Shape Rules (normative)

A spelling that is not in `lexicon.tsv` is unknown. The tags allowed for a token are:

1. the lower-case token is not in the lexicon: **all 17 tags**;
2. otherwise the lexicon tags of the lower-case token;
3. a token with no letter or digit also allows PUNCT, SYM and X;
4. a token that contains a digit also allows NUM, SYM, X, NOUN, PROPN and ADJ;
5. a token whose first character is an upper-case letter also allows PROPN and X.

`tools/data/common.py` (`allowed_tags`) implements them; the C# decoder (`Lexicon.AllowedTags` in the library) and the
C# data tests implement them identically. The comparison is by `lower()` of the token (Python) and ordinal lower-casing
in C#.

## Embedded Form

The package does not contain `lexicon.tsv` or `analyses.tsv`; it embeds one compiled binary form of both,
`src/DemaConsulting.PartOfSpeech/Resources/lexicon.bin` (format `POSL`, version 3, little endian, raw Deflate payload;
the header checksum covers the stored bytes and the reader requires the Deflate stream to be complete; a file of version
2 is refused). It is 762,836 bytes for the committed data (262,635 spellings, 339,819 entries, 225 distinct tag masks,
258,255 spellings with analyses, 290,365 analyses in 414 templates and 804 lists, 14 feature sets) and 2,157,499 bytes
uncompressed, against 4,452,392 bytes of TSV for the lexicon and 10,830,924 bytes for the analyses. The spellings are
stored sorted by UTF-8 bytes in blocks of 16: the first spelling of a block whole and the others front-coded against
their predecessor, each with the index of its tag mask in a palette. The analyses are stored as palettes: an analysis is
a template (tag, feature set, the number of bytes cut from the end of the spelling and the suffix that completes the
base word), a list is a sequence of templates, and each spelling has the index of its list. A lookup is exact (no hash
collisions), searches the block heads by binary search and scans one block; it allocates nothing, and reading the
analyses of a spelling allocates only its result. The assembly contains the resource once per target framework, so the
NuGet package is about 8.0 MB (five assemblies of 1.4 to 1.5 MB stored). The byte layout is specified in the design
document of the `Lexicon` unit (`docs/design/part-of-speech/model-storage/lexicon.md`).

The binary is generated and committed, so that a clean checkout builds with nothing to run:

- Regenerate it after any change of `lexicon.tsv` or `analyses.tsv`: `dotnet run --project tools/LexiconCompiler`
  (options `--input`, `--analyses` and `--output` select other files; an explicit `--input` without `--analyses`
  compiles a lexicon without analyses).
- Check that it is current: `dotnet run --project tools/LexiconCompiler -- --check`. `build.ps1` runs this step, and a
  unit test makes the same comparison, so a stale binary fails the build and the tests. The check compares the
  uncompressed payloads, the analyses included, because the Deflate bytes depend on the runtime.
- The compiler refuses a table that breaks a format rule (unsorted or duplicate spellings, a space in a spelling, an
  unknown or unordered tag, an analysis for a tag that the spelling lacks) and a header whose counts do not match the
  rows, and names the line.

The C# decoder applies the Token-Shape Rules above to the tags that it reads from the embedded lexicon.

## Coverage

A token is **covered** when its gold tag is allowed. The gate G1 is: at least 99.9 percent of the non-`disputed` tokens
of open train and open dev are covered (`python tools/data/check_lexicon.py` exits with code 1 otherwise).

| File | Tokens | Covered % (all tokens) | Non-disputed tokens | Covered % (non-disputed) | Unknown-spelling tokens |
| --- | ---: | ---: | ---: | ---: | ---: |
| open train | 209,224 | 99.935 | 201,131 | 100.000 | 1,599 |
| open dev | 26,232 | 99.874 | 25,297 | **99.921** | 593 |
| open test | 26,185 | 99.843 | 25,198 | 99.909 | 554 |
| masc | 386,445 | 99.859 | 385,828 | 99.864 | 9,078 |

- **Train is covered by construction** (every non-disputed training tag is added to the lexicon) except one token: `1st`
  as an adverb, which the digit rule does not cover. Dev is the gate, but the attestation thresholds were tuned on it,
  so test is the only unbiased report. The all-token figures are lower because the lexicon ignores `disputed` tokens,
  which are the annotators' hardest cases.
- **Unknown-spelling tokens count as covered** (all tags allowed); the last column shows how much of the figure that is.
  The restriction value of the lexicon is therefore smaller on texts with many unknown words (names, jargon).
- **Test misses:** 41 tokens of 26,185 (32 types; 23 tokens non-disputed) have a gold tag that the lexicon does not
  allow; all are listed in `COVERAGE.md`. Most are annotation noise or conventions rather than lexicon gaps: words
  mentioned as words and tagged NOUN (`avoid`, `prevent`, `kindly`, `please`, `to`; all `disputed`), foreign
  phrases tagged X (`culpa` in `mea culpa`, 3 tokens), noun uses of verbs (`abstracts`, `downloads`), noun-to-adjective
  conversions (`chocolate`, `commoner`, `steampunk`) and hyphenated numbers (`one-half`, `seventy-seven`).
- **MASC misses** are mapping noise of the Penn conversion; the lexicon adds a MASC tag only when it occurs twice.
- **Known weakness:** corpus attestation also admits annotation noise, so a function word can carry a noise tag (for
  example `the` is PROPN in names such as `The Hague`); the lexicon is a restriction, not a gold standard.

Run `python tools/data/check_lexicon.py` to regenerate `coverage.tsv` and `COVERAGE.md`.

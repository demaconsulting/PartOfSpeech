<!-- markdownlint-disable MD013 MD060 -->
<!-- cspell:ignore pnoun pverb -->

# hmm-tags: WordSense Basic classifier tags for the hard development tranche

A standalone, development-time console tool. It tags tokenized sentences with the WordSense Basic classifier (HMM tagger plus
cue model; below, "the WordSense Basic classifier (HMM tagger plus cue model)") and writes the part-of-speech probability mass
of every token. The hard tranche of the shapes development set (`data/eval/shapes-dev/hard.tsv`) and its `compound-head` tranche
(`compound-head.tsv`) use that mass as a
model-independent selection signal: a different model family, trained by another project, that has no connection to the
PartOfSpeech tagger or its weights. The selection rules are in
[`tools/data/shapes_dev.py`](../shapes_dev.py), [`tools/data/shapes_dev_extra.py`](../shapes_dev_extra.py) and in `data/eval/shapes-dev/README.md`.

## Not part of the solution

`tools/data/hmm-tags/` is deliberately **not** in `PartOfSpeech.slnx`. `build.ps1`, `lint.ps1`, `fix.ps1` and CI never build or
format it, because it needs a binary that must not be committed: a pinned copy of `DemaConsulting.WordSense.dll`, kept outside
this repository. The harness is therefore built and run by hand, only when `hmm.tsv` of a hard-tranche work folder has to be
produced. The committed hard tranche does not need the harness at all: its selection record is in the README, and the
builder reads the output file, never the DLL. The project file fails with a clear message when `WordSenseDll` is not given.
(A reflection-based loader would build without the DLL, but a typed reference keeps the call into the classifier checked by the
compiler, which matters for a pinned binary.) The harness depends on no PartOfSpeech project, package or file.

## Usage

```text
dotnet run -c Release --project tools/data/hmm-tags -p:WordSenseDll=PATH -- --in FILE --out FILE --wordsense-commit SHA
```

- `-p:WordSenseDll=PATH` is the pinned copy of `DemaConsulting.WordSense.dll` (built from the WordSense repository outside this
  repository; the WordSense repository is only ever read, never built or written by this work). The file that is copied next to
  the harness is the one that runs, and the harness hashes exactly that file.
- `--in FILE` is a `tokens.tsv` of the shapes tools (header `text<TAB>sentences<TAB>tokens`; only one-sentence rows are tagged)
  or plain lines of tokens that are separated by single spaces.
- `--wordsense-commit SHA` is recorded in the header of the output (the commit of the WordSense source the DLL was built from).

Example (the work folder `W` of the hard tranche holds `tokens.tsv`, the pinned DLL is in `W/hmm/lib`):

```text
dotnet run -c Release --project tools/data/hmm-tags -p:WordSenseDll=W/hmm/lib/DemaConsulting.WordSense.dll -- --in W/tokens.tsv --out W/hmm.tsv --wordsense-commit 6530de1928baf30f9a14446a54bfb5dd5f67b41c
```

## Output format (`hmm.tsv`)

```text
# hmm-tags 1: part-of-speech mass per token of the WordSense Basic classifier (HMM tagger plus cue model)
# wordsense_commit = 6530de1928baf30f9a14446a54bfb5dd5f67b41c
# wordsense_dll_sha256 = b96e0055664896983e311f626d41e5251a2dcf1a236d86f6ec50beaae6a746bf
# input_sha256 = SHA-256 of the --in file
# sentences = number of tagged sentences
# cell = form|POS|pNoun|pVerb (one line per sentence, in input order)
form|POS|pNoun|pVerb<TAB>form|POS|pNoun|pVerb ...
```

One line per sentence, in input order, one tab-separated cell per token: the token, the part of speech with the most
probability mass (`?` when the classifier has no analysis for the token), and the summed analysis probabilities of the parts
of speech Noun and Verb (three decimals, invariant culture). A reader splits a cell from the right, because a token may
contain `|`. The shapes builder finds the line of a sentence by its token sequence, so a re-sorted `tokens.tsv` does not
need a new run; a sentence whose tokens are missing from `hmm.tsv` is an error, never a silent default.

The run is deterministic for one DLL and one input. The header records the evidence for a re-run: the WordSense commit and
the SHA-256 of the DLL that ran, and the SHA-256 of the input.

## Pinned build used for the hard tranche

| Item | Value |
| --- | --- |
| WordSense repository | `github.com/DemaConsulting/WordSense` (read only) |
| Commit | `6530de1928baf30f9a14446a54bfb5dd5f67b41c` (2026-11-01, "s3 infrastructure ...") |
| DLL | `src/DemaConsulting.WordSense/bin/Debug/net10.0/DemaConsulting.WordSense.dll`, built 2026-11-05 from that commit (no tracked change in the tree) |
| DLL SHA-256 | `b96e0055664896983e311f626d41e5251a2dcf1a236d86f6ec50beaae6a746bf` |
| Class | `BasicWordSenseModel` (no model files; embedded image), call `ClassifyAsync(IEnumerable<string>)` |

The binary is never committed. The class needs no model download, runs offline, and tags about 24,000 sentences in a few
seconds.

## What the mass means

The Basic classifier answers with the HMM tagger posterior combined with the lexicon and the cue model. This tool sums, per
token, the probabilities of the analyses of each part of speech, so `pNoun` and `pVerb` are the classifier's own mass for
the classes the shapes are about; it is not the pure HMM posterior (that one is internal to WordSense and out of reach
without building the WordSense projects). Nothing of a PartOfSpeech tagger is read, and the mass never decides a label: the
gold of the hard tranche comes only from two blind model labelers.

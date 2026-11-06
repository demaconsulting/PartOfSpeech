// cspell:ignore Contoso
namespace DemaConsulting.PartOfSpeech;

/// <summary>
///     Part-of-speech tagging for English text: a tokenizer, a sentence splitter and a greedy averaged-perceptron tagger
///     that assigns one of the 17 Universal POS tags to every token, with a confidence, ranked alternatives and
///     optional per-token constraints and glossaries of domain terms. This is the whole public API of the package
///     <c>DemaConsulting.PartOfSpeech</c>.
/// </summary>
/// <remarks>
///     <para>
///     ## Overview
///     </para>
///     <para>
///     Package: the NuGet package <c>DemaConsulting.PartOfSpeech</c>, which targets <c>netstandard2.0</c>,
///     <c>net8.0</c>, <c>net9.0</c> and <c>net10.0</c> and has no package dependencies, no native code and no model
///     runtime. Everything is in the single namespace <c>DemaConsulting.PartOfSpeech</c>; there are no sub-namespaces to
///     import.
///     </para>
///     <para>
///     What is in the package, in the order that you meet the types. <see cref="Tokenizer"/> splits a string into
///     <see cref="Sentence"/> objects whose <see cref="Token"/> items know their exact offsets in the source string.
///     <see cref="Tagger"/> tags a text, a <see cref="Sentence"/> or a list of tokens that you already split, and
///     returns <see cref="TaggedSentence"/> objects of <see cref="TaggedToken"/> items; each token carries its
///     <see cref="Upos"/> tag, a confidence, and its ranked alternatives as <see cref="TagScore"/> items.
///     <see cref="TaggerOptions"/> sets how many alternatives are reported. <see cref="TokenConstraint"/> lets you
///     restrict or nudge the tag of one token, and <see cref="Glossary"/> (with <see cref="GlossaryBuilder"/>,
///     <see cref="GlossaryEntry"/>, <see cref="GlossaryOptions"/>, <see cref="GlossaryMode"/> and
///     <see cref="GlossaryMatch"/>) applies a whole list of domain terms to a text. <see cref="UposSet"/> is an allocation-free set of tags, and
///     <see cref="UposExtensions"/> converts between <see cref="Upos"/> values and the Universal Dependencies spelling
///     (<c>NOUN</c>, <c>CCONJ</c>, <c>X</c>).
///     </para>
///     <para>
///     How the pieces fit together. The text goes through <see cref="Tokenizer.Tokenize(string)"/> (or you provide the
///     tokens yourself); the <see cref="Tagger"/> then walks the tokens from left to right. For each token it looks the
///     word up in the lexicon, builds features of the word and of the tags it has chosen so far, scores the allowed tags
///     with the weights of the model and picks the best one; the chosen tag becomes history for the next token. The
///     <see cref="Tagger.Tag(string)"/> method does all of this for a whole text.
///     </para>
///     <para>
///     ## Terminology
///     </para>
///     <para>
///     These terms are used on every page of this reference; this is the only place where they are defined.
///     </para>
///     <list type="bullet">
///     <item><description>
///     **UPOS** is the Universal POS tag set of the Universal Dependencies project: 17 coarse tags such as
///     <see cref="Upos.Noun"/>, <see cref="Upos.Verb"/>, <see cref="Upos.Adj"/> and <see cref="Upos.Punct"/>,
///     represented by the enum <see cref="Upos"/> and spelled in upper case (<c>NOUN</c>) in text. The tagger follows
///     the conventions of its training data, which differ from the Universal Dependencies guidelines in a few places
///     (for example a possessive word such as <c>my</c> is a determiner); the one-line definitions are on the page of
///     <see cref="Upos"/>.
///     </description></item>
///     <item><description>
///     A **token** is one unit that receives one tag: a word, a number, a punctuation mark, or the part of a contraction
///     such as <c>n't</c>. A **sentence** is the tokens between two sentence boundaries.
///     </description></item>
///     <item><description>
///     The **lexicon** is a compiled list of English spellings (compared in lower case) with the tags that the training
///     data supports for each of them; it is embedded in the package. A word that is in the lexicon can only take its
///     listed tags, plus the tags that the shape of the token allows (a token without a letter or a digit may also be
///     <c>PUNCT</c>, <c>SYM</c> or <c>X</c>; a token with a digit also <c>NUM</c>, <c>SYM</c>, <c>X</c>, <c>NOUN</c>,
///     <c>PROPN</c> or <c>ADJ</c>; a token that starts with an upper-case letter also <c>PROPN</c> or <c>X</c>). A word
///     that is not in the lexicon may take any of the 17 tags. The tags that remain for a token, after the lexicon, the
///     shape rules and any hard constraint, are its **allowed tags**.
///     </description></item>
///     <item><description>
///     The **weights** are the trained model: a <c>POSW</c> file that you load with
///     <see cref="Tagger.Load(Stream, TaggerOptions?)"/>. A <c>POSL</c> file is a replacement lexicon
///     (<see cref="Tagger.Load(Stream, Stream, TaggerOptions?)"/>).
///     </description></item>
///     <item><description>
///     **Averaged perceptron** names the kind of model. Every feature of a token (the word, its ending, its
///     capitalization, the neighboring words and the tags chosen before it) has one learned weight per tag; the weights
///     of all features of the token are added up for each tag, and the tag with the highest sum fits best. "Averaged"
///     means that the weights are the average over the training updates, which makes them more stable than the last
///     update.
///     </description></item>
///     <item><description>
///     **Greedy** means that the tagger decides token by token from left to right and never revisits a decision: the
///     tag that it chose for a token is final, and it is one of the inputs for the next tokens. It does not search for
///     the best tag sequence of the whole sentence.
///     </description></item>
///     <item><description>
///     The **raw score** of a tag is the sum of the weights of the features of the token for that tag (plus the
///     adjustment of a soft constraint, if any). It is a model-internal number: higher is better, it has no upper
///     limit, and it is only meaningful when compared with the raw scores of the other allowed tags of the same token.
///     </description></item>
///     <item><description>
///     The **temperature** is one number stored in the weights file. The tagger divides the raw scores by it before it
///     turns them into probabilities with the softmax: a higher temperature gives flatter probabilities, a lower one
///     gives more extreme probabilities.
///     </description></item>
///     <item><description>
///     The **softmax** is the function that turns the raw scores of the allowed tags into probabilities that are all
///     positive and add up to 1: divide every raw score by the temperature, take the exponential, and divide each
///     result by the sum of all of them (see [Confidence and alternatives](#confidence-and-alternatives)).
///     </description></item>
///     <item><description>
///     The **confidence** of a token (<see cref="TaggedToken.Confidence"/>) and its **alternatives**
///     (<see cref="TaggedToken.Alternatives"/>) are explained in [Confidence and alternatives](#confidence-and-alternatives).
///     </description></item>
///     <item><description>
///     A **constraint** (<see cref="TokenConstraint"/>) is knowledge that you add for one token, for example a
///     glossary that says a word is a noun in this document. It is **hard** when it limits the allowed tags
///     (<see cref="TokenConstraint.Only(Upos[])"/>) and **soft** when it adds to or subtracts from the raw score of a
///     tag (<see cref="TokenConstraint.Prefer(Upos, double)"/>, <see cref="TokenConstraint.Penalize(Upos, double)"/>).
///     A hard constraint that excludes every tag of the lexicon wins over the lexicon, and a chosen tag also influences
///     the tags of the following tokens.
///     </description></item>
///     <item><description>
///     The **head** of a glossary entry (<see cref="GlossaryEntry"/>) is the word of its term that takes the tags of the entry.
///     By default it is the last word of the term (<c>Contoso Hub</c> has the head <c>Hub</c>, <c>bleed valve</c> has
///     <c>valve</c>); for a phrasal verb it is the first word (<c>shut down</c> has the head <c>shut</c>). The other words of
///     the term are not given the tags of the entry. The rules are in [Glossary](#glossary) below.
///     </description></item>
///     </list>
///     <para>
///     ## Confidence and alternatives
///     </para>
///     <para>
///     For every token the tagger computes the raw score of each allowed tag and turns the raw scores into
///     probabilities with the softmax at the temperature of the model: the probability of a tag is
///     <c>exp(score / temperature)</c> divided by the sum of the same expression over all allowed tags. The tag with
///     the highest raw score is the chosen tag (when two tags have the same score, the one that comes first in the
///     alphabetical order of the Universal Dependencies spelling wins). Then:
///     </para>
///     <list type="bullet">
///     <item><description>
///     <see cref="TaggedToken.Confidence"/> is the probability of the chosen tag, from 0 to 1.
///     </description></item>
///     <item><description>
///     <see cref="TaggedToken.Alternatives"/> lists the other allowed tags with their probabilities
///     (<see cref="TagScore"/>), the most probable first. The chosen tag is never in the list.
///     </description></item>
///     <item><description>
///     When the lexicon and the shape rules leave a single allowed tag, the confidence is exactly 1.0 and there are no
///     alternatives, whatever the model thinks: the value then says that there was no choice, not that the model is
///     sure. Judge the confidence on tokens that had several allowed tags.
///     </description></item>
///     <item><description>
///     The confidence and the probabilities of all alternatives add up to 1 (up to floating-point rounding) only when no
///     alternative is left out. The number of alternatives of a token is the smaller of
///     <see cref="TaggerOptions.MaxAlternatives"/> and the number of allowed tags minus one; with the default of 3,
///     a token that has more than four allowed tags reports only the three most probable others, and with
///     <see cref="TaggerOptions.MaxAlternatives"/> set to 0 the confidence stands alone. To always get the complete
///     distribution, set it to <see cref="TaggerOptions.MaxAlternativesLimit"/> (16).
///     </description></item>
///     <item><description>
///     The probabilities are relative to the allowed tags only. A tag that the lexicon or a hard constraint excluded has
///     no probability at all, with one exception: when the allowed set of a hard constraint and the set that the lexicon
///     and the shape rules allow have no tag in common, the constraint overrides the lexicon and the allowed set of the
///     constraint alone is used, so the probabilities are relative to those tags. A soft constraint changes the raw scores before the softmax, so the confidence of a
///     constrained token (<see cref="TaggedToken.IsConstrained"/>) includes the effect of your constraint.
///     </description></item>
///     <item><description>
///     The tagger is greedy, so the probabilities of a token are conditional on the tags that were already chosen for
///     the tokens before it; the confidence is not the probability that the whole sentence is tagged correctly.
///     </description></item>
///     </list>
///     <para>
///     The temperature is meant to make the probabilities match how often the chosen tag is right, and for the evaluated
///     model the evaluation shows that it does. Which confidence is low enough to check a token again is measured too;
///     see [Confidence thresholds](#confidence-thresholds).
///     </para>
///     <para>
///     ## Confidence thresholds
///     </para>
///     <para>
///     The measurement below was made once on held-out data for the model that the package embedded before the training
///     corpora were corrected, after two thresholds had been chosen on development data only. The embedded model has been
///     retrained on the corrected corpora since then, checked on development data only, and not evaluated on held-out data
///     (the evaluation record and the trainer run log say how it stands), so read the figures as those of the earlier model
///     and re-measure before you rely on them. The measurement is about the mistake that changes the meaning of a sentence: a word that is a noun and is
///     tagged as a verb, or the other way round (a <em>harmful error</em>). A token is <em>answered</em> at a threshold
///     when the confidence of its tag is at least the threshold, and <em>coverage</em> is the share of the noun-verb
///     decisions that is answered. Without a threshold, 20.6 of 1,000 noun-verb decisions of the open test split are
///     harmful errors. Two thresholds are recommended for the confidence of <see cref="Tagger.Default"/>
///     (<see cref="TaggedToken.Confidence"/>):
///     </para>
///     <list type="bullet">
///     <item><description>
///     **strict**, 0.95: on the development data it answered 80.6% of the decisions with 3.9 harmful errors in 1,000
///     answered decisions; on the open test split it answered 82.3% with 4.1 in 1,000. Use it when a wrong noun or verb
///     does harm, for example in a rule checker.
///     </description></item>
///     <item><description>
///     **balanced**, 0.80: on the development data it answered 91.0% of the decisions with 9.9 harmful errors in 1,000
///     answered decisions; on the open test split it answered 92.3% with 9.3 in 1,000. Use it when more errors are
///     acceptable or when a person sees the result.
///     </description></item>
///     </list>
///     <para>
///     Treat a token below the threshold as unknown: do not act on its tag, show it to a person, or look at
///     <see cref="TaggedToken.Alternatives"/> and give the word a <see cref="TokenConstraint"/> or a
///     <see cref="Glossary"/> entry. A consumer that needs a low harmful rate must use a threshold, because the harmful
///     rate without one is two to five times higher; the rate is higher still on short technical and imperative text
///     (86.3 in 1,000 on a probe of such sentences without a threshold). The thresholds apply to the embedded model:
///     weights that you load need their own measurement. A token with only one allowed tag has the confidence 1.0 and
///     is not a decision, and the confidence of a token that a hard glossary entry limits to one tag says nothing about
///     whether the entry is right.
///     </para>
///     <para>
///     The development selection, the held-out results on every set, the limits of the measurement and how to reproduce
///     it are in the evaluation record of the repository:
///     https://github.com/demaconsulting/PartOfSpeech/blob/main/docs/evaluation/confidence-thresholds.md (the selection),
///     https://github.com/demaconsulting/PartOfSpeech/blob/main/docs/evaluation/test-results.md (the held-out results)
///     and https://github.com/demaconsulting/PartOfSpeech/blob/main/docs/evaluation/README.md (how to read them).
///     </para>
///     <para>
///     ## Tokenization conventions
///     </para>
///     <para>
///     The tagger was trained on text that is tokenized in one convention, and <see cref="Tokenizer"/> produces exactly
///     that convention. In short: contractions are split (<c>doesn't</c> becomes <c>does</c> and <c>n't</c>, <c>John's</c>
///     becomes <c>John</c> and <c>'s</c>), punctuation marks are tokens of their own, hyphenated compounds stay whole
///     (<c>well-known</c>), and tokens are slices of the source string, so the characters are never changed. The rules
///     with examples are on the page of <see cref="Tokenizer"/>; the complete normative description is
///     https://github.com/demaconsulting/PartOfSpeech/blob/main/data/CONVENTIONS.md in the repository. Text that you
///     tokenize yourself should follow the same convention (see [Offsets and pre-tokenized input](#offsets-and-pre-tokenized-input)).
///     </para>
///     <para>
///     ## Offsets and pre-tokenized input
///     </para>
///     <para>
///     Every token offset (<see cref="Token.Start"/>, <see cref="TaggedToken.Start"/>) indexes the whole string that
///     you gave to the tokenizer or the tagger, so <c>text.Substring(token.Start, token.Length)</c> is always the token
///     text, also in a text with several sentences, line breaks or decomposed characters. Token texts are never
///     normalized: capitalization, accents and quote characters are reported as written.
///     </para>
///     <para>
///     When you already have tokens (from another tokenizer, or from a glossary pipeline), pass them to
///     <see cref="Tagger.Tag(IReadOnlyList{string}, IReadOnlyList{TokenConstraint})"/> or
///     <see cref="Tokenizer.CreateSentence(IReadOnlyList{string})"/>. The tagger does not split them again, so
///     punctuation and clitics must already be separate tokens (<c>Hello</c>, <c>,</c>, <c>world</c>, <c>!</c> and
///     <c>does</c>, <c>n't</c>); a token such as <c>Hello,</c> stays one token and is then a spelling that the model has
///     not seen. No token may be empty or contain white space. The tokens are joined with single spaces into the text of
///     the sentence (<c>Hello , world !</c>), and the offsets index that joined text, not the text that you tokenized
///     elsewhere: keep your own mapping if you need positions in the original. Capitalization and spelling of every
///     token are kept as given.
///     </para>
///     <para>
///     ## Default model and loading cost
///     </para>
///     <para>
///     The package embeds the trained weights and the lexicon, so <see cref="Tagger.Default"/> returns a shared tagger
///     that needs no file: it is created on first use and every caller receives the same thread-safe instance. Create
///     your own tagger from a weights file with <see cref="Tagger.Load(Stream, TaggerOptions?)"/> when you want other
///     weights (the embedded lexicon is used unless you pass one as well).
///     </para>
///     <para>
///     The embedded weights file (<c>weights.bin</c>) is 477,899 bytes compressed and 1,077,990 bytes uncompressed (about
///     0.5 MB and 1.1 MB); it holds 116,448 rows of weights. The embedded lexicon (the file <c>lexicon.bin</c>) is 639,617
///     bytes compressed and 1,625,974 bytes uncompressed and holds 262,635 spellings. Both files are stored as resources
///     of the assembly, so the package grows by their compressed sizes for each target framework. The first read of
///     <see cref="Tagger.Default"/> inflates and validates both files once in the process (tens of milliseconds on a
///     current machine); every later read, and every later tagger that uses the embedded
///     lexicon, reuses the loaded data, while a program that only tokenizes never loads either file.
///     </para>
///     <para>
///     Loading your own weights reads the whole stream, from its current position to its end, into memory, then
///     decompresses and validates the whole file before <see cref="Tagger.Load(Stream, TaggerOptions?)"/> returns; there
///     is no lazy or partial loading. Load once, keep the <see cref="Tagger"/> and share it across threads and calls. The
///     stream is not needed afterwards: the tagger keeps no reference to it, so you may dispose it as soon as Load has
///     returned (the examples use <c>using</c>), and the tagger does not close it itself.
///     </para>
///     <para>
///     ## Glossary
///     </para>
///     <para>
///     A <see cref="Glossary"/> is a set of domain terms that you pass to a <c>Tag</c> call, for example the technical
///     names of a maintenance manual. A general model reads <c>bleed valve</c> as a verb and a noun because <c>bleed</c> is
///     also a verb; the glossary states the answer once and the tagger applies it to every occurrence. You create entries
///     (<see cref="GlossaryEntry"/>: a term of one or more words, the <see cref="Upos"/> tags that its <em>head</em> word takes,
///     and an optional definition), collect them with a <see cref="GlossaryBuilder"/> (or
///     <see cref="Glossary.Create(IEnumerable{GlossaryEntry}, GlossaryOptions?)"/>), and pass the glossary to
///     <see cref="Tagger.Tag(string, Glossary)"/>, <see cref="Tagger.Tag(Sentence, Glossary, IReadOnlyList{TokenConstraint}?)"/>
///     or <see cref="Tagger.Tag(IReadOnlyList{string}, Glossary, IReadOnlyList{TokenConstraint}?)"/>. The glossary belongs to
///     the call, not to the tagger, so <see cref="Tagger.Default"/> can serve any number of glossaries. The head is the word
///     that takes the tags of the entry: the last word of the term by default (<c>Contoso Hub</c> gives <c>Hub</c>), the first
///     word of a phrasal verb (<c>shut down</c> gives <c>shut</c>). The quick start at the end of this page is one complete
///     example; the sections after this list give a comparison of the two modes, a map of the glossary members, the edge
///     cases and the costs.
///     </para>
///     <list type="bullet">
///     <item><description>
///     **Matching.** A sentence is searched from left to right; at each position the longest term that matches wins, and
///     matches never overlap (<c>bleed valve</c> beats <c>valve</c>). A term is split into tokens by the tokenizer, like
///     the text, so <c>bleed-valve</c> is one token and does not match <c>bleed valve</c>, and a pre-tokenized token with a
///     period stuck to it (<c>valve.</c>) does not match <c>valve</c>. <see cref="Glossary.FindMatches(Sentence)"/> finds the
///     terms without tagging, for example to show their definitions. Overlaps, punctuation at the boundary of a term and terms
///     that are not consecutive are covered in "Glossary edge cases" below.
///     </description></item>
///     <item><description>
///     **Inflection.** A glossary lists lemmas and text has plurals and verb forms, so the regular English forms of the
///     <em>head word</em> match too (<c>valves</c>, <c>isolated</c>, <c>isolating</c>), with the tags that fit the form. Irregular
///     forms (<c>bled</c>) are listed with <see cref="GlossaryEntry.WithVariants(string[])"/>, which takes whole-term spellings with
///     the same number of words; an irregular form that is identical to the lemma (<c>set</c> to <c>set</c>, <c>cut</c> to
///     <c>cut</c>) needs no variant, because the term itself already matches it, and a variant that repeats the term or another
///     variant is ignored without an error. Inflection can be switched off for
///     the glossary (<see cref="GlossaryOptions.Inflect"/>) or an entry (<see cref="GlossaryEntry.WithInflection(bool)"/>).
///     </description></item>
///     <item><description>
///     **Letter case.** Terms match in any letter case by default, because a term at the start of a sentence is
///     capitalized. An acronym whose lower-case spelling is another word needs a case-sensitive entry
///     (<see cref="GlossaryEntry.WithCaseSensitive(bool)"/>): the entry <c>IT</c> then leaves the pronoun <c>it</c> alone.
///     </description></item>
///     <item><description>
///     **Modes.** In <see cref="GlossaryMode.Hard"/> mode (the default) the head word can only take the tags of the entry;
///     <see cref="GlossaryMode.Soft"/> mode only prefers them. The table in "Glossary modes: hard and soft" below compares
///     the two and says when to use which.
///     </description></item>
///     <item><description>
///     **Other words of a term.** The other words of a noun term (<c>bleed</c> in <c>bleed valve</c>) can no longer be a verb or an
///     auxiliary, but they are not given the tags of the entry; for a product name that the model has never seen, such as
///     <c>Contoso Hub</c>, say what the first word is with <see cref="GlossaryEntry.WithTokenTags(int, UposSet)"/>. The head of a
///     term is its last word, except for a phrasal verb (a verb term whose later words all belong to a
///     small closed list of particle words that the lexicon allows as adposition, adverb or particle, such as <c>shut down</c> and
///     <c>carry out</c>): its head is the first word, and the second word of such a two-word
///     verb term is limited to adposition, adverb and particle. <see cref="GlossaryEntry"/> lists the particle words;
///     <see cref="GlossaryEntry.WithHead(int)"/> overrides the head in both directions. Every other word (for example <c>it</c> in
///     <c>take it apart</c>) is left to the model, and
///     <see cref="GlossaryEntry.WithTokenTags(int, UposSet)"/> sets the tags of one word explicitly.
///     </description></item>
///     <item><description>
///     **Constraints.** A glossary is turned into per-token constraints, so a tag that it decides becomes history for the next
///     tokens: the neighbors after a match can change, the tokens before the first match never do. Your own constraints, passed
///     as the <c>constraints</c> parameter of the same call, are combined with those of the glossary per token. When you give no
///     constraint for a token, the constraint of the glossary applies (hard or soft); when the glossary has none for the token,
///     yours applies; when both exist they are combined with <see cref="TokenConstraint.And(TokenConstraint)"/>. If the allowed
///     tags of the two have no tag in common (your constraint contradicts a hard entry, for example you allow only <c>VERB</c>
///     for a head word that the entry limits to <c>NOUN</c>), <strong>your constraint wins</strong> for that token. The reason
///     is that your constraint is explicit and belongs to one call, while the glossary is the default for all calls. A soft
///     glossary constraint allows every tag, so it never conflicts with yours.
///     </description></item>
///     <item><description>
///     **Results.** Every token of a match reports it (<see cref="TaggedToken.GlossaryMatch"/>), the head word is flagged
///     (<see cref="TaggedToken.IsGlossaryHead"/>), and the sentence lists its matches in text order
///     (<see cref="TaggedSentence.GlossaryMatches"/>) with the entry, the definition, the tokens and the offsets in the string.
///     </description></item>
///     <item><description>
///     **Confidence.** The confidence of a glossary token measures the choice among the tags that are still allowed after the
///     constraints (the softmax over them, see [Confidence and alternatives](#confidence-and-alternatives)). It is <em>not</em>
///     evidence that the entry is correct. A head word that a hard entry limits to one tag always has the confidence exactly
///     1.0 and no alternatives, also when the entry is wrong: there was no choice, so the value does not say that the model is
///     sure. With several tags or in soft mode the confidence is the softmax over the tags that remain, so a soft preference
///     shows in the number.
///     </description></item>
///     </list>
///     <para>
///     ## Glossary modes: hard and soft
///     </para>
///     <para>
///     The two modes differ in what they do with the head word of a match, in when to use them, and in what a wrong entry
///     costs:
///     </para>
///     <para>
///     | | Hard (the default) | Soft |
///     | --- | --- | --- |
///     | What it does | Limits the head word to the tags of the entry; the model cannot disagree. | Adds a boost of <see cref="GlossaryOptions.SoftStrength"/> times the model temperature to the tags of the entry; the model can still disagree when it is clearly sure of another tag. |
///     | When to use it | A curated list that you trust completely, such as the ASD-STE100 technical names and verbs. | A list that is not curated or may contain wrong entries. |
///     | With wrong entries | A wrong entry forces a wrong tag: hard mode makes confident errors. | The accuracy stays near that of tagging without a glossary, because the model can overrule a wrong entry. |
///     </para>
///     <para>
///     In short: with wrong entries, hard mode makes confident errors, while soft mode stays near no-glossary accuracy. The
///     full measurements (simulated glossaries on development data, with 0, 10 and 30 percent wrong entries, and the effect of
///     the strength) are in the trainer run log of the repository, section E9:
///     https://github.com/demaconsulting/PartOfSpeech/blob/main/tools/DemaConsulting.PartOfSpeech.Trainer/RUN-LOG.md
///     </para>
///     <para>
///     ## Glossary navigation
///     </para>
///     <para>
///     Where to find each part of the glossary:
///     </para>
///     <list type="table">
///     <listheader>
///     <term>Area</term>
///     <description>Members</description>
///     </listheader>
///     <item>
///     <term>Construction</term>
///     <description><see cref="GlossaryBuilder"/> (collect entries with a collection initializer or
///     <see cref="GlossaryBuilder.Add(GlossaryEntry)"/>, then <see cref="GlossaryBuilder.Build"/>);
///     <see cref="Glossary.Create(IEnumerable{GlossaryEntry}, GlossaryOptions?)"/> (build from a list);
///     <see cref="GlossaryEntry"/> (one term, its tags and definition, and its settings
///     <see cref="GlossaryEntry.WithVariants(string[])"/>, <see cref="GlossaryEntry.WithTokenTags(int, UposSet)"/>,
///     <see cref="GlossaryEntry.WithInflection(bool)"/>, <see cref="GlossaryEntry.WithHead(int)"/>,
///     <see cref="GlossaryEntry.WithCaseSensitive(bool)"/>, <see cref="GlossaryEntry.WithMode(GlossaryMode)"/> and
///     <see cref="GlossaryEntry.WithDefinition(string?)"/>); <see cref="GlossaryOptions"/> (settings for all entries) with
///     <see cref="GlossaryMode"/></description>
///     </item>
///     <item>
///     <term>Matching</term>
///     <description><see cref="Glossary.FindMatches(Sentence)"/> (find the terms of a sentence without tagging) and
///     <see cref="GlossaryMatch"/> (one occurrence: entry, first token, token count, head token, text and offsets)</description>
///     </item>
///     <item>
///     <term>Tagging</term>
///     <description><see cref="Tagger.Tag(string, Glossary)"/>,
///     <see cref="Tagger.Tag(Sentence, Glossary, IReadOnlyList{TokenConstraint}?)"/> and
///     <see cref="Tagger.Tag(IReadOnlyList{string}, Glossary, IReadOnlyList{TokenConstraint}?)"/></description>
///     </item>
///     <item>
///     <term>Results</term>
///     <description><see cref="TaggedToken.GlossaryMatch"/> (the match that a token belongs to),
///     <see cref="TaggedToken.IsGlossaryHead"/> (the head word of the match), <see cref="TaggedToken.IsConstrained"/> and
///     <see cref="TaggedSentence.GlossaryMatches"/> (all matches of a sentence in text order)</description>
///     </item>
///     </list>
///     <para>
///     The exceptions of the glossary members, and when they are thrown:
///     </para>
///     <list type="bullet">
///     <item><description>
///     Creating an entry (<see cref="GlossaryEntry"/> constructors, also through <see cref="GlossaryBuilder.Add(string, Upos, string?)"/>):
///     <see cref="ArgumentNullException"/> for a <c>null</c> term; <see cref="ArgumentException"/> for a term that is empty or
///     has no token with a letter or a digit, or for an empty tag set; <see cref="ArgumentOutOfRangeException"/> for a tag that
///     is not one of the 17 defined values.
///     </description></item>
///     <item><description>
///     Changing an entry: <see cref="GlossaryEntry.WithHead(int)"/>, <see cref="GlossaryEntry.WithTokenTags(int, UposSet)"/>
///     and <see cref="GlossaryEntry.GetTokenTags(int)"/> throw <see cref="ArgumentOutOfRangeException"/> for an index outside the
///     term; <see cref="GlossaryEntry.WithTokenTags(int, UposSet)"/> throws <see cref="ArgumentException"/> for the head token
///     or an empty tag set; <see cref="GlossaryEntry.WithMode(GlossaryMode)"/> throws <see cref="ArgumentOutOfRangeException"/>
///     for a mode that is not hard or soft; <see cref="GlossaryEntry.WithVariants(string[])"/> throws
///     <see cref="ArgumentNullException"/> for a <c>null</c> variant list or item and <see cref="ArgumentException"/> for a
///     variant that is empty, has no token with a letter or a digit, or has another number of tokens than the term.
///     </description></item>
///     <item><description>
///     Building: the <see cref="GlossaryBuilder"/> constructor and <see cref="Glossary.Create(IEnumerable{GlossaryEntry}, GlossaryOptions?)"/>
///     throw <see cref="ArgumentOutOfRangeException"/> for options whose mode is not hard or soft or whose
///     <see cref="GlossaryOptions.SoftStrength"/> is not finite and greater than zero; <see cref="GlossaryBuilder.Add(GlossaryEntry)"/>
///     and <see cref="Glossary.Create(IEnumerable{GlossaryEntry}, GlossaryOptions?)"/> throw <see cref="ArgumentNullException"/> for a
///     <c>null</c> entry or list and <see cref="ArgumentException"/> for two entries with the same tokens under the same
///     effective letter-case rule (merge their tags into one entry); <see cref="GlossaryBuilder.Add(GlossaryEntry)"/> reports the
///     duplicate at the line that added it.
///     </description></item>
///     <item><description>
///     Matching and tagging: <see cref="Glossary.FindMatches(Sentence)"/> throws <see cref="ArgumentNullException"/> for a
///     <c>null</c> sentence; the <see cref="Tagger"/> methods that take a glossary throw <see cref="ArgumentNullException"/> for a
///     <c>null</c> text, sentence, token list or glossary and <see cref="ArgumentException"/> for a token that is empty or
///     contains white space or for a constraint list whose length differs from the number of tokens. The content of the text
///     never makes matching or tagging fail.
///     </description></item>
///     </list>
///     <para>
///     ## Glossary edge cases
///     </para>
///     <para>
///     Situations that often surprise, with what happens and what to do:
///     </para>
///     <list type="table">
///     <listheader>
///     <term>Situation</term>
///     <description>What happens and what to do</description>
///     </listheader>
///     <item>
///     <term>Overlapping entries: <c>bleed valve</c> and <c>bleed valve cover</c></term>
///     <description>The longest term at a position wins and matches never overlap: <c>Remove the bleed valve cover.</c> gives
///     one match, <c>bleed valve cover</c> (head <c>cover</c>); <c>Close the bleed valve.</c> matches the shorter entry. You do
///     not have to order the entries.</description>
///     </item>
///     <item>
///     <term>Punctuation at the boundary of a term: <c>(Contoso Hub),</c> or a final period</term>
///     <description>Punctuation marks are tokens of their own, so they neither prevent a match nor belong to it:
///     <c>Restart (Contoso Hub), then wait.</c> and <c>Restart the Contoso Hub.</c> both match <c>Contoso Hub</c> (two tokens).
///     A hyphen inside a word is not punctuation: in <c>the Contoso Hub-based gear</c> the token is <c>Hub-based</c>, which does
///     not match <c>Hub</c>.</description>
///     </item>
///     <item>
///     <term>A word inside a phrasal verb: <c>shut it down</c></term>
///     <description>A term matches only consecutive tokens, so the entry <c>shut down</c> does not match <c>shut it down</c>.
///     Add a second entry, <c>shut it down</c>, with <see cref="GlossaryEntry.WithHead(int)"/> set to the first word.</description>
///     </item>
///     <item>
///     <term>An unknown particle: <c>look after</c></term>
///     <description>The head of a verb term is its first word only when every later word is in the list of particle words that
///     <see cref="GlossaryEntry"/> gives. <c>after</c> is not in the list, so the head falls back to the last word, <c>after</c>,
///     and a hard verb entry would force <c>after</c> to be a verb. Use <c>WithHead(0)</c>: <c>look</c> is then the verb and
///     <c>after</c> is limited to adposition, adverb and particle.</description>
///     </item>
///     <item>
///     <term>A phrasal verb of three words: <c>look forward to</c>, <c>get back together</c></term>
///     <description>In <c>look forward to</c>, <c>to</c> is not in the list, so the head falls back to <c>to</c>: use
///     <c>WithHead(0)</c>. In <c>get back together</c> every later word is in the list, so the head is <c>get</c> without a
///     call. In both, only the head is constrained: the other words are left to the model (the rule that limits the particle
///     applies to two-word verb terms), and <see cref="GlossaryEntry.WithTokenTags(int, UposSet)"/> sets them if you need to.</description>
///     </item>
///     </list>
///     <para>
///     ## Glossary costs
///     </para>
///     <para>
///     Building a glossary is linear in the number of entries and takes milliseconds for thousands of entries: measured
///     once on a developer machine (Release build, one thread, after warm-up), about 5 milliseconds for 1,000 entries and
///     about 50 milliseconds for 10,000. A tagger compiles the constraints of a glossary once, when the first sentence that
///     contains a match of it is tagged (a fraction of a millisecond for 1,000 entries and a few milliseconds for 10,000), keeps
///     them for as long as the glossary object is alive, and reuses them for every later call; a new glossary object, also one
///     built from the same entries, is compiled again, so build a glossary once and keep it. Matching costs at most the length
///     of the longest term per token, whatever the size of the glossary. These figures are the only place in this reference
///     that gives the cost; the other pages refer to this section.
///     </para>
///     <para>
///     ## Thread safety
///     </para>
///     <para>
///     A <see cref="Tagger"/> is immutable after creation and may be used from any number of threads at once; share one
///     instance. A <see cref="Glossary"/> and all result types are immutable too, and <see cref="Tokenizer"/> is stateless, so
///     they are also safe to share. A <see cref="GlossaryBuilder"/> is not thread-safe.
///     </para>
///     <para>
///     ## Errors
///     </para>
///     <para>
///     The same rules apply to every member, and each member page lists its own exceptions. A <c>null</c> argument
///     throws <see cref="ArgumentNullException"/>. A value of the wrong kind throws <see cref="ArgumentException"/>: a
///     token that is empty or contains white space, a constraint list that does not have one element per token, an
///     empty tag set for <see cref="TokenConstraint.Only(UposSet)"/>, a glossary term without a letter or a digit or two
///     entries for the same term. A number or enum value out of range throws
///     <see cref="ArgumentOutOfRangeException"/> (a <see cref="TaggerOptions.MaxAlternatives"/> outside 0 to 16, a tag
///     that is not one of the 17 defined values, a boost or penalty that is not finite and greater than zero). A
///     weights or lexicon file that is damaged, of an unsupported version or built for another feature set throws
///     <see cref="InvalidDataException"/> when the tagger is created; a stream that cannot be read throws the exception
///     of the stream (for example <see cref="NotSupportedException"/> or <see cref="ObjectDisposedException"/>). Reading
///     <see cref="Tagger.Default"/> does not fail in a released package (an <see cref="InvalidOperationException"/> or
///     <see cref="InvalidDataException"/> would mean that the package is broken).
///     Tokenizing never fails for a non-null string. Tagging does not depend on the content of the text either: no text
///     makes it fail, including a text with an unpaired surrogate (which is not valid UTF-16); such a character is
///     treated like U+FFFD for the features of its token only, and the token text and offsets stay those of the input.
///     </para>
///     <para>
///     ## Quick start
///     </para>
/// </remarks>
/// <example>
///     Tag a text with the embedded model and read the tags, confidences and alternatives:
///     <code>
///     var tagger = Tagger.Default;
///
///     foreach (var sentence in tagger.Tag("The cat sat on the mat. Does it purr?"))
///     {
///         foreach (var token in sentence.Tokens)
///         {
///             Console.WriteLine($"{token.Text} {token.Tag.ToTagString()} {token.Confidence:0.00}");
///             foreach (var alternative in token.Alternatives)
///             {
///                 Console.WriteLine($"    or {alternative.Tag.ToTagString()} {alternative.Probability:0.00}");
///             }
///         }
///     }
///     </code>
///     To use other weights, create a tagger from a weights file instead:
///     <code>
///     using var weights = File.OpenRead("model.posw");
///     var tagger = Tagger.Load(weights);
///     </code>
///     <para>
///     ## Glossary quick start
///     </para>
///     <para>
///     One complete example of a glossary with the four kinds of term that cause most questions: a multiword product name
///     (<c>Contoso Hub</c>, a proper noun whose first word the model has never seen), an acronym whose lower-case spelling is
///     another word (<c>IT</c> against the pronoun <c>it</c>, so the entry is case-sensitive), a hyphenated verb
///     (<c>re-sync</c>, which the tokenizer keeps as one token, so <c>re-sync</c> and its forms <c>re-syncs</c>,
///     <c>re-synced</c> and <c>re-syncing</c> match, while the spelling <c>resync</c> is another word and needs a variant) and
///     a phrasal verb (<c>shut down</c>, whose head is the first word). The loop then reads what a match reports: the
///     <see cref="GlossaryMatch.Entry"/>, the span (<see cref="GlossaryMatch.FirstToken"/> and
///     <see cref="GlossaryMatch.TokenCount"/>), the <see cref="GlossaryMatch.HeadToken"/>, the matched
///     <see cref="GlossaryMatch.Text"/> as written, and <see cref="TaggedToken.IsGlossaryHead"/> on the tokens of the span.
///     </para>
///     <code>
///     Glossary glossary = new GlossaryBuilder
///     {
///         // A product name: the head Hub takes the tag of the entry, and Contoso is set to PROPN as well
///         new GlossaryEntry("Contoso Hub", Upos.Propn, "The Contoso device manager.")
///             .WithTokenTags(0, UposSet.Of(Upos.Propn)),
///
///         // An acronym: only the capitals match, so the pronoun "it" is left alone
///         new GlossaryEntry("IT", Upos.Noun, "Information technology.").WithCaseSensitive(true),
///
///         // A hyphenated verb is one token; "resync" is another spelling of the same term
///         new GlossaryEntry("re-sync", Upos.Verb).WithVariants("resync"),
///
///         // A phrasal verb: the head is the first word (shut), and down is limited to ADP, ADV or PART
///         { "shut down", Upos.Verb },
///     }.Build();
///
///     string text = "Ask IT to re-sync the Contoso Hub, then shut down the unit. Ask it to resync.";
///     foreach (TaggedSentence sentence in tagger.Tag(text, glossary))
///     {
///         foreach (GlossaryMatch match in sentence.GlossaryMatches)
///         {
///             TaggedToken head = sentence.Tokens[match.HeadToken];
///             var span = sentence.Tokens.Skip(match.FirstToken).Take(match.TokenCount);
///
///             Console.WriteLine($"{match.Entry.Term}: '{match.Text}', {match.TokenCount} token(s), "
///                 + $"head {head.Text} {head.Tag.ToTagString()} {head.Confidence:0.00}");
///             Console.WriteLine("    " + string.Join(" ", span.Select(t => t.Text + (t.IsGlossaryHead ? "*" : ""))));
///         }
///     }
///
///     // IT: 'IT', 1 token(s), head IT NOUN 1.00
///     //     IT*
///     // re-sync: 're-sync', 1 token(s), head re-sync VERB 1.00
///     //     re-sync*
///     // Contoso Hub: 'Contoso Hub', 2 token(s), head Hub PROPN 1.00
///     //     Contoso Hub*
///     // shut down: 'shut down', 2 token(s), head shut VERB 1.00
///     //     shut* down
///     // re-sync: 'resync', 1 token(s), head resync VERB 1.00      (the second sentence; "it" is no match)
///     //     resync*
///     </code>
///     <para>
///     The confidence of every head here is 1.00 because each hard entry leaves one tag: it says that there was no choice, not
///     that the entries were checked. The word <c>down</c> is limited to adposition, adverb and particle and the model picks
///     among them; <c>Contoso</c> is a proper noun because of <see cref="GlossaryEntry.WithTokenTags(int, UposSet)"/>.
///     </para>
/// </example>
internal static class NamespaceDoc;

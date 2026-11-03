namespace DemaConsulting.PartOfSpeech;

/// <summary>
///     Part-of-speech tagging for English text: a tokenizer, a sentence splitter and a greedy averaged-perceptron tagger
///     that assigns one of the 17 Universal POS tags to every token, with a confidence, ranked alternatives and
///     optional per-token constraints. This is the whole public API of the package
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
///     restrict or nudge the tag of one token. <see cref="UposSet"/> is an allocation-free set of tags, and
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
///     The temperature is meant to make the probabilities match how often the chosen tag is right; how well it does that
///     is measured for each trained model. What confidence is low enough to check a token again is the choice of your
///     application: this release does not recommend a threshold, and measured guidance will be added to this reference
///     once the trained model has been evaluated.
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
///     This build embeds the lexicon but no trained weights, so <see cref="Tagger.Default"/> throws
///     <see cref="InvalidOperationException"/>. Create a tagger from a weights file with
///     <see cref="Tagger.Load(Stream, TaggerOptions?)"/>; a later release embeds a trained model, and
///     <see cref="Tagger.Default"/> then returns a shared tagger without a file.
///     </para>
///     <para>
///     Loading reads the whole stream, from its current position to its end, into memory, then decompresses and
///     validates the whole file before <see cref="Tagger.Load(Stream, TaggerOptions?)"/> returns; there is no lazy or
///     partial loading. Load once, keep the <see cref="Tagger"/> and share it across threads and calls. The stream is
///     not needed afterwards: the tagger keeps no reference to it, so you may dispose it as soon as Load has returned
///     (the examples use <c>using</c>), and the tagger does not close it itself. The embedded lexicon (the file
///     <c>lexicon.bin</c>) is 641,418 bytes compressed and holds 262,667 spellings; the first creation of a tagger that
///     uses it (<see cref="Tagger.Default"/> or <see cref="Tagger.Load(Stream, TaggerOptions?)"/>) inflates and validates
///     it once in the process, and every later tagger reuses it, while a program that only tokenizes never loads it.
///     The size and load time of the trained weights will be stated here when they are embedded.
///     </para>
///     <para>
///     ## Thread safety
///     </para>
///     <para>
///     A <see cref="Tagger"/> is immutable after creation and may be used from any number of threads at once; share one
///     instance. All result types and <see cref="Tokenizer"/> are immutable or stateless and are also safe to share.
///     </para>
///     <para>
///     ## Errors
///     </para>
///     <para>
///     The same rules apply to every member, and each member page lists its own exceptions. A <c>null</c> argument
///     throws <see cref="ArgumentNullException"/>. A value of the wrong kind throws <see cref="ArgumentException"/>: a
///     token that is empty or contains white space, a constraint list that does not have one element per token, an
///     empty tag set for <see cref="TokenConstraint.Only(UposSet)"/>. A number or enum value out of range throws
///     <see cref="ArgumentOutOfRangeException"/> (a <see cref="TaggerOptions.MaxAlternatives"/> outside 0 to 16, a tag
///     that is not one of the 17 defined values, a boost or penalty that is not finite and greater than zero). A
///     weights or lexicon file that is damaged, of an unsupported version or built for another feature set throws
///     <see cref="InvalidDataException"/> when the tagger is created; a stream that cannot be read throws the exception
///     of the stream (for example <see cref="NotSupportedException"/> or <see cref="ObjectDisposedException"/>). Reading
///     <see cref="Tagger.Default"/> throws <see cref="InvalidOperationException"/> while no model is embedded.
///     Tokenizing never fails for a non-null string. Tagging does not depend on the content of the text either, with one
///     exception: a text that contains an unpaired surrogate (which is not valid UTF-16) makes the tagger throw
///     <see cref="ArgumentException"/> from the Unicode normalization that it applies for the lexicon lookup.
///     </para>
///     <para>
///     ## Quick start
///     </para>
/// </remarks>
/// <example>
///     Create a tagger from a weights file, tag a text and read the tags, confidences and alternatives:
///     <code>
///     using var weights = File.OpenRead("model.posw");
///     var tagger = Tagger.Load(weights);
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
/// </example>
internal static class NamespaceDoc;

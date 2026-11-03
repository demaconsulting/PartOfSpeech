namespace DemaConsulting.PartOfSpeech;

/// <summary>
///     Part-of-speech tagging for English text: a tokenizer, a sentence splitter and an averaged-perceptron tagger that
///     assigns one of the 17 Universal POS tags to every token, with a calibrated confidence, ranked alternatives and
///     optional per-token constraints. This is the whole public API of the package <c>DemaConsulting.PartOfSpeech</c>.
/// </summary>
/// <remarks>
///     <para>
///     Package: the NuGet package <c>DemaConsulting.PartOfSpeech</c>, which targets <c>netstandard2.0</c>,
///     <c>net8.0</c>, <c>net9.0</c> and <c>net10.0</c> and has no package dependencies, no native code and no model
///     runtime. Everything is in the single namespace <c>DemaConsulting.PartOfSpeech</c>; there are no sub-namespaces to
///     import.
///     </para>
///     <para>
///     What is in the package, in the order that you meet the types. <see cref="Tokenizer"/> splits a string into
///     <see cref="Sentence"/> objects whose <see cref="Token"/> items know their exact offsets in the source string.
///     <see cref="Tagger"/> tags a text, a <see cref="Sentence"/> or an array of tokens that you already split, and
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
///     Terminology.
///     <strong>UPOS</strong> is the Universal POS tag set of the Universal Dependencies project: 17 coarse tags such as
///     <see cref="Upos.Noun"/>, <see cref="Upos.Verb"/>, <see cref="Upos.Adj"/> and <see cref="Upos.Punct"/>, represented
///     by the enum <see cref="Upos"/> and spelled in upper case (<c>NOUN</c>) in text.
///     The <strong>lexicon</strong> is a compiled list of English spellings with the tags that the data supports for each
///     of them; it is embedded in the package. A word that is in the lexicon can only take its listed tags, which
///     removes impossible answers; a word that is not in the lexicon may take any of the 17 tags. The
///     <strong>weights</strong> are the trained perceptron model, a <c>POSW</c> file that you load with
///     <see cref="Tagger.Load(Stream, TaggerOptions?)"/>; a <c>POSL</c> file is a replacement lexicon.
///     The <strong>confidence</strong> of a token (<see cref="TaggedToken.Confidence"/>) is the probability of its tag
///     among the tags that were allowed for the token, calibrated by the temperature of the model, from 0 to 1. It
///     measures the choice between the allowed tags only: when the lexicon leaves a single tag the confidence is 1.0
///     whatever the model thinks, so judge the confidence on tokens that had several candidates. The
///     <strong>alternatives</strong> (<see cref="TaggedToken.Alternatives"/>) are the other allowed tags in descending
///     probability.
///     A <strong>constraint</strong> (<see cref="TokenConstraint"/>) is knowledge that you add for one token, for example a
///     glossary that says a word is a noun in this document. It is hard when it limits the allowed tags
///     (<see cref="TokenConstraint.Only(Upos[])"/>) and soft when it adds to or subtracts from the score of a tag
///     (<see cref="TokenConstraint.Prefer"/>, <see cref="TokenConstraint.Penalize"/>). A hard constraint that excludes
///     every tag of the lexicon wins over the lexicon, and a chosen tag also influences the tags of the following tokens.
///     </para>
///     <para>
///     Default model. This build embeds the lexicon but no trained weights, so <see cref="Tagger.Default"/> throws
///     <see cref="InvalidOperationException"/>. Create a tagger from a weights file with
///     <see cref="Tagger.Load(Stream, TaggerOptions?)"/>; a later release embeds a trained model and then
///     <see cref="Tagger.Default"/> returns a shared tagger.
///     </para>
///     <para>
///     Offsets. Every token offset (<see cref="Token.Start"/>, <see cref="TaggedToken.Start"/>) indexes the whole
///     string that you gave to the tagger, so <c>text.Substring(token.Start, token.Length)</c> is always the token
///     text. Token texts are never normalized. When you tag tokens that you already split, the source string is the
///     tokens joined with single spaces.
///     </para>
///     <para>
///     Thread safety. A <see cref="Tagger"/> is immutable after creation and may be used from any number of threads at
///     once; share one instance. All result types and <see cref="Tokenizer"/> are immutable or stateless and are also
///     safe to share.
///     </para>
///     <para>
///     Errors. A <c>null</c> argument throws <see cref="ArgumentNullException"/>, a value out of range throws
///     <see cref="ArgumentOutOfRangeException"/>, and a model or lexicon file that is damaged, of an unsupported version
///     or built for another feature set throws <see cref="InvalidDataException"/> when the tagger is created. Tagging
///     itself does not fail for any text.
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

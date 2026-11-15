// cspell:ignore zzqxv
using System.Runtime.CompilerServices;

namespace DemaConsulting.PartOfSpeech;

/// <summary>
///     Assigns a Universal POS tag to every token of a text, with a confidence and ranked alternatives.
/// </summary>
/// <remarks>
///     <para>
///         Create a tagger once with <see cref="Load(Stream, TaggerOptions?)"/> and share it: it holds the trained
///         weights and the lexicon, and tagging a text, a <see cref="Sentence"/> or a list of tokens that you already
///         split never changes it. The words used here (greedy, averaged perceptron, lexicon, raw score, temperature,
///         constraint) are defined once in
///         [Terminology](../DemaConsulting.PartOfSpeech.md#terminology), and how to read the confidence and the
///         alternatives of a token is explained in
///         [Confidence and alternatives](../DemaConsulting.PartOfSpeech.md#confidence-and-alternatives). Callers can
///         inject knowledge per token with a <see cref="TokenConstraint"/>, or per domain term with a
///         <see cref="Glossary"/> that is passed to the <c>Tag</c> methods that take one.
///     </para>
///     <para>
///         <strong>Thread safety:</strong> a <see cref="Tagger"/> is immutable after creation and all members may be
///         called concurrently from any number of threads; every call uses its own per-thread scratch memory. The
///         results are immutable too.
///     </para>
///     <para>
///         <strong>Default model:</strong> <see cref="Default"/> is the shared tagger that uses the trained weights
///         and the lexicon embedded in the package, so you need no file; it is created on first use. Create your own
///         tagger with <see cref="Load(Stream, TaggerOptions?)"/> to use other weights. Loading cost and the lifetime of
///         the stream are described in
///         [Default model and loading cost](../DemaConsulting.PartOfSpeech.md#default-model-and-loading-cost).
///     </para>
///     <para>
///         <strong>Word lookup:</strong> <see cref="GetPossibleTags(string, PossibleTagsMode)"/> and
///         <see cref="IsFormOf(string, string, Upos)"/> answer questions about one word without a sentence: which tags the
///         lexicon lists or the tagger can give it, whether the lexicon recognizes it, and its base word and features.
///     </para>
///     <para>
///         <strong>Exceptions:</strong> creating a tagger throws <see cref="ArgumentNullException"/> for a <c>null</c>
///         stream, <see cref="ArgumentOutOfRangeException"/> for options out of range and
///         <see cref="InvalidDataException"/> for a damaged file or one of an unsupported version or feature set. The
///         tagging methods throw <see cref="ArgumentNullException"/> for a <c>null</c> text, sentence or token list and
///         <see cref="ArgumentException"/> for an invalid token or a constraint list of the wrong length. The general
///         rules are in [Errors](../DemaConsulting.PartOfSpeech.md#errors); every member page lists its own exceptions.
///     </para>
/// </remarks>
/// <example>
///     Tag a text with the model that is embedded in the package, and read the tags and confidences:
///     <code>
///     TaggedSentence sentence = Tagger.Default.Tag("The cat sat on the mat.")[0];
///     foreach (TaggedToken token in sentence.Tokens)
///     {
///         Console.WriteLine($"{token.Text}\t{token.Tag.ToTagString()}\t{token.Confidence:0.00}");
///     }
///     </code>
///     To use other weights, create a tagger from a weights file once and share it:
///     <code>
///     using var weights = File.OpenRead("model.posw");
///     var tagger = Tagger.Load(weights);
///
///     TaggedSentence sentence = tagger.Tag("The cat sat on the mat.")[0];
///     </code>
/// </example>
public sealed class Tagger
{
    /// <summary>
    ///     The message for a word that is empty or contains white space.
    /// </summary>
    private const string WordRuleMessage =
        "A word must be one token: not empty and without white space. Split a text into tokens first, or use a Glossary for multiword terms.";

    /// <summary>
    ///     The lazily created default tagger.
    /// </summary>
    private static readonly Lazy<Tagger> LazyDefault = new(CreateDefault, LazyThreadSafetyMode.ExecutionAndPublication);

    /// <summary>
    ///     The decoder that does the work.
    /// </summary>
    private readonly TagDecoder _decoder;

    /// <summary>
    ///     The maximum number of alternatives per token.
    /// </summary>
    private readonly int _maxAlternatives;

    /// <summary>
    ///     The compiled constraint tables of the glossaries that this tagger has seen (one table per glossary, created on first use).
    /// </summary>
    /// <remarks>
    ///     A soft boost is stated in units of the model temperature, so the tables depend on this tagger's model. The table is
    ///     weak, so a glossary that the application drops is released together with its tables, and thread-safe, so
    ///     concurrent first uses of one glossary all end up with equal tables.
    /// </remarks>
    private readonly ConditionalWeakTable<Glossary, GlossaryConstraints> _compiledGlossaries = new();

    /// <summary>
    ///     The callback that compiles a glossary for this tagger (created once, so that looking up a glossary allocates nothing).
    /// </summary>
    private readonly ConditionalWeakTable<Glossary, GlossaryConstraints>.CreateValueCallback _compileGlossary;

    /// <summary>
    ///     Initializes a new instance of the <see cref="Tagger"/> class.
    /// </summary>
    /// <param name="model">The weights.</param>
    /// <param name="lexicon">The lexicon.</param>
    /// <param name="maxAlternatives">The maximum number of alternatives per token.</param>
    private Tagger(PerceptronModel model, Lexicon lexicon, int maxAlternatives)
    {
        _decoder = new TagDecoder(model, lexicon);
        _maxAlternatives = maxAlternatives;
        _compileGlossary = glossary => GlossaryConstraints.Compile(glossary, _decoder.Temperature);
    }

    /// <summary>
    ///     Gets the shared tagger that uses the model embedded in the package.
    /// </summary>
    /// <remarks>
    ///     The package embeds the trained weights and the lexicon. The tagger is created on first use (inflating and
    ///     validating both embedded files once, see
    ///     [Default model and loading cost](../../DemaConsulting.PartOfSpeech.md#default-model-and-loading-cost)) and
    ///     every caller receives the same thread-safe instance; a program that never reads this property pays nothing.
    /// </remarks>
    /// <exception cref="InvalidOperationException">An embedded resource is missing from the assembly (a broken package; not expected in a released package).</exception>
    /// <exception cref="InvalidDataException">The embedded model is damaged or built for another feature set (not expected in a released package).</exception>
    /// <example>
    ///     Tag a text with the embedded model, with no file to load:
    ///     <code>
    ///     TaggedSentence sentence = Tagger.Default.Tag("The cat sat on the mat.")[0];
    ///     Console.WriteLine(sentence.Tokens[1].Tag.ToTagString());   // NOUN
    ///     </code>
    /// </example>
    public static Tagger Default => LazyDefault.Value;

    /// <summary>
    ///     Gets a copy of the options that the tagger was created with.
    /// </summary>
    public TaggerOptions Options => new() { MaxAlternatives = _maxAlternatives };

    /// <summary>
    ///     Creates a tagger from a weights file, using the lexicon that is embedded in the package.
    /// </summary>
    /// <remarks>
    ///     Loading reads the whole stream into memory and decompresses and validates the whole file before it returns,
    ///     so do it once and share the tagger across threads. The stream is read from its current position to its end
    ///     and is not closed; the tagger keeps no reference to it, so it may be disposed as soon as this method has
    ///     returned. The first tagger of the process also loads the embedded lexicon (762,836 bytes compressed, 262,635
    ///     spellings with the analyses of their forms), which later taggers reuse. See
    ///     [Default model and loading cost](../../DemaConsulting.PartOfSpeech.md#default-model-and-loading-cost).
    /// </remarks>
    /// <param name="weights">A stream that holds a <c>POSW</c> weights file, read from its current position to its end; it is not closed and may be disposed after this method returns.</param>
    /// <param name="options">The settings, or <c>null</c> for the defaults.</param>
    /// <returns>The tagger.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="weights"/> is <c>null</c>.</exception>
    /// <exception cref="ArgumentOutOfRangeException"><see cref="TaggerOptions.MaxAlternatives"/> of <paramref name="options"/> is outside 0 to 16.</exception>
    /// <exception cref="InvalidDataException">The file is not a valid weights file of a supported version and feature set (or the embedded lexicon is damaged).</exception>
    /// <exception cref="NotSupportedException"><paramref name="weights"/> does not support reading.</exception>
    /// <exception cref="ObjectDisposedException"><paramref name="weights"/> is already closed.</exception>
    /// <exception cref="IOException">Reading <paramref name="weights"/> failed.</exception>
    /// <example>
    ///     Load weights from a file and ask for at most five alternatives per token:
    ///     <code>
    ///     using var weights = File.OpenRead("model.posw");
    ///     var tagger = Tagger.Load(weights, new TaggerOptions { MaxAlternatives = 5 });
    ///     </code>
    /// </example>
    public static Tagger Load(Stream weights, TaggerOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(weights);
        var maxAlternatives = ValidateOptions(options);
        return new Tagger(PerceptronModel.Load(weights), EmbeddedModel.Lexicon, maxAlternatives);
    }

    /// <summary>
    ///     Creates a tagger from a weights file and a lexicon file.
    /// </summary>
    /// <remarks>
    ///     Both files are read completely, decompressed and validated before this method returns, and both streams may
    ///     be disposed afterwards (see <see cref="Load(Stream, TaggerOptions?)"/>). The lexicon is loaded for this
    ///     tagger only; the embedded lexicon is not used. The lexicon also feeds the features of a weights file with
    ///     feature groups (the clause-cue, position and inflection features read it), so a lexicon that differs from
    ///     the one the weights were trained with changes the features the model sees, not only the allowed tags.
    /// </remarks>
    /// <param name="weights">A stream that holds a <c>POSW</c> weights file, read from its current position to its end; it is not closed and may be disposed after this method returns.</param>
    /// <param name="lexicon">A stream that holds a <c>POSL</c> lexicon file, read from its current position to its end; it is not closed and may be disposed after this method returns.</param>
    /// <param name="options">The settings, or <c>null</c> for the defaults.</param>
    /// <returns>The tagger.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="weights"/> or <paramref name="lexicon"/> is <c>null</c>.</exception>
    /// <exception cref="ArgumentOutOfRangeException"><see cref="TaggerOptions.MaxAlternatives"/> of <paramref name="options"/> is outside 0 to 16.</exception>
    /// <exception cref="InvalidDataException">A file is invalid, of an unsupported version or of another feature set.</exception>
    /// <exception cref="NotSupportedException">A stream does not support reading.</exception>
    /// <exception cref="ObjectDisposedException">A stream is already closed.</exception>
    /// <exception cref="IOException">Reading a stream failed.</exception>
    /// <example>
    ///     Load weights together with a lexicon file instead of the embedded lexicon:
    ///     <code>
    ///     using var weights = File.OpenRead("model.posw");
    ///     using var lexicon = File.OpenRead("lexicon.posl");
    ///     var tagger = Tagger.Load(weights, lexicon);
    ///     </code>
    /// </example>
    public static Tagger Load(Stream weights, Stream lexicon, TaggerOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(weights);
        ArgumentNullException.ThrowIfNull(lexicon);
        var maxAlternatives = ValidateOptions(options);
        return new Tagger(PerceptronModel.Load(weights), Lexicon.Load(lexicon), maxAlternatives);
    }

    /// <summary>
    ///     Splits a text into sentences and tokens and tags every token.
    /// </summary>
    /// <remarks>
    ///     The text is split by <see cref="Tokenizer.Tokenize(string)"/>, whose conventions are summarized in
    ///     [Tokenization conventions](../../DemaConsulting.PartOfSpeech.md#tokenization-conventions); the offsets of the
    ///     tokens index <paramref name="text"/>. To read the confidence and the alternatives of a token, see
    ///     [Confidence and alternatives](../../DemaConsulting.PartOfSpeech.md#confidence-and-alternatives).
    /// </remarks>
    /// <param name="text">The text.</param>
    /// <returns>The tagged sentences in text order; empty for a text without tokens.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="text"/> is <c>null</c>.</exception>
    /// <example>
    ///     Tag a text with two sentences and read each token, its confidence and its alternatives:
    ///     <code>
    ///     foreach (TaggedSentence sentence in tagger.Tag("Time flies. Fruit flies like a banana."))
    ///     {
    ///         foreach (TaggedToken token in sentence.Tokens)
    ///         {
    ///             // token.Start indexes the string that was tagged
    ///             Console.WriteLine($"{token.Start,3} {token.Text} {token.Tag} {token.Confidence:0.00}");
    ///
    ///             foreach (TagScore alternative in token.Alternatives)
    ///             {
    ///                 Console.WriteLine($"      or {alternative.Tag} {alternative.Probability:0.00}");
    ///             }
    ///         }
    ///     }
    ///     </code>
    /// </example>
    public IReadOnlyList<TaggedSentence> Tag(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        var sentences = Tokenizer.Tokenize(text);
        var result = new List<TaggedSentence>(sentences.Count);
        foreach (var sentence in sentences)
        {
            result.Add(Tag(sentence));
        }

        return result;
    }

    /// <summary>
    ///     Tags the tokens of a sentence.
    /// </summary>
    /// <remarks>
    ///     Use the constraints to inject knowledge that the model does not have, for example a glossary of technical
    ///     names. The tag that is chosen for a constrained token is final and, like every chosen tag, becomes part of
    ///     the context of the tokens that follow it, so a constraint can also change the tags of its neighbors. A
    ///     constraint that leaves a single tag gives the token the confidence 1.0.
    /// </remarks>
    /// <param name="sentence">The sentence, from <see cref="Tokenizer"/>.</param>
    /// <param name="constraints">One constraint per token, in token order, or <c>null</c> for none; use <see cref="TokenConstraint.None"/> for a token that the model decides alone.</param>
    /// <returns>The tagged sentence.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="sentence"/> is <c>null</c>.</exception>
    /// <exception cref="ArgumentException"><paramref name="constraints"/> does not have one element per token.</exception>
    /// <example>
    ///     Force a technical name from a glossary to be a noun wherever it occurs in a sentence (a hard constraint), and
    ///     leave the other tokens to the model:
    ///     <code>
    ///     var glossary = new HashSet&lt;string&gt;(StringComparer.OrdinalIgnoreCase) { "valve" };
    ///     Sentence sentence = Tokenizer.Tokenize("Close the valve before you open the next valve.")[0];
    ///
    ///     // One constraint per token, in token order
    ///     var constraints = sentence.Tokens
    ///         .Select(token => glossary.Contains(token.Text) ? TokenConstraint.Only(Upos.Noun) : TokenConstraint.None)
    ///         .ToArray();
    ///
    ///     TaggedSentence tagged = tagger.Tag(sentence, constraints);
    ///     foreach (TaggedToken token in tagged.Tokens)
    ///     {
    ///         // Both "valve" tokens are NOUN with confidence 1.0 and IsConstrained is true; the tags that were chosen
    ///         // for the tokens before a word are part of its context, so "valve" also shapes how its neighbors are read
    ///         Console.WriteLine($"{token.Text} {token.Tag} {token.Confidence:0.00} {token.IsConstrained}");
    ///     }
    ///     </code>
    /// </example>
    public TaggedSentence Tag(Sentence sentence, IReadOnlyList<TokenConstraint>? constraints = null)
    {
        ArgumentNullException.ThrowIfNull(sentence);
        return new TaggedSentence(sentence, _decoder.Decode(sentence.TokenArray, constraints, _maxAlternatives));
    }

    /// <summary>
    ///     Tags a sentence that is already split into tokens.
    /// </summary>
    /// <remarks>
    ///     The tokens are not split again, so they must already follow the convention of <see cref="Tokenizer"/>:
    ///     punctuation marks and clitics such as <c>n't</c> are tokens of their own, and a token such as <c>Hello,</c>
    ///     stays one token (a spelling that the model has not seen). Capitalization and spelling are kept exactly as
    ///     given. The tokens are joined with single spaces into the text of the sentence (<see cref="TaggedSentence.Text"/>),
    ///     and the offsets of the tagged tokens index that joined text, not any text that the tokens came from. See
    ///     [Offsets and pre-tokenized input](../../DemaConsulting.PartOfSpeech.md#offsets-and-pre-tokenized-input).
    /// </remarks>
    /// <param name="tokens">The tokens; none may be <c>null</c>, empty or contain white space. They should follow the convention of <see cref="Tokenizer"/>.</param>
    /// <param name="constraints">One constraint per token, in token order, or <c>null</c> for none.</param>
    /// <returns>The tagged sentence; its text is the tokens joined by single spaces.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="tokens"/> is <c>null</c>.</exception>
    /// <exception cref="ArgumentException">A token is <c>null</c>, empty or contains white space, or <paramref name="constraints"/> does not have one element per token.</exception>
    /// <example>
    ///     Tag tokens that another component already produced. Punctuation and the contraction are separate tokens, and
    ///     nothing is lower-cased or otherwise changed:
    ///     <code>
    ///     var tokens = new[] { "She", "does", "n't", "know", "Paris", "." };
    ///     TaggedSentence sentence = tagger.Tag(tokens);
    ///
    ///     Console.WriteLine(sentence.Text);   // She does n't know Paris .
    ///     foreach (TaggedToken token in sentence.Tokens)
    ///     {
    ///         // Start is the offset in the joined text: She 0, does 4, n't 9, know 13, Paris 18, . 24
    ///         Console.WriteLine($"{token.Start,2} {token.Text} {token.Tag.ToTagString()}");
    ///     }
    ///     </code>
    /// </example>
    public TaggedSentence Tag(IReadOnlyList<string> tokens, IReadOnlyList<TokenConstraint>? constraints = null) =>
        Tag(Tokenizer.CreateSentence(tokens), constraints);

    /// <summary>
    ///     Splits a text into sentences and tokens, finds the terms of a glossary in them, and tags every token.
    /// </summary>
    /// <remarks>
    ///     This is <see cref="Tag(string)"/> with the terms of <paramref name="glossary"/> applied: each sentence is
    ///     searched for the terms (leftmost, longest, no overlaps) and the tokens of a term are constrained to the tags
    ///     of its entry. How terms are matched, how hard and soft entries work and what happens to the confidence is
    ///     described on <see cref="Glossary"/>; the matches are reported on <see cref="TaggedToken.GlossaryMatch"/> and
    ///     <see cref="TaggedSentence.GlossaryMatches"/>. The glossary is not stored: one tagger can be used with any
    ///     number of glossaries. The first sentence with a match of a glossary compiles its constraint tables for this
    ///     tagger once; the tagger keeps them for as long as the glossary object is alive and reuses them in later calls,
    ///     so build a glossary once and keep it. The confidence of a glossary head word is not evidence that the entry is
    ///     right: a hard entry that leaves one tag gives 1.0 whatever the model thinks. In
    ///     <see cref="Tag(Sentence, Glossary, IReadOnlyList{TokenConstraint}?)"/> and the other overloads, your own
    ///     constraints are combined with those of the glossary as described there.
    /// </remarks>
    /// <param name="text">The text.</param>
    /// <param name="glossary">The glossary; <see cref="Glossary.Empty"/> for none.</param>
    /// <returns>The tagged sentences in text order; empty for a text without tokens.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="text"/> or <paramref name="glossary"/> is <c>null</c>.</exception>
    /// <example>
    ///     Tag a maintenance instruction with a glossary of technical names, so that the first word of each compound is
    ///     not mistaken for a verb:
    ///     <code>
    ///     Glossary glossary = new GlossaryBuilder
    ///     {
    ///         { "bleed valve", Upos.Noun },
    ///         { "torque wrench", Upos.Noun },
    ///     }.Build();
    ///
    ///     foreach (TaggedSentence sentence in tagger.Tag("Open the bleed valve. Use a torque wrench.", glossary))
    ///     {
    ///         foreach (TaggedToken token in sentence.Tokens)
    ///         {
    ///             Console.WriteLine($"{token.Text} {token.Tag} {token.Confidence:0.00} head={token.IsGlossaryHead}");
    ///         }
    ///     }
    ///     </code>
    /// </example>
    public IReadOnlyList<TaggedSentence> Tag(string text, Glossary glossary)
    {
        ArgumentNullException.ThrowIfNull(text);
        ArgumentNullException.ThrowIfNull(glossary);
        var sentences = Tokenizer.Tokenize(text);
        var result = new List<TaggedSentence>(sentences.Count);
        foreach (var sentence in sentences)
        {
            result.Add(Tag(sentence, glossary));
        }

        return result;
    }

    /// <summary>
    ///     Finds the terms of a glossary in a sentence and tags its tokens.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         The terms are found as described on <see cref="Glossary"/>. The tokens of a term get the constraints of its
    ///         entry: in hard mode the head word is limited to the tags of the entry, in soft mode they are boosted by the
    ///         strength of the glossary times the temperature of this tagger's model. A hard constraint with one allowed
    ///         tag gives the token the confidence exactly 1.0 and no alternatives; otherwise the confidence is the softmax
    ///         over the tags that remain. This confidence measures the choice among the tags that the constraints still
    ///         allow; it is not evidence that the entry is correct. As for every constraint, the tag that is chosen becomes history for the tokens
    ///         after it, so the neighbors after a match can change; the tokens before the first match never do.
    ///     </para>
    ///     <para>
    ///         <strong>Precedence of your constraints:</strong> your own <paramref name="constraints"/> (one per token) and
    ///         the constraints of the glossary are combined per token. If you give no constraint for a token
    ///         (<see cref="TokenConstraint.None"/>), the glossary's constraint applies; if the glossary has none for the
    ///         token, yours applies; if both exist, they are combined with
    ///         <see cref="TokenConstraint.And(TokenConstraint)"/>. When the allowed tags of the two are disjoint (your
    ///         constraint contradicts a hard entry, for example <c>Only(Upos.Verb)</c> on a head word that the entry limits
    ///         to <c>NOUN</c>), <strong>your constraint wins</strong> for that token: it is explicit and per call, while the
    ///         glossary is the default for all calls. A sentence without a match is tagged with your constraints alone.
    ///         The parameter is optional and comes last so that an existing call such as <c>Tag(sentence, null)</c> keeps
    ///         choosing <see cref="Tag(Sentence, IReadOnlyList{TokenConstraint}?)"/>.
    ///     </para>
    /// </remarks>
    /// <param name="sentence">The sentence, from <see cref="Tokenizer"/>.</param>
    /// <param name="glossary">The glossary; <see cref="Glossary.Empty"/> for none.</param>
    /// <param name="constraints">One constraint per token, in token order, or <c>null</c> for none; they are combined with the glossary's constraints.</param>
    /// <returns>The tagged sentence; its <see cref="TaggedSentence.GlossaryMatches"/> lists the terms that were found.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="sentence"/> or <paramref name="glossary"/> is <c>null</c>.</exception>
    /// <exception cref="ArgumentException"><paramref name="constraints"/> does not have one element per token.</exception>
    /// <example>
    ///     Tag a sentence with a glossary and your own constraint for one more word, then read the match of each term:
    ///     <code>
    ///     Glossary glossary = Glossary.Create(new[] { new GlossaryEntry("bleed valve", Upos.Noun) });
    ///     Sentence sentence = Tokenizer.Tokenize("Please book the bleed valve.")[0];
    ///
    ///     // One constraint per token: "book" is a verb here, the glossary decides the rest
    ///     var constraints = sentence.Tokens
    ///         .Select(token => token.Text == "book" ? TokenConstraint.Only(Upos.Verb) : TokenConstraint.None)
    ///         .ToArray();
    ///
    ///     TaggedSentence tagged = tagger.Tag(sentence, glossary, constraints);
    ///     foreach (GlossaryMatch match in tagged.GlossaryMatches)
    ///     {
    ///         Console.WriteLine($"{match.Text}: head {tagged.Tokens[match.HeadToken].Tag}");
    ///     }
    ///     </code>
    ///     When your constraint contradicts a hard entry, yours wins for that token (here the entry says <c>NOUN</c>, the
    ///     constraint allows only <c>VERB</c>):
    ///     <code>
    ///     Glossary glossary = Glossary.Create(new[] { new GlossaryEntry("bleed valve", Upos.Noun) });
    ///     Sentence sentence = Tokenizer.Tokenize("Close the bleed valve.")[0];
    ///     var constraints = sentence.Tokens
    ///         .Select(token => token.Text == "valve" ? TokenConstraint.Only(Upos.Verb) : TokenConstraint.None)
    ///         .ToArray();
    ///
    ///     TaggedSentence tagged = tagger.Tag(sentence, glossary, constraints);
    ///     Console.WriteLine(tagged.Tokens[3].Tag);   // Verb
    ///     </code>
    /// </example>
    public TaggedSentence Tag(Sentence sentence, Glossary glossary, IReadOnlyList<TokenConstraint>? constraints = null)
    {
        ArgumentNullException.ThrowIfNull(sentence);
        ArgumentNullException.ThrowIfNull(glossary);

        // Find the terms first; without a match the glossary changes nothing, so the plain path (and its validation) applies
        var matches = glossary.Match(sentence);
        if (matches.Count == 0)
        {
            return new TaggedSentence(sentence, _decoder.Decode(sentence.TokenArray, constraints, _maxAlternatives));
        }

        // Turn the matches into one constraint per token (merged with the caller's), tag, and attach the match to its tokens
        var compiled = _compiledGlossaries.GetValue(glossary, _compileGlossary);
        var merged = compiled.Build(matches, sentence.TokenArray.Length, constraints);
        var tagged = _decoder.Decode(sentence.TokenArray, merged, _maxAlternatives);
        foreach (var match in matches)
        {
            for (var k = 0; k < match.TokenCount; k++)
            {
                tagged[match.FirstToken + k] = tagged[match.FirstToken + k].WithGlossaryMatch(match);
            }
        }

        return new TaggedSentence(sentence, tagged, matches);
    }

    /// <summary>
    ///     Finds the terms of a glossary in a sentence that is already split into tokens, and tags it.
    /// </summary>
    /// <remarks>
    ///     The tokens must follow the convention of <see cref="Tokenizer"/> (see
    ///     <see cref="Tag(IReadOnlyList{string}, IReadOnlyList{TokenConstraint}?)"/>): a term is matched token by token, so a
    ///     token with punctuation stuck to it (<c>valve.</c>) does not match <c>valve</c>. Everything else, including the
    ///     precedence of <paramref name="constraints"/> over the glossary when the two contradict each other, is described on
    ///     <see cref="Tag(Sentence, Glossary, IReadOnlyList{TokenConstraint}?)"/> and <see cref="Glossary"/>.
    /// </remarks>
    /// <param name="tokens">The tokens; none may be <c>null</c>, empty or contain white space.</param>
    /// <param name="glossary">The glossary; <see cref="Glossary.Empty"/> for none.</param>
    /// <param name="constraints">One constraint per token, in token order, or <c>null</c> for none.</param>
    /// <returns>The tagged sentence; its text is the tokens joined by single spaces.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="tokens"/> or <paramref name="glossary"/> is <c>null</c>.</exception>
    /// <exception cref="ArgumentException">A token is <c>null</c>, empty or contains white space, or <paramref name="constraints"/> does not have one element per token.</exception>
    /// <example>
    ///     Tag pre-split tokens with a glossary, and read the head word of the term:
    ///     <code>
    ///     Glossary glossary = new GlossaryBuilder { { "torque wrench", Upos.Noun } }.Build();
    ///     TaggedSentence sentence = tagger.Tag(new[] { "Use", "a", "torque", "wrench", "." }, glossary);
    ///
    ///     TaggedToken head = sentence.Tokens.Single(token => token.IsGlossaryHead);
    ///     Console.WriteLine($"{head.Text} {head.Tag}");   // wrench NOUN
    ///     </code>
    /// </example>
    public TaggedSentence Tag(IReadOnlyList<string> tokens, Glossary glossary, IReadOnlyList<TokenConstraint>? constraints = null) =>
        Tag(Tokenizer.CreateSentence(tokens), glossary, constraints);

    /// <summary>
    ///     Looks up one word on its own: the tags it can take, whether the lexicon recognizes it, and its analyses.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         This is a context-free lookup in the lexicon of this tagger, for callers that need the dictionary roles of a
    ///         single word (for example a rule checker that asks "may this word be a verb?") and not the tag in a sentence.
    ///         It never uses a sentence, the model or a glossary. For the tag of a word in context, tag the sentence with
    ///         <see cref="Tag(string)"/> and read <see cref="TaggedToken.Tag"/> and <see cref="TaggedToken.Confidence"/>; for domain
    ///         terms and phrases use a <see cref="Glossary"/>.
    ///     </para>
    ///     <para>
    ///         <strong>Normalization:</strong> the word is folded exactly like a token of the tagger: Unicode normalization form C,
    ///         typographic quotes, dashes and the ellipsis mapped to their ASCII forms, then lower case. Capitalization
    ///         never changes the lexicon result (<c>Valve</c> and <c>valve</c> are the same spelling); in
    ///         <see cref="PossibleTagsMode.WithShapeRules"/> mode a leading capital letter also allows PROPN and X, as it does
    ///         for the tagger. An unpaired surrogate counts as U+FFFD and never throws.
    ///     </para>
    ///     <para>
    ///         <strong>Modes:</strong> see <see cref="PossibleTagsMode"/>. The result tells the match (<see cref="WordMatch"/>):
    ///         test <see cref="WordTags.Match"/> or <see cref="WordTags.IsKnownWord"/> to find out whether the lexicon knows the word,
    ///         never whether <see cref="WordTags.Tags"/> is empty. <see cref="WordTags.IsKnownWord"/> equals
    ///         <see cref="TaggedToken.IsKnownWord"/> of the same word as a token.
    ///     </para>
    ///     <para>
    ///         <strong>Hyphenated words:</strong> a word is looked up as a whole first. If the folded, lower-cased word is a
    ///         lexicon spelling, the result is <see cref="WordMatch.Spelling"/> and the analyses are those of that spelling
    ///         (<c>built-in</c> has its own analyses, not those of <c>in</c>). If it is not, and it is a hyphenated word, the last
    ///         part is tried: a word is a hyphenated compound when it contains the hyphen-minus and every part between hyphens is
    ///         non-empty (<c>well-tested</c>; not <c>-ed</c>, <c>well-</c> or <c>a--b</c>). The last part is looked up as a whole and
    ///         nothing is split, stripped or looked up recursively. If it is a spelling, the result is
    ///         <see cref="WordMatch.CompoundHead"/> and the analyses are those of the last part; otherwise the result is
    ///         <see cref="WordMatch.NotRecognized"/>. Only the hyphen-minus counts: a dash character is folded to <c>--</c>, which is
    ///         not a word hyphen. The tagger looks only at the whole token, so for a compound head <see cref="WordTags.Tags"/>
    ///         is what the tagger allows for the whole token (empty in <see cref="PossibleTagsMode.LexiconOnly"/> mode, all 17 tags
    ///         in <see cref="PossibleTagsMode.WithShapeRules"/> mode), <see cref="WordTags.IsKnownWord"/> is <c>false</c>, and the analyses of
    ///         the head are extra evidence that never narrows the tags and never changes the tagging.
    ///     </para>
    ///     <para>
    ///         <strong>Edge cases:</strong> a word that is longer than the longest spelling of the lexicon (122 UTF-8 bytes in the
    ///         embedded lexicon) cannot be a spelling as a whole, so only the compound rule can recognize it: it is
    ///         <see cref="WordMatch.CompoundHead"/> when it is a hyphenated compound whose last part is a spelling, and
    ///         <see cref="WordMatch.NotRecognized"/> otherwise (no hyphen, an empty part, or a last part that is not a spelling or is itself
    ///         longer than the longest spelling). Such a word costs one pass over the text, folds at most its last part, and never
    ///         grows a buffer beyond what a word that fits needs. A lexicon without an analyses table (a custom lexicon file) gives no analyses and
    ///         <see cref="WordTags.TagsWithoutAnalysis"/> equals <see cref="WordTags.Tags"/>. The lexicon has no phrases, so text with white
    ///         space is refused rather than answered "unknown".
    ///     </para>
    ///     <para>
    ///         <strong>Thread safety and cost:</strong> the call is safe from any number of threads, also while other threads
    ///         tag text, and uses the same per-thread scratch memory as tagging. After the first call on a thread, a word of
    ///         ASCII characters allocates nothing; a word with other characters allocates only what the normalization needs
    ///         (nothing when it is already normalized on .NET 8 and later). <see cref="WordTags.GetAnalyses"/> allocates its list.
    ///     </para>
    ///     <para>
    ///         On .NET 8 and later an overload takes a <c>ReadOnlySpan&lt;char&gt;</c> for callers that hold the word
    ///         inside a larger text; it has the same rules and results. <c>netstandard2.0</c> has no such overload.
    ///     </para>
    /// </remarks>
    /// <param name="word">The word as written; one token, not empty and without white space.</param>
    /// <param name="mode">Which tags to report; <see cref="PossibleTagsMode.LexiconOnly"/> by default.</param>
    /// <returns>The tags, the match and the analyses of the word.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="word"/> is <c>null</c>.</exception>
    /// <exception cref="ArgumentException"><paramref name="word"/> is empty or contains a white-space character (the same rule as for a token of <see cref="Tag(IReadOnlyList{string}, IReadOnlyList{TokenConstraint}?)"/>); split a text into tokens with <see cref="Tokenizer"/> first, or use a <see cref="Glossary"/> for a multiword term.</exception>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="mode"/> is not a defined value.</exception>
    /// <example>
    ///     Find out whether the lexicon knows a word and which tags it can take, in both modes:
    ///     <code>
    ///     WordTags valve = tagger.GetPossibleTags("Valve");
    ///     Console.WriteLine($"{valve.Match} {valve.Tags}");     // Spelling NOUN
    ///
    ///     WordTags unknown = tagger.GetPossibleTags("zzqxv");
    ///     Console.WriteLine(unknown.Match);                      // NotRecognized (the tag set is empty)
    ///
    ///     WordTags forTagger = tagger.GetPossibleTags("zzqxv", PossibleTagsMode.WithShapeRules);
    ///     Console.WriteLine(forTagger.Tags.Count);               // 17: the tagger may choose any tag
    ///     </code>
    ///     Read the base word, tag and features of each form of a word:
    ///     <code>
    ///     foreach (WordAnalysis analysis in tagger.GetPossibleTags("tests").GetAnalyses())
    ///     {
    ///         // test NOUN Number=Plur, then test VERB Person=3|Tense=Pres
    ///         Console.WriteLine($"{analysis.Lemma} {analysis.Tag} {analysis.Features}");
    ///     }
    ///     </code>
    /// </example>
    public WordTags GetPossibleTags(string word, PossibleTagsMode mode = PossibleTagsMode.LexiconOnly)
    {
        ArgumentNullException.ThrowIfNull(word);
        ValidateMode(mode);
        ValidateWord(word, nameof(word));
        return _decoder.GetPossibleTags(word, mode);
    }

#if NET
    /// <summary>
    ///     Looks up one word given as a span of characters; this is <see cref="GetPossibleTags(string, PossibleTagsMode)"/> without the string.
    /// </summary>
    /// <remarks>
    ///     Available on .NET 8 and later only. The rules, the result and the thread safety are those of the string overload. A span
    ///     of ASCII characters is looked up without a copy and allocates nothing after the first call on the thread; a span that contains
    ///     other characters is copied to a string for the normalization, which allocates.
    /// </remarks>
    /// <param name="word">The word as written; one token, not empty and without white space.</param>
    /// <param name="mode">Which tags to report; <see cref="PossibleTagsMode.LexiconOnly"/> by default.</param>
    /// <returns>The tags, the match and the analyses of the word.</returns>
    /// <exception cref="ArgumentException"><paramref name="word"/> is empty or contains a white-space character.</exception>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="mode"/> is not a defined value.</exception>
    public WordTags GetPossibleTags(ReadOnlySpan<char> word, PossibleTagsMode mode = PossibleTagsMode.LexiconOnly)
    {
        ValidateMode(mode);
        if (word.IsEmpty)
        {
            throw new ArgumentException(WordRuleMessage, nameof(word));
        }

        for (var i = 0; i < word.Length; i++)
        {
            if (char.IsWhiteSpace(word[i]))
            {
                throw new ArgumentException(WordRuleMessage, nameof(word));
            }
        }

        return _decoder.GetPossibleTags(word, mode);
    }
#endif

    /// <summary>
    ///     Tells whether a word is a form of a base word with a tag, according to the analyses of the lexicon.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         This is the convenient answer to "is <paramref name="word"/> an inflected form of the verb (or noun, or adjective)
    ///         <paramref name="lemma"/>?": it is <c>true</c> when <see cref="WordTags.GetAnalyses"/> of the word contains an analysis
    ///         with this base word and this tag. It does not allocate for text that is pure ASCII, unlike reading the analyses.
    ///     </para>
    ///     <para>
    ///         Word and base word are folded like a token of the tagger, so case never matters:
    ///         <c>IsFormOf("TESTS", "Test", Upos.Noun)</c> is <c>true</c>. A word is found with the rule of
    ///         <see cref="GetPossibleTags(string, PossibleTagsMode)"/>, including its hyphenated compound rule, so
    ///         <c>IsFormOf("well-tested", "test", Upos.Verb)</c> is <c>true</c> because the analysis of the head <c>tested</c> has the
    ///         base word <c>test</c>; a caller who must exclude compounds checks <see cref="WordTags.Match"/> first. A form that
    ///         the lexicon records no analysis for (a tag that is attested without one, see <see cref="WordTags.TagsWithoutAnalysis"/>)
    ///         is not a form of anything and gives <c>false</c>, as does an unrecognized word and a lexicon without analyses. The
    ///         answer does not depend on the mode, the model or a glossary, and the call is thread-safe.
    ///     </para>
    /// </remarks>
    /// <param name="word">The word as written; one token, not empty and without white space.</param>
    /// <param name="lemma">The base word as written; one token, not empty and without white space (a phrase such as <c>give up</c> is refused).</param>
    /// <param name="tag">The tag of the analysis.</param>
    /// <returns><c>true</c> if the word has an analysis with this base word and tag.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="word"/> or <paramref name="lemma"/> is <c>null</c>.</exception>
    /// <exception cref="ArgumentException"><paramref name="word"/> or <paramref name="lemma"/> is empty or contains a white-space character.</exception>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="tag"/> is not one of the 17 defined values.</exception>
    /// <example>
    ///     Check the dictionary role of a word without reading all its analyses:
    ///     <code>
    ///     bool isVerbForm = tagger.IsFormOf("tests", "test", Upos.Verb);   // true
    ///     bool isAdjForm = tagger.IsFormOf("tests", "test", Upos.Adj);     // false
    ///     bool irregular = tagger.IsFormOf("went", "go", Upos.Verb);       // true
    ///     </code>
    /// </example>
    public bool IsFormOf(string word, string lemma, Upos tag)
    {
        ArgumentNullException.ThrowIfNull(word);
        ArgumentNullException.ThrowIfNull(lemma);
        if ((uint)tag >= UposExtensions.Count)
        {
            throw new ArgumentOutOfRangeException(nameof(tag), tag, "The value is not one of the 17 Universal POS tags.");
        }

        ValidateWord(word, nameof(word));
        ValidateWord(lemma, nameof(lemma));
        return _decoder.IsFormOf(word, lemma, tag);
    }

    /// <summary>
    ///     Creates the default tagger from the embedded model.
    /// </summary>
    /// <returns>The tagger.</returns>
    /// <exception cref="InvalidOperationException">An embedded resource is missing.</exception>
    /// <exception cref="InvalidDataException">An embedded resource is damaged.</exception>
    private static Tagger CreateDefault() =>
        new(EmbeddedModel.Weights, EmbeddedModel.Lexicon, new TaggerOptions().MaxAlternatives);

    /// <summary>
    ///     Validates the mode of a word lookup.
    /// </summary>
    /// <param name="mode">The mode.</param>
    /// <exception cref="ArgumentOutOfRangeException">The mode is not a defined value.</exception>
    private static void ValidateMode(PossibleTagsMode mode)
    {
        if (mode != PossibleTagsMode.LexiconOnly && mode != PossibleTagsMode.WithShapeRules)
        {
            throw new ArgumentOutOfRangeException(nameof(mode), mode, "The value is not a defined PossibleTagsMode.");
        }
    }

    /// <summary>
    ///     Validates a word or base word of a lookup: one non-empty token without white space.
    /// </summary>
    /// <param name="value">The text, already checked for <c>null</c>.</param>
    /// <param name="parameterName">The name of the parameter for the exception.</param>
    /// <exception cref="ArgumentException">The text is empty or contains a white-space character.</exception>
    private static void ValidateWord(string value, string parameterName)
    {
        if (value.Length == 0 || Tokenizer.ContainsWhiteSpace(value))
        {
            throw new ArgumentException(WordRuleMessage, parameterName);
        }
    }

    /// <summary>
    ///     Validates the options.
    /// </summary>
    /// <param name="options">The options or <c>null</c>.</param>
    /// <returns>The number of alternatives to report.</returns>
    /// <exception cref="ArgumentOutOfRangeException">The number of alternatives is outside 0 to 16.</exception>
    private static int ValidateOptions(TaggerOptions? options)
    {
        var value = (options ?? new TaggerOptions()).MaxAlternatives;
        if (value < 0 || value > TaggerOptions.MaxAlternativesLimit)
        {
            throw new ArgumentOutOfRangeException(nameof(options), value, "MaxAlternatives must be between 0 and 16.");
        }

        return value;
    }
}

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
///         inject knowledge per token with a <see cref="TokenConstraint"/>.
///     </para>
///     <para>
///         <strong>Thread safety:</strong> a <see cref="Tagger"/> is immutable after creation and all members may be
///         called concurrently from any number of threads; every call uses its own per-thread scratch memory. The
///         results are immutable too.
///     </para>
///     <para>
///         <strong>Default model:</strong> in this build <see cref="Default"/> throws
///         <see cref="InvalidOperationException"/>, because no trained weights are embedded yet (it returns the shared
///         tagger with the embedded model once they are); create the tagger with
///         <see cref="Load(Stream, TaggerOptions?)"/> instead. Loading cost and the lifetime of the stream are
///         described in [Default model and loading cost](../DemaConsulting.PartOfSpeech.md#default-model-and-loading-cost).
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
///     Create a tagger from a weights file once, share it, and tag a text:
///     <code>
///     using var weights = File.OpenRead("model.posw");
///     var tagger = Tagger.Load(weights);
///
///     TaggedSentence sentence = tagger.Tag("The cat sat on the mat.")[0];
///     foreach (TaggedToken token in sentence.Tokens)
///     {
///         Console.WriteLine($"{token.Text}\t{token.Tag.ToTagString()}\t{token.Confidence:0.00}");
///     }
///     </code>
/// </example>
public sealed class Tagger
{
    /// <summary>
    ///     The message of the exception that <see cref="Default"/> throws while no model is embedded.
    /// </summary>
    private const string NoModelMessage =
        "This build of DemaConsulting.PartOfSpeech contains no trained model yet. Load a weights file with Tagger.Load(Stream).";

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
    ///     Initializes a new instance of the <see cref="Tagger"/> class.
    /// </summary>
    /// <param name="model">The weights.</param>
    /// <param name="lexicon">The lexicon.</param>
    /// <param name="maxAlternatives">The maximum number of alternatives per token.</param>
    private Tagger(PerceptronModel model, Lexicon lexicon, int maxAlternatives)
    {
        _decoder = new TagDecoder(model, lexicon);
        _maxAlternatives = maxAlternatives;
    }

    /// <summary>
    ///     Gets the shared tagger that uses the model embedded in the package.
    /// </summary>
    /// <remarks>
    ///     In this build the package embeds the lexicon but no trained weights, so reading this property always throws
    ///     <see cref="InvalidOperationException"/>; create the tagger with <see cref="Load(Stream, TaggerOptions?)"/>
    ///     instead. When a trained model is embedded, the tagger is created on first use and every caller receives the
    ///     same thread-safe instance. See
    ///     [Default model and loading cost](../../DemaConsulting.PartOfSpeech.md#default-model-and-loading-cost).
    /// </remarks>
    /// <exception cref="InvalidOperationException">No trained weights are embedded in this build.</exception>
    /// <exception cref="InvalidDataException">The embedded model is damaged or built for another feature set (not expected in a released package).</exception>
    /// <example>
    ///     Use the embedded model when the build has one, otherwise load a weights file:
    ///     <code>
    ///     Tagger tagger;
    ///     try
    ///     {
    ///         tagger = Tagger.Default;
    ///     }
    ///     catch (InvalidOperationException)
    ///     {
    ///         using var weights = File.OpenRead("model.posw");
    ///         tagger = Tagger.Load(weights);
    ///     }
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
    ///     returned. The first tagger of the process also loads the embedded lexicon (641,418 bytes compressed, 262,667
    ///     spellings), which later taggers reuse. See
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
        return new Tagger(PerceptronModel.Load(weights, FeatureExtractor.FeatureSetVersion), EmbeddedModel.Lexicon, maxAlternatives);
    }

    /// <summary>
    ///     Creates a tagger from a weights file and a lexicon file.
    /// </summary>
    /// <remarks>
    ///     Both files are read completely, decompressed and validated before this method returns, and both streams may
    ///     be disposed afterwards (see <see cref="Load(Stream, TaggerOptions?)"/>). The lexicon is loaded for this
    ///     tagger only; the embedded lexicon is not used.
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
        return new Tagger(PerceptronModel.Load(weights, FeatureExtractor.FeatureSetVersion), Lexicon.Load(lexicon), maxAlternatives);
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
    ///     Creates the default tagger or explains why that is not possible.
    /// </summary>
    /// <returns>The tagger.</returns>
    /// <exception cref="InvalidOperationException">No weights are embedded in this build.</exception>
    private static Tagger CreateDefault()
    {
        var weights = EmbeddedModel.Weights ?? throw new InvalidOperationException(NoModelMessage);
        return new Tagger(weights, EmbeddedModel.Lexicon, new TaggerOptions().MaxAlternatives);
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

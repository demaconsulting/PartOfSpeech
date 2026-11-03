namespace DemaConsulting.PartOfSpeech;

/// <summary>
///     Assigns a Universal POS tag to every token of a text, with a calibrated confidence and ranked alternatives.
/// </summary>
/// <remarks>
///     <para>
///         The tagger is a greedy averaged-perceptron model: weights come from a <c>POSW</c> file (loaded with
///         <see cref="Load(Stream, TaggerOptions?)"/>), and a compiled word lexicon restricts a known word to the tags
///         that the data supports (an unknown word may take any of the 17 tags). Callers can inject knowledge per token
///         with a <see cref="TokenConstraint"/>.
///     </para>
///     <para>
///         <strong>Thread safety:</strong> a <see cref="Tagger"/> is immutable after creation and all members may be
///         called concurrently from any number of threads; every call uses its own per-thread scratch memory. The
///         results are immutable too.
///     </para>
///     <para>
///         <strong>Default model:</strong> this build does not yet embed a trained model, so <see cref="Default"/>
///         throws an <see cref="InvalidOperationException"/>; load weights explicitly instead.
///     </para>
///     <para>
///         <strong>Exceptions:</strong> creating a tagger throws <see cref="ArgumentNullException"/> for a <c>null</c>
///         stream, <see cref="ArgumentOutOfRangeException"/> for options out of range and
///         <see cref="InvalidDataException"/> for a damaged file or one of an unsupported version or feature set. The
///         tagging methods throw <see cref="ArgumentNullException"/> for a <c>null</c> text, sentence or token list and
///         <see cref="ArgumentException"/> for an invalid token or a constraint list of the wrong length; no text makes
///         them fail.
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
    ///     Gets the tagger with the model that is embedded in the package.
    /// </summary>
    /// <remarks>
    ///     No trained model is embedded yet, so reading this property currently always throws. A later release embeds a
    ///     model and this property then returns a shared, thread-safe tagger.
    /// </remarks>
    /// <exception cref="InvalidOperationException">This build contains no trained model.</exception>
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
    /// <param name="weights">A stream that holds a <c>POSW</c> weights file, read to its end; it is not closed.</param>
    /// <param name="options">The settings, or <c>null</c> for the defaults.</param>
    /// <returns>The tagger.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="weights"/> is <c>null</c>.</exception>
    /// <exception cref="ArgumentOutOfRangeException">The options are out of range.</exception>
    /// <exception cref="InvalidDataException">The file is not a valid weights file of a supported version and feature set.</exception>
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
    /// <param name="weights">A stream that holds a <c>POSW</c> weights file, read to its end; it is not closed.</param>
    /// <param name="lexicon">A stream that holds a <c>POSL</c> lexicon file, read to its end; it is not closed.</param>
    /// <param name="options">The settings, or <c>null</c> for the defaults.</param>
    /// <returns>The tagger.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="weights"/> or <paramref name="lexicon"/> is <c>null</c>.</exception>
    /// <exception cref="ArgumentOutOfRangeException">The options are out of range.</exception>
    /// <exception cref="InvalidDataException">A file is invalid, of an unsupported version or of another feature set.</exception>
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
    /// <param name="sentence">The sentence, from <see cref="Tokenizer"/>.</param>
    /// <param name="constraints">One constraint per token, or <c>null</c> for none.</param>
    /// <returns>The tagged sentence.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="sentence"/> is <c>null</c>.</exception>
    /// <exception cref="ArgumentException"><paramref name="constraints"/> does not have one element per token.</exception>
    /// <example>
    ///     Tokenize a sentence yourself and force the word "flies" to be a verb:
    ///     <code>
    ///     Sentence sentence = Tokenizer.Tokenize("Fruit flies like a banana.")[0];
    ///
    ///     var constraints = new TokenConstraint[sentence.Tokens.Count];
    ///     for (var i = 0; i &lt; constraints.Length; i++)
    ///     {
    ///         constraints[i] = sentence.Tokens[i].Text == "flies"
    ///             ? TokenConstraint.Only(Upos.Verb)
    ///             : TokenConstraint.None;
    ///     }
    ///
    ///     TaggedSentence tagged = tagger.Tag(sentence, constraints);
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
    /// <param name="tokens">The tokens; none may be <c>null</c>, empty or contain white space. They should follow the convention of <see cref="Tokenizer"/>.</param>
    /// <param name="constraints">One constraint per token, or <c>null</c> for none.</param>
    /// <returns>The tagged sentence; its text is the tokens joined by single spaces.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="tokens"/> is <c>null</c>.</exception>
    /// <exception cref="ArgumentException">A token is invalid, or <paramref name="constraints"/> does not have one element per token.</exception>
    /// <example>
    ///     Tag tokens that another component already produced:
    ///     <code>
    ///     TaggedSentence sentence = tagger.Tag(new[] { "She", "does", "n't", "know", "." });
    ///     Console.WriteLine(string.Join(" ", sentence.Tokens));   // each token as text/TAG, for example cat/NOUN
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

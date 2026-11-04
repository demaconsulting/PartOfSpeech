namespace DemaConsulting.PartOfSpeech.Trainer;

/// <summary>
///     The record of one development file in the selection artifact.
/// </summary>
internal sealed class DevFileRecord
{
    /// <summary>
    ///     Gets or sets the path of the file below the data folder, with forward slashes.
    /// </summary>
    public string Path { get; set; } = string.Empty;

    /// <summary>
    ///     Gets or sets the SHA-256 of the file as lower-case hexadecimal text.
    /// </summary>
    public string Sha256 { get; set; } = string.Empty;
}

/// <summary>
///     The record of one operating point in the selection artifact.
/// </summary>
internal sealed class PointRecord
{
    /// <summary>
    ///     Gets or sets the name of the point (<c>strict</c> or <c>balanced</c>).
    /// </summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>
    ///     Gets or sets the target for the harmful errors per 1,000 answered decisions.
    /// </summary>
    public double MaxHarmfulPer1000Answered { get; set; }

    /// <summary>
    ///     Gets or sets the coverage floor in percent.
    /// </summary>
    public double MinCoveragePercent { get; set; }

    /// <summary>
    ///     Gets or sets the selected threshold, or <c>null</c> when no grid threshold qualified.
    /// </summary>
    public double? Threshold { get; set; }

    /// <summary>
    ///     Gets or sets the pooled development decisions answered at the threshold.
    /// </summary>
    public long Answered { get; set; }

    /// <summary>
    ///     Gets or sets the pooled development harmful errors at the threshold.
    /// </summary>
    public long Harmful { get; set; }

    /// <summary>
    ///     Gets or sets the pooled development coverage at the threshold in percent.
    /// </summary>
    public double CoveragePercent { get; set; }

    /// <summary>
    ///     Gets or sets the pooled development harmful errors per 1,000 answered decisions at the threshold.
    /// </summary>
    public double HarmfulPer1000Answered { get; set; }
}

/// <summary>
///     The content of the selection artifact (<c>operating-points.json</c>): the frozen model, the development files, the
///     rule and the selected operating points.
/// </summary>
/// <remarks>
///     A plain data class; the JSON reading and writing is in the trainer program project (it needs <c>System.Text.Json</c>).
///     The artifact is only ever used through <see cref="SelectedOperatingPoints"/>, which validates it.
/// </remarks>
internal sealed class OperatingPointsData
{
    /// <summary>
    ///     The schema version that this build reads and writes.
    /// </summary>
    public const int CurrentSchemaVersion = 1;

    /// <summary>
    ///     Gets or sets the schema version.
    /// </summary>
    public int SchemaVersion { get; set; } = CurrentSchemaVersion;

    /// <summary>
    ///     Gets or sets the identifier of the selection rule.
    /// </summary>
    public string RuleId { get; set; } = string.Empty;

    /// <summary>
    ///     Gets or sets the SHA-256 of the uncompressed image of the model that the selection was made for.
    /// </summary>
    public string ModelImageSha256 { get; set; } = string.Empty;

    /// <summary>
    ///     Gets or sets the softmax temperature of that model.
    /// </summary>
    public double Temperature { get; set; }

    /// <summary>
    ///     Gets or sets the accuracy of the model on the open development split in percent (a reference of the held-out run).
    /// </summary>
    public double DevOpenAccuracyPercent { get; set; }

    /// <summary>
    ///     Gets or sets the number of noun-verb decisions of the open development split.
    /// </summary>
    public long OpenDevDecisions { get; set; }

    /// <summary>
    ///     Gets or sets the number of noun-verb decisions of the gold v2 development split (without <c>to</c> and <c>not</c>).
    /// </summary>
    public long GoldDevDecisions { get; set; }

    /// <summary>
    ///     Gets or sets the development files that the selection read.
    /// </summary>
    public List<DevFileRecord> DevFiles { get; set; } = new();

    /// <summary>
    ///     Gets or sets the threshold grid.
    /// </summary>
    public List<double> Grid { get; set; } = new();

    /// <summary>
    ///     Gets or sets the selected points.
    /// </summary>
    public List<PointRecord> Points { get; set; } = new();

    /// <summary>
    ///     Builds the artifact from a development selection.
    /// </summary>
    /// <param name="modelImageSha256">The SHA-256 of the uncompressed image of the model.</param>
    /// <param name="temperature">The temperature of the model.</param>
    /// <param name="devOpenAccuracyPercent">The accuracy on the open development split.</param>
    /// <param name="openDecisions">The decisions of the open development split.</param>
    /// <param name="goldDecisions">The decisions of the gold v2 development split.</param>
    /// <param name="devFiles">The development files with their hashes.</param>
    /// <param name="points">The selected points.</param>
    /// <returns>The artifact.</returns>
    public static OperatingPointsData Build(
        string modelImageSha256,
        double temperature,
        double devOpenAccuracyPercent,
        long openDecisions,
        long goldDecisions,
        IEnumerable<DevFileRecord> devFiles,
        IEnumerable<SelectedPoint> points)
    {
        ArgumentNullException.ThrowIfNull(devFiles);
        ArgumentNullException.ThrowIfNull(points);
        var data = new OperatingPointsData
        {
            RuleId = OperatingPointRule.Id,
            ModelImageSha256 = modelImageSha256,
            Temperature = temperature,
            DevOpenAccuracyPercent = devOpenAccuracyPercent,
            OpenDevDecisions = openDecisions,
            GoldDevDecisions = goldDecisions,
        };
        data.DevFiles.AddRange(devFiles);
        data.Grid.AddRange(ConfidenceCurve.Grid);
        foreach (var point in points)
        {
            data.Points.Add(new PointRecord
            {
                Name = point.Name,
                MaxHarmfulPer1000Answered = point.MaxHarmfulPer1000Answered,
                MinCoveragePercent = point.MinCoveragePercent,
                Threshold = point.Threshold,
                Answered = point.Row?.Answered ?? 0,
                Harmful = point.Row?.Harmful ?? 0,
                CoveragePercent = point.Row?.CoveragePercent ?? 0,
                HarmfulPer1000Answered = point.Row?.HarmfulPer1000Answered ?? 0,
            });
        }

        return data;
    }
}

/// <summary>
///     What a held-out run requires of the selection artifact: the model it is about to score and the development files that
///     the repository records.
/// </summary>
internal sealed class OperatingPointsExpectation
{
    /// <summary>
    ///     Gets or sets the SHA-256 of the uncompressed image of the embedded model.
    /// </summary>
    public string ModelImageSha256 { get; set; } = string.Empty;

    /// <summary>
    ///     Gets or sets the image hash recorded in the training configuration.
    /// </summary>
    public string ConfiguredImageSha256 { get; set; } = string.Empty;

    /// <summary>
    ///     Gets the SHA-256 of the development files recorded in the data manifest, by path below the data folder.
    /// </summary>
    public Dictionary<string, string> DevFileHashes { get; } = new(StringComparer.Ordinal);

    /// <summary>
    ///     Gets or sets the derivation of the pooled development curve of the frozen model, or <c>null</c> when the
    ///     development data is not available. When set, the points of the artifact must equal what the registered rule
    ///     selects from the derived curve.
    /// </summary>
    public Func<IReadOnlyList<CurveRow>>? DerivePooledCurve { get; set; }
}

/// <summary>
///     The validated operating points of a development selection: the proof that the selection was made, for this model and
///     for these development files, before a held-out file is opened.
/// </summary>
/// <remarks>
///     <para>
///         The held-out readers of <see cref="DataGuard"/> take an instance of this class as a required parameter, and the
///         only way to get one is <see cref="Validate"/>, which checks the artifact against the frozen model and the data
///         manifest. A held-out run therefore cannot start without a committed, consistent selection. The class is immutable.
///     </para>
/// </remarks>
internal sealed class SelectedOperatingPoints
{
    /// <summary>
    ///     Initializes a new instance of the <see cref="SelectedOperatingPoints"/> class.
    /// </summary>
    /// <param name="data">The validated artifact.</param>
    /// <param name="selectionSha256">The SHA-256 of the exact bytes of the artifact file.</param>
    private SelectedOperatingPoints(OperatingPointsData data, string selectionSha256)
    {
        Data = data;
        SelectionSha256 = selectionSha256;
    }

    /// <summary>
    ///     Gets the validated artifact.
    /// </summary>
    public OperatingPointsData Data { get; }

    /// <summary>
    ///     Gets the SHA-256 of the exact bytes of the artifact file.
    /// </summary>
    public string SelectionSha256 { get; }

    /// <summary>
    ///     Gets the points that have a threshold.
    /// </summary>
    public IEnumerable<PointRecord> SelectedPoints => Data.Points.Where(p => p.Threshold.HasValue);

    /// <summary>
    ///     Validates an artifact against the model and the data manifest.
    /// </summary>
    /// <param name="data">The artifact.</param>
    /// <param name="selectionSha256">The SHA-256 of the exact bytes of the artifact file.</param>
    /// <param name="expectation">What the run requires.</param>
    /// <returns>The validated points.</returns>
    /// <exception cref="InvalidDataException">The artifact is for another schema, rule, model or development file, or a point is off the grid.</exception>
    public static SelectedOperatingPoints Validate(OperatingPointsData data, string selectionSha256, OperatingPointsExpectation expectation)
    {
        ArgumentNullException.ThrowIfNull(data);
        ArgumentNullException.ThrowIfNull(selectionSha256);
        ArgumentNullException.ThrowIfNull(expectation);

        // The artifact must be of this schema and made with this rule
        if (data.SchemaVersion != OperatingPointsData.CurrentSchemaVersion)
        {
            throw new InvalidDataException($"The selection artifact has schema version {data.SchemaVersion}; this build reads {OperatingPointsData.CurrentSchemaVersion}.");
        }

        if (!string.Equals(data.RuleId, OperatingPointRule.Id, StringComparison.Ordinal))
        {
            throw new InvalidDataException($"The selection artifact was made with the rule '{data.RuleId}'; this build applies '{OperatingPointRule.Id}'.");
        }

        // The model must be the frozen one: the embedded image, the configuration and the artifact all name the same hash
        if (!string.Equals(data.ModelImageSha256, expectation.ModelImageSha256, StringComparison.OrdinalIgnoreCase)
            || !string.Equals(data.ModelImageSha256, expectation.ConfiguredImageSha256, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException("The selection artifact was made for another model than the embedded one; a held-out run needs a selection for the frozen model.");
        }

        ValidateDevFiles(data, expectation);
        ValidateGridAndPoints(data);

        // When the development data is available, the points must be what the registered rule selects from it
        if (expectation.DerivePooledCurve != null)
        {
            ValidateAgainstDerivation(data, expectation.DerivePooledCurve());
        }

        return new SelectedOperatingPoints(data, selectionSha256);
    }

    /// <summary>
    ///     Requires that the development files of the artifact are exactly the expected set, each once and with its manifest hash.
    /// </summary>
    /// <param name="data">The artifact.</param>
    /// <param name="expectation">What the run requires.</param>
    /// <exception cref="InvalidDataException">A file is missing, duplicated, unexpected or has another hash.</exception>
    private static void ValidateDevFiles(OperatingPointsData data, OperatingPointsExpectation expectation)
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var file in data.DevFiles)
        {
            if (!seen.Add(file.Path))
            {
                throw new InvalidDataException($"The selection artifact lists the development file '{file.Path}' more than once.");
            }

            if (!expectation.DevFileHashes.TryGetValue(file.Path, out var expected))
            {
                throw new InvalidDataException($"The selection artifact names '{file.Path}', which is not a development file of the data manifest.");
            }

            if (!string.Equals(expected, file.Sha256, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidDataException($"The selection artifact names '{file.Path}' with a hash that differs from the data manifest.");
            }
        }

        var absent = expectation.DevFileHashes.Keys.FirstOrDefault(p => !seen.Contains(p));
        if (absent != null)
        {
            throw new InvalidDataException($"The selection artifact does not list the development file '{absent}' of the data manifest.");
        }
    }

    /// <summary>
    ///     Requires the registered grid, exactly the points <c>strict</c> and <c>balanced</c> with their registered targets, and grid thresholds.
    /// </summary>
    /// <param name="data">The artifact.</param>
    /// <exception cref="InvalidDataException">The grid or the set of points differs from the registered one, or a threshold is off the grid.</exception>
    private static void ValidateGridAndPoints(OperatingPointsData data)
    {
        if (data.Grid.Count != ConfidenceCurve.Grid.Count || data.Grid.Where((t, i) => Math.Abs(t - ConfidenceCurve.Grid[i]) >= 1e-9).Any())
        {
            throw new InvalidDataException("The selection artifact does not carry the registered threshold grid.");
        }

        var registered = new Dictionary<string, (double MaxHarmful, double MinCoverage)>(StringComparer.Ordinal)
        {
            ["strict"] = (OperatingPointRule.StrictMaxHarmful, OperatingPointRule.StrictMinCoverage),
            ["balanced"] = (OperatingPointRule.BalancedMaxHarmful, OperatingPointRule.BalancedMinCoverage),
        };
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var point in data.Points)
        {
            if (!registered.TryGetValue(point.Name, out var target))
            {
                throw new InvalidDataException($"The selection artifact names the point '{point.Name}'; the registered points are 'strict' and 'balanced'.");
            }

            if (!seen.Add(point.Name))
            {
                throw new InvalidDataException($"The selection artifact lists the point '{point.Name}' more than once.");
            }

            if (Math.Abs(point.MaxHarmfulPer1000Answered - target.MaxHarmful) >= 1e-9 || Math.Abs(point.MinCoveragePercent - target.MinCoverage) >= 1e-9)
            {
                throw new InvalidDataException($"The target of the point '{point.Name}' differs from the registered rule.");
            }

            if (point.Threshold.HasValue && ConfidenceCurve.IndexOf(point.Threshold.Value) < 0)
            {
                throw new InvalidDataException($"The threshold of the point '{point.Name}' is not on the grid.");
            }
        }

        if (seen.Count != registered.Count)
        {
            throw new InvalidDataException("The selection artifact must hold exactly the points 'strict' and 'balanced'.");
        }
    }

    /// <summary>
    ///     Requires that the points of the artifact equal the points that the registered rule selects from the development curve.
    /// </summary>
    /// <param name="data">The artifact.</param>
    /// <param name="pooled">The curve rows of the pooled development decisions, in grid order.</param>
    /// <exception cref="InvalidDataException">A point or the number of development decisions differs from the derivation.</exception>
    private static void ValidateAgainstDerivation(OperatingPointsData data, IReadOnlyList<CurveRow> pooled)
    {
        if (pooled.Count > 0 && pooled[0].Decisions != data.OpenDevDecisions + data.GoldDevDecisions)
        {
            throw new InvalidDataException("The decisions of the selection artifact differ from the development data.");
        }

        foreach (var derived in OperatingPointRule.Select(pooled))
        {
            var recorded = data.Points.Single(p => string.Equals(p.Name, derived.Name, StringComparison.Ordinal));
            var row = derived.Row;
            var same = recorded.Threshold.HasValue == derived.Threshold.HasValue
                && (!recorded.Threshold.HasValue || Math.Abs(recorded.Threshold.Value - derived.Threshold!.Value) < 1e-9)
                && recorded.Answered == (row?.Answered ?? 0)
                && recorded.Harmful == (row?.Harmful ?? 0)
                && Math.Abs(recorded.CoveragePercent - (row?.CoveragePercent ?? 0)) < 1e-9
                && Math.Abs(recorded.HarmfulPer1000Answered - (row?.HarmfulPer1000Answered ?? 0)) < 1e-9;
            if (!same)
            {
                throw new InvalidDataException($"The point '{derived.Name}' of the selection artifact is not what the rule '{OperatingPointRule.Id}' selects from the development data.");
            }
        }
    }
}

/// <summary>
///     Derives the pooled development curve of a model: the cheap, deterministic input of the check that the selection
///     artifact is what the registered rule selects.
/// </summary>
/// <remarks>
///     Reads the two development files only (through <see cref="DevSets"/>) and runs no bootstrap. Stateless and thread-safe.
/// </remarks>
internal static class DevDerivation
{
    /// <summary>
    ///     Computes the curve rows of the pooled development decisions from the outcomes of the two development sets.
    /// </summary>
    /// <param name="open">The outcomes of the open development split.</param>
    /// <param name="gold">The outcomes of the gold v2 development split without <c>to</c> and <c>not</c>.</param>
    /// <returns>One row for each threshold of the grid.</returns>
    public static List<CurveRow> PooledCurve(IReadOnlyList<Outcome> open, IReadOnlyList<Outcome> gold)
    {
        ArgumentNullException.ThrowIfNull(open);
        ArgumentNullException.ThrowIfNull(gold);
        var pooled = new EvalTally();
        pooled.Add(EvalTally.Of(open));
        pooled.Add(EvalTally.Of(gold));
        return ConfidenceCurve.Of(pooled);
    }

    /// <summary>
    ///     Tells whether the two development files exist below a repository root.
    /// </summary>
    /// <param name="repositoryRoot">The full path of the repository root.</param>
    /// <returns><c>true</c> when both files exist.</returns>
    public static bool IsAvailable(string repositoryRoot)
    {
        ArgumentNullException.ThrowIfNull(repositoryRoot);
        return DataGuard.DevelopmentFiles.All(f => File.Exists(Path.Combine(repositoryRoot, f.Replace('/', Path.DirectorySeparatorChar))));
    }

    /// <summary>
    ///     Scores the model on the two development sets of a repository and computes the pooled curve.
    /// </summary>
    /// <param name="repositoryRoot">The full path of the repository root.</param>
    /// <param name="weights">The bytes of the weights file of the model.</param>
    /// <returns>One row for each threshold of the grid.</returns>
    public static List<CurveRow> PooledCurveOfModel(string repositoryRoot, byte[] weights)
    {
        ArgumentNullException.ThrowIfNull(repositoryRoot);
        ArgumentNullException.ThrowIfNull(weights);
        var result = DevEvaluator.Evaluate(DevEvaluator.CreateTagger(weights), DevSets.ReadOpenDev(repositoryRoot), DevSets.ReadGoldDev(repositoryRoot));
        return PooledCurve(result.Open, result.Gold);
    }
}

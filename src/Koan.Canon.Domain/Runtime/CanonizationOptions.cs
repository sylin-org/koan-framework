namespace Koan.Canon.Domain.Runtime;

/// <summary>
/// Options that influence canonization behaviour for a single operation.
/// </summary>
public sealed record CanonizationOptions
{
    /// <summary>
    /// Default options instance.
    /// </summary>
    public static CanonizationOptions Default { get; } = new();

    /// <summary>
    /// Optional origin override for the canonization request.
    /// </summary>
    public string? Origin { get; init; }

    /// <summary>
    /// Correlation identifier carried through the pipeline.
    /// </summary>
    public string? CorrelationId { get; init; }

    /// <summary>
    /// Forces reprojection of all configured views.
    /// </summary>
    public bool ForceRebuild { get; init; }

    /// <summary>
    /// Prevents distribution hooks from executing.
    /// </summary>
    public bool SkipDistribution { get; init; }

    /// <summary>
    /// Controls staging behaviour for the request.
    /// </summary>
    public CanonStageBehavior StageBehavior { get; init; } = CanonStageBehavior.Default;

    /// <summary>
    /// Optional view subset that should be reprojected.
    /// </summary>
    public string[]? RequestedViews { get; init; }

    /// <summary>
    /// Additional metadata tags forwarded to the pipeline.
    /// </summary>
    public Dictionary<string, string?> Tags { get; init; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Produces a copy of the current options instance with defensive copies of mutable collections.
    /// </summary>
    public CanonizationOptions Copy()
        => this with
        {
            RequestedViews = RequestedViews?.ToArray(),
            Tags = new Dictionary<string, string?>(Tags, StringComparer.OrdinalIgnoreCase)
        };

    /// <summary>
    /// Combines two option sets, preferring explicitly provided values on <paramref name="primary"/>.
    /// </summary>
    public static CanonizationOptions Merge(CanonizationOptions primary, CanonizationOptions? fallback)
    {
        if (primary is null)
        {
            throw new ArgumentNullException(nameof(primary));
        }

        if (fallback is null)
        {
            return primary.Copy();
        }

        var secondary = fallback.Copy();
        var mergedTags = BuildMergedTags(primary.Tags, secondary.Tags);

        return new CanonizationOptions
        {
            Origin = primary.Origin ?? secondary.Origin,
            CorrelationId = primary.CorrelationId ?? secondary.CorrelationId,
            ForceRebuild = primary.ForceRebuild || secondary.ForceRebuild,
            SkipDistribution = primary.SkipDistribution || secondary.SkipDistribution,
            StageBehavior = primary.StageBehavior != CanonStageBehavior.Default ? primary.StageBehavior : secondary.StageBehavior,
            RequestedViews = primary.RequestedViews ?? secondary.RequestedViews,
            Tags = mergedTags
        };
    }

    /// <summary>
    /// Creates a copy of the options with an additional tag.
    /// </summary>
    public CanonizationOptions WithTag(string key, string? value)
    {
        if (string.IsNullOrWhiteSpace(key))
        {
            throw new ArgumentException("Tag key must be provided.", nameof(key));
        }

    var copy = Copy();
    copy.Tags[key] = value;
    return copy;
    }

    /// <summary>
    /// Creates a copy of the options with a custom origin.
    /// </summary>
    public CanonizationOptions WithOrigin(string origin)
    {
        if (string.IsNullOrWhiteSpace(origin))
        {
            throw new ArgumentException("Origin must be provided.", nameof(origin));
        }

    return Copy() with { Origin = origin };
    }

    /// <summary>
    /// Creates a copy with explicit stage behaviour.
    /// </summary>
    public CanonizationOptions WithStageBehavior(CanonStageBehavior behaviour)
        => Copy() with { StageBehavior = behaviour };

    /// <summary>
    /// Creates a copy with requested views set to the provided values.
    /// </summary>
    public CanonizationOptions WithRequestedViews(params string[] views)
    {
        if (views is null)
        {
            throw new ArgumentNullException(nameof(views));
        }

    return Copy() with { RequestedViews = views.Length == 0 ? Array.Empty<string>() : views.ToArray() };
    }

    private static Dictionary<string, string?> BuildMergedTags(IReadOnlyDictionary<string, string?> primary, IReadOnlyDictionary<string, string?> fallback)
    {
        var tags = new Dictionary<string, string?>(fallback, StringComparer.OrdinalIgnoreCase);
        foreach (var pair in primary)
        {
            tags[pair.Key] = pair.Value;
        }

        return tags;
    }
}

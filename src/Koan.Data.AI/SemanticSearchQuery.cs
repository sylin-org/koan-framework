namespace Koan.Data.AI;

/// <summary>Compact declaration of one entity semantic search — the <c>s => s.Top(10).Threshold(0.7)</c>
/// shape passed to <see cref="AiStatics{TModel}.Search"/> and
/// <see cref="AiStatics{TModel}.SearchScored"/>. Fluent like <c>VectorQuery</c>, scoped to the
/// entity-level knobs; undeclared members keep their defaults.</summary>
public sealed class SemanticSearchQuery
{
    /// <summary>Matches returned when <see cref="Top"/> is not declared.</summary>
    public const int DefaultTop = 10;

    private int _top = DefaultTop;
    private double _threshold;
    private string? _partition;

    /// <summary>Maximum number of vector matches to consider.</summary>
    public SemanticSearchQuery Top(int top)
    {
        if (top <= 0) throw new ArgumentOutOfRangeException(nameof(top));
        _top = top;
        return this;
    }

    /// <summary>Minimum similarity for a match to be returned. Similarity is normalized to [0,1];
    /// 0 (the default) keeps every match the store ranks.</summary>
    public SemanticSearchQuery Threshold(double threshold)
    {
        if (!double.IsFinite(threshold) || threshold is < 0 or > 1)
            throw new ArgumentOutOfRangeException(nameof(threshold), "Similarity threshold must be finite and within [0,1].");
        _threshold = threshold;
        return this;
    }

    /// <summary>Load the matching entities under this partition instead of the ambient scope;
    /// null or empty reads unscoped.</summary>
    public SemanticSearchQuery Partition(string? partition)
    {
        _partition = partition;
        return this;
    }

    internal int TopCount => _top;
    internal double MinimumSimilarity => _threshold;
    internal string? PartitionName => _partition;
}

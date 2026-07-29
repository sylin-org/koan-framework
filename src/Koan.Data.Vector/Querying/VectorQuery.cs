using Koan.Data.Abstractions.Filtering;
using Koan.Data.Vector.Abstractions;

namespace Koan.Data.Vector;

/// <summary>Compact provider-neutral vector query declaration.</summary>
public sealed class VectorQuery
{
    public const int DefaultTop = 10;

    private int _top = DefaultTop;
    private Filter? _filter;
    private string? _space;
    private double? _minimumSimilarity;
    private string? _text;
    private double? _semanticWeight;
    private string? _continuation;

    public VectorQuery Top(int top)
    {
        if (top <= 0) throw new ArgumentOutOfRangeException(nameof(top));
        _top = top;
        return this;
    }

    public VectorQuery Where(Filter filter)
    {
        ArgumentNullException.ThrowIfNull(filter);
        _filter = filter;
        return this;
    }

    public VectorQuery Space(string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        _space = name.Trim();
        return this;
    }

    public VectorQuery AtLeast(double similarity)
    {
        if (!double.IsFinite(similarity) || similarity is < 0 or > 1)
            throw new ArgumentOutOfRangeException(nameof(similarity), "Vector similarity must be finite and within [0,1].");
        _minimumSimilarity = similarity;
        return this;
    }

    public VectorQuery Text(string text)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(text);
        _text = text;
        return this;
    }

    public VectorQuery SemanticWeight(double weight)
    {
        if (!double.IsFinite(weight) || weight is < 0 or > 1)
            throw new ArgumentOutOfRangeException(nameof(weight), "SemanticWeight must be finite and within [0,1].");
        _semanticWeight = weight;
        return this;
    }

    public VectorQuery After(string continuation)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(continuation);
        _continuation = continuation.Trim();
        return this;
    }

    internal VectorSearchRequest Build(ReadOnlyMemory<float> embedding, VectorSpacePlan plan, int maxTop)
    {
        if (_top > maxTop)
            throw new InvalidOperationException(
                $"Vector Top({_top}) exceeds the configured bound of {maxTop}. Narrow the query or increase VectorDefaults:MaxTop.");
        if (_semanticWeight is not null && _text is null)
            throw new InvalidOperationException("SemanticWeight(...) requires Text(...).");
        if (_space is not null && !string.Equals(_space, plan.Name, StringComparison.Ordinal))
            throw new InvalidOperationException(
                $"Vector space '{_space}' is not declared for source '{plan.Source}'. Available space: {plan.Name}.");
        return new VectorSearchRequest(
            embedding,
            _top,
            _filter,
            _space ?? plan.Name,
            _minimumSimilarity,
            _text,
            _semanticWeight,
            _continuation);
    }
}

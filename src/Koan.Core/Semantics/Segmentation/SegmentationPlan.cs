using System.Collections.Concurrent;
using System.Collections.Immutable;

namespace Koan.Core.Semantics.Segmentation;

/// <summary>One immutable host-owned catalog of hard segmentation dimensions.</summary>
public sealed class SegmentationPlan
{
    public static SegmentationPlan Empty { get; } = new([]);

    private readonly ImmutableArray<SegmentationDimension> _dimensions;
    private readonly ConcurrentDictionary<Type, SegmentationScope> _typedScopes = new();

    internal SegmentationPlan(ImmutableArray<SegmentationDimension> dimensions)
    {
        _dimensions = dimensions;
        Untyped = dimensions.IsEmpty
            ? SegmentationScope.Empty
            : new SegmentationScope(dimensions, allowHost: true);
    }

    public bool IsEmpty => _dimensions.IsEmpty;

    public IReadOnlyList<SegmentationDimension> Dimensions => _dimensions;

    /// <summary>All active dimensions for a type-less pillar operation such as a generic cache key.</summary>
    public SegmentationScope Untyped { get; }

    /// <summary>Compiles applicable dimensions once for an Entity or other stable CLR operation shape.</summary>
    public SegmentationScope For(Type subject) => _typedScopes.GetOrAdd(
        subject ?? throw new ArgumentNullException(nameof(subject)),
        static (type, dimensions) =>
        {
            var applicable = dimensions
                .Where(dimension => dimension.AppliesTo?.Invoke(type) ?? true)
                .ToImmutableArray();
            return applicable.IsEmpty
                ? SegmentationScope.Empty
                : new SegmentationScope(applicable, allowHost: false);
        },
        _dimensions);
}

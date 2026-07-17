using System.Collections.Immutable;

namespace Koan.Core.Semantics.Segmentation;

/// <summary>Immutable structural dimension set for one pillar-owned operation shape.</summary>
public sealed class SegmentationScope
{
    internal static SegmentationScope Empty { get; } = new([], allowHost: true);

    private readonly ImmutableArray<SegmentationDimension> _dimensions;
    private readonly bool _allowHost;

    internal SegmentationScope(ImmutableArray<SegmentationDimension> dimensions, bool allowHost)
    {
        _dimensions = dimensions;
        _allowHost = allowHost;
    }

    public bool IsEmpty => _dimensions.IsEmpty;

    public IReadOnlyList<string> DimensionIds => _dimensions
        .Select(static dimension => dimension.Id)
        .ToArray();

    /// <summary>Binds current ambient values once for one semantic operation.</summary>
    public ImmutableArray<SegmentationBinding> Bind(string operation)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(operation);
        if (_dimensions.IsEmpty) return [];

        var bindings = ImmutableArray.CreateBuilder<SegmentationBinding>(_dimensions.Length);
        foreach (var dimension in _dimensions)
        {
            var current = dimension.Read();
            switch (current.Kind)
            {
                case SegmentationValueKind.Concrete when current.Value is { Length: > 0 } value:
                    bindings.Add(new SegmentationBinding(dimension.Id, value));
                    break;
                case SegmentationValueKind.Host:
                    if (_allowHost) break;
                    throw new SegmentationRequiredException(
                        dimension.Id,
                        operation.Trim(),
                        dimension.Correction);
                default:
                    throw new SegmentationRequiredException(
                        dimension.Id,
                        operation.Trim(),
                        dimension.Correction);
            }
        }

        return bindings.ToImmutable();
    }
}

using System.Collections.Immutable;

namespace Koan.Core.Semantics.Segmentation;

/// <summary>Stages capability-owned dimensions and freezes one validated host plan.</summary>
internal sealed class SegmentationPlanBuilder
{
    private readonly List<SegmentationDimension> _dimensions = [];
    private SegmentationPlan? _compiled;

    internal SegmentationContributionTarget ForOwner(SemanticId owner)
    {
        EnsureMutable();
        return new SegmentationContributionTarget(this, owner.Value);
    }

    internal void Add(
        string owner,
        string id,
        Func<SegmentationValue> read,
        Func<Type, bool>? appliesTo,
        string correction)
    {
        EnsureMutable();
        ArgumentNullException.ThrowIfNull(read);
        ArgumentException.ThrowIfNullOrWhiteSpace(correction);
        var normalizedCorrection = correction.Trim();
        if (normalizedCorrection.Length > 300 || normalizedCorrection.Contains('\n') || normalizedCorrection.Contains('\r'))
        {
            throw new ArgumentException(
                "A segmentation correction must be a single bounded semantic sentence (300 characters or fewer).",
                nameof(correction));
        }

        _dimensions.Add(new SegmentationDimension(
            owner,
            new SemanticId(id),
            read,
            appliesTo,
            normalizedCorrection));
    }

    internal SegmentationPlan Build()
    {
        if (_compiled is not null) return _compiled;

        var duplicate = _dimensions
            .GroupBy(static dimension => dimension.Id, StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault(static group => group.Count() > 1);
        if (duplicate is not null)
        {
            var owners = string.Join(", ", duplicate
                .Select(static dimension => dimension.Owner)
                .Order(StringComparer.Ordinal));
            throw new InvalidOperationException(
                $"Segmentation dimension '{duplicate.Key}' is declared more than once by: {owners}. " +
                "One capability must own each dimension identity.");
        }

        var dimensions = _dimensions
            .OrderBy(static dimension => dimension.Id, StringComparer.Ordinal)
            .ToImmutableArray();
        _compiled = dimensions.IsEmpty ? SegmentationPlan.Empty : new SegmentationPlan(dimensions);
        return _compiled;
    }

    private void EnsureMutable()
    {
        if (_compiled is not null)
        {
            throw new InvalidOperationException(
                "The segmentation plan is already compiled and cannot accept more dimensions.");
        }
    }
}

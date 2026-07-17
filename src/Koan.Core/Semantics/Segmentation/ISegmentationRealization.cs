using System.Collections.Immutable;
using System.ComponentModel;

namespace Koan.Core.Semantics.Segmentation;

/// <summary>Framework-facing receipt from a pillar that physically realizes hard segmentation.</summary>
[EditorBrowsable(EditorBrowsableState.Never)]
public interface ISegmentationRealization
{
    SegmentationRealizationDescriptor SegmentationRealization { get; }
}

/// <summary>Stable, value-free coverage metadata for one pillar realization.</summary>
[EditorBrowsable(EditorBrowsableState.Never)]
public sealed class SegmentationRealizationDescriptor
{
    public SegmentationRealizationDescriptor(
        string pillarId,
        string realizationId,
        IEnumerable<string> coverageIds)
    {
        PillarId = StableId(pillarId, nameof(pillarId));
        RealizationId = StableId(realizationId, nameof(realizationId));
        ArgumentNullException.ThrowIfNull(coverageIds);
        CoverageIds = coverageIds
            .Select(id => StableId(id, nameof(coverageIds)))
            .Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal)
            .ToImmutableArray();
        if (CoverageIds.IsEmpty)
            throw new ArgumentException("A segmentation realization must declare at least one covered operation family.", nameof(coverageIds));
    }

    public string PillarId { get; }

    public string RealizationId { get; }

    public ImmutableArray<string> CoverageIds { get; }

    private static string StableId(string value, string parameter)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value, parameter);
        var normalized = value.Trim();
        if (normalized.Length > 80 || normalized.Any(static c =>
                !(char.IsAsciiLetterLower(c) || char.IsAsciiDigit(c) || c is '.' or '-')))
            throw new ArgumentException(
                "Segmentation evidence IDs must be lowercase stable tokens using letters, digits, '.' or '-'.",
                parameter);
        return normalized;
    }
}

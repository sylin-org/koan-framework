using Koan.Canon.Domain.Metadata;
using Koan.Canon.Domain.Model;

namespace Koan.Canon.Domain.Runtime;

/// <summary>
/// Represents the outcome of a canonization operation.
/// </summary>
/// <typeparam name="T">Canonical entity type.</typeparam>
public sealed class CanonizationResult<T>
    where T : CanonEntity<T>, new()
{
    private readonly IReadOnlyList<CanonizationEvent> _events;

    /// <summary>
    /// Initializes a new instance of <see cref="CanonizationResult{T}"/>.
    /// </summary>
    public CanonizationResult(
        T canonical,
        CanonizationOutcome outcome,
        CanonMetadata metadata,
        IReadOnlyList<CanonizationEvent>? events = null,
        bool reprojectionTriggered = false,
        bool distributionSkipped = false)
    {
        Canonical = canonical ?? throw new ArgumentNullException(nameof(canonical));
        Metadata = metadata ?? throw new ArgumentNullException(nameof(metadata));
        Outcome = outcome;
        _events = events ?? Array.Empty<CanonizationEvent>();
        ReprojectionTriggered = reprojectionTriggered;
        DistributionSkipped = distributionSkipped;
    }

    /// <summary>
    /// Canonical entity after processing.
    /// </summary>
    public T Canonical { get; }

    /// <summary>
    /// Final outcome of the operation.
    /// </summary>
    public CanonizationOutcome Outcome { get; }

    /// <summary>
    /// Metadata snapshot returned with the canonical entity.
    /// </summary>
    public CanonMetadata Metadata { get; }

    /// <summary>
    /// Events emitted during processing.
    /// </summary>
    public IReadOnlyList<CanonizationEvent> Events => _events;

    /// <summary>
    /// Indicates whether reprojection logic was executed.
    /// </summary>
    public bool ReprojectionTriggered { get; }

    /// <summary>
    /// Indicates whether distribution steps were skipped.
    /// </summary>
    public bool DistributionSkipped { get; }

    /// <summary>
    /// Creates a copy of the result with additional events appended.
    /// </summary>
    public CanonizationResult<T> WithEvents(IEnumerable<CanonizationEvent> additionalEvents)
    {
        if (additionalEvents is null)
        {
            throw new ArgumentNullException(nameof(additionalEvents));
        }

        var combined = _events.Concat(additionalEvents).ToList();
        return new CanonizationResult<T>(Canonical, Outcome, Metadata.Clone(), combined, ReprojectionTriggered, DistributionSkipped);
    }

    /// <summary>
    /// Creates a copy of the result with a different outcome flag.
    /// </summary>
    public CanonizationResult<T> WithOutcome(CanonizationOutcome outcome)
        => new(Canonical, outcome, Metadata.Clone(), _events, ReprojectionTriggered, DistributionSkipped);
}

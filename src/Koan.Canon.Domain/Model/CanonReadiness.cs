namespace Koan.Canon.Domain.Model;

/// <summary>
/// Describes the readiness level of a canonical entity for downstream consumers.
/// </summary>
public enum CanonReadiness
{
    /// <summary>
    /// Readiness is unknown; contributors have not explicitly set it.
    /// </summary>
    Unknown = 0,

    /// <summary>
    /// Canonical entity is complete and safe for downstream consumers.
    /// </summary>
    Complete = 1,

    /// <summary>
    /// Canonical entity is waiting on parent or related entities to be canonized.
    /// </summary>
    PendingRelationships = 2,

    /// <summary>
    /// Canonical entity is awaiting enrichment or external data to reach the desired fidelity.
    /// </summary>
    PendingEnrichment = 3,

    /// <summary>
    /// Canonical entity is provisional and may be replaced or retracted.
    /// </summary>
    Provisional = 4,

    /// <summary>
    /// Canonical entity failed quality gates and requires human remediation.
    /// </summary>
    RequiresManualReview = 5,

    /// <summary>
    /// Canonical entity is in a degraded state and should not be distributed further.
    /// </summary>
    Degraded = 6
}

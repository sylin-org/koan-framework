namespace Koan.Canon.Domain.Model;

/// <summary>
/// Represents the lifecycle state of a canonical entity.
/// </summary>
public enum CanonLifecycle
{
    /// <summary>
    /// Canonical entity is active and represents the latest assembled view.
    /// </summary>
    Active = 0,

    /// <summary>
    /// Canonical entity is pending retirement while a replacement is evaluated.
    /// </summary>
    PendingRetirement = 1,

    /// <summary>
    /// Canonical entity has been superseded by a newer canonical identifier.
    /// </summary>
    Superseded = 2,

    /// <summary>
    /// Canonical entity is archived and no longer participates in projection updates.
    /// </summary>
    Archived = 3,

    /// <summary>
    /// Canonical entity has been withdrawn due to irrecoverable policy or validation failures.
    /// </summary>
    Withdrawn = 4
}

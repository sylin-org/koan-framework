namespace Koan.Web.Contracts;

/// <summary>
/// Revert payload for audit snapshots.
/// </summary>
public sealed class AuditRevert
{
    /// <summary>Snapshot version to revert to.</summary>
    public required int Version { get; set; }
    /// <summary>Optional note.</summary>
    public string? Note { get; set; }
    /// <summary>Optional target set to write the reverted entity to; defaults to current set.</summary>
    public string? TargetSet { get; set; }
}
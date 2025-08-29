namespace Sora.Web.Contracts;

/// <summary>
/// Moderation approve options.
/// </summary>
public sealed class ApproveOptions
{
    /// <summary>Optional target set to write the approved entity to; defaults to current set.</summary>
    public string? TargetSet { get; set; }
    /// <summary>Free-form note for audit trails.</summary>
    public string? Note { get; set; }
    /// <summary>Optional object whose properties will be merged onto the draft before approval.</summary>
    public object? Transform { get; set; }
}

/// <summary>
/// Moderation rejection payload.
/// </summary>
public sealed class RejectOptions
{
    /// <summary>Required human-readable reason for rejection.</summary>
    public required string Reason { get; set; }
    /// <summary>Optional additional note.</summary>
    public string? Note { get; set; }
}

/// <summary>
/// Create draft payload.
/// </summary>
public sealed class DraftCreate
{
    /// <summary>Optional initial snapshot for the draft; merged as-is into the entity.</summary>
    public object? Snapshot { get; set; }
    /// <summary>Optional note.</summary>
    public string? Note { get; set; }
}

/// <summary>
/// Update draft payload.
/// </summary>
public sealed class DraftUpdate
{
    /// <summary>Partial object to merge onto the existing draft snapshot.</summary>
    public object? Snapshot { get; set; }
    /// <summary>Optional note.</summary>
    public string? Note { get; set; }
}

/// <summary>
/// Submit draft payload.
/// </summary>
public sealed class DraftSubmit
{
    /// <summary>Optional note.</summary>
    public string? Note { get; set; }
}

/// <summary>
/// Withdraw draft payload.
/// </summary>
public sealed class DraftWithdraw
{
    /// <summary>Optional reason for withdrawing.</summary>
    public string? Reason { get; set; }
}

/// <summary>
/// Soft delete options.
/// </summary>
public sealed class SoftDeleteOptions
{
    /// <summary>Optional note.</summary>
    public string? Note { get; set; }
    /// <summary>Optional source set to move from; if not set, deletes from the current set.</summary>
    public string? FromSet { get; set; }
}

/// <summary>
/// Restore options.
/// </summary>
public sealed class RestoreOptions
{
    /// <summary>Optional note.</summary>
    public string? Note { get; set; }
    /// <summary>Optional target set to restore into; defaults to current set.</summary>
    public string? TargetSet { get; set; }
}

/// <summary>
/// Bulk operation shape for batch actions.
/// </summary>
public sealed class BulkOperation<TId>
{
    /// <summary>IDs to include in the operation; either Ids or Filter must be provided.</summary>
    public IReadOnlyList<TId>? Ids { get; set; }
    /// <summary>Optional string-query filter; requires a string-query capable repository.</summary>
    public string? Filter { get; set; }
    /// <summary>Operation-specific options (e.g., FromSet/TargetSet).</summary>
    public object? Options { get; set; }
}

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

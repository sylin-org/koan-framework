namespace Koan.Data.Backup.Models;

/// <summary>Choices required to restore one Entity archive.</summary>
public sealed record RestoreRequest
{
    public string StorageProfile { get; init; } = "";
    public string? TargetPartition { get; init; }
    public int BatchSize { get; init; } = 1000;

    /// <summary>
    /// An administrative override: restore an archive taken under another tenant (or other hard segmentation), or a
    /// format 1 archive that recorded none, into the current scope. Without it such a restore is refused before any
    /// record is written, because the rows would be stamped with the current tenant (NK-084).
    /// </summary>
    public bool AcrossScopes { get; init; }
}

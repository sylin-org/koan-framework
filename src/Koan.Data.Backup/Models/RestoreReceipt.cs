namespace Koan.Data.Backup.Models;

/// <summary>Immutable evidence that a validated Entity archive was applied.</summary>
public sealed record RestoreReceipt(
    string ArchiveId,
    string StorageKey,
    string? TargetPartition,
    int RecordCount,
    string DataContentSha256,
    DateTimeOffset CompletedAt);

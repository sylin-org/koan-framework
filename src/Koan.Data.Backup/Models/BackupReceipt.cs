namespace Koan.Data.Backup.Models;

/// <summary>Immutable evidence that a complete Entity archive was published.</summary>
public sealed record BackupReceipt(
    string ArchiveId,
    string Name,
    string StorageProfile,
    string StorageKey,
    DateTimeOffset CreatedAt,
    string? SourcePartition,
    int RecordCount,
    string DataContentSha256,
    long ArchiveBytes,
    string? ArchiveContentHash);

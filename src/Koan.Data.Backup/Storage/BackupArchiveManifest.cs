namespace Koan.Data.Backup.Storage;

internal sealed record BackupArchiveManifest(
    int FormatVersion,
    string ArchiveId,
    string Name,
    DateTimeOffset CreatedAt,
    string EntityType,
    string KeyType,
    string? SourcePartition,
    int RecordCount,
    string DataEntry,
    string DataContentSha256);

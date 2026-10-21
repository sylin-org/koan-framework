namespace Koan.Data.Backup.Storage;

/// <summary>
/// The manifest of one archive. Format 2 adds <c>Scope</c>: the hard segmentation the archive was taken under, one
/// entry per bound dimension (the tenant), each value a SHA-256 digest so the archive does not carry the tenant id in
/// clear. It is empty when nothing was bound, and absent in a format 1 archive, which recorded no scope (NK-084).
/// </summary>
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
    string DataContentSha256,
    Dictionary<string, string>? Scope = null);

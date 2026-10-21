namespace Koan.Data.Backup.Infrastructure;

internal static class BackupConstants
{
    /// <summary>Format 2 records the archive's segmentation scope (NK-084). Format 1 archives still restore.</summary>
    public const int FormatVersion = 2;
    public const int LegacyFormatVersion = 1;
    public const string ArchiveContainer = "backups";
    public const string ArchiveContentType = "application/zip";
    public const string ManifestEntry = "manifest.json";
    public const string DataEntry = "entity.jsonl";
    public const string TempDirectory = "Koan.Data.Backup";
}

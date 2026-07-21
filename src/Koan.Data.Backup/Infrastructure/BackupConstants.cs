namespace Koan.Data.Backup.Infrastructure;

internal static class BackupConstants
{
    public const int FormatVersion = 1;
    public const string ArchiveContainer = "backups";
    public const string ArchiveContentType = "application/zip";
    public const string ManifestEntry = "manifest.json";
    public const string DataEntry = "entity.jsonl";
    public const string TempDirectory = "Koan.Data.Backup";
}

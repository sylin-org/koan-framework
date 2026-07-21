using System.Globalization;
using System.Text;

namespace Koan.Data.Backup.Storage;

internal static class BackupArchiveNaming
{
    public static BackupArchiveDescriptor Create(string backupName, DateTimeOffset createdAt, string archiveId)
    {
        if (string.IsNullOrWhiteSpace(backupName))
            throw new ArgumentException("Backup name is required.", nameof(backupName));
        if (string.IsNullOrWhiteSpace(archiveId))
            throw new ArgumentException("Archive ID is required.", nameof(archiveId));

        var safeName = SanitizeName(backupName);
        var fileName = $"{safeName}-{createdAt:yyyyMMdd-HHmmss}-{archiveId}.zip";
        var datePath = createdAt.ToString("yyyy/MM/dd", CultureInfo.InvariantCulture);
        return new BackupArchiveDescriptor(fileName, string.Join('/', safeName, datePath, fileName));
    }

    private static string SanitizeName(string name)
    {
        var builder = new StringBuilder(name.Length);
        var lastWasSeparator = false;

        foreach (var ch in name.Trim())
        {
            if (char.IsLetterOrDigit(ch))
            {
                builder.Append(char.ToLowerInvariant(ch));
                lastWasSeparator = false;
            }
            else if (!lastWasSeparator)
            {
                builder.Append('-');
                lastWasSeparator = true;
            }
        }

        var result = builder.ToString().Trim('-');
        return string.IsNullOrEmpty(result) ? "backup" : result;
    }
}

internal readonly record struct BackupArchiveDescriptor(string FileName, string StorageKey);

using System;
using System.Globalization;
using System.IO;
using System.Text;

namespace Koan.Data.Backup.Storage;

/// <summary>
/// Produces consistent archive naming artifacts for backups so storage and tests agree on paths.
/// </summary>
public static class BackupArchiveNaming
{
    public static BackupArchiveDescriptor Create(string backupName, DateTimeOffset createdAt)
    {
        if (string.IsNullOrWhiteSpace(backupName))
            throw new ArgumentException("Backup name is required", nameof(backupName));

        var safeName = SanitizeName(backupName);
        var fileName = $"{safeName}-{createdAt:yyyyMMdd-HHmmss}.zip";
        var datePath = createdAt.ToString("yyyy/MM/dd", CultureInfo.InvariantCulture);
        var storageKey = string.Join('/', safeName, datePath, fileName);

        return new BackupArchiveDescriptor(safeName, createdAt, fileName, storageKey);
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
            else if (ch is '-' or '_' or '.')
            {
                if (!lastWasSeparator)
                {
                    builder.Append('-');
                    lastWasSeparator = true;
                }
            }
            else if (char.IsWhiteSpace(ch))
            {
                if (!lastWasSeparator)
                {
                    builder.Append('-');
                    lastWasSeparator = true;
                }
            }
            else
            {
                if (!lastWasSeparator)
                {
                    builder.Append('-');
                    lastWasSeparator = true;
                }
            }
        }

        var result = builder.ToString().Trim('-');
        return string.IsNullOrEmpty(result) ? "backup" : result;
    }
}

public readonly record struct BackupArchiveDescriptor(string SafeName, DateTimeOffset CreatedAt, string FileName, string StorageKey)
{
    public string GetDisplayPath(string basePath)
    {
        if (string.IsNullOrWhiteSpace(basePath))
            throw new ArgumentException("Base path is required", nameof(basePath));

        var relative = Path.Combine("backups", StorageKey.Replace('/', Path.DirectorySeparatorChar));
        return Path.Combine(basePath, relative);
    }
}


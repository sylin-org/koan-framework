namespace Koan.Data.Backup.Models;

/// <summary>Choices required to create one Entity archive.</summary>
public sealed record BackupRequest
{
    public string StorageProfile { get; init; } = "";
    public string? Partition { get; init; }
    public int PageSize { get; init; } = 1000;
}

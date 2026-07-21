namespace Koan.Data.Backup.Models;

/// <summary>Choices required to restore one Entity archive.</summary>
public sealed record RestoreRequest
{
    public string StorageProfile { get; init; } = "";
    public string? TargetPartition { get; init; }
    public int BatchSize { get; init; } = 1000;
}

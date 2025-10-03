using System.IO.Compression;

namespace Koan.Data.Backup.Models;

public class BackupOptions
{
    public string? Description { get; set; }
    public string[] Tags { get; set; } = Array.Empty<string>();
    public string? Partition { get; set; }
    public string StorageProfile { get; set; } = string.Empty;
    public CompressionLevel CompressionLevel { get; set; } = CompressionLevel.Optimal;
    public bool VerificationEnabled { get; set; } = true;
    public int BatchSize { get; set; } = 1000;
    public Dictionary<string, string> Metadata { get; set; } = new();
}

public class GlobalBackupOptions : BackupOptions
{
    public int MaxConcurrency { get; set; } = Environment.ProcessorCount;
    public string[]? IncludeProviders { get; set; }
    public string[]? ExcludeProviders { get; set; }
    public string[]? IncludeEntityTypes { get; set; }
    public string[]? ExcludeEntityTypes { get; set; }
    public bool IncludeEmptyEntities { get; set; } = false;
    public long MaxEntitySizeBytes { get; set; } = long.MaxValue;
    public TimeSpan Timeout { get; set; } = TimeSpan.FromHours(2);
}

public class RestoreOptions
{
    public string? TargetPartition { get; set; }
    public string StorageProfile { get; set; } = string.Empty;
    public bool ReplaceExisting { get; set; } = false;
    public bool DisableConstraints { get; set; } = true;
    public bool DisableIndexes { get; set; } = true;
    public bool UseBulkMode { get; set; } = true;
    public int BatchSize { get; set; } = 1000;
    public string OptimizationLevel { get; set; } = "Balanced"; // "Fast", "Balanced", "Safe"
    public bool DryRun { get; set; } = false;
    public bool ContinueOnError { get; set; } = false;
    public TimeSpan Timeout { get; set; } = TimeSpan.FromHours(2);
}

public class GlobalRestoreOptions : RestoreOptions
{
    public int MaxConcurrency { get; set; } = Environment.ProcessorCount;
    public string[]? IncludeEntityTypes { get; set; }
    public string[]? ExcludeEntityTypes { get; set; }
    public Dictionary<string, string> EntityPartitionMapping { get; set; } = new(); // EntityType -> TargetPartition
    public bool RestoreToOriginalPartitions { get; set; } = true;
    public bool ValidateBeforeRestore { get; set; } = true;
}
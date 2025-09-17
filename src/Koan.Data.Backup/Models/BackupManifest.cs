using Koan.Data.Core.Model;
using Koan.Storage.Model;

namespace Koan.Data.Backup.Models;

public class BackupManifest : StorageEntity<BackupManifest>
{
    public string Name { get; set; } = default!;
    public string Description { get; set; } = string.Empty;
    public string[] Tags { get; set; } = Array.Empty<string>();
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? CompletedAt { get; set; }
    public TimeSpan Duration { get; set; }
    public BackupStatus Status { get; set; }
    public string CreatedBy { get; set; } = "system";
    public string Version { get; set; } = "1.0";

    // Entity metadata
    public List<EntityBackupInfo> Entities { get; set; } = new();
    public Dictionary<string, string> Metadata { get; set; } = new();

    // Verification data
    public BackupVerification Verification { get; set; } = new();

    // Discovery information
    public EntityDiscoveryInfo Discovery { get; set; } = new();

    // Performance metrics
    public BackupPerformanceInfo Performance { get; set; } = new();
}

public class EntityBackupInfo
{
    public string EntityType { get; set; } = default!;
    public string KeyType { get; set; } = default!;
    public string Set { get; set; } = "root";
    public int ItemCount { get; set; }
    public long SizeBytes { get; set; }
    public string ContentHash { get; set; } = default!;
    public string StorageFile { get; set; } = default!;
    public string Provider { get; set; } = default!;
    public Dictionary<string, string> SchemaSnapshot { get; set; } = new();
    public TimeSpan BackupDuration { get; set; }
    public string? ErrorMessage { get; set; }
}

public class BackupVerification
{
    public string OverallChecksum { get; set; } = default!;
    public long TotalSizeBytes { get; set; }
    public int TotalItemCount { get; set; }
    public double CompressionRatio { get; set; }
    public bool IsValid { get; set; } = true;
    public List<string> ValidationErrors { get; set; } = new();
}

public class EntityDiscoveryInfo
{
    public DateTimeOffset DiscoveredAt { get; set; }
    public int TotalEntityTypes { get; set; }
    public int TotalProviders { get; set; }
    public Dictionary<string, int> EntitiesByProvider { get; set; } = new();
    public int TotalAssembliesScanned { get; set; }
    public int TotalTypesExamined { get; set; }
    public TimeSpan DiscoveryDuration { get; set; }
}

public class BackupPerformanceInfo
{
    public TimeSpan TotalDuration { get; set; }
    public TimeSpan EntityDiscoveryDuration { get; set; }
    public TimeSpan StreamingDuration { get; set; }
    public TimeSpan CompressionDuration { get; set; }
    public TimeSpan VerificationDuration { get; set; }
    public long MemoryUsagePeakBytes { get; set; }
    public int ConcurrentOperations { get; set; }
    public double ThroughputMbps { get; set; }
}

public enum BackupStatus
{
    Unknown,
    Created,
    InProgress,
    Completed,
    Failed,
    Cancelled,
    Corrupted
}
namespace Koan.Data.Backup.Models;

public class EntityTypeInfo
{
    public Type EntityType { get; set; } = default!;
    public Type KeyType { get; set; } = default!;
    public string? Assembly { get; set; }
    public string Provider { get; set; } = default!;
    public List<string> Sets { get; set; } = new();
    public long EstimatedRecords { get; set; }
    public long EstimatedSizeBytes { get; set; }
    public bool IsActive { get; set; } = true;
    public Dictionary<string, object> Metadata { get; set; } = new();
}

public class EntityDiscoveryResult
{
    public List<EntityTypeInfo> Entities { get; set; } = new();
    public DateTimeOffset DiscoveredAt { get; set; } = DateTimeOffset.UtcNow;
    public int TotalAssembliesScanned { get; set; }
    public int TotalTypesExamined { get; set; }
    public TimeSpan DiscoveryDuration { get; set; }
    public string? AssemblyHash { get; set; }
    public List<string> Errors { get; set; } = new();
}

public class RestorePreparationOptions
{
    public int EstimatedEntityCount { get; set; }
    public long EstimatedSizeBytes { get; set; }
    public bool DisableConstraints { get; set; } = true;
    public bool DisableIndexes { get; set; } = true;
    public bool UseBulkMode { get; set; } = true;
    public string OptimizationLevel { get; set; } = "Balanced"; // "Fast", "Balanced", "Safe"
    public Dictionary<string, object> AdapterSpecificOptions { get; set; } = new();
}

public class RestorePreparationContext
{
    public string AdapterType { get; set; } = default!;
    public Dictionary<string, object> PreparationState { get; set; } = new();
    public DateTimeOffset PreparedAt { get; set; }
    public RestorePreparationOptions Options { get; set; } = default!;
    public TimeSpan PreparationDuration { get; set; }
    public List<string> ActionsPerformed { get; set; } = new();
}

public class RestoreOptimizationInfo
{
    public bool SupportsConstraintDisabling { get; set; }
    public bool SupportsIndexDisabling { get; set; }
    public bool SupportsBulkMode { get; set; }
    public double EstimatedSpeedupFactor { get; set; } = 1.0;
    public string RecommendedOptimizationLevel { get; set; } = "Balanced";
    public Dictionary<string, object> AdapterCapabilities { get; set; } = new();
}
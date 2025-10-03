using System.Text.Json;
using Koan.Jobs.Model;

namespace Koan.Jobs.Options;

public sealed class JobsOptions
{
    public JobStorageMode DefaultStore { get; set; } = JobStorageMode.InMemory;
    public string? DefaultSource { get; set; }
    public string? DefaultPartition { get; set; }
    public bool AuditByDefault { get; set; }
    public bool PublishEvents { get; set; } = true;
    public InMemoryStoreOptions InMemory { get; } = new();
    public JsonSerializerOptions SerializerOptions { get; } = new(JsonSerializerDefaults.Web);
}

public sealed class InMemoryStoreOptions
{
    public int CompletedRetentionMinutes { get; set; } = 15;
    public int FaultedRetentionMinutes { get; set; } = 60;
    public int SweepIntervalSeconds { get; set; } = 60;
}

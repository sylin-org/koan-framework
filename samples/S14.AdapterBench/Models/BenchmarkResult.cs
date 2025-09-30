namespace S14.AdapterBench.Models;

public class BenchmarkResult
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public DateTime StartedAt { get; set; } = DateTime.UtcNow;
    public DateTime? CompletedAt { get; set; }
    public BenchmarkMode Mode { get; set; }
    public BenchmarkScale Scale { get; set; }
    public int EntityCount { get; set; }
    public List<ProviderResult> ProviderResults { get; set; } = new();
    public BenchmarkStatus Status { get; set; } = BenchmarkStatus.Running;
}

public class ProviderResult
{
    public string ProviderName { get; set; } = "";
    public bool IsContainerized { get; set; }
    public List<TestResult> Tests { get; set; } = new();
    public TimeSpan TotalDuration { get; set; }
}

public class TestResult
{
    public string TestName { get; set; } = "";
    public string EntityTier { get; set; } = "";
    public TimeSpan Duration { get; set; }
    public int OperationCount { get; set; }
    public double OperationsPerSecond { get; set; }
    public bool UsedNativeExecution { get; set; }
    public string? Error { get; set; }
}

public enum BenchmarkStatus
{
    Running,
    Completed,
    Failed
}

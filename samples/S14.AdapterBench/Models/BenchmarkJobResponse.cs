namespace S14.AdapterBench.Models;

public class BenchmarkJobResponse
{
    public string JobId { get; set; } = "";
    public string Status { get; set; } = "";
    public string Message { get; set; } = "";
}

public class BenchmarkJobStatusResponse
{
    public string JobId { get; set; } = "";
    public string Status { get; set; } = "";
    public double Progress { get; set; }
    public string? ProgressMessage { get; set; }
    public DateTimeOffset? StartedAt { get; set; }
    public DateTimeOffset? CompletedAt { get; set; }
    public TimeSpan? Duration { get; set; }
    public BenchmarkResult? Result { get; set; }
    public string? Error { get; set; }
}

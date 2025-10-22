namespace S14.AdapterBench.Models;

public class BenchmarkProgress
{
    public string CurrentProvider { get; set; } = "";
    public string CurrentTest { get; set; } = "";
    public int TotalTests { get; set; }
    public int CompletedTests { get; set; }
    public int ProgressPercentage => TotalTests > 0 ? (int)((CompletedTests / (double)TotalTests) * 100) : 0;
    public int CurrentOperationCount { get; set; }
    public int TotalOperations { get; set; }
    public double CurrentOperationsPerSecond { get; set; }

    /// <summary>
    /// Per-provider progress tracking for parallel mode visualization.
    /// Key: provider name, Value: completion percentage (0-100)
    /// </summary>
    public Dictionary<string, ProviderProgress> ProviderProgress { get; set; } = new();
}

public class ProviderProgress
{
    public string ProviderName { get; set; } = "";
    public int CompletedTests { get; set; }
    public int TotalTests { get; set; }
    public int ProgressPercentage => TotalTests > 0 ? (int)((CompletedTests / (double)TotalTests) * 100) : 0;
    public string CurrentTest { get; set; } = "";
    public string Status { get; set; } = "pending"; // pending, running, completed, failed
}

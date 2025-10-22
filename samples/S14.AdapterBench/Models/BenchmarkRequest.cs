namespace S14.AdapterBench.Models;

public class BenchmarkRequest
{
    public BenchmarkMode Mode { get; set; } = BenchmarkMode.Sequential;
    public BenchmarkScale Scale { get; set; } = BenchmarkScale.Quick;
    public List<string> Providers { get; set; } = new();
    public List<string> EntityTiers { get; set; } = new() { "Minimal", "Indexed", "Complex" };

    /// <summary>
    /// Enable context switching tests to measure provider switch overhead
    /// </summary>
    public bool IncludeContextSwitchingTests { get; set; } = false;

    /// <summary>
    /// Enable mirror/move operator tests (cross-provider data migration)
    /// </summary>
    public bool IncludeMirrorMoveTests { get; set; } = false;

    /// <summary>
    /// Custom entity count (overrides Scale if set)
    /// </summary>
    public int? CustomEntityCount { get; set; }
}

public enum BenchmarkMode
{
    Sequential,
    Parallel
}

public enum BenchmarkScale
{
    Micro,      // 100 entities - for quick smoke tests
    Quick,      // 1k entities - fast testing
    Standard,   // 5k entities - baseline benchmarks
    Full,       // 10k entities - comprehensive testing
    Large,      // 100k entities - stress testing
    Massive,    // 1M entities - extreme stress testing
    Custom      // Use CustomEntityCount property
}

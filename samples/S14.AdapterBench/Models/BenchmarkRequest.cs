namespace S14.AdapterBench.Models;

public class BenchmarkRequest
{
    public BenchmarkMode Mode { get; set; } = BenchmarkMode.Sequential;
    public BenchmarkScale Scale { get; set; } = BenchmarkScale.Quick;
    public List<string> Providers { get; set; } = new();
    public List<string> EntityTiers { get; set; } = new() { "Minimal", "Indexed", "Complex" };
}

public enum BenchmarkMode
{
    Sequential,
    Parallel
}

public enum BenchmarkScale
{
    Quick,    // 1k entities
    Standard, // 5k entities
    Full      // 10k entities
}

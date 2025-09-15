namespace Koan.Scheduling;

public sealed class SchedulingOptions
{
    public bool Enabled { get; set; } = true; // Dev default on; production may override via config binding site.
    public bool ReadinessGate { get; set; } = true;

    // Per-job overrides by Id
    public Dictionary<string, JobOptions> Jobs { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    public sealed class JobOptions
    {
        public bool? Enabled { get; set; }
        public bool? OnStartup { get; set; }
        public TimeSpan? FixedDelay { get; set; }
        public string? Cron { get; set; }
        public bool? Critical { get; set; }
        public TimeSpan? Timeout { get; set; }
        public int? MaxConcurrency { get; set; }
        public string? Runner { get; set; } // e.g., "bootstrap"
        public List<string>? Tasks { get; set; } // runner-specific tasks
    }
}

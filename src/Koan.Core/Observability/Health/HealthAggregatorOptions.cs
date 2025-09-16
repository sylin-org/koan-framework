namespace Koan.Core.Observability.Health;

public sealed class HealthAggregatorOptions
{
    public bool Enabled { get; set; } = true;

    public LimitsOptions Limits { get; set; } = new();
    public PolicyOptions Policy { get; set; } = new();
    public SchedulerOptions Scheduler { get; set; } = new();
    public TtlOptions Ttl { get; set; } = new();

    public sealed class LimitsOptions
    {
        public int MaxMessageLength { get; set; } = 2048;
        public int MaxFactsCountPerComponent { get; set; } = 32;
        public int MaxFactsBytesPerComponent { get; set; } = 4096;
    }

    public sealed class PolicyOptions
    {
        public bool ConsiderOnlyCriticalForOverall { get; set; } = false;
        public bool TreatUnknownAsDegradedForRequired { get; set; } = true;
        public List<string> RequiredComponents { get; set; } = new();
        public TimeSpan SnapshotStalenessWindow { get; set; } = TimeSpan.FromSeconds(10);
    }

    public sealed class SchedulerOptions
    {
        public bool EnableTtlScheduling { get; set; } = true;
        public TimeSpan QuantizationWindow { get; set; } = TimeSpan.FromSeconds(2);
        public double JitterPercent { get; set; } = 0.05; // 5%
        public TimeSpan JitterAbsoluteMin { get; set; } = TimeSpan.FromMilliseconds(25);
        public double RefreshLeadPercent { get; set; } = 0.20; // 20%
        public TimeSpan RefreshLeadAbsoluteMin { get; set; } = TimeSpan.FromMilliseconds(250);
        public int BroadcastThreshold { get; set; } = 8;
        public int MaxComponentsPerBucket { get; set; } = 16;
        public TimeSpan MinInterBucketGap { get; set; } = TimeSpan.FromMilliseconds(50);
        public TimeSpan MinComponentGap { get; set; } = TimeSpan.FromSeconds(5);
    }

    public sealed class TtlOptions
    {
        public TimeSpan MinTtl { get; set; } = TimeSpan.FromSeconds(1);
        public TimeSpan MaxTtl { get; set; } = TimeSpan.FromMinutes(15);
    }
}

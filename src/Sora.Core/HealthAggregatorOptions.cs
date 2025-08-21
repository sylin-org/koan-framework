namespace Sora.Core;

/// Options for the Health Aggregator. Bind from configuration path: "Sora:Health:Aggregator".
public sealed class HealthAggregatorOptions
{
    public bool Enabled { get; set; } = true;

    public SchedulerOptions Scheduler { get; set; } = new();
    public TtlOptions Ttl { get; set; } = new();
    public PolicyOptions Policy { get; set; } = new();
    public LimitsOptions Limits { get; set; } = new();

    public sealed class SchedulerOptions
    {
        public bool EnableTtlScheduling { get; set; } = true;
        public TimeSpan QuantizationWindow { get; set; } = TimeSpan.FromSeconds(2);
        public double JitterPercent { get; set; } = 0.10; // ±10%
        public TimeSpan JitterAbsoluteMin { get; set; } = TimeSpan.FromMilliseconds(100);
        public TimeSpan MinComponentGap { get; set; } = TimeSpan.FromSeconds(1);
        public int MaxComponentsPerBucket { get; set; } = 256;
        public TimeSpan MinInterBucketGap { get; set; } = TimeSpan.FromMilliseconds(100);
        public int BroadcastThreshold { get; set; } = 2;
        public double RefreshLeadPercent { get; set; } = 0.10; // schedule slightly before expiry
        public TimeSpan RefreshLeadAbsoluteMin { get; set; } = TimeSpan.FromMilliseconds(100);
    }

    public sealed class TtlOptions
    {
        public TimeSpan MinTtl { get; set; } = TimeSpan.FromSeconds(1);
        public TimeSpan MaxTtl { get; set; } = TimeSpan.FromHours(1);
    }

    public sealed class PolicyOptions
    {
        public TimeSpan SnapshotStalenessWindow { get; set; } = TimeSpan.FromSeconds(30);
        public bool TreatUnknownAsDegradedForRequired { get; set; } = true;
        public List<string> RequiredComponents { get; set; } = new();
        public List<string> OptionalComponents { get; set; } = new();
        public int DegradedComponentsThreshold { get; set; } = 1;
    }

    public sealed class LimitsOptions
    {
        public int MaxFactsBytesPerComponent { get; set; } = 4096;
        public int MaxFactsCountPerComponent { get; set; } = 32;
        public int MaxMessageLength { get; set; } = 512;
    }
}

namespace Koan.Core.Observability;

public sealed class ObservabilityOptions
{
    public bool Enabled { get; set; } = true; // dev default; sampler will turn off in prod if not configured
    public TracesOptions Traces { get; set; } = new();
    public MetricsOptions Metrics { get; set; } = new();
    public OtlpOptions Otlp { get; set; } = new();

    public sealed class TracesOptions
    {
        public bool Enabled { get; set; } = true;
        public double SampleRate { get; set; } = 0.1; // 10% dev default
    }
    public sealed class MetricsOptions
    {
        public bool Enabled { get; set; } = true;
    }
    public sealed class OtlpOptions
    {
        public string? Endpoint { get; set; }
        public string? Headers { get; set; }
    }
}

namespace Koan.Observability.Infrastructure;

internal static class Constants
{
    public const string KoanActivitySources = "Koan.*";
    public const string KoanMeters = "Koan.*";

    public static class Configuration
    {
        private const string Section = "Koan:Observability";

        public const string Enabled = Section + ":Enabled";
        public const string TracesEnabled = Section + ":Traces:Enabled";
        public const string TraceSampleRate = Section + ":Traces:SampleRate";
        public const string MetricsEnabled = Section + ":Metrics:Enabled";
        public const string OtlpEndpoint = Section + ":Otlp:Endpoint";
        public const string OtlpHeaders = Section + ":Otlp:Headers";
    }

    public static class Diagnostics
    {
        public const string CapabilityCode = "observability";
        public const string CapabilitySubject = "OpenTelemetry pipeline";
        public const string CapabilityReason = "observability-reference-intent";
    }
}

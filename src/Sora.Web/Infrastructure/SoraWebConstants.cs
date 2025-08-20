namespace Sora.Web.Infrastructure;

/// <summary>
/// Centralized constants for Sora.Web to avoid magic strings and numbers across the codebase.
/// </summary>
public static class SoraWebConstants
{
    public static class Headers
    {
        public const string XContentTypeOptions = "X-Content-Type-Options";
        public const string XFrameOptions = "X-Frame-Options";
        public const string ReferrerPolicy = "Referrer-Policy";
        public const string ContentSecurityPolicy = "Content-Security-Policy";
        public const string XXssProtection = "X-XSS-Protection"; // deprecated; remove if present
        public const string SoraTraceId = "Sora-Trace-Id";
    }

    public static class Policies
    {
        public const string NoSniff = "nosniff";
        public const string Deny = "DENY";
        public const string NoReferrer = "no-referrer";
        public const string NoStore = "no-store";
    }

    public static class Routes
    {
        // Health
        public const string HealthBase = "health";
        public const string HealthLive = "live";    // relative to HealthBase
        public const string HealthReady = "ready";  // relative to HealthBase
        public const string ApiHealth = "/api/health"; // legacy absolute alias

        // Well-known
        public const string WellKnownBase = ".well-known/sora";
        public const string WellKnownObservability = "observability"; // relative to WellKnownBase
        public const string WellKnownAggregates = "aggregates";       // relative to WellKnownBase
    }

    public static class Defaults
    {
        // Pagination defaults
        public const int DefaultPageSize = 50;
        public const int MaxPageSize = 200;
    }
}

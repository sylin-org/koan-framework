namespace Koan.Web.Infrastructure;

/// <summary>
/// Centralized constants for Koan.Web to avoid magic strings and numbers across the codebase.
/// </summary>
public static class KoanWebConstants
{
    public static class Codes
    {
        public static class Moderation
        {
            // Stable error codes for moderation flows
            public const string NotFound = "moderation.notFound";
            public const string ReasonRequired = "moderation.reasonRequired";
        }
    }

    public static class Headers
    {
        public const string XContentTypeOptions = "X-Content-Type-Options";
        public const string XFrameOptions = "X-Frame-Options";
        public const string ReferrerPolicy = "Referrer-Policy";
        public const string ContentSecurityPolicy = "Content-Security-Policy";
        public const string XXssProtection = "X-XSS-Protection"; // deprecated; remove if present
        public const string KoanTraceId = "Koan-Trace-Id";
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
        public const string WellKnownBase = ".well-known/Koan";
        public const string WellKnownObservability = "observability"; // relative to WellKnownBase
        public const string WellKnownAggregates = "aggregates";       // relative to WellKnownBase
        public const string WellKnownScheduling = "scheduling";       // relative to WellKnownBase
    }

    public static class Defaults
    {
        // Pagination defaults
        public const int DefaultPageSize = 50;
        public const int MaxPageSize = 200;
    }

    public static class Sets
    {
        // Well-known, stable set names for cross-cutting capabilities
        public const string Deleted = "deleted";

        public static class Moderation
        {
            public const string Draft = "moderation.draft";
            public const string Submitted = "moderation.submitted";
            public const string Approved = "moderation.approved";
            public const string Denied = "moderation.denied";
            public const string Audit = "moderation.audit"; // optional
        }

        public const string Audit = "audit"; // global audit snapshots (when enabled)
    }
}

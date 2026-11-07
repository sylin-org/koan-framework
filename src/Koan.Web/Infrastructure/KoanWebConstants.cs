namespace Koan.Web.Infrastructure;

/// <summary>
/// Centralized constants for Koan.Web to avoid magic strings and numbers across the codebase.
/// </summary>
public static class KoanWebConstants
{
    public static class ContentTypes
    {
        public const string ApplicationJson = "application/json";
        public const string ApplicationJsonPatch = "application/json-patch+json";
        public const string ApplicationMergePatch = "application/merge-patch+json";
    }

    public static class Codes
    {
        public static class FieldAccess
        {
            public const string Denied = "web.fieldAccess.denied";
            public const string Unsupported = "web.fieldAccess.unsupported";
        }
        public static class Read
        {
            public const string EvidenceChanged = "web.read.evidenceChanged";
            public const string ProjectionFailed = "web.read.projectionFailed";
            /// <summary>WEB-0074: a request carried <c>q</c> and the endpoint did not declare it a provider query.</summary>
            public const string FreeTextUnsupported = "web.read.freeTextUnsupported";
            /// <summary>WEB-0074: <c>q</c> is a provider query on this endpoint and cannot compose with a filter.</summary>
            public const string FreeTextNotComposable = "web.read.freeTextNotComposable";
        }
        public static class Mutation
        {
            public const string InsertUnsupported = "web.mutation.insertUnsupported";
            public const string BulkCreateUnsupported = "web.mutation.bulkCreateUnsupported";
            /// <summary>WEB-0086: the word of a create whose id another row took first.</summary>
            public const string IdTaken = "id-taken";
        }
        public static class Patch
        {
            public const string IdMismatch = "web.patch.idMismatch";
        }
        public static class Put
        {
            public const string IdMismatch = "web.put.idMismatch";
        }
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

        // ARCH-0144: what the header rule of a request made in process names beyond ASP.NET's own constants.
        public const string Forwarded = "Forwarded";
        public const string ProxyConnection = "Proxy-Connection";
        public const string ContentPrefix = "Content-";
    }

    public static class Policies
    {
        public const string NoSniff = "nosniff";
        public const string Deny = "DENY";
        public const string NoReferrer = "no-referrer";
        public const string NoStore = "no-store";
    }

    public static class Authorization
    {
        // SEC-0020: the runtime fact stating what an entity or verb nothing declares resolves to.
        public const string DefaultDecisionFactCode = "koan.web.authorization.default";
        public const string DefaultDecisionFactReason = "authorize-default-decision";

        // ARCH-0092 / SEC-0012: [Authorize(Policy = "scope:<x>")] on an entity lowers to the scope grant <x>.
        public const string ScopePolicyPrefix = "scope:";
    }

    public static class BaseUrl
    {
        // WEB-0078: the runtime fact stating which base URL the host serves under and derives absolute URLs from.
        public const string FactCode = "koan.web.baseurl";
        public const string FactSubject = "web:baseurl";
        public const string ConfiguredFactReason = "base-url-configured";
        // WEB-0081: the base URL is the one the application accepted and keeps in its IApplicationUrlStore.
        public const string AcceptedFactReason = "base-url-accepted";
        public const string DerivedFactReason = "base-url-derived-from-request";
    }

    public static class KeyRing
    {
        // WEB-0079: the runtime fact stating where the Data Protection key ring lives.
        public const string FactCode = "koan.web.keyring";
        public const string FactSubject = "web:keyring";
        public const string PersistedFactReason = "key-ring-persisted";
        public const string DefaultFactReason = "key-ring-default";
        public const string ApplicationFactReason = "key-ring-application";

        // The file in the key ring folder that remembers the application name its keys are scoped to; not *.xml, so
        // the file-system repository never reads it as a key.
        public const string ApplicationNameFile = "koan-application-name.txt";

        // The start's write probe: a file name unique to the attempt follows the prefix.
        public const string ProbePrefix = ".koan-keyring-probe-";

        // ASP.NET Data Protection's payload header (big-endian): the 128-bit id of the key that protected the payload
        // follows it.
        public const uint PayloadMagicHeader = 0x09F0C9F0;
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
        public const string WellKnownFacts = "facts";                 // relative to WellKnownBase
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

    public static class Query
    {
        // General-purpose null policy override for PATCH endpoints
        // For merge-patch: values = "default" | "reject"
        // For partial-json: values = "null" | "ignore" | "reject"
        public const string Nulls = "nulls";

        // Specific overrides (take precedence when provided)
        public const string MergeNulls = "mergeNulls";     // values: "default" | "reject"
        public const string PartialNulls = "partialNulls"; // values: "null" | "ignore" | "reject"

        // Common set/partition selector already used across endpoints
        public const string Set = "set";

        // DATA-0170: a declared collection continues from a sealed key, a page at a time.
        public const string After = "after";
        public const string Size = "size";
    }

    /// <summary>DATA-0170: what seals a declared collection's key, one purpose per list.</summary>
    public static class Keyset
    {
        public const string Purpose = "Koan.Web.Keyset";
    }
}

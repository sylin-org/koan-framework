namespace Koan.Data.Core.Infrastructure;

public static class Constants
{
    public static class Diagnostics
    {
        public static class Codes
        {
            public const string AdapterSelected = "koan.data.adapter.selected";
            public const string AdapterRejected = "koan.data.adapter.rejected";
            public const string RelationshipExecution = "koan.data.relationship.execution";
            public const string StreamExecution = "koan.data.stream.execution";
            public const string LifecycleSelected = "koan.data.lifecycle.selected";
        }

        public static class Reasons
        {
            public const string ContextSource = "context-source";
            public const string DatabaseAxis = "database-axis";
            public const string ContextAdapter = "context-adapter";
            public const string EntityAttribute = "entity-attribute";
            public const string DefaultSource = "default-source";
            public const string ReferencePriority = "reference-priority";
            public const string NoFactory = "no-factory";
            public const string NativeFilter = "native-filter";
            public const string InMemoryFilter = "in-memory-filter";
            public const string BoundedScan = "bounded-scan";
            public const string BoundedFallback = "bounded-fallback";
            public const string MissingExecutionProfile = "missing-execution-profile";
            public const string AdapterUnavailable = "adapter-unavailable";
            public const string UnboundedScan = "unbounded-scan";
            public const string FallbackLimit = "fallback-candidate-limit";
            public const string ResultLimit = "relationship-result-limit";
            public const string ProviderBoundedPaging = "provider-bounded-paging";
            public const string MissingProviderBoundedPaging = "missing-provider-bounded-paging";
            public const string InvalidStreamBatchSize = "invalid-stream-batch-size";
            public const string UnsupportedStreamSort = "unsupported-stream-sort";
            public const string PaginationNotHandled = "provider-pagination-not-handled";
            public const string StreamSortNotHandled = "provider-stream-sort-not-handled";
            public const string StreamPageLimitExceeded = "provider-stream-page-limit-exceeded";
        }
    }

    public static class Defaults
    {
        // Default page size used by facade loops when materializing "All"/"QueryAll" across providers.
        // Keep conservative to balance throughput and memory. Adapters no longer clamp to their own
        // MaxPageSize (that cap was removed); request-time output-layer policy is the right boundary.
        public const int UnboundedLoopPageSize = 1000;
    }
    public static class Configuration
    {
        public static class Direct
        {
            public const string Section = "Koan:Data:Direct";
        }
    }
}

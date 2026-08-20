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
            public const string SourcePlanSelected = "koan.data.source.plan.selected";
            public const string SourceClaimsSelected = "koan.data.source.claims.selected";
            public const string DefaultRouteSelected = "koan.data.route.default.selected";
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
            public const string MaterializedBulkRead = "materialized-bulk-read";
            public const string InvalidStreamBatchSize = "invalid-stream-batch-size";
            public const string UnsupportedStreamSort = "unsupported-stream-sort";
            public const string PaginationNotHandled = "provider-pagination-not-handled";
            public const string StreamSortNotHandled = "provider-stream-sort-not-handled";
            public const string StreamOrderIsProviderDefined = "stream-order-is-provider-defined";
            public const string StreamPageLimitExceeded = "provider-stream-page-limit-exceeded";
            public const string InvalidStreamReceipt = "provider-stream-receipt-invalid";
            public const string UnsupportedRegisteredOperation = "registered-operation-unsupported";
        }
    }

    public static class Defaults
    {
        public const int RelationshipBatchSize = 100;
        public const int SourceMaxRecords = 1_000;
        public const long SourceMaxBytes = 16 * 1024 * 1024;
        public const long SourceMaxValueBytes = 4 * 1024 * 1024;
        public const int SourceMaxDurationSeconds = 30;
        public const int SourceParameterPlanCacheEntries = 1_024;
        public const int MappingPlanEntries = 256;
        public const int DiagnosticSourceEntries = 256;
        public const int NativeEvidenceEntries = 256;
        public const int StorageNameCacheEntries = 4_096;
        public const int EntityTypesPerRoot = 1_024;
        public const int SourceEntries = 256;
        public const int SourcePlanEntries = 1_024;
        public const int RepositoryEntries = 1_024;
        public const int VariantRepositoryEntries = 1_024;
        public const int DoctorTimeoutSeconds = 10;
        public const int DataRouteDrainTimeoutSeconds = 30;
        public const int DataRouteTrackedRoutes = 256;
        public const string DataRouteStateDirectory = ".Koan";
        public const string DataRouteStateSubdirectory = "data";
        public const string DataRouteStateFile = "active-route.json";
        public const int DataRouteStateSchema = 1;
        public const string SourceContinuationPrefix = "koan-source-v1.";

        // Default page size used by facade loops when materializing "All"/"QueryAll" across providers.
        // Keep conservative to balance throughput and memory. Adapters no longer clamp to their own
        // MaxPageSize (that cap was removed); request-time output-layer policy is the right boundary.
        public const int UnboundedLoopPageSize = 1000;
    }
    public static class Configuration
    {
        public const string SourceIntegration = "Koan:Data:SourceIntegration";
        public const string Mapping = "Koan:Data:Mapping";
        public const string Route = "Koan:Data:Route";

        public static class Direct
        {
            public const string Section = "Koan:Data:Direct";
        }
    }
}

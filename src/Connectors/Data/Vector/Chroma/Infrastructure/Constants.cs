namespace Koan.Data.Vector.Connector.Chroma.Infrastructure;

internal static class Constants
{
    internal static class Provider
    {
        internal const string Name = "chroma";
        internal const int Priority = 30;
        internal static readonly string[] Aliases = ["chromadb", "chroma-vector"];
    }

    internal static class Configuration
    {
        internal const string Section = "Koan:Data:Chroma";
        internal const string Automatic = "auto";

        internal static class Keys
        {
            internal const string Endpoint = Section + ":Endpoint";
            internal const string Tenant = Section + ":Tenant";
            internal const string Database = Section + ":Database";
            internal const string ApiKey = Section + ":ApiKey";
            internal const string TimeoutSeconds = Section + ":TimeoutSeconds";
            internal const string MaxMetadataBytesPerPoint = Section + ":MaxMetadataBytesPerPoint";
            internal const string MaxBatchPoints = Section + ":MaxBatchPoints";
            internal const string MaxSearchCandidates = Section + ":MaxSearchCandidates";
            internal const string MaxResponseBytes = Section + ":MaxResponseBytes";
            internal const string DisableAutoDetection = Section + ":DisableAutoDetection";
            internal const string LegacyConnectionString = Section + ":ConnectionString";
        }
    }

    internal static class Defaults
    {
        internal const string Endpoint = "http://localhost:8000";
        internal const int Port = 8000;
        internal const string Tenant = "default_tenant";
        internal const string Database = "default_database";
        internal const int TimeoutSeconds = 30;
        internal const int MaxMetadataBytesPerPoint = 1 * 1024 * 1024;
        internal const int MaxBatchPoints = 1_024;
        internal const int MaxSearchCandidates = 10_000;
        internal const int MaxResponseBytes = 64 * 1024 * 1024;
        internal const int MaxAttempts = 3;
        internal const int RetryDelayMilliseconds = 50;
    }

    internal static class Wire
    {
        internal const string Id = "__koan_id";
        internal const string Metadata = "__koan_metadata";
        internal const string Index = "__koan_index";
        internal const string Scope = "__koan_scope";
        internal const string CollectionSpace = "koan_space";
        internal const string CollectionModel = "koan_model";
    }

    internal const string HttpClientName = "chroma";
    internal const string ApiBase = "api/v2";
    internal const string HeartbeatPath = "/api/v2/heartbeat";
    internal const string HealthLog = "data.chroma.health";

    // Deterministic scope-fold namespace for scoped storage ids (adapter-owned; stable across runs).
    internal static readonly Guid ScopeIdNamespace = new("6d4a2c8e-9b31-4f57-a2d6-0e8c5b7a1f93");
}

namespace Koan.Data.Vector.Connector.Milvus.Infrastructure;

internal static class Constants
{
    internal static class Provider
    {
        internal const string Name = "milvus";
        internal const int Priority = 30;
        internal static readonly string[] Aliases = ["milvus-db", "milvus-vector"];
    }

    internal static class Configuration
    {
        internal const string Section = "Koan:Data:Milvus";
        internal const string Automatic = "auto";

        internal static class Keys
        {
            internal const string Endpoint = Section + ":Endpoint";
            internal const string Database = Section + ":Database";
            internal const string Token = Section + ":Token";
            internal const string TimeoutSeconds = Section + ":TimeoutSeconds";
            internal const string VisibilityTimeoutSeconds = Section + ":VisibilityTimeoutSeconds";
            internal const string MaxMetadataBytesPerPoint = Section + ":MaxMetadataBytesPerPoint";
            internal const string MaxBatchPoints = Section + ":MaxBatchPoints";
            internal const string MaxClearPoints = Section + ":MaxClearPoints";
            internal const string MaxSearchCandidates = Section + ":MaxSearchCandidates";
            internal const string MaxResponseBytes = Section + ":MaxResponseBytes";
            internal const string DisableAutoDetection = Section + ":DisableAutoDetection";
            internal const string LegacyConnectionString = Section + ":ConnectionString";
        }
    }

    internal static class Defaults
    {
        internal const string Endpoint = "http://localhost:19530";
        internal const string Database = "default";
        internal const int Port = 19530;
        internal const int TimeoutSeconds = 30;
        internal const int VisibilityTimeoutSeconds = 60;
        internal const int MaxMetadataBytesPerPoint = 1 * 1024 * 1024;
        internal const int MaxBatchPoints = 1_024;
        internal const int MaxClearPoints = 10_000;
        internal const int MaxSearchCandidates = 16_384;
        internal const int MaxResponseBytes = 64 * 1024 * 1024;
        internal const int MaxAttempts = 3;
        internal const int RetryDelayMilliseconds = 50;
        internal const int VisibilityPollMilliseconds = 50;
        internal const int PrimaryKeyLength = 512;
        internal const int HnswM = 16;
        internal const int HnswEfConstruction = 200;
    }

    internal static class Wire
    {
        internal const string Id = "koan_id";
        internal const string LogicalId = "koan_logical_id";
        internal const string Vector = "koan_vector";
        internal const string Metadata = "koan_metadata";
        internal const string Index = "koan_vector_hnsw";
        internal const string ContractPrefix = "koan_contract_v1_";
    }

    internal const string HttpClientName = "milvus";
    internal const string HealthLog = "data.milvus.health";
}

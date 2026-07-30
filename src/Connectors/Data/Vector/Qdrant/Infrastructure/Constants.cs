namespace Koan.Data.Vector.Connector.Qdrant.Infrastructure;

internal static class Constants
{
    internal static class Provider
    {
        internal const string Name = "qdrant";
        internal const int Priority = 30;
        internal static readonly string[] Aliases = ["qdrant-db", "qdrant-vector"];
    }

    internal static class Configuration
    {
        internal const string Section = "Koan:Data:Qdrant";
        internal const string Automatic = "auto";

        internal static class Keys
        {
            internal const string Endpoint = Section + ":Endpoint";
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
        internal const string Endpoint = "http://localhost:6333";
        internal const int Port = 6333;
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
        internal const string Norm = "__koan_norm";
        internal const string CollectionSpace = "koan_space";
        internal const string CollectionModel = "koan_model";
    }

    internal const string HttpClientName = "qdrant";
    internal const string ReadyPath = "/readyz";
    internal const string HealthLog = "data.qdrant.health";

    // Observable identity for unscoped arbitrary-string keys written by earlier adapter versions.
    internal static readonly Guid StringIdNamespace = new("3b8c4e6a-1c2f-4d8b-9a5e-7c3f1d2b8e4a");
}

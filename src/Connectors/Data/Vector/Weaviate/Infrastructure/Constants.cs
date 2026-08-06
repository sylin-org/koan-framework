namespace Koan.Data.Vector.Connector.Weaviate.Infrastructure;

internal static class Constants
{
    internal static class Provider
    {
        internal const string Name = "weaviate";
        internal const int Priority = 25;
        internal static readonly string[] Aliases = ["weaviate-db", "weaviate-vector"];
    }

    internal static class Configuration
    {
        internal const string Section = "Koan:Data:Weaviate";
        internal const string Automatic = "auto";

        internal static class Keys
        {
            internal const string Endpoint = Section + ":Endpoint";
            internal const string ApiKey = Section + ":ApiKey";
            internal const string TimeoutSeconds = Section + ":TimeoutSeconds";
            internal const string MaxMetadataBytesPerPoint = Section + ":MaxMetadataBytesPerPoint";
            internal const string MaxBatchPoints = Section + ":MaxBatchPoints";
            internal const string MaxClearPoints = Section + ":MaxClearPoints";
            internal const string MaxSearchCandidates = Section + ":MaxSearchCandidates";
            internal const string MaxResponseBytes = Section + ":MaxResponseBytes";
            internal const string VisibilityTimeoutSeconds = Section + ":VisibilityTimeoutSeconds";
            internal const string DisableAutoDetection = Section + ":DisableAutoDetection";
            internal const string LegacyConnectionString = Section + ":ConnectionString";
        }
    }

    internal static class Defaults
    {
        internal const string Endpoint = "http://localhost:8080";
        internal const int Port = 8080;
        internal const int TimeoutSeconds = 30;
        internal const int VisibilityTimeoutSeconds = 30;
        internal const int MaxMetadataBytesPerPoint = 1 * 1024 * 1024;
        internal const int MaxBatchPoints = 1_024;
        // One extra result is reserved for the mutation-free overflow proof under Weaviate's 10,000 query ceiling.
        internal const int MaxClearPoints = 9_999;
        internal const int MaxSearchCandidates = 10_000;
        internal const int MaxResponseBytes = 64 * 1024 * 1024;
        internal const int MaxAttempts = 3;
        internal const int RetryDelayMilliseconds = 50;
        internal const int VisibilityPollMilliseconds = 25;
    }

    internal static class Wire
    {
        internal const string Id = "koanId";
        internal const string Metadata = "koanMetadata";
        internal const string Terms = "koanTerms";
        internal const string ContractPrefix = "koan-vector-v1:";
    }

    internal const string HttpClientName = "weaviate";
    internal const string ReadyPath = "/v1/.well-known/ready";
    internal const string HealthLog = "data.weaviate.health";
    internal static readonly Guid PointNamespace = new("67591f20-4d57-54ec-b218-7cf6c75c0f4a");
}

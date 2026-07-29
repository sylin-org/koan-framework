namespace Koan.Data.Connector.ElasticSearch.Infrastructure;

internal static class Constants
{
    internal static class Provider
    {
        internal const string Name = "elasticsearch";
        internal const int Priority = 20;
        internal static readonly string[] Aliases = ["elastic"];
    }

    internal static class Configuration
    {
        internal const string Section = "Koan:Data:ElasticSearch";
        internal const string Automatic = "auto";

        internal static class Keys
        {
            internal const string Endpoint = Section + ":Endpoint";
            internal const string ApiKey = Section + ":ApiKey";
            internal const string Username = Section + ":Username";
            internal const string Password = Section + ":Password";
            internal const string TimeoutSeconds = Section + ":TimeoutSeconds";
            internal const string MaxMetadataBytesPerPoint = Section + ":MaxMetadataBytesPerPoint";
            internal const string MaxBatchPoints = Section + ":MaxBatchPoints";
            internal const string MaxRequestBytes = Section + ":MaxRequestBytes";
            internal const string MaxSearchCandidates = Section + ":MaxSearchCandidates";
            internal const string MaxResponseBytes = Section + ":MaxResponseBytes";
            internal const string DisableAutoDetection = Section + ":DisableAutoDetection";
            internal const string LegacyConnectionString = Section + ":ConnectionString";
        }
    }

    internal static class Defaults
    {
        internal const string Endpoint = "http://localhost:9200";
        internal const int TimeoutSeconds = 30;
        internal const int MaxMetadataBytesPerPoint = 1 * 1024 * 1024;
        internal const int MaxBatchPoints = 1_024;
        internal const int MaxRequestBytes = 16 * 1024 * 1024;
        internal const int MaxSearchCandidates = 10_000;
        internal const int MaxResponseBytes = 64 * 1024 * 1024;
        internal const int MaxAttempts = 3;
        internal const int RetryDelayMilliseconds = 50;
    }

    internal static class Wire
    {
        internal const string Contract = "koan_vector_contract";
        internal const string ContractVersion = "1";
        internal const string Space = "koan_space";
        internal const string Model = "koan_model";
        internal const string Metric = "koan_metric";
        internal const string Id = "__koan_id";
        internal const string Scope = "__koan_scope";
        internal const string Vector = "__koan_vector";
        internal const string Metadata = "__koan_metadata";
        internal const string Index = "__koan_index";
        internal const string Values = "values";
        internal const string Exists = "exists";
        internal const string Sizes = "sizes";
        internal const string Path = "path";
        internal const string Value = "value";
    }

    internal const string HttpClientName = "elasticsearch";
    internal const string HealthPath = "/_cluster/health";
    internal const string HealthLog = "data.elasticsearch.health";
}

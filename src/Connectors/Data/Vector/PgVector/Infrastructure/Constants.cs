namespace Koan.Data.Vector.Connector.PgVector.Infrastructure;

internal static class Constants
{
    internal static class Provider
    {
        internal const string Name = "pgvector";
        internal const string PairedDataProvider = "postgres";
        internal const int Priority = 35;
        internal static readonly IReadOnlyCollection<string> Aliases =
            Array.AsReadOnly(["pg-vector", "postgres-vector", "postgres", "postgresql", "npgsql"]);
        internal static readonly IReadOnlyCollection<string> DiscoveryAliases =
            Array.AsReadOnly(["pg-vector", "postgres-vector"]);
    }

    internal static class Configuration
    {
        internal const string Section = "Koan:Data:PgVector";
        internal const string Automatic = "auto";
        internal const string PairedConnectionString = "Koan:Data:Postgres:ConnectionString";
        internal const string PairedDatabase = "Koan:Data:Postgres:Database";
        internal const string PairedUsername = "Koan:Data:Postgres:Username";
        internal const string PairedPassword = "Koan:Data:Postgres:Password";
        internal const string PairedSearchPath = "Koan:Data:Postgres:SearchPath";

        internal static class Keys
        {
            internal const string ConnectionString = Section + ":ConnectionString";
            internal const string CommandTimeoutSeconds = Section + ":CommandTimeoutSeconds";
            internal const string MaxMetadataBytesPerPoint = Section + ":MaxMetadataBytesPerPoint";
            internal const string MaxBatchPoints = Section + ":MaxBatchPoints";
            internal const string MaxSearchCandidates = Section + ":MaxSearchCandidates";
            internal const string DisableAutoDetection = Section + ":DisableAutoDetection";
        }
    }

    internal static class Defaults
    {
        internal const int Port = 5432;
        internal const int CommandTimeoutSeconds = 30;
        internal const int MaxMetadataBytesPerPoint = 1024 * 1024;
        internal const int MaxBatchPoints = 1024;
        internal const int MaxSearchCandidates = 100_000;
    }

    internal static class Schema
    {
        internal const string ShapeMarkerPrefix = "koan-vector-v1:";
        internal const string Extension = "vector";
        internal const string Embedding = "embedding";
        internal const string Metadata = "metadata";
        internal const string FilterData = "filter_data";
        internal const string Scope = "scope";
        internal const string Id = "id";
    }

    internal const string HealthLog = "data.pgvector.health";
}

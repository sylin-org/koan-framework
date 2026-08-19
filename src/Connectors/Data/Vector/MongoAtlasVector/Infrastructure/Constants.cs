namespace Koan.Data.Vector.Connector.MongoAtlasVector.Infrastructure;

internal static class Constants
{
    internal static class Provider
    {
        internal const string Name = "mongo-atlas-vector";
        internal const string PairedDataProvider = "mongo";
        internal const string PairedMongo = PairedDataProvider;
        internal const int Priority = 35;
        internal const int MaximumRoutes = Defaults.MaximumRoutes;
        internal static readonly IReadOnlyCollection<string> Aliases =
            Array.AsReadOnly(["mongo-atlas", "atlas-vector", "mongo", "mongodb"]);
    }

    internal static class Configuration
    {
        internal const string Section = "Koan:Data:MongoAtlasVector";
        internal const string Automatic = "auto";
        internal const string ZenGardenPrefix = "zen-garden://";
        internal const string PairedConnectionString = "Koan:Data:Mongo:ConnectionString";
        internal const string PairedStandardConnectionString = "ConnectionStrings:Mongo";
        internal const string DisableMongoAutoDetection = "Koan:Data:Mongo:DisableAutoDetection";

        internal static class Keys
        {
            internal const string ConnectionString = Section + ":ConnectionString";
            internal const string Database = Section + ":Database";
            internal const string DisableAutoDetection = Section + ":DisableAutoDetection";
            internal const string CommandTimeoutSeconds = Section + ":CommandTimeoutSeconds";
            internal const string MaxMetadataBytesPerPoint = Section + ":MaxMetadataBytesPerPoint";
            internal const string MaxBatchPoints = Section + ":MaxBatchPoints";
            internal const string MaxSearchCandidates = Section + ":MaxSearchCandidates";
            internal const string IndexReadyTimeoutSeconds = Section + ":IndexReadyTimeoutSeconds";
            internal const string MutationVisibilityTimeoutSeconds = Section + ":MutationVisibilityTimeoutSeconds";
            internal const string VisibilityPollMilliseconds = Section + ":VisibilityPollMilliseconds";
        }
    }

    internal static class Defaults
    {
        internal const string Database = "KoanVectors";
        internal const int MaxMetadataBytesPerPoint = 1024 * 1024;
        internal const int CommandTimeoutSeconds = 30;
        internal const int MaxBatchPoints = 1024;
        internal const int MaxSearchCandidates = 100_000;
        internal const int IndexReadyTimeoutSeconds = 120;
        internal const int MutationVisibilityTimeoutSeconds = 15;
        internal const int VisibilityPollMilliseconds = 10;
        internal const int MaximumRoutes = 128;
    }

    internal static class Wire
    {
        internal const string Index = "koan_vector";
        internal const string ShapeId = "__koan_shape";
        internal const string Embedding = "__koan_embedding";
        internal const string Metadata = "__koan_metadata";
        internal const string Id = "__koan_id";
        internal const string Key = "__koan_key";
        internal const string Scope = "__koan_scope";
        internal const string Generation = "__koan_generation";
        internal const string Scalar = "__koan_scalar";
        internal const string Elements = "__koan_elements";
        internal const string Present = "__koan_present";
        internal const string Numeric = "__koan_numeric";
        internal const string Size = "__koan_size";
        internal const string Score = "__koan_score";
        internal const string HealthCollection = "__koan_vector_health_probe__";
    }

    internal const string HealthLog = "data.mongo-atlas-vector.health";
}

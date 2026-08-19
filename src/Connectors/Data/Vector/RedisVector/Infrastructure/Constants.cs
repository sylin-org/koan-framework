namespace Koan.Data.Vector.Connector.RedisVector.Infrastructure;

internal static class Constants
{
    internal static class Provider
    {
        internal const string Name = "redis-vector";
        internal const string PairedRedis = "redis";
        internal const int Priority = 35;
        internal static readonly IReadOnlyCollection<string> Aliases = Array.AsReadOnly([PairedRedis]);
    }

    internal static class Configuration
    {
        internal const string Section = "Koan:Data:RedisVector";

        internal static string SourceDatabase(string source) =>
            $"Koan:Data:Sources:{source}:{Provider.Name}:Database";
    }

    internal static class Defaults
    {
        internal const int Database = 0;
        internal const int MaxMetadataBytesPerPoint = 1024 * 1024;
        internal const int MaxBatchPoints = 1024;
        internal const int MaxSearchCandidates = 10_000;
        internal const int MaxIndexedPaths = 256;
    }

    internal static class Wire
    {
        internal const string IndexPrefix = "koan_vector_";
        internal const string KeyPrefixStart = "koan:v:{";
        internal const string KeyPrefixEnd = "}:point:";
        internal const string MarkerPrefix = "koan:v:shape:";
        internal const string MarkerVersion = "koan-redis-vector-v1";
        internal const string HealthProbeIndex = "__koan_vector_health_probe__";
        internal const string Id = "__koan_id";
        internal const string Key = "__koan_key";
        internal const string Scope = "__koan_scope";
        internal const string Embedding = "__koan_embedding";
        internal const string Metadata = "__koan_metadata";
        internal const string Present = "__koan_present";
        internal const string Scalar = "__koan_scalar";
        internal const string Elements = "__koan_elements";
        internal const string Unordered = "__koan_unordered";
        internal const string Distance = "__koan_distance";
        internal const string TagSeparator = "|";
        internal const char TagSeparatorCharacter = '|';
        internal const string NumberPrefix = "n_";
        internal const string SizePrefix = "z_";
    }

    internal static class Commands
    {
        internal const string Create = "FT.CREATE";
        internal const string Alter = "FT.ALTER";
        internal const string Info = "FT.INFO";
        internal const string Search = "FT.SEARCH";
    }

    internal static class Scripts
    {
        internal const string ReplaceHash =
            "local existed=redis.call('EXISTS',KEYS[1]);redis.call('DEL',KEYS[1]);redis.call('HSET',KEYS[1],unpack(ARGV));return existed";
    }

    internal const string HealthLog = "data.redis-vector.health";
}

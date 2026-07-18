namespace Koan.Redis.Infrastructure;

internal static class Constants
{
    internal static class Configuration
    {
        internal const string Section = "Koan:Redis";
        internal const string ConnectionString = Section + ":ConnectionString";
        internal const string DisableAutoDetection = Section + ":DisableAutoDetection";
        internal const string StandardConnectionString = "ConnectionStrings:Redis";
    }

    internal static class Discovery
    {
        internal const string ServiceName = "redis";
        internal const string DefaultLocal = "localhost:6379";
        internal const string DefaultContainer = "redis:6379";
        internal const string RedisUrl = "REDIS_URL";
        internal const string RedisConnectionString = "REDIS_CONNECTION_STRING";
    }

    internal static class Logging
    {
        internal const string Connection = "redis.connection";
    }
}

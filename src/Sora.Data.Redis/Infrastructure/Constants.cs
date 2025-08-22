namespace Sora.Data.Redis.Infrastructure;

public static class Constants
{
    public static class Configuration
    {
        public const string Section_Data = "Sora:Data:Redis";
        public const string Section_Sources_Default = "Sora:Data:Sources:Default:redis";
        public static class Keys
        {
            public const string ConnectionString = nameof(ConnectionString);
            public const string Database = nameof(Database);
            public const string DefaultPageSize = nameof(DefaultPageSize);
            public const string MaxPageSize = nameof(MaxPageSize);
        }
    }

    public static class Bootstrap
    {
        public const string DefaultPageSize = "data.redis.defaultPageSize";
        public const string MaxPageSize = "data.redis.maxPageSize";
        public const string EnsureCreatedSupported = "EnsureCreatedSupported";
    }

    public static class Discovery
    {
        public const string DefaultLocal = "localhost:6379";
        public const string DefaultCompose = "redis:6379";
        public const string EnvRedisUrl = "REDIS_URL";
        public const string EnvRedisConnectionString = "REDIS_CONNECTION_STRING";
    public const string EnvRedisList = "SORA_DATA_REDIS_URLS"; // comma/semicolon-separated list of endpoints
    }
}

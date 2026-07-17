namespace Koan.Data.Connector.Redis.Infrastructure;

public static class Constants
{
    public const string Section = "Koan:Data:Redis";

    public static class Configuration
    {
        public const string Section_Data = Constants.Section;
        public const string Section_Sources_Default = "Koan:Data:Sources:Default:redis";
        public const string AltConnectionString = "Koan:Data:ConnectionString";
        public static class Keys
        {
            public const string ConnectionString = Constants.Section + ":ConnectionString";
            public const string Database = Constants.Section + ":Database";
            public const string AltDatabase = "Koan:Data:Database";
            public const string Password = Constants.Section + ":Password";
            public const string AltPassword = "Koan:Data:Password";
            public const string DefaultPageSize = Constants.Section + ":DefaultPageSize";
            public const string AltDefaultPageSize = "Koan:Data:DefaultPageSize";
            public const string DisableAutoDetection = Constants.Section + ":DisableAutoDetection";
            public const string EnsureCreatedSupported = nameof(EnsureCreatedSupported);
        }
    }

    public static class Bootstrap
    {
        public const string DefaultPageSize = "data.redis.defaultPageSize";
        public const string EnsureCreatedSupported = "EnsureCreatedSupported";
    }

    public static class Discovery
    {
        public const string DefaultLocal = "localhost:6379";
        public const string DefaultCompose = "redis:6379";
        public const string EnvRedisUrl = "REDIS_URL";
        public const string EnvRedisConnectionString = "REDIS_CONNECTION_STRING";
        public const string EnvRedisList = "Koan_DATA_REDIS_URLS"; // comma/semicolon-separated list
        public const string WellKnownServiceName = "redis";
        public const string HostDocker = "host.docker.internal";
        public const string Localhost = "localhost";
    }

    internal static class Logging
    {
        public const string Connection = "data.redis.connection";
    }
}


namespace Koan.Data.Vector.Connector.Weaviate.Infrastructure;

internal static class Constants
{
    public static class Configuration
    {
        public const string Section = "Koan:Data:Weaviate";

        public static class Keys
        {
            public const string ConnectionString = Section + ":ConnectionString";
            public const string Endpoint = Section + ":Endpoint";
            public const string ApiKey = Section + ":ApiKey";
            public const string Metric = Section + ":Metric";
            public const string TimeoutSeconds = Section + ":TimeoutSeconds";
        }

        public static class Flags
        {
            public const string DisableAutoDetection = Section + ":DisableAutoDetection";
        }

        public static class ZenGarden
        {
            public const string Section = Configuration.Section + ":ZenGarden";
            public const string Offering = Section + ":Offering";
            public const string Instance = Section + ":Instance";
            public const string Capabilities = Section + ":Capabilities";
            public const string Capability = Section + ":Capability";
        }

    }

    public static class Discovery
    {
        public const string EnvList = "Koan_DATA_WEAVIATE_URLS"; // comma/semicolon-separated

        public const int DefaultPort = 8080;
        public const string WellKnownServiceName = "weaviate";
        public const string HostDocker = "host.docker.internal";
        public const string Localhost = "localhost";
    }

    internal static class Logging
    {
        public const string Health = "data.weaviate.health";
    }

    internal const string HttpClientName = "weaviate";
    internal const string DefaultEndpoint = "http://localhost:8080";
}


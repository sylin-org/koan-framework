namespace Koan.AI.Connector.LMStudio.Infrastructure;

internal static class Constants
{
    public const string Section = "Koan:Ai:Provider:LMStudio";
    public const string ShortSection = "Koan:Ai:LMStudio";

    public static class Configuration
    {
        public const string ServicesRoot = "Koan:Ai:Services:LMStudio";

        public static class Keys
        {
            public const string ConnectionString = Section + ":ConnectionString";
            public const string AltConnectionString = "Koan:Ai:ConnectionString";
            public const string ApiKey = Section + ":ApiKey";
            public const string BaseUrl = Section + ":BaseUrl";
            public const string AltBaseUrl = ShortSection + ":BaseUrl";
            public const string DefaultModel = Section + ":DefaultModel";
            public const string AltDefaultModel = ShortSection + ":DefaultModel";
            public const string AutoDiscoveryEnabled = Section + ":AutoDiscoveryEnabled";
            public const string AltAutoDiscoveryEnabled = ShortSection + ":AutoDiscoveryEnabled";
            public const string Weight = Section + ":Weight";
            public const string Labels = Section + ":Labels";
        }
    }

    public static class Discovery
    {
        public const int DefaultPort = 1234;
        public const string ModelsPath = "/v1/models";
        public const string ChatPath = "/v1/chat/completions";
        public const string EmbeddingsPath = "/v1/embeddings";
        public const string HealthPath = "/health";
        public const string EnvBaseUrl = "LMSTUDIO_API_BASE_URL";
        public const string EnvKey = "LMSTUDIO_API_KEY";
        public const string EnvList = "Koan_AI_LMSTUDIO_URLS";
        public const string HostDocker = "host.docker.internal";
        public const string Localhost = "localhost";
        public const string Loopback = "127.0.0.1";
        public const string WellKnownServiceName = "lmstudio";
    }

    public static class Adapter
    {
        public const string Type = "lmstudio";
    }
}


namespace Koan.AI.Connector.LMStudio.Infrastructure;

internal static class Constants
{
    public const string Section = "Koan:Ai:Provider:LMStudio";

    public static class Configuration
    {
        public const string ServicesRoot = "Koan:Ai:Services:LMStudio";

        public static class Keys
        {
            public const string ConnectionString = "Koan:Ai:Provider:LMStudio:ConnectionString";
            public const string AltConnectionString = "Koan:Ai:ConnectionString";
            public const string ApiKey = "Koan:Ai:Provider:LMStudio:ApiKey";
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


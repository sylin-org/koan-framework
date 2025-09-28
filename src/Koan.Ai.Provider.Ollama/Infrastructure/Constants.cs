namespace Koan.Ai.Provider.Ollama.Infrastructure;

internal static class Constants
{
    public const string Section = "Koan:Ai:Provider:Ollama";

    public static class Configuration
    {
        public const string ServicesRoot = "Koan:Ai:Services:Ollama";

        public static class Keys
        {
            public const string ConnectionString = "Koan:Ai:Provider:Ollama:ConnectionString";
            public const string AltConnectionString = "Koan:Ai:ConnectionString";
        }
    }

    public static class Discovery
    {
        public const int DefaultPort = 11434;
        public const string TagsPath = "/api/tags";
        public const string EnvBaseUrl = "OLLAMA_BASE_URL";
        public const string EnvList = "Koan_AI_OLLAMA_URLS"; // comma/semicolon-separated list
        public const string HostDocker = "host.docker.internal";
        public const string Localhost = "localhost";
        public const string Loopback = "127.0.0.1";
        public const string WellKnownServiceName = "ollama";
    }

    public static class Api
    {
        public const string GeneratePath = "/api/generate";
        public const string EmbeddingsPath = "/api/embeddings";
    }

    public static class Adapter
    {
        public const string Type = "ollama";
    }
}

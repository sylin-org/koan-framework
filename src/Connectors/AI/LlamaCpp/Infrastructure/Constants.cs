namespace Koan.AI.Connector.LlamaCpp.Infrastructure;

internal static class Constants
{
    public const string Section = "Koan:Ai:LlamaCpp";

    public static class Configuration
    {
        public const string ConnectionString = "ConnectionStrings:LlamaCpp";
    }

    public static class Discovery
    {
        public const int DefaultPort = 8080;
        public const string HealthPath = "/health";
        public const string ModelsPath = "/v1/models";
        public const string ChatPath = "/v1/chat/completions";
        public const string EmbeddingsPath = "/v1/embeddings";
        public const string HostDocker = "host.docker.internal";
        public const string Localhost = "localhost";
        public const string Loopback = "127.0.0.1";
        public const string WellKnownServiceName = "llamacpp";
    }

    public static class Adapter
    {
        public const string Type = "llamacpp";
    }
}

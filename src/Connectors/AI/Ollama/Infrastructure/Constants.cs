namespace Koan.AI.Connector.Ollama.Infrastructure;

internal static class Constants
{
    public static class Discovery
    {
        public const int DefaultPort = 11434;
        public const string ModelsPath = "/api/tags";
        public const string HostDocker = "host.docker.internal";
        public const string Localhost = "localhost";
        public const string WellKnownServiceName = "ollama";
    }

    public static class Adapter
    {
        public const string Type = "ollama";
    }
}

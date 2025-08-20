namespace Sora.Ai.Provider.Ollama.Infrastructure;

internal static class Constants
{
    public static class Configuration
    {
        public const string ServicesRoot = "Sora:Ai:Services:Ollama";
    }

    public static class Discovery
    {
        public const int DefaultPort = 11434;
        public const string TagsPath = "/api/tags";
    }
}

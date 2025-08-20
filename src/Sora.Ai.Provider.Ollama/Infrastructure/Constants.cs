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

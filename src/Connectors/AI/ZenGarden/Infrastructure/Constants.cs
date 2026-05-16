namespace Koan.AI.Connector.ZenGarden.Infrastructure;

internal static class Constants
{
    internal static class Adapter
    {
        public const string Id = "zen-garden";
        public const string Name = "Zen Garden AI Orchestrator";
        public const string Type = "zen-garden";
    }

    internal static class Discovery
    {
        public const string WellKnownServiceName = "zen-garden-ai";
        public const string OfferingName = "zen-garden.ai.orchestrator";
    }

    internal static class Endpoints
    {
        // Ollama-compatible (proxied by orchestrator)
        public const string Chat = "/api/chat";
        public const string Generate = "/api/generate";
        public const string Embed = "/api/embed";

        // New capability endpoints (AI-0033)
        public const string Imagine = "/api/imagine";
        public const string Edit = "/api/edit";
        public const string Render = "/api/render";
        public const string Transcribe = "/api/transcribe";
        public const string Speak = "/api/speak";
        public const string Rerank = "/api/rerank";
        public const string Translate = "/api/translate";

        // Discovery
        public const string Capabilities = "/v1/capabilities";
        public const string Models = "/v1/models";
        public const string Health = "/";
    }
}

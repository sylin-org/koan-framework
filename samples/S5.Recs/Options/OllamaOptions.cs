namespace S5.Recs.Options;

public sealed class OllamaOptions
{
    public string[] BaseUrls { get; set; } = new[] {
        "http://localhost:11434",
        "http://host.docker.internal:11434",
        "http://ollama:11434"
    };
    public string Model { get; set; } = "all-minilm";
}

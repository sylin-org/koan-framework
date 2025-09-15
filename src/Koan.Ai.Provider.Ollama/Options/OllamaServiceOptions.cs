using System.ComponentModel.DataAnnotations;

namespace Koan.Ai.Provider.Ollama.Options;

public sealed class OllamaServiceOptions
{
    [Required]
    public string Id { get; set; } = string.Empty;
    [Required]
    public string BaseUrl { get; set; } = string.Empty;
    public string? DefaultModel { get; set; }
    public int? Weight { get; set; }
    public Dictionary<string, string>? Labels { get; set; }
    public bool Enabled { get; set; } = true;
}

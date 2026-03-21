using Koan.Data.AI.Attributes;
using Koan.Data.Core.Model;

namespace S18.Prism.Models;

[Embedding(
    Policy = EmbeddingPolicy.Explicit,
    Async = true,
    MaxTokens = 4096,
    Version = 1)]
public class ModelCard : Entity<ModelCard>
{
    public string HubId { get; set; } = "";
    public string Provider { get; set; } = "";
    public string? Author { get; set; }
    public string Title { get; set; } = "";
    public string Description { get; set; } = "";
    public List<string> Tags { get; set; } = [];
    public string? Task { get; set; }
    public List<string> Domains { get; set; } = [];
    public long ParameterCount { get; set; }
    public long FileSizeBytes { get; set; }
    public string? License { get; set; }
    public int Downloads { get; set; }
    public int Likes { get; set; }
    public DateTime LastModified { get; set; }
    public long EstimatedVramBytes { get; set; }
    public float[]? Embedding { get; set; }
    public DateTime CrawledAt { get; set; }

    public string ToEmbeddingText() =>
        $"{Title}\n{Description}\n{string.Join(", ", Tags)}\n{string.Join(", ", Domains)}";
}

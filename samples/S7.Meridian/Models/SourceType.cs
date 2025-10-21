using Koan.Data.Core.Model;

namespace Koan.Samples.Meridian.Models;

public sealed class SourceType : Entity<SourceType>
{
    public string Name { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public int Version { get; set; } = 1;

    public List<string> FilenamePatterns { get; set; } = new();
    public List<string> Keywords { get; set; } = new();
    public int? ExpectedPageCountMin { get; set; }
        = null;
    public int? ExpectedPageCountMax { get; set; }
        = null;
    public List<string> MimeTypes { get; set; } = new();

    public float[]? TypeEmbedding { get; set; }
        = null;
    public int TypeEmbeddingVersion { get; set; }
        = 0;
    public string? TypeEmbeddingHash { get; set; }
        = null;
    public DateTime? TypeEmbeddingComputedAt { get; set; }
        = null;

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}

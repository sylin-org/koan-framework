using Koan.Data.Core.Model;
using Koan.Data.AI.Attributes;

namespace Koan.Samples.Meridian.Models;

[Embedding(
    Properties = new[] { nameof(Text) },
    Policy = EmbeddingPolicy.Explicit,
    Async = true,
    MaxTokens = 8191,
    Version = 1)]
public sealed class Passage : Entity<Passage>
{
    public string SourceDocumentId { get; set; } = string.Empty;

    public int SequenceNumber { get; set; }
        = 0;

    public string Text { get; set; } = string.Empty;
    public string TextHash { get; set; } = string.Empty;

    public int? PageNumber { get; set; }
        = null;

    public string? Section { get; set; }
        = null;

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? IndexedAt { get; set; }
        = null;
}

using Koan.Data.Core.Model;

namespace Koan.Samples.Meridian.Models;

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

using Koan.Data.AI.Attributes;
using Koan.Media.Abstractions.Model;
using Koan.Storage.Infrastructure;

namespace S18.Prism.Models;

[StorageBinding(Profile = "standard", Container = "knowledge")]
[Embedding(
    Policy = EmbeddingPolicy.Explicit,
    Async = true,
    MaxTokens = 8191,
    Version = 1)]
public class Note : MediaEntity<Note>
{
    public string? Title { get; set; }
    public string SpaceId { get; set; } = "";
    public NoteOrigin Origin { get; set; } = NoteOrigin.Upload;

    // Content (extracted from any file format)
    public List<ContentBlock> Blocks { get; set; } = [];

    // AI enrichment
    public string? Summary { get; set; }
    public List<string> KeyConcepts { get; set; } = [];
    public string? Category { get; set; }
    public NoteAnalysis? Analysis { get; set; }

    // Source tracking
    public string? SourceId { get; set; }
    public string? SourceUrl { get; set; }
    public DateTime? SourcePublishedAt { get; set; }
    public bool AutoIngested { get; set; }
    public string? ExtractorUsed { get; set; }

    // Search
    public float[]? Embedding { get; set; }

    // Feedback
    public int? UserRating { get; set; }

    public string ToEmbeddingText()
    {
        var parts = new List<string>();
        if (Title is not null) parts.Add(Title);
        if (Summary is not null) parts.Add(Summary);
        parts.AddRange(Blocks
            .Where(b => b.Kind is ContentKind.Text or ContentKind.Table)
            .Select(b => b.Content));
        if (KeyConcepts.Count > 0) parts.Add(string.Join(", ", KeyConcepts));
        return string.Join("\n\n", parts);
    }
}

public enum NoteOrigin
{
    Upload,
    Capture,
    Source,
    Brief,
    Digest,
    Generated
}

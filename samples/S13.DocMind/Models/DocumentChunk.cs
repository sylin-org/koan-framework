using Koan.Data.Core.Model;
using Koan.Mcp;

namespace S13.DocMind.Models;

/// <summary>
/// A chunk of extracted document content enriched with embeddings.
/// </summary>
[McpEntity(Name = "document-chunks", Description = "Chunked text and diagram excerpts derived from source documents.")]
public sealed class DocumentChunk : Entity<DocumentChunk>
{
    public string DocumentId { get; set; } = string.Empty;
    public int Index { get; set; }
    public string Channel { get; set; } = DocumentChannels.Text;
    public string Content { get; set; } = string.Empty;
    public string? Summary { get; set; }
    public int TokenEstimate { get; set; }
    public float[]? Embedding { get; set; }
    public Dictionary<string, string> Metadata { get; set; } = new(StringComparer.OrdinalIgnoreCase);
}

public static class DocumentChannels
{
    public const string Text = "text";
    public const string Vision = "vision";
}

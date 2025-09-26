using Koan.Data.Core.Model;
using Koan.Mcp;

namespace S13.DocMind.Models;

/// <summary>
/// Structured insight synthesised from a document or chunk.
/// </summary>
[McpEntity(Name = "document-insights", Description = "Structured findings, summaries, and risk highlights for DocMind documents.")]
public sealed class DocumentInsight : Entity<DocumentInsight>
{
    public string DocumentId { get; set; } = string.Empty;
    public string? ChunkId { get; set; }
    public string Channel { get; set; } = DocumentChannels.Text;
    public string Title { get; set; } = string.Empty;
    public string Content { get; set; } = string.Empty;
    public double Confidence { get; set; }
    public Dictionary<string, string> Metadata { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
}

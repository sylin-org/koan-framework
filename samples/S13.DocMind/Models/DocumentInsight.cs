using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Koan.Data.Abstractions.Annotations;
using Koan.Data.Core.Model;
using Koan.Mcp;

namespace S13.DocMind.Models;

/// <summary>
/// Represents a structured insight generated during processing. Multiple channels can add insights over time.
/// Vector data is stored in SemanticTypeEmbedding/DocumentChunkEmbedding entities.
/// </summary>
[McpEntity(Name = "document-insights", Description = "Structured findings, summaries, and risk highlights for DocMind documents.")]
public sealed class DocumentInsight : Entity<DocumentInsight>
{
    [Parent(typeof(SourceDocument))]
    public Guid SourceDocumentId { get; set; }
        = Guid.Empty;

    public Guid? ChunkId { get; set; }
        = null;

    public InsightChannel Channel { get; set; }
        = InsightChannel.Text;

    [MaxLength(120)]
    public string? Section { get; set; }
        = null;

    [Required, MaxLength(200)]
    public string Heading { get; set; } = string.Empty;

    [Required, MaxLength(4000)]
    public string Body { get; set; } = string.Empty;

    public double? Confidence { get; set; }
        = null;

    [Column(TypeName = "jsonb")]
    public Dictionary<string, object?> StructuredPayload { get; set; }
        = new();

    [Column(TypeName = "jsonb")]
    public Dictionary<string, string> Metadata { get; set; }
        = new();

    public DateTimeOffset GeneratedAt { get; set; }
        = DateTimeOffset.UtcNow;

    public DateTimeOffset? UpdatedAt { get; set; }
        = null;
}

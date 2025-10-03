using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Koan.Data.Abstractions.Annotations;
using Koan.Data.Core.Model;
using Koan.Data.Core.Relationships;
using Koan.Mcp;

namespace S13.DocMind.Models;

/// <summary>
/// Represents a processed fragment of a source document along with structured payloads and cross references.
/// Embeddings are persisted through the dedicated DocumentChunkEmbedding entity to align with the blueprint.
/// </summary>
[McpEntity(Name = "document-chunks", Description = "Chunked text and diagram excerpts derived from source documents.")]
public sealed class DocumentChunk : Entity<DocumentChunk>
{
    [Parent(typeof(SourceDocument))]
    public Guid SourceDocumentId { get; set; }
        = Guid.Empty;

    public int Order { get; set; }
        = 0;

    [Required]
    public string Text { get; set; } = string.Empty;

    public int CharacterCount { get; set; }
        = 0;

    public int TokenCount { get; set; }
        = 0;

    public bool IsLastChunk { get; set; }
        = false;

    [Column(TypeName = "jsonb")]
    public List<InsightReference> InsightRefs { get; set; }
        = new();

    [Column(TypeName = "jsonb")]
    public Dictionary<string, object?> StructuredPayload { get; set; }
        = new();

    [Column(TypeName = "jsonb")]
    public Dictionary<string, double> ConfidenceByExtractor { get; set; }
        = new();

    public DateTimeOffset CapturedAt { get; set; }
        = DateTimeOffset.UtcNow;
}

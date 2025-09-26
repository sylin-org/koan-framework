using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Koan.Data.Abstractions.Annotations;
using Koan.Data.Core.Model;

namespace S13.DocMind.Models;

/// <summary>
/// Represents a processed fragment of a source document along with structured payloads and insight references.
/// Supports multi-modal annotations, semantic tagging, and vector enrichment for retrieval workflows.
/// </summary>
[DataAdapter("mongodb")]
[Table("document_chunks")]
public sealed class DocumentChunk : Entity<DocumentChunk>
{
    [Parent(typeof(SourceDocument))]
    public Guid DocumentId { get; set; }
        = Guid.Empty;

    public int ChunkIndex { get; set; }
        = 0;

    [Required]
    public string Content { get; set; } = string.Empty;

    [MaxLength(50)]
    public string ContentEncoding { get; set; } = "utf-8";

    [MaxLength(150)]
    public string ContentType { get; set; } = "text/plain";

    public int StartOffset { get; set; }
        = 0;

    public int EndOffset { get; set; }
        = 0;

    public int TokenCount { get; set; }
        = 0;

    [MaxLength(100)]
    public string? SemanticProfileCode { get; set; }
        = null;

    [Column(TypeName = "jsonb")]
    public List<string> Tags { get; set; }
        = new();

    [Column(TypeName = "jsonb")]
    public Dictionary<string, object> StructuredPayload { get; set; }
        = new();

    [Column(TypeName = "jsonb")]
    public Dictionary<string, object> SchemaMetadata { get; set; }
        = new();

    [Column(TypeName = "jsonb")]
    public List<Guid> InsightReferences { get; set; }
        = new();

    [Vector(Dimensions = 1536, IndexType = "HNSW")]
    public double[]? Embedding { get; set; }
        = null;

    [Column(TypeName = "jsonb")]
    public Dictionary<string, double> VectorAnnotations { get; set; }
        = new();

    [Column(TypeName = "jsonb")]
    public Dictionary<string, double> ConfidenceByExtractor { get; set; }
        = new();

    public DateTimeOffset? ProcessedAt { get; set; }
        = null;

    public DocumentProcessingStage StageCaptured { get; set; }
        = DocumentProcessingStage.Chunk;
}

using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Koan.Data.Abstractions.Annotations;
using Koan.Data.Core.Model;

namespace S13.DocMind.Models;

/// <summary>
/// Represents an extracted insight from a processed document or chunk.
/// Carries schema-aware payloads, cross references, and vector annotations for retrieval.
/// </summary>
[DataAdapter("mongodb")]
[Table("document_insights")]
public sealed class DocumentInsight : Entity<DocumentInsight>
{
    [Parent(typeof(SourceDocument))]
    public Guid DocumentId { get; set; }
        = Guid.Empty;

    public Guid? ChunkId { get; set; }
        = null;

    [MaxLength(100)]
    public string InsightType { get; set; } = string.Empty;

    [MaxLength(200)]
    public string Title { get; set; } = string.Empty;

    [MaxLength(2000)]
    public string Summary { get; set; } = string.Empty;

    public double? ConfidenceScore { get; set; }
        = null;

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
    public List<Guid> RelatedInsightIds { get; set; }
        = new();

    [Column(TypeName = "jsonb")]
    public List<Guid> SupportingChunkIds { get; set; }
        = new();

    public int? StartOffset { get; set; }
        = null;

    public int? EndOffset { get; set; }
        = null;

    [Vector(Dimensions = 1536, IndexType = "HNSW")]
    public double[]? Embedding { get; set; }
        = null;

    [Column(TypeName = "jsonb")]
    public Dictionary<string, double> VectorAnnotations { get; set; }
        = new();

    public DateTimeOffset CreatedAt { get; set; }
        = DateTimeOffset.UtcNow;

    public DateTimeOffset? UpdatedAt { get; set; }
        = null;
}

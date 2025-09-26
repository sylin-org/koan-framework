using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Koan.Data.Abstractions.Annotations;
using Koan.Data.Core.Model;
using Koan.Mcp;

namespace S13.DocMind.Models;

/// <summary>
/// Semantic profile metadata powering document auto-classification and structured extraction.
/// Captures the canonical schema, tags, and vector annotations for downstream processing.
/// </summary>
[DataAdapter("postgresql")]
[Table("semantic_type_profiles")]
[McpEntity(Name = "semantic-type-profiles", Description = "Template definitions, prompts, and embeddings for DocMind document types.")]
public sealed class SemanticTypeProfile : Entity<SemanticTypeProfile>
{
    [Required, MaxLength(100)]
    public string Code { get; set; } = string.Empty;

    [Required, MaxLength(200)]
    public string Name { get; set; } = string.Empty;

    [MaxLength(1024)]
    public string? Description { get; set; }
        = null;

    [Column(TypeName = "jsonb")]
    public List<string> Tags { get; set; }
        = new();

    [Column(TypeName = "jsonb")]
    public Dictionary<string, object> ExtractionSchema { get; set; }
        = new();

    [Column(TypeName = "jsonb")]
    public Dictionary<string, object> ValidationRules { get; set; }
        = new();

    [Column(TypeName = "jsonb")]
    public List<Guid> CanonicalInsightIds { get; set; }
        = new();

    [Vector(Dimensions = 1536, IndexType = "HNSW")]
    public double[]? Embedding { get; set; }
        = null;

    [Column(TypeName = "jsonb")]
    public Dictionary<string, double> VectorAnnotations { get; set; }
        = new();

    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;

    public DateTimeOffset? UpdatedAt { get; set; }
        = null;
}

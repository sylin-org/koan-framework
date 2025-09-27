using System;
using Koan.Data.Abstractions.Annotations;
using Koan.Data.Core.Model;
using Koan.Data.Core.Relationships;
using Koan.Data.Vector.Abstractions;

namespace S13.DocMind.Models;

/// <summary>
/// Vector representation of a processed document chunk persisted through the configured adapter (e.g. Weaviate).
/// </summary>
[VectorAdapter("weaviate")]
public sealed class DocumentChunkEmbedding : Entity<DocumentChunkEmbedding>
{
    [Parent(typeof(DocumentChunk))]
    public Guid DocumentChunkId { get; set; }
        = Guid.Empty;

    [Parent(typeof(SourceDocument))]
    public Guid SourceDocumentId { get; set; }
        = Guid.Empty;

    [VectorEmbedding]
    public float[] Embedding { get; set; }
        = Array.Empty<float>();

    public DateTimeOffset GeneratedAt { get; set; }
        = DateTimeOffset.UtcNow;
}

/// <summary>
/// Vector representation of a semantic type profile to drive auto-classification recommendations.
/// </summary>
[VectorAdapter("weaviate")]
public sealed class SemanticTypeEmbedding : Entity<SemanticTypeEmbedding>
{
    [Parent(typeof(SemanticTypeProfile))]
    public Guid SemanticTypeProfileId { get; set; }
        = Guid.Empty;

    [VectorEmbedding]
    public float[] Embedding { get; set; }
        = Array.Empty<float>();

    public DateTimeOffset GeneratedAt { get; set; }
        = DateTimeOffset.UtcNow;
}

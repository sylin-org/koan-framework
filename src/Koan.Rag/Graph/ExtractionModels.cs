namespace Koan.Rag.Graph;

/// <summary>
/// LLM extraction output: entities and facts from a text chunk.
/// Used as the deserialization target for <see cref="EntityExtractor"/>.
/// </summary>
internal sealed class ExtractionResult
{
    public List<ExtractedEntity> Entities { get; set; } = [];
    public List<ExtractedFact> Facts { get; set; } = [];
}

/// <summary>
/// A concept entity extracted from a chunk by the LLM.
/// </summary>
internal sealed class ExtractedEntity
{
    /// <summary>Canonical name for the entity.</summary>
    public string Name { get; set; } = "";

    /// <summary>One-line description in corpus context.</summary>
    public string Description { get; set; } = "";
}

/// <summary>
/// A self-contained assertion extracted from a chunk.
/// </summary>
internal sealed class ExtractedFact
{
    /// <summary>The factual assertion, self-contained without the source document.</summary>
    public string Statement { get; set; } = "";

    /// <summary>Entity names this fact relates to.</summary>
    public List<string> RelatedEntities { get; set; } = [];
}

/// <summary>
/// LLM extraction output for explicit relationships (Full graph strategy only).
/// </summary>
internal sealed class RelationshipExtractionResult
{
    public List<ExtractedRelationship> Relationships { get; set; } = [];
}

/// <summary>
/// An explicit relationship between two entities, extracted by the LLM.
/// </summary>
internal sealed class ExtractedRelationship
{
    /// <summary>Source entity name.</summary>
    public string From { get; set; } = "";

    /// <summary>Target entity name.</summary>
    public string To { get; set; } = "";

    /// <summary>Natural-language relationship label (e.g., "requires", "is-a", "governed-by").</summary>
    public string Label { get; set; } = "";
}

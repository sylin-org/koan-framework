namespace Koan.Rag.Abstractions;

/// <summary>
/// Declares an entity type as a RAG corpus participant. Multiple attributes
/// on a single entity type create named corpora with independent directives.
/// An unnamed attribute creates the default corpus for the entity type.
/// <para>
/// Lifecycle integration (auto-ingest on save) is activated by this attribute.
/// On-demand operations (Ask, Search) work without the attribute via convention.
/// </para>
/// </summary>
[AttributeUsage(AttributeTargets.Class, AllowMultiple = true, Inherited = false)]
public sealed class RagCorpusAttribute : Attribute
{
    /// <summary>
    /// Creates a default (unnamed) corpus for this entity type.
    /// </summary>
    public RagCorpusAttribute() { }

    /// <summary>
    /// Creates a named corpus with an optional directive.
    /// </summary>
    /// <param name="name">Corpus identity. Must be unique per entity type.</param>
    public RagCorpusAttribute(string name)
    {
        Name = name ?? throw new ArgumentNullException(nameof(name));
    }

    /// <summary>
    /// Corpus identity. Null for the default corpus.
    /// </summary>
    public string? Name { get; }

    /// <summary>
    /// Natural-language directive that shapes extraction behavior during ingestion.
    /// Injected as domain guidance into every LLM extraction prompt.
    /// Example: "Optimize for medical terminology".
    /// </summary>
    public string? Directive { get; init; }

    /// <summary>
    /// AI source routing for the extraction pipeline.
    /// Matches the <c>[Embedding].Source</c> convention.
    /// Example: "garden/reasoning-model".
    /// </summary>
    public string? Source { get; init; }

    /// <summary>
    /// Controls concept graph construction strategy.
    /// Default: <see cref="GraphStrategy.Lightweight"/>.
    /// </summary>
    public GraphStrategy GraphStrategy { get; init; } = GraphStrategy.Lightweight;

    /// <summary>
    /// When true, ingestion is deferred to the background worker.
    /// When false, ingestion runs synchronously in the entity lifecycle hook.
    /// Default: true.
    /// </summary>
    public bool Async { get; init; } = true;

    /// <summary>
    /// Increment to trigger re-ingestion of all entities.
    /// Matches the <c>[MediaAnalysis].Version</c> re-processing pattern.
    /// </summary>
    public int Version { get; init; } = 1;

    /// <summary>
    /// When true, enables corpus-scoped embedding fine-tuning via <c>Adapt()</c>.
    /// Default: false.
    /// </summary>
    public bool AdaptEmbeddings { get; init; }
}

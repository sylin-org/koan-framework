using Koan.Data.Abstractions;
using Koan.Rag.Abstractions;

namespace Koan.Rag;

/// <summary>
/// Static entry point for the RAG subsystem. Provides access to entity-typed
/// knowledge corpora, cross-corpus composition, and partition scoping.
/// <para>
/// <b>Usage:</b>
/// <code>
/// // Default corpus — zero config
/// await Rag.Corpus&lt;Policy&gt;().Ingest(files);
/// var answer = await Rag.Corpus&lt;Policy&gt;().Ask("What are the PII rules?");
///
/// // Named corpus with directive
/// var medical = Rag.Corpus&lt;Policy&gt;("Medical", "Optimize for medical terminology");
///
/// // Cross-corpus composition
/// var answer = await Rag.Compose(
///     Rag.Corpus&lt;Policy&gt;(),
///     Rag.Corpus&lt;TechGuide&gt;()
/// ).Ask("How do I connect to external APIs?");
/// </code>
/// </para>
/// </summary>
public static class Rag
{
    // ── Service Resolution (follows Vector<T> pattern) ──────────────────

    private static IRagService Service
        => (Koan.Core.Hosting.App.AppHost.Current
                ?.GetService(typeof(IRagService)) as IRagService)
            ?? throw new InvalidOperationException(
                "No RAG service configured. Ensure Koan.Rag is referenced and " +
                "services.AddKoan() has been called.");

    private static IRagService? TryService
        => Koan.Core.Hosting.App.AppHost.Current
                ?.GetService(typeof(IRagService)) as IRagService;

    /// <summary>True when the RAG subsystem is registered and available.</summary>
    public static bool IsAvailable => TryService is not null;

    // ── Corpus Access ───────────────────────────────────────────────────

    /// <summary>
    /// Get the default (unnamed) corpus for an entity type. Singleton per type.
    /// Works with zero configuration via convention inference.
    /// </summary>
    public static IRagCorpus<TEntity> Corpus<TEntity>() where TEntity : class, IEntity<string>
        => Service.GetCorpus<TEntity>();

    /// <summary>
    /// Get a named corpus. Throws <see cref="RagCorpusNotFoundException"/>
    /// if no corpus with this name exists for the entity type.
    /// </summary>
    public static IRagCorpus<TEntity> Corpus<TEntity>(string name) where TEntity : class, IEntity<string>
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        return Service.GetCorpus<TEntity>(name);
    }

    /// <summary>
    /// Get or create a named corpus with a directive.
    /// The directive is set on first creation.
    /// </summary>
    public static IRagCorpus<TEntity> Corpus<TEntity>(string name, string directive) where TEntity : class, IEntity<string>
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        return Service.GetCorpus<TEntity>(name, directive);
    }

    // ── Composition ─────────────────────────────────────────────────────

    /// <summary>
    /// Compose multiple corpora into a federated query surface.
    /// Each corpus searches independently; results are merged via
    /// percentile normalization and cross-encoder reranking.
    /// </summary>
    public static IComposedRagCorpus Compose(params IRagCorpusBase[] corpora)
    {
        if (corpora.Length < 2)
            throw new ArgumentException("Compose requires at least two corpora.", nameof(corpora));

        return new ComposedRagCorpus(corpora);
    }

    // ── Partition Scoping ───────────────────────────────────────────────

    /// <summary>
    /// Scope all corpus operations to a partition. Multi-tenant isolation.
    /// Each partition gets its own concept graph.
    /// </summary>
    public static IDisposable WithPartition(string partition)
        => Koan.Data.Core.EntityContext.Partition(partition);
}

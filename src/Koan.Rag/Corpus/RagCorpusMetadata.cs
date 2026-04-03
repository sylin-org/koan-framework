using System.Collections.Concurrent;
using System.Reflection;
using Koan.Rag.Abstractions;

namespace Koan.Rag;

/// <summary>
/// Resolved metadata for a RAG corpus declaration on an entity type.
/// Convention-first: undecorated entities get defaults that work immediately.
/// <para>
/// Matches the <c>EmbeddingMetadata</c> pattern: never returns null for the
/// default corpus. Named corpora return null if not declared.
/// </para>
/// </summary>
public sealed class RagCorpusMetadata
{
    private static readonly ConcurrentDictionary<(Type EntityType, string? Name), RagCorpusMetadata?> _cache = new();

    public string? Name { get; private init; }
    public string? Directive { get; private init; }
    public string? Source { get; private init; }
    public GraphStrategy GraphStrategy { get; private init; } = GraphStrategy.Lightweight;
    public bool Async { get; private init; } = true;
    public int Version { get; private init; } = 1;
    public bool AdaptEmbeddings { get; private init; }

    /// <summary>
    /// True when lifecycle hooks should be wired (attribute is present).
    /// False for convention-inferred defaults (no attribute).
    /// </summary>
    public bool LifecycleEnabled { get; private init; }

    /// <summary>
    /// Resolve metadata for the default (unnamed) corpus of an entity type.
    /// Never returns null — undecorated entities get convention defaults.
    /// </summary>
    public static RagCorpusMetadata ResolveDefault<TEntity>() where TEntity : class
        => ResolveDefault(typeof(TEntity));

    /// <summary>
    /// Resolve metadata for the default (unnamed) corpus of an entity type.
    /// Never returns null — undecorated entities get convention defaults.
    /// </summary>
    public static RagCorpusMetadata ResolveDefault(Type entityType)
        => Resolve(entityType, name: null) ?? ConventionDefault(entityType);

    /// <summary>
    /// Resolve metadata for a named corpus. Returns null if the name is not
    /// declared via <see cref="RagCorpusAttribute"/> on the entity type.
    /// </summary>
    public static RagCorpusMetadata? Resolve<TEntity>(string name) where TEntity : class
        => Resolve(typeof(TEntity), name);

    /// <summary>
    /// Resolve metadata for a specific corpus name on an entity type.
    /// Returns null if the name is not declared.
    /// </summary>
    public static RagCorpusMetadata? Resolve(Type entityType, string? name)
    {
        var key = (entityType, name);
        return _cache.GetOrAdd(key, static k => ResolveFromAttribute(k.EntityType, k.Name));
    }

    /// <summary>
    /// Get all declared corpus metadata for an entity type (all [RagCorpus] attributes).
    /// </summary>
    public static IReadOnlyList<RagCorpusMetadata> ResolveAll(Type entityType)
    {
        var attributes = entityType.GetCustomAttributes<RagCorpusAttribute>(inherit: false);
        var results = new List<RagCorpusMetadata>();

        foreach (var attr in attributes)
        {
            results.Add(FromAttribute(attr));
        }

        return results;
    }

    /// <summary>
    /// The effective corpus name: the declared name, or the entity type name for unnamed corpora.
    /// </summary>
    public string EffectiveName(Type entityType)
        => Name ?? entityType.Name;

    private static RagCorpusMetadata? ResolveFromAttribute(Type entityType, string? name)
    {
        var attributes = entityType.GetCustomAttributes<RagCorpusAttribute>(inherit: false).ToArray();

        if (attributes.Length == 0)
        {
            // No attributes: return convention default for unnamed, null for named
            return name is null ? ConventionDefault(entityType) : null;
        }

        // Find matching attribute
        var match = name is null
            ? attributes.FirstOrDefault(a => a.Name is null) ?? attributes.FirstOrDefault()
            : attributes.FirstOrDefault(a =>
                string.Equals(a.Name, name, StringComparison.OrdinalIgnoreCase));

        return match is not null ? FromAttribute(match) : null;
    }

    private static RagCorpusMetadata FromAttribute(RagCorpusAttribute attr) => new()
    {
        Name = attr.Name,
        Directive = attr.Directive,
        Source = attr.Source,
        GraphStrategy = attr.GraphStrategy,
        Async = attr.Async,
        Version = attr.Version,
        AdaptEmbeddings = attr.AdaptEmbeddings,
        LifecycleEnabled = true
    };

    /// <summary>
    /// Create metadata for a dynamically registered corpus (not attribute-declared).
    /// Used when <c>Rag.Corpus&lt;T&gt;("name", "directive")</c> is called
    /// without a corresponding <c>[RagCorpus]</c> attribute.
    /// </summary>
    internal static RagCorpusMetadata CreateDynamic(string name, string directive) => new()
    {
        Name = name,
        Directive = directive,
        Source = null,
        GraphStrategy = GraphStrategy.Lightweight,
        Async = true,
        Version = 1,
        AdaptEmbeddings = false,
        LifecycleEnabled = false // No auto-hooks for dynamic corpora
    };

    private static RagCorpusMetadata ConventionDefault(Type entityType) => new()
    {
        Name = null,
        Directive = null,
        Source = null,
        GraphStrategy = GraphStrategy.Lightweight,
        Async = true,
        Version = 1,
        AdaptEmbeddings = false,
        LifecycleEnabled = false
    };
}

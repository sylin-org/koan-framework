using Koan.Data.Abstractions;

namespace Koan.Rag.Abstractions;

/// <summary>
/// DI entry point for the RAG subsystem. Resolves corpus instances by entity type and name.
/// Registered as singleton via <c>KoanRagAutoRegistrar</c>.
/// </summary>
public interface IRagService
{
    /// <summary>
    /// Get or create a corpus for the given entity type and optional name.
    /// Returns the same instance for repeated calls with the same type + name.
    /// </summary>
    IRagCorpus<TEntity> GetCorpus<TEntity>(string? name = null) where TEntity : class, IEntity<string>;

    /// <summary>
    /// Get or create a named corpus with a directive.
    /// The directive is set on first creation and cannot be changed after
    /// (use <c>Rebuild(newDirective)</c> to change).
    /// </summary>
    IRagCorpus<TEntity> GetCorpus<TEntity>(string name, string directive) where TEntity : class, IEntity<string>;
}

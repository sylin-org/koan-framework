using Koan.Data.Abstractions;
using Koan.Data.Abstractions.Capabilities;
using Koan.Data.Abstractions.Filtering;
using Koan.Data.Abstractions.Lexical;
using Koan.Data.Abstractions.Sources;

namespace Koan.Data.Core;

/// <summary>
/// The lexical facet of the facade: the one place the capability is negotiated. An entity that declares
/// <c>[Lexical]</c> on a route whose provider does not declare <c>query.lexical</c> is refused at its first write,
/// before anything is held, naming the provider, the token and the corrections; a search on such a route is refused
/// the same way. A search that is admitted carries the caller's read scope (the tenant, the not-yet-deleted predicate)
/// to the store, so a match is always a row the caller could read.
/// </summary>
internal sealed partial class RepositoryFacade<TEntity, TKey> : ILexicalSearchRepository<TEntity, TKey>
{
    private const string LexicalSearchOperation = "entity lexical search";
    private const string LexicalRebuildOperation = "entity lexical rebuild";
    private const string LexicalStatusOperation = "entity lexical status";

    public async Task<IReadOnlyList<LexicalMatch<TKey>>> Search(LexicalQuery query, Filter? scope, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(query);
        var lexical = RequireLexical();
        await using var operationScope = await Guard(DataOperationEffect.Read, LexicalSearchOperation, ct);
        var managed = ReadScopeFilter(operationScope.Segmentation);
        var effective = scope is null ? managed : managed is null ? scope : Filter.All(scope, managed);
        return await lexical.Search(query, effective, ct);
    }

    public async Task<long> Rebuild(CancellationToken ct = default)
    {
        var lexical = RequireLexical();
        await using var operationScope = await Guard(DataOperationEffect.SchemaOrAdmin, LexicalRebuildOperation, ct);
        return await lexical.Rebuild(ct);
    }

    public async Task<LexicalIndexStatus> Status(CancellationToken ct = default)
    {
        var lexical = RequireLexical();
        await using var operationScope = await Guard(DataOperationEffect.Read, LexicalStatusOperation, ct);
        return await lexical.Status(ct);
    }

    /// <summary>
    /// The refusal a write or a search meets when the entity declares a lexical index its route cannot keep, computed
    /// once per facade because both sides of it are type-plane constants: the declaration is a property of the type and
    /// the token is a property of the provider.
    /// </summary>
    private string? LexicalRefusal()
    {
        if (LexicalMetadata.Get(typeof(TEntity)) is null) return null;
        if (DataCaps.Describe(_inner, _inner.GetType().Name).Has(DataCaps.Query.Lexical) &&
            _inner is ILexicalSearchRepository<TEntity, TKey>)
            return null;
        return $"{typeof(TEntity).Name} declares [Lexical] properties, but route '{_sourcePlan.Source}' on provider " +
               $"'{_sourcePlan.Adapter}' does not declare '{DataCaps.Query.Lexical.Id}'. Route the entity to a provider that " +
               "keeps a lexical index (SQLite and PostgreSQL declare it), or remove the [Lexical] declaration.";
    }

    private ILexicalSearchRepository<TEntity, TKey> RequireLexical()
    {
        if (LexicalMetadata.Get(typeof(TEntity)) is null)
            throw new NotSupportedException(
                $"{typeof(TEntity).Name} declares no [Lexical] property. Place [Lexical] on the string properties to search.");
        if (_lexicalRefusal is not null) throw new NotSupportedException(_lexicalRefusal);
        return (ILexicalSearchRepository<TEntity, TKey>)_inner;
    }
}

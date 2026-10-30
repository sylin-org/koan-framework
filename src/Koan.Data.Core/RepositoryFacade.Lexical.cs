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
/// the same way. A search that is admitted carries the application's scope and the caller's read scope (the tenant, the
/// not-yet-deleted predicate) to the store, so a match is always a row the caller could read. The store ranks and pages
/// what it is told, so a scope it cannot run is refused here: applied after the page, it would leave pages short and
/// say that other rows matched.
/// </summary>
internal sealed partial class RepositoryFacade<TEntity, TKey> : ILexicalSearchRepository<TEntity, TKey>
{
    private const string LexicalSearchOperation = "entity lexical search";
    private const string LexicalRebuildOperation = "entity lexical rebuild";
    private const string LexicalStatusOperation = "entity lexical status";

    private FilterSupport? _lexicalScopeSupport;

    public async Task<IReadOnlyList<LexicalMatch<TKey>>> Search(LexicalQuery query, Filter? scope, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(query);
        var lexical = RequireLexical();
        if (scope is not null && FilterSplitter.Split(scope, LexicalScopeSupport(), typeof(TEntity)).Residual is not null)
            throw new NotSupportedException(
                $"A lexical search of {typeof(TEntity).Name} runs its scope in the store, and provider '{_sourcePlan.Adapter}' " +
                "cannot run all of this one. Compare the entity's own fields with values, or read the matches and judge them yourself.");
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

    /// <summary>What the store can run of a search's scope: the adapter's own filter support, a type-plane constant read
    /// once, since the facade's scope support is only inspected for an entity a managed scope could reach.</summary>
    private FilterSupport LexicalScopeSupport() =>
        _lexicalScopeSupport ??= DataCaps.Describe(_inner, _inner.GetType().Name).Detail<FilterSupport>(DataCaps.Query.Filter) ?? FilterSupport.None;

    private ILexicalSearchRepository<TEntity, TKey> RequireLexical()
    {
        if (LexicalMetadata.Get(typeof(TEntity)) is null)
            throw new NotSupportedException(
                $"{typeof(TEntity).Name} declares no [Lexical] property. Place [Lexical] on the string properties to search.");
        if (_lexicalRefusal is not null) throw new NotSupportedException(_lexicalRefusal);
        return (ILexicalSearchRepository<TEntity, TKey>)_inner;
    }
}

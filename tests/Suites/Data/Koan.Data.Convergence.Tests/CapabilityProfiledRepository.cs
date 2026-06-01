using System.Collections.Frozen;
using Koan.Data.Abstractions;
using Koan.Data.Abstractions.Filtering;

namespace Koan.Data.Convergence.Tests;

/// <summary>
/// A minimal in-memory <see cref="IQueryRepository{TEntity,TKey}"/> whose declared
/// <see cref="FilterCapabilities"/> are configurable. It models an architecturally-distinct
/// adapter's pushdown boundary: it "pushes" exactly the operators it declares (by evaluating
/// only that sub-filter against its store) and reports an honest result; everything it does NOT
/// declare is left for the framework's <c>FilterPushdownCoordinator</c> to finish in memory.
///
/// This is the heart of the convergence proof. The real adapters differ only in WHERE the
/// pushdown boundary sits (relational: scalar + JSON-array containment; Mongo: nearly all;
/// in-memory: everything). By sweeping the boundary across capability profiles here and asserting
/// every profile returns the SAME ids as the hand-computed oracle, we prove the contract that the
/// per-adapter pushdown + coordinator floor composition is result-preserving for ANY boundary —
/// which is exactly the guarantee each real adapter then only has to honor for its own profile.
/// </summary>
internal sealed class CapabilityProfiledRepository : IQueryRepository<Widget, string>
{
    private readonly IReadOnlyList<Widget> _store;

    public CapabilityProfiledRepository(IReadOnlyList<Widget> store, FilterCapabilities caps)
    {
        _store = store;
        FilterCapabilities = caps;
    }

    public FilterCapabilities FilterCapabilities { get; }

    public Task<RepositoryQueryResult<Widget>> Query(QueryDefinition query, CancellationToken ct = default)
    {
        // The coordinator guarantees query.Filter contains only nodes this profile declared pushable.
        // We honor that by evaluating the (already-split) filter via the in-memory evaluator — the
        // stand-in for native translation. A real adapter would instead emit native SQL/N1QL/etc.
        IEnumerable<Widget> items = _store;
        if (query.Filter is not null)
            items = items.Where(InMemoryFilterEvaluator.Compile<Widget>(query.Filter));

        var list = items.ToList();
        return Task.FromResult(new RepositoryQueryResult<Widget>
        {
            Items = list,
            TotalCount = list.Count,
            IsEstimate = false,
            SortHandled = RepositoryQueryResult<Widget>.NoSortHandled,
            PaginationHandled = false,
        });
    }

    public Task<CountResult> Count(QueryDefinition query, CancellationToken ct = default)
    {
        IEnumerable<Widget> items = _store;
        if (query.Filter is not null)
            items = items.Where(InMemoryFilterEvaluator.Compile<Widget>(query.Filter));
        return Task.FromResult(new CountResult(items.LongCount(), false));
    }
}

/// <summary>The capability profiles that bracket every real adapter's pushdown boundary.</summary>
internal static class CapabilityProfiles
{
    private static readonly FilterOperator[] AllScalar =
    {
        FilterOperator.Eq, FilterOperator.Ne, FilterOperator.Gt, FilterOperator.Gte,
        FilterOperator.Lt, FilterOperator.Lte, FilterOperator.In, FilterOperator.Nin,
        FilterOperator.StartsWith, FilterOperator.EndsWith, FilterOperator.Contains, FilterOperator.Exists,
    };

    private static readonly FilterOperator[] AllCollection =
    {
        FilterOperator.Has, FilterOperator.HasAny, FilterOperator.HasAll, FilterOperator.HasNone, FilterOperator.Size,
    };

    /// <summary>Pushes everything — InMemory / JSON / Redis floor adapters.</summary>
    public static FilterCapabilities Full => FilterCapabilities.Full;

    /// <summary>Pushes nothing — the always-residual baseline (degenerate KV adapter).</summary>
    public static FilterCapabilities None => FilterCapabilities.None;

    /// <summary>Scalar only — collection containment goes to the floor (a JSON-blob store w/o array ops).</summary>
    public static FilterCapabilities ScalarOnly => new(
        AllScalar.ToFrozenSet(), FrozenSet<FilterOperator>.Empty, NestedPaths: true, IgnoreCase: false);

    /// <summary>Scalar + collection, no ignoreCase/nested — the relational trio (PG/SqlServer/Sqlite).</summary>
    public static FilterCapabilities Relational => new(
        AllScalar.ToFrozenSet(), AllCollection.ToFrozenSet(), NestedPaths: false, IgnoreCase: false);

    /// <summary>Collection only — a contrived boundary to stress the OR-of-split path.</summary>
    public static FilterCapabilities CollectionOnly => new(
        FrozenSet<FilterOperator>.Empty, AllCollection.ToFrozenSet(), NestedPaths: false, IgnoreCase: false);

    public static IReadOnlyDictionary<string, FilterCapabilities> All => new Dictionary<string, FilterCapabilities>
    {
        ["Full (InMemory/JSON/Redis)"] = Full,
        ["Relational (PG/SqlServer/Sqlite)"] = Relational,
        ["ScalarOnly (JSON-blob)"] = ScalarOnly,
        ["CollectionOnly"] = CollectionOnly,
        ["None (degenerate KV)"] = None,
    };
}

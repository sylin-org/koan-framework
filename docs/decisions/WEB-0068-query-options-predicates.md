# WEB-0068 Additive server-side predicates via `QueryOptions.Predicates`

**Status**: Proposed, 2026-05-27
**Drivers**: Hook-contributed visibility / tenancy / soft-delete filters that must apply server-side on every collection read regardless of client input
**Deciders**: Koan Framework maintainers
**Inputs**: `Koan.Web.Hooks` (`IRequestOptionsHook<TEntity>`), `Koan.Web.Filtering.JsonFilterBuilder`, `Koan.Web.Endpoints.EntityEndpointService`
**Outputs**: `QueryOptions.Predicates`, `QueryOptions.AddPredicate<TEntity>`, `QueryPredicateComposer`, updated `EntityEndpointService.QueryCollection` and `QueryCollectionFromBody`
**Extends**: hook surface introduced by the request pipeline; consumed by gposingway emporium `PackageVisibilityHook`

## Context

Koan's request pipeline gives hooks two natural seams for collection reads:

1. `IRequestOptionsHook<TEntity>.OnBuildingOptions` — runs after the request is parsed, before the
   repository is hit. Today this hook can mutate `QueryOptions.Q`, `QueryOptions.Sort`, page size,
   shape, view — every shaping concern except *what records the user is allowed to see*.
2. `ICollectionHook<TEntity>.OnAfterFetch` — runs after the repository returns. A hook can mutate
   the in-memory list, including filtering it down.

The `OnAfterFetch` route is structurally broken for visibility / authorization / scoping
because it tears the contract between the response body and the pagination headers. Concretely:

- The repository returns `RepositoryQueryResult { Items, Total }`. `Total` is computed against the
  pre-filter set. If the hook drops 12 of 50 items, the body has 38 rows but `X-Total-Count` still
  says 50 and `X-Page-Size: 50` is a lie. Clients page off the headers and see "next" buttons that
  silently return wrong-count pages.
- Infinite scroll renders gaps. Page 1 of 50 returns 38 items; page 2 of 50 returns another partial.
  The visible card grid is gap-y and the "load more" sentinel fires on a stale count.
- `?filter=` interacts with the post-fetch drop in non-obvious ways: the user filter pre-narrows the
  candidate set, then the hook drops within that set, so the perceived page size depends on the
  user's filter — not the framework's contract.

The bug class is "post-fetch in-memory filtering for security-relevant concerns". This isn't a Koan
quirk — it's a general anti-pattern. Once you accept that, the only correct seam is the *pre-query
predicate*: the visibility filter has to compose into the expression handed to the adapter so the
adapter does count + page against the already-filtered set.

`IRequestOptionsHook` is the right hook by lifecycle, but `QueryOptions` today exposes no slot for a
contributing predicate. `QueryOptions.Q` is free-text routed to `IStringQueryRepository`; mutating it
from a hook would clash with adapter-specific search semantics and can't AND-compose with
`?filter=`. The only escape is for the hook to short-circuit, render the page itself, and re-route
the request — far past what a hook is supposed to do.

The platform-emporium `PackageVisibilityHook` use case requires:

- Anonymous: `Status == Published`
- Authenticated user: `Status == Published OR ClaimedByUserId == self`
- Admin: `Status != Merged`
- Every tier: never expose `Status == Merged` (the canonical row lives at
  `MergedIntoPackageId`)

All three tiers express the same predicate-tree contribution at the same point in the pipeline, and
all three must compose with any `?filter=` the SPA sends.

## Decision

Add an additive predicate slot to `QueryOptions` and compose contributions AND-wise with the user's
filter at query-execution time. Two pieces, one consumer.

### 1. `QueryOptions.Predicates` — typed slot for hook contributions

```csharp
namespace Koan.Web.Hooks;

public sealed class QueryOptions
{
    // ... existing Q / Page / PageSize / Sort / Shape / View / Extras ...

    /// <summary>
    /// Additional server-side predicates contributed by IRequestOptionsHook
    /// implementations. AND-composed with the user's ?filter= at query-execution time.
    /// Each entry is an Expression&lt;Func&lt;TEntity, bool&gt;&gt; for the request's TEntity;
    /// add via <see cref="QueryOptionsExtensions.AddPredicate{TEntity}"/> so the lambda
    /// type is enforced at compile time.
    /// </summary>
    public List<LambdaExpression> Predicates { get; } = new();
}

public static class QueryOptionsExtensions
{
    public static void AddPredicate<TEntity>(
        this QueryOptions options,
        Expression<Func<TEntity, bool>> predicate);
}
```

`QueryOptions` stays non-generic. The list holds `LambdaExpression` (the generic base of
`Expression<Func<,>>`) so the existing non-generic hook-context plumbing keeps working — every
existing consumer of `QueryOptions` is unaffected. The framework casts to
`Expression<Func<TEntity, bool>>` at query time, which is safe by construction because
`IRequestOptionsHook<TEntity>` is the only intended writer and the extension method enforces the
TEntity match.

### 2. `QueryPredicateComposer` — single composition helper

```csharp
namespace Koan.Web.Filtering;

internal static class QueryPredicateComposer
{
    public static Expression<Func<TEntity, bool>>? AndAll<TEntity>(
        Expression<Func<TEntity, bool>>? user,
        IReadOnlyList<LambdaExpression> hooks);
}
```

- `AndAll(null, [])` → `null` (no predicate; preserves the free-text `Q` path).
- `AndAll(user, [])` → `user` unchanged.
- `AndAll(null, [h1, h2])` → `h1 AND h2`.
- `AndAll(user, [h1, h2])` → `user AND h1 AND h2`.

Each lambda has its own `ParameterExpression`. `AndAll` rewrites every body to share a single new
parameter via an `ExpressionVisitor` so the adapter sees a clean `Expression<Func<TEntity, bool>>`
that can be pushed down by EF / Mongo / SqlServer LINQ providers without parameter-binding errors.

### 3. `EntityEndpointService.QueryCollection` and `QueryCollectionFromBody` — single composition site

Today the query path is `FilterJson XOR Q`:

```csharp
if (request.FilterJson is not null) queryPayload = parsed-predicate;
else if (options.Q is not null) queryPayload = options.Q;
```

After this ADR:

```csharp
Expression<Func<TEntity, bool>>? userPredicate = ParseFilterJson(request.FilterJson);
var composed = QueryPredicateComposer.AndAll<TEntity>(userPredicate, options.Predicates);

object? queryPayload =
    composed is not null            ? composed       // predicate path
  : !string.IsNullOrWhiteSpace(options.Q) ? options.Q  // free-text path (unchanged)
  : null;                                              // fetch-all
```

The composition happens *inside* the endpoint service, not in user code — hooks never see the user's
filter and never need to reason about composition order. The `Q` path is preserved when no
predicates contribute; if any hook contributes a predicate, `Q` is dropped (see Constraints).

## Constraints

### Free-text `Q` and hook predicates are mutually exclusive in v1

The `Q` string routes to adapter-specific `IStringQueryRepository.Query(string, ...)` — a separate
repository surface from `ILinqQueryRepository.Query(Expression<...>, ...)`. The two paths can't be
AND-composed at the framework layer because the framework doesn't know how a given adapter resolves
the free-text string into structured matches.

**Resolution**: when `options.Predicates.Count > 0`, `options.Q` is ignored. The framework logs
this at `Information` level so a developer wiring a new free-text feature alongside a visibility
hook discovers the silent drop quickly. Apps that need free-text + visibility today have two paths:

- Build the visibility predicate using `JsonFilterBuilder` semantics, contribute it through
  `Predicates`, and let the client send free-text through `?filter` with a custom operator that
  the app exposes; or
- Implement `IRequestOptionsHook` that converts `Q` into a predicate (e.g. substring match across
  known text fields) and contributes that *plus* the visibility predicate to `Predicates`. The
  hook then sets `options.Q = null` so the framework takes the predicate path.

A future ADR can promote `Q`-into-predicate conversion to a first-class framework helper. v1 keeps
the surface small.

### Adapter pushdown depends on the adapter, not the framework

Adapters that implement `ILinqQueryRepository<TEntity, TKey>` (in-memory, Mongo, EF-based stores,
SqlServer LINQ provider) get the AND-composed expression tree and push it down natively. Adapters
that only implement `IStringQueryRepository` see no change — `QueryOptions.Predicates` flows through
the same `Data<TEntity, TKey>.QueryWithCount` orchestrator that already routes by capability. The
orchestrator's behaviour against capability-mismatched adapters is preserved.

### `?filter=` semantics are unchanged

`request.FilterJson` continues to parse through `JsonFilterBuilder`. The user predicate is the
*leftmost* operand of the AND chain; hook predicates AND on top of it in registration order. The
composed result is logically equivalent to `user AND h1 AND h2 AND ...`.

## Consequences

**Positive:**

- Visibility, tenancy, soft-delete, and per-role scoping are expressible as a single typed
  predicate contribution. `IRequestOptionsHook<TEntity>` becomes the one canonical seam for "this
  always applies, regardless of what the client asked for".
- Pagination contract stays correct. The adapter counts and pages against the already-filtered
  set; `X-Total-Count`, `X-Total-Pages`, and the `Link` header rel="next" reflect the visible
  population.
- Adapter pushdown works for every adapter that already supports LINQ. The framework adds no new
  capability requirement.
- The change is additive — existing hooks that mutate `Q`/`Sort`/`Page`/`PageSize` still work
  unchanged. No interface signature changes. No breaking change for downstream consumers.
- Removes the temptation to filter in `ICollectionHook.OnAfterFetch`, which is the existing
  footgun this ADR documents away.

**Negative:**

- `QueryOptions.Predicates` is `List<LambdaExpression>` (non-generic) because `QueryOptions`
  itself is non-generic. The type-safe entry point is the extension method
  `AddPredicate<TEntity>` — direct list manipulation is permitted but loses the compile-time
  TEntity check. The composer throws a descriptive `InvalidOperationException` at query time if
  the type doesn't match.
- Free-text `Q` and hook predicates can't both apply (see Constraints). The silent drop is
  flagged in logs; apps that need both will have to encode the free-text as a predicate.

## Alternatives considered

- **`QueryOptions.ServerFilterJson` (a parallel `?filter=` JSON string)**. Rejected: forces every
  hook to round-trip through JSON serialization, loses static typing, doesn't AND-compose without
  parsing twice, and bleeds storage / transport concerns into a hook surface that should be
  expression-shaped.
- **Make `QueryOptions` generic (`QueryOptions<TEntity>`)**. Cleaner type story, but every method
  signature touching `QueryOptions` (`IRequestOptionsHook.OnBuildingOptions`, hook context, the
  endpoint service, every test) needs the TEntity parameter threaded through. The change is
  invasive for a single field. Re-evaluate if the framework grows a second per-entity slot on
  `QueryOptions`.
- **Filter in `ICollectionHook.OnAfterFetch`**. Documented above as the broken-pagination path —
  the bug this ADR exists to prevent.
- **A predicate-contributor extension point separate from `IRequestOptionsHook`**. Adds a third
  hook interface for an idea that's structurally a refinement of "build options". Folding it into
  the existing options hook keeps the lifecycle model small.

## Adoption notes (usage)

```csharp
public sealed class PackageVisibilityHook : IRequestOptionsHook<Package>
{
    public int Order => 0;

    public Task OnBuildingOptions(HookContext<Package> ctx, QueryOptions opts)
    {
        opts.AddPredicate<Package>(p => p.Status != PackageStatus.Merged);

        if (ctx.User?.IsInRole("admin") == true) return Task.CompletedTask;

        var userId = ctx.User?.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        if (string.IsNullOrEmpty(userId))
        {
            opts.AddPredicate<Package>(p => p.Status == PackageStatus.Published);
            return Task.CompletedTask;
        }

        opts.AddPredicate<Package>(p =>
            p.Status == PackageStatus.Published || p.ClaimedByUserId == userId);
        return Task.CompletedTask;
    }
}
```

Discovery is unchanged — `IRequestOptionsHook<Package>` is picked up by the existing hook
auto-registration. Order honours `IOrderedHook.Order` as today.

## Follow-ups

- Document the `Q`/Predicates exclusion in the hook XML docs so the IDE surfaces it at the call site.
- Optional: ship a small `QToPredicateContributor<TEntity>` helper (substring match across
  `[StringQueryable]`-marked properties) once a real downstream case appears.
- Cover predicate AND-composition, hook+`?filter=` interaction, pagination correctness with
  hooks, and the `Q`-drop-with-warning path in the adapter surface test suite.

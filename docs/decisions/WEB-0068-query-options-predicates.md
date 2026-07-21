# WEB-0068 Additive server-side predicates via `QueryOptions.Predicates`

**Status**: Proposed, 2026-05-27
**Drivers**: Hook-contributed visibility / tenancy / soft-delete filters that must apply server-side on every collection read regardless of client input
**Deciders**: Koan Framework maintainers
**Inputs**: `Koan.Web.Hooks` (`IRequestOptionsHook<TEntity>`), `Koan.Web.Filtering.JsonFilterBuilder`, `Koan.Web.Endpoints.EntityEndpointService`
**Outputs**: `QueryOptions.Predicates`, `QueryOptions.AddPredicate<TEntity>`, `QueryPredicateComposer`, updated `EntityEndpointService.QueryCollection` and `QueryCollectionFromBody`
**Extends**: hook surface introduced by the request pipeline; consumed by a downstream consumer `PackageVisibilityHook`

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

The platform-app `PackageVisibilityHook` use case requires:

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

## Amendment 2026-06-14: keyed get-by-id honors the same predicates

The original decision scoped composition to the *collection* read paths (`QueryCollection`,
`QueryCollectionFromBody`). The keyed `GetById` path was left fetching by id with a raw
`Data<TEntity, TKey>.Get(id)` and never ran `BuildOptions`, so `QueryOptions.Predicates` was empty
and never applied. That is a **row-level visibility bypass**: a row a hook filters out of every
listing (multi-tenant scope, published-only, soft-delete) is still returned by id. The first
real-world hit was a public read-only MCP surface where `get-by-id` returned Suppressed/Draft rows
the collection tool correctly hid (MCP routes get-by-id through `IEntityEndpointService.GetById`).

**Resolution.** `EntityEndpointService.GetById` now runs `BuildOptions` (so `IRequestOptionsHook`
contributions populate) and evaluates every contributed predicate against the fetched model
(`PassesRequestPredicates`). A row that fails any predicate returns the same `NotFound` as a missing
row — existence is never revealed to a caller the hook excludes. The relationship-expansion branch
(`?with=all`) is gated before it runs, so it is not a second bypass.

Predicates are evaluated by **compiling the `Expression<Func<TEntity, bool>>` the developer wrote and
invoking it** against the single in-memory model, rather than lowering to the `Filter` AST the
collection path pushes down. For a security gate the literal predicate is the ground truth of intent
and avoids any lowering-divergence between the keyed and collection paths failing *open*. The
type-mismatch guard mirrors `QueryFilterComposer` so a mistyped predicate fails identically on both
paths.

**Scope boundary (deliberate).** `IRequestOptionsHook` is a *read-options* hook; this gate applies to
the read paths (`GetCollection`, `Query`, `GetById`). It does **not** extend to the write paths
(`Delete`, `Patch`, `Upsert`), which read a row internally before mutating it. Governing *write*
authorization by read-visibility predicates would be a semantic expansion that surprises apps which
hide a row from listings yet still allow its owner to mutate it via a separate auth path. Write
authorization remains the job of `IAuthorizeHook` / `IModelHook`. Apps that intentionally want
any-status get-by-id simply register no visibility hook (or use an admin context that contributes no
predicate) — the gate is a no-op when `Predicates` is empty, so non-protected entities are unaffected.

Coverage: `GetByIdVisibilitySpecs` in the adapter-surface InMemory suite (anonymous/owner/admin
matrix, hidden-never-surfaces, owner-vs-other draft, `?with=all` gating).

### GraphQL connector parity

The GraphQL connector (`Koan.Web.Connector.GraphQl`) hand-rolls its entity resolvers instead of
routing through `IEntityEndpointService`, and never implemented WEB-0068: its `GetById` did not run
`BuildOptions` at all, and its collection `GetItems` ran `BuildOptions` but then discarded
`opts.Predicates` when building the query — so the connector leaked hidden rows on **both** read
paths. Both are now fixed to mirror `EntityEndpointService`: `GetItems` AND-composes predicates via
`QueryFilterComposer.AndAll` (exposed to the connector via `InternalsVisibleTo`), and `GetById` runs
`BuildOptions` then gates with the same `PassesRequestPredicates` logic, returning `null` (GraphQL's
not-found) for a filtered row.

Status caveat (honesty rule): the GraphQL connector currently has **no integration test project**
(`unknown since 2026-06-14` — the `Koan.Web.Connector.GraphQl.Tests` / `S4.Web.IntegrationTests`
references in `InternalsVisibleTo` point at projects that no longer exist). The fix mirrors the
tested REST/MCP logic, but a GraphQL ARCH-0079 harness is an outstanding follow-up. The durable fix
is to route the GraphQL resolvers through `IEntityEndpointService` so they stop drifting from the
canonical read pipeline.

## Amendment 2026-06-19: relationship expansion (`?with=all`) is governed per related type (AN-leak)

The keyed-read amendment gated the *root* row before the expansion branch ran, but the expansion
itself was still app-authority: `EntityEndpointService` called the raw `Entity<T,K>.Relatives()`
loaders, which fetch related rows via `Data<TChild,TKey>.All()` and filter by foreign key in-memory
with **no request predicates**. That is a second row-level visibility bypass — a caller reads a
*visible* parent, expands with `?with=all` (REST) or `with: "all"` (MCP), and receives related rows a
direct query of that type would hide. MCP amplifies it: the agent cannot distinguish "forbidden to
see" from "doesn't exist." The original report (docs/assessment/09 §10) confirmed it first-hand: a
visible `Maker` expanded out its Draft `Work` children, and a visible `Work` expanded out a hidden
parent `Maker`.

**Resolution.** A new internal `GovernedRelationshipExpander` (in `Koan.Web`) resolves each edge as a
*governed* query through the related type's own visibility pipeline. For each relationship it runs the
related type's `IRequestOptionsHook`s for the same request (principal + headers), AND-composes the
contributed predicates with the foreign-key filter via `QueryFilterComposer`/`LinqFilterCompiler`, and
pushes the whole thing down to the adapter — fixing the leak **and** the `All()`+in-memory N-load. An
edge inherits its resolved query's projection: a child edge that resolves to zero visible rows is
omitted entirely (no empty-but-present edge that would leak the relationship), and a walled or missing
parent is omitted. Walled-means-silent — no count, no field name, no existence signal. Both expansion
entry points are fixed (GetById and the collection `EnrichRelationships`). MCP rides the same
`IEntityEndpointService`, so the endpoint fix covers every transport — the governance is **not**
duplicated in the transport.

**Scope boundary (deliberate).** The domain traversal API `Entity<T,K>.GetChildren()/GetParents()/
Relatives()` stays **app-authority** — service code (no HTTP principal) still sees all
related rows. Request predicates never leak into `Koan.Data.Core`; the clamp lives entirely at the
`Koan.Web` endpoint layer, where the request context and each related type's hooks exist.

Coverage: `RelationshipExpansionVisibilitySpecs` in the adapter-surface InMemory suite (T1
lateral-movement tunnel, T2 divergent edges to the same target with asymmetric disclosure, T-parent
walled-parent omission, T-app-authority the domain API stays app-authority) and
`RelationshipVisibilityMcpSpec` (the MCP `get-by-id` `with: "all"` path) — the latter through the new
reusable `Koan.Mcp.TestKit` harness. Mutation-verified (dropping the predicates reverts the leak and
fails the specs).

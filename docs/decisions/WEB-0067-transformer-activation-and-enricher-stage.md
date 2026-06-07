# WEB-0067 Transformer activation predicate and Pipeline-stage enrichers

**Status**: Proposed, 2026-05-18
**Drivers**: Per-request enrichment of entity responses without forking the controller; clean composition of multiple per-user transformations
**Deciders**: Koan Framework maintainers
**Inputs**: `Koan.Web.Transformers`, downstream platform (a downstream consumer likes overlay)
**Outputs**: `ITransformerActivationPredicate`, `IEntityEnricher<TEntity>`, registry resolution split into pipeline + terminal, `EnableEntityTransformersAttribute` removal
**Extends**: [WEB-0035](WEB-0035-entitycontroller-transformers.md)

## Context

WEB-0035 established `IEntityTransformer<TEntity, TShape>` as Koan's entity-shaped response
mechanism. The activation model has two properties worth restating:

1. **Activation is purely Accept-header driven.** The result filter
   ([EntityOutputTransformFilter](../../src/Koan.Web.Transformers/EntityOutputTransformFilter.cs))
   inspects the request's Accept header, asks the registry to resolve a transformer for the entity
   type + Accept ranges, and invokes the winner. There is no hook for request context (authenticated
   user, role, header value, feature flag, query parameter).
2. **Activation produces a single winner.** Content negotiation is selective by design — given an
   `Accept: text/csv, application/json;q=0.9`, exactly one transformer wins.

The platform-app "user likes overlay" use case violates both properties:

- It needs to fire on `application/json` even when the SPA sends `Accept: */*` (the resolver's
  current wildcard-only branch hard-skips to MVC default JSON).
- It needs to fire **only when the user is authenticated** — a request-context decision the registry
  cannot make today.
- A second enricher (admin metadata, feature-flagged previews, anything else) must be able to **stack**
  on top — both should run for an admin, not just whichever has higher priority.

The current contract can't express any of these. The naive workaround is to write a custom
`IAsyncResultFilter` per use case, but that bypasses the registry, duplicates the entity-type
discovery code, and means every enrichment lives outside the same composition surface as
content-negotiated transformers.

## Decision

Extend `Koan.Web.Transformers` with three orthogonal pieces:

### 1. `ITransformerActivationPredicate` — universal activation gate

```csharp
namespace Koan.Web.Transformers;

public interface ITransformerActivationPredicate
{
    bool ShouldActivate(HttpContext context);
}
```

Implemented by **controllers** (as the opt-in signal — replaces the marker attribute),
**enrichers** (to gate per-instance, per-request), and optionally by **transformers** (same gate
applied to Accept-resolved candidates). The same contract reused at all three layers keeps the
mental model single-axis.

Implementations are expected to be cheap (no I/O, no DB). They are invoked on every matching
response.

### 2. `IEntityEnricher<TEntity>` — Pipeline-stage transformer

```csharp
namespace Koan.Web.Transformers;

public interface IEntityEnricher<TEntity>
{
    Task<TEntity> Enrich(TEntity model, HttpContext context);
    Task<IReadOnlyList<TEntity>> EnrichMany(IReadOnlyList<TEntity> models, HttpContext context);
}
```

A second, output-only contract for the **Pipeline stage**: same shape in, same shape out.
Multiple enrichers can be registered per entity type; all activated enrichers run in priority
order before the response leaves the filter. Each enricher receives the (possibly already
enriched) value from the previous step and returns a new value.

`IEntityTransformer<TEntity, TShape>` remains the **Terminal stage**: shape-changing,
Accept-negotiated, single-winner. The two interfaces share the registry but partition the
resolver output.

### 3. Resolver returns `(pipeline[], terminal?)`

`ITransformerRegistry.ResolveForOutput` becomes context-aware:

```csharp
TransformerOutputSelection Resolve(
    Type entityType,
    IEnumerable<string> acceptTypes,
    HttpContext context);

public sealed record TransformerOutputSelection(
    IReadOnlyList<EnricherSelection> Pipeline,
    TransformerSelection? Terminal);
```

The resolver:

1. Loads candidate enrichers for `entityType`. Filters to those whose predicate passes (or has
   none). Returns them in priority order — that's `Pipeline`.
2. Resolves a Terminal transformer the same way as today (Accept negotiation + priority + tiebreak),
   with one carve-out: the wildcard-only Accept skip
   ([TransformerRegistry.cs:166-172](../../src/Koan.Web.Transformers/TransformerRegistry.cs#L166-L172))
   still applies. Terminal transformers stay Accept-driven; predicate gating on a Terminal is an
   additional filter, not a replacement for Accept matching.
3. Returns both.

### Filter pipeline

```
[EntityOutputTransformFilter.OnResultExecutionAsync]
        ↓
controller implements ITransformerActivationPredicate ?  ───── no ──→ skip (default JSON)
        ↓ yes
controller.ShouldActivate(ctx) ?                          ───── no ──→ skip
        ↓ yes
resolve (pipeline[], terminal?) for the response value's item type
        ↓
apply pipeline[0] → pipeline[1] → ... (each receives TEntity, returns TEntity)
        ↓
terminal present ?  ── yes ──→ apply terminal, set Content-Type to terminal.ContentType
                    ── no  ──→ pass enriched value through to default JSON
```

### Removal: `EnableEntityTransformersAttribute`

The attribute is deleted. Opt-in is now exclusively via the controller implementing
`ITransformerActivationPredicate`. This is a breaking change for the one archived sample
(`samples/archive/S2/API/Controllers/ItemsController.cs`) and consumers outside this repo, justified
by the pre-1.0 status and the lack of internal adoption observed at the time of writing.

### Auto-discovery

The existing `TransformerStartupInitializer` scan in
[TransformerServiceCollectionExtensions.cs](../../src/Koan.Web.Transformers/TransformerServiceCollectionExtensions.cs)
gains a parallel branch for `IEntityEnricher<>` registrations. DI override
(`services.AddEntityEnricher<TEntity, TEnricher>()`) takes precedence over discovered enrichers
at `TransformerPriority.Explicit`.

## Activation matrix

| Need                                              | How                                                                                          |
|---------------------------------------------------|----------------------------------------------------------------------------------------------|
| "X if A, Y if B, mutually exclusive"              | Both Pipeline (or both Terminal — Terminal is naturally exclusive). Predicates encode A, B. |
| "X if A *and* Y if B, both stack"                 | Both Pipeline. Predicates encode A, B. Both run when both pass.                              |
| "X if A, fallback to default if not"              | Single Pipeline with predicate A. Absent activation falls through to MVC default JSON.       |
| "CSV variant alongside auth-enrichment"           | CSV is Terminal, likes is Pipeline. Both run: enriched → CSV.                                |
| "Higher-priority enricher first"                  | Same Stage, priority breaks ties (Explicit > Discovered), registration order is the second key. |
| "Per-request controller bypass"                   | Controller's `ShouldActivate` returns `false` for the request (e.g. `?raw=1`).               |

## Consequences

**Positive:**

- One mental model — predicates everywhere, two stages (Pipeline / Terminal) for two purposes
  (enrich / project).
- Zero migration cost for existing Terminal transformers — they implement the same `IEntityTransformer<,>`
  and don't need to opt into anything new. The Stage distinction is encoded by the interface, not a
  flag on the existing contract.
- Compositional enrichment unlocks per-user, per-role, per-flag overlays without forking controllers.
- The wildcard-only Accept hard-skip is preserved, so anonymous catalog requests stay edge-cacheable.

**Negative:**

- Two interfaces to learn instead of one. The DX win — no throw-away `Parse`/`ParseMany` stubs on
  enrichers — outweighs the surface increase.
- `[EnableEntityTransformers]` removal is a breaking change for downstream consumers. Koan is
  pre-1.0; the attribute had near-zero adoption observed.
- Pipeline ordering must be deterministic. Priority + registration order is documented as the
  stable contract.

## Alternatives considered

- **One interface + `Stage` enum on `IEntityTransformer<,>`.** Smaller surface, but every Pipeline
  enricher would carry throw-away `Parse`/`ParseMany` implementations and a phantom `TShape` generic
  parameter. The two-interface shape is strictly clearer.
- **Keep the attribute, add the interface as an alternative.** Two opt-in mechanisms doing the same
  job. Rejected — pick one.
- **Predicate on `IEntityTransformer<,>` only; no enricher abstraction.** Would let one transformer
  activate by predicate, but doesn't solve the composition case (two enrichers both stacking).

## Adoption notes (usage)

```csharp
public sealed class PackagesController
    : EntitySummaryController<Package, PackageSummary>, ITransformerActivationPredicate
{
    public bool ShouldActivate(HttpContext ctx) => true;
}

public sealed class PackageSummaryLikesEnricher
    : IEntityEnricher<PackageSummary>, ITransformerActivationPredicate
{
    public bool ShouldActivate(HttpContext ctx)
        => ctx.User?.Identity?.IsAuthenticated == true;

    public Task<PackageSummary> Enrich(PackageSummary model, HttpContext ctx) { /* ... */ }
    public Task<IReadOnlyList<PackageSummary>> EnrichMany(IReadOnlyList<PackageSummary> models, HttpContext ctx) { /* ... */ }
}
```

Auto-discovery picks up the enricher. Stricter opt-in via `services.AddEntityEnricher<PackageSummary, PackageSummaryLikesEnricher>()`.

## Follow-ups

- Swagger/OpenAPI: filter advertises Terminal content types only — enrichers are not negotiable media
  variants. Update
  [TransformerMediaTypesOperationTransformer.cs](../../src/Koan.Web.OpenApi/Transformers/TransformerMediaTypesOperationTransformer.cs)
  to look up the controller via `ITransformerActivationPredicate` instead of the attribute.
- Cover predicate activation, enricher stacking, pipeline+terminal composition, and the wildcard-only
  fallthrough rule with tests.

# Pagination & Data Stack Refactoring Plan

## Purpose

Align pagination behavior across EntityController endpoints, data helpers, and repository adapters while reducing duplicated logic and simplifying the mental model for developers building against the Koan data stack.

This plan integrates the discoveries captured during the pagination attribute proposal review:

- **Unify Query Pipelines**: `EntityEndpointService` still duplicates logic now exposed via `Data.QueryWithCount`, risking drift in count enforcement, safety caps, and header metadata.
- **Clarify Unpaged Semantics**: Optional and Off controller flows leak pagination markers (`Page = 1`) into downstream `DataQueryOptions`, causing helpers to mis-detect paging intent and skip absolute safety limits.
- **Extend Ergonomics to Statics**: Model/Data statics such as `All()` and `Remove()` do not accept `DataQueryOptions`, forcing ad-hoc plumbing for pagination, sharding, and sorting modifiers.
- **Keep Modes Safe by Default**: With streaming removed, we must confirm the default `PaginationMode.On` path is the single source of truth for windowing and that Optional/Off modes still respect global guardrails.

## Goals

1. **Single Path for Query Execution** – All collection reads should flow through `Data.QueryWithCount` (or an equivalent core helper) so pagination and safety semantics are defined once.
2. **Consistent Option Semantics** – When pagination is disabled, option builders must surface `HasPagination == false` to avoid bypassing safety caps or returning misleading metadata.
3. **Ergonomic Static APIs** – Model and data helpers expose optional `DataQueryOptions` (and `QueryResult` variants) so orchestration code can opt into paging and totals without hand-crafting payloads.
4. **Composable Removal Helpers** – Deletion APIs accept the same option objects to target sets/filters without proliferating bespoke overloads.
5. **Reduced Cognitive Load** – Developers understand pagination behavior by inspecting attributes + a small surface of well-documented helpers, not multiple scattered implementations.
6. **Safety First** – Optional/Off modes still enforce `AbsoluteMaxRecords`, return 413 when exceeded, and document the policy clearly.

## Guiding Principles

- **KISS** – Prefer composition over branching; avoid exposing more knobs than necessary in public APIs.
- **Separation of Concerns** – Controllers translate HTTP intent into `DataQueryOptions`; data helpers enforce safety and call repositories; repositories focus on storage mechanics.
- **Incremental Delivery** – Land refactor in stages with regression tests at each layer (options, data helpers, endpoint service, controller).
- **Backwards Compatibility** – Preserve legacy signatures by forwarding to new overloads to avoid breaking consumers.

## Phased Refactoring Plan

### Phase 1 – Normalize Option Semantics

1. **Option Builder Adjustments**
   - Update `EntityController.GetCollection` (and any helper building `DataQueryOptions`) to emit `null`/`0` for `Page`/`PageSize` when pagination is not active.
   - Extend `DataQueryOptions` with helpers like `WithoutPagination()` and ensure `HasPagination` strictly checks for positive page & size.
2. **Unit Coverage**
   - Add tests for Optional/Off flows verifying `HasPagination == false` and `AbsoluteMaxRecords` enforcement.
   - Confirm paged flows still pass through unchanged.

### Phase 2 – Centralize Query Execution

1. **Endpoint Service Refactor**
   - Translate existing branches (`FilterJson`, `Q`, default) into a `(queryPayload, options)` tuple, then call `Data<TEntity, TKey>.QueryWithCount`.
   - Extend `QueryResult<TEntity>` with an `ExceededSafetyLimit` flag (or return type wrapper) so the controller can emit 413 without inspecting empty payloads.
2. **Remove Duplicate Logic**
   - Delete in-service counting/paging/safety code now handled by `QueryWithCount`.
   - Ensure hook invocation & response headers remain intact.
3. **Regression Tests**
   - Update endpoint service tests to validate 413 behavior, pagination headers, and Optional/Off flows via the shared helper.

### Phase 3 – Harden `QueryWithCount`

1. **Repository Capability Detection**
   - When `IPagedRepository` is unavailable, ensure `QueryWithCount` enforces caps before materializing full results (short-circuit on `CountAsync`).
   - Add guardrails for repositories that cannot supply counts (document fallback behavior).
2. **API Refinements**
   - Provide `QueryWithCount` overloads for LINQ/string queries to avoid forcing callers to pass raw `object?` payloads.
   - Document the contract and expected adapter implementations.
3. **Test Matrix**
   - Cover paged, unpaged, and cap-exceeded flows for both paged and non-paged repository adapters.

### Phase 4 – Expose Ergonomic Static APIs

1. **Query Helpers**
   - Add overloads: `All(DataQueryOptions? options = null, CancellationToken ct = default)` and `AllWithCount(DataQueryOptions? options = null, CancellationToken ct = default)` forwarding to `QueryWithCount(null, options, ct, absoluteMaxRecords: null)`.
   - Mirror overloads on generated model statics (`Model.All`, `Model.AllWithCount`).
2. **LINQ/String/Custom Queries**
   - Add `Query(predicate, DataQueryOptions? options, ...)` overloads that map to `QueryWithCount` for totals when requested.
   - Supply builder helpers so callers can fluently construct `DataQueryOptions` (e.g., `.WithSort("Name")`, `.ForSet("regional")`).
3. **Documentation**
   - Update proposal and developer guides with usage examples, clarifying that passing `null` options preserves legacy full-scan behavior.

### Phase 5 – Align Removal Helpers

1. **Data Layer**
   - Introduce `RemoveAll(DataQueryOptions? options, ...)` and `Remove(DataQueryOptions? options, ...)` overloads honoring `options.Set`/`Filter`.
   - Ensure repository interfaces can translate modifiers (fallback to legacy `set` parameter where required).
2. **Model Layer**
   - Mirror overloads on generated statics, maintaining existing signatures as forwards.
3. **Safety Checks**
   - Gate destructive operations behind explicit pagination/filters when required; document expectations in guides.

### Phase 6 – Controller & Client Experience

1. **Controller Simplification**
   - Reduce `EntityController` pagination logic to: resolve policy → build `DataQueryOptions` → call endpoint service → handle 413 vs. success.
   - Ensure response headers (page, pageSize, totalCount) are emitted only when pagination is active.
2. **Client Communication**
   - Update OpenAPI filters and docs to reflect new default `PaginationMode.On`, optional modifiers, and 413 behavior.
   - Provide migration notes for clients expecting streaming/full scans.

**Status:** The OpenAPI layer now ships a dedicated `PaginationOperationFilter` that projects `[Pagination]` metadata into query parameters, safety headers, and a documented `413` response. Pagination headers are emitted only when the policy enables them, and client documentation now references the shared defaults instead of controller-specific logic.

### Phase 7 – Validation & Rollout

1. **Cross-Cutting Tests**
   - End-to-end tests covering each pagination mode, optional/off caps, and attribute overrides.
   - Load/perf validation on large datasets to ensure `QueryWithCount` does not regress memory usage.
2. **Telemetry & Logging**
   - Add structured logs or metrics when safety caps trigger to monitor real-world impact.
3. **Release Notes & Adoption Guide**
   - Publish guidance summarizing new APIs, defaults, and upgrade steps for existing applications.

**Status:** Pagination safety trips now emit structured warnings from `EntityEndpointService`, giving operations teams visibility when clients exceed absolute caps. New Swagger operation filter tests exercise mode-specific query parameters and headers, providing regression coverage until full end-to-end suites are added. Documentation updates summarize the new defaults and migration guidance for consumers.

## Risks & Mitigations

| Risk | Impact | Mitigation |
|------|--------|------------|
| Repositories lacking count support | Unable to enforce caps before materializing data | Define adapter contract fallback (e.g., partial counts) and document requirements; provide default in-memory guard with warning logs |
| Legacy code relying on implicit pagination | Behavior changes could surprise consumers | Maintain old overloads, add feature flag to opt-in, provide migration tooling/tests |
| Increased surface area overwhelms teams | Ergonomic goal missed | Supply option builder helpers and concise documentation; keep modifiers optional |
| Safety flag misinterpretation | 413 not triggered | Add explicit `ExceededSafetyLimit` in `QueryResult` and enforce via integration tests |

## Success Criteria

- Controllers exhibit a single, well-tested data access path with predictable pagination headers.
- Optional/Off requests never misreport pagination state and still honor absolute record caps.
- Developers can opt into pagination/sorting/sharding modifiers from statics without bespoke plumbing.
- Documentation concisely explains pagination modes, helper overloads, and safety behavior.
- Runtime telemetry shows reduced incidence of accidental full scans or controller-specific pagination bugs.


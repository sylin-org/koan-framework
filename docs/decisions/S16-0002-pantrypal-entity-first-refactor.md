# S16-0001 PantryPal Entity-First Refactor

Status: Accepted  
Date: 2025-10-08  
Owner: PantryPal Sample Maintainers  
Related: WEB-0035, AI-0014, SAMPLE-0016

## Context
The initial PantryPal implementation concentrated multi-concern logic (vision ingestion, confirmation, inventory search, stats, meal planning) inside a single `PantryController`. Custom in-memory search (`/api/pantry/search`) duplicated the generic `EntityController<T>` surface and eagerly materialized full collections (`PantryItem.All()`), creating scale and maintenance risks.

Koan's `EntityController<TEntity>` already supplies a rich, uniform REST surface (CRUD, paging, filtering JSON, sorting, relationship expansion, shape/view selection, JSON Patch). Retaining bespoke search obscured the framework's capabilities and encouraged anti-patterns (in-memory filtering, unbounded list materialization).

## Decision
1. Remove legacy `/api/pantry/search` endpoint. All inventory querying now uses `GET /api/data/pantry` with `filter=`, `q=`, `sort=`, paging params.
2. Split controllers by concern:
   - `PantryIngestionController` (upload + confirm)
   - `PantryInsightsController` (stats)
   - `MealsController` (suggest/plan/shopping orchestration)
   - Generic entity controllers retained (`RecipeController`, `PantryItemController`, etc.)
3. Extract orchestration/domain logic into services:
   - `PantryConfirmationService`
   - `PantryInsightsService`
   - `MealPlanningService`
4. Centralize route literals in `Infrastructure/Constants.cs` (`PantryRoutes`).
5. Keep entity CRUD thin and declarative; showcase filter/paging examples in README.
6. Introduce contracts folder for DTO separation (`Contracts/*.cs`).

## Rationale
- Eliminates duplication of framework primitives.
- Enables large dataset efficiency (pagination/streaming available provider-side).
- Improves testability (services unit-testable without HTTP layer).
- Clarifies sample pedagogy: domain innovation (vision + planning) vs commodity CRUD.
- Aligns with Koan directives (entity-first, no repository layers, no inline endpoint duplication).

## Consequences
Positive:
- Cleaner controller code and separation of concerns.
- Reduced memory pressure risk and easier future provider optimization.
- Documentation now directly reflects canonical Koan usage.

Negative / Mitigations:
- Clients using removed `/api/pantry/search` must migrate (sample is greenfield; accepted). If transitional support is desired, a 410 shim can be reintroduced temporarily.
- Additional service classes increase file count; mitigated by clearer boundaries.

## Alternatives Considered
- Retain `/api/pantry/search` but internally proxy → Rejected (adds noise, encourages legacy usage).
- Add custom query DSL endpoint → Deferred until semantic / AI search adds net-new capability.
- Monolithic controller retained → Rejected (poor cohesion, hides framework showcase).

## Implementation Notes
- `PantryStats` moved to dedicated service, shape stable for UI.
- Meal planning scoring retained (extracted); future improvement: push scoring heuristics behind strategy interface.
- Vision ingestion still writes images to local storage; future: abstract to a storage provider.

## Follow Ups
- Add semantic search endpoint (`/api/pantry-semantic/query`) once vector/embedding pipeline is integrated.
- Add unit tests for services (scoring edge cases, stats aggregation, confirmation corrections learning).
- Consider PaginationAttribute tuning for pantry vs recipes (different default sizes).
- Add relationship examples (e.g., `with=recipe` once relationships defined).

## References
- WEB-0035 EntityController transformers & payload shaping
- AI-0014 MCP Code Mode foundation
- SAMPLE-0016 Initial PantryPal architecture


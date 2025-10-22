# S16 PantryPal Technical Overview

## Layering Contract
- Entity CRUD: Provided by `EntityController<T>` classes under `Controllers/EntityControllers.cs`.
- Domain Orchestration: Implemented in services (`Services/*Service.cs`). Controllers are thin delegators.
- Ingestion vs Insights vs Planning separated for cohesion and testability.
- DTOs isolated under `Contracts/`.

## Services
| Service | Responsibility |
|---------|----------------|
| PantryConfirmationService | Maps detections → pantry items; learns corrections |
| PantryInsightsService | Aggregated inventory metrics (counts, categories, expirations) |
| MealPlanningService | Suggest scoring, plan persistence, shopping list generation |

## Removed / Avoided
- Deprecated `/api/pantry/search` removed (generic entity endpoints cover filtering/paging).
- No repository pattern (uses `Entity<T>.All()`, `Get()`, `Save()`).
- No manual DI clutter in Program.cs (auto-registrar pattern).

## Querying Inventory
Rely on `GET /api/data/pantry` with:
- `filter={"Status":"available"}`
- `sort=-ExpiresAt,Name`
- `page=1&pageSize=25`
- Optionally `all=true` when policy allows (avoid for large datasets).

## Pagination Policy Customization
Annotate a controller with:
```csharp
[Pagination(Mode = PaginationMode.On, DefaultSize = 20, MaxSize = 100, IncludeCount = true, DefaultSort = "-ExpiresAt,Name")] 
public class PantryItemController : EntityController<PantryItem, string> {}
```
(Example not applied globally to keep sample defaults minimal.)

## Future Extensions
- Relationship expansion examples (`with=recipe` once relationships established)
- AI-driven substitution recommendations
- Embedding cache invalidation on PantryItem mutation
- Moderation / EXIF strip pre-ingestion

## Testing Strategy
- Unit tests target service orchestration logic (confirmation, insights, meal planning).
- Integration tests (separate project) can exercise entity controller surface + vision ingestion workflow.
 - Pagination policy enforcement tests deferred post-MVP; controller attribute documents intent (MaxSize=200) and connector-level clamping covers safety.

## Error Handling
Controllers return simple `{ error: "..." }` payloads for clarity. Enhancements (error codes, trace IDs) can be added when a common error envelope lands in core.

## Security & Auth
Sample omits authentication for brevity; add an auth filter or middleware for protected operations when integrating with identity.

## Storage & Vision
Photo persistence uses `IPhotoStorage` (wrapper over Koan.Storage). Keys are GUID v7 with `photos/` prefix. Thumbnails deliberately omitted to keep sample lean (noise vs learning value). Default `photos` profile uses local provider; swap to cloud by updating `Koan:Storage:Profiles`. Vector search (semantic+lexical hybrid) engages automatically when a vector provider is configured; otherwise service degrades to lexical filtering with header `X-Search-Degraded: true`.

## Decision References
See ADR `S16-0001-pantrypal-entity-first-refactor.md` for rationale behind the refactor.

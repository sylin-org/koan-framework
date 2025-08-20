# HTTP API conventions (Sora.Web)

Sora.Web provides sensible defaults for entity-centric APIs and well-known operational endpoints.

Entity endpoints (typical)
- GET /api/{entity} — list/query
- GET /api/{entity}/{id} — read by id
- POST /api/{entity} — create
- PUT /api/{entity}/{id} — update
- DELETE /api/{entity}/{id} — delete

Query/filter
- Prefer POST /api/{entity}/query with a JSON filter body for complex queries.
- Adapters push down filters/paging when possible. If fallback to in-memory slicing occurs, Sora adds header: `Sora-InMemory-Paging: true`.
- Totals should use repository `CountAsync` (not by reading all). Your API can return totals in the body or via a total-count header as appropriate.

Paging
- Common parameters: `page` (1-based) and `size` (page size). Exact shape may vary by controller template.
- Header `Sora-InMemory-Paging: true` is emitted when the controller had to paginate locally.

Headers
- `Sora-Trace-Id`: correlation id of the current request’s trace (when tracing enabled).
- `Sora-InMemory-Paging`: `true` when in-memory pagination fallback happened.

Well-known endpoints
- See well-known-endpoints.md for `/.well-known/sora/*` routes.

References
- docs/15-entity-filtering-and-query.md
- docs/12-cqrs-for-humans.md
- docs/decisions/DATA-0032-paging-pushdown-and-in-memory-fallback.md

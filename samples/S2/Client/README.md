# S2.Client (AngularJS)

A minimal static client served by Nginx that talks to S2.Api at `/api`.

- Pagination controls respect server-provided headers.
- Seed buttons support client-side or server-side generation.
- Clear deletes all items and now forces a reload of page 1.
- Request log shows last ~100 calls.
- Structured filter box builds a JSON filter based on visible model fields.
	- Choose Field (detected from data, PascalCase heuristic), Operator (contains/equals), and Value.
	- Example: Field=Name, Operator=contains, Value=foo → `?filter={"Name":"*foo*"}`.

## Dev notes

- Clear action sets `page = 1` and calls `load()` to always refresh the list.
- Server-side seed uses `POST /api/items/seed/{count}` which replaces the set on the server.
- Client-side seed issues multiple `POST /api/items` requests and then reloads.


# Entity Filtering and Query: From Simple to Powerful

This guide explains how to filter and page entities using Koan’s generic `EntityController`, step by step.
It covers both GET with querystring filters and POST /query for richer payloads, and shows how clients like S2.Client compose requests.

Audience: Developers building web APIs or clients on top of Koan.

---

## 1) TL;DR (Happy path)

- GET by filter (querystring):
  - `GET /api/items?filter={"Name":"*milk*"}&page=1&size=10`
- POST with body:
  - `POST /api/items/query`
  - Body: `{ "filter": { "Name": "*milk*" }, "page": 1, "size": 10 }`
- Headers: `X-Total-Count` is set to the count AFTER filtering (before pagination). `X-Page`, `X-Page-Size`, `X-Total-Pages` are included.
- Header `Koan-InMemory-Paging: true` is set when the server slices the page in memory (fallback). Providers should prefer native paging pushdown.
- Optional set routing: add `set=backup` to target a non-root logical set.

---

## 2) Basics: Wildcards and equality

Koan uses a pragmatic JSON filter subset:
- Equality: `{ "Status": "active" }`
- String wildcards with `*`:
  - Starts with: `{ "Name": "milk*" }`
  - Ends with: `{ "Sku": "*123" }`
  - Contains: `{ "Title": "*pro*" }`

These work over GET querystrings (URL-encode or use single quotes) and POST bodies.

Examples:
- `GET /api/items?filter={'Name':'*milk*'}`
- `GET /api/products?filter={"Sku":"*ABC"}`

Tip: Single quotes are tolerated for querystrings to ease manual testing.

---

## 3) Combining multiple conditions

You can express multiple criteria in two ways:

- Implicit AND by listing fields:
  - `{ "Name": "*milk*", "Status": "active" }`
- Explicit boolean operators:
  - AND: `{ "$and": [ { "Name": "*milk*" }, { "Status": "active" } ] }`
  - OR: `{ "$or": [ { "Name": "*milk*" }, { "Name": "*oat*" } ] }`
  - NOT: `{ "$not": { "Status": "inactive" } }`

Membership:
- `$in`: `{ "Status": { "$in": ["active","pending"] } }`

Examples:
- `GET /api/items?filter={"$or":[{"Name":"*milk*"},{"Name":"*oat*"}]}`
- `POST /api/items/query` with `{ "filter": { "$and": [ {"Status":"active"}, {"Name":"*milk*"} ] } }`

---

## 4) Pagination and headers

- Koan paginates server-side when page/size are present (or when required by controller behavior).
- Response headers:
  - `X-Total-Count`: number of items matching the filter
  - `X-Page`, `X-Page-Size`, `X-Total-Pages`
- Link header may include `first`, `prev`, `next`, `last` when applicable.

Example flow:
1) Client calls `GET /api/items?filter={'Name':'*milk*'}&page=1&size=10`
2) Server computes total after filtering, returns page 1.
3) Client reads headers and updates its pager.

---

## 5) Client example: S2.Client (AngularJS)

S2.Client provides a minimal UI and builds filters based on the model:
- Field dropdown (auto-detected from the first returned item; camelCase → PascalCase heuristic)
- Operator dropdown (contains|equals)
- Value input
- The client builds `{ Field: pattern }` and appends it to the URL as `filter=...`.

Example request it generates:
- `GET /api/items?page=1&size=10&filter={"Name":"*foo*"}`

This aligns with the server’s filter semantics and pagination headers.

---

## 6) Sets (routing to non-root data)

Koan supports logical sets to route the same entity to different physical storage names (e.g., `root` vs `backup`).
- Root set uses no suffix; others are suffixed internally (e.g., `#backup`).
- Pass `set` via querystring or POST body.

Examples:
- `GET /api/items?filter={'Name':'*milk*'}&set=backup`
- `POST /api/items/query` with `{ "filter": {"Name":"*milk*"}, "set": "backup" }`

---

## 7) Constraints and tips

- v1 targets top-level fields for comparisons.
- No regex support. Use `*` wildcards for starts/ends/contains.
- Type coercion is relaxed (strings to numbers/bools where obvious).
- Prefer LINQ-capable adapters to push down filters; otherwise a safe in-memory filter is applied.

---

## 8) API reference (EntityController)

- GET collection: `GET /api/{entity}?page&size&filter&set`
- POST query: `POST /api/{entity}/query` with `{ filter?, page?, size?, set?, $options? }`

### $options

You can control behavior with an optional `$options` object at the root or nested inside any operator/object. Currently supported:

- `ignoreCase` (bool, default false): when true, string comparisons (equals/startsWith/endsWith/contains and `$in` on strings) are case-insensitive using a provider-friendly approach (lowercasing both sides). For example:

- `GET /api/items?filter={"$options":{"ignoreCase":true},"Name":"*milk*"}`
- `POST /api/items/query` body:
  ```json
  {
    "$options": { "ignoreCase": true },
    "filter": { "$or": [ {"Name":"*milk*"}, {"Title":"*oat*"} ] },
    "page": 1,
    "size": 10
  }
  ```

Notes:
- Case-insensitive equality maintains null semantics. For method-based matches (starts/ends/contains), nulls are treated as empty strings to allow translation.
- This is implemented with single-argument string methods to maximize LINQ provider compatibility (e.g., Mongo LINQ).
- Headers: `X-Total-Count`, `X-Page`, `X-Page-Size`, `X-Total-Pages`, optional `Link`

See also:
- ADR 0029: JSON Filter Language and Query Endpoint
- ADR 0030: Entity Sets, Routing, and Storage Suffixing

---
type: GUIDE
domain: web
title: "Patch Capabilities How-To"
audience: [developers, architects, ai-agents]
status: current
last_updated: 2025-10-09
framework_version: v0.6.3
validation:
  date_last_tested: 2025-10-09
  status: verified
  scope: docs/guides/patch-capabilities-howto.md
---

# Patch Capabilities How-To

## Contract

- Inputs:
  - An ASP.NET Core app with `builder.Services.AddKoan()` and `EntityController<TEntity>` wired
  - Clients issuing HTTP PATCH with one of:
    - application/json-patch+json (RFC 6902)
    - application/merge-patch+json (RFC 7386)
    - application/json (partial JSON)
  - Optional: in-process patch via Data layer using the canonical PatchOps model
- Outputs: Updated entity state with lifecycle hooks and transformers applied
- Error modes:
  - Invalid JSON Pointer → 400
  - Attempt to mutate `/id` → 409
  - Unsupported ops when falling back to in-process executor (copy/move/test) → 400
  - Provider rejects patch when pushdown not supported → 501/409 depending on adapter
- Success criteria:
  - Requests normalize to canonical PatchOps (ADR DATA-0077)
  - Null and array policies applied per options
  - Provider pushdown used when available; otherwise in-process fallback applies

See also:

- Canonical model: [DATA-0077: Canonical Patch Operations](../decisions/DATA-0077-canonical-patch-operations.md)
- Web API details: [PATCH formats and normalization](../api/patch-normalization.md)

---

## Usage patterns: HTTP PATCH (controllers)

Entity controllers accept all three PATCH formats and normalize them to canonical operations. Do not declare inline endpoints; use MVC controllers.

DX defaults and guardrails:
- The route id is canonical. If a client includes `id` in the body, it must match the route id. Mismatch returns 400 with code `web.patch.idMismatch`.
- Optional per-request policy overrides via query:
  - `?nulls=default|null|ignore|reject`
  - or granular: `?mergeNulls=default|reject`, `?partialNulls=null|ignore|reject`
  See `Koan.Web.Infrastructure.KoanWebConstants.Query`.

### RFC 6902 (application/json-patch+json)

Example request:

PATCH /api/todos/{id}
Content-Type: application/json-patch+json

[
  { "op": "replace", "path": "/title", "value": "Buy oat milk" },
  { "op": "remove",  "path": "/notes/archived" }
]

Notes:

- Ops map one-to-one to the canonical form
- `copy`/`move`/`test` may be rejected by fallback executor if adapter pushdown is unavailable

Developer composition example (C#):

```csharp
using Microsoft.AspNetCore.JsonPatch;
using Microsoft.AspNetCore.JsonPatch.Operations;

var doc = new JsonPatchDocument<Todo>();
doc.Operations.Add(new Operation<Todo>("replace", "/title", from: null, value: "Buy oat milk"));
doc.Operations.Add(new Operation<Todo>("remove", "/notes/archived", from: null));

// Send over HTTP with Content-Type: application/json-patch+json
// or normalize to canonical PatchOps (recommended for transport-agnostic flows)
```

### RFC 7386 (application/merge-patch+json)

Example request:

PATCH /api/todos/{id}
Content-Type: application/merge-patch+json

{ "title": "Buy oat milk", "notes": { "archived": null } }

Normalization:

- `replace /title "Buy oat milk"`
- `remove /notes/archived` (applied as null assignment)

Null semantics:

- For non-nullable targets, `null` maps to `default(T)` by default (configurable)

### Partial JSON (application/json)

Example request (default policy SetNull):

PATCH /api/todos/{id}
Content-Type: application/json

{ "title": null, "priority": 2 }

Normalization:

- `replace /title null`
- `replace /priority 2`

Policy:

- `PartialJsonNullPolicy` controls whether `null` sets null, is ignored, or rejected

Per-request override examples:
- `PATCH /api/todos/123?partialNulls=ignore` with body `{ "title": null }` → null ignored; `title` unchanged.
- `PATCH /api/todos/123?mergeNulls=reject` with body `{ "count": null }` (merge-patch) → reject when `count` is non-nullable.

### Options and policies

- KoanWebOptions controls merge/partial JSON behavior:
  - `MergePatchNullsForNonNullable`: SetDefault | Reject
  - `PartialJsonNulls`: SetNull | Ignore | Reject
- Arrays are replaced by default for merge and partial JSON

---

## Usage patterns: Direct model (in-process)

When patching inside services/hosts (no transport), prefer the entity-first helpers and the canonical PatchOps model through the Data facade. Adapters may push down to the store; if not, Koan applies the operations in-process.

### Build operations

Pseudocode (shape of the canonical model):

- PatchPayload<TKey>
  - Id: entity id (e.g., string)
  - Ops: list of PatchOp { op, path, from?, value? }
  - Options: PatchOptions { MergePatchNullPolicy, PartialJsonNullPolicy, ArrayBehavior }

Example (replace title and remove a nested flag):

// inside a domain service
var payload = new PatchPayload<string>
{
    Id = todoId,
    Ops =
    [
        new PatchOp("replace", "/title", value: "Buy oat milk"),
        new PatchOp("remove", "/notes/archived")
    ],
    Options = PatchOptions.Default
};

await Data<Todo, string>.PatchAsync(payload, ct);

Notes:

- Prefer `Entity<T>` statics for common cases:
  - `await Todo.Patch(id, new { Title = "Buy oat milk", Note = (string?)null });` // partial JSON (null → null by default)
  - `await Todo.PatchMerge(id, new { Title = "Buy oat milk", Count = (int?)null });` // merge-patch (null → default by default)
- Otherwise use `Data<TEntity, TKey>.PatchAsync(...)` with the canonical payload directly.
- Identity `/id` is immutable and guarded.
- On fallback, `remove` translates to null assignment for reference/nullable targets.

### RFC 6902 in-process

You can also use `JsonPatchDocument<TEntity>` to compose RFC 6902 and execute in-process via the applicator when appropriate. Prefer the canonical ops form for transport-agnostic code paths.

---

## Edge cases and guidance

1. Arrays replace: merge/partial JSON treat arrays as replace operations; plan accordingly.
2. Case-insensitive pointers: JSON Pointer resolution is case-insensitive and creates missing objects when normalizing.
3. Unsupported ops: `copy`/`move`/`test` may require adapter pushdown; fallback executor will reject with 400.
4. Concurrency: add ETag/If-Match once available to avoid lost updates; not enforced by default.
5. Hooks and transformers: patches run through lifecycle hooks; response shaping honors configured transformers.

---

## Minimal controller usage

```csharp
[Route("api/[controller]")]
public class TodosController : EntityController<Todo>
{
    // Inherit PATCH support; override only when you need to add custom behavior
}
```

This controller consumes all three PATCH formats as documented above. Prefer controller-based HTTP routes; avoid inline `MapGet`/`MapPost`/`MapPatch` in startup.

---

## Troubleshooting

- 400 Invalid Pointer: check `path` and JSON Pointer encoding; remember to escape `~` and `/` in segments.
- 409 Identity Mutation: don’t modify `/id`. Use PUT for full replacement with preserved id.
- 415 Unsupported Media Type: ensure Content-Type matches one of the supported PATCH formats.
- 501/409 Adapter Capability: some providers can’t push down patches; Koan falls back to in-process or returns an error depending on adapter.

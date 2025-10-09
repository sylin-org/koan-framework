---
id: DATA-0077
slug: DATA-0077-canonical-patch-operations
domain: DATA
status: Accepted
date: 2025-10-09
---

# DATA-0077: Canonical patch operations and multi-format normalization

Date: 2025-10-09

Status: Accepted

## Context

Koan previously supported only RFC 6902 (application/json-patch+json) on entity PATCH endpoints via JsonPatchDocument. We want:

- A single, transport-agnostic patch contract that works over HTTP and non-HTTP (e.g., MQ, jobs) without leaking web-specific types
- First-class support for three PATCH formats at the HTTP edge:
  1) application/json (partial JSON)
  2) application/merge-patch+json (RFC 7386)
  3) application/json-patch+json (RFC 6902)
- Clear and configurable null semantics for merge/partial
- Provider transparency with optional pushdown where adapters can apply patch ops atomically

## Decision

Adopt a canonical patch block built from a simple envelope plus a list of operations, and normalize all inbound PATCH formats to this internal form.

Contract (canonical):

- PatchPayload<TKey>
  - id: TKey
  - set?: string (partition)
  - etag?: string (If-Match concurrency)
  - kindHint?: json-patch | merge-patch | partial-json | normalized
  - options?:
    - mergeNulls: SetDefault | Reject (default SetDefault)
    - partialNulls: SetNull | Ignore | Reject (default SetNull)
    - arrayBehavior: Replace (default)
  - ops: PatchOp[]

- PatchOp
  - op: add | remove | replace | move | copy | test
  - path: string (JSON Pointer)
  - from?: string (for move/copy)
  - value?: JSON (opaque JSON value)

Normalization at the HTTP edge:

- JSON Patch (6902): map 1:1 to ops.
- Merge Patch (7386):
  - null → remove op at the property path
  - object → recurse
  - primitive/array → replace op at path (arrays replace whole node)
  - Non-nullable with null: resolved by policy at execution (default SetDefault)
- Partial JSON (application/json):
  - object → recurse
  - primitive/array → replace op at path (arrays replace whole node)
  - null handling controlled by policy (default SetNull)

Execution:

- Primary API: `Data<TEntity, TKey>.PatchAsync(...)` consumes the canonical patch payload (or a close equivalent) and applies it.
  - If the underlying adapter supports patch ops execution (capability), push down the ops.
  - Otherwise, fallback to read-modify-upsert: `Get → apply → Upsert`.
  - Hooks (OnBeforePatch/OnAfterPatch) are invoked with the canonical block.
  - Identity (Id) is immutable by default; attempts to change `/id` are rejected. Identity mutation, if ever supported, will be a separate explicit operation with capability gating.

HTTP controller surface:

- Expose one route with three [Consumes] variants:
  - application/json → partial JSON
  - application/merge-patch+json → RFC 7386
  - application/json-patch+json → RFC 6902
- The controller normalizes to the canonical ops list and delegates to `Data<TEntity, TKey>.PatchAsync`.

Policies (KoanWebOptions):

- `MergePatchNullsForNonNullable`: SetDefault (default) or Reject
- `PartialJsonNulls`: SetNull (default), Ignore, or Reject

## Consequences

Positive:
- One ubiquitous patch block for HTTP/MQ/Jobs; simple for hooks and tests
- Standards-compliant at the edge while remaining provider-agnostic internally
- Adapters can optionally implement atomic patch ops; others automatically fallback
- Clear, configurable null semantics; arrays are consistent (replace by default)

Negative/risks:
- JSON Patch `test` op requires atomic semantics to be meaningful; if unsupported by adapter, it should fail clearly (capability advertised)
- Identity mutation is deliberately excluded from PATCH to avoid integrity pitfalls; requires explicit rename command if needed

## Contract block (summary)

Inputs: PatchPayload<TKey> with ops; options for null behavior; optional ETag; optional partition set
Outputs: Updated entity (or shaped payload at HTTP), or null if not found
Errors: 400 malformed; 404 missing; 409 on identity-change attempts; 412 ETag mismatch (when used);
415 unsupported media type; 422 validation errors
Success criteria: ops applied per semantics; hooks/transformers run; headers populated

## Edge cases

1) Non-nullable with merge null: defaults to default(T) (configurable)
2) Arrays: always replaced in merge/partial; use 6902 for granular array ops
3) Dictionaries: merge-patch null removes key; partial-json follows null policy
4) JSON Patch `test`: only valid when adapter can atomically evaluate + apply
5) Attempts to patch `/id`: rejected by default

## References

- DATA-0061: Data access pagination and streaming
- DATA-0050/0051: Instruction name constants and direct routing
- ARCH-0061: JSON layer unification on Newtonsoft
- WEB-0035: EntityController transformers

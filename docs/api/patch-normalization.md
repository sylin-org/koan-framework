---
type: REF
domain: web
title: "PATCH Formats and Normalization"
audience: [developers, architects, ai-agents]
status: current
last_updated: 2025-10-09
framework_version: v0.6.3
validation:
  status: verified
  date_last_tested: 2025-10-09
  scope: docs/api/patch-normalization.md
---

# PATCH formats and normalization

## Contract

- Inputs:
  - HTTP PATCH on `EntityController<TEntity, TKey>` accepting one of:
    - application/json-patch+json (RFC 6902)
    - application/merge-patch+json (RFC 7386)
    - application/json (partial JSON)
- Output: Updated entity model (subject to hooks and shaping) or appropriate error (404, 409, 400).
- Error modes:
  - Invalid JSON Pointer → 400
  - Identity mutation attempt (/id) → 409
  - Route/body id mismatch (when body contains id) → 400 `web.patch.idMismatch`
  - Unsupported op for fallback executor (copy/move/test) → 400
- Success criteria:
  - Payload is normalized to canonical PatchOps; options (null/array policies) applied.

## Canonical normalization

All PATCH formats are normalized to a canonical PatchPayload<TKey> with PatchOp list:

- RFC 6902: one-to-one mapping of ops.
- RFC 7386: object recursion; null → remove (applied as null assignment); arrays replaced.
- Partial JSON: object recursion; null handling via PartialJsonNullPolicy; arrays replaced.

Options are populated from KoanWebOptions:

- MergePatchNullsForNonNullable (default: SetDefault)
- PartialJsonNulls (default: SetNull)

Per-request overrides (querystring):
- `nulls=default|null|ignore|reject`
- or granular: `mergeNulls=default|reject`, `partialNulls=null|ignore|reject`
See `Koan.Web.Infrastructure.KoanWebConstants.Query`.

See ADR DATA-0077 for the canonical model.

## Samples

- RFC 7386
  Content-Type: application/merge-patch+json
  Body: { "name": "B", "sub": { "note": null } }

  Normalized ops:
  - replace /name "B"
  - remove /sub/note

- Partial JSON
  Content-Type: application/json
  Body: { "name": null }

  Normalized ops (default policy SetNull):
  - replace /name null

## Notes

- Identity (/id) is immutable via PATCH.
- Large patches should consider streaming via Data layer instructions.
- Providers may push down patch execution; Koan falls back to in-process applicators/executor when needed.
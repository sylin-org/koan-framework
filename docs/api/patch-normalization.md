---
type: REFERENCE
domain: web
title: "PATCH Formats and Normalization"
audience: [developers, architects, ai-agents]
status: current
last_updated: 2025-10-09
framework_version: v1.0.0
validation:
  status: verified
  date_last_tested: 2025-10-09
  scope: docs/api/patch-normalization.md
---

# PATCH formats and normalization

`EntityController<TEntity, TKey>` accepts PATCH in three media types — `application/json-patch+json`
(RFC 6902), `application/merge-patch+json` (RFC 7386), and a partial `application/json` body — and
normalizes all three to one canonical `PatchOps` list before applying null and array policy. A
successful request returns the updated model, shaped by the same hooks and transformers as any other
write.

| Condition | Status |
|---|---|
| Invalid JSON Pointer | `400` |
| Attempt to mutate `/id` | `409` |
| Route id and body id disagree | `400` `web.patch.idMismatch` |
| `copy` / `move` / `test` under the fallback executor | `400` |

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

See [DATA-0116](../decisions/DATA-0116-canonical-patch-operations.md) for the canonical model.

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
- For large selections, use explicit paging or an Entity stream whose adapter advertises
  `DataCaps.Query.ProviderBoundedPaging`; patch normalization does not create a bounded source.
- Providers may push down patch execution; Koan falls back to in-process applicators/executor when needed.

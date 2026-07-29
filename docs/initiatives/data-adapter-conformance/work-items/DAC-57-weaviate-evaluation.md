---
type: SPEC
domain: data
title: "DAC-57 Evaluate and Certify the Weaviate Adapter"
audience: [architects, maintainers, developers, ai-agents]
status: draft
last_updated: 2026-07-27
framework_version: v0.20.0
validation:
  date_last_tested: 2026-07-27
  status: reviewed
  scope: Weaviate adapter evaluation prompt
---

# DAC-57 — Evaluate and certify the Weaviate adapter

| Field | Value |
|---|---|
| Phase / kind | vector / audit-certification |
| Depends on | DAC-53 |
| Primer scope | dynamically selected Source Core, Source Integration, and Vector manifest |
| Production writes | forbidden |
| Owner | Adapter(Weaviate) |

## Meaningful outcome

Weaviate connects Koan to pre-existing or managed vector collections without hidden vectorization, schema mutation, or
provider-specific result surprises.

## Execute

1. Pin image/digest and client, disable automatic vectorization for the deterministic fixture, configure identities,
   create an isolated corpus, and initialize `evidence/weaviate/`.
2. Audit class/collection naming and description, schema/dimensions, object/vector/value encoding, CRUD/batch, filters,
   `topK`, distance/certainty/score mapping, partitions/source axes, consistency, inspection, and health.
3. Prove managed/external/read-only postures. External/read-only execution cannot create classes, add properties,
   change vectorizer/index configuration, repair shape, or delete data.
4. Compare common networked-vector semantics with DAC-53 while preserving explicit provider differences. Establish
   settling rules and evidence for reads after writes/deletes.
5. Exercise auth, missing collection, schema/dimension mismatch, forbidden vectorizer behavior, partial batch,
   cancellation, disconnect/restart, concurrency, disposal, and cold/warm baselines.
6. RED creates scoped remediation cards and blocks; this evaluation cannot edit production.

## Verification

- Strict Forge runs against the pinned service with native request/response, lifecycle-negative, settling, fault,
  restart, and packet evidence.

## Definition of done

- [ ] Weaviate is green for every advertised claim and corrective for every decline.
- [ ] Vectorizer, score, schema, lifecycle, and consistency semantics are unambiguous.
- [ ] External data can be inspected/read without mutation under the configured policy.

## Stop conditions

Automatic vectorization in the fixture, hidden schema repair, unavailable LIVE provider, ambiguous consistency, or
production edits block certification.

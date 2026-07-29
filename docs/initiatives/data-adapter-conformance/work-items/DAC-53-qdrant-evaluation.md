---
type: SPEC
domain: data
title: "DAC-53 Evaluate and Certify the Qdrant Adapter"
audience: [architects, maintainers, developers, ai-agents]
status: draft
last_updated: 2026-07-27
framework_version: v0.20.0
validation:
  date_last_tested: 2026-07-27
  status: reviewed
  scope: Qdrant adapter evaluation prompt
---

# DAC-53 — Evaluate and certify the Qdrant adapter

| Field | Value |
|---|---|
| Phase / kind | vector / audit-certification |
| Depends on | DAC-51 |
| Primer scope | dynamically selected Source Core, Source Integration, and Vector manifest |
| Production writes | forbidden |
| Owner | Adapter(Qdrant) |

## Meaningful outcome

Qdrant supplies the first networked vector boundary: collection lifecycle, similarity, filtering, consistency, and
faults remain faithful under a real service rather than an in-process substitute.

## Execute

1. Pin Qdrant image/digest and client; create isolated collections, read/write and read-only identities where the
   provider permits, a deterministic corpus, and `evidence/qdrant/`.
2. Audit collection naming/description, dimensions, distance configuration, payload/value encoding, upsert/get/delete,
   batch behavior, filters, `topK`, scores, pagination where exposed, partitions/source axes, and health.
3. Prove managed/external and read-only source postures. External inspection may observe but cannot create, repair,
   reconfigure, or delete collections.
4. Establish write visibility/settling rules and receipts; compare the semantic corpus against DAC-51 without requiring
   identical floating-point values or physical plans.
5. Exercise auth failure, missing collection, wrong dimensions/metric, timeout/cancellation, partial batch, disconnect,
   service restart, concurrency, pooled-client disposal, and cold/warm baselines.
6. RED creates bounded remediation cards and blocks; no adapter change is allowed in this card.

## Verification

- Strict Forge runs against the pinned real service with native request/response evidence, fault/restart coverage, and
  a complete packet. Provider unavailability or skipped LIVE cells are not green.

## Definition of done

- [ ] Qdrant is green for every advertised Source/Vector claim.
- [ ] Visibility, score tolerance, collection lifecycle, and failure semantics are explicit.
- [ ] Every decline fails correctively without hidden emulation or mutation.

## Stop conditions

Unpinned service identity, unavailable LIVE provider, ambiguous settling, external-lifecycle mutation, or production
edits block certification.

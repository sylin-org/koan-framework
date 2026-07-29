---
type: SPEC
domain: data
title: "DAC-55 Evaluate and Certify the Elasticsearch Adapter"
audience: [architects, maintainers, developers, ai-agents]
status: draft
last_updated: 2026-07-27
framework_version: v0.20.0
validation:
  date_last_tested: 2026-07-27
  status: reviewed
  scope: Elasticsearch Vector adapter evaluation prompt
---

# DAC-55 — Evaluate and certify the Elasticsearch adapter

| Field | Value |
|---|---|
| Phase / kind | vector / audit-certification |
| Depends on | DAC-54 |
| Primer scope | dynamic Elasticsearch delta over Source Core, SearchEngine, and Vector manifests |
| Production writes | forbidden |
| Owner | Adapter(Elasticsearch); Family rows consumed, not re-certified structurally |

## Meaningful outcome

The Elasticsearch adapter proves its advertised vector behavior against a pinned real cluster, including native KNN,
filtering, refresh visibility, security, lifecycle, and failures.

## Execute

1. Pin the server image/digest, client compatibility, license/security posture, mappings, deterministic corpus, and
   read/write plus least-privilege identities; create `evidence/elasticsearch/`.
2. Freeze the provider-delta manifest. Audit index naming/inspection, dimensions and similarity, mappings, CRUD/bulk,
   KNN query/filter construction, score/result translation, `topK`, paging, partitions/source axes, and health.
3. Prove managed/external/read-only lifecycle and refresh/settling semantics; no external or read-only path may create,
   repair, remap, refresh-for-write, or delete provider state.
4. Capture native requests/responses/plans or profiles sufficient to prove every advertised execution claim.
5. Exercise auth, missing index, mapping/dimension mismatch, unsupported server feature, partial bulk, cancellation,
   disconnect/restart, disposal, concurrency, and cold/warm baselines.
6. RED creates one-owner remediation cards and blocks. Do not repair family or adapter code in this evaluation.

## Verification

- Strict Forge and provider suites run on the pinned cluster with native evidence, fault/restart scenarios, least-
  privilege negatives, and complete packet validation.

## Definition of done

- [ ] All advertised Elasticsearch Source/SearchEngine/Vector claims are green against the real provider.
- [ ] Version, score, refresh, security, lifecycle, and partial-bulk semantics are explicit.
- [ ] Family evidence and provider-delta evidence remain distinguishable and traceable.

## Stop conditions

Version drift, skipped LIVE evidence, implicit index mutation, ambiguous partial outcome, or production edits block PASS.

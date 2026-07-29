---
type: SPEC
domain: data
title: "DAC-56 Evaluate and Certify the OpenSearch Adapter"
audience: [architects, maintainers, developers, ai-agents]
status: draft
last_updated: 2026-07-27
framework_version: v0.20.0
validation:
  date_last_tested: 2026-07-27
  status: reviewed
  scope: OpenSearch Vector adapter evaluation prompt
---

# DAC-56 — Evaluate and certify the OpenSearch adapter

| Field | Value |
|---|---|
| Phase / kind | vector / audit-certification |
| Depends on | DAC-54 |
| Primer scope | dynamic OpenSearch delta over Source Core, SearchEngine, and Vector manifests |
| Production writes | forbidden |
| Owner | Adapter(OpenSearch); Family rows consumed, not re-certified structurally |

## Meaningful outcome

OpenSearch earns its own real-provider verdict rather than inheriting Elasticsearch compatibility assumptions.

## Execute

1. Pin OpenSearch image/digest, client compatibility, security plugin posture, vector engine/index settings, corpus, and
   least-privilege identities; create `evidence/opensearch/`.
2. Freeze and prove the provider delta: index/mapping lifecycle, dimensions, engine/space type, CRUD/bulk, KNN query
   and filters, score/result meaning, `topK`, paging, partitions/source axes, inspection, and health.
3. Exercise managed/external/read-only sources and document refresh/settling rules. Capture native request/response and
   profile evidence rather than treating family-shape tests as provider proof.
4. Exercise auth/plugin failures, missing index, mapping/dimension mismatch, unsupported engine/version, partial bulk,
   cancellation, disconnect/restart, concurrent use, client disposal, and cold/warm performance.
5. Compare only shared semantics with DAC-55; any genuine provider difference becomes an explicit capability/delta,
   never a conditional hidden in shared code.
6. RED creates one-owner remediation cards and blocks; production code is read-only in this card.

## Verification

- Strict Forge and provider suites run against the pinned cluster with native, security, fault, restart, and packet
  evidence. Missing plugin/provider prerequisites produce DEFER/RED, not PASS.

## Definition of done

- [ ] Every advertised OpenSearch claim is green against its own real provider/version.
- [ ] Engine, scoring, refresh, security, and lifecycle distinctions are explicit.
- [ ] No Elasticsearch compatibility assumption stands in for native evidence.

## Stop conditions

Unpinned plugin/version, skipped LIVE evidence, implicit lifecycle mutation, family/provider ownership confusion, or
production edits block certification.

---
type: SPEC
domain: data
title: "DAC-58 Evaluate and Certify the Milvus Adapter"
audience: [architects, maintainers, developers, ai-agents]
status: draft
last_updated: 2026-07-27
framework_version: v0.20.0
validation:
  date_last_tested: 2026-07-27
  status: reviewed
  scope: Milvus heavy-lane adapter evaluation prompt
---

# DAC-58 — Evaluate and certify the Milvus adapter

| Field | Value |
|---|---|
| Phase / kind | vector / audit-certification, heavy lane |
| Depends on | DAC-53 |
| Primer scope | dynamically selected Source Core, Source Integration, and Vector manifest |
| Production writes | forbidden |
| Owner | Adapter(Milvus) |

## Meaningful outcome

Milvus is trustworthy under its real distributed prerequisites, including collection/index/load state, eventual
visibility, component failure, and cleanup—not merely under a happy-path client mock.

## Execute

1. Pin digests/configuration for Milvus, etcd, MinIO, client, and every network/service dependency. Record machine and
   resource budgets, create isolated data, and initialize `evidence/milvus/`.
2. Audit database/collection/partition naming, fields/dimensions, metric/index choice, collection creation, index build,
   load/release state, CRUD/batch, filters, `topK`, score mapping, source axes, inspection, and health.
3. Prove managed/external/read-only behavior across collection, index, and load transitions; no forbidden source may
   trigger background shape/index creation, loading with a mutating side effect, repair, or deletion.
4. Establish visibility/flush/settling and outcome receipts for writes/deletes. Capture native plans/metrics or requests
   proving advertised indexed/vector execution.
5. Exercise wrong dimensions/metric/index, partial batch, cancellation, lost Milvus/etcd/object storage, restart and
   recovery, concurrency, pool/disposal, orphan cleanup, soak, and cold/warm baselines.
6. RED creates one-owner cards and blocks. Infrastructure absence, flaky setup, or skipped scenarios are DEFER/RED.

## Verification

- The pinned multi-service topology runs strict Forge, fault/restart/recovery, load/index-state assertions, leak/cleanup
  checks, and complete packet validation in the heavy CI lane.

## Definition of done

- [ ] Every service, configuration, and client dependency is reproducibly pinned.
- [ ] Index/load/visibility/failure semantics support every advertised claim.
- [ ] Resource cleanup and performance remain within recorded provider-relative budgets.

## Stop conditions

Unpinned infrastructure, absent fault/restart evidence, hidden state transitions, leaked resources, or production edits
block certification.

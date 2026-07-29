---
type: GUIDE
domain: data
title: "Data Adapter Conformance Current Handoff"
audience: [architects, maintainers, developers, ai-agents]
status: current
last_updated: 2026-07-29
framework_version: v0.20.0
validation:
  date_last_tested: 2026-07-29
  status: reviewed
  scope: full implementation inventory and MongoDB gold recovery
---

# Data Adapter Conformance — current handoff

## Current state

All 16 discovered adapters have replacement implementations on
`agent/polymorphic-entity-root-persistence`. Do not start another adapter rewrite. The remaining work is gold closure,
strict evidence emission, deferred provider cells, truth reconciliation, and independent certification.

The nine Entity adapters are SQLite, MongoDB, PostgreSQL, SQL Server, CockroachDB, Couchbase, Redis, InMemory, and
JSON. The seven Vector adapters are InMemory, SqliteVec, Qdrant, Elasticsearch, OpenSearch, Weaviate, and Milvus.
`Koan.Data.SearchEngine` was retired after the rebuilt Elasticsearch and OpenSearch adapters proved there was no
smaller shared semantic owner.

SQLite is the first sealed gold replacement: 47/47 SQLite, 16/16 Relational, 471/471 Data Core, and the greenfield
source/hash gate pass. Web rebuild, stable performance evidence, strict packet, and independent certification remain
deferred.

MongoDB was already clean-room rebuilt in `5cf55ab3ab04847d61d6ee1e089c084a76df8f61`; a redundant restart was fully
discarded. Recovery restored every source file byte-identically and the existing exact-source binary passed all 34
cases against a fresh MongoDB 8.3.4 container. Its greenfield gate passes with 23 source items, nine justified moving
parts, and 11 retired implementation paths. A fresh build is not claimed because external NuGet restore permission was
denied.

PostgreSQL passes 26/26 live, SQL Server 33/33, CockroachDB 17/17, Couchbase 25/25, Redis 17/17, InMemory Entity
56/56, and JSON Entity 34/34. Each still lacks a strict versioned adapter packet and some provider-specific deferred
cells recorded in its card.

The Vector fleet passes its behavioral ledgers: InMemory 50/50; SqliteVec 58 with five declared skips; Qdrant,
Elasticsearch, OpenSearch, and Weaviate 28/28 live; Milvus three separate 28/28 live passes. SqliteVec native RID
coverage and strict packets remain deferred.

## Latest validation

| Gate | Result |
|---|---|
| Workspace recovery | clean MongoDB source restored from pushed `HEAD`; branch was synchronized before evidence edits |
| MongoDB live recovery | exit 0 against fresh MongoDB 8.3.4; 34/34, zero provider skips |
| MongoDB greenfield integrity | PASS: 23 source items, nine moving parts, 11 retired paths; all source hashes match |
| MongoDB fresh build | not run; NuGet restore permission denied |
| SQLite gold replacement | 47/47 SQLite; 16/16 Relational; 471/471 Core; greenfield/hash gates PASS |
| Entity fleet | every discovered adapter has replacement code and recorded provider behavior |
| Vector fleet | every discovered adapter has replacement code and recorded vector behavior |
| Strict certification | DEFERRED: packet compiler/validator exist, but no adapter-evidence emission command produces `conformance.json` |

## Next action

Close DAC-21 without rewriting MongoDB again:

1. Obtain a fresh build and run its shared Data/Web regressions when package access is authorized.
2. Audit the committed adapter against the primer and change production code only for an executable failure.
3. Add the smallest shared command that emits a versioned packet from real adapter evidence; do not create a second
   semantic catalog or adapter-local harness.
4. Run strict SQLite and MongoDB packets, then complete DAC-13, DAC-24, DAC-23, DAC-12, DAC-22, and DAC-30.
5. Emit packets and close deferred cells for the remaining fleet before DAC-90 and DAC-99.

## Guardrails

- All adapter implementation roots have already been rebuilt; assess and correct them, do not restart them by default.
- Reuse framework contracts, not retired adapter structure or control flow.
- Add a shared abstraction only when rebuilt providers demonstrate identical meaning and lifecycle.
- Production changes require a failing provider or gold-contract case.
- Prefer fewer meaningful runtime parts; do not grow certification scaffolding inside adapter cards.
- Real provider cells run live; unavailable infrastructure is a skip or defer, never a pass.
- Unsupported behavior fails before mutation or unbounded fallback.
- Strict certification remains deferred until an authentic versioned packet is emitted and validated.

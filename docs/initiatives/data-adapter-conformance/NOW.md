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
  scope: full implementation inventory and MongoDB gold closure
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

MongoDB is the active second gold replacement. Its clean-room implementation has a fresh zero-warning offline build
and passes 40/40 cases against MongoDB 8.3.4. Canonical Forge executes 11 exact rows, including provider-specific D-01
through D-05. The D-05 proof corrected bounded inspection to preserve legal duplicate BSON element names, field order,
missing versus null, and structured values. Its greenfield gate passes with 23 source items, nine justified moving
parts, 11 retired implementation paths, and matching hashes for every exported source.

PostgreSQL passes 26/26 live, SQL Server 33/33, CockroachDB 17/17, Couchbase 25/25, Redis 17/17, InMemory Entity
56/56, and JSON Entity 34/34. Each still lacks a strict versioned adapter packet and some provider-specific deferred
cells recorded in its card.

The Vector fleet passes its behavioral ledgers: InMemory 50/50; SqliteVec 58 with five declared skips; Qdrant,
Elasticsearch, OpenSearch, and Weaviate 28/28 live; Milvus three separate 28/28 live passes. SqliteVec native RID
coverage and strict packets remain deferred.

## Latest validation

| Gate | Result |
|---|---|
| Workspace | branch synchronized before this slice; current MongoDB inspection/Forge slice verified before commit |
| MongoDB live suite | PASS against MongoDB 8.3.4: 40/40, zero provider skips |
| MongoDB Forge | GREEN: 11/11 exact rows, including D-01–D-05 provider facts |
| MongoDB greenfield integrity | PASS: 23 source items, nine moving parts, 11 retired paths; all source hashes match |
| MongoDB fresh build | PASS from installed offline package cache; zero warnings and zero errors |
| SQLite gold replacement | 47/47 SQLite; 16/16 Relational; 471/471 Core; greenfield/hash gates PASS |
| Entity fleet | every discovered adapter has replacement code and recorded provider behavior |
| Vector fleet | every discovered adapter has replacement code and recorded vector behavior |
| Strict certification | DEFERRED: packet compiler/validator exist, but no adapter-evidence emission command produces `conformance.json` |

## Next action

Continue closing DAC-21 from executable primer evidence:

1. Bind the next unproved MongoDB rows to focused real-provider or framework-owned facts, preserving evidence ownership.
2. Run the relevant shared Data/Web regressions that are executable from the installed package cache.
3. Add only the smallest shared seam needed to emit a versioned packet from authentic Forge evidence; do not create a
   second semantic catalog or adapter-local harness.
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

---
type: REFERENCE
domain: data
title: "Qdrant Conformance Identity"
audience: [architects, maintainers, developers, ai-agents]
status: draft
last_updated: 2026-07-28
framework_version: v0.20.0
validation:
  date_last_tested: 2026-07-28
  status: behavior-green-strict-deferred
  scope: Qdrant live-run identity
---
# Qdrant identity

| Identity | Value |
|---|---|
| Provider | Qdrant REST |
| Runtime | `qdrant/qdrant:v1.18.3` |
| Fixture | one assembly-scoped Testcontainers instance; stable REST binding; collection reset between cells |
| Vector profile | V-01 through V-24 and G-09 declarations/row/container/database |
| Visibility | Session through awaited `wait=true` mutations |
| Source policies | Managed/read-write, ReadOnly, External, routed database sources |
| Metrics | Cosine, Euclidean, DotProduct |
| Execution date | 2026-07-28 |
| Source checkpoint | commit containing this packet; strict immutable identity deferred until `conformance.json` generation |

The test process used a real Docker provider. No unavailable-provider skip was counted as passing.

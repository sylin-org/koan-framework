---
uid: reference.modules.Koan.data.weaviate
title: Koan.Data.Weaviate — Technical Reference
description: Weaviate adapter for Koan vector data.
since: 0.2.x
packages: [Sylin.Koan.Data.Weaviate]
source: src/Koan.Data.Weaviate/
---

## Contract
- Adapter integrating Weaviate with Koan.Data.Vector facade
- Save/search embeddings for entities with provider-specific options

## Schema and index lifecycle
- Class creation and mapping rules; reindexing considerations
- Backfill guidance when enabling vectors for existing entities

## Search parameters
- `topK` as primary selector
- Distance metric and optional thresholds (provider-dependent)

## Operations
- Health: Weaviate readiness
- Metrics: request latency, error rates
- Logs: redact embeddings and sensitive metadata

## References
- Data access patterns: `/docs/guides/data/all-query-streaming-and-pager.md`

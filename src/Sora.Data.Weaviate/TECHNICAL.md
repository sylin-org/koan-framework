---
uid: reference.modules.sora.data.weaviate
title: Sora.Data.Weaviate — Technical Reference
description: Weaviate adapter for Sora vector data.
since: 0.2.x
packages: [Sylin.Sora.Data.Weaviate]
source: src/Sora.Data.Weaviate/
---

## Contract
- Adapter integrating Weaviate with Sora.Data.Vector facade
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

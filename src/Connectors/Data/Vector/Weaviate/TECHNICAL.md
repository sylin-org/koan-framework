---
uid: reference.modules.Koan.data.weaviate
title: Koan.Data.Vector.Connector.Weaviate - Technical Reference
description: Weaviate adapter for Koan vector data.
since: 0.2.x
packages: [Sylin.Koan.Data.Vector.Connector.Weaviate]
source: src/Koan.Data.Vector.Connector.Weaviate/
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

## Layered discovery

- The adapter declares its Zen Garden offering binding without activating Zen Garden.
- Referencing and activating `Koan.ZenGarden` adds a health-checked automatic candidate through the
  shared discovery coordinator; without that engine, Weaviate keeps its normal local/orchestrated path.
- Concrete configuration remains authoritative over Aspire, activated contributors, and topology guesses.
- Weaviate owns endpoint normalization and health; Zen Garden contributes but never elects.

## References

- Data access patterns: `/docs/guides/data/all-query-streaming-and-pager.md`
- [ARCH-0114 layered capability activation](../../../../docs/decisions/ARCH-0114-layered-capability-activation.md)


---
id: DATA-0063
slug: ranges-as-canonical-and-materialized-model
domain: Data
status: accepted
date: 2025-08-31
title: Ranges as canonical (policy-free) and Materialized as single-valued model
---

## Context

Current canonical is a transport-normalized store (facts), not the business model. We want a policy-free, model-shaped canonical and a policy-resolved, single-valued read model.

## Decision

- Canonical becomes "ranges": model-shaped JSON with arrays for each attribute (policy-free, lossless).
- Add "materialized": single-valued per attribute, resolved via policies (First/Last/Custom), stamped with metadata.
- Derivation order: intake → keyed → facts/patch merge → ranges → materialized (+ analytics).
- Reuse Sora.Core.Json: JsonPathMapper for flatten/expand, JsonMerge for layering, canonical JSON helpers.

## Scope

- Flow projections write to `flow.views.ranges` and `flow.views.materialized` per model base.
- Controllers may expose materialized as default read; ranges for audit/filtering.

## Consequences

- Clear separation: facts vs canonical ranges vs materialized model.
- Extra storage/writes but simpler consumers and explainable policies.
- Indexing must be selective for nested arrays.

## Implementation notes

- Extend merge with primitive-array SetUnion or post-merge dedupe.
- Add minimal materializer with per-path policy registry and provenance metadata.
- Idempotent upserts keyed by canonical id.

## Follow-ups

- Add targeted indexes for materialized.
- Document policy registry and examples.

## References

- ARCH-0052 core IDs and JSON merge policy
- DATA-0061 data access pagination and streaming
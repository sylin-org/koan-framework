# AI-0006 - Data formats and grounding (Parquet, JSON-LD, Schema.org)

Status: Proposed
Date: 2025-08-19
Owners: Koan Data

## Context

Data mobility and knowledge grounding enhance AI scenarios. We need practical, low-friction formats and semantics without heavy runtime cost.

## Decision

- Parquet is supported in materialization jobs (D5) and optionally in snapshot export (D1) via pluggable codec.
- JSON-LD manifests accompany exports; Schema.org types are recommended where applicable for grounding.

## Consequences

- Keep Parquet behind an optional dependency; provide counts/checksums and schema manifests.
- Guidance docs show minimal JSON-LD contexts and Schema.org mappings for common entities.

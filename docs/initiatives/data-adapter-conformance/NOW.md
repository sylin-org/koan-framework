---
type: GUIDE
domain: data
title: "Data Adapter Conformance Current Handoff"
audience: [architects, maintainers, developers, ai-agents]
status: current
last_updated: 2026-07-28
framework_version: v0.20.0
validation:
  date_last_tested: 2026-07-28
  status: reviewed
  scope: vector adapter rebuild lane handoff
---

# Data Adapter Conformance — current handoff

## Current state

The framework Vector contract is ratified in V-01 through V-24 plus G-09. Three adapters have now been rebuilt from
empty implementation roots:

- DAC-51 InMemory Vector: behavioral suite green; strict packet generation deferred;
- DAC-52 SqliteVec: behavioral suite green; native RID matrix and strict packet generation deferred;
- DAC-53 Qdrant: behavioral suite green against live `qdrant/qdrant:v1.18.3`; strict packet generation deferred.

DAC-53 replaces the legacy adapter with one plan-bound repository, one REST boundary, one native filter writer, a
source-aware route, and compact activation/health/discovery code. It deletes provider-owned shape, collection, wait,
field-name, on-disk, and quantization decisions. `VectorSpacePlan` owns dimensions, metric, name, model, source, and
visibility; `DataSourcePlan` owns lifecycle/access; Qdrant options own placement, credentials, and bounded budgets.

## Latest validation

| Gate | Result |
|---|---|
| Qdrant live Vector ledger | 28/28 passed, zero skipped |
| Filter convergence | every declared operator and boolean composition converges with the neutral oracle |
| Metric normalization | cosine, Euclidean, and dot product proven finite, monotonic, higher-is-closer |
| Data Core Vector regression | 24/24 passed |
| InMemory Vector regression | 50/50 passed |
| SqliteVec regression | 58 passed; five deliberate capability skips unchanged |
| Solution build | zero warnings, zero errors |
| Strict Forge | every Qdrant row passed; final status DEFERRED for missing versioned `conformance.json` |
| Documentation lint | zero errors; repository warning backlog remains non-blocking |

The strict packet is not synthesized locally. DAC-51 through DAC-53 remain `in-progress` in the certification ledger
until the shared packet-generation control plane exists.

## Next action

Execute [DAC-54](work-items/DAC-54-searchengine-family-audit.md): inspect the shared SearchEngine seam only far
enough to decide whether Elasticsearch and OpenSearch genuinely share a minimal contract. Preserve provider-specific
query, mapping, lifecycle, and failure semantics. Then rebuild Elasticsearch under DAC-55 and OpenSearch under DAC-56
from empty roots, using existing code only to harvest provider facts and failure modes.

## Guardrails

- Begin every adapter with the primer and an empty implementation root.
- Reuse framework contracts, not legacy adapter structure or control flow.
- Add a shared abstraction only when two rebuilt providers demonstrate the same stable responsibility.
- Keep schema, metric, lifecycle, visibility, and source decisions with their existing Koan owners.
- Prefer fewer meaningful runtime parts; do not grow certification scaffolding inside adapter cards.
- Real provider cells must run live; unavailable infrastructure is a skip or defer, never a pass.
- Unsupported behavior fails before mutation or unbounded fallback.
- Keep strict packet absence visible until the shared control plane can produce a versioned artifact.

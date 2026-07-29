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

The ratified Vector contract is V-01 through V-24 plus G-09. Four adapters have been rebuilt from empty implementation
roots:

- DAC-51 InMemory Vector: behavioral suite green; strict packet generation deferred;
- DAC-52 SqliteVec: behavioral suite green; native RID matrix and strict packet generation deferred;
- DAC-53 Qdrant: 28/28 live against `qdrant/qdrant:v1.18.3`; strict packet generation deferred;
- DAC-55 Elasticsearch: 28/28 live against Elasticsearch 9.4.3; strict packet generation deferred.

Elasticsearch is now independent of the retired SearchEngine execution design. Its application contract comes from
`VectorSpacePlan` and `DataSourcePlan`; its options contain placement, credentials, timeout, and bounded-work controls
only. The implementation has one source-aware route, one bounded client, one native filter compiler, and one
plan-bound repository.

## Latest validation

| Gate | Result |
|---|---|
| Elasticsearch live Vector ledger | 28/28 passed, zero skipped, 17 seconds |
| Filter convergence | every declared operator and boolean composition converges with the neutral oracle |
| Metric normalization | Cosine, Euclidean, and DotProduct are finite, monotonic, and higher-is-closer |
| Data Core Vector regression | 24/24 passed |
| InMemory Vector regression | 50/50 passed |
| SqliteVec regression | 58 passed; five deliberate capability skips unchanged |
| Solution build | zero warnings, zero errors |
| Documentation lint | zero errors; 1,472 existing warnings remain non-gating |
| Strict packet | deferred because the shared versioned packet generator is absent |

Behavior is proven, but no local substitute for the strict packet is accepted. DAC-51, DAC-52, DAC-53, and DAC-55
remain `in-progress` in the certification ledger until the shared control plane can emit the required artifact.

## Next action

Execute DAC-56: rebuild OpenSearch from an empty implementation root against its pinned native behavior. Do not copy
the Elasticsearch repository or preserve the current SearchEngine/OpenSearch control flow. After both independent
providers are green, compare only their proven mechanical responsibilities; share nothing unless the repeated seam is
smaller and clearer than two provider-owned implementations. Retire `Koan.Data.SearchEngine` only after OpenSearch no
longer consumes it and repository references prove that it has no remaining owner.

## Guardrails

- Begin every adapter with the primer and an empty implementation root.
- Reuse framework contracts, not legacy adapter structure or control flow.
- Add a shared abstraction only when two rebuilt providers demonstrate the same stable responsibility.
- Keep schema, metric, lifecycle, visibility, and source decisions with their existing Koan owners.
- Prefer fewer meaningful runtime parts; do not grow certification scaffolding inside adapter cards.
- Real provider cells must run live; unavailable infrastructure is a skip or defer, never a pass.
- Unsupported behavior fails before mutation or unbounded fallback.
- Keep strict packet absence visible until the shared control plane produces a versioned artifact.

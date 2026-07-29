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
  scope: Entity adapter rebuild lane handoff after DAC-40
---

# Data Adapter Conformance — current handoff

## Current state

The ratified Vector contract is V-01 through V-24 plus G-09. Seven adapters have been rebuilt from empty
implementation roots:

- DAC-51 InMemory Vector: behavioral suite green; strict packet generation deferred;
- DAC-52 SqliteVec: behavioral suite green; native RID matrix and strict packet generation deferred;
- DAC-53 Qdrant: 28/28 live against `qdrant/qdrant:v1.18.3`; strict packet generation deferred;
- DAC-55 Elasticsearch: 28/28 live against Elasticsearch 9.4.3; strict packet generation deferred;
- DAC-56 OpenSearch: 28/28 live against OpenSearch 3.7.0; strict packet generation deferred.
- DAC-57 Weaviate: 28/28 live against Weaviate 1.37.6; strict packet generation deferred.
- DAC-58 Milvus: three 28/28 live passes against Milvus 2.6.20 with pinned etcd and MinIO; strict packet generation deferred.

The Entity fleet has resumed with DAC-40 InMemory. Its old execution root is gone; one bounded host state and one thin
KeyValue repository now store detached, root-aware snapshots. The live-in-process ledger passes 56/56, with Data Core
471/471, JSON 28/28, and live Redis 17/17 regressions green. Strict packet generation remains deferred.

Elasticsearch and OpenSearch now independently realize the same Koan vector decisions through native mappings,
queries, scores, failures, and source policy. Comparing the proven implementations found no provider-neutral runtime
whose extraction would be smaller or clearer than the two provider-owned paths. `Koan.Data.SearchEngine` therefore had
no remaining owner and was deleted from source, solution membership, and the package/product surface.

## Latest validation

| Gate | Result |
|---|---|
| Milvus live Vector ledger | 28/28 passed three times, zero skipped, fresh pinned topology each pass |
| Vector regressions | Data Core 24/24; InMemory 50/50; SqliteVec 58 plus five deliberate skips |
| Filter convergence | every declared operator and boolean composition converges with the neutral oracle |
| Metric normalization | Cosine, Euclidean, and DotProduct are finite, monotonic, and higher-is-closer |
| SearchEngine retirement | no production, solution, claim, or current package-inventory owner remains |
| Strict packet | deferred because the shared versioned packet generator is absent |
| InMemory Entity | 56/56; detached graphs, root/variant round trips, exact source/partition isolation, finite host state |
| KeyValue regressions | Data Core 471/471; JSON 28/28; live Redis 17/17 |

Behavior is proven, but no local substitute for the strict packet is accepted. DAC-51, DAC-52, DAC-53, DAC-55,
DAC-56, DAC-57, and DAC-58 remain `in-progress` in the certification ledger until the shared control plane can emit the
required artifact.

## Next action

Proceed with DAC-41 JSON as the next dependency-ready Entity adapter. Empty its provider-owned implementation root,
retain only stable provider facts and the proven KeyValue family contract, and use DAC-40 as a behavioral oracle—not as
code to copy. Do not enter DAC-90 while any discovered adapter card remains pending.

## Guardrails

- Begin every adapter with the primer and an empty implementation root.
- Reuse framework contracts, not legacy adapter structure or control flow.
- Add a shared abstraction only when rebuilt providers demonstrate the same stable responsibility.
- Keep schema, metric, lifecycle, visibility, and source decisions with their existing Koan owners.
- Prefer fewer meaningful runtime parts; do not grow certification scaffolding inside adapter cards.
- Real provider cells must run live; unavailable infrastructure is a skip or defer, never a pass.
- Unsupported behavior fails before mutation or unbounded fallback.
- Keep strict packet absence visible until the shared control plane produces a versioned artifact.

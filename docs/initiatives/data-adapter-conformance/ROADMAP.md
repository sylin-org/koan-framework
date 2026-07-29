---
type: ARCHITECTURE
domain: data
title: "Data Adapter Conformance Roadmap"
audience: [architects, maintainers, developers, ai-agents]
status: draft
last_updated: 2026-07-27
framework_version: v0.20.0
validation:
  date_last_tested: 2026-07-27
  status: reviewed
  scope: dependency graph, work-item decomposition, and phase exits
---

# Data Adapter Conformance Roadmap

This file records dependency order. Live status belongs only in [PROGRESS.md](PROGRESS.md).

## Dependency graph

```text
DAC-00 roster and packet bootstrap
  -> DAC-01 Koan.Data public-surface audit
      -> DAC-02 contract/API decision gate [human approval]
          -> DAC-14 retire source-quarantine scaffolding; freeze lean rewrite boundary
              -> DAC-03 executable conformance control plane
                  -> DAC-49 Vector annex [human approval]
                      -> DAC-50 Vector control plane and stable shared runner
                          -> DAC-04 source policy, routing, readiness, and failure ownership
                              -> DAC-05 Entity/query/bulk/stream semantics
                                  -> DAC-06 inspection, RecordSet, and registered operations
                                      -> DAC-07 mapping compiler and relational family substrate
                                          -> DAC-08 diagnostics, claims, lifecycle, and performance
                                              -> DAC-09 independent framework gate
                                                  -> DAC-10 harvest SQLite lessons and contract facts
                                                  -> DAC-20 harvest MongoDB lessons and contract facts
                                                      DAC-10 + DAC-20 -> DAC-15 greenfield rewrite gate [human claims gate]
                                                          -> linked Framework/Family contract completion
                                                          -> empty SQLite and MongoDB replacement roots
                                                          -> DAC-11 SQLite ground-up authoring
                                                              -> DAC-13 SQLite packaging/archaeology
                                                          -> DAC-21 MongoDB ground-up authoring
                                                              -> DAC-24 MongoDB packaging/archaeology
                                                          DAC-13 + DAC-24 -> DAC-23 atomic replacement checkpoint
                                                              -> DAC-12 SQLite certification
                                                              -> DAC-22 Mongo certification
                                                                  DAC-12 + DAC-22 -> DAC-30 cross-gold convergence
                                                                      -> Entity fleet lanes DAC-40–DAC-46
                                                                      -> Vector lanes DAC-51–DAC-58
                                                                          all fleet lanes -> DAC-90 truth/workflow/CI freeze
                                                                              -> DAC-99 portfolio certification
```

DAC-14 removes process machinery that cannot prove cognitive isolation and freezes the minimum-meaningful-parts rule.
DAC-10 and DAC-20 may harvest in either serial order after DAC-09. DAC-15 is the enforced cross-comparison and
public-contract gate. DAC-13/DAC-24 package complete replacements, and DAC-23 atomically integrates both reviewed
replacements into one checkpoint before either certification.
DAC-49/DAC-50 run during Foundation so every verdict consumes one stable runner; DAC-30 opens leased provider lanes.

## Work-item map

| Card | Outcome | Depends on |
|---|---|---|
| DAC-00 | dynamic roster, pinned baseline, packet skeleton, and integrity checks | — |
| DAC-01 | complete Framework/Family/Adapter scorecard for current Koan.Data public and alternate surfaces | DAC-00 |
| DAC-02 | human-ratified public API and resolution of every primer ambiguity exposed by DAC-01 | DAC-01 |
| DAC-14 | retire source-quarantine scaffolding and freeze the lean ground-up replacement boundary | DAC-02 |
| DAC-03 | Forge/TestKit and claim projection structurally cover every applicable stable primer ID | DAC-14 |
| DAC-49 | human-ratify a Vector annex inside the primer's single conformance catalog | DAC-03 |
| DAC-50 | project the ratified Vector annex into TestKit/Forge/claims/evidence | DAC-49 |
| DAC-04 | immutable source/effect plan, monotonic policy gate, readiness/provisioning split, and exact failures | DAC-50 |
| DAC-05 | Entity, query, residual, count, paging, bulk, batch, transaction, and streaming semantics align | DAC-04 |
| DAC-06 | Source Integration, inspection, neutral RecordSet, DTO projection, and registered reads align | DAC-05 |
| DAC-07 | compiled mapping and relational family execution own all shared mapping/physical-shape behavior | DAC-06 |
| DAC-08 | claims, receipts, facts, health, lifecycle, fault, soak, and benchmark evidence align | DAC-07 |
| DAC-09 | independent Framework-owned scorecard is green; no provider workaround supplies Framework behavior | DAC-08 |
| DAC-10 | harvest SQLite provider/public facts, black-box scenarios, negative lessons, and retirement inventory | DAC-09 |
| DAC-20 | harvest MongoDB provider/public facts, black-box scenarios, negative lessons, and retirement inventory | DAC-09 |
| DAC-15 | ratify both gold contracts, freeze rewrite inputs, and require empty implementation roots | DAC-10, DAC-20 |
| DAC-11 | author a complete SQLite candidate from empty roots | DAC-15 |
| DAC-13 | independently package, archaeologically review, and prove absence/lineage for SQLite | DAC-11 |
| DAC-21 | author a complete MongoDB candidate from empty roots | DAC-15 |
| DAC-24 | independently package, archaeologically review, and prove absence/lineage for MongoDB | DAC-21 |
| DAC-23 | atomically integrate both reviewed replacement bundles and seal one source checkpoint | DAC-13, DAC-24 |
| DAC-12 | independently certify SQLite on the integrated checkpoint | DAC-23 |
| DAC-22 | independently certify MongoDB on the integrated checkpoint | DAC-23 |
| DAC-30 | prove SQLite/Mongo differential semantics and the provisional Entity authoring loop | DAC-12, DAC-22 |
| DAC-40 | evaluate/certify InMemory Entity adapter | DAC-30 |
| DAC-41 | evaluate/certify JSON Entity adapter after the KeyValue family oracle | DAC-40 |
| DAC-42 | evaluate/certify PostgreSQL | DAC-30 |
| DAC-43 | evaluate/certify SQL Server | DAC-30 |
| DAC-44 | evaluate/certify CockroachDB after Npgsql family evidence | DAC-42 |
| DAC-45 | evaluate/certify Couchbase against the document family boundary | DAC-30 |
| DAC-46 | evaluate/certify Redis after the KeyValue family oracle | DAC-40 |
| DAC-51 | evaluate/certify InMemory Vector | DAC-30 |
| DAC-52 | evaluate/certify SqliteVec | DAC-51 |
| DAC-53 | evaluate/certify Qdrant | DAC-51, DAC-52 |
| DAC-54 | audit and independently certify the shared SearchEngine family seam | DAC-51, DAC-52, DAC-53 |
| DAC-55 | evaluate/certify Elasticsearch | DAC-54 |
| DAC-56 | evaluate/certify OpenSearch | DAC-54, DAC-55 |
| DAC-57 | evaluate/certify Weaviate | DAC-53 |
| DAC-58 | evaluate/certify Milvus in the heavy-provider lane | DAC-53 |
| DAC-90 | reconcile public truth and freeze the final author workflow and CI topology | all discovered adapter cards |
| DAC-99 | independently re-derive roster and certify the complete portfolio | DAC-90 |

## Dynamic remediation, gold correction, and certification return edges

Audit and certification cards never repair production code. A RED result creates one or more
`DAC-<scope>-R<n>` cards from [work-items/TEMPLATE.md](work-items/TEMPLATE.md), each with one owner, exact allowed paths,
and frozen scorecard rows. Before work begins, the packet dependency index marks every verdict consuming the changed
semantic owner, source path, profile/schema, TestKit/Forge version, or fixture stale—whether it is upstream, sibling,
or downstream in this DAG. `PROGRESS.md` inserts remediation and rerun dependencies for every affected certification.
Prior evidence remains as superseded history.

The audit mechanically freezes Observed/Advertised truth. Before a remediation targets a new capability—or truth
publication withdraws, downgrades, or marks one non-shipping—the human product owner approves the exact `CLM-*` rows.
Auditors and implementers cannot narrow the evaluated profile to obtain green.

After remediation, a different reviewer reruns the failed audit-certification or certification card from a newly
sealed common checkpoint. Every invalidated consumer reruns before the discovering/failed gate can pass. Even a
zero-RED initial fleet audit requires this independent second invocation before its row can pass. These return rules
apply to DAC-09, DAC-12/DAC-13, DAC-22/DAC-24, DAC-30, DAC-40–DAC-46, DAC-51–DAC-58, and DAC-99; the roadmap does not
become a live defect list.

DAC-12/DAC-22 corrections are a special case: their dynamic cards may change only the new implementation against the
failing black-box case and preserve the ground-up/atomic-retirement boundary. Every correction regenerates the
complete new-source manifest, reruns DAC-13/DAC-24, reseals DAC-23, and reruns both certifications.
Retired gold source, tests, structure, and compatibility paths never re-enter the input set or tree. A source-lineage
failure discards the affected candidate and reruns its ground-up card from an empty implementation; an incomplete retirement
inventory returns to DAC-10/DAC-20 and DAC-15 before recomposition.

## Parallel-lane coordination

After DAC-30, the orchestrator may lease independent provider evaluations in parallel. It remains the only writer of
`PROGRESS.md` and `NOW.md`. A worker receives one recorded lease, writes only its scoped evidence directory and
`evidence/<scope>/handoff.md`, and never advances another card. The orchestrator validates and merges results after the
workers finish. Dynamic remediation is serialized by semantic owner, so two lanes cannot change the same Framework or
Family seam concurrently.

## Phase exits

### Foundation exit

- Base Data and Vector-annex semantics are human-ratified in the primer's single catalog.
- Every new Framework/Family moving part has one necessary shared-contract or measured hot-path reason.
- Every current Data public/alternate execution path maps to a stable ID or an explicit Direct/provider-native boundary.
- Every announceable capability has an executable conformance cell.
- Framework-owned cells pass without using a production adapter as a policy substitute.

### Gold exit

- SQLite and MongoDB have complete §10.7 packets and independent PASS verdicts.
- Both replacements were authored from empty implementation roots; retirement and independent review prove that no
  old implementation, adapter-specific compatibility fixture, bridge, or shadow path remains.
- Differential semantics are green or explicitly capability-separated.
- Each responsibility has one Framework, Family, or Adapter owner.
- Warm-path baselines and native plans are pinned for both providers.

### Fleet exit

- Every dynamically discovered adapter has a green claim-relative packet or a non-shipping disposition.
- No certification relies on skipped LIVE evidence.
- Shared-family behavior is proved once structurally and again at each real-provider boundary.

### Portfolio exit

- Runtime claims, facts, documentation, and product maturity all resolve to green evidence.
- Dockerless, merge-gold, nightly, heavy, and release certification lanes are encoded in CI.
- The final author workflow and CI topology are frozen only after Entity and Vector fleet evidence exists.
- A fresh agent can use the primer and Forge to author or audit an adapter without depending on implementation history.

---
type: REFERENCE
domain: data
title: "Data Adapter Conformance Progress"
audience: [architects, maintainers, developers, ai-agents]
status: draft
last_updated: 2026-07-28
framework_version: v0.20.0
validation:
  date_last_tested: 2026-07-28
  status: reviewed
  scope: initial work-item ledger and known dependency state
---

# Data Adapter Conformance Progress

This is the sole live status ledger. Valid states are `pending`, `ready`, `in-progress`, `blocked`, `passed`, and
`declined`. At most one row is `in-progress` until DAC-30 explicitly opens independent provider lanes. After that gate,
only the orchestrator edits this ledger; workers publish scoped handoffs under `evidence/<scope>/handoff.md`.

| Card | State | Depends on | Evidence / result | Next condition |
|---|---|---|---|---|
| DAC-00 | passed | — | 16-adapter/5-family roster, 22 packet scopes, 15 focused mutation cases, exact bundle replay, and honest Forge baseline | complete |
| DAC-01 | passed | DAC-00 | 27-project restore-free inventory, 2,311 declarations mapped exactly once to 52 SUR surfaces, all 81 IDs dispositioned, 10 RED findings, and 18 finite decisions | complete |
| DAC-02 | passed | DAC-01 | 18/18 decisions ratified; exact public roots/config keys/semantics and consumer fixture frozen; docs and initiative validation green | complete |
| DAC-14 | passed | DAC-02 | retired unenforceable role/access scaffolding; restored default exploration; froze minimum-meaningful-parts boundary | complete |
| DAC-03 | passed | DAC-14 | generated 81-cell/27-profile catalog, complete claim registry, deterministic packets, strict Forge statuses, impact invalidation, and clean solution build | complete |
| DAC-49 | passed | DAC-03 | exact ten-item Vector ballot ratified; primer now owns V-01–V-24 and 12 conditional profiles | complete |
| DAC-50 | passed | DAC-49 | generated 105-cell/39-profile projection; 28 Vector AODB rows; false/incompatible claims and unavailable evidence stay non-green | complete |
| DAC-04 | passed | DAC-50 | immutable redacted source plan; first-boundary policy; distinct bounded readiness/provision/post-validation; stable failure seam; 89 focused tests and clean solution build | complete |
| DAC-05 | passed | DAC-04 | positional Entity reads; strict query/count receipts; one-dispatch bulk; qualified atomic/logical batch outcomes; final-visible Lifecycle; honest deferred coordination | complete; provider-native proofs remain on child cards |
| DAC-06 | passed | DAC-05 | source-only integration; neutral inspection/records; immutable registered reads; shared ordinal projection; 21 focused cases and clean solution build | complete; provider-native proofs remain on child cards |
| DAC-07 | passed | DAC-06 | one immutable mapping plan; compact four-shape grammar; complete relational command/schema substrate; 16/16 oracle; clean solution build | complete; provider-native projection/index/TTL proofs remain on child cards |
| DAC-08 | passed | DAC-07 | pure Describe/Explain, active non-mutating Doctor, one executable claim set, restricted evidence, bounded host ownership, eight scenarios, complete benchmark grammar, and canonical responsibility map | complete; provider LIVE proofs remain on child cards |
| DAC-09 | ready | DAC-08 | R01-R04 corrected observable semantics; the remaining framework-first gate is intentionally paused | both gold adapters identify the minimum shared seams worth certifying |
| DAC-10 | passed | DAC-09 | bounded public/provider lessons were consumed by the SQLite clean-room acceptance brief without a separate reuse phase | complete |
| DAC-20 | passed | DAC-09 | MongoDB provider facts, black-box failures, and retirement boundary were consumed by the clean-room replacement without a separate reuse phase | complete |
| DAC-15 | passed | DAC-10, DAC-20 | SQLite and MongoDB common bases, source manifests, moving parts, and retirement inventories are sealed | complete |
| DAC-11 | ready | DAC-15 | empty-root SQLite rebuild; 47/47 provider, 16/16 Relational, 471/471 Core; greenfield replacement gate passes | rebuild Web surface when packages are available; strict packet and stable performance runner remain explicit defers |
| DAC-13 | pending | DAC-11 | — | complete SQLite replacement manifest and tests ready for independent review |
| DAC-21 | in-progress | DAC-15 | clean-room MongoDB replacement; fresh zero-warning build; 40/40 live; Forge GREEN on 11 exact rows including D-01–D-05; greenfield `source=23 parts=9 retired=11`; 23/23 source hashes | shared/Web regressions, remaining provider rows, topology/performance evidence, strict packet emission, and independent review |
| DAC-24 | pending | DAC-21 | — | complete MongoDB replacement manifest and tests ready for independent review |
| DAC-23 | pending | DAC-13, DAC-24 | — | both reviewed atomic replacement bundles sealed from the common base |
| DAC-12 | pending | DAC-23 | — | integrated gold checkpoint sealed |
| DAC-22 | pending | DAC-23 | — | integrated gold checkpoint sealed |
| DAC-30 | pending | DAC-12, DAC-22 | — | both independent gold certifications pass |
| DAC-40 | ready | DAC-30 | empty-root InMemory Entity rebuild; 56/56 behavior, detached root-aware snapshots, finite host state, shared family regressions and solution gates green | strict versioned packet generation is available from the conformance control plane |
| DAC-41 | ready | DAC-40 | empty-root JSON rebuild; 34/34 real-file behavior, canonical host state, detached persist-then-publish, exact bounds, and truthful bulk/decline claims | strict versioned packet generation is available from the conformance control plane |
| DAC-42 | ready | DAC-30 | greenfield PostgreSQL implementation; 26/26 live and Relational 16/16 | strict packet and remaining fault/security/restart/performance evidence |
| DAC-43 | ready | DAC-30 | greenfield SQL Server implementation; 33/33 live and Relational 16/16 | strict packet and remaining fault/security/restart/performance evidence |
| DAC-44 | ready | DAC-42 | greenfield CockroachDB provider delta; 17/17 live, Npgsql 16/16, PostgreSQL 26/26 regression | strict packet and remaining operational evidence |
| DAC-45 | ready | DAC-30 | greenfield Couchbase implementation; 25/25 live and Source Integration 24/24 | strict packet and broad-Core environment reconciliation |
| DAC-46 | ready | DAC-40 | greenfield Redis implementation; 17/17 live, Source Integration 24/24, Cache 6/6 | strict packet and declared Web transfer-skip reconciliation |
| DAC-51 | ready | DAC-30 | empty-root InMemory rebuild; 50/50 surface tests, V-01–V-24 and G-09 Forge behavior green; solution build clean | strict versioned packet generation is available from the conformance control plane |
| DAC-52 | ready | DAC-51 | empty-root native rebuild; 58 suite passes, 28/28 Forge cells, source policy/isolation/restart/metric proofs, and clean solution build | strict packet is explicitly deferred; linux-x64/linux-arm64 execution remains external-runner evidence |
| DAC-53 | ready | DAC-51, DAC-52 | empty-root Qdrant v1.18.3 rebuild; 28/28 live V/G cells with zero skips; all declared filters and three metrics proven; solution build clean | strict packet generation remains deferred with DAC-51/DAC-52 |
| DAC-54 | passed | DAC-51, DAC-52, DAC-53 | current shared runtime rejected as a semantic family seam; provider-owned empty-root briefs frozen | complete |
| DAC-55 | ready | DAC-54 | empty-root Elasticsearch 9.4.3 rebuild; 28/28 live V/G cells with zero skips; all declared filters and three metrics proven; regressions green | strict packet generation remains deferred with DAC-51–DAC-53 |
| DAC-56 | ready | DAC-54, DAC-55 | empty-root OpenSearch 3.7.0 rebuild; 28/28 live V/G cells with zero skips; SearchEngine runtime retired | strict packet generation remains deferred with DAC-51–DAC-55 |
| DAC-57 | ready | DAC-53 | empty-root Weaviate 1.37.6 rebuild; 28/28 live V/G cells with zero skips; fixed schema, native token filters, three metrics, and regressions green | strict packet generation remains deferred with DAC-51–DAC-56 |
| DAC-58 | ready | DAC-53 | empty-root Milvus 2.6.20 rebuild; three 28/28 live V/G passes with zero skips; fixed schema/index/load lifecycle, native JSON filters, three metrics, and regressions green | strict packet generation remains deferred with DAC-51–DAC-57 |
| DAC-90 | pending | all discovered adapter cards | — | every adapter packet green or declined |
| DAC-99 | pending | DAC-90 | — | public truth reconciled |

## Dynamically generated remediation and gold-correction cards

| Card | Kind | State | Owner | Allowed paths | Frozen rows | Invalidated consumers | Parent gate / re-entry | Result |
|---|---|---|---|---|---|---|---|---|
| DAC-09R-01 | remediation | passed | Framework conformance harness | TestKit host/oracle, consumer fixture, scorecard/checkpoint tools | G-09 + certification protocol | DAC-09, DAC-10, DAC-20 and every downstream card | DAC-09 RED / complete | G-09 green on InMemory/JSON; compile, 105-cell scorecard, and RED-checkpoint mutation green |
| DAC-09R-01A | remediation | passed | Relational native operation binding | Relational SourceIntegration binding + compile contract/tests | F-01, F-02, F-04, F-05, F-06 | DAC-07, DAC-09R-01, DAC-09 and downstream | compile probe RED / complete | production opaque `.Sql(...)` binding; SourceIntegration 20/20 and consumer compile green |
| DAC-09R-02 | remediation | passed | Framework source decision/lifetime | source registry/service/integration/options/tests | A-02, G-08, P-01, P-03 | DAC-04, DAC-08, DAC-09 and downstream | DAC-09 RED / complete | frozen declarations; finite host caches; retryable single-flight repository/source activation; exact async disposal |
| DAC-09R-03 | remediation | passed | Framework operation/effect plan | Direct, instructions, registered-operation planning/tests | C-01, C-04, C-05, F-05, F-06, F-11, H-06 | DAC-04, DAC-06, DAC-09 and downstream | DAC-09 RED / complete | one `DataOperationEffect`; compact Direct declaration; no result/text inference; pre-provider rejection |
| DAC-09R-04 | remediation | passed | Framework fallback/transfer plan | repository fallback, transfer/query planning/tests | B-03, B-04, B-08, C-04, G-04, P-02, P-04 | DAC-05, DAC-09 and downstream | DAC-09 RED / complete | one bounded Copy/Move/Mirror owner; no replay; semantic clear selected before dispatch; duplicate partition transfer surface removed |
| DAC-09R-05 | remediation | deferred | Framework claim identity | claim contracts, TestKit manifest/applicability/tests | A-01, H-01, H-04, P-06 | DAC-03, DAC-08, DAC-09 and downstream | gold-adapter feedback / deferred | implement only the minimum seam proven necessary by SQLite and MongoDB |
| DAC-09R-06 | remediation | pending | Framework diagnostic/privacy boundary | public plans, diagnostics, transactions/tests | H-01, H-02, H-05, H-06, P-01 | DAC-04, DAC-05, DAC-08, DAC-09 and downstream | DAC-09 RED / after R05 | — |
| DAC-09R-07 | remediation | pending | Framework compiled mapping/materialization | Direct, records, mapping, patch, bounds/tests | D-05, D-06, D-07, E-10, P-02, P-03, P-04 | DAC-06, DAC-07, DAC-09 and downstream | DAC-09 RED / after R06 | — |

## Active lane leases

Only the orchestrator adds or closes leases. Two active leases cannot share a semantic owner or allowed path.

| Lease | Card | Worker | Allowed evidence/handoff paths | Semantic owners | State |
|---|---|---|---|---|---|
| mongodb-dac21 | DAC-21 | orchestrator | `evidence/mongodb/**`, MongoDB connector/tests/docs, and proven shared-seam fixes | Adapter(MongoDB) | active |

## Divergence and risk log

| Date | Card | Observation | Consequence / owner |
|---|---|---|---|
| 2026-07-28 | course correction | Four semantic Framework remediations are complete but no gold adapter has been rewritten; continuing R05-R07 first would optimize certification machinery without implementation feedback. | Freeze R01-R04, defer R05-R07/DAC-09, and use empty-root SQLite then MongoDB as vertical design proofs. Shared code changes now require a failing gold case. |
| 2026-07-29 | ledger reconciliation | All 16 discovered adapters have replacement implementations, but the gold/fleet card states and generated packet summaries remained pending. The stale handoff incorrectly instructed another MongoDB rewrite. | Restore the pushed MongoDB implementation, seal its greenfield lineage, update implementation states, and finish evidence/certification rather than rebuilding adapters again. |
| 2026-07-29 | strict packet gap | `DataConformancePacket` can compile and validate packets and strict Forge consumes them, but no command emits adapter evidence as `conformance.json`. | Add only the bounded emission seam required by gold evidence; keep every strict verdict DEFERRED until real packets exist. |
| 2026-07-27 | bootstrap | The primer defines major Source Integration, RecordSet, named-operation, policy, and mapping contracts absent from current Data source. | Framework-first implementation is mandatory; adapters cannot close these gaps locally. |
| 2026-07-27 | bootstrap | Existing Forge/AODB coverage is materially smaller than the primer's 81-cell catalog. | DAC-03 evolves it as the sole executable projection. |
| 2026-07-27 | bootstrap | Vector adapters use a distinct `Vector<TEntity>` surface not exhaustively specified by the primer. | DAC-49 obtains human approval for a Vector annex inside the primer before DAC-50 changes conformance tooling. |
| 2026-07-27 | gold policy | Current SQLite/MongoDB implementations are useful only for sanitized lessons, public facts, black-box cases, and retirement inventory. | DAC-11/DAC-21 begin from empty implementations; DAC-23 proves atomic legacy absence and one execution path. |
| 2026-07-27 | DAC-14 simplification | Role chains, history-free exports, and per-read logs cannot prove cognitive isolation and added no shipped guarantee. | Retired the scaffolding; ground-up replacement is enforced by empty roots, atomic retirement, black-box conformance, and review. |
| 2026-07-28 | DAC-00 Forge baseline | The broad `FullyQualifiedName~Aodb` filter now selects a passing polymorphic-root case outside Forge's fixed five record modes, so three record projects are classified structural `ERROR` despite all expected modes passing. | DAC-03 must use explicit catalog discovery; the current Forge verdict remains inconclusive and cannot certify primer adherence. |
| 2026-07-28 | DAC-03 | Strict Forge returns DEFERRED when a selected adapter has no versioned packet even when its bounded AODB proofs pass. | Certification cannot become green through missing packet state; adapter cards must produce a complete protocol packet. |
| 2026-07-28 | DAC-04 | Legacy `EnsureReady` conflated reachability and provisioning, while `DataAdapterReadinessExtensions` guessed missing shape from message text and replayed the business operation. | The replay helper is deleted; constrained sources bypass legacy provisioning readiness; native non-creating open/shape proof is mandatory per adapter. |
| 2026-07-28 | DAC-05 | Existing SQLite native relationship execution advertises filter support but omits the required handled-filter receipt. | Keep the provider cell RED; the DAC-11 empty-root replacement must return the exact receipt. Core will not infer it or carry a legacy bridge. |
| 2026-07-28 | DAC-09 | Two independent reviewers returned RED: the strict shared G-09 oracle violates composition ownership, the sealed consumer contract is not compiled, and seven bounded Framework seams remain false. | DAC-10/DAC-20 stay blocked; execute DAC-09R-01 through R07 serially and rerun DAC-09 with fresh reviewers. |
| 2026-07-28 | DAC-09R-01 compile probe | The ratified `.Sql(...)` example compiled only inside one test because `SourceIntegrationSpec` supplied a private fake extension; no Relational Family binding existed. | DAC-09R-01A adds the one opaque SQL binding leaf, deletes the fake authority, and returns to the harness gate. |
| 2026-07-28 | DAC-06 | The broad Data Core project still mixes known legacy receipt failures with unrelated AI/host fixture pollution and exceeded its bounded runtime. | Use the 21-case Source Integration/Direct oracle plus clean solution build; retain provider RED cases for the empty-root gold replacements. |
| 2026-07-28 | DAC-07 | Shallow projection and index metadata gave hydration, queries, schema, and indexes separate interpretations and process-static caches. | One host-owned bounded `MappingPlan` now owns every consumer; the generic connector compatibility surface delegates to it until connector cards retire that surface. |
| 2026-07-28 | DAC-08 exploration | Runtime facts, health, and Testing each project capabilities independently; source resolution activates integrations before pure inspection; several composition registries and structural caches are process-static or unbounded. | Move claim projection to one abstraction owner, make Source Integration lazy/disposable, project diagnostics from frozen plans, and put mutable decisions/caches under bounded host ownership. |
| 2026-07-28 | DAC-08 | The broad Core run exposed 22 strict handled-filter/count receipt failures in legacy adapter-backed paths after Framework/test/environment issues were separated. | Keep them RED for gold/fleet adapter cards. Core will not infer handled work or relax receipts; DAC-09 certifies only the Framework boundary. |

## Operator gates

| Gate | Needed by | Requirement | Status |
|---|---|---|---|
| Primer ergonomics/API ratification | DAC-02 | human approves the exact public surface and any amendments | approved 2026-07-27; `evidence/framework/ratification.json` |
| Reproducible source checkpoint | DAC-09 onward | operator supplies an authorized phase commit or a sealed base-commit + bundle + source-manifest identity | DAC-00 mechanism proven; reseal at each implementation/certification gate |
| Gold Target/Declined manifests | DAC-15 | human product owner approves target additions, abandoned targets, and any proposed withdrawal | pending |
| Gold replacement integrity | DAC-15/DAC-23 | empty implementation roots, complete retirement inventory, one execution path, and no bridge/fallback are reproducible | pending |
| Vector annex ratification | DAC-49 | human approves exact public semantics and stable primer IDs before TestKit work | approved 2026-07-27; exact ten-item ballot |
| Fleet claim changes | DAC-40–DAC-90 | human product owner approves new targets, withdrawals, downgrades, and non-shipping dispositions | pending |
| Real MongoDB provider | DAC-20, DAC-21, DAC-22, DAC-23, DAC-24 | Docker or pinned reachable MongoDB with least-privilege identities | pending |
| SqliteVec native RID matrix | DAC-52 | reproducible win-x64, linux-x64, and linux-arm64 runners/artifacts for every shipped RID | pending |
| Networked provider fleet | DAC-42–DAC-46, DAC-53, DAC-55–DAC-58 | pinned container/provider prerequisites; skipped LIVE is not PASS | pending |
| Stable performance runner | DAC-11, DAC-12, DAC-13, DAC-21, DAC-22, DAC-23, DAC-24, DAC-99 | identified machine/runner for comparable provider-relative baselines | pending |

## Session metrics

Append one row per completed card.

| Card | Result | Focused build/test time | LIVE provider | Scorecard PASS/RED/DEFER | Review findings | Human action | Notes |
|---|---|---:|---|---|---:|---|---|
| DAC-00 | PASS | integrity 3s; mutations 15s; checkpoint replay; Forge baseline | Docker-free only | bootstrap PASS; Forge INCONCLUSIVE | 1 control-plane gap recorded | none | 16 adapters, 5 families, 22 packets, 15 focused negative cases; role/access ceremony retired by DAC-14 |
| DAC-01 | PASS | restore-free source audit | none | 71 RED / 10 DEFER | 10 findings | none | 2,311 declarations mapped once to 52 surfaces; all 81 IDs dispositioned |
| DAC-02 | PASS | docs and initiative validation | none | contract gate only | 18 decisions ratified | approved | exact compact consumer contract frozen |
| DAC-14 | PASS | source/docs validation | none | workflow gate only | quarantine machinery declined | none | removed guard/reference/test scaffolding; no production behavior changed |
| DAC-03 | PASS | focused tests 7s; TestKits 13s; solution build 38s | Docker-free InMemory | protocol PASS; strict missing packet DEFERRED | 1 PowerShell binding defect corrected | none | 81 cells, 27 profiles, five exit states, dependency invalidation, one C# validator |
| DAC-49 | PASS | docs lint; Vector InMemory 34/34; SqliteVec 29 pass/5 explicit skips | Docker-free Vector baselines | decision gate only | provider vocabulary and current surface kept non-normative | approved | exact Vector grammar and V-01–V-24 ratified as written |
| DAC-50 | PASS | protocol 16 pass/1 expected skip; full Koan.Testing 29 pass/4 skips; solution build 23s | Vector InMemory fixture | current providers DEFER on 24 explicit seams | legacy streaming-result claim classified incompatible | none | 105 cells, 39 profiles, 28 Vector rows, one runner and packet validator |
| DAC-04 | PASS | owned matrix 89/89; solution build 27s | none | Framework foundation PASS; native rows remain explicit child proofs | broad Data Core attempt exposed unrelated fixture/host failures | none | typed lifecycle/access/read lanes, redacted plan diagnostics, no message replay, bounded host stage state |
| DAC-05 | PASS | owned Core 76/76; source/transaction/relationship framework 62/62; convergence 19/19; solution build clean | existing SQLite child case RED | Framework execution PASS; native receipt/rollback/resource rows remain child proofs | 1 legacy SQLite receipt defect assigned to DAC-11 | none | strict per-axis receipts, positional get-many, one-dispatch bulk, logical batch outcomes, final-visible Lifecycle, non-atomic coordinator honesty |
| DAC-06 | PASS | Source Integration 16/16; Direct 5/5; solution build 27s | SQLite Direct fixture only | Framework D/F boundary PASS; native inspection/binding/resource rows remain child proofs | 2 projector defects found and corrected | none | entity-free source factory, signed continuations, bounded RecordSet, compiled ordinal projection, immutable named reads, no replay |
| DAC-07 | PASS | mapping/relational 16/16; Core regression 21/21; solution build 21s | none | Framework A/E/P-06 substrate PASS; native projection/index/TTL rows remain child proofs | 3 forged physical-fact mutations rejected | none | compact four-shape grammar, one bounded compiled plan, complete symbolic relational commands, exact schema validation, External zero DDL |
| DAC-08 | PASS | Core 140/140; Testing 33 pass/4 intentional skips; Relational 16/16; Axes 56/56; host lifecycle 5/5; clean solution build | none | Framework G/H/P PASS; provider LIVE/receipt rows remain child proofs | 22 legacy adapter receipt REDs classified and retained | none | pure diagnostics, exact claim spine, bounded host lifecycle/evidence/caches, eight scenarios, pinned four-metric benchmark grammar, one responsibility map |
| DAC-09R-02 | PASS | owned matrix 71/71; broader source/hosting 90/90; initiative 41/41; mutations 16/16 | none | Framework A-02/G-08/P-01/P-03 remediation PASS | async-only disposal race found during final review and corrected | none | immutable source freeze; finite single-flight plan/repository caches; failed creation retry; provider activation only after admission; exact host disposal |
| DAC-09R-03 | PASS | owned matrix 75/75; broader Direct/source 84/84; Relational 16/16; solution build clean | SQLite Direct fixture only | Framework C-01/C-04/C-05/F-05/F-06/F-11/H-06 remediation PASS | normal `IDataService` provider-construction gap found during final review and corrected | none | one effect enum; no text/result inference; compact Direct `.Effect(...)`; opaque/lane/parameter contradictions reject before activation; cancellation preserved |
| DAC-09R-04 | PASS | owned matrix 19/19; broader transfer/stream/source 107/107; solution build clean | truthful in-process Framework fixture | Framework B-03/B-04/B-08/C-04/G-04/P-02/P-04 remediation PASS | legacy SQLite false paging receipt retained RED; duplicate partition transfer implementation removed | none | one compact bounded builder; page/write/delete/conflict bounds; exact receipts; deferred delete-on-close journal; no replay |
| DAC-53 | BEHAVIOR PASS / STRICT DEFER | Qdrant 28/28; Data Vector 24/24; InMemory 50/50; SqliteVec 58 pass/5 declared skips; solution build 45s | Docker Qdrant v1.18.3, zero provider skips | all V-01–V-24/G-09 rows PASS; strict packet missing | naming policy and restart-fixture endpoint defects found and corrected | none | one plan-bound REST path; compact options; native filter convergence; three metrics; strict certificate awaits shared packet generation |
| DAC-54 | PASS | source/docs exploration | pinned Elastic/OpenSearch official contracts inspected | implementation-boundary gate only | legacy family seam owns provider semantics and misses the ratified repository contract | none | prove twice, share last; DAC-55/56 rebuild independently and retire the shared runtime |
| DAC-55 | BEHAVIOR PASS / STRICT DEFER | Elasticsearch 28/28; Data Vector 24/24; InMemory 50/50; SqliteVec 58 pass/5 declared skips | Docker Elasticsearch 9.4.3, zero provider skips | all V-01–V-24/G-09 rows PASS; strict packet missing | case-folded naming, implicit object mapping, flattened range, restart readiness, and refresh latency defects corrected | none | one plan-bound native path; bounded nested filter projection; three metrics; full live ledger 17s |
| DAC-56 | BEHAVIOR PASS / STRICT DEFER | OpenSearch 28/28 live | Docker OpenSearch 3.7.0, zero provider skips | all V-01–V-24/G-09 rows PASS; strict packet missing | test fixture scoping defect found and corrected; retired SearchEngine after proving zero owner | none | one plan-bound native path; bounded nested filter projection; three metrics; full live ledger 30s |
| DAC-57 | BEHAVIOR PASS / STRICT DEFER | Weaviate 28/28; Data Vector 24/24; InMemory 50/50; SqliteVec 58 pass/5 declared skips; solution build 29s | Docker Weaviate 1.37.6, zero provider skips | all V-01–V-24/G-09 rows PASS; strict packet missing | framework-reserved metadata test and clear preflight ceiling corrected from live evidence | none | one plan-bound HTTP/GraphQL path; fixed lossless metadata/filter schema; three metrics; live ledger 14s |
| DAC-58 | BEHAVIOR PASS / STRICT DEFER | Milvus 28/28 three times; Data Vector 24/24; InMemory 50/50; SqliteVec 58 pass/5 declared skips; solution build clean | Docker Milvus 2.6.20 with pinned etcd 3.5.25 and MinIO, zero provider skips | all V-01–V-24/G-09 rows PASS; strict packet missing | REST readiness, stripped schema descriptions, encoded JSON responses, three-valued negation, and unbounded readiness memo defects corrected from live evidence | none | one plan-bound REST v2 path; fixed contract schema and HNSW/load ownership; native JSON filters; three metrics |
| DAC-40 | BEHAVIOR PASS / STRICT DEFER | InMemory 56/56; Data Core 471/471; JSON 28/28; Redis 17/17; solution build clean | Docker Redis sibling regression only; InMemory is host-local | all advertised Entity/Source/KeyValue behavior PASS; strict packet missing | retained-reference persistence and poisoned over-limit store creation removed | none | six compact owners; detached root-aware snapshots; lock-free warm lookup; exact 4,096-store host ceiling |
| DAC-41 | BEHAVIOR PASS / STRICT DEFER | JSON 34/34; Data Core 471/471; InMemory 56/56; live Redis 17/17; JSON build clean; solution restore externally blocked | real filesystem plus pinned Docker Redis sibling | all advertised Entity/Source/KeyValue/file behavior PASS; strict packet missing | canonical alias lost updates, eager construction I/O, duplicate identity, unbounded file input, and false bulk claims removed | none | nine compact owners; immutable serialized records; persist-then-publish; exact 1,024-file and 64 MiB ceilings |
| DAC-11 | BEHAVIOR PASS / STRICT DEFER | SQLite 47/47; Relational 16/16; Data Core 471/471; greenfield gate and 16-file hash manifest pass | native embedded SQLite, zero skips | one-path replacement and advertised connector behavior PASS; Web rebuild, strict packet, and stable performance evidence unavailable | two repository paths, sync discovery, mutating health, duplicated mapping/query/record helpers, false bulk shape, and read-transaction bug removed | none | one repository/mapping path; one bounded native bulk dispatch; non-creating External/health; cold polymorphic restart |

---
type: GUIDE
domain: framework
title: "Koan V1 Post-Main-Cycle Todo Register"
audience: [maintainers, architects, ai-agents]
status: current
last_updated: 2026-08-20
framework_version: v0.20.0
validation:
  date_last_tested: 2026-08-20
  status: reviewed
  scope: bounded design and polish debt deliberately deferred from active V1 slices
---

# Koan V1 post-main-cycle todo register

## Contract

This register preserves small but real issues that deserve deliberate treatment after the active
V1 slice. It prevents two failure modes: widening the active repair until it never
finishes, and losing a design concern because the immediate warning or symptom was made quiet.

An entry is not authorization to change a public contract. Before implementation, give it a bounded
card, apply the normal exploration workflow, decide compatibility explicitly, and name executable
acceptance evidence.

**Verify the premise against the tree first.** A deferred entry records the tree as it was on the day it
was written, and unrelated work closes these silently: on 2026-08-20, four of the eight entries examined
had already lapsed, two of them retired by a single commit (`a39edffa4`) that nobody reconciled back
here. Treating a stale premise as work is how a register becomes a source of invented tasks. Beware
`bin`/`obj` left in `src/` by an earlier build — an ignored directory can make a retired package look
live; `git ls-files` is the authority, not `ls`. If new evidence makes an entry a correctness, security, or release blocker,
promote it into the active backlog instead of waiting for this list.

## Current register

| ID | Surface | Deferred issue | Why it stays out of the main cycle | Decision required before work | Acceptance evidence |
|---|---|---|---|---|---|
| PMC-006 | Release tooling | Long package plans capture child output and can appear idle between package completions. | Buffered output does not weaken artifact evidence, but it makes operator supervision unnecessarily uncertain. | Define concise live progress events without leaking secrets or making resumable state depend on console rendering. | A bounded slow-process test proving periodic progress, failure context, and unchanged machine-readable evidence. |
| PMC-008 | Data / vector transactions | `VectorSaveOperation` crosses the Data.Core→Vector boundary through reflection, method-name lookup, and a runtime `Task` cast; nearby error text still says `UpsertAsync` while lookup uses `Upsert`. | The nullable-array correction is behavior-preserving; replacing the bridge changes a cross-project contract and transaction behavior. | Decide whether a small Core-owned capability seam can remove reflection without reversing dependency direction; otherwise make the reflection contract explicit and fail-loud. | Transactional vector save/delete integration proof, missing/incompatible method mutations, cancellation, and non-atomic reporting. |
| PMC-009 | Documentation tooling | XML documentation defects can remain invisible until a public sample rebuild happens to traverse the owning project. | The supported contracts now reject warnings, but repository-wide doc-link validation belongs with the broader warning policy. | Decide whether packable projects or the solution should treat XML-doc reference warnings as errors and how generated/legacy code is scoped. | A deliberately broken `cref` mutation fails the selected CI lane; current shipping modules pass it. |
| PMC-020 | Certification evidence | The durable aggregate manifest now exists: every project emits a TRX and `scripts/aggregate-test-evidence.ps1` merges them into `artifacts/ratchet/test-manifest.json`. **What remains is CI parity** — no CI lane runs the ratchet at all, so there is no protected run to compare a local manifest against. Certification is a manual milestone by design (ARCH-0121), which is why red accumulates unseen: the first full run in some time was RED with 13 failing suites. | Adding a scheduled ratchet lane is a real compute and noise decision, not a mechanical follow-on. Many suites are container-backed, and a flaky nightly gate that gets ignored is worse than none. | Decide which lane CI runs (every suite, or the docker-free subset), on what trigger (nightly cron as `skills-verify.yml` already does, or on demand), and whether a red result notifies or merely records. Then upload the manifest as a build artifact so local and CI shapes can be compared. | A protected run publishes `test-manifest.json`; its aggregate agrees with a local run over the same commit and suite set; a deliberately failing suite is named identically in both. |
| PMC-021 | Entity Communication authoring | `[EventDetailsRequired]` makes payloadless `Raise<TEvent>()` fail before source enumeration at runtime, but no build-time diagnostic guides the author at the call site. | R07-08 deliberately keeps one Communication package and avoids introducing an analyzer assembly for a misuse that already fails safely and clearly. | Decide whether this friction occurs often enough to earn a shared Koan analyzer rule, and define generic/event-attribute resolution without coupling analyzers to runtime discovery. | A compile fixture rejects payloadless Raise only for details-required event kinds, accepts optional and explicit-details calls, supplies one corrective message, and leaves runtime enforcement unchanged. |
| PMC-022 | Media derivative lifecycle | Public `MediaDerivation` exposes framework storage mechanics, keys persisted output by source id plus recipe fingerprint without source-contract identity, and requires applications to query framework rows for targeted cleanup or statistics. | R07-17 removes a false generic sweep safely, but a correct replacement needs one context-aware render/lifecycle owner and an explicit migration rather than another speculative storage SPI. Current on-demand and targeted application behavior remains honest. | Centralize derivative identity, storage, access, and cleanup behind a Media-owned coordinator keyed by source contract/type and logical context; decide compatibility and migration for existing rows before hiding framework storage. | Multi-source-type collision and isolation proofs, tenant/access context safety, targeted cleanup and statistics, HTTP/direct-render convergence, restart behavior, and an explicit migration fixture for existing derivatives. |
| PMC-023 | Entity Communication evolution | Distributed receiver groups and wire contracts currently derive identity from CLR application types. A type rename changes the group/contract identity; there is no stable alias, schema-version negotiation, or heterogeneous application manifest. | R07-18 completes one-application business-channel policy and real RabbitMQ carriage. Inventing aliases before an integration/rolling-upgrade use case would add a second naming system without proving migration semantics. | Define stable contract and receiver-group aliases, ownership, version compatibility, rollout precedence, collision handling, and whether aliases belong on business types, handlers, or host composition. Keep cross-application integration distinct from Entity replication. | A rolling two-version RabbitMQ fixture proves compatible old/new participants, rename migration, collision rejection, exact startup/facts manifests, authenticated context, and fail-loud incompatible schemas without duplicate group delivery. |
| PMC-026 | Package impact precision | Analyzer ProjectReference sources conservatively mint every consuming package when the analyzer changes, even when a generator emits no source for a particular consumer or a diagnostic-only analyzer cannot change its assembly bytes. | Conservative automatic impact is release-safe and operator-free; proving output-sensitive selection requires compiler evidence and must not reintroduce a maintained package list or stale identity risk. | Decide whether generator-emission fingerprints can safely narrow release impact while diagnostic analyzers retain an intentional policy, with conservative mapping as the fail-safe default. | A generator change selects every consumer whose compiled/generated bytes can change, leaves proven non-emitting consumers untouched, handles diagnostics/config changes deliberately, and mutation tests reject any false negative. |
| PMC-029 | First-use startup truth | The exact R08-04 package console completed persistence/query work but reported two framework-owned collection failures. The repair is implemented: `StartKoan()` now owns one standard Generic Host lifecycle, focused host/Communication suites pass 8/8, a source-equivalent template console starts hosted capabilities and completes Entity work without either failure, and a focused Data.Core nupkg carries the required Host dependency. | Rebuilding the complete 108-package bootstrap solely to repeat package execution would spend a release-certification wave after the owner and consumer proofs are already green. A truthful Diagnostics section may still contain useful elections, guarantees, and corrections; the defect is a false `CollectionFailed` fact, not the existence of inspectability. | Keep [ARCH-0119](../../decisions/ARCH-0119-one-console-host-lifecycle.md) as the lifecycle owner. On the next exact candidate, make the existing package-only probes reject Communication-composition and health-registry collection failures before any public promotion. | The next exact package-only console, FirstUse, and GoldenJourney contain no false Communication or health-registry collection failure; focused tests continue to prove standard host services, hosted start/stop, real corrective faults, and clean disposal. Truthful non-failure Diagnostics remain visible. |
| PMC-030 | AI adapter lifetime | `IAiAdapterRegistry.Add` no longer exists; the registry is compiled once via `Compile(IEnumerable<IAiAdapter>)`. `InMemoryAdapterRegistry` still retains every compiled adapter — including disposable ONNX sessions — and implements no disposal, so host-shutdown ownership of adapter lifetime is still unstated. Premise re-verified 2026-08-20; the surface changed, the ownership question did not. | R10-06 moves contributor execution into `AiModule.Start` and proves process behavior; resolving mixed adapter ownership requires a deliberate contract rather than sample teardown code or an ONNX-only exception. | Choose one ownership model: registry-owned registrations, explicit ownership metadata, or DI-created adapters only. Align contributor construction and removal semantics before adding disposal. | Repeated-host ONNX and DI-owned-adapter fixtures prove exactly-once disposal, removal disposal policy, failed-contribution cleanup, no retained inference sessions, and unchanged routing/provenance behavior. |
| PMC-034 | Web Auth / Identity factors | The retired Credentials, Passwords, and MFA packages contained real BCrypt, TOTP, recovery, `amr`/`acr`, and checkup mechanics, but no supported authentication ceremony connected primary proof, step-up continuation, factor verification, and cookie issuance. The former specs manufactured proof claims and redispatched the lifecycle, while a real provider callback could be aborted by an untranslated gate exception. | Shipping or polishing those packages would turn application-auth scaffolding into a falsely complete framework claim. R11 removes the partial surface so V1 stays semantically honest; the dated SEC-0007 decision remains historical input. | Design one Web Auth-owned ceremony engine for external and Koan-managed primary factors. Put cross-module factor vocabulary in an inert contracts assembly; keep factor storage/mechanics in opt-in leaves; define continuation secrecy/lifetime, return handling, local-login collision and lockout, distributed single-use semantics, rate limiting, CSRF/origin posture, data-protection key persistence, enrollment/recovery authorization, startup/facts/health, and explicit unsupported guarantees. | A package-only application adds Passwords and/or MFA by reference plus existing `AddKoan()`; real controller/provider round trips prove password-only success, enrolled-MFA interruption and resume, TOTP/recovery single use under concurrency, callback continuity, restart/key behavior, lockout/rate limiting, CSRF/origin posture, no session before all factors, standard session/role projection after completion, and matching startup/runtime facts. |
| PMC-035 | Identity × Tenancy invitations | The former invitation record and acceptance service were retired together. Their check-then-write flow could grant one token to two different people under multi-node contention; verified-email matching also depended on upstream assurance and was not itself an inbox-ownership ceremony. | A process-local keyed lease would hide rather than solve the distributed claim problem; fail-closed pre-claim without recovery could consume an invitation without creating its seat. R11 keeps no placeholder type for a guarantee the framework does not complete. | Design one explicit invitation claim state machine with claimant identity, attempt/accepted timestamps, provider-independent conditional-write requirements or transaction ownership, idempotent recovery after partial failure, token hashing/uniqueness, expiry/revocation races, verified-address assurance policy, role validation, audit, and controller/browser security posture. | Two identities racing one token across multiple hosts yield exactly one seat and one durable claimant; retries recover every injected failure boundary without double grant or lost invitation; token/revocation/expiry/email-assurance/role cases fail correctly; package reference plus existing `AddKoan()` exposes one supported acceptance ceremony with startup/runtime explanation. |
| PMC-054 | SQL Server / a store that describes nothing cannot notice drift | `SqlServerDdlExecutor.Describe` reports column **presence only** — it returns a null state for every column and compares no definition at all, by a decision PMC-040 recorded deliberately. SQL Server is one of only two stores that build projected columns (`AS ... PERSISTED`), and it is the one that cannot tell a current projection from one an older Koan built. The mechanism that made this matter on MySQL is identical: a dialect changes how it reads a JSON scalar, new tables get the new expression, existing tables keep the old one, and the optimizer stops substituting a computed column whose expression no longer matches the query — silently retiring every index over it. No SQL Server expression has changed yet, so nothing is wrong today; the store simply has no way to find out when one does. | Starting to compare definitions judges every existing database against expectations no release has held it to, which is the reason presence-only was chosen. Marking is narrower than comparing — it judges only columns Koan itself computes — but on an existing database *every* projected column lacks a marker, so the first upgraded boot reports drift on all of them. That is honest (Koan genuinely cannot verify them) and is now self-clearing, since PMC-052 rebuilds exactly this class of column under the consent that created it — but it is still a behaviour change on upgrade and belongs to its own proof. | Decide the marker mechanism: SQL Server has no inline column comment, so the analogue of MySQL's `COMMENT` is an extended property (`sp_addextendedproperty`, read back through `sys.extended_properties` joined on `major_id`/`minor_id`), which is a second statement per projected column and needs a stated lifetime when the column is dropped or rebuilt. Reading `sys.computed_columns.definition` back is the alternative and is the one MySQL rejected, because a store returns its own canonical rendering rather than the text it was given. Decide too whether an absent marker reports drift — MySQL says yes, and matching it keeps one rule across both stores rather than a second dialect of the same idea. | A SQL Server table whose persisted computed column was built from an older expression is named on boot and rebuilt under `AutoCreate`, validating clean afterwards with the index over it used again; a column Koan wrote in the same release validates clean without a rebuild; and a non-projected column that drifted is still refused rather than rewritten. |
| PMC-055 | Jobs / fleet roster — **SHIPPED 2026-08-26** (WorkerNode roster + 10s/40s K8s-ratio heartbeat/death, confirmed-death reclaim default-on, TrySettle ownership fencing on all tiers, graceful resignation, workersOnline health, 4 specs; suites 97/85/70/75/16 green) — retained as the record; | Nodes are anonymous `Owner` GUIDs; there is no programmatic way to ask who is online, who stalled, or to detect a dead node faster than `LeaseDuration`. A `WorkerNode : Entity<WorkerNode>` roster (register at boot, guarded heartbeat of `LastSeenAt`, deregister on graceful stop, stale sweep) would project fleet facts into `jobs:*` and health, and license the reaper to reclaim rows of a *confirmed-dead* owner before lease lapse — safe only since JOBS-0009's lost-lease abandon makes early takeover non-clobbering. **Prior-art-pinned (2026-08-26):** heartbeat 10s / death 40s = the Kubernetes Lease ratio (4×; K8s: 10s heartbeat, 40s grace); Hangfire uses 30s/5min and self-restarts on own heartbeat failure; Oban Lifeline has no liveness at all (time-based rescue after 60 min, admits duplicates); BullMQ caps reclaim amplification with `maxStalledCount`. Phi-accrual (Cassandra φ=8, Akka φ=10) rejected for v1 — fixed ratio now, adaptive detection later. Fencing: JOBS-0009 renewal + a new ownership-guarded settle are the fence at the two dangerous writes (renew and settle); Kleppmann's rule — timeouts give liveness, fences give safety. | Death-timeout value (40s default, must exceed 2× worst GC pause — documented); reclaim-on-confirmed-death default-on (fencing makes it safe); roster on all tiers (in-memory floor = single node, trivially fresh). | Two-node spec: kill one mid-job, peer reclaims within death timeout, revived node abandons without settling; zombie-settle guard spec (settle after steal bounces). |
| PMC-056 | Jobs / dispatch modality seam — **SHIPPED 2026-08-27** (events half shipped 2026-08-26 via `JobStatics` outcome observers; reservation half: opt-in `JobDispatchMode.Reservation` — senior-roster-node coordinator, guarded `TryReserve` stamps onto `JobRecord.ReservedFor/ReservedUntil`, claim eligibility converges across tiers, death/lapse release, `jobs:dispatch` fact + corrective Inline refusal; core 109 · SQLite 86 green) — retained as the record; | Dispatch is pull/CAS only. A reservation modality ("jar hands a cookie to a named hand" — active coordinator) needs the roster first and must stay strictly opt-in: it reintroduces a coordinator by design. Related vocabulary pinned during ideation: lifecycle event projections (`Enqueued`, `Claimed`, `Stalled` per BullMQ naming, `Completed`, `Failed`, `DeadLettered`, `Abandoned`) as projections of the already-persisted `Transitions` audit fanned out through Communication signal lanes — no new write paths. | Modality pluralization without a membership primitive would invent routing state the ledger cannot verify. | Whether events ride Communication signals vs Canon-style outcome observers; veto-capable hooks follow the ratified base-form-intervenes grammar. | Roster-dependent assignment spec plus an events spec asserting each transition projects exactly once. |
| PMC-057 | Jobs / jar topology | Per-entity-type physical jars were decided against in JOBS-0003's aftermath in favor of one shared jar with logical compartments (lanes = ordering, pools = capacity, `[JobPersistence]` = tier). A future `JobJar("name")` attribute could still virtualize physical separation per type/group through existing storage naming, with lane-fair arbitration extended across jars. Fragmenting without arbitration kills cross-crowd fairness; virtualizing keeps it. | Physical-per-type tables change schema management and claim iteration for a benefit lanes/pools already mostly deliver. | Prove a real isolation need (noisy neighbor at index level) before building. | Cross-jar fairness spec: two jars, WFQ rotation, no starvation; retention/trim per jar. |
| PMC-058 | Jobs / SKIP LOCKED claim strategy | Dialects offering `FOR UPDATE SKIP LOCKED` (Postgres, SQL Server) could grade the hot claim beyond conditional-UPDATE CAS at high fleet concurrency. Same capability-grading philosophy as claims today; explicitly deferred from JOBS-0009. | Contention numbers do not currently justify it; CAS is proven across five suites. | Whether it slots beside ConditionalReplace as another DataCaps write token. | High-concurrency claim spec on Postgres showing reduced retry rate vs CAS baseline. |
| PMC-060 | Jobs / surface hygiene batch — **SHIPPED 2026-08-27** (dead `Koan.Jobs.Transport.Messaging` shell deleted; `JobHandle` doc now states the ledger-poll truth; gate model unified — the read-shape `JobGate` POCO deleted, `JobGateRecord` is the single type across the contract; `Query` documented as a filter-only unordered contract with a tier-parity spec; core 110 · SQLite 87 green) — retained as the record; | Dead shell `src/Koan.Jobs.Transport.Messaging/` (bin/obj only) misleads archaeology; `JobHandle` XML doc says "push signal when bus present" but the mechanism is a poll wait; dual gate model (`JobGate` POCO vs durable `JobGateRecord`) awaits unification; in-memory `Query` ignores sort/paging while durable honors it. | Cosmetic-to-small-seam items bundled deliberately so none rides an unrelated slice. | Batch together after the gateway pilot reshapes the public surface. | Build with shell deleted; doc/code agreement lint; converged Query behavior spec. |
| PMC-062 | Koan / conformance-kit manifest rejects real application project names — **SHIPPED 2026-08-27** (parser half: Core 1.0.25/1.0.26 — ProjectReference raw identity is provenance, canonical always enforced, regression spec in Koan.Core.Tests; release-model half: `stamp-dependents` command stamps each package's `dependency-versions.json` when direct or transitive dependency versions move, and plan-release refuses any release stranding direct dependents behind new floors; verified by the full 99-package train flood release and a fresh-restore probe resolving App 1.0.14 → Core 1.0.26, after which the proof-and-observability cold run PASSED with the batteries 4/0/2-gated); retained as the record; (Core 1.0.25: a ProjectReference raw identity is the referenced project's own assembly name — provenance, not a Koan identity — so Parse now enforces the Koan-identity rule on package references and always enforces the canonical check; regression spec in Koan.Core.Tests; testing guide documents the required stable app `<PackageId>`), **release-model half OPEN** — `Sylin.Koan.App`/`Sylin.Koan.Testing` floors stamp at pack time and the release plan does not re-advance unchanged dependents when only a referenced package changes, so fresh restores still resolve Core 1.0.24 and the documented real-host battery flow cannot boot against published packages. Evidence: proof-and-observability cold runs (b2–b7) plus a local koan-web-template reproduction (`reference|project|TmplApp|Sylin.TmplApp` under the old parser; canonical fixed via PackageId, raw fixed via parser, floors pending). | The documented test-your-app flow is the framework's own proof contract; every real user app has a non-Koan assembly name. | Cosmetic-to-nothing observed yet — the batteries fail at boot with a malformed-manifest correction, which at least fails loudly. | Decide the dependent-advance policy: when a shared package (Core/Data.Abstractions) advances, direct dependents' floors re-stamp and their versions advance in the same release. | A cold agent follows testing-your-app.md against published packages and the inherited batteries run green. |
| PMC-061 | Canon / duplicate stage transitions — **SHIPPED 2026-08-27** (root cause: hydration deserialized with Newtonsoft's default `ObjectCreationHandling.Auto`, which populates the constructor-seeded `Transitions` list by ADDING stored history — one duplicate per save/reload cycle, matching the observed 3–5 per receipt; fix: `ObjectCreationHandling.Replace` in the relational entity codec AND the shared document materializer, making hydration store-authoritative; decision: the constructor stays the seeding owner, dedup belongs to the materializer, not `AppendTransition`; canon unit 44 · Data.Core 492 green) — retained as the record; | A staged receipt's transition log records "Stage created" multiple times (observed 3–5 duplicates per receipt in the blind capability run on Canon 1.0.12) — likely one append per Entity materialization path re-running the constructor-seeded transition. | Cosmetic audit-noise; no correctness impact observed. | Decide the single-owner rule for the seeding transition (constructor vs persistence path) and whether dedup belongs in AppendTransition or the materializer. | Spec asserting exactly one "Stage created" transition per fresh receipt across create/persist/reload. |

## Promoted and resolved history

- **PMC-061 — the duplicates were a serializer default, and the card's second option was the right owner.**
  Shipped 2026-08-27. The card guessed "one append per materialization path" — half right. The constructor DOES
  re-run on every materialization, but re-running alone is harmless when hydration *replaces* the seeded list; the
  duplicates came from hydration *adding*: the relational entity codec and the shared document materializer both
  deserialize with Newtonsoft, whose default `ObjectCreationHandling.Auto` populates an already-populated collection —
  so a materialized receipt kept its fresh constructor seed AND gained the stored history on top, growing exactly one
  duplicate per save/reload cycle (a receipt processed, held, and recovered cycles 3–5 times: the observed 3–5).
  The single-owner decision falls out: the constructor owns creation-time artifacts (it cannot know it is being used
  as a hydration activator), `AppendTransition` dedup is rejected — it would silently mask any future store-vs-ctor
  merge defect and suppress legitimate identical entries — and the **materializer** owns hydration authority via
  `ObjectCreationHandling.Replace` in both `RelationalManagedMapping`'s codec and `EntityJsonSerialization.DocumentSettings`
  (the eager/document stores shared the defect). This is a data-layer correctness fix, not a Canon patch: any Entity
  whose constructor seeds a collection had the same silent growth. Proof: `reload_keeps_exactly_one_stage_created_transition`
  (canon unit — demonstrated failing before the fix), `constructor_seeded_entries_are_never_duplicated_by_reload_cycles`
  (three real save/reload cycles on SQLite in Data.Core), full canon/Data.Core/Jobs suites green.

- **PMC-060 — the hygiene batch, where one finding had already dissolved and another became a single-owner law.**
  Shipped 2026-08-27. The dead shell was pure untracked bin/obj leftover (no project, no references) and simply
  deleted. The `JobHandle` doc now names the real mechanism: a bounded 50 ms ledger poll; there is no bus-dependent
  push by design, because hints hurry claims while the ledger remains the only truth a waiter reads. The gate model
  unified by deletion: `JobGate` was a read-shape POCO mirroring three fields of `JobGateRecord`, referenced nowhere
  outside the ledger contract — `ActiveGates` now returns `JobGateRecord` on every tier, and the cooperative-backoff
  doc moved to the survivor. The Query divergence resolved as *already gone*: git history shows no sort/paging axis
  ever shipped on the current `JobQuery` surface; both tiers take the same four wildcard filters and return an
  unordered set (callers needing determinism order client-side, as `StatusAsync` does). Rather than invent dead
  knobs, the contract is now stated in `IJobLedger.Query`'s doc and pinned by `facade_query_filters_converge_across_tiers`
  in the shared behavior suite. Proof: core suite 110 green (one new fact), SQLite tier 87 green (two tier-neutral
  facts on the durable path).

- **Canon intake parity — hygiene annotations sweep at arrival too.** Closed 2026-08-27 alongside PMC-060's finding
  that annotation semantics needed one owner: `HygieneTransform` now lives beside `[Trim]`/`[Lowercase]`/`[Uppercase]`
  in Data.Abstractions, Data.Hygiene consumes it for its persistence transforms, and Canon intake sweeps the same
  annotations between the model override and composed rules (`IntakeHygiene<TModel>`, scanned once per closed type,
  property selection mirroring the persistence bag exactly). Match keys and validators therefore see prepared values
  even on models with no hand-written normalization, explicit rules still win final say over a declared case, and no
  host is forced into a transitive Data.Hygiene reference — interpretation comes from Abstractions, activation stays
  per lifecycle. Proof: canon unit 43/43 (annotation-only normalization; rules-see-normalized ordering;
  explicit-over-declarative precedence), canon integration 8/8, Data.Core 491/491.

- **PMC-056 — the modality seam shipped as a reservation layer over the same ledger, not a second queue.** Opened
  during ideation; the events half closed 2026-08-26 (`JobStatics.On{Claimed,Completed,Failed,DeadLettered,Rescheduled,
  Cancelled,Stalled,Abandoned}` as throw-safe post-write projections — Canon-style outcome observers won over
  Communication signal lanes, which keep only their existing wake-signal job), and the reservation half closed
  2026-08-27. The ideation constraint did the design: assignments had to stay ledger-verifiable, so the cookie is two
  fields on `JobRecord` (`ReservedFor`/`ReservedUntil`, stamped while Queued) rather than any per-hand routing state.
  One guarded narrow write (`IJobLedger.TryReserve`, capability-graded like `TrySettle`) mints it; claim eligibility
  gains exactly one clause in every tier's claim path (unreserved / mine / lapsed) and consuming a claim clears it.
  The "active coordinator" is deliberately protocol-free: seniority is derived from the PMC-055 roster (oldest alive
  `StartedAt`, id tie-break), concurrent-seniority windows are absorbed by the same conditional writes claims use,
  and coordinator death self-heals at the death timeout when the next-senior assumes the pass. Assignment writes no
  transition and raises no event — dispatch metadata is not lifecycle (the events spec proves projections track the
  persisted transition audit exactly). Opt-in guard per the corrective-failure law: `Reservation` × `Mode.Inline`
  refuses at orchestrator construction with the fix in the message. Proof: core suite 109/109 and SQLite tier
  86/86 including a tier-neutral `claim_honors_reservations_on_every_tier` fact every adapter suite now inherits,
  plus assignment-to-live-members, lapse-openness, dead-hand release-and-reassign, and the boot-refusal specs.

- **PMC-058 — the generic sentinel wake rung shipped as JOBS-0009's WakeStamp.** Opened during ideation and
  closed 2026-08-25. The single-row sentinel entity, bumped inside the submission transaction and probed at
  `WakeProbeInterval`, replaced a designed-then-removed `IStoreSignalChannel` adapter seam: framework-owned
  carriage in one Entity beat driver-specific push adapters spread across connectors, with sub-second peer
  discovery on every durable store and no new interfaces. Store-native push stays rejected unless measured
  need reopens it; instant wake remains a Communication connector away.
- **PMC-056 — the seam that classifies a store failure has a producer and a consumer now.** Opened and closed
  on 2026-08-22. Eight concurrent claimers deadlock SQL Server about one full jobs-suite run in five, and the
  victim's `SqlException` ended the drain outright because `JobOrchestrator.DrainAsync` wraps its loop in
  `try`/`finally` with no `catch`.
  The decision the entry posed — wire `DataFailure` or bolt on a retry — turned out to be a false choice. A
  retry done properly *is* the seam, because `Koan.Jobs` catching `SqlException` 1205 is precisely the boundary
  DATA-0119 draws. And the seam was far more finished than "unused" suggested: `IDataFailureClassifier`,
  `DataFailure`, `DataFailureKind`, the commit and replay dispositions and the correction text all existed, with
  a doc comment already forbidding the trap ("must not classify by message text"). What was missing was one
  implementation and one consumer. **No public enum needed a new member** — a deadlock is `Conflict`, whose
  correction already reads "Reload current state and apply the declared conflict policy."
  Scope is deliberately one failure. `SqlServerFailureClassifier` recognises 1205 by number across the whole
  `SqlException.Errors` collection, and answers `Conflict` / `NotCommitted` / `RequiresIdempotency`.
  `NotCommitted` is the load-bearing half — SQL Server has already rolled the victim back — while the retry
  disposition is deliberately weaker than the rollback alone would permit, because the enum has no "always
  safe" and overstating safety is the expensive direction. Everything else SQL Server can throw returns
  `false` and stays exactly as raw as it was.
  `DataFailurePolicy` is the consumer half, in `Koan.Data.Core`. It retries nothing: it answers whether an
  operation *the caller knows to be idempotent* may be attempted again, gated first on the store saying nothing
  committed. `ClaimNext` asks it, and may because a claim ends in a conditional write that either wins or
  reports that someone else did — the property established by PMC-048 the day before.
  The other three relational adapters stay unclassified on purpose. `40P01`, `1213` and `SQLITE_BUSY` are each
  a real classification, and each should arrive with a reproduction rather than by analogy.
  Proof: five dockerless specs on the policy, of which the one that matters asserts that a `Committed` or
  `Unknown` outcome is never retried whatever its disposition says, plus three on the classifier. Jobs suites
  green on all four tiers — SQL Server 68, SQLite 81, PostgreSQL 68, Mongo 73 — and Jobs core 87.

- **PMC-048 — the claim was never the problem; SQL Server was deadlocking.** Opened 2026-08-21, closed
  2026-08-22. A one-off failure of `concurrent_claimers_take_distinct_jobs_no_double_claim` was recorded as a
  possible double claim, with the suspicion that moving SQL Server off Dapper had weakened the compare-and-set.
  **The claim cannot admit two winners, and this is settled by reading it rather than by a green suite.**
  `ClaimNext` marks the candidate and calls `ConditionalReplaceAsync` with the guard
  `r.Status == JobStatus.Queued && r.Owner == null`, which the relational adapter emits as one statement:
  `UPDATE ... SET ... WHERE <identity> AND (Status = @p) AND (Owner IS NULL)`, succeeding only on
  `rowcount == 1`. `Owner == null` becomes `IS NULL` rather than `= @p` — checked in `SqlFilterTranslator`,
  since a null-bound equality would have been the binding difference the entry suspected, and it would have
  made *every* claim fail rather than two succeed. An UPDATE takes an exclusive row lock and re-evaluates its
  predicate against the current committed row, so the loser affects nothing. No isolation level changes that.
  The spec that guards it is load-bearing, proven by removing the guarantee rather than by argument: with the
  conditional write disabled so the claim is last-write-wins, the suite reports **104 claims where 24 were
  seeded** — `c21` three times, `c15` three times. A double claim is therefore something this spec sees.
  **What actually failed has a name now.** Running the full SQL Server jobs suite with output preserved
  reproduced it: `SqlException` — *"Transaction (Process ID 65) was deadlocked on lock resources with another
  process and has been chosen as the deadlock victim"* — thrown from a scalar read inside the claim, not from
  the compare-and-set. Roughly one in five full runs; never once in eight isolated runs of the spec alone,
  which is exactly why it read as a flake. That is a real defect and it is **not** this entry's; it is carried
  as PMC-056, together with the finding that the seam meant to absorb it has no producers or consumers at all.
  The spec now checks that nothing is left queued **before** it counts, so the two directions separate on
  sight: a duplicated claim leaves nothing queued and counts high, a lost claim leaves rows queued and counts
  low, and they have nothing to do with each other. Verified on all four durable tiers — SQL Server 68,
  SQLite 81, PostgreSQL 68, Mongo 73.
  Decided in [DATA-0122](../../decisions/DATA-0122-adapters-classify-failures-the-framework-decides.md).
  Third time this cycle that "intermittent, did not reproduce" meant a real defect, after PMC-042 and PMC-053.
  The pattern is not that these suites are flaky; it is that a failure read through a summary filter has had
  its evidence thrown away, and the mechanism was in the output every time.

- **PMC-053 — the SQLite cluster was never flaky; a guessed pillar was poisoning host boot.** Opened
  2026-08-20, bounded 2026-08-21, **closed the same day with the mechanism named**. The suite had reported a
  cluster of failures four times and passed on immediate re-run every time.
  The entry's standing instruction is what solved it: *any batch run of this suite must preserve full output or
  the TRX*. The fourth occurrence was caught behind a summary filter again — 48 of 49 red, error text
  discarded — and the very next run, captured to a file, named it in one line:
  `Pillar 'data' is already registered with different metadata`, thrown from `KoanPillarCatalog.MergeDescriptors`
  while `KoanDataCoreModule.Register` was running.
  `ProvenanceRegistry.ResolveDescriptor` invents a placeholder for any pillar no manifest has declared yet, so a
  module reporting under an unknown pillar still has something to be shown under — `("data", "Data", "#2563eb",
  "📦")` — and it **registered that guess as authoritative**. When `DataPillarManifest` then declared the real
  `("data", "Data", "#38bdf8", "🗄️")`, the colour and icon differed, the merge refused it, the data module threw
  during register, and every `AddKoan()` in that process failed from there. `DataPillarManifest` only sets its
  `_registered` latch after a successful registration, so each subsequent boot retried and threw again — one
  poisoning, then a cascade, which is the cluster.
  Whether it happened at all depended on whether a module with pillar code `data` reported provenance before
  `Koan.Data.Core` registered — an ordering question, which is exactly why it looked like an environment
  interaction and why the size of the cluster varied with where the first host boot landed in the order.
  **The previous conclusion was wrong, and worth recording as wrong.** The four eliminations in the earlier pass
  were all sound and all beside the point: parallelism, fixture paths, connection pooling and fault containment
  are properties of the suite, and the defect was in `Koan.Core`. Nothing about the suite could have led there.
  The reproduction is a batch — three container suites, then SQLite — and it now passes 49/49 where it produced
  48 failures immediately before the fix.
  The fix went through two shapes, and the second is the one to remember. The first taught the catalog to tell
  a guess from a declaration and let the declaration win. It worked, and it was more machinery than the problem
  deserved: reading the consumers showed **nothing anywhere reads the catalog for a pillar nobody declared** —
  `KoanPillarCatalog.All` has no readers at all, the admin status page matches by namespace prefix which a
  guess never populates, and `ResolveDescriptor` runs once per pillar code because its caller caches the
  result. The write achieved nothing except the collision. So it is gone: provenance describes an undeclared
  pillar and does not register it, and the catalog is back to one kind of registration, refusing two manifests
  that disagree — which is a genuine contradiction and reachable only from declarations now.
  Decided in [ARCH-0132](../../decisions/ARCH-0132-a-registry-separates-declaration-from-inference.md).
  One dockerless spec pins the cause rather than the symptom: driving provenance for an undeclared pillar
  leaves the catalog without it. Restoring the write fails that spec, and the reproduction batch stays green.

- **PMC-055 — a pinned map silently ignored an ambient partition on three stores.** Opened and closed on
  2026-08-21, found while measuring whether the four relational repositories could share a base class.
  A declared map names one physical container; an ambient partition asks for a different one. SQLite and Redis
  had always refused the combination outright. MySQL, SQL Server and PostgreSQL served the pinned container
  instead — so a caller who asked for a partition got unpartitioned data and nothing said so. Confirmed by
  probing a real PostgreSQL: under `EntityContext.Partition("must-not-alias")` the read returned the row from
  the pinned table.
  This is the failure mode Koan's own design principles reject — a request that cannot be honoured being
  answered with silence rather than a refusal — and it is worse than a wrong answer because the caller's next
  step is to trust the isolation. The three now throw the same `NotSupportedException` SQLite does, in the same
  words. Pinned by a spec on PostgreSQL and SQL Server against real containers.
  **MySQL is guarded but unproven: it has no legacy-mapping spec at all**, so declared maps are untested on that
  store end to end. That is a coverage gap of its own rather than a defect, and it is bigger than this entry —
  the other five relational and document stores each have one.
  The document stores were not touched. Couchbase and Mongo pin a container the same way and carry no such
  guard; whether they should is the same question answered for a different family, and it needs its own probe
  rather than an assumption that the answer transfers.

- **PMC-044 — the bulk-write spec states its claim instead of timing it.** Opened and closed on 2026-08-21.
  `bulk_save_of_a_large_batch_is_a_single_batched_write` asserted a fixed wall clock of ten seconds and failed
  on a cold run while passing on a warm one, reporting a defect where there was none; ruling it out as fallout
  from unrelated work had already cost a full worktree bisect.
  The entry preferred a statement count the adapter reports. No adapter exposes one, and adding that surface to
  learn what a ratio already tells you would be a new public contract bought for a test. The measurement it
  named second is exact here and needs nothing: the claim is that a bulk save amortizes one commit across the
  batch, so it is asserted as **cost per row through the bulk path against cost per row saved individually,
  both measured in the same run**. Cold start lands on both and cancels.
  The two are ordered deliberately — the batch runs first while the process is coldest, the baseline second
  once it is warm — so everything JIT and the page cache contribute counts against the assertion rather than
  for it. Measured on a loaded machine in Debug: 0.246 ms/row batched (50,000 rows in 12,294 ms) against
  7.425 ms/row individually (200 rows in 1,485 ms), a ratio of 30. Degenerating the bulk path to per-row writes
  scores **1.0** — 7.935 against 8.011 ms/row — because it is then doing exactly what the baseline does. The
  bar is 5: six times below a working implementation, five times above a broken one, and no longer a quantity
  that a slow machine can move.
  The other two specs in the file still assert wall clocks (2 s for the dashboard query, 3 s for the claim
  loop). Both have far more headroom than the one that failed — each is a handful of rows out of 100,000 — and
  neither has been observed to fail, so they are left as they are rather than rewritten on suspicion. They are
  the same shape, and the same treatment applies if one of them ever goes red on a cold run.

- **PMC-037 — Couchbase honours the comparable-encoding contract, and the oracle now proves it.** Opened
  2026-08-20, closed 2026-08-21. Couchbase stored a `TimeSpan` in .NET's default form, so N1QL ordered
  `1.00:00:00` before `23:00:00` — a day ahead of twenty-three hours, the exact inversion DATA-0100 exists to
  close. Range filters on the same values were wrong for the same reason.
  Both decisions the entry asked for turned out narrower than it assumed. **The home:**
  `ComparableScalarEncoding` moved from `Koan.Data.Relational` to `Koan.Data.Core`, which it already depended
  on — its `Apply` ended by calling `EntityJsonSerialization.Apply`, and its managed-field wiring was Core's
  too. The move cost one `using` correction and nothing else: every consumer already imported `Koan.Data.Core`.
  **The read path:** already solved. `TimeSpanTicksConverter` had tolerated the legacy string since it was
  written, for relational stores, so no migration or versioned field was needed; a dockerless spec now pins
  that tolerance so nobody deletes the branch as dead.
  The one real decision was scope. Couchbase writes its unmapped documents through the framework's shared
  document settings — and so do the Json and Redis stores, backup archives, cutover evidence and the cache.
  Putting the converters there would have rewritten the on-disk form for stores that nobody asked to migrate,
  so Couchbase carries its own settings instead, exactly as Mongo, Json and Redis already do. Three seams
  needed it: the unmapped document, `ToJson` on the mapped write, and `FilterValue` for comparands.
  `EncodeComparand` transforms only the four governed CLR types, so applying it after a binding has already
  encoded a value is a pass-through rather than a double encoding.
  `Duration` joined `SortPushdownConvergence.PortableScalars` and `TimeSpan` joined
  `TypeClassification.IsPortableStreamSortScalar`. `UnprovenScalar`, the constant that recorded the exclusion,
  was declared and never read — the exclusion was enforced only by absence from the list — so it is gone.
  Proof, and it is load-bearing in the strict sense: with the TimeSpan converter alone reverted to the .NET
  string form, Couchbase fails with `Duration: expected d,e,a,c,b, store gave d,e,c,b,a` — the entry's own
  reproduction, arrived at from the other direction. Restored, all nine data connector suites pass with
  `Duration` asserted on every one of them: sqlite 49, inmemory 56, json 41, postgres 28, mysql 12,
  sqlserver 37, mongo 41, cockroach 19, couchbase 27.

- **PMC-052 — drift that could be repaired is repaired now, and only where repairing is safe.** Opened and
  closed on 2026-08-21. `EnsureCreatedAsync` added columns a table was missing and treated every other
  difference as something to report or refuse, so an upgraded MySQL database reported Degraded forever over a
  stale generated column that Koan knew the correct expression for (PMC-045).
  The boundary is the whole decision, and it is narrow on purpose: **a projected column, and nothing else**. A
  projected column holds no value of its own — the store recomputes it from the structured document on every
  write — so rebuilding one loses nothing whatever it had drifted into. Every other column holds its own value,
  and replacing one is how a framework destroys data by being wrong about what it is looking at. The structured
  root is the sharpest case and is refused rather than rebuilt, which the dockerless spec pins directly.
  Consent is the consent that already exists. Repair rides `IsDdlAllowed` — the same gate that lets Koan create
  the column in the first place — rather than earning a second knob. A store that may not issue DDL still gets
  the finding, and its reads still resolve through the document. Repair also precedes index creation, so an
  index is never built over an expression about to change; MySQL's `MODIFY COLUMN` rebuilds the indexes over
  the column it restates, which is what brings back the indexes the stale expression had quietly retired.
  `IRelationalDdlExecutor.RebuildProjection` is the one shared addition, defaulting to a refusal the same way
  `CreateIndex` does: the orchestrator asks only a store that answered `SupportsPersistedComputedColumns`, and
  a store that computes a column it cannot restate is a contradiction worth hearing about. Only MySQL and SQL
  Server project at all, and only MySQL can currently see that a projection is stale — **SQL Server describes
  its columns by presence only and compares nothing**, so it cannot yet detect the drift this repairs. Giving
  it the marker is the follow-on, and it is a better change now than before: with repair in place the first
  boot after an upgrade fixes what the marker finds instead of reporting it forever. Carried as PMC-054.
  Proof: the MySQL spec seeds a table, rewrites its generated column to the pre-PMC-038 expression with no
  marker, boots, and asserts the report is Healthy, that the column comment went from empty to `koan-gen:`
  (which only the ALTER writes, so the report cannot pass by validation merely giving up), and that a write
  round-trips afterwards. Three dockerless specs pin the decision itself: rebuilt under `AutoCreate`, reported
  and untouched under `NoDdl`, and refused when the drifted column is the structured root.

- **PMC-045 — a generated column can go stale without its type changing, and nothing said so.** Opened and
  closed on 2026-08-21. Fixing the JSON-null read changed the expression MySQL's stored generated columns are
  built from. New tables got the new expression; a table an earlier Koan created kept the old one, so on an
  existing database the null-write defect survived the upgrade *and* the optimizer stopped substituting the
  column, silently retiring every index built on it. `ColumnMatches` compared the column's type, character set
  and nullability — none of which change — so both were invisible.
  The entry framed detection as the hard half, and on its own terms it was: `information_schema` returns
  MySQL's canonical rendering of a generation expression rather than the text it was given, so comparing them
  compares against the server's formatter rather than against Koan. The answer was to stop parsing and start
  marking. A projected column is now created with `COMMENT 'koan-gen:<fingerprint>'`, the fingerprint being a
  hash of the expression the dialect produced, and validation compares a marker Koan wrote against one Koan
  computes — exact, with no normalizer to get wrong. A column with no marker was written by a Koan that did not
  know to leave one, which is exactly the population that needs rebuilding.
  Reported rather than made fatal: a stale projection still answers reads, so it surfaces as Degraded with the
  column named, and an operator can rebuild it. Making it corrective would stop every existing MySQL deployment
  from booting on upgrade with no remedy but manual DDL. **Repairing it automatically under DDL consent is the
  natural next step and is not done** — `EnsureCreatedAsync` adds absent columns but treats all drift as
  something to report or refuse, never to correct. That is carried as PMC-052.
  Decided in [DATA-0121](../../decisions/DATA-0121-repairable-schema-drift.md).
  `RelationalColumnState.ProjectionStamp` is the one shared addition; everything else is MySQL-local, because
  MySQL remains the only store that compares definitions at all.

- **PMC-050 — the AOT proof re-runs now, on a schedule, and every cell was proven by breaking it.**
  Opened and closed on 2026-08-21. `scripts/aot-verify.ps1` publishes the `AotRelational` sample under
  ILC and **runs** the binary; `.github/workflows/aot-verify.yml` runs it daily on `windows-2022`.
  **Where.** A scheduled lane, not the certification ratchet. `scripts/green-ratchet.ps1` was the other
  candidate and was rejected for the reason the entry itself names: it is a deliberate manual boundary,
  and manual is exactly the status ARCH-0093's proof already had when it decayed for five weeks. A guard
  that waits for someone to decide to run it inherits the failure mode it exists to remove. Per-PR was
  rejected on cost (a publish is minutes), and compile-only was rejected on evidence — see below.
  **Breadth.** Six cells exist; two run daily. The daily pair is `Sqlite` and `SqlServerInvariant`,
  chosen because they need no container and because between them they catch all three PMC-049 defects.
  The four server cells (`Postgres`, `Cockroach`, `MySql`, `SqlServer`) run from the same script with
  `-Connectors All` where Docker is available. That split is forced, not preferred: every workflow in
  this repository runs on Linux, and a GitHub-hosted **Windows** runner — which win-x64 ILC requires —
  cannot host the Linux database containers those cells need. Dropping Cockroach was considered, since
  it shares both `Npgsql` and `NpgsqlRepository` with Postgres and adds no distinct ILC input; it was
  kept because the on-demand matrix is cheap and because ARCH-0093 and DATA-0120 now assert five
  backends, and a matrix that reproduces four of five invites exactly the inference this cycle keeps
  being burned by.
  **The compile-only proxy is dead, and the trial shows why.** Reintroducing `MetadataToken` on the
  mapping path produced a **successful publish** and a binary that exited 1 on the first entity it
  mapped. An ILC compile would have gone green on that change.
  **Every cell was proven against the defect it exists to catch**, red then green:
  reference manifest → publish fails (`DirectoryNotFoundException`); `MetadataToken` → publish succeeds,
  run exits 1; `Assembly.GetName()` on a satellite → `SqlServerInvariant` reports boot died on
  `CultureNotFoundException` before reaching the driver's refusal.
  Two findings came out of building it. The manifest defect fires only on a *first* RID publish, so a
  warm intermediate tree hides it — the script now removes the sample's `obj` so a developer machine and
  a fresh CI checkout mean the same thing. And the satellite defect is caught by **no** ordinary cell:
  SQLite ships no satellites, and the SqlServer build carries culture data because SqlClient demands it,
  which makes its satellites nameable. `SqlServerInvariant` exists solely to put satellites and invariant
  mode in one process; it asserts the refusal it should get and the failure it must not.
  **Same-PR half.** `scripts/aot-lint.ps1` runs as ratchet leg F, which the PR gate already invokes, and
  rejects `MetadataToken` and `(dynamic)` in `src/` — the two constructs whose damage is documented. Both
  rules were proven red-then-green, and the gate trial is the point: with `MetadataToken` reintroduced the
  ratchet's **build leg passes** and only leg F fails, which is precisely the change that would otherwise
  sit in `dev` until the next morning. It is a complement to the daily lane, not a substitute; a grep
  cannot enumerate what ILC forbids, which is why compile-only was rejected in the first place.
  **Known residual:** the four server cells are not machine-guarded, so a `Npgsql`, `MySqlConnector` or
  `Microsoft.Data.SqlClient` version bump could break server AOT and go unnoticed until someone runs the
  matrix. `docs/SURFACES.md` carries that distinction with its date rather than leaving it in prose.

- **PMC-049 — the single binary does reach the servers, and three framework defects stood between the
  claim and the proof.** Opened and closed on 2026-08-21. Measured, not reasoned: a minimal Koan console
  (`samples/fundamentals/AotRelational`) NativeAOT-published win-x64 once per connector and run against a
  real container, writing and reading one `Note` through the ordinary `Entity<T>` surface, with the row
  then confirmed in the store from outside the application.
  **All five relational backends publish and run**: SQLite (26 MB), MySQL 8.4 / MySqlConnector 2.6.1
  (28 MB), PostgreSQL 17 / Npgsql 10.0.3 (32 MB), CockroachDB v24.3 on the same `NpgsqlRepository`
  (32 MB), SQL Server 2022 / Microsoft.Data.SqlClient 7.0.2 (35 MB). No provider produced a link failure
  and none needed a trimming suppression; the IL2026/IL3050 warnings are the expected Newtonsoft and
  reflection-path family ARCH-0093 already documents.
  **The one real provider constraint is SQL Server's, and it is not about AOT.**
  `Microsoft.Data.SqlClient` refuses globalization-invariant mode outright —
  `System.NotSupportedException: Globalization Invariant Mode is not supported`, thrown from
  `SqlConnection.TryOpen`. A SQL Server build therefore carries culture data
  (`InvariantGlobalization=false`); the other four do not need it. That is a driver policy, so it applies
  to a JIT build in invariant mode just as much, and no AOT work can lift it.
  Getting there required three framework repairs, each a genuine defect on the AOT path:
  `WriteKoanReferenceManifest` wrote into the RID-specific intermediate directory without creating it, so
  the *first* `-r <rid>` publish of any Koan application died with `DirectoryNotFoundException`;
  `MemberInfo.MetadataToken`, which four sites used to recover declaration order, does not exist under
  ILC and threw `MappingCompilationException: There is no metadata token available for the given member`
  on the first entity mapped; and `AppBootstrapper`'s `AddAsm` called `Assembly.GetName()` on every
  assembly, which materializes the culture and threw `CultureNotFoundException` in invariant mode for the
  eleven satellite resource assemblies SqlClient ships — a Koan bug that presented as a SqlClient one.
  The second of those had also broken **SQLite**, which ARCH-0093 certified working: the mapping compiler
  landed 2026-08-06, three weeks after the 2026-07-17 proof, and nothing re-ran the proof. The single-binary
  claim had been false for the floor as well as unproven for the servers, and only publishing it showed that.

- **PMC-047 — the AOT-clean ADO surface had no caller, because the framework was paying for Dapper and using
  none of it.** Opened and closed on 2026-08-21. `Koan.Data.Relational/Ado` was written for ARCH-0093 so SQLite
  could avoid Dapper's runtime IL emit under NativeAOT, and then nothing adopted it — SQLite hand-rolled its
  commands and the three server adapters called Dapper directly.
  The question "should Dapper live behind one framework seam" turned out to have a better answer, and the
  evidence decided it rather than taste: every Dapper call in all three adapters was untyped (`QueryAsync`,
  `QuerySingleOrDefaultAsync`) or scalar, and each immediately cast the row to `IDictionary<string, object>` and
  handed a plain dictionary to `plan.Hydrate`. Dapper's compiled materializer — the whole reason it exists, and
  the exact thing NativeAOT forbids — was never called. It was serving as a dictionary reader and a parameter
  bag, both of which `AdoCommands` and `SqlParameters` already provided.
  So SQL Server, MySQL and PostgreSQL/CockroachDB now execute through that surface, the package is gone from
  the tree and from `Directory.Packages.props`, and the Postgres package description no longer advertises
  "Dapper-based SQL integration". `SqlParameters.FromObject` absorbs the raw-query binder each adapter had been
  carrying privately. The surface's documentation had also outlived a "Dapper-backed twin" that R11-02 retired;
  that is corrected.
  The consequence for DATA-0120 is the point: the collapse is over four adapters again, not three, because the
  reason SQLite had to be excluded no longer exists.

- **PMC-042 — the Jobs suites were not flaky; one spec depended on an unspecified order.** Opened and closed on
  2026-08-21. `retired_work_types_are_dead_lettered_without_blocking_valid_work` seeded a retired ledger row and
  a valid job at the same instant, leaving them tied on `(VisibleAt, FirstSubmittedAt)` — the claim window's
  entire sort. Which one the conveyor claimed first was therefore undefined, and it matters: `ClaimNextRunnable`
  dead-letters unregistered rows only until it reaches the first registered one, then returns. Claim the retired
  row first and the spec passes; claim the valid job first and the retired row is simply deferred to a later
  pass, which the spec read as a failure.
  It was filed as an isolation problem because that is what it looked like — it failed in a batch run and passed
  alone, on PostgreSQL and later on SQL Server. It was neither: the order was arbitrary and the arbitrary choice
  moved with load. Adding the identity tiebreaker (PMC-046) made it deterministic and deterministically the
  other way, which turned an intermittent failure on one store into a reproducible one on three — and that is
  what made it diagnosable at all.
  The spec now seeds the retired row a minute earlier, so the claim order is the one it is actually about:
  head-of-line blocking, a retired row the conveyor reaches *first* being settled and stepped over. The
  behaviour was never wrong. A retired row behind claimable work is dead-lettered on a later pass, and claimed
  rows leave the queued set, so it always eventually reaches the head.

- **PMC-046 — a paged read over a non-unique sort had no tiebreaker.** Opened and closed on 2026-08-21.
  DATA-0119 moved "the order a page is a window onto" to the framework, and closed the case where the caller
  named no order at all. Naming a sort is not naming a total order: paging by Status, where many rows share a
  Status, left the store free to break those ties differently per request, so page two repeated and skipped
  rows exactly as it would have unordered. `EnsureOrderForPage` now appends the entity identity to every
  paginated read — the caller's keys lead and keep their direction, the identity only settles rows they left
  equal, and it is skipped when the caller already ordered by identity. An unpaged read is untouched: with no
  window to be a window of, ties cost nothing and a second key would cost a sort.
  Found by reading, not by a failing test. Planning the DATA-0120 collapse meant reading `Order` across the
  four relational repositories; three were identical down to the comment and MySQL differed. The tempting
  conclusion was that MySQL had drifted. It had instead been appending the identity to every `ORDER BY` itself,
  and was the only store whose paged reads were stable — a majority-rule collapse would have deleted the fix
  and kept the bug on all four. MySQL's private copy is now gone, and `Order` is logically identical across the
  four, which is a concrete input to DATA-0120.
  `PageOrderOwnershipSpec` asserted the defect as intended behaviour — that a paged query with a caller sort
  reaches the adapter with exactly one sort spec — which is why nothing caught this. It now pins the decision
  in three parts and fails against a disabled fix.
  The risk in the change was that lengthening a sort would push an adapter off its native paging, and that
  turned out to be covered already: `SortPushdownConvergence.AssertNothingFallsBackAsync` pages by every
  portable scalar in both directions, so each of those queries now carries the appended identity and the
  guard fails if the store stops doing the work. It stayed green on all seven adapters that derive the
  oracle — SQLite, PostgreSQL, CockroachDB, SQL Server, MySQL, MongoDB and Couchbase.

- **PMC-038 — MySQL could not write a null.** Opened 2026-08-20, closed 2026-08-21. The entry recorded a
  seeding failure that blocked the shared filter corpus; the premise check found the corpus was not the subject.
  `MySqlDialect.Read` built every scalar as `CAST(JSON_UNQUOTE(JSON_EXTRACT(...)) AS SIGNED)`, and
  `JSON_UNQUOTE` renders a JSON null as the four-character string `null`. That expression is what a stored
  generated column is built from and what every filter and order emits, so an entity with a null nullable
  scalar could not be inserted at all — `Truncated incorrect INTEGER value: 'null'` — and no filter could be
  evaluated across one. Reading a JSON scalar now goes through `JSON_TYPE`, which separates a JSON null from a
  string that happens to read "null", so a real value spelled that way still round-trips. MySQL now runs the
  shared `FilterConvergence` oracle, which is the thing the register entry originally asked for and the thing
  that would have caught this years earlier. The defect had survived because MySQL's suite was the smallest of
  the relational set and nothing in it wrote a null. Existing tables keep the old expression: PMC-045.

- **PMC-041 — a declared index built nothing on four of the six stores that should build one.** Opened and
  closed on 2026-08-21. `[Index]` now becomes a real index on PostgreSQL, CockroachDB, SQL Server, MySQL and
  Couchbase, joining SQLite and MongoDB, and each store proves the planner will use what it built rather than
  only that it exists. The survey that scoped the work is the durable part: ElasticSearch and OpenSearch index
  every field through their own mapping, and Redis, JSON and InMemory have no secondary-index concept, so all
  five are correctly out of scope rather than merely unimplemented.
  Two shapes, both driven by what the store already holds. PostgreSQL and CockroachDB build an expression index
  from the dialect's own read — usable only because every cast that dialect emits (`boolean`, `bigint`,
  `numeric`) is immutable. SQL Server and MySQL index the persisted column they already compute per mapped
  scalar, which is the column their reads resolve through, so the optimizer substitutes it without the query
  naming either. Couchbase builds a secondary GSI over the same path grammar its filters use; its path spelling
  had to be lifted out of the generic document plan first, because an index built for a container and a filter
  compiled for an entity have to agree character for character or the query service ignores the index.
  The find that justified the whole slice came from asking what an index does to a text property: SQL Server
  built one over `nvarchar(4000)` and then rejected the first insert whose key exceeded 1700 bytes. Trading
  "the index does nothing" for "the index breaks writes" would have been strictly worse, and no existing spec
  covered it. Both stores now decline a key they cannot hold and record it as unproved; the deeper fix is
  PMC-043.

- **PMC-040 — the relational schema seam was registered and unused.** Opened 2026-08-20, closed 2026-08-21.
  All four relational runtimes now route schema work through `RelationalSchemaOrchestrator`, and the four
  hand-rolled `*Schema.cs` files (SQLite 201, MySQL 326, SQL Server 140, Npgsql 103) are deleted.
  The two questions the entry asked were answered by the tree rather than by argument. **Connections:** three
  of the four already received an open connection from their repository, so the executor takes one and lives
  for a single schema operation; only SQLite differs, because opening a managed SQLite connection *creates the
  database*, so its executor opens non-creating to read and creating to write. That distinction was not
  cosmetic — the first adoption made SQLite materialize its file before the consent gate, and
  `NoDdl_remains_an_explicit_non_creating_policy` had been passing without ever reaching the assertion that
  would have caught it, because a message-wildcard assertion failed first. **Column sets:** the grouping
  matched (all four group bindings by `PhysicalPath.Name`), but the plan could not express two things the
  adapters built — SQLite's `INTEGER PRIMARY KEY AUTOINCREMENT`, which reconstructs from a single
  `IsIdentity` column that is also `IsGenerated`, and the persisted computed columns SQL Server and MySQL
  create per mapped scalar, which had to become a framework decision the store merely consents to.
  Adoption also removed a second mapping compiler (`RelationalCompatibilityMapping`, reached only through the
  type-based orchestrator entry) that would have validated a shape no adapter reads or writes, and four
  executor members that existed only as defaults feeding other members. `NativeTypeFor`, added the previous
  day to make adoption lossless, turned out to be the wrong half of the answer and was replaced by
  `ColumnMatches`: rendering the expectation into the store's vocabulary still left the framework comparing
  spellings it cannot judge. Details and the full seam rationale are in DATA-0119's implementation section.

- **PMC-039 — sparse Entity projection removed rather than pushed down.** Opened and closed on 2026-08-20.
  The question was why no adapter pushed a projection down. The answer was that the feature should not have
  been in the data layer at all.
  `Data<T>` returns `TEntity`, so a partial result still had to *be* an entity. The fallback cloned the
  materialized row and assigned `default` to every unselected property, which meant: no IO or transfer was
  saved, since the row was read in full and then blanked; the result lied, because `0` and `0001-01-01` are
  indistinguishable from real values with nothing marking them absent; it violated the entity's own
  invariants, blanking a `List<T>` that its declaration guarantees is never null; and because the Id was
  deliberately preserved, saving such an entity wrote the blanks back. Measured on SQLite: a widget projected
  to `Name` came back `Sequence=0 ObservedAt=0001-01-01 Sightings=NULL`, and one `Save()` later the stored row
  had lost all three, permanently and silently.
  Nothing in the tree constructed one — no web surface parsed a field list, no Entity static built one, and
  the only caller was a Data.Core spec. Two mechanisms already cover the intent honestly: `RecordSet` for a
  partial data shape (which the fallback's own error message pointed at) and `IProjectionOf<TEntity, TSummary>`
  summary DTOs, plus `RowProjection` for field visibility, for a response shape.
  So `Projection`, `QueryDefinition.WithProjection`, `InMemoryEntityProjection`,
  `RepositoryQueryResult.ProjectionHandled` and `QueryReceiptAxis.Projection` are gone, along with the
  hardcoded `ProjectionHandled = false` in all seven adapters and a dead projection branch in
  `RelationalCommandPlanner.Query`. This is a compile-level break for an external adapter that set that
  property — deliberately so; the alternative was leaving a published surface whose only behaviour was to
  corrupt data. Sparse fieldsets, if they return, belong on a shape that can express absence.


- **PMC-036 — every store computes a collection order key itself.** Opened and closed on 2026-08-20.
  `?sort=-Sightings.LastChangedAt` — "by each widget's latest sighting" — is an aggregate over a nested array.
  No adapter pushed it down: the mapping compiler stops at a collection of objects, because such a collection
  has no single queryable physical path, so every relational runtime declined the key and MongoDB broke out of
  its sort loop. Answers stayed correct because `InMemorySorter` finished the ordering, which meant
  materializing the whole result and giving up native paging for a query written in one line.
  Verified before the fix rather than assumed: the SQLite repository's own receipt reported `handled=0/1` for
  the collection key and `1/1` for a scalar one.
  Every store Koan ships on can express it, and now does — `ARRAY_MAX` over an array comprehension (Couchbase),
  `jsonb_array_elements` (PostgreSQL, CockroachDB), `OPENJSON` (SQL Server), `json_each` (SQLite), `JSON_TABLE`
  (MySQL), and `$max` over a `$map` (MongoDB, which cannot sort by an expression through `find` at all, so the
  query runs as a pipeline that adds the value, sorts and pages on the server, and removes it again).
  One rule decides it for the whole relational family — `RelationalCollectionOrder` locates the array inside the
  document the root binding owns — and one dialect member expresses it, so there are four grammars rather than
  four algorithms. A dialect that cannot express it declines the ordering rather than refusing the query, and
  then does not paginate either: a page is only a page of an ordered set.
  Proved by `SortPushdownConvergence`, a cross-adapter oracle that asserts both halves — the receipt claims the
  key, and the ordering equals the one the in-memory sorter would have produced. Its corpus is adversarial and
  total: several sightings per widget so taking the first element diverges, indices of 3 and 20 and 30 and 40 so
  comparing extracted JSON as text diverges, one widget with no sightings so the NULL aggregate must land where
  the framework puts it, and no two widgets sharing a value so no assertion rests on a database promising an
  order among equals. PostgreSQL needed its null placement stated, since it sorts NULL as larger than every
  value — the opposite of the framework's sorter.
  The oracle also surfaced a defect of its own: Couchbase issued `CREATE PRIMARY INDEX` against a collection the
  query service had not seen yet, so the *first* query against a newly created collection could fail with
  keyspace-not-found. Creating the collection is answered by the data service and the statement by the query
  service, and the cluster map reaches the second a moment later. That wait is now bounded and explicit.


- **PMC-020 (first half) — durable test evidence.** Delivered on 2026-08-20. Every project in the
  ratchet's test leg now emits a TRX, and `scripts/aggregate-test-evidence.ps1` merges the run records
  and those TRX files into one `artifacts/ratchet/test-manifest.json`: per-project and aggregate
  total/passed/failed/skipped, wall duration, lane, exit code, and `startedUtc` so completion order is
  recoverable from a parallel wave. It is written **before** the verdict, so a red run keeps its
  evidence, and it never changes pass/fail — the ratchet still decides from process exit codes.
  A project that produced no readable TRX is named as an evidence gap rather than left as a silent hole
  in the totals, which is what a host killed by `--blame-hang-timeout` looks like.
  The aggregator is a separate script precisely so its failure paths could be proved: crafted inputs
  covering a passing project, a failing one, skips, a hung project with no TRX, and a corrupt TRX
  produce total=49/passed=43/failed=3/skipped=3 with the failed and unreported projects named, and the
  aggregation still exits 0. End-to-end proof came from a real RED run — 113 projects, 4998 tests,
  4959 passed, 17 failed, 22 skipped, 13 projects named, 0 unreported. The remaining CI-parity half
  stays open under PMC-020.

- **PMC-005 / PMC-018 / PMC-027 — premises verified against the tree and found lapsed.** Closed on
  2026-08-20 by a sweep of the whole open register rather than by new work.
  **PMC-005** (release-tooling worktree discovery): `Koan.Packaging.FindRepositoryRoot` already accepts
  both forms — `Directory.Exists(gitPath) || File.Exists(gitPath)` — so the `.git` indirection file used
  by linked worktrees resolves. Exercised incidentally the same day: two linked worktrees were created
  and tooling ran from the repository without incident.
  **PMC-018** (remote providers pre-resolving generic `ConnectionStrings:Default`): a whole-tree search
  finds exactly one reference, in SQLite's constants, and the entry itself records that SQLite no longer
  has the ambiguity. None of the five named remote providers — PostgreSQL, SQL Server, CockroachDB,
  MongoDB, Couchbase — carries it.
  **PMC-027** (`Koan.Storage.Connector.Local.Tests` unmarked and non-compiling): the project declares
  `Microsoft.NET.Test.Sdk`, is discovered by the ratchet, builds, and passes 31/31.

- **PMC-013 — Web Backup graduation.** Closed on 2026-08-20; every premise had lapsed. The entry
  described Web operation tracking, a cancel endpoint that marks tracker state without stopping I/O,
  sequential global export despite `MaxConcurrency`, and a missing Web suite. **`Koan.Web.Backup` was
  retired** in `33a50be67` ("retire unsupported backup projection"), which is the graduation decision
  the entry asked for, taken in the honest direction: the unsupported projection was removed rather
  than polished. No source file, solution entry, or package remains; only untracked build leftovers.
  The separate "complete ZIP is memory-resident" claim also lapsed — `a39edffa4` moved archive creation
  onto a temporary `FileStream`, and zero `MemoryStream` uses remain in `Koan.Data.Backup`. What
  survives is `Sylin.Koan.Data.Backup` alone, still `unassessed` in the product surface, whose read
  path is now governed by DATA-0113. Any future Web projection is a new decision, not this entry.

- **PMC-014 — Data Backup metadata-only encryption flags.** Closed on 2026-08-20; the entry was stale.
  `Encrypt` and `EncryptByDefault` were removed in `a39edffa4` (graduate backup and soft-delete
  leaves), so the footgun the entry described — a flag named `Encrypt` that records manifest intent and
  encrypts nothing — no longer exists. Verified by inspection rather than by the register: `BackupRequest`
  exposes only `StorageProfile`, `Partition`, and `PageSize`; `BackupArchiveManifest` carries no
  encryption field; no encryption claim survives in the reference, guide, or recipe surfaces; and
  `TECHNICAL.md` states the boundary positively — the package "does not own … encryption". The register's
  own choice ("remove/rename the metadata-only flags, or specify authenticated encryption") was taken in
  the first direction, and archive encryption remains an unclaimed capability rather than a half-built one.

- **PMC-003 / PMC-028 / PMC-032 — warning and connector-test graph premises.** Closed by current
  evidence in R12-02 on 2026-07-19. R11-07's exact public-release solution build reports zero warnings
  and errors, superseding PMC-003's 19-warning baseline. SQLite's discovery fake implements
  `ResolveServiceIntent`, and its full connector-owned Release suite passes 36/36. A fresh XML-based
  inventory of every test-project `ProjectReference` finds zero missing targets and no retired
  `Koan.Core.Adapters.csproj` reference. No suppression, replacement dependency, or compatibility fake
  was added for defects that no longer exist.

- **PMC-024 — direct-reference build-fixture isolation.** Resolved in R12-02 on 2026-07-19. The
  fixture no longer restores `src/Koan.Core` under a throwaway NuGet configuration. A synthetic
  `Koan.Core` project, package feed, global package cache, intermediates, outputs, and every evaluated
  `ProjectReference` now live under one temporary root; only the real composition targets are imported
  read-only. The focused fixture passes 1/1 offline, preserves package/project direct-reference truth,
  asserts graph containment, and proves a planted missing package fails inside the temporary root.
  No asset backup/restore, repository build lock, or production helper was added.

- **PMC-007 / PMC-015 — Web filtering and portable Entity names.** Closed in R12-02 on
  2026-07-19. PMC-007's unsafe-degradation premise was stale: Web parses one filter AST, Data pushes
  only declared nodes and evaluates the residual before sort/pagination, and malformed, unknown, or
  unsupported input maps to 400. The shared HTTP surface now pins compound operators, mixed-case
  field binding, and malformed/unknown anti-drop behavior across InMemory 74/74, JSON 52/52, and
  SQLite 52/52. PMC-015 is repaired at Data's existing cached first-use shape guard: public Entity
  properties that differ only by case reject with both names and one rename correction before adapter
  creation. Exact guard/activation tests pass 9/9 and affected builds are warning-clean. No provider
  flag, model annotation, persisted-name mapping, or new filter layer was added.

- **PMC-002 / PMC-004 — MCP host transport and application JSON contract.** Resolved locally in
  R12-02 on 2026-07-19 as a deliberate pre-0.20 break. The transitional HTTP master/nullable
  override, SSE-derived primary names, and per-Entity transport metadata are removed. Hosts now own
  three explicit edges (`EnableStdioTransport`, `EnableStreamableHttpTransport`, and deprecated
  `EnableLegacySseTransport`), one `HttpRoute`, and unified session limits. Entity and custom-tool
  payloads, schemas, deltas, binding, and Code Mode share one camelCase `[McpIgnore]`-aware contract;
  protocol DTO names remain spec-owned. Conformance passes 80/80, Streamable/legacy HTTP 19/19,
  field exclusion 5/5, Code Mode 27/27, source FirstUse/GoldenJourney 3/3, bootstrap pillars 13/13,
  and the MCP Release build is warning-clean. Public docs pass 233/42. No aliases preserve the
  misleading pre-preview vocabulary.

- **PMC-001 — Entity / Jobs metric collision.** Resolved locally in R12-02 on 2026-07-19. The
  `JobMetric` Entity was framework-owned persistence accidentally exposed as application language;
  users were only taught its summary operation. The row is now internal with its CLR type, storage
  identity, and `Count` field unchanged. One public `JobMetrics.Summary(...)` concept owns retained
  throughput intent. Jobs passes 84/84, Tenancy 16/16, and the Jobs Release build is warning-clean.
  No alias, field migration, model attribute, or analyzer assembly was added.

- **PMC-025 — Windows first use.** Closed as a stale historical premise in R12-02 on 2026-07-19.
  The current `ApplicationProbeHost` supplies no EventLog override, the exact R08-05 Windows candidate
  passed package-only FirstUse, R11-07 passed the same public contract, and the current source
  FirstUse proof passes 1/1. The repository's Microsoft.Extensions.Logging.EventLog 10.0.8 runtime
  disables the EventLog sink after a `SecurityException` while other providers continue. Koan keeps
  standard ASP.NET Core provider ownership rather than adding a parallel logging switch or removing
  application-selected providers for a defect that no longer reproduces.

- **PMC-033 — Storage layered activation.** Resolved locally in R12-02 on 2026-07-19. The historical
  GardenCoop C2 trigger had already disappeared with its obsolete Storage dependency and now passes
  1/1. The remaining invariant was repaired at the Storage chokepoint: a reference makes Storage
  available; a declared profile/default or actual service use activates its one routing plan.
  Profile configuration still validates at startup, unconfigured service use still fails with the
  existing correction, and facts distinguish inactive availability from selected routes. Media Web
  passes 8/8, Storage 20/20, bootstrap pillars 13/13, and Data.AI 87/87 without sample-only profiles.

- **PMC-031 — Tenancy / Local Storage test composition.** Resolved locally on 2026-07-18. The shared Tenancy runtime
  fixture now supplies an isolated temporary Local Storage profile because its test graph intentionally references the
  functional Local connector. The two formerly blocked Cache/Tenancy contracts reach their assertions and the full
  focused Tenancy suite passes 87/87 without weakening Local's missing-path validation. The separate question of
  transitive unused Storage activation was later resolved by PMC-033.

- **PMC-010 — public module catalog.** Promoted into
  [R08-03](work-items/r08/R08-03-canonical-product-surface.md) and resolved locally on 2026-07-17.
  The stale hand-maintained catalog is retired. One compiler now joins 108 evaluated standard
  package/project facts to 14 explicit evidence claims and generates both human and machine references.
  Thirty-seven missing package-owned READMEs and 88 unassessed packages remain visible rather than being
  converted into support claims.
- **PMC-019 — connector logging security.** Promoted into
  [R08-02](work-items/r08/R08-02-safe-connector-telemetry.md) and resolved locally on 2026-07-17.
  `Redaction` owns one credential grammar, `KoanLog` sanitizes structured connector context once, shared
  configuration/discovery/orchestration chokepoints own narration, and a repository policy rejects direct
  logger bypasses in the bounded source surface. Runtime mutation proof passes 28/28, the policy passes 1/1,
  and 17 affected connector projects build. The exact non-claim excludes arbitrary application, driver,
  third-party, and business-payload logs.
- **PMC-016 — exact cross-event release recovery.** Promoted into
  [R08-01](work-items/r08/R08-01-durable-release-wave.md) and resolved locally on 2026-07-16. One
  hash-bound GitHub Release escrow retains the exact nupkg/snupkg and evidence bytes; the resumable
  coordinator reconciles an incomplete prior wave before later source, replays symbols, and refuses
  public identity without exact prepared custody. Failure-injection and workflow-contract evidence is
  recorded in R08-01. Real immutable-Release observation remains an operator-gated R08 step.
- **PMC-017 — release input lineage.** Promoted into
  [R08-01](work-items/r08/R08-01-durable-release-wave.md) and resolved on 2026-07-16. Lineage schema
  3 persists normalized per-package evaluated input maps; compiler and planner independently compare
  prior and current ownership. Real Git add/change/rename/delete scenarios select only the owner,
  while missing, noncanonical, or tampered maps fail closed.
- **PMC-012 — layered service discovery.** Resolved on 2026-07-15 through ARCH-0114's uniform
  adapter-declaration and engine-activation contract. Core Unit passes 112/112, Mongo 70/70, and the
  prior Couchbase 17/17 proof is corroborated by the final clean aggregate completing without the
  earlier node-readiness failure. No adapter-specific election fork remains.
- **PMC-011 — automatic reverse-dependent release closure.** Promoted into
  [R07-03](work-items/r07/R07-03-automatic-package-lineage.md) and resolved on 2026-07-15. The
  evaluated graph, durable Git lineage, complete breaking closure, and independent leaf behavior are
  recorded in that card and in [PROGRESS.md](PROGRESS.md). This history remains here so the original
  release-safety concern cannot disappear when the active register changes.

## Working order

Before the first real automatic publication, compile one evidence-derived public product boundary;
PMC-019 and PMC-016 are already resolved locally, while release recovery still awaits real observation.
Then start with the inventory-oriented items (`PMC-003`, `PMC-007`, `PMC-009`, `PMC-018`), because they establish
the real size and severity of the work. Discuss
compatibility-sensitive API choices (`PMC-001`, `PMC-002`, `PMC-004`, `PMC-008`, `PMC-014`,
`PMC-015`, `PMC-022`, `PMC-023`) and the bounded Backup graduation decision (`PMC-013`) next. Finish with independently
useful release-tooling polish (`PMC-005`, `PMC-006`, `PMC-020`). Fewer cards may result if one root repair
responsibly closes several entries.

## Closure rule

Remove an entry only when its decision and evidence are linked from `PROGRESS.md`, or when a recorded
review rejects the work as unnecessary. Do not mark an item complete merely because its warning was
suppressed, its documentation was softened, or the active cycle ended.

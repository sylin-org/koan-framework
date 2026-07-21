# JOBS-0008: Lane-Fair Dispatch (weighted-fair-queuing lane scheduler)

**Status**: Accepted (implemented; verified green on the in-memory and SQLite tiers — see §Shipped)
**Pillar**: Jobs
**Depends on**: JOBS-0005 (orchestrator rebuild — the durable substrate this builds on), JOBS-0007 (dispatch-time pools/gates — its head-of-line paging is subsumed here), ARCH-0079 (integration tests are canon)
**Supersedes**: the *selection* half of JOBS-0005 §7/§13 (global `(VisibleAt, FirstSubmittedAt)` `ClaimNext` ordering; lane as a saturated-blocklist) and the JOBS-0007 forward-paging `SelectCandidates` loop. The claim *primitive* (CAS / optimistic / ticket), exclusivity (§17.2), pool/gate election, lease/reaper, outbox, and retention are **retained unchanged**.

> **Shipped implementation (reconciles the §Decision below to what was built).** The schedulable unit is the lane and fairness is decided across lanes — as designed. The fairness *state* is a **per-node weighted-fair-queuing (WFQ) virtual-time** selector (`LaneFairSelector`, a pure function whose state each ledger holds as a per-process `Dictionary`). WFQ-by-virtual-time is clock-independent and deterministic (a wall-clock deficit refill grants zero credit between claims in a burst or on a frozen test clock), and is **starvation-free and weight-correct per node** — which is the requirement: every node fairly multiplexes the lanes it claims, so no lane starves anywhere. Weights ship via `JobsOptions.LaneWeights`.
>
> **A durable cross-node `LaneCursor` was attempted and REJECTED** (the §Decision text below describing it is superseded by this note). Making the cursor durable means a per-claim read-modify-**CAS write to a single shared per-lane row** — a write-contention hotspot on the dispatch hot path. SQLite (single-writer) surfaces it as `'database is locked'`; every store pays it as serialization of a hot lane's claims through one row. The benefit it buys — *exact* global weight proportions under skewed multi-node feed — is a refinement the system doesn't need (per-node WFQ is already starvation-free globally). If exact global proportions are ever required, the contention-free way is **node-sharded, batched** state (the §20.2 `JobMetric` pattern: each node writes only its own shard periodically, reads union the shards), **never a per-claim shared write**.
>
> **Self-reporting (`JobsHealthContributor`)** ships as a **cheap, global** probe — a few index-served COUNTs + one oldest-due seek (queued/running/reclaim-backlog + oldest-queued-age; `Degraded` past the opt-in `JobsOptions.QueueAgeWarning`). It is deliberately NOT per-lane: the framework auto-runs health contributors at boot, so a per-lane fan-out would be a scan-storm contending with real work. The global oldest-queued-age IS the starvation tripwire (if the oldest job anywhere has aged past budget, some lane is starved); per-lane drill-down is a separate on-demand concern.
>
> Also shipped: the per-lane indexed head seek with a **buried-work guard** (skip the per-lane fan-out unless work exists outside the window — keeps the claim O(window) on a deep single-lane backlog), the **Mongo index field-name fix**, and **relational composite-index creation** (a latent gap: freshly-created relational tables previously had no secondary indexes).

---

## Problem

A staged content pipeline (crawl lanes → processing lanes → `translation`, fed by reconcile/retry schedulers, with an AI-server pool) reported that under deep backlog the `translation` lane is **starved**: its jobs are Queued, due, ungated, eligible — yet effectively never claimed — while continuously-fed crawl lanes drain normally. Not a tuning gap; a structural defect, and one instance of a class (any pipeline with a perpetually-fed upstream lane; any bursty-vs-steady lane mix; any priority need).

### Root cause: a flat global row-order is the wrong scheduling substrate

`ClaimNext` selects the next job by a **single global ordering `(VisibleAt, FirstSubmittedAt)`** over the whole Queued set, with lanes applied only as a *post-hoc exclusion* (`SaturatedLanes()` skips a lane already at its `MaxConcurrency` ceiling — it never *reserves* a lane a share). Three consequences, all confirmed in code:

1. **Cross-lane starvation is guaranteed.** A chain follow-on is appended with `VisibleAt = now` (`SettleSuccessAsync` → `JobRecordFactory.Create(... now ...)`, `JobOrchestrator.cs:237`). A downstream stage is therefore *always newer* than a perpetually-fed upstream lane's backlog, so it never enters the oldest-N claim window. `WorkerConcurrency` (`JobsOptions.cs:46`) is one global cap a hot lane drains first; per-lane `MaxConcurrency` caps but never reserves (`SaturatedLanes`, `JobOrchestrator.cs:396`; `LaneSem`, `:405`).
2. **O(backlog) dispatch.** The flat order forces JOBS-0007's forward-paging band-aid (`SelectCandidates`, `DataJobLedger.cs:271-286`) to walk past unclaimable windows on every claim.
3. **Hand-mirrored selection logic.** `IsClaimable`/`SelectCandidates` (`DataJobLedger.cs:250-305`) and the inline LINQ (`InMemoryJobLedger.cs:73-101`) encode the *same* selection twice, kept in lockstep by a comment — the ARCH-0079 convergence hazard.

The deciding insight: **`VisibleAt` is overloaded as both eligibility and dispatch priority, so "later in the pipeline" = "newer" = "lower priority."** No fairer scan over a flat order fixes a substrate whose primary key equates recency with priority. The substrate must change.

### Compounding defects (fixed alongside, not the root)
- **Mongo index field-name mismatch (framework-wide):** `MongoRepository` builds `[Index]` keys with the raw PascalCase `property.Name` while documents store camelCase, so `ix_jobs_claim` does not cover the claim query → unindexed in-memory sort. A *throughput* defect (the sort stays chronologically correct, DATA-0100). The new lane index depends on this being fixed.
- **No self-reporting:** the Jobs pillar ships zero `IHealthContributor`, and `JobMetric` records only terminal outcomes — a never-claimed job produces no signal. This is *why* starvation was invisible for hours.

---

## What the system is asked to do (use cases → requirements)

From the authoring guide read as a requirements doc + the reporting consumer.

**Tier 1 — simple cases served well (preserve verbatim):** a job is an entity with one `Execute`, no visible queue (§1); one entity, many actions (§2).
**Tier 2 — composite at-scale (where it breaks):** linear pipelines (§3); per-work-item serialization (§4); lanes for isolation (§4); scheduled feeders (§7); cooperative gates + dispatch-time pools (§6, JOBS-0007); windowed bulk (§8.1); the capability ladder (§10).

| # | Requirement | Today | This ADR |
|---|---|---|---|
| Ergonomics | entity + `Execute`, no visible queue | ✅ | unchanged |
| Pipeline / chains | ordered stages, entity is the flowing state | ✅ | unchanged |
| Serialization | one work-item at a time | ✅ (busy-set + `Exclusive`) | unchanged |
| **Fairness** | no lane starves another regardless of feed rate or pipeline position | ❌ | **fixed (durable DRR per lane)** |
| Isolation | per-lane concurrency cap | ⚠️ cap only | cap **+ reserved share** |
| **Eligibility** | gated/pool-blocked/not-yet-visible work doesn't consume dispatch attention | ❌ head-of-line | per-lane head seek + `IsClaimable` (subsumes JOBS-0007) |
| Ladder | durable, distributed, at-least-once, recovery, outbox | ✅ | unchanged |
| **Self-report** | surface queue/lane health | ❌ | `JobsHealthContributor` |
| Backlog safety | a lane can't grow unbounded | ⚠️ warn only | optional per-lane `MaxBacklog` |

The bold rows are what the workload needs and the flat-queue substrate cannot give.

---

## Decision

**The schedulable unit is the LANE, not the row. Fairness is decided across lanes from durable, shared state (deficit round-robin), and the ledger is read in O(lanes) indexed seeks — never re-ordered globally per claim.** The existing CAS remains the unchanged claim boundary underneath.

Concretely:

1. **One new durable entity `LaneCursor`** (`Id = lane`, `Deficit`, `Weight` default 1, `LastRefillAt`). It carries the lane's DRR credit; it is refilled by elapsed-time × weight and decremented on claim, both via the **existing `ConditionalReplaceAsync` CAS** (`DataCaps.Write.ConditionalReplace`). Lanes are enumerated once at boot from `JobTypeRegistry.All` → `ResolvedActionPolicy.Lane` (which defaults to the action name) — no discovery scan.
2. **One new composite index** `(Lane, Status, VisibleAt, FirstSubmittedAt)` on `JobRecord`, so each lane's claimable FIFO head is an O(log n) seek.
3. **The claim becomes:** take the existing single `running` snapshot (reused for both the exclusivity busy-set *and* per-lane in-flight tally — zero extra cost); refill lane deficits; among lanes below `MaxConcurrency` in-flight with `Deficit > 0`, pick `argmax Deficit`; seek that lane's claimable head (skipping exclusivity/gate/pool-exhausted via the **existing `IsClaimable` predicate**); CAS-claim it; decrement that lane's deficit. If a lane's head isn't claimable, move to the next lane (this subsumes JOBS-0007 forward-paging structurally).

`JobRecord` stays the durable record and the dispatch row. The authoring surface (`Submit`, `Execute`, `[JobChain]`, `[JobAction]`, `[JobIdempotent]`, `[JobGate]`, `[JobPool]`, `[ParallelSafe]`) is unchanged. Net change: **+1 entity, +1 index**, and a *deletion* of the forward-paging scan.

### Why not the alternatives (recorded, because the design dialogue explored them)

- **(rejected) Work-item mailbox as the durable unit** (replace `JobRecord` with a per-`(WorkType,WorkId)` mailbox; hierarchical fair scheduler over mailboxes). Two fatal problems: (a) **it is orthogonal to the bug** — a lane spans thousands of work-items, so making the work-item the unit still requires a fairness layer *above* the mailboxes across lanes; the mailbox does not by itself decide crawl-vs-translation. (b) It is a full break-and-rebuild touching every file + every spec + the dashboard/query model, and its fairness layer would be **in-memory per-node state** requiring cross-node reconciliation that the in-memory tier (no second node) structurally cannot test — the ARCH-0079 anti-pattern. Per-work-item serialization is already handled (busy-set + `Exclusive`); chains already work. We do not rebuild what works to fix a different axis.
- **(rejected) In-process dispatch-seam** (bounded hydration + an in-memory per-lane DRR dispatcher). Same volatile-per-node-fairness flaw: each node has its own deficit counters, so global fairness diverges under skewed feed, and the volatile ready-set must be reconciled on every membership change and crash. Net **+~5 concepts** (dispatcher + DRR engine state + hydration cursor + reservation + per-lane queues). The durable `LaneCursor` gets the same DRR fairness with shared state and **fewer** parts.
- **(rejected) Capability-gated store-side fair claim** (`SKIP LOCKED` / lateral oldest-per-lane where the adapter supports it). JOBS-0005 §20.3 already rejected `SELECT … FOR UPDATE SKIP LOCKED LIMIT 1` for a structural reason: Koan persists every entity as `(Id, Json)`, so a row-lock claim cannot express the cross-row exclusivity invariant on Mongo's single-doc atomicity without a racy claim-then-revert. A *fair-selection* lateral join is strictly harder and would ship a Postgres/SqlServer-only fast path **plus** the universal fallback — two selection implementations to keep in lockstep (the exact §20.3 pain). The capability ladder's role is grading the **claim primitive** (CAS vs optimistic, already shipped), not the **selection policy**, which stays universal and on-ledger.
- **(complement, not substitute) Admission control / per-lane `MaxBacklog` at submit.** Necessary for the job-per-row pileup but cannot deliver fairness — and on a *chain* it would throttle the upstream that *produces* downstream work, starving it harder. Adopted as a separate opt-in knob (below), never as the model.

---

## Design

### `LaneCursor` (the only new persistent concept)
`LaneCursor : Entity<LaneCursor>` — `Id = lane name`; `double Deficit`; `double Weight` (default 1); `DateTimeOffset LastRefillAt`. Durable on the data tier (mutated via `ConditionalReplaceAsync`); a `Dictionary<string, LaneCursor>` under the in-memory tier's lock. Adapters without `ConditionalReplace` fall back to in-memory deficit — the same degradation `ClaimNext` already tolerates.

### `IJobLedger` reshape (in words)
- **Extract** the claim tail of `ClaimNext` into `ClaimRow(record, guard, owner, now, leaseUntil, ct)` — the existing CAS / under-lock / optimistic mark, unchanged semantics (single-execution, exclusivity preserved).
- **Add** `HydrateLaneHead(lane, now, busy, gatedKeys, claimablePools, ct)` → the single oldest claimable Queued row for that lane (`Status==Queued ∧ Lane==lane ∧ VisibleAt<=now ∧ CancelRequestedAt==null`, ordered `VisibleAt, FirstSubmittedAt`, filtered by the existing `IsClaimable`), via the new composite index. O(log n).
- **Add** `ReadLaneCursors(lanes, ct)` / `WriteLaneCursor(cursor, guard, ct)` — refill/decrement via CAS (dictionary under lock for in-memory).
- **Keep** `Append`/`Update`/`Progress`/`Stuck`/`NonTerminal`/`InStage`/`Query`/gates/retention/`CountActive` unchanged.

The **selection algorithm lives once in the orchestrator** (refill → pick max-deficit non-saturated lane → hydrate head → `ClaimRow` → decrement). Both ledgers shrink to mechanical I/O, so they can no longer drift in scheduling semantics — tier convergence becomes structural, not disciplinary.

### Where each concern lives
- **CAS** at `ClaimRow` — unchanged single-execution + exclusivity + at-least-once across all five tiers.
- **Fairness/priority** in the orchestrator's selection function — the only place scheduling decisions are made.
- **Exclusivity / lane in-flight tally** from the single `running` snapshot (`DataJobLedger.cs:50`), reused for both — replaces `SaturatedLanes` at zero extra cost.
- **Pool/gate** election stays in the claim path, *after* the lane is chosen: an exhausted pool's head simply isn't claimable, so selection moves to the next lane (subsumes JOBS-0007 forward-paging with no special code).

---

## Fairness policy (precise) + starvation-freedom

**Deficit Round Robin over the static lane set; weight = quantum.**
- `Deficit_L += Weight_L × (t − LastRefillAt_L)`, capped at `MaxDeficit_L = MaxConcurrency_L` (so an idle/low-traffic lane can't hoard unbounded credit); refill is **per drain iteration, not per claim** (and batchable).
- Among lanes below `MaxConcurrency` in-flight with `Deficit_L > 0`, select `argmax Deficit_L`; hydrate its head; if none, reset `Deficit_L = 0` (DRR empty-lane rule) and try the next.
- On a successful claim, `Deficit_L −= 1`.
- **Priority** = `[JobLane(Weight=N)]` (new optional knob) or `JobsOptions.LaneWeights["translation"]=N`. Default weight 1 ⇒ equal-share round-robin, zero-config — the Sidekiq/Faktory *weighted* (anti-starvation) family, never strict priority.

**Starvation-freedom (proof sketch).** Any non-empty lane *D* accrues deficit at rate `Weight_D > 0` per iteration, a function of elapsed time — **not** row age. Within `⌈1/Weight_D⌉` intervals `Deficit_D ≥ 1`, making *D* eligible. Every competing lane that is serviced has its deficit *decremented*, while *D*'s only grows, so within a bounded number of iterations *D* attains `argmax` and is serviced. A perpetually-fed upstream lane cannot prevent this — serving it *spends* its deficit. Row recency is irrelevant. ∎ This is exactly what the flat `(VisibleAt, FirstSubmittedAt)` order cannot provide.

---

## Invariants preserved
At-least-once + idempotent delivery (constant across tiers); ledger is the single source of truth + single writer; transactional outbox on submit; retention/TTL; cancellation; the JOBS-0005 §17.2 exclusivity model and JOBS-0007 pool/gate election — all unchanged. **Nothing volatile to recover**: the cursor is durable, so a crash loses no scheduling state (the reaper's lease + `Stuck` sweep is the only recovery, as today).

---

## Staged migration (each step independently shippable + ARCH-0079 spec; in-memory tier first)

- **Stage 0 — Extract `ClaimRow` (no behavior change).** Refactor the CAS/optimistic/ticket tail out of `ClaimNext`. *Gate:* existing cross-tier `concurrent_claimers_take_distinct_jobs` (§20.3) stays green on all 5 tiers.
- **Stage 1 — DRR in the in-memory tier.** `LaneCursor` as an in-memory dictionary; orchestrator selection replaces `SaturatedLanes`-exclude; `HydrateLaneHead` over the in-memory store; durable tier behind a flag. *Spec:* `crawl_feed_does_not_starve_translation` (perpetually-fed upstream; assert downstream makes monotonic progress within a bounded iteration count) — **the spec that proves the bug is dead.**
- **Stage 2 — Composite index + durable `HydrateLaneHead`.** Add `(Lane, Status, VisibleAt, FirstSubmittedAt)`; indexed per-lane head seek. (Requires the Mongo index field-name fix.) *Spec:* `lane_head_hydration_is_indexed_not_scan` on PG/SqlServer/SQLite/Mongo via `KoanIntegrationHost`.
- **Stage 3 — Durable `Entity<LaneCursor>` + credit-CAS.** Global, shared fairness. *Spec:* `lane_fairness_is_global_across_nodes` (two orchestrators, skewed feed; service ratios track weights, not per-node).
- **Stage 4 — Delete forward-paging + `SaturatedLanes`.** The net removal that pays for the new entity. *Spec:* the JOBS-0007 head-of-line regression spec still passes — now satisfied structurally.
- **Stage 5 — `[JobLane(Weight=)]` + `JobsOptions.LaneWeights`.** *Spec:* `weighted_lanes_get_proportional_service` (3:1 ⇒ ~3:1) + `idle_lane_does_not_hoard_credit`.

**Stop-early honesty:** if after Stage 1 the starvation spec is green and no real deployment shows cross-node fairness divergence, Stages 3–5 are *deferrable* (the cursor stays a dictionary). The staging is designed so the cheapest correct point ships first and durable/weighted machinery is pulled in only when a multi-node or priority need actually appears.

Two adjacent items, sequenced independently:
- **Mongo index field-name fix** (`property.Name` → mapped element name) — its own change + `SURFACES.md` row + boot note (affects every Mongo index, not just Jobs). Stage 2 depends on it.
- **`JobsHealthContributor`** (per-lane depth, oldest-queued-age, reclaim backlog; Degraded on a configurable age budget) + optional per-lane `MaxBacklog` admission knob (off by default; reuses the pushed `CountActive`).

---

## Consequences

**Positive** — fairness/priority/starvation-freedom are structural and **global** (durable shared deficit, no reconciliation protocol); dispatch is O(lanes) seeks (the per-claim `CountDocuments` + full `Running` scan + forward-paging disappear); selection logic exists once (convergence structural); self-reporting added; **removes more code than it adds**.

**Negative / cost** — a credit-CAS per claim on the durable tier (the one real hot-path cost; mitigated by time-batched refill + an optional short lane-credit lease); a new entity + index + boot-time lane enumeration.

**Risks + the telemetry that says over/under-built** — credit-CAS contention (watch `LaneCursor` CAS-loss rate: near-zero ⇒ the lease amortization is unnecessary; climbing ⇒ add it); lane-cardinality explosion if an author sets `Lane` per-work-item (mirror the §19.4 guardrail: warn past a threshold); low-traffic lanes hoarding credit (the `MaxDeficit` cap bounds it); per-node vs global fairness in the Stages 1–2 interim (divergent per-lane ratios across nodes ⇒ Stage 3 is needed; if single-node is the only deployment, Stages 1–2 suffice).

---

## Open questions (close before Accepted)
1. Credit-CAS amortization: ship the lane-credit lease in Stage 3, or only if CAS-loss telemetry warrants?
2. Weight surface: `[JobLane(Weight=)]` per lane vs `JobsOptions.LaneWeights` config vs both? Default strictly 1.
3. `JobsHealthContributor` thresholds: what default oldest-queued-age flips Degraded (off by default vs a generous default)?

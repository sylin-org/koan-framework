# Proposal: Stone Lifecycle Operations (Lift / Replace / Retire Safely) + Rule-Based Scaling

## Summary

Add a **stone-centric lifecycle feature set** that lets humans operate on **machines** (Stones) while apps continue to consume **offerings** (services) by intent. The flagship operation is:

> `garden-rake retire <old-stone> safely`
> **Move everything off the machine, verify, then wipe and retire it.**

This proposal also defines a small, semantic CLI vocabulary and the underlying "offering lifecycle adapter" contract so each offering can migrate correctly (cache cutover vs DB replication vs storage sync). It includes a simple rule-based election mechanism for:

> `garden-rake scale <offering> to N`

## Goals

* **Human-first DX/UX:** users think "this box is dying," not "mongodb is unhealthy."
* **One-liner operations:** default flows should be **one command** with a readable plan.
* **Offering-aware correctness:** migrations differ per offering (cache vs DB vs storage).
* **Safety by default:** avoid accidental data loss and accidental wipes.
* **Observe everything:** always show what will happen and what happened.
* **Stay Zen Garden-simple:** not Kubernetes; minimal scheduling, minimal configuration.
* **Honor material constraints:** users have 3-5 devices, not 300. Hardware is finite, physical, and fails. Operations respect this reality.

## Non-goals

* Full general-purpose orchestration / binpacking scheduler.
* "Bitwise cloning" of machines (OS image copying, secrets duplication).
* Automatically inventing replication topologies for every service (adapters opt-in).

---

## Mental Model

* **Stone (machine):** what humans see and replace.
* **Offering (workload/service):** what apps consume; runs *on* a stone.
* **Intent (address):** what apps resolve (`zen-garden:mongodb`), producing concrete endpoints.

**Principle:** *Apps talk to offerings. Humans tend stones.*

### Why Stone-Centric Operations Matter

**Cloud orchestration assumes infinite resources:**
```bash
kubectl scale deployment/webapp --replicas=10
# Assumption: compute available on-demand, just pay more
# Reality: abstracts away the machine entirely
```

**Zen Garden users have material constraints:**
```bash
garden-rake scale webapp to 10
# Reality: "You have 3 stones. Max capacity: 6 replicas."
# System response: honest feedback about physical limits
```

**Operations are fundamentally physical:**
- `replace old-stone` = "This laptop is dying, swap it for the thin client I bought"
- `retire stone-01 safely` = "Remove this machine from my office/closet"
- `lift old-stone` = "I'm unplugging this box for maintenance"
- `cordon old-stone` = "This device is flaky, don't put new things on it"

Users don't have infinite budgets or infinite hardware. They point at **physical devices** that overheat, run out of disk space, and eventually die. This proposal respects that reality instead of pretending infrastructure is weightless.

**This is not a limitation—it's honest engineering.** Small-scale self-hosting has different constraints than cloud hyperscale, and the tooling should acknowledge that.

---

## CLI: Semantic Actions

### Stone-centric commands (primary UX)

#### 1) `replace`

Replace hardware without rethinking the system.

**Explicit:**

```bash
garden-rake replace old-stone with new-stone
```

**Implicit ("this stone replaces old-stone"):**

```bash
garden-rake replace old-stone
```

**Semantics:** move all offerings off `old-stone` and onto `new-stone` (or suitable destinations), using offering-specific migration playbooks, then verify. Does **not** wipe by default (that's `retire safely`).

**Implicit rule:** `replace old-stone` only works when executed **on a stone** (i.e., the local agent can identify "this stone"). If run from a laptop/non-stone host, require the explicit form.

---

#### 2) `cordon` / `uncordon`

Prevent new placements on a stone.

```bash
garden-rake cordon old-stone
garden-rake uncordon old-stone
```

**Semantics:** stop scheduling new offerings to that stone; existing offerings keep running.

---

#### 3) `lift` (vacate/drain/evacuate)

Move everything off a stone (destinations auto-elected or specified).

```bash
garden-rake lift old-stone --to auto
garden-rake lift old-stone --to stone-02 --to stone-03
```

**Semantics:** enumerate all offerings on `old-stone` and migrate each using its adapter. Typically invoked before unplugging or maintenance.

---

#### 4) `retire`

Retire is about **removing a stone from the garden**.

```bash
garden-rake retire old-stone
```

**Default semantics:** mark as retired/quarantined (no scheduling, no participation), but **do not wipe**. This protects against accidental destruction.

---

#### 5) `retire safely` (flagship)

**Drain → verify → wipe → retire** in one semantic action.

```bash
garden-rake retire old-stone safely
garden-rake retire old-stone safely --yes
garden-rake retire old-stone safely --to auto
```

**Semantics (macro):**

1. `cordon old-stone`
2. `lift old-stone` (per-offering migration)
3. **verify**: health + resolution no longer returns `old-stone`
4. **wipe**: destroy identity + remove offering data (see Wipe semantics)
5. mark stone **retired** and stop participating/announcing

---

### Offering-centric commands (secondary UX)

#### 6) `move` (single offering)

Move *one* offering instance from stone A to stone B.

```bash
garden-rake move mongodb from stone-01 to stone-02
garden-rake move cache from stone-01 to stone-03
```

---

#### 7) `place` (run offering on a stone)

```bash
garden-rake place mongodb on stone-02
```

---

#### 8) `scale` (rule-based election)

```bash
garden-rake scale app1 to 2
```

**Semantics:** increase/decrease desired instance count; elect candidate compute stones using rules + scoring; deploy; update gateway routing/pools.

---

### App/intent command (debug/understanding)

#### 9) `resolve`

```bash
garden-rake resolve mongodb
garden-rake resolve zen-garden:mongodb
```

Shows what endpoint(s) the app would receive right now (and why).

---

## "Plan-first" UX (trust builder)

High-impact operations should default to showing a plan before execution.

Example:

```bash
garden-rake retire old-stone safely
```

Outputs (conceptual):

* Step 1: cordon ✅
* Step 2: lift offerings:

  * cache: cutover → stone-03 (expected blip: none)
  * app1: redeploy → stone-02 (expected blip: ~1s)
  * mongodb: replicate+promote → stone-04 (expected blip: none if healthy)
* Step 3: verify: health + resolve excludes old-stone
* Step 4: wipe: identity + volumes
  Proceed? (Y/n)

Flags:

* `--plan` (never execute)
* `--yes` (no prompt)

---

## Stone States

A tiny state machine that keeps everything understandable:

* **active**: normal participation
* **cordoned**: no new placements
* **lifting**: migration in progress
* **quarantined**: removed from participation, no wipe (for inspection)
* **retired**: permanently removed (typically after wipe)

Recommended invariants:

* `cordoned` stones can still run offerings.
* `quarantined` stones do not announce offerings.
* `retired` implies "inert"; no announcements and identity destroyed.

---

## Wipe Semantics (Retire Safely)

"Wipe" should mean **the stone can no longer function as itself**, and data is removed or made unreadable.

Minimum required:

* Destroy Zen Garden identity materials (keys/keystones/bindings)
* Stop all offerings
* Remove offering data volumes / configs
* Clear cached garden metadata

Preferred fast + credible path:

* Use **crypto-erase** if offerings live on encrypted volumes (drop keys = instant wipe)
  Fallback:
* best-effort disk wipe (configurable, may be slow and SSD-dependent)

Provide escape hatches:

* `retire old-stone --quarantine` (no wipe)
* `garden-rake wipe old-stone` (explicit wipe later)

---

## Offering Lifecycle Adapter (Core Abstraction)

Each offering type provides an adapter that describes and performs safe operations.

### Adapter responsibilities

* Declare capability metadata:

  * `statefulness`: ephemeral | stateful
  * `migration`: redeploy | replicate | backup_restore | none
  * `supports`: move, replace, lift, heal
  * prerequisites (e.g., "needs replica set mode enabled")
* Produce a **plan**:

  * steps, ordering, verification checks, expected downtime
* Execute plan steps with progress
* Provide rollback guidance where applicable

### Why this matters

* Cache replacement can be instantaneous.
* Mongo replacement should use replica set mechanics, not file copies.
* Storage might require sync/replication or refuse live migration.
* The *same stone-level command* works because offerings know what to do.

---

## Rule-Based Election for `scale`

Scaling should feel smart but remain simple.

### Candidate selection

**Hard filters:**

* stone is `active` (not cordoned/quarantined/retired)
* has required capabilities (compute runtime, arch, etc.)
* meets minimum resources (RAM/disk)
* is reachable within same security domain (pond/garden)

**Soft scoring:**

* prefer wired over Wi-Fi
* prefer more headroom (RAM/CPU)
* prefer lower load
* prefer spreading across stones (avoid collocation)
* optional affinity/anti-affinity hints

### Observability

Always print election reasoning:

* candidates, scores, filtered reasons
* which stone was chosen
* what changed (new instance + routing updates)

---

## Safety Rules (Refuse vs Force)

Operations must be honest:

Refuse (unless explicitly forced):

* No eligible destination stones
* Stateful offering without a safe migration path
* Storage offering not synced / no adapter
* Attempt to "replace" from a non-stone host without specifying `with`

Provide explicit escape hatches:

* `--force` (with loud warnings)
* `--quarantine` to stop participation without wiping

---

## Minimal Implementation Plan (Phased)

### Phase 1: Foundations

* Stone identity ("this stone"), stone states
* Enumerate offerings per stone
* `cordon`, `lift` (stateless only), `replace` (stateless only), `resolve`
* `--plan` output + `--yes`

### Phase 2: Safe retirement

* `retire` default (quarantine/retire state)
* `retire safely` macro
* wipe semantics (identity + volumes; crypto-erase if supported)

### Phase 3: Offering adapters for stateful services

* Mongo adapter: replicate → sync → promote/stepdown → remove old
* Redis adapter: cutover + optional warming
* Storage adapter: sync/replicate or refuse without configured mechanism

### Phase 4: Rule-based scaling

* `scale offering to N` with election filters + scoring
* gateway/lantern pool updates for load balancing scenarios

---

## Expected Outcomes

* A teacher/demo operator can stand at a new box and run:

  * `garden-rake replace old-stone`
* Or end-of-life a box safely with:

  * `garden-rake retire old-stone safely`
* The system migrates *everything* hosted on the machine using correct per-offering procedures.
* Apps keep using intents; humans keep thinking in machines.

---

## Open Questions (to resolve later, but not blockers)

* How to represent offering "instance identity" consistently (names, labels).
* How much of `replace` should allow multi-target destinations vs strict one-to-one "new replaces old."
* Default wipe level (crypto-erase vs best-effort) and how it depends on storage configuration.
* How Lantern participates (advisory/directory-only vs scheduling authority).

---

# Assessment: Multi-Specialist Review

## Overall Verdict: 8.5/10 - Strong Direction with Implementation Cautions

**Unanimous strong approve with refinements needed before implementation.**

---

## Specialist 1: System Architecture (8/10)

### Strengths

**Mental model alignment is exceptional:**
- "Apps talk to offerings, humans tend stones" perfectly captures Zen Garden's philosophy
- Stone-centric operations match physical reality (this is the laptop that's dying)
- Clear separation: discovery protocol (ZGP-001/002) vs lifecycle operations (new territory)

**Scope is appropriately constrained:**
- Not trying to be Kubernetes (good - maintains simplicity ethos)
- Adapters opt-in to migration capabilities (stateless → stateful progression)
- Rule-based election, not AI scheduler (predictable, debuggable)

**Plan-first UX builds trust:**
- Show impact before execution (critical for educational use case)
- `--plan` without execution (learning/verification)
- `--yes` for automation (scripts/CI)

### Concerns

**1. Protocol surface expansion (moderate concern)**

Current protocol is discovery-only (mDNS + Lantern + Pond). This adds:
- Stone state management (who tracks `cordoned`, `quarantined`, `retired`?)
- Offering lifecycle coordination (orchestration layer)
- Inter-stone migration coordination (distributed state)

**Question:** Does Lantern become stateful coordinator? Or peer-to-peer gossip? Or SQLite on each stone?

**Recommendation:** Define state storage early (Phase 0). Options:
- **Lantern as coordinator** (simple, single source of truth, but SPOF)
- **Distributed state on stones** (complex, but resilient)
- **Hybrid** (Lantern caches, stones persist locally)

**2. Offering adapter contract needs formalization (high priority)**

Proposal mentions adapters but doesn't specify:
- Interface definition (Rust trait? Plugin boundary?)
- Versioning (what if MongoDB adapter changes?)
- Registration (how does garden-rake discover adapters?)
- Packaging (separate crates? Built-in?)

**Recommendation:** Write ADR early defining:
```rust
trait OfferingLifecycleAdapter {
    fn capabilities(&self) -> AdapterCapabilities;
    fn plan_migration(&self, from: Stone, to: Stone) -> MigrationPlan;
    fn execute_step(&self, step: MigrationStep) -> Result<StepOutcome>;
    fn verify(&self, migration: &Migration) -> Result<HealthStatus>;
    fn rollback(&self, migration: &Migration) -> Result<()>;
}
```

**3. Multi-tenancy of offerings unclear**

Proposal assumes one MongoDB per stone. Reality:
- One stone might host `mongodb/db1`, `mongodb/db2` (multiple databases)
- Or multiple Redis instances (different ports/configs)

**Question:** Is "offering" = service type, or = service instance?

**Example ambiguity:**
```bash
garden-rake move mongodb from stone-01 to stone-02
```

If stone-01 hosts 3 MongoDB databases, which moves? All? First? Requires `--all` or instance selector?

**Recommendation:** Clarify offering identity model:
- **Offering type**: `mongodb` (service category)
- **Offering instance**: `mongodb/mydb` or `mongodb:27017` (specific instance)
- Commands operate on instances by default, accept `--all` flag for type-level

### Architecture Score: 8/10

Strong conceptual foundation. Needs protocol state management definition and adapter contract formalization before Phase 1 implementation.

---

## Specialist 2: Developer Experience (9/10)

### Strengths

**CLI semantics are brilliant:**
- `retire old-stone safely` reads like English, unambiguous intent
- `garden-rake replace old-stone` (implicit: "this stone replaces old-stone") is magic for classroom demos
- `cordon` / `lift` / `retire` vocabulary borrowed from Kubernetes (familiar to ops folks)

**Plan-first UX is a DX win:**
```
Step 1: cordon ✅
Step 2: lift offerings:
  • cache: cutover → stone-03 (expected blip: none)
  • mongodb: replicate+promote → stone-04 (expected blip: none if healthy)
Step 3: verify: health + resolve excludes old-stone
Step 4: wipe: identity + volumes
Proceed? (Y/n)
```

This is **teaching infrastructure by showing causality**. Students see:
1. What will happen (plan)
2. Why it's safe (verification)
3. Expected impact (downtime prediction)

**Safety defaults are correct:**
- `retire` without `safely` = quarantine (no data loss)
- `retire safely` requires explicit acknowledgment (or `--yes`)
- `--force` exists but discouraged (loud warnings)

### Concerns

**1. Implicit `replace old-stone` requires context awareness (minor concern)**

Proposal: implicit form only works when run **on a stone**. 

**Problem:** How does garden-rake know "this is a stone"?
- Check for stone agent running? (brittle)
- Check hostname matches stone registry? (assumes naming convention)
- Check for stone identity file? (works, but must be documented)

**Scenario:**
```bash
# SSH'd into stone-02
garden-rake replace stone-01
# Does this mean:
# A) "stone-02 replaces stone-01" (implicit)
# B) Error: "must specify 'with new-stone'" (explicit required)
```

**Recommendation:** Require `--implicit` flag or configuration:
```bash
garden-rake replace stone-01 --with-current-stone
# Or environment variable
CURRENT_STONE=stone-02 garden-rake replace stone-01
```

**2. Error messages must be exceptional (high priority)**

Lifecycle operations have complex failure modes:
- MongoDB replica set not configured (can't migrate safely)
- No eligible destination (all stones cordoned)
- Mid-migration network partition (stone unreachable)
- Verification failed (old stone still resolving)

**Recommendation:** Each failure mode needs:
- Clear error message (what failed, why)
- Suggested remediation (how to fix)
- Escape hatch (how to proceed manually if needed)

**Example:**
```
Error: Cannot migrate MongoDB from stone-01
Reason: Replica set not enabled (standalone mode)

Solutions:
  1. Configure replica set: garden-rake enable-replication stone-01
  2. Use backup/restore: garden-rake move mongodb --via backup
  3. Force redeploy (DATA LOSS): garden-rake move mongodb --force

Docs: https://zen-garden.dev/docs/mongodb-migration
```

**3. Observability during migration (high priority)**

Long-running operations (MongoDB replication, storage sync) need progress feedback:
- Percent complete
- Time remaining estimate
- Current step (5 of 12)
- Ability to tail logs (`--follow`)

**Recommendation:** Use progress library (indicatif in Rust) and structured output:
```bash
garden-rake retire stone-01 safely --follow

[1/5] Cordoning stone-01...                        ✓ (0.2s)
[2/5] Lifting offerings...
  ├─ cache → stone-03                              ✓ (0.1s)
  ├─ app1 → stone-02                               ✓ (3.2s)
  └─ mongodb → stone-04                            [####------] 40% (2.1GB/5.2GB) ~30s
```

### DX Score: 9/10

Exceptional CLI semantics. Minor refinements needed for implicit context and error messaging.

---

## Specialist 3: Operations/SRE (7/10)

### Strengths

**Safety mechanisms are solid foundation:**
- Quarantine before wipe (protect against accidents)
- Verification step (confirm old stone no longer resolving)
- Plan-first (operator reviews before execution)

**State machine is minimal and clear:**
- 5 states: active → cordoned → lifting → quarantined → retired
- Clear invariants (cordoned stones still run offerings, quarantined don't announce)

### Concerns

**1. Failure modes are underspecified (critical concern)**

What happens when:

**Mid-migration network partition:**
```bash
garden-rake lift stone-01 --to stone-02
# Step 1: MongoDB replication started
# Step 2: Network partition (stone-01 unreachable)
# Step 3: ???
```

**Options:**
- A) Abort, rollback to original state (safest, but complex)
- B) Continue on best-effort (hope stone-01 comes back)
- C) Operator choice: `--on-failure [abort|continue|pause]`

**Recommendation:** Default to `pause` with operator prompt:
```
Error: Lost connection to stone-01 during mongodb migration
Current state: Replication 60% complete (3.2GB/5.2GB)

Options:
  1. Wait for stone-01 to reconnect (safe, may take time)
  2. Abort migration, rollback (safe, discards progress)
  3. Continue without stone-01 (RISK: partial state)

Choice (1-3):
```

**2. Concurrent operations (moderate concern)**

What if:
```bash
# Operator A
garden-rake lift stone-01 --to stone-02

# Operator B (simultaneously)
garden-rake move mongodb from stone-01 to stone-03
```

**Conflict:** Two operators moving same offering to different destinations.

**Recommendation:** Distributed lock or optimistic concurrency:
- Each operation acquires lock on stone/offering
- Lock timeout (auto-release after 30 minutes)
- `--break-lock` escape hatch (with warning)

**3. Verification is vague (high priority)**

Proposal says "verify health + resolution no longer returns old-stone". 

**Questions:**
- How long to wait before considering verification failed? (30s? 5 minutes?)
- What if app cached old resolution? (5-minute TTL in spec)
- What if DNS still has old record? (mDNS vs Lantern discrepancy)

**Recommendation:** Multi-phase verification:
```
Verification (60s timeout):
  [1/3] Stone announces stopped               ✓ (0.5s)
  [2/3] Lantern registry updated              ✓ (1.2s)
  [3/3] mDNS query excludes stone-01          ✓ (0.8s)
  [4/3] Optional: App health checks passing   ⚠ (1 of 3 apps responding)

Status: Verification passed (1 app still warming up, expected)
```

**4. Crypto-erase is overspecified for Phase 1 (minor concern)**

Proposal suggests crypto-erase (drop encryption keys) as preferred wipe method.

**Reality check:**
- Most Phase 1 users won't have encrypted volumes (LUKS setup complexity)
- Crypto-erase requires key management infrastructure (where are keys stored?)
- Best-effort `rm -rf` is sufficient for home labs

**Recommendation:** Phase 1 = simple wipe:
```bash
# Phase 1 wipe
1. Stop all offerings
2. rm -rf /var/zen-garden/offerings/*
3. rm -rf /etc/zen-garden/identity
4. Clear mDNS cache
```

Crypto-erase is Phase 3 (after Pond maturity + key management).

**5. Rollback is mentioned but not specified (high priority)**

Adapters should provide "rollback guidance" but proposal doesn't define:
- Is rollback automatic or manual?
- Is rollback always possible? (MongoDB replication can't be "undone" after promotion)
- What's the rollback window? (5 minutes? 1 hour?)

**Recommendation:** Clarify rollback scope:
- **Transactional operations** (stateless apps): automatic rollback
- **Progressive operations** (DB replication): manual rollback with guidance
- **Destructive operations** (wipe): no rollback, require confirmation

### Ops Score: 7/10

Solid safety foundation. Critical gaps in failure handling, concurrency, and verification specs must be addressed before Phase 1.

---

## Specialist 4: Implementation Feasibility (7.5/10)

### Complexity Assessment

**Phase 1 (Foundations): ~2,000-3,000 LOC**
- Stone state machine: 300 LOC
- Offering enumeration: 200 LOC
- `cordon` / `uncordon`: 100 LOC
- `lift` (stateless only): 500 LOC
- `replace` (stateless only): 400 LOC
- `resolve`: 200 LOC
- Plan generation + rendering: 400 LOC
- Tests: 800 LOC

**Phase 2 (Safe Retirement): +1,500 LOC**
- `retire` (quarantine): 200 LOC
- `retire safely` macro: 300 LOC
- Wipe semantics: 400 LOC
- Verification engine: 400 LOC
- Tests: 200 LOC

**Phase 3 (Stateful Adapters): +3,000-5,000 LOC**
- Adapter trait + plugin system: 800 LOC
- MongoDB adapter (replica set mechanics): 1,200 LOC
- Redis adapter (cutover + warming): 600 LOC
- Storage adapter (sync/refuse): 800 LOC
- Tests: 600-1,400 LOC

**Phase 4 (Scaling): +2,000 LOC**
- Election engine: 800 LOC
- Rule scoring: 400 LOC
- Gateway integration: 600 LOC
- Tests: 200 LOC

**Total: 8,500-11,500 LOC** (garden-rake + adapters)

### Hard Parts

**1. MongoDB replica set automation (hardest)**

MongoDB migration requires:
- Detect current topology (standalone vs replica set)
- If standalone: convert to single-node replica set (safe but requires restart)
- Add new stone as secondary (wait for sync)
- Promote secondary to primary (stepdown command)
- Remove old primary (graceful)

**Failure modes:**
- Replication lag too high (stone-01 dies before sync completes)
- Network partition during promotion (split-brain risk)
- App doesn't handle replica set connection strings (requires `replicaSet=` parameter)

**Recommendation:** Phase 3 should start with MongoDB adapter **design document** including:
- Prerequisites (replica set must be pre-configured vs auto-configure)
- Failure scenarios + rollback procedures
- Integration tests (testcontainers with replica set)

**2. Distributed state management (second hardest)**

Stone states (`cordoned`, `lifting`, etc.) must be:
- Consistent across garden (all stones see same state)
- Survive Lantern restart (persistent)
- Handle split-brain (what if two stones both think they're primary?)

**Options:**
- **Lantern as coordinator** (simple, SPOF)
- **Raft consensus** (complex, but proven - etcd/Consul)
- **CRDTs** (eventual consistency, complex)

**Recommendation:** Phase 1 = Lantern-coordinated (SQLite backend). Phase 3+ = distributed (if needed).

**3. Offering instance identity (architecture decision)**

Current protocol has no notion of "this specific MongoDB instance". Discovery returns "a MongoDB Stone" but not "MongoDB instance #2 on stone-01:27018".

**Problem:** If stone-01 hosts multiple MongoDB instances (different ports), how does lifecycle track them?

**Recommendation:** Extend TXT records:
```
offering=mongodb, port=27017, instance=default
offering=mongodb, port=27018, instance=secondary
```

This requires protocol change (ZGP-001 amendment or ZGP-006 for multi-instance).

**4. Progress tracking for long operations (medium difficulty)**

MongoDB replication might take 10+ minutes (10GB database, 100MB/s network). Need:
- Non-blocking execution (tokio async)
- Progress updates (query replication lag)
- Cancellation support (operator hits Ctrl-C)

**Recommendation:** Use async Rust + tokio channels for progress:
```rust
async fn replicate_mongodb(from: Stone, to: Stone) -> impl Stream<Item = Progress> {
    // Yields progress updates
    loop {
        let lag = query_replication_lag(&to).await?;
        yield Progress::Syncing { bytes: lag };
        tokio::time::sleep(Duration::from_secs(5)).await;
    }
}
```

### Implementation Score: 7.5/10

Feasible but complex. MongoDB adapter is the critical path. Distributed state adds risk. Need 4-6 months engineering time (1-2 devs).

---

## Specialist 5: Educational Use Case (10/10)

### Assessment

**This proposal is a pedagogical goldmine.**

### Strengths for Teaching

**1. Physical operations map to commands:**

Classroom demo:
```
Teacher: "This laptop (stone-01) is overheating. We need to replace it."
[Holds up failing laptop, holds up new laptop]

Teacher: "Stand at the new laptop and run:"
garden-rake replace stone-01

[Students watch terminal output showing migration plan]
[Students see old laptop's services move to new laptop]
[Students see apps reconnect automatically]

Teacher: "Infrastructure is physical. We just swapped hardware without touching code."
```

**2. Failure becomes visible:**

```bash
garden-rake lift stone-01 --to stone-02

# Output shows:
# ✓ cache moved (0.1s)
# ✗ mongodb failed: replica set not configured
# 
# This teaches: "Caches are stateless (easy). Databases need special handling (hard)."
```

Students learn operational complexity through **real failure feedback**, not abstract diagrams.

**3. "Plan-first" is a teaching tool:**

```bash
garden-rake retire stone-01 safely --plan
```

Shows students:
- What operations would happen (sequence)
- Why each step exists (verification, safety)
- Expected impact (downtime, data movement)

**Then run without `--plan` and watch it execute.** Theory → practice in one command.

**4. Safety mechanisms teach production mindset:**

- `retire` vs `retire safely` teaches: "deletion is dangerous, require confirmation"
- `--force` teaches: "escape hatches exist but are discouraged"
- Verification step teaches: "trust but verify"

### Curriculum Integration

**Week 6: Stone Lifecycle (new module, 90 minutes)**

**Activity 1: Cordon and observe (15 min)**
```bash
garden-rake cordon stone-01
garden-rake scale webapp to 3
# Students see: new instances deploy to stone-02/03, not cordoned stone-01
```

**Activity 2: Lift stateless service (20 min)**
```bash
garden-rake lift stone-01 --plan
garden-rake lift stone-01 --yes
# Students watch cache/app move
```

**Activity 3: Replace hardware (30 min)**
```bash
# Physically swap laptops
garden-rake replace stone-01
# Watch MongoDB migration (if replica set configured)
```

**Activity 4: Safe retirement (25 min)**
```bash
garden-rake retire stone-01 safely --plan
# Review plan together
garden-rake retire stone-01 safely --yes
# Verify stone no longer announces
```

**Homework:** Calculate e-waste prevented if 100 schools replaced 1 dying laptop/year using this workflow (vs discarding).

### Educational Score: 10/10

Perfect alignment with Zen Garden's teaching mission. Turns operational complexity into learning opportunity.

---

## Specialist 6: Protocol Impact (8/10)

### Protocol Changes Required

**Current protocol (ZGP-001/002/003/004):**
- Service announcement (mDNS TXT records)
- Connection string resolution
- Lantern HTTP API (registration, resolution)
- Pond security (mTLS)

**New requirements from this proposal:**

**1. Stone state in announcements (ZGP-001 amendment)**

TXT records need state field:
```
offering=mongodb, version=7.0, port=27017, state=active
offering=mongodb, version=7.0, port=27017, state=cordoned
offering=mongodb, version=7.0, port=27017, state=lifting
```

Resolvers should prefer `state=active` stones, warn on `state=cordoned`.

**2. Offering instance identity (new: ZGP-006?)**

Current protocol can't distinguish multiple instances of same service on one stone.

**Proposal:** Add `instance` field:
```
offering=mongodb, instance=primary, port=27017
offering=mongodb, instance=secondary, port=27018
```

**3. Lantern API extensions (ZGP-003 amendment)**

New endpoints:
```
PUT  /api/stones/:id/state         # Update stone state
GET  /api/stones/:id/offerings     # List offerings on stone
POST /api/offerings/:id/migrate    # Initiate migration
GET  /api/migrations/:id           # Migration status
```

**4. Migration coordination protocol (new: ZGP-007?)**

Define wire protocol for stone-to-stone migration coordination:
- Migration request (source stone → dest stone)
- Progress updates (dest → coordinator)
- Verification (coordinator → all stones)

**Options:**
- HTTP API (simple, Lantern-mediated)
- gRPC (efficient, peer-to-peer)
- NATS/message bus (decoupled, but new dependency)

### Backward Compatibility

**Breaking changes:**
- TXT record schema changes (old resolvers ignore new fields - graceful degradation)
- Lantern API additions (old clients don't call new endpoints - non-breaking)

**Migration path:**
- Phase 0: Publish ZGP amendments (state field optional)
- Phase 1: Implement with new fields
- Phase 2: Deprecate stateless discovery (announce state required)

### Protocol Score: 8/10

Reasonable extensions to existing protocol. Needs formal specification (ZGP-006/007) before implementation.

---

## Synthesis: Recommendations

### Immediate Actions (Before Phase 1)

**1. Write ADRs (Architecture Decision Records):**
- `LIFE-0001`: Stone state management (Lantern vs distributed)
- `LIFE-0002`: Offering instance identity model
- `LIFE-0003`: Adapter contract specification (Rust trait)
- `LIFE-0004`: Migration coordination protocol

**2. Prototype offering adapter:**

Create minimal MongoDB adapter (even if it just logs, doesn't migrate) to validate:
- Trait design (is it expressive enough?)
- Plugin loading (dynamic libraries? Built-in?)
- Error propagation (how do adapter errors surface to CLI?)

**3. Refine failure handling:**

Document every failure mode for Phase 1 operations:
- Network partition during lift
- Destination stone out of disk
- MongoDB not in replica set mode
- App still caching old resolution

For each: decision tree (abort/continue/pause/operator-choice).

**4. Update protocol specs:**

Draft ZGP amendments:
- ZGP-001-v2: Stone state in TXT records
- ZGP-006: Multi-instance offerings (if needed)
- ZGP-007: Migration coordination wire protocol

**5. Revise wipe semantics:**

Phase 1: Simple wipe (rm -rf, identity destruction)  
Phase 3: Crypto-erase (after key management ready)

Don't block Phase 1 on crypto-erase complexity.

### Phase Adjustments

**Phase 1: Focus on observability + stateless**

Don't attempt stateful migrations in Phase 1. Deliver:
- `cordon` / `uncordon` (works immediately)
- `lift` stateless only (apps, caches)
- `replace` stateless only
- `resolve` with state awareness
- Plan-first UX (builds trust)

**Success criteria:** Teacher can demo `lift` with cache + stateless app, students see instant cutover.

**Phase 2: Retirement without stateful complexity**

Deliver:
- `retire` (quarantine)
- `retire safely` (for stateless stones only)
- Simple wipe

**Skip:** MongoDB migration (Phase 3).

**Success criteria:** End-of-life a cache stone safely with one command.

**Phase 3: Stateful adapters (hardest)**

Start with design doc:
- MongoDB replica set prerequisites
- Failure scenarios + rollback procedures
- Integration test strategy (testcontainers)

Then implement:
- MongoDB adapter (replica set mechanics)
- Redis adapter (cutover)
- Storage adapter (sync or refuse)

**Success criteria:** Replace MongoDB stone with zero downtime (replica set promotion).

**Phase 4: Scaling**

After stateful migration proven, add election + scaling.

### Migration Strategy

**From current state:**
1. Q1 2026: Specifications (ZGP amendments, ADRs)
2. Q2 2026: Phase 1 (stateless lifecycle)
3. Q3 2026: Phase 2 (retirement)
4. Q4 2026: Phase 3 (stateful adapters)
5. Q1 2027: Phase 4 (scaling)

**Total: 12 months** (vs proposal's 6-9 months). Stateful migration is the schedule risk.

---

## Final Verdict: 8.5/10 - Strong Approve with Refinements

### Why 8.5 (not 9 or 10)

**Strengths (+):**
- Perfect mental model (apps → offerings, humans → stones)
- Exceptional CLI semantics (readable, teachable)
- Safety-first approach (plan, verify, escape hatches)
- Phases appropriately scoped (stateless → stateful progression)
- Educational gold mine (turns ops complexity into learning)

**Concerns (−):**
- Underspecified failure modes (network partition, concurrent ops)
- Adapter contract needs formalization (Rust trait, plugin boundary)
- Offering instance identity ambiguous (multiple instances per stone)
- Verification vague (timeout? caching? DNS lag?)
- MongoDB replica set complexity may exceed Phase 3 timeline

### Recommend: Approve with Conditions

**Conditions:**
1. Write ADRs for state management, adapter contract, instance identity (before Phase 1)
2. Prototype MongoDB adapter as design validation (before committing to trait)
3. Document failure handling decision trees (abort/continue/pause per scenario)
4. Revise wipe to simple (Phase 1) + crypto-erase (Phase 3)
5. Extend timeline: 12 months (not 6-9) to account for stateful migration complexity

**Once conditions met:** This becomes the flagship operational feature of Zen Garden. The "retire safely" one-liner is a killer demo.

---

## Additional Notes

### Naming Bikeshed (Minor)

`garden-rake` is excellent tool name (evokes tending a garden). Consider:

**Alternative verbs:**
- `drain` instead of `lift` (more common in k8s/Docker Swarm)
- `evict` instead of `lift` (implies urgency)
- `migrate` instead of `move` (clearer intent)

**Proposal author's choice is fine.** These are nitpicks.

### Lantern's Role (Clarify)

Proposal is ambiguous about Lantern:
- Directory service (current role) or
- Orchestration coordinator (new role) or
- Both?

If Lantern becomes stateful coordinator, update ROADMAP.md (Lantern is currently described as lightweight HTTP directory).

### Testing Strategy (Critical for Phase 3)

Stateful migrations require **integration tests with real services**:
- MongoDB replica set scenarios (testcontainers)
- Network partition simulation (toxiproxy)
- Slow replication (throttled network)

Budget test infrastructure: Docker-in-Docker, network chaos tools.

---

## Community Feedback Opportunities

Before Phase 1 implementation:

1. **GitHub Discussion:** Share proposal, gather use cases (what stones do people actually want to retire?)
2. **Prototype Demo:** Record video of `lift` with stateless apps, show plan-first UX
3. **ADR Review:** Publish state management + adapter contract ADRs, request feedback

Zen Garden is open protocol. Major operational features should have community input before implementation.

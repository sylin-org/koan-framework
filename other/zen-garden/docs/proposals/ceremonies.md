# Zen Garden Ceremony Specification

**Long-running distributed operations with Elder Stone coordination**

**Status:** Proposal  
**Date:** January 2026  
**Authors:** Collaborative design session

---

## Table of Contents

1. [Overview](#overview)
2. [Why Ceremonies Exist](#why-ceremonies-exist)
3. [Philosophy](#philosophy)
4. [Interactive vs Ceremony](#interactive-vs-ceremony)
5. [Elder Stones](#elder-stones)
6. [Ceremony Types](#ceremony-types)
7. [Election Protocol](#election-protocol)
8. [Ceremony Lifecycle](#ceremony-lifecycle)
9. [Migration Ceremonies](#migration-ceremonies)
10. [Self-Healing](#self-healing)
11. [Ceremony Records](#ceremony-records)
12. [Failure Handling](#failure-handling)
13. [API Specification](#api-specification)

---

## Overview

### What is a Ceremony?

A **Ceremony** is a long-running, multi-step operation that coordinates work across multiple Stones in a Zen Garden. Unlike immediate commands that return in milliseconds, ceremonies have permission to take minutes or hours, retry on failure, and report results asynchronously.

**The term "ceremony" is intentional.** In Zen Garden's vocabulary, we don't have "jobs" or "tasks"—we have ceremonies. This reflects the philosophy that infrastructure operations should be intentional, careful, and meaningful rather than rushed background noise.

### Core Characteristics

1. **Multi-Stone Coordination** — Ceremonies involve communication between Stones, not just local operations
2. **Distributed Execution** — Work happens across the garden, coordinated by an elected leader
3. **Fault Tolerant** — Built-in retry logic, candidate failover, and graceful degradation
4. **Asynchronous** — Operator can initiate and walk away; results reported later
5. **Auditable** — Full record of what happened, when, and why

### Examples of Ceremonies

| Ceremony | Purpose | Typical Duration |
|----------|---------|------------------|
| `vacate` | Move all offerings off a Stone (hardware replacement) | 5-30 minutes |
| `move` | Relocate specific offering to another Stone | 1-10 minutes |
| `nourish --all` | Upgrade all offerings across garden | 10-60 minutes |
| `heal` | Garden-wide consistency check and repair | 2-15 minutes |
| `rebalance` | Redistribute offerings for optimal utilization | 5-20 minutes |

### What Ceremonies Are NOT

- **Not interactive commands** — `garden-rake offer mongodb` is immediate, not a ceremony
- **Not scheduled jobs** — Ceremonies are initiated explicitly, not on timers
- **Not atomic transactions** — Partial success is possible and handled gracefully
- **Not centrally orchestrated** — Coordination emerges through election, not a master node

---

## Why Ceremonies Exist

### The Problem: Distributed State Changes

In a garden with multiple Stones, some operations cannot be completed by a single Stone acting alone:

**Scenario: Hardware Replacement**

Your old laptop (stone-01) is dying. It runs MongoDB, Redis, and PostgreSQL. You bought a replacement thin client. You need to:

1. Snapshot MongoDB data on stone-01
2. Find a Stone with enough capacity (stone-02)
3. Transfer the snapshot to stone-02
4. Restore MongoDB on stone-02
5. Verify it's healthy
6. Update the garden so apps discover the new location
7. Repeat for Redis and PostgreSQL
8. Clean up stone-01

This involves:
- Multiple Stones communicating
- Sequential steps with dependencies
- Potential failures at any step
- Need for rollback if things go wrong
- Coordination to prevent conflicts

**A single CLI command can't do this atomically.** You need an operation that spans time and space.

### The Solution: Ceremonies

Ceremonies provide:

1. **Elected Coordinator** — One Stone leads the operation (preferring reliable Elder Stones)
2. **Phased Execution** — Survey → Plan → Execute → Verify → Commit
3. **Retry Logic** — If stone-02 fails, try stone-03 automatically
4. **Progress Tracking** — Know what's happening even when you're not watching
5. **Clear Reporting** — Understand what succeeded, what failed, and why

### When to Use Ceremonies vs Interactive Commands

**Use interactive commands when:**
- Operation affects single Stone
- Result needed immediately
- Human is watching and waiting
- Failure requires immediate human decision

**Use ceremonies when:**
- Operation spans multiple Stones
- Operation involves data migration
- Operation can take minutes/hours
- Retry logic should be automatic
- Human wants to "fire and forget"

---

## Philosophy

### The Name "Ceremony"

We deliberately chose "ceremony" over "job," "task," or "workflow":

| Term | Connotation | Why Not |
|------|-------------|---------|
| Job | Industrial, mechanical | Implies disposable background work |
| Task | Todo item, chore | Lacks gravitas for critical operations |
| Workflow | Enterprise, complex | Suggests heavyweight orchestration |
| **Ceremony** | Intentional, meaningful | Reflects care and purpose |

A ceremony is a **ritual with steps**. It has:
- A beginning and end
- Defined participants
- A purpose that matters
- Respect for the process

When you run `garden-rake vacate stone-01`, you're not spawning a background job. You're initiating a ceremony to honorably retire a piece of hardware that has served you.

### Measure Twice, Cut Once

Ceremonies follow a deliberate, cautious approach:

```
┌─────────────────────────────────────────────────────────────────┐
│                    CEREMONY PHILOSOPHY                          │
├─────────────────────────────────────────────────────────────────┤
│                                                                 │
│   1. SURVEY      "What is the current state of the garden?"    │
│        ↓                                                        │
│   2. PLAN        "What's the best path forward?"               │
│        ↓                                                        │
│   3. PREPARE     "Is everything ready?"                        │
│        ↓                                                        │
│   4. EXECUTE     "Do the work, carefully"                      │
│        ↓                                                        │
│   5. VERIFY      "Did it actually work?"                       │
│        ↓                                                        │
│   6. COMMIT      "Make it official"                            │
│        ↓                                                        │
│   7. REPORT      "What happened?"                              │
│                                                                 │
└─────────────────────────────────────────────────────────────────┘
```

At each step, the ceremony can:
- Proceed to the next step
- Retry the current step
- Fall back to an alternative
- Abort with clear explanation

### Unassisted Operation

Ceremonies are designed for the operator who initiates before leaving for the day:

```bash
# Friday evening
$ garden-rake vacate stone-01

Ceremony initiated: vacate-stone01-20260120-1830
Coordinator elected: stone-02 (Elder, 47d uptime)
Migrating 3 offerings...

Progress will continue in background.
Check status: garden-rake ceremonies vacate-stone01

# Monday morning
$ garden-rake ceremonies

RECENT CEREMONIES
───────────────────────────────────────────────
  vacate-stone01    ✓ completed   3/3 migrated     2d ago
```

The ceremony doesn't need you watching. It will:
- Handle transient failures automatically
- Try alternative Stones if first choice fails
- Record everything for later review
- Surface issues that need human attention

### Graceful Degradation

Ceremonies embrace partial success:

```
VACATE STONE-01                         [partial]
───────────────────────────────────────────────
  mongodb      ✓ migrated → stone-02
  redis        ✓ migrated → stone-03  
  postgresql   ✗ failed (no compatible Stone)
```

This is not a failure state—it's an honest report. Two offerings moved successfully. One couldn't. The ceremony did everything it could and told you exactly what remains.

---

## Interactive vs Ceremony

### Command Classification

| Command | Type | Why |
|---------|------|-----|
| `garden-rake offer mongodb` | Interactive | Single Stone, immediate result |
| `garden-rake observe` | Interactive | Read-only, instant |
| `garden-rake rest mongodb` | Interactive | Single Stone, immediate |
| `garden-rake vacate stone-01` | **Ceremony** | Multi-Stone, minutes duration |
| `garden-rake move mongodb to stone-02` | **Ceremony** | Cross-Stone data migration |
| `garden-rake nourish --all` | **Ceremony** | Garden-wide, sequential |
| `garden-rake heal` | **Ceremony** | Multi-Stone consistency check |

### Feedback Patterns

**Interactive command feedback:**
```bash
$ garden-rake offer mongodb

Offering mongodb to stone-01...
  Pulling image: mongo:7 ✓
  Creating container ✓
  Starting service ✓
  Health check ✓

mongodb is thriving on stone-01
Connection: mongodb://stone-01.local:27017
```
*Immediate, synchronous, human watches completion.*

**Ceremony feedback:**
```bash
$ garden-rake vacate stone-01

Ceremony initiated: vacate-stone01-20260120-1423
───────────────────────────────────────────────
  Type:         vacate
  Source:       stone-01
  Offerings:    mongodb, redis, postgresql
  
Electing coordinator...
  stone-02 elected (Elder, 47d uptime)

Ceremony is now running.
  
  Watch progress:  garden-rake watch ceremony vacate-stone01
  Check status:    garden-rake ceremonies vacate-stone01
```
*Acknowledgment immediate, execution asynchronous, human can leave.*

### Time Budgets

| Type | Expected Duration | Timeout | Human Presence |
|------|-------------------|---------|----------------|
| Interactive | < 30 seconds | 60 seconds | Required |
| Ceremony | 1 minute - 2 hours | Configurable | Optional |

### Error Handling

**Interactive errors surface immediately:**
```bash
$ garden-rake offer mongodb

Error: Incompatible architecture
  mongodb requires x86_64, stone-01 is armv6l
  
Suggestion: Try a different Stone with garden-rake offer mongodb at stone-02
```

**Ceremony errors are collected and reported:**
```bash
$ garden-rake ceremonies vacate-stone01

postgresql migration failed:
  Attempt 1: stone-02 — timeout during pg_dump
  Attempt 2: stone-03 — insufficient disk space
  Attempt 3: stone-04 — incompatible architecture
  
All 3 attempts exhausted. Manual intervention required.

Suggestions:
  • Increase timeout: garden-rake move postgresql --timeout 600
  • Free space on stone-03: garden-rake prune stone-03
  • Add compatible Stone to garden
```

---

## Elder Stones

### Concept

**Elder Stones** are Stones that have demonstrated reliability through sustained uptime. They earn trust through stability, not assignment.

**Why uptime matters:**
- In a distributed, peer-to-peer, fragile-by-design infrastructure, stability is signal
- A Stone running for 47 days without restart has proven itself
- Elder Stones are natural candidates for coordinating ceremonies

### Elder Status Calculation

```rust
struct ElderMetrics {
    uptime_hours: u64,
    manual_trust: u64,            // Operator-granted trust (recognition)
    restart_count_30d: u32,
    ceremony_success_rate: f64,   // ceremonies coordinated successfully
    network_stability: f64,       // % time reachable by peers
}

fn elder_score(metrics: &ElderMetrics) -> u64 {
    // Logarithmic uptime: early hours matter most, diminishing returns
    // 1h → 0, 6h → 180, 24h → 318, 3d → 430, 7d → 512, 30d → 658, 90d → 768
    let uptime_score = ((metrics.uptime_hours as f64 + 1.0).ln() * 100.0) as u64;
    
    // Manual trust: operator recognized this stone as elder
    // Equivalent to granting virtual uptime worth of trust
    let trust_score = metrics.manual_trust;
    
    // Penalty for recent restarts (instability signal)
    let stability_penalty = (metrics.restart_count_30d as u64) * 50;
    
    // Bonus for successful ceremony coordination track record
    let ceremony_bonus = (metrics.ceremony_success_rate * 100.0) as u64;
    
    // Compose score
    let base = uptime_score + trust_score + ceremony_bonus;
    let penalized = base.saturating_sub(stability_penalty);
    
    // Network reliability as final multiplier
    (penalized as f64 * metrics.network_stability) as u64
}
```

### Elder Thresholds

| Score Range | Status | Typical Uptime | Can Coordinate? |
|-------------|--------|----------------|-----------------|
| 0-150 | **Seedling** | < 6 hours | No |
| 150-350 | **Established** | 6h - 3 days | No |
| 350-550 | **Mature** | 3 - 14 days | Yes |
| 550-750 | **Elder** | 14 - 60 days | Yes (preferred) |
| 750+ | **Ancient** | 60+ days | Yes (highly preferred) |

**Note:** Manual recognition can accelerate a Stone to Elder status immediately.

### Manual Elder Recognition

Operators can recognize a Stone as Elder when they trust the hardware, regardless of uptime.

**Use cases:**
- New server with enterprise-grade hardware
- Migrated Stone from another garden (proven reliability)
- UPS-backed Stone with redundant power

**Zen syntax:**

```bash
# Recognize stone as elder
garden-rake recognize stone-03 as elder

# With explicit trust value (default: 500)
garden-rake recognize stone-03 as elder with trust 800

# Remove recognition (let it prove itself through uptime)
garden-rake forget stone-03 as elder
```

**Normative syntax:**

```bash
garden-rake make stone-03 elder
garden-rake make stone-03 elder --trust 800
garden-rake unmake stone-03 elder
```

**Output:**

```
$ garden-rake recognize stone-03 as elder

STONE-03 recognized as Elder
───────────────────────────────────────────────
    Previous score:    127 (Seedling, 4h uptime)
    Trust granted:     500
    New score:         627 (Elder)
    
    stone-03 will now be preferred for coordinating ceremonies.
    
    To remove: garden-rake forget stone-03 as elder
```

**Philosophy:**

Elders aren't appointed—they're *recognized*. The Stone was always capable; you're telling the garden to trust what you see. And "forget" for removal is gentle, not punitive—you're letting the Stone prove itself through uptime again.

### Elder in Garden Overview

```
$ garden-rake observe

GARDEN OVERVIEW                        [thriving]
───────────────────────────────────────────────
STONE-CRIMSON-SUMMIT          Elder (47d)    [thriving]
    mongodb, redis, postgresql

STONE-AZURE-BROOK             Mature (12d)   [thriving]
    rabbitmq, elasticsearch

STONE-RELIABLE-SERVER         Elder ★        [thriving]
    (recognized, 6h actual uptime)
    ollama, minio

STONE-YOUNG-SPROUT            Seedling (6h)  [growing]
    (no offerings yet)
```

The ★ indicates a manually recognized Elder (trust granted by operator).

---

## Ceremony Types

### Vacate Stone

**Purpose:** Move all offerings off a Stone (hardware replacement, retirement)

```bash
garden-rake vacate stone-01
garden-rake vacate stone-01 --to stone-02    # explicit destination
garden-rake vacate stone-01 --dry-run        # plan without executing
```

### Move Offering

**Purpose:** Relocate specific offering to another Stone

```bash
garden-rake move mongodb to stone-02
garden-rake move mongodb                      # system chooses destination
garden-rake move mongodb --from stone-01 --to stone-02
```

### Nourish All

**Purpose:** Upgrade all offerings across garden

```bash
garden-rake nourish --all
garden-rake nourish --all --rolling          # one Stone at a time
```

### Heal Garden

**Purpose:** Consistency check and repair across all Stones

```bash
garden-rake heal
garden-rake heal --dry-run                   # report issues without fixing
```

### Rebalance

**Purpose:** Redistribute offerings for optimal resource utilization

```bash
garden-rake rebalance
garden-rake rebalance --prefer-elders        # concentrate on stable Stones
```

---

## Election Protocol

### Overview

When a ceremony requires coordination, Stones elect a coordinator. The election biases toward Elder Stones while maintaining the existing BLAKE3-based protocol.

### Election Algorithm

```rust
fn calculate_election_delay(
    stone_name: &str,
    ceremony_id: &str,
    elder_score: u64,
) -> Duration {
    // Base delay from BLAKE3 hash (existing protocol)
    let hash = blake3::hash(format!("{}{}", stone_name, ceremony_id).as_bytes());
    let base_delay_ms = (hash.as_bytes()[0] as u64) * 10; // 0-2550ms
    
    // Elder factor: multiplicative reduction (more aggressive)
    // Score 0    → factor 1.0  → full delay (0-2550ms)
    // Score 400  → factor 0.6  → 40% reduction (0-1530ms)  
    // Score 800+ → factor 0.2  → 80% reduction (0-510ms)
    let max_elder_score = 800u64;
    let normalized = (elder_score as f64 / max_elder_score as f64).min(1.0);
    let elder_factor = 1.0 - (normalized * 0.8);
    
    let adjusted_delay_ms = (base_delay_ms as f64 * elder_factor) as u64;
    
    // Minimum 50ms to prevent instant storms
    Duration::from_millis(adjusted_delay_ms.max(50))
}
```

**Election advantage by status:**

| Stone Status | Elder Score | Worst Case Delay | Advantage |
|--------------|-------------|------------------|-----------|
| Seedling | ~50 | 2,422ms | Baseline |
| Established | ~250 | 1,912ms | 25% faster |
| Mature | ~450 | 1,402ms | 45% faster |
| Elder | ~650 | 893ms | 65% faster |
| Ancient / Recognized | 800+ | 510ms | 80% faster |

Elders almost always win elections unless BLAKE3 gives them a particularly bad hash.

### Election Flow

```
1. Ceremony initiated (e.g., vacate stone-01)
   Initiator broadcasts: CEREMONY_ELECTION
   {
     "ceremony_id": "vacate-stone01-20260120-1423",
     "ceremony_type": "vacate",
     "source": "stone-01",
     "initiated_by": "operator"
   }

2. All Stones calculate election delay
   - Include elder_score in calculation
   - Elder Stones get shorter delays
   - Wait for calculated duration

3. First responder claims coordinator role
   Broadcasts: CEREMONY_CLAIM
   {
     "ceremony_id": "vacate-stone01-20260120-1423",
     "coordinator": "stone-02",
     "elder_score": 423
   }

4. Other Stones acknowledge and defer
   - Suppress their own claim
   - Register stone-02 as coordinator for this ceremony

5. Coordinator begins ceremony execution
   - Has authority to direct other Stones
   - Reports progress and results
```

### Conflict Resolution

If two Stones claim simultaneously (network race):

```rust
fn resolve_coordinator_conflict(claim_a: &Claim, claim_b: &Claim) -> &Claim {
    // Higher elder_score wins
    if claim_a.elder_score != claim_b.elder_score {
        return if claim_a.elder_score > claim_b.elder_score { claim_a } else { claim_b };
    }
    
    // Tie-breaker: lexicographic stone name (deterministic)
    if claim_a.coordinator < claim_b.coordinator { claim_a } else { claim_b }
}
```

---

## Ceremony Lifecycle

### State Machine

```
┌─────────────┐
│  INITIATED  │ ← Operator or automation triggers ceremony
└──────┬──────┘
       │
       ▼
┌─────────────┐
│  ELECTING   │ ← Stones compete to become coordinator
└──────┬──────┘
       │
       ▼
┌─────────────┐
│  PLANNING   │ ← Coordinator surveys garden, builds plan
└──────┬──────┘
       │
       ▼
┌─────────────┐
│  EXECUTING  │ ← Coordinator directs Stones through steps
└──────┬──────┘
       │
       ├─────────────┐
       ▼             ▼
┌─────────────┐ ┌─────────────┐
│  COMPLETED  │ │   FAILED    │
└─────────────┘ └─────────────┘
       │             │
       ▼             ▼
┌─────────────┐ ┌─────────────┐
│  ARCHIVED   │ │  REQUIRES   │
│             │ │ INTERVENTION│
└─────────────┘ └─────────────┘
```

### Phase Details

**INITIATED**
- Ceremony request received
- Validated (is this a sensible operation?)
- Assigned unique ceremony_id

**ELECTING**
- Broadcast election request
- Stones calculate delays (biased by elder_score)
- Coordinator claims role
- Timeout: 5 seconds (if no coordinator, retry or fail)

**PLANNING**
- Coordinator queries all relevant Stones
- Builds execution plan
- Identifies candidates for each offering
- Estimates time and resource requirements
- Plan can be displayed with `--dry-run`

**EXECUTING**
- Coordinator directs operations step by step
- Each offering processed according to its migration strategy
- Progress tracked and logged
- Retries on transient failures

**COMPLETED**
- All offerings successfully processed
- Results broadcast to garden
- Ceremony record finalized

**FAILED**
- Maximum retries exhausted
- Unrecoverable error encountered
- Partial results recorded
- May require operator intervention

---

## Migration Ceremonies

### Offering Migration Strategy

Each offering *may* declare how it should be migrated in a fourth manifest file. If absent, the system uses a sensible default (volume snapshot).

**File:** `{offering}.migration.yaml`

```yaml
# mongodb.migration.yaml
version: "1"

strategy: stateful-snapshot

snapshot:
  method: mongodump
  command: 
    - "mongodump"
    - "--archive=/backup/snapshot.archive"
    - "--gzip"
  volume: mongo-data
  estimated_size_factor: 1.2
  timeout_seconds: 300

restore:
  command:
    - "mongorestore"
    - "--archive=/backup/snapshot.archive"
    - "--gzip"
    - "--drop"
  post_restore_healthcheck: true
  timeout_seconds: 600

transfer:
  priority: volume        # prefer volume copy (faster)
  fallback: archive       # fall back to archive transfer
  
verification:
  queries:
    - "db.adminCommand('ping')"
    - "db.stats().collections > 0"
  timeout_seconds: 30

cleanup:
  source_after_success: true    # clean source after verified migration
  target_after_failure: true    # clean target on failed migration
```

### Default Migration (No Manifest)

When `{offering}.migration.yaml` doesn't exist, the system applies a sensible default:

```rust
fn get_migration_strategy(offering: &str) -> MigrationStrategy {
    let path = format!("/etc/zen-garden/offerings/{}.migration.yaml", offering);
    
    match read_yaml(&path) {
        Ok(manifest) => MigrationStrategy::Custom(manifest),
        Err(_) => MigrationStrategy::Default {
            // Default: Docker volume snapshot → transfer → restore
            snapshot: VolumeSnapshot,
            transfer: DirectVolumeCopy,
            verify: HealthcheckOnly,
            cleanup: StandardCleanup,
        }
    }
}
```

**Default behavior:**
1. Stop the container gracefully
2. Snapshot the Docker volume(s)
3. Transfer volume data to target Stone
4. Start container on target with same configuration
5. Verify via standard healthcheck (from offering's `snippet.yaml`)
6. Clean up source after verification passes

This works well for most stateful services. Custom manifests exist for offerings that need special handling (database-specific dump tools, custom verification queries, etc.).

### Strategy Types

| Strategy | Description | Examples |
|----------|-------------|----------|
| `stateless` | No data to migrate, just start elsewhere | nginx, static servers |
| `stateful-snapshot` | Use offering-specific dump/restore | mongodb, postgresql, redis |
| `stateful-volume` | Copy Docker volume directly | generic stateful apps |
| `manual` | Requires operator guidance | custom applications |

### Migration Flow (Stateful Snapshot)

```
Coordinator                    Source Stone                 Target Stone
    │                              │                             │
    │  1. PREPARE_MIGRATION        │                             │
    │─────────────────────────────>│                             │
    │                              │                             │
    │  2. RESERVATION_REQUEST      │                             │
    │─────────────────────────────────────────────────────────────>
    │                              │                             │
    │  3. RESERVATION_CONFIRMED    │                             │
    │<─────────────────────────────────────────────────────────────
    │     (space reserved, timeout set)                          │
    │                              │                             │
    │  4. BEGIN_SNAPSHOT           │                             │
    │─────────────────────────────>│                             │
    │                              │                             │
    │  5. SNAPSHOT_COMPLETE        │                             │
    │<─────────────────────────────│                             │
    │     (archive ready)          │                             │
    │                              │                             │
    │  6. TRANSFER_PAYLOAD         │                             │
    │                              │────────────────────────────>│
    │                              │   (direct stone-to-stone)   │
    │                              │                             │
    │  7. TRANSFER_COMPLETE        │                             │
    │<─────────────────────────────────────────────────────────────
    │                              │                             │
    │  8. RESTORE_AND_VERIFY       │                             │
    │─────────────────────────────────────────────────────────────>
    │                              │                             │
    │  9. VERIFICATION_RESULT      │                             │
    │<─────────────────────────────────────────────────────────────
    │     (healthy / failed)       │                             │
    │                              │                             │
    │ 10. COMMIT / ROLLBACK        │                             │
    │─────────────────────────────>│─────────────────────────────>
    │                              │                             │
```

### Candidate Selection

When migrating an offering, the coordinator ranks candidate Stones:

```rust
fn rank_candidates(
    offering: &Offering,
    candidates: &[Stone],
    source: &Stone,
) -> Vec<RankedCandidate> {
    candidates.iter()
        .filter(|c| c.name != source.name)                    // Not the source
        .filter(|c| passes_compatibility(offering, c))         // Passes compat rules
        .map(|c| RankedCandidate {
            stone: c.clone(),
            score: calculate_suitability(offering, c),
        })
        .sorted_by(|a, b| b.score.cmp(&a.score))              // Highest score first
        .take(3)                                               // Top 3 candidates
        .collect()
}

fn calculate_suitability(offering: &Offering, stone: &Stone) -> u64 {
    let mut score = 0u64;
    
    // Available RAM headroom (after offering placed)
    let ram_after = stone.available_ram_mb - offering.estimated_ram_mb;
    score += (ram_after / 100) as u64 * 10;
    
    // Available disk space
    let disk_after = stone.available_disk_gb - offering.estimated_disk_gb;
    score += (disk_after as u64) * 5;
    
    // Prefer less loaded Stones (fewer offerings)
    score += (10u64.saturating_sub(stone.offering_count as u64)) * 20;
    
    // Elder bonus (prefer stable Stones)
    score += stone.elder_score / 10;
    
    // Anti-affinity: penalize if same offering type already present
    if stone.has_offering_type(&offering.category) {
        score = score.saturating_sub(50);
    }
    
    score
}
```

---

## Self-Healing

### Moss Autonomy

Each Stone is responsible for its own hygiene. Moss doesn't wait for instructions to clean up.

### Healing Triggers

| Trigger | Description |
|---------|-------------|
| **Explicit failure** | Migration fails mid-operation |
| **Timeout** | Coordinator disappeared, reservation expired |
| **Restart recovery** | Moss finds orphaned artifacts after crash |
| **Periodic sweep** | Configurable interval (default: hourly) |
| **Pressure** | New offering incoming, need to free space |

### Healing Actions

```rust
enum HealingAction {
    // Orphaned migration artifacts
    CleanupOrphanedSnapshots,
    CleanupPartialVolumes,
    CleanupStagingDirectories,
    
    // Container state
    RestartCrashedOffering,
    RemoveOrphanedContainer,
    
    // Resource recovery
    PruneDanglingImages,
    PruneUnusedVolumes,
    
    // Registry consistency
    AdoptUnknownGardenContainers,
    RemoveStaleRegistryEntries,
}
```

### Pressure-Triggered Cleanup

```rust
fn prepare_for_incoming(offering: &Offering) -> Result<(), PrepareError> {
    let required_space = offering.estimated_disk_gb * 1.5; // Buffer
    let available = get_available_disk_gb();
    
    if available >= required_space {
        return Ok(()); // Plenty of space
    }
    
    // Need to clean house
    let actions = vec![
        HealingAction::CleanupOrphanedSnapshots,
        HealingAction::PruneDanglingImages,
        HealingAction::PruneUnusedVolumes,
    ];
    
    for action in actions {
        execute_healing(action)?;
        
        if get_available_disk_gb() >= required_space {
            return Ok(()); // Freed enough space
        }
    }
    
    Err(PrepareError::InsufficientSpaceAfterCleanup {
        required: required_space,
        available: get_available_disk_gb(),
    })
}
```

### Reconciliation

Moss periodically reconciles actual state with expected state:

```rust
fn reconcile() -> ReconciliationReport {
    let registry = load_registry();
    let containers = docker_list_containers("garden-offering-*");
    
    let mut report = ReconciliationReport::new();
    
    // Containers in registry but not running
    for offering in &registry.offerings {
        if offering.state == State::Running {
            if !containers.contains(&offering.container_name) {
                report.add(Discrepancy::ExpectedRunningButMissing {
                    offering: offering.name.clone(),
                    action: Action::Restart,
                });
            }
        }
    }
    
    // Containers running but not in registry
    for container in &containers {
        if !registry.knows_about(container) {
            report.add(Discrepancy::UnknownContainer {
                container: container.clone(),
                action: Action::AdoptOrFlag,
            });
        }
    }
    
    // State mismatches
    for offering in &registry.offerings {
        if let Some(container) = containers.get(&offering.container_name) {
            if offering.state == State::Running && container.state == "exited" {
                report.add(Discrepancy::StateMismatch {
                    offering: offering.name.clone(),
                    expected: State::Running,
                    actual: State::Stopped,
                    action: Action::InvestigateAndMaybeRestart,
                });
            }
        }
    }
    
    report
}
```

### Conflict Visibility

When Moss detects external interference (e.g., operator manually stopped a container):

```
$ garden-rake observe

STONE-CRIMSON-SUMMIT               [attention needed]
───────────────────────────────────────────────
    mongodb          [conflict]    expected: running, actual: stopped
                                   (manually stopped outside Moss?)
    
    redis            [thriving]    1.83%   271.52 MB
    postgresql       [thriving]    0.05%    55.12 MB

Resolve conflicts:
    garden-rake wake mongodb       # Moss takes over, starts it
    garden-rake rest mongodb       # Legitimize stopped state
    garden-rake release mongodb    # Remove it entirely
```

---

## Ceremony Records

### Record Structure

```yaml
ceremony:
  id: vacate-stone01-20260120-1423
  type: vacate
  source: stone-01
  
  initiated:
    by: operator
    at: 2026-01-20T14:23:00Z
    command: "garden-rake vacate stone-01"
    
  coordinator:
    stone: stone-02
    elder_score: 423
    elected_at: 2026-01-20T14:23:02Z
    
  plan:
    offerings:
      - mongodb → stone-02 (candidate 1), stone-03 (candidate 2)
      - redis → stone-03 (candidate 1), stone-02 (candidate 2)
      - postgresql → stone-02 (candidate 1)
    estimated_duration: 8 minutes
    
  execution:
    started_at: 2026-01-20T14:23:05Z
    
    offerings:
      - name: mongodb
        status: migrated
        destination: stone-02
        duration_seconds: 263
        attempts:
          - target: stone-02
            result: success
            started_at: 2026-01-20T14:23:05Z
            completed_at: 2026-01-20T14:27:28Z
            
      - name: redis
        status: migrated
        destination: stone-03
        duration_seconds: 12
        attempts:
          - target: stone-02
            result: failed
            reason: "insufficient memory headroom"
          - target: stone-03
            result: success
            
      - name: postgresql
        status: failed
        attempts:
          - target: stone-02
            result: failed
            reason: "pg_dump timeout after 300s"
          - target: stone-03
            result: failed
            reason: "disk space exhausted during transfer"
          - target: stone-04
            result: failed
            reason: "incompatible architecture (requires x86_64)"
        requires_intervention: true
        
  completed_at: 2026-01-20T14:31:45Z
  
  summary:
    status: partial
    migrated: 2
    failed: 1
    message: "2/3 offerings migrated. postgresql requires manual intervention."
```

### Record Retention

| Age | Storage |
|-----|---------|
| < 24 hours | Full detail in memory |
| 1-7 days | Full detail on disk |
| 7-30 days | Summary only |
| > 30 days | Pruned (configurable) |

### Querying Ceremonies

```bash
# List recent ceremonies
$ garden-rake ceremonies
RECENT CEREMONIES
───────────────────────────────────────────────
  vacate-stone01    ⚠ partial     2/3 migrated     8h ago
  nourish-all       ✓ completed   5/5 upgraded     2d ago
  move-redis        ✓ completed   1/1 migrated     5d ago

# View specific ceremony
$ garden-rake ceremonies vacate-stone01

# Normative alias
$ garden-rake jobs
$ garden-rake jobs vacate-stone01
```

---

## Failure Handling

### Retry Strategy

```rust
struct RetryPolicy {
    max_attempts: u32,           // Default: 3
    backoff_base_ms: u64,        // Default: 1000
    backoff_multiplier: f64,     // Default: 2.0
    max_backoff_ms: u64,         // Default: 30000
}

fn calculate_backoff(attempt: u32, policy: &RetryPolicy) -> Duration {
    let backoff = policy.backoff_base_ms as f64 
        * policy.backoff_multiplier.powi(attempt as i32 - 1);
    let capped = backoff.min(policy.max_backoff_ms as f64);
    Duration::from_millis(capped as u64)
}
```

### Failure Categories

| Category | Retry? | Action |
|----------|--------|--------|
| **Transient** | Yes | Network timeout, temporary resource pressure |
| **Candidate-specific** | Yes (next candidate) | Incompatible, insufficient space |
| **Permanent** | No | No compatible candidates, data corruption |
| **Operator error** | No | Invalid ceremony request |

### Graceful Degradation

When a ceremony partially fails:

1. Successfully migrated offerings stay migrated
2. Failed offerings remain on source Stone
3. Source Stone is NOT automatically wiped
4. Clear report of what succeeded and what failed
5. Actionable suggestions for resolution

### Intervention Required

```
$ garden-rake ceremonies vacate-stone01

VACATE STONE-01                         [partial]
───────────────────────────────────────────────
  Started:    2026-01-20 14:23
  Completed:  2026-01-20 14:31 (8m 45s)
  Coordinator: stone-02 (Elder, 47d uptime)
  
  mongodb      ✓ migrated → stone-02     (4m 23s)
  redis        ✓ migrated → stone-03     (0m 12s, 1 retry)
  postgresql   ✗ failed                  (3 attempts exhausted)
  
  postgresql failed because:
    Attempt 1: stone-02 — pg_dump timeout after 300s
    Attempt 2: stone-03 — disk space exhausted during transfer  
    Attempt 3: stone-04 — incompatible architecture (requires x86_64)
    
  Suggestions:
    • Increase pg_dump timeout: garden-rake move postgresql --timeout 600
    • Free disk space on stone-03: garden-rake prune stone-03
    • Add x86_64 Stone with 20GB+ free space
    • Manual migration: pg_dump on source, pg_restore on target
```

---

## API Specification

### Elder Recognition Endpoints (Moss)

**Recognize stone as elder:**
```http
POST /api/v1/stones/{stone_name}/elder
Content-Type: application/json

{
  "trust": 500
}

Response 200:
{
  "stone": "stone-03",
  "previous_score": 127,
  "trust_granted": 500,
  "new_score": 627,
  "status": "elder",
  "message": "stone-03 recognized as Elder"
}
```

**Remove elder recognition:**
```http
DELETE /api/v1/stones/{stone_name}/elder

Response 200:
{
  "stone": "stone-03",
  "previous_score": 627,
  "trust_removed": 500,
  "new_score": 127,
  "status": "seedling",
  "message": "stone-03 will prove itself through uptime"
}
```

**Query elder status:**
```http
GET /api/v1/stones/{stone_name}/elder

Response 200:
{
  "stone": "stone-03",
  "elder_score": 627,
  "status": "elder",
  "components": {
    "uptime_score": 127,
    "manual_trust": 500,
    "ceremony_bonus": 0,
    "stability_penalty": 0,
    "network_factor": 1.0
  },
  "recognized": true,
  "recognized_at": "2026-01-20T10:30:00Z"
}
```

### Ceremony Endpoints (Moss)

**Initiate ceremony:**
```http
POST /api/v1/ceremonies
Content-Type: application/json

{
  "type": "vacate",
  "source": "stone-01",
  "options": {
    "dry_run": false
  }
}

Response 202 Accepted:
{
  "ceremony_id": "vacate-stone01-20260120-1423",
  "status": "electing",
  "message": "Ceremony initiated, electing coordinator"
}
```

**Query ceremony status:**
```http
GET /api/v1/ceremonies/{ceremony_id}

Response 200:
{
  "ceremony_id": "vacate-stone01-20260120-1423",
  "status": "executing",
  "coordinator": "stone-02",
  "progress": {
    "offerings_total": 3,
    "offerings_completed": 1,
    "current_offering": "redis",
    "current_step": "transferring"
  }
}
```

**List ceremonies:**
```http
GET /api/v1/ceremonies?status=active&limit=10

Response 200:
{
  "ceremonies": [...],
  "total": 5
}
```

**Cancel ceremony:**
```http
DELETE /api/v1/ceremonies/{ceremony_id}

Response 200:
{
  "ceremony_id": "vacate-stone01-20260120-1423",
  "status": "cancelled",
  "message": "Ceremony cancelled, rollback initiated"
}
```

### Election Messages (UDP)

**Election request:**
```json
{
  "type": "CEREMONY_ELECTION",
  "ceremony_id": "vacate-stone01-20260120-1423",
  "ceremony_type": "vacate",
  "source": "stone-01",
  "initiated_by": "operator",
  "timestamp": "2026-01-20T14:23:00Z"
}
```

**Coordinator claim:**
```json
{
  "type": "CEREMONY_CLAIM",
  "ceremony_id": "vacate-stone01-20260120-1423",
  "coordinator": "stone-02",
  "elder_score": 423,
  "timestamp": "2026-01-20T14:23:02Z"
}
```

**Progress update:**
```json
{
  "type": "CEREMONY_PROGRESS",
  "ceremony_id": "vacate-stone01-20260120-1423",
  "coordinator": "stone-02",
  "phase": "executing",
  "current_offering": "mongodb",
  "current_step": "snapshot",
  "progress_percent": 45,
  "timestamp": "2026-01-20T14:25:30Z"
}
```

### Subscription Events (SSE)

Applications can subscribe to ceremony events:

```http
GET /api/v1/events?topics=ceremonies

event: ceremony.started
data: {"ceremony_id": "vacate-stone01-...", "type": "vacate"}

event: ceremony.progress
data: {"ceremony_id": "vacate-stone01-...", "offering": "mongodb", "step": "migrating"}

event: ceremony.offering_migrated
data: {"ceremony_id": "vacate-stone01-...", "offering": "mongodb", "to": "stone-02"}

event: ceremony.completed
data: {"ceremony_id": "vacate-stone01-...", "status": "partial", "migrated": 2, "failed": 1}
```

---

## Configuration

### Moss Configuration

```toml
# /etc/zen-garden/moss.toml

[ceremonies]
# Maximum concurrent ceremonies this Stone can coordinate
max_coordinating = 2

# Maximum concurrent ceremonies this Stone can participate in
max_participating = 5

# Election timeout
election_timeout_seconds = 5

# Default retry policy
max_attempts = 3
retry_backoff_base_ms = 1000
retry_backoff_multiplier = 2.0

[healing]
# Periodic sweep interval
sweep_interval_minutes = 60

# Auto-cleanup orphaned artifacts older than
orphan_threshold_minutes = 30

# Pressure threshold (free space %)
pressure_threshold_percent = 10

[elder]
# Minimum score to coordinate ceremonies (Mature threshold)
coordinator_min_score = 350

# Default trust value for manual recognition
default_recognition_trust = 500

# Maximum trust value allowed
max_recognition_trust = 1000

# Elder score calculation weights (advanced tuning)
uptime_log_multiplier = 100.0
restart_penalty = 50
ceremony_bonus_multiplier = 100.0
```

### Elder Recognition Persistence

Manual elder recognition is stored in the Stone's local configuration:

```toml
# /etc/zen-garden/stone-identity.toml

[identity]
name = "stone-03"
garden = "home-lab"

[elder]
recognized = true
trust = 500
recognized_at = "2026-01-20T10:30:00Z"
recognized_by = "operator"
```

This persists across Moss restarts. Remove with `garden-rake forget stone-03 as elder`.

---

## References

- [Technical Specification](../specs/technical.md) — Core architecture
- [Discovery Protocol](../specs/discovery.md) — mDNS and UDP broadcast
- [Offerings Specification](../specs/offerings.md) — Manifest format
- [Architecture Joy](../architecture/joy-in-infrastructure.md) — Design philosophy
- [Stone Lifecycle Proposal](stone-lifecycle.md) — Related operations (retire, replace, lift)
- [CLI Taxonomy Proposal](cli-taxonomy.md) — Zen vs normative syntax

---

**Last Updated:** January 2026  
**Status:** Proposal — pending review and implementation

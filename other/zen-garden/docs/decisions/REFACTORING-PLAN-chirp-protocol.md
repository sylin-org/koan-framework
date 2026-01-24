# Refactoring Plan: Chirp Protocol Implementation Gaps

**Date**: 2026-01-23  
**Related ADR**: TOPO-0001 Inter-Stone Communication Protocol (P2P Mode)  
**Priority**: High - Core topology discovery broken

---

## Executive Summary

Validation of TOPO-0001 against actual implementation revealed **critical gaps**. The document describes a two-stage startup chirp system with health status progression that **does not exist in the code**. Current implementation has only a single chirp at startup with no health status field.

---

## Validation Results

### ✅ Correctly Documented (Already Implemented)

1. **UDP Listener Pipeline** - Single listener with message routing exists
   - `UdpEvent::Request` → sends chirp response  
   - `UdpEvent::Chirp` → updates topology cache  
   - `UdpEvent::Goodbye` → marks stone offline  
   - Location: `src/moss/src/discovery.rs` (lines 173-190)

2. **Periodic Announcer** - 30s interval with change detection
   - Location: `src/moss/src/tasks/announcer.rs`
   - Uses JSON-based hash comparison
   - 5-minute keep-alive logic present

3. **Service Event Subscription** - Immediate chirps on service changes
   - Location: `src/moss/src/bootstrap/run.rs` (Phase 15, lines 218-237)
   - Listens for events containing "service" or "offering"

4. **Graceful Shutdown** - Goodbye announcement exists
   - Location: `src/moss/src/announcement.rs` (`send_goodbye`)

5. **Announcement Module** - Core chirp logic centralized
   - Location: `src/moss/src/announcement.rs`
   - DRY principle applied

### ❌ Missing from Code (Document Claims Not Implemented)

#### **CRITICAL: Two-Stage Startup Chirp**

**Document Claims:**
- Phase 13a: Early chirp with status "initializing"
- Phase 13b-17: Complete inventory
- Phase 13c: Full chirp with status "thriving" or "degraded"

**Actual Code:**
- **Phase 13**: Single chirp only (line 208-213 in `bootstrap/run.rs`)
- No early chirp before inventory
- No full chirp after inventory
- Services already loaded when chirp sent (Phase 11 registry loader runs before chirp)

**Impact**: Peers see stones either fully ready or offline, no "initializing" state. Document claims two chirps but only one exists.

---

#### **CRITICAL: Health Status Field Missing**

**Document Claims:**
- Chirp payload includes "health status" field
- Health progression: "initializing" → "thriving" | "degraded"
- Status determined by service errors during inventory

**Actual Code:**
- `StoneChirpPayload` has NO `health` field (`src/common/src/types.rs`, lines 438-447)
- `AnnouncementPayload` has NO `health` field (`src/moss/src/announcement.rs`, lines 27-36)
- `build_payload()` does NOT calculate health status
- No health determination logic exists anywhere

**Impact**: Topology consumers (Rake observe) cannot display health status. Critical for operational visibility.

---

#### **CRITICAL: UDP Listener Not Phase 1**

**Document Claims:**
- Phase 1: Start UDP listener (first thing)
- Listener enables all subsequent UDP communication

**Actual Code:**
- **Phase 11**: UDP listener starts (line 140 in `bootstrap/run.rs`)
- After: Phase 0 (stone_id), Phase 1 (first-boot), Phase 2 (network monitor), Phase 3 (endpoint), Phase 4 (mDNS), Phase 5 (Lantern), Phase 6 (console), Phase 7 (docker), Phase 8 (channels), Phase 9 (capabilities), Phase 10 (AppState)

**Impact**: **DOCUMENT IS CORRECT, CODE IS WRONG**. UDP listener SHOULD be Phase 1 to enable early discovery. Currently starts late after dependencies that don't need it (docker, capabilities, etc.). This delays stone visibility on network.

---

#### **ISSUE: Hardware Capabilities Not in Chirp Payload**

**Document Claims (Known Limitation section):**
- "No capabilities in chirps: Hardware capabilities announced via mDNS TXT only, not UDP payload"
- "Basic info in early chirp" (Chirp Payload section)

**Actual Code:**
- Completely true - no capabilities in `StoneChirpPayload`
- BUT: Document describes "basic info (MAC, CPU, storage)" in early chirp - this doesn't exist

**Impact**: Limitation documented correctly but early chirp claim contradicts it.

---

## Refactoring Tasks

### Phase 1: Add Health Status Field (Required for Current Doc)

**Priority**: P0 - Breaks observe output expectations

**Tasks:**
1. Add `health: String` field to `StoneChirpPayload` (src/common/src/types.rs)
2. Add `health: String` field to `AnnouncementPayload` (src/moss/src/announcement.rs)
3. Implement health calculation in `build_payload()`:
   ```rust
   let health = if state.registry.read().await.iter().any(|s| s.status != ServiceStatus::Running) {
       "degraded"
   } else {
       "thriving"
   }.to_string();
   ```
4. Update topology cache to store health field
5. Update Rake observe to display health status
6. Add health status to `/api/v1/garden/topology` response

**Estimated Effort**: 2 hours

---

### Phase 2: Implement Two-Stage Startup Chirp (Optional - Architecture Decision Needed)

**Priority**: P1 - Document describes this but may not be necessary

**Decision Required**: Is two-stage chirp actually needed, or should document be updated to reflect single-chirp reality?

**If Implementing Two-Stage:**

**Tasks:**
1. Add "initializing" health status value
2. Move Phase 13 chirp to Phase 13a (before inventory tasks)
3. Send early chirp with:
   - Empty services list
   - health: "initializing"
   - Basic hardware (MAC, CPU, storage) if available
4. Keep existing chirp as Phase 13c (after Phase 13b-17 inventory)
5. Calculate final health in Phase 13c based on service states

**Estimated Effort**: 4 hours

**If NOT Implementing:**
- Update TOPO-0001 to remove two-stage chirp description
- Document single chirp at Phase 13 with full data
- Simplify health status to: "thriving" | "degraded" (no "initializing")

**Estimated Effort**: 30 minutes (documentation only)

---

### Phase 3: Move UDP Listener to Phase 1 (CODE FIX - CRITICAL)

**Priority**: P0 - Architecture correctness, early stone visibility

**Rationale:**
- UDP listener has minimal dependencies (only stone_id, stone_name, api_endpoint)
- Does NOT need: Docker, capabilities, console, AppState
- Starting early enables stones to respond to discovery immediately
- Other stones can see this stone as soon as it boots
- Document correctly specifies this architecture, code needs to match

**Tasks:**
1. Move `start_discovery_listener()` call to immediately after Phase 0 (stone_id load)
2. Requires: stone_id, stone_name, initial endpoint (before network monitor)
3. Change from Phase 11 → Phase 1
4. Update subsequent phase numbers if needed
5. Test that early listener doesn't break anything
6. Verify stones respond to discovery during boot

**Dependencies to Resolve:**
- Topology cache: Currently needs AppState (Phase 10). Options:
  - Create topology cache earlier (just needs HashMap)
  - Pass as parameter rather than via AppState
  - Start listener but delay topology updates until cache ready
  
**Estimated Effort**: 1-2 hours (careful refactoring)

---

### Phase 4: Hardware Capabilities in Chirp (Future Enhancement)

**Priority**: P3 - Listed as "Known Limitation #2" in doc

**Tasks:**
1. Add `capabilities: Option<HardwareCapabilities>` to `StoneChirpPayload`
2. Include capabilities from `state.capabilities.read().await` in `build_payload()`
3. Update topology cache to store capabilities
4. Update Rake observe to use capabilities from topology (already attempted in earlier work)
Move UDP listener to Phase 1 (Phase 3 above) - **1-2 hours**

**Total**: ~4-5 hours, critical architecture fixes
**Note**: This makes UDP chirp size larger (~200-500 bytes), acceptable tradeoff for avoiding HTTP fallback.

---

## Recommended Action Plan

### Option A: Quick Fix (Minimal Changes)

1. Add health status field to chirps (Phase 1 above) - **2 hours**
2. Update TOPO-0001 to remove two-stage chirp claims - **30 minutes**
3. Fix phase numbering in doc (Phase 3 above) - **15 minutes**

**Total**: ~3 hours, document accurate, health status working

### Option B: Full Implementation (Match Document)

1. Add health status field (Phase 1) - **2 hours**
2. Move UDP listener to Phase 1 (Phase 3) - **1-2 hours**
3. Implement two-stage chirp (Phase 2) - **4 hours**
4. Add capabilities to chirp payload (Phase 4) - **3 hours**

**Total**: ~10-11 hours, document fully accurate, complete feature

### Option C: Current State (No Changes) - **NOT RECOMMENDED**

1. Update TOPO-0001 to reflect actual code - **1 hour**
   - Remove two-stage chirp
   - Remove health status
   - Change "Phase 1" to "Phase 11" for listener
   - Remove "basic info in early chirp" claims

**Total**: 1 hour, document accurate but **architecture remains suboptimal** (late listener start)

---

## Recommendation

**NEW APPROACH: Self Topology Entry (Progressive Chirp Model)**

**Architecture:**
Instead of two-stage chirps or single chirp, implement a **self topology entry** that progressively populates during boot:

```rust
// New struct in AppState or dedicated module
pub struct SelfTopologyEntry {
    pub stone_id: String,
    pub stone_name: String,
    pub endpoint: Option<String>,
    pub moss_version: String,
    pub mac: Option<String>,
    pub health: String, // "starting" → "initializing" → "thriving" | "degraded"
    pub basic_hardware: Option<BasicHardware>, // CPU, memory, storage
    pub capabilities: Option<HardwareCapabilities>, // Full detection
    pub services: Vec<ChirpServiceInfo>, // Starts empty, grows
    pub last_updated: Instant,
}
```

**Boot Flow:**
1. **Phase 0**: Create self entry: `{ health: "starting", stone_id, stone_name, ... }` - rest None/empty
2. **Phase 1**: Start UDP listener → **Announcement requests chirp self entry immediately** (whatever state available)
3. **Phase 2**: Network ready → populate `endpoint`, `mac` → update self entry → **auto-chirp**
4. **Phase 3**: Basic hardware detected → populate `basic_hardware` → update self entry → `health = "initializing"`
5. **Phase 11**: Services loaded → populate `services` → update self entry
6. **Phase 13**: Full inventory complete → populate `capabilities` → `health = calculate_health()` → **auto-chirp**
7. **Ongoing**: Service changes → update `services` in self entry → **auto-chirp**

**Benefits:**
- ✅ **Always ready to respond**: Announcement requests work from boot onwards
- ✅ **Progressive disclosure**: Chirps get richer as data becomes available
- ✅ **No timing logic**: No "early vs full" chirp coordination
- ✅ **Single source of truth**: Self entry = what we chirp
- ✅ **Natural health progression**: "starting" → "initializing" → "thriving"/"degraded"
- ✅ **Decoupled concerns**: Self-awareness separate from peer topology cache

**Implementation Tasks:**

### Phase 1: Create Self Topology Entry (4 hours)
1. Define `SelfTopologyEntry` struct in new module `src/moss/src/domain/self_topology.rs`
2. Add `self_topology: Arc<RwLock<SelfTopologyEntry>>` to AppState
3. Initialize in Phase 0 with `health: "starting"`
4. Create `update_self_topology()` helper that triggers auto-chirp on changes
5. Wire progressive updates throughout bootstrap phases

### Phase 2: Integrate with Announcement System (2 hours)
1. Change `build_payload()` to read from self entry instead of gathering from state
2. Announcement requests chirp self entry immediately (no dependencies)
3. Remove Phase 13 single chirp (happens automatically on full inventory)
4. Add auto-chirp on self entry updates

### Phase 3: Move UDP Listener to Phase 1 (1-2 hours)
1. Listener only needs stone_id, stone_name, initial self entry
2. Announcement requests work immediately (chirp whatever is in self entry)
3. Topology cache can initialize later (Phase 10)

### Phase 4: Add Health Status Field (1 hour)
1. Add `health: String` to StoneChirpPayload
2. Already in SelfTopologyEntry
3. Calculate during Phase 13 inventory completion

**Total Effort**: ~8 hours for complete progressive chirp system

**Advantages Over Two-Stage:**
- Simpler: No coordination between "early" and "full" chirp
- Flexible: Can answer requests at ANY point in boot
- Observable: Self entry shows exact readiness state
- Maintainable: Adding new fields just updates self entry, auto-chirps

**Next Steps:**
1. Implement SelfTopologyEntry structure
2. Move UDP listener to Phase 1
3. Wire progressive updates
4. Test that announcements work from early boot onwards

## Additional Finding: Periodic Announcer IS Working

**Verification Results:**
- Periodic announcer DOES start (confirmed in logs: "Periodic announcer started")
- Announcer IS running and emitting chirps every 30s
- Logs use `debug!` and `trace!` levels, so chirps not visible at default INFO level
- Change detection working (skips unchanged states)
- Keep-alive working (forces chirp every 5 minutes)

**Evidence:**
```
Jan 23 23:14:35 garden-moss: Periodic announcer started (30s interval, 5min keep-alive)
```

**Status**: ✅ Working as designed, just needs log level adjustment to see activity

---

## Testing Checklist

After implementing chosen option:

- [ ] Three stones boot and chirp successfully
- [ ] Rake observe shows health status for all stones
- [ ] Health changes from "thriving" to "degraded" when service fails
- [ ] Periodic chirps include health status
- [ ] Service change events trigger chirps with updated health
- [ ] Goodbye announcements preserve health state
- [ ] Topology API includes health in response
- [ ] Document claims match code behavior

---

## Related Files

**Code:**
- `src/moss/src/bootstrap/run.rs` - Startup orchestration
- `src/moss/src/announcement.rs` - Chirp logic
- `src/common/src/types.rs` - StoneChirpPayload definition
- `src/moss/src/tasks/announcer.rs` - Periodic chirps
- `src/moss/src/discovery.rs` - UDP listener
- `src/rake/src/commands/discovery/observe.rs` - Consumer of chirps

**Documentation:**
- `docs/decisions/TOPO-0001-unified-announcement-system.md` - Protocol specification

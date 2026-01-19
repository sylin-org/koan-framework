---
status: Accepted
date: 2026-01-15
---

# STATE-0001: Stateless Moss Architecture

## Status

**Accepted** - Implemented in Moss daemon design

## Context

Moss daemon maintains a **Stone registry** (topology of other Stones in the garden). Registry includes: Stone names, endpoints, services offered, last-seen timestamps.

**Question:** Should Moss persist registry to disk or keep it in-memory only?

**Constraints:**
- Moss restarts must not break garden operations
- Discovery must remain fast (<1 second)
- Simplicity preferred (no database dependency)
- Eventual consistency acceptable (home lab scale)

**Source:** [TECHNICAL-SPEC.md § State Management](../TECHNICAL-SPEC.md#concurrency-and-state-management), [decisions/MOSS-0001-persistent-registry-and-adoption.md](MOSS-0001-persistent-registry-and-adoption.md)

## Decision

Moss uses **in-memory registry** with fast-sync rebuild on restart. NO disk persistence.

**Rationale:**
1. **Simplicity:** No database, no file corruption, no consistency checks
2. **Source of truth:** UDP broadcasts and Lantern HTTP are authoritative (not Moss memory)
3. **Fast recovery:** Fast-sync rebuilds registry in <5 seconds
4. **Eventual consistency:** Home lab scale (10 Stones max) converges quickly

**Fast-sync process (on Moss startup):**
```
1. Moss boots, registry empty
2. Query Lantern HTTP: GET /api/stones (if configured)
3. For each known Stone: HTTP probe GET /api/garden/stones
4. Aggregate responses, populate registry
5. Begin listening to UDP lifecycle broadcasts (passive updates)
```

**Fallback:** If Lantern unavailable, rely on UDP broadcasts (registry rebuilds within 90s TTL).

## Consequences

### Positive

✅ **No database dependency:** Moss is single Rust binary, no SQLite/PostgreSQL required  
✅ **No file corruption:** In-memory state lost on crash, but rebuilt from source of truth  
✅ **Fast restarts:** No disk I/O, startup in milliseconds  
✅ **Simpler testing:** No need to mock filesystem, database  
✅ **Eventual consistency:** Acceptable for home lab (not mission-critical CAP theorem system)

### Negative

❌ **Registry rebuild latency:** 5-90 seconds (Lantern fast-sync vs UDP fallback)  
❌ **Repeated discovery:** Every Moss restart queries Lantern (adds load)  
❌ **No historical data:** Cannot query "what Stones were online yesterday"  
❌ **No crash recovery:** If Moss crashes mid-operation, operator must retry

### Risks

**Risk:** Moss restart during critical operation (e.g., multi-Stone upgrade)  
**Mitigation:** Operations are idempotent (safe to retry). Operator re-runs command.

**Risk:** Fast-sync timeout (Lantern slow, Stones unreachable)  
**Mitigation:** Configurable timeout (default 5s), falls back to UDP broadcasts (90s worst-case)

**Risk:** Network partition during fast-sync (incomplete registry)  
**Mitigation:** Eventual consistency via ongoing UDP broadcasts (missing Stones re-announce)

## Alternatives Considered

### Alternative 1: Disk-Backed Registry (SQLite)

**Approach:** Moss writes registry to SQLite database (`/var/lib/zen-garden/registry.db`)

**Why not:**
- **Complexity:** SQLite dependency, schema migrations, consistency checks
- **Corruption risk:** Power loss during write corrupts database (recovery difficult)
- **Limited benefit:** Registry rebuilt from UDP/Lantern anyway (disk not source of truth)
- **Testing burden:** Must mock filesystem, handle SQLite errors

**Trade-off:** Instant restart (no fast-sync) vs complexity/corruption risk → **Not worth it**

### Alternative 2: Persistent Memory (mmap)

**Approach:** Memory-mapped file (`/var/lib/zen-garden/registry.mmap`), OS handles persistence

**Why not:**
- **Platform-specific:** mmap behavior varies (Linux, macOS, Windows)
- **Corruption risk:** Same as SQLite (power loss mid-write)
- **No portability:** Binary format not human-readable (debugging harder)

### Alternative 3: Distributed Consensus (Raft/Paxos)

**Approach:** Stones maintain replicated state machine, consensus on registry changes

**Why not:**
- **Massive complexity:** Raft implementation is ~5,000 LOC minimum
- **Over-engineering:** Home lab (10 Stones) does not need distributed consensus
- **Performance penalty:** Consensus adds latency (50-200ms per operation)
- **CAP theorem:** Not building a distributed database (AP system sufficient)

**When reconsidered:** If Zen Garden scales to 100+ Stones (Phase 3+), distributed consensus may be needed

## Implementation Notes

### Fast-Sync Configuration

```toml
# /etc/zen-garden/moss.toml
[discovery]
fast_sync_on_startup = true      # Enable fast-sync (default: true)
fast_sync_timeout = 5            # Seconds to wait for Lantern response
fast_sync_lantern = "http://lantern-host:7186"  # Optional Lantern URL
```

### Fast-Sync Flow (Rust)

```rust
async fn fast_sync(config: &Config) -> Result<StoneRegistry> {
    let mut registry = StoneRegistry::new();
    
    // 1. Query Lantern HTTP (if configured)
    if let Some(lantern_url) = &config.fast_sync_lantern {
        match tokio::time::timeout(
            Duration::from_secs(config.fast_sync_timeout),
            query_lantern(lantern_url)
        ).await {
            Ok(Ok(stones)) => {
                for stone in stones {
                    registry.insert(stone);
                }
                info!("Fast-sync: {} stones from Lantern", registry.len());
            },
            Ok(Err(e)) => warn!("Lantern unreachable: {}", e),
            Err(_) => warn!("Lantern timeout after {}s", config.fast_sync_timeout),
        }
    }
    
    // 2. Probe known Stones via HTTP (if any discovered from Lantern)
    for stone in registry.stones() {
        if let Ok(details) = probe_stone(&stone.endpoint).await {
            registry.update(stone.name, details);
        }
    }
    
    // 3. Begin listening to UDP broadcasts (ongoing)
    tokio::spawn(listen_udp_broadcasts(registry.clone()));
    
    Ok(registry)
}
```

### UDP Broadcast Fallback

If fast-sync fails (Lantern unreachable, all probes timeout), Moss relies on UDP broadcasts:

```
1. Moss listens on UDP port 7184
2. Stones broadcast lifecycle events every 30s:
   - StoneOnline { name, endpoint, services }
   - ServiceInstalled { stone, service, port }
   - ServiceRemoved { stone, service }
3. Moss updates registry from broadcasts
4. TTL: 90 seconds (if no broadcast, Stone marked stale)
```

**Worst-case recovery:** 90 seconds (3x 30s broadcast interval)

## References

- **Technical spec:** [TECHNICAL-SPEC.md § Moss Startup Fast-Sync](../TECHNICAL-SPEC.md#moss-startup-fast-sync)
- **Related decision:** [MOSS-0001-persistent-registry-and-adoption.md](MOSS-0001-persistent-registry-and-adoption.md)
- **Discovery spec:** [TECHNICAL-SPEC.md § mDNS Discovery](../TECHNICAL-SPEC.md#mdns-discovery)
- **Lantern API:** [REFERENCE.md § Lantern HTTP API](../REFERENCE.md#lantern-http-api)

## Trade-Offs Summary

| Aspect | In-Memory (Chosen) | Disk-Backed (Rejected) |
|--------|-------------------|----------------------|
| **Restart latency** | 5-90s (fast-sync) | <1s (instant) |
| **Complexity** | Low (no DB) | High (SQLite + migrations) |
| **Corruption risk** | None (volatile) | Medium (power loss) |
| **Source of truth** | UDP/Lantern | Disk file (stale risk) |
| **Historical queries** | Not supported | Supported |
| **Testing** | Simple (in-memory) | Complex (mock filesystem) |

**Decision:** Simplicity + reliability > instant restart

## Future Considerations

**If Zen Garden scales beyond 100 Stones (Phase 3+):**
- Reconsider distributed consensus (Raft/etcd)
- Add persistent audit log (append-only, for compliance)
- Implement snapshot/restore (backup registry state)

**Current scale (10 Stones):** In-memory + fast-sync sufficient ✅

## Versioning

**Moss v1.x:** Stateless architecture stable (no disk persistence)

**Breaking changes:** If v2.0 adds disk persistence, must provide migration path (export in-memory registry → import to disk)

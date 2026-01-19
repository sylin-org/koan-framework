# RAKE-0010: Tending - Cached Endpoint Resolution

**Status:** Accepted  
**Date:** 2026-01-17  
**Deciders:** Architecture Team  
**Tags:** rake, discovery, performance, ux

## Context

Garden-rake exhibited 1-2 second startup delays on every command invocation due to stateless endpoint resolution:

1. Check localhost:7185 (750ms timeout - always fails on Windows)
2. UDP broadcast discovery (3s timeout - even fast responses wait full timeout window)
3. No persistence between command invocations

For interactive CLI use, this creates poor UX. Users expect sub-100ms response for simple commands like `garden-rake status`.

Constraints:
- Rake operates in environments with no stone (valid scenario - must signal clearly)
- Rake on Linux stones tends to localhost by default (zero network overhead)
- Rake on Windows must discover remote stones (network required)
- `--at` flag must provide deterministic behavior for CI/CD scripts
- Zero-config first use: `garden-rake status` should "just work" on fresh stone

## Decision

Implement **tending** - a stateful concept where rake "tends to" a specific stone, with cached endpoint resolution.

### Resolution Priority

1. `--at <endpoint>` flag (explicit override, deterministic)
2. `GARDEN_STONE` environment variable (optional optimization)
3. `~/.zen-garden/.tending` file (cached discovery result, 90s TTL)
4. Auto-discover via UDP broadcast + write `.tending` file

### Tending File Format

Location: `~/.zen-garden/.tending`

```json
{
  "stone_name": "stone-golden-delta",
  "endpoint": "http://192.168.1.108:7185",
  "last_seen": "2026-01-17T09:42:15Z"
}
```

### New Subcommand: `tend`

```bash
garden-rake tend                    # Show current tending state
garden-rake tend this               # Set to localhost (alias: local)
garden-rake tend auto               # Force fresh discovery
garden-rake tend <endpoint>         # Set explicit endpoint
garden-rake tend --clear            # Clear tending state
```

### Behavior Changes

**First use (no .tending file):**
```
Discovering stones...
  Found stone-golden-delta.local (192.168.1.108:7185)
  Now tending to stone-golden-delta.local

Status: Healthy
```

**Subsequent use (cached, <50ms startup):**
```
Tending to: stone-golden-delta.local

Status: Healthy
```

**With --at override:**
```
Tending to: 192.168.1.110:7185 (override)

Status: Healthy
```

**Cache invalidation:**
- TTL expired (>90s): Transparent re-validation
- Connection failed: Clear `.tending`, auto-discover again
- User runs `garden-rake tend --clear`: Manual clear

## Consequences

### Positive

- **Sub-100ms startup** for cached commands (1-2ms file I/O vs 1-2s discovery)
- **Zero-config UX**: First command auto-discovers and caches
- **Clear mental model**: "Which stone am I working with?" vs "where is my config?"
- **Explicit overrides**: `--at` for deterministic scripts, `tend` for manual control
- **Graceful degradation**: Failed connection triggers auto-rediscovery
- **Cross-platform**: Works identically on Linux/Windows, with/without local moss

### Negative

- **Stale cache risk**: If stone IP changes (DHCP), cached endpoint fails until TTL expires
  - Mitigation: 90s TTL matches existing CACHE_TTL constant
  - Mitigation: Connection failure triggers immediate rediscovery
- **Hidden state**: `.tending` file is invisible until user runs `garden-rake tend`
  - Mitigation: All commands show "Tending to: ..." header for visibility
- **Additional I/O**: Every command reads `.tending` file
  - Acceptable: 1-2ms file read vs 1000-3000ms network discovery

### Alternative Approaches Considered

1. **Reduced timeouts** (750ms→200ms, 3s→800ms)
   - Rejected: Band-aid solution, still 1s delay minimum
   
2. **Background daemon** (long-running process, CLI talks to daemon)
   - Rejected: Added complexity, cross-platform daemon management issues
   
3. **DNS-SD/mDNS** (proper service discovery)
   - Deferred: Requires mDNS client library, moss already uses mDNS for stone-to-stone
   
4. **Environment variable only** (`GARDEN_STONE=http://...`)
   - Rejected: Requires manual setup, doesn't solve zero-config UX goal

## Implementation Notes

### File Location

- Linux/macOS: `~/.zen-garden/.tending`
- Windows: `%USERPROFILE%\.zen-garden\.tending`
- Use `dirs::home_dir()` crate for cross-platform home directory resolution

### TTL Validation

```rust
const TENDING_TTL: Duration = Duration::from_secs(90);

fn is_tending_valid(tending: &TendingState) -> bool {
    tending.last_seen.elapsed() < TENDING_TTL
}
```

### Localhost Fast-Path

When `garden-rake tend this` is used, validate local moss exists:

```rust
let health_check = client
    .get("http://127.0.0.1:7185/health")
    .timeout(Duration::from_millis(200))
    .send()
    .await?;

if health_check.status().is_success() {
    write_tending("localhost", "http://127.0.0.1:7185")?;
} else {
    return Err("No local moss detected");
}
```

## References

- Existing stone cache: `src/rake/src/stone_cache.rs` (90s TTL)
- Discovery module: `src/rake/src/discovery.rs`
- Moss discovery response includes stone_name for display

## Acceptance Criteria

- [ ] First `garden-rake status` auto-discovers and caches (shows discovery message)
- [ ] Second `garden-rake status` completes in <100ms (reads cache)
- [ ] `garden-rake tend` shows current tending state
- [ ] `garden-rake tend this` validates localhost moss before writing cache
- [ ] `garden-rake tend auto` forces fresh discovery
- [ ] `--at` flag bypasses all caching (deterministic for scripts)
- [ ] Failed connection clears cache and rediscovers automatically
- [ ] All commands show "Tending to: ..." header for context
- [ ] Cache expires after 90s, triggers transparent revalidation
- [ ] `GARDEN_STONE` env var honored when set

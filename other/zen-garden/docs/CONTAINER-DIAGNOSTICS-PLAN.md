# Container Diagnostics Enhancement Plan

**Problem:** Containers restarting repeatedly with no visibility into logs or failure reasons.

**Date:** 2026-01-16  
**Priority:** HIGH (blocking production debugging)

---

## Related Features

### Binary Refresh (Development Tool) ✅ COMPLETE

**Purpose:** Push locally-built binaries to remote stones during development

**Command:**
```bash
garden-rake refresh garden-moss --from ./target/release/garden-moss
garden-rake refresh rake --from ./dist/linux-x64/garden-rake
```

**API Endpoint:** `POST /api/system/refresh`

**Features:**
- ELF architecture validation (prevents installing x86_64 binary on ARM stone)
- Base64 encoding for binary transfer over HTTP
- Atomic replacement using temp file + rename
- Automatic moss restart after self-update
- Health check polling after restart
- Supports both moss and garden-rake updates

**Workflow:**
1. Build binary locally (Windows or Linux)
2. Run `refresh` command pointing to binary file
3. Binary validated for ELF format and architecture match
4. Uploaded via HTTP POST (base64-encoded)
5. Written to `/usr/local/bin/{component}.new`
6. Made executable (chmod +x)
7. Atomically moved to final location
8. For moss: exits (systemd restarts with new binary)
9. CLI waits and confirms moss is back online

**Security Note:** No authentication (development-only feature, assumes trusted network)

---

## Proposed Solution

### 1. Extend `garden-rake watch` Command

**Current Syntax:**
```bash
garden-rake watch [--until PATTERN]   # Watches event stream
```

**New Syntax:**
```bash
# Stream logs in real-time (SSE)
garden-rake watch offering <name> logs [OPTIONS]
garden-rake watch stone <name> logs [OPTIONS]

# Dump tail once and exit (no streaming)
garden-rake watch offering <name> logs --tail <N>

Options:
  --tail <N>           Show last N lines and exit (no streaming)
  --timestamps         Show timestamps
  --at <ENDPOINT>      Moss endpoint (omit to auto-discover)
```

**Examples:**
```bash
# Stream MongoDB logs in real-time via SSE (like watch for events)
garden-rake watch offering mongodb logs

# Just dump last 100 lines once and exit
garden-rake watch offering mongodb logs --tail 100

# Stream with timestamps
garden-rake watch offering mongodb logs --timestamps

# Stream all offerings on a stone (interleaved)
garden-rake watch stone stone-amber-terrace logs
```

**Behavior:**
- **Without `--tail`**: Opens SSE connection, streams logs continuously as they appear
- **With `--tail N`**: Fetches last N lines via HTTP GET, prints them, exits (no streaming)

---

### 2. Enhance Moss API Endpoints

**New Endpoints:**

**1. GET /api/services/:service/logs (SSE streaming)**
```
GET /api/services/:service/logs?timestamps=false
```

**Response (Server-Sent Events - continuous stream):**
```
event: log
data: {"timestamp": "2026-01-16T16:00:00Z", "stream": "stdout", "log": "Starting MongoDB..."}

event: log
data: {"timestamp": "2026-01-16T16:00:01Z", "stream": "stderr", "log": "Error: Permission denied"}
```

**2. GET /api/services/:service/logs/tail (one-shot)**
```
GET /api/services/:service/logs/tail?lines=100&timestamps=false
```

**Response (JSON - single response):**
```json
{
  "service": "mongodb",
  "lines": [
    {"timestamp": "2026-01-16T16:00:00Z", "stream": "stdout", "log": "Starting MongoDB..."},
    {"timestamp": "2026-01-16T16:00:01Z", "stream": "stderr", "log": "Error: Permission denied"}
  ]
}
```

**Behavior:**
- SSE endpoint: Streams logs continuously using bollard's `logs()` with `follow=true`
- Tail endpoint: Returns last N lines as JSON, then closes connection

---

### 3. Enhance `observe` Command Output

**Current Output:**
```
●  stone-amber-terrace (Healthy, uptime: 4m 38s)
   OFFERINGS:
   ├─ mongodb       Run   0.00%        0 B  ↓     0 B  43s
```

**Enhanced Output (show restart count and status):**
```
●  stone-amber-terrace (Healthy, uptime: 4m 38s)
   OFFERINGS:
   ├─ mongodb       Run   0.00%        0 B  ↓     0 B  43s  (restarted 3x)
   └─ redis         Crash                                   (restarted 5x, exit code 1)
```

**Add color coding:**
- Green: 0 restarts
- Yellow: 1-2 restarts
- Red: 3+ restarts

---

### 4. Add Container Diagnostics to API

**Enhance ServiceInfo with diagnostics:**
```rust
pub struct ServiceInfo {
    // ... existing fields ...
    pub diagnostics: Option<ServiceDiagnostics>,
}

pub struct ServiceDiagnostics {
    pub restart_count: u32,
    pub last_exit_code: Option<i64>,
    pub last_error: Option<String>,
    pub oom_killed: bool,
    pub health_check_failing: bool,
}
```

**Add to docker.rs:**
```rust
impl DockerManager {
    pub async fn get_diagnostics(&self, name: &str) -> Result<ServiceDiagnostics> {
        let inspect = self.docker.inspect_container(&container_name, None).await?;
        
        Ok(ServiceDiagnostics {
            restart_count: inspect.restart_count.unwrap_or(0),
            last_exit_code: inspect.state.and_then(|s| s.exit_code),
            last_error: inspect.state.and_then(|s| s.error),
            oom_killed: inspect.state.and_then(|s| s.oom_killed).unwrap_or(false),
            health_check_failing: false, // Calculated from health status
        })
    }
}
```

---

### 5. Add `garden-rake diagnose` Command

**Syntax:**
```bash
garden-rake diagnose [SERVICE]

# Diagnose specific service
garden-rake diagnose mongodb

# Diagnose all services
garden-rake diagnose
```

**Output:**
```
━━━ DIAGNOSTICS: mongodb ━━━

Status:         Running (restarted 3 times)
Container ID:   abc123def456
Image:          mongo:7
Exit Code:      1 (last restart)
OOM Killed:     No
Health Check:   Passing

Recent Events:
  [16:00:00] Container started
  [16:00:05] Container exited (code 1)
  [16:00:06] Container restarted (attempt 1/3)
  [16:00:11] Container exited (code 1)
  [16:00:12] Container restarted (attempt 2/3)

Last 20 Log Lines:
  2026-01-16 16:00:04 | Starting MongoDB...
  2026-01-16 16:00:05 | Error: Permission denied on /data/db
  2026-01-16 16:00:05 | Exiting...

Possible Issues:
  ⚠ High restart count (3) - check logs for errors
  ⚠ Exit code 1 - application error
  💡 Suggestion: Check volume permissions with 'docker exec'

Quick Actions:
  • View full logs:    garden-rake logs mongodb
  • Remove service:    garden-rake remove mongodb
  • Restart service:   garden-rake rest mongodb && garden-rake wake mongodb
```

---

## Implementation Plan

### Phase 1: Basic Offering Logs Streaming ✅ COMPLETE
1. ✅ Add `get_logs_stream()` method to docker.rs using bollard (follow=true)
2. ✅ Add `GET /api/services/:service/logs` SSE endpoint to moss
3. ✅ Extend `watch` command parser to support `offering <name> logs` syntax
4. ✅ Stream logs to stdout in real-time
5. ✅ Test streaming with MongoDB
6. ✅ Add CPU detection (model, features, architecture) to metrics.rs
7. ✅ Enhance StoneCapabilities with CPU info and memory
8. ✅ Create compatibility types in zen-common
9. ✅ Document compatibility system in COMPAT-0001 ADR

**Commit:** 04fedb3 - feat(diagnostics): add container log streaming and CPU detection

### Phase 2: Compatibility System 🔄 IN PROGRESS
#### 2.1: Template Loader Enhancement ✅ COMPLETE
1. ✅ Add CompatibilityRules field to ServiceTemplate
2. ✅ Implement load_compatibility_from_runtime()
3. ✅ Implement load_compatibility_from_embedded()
4. ✅ Update parse_template() to accept compatibility parameter
5. ✅ Create mongodb.compatibility.yaml with processor patterns

#### 2.2: Pre-Install Evaluation ✅ COMPLETE
1. ✅ Add evaluate_compatibility() function
2. ✅ Integrate compatibility check in install_service_task()
3. ✅ Override image when fallback rule matches
4. ✅ Emit warning events for compatibility fallbacks
5. ⏳ Test with J4105 stone (requires hardware access)

#### 2.3: Post-Install Healthcheck ⏳ TODO
1. ⏳ Implement post-install log pattern detection
2. ⏳ Automatic rollback on pattern match
3. ⏳ Reinstall with fallback image

### Phase 3: Tail Mode ⏳ TODO
1. Add `get_logs_tail()` method to docker.rs (follow=false, last N lines)
2. Add `GET /api/services/:service/logs/tail` endpoint to moss
3. Add `--tail N` flag support in garden-rake
4. Fetch and print logs once, then exit

### Phase 3: Enhanced Formatting (20 min)
1. Add `--timestamps` flag support
2. Add color coding (stdout=white, stderr=red)
3. Handle Ctrl+C gracefully in streaming mode

### Phase 4: Stone-wide Logs (30 min)
1. Add `GET /api/logs` endpoint (all services interleaved)
2. Implement `watch stone <name> logs` syntax
3. Prefix each line with service name
4. Add service-based color coding

### Phase 5: Diagnostics Enhancement (40 min)
1. Add diagnostics fields to ServiceInfo
2. Update health_monitor_task to collect restart counts
3. Enhance observe command output
4. Add color coding for restart counts

**Total Estimated Time:** 2 hours 30 minutes

---

## Testing Strategy

### Test Scenarios

1. **Normal Container:**
   - Logs show startup messages
   - Restart count = 0
   - Diagnose shows healthy status

2. **Crashing Container:**
   - Create intentionally failing container
   - Verify restart count increments
   - Verify logs show error messages
   - Verify diagnose shows exit code

3. **OOM Container:**
   - Create memory-limited container that exceeds limit
   - Verify oom_killed flag is true
   - Verify observe shows OOM indicator

4. **Streaming Logs:**
   - Start service with `--follow`
   - Verify real-time log streaming
   - Verify Ctrl+C exits cleanly

### Test Commands
```bash
# Stream MongoDB logs in real-time via SSE
garden-rake watch offering mongodb logs

# Dump last 100 lines once and exit
garden-rake watch offering mongodb logs --tail 100

# Stream with timestamps
garden-rake watch offering mongodb logs --timestamps

# Stream all services on a stone
garden-rake watch stone stone-amber-terrace logs

# Test observe with failing container
garden-rake observe

# Test diagnose
garden-rake diagnose mongodb
```

---

## API Changes

### Moss Endpoints (Added)

```
GET  /api/services/:service/logs
     Query params: timestamps
     Response: SSE stream with log events (continuous)
     
GET  /api/services/:service/logs/tail
     Query params: lines, timestamps
     Response: JSON with last N lines (one-shot)
     
GET  /api/logs
     Query params: timestamps
     Response: SSE stream with logs from all services (interleaved)
```

### Garden-Rake Commands (Enhanced)

```
watch offering <name> logs [--timestamps]              # Stream via SSE
watch offering <name> logs --tail N [--timestamps]     # Dump once and exit
watch stone <name> logs [--timestamps]                 # Stream all offerings
diagnose [service]
```

### ServiceInfo (Enhanced)

```rust
pub struct ServiceInfo {
    // ... existing ...
    pub diagnostics: Option<ServiceDiagnostics>,  // NEW
}
```

---

## File Changes Required

### Moss (src/linux/moss/src/)
- `docker.rs` - Add get_logs_stream() (follow=true) and get_logs_tail() (follow=false)
- `main.rs` - Add logs SSE endpoint and logs/tail JSON endpoint
- Health monitor - Collect diagnostics

### Garden-Rake (src/windows/garden-rake/src/)
- `main.rs` - Extend watch command parser for `offering <name> logs` and `stone <name> logs`
- SSE client for streaming mode (consistent with existing watch events)
- HTTP GET client for tail mode (one-shot)
- Display formatting with color coding

### Common (src/linux/common/src/)
- `lib.rs` - Add ServiceDiagnostics struct

---

## Priority Actions (Immediate)

1. ✅ **Document the plan** (this file)
2. ✅ **Implement `watch offering <name> logs`** - COMPLETE & TESTED
3. ✅ **Document compatibility system** (COMPAT-0001 ADR)
4. ✅ **Create mongodb.compatibility.yaml**
5. 🔄 **Implement CPU detection + compatibility checking** (IN PROGRESS)
6. 🔄 **Add restart count to observe**
7. 🔄 **Test with failing container**

---

## Implementation Status

### ✅ Phase 1: Basic Offering Logs Streaming (COMPLETE)
- ✅ Added `get_logs_stream()` method to docker.rs using bollard (follow=true)
- ✅ Added `GET /api/services/:service/logs` SSE endpoint to moss
- ✅ Extended `watch` command parser to support `offering <name> logs` syntax
- ✅ Streams logs to stdout in real-time
- ✅ Tested with MongoDB - successfully streams container warnings
- ✅ Color coding (stderr detection ready)
- ✅ 404 handling for non-existent services

**Testing Results:**
```bash
$ garden-rake watch offering mongodb logs
📡 Streaming logs from offering: mongodb

WARNING: MongoDB 5.0+ requires a CPU with AVX support...
(repeating warnings streamed in real-time)
```

### 🔄 Phase 2: Compatibility System (IN PROGRESS)
- ✅ COMPAT-0001 ADR documented
- ✅ mongodb.compatibility.yaml created with J4105 rules
- ✅ CPU detection added to metrics.rs (get_cpu_info)
- ✅ StoneCapabilities extended (cpu_model, cpu_features, architecture)
- ✅ Compatibility types added to zen-common
- ⏳ Template loader enhancement (load .compatibility.yaml files)
- ⏳ Pre-install compatibility check in offer_service()
- ⏳ Post-install healthcheck in health_monitor_task()

**Next Steps:**
1. Update templates.rs to load compatibility rules alongside service configs
2. Add compatibility evaluation logic to offer_service()
3. Test with J4105 stone
4. Implement post-install healthcheck (Phase 3)

---

## Design Decisions (Confirmed)

1. **Streaming mode behavior:** Pure streaming - only new logs from now forward. Historical logs require `--tail N`
2. **Companion containers:** Each service/companion has unique name. User watches them separately (e.g., `watch offering mongodb-exporter logs`)
3. **Container lifecycle:** Keep SSE connection open on container stop/crash. Display disconnection/reconnection messages clearly
4. **Log format:** Prefix with service name: `[mongodb] Starting MongoDB...`
5. **Error handling:** Return 404 for non-existent services. Fail fast
6. **Backward compatibility:** `watch` (no args) and `watch --until` still work unchanged. Adding `offering <name> logs` switches to log observability mode

---

## Success Criteria

- [ ] Can stream container logs with `watch offering <name> logs`
- [ ] Can stream all offering logs on a stone with `watch stone <name> logs`
- [ ] Logs update in real-time as container produces output
- [ ] Can see restart counts in observe output
- [ ] Can diagnose why a container is failing
- [ ] Clear error messages for non-existent offerings
- [ ] Documentation updated with examples

---

## References

- Bollard logs API: https://docs.rs/bollard/latest/bollard/container/struct.Docker.html#method.logs
- Docker logs format: https://docs.docker.com/engine/api/v1.41/#operation/ContainerLogs
- Similar tools: `kubectl logs`, `docker logs`, `docker-compose logs`

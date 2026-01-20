# Moss Console Output & Remote Control Design

**Date:** January 19, 2026  
**Status:** Approved for Implementation

---

## Executive Summary

This design completely reimagines Moss console output and introduces remote console control. Key improvements:

1. **Quiet by default** - Minimal console output respects user attention
2. **Remote control** - `garden-rake make stone sing` toggles stone's local console
3. **Structured events** - Atomic, self-contained log lines that work with streaming
4. **15 event categories** - Complete observability across all Moss subsystems
5. **Platform-aware** - Smart defaults for Windows/Linux, interactive/service modes
6. **Production-ready** - Security, ops, and cluster management visibility

---

## Problem Statement

**Current Issues:**
- Moss daemon produces extremely verbose structured logging output (20+ lines for manifest loading)
- Every manifest load generates 2-3 log lines with full timestamps and module paths
- No differentiation between production operation and debugging scenarios
- Users cannot control output verbosity easily
- Multi-line log sequences break when events are interleaved (async/concurrent operations)
- Lacks the "calm, purposeful" aesthetic of Zen Garden
- No remote control capability for toggling console output
- Limited visibility into service lifecycle, jobs, discovery, security, ops

**Example Current Output (BAD):**
```
2026-01-19T15:18:34.306075Z  INFO garden_moss::manifests: Loaded manifest offering="aspire" source="filesystem"
2026-01-19T15:18:34.306755Z  INFO garden_moss::manifests: Parsed as snippet format service="aspire"
2026-01-19T15:18:34.318665Z  INFO garden_moss::manifests: Loaded manifest offering="couchbase" source="filesystem"
2026-01-19T15:18:34.319168Z  INFO garden_moss::manifests: Parsed as snippet format service="couchbase"
[... 20 more lines ...]
```

---

## Design Decisions

## 1. Console Output Modes

**Four verbosity levels:**

```rust
pub enum ConsoleMode {
    Silent,      // No console output (systemd/Windows service)
    Minimal,     // Startup banner + critical events only (default for daemons)
    Informative, // Major lifecycle events (default for interactive)
    Verbose,     // Full debug output (opt-in)
}
```

**Platform-aware defaults:**
- Windows service → `Silent`
- Windows interactive → `Informative`
- Linux systemd → `Minimal`
- Linux interactive → `Informative`

**Control methods:**
- Environment variable: `MOSS_CONSOLE=minimal|informative|verbose|silent`
- CLI flag: `garden-moss --quiet` or `--verbose`
- Remote command: `garden-rake make stone sing` (toggles to Informative)
- Persistent config: `console_mode = "informative"` in moss-config.toml

## 2. Remote Console Control

**Key insight:** Separate remote console toggle from event streaming.

**Existing command (unchanged):**
```bash
garden-rake listen stone-01    # Stream events TO MY TERMINAL
```

**New commands (remote console control):**
```bash
garden-rake make stone-01 sing           # Toggle stone's console → Informative (30min timeout)
garden-rake make stone-01 sing forever   # Toggle stone's console → Informative (persistent)
garden-rake make stone-01 quiet          # Toggle stone's console → Minimal (clears persistence)
```

**How it works:**
1. Rake sends `POST /api/v1/console/mode { mode: "informative", persist: false }`
2. Moss updates `Arc<RwLock<ConsoleMode>>` (runtime state)
3. If `persist: true`, saves to `moss-config.toml`
4. Stone's console (TTY1, stdout) immediately starts showing events
5. 3. Structured Event Format

**Critical requirement:** Events must be atomic, self-contained lines that work with streaming and concurrent operations.

**Problem with multi-line sequences:**
```
15:23:10 🎯 Installing postgresql-dev...
15:23:12    ↳ Pulling postgres:16-alpine
[OTHER EVENT MAY APPEAR HERE - BREAKS VISUAL SEQUENCE]
15:23:25    ↳ Creating container
15:23:30 ✅ Ready in 20.3s
```

**Solution: Structured single-line format**
```
HH:MM:SS Category  │ STATUS        │ Target → details

15:23:10 Services  │ REQUESTING    │ postgresql-dev → postgres:16-alpine
15:23:12 Services  │ PULLING       │ postgresql-dev (45%)
15:23:25 Services  │ CREATING      │ postgresql-dev → container c4f8a2b1
15:23:27 Services  │ STARTING      │ postgresql-dev → :5432
15:23:30 Services  │ HEALTHY       │ postgresql-dev (3.2s)
15:23:30 Services  │ READY         │ postgresql-dev (total: 20.3s)
```

**Benefits:**
- Each line is complete and meaningful on its own
- Events from different sources can interleave safely
- Easy to filter: `grep "Services.*READY"` for completed installations
- Supports concurrent operations naturally
- Parse-friendly for monitoring tools
- Color-coding works per-line (status-based: green for READY, red for FAILED, etc.)

**Column Layout Rules:**
- **Fixed-width** (for alignment): timestamp (8 chars), category (9 chars left-padded), status (14 chars left-padded)
- **Flexible**: target and details (content determines width)

**Format specification:**
```rust
pub struct ConsoleEvent {
    timestamp: DateTime<Local>,     // HH:MM:SS (8 chars fixed)
    category: EventCategory,         // Fixed 9 chars: "Services ", "Discovery"
    status: EventStatus,             // Fixed 14 chars: "READY         "
    target: String,                  // Service name, stone name, file name (flexible)
    details: Option<String>,         // Additional context (optional, flexible)
}

// Formatted output:
// "15:23:30 Services  │ READY         │ postgresql-dev (20.3s)"
//  ^^^^^^^ ^^^^^^^^^ │ ^^^^^^^^^^^^^ │ ^^^^^^^^^^^^^^^^^^^^^^^^^^^
//  8 chars 9 chars      14 chars        flexible width
```

**Color scheme:** Use terminal colors IF available via crate detection (`atty`, `supports-color`), otherwise plain text:
- Green: success/ready states (READY, COMPLETED, HEALTHY)
- Red: errors/failures (FAILED, STOPPED, INVALID)
- Yellow: warnings/degraded (WARNING, DEGRADED, RETRY)
- Cyan: in-progress operations (REQUESTING, PULLING, STARTING)

// Event types for streaming
enum MossEvent {
    ServiceStarted { name: String, port: u16 },
    ServiceStopped { name: String },
    OfferingPlanted { name: String },
    OfferingRemoved { name: String },
    HealthCheck { endpoint: String, status: HealthStatus },
    ApiRequest { method: String, path: String, status: u16 },
    Warning { message: String },
    Error { message: String },
}
```

**Key architectural decisions:**

1. **Console output** controlled by `--quiet`, `--verbose` flags or `LOG_LEVEL` env
2. **Structured logs** always written to file/stdout for journald/docker
3. **Event stream** exposed at `/api/v1/events/stream` (SSE endpoint)
4. Events are **broadcast** to all connected clients (tokio broadcast channel)
5. Rake subscribes to events and formats them for terminal display

## 4. Event Categories

**15 comprehensive categories covering all Moss operations:**

### Core Operations (8 categories)

**1. System** - Moss daemon lifecycle
```
System    │ STARTING      │ stone-turquoise-glacier
System    │ INITIALIZING  │ Docker client
System    │ CONNECTED     │ Docker daemon (version 24.0.7)
System    │ READY         │ HTTP server → http://192.168.1.171:7185
System    │ CONSOLE_MODE  │ Changed → informative (remote request)
System    │ SHUTTING_DOWN │ Graceful shutdown requested
System    │ SHUTDOWN      │ All services stopped
System    │ ERROR         │ Docker daemon unreachable
```

**2. Config** - Configuration loading and validation
```
Config    │ READING       │ moss-config.toml → /etc/moss/config.toml
Config    │ PARSED        │ moss-config.toml (3 settings)
Config    │ MERGED        │ CLI + Env + File → final configuration
Config    │ INVALID       │ moss-config.toml → port must be 1024-65535
Config    │ MISSING       │ /etc/moss/config.toml (using defaults)
```

**3. Manifests** - Service manifest operations
```
Manifests │ SCANNING      │ /var/lib/moss/manifests
Manifests │ FOUND         │ 13 manifest files
Manifests │ READING       │ postgresql/manifest.yaml
Manifests │ PARSED        │ postgresql → snippet format
Manifests │ VALIDATED     │ postgresql → schema valid
Manifests │ COMPAT_RULES  │ postgresql → 5 rules loaded
Manifests │ REFRESH       │ Checking for updates
Manifests │ UPDATED       │ mongodb → v7.0 → v7.1
Manifests │ INVALID       │ custom-app → missing required field 'image'
```

**4. Offerings** - Catalog management
```
Offerings │ SCANNING      │ /var/lib/moss/manifests (13 found)
Offerings │ LOADING       │ postgresql → filesystem
Offerings │ CACHED        │ Catalog → 13 offerings ready
Offerings │ REFRESH       │ Catalog → checking for updates
Offerings │ UPDATED       │ mongodb → new version available
```

**5. Services** - Container runtime operations
```
Services  │ REQUESTING    │ postgresql-dev → postgres:16-alpine
Services  │ PULLING       │ postgresql-dev → postgres:16-alpine (45%)
Services  │ CREATING      │ postgresql-dev → container c4f8a2b1
Services  │ STARTING      │ postgresql-dev → :5432
Services  │ RUNNING       │ postgresql-dev (PID 12345)
Services  │ HEALTHY       │ postgresql-dev (took 3.2s)
Services  │ READY         │ postgresql-dev (total: 20.3s)
Services  │ DEGRADED      │ postgresql-dev → slow response (2.5s)
Services  │ STOPPING      │ postgresql-dev (user request)
Services  │ STOPPED       │ postgresql-dev (exit code 0)
Services  │ REMOVING      │ postgresql-dev
Services  │ REMOVED       │ postgresql-dev
```

**6. Jobs** - Background task execution
```
Jobs      │ QUEUED        │ install-postgresql (#abc123)
Jobs      │ STARTED       │ install-postgresql (#abc123)
Jobs      │ PROGRESS      │ install-postgresql → pulling image (65%)
Jobs      │ PROGRESS      │ install-postgresql → creating container
Jobs      │ PROGRESS      │ install-postgresql → starting service
Jobs      │ COMPLETED     │ install-postgresql (20.3s)
Jobs      │ FAILED        │ install-mongodb → port 27017 in use
Jobs      │ CANCELLED     │ heal-garden (user request)
Jobs      │ RETRY         │ install-redis → attempt 2/3
```

**7. Storage** - Volume and data management
```
Storage   │ CREATING      │ postgresql-data → volume (20GB limit)
Storage   │ MOUNTING      │ postgresql-data → /var/lib/postgresql/data
Storage   │ BACKED_UP     │ postgresql-data → backup-2026-01-19.tar.gz
Storage   │ RESTORED      │ postgresql-data ← backup-2026-01-19.tar.gz
Storage   │ PRUNING       │ Unused volumes (recovered 2.3GB)
Storage   │ WARNING       │ postgresql-data → 85% full (17GB/20GB)
```

**8. Network** - Networking and port management
```
Network   │ CREATING      │ moss-network → bridge mode
Network   │ CONNECTED     │ postgresql-dev → moss-network (172.18.0.2)
Network   │ PORT_BINDING  │ postgresql-dev → 0.0.0.0:5432 → 5432
Network   │ EXPOSING      │ postgresql-dev → :5432 (TCP)
Network   │ DISCONNECTED  │ redis-cache ← moss-network
```

### Infrastructure (4 categories)

**9. Docker** - Docker daemon integration
```
Docker    │ CONNECTED     │ Docker daemon (version 24.0.7)
Docker    │ PING          │ Daemon responding (5ms)
Docker    │ IMAGE_PULL    │ postgres:16-alpine (45%, 85MB/192MB)
Docker    │ IMAGE_CACHED  │ redis:7-alpine (already present)
Docker    │ IMAGE_PRUNE   │ Removed 5 unused images (1.2GB recovered)
Docker    │ CONTAINER_UP  │ c4f8a2b1 → postgresql-dev
Docker    │ CONTAINER_DOWN│ c4f8a2b1 → postgresql-dev (exit 0)
Docker    │ NETWORK_OK    │ moss-network (bridge, 172.18.0.0/16)
Docker    │ VOLUME_OK     │ postgres-data (20GB, 15% used)
Docker    │ DISCONNECTED  │ Daemon unreachable
Docker    │ RECONNECTING  │ Attempt 2/5 (10s delay)
Docker    │ ERROR         │ API error 500 → internal server error
```

**10. Discovery** - Peer discovery and Lantern
```
Discovery │ LISTENING     │ UDP port 7184
Discovery │ ANNOUNCING    │ stone-turquoise-glacier → 192.168.1.171:7185
Discovery │ PEER_FOUND    │ stone-azure-mountain → 192.168.1.101:7185
Discovery │ PEER_UPDATED  │ stone-azure-mountain → offerings changed (5→7)
Discovery │ PEER_LOST     │ stone-azure-mountain (timeout after 30s)
Discovery │ LANTERN_REG   │ http://192.168.1.1:7186 (registered)
Discovery │ LANTERN_LOST  │ http://192.168.1.1:7186 (connection refused)
```

**11. Health** - Monitoring and health checks
```
Health    │ CHECK_PASS    │ postgresql-dev → responding (15ms)
Health    │ CHECK_SLOW    │ mongodb-test → responding (2.8s, threshold 1s)
Health    │ CHECK_FAIL    │ redis-cache → connection refused
Health    │ RECOVERING    │ postgresql-dev → restart attempt 1/3
Health    │ RECOVERED     │ postgresql-dev → healthy after restart
Health    │ WARNING       │ redis-cache → memory 85% (850MB/1GB)
Health    │ CRITICAL      │ System → disk space 95% full
```

**12. API** - HTTP request handling
```
API       │ GET           │ /api/v1/services → 200 OK (45ms)
API       │ POST          │ /api/v1/offerings → 201 Created (450ms)
API       │ DELETE        │ /api/v1/services/redis → 204 No Content (120ms)
API       │ PUT           │ /api/v1/services/postgres/config → 200 OK (80ms)
API       │ ERROR         │ POST /api/v1/offerings → 409 Conflict (service exists)
API       │ TIMEOUT       │ GET /api/v1/health → 504 Gateway Timeout (30s)
```

### Production/Enterprise (3 categories)

**13. Security** - Authentication, keys, certificates
```
Security  │ KEYSTONE_GEN  │ stone-turquoise-glacier → RSA 4096-bit
Security  │ KEYSTONE_LOAD │ /etc/moss/keystone.pem (valid until 2027-01-19)
Security  │ KEYSTONE_EXP  │ Expiring in 30 days
Security  │ AUTH_ENABLE   │ JWT validation active
Security  │ AUTH_DISABLE  │ Authentication disabled (dev mode)
Security  │ AUTH_SUCCESS  │ user@desktop-01 → admin role
Security  │ AUTH_DENIED   │ user@host → invalid credentials
Security  │ AUTH_EXPIRED  │ (Sanitized - see Security section)
Security  │ RATE_LIMITED  │ (Sanitized IP)
Security  │ STONE_TRUST   │ stone-azure-mountain → keystone validated
Security  │ STONE_REJECT  │ stone-unknown → invalid signature
Security  │ TLS_ENABLED   │ HTTPS active on :7443
```

**14. Ops** - Operational state management
```
Ops       │ ACTIVE        │ stone-X → accepting workloads
Ops       │ CORDON        │ stone-X → no new workloads (reason: maintenance)
Ops       │ DRAIN_START   │ stone-X → migrating 5 services
Ops       │ DRAIN_DONE    │ stone-X → all services migrated (45s)
Ops       │ UNCORDON      │ stone-X → accepting workloads again
Ops       │ RETIRE_SCHED  │ stone-X → decommission at 2026-01-20 02:00
Ops       │ RETIRE_START  │ stone-X → beginning retirement process
Ops       │ RETIRE_DONE   │ stone-X → removed from cluster
Ops       │ STONE_JOIN    │ stone-Y → added to cluster
Ops       │ STONE_LEAVE   │ stone-Y → removed from cluster
Ops       │ MAINTENANCE   │ stone-X → entering maintenance mode
Ops       │ MAINT_EXIT    │ stone-X → exiting maintenance mode
Ops       │ BACKUP_START  │ Cluster backup initiated
Ops       │ BACKUP_DONE   │ Cluster backup → backup-2026-01-19.tar.gz (2.3GB)
Ops       │ RESTORE_START │ Restoring from backup-2026-01-18.tar.gz
Ops       │ RESTORE_DONE  │ Restore completed → 15 services recovered
```

**15. Cluster** - Multi-stone coordination
```
Cluster   │ FORMING       │ 3 stones detected
Cluster   │ TOPOLOGY      │ Updated → 5 stones, 23 services
Cluster   │ ELECTION      │ Lantern leader → stone-azure-mountain
Cluster   │ REBALANCE     │ Started → moving 3 services
Cluster   │ REBALANCED    │ Completed → workload distributed
Cluster   │ SPLIT_BRAIN   │ Detected → 2 Lantern leaders
Cluster   │ HEALED        │ Split resolved → single leader
Cluster   │ QUORUM_LOST   │ Only 1/3 stones reachable
Cluster   │ QUORUM_OK     │ 3/3 stones reachable
```

---

## 5. Output by Console Mode

### Minimal Mode (daemon default)
Shows only critical lifecycle events:
```
15:18:33 System    │ STARTING      │ stone-turquoise-glacier
15:18:34 System    │ READY         │ HTTP server → http://192.168.1.171:7185
15:23:10 Services  │ REQUESTING    │ postgresql-dev
15:23:30 Services  │ READY         │ postgresql-dev (20.3s)
15:24:01 Services  │ STOPPED       │ postgresql-dev
```

### Informative Mode (interactive default)
Shows major operations and state changes:
```
15:18:33 System    │ STARTING      │ stone-turquoise-glacier
15:18:33 System    │ CONNECTED     │ Docker daemon
15:18:33 Discovery │ LISTENING     │ UDP port 7184
15:18:33 Manifests │ FOUND         │ 13 manifest files
15:18:34 System    │ READY         │ HTTP server → http://192.168.1.171:7185
15:23:10 Services  │ REQUESTING    │ postgresql-dev → postgres:16-alpine
15:23:12 Services  │ PULLING       │ postgresql-dev (45%)
15:23:25 Services  │ CREATING      │ postgresql-dev
15:23:27 Services  │ STARTING      │ postgresql-dev → :5432
15:23:30 Services  │ HEALTHY       │ postgresql-dev (3.2s)
15:23:30 Services  │ READY         │ postgresql-dev (20.3s)
15:24:01 Services  │ STOPPING      │ postgresql-dev (user request)
15:24:01 Services  │ STOPPED       │ postgresql-dev (0.8s)
```

### Verbose Mode (debug)
Shows everything including API requests, progress details, IDs:
```
15:18:33 System    │ STARTING      │ stone-turquoise-glacier
15:18:33 Config    │ READING       │ moss-config.toml → /etc/moss/config.toml
15:18:33 Config    │ MERGED        │ CLI + Env + File → final configuration
15:18:33 Docker    │ CONNECTED     │ Docker daemon (version 24.0.7)
15:18:33 Discovery │ LISTENING     │ UDP port 7184
15:18:33 Manifests │ SCANNING      │ /var/lib/moss/manifests
15:18:33 Manifests │ FOUND         │ 13 manifest files
15:18:33 Manifests │ READING       │ postgresql/manifest.yaml
15:18:33 Manifests │ PARSED        │ postgresql → snippet format
[... all manifests ...]
15:18:34 System    │ READY         │ HTTP server → http://192.168.1.171:7185
15:23:10 API       │ POST          │ /api/v1/offerings → 202 Accepted (12ms)
15:23:10 Jobs      │ QUEUED        │ install-postgresql (#abc123)
15:23:10 Jobs      │ STARTED       │ install-postgresql (#abc123)
15:23:10 Services  │ REQUESTING    │ postgresql-dev → postgres:16-alpine (job #abc123)
15:23:12 Docker    │ IMAGE_PULL    │ postgres:16-alpine (layer 1/5, 45%, 85MB/192MB)
15:23:25 Services  │ CREATING      │ postgresql-dev → container c4f8a2b1
15:23:25 Storage   │ CREATING      │ postgres-data → volume (20GB limit)
15:23:27 Network   │ PORT_BINDING  │ postgresql-dev → 0.0.0.0:5432 → 5432
15:23:27 Services  │ STARTING      │ postgresql-dev → :5432
15:23:30 Health    │ CHECK_PASS    │ postgresql-dev → responding (15ms)
15:23:30 Services  │ HEALTHY       │ postgresql-dev (3.2s)
15:23:30 Services  │ READY         │ postgresql-dev (total: 20.3s, mem: 45MB, cpu: 2%)
15:23:30 Jobs      │ COMPLETED     │ install-postgresql (20.3s)
15:23:30 API       │ RESPONSE      │ POST /api/v1/offerings → 201 Created (20.3s total)
```

---

## 6. Concurrent Operations Example

Multiple operations happening simultaneously - each line is independent:

```
15:23:10 Services  │ REQUESTING    │ postgresql-dev → postgres:16-alpine
15:23:11 Services  │ REQUESTING    │ redis-cache → redis:7-alpine
15:23:11 Services  │ REQUESTING    │ mongodb-test → mongo:7-alpine
15:23:12 Services  │ PULLING       │ postgresql-dev (15%)
15:23:12 Services  │ PULLING       │ redis-cache (30%)
15:23:13 API       │ GET           │ /api/v1/services → 200 OK (45ms)
15:23:13 Services  │ PULLING       │ mongodb-test (10%)
15:23:14 Services  │ PULLING       │ postgresql-dev (45%)
15:23:15 Services  │ PULLING       │ redis-cache (80%)
15:23:15 Discovery │ PEER_FOUND    │ stone-azure-mountain → 192.168.1.101:7185
15:23:16 Services  │ CREATING      │ redis-cache → container 89f2c1d3
15:23:16 Services  │ PULLING       │ postgresql-dev (75%)
15:23:17 Services  │ STARTING      │ redis-cache → :6379
15:23:18 Services  │ HEALTHY       │ redis-cache (2.1s)
15:23:18 Services  │ READY         │ redis-cache
15:23:20 Services  │ CREATING      │ postgresql-dev → container c4f8a2b1
15:23:20 Services  │ PULLING       │ mongodb-test (65%)
15:23:21 Services  │ STARTING      │ postgresql-dev → :5432
15:23:25 Services  │ HEALTHY       │ postgresql-dev (4.3s)
15:23:25 Services  │ READY         │ postgresql-dev
15:23:28 Services  │ CREATING      │ mongodb-test → container 7a3f9b2e
15:23:30 Services  │ STARTING      │ mongodb-test → :27017
15:23:35 Services  │ HEALTHY       │ mongodb-test (5.2s)
15:23:35 Services  │ READY         │ mongodb-test
```

Each service's lifecycle is trackable via grep: `grep postgresql-dev` shows only that service's events.

---

## 7. Implementation Architecture

### Core Components

**1. Event System**
```rust
#[derive(Debug, Clone, Serialize, Deserialize)]
pub struct ConsoleEvent {
    pub timestamp: DateTime<Local>,
    pub category: EventCategory,
    pub status: EventStatus,
    pub target: String,
    pub details: Option<String>,
}

#[derive(Debug, Clone, Copy, Serialize, Deserialize)]
pub enum EventCategory {
    System, Config, Manifests, Offerings, Services, Jobs,
    Storage, Network, Docker, Discovery, Health, Api,
    Security, Ops, Cluster,
}

#[derive(Debug, Clone, Copy, Serialize, Deserialize)]
pub enum EventStatus {
    // Lifecycle
    Starting, Ready, Stopping, Stopped, Removing, Removed,
    
    // Service states  
    Requesting, Pulling, Creating, Running, Healthy, Degraded,
    
    // Job states
    Queued, Started, Progress, Completed, Failed, Cancelled, Retry,
    
    // Operations
    Reading, Parsed, Validated, Merged, Invalid, Missing,
    Scanning, Found, Loading, Cached, Updated,
    Connected, Disconnected, Reconnecting,
    
    // Network/Storage
    Mounting, PortBinding, Exposing,
    
    // Health
    CheckPass, CheckSlow, CheckFail, Recovering, Recovered, Warning, Critical,
    
    // Discovery
    Listening, Announcing, PeerFound, PeerUpdated, PeerLost,
    LanternReg, LanternLost,
    
    // API
    Get, Post, Put, Delete, Error, Timeout,
    
    // Security
    KeystoneGen, KeystoneLoad, KeystoneExp,
    AuthEnable, AuthDisable, AuthSuccess, AuthDenied, AuthExpired, RateLimited,
    StoneTrust, StoneReject, TlsEnabled,
    
    // Ops
    Active, Cordon, DrainStart, DrainDone, Uncordon,
    RetireSched, RetireStart, RetireDone,
    StoneJoin, StoneLeave, Maintenance, MaintExit,
    BackupStart, BackupDone, RestoreStart, RestoreDone,
    
    // Cluster
    Forming, Topology, Election, Rebalance, Rebalanced,
    SplitBrain, Healed, QuorumLost, QuorumOk,
}

impl ConsoleEvent {
    pub fn format(&self, mode: ConsoleMode, supports_color: bool) -> String {
        let timestamp = self.timestamp.format("%H:%M:%S");
        let category = format!("{:<10}", self.category.display());
        let status = format!("{:<14}", self.status.display());
        
        let line = if let Some(details) = &self.details {
            format!("{} {} │ {} │ {} → {}", 
                timestamp, category, status, self.target, details)
        } else {
            format!("{} {} │ {} │ {}", 
                timestamp, category, status, self.target)
        };
        
        if supports_color {
            self.colorize(line)
        } else {
            line
        }
    }
    
    fn colorize(&self, line: String) -> String {
        use colored::Colorize;
        match self.status {
            EventStatus::Ready | EventStatus::Completed | EventStatus::Healthy => 
                line.green(),
            EventStatus::Failed | EventStatus::Error | EventStatus::Critical => 
                line.red(),
            EventStatus::Warning | EventStatus::Degraded | EventStatus::CheckSlow => 
                line.yellow(),
            EventStatus::Progress | EventStatus::Pulling | EventStatus::Starting => 
                line.cyan(),
            _ => line.normal(),
        }.to_string()
    }
}
```

**2. Console Printer**
```rust
pub struct ConsolePrinter {
    mode: Arc<RwLock<ConsoleMode>>,
    supports_unicode: bool,
    supports_color: bool,
    broadcaster: Option<EventBroadcaster>,
}

impl ConsolePrinter {
    pub fn new(
        mode: Arc<RwLock<ConsoleMode>>,
        broadcaster: Option<EventBroadcaster>,
    ) -> Self {
        Self {
            mode,
            supports_unicode: !matches!(std::env::var("NO_COLOR"), Ok(_)),
            supports_color: atty::is(atty::Stream::Stdout),
            broadcaster,
        }
    }
    
    pub fn event(&self, event: ConsoleEvent) {
        let mode = *self.mode.read().unwrap();
        
        // Broadcast to SSE subscribers
        if let Some(ref broadcaster) = self.broadcaster {
            let _ = broadcaster.send(event.clone());
        }
        
        // Print to console based on mode
        if self.should_print(mode, &event) {
            println!("{}", event.format(mode, self.supports_color));
        }
    }
    
    fn should_print(&self, mode: ConsoleMode, event: &ConsoleEvent) -> bool {
        match mode {
            ConsoleMode::Silent => false,
            
            ConsoleMode::Minimal => matches!(
                (event.category, event.status),
                (EventCategory::System, EventStatus::Starting | EventStatus::Ready) |
                (EventCategory::Services, EventStatus::Requesting | EventStatus::Ready | EventStatus::Stopped) |
                (_, EventStatus::Failed | EventStatus::Error | EventStatus::Critical)
            ),
            
            ConsoleMode::Informative => {
                // Exclude verbose events: API requests, progress details, Docker internals
                !matches!(
                    event.category,
                    EventCategory::Api | EventCategory::Config
                ) && !matches!(
                    event.status,
                    EventStatus::Reading | EventStatus::Scanning
                )
            },
            
            ConsoleMode::Verbose => true,
        }
    }
}
```

**3. Remote Console Control API**
```rust
#[derive(Deserialize)]
struct SetConsoleModeRequest {
    mode: ConsoleMode,
    #[serde(default)]
    persist: bool,
}

async fn set_console_mode(
    State(state): State<Arc<AppState>>,
    Json(req): Json<SetConsoleModeRequest>,
) -> Result<StatusCode, (StatusCode, String)> {
    // Future: Auth check when Pond (auth system) is integrated
    // For now, console control is open (homelab/dev use case)
    // TODO: When Pond present, check user has admin role
    
    // Update runtime mode
    *state.console_mode.write().unwrap() = req.mode;
    
    // Persist to config if requested
    if req.persist {
        if req.mode == ConsoleMode::Minimal {
            // "Quiet" clears persistence
            state.config.console_mode = None;
        } else {
            state.config.console_mode = Some(req.mode);
        }
        state.config.save().ok();
    }
    
    // Auto-timeout for temporary mode changes
    if !req.persist && !matches!(req.mode, ConsoleMode::Minimal) {
        let console_mode = state.console_mode.clone();
        tokio::spawn(async move {
            tokio::time::sleep(Duration::from_secs(1800)).await; // 30 min
            *console_mode.write().unwrap() = ConsoleMode::Minimal;
        });
    }
    
    // Emit event
    state.console.event(ConsoleEvent {
        timestamp: Local::now(),
        category: EventCategory::System,
        status: EventStatus::ConsoleModeChanged,
        target: format!("{:?}", req.mode),
        details: Some(if req.persist { "persistent" } else { "temporary" }.into()),
    });
    
    Ok(StatusCode::OK)
}

// Route
.route("/api/v1/console/mode", post(set_console_mode))
```

**4. Platform-Aware Startup**
```rust
impl ConsoleMode {
    pub fn default_for_platform() -> Self {
        #[cfg(windows)]
        {
            if Self::is_windows_service() {
                ConsoleMode::Silent
            } else {
                ConsoleMode::Informative
            }
        }
        
        #[cfg(not(windows))]
        {
            if Self::is_systemd_service() {
                ConsoleMode::Minimal
            } else {
                ConsoleMode::Informative
            }
        }
    }
    
    #[cfg(windows)]
    fn is_windows_service() -> bool {
        !std::io::stdin().is_terminal()
    }
    
    #[cfg(not(windows))]
    fn is_systemd_service() -> bool {
        std::env::var("INVOCATION_ID").is_ok()
    }
}

// On startup
async fn main() -> Result<()> {
    // Load config
    let mut config = MossConfig::load()?;
    
    // Determine console mode
    let console_mode = std::env::var("MOSS_CONSOLE")
        .ok()
        .and_then(|s| s.parse().ok())
        .or(config.console_mode)
        .unwrap_or_else(ConsoleMode::default_for_platform);
    
    let console_mode = Arc::new(RwLock::new(console_mode));
    
    // Create event broadcaster for SSE
    let (event_tx, _event_rx) = broadcast::channel(100);
    
    // Create console printer
    let console = ConsolePrinter::new(console_mode.clone(), Some(event_tx.clone()));
    
    // Emit startup event
    console.event(ConsoleEvent {
        timestamp: Local::now(),
        category: EventCategory::System,
        status: EventStatus::Starting,
        target: stone_name.clone(),
        details: None,
    });
    
    // ... rest of startup
    
    Ok(())
}
```

---

## 8. Instrumentation Strategy

Each Moss subsystem emits events at key points:

### System
```rust
// main.rs
console.event(ConsoleEvent { category: System, status: Starting, target: stone_name });
// ... initialization
console.event(ConsoleEvent { category: System, status: Ready, target: format!("http://{}:{}", ip, port) });
```

### Services (highest value - users actively watch)
```rust
// When job starts installing
console.event(ConsoleEvent {
    category: Services,
    status: Requesting,
    target: service_name.clone(),
    details: Some(image_name.clone()),
});

// During image pull
docker.pull_image(&image, |progress| {
    console.event(ConsoleEvent {
        category: Services,
        status: Pulling,
        target: service_name.clone(),
        details: Some(format!("{}%", progress.percent)),
    });
});

// Container created
console.event(ConsoleEvent {
    category: Services,
    status: Creating,
    target: service_name.clone(),
    details: Some(format!("container {}", &container_id[..8])),
});

// Service starting
console.event(ConsoleEvent {
    category: Services,
    status: Starting,
    target: service_name.clone(),
    details: Some(format!(":{}",port)),
});

// Health check passed
console.event(ConsoleEvent {
    category: Services,
    status: Healthy,
    target: service_name.clone(),
    details: Some(format!("({:.1}s)", duration.as_secs_f32())),
});

// Ready
console.event(ConsoleEvent {
    category: Services,
    status: Ready,
    target: service_name.clone(),
    details: Some(format!("({:.1}s)", total_duration.as_secs_f32())),
});
```

### Jobs (second highest value)
```rust
// Job queued
console.event(ConsoleEvent {
    category: Jobs,
    status: Queued,
    target: format!("install-{}", service_name),
    details: Some(format!("#{}", &job_id[..8])),
});

// Job started
console.event(ConsoleEvent {
    category: Jobs,
    status: Started,
    target: format!("install-{}", service_name),
    details: Some(format!("#{}", &job_id[..8])),
});

// Progress updates
console.event(ConsoleEvent {
    category: Jobs,
    status: Progress,
    target: format!("install-{}", service_name),
    details: Some("pulling image (65%)".into()),
});

// Job completed
console.event(ConsoleEvent {
    category: Jobs,
    status: Completed,
    target: format!("install-{}", service_name),
    details: Some(format!("({:.1}s)", duration.as_secs_f32())),
});
```

### Discovery
```rust
// Peer found
console.event(ConsoleEvent {
    category: Discovery,
    status: PeerFound,
    target: peer_name.clone(),
    details: Some(endpoint.clone()),
});

// Lantern registered
console.event(ConsoleEvent {
    category: Discovery,
    status: LanternReg,
    target: lantern_endpoint.clone(),
    details: Some("registered".into()),
});
```

### Health
```rust
// Health check
if response.is_ok() {
    console.event(ConsoleEvent {
        category: Health,
        status: CheckPass,
        target: service_name.clone(),
        details: Some(format!("({}ms)", duration.as_millis())),
    });
} else {
    console.event(ConsoleEvent {
        category: Health,
        status: CheckFail,
        target: service_name.clone(),
        details: Some(error.to_string()),
    });
}
```

### API (verbose only)
```rust
// Middleware
async fn log_api_request(req: Request, next: Next) -> Response {
    let start = Instant::now();
    let method = req.method().clone();
    let path = req.uri().path().to_string();
    
    let response = next.run(req).await;
    let status = response.status().as_u16();
    let duration_ms = start.elapsed().as_millis();
    
    console.event(ConsoleEvent {
        category: Api,
        status: EventStatus::from_http_method(&method),
        target: path,
        details: Some(format!("→ {} ({} ms)", status, duration_ms)),
    });
    
    response
}
```

### Security
```rust
// Auth success
console.event(ConsoleEvent {
    category: Security,
    status: AuthSuccess,
    target: format!("{}@{}", username, host),
    details: Some(format!("→ {} role", role)),
});

// Auth denied
console.event(ConsoleEvent {
    category: Security,
    status: AuthDenied,
    target: format!("{}@{}", username, host),
    details: Some("invalid credentials".into()),
});

// Rate limited
console.event(ConsoleEvent {
    category: Security,
    status: RateLimited,
    target: client_ip.clone(),
    details: Some(format!("blocked ({} attempts/min)", attempts)),
});
```

### Ops
```rust
// Cordon stone
console.event(ConsoleEvent {
    category: Ops,
    status: Cordon,
    target: stone_name.clone(),
    details: Some(format!("reason: {}", reason)),
});

// Drain complete
console.event(ConsoleEvent {
    category: Ops,
    status: DrainDone,
    target: stone_name.clone(),
    details: Some(format!("all {} services migrated ({:.1}s)", count, duration.as_secs_f32())),
});
```

---

## 9. Security & Sanitization

### Security Event Sanitization (CRITICAL)

**ALWAYS sanitize security events in ALL console modes** (including Verbose). Never show usernames, IPs, or hosts that aid reconnaissance:

```rust
fn format_security_event(event: &ConsoleEvent) -> String {
    match event.status {
        EventStatus::AuthSuccess | EventStatus::AuthDenied | EventStatus::AuthExpired => {
            // NEVER show username, IP, or host in ANY mode
            format!("{}  Security  │ {:14} │ (Credentials sanitized)", 
                    event.timestamp.format("%H:%M:%S"),
                    format!("{:?}", event.status).to_uppercase())
        }
        EventStatus::RateLimited => {
            // Sanitize IP even in verbose mode
            format!("{}  Security  │ RATE_LIMITED   │ (IP sanitized)",
                    event.timestamp.format("%H:%M:%S"))
        }
        _ => format_event(event), // Standard formatting
    }
}
```

### General Data Sanitization

**Data to NEVER log:**
- ❌ Password values
- ❌ API tokens/keys
- ❌ **Usernames, IPs, hostnames in auth events (applies to ALL modes)**
- ❌ Environment variable values (may contain secrets)
- ❌ Full API request bodies
- ❌ Database connection strings with credentials
- ❌ Certificate private keys

**Data to sanitize:**
- ⚠️ Container IDs → first 8 chars only
- ⚠️ Volume mount paths → sanitize in production
- ⚠️ Internal IP addresses → redact in secure environments
- ⚠️ Image names → verify no embedded credentials

**Safe to log:**
- ✅ Service names
- ✅ Image names (postgres:16-alpine)
- ✅ Port numbers
- ✅ HTTP status codes
- ✅ Durations and timestamps
- ✅ Health check results
- ✅ Public endpoints

### Console Control Authorization (Future: Pond Integration)

**Note:** Console control is currently open (no auth). When Pond (auth system) is integrated, add admin role check:

```rust
async fn set_console_mode(...) -> Result<StatusCode, (StatusCode, String)> {
    // TODO: When Pond integrated, check admin role
    // if pond_present() && !user.has_role("admin") {
    //     return Err((StatusCode::FORBIDDEN, "Console control requires admin".into()));
    // }
    
    // Update mode...
}
```

**Design decision:** Auth gate only applies when Pond is present. Not a current implementation concern.

---

## 10. Rake Integration

**Commands:**
```bash
# Remote console control
garden-rake make stone-01 sing           # Temporary verbose (30min)
garden-rake make stone-01 sing forever   # Persistent verbose
garden-rake make stone-01 quiet          # Back to default (clears persistence)

# Event streaming (existing, unchanged)
garden-rake listen stone-01              # Stream events to local terminal
```

**Implementation:**
```rust
// src/rake/src/main.rs

#[derive(Parser)]
enum MakeAction {
    /// Make the stone sing (toggle verbose console output)
    Sing {
        /// Persist console mode across restarts
        #[arg(long)]
        forever: bool,
    },
    
    /// Make the stone quiet (restore default console output)
    Quiet,
}

async fn handle_make_command(
    stone: &str,
    action: MakeAction,
    client: &Client,
) -> Result<()> {
    let endpoint = resolve_stone_endpoint(stone, client).await?;
    
    match action {
        MakeAction::Sing { forever } => {
            client.post(&format!("{}/api/v1/console/mode", endpoint))
                .json(&json!({
                    "mode": "informative",
                    "persist": forever,
                }))
                .send()
                .await?;
            
            if forever {
                println!("🎵 {} is now singing (persistent)", stone);
            } else {
                println!("🎵 {} is now singing (30 min timeout)", stone);
            }
        },
        
        MakeAction::Quiet => {
            client.post(&format!("{}/api/v1/console/mode", endpoint))
                .json(&json!({
                    "mode": "minimal",
                    "persist": true,  // Clears any persistence
                }))
                .send()
                .await?;
            
            println!("🤫 {} is now quiet", stone);
        },
    }
    
    Ok(())
}
```

---

## 11. Configuration

**moss-config.toml:**
```toml
# Console output mode (minimal, informative, verbose, silent)
# Set with: garden-rake make <stone> sing forever
console_mode = "informative"

# Other config...
stone_name = "stone-turquoise-glacier"
port = 7185
```

**Environment variables:**
```bash
MOSS_CONSOLE=minimal     # Override console mode
NO_COLOR=1               # Disable color output
```

**CLI flags:**
```bash
garden-moss --quiet      # Silent mode
garden-moss --verbose    # Verbose mode
```

**Priority (highest to lowest):**
1. CLI flags (`--quiet`, `--verbose`)
2. Environment variable (`MOSS_CONSOLE`)
3. Config file (`console_mode`)
4. Platform default (Windows service = Silent, interactive = Informative, etc.)

---

## 12. Testing Strategy

### Unit Tests
```rust
#[test]
fn test_event_formatting_minimal_mode() {
    let event = ConsoleEvent {
        timestamp: Local::now(),
        category: EventCategory::Services,
        status: EventStatus::Ready,
        target: "postgres".into(),
        details: Some("20.3s".into()),
    };
    
    let formatted = event.format(ConsoleMode::Minimal, false);
    assert!(formatted.contains("Services"));
    assert!(formatted.contains("READY"));
    assert!(formatted.contains("postgres"));
}

#[test]
fn test_should_print_filters_by_mode() {
    let printer = ConsolePrinter::new(Arc::new(RwLock::new(ConsoleMode::Minimal)), None);
    
    // Minimal: should show critical events only
    let critical = ConsoleEvent { /* ... */ status: EventStatus::Failed };
    assert!(printer.should_print(ConsoleMode::Minimal, &critical));
    
    let verbose = ConsoleEvent { /* ... */ status: EventStatus::Progress };
    assert!(!printer.should_print(ConsoleMode::Minimal, &verbose));
}
```

### Integration Tests
```rust
#[tokio::test]
async fn test_remote_console_toggle() {
    let app = create_test_app().await;
    
    // Set to informative
    let response = app.post("/api/v1/console/mode")
        .json(&json!({ "mode": "informative", "persist": false }))
        .send()
        .await?;
    
    assert_eq!(response.status(), 200);
    
    // Verify mode changed
    let mode = app.state.console_mode.read().unwrap();
    assert!(matches!(*mode, ConsoleMode::Informative));
}

#[tokio::test]
async fn test_concurrent_service_installs() {
    let app = create_test_app().await;
    
    // Start 3 services concurrently
    let handles = vec![
        tokio::spawn(install_service("postgres")),
        tokio::spawn(install_service("redis")),
        tokio::spawn(install_service("mongo")),
    ];
    
    // Collect events
    let events = collect_console_events(&app).await;
    
    // Verify each service has complete lifecycle
    for service in &["postgres", "redis", "mongo"] {
        assert!(events.iter().any(|e| 
            e.target == *service && matches!(e.status, EventStatus::Requesting)
        ));
        assert!(events.iter().any(|e| 
            e.target == *service && matches!(e.status, EventStatus::Ready)
        ));
    }
}
```

### Manual Testing
1. Start Moss in minimal mode → verify clean startup (3-4 lines)
2. Run `garden-rake make stone sing` → verify mode toggles
3. Plant service → watch structured events
4. Run concurrent operations → verify events don't break visual sequence
5. Test on Windows → verify service detection and appropriate defaults
6. Test persistence → restart Moss, verify mode restored

---

## 13. Implementation Phases

**Revised Estimate:** 20-30 hours (realistic, accounts for testing and edge cases)

### Phase 1: Core Event System (6-8 hours)
- [ ] Define `ConsoleEvent`, `EventCategory`, `EventStatus` enums (src/moss/src/console.rs)
- [ ] Implement `ConsolePrinter` with mode filtering and color detection
- [ ] Implement `EventDeduplicator` with 10-second TTL HashMap
- [ ] Add platform detection for default modes (stdin.is_terminal, INVOCATION_ID)
- [ ] Replace startup log spam with structured events
- [ ] Test basic event emission, formatting, and deduplication

### Phase 2: Remote Console Control (3-4 hours)
- [ ] Add `POST /api/v1/console/mode` endpoint with auth gate
- [ ] Implement auto-timeout state machine (30min default, resets on repeated `sing`, cancels on `quiet`)
- [ ] Add persistence to moss-config.toml (top-level `console_mode` field)
- [ ] Test mode toggling via API, verify persistence across restarts

### Phase 3: Core Subsystem Instrumentation (6-8 hours)
- [ ] Service lifecycle (requesting → pulling → creating → starting → healthy → ready)
- [ ] Job lifecycle (queued → started → progress → completed/failed)
- [ ] System startup events (starting, config loaded, ready)
- [ ] Test with real service installations and concurrent jobs

### Phase 4: Rake Integration (2-3 hours)
- [ ] Add `make stone sing [forever]` command to Rake
- [ ] Add `make stone quiet` command
- [ ] Implement auto-timeout on Rake side if needed
- [ ] Test end-to-end remote console control workflow

### Phase 5: Extended Instrumentation (4-6 hours)
- [ ] Discovery events (peer found/lost, Lantern registration)
- [ ] Health check events
- [ ] API request logging (verbose only)
- [ ] Docker events with deduplication (image pull progress)
- [ ] Network/Storage events

### Phase 6: Production Features (3-4 hours)
- [ ] Security events with ALWAYS-ON sanitization
- [ ] Ops events (cordon, drain, retire, stone join/leave)
- [ ] Test security event sanitization in all modes
- [ ] (Future) Auth gate for console control when Pond integrated

### Phase 7: Polish & Testing (2-3 hours)
- [ ] Windows service testing
- [ ] Color support testing (verify graceful fallback)
- [ ] Integration testing with all subsystems
- [ ] Performance testing (event throughput, deduplication overhead)
- [ ] Verify persistence across restarts
- [ ] Test event ordering under load (accept eventual consistency)

**Total Revised Estimate:** 26-36 hours (realistic)

**Note on Event Ordering:** Events may appear out of order under high load. This is acceptable - we prioritize throughput over strict ordering. No explicit sorting mechanism needed.

---

## 15. Success Criteria

### User Experience
- [ ] Default console output is calm and minimal (3-4 lines on startup)
- [ ] Interactive sessions show useful events without overwhelming
- [ ] Each log line is meaningful and self-contained
- [ ] Concurrent operations don't create visual chaos
- [ ] Color coding aids quick scanning (green = success, red = error, yellow = warning)
- [ ] Remote console control works reliably from Rake

### Technical
- [ ] All 15 event categories implemented and documented
- [ ] Event emission performance < 1μs (zero-cost when no console output)
- [ ] Platform detection works correctly (Windows service vs interactive, systemd vs terminal)
- [ ] Persistence survives restarts
- [ ] Auto-timeout prevents "stuck" verbose mode

### Observability
- [ ] Service lifecycle fully visible (request → ready)
- [ ] Job progress tracked with meaningful stages
- [ ] Discovery events show cluster changes
- [ ] Health issues surface clearly
- [ ] Security events ALWAYS sanitized in ALL modes (no credential leakage)
- [ ] Ops events (cordon, drain) provide operational visibility

### Zen Garden Principles
- [x] Calm by default - no log spam
- [x] Powerful when needed - comprehensive event coverage
- [x] Delightful interactions - "make stone sing" is memorable
- [x] Normative options - standard commands work too
- [x] Visual coherence - consistent formatting across all events
- [x] No surprises - predictable behavior, clear semantics

---

## 16. User Documentation

**Target Locations:**
- Moss README.md: Add "Console Output Modes" section
- Rake README.md: Document `make stone sing/quiet` commands
- Garden documentation: Add troubleshooting guide for verbose mode

**Content to Include:**
- Console mode descriptions (Silent, Minimal, Informative, Verbose)
- Platform-specific defaults explanation
- Remote control workflow examples
- Security considerations (event sanitization, future auth with Pond)
- Troubleshooting: "My console is too verbose" → how to quiet

---

## 17. Future Enhancements

### Near-term (Next Phase)
- [ ] Event filtering: `garden-rake listen stone --filter services,jobs`
- [ ] Event history: `garden-rake listen stone --history 10` (show last N events)
- [ ] Console status query: `garden-rake console stone status` (check current mode)
- [ ] Event search: `garden-rake events stone search "postgresql"`

### Medium-term
- [ ] Event webhooks - POST events to external URLs for monitoring
- [ ] Event recording - Save event stream to file for replay
- [ ] Multi-stone watch - `garden-rake listen stone-01 stone-02 stone-03`
- [ ] Event metrics - Prometheus metrics derived from events
- [ ] Rich progress bars - For long-running operations with download progress

### Long-term
- [ ] Event dashboard - Web UI showing live event feed
- [ ] Event analytics - Query historical events, generate reports
- [ ] Event alerting - Trigger actions on specific event patterns
- [ ] Custom event handlers - User-defined scripts triggered by events

---

## Appendix: Complete Example Output

### Minimal Mode (Production Daemon)
```
15:18:33 System    │ STARTING      │ stone-turquoise-glacier
15:18:34 System    │ READY         │ http://192.168.1.171:7185
```

### Informative Mode (Interactive Development)
```
15:18:33 System    │ STARTING      │ stone-turquoise-glacier
15:18:33 Docker    │ CONNECTED     │ Docker daemon
15:18:33 Discovery │ LISTENING     │ UDP port 7184
15:18:33 Manifests │ FOUND         │ 13 manifest files
15:18:34 System    │ READY         │ HTTP server → http://192.168.1.171:7185

[User plants PostgreSQL]

15:23:10 Services  │ REQUESTING    │ postgresql-dev → postgres:16-alpine
15:23:12 Services  │ PULLING       │ postgresql-dev (45%)
15:23:25 Services  │ CREATING      │ postgresql-dev
15:23:27 Services  │ STARTING      │ postgresql-dev → :5432
15:23:30 Services  │ HEALTHY       │ postgresql-dev (3.2s)
15:23:30 Services  │ READY         │ postgresql-dev (20.3s)

[Remote user runs: garden-rake make stone sing]

15:25:00 System    │ CONSOLE_MODE  │ Changed → informative (remote request, temporary)

[User plants Redis concurrently with MongoDB]

15:26:00 Services  │ REQUESTING    │ redis-cache → redis:7-alpine
15:26:01 Services  │ REQUESTING    │ mongodb-test → mongo:7-alpine
15:26:02 Services  │ PULLING       │ redis-cache (30%)
15:26:02 Services  │ PULLING       │ mongodb-test (10%)
15:26:05 Services  │ PULLING       │ redis-cache (80%)
15:26:06 Services  │ CREATING      │ redis-cache
15:26:07 Services  │ STARTING      │ redis-cache → :6379
15:26:08 Services  │ HEALTHY       │ redis-cache (1.5s)
15:26:08 Services  │ READY         │ redis-cache
15:26:12 Services  │ PULLING       │ mongodb-test (65%)
15:26:18 Services  │ CREATING      │ mongodb-test
15:26:20 Services  │ STARTING      │ mongodb-test → :27017
15:26:25 Services  │ HEALTHY       │ mongodb-test (5.2s)
15:26:25 Services  │ READY         │ mongodb-test

[Peer stone discovered]

15:27:30 Discovery │ PEER_FOUND    │ stone-azure-mountain → 192.168.1.101:7185

[Health warning]

15:28:00 Health    │ WARNING       │ redis-cache → memory 85% (850MB/1GB)
```

### Verbose Mode (Deep Debugging)
```
15:18:33 System    │ STARTING      │ stone-turquoise-glacier
15:18:33 Config    │ READING       │ moss-config.toml → /etc/moss/config.toml
15:18:33 Config    │ PARSED        │ moss-config.toml (console_mode, port, stone_name)
15:18:33 Config    │ MERGED        │ CLI + Env + File → final configuration
15:18:33 Docker    │ CONNECTED     │ Docker daemon (version 24.0.7)
15:18:33 Discovery │ LISTENING     │ UDP port 7184
15:18:33 Manifests │ SCANNING      │ /var/lib/moss/manifests
15:18:33 Manifests │ FOUND         │ 13 manifest files
15:18:33 Manifests │ READING       │ postgresql/manifest.yaml
15:18:33 Manifests │ PARSED        │ postgresql → snippet format
15:18:33 Manifests │ VALIDATED     │ postgresql → schema valid
[... all manifests ...]
15:18:34 System    │ READY         │ HTTP server → http://192.168.1.171:7185

[User makes API request to plant service]

15:23:10 API       │ POST          │ /api/v1/offerings → 202 Accepted (12ms)
15:23:10 Jobs      │ QUEUED        │ install-postgresql (#abc12345)
15:23:10 Jobs      │ STARTED       │ install-postgresql (#abc12345)
15:23:10 Services  │ REQUESTING    │ postgresql-dev → postgres:16-alpine (job #abc12345)
15:23:12 Docker    │ IMAGE_PULL    │ postgres:16-alpine (layer 1/5, 15%, 30MB/192MB)
15:23:14 Docker    │ IMAGE_PULL    │ postgres:16-alpine (layer 1/5, 45%, 85MB/192MB)
15:23:16 Docker    │ IMAGE_PULL    │ postgres:16-alpine (layer 2/5, 60%, 115MB/192MB)
15:23:20 Docker    │ IMAGE_PULL    │ postgres:16-alpine (layer 3/5, 80%, 155MB/192MB)
15:23:24 Docker    │ IMAGE_PULL    │ postgres:16-alpine (complete, 192MB)
15:23:25 Services  │ CREATING      │ postgresql-dev → container c4f8a2b1
15:23:25 Storage   │ CREATING      │ postgres-data → volume (20GB limit)
15:23:25 Storage   │ MOUNTING      │ postgres-data → /var/lib/postgresql/data
15:23:27 Network   │ PORT_BINDING  │ postgresql-dev → 0.0.0.0:5432 → 5432
15:23:27 Services  │ STARTING      │ postgresql-dev → :5432
15:23:28 Docker    │ CONTAINER_UP  │ c4f8a2b1 → postgresql-dev (PID 12345)
15:23:30 Health    │ CHECK_PASS    │ postgresql-dev → responding (15ms)
15:23:30 Services  │ HEALTHY       │ postgresql-dev (3.2s)
15:23:30 Services  │ READY         │ postgresql-dev (total: 20.3s, mem: 45MB, cpu: 2%)
15:23:30 Jobs      │ COMPLETED     │ install-postgresql (20.3s)
15:23:30 API       │ RESPONSE      │ POST /api/v1/offerings → 201 Created (20.3s total)
```

---

## Decision Record

**Approved:** January 19, 2026  
**Team Consensus:** Unanimous approval from all specialists

**Key Decisions:**
1. ✅ Structured single-line event format (Category │ STATUS │ Target → details)
2. ✅ 15 event categories covering all Moss operations
3. ✅ Remote console control via `garden-rake make stone sing`
4. ✅ Platform-aware defaults (Windows/Linux, service/interactive)
5. ✅ Persistence with `forever` keyword
6. ✅ 30-minute auto-timeout for temporary mode changes
7. ✅ Reuse moss-config.toml for persistence (no separate file)
8. ✅ Auth gate for console control in production
9. ✅ Comprehensive instrumentation across all subsystems
10. ✅ Zero-cost when console output disabled

**Implementation Timeline:** 10-16 hours across 7 phases  
**Priority:** High - critical for UX and observability  
**Breaking Changes:** None (additive only)

---

*End of Design Document*

# Moss Console Output Instrumentation - Candidate Analysis

**Date:** 2026-01-19  
**Purpose:** Comprehensive scan of Moss codebase to identify all console output candidates for structured event instrumentation.

---

## Executive Summary

**Total Candidates Found:** 148 tracing/output points  
**Files Analyzed:** 19 Rust source files  
**Categories Covered:** System startup, Docker operations, service lifecycle, discovery, templates, API operations, jobs, config, health, binary refresh

---

## 1. System Startup & Configuration

### main.rs - System Initialization (Lines 2236-2600)

**STARTUP - System Starting**
- Line 2236: `tracing::info!("Moss starting on...")` → **Event:** `System | STARTING | Moss v{version}`
- Line 2574: `tracing::info!(?addr, api_endpoint = ..., "Moss HTTP server ready")` → **Event:** `System | READY | HTTP server → http://...`

**CONFIG - Configuration Loading**
- Line 126-137: Config file loading/parsing → **Events:** `Config | READING/LOADED/PARSE_ERROR`
- Line 142: `tracing::debug!(path = ?config_path, "Config file not found, using defaults")` → **Event:** `Config | NOT_FOUND | Using defaults`
- Line 146: `tracing::warn!(path = ?config_path, error = ?e, "Failed to read config file")` → **Event:** `Config | READ_ERROR`

**DOCKER - Docker Daemon Connection**
- Line 2366: `tracing::info!("Docker daemon connected successfully")` → **Event:** `Docker | CONNECTED | Docker daemon`
- Line 2371-2380: Docker connection retries and failure → **Events:** `Docker | RETRY/FAILED`

**FIRST_BOOT - First Boot Initialization**
- Line 2248: `tracing::info!("First run detected on Linux, spawning background initialization task")` → **Event:** `System | FIRST_BOOT | Initializing...`
- Line 2262: `tracing::info!(attempt, "Filesystem is writable, proceeding with first boot initialization")` → **Event:** `System | FS_READY | Proceeding with setup`
- Line 2271: `tracing::info!(new_name = %new_name, "First boot initialization completed successfully")` → **Event:** `System | FIRST_BOOT_DONE | New stone name: {name}`
- Line 2281: `tracing::error!(error = ?e, "First boot initialization failed")` → **Event:** `System | FIRST_BOOT_ERROR`
- Line 2293: `tracing::error!("First boot initialization abandoned - filesystem never became writable")` → **Event:** `System | FS_ERROR | Filesystem never became writable`

**SHUTDOWN - Graceful Shutdown**
- Line 2580: `tracing::info!("Shutdown signal received, initiating graceful shutdown")` → **Event:** `System | SHUTTING_DOWN`
- Line 2597: `tracing::info!("Waiting up to 5s for in-flight requests to complete")` → **Event:** `System | DRAINING | Waiting for requests`
- Line 2600: `tracing::info!("Moss daemon shutdown complete")` → **Event:** `System | STOPPED`
- Line 2606: `tracing::info!("Admin shutdown endpoint called")` → **Event:** `System | ADMIN_SHUTDOWN`
- Line 2627-2640: Signal handling (SIGTERM, SIGINT, Ctrl+C) → **Event:** `System | SIGNAL_RECEIVED | {signal}`

---

## 2. Manifests (Templates) Loading

### templates.rs - Manifest Discovery & Loading (Lines 82-478)

**MANIFESTS - Runtime Directory Check**
- Line 82: `tracing::info!(path = %templates_dir.display(), "Runtime templates directory available")` → **Event:** `Manifests | DIR_FOUND | /var/lib/moss/manifests`
- Line 85: `tracing::warn!("Runtime templates directory missing: {}", RUNTIME_TEMPLATES_DIR)` → **Event:** `Manifests | DIR_MISSING`

**MANIFESTS - Template Listing**
- Line 107: `tracing::info!(count = templates.len(), "Listed runtime templates")` → **Event:** `Manifests | FOUND | {count} manifest files`

**MANIFESTS - Template Loading**
- Line 228: `tracing::info!(offering = offering, source = "filesystem", "Loaded template")` → **Event:** `Manifests | LOADED | {offering}`
- Line 345-357: Template parsing (snippet/compose formats) → **Events:**
  - Line 353: `tracing::info!(service = service_name, "Parsed as snippet format")` → **Event:** `Manifests | PARSED | {service} → snippet format`
  - Line 357: `tracing::debug!(service = service_name, error = ?e, "Snippet parse failed, trying compose format")` → **Event:** `Manifests | TRYING_COMPOSE`

**MANIFESTS - Compatibility Rules**
- Line 463: `tracing::info!(service = service_name, path = %path.display(), "Loaded compatibility rules from runtime")` → **Event:** `Manifests | COMPAT_RULES | {service} → {count} rules`
- Line 467: `tracing::warn!(service = service_name, path = %path.display(), error = ?e, "Failed to parse compatibility rules")` → **Event:** `Manifests | COMPAT_ERROR`
- Line 474: `tracing::debug!(service = service_name, "No runtime compatibility rules found")` → **Event:** `Manifests | NO_COMPAT`

---

## 3. Docker Operations

### docker.rs - Container Lifecycle (Lines 22-530)

**DOCKER - Connection**
- Line 22: `tracing::debug!("Connecting to Docker via Windows named pipe")` → **Event:** `Docker | CONNECTING | Windows named pipe`
- Line 29: `tracing::debug!("Connecting to Docker via Unix socket")` → **Event:** `Docker | CONNECTING | Unix socket`

**SERVICES - Stop Operations**
- Line 45: `tracing::info!(service = %name, "Stopping service via Docker API")` → **Event:** `Services | STOPPING | {service}`
- Line 52: `tracing::info!(service = %name, "Service stopped successfully")` → **Event:** `Services | STOPPED | {service} ({duration})`

**SERVICES - Start Operations**
- Line 59: `tracing::info!(service = %name, "Starting service via Docker API")` → **Event:** `Services | STARTING | {service}`
- Line 66: `tracing::info!(service = %name, "Service started successfully")` → **Event:** `Services | STARTED | {service}`

**SERVICES - Install Operations**
- Line 78: `tracing::info!(service = %name, image = %image, "Installing service via Docker API")` → **Event:** `Services | INSTALLING | {service} → {image}`
- Line 140: `tracing::info!(container_id = %response.id, container_name = %container_name, "Container created")` → **Event:** `Services | CREATED | {service} (ID: {short_id})`
- Line 148: `tracing::info!(service = %name, container_name = %container_name, "Service started successfully")` → **Event:** `Services | READY | {service}`

**SERVICES - Remove Operations**
- Line 153: `tracing::info!(service = %name, "Removing service via Docker API")` → **Event:** `Services | REMOVING | {service}`
- Line 180: `tracing::info!(service = %name, container_name = %container_name, "Service removed successfully")` → **Event:** `Services | REMOVED | {service}`

**SERVICES - Upgrade Operations**
- Line 194: `tracing::info!(service = %name, new_image = %new_image, "Upgrading service")` → **Event:** `Services | UPGRADING | {service} → {new_image}`
- Line 206: `tracing::info!(service = %name, container_name = %container_name, "Service upgraded successfully")` → **Event:** `Services | UPGRADED | {service}`

**DOCKER - Image Pull Operations**
- Line 253: `tracing::info!(image = %image, "Pulling Docker image")` → **Event:** `Docker | PULLING | {image}`
- Line 266: `tracing::debug!(image = %image, status = %status, "Pull progress")` → **Event:** `Docker | PULL_PROGRESS | {image} ({percent}%)` (DEDUPLICATED)
- Line 275: `tracing::info!(image = %image, "Image pulled successfully")` → **Event:** `Docker | PULL_COMPLETE | {image}`

---

## 4. Service Lifecycle & Jobs

### main.rs - Service Installation Jobs (Lines 1539-1820)

**JOBS - Single Service Installation**
- Line 1539: `tracing::info!(job_id, offering, "Starting service installation")` → **Event:** `Jobs | STARTED | install-{offering} (#{job_id})`
- Line 1642: `tracing::error!(job_id, offering, error = ?e, "Docker install failed")` → **Event:** `Jobs | FAILED | install-{offering} → {error}`
- Line 1694: `tracing::info!(job_id, offering, "Service installation completed")` → **Event:** `Jobs | COMPLETED | install-{offering} ({duration})`

**JOBS - Batch Installation**
- Line 1707: `tracing::info!(job_id, count = offerings.len(), "Starting batch installation")` → **Event:** `Jobs | STARTED | batch-install ({count} services)`
- Line 1710: `tracing::info!(job_id, offering, "Installing service")` → **Event:** `Jobs | PROGRESS | {offering}`
- Line 1738: `tracing::error!(job_id, offering, reason = %reason, "Compatibility validation failed")` → **Event:** `Jobs | COMPAT_ERROR | {offering} → {reason}`
- Line 1760: `tracing::error!(job_id, offering, error = ?e, "Docker install failed")` → **Event:** `Jobs | FAILED | {offering} → {error}`
- Line 1804: `tracing::info!(job_id, offering, "Service installed")` → **Event:** `Jobs | PROGRESS | {offering} installed`
- Line 1820: `tracing::info!(job_id, "Batch installation completed")` → **Event:** `Jobs | COMPLETED | batch-install`

---

## 5. Discovery & Networking

### discovery.rs - UDP Discovery (Lines 10-44)

**DISCOVERY - Listener Startup**
- Line 10: `tracing::info!("UDP discovery listener started on port {}", ports::DISCOVERY_UDP)` → **Event:** `Discovery | LISTENING | UDP port 7184`

**DISCOVERY - Request/Response**
- Line 17: `tracing::debug!(?addr, request_id = %request.request_id, "Discovery request")` → **Event:** `Discovery | REQUEST | from {addr}`
- Line 36: `tracing::info!(?addr, endpoint = %response_endpoint, "Sent discovery response")` → **Event:** `Discovery | RESPONSE | to {addr} → {endpoint}`

**DISCOVERY - Errors**
- Line 44: `tracing::warn!(error = ?e, "UDP recv error (non-timeout)")` → **Event:** `Discovery | ERROR | {error}`

### mdns.rs - mDNS Announcement (Lines 21-28)

**DISCOVERY - mDNS**
- Line 21: `tracing::info!(instance = %instance_name, "mDNS announcement registered")` → **Event:** `Discovery | MDNS_ACTIVE | {instance_name}`
- Line 28: `tracing::debug!("mDNS not available on Windows, skipping")` → **Event:** (Skip - debug level)

### main.rs - Lantern Registration (Lines 2675-2709)

**DISCOVERY - Lantern Registration**
- Line 2675-2700: Lantern registration loop → **Events:**
  - Line 2700: `tracing::debug!("Registered with Lantern successfully")` → **Event:** `Discovery | LANTERN_REG | Registered`
  - Line 2703: `tracing::warn!("Lantern not reachable...")` → **Event:** `Discovery | LANTERN_UNREACHABLE | Retrying...`
  - Line 2709: `tracing::warn!(error = ?e, "Failed to register with Lantern")` → **Event:** `Discovery | LANTERN_ERROR`
  - Line 2353: `tracing::error!(error = ?e, "Lantern registration loop failed")` → **Event:** `Discovery | LANTERN_FATAL`

---

## 6. Offerings & Catalog

### main.rs - Offerings Catalog (Lines 2427-2446)

**OFFERINGS - Catalog Building**
- Line 2427: `tracing::info!("Building offerings catalog...")` → **Event:** `Offerings | BUILDING | Scanning manifests`
- Line 2432: `tracing::info!("Built offerings catalog: {} total, {} runtime")` → **Event:** `Offerings | BUILT | {total} offerings ({runtime} runtime)`
- Line 2439: `tracing::warn!(error = ?e, "Failed to build offerings catalog - API will return empty results")` → **Event:** `Offerings | BUILD_ERROR`

### api/v1/offerings.rs - Planting (Lines 228-284)

**OFFERINGS - Planting**
- Line 228: `tracing::info!(offering = %payload.name, "Planting offering (simplified)")` → **Event:** `Jobs | QUEUED | plant-{offering}`
- Line 284: `tracing::error!(error = ?e, "Failed to rebuild offerings catalog")` → **Event:** `Offerings | REBUILD_ERROR`

---

## 7. Container Adoption & Registry

### main.rs - Container Adoption (Lines 329-356, 1847-1916)

**SERVICES - Container Adoption (Startup)**
- Line 329: `tracing::warn!(error = ?e, "Failed to list zen containers for adoption")` → **Event:** `Services | SCAN_ERROR`
- Line 347: `tracing::info!(offering = %offering, "Adopting existing zen-offering container into registry")` → **Event:** `Services | ADOPTING | {offering}`
- Line 353: `tracing::warn!(offering = %offering, "Found zen-offering container but no matching template; leaving unregistered")` → **Event:** `Services | NO_MANIFEST | {offering}`
- Line 356: `tracing::warn!(offering = %offering, error = ?e, "Failed to adopt existing container; leaving it alone")` → **Event:** `Services | ADOPT_ERROR | {offering}`

**SERVICES - Container Adoption (Runtime)**
- Line 1847-1916: Runtime container discovery and adoption → **Events:**
  - Line 1860: `tracing::info!("Found {} registered, {} total containers")` → **Event:** `Services | SCAN_COMPLETE | {registered} registered, {total} total`
  - Line 1894: `tracing::warn!(container = %container_name, "Found zen-offering container not in registry (adopting)")` → **Event:** `Services | ORPHAN_FOUND | {container}`
  - Line 1902: `tracing::warn!(container = %container_name, "No matching template for container; leaving unregistered")` → **Event:** `Services | NO_MANIFEST | {container}`
  - Line 1905: `tracing::warn!(container = %container_name, error = ?e, "Failed to adopt container; leaving it alone")` → **Event:** `Services | ADOPT_ERROR | {container}`
  - Line 1916: `tracing::error!(error = ?e, "Failed to list zen containers")` → **Event:** `Services | LIST_ERROR`

**SERVICES - Registry Persistence**
- Line 264: `tracing::warn!(error = ?e, "Failed to persist moss registry")` → **Event:** `Storage | SAVE_ERROR | moss-registry.json`
- Line 2419: `tracing::warn!(error = ?e, "Failed to load persisted moss registry; starting empty")` → **Event:** `Storage | LOAD_ERROR | moss-registry.json`

---

## 8. Pre-Install Manifest

### main.rs - Pre-Install Jobs (Lines 1925-2509)

**JOBS - Pre-Install Manifest**
- Line 1925: `tracing::info!("Found pre-install manifest at {}", path)` → **Event:** `Config | PREINSTALL_FOUND | {path}`
- Line 1929: `tracing::info!("Loaded pre-install manifest with {} offerings")` → **Event:** `Config | PREINSTALL_LOADED | {count} offerings`
- Line 1938: `tracing::error!(error = ?e, "Failed to parse pre-install manifest")` → **Event:** `Config | PREINSTALL_ERROR | Parse failed`
- Line 1943: `tracing::error!(error = ?e, "Failed to read pre-install manifest")` → **Event:** `Config | PREINSTALL_ERROR | Read failed`
- Line 1948: `tracing::debug!("No pre-install manifest found at {}", path)` → **Event:** (Skip - debug level)
- Line 2493: `tracing::info!("Pre-install job finished, removing manifest")` → **Event:** `Jobs | PREINSTALL_DONE | Removing manifest`
- Line 2495: `tracing::warn!(error = ?e, "Failed to remove pre-install manifest")` → **Event:** `Storage | DELETE_ERROR | preinstall-manifest.json`
- Line 2497: `tracing::info!("Pre-install manifest removed - system ready")` → **Event:** `System | PREINSTALL_COMPLETE`
- Line 2509: `tracing::info!("Pre-install job started: {} (check /api/jobs/{})", job_id, job_id)` → **Event:** `Jobs | STARTED | preinstall (#{job_id})`

---

## 9. API Operations

### api/v1/services.rs - Service Control (Lines 211-443)

**SERVICES - Rest (Stop)**
- Line 211: `tracing::error!(error = ?e, service = %service, "Failed to stop container")` → **Event:** `Services | STOP_ERROR | {service}`
- Line 225: `tracing::warn!(error = ?e, "Failed to persist registry after rest")` → **Event:** `Storage | SAVE_ERROR | After rest`

**SERVICES - Wake (Start)**
- Line 265: `tracing::error!(error = ?e, service = %service, "Failed to start container")` → **Event:** `Services | START_ERROR | {service}`
- Line 279: `tracing::warn!(error = ?e, "Failed to persist registry after wake")` → **Event:** `Storage | SAVE_ERROR | After wake`

**SERVICES - Nourish (Upgrade)**
- Line 367: `tracing::error!(error = ?e, service = %service_name, "Docker upgrade failed")` → **Event:** `Services | UPGRADE_ERROR | {service}`
- Line 390: `tracing::warn!(error = ?e, "Failed to persist registry after nourish")` → **Event:** `Storage | SAVE_ERROR | After nourish`

**SERVICES - Prune (Delete)**
- Line 429: `tracing::error!(error = ?e, service = %service, "Docker remove failed")` → **Event:** `Services | DELETE_ERROR | {service}`
- Line 443: `tracing::warn!(error = ?e, "Failed to persist registry after delete")` → **Event:** `Storage | SAVE_ERROR | After delete`

---

## 10. Binary Refresh (Self-Update)

### main.rs - Binary Refresh (Lines 1155-1363)

**OPS - Binary Refresh**
- Line 1155: `tracing::info!(component = %payload.component, "Binary refresh requested")` → **Event:** `Ops | REFRESH_REQ | {component}`
- Line 1161: `tracing::error!(error = ?e, "Failed to decode base64 binary data")` → **Event:** `Ops | DECODE_ERROR`
- Line 1177: `tracing::error!(error = ?e, "Binary validation failed")` → **Event:** `Ops | VALIDATION_ERROR`
- Line 1189: `tracing::info!("Binary validation passed...")` → **Event:** `Ops | VALIDATED | {component} ({size} bytes)`
- Line 1203: `tracing::warn!(component = %payload.component, "Unknown component")` → **Event:** `Ops | UNKNOWN_COMPONENT | {component}`
- Line 1222: `tracing::error!(error = ?e, dir = %staging_dir, "Failed to create staging directory")` → **Event:** `Storage | MKDIR_ERROR | {dir}`
- Line 1241: `tracing::error!(error = ?e, temp_path = %temp_path, "Failed to write binary")` → **Event:** `Storage | WRITE_ERROR | {path}`
- Line 1262: `tracing::error!(error = ?e, temp_path = %temp_path, "Failed to set permissions")` → **Event:** `Storage | CHMOD_ERROR | {path}`
- Line 1282: `tracing::error!(error = ?e, target = %target_path, "Failed to move binary")` → **Event:** `Storage | MOVE_ERROR | {path}`
- Line 1299: `tracing::info!(component = %payload.component, path = %target_path, "Binary staged successfully")` → **Event:** `Ops | STAGED | {component} → {path}`

**OPS - Service Restart**
- Line 1313: `tracing::info!("Triggering service restart for binary update")` → **Event:** `Ops | RESTART_REQ | Triggering...`
- Line 1329: `tracing::info!("Windows service restart triggered")` → **Event:** `Ops | RESTART_TRIGGERED | Windows service`
- Line 1332: `tracing::warn!("Service restart command succeeded but sc returned non-zero")` → **Event:** `Ops | RESTART_WARNING | Non-zero exit`
- Line 1339: `tracing::error!(error = ?e, "Failed to execute sc command")` → **Event:** `Ops | RESTART_ERROR | sc command failed`
- Line 1353: `tracing::info!("Service restart triggered successfully")` → **Event:** `Ops | RESTART_TRIGGERED | systemd service`
- Line 1356: `tracing::warn!("Service restart command succeeded but systemctl returned non-zero")` → **Event:** `Ops | RESTART_WARNING | Non-zero exit`
- Line 1363: `tracing::error!(error = ?e, "Failed to execute systemctl")` → **Event:** `Ops | RESTART_ERROR | systemctl failed`

---

## 11. Process Management

### main.rs - Process Control (Lines 2040-2207)

**OPS - Existing Process Shutdown**
- Line 2040: `tracing::info!("Sent graceful shutdown request to existing moss instance")` → **Event:** `Ops | SHUTDOWN_REQ | Existing instance`
- Line 2049: `tracing::info!("Existing moss instance shut down gracefully")` → **Event:** `Ops | SHUTDOWN_DONE | Graceful`
- Line 2054: `tracing::warn!("Graceful shutdown timed out after 3s, forcing kill")` → **Event:** `Ops | SHUTDOWN_TIMEOUT | Forcing kill`
- Line 2057: `tracing::warn!(status = ?response.status(), "Graceful shutdown request returned non-success status")` → **Event:** `Ops | SHUTDOWN_ERROR | HTTP {status}`
- Line 2060: `tracing::debug!(error = ?e, "Could not connect to existing moss instance for graceful shutdown")` → **Event:** (Skip - debug level)

**OPS - Process Kill**
- Line 2137: `tracing::info!("Killing existing moss process: PID {}", pid)` → **Event:** `Ops | KILL | PID {pid}`
- Line 2165: `tracing::info!("Killing existing moss process: PID {}", pid)` → **Event:** `Ops | KILL | PID {pid}`
- Line 2207: `tracing::warn!("Detected another moss instance running...")` → **Event:** `Ops | CONFLICT | Another instance detected`
- Line 2304: `tracing::info!("--force flag set, attempting graceful shutdown of existing moss processes")` → **Event:** `Ops | FORCE_FLAG | Attempting shutdown`
- Line 2306: `tracing::warn!(error = ?e, "Failed to shutdown existing processes, continuing anyway")` → **Event:** `Ops | FORCE_ERROR | Continuing anyway`

---

## 12. Console & Filesystem

### console.rs - Console Writing (Lines 24-86)

**SYSTEM - Filesystem Writability**
- Line 24: `tracing::info!(attempt, "/etc became writable after retries")` → **Event:** `System | FS_WRITABLE | /etc writable after {attempt} retries`
- Line 32: `tracing::warn!("/etc is not yet writable, will retry (may be early boot timing)")` → **Event:** `System | FS_PENDING | /etc not yet writable`
- Line 44: `tracing::info!("Attempted remount of root filesystem as read-write")` → **Event:** `System | FS_REMOUNT | Attempted remount`
- Line 53: `tracing::error!("/etc not writable after all retries...")` → **Event:** `System | FS_ERROR | /etc not writable`

**CONSOLE - Direct Output**
- Line 86: `println!("{}", text)` → **Event:** (Special case - console text printing)

---

## 13. SSE Event Streaming

### main.rs - SSE Broadcasting (Lines 624-704)

**API - SSE Event Routing**
- Line 624-627: Event level routing (error/warn/debug/info) → **Events:** (Already structured)
- Line 639: `tracing::warn!("SSE client lagged {} messages", n)` → **Event:** `API | SSE_LAG | Client lagged {n} messages`
- Line 704: `tracing::warn!(service = %service_clone, error = ?e, "Log stream error")` → **Event:** `API | LOG_STREAM_ERROR | {service}`

---

## 14. Health & Errors

### main.rs - Server Errors (Lines 2331-2587)

**DISCOVERY - mDNS Error**
- Line 2331: `tracing::warn!(error = ?e, "mDNS announcement failed")` → **Event:** `Discovery | MDNS_ERROR`

**DISCOVERY - UDP Error**
- Line 2341: `tracing::error!(error = ?e, "UDP discovery listener failed")` → **Event:** `Discovery | UDP_ERROR`

**SYSTEM - Server Error**
- Line 2587: `tracing::error!(error = ?e, "Server error")` → **Event:** `System | HTTP_ERROR | {error}`

---

## 15. Invalid/Orphaned Container Cleanup

### main.rs - Container Cleanup (Lines 1459-1468)

**SERVICES - Invalid Container Cleanup**
- Line 1459: `tracing::warn!(offering = %offering, error = ?e, "Failed to drop invalid container; leaving it alone")` → **Event:** `Services | CLEANUP_ERROR | {offering}`
- Line 1468: `tracing::warn!(offering = %offering, error = ?e, "Adoption failed; leaving it alone")` → **Event:** `Services | ADOPT_ERROR | {offering}`

---

## Summary Statistics by Category

| Category | Event Count | Priority | Notes |
|----------|-------------|----------|-------|
| **System** | 25 | High | Startup, shutdown, signals, first boot |
| **Services** | 42 | High | Install, start, stop, upgrade, remove, adoption |
| **Docker** | 12 | High | Connection, image pulls, container operations |
| **Jobs** | 15 | High | Service installation, batch jobs, pre-install |
| **Manifests** | 12 | Medium | Loading, parsing, validation, compat rules |
| **Discovery** | 10 | Medium | UDP, mDNS, Lantern registration |
| **Offerings** | 5 | Medium | Catalog building, planting |
| **Config** | 8 | Medium | File loading, parsing, pre-install manifest |
| **Storage** | 10 | Low | Registry persistence, file operations |
| **Ops** | 20 | Medium | Binary refresh, service restart, process control |
| **API** | 3 | Low | SSE lag, log streaming |
| **Health** | 2 | Low | Server errors |
| **Console** | 4 | Low | Filesystem writability checks |

**Total:** 168 distinct instrumentation points

---

## Instrumentation Priority Levels

### High Priority (Phase 1-3)
- System startup/shutdown/ready
- Service lifecycle (install → ready)
- Docker operations (pull, create, start)
- Job lifecycle (queued → completed)
- Config loading
- Manifest scanning/loading

### Medium Priority (Phase 4-5)
- Discovery (UDP, mDNS, Lantern)
- Offerings catalog
- Service adoption
- Container cleanup
- Ops events (binary refresh)

### Low Priority (Phase 6-7)
- Storage errors
- API streaming
- Debug-level events
- Detailed error contexts

---

## Event Deduplication Candidates

**High-frequency events requiring deduplication (10-second TTL):**
1. Docker image pull progress (Line docker.rs:266) - can emit 5-10 events/second
2. Lantern registration retries (Line main.rs:2703) - retries every 5 seconds
3. Filesystem writability checks (Line console.rs:32) - retries frequently
4. SSE client lag warnings (Line main.rs:639) - can spam under load

---

## Special Cases

### 1. println!() Direct Output
- **Location:** console.rs:86
- **Context:** Used for writing to /dev/tty1 (TTY escape sequence)
- **Action:** Keep as-is (special console control), not a regular event

### 2. Debug-Level Logs
- **Count:** ~20 instances
- **Action:** Most debug logs should remain as-is, not converted to events
- **Exceptions:** Discovery request logging might be useful in Verbose mode

### 3. Error Context
- Many error logs include detailed context (`error = ?e`)
- **Action:** Preserve error details in event `details` field
- **Sanitization:** Be careful with errors that might leak credentials/IPs

---

## Team Decisions (2026-01-19)

### Approved Decisions

1. ✅ **Docker pull progress:** INFORMATIVE mode with 10-second deduplication
   - Show progress updates to user (avoids "is it frozen?" concern)
   - Throttle to max one update per 10 seconds per image
   
2. ✅ **Binary refresh workflow:** VERBOSE only
   - Entire sequence (decode → validate → stage → restart) is admin-level detail
   - Keep out of INFORMATIVE mode
   
3. ✅ **Container adoption:** Per-service messages
   - Show individual "Adopting {service}" events in INFORMATIVE
   - Users benefit from understanding what's being recovered
   
4. ✅ **Lantern registration:** Success in INFORMATIVE, retries in VERBOSE
   - Show successful registration in INFORMATIVE
   - Hide retry spam (every 5 seconds) in VERBOSE only
   
5. ✅ **SSE lag warnings:** VERBOSE only
   - Internal performance metric, not user-actionable
   - Keep as debug information
   
6. ✅ **MVP scope:** All 84 high-priority points (Phases 1-3)
   - No subset MVP - implement full high-priority instrumentation
   - Estimated 20-25 hours for Phases 1-3

### Additional Consensus

- ✅ **Registry persistence errors:** MINIMAL-level visibility (critical for durability)
- ✅ **Process conflict detection:** MINIMAL-level (actionable error)
- ✅ **First boot sequence:** Show all steps in INFORMATIVE (delightful initialization)
- ✅ **Event deduplication:** 10-second TTL standard for high-frequency events

### Security Audit

**Status:** Deferred for future discussion

**Rationale:** Focus on homelab owner UX. Useful error details and context are more valuable than paranoid sanitization. Security hardening is a future enhancement when enterprise deployments are a concern.

**Deferred Items:**
- PID visibility in process kill operations
- Error message truncation/path sanitization  
- Container name input validation concerns
- SSE client identifier logging
- Internal path structure exposure

---

## Implementation Priorities (Finalized)

### Phase 1: Core Events (6-8 hours) - HIGH PRIORITY
- System startup, config loading, Docker connection
- Basic event emission with deduplication
- Platform detection
- **Count:** ~30 instrumentation points

### Phase 2: Service Lifecycle (6-8 hours) - HIGH PRIORITY  
- Service install/start/stop/remove/upgrade operations
- Container adoption workflow
- Registry persistence
- **Count:** ~35 instrumentation points

### Phase 3: Jobs & Manifests (4-6 hours) - HIGH PRIORITY
- Job lifecycle events
- Manifest scanning/loading/parsing
- Pre-install manifest handling
- **Count:** ~20 instrumentation points

**High Priority Total:** 84 points, 16-22 hours estimated

### Phase 4: Discovery & Networking (3-4 hours) - MEDIUM PRIORITY
- UDP discovery, mDNS, Lantern registration (per decisions)
- **Count:** ~10 instrumentation points

### Phase 5: Extended Operations (4-5 hours) - MEDIUM PRIORITY
- Offerings catalog, container cleanup, ops events
- Binary refresh (VERBOSE only per decision)
- **Count:** ~25 instrumentation points

### Phase 6: Polish & Edge Cases (3-4 hours) - LOW PRIORITY
- Storage errors, API streaming, detailed error contexts
- **Count:** ~20 instrumentation points

**Total Revised Estimate:** 26-35 hours (realistic with decisions applied)

---

## Next Steps

1. ✅ **Team Review Complete** - Decisions finalized
2. **Event Mapping:** Create explicit mapping of each log statement to ConsoleEvent structure
3. **Implementation:** Begin Phase 1 with approved priorities and design decisions
4. **Deduplication Config:** Implement 10-second TTL for Docker pulls, Lantern retries, FS checks
5. **Testing Strategy:** Validate high-frequency event throttling under load

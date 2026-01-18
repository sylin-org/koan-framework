# Moss & Rake Feature Specification

**Developer-focused feature breakdown for implementation.**

**Date:** January 15, 2026  
**Status:** Ready for implementation

---

## Component Overview

**Moss** = Rust daemon running on each Stone (systemd service)  
**Rake** = Rust CLI tool for managing Stones from any device

**Communication:**
```
Rake (CLI) → HTTP/JSON → Moss (daemon) → Docker/mDNS
```

---

## Moss (Daemon) Feature Set

**Binary:** `garden-moss`  
**Installation:** `/usr/local/bin/garden-moss`  
**Service:** `garden-moss.service` (systemd)  
**Port:** 3001 (HTTP API)  
**Language:** Rust

### Core Responsibilities

1. **Listen for management requests** (HTTP API on port 3001)
2. **Execute service lifecycle operations** (install, uninstall, upgrade)
3. **Announce services via mDNS** (native + agnostic sidecars)
4. **Monitor container health** (update mDNS TXT records)
5. **Manage Docker Compose** (atomic updates, rollback)
6. **Detect and resolve port conflicts**
7. **Signal resource warnings** (too many services)
8. **Validate service templates** (no ad-hoc Docker configs)

---

### Feature 1: HTTP API Server

**Framework:** Axum or Actix-web  
**Port:** 3001  
**Format:** JSON request/response

**Endpoints:**

```rust
// Service Management
POST   /api/services/install       // Install service from template
DELETE /api/services/{name}        // Uninstall service
PUT    /api/services/{name}/upgrade // Upgrade to new version
GET    /api/services               // List all installed services
GET    /api/services/{name}        // Get service details

// Compose Management
GET    /api/compose                // Get current docker-compose.yml
POST   /api/compose/reload         // Reload compose file
GET    /api/manifests              // List available templates
GET    /api/manifests/{name}       // Get template details

// Announcements
GET    /api/announcements          // List current mDNS announcements
POST   /api/announcements/refresh  // Force mDNS refresh

// Health & Metadata
GET    /health                     // Daemon + container health
GET    /info                       // Stone info (name, version, capabilities)
```

**Request/Response Examples:**

```json
// POST /api/services/install
{
  "offering": "mongodb",
  "version": "7.0"  // optional, defaults to template default
}

// Response 201
{
  "status": "installed",
  "offering": "mongodb",
  "version": "7.0.4",
  "ports": {
    "native": 27017,
    "agnostic": 8080
  },
  "containers": ["mongodb", "mongodb-agnostic"],
  "announced": true
}

// GET /api/services
{
  "services": [
    {
      "name": "mongodb",
      "offering": "mongodb",
      "version": "7.0.4",
      "status": "running",
      "health": "healthy",
      "ports": { "native": 27017, "agnostic": 8080 },
      "uptime": 3600,
      "memory_mb": 450
    }
  ],
  "total": 1,
  "stone_health": "healthy"
}

// GET /health
{
  "status": "healthy",
  "moss_version": "0.1.0",
  "stone_name": "stone-01",
  "docker_running": true,
  "containers_running": 2,
  "containers_total": 2,
  "warnings": []
}

// GET /health (with warnings)
{
  "status": "degraded",
  "moss_version": "0.1.0",
  "stone_name": "stone-01",
  "docker_running": true,
  "containers_running": 4,
  "containers_total": 6,
  "warnings": [
    "High container count (6) for Stone capacity",
    "MongoDB container restarting (3 times in 10 minutes)"
  ]
}
```

**Error Responses:**

```json
// 400 Bad Request
{
  "error": "invalid_offering",
  "message": "Offering 'mysql' not found in manifest registry",
  "details": {
    "available_offerings": ["mongodb", "redis", "postgresql", "sqlserver"]
  }
}

// 409 Conflict
{
  "error": "port_conflict",
  "message": "Port 27017 already in use by 'redis' service",
  "details": {
    "requested_port": 27017,
    "conflicting_service": "redis"
  }
}

// 500 Internal Server Error
{
  "error": "docker_error",
  "message": "Failed to start container: mongodb",
  "details": {
    "docker_output": "Error: pull rate limit exceeded"
  }
}
```

**Technical Requirements:**
- Async/await (Tokio runtime)
- Graceful shutdown (SIGTERM handling)
- Request logging (structured JSON logs)
- Error handling (anyhow or thiserror)
- CORS enabled (for future web dashboard)

---

### Feature 2: mDNS Announcer

**Library:** `mdns-sd` crate  
**Service Type (self):** `_moss._tcp.local.`  
**Service Type (services):** `_koan-stone._tcp.local.`

**Responsibilities:**

1. **Announce Moss itself** (for Rake discovery)
2. **Announce native services** (MongoDB on 27017)
3. **Announce agnostic sidecars** (mongodb-agnostic on 8080)
4. **Update announcements on health changes**
5. **Remove announcements when services stop**

**Self-Announcement:**

```
stone-01-moss._moss._tcp.local.
TXT: stone_name=stone-01
     version=0.1.0
     api_port=3001
     health=healthy
```

**Service Announcement (Native):**

```
stone-01-mongodb._koan-stone._tcp.local.
TXT: offering=mongodb
     port=27017
     protocol=native
     version=7.0.4
     categories=database,document-database
     health=healthy
     priority=50
```

**Service Announcement (Agnostic Sidecar):**

```
stone-01-mongodb-agnostic._koan-stone._tcp.local.
TXT: offering=mongodb-agnostic
     port=8080
     protocol=agnostic
     version=1.0.2
     backend_service_version=7.0.4
     categories=database,document-database
     set_mode=database
     capabilities=crud,query,filter,bulk,transactions
     health=healthy
     priority=50
```

**Technical Requirements:**
- Spawn mDNS responder on startup
- Re-announce on service lifecycle events (install/uninstall/upgrade)
- Update TXT records on health check changes
- Handle network interface changes gracefully
- Log announcement/un-announcement events

---

### Feature 3: Docker Compose Manager

**Responsibilities:**

1. **Read/parse docker-compose.yml** (on Stone filesystem)
2. **Add services to compose file** (install operation)
3. **Remove services from compose file** (uninstall operation)
4. **Update service versions** (upgrade operation)
5. **Detect port conflicts** (before applying changes)
6. **Apply changes atomically** (all-or-nothing)
7. **Rollback on failure** (restore previous state)
8. **Invoke `docker compose` CLI** (or `docker-compose` fallback)

**Compose File Location:**
```
/etc/zen-garden/docker-compose.yml
```

**Operations:**

**Install Service:**
```yaml
# Before
services:
  redis:
    image: redis:7
    ports:
      - "6379:6379"

# After (mongo installed)
services:
  redis:
    image: redis:7
    ports:
      - "6379:6379"
  
  mongodb:
    image: mongo:7.0
    ports:
      - "27017:27017"
    volumes:
      - mongodb-data:/data/db
  
  mongodb-agnostic:
    image: koan/mongodb-data-api:1.0
    ports:
      - "8080:8080"
    depends_on:
      - mongodb
    environment:
      BACKEND_URI: mongodb://mongodb:27017

volumes:
  mongodb-data:
```

**Port Conflict Resolution:**
```rust
// Pseudo-code
fn resolve_port_conflict(requested_port: u16, service: &str) -> u16 {
    if port_is_available(requested_port) {
        return requested_port;
    }
    
    // Auto-increment to find next available
    let mut port = requested_port + 1;
    while !port_is_available(port) {
        port += 1;
        if port > 65535 {
            panic!("No available ports");
        }
    }
    
    log::warn!(
        "Port conflict: {} wanted {}, assigned {}",
        service, requested_port, port
    );
    
    port
}
```

**Atomic Application:**
```rust
// Pseudo-code
async fn apply_compose_changes(new_compose: ComposeFile) -> Result<()> {
    // 1. Backup current compose
    backup_compose("/etc/zen-garden/docker-compose.yml.bak")?;
    
    // 2. Write new compose
    write_compose("/etc/zen-garden/docker-compose.yml", new_compose)?;
    
    // 3. Validate syntax
    validate_compose()?;
    
    // 4. Apply changes
    let result = Command::new("docker")
        .args(&["compose", "up", "-d"])
        .output()
        .await?;
    
    if !result.status.success() {
        // Rollback
        log::error!("Compose up failed, rolling back");
        restore_compose_backup()?;
        Command::new("docker")
            .args(&["compose", "up", "-d"])
            .output()
            .await?;
        return Err(anyhow!("Failed to apply compose changes"));
    }
    
    // 5. Wait for containers to start
    tokio::time::sleep(Duration::from_secs(5)).await;
    
    // 6. Check container health
    for container in new_compose.services.keys() {
        if !is_container_running(container).await? {
            log::error!("Container {} failed to start", container);
            restore_compose_backup()?;
            return Err(anyhow!("Container startup failed"));
        }
    }
    
    Ok(())
}
```

**Technical Requirements:**
- YAML parsing (`serde_yaml`)
- File I/O (atomic writes)
- Process spawning (`tokio::process`)
- Port availability checks (TCP bind test)
- Backup/restore logic

---

### Feature 4: Service Template Handler

**Template Location:**
```
/etc/zen-garden/templates/
  mongodb.yml
  redis.yml
  postgresql.yml
  sqlserver.yml
```

**Template Format:**

```yaml
# /etc/zen-garden/templates/mongodb.yml
name: mongodb
offering: mongodb
description: MongoDB document database
categories:
  - database
  - document-database

versions:
  - tag: "7.0"
    default: true
  - tag: "6.0"
  - tag: "8.0-rc"

docker:
  native:
    image: "mongo:${VERSION}"
    ports:
      - "27017:27017"
    volumes:
      - "mongodb-data:/data/db"
    environment:
      MONGO_INITDB_ROOT_USERNAME: admin
      MONGO_INITDB_ROOT_PASSWORD: "${MONGO_PASSWORD:-changeme}"
    
  agnostic:
    image: "koan/mongodb-data-api:1.0"
    ports:
      - "8080:8080"
    depends_on:
      - mongodb
    environment:
      BACKEND_URI: "mongodb://mongodb:27017"
      SET_MODE: "database"

volumes:
  mongodb-data:

announcements:
  native:
    offering: mongodb
    protocol: native
    categories: [database, document-database]
    priority: 50
  
  agnostic:
    offering: mongodb-agnostic
    protocol: agnostic
    categories: [database, document-database]
    priority: 50
    set_mode: database
    capabilities: [crud, query, filter, bulk, transactions]
```

**Validation Rules:**
1. Template must have `name`, `offering`, `docker` sections
2. Image tags must be valid
3. Port numbers must be in range 1-65535
4. No arbitrary shell commands allowed
5. Environment variables must use `${VAR}` or `${VAR:-default}` syntax
6. Volume names must match pattern `^[a-z0-9-]+$`

**Technical Requirements:**
- YAML parsing with validation
- Variable substitution (VERSION, passwords)
- Schema validation (JSON Schema or Rust types)
- Template registry (scan directory on startup)

---

### Feature 5: Health Monitor

**Responsibilities:**

1. **Check Docker daemon status** (every 30 seconds)
2. **Check container status** (running, restarting, stopped)
3. **Monitor container resource usage** (RAM, CPU via Docker API)
4. **Detect restart loops** (container restarted >3 times in 10 min)
5. **Update mDNS TXT records** (health=healthy/degraded/unavailable)
6. **Log health transitions** (for debugging)

**Health States:**

```rust
enum HealthStatus {
    Healthy,      // All checks passing
    Degraded,     // Some issues but functional
    Unavailable,  // Critical failure
}
```

**Health Check Logic:**

```rust
async fn check_service_health(service: &str) -> HealthStatus {
    let container = format!("{}-{}", stone_name(), service);
    
    // 1. Check if container exists
    let exists = docker_container_exists(&container).await;
    if !exists {
        return HealthStatus::Unavailable;
    }
    
    // 2. Check if running
    let running = docker_container_running(&container).await;
    if !running {
        return HealthStatus::Unavailable;
    }
    
    // 3. Check restart count
    let restarts = docker_container_restart_count(&container).await;
    if restarts > 3 {
        return HealthStatus::Degraded;
    }
    
    // 4. Check memory usage (warn if >80% of limit)
    let mem_percent = docker_container_memory_percent(&container).await;
    if mem_percent > 80.0 {
        return HealthStatus::Degraded;
    }
    
    HealthStatus::Healthy
}
```

**Technical Requirements:**
- Docker API client (`bollard` crate)
- Background health check loop (spawned task)
- Async state updates (Arc<RwLock<...>>)
- Integration with mDNS announcer (update TXT records)

---

### Feature 6: Resource Monitor

**Responsibilities:**

1. **Count total containers** (running + stopped)
2. **Estimate total RAM usage** (sum of container memory)
3. **Check Stone capacity** (based on system RAM)
4. **Warn when approaching limits**
5. **Include warnings in `/health` endpoint**

**Capacity Thresholds:**

```rust
fn stone_capacity() -> StoneCapacity {
    let total_ram_gb = system_total_ram() / 1024 / 1024 / 1024;
    
    match total_ram_gb {
        0..=2 => StoneCapacity {
            max_services: 2,
            warn_threshold: 1,
            category: "Mini"
        },
        3..=4 => StoneCapacity {
            max_services: 4,
            warn_threshold: 3,
            category: "Standard"
        },
        _ => StoneCapacity {
            max_services: 8,
            warn_threshold: 6,
            category: "Large"
        }
    }
}
```

**Warning Examples:**
- "High container count (6) for Stone capacity (Standard: 4 max)"
- "Total RAM usage (3.2GB) approaching system limit (4GB)"
- "Container 'mongodb' restarting frequently (5 times in 10 minutes)"

**Technical Requirements:**
- System info (`sysinfo` crate)
- Container stats (Docker API)
- Warning persistence (in-memory, cleared on restart)

---

### Feature 7: Configuration

**Config File:** `/etc/zen-garden/garden-moss.toml`

```toml
[stone]
name = "stone-01"  # Auto-generated or user-provided

[api]
port = 3001
cors_enabled = true

[docker]
compose_file = "/etc/zen-garden/docker-compose.yml"
template_dir = "/etc/zen-garden/templates"

[mdns]
enabled = true
ttl = 120  # seconds

[health]
check_interval = 30  # seconds
restart_threshold = 3  # restarts before degraded

[logging]
level = "info"  # debug, info, warn, error
format = "json"  # json or text
```

**Technical Requirements:**
- TOML parsing (`toml` or `config` crate)
- Environment variable overrides (e.g., `MOSS_API_PORT=3001`)
- Sensible defaults (works without config file)

---

## Rake (CLI) Feature Set

**Binary:** `garden-rake`  
**Installation:** `/usr/local/bin/garden-rake` or user PATH  
**Language:** Rust

### Core Responsibilities

1. **Discover Moss daemons** (via mDNS)
2. **Send HTTP requests to Moss** (service management)
3. **Parse command-line arguments** (subcommands, flags, targets)
4. **Handle target selection** (local, specific stone, all stones)
5. **Pretty-print responses** (colored output, tables)
6. **Handle errors gracefully** (network, API, parsing)

---

### Feature 1: mDNS Service Discovery

**Library:** `mdns-sd` crate  
**Query:** `_moss._tcp.local.`

**Responsibilities:**

1. **Query for Moss daemons** (on local network)
2. **Parse TXT records** (stone_name, api_port, health)
3. **Build Moss endpoint list** (IP:port pairs)
4. **Cache discoveries** (avoid repeated lookups)
5. **Handle timeouts** (no Moss found)

**Discovery Logic:**

```rust
async fn discover_moss(target: Target) -> Result<Vec<MossEndpoint>> {
    let mdns = ServiceDaemon::new()?;
    let receiver = mdns.browse("_moss._tcp.local.")?;
    
    let mut endpoints = Vec::new();
    let timeout = Duration::from_secs(3);
    
    let start = Instant::now();
    while start.elapsed() < timeout {
        if let Ok(event) = receiver.recv_timeout(Duration::from_millis(100)) {
            match event {
                ServiceEvent::ServiceResolved(info) => {
                    let stone_name = info.get_property_val_str("stone_name")
                        .unwrap_or("unknown");
                    let api_port = info.get_property_val_str("api_port")
                        .and_then(|s| s.parse().ok())
                        .unwrap_or(3001);
                    
                    // Filter by target
                    if target.matches(stone_name) {
                        endpoints.push(MossEndpoint {
                            stone_name: stone_name.to_string(),
                            host: info.get_hostname().to_string(),
                            port: api_port,
                        });
                    }
                }
                _ => {}
            }
        }
    }
    
    if endpoints.is_empty() {
        return Err(anyhow!("No Moss daemons found on network"));
    }
    
    Ok(endpoints)
}
```

**Technical Requirements:**
- mDNS query with timeout
- TXT record parsing
- Error handling (no services found)

---

### Feature 2: HTTP Client

**Library:** `reqwest` crate  
**Format:** JSON

**Responsibilities:**

1. **Send requests to Moss API** (GET, POST, PUT, DELETE)
2. **Handle authentication** (future: API keys, mTLS)
3. **Parse JSON responses**
4. **Handle errors** (connection refused, timeout, 4xx/5xx)
5. **Retry logic** (optional, for network flakiness)

**Client Implementation:**

```rust
struct MossClient {
    endpoint: MossEndpoint,
    client: reqwest::Client,
}

impl MossClient {
    async fn install_service(&self, offering: &str, version: Option<&str>) 
        -> Result<ServiceInstallResponse> 
    {
        let url = format!("http://{}:{}/api/services/install", 
            self.endpoint.host, self.endpoint.port);
        
        let body = serde_json::json!({
            "offering": offering,
            "version": version,
        });
        
        let response = self.client
            .post(&url)
            .json(&body)
            .send()
            .await?;
        
        if !response.status().is_success() {
            let error: ApiError = response.json().await?;
            return Err(anyhow!("{}: {}", error.error, error.message));
        }
        
        let result: ServiceInstallResponse = response.json().await?;
        Ok(result)
    }
    
    async fn list_services(&self) -> Result<ServiceListResponse> {
        let url = format!("http://{}:{}/api/services", 
            self.endpoint.host, self.endpoint.port);
        
        let response = self.client.get(&url).send().await?;
        let result: ServiceListResponse = response.json().await?;
        Ok(result)
    }
}
```

**Technical Requirements:**
- HTTP client with connection pooling
- JSON serialization/deserialization (`serde_json`)
- Error type for API errors
- Timeout configuration (default 30s)

---

### Feature 3: Command Parser

**Library:** `clap` crate  
**Style:** Subcommands with flags

**Command Structure:**

```bash
garden-rake <SUBCOMMAND> [ARGS] [FLAGS]
```

**Subcommands:**

```rust
#[derive(Parser)]
#[command(name = "garden-rake")]
#[command(about = "Manage Zen Garden Stones")]
struct Cli {
    #[command(subcommand)]
    command: Commands,
}

#[derive(Subcommand)]
enum Commands {
    /// Get Stone status
    Status {
        /// Target stone (default: local)
        #[arg(long)]
        at: Option<String>,
        
        /// Query all Stones
        #[arg(long)]
        all: bool,
    },
    
    /// Install service offering
    Offer {
        /// Service name (e.g., mongodb, redis)
        service: String,
        
        /// Service version (optional)
        #[arg(long)]
        version: Option<String>,
        
        /// Target stone (default: local)
        #[arg(long)]
        at: Option<String>,
        
        /// Install on all Stones
        #[arg(long)]
        all: bool,
    },
    
    /// Remove service
    Remove {
        /// Service name
        service: String,
        
        /// Target stone (default: local)
        #[arg(long)]
        at: Option<String>,
    },
    
    /// List installed services
    List {
        /// Target stone (default: local)
        #[arg(long)]
        at: Option<String>,
        
        /// List from all Stones
        #[arg(long)]
        all: bool,
    },
    
    /// Upgrade service
    Upgrade {
        /// Service name
        service: String,
        
        /// Target version (optional, defaults to latest)
        #[arg(long)]
        to: Option<String>,
        
        /// Target stone (default: local)
        #[arg(long)]
        at: Option<String>,
    },
}
```

**Usage Examples:**

```bash
# Status
garden-rake status                  # Local Stone
garden-rake status --at stone-01    # Specific Stone
garden-rake status --all            # All Stones

# Install
garden-rake offer mongodb           # Install locally
garden-rake offer mongodb here      # Explicit local
garden-rake offer mongodb --at stone-01  # Remote Stone
garden-rake offer mongodb --all     # All Stones
garden-rake offer mongodb --version 7.0  # Specific version

# List
garden-rake list                    # Local services
garden-rake list --at stone-01      # Remote services
garden-rake list --all              # All services across garden

# Remove
garden-rake remove mongodb          # Remove from local
garden-rake remove mongodb --at stone-01  # Remove from remote

# Upgrade
garden-rake upgrade mongodb         # Upgrade to latest
garden-rake upgrade mongodb --to 8.0  # Upgrade to specific version
```

**Technical Requirements:**
- Argument parsing with validation
- Help text generation
- Subcommand dispatch
- Flag conflict detection (--at and --all mutually exclusive)

---

### Feature 4: Output Formatting

**Library:** `colored` crate for colors, custom table formatting

**Responsibilities:**

1. **Pretty-print tables** (service lists, status)
2. **Colorize output** (success=green, error=red, warning=yellow)
3. **Handle JSON output** (--json flag for scripting)
4. **Progress indicators** (for long operations)

**Examples:**

**Status Output:**

```bash
$ garden-rake status

Stone: stone-01
Status: Healthy
Moss Version: 0.1.0
Docker: Running

Services (2):
┌──────────┬─────────┬─────────┬────────┬──────────┬────────────┐
│ Name     │ Offering│ Version │ Health │ Uptime   │ Memory     │
├──────────┼─────────┼─────────┼────────┼──────────┼────────────┤
│ mongodb  │ mongodb │ 7.0.4   │ ✓      │ 2d 3h    │ 450 MB     │
│ redis    │ redis   │ 7.2.3   │ ✓      │ 5h 23m   │ 80 MB      │
└──────────┴─────────┴─────────┴────────┴──────────┴────────────┘

Total: 2 services, 530 MB memory
```

**List All Output:**

```bash
$ garden-rake list --all

┌──────────┬──────────┬─────────┬─────────┬────────┐
│ Stone    │ Name     │ Offering│ Version │ Health │
├──────────┼──────────┼─────────┼─────────┼────────┤
│ stone-01 │ mongodb  │ mongodb │ 7.0.4   │ ✓      │
│ stone-01 │ redis    │ redis   │ 7.2.3   │ ✓      │
│ stone-02 │ postgres │ postgres│ 16.1    │ ✓      │
│ stone-03 │ sqlserver│ sqlsrv  │ 2022    │ ⚠      │
└──────────┴──────────┴─────────┴─────────┴────────┘

Total: 4 services across 3 Stones
```

**Install Output:**

```bash
$ garden-rake offer mongodb

Discovering Moss daemons... ✓ (found stone-01)
Installing mongodb on stone-01...
  [1/4] Validating template... ✓
  [2/4] Checking port availability... ✓ (27017, 8080)
  [3/4] Updating docker-compose.yml... ✓
  [4/4] Starting containers... ✓

✓ Successfully installed mongodb 7.0.4

Services:
  - mongodb (native): mongodb://stone-01.local:27017
  - mongodb-agnostic (HTTP): http://stone-01.local:8080

Next steps:
  1. Use connection string: zen-garden:mongodb
  2. Check status: garden-rake status
```

**Error Output:**

```bash
$ garden-rake offer mysql

✗ Error: Offering 'mysql' not found

Available offerings:
  - mongodb
  - postgresql
  - redis
  - sqlserver

Try: garden-rake offer <service>
```

**JSON Output (for scripting):**

```bash
$ garden-rake list --json

{
  "stone": "stone-01",
  "services": [
    {
      "name": "mongodb",
      "offering": "mongodb",
      "version": "7.0.4",
      "status": "running",
      "health": "healthy",
      "ports": {
        "native": 27017,
        "agnostic": 8080
      },
      "uptime_seconds": 180000,
      "memory_mb": 450
    }
  ],
  "total": 1
}
```

**Technical Requirements:**
- Color output detection (isatty check)
- Table formatting library or custom impl
- JSON serialization for --json flag
- Progress bars for long operations (`indicatif` crate)

---

### Feature 5: Target Selection

**Targets:**

```rust
enum Target {
    Local,              // Default, discover local Moss (127.0.0.1 or hostname)
    Specific(String),   // --at stone-01
    All,                // --all
}
```

**Resolution Logic:**

```rust
fn resolve_target(at: Option<String>, all: bool) -> Result<Target> {
    if all {
        return Ok(Target::All);
    }
    
    if let Some(stone_name) = at {
        return Ok(Target::Specific(stone_name));
    }
    
    Ok(Target::Local)
}

async fn execute_on_target<F, T>(target: Target, operation: F) -> Result<Vec<T>>
where
    F: Fn(&MossClient) -> BoxFuture<'_, Result<T>>,
{
    let endpoints = discover_moss(target).await?;
    
    let mut results = Vec::new();
    for endpoint in endpoints {
        let client = MossClient::new(endpoint);
        let result = operation(&client).await?;
        results.push(result);
    }
    
    Ok(results)
}
```

**Technical Requirements:**
- mDNS filtering by stone name
- Parallel execution for --all (tokio::join!)
- Error aggregation (show all failures, not just first)

---

### Feature 6: Error Handling

**Error Types:**

```rust
#[derive(Debug, thiserror::Error)]
enum RakeError {
    #[error("No Moss daemons found on network")]
    NoMossFound,
    
    #[error("Failed to connect to Moss at {0}:{1}")]
    ConnectionFailed(String, u16),
    
    #[error("API error: {0}")]
    ApiError(String),
    
    #[error("Invalid command: {0}")]
    InvalidCommand(String),
    
    #[error("Service '{0}' not found on {1}")]
    ServiceNotFound(String, String),
}
```

**User-Friendly Messages:**

```bash
$ garden-rake status

✗ Error: No Moss daemons found on network

Troubleshooting:
  1. Is Moss running? Check: systemctl status garden-moss.service
  2. Is mDNS working? Check: avahi-browse -a
  3. Are you on the same network as your Stones?
  4. Firewall blocking port 5353 (mDNS)?

For more help: garden-rake --help
```

**Technical Requirements:**
- Custom error types (`thiserror`)
- Error context (`anyhow`)
- User-friendly error messages (no stack traces by default)
- Debug mode (--debug flag shows full errors)

---

## Implementation Priorities

**Phase 1 (MVP):**
1. Moss: HTTP API (basic endpoints)
2. Moss: Docker Compose manager (install/uninstall)
3. Moss: mDNS announcer (self + services)
4. Rake: mDNS discovery
5. Rake: Basic commands (offer, remove, list, status)
6. Rake: Output formatting (tables, colors)

**Phase 2 (Polish):**
1. Moss: Health monitoring
2. Moss: Port conflict resolution
3. Moss: Resource warnings
4. Rake: --all flag (parallel execution)
5. Rake: --json output
6. Rake: Progress bars

**Phase 3 (Advanced):**
1. Moss: Template validation
2. Moss: Rollback on failure
3. Rake: Error recovery
4. Rake: Interactive mode
5. Integration tests

---

## Development Workflow

**1. Set up Rust workspace:**

```toml
# Cargo.toml
[workspace]
members = ["moss", "garden-rake", "common"]

[workspace.dependencies]
tokio = { version = "1", features = ["full"] }
serde = { version = "1", features = ["derive"] }
serde_json = "1"
anyhow = "1"
thiserror = "1"
```

**2. Shared types (common crate):**

```rust
// common/src/lib.rs
use serde::{Deserialize, Serialize};

#[derive(Debug, Clone, Serialize, Deserialize)]
pub struct ServiceInfo {
    pub name: String,
    pub offering: String,
    pub version: String,
    pub status: String,
    pub health: HealthStatus,
    pub ports: ServicePorts,
}

#[derive(Debug, Clone, Serialize, Deserialize)]
pub struct ServicePorts {
    pub native: Option<u16>,
    pub agnostic: Option<u16>,
}

#[derive(Debug, Clone, Serialize, Deserialize)]
#[serde(rename_all = "lowercase")]
pub enum HealthStatus {
    Healthy,
    Degraded,
    Unavailable,
}
```

**3. Testing strategy:**

```rust
// moss/tests/integration_test.rs
#[tokio::test]
async fn test_install_service() {
    let client = reqwest::Client::new();
    
    // Install mongodb
    let response = client
        .post("http://localhost:3001/api/services/install")
        .json(&serde_json::json!({
            "offering": "mongodb",
            "version": "7.0"
        }))
        .send()
        .await
        .unwrap();
    
    assert_eq!(response.status(), 201);
    
    let result: ServiceInstallResponse = response.json().await.unwrap();
    assert_eq!(result.offering, "mongodb");
    assert!(result.announced);
}
```

---

## Questions for Team

1. **Docker API vs CLI:** Should Moss use Bollard (Docker API) or shell out to `docker compose`?
2. **Template distribution:** Embed templates in binary or read from filesystem?
3. **Config hot-reload:** Should Moss watch config file and reload without restart?
4. **Rake binary name:** `garden-rake`, `rake`, or just `gr`?
5. **Windows support:** Priority for Rake on Windows (mDNS challenging)?
6. **Logging:** Structured JSON logs or human-readable text?
7. **Metrics:** Should Moss expose Prometheus metrics endpoint?
8. **Database:** Does Moss need persistent state (SQLite) or in-memory only?

---

## Success Criteria

**Moss:**
- ✅ Starts as systemd service on boot
- ✅ Responds to HTTP API requests within 100ms
- ✅ Announces itself via mDNS within 5 seconds of startup
- ✅ Installs service from template in <30 seconds
- ✅ Detects port conflicts before applying changes
- ✅ Rolls back failed compose changes
- ✅ Updates mDNS announcements on health changes
- ✅ Handles SIGTERM gracefully (shutdown in <5s)

**Rake:**
- ✅ Discovers local Moss within 3 seconds
- ✅ Sends API requests and parses responses
- ✅ Pretty-prints output with colors and tables
- ✅ Handles --at and --all flags correctly
- ✅ Shows user-friendly error messages
- ✅ Provides --json output for scripting
- ✅ Works on Linux, macOS, Windows (with Lantern fallback)

---

## Next Steps

1. **Create workspace structure** (`cargo new --workspace`)
2. **Implement Moss HTTP API** (Axum + basic endpoints)
3. **Implement mDNS announcer** (self-announcement first)
4. **Implement Rake discovery** (find local Moss)
5. **Integrate Moss + Rake** (offer command end-to-end)
6. **Add Docker Compose management** (install/uninstall)
7. **Add health monitoring** (background task)
8. **Polish output formatting** (tables, colors)
9. **Write integration tests** (Moss + Rake + Docker)
10. **Package for distribution** (Debian packages, brew formulas)

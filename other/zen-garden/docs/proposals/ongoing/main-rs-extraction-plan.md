# Main.rs Extraction Plan

**Status:** Approved
**Principles:** SoC, YAGNI, KISS, DRY
**Target:** main.rs < 100 lines
**Priority:** Developer ergonomics, reduced mental load

---

## Current State

**main.rs: 1,113 lines** containing:

| Lines | Content | Problem |
|-------|---------|---------|
| 1-70 | Imports, comments | Acceptable |
| 92-180 | CLI struct, Commands enum | Should stay |
| 180-460 | `run_daemon()` setup: config, network monitor, lantern registration | Bootstrap logic |
| 461-600 | Console init, Docker retry loop, capabilities init | Bootstrap logic |
| 600-706 | AppState construction, registry loading | Bootstrap logic |
| 708-789 | Offerings catalog, manifest loading | Bootstrap logic |
| 791-860 | Pre-install manifest handling | Bootstrap logic |
| 862-905 | Background task spawning | Task composition |
| 908-981 | Route definitions (70 lines) | Router module |
| 983-1077 | Server binding, shutdown handling | Server module |
| 1083-1113 | `shutdown_signal()` | Infra utility |

**Root-level legacy files (5,677 lines):**
- metrics.rs (1,481) → infra/telemetry/
- console.rs (1,227) → infra/output/
- docker.rs (679) → infra/container/
- templates.rs (463) → domain/templates/
- app_state.rs (126) → domain/ or bootstrap/

---

## Target Architecture

### Ideal main.rs (~80 lines)

```rust
use garden_moss::{cli, bootstrap, infra};

#[tokio::main]
async fn main() -> anyhow::Result<()> {
    let cli = cli::parse();

    match cli.command {
        Some(cli::Commands::Install) => {
            infra::install_service()?;
        }
        Some(cli::Commands::Uninstall) => {
            infra::uninstall_service()?;
        }
        Some(cli::Commands::Version) => {
            println!("{}", bootstrap::version_string());
        }
        None => {
            // Main daemon mode
            let config = infra::load_config(&cli)?;
            bootstrap::run(config).await?;
        }
    }

    Ok(())
}
```

### New Module Structure

**Existing modules (NO changes needed):**
- `infra/config.rs` - Already has MossConfig ✓
- `tasks/discovery.rs` - Already has lantern_registration_loop ✓
- `tasks/network_monitor.rs` - Already has NetworkMonitor, NetworkEvent ✓
- `app_state.rs` - Already has AppState, Job, JobStatus, MossEvent ✓

**New modules to create:**

```
moss/src/
├── main.rs              (~80 lines - CLI dispatch only)
├── lib.rs               (public API exports)
├── cli.rs               (NEW: CLI parsing, moved from main.rs)
│
├── bootstrap/
│   ├── mod.rs           (pub fn run() - main orchestration)
│   ├── server.rs        (NEW: HTTP server setup, binding, shutdown)
│   ├── router.rs        (NEW: all route definitions)
│   ├── startup.rs       (NEW: initialization sequence)
│   ├── first_boot.rs    (existing)
│   └── preinstall.rs    (existing)
│
├── tasks/
│   ├── mod.rs           (existing)
│   ├── coordinator.rs   (NEW: spawn/manage all background tasks)
│   ├── network_monitor.rs (existing - add IP change → Lantern re-registration)
│   ├── ... (existing modules unchanged)
│
├── infra/
│   ├── container/
│   │   ├── mod.rs
│   │   └── docker.rs    (MOVED from root)
│   ├── telemetry/
│   │   ├── mod.rs
│   │   └── metrics.rs   (MOVED from root)
│   ├── output/
│   │   ├── mod.rs
│   │   └── console.rs   (MOVED from root)
│   ├── platform.rs      (existing - add shutdown_signal())
│   └── ... (existing)
│
├── domain/
│   ├── templates/
│   │   ├── mod.rs
│   │   └── loader.rs    (MOVED from root templates.rs)
│   └── ... (existing)
│
└── api/ (unchanged)
```

---

## Extraction Plan

### Phase 1: CLI Extraction

**Create `cli.rs`** - Move CLI parsing out of main.rs

```rust
// cli.rs
use clap::Parser;

#[derive(Parser)]
#[command(name = "garden-moss")]
#[command(about = "Zen Garden Moss - Service orchestration daemon")]
pub struct Cli {
    #[command(subcommand)]
    pub command: Option<Commands>,

    #[arg(long)]
    pub stone_name: Option<String>,

    #[arg(long, short)]
    pub port: Option<u16>,

    // ... all existing args
}

#[derive(clap::Subcommand)]
pub enum Commands {
    Install,
    Uninstall,
    // ... existing
}

pub fn parse() -> Cli {
    Cli::parse()
}
```

**Impact:** ~90 lines out of main.rs

---

### Phase 2: Router Extraction

**Create `bootstrap/router.rs`** - All route definitions

```rust
// bootstrap/router.rs
use axum::{routing::{get, post, delete}, Router};
use crate::{api, AppState};

pub fn configure(state: AppState) -> Router {
    Router::new()
        // Health/monitoring (root level)
        .route("/health", get(api::v1::health::get_health))
        .route("/capabilities", get(api::v1::capabilities::get_capabilities))
        .route("/metrics", get(api::v1::metrics::get_metrics))

        // V1 API - Offerings
        .route("/api/v1/offerings", get(api::v1::offerings::list_offerings_v1))
        .route("/api/v1/offerings", post(api::v1::offerings::plant_offering_v1))
        // ... all routes

        .layer(axum::extract::DefaultBodyLimit::max(200 * 1024 * 1024))
        .with_state(state)
}
```

**Impact:** ~70 lines out of main.rs

---

### Phase 3: Server Extraction

**Create `bootstrap/server.rs`** - HTTP server lifecycle

```rust
// bootstrap/server.rs
use std::net::SocketAddr;
use axum::Router;
use crate::infra::platform::shutdown_signal;

pub struct ServerConfig {
    pub port: u16,
    pub graceful_shutdown_timeout_secs: u64,
}

pub async fn run(
    router: Router,
    config: ServerConfig,
    console: Arc<ConsolePrinter>,
) -> anyhow::Result<()> {
    let addr: SocketAddr = format!("0.0.0.0:{}", config.port).parse()?;

    let listener = bind_with_error_handling(addr, &console).await?;

    console.emit(ConsoleEvent::new(
        EventCategory::System,
        EventStatus::Ready,
        format!("HTTP server → http://{}", addr),
    ));

    axum::serve(listener, router)
        .with_graceful_shutdown(shutdown_signal())
        .await?;

    // Drain in-flight requests
    console.emit(ConsoleEvent::new(
        EventCategory::System,
        EventStatus::Draining,
        "In-flight requests",
    ));
    tokio::time::sleep(Duration::from_secs(config.graceful_shutdown_timeout_secs)).await;

    console.emit(ConsoleEvent::new(
        EventCategory::System,
        EventStatus::Stopped,
        "Shutdown complete",
    ));

    Ok(())
}

async fn bind_with_error_handling(
    addr: SocketAddr,
    console: &ConsolePrinter,
) -> anyhow::Result<TcpListener> {
    // Port-in-use error handling extracted here
}
```

**Impact:** ~100 lines out of main.rs

---

### Phase 4: Startup Sequence Extraction

**Create `bootstrap/startup.rs`** - All initialization logic

```rust
// bootstrap/startup.rs

/// Docker connection with retry
pub async fn connect_docker(
    console: &ConsolePrinter,
    max_retries: u32,
) -> anyhow::Result<Arc<DockerManager>> {
    for attempt in 1..=max_retries {
        match DockerManager::new() {
            Ok(dm) => {
                console.emit(ConsoleEvent::docker_connected());
                return Ok(Arc::new(dm));
            }
            Err(e) if attempt < max_retries => {
                console.emit(ConsoleEvent::docker_retry(attempt, max_retries));
                tokio::time::sleep(Duration::from_secs(2)).await;
            }
            Err(e) => {
                console.emit(ConsoleEvent::docker_failed(max_retries));
                return Err(e);
            }
        }
    }
    unreachable!()
}

/// Hardware capabilities initialization
pub async fn init_capabilities(
    stone_name: &str,
    console: &ConsolePrinter,
) -> Arc<RwLock<Option<HardwareCapabilities>>> {
    let cached = infra::load_cached_capabilities().await;
    let capabilities = Arc::new(RwLock::new(cached.clone()));

    if cached.is_none() {
        let skeleton = create_skeleton_capabilities(stone_name);
        *capabilities.write().await = Some(skeleton.clone());
        let _ = infra::save_capabilities_cache(&skeleton).await;
    } else {
        console.emit(ConsoleEvent::capabilities_loaded());
    }

    capabilities
}

/// Registry loading and container adoption
pub async fn init_registry(state: &AppState) {
    // Load persisted registry
    // Adopt existing containers
}

/// Offerings catalog building
pub async fn init_offerings(state: &AppState, console: &ConsolePrinter) {
    // Build offerings index
    // Load manifests
}

/// Pre-install manifest handling
pub async fn handle_preinstall(state: &AppState) {
    // Check for manifest, create jobs if needed
}
```

**Impact:** ~300 lines out of main.rs

---

### Phase 5: Task Coordinator

**Create `tasks/coordinator.rs`** - Background task composition

```rust
// tasks/coordinator.rs
use crate::{AppState, infra::MossConfig};

pub struct TaskHandles {
    pub health_monitor: JoinHandle<()>,
    pub auto_adoption: Option<JoinHandle<()>>,
    pub hardware_detection: JoinHandle<()>,
    pub network_monitor: Option<JoinHandle<()>>,
    pub lantern_registration: Option<JoinHandle<()>>,
}

pub struct TaskConfig {
    pub enable_auto_adoption: bool,
    pub enable_network_monitor: bool,
    pub lantern_endpoint: Option<String>,
}

/// Spawn all background tasks based on configuration
pub fn spawn_all(
    state: AppState,
    config: TaskConfig,
) -> TaskHandles {
    let health_monitor = tokio::spawn({
        let s = state.clone();
        async move { health_monitor_task(s).await }
    });

    let auto_adoption = if config.enable_auto_adoption {
        Some(tokio::spawn({
            let s = state.clone();
            async move { auto_adoption_task(s).await }
        }))
    } else {
        None
    };

    let network_monitor = if config.enable_network_monitor {
        Some(spawn_network_monitor(state.clone(), config.lantern_endpoint.clone()))
    } else {
        None
    };

    // ... etc

    TaskHandles {
        health_monitor,
        auto_adoption,
        hardware_detection,
        network_monitor,
        lantern_registration,
    }
}

/// Graceful shutdown of all tasks
pub async fn shutdown_all(handles: TaskHandles) {
    // Cancel and await all handles
}
```

**Impact:** ~50 lines out of main.rs + cleaner task management

---

### Phase 6: Network Event Handling (Extend Existing)

**Extend `tasks/network_monitor.rs`** - Add Lantern re-registration on IP change

The network event handling logic in main.rs (lines 380-446) that re-registers with Lantern on IP change should be moved into the existing `tasks/network_monitor.rs` module, NOT a new file.

```rust
// tasks/network_monitor.rs (EXTEND existing)

impl NetworkMonitor {
    /// Handle network event with optional Lantern re-registration
    pub async fn handle_event_with_lantern(
        &self,
        event: NetworkEvent,
        lantern_endpoint: Option<&str>,
        stone_name: &str,
        port: u16,
    ) {
        match event {
            NetworkEvent::AddressChanged { ref old, ref new } => {
                if let Some(lantern) = lantern_endpoint {
                    self.reregister_with_lantern(lantern, stone_name, new, port).await;
                }
            }
            NetworkEvent::Reconnected { ref new } => {
                if let Some(lantern) = lantern_endpoint {
                    self.reregister_with_lantern(lantern, stone_name, new, port).await;
                }
            }
            NetworkEvent::Disconnected { .. } => {
                // Just log - no action needed
            }
        }
    }

    async fn reregister_with_lantern(&self, lantern: &str, stone: &str, ip: &str, port: u16) {
        // Re-registration logic from main.rs lines 400-446
    }
}
```

**Note:** `tasks/discovery.rs` already has `lantern_registration_loop` - this is for IP change events only.

**Impact:** ~80 lines out of main.rs (move to existing module)

---

### Phase 7: State Builder (Optional Enhancement)

**Extend `app_state.rs`** - Add builder pattern (optional)

`app_state.rs` already exists with AppState, Job, JobStatus, MossEvent. The builder pattern is optional - the current construction in main.rs is straightforward and could simply move to `bootstrap/startup.rs` as a function.

**Option A: Keep current pattern, just move to startup.rs**
```rust
// bootstrap/startup.rs
pub fn build_app_state(...) -> AppState {
    // Current construction logic from main.rs
}
```

**Option B: Add builder to app_state.rs (optional refactor)**
```rust
// app_state.rs (EXTEND existing)
impl AppState {
    pub fn builder(stone_name: String, port: u16) -> AppStateBuilder { ... }
}

pub struct AppStateBuilder { ... }
```

**Recommendation:** Option A is simpler and follows KISS. Don't over-engineer.

**Impact:** ~50 lines out of main.rs (move to startup.rs)

---

### Phase 8: Legacy File Migration

| File | Target | Notes |
|------|--------|-------|
| docker.rs (679) | infra/container/docker.rs | Container runtime |
| metrics.rs (1,481) | infra/telemetry/metrics.rs | Observability |
| console.rs (1,227) | infra/output/console.rs | User output |
| templates.rs (463) | domain/templates/loader.rs | Template loading |
| app_state.rs (126) | bootstrap/state.rs | State definition |
| discovery.rs (153) | infra/discovery/ | Already has home |
| network_singletons.rs (112) | infra/network/ | Network utilities |
| mdns.rs (30) | infra/discovery/mdns.rs | mDNS |
| legacy_helpers.rs (160) | Delete or migrate | Temporary shim |
| api_legacy.rs (58) | Delete | Should be unused |

---

### Phase 9: Signal Handling

**Create `infra/platform/signals.rs`**

```rust
// infra/platform/signals.rs

/// Cross-platform shutdown signal handler
pub async fn shutdown_signal() {
    #[cfg(unix)]
    {
        use tokio::signal::unix::{signal, SignalKind};
        let mut sigterm = signal(SignalKind::terminate()).expect("SIGTERM handler");
        let mut sigint = signal(SignalKind::interrupt()).expect("SIGINT handler");

        tokio::select! {
            _ = sigterm.recv() => tracing::info!("SIGTERM received"),
            _ = sigint.recv() => tracing::info!("SIGINT received"),
        }
    }

    #[cfg(windows)]
    {
        tokio::signal::ctrl_c().await.expect("Ctrl+C handler");
        tracing::info!("Ctrl+C received");
    }
}
```

**Impact:** ~30 lines out of main.rs

---

## Final bootstrap/mod.rs

```rust
// bootstrap/mod.rs
mod config;
mod router;
mod server;
mod startup;
mod state;
mod first_boot;
mod preinstall;

pub use config::*;
pub use state::StateBuilder;

use crate::{tasks, infra};

/// Main entry point for daemon mode
pub async fn run(config: MossConfig) -> anyhow::Result<()> {
    // 1. Initialize console
    let console = startup::init_console(&config);
    console.emit_startup();

    // 2. Connect to Docker
    let docker = startup::connect_docker(&console, 30).await?;

    // 3. Initialize capabilities
    let capabilities = startup::init_capabilities(&config.stone_name, &console).await;

    // 4. Build state
    let state = StateBuilder::new(config.stone_name.clone(), config.port)
        .with_docker(docker)
        .with_console(console.clone())
        .with_capabilities(capabilities)
        .build();

    // 5. Initialize registry and offerings
    startup::init_registry(&state).await;
    startup::init_offerings(&state, &console).await;
    startup::handle_preinstall(&state).await;

    // 6. Spawn background tasks
    let task_config = tasks::TaskConfig::from(&config);
    let task_handles = tasks::coordinator::spawn_all(state.clone(), task_config);

    // 7. Build router and run server
    let router = router::configure(state.clone());
    server::run(router, server::ServerConfig::from(&config), console).await?;

    // 8. Shutdown tasks
    tasks::coordinator::shutdown_all(task_handles).await;

    Ok(())
}

pub fn version_string() -> String {
    format!("{}.{}", env!("CARGO_PKG_VERSION"), env!("BUILD_NUMBER"))
}
```

---

## Summary

### What's Actually Needed

**NEW files to create (4):**
| File | Purpose | Lines |
|------|---------|-------|
| `cli.rs` | CLI parsing | ~90 |
| `bootstrap/router.rs` | Route definitions | ~70 |
| `bootstrap/server.rs` | HTTP server lifecycle | ~100 |
| `bootstrap/startup.rs` | Initialization sequence | ~300 |
| `tasks/coordinator.rs` | Task spawning/management | ~50 |

**EXTEND existing files (2):**
| File | Addition | Lines |
|------|----------|-------|
| `tasks/network_monitor.rs` | Lantern re-registration on IP change | ~50 |
| `infra/platform.rs` | shutdown_signal() | ~30 |

**MOVE files (4):**
| From | To |
|------|-----|
| `docker.rs` | `infra/container/docker.rs` |
| `metrics.rs` | `infra/telemetry/metrics.rs` |
| `console.rs` | `infra/output/console.rs` |
| `templates.rs` | `domain/templates/loader.rs` |

**NO changes needed (existing):**
- `infra/config.rs` - MossConfig already there ✓
- `tasks/discovery.rs` - lantern_registration_loop already there ✓
- `app_state.rs` - AppState, Job, etc. already there ✓

### Line Count Projection

| Component | Current (main.rs) | Destination |
|-----------|-------------------|-------------|
| CLI parsing | 90 | cli.rs (NEW) |
| Router | 70 | bootstrap/router.rs (NEW) |
| Server | 100 | bootstrap/server.rs (NEW) |
| Startup sequence | 300 | bootstrap/startup.rs (NEW) |
| Task spawning | 50 | tasks/coordinator.rs (NEW) |
| Network events | 80 | tasks/network_monitor.rs (EXTEND) |
| Signals | 30 | infra/platform.rs (EXTEND) |
| **Remaining** | **~80** | CLI dispatch + version |

### Benefits

1. **SoC**: Each module has single responsibility
2. **KISS**: main.rs is trivial CLI dispatch
3. **DRY**: Reuses existing infra/config, tasks/discovery, app_state
4. **YAGNI**: No over-abstraction, just proper placement
5. **Testability**: Each component testable in isolation
6. **Composability**: Tasks can be enabled/disabled via config

### Migration Order

1. **Phase 1-2**: CLI + Router (low risk, high impact, quick wins)
2. **Phase 3-4**: Server + Startup (medium risk, most lines)
3. **Phase 5-6**: Task coordinator + Network events (medium risk)
4. **Phase 7**: State builder (optional, KISS says skip)
5. **Phase 8**: Legacy file migration (parallel, low risk)
6. **Phase 9**: Signals (trivial)

---

## Validation Criteria

- [ ] main.rs < 100 lines
- [ ] All business logic in domain/
- [ ] All I/O in infra/
- [ ] All HTTP handlers in api/
- [ ] All background work in tasks/
- [ ] All initialization in bootstrap/
- [ ] No files at moss/src/ root except main.rs, lib.rs
- [ ] Each module < 500 lines (prefer < 300)
- [ ] 100% test pass rate maintained
- [ ] Boot time unchanged or improved

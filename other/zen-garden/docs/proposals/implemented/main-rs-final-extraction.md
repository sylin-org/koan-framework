# Main.rs Final Extraction Plan

**Status:** ✅ Completed
**Principles:** SoC, YAGNI, KISS, DRY
**Target:** main.rs < 80 lines
**Final Result:** 54 lines (from 783)
**Reduction:** 93%

---

## Executive Summary

The previous extraction (Phase 1-6) reduced main.rs from 1,114 to 783 lines. However, **key extracted functions are not being used** - main.rs still has inline implementations of `connect_docker()` and `init_capabilities()` that duplicate the extracted versions.

This plan completes the extraction by:
1. **Using already-extracted functions** (immediate wins)
2. **Extracting the remaining orchestration** into `bootstrap/run.rs`
3. **Moving configuration merging** into a dedicated module
4. **Consolidating background task spawning** into coordinator.rs

---

## Current State Analysis

### Lines 1-100: Imports + Migration Comments (~100 lines)
- 45 lines of actual imports
- 53 lines of migration notes (can be deleted)

### Lines 101-124: CLI Dispatch (~24 lines)
- Windows service commands (TakeRoot, InstallService)
- Update finalization flags
- **Should stay in main.rs** (entry point logic)

### Lines 126-203: Configuration Loading (~78 lines)
| Content | Lines | Destination |
|---------|-------|-------------|
| Config file loading | 5 | bootstrap/config.rs |
| Stone name resolution | 25 | bootstrap/config.rs |
| Port/timeout merging | 10 | bootstrap/config.rs |
| Console mode detection | 10 | bootstrap/config.rs |
| Tracing initialization | 20 | bootstrap/config.rs |
| Startup logging | 8 | bootstrap/config.rs |

### Lines 205-263: First Boot (~59 lines)
- Linux-only first-boot spawning
- Filesystem writability checks
- **Move to:** `bootstrap/first_boot.rs` (extend existing)

### Lines 265-314: Network + Lantern (~50 lines)
- `--force` flag handling (10 lines)
- NetworkMonitor initialization (7 lines)
- Static host detection (8 lines)
- mDNS announcement (8 lines)
- Lantern registration call (17 lines)
- **Move to:** `bootstrap/run.rs`

### Lines 316-406: Console + Docker (~91 lines)
| Content | Lines | Status |
|---------|-------|--------|
| Console printer init | 40 | Move to run.rs |
| Docker connection loop | 51 | **DUPLICATE** - use `connect_docker()` |

### Lines 408-473: State Construction (~66 lines)
| Content | Lines | Status |
|---------|-------|--------|
| Event/shutdown channels | 5 | Move to run.rs |
| Capabilities loading | 35 | **DUPLICATE** - use `init_capabilities()` |
| AppState construction | 18 | Move to run.rs or state builder |
| Network monitor wrap | 3 | Move to run.rs |

### Lines 475-561: Task Spawning Part 1 (~87 lines)
| Task | Lines | Status |
|------|-------|--------|
| UDP discovery listener | 38 | **DUPLICATE** - use `start_discovery_listener()` |
| Hardware detection | 22 | **DUPLICATE** - use `start_hardware_detection()` |
| Registry loading | 27 | **DUPLICATE** - use `start_registry_loader()` |

### Lines 563-715: Task Spawning Part 2 (~153 lines)
| Task | Lines | Status |
|------|-------|--------|
| Catalog building | 40 | **DUPLICATE** - use `start_catalog_builder()` |
| Manifest loading | 37 | **DUPLICATE** - use `start_manifest_loader()` |
| Pre-install handling | 70 | Extract to `start_preinstall_handler()` |
| Health monitor | 6 | **DUPLICATE** - use `start_health_monitor()` |

### Lines 717-777: Auto-adoption + Server (~61 lines)
| Content | Lines | Status |
|---------|-------|--------|
| Auto-adoption spawn | 45 | **DUPLICATE** - use `start_auto_adoption()` |
| Router + server | 16 | Already using extracted functions |

---

## Duplication Analysis

**Already extracted but NOT used in main.rs:**

| Function | Location | main.rs Lines | Savings |
|----------|----------|---------------|---------|
| `connect_docker()` | bootstrap/startup.rs | 356-406 | 51 |
| `init_capabilities()` | bootstrap/startup.rs | 414-454 | 41 |
| `start_discovery_listener()` | tasks/coordinator.rs | 475-514 | 40 |
| `start_hardware_detection()` | tasks/coordinator.rs | 516-536 | 21 |
| `start_registry_loader()` | tasks/coordinator.rs | 538-561 | 24 |
| `start_catalog_builder()` | tasks/coordinator.rs | 563-604 | 42 |
| `start_manifest_loader()` | tasks/coordinator.rs | 606-644 | 39 |
| `start_health_monitor()` | tasks/coordinator.rs | 717-721 | 5 |
| `start_auto_adoption()` | tasks/coordinator.rs | 723-760 | 38 |
| **Total Duplicated** | | | **~301 lines** |

This is 38% of main.rs that can be eliminated by simply using the extracted functions!

---

## Target Architecture

### Ideal main.rs (~60-70 lines)

```rust
//! Zen Garden Moss - Service orchestration daemon
//!
//! Entry point with CLI dispatch. All logic delegated to bootstrap module.

use garden_moss::{cli, bootstrap, infra};

#[tokio::main]
async fn main() -> anyhow::Result<()> {
    let args = cli::parse();

    // Handle Windows service commands (early exit)
    #[cfg(target_os = "windows")]
    if let Some(result) = cli::handle_windows_commands(&args).await {
        return result;
    }

    // Load and merge configuration (CLI > Env > File > Defaults)
    let config = bootstrap::DaemonConfig::from_cli(&args)?;

    // Initialize tracing/logging
    bootstrap::init_tracing(&config);

    // Handle --force flag (kill existing processes)
    if args.force {
        if let Err(e) = infra::kill_existing_moss_processes_graceful().await {
            tracing::warn!(error = ?e, "Failed to shutdown existing processes");
        }
        tokio::time::sleep(tokio::time::Duration::from_millis(500)).await;
    }

    // Run daemon (all orchestration in bootstrap::run)
    bootstrap::run(config).await
}
```

### New Module: bootstrap/config.rs (~120 lines)

```rust
//! Configuration loading and merging
//!
//! Handles the priority chain: CLI > Env > Config File > Defaults

use crate::{cli::Cli, infra::MossConfig, console};

/// Merged daemon configuration from all sources
pub struct DaemonConfig {
    pub stone_name: String,
    pub port: u16,
    pub log_level: String,
    pub console_mode: console::ConsoleMode,
    pub fast_sync_timeout: Option<u64>,
    pub event_dedup_ttl_secs: u64,
    pub file_config: Option<MossConfig>,
}

impl DaemonConfig {
    /// Load and merge configuration from CLI, env, and file
    pub fn from_cli(cli: &Cli) -> anyhow::Result<Self> {
        let file_config = MossConfig::load();

        // Stone name resolution: CLI > config > hostname > env > default
        let stone_name = resolve_stone_name(cli, &file_config)?;

        // Port: CLI > config > default
        let port = cli.port
            .or_else(|| file_config.as_ref().and_then(|c| c.port))
            .unwrap_or(garden_common::ports::MOSS_HTTP);

        // ... other fields

        Ok(Self { stone_name, port, /* ... */ })
    }
}

fn resolve_stone_name(cli: &Cli, config: &Option<MossConfig>) -> anyhow::Result<String> {
    // Current logic from main.rs lines 137-164
}

/// Initialize tracing subscriber
pub fn init_tracing(config: &DaemonConfig) {
    // Current logic from main.rs lines 182-193
}
```

### New Module: bootstrap/run.rs (~200 lines)

```rust
//! Main daemon orchestration
//!
//! Coordinates all startup phases and background tasks.

use crate::{
    AppState, bootstrap, infra, tasks,
    console::{ConsolePrinter, ConsoleEvent, EventCategory, EventStatus},
    NetworkMonitor, NetworkMonitorConfig,
};

/// Run the Moss daemon with the given configuration
pub async fn run(config: bootstrap::DaemonConfig) -> anyhow::Result<()> {
    // Phase 1: First-boot check (Linux only)
    if cfg!(target_os = "linux") && console::is_first_run() {
        tasks::start_first_boot_task(&config.stone_name, config.port).await;
    }

    // Phase 2: Network monitoring
    let network_monitor = NetworkMonitor::start_with_config(
        NetworkMonitorConfig::default()
            .with_disconnect_retry(5)
            .with_connected_poll(30)
    ).await;

    let api_endpoint = resolve_api_endpoint(&network_monitor, config.port).await;

    // Phase 3: mDNS announcement
    let _mdns = announce_mdns(&config.stone_name, config.port);

    // Phase 4: Lantern registration
    tasks::start_lantern_registration(
        &config.stone_name, &api_endpoint, config.port,
        is_static_host(), &network_monitor, None,
    ).await;

    // Phase 5: Console printer
    let console = Arc::new(ConsolePrinter::with_dedup_ttl(
        config.console_mode, config.event_dedup_ttl_secs
    ));
    emit_startup_events(&console, &config);

    // Phase 6: Docker connection (uses extracted function!)
    let docker = bootstrap::connect_docker(&console, bootstrap::DockerConfig::default()).await?;

    // Phase 7: Capabilities (uses extracted function!)
    let capabilities = bootstrap::init_capabilities(&config.stone_name, &console).await;

    // Phase 8: Build AppState
    let (event_tx, _) = tokio::sync::broadcast::channel(100);
    let shutdown_tx = Arc::new(tokio::sync::Notify::new());

    let state = build_app_state(
        config, docker, console.clone(), capabilities,
        network_monitor, event_tx, shutdown_tx.clone(),
    );

    // Phase 9: Start all background tasks (uses extracted functions!)
    tasks::start_all_background_tasks(
        &state, &config.stone_name, &api_endpoint,
        capabilities.clone(), config.file_config,
    ).await;

    // Phase 10: Pre-install manifest
    tasks::start_preinstall_handler(&state).await;

    // Phase 11: HTTP server
    let app = bootstrap::router::configure(state.clone());
    let listener = bootstrap::bind_server(config.port, &console).await?;
    bootstrap::run_server(listener, app, &api_endpoint, console, shutdown_tx, ServerConfig::default()).await
}

fn build_app_state(...) -> AppState {
    // AppState construction (18 lines from main.rs)
}
```

### Extended: tasks/coordinator.rs

Add two new functions:

```rust
/// Start first-boot initialization task (Linux only)
pub fn start_first_boot_task(stone_name: &str, port: u16) {
    // Logic from main.rs lines 205-263
    tokio::spawn(async move {
        // Filesystem writability check loop
        // run_first_boot_initialization call
        // Exit for systemd restart
    });
}

/// Start pre-install manifest handler
pub async fn start_preinstall_handler(state: &AppState) {
    // Logic from main.rs lines 646-714
    if let Some(manifest) = load_preinstall_manifest().await {
        // Validate offerings
        // Create job
        // Spawn installation task
    }
}
```

### Extended: cli.rs

Add Windows command handling:

```rust
/// Handle Windows-specific CLI commands
/// Returns Some(result) if a command was handled, None to continue to daemon mode
#[cfg(target_os = "windows")]
pub async fn handle_windows_commands(cli: &Cli) -> Option<anyhow::Result<()>> {
    if let Some(command) = &cli.command {
        return Some(match command {
            Commands::TakeRoot | Commands::InstallService => {
                infra::install_windows_service().await
            }
        });
    }

    if cli.update_finalize {
        return Some(infra::finalize_service_update().await);
    }

    if cli.cleanup_old {
        return Some(infra::cleanup_after_service_update().await);
    }

    None
}

#[cfg(not(target_os = "windows"))]
pub async fn handle_windows_commands(_cli: &Cli) -> Option<anyhow::Result<()>> {
    None
}
```

---

## Implementation Phases

### Phase A: Eliminate Duplicates (Immediate Win)
**Estimated reduction: 301 lines**

Replace inline implementations with calls to extracted functions:
1. Replace Docker loop with `connect_docker()`
2. Replace capabilities skeleton with `init_capabilities()`
3. Replace task spawning with coordinator functions

**No new modules needed** - just use what exists!

### Phase B: Create bootstrap/config.rs
**Estimated reduction: 78 lines**

Extract configuration loading and merging logic.

### Phase C: Create bootstrap/run.rs
**Estimated reduction: ~200 lines**

Extract main orchestration into a single `run()` function.

### Phase D: Extend coordinator.rs
**Estimated reduction: ~130 lines**

Add `start_first_boot_task()` and `start_preinstall_handler()`.

### Phase E: Extend cli.rs
**Estimated reduction: ~25 lines**

Add `handle_windows_commands()`.

### Phase F: Cleanup
- Remove 53 lines of migration comments
- Clean up imports

---

## Line Count Projection

| Phase | Lines Removed | main.rs After |
|-------|---------------|---------------|
| Current | - | 783 |
| Phase A | 301 | 482 |
| Phase B | 78 | 404 |
| Phase C | 200 | 204 |
| Phase D | 130 | 74 |
| Phase E | 25 | 49 |
| Phase F | ~20 | **~30** |

**Final target: ~30-50 lines** (well under 80)

---

## Module Summary

### New Modules (2)

| Module | Purpose | Lines |
|--------|---------|-------|
| `bootstrap/config.rs` | Config loading + merging | ~120 |
| `bootstrap/run.rs` | Daemon orchestration | ~200 |

### Extended Modules (2)

| Module | Addition | Lines |
|--------|----------|-------|
| `cli.rs` | `handle_windows_commands()` | ~30 |
| `tasks/coordinator.rs` | `start_first_boot_task()`, `start_preinstall_handler()` | ~130 |

### Unchanged (use existing)

| Module | Functions |
|--------|-----------|
| `bootstrap/startup.rs` | `connect_docker()`, `init_capabilities()` |
| `bootstrap/server.rs` | `bind_server()`, `run_server()` |
| `bootstrap/router.rs` | `configure()` |
| `tasks/coordinator.rs` | All `start_*` functions |

---

## Validation Criteria

- [ ] main.rs < 80 lines
- [ ] No duplicated implementations
- [ ] All background tasks via coordinator.rs
- [ ] Configuration merging in one place
- [ ] Clear separation: CLI dispatch → config → run → tasks → server
- [ ] All tests pass
- [ ] Boot time unchanged

---

## Benefits

1. **SoC**: main.rs is pure dispatch, bootstrap handles orchestration
2. **DRY**: Eliminate ~300 lines of duplicated code
3. **KISS**: Linear startup flow in run.rs
4. **YAGNI**: No over-abstraction, just proper placement
5. **Testability**: Each phase testable in isolation
6. **Debuggability**: Clear startup sequence in one file

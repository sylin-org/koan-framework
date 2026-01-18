# Source Code Reorganization Proposal

**Status:** Proposal  
**Date:** January 17, 2026  
**Context:** Current src/ structure violates DRY, SoC, and creates confusion with meaningless platform folders

---

## Executive Summary

The current source structure uses platform-based folders (`linux/`, `windows/`) despite all three programs being cross-platform Rust binaries. This creates confusion, violates separation of concerns, and obscures the actual architecture:

- **Moss** (daemon) runs on Linux AND Windows (conditional mDNS)
- **Rake** (CLI) runs on Linux AND Windows (identical code)
- **Lantern** (registry) runs on Linux AND Windows (identical code)
- **Common** (shared library) is NOT Linux-specific

**Proposed action:** Reorganize by **component** (not platform), extract shared modules, eliminate duplication.

---

## Problems with Current Structure

### 1. Misleading Platform Folders

```
src/
  linux/           # MISLEADING: contains cross-platform code
    common/        # Used by Windows builds too
    moss/          # Builds for Windows via cfg gates
  windows/
    garden-rake/   # Identical to Linux rake, no platform difference
  lantern/         # Cross-platform, at top level (inconsistent)
```

**Issues:**
- `linux/common` is used by Windows programs (misnomer)
- `moss` builds for Windows with conditional compilation
- `garden-rake` has zero platform-specific code
- Folder names suggest platform exclusivity that doesn't exist
- Inconsistent: why is `lantern` at top level but `moss` isn't?

### 2. Duplication & DRY Violations

**Discovery logic duplicated:**
- `src/linux/moss/src/discovery.rs` - UDP listener for moss daemon
- `src/windows/garden-rake/src/discovery.rs` - UDP client for rake CLI
- Both implement the same protocol, could share ~60% of code

**Error handling duplication:**
- `moss/src/main.rs` has `error_response()` and `error_response_value()` functions (lines 37-72)
- Should be in shared library with consistent API surface

**Config loading duplication:**
- Each binary implements its own config loading with similar patterns
- `MossConfig::load()`, CLI args parsing - could be unified

**Build scripts:**
- Three separate `build.rs` files with identical logic (capture BUILD_NUMBER)
- Should be consolidated or use workspace-level build utilities

### 3. Lack of Separation of Concerns

**Moss `main.rs` is 2,651 lines:**
- HTTP handlers inline
- Business logic mixed with routing
- Error handling scattered
- State management, metrics, Docker operations all in one file
- Violates single responsibility principle

**Missing domain modules:**
- No clear separation between API layer, domain logic, infrastructure
- HTTP concerns mixed with business rules
- No clear module boundaries

### 4. Poor Discoverability

**New developers face:**
- "Where is the rake source?" → Hidden in `windows/garden-rake`
- "Is moss Linux-only?" → No, but folder name says so
- "What's shared code?" → Could be `common` or any binary
- "What does each module do?" → Must read 2,000+ line files

---

## Proposed Structure

### Flat, Component-Based Organization

```
src/
  moss/              # Daemon (cross-platform)
    Cargo.toml
    build.rs
    src/
      main.rs        # Entry point + router (< 200 lines)
      config.rs      # Configuration loading
      api/           # HTTP handlers (grouped by domain)
        health.rs
        services.rs
        metrics.rs
        jobs.rs
      domain/        # Business logic
        service_manager.rs
        job_orchestrator.rs
        template_engine.rs
      infra/         # Infrastructure adapters
        docker.rs
        mdns.rs
        metrics_collector.rs
      
  rake/              # CLI tool (cross-platform)
    Cargo.toml
    build.rs
    src/
      main.rs        # Entry point + CLI parser (< 200 lines)
      commands/      # Command implementations
        status.rs
        offer.rs
        list.rs
        upgrade.rs
      cache.rs       # Stone cache (hot cache)
      
  lantern/           # Registry daemon (cross-platform)
    Cargo.toml
    build.rs
    src/
      main.rs        # Entry point + router (< 200 lines)
      api/
        registry.rs
        topology.rs
      domain/
        election.rs
        state.rs
      infra/
        persistence.rs
      
  common/            # Shared library (all platforms)
    Cargo.toml
    src/
      lib.rs         # Re-exports
      types/         # Domain types
        service.rs
        hardware.rs
        health.rs
        discovery.rs
        lantern.rs
        compatibility.rs
      net/           # Networking utilities
        discovery_protocol.rs  # Shared UDP discovery
        endpoints.rs
      errors/        # Error handling
        api_error.rs
        error_builder.rs
      utils/         # Utility functions
        formatting.rs
        config.rs
      constants.rs   # Ports, names, paths
      
  build-utils/       # Shared build utilities
    Cargo.toml
    src/
      lib.rs
      build_number.rs  # BUILD_NUMBER capture logic
```

### Key Improvements

**1. Clear Component Boundaries**
- Each binary gets its own top-level folder (moss, rake, lantern)
- No misleading platform folders
- Consistent structure across all components

**2. Separation of Concerns (Layered Architecture)**

```
┌─────────────────────────────────────┐
│  API Layer (HTTP/CLI)               │  ← Thin handlers, routing
├─────────────────────────────────────┤
│  Domain Layer (Business Logic)      │  ← Pure business rules
├─────────────────────────────────────┤
│  Infrastructure Layer (External)    │  ← Docker, mDNS, SQLite
└─────────────────────────────────────┘
```

Each moss module:
- **api/** - HTTP handlers: validate, delegate, respond
- **domain/** - Business logic: orchestration, validation, rules
- **infra/** - External systems: Docker API, mDNS, filesystem

**3. DRY via Shared Modules**

Move to `common/`:
- Discovery protocol (client + server): `net/discovery_protocol.rs`
- Error handling: `errors/api_error.rs` with builder pattern
- Config utilities: `utils/config.rs` with trait-based loading
- Type definitions: `types/*.rs` (already there, but expand)

**4. Smaller, Focused Files**

Target: **< 300 lines per file**, exceptions documented

Current moss `main.rs` (2,651 lines) splits into:
- `main.rs` - 150 lines (setup + router)
- `api/services.rs` - 250 lines (offer, remove, upgrade handlers)
- `api/jobs.rs` - 200 lines (job queue handlers)
- `api/health.rs` - 100 lines (health/metrics handlers)
- `domain/service_manager.rs` - 300 lines (service orchestration)
- `domain/job_orchestrator.rs` - 250 lines (async job handling)
- `domain/template_engine.rs` - 300 lines (template validation/rendering)
- `infra/docker.rs` - 400 lines (Docker API wrapper)
- `infra/mdns.rs` - 100 lines (mDNS announcement)
- `infra/metrics_collector.rs` - 300 lines (metrics gathering)
- `config.rs` - 150 lines (configuration loading)

Total: ~2,500 lines across 11 files (more testable, navigable)

**5. Platform-Specific Code Handling**

Use **conditional compilation at module level**, not folder level:

```rust
// common/src/net/discovery_protocol.rs

#[cfg(not(target_os = "windows"))]
use mdns_sd::{ServiceDaemon, ServiceInfo};

pub struct DiscoveryServer {
    #[cfg(not(target_os = "windows"))]
    mdns: Option<ServiceDaemon>,
}

impl DiscoveryServer {
    #[cfg(not(target_os = "windows"))]
    pub fn announce_mdns(&self, name: &str, port: u16) -> Result<()> {
        // mDNS implementation
    }
    
    #[cfg(target_os = "windows")]
    pub fn announce_mdns(&self, _name: &str, _port: u16) -> Result<()> {
        tracing::debug!("mDNS not available on Windows");
        Ok(())
    }
}
```

**Benefits:**
- Source structure doesn't imply platform exclusivity
- Conditional compilation at call sites (explicit)
- Code colocation (platform variants in same file when small)

---

## Migration Plan

### Phase 0: Impact Assessment (1 hour)

**0.1: Audit embedded content references**

Moss embeds manifests at compile time:
```rust
// src/linux/moss/src/templates.rs:10
static EMBEDDED_MANIFESTS: Dir = include_dir!("$CARGO_MANIFEST_DIR/../../../manifests");
```

**Impact:** Path changes from `src/linux/moss/` → `src/moss/`
- Update relative path: `../../../manifests` → `../../manifests` (one less `..`)

**0.2: Identify all scripts requiring updates**

Scripts that reference source structure:
- `installer/build-dist.ps1` - Workspace root references (no src/ paths - ✅ safe)
- `installer/build-linux.ps1` - Docker volume mount (workspace root - ✅ safe)
- `installer/build-windows.ps1` - Workspace root cargo build (✅ safe)
- `installer/push-moss-to-all-stones.ps1` - Uses binaries, not source (✅ safe)
- `installer/NewStone.ps1` - Uses binaries, not source (✅ safe)

**Finding:** Build scripts use workspace-level cargo commands, NOT explicit src/ paths.
**Result:** ✅ Scripts should continue working after refactor (cargo handles workspace members)

**0.3: Document affected documentation**

Files with hardcoded src/ paths (found via grep):
- `docs/STONE-INSTALLATION-FLOW.md` - Line 227
- `docs/CONTAINER-DIAGNOSTICS-PLAN.md` - Lines 392, 397, 403
- `docs/MOSS-CROSS-PLATFORM-REFACTOR.md` - Multiple references
- `docs/MOSS-CONFIG.md` - Lines 114, 142
- `docs/PORT-ALLOCATION.md` - Lines 35, 36, 70, 100, 121, 139-142
- `docs/decisions/LANTERN-0001-service-registry-architecture.md`
- `docs/proposals/LANTERN-SERVICE-PROPOSAL.md`

**Action:** Phase 7 will update all documentation references

### Phase 1: Preparation (2-4 hours)

**1.1: Create new structure (empty)**
```powershell
# Create new directories
mkdir src/moss/src/{api,domain,infra}
mkdir src/rake/src/commands
mkdir src/common/src/{types,net,errors,utils}
mkdir src/build-utils/src
```

**1.2: Update workspace Cargo.toml**
```toml
[workspace]
members = [
  "src/common",
  "src/moss",
  "src/rake", 
  "src/lantern",
  "src/build-utils",
]
```

**1.3: Set up build-utils crate**
- Extract common build.rs logic
- Publish as workspace dependency

**1.4: Rust Best Practices Setup**

Following Rust ecosystem conventions:

**Module organization (lib.rs as public API):**
```rust
// src/common/src/lib.rs - Re-export public API
pub mod types;
pub mod net;
pub mod errors;
pub mod utils;
pub mod constants;

// Re-export commonly used items
pub use types::{ServiceInfo, ServiceStatus, HardwareCapabilities};
pub use errors::{ApiError, ErrorBuilder};
pub use constants::{ports, names};
```

**Cargo.toml best practices:**
```toml
[package]
edition = "2021"
rust-version = "1.75"  # Specify MSRV (Minimum Supported Rust Version)

[dependencies]
# Group by purpose with comments
# HTTP & Async
axum = { workspace = true }
tokio = { workspace = true }

# Serialization
serde = { workspace = true }
serde_json = { workspace = true }

[dev-dependencies]
# Test-only dependencies separate

[features]
# Optional features (e.g., TLS, mDNS)
mdns = ["dep:mdns-sd"]
```

**Error handling (use thiserror for library errors):**
```rust
// common/src/errors/mod.rs
use thiserror::Error;

#[derive(Error, Debug)]
pub enum ZenError {
    #[error("Service not found: {0}")]
    ServiceNotFound(String),
    
    #[error("Template validation failed: {0}")]
    TemplateValidation(String),
    
    #[error("Docker operation failed: {0}")]
    Docker(#[from] bollard::errors::Error),
}
```

### Phase 2: Move Common Library (2-3 hours)

**2.1: Rename folder**
```powershell
git mv src/linux/common src/common
```

**2.2: Expand common library**

Extract from moss/rake into common:
- Discovery protocol (from moss + rake discovery modules)
- Error builders (from moss error_response functions)
- Config utilities (from MossConfig pattern)

**2.3: Reorganize common internals**
```
src/common/src/
  lib.rs                    # Public API
  types/
    service.rs              # ServiceInfo, ServiceStatus
    hardware.rs             # HardwareCapabilities, MetricsSnapshot
    health.rs               # HealthCheck, DaemonHealthStatus
    discovery.rs            # DiscoveryRequest, DiscoveryResponse
    lantern.rs              # Register*, Resolve*, Lantern*
    compatibility.rs        # CompatibilityRules
  net/
    discovery_protocol.rs   # Unified discovery (UDP client + server)
    endpoints.rs            # Endpoint resolution utilities
  errors/
    api_error.rs            # ApiError, ErrorDetails
    error_builder.rs        # Builder pattern for errors
  utils/
    formatting.rs           # format_bytes, format_uptime
    config.rs               # Config loading trait
  constants.rs              # ports, names modules
```

**2.4: Update imports in moss/rake/lantern**

Prefer flat re-exports for ergonomics (Rust best practice):

```rust
// common/src/lib.rs
pub use types::ServiceInfo;
pub use constants::ports;
pub use errors::ApiError;

// Consumers use simple imports
use zen_common::{ServiceInfo, ports, ApiError};
```

Internal organization remains modular, but public API stays ergonomic.

### Phase 2.5: Standardization Layer (3-4 hours)

**2.5.1: Create constants module hierarchy**

```
src/common/src/constants/
  mod.rs          # Re-export all constants
  timeouts.rs     # All timeout/duration values
  paths.rs        # File/directory paths
  error_codes.rs  # Standardized error codes
  limits.rs       # Retry limits, buffer sizes, etc.
```

**Timeouts with environment variable support:**

```rust
// common/src/constants/timeouts.rs
use std::time::Duration;

// Discovery
pub const DEFAULT_DISCOVERY_UDP_TIMEOUT_SECS: u64 = 3;
pub fn discovery_udp_timeout() -> Duration {
    Duration::from_secs(
        std::env::var("GARDEN_DISCOVERY_TIMEOUT_SECS")
            .ok()
            .and_then(|s| s.parse().ok())
            .unwrap_or(DEFAULT_DISCOVERY_UDP_TIMEOUT_SECS)
    )
}

// First-boot initialization
pub const DEFAULT_FIRST_BOOT_RETRY_ATTEMPTS: u32 = 20;
pub const DEFAULT_FIRST_BOOT_RETRY_DELAY_SECS: u64 = 3;

pub fn first_boot_retry_attempts() -> u32 {
    std::env::var("GARDEN_FIRST_BOOT_RETRY_ATTEMPTS")
        .ok()
        .and_then(|s| s.parse().ok())
        .unwrap_or(DEFAULT_FIRST_BOOT_RETRY_ATTEMPTS)
}

// Cache TTL
pub const DEFAULT_STONE_CACHE_TTL_SECS: u64 = 90;
pub fn stone_cache_ttl() -> Duration {
    Duration::from_secs(
        std::env::var("GARDEN_CACHE_TTL_SECS")
            .ok()
            .and_then(|s| s.parse().ok())
            .unwrap_or(DEFAULT_STONE_CACHE_TTL_SECS)
    )
}

// HTTP request timeouts
pub const DEFAULT_HTTP_TIMEOUT_SECS: u64 = 30;
pub fn http_timeout() -> Duration {
    Duration::from_secs(
        std::env::var("GARDEN_HTTP_TIMEOUT_SECS")
            .ok()
            .and_then(|s| s.parse().ok())
            .unwrap_or(DEFAULT_HTTP_TIMEOUT_SECS)
    )
}
```

**Path constants (platform-aware):**

```rust
// common/src/constants/paths.rs

#[cfg(target_os = "windows")]
pub const TEMPLATES_DIR: &str = "C:\\ProgramData\\ZenGarden\\templates";
#[cfg(not(target_os = "windows"))]
pub const TEMPLATES_DIR: &str = "/etc/zen-garden/templates";

pub const COMPOSE_FILE_NAME: &str = "docker-compose.yml";
pub const COMPATIBILITY_FILE_SUFFIX: &str = "-compatibility.yaml";

// Environment variable override
pub fn templates_dir() -> String {
    std::env::var("GARDEN_TEMPLATES_DIR")
        .unwrap_or_else(|_| TEMPLATES_DIR.to_string())
}
```

**Error codes standardization:**

```rust
// common/src/constants/error_codes.rs
pub mod service {
    pub const NOT_FOUND: &str = "SVC_NOT_FOUND";
    pub const ALREADY_EXISTS: &str = "SVC_ALREADY_EXISTS";
    pub const START_FAILED: &str = "SVC_START_FAILED";
    pub const STOP_FAILED: &str = "SVC_STOP_FAILED";
}

pub mod template {
    pub const INVALID: &str = "TEMPLATE_INVALID";
    pub const NOT_FOUND: &str = "TEMPLATE_NOT_FOUND";
    pub const VALIDATION_FAILED: &str = "TEMPLATE_VALIDATION_FAILED";
}

pub mod docker {
    pub const CONNECTION_FAILED: &str = "DOCKER_CONNECTION_FAILED";
    pub const CONTAINER_ERROR: &str = "DOCKER_CONTAINER_ERROR";
    pub const IMAGE_ERROR: &str = "DOCKER_IMAGE_ERROR";
}

pub mod discovery {
    pub const TIMEOUT: &str = "DISCOVERY_TIMEOUT";
    pub const NO_STONES: &str = "DISCOVERY_NO_STONES";
}
```

**2.5.2: Create standard response wrapper**

```rust
// common/src/responses.rs
use serde::{Deserialize, Serialize};
use chrono::Utc;

#[derive(Debug, Clone, Serialize, Deserialize)]
pub struct ApiResponse<T> {
    pub data: T,
    pub timestamp: String,
    #[serde(skip_serializing_if = "Option::is_none")]
    pub request_id: Option<String>,
}

impl<T> ApiResponse<T> {
    pub fn new(data: T) -> Self {
        Self {
            data,
            timestamp: Utc::now().to_rfc3339(),
            request_id: None,
        }
    }
    
    pub fn with_request_id(mut self, id: String) -> Self {
        self.request_id = Some(id);
        self
    }
}

// Axum IntoResponse implementation
#[cfg(feature = "axum")]
impl<T> axum::response::IntoResponse for ApiResponse<T>
where
    T: Serialize,
{
    fn into_response(self) -> axum::response::Response {
        axum::Json(self).into_response()
    }
}
```

**2.5.3: Create background job abstraction**

```rust
// common/src/jobs.rs
use anyhow::Result;
use std::time::Duration;

#[derive(Debug, Clone)]
pub struct RetryPolicy {
    pub max_attempts: u32,
    pub delay: Duration,
    pub backoff: BackoffStrategy,
}

#[derive(Debug, Clone)]
pub enum BackoffStrategy {
    Fixed,
    Linear,
    Exponential { base: u32 },
}

impl RetryPolicy {
    pub fn fixed(max_attempts: u32, delay: Duration) -> Self {
        Self {
            max_attempts,
            delay,
            backoff: BackoffStrategy::Fixed,
        }
    }
    
    pub fn exponential(max_attempts: u32, base_delay: Duration, base: u32) -> Self {
        Self {
            max_attempts,
            delay: base_delay,
            backoff: BackoffStrategy::Exponential { base },
        }
    }
    
    pub fn delay_for_attempt(&self, attempt: u32) -> Duration {
        match self.backoff {
            BackoffStrategy::Fixed => self.delay,
            BackoffStrategy::Linear => self.delay * attempt,
            BackoffStrategy::Exponential { base } => {
                self.delay * base.pow(attempt)
            }
        }
    }
}

pub async fn retry_with_policy<F, Fut, T>(
    mut operation: F,
    policy: RetryPolicy,
) -> Result<T>
where
    F: FnMut() -> Fut,
    Fut: std::future::Future<Output = Result<T>>,
{
    let mut last_error = None;
    
    for attempt in 1..=policy.max_attempts {
        match operation().await {
            Ok(result) => return Ok(result),
            Err(e) => {
                last_error = Some(e);
                if attempt < policy.max_attempts {
                    let delay = policy.delay_for_attempt(attempt - 1);
                    tracing::debug!(
                        attempt = attempt,
                        max_attempts = policy.max_attempts,
                        delay_ms = delay.as_millis(),
                        "Retrying operation"
                    );
                    tokio::time::sleep(delay).await;
                }
            }
        }
    }
    
    Err(last_error.unwrap_or_else(|| anyhow::anyhow!("All retry attempts failed")))
}

#[cfg(test)]
mod tests {
    use super::*;
    
    #[test]
    fn test_fixed_backoff() {
        let policy = RetryPolicy::fixed(3, Duration::from_secs(1));
        assert_eq!(policy.delay_for_attempt(0), Duration::from_secs(1));
        assert_eq!(policy.delay_for_attempt(1), Duration::from_secs(1));
        assert_eq!(policy.delay_for_attempt(2), Duration::from_secs(1));
    }
    
    #[test]
    fn test_exponential_backoff() {
        let policy = RetryPolicy::exponential(3, Duration::from_secs(1), 2);
        assert_eq!(policy.delay_for_attempt(0), Duration::from_secs(1));
        assert_eq!(policy.delay_for_attempt(1), Duration::from_secs(2));
        assert_eq!(policy.delay_for_attempt(2), Duration::from_secs(4));
    }
}
```

**2.5.4: Update common/src/lib.rs**

```rust
pub mod constants;
pub mod types;
pub mod net;
pub mod errors;
pub mod responses;
pub mod jobs;
pub mod utils;

// Re-export commonly used items
pub use constants::{ports, names};
pub use errors::ApiError;
pub use responses::ApiResponse;
pub use types::{ServiceInfo, ServiceStatus, HardwareCapabilities};
pub use jobs::{RetryPolicy, BackoffStrategy, retry_with_policy};
```

**2.5.5: Add chrono dependency**

```toml
# common/Cargo.toml
[dependencies]
chrono = "0.4"  # For timestamp generation
```

### Phase 3: Refactor Moss (8-10 hours)

**3.1: Move and rename**
```powershell
git mv src/linux/moss src/moss
```

**3.2: Fix embedded manifest path**

Update template loader:
```rust
// Before: src/linux/moss/src/templates.rs:10
static EMBEDDED_MANIFESTS: Dir = include_dir!("$CARGO_MANIFEST_DIR/../../../manifests");

// After: src/moss/src/templates.rs:10
static EMBEDDED_MANIFESTS: Dir = include_dir!("$CARGO_MANIFEST_DIR/../../manifests");
```

**3.3: Split main.rs into modules (following Rust conventions)**

**Before:** `main.rs` (2,651 lines)

**After (Rust idiomatic structure):**

```
src/moss/src/
  main.rs           # 100 lines - setup, tokio::main, router
  lib.rs            # 50 lines - re-export modules for testing
  config.rs         # 150 lines - MossConfig, loading
  state.rs          # 100 lines - AppState definition
  api/
    mod.rs          # Re-export handlers
    health.rs       # 100 lines - /health, /metrics
    services.rs     # 250 lines - /offerings, /services
    jobs.rs         # 200 lines - /jobs endpoints
  domain/
    mod.rs          # Re-export domain logic
    service_manager.rs   # 300 lines - service orchestration
    job_orchestrator.rs  # 250 lines - async job handling
    template_engine.rs   # 300 lines - template validation
  infra/
    mod.rs          # Re-export infrastructure
    docker.rs       # 400 lines - Docker API wrapper
    mdns.rs         # 100 lines - mDNS announcement
    metrics.rs      # 300 lines - metrics gathering
```

**Why lib.rs + main.rs:**
- `lib.rs` allows integration tests to access internal modules
- `main.rs` stays minimal (setup + tokio runtime)
- Rust convention: library logic in lib.rs, entry point in main.rs

**3.4: Refactor approach (Rust best practices)**

1. **Create module skeleton with public interfaces:**
```rust
// src/moss/src/domain/service_manager.rs
pub struct ServiceManager {
    docker: Arc<DockerManager>,
    templates: Arc<TemplateLoader>,
}

impl ServiceManager {
    pub async fn offer_service(&self, offering: &str) -> Result<ServiceInfo> {
        // Business logic here
    }
}
```

2. **Move functions maintaining public API stability**
3. **Use traits for testability:**
```rust
// infra/docker.rs
#[cfg_attr(test, mockall::automock)]
pub trait DockerClient {
    async fn start_container(&self, config: ContainerConfig) -> Result<String>;
}
```

4. **Replace error_response with error types:**
```rust
// Before
return error_response(StatusCode::BAD_REQUEST, "ERR001", "Invalid".into(), None);

// After (using thiserror + axum IntoResponse)
return Err(ZenError::InvalidTemplate("Invalid YAML".into()));
```

**3.5: Update Cargo.toml paths**
```toml
[dependencies]
zen-common = { path = "../common" }
build-utils = { path = "../build-utils" }

[dev-dependencies]
mockall = "0.12"  # For mocking Docker/mDNS in tests
```

**3.6: Add module documentation (Rust rustdoc standards)**

```rust
//! # Service Manager
//!
//! Orchestrates service lifecycle operations.
//!
//! ## Responsibilities
//! - Template validation and rendering
//! - Docker Compose generation
//! - Service state transitions
//!
//! ## Example
//! ```no_run
//! let manager = ServiceManager::new(docker, templates);
//! let service = manager.offer_service("mongodb").await?;
//! ```
```

### Phase 4: Refactor Rake (3-4 hours)

**4.1: Move and rename**
```powershell
git mv src/windows/garden-rake src/rake
```

**4.2: Split into command modules (Rust CLI best practices)**

**Before:** `main.rs` (1,449 lines)

**After (idiomatic Rust CLI structure):**

```
src/rake/src/
  main.rs           # 100 lines - clap CLI setup, dispatch
  lib.rs            # 50 lines - re-export for testing
  commands/
    mod.rs          # Re-export command handlers
    status.rs       # 100 lines - status command
    offer.rs        # 150 lines - offer command
    list.rs         # 100 lines - list command
    upgrade.rs      # 150 lines - upgrade command
    rest.rs         # 80 lines - rest command
    wake.rs         # 80 lines - wake command
    place.rs        # 100 lines - place command (scaffolding)
    observe.rs      # 150 lines - observe command
  client/
    mod.rs          # HTTP client wrapper
    connection.rs   # Connection context, endpoint resolution
  cache.rs          # 150 lines - Stone cache (hot cache)
```

**Why this structure:**
- Each command = one file (easy to find, modify)
- `client/` module for HTTP operations (SoC)
- `cache.rs` for caching logic (not mixed with commands)
- Commands return `Result<()>` for consistent error handling

**Command pattern:**
```rust
// commands/offer.rs
pub async fn execute(offering: String, at: Option<String>) -> Result<()> {
    let client = reqwest::Client::new();
    let endpoint = resolve_endpoint(&client, at).await?;
    
    // Command logic
    let response = client
        .post(format!("{}/offerings", endpoint))
        .json(&json!({ "offering": offering }))
        .send()
        .await?;
    
    // Handle response, print output
    Ok(())
}
```

**4.3: Move discovery logic to common**

Discovery functionality moves to shared library:

```rust
// common/src/net/discovery_protocol.rs

/// UDP-based discovery (client side)
pub fn discover_moss_udp() -> Result<String> { 
    // Client broadcast + receive (from rake)
}

/// UDP-based discovery (server side)
pub async fn udp_discovery_listener(
    stone_name: String,
    endpoint: String,
) -> Result<()> {
    // Server listen + respond (from moss)
}

pub fn discover_all_moss_udp(timeout: Duration) -> Result<Vec<String>> {
    // Multi-response collection
}
```

**Why consolidate:** Both moss and rake implement the same protocol. Shared module:
- Eliminates duplication (DRY)
- Ensures protocol consistency
- Makes protocol changes affect both sides atomically
- Easier to test (one test suite)

**4.4: Update Cargo.toml**
```toml
[dependencies]
zen-common = { path = "../common" }
clap = { workspace = true, features = ["derive", "cargo"] }  # cargo for version from Cargo.toml
reqwest = { workspace = true }
tokio = { workspace = true, features = ["rt-multi-thread", "macros"] }

[dev-dependencies]
assert_cmd = "2.0"  # CLI testing
predicates = "3.0"  # Output assertions
```

### Phase 5: Update Lantern (1-2 hours)

**5.1: Already at correct location** (`src/lantern`)

**5.2: Refactor internal structure**

Move to layered approach:
- `api/` - HTTP handlers
- `domain/` - Election, state management
- `infra/` - SQLite persistence

**5.3: Update dependencies**
```toml
[dependencies]
zen-common = { path = "../common" }
build-utils = { path = "../build-utils" }
```

### Phase 6: Clean Up (1-2 hours)

**6.1: Delete old folders**
```powershell
Remove-Item src/linux -Recurse
Remove-Item src/windows -Recurse
```

**6.2: Update all import paths**

Use workspace-level find/replace:
```powershell
# Update paths in Cargo.toml files
# Update imports in all .rs files
# Update documentation references
```

**Note:** Most imports use workspace dependency names (`zen-common`), not paths. 
**Impact:** Minimal - Cargo.toml [dependencies] sections already use relative paths.

**6.3: Verify build scripts still work**

Build scripts use **workspace-level** cargo commands (no hardcoded src/ paths):

```powershell
# installer/build-linux.ps1
cargo build --release --bin garden-moss --bin garden-lantern --bin garden-rake

# installer/build-windows.ps1  
cargo build --release --bin garden-moss --bin garden-rake --target x86_64-pc-windows-msvc
```

**Result:** ✅ Scripts work unchanged (cargo resolves workspace members automatically)

**Only change needed:** Update embedded manifest path in templates.rs (already done in Phase 3.2)

**6.4: Verify builds**
```powershell
cargo clean
cargo build --workspace --release
cargo test --workspace
cargo clippy --workspace --all-targets -- -D warnings
cargo fmt --all -- --check
```

**Rust tooling verification:**
```powershell
# Check for unused dependencies
cargo machete

# Security audit
cargo audit

# Check for outdated dependencies
cargo outdated
```

### Phase 7: Documentation (2-3 hours)

**7.1: Update architecture docs**

Files to update (found via grep):
- `docs/TECHNICAL-SPEC.md` - Update structure diagrams
- `docs/README.md` - Update component descriptions
- `docs/STONE-INSTALLATION-FLOW.md` - Line 227 (main.rs reference)
- `docs/CONTAINER-DIAGNOSTICS-PLAN.md` - Lines 392, 397, 403 (module paths)
- `docs/MOSS-CROSS-PLATFORM-REFACTOR.md` - Multiple src/ references
- `docs/MOSS-CONFIG.md` - Lines 114, 142 (main.rs and Cargo.toml links)
- `docs/PORT-ALLOCATION.md` - Lines 35-142 (multiple file references)
- `docs/decisions/LANTERN-0001-service-registry-architecture.md` - src/lantern paths
- `docs/proposals/LANTERN-SERVICE-PROPOSAL.md` - src/lantern paths
- `docs/architecture/*` - Update any architecture docs

**Automated find/replace for docs:**
```powershell
# Regex replacements
src/linux/moss     → src/moss
src/linux/common   → src/common
src/windows/garden-rake → src/rake
```

**7.2: Add module-level documentation (Rust rustdoc)**

Each module gets rustdoc comments:
```rust
//! # Module Name
//!
//! ## Purpose
//! What this module does
//!
//! ## Responsibilities
//! - Responsibility 1
//! - Responsibility 2
//!
//! ## Dependencies
//! - Dependency on X for Y
//!
//! ## Example
//! ```no_run
//! use zen_common::ServiceInfo;
//! let info = ServiceInfo { ... };
//! ```
```

**Generate rustdoc:**
```powershell
cargo doc --workspace --no-deps --open
```

**7.3: Create ARCHITECTURE.md in zen-garden root**

Document the layered architecture:
- Component boundaries (moss, rake, lantern, common)
- Dependency rules (layers can only depend downward)
- Platform-specific handling conventions (cfg attributes)
- Module size guidelines (< 300 lines, < 500 hard limit)
- Testing patterns (unit, integration, E2E)
- Embedded content patterns (manifests, templates)

**7.4: Update BUILD.md (or create if missing)**

Document build system:
- Workspace structure
- Build scripts (build-dist.ps1, build-linux.ps1, build-windows.ps1)
- Build number system (timestamp-based)
- Cross-compilation strategy (Docker for Linux, native for Windows)
- Embedded manifests (compile-time inclusion)
- CI/CD considerations

**7.5: Update CONTRIBUTING.md**

Add Rust-specific guidelines:
```markdown
### Code Organization

- Follow Rust API guidelines: https://rust-lang.github.io/api-guidelines/
- Keep modules < 300 lines (hard limit: 500)
- Use `cargo fmt` before committing
- Run `cargo clippy` and fix warnings
- Write rustdoc for public items
- Use `thiserror` for errors, `anyhow` for applications
- Prefer traits over concrete types for testability

### Testing

- Unit tests in same file as code (Rust convention)
- Integration tests in `tests/` directory
- Use `mockall` for mocking infrastructure
- Run `cargo test --workspace` before PR

### Platform-Specific Code

- Use `cfg` attributes, not folder structure
- Test on both Windows and Linux
- Document platform limitations in rustdoc
```

---

## Testing Strategy

### During Migration

**After each phase:**
1. Run `cargo build --workspace` - ensure compilation
2. Run `cargo test --workspace` - ensure tests pass
3. Run `cargo clippy --workspace` - check warnings
4. Manual smoke test: build binaries, test basic commands

**Specific tests:**

**Phase 2 (Common):**
- Verify all type serialization tests pass
- Test discovery protocol functions (unit tests)
- Test error builders produce correct JSON

**Phase 3 (Moss):**
- Test `/health` endpoint
- Test `POST /offerings` (single service)
- Test service lifecycle (offer → running → remove)
- Test metrics collection

**Phase 4 (Rake):**
- Test `garden-rake status`
- Test `garden-rake list`
- Test `garden-rake offer mongodb`
- Test auto-discovery vs explicit --at

**Phase 5 (Lantern):**
- Test registry operations
- Test election logic
- Test topology queries

### Post-Migration Testing

**Integration tests:**
- End-to-end: rake → moss → docker (offer service)
- End-to-end: lantern registration → query
- Cross-platform: build on Windows, test on Linux (and vice versa)

**Regression tests:**
- Use existing test scripts (if any)
- Test all documented workflows in DEPLOYMENT-GUIDE
- Test installer scripts still work

---

## Risk Mitigation

### High-Risk Areas

**1. Embedded manifest path changes**
- **Risk:** Templates fail to load after moving moss from src/linux/moss → src/moss
- **Manifestation:** Runtime error: "Template not found" or compile error
- **Mitigation:** 
  - Update `include_dir!` path in templates.rs (Phase 3.2)
  - Test template loading immediately after path change
  - Verify with `cargo test` (template tests will fail if path wrong)
  - Keep manifests/ at workspace root (unchanged)

**2. Discovery protocol refactoring**
- **Risk:** Breaking UDP communication between rake/moss
- **Mitigation:** 
  - Keep old discovery modules temporarily in both projects
  - Add feature flag for new/old discovery during transition
  - Test on multiple networks before removing old code
  - Protocol is wire format (JSON) - refactoring internal code doesn't affect compatibility

**3. Import path changes**
- **Risk:** Missing updates cause compilation failures
- **Mitigation:**
  - Most imports use workspace dependency names (zen-common) - unchanged
  - Cargo.toml paths are relative - update systematically
  - Compiler will catch all broken imports (Rust type checking)
  - Update one component at a time, compile after each

**4. Build scripts and embedded content**
- **Risk:** Scripts fail to find source or cargo fails to build
- **Mitigation:**
  - Build scripts use workspace-level `cargo build` (no explicit src/ paths)
  - Cargo.toml workspace members list updated in Phase 1.2
  - Embedded content uses CARGO_MANIFEST_DIR (relative from crate root)
  - Test builds after EVERY phase (compile early, compile often)

**5. Documentation becomes outdated**
- **Risk:** Docs reference old paths, confusing developers
- **Mitigation:**
  - Automated grep to find all src/ references
  - Regex find/replace for path updates
  - Manual review of updated docs
  - Add CI check for broken doc links (future)

**6. Tests reference old structure**
- **Risk:** Integration tests fail due to path assumptions
- **Mitigation:**
  - Rust tests use module paths, not file paths
  - Integration tests in tests/ use public API (unchanged)
  - Run `cargo test --workspace` after each phase
  - Update any tests that use file I/O or path manipulation

### Rollback Strategy

**Git branch strategy:**
```
main (stable)
  └─ refactor/src-reorganization (working branch)
       ├─ refactor/phase1-prep
       ├─ refactor/phase2-common
       ├─ refactor/phase3-moss
       └─ ...
```

**Each phase gets a commit:**
- Commit = working state (compiles + tests pass)
- Tag phases: `refactor-phase-1-complete`
- Easy rollback: `git reset --hard refactor-phase-N-complete`

**Parallel old/new (if needed):**
- Keep old folders until new structure fully validated
- Use feature flags if necessary during transition
- Final commit: delete old folders + verify

---

## Rust Best Practices & Principles

### Separation of Concerns (SoC)

**Layered architecture enforced via modules:**

```rust
// api/ - Thin handlers (validate, delegate, respond)
async fn offer_service(
    State(service_mgr): State<Arc<ServiceManager>>,
    Json(req): Json<OfferRequest>,
) -> Result<Json<ServiceInfo>, ApiError> {
    service_mgr.offer(&req.offering).await
        .map(Json)
        .map_err(ApiError::from)
}

// domain/ - Business logic (pure, testable)
impl ServiceManager {
    pub async fn offer(&self, offering: &str) -> Result<ServiceInfo> {
        let template = self.templates.load(offering)?;
        let config = self.validate_template(&template)?;
        self.orchestrator.deploy(config).await
    }
}

// infra/ - External systems (mockable traits)
#[cfg_attr(test, mockall::automock)]
pub trait DockerClient {
    async fn create_container(&self, config: Config) -> Result<String>;
}
```

**Benefits:**
- API layer doesn't know about Docker
- Domain logic doesn't know about HTTP
- Infrastructure is swappable (mock in tests)

### DRY (Don't Repeat Yourself)

**Current violations → Solutions:**

| Duplication | Current State | After Refactor |
|-------------|---------------|----------------|
| Discovery protocol | 2 files (moss, rake) | 1 module (common/net) |
| Error handling | error_response in each binary | ApiError in common |
| Config loading | MossConfig pattern repeated | ConfigLoader trait in common |
| Build scripts | 3 identical build.rs | 1 build-utils crate |
| Formatting helpers | format_bytes in common, elsewhere? | Only in common/utils |

**Rust-specific DRY patterns:**

```rust
// Shared trait for config loading
pub trait Config: Sized {
    fn load() -> Result<Self>;
    fn from_file(path: &Path) -> Result<Self>;
}

// Each binary implements for its config type
impl Config for MossConfig {
    fn load() -> Result<Self> {
        // Platform-specific paths via cfg
    }
}
```

### YAGNI (You Aren't Gonna Need It)

**What NOT to add during refactor:**

❌ **Don't add:**
- Abstract factory patterns (just use functions)
- Complex dependency injection framework (Arc<T> is sufficient)
- ORM layer (bollard Docker client is fine)
- Sophisticated plugin system (templates are sufficient)
- Generic repository pattern (CRUD is simple)
- Async trait objects everywhere (use concrete types when possible)

✅ **Do add:**
- Traits ONLY where mocking is needed (DockerClient, mDNS)
- Error types for domain errors (thiserror)
- Module boundaries matching actual concerns
- Documentation explaining WHY, not WHAT

**Rust YAGNI guidelines:**

```rust
// ❌ Over-engineering
pub trait ServiceRepository: Send + Sync {
    async fn find_by_id(&self, id: &str) -> Result<Option<Service>>;
    async fn save(&self, service: Service) -> Result<()>;
}

pub struct DockerServiceRepository { /* ... */ }
impl ServiceRepository for DockerServiceRepository { /* ... */ }

// ✅ YAGNI-compliant (direct, simple)
pub struct ServiceManager {
    docker: Arc<DockerManager>,
}

impl ServiceManager {
    pub async fn get_service(&self, id: &str) -> Result<ServiceInfo> {
        self.docker.inspect_container(id).await
    }
}
```

**When to add abstractions:**
- **For testing:** Use traits to mock external systems (Docker, network, filesystem)
- **For platform differences:** Use cfg attributes and conditional compilation
- **For reusability:** Extract to common/ only after 3rd use (Rule of Three)

**When NOT to add abstractions:**
- **Speculative flexibility:** "We might swap Docker for Podman later" (YAGNI)
- **Premature generalization:** One implementation doesn't need a trait
- **"Clean Architecture" dogma:** Rust's type system provides safety without heavy abstraction

### Cargo Workspace Best Practices

**Workspace-level dependency versions:**

```toml
# Workspace Cargo.toml
[workspace.dependencies]
tokio = { version = "1.35", features = ["full"] }
serde = { version = "1.0", features = ["derive"] }
anyhow = "1.0"
thiserror = "1.0"

# Member Cargo.toml
[dependencies]
tokio = { workspace = true }
serde = { workspace = true }
```

**Benefits:**
- Single version across workspace
- Update once, affects all crates
- Faster compilation (shared build cache)

**Dependency guidelines:**
- Keep `workspace = true` for shared deps
- Use specific features only where needed
- Separate `[dev-dependencies]` (test-only)
- Avoid transitive dependency conflicts

### Module Organization (Rust Idioms)

**Public API in lib.rs:**

```rust
// src/common/src/lib.rs
pub mod types;
pub mod net;
pub mod errors;
pub mod utils;

// Re-export common items for ergonomics
pub use types::{ServiceInfo, ServiceStatus};
pub use errors::ApiError;
pub use constants::ports;
```

**Consumers use simple imports:**
```rust
// Instead of: use zen_common::types::ServiceInfo;
use zen_common::ServiceInfo;  // ✅ Ergonomic
```

**Module privacy:**
```rust
// Internal helper (not re-exported)
mod internal {
    pub(crate) fn helper() { /* ... */ }
}

// Public API
pub struct ServiceManager { /* ... */ }
```

**File structure matches module tree:**
```
src/moss/src/
  lib.rs         # pub mod api; pub mod domain;
  main.rs        # use moss::{api, domain};
  api/
    mod.rs       # pub mod health; pub mod services;
    health.rs
    services.rs
  domain/
    mod.rs       # pub mod service_manager;
    service_manager.rs
```

### Error Handling (Rust Patterns)

**Library errors use thiserror:**

```rust
// common/src/errors/mod.rs
use thiserror::Error;

#[derive(Error, Debug)]
pub enum ZenError {
    #[error("Service {0} not found")]
    ServiceNotFound(String),
    
    #[error("Template validation failed: {0}")]
    TemplateValidation(String),
    
    #[error("Docker error: {0}")]
    Docker(#[from] bollard::errors::Error),
    
    #[error("I/O error: {0}")]
    Io(#[from] std::io::Error),
}
```

**Application errors use anyhow:**

```rust
// main.rs or top-level handlers
use anyhow::{Context, Result};

async fn main_logic() -> Result<()> {
    let config = MossConfig::load()
        .context("Failed to load moss configuration")?;
    
    start_server(config).await
        .context("Server startup failed")?;
    
    Ok(())
}
```

**HTTP errors implement IntoResponse:**

```rust
// api/errors.rs
impl IntoResponse for ZenError {
    fn into_response(self) -> Response {
        let (status, error_code) = match self {
            ZenError::ServiceNotFound(_) => (StatusCode::NOT_FOUND, "SVC_NOT_FOUND"),
            ZenError::TemplateValidation(_) => (StatusCode::BAD_REQUEST, "INVALID_TEMPLATE"),
            ZenError::Docker(_) => (StatusCode::INTERNAL_SERVER_ERROR, "DOCKER_ERROR"),
            _ => (StatusCode::INTERNAL_SERVER_ERROR, "INTERNAL_ERROR"),
        };
        
        let body = json!({
            "error": {
                "code": error_code,
                "message": self.to_string(),
            }
        });
        
        (status, Json(body)).into_response()
    }
}
```

### Testing Patterns

**Unit tests in same file:**

```rust
// domain/service_manager.rs
pub struct ServiceManager { /* ... */ }

impl ServiceManager {
    pub fn validate_template(&self, tmpl: &str) -> Result<()> {
        // validation logic
    }
}

#[cfg(test)]
mod tests {
    use super::*;
    
    #[test]
    fn test_validate_template_rejects_invalid_yaml() {
        let mgr = ServiceManager::new();
        assert!(mgr.validate_template("not: yaml: :::").is_err());
    }
}
```

**Integration tests in tests/ directory:**

```
tests/
  integration/
    moss_api_test.rs      # Test full HTTP API
    rake_commands_test.rs # Test CLI commands
  common/
    mod.rs               # Shared test utilities
```

**Mock external dependencies:**

```rust
// infra/docker.rs
#[cfg_attr(test, mockall::automock)]
pub trait DockerClient {
    async fn create_container(&self, cfg: Config) -> Result<String>;
}

// In tests
#[tokio::test]
async fn test_service_deployment() {
    let mut mock_docker = MockDockerClient::new();
    mock_docker
        .expect_create_container()
        .returning(|_| Ok("container_id".into()));
    
    let mgr = ServiceManager::new(Arc::new(mock_docker));
    let result = mgr.deploy_service("mongodb").await;
    assert!(result.is_ok());
}
```

---

## Success Criteria

### Measurable Outcomes

**Code organization:**
- ✅ No files > 500 lines (target: < 300)
- ✅ Clear separation: API / Domain / Infrastructure
- ✅ No platform folders (`linux/`, `windows/`)
- ✅ Component names match binary names (`moss/`, `rake/`, `lantern/`)

**DRY compliance:**
- ✅ Discovery protocol in single module (not duplicated)
- ✅ Error handling in common library (not per-binary)
- ✅ Config loading uses shared trait (not reimplemented)
- ✅ Build number logic in build-utils (not per-binary)

**Discoverability:**
- ✅ New developer can find rake source in < 10 seconds
- ✅ Module names clearly indicate purpose
- ✅ Folder structure matches documentation diagrams
- ✅ Platform-specific code has explicit cfg annotations

**Functional integrity:**
- ✅ All existing tests pass
- ✅ Binaries build for Windows and Linux
- ✅ End-to-end workflows still function
- ✅ No regressions in installer/deployment scripts

---

## Timeline Estimate

| Phase | Duration | Cumulative |
|-------|----------|------------|
| 0. Impact Assessment | 1 hour | 1 hour |
| 1. Preparation | 3-4 hours | 5 hours |
| 2. Common Library | 3-4 hours | 9 hours |
| **2.5. Standardization Layer** | **3-4 hours** | **13 hours** |
| 3. Moss Refactor | 8-10 hours | 23 hours |
| 4. Rake Refactor | 4-5 hours | 28 hours |
| 5. Lantern Update | 2-3 hours | 31 hours |
| 6. Clean Up | 2-3 hours | 34 hours |
| 7. Documentation | 3-4 hours | 38 hours |
| **Testing & Validation** | 6-8 hours | **44-46 hours** |

**Total: 44-46 hours** (5.5-6 days of focused work)

**Phase 2.5 additions:**
- Constants hierarchy (timeouts, paths, error_codes, limits)
- Environment variable support (GARDEN_* prefix)
- Standard response wrappers (ApiResponse)
- Background job abstraction (RetryPolicy, retry_with_policy)
- Eliminates all magic numbers and strings

**Recommendation:** Execute over 1-2 weeks with validation pauses between phases.

---

## Long-Term Maintenance Benefits

### Onboarding Time Reduction

**Before:** 
- "Where's the code?" → Search through platform folders
- "Why is common in linux/?" → Confusing misnomer
- "What does main.rs do?" → Read 2,651 lines

**After:**
- "Where's the code?" → `src/moss`, `src/rake`, `src/lantern` (obvious)
- "Where's shared code?" → `src/common` (explicit)
- "What does main.rs do?" → Read 150 lines (setup + router)

**Estimated impact:** 50% reduction in onboarding time for new contributors

### Maintenance Velocity

**Before:**
- Change discovery protocol → Update 2+ files (duplication)
- Add API endpoint → Find line 1,500 in 2,651-line file
- Fix error handling → Change error_response function + all call sites

**After:**
- Change discovery protocol → Update single module (DRY)
- Add API endpoint → Create new handler file in `api/` (SoC)
- Fix error handling → Update error builder (common library)

**Estimated impact:** 30% faster feature development, 40% faster bug fixes

### Testing & Refactoring

**Before:**
- Hard to test business logic (mixed with HTTP handlers)
- Hard to mock Docker (tightly coupled)
- Large files discourage refactoring (high change risk)

**After:**
- Domain layer testable in isolation (pure functions)
- Infrastructure mockable via traits (clean boundaries)
- Small files encourage refactoring (low change risk)

**Estimated impact:** 2x increase in test coverage feasibility

---

## Alternatives Considered

### Alternative A: Keep Platform Folders, Refactor Internally

**Approach:** Keep `linux/moss`, `windows/garden-rake` but improve file organization within each

**Pros:**
- Smaller change scope
- Less path updating needed

**Cons:**
- Doesn't fix misnomer problem (folders still misleading)
- Doesn't address duplication (discovery still in 2 places)
- Still confusing for new developers

**Verdict:** ❌ Rejected - doesn't solve core problems

### Alternative B: Monorepo with Separate Repos

**Approach:** Move each component to separate git repository

**Pros:**
- True component independence
- Separate versioning

**Cons:**
- Massive change (git history split)
- Complicates cross-component changes
- Overkill for current project size

**Verdict:** ❌ Rejected - too aggressive for current needs

### Alternative C: Gradual Refactoring (No Moves)

**Approach:** Improve code organization without moving folders

**Pros:**
- Zero path changes
- Lower risk

**Cons:**
- Doesn't fix confusing folder names
- Platform folders still misleading
- Leaves structural debt unaddressed

**Verdict:** ❌ Rejected - kicks problem down the road

---

## Recommendation

✅ **Proceed with proposed flat, component-based reorganization**

**Rationale:**
1. Solves all identified problems (misnaming, duplication, poor SoC)
2. Aligns structure with actual architecture (cross-platform components)
3. Reasonable effort (30-32 hours) for high long-term value
4. Clear migration plan with rollback strategy
5. Measurable success criteria

**Next steps:**
1. Review this proposal with stakeholders
2. Schedule 1-2 week implementation window
3. Create `refactor/src-reorganization` branch
4. Execute phases 1-7 with validation checkpoints
5. Merge to main after full validation

---

## Questions & Answers

**Q: Why not keep platform folders as "this is where it typically runs"?**
A: Because it's factually incorrect and confusing. Moss runs on Windows. Rake runs on Linux. Common is used by Windows. Platform-specific code should use `cfg` attributes, not folder organization.

**Q: Won't this break existing build scripts?**
A: No. Build scripts use workspace-level `cargo build --bin garden-moss` commands, not explicit src/ paths. Cargo resolves workspace members automatically. Only change needed: embedded manifest path in templates.rs (one line).

**Q: Is 40-42 hours worth it for "just" reorganization?**
A: Yes. This is structural debt that compounds over time. Current confusion already slows development. Without fixing this, every new feature pays the "where does this go?" tax. 40 hours now saves hundreds of hours over the project lifetime. Plus: better testability, clearer onboarding, easier maintenance.

**Q: What if we introduce a bug during refactoring?**
A: Testing strategy mitigates this: (1) Test after each phase, (2) Keep old code temporarily, (3) Git history preserves working states, (4) Smoke tests before finalizing. The refactoring is mostly moving code, not changing logic. Rust's type system catches most errors at compile time.

**Q: Can we do this incrementally over months?**
A: Not recommended. Half-refactored state creates confusion (which structure to use?). Better to dedicate 1-2 weeks, execute fully, move forward with clean structure. Incremental approach prolongs confusion period and risks merge conflicts.

**Q: Why separate build-utils? Seems like overkill.**
A: Three binaries currently duplicate build.rs logic (BUILD_NUMBER capture). Extracting to build-utils: (1) eliminates duplication, (2) documents the pattern, (3) makes future binaries trivial to add. Low effort, high reusability gain. This is DRY in action.

**Q: What about embedded manifests breaking?**
A: Manifests stay at workspace root (unchanged location). Only the relative path in `include_dir!` macro changes (one less `..`). Compiler will catch this immediately if wrong. Test template loading right after the change.

**Q: Will documentation get out of sync?**
A: Phase 7 includes automated grep/replace for all src/ references in docs. Manual review ensures accuracy. Future: Add CI check for broken doc links. Most critical: rustdoc stays in sync with code (living documentation).

**Q: Why add lib.rs if we just have binaries?**
A: Rust best practice: lib.rs contains testable logic, main.rs is entry point. Benefits: (1) Integration tests can import modules, (2) Business logic testable without running full binary, (3) Potential code reuse between binaries, (4) Standard Rust pattern developers expect.

**Q: Isn't thiserror overkill for simple errors?**
A: No. thiserror eliminates error boilerplate and provides: (1) Automatic Display impl, (2) Error trait impl, (3) Source error chain, (4) From conversions. Zero runtime cost (macro). Alternative is manual impl for every error type (more code, more mistakes).

**Q: Why not add more abstractions for "clean architecture"?**
A: YAGNI principle. Rust's type system provides safety without heavy abstraction layers. Add traits only where needed (mocking, platform differences). Over-abstraction hurts: (1) Harder to understand, (2) More code to maintain, (3) Compile time overhead, (4) False sense of flexibility. Prefer concrete types, add abstraction when actually needed.

**Q: Won't workspace dependencies cause version conflicts?**
A: Opposite - workspace dependencies PREVENT conflicts. All crates use same version. Cargo deduplicates in build graph. Single source of truth. Update once affects all. This is Cargo best practice for monorepos.

**Q: Do we need to update Dockerfile.build?**
A: No. Dockerfile copies entire workspace root and runs `cargo build`. Workspace member list in Cargo.toml determines what builds. As long as Cargo.toml is updated (Phase 1.2), Docker builds work unchanged.

---

**Author:** GitHub Copilot  
**Reviewer:** [Pending]  
**Approval:** [Pending]

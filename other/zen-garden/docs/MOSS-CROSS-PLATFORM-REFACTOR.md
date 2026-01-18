# Moss Cross-Platform Refactoring Plan

**Goal:** Single codebase compiles for both Linux and Windows using conditional compilation

**Current State:** `src/linux/moss/` (Linux-only)  
**Desired State:** `src/moss/` (cross-platform with `#[cfg]` attributes)

---

## Current Structure Analysis

```
src/
├── linux/
│   ├── common/         (shared)
│   └── moss/           (Linux-specific path)
│       ├── Cargo.toml
│       └── src/
│           ├── main.rs      (95% shared)
│           ├── docker.rs    (90% shared - socket vs named pipe)
│           ├── mdns.rs      (already has #[cfg] guards)
│           ├── discovery.rs (100% shared - UDP)
│           ├── metrics.rs   (95% shared - sysinfo cross-platform)
│           └── templates.rs (100% shared)
└── windows/
    └── garden-rake/    (cross-platform client, already works)
```

**Key Finding:** Moss modules already mostly platform-agnostic. Only 2-3 areas need conditional compilation.

---

## Proposed Structure

```
src/
├── common/             (zen-common crate, unchanged)
├── moss/               (unified, replaces src/linux/moss)
│   ├── Cargo.toml      (with platform-specific deps)
│   └── src/
│       ├── main.rs          (cross-platform entry point)
│       ├── docker.rs        (#[cfg] for socket vs named pipe)
│       ├── mdns.rs          (already has #[cfg] guards ✅)
│       ├── discovery.rs     (100% shared ✅)
│       ├── metrics.rs       (95% shared, minor #[cfg])
│       ├── templates.rs     (100% shared ✅)
│       ├── service.rs       (NEW: platform-specific service lifecycle)
│       └── gpu.rs           (NEW: GPU detection for Windows)
└── garden-rake/        (client tool, already cross-platform ✅)
```

---

## Module-by-Module Refactoring

### 1. docker.rs - Docker Connection

**Current (Linux-only):**
```rust
pub fn new() -> Result<Self> {
    let docker = Docker::connect_with_socket_defaults()
        .context("Failed to connect to Docker daemon via Unix socket")?;
    Ok(Self { docker })
}
```

**Refactored (cross-platform):**
```rust
pub fn new() -> Result<Self> {
    #[cfg(not(target_os = "windows"))]
    let docker = Docker::connect_with_socket_defaults()
        .context("Failed to connect to Docker daemon via Unix socket")?;
    
    #[cfg(target_os = "windows")]
    let docker = Docker::connect_with_named_pipe_defaults()
        .context("Failed to connect to Docker daemon via named pipe")?;
    
    Ok(Self { docker })
}
```

**Impact:** Single function with 2-line platform guard. Everything else unchanged.

---

### 2. mdns.rs - Already Cross-Platform ✅

**Current state:**
```rust
#[cfg(not(target_os = "windows"))]
pub fn announce_moss(stone_name: &str, port: u16) -> anyhow::Result<mdns_sd::ServiceDaemon> {
    // Linux mDNS implementation
}

#[cfg(target_os = "windows")]
pub fn announce_moss(_stone_name: &str, _port: u16) -> anyhow::Result<()> {
    tracing::debug!("mDNS not available on Windows, skipping");
    Ok(())
}
```

**Action:** No changes needed. Already uses conditional compilation correctly.

---

### 3. main.rs - Startup Logic

**Current (line 1338-1343):**
```rust
// Start mDNS announcer (Linux only)
let _mdns = match mdns::announce_moss(&stone_name, 3001) {
    Ok(daemon) => Some(daemon),
    Err(e) => {
        tracing::warn!(error = ?e, "mDNS announcement failed");
        None
    }
};
```

**Refactored (explicit platform guard):**
```rust
// Start mDNS announcer (Linux only)
#[cfg(not(target_os = "windows"))]
let _mdns = match mdns::announce_moss(&stone_name, 3001) {
    Ok(daemon) => Some(daemon),
    Err(e) => {
        tracing::warn!(error = ?e, "mDNS announcement failed");
        None
    }
};

#[cfg(target_os = "windows")]
let _mdns: Option<()> = None; // Windows uses UDP broadcast only
```

**Main function change:**
```rust
#[tokio::main]
async fn main() -> anyhow::Result<()> {
    // Tracing setup (shared)
    tracing_subscriber::fmt()
        .with_env_filter(EnvFilter::from_default_env())
        .init();

    // Platform-specific service mode
    #[cfg(target_os = "windows")]
    {
        // Check if running as Windows Service
        if std::env::var("RUNNING_AS_SERVICE").is_ok() {
            return service::run_windows_service().await;
        }
    }

    // Shared: Stone initialization
    let stone_name = std::env::var("STONE_NAME").unwrap_or_else(|_| "stone-01".into());
    
    // Shared: Docker, HTTP server, etc. (95% of main.rs unchanged)
    // ...
}
```

---

### 4. service.rs (NEW) - Platform-Specific Service Lifecycle

**Linux Implementation:**
```rust
#[cfg(not(target_os = "windows"))]
pub async fn run_service() -> anyhow::Result<()> {
    // Systemd integration (if needed)
    // Currently, Moss just runs as regular binary with systemd unit file
    // No special code needed - systemd handles lifecycle
    Ok(())
}
```

**Windows Implementation:**
```rust
#[cfg(target_os = "windows")]
pub async fn run_windows_service() -> anyhow::Result<()> {
    use windows_service::{
        define_windows_service,
        service_dispatcher,
        service::{
            ServiceControl, ServiceControlAccept, ServiceExitCode,
            ServiceState, ServiceStatus, ServiceType,
        },
    };

    define_windows_service!(ffi_service_main, moss_service_main);

    fn moss_service_main(_arguments: Vec<OsString>) {
        if let Err(e) = run_service_impl() {
            // Log error to Windows Event Log
        }
    }

    fn run_service_impl() -> anyhow::Result<()> {
        // Set service status to running
        // Run main Moss logic
        // Handle service control events (stop, pause, etc.)
    }

    service_dispatcher::start("MossService", ffi_service_main)?;
    Ok(())
}
```

---

### 5. gpu.rs (NEW) - GPU Detection for Windows

```rust
use anyhow::{Context, Result};

#[derive(Debug, Clone)]
pub struct GpuInfo {
    pub name: String,
    pub vram_mb: u64,
    pub vendor: String, // "NVIDIA", "AMD", "Intel"
}

#[cfg(target_os = "windows")]
pub fn detect_gpu() -> Result<Vec<GpuInfo>> {
    use std::process::Command;
    
    // Try nvidia-smi first
    if let Ok(output) = Command::new("nvidia-smi")
        .args(["--query-gpu=name,memory.total", "--format=csv,noheader,nounits"])
        .output()
    {
        if output.status.success() {
            let stdout = String::from_utf8_lossy(&output.stdout);
            let mut gpus = Vec::new();
            
            for line in stdout.lines() {
                let parts: Vec<&str> = line.split(',').collect();
                if parts.len() >= 2 {
                    gpus.push(GpuInfo {
                        name: parts[0].trim().to_string(),
                        vram_mb: parts[1].trim().parse().unwrap_or(0),
                        vendor: "NVIDIA".to_string(),
                    });
                }
            }
            
            if !gpus.is_empty() {
                return Ok(gpus);
            }
        }
    }
    
    // Fallback: WMIC query
    if let Ok(output) = Command::new("wmic")
        .args(["path", "win32_VideoController", "get", "name"])
        .output()
    {
        // Parse WMIC output
        // ...
    }
    
    Err(anyhow::anyhow!("No compatible GPU detected"))
}

#[cfg(not(target_os = "windows"))]
pub fn detect_gpu() -> Result<Vec<GpuInfo>> {
    // Linux GPU detection (nvidia-smi, rocm-smi)
    // Optional - not required for Linux Moss Phase 1
    Err(anyhow::anyhow!("GPU detection not implemented for Linux"))
}

pub fn require_gpu() -> Result<GpuInfo> {
    let gpus = detect_gpu()?;
    gpus.into_iter().next()
        .ok_or_else(|| anyhow::anyhow!("No GPU detected. Windows Moss requires NVIDIA or AMD GPU."))
}
```

---

### 6. Cargo.toml Updates

**Current:**
```toml
[package]
name = "moss"
version = "0.1.0"
edition = "2021"

[dependencies]
# All deps here

[target.'cfg(not(target_os = "windows"))'.dependencies]
mdns-sd = { workspace = true }
```

**Refactored:**
```toml
[package]
name = "moss"
version = "0.1.0"
edition = "2021"

[dependencies]
anyhow = { workspace = true }
async-stream = "0.3"
axum = { workspace = true }
base64 = "0.22"
bollard = "0.16"
chrono = "0.4"
futures-util = "0.3"
local-ip-address = "0.6"
serde = { workspace = true }
serde_json = { workspace = true }
serde_yaml = "0.9"
sysinfo = "0.30"
tokio = { workspace = true }
tokio-stream = { version = "0.1", features = ["sync"] }
tracing = { workspace = true }
tracing-subscriber = { workspace = true }
uuid = { version = "1.0", features = ["v7", "serde"] }
zen-common = { workspace = true }

> Note: Moss offerings are loaded from the runtime templates directory (no embedded templates).

# Platform-specific dependencies
[target.'cfg(not(target_os = "windows"))'.dependencies]
mdns-sd = { workspace = true }

[target.'cfg(target_os = "windows")'.dependencies]
windows-service = "0.7"
wmi = "0.13"
winapi = { version = "0.3", features = ["winerror", "winbase"] }
```

---

## Build Configuration

**Cargo Workspace Update:**
```toml
# Cargo.toml (workspace root)
[workspace]
members = [
  "src/common",
  "src/moss",              # Unified path (not src/linux/moss)
  "src/garden-rake",       # Renamed from src/windows/garden-rake
]
resolver = "2"
```

**Build Commands:**
```bash
# Linux build (from Windows or Linux)
cargo build --target x86_64-unknown-linux-gnu --package garden-moss

# Windows build
cargo build --target x86_64-pc-windows-msvc --package garden-moss

# Both in CI
cargo build --package garden-moss  # Builds for host platform
```

---

## Migration Steps

### Phase 1: Restructure (Week 1)
1. Move `src/linux/moss` → `src/moss`
2. Update `Cargo.toml` workspace members
3. Add Windows-specific dependencies to `[target]` sections
4. Update imports in `garden-rake` (if any reference moss paths)

### Phase 2: Add Conditional Compilation (Week 1-2)
1. Update `docker.rs` with `#[cfg]` for socket vs named pipe
2. Add `service.rs` with Windows Service stub
3. Add `gpu.rs` with Windows GPU detection
4. Update `main.rs` startup logic with platform guards
5. Update `metrics.rs` if any platform-specific sysinfo usage

### Phase 3: Test Build (Week 2)
1. Test Linux build: `cargo build --target x86_64-unknown-linux-gnu`
2. Test Windows build: `cargo build --target x86_64-pc-windows-msvc`
3. Verify both compile successfully
4. Integration test on both platforms

### Phase 4: CI/CD Update (Week 2)
1. Update GitHub Actions workflow
2. Build matrix: [linux-x86_64, windows-x86_64]
3. Separate test jobs for each platform
4. Artifact upload for both binaries

---

## Expected Code Reuse

| Module | Shared % | Platform-Specific Code |
|--------|----------|------------------------|
| main.rs | 95% | 5% (mDNS init, service mode check) |
| docker.rs | 95% | 5% (socket vs named pipe connection) |
| mdns.rs | Already conditional | ✅ |
| discovery.rs | 100% | None (UDP is cross-platform) |
| metrics.rs | 95% | 5% (possible sysinfo quirks) |
| templates.rs | 100% | None (pure logic) |
| service.rs | NEW | 100% platform-specific (systemd vs SCM) |
| gpu.rs | NEW | 90% shared (nvidia-smi), 10% WMIC on Windows |

**Overall: ~92% code reuse, 8% platform-specific**

---

## Benefits of Single Codebase

1. **Reduced Maintenance:** Fix bugs once, applies to both platforms
2. **Feature Parity:** New features automatically available on both platforms
3. **Clear Contracts:** Conditional compilation makes platform differences explicit
4. **CI Efficiency:** Single pipeline builds both targets
5. **Documentation:** One codebase to document, with platform notes where needed

---

## Testing Strategy

**Unit Tests (cross-platform):**
```rust
#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn test_docker_connection() {
        // This test runs on both platforms
        let manager = DockerManager::new().unwrap();
        assert!(manager.ping().is_ok());
    }

    #[test]
    #[cfg(target_os = "windows")]
    fn test_gpu_detection_windows() {
        // Windows-only test
        let gpus = detect_gpu();
        assert!(gpus.is_ok() || gpus.is_err()); // At least doesn't panic
    }
}
```

**Integration Tests:**
- Linux: Test on Ubuntu 22.04 with Docker Engine
- Windows: Test on Windows 11 with Docker Desktop
- Verify UDP discovery cross-platform
- Verify HTTP API responses identical

---

## Risks & Mitigations

| Risk | Likelihood | Impact | Mitigation |
|------|------------|--------|------------|
| Bollard named pipe issues | Low | High | Bollard has mature Windows support; fallback to TCP socket |
| Windows Service complexity | Medium | Medium | Use well-tested `windows-service` crate; phased rollout |
| Platform-specific bugs | Medium | Low | Extensive testing on both platforms before GA |
| CI build matrix complexity | Low | Low | GitHub Actions has built-in matrix support |

---

## Success Criteria

1. ✅ Single `src/moss` directory compiles for both Linux and Windows
2. ✅ `cargo build --target x86_64-unknown-linux-gnu` succeeds
3. ✅ `cargo build --target x86_64-pc-windows-msvc` succeeds
4. ✅ No duplicate code (only `#[cfg]` guards)
5. ✅ Integration tests pass on both platforms
6. ✅ Binary size similar between platforms (<5MB difference)
7. ✅ HTTP API responses identical (JSON format, status codes)

---

## Timeline

| Week | Focus | Deliverables |
|------|-------|--------------|
| 1 | Restructure + Basic Guards | `src/moss` compiles on Linux |
| 2 | Windows Support + Testing | `src/moss` compiles on Windows, both pass tests |
| 3 | Windows Service + GPU | Service lifecycle working, GPU detection |
| 4 | Integration + CI | Full CI/CD, integration tests, documentation |

**Total: 4 weeks to fully cross-platform codebase**

---

## Next Steps

1. **Review this plan** - Validate approach with team
2. **Create branch** - `feat/cross-platform-moss`
3. **Phase 1 PR** - Restructure to `src/moss`, keep Linux-only functionality
4. **Phase 2 PR** - Add Windows conditional compilation
5. **Phase 3 PR** - Windows Service + GPU detection
6. **Phase 4 PR** - CI/CD updates

**Ready to proceed?** Start with Phase 1: move `src/linux/moss` to `src/moss` and verify Linux builds still work.

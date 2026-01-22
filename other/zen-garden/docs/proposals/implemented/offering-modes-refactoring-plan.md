# Offering Modes: Complete Refactoring & Implementation Plan

**Status:** ✅ Implemented (2026-01-21)
**Implementation Date:** 2026-01-21
**Implementation Report:** [OFFERING-MODES-IMPLEMENTATION-COMPLETE.md](../OFFERING-MODES-IMPLEMENTATION-COMPLETE.md)
**Data Population Report:** [OFFERING-MODES-DATA-POPULATION.md](../OFFERING-MODES-DATA-POPULATION.md)
**Based On:** [offering-modes-implementation.md](offering-modes-implementation.md)
**Codebase:** zen-garden moss daemon (Rust)

---

## Executive Summary

This document provides a complete implementation plan for offering modes (Managed, Adopted, Borrowed) based on detailed codebase analysis. The existing architecture is **remarkably well-prepared** - much of the foundation already exists.

### Key Findings

**Existing Infrastructure (Reuse)**:
- ✅ Container adoption system exists (`domain/adoption.rs`) - extend it
- ✅ Template/manifest system ready
- ✅ Job system for async operations
- ✅ Event broadcasting for console feedback
- ✅ Configuration framework (TOML)
- ✅ Clean domain/infra separation
- ✅ AppState dependency injection

**Gaps (Implement)**:
- ❌ Detection engine for native services
- ❌ Multi-mode offering data models
- ❌ Secrets manager (TPM/keyring/file)
- ❌ Connection templates (Tera rendering)
- ❌ API endpoints for adoption
- ❌ Deployment profile detection

---

## Implementation Strategy

### Principles

1. **KISS**: Start with minimal manifests, add complexity only when needed
2. **DRY**: Reuse existing adoption.rs, templates.rs, config.rs patterns
3. **SoC**: Maintain domain/infra/API separation
4. **YAGNI**: Implement Tier 1 (minimal) first, defer advanced features

### Parallelization Plan

**Phase 1** - Foundation (parallel tracks):
- Track A: Data models + manifest schema (1-2 days)
- Track B: Detection engine (2-3 days)
- Track C: Secrets manager (2-3 days)

**Phase 2** - Integration (sequential, depends on Phase 1):
- API endpoints (1 day)
- Bootstrap integration (1 day)
- CLI commands (1 day)

**Phase 3** - Polish (parallel):
- Tests (2 days)
- Documentation (1 day)

**Total Estimate**: 7-10 days (with parallelization)

---

## Architecture Integration Map

### Current Moss Architecture

```
moss/
├── domain/              # Business logic (pure functions)
│   ├── adoption.rs      # ✅ EXISTS - extend for native services
│   ├── offerings.rs     # ✅ EXISTS - extend for multi-mode
│   ├── compatibility.rs # ✅ EXISTS - reuse
│   ├── service_manager.rs
│   └── [NEW] modes/     # Multi-mode offering logic
│       ├── mod.rs
│       ├── detection.rs
│       ├── lifecycle.rs
│       └── connection.rs
├── infra/               # I/O operations
│   ├── config.rs        # ✅ EXISTS - extend MossConfig
│   ├── container.rs     # ✅ EXISTS - reuse
│   ├── persistence.rs   # ✅ EXISTS - extend
│   ├── [NEW] secrets.rs # TPM/keyring/file backends
│   └── [NEW] detection/ # Detection execution
│       ├── mod.rs
│       ├── command.rs
│       ├── container_inspect.rs
│       └── http_probe.rs
├── api/v1/
│   ├── offerings.rs     # ✅ EXISTS - extend endpoints
│   └── [NEW] adoption.rs # New adoption endpoints
├── tasks/
│   ├── [NEW] detection_monitor.rs
│   └── [NEW] adopted_health.rs
└── bootstrap/
    └── mod.rs           # ✅ EXISTS - add auto-adoption

common/
├── types.rs             # ✅ EXISTS - extend with OfferingMode enum
└── [NEW] manifests/     # Manifest schemas
    └── offering.rs
```

---

## Detailed Implementation Plan

## Phase 1A: Data Models & Manifest Schema (2 days)

### 1.1 Extend Common Types

**File**: `common/src/types.rs`

**Add new types** (follows existing patterns):

```rust
// ===== Offering Modes =====

#[derive(Debug, Clone, Serialize, Deserialize, PartialEq)]
#[serde(rename_all = "lowercase")]
pub enum OfferingMode {
    Managed,   // Container-based (current system)
    Adopted,   // Existing service (native or containerized)
    Borrowed,  // External network service
}

#[derive(Debug, Clone, Serialize, Deserialize, PartialEq)]
#[serde(rename_all = "lowercase")]
pub enum AdoptedControlLevel {
    Full,      // Moss manages lifecycle (start/stop/restart)
    Monitor,   // Moss monitors health only
    Announce,  // Moss announces existence only
}

impl Default for AdoptedControlLevel {
    fn default() -> Self {
        Self::Monitor  // Safe default
    }
}

// ===== Service Location (for all modes) =====

#[derive(Debug, Clone, Serialize, Deserialize)]
pub struct ServiceLocation {
    pub host: String,
    pub port: u16,
    pub protocol: String,  // "http", "tcp", "mongodb", etc.
}

// ===== Adopted Offering =====

#[derive(Debug, Clone, Serialize, Deserialize)]
pub struct AdoptedOfferingInfo {
    pub name: String,
    pub mode: OfferingMode,  // Always Adopted
    pub location: ServiceLocation,
    pub control_level: AdoptedControlLevel,
    pub health_status: ServiceHealthStatus,
    pub detected_at: String,  // ISO 8601

    #[serde(skip_serializing_if = "Option::is_none", default)]
    pub start_command: Option<String>,

    #[serde(skip_serializing_if = "Option::is_none", default)]
    pub stop_command: Option<String>,

    #[serde(skip_serializing_if = "Option::is_none", default)]
    pub restart_command: Option<String>,
}

// ===== Borrowed Offering =====

#[derive(Debug, Clone, Serialize, Deserialize)]
pub struct BorrowedOfferingInfo {
    pub name: String,
    pub mode: OfferingMode,  // Always Borrowed
    pub location: ServiceLocation,
    pub health_method: HealthMethod,
    pub health_status: ServiceHealthStatus,
    pub borrowed_at: String,  // ISO 8601
}

#[derive(Debug, Clone, Serialize, Deserialize)]
#[serde(rename_all = "lowercase")]
pub enum HealthMethod {
    Ping,
    Http,
    Tcp,
}
```

**Rationale**: Follows existing `ServiceInfo` pattern, integrates with current health/status enums.

---

### 1.2 Manifest Schema

**File**: `common/src/manifests/offering.rs` (NEW)

**Purpose**: Deserialize YAML manifests with serde defaults

```rust
use serde::{Deserialize, Serialize};
use std::collections::HashMap;

/// Offering manifest (multi-mode support)
#[derive(Debug, Clone, Deserialize, Serialize)]
pub struct OfferingManifest {
    // ===== REQUIRED FIELDS =====
    pub name: String,
    pub modes: Vec<String>,  // ["managed", "adopted", "borrowed"]

    // ===== OPTIONAL FIELDS (with defaults) =====
    #[serde(default)]
    pub version: String,  // Default: "0.0.0"

    #[serde(skip_serializing_if = "Option::is_none", default)]
    pub auto_adopt: Option<bool>,  // Default: None (use profile default)

    // ===== MODE-SPECIFIC SECTIONS =====

    /// Detection rules for Adopted mode (OPTIONAL)
    #[serde(skip_serializing_if = "Vec::is_empty", default)]
    pub detection: Vec<DetectionRule>,

    /// Container config for Managed mode (OPTIONAL)
    #[serde(skip_serializing_if = "Option::is_none", default)]
    pub container: Option<ContainerConfig>,

    /// Connection template (OPTIONAL - default: basic JSON)
    #[serde(skip_serializing_if = "Option::is_none", default)]
    pub connection_template: Option<ConnectionTemplate>,

    /// Compatibility rules (OPTIONAL - works for both Managed and Adopted)
    #[serde(skip_serializing_if = "Option::is_none", default)]
    pub compatibility: Option<CompatibilityRules>,
}

/// Detection rule for Adopted mode
#[derive(Debug, Clone, Deserialize, Serialize)]
pub struct DetectionRule {
    // REQUIRED
    pub method: DetectionMethod,

    // Method-specific required fields
    #[serde(skip_serializing_if = "Option::is_none", default)]
    pub command: Option<String>,  // Required if method=command

    // OPTIONAL fields (with defaults)
    #[serde(skip_serializing_if = "Option::is_none", default)]
    pub name: Option<String>,

    #[serde(default)]
    pub priority: i32,  // Default: 0

    #[serde(default = "default_true")]
    pub can_parallelize_check: bool,  // Default: true

    #[serde(default)]
    pub cost: DetectionCost,  // Default: Cheap

    #[serde(skip_serializing_if = "Option::is_none", default)]
    pub validation: Option<ValidationRule>,

    #[serde(skip_serializing_if = "Option::is_none", default)]
    pub stability: Option<StabilityConfig>,

    #[serde(skip_serializing_if = "Option::is_none", default)]
    pub service: Option<ServiceConfig>,

    #[serde(skip_serializing_if = "Option::is_none", default)]
    pub control: Option<ControlConfig>,
}

#[derive(Debug, Clone, Deserialize, Serialize)]
#[serde(rename_all = "lowercase")]
pub enum DetectionMethod {
    Command,
    ContainerInspect,
    HttpProbe,
}

#[derive(Debug, Clone, Deserialize, Serialize, Default)]
#[serde(rename_all = "lowercase")]
pub enum DetectionCost {
    #[default]
    Cheap,     // No caching (always run)
    Expensive, // Cache for 5 minutes
}

/// Container config for Managed mode
#[derive(Debug, Clone, Deserialize, Serialize)]
pub struct ContainerConfig {
    // REQUIRED
    pub image: String,
    pub ports: Vec<String>,  // ["8080:8080"]

    // OPTIONAL
    #[serde(skip_serializing_if = "Vec::is_empty", default)]
    pub volumes: Vec<VolumeMount>,

    #[serde(skip_serializing_if = "HashMap::is_empty", default)]
    pub environment: HashMap<String, String>,

    #[serde(skip_serializing_if = "Option::is_none", default)]
    pub resources: Option<ResourceLimits>,

    #[serde(skip_serializing_if = "Option::is_none", default)]
    pub health_check: Option<HealthCheckConfig>,

    #[serde(default)]
    pub update_strategy: UpdateStrategy,  // Default: Rolling
}

/// Connection template (OPTIONAL - for custom connection formats)
#[derive(Debug, Clone, Deserialize, Serialize)]
pub struct ConnectionTemplate {
    pub template_type: String,  // "templated_json"
    pub template: String,       // Tera template string
    pub variables: HashMap<String, VariableSpec>,
}

#[derive(Debug, Clone, Deserialize, Serialize)]
pub struct VariableSpec {
    pub source: String,  // "location", "credentials", "config"
    pub key: String,
    #[serde(skip_serializing_if = "Option::is_none", default)]
    pub default: Option<String>,
}

fn default_true() -> bool { true }
```

**Validation function** (domain layer):

```rust
/// Validate manifest structure
///
/// Checks:
/// - Required fields present
/// - Mode-specific requirements (detection for adopted, container for managed)
/// - Optional field validity
///
/// Returns Ok(()) if valid, Err with descriptive message if invalid
pub fn validate_manifest(manifest: &OfferingManifest) -> anyhow::Result<()> {
    // Name validation
    if !manifest.name.chars().all(|c| c.is_alphanumeric() || c == '-' || c == '_') {
        anyhow::bail!("Invalid offering name: {}", manifest.name);
    }

    // At least one mode
    if manifest.modes.is_empty() {
        anyhow::bail!("At least one mode must be specified");
    }

    // Adopted mode requires detection rules
    if manifest.modes.contains(&"adopted".to_string()) {
        if manifest.detection.is_empty() {
            anyhow::bail!("Adopted mode requires at least one detection rule");
        }

        // Validate each detection rule
        for (idx, rule) in manifest.detection.iter().enumerate() {
            match rule.method {
                DetectionMethod::Command if rule.command.is_none() => {
                    anyhow::bail!("Detection rule {} method 'command' requires 'command' field", idx);
                }
                _ => {}
            }
        }
    }

    // Managed mode requires container config
    if manifest.modes.contains(&"managed".to_string()) {
        let container = manifest.container.as_ref()
            .ok_or_else(|| anyhow::anyhow!("Managed mode requires 'container' config"))?;

        if container.image.is_empty() {
            anyhow::bail!("Container config requires 'image' field");
        }
        if container.ports.is_empty() {
            anyhow::bail!("Container config requires at least one port mapping");
        }
    }

    Ok(())
}
```

---

### 1.3 Extend AppState

**File**: `moss/src/app_state.rs`

**Add registries for adopted/borrowed offerings**:

```rust
#[derive(Clone)]
pub struct AppState {
    // ... existing fields ...

    /// Adopted offerings registry (native/containerized services)
    pub adopted_offerings: Arc<RwLock<Vec<AdoptedOfferingInfo>>>,

    /// Borrowed offerings registry (external network services)
    pub borrowed_offerings: Arc<RwLock<Vec<BorrowedOfferingInfo>>>,

    /// Offering manifests cache (loaded from .zen-garden/manifests/)
    pub manifests: Arc<RwLock<Vec<OfferingManifest>>>,
}
```

**Rationale**: Maintains separation of concerns - each mode has its own registry.

---

## Phase 1B: Detection Engine (3 days)

### 2.1 Detection Execution (Infra Layer)

**File**: `moss/src/infra/detection/command.rs` (NEW)

**Purpose**: Execute shell commands for detection (non-blocking, timeout)

```rust
use std::process::Stdio;
use std::time::Duration;
use tokio::process::Command;
use anyhow::Result;

/// Execute detection command with timeout
///
/// Returns stdout if command succeeds (exit code 0), error otherwise
pub async fn execute_command_detection(
    command: &str,
    timeout: Duration,
) -> Result<String> {
    let output = tokio::time::timeout(
        timeout,
        Command::new("sh")
            .arg("-c")
            .arg(command)
            .stdout(Stdio::piped())
            .stderr(Stdio::piped())
            .output()
    ).await??;

    if !output.status.success() {
        anyhow::bail!("Command failed with exit code: {:?}", output.status.code());
    }

    Ok(String::from_utf8_lossy(&output.stdout).to_string())
}
```

**File**: `moss/src/infra/detection/container_inspect.rs` (NEW)

**Purpose**: Inspect Docker/Podman containers for adopted offerings

```rust
use bollard::Docker;
use bollard::container::{ListContainersOptions};
use anyhow::Result;

/// Detect containerized service by image pattern
///
/// Searches running containers matching image filter
pub async fn detect_containerized_service(
    docker: &Docker,
    image_contains: &str,
) -> Result<Option<ContainerInfo>> {
    let mut filters = std::collections::HashMap::new();
    filters.insert("status".to_string(), vec!["running".to_string()]);

    let options = ListContainersOptions {
        filters,
        ..Default::default()
    };

    let containers = docker.list_containers(Some(options)).await?;

    for container in containers {
        if let Some(image) = container.image {
            if image.contains(image_contains) {
                return Ok(Some(ContainerInfo {
                    id: container.id.unwrap_or_default(),
                    image,
                    ports: extract_ports(&container),
                }));
            }
        }
    }

    Ok(None)
}

#[derive(Debug)]
pub struct ContainerInfo {
    pub id: String,
    pub image: String,
    pub ports: Vec<(u16, u16)>,  // (host, container)
}
```

**File**: `moss/src/infra/detection/http_probe.rs` (NEW)

**Purpose**: HTTP health probes for detection

```rust
use reqwest::Client;
use std::time::Duration;
use anyhow::Result;

/// Probe HTTP endpoint for service detection
///
/// Returns Ok(()) if endpoint responds with 2xx/3xx, error otherwise
pub async fn probe_http_endpoint(
    url: &str,
    timeout: Duration,
) -> Result<()> {
    let client = Client::builder()
        .timeout(timeout)
        .build()?;

    let response = client.get(url).send().await?;

    if response.status().is_success() || response.status().is_redirection() {
        Ok(())
    } else {
        anyhow::bail!("HTTP probe failed with status: {}", response.status())
    }
}
```

---

### 2.2 Detection Orchestration (Domain Layer)

**File**: `moss/src/domain/modes/detection.rs` (NEW)

**Purpose**: Parallel detection with caching, stability tracking

```rust
use futures_util::stream::{self, StreamExt};
use std::time::{Duration, Instant};
use dashmap::DashMap;
use anyhow::Result;

/// Detection cache with TTL
pub struct DetectionCache {
    cache: DashMap<String, CachedDetection>,
}

#[derive(Clone)]
struct CachedDetection {
    result: DetectionResult,
    cached_at: Instant,
    ttl: Duration,
}

#[derive(Clone, Debug)]
pub struct DetectionResult {
    pub offering_name: String,
    pub found: bool,
    pub location: Option<ServiceLocation>,
    pub method: String,
}

impl DetectionCache {
    pub fn new() -> Self {
        Self {
            cache: DashMap::new(),
        }
    }

    pub fn get(&self, key: &str) -> Option<DetectionResult> {
        self.cache.get(key).and_then(|entry| {
            if entry.cached_at.elapsed() < entry.ttl {
                Some(entry.result.clone())
            } else {
                None
            }
        })
    }

    pub fn set(&self, key: String, result: DetectionResult, ttl: Duration) {
        self.cache.insert(key, CachedDetection {
            result,
            cached_at: Instant::now(),
            ttl,
        });
    }
}

/// Detect all adoptable offerings in parallel
///
/// Executes detection rules by priority, caches expensive checks,
/// parallelizes where possible
pub async fn detect_adoptable_offerings(
    manifests: Vec<OfferingManifest>,
    cache: &DetectionCache,
) -> Vec<DetectionResult> {
    stream::iter(manifests)
        .map(|manifest| async move {
            detect_offering(&manifest, cache).await
        })
        .buffer_unordered(10)  // Parallel limit
        .collect::<Vec<_>>()
        .await
        .into_iter()
        .flatten()
        .collect()
}

async fn detect_offering(
    manifest: &OfferingManifest,
    cache: &DetectionCache,
) -> Option<DetectionResult> {
    // Sort rules by priority (higher first)
    let mut rules = manifest.detection.clone();
    rules.sort_by_key(|r| std::cmp::Reverse(r.priority));

    for rule in rules {
        // Check cache for expensive detections
        if rule.cost == DetectionCost::Expensive {
            let cache_key = format!("{}:{}", manifest.name, rule.name.as_deref().unwrap_or("default"));
            if let Some(cached) = cache.get(&cache_key) {
                if cached.found {
                    return Some(cached);
                } else {
                    continue;  // Try next rule
                }
            }
        }

        // Execute detection
        match execute_detection_rule(&rule, manifest).await {
            Ok(Some(result)) => {
                // Cache if expensive
                if rule.cost == DetectionCost::Expensive {
                    let cache_key = format!("{}:{}", manifest.name, rule.name.as_deref().unwrap_or("default"));
                    cache.set(cache_key, result.clone(), Duration::from_secs(300));
                }
                return Some(result);
            }
            Ok(None) | Err(_) => continue,  // Try next rule
        }
    }

    None
}

async fn execute_detection_rule(
    rule: &DetectionRule,
    manifest: &OfferingManifest,
) -> Result<Option<DetectionResult>> {
    match rule.method {
        DetectionMethod::Command => {
            let stdout = crate::infra::detection::execute_command_detection(
                rule.command.as_deref().unwrap(),
                Duration::from_secs(5),
            ).await?;

            // Validate output if specified
            if let Some(validation) = &rule.validation {
                validate_output(&stdout, validation)?;
            }

            // Extract service metadata
            let location = rule.service.as_ref().map(|svc| ServiceLocation {
                host: "localhost".to_string(),
                port: svc.port,
                protocol: svc.protocol.clone(),
            });

            Ok(Some(DetectionResult {
                offering_name: manifest.name.clone(),
                found: true,
                location,
                method: "command".to_string(),
            }))
        }
        DetectionMethod::ContainerInspect => {
            // Implementation similar to above
            todo!("Container inspect detection")
        }
        DetectionMethod::HttpProbe => {
            // Implementation similar to above
            todo!("HTTP probe detection")
        }
    }
}
```

---

## Phase 1C: Secrets Manager (3 days)

### 3.1 Secrets Backend Trait

**File**: `moss/src/infra/secrets.rs` (NEW)

**Purpose**: Platform-adaptive secret storage

```rust
use anyhow::Result;

/// Secret storage backend trait
pub trait SecretBackend: Send + Sync {
    fn name(&self) -> &str;
    fn is_available() -> bool where Self: Sized;
    fn store(&self, key: &str, secret: &[u8]) -> Result<()>;
    fn retrieve(&self, key: &str) -> Result<Option<Vec<u8>>>;
    fn delete(&self, key: &str) -> Result<()>;
}

/// Secrets manager with platform-adaptive backend selection
pub struct SecretsManager {
    backend: Box<dyn SecretBackend>,
}

impl SecretsManager {
    pub fn new() -> Self {
        // Priority: TPM > Platform Keyring > Encrypted File
        let backend: Box<dyn SecretBackend> = if TpmBackend::is_available() {
            tracing::info!("Using TPM for secret storage (hardware-backed)");
            Box::new(TpmBackend::new())
        } else if PlatformKeyringBackend::is_available() {
            tracing::info!("Using platform keyring for secret storage");
            Box::new(PlatformKeyringBackend::new())
        } else {
            tracing::warn!("No hardware secret storage available, using encrypted file");
            Box::new(EncryptedFileBackend::new())
        };

        Self { backend }
    }

    pub fn store(&self, key: &str, secret: &[u8]) -> Result<()> {
        self.backend.store(key, secret)
    }

    pub fn retrieve(&self, key: &str) -> Result<Option<Vec<u8>>> {
        self.backend.retrieve(key)
    }

    pub fn delete(&self, key: &str) -> Result<()> {
        self.backend.delete(key)
    }
}

// TPM backend (Windows/Linux)
struct TpmBackend {
    // TPM context (placeholder - use tpm2-tss crate)
}

impl SecretBackend for TpmBackend {
    fn name(&self) -> &str { "TPM" }

    fn is_available() -> bool {
        #[cfg(target_os = "windows")]
        {
            std::path::Path::new("\\\\.\\TPM").exists()
        }
        #[cfg(target_os = "linux")]
        {
            std::path::Path::new("/dev/tpm0").exists() ||
            std::path::Path::new("/dev/tpmrm0").exists()
        }
        #[cfg(not(any(target_os = "windows", target_os = "linux")))]
        {
            false
        }
    }

    fn store(&self, key: &str, secret: &[u8]) -> Result<()> {
        // Seal with PCR 0, 7, 14
        todo!("TPM seal implementation")
    }

    fn retrieve(&self, key: &str) -> Result<Option<Vec<u8>>> {
        // Unseal with PCR validation
        todo!("TPM unseal implementation")
    }

    fn delete(&self, key: &str) -> Result<()> {
        todo!("TPM delete implementation")
    }
}

// Encrypted file backend (fallback)
struct EncryptedFileBackend {
    secrets_dir: std::path::PathBuf,
}

impl EncryptedFileBackend {
    fn new() -> Self {
        Self {
            secrets_dir: std::path::PathBuf::from(".zen-garden/secrets"),
        }
    }
}

impl SecretBackend for EncryptedFileBackend {
    fn name(&self) -> &str { "EncryptedFile" }

    fn is_available() -> bool { true }  // Always available as fallback

    fn store(&self, key: &str, secret: &[u8]) -> Result<()> {
        std::fs::create_dir_all(&self.secrets_dir)?;

        // Encrypt using ChaCha20-Poly1305 with derived key
        let encrypted = encrypt_secret(secret)?;

        let path = self.secrets_dir.join(format!("{}.cred", key));
        std::fs::write(path, encrypted)?;

        Ok(())
    }

    fn retrieve(&self, key: &str) -> Result<Option<Vec<u8>>> {
        let path = self.secrets_dir.join(format!("{}.cred", key));

        if !path.exists() {
            return Ok(None);
        }

        let encrypted = std::fs::read(path)?;
        let decrypted = decrypt_secret(&encrypted)?;

        Ok(Some(decrypted))
    }

    fn delete(&self, key: &str) -> Result<()> {
        let path = self.secrets_dir.join(format!("{}.cred", key));
        if path.exists() {
            std::fs::remove_file(path)?;
        }
        Ok(())
    }
}

fn encrypt_secret(plaintext: &[u8]) -> Result<Vec<u8>> {
    // Use ChaCha20-Poly1305 with machine-derived key
    todo!("Encryption implementation")
}

fn decrypt_secret(ciphertext: &[u8]) -> Result<Vec<u8>> {
    todo!("Decryption implementation")
}
```

**Dependencies to add** (in `moss/Cargo.toml`):
```toml
[dependencies]
# Secrets
chacha20poly1305 = "0.10"  # Encryption
argon2 = "0.5"             # Key derivation
tera = "1.19"              # Template rendering

# Detection
dashmap = "5.5"            # Concurrent cache
```

---

## Phase 2: API & Integration (3 days)

### 4.1 API Endpoints

**File**: `moss/src/api/v1/adoption.rs` (NEW)

**Purpose**: RESTful adoption endpoints

```rust
use axum::{
    extract::{Path, State, Query},
    http::StatusCode,
    response::Json,
};
use serde::{Deserialize, Serialize};
use crate::AppState;

/// POST /api/v1/offerings/adopt
#[derive(Debug, Deserialize)]
pub struct AdoptRequest {
    pub name: String,
    #[serde(default)]
    pub control_level: Option<AdoptedControlLevel>,
}

pub async fn adopt_offering_v1(
    State(state): State<AppState>,
    Json(req): Json<AdoptRequest>,
) -> Result<Json<AdoptedOfferingInfo>, (StatusCode, String)> {
    // 1. Run detection for this offering
    // 2. Validate it was found
    // 3. Register in adopted_offerings registry
    // 4. Persist to disk
    // 5. Emit console event
    todo!("Adopt offering implementation")
}

/// POST /api/v1/offerings/adopt-all
pub async fn adopt_all_offerings_v1(
    State(state): State<AppState>,
    Json(req): Json<AdoptAllRequest>,
) -> Result<Json<AdoptAllResponse>, (StatusCode, String)> {
    // 1. Load all manifests with auto_adopt=true
    // 2. Run parallel detection
    // 3. Register all found offerings
    // 4. Return adoption results
    todo!("Adopt all implementation")
}

#[derive(Debug, Deserialize)]
pub struct AdoptAllRequest {
    #[serde(default)]
    pub control_level: Option<AdoptedControlLevel>,
}

#[derive(Debug, Serialize)]
pub struct AdoptAllResponse {
    pub adopted: Vec<AdoptedOfferingInfo>,
    pub not_found: Vec<String>,
    pub failed: Vec<(String, String)>,
}
```

**Extend existing**: `moss/src/api/v1/offerings.rs`

```rust
// Add mode filter to list_offerings_v1
#[derive(Debug, Deserialize)]
pub struct ListOfferingsQuery {
    #[serde(default)]
    pub mode: Option<String>,  // "managed", "adopted", "borrowed", "all"
}

pub async fn list_offerings_v1(
    State(state): State<AppState>,
    Query(query): Query<ListOfferingsQuery>,
) -> Result<Json<ListOfferingsResponse>, (StatusCode, String)> {
    let mode_filter = query.mode.as_deref();

    let mut offerings = Vec::new();

    // Add managed offerings (existing logic)
    if mode_filter.is_none() || mode_filter == Some("managed") || mode_filter == Some("all") {
        // ... existing code ...
    }

    // Add adopted offerings (NEW)
    if mode_filter.is_none() || mode_filter == Some("adopted") || mode_filter == Some("all") {
        let adopted = state.adopted_offerings.read().await;
        for offering in adopted.iter() {
            offerings.push(OfferingResponse {
                name: offering.name.clone(),
                mode: "adopted".to_string(),
                status: offering.health_status.clone(),
                location: Some(offering.location.clone()),
                // ... more fields ...
            });
        }
    }

    // Add borrowed offerings (NEW)
    if mode_filter.is_none() || mode_filter == Some("borrowed") || mode_filter == Some("all") {
        let borrowed = state.borrowed_offerings.read().await;
        for offering in borrowed.iter() {
            offerings.push(OfferingResponse {
                name: offering.name.clone(),
                mode: "borrowed".to_string(),
                status: offering.health_status.clone(),
                location: Some(offering.location.clone()),
                // ... more fields ...
            });
        }
    }

    Ok(Json(ListOfferingsResponse { offerings }))
}
```

---

### 4.2 Bootstrap Auto-Adoption

**File**: `moss/src/bootstrap/mod.rs`

**Extend with auto-adoption**:

```rust
/// Auto-adopt existing services on startup
///
/// Only runs if:
/// - Deployment profile allows auto-adoption (not USB/container)
/// - Config has adoption.enabled = true
pub async fn bootstrap_auto_adoption(state: &AppState, config: &MossConfig) {
    // Detect deployment profile
    let profile = detect_deployment_profile();

    let adoption_enabled = config.adoption_enabled.unwrap_or_else(|| {
        profile.default_auto_adopt_enabled()
    });

    if !adoption_enabled {
        tracing::info!(
            profile = ?profile,
            "Auto-adoption disabled for this deployment profile"
        );
        return;
    }

    // Load manifests with auto_adopt=true
    let manifests = state.manifests.read().await;
    let auto_adopt_manifests: Vec<_> = manifests.iter()
        .filter(|m| m.auto_adopt.unwrap_or(false))
        .filter(|m| {
            // Check exclude list
            !config.adoption_exclude.contains(&m.name)
        })
        .cloned()
        .collect();

    if auto_adopt_manifests.is_empty() {
        return;
    }

    tracing::info!(
        count = auto_adopt_manifests.len(),
        "Starting auto-adoption for {} manifests",
        auto_adopt_manifests.len()
    );

    // Spawn background detection task
    tokio::spawn(async move {
        let cache = DetectionCache::new();
        let detected = detect_adoptable_offerings(auto_adopt_manifests, &cache).await;

        tracing::info!(
            detected = detected.len(),
            "Auto-adoption detected {} offerings",
            detected.len()
        );

        // Register each detected offering
        for result in detected {
            if result.found {
                // Create AdoptedOfferingInfo and register
                // ... implementation ...
            }
        }
    });
}

#[derive(Debug, Clone, Copy, PartialEq)]
enum DeploymentProfile {
    Regular,        // Bare metal/VM
    UsbPortable,    // USB stick
    Container,      // Docker/Podman
}

fn detect_deployment_profile() -> DeploymentProfile {
    if std::path::Path::new("/mnt/.zen-garden-usb").exists() ||
       std::env::var("ZEN_GARDEN_USB").is_ok() {
        return DeploymentProfile::UsbPortable;
    }

    if std::path::Path::new("/.dockerenv").exists() {
        return DeploymentProfile::Container;
    }

    DeploymentProfile::Regular
}

impl DeploymentProfile {
    fn default_auto_adopt_enabled(&self) -> bool {
        match self {
            Self::Regular => true,
            Self::UsbPortable => false,  // Self-contained
            Self::Container => false,    // Isolated
        }
    }
}
```

---

### 4.3 Configuration Extension

**File**: `moss/src/infra/config.rs`

**Extend MossConfig**:

```rust
#[derive(Debug, Clone, serde::Deserialize, serde::Serialize)]
pub struct MossConfig {
    // ... existing fields ...

    /// Adoption configuration (OPTIONAL)
    #[serde(skip_serializing_if = "Option::is_none", default)]
    pub adoption: Option<AdoptionConfig>,
}

#[derive(Debug, Clone, serde::Deserialize, serde::Serialize)]
pub struct AdoptionConfig {
    /// Enable auto-adoption on startup (default: profile-dependent)
    #[serde(skip_serializing_if = "Option::is_none", default)]
    pub enabled: Option<bool>,

    /// Default control level for auto-adopted services (default: "monitor")
    #[serde(default = "default_monitor")]
    pub default_control_level: String,

    /// Exclude these offerings from auto-adoption (default: empty)
    #[serde(skip_serializing_if = "Vec::is_empty", default)]
    pub exclude: Vec<String>,
}

fn default_monitor() -> String {
    "monitor".to_string()
}
```

**Example config** (`.zen-garden/moss.toml`):

```toml
stone_name = "stone-01"
port = 7185

[adoption]
enabled = true
default_control_level = "monitor"
exclude = ["postgres"]  # Don't auto-adopt postgres
```

---

## Phase 3: Testing & Validation (2 days)

### 5.1 Unit Tests

**File**: `moss/src/domain/modes/detection_tests.rs` (NEW)

```rust
#[cfg(test)]
mod tests {
    use super::*;

    #[tokio::test]
    async fn test_minimal_manifest_loads() {
        let yaml = r#"
name: test-service
modes: ['adopted']
detection:
  - method: command
    command: "echo 'version 1.0.0'"
"#;

        let manifest: OfferingManifest = serde_yaml::from_str(yaml).unwrap();
        assert_eq!(manifest.name, "test-service");
        assert_eq!(manifest.modes, vec!["adopted"]);
        assert_eq!(manifest.detection.len(), 1);

        // Validate no optional fields break
        validate_manifest(&manifest).unwrap();
    }

    #[tokio::test]
    async fn test_manifest_validation_rejects_invalid() {
        let yaml = r#"
name: test-service
modes: ['adopted']
detection: []  # Invalid: adopted needs detection
"#;

        let manifest: OfferingManifest = serde_yaml::from_str(yaml).unwrap();
        assert!(validate_manifest(&manifest).is_err());
    }

    #[tokio::test]
    async fn test_detection_cache_expiry() {
        let cache = DetectionCache::new();

        let result = DetectionResult {
            offering_name: "test".to_string(),
            found: true,
            location: None,
            method: "command".to_string(),
        };

        cache.set("test".to_string(), result.clone(), Duration::from_millis(100));

        // Should be cached
        assert!(cache.get("test").is_some());

        // Wait for expiry
        tokio::time::sleep(Duration::from_millis(150)).await;

        // Should be expired
        assert!(cache.get("test").is_none());
    }
}
```

### 5.2 Integration Tests

**File**: `moss/tests/offering_modes_integration.rs` (NEW)

```rust
#[tokio::test]
async fn test_minimal_manifest_adoption_flow() {
    // 1. Write minimal manifest to temp dir
    // 2. Load manifest
    // 3. Validate it passes
    // 4. Mock detection (command returns success)
    // 5. Verify adopted offering registered
    // 6. Verify no null/empty fields in JSON output
}

#[tokio::test]
async fn test_deployment_profile_detection() {
    // Test that USB/container profiles disable auto-adoption
}

#[tokio::test]
async fn test_namespace_collision_handling() {
    // Test that both mongodb@managed and mongodb@adopted can coexist
}
```

---

## Validation Checklist

**Before merging**:

```bash
# 1. NO hardcoded service names
rg '"mongodb"|"postgres"|"redis"|"ollama"' src/moss/src/ src/common/src/

# 2. NO null/empty values in minimal manifests
# Manually inspect test manifests

# 3. Minimal manifests work without optional fields
cargo test test_minimal_manifest --package garden-moss

# 4. Defaults are sensible
cargo test test_default_values --package garden-moss

# 5. All modes can be queried
curl http://localhost:7185/api/v1/offerings?mode=adopted
curl http://localhost:7185/api/v1/offerings?mode=managed
curl http://localhost:7185/api/v1/offerings?mode=borrowed
```

---

## File Tree After Implementation

```
moss/
├── src/
│   ├── domain/
│   │   ├── adoption.rs          # ✅ EXISTS - extend for native
│   │   ├── offerings.rs         # ✅ EXISTS - extend for multi-mode
│   │   ├── compatibility.rs     # ✅ EXISTS - reuse
│   │   ├── service_manager.rs   # ✅ EXISTS
│   │   └── modes/               # NEW
│   │       ├── mod.rs
│   │       ├── detection.rs     # Detection orchestration
│   │       ├── lifecycle.rs     # Adopted service lifecycle
│   │       └── connection.rs    # Connection template rendering
│   ├── infra/
│   │   ├── config.rs            # EXTEND with AdoptionConfig
│   │   ├── secrets.rs           # NEW - TPM/keyring/file
│   │   └── detection/           # NEW
│   │       ├── mod.rs
│   │       ├── command.rs
│   │       ├── container_inspect.rs
│   │       └── http_probe.rs
│   ├── api/v1/
│   │   ├── offerings.rs         # EXTEND with mode filter
│   │   └── adoption.rs          # NEW - adoption endpoints
│   ├── tasks/
│   │   ├── detection_monitor.rs # NEW - background detection
│   │   └── adopted_health.rs    # NEW - health monitoring
│   ├── bootstrap/
│   │   └── mod.rs               # EXTEND with auto-adoption
│   └── app_state.rs             # EXTEND with adopted/borrowed registries

common/
├── src/
│   ├── types.rs                 # EXTEND with OfferingMode, AdoptedOfferingInfo, etc.
│   └── manifests/               # NEW
│       └── offering.rs          # Manifest schema

.zen-garden/
├── manifests/                   # NEW - user manifests
│   ├── ollama.yaml
│   ├── postgres.yaml
│   └── custom-service.yaml
├── adopted/                     # NEW - runtime adoption state
│   └── ollama@adopted.json
└── secrets/                     # NEW - encrypted credentials
    └── nas@borrowed.cred
```

---

## Risk Mitigation

### Risk 1: Complexity Bloat

**Mitigation**:
- Start with Tier 1 (minimal manifests) only
- Defer advanced features (stability, graduated health) to Phase 2
- Use feature flags for optional components

### Risk 2: Performance Impact

**Mitigation**:
- Cache expensive detections (5-min TTL)
- Parallelize detection (10 concurrent limit)
- Background tasks for non-critical operations

### Risk 3: Breaking Changes

**Mitigation**:
- All new fields are optional with `#[serde(skip_serializing_if)]`
- Existing offerings.json registry format unchanged
- New registries (adopted/borrowed) use separate files
- API versioning (/api/v1/) allows future changes

---

## Implementation Order (Optimized for Parallelization)

### Week 1

**Day 1-2** (Parallel):
- Track A: Data models (types.rs, manifests/offering.rs)
- Track B: Detection infra (command.rs, container_inspect.rs)
- Track C: Secrets manager stub (encrypted file only)

**Day 3** (Sequential, depends on Day 1-2):
- Detection orchestration (detection.rs)
- API endpoints (adoption.rs, extend offerings.rs)

**Day 4**:
- Bootstrap integration (auto-adoption)
- Config extension (moss.toml adoption section)

**Day 5**:
- Unit tests
- Integration tests
- Validation checklist

---

## Success Metrics

**Functional**:
- [ ] Minimal manifests (4-6 lines) work without errors
- [ ] Auto-adoption runs on startup (profile-aware)
- [ ] API endpoints return multi-mode offerings
- [ ] Detection cache reduces redundant checks
- [ ] Secrets stored securely (encrypted file minimum)

**Technical**:
- [ ] Zero hardcoded service names (verified by grep)
- [ ] No null/empty values in minimal manifests
- [ ] All optional fields truly optional (unit tests)
- [ ] Default values sensible (monitor control, no flapping)
- [ ] Clean separation: domain/infra/API

**Performance**:
- [ ] Detection completes in <5s for 10 offerings
- [ ] Parallel detection utilizes multiple cores
- [ ] Expensive checks cached (not re-run)

---

**End of Refactoring Plan**

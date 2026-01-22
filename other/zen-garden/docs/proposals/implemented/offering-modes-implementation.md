# Offering Modes Implementation Plan

**Status:** ✅ Superseded by [offering-modes-refactoring-plan.md](implemented/offering-modes-refactoring-plan.md)
**Date:** 2026-01-21
**Based On:** [offering-modes.md](offering-modes.md)
**Integrates:** [cli-taxonomy.md](cli-taxonomy.md), [rake-cli.md](../specs/rake-cli.md)
**Superseded By:** Detailed refactoring plan with actual codebase integration
**Implementation:** See [OFFERING-MODES-IMPLEMENTATION-COMPLETE.md](../OFFERING-MODES-IMPLEMENTATION-COMPLETE.md)

---

## Executive Summary

Implementation plan for **Offering Modes** - three deployment patterns for services in Zen Garden:

1. **Managed (Planted)** - Container-based services with full Moss lifecycle control
2. **Adopted** - Existing services (native or containerized) with configurable Moss control
3. **Borrowed** - External network services that Moss announces but doesn't control

**Key Principles**:
- "offer" = deploy new, "adopt" = integrate existing
- **ZERO hardcoded service names** - all offerings defined in manifest files
- System is completely data-driven and extensible
- **Minimal manifests by default** - keep configurations small and expressive
- All advanced features are OPTIONAL with sensible defaults

**Minimal Manifest Philosophy**:
- Start with 3-5 required fields, everything else optional
- No bloat - add features only when needed
- Sensible defaults for all optional features
- Progressive complexity tiers (minimal → common → advanced)

---

## Design Principles

### 1. Manifest-Driven Architecture

**CRITICAL**: The codebase must contain **NO hardcoded service/offering names**.

- ❌ **NEVER**: `if offering.name == "mongodb"` or `match service_type { "postgres" => ... }`
- ✅ **ALWAYS**: Load from manifest files, query by generic attributes

**Examples in this document** (mongodb, postgres, ollama) are for **illustration only** and should NEVER appear in production code.

### 2. Generic Implementation

All offering logic must be generic and work with any manifest-defined offering:

```rust
// ❌ BAD - Hardcoded service names
match offering.name.as_str() {
    "mongodb" => generate_mongodb_connection(),
    "postgres" => generate_postgres_connection(),
    _ => generate_generic_connection()
}

// ✅ GOOD - Manifest-driven connection format
pub fn generate_connection_payload(offering: &Offering) -> serde_json::Value {
    // Use connection_template from manifest
    let template = &offering.manifest.connection_template;
    template.render(offering.location, offering.credentials)
}
```

### 3. Extensibility

New offerings should be added by:
1. Creating a new manifest file (e.g., `custom-service.yaml`)
2. Placing in `.zen-garden/manifests/` directory
3. **Zero code changes required**

---

## Command Structure

### Zen Syntax (Normative)

```bash
# List available offerings (planted + adoptable)
garden-rake explore
garden-rake explore adoptable      # Only adoptable offerings

# Deploy managed offering (existing command - unchanged)
garden-rake offer mongodb
garden-rake offer mongodb at stone-02

# Adopt detected service (NEW)
garden-rake adopt mongodb
garden-rake adopt mongodb at stone-02
garden-rake adopt mongodb at stone-02 with full-control   # Control level override

# Adopt all detected services (NEW)
garden-rake adopt all
garden-rake adopt all with monitor-only   # Default control level

# Lifecycle operations (existing - works with all modes)
garden-rake rest mongodb           # Stop service (managed or adopted)
garden-rake wake mongodb           # Start service
garden-rake observe mongodb        # View details
garden-rake release mongodb        # Remove/unadopt
```

### Namespace Collision Resolution

**Strategy**: Register offerings with multiple identifiers

```rust
// When both managed and adopted "mongodb" exist:
OfferingRegistry {
    "mongodb": vec![
        OfferingRef { id: "mongodb@managed", mode: Managed },
        OfferingRef { id: "mongodb@adopted", mode: Adopted },
    ],
    "mongodb@managed": OfferingRef { id: "mongodb@managed", mode: Managed },
    "mongodb@adopted": OfferingRef { id: "mongodb@adopted", mode: Adopted },
}
```

**Query behavior**:
- `garden-rake observe mongodb` → Returns **both** (user chooses)
- `garden-rake observe mongodb@adopted` → Returns specific instance
- `garden-rake rest mongodb` → **Interactive prompt** if ambiguous

**Interactive prompt example**:
```
$ garden-rake rest mongodb

Multiple 'mongodb' offerings found:
  1. mongodb@managed (localhost:27017, container)
  2. mongodb@adopted (localhost:5432, native process)

Which offering? [1/2]: 1
Resting mongodb@managed...
```

---

## Data Models

### Core Types

**IMPORTANT**: All optional fields use `#[serde(skip_serializing_if = "Option::is_none")]` to ensure they're **completely omitted** when not present.

```rust
#[derive(Debug, Clone, Serialize, Deserialize)]
pub enum OfferingMode {
    /// Managed container with full Moss control
    Managed,

    /// Existing service (native or containerized) with configurable control
    Adopted,

    /// External network service (announce-only)
    Borrowed,
}

#[derive(Debug, Clone, Serialize, Deserialize)]
pub enum AdoptedControlLevel {
    /// Moss manages full lifecycle (start/stop/restart)
    Full,

    /// Moss monitors health but doesn't control
    Monitor,

    /// Moss only announces existence (discovery only)
    Announce,
}

#[derive(Debug, Clone, Serialize, Deserialize)]
pub struct Offering {
    pub id: String,                    // "mongodb@managed", "redis@adopted"
    pub name: String,                  // "mongodb", "redis"
    pub mode: OfferingMode,
    pub status: OfferingStatus,
    pub location: ServiceLocation,
    pub metadata: OfferingMetadata,

    #[serde(skip_serializing_if = "Option::is_none", default)]
    pub control_config: Option<ControlConfig>,
}

#[derive(Debug, Clone, Serialize, Deserialize)]
pub struct ServiceLocation {
    pub host: String,
    pub port: u16,
    pub protocol: String,    // "http", "tcp", "mongodb", etc.
}

#[derive(Debug, Clone, Serialize, Deserialize)]
pub struct ControlConfig {
    #[serde(default = "default_control_level")]
    pub level: AdoptedControlLevel,

    #[serde(skip_serializing_if = "Option::is_none", default)]
    pub start_command: Option<String>,

    #[serde(skip_serializing_if = "Option::is_none", default)]
    pub stop_command: Option<String>,

    #[serde(skip_serializing_if = "Option::is_none", default)]
    pub restart_command: Option<String>,

    #[serde(skip_serializing_if = "Option::is_none", default)]
    pub health_check: Option<HealthCheckConfig>,
}

fn default_control_level() -> AdoptedControlLevel {
    AdoptedControlLevel::Monitor
}
```

### Manifest Complexity Tiers

**Philosophy**: Keep manifests as simple as needed. Start minimal, add features only when required.

**CRITICAL**: Optional fields must **NOT EXIST** in the manifest. Don't use `null`, `{}`, or `[]` - **completely omit** optional fields.

#### Tier 1: Minimal Manifest (Required Fields Only)

**Adopted Mode - Absolute Minimum**:
```yaml
# my-service.offering.yaml - 4 lines total
name: my-service
modes: ['adopted']
detection:
  - method: command
    command: "my-service --version"
```

**Managed Mode - Absolute Minimum**:
```yaml
# my-container.offering.yaml - 6 lines total
name: my-container
modes: ['managed']
container:
  image: "my-image:latest"
  ports:
    - "8080:8080"
```

**That's it!** No `health_check: null`, no `volumes: []`, no `control: {}`. If it's optional, **don't include it**.

#### Tier 2: Common Use Cases

Add basic health checks and control:

```yaml
name: ollama
modes: ['adopted']
detection:
  - method: command
    command: "ollama --version"
    service:
      protocol: http
      port: 11434
      health_check:              # OPTIONAL
        endpoint: "/api/health"

    control:                     # OPTIONAL
      level: monitor             # Default: monitor (safe)
```

#### Tier 3: Advanced Features

Full featured with all optional enhancements:

```yaml
name: ollama
modes: ['managed', 'adopted']
version: "0.1.0"
auto_adopt: true                 # OPTIONAL - Default: false

# ===== DETECTION RULES (for Adopted mode) =====
detection:
  - name: bare-metal
    priority: 1                  # OPTIONAL - Default: 0
    can_parallelize_check: false # OPTIONAL - Default: true
    cost: cheap                  # OPTIONAL - Default: cheap
    method: command
    command: "ollama --version"
    validation:                  # OPTIONAL - Default: check exit code
      type: regex
      pattern: "ollama version is (\\d+\\.\\d+\\.\\d+)"
      capture_group: 1

    stability:                   # OPTIONAL - Prevent flapping
      min_consecutive_successes: 2  # Default: 1
      min_consecutive_failures: 3   # Default: 1

    service:
      protocol: http
      port: 11434
      health_check:              # OPTIONAL
        endpoint: "/api/health"
        interval_seconds: 30
        graduated:               # OPTIONAL - Advanced health checks
          - interval: 5s         # First 5 min: aggressive
            duration: 5m
          - interval: 30s        # After 5 min: normal
            duration: forever

    control:                     # OPTIONAL - Default: monitor
      level: full                # Options: full, monitor, announce
      start_command: "systemctl start ollama"
      stop_command: "systemctl stop ollama"
      restart_command: "systemctl restart ollama"
      windows:                   # OPTIONAL - Platform-specific
        start_command: "net start ollama"
        stop_command: "net stop ollama"

  - name: containerized
    priority: 2
    can_parallelize_check: true
    cost: expensive
    method: container_inspect
    runtime: auto
    filters:
      image_contains: "ollama/ollama"
      status: running

    service:
      protocol: http
      port_mapping: "11434/tcp"
      health_check:
        method: container_health

    control:
      level: full
      start_command: "{runtime} start {container_id}"
      stop_command: "{runtime} stop {container_id}"
      restart_command: "{runtime} restart {container_id}"

# ===== DEPLOYMENT CONFIG (for Managed mode) =====
container:
  image: "ollama/ollama:latest"
  ports:
    - "11434:11434"
  volumes:                       # OPTIONAL
    - type: named
      name: ollama-data
      mount: /root/.ollama
  environment:                   # OPTIONAL
    OLLAMA_HOST: "0.0.0.0"

  resources:                     # OPTIONAL
    memory_mb: 4096
    cpu_cores: 2

  health_check:                  # OPTIONAL
    test: "curl -f http://localhost:11434/api/health || exit 1"
    interval_seconds: 30
    timeout_seconds: 5
    retries: 3

  update_strategy:               # OPTIONAL - Default: rolling
    type: blue-green
    smoke_tests:                 # OPTIONAL
      - endpoint: "/api/health"
        expect_status: 200

# ===== COMPATIBILITY (validates against Stone capabilities) =====
compatibility:                   # OPTIONAL
  requires_ai_any:
    - cuda
    - rocm
    - cpu

# ===== CONNECTION TEMPLATE (for flexible payloads) =====
connection_template:             # OPTIONAL - Default: basic JSON
  type: templated_json
  template: |
    {
      "endpoint": "http://{{host}}:{{port}}",
      "host": "{{host}}",
      "port": {{port}}
    }
  variables:
    host: { source: location, key: host }
    port: { source: location, key: port }
```

### Default Values (When Optional Fields Omitted)

**CRITICAL**: Use `#[serde(default)]` for all optional fields. Never require fields to exist in YAML.

```rust
#[derive(Debug, Clone, Serialize, Deserialize)]
pub struct DetectionRule {
    // REQUIRED fields (no default)
    pub method: DetectionMethod,
    pub command: String,  // Required if method=command

    // OPTIONAL fields (omit from manifest if not needed)
    #[serde(skip_serializing_if = "Option::is_none", default)]
    pub name: Option<String>,

    #[serde(default)]
    pub priority: i32,  // Default: 0

    #[serde(default = "default_true")]
    pub can_parallelize_check: bool,  // Default: true

    #[serde(default)]
    pub cost: DetectionCost,  // Default: Cheap

    #[serde(skip_serializing_if = "Option::is_none", default)]
    pub validation: Option<ValidationRule>,  // Default: None (use exit code)

    #[serde(skip_serializing_if = "Option::is_none", default)]
    pub stability: Option<StabilityConfig>,  // Default: None (no flapping prevention)

    #[serde(skip_serializing_if = "Option::is_none", default)]
    pub service: Option<ServiceConfig>,

    #[serde(skip_serializing_if = "Option::is_none", default)]
    pub control: Option<ControlConfig>,
}

#[derive(Debug, Clone, Serialize, Deserialize)]
pub struct HealthCheckConfig {
    // Required field
    pub endpoint: String,

    // Optional fields - OMIT from manifest if not needed
    #[serde(default = "default_health_interval")]
    pub interval_seconds: u32,  // Default: 30

    #[serde(default = "default_health_timeout")]
    pub timeout_seconds: u32,  // Default: 5

    #[serde(default = "default_health_retries")]
    pub retries: u32,  // Default: 3

    #[serde(skip_serializing_if = "Option::is_none", default)]
    pub graduated: Option<Vec<GraduatedHealthCheck>>,  // Default: None
}

#[derive(Debug, Clone, Serialize, Deserialize)]
pub struct ContainerConfig {
    // REQUIRED fields
    pub image: String,
    pub ports: Vec<String>,

    // OPTIONAL fields - OMIT from manifest if not needed
    #[serde(skip_serializing_if = "Vec::is_empty", default)]
    pub volumes: Vec<VolumeMount>,  // Default: empty vec

    #[serde(skip_serializing_if = "HashMap::is_empty", default)]
    pub environment: HashMap<String, String>,  // Default: empty map

    #[serde(skip_serializing_if = "Option::is_none", default)]
    pub resources: Option<ResourceLimits>,  // Default: None

    #[serde(skip_serializing_if = "Option::is_none", default)]
    pub health_check: Option<HealthCheckConfig>,  // Default: None

    #[serde(default)]
    pub update_strategy: UpdateStrategy,  // Default: Rolling
}

fn default_true() -> bool { true }
fn default_health_interval() -> u32 { 30 }
fn default_health_timeout() -> u32 { 5 }
fn default_health_retries() -> u32 { 3 }
```

**Anti-Pattern Examples**:

```yaml
# ❌ BAD - Don't do this!
detection:
  - method: command
    command: "my-service --version"
    priority: null           # Don't include if using default
    validation: null         # Don't include if not needed
    stability: {}            # Don't include empty objects
    service: null            # Don't include if not needed
    control:
      level: monitor
      start_command: null    # Don't include null values

# ✅ GOOD - Omit optional fields entirely
detection:
  - method: command
    command: "my-service --version"
    # That's it! Everything else is defaulted
```

### Required vs Optional Fields - Quick Reference

| Field | Adopted | Managed | Default | Notes |
|-------|---------|---------|---------|-------|
| `name` | ✅ Required | ✅ Required | - | Offering identifier |
| `modes` | ✅ Required | ✅ Required | - | Array of modes |
| `detection` | ✅ Required | ⚪ N/A | - | How to detect service |
| `detection.method` | ✅ Required | ⚪ N/A | - | command/container_inspect/http_probe |
| `detection.command` | ✅ (if method=command) | ⚪ N/A | - | Command to run |
| `container` | ⚪ N/A | ✅ Required | - | Container configuration |
| `container.image` | ⚪ N/A | ✅ Required | - | Docker image |
| `container.ports` | ⚪ N/A | ✅ Required | - | Port mappings |
| `version` | ⚪ Optional | ⚪ Optional | "0.0.0" | Manifest version |
| `auto_adopt` | ⚪ Optional | ⚪ N/A | false | Auto-adopt on boot |
| `detection.priority` | ⚪ Optional | ⚪ N/A | 0 | Rule priority |
| `detection.validation` | ⚪ Optional | ⚪ N/A | exit_code | Output validation |
| `detection.stability` | ⚪ Optional | ⚪ N/A | 1/1 | Flapping prevention |
| `detection.service` | ⚪ Optional | ⚪ N/A | {} | Service metadata |
| `detection.control` | ⚪ Optional | ⚪ N/A | monitor | Control level |
| `container.volumes` | ⚪ N/A | ⚪ Optional | [] | Volume mounts |
| `container.environment` | ⚪ N/A | ⚪ Optional | {} | Environment vars |
| `container.resources` | ⚪ N/A | ⚪ Optional | none | CPU/memory limits |
| `container.health_check` | ⚪ N/A | ⚪ Optional | none | Health check config |
| `compatibility` | ⚪ Optional | ⚪ Optional | none | Hardware requirements |
| `connection_template` | ⚪ Optional | ⚪ Optional | basic | Connection payload format |

**Legend**: ✅ Required, ⚪ Optional, ⚪ N/A (not applicable for this mode)

---

## Detection Engine

### Parallel Detection with Timeout

```rust
/// Detect all adoptable offerings in parallel
pub async fn detect_adoptable_offerings(
    manifests: Vec<OfferingManifest>,
) -> Vec<DetectedOffering> {
    use futures_util::stream::{self, StreamExt};

    // Parallel detection with concurrency limit
    let detected = stream::iter(manifests)
        .map(|manifest| async move {
            // For each manifest, run detection rules by priority
            let mut results = Vec::new();

            // Sort by priority (higher = check first)
            let mut rules = manifest.detection.clone();
            rules.sort_by_key(|r| std::cmp::Reverse(r.priority));

            for rule in rules {
                // Skip expensive checks that can be cached
                if rule.cost == DetectionCost::Expensive {
                    if let Some(cached) = DETECTION_CACHE.get(&manifest.name, &rule.name).await {
                        if !cached.is_expired() {
                            results.push(cached.result.clone());
                            continue;
                        }
                    }
                }

                // Run detection with timeout
                match detect_offering_by_rule(&rule, &manifest).await {
                    Ok(detected) => {
                        // Cache expensive detections
                        if rule.cost == DetectionCost::Expensive {
                            DETECTION_CACHE.set(&manifest.name, &rule.name, &detected, Duration::from_secs(300)).await;
                        }
                        results.push(detected);
                        break;  // Found via this rule, stop checking
                    }
                    Err(e) => {
                        tracing::debug!("Detection failed for {} via {}: {}", manifest.name, rule.name, e);
                        continue;  // Try next rule
                    }
                }
            }

            results
        })
        .buffer_unordered(10)  // Run up to 10 manifests in parallel
        .collect::<Vec<_>>()
        .await;

    detected.into_iter().flatten().flatten().collect()
}

async fn detect_offering_by_rule(
    rule: &DetectionRule,
    manifest: &OfferingManifest,
) -> Result<DetectedOffering> {
    match rule.method {
        DetectionMethod::Command => {
            tokio::time::timeout(
                Duration::from_secs(5),
                execute_command_detection(rule, manifest)
            ).await?
        }
        DetectionMethod::ContainerInspect => {
            detect_containerized_offering(rule, manifest).await
        }
        DetectionMethod::HttpProbe => {
            tokio::time::timeout(
                Duration::from_secs(3),
                probe_http_endpoint(rule, manifest)
            ).await?
        }
    }
}
```

### Detection Caching Strategy

```rust
pub struct DetectionCache {
    /// Hardware capabilities (perennial - once per lifecycle)
    hardware: OnceCell<HardwareCapabilities>,

    /// Software detections (5-min TTL with 4-min proactive refresh)
    software: DashMap<(String, String), CachedDetection>,
}

impl DetectionCache {
    pub async fn get(&self, offering: &str, rule: &str) -> Option<CachedDetection> {
        let key = (offering.to_string(), rule.to_string());
        self.software.get(&key).map(|entry| entry.clone())
    }

    pub async fn set(&self, offering: &str, rule: &str, result: &DetectedOffering, ttl: Duration) {
        let key = (offering.to_string(), rule.to_string());
        let cache_entry = CachedDetection {
            result: result.clone(),
            cached_at: Instant::now(),
            ttl,
        };

        // Proactive refresh at 80% of TTL (4min for 5min TTL)
        let refresh_at = ttl.mul_f32(0.8);
        let offering_clone = offering.to_string();
        let rule_clone = rule.to_string();

        tokio::spawn(async move {
            tokio::time::sleep(refresh_at).await;

            // Background refresh - don't block
            if let Ok(new_result) = detect_offering_by_rule(&rule_clone, &manifest).await {
                self.set(&offering_clone, &rule_clone, &new_result, ttl).await;
            }
        });

        self.software.insert(key, cache_entry);
    }
}
```

---

## Secrets Manager Architecture

### Platform-Adaptive Backend Selection

```rust
pub struct SecretsManager {
    backend: Box<dyn SecretBackend>,
}

impl SecretsManager {
    pub fn new() -> Self {
        // Priority order (best to worst):
        // 1. TPM (hardware-backed) - ALWAYS use if available
        // 2. Platform keyring (Keychain, DPAPI, Secret Service)
        // 3. Encrypted file (fallback)

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
}

pub trait SecretBackend: Send + Sync {
    fn name(&self) -> &str;
    fn is_available() -> bool where Self: Sized;
    fn store(&self, key: &str, secret: &[u8]) -> Result<()>;
    fn retrieve(&self, key: &str) -> Result<Option<Vec<u8>>>;
    fn delete(&self, key: &str) -> Result<()>;
}
```

### TPM Backend (Windows & Linux)

```rust
pub struct TpmBackend {
    tpm_context: TpmContext,
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
        // Seal secret with PCR values (chosen by security specialist based on scope)
        // PCR selection criteria:
        // - PCR0: Firmware integrity
        // - PCR7: Secure Boot state
        // - PCR14: MOR (Memory Overwrite Request) - protects against cold boot attacks
        self.tpm_context.seal(key, secret, &[0, 7, 14])
    }

    fn retrieve(&self, key: &str) -> Result<Option<Vec<u8>>> {
        // Unseal only if PCR values match (hardware state unchanged)
        self.tpm_context.unseal(key)
    }
}
```

**Security Specialist Recommendation - TPM PCR Selection**:
- **PCR0 + PCR7**: Binds secrets to firmware and Secure Boot state (prevents offline attacks)
- **PCR14**: Protects against cold boot attacks (requires system reboot to change)
- **Scope**: System-wide credentials (no per-user distinction in garden)

---

## API Endpoints

### Offerings API

```http
# List all offerings (managed, adoptable, borrowed)
GET /api/v1/offerings?mode=all
GET /api/v1/offerings?mode=adopted       # Only adoptable
GET /api/v1/offerings?mode=managed       # Only managed

# Response
{
  "offerings": [
    {
      "id": "ollama@adopted",
      "name": "ollama",
      "mode": "adopted",
      "status": "running",
      "location": {
        "host": "localhost",
        "port": 11434,
        "protocol": "http"
      },
      "control_level": "monitor",
      "detected_at": "2026-01-21T10:30:00Z"
    },
    {
      "id": "postgres@managed",
      "name": "postgres",
      "mode": "managed",
      "status": "running",
      "location": {
        "host": "localhost",
        "port": 5432,
        "protocol": "tcp"
      },
      "container": {
        "id": "a3f7b2c1...",
        "image": "postgres:16-alpine",
        "runtime": "docker"
      },
      "deployed_at": "2026-01-20T15:00:00Z"
    }
  ]
}

# Adopt offering
POST /api/v1/offerings/adopt
Body: {
  "name": "mongodb",
  "control_level": "monitor"  # Optional override
}

# Adopt all detected offerings
POST /api/v1/offerings/adopt-all
Body: {
  "control_level": "monitor"  # Default control level
}

# Connection payload (flexible JSON based on offering type)
GET /api/v1/offerings/{id}/connection
```

### Connection Payload Generation

**Connection payloads are OPTIONAL and template-driven from manifests**.

#### Default Behavior (No Template)

If no `connection_template` is specified, Moss generates a basic JSON payload:

```json
{
  "host": "localhost",
  "port": 5432,
  "protocol": "tcp"
}
```

**This is sufficient for most simple use cases.**

#### Custom Connection Templates (Optional)

For complex connection formats, provide a template:

```yaml
# postgres.offering.yaml - OPTIONAL advanced template
connection_template:
  type: templated_json
  template: |
    {
      "connection_string": "postgresql://{{username}}:{{password}}@{{host}}:{{port}}/{{database}}",
      "host": "{{host}}",
      "port": {{port}},
      "database": "{{database}}",
      "ssl": {{ssl_enabled}}
    }
  variables:
    username: { source: credentials, key: username }
    password: { source: credentials, key: password }
    host: { source: location, key: host }
    port: { source: location, key: port }
    database: { source: config, key: database, default: "postgres" }
    ssl_enabled: { source: config, key: ssl, default: false }

# nas-share.offering.yaml - OPTIONAL custom format
connection_template:
  type: templated_json
  template: |
    {
      "unc_path": "//{{host}}/{{share_name}}",
      "mount_point": "/mnt/{{mount_name}}",
      "credentials": {{credentials_json}}
    }
  variables:
    host: { source: location, key: host }
    share_name: { source: config, key: share_name }
    mount_name: { source: config, key: mount_name }
    credentials_json: { source: credentials, key: full, format: json }
```

**Implementation**:

```rust
// Generic connection payload generator (no hardcoded service types)
pub fn generate_connection_payload(offering: &Offering) -> Result<serde_json::Value> {
    // Default: simple JSON if no template specified
    let Some(template) = &offering.manifest.connection_template else {
        return Ok(serde_json::json!({
            "host": offering.location.host,
            "port": offering.location.port,
            "protocol": offering.location.protocol,
        }));
    };

    // Custom template: render with variables
    let mut context = tera::Context::new();

    for (var_name, var_spec) in &template.variables {
        let value = match var_spec.source.as_str() {
            "location" => offering.location.get(&var_spec.key)?,
            "credentials" => offering.credentials.get(&var_spec.key)?,
            "config" => offering.config.get(&var_spec.key).or(var_spec.default.as_ref())?,
            _ => return Err(anyhow::anyhow!("Unknown variable source: {}", var_spec.source)),
        };

        context.insert(var_name, &value);
    }

    // Render template
    let rendered = tera::Tera::one_off(&template.template, &context, false)?;

    // Parse as JSON
    let payload: serde_json::Value = serde_json::from_str(&rendered)?;

    Ok(payload)
}
```

**Example API Response** (generated from template above):

```http
GET /api/v1/offerings/postgres@adopted/connection
{
  "connection_string": "postgresql://admin:secret@localhost:5432/mydb",
  "host": "localhost",
  "port": 5432,
  "database": "mydb",
  "ssl": false
}
```

**Key Point**: The response format is defined entirely in the manifest. Code never knows about "postgresql://" format or specific field names.

---

## Deployment Strategies

### Blue-Green Deployment (Managed Mode)

```rust
pub async fn blue_green_deploy(
    offering: &ManagedOffering,
    new_image: &str,
) -> Result<()> {
    // 1. Pre-flight check: Ensure enough resources
    if !can_deploy_blue_green(offering).await? {
        tracing::info!("Insufficient resources for blue-green, falling back to rolling update");
        return rolling_update(offering, new_image).await;
    }

    // 2. Deploy "green" environment (new version)
    let green = runtime.create_container(ContainerConfig {
        image: new_image,
        ports: vec![],  // No external ports yet
        volumes: offering.volumes.clone(),
        networks: vec!["internal"],
    }).await?;

    runtime.start_container(&green.id).await?;

    // 3. Wait for health check
    wait_for_healthy(&green, Duration::from_secs(30)).await?;

    // 4. Smoke tests (if configured)
    if let Some(smoke_tests) = &offering.update_strategy.smoke_tests {
        run_smoke_tests(&green, smoke_tests).await?;
    }

    // 5. Atomic swap: Update port bindings
    runtime.update_port_bindings(&green.id, &offering.port_bindings).await?;
    runtime.update_port_bindings(&offering.blue_id, &[]).await?;

    // 6. Grace period for connection draining
    tokio::time::sleep(Duration::from_secs(10)).await;

    // 7. Stop and remove blue environment
    runtime.stop_container(&offering.blue_id, Duration::from_secs(10)).await?;
    runtime.remove_container(&offering.blue_id).await?;

    // 8. Green becomes new blue
    offering.blue_id = green.id;

    Ok(())
}

async fn can_deploy_blue_green(offering: &ManagedOffering) -> Result<bool> {
    // Check disk space (need 2x for blue + green)
    let required_space = offering.image_size * 2;
    let available_space = get_available_disk_space().await?;

    if available_space < required_space {
        return Ok(false);
    }

    // Check memory (if limits specified)
    if let Some(mem_limit) = offering.memory_limit {
        let available_mem = get_available_memory().await?;
        if available_mem < mem_limit * 2 {
            return Ok(false);
        }
    }

    Ok(true)
}
```

---

## Bootstrap Auto-Adoption

### Configuration

Auto-adoption behavior is controlled by deployment profile:

```toml
# moss.toml (or .zen-garden/moss.toml)
[adoption]
# Auto-adopt enabled by default for regular installations
# Disabled for USB/portable Moss (self-contained by design)
enabled = true              # Default: true for regular, false for USB

# Control levels for auto-adopted services
default_control_level = "monitor"   # Options: full, monitor, announce

# Exclude specific offerings from auto-adoption
exclude = ["postgres"]      # Don't auto-adopt these
```

**Deployment Profiles**:
- **Regular installation** (apt/rpm/installer): `auto_adopt.enabled = true`
- **USB/Portable Moss**: `auto_adopt.enabled = false`
- **Container/Docker**: `auto_adopt.enabled = false` (isolated environment)

**Rationale**: USB Moss is a managed, portable garden that should NOT adopt host services. It's meant to be self-contained.

### Implementation

```rust
// On moss startup (main.rs)
async fn bootstrap_adoption(state: AppState, config: &MossConfig) {
    // Check if auto-adoption is enabled for this deployment profile
    if !config.adoption.enabled {
        tracing::info!("Auto-adoption disabled for this deployment profile (USB/portable mode)");
        return;
    }

    tracing::info!("Starting bootstrap adoption...");

    // 1. Load manifests marked as 'auto_adopt: true'
    let manifests = load_manifests()
        .into_iter()
        .filter(|m| {
            // Check manifest auto_adopt flag
            if !m.auto_adopt.unwrap_or(false) {
                return false;
            }

            // Check exclude list
            if config.adoption.exclude.contains(&m.name) {
                tracing::debug!("Skipping auto-adoption of {} (excluded in config)", m.name);
                return false;
            }

            true
        })
        .collect::<Vec<_>>();

    tracing::debug!("Found {} manifests with auto_adopt enabled", manifests.len());

    // 2. Detect in parallel (background task)
    let default_control_level = config.adoption.default_control_level.clone();
    tokio::spawn(async move {
        let detected = detect_adoptable_offerings(manifests).await;

        tracing::info!("Auto-adoption detected {} offerings", detected.len());

        // 3. Register each detected offering
        for mut offering in detected {
            // Apply default control level if not specified
            if offering.control_config.is_none() {
                offering.control_config = Some(ControlConfig {
                    level: default_control_level.clone(),
                    ..Default::default()
                });
            }

            match register_adopted_offering(&state, offering).await {
                Ok(id) => {
                    tracing::info!("Auto-adopted offering: {} ({})", offering.name, id);
                }
                Err(e) => {
                    tracing::warn!("Failed to auto-adopt {}: {}", offering.name, e);
                }
            }
        }
    });
}

/// Detect deployment profile (regular vs USB vs container)
fn detect_deployment_profile() -> DeploymentProfile {
    // Check for USB deployment marker
    if std::path::Path::new("/mnt/.zen-garden-usb").exists() ||
       std::env::var("ZEN_GARDEN_USB").is_ok() {
        return DeploymentProfile::UsbPortable;
    }

    // Check for container environment
    if std::path::Path::new("/.dockerenv").exists() ||
       std::path::Path::new("/run/.containerenv").exists() {
        return DeploymentProfile::Container;
    }

    // Default: regular installation
    DeploymentProfile::Regular
}

#[derive(Debug, Clone, PartialEq)]
enum DeploymentProfile {
    Regular,        // Installed on bare metal/VM
    UsbPortable,    // USB stick/portable drive
    Container,      // Docker/Podman container
}

impl DeploymentProfile {
    fn default_auto_adopt_enabled(&self) -> bool {
        match self {
            Self::Regular => true,           // Adopt host services
            Self::UsbPortable => false,      // Self-contained, don't adopt host
            Self::Container => false,        // Isolated, don't adopt host
        }
    }
}
```

---

## File Structure

```
.zen-garden/
  capabilities.json         # Hardware capabilities (perennial)
  offerings.json            # Offering registry (dynamic)
  secrets/                  # Encrypted credentials (TPM/keyring/file)
    mongodb@borrowed.cred
    nas-share@borrowed.cred
  manifests/                # Offering manifests
    ollama.yaml
    postgres.yaml
    mongodb.yaml
```

---

## Manifest Loading & Validation

### Manifest Discovery

```rust
/// Load all offering manifests from standard locations
pub fn load_manifests() -> Result<Vec<OfferingManifest>> {
    let manifest_dirs = vec![
        PathBuf::from(".zen-garden/manifests"),           // User manifests
        PathBuf::from("/etc/zen-garden/manifests"),       // System manifests
        PathBuf::from("/usr/share/zen-garden/manifests"), // Distribution manifests
    ];

    let mut manifests = Vec::new();

    for dir in manifest_dirs {
        if !dir.exists() {
            continue;
        }

        for entry in std::fs::read_dir(dir)? {
            let entry = entry?;
            let path = entry.path();

            // Only load .yaml or .yml files
            if !path.extension().map_or(false, |ext| ext == "yaml" || ext == "yml") {
                continue;
            }

            // Parse and validate manifest
            match parse_manifest(&path) {
                Ok(manifest) => {
                    // Validate manifest structure
                    if let Err(e) = validate_manifest(&manifest) {
                        tracing::warn!("Invalid manifest {}: {}", path.display(), e);
                        continue;
                    }

                    manifests.push(manifest);
                }
                Err(e) => {
                    tracing::warn!("Failed to parse manifest {}: {}", path.display(), e);
                }
            }
        }
    }

    Ok(manifests)
}

fn validate_manifest(manifest: &OfferingManifest) -> Result<()> {
    // ===== REQUIRED FIELD VALIDATION =====

    // Ensure name is valid (no special characters, etc.)
    if !manifest.name.chars().all(|c| c.is_alphanumeric() || c == '-' || c == '_') {
        return Err(anyhow::anyhow!("Invalid offering name: {}", manifest.name));
    }

    // Ensure at least one mode is specified
    if manifest.modes.is_empty() {
        return Err(anyhow::anyhow!("At least one mode must be specified"));
    }

    // If 'adopted' mode, ensure detection rules exist with minimum required fields
    if manifest.modes.contains(&"adopted".to_string()) {
        if manifest.detection.is_empty() {
            return Err(anyhow::anyhow!("Adopted mode requires at least one detection rule"));
        }

        // Validate each detection rule has required fields only
        for (idx, rule) in manifest.detection.iter().enumerate() {
            if rule.method.is_none() {
                return Err(anyhow::anyhow!("Detection rule {} missing required 'method' field", idx));
            }

            // Validate method-specific requirements
            match rule.method.as_ref().unwrap().as_str() {
                "command" if rule.command.is_none() => {
                    return Err(anyhow::anyhow!("Detection rule {} method 'command' requires 'command' field", idx));
                }
                _ => {} // All other fields are optional
            }
        }
    }

    // If 'managed' mode, ensure container config exists with minimum required fields
    if manifest.modes.contains(&"managed".to_string()) {
        let container = manifest.container.as_ref()
            .ok_or_else(|| anyhow::anyhow!("Managed mode requires 'container' config"))?;

        if container.image.is_none() {
            return Err(anyhow::anyhow!("Container config requires 'image' field"));
        }
        if container.ports.is_empty() {
            return Err(anyhow::anyhow!("Container config requires at least one port mapping"));
        }

        // All other container fields (volumes, environment, resources, health_check) are OPTIONAL
    }

    // ===== OPTIONAL FIELD VALIDATION =====

    // Validate connection template if present (OPTIONAL)
    if let Some(template) = &manifest.connection_template {
        validate_connection_template(template)?;
    }

    // Validate stability config if present (OPTIONAL)
    if let Some(detection) = manifest.detection.first() {
        if let Some(stability) = &detection.stability {
            if stability.min_consecutive_successes == 0 {
                return Err(anyhow::anyhow!("Stability min_consecutive_successes must be >= 1"));
            }
            if stability.min_consecutive_failures == 0 {
                return Err(anyhow::anyhow!("Stability min_consecutive_failures must be >= 1"));
            }
        }
    }

    Ok(())
}
```

### Manifest Hot-Reload

```rust
/// Watch manifest directories for changes and reload
pub async fn watch_manifests(state: AppState) {
    use notify::{Watcher, RecursiveMode, watcher};

    let (tx, rx) = std::sync::mpsc::channel();
    let mut watcher = watcher(tx, Duration::from_secs(2)).unwrap();

    // Watch manifest directories
    for dir in &[".zen-garden/manifests", "/etc/zen-garden/manifests"] {
        if PathBuf::from(dir).exists() {
            watcher.watch(dir, RecursiveMode::NonRecursive).ok();
        }
    }

    // Process file changes
    while let Ok(event) = rx.recv() {
        match event {
            notify::DebouncedEvent::Create(path) |
            notify::DebouncedEvent::Write(path) |
            notify::DebouncedEvent::Remove(path) => {
                tracing::info!("Manifest changed: {}", path.display());

                // Reload all manifests
                match load_manifests() {
                    Ok(manifests) => {
                        state.manifests.write().await.replace(manifests);
                        tracing::info!("Reloaded {} manifests", state.manifests.read().await.as_ref().map_or(0, |m| m.len()));
                    }
                    Err(e) => {
                        tracing::error!("Failed to reload manifests: {}", e);
                    }
                }
            }
            _ => {}
        }
    }
}
```

---

## Phase 1 Implementation (2 weeks)

**CRITICAL**: All structs must use `Option<T>` for optional fields and provide `Default` implementations.

### Core Infrastructure
- [ ] **Manifest system**:
  - [ ] Manifest schema definition (`OfferingManifest` struct with minimal required fields)
  - [ ] **All optional fields use `Option<T>`** (no required bloat)
  - [ ] Manifest loader (multi-directory, `.yaml` files)
  - [ ] Manifest validator (validates ONLY required fields, all else optional)
  - [ ] Manifest hot-reload (watch for changes)
  - [ ] **VERIFY**: Zero hardcoded service names in codebase

### Data Models
- [ ] `OfferingMode`, `AdoptedOffering`, `ManagedOffering`, `BorrowedOffering`
- [ ] `ControlConfig`, `DetectionRule`, `ConnectionTemplate` (all with `Default` impls)
- [ ] **Stability thresholds** (OPTIONAL - default 1/1, no flapping prevention)
- [ ] **Graduated health checks** (OPTIONAL - default single interval)
- [ ] Registry storage: `offerings.json` persistence

### Detection Engine
- [ ] Parallel detection with timeout
- [ ] Detection caching (OPTIONAL - default no caching for cheap checks)
- [ ] Proactive cache refresh (OPTIONAL - only for expensive checks)
- [ ] Container inspect detection
- [ ] Command execution detection
- [ ] **Stability tracking** (OPTIONAL - only when configured in manifest)

### API Endpoints
- [ ] `GET /api/v1/offerings?mode=all|adopted|managed|borrowed`
- [ ] `POST /api/v1/offerings/adopt`
- [ ] `POST /api/v1/offerings/adopt-all`
- [ ] `GET /api/v1/offerings/{id}/connection` (default basic JSON, template optional)

### CLI Commands
- [ ] `garden-rake adopt <name>`
- [ ] `garden-rake adopt all`
- [ ] `garden-rake adopt <name> at <stone> with <control-level>`
- [ ] Namespace collision handling (interactive prompt)

### Bootstrap
- [ ] Deployment profile detection (regular/USB/container)
- [ ] Auto-adoption on moss startup (OPTIONAL - configurable, default varies by profile)
- [ ] Config file support (`moss.toml` adoption section)

### Testing
- [ ] Manifest loading tests
- [ ] **Minimal manifest tests** (test with absolutely minimal valid manifests)
- [ ] **NO hardcoded service names in tests** - use generic test manifests
- [ ] Detection engine tests (mocked commands/containers)
- [ ] Template rendering tests (including default no-template case)
- [ ] **Validation tests**: Ensure optional fields are truly optional

### Implementation Guidelines
- [ ] **Never require optional features** - code must work with minimal manifests
- [ ] **Provide sensible defaults** for all optional features
- [ ] **Document defaults** in code comments and manifest examples
- [ ] **Test minimal configurations first** before adding optional features

---

## Success Criteria

### Functional Requirements
1. ✅ **Dual-mode support**: Offerings can be both managed AND adopted
2. ✅ **Namespace collision**: Multiple instances of same offering work correctly
3. ✅ **Auto-adoption**: Services detected on boot (except USB/container deployments)
4. ✅ **TPM integration**: Secrets always use TPM when available
5. ✅ **Detection efficiency**: Parallel detection with caching and proactive refresh
6. ✅ **Zen commands**: `adopt`, `offer` verbs work naturally
7. ✅ **Connection payloads**: Flexible JSON format per offering type

### Architectural Requirements
8. ✅ **ZERO hardcoded service names**: All service logic is manifest-driven
9. ✅ **Manifest extensibility**: Adding new offerings requires zero code changes
10. ✅ **Template-driven connections**: Connection payloads generated from manifests
11. ✅ **Hot-reload support**: Manifest changes detected and applied without restart

### Verification Checklist

Before merging implementation:

- [ ] **Code audit**: Search codebase for hardcoded service names
  ```bash
  # These searches should return ZERO results in src/
  rg '"mongodb"' src/
  rg '"postgres"' src/
  rg '"redis"' src/
  rg '"ollama"' src/
  rg 'match.*"(mongodb|postgres|redis)"' src/
  ```

- [ ] **Test manifest diversity**: Tests use at least 3 different generic manifests
  - [ ] Not using real service names (mongodb, postgres, etc.)
  - [ ] Using generic names like "test-service-a", "test-db-1", etc.

- [ ] **Manifest-only additions**: Verify adding new offering requires:
  - [ ] Creating `new-service.yaml` manifest
  - [ ] Placing in `.zen-garden/manifests/`
  - [ ] **NO code changes**
  - [ ] **NO configuration changes**

- [ ] **Connection template coverage**: All offering types have connection templates
  - [ ] Templates render correctly with real data
  - [ ] Variables resolve from correct sources (location, credentials, config)

---

## Anti-Patterns to Avoid

### ❌ NEVER DO THIS

```rust
// Hardcoded service-specific logic
if offering.name == "mongodb" {
    deploy_mongodb(&offering);
} else if offering.name == "postgres" {
    deploy_postgres(&offering);
}

// Hardcoded connection formats
fn generate_connection_string(offering: &Offering) -> String {
    match offering.type.as_str() {
        "database" => format!("postgresql://{}:{}", offering.host, offering.port),
        "cache" => format!("redis://{}:{}", offering.host, offering.port),
        _ => "unknown".to_string()
    }
}

// Hardcoded health check logic
fn check_health(service: &str) -> bool {
    match service {
        "mongodb" => check_mongodb_health(),
        "redis" => check_redis_health(),
        _ => false
    }
}
```

### ✅ ALWAYS DO THIS

```rust
// Generic deployment from manifest
fn deploy_offering(offering: &Offering) -> Result<()> {
    match offering.mode {
        OfferingMode::Managed => deploy_managed(&offering.manifest.container),
        OfferingMode::Adopted => adopt_service(&offering.manifest.detection),
        OfferingMode::Borrowed => register_borrowed(&offering.manifest.location),
    }
}

// Template-driven connection generation
fn generate_connection_payload(offering: &Offering) -> Result<serde_json::Value> {
    let template = &offering.manifest.connection_template;
    render_template(template, &offering)
}

// Manifest-driven health check
fn check_health(offering: &Offering) -> Result<bool> {
    let health_config = &offering.manifest.health_check;
    execute_health_check(health_config, &offering.location)
}
```

---

## Appendix: Example Test Manifests

**Use these generic manifests in tests** - NEVER use real service names (mongodb, postgres, etc.)

### Minimal Adopted Test Manifest

```yaml
# tests/manifests/test-service-a.yaml
name: test-service-a
modes: ['adopted']
detection:
  - method: command
    command: "echo 'version 1.0.0'"
```

### Minimal Managed Test Manifest

```yaml
# tests/manifests/test-container-b.yaml
name: test-container-b
modes: ['managed']
container:
  image: "alpine:latest"
  ports:
    - "9999:9999"
```

### Advanced Test Manifest (All Features)

```yaml
# tests/manifests/test-service-advanced.yaml
name: test-service-advanced
modes: ['adopted', 'managed']
version: "1.0.0"
auto_adopt: true

detection:
  - name: native
    priority: 1
    method: command
    command: "test-service --version"
    validation:
      type: regex
      pattern: "version (\\d+\\.\\d+\\.\\d+)"
      capture_group: 1
    stability:
      min_consecutive_successes: 2
      min_consecutive_failures: 3
    service:
      protocol: http
      port: 8888
      health_check:
        endpoint: "/health"
        interval_seconds: 10
    control:
      level: full
      start_command: "test-service start"
      stop_command: "test-service stop"

container:
  image: "test-image:latest"
  ports:
    - "8888:8888"
  volumes:
    - type: named
      name: test-data
      mount: /data
  health_check:
    test: "curl -f http://localhost:8888/health"
    interval_seconds: 30

connection_template:
  type: templated_json
  template: |
    {
      "endpoint": "http://{{host}}:{{port}}",
      "version": "{{version}}"
    }
  variables:
    host: { source: location, key: host }
    port: { source: location, key: port }
    version: { source: config, key: version, default: "unknown" }
```

### Test Strategy

**Tier 1 Tests**: Start with minimal manifests
- Validate minimal adopted manifest loads and works
- Validate minimal managed manifest loads and works
- **Verify NO optional fields are required**

**Tier 2 Tests**: Add optional features incrementally
- Test health checks (optional)
- Test control configurations (optional)
- Test connection templates (optional)

**Tier 3 Tests**: Advanced features
- Test stability thresholds (optional)
- Test graduated health checks (optional)
- Test resource limits (optional)

**Anti-Pattern Tests**: Ensure these FAIL correctly
- ❌ Manifest with hardcoded service name in code
- ❌ Required field marked as optional
- ❌ Optional field that breaks when omitted

---

**End of Implementation Plan**

# COMPAT-0001: Offering Compatibility Rules System

**Status:** Accepted  
**Date:** 2026-01-16  
**Context:** Real-world deployment revealed MongoDB 5+ requires AVX CPU extensions that J4105 Celeron processors lack, causing restart loops

---

## Problem

Container offerings may be incompatible with specific stone hardware:
- MongoDB 5+ requires AVX CPU extensions (J4105 lacks this)
- Some workloads require minimum RAM/disk
- Architecture-specific compatibility (x86_64, ARM, etc.)
- Kernel version requirements

Without compatibility checking:
- ❌ Containers restart indefinitely
- ❌ User wastes time debugging
- ❌ No visibility into why service fails
- ❌ Manual intervention required

---

## Decision

Implement **three-tier compatibility detection system** with template-driven rules:

### Tier 1: Hardware Whitelist (Fastest - 0ms)
Pre-install check against known incompatible hardware models.

### Tier 2: Feature Detection (Fast - <1ms)  
Pre-install check for required CPU features via `/proc/cpuinfo`.

### Tier 3: Log Pattern Detection (Slowest - Post-install)
Scan container logs for known incompatibility patterns, rollback if detected.

### Rule Evaluation
- Rules evaluated in order (first match wins)
- If rule has `fallback` → use fallback image
- If rule has no `fallback` → deny installation with reason
- If no rules match → proceed with default image

---

## Architecture

### File Structure
```
manifests/
├── data/
│   ├── mongodb.snippet.yaml          # Service config
│   └── mongodb.compatibility.yaml    # NEW: Compatibility rules
└── messaging/
    ├── redis.snippet.yaml
    └── redis.compatibility.yaml      # Optional (only if needed)
```

### Compatibility File Schema

```yaml
# mongodb.compatibility.yaml
version: "1"

# Pre-install compatibility rules (evaluated in order)
compatibility_rules:
  # Rule with fallback - auto-downgrade
  - name: "j4105-no-avx"
    condition:
      processor_models:
        - "Intel(R) Celeron(R) J4105"
        - "Intel(R) Celeron(R) J3455"
      # OR
      cpu_features_missing: ["avx"]
      # OR
      architectures: ["armv6l"]
    reason: "J4105 lacks AVX required by MongoDB 5+"
    fallback:
      image: "mongo:4.4"
      
  # Rule without fallback - deny installation
  - name: "insufficient-memory"
    condition:
      memory_mb_less_than: 512
    reason: "MongoDB requires at least 512MB RAM"
    suggestion: "Upgrade stone hardware"

# Post-install healthcheck (catches runtime issues)
post_install_healthcheck:
  enabled: true
  scan_log_lines: 100
  timeout_seconds: 30
  patterns:
    - pattern: "MongoDB 5\\.0\\+ requires a CPU with AVX support"
      reason: "CPU incompatibility detected at runtime"
      fallback:
        image: "mongo:4.4"
    - pattern: "WiredTiger error.*ENOSPC"
      reason: "Insufficient disk space"
      # No fallback = fail and prevent future installs
```

### Data Model

```rust
// zen-common
pub struct StoneCapabilities {
    pub cpu_model: String,
    pub cpu_features: Vec<String>,
    pub architecture: String,
    pub total_memory_mb: u64,
    // ... existing fields
}

pub struct CompatibilityRules {
    pub version: String,
    pub compatibility_rules: Vec<CompatibilityRule>,
    pub post_install_healthcheck: Option<PostInstallHealthcheck>,
}

pub struct CompatibilityRule {
    pub name: String,
    pub condition: RuleCondition,
    pub reason: String,
    pub suggestion: Option<String>,
    pub fallback: Option<FallbackConfig>,
}

pub struct RuleCondition {
    pub processor_models: Option<Vec<String>>,
    pub processor_patterns: Option<Vec<String>>,
    pub cpu_features_missing: Option<Vec<String>>,
    pub architectures: Option<Vec<String>>,
    pub memory_mb_less_than: Option<u64>,
}

pub struct FallbackConfig {
    pub image: String,
}
```

---

## Implementation Flow

### Pre-Install Check (offer_service)

```
1. Load service template + compatibility rules
2. Get stone capabilities from metrics
3. For each rule in compatibility_rules:
   a. Evaluate condition against capabilities
   b. If match found:
      - Has fallback? Emit warning, override image, proceed
      - No fallback? Return 400 Bad Request with reason
4. If no match, proceed with default image
5. Queue async installation
```

### Post-Install Healthcheck (health_monitor_task)

```
1. For newly installed services (age < 5 min):
2. If has post_install_healthcheck:
   a. Fetch first N lines of container logs
   b. Match against incompatibility patterns
   c. If pattern found:
      - Has fallback? Remove container, re-install with fallback
      - No fallback? Mark as failed, emit event
```

---

## Benefits

1. **Fail Fast:** Most incompatibilities caught before pulling image
2. **Self-Healing:** Automatic fallback to compatible versions
3. **Observable:** All tier triggers emit events visible in `watch`
4. **Self-Learning:** Post-install detection feeds processor whitelist
5. **Declarative:** Compatibility rules co-located with service templates
6. **Extensible:** Easy to add new condition types (kernel version, etc.)

---

## User Experience

### Compatible Hardware
```bash
$ garden-rake offer mongodb
(stone-noble-nebula: 192.168.1.107:3001)
⏳ Installation queued for 'mongodb'
   Job ID: 019bc7b8-3d09-75b3-b779-598eb2e6e7da

# Installs mongo:7 (default)
```

### Known Incompatible (Tier 1)
```bash
$ garden-rake offer mongodb
(stone-amber-terrace: 192.168.1.107:3001)
⚠️  Using mongo:4.4 (J4105 lacks AVX required by MongoDB 5+)
⏳ Installation queued for 'mongodb'
   Job ID: 019bc7b8-3d09-75b3-b779-598eb2e6e7da

# Auto-installs mongo:4.4
```

### Incompatible (No Fallback)
```bash
$ garden-rake offer mongodb
(stone-tiny: 192.168.1.107:3001)
✗ Incompatible hardware
   Reason: MongoDB requires at least 512MB RAM
   Suggestion: Upgrade stone hardware
```

### Runtime Detection (Tier 3)
```bash
$ garden-rake watch
[16:40:05] ℹ Installing mongodb...
[16:40:20] ⚠️ Incompatibility detected: CPU lacks AVX support
[16:40:21] ℹ Rolling back to mongo:4.4...
[16:40:35] ✓ Service mongodb started successfully (mongo:4.4)
```

---

## Alternatives Considered

### 1. Universal Pre-Check Script
Run pre-install script that checks all requirements.

**Rejected:** Not declarative, harder to maintain, no per-offering customization.

### 2. Post-Install Only
Let containers fail, detect via logs, rollback.

**Rejected:** Wastes bandwidth pulling wrong images, container churn, slower.

### 3. Hardcoded CPU Feature Map
Build CPU model → features mapping in code.

**Rejected:** Brittle, incomplete, requires code changes to add models.

### 4. User-Specified Versions
User manually specifies version: `offer mongodb:4.4`.

**Rejected:** Requires user knowledge, error-prone, defeats zero-config goal.

---

## Migration Path

1. **Week 1:** CPU detection + schema + mongodb.compatibility.yaml
2. **Week 2:** Pre-install checks (Tier 1 + 2)
3. **Week 3:** Post-install healthcheck (Tier 3)
4. **Future:** Extend to other offerings as incompatibilities discovered

---

## Testing Strategy

1. **Tier 1:** Mock StoneCapabilities with J4105, verify fallback used
2. **Tier 2:** Mock cpu_features without AVX, verify fallback used
3. **Tier 3:** Create test container that logs failure pattern, verify rollback
4. **Deny:** Mock condition with no fallback, verify 400 response
5. **Integration:** Deploy to real J4105 stone, verify mongo:4.4 installs

---

## References

- MongoDB AVX requirement: https://jira.mongodb.org/browse/SERVER-54407
- Real-world failure: stone-noble-nebula restart loop (2026-01-16)
- Log streaming feature: Enabled debugging of this issue

---

## Decision Makers

- Infrastructure Team: Approved three-tier approach
- Dev Team: Approved declarative YAML schema
- Ops Team: Confirmed J4105 processor model detection

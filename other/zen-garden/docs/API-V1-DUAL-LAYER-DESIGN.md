# Zen Garden API v1: Dual-Layer Architecture

**Status:** Design Document  
**Date:** 2026-01-17  
**Purpose:** Complete API structure with progressive disclosure through dual layers

---

## Executive Summary

Zen Garden provides **two API layers** for the same underlying resources:

1. **Offerings API** — Human-friendly, safe, simplified (90% of users)
2. **Services API** — Technical, detailed, full control (10% power users)

Both layers access the same data but present different views optimized for different audiences.

---

## Complete API v1 Structure

### Offerings API (Human Layer)

**Target:** Beginners, scripters, CI/CD, simple automation  
**Philosophy:** Hide Docker complexity, provide safety rails, optimize for common case

#### Catalog Operations

```http
GET  /api/v1/offerings                     # List all offerings (available + installed)
GET  /api/v1/offerings?state=available     # Filter: available to install
GET  /api/v1/offerings?state=installed     # Filter: planted offerings
GET  /api/v1/offerings/{name}              # Offering details + compatibility
GET  /api/v1/offerings/{name}/manifest     # Raw YAML definition
```

**GET /api/v1/offerings response:**
```json
{
  "offerings": [
    {
      "name": "mongodb",
      "state": "available",
      "category": "databases",
      "description": "MongoDB NoSQL database",
      "tags": ["nosql", "document-store"],
      "compatibility": {"decision": "pass", "reason": null}
    },
    {
      "name": "postgres",
      "state": "installed",
      "category": "databases",
      "health": "healthy",
      "uptime": "2 days"
    }
  ]
}
```

#### Lifecycle Operations

```http
POST   /api/v1/offerings                   # Plant offering (simplified install)
DELETE /api/v1/offerings/{name}            # Take away offering (uninstall)
POST   /api/v1/offerings:heal              # Heal garden (discover orphans)
POST   /api/v1/offerings:refresh           # Refresh catalog from disk
```

**POST /api/v1/offerings (plant):**
```json
// Request
{
  "name": "mongodb",
  "config": {
    "environment": {"MONGO_INITDB_ROOT_USERNAME": "admin"}
  }
}

// Response (202 Accepted)
{
  "name": "mongodb",
  "state": "installing",
  "job_id": "job_abc123"
}
```

---

### Services API (Technical Layer)

**Target:** Operators, troubleshooting, DevOps, advanced automation  
**Philosophy:** Expose container reality, provide full control, enable debugging

#### Manifest Operations

```http
GET /api/v1/services/manifests             # List all service manifests
GET /api/v1/services/{name}/manifest       # Get specific manifest YAML
```

**GET /api/v1/services/manifests response:**
```json
{
  "manifests": [
    {
      "name": "mongodb",
      "category": "databases",
      "image": "mongo:7.0",
      "ports": [{"container": 27017, "protocol": "tcp"}],
      "volumes": [{"mount": "/data/db"}],
      "environment_defaults": {
        "MONGO_INITDB_ROOT_USERNAME": "admin",
        "MONGO_INITDB_ROOT_PASSWORD": "${GENERATED}"
      },
      "compatibility_rules": {
        "architectures": ["amd64", "arm64"],
        "fallback_image": "mongo:7.0-alpine"
      }
    }
  ]
}
```

#### Runtime Operations

```http
GET    /api/v1/services                    # List services (container-level details)
GET    /api/v1/services/{name}             # Service details (full technical view)
GET    /api/v1/services/{name}/logs        # Stream logs (SSE)
POST   /api/v1/services                    # Install (full Docker control)
POST   /api/v1/services/{name}:restart     # Restart service
POST   /api/v1/services/{name}:cordon      # Mark unavailable
DELETE /api/v1/services/{name}             # Uninstall
POST   /api/v1/services:reconcile          # Reconcile inventory
POST   /api/v1/services:refresh            # Refresh manifests
```

**GET /api/v1/services/{name} response (detailed):**
```json
{
  "name": "mongodb",
  "container_id": "a1b2c3d4e5f6",
  "state": "running",
  "image": "mongo:7.0",
  "image_id": "sha256:abc123...",
  "created_at": "2026-01-15T10:30:00Z",
  "ports": [
    {"host": 27017, "container": 27017, "protocol": "tcp", "host_ip": "0.0.0.0"}
  ],
  "volumes": [
    {"host": "/var/lib/zen-garden/mongodb", "container": "/data/db", "mode": "rw", "size_mb": 2048}
  ],
  "environment": {"MONGO_INITDB_ROOT_USERNAME": "admin"},
  "networks": [{"name": "zen-garden-default", "ip": "172.18.0.5"}],
  "health_check": {
    "status": "healthy",
    "last_check": "2026-01-17T10:30:00Z",
    "consecutive_failures": 0
  },
  "resource_usage": {
    "cpu_percent": 5.2,
    "memory_mb": 256,
    "memory_limit_mb": 1024
  },
  "restart_policy": "unless-stopped",
  "uptime_seconds": 172800
}
```

**POST /api/v1/services (install with full control):**
```json
{
  "name": "mongodb",
  "image": "mongo:7.0",
  "ports": [{"host": 27017, "container": 27017}],
  "volumes": [{"host": "/custom/path", "container": "/data/db"}],
  "environment": {"MONGO_INITDB_ROOT_USERNAME": "admin"},
  "restart_policy": "unless-stopped",
  "memory_limit_mb": 1024,
  "health_check": {
    "command": ["mongo", "--eval", "db.adminCommand('ping')"],
    "interval_seconds": 30
  }
}
```

---

### Stone Operations (Universal)

```http
GET  /health                               # Health check (Prometheus standard)
GET  /capabilities                         # Stone hardware capabilities
GET  /metrics                              # Prometheus metrics
POST /api/v1/stone:upgrade                 # Upgrade stone software
POST /api/v1/stone:shutdown                # Shutdown Moss daemon
```

---

### Events & Jobs (Universal)

```http
GET /api/v1/events                         # Stream events (SSE)
GET /api/v1/jobs                           # List recent jobs
GET /api/v1/jobs/{id}                      # Job status/result
```

---

## Key Differences: Offerings vs Services

| Aspect | Offerings API | Services API |
|--------|---------------|--------------|
| **State names** | `available`, `installing`, `installed` | `creating`, `running`, `exited` |
| **Health** | `healthy`, `degraded` | Detailed health check results |
| **Configuration** | Simplified (no raw flags) | Full Docker configuration |
| **Responses** | Human-readable summaries | Container IDs, technical details |
| **Operations** | Plant, take away, heal | Install, uninstall, reconcile |
| **Errors** | Friendly messages | Technical error codes |

---

## API Selection Guide

**Use Offerings API when:**
- Building quick start tutorials
- Writing simple automation scripts
- Creating beginner-friendly UIs
- You don't need container internals

**Use Services API when:**
- Debugging production issues
- Building operator dashboards
- Need port bindings, volume paths
- Implementing advanced orchestration
- Writing monitoring integrations

---

## CLI Mapping

**Offerings API (Zen commands):**
```bash
garden-rake explore              # GET /api/v1/offerings?state=available
garden-rake offer mongodb        # POST /api/v1/offerings
garden-rake observe              # GET /api/v1/offerings?state=installed
```

**Services API (Technical operations):**
```bash
garden-rake service logs mongodb     # GET /api/v1/services/mongodb/logs
garden-rake service inspect mongodb  # GET /api/v1/services/mongodb
garden-rake service reconcile        # POST /api/v1/services:reconcile
```

---

## Implementation Notes

### Shared Backend

Both APIs access the same:
- Registry state (in-memory + persistent)
- Docker API calls
- Template/manifest files
- Compatibility engine

**Difference is presentation layer only.**

### Response Transformers

```rust
// Offerings layer - simplified view
pub fn to_offering_view(service: &Service) -> OfferingView {
    OfferingView {
        name: service.name.clone(),
        state: simplify_state(&service.state),
        health: simplify_health(&service.health),
        uptime: humanize_duration(service.uptime_seconds),
        // Container internals hidden
    }
}

// Services layer - full technical view
pub fn to_service_view(service: &Service, container_info: &ContainerInfo) -> ServiceView {
    ServiceView {
        name: service.name.clone(),
        container_id: container_info.id.clone(),
        state: container_info.state.clone(),
        ports: container_info.ports.clone(),
        volumes: container_info.mounts.clone(),
        resource_usage: get_resource_usage(container_info),
        // All details exposed
    }
}
```

### Error Handling

**Offerings API:**
```json
{
  "error": "OFFERING_NOT_FOUND",
  "message": "MongoDB is not available in the catalog",
  "suggestion": "Run 'garden-rake explore database' to see available options"
}
```

**Services API:**
```json
{
  "error": "CONTAINER_NOT_FOUND",
  "message": "No container found with name 'mongodb'",
  "details": {
    "searched_names": ["zen-offering-mongodb", "mongodb"],
    "available_containers": ["zen-offering-postgres"]
  }
}
```

---

## Documentation Strategy

### Quick Start (Offerings API Only)
```markdown
## Getting Started

1. Explore available offerings:
   ```bash
   garden-rake explore
   ```

2. Plant MongoDB:
   ```bash
   garden-rake offer mongodb
   ```

3. Check status:
   ```bash
   garden-rake observe
   ```
```

### Troubleshooting Guide (Introduces Services API)
```markdown
## Debugging Services

If an offering isn't working as expected, use the services API for detailed inspection:

```bash
# Get container-level details
curl http://localhost:7185/api/v1/services/mongodb

# Stream logs
curl http://localhost:7185/api/v1/services/mongodb/logs
```

This exposes:
- Container ID and image SHA
- Port bindings and volume mounts
- Resource usage and limits
- Health check results
```

---

## Migration Path

**Phase 1:** Implement both APIs  
**Phase 2:** Document Offerings API first (quick start)  
**Phase 3:** Document Services API (operations manual)  
**Phase 4:** Build CLI commands for both layers

**No deprecation needed** — both APIs coexist permanently, serving different needs.

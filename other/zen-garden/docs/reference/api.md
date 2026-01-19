---
audience: [api-client, operator, maintainer]
doc_type: reference
status: current
last_verified: 2026-01-19
canonical: true
note: "Complete HTTP API documentation for Moss daemon v1. Phase 3 complete (scaffolding), Phase 3b pending (cryptographic implementation)."
related:
  - TECHNICAL-SPEC.md
  - REFERENCE.md
  - decisions/API-V1-DUAL-LAYER-DESIGN.md
---

# Zen Garden API Reference

**Version:** v1  
**Base Path:** `/api/v1`  
**Protocol:** HTTP/REST with JSON payloads  
**Date:** January 2026

## Overview

The Zen Garden API v1 provides **two layers** for managing containerized services across distributed stones:

1. **Offerings API** (`/api/v1/offerings`) — Human-friendly, simplified responses for beginners and automation (90% use case)
2. **Services API** (`/api/v1/services`) — Technical, detailed container-level operations for operators (10% use case)

Both layers access the same underlying resources but provide different views optimized for their audiences. See [API-V1-DUAL-LAYER-DESIGN.md](API-V1-DUAL-LAYER-DESIGN.md) for complete architecture details.

### API Selection Guide

**Use Offerings API when:**
- Building quick start tutorials or simple automation
- Creating beginner-friendly UIs
- You don't need container internals (IDs, volume paths, etc.)

**Use Services API when:**
- Debugging production issues or building operator dashboards
- Need technical details (port bindings, resource usage, health checks)
- Implementing advanced orchestration or monitoring

### Authentication

**Current:** None (Phase 1-3)  
**Future:** mTLS with pond security certificates (Phase 3b)

### Response Format

All successful responses follow this structure:

```json
{
  "status": "success",
  "message": "Human-readable status message",
  "data": { /* endpoint-specific payload */ },
  "suggestions": ["Optional guidance messages"]
}
```

Error responses use standard HTTP status codes with:

```json
{
  "error": {
    "code": "ERROR_CODE",
    "message": "Human-readable error description",
    "details": { /* optional context */ }
  }
}
```

### Quiet Mode

All endpoints respect the `X-Quiet: true` header, which suppresses suggestion generation. Useful for scripting and automation.

---

## Endpoint Index

### Offerings API (Human Layer)

**Catalog Operations:**
- `GET /api/v1/offerings` - List offerings (available + installed)
- `GET /api/v1/offerings?state=available` - Filter available offerings
- `GET /api/v1/offerings?state=installed` - Filter planted offerings
- `GET /api/v1/offerings/:name` - Get offering details
- `GET /api/v1/offerings/:name/manifest` - Get offering YAML template

**Lifecycle Operations:**
- `POST /api/v1/offerings` - Plant offering (simplified install)
- `DELETE /api/v1/offerings/:name` - Take away offering (uninstall)
- `POST /api/v1/offerings:heal` - Heal garden (discover orphans)
- `POST /api/v1/offerings:refresh` - Refresh catalog from disk

### Services API (Technical Layer)

**Manifest Operations:**
- `GET /api/v1/services/manifests` - List all service manifests
- `GET /api/v1/services/:name/manifest` - Get specific manifest YAML

**Runtime Operations:**
- `GET /api/v1/services` - List services (container-level details)
- `GET /api/v1/services/:name` - Get service details (full technical view)
- `GET /api/v1/services/:name/logs` - Stream logs (SSE)
- `POST /api/v1/services` - Install (full Docker control)
- `POST /api/v1/services/:name:restart` - Restart service
- `POST /api/v1/services/:name:cordon` - Mark unavailable
- `DELETE /api/v1/services/:name` - Uninstall service
- `POST /api/v1/services:reconcile` - Reconcile inventory
- `POST /api/v1/services:refresh` - Refresh manifests

### Stone Operations
- `GET /health` - Health check with component status
- `GET /capabilities` - Hardware and software capabilities
- `GET /metrics` - Prometheus metrics exposition
- `POST /api/v1/stone:upgrade` - Upgrade stone software
- `POST /api/v1/stone:shutdown` - Shutdown Moss daemon

### Events & Jobs
- `GET /api/v1/events` - Stream all moss operation events (SSE)
- `GET /api/v1/jobs` - List background jobs
- `GET /api/v1/jobs/:job_id` - Get job status and result

### Administrative
- `POST /admin/shutdown` - Graceful daemon shutdown

**Total:** 32 endpoints

---

## Migration from v0 to v1

### Breaking Changes

| Old Endpoint (v0) | New Endpoint (v1) | Notes |
|-------------------|-------------------|-------|
| `GET /api/offerings` | `GET /api/v1/offerings` | Versioned path |
| `GET /api/offerings/:name` | `GET /api/v1/offerings/:name` | Versioned path |
| `POST /api/offerings/refresh` | `POST /api/v1/offerings:refresh` | Custom action format |
| `GET /api/templates` | `GET /api/v1/services/manifests` | New endpoint + key name |
| `GET /api/templates/:name` | `GET /api/v1/services/:name/manifest` | New endpoint structure |
| `GET /api/services` | `GET /api/v1/services` | Versioned path |
| `POST /api/services` | `POST /api/v1/services` | Versioned path |
| `DELETE /api/services/:name` | `DELETE /api/v1/services/:name` | Versioned path |

### Response Format Changes

**Manifests list changed key:**
```json
// Old (v0)
{"templates": [...]}

// New (v1)
{"manifests": [...]}
```

**Offerings now support state filtering:**
```http
GET /api/v1/offerings?state=available
GET /api/v1/offerings?state=installed
```

### Custom Actions

v1 uses single-colon format for custom actions (aligns with Kubernetes/GCP standards):

```http
POST /api/v1/offerings:heal
POST /api/v1/offerings:refresh
POST /api/v1/services:reconcile
POST /api/v1/services:refresh
POST /api/v1/stone:upgrade
POST /api/v1/stone:shutdown
```

### Backwards Compatibility

- v0 endpoints are **deprecated** and will be removed in future releases
- Update clients to use v1 endpoints immediately
- Both APIs currently share the same backend (no data migration needed)

---

## Services API

Manage containerized services (offerings) on a stone.

### POST /api/v1/services

**Create and start a service from an offering template.**

**Request:**
```json
{
  "offering": "mongodb",
  "name": "my-mongo",  // optional, defaults to offering name
  "ports": [],         // optional override
  "environment": {}    // optional override
}
```

**Response (201 Created):**
```json
{
  "status": "success",
  "message": "Service 'my-mongo' created from mongodb",
  "data": {
    "service_name": "my-mongo",
    "offering": "mongodb",
    "status": "running",
    "container_id": "abc123...",
    "ports": [
      {"container": 27017, "host": 27017, "protocol": "tcp"}
    ],
    "environment": {
      "MONGO_INITDB_ROOT_USERNAME": "admin",
      "MONGO_INITDB_ROOT_PASSWORD": "generated-password"
    }
  },
  "suggestions": [
    "Service is now accessible at localhost:27017",
    "Use 'garden-rake observe' to monitor service health"
  ]
}
```

**Errors:**
- `400` - Invalid offering name or missing template
- `409` - Service name already exists
- `500` - Docker daemon error or container start failure

**Edge Cases:**
- Port conflicts: Returns error with available port suggestions
- Image pull failures: Falls back to architecture-compatible image if available
- Resource constraints: Returns error if disk/memory thresholds exceeded
- Name collisions: Validates against existing zen-offering-* containers

**CLI Examples:**
```bash
# Zen syntax
garden-rake offer mongodb

# Normative syntax
garden-rake services create mongodb

# With quiet mode
garden-rake offer redis quietly
garden-rake services create redis --quiet
```

---

### GET /api/v1/services

**List all services on the stone.**

**Response (200 OK):**
```json
{
  "status": "success",
  "data": {
    "services": [
      {
        "name": "mongodb",
        "offering": "mongodb",
        "status": "running",
        "container_id": "abc123...",
        "uptime_seconds": 3600,
        "ports": [{"container": 27017, "host": 27017}]
      }
    ]
  }
}
```

**Status Values:**
- `running` - Container is active
- `stopped` - Container exists but not running (rest mode)
- `restarting` - Container in restart loop
- `error` - Container in error state

**CLI Examples:**
```bash
# Zen syntax
garden-rake observe

# Normative syntax
garden-rake list
```

---

### GET /api/v1/services/:service

**Get detailed information about a specific service.**

**Response (200 OK):**
```json
{
  "status": "success",
  "data": {
    "name": "mongodb",
    "offering": "mongodb",
    "status": "running",
    "container_id": "abc123...",
    "image": "mongo:7.0",
    "created": "2026-01-17T10:30:00Z",
    "started": "2026-01-17T10:30:05Z",
    "ports": [
      {"container": 27017, "host": 27017, "protocol": "tcp"}
    ],
    "environment": {
      "MONGO_INITDB_ROOT_USERNAME": "admin"
    },
    "volumes": [
      {"name": "zen-vol-mongodb-data", "mount": "/data/db"}
    ]
  }
}
```

**Errors:**
- `404` - Service not found

**CLI Examples:**
```bash
# Zen syntax
garden-rake touch mongodb

# Normative syntax
garden-rake status mongodb
```

---

### POST /api/v1/services/:service/rest

**Stop a running service (rest mode).**

Stops the container but preserves volumes and configuration for quick restart.

**Response (200 OK):**
```json
{
  "status": "success",
  "message": "Service 'mongodb' stopped",
  "data": {
    "service_name": "mongodb",
    "status": "stopped"
  },
  "suggestions": [
    "Service volumes preserved",
    "Use 'garden-rake wake mongodb' to restart"
  ]
}
```

**Errors:**
- `404` - Service not found
- `409` - Service already stopped

**Edge Cases:**
- Graceful shutdown timeout: 10 seconds before force-stop
- Active connections: Logs warning but proceeds with shutdown
- Volume cleanup: Volumes preserved unless service is removed

**CLI Examples:**
```bash
# Zen syntax
garden-rake rest mongodb

# Normative syntax
garden-rake services stop mongodb
```

---

### POST /api/v1/services/:service/wake

**Start a stopped service (wake from rest).**

**Response (200 OK):**
```json
{
  "status": "success",
  "message": "Service 'mongodb' started",
  "data": {
    "service_name": "mongodb",
    "status": "running"
  },
  "suggestions": [
    "Service restored from rest mode",
    "All volumes and configuration preserved"
  ]
}
```

**Errors:**
- `404` - Service not found
- `409` - Service already running
- `500` - Container start failure

**CLI Examples:**
```bash
# Zen syntax
garden-rake wake mongodb

# Normative syntax
garden-rake services start mongodb
```

---

### POST /api/v1/services/:service/nourish

**Upgrade service to latest image version.**

Pulls latest image, stops container, recreates with same config, preserves volumes.

**Response (200 OK):**
```json
{
  "status": "success",
  "message": "Service 'mongodb' upgraded",
  "data": {
    "service_name": "mongodb",
    "old_image": "mongo:7.0",
    "new_image": "mongo:7.0.5",
    "status": "running"
  },
  "suggestions": [
    "Service upgraded with zero data loss",
    "Verify application compatibility with new version"
  ]
}
```

**Errors:**
- `404` - Service not found
- `500` - Image pull failure or recreation error

**Edge Cases:**
- Image unchanged: Returns success with note "already up to date"
- Downtime: Brief interruption during container recreation
- Volume compatibility: Upgrade may fail if new version incompatible with existing data
- Rollback: Manual process - no automatic rollback on failure

**CLI Examples:**
```bash
# Zen syntax
garden-rake nourish mongodb
garden-rake nourish --all  # upgrade all services

# Normative syntax
garden-rake services update mongodb
garden-rake upgrade --all
```

---

### DELETE /api/v1/services/:service

**Remove service and its containers (volumes preserved).**

**Response (200 OK):**
```json
{
  "status": "success",
  "message": "Service 'mongodb' removed",
  "data": {
    "service_name": "mongodb",
    "volumes_preserved": true
  },
  "suggestions": [
    "Service removed but volumes preserved",
    "Volumes: zen-vol-mongodb-data",
    "Use 'docker volume rm' to remove volumes if needed"
  ]
}
```

**Errors:**
- `404` - Service not found
- `500` - Container removal failure

**Edge Cases:**
- Volume preservation: Volumes kept by design for data safety
- Registry cleanup: Service removed from Moss registry immediately
- Container force-remove: Uses force flag if graceful stop fails
- Orphaned volumes: Listed in suggestions for manual cleanup

**CLI Examples:**
```bash
# Zen syntax
garden-rake release mongodb

# Normative syntax
garden-rake services delete mongodb
garden-rake remove mongodb
```

---

## Garden API

Observe multi-stone topology and service distribution.

### GET /api/v1/garden

**Get garden overview with all discovered stones.**

**Response (200 OK):**
```json
{
  "status": "success",
  "data": {
    "stones": [
      {
        "name": "stone-01",
        "endpoint": "http://192.168.1.100:7185",
        "status": "healthy",
        "services": [
          {
            "name": "mongodb",
            "offering": "mongodb",
            "status": "running"
          }
        ],
        "capabilities": {
          "arch": "x86_64",
          "platform": "linux",
          "docker_version": "24.0.7"
        }
      }
    ]
  }
}
```

**CLI Examples:**
```bash
garden-rake observe
garden-rake observe --offering mongodb  # filter by offering
```

---

### GET /api/v1/garden/stones/:stone_name

**Get detailed information about a specific stone.**

**Response (200 OK):**
```json
{
  "status": "success",
  "data": {
    "name": "stone-01",
    "endpoint": "http://192.168.1.100:7185",
    "status": "healthy",
    "services": [...],
    "capabilities": {
      "arch": "x86_64",
      "platform": "linux",
      "cpu_cores": 8,
      "memory_gb": 16.0,
      "docker_version": "24.0.7"
    },
    "resources": {
      "disk": {
        "total_gb": 500.0,
        "available_gb": 350.0,
        "used_percent": 30.0
      },
      "memory": {
        "total_gb": 16.0,
        "available_gb": 8.0,
        "used_percent": 50.0
      }
    }
  }
}
```

**Errors:**
- `404` - Stone not found in garden topology

---

### GET /api/v1/stone

**Get local stone information (self-introspection).**

Returns the same data as `/garden/stones/:stone_name` but always for the local stone where the request is made.

**Response (200 OK):**
```json
{
  "status": "success",
  "data": {
    "name": "stone-01",
    "endpoint": "http://localhost:7185",
    "services": [...],
    "capabilities": {...},
    "resources": {...}
  }
}
```

---

## Pond Security API

**Status:** Phase 3 - Scaffolding complete, implementation pending (Phase 3b)

All pond endpoints currently return `501 Not Implemented` with informative messages. These endpoints prepare the structure for future multi-stone security with mTLS certificates.

### POST /api/v1/pond/init

**Initialize pond security (place keystone).**

**Future Functionality:**
- Generates cornerstone identity (CA certificate)
- Creates encrypted keystone file (private key)
- Initializes pond trust network
- Passphrase-protected certificate encryption

**Request:**
```json
{
  "passphrase": "secure-passphrase"
}
```

**Current Response (501 Not Implemented):**
```json
{
  "error": {
    "code": "POND_NOT_IMPLEMENTED",
    "message": "Pond security implementation pending (Phase 3b - cryptographic implementation)",
    "details": {
      "phase": "3b",
      "feature": "pond-security"
    }
  }
}
```

**CLI Examples:**
```bash
# Zen syntax
garden-rake place keystone --passphrase "my-secure-pass"

# Normative syntax
garden-rake pond init --passphrase "my-secure-pass"
```

---

### GET /api/v1/pond/status

**Get pond security status and membership.**

**Current Response (200 OK):**
```json
{
  "status": "success",
  "data": {
    "active": false,
    "cornerstone": null,
    "stones": [],
    "tier": "garden-pond",
    "note": "Pond security not initialized. Run 'garden-rake place keystone' to secure your garden."
  }
}
```

**Future Response (when active):**
```json
{
  "status": "success",
  "data": {
    "active": true,
    "cornerstone": "stone-01",
    "stones": [
      {
        "name": "stone-01",
        "is_cornerstone": true,
        "certificate_expires": "2027-01-17T10:30:00Z",
        "joined_at": "2026-01-17T10:30:00Z"
      },
      {
        "name": "stone-02",
        "is_cornerstone": false,
        "certificate_expires": "2027-01-17T11:00:00Z",
        "joined_at": "2026-01-17T11:00:00Z"
      }
    ],
    "tier": "garden-pond"
  }
}
```

**CLI Examples:**
```bash
# Both syntax forms
garden-rake pond status
```

---

### POST /api/v1/pond/invite

**Generate time-limited TOTP invitation code.**

**Future Functionality:**
- Generates 6-digit TOTP code
- 90-second validity window
- One-time use for stone joining
- Includes inviter stone identity

**Current Response (501 Not Implemented)**

**Future Response:**
```json
{
  "status": "success",
  "data": {
    "code": "123456",
    "expires_at": "2026-01-17T10:31:30Z",
    "ttl_seconds": 90,
    "inviter_stone": "stone-01"
  },
  "suggestions": [
    "Share this code with the stone you want to invite",
    "Code expires in 90 seconds",
    "Use 'garden-rake place stone --code 123456' on the new stone"
  ]
}
```

**CLI Examples:**
```bash
# Zen syntax
garden-rake invite

# Normative syntax
garden-rake pond invite
```

---

### POST /api/v1/pond/join

**Join pond using invitation code.**

**Future Functionality:**
- Validates TOTP code
- Exchanges certificates with cornerstone
- Establishes mTLS trust
- Registers stone in pond membership

**Request:**
```json
{
  "code": "123456"
}
```

**Current Response (501 Not Implemented)**

**Future Response:**
```json
{
  "status": "success",
  "data": {
    "stone_name": "stone-02",
    "cornerstone": "stone-01",
    "certificate_expires": "2027-01-17T10:30:00Z",
    "status": "joined"
  },
  "suggestions": [
    "Successfully joined pond",
    "All cross-stone communication now uses mTLS",
    "Use 'garden-rake observe' to see all stones in your garden"
  ]
}
```

**Errors (future):**
- `400` - Invalid or expired code
- `409` - Stone already in a pond
- `500` - Certificate exchange failure

**CLI Examples:**
```bash
# Zen syntax
garden-rake place stone --code 123456

# Normative syntax
garden-rake pond join 123456
```

---

### DELETE /api/v1/pond

**Remove pond from this stone.**

**Future Functionality:**
- Removes local certificates
- Deletes keystone file
- Unregisters from pond topology
- Reverts to non-authenticated mode

**Current Response (501 Not Implemented)**

**CLI Examples:**
```bash
# Zen syntax
garden-rake lift keystone

# Normative syntax
garden-rake pond remove
```

---

### DELETE /api/v1/pond/stones/:stone_name

**Remove a stone from pond (untrust).**

**Future Functionality:**
- Revokes stone's certificate
- Removes from pond membership
- Broadcasts revocation to all stones
- Cornerstone-only operation

**Current Response (501 Not Implemented)**

**Errors (future):**
- `404` - Stone not in pond
- `403` - Caller is not cornerstone
- `409` - Cannot untrust cornerstone itself

**CLI Examples:**
```bash
# Zen syntax
garden-rake lift stone stone-02

# Normative syntax
garden-rake pond untrust stone-02
```

---

## Health & Capabilities

### GET /health

**Health check endpoint for load balancers and monitoring.**

**Response (200 OK or 503):**
```json
{
  "status": "healthy",
  "timestamp": "2026-01-17T10:30:00Z",
  "components": {
    "docker": {
      "status": "healthy",
      "details": {
        "available": true
      }
    },
    "disk": {
      "status": "healthy",
      "details": {
        "free_gb": "350.0",
        "total_gb": "500.0",
        "usage_percent": "30.00"
      }
    },
    "memory": {
      "status": "healthy",
      "details": {
        "available_gb": "8.0",
        "total_gb": "16.0",
        "usage_percent": "50.00"
      }
    }
  },
  "docker_available": true,
  "disk_space_ok": true,
  "memory_ok": true,
  "uptime_seconds": 86400
}
```

**Health Status Values:**
- `healthy` - All components operational, <80% resource usage
- `degraded` - Functional but 80-95% resource usage
- `unhealthy` - Critical issues, >95% resources or component failure

**HTTP Status Codes:**
- `200` - Healthy or degraded
- `503` - Unhealthy (should not receive traffic)

---

### GET /capabilities

**Get stone hardware and software capabilities.**

**Response (200 OK):**
```json
{
  "stone_name": "stone-01",
  "arch": "x86_64",
  "platform": "linux",
  "os": "Ubuntu 22.04.3 LTS",
  "kernel": "5.15.0-91-generic",
  "cpu_cores": 8,
  "memory_gb": 16.0,
  "docker_version": "24.0.7",
  "docker_api_version": "1.43",
  "offerings_count": 47
}
```

**Used For:**
- Compatibility decisions (architecture matching)
- Service placement recommendations
- Resource-aware scheduling
- Garden topology display

---

### GET /metrics

**Prometheus-compatible metrics endpoint.**

Returns stone resource metrics in Prometheus exposition format for monitoring and alerting.

**Response (200 OK - text/plain):**
```
# HELP zen_stone_disk_free_bytes Free disk space in bytes
# TYPE zen_stone_disk_free_bytes gauge
zen_stone_disk_free_bytes{stone="stone-01"} 376000000000

# HELP zen_stone_memory_available_bytes Available memory in bytes
# TYPE zen_stone_memory_available_bytes gauge
zen_stone_memory_available_bytes{stone="stone-01"} 8589934592

# HELP zen_stone_services_total Total number of services
# TYPE zen_stone_services_total gauge
zen_stone_services_total{stone="stone-01"} 3
```

**Metrics Exposed:**
- `zen_stone_disk_free_bytes` - Available disk space
- `zen_stone_disk_total_bytes` - Total disk space
- `zen_stone_memory_available_bytes` - Available RAM
- `zen_stone_memory_total_bytes` - Total RAM
- `zen_stone_services_total` - Service count
- `zen_stone_services_running` - Running services
- `zen_stone_uptime_seconds` - Moss daemon uptime

**Integration:**
- Prometheus scrape target
- Grafana dashboards
- AlertManager rules
- Custom monitoring tools

---

## Offerings Catalog

### GET /api/offerings

**Get validated offering templates with compatibility info.**

**Response (200 OK):**
```json
{
  "offerings": [
    {
      "name": "mongodb",
      "category": "databases",
      "description": "MongoDB NoSQL database",
      "tags": ["nosql", "database", "document-store"],
      "image": "mongo:7.0",
      "ports": [
        {"container": 27017, "host": 27017, "protocol": "tcp"}
      ],
      "environment": {
        "MONGO_INITDB_ROOT_USERNAME": "admin",
        "MONGO_INITDB_ROOT_PASSWORD": "${GENERATED}"
      },
      "volumes": [
        {"name": "${SERVICE}-data", "mount": "/data/db"}
      ],
      "compatibility": {
        "decision": "pass",
        "reason": null,
        "original_image": null,
        "fallback_image": null,
        "suggestion": null
      }
    },
    {
      "name": "postgres",
      "category": "databases",
      "description": "PostgreSQL relational database",
      "tags": ["sql", "database", "relational"],
      "image": "postgres:16",
      "compatibility": {
        "decision": "fallback",
        "reason": "Architecture mismatch: amd64 image on arm64 host",
        "original_image": "postgres:16",
        "fallback_image": "postgres:16-alpine",
        "suggestion": "Will use ARM-compatible Alpine image"
      }
    }
  ]
}
```

**Compatibility Decisions:**
- `pass` - Image compatible with stone architecture
- `fallback` - Architecture-compatible fallback available
- `block` - No compatible image available for this architecture

**CLI Examples:**
```bash
# Zen syntax
garden-rake explore

# Normative syntax
garden-rake offerings list
```

---

## Templates API

### GET /api/templates

**List available offering templates (raw YAML).**

**Response (200 OK):**
```json
{
  "templates": [
    {"name": "mongodb", "category": "databases"},
    {"name": "redis", "category": "caching"},
    {"name": "nginx", "category": "web-servers"}
  ]
}
```

**CLI Examples:**
```bash
garden-rake template list
```

---

### GET /api/templates/:name

**Get raw YAML content of a template.**

**Response (200 OK):**
```yaml
name: mongodb
category: databases
description: MongoDB NoSQL database
image: mongo:7.0
ports:
  - container: 27017
    host: 27017
environment:
  MONGO_INITDB_ROOT_USERNAME: admin
  MONGO_INITDB_ROOT_PASSWORD: ${GENERATED}
volumes:
  - name: ${SERVICE}-data
    mount: /data/db
```

**CLI Examples:**
```bash
garden-rake template show mongodb
```

---

## Offerings Catalog

Validated offering templates with architecture compatibility analysis.

### GET /api/offerings

**List validated offerings with compatibility decisions.**

Returns all offering templates analyzed for this stone's architecture with automatic fallback recommendations.

**Response (200 OK):**
```json
{
  "offerings": [
    {
      "name": "mongodb",
      "category": "databases",
      "description": "MongoDB NoSQL database",
      "tags": ["nosql", "database", "document-store"],
      "image": "mongo:7.0",
      "ports": [
        {"container": 27017, "host": 27017, "protocol": "tcp"}
      ],
      "environment": {
        "MONGO_INITDB_ROOT_USERNAME": "admin",
        "MONGO_INITDB_ROOT_PASSWORD": "${GENERATED}"
      },
      "volumes": [
        {"name": "${SERVICE}-data", "mount": "/data/db"}
      ],
      "compatibility": {
        "decision": "pass",
        "reason": null,
        "original_image": null,
        "fallback_image": null,
        "suggestion": null
      }
    },
    {
      "name": "postgres",
      "category": "databases",
      "description": "PostgreSQL relational database",
      "tags": ["sql", "database", "relational"],
      "image": "postgres:16",
      "compatibility": {
        "decision": "fallback",
        "reason": "Architecture mismatch: amd64 image on arm64 host",
        "original_image": "postgres:16",
        "fallback_image": "postgres:16-alpine",
        "suggestion": "Will use ARM-compatible Alpine image"
      }
    }
  ]
}
```

**Compatibility Decisions:**
- `pass` - Image compatible with stone architecture
- `fallback` - Architecture-compatible fallback available
- `block` - No compatible image available for this architecture

**CLI Examples:**
```bash
# Zen syntax
garden-rake explore

# Normative syntax
garden-rake offerings list
```

---

### GET /api/offerings/:name

**Get detailed offering info with compatibility analysis.**

Returns single offering template with full compatibility analysis for this specific stone.

**Response (200 OK):**
```json
{
  "name": "mongodb",
  "category": "databases",
  "description": "MongoDB NoSQL database",
  "tags": ["nosql", "database"],
  "image": "mongo:7.0",
  "ports": [...],
  "environment": {...},
  "volumes": [...],
  "compatibility": {
    "decision": "pass",
    "reason": null,
    "original_image": null,
    "fallback_image": null,
    "suggestion": null
  }
}
```

**Errors:**
- `404` - Offering template not found

**CLI Examples:**
```bash
garden-rake offer mongodb info
```

---

### POST /api/offerings/refresh

**Force rebuild of offerings index cache.**

**Note:** Per zen taxonomy, this should be `POST /api/offerings/_refresh` (underscore prefix for system actions)

Triggers immediate recompilation of all offering templates with compatibility decisions. Normally done automatically on template changes.

**Response (200 OK):**
```json
{
  "message": "Offerings index refreshed",
  "count": 47,
  "generated_at": "2026-01-17T10:30:00Z"
}
```

**Use Cases:**
- After manual template modifications
- Testing compatibility fallback logic
- Debugging template issues
- Development workflow

---

## Templates

Raw YAML offering templates without compatibility analysis.

### GET /api/templates

**List available offering templates.**

**Response (200 OK):**
```json
{
  "templates": [
    {"name": "mongodb", "category": "databases"},
    {"name": "redis", "category": "caching"},
    {"name": "nginx", "category": "web-servers"}
  ]
}
```

**CLI Examples:**
```bash
garden-rake template list
```

---

### GET /api/templates/:name

**Get raw YAML content of a template.**

**Response (200 OK):**
```yaml
name: mongodb
category: databases
description: MongoDB NoSQL database
image: mongo:7.0
ports:
  - container: 27017
    host: 27017
environment:
  MONGO_INITDB_ROOT_USERNAME: admin
  MONGO_INITDB_ROOT_PASSWORD: ${GENERATED}
volumes:
  - name: ${SERVICE}-data
    mount: /data/db
```

**Errors:**
- `404` - Template not found

**CLI Examples:**
```bash
garden-rake template show mongodb
```

---

## Real-Time Streams (SSE)

Server-Sent Events for logs and system events.

### GET /api/services/:service/logs

**Stream service container logs in real-time.**

Returns Server-Sent Events stream of container stdout/stderr.

**Query Parameters:**
- `timestamps=true` - Include ISO8601 timestamps
- `tail=100` - Number of historical lines to include (default: all)

**Response (200 OK - text/event-stream):**
```
data: 2026-01-17T10:30:00Z Starting MongoDB...
data: 2026-01-17T10:30:01Z Listening on port 27017
data: 2026-01-17T10:30:02Z Waiting for connections
```

**Errors:**
- `404` - Service not found
- `500` - Docker logging driver not compatible

**Edge Cases:**
- Container restart: Stream continues with new container logs
- Service removed: Stream closes with error event
- Network timeout: Client should reconnect

**CLI Examples:**
```bash
garden-rake watch offering mongodb logs
garden-rake watch offering mongodb logs --timestamps
```

---

### GET /api/events

**Stream moss system events in real-time.**

Server-Sent Events stream of moss operations and service state changes.

**Response (200 OK - text/event-stream):**
```
event: service.created
data: {"service": "mongodb", "timestamp": "2026-01-17T10:30:00Z"}

event: service.started
data: {"service": "mongodb", "timestamp": "2026-01-17T10:30:05Z"}

event: service.stopped
data: {"service": "redis", "timestamp": "2026-01-17T10:31:00Z"}
```

**Event Types:**
- `service.created` - Service installed
- `service.started` - Service started
- `service.stopped` - Service stopped
- `service.removed` - Service deleted
- `service.upgraded` - Service image updated
- `system.reconcile` - Reconciliation completed

**CLI Examples:**
```bash
garden-rake watch
garden-rake watch until 'completed'
```

---

## Async Jobs (Reserved)

Job tracking infrastructure for future long-running operations.

### GET /api/jobs

**List all async jobs.**

Returns list of background jobs and their statuses.

**Response (200 OK):**
```json
{
  "jobs": []
}
```

**Note:** Currently unused - reserved for future async operations like multi-stone upgrades or backup/restore.

---

### GET /api/jobs/:job_id

**Get async job status.**

Check status of specific background operation.

**Response (200 OK):**
```json
{
  "job_id": "uuid-here",
  "status": "completed",
  "result": {...}
}
```

**Status Values:**
- `pending` - Job queued
- `running` - Job in progress
- `completed` - Job finished successfully
- `failed` - Job encountered error

**Errors:**
- `404` - Job not found

**Note:** Infrastructure in place but no jobs currently use this system.

---

## Discovery

Multi-stone network discovery via mDNS.

### GET /api/peer-stones

**Query discovered stones in the garden.**

Returns list of stones discovered via mDNS/Avahi, simpler format than `/api/v1/garden`.

**Response (200 OK):**
```json
{
  "stones": [
    {
      "name": "stone-01",
      "endpoint": "http://192.168.1.100:7185"
    },
    {
      "name": "stone-02",
      "endpoint": "http://192.168.1.101:7185"
    }
  ]
}
```

**Use Cases:**
- Quick stone discovery
- Network topology visualization
- Load balancing stone selection

---

## System Administration

### POST /api/system/reconcile

**Reconcile Moss registry with existing containers.**

Adopts orphaned zen-offering-* containers and optionally removes invalid ones.

**Request:**
```json
{
  "drop_invalid": false
}
```

**Response (200 OK):**
```json
{
  "adopted": ["mongodb", "redis"],
  "dropped_invalid": [],
  "left_unregistered": [],
  "message": "Reconciliation complete: 2 adopted, 0 dropped"
}
```

**Use Cases:**
- After Moss restart or upgrade
- Recovering from registry corruption
- Cleaning up manual container operations
- Migration from external container management

**CLI Examples:**
```bash
garden-rake reconcile
garden-rake reconcile --drop-invalid
```

---

### POST /api/system/refresh

**Refresh Moss or Rake binary on stone.**

Upload and replace garden-moss or garden-rake binary. Moss automatically restarts after update.

**Request (multipart/form-data):**
- `component` - "moss" or "rake"
- `binary` - Binary file upload

**Response (200 OK):**
```json
{
  "message": "Component refreshed successfully",
  "component": "moss",
  "version": "0.1.0",
  "restart_required": true
}
```

**Security Notes:**
- Development/testing use only
- No authentication (Phase 1-3)
- Validates binary architecture before installation
- Automatic Moss restart after upload

**CLI Examples:**
```bash
garden-rake refresh moss --from ./target/release/garden-moss
garden-rake refresh rake --from ./dist/linux-x64/garden-rake
```

---

### GET /api/system/templates/:name/sources

**Get template image sources and fallbacks.**

Returns list of available image sources for an offering template, including architecture-specific fallbacks.

**Response (200 OK):**
```json
{
  "template": "postgres",
  "sources": [
    {
      "image": "postgres:16",
      "arch": "amd64",
      "compatible": false
    },
    {
      "image": "postgres:16-alpine",
      "arch": "arm64",
      "compatible": true
    }
  ]
}
```

**Use Cases:**
- Debugging compatibility decisions
- Understanding architecture fallback strategy
- Template development and testing

---

### PUT /api/system/templates/:name/compatibility

**Override compatibility decision for template.**

Manually set compatibility fallback image for a template (development/testing).

**Request:**
```json
{
  "fallback_image": "postgres:16-alpine"
}
```

**Response (200 OK):**
```json
{
  "message": "Compatibility override applied",
  "template": "postgres",
  "fallback_image": "postgres:16-alpine"
}
```

**Warning:** Manual overrides bypass automatic compatibility detection.

---

## Administrative Endpoints

### POST /admin/shutdown

**Gracefully shutdown Moss daemon.**

Stops all services and shuts down the Moss HTTP server.

**Response (200 OK):**
```json
{
  "message": "Shutting down Moss daemon",
  "services_stopped": 3
}
```

**Behavior:**
- Stops all running services
- Closes HTTP server
- Exits process with code 0

**Security:** No authentication - administrative access control pending (Phase 3b+)

---

## Rate Limits & Throttling

**Current:** None  
**Future:** Per-client rate limiting with 429 responses

---

## Versioning Strategy

- **Current:** v1 only
- **Breaking Changes:** Require major version bump (v2)
- **Backward Compatibility:** v1 endpoints maintained for minimum 6 months after v2 release
- **Deprecation:** 90-day notice via response headers and documentation

---

## Error Codes Reference

| Code | HTTP Status | Description |
|------|-------------|-------------|
| `OFFERING_NOT_FOUND` | 404 | Offering template doesn't exist |
| `SERVICE_NOT_FOUND` | 404 | Service not registered in Moss |
| `SERVICE_EXISTS` | 409 | Service name already in use |
| `PORT_CONFLICT` | 409 | Requested port already allocated |
| `DOCKER_ERROR` | 500 | Docker daemon communication failure |
| `IMAGE_PULL_FAILED` | 500 | Cannot pull container image |
| `CONTAINER_START_FAILED` | 500 | Container failed to start |
| `RESOURCE_EXHAUSTED` | 503 | Insufficient disk/memory resources |
| `POND_NOT_IMPLEMENTED` | 501 | Pond security not yet implemented |

---

## Change Log

### v1 (Current)

**Phase 1-2 (Complete):**
- Services CRUD endpoints
- Lifecycle operations (rest/wake/nourish)
- Garden topology discovery
- Offerings catalog with compatibility
- Health and capabilities introspection

**Phase 3 (Complete - Scaffolding):**
- Pond security endpoints (stubs returning 501)
- Status endpoint returns inactive state

**Phase 3b (Planned):**
- Pond security cryptographic implementation
- mTLS certificate exchange
- TOTP invitation system
- Cross-stone authentication

**Phase 4 (Planned):**
- Automated integration tests
- Performance benchmarks
- Extended error handling

---

## Client Libraries

### Official

**Rake CLI:** Built-in client with dual syntax (zen/normative)

### Community

None yet - contributions welcome!

**Reference Implementation:** See `src/rake/src/main.rs` for HTTP client patterns

---

## Support & Feedback

- **Issues:** GitHub Issues
- **Documentation:** `/docs` directory
- **Architecture Decisions:** `/docs/decisions/*.md`

---

**Last Updated:** January 17, 2026  
**API Version:** v1  
**Implementation Status:** Phase 3 Complete

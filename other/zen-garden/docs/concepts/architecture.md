# Architecture

**High-level system architecture - component relationships and communication flows**

**Purpose**: Understand how Moss, Rake, Docker Compose, mDNS, Lantern, and Pond work together  
**Audience**: Developer, Maintainer, Contributor

---

## Contents

- [Overview](#overview)
- [Core Components](#core-components)
- [Communication Flows](#communication-flows)
- [Design Philosophy](#design-philosophy)
- [Scale Assumptions](#scale-assumptions)
- [State Management](#state-management)

---

## Overview

Zen Garden is a distributed infrastructure management system treating physical machines as a compute fabric. Services are deployed across "Stones" (devices) using Docker Compose, discovered automatically via mDNS, and managed through a unified HTTP API.

**Key insight**: Decouple services from machines through automatic discovery. Apps reference services by type (`zen-garden:mongodb`), not by machine name (`old-laptop-01:27017`).

---

## Core Components

### Stone (Physical Device)

Any laptop, desktop, Raspberry Pi, or thin client running Garden-Moss daemon. Stones offer services to apps.

**Responsibilities**:
- Run Docker Compose services
- Announce availability via mDNS
- Respond to management commands
- Monitor service health
- Report resource usage

**Examples**:
- 2015 laptop → MongoDB Stone (4GB RAM, 500GB HDD)
- Thin client → Redis Stone (2GB RAM, low power)
- Old desktop → Storage Stone (2TB HDD, repurposed)

→ See: [guides/hardware.md](../guides/hardware.md)

### Garden-Moss Daemon

Rust daemon running on each Stone (port 7185). Manages service lifecycle and coordinates with other Stones.

**Responsibilities**:
- Listen for management requests (HTTP API)
- Execute service operations (plant, take away, upgrade)
- Announce self and services via mDNS
- Monitor container health
- Maintain Stone registry (discovered Stones)
- Validate service templates
- Coordinate garden-wide operations

**What it does NOT do**:
- Application-level orchestration (use Kubernetes for that)
- Cross-Stone data replication (services handle their own persistence)
- Load balancing between service instances
- Container runtime (delegates to Docker Engine)

→ See: [specs/moss-daemon.md](../specs/moss-daemon.md)

### Rake CLI

Command-line tool for operators to discover Stones and manage services.

**Responsibilities**:
- Discover Stones via mDNS or Lantern
- Send HTTP commands to target Stone(s)
- Display results with visual feedback
- Cache endpoint resolution (hot cache)
- Coordinate multi-Stone operations

**Common commands**:
```bash
garden-rake discover              # Find all Stones
garden-rake offer mongodb         # Plant MongoDB on local Stone
garden-rake list --all            # List services on all Stones
garden-rake upgrade --all         # Upgrade all services everywhere
garden-rake health               # Check Garden health
```

→ See: [reference/rake-cli.md](../reference/rake-cli.md)

### Docker Compose

Service orchestration engine on each Stone. Moss generates `docker-compose.yml` files from offering templates.

**Why Docker Compose (not Swarm/Kubernetes)?**:
- Simpler for home labs (3-10 Stones)
- Per-Stone service management (not cluster-wide)
- Mature, stable, well-understood
- Sufficient for target scale

**Integration**:
- Moss writes `docker-compose.yml` atomically
- Moss runs `docker compose up -d` for deployment
- Health checks mapped to Docker healthchecks
- Container logs accessible via Docker CLI

### mDNS (Multicast DNS)

Automatic discovery protocol (20+ years proven, used by AirPlay/Chromecast).

**How it works**:
1. Stone announces: `_koan-stone._tcp.local.` with TXT record (`offering=mongodb port=27017`)
2. App queries: "Who offers mongodb?"
3. Stone responds with connection details
4. App connects using standard driver

**Built into**:
- macOS (Bonjour)
- Linux (Avahi)
- Windows (requires Bonjour for Windows or Lantern)

→ See: [specs/discovery.md](../specs/discovery.md)

### Lantern (Optional)

HTTP directory service (port 7184) for cross-subnet discovery and Windows compatibility.

**Use cases**:
- Windows clients without mDNS
- VLANs blocking multicast
- Docker Desktop network isolation
- Central monitoring dashboard

**Not needed for**:
- Linux/macOS on same LAN
- Peer-to-peer discovery sufficient

→ See: [decisions/LANTERN-0001-registry.md](../decisions/LANTERN-0001-registry.md)

### Pond (Optional)

mTLS security layer for authentication and encryption.

**Tiers**:
- **Garden Pond (Tier 1)**: Basic encryption, home lab focus
- **Deep Pond (Tier 2)**: Enterprise hardening with defense-in-depth. Audit logs, certificate rotation, multi-admin approval, compliance features.

**Components**:
- **Keystone**: Encrypted file with CA keypair
- **Cornerstone**: First Stone with certificate authority
- **Certificate binding**: mTLS authentication for all connections

→ See: [security/overview.md](../security/overview.md)

---

## Communication Flows

### Discovery Flow

```
┌─────────┐                          ┌─────────┐
│  Rake   │                          │  Stone  │
│  (CLI)  │                          │  (Moss) │
└────┬────┘                          └────┬────┘
     │                                    │
     │ 1. mDNS query: "_koan-stone._tcp" │
     ├───────────────────────────────────>│
     │                                    │
     │ 2. Response: stone-01:7185        │
     │<───────────────────────────────────┤
     │    TXT: offering=mongodb          │
     │                                    │
     │ 3. Cache endpoint (TTL 90s)       │
     │                                    │
```

### Service Management Flow

```
┌─────────┐         ┌─────────┐         ┌────────────┐         ┌────────┐
│  Rake   │         │  Moss   │         │   Docker   │         │ mDNS   │
│  (CLI)  │         │ (Daemon)│         │  Compose   │         │        │
└────┬────┘         └────┬────┘         └─────┬──────┘         └───┬────┘
     │                   │                    │                    │
     │ POST /offer/mongo │                    │                    │
     ├──────────────────>│                    │                    │
     │                   │                    │                    │
     │                   │ Validate template  │                    │
     │                   │                    │                    │
     │                   │ Write compose YAML │                    │
     │                   │                    │                    │
     │                   │ docker compose up  │                    │
     │                   ├───────────────────>│                    │
     │                   │                    │                    │
     │                   │ Container running  │                    │
     │                   │<───────────────────┤                    │
     │                   │                    │                    │
     │                   │ Announce service   │                    │
     │                   ├────────────────────────────────────────>│
     │                   │                    │                    │
     │ 200 OK            │                    │                    │
     │<──────────────────┤                    │                    │
```

### Garden-Wide Operation Flow

```
┌────────┐    ┌──────────┐    ┌──────────┐    ┌──────────┐
│  Rake  │    │  Moss-A  │    │  Moss-B  │    │  Moss-C  │
│  (CLI) │    │ (coord)  │    │ (worker) │    │ (worker) │
└───┬────┘    └────┬─────┘    └────┬─────┘    └────┬─────┘
    │              │               │               │
    │ upgrade --all│               │               │
    ├─────────────>│               │               │
    │              │               │               │
    │              │ UDP broadcast: upgrade op_id  │
    │              ├──────────────>│──────────────>│
    │              │               │               │
    │              │ Exec locally  │ Exec locally  │ Exec locally
    │              │               │               │
    │              │ Log: op_id    │ Log: op_id    │ Log: op_id
    │              │               │               │
    │              │ Response      │ Response      │
    │              │<──────────────┤<──────────────┤
    │              │               │               │
    │ 200: results │               │               │
    │<─────────────┤               │               │
```

---

## Design Philosophy

### Frictionless by Default

- **Zero configuration**: Moss starts with sane defaults
- **Auto-discovery**: No IP addresses to configure
- **Template-driven**: Curated offerings (no ad-hoc YAML editing)
- **Observable**: Visual feedback at every step

### Hot Cache Architecture

- **Discovery only once**: Endpoints cached (TTL 90s)
- **Localhost-first**: Most operations <1ms (no network)
- **Passive updates**: UDP broadcasts keep cache fresh
- **Fallback**: TTL expiry triggers re-discovery

### Localhost-First

```bash
# Rake running on Stone-A
garden-rake offer mongodb
# → HTTP POST to localhost:7185 (instant)

# Rake running on developer machine
garden-rake offer mongodb --at stone-02
# → Cache lookup → HTTP POST to stone-02:7185
```

### Template-Driven

**Anti-pattern** (ad-hoc configuration):
```bash
docker run -d -p 27017:27017 \
  -v /data/mongo:/data/db \
  -e MONGO_INITDB_ROOT_USERNAME=admin \
  mongo:7
```

**Zen Garden pattern** (curated offering):
```bash
garden-rake offer mongodb
# → Moss loads validated template
# → Applies defaults, generates compose YAML
# → Deploys with healthcheck
```

→ See: [specs/offerings.md](../specs/offerings.md)

### Home Lab Optimized

**Target scale**: 3-10 Stones (not 100+)  
**Tested**: 20 Stones in Docker environment  
**Future**: Redesign for P2P communication beyond 100 Stones

**Rationale**:
- Small team/solo admin focus
- UDP broadcast efficient for local networks
- Simplicity over premature optimization
- Proven pattern (Docker Compose, early Kubernetes)

---

## Scale Assumptions

### Supported Configurations

| Scenario        | Stones | Services/Stone | Total Services | Notes                       |
| --------------- | ------ | -------------- | -------------- | --------------------------- |
| **Home lab**    | 3-5    | 1-3            | 5-10           | Typical (Phase 1 target)    |
| **Small team**  | 5-10   | 2-4            | 15-30          | Tested configuration        |
| **Large setup** | 10-20  | 3-5            | 40-80          | Tested in Docker            |
| **Enterprise**  | 20-100 | 5-10           | 150-500        | Future (requires redesign)  |

### Limits and Constraints

**Hard limits** (Phase 1):
- **10 Stones maximum** for reliable UDP broadcast
- **5 services per Stone** (resource constraints)
- **50 total services** across Garden
- **90-second TTL** for discovery cache
- **Same subnet** (no cross-VLAN without Lantern)

**Soft limits** (observable degradation):
- **20 Stones**: UDP broadcast congestion possible
- **100+ services**: mDNS response time increases
- **Multi-subnet**: Discovery fails without Lantern

**Future scalability** (Phase 3+):
- Redesign for TCP-based P2P gossip protocol
- Hierarchical Lantern registries
- Sharding by service type or geography

---

## State Management

### Moss State

**Stateless design** (mostly):
- Service status tracked in Docker engine (authoritative)
- Stone registry in memory (rebuilt from broadcasts)
- Configuration from files (idempotent)

**Persistent state**:
- `docker-compose.yml` (service definitions)
- Offering templates (immutable)
- Configuration file (`moss.toml`)

→ See: [decisions/STATE-0001-stateless-moss.md](../decisions/STATE-0001-stateless-moss.md)

### Registry Persistence

**In-memory registry** (Stone discovery):
- Updated via UDP broadcasts (TTL 90s)
- Lost on Moss restart
- Rebuilt via fast-sync (query Lantern or probe known Stones)

**Why not persistent?**:
- Network topology changes frequently
- Stale data worse than no data
- Fast-sync completes in <5s
- Simplifies implementation (no database)

→ See: [decisions/MOSS-0001-registry.md](../decisions/MOSS-0001-registry.md)

### Concurrency Handling

**Service operations**:
- Moss tracks status: Running, Stopped, Maintenance, Degraded, Unknown
- Concurrent operations check status first
- If Maintenance, return HTTP 202 Accepted ("retry later")
- If partial availability, operation proceeds on available services only

**Example**:
```json
POST /api/operations/upgrade
Request: {} (all services)

mongodb=Running, redis=Maintenance
Response 200: {
  "upgraded": ["mongodb"],
  "skipped": [{"service": "redis", "reason": "maintenance"}]
}

mongodb=Maintenance, redis=Maintenance
Response 202: {
  "message": "All services under maintenance, retry later"
}
```

---

## Related Documentation

- **[Core Concepts](overview.md)** - 2-minute mental model
- **[Moss Daemon Spec](../specs/moss-daemon.md)** - Implementation details
- **[Rake CLI Spec](../specs/rake-cli.md)** - Command reference
- **[Discovery Spec](../specs/discovery.md)** - mDNS protocol
- **[Security Overview](../security/overview.md)** - Pond architecture
- **[Maintainer Docs](../ops/maintainers.md)** - System invariants

---

**Last Updated**: 2026-01-18

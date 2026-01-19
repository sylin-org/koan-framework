# AI Index - Zen Garden Documentation

**Purpose**: AI-friendly navigation layer for canonical documentation  
**Last Updated**: 2026-01-19  
**Canonical Sources**: 22 documents (see below)

---

## Canonical Documentation (Authoritative Sources)

These documents are the single source of truth for their respective topics. When information conflicts, these sources win.

### Core Philosophy & Vision
- **[mission.md](../meta/mission.md)** - Why Zen Garden exists: e-waste crisis (62M tonnes/year), self-hosting coordination barriers, material constraints, dual environmental benefit, social equity
- **[overview.md](../concepts/overview.md)** - Core concepts: configuration brittleness problem, resource abstraction solution, 60-second Hello World, Stones, discovery protocol, connection strings
- **[stories.md](../meta/stories.md)** - Real-world use cases: small business owner (97% cost reduction), privacy advocate (digital sovereignty), educator (tangible infrastructure)

### Technical Specifications
- **[technical.md](../specs/technical.md)** - 2500+ line comprehensive development reference: architecture, glossary, Moss daemon, Rake CLI, service templates, mDNS discovery, implementation roadmap
- **[security.md](../specs/security.md)** - Security design: threat models (home lab vs enterprise), Pond architecture (Bluetooth pairing model, two-keypair system), Tier 1 vs Tier 2, cryptographic design
- **[connection-strings.md](../reference/connection-strings.md)** - Technical API reference: connection string protocol, mDNS announcement, Lantern HTTP API, TXT record schema, service types, error handling

### API & Protocol
- **[api.md](../reference/api.md)** - HTTP API endpoints, request/response schemas, authentication
- **[api-v1.md](../specs/api-v1.md)** - v1 API architecture ADR: Offerings API (90% use case, human-friendly) vs Services API (10% use case, technical/debugging)

### Operational Documentation
- **[README.md](../README.md)** - Central navigation hub: quick start paths, audience-specific routes (visitor/operator/developer/contributor/security/AI), canonical source table
- **[hardware.md](../guides/hardware.md)** - Stone hardware guide: e-waste reframing, tier system ($0-30, $30-100, $100-250), service-to-hardware matching, environmental impact
- **[roadmap.md](../ops/roadmap.md)** - Implementation timeline: Phase 0 (protocol specs ZGP-001 to ZGP-005, Q1 2026), Phase 1 (Python prototype, Q2 2026), Phase 2 (Rust/C# production, Q3-Q4 2026)

### Configuration & Infrastructure
- **[config.md](../reference/config.md)** - Garden-Moss daemon configuration: layered config system (CLI > Env > File > Defaults), options (stone_name, port, log_level)
- **[ports.md](../reference/ports.md)** - Port registry: baseline port 7184 (GRDN), allocation table (7184-7199 reserved), firewall rules
- **[STRUCTURE.md](../STRUCTURE.md)** - Documentation structure rules

### Design Decisions & Architecture
- **[CONSOLIDATION-MAP.md](CONSOLIDATION-MAP.md)** - Content consolidation strategy
- **[UUID-V7-DESIGN.md](UUID-V7-DESIGN.md)** - UUID design rationale: RFC 9562, time-ordered IDs, 30-40% faster DB inserts
- **[SNAPSHOT-DESIGN.md](SNAPSHOT-DESIGN.md)** - Snapshot design patterns
- **[ADAPTER-COMPARISON.md](ADAPTER-COMPARISON.md)** - Service adapter comparison
- **[DEPLOYMENT-TESTING.md](DEPLOYMENT-TESTING.md)** - Deployment procedures
- **[BOOT-FAILURE-RECOVERY.md](BOOT-FAILURE-RECOVERY.md)** - Recovery procedures
- **[FIRST-BOOT.md](FIRST-BOOT.md)** - First boot procedures
- **[INSTALLATION-MODES.md](INSTALLATION-MODES.md)** - Installation options

---

## Glossary

**Source**: [technical.md § Glossary](../specs/technical.md#glossary), [overview.md § Core Concepts](../concepts/overview.md#core-concepts)

### Core Terms

**Stone** - Physical device running Garden-Moss Daemon (laptop, server, Raspberry Pi, thin client)  
*Source*: specs/technical.md § Glossary, concepts/overview.md § Stones

**Moss** - Daemon service on each Stone handling management requests (port 7185, HTTP API)  
*Source*: specs/technical.md § Glossary, specs/technical.md § Garden-Moss Daemon

**Rake** - CLI tool for sending commands to Stones (`garden-rake`)  
*Source*: specs/technical.md § Glossary, specs/technical.md § Rake CLI

**Offering** - Pre-defined service template (mongodb, redis, postgresql) with curated configuration  
*Source*: specs/technical.md § Glossary, specs/technical.md § Service Templates & Offerings

**Service** - Running container instance of an offering (actual deployed workload)  
*Source*: API-V1-DUAL-LAYER-DESIGN.md § Services API

**Native Service** - Database/service on its native protocol (MongoDB port 27017, Redis port 6379)  
*Source*: specs/technical.md § Glossary

**Agnostic Sidecar** - HTTP REST API wrapping native service (port 8080+) for unified access  
*Source*: specs/technical.md § Glossary, specs/technical.md § Agnostic Data API

**Pond** - Security model connecting Stones with mTLS certificates (optional, opt-in)  
*Source*: specs/security.md § Pond Security Architecture, concepts/overview.md § Optional: Pond

**Keystone** - Encrypted file containing Pond CA keypair (distributed to all Stones in pond)  
*Source*: specs/security.md § Two-Keypair Architecture, specs/technical.md § Glossary

**Cornerstone** - First Stone with Pond authority (certificate issuer for new Stones)  
*Source*: specs/security.md, specs/technical.md § Glossary

**Lantern** - HTTP directory service for Windows clients and cross-subnet discovery (optional)  
*Source*: concepts/overview.md § Optional: Lantern, reference/connection-strings.md § Lantern HTTP API

**Garden** - Logical collection of Stones working together (your infrastructure)  
*Source*: specs/technical.md § Design Philosophy

**Set** - Logical namespace for application data (maps to database/schema/prefix)  
*Source*: specs/technical.md § Glossary

### Discovery & Connectivity

**Connection String** - Format `zen-garden:<service-type>[/<database>]` for automatic resolution  
*Source*: reference/connection-strings.md § Connection String Protocol, concepts/overview.md § Connection Strings

**mDNS** - Multicast DNS protocol for local network service discovery (20+ years proven, built into macOS/Linux)  
*Source*: concepts/overview.md § Discovery Protocol, reference/connection-strings.md § mDNS Service Announcement

**TXT Record** - DNS-SD metadata announcing service capabilities (offering, version, port, health, fingerprint)  
*Source*: REFERENCE.md § TXT Record Schema

**Service Type** - DNS-SD identifier: `_koan-stone._tcp.local.` (all Stones announce under this)  
*Source*: REFERENCE.md § mDNS Service Announcement

### API Layers

**Offerings API** - Human-friendly, safe, simplified API layer (90% of users, `/api/v1/offerings`)  
*Source*: API-V1-DUAL-LAYER-DESIGN.md § Offerings API (Human Layer)

**Services API** - Technical, detailed, full control API layer (10% power users, `/api/v1/services`)  
*Source*: API-V1-DUAL-LAYER-DESIGN.md § Services API (Technical Layer)

---

## Project Invariants

**Source**: [mission.md](../meta/mission.md), [overview.md](../concepts/overview.md), [technical.md § Design Philosophy](../specs/technical.md#design-philosophy)

### Core Guarantees

1. **Resource Abstraction**: `zen-garden:mongodb` never changes, even when hardware swaps  
   *Source*: concepts/overview.md § The Core Idea

2. **Configuration Stability**: Apps use connection strings that survive machine failures  
   *Source*: concepts/overview.md § The Core Idea

3. **Frictionless by Default**: Zero configuration, sane defaults, auto-discovery  
   *Source*: specs/technical.md § Design Philosophy

4. **Security is Optional**: Pond is opt-in after initial setup ("set your stones, make sure everything is working, fill the pond")  
   *Source*: specs/security.md § Pond Security Architecture

5. **Local-First**: Most operations instant (<1ms) with no network overhead, localhost-first architecture  
   *Source*: specs/technical.md § Design Philosophy

6. **Template-Driven**: Curated offerings prevent ad-hoc Docker configurations  
   *Source*: specs/technical.md § Design Philosophy

7. **Physical Visibility**: Infrastructure is tangible (colored Stones, point at device = service)  
   *Source*: meta/mission.md § Educational Pathways, concepts/overview.md § Example: Classroom Demo

### Scale Assumptions & Limits

**Target Scale**: 10 Stones maximum for Phase 1 (tested up to 20 in Docker)  
*Source*: specs/technical.md § Scale Assumptions

**Future Redesign**: P2P communication required beyond 100 Stones  
*Source*: specs/technical.md § Scale Assumptions

**Network**: Same local LAN assumed, UDP broadcast efficient for small networks  
*Source*: specs/technical.md § Scale Assumptions

### Non-Goals (What Zen Garden Does NOT Do)

1. **Not a Cloud Replacement**: Designed for owned hardware (2-10 devices), not infinite elastic scaling  
   *Source*: meta/mission.md § The Material Constraint Reality

2. **Not Multi-Tenant**: Home lab / small trusted team focus (single admin or trusted group)  
   *Source*: specs/security.md § Home Lab Reality (Tier 1 Target)

3. **Not Enterprise Orchestration**: Simple for solo admins, not competing with Kubernetes complexity  
   *Source*: specs/technical.md § Design Philosophy

4. **Not Production-First**: Optimized for home labs, self-hosters, educators (enterprise is Tier 2 optional hardening)  
   *Source*: specs/security.md § Security Tiers

5. **Not Nation-State Secure**: Threat model is accidents > attacks, not APT/nation-state adversaries  
   *Source*: specs/security.md § Threat Models

### Environmental & Social Commitments

**E-Waste Reduction**: Extend device lifespan 3-5 years, prevent functional hardware from landfills  
*Source*: meta/mission.md § Problem 1: E-Waste Crisis, guides/hardware.md § Philosophy

**Impact Target**: 10,000 repurposed devices = 40-60 tonnes prevented from landfills  
*Source*: meta/mission.md § Problem 1: E-Waste Crisis

**Digital Sovereignty**: Data stays on-premise, zero cloud dependencies, GDPR compliance simplified  
*Source*: meta/mission.md § Social Equity Dimension

**Accessibility**: Lower barrier to self-hosting (no networking expertise required)  
*Source*: meta/mission.md § Social Equity Dimension

---

## Architecture Map

**Source**: [technical.md § Architecture Overview](../specs/technical.md#architecture-overview)

### Components

| Component | Language | Purpose | Port | Location |
|-----------|----------|---------|------|----------|
| **Garden-Moss** | Rust (Axum) | Stone daemon, HTTP API, mDNS announcer, Docker Compose manager | 7185 | `src/moss/` |
| **garden-rake** | Rust (Clap) | CLI tool for Stone discovery and management | - | `src/rake/` |
| **Garden-Lantern** | Rust (planned) | HTTP directory service, Stone registry, cross-subnet discovery | 7186 | Planned Phase 1 |
| **Agnostic Sidecars** | Various | HTTP REST wrappers for native services (MongoDB, Redis, PostgreSQL) | 8080+ | Docker Compose |
| **Service Offerings** | YAML | Pre-defined service templates (mongodb, redis, postgresql, etc.) | - | `offerings/` |

### Ports & Network

**Port Allocation** (*Source*: [ports.md](../reference/ports.md)):
- **7184** (UDP): P2P discovery broadcasts (stone-to-stone peer discovery)
- **7185** (TCP): Garden-Moss HTTP API (primary management endpoint)
- **7186** (TCP): Garden-Lantern Registry (planned Phase 1, centralized directory)
- **7187** (UDP): Garden-Lantern Election (planned Phase 1, multi-active coordination)
- **7188-7199**: Reserved for future Zen Garden infrastructure

**Baseline Port**: 7184 = GRDN (phone keypad mapping: G=7, R=18, D=4, N=4)

### Discovery Mechanisms

**mDNS (Primary)** (*Source*: reference/connection-strings.md § mDNS Service Announcement):
- Service type: `_koan-stone._tcp.local.`
- TXT record fields: offering, version, port, capabilities, priority, health, fingerprint
- Built into Linux (Avahi), macOS (Bonjour)

**UDP Broadcast (Fallback)** (*Source*: specs/technical.md § mDNS Discovery):
- Port 7184, broadcast `DiscoveryRequest` → stones respond with `DiscoveryResponse`
- Used when mDNS unavailable (Windows, containers)

**Lantern HTTP (Cross-Subnet)** (*Source*: reference/connection-strings.md § Lantern HTTP API):
- Stone registration: `POST /api/register` (60s TTL, periodic heartbeat)
- Service resolution: `GET /api/resolve?service=<type>`
- Full topology: `GET /api/stones`

### Data Flow

**Source**: specs/technical.md § Architecture Overview

```
1. Rake → mDNS query (_moss._tcp.local.) → Discover Moss endpoints
2. Rake → HTTP POST (offer service) → Moss
3. Moss → Docker Compose (update docker-compose.yml)
4. Moss → Docker (docker compose up -d)
5. Moss → mDNS announce (service available)
```

---

## Where to Find X

### API Documentation

**HTTP Endpoints**: [api.md](../reference/api.md), [connection-strings.md § Lantern HTTP API](../reference/connection-strings.md#lantern-http-api)  
**v1 Dual-Layer Design**: [api-v1.md](../specs/api-v1.md)  
**Connection Strings**: [connection-strings.md § Connection String Protocol](../reference/connection-strings.md#connection-string-protocol)  
**Error Handling**: [connection-strings.md § Error Handling](../reference/connection-strings.md#error-handling), [technical.md § Error Handling](../specs/technical.md#error-handling)

### Security & Authentication

**Threat Models**: [security.md § Threat Models](../specs/security.md#threat-models)  
**Pond Architecture**: [security.md § Pond Security Architecture](../specs/security.md#pond-security-architecture)  
**Bluetooth Pairing Model**: [security.md § Bluetooth Pairing Model](../specs/security.md#bluetooth-pairing-model)  
**Cryptography**: [security.md § Cryptographic Design](../specs/security.md#cryptographic-design)  
**Security Tiers**: [security.md § Security Tiers](../specs/security.md#security-tiers) (Tier 1: Garden Pond, Tier 2: Deep Pond)

### Discovery & Networking

**mDNS Protocol**: [connection-strings.md § mDNS Service Announcement](../reference/connection-strings.md#mdns-service-announcement)  
**TXT Records**: [connection-strings.md § TXT Record Schema](../reference/connection-strings.md#txt-record-schema)  
**Port Allocation**: [PORT-ALLOCATION.md](PORT-ALLOCATION.md)  
**Lantern Directory**: [protocols.md § Lantern HTTP API](../reference/protocols.md#lantern-http-api)

### CLI (garden-rake)

**CLI Reference**: [technical.md § Rake CLI](../specs/technical.md#rake-cli)  
**Command Examples**: [overview.md § Hello World](../concepts/overview.md#hello-world-60-seconds)  
**Watch Command**: [WATCH-COMMAND.md](WATCH-COMMAND.md)  
**CLI v1 Migration**: [CLI-V1-MIGRATION.md](CLI-V1-MIGRATION.md)

### Service Manifests & Offerings

**Service Templates**: [technical.md § Service Templates & Offerings](../specs/technical.md#service-templates--offerings)  
**Service Catalog**: [service-catalog.md](../reference/service-catalog.md) (moved from SERVICE-INVENTORY.md)  
**Supported Services**: [protocols.md § Supported Service Types](../reference/protocols.md#supported-service-types)  
**Adapter Comparison**: [ADAPTER-COMPARISON.md](ADAPTER-COMPARISON.md)

### Configuration

**Moss Configuration**: [config.md](../reference/config.md) (layered config: CLI > Env > File > Defaults)  
**Port Registry**: [PORT-ALLOCATION.md](PORT-ALLOCATION.md)  
**Stone Naming**: [config.md § stone_name](../reference/config.md)

### Hardware & Setup

**Hardware Selection**: [hardware.md](../guides/hardware.md) (tier system: $0-30, $30-100, $100-250)  
**USB Installer**: [STONE-INSTALLATION-FLOW.md](STONE-INSTALLATION-FLOW.md), installer/NewStone.ps1  
**First Boot**: [FIRST-BOOT.md](FIRST-BOOT.md)  
**Installation Modes**: [INSTALLATION-MODES.md](INSTALLATION-MODES.md)  
**E-Waste Test Plan**: [EWASTE-TEST-PLAN.md](EWASTE-TEST-PLAN.md)

### Roadmap & Planning

**Project Timeline**: [roadmap.md](../meta/roadmap.md) (Phase 0-2, Q1-Q4 2026)  
**Implementation Status**: [V1-IMPLEMENTATION-COMPLETE.md](V1-IMPLEMENTATION-COMPLETE.md) (archived, v1 API complete)  
**Migration Guides**: [CLI-V1-MIGRATION.md](CLI-V1-MIGRATION.md), [UUID-V7-MIGRATION.md](UUID-V7-MIGRATION.md)

### Design Decisions (ADRs)

**Architecture Decisions**: [decisions/](../decisions/) directory  
**Build System**: BUILD-0001 (Natural Flow Versioning)  
**API Design**: [api-v1.md](../specs/api-v1.md) (dual-layer ADR)  
**UUID Strategy**: [UUID-V7-DESIGN.md](UUID-V7-DESIGN.md) (RFC 9562, time-ordered)

### Philosophy & Mission

**Why Zen Garden**: [mission.md](../meta/mission.md) (e-waste, self-hosting, material constraints)  
**Core Concepts**: [overview.md](../concepts/overview.md) (resource abstraction, discovery)  
**User Stories**: [stories.md](../meta/stories.md) (small business, privacy advocate, educator)  
**Environmental Impact**: [mission.md § Dual Environmental Benefit](../meta/mission.md#dual-environmental-benefit)

---

## Document Status Key

**Source**: Frontmatter metadata (all 60 documentation files)

- **current** - Active documentation, reflects current/planned implementation
- **draft** - Work in progress, not yet validated
- **archived** - Historical record, superseded by newer documents
- **moved** - Content relocated, redirect document remains
- **proposed** - Design proposal under review

### Canonical Flag

**`canonical: true`** - Authoritative source of truth for topic (22 documents total)  
**`canonical: false`** - Supporting documentation, references canonical sources

When information conflicts, canonical sources win.

---

## AI Consumption Notes

### Frontmatter Schema

All documentation files include structured YAML frontmatter:

```yaml
---
audience: [visitor|operator|developer|contributor|security|maintainer|ai]
doc_type: [overview|tutorial|guide|reference|spec|adr|proposal|notes|analysis]
status: [current|draft|superseded|archived|proposed|moved]
last_verified: YYYY-MM-DD
canonical: [true|false]
note: "Context and purpose"
related: [cross-references]
superseded_by: "Optional for archived"
---
```

### Relationship Graph

Use `related:` frontmatter field to navigate document relationships.

### Verification Dates

`last_verified` indicates documentation freshness. All canonical sources verified 2026-01-19.

### Cross-References

Source citations format: `[DOCUMENT.md § Section Heading](DOCUMENT.md#section-heading)`

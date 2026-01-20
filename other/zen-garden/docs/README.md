---
audience: [visitor, developer, operator, contributor]
doc_type: navigation
status: current
last_verified: 2026-01-20
canonical: true
note: "Navigation hub for Zen Garden documentation. Audience-specific routing with visual structure diagram."
related:
  - ../README.md
  - START_HERE.md
  - glossary.md
  - CANON_MANIFEST.md
---

# Documentation Hub

**Navigate Zen Garden documentation by your role and need.**

---

## Choose Your Path

### 🌱 I'm New Here

Start with the basics:

1. **[Project README](../README.md)** - 30-second pitch, 2-minute mental model
2. **[Start Here](START_HERE.md)** - Complete beginner path (hardware → first Stone → first service)
3. **[Glossary](glossary.md)** - Essential terms (Stone, Offering, Pond, Moss, Rake)
4. **[Core Concepts](concepts/overview.md)** - What is a Stone? How does discovery work?
5. **[System Architecture](concepts/architecture.md)** - How components fit together

**Example use case:** [Installing your first Stone](guides/first-stone.md)

---

### 🔧 I Need to Operate

Deploy, configure, and maintain your garden:

**Guides:**
- [Hardware Selection Guide](guides/hardware.md) - Choosing the right hardware for your Stone
- [Install Your First Stone](guides/first-stone.md) - Hardware selection, USB installer, verification
- [Manage Service Offerings](guides/offering-services.md) - Plant, upgrade, rest, wake, take away
- [Troubleshooting](guides/troubleshooting.md) - Common problems and solutions

**Operations:**
- [Release Notes](ops/release-notes.md) - Current release, breaking changes, known issues
- [Roadmap](ops/roadmap.md) - Development timeline and milestones

**Reference:**
- [Port Allocation](reference/ports.md) - Reserved ports (7184-7199)
- [Service Offerings](reference/offerings.md) - Available services catalog

---

### 🛠️ I Need Technical Details

Architecture, specifications, and API documentation:

**Specifications:**
- [Moss Daemon](specs/moss-daemon.md) - HTTP API, Docker Compose, health monitoring
- [Rake CLI](specs/rake-cli.md) - Hot cache discovery, UDP broadcast, CLI commands
- [Service Offerings](specs/offerings.md) - Templates, taxonomy, query recommendations
- [Discovery Protocol](specs/discovery.md) - mDNS, TXT records, connection string resolution
- [Security (Full)](specs/security.md) - Pond security, mTLS, threat models

**API Reference:**
- [HTTP API](reference/api.md) - Moss endpoints, request/response formats
- [Connection Strings](reference/connection-strings.md) - Protocol details, mDNS announcement
- [System Architecture](reference/system-architecture.md) - Technical architecture, project structure
- [Design Patterns](reference/patterns/network-singleton-pattern.md) - Network singleton pattern

**Build & Deployment:**
- [Build Distribution](ops/build-distribution.md) - Build artifacts, commands, release packaging
- [Build Optimization](ops/build-optimization.md) - Release vs debug, size optimization

---

### 🔐 I Need Security Information

Understand threat models and optional security layer:

**Security Documentation:**
- [Security Overview](security/overview.md) - Default plaintext, optional Pond mTLS
- [Setting Up Pond Security](security/pond-setup.md) - Certificate management, admission
- [Threat Analysis](security/threat-analysis.md) - Attack vectors, mitigations

**Complete Specification:**
- [Security Specification](specs/security.md) - Comprehensive cryptography details

---

### 🏗️ I Want to Contribute

Architecture decisions, proposals, and development:

**Architecture Decisions:**
- [Decision Records](decisions/) - ADRs documenting design choices
- [Decision Index](decisions/README.md) - Browse by category (DATA, WEB, MESS, etc.)

- [Build Distribution](ops/build-distribution.md) - Build process and packaging
- [Build Optimization](ops/build-optimization.md) - Optimization strategies
- [Joy in Infrastructure](architecture/joy-in-infrastructure.md) - Design philosophy

**Documentation:**
- [Canonical Manifest](CANON_MANIFEST.md) - Authoritative list of all canonical files
- [Coordination Report](COORDINATION_REPORT.md) - Documentation cleanup and consolidation
- [AI Navigation](reference/ai-navigation.md) - AI-friendly documentation navigation
**Proposals:**
- [Proposals Directory](proposals/) - Feature proposals and evaluations
  - [Bridges](proposals/bridges.md) - Garden-to-garden federation with capability sharing
  - [Ceremonies](proposals/ceremonies.md) - Long-running distributed operations with Elder Stone coordination
  - [Offering Modes](proposals/offering-modes.md) - Planted (Docker), Adopted (native), Borrowed (external) service lifecycle
  - [Stone Profiles](proposals/stone-profiles.md) - Hearth, Workbench, Gateway, Full hardware role configurations
  - [Stone Lifecycle](proposals/stone-lifecycle.md) - Stone-centric operations (retire, replace, lift)
  - [CLI Taxonomy](proposals/cli-taxonomy.md) - Dual-syntax CLI design (zen + normative)

**Development:**
- [Roadmap](ops/roadmap.md) - Implementation timeline
- [Release Notes](ops/release-notes.md) - Version history

---

## Documentation Structure

```
docs/
├── README.md ...................... This file (navigation hub)
├── glossary.md .................... Essential term definitions
├── START_HERE.md .................. Beginner quickstart
│
├── concepts/
│   └── architecture.md ............ System architecture overview
│
├── guides/
│   ├── first-stone.md ............. Install your first Stone
│   ├── offering-services.md ....... Manage service lifecycle
│   └── troubleshooting.md ......... Common problems and solutions
│
├── specs/
│   ├── moss-daemon.md ............. Moss daemon specification
│   ├── rake-cli.md ................ Rake CLI specification
│   ├── offerings.md ............... Service offerings specification
│   ├── discovery.md ............... Discovery protocol specification
│   ├── security.md ................ Complete security specification
│   └── technical.md ............... Comprehensive technical spec (legacy)
│
├── security/
│   ├── overview.md ................ Security posture summary
│   ├── pond-setup.md .............. Pond security setup guide
│   └── threat-analysis.md ......... Threat models and mitigations
│
├── reference/
│   ├── api.md ..................... HTTP API reference
│   ├── connection-strings.md ...... Protocol and mDNS details
│   ├── offerings.md ............... Available services catalog
│   └── ports.md ................... Port allocation registry
│
├── ops/
│   ├── release-notes.md ........... Release history and known issues
│   └── roadmap.md ................. Development timeline
│
└── decisions/
    └── (ADRs) ..................... Architecture decision records
```

---

## Quick Links by Topic

### Getting Started
- [Project README](../README.md) - What is Zen Garden?
- [First Stone Installation](guides/first-stone.md) - Hardware → service deployment
- [Glossary](glossary.md) - Learn the vocabulary

### Service Management
- [Offering Services Guide](guides/offering-services.md) - Full lifecycle management
- [Service Catalog](reference/offerings.md) - Available offerings
- [Troubleshooting](guides/troubleshooting.md) - Fix common issues

### API & Integration
- [HTTP API Reference](reference/api.md) - Moss endpoints
- [Connection Strings](reference/connection-strings.md) - zen-garden:// protocol
- [Discovery Protocol](specs/discovery.md) - mDNS details

### Security
- [Security Overview](security/overview.md) - Default posture
- [Pond Setup](security/pond-setup.md) - Optional mTLS
- [Threat Analysis](security/threat-analysis.md) - Attack vectors

### Development
- [Moss Daemon Spec](specs/moss-daemon.md) - Implementation details
- [Rake CLI Spec](specs/rake-cli.md) - CLI tool details
- [Roadmap](ops/roadmap.md) - What's coming
- [Architecture Decisions](decisions/) - Design rationale

---

## See Also

- **[Project Root README](../README.md)** - Overview and quick pitch
- **[Glossary](glossary.md)** - Term definitions
- **[START_HERE](START_HERE.md)** - Beginner tutorial

---

**Last Updated:** January 18, 2026

# Glossary

**Purpose**: Single source of truth for all Zen Garden terminology  
**Audience**: All (visitor, operator, developer, contributor, security, AI)

---

## Contents

- [Core Components](#core-components)
- [Services & Templates](#services--templates)
- [Discovery & Networking](#discovery--networking)
- [Security](#security)
- [Operations](#operations)

---

## Core Components

**Garden** - Logical collection of Stones working together as distributed infrastructure. Example: "My home lab Garden has 3 Stones running MongoDB, Redis, and MinIO."

**Stone** - Physical device running Garden-Moss daemon. Any laptop, desktop, Raspberry Pi, or thin client can be a Stone. Offers services to apps via automatic discovery.  
→ See: [guides/hardware.md](guides/hardware.md)

**Moss** - Daemon service running on each Stone (port 7185). Manages Docker Compose services, announces via mDNS, responds to management commands from Rake.  
→ See: [specs/moss-daemon.md](specs/moss-daemon.md)

**Rake** - CLI tool (`garden-rake`) for discovering Stones and sending management commands. Operators use Rake to install services, check health, and coordinate operations.  
→ See: [reference/rake-cli.md](reference/rake-cli.md)

**Lantern** - Optional HTTP directory service (port 7184) for cross-subnet discovery and Windows compatibility. Not required for Linux/macOS on same LAN.  
→ See: [decisions/LANTERN-0001-registry.md](decisions/LANTERN-0001-registry.md)

**Cornerstone** - First Stone in a Pond with certificate authority. Issues certificates to other Stones during admission. Only one Cornerstone per Pond.  
→ See: [security/pond-setup.md](security/pond-setup.md)

---

## Services & Templates

**Offering** - Pre-defined service template (YAML file) specifying Docker image, ports, volumes, environment variables, and healthcheck. Examples: `mongodb`, `redis`, `postgresql`.  
→ See: [reference/offerings.md](reference/offerings.md)

**Service** - Running container instance of an offering. A Stone may run multiple services simultaneously (MongoDB + Redis on same Stone).

**Template** - Service configuration blueprint (YAML spec). Operators can create custom templates for services not in the standard catalog.  
→ See: [specs/offerings.md](specs/offerings.md)

**Catalog** - Collection of available offerings. Moss maintains a catalog of validated templates in `/etc/zen-garden/offerings/`.

**Native Service** - Database/service running on its native protocol. Examples: MongoDB on port 27017, Redis on 6379, PostgreSQL on 5432. Apps connect using standard drivers.

**Agnostic Sidecar** - HTTP REST API wrapping a native service (port 8080+). Provides protocol-agnostic access for clients that can't use native drivers.  
→ See: [specs/api-design.md](specs/api-design.md)

**Set** - Logical namespace for application data (maps to database/schema/prefix). Example: `zen-garden:mongodb/production` connects to MongoDB's `production` database.

---

## Discovery & Networking

**Discovery** - Protocol for finding services and Stones on the local network. Uses mDNS (multicast DNS) for automatic announcement and resolution. Services advertised as `<name>._http._tcp.local`.  
→ See: [decisions/LANTERN-0003-mdns-service-discovery.md](decisions/LANTERN-0003-mdns-service-discovery.md)

**mDNS** - Multicast DNS protocol (proven technology, 20+ years old). Zero-config service discovery on local networks. Used by AirPlay, Chromecast, Spotify Connect, and Zen Garden. Built into macOS (Bonjour) and Linux (Avahi).  
→ See: [decisions/LANTERN-0003-mdns-service-discovery.md](decisions/LANTERN-0003-mdns-service-discovery.md)

**Service Advertisement** - mDNS broadcast announcing service availability. Format: `<name>._http._tcp.local` with TXT records for metadata (garden, stone, version). Example: `MediaX._http._tcp.local` points to `stone-02.local:8080`.

**Friendly Proxy Names** - Optional reverse proxy layer on Cornerstone exposing services as `<name>.zen-garden.local`. Cornerstone discovers services via mDNS, verifies with Moss API, exposes unified naming. Example: `http://MediaX.zen-garden.local` → `stone-02:8080`.  
→ See: [decisions/LANTERN-0003-mdns-service-discovery.md](decisions/LANTERN-0003-mdns-service-discovery.md)

**Announcement** - Broadcast message where a Stone announces "I offer [service]" via mDNS. Includes service type, port, and metadata in TXT records.

**Connection String** - Format for requesting services: `zen-garden:<service-type>[/<database>]`. Example: `zen-garden:mongodb/mydb`. Resolver translates to native connection string.  
→ See: [reference/connection-strings.md](reference/connection-strings.md)

**Registry** - In-memory catalog of discovered services and Stones. Moss maintains a registry updated via UDP broadcasts (TTL 90 seconds).  
→ See: [decisions/MOSS-0001-registry.md](decisions/MOSS-0001-registry.md)

**TXT Record** - mDNS metadata field containing offering name, version, capabilities, and Stone ID. Example: `offering=mongodb version=7.0 stone_id=01936d2e-...`.

**Topology** - Map of all Stones and services in a Garden (network graph). Used for health dashboards and coordinated operations.

---

## Security

**Pond** - Security model connecting Stones with mTLS certificates. Provides authentication (verify Stone identity) and encryption (protect traffic).  
→ See: [security/overview.md](security/overview.md)

**Keystone** - Encrypted file containing Pond CA (certificate authority) keypair. Cornerstone holds the Keystone and uses it to issue certificates to joining Stones. Protected using best available method: TPM 2.0 (hardware-backed), vTPM (hypervisor-backed), or passphrase encryption (software-backed).  
→ See: [security/pond-setup.md](security/pond-setup.md), [decisions/SECURITY-0003-keystone-protection-tiers.md](decisions/SECURITY-0003-keystone-protection-tiers.md)

**TPM (Trusted Platform Module)** - Hardware security chip for cryptographic operations. Zen Garden auto-detects TPM 2.0 and seals Keystone in hardware when available. Provides physical tamper resistance and boot attestation.  
→ See: [decisions/SECURITY-0003-keystone-protection-tiers.md](decisions/SECURITY-0003-keystone-protection-tiers.md)

**Keystone Protection Tiers** - Automatic security capability detection:
- **Hardware-backed**: TPM 2.0 (keys sealed in physical chip)
- **Hypervisor-backed**: vTPM (VM isolation via KVM/VMware/Hyper-V)
- **Software-backed**: Passphrase encryption (AES-256-GCM fallback)  
→ See: [decisions/SECURITY-0003-keystone-protection-tiers.md](decisions/SECURITY-0003-keystone-protection-tiers.md)

**Garden Pond** (Tier 1) - Basic Pond security for home labs. Prevents network sniffing and rogue devices. Assumes trusted operators and physical security. Created with: `garden-rake place keystone`  
→ See: [decisions/SECURITY-0001-pond-tiers.md](decisions/SECURITY-0001-pond-tiers.md)

**Deep Pond** (Tier 2) - Enterprise-grade Pond security with additional layers. Adds audit logging, certificate rotation, multi-admin approval, and compliance features. The keystone rests deeper, providing defense-in-depth. Created with: `garden-rake place keystone deep`  
→ See: [decisions/SECURITY-0001-pond-tiers.md](decisions/SECURITY-0001-pond-tiers.md)

**Stone Admission** - Process of joining a Stone to a Pond. Cornerstone verifies Stone authenticity (via TOTP or challenge-response) and issues certificate.  
→ See: [proposals/totp-admission.md](proposals/totp-admission.md)

---

## Operations

**Installer** - USB creation tool (`NewStone.ps1` on Windows) that generates bootable Debian drives with preseed configuration. Automates Stone provisioning.

**Provisioning** - Stone setup process: boot from USB → install Debian → configure networking → install Moss daemon → join Garden.  
→ See: [guides/first-stone.md](guides/first-stone.md)

**Migration** - Moving services between Stones. Example: MongoDB running on failing laptop → migrate to replacement Stone without app downtime.

**Health** - Service/Stone status monitoring. Moss checks container healthchecks and reports: Running, Stopped, Maintenance, Degraded, or Unknown.

**Diagnostics** - Troubleshooting data collection (logs, metrics, container state). Used to debug discovery failures, connection errors, and performance issues.  
→ See: [guides/troubleshooting.md](guides/troubleshooting.md)

**Compatibility** - Hardware/architecture validation rules. Example: MongoDB requires x86_64 (not ARM) and minimum 4GB RAM for production workloads.  
→ See: [decisions/COMPAT-0001-compatibility.md](decisions/COMPAT-0001-compatibility.md)

**Version** - Timestamp-based release identifier using Natural Flow Versioning. Format: `major.minor.timestamp`. Example: `0.1.202601181256`.  
→ See: [decisions/BUILD-0001-versioning.md](decisions/BUILD-0001-versioning.md)

**E-waste** - Repurposed obsolete hardware. Zen Garden's mission is to reduce the 62M tonnes/year of electronic waste by making old devices productive again.  
→ See: [mission.md](mission.md)

**Cordon** - Mark Stone as "do not schedule new services" (existing services continue). Used when hardware is flaky (overheating, disk errors).

**Lift** - Planned Stone maintenance (need to move device to different room). Services migrated temporarily, Stone taken offline gracefully.

**Replace** - Swap failing Stone with replacement. Services migrate to new Stone, old Stone retired. Apps reconnect automatically (connection strings unchanged).

**Retire** - Responsibly end-of-life a Stone. Data wiped, hardware recycled or repurposed. Services migrated to other Stones first.

---

## Quick Reference

| Term       | Summary                                    | Category     |
| ---------- | ------------------------------------------ | ------------ |
| Stone      | Physical device running Moss               | Core         |
| Moss       | Daemon on each Stone (port 7185)           | Core         |
| Rake       | CLI tool for operators                     | Core         |
| Garden     | Collection of Stones                       | Core         |
| Offering   | Pre-defined service template               | Services     |
| mDNS       | Multicast DNS discovery protocol           | Discovery    |
| Connection String | `zen-garden:<type>[/<db>]`       | Discovery    |
| Pond       | mTLS security layer                        | Security     |
| Keystone   | Encrypted CA keypair file                  | Security     |
| Cornerstone| First Stone with CA authority              | Security     |
| Lantern    | Optional HTTP directory (port 7184)        | Discovery    |
| Set        | Logical namespace (database/schema/prefix) | Services     |
| E-waste    | Repurposed obsolete hardware               | Mission      |

---

**Last Updated**: 2026-01-18  
**Related**: [concepts/overview.md](concepts/overview.md), [specs/technical.md](specs/technical.md), [mission.md](mission.md)

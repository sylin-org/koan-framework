---
audience: [maintainer]
doc_type: guide
status: current
last_verified: 2026-01-19
canonical: false
note: "Maintainer-grade operational documentation covering system overview, invariants, release process, debugging entrypoints, and stability markers. Summarizes security operational implications (full spec in SECURITY-SPEC.md)."
related:
  - TECHNICAL-SPEC.md
  - SECURITY-SPEC.md
  - ROADMAP.md
  - decisions/
---

# Maintainer Documentation

**Operational reference for Zen Garden maintainers**

**Purpose:** System invariants, release procedures, debugging paths, stability commitments  
**Audience:** Core maintainers, infrastructure operators, security reviewers

---

## Table of Contents

1. [System Overview](#system-overview)
2. [Invariants and Safety Rules](#invariants-and-safety-rules)
3. [Release Process](#release-process)
4. [Debugging Entrypoints](#debugging-entrypoints)
5. [Stability Commitments](#stability-commitments)
6. [Security Operational Implications](#security-operational-implications)
7. [Architecture Decision Index](#architecture-decision-index)

---

## System Overview

### Component Responsibilities

**Garden-Moss Daemon** (`garden-moss`, Rust/Axum, port 7185)

**What it does:**
- Manages Docker Compose services on a single Stone
- Announces services via mDNS (`_koan-stone._tcp.local.`)
- Provides HTTP API for service lifecycle (install, upgrade, uninstall)
- Monitors service health (health checks, resource usage)
- Maintains Stone registry (other Stones discovered via UDP broadcasts)
- Coordinates garden-wide operations (upgrade --all, backup --all)

**What it does NOT do:**
- Does NOT manage containers directly (delegates to Docker Compose)
- Does NOT persist state to disk (registry rebuilt from UDP broadcasts on restart)
- Does NOT require database (stateless, eventual consistency model)
- Does NOT enforce authentication (Pond is optional, see SECURITY-SPEC.md § Pond Architecture)

**Failure modes:**
- If Moss crashes: services continue running (Docker daemon independent)
- If Moss restarts: registry rebuilt via fast-sync (Lantern query + UDP broadcasts)
- If Docker unreachable: Moss returns 503 Service Unavailable

---

**Rake CLI** (`garden-rake`, Rust/Clap)

**What it does:**
- Discovers Stones via mDNS (queries `_koan-stone._tcp.local.`)
- Sends HTTP commands to target Stone(s) (POST /api/v1/offerings, DELETE /api/v1/services/{name})
- Provides visual feedback (progress spinners, colored status, tables)
- Supports garden-wide operations (--all flag broadcasts to all Stones)
- Streams real-time events (watch command via SSE)

**What it does NOT do:**
- Does NOT run as daemon (CLI invocation only)
- Does NOT persist state (discovery fresh on each invocation)
- Does NOT manage authentication (passes bearer tokens if provided)
- Does NOT retry failed operations (operator responsible for error handling)

**Failure modes:**
- If mDNS fails: falls back to Lantern HTTP directory (if configured)
- If no Stones discovered: exits with error "No Stones found on network"
- If HTTP request timeout: exits with error "Stone unreachable"

---

**Garden-Lantern Registry** (Rust, port 7186, optional)

**What it does:**
- Centralized HTTP directory for Stone discovery (fallback for mDNS-less environments)
- Stone registration endpoint (POST /api/register, 60s TTL)
- Service resolution endpoint (GET /api/resolve?service=mongodb)
- Cross-subnet discovery (mDNS limited to local broadcast domain)
- Windows client support (mDNS unreliable on Windows without Bonjour)

**What it does NOT do:**
- Does NOT manage Stones (read-only directory, no lifecycle control)
- Does NOT enforce security (Pond authentication optional)
- Does NOT persist registrations (in-memory TTL-based expiration)
- Does NOT require consensus (last-write-wins for Stone metadata)

**Failure modes:**
- If Lantern crashes: Stones continue operating via mDNS peer-to-peer
- If Lantern unreachable: Rake falls back to mDNS discovery
- If registration expired: Stone re-registers on next heartbeat (30s interval)

---

**Pond Security Layer** (optional, see SECURITY-SPEC.md)

**What it does:**
- Provides mTLS authentication between Stones
- Issues certificates to new Stones joining pond
- Validates bearer tokens for HTTP API requests
- Encrypts inter-Stone communication (UDP broadcasts, HTTP API)

**What it does NOT do:**
- Does NOT prevent physical access threats (assumes physical security)
- Does NOT protect against nation-state adversaries (threat model: home lab)
- Does NOT require central authority (distributed CA via Keystone replication)

**Operational philosophy:**
> "Set your stones, make sure everything is working, **fill the pond**."

Security is **opt-in** after initial setup. Start frictionless, add hardening when needed.

---

### Data Flow Diagrams

#### Service Deployment Flow

```
Operator                    Rake CLI                 Moss (target Stone)         Docker Compose
   │                           │                           │                            │
   │ garden-rake offer mongodb │                           │                            │
   │───────────────────────────>│                           │                            │
   │                           │                           │                            │
   │                           │ 1. mDNS query            │                            │
   │                           │   _koan-stone._tcp.local. │                            │
   │                           │───────────────────────────>│                            │
   │                           │                           │                            │
   │                           │ 2. Moss endpoint          │                            │
   │                           │   http://stone-01:7185    │                            │
   │                           │<───────────────────────────│                            │
   │                           │                           │                            │
   │                           │ 3. POST /api/v1/offerings │                            │
   │                           │    {name: "mongodb"}      │                            │
   │                           │───────────────────────────>│                            │
   │                           │                           │ 4. Read mongodb.yaml       │
   │                           │                           │───┐                        │
   │                           │                           │<──┘                        │
   │                           │                           │                            │
   │                           │                           │ 5. Update docker-compose.yml│
   │                           │                           │───┐                        │
   │                           │                           │<──┘                        │
   │                           │                           │                            │
   │                           │                           │ 6. docker compose up -d    │
   │                           │                           │────────────────────────────>│
   │                           │                           │                            │
   │                           │                           │ 7. Container started        │
   │                           │                           │<────────────────────────────│
   │                           │                           │                            │
   │                           │                           │ 8. mDNS announce           │
   │                           │                           │   (mongodb available)      │
   │                           │                           │───┐                        │
   │                           │                           │<──┘                        │
   │                           │                           │                            │
   │                           │ 9. 201 Created            │                            │
   │                           │   {state: "running"}      │                            │
   │                           │<───────────────────────────│                            │
   │                           │                           │                            │
   │  ✓ mongodb running        │                           │                            │
   │<───────────────────────────│                           │                            │
```

#### Discovery and Connection Flow

```
Application               Client Library            mDNS                    Stone (Moss)
    │                         │                       │                          │
    │ zen-garden:mongodb      │                       │                          │
    │────────────────────────>│                       │                          │
    │                         │                       │                          │
    │                         │ 1. Query              │                          │
    │                         │   _koan-stone._tcp    │                          │
    │                         │──────────────────────>│                          │
    │                         │                       │                          │
    │                         │                       │ 2. Multicast query       │
    │                         │                       │─────────────────────────>│
    │                         │                       │                          │
    │                         │                       │ 3. TXT record            │
    │                         │                       │   offering=mongodb       │
    │                         │                       │   port=27017             │
    │                         │                       │<─────────────────────────│
    │                         │                       │                          │
    │                         │ 4. stone-01:27017     │                          │
    │                         │<──────────────────────│                          │
    │                         │                       │                          │
    │ mongodb://stone-01:27017│                       │                          │
    │<────────────────────────│                       │                          │
    │                         │                       │                          │
    │ 5. Native MongoDB       │                       │                          │
    │    connection           │                       │    MongoDB container     │
    │────────────────────────────────────────────────────────────────────────────>│
```

---

## Invariants and Safety Rules

### Core Guarantees (MUST NOT Break)

**Source:** [TECHNICAL-SPEC.md § Design Decisions](TECHNICAL-SPEC.md#design-decisions-and-constraints), [UNDERSTANDING.md § Core Idea](UNDERSTANDING.md#the-core-idea)

#### 1. Connection String Stability

**Guarantee:** `zen-garden:mongodb` connection strings NEVER change, even when hardware is replaced.

**Why it matters:** This is the primary value proposition. If connection strings change on hardware swap, Zen Garden solves nothing.

**Implementation:** Connection strings resolve to service type (mongodb, redis), NOT machine identity (stone-01, stone-02). mDNS discovery finds current provider.

**Test:** Deploy mongodb to stone-01, record connection string, destroy stone-01, deploy mongodb to stone-02, verify connection string resolves correctly.

---

#### 2. Service Continuity on Moss Failure

**Guarantee:** If Moss daemon crashes, deployed services continue running.

**Why it matters:** Moss is infrastructure management, not service execution. Services should survive infrastructure failures.

**Implementation:** Moss delegates to Docker Compose. Docker daemon is independent process. If Moss dies, Docker keeps containers running.

**Test:** Deploy mongodb, verify running, kill `garden-moss` process, verify mongodb container still running, verify apps can still connect.

---

#### 3. Idempotent Operations

**Guarantee:** Repeated identical operations are safe (no double-install, no phantom uninstall).

**Why it matters:** Network failures, operator errors, automation scripts may retry. Retries should not corrupt state.

**Implementation:** Moss checks service state before operations. Install on installed service returns 409 Conflict. Uninstall on absent service returns 404 Not Found.

**Test:** Deploy mongodb twice (expect 409 second time), uninstall mongodb, uninstall again (expect 404 second time).

---

#### 4. mDNS Service Type Stability

**Guarantee:** All Stones announce under `_koan-stone._tcp.local.` service type. Service differentiation via TXT records, NOT different mDNS types.

**Why it matters:** Single query discovers all Stones. Multiple service types would require multiple queries, breaking discovery performance guarantee (<1s).

**Implementation:** Moss announces `_koan-stone._tcp.local.` with TXT record `offering=mongodb` (not `_mongodb._tcp.local.`).

**Rationale:** See [decisions/MDNS-0001-single-service-type.md](decisions/MDNS-0001-single-service-type.md) (to be created).

---

#### 5. Backward-Compatible TXT Records

**Guarantee:** New TXT record fields are additive. Existing fields never removed or renamed.

**Why it matters:** Mixed-version gardens must interoperate. Old Rake must discover new Moss (and vice versa).

**Implementation:** Version negotiation via `version=1.2.3` TXT field. Clients ignore unknown fields.

**Deprecated fields:** Marked in docs, kept for 2 major versions, then removed.

**Example:**
- v0.1: `offering=mongodb`, `port=27017`
- v0.2: Add `health=healthy` (old clients ignore)
- v0.3: Add `fingerprint=abc123` (old clients ignore)
- v1.0: Deprecate `port` field (but keep it), add `endpoints=tcp:27017,http:8080`
- v3.0: Remove deprecated `port` field (2 major versions later)

---

#### 6. Best-Effort Delivery (No Guaranteed Ordering)

**Guarantee:** UDP broadcasts are best-effort. Operations may arrive out-of-order or not at all.

**Why it matters:** Setting false expectations breaks trust. Zen Garden is NOT a distributed database.

**Accepted risks:**
- UDP broadcast missed: Stone registry stale for up to 90s (TTL expiration triggers re-discovery)
- Clock skew: Cursors time-ordered within Stone, NOT across Stones
- Network partition: Temporarily inconsistent view of garden topology

**Mitigation:** TTL-based fallback (90s), eventual consistency model, operator awareness.

---

### Safety Rules (SHOULD Follow)

**Source:** [SECURITY-SPEC.md § Operational Security](SECURITY-SPEC.md#operational-security)

#### 1. Fail Secure (Not Fail Open)

**Rule:** On authentication failure, reject request. Do NOT fall back to unauthenticated access.

**Example:** If Pond enabled but bearer token missing, return 401 Unauthorized (not 200 OK with degraded access).

---

#### 2. Validate Before Execute

**Rule:** Template validation before Docker Compose execution. Catch injection attacks early.

**Checks:**
- Template YAML well-formed (parse without error)
- Image tags match regex `^[a-z0-9-_./]+:[a-z0-9-_.]+$` (no shell injection)
- Port numbers in valid range 1-65535
- Volume paths absolute, no `..` traversal

**Failure:** Return 400 Bad Request with validation errors (do NOT attempt Docker operation).

---

#### 3. Graceful Degradation

**Rule:** Missing optional components should not break core functionality.

**Examples:**
- Lantern unreachable → fall back to mDNS
- Pond disabled → allow unauthenticated access (if opt-in security, not mandatory)
- Docker health check missing → mark health as "unknown" (not "failed")

---

#### 4. Operator Visibility

**Rule:** Operations should provide clear feedback. No silent failures.

**Guidelines:**
- HTTP 202 Accepted: "Request received, service busy, check back later"
- HTTP 500 Internal Server Error: "Moss encountered unexpected error, check logs at /var/log/garden-moss.log"
- Progress indicators: Rake shows spinners, progress bars for long operations

---

#### 5. No Destructive Defaults

**Rule:** Dangerous operations require explicit confirmation.

**Examples:**
- `garden-rake upgrade --all` requires `--confirm` flag (prevent accidental garden-wide upgrade)
- `garden-rake pond drain` requires `--yes-i-am-sure` (destroys Pond CA, irreversible)
- Uninstall with data: warns "This will delete volumes. Use --preserve-data to keep."

---

## Release Process

### Version Scheme

**Format:** `major.minor.timestamp`  
**Example:** `0.1.202601181256` (January 18, 2026, 12:56 UTC)

**Rationale:** See [decisions/BUILD-0001-natural-flow-versioning.md](decisions/BUILD-0001-natural-flow-versioning.md)

**Components:**
- **major**: Breaking changes (manual increment, rare)
- **minor**: Features/improvements (manual, controlled by version.json)
- **timestamp**: Build timestamp `YYYYMMDDHHmm` (automatic, generated by dist.ps1)

**Philosophy:** "Timestamp = truth, not arbitrary bug count. Simple, present-focused, flows naturally."

---

### Release Checklist

#### Pre-Release (Development)

- [ ] All tests passing (unit, integration, e2e)
- [ ] Documentation updated (README, TECHNICAL-SPEC, ADRs)
- [ ] Changelog generated (git log summary since last release)
- [ ] Security review (if changes touch authentication/authorization)
- [ ] Compatibility check (new Moss works with old Rake, vice versa)

#### Build Process

```powershell
# 1. Update version.json (if major/minor bump)
{
  "major": 0,
  "minor": 2,
  "phase": "Phase 1 Prototype"
}

# 2. Run distribution build
.\dist.ps1

# dist.ps1 performs:
# - Read version.json (get major.minor)
# - Generate timestamp: $timestamp = Get-Date -Format "yyyyMMddHHmm"
# - Update Cargo.toml files: version = "0.2.$timestamp"
# - Set environment: $env:BUILD_NUMBER = $timestamp
# - Build Rust binaries: cargo build --release
# - Copy artifacts to artifacts/dist/

# 3. Verify version embedding
.\artifacts\dist\garden-moss.exe --version
# Expected: garden-moss 0.2.202601181256

.\artifacts\dist\garden-rake.exe --version
# Expected: garden-rake 0.2.202601181256
```

#### Distribution

```bash
# 4. Package binaries
cd artifacts/dist
tar czf zen-garden-$VERSION-linux-x64.tar.gz garden-moss garden-rake
zip zen-garden-$VERSION-windows-x64.zip garden-moss.exe garden-rake.exe

# 5. Generate checksums
sha256sum zen-garden-$VERSION-* > checksums.txt

# 6. Create GitHub release
gh release create v$VERSION \
  --title "Zen Garden $VERSION" \
  --notes-file CHANGELOG.md \
  zen-garden-$VERSION-linux-x64.tar.gz \
  zen-garden-$VERSION-windows-x64.zip \
  checksums.txt

# 7. Update version.json for next release (minor bump)
# Example: 0.2.x → 0.3.x
```

#### Post-Release

- [ ] Announce release (GitHub Discussions, mailing list)
- [ ] Update documentation site (docfx build, deploy)
- [ ] Test upgrade path (0.1.x → 0.2.x upgrade, verify backward compatibility)
- [ ] Monitor issue tracker (first 48 hours critical for bug reports)

---

### Hotfix Process

**When:** Critical security vulnerability, data loss bug, crash on startup

```bash
# 1. Branch from release tag
git checkout v0.2.202601150830
git checkout -b hotfix/CVE-2026-12345

# 2. Apply minimal fix (no feature additions)
# ... make changes ...

# 3. Increment major version (breaking security fix) OR bump minor (non-breaking)
# version.json: 0.2.x → 0.3.x (if breaking) OR 0.2.x → 0.2.$(timestamp)+1

# 4. Build and test
.\dist.ps1
.\scripts\run-tests.ps1

# 5. Release as 0.3.x (or 0.2.$(new_timestamp))
gh release create v0.3.202601181400 --notes "Security fix: CVE-2026-12345"

# 6. Merge hotfix to main
git checkout main
git merge hotfix/CVE-2026-12345
```

---

## Debugging Entrypoints

### Quick Diagnostic Commands

**"Moss not responding"**
```bash
# 1. Check if Moss process running
systemctl status garden-moss
# If inactive: sudo systemctl start garden-moss

# 2. Check if listening on port 7185
ss -tlnp | grep 7185
# Expected: LISTEN 0 511 0.0.0.0:7185

# 3. Test HTTP API directly
curl http://localhost:7185/api/health
# Expected: {"status":"healthy","version":"0.2.202601181256"}

# 4. Check logs
sudo journalctl -u garden-moss -n 100 --no-pager
# Look for: startup errors, panics, HTTP 500 responses
```

**"Rake can't discover Stones"**
```bash
# 1. Check mDNS daemon (Linux)
systemctl status avahi-daemon
# If inactive: sudo systemctl start avahi-daemon

# 2. Verify mDNS announcement
avahi-browse -a | grep koan-stone
# Expected: + eth0 IPv4 stone-01 _koan-stone._tcp local

# 3. Test Lantern fallback (if configured)
curl http://lantern-host:7186/api/stones
# Expected: {"stones":[{"name":"stone-01","endpoint":"http://192.168.1.42:7185"}]}

# 4. Check firewall
sudo ufw status | grep 5353  # mDNS
sudo ufw status | grep 7185  # Moss HTTP
# If missing: sudo ufw allow 5353/udp && sudo ufw allow 7185/tcp
```

**"Service deployed but apps can't connect"**
```bash
# 1. Verify container running
docker ps | grep mongodb
# Expected: Up 5 minutes, healthy

# 2. Test native connection
mongosh mongodb://localhost:27017
# If success, problem is discovery (not service)

# 3. Check mDNS TXT record
avahi-browse -a -r | grep mongodb
# Expected: txt = ["offering=mongodb" "port=27017" "health=healthy"]

# 4. Test connection string resolution
garden-rake resolve zen-garden:mongodb
# Expected: mongodb://stone-01.local:27017
```

**"Pond authentication failing"**
```bash
# 1. Verify Stone has Pond certificate
ls -l /var/lib/zen-garden/*.pem
# Expected: stone-01.pem, ca.pem

# 2. Check certificate validity
openssl x509 -in /var/lib/zen-garden/stone-01.pem -noout -dates
# Verify: notAfter > current date

# 3. Test with bearer token
curl -H "Authorization: Bearer $(cat /var/lib/zen-garden/token)" \
  http://stone-01:7185/api/pond/status
# Expected: {"pond":"active","cornerstone":"stone-01"}

# 4. Check Pond logs
sudo journalctl -u garden-moss -n 100 | grep -i pond
# Look for: certificate validation errors, TOTP failures
```

---

### Log Locations

| Component | Log Location | Rotation |
|-----------|-------------|----------|
| **Moss daemon** | `/var/log/garden-moss.log` | Daily, keep 7 days |
| **Moss systemd** | `journalctl -u garden-moss` | systemd default (persistent) |
| **Docker containers** | `docker logs <container>` | JSON files, 10MB max |
| **mDNS (Avahi)** | `/var/log/avahi-daemon.log` | Weekly |
| **Lantern** | `/var/log/garden-lantern.log` | Daily, keep 7 days |

**Debugging environment variable:**
```bash
# Enable verbose logging
export RUST_LOG=debug,garden_moss=trace
sudo systemctl restart garden-moss

# Check logs
tail -f /var/log/garden-moss.log
```

---

### Performance Profiling

**Moss HTTP API latency:**
```bash
# Measure Rake → Moss latency
time garden-rake discover
# Expected: <1 second (mDNS + HTTP round-trip)

# Measure Moss operation latency
time garden-rake offer mongodb --to stone-01
# Expected: 2-5 seconds (image pull + container start)
```

**Resource monitoring:**
```bash
# Moss memory usage
ps aux | grep garden-moss
# Expected: <50MB RSS (Rust daemon, minimal footprint)

# Docker resource usage
docker stats
# Expected: Varies by service (MongoDB ~200MB, Redis ~20MB)
```

---

## Stability Commitments

### What is STABLE (Backward Compatible)

**Source:** [TECHNICAL-SPEC.md § mDNS Discovery](TECHNICAL-SPEC.md#mdns-discovery), [API-V1-DUAL-LAYER-DESIGN.md](API-V1-DUAL-LAYER-DESIGN.md)

#### 1. mDNS Service Type

**Stable:** `_koan-stone._tcp.local.` service type NEVER changes.

**Why:** Service type is discovery contract. Changing breaks all existing clients.

**Backward compatibility:** New Moss versions announce `_koan-stone._tcp.local.`, old Rake versions discover them.

---

#### 2. Core TXT Record Fields

**Stable fields (present since v0.1, never removed):**
- `offering=<service-type>` (e.g., mongodb, redis)
- `port=<port-number>` (deprecated v1.0, but kept for compatibility)
- `version=<semver>` (Moss version)
- `health=<status>` (healthy, degraded, unavailable)

**Additive fields (new versions may add, old clients ignore):**
- `endpoints=<uri-list>` (v1.0+: replaces port, supports multiple endpoints)
- `fingerprint=<sha256>` (v0.2+: Pond certificate hash)
- `capabilities=<flag-list>` (v0.3+: feature flags like "snapshot", "backup")

---

#### 3. HTTP API Endpoints (v1)

**Stable endpoints (v1.x series):**
- `GET /api/health` - Health check
- `GET /api/v1/offerings` - List offerings
- `POST /api/v1/offerings` - Install offering
- `DELETE /api/v1/offerings/{name}` - Uninstall offering
- `GET /api/v1/services` - List services (technical view)

**Deprecation policy:**
- Endpoints marked deprecated for 1 major version (v1.x deprecation notice)
- Removed in next major version (v2.0 removes deprecated v1 endpoints)
- Migration guide published with deprecation notice

---

#### 4. Connection String Format

**Stable:** `zen-garden:<service-type>[/<database>]`

**Examples (never change):**
- `zen-garden:mongodb` (discover MongoDB)
- `zen-garden:mongodb/mydb` (discover MongoDB, use 'mydb' database)
- `zen-garden:redis` (discover Redis)
- `zen-garden:postgresql/app` (discover PostgreSQL, use 'app' database)

**Why:** Applications embed connection strings in code. Changing format breaks all existing apps.

---

### What is EVOLVING (Subject to Change)

**Source:** [ROADMAP.md § Phase 1](ROADMAP.md#phase-1-reference-implementation-q2-2026), [TECHNICAL-SPEC.md § Implementation Roadmap](TECHNICAL-SPEC.md#implementation-roadmap)

#### 1. Agnostic Data API

**Status:** Future implementation, documented but not built.

**Reason:** Phase 1 focuses on native protocol support (mongodb://, redis://). HTTP REST API for databases is Phase 2+ feature.

**Migration path:** When implemented, announce as additional endpoint (`endpoints=tcp:27017,http:8080` in TXT record). Native protocol remains primary.

---

#### 2. Garden-Lantern Protocol

**Status:** Planned Phase 1 (Q2 2026), but protocol subject to change.

**Reason:** Cross-subnet discovery requirements not fully validated. Enterprise users may have different needs.

**Feedback window:** Protocol finalized after Phase 1 prototype feedback (Q2 2026 community validation).

---

#### 3. Pond Multi-Signature (Tier 2)

**Status:** Planned Phase 2 (Q3-Q4 2026), design in progress.

**Reason:** Enterprise security requirements (insider threat mitigation) still being validated with security team.

**Current:** Tier 1 (Garden Pond, single admin) stable. Tier 2 (Deep Pond, multi-sig) evolving.

**Source:** [SECURITY-SPEC.md § Tier 2 Hardening](SECURITY-SPEC.md#tier-2-hardening-enterprise)

---

#### 4. Service Template Schema

**Status:** Stable fields defined, but schema may gain optional fields.

**Current schema (stable):**
```yaml
name: mongodb          # STABLE (required)
offering: database     # STABLE (required)
docker:
  image: mongo:7       # STABLE (required)
  ports: [27017]       # STABLE (required)
  environment: {}      # STABLE (optional)
```

**Potential additions (Phase 2+):**
```yaml
compatibility:         # NEW (optional)
  architectures: [amd64, arm64]
  fallback_image: mongo:7-alpine

healthcheck:           # NEW (optional)
  command: ["mongosh", "--eval", "db.adminCommand('ping')"]
  interval: 30s
  timeout: 5s
```

**Backward compatibility:** Old Moss ignores unknown fields. New Moss validates new fields.

---

## Security Operational Implications

**Full specification:** [SECURITY-SPEC.md](SECURITY-SPEC.md) (canonical source)

**This section summarizes operational implications only.**

### Pond Lifecycle

#### Initialization (First Stone)

**Operator action:**
```bash
garden-rake pond init --passphrase-file passphrase.txt
```

**What happens:**
1. Generate Pond CA keypair (self-signed root certificate)
2. Encrypt CA with passphrase (AES-256-GCM)
3. Save as `/var/lib/zen-garden/keystone.enc`
4. Mark Stone as Cornerstone (first authority)
5. Announce Pond active via mDNS (`txt = ["pond=active" "cornerstone=stone-01"]`)

**Security notes:**
- Passphrase protects Keystone at rest (required for physical theft scenarios)
- CA private key NEVER leaves Stone unencrypted
- Keystone replicated to all Stones (encrypted with each Stone's identity key)

---

#### Adding Stone to Pond (Bluetooth Pairing Model)

**Operator action:**
```bash
# On Cornerstone (existing Stone in pond)
garden-rake invite stone-02

# Displays TOTP code: 836294 (6 digits, 30s validity)
# Time-based one-time password, synchronized clocks required (NTP)
```

**New Stone action:**
```bash
# On stone-02 (new Stone joining pond)
garden-rake pond join --code 836294

# Sends join request with TOTP code
# Cornerstone validates code, issues certificate
# stone-02 receives certificate, stores at /var/lib/zen-garden/stone-02.pem
```

**Security properties:**
- TOTP prevents replay attacks (30s window)
- Physical proximity assumed (operator types code from screen)
- No pre-shared secrets required (Bluetooth pairing analogy)

**Source:** [SECURITY-SPEC.md § Bluetooth Pairing Model](SECURITY-SPEC.md#bluetooth-pairing-model)

---

### Authentication Modes

#### Mode 1: No Pond (Default)

**State:** Pond not initialized, all Stones operate without authentication.

**HTTP API:** Accepts requests without `Authorization` header.

**Risk:** Anyone on network can manage services (assumed trusted LAN).

**When acceptable:** Home lab, single admin, trusted network.

---

#### Mode 2: Pond Active (Tier 1)

**State:** Pond initialized, Stones have certificates, mTLS enabled.

**HTTP API:** Requires `Authorization: Bearer <token>` header.

**Token generation:**
```bash
# On any Stone in pond
garden-rake pond token --validity 1h

# Returns: eyJhbGciOiJFZDI1NTE5...
# Token signed with Stone's identity keypair
# Moss validates token signature, checks pond membership
```

**Risk:** Single admin trusted (can issue tokens), insider threat not mitigated.

**When acceptable:** Home lab, small trusted team (family, colleagues).

**Source:** [SECURITY-SPEC.md § Tier 1 Implementation](SECURITY-SPEC.md#tier-1-implementation-mvp)

---

#### Mode 3: Deep Pond (Tier 2, Planned)

**State:** Multi-signature required for sensitive operations (add Stone, drain pond).

**HTTP API:** Some operations require multiple bearer tokens (co-signing).

**Implementation:** Phase 2 (Q3-Q4 2026), design in progress.

**When required:** Enterprise, untrusted admins, compliance (SOC2, HIPAA).

**Source:** [SECURITY-SPEC.md § Tier 2 Hardening](SECURITY-SPEC.md#tier-2-hardening-enterprise)

---

### Threat Model Summary

**Tier 1 (Home Lab):**
- Threats addressed: Network sniffing, user mistakes, physical theft (passphrase)
- Threats NOT addressed: Insider attack (single admin trusted), nation-state adversaries
- Accepted risks: Single admin can break things, weak passphrase extraction possible

**Tier 2 (Enterprise):**
- Threats addressed: Insider attack (multi-sig), lateral movement (segmentation), compliance violations (audit logs)
- Threats NOT addressed: Nation-state adversaries, zero-day exploits, supply chain attacks (defense-in-depth recommended)
- Accepted risks: Multi-sig increases complexity, recovery harder if admins unavailable

**Full threat analysis:** [SECURITY-SPEC.md § Threat Models](SECURITY-SPEC.md#threat-models)

---

### Security Maintenance

**Regular tasks:**
```bash
# 1. Check certificate expiration (monthly)
garden-rake pond status --certificates
# Warns if certificates expire within 30 days

# 2. Rotate bearer tokens (weekly for automation, manually for operators)
garden-rake pond token --revoke <old-token> --issue-new

# 3. Audit Stone membership (quarterly)
garden-rake pond list-stones
# Verify all Stones authorized, remove decommissioned Stones

# 4. Backup Keystone (after each Stone addition)
sudo cp /var/lib/zen-garden/keystone.enc ~/backups/keystone-$(date +%Y%m%d).enc
# Store off-site (encrypted, passphrase separate)
```

**Incident response:**
```bash
# Stone compromised (suspected)
garden-rake pond revoke stone-03 --reason "suspected compromise"
# Revokes certificate, Stone removed from pond, must re-join with new certificate

# Pond CA compromised (catastrophic)
garden-rake pond drain --yes-i-am-sure
# Destroys Pond CA, all Stones revert to no-pond mode
# Requires full re-initialization (new CA, new certificates)
```

---

## Architecture Decision Index

**Location:** [decisions/](decisions/) directory

### Existing ADRs

- **BUILD-0001**: Natural Flow Versioning (`major.minor.timestamp`, rationale: timestamp = truth)
- **COMPAT-0001**: Offering Compatibility Rules (architecture checks, fallback images)
- **LANTERN-0001**: Service Registry Architecture (centralized directory vs peer-to-peer)
- **MOSS-0001**: Persistent Registry and Adoption (in-memory vs disk-backed registry)
- **OFFER-0001**: Offering Taxonomy (databases, queues, storage, compute)
- **RAKE-0010**: Endpoint Resolution (cached vs fresh discovery)

### ADRs to Create (Implied by Docs)

**Source:** [API-V1-DUAL-LAYER-DESIGN.md](API-V1-DUAL-LAYER-DESIGN.md), [SECURITY-SPEC.md § Pond Architecture](SECURITY-SPEC.md#pond-security-architecture), [TECHNICAL-SPEC.md § mDNS Discovery](TECHNICAL-SPEC.md#mdns-discovery)

- **API-0001**: Dual-Layer API Design (Offerings vs Services, progressive disclosure)
- **SECURITY-0001**: Pond Two-Tier Model (Garden Pond vs Deep Pond, threat models)
- **MDNS-0001**: Single Service Type (`_koan-stone._tcp.local.` for all Stones, TXT record differentiation)
- **PROTO-0001**: Connection String Format (`zen-garden:` URI scheme, stability commitment)
- **STATE-0001**: Stateless Moss Architecture (no disk persistence, eventual consistency)

See [Creating ADRs](#creating-adrs) below.

---

## Creating ADRs

**Template:** [decisions/template.md](decisions/template.md) (to be created)

**Naming:** `{AREA}-{NUMBER}-{slug}.md` (e.g., `API-0001-dual-layer-design.md`)

**Areas:**
- API: HTTP API design
- SECURITY: Authentication, authorization, cryptography
- MDNS: Service discovery protocol
- PROTO: Wire formats, connection strings
- STATE: State management, consistency
- BUILD: Build system, versioning, packaging
- TEST: Testing strategy, validation

**Required sections:**
1. **Status** (Proposed | Accepted | Deprecated | Superseded)
2. **Context** (Problem statement, constraints)
3. **Decision** (What we decided, rationale)
4. **Consequences** (Positive, negative, risks)
5. **Alternatives Considered** (Why not X, Y, Z?)

**Process:**
1. Create PR with ADR markdown file
2. Request review from maintainers
3. Community feedback period (1 week minimum)
4. Update based on feedback
5. Merge when accepted (status: Proposed → Accepted)
6. Link from relevant docs (TECHNICAL-SPEC, SECURITY-SPEC, README)

---

## Maintenance Contacts

**Primary Maintainer:** Sylin.org (Koan Framework maintainer)  
**Security Contact:** security@zen-garden.dev (to be established)  
**Repository:** github.com/sylin/zen-garden (example, update actual URL)

**Issue Triage:**
- P0 (Critical): Security vulnerabilities, data loss, crash on startup → 24h response
- P1 (High): Service deployment failures, discovery broken → 48h response
- P2 (Medium): Performance degradation, UX issues → 1 week response
- P3 (Low): Documentation errors, feature requests → Best effort

---

**Last Updated:** January 19, 2026  
**Maintained By:** Zen Garden Core Team

---
audience: [visitor, contributor, maintainer]
doc_type: reference
status: current
last_verified: 2026-01-19
canonical: true
note: "Authoritative development roadmap and implementation timeline. Covers current status (Q1 2026 design/spec), Phase 0 (protocol specifications ZGP-001 through ZGP-005), Phase 1 (Python prototype), Phase 2 (Rust CLI + C# library). Defines deliverables, success criteria, and estimated effort."
related:
  - MISSION.md
  - TECHNICAL-SPEC.md
  - UNDERSTANDING.md
---

# Roadmap

**Implementation timeline and milestones.**

---

## Current Status

**Phase: Design and Specification (Q1 2026)**

**What exists:**
- Documentation (this repository)
- NewStone.ps1 USB installer (working, creates bootable Debian Stones)
- Service manifests (14 YAML files defining service types)
- Architecture decisions documented

**What doesn't exist yet:**
- Protocol specifications (ZGP-001 through ZGP-005)
- Reference implementation code (Rust CLI, C# library)
- Client libraries (Node.js, Python)
- Lantern HTTP service
- Pond security layer

**Maintainer:** Sylin.org (Koan Framework maintainer)  
**License:** Apache 2.0  
**Repository:** zen-garden (GitHub)

---

## Phase 0: Specification (Q1 2026)

**Goal:** Formal protocol documentation enabling multiple implementations.

### Deliverables

**Protocol Specifications (ZGP-001 through ZGP-005):**
- ZGP-001: Core mDNS protocol (service announcement, TXT record format)
- ZGP-002: Connection string resolution (zen-garden: URI scheme)
- ZGP-003: Lantern HTTP API (directory service for Windows/cross-subnet)
- ZGP-004: Pond security model (mTLS certificate binding)
- ZGP-005: Conformance tests (validation suite for implementations)

**Target:** 40-60 pages total, published under docs/specifications/

**Success criteria:**
- Independent developer can implement protocol from specs alone
- Conformance tests validate interoperability
- Community feedback incorporated (GitHub discussions)

### Timeline

- Week 1-2: ZGP-001 (Core Protocol) + ZGP-002 (Connection Strings)
- Week 3-4: ZGP-003 (Lantern API) + ZGP-004 (Pond Security)
- Week 5-6: ZGP-005 (Conformance Tests) + Community review
- Week 7-8: Revisions, final publication

**Estimated effort:** 40-60 hours (1 engineer, part-time)

---

## Phase 1: Reference Implementation (Q2 2026)

**Goal:** Working Python prototype validating protocol feasibility.

> **Note:** Phase 1 produces Python prototype utilities (`zen-announce`, `zen-pond`) for rapid validation. These are **not** production tools. The Rust CLI (`garden-rake`) and daemon (`garden-moss`) remain the primary implementation.

### Why Python First

**Rationale:**
- Rapid prototyping (test assumptions quickly)
- Rich mDNS library support (zeroconf)
- Easy installation (pip install zen-garden-resolver)
- Accessible to broad audience (educational use case)

**Not final:** Production implementations (Rust CLI, C# library) follow in Phase 2.

### Deliverables

**1. Python Resolver Library**
```python
from zen_garden import resolve

uri = resolve('zen-garden:mongodb')
# Returns: mongodb://stone-01.local:27017
```

**Features:**
- mDNS discovery (zeroconf library)
- Lantern HTTP fallback
- Connection string caching (5-minute TTL)
- Timeout handling (1 second default)

**LOC estimate:** 300-500 lines

---

**2. Stone Announcer (Python)**
```bash
zen-announce mongodb --port 27017 --version 7.0.4
```

**Features:**
- Announce service via mDNS
- Register with Lantern (if configured)
- Periodic re-announcement (30-second heartbeat)
- Graceful shutdown (de-register)

**LOC estimate:** 200-300 lines

---

**3. Lantern HTTP Service (Flask)**
```bash
garden-lantern --port 3000
```

**Features:**
- Stone registration endpoint (POST /api/register)
- Service resolution endpoint (GET /api/resolve?service=mongodb)
- Stone listing (GET /api/stones)
- Health check (GET /api/health)
- TTL-based expiration (60-second Stone heartbeat)

**LOC estimate:** 400-600 lines

---

**4. Basic mTLS (Pond - simplified)**
```bash
zen-pond init               # Create CA
zen-pond bind stone-01      # Issue certificate
```

**Features:**
- Self-signed CA generation
- Stone certificate issuance
- Fingerprint in TXT records
- Certificate validation (pinning)

**LOC estimate:** 300-500 lines

**Note:** Full Pond implementation (rotation, revocation) deferred to Phase 2.

---

### Success Criteria

**Validation tests:**
1. Announce MongoDB Stone, resolve from Python client (mDNS)
2. Announce Stone, resolve via Lantern HTTP (Windows/cross-subnet)
3. Multiple Stones, select by priority (TXT record)
4. Pond-secured Stone, validate certificate before connection
5. Stone failure, detect within 60 seconds (heartbeat timeout)

**Documentation:**
- Installation guide (pip install)
- 5-minute quickstart (announce + resolve)
- API reference (Python library)

**Community validation:**
- 3-5 external testers
- Report issues, document friction points
- Iterate on DX (developer experience)

### Timeline

- Month 1: Resolver + Announcer (core mDNS)
- Month 2: Lantern HTTP service
- Month 3: Basic Pond (mTLS)
- Month 4: Testing, documentation, community feedback

**Estimated effort:** 120-160 hours (1 engineer, part-time)

---

## Phase 2: Production Implementations (Q3-Q4 2026)

**Goal:** Production-ready Rust CLI and C# library.

### Deliverables

**1. garden-rake (Rust CLI)**
```bash
# Announce services
garden-rake announce mongodb --port 27017

# Resolve services
garden-rake resolve mongodb
# Output: mongodb://stone-01:27017

# Pond management
garden-rake pond init
garden-rake pond bind stone-01
garden-rake pond renew stone-01
```

**Features:**
- All Phase 1 features (mDNS, Lantern, Pond)
- Certificate rotation (automated)
- Service health checks (validate connectivity)
- Stone status monitoring (CPU, RAM, disk)
- JSON output mode (scripting integration)

**LOC estimate:** 2,000-3,000 lines (Rust)

**Why Rust:**
- Performance (native binary, fast startup)
- Reliability (memory safety, strong typing)
- Cross-platform (Linux, macOS, Windows)
- Ecosystem (mdns-sd crate, tokio async)

---

**2. Koan.ZenGarden (C# Library)**
```csharp
// Automatic resolution in ASP.NET
services.AddZenGarden();
services.AddMongoClient(); // Reads zen-garden:mongodb from config

// Manual resolution
var uri = await ZenGarden.ResolveAsync("zen-garden:mongodb");
```

**Features:**
- Automatic connection string resolution
- ASP.NET Core integration (dependency injection)
- Caching (IMemoryCache)
- Health checks (IHealthCheck)
- Pond certificate validation (X509Certificate2)

**LOC estimate:** 1,500-2,500 lines (C#)

**Why C#:**
- Koan Framework integration (first-class support)
- .NET ecosystem (enterprise adoption)
- Strong typing (IntelliSense, compile-time safety)

---

**3. Node.js + Python Client Libraries**
```javascript
// Node.js
const { resolve } = require('@zen-garden/resolver');
const uri = await resolve('zen-garden:mongodb');
```

```python
# Python (production-ready version)
from zen_garden import resolve
uri = resolve('zen-garden:mongodb')
```

**LOC estimate:** 500-800 lines each

---

**4. Advanced Pond Features**
- Automated certificate rotation (expiration warnings, one-command renewal)
- Revocation handling (CRL or OCSP-like mechanism)
- CA compromise recovery (full garden re-initialization)
- Audit logging (certificate issuance, revocation events)

---

### Success Criteria

**Performance:**
- Discovery latency: <100ms (mDNS), <20ms (Lantern)
- Binary size: <10MB (Rust CLI)
- Memory footprint: <50MB (Lantern service)

**Reliability:**
- 99%+ uptime (Lantern service, 30-day test)
- Graceful degradation (mDNS fails → Lantern fallback)
- Zero data loss (Stone registry persistence)

**Community:**
- 10+ external contributors (code, docs, translations)
- 50+ GitHub stars (awareness metric, not primary)
- 5+ protocol implementations (Rust, C#, Python, Node.js, Go/community)

### Timeline

- Q3 2026 (Months 1-3):
  - Month 1-2: garden-rake (Rust CLI)
  - Month 3: Koan.ZenGarden (C# library)
  
- Q4 2026 (Months 4-6):
  - Month 4: Node.js + Python production libraries
  - Month 5: Advanced Pond features
  - Month 6: Documentation, testing, release prep

**Estimated effort:** 300-400 hours (1-2 engineers, part-time)

---

## Phase 3: Ecosystem Growth (2027)

**Goal:** Educational adoption, community growth, circular economy partnerships.

### Educational Initiatives

**Curriculum Development (5-module structure):**
1. E-Waste Crisis (30 minutes) - Environmental impact, UN data
2. Hardware Basics (60 minutes) - Benchmarking, power measurement
3. Discovery Protocol (90 minutes) - mDNS deep dive, hands-on
4. Deploy First Stone (90 minutes) - Physical device setup
5. Measure Impact (60 minutes) - CO2 calculations, impact reporting

**Target:** 5 hours total (1 intensive day or 5 weekly sessions)

**Delivery formats:**
- Self-paced online (video + exercises)
- Workshop templates (in-person facilitation)
- Educator guide (lesson plans, assessment rubrics)

---

**Potential Partnerships (Exploratory):**
- **Code.org**: Hour of Code activity (40M+ students globally)
- **Library Programs**: Micro-datacenters in public libraries (digital inclusion)
- **Maker Spaces**: Hands-on workshops (hardware repurposing)
- **Community Colleges**: IT curriculum integration (infrastructure module)

**Status:** Ideas, not active partnerships. No commitments or MOUs.

---

### Circular Economy Collaborations

**E-Waste Collection Partnerships (Exploratory):**
- **North America**: Call2Recycle, e-Stewards certified recyclers
- **Brazil**: Green Eletron (national e-waste system)
- **Latin America**: RELAC (regional e-waste collaboration)
- **Africa**: Ghana/Nigeria e-waste collection programs (Accra, Lagos)

**Potential models:**
- Device donation programs (repair → repurpose → deploy as Stones)
- Refurbishment incentives (extend lifespan, delay recycling)
- Impact measurement (tonnes e-waste prevented, CO2 avoided)

**Status:** Conceptual. Requires operational capacity and partnerships.

---

### Carbon Impact Calculator

**Web Tool:**
- Input: Device type, year, service, runtime hours
- Output: CO2 avoided (manufacturing + cloud alternative), e-waste prevented
- Social sharing: "I prevented 127kg CO2 by repurposing a 2015 laptop"

**Features:**
- Embeddable widget (project websites)
- API (integrate into Stone dashboard)
- Leaderboard (community with most repurposed devices)

**LOC estimate:** 800-1,200 lines (web app + API)

---

### Community Tools

**Ambassador Program:**
- Regional representatives (10-20 volunteers)
- Local meetups, workshops
- Translation (documentation, curriculum)
- Issue triage, community support

**Contributor Ladder:**
- User → Contributor (docs, bug reports)
- Contributor → Maintainer (code review, feature development)
- Maintainer → Core Team (architectural decisions)

**Hardware Certification:**
- Tested device list (known-good hardware)
- Power consumption measurements
- Service compatibility matrix
- Community-submitted benchmarks

---

### Advanced Protocol Features

**Service Health Metadata:**
```
TXT "health=healthy" "cpu_usage=45" "memory_free=4096" "load=0.8"
```

Apps select least-loaded Stone automatically.

**Load Balancing:**
- Multiple Stones offering same service
- Priority + health-aware selection
- Automatic failover (Stone failure)

**Service Dependencies:**
```
TXT "requires=mongodb,redis" "status=waiting"
```

Stone delays announcement until dependencies available.

**IPv6 Support:**
```
stone-01.local. AAAA 2001:db8::1
```

Dual-stack support (IPv4 + IPv6).

**Plugin Architecture:**
- Community-contributed service types
- Plugin registry (vetted, versioned)
- Custom TXT schemas (service-specific metadata)

---

### Success Metrics

**Primary (Impact):**
- Devices repurposed: 1,000+ (self-reported via optional telemetry)
- E-waste prevented: 4-6 tonnes (estimated from device count)
- CO2 avoided: 300+ tonnes CO2e (manufacturing + cloud offset)

**Secondary (Community):**
- Educational adoptions: 10+ schools/maker spaces documenting use
- Protocol implementations: 5+ languages (Rust, C#, Python, Node.js, Go, etc.)
- Community retention: 20+ returning contributors month-over-month

**Not metrics:**
- GitHub stars (vanity metric)
- User count (unverifiable without telemetry)
- Media coverage (awareness ≠ impact)

---

## Beyond 2027

**Potential future work (no commitments):**

**GUI Tool:**
- Visual Stone management (non-technical users)
- Drag-and-drop service deployment
- Impact dashboard (CO2 avoided, devices repurposed)

**Enhanced Security:**
- Per-service authorization policies (not just connection-level mTLS)
- Anomaly detection (unusual traffic patterns)
- Audit logging (compliance requirements)

**Enterprise Features:**
- High availability (Stone clustering)
- Disaster recovery (automated backups)
- Monitoring integration (Prometheus, Grafana)

**Status:** Speculative. Depends on community needs and adoption.

---

## How to Contribute

**Current priorities (Phase 0-1):**
1. Review protocol specifications (GitHub discussions)
2. Test Python prototype (report issues, DX feedback)
3. Write documentation (tutorials, translations)
4. Create educational materials (lesson plans, workshop guides)

**Future opportunities (Phase 2-3):**
- Implement protocol in additional languages (Go, Ruby, Java)
- Build community tools (hardware certification, impact calculator)
- Partner with educational organizations (curriculum adoption)
- Collaborate with circular economy initiatives (e-waste collection)

**Contact:** GitHub issues/discussions (zen-garden repository)

---

## Funding and Sustainability

**Current:** Maintained by Sylin.org (organizational resources)

**No revenue model.** Open protocol, permissive license (Apache 2.0).

**Potential future support (ideas, not plans):**
- Educational grants (curriculum development)
- Circular economy partnerships (e-waste impact measurement)
- Community contributions (code, docs, translations)

**Success does not depend on funding.** Protocol succeeds if devices get repurposed and self-hosting becomes accessible.

---

## Further Reading

- [Mission](MISSION.md) - Why Zen Garden exists
- [Understanding](UNDERSTANDING.md) - How protocol works
- [Technical Reference](REFERENCE.md) - API details
- [Getting Started](GETTING-STARTED.md) - Quick setup guide

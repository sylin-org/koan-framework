---
type: PROPOSAL
domain: infrastructure
title: "Koan Satellite: Zero-Friction Container Host Constellation"
audience: [architects, developers]
status: draft
created: 2026-01-12
last_updated: 2026-01-12
framework_version: v0.6.3
---

# Koan Satellite: Zero-Friction Container Host Constellation

**Proposal for a companion project that enables extreme-beginner deployment of Koan applications on repurposed hardware.**

**Constellation Metaphor:** Deploy apps to your "satellite constellation"—multiple distributed hosts orbiting your local network.

---

## Executive Summary

**Koan Satellite** is a **separate companion project** (under `koan/satellite` GitHub org) that provides:

1. **USB stick installer** that auto-configures old computers as container hosts
2. **"Plug-and-go" experience** requiring ~5 minutes of user time
3. **Auto-discovering dashboard** showing all running Koan services across your constellation
4. **One-command deployment** from Windows/Mac dev machines to any satellite

**Target Audience:** Developers who want a local test environment without cloud costs, using $30-50 refurbished office PCs that can't run Windows 11.

**Key Differentiator:** Extends Koan's "Reference = Intent" philosophy to infrastructure—"flash USB = deployment target". Deploy to a single satellite or build a constellation.

**GitHub Repository:** `koan/satellite` (same umbrella as `koan/koan-framework`)

---

## Strategic Rationale

### Alignment with Koan Philosophy

| Koan Principle | Satellite Expression |
|----------------|---------------------|
| **Reference = Intent** | Flash USB = Get deployment target |
| **Zero Configuration** | No terminal commands after boot |
| **Container-Native** | Leverages existing orchestration infrastructure |
| **Premium DX** | Windows dev → Linux host with zero Linux knowledge |
| **Beginner-Friendly** | Fills gap left by instruction-first docs (ADR ARCH-0041) |

### Why Separate Project?

- **Documentation freedom**: Can use tutorial-style language, screenshots, videos
- **Independent lifecycle**: Updates don't trigger Koan framework releases
- **Clean separation**: Koan framework changes follow all ADR/engineering rules
- **Reduced maintenance burden**: Separate CI/CD, testing, support channels

**Repository Structure:**
- Main framework: `koan/koan-framework`
- Satellite project: `koan/satellite`
- Unified branding, separate concerns

## Architecture Overview

### Component Separation

```
┌─────────────────────────────────────────────────────────────┐
│ Koan Framework (main repo)                                  │
│                                                              │
│  ✅ Already exists:                                         │
│     - Koan.Orchestration.Aspire (self-orchestration)       │
│     - Koan.Orchestration.Cli (container management)        │
│     - Health checks, readiness probes                       │
│     - WellKnownController (metadata endpoints)              │
│                                                              │
│  ➕ Minimal additions needed:                               │
│     - Service metadata endpoint                             │
│     - Remote deployment target API                          │
│     - Health aggregation for dashboard                      │
│                                                              │
└─────────────────────────────────────────────────────────────┘
                              ▲
                              │ uses
                              │
┌─────────────────────────────────────────────────────────────┐
│ Koan Satellite (companion project - koan/satellite)         │
│                                                              │
│  📦 Components:                                             │
│     - USB installer image builder                           │
│     - Auto-install scripts (PowerShell/Bash)                │
│     - Infrastructure stack (Traefik, Homepage, Harbor)      │
│     - Windows/Mac deployment CLI                            │
│     - Tutorial documentation (beginner-friendly)            │
│     - Hardware compatibility database                       │
│                                                              │
│  GitHub: koan/satellite (same org as koan/koan-framework)   │
└─────────────────────────────────────────────────────────────┘
```

---

## User Experience Flow

### Phase 1: USB Creation (Windows/Mac - 5 minutes)

```powershell
# Download KoanBox Installer CLI
Invoke-WebRequest -Uri "https://koanbox.io/install.ps1" -OutFile "koanbox-install.ps1"

# Create bootable USB (requires admin, auto-downloads Ubuntu ISO)
.\koanbox-install.ps1 -UsbDrive "E:" -BoxName "koan-lab-01"

# Output:
# ✓ Downloaded Ubuntu Server 24.04 LTS (reduced image, 1.2GB)
# ✓ Configured auto-install with KoanBox stack
# ✓ USB ready - boot target machine and walk away
```

**User Actions:** Provide USB drive letter, choose box name. **That's it.**

### Phase 2: Hardware Setup (Unattended - 15-20 minutes)

1. Plug USB into refurb machine
2. Boot from USB (BIOS auto-detected or one-time F12)
3. **Walk away** - system auto-installs:
   - Ubuntu Server 24.04 LTS (minimal, headless)
   - Docker Engine
   - Traefik reverse proxy
   - Homepage dashboard
   - Harbor container registry (lightweight config)
   - SSH server (key from USB installer)
   - mDNS responder (`koan-lab-01.local`)

4. Machine reboots, LED pattern indicates ready state (blinking = booting, solid = ready)

### Phase 3: Deployment (Windows/Mac - 30 seconds)

```powershell
# From your Koan project directory
koanbox deploy

# Output:
# ✓ Discovered KoanBox: koan-lab-01.local (192.168.1.42)
# ✓ Built container image
# ✓ Pushed to koan-lab-01.local:5000/my-koan-app:latest
# ✓ Service started and healthy
# ✓ Available at http://koan-lab-01.local/my-koan-app
# ✓ Dashboard: http://koan-lab-01.local
```

**User Actions:** Run one command. **That's it.**

---

## Technical Architecture

### Hardware Requirements (Minimal)

| Component | Minimum | Recommended |
|-----------|---------|-------------|
| CPU | 64-bit x86/AMD64 | Intel i3/i5 4th gen+ |
| RAM | 4GB | 8GB |
| Storage | 32GB | 64GB SSD |
| Network | 100Mbps Ethernet | 1Gbps |
| Age | 2012+ | 2016+ |

**Tested Hardware:**
- Dell OptiPlex 7010 (2012, $35 refurb)
- HP EliteDesk 800 G2 (2015, $80 refurb)
- Lenovo ThinkCentre M710 (2017, $100 refurb)

### Software Stack

#### Base OS: Ubuntu Server 24.04 LTS
- **Why**: 10-year support, mature, beginner-friendly
- **Customizations**: Minimal install, pre-configured SSH, mDNS, Docker

#### Container Runtime: Docker Engine
- **Why**: Mature, well-documented, Koan already supports it
- **Not Docker Desktop**: Licensing issues, resource overhead

#### Reverse Proxy: Traefik v3.1
- **Auto-discovery**: Reads Docker labels for routing
- **Zero config**: Services self-register
- **HTTPS**: Let's Encrypt integration (optional)

#### Dashboard: Homepage
- **Auto-discovery**: Shows all services from Docker labels
- **Koan-aware**: Displays framework version, health status
- **Customizable**: Users can add non-Koan services

#### Registry: Harbor (Lightweight Mode)
- **Why**: Better than bare `registry:3` - UI, webhooks, security
- **Lightweight**: Core registry + UI only, no scanning/replication
- **Alternative**: Plain `registry:3` for extreme resource constraints

#### Discovery: Avahi (mDNS/DNS-SD)
- **Why**: Zero-config networking (`koan-lab-01.local`)
- **Fallback**: Static IP display on first boot

---

## Framework Changes Required (Koan Repo)

### 1. Service Metadata Endpoint

**File:** `src/Koan.Web/Controllers/WellKnownController.cs`

Add `GET /.well-known/koan-service` endpoint:

```csharp
[HttpGet("koan-service")]
public IActionResult KoanService()
{
    var assembly = Assembly.GetEntryAssembly();
    var frameworkVersion = assembly?.GetCustomAttribute<KoanFrameworkVersionAttribute>()?.Version;
    
    return Ok(new
    {
        name = env.ApplicationName,
        version = assembly?.GetName().Version?.ToString() ?? "unknown",
        frameworkVersion = frameworkVersion ?? "unknown",
        health = new { live = "/api/health/live", ready = "/api/health/ready" },
        capabilities = GetServiceCapabilities(),
        routes = GetDiscoverableRoutes()
    });
}
```

**ADR Required:** `WEB-0065-service-metadata-endpoint.md`

### 2. Remote Deployment Target API

**File:** `src/Koan.Orchestration.Cli/Commands/TargetCommand.cs` (new)

Add `koan target add <url>` and `koan deploy --target <name>`:

```csharp
public class TargetCommand : ICommand
{
    public string Name => "target";
    public string Description => "Manage remote deployment targets";
    
    public Task<int> ExecuteAsync(string[] args, CancellationToken ct)
    {
        // Subcommands: add, remove, list, test
        // Stores targets in ~/.koan/targets.json
        // SSH-based deployment using Docker context or direct push
    }
}
```

**ADR Required:** `ORCH-0010-remote-deployment-targets.md`

### 3. Health Aggregation Endpoint

**File:** `src/Koan.Web/Controllers/HealthController.cs` (existing, enhance)

Add `GET /api/health/summary` for dashboard integration:

```csharp
[HttpGet("summary")]
public async Task<IActionResult> Summary(CancellationToken ct)
{
    var contributors = sp.GetServices<IHealthContributor>();
    var reports = await Task.WhenAll(contributors.Select(c => c.CheckAsync(ct)));
    
    return Ok(new
    {
        overall = reports.All(r => r.IsHealthy) ? "healthy" : "degraded",
        components = reports.Select(r => new { r.Name, r.IsHealthy, r.Message }),
        timestamp = DateTime.UtcNow
    });
}
```

**ADR Required:** `WEB-0066-health-aggregation-endpoint.md`

---

## KoanBox Project Structure (Separate Repo)

```
koanbox/                           # Root of companion project
├── README.md                      # "What is KoanBox?"
├── LICENSE                        # Apache 2.0 (same as Koan)
├── CONTRIBUTING.md
│
├── installer/                     # USB creation tools
│   ├── koanbox-install.ps1        # Windows installer
│   ├── koanbox-install.sh         # macOS/Linux installer
│   ├── cloud-init/                # Ubuntu auto-install configs
│   │   ├── user-data              # Primary install script
│   │   └── meta-data
│   ├── scripts/
│   │   ├── 01-base-setup.sh       # Docker, SSH, mDNS
│   │   ├── 02-infra-stack.sh      # Traefik, Homepage, Harbor
│   │   └── 03-finalize.sh         # LED patterns, ready signal
│   └── assets/
│       └── ubuntu-24.04-koanbox.iso  # Pre-patched ISO (optional)
│
├── cli/                           # Deployment CLI for dev machines
│   ├── koanbox-cli/               # .NET tool
│   │   ├── Commands/
│   │   │   ├── DiscoverCommand.cs # Find KoanBox on network
│   │   │   ├── DeployCommand.cs   # Build + push + start
│   │   │   └── StatusCommand.cs   # Show services
│   │   └── koanbox-cli.csproj
│   └── publish/                   # Pre-built binaries
│
├── stack/                         # Infrastructure compose files
│   ├── traefik/
│   │   ├── docker-compose.yml
│   │   └── traefik.yml            # Static config
│   ├── homepage/
│   │   ├── docker-compose.yml
│   │   └── services.yml           # Koan service discovery
│   └── harbor/
│       └── docker-compose.yml     # Lightweight mode
│
├── docs/                          # Tutorial-style docs
│   ├── getting-started.md         # Complete walkthrough
│   ├── hardware-guide.md          # What to buy, BIOS settings
│   ├── usb-creation.md            # Step-by-step USB prep
│   ├── deployment-guide.md        # Using koanbox deploy
│   ├── troubleshooting.md         # Common issues
│   └── advanced/
│       ├── custom-labels.md       # Traefik label patterns
│       ├── https-setup.md         # Let's Encrypt config
│       └── multi-box.md           # Running multiple boxes
│
├── tests/                         # Integration tests
│   └── hardware-validation/       # Scripts for testing hardware
│
└── samples/                       # Example Koan apps with labels
    ├── hello-koanbox/
    │   ├── Dockerfile
    │   └── docker-compose.yml     # Traefik + Homepage labels
    └── multi-service/
```

---

## Cognitive Load Reduction Strategies

### Principle: "No Decisions After Download"

| Decision Point | Traditional Approach | KoanBox Approach |
|----------------|---------------------|------------------|
| **What Linux distro?** | Research, compare | Pre-selected: Ubuntu 24.04 LTS |
| **Manual or automated install?** | Choose installer type | Always unattended |
| **Partition layout?** | Manual or guided | Pre-configured: single ext4 |
| **Network config?** | Static vs DHCP | DHCP + mDNS (auto-discover) |
| **Docker installation?** | Follow Docker docs | Pre-installed, pre-configured |
| **Firewall rules?** | ufw, iptables | Pre-configured for Koan |
| **Service discovery?** | Manually configure | Traefik labels (auto) |
| **Dashboard setup?** | Install, configure | Homepage (pre-configured) |
| **Image registry?** | DockerHub? Local? | Harbor on box (local-first) |
| **Deployment method?** | scp? CI/CD? | `koanbox deploy` |

### Automation Wins

1. **BIOS Boot Order**: USB installer detects common BIOS patterns, displays key-press hint
2. **Network Discovery**: mDNS + SSDP + manual IP fallback (covers 99% of home networks)
3. **SSH Key Management**: Generated during USB creation, auto-configured on box
4. **Service Labeling**: `koanbox deploy` auto-adds standard labels if missing
5. **Port Conflicts**: Traefik handles all routing, no exposed ports except 80/443
6. **Updates**: Systemd timer for security patches, preserves user data

### Escape Hatches (When Things Go Wrong)

1. **LED Patterns**: Visual feedback without monitor (blink codes for common errors)
2. **Recovery Mode**: Hold power button 10 seconds during boot → factory reset
3. **Web Terminal**: Cockpit pre-installed (http://koan-lab-01.local:9090)
4. **Serial Console**: USB-to-serial adapter instructions (last resort)

---

## Implementation Phases

### Phase 0: Framework Preparation (2 weeks)

**Koan Repository Changes** (follow all ADR/engineering rules):

- [ ] Create `WEB-0065-service-metadata-endpoint.md`
- [ ] Implement `GET /.well-known/koan-service` in `WellKnownController`
- [ ] Create `WEB-0066-health-aggregation-endpoint.md`
- [ ] Enhance `HealthController` with `/api/health/summary`
- [ ] Create `ORCH-0010-remote-deployment-targets.md`
- [ ] Extend `Koan.Orchestration.Cli` with `target` command
- [ ] Add tests, update TECHNICAL.md files
- [ ] Merge via PR with full review

### Phase 1: Foundation (3 weeks)

**KoanBox Repository Setup** (separate repo):

- [ ] Create GitHub repo: `koanbox/koanbox`
- [ ] Set up CI/CD (GitHub Actions)
- [ ] Write USB installer scripts (Windows + macOS/Linux)
- [ ] Create cloud-init configs for unattended install
- [ ] Test on 3 hardware models (Dell, HP, Lenovo)
- [ ] Document hardware compatibility matrix

### Phase 2: Infrastructure Stack (2 weeks)

- [ ] Configure Traefik with Docker provider
- [ ] Set up Homepage with Koan service discovery
- [ ] Deploy Harbor in lightweight mode
- [ ] Create base compose files
- [ ] Test end-to-end flow (USB → running service)

### Phase 3: Deployment CLI (2 weeks)

- [ ] Implement `koanbox discover` (mDNS + SSDP)
- [ ] Implement `koanbox deploy` (build + push + start)
- [ ] Implement `koanbox status` (show services)
- [ ] Package as .NET tool (`dotnet tool install -g koanbox-cli`)
- [ ] Cross-platform testing (Windows, macOS, Linux)

### Phase 4: Documentation (2 weeks)

- [ ] Getting started guide (complete walkthrough)
- [ ] Hardware selection guide (what to buy)
- [ ] Video tutorial (USB creation to deployed app)
- [ ] Troubleshooting runbook
- [ ] Advanced guides (HTTPS, multi-box, etc.)

### Phase 5: Beta Testing (2 weeks)

- [ ] Recruit 10 beta testers (3 beginners, 4 intermediate, 3 advanced)
- [ ] Collect feedback, iterate
- [ ] Fix top 5 pain points
- [ ] Prepare launch materials

### Phase 6: Launch (1 week)

- [ ] Create landing page (koanbox.io)
- [ ] Announce on Koan channels
- [ ] Submit to Hacker News, Reddit r/selfhosted
- [ ] Monitor support channels, rapid-fix critical issues

---

## Success Metrics

### Quantitative (v1.0 Release)

- [ ] **Setup time**: <60 minutes (USB creation + hardware boot + first deployment)
- [ ] **User time**: <10 minutes of active work (rest is automated)
- [ ] **Hardware compatibility**: 90%+ of refurbs from 2012+ boot successfully
- [ ] **Error rate**: <5% of installations require troubleshooting
- [ ] **Documentation completeness**: 95%+ of support questions answered by docs

### Qualitative

- [ ] **Beginner success**: Non-technical users can complete setup
- [ ] **No terminal required**: After USB boot, zero manual commands
- [ ] **Delight factor**: "This just works!" reactions
- [ ] **Community adoption**: 3+ community-contributed hardware profiles

---

## Risk Assessment

| Risk | Probability | Impact | Mitigation |
|------|-------------|--------|-----------|
| **BIOS boot variance** | High | Medium | Test 20+ models, provide boot key database |
| **Network discovery fails** | Medium | High | Multi-method discovery (mDNS, SSDP, manual IP) |
| **Hardware incompatibility** | Medium | Medium | Maintain compatibility database, recovery USB |
| **Storage too small** | Low | Low | Require 32GB min, warn if <64GB |
| **Ubuntu 24.04 regressions** | Low | High | Pin to specific point release, test upgrades |
| **Beginner overwhelm** | Medium | High | Video tutorial, LED feedback, escape hatches |

---

## Alternative Approaches Considered

### Podman Instead of Docker

**Pros:** Daemonless, rootless, more secure
**Cons:** Less mature tooling, Koan's orchestration built for Docker
**Decision:** Start with Docker, add Podman support in v2.0

### NixOS Instead of Ubuntu

**Pros:** Declarative, immutable, perfect for appliances
**Cons:** Steep learning curve for contributors, smaller community
**Decision:** Consider for v2.0 "advanced mode"

### Portainer Instead of Homepage

**Pros:** Full Docker management UI
**Cons:** Heavier, not Koan-aware, license complexity
**Decision:** Homepage for v1.0, Portainer as optional add-on

### Cloud Image Instead of USB

**Pros:** No hardware required
**Cons:** Defeats "free/cheap refurb" value prop, recurring costs
**Decision:** Not the primary use case, but cloud-init configs work on VPS

---

## Long-Term Vision

### Satellite v1.0 (Current Proposal)

- **Single satellite deployment**: One app → one satellite
- **Constellation discovery**: Auto-detect all satellites on network
- **Zero-config deployment**: `koan push --satellite` auto-routes

### Satellite v2.0 (Future: Multi-Satellite Orchestration)

**Segmented, Multi-Box Deploys:**
- **Service distribution**: Deploy microservices across constellation (API → satellite-alpha, DB → satellite-bravo)
- **Load balancing**: Horizontal scaling across satellites (3 API instances on 3 boxes)
- **Concern segregation**: Separate satellites for databases, APIs, frontends, background workers
- **Geo-simulation**: Test multi-region deployments on local hardware
- **Affinity rules**: "Deploy this service on satellites with GPU/SSD/high-RAM"

**Architectural Considerations (Design Now, Build Later):**
- Service discovery must support multi-satellite routing
- Health checks aggregate across constellation
- Deployment manifests can specify placement strategies
- Dashboard shows topology view (which services on which satellites)

**Example Future Workflow:**
```powershell
# Deploy microservices across 3 satellites
koan push --constellation production \
  --placement api=satellite-alpha \
  --placement db=satellite-bravo \
  --placement worker=satellite-gamma

# Or let orchestrator decide
koan push --constellation production --auto-place
```

### Satellite v3.0 (Further Future)

- **Auto-scaling**: Spin up services based on load
- **Backup/restore**: One-click snapshots across constellation
- **Hardware monitoring**: Temperature, disk health, fan speed
- **Power management**: Wake-on-LAN, scheduled shutdown
- **NixOS variant**: Declarative, immutable infrastructure

### Ecosystem Growth

- **Community hardware profiles**: Database of tested models
- **Satellite marketplace**: Pre-configured hardware vendors
- **Education partnerships**: University computer labs
- **Edge deployment**: IoT, remote offices, retail

---

## Architectural Review & Design Decisions

### **Systems Integration Assessment: 9/10 - Strong Recommend**

After comprehensive technical review, this proposal demonstrates mature operational thinking and sound architectural choices. Key findings:

### **Strategic Strengths**

#### **1. Market Positioning (Exceptional)**
- **Unserved niche**: Local dev environments with zero cloud costs
- **Hardware arbitrage**: $30-50 refurbs vs $5-50/month cloud = ROI in 1-6 months
- **Timing advantage**: Windows 11 incompatibility creating hardware surplus
- **Unique differentiation**: "Constellation" concept, not just "another Docker host"

#### **2. Technical Foundation (Solid)**
- **Ubuntu installer**: Leverages Canonical's battle-tested compatibility (thousands of configs)
- **Cloud-init automation**: Production-grade (AWS/Azure use it)
- **Install-to-disk model**: Avoids live USB fragility, standard boot path
- **Proven stack**: Docker + Traefik + plain registry = boring tech (good!)
- **Modern UX**: Pairing codes solve auth/DX elegantly

#### **3. Scope Discipline (Excellent)**
- **v1.0 focus**: Single satellite deployment, proven patterns
- **v2.0 vision**: Multi-satellite orchestration documented but not blocking
- **Clear boundaries**: Framework changes minimal (3 ADRs), main work separate
- **Escape hatches**: Manual SSH fallback, standard Docker commands work

---

### **Key Design Decisions**

#### **Security Model: Pairing Code Authentication**

**Lab Mode:**
```powershell
# No pairing needed, auto-discovered
# Network detection blocks remote access
# Clear warnings: "Lab Mode - Not Internet Safe"
```

**Balanced/Hardened Mode:**
```powershell
# 6-letter pairing code displayed on satellite console/dashboard
# Format: XJ8M4P (alphanumeric, no 0/O/1/I/l confusion)
# One-time use OR 10-minute expiry
# After pairing: SSH key/TLS cert exchange
# Stored in ~/.koan/satellites/{name}/
```

**Security Boundaries:**
- Network detection prevents remote deployment without explicit flags
- Firewall rules (ufw): SSH, HTTP, HTTPS, Registry from local subnet only
- No Docker API exposure (SSH tunnel only)
- Cockpit optional (disabled by default)

**Example Flow:**
```powershell
# Satellite displays on console:
Pairing Code: XJ8M4P (expires in 5 min)

# Dev machine:
PS> koan satellite add gentle-sunrise
🔐 Enter pairing code: XJ8M4P
✓ Code verified
✓ Credentials exchanged
✓ Satellite added

# Future deployments (no code needed):
PS> koan push --satellite gentle-sunrise
✓ Authenticated via stored credential
```

---

#### **Fleet Operations: Resilience-First Design**

**Philosophy: "Cattle, Not Pets"**

Satellites are designed for commodity refurbished hardware with expected failure rates of 10-20%. The system embraces partial failure as normal operations.

**Update Strategy: Best-Effort, Continue on Failure**

```powershell
koan satellite update all

# Updates all satellites serially
# Continues despite individual failures
# Reports success rate at end

Update Summary: 9/10 successful ✓
Constellation Status: Operational (90% on latest)

Satellites:
  ✓ satellite-01 through 05  (v1.1.0) - Healthy
  ⚠️ satellite-06             (v1.0.0) - UPDATE FAILED
  ✓ satellite-07 through 10  (v1.1.0) - Healthy

🔍 Attention Required: 1 satellite
satellite-06 failed: Disk I/O error
  Possible cause: Failing storage
  Action: koan satellite ssh 06 "sudo smartctl -a /dev/sda"
  
Apps on satellite-06 continue running on v1.0.0
```

**Key Principles:**
1. **Best-effort execution**: Continue updating remaining satellites even if one fails
2. **Preserve availability**: Failed update = satellite stays operational on old version
3. **Clear status reporting**: Success rate, color-coded health, actionable next steps
4. **Targeted intervention**: Notification identifies specific machine needing attention

**Why This Matters:**
- **10 refurb machines @ $40 = $400 total**
- **Expected: 1-2 failures during updates**
- **9/10 success = operational system, not failure**
- **Replacement cost: $40 (vs manual intervention × 10)**

**Availability Math:**
```
Scenario: Update 10 satellites, #6 fails

Result: 9 updated (90%) ✓
        1 failed (10%) ⚠️ (still running old version)
        
Constellation: 90% updated, 100% operational
Apps: Available throughout update process
```

---

#### **Economic Model: Refurb Hardware Arbitrage**

**10-Satellite Constellation Cost Comparison:**

| Approach | Hardware | Electricity | Replacements | 5-Year Total |
|----------|----------|-------------|--------------|--------------|
| **Cloud VPS** | $0 | $0 | $0 | $6,000 |
| **Satellite** | $400 | $100 | $80 | $580 |
| **Savings** | - | - | - | **$5,420 (91%)** |

**Assumptions:**
- Cloud: 10 × $10/month VPS (2GB RAM, 2 CPU, 60GB)
- Satellite: 10 × $40 refurb (4GB RAM, i5, 128GB SSD)
- Electricity: 10W per machine × 10 × 24/7 × $0.12/kWh = $20/year
- Expected failures: 2 machines over 5 years (20%)

**Even with 50% failure rate (5 replacements):**
- Hardware: $400 + $200 = $600
- Total 5-year: $700
- Still saves **$5,300 (88%)**

**This economic model makes "9/10 works" = success.**

---

### **Storage Requirements (Revised)**

**Original:** 32GB minimum  
**Revised:** 64GB minimum, 128GB recommended

**Reality Check (32GB drive):**
```
Ubuntu Server:        5 GB
Docker + images:      3 GB
Registry:             0.5 GB
Traefik + Homepage:   0.3 GB
System reserved:      2 GB
─────────────────────────
Base install:        10.8 GB
Available:           21.2 GB

After deploying PostgreSQL app:
PostgreSQL image:     0.3 GB
.NET runtime image:   0.8 GB
App image:            0.5 GB
PostgreSQL data:     10 GB (grows over time)
App logs:             2 GB
Docker build cache:   3 GB
─────────────────────────
Used:                27.4 GB
Remaining:            4.6 GB (14% free) ⚠️
```

**Problem:** Docker doesn't fail gracefully when disk is full. Corruption risk.

**Recommendation:**
- **Minimum: 64GB** (comfortable for 1-2 apps + database)
- **Recommended: 128GB** (comfortable for 3-5 apps + data)
- **Detection:** During USB prep, warn if <64GB
- **Documentation:** "32GB works for single lightweight service (expert mode)"

---

### **Hardware Compatibility Strategy**

**Install-to-Disk Model (Not Live USB):**
```
Boot sequence (one-time):
USB → Ubuntu installer → Formats internal HDD → Installs to HDD → Reboot
                                                                   ↓
                                                      Normal HDD boot (forever)
```

**Why This Works:**
- Leverages Ubuntu Server installer (battle-tested on thousands of configs)
- Auto-detects UEFI vs Legacy BIOS
- Handles Secure Boot with signed bootloader
- After install, USB removed (no ongoing compatibility concerns)

**BIOS Boot Variance (Mitigated):**
- User only navigates BIOS once (one-time boot from USB)
- Provide brand-specific guides: Dell (F12), HP (F9), Lenovo (F12)
- Hardware compatibility database: Pre-flight check before USB creation
- Focus on 5-10 "Tier 1" models initially (Dell OptiPlex, HP EliteDesk, Lenovo ThinkCentre)

---

### **Network Discovery (Multi-Layer)**

**Primary:** mDNS/Avahi (`.local` domain, zero-config)  
**Fallback:** IP display on physical console

```bash
╔════════════════════════════════════╗
║   🛰️  Satellite Ready!             ║
║                                    ║
║   Name: satellite-gentle-sunrise  ║
║   IP:   192.168.1.42               ║
║   URL:  http://192.168.1.42        ║
║                                    ║
║   📋 Pairing Code: XJ8M4P          ║
║      (expires in 5 minutes)        ║
║                                    ║
║   koan satellite add gentle-sunrise
╚════════════════════════════════════╝
```

**Why Both:**
- mDNS works great on home networks (95% coverage)
- Corporate networks often block mDNS (VLANs, Pi-hole, firewalls)
- IP display on console = universal fallback
- Display duration: 60 seconds after boot, then on-demand

**Cost:** 10 lines of bash, eliminates "I can't find my satellite" support tickets

---

### **Docker Remote Access Method**

**Chosen: SSH + Docker Socket**

```powershell
koan push --satellite alpha

# Behind the scenes:
1. SSH tunnel using paired credential
2. Access /var/run/docker.sock via tunnel
3. Standard Docker commands over SSH
```

**Why This Approach:**
- ✅ Simple: Reuses SSH authentication (no TLS cert management)
- ✅ Secure: No exposed Docker API port
- ✅ Standard: Uses native Docker socket
- ✅ Familiar: Developers understand SSH

**Alternatives Rejected:**
- ❌ Docker Context (port 2376): TLS certificate complexity
- ❌ Custom API: Build/maintain overhead

---

### **Upgrade Path**

**v1.0: `koan satellite upgrade` Command**

```powershell
koan satellite upgrade <name>
# SSH in, run update script
# - apt update && apt upgrade
# - docker-compose pull
# - Restart services
# - Health check
# - Rollback on failure

koan satellite update all
# Updates all satellites, best-effort
# Continues despite failures
# Reports summary at end
```

**v2.0: Advanced Strategies**
- Rolling updates (canary pattern)
- Parallel updates (with confirmation)
- Rollback capability
- Scheduled maintenance windows

---

### **Risk Assessment & Mitigation**

| Risk | Probability | Impact | Mitigation |
|------|-------------|--------|-----------|
| **BIOS boot variance** | Low | Low | Ubuntu installer maturity, brand-specific docs |
| **Network discovery fails** | Medium | Low | IP display on console (60 sec) |
| **Disk space exhaustion** | Medium | High | 64GB minimum, warnings during setup |
| **Hardware failure** | High | Low | Expected with refurbs, fleet operations model |
| **Update failures** | Medium | Low | Best-effort continues, targeted intervention |
| **Security exposure** | Low | High | Network detection, pairing codes, firewall rules |

**Overall Risk Level:** Low to Medium (well-mitigated)

---

### **Implementation Priorities (v1.0)**

**Must-Have:**
- [x] Pairing code authentication system
- [x] Network detection (block remote access)
- [x] Best-effort update strategy
- [x] USB installer script (Windows PowerShell)
- [x] Cloud-init unattended install
- [x] SSH tunnel for Docker remote access
- [x] Basic documentation (getting started, troubleshooting)

**Strongly Recommended:**
- [ ] Hardware compatibility database + pre-flight check
- [ ] IP display on console (60 sec after boot)
- [ ] Raise minimum storage to 64GB
- [ ] Fleet status command: `koan satellite status`
- [ ] Firewall rules (ufw) configuration
- [ ] 5 hardware models tested (Tier 1)

**Nice-to-Have (v1.0 or v1.1):**
- [ ] QR code pairing
- [ ] LED status codes (case LEDs)
- [ ] USB health check
- [ ] Slack/Discord notifications
- [ ] Web dashboard visualizer

---

### **Success Criteria**

**Quantitative (v1.0 Launch):**
- Setup time: <60 minutes total (USB creation + auto-install + first deployment)
- User time: <10 minutes of active work
- Hardware compatibility: 5 "Tier 1" models tested, 90%+ success rate
- Update success rate: 80%+ satellites update successfully in fleet scenarios
- Security: Zero-auth mode requires explicit local network detection

**Qualitative:**
- Beginner success: Non-technical users complete setup
- Fleet operations: "9/10 works" perceived as success, not failure
- Community adoption: 3+ community hardware profiles within 3 months
- Support efficiency: <5% of installations require manual intervention

---

### **Architect Sign-Off**

**Verdict: Approved for v1.0 Development (9/10)**

This is a **well-architected, pragmatically-scoped project** with clear value proposition and thoughtful execution strategy. After detailed technical review:

**Key Strengths:**
- ✅ Solid technical foundation (Ubuntu installer, proven stack)
- ✅ Modern UX patterns (pairing codes, auto-discovery, tap menus)
- ✅ Secure by default (network detection, minimal exposure)
- ✅ Resilience-first design (embraces partial failure)
- ✅ Economic viability (91% cost savings vs cloud)

**Why 9/10 not 10/10:**
- Storage requirements need adjustment (64GB minimum)
- Hardware compatibility database would reduce risk
- IP console display is low-effort, high-value addition

**These are minor adjustments, not blocking issues.**

**Timeline Estimate:** 11 weeks to v1.0 launch (1 developer)

**Recommendation:** Proceed with development. This has strong potential to become the canonical way to deploy Koan locally.

---

## Next Steps (If Approved)

1. **Create minimal ADRs for framework changes**
   - WEB-0065, WEB-0066, ORCH-0010
   - Follow Koan engineering guardrails

2. **Prototype on one hardware model**
   - Dell OptiPlex 7010 (cheap, common, well-supported)
   - End-to-end: USB creation → boot → deploy S1.Web

3. **Validate with 2-3 beta testers**
   - One beginner, one intermediate, one advanced
   - Collect detailed feedback

4. **Create GitHub repo**
   - `koan/satellite` (under koan org)
   - Apache 2.0 license (consistent with Koan)

5. **Announce intent**
   - Blog post on Koan site
   - Solicit early adopters

---

## Questions for Review

1. **Scope creep concern?** Is this too much for a companion project?
2. **Maintenance burden?** Who owns KoanBox long-term?
3. **Branding?** Should it be "KoanBox" or "Koan Companion Host" or something else?
4. **Hardware support?** Should we partner with a refurb vendor for "official" hardware?
5. **Cloud variant?** Should we provide DigitalOcean/AWS one-click images?

---

## ⌨️ **Tap-Based Interaction System**

### **Design Philosophy**

Modern installer UX should feel **responsive and immediate**:
- **Tap = Execute** (no Enter confirmation)
- **Instant feedback** (< 100ms response)
- **Progressive regeneration** (tap multiple times)
- **Clear visual state** (show what's selected)
- **Undo-friendly** (can go back)

### **Implementation Notes**

**Bootstrap Constraint:**
Installer runs from USB on bare metal (no runtime installed yet). Must use:
- **Windows**: PowerShell (built-in, no dependencies)
- **Linux**: Bash (universally available)

**Tap Input Pattern:**
- Platform-native single-keypress capture (no Enter confirmation)
- PowerShell: `$host.UI.RawUI.ReadKey("NoEcho,IncludeKeyDown")`
- Bash: `read -n1 -s` with IFS handling

**Regeneration Loop:**
- Poll for keypress in tight loop
- On '2' pressed: generate name + DNS check (< 1 sec target)
- Redraw menu with new name instantly
- Continue loop until user accepts ('1') or cancels (Esc)
            }
        }
    }
}
```

#### **Countdown Timer with Tap Interrupt**

```csharp
// src/Haven.Installer/UI/CountdownTimer.cs

public class CountdownTimer
{
    public async Task<bool> WaitWithOption(
        int seconds, 
        Action<int> onTick,
        CancellationToken ct = default)
    {
        var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        
        // Task 1: Countdown
        var countdownTask = Task.Run(async () =>
        {
            for (int i = seconds; i > 0; i--)
            {
                if (cts.Token.IsCancellationRequested)
                    return false;
                    
                onTick(i);
                await Task.Delay(1000, cts.Token);
            }
            return true; // Completed
        }, cts.Token);
        
        // Task 2: Key listener (tap = instant interrupt)
        var keyTask = Task.Run(() =>
        {
            Console.ReadKey(intercept: true);
            cts.Cancel(); // Stop countdown immediately
            return false; // Interrupted
        });
        
        // Race: countdown vs keypress
        var result = await Task.WhenAny(countdownTask, keyTask);
        cts.Cancel(); // Stop other task
        
        return await result; // true = auto-install, false = customize
    }
}
```

#### **Progress Bar with Live Update**

```csharp
// src/Haven.Installer/UI/ProgressBar.cs

public class ProgressBar
{
    public void Render(int current, int total, int width = 30)
    {
        var percent = (double)current / total;
        var filled = (int)(percent * width);
        
        var bar = new string('█', filled) + new string('░', width - filled);
        
        // Use \r to overwrite same line (no flicker)
        Console.Write($"\r[{bar}] {current}s   ");
    }
}
```

### **Interaction Patterns**

#### **Pattern 1: Tap to Regenerate (Infinite Loop)**

```
User Flow:
1. Menu shows current name
2. User taps '2' → Instant new name (< 1 sec)
3. User taps '2' again → Another new name
4. User taps '2' 10 more times → 10 new names
5. User taps '1' → Accept current name

No limit on regeneration attempts
```

#### **Pattern 2: Tap to Select (Instant Commit)**

```
User Flow:
1. Menu shows 3 security modes
2. User taps '2' → Instant selection
3. Brief confirmation ("✓ Mode changed")
4. Auto-return to main menu (no extra key)

Single tap = decision + navigation
```

#### **Pattern 3: Tap to Navigate (Menu Drill-Down)**

```
User Flow:
1. Main menu shows options [N, M, O, A]
2. User taps 'N' → Instant navigation to Box Name
3. User taps '2' → Regenerate
4. User taps '1' → Accept + auto-return
5. User taps Enter → Start installation

No "back" buttons needed (Esc anywhere)
```

### **Performance Targets**

| Action | Target Latency | Acceptable Max |
|--------|---------------|----------------|
| **Tap recognition** | < 50ms | 100ms |
| **Name generation** | < 500ms | 1000ms |
| **DNS check** | < 300ms | 800ms |
| **Menu render** | < 100ms | 200ms |
| **Screen transition** | < 200ms | 500ms |

**Total regeneration cycle (tap '2'):** < 1 second

### **Accessibility Considerations**

```csharp
// Support both number row and numpad
case ConsoleKey.D1 or ConsoleKey.NumPad1:
    // Same action
    
// Support both Esc and Ctrl+C
case ConsoleKey.Escape:
case ConsoleKey.C when Console.KeyAvailable && Console.ReadKey(true).Modifiers == ConsoleModifiers.Control:
    // Cancel
    
// Visual feedback for every tap
Console.Beep(800, 50); // Optional: brief tone
Console.Write("✓");     // Visual confirmation
```

### **Error Handling**

```csharp
// If DNS check fails during regeneration
try
{
    currentName = await generator.GenerateNames(NamingPattern.Random, 1)[0];
}
catch (Exception ex)
{
    Console.WriteLine("\n⚠️  Network check unavailable");
    Console.WriteLine("    Generated name (unchecked)");
    // Continue anyway with unchecked name
}
```

---

## ⏱️ **30-Second Auto-Install with Tap-Based Customization**

### **Zero-Friction Default Path**

The installer auto-proceeds after 30 seconds, allowing power users to customize while providing a "just works" experience for others.

```powershell
.\companion-box-install.ps1 -UsbDrive "E:"

# ═══════════════════════════════════════════════════
# 🔍 Analyzing environment...
# ✓ Detected: Windows 11, Home network
# ✓ Generated: companion-gentle-sunrise
# ✓ Mode: Lab (recommended for solo dev)
# ═══════════════════════════════════════════════════

┌────────────────────────────────────────────────────┐
│ 🎯 Companion Box Configuration                     │
│                                                     │
│ Name:    companion-gentle-sunrise                  │
│ Mode:    Lab (no authentication)                   │
│ Network: Home (192.168.1.x only)                   │
│ Storage: 64GB minimum, auto-partition              │
│                                                     │
│ ⏱️  Auto-installing in 27 seconds...                │
│                                                     │
│ Tap any key to customize (no Enter needed)        │
│ Press Ctrl+C to cancel                             │
│                                                     │
│ [████████████████░░░░░░░░] 27s                     │
└────────────────────────────────────────────────────┘
```

**Design Benefits:**
- ✅ **Zero friction**: Most users do nothing, get perfect defaults
- ✅ **Clear escape**: "Tap any key" = instant customization
- ✅ **Visual progress**: Countdown + progress bar
- ✅ **Confidence-building**: Shows exactly what will happen
- ✅ **Familiar pattern**: Like GRUB, server installers

---

### **Tap-Based Customization Menu**

```powershell
# User tapped any key at 18 seconds remaining

┌────────────────────────────────────────────────────┐
│ 🔧 Customize Installation                          │
│                                                     │
│ Tap a key (no Enter needed)                       │
│                                                     │
│ [N] Box Name                                       │
│     companion-gentle-sunrise                       │
│                                                     │
│ [M] Security Mode                                  │
│     Lab (no auth, local network)                   │
│                                                     │
│ [O] Observability Stack                            │
│     Disabled (lighter, faster)                     │
│                                                     │
│ [A] Advanced Options                               │
│     (Network, storage, updates)                    │
│                                                     │
│ [Enter] Start Installation                         │
│ [Esc]   Cancel                                     │
└────────────────────────────────────────────────────┘
```

**Interaction Model:**
- **Tap = Execute**: Single keypress (no Enter confirmation)
- **Instant feedback**: < 100ms response time
- **Clear navigation**: Letter shortcuts for all options
- **Commit with Enter**: Final confirmation before install

---

### **[N] Box Name - Tap to Regenerate**

```powershell
# User tapped 'N'

┌────────────────────────────────────────────────────┐
│ 📝 Box Name                                        │
│                                                     │
│ Current: companion-gentle-sunrise                  │
│                                                     │
│ [1] Keep current (go back)                        │
│ [2] Regenerate (tap repeatedly for more)          │
│ [3] Serial pattern (companion-box-0001)            │
│ [4] Custom name                                    │
│                                                     │
│ Tap a key (no Enter needed)                       │
└────────────────────────────────────────────────────┘

# User taps '2' → INSTANT regeneration

🔍 Generating names...
✓ Checking DNS...

┌────────────────────────────────────────────────────┐
│ 📝 Box Name                                        │
│                                                     │
│ New: companion-amber-cascade ✓ Available          │
│                                                     │
│ [1] Use this name                                  │
│ [2] Regenerate again (tap for more)               │
│ [3] Serial pattern                                 │
│ [4] Custom name                                    │
│                                                     │
│ Can tap '2' as many times as you like!            │
└────────────────────────────────────────────────────┘

# User taps '2' again → INSTANT new generation

🔍 Generating names...
✓ Checking DNS...

┌────────────────────────────────────────────────────┐
│ 📝 Box Name                                        │
│                                                     │
│ New: companion-crystal-nexus ✓ Available          │
│                                                     │
│ [1] Use this name                                  │
│ [2] Keep regenerating (tap for more)              │
│ [3] Serial pattern                                 │
│ [4] Custom name                                    │
│                                                     │
│ Tap '1' to accept, '2' for more names             │
└────────────────────────────────────────────────────┘

# Tapping '1' immediately accepts and returns to main menu
```

**Regeneration Flow:**
- **Tap '2' infinite times**: Each tap = new name + DNS check (< 1 sec)
- **No waiting**: Instant visual feedback
- **Clear availability**: "✓ Available" badge on each name
- **Easy acceptance**: Tap '1' to commit
- **Escape anytime**: Esc returns without changes

---

### **[M] Security Mode - Tap to Select**

```powershell
# User tapped 'M'

┌────────────────────────────────────────────────────┐
│ 🔒 Security Mode                                   │
│                                                     │
│ [1] Lab (solo dev, no auth) ⭐ Current             │
│     • Fastest setup                                │
│     • No passwords/tokens                          │
│     • Local network only                           │
│                                                     │
│ [2] Balanced (team, token auth)                    │
│     • Shared token for team                        │
│     • Self-signed HTTPS                            │
│     • Audit logging                                │
│                                                     │
│ [3] Hardened (production, certificates)            │
│     • Per-user certificates                        │
│     • Let's Encrypt HTTPS                          │
│     • Full compliance mode                         │
│                                                     │
│ Tap 1, 2, or 3 (no Enter needed)                  │
└────────────────────────────────────────────────────┘

# User taps '2' → INSTANT selection, returns to main menu

✓ Mode changed to: Balanced
✓ Token will be generated during installation

[Returns to customization menu automatically]
```

**Selection Flow:**
- **Tap number**: Instant selection + auto-return
- **No confirmation**: Single tap commits choice
- **Visual feedback**: "✓ Mode changed" message
- **Undo-friendly**: Can re-enter menu to change

---

### **Implementation Notes**

**Bootstrap Constraint:**
Installer runs from USB on bare metal (no runtime installed yet). Must use:
- **Windows**: PowerShell (built-in, no dependencies)
- **Linux**: Bash (universally available)

**Tap Input Pattern:**
- Use platform-native single-keypress capture (no Enter confirmation)
- PowerShell: `$host.UI.RawUI.ReadKey("NoEcho,IncludeKeyDown")`
- Bash: `read -n1 -s` with IFS handling

**Regeneration Loop:**
- Poll for keypress in tight loop
- On '2' pressed: generate name + DNS check (< 1 sec target)
- Redraw menu with new name instantly
- Continue loop until user accepts ('1') or cancels (Esc)

#### **Performance Targets**

| Action | Target | Max |
|--------|--------|-----|
| Tap recognition | < 50ms | 100ms |
| Name generation | < 500ms | 1s |
| DNS check | < 300ms | 800ms |
| Screen render | < 100ms | 200ms |

**Total regeneration cycle:** < 1 second per tap

---

## 🏷️ **Box Naming System (Memorable + Collision-Free)**

### **Design Philosophy**

Box names should be:
- **Memorable**: Easy to say, type, and remember (not GUIDs)
- **Unique**: DNS collision checking prevents conflicts
- **Flexible**: Multiple patterns for different use cases
- **Low-friction**: Smart defaults, optional customization

### **Naming Patterns**

#### **Pattern 1: Random Words (Default - Recommended)**

```bash
.\koanbox-install.ps1 -UsbDrive "E:"

# System generates 3 collision-free options:
┌──────────────────────────────────────────────────┐
│ 🎯 Choose Box Name                               │
│                                                   │
│ [1] koan-gentle-sunrise (recommended)            │
│ [2] koan-amber-cascade                           │
│ [3] koan-swift-meadow                            │
│                                                   │
│ [C] Custom name                                  │
│ [O] Other patterns (Serial/GUID)                 │
│                                                   │
│ [?] Select [1]: _                                │
└──────────────────────────────────────────────────┘

# Format: koan-{adjective}-{noun}
# Examples:
#   koan-gentle-sunrise, koan-amber-cascade, koan-swift-meadow
#   koan-crimson-harbor, koan-silver-nexus, koan-jade-prism
```

**Dictionary Design:**

- **Adjectives (24):** gentle, amber, swift, quiet, bright, silver, crimson, jade, azure, golden, velvet, mystic, lunar, solar, cosmic, crystal, emerald, ruby, sapphire, pearl, opal, topaz, coral, obsidian

- **Nouns (24):** sunrise, cascade, meadow, horizon, nebula, prism, beacon, haven, nexus, forge, summit, harbor, citadel, atrium, vault, terrace, garden, plaza, portal, gateway, chamber, sanctum, archive, gallery

- **Combinations:** 576 possible names (24 × 24)
- **Collision handling:** Append number if DNS conflict (e.g., koan-amber-cascade-2)

#### **Pattern 2: Serial (Organizational)**

```bash
.\koanbox-install.ps1 -UsbDrive "E:" -Pattern Serial

# Auto-scans network for existing boxes
# Suggests next available number

# Output:
┌──────────────────────────────────────────────────┐
│ Serial Box Naming                                │
│                                                   │
│ Detected existing boxes:                         │
│   • koan-box-0001 (192.168.1.42)                 │
│   • koan-box-0002 (192.168.1.50)                 │
│                                                   │
│ Next available: koan-box-0003                    │
│                                                   │
│ [?] Use koan-box-0003? [Y/n]: _                  │
└──────────────────────────────────────────────────┘

# Format: koan-box-{nnnn} (4-digit zero-padded)
# Prefix customizable: -Pattern Serial -Prefix "acme-koan"
#   → acme-koan-0001, acme-koan-0002, etc.
```

#### **Pattern 3: GUID (Maximum Uniqueness)**

```bash
.\koanbox-install.ps1 -UsbDrive "E:" -Pattern GUID

# Generates short GUID (first 8 chars)
# Guaranteed unique, no collision checks needed

# Output: koan-7a3f2c8d
# Format: koan-{guid[:8]}
```

#### **Pattern 4: Custom (User Choice)**

```bash
.\koanbox-install.ps1 -UsbDrive "E:" -Name "my-lab"

# Direct specification, performs collision check
# If collision detected, prompts for alternative
```

---

### **DNS Collision Avoidance**

#### **Pre-Flight Checks**

Before suggesting any name, the system performs:

```bash
# 1. mDNS Check (Avahi/Bonjour)
avahi-resolve -n koan-amber-cascade.local
# Exit 0 = collision, Exit 1 = available

# 2. DNS Check (corporate networks)
nslookup koan-amber-cascade.corp.example.com
# Found = collision, NXDOMAIN = available

# 3. Docker Host Check (existing containers)
docker ps -a --filter "name=koan-amber-cascade" --format "{{.Names}}"
# Output = collision, Empty = available

# 4. Registry Check (Harbor API)
curl -sf https://registry:5000/v2/_catalog | grep "koan-amber-cascade"
# Found = collision, Empty = available
```

#### **Collision Resolution**

```bash
# If collision detected during generation:

# Option 1: Regenerate (tries different word combo)
koan-swift-meadow (collision) → koan-golden-haven (available)

# Option 2: Append suffix (if regeneration fails)
koan-amber-cascade → koan-amber-cascade-2

# Option 3: Prompt user (if pattern is Custom)
┌──────────────────────────────────────────────────┐
│ ⚠️  Name Collision Detected                      │
│                                                   │
│ "my-lab" is already in use on this network.     │
│                                                   │
│ Suggestions:                                     │
│   [1] my-lab-2 (append suffix)                   │
│   [2] my-lab-dev (append context)                │
│   [3] koan-gentle-sunrise (random alternative)   │
│   [C] Choose different name                      │
│                                                   │
│ [?] Select [1]: _                                │
└──────────────────────────────────────────────────┘
```

---

### **Implementation: Name Generator Service**

```csharp
// src/Koan.Orchestration.Cli/Naming/BoxNameGenerator.cs

public interface IBoxNameGenerator
{
    Task<string[]> GenerateNames(NamingPattern pattern, int count = 3);
    Task<bool> CheckAvailability(string name);
    Task<string> GetNextSerial(string prefix = "koan-box");
}

public enum NamingPattern
{
    Random,      // koan-amber-cascade
    Serial,      // koan-box-0001
    GUID,        // koan-7a3f2c8d
    Custom       // user-provided
}

**Name Generation Algorithm (PowerShell/Bash):**

```text
1. Maintain word lists:
   - Adjectives: ["gentle", "amber", "swift", "quiet", ...] (24 words)
   - Nouns: ["sunrise", "cascade", "meadow", "horizon", ...] (24 words)
   - Total combinations: 576

2. Generation loop:
   - Pick random adjective + noun
   - Format: "satellite-{adj}-{noun}"
   - Check availability (DNS, mDNS, Docker, Registry)
   - If collision, retry (max 10 attempts)
   - Fallback: serial pattern (satellite-0001)

3. Multi-layer collision checking:
   a. mDNS: avahi-resolve --name {name}.local
   b. DNS: Resolve-DnsName {name}.local
   c. Docker: docker ps --filter name={name}
   d. Registry: curl harbor.local/api/v2.0/projects?name={name}
```

**Target Performance:**
- Name generation: < 500ms
- DNS checking: < 300ms (parallel where possible)
- Total cycle: < 1 second per tap
        }
    }
    
    private async Task<bool> CheckDockerHost(string name)
    {
        var result = await ExecuteCommand($"docker ps -a --filter \"name={name}\" --format \"{{{{.Names}}}}\"");
        return !string.IsNullOrWhiteSpace(result.Output); // Output = collision
    }
}
```

---

### **Interactive UI Examples**

#### **Primary Flow (Random Words)**

```powershell
PS> .\koanbox-install.ps1 -UsbDrive "E:"

🔍 Analyzing network...
✓ Checked DNS (0 collisions)
✓ Generated 3 available names

┌──────────────────────────────────────────────────┐
│ 🎯 Choose Box Name                               │
│                                                   │
│ [1] koan-gentle-sunrise (recommended)            │
│     → http://koan-gentle-sunrise.local           │
│                                                   │
│ [2] koan-amber-cascade                           │
│     → http://koan-amber-cascade.local            │
│                                                   │
│ [3] koan-swift-meadow                            │
│     → http://koan-swift-meadow.local             │
│                                                   │
│ [C] Custom name                                  │
│ [O] Other patterns (Serial/GUID)                 │
│                                                   │
│ Press Enter for [1], or type choice: _           │
└──────────────────────────────────────────────────┘
```

#### **Alternative Patterns Menu**

```powershell
# User selected [O]

┌──────────────────────────────────────────────────┐
│ 📋 Naming Patterns                               │
│                                                   │
│ [1] Random Words (memorable)                     │
│     → koan-amber-cascade                         │
│     Best for: Solo devs, home labs               │
│                                                   │
│ [2] Serial (sequential)                          │
│     → koan-box-0001                              │
│     Best for: Teams, organizations               │
│                                                   │
│ [3] GUID (guaranteed unique)                     │
│     → koan-7a3f2c8d                              │
│     Best for: Automation, scripting              │
│                                                   │
│ [4] Custom (you choose)                          │
│     Best for: Specific naming conventions        │
│                                                   │
│ Press Enter for [1], or type choice: _           │
└──────────────────────────────────────────────────┘
```

---

### **Benefits of This Approach**

| Aspect | Traditional | KoanBox Naming | Improvement |
|--------|-------------|----------------|-------------|
| **Memorability** | "192.168.1.42" or "kb-7a3f" | "koan-amber-cascade" | Human-friendly |
| **Collision Risk** | Manual checking | Auto-verified | Zero conflicts |
| **Typing Effort** | Copy-paste GUIDs | Memorable words | Fast to type |
| **Documentation** | "Deploy to IP X" | "Deploy to amber-cascade" | Self-documenting |
| **Team Comms** | "Use the box at .42" | "Use gentle-sunrise" | Clear reference |

---

### **Command-Line Shortcuts**

```bash
# Auto-select (no prompts)
.\koanbox-install.ps1 -UsbDrive "E:" -Auto
# → Uses first random name, skips all prompts

# Direct naming
.\koanbox-install.ps1 -UsbDrive "E:" -Name "my-lab"

# Pattern selection
.\koanbox-install.ps1 -UsbDrive "E:" -Pattern Serial -Prefix "acme"
# → acme-0001, acme-0002, etc.

# Regenerate suggestions (interactive)
.\koanbox-install.ps1 -UsbDrive "E:" -Regenerate 5
# → Shows 5 random options instead of 3
```

---

## References

- Koan Framework: https://github.com/sylin-org/koan-framework
- ADR ARCH-0041: Documentation posture (instruction-first)
- ADR ARCH-0042: Per-project companion docs
- Ubuntu Cloud-Init: https://cloudinit.readthedocs.io/
- Traefik Docker Provider: https://doc.traefik.io/traefik/providers/docker/
- Homepage Auto-Discovery: https://gethomepage.dev/
- mDNS (Avahi): https://www.avahi.org/
- DNS-SD (Bonjour): https://developer.apple.com/bonjour/

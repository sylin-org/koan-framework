# Windows Moss Service: Feasibility & Value Assessment

**Date:** January 16, 2026  
**Status:** Strategic Evaluation  
**Scope:** GPU-First Implementation Strategy

---

## Executive Summary

**Question:** Should Moss run as a Windows service alongside the existing Linux implementation?

**Answer:** **Yes, with GPU-first positioning.** Windows Moss enables repurposing old gaming PCs as AI Stones, turning e-waste into high-value infrastructure. Rust's cross-platform nature (proven by garden-rake.exe) makes implementation practical with ~70% code reuse.

**Strategic Positioning:**
- **Primary use case:** GPU-enabled AI workloads (Ollama, Stable Diffusion, Whisper)
- **Target hardware:** Old gaming PCs (2015-2018) with NVIDIA GTX 1060/1070/1080
- **Value proposition:** Add local AI inference capabilities without new hardware purchases
- **Mission alignment:** Prevent functional GPUs from reaching landfills

**Key Success Factors:**
1. ✅ **Rust portability** - Garden-rake.exe proves cross-platform viability; extend to Moss
2. ✅ **GPU repurposing** - Old gaming PCs are prime e-waste candidates with capable GPUs
3. ✅ **WSL2 GPU passthrough** - Mature, well-documented Docker Desktop feature
4. ✅ **Clear positioning** - "AI Stones on Windows, general compute on Linux"

**Recommendation:** Implement Windows Moss via three-phase approach (document, prototype, validate) with GPU detection as installation prerequisite. Total effort: 8-10 weeks to production-ready beta.

---

## Strategic Context

### The GPU Compute Opportunity

**Hardware Spectrum:**

**Repurposed Gaming PCs (2015-2018):**
- GTX 1060/1070/1080 still capable for AI inference
- "Obsolete" for modern gaming, perfect for LLMs/SD
- Windows + drivers already working
- Estimated millions approaching end-of-primary-use

**Modern GPU Builds (2022+):**
- RTX 4070/4080/4090 for high-performance AI
- Purpose-built for local AI workloads
- User owns hardware vs renting cloud GPU time
- Complements garden of repurposed machines

**The Ownership Value:**
- **Repurposed:** Prevent e-waste, zero acquisition cost
- **Modern:** Own vs rent, no per-token charges, full control
- **Both:** Local compute fabric under user's control

**Use Cases Enabled:**

| Workload | Old GPU (GTX 1070) | Modern GPU (RTX 4080) | Cloud Alternative Cost |
|----------|-------------------|----------------------|------------------------|
| LLM inference (7B) | 15 tokens/sec | 60 tokens/sec | $0.50-2/M tokens |
| Stable Diffusion | 8 sec/image | 2 sec/image | $0.02-0.10/image |
| Whisper transcription | Real-time | 4x real-time | $0.006/min |

**Result:** Whether repurposed or new, local GPU hosting delivers **compute ownership** and eliminates recurring cloud costs.

### Mission Alignment

**Zen Garden Core Principles:**
1. **Compute Ownership** - Own and control your infrastructure (vs cloud dependency)
2. **Heterogeneous Gardens** - Mix repurposed and modern hardware as needed
3. **Self-Hosting Simplification** - Make local infrastructure accessible
4. **E-Waste Reduction** - Extend hardware lifespan where practical

**Windows Moss Fit:**

| Principle | Alignment | Rationale |
|-----------|-----------|-----------|
| Compute Ownership | ✅ Strong | Modern GPU rig + 10 ewaste servers = owned compute fabric under your control |
| Heterogeneous Gardens | ✅ Strong | Windows AI Stone complements Linux database/cache Stones seamlessly |
| Self-Hosting | ✅ Strong | Local AI inference without cloud API dependency or per-token costs |
| E-Waste Reduction | ✅ Strong | Repurpose old gaming PCs; also accommodate modern builds |

**Garden Composition Examples:**

| Garden Profile | Stone Mix | Windows Moss Role |
|----------------|-----------|-------------------|
| Home Lab | 3 old laptops (Linux) + 1 gaming PC (Windows GPU) | AI inference for entire garden |
| Small Studio | 5 thin clients (Linux) + 1 modern workstation (Windows GPU) | Stable Diffusion, LLM agents |
| Prosumer NAS+ | 1 NAS (Linux) + 10 ewaste servers (Linux) + 1 GPU rig (Windows) | High-performance AI + distributed storage + backup |

**Key Insight:** Zen Garden is about **owned compute**, not exclusively old hardware. Windows Moss enables modern GPU rigs to participate in gardens composed primarily of repurposed machines. The garden's value is **local ownership** vs cloud dependency.

---

## Technical Feasibility

### Rust Cross-Platform Advantage

**Garden-rake.exe Proves Viability:**

The existing Windows garden-rake demonstrates Rust's cross-platform capabilities:

```bash
# Same source code, two binaries
cargo build --target x86_64-unknown-linux-gnu      # → garden-rake (Linux)
cargo build --target x86_64-pc-windows-msvc       # → garden-rake.exe (Windows)
```

**What Works Cross-Platform in Garden-Rake:**
- ✅ UDP socket discovery (tokio::net::UdpSocket)
- ✅ HTTP client calls (reqwest with rustls)
- ✅ JSON serialization (serde, serde_json)
- ✅ Command-line parsing (clap)
- ✅ Async runtime (tokio)

**What Moss Adds (platform-specific):**
- 🔧 Docker daemon connection (socket vs named pipe)
- 🔧 Service lifecycle (systemd vs Windows Service API)
- 🔧 mDNS announcement (Linux-only, Windows uses UDP only)

### Code Reuse Estimate

**Shared Components (~70-75%):**

| Component | Shared % | Notes |
|-----------|----------|-------|
| HTTP API handlers | 95% | Axum works identically on both platforms |
| Docker API logic | 90% | Bollard supports named pipes; API calls same |
| JSON serialization | 100% | Serde platform-agnostic |
| UDP discovery | 100% | Tokio networking cross-platform |
| Metrics collection | 95% | sysinfo crate supports Windows |
| Template engine | 100% | Text processing identical |
| Service lifecycle | 0% | systemd vs Windows Service completely different |
| mDNS | 0% | Linux-only; Windows uses UDP fallback |

**Platform-Specific Code (~25-30%):**
- Windows Service API integration (`windows-service` crate)
- Docker named pipe connection (`\\.\pipe\docker_engine`)
- Event Log integration (vs journald on Linux)
- GPU detection (NVIDIA-SMI / wmic queries)

**Maintenance Reality:**  
One Rust codebase with conditional compilation beats maintaining separate implementations in different languages.

### Technical Implementation Details

#### 1. Docker Connection

**Linux:**
```rust
#[cfg(not(target_os = "windows"))]
let docker = Docker::connect_with_socket_defaults()
    .context("Failed to connect via Unix socket")?;
```

**Windows:**
```rust
#[cfg(target_os = "windows")]
let docker = Docker::connect_with_named_pipe_defaults()
    .context("Failed to connect via named pipe")?;
```

**Bollard Support:** `bollard` crate has first-class Windows support via named pipes. No third-party patches needed.

#### 2. Service Lifecycle

**Linux (systemd):**
- Unit file: `/etc/systemd/system/garden-moss.service`
- Logging: journald (structured logs)
- Management: `systemctl start/stop/restart garden-moss`

**Windows (SCM):**
- Service registration via `windows-service` crate
- Logging: Windows Event Log
- Management: `sc.exe` or Services MMC snap-in

**Implementation:**
```rust
#[cfg(target_os = "windows")]
fn run_as_windows_service() -> Result<()> {
    use windows_service::{define_windows_service, service_dispatcher};
    define_windows_service!(ffi_service_main, moss_service_main);
    service_dispatcher::start("MossService", ffi_service_main)?;
    Ok(())
}
```

**Existing Patterns:** Hundreds of Rust projects run as Windows services (e.g., cloudflared, tailscale-windows).

#### 3. Discovery

**mDNS (Linux only):**
```rust
#[cfg(not(target_os = "windows"))]
pub fn announce_moss(name: &str, port: u16) -> Result<ServiceDaemon> {
    let mdns = ServiceDaemon::new()?;
    // register _moss._tcp.local service
    Ok(mdns)
}
```

**UDP Broadcast (Cross-Platform):**
```rust
// Already works in garden-rake.exe
fn broadcast_presence() -> Result<()> {
    let socket = UdpSocket::bind("0.0.0.0:3002")?;
    socket.set_broadcast(true)?;
    socket.send_to(b"MOSS_ONLINE", "255.255.255.255:3004")?;
    Ok(())
}
```

**Windows Strategy:** Skip mDNS, rely on UDP broadcast (already implemented and working).

#### 4. GPU Detection

**Prerequisite Check:**
```rust
#[cfg(target_os = "windows")]
fn check_gpu() -> Result<GpuInfo> {
    // Option 1: nvidia-smi query
    let output = Command::new("nvidia-smi")
        .args(["--query-gpu=name,memory.total", "--format=csv,noheader"])
        .output()?;
    
    // Option 2: WMIC query
    // wmic path win32_VideoController get name
    
    // Return GPU name + VRAM, or error if no NVIDIA GPU found
}
```

**Installation Guard:** Windows Moss installer refuses to proceed if no compatible GPU detected.

### Dependency Matrix

**Core Dependencies (Shared):**
- `axum` - HTTP server (works on Windows)
- `tokio` - Async runtime (works on Windows)
- `bollard` - Docker client (named pipe support)
- `serde` / `serde_json` - Serialization
- `anyhow` - Error handling
- `tracing` - Logging

**Platform-Specific:**
- Linux: `mdns-sd`, `systemd-notify`
- Windows: `windows-service`, `winapi`, `wmi`

**Testing Matrix:**
- Linux x86_64 + Docker Engine (existing)
- Windows x86_64 + Docker Desktop WSL2 (new)
- CI: GitHub Actions supports both platforms

---

## Use Case Analysis

### Primary: GPU-Enabled AI Stone

**User Profile A: Repurposed Hardware**
- Has old gaming PC (2015-2018) with GTX 1060/1070/1080
- PC no longer viable for modern gaming
- Windows 10/11 + drivers already working
- Wants local AI for garden with 5-10 other repurposed machines
- Not comfortable with Linux NVIDIA driver complexity

**User Profile B: Modern Prosumer Build**
- Built/bought modern workstation with RTX 4070/4080
- Wants to add AI capabilities to existing garden (NAS + ewaste servers)
- Prefers Windows for GPU workloads (driver stability, tooling)
- Values compute ownership vs recurring cloud GPU costs
- Garden composition: 1 modern AI rig + 10 repurposed Linux Stones

**User Profile C: Hybrid Upgrade**
- Garden has 8 old machines (databases, caches, storage)
- Decides to add dedicated AI Stone (new or repurposed GPU)
- Windows Moss enables seamless integration
- Result: Heterogeneous garden with specialized GPU node

**User Journey:**
1. Download `moss-windows-setup.exe` from releases
2. Installer detects GPU → "RTX 4080 detected, 16GB VRAM available" or "GTX 1070, 8GB"
3. Installer checks Docker Desktop → guides installation if missing
4. Moss service installed and started
5. User runs: `garden-rake offer ollama` from any machine on LAN
6. Stone downloads Ollama image, starts with GPU passthrough
7. Garden now has local AI inference (e.g., `http://ai-stone:11434`)

**Value Delivered:**
- **Repurposed:** Zero acquisition cost, prevents e-waste, functional GPU utilized
- **Modern:** Ownership vs rental, no per-token costs, full control over infrastructure
- **Both:** Entire garden gains AI capabilities under user's ownership

### Secondary: Windows-Only Environments

**Enterprise/Organization Constraints:**
- Group Policy locks machines to Windows
- IT department requires Windows for compliance/monitoring
- Security tooling (Defender, SIEM agents) Windows-specific
- Staff lacks Linux expertise

**Windows Moss Enables:**
- Zen Garden adoption without OS exception approvals
- Gradual migration (start with Windows Stones, migrate to Linux over time)
- Hybrid gardens (Windows AI Stones + Linux database Stones)

### Non-Target: General-Purpose CPU Workloads

**Low-Spec Hardware (4-8GB RAM, no GPU):**
- **Don't use Windows Moss** - WSL2 overhead too high
- **Recommendation:** Install Linux (Debian minimal), use Linux Moss
- **Rationale:** 2GB overhead unacceptable on resource-constrained hardware

**Database/Cache Hosting:**
- **Don't use Windows Moss** - overhead not justified by workload
- **Recommendation:** Linux Stones for MongoDB, Redis, PostgreSQL
- **Windows Role:** Client (garden-rake.exe discovers and manages Linux Stones)

**Garden Composition Patterns:**

```
Optimal Heterogeneous Garden:

┌─────────────────────────────────────────────────────┐
│  Modern Windows AI Stone (RTX 4080, 32GB RAM)       │
│  → Ollama, Stable Diffusion, Whisper               │
│  → High-performance inference for entire garden     │
└─────────────────────────────────────────────────────┘
                        ↓
        ┌───────────────┴───────────────┐
        │                               │
┌───────▼────────┐            ┌────────▼───────┐
│ Modern NAS     │            │ 10 Repurposed  │
│ (Linux, TrueNAS│            │ Laptops/Thin   │
│ or Debian)     │            │ Clients (Linux)│
│ → Storage      │            │ → MongoDB,     │
│ → Backups      │            │   Redis, Etc.  │
└────────────────┘            └────────────────┘

Value: Owned infrastructure mixing purpose-built
       modern hardware with repurposed ewaste
```

**Clear Positioning:**
```
Use Windows Moss IF:
  • GPU present (NVIDIA/AMD)
  • AI workloads (Ollama, SD, Whisper)
  • 12GB+ RAM (to absorb WSL2 overhead)
  • Part of larger garden (modern + repurposed mix)

Use Linux Moss IF:
  • No GPU or CPU-only workloads
  • Low-spec hardware (<8GB RAM)
  • Database/cache/storage offerings
  • Repurposed machines optimizing resources
```

---

## Implementation Strategy

### Phase 1: Foundation & Documentation (Weeks 1-2)

**Deliverables:**
1. **docs/WINDOWS-GPU-STONES.md**
   - GPU detection requirements
   - Docker Desktop WSL2 setup guide
   - NVIDIA Container Toolkit installation
   - AI offering templates (Ollama, SD, Whisper)

2. **Manifest Updates**
   - Create `manifests/ai/ollama.yaml` with GPU configuration
   - Create `manifests/ai/stable-diffusion.yaml`
   - Document `--gpus all` Docker flag usage

3. **Architecture Decision Record**
   - Document Windows Moss scope (GPU-only)
   - Rationale for GPU-first strategy
   - Linux vs Windows offering matrix

**Success Criteria:**
- Documentation clear enough for community validation
- GPU requirements explicitly stated
- Installation prerequisites documented

### Phase 2: Core Implementation (Weeks 3-8)

**Week 3-4: Docker & Service Foundation**
- Implement Windows Service API integration
- Add named pipe Docker connection
- Create installer stub (check GPU, check Docker Desktop)

**Week 5-6: HTTP API & Discovery**
- Port HTTP API handlers (minimal changes needed)
- Implement UDP broadcast (skip mDNS on Windows)
- GPU detection on startup
- Event Log integration

**Week 7-8: AI Offerings & Testing**
- Test Ollama installation with GPU passthrough
- Test Stable Diffusion WebUI
- Validate GPU memory allocation
- Integration tests with garden-rake.exe

**Success Criteria:**
- `garden-moss.exe` runs as Windows Service
- GPU detection blocks installation on non-GPU machines
- `garden-rake offer ollama` works from Windows client
- Ollama container utilizes GPU (verified via nvidia-smi)

### Phase 3: Community Validation (Weeks 9-10)

**Beta Release:**
1. Package installer MSI: `moss-windows-gpu-v0.1.0-beta.msi`
2. Publish release notes clearly stating "GPU-only beta"
3. Create discussion thread for feedback
4. Monitor adoption metrics via telemetry (opt-in)

**Validation Questions:**
- How many installations succeed vs fail GPU detection?
- Performance benchmarks (Ollama inference tokens/sec)
- User pain points (Docker Desktop licensing, WSL2 setup complexity)
- Demand for general-purpose support vs GPU-only satisfaction

**Decision Matrix:**

| Outcome | Action |
|---------|--------|
| >70% installs have GPU, positive feedback | Expand to GA release |
| 30-70% GPU installs, mixed feedback | Iterate, extend beta |
| <30% GPU installs, negative feedback | Reconsider or document Linux path |

**Success Criteria:**
- At least 50 beta installations
- Average Ollama performance: >20 tokens/sec (GTX 1060 baseline)
- <20% installer failures due to GPU detection
- Positive community sentiment on value proposition

---

## Resource Allocation

### Development Effort

| Phase | Duration | Engineer-Weeks | Key Activities |
|-------|----------|----------------|----------------|
| Phase 1: Documentation | 2 weeks | 1 EW | Write docs, create manifests, ADRs |
| Phase 2: Implementation | 6 weeks | 5 EW | Docker client, Windows Service, GPU detection, testing |
| Phase 3: Beta & Validation | 2 weeks | 1 EW | Packaging, release, feedback collection |
| **Total** | **10 weeks** | **7 EW** | |

**Assumptions:**
- 1 engineer dedicated full-time
- Access to Windows GPU machine for testing
- Docker Desktop licensing resolved (personal use free)

### Ongoing Maintenance

**Incremental Burden:**
- +20% testing matrix expansion (Windows scenarios)
- +10% documentation maintenance (Windows-specific troubleshooting)
- +15% support surface (Docker Desktop, GPU driver issues)

**Total: +45% maintenance vs Linux-only**

**Mitigation:**
- Limit Windows Moss to GPU use case (reduces support surface)
- Clear documentation on prerequisites (self-service troubleshooting)
- Community forum for Windows-specific issues

### Infrastructure

**CI/CD:**
- GitHub Actions supports Windows runners
- Add windows-latest build target to existing pipeline
- GPU testing via local machine (no GPU in CI)

**Distribution:**
- MSI installer built with WiX Toolset
- GitHub Releases for binary distribution
- Chocolatey package (future: community-maintained)

---

## Risk Assessment

### Technical Risks

| Risk | Likelihood | Impact | Mitigation |
|------|------------|--------|------------|
| Docker Desktop licensing changes | Medium | High | Document Rancher Desktop alternative |
| WSL2 GPU passthrough breaks in update | Low | Medium | Test on Windows Insider builds, doc workarounds |
| Bollard named pipe regression | Low | Low | Maintain fork if needed; Bollard actively maintained |
| GPU detection false negatives | Medium | Medium | Multiple detection methods (nvidia-smi, wmic, nvml) |

### Market Risks

| Risk | Likelihood | Impact | Mitigation |
|------|------------|--------|------------|
| Low GPU hardware availability | Medium | Medium | Survey community before Phase 2 investment |
| Users prefer Linux for GPU | Low | High | Validate with beta; offer Linux guide as alternative |
| Docker Desktop adoption barrier | High | Medium | Clear setup guide; consider Podman Desktop path |

### Organizational Risks

| Risk | Likelihood | Impact | Mitigation |
|------|------------|--------|------------|
| Maintenance burden unsustainable | Medium | High | Limit scope to GPU-only; clear positioning |
| Platform fragmentation | Medium | Medium | 70% code reuse minimizes divergence |
| Community expects general-purpose support | High | Low | Clear messaging: "Windows for GPU, Linux for all else" |

---

## Success Metrics

### Phase 1 (Documentation)

- [ ] docs/WINDOWS-GPU-STONES.md published
- [ ] 3 AI offering manifests created (Ollama, SD, Whisper)
- [ ] Community feedback on scope (>20 responses on discussion)

### Phase 2 (Implementation)

- [ ] garden-moss.exe runs as Windows Service
- [ ] GPU detection blocks non-GPU installs
- [ ] Ollama offering deploys with GPU passthrough
- [ ] Integration tests passing on Windows runner

### Phase 3 (Validation)

- [ ] 50+ beta installations
- [ ] 80% installations have compatible GPU
- [ ] Average user satisfaction: 4/5 stars
- [ ] <10% installer failures

### Post-Launch (6 months)

- [ ] 200+ active Windows Stones
- [ ] 60% running Ollama or SD
- [ ] <5 critical bugs per month
- [ ] Community contributions (manifests, docs)

---

## Decision Framework

### Go Criteria

**Proceed to Phase 2 IF:**
1. Phase 1 documentation receives positive community feedback
2. At least 30 respondents indicate access to GPU-enabled hardware
3. Rust toolchain builds successfully on Windows (validated)
4. Docker Desktop licensing acceptable for target audience

**Proceed to Phase 3 IF:**
2. Implementation complete within 6-week estimate
2. Integration tests pass on Windows
3. Ollama GPU utilization verified (>50% VRAM usage under load)

**Proceed to GA Release IF:**
1. >50 beta installations
2. >70% positive feedback
3. <20% installer failures
4. No critical bugs remaining

### No-Go Criteria

**Abort IF:**
- GPU detection proves unreliable (<80% accuracy)
- Docker Desktop EULA changes prohibit free use
- Maintenance burden exceeds 50% of Linux maintenance
- Community feedback indicates Linux GPU path preferred

### Pivot Options

**If GPU adoption low:**
- Document Linux GPU installation guide
- Sunset Windows Moss, focus on Lantern Desktop (GUI client)

**If general-purpose demand high despite messaging:**
- Re-evaluate general-purpose support after 6 months
- Require 16GB+ RAM for non-GPU installations

---

## Conclusion

**Windows Moss is strategically sound with GPU-first positioning.**

**Core Rationale:**
1. **Compute Ownership** - Local GPU hosting (repurposed OR modern) eliminates cloud dependency
2. **Heterogeneous Gardens** - Modern AI Stone + repurposed Linux Stones = owned compute fabric
3. **Rust Portability** - Garden-rake.exe proves cross-platform viability; 70% code reuse expected
4. **Practical Reality** - Windows GPU drivers work; enables both old gaming PCs AND modern builds

**Clear Scope:**
- ✅ Windows Moss for GPU-enabled AI workloads (repurposed OR modern hardware)
- ❌ Windows Moss for general-purpose compute (databases, caches on low-spec machines)
- ✅ Linux Moss for databases, caches, storage (optimal for resource-constrained hardware)
- ✅ Garden-rake.exe for cross-platform management
- ✅ Heterogeneous gardens: modern AI Stone + 10 ewaste servers = owned infrastructure

**Implementation Path:**
1. Document & validate (2 weeks)
2. Prototype & test (6 weeks)
3. Beta & community feedback (2 weeks)
4. Decision point: GA, iterate, or sunset

**Bottom Line:** The GPU use case justifies Windows Moss development. Rust's cross-platform nature makes it practical. Phased approach mitigates risk while validating market demand.

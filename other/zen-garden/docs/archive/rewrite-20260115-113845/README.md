# Zen Garden

**Automatic service discovery for self-hosted infrastructure. No hardcoded IPs. Physical devices that just work.**

---

## What It Does

Traditional self-hosting breaks when IP addresses change:

```bash
# Hardcoded everywhere
MONGODB_URI=mongodb://192.168.1.50:27017/mydb
```

**Zen Garden uses intent-based discovery:**

```bash
# Never changes
MONGODB_URI=zen-garden:mongodb/mydb
```

Devices ("Stones") announce services automatically. Apps discover by intent. Router reboots, IPs change—apps reconnect without configuration edits.

---

## Hello World (60 Seconds)

```bash
# Terminal 1: Start MongoDB Stone
docker run -d -p 27017:27017 \
  -e ANNOUNCE_SERVICE=mongodb \
  zen-garden/stone:latest

# Terminal 2: Your app connects
CONNECTION_STRING="zen-garden:mongodb" node app.js
# Autodiscovers, connects. Done.
```

**What happened:** Stone announced "I offer MongoDB" via mDNS. App asked "Who has MongoDB?" Stone responded. Connection established automatically.

---

## What This Is Not

- **Not Kubernetes**: For 3-20 devices, not 100+ servers
- **Not cloud scale**: Local network (home, office, small business)
- **Not zero-config networking**: Requires mDNS/Avahi on same LAN
- **Not a vendor appliance**: Open reference designs, DIY supported

---

## Quick Links

**Run it:**  
→ [Getting Started](GETTING-STARTED.md) - Docker Stone in 5 minutes  
→ [Hardware Guide](HARDWARE.md) - Build physical Stones ($0-150)

**Integrate it:**  
→ [Technical Reference](REFERENCE.md) - Protocols, APIs, garden-rake deployment  
→ [Understanding](UNDERSTANDING.md) - How discovery works, architecture

**Evaluate it:**  
→ [Security](SECURITY.md) - When to add Pond security layer  
→ [Strategy](STRATEGY.md) - Business case, market fit  
→ [Roadmap](ROADMAP.md) - Development phases, contribution areas

---

## Classroom Demo (Why This Matters)

*Students plug in three devices: Database (blue), Storage (green), Compute (orange).*

```bash
# Lantern output
[lantern] stone joined: db-stone-01 (mongodb)
[lantern] stone joined: storage-stone-01 (minio)

# App connects via intent
APP_DB=zen-garden:mongodb dotnet run
# [resolver] mongodb → mongodb://db-stone-01:27017

# Teacher unplugs db-stone-01, plugs in db-stone-02
[lantern] stone left: db-stone-01
[lantern] stone joined: db-stone-02 (mongodb)
# [resolver] mongodb → mongodb://db-stone-02:27017
# App reconnects automatically
```

**No config files changed. Infrastructure became physical, visible, swappable.**

---

## Status

**What exists today:**
- mDNS-based discovery (peer-to-peer)
- Connection string resolver (`zen-garden:mongodb`)
- Docker Stone containers
- NewStone.ps1 USB installer (Debian)

**What's planned:**
- Lantern (optional directory + dashboard) - Phase 2
- garden-rake deployment tool - Phase 3
- Pond security (cryptographic binding) - Phase 5

See [ROADMAP.md](ROADMAP.md) for phases and timelines.

---

**License:** Open source (see LICENSE)  
**Contributing:** See CONTRIBUTING.md  
**Support:** GitHub Issues
```

Devices ("Stones") announce services automatically. Apps discover by intent. Infrastructure becomes **intent-based** instead of **location-based**.

---

## Complete Workflow: Discovery + Deployment

**Setup (5 minutes):**
```bash
# Install Lantern (any device)
curl -sSL https://get.zen-garden.dev | bash

# Install Stone software on old laptop
curl -sSL https://get.zen-garden.dev/stone | bash

# Run MongoDB
docker run -d -p 27017:27017 \
  -e ANNOUNCE_SERVICE=mongodb \
  mongo:latest
```

**Deploy app:**
```bash
garden-rake push myapp --image myapp:latest
# [rake] selecting compute stone... compute-stone-01
# [rake] app live at: http://myapp.garden/
```

**Your app auto-discovers services:**
```javascript
// app.js
const db = await connect(process.env.DB); // zen-garden:mongodb
// Automatically resolves to actual Stone location
```

**No Kubernetes. No YAML. Just devices you can hold.**

---

## Start Here: Choose Your Path

### 👨‍💼 Decision Maker / Executive
**Time**: 15 minutes  
**Path**: [Business Case](./strategy/business-case.md) → [Vision](./strategy/vision.md) → [Roadmap](./planning/ROADMAP.md)

**You'll learn**:
- ROI & cost savings ($75K+/year potential)
- Market positioning vs. cloud/competitors
- Investment requirements & success criteria

---

### 👩‍💻 Developer / Contributor
**Time**: 5 minutes to running code  
**Path**: [Quick Start](./guides/getting-started.md) → [Architecture](./architecture/overview.md) → [API Reference](./reference/api.md)

**You'll learn**:
- Hello World in 60 seconds
- How discovery actually works (mDNS/directory)
- Integration patterns for your apps

---

### 🏗️ Architect / Tech Lead
**Time**: 20 minutes  
**Path**: [Architecture](./architecture/overview.md) → [Limitations](./reference/limitations.md) → [Security](./security/approach.md)

**You'll learn**:
- What Zen Garden does (and doesn't do)
- Failure modes & tradeoffs
- Security model (opt-in complexity)

---

### 🔨 Hardware Enthusiast / DIY
**Time**: 10 minutes  
**Path**: [Hardware Philosophy](./hardware/philosophy.md) → [Stone Specs](./hardware/stone-specs.md) → [USB Installer](../installer/README.md)

**You'll learn**:
- E-waste reclamation strategy
- Thin client/laptop requirements
- How to create bootable Stone installers

---

## Current Status

**Phase**: Hello World Milestone (Week 3)  
**Decision**: ✅ **CONDITIONAL GO** (9.1/10 Strategic Approval)  
**Next Milestone**: Validation gate (6 weeks)

See [ROADMAP.md](./planning/ROADMAP.md) for timeline and milestones.

---

## Philosophy in 3 Bullets

- **Stones rest where placed**: Devices announce services; no configuration required
- **Start dry, add water**: Security is opt-in; playground first, production hardening when needed
- **E-waste first**: Best stone is one that doesn't create new waste

---

## Quick Links

- **USB Installer**: [Create bootable Stones](../installer/README.md)
- **Manifest System**: [Define service offerings](./reference/manifest-spec.md)
- **Troubleshooting**: [Common issues](./guides/troubleshooting.md)
- **Contributing**: [How to help](./contributing/getting-started.md)

---

## What Makes This Different

Most homelabs trust the LAN. Zen Garden leans into that reality by default: a "dry garden" is meant to be a playground—fast to adopt, easy to understand. When stakes rise (sensitive data, compliance), you can introduce the **pond**: cryptographic binding that makes stones belong to a specific garden.

**Security is opt-in complexity.** This aligns with how teams actually adopt infrastructure: start simple, then harden as the value proves itself.

---

**Next**: [5-Minute Quick Start](./guides/getting-started.md) | [Business Case](./strategy/business-case.md) | [Full Documentation Index](./introduction/INDEX.md)

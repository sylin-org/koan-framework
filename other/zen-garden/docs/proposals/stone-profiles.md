# Zen Garden Stone Profiles Specification

**Hearth, Workbench, Gateway: Pre-configured modes for different hardware roles**

**Status:** Proposal  
**Date:** January 2026  
**Authors:** Collaborative design session

---

## Table of Contents

1. [Overview](#overview)
2. [The Four Profiles](#the-four-profiles)
3. [Profile Details](#profile-details)
4. [Profile Detection](#profile-detection)
5. [Installer Flow](#installer-flow)
6. [Garden Composition](#garden-composition)
7. [CLI Reference](#cli-reference)
8. [API Specification](#api-specification)
9. [Configuration](#configuration)

---

## Overview

### The Problem

Different hardware serves different purposes:

| Hardware | Docker? | Always-on? | GPU? | Role |
|----------|---------|------------|------|------|
| Refurb Dell Wyse | Yes | Yes | No | Dedicated server |
| Gaming PC | Optional | No | Yes | Occasional AI provider |
| Developer laptop | Optional | No | Maybe | Workstation |
| Raspberry Pi | No | Yes | No | Network announcer |

A one-size-fits-all configuration doesn't work. The pendrive installer shouldn't configure a gaming PC the same way as dedicated e-waste.

### The Solution: Stone Profiles

Profiles pre-configure which [offering modes](offering-modes.md) are enabled, whether Docker is required, and how the Stone participates in garden operations.

```
┌─────────────────────────────────────────────────────────────────┐
│                       STONE PROFILES                            │
├─────────────────────────────────────────────────────────────────┤
│                                                                 │
│   HEARTH           WORKBENCH        GATEWAY          FULL       │
│   ────────         ─────────        ───────          ────       │
│   Dedicated        Developer/       Network          Power      │
│   server           Gaming           announcer        user       │
│                                                                 │
│   planted ✓        planted ✗        planted ✗        planted ✓  │
│   adopted ✗        adopted ✓        adopted ✗        adopted ✓  │
│   borrowed ✓       borrowed ✓       borrowed ✓       borrowed ✓ │
│                                                                 │
│   Docker: required Docker: no       Docker: no       Docker: yes│
│   Always-on: yes   Always-on: no    Always-on: yes   Always-on: │
│   Elder: yes       Elder: no        Elder: no        varies     │
│                                                                 │
└─────────────────────────────────────────────────────────────────┘
```

### Design Principles

1. **Sensible defaults** — Each profile works out of the box for its use case
2. **Hardware-aware** — Installer recommends based on detected hardware
3. **Not locked in** — Profiles can be changed or customized
4. **Explicit trade-offs** — Each profile documents what it enables and disables

---

## The Four Profiles

### Summary

| Profile | Modes | Docker | Always-on | Elder Eligible | Target Hardware |
|---------|-------|--------|-----------|----------------|-----------------|
| **Hearth** | planted, borrowed | Required | Yes | Yes | Refurb e-waste, dedicated servers |
| **Workbench** | adopted, borrowed | No | No | No | Gaming PCs, developer machines |
| **Gateway** | borrowed | No | Yes | No | Raspberry Pi, tiny always-on devices |
| **Full** | all three | Required | Configurable | Yes | Power users, lab environments |

### Offering Modes Recap

| Mode | What It Manages | Lifecycle Control |
|------|-----------------|-------------------|
| **Planted** | Docker containers | Full (pull, start, stop, update) |
| **Adopted** | Native processes | Configurable (monitor, optionally start/stop) |
| **Borrowed** | External devices | None (announce only) |

See [Offering Modes Specification](offering-modes.md) for details.

---

## Profile Details

### Hearth

*The warm center of your garden. Always on, tending the fire.*

```
┌─────────────────────────────────────────────────────────────────┐
│                     PROFILE: HEARTH                             │
├─────────────────────────────────────────────────────────────────┤
│                                                                 │
│   Philosophy:    Dedicated infrastructure. This machine         │
│                  exists to serve the garden.                    │
│                                                                 │
│   Modes:         planted ✓    (Docker containers)               │
│                  adopted ✗    (no native services)              │
│                  borrowed ✓   (external devices)                │
│                                                                 │
│   Docker:        Required                                       │
│   Always-on:     Yes (expected to stay running)                 │
│   Elder:         Eligible (can coordinate ceremonies)           │
│                                                                 │
│   Target:        • Refurbished e-waste (Dell Wyse, thin clients)│
│                  • Dedicated home servers                       │
│                  • NUCs and mini PCs                            │
│                  • Old laptops repurposed as servers            │
│                  • Anything plugged in and forgotten            │
│                                                                 │
└─────────────────────────────────────────────────────────────────┘
```

**Why no adopted mode?**

A Hearth is dedicated infrastructure. If you're running a native service on it, you should containerize it for consistent management. The machine IS the infrastructure—there's no "user" running other applications.

**Configuration:**

```toml
# Profile: hearth
[stone]
profile = "hearth"
name = "stone-wyse-01"

[offerings]
modes = ["planted", "borrowed"]

[availability]
always_on = true
expected_uptime = "24/7"

[elder]
eligible = true
# Hearths make excellent Elders - they're always available

[docker]
required = true
# socket auto-detected
```

**Pendrive installer default.** When you boot the Zen Garden USB on e-waste hardware, Hearth is the default profile.

---

### Workbench

*Where you do your work. Powerful, but not always available.*

```
┌─────────────────────────────────────────────────────────────────┐
│                    PROFILE: WORKBENCH                           │
├─────────────────────────────────────────────────────────────────┤
│                                                                 │
│   Philosophy:    A capable machine that participates when       │
│                  available. User controls lifecycle.            │
│                                                                 │
│   Modes:         planted ✗    (no Docker requirement)           │
│                  adopted ✓    (native services)                 │
│                  borrowed ✓   (external devices)                │
│                                                                 │
│   Docker:        Not required (user may have it)                │
│   Always-on:     No (may sleep, reboot, travel)                 │
│   Elder:         Not eligible (too intermittent)                │
│                                                                 │
│   Target:        • Gaming PCs with GPU                          │
│                  • Developer laptops                            │
│                  • Workstations                                 │
│                  • Any machine used for other purposes          │
│                                                                 │
└─────────────────────────────────────────────────────────────────┘
```

**Why no planted mode by default?**

Workbenches are machines people use for other things. Running Docker daemon constantly:
- Uses memory
- Slows startup
- May conflict with games/apps
- Adds complexity users don't want

If a Workbench user has Docker and wants planted offerings, they can switch to Full profile or enable planted mode manually.

**Configuration:**

```toml
# Profile: workbench
[stone]
profile = "workbench"
name = "stone-gaming-pc"

[offerings]
modes = ["adopted", "borrowed"]

[availability]
always_on = false
# Garden won't rely on this Stone for critical services

[elder]
eligible = false
# Can't coordinate ceremonies if you might be playing games

[schedule]
# Optional: only share to meadow during certain hours
enabled = true
share_window = "22:00-08:00"
timezone = "America/New_York"
# During these hours, adopted services are announced to meadow
# Outside these hours, only available within local garden
```

**The gaming PC scenario:**

João has a powerful gaming PC with an RTX 4090. He:
1. Installs Moss with Workbench profile
2. Moss adopts his existing ollama installation
3. Configures share window for nighttime
4. Joins the village meadow

At night, his GPU serves AI requests. During the day, he plays games. Moss monitors ollama but doesn't interfere.

---

### Gateway

*A small stone that points the way to borrowed scenery.*

```
┌─────────────────────────────────────────────────────────────────┐
│                     PROFILE: GATEWAY                            │
├─────────────────────────────────────────────────────────────────┤
│                                                                 │
│   Philosophy:    Minimal footprint. Just announces external     │
│                  devices to the garden.                         │
│                                                                 │
│   Modes:         planted ✗    (no Docker)                       │
│                  adopted ✗    (no native services)              │
│                  borrowed ✓   (external devices only)           │
│                                                                 │
│   Docker:        Not required                                   │
│   Always-on:     Yes (but minimal resources)                    │
│   Elder:         Not eligible (too limited)                     │
│                                                                 │
│   Target:        • Raspberry Pi Zero                            │
│                  • Old routers with OpenWrt                     │
│                  • Any tiny always-on device                    │
│                  • Placed near IoT devices / NAS                │
│                                                                 │
└─────────────────────────────────────────────────────────────────┘
```

**Use case:**

Your NAS lives in the basement. No Stone down there. You can't install Moss on the Synology (proprietary OS). Solution: put a Raspberry Pi next to it running Moss as a Gateway. It borrows the NAS and announces it to the garden.

**Configuration:**

```toml
# Profile: gateway
[stone]
profile = "gateway"
name = "stone-pi-basement"

[offerings]
modes = ["borrowed"]
# That's it. No planted, no adopted.

[availability]
always_on = true
# Always-on but using minimal resources

[elder]
eligible = false
# Too limited to coordinate ceremonies

[resources]
# Moss optimizes for low memory usage
low_memory_mode = true
health_check_interval = 120  # Less frequent checks
```

**Minimal Moss:**

On a Gateway, Moss runs in minimal mode:
- No Docker integration
- No service lifecycle management
- Just: health ping borrowed devices, announce to Lantern
- Memory footprint: ~20MB

---

### Full

*Everything enabled. You know what you're doing.*

```
┌─────────────────────────────────────────────────────────────────┐
│                      PROFILE: FULL                              │
├─────────────────────────────────────────────────────────────────┤
│                                                                 │
│   Philosophy:    All capabilities enabled. Maximum flexibility. │
│                                                                 │
│   Modes:         planted ✓    (Docker containers)               │
│                  adopted ✓    (native services)                 │
│                  borrowed ✓   (external devices)                │
│                                                                 │
│   Docker:        Required                                       │
│   Always-on:     Configurable                                   │
│   Elder:         Eligible                                       │
│                                                                 │
│   Target:        • Power users                                  │
│                  • Lab environments                             │
│                  • Users who want everything                    │
│                                                                 │
└─────────────────────────────────────────────────────────────────┘
```

**When to use Full:**

You have a beefy machine that:
- Runs Docker for most services (planted)
- Has GPU with native CUDA apps (adopted)
- Also announces your NAS (borrowed)

All three modes, maximum capability.

**Configuration:**

```toml
# Profile: full
[stone]
profile = "full"
name = "stone-beast"

[offerings]
modes = ["planted", "adopted", "borrowed"]

[availability]
always_on = true    # or false, your choice

[elder]
eligible = true

[docker]
required = true
```

---

## Profile Detection

### Automatic Recommendation

The installer detects hardware and recommends a profile:

```rust
fn recommend_profile(hw: &HardwareInfo, os: &OsInfo) -> Profile {
    // Gateway: Very limited hardware
    if hw.ram_mb < 2048 {
        return Profile::Gateway;
    }
    if hw.is_raspberry_pi() || hw.is_openwrt() {
        return Profile::Gateway;
    }
    
    // Workbench: Has discrete GPU
    if hw.has_nvidia_gpu() || hw.has_amd_discrete_gpu() {
        return Profile::Workbench;
    }
    
    // Workbench: Windows desktop (not server)
    if os.is_windows() && !os.is_server_edition() {
        return Profile::Workbench;
    }
    
    // Workbench: macOS (it's someone's computer)
    if os.is_macos() {
        return Profile::Workbench;
    }
    
    // Hearth: Booted from Zen Garden installer
    if os.is_booted_from_installer() {
        return Profile::Hearth;
    }
    
    // Hearth: Linux server edition
    if os.is_linux_server() {
        return Profile::Hearth;
    }
    
    // Hearth: Headless Linux (no display manager)
    if os.is_linux() && !os.has_display_manager() {
        return Profile::Hearth;
    }
    
    // Default: Hearth for unknown dedicated hardware
    // Default: Workbench for unknown desktop hardware
    if os.has_display_manager() {
        Profile::Workbench
    } else {
        Profile::Hearth
    }
}
```

### Detection Signals

| Signal | Suggests |
|--------|----------|
| RAM < 2GB | Gateway |
| Raspberry Pi / ARM SBC | Gateway |
| NVIDIA/AMD discrete GPU | Workbench |
| Windows Desktop | Workbench |
| macOS | Workbench |
| Booted from installer USB | Hearth |
| Linux Server edition | Hearth |
| No display manager | Hearth |
| Display manager present | Workbench |

### Detected Services

The installer also scans for existing services:

```
Detected services:
  ✓ ollama (port 11434)      → Will be adopted
  ✓ postgresql (port 5432)   → Will be adopted
  ✓ Docker daemon            → Planted mode available
```

If existing services are found, the installer can pre-configure adoption.

---

## Installer Flow

### Pendrive Installer (E-waste Target)

```
┌─────────────────────────────────────────────────────────────────┐
│                     ZEN GARDEN INSTALLER                        │
│                        version 0.3.0                            │
├─────────────────────────────────────────────────────────────────┤
│                                                                 │
│  Detecting hardware...                                          │
│                                                                 │
│    Device:    Dell Wyse 5070                                    │
│    CPU:       Intel Celeron J4105 (4 cores)                     │
│    RAM:       8 GB                                              │
│    Disk:      128 GB SSD                                        │
│    GPU:       Intel UHD 600 (integrated)                        │
│    Network:   Gigabit Ethernet                                  │
│                                                                 │
│  ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━  │
│                                                                 │
│  Recommended: HEARTH                                            │
│                                                                 │
│  This looks like dedicated server hardware.                     │
│  Hearth profile will:                                           │
│    • Install Docker for container management                    │
│    • Enable planted offerings (full lifecycle)                  │
│    • Enable borrowed offerings (announce external devices)      │
│    • Configure as always-on (Elder eligible)                    │
│                                                                 │
│  ┌─────────────────────────────────────────────────────────┐   │
│  │ ► [HEARTH]     Dedicated server (recommended)           │   │
│  │    Workbench   Developer/gaming machine                 │   │
│  │    Gateway     Minimal - just announce devices          │   │
│  │    Full        All modes enabled                        │   │
│  │    Custom      Configure manually                       │   │
│  └─────────────────────────────────────────────────────────┘   │
│                                                                 │
│  Use ↑↓ to select, Enter to continue                           │
│                                                                 │
└─────────────────────────────────────────────────────────────────┘
```

### Windows/macOS Installer (Desktop Target)

```
┌─────────────────────────────────────────────────────────────────┐
│                     ZEN GARDEN INSTALLER                        │
├─────────────────────────────────────────────────────────────────┤
│                                                                 │
│  Detecting hardware...                                          │
│                                                                 │
│    Device:    Custom Gaming PC                                  │
│    CPU:       AMD Ryzen 9 7950X (16 cores)                      │
│    RAM:       64 GB                                             │
│    Disk:      2 TB NVMe                                         │
│    GPU:       NVIDIA GeForce RTX 4090 (24GB) ◄── Detected!      │
│    OS:        Windows 11 Pro                                    │
│                                                                 │
│  ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━  │
│                                                                 │
│  Recommended: WORKBENCH                                         │
│                                                                 │
│  This looks like a powerful workstation/gaming PC.              │
│  Workbench profile will:                                        │
│    • NOT require Docker (native services only)                  │
│    • Enable adopted offerings (monitor native apps)             │
│    • Enable borrowed offerings (announce external devices)      │
│    • NOT mark as always-on (you use this machine)              │
│                                                                 │
│  Detected existing services:                                    │
│    ✓ ollama (running on port 11434)                            │
│      → Will be adopted automatically                            │
│                                                                 │
│  ┌─────────────────────────────────────────────────────────┐   │
│  │ ► [WORKBENCH] Native services + GPU (recommended)       │   │
│  │    Hearth      Dedicated server (requires Docker)       │   │
│  │    Full        All modes enabled                        │   │
│  │    Custom      Configure manually                       │   │
│  └─────────────────────────────────────────────────────────┘   │
│                                                                 │
└─────────────────────────────────────────────────────────────────┘
```

### Raspberry Pi Installer

```
┌─────────────────────────────────────────────────────────────────┐
│                     ZEN GARDEN INSTALLER                        │
├─────────────────────────────────────────────────────────────────┤
│                                                                 │
│  Detecting hardware...                                          │
│                                                                 │
│    Device:    Raspberry Pi Zero 2 W                             │
│    CPU:       ARM Cortex-A53 (4 cores)                          │
│    RAM:       512 MB                                            │
│    Disk:      32 GB SD Card                                     │
│    Network:   WiFi                                              │
│                                                                 │
│  ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━  │
│                                                                 │
│  Recommended: GATEWAY                                           │
│                                                                 │
│  This is a low-resource device.                                 │
│  Gateway profile will:                                          │
│    • NOT install Docker (too heavy for this device)             │
│    • Enable borrowed offerings only                             │
│    • Run Moss in low-memory mode (~20MB)                        │
│    • Perfect for announcing NAS, printers, IoT devices          │
│                                                                 │
│  ┌─────────────────────────────────────────────────────────┐   │
│  │ ► [GATEWAY]   Announce external devices (recommended)   │   │
│  │    Workbench  Native services (may be too heavy)        │   │
│  │    Custom     Configure manually                        │   │
│  └─────────────────────────────────────────────────────────┘   │
│                                                                 │
└─────────────────────────────────────────────────────────────────┘
```

### Post-Install Summary

```
┌─────────────────────────────────────────────────────────────────┐
│                   INSTALLATION COMPLETE                         │
├─────────────────────────────────────────────────────────────────┤
│                                                                 │
│  ✓ Moss installed and running                                   │
│  ✓ Profile: WORKBENCH                                           │
│  ✓ Stone name: stone-gaming-pc                                  │
│                                                                 │
│  Adopted services:                                              │
│    ✓ ollama         [thriving]    ai:chat, ai:embeddings        │
│                                                                 │
│  Next steps:                                                    │
│                                                                 │
│    Join your garden:                                            │
│      garden-rake tend stone-wyse-01                             │
│                                                                 │
│    See what's available:                                        │
│      garden-rake observe                                        │
│      garden-rake wishes                                         │
│                                                                 │
│    Borrow external devices:                                     │
│      garden-rake borrow storage from nas.local                  │
│                                                                 │
│    Join a meadow:                                               │
│      garden-rake join meadow village-tech                       │
│                                                                 │
│                           [Finish]                              │
│                                                                 │
└─────────────────────────────────────────────────────────────────┘
```

---

## Garden Composition

### Typical Home Garden

```
┌─────────────────────────────────────────────────────────────────┐
│                     GARDEN: chen-family                         │
├─────────────────────────────────────────────────────────────────┤
│                                                                 │
│  STONES                                                         │
│  ──────                                                         │
│                                                                 │
│  stone-wyse-01           HEARTH           always-on             │
│  ├── profile: hearth                                            │
│  ├── planted: mongodb, redis, postgres, grafana, homeassistant  │
│  ├── borrowed: (none)                                           │
│  └── elder: yes (score: 658)                                    │
│                                                                 │
│  stone-gaming-pc         WORKBENCH        intermittent          │
│  ├── profile: workbench                                         │
│  ├── adopted: ollama (CUDA, managed)                            │
│  ├── borrowed: synology-nas, canon-printer                      │
│  └── elder: no (not eligible)                                   │
│                                                                 │
│  stone-pi-basement       GATEWAY          always-on             │
│  ├── profile: gateway                                           │
│  ├── borrowed: ups-monitor, temp-sensor, humidity-sensor        │
│  └── elder: no (not eligible)                                   │
│                                                                 │
│  stone-macbook           WORKBENCH        intermittent          │
│  ├── profile: workbench                                         │
│  ├── adopted: postgresql, ollama (CPU)                          │
│  ├── borrowed: (none)                                           │
│  └── elder: no (not eligible)                                   │
│                                                                 │
│  OFFERINGS (unified view)                                       │
│  ────────────────────────                                       │
│  mongodb           stone-wyse-01     planted    [thriving]      │
│  redis             stone-wyse-01     planted    [thriving]      │
│  postgres          stone-wyse-01     planted    [thriving]      │
│  grafana           stone-wyse-01     planted    [thriving]      │
│  homeassistant     stone-wyse-01     planted    [thriving]      │
│  ollama            stone-gaming-pc   adopted    [thriving]      │
│  ollama            stone-macbook     adopted    [dormant]       │
│  postgresql        stone-macbook     adopted    [dormant]       │
│  synology-nas      nas.local         borrowed   [reachable]     │
│  canon-printer     printer.local     borrowed   [reachable]     │
│  ups-monitor       ups.local         borrowed   [reachable]     │
│  temp-sensor       sensor-01.local   borrowed   [reachable]     │
│  humidity-sensor   sensor-02.local   borrowed   [reachable]     │
│                                                                 │
│  ELDER STATUS                                                   │
│  ────────────                                                   │
│  stone-wyse-01        Elder (658)      can coordinate           │
│  stone-pi-basement    Established      ineligible (gateway)     │
│  stone-gaming-pc      Seedling         ineligible (workbench)   │
│  stone-macbook        Seedling         ineligible (workbench)   │
│                                                                 │
└─────────────────────────────────────────────────────────────────┘
```

### Village Meadow

```
┌─────────────────────────────────────────────────────────────────┐
│                   MEADOW: village-tech                          │
├─────────────────────────────────────────────────────────────────┤
│                                                                 │
│  GARDENS BORDERING THIS MEADOW                                  │
│  ─────────────────────────────                                  │
│                                                                 │
│  chen-family                                                    │
│  ├── sharing: database:mongodb, database:postgres               │
│  ├── from: stone-wyse-01 (HEARTH, always-on)                    │
│  └── schedule: 24/7                                             │
│                                                                 │
│  silva-home                                                     │
│  ├── sharing: ai:chat, ai:embeddings, ai:vision                 │
│  ├── from: stone-joao-gaming (WORKBENCH)                        │
│  └── schedule: 22:00-08:00 (nighttime only)                     │
│                                                                 │
│  martinez-apt                                                   │
│  ├── sharing: storage:s3                                        │
│  ├── from: stone-server (HEARTH, always-on)                     │
│  └── schedule: 24/7                                             │
│                                                                 │
│  kim-studio                                                     │
│  ├── sharing: ai:vision, ai:image-generation                    │
│  ├── from: stone-workstation (WORKBENCH)                        │
│  └── schedule: weekdays 09:00-18:00                             │
│                                                                 │
│  AVAILABLE IN MEADOW                                            │
│  ───────────────────                                            │
│  database:mongodb      chen-family       always                 │
│  database:postgres     chen-family       always                 │
│  ai:chat              silva-home        22:00-08:00             │
│  ai:embeddings        silva-home        22:00-08:00             │
│  ai:vision            silva-home        22:00-08:00             │
│  ai:vision            kim-studio        weekdays 09:00-18:00    │
│  ai:image-generation  kim-studio        weekdays 09:00-18:00    │
│  storage:s3           martinez-apt      always                  │
│                                                                 │
└─────────────────────────────────────────────────────────────────┘
```

### Role of Each Profile

| Profile | Garden Role | Meadow Role |
|---------|-------------|-------------|
| **Hearth** | Backbone, always available, Elder | Reliable capability provider |
| **Workbench** | Powerful but intermittent | Scheduled sharing, burst capacity |
| **Gateway** | Announces borrowed devices | (Typically not shared to meadow) |
| **Full** | Flexible, user-configured | Depends on always_on setting |

---

## CLI Reference

### View Current Profile

```bash
$ garden-rake profile

STONE PROFILE
───────────────────────────────────────────────
  Stone:           stone-gaming-pc
  Profile:         workbench
  
  Modes enabled:
    planted:       no
    adopted:       yes
    borrowed:      yes
    
  Configuration:
    Docker:        not required
    Always-on:     no
    Elder:         not eligible
    
  Schedule:
    Share window:  22:00-08:00 America/New_York
```

### Change Profile

```bash
$ garden-rake profile set hearth

CHANGE PROFILE
───────────────────────────────────────────────
  Current:         workbench
  New:             hearth
  
  This will:
    ✓ Enable planted mode (Docker containers)
    ✓ Disable adopted mode
    • Keep borrowed mode enabled
    • Require Docker daemon
    • Mark as always-on
    • Enable Elder eligibility
    
  ⚠ Adopted services will be disowned:
    • ollama
    
  Docker detected: yes
  
Continue? [y/N]: y

✓ Profile changed to hearth
✓ ollama disowned
✓ Stone marked as always-on
```

### Enable Additional Mode

```bash
$ garden-rake profile enable planted

ENABLE MODE
───────────────────────────────────────────────
  Current profile: workbench
  Enabling:        planted
  
  This will:
    • Install/verify Docker
    • Allow planted offerings
    • Profile becomes: custom (workbench + planted)
    
Continue? [y/N]: y

Checking Docker... ✓ Docker Desktop found
✓ Planted mode enabled
✓ Profile is now: custom
```

### View All Profiles

```bash
$ garden-rake profile list

AVAILABLE PROFILES
───────────────────────────────────────────────
  hearth       Dedicated server
               modes: planted, borrowed
               Docker required, always-on, Elder eligible
               
  workbench    Developer/gaming machine
               modes: adopted, borrowed
               No Docker required, intermittent, not Elder
               
  gateway      Network announcer
               modes: borrowed
               Minimal footprint, always-on, not Elder
               
  full         All capabilities
               modes: planted, adopted, borrowed
               Docker required, Elder eligible

Current: workbench
Change:  garden-rake profile set <profile>
```

### Normative Aliases

```bash
garden-rake config profile show
garden-rake config profile set <profile>
garden-rake config profile enable <mode>
garden-rake config profile disable <mode>
garden-rake config profiles list
```

---

## API Specification

### Get Profile

```http
GET /api/v1/stone/profile

Response 200:
{
  "profile": "workbench",
  "modes": {
    "planted": false,
    "adopted": true,
    "borrowed": true
  },
  "docker": {
    "required": false,
    "available": true
  },
  "availability": {
    "always_on": false,
    "schedule": {
      "enabled": true,
      "share_window": "22:00-08:00",
      "timezone": "America/New_York"
    }
  },
  "elder": {
    "eligible": false
  }
}
```

### Set Profile

```http
PUT /api/v1/stone/profile
Content-Type: application/json

{
  "profile": "hearth"
}

Response 200:
{
  "profile": "hearth",
  "changes": {
    "modes": {
      "planted": {"was": false, "now": true},
      "adopted": {"was": true, "now": false}
    },
    "disowned_services": ["ollama"],
    "docker_required": true,
    "elder_eligible": true
  }
}
```

### Enable/Disable Mode

```http
PATCH /api/v1/stone/profile/modes
Content-Type: application/json

{
  "planted": true
}

Response 200:
{
  "profile": "custom",
  "modes": {
    "planted": true,
    "adopted": true,
    "borrowed": true
  }
}
```

### List Profiles

```http
GET /api/v1/profiles

Response 200:
{
  "profiles": [
    {
      "name": "hearth",
      "description": "Dedicated server",
      "modes": ["planted", "borrowed"],
      "docker_required": true,
      "always_on": true,
      "elder_eligible": true
    },
    {
      "name": "workbench",
      "description": "Developer/gaming machine",
      "modes": ["adopted", "borrowed"],
      "docker_required": false,
      "always_on": false,
      "elder_eligible": false
    },
    ...
  ],
  "current": "workbench"
}
```

---

## Configuration

### Profile Configuration Files

```toml
# /etc/zen-garden/profiles/hearth.toml

[profile]
name = "hearth"
description = "Dedicated server - always on, Docker-based"

[offerings]
modes = ["planted", "borrowed"]

[docker]
required = true

[availability]
always_on = true

[elder]
eligible = true
```

```toml
# /etc/zen-garden/profiles/workbench.toml

[profile]
name = "workbench"
description = "Developer/gaming machine - native services, intermittent"

[offerings]
modes = ["adopted", "borrowed"]

[docker]
required = false

[availability]
always_on = false

[elder]
eligible = false

[schedule]
# Default schedule for workbench (can be customized per-stone)
default_share_window = "22:00-08:00"
```

```toml
# /etc/zen-garden/profiles/gateway.toml

[profile]
name = "gateway"
description = "Network announcer - minimal footprint"

[offerings]
modes = ["borrowed"]

[docker]
required = false

[availability]
always_on = true

[elder]
eligible = false

[resources]
low_memory_mode = true
health_check_interval = 120
```

### Stone Configuration

```toml
# /etc/zen-garden/stone.toml

[stone]
name = "stone-gaming-pc"
profile = "workbench"

# Override profile settings if needed
[offerings]
modes = ["adopted", "borrowed"]  # From profile, or override here

[availability]
always_on = false

[schedule]
enabled = true
share_window = "22:00-08:00"
timezone = "America/New_York"

[elder]
eligible = false

# Adopted services (populated by garden-rake adopt)
[adopted.ollama]
endpoint = "http://localhost:11434"
managed = true
lifecycle.windows.start = "net start ollama"
lifecycle.windows.stop = "net stop ollama"

# Borrowed devices (populated by garden-rake borrow)
[borrowed.synology-nas]
hostname = "nas.local"
capabilities = ["storage:smb", "storage:nfs"]
```

---

## References

- [Offering Modes Specification](offering-modes.md) — Planted, Adopted, Borrowed
- [Federation Specification](bridges.md) — Bridges and Meadows
- [Ceremony Specification](ceremonies.md) — Elder Stones
- [Installer Specification](../specs/installer.md) — USB installer

---

## Glossary

| Term | Meaning |
|------|---------|
| **Profile** | Pre-configured set of offering modes and settings |
| **Hearth** | Dedicated server profile (planted + borrowed) |
| **Workbench** | Developer/gaming profile (adopted + borrowed) |
| **Gateway** | Minimal announcer profile (borrowed only) |
| **Full** | All modes enabled profile |
| **Always-on** | Stone expected to be running 24/7 |
| **Elder eligible** | Stone can coordinate ceremonies |

---

**Last Updated:** January 2026  
**Status:** Proposal — pending review and implementation

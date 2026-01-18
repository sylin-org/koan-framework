# Hardware Guide

**Build Stones from e-waste or buy minimal hardware. USB installer creates Debian Stones in 8-15 minutes unattended.**

---

## Creating Stone USB Installer

### Prerequisites

**USB Creation Machine (Windows):**
- Windows 10/11
- PowerShell 5.1+ (built-in)
- Administrator privileges
- 8GB+ USB drive (will be erased)
- Internet connection (downloads Debian ISO)

**Target Hardware (Stone):**
- x86-64 processor (2012+ laptop/thin client)
- 4GB RAM minimum (8GB recommended)
- 32GB storage minimum (64GB+ recommended)
- Wired Ethernet connection
- UEFI or Legacy BIOS boot

### Create Installer

```powershell
# Navigate to installer directory
cd path\to\zen-garden\installer

# Auto-detect USB drive
.\NewStone.ps1

# Or specify drive/name
.\NewStone.ps1 -UsbDrive "E:" -StoneName "db-stone-01"
```

**What it does:**
1. Downloads Debian 13.x netinst ISO (cached after first run)
2. Shows 30-second countdown with auto-generated name
3. Formats USB (labeled GARDEN-STONE)
4. Copies Debian + preseed automation
5. Copies docker-compose.yml if services selected

**Customization during countdown:**
- Tap `N`: Change stone name
- Tap `2`: Regenerate random name
- Tap `3`: Enter custom name

### Install on Target Hardware

**Boot from USB:**
1. Insert USB drive
2. Power on, tap boot menu key:
   - Dell: F12
   - HP: F9 or Esc
   - Lenovo: F12
   - Other: F2/F8/F10/Del
3. Select USB drive

**Automated installation:**
- 1-second auto-boot (no interaction required)
- Debian installs (5-8 minutes)
- Auto-reboot
- Docker + Avahi services start
- Docker Compose services launch
- **Total time:** 8-15 minutes

**Default credentials:** `stone` / `stone` (change immediately!)

### Verify Installation

```bash
# SSH into Stone
ssh stone@stone-name.local

# Check services
docker ps
docker-compose ps

# Verify mDNS
avahi-browse -a | grep stone
```

---

## Recommended Hardware

### Philosophy: E-Waste First

**Best Stone = one that doesn't create new waste.** Old laptops, thin clients, retired desktops—hardware already thrown away.

**Why this matters:**
- Entry cost: $0 (whatever you have)
- Demo impact: Physical proof of reclaimed hardware value
- No vendor lock-in: Commodity components, community designs

### Tier 1: Thin Clients (Best Value)

**Examples:** Dell Wyse 5070, HP t640, Lenovo ThinkCentre Tiny

**Specs:**
- CPU: Intel Celeron/Pentium (fanless)
- RAM: 4GB (upgradable to 8GB)
- Storage: 16GB flash (add USB SSD)
- Power: 10-25W (vs 50-150W for old laptop)

**Cost:** $30-80 used (eBay)  
**Use case:** Database/cache Stones (low power, always-on)  
**Stone roles:** Redis, MongoDB, Lantern

### Tier 2: Old Laptops (Most Accessible)

**Examples:** Any x86-64 laptop from 2012+

**Specs:**
- CPU: Intel i3/i5/i7 2nd gen+
- RAM: 4GB+ (usually upgradable)
- Storage: 64GB+ HDD/SSD
- Network: Ethernet port required

**Cost:** $0 (already owned) or $50-150 used  
**Use case:** General-purpose Stones, compute nodes  
**Stone roles:** PostgreSQL, MinIO (storage), compute workloads

### Tier 3: Mini PCs (Overkill for Most)

**Examples:** Intel NUC, Lenovo M-series

**Specs:**
- CPU: Intel i5/i7
- RAM: 8-16GB
- Storage: 256GB+ NVMe
- Form: Compact (4x4 inches)

**Cost:** $150-300 used  
**Use case:** High-performance compute, AI workloads (Ollama)  
**Stone roles:** AI inference, video transcoding, heavy compute

---

## What to Buy Today (Shopping List)

**Recommended known-good models with power/price/role mapping:**

### Option A: Dell Wyse 5070 (Best for database Stones)
- **Price**: $50-80 on eBay
- **Power**: 10W idle, 15W load
- **Best for**: MongoDB, Redis, Lantern directory
- **Storage**: Add 128GB+ USB SSD ($20-30)
- **Why**: Fanless, silent, extremely reliable
- **Search term**: "Dell Wyse 5070 thin client"

### Option B: Lenovo ThinkCentre M710q (Best for compute Stones)
- **Price**: $100-150 on eBay
- **Power**: 20W idle, 35W load
- **Best for**: PostgreSQL, compute, Docker builds
- **Storage**: Built-in 128GB+ SSD (often included)
- **Why**: Quiet, upgradable, enterprise-grade
- **Search term**: "Lenovo ThinkCentre M710q Tiny"

### Option C: HP t640 (Best for storage Stones)
- **Price**: $60-100 on eBay
- **Power**: 15W idle, 25W load
- **Best for**: MinIO (S3), Backups, NAS role
- **Storage**: Add 500GB+ USB SSD ($40-60)
- **Why**: Low power, multiple USB 3.0 ports
- **Search term**: "HP t640 thin client"

### Storage Minimums by Role

| Stone Role | Minimum Storage | Recommended | Rationale |
|------------|----------------|-------------|-----------|
| **Cache (Redis)** | 32GB | 64GB | Ephemeral data, small footprint |
| **Database (MongoDB, PostgreSQL)** | 64GB | 128GB | Persistent data with indexes |
| **Compute (Docker)** | 128GB | 256GB | Image layers, build cache |
| **Storage (MinIO, NAS)** | 256GB | 1TB+ | Actual file storage |
| **Lantern (Directory)** | 32GB | 64GB | Metadata only, tiny DB |

**Clarification on storage requirements:**
- 64GB works for cache-only Stones (Redis, Lantern)
- 128GB is baseline for database Stones (MongoDB with multi-GB datasets)
- 256GB+ for compute Stones (multiple Docker images)

---

## Not Recommended: Raspberry Pi

**Why:** ARM architecture requires different Docker images, less ecosystem support. May support when ARM matures.

### Avoid
- ❌ **32-bit CPUs**: Too old, limited OS support
- ❌ **Less than 2GB RAM**: Won't run modern services
- ❌ **ARM architecture** (for now): Ecosystem not mature

---

## Repairability Philosophy

Zen Garden encourages **right to repair**:

1. **Use standard components**: DDR4 RAM, M.2/SATA SSDs, USB boot
2. **Document disassembly**: Community-maintained repair guides
3. **Source parts widely**: Nothing proprietary that requires special vendors
4. **Extend lifespan**: A 2015 laptop can be a stone until 2030+

---

## Power Consumption Math

**Example: 3-Stone Garden**
- **Thin client** (Dell Wyse 5070): 10W idle, 15W load
- **Old laptop** (2015 model): 15W idle, 25W load
- **Total**: ~75W worst case

**Annual cost** (at $0.12/kWh):
```
75W × 24h × 365 days × $0.12 = $78.84/year
```

**Compare to cloud**:
- **MongoDB Atlas M10**: $57/month = $684/year
- **AWS t3.medium**: $30/month = $360/year
- **3-service cloud stack**: ~$1500-3000/year

**ROI**: Hardware pays for itself in 1-2 months.

---

## Sourcing Hardware

### Best Places to Find Thin Clients/Laptops
- **eBay**: Search "Dell Wyse", "HP t640", "ThinkCentre Tiny"
- **Government surplus auctions**: Often sell bulk thin clients
- **Corporate IT refreshes**: Ask local businesses about decommissioned hardware
- **University surplus**: College IT often sells old lab machines
- **Craigslist/Facebook Marketplace**: Free or cheap old laptops

### What to Look For
- ✅ Powers on
- ✅ Has network port
- ✅ BIOS accessible (not locked)
- ❌ Dead battery (doesn't matter for stone use)
- ❌ Cracked screen (stone runs headless)
- ❌ Missing keycaps (stone runs headless)

**Key insight**: Hardware that's "broken" as a laptop (bad screen, dead battery) is often perfect as a stone.

---

## Open Ecosystem Commitment

Zen Garden will **never require** proprietary hardware.

- ✅ Reference designs published openly
- ✅ Community can build compatible devices
- ✅ No locked firmware or special activation
- ✅ Vendor partnerships encouraged but not required

If you can boot Linux, you can run a stone.

---

**Next**: [Stone Specifications](./stone-specs.md) | [USB Installer](../../installer/README.md) | [Getting Started](../guides/getting-started.md)
# Zen Garden: Hardware Reference

**Document Status**: Living Document  
**Last Updated**: January 14, 2026  
**Target Audience**: Hardware Engineers, DIY Builders, Manufacturing Partners

---

## Table of Contents

1. [Overview](#overview)
2. [Design Philosophy](#design-philosophy)
3. [Reference Designs](#reference-designs)
4. [Component Specifications](#component-specifications)
5. [Connection Modes](#connection-modes)
6. [LED & Button Specifications](#led--button-specifications)
7. [DIY Build Guides](#diy-build-guides)
8. [Testing & Certification](#testing--certification)

---

## Overview

Zen-garden hardware is **not a product**—it's a **reference design ecosystem**. All schematics, BOMs, and assembly instructions are published under Creative Commons (CC BY 4.0), enabling:

- **DIY Builds**: Community members build from e-waste, commodity components
- **Educational Use**: Universities/bootcamps use as teaching platform (STEM programs)
- **Commercial Manufacturing**: Third-party vendors manufacture compatible devices (no vendor exclusivity)
- **Regional Makers**: Local makers offer assembly services (no centralized vendor)

**Core Principle**: Any device that implements the zen-garden protocol is a valid Stone/Lantern—no vendor approval required.

---

## Design Philosophy

### 1. Commodity Components Only

**Mandate**: Zero vendor-specific chips or proprietary components.

**Approved Components**:
- ✅ Rockchip RK3588, Raspberry Pi CM4, x86 CPUs (Intel, AMD)
- ✅ Standard eMMC, NVMe M.2 SSDs, SATA drives
- ✅ TPM 2.0 modules (discrete, not fused)
- ✅ Standard PoE modules, USB-C power delivery
- ✅ Generic LEDs, tactile switches, capacitive touch sensors

**Prohibited**:
- ❌ Custom ASICs (prevents community replication)
- ❌ Vendor-locked crypto chips (defeats open architecture)
- ❌ Proprietary connectors (limits repairability)

---

### 2. Repairability First

**Requirements**:
- User-replaceable storage (M.2, SATA, eMMC on socket—not soldered BGA)
- Standard screws (Phillips/Torx, no security bits)
- Documented disassembly (photos, measurements, thermal pad specs)
- Spare parts availability (BOMs published, distributors listed)

**Repair Score Target**: iFixit 8/10 or higher (reference designs)

---

### 3. E-Waste Reuse Encouraged

**Philosophy**: Best Stone is one that doesn't create new e-waste.

**Recommended E-Waste Builds**:
- Old laptops (2010-2020): Remove screen, repurpose motherboard as Stone
- Thin clients (HP t730, Dell Wyse 5070): Add storage, flash zen-garden firmware
- Retired servers (Dell R210 II, HP DL320): Low-power compute Stones
- Gaming PCs (2015-2020): Remove GPU, use as high-capacity Stone

**Community Resources**: `awesome-zen-garden-hardware` GitHub repo (community-curated e-waste builds)

---

## Reference Designs

### Design 1: DIY Lantern (E-Waste Laptop)

**Cost**: $0-$50 (e-waste laptop + optional touchscreen)  
**Purpose**: Zero-cost Lantern for community adoption

**Components**:
- **Compute**: Any laptop 2010+ (dual-core CPU, 4GB RAM minimum)
- **Display**: Laptop's built-in screen OR cheap 7" HDMI touchscreen ($30)
- **Storage**: Laptop's existing drive (no additional storage needed for Lantern role)
- **Power**: Laptop's power adapter
- **Network**: Built-in Ethernet OR Wi-Fi

**Software**:
```bash
# Install Lantern software on Ubuntu/Debian
curl -fsSL https://zen-garden.dev/install-lantern.sh | bash

# Verify installation
garden-lantern status
# ✓ Service directory running (port 8080)
# ✓ Web UI accessible (http://192.168.1.x:8080)
```

**Build Time**: 30 minutes (software install + network configuration)

**Use Case**: First-time users, testing, educational labs

---

### Design 2: Raspberry Pi Lantern

**Cost**: ~$120 total  
**Purpose**: Minimal-cost reference design with touchscreen

**Bill of Materials**:
| Component | Model | Cost | Distributor |
|-----------|-------|------|-------------|
| Compute Module | Raspberry Pi 4 8GB | $75 | Adafruit, SparkFun |
| Display | Official 7" Touchscreen | $75 | Raspberry Pi Foundation |
| Case | SmartiPi Touch 2 | $35 | SmartiPi.com |
| Power | USB-C 3A PSU | $10 | Any |
| Storage | 32GB microSD (Class 10) | $8 | SanDisk, Samsung |
| **Total** | | **$203** | |

*Note: No TPM 2.0 (Raspberry Pi lacks hardware support). Pebble encryption uses CPU serial + MAC address.*

**Assembly Guide**:
1. Flash Raspberry Pi OS Lite (64-bit) to microSD
2. Assemble Pi + touchscreen + case (SmartiPi instructions)
3. Boot Pi → run Lantern installer script
4. Configure pond settings via touchscreen UI

**Build Time**: 2 hours (first time), 30 minutes (experienced)

**Use Case**: Home users wanting physical dashboard, secondary Lantern (HA)

---

### Design 3: DIY Stone (Raspberry Pi + USB SSD)

**Cost**: ~$80 total  
**Purpose**: Low-cost storage/compute Stone

**Bill of Materials**:
| Component | Model | Cost | Distributor |
|-----------|-------|------|-------------|
| Compute Module | Raspberry Pi 4 4GB | $55 | Adafruit, SparkFun |
| Storage | 500GB USB 3.0 SSD | $40 | Samsung T5, Crucial X6 |
| Case | Argon ONE M.2 Case | $30 | Argon40.com |
| Power | USB-C 3A PSU | $10 | Any |
| **Total** | | **$135** | |

*Note: Argon ONE M.2 enables direct M.2 SATA SSD attachment (faster than USB adapter).*

**Assembly Guide**:
1. Flash Raspberry Pi OS Lite to microSD (boot partition)
2. Attach M.2 SSD to Argon ONE case
3. Install case → boot Pi → mount SSD
4. Run Stone installer script:
```bash
curl -fsSL https://zen-garden.dev/install-stone.sh | bash
garden-stone announce --service mongodb --port 27017
```

**Build Time**: 1 hour

**Use Case**: MongoDB Stone, file storage Stone, cache Stone

---

### Design 4: Rockchip Stone (Compute Module)

**Cost**: ~$100-150 total  
**Purpose**: High-performance compute Stone (ARM64, low power)

**Bill of Materials**:
| Component | Model | Cost | Distributor |
|-----------|-------|------|-------------|
| Compute Module | Rockchip RK3588 (8-core ARM) | $80 | Radxa, FriendlyElec |
| Storage | 128GB eMMC module | $25 | Hardkernel, Radxa |
| PoE Module | IEEE 802.3af/at HAT | $20 | Waveshare, UCTRONICS |
| Case | Aluminum heatsink case | $15 | Generic (AliExpress) |
| TPM Module | Infineon SLB9670 (optional) | $10 | Mouser, DigiKey |
| **Total** | | **$150** | |

**Assembly Guide**:
1. Attach eMMC module to compute module socket
2. Install PoE HAT (if using PoE power)
3. Flash zen-garden firmware to eMMC:
```bash
# From Linux host
sudo dd if=zen-garden-rk3588.img of=/dev/sdX bs=4M status=progress
```
4. Boot Stone → configure via SSH or Lantern web UI

**Build Time**: 2 hours (firmware flashing + assembly)

**Use Case**: PostgreSQL Stone, AI inference Stone (8-core ARM), high-capacity storage

---

### Design 5: Backup Stone (Offsite Recovery)

**Cost**: ~$100-200 (varies by connection mode)  
**Purpose**: Automated disaster recovery (bound to Secure Pond, stored offsite)

**Bill of Materials** (Base):
| Component | Model | Cost | Distributor |
|-----------|-------|------|-------------|
| Compute Module | Raspberry Pi 4 4GB OR Rockchip RK3588 | $55-80 | Various |
| Storage | 1TB USB SSD (for backups) | $80 | Samsung T7, Crucial X6 |
| Case | Waterproof enclosure (optional) | $20 | Pelican-style (IP67) |
| Power | USB-C PSU | $10 | Any |
| **Base Total** | | **$165** | |

**Connection Mode Add-Ons**:
- **VPN** (Tailscale, Cloudflare Tunnel): $0 (software-only)
- **Meshtastic LoRa**: +$50 (LoRa module + antenna)
- **Cellular 4G/5G**: +$30 (USB modem) + data plan ($10-20/month)

**Assembly Guide**:
1. Build standard Stone (Design 3 or 4)
2. Configure Backup Stone role via Lantern web UI:
```
Lantern Web UI → Stones → Add Stone → [Select] Backup Stone
  Connection Mode: [VPN | LoRa | Cellular | Periodic]
  Offsite Location: [Parent's house | Safe deposit box | Neighbor's house]
  Sync Frequency: [Hourly | Daily | Weekly | On-connect]
```
3. Stone binds to Secure Pond → receives encrypted snapshots automatically

**Build Time**: 1.5 hours (base Stone) + 30 minutes (connection mode setup)

**Use Case**: Disaster recovery (house fire, flood), offsite backup (parent's house), long-range sync (Meshtastic LoRa)

---

## Component Specifications

### CPU/Compute Requirements

**Minimum** (Stone):
- Architecture: ARM64 or x86-64
- Cores: 2 (quad-core recommended)
- Clock: 1.5 GHz base
- RAM: 2GB (4GB recommended)

**Minimum** (Lantern):
- Architecture: ARM64 or x86-64
- Cores: 4 (for web UI + service directory)
- Clock: 2.0 GHz base
- RAM: 4GB (8GB recommended for 50+ Stones)

**Tested Platforms**:
- ✅ Raspberry Pi 4 (4GB/8GB)
- ✅ Rockchip RK3588 (8-core ARM)
- ✅ Intel N100 (x86, low-power)
- ✅ AMD Ryzen (desktop/server)

---

### Storage Requirements

**Stone (Data)**:
- Capacity: 128GB minimum (1TB+ for video/backups)
- Interface: USB 3.0, SATA III, NVMe M.2
- Endurance: Consumer-grade acceptable (TBW >150 for 256GB SSD)

**Stone (Pebble Partition)**:
- Capacity: 4MB (dedicated partition for binding keys)
- Interface: Same as data partition (eMMC/SSD)
- Encryption: Mandatory (AES-256-GCM with hardware key derivation)

**Lantern**:
- Capacity: 32GB minimum (service directory metadata, logs)
- Interface: microSD, eMMC, SSD

---

### Network Requirements

**Minimum**:
- Ethernet: 100 Mbps (Gigabit recommended)
- Wi-Fi: 802.11n (802.11ac recommended)
- mDNS: Enabled (Avahi, Bonjour, mdns-repeater)

**PoE** (Optional):
- Standard: IEEE 802.3af (12.95W) or 802.3at (25.5W)
- Use Case: Cable runs >15 feet, rack-mounted Stones

---

### TPM Requirements

**Optional** (Phase 5 - Secure Pond):
- Version: TPM 2.0 (discrete module or firmware TPM)
- Interface: SPI, I2C, or LPC
- Use Case: Hardware-backed pebble encryption (preferred over CPU serial key derivation)

**Fallback** (No TPM):
- Key derivation from CPU serial + MAC address (HKDF-SHA256)
- Security: Lower (physical Stone theft → key extraction possible with specialized equipment)

---

## Connection Modes

### Overview

Backup Stones support five connection modes for offsite disaster recovery. User selects mode during Secure Pond setup based on location and network availability.

---

### Mode 1: Periodic Sync (Manual)

**Description**: User brings Backup Stone home periodically (monthly, quarterly), plugs into network, automatic sync triggers.

**Cost**: $0 (no additional hardware)

**Network**: None (only when physically connected)

**Use Case**: Safe deposit box, parent's house (visit quarterly)

**Configuration**:
```yaml
# Backup Stone config
connection_mode: periodic
sync_on_connect: true
data_retention: 90_days  # Keep 90 days of snapshots
```

**User Experience**:
```
1. User visits bank (quarterly)
2. Retrieves Backup Stone from safe deposit box
3. Plugs into network at home
4. Lantern detects Backup Stone → auto-sync starts
5. LED: Amber pulse (syncing), Pulsing green (complete)
6. User returns Backup Stone to bank
```

---

### Mode 2: VPN (Tailscale, WireGuard, Cloudflare Tunnel)

**Description**: Backup Stone connects to home network via VPN tunnel (internet-based).

**Cost**: $0 (software-only, most VPN services have free tiers)

**Network**: Broadband internet at offsite location

**Use Case**: Parent's house (has internet), office desk, second home

**Configuration**:
```yaml
# Backup Stone config
connection_mode: vpn
vpn_type: tailscale  # or wireguard, cloudflare_tunnel
vpn_config:
  auth_key: "tskey-auth-xxx..."
  hostname: "backup-stone-01"
sync_frequency: hourly
```

**Setup**:
```bash
# On Backup Stone
garden-backup configure --mode vpn --type tailscale
# Follow Tailscale login flow → device joins tailnet
# Backup Stone connects to home Lantern via VPN

# Verify connection
garden-backup status
# ✓ VPN connected (latency: 45ms)
# ✓ Last sync: 2 hours ago
# ✓ Backed up: 234 GB
```

---

### Mode 3: Cloudflare Tunnel (Zero-Trust)

**Description**: Cloudflare Tunnel creates secure connection without exposing home IP (no port forwarding).

**Cost**: $0 (Cloudflare free tier: 1 tunnel, unlimited traffic)

**Network**: Internet at offsite location

**Use Case**: Offsite location with NAT/firewall restrictions (no port forwarding possible)

**Configuration**:
```yaml
# Backup Stone config
connection_mode: cloudflare_tunnel
cloudflare_config:
  tunnel_id: "xxx-yyy-zzz"
  tunnel_token: "tunnel_token_here"
  target: "lantern.home.local:8080"
sync_frequency: hourly
```

**Setup**:
```bash
# On Lantern (home)
cloudflared tunnel create backup-tunnel
cloudflared tunnel route dns backup-tunnel backup.home.example.com

# On Backup Stone (offsite)
garden-backup configure --mode cloudflare \
  --tunnel-token "xxx" \
  --target "backup.home.example.com"
```

---

### Mode 4: Meshtastic LoRa (Long-Range, No Internet)

**Description**: LoRa radio (5-10 miles range) connects Backup Stone to home without internet.

**Cost**: ~$50 (LoRa module: Heltec V3, LILYGO T-Beam)

**Network**: None (direct LoRa mesh)

**Use Case**: Rural property, neighbor's house (within 5-10 miles), cabin

**Hardware**:
- LoRa Module: 915 MHz (North America), 868 MHz (Europe), 433 MHz (Asia)
- Antenna: 3dBi omni (included) or 5dBi directional (better range)
- Enclosure: Waterproof (IP67) for outdoor mounting

**Configuration**:
```yaml
# Backup Stone config
connection_mode: lora_mesh
lora_config:
  frequency: 915_000_000  # 915 MHz
  bandwidth: 125_000      # 125 kHz
  spreading_factor: 10    # SF10 (trade-off: range vs speed)
  tx_power: 20            # 20 dBm (100 mW)
sync_frequency: daily     # LoRa limited bandwidth (200-1000 bps)
```

**Data Rate**:
- **SF7** (short range): ~5,470 bps → 47 KB/minute (city, 1-2 miles)
- **SF10** (long range): ~980 bps → 7 KB/minute (rural, 5-10 miles)
- **Incremental sync**: Only changed blocks transmitted (efficient for daily backups)

**Setup**:
```bash
# On Lantern (home)
garden-lora configure --role gateway --frequency 915

# On Backup Stone (offsite)
garden-backup configure --mode lora \
  --frequency 915 \
  --gateway "lantern-lora-01"

# Test connection
garden-lora ping "backup-stone-01"
# Pong received (latency: 250ms, RSSI: -85 dBm)
```

**Use Case Example**:
```
Scenario: User lives in suburban neighborhood, parent lives 3 miles away

1. Mount LoRa antenna on roof (both locations)
2. Configure Backup Stone with LoRa mode
3. Place Backup Stone at parent's house
4. Daily incremental sync: 100 MB changed data = ~2 hours transfer (overnight)
5. Cost: $0/month (no internet bill), one-time $100 (2x LoRa modules)
```

---

### Mode 5: Cellular (4G/5G)

**Description**: USB cellular modem connects Backup Stone to internet via cellular network.

**Cost**: ~$30 (USB modem) + $10-20/month (data plan)

**Network**: Cellular coverage

**Use Case**: Remote cabin, RV, boat, locations without broadband

**Hardware**:
- USB Modem: Huawei E3372, Sierra Wireless MC7455
- SIM Card: Data-only plan (1-10 GB/month)
- Antenna: External (optional, improves signal in rural areas)

**Configuration**:
```yaml
# Backup Stone config
connection_mode: cellular
cellular_config:
  apn: "wireless.twilio.com"  # Carrier APN
  pin: "1234"                 # SIM PIN (if enabled)
  data_limit: 5_000_000_000   # 5 GB/month (prevents overage)
sync_frequency: daily         # Cellular data costs → sync overnight
```

**Setup**:
```bash
# On Backup Stone
garden-backup configure --mode cellular \
  --apn "wireless.twilio.com" \
  --data-limit 5GB

# Monitor data usage
garden-backup data-usage
# ✓ This month: 2.3 GB / 5 GB
# ✓ Last sync: 4 hours ago (450 MB transferred)
# ✓ Estimated monthly: 3.8 GB
```

**Cost Example**:
```
Scenario: 500 GB home storage, 50 GB changes/month (incremental backups)

- Data plan: $20/month (10 GB)
- Transfer: 50 GB ÷ 30 days = 1.7 GB/day (within limit if daily sync)
- Alternative: Weekly sync (7x larger transfers, but 4x per month = fits 10 GB limit)
```

---

## LED & Button Specifications

### LED States (Reference Design)

**LED Type**: Single RGB LED (WS2812B or equivalent)

| Color | Pattern | Meaning | Example Scenario |
|-------|---------|---------|------------------|
| **Solid Blue** | Constant | Virgin Stone (unbound) | Fresh from factory |
| **Pulsing Green** | Slow pulse (2s period) | Bound to this pond, healthy | Normal operation |
| **Red Blink (slow)** | 1 Hz | Bound to other pond | Work Stone at home |
| **Amber Pulse** | Medium pulse (1s) | Binding in progress | Virgin Stone joining Secure Pond |
| **Red Blink (fast)** | 5 Hz | Factory reset countdown | User holding button |
| **White Strobe** | Rapid flash | Diagnostic mode | Troubleshooting |
| **Off** | No light | Powered off OR LED disabled | User preference |

**GPIO Configuration** (Raspberry Pi example):
```python
# GPIO 18 (PWM) for WS2812B LED
import board
import neopixel

led = neopixel.NeoPixel(board.D18, 1, brightness=0.3)

# Pulsing green (bound, healthy)
while True:
    for brightness in range(0, 255, 5):
        led[0] = (0, brightness, 0)
        time.sleep(0.01)
    for brightness in range(255, 0, -5):
        led[0] = (0, brightness, 0)
        time.sleep(0.01)
```

---

### Physical Button (Factory Reset)

**Button Type**: Recessed tactile switch (SPST, momentary)

**Specifications**:
- Actuation Force: 160-200 gf (gram-force)
- Travel: 0.25-0.5 mm
- Lifecycle: 100,000 cycles minimum
- Mounting: PCB through-hole or SMD
- Accessibility: Recessed 3mm (requires paperclip/SIM tool to prevent accidental presses)

**Behavior**:
| Hold Duration | Action | LED Feedback |
|---------------|--------|--------------|
| <3 seconds | Ignore (accidental press) | No change |
| 3-9 seconds | Enter reset mode | Slow red blink |
| 10-19 seconds | **Data wipe** (pebble preserved) | Fast red blink |
| 20+ seconds | **Factory reset** (pebble destroyed) | Rapid red strobe |

**GPIO Configuration** (Raspberry Pi example):
```python
# GPIO 17 for physical button
import RPi.GPIO as GPIO
import time

GPIO.setmode(GPIO.BCM)
GPIO.setup(17, GPIO.IN, pull_up_down=GPIO.PUD_UP)

def on_button_press():
    press_time = time.time()
    while GPIO.input(17) == GPIO.LOW:  # Button held
        duration = time.time() - press_time
        
        if duration < 3:
            pass  # Ignore
        elif 3 <= duration < 10:
            set_led('red_slow_blink')
        elif 10 <= duration < 20:
            set_led('red_fast_blink')
            if duration >= 10 and not data_wipe_triggered:
                trigger_data_wipe()
        elif duration >= 20:
            set_led('red_strobe')
            if not factory_reset_triggered:
                trigger_factory_reset()
                break

GPIO.add_event_detect(17, GPIO.FALLING, callback=on_button_press, bouncetime=200)
```

---

### Capacitive Touch (Alternative to Physical Button)

**Advantage**: No mechanical wear, longer lifecycle (unlimited touches)

**Component**: TTP223 capacitive touch sensor IC

**Configuration**:
- Sensitivity: Adjustable (1-10 mm detection distance)
- Output: Active-high GPIO signal
- Power: 3.3V or 5V
- Cost: $0.50 per unit

**Use Case**: Premium reference designs (Rockchip Stone, commercial builds)

---

## DIY Build Guides

### Guide 1: E-Waste Laptop Lantern (30 Minutes)

**Materials**:
- Old laptop (2010+, working motherboard)
- USB flash drive (16GB+) OR use laptop's existing drive
- Ethernet cable OR Wi-Fi adapter

**Steps**:
1. **Boot laptop** into Ubuntu/Debian (live USB or existing OS)
2. **Install Lantern**:
```bash
wget -qO- https://zen-garden.dev/install-lantern.sh | sudo bash
```
3. **Configure network**:
```bash
# If using Ethernet: DHCP auto-configuration (nothing needed)
# If using Wi-Fi:
sudo nmcli dev wifi connect "YOUR_SSID" password "YOUR_PASSWORD"
```
4. **Verify installation**:
```bash
garden-lantern status
# ✓ Service directory running on http://192.168.1.x:8080
# ✓ mDNS announcement: _pond._tcp.local.
```
5. **Access web UI**: Open browser → `http://192.168.1.x:8080` → Configure pond settings

**Troubleshooting**:
- **Issue**: "mDNS not working"  
  **Fix**: `sudo systemctl restart avahi-daemon`
  
- **Issue**: "Web UI not accessible"  
  **Fix**: Check firewall: `sudo ufw allow 8080/tcp`

---

### Guide 2: Raspberry Pi Storage Stone (1 Hour)

**Materials**:
- Raspberry Pi 4 (4GB)
- 500GB USB SSD (Samsung T5, Crucial X6)
- microSD card (32GB, Class 10)
- USB-C power adapter (3A)

**Steps**:
1. **Flash Raspberry Pi OS Lite** (64-bit) to microSD:
```bash
# From Linux/macOS/Windows host
curl -L https://downloads.raspberrypi.org/raspios_lite_arm64/images/... -o rpi-lite.img
sudo dd if=rpi-lite.img of=/dev/sdX bs=4M status=progress
```
2. **Boot Pi** → Enable SSH:
```bash
# On Pi's boot partition (before first boot)
touch /boot/ssh
```
3. **Connect Pi** to network (Ethernet recommended), SSH in:
```bash
ssh pi@192.168.1.x  # Default password: raspberry
```
4. **Attach USB SSD** → Mount:
```bash
# Format SSD (if new)
sudo mkfs.ext4 /dev/sda1

# Mount persistently
sudo mkdir /mnt/stone-data
echo "/dev/sda1 /mnt/stone-data ext4 defaults 0 2" | sudo tee -a /etc/fstab
sudo mount -a
```
5. **Install Stone software**:
```bash
curl -fsSL https://zen-garden.dev/install-stone.sh | sudo bash
```
6. **Announce MongoDB** (example):
```bash
# Install MongoDB
sudo apt install mongodb

# Configure MongoDB to use Stone storage
sudo nano /etc/mongodb.conf
# Change: dbpath=/mnt/stone-data/mongodb

# Restart MongoDB
sudo systemctl restart mongodb

# Announce service
garden-stone announce --service mongodb --port 27017
```
7. **Verify**:
```bash
# On any device on same network
avahi-browse -rt _mongodb._tcp
# Should see: mongodb-stone-01._mongodb._tcp.local
```

---

## Testing & Certification

### Compliance Testing

**Required Tests** (before production):
1. **EMI/EMC**: FCC Part 15 Class B (USA), CE (Europe)
2. **Safety**: UL/CSA (USA/Canada), IEC 60950-1 (international)
3. **PoE**: IEEE 802.3af/at compliance (if using PoE)

**Cost**: $5,000-15,000 per device family (one-time)

**Recommendation**: DIY builds exempt (personal use). Commercial manufacturers must certify.

---

### Community Certification Program

**Proposed** (Phase 3+):
- Community members test reference designs, submit results
- "Zen Garden Certified" badge for tested builds (GitHub wiki)
- Test criteria:
  - mDNS discovery: 100% success rate (10 trials)
  - Stone uptime: >99% (7-day test)
  - Thermal: <85°C under load (ambient 25°C)

**Incentive**: Recognition, wiki listing, community support

---

## Related Documents

- [README.md](./README.md) - Project overview
- [ROADMAP.md](./ROADMAP.md) - Development timeline
- [STRATEGY.md](./STRATEGY.md) - Business case
- [TECHNICAL-REFERENCE.md](./TECHNICAL-REFERENCE.md) - API documentation
- [STONE-BINDING-SECURITY-EVALUATION.md](./STONE-BINDING-SECURITY-EVALUATION.md) - Security architecture

---

**Document Version**: 1.0  
**Last Reviewed**: January 14, 2026  
**Community Contributions**: Submit pull requests to update BOMs, add new reference designs, or correct specifications.

**License**: This document is licensed under Creative Commons Attribution 4.0 International (CC BY 4.0). All hardware schematics, BOMs, and assembly instructions are CC BY 4.0. Software components remain MIT licensed.

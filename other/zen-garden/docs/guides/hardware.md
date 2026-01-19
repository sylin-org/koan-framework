---
audience: [operator, visitor, contributor]
doc_type: reference
status: current
last_verified: 2026-01-19
canonical: true
note: "Authoritative hardware guide for Stone selection and setup. Covers e-waste reframing philosophy, tier system ($0-250), service-to-hardware matching, shopping lists, and environmental impact calculations."
related:
  - MISSION.md
  - UNDERSTANDING.md
  - STORIES.md
---

# Hardware Guide

**Turn old devices into productive Stones.**

---

## Philosophy

**E-waste reframing:**
- "Too slow for desktop" ≠ "useless"
- 2015 laptop insufficient for video editing, sufficient for MongoDB (1,000+ req/sec)
- Thin client can't run Windows 11, can run Redis cache
- Old desktop obsolete for gaming, excellent for storage (large drives)

**Target devices:**
- Decommissioned corporate laptops (5-8 years old)
- Outdated thin clients (Wyse, HP t520, Dell OptiPlex Micro)
- Personal upgrades (replaced but functional)
- Failed screen laptops (display broken, internals fine)

**Environmental impact:**
- Extend lifespan 3-5 years (delay manufacturing demand)
- 10 devices = ~3 tonnes CO2 avoided (300kg per device manufacturing)
- Keep functional hardware from landfills

---

## Stone Tiers

### Tier 1: Minimal ($0-30)

**Hardware you likely already own:**
- Laptop (2012-2018, any condition)
- USB drive (8GB+, for Debian installer)
- Ethernet cable (optional but recommended)

**Use cases:**
- MongoDB Stone (small databases <10GB)
- Redis cache (up to 2GB)
- Development/learning

**Setup:**
1. Create bootable USB with NewStone.ps1
2. Boot laptop from USB
3. Install Debian + service
4. Configure announcement

**Power consumption:** 15-35W (laptop idle + service load)  
**Cost per year:** $13-30 electricity @ $0.10/kWh

---

### Tier 2: Dedicated ($30-100)

**Sourced hardware:**
- Used thin client (eBay/Craigslist, $30-60)
- Power supply included
- Ethernet cable
- Optional: SSD upgrade (120GB, $20-40)

**Advantages:**
- Lower power (10-20W typical)
- Silent operation (fanless designs)
- Small form factor (stackable)
- Always-on reliability

**Use cases:**
- Production MongoDB (up to 50GB)
- PostgreSQL (medium databases)
- MinIO storage (100GB-1TB)
- RabbitMQ messaging

**Recommended models:**
- HP t520/t620 (AMD GX-415GA, 4GB RAM, $40-60)
- Dell Wyse 5070 (Intel Celeron J4105, 8GB RAM, $80-100)
- Lenovo ThinkCentre Tiny (various, $60-100)

**Power consumption:** 10-20W  
**Cost per year:** $9-18 electricity

---

### Tier 3: Robust ($100-250)

**New hardware for reliability:**
- Intel NUC or equivalent (refurbished, $150-200)
- RAM upgrade (16-32GB, $30-80)
- SSD storage (500GB-1TB, $40-80)
- UPS (battery backup, optional $60-100)

**Advantages:**
- Warranty (if new/refurbished)
- Modern efficiency (10-15W idle)
- ECC RAM option (data integrity)
- Better thermals (sustained load)

**Use cases:**
- Production databases (100GB+)
- High-throughput services (10K+ req/sec)
- Ollama LLM inference (requires 16GB+ RAM)
- MinIO storage (multi-TB)

**Recommended models:**
- Intel NUC 11/12 (refurbished, $150-200)
- Beelink Mini PC (AMD Ryzen, $180-250)
- Lenovo ThinkCentre M75q (Ryzen, $200-250)

**Power consumption:** 10-25W (load dependent)  
**Cost per year:** $9-22 electricity

---

## Shopping List

### Essential

**USB Installer (one-time):**
- USB 3.0 drive, 8GB+ ($8-15)
- Used: Check drawer, probably already own one

**Network:**
- Ethernet cables ($5-10 each)
- Gigabit switch if needed (8-port, $20-40)

**Power:**
- Power strip with surge protection ($15-30)
- Optional: UPS for uptime (500VA, $60-100)

### Storage Options

**Internal (preferred for databases):**
- 2.5" SATA SSD, 120GB ($18-25)
- 2.5" SATA SSD, 500GB ($35-50)
- M.2 NVMe SSD, 500GB ($40-60)

**External (for MinIO/bulk storage):**
- USB 3.0 external HDD, 2TB ($50-70)
- USB 3.0 external HDD, 4TB ($80-100)

### Cooling

**Usually unnecessary but available:**
- Laptop cooling pad ($15-30) - only if overheating
- Thermal paste reapplication (free, DIY)

---

## Service Type → Hardware Matching

### Light Services (Any Tier)

**Redis cache:**
- Minimum: 2GB RAM, any CPU
- Storage: Minimal (in-memory)
- Example: HP t520 thin client ($40)

**RabbitMQ messaging:**
- Minimum: 2GB RAM, dual-core CPU
- Storage: 10GB+
- Example: 2015 laptop with SSD

**Ollama (small models <7B params):**
- Minimum: 8GB RAM, quad-core CPU
- Storage: 50GB+ (model weights)
- Example: Dell Wyse 5070 with RAM upgrade

### Medium Services (Tier 2+)

**MongoDB:**
- Recommended: 4GB RAM, dual-core CPU
- Storage: 100GB+ SSD
- Example: Lenovo ThinkCentre Tiny M710q

**PostgreSQL:**
- Recommended: 4GB RAM, dual-core CPU
- Storage: 100GB+ SSD
- Example: Intel NUC 8 (refurbished)

**MinIO storage:**
- Recommended: 4GB RAM, any CPU
- Storage: 500GB+ HDD/SSD
- Example: Old desktop with large drives

### Heavy Services (Tier 3)

**Ollama (large models 13B+ params):**
- Required: 16GB+ RAM, quad-core CPU
- Storage: 100GB+ SSD
- Example: Intel NUC 11 with RAM upgrade

**SQL Server:**
- Recommended: 8GB+ RAM, quad-core CPU
- Storage: 200GB+ SSD
- Example: Beelink Mini PC (AMD Ryzen)

**High-traffic PostgreSQL:**
- Recommended: 16GB RAM, quad-core CPU
- Storage: 500GB+ NVMe SSD
- Example: Lenovo ThinkCentre M75q Gen 2

---

## Installation Process

### Step 1: Create Bootable USB

**Windows:**
```powershell
# Download NewStone.ps1
Invoke-WebRequest -Uri https://zen-garden.dev/NewStone.ps1 -OutFile NewStone.ps1

# Run installer (requires admin)
.\NewStone.ps1 -Drive E: -Hostname stone-01

# Output: Bootable Debian USB ready
```

**Linux/macOS:**
```bash
# Download ISO
wget https://zen-garden.dev/zen-garden-debian.iso

# Write to USB (replace /dev/sdX with your USB device)
sudo dd if=zen-garden-debian.iso of=/dev/sdX bs=4M status=progress
sync
```

### Step 2: Boot Device from USB

1. Insert USB into target device
2. Restart device
3. Enter boot menu (typically F12, F2, or ESC during startup)
4. Select USB drive
5. Boot into Debian installer

### Step 3: Install Debian

**Automated installer:**
- Selects defaults (entire disk, automatic partitioning)
- Installs minimal system (no GUI)
- Configures SSH access
- Sets hostname from USB label

**Manual configuration:**
- Username: `stone`
- SSH enabled by default (key-based auth recommended)
- Network: DHCP automatic

### Step 4: Install Service

```bash
# SSH into Stone
ssh stone@stone-01.local

# Install MongoDB
sudo apt update
sudo apt install mongodb-server

# Install Stone announcer
curl -sSL https://zen-garden.dev/install.sh | bash
garden-rake announce mongodb

# Output:
# [stone] announcing mongodb service
# [stone] discoverable as zen-garden:mongodb
```

**Service starts automatically on boot.**

---

## Power Management

### Idle Optimization

**Laptop Stones:**
```bash
# Disable screen (save power)
sudo vbetool dpms off

# Close lid without sleeping
# Edit /etc/systemd/logind.conf
HandleLidSwitch=ignore

# Restart service
sudo systemctl restart systemd-logind
```

**Thin Clients:**
- Typically optimized by default (10-15W idle)
- BIOS settings: Enable wake-on-LAN if needed

### Power Consumption Measurement

```bash
# Install powertop (Linux)
sudo apt install powertop
sudo powertop

# Shows current wattage and optimization suggestions
```

**Expected ranges:**
- Laptop idle: 10-20W
- Laptop + light service: 15-30W
- Laptop + heavy service: 25-45W
- Thin client idle: 5-10W
- Thin client + service: 10-20W

### Cost Calculation

```
Annual cost = (Watts × 24 hours × 365 days ÷ 1000) × $/kWh

Example:
- Device: 20W average
- Rate: $0.10/kWh
- Cost: (20 × 24 × 365 ÷ 1000) × 0.10 = $17.52/year
```

**Compare to cloud:**
- Managed MongoDB (AWS): $50-100/month = $600-1,200/year
- Self-hosted Stone: $15-30/year
- **Savings: $570-1,170/year**

---

## Physical Setup

### Placement

**Ideal conditions:**
- Ventilated space (not enclosed cabinets)
- Elevated surface (off floor, dust reduction)
- Near network switch (wired Ethernet preferred)
- Accessible for maintenance (monitor LED status)

**Avoid:**
- Direct sunlight (heat buildup)
- High humidity (basements without dehumidifier)
- Unstable surfaces (vibration damages HDDs)

### Cable Management

```
Example setup:
├─ Power strip (surge protected)
│  ├─ Stone 1 (MongoDB)
│  ├─ Stone 2 (Redis)
│  └─ Stone 3 (Storage)
│
├─ Network switch (8-port gigabit)
│  ├─ Uplink to router
│  ├─ Stone 1 (blue cable = database)
│  ├─ Stone 2 (orange cable = cache)
│  └─ Stone 3 (green cable = storage)
│
└─ Optional: Label devices (blue tape, permanent marker)
```

**Color-coding helps visual debugging:**
- Blue = Database Stones (MongoDB, PostgreSQL)
- Orange = Cache Stones (Redis, Memcached)
- Green = Storage Stones (MinIO, NFS)

### Labeling

**Physical labels:**
- Service type (MongoDB, Redis, etc.)
- Hostname (stone-01, stone-02)
- IP address (if static, optional with mDNS)
- Date commissioned

**Sticker/tape on device + printed label on network port**

---

## Maintenance

### Monthly Checks

```bash
# Disk usage
df -h

# Memory usage
free -h

# Service status
sudo systemctl status mongodb

# System updates
sudo apt update && sudo apt upgrade -y
```

### Storage Monitoring

**Alert thresholds:**
- 80% full: Monitor more frequently
- 90% full: Plan expansion
- 95% full: Urgent action required

**Expansion options:**
1. Add external USB drive
2. Replace internal drive with larger SSD
3. Migrate database to different Stone

### Thermal Monitoring

```bash
# Install sensors
sudo apt install lm-sensors
sudo sensors-detect

# Check temperatures
sensors

# Typical safe ranges:
# CPU: <80°C under load
# HDD: <50°C
# SSD: <70°C
```

**If overheating:**
1. Clean dust from vents (compressed air)
2. Add cooling pad (laptops)
3. Reduce ambient temperature
4. Migrate heavy service to better-cooled device

---

## Environmental Impact

### Device Repurposing Calculation

**Single device:**
- Manufacturing CO2: ~300kg (laptop/thin client avg)
- Extended lifespan: 3-5 years
- E-waste prevented: ~2-3kg device weight
- Cloud alternative avoided: ~50-100kg CO2/year (data center + transport)

**10-device garden:**
- Manufacturing avoided: ~3,000kg CO2
- Annual cloud offset: 500-1,000kg CO2
- **Total over 5 years: ~5,500kg CO2 saved**

### Disposal (Eventually)

**When device truly fails:**
1. Remove storage drives (data security)
2. Locate e-waste recycling center (e-Stewards certified)
3. Document repurposing journey (years extended)

**Recycling partners:**
- North America: Call2Recycle, e-Stewards network
- Brazil: Green Eletron
- Latin America: RELAC regional programs
- Europe: WEEE compliance take-back

---

## Further Reading

- [Getting Started](GETTING-STARTED.md) - Software setup on Stone
- [Understanding](UNDERSTANDING.md) - Discovery protocol details
- [Mission](MISSION.md) - E-waste impact and environmental goals
- [Stories](STORIES.md) - Real-world device repurposing examples

---
audience: [visitor, operator]
doc_type: guide
status: current
last_verified: 2026-01-19
canonical: false
note: "Beginner-friendly path from zero to first working Stone. Step-by-step with explanations, troubleshooting, and next steps. Designed for GitHub visitors evaluating Zen Garden."
related:
  - README.md
  - UNDERSTANDING.md
  - HARDWARE.md
  - MISSION.md
---

# Start Here - Your First Stone in 30 Minutes

**Complete beginner path**: understand the problem → see the solution → run it yourself → explore deeper.

---

## Step 1: Understand the Problem (2 minutes)

### Why Zen Garden Exists

You have old hardware sitting in a closet. A 2015 laptop "too slow" for video editing but perfectly capable of running MongoDB. The problem isn't the hardware—it's **coordination**.

**Traditional self-hosting:**
```bash
# Your app's config.env
MONGODB_URI=mongodb://old-laptop-01.local:27017
REDIS_URL=redis://thin-client-02.local:6379
```

**When old-laptop-01's hard drive dies:**
1. Deploy replacement laptop
2. Choose: rename new machine to `old-laptop-01` (complex networking) OR update every app's config (error-prone)
3. Repeat for every machine failure

**This is why people give up and pay $100/month for cloud databases.**

### Zen Garden's Solution

```bash
# Connection strings reference SERVICES, not MACHINES
MONGODB_URI=zen-garden:mongodb/mydb
```

- Stone announces: "I offer MongoDB"
- App discovers: "Who has MongoDB?"
- Connection: Automatic

When hardware fails, swap in a replacement. Apps reconnect automatically. No config updates.

---

## Step 2: See It Work (5 minutes)

### Prerequisites

- **Docker** installed ([get Docker](https://docs.docker.com/get-docker/))
- **Any OS** (Linux, macOS, Windows with WSL2)
- **Two terminals** open

### Docker Stone Demo

**Terminal 1 - Start a MongoDB Stone:**
```bash
# This would be your old laptop, but we're using Docker for demo
docker run -d \
  -p 27017:27017 \
  --name mongo-stone \
  -e ANNOUNCE_SERVICE=mongodb \
  -e STONE_NAME=demo-stone-01 \
  zen-garden/stone:latest
```

**What this does:**
- Starts MongoDB in a container
- Announces "I offer MongoDB" via mDNS (multicast DNS)
- Listens on port 27017 (MongoDB's default port)

**Terminal 2 - Watch discovery in action:**
```bash
# Install garden-rake CLI (Rust binary)
# Option 1: Pre-built binary
curl -L https://github.com/sylin/zen-garden/releases/latest/download/garden-rake-linux-x64 -o garden-rake
chmod +x garden-rake

# Option 2: Build from source (requires Rust)
git clone https://github.com/sylin/zen-garden.git
cd zen-garden/src/rake
cargo build --release
cp target/release/garden-rake ~/bin/

# Discover all Stones on your network
garden-rake discover

# Expected output:
# Stone: demo-stone-01
#   Services: mongodb
#   Address: 192.168.1.42:27017
#   Status: healthy
```

**Connect from your application:**
```javascript
// Node.js example
const { MongoClient } = require('mongodb');

// Connection string never changes, even when hardware is replaced
const uri = process.env.MONGODB_URI || 'zen-garden:mongodb/myapp';

const client = new MongoClient(uri);
await client.connect();
console.log('Connected to MongoDB via Zen Garden discovery');
```

### What Just Happened?

1. **Stone announced** via mDNS (same protocol as AirPlay, Chromecast)
2. **Rake discovered** by listening for mDNS announcements
3. **App connected** by resolving `zen-garden:mongodb` to actual endpoint

**Zero configuration files. No IP addresses. No DNS setup.**

---

## Step 3: Try It On Real Hardware (15 minutes)

### Hardware Options

**Tier 1 - Free ($0-30):**
- Old laptop (2010-2015 era, any condition)
- Decommissioned thin client
- Unused desktop with failing display

**Tier 2 - Budget ($30-100):**
- Raspberry Pi 4 (4GB RAM)
- Used enterprise thin client (eBay: $40-80)
- Refurbished mini PC

**Tier 3 - Performance ($100-250):**
- Intel NUC (used: $150-200)
- Raspberry Pi 5 (8GB RAM)
- Budget mini PC with SSD

See [Hardware Guide](HARDWARE.md) for detailed specs, power consumption, and service-to-hardware matching.

### USB Stone Installer (Debian-based)

**Create bootable USB:**
```powershell
# Windows - Run from PowerShell as Administrator
cd installer
.\NewStone.ps1 -UsbDrive D: -StoneName "blue-stone"

# This creates a bootable Debian USB with:
# - Garden-Moss daemon (pre-configured)
# - Docker + Docker Compose
# - Service templates (mongodb, redis, postgresql, etc.)
# - Auto-start on boot
```

**Boot target device from USB:**
1. Insert USB into old laptop/device
2. Boot from USB (usually F12 or DEL to access boot menu)
3. Wait 2-5 minutes for first boot setup
4. Device announces itself as a Stone

**Verify Stone is online:**
```bash
# From any machine on the same network
garden-rake discover

# Should show your new Stone:
# Stone: blue-stone
#   Services: (none yet)
#   Address: 192.168.1.42:7185
#   Status: healthy
```

### Deploy Your First Service

```bash
# Deploy MongoDB to blue-stone
garden-rake offer mongodb --to blue-stone

# Expected output:
# Deploying mongodb to blue-stone...
# Pulling image: mongo:7
# Starting container...
# Service healthy: mongodb on blue-stone:27017
```

**What happened:**
1. Rake sent HTTP request to Moss daemon on blue-stone (port 7185)
2. Moss updated `docker-compose.yml` with mongodb service
3. Moss ran `docker compose up -d`
4. Stone announced "I now offer MongoDB" via mDNS

**Your app connects:**
```bash
# Add to your app's .env
MONGODB_URI=zen-garden:mongodb/myapp

# App discovers blue-stone automatically
# No IP addresses, no config updates when hardware changes
```

---

## Step 4: Understand What You Built (5 minutes)

### Architecture You Just Created

```
Your Network (192.168.1.0/24)
│
├─ blue-stone (192.168.1.42)
│  ├─ Garden-Moss daemon (port 7185)  ← HTTP API for management
│  ├─ MongoDB container (port 27017)   ← Your database
│  └─ mDNS announcer                   ← "I offer mongodb"
│
└─ Your laptop (192.168.1.10)
   ├─ garden-rake CLI                  ← Send commands
   ├─ mDNS listener                    ← Discover stones
   └─ Your app                         ← Connect to zen-garden:mongodb
```

### Key Concepts

**Stone** = Physical device offering services  
- Can be laptop, desktop, Raspberry Pi, thin client
- Runs Garden-Moss daemon
- Announces services via mDNS

**Moss** = Daemon on each Stone  
- HTTP API (port 7185)
- Manages Docker Compose services
- Announces service availability

**Rake** = CLI tool for management  
- Discover Stones: `garden-rake discover`
- Deploy services: `garden-rake offer <service> --to <stone>`
- Monitor health: `garden-rake watch`

**Connection String** = Stable resource reference  
- Format: `zen-garden:<service-type>[/<database>]`
- Never changes, even when hardware swaps
- Resolves to native protocol (e.g., `mongodb://...`)

### Discovery Flow (Technical)

```
1. App needs MongoDB
   → Queries mDNS for "_koan-stone._tcp.local."

2. All Stones respond with TXT records
   → blue-stone: "offering=mongodb port=27017 health=healthy"

3. App connects to native protocol
   → mongodb://blue-stone:27017
```

**Why mDNS?** 20+ years proven (AirPlay, Chromecast, network printers). Built into macOS, Linux (Avahi). No central server, no single point of failure.

---

## Step 5: Add More Services (5 minutes)

### Deploy Multiple Services

```bash
# Add Redis to blue-stone
garden-rake offer redis --to blue-stone

# Add PostgreSQL to a second Stone (if you have one)
garden-rake offer postgresql --to red-stone

# Discover all services
garden-rake discover

# Output:
# Stone: blue-stone
#   Services: mongodb, redis
#   Address: 192.168.1.42
#
# Stone: red-stone
#   Services: postgresql
#   Address: 192.168.1.43
```

### Connect Applications

```bash
# Your app's .env now has stable connection strings
MONGODB_URI=zen-garden:mongodb/myapp
REDIS_URL=zen-garden:redis
POSTGRES_DSN=zen-garden:postgresql/myapp
```

**When blue-stone's hard drive dies:**
1. Swap in replacement laptop
2. Boot from USB (same process as Step 3)
3. Name it `blue-stone`
4. Deploy services: `garden-rake offer mongodb redis --to blue-stone`
5. **Apps reconnect automatically** (connection strings unchanged)

---

## Troubleshooting

### "garden-rake discover shows no Stones"

**Check network:**
```bash
# Verify Stone is reachable
ping blue-stone.local

# If ping fails, use IP directly
ping 192.168.1.42
```

**Check mDNS:**
```bash
# Linux - verify Avahi is running
systemctl status avahi-daemon

# macOS - built-in, no setup needed

# Windows - install Bonjour Print Services
# Download from Apple: https://support.apple.com/kb/DL999
```

**Check firewall:**
```bash
# Linux - allow mDNS (UDP 5353)
sudo ufw allow 5353/udp

# Allow Moss HTTP API (TCP 7185)
sudo ufw allow 7185/tcp
```

### "Service deployed but app can't connect"

**Verify service is running:**
```bash
# SSH to Stone (default credentials: garden / garden)
ssh garden@blue-stone.local

# Check Docker containers
docker ps

# Expected output:
# CONTAINER ID   IMAGE      PORTS                      STATUS
# abc123         mongo:7    0.0.0.0:27017->27017/tcp   Up 5 minutes
```

**Test native connection:**
```bash
# Connect directly to verify service works
mongosh mongodb://blue-stone.local:27017

# If this works, problem is discovery (not the service)
```

**Check mDNS announcement:**
```bash
# From your laptop
avahi-browse -a | grep koan-stone

# Expected output:
# + eth0 IPv4 blue-stone                 _koan-stone._tcp     local
```

### "USB installer boot fails"

**Verify BIOS/UEFI settings:**
- Secure Boot: Disabled (Debian may not be signed)
- Boot Mode: UEFI preferred (Legacy/BIOS works too)
- Boot Order: USB first

**Check USB creation:**
```powershell
# Windows - verify USB was written correctly
# USB should have:
# - EFI partition (FAT32)
# - Debian root partition (ext4)
# - preseed.cfg (automated install config)

# Re-run NewStone.ps1 if USB is corrupted
.\NewStone.ps1 -UsbDrive D: -StoneName "blue-stone" -Force
```

### "Stone won't start after reboot"

**Check Moss daemon status:**
```bash
# SSH to Stone
ssh garden@blue-stone.local

# Check service status
sudo systemctl status garden-moss

# Expected: active (running)
# If failed, check logs:
sudo journalctl -u garden-moss -n 50
```

---

## What You've Learned

✅ **Problem**: Configuration brittleness when machines fail  
✅ **Solution**: Resource abstraction via automatic discovery  
✅ **Demo**: Docker Stone with MongoDB  
✅ **Real Hardware**: USB installer on old laptop  
✅ **Architecture**: Stone + Moss + Rake + mDNS  
✅ **Deployment**: Multi-service setup across multiple Stones  

---

## Next Steps

### Dive Deeper

**Understand the system:**
- [Understanding Zen Garden](concepts/overview.md) - Complete technical overview
- [Technical Specification](specs/technical.md) - Architecture deep dive (2,500+ lines)
- [Mission & Impact](meta/mission.md) - E-waste crisis, social value, environmental benefit

**Production deployment:**
- [Hardware Guide](HARDWARE.md) - Hardware selection, power consumption, cost analysis
- [Security Specification](specs/security.md) - Threat models, Pond (optional mTLS), cryptography
- [Configuration Reference](MOSS-CONFIG.md) - Moss daemon options, logging, advanced config

**Build more:**
- [Roadmap](ROADMAP.md) - Development timeline (Phase 0-2, Q1-Q4 2026)
- [API Reference](REFERENCE.md) - HTTP API, mDNS protocol, connection strings
- [Service Catalog](reference/offerings.md) - All available offerings

### Optional: Add Security (Pond)

**When to enable Pond:**
- Multi-user environment (family, small team)
- Exposed to internet (port forwarding)
- Compliance requirements (GDPR, HIPAA)

**Philosophy:** "Set your stones, make sure everything is working, **fill the pond**."

Security is **opt-in** after initial setup. Start simple, add hardening when needed.

See [Security Specification](specs/security.md#pond-security-architecture) for details.

### Join the Community

**Status:** Phase 0 complete (specs), Phase 1 in progress (Python prototype, February 2026)

**Ways to help:**
- Test prototype on your hardware
- Report issues, suggest improvements
- Write service integrations
- Translate documentation
- Share your Stone builds (photos, specs, use cases)

**Maintained by:** Sylin.org (Koan Framework maintainer)  
**License:** Open source (see [LICENSE](../LICENSE))

---

## Quick Reference

**Install garden-rake:**
```bash
# Pre-built binary
curl -L https://github.com/sylin/zen-garden/releases/latest/download/garden-rake-linux-x64 -o garden-rake
chmod +x garden-rake && sudo mv garden-rake /usr/local/bin/

# From source (requires Rust)
git clone https://github.com/sylin/zen-garden.git
cd zen-garden/src/rake
cargo build --release
sudo cp target/release/garden-rake /usr/local/bin/
```

**Common commands:**
```bash
# Discover all Stones
garden-rake discover

# Deploy service
garden-rake offer mongodb --to blue-stone

# Remove service
garden-rake take-away mongodb --from blue-stone

# Watch real-time events
garden-rake watch

# Check Stone health
garden-rake stones --health
```

**Connection strings in apps:**
```bash
# .env file
MONGODB_URI=zen-garden:mongodb/myapp
REDIS_URL=zen-garden:redis
POSTGRES_DSN=zen-garden:postgresql/myapp
```

**USB installer:**
```powershell
# Windows - Create bootable Debian Stone USB
.\installer\NewStone.ps1 -UsbDrive D: -StoneName "blue-stone"
```

---

**Got questions?** Open an issue or start a discussion on GitHub.  
**Ready for technical depth?** → [Understanding Zen Garden](concepts/overview.md)

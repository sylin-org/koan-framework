# Your First Stone: Installation Guide

**Purpose:** Step-by-step Stone installation from hardware selection to first service deployment.  
**Audience:** Operators installing their first Stone, developers setting up development environment.

---

## Table of Contents

1. [Hardware Selection](#hardware-selection)
2. [Create Bootable USB](#create-bootable-usb)
3. [Install Stone](#install-stone)
4. [Verify Installation](#verify-installation)
5. [Deploy First Service](#deploy-first-service)
6. [Connection Strings](#connection-strings)
7. [Post-Install Configuration](#post-install-configuration)
8. [Troubleshooting](#troubleshooting)

---

## Hardware Selection

### Recommended Hardware Tiers

**Tier 1 - Reclaimed ($0-30)**
- Old laptop (2010-2015 era, any condition)
- Decommissioned thin client
- Unused desktop with failing display
- **Use case:** Low-traffic databases, development environments, file servers

**Tier 2 - Budget ($30-100)**
- Raspberry Pi 4 (4GB RAM)
- Used enterprise thin client (eBay: $40-80)
- Refurbished mini PC
- **Use case:** Production databases for small teams, continuous integration, media servers

**Tier 3 - Performance ($100-250)**
- Intel NUC (used: $150-200)
- Raspberry Pi 5 (8GB RAM)
- Budget mini PC with SSD
- **Use case:** Multiple services per Stone, high-traffic databases, container orchestration

### Minimum Requirements

- **CPU:** 64-bit x86_64 or ARM64 (2+ cores recommended)
- **RAM:** 2GB minimum, 4GB recommended, 8GB for multiple services
- **Storage:** 16GB minimum (USB), 32GB+ recommended (SSD preferred)
- **Network:** Ethernet (preferred) or WiFi with stable connection
- **Power:** USB-C PD (Raspberry Pi 5), barrel jack (thin clients), standard ATX (desktops)

### Compatibility Notes

- **ARM64:** Full support (Raspberry Pi, Apple Silicon, AWS Graviton)
- **x86_64:** Full support (Intel, AMD)
- **32-bit:** Not supported (Docker limitations)
- **Windows/macOS:** Not supported as host OS (use WSL2/Docker Desktop for development only)

---

## Create Bootable USB

### Prerequisites

- **USB drive:** 8GB minimum, 16GB+ recommended (will be erased)
- **Windows machine:** PowerShell 5.1+ with Administrator privileges
- **Internet connection:** For downloading Debian base image

### Generate USB Installer

```powershell
# Navigate to zen-garden installer directory
cd zen-garden\installer

# Create bootable Stone USB
.\NewStone.ps1 -UsbDrive "E:" -StoneName "blue-stone" -Offering mongodb,redis

# Parameters:
#   -UsbDrive      : Drive letter of USB (will be formatted)
#   -StoneName     : Hostname for Stone (lowercase, hyphens allowed)
#   -Offering      : Optional pre-install services (comma-separated)
```

**What the script does:**
1. Downloads Debian 12 net-install ISO
2. Creates preseed configuration (automated installation)
3. Injects garden-moss Debian package
4. Configures systemd service (auto-start on boot)
5. Optionally creates pre-install manifest (`garden-moss-preinstall.json`)
6. Writes bootable image to USB

**Pre-install manifest example:**
```json
{
  "version": "1.0",
  "offerings": ["mongodb", "redis"],
  "auto_install": true
}
```

Pre-install services deploy automatically on first boot (no manual commands needed).

---

## Install Stone

### Installation Workflow

1. **Insert USB** into target hardware
2. **Boot from USB** (press F12, DEL, or F2 during boot to access boot menu)
3. **Wait for automated installation** (10-20 minutes depending on hardware):
   - Debian base system installation
   - Network configuration (DHCP)
   - Docker and Docker Compose installation
   - Garden-Moss daemon installation
   - System reboot
4. **First boot:**
   - Garden-Moss starts via systemd
   - Stone announces itself via mDNS
   - Pre-install services deploy (if configured)

### Installation Sequence (Technical)

```
Boot USB → Debian Installer (preseed) → Reboot → systemd → garden-moss.service
                                                              ↓
                                                    Detect preinstall.json?
                                                         ↙        ↘
                                                       YES        NO
                                                        ↓          ↓
                                            Spawn async job   Ready for commands
                                            Install services
                                            Remove manifest
                                            Announce services
```

**Resilience features:**
- If Moss crashes during pre-install: systemd restarts it (`Restart=always`)
- If Docker not ready: Moss waits up to 60 seconds (30 retries × 2s)
- If power loss during pre-install: Manifest persists, job resumes on next boot
- If service fails: Tracked in job's `failed` map, other services continue

---

## Verify Installation

### Discover Stone

From any machine on the same network:

```bash
# Install garden-rake CLI (if not already installed)
curl -L https://github.com/sylin/zen-garden/releases/latest/download/garden-rake-linux-x64 -o garden-rake
chmod +x garden-rake && sudo mv garden-rake /usr/local/bin/

# Discover all Stones
garden-rake discover

# Expected output:
# ●  blue-stone (192.168.1.42:7185)
#    Services: mongodb, redis
#    Status: healthy
#    Uptime: 5m 23s
```

If Stone not discovered:
- Check Stone is powered on and network cable connected
- Verify Stone and client on same subnet
- Try Lantern discovery (if configured): `garden-rake discover --via-lantern http://lantern:7186`
- See [Troubleshooting](#troubleshooting) for mDNS debugging

### Check Pre-Install Services

If you specified `-Offering` during USB creation:

```bash
# Check service status
garden-rake observe

# Expected output for mongodb,redis:
# ●  blue-stone (Healthy, uptime: 5m 23s)
#    OFFERINGS:
#    ├─ mongodb       Run   1.2%        64 MB  ↓  256 KB  5m
#    └─ redis         Run   0.3%        12 MB  ↓   64 KB  5m
```

### Verify Moss Daemon

SSH into Stone (default credentials during development):

```bash
# SSH access (if enabled during preseed)
ssh stone@blue-stone.local

# Check Moss daemon status
sudo systemctl status garden-moss

# Expected output:
# ● garden-moss.service - Garden Moss Daemon
#    Loaded: loaded (/etc/systemd/system/garden-moss.service; enabled)
#    Active: active (running) since 2026-01-19 12:00:00 UTC; 5min ago
#    Main PID: 1234
#    Memory: 45.2M

# Check Moss logs
sudo journalctl -u garden-moss -n 50

# Verify HTTP API
curl http://localhost:7185/health

# Expected: {"status":"healthy","components":{"docker":"up","mDNS":"up"}}
```

---

## Deploy First Service

### Plant an Offering

```bash
# Deploy MongoDB
garden-rake offer mongodb --to blue-stone

# Expected output:
# Deploying mongodb to blue-stone...
# Pulling image: mongo:7
# Starting container...
# ✓ Service healthy: mongodb on blue-stone:27017
```

### What Happened (Technical View)

1. **Rake** discovered blue-stone via mDNS query (`_koan-stone._tcp.local.`)
2. **Rake** sent HTTP POST to `http://blue-stone:7185/api/v1/offerings` with `{name: "mongodb"}`
3. **Moss** read `/usr/share/garden-moss/templates/mongodb.yaml` service template
4. **Moss** updated `/opt/garden-moss/docker-compose.yml` with MongoDB service definition
5. **Moss** executed `docker compose up -d` to start container
6. **Moss** announced new service via mDNS (updated TXT record with `offering=mongodb,port=27017`)
7. **Rake** polled service health and confirmed deployment

### Check Service Status

```bash
# List all services
garden-rake observe

# Output:
# ●  blue-stone (Healthy, uptime: 10m 15s)
#    OFFERINGS:
#    ├─ mongodb       Run   1.2%        64 MB  ↓  256 KB  2m

# Get detailed service info
garden-rake describe mongodb

# Output:
# Service: mongodb
# Template: mongodb
# Image: mongo:7
# Status: Running
# Health: Passing
# Port: 27017
# Connection: zen-garden:mongodb
```

---

## Connection Strings

### Using zen-garden:// Protocol

Applications connect using stable resource references (never changes, even when hardware swaps):

```bash
# .env file for your application
MONGODB_URI=zen-garden:mongodb/myapp
REDIS_URL=zen-garden:redis
POSTGRES_DSN=zen-garden:postgresql/production
```

### Client Library Support

**Node.js:**
```javascript
const { MongoClient } = require('mongodb');
const uri = process.env.MONGODB_URI || 'zen-garden:mongodb/myapp';
const client = new MongoClient(uri);
await client.connect();
```

**Python:**
```python
from pymongo import MongoClient
uri = os.getenv('MONGODB_URI', 'zen-garden:mongodb/myapp')
client = MongoClient(uri)
```

**Connection string resolution:**
1. Client library queries mDNS for `_koan-stone._tcp.local.` with TXT record `offering=mongodb`
2. Discovers Stone IP (e.g., `192.168.1.42`) and port (`27017`)
3. Rewrites to native protocol: `mongodb://192.168.1.42:27017/myapp`
4. Connects to MongoDB using standard driver

**Why this matters:**
- Replace failing laptop with Raspberry Pi → applications automatically discover new hardware
- Move database from stone-01 to stone-02 → no configuration updates needed
- Add load balancing → connection string unchanged, resolution returns multiple endpoints

---

## Post-Install Configuration

### Optional: Enable Pond Security

**When to enable:**
- Multi-user environment (family, small team)
- Exposed to internet (port forwarding)
- Compliance requirements (GDPR, HIPAA)

**Philosophy:** "Set your stones, make sure everything is working, **fill the pond**."

Security is **opt-in** after verifying basic functionality.

```bash
# Step 1: Create Pond on Cornerstone (first Stone)
ssh stone@blue-stone.local
sudo garden-rake place keystone

# Enter strong passphrase (20+ characters)
# Keystone created: /var/lib/zen-garden/keystone.enc

# Step 2: Invite additional Stones
sudo garden-rake invite stone-02

# Output: TOTP code (6 characters, valid 5 minutes)
# Code: KP7X9M

# Step 3: Join Stone to Pond (on stone-02)
ssh stone@stone-02.local
sudo garden-rake join pond

# Enter code: KP7X9M
# ✓ Certificate issued (1-hour TTL, auto-renews every 30 min)
```

See [Security Guide](../security/pond-setup.md) for complete Pond setup instructions.

### Optional: Configure Lantern Registry

For cross-subnet discovery or Windows client support:

```bash
# Install Lantern on separate machine (or shared Stone)
docker run -d -p 7186:7186 --name garden-lantern \
  --restart=always \
  sylin/garden-lantern:latest

# Configure Stones to register with Lantern
# Add to /etc/garden-moss/config.toml on each Stone:
[registry]
lantern_url = "http://lantern-host:7186"
register_interval = 30  # seconds

# Restart Moss
sudo systemctl restart garden-moss

# Verify registration
curl http://lantern-host:7186/api/stones

# Output: [{"name": "blue-stone", "address": "192.168.1.42:7185", ...}]
```

### Optional: Add Monitoring

```bash
# Deploy Prometheus and Grafana
garden-rake offer prometheus --to blue-stone
garden-rake offer grafana --to blue-stone

# Access Grafana
# URL: http://blue-stone:3000
# Default credentials: admin/admin

# Moss exposes metrics at /metrics
curl http://blue-stone:7185/metrics

# Prometheus auto-discovers Stones via mDNS
```

---

## Troubleshooting

### Stone Not Discovered

**Symptom:** `garden-rake discover` returns "No Stones found"

**Diagnosis:**
```bash
# Check mDNS availability
avahi-browse -a  # Linux
dns-sd -B _koan-stone._tcp  # macOS

# Test direct HTTP
curl http://blue-stone.local:7185/health

# If reachable: mDNS issue
# If unreachable: network/firewall issue
```

**Solutions:**
1. **Same subnet?** mDNS limited to local broadcast domain (192.168.1.0/24)
   - Solution: Use Lantern registry for cross-subnet discovery
2. **Firewall blocking?** Check port 7185 (HTTP API) and 5353 (mDNS)
   - Solution: `sudo ufw allow 7185 && sudo ufw allow 5353/udp`
3. **Windows client?** mDNS unreliable without Bonjour service
   - Solution: Install Bonjour Print Services or use Lantern registry

### Service Won't Start

**Symptom:** `garden-rake observe` shows service as "Exited" or "Restarting"

**Diagnosis:**
```bash
# Stream service logs in real-time
garden-rake watch offering mongodb logs

# Check last 100 lines
garden-rake watch offering mongodb logs --tail 100

# Check Docker directly (SSH to Stone)
ssh stone@blue-stone.local
docker ps -a  # Shows all containers including stopped
docker logs <container_id>
```

**Common issues:**
1. **Port conflict:** Another service using same port
   - Solution: Change port in offering template (see customization guide)
2. **Volume permissions:** Container cannot write to volume
   - Solution: `docker exec <container> chown -R mongodb:mongodb /data/db`
3. **Resource exhaustion:** Out of memory or disk space
   - Solution: Check `df -h` and `free -m`, remove unused containers

### Pre-Install Services Failed

**Symptom:** Stone boots but pre-install services not running

**Diagnosis:**
```bash
# Check pre-install job status (HTTP API)
curl http://blue-stone:7185/api/jobs

# Output example:
# {
#   "id": "018d3c6f-8e4c-7890-a123-456789abcdef",
#   "status": "Failed",
#   "completed": ["redis"],
#   "failed": {"mongodb": "Image pull timeout"}
# }

# Check Moss logs for job errors
ssh stone@blue-stone.local
sudo journalctl -u garden-moss -n 200 | grep preinstall
```

**Common issues:**
1. **Network timeout:** Image pull failed during installation
   - Solution: Manually install service: `garden-rake offer mongodb --to blue-stone`
2. **Invalid offering name:** Typo in pre-install manifest
   - Solution: Check available offerings: `garden-rake list --available`
3. **Docker not ready:** Moss started before Docker daemon
   - Solution: Restart Moss: `sudo systemctl restart garden-moss`

### Connection String Not Resolving

**Symptom:** Application cannot resolve `zen-garden:mongodb`

**Diagnosis:**
```bash
# Test mDNS resolution manually
avahi-resolve -n blue-stone.local  # Linux
dns-sd -G v4 blue-stone.local  # macOS

# If resolved: DNS working
# If not resolved: mDNS issue or Stone not announcing
```

**Solutions:**
1. **mDNS not enabled:** Install Avahi (Linux) or Bonjour (Windows)
   - Linux: `sudo apt install avahi-daemon`
   - Windows: Install Bonjour Print Services or use direct IP
2. **Wrong service name:** Check exact offering name
   - Correct: `zen-garden:mongodb`
   - Wrong: `zen-garden://mongodb` (no protocol slashes)
3. **Stone offline:** Verify Stone reachable via HTTP
   - Test: `curl http://blue-stone:7185/health`

### SSH Access Not Working

**Symptom:** Cannot SSH into Stone after installation

**Note:** SSH is **disabled by default** in production preseed for security.

**Enable SSH (development only):**
1. Edit `installer/preseed.cfg` before USB creation
2. Uncomment SSH server installation:
   ```
   d-i pkgsel/include string openssh-server
   ```
3. Regenerate USB with `NewStone.ps1`

**Alternative access methods:**
- Direct keyboard/monitor access (Stone has full Debian console)
- Moss HTTP API for diagnostics: `curl http://blue-stone:7185/metrics`
- Physical access to edit `/etc/garden-moss/config.toml`

---

## Next Steps

- **Add more services:** [Service Catalog](../reference/offerings.md)
- **Customize offerings:** [Creating Custom Offerings](offering-services.md)
- **Enable security:** [Pond Setup Guide](../security/pond-setup.md)
- **Monitor health:** [Operations Guide](../ops/maintainers.md)
- **Multi-Stone garden:** Repeat installation with different Stone names

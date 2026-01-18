# Getting Started with Zen Garden

**From zero to running services in 5 minutes.**

---

## Prerequisites That Bite People

Before starting, verify these common failure points:

**mDNS/Bonjour/Avahi:**
- **macOS/iOS**: Built-in (Bonjour)
- **Linux**: Install `avahi-daemon` (`sudo apt install avahi-daemon`)
- **Windows**: Install Bonjour Print Services OR use Lantern API (HTTP fallback)

**Docker permissions:**
```bash
# Verify Docker works without sudo
docker ps
# If fails: sudo usermod -aG docker $USER && logout/login
```

**Firewall:**
- mDNS uses UDP port 5353 (must be allowed)
- Stone services need their ports open (27017 for MongoDB, etc.)

**Network:**
- All devices must be on same LAN (mDNS doesn't cross subnets)
- VLANs/guest networks may block mDNS

---

## Hello World: Docker Stone

The fastest way to understand Zen Garden is to run a Stone and connect to it.

### 1. Start a MongoDB Stone

```bash
docker run -d \
  --name mongo-stone \
  -p 27017:27017 \
  -e ANNOUNCE_SERVICE=mongodb \
  zen-garden/stone:latest
```

**What this does:**
- Runs MongoDB in a container
- Announces via mDNS: "I offer MongoDB"
- Makes service discoverable as `zen-garden:mongodb`

### 2. Connect Your App

**Node.js:**
```javascript
const MongoClient = require('mongodb').MongoClient;
const url = process.env.MONGODB_URI; // zen-garden:mongodb
const client = await MongoClient.connect(url);
// Automatically discovers and connects
```

**Python:**
```python
import os
from pymongo import MongoClient
uri = os.getenv('MONGODB_URI')  # zen-garden:mongodb
client = MongoClient(uri)
# Automatically discovers and connects
```

**Any language (HTTP fallback):**
```bash
# Use the resolver API
curl http://lantern.local/api/resolve?service=mongodb
# Returns: {"uri": "mongodb://192.168.1.50:27017"}
```

### 3. Verify Discovery Working

**Success criteria (30-second check):**

```bash
# 1. Check Stone is announcing
avahi-browse -a -t -r | grep zen-garden
# Expected: _zen-garden._tcp entries

# 2. Test resolution
ping zen-garden:mongodb.local
# Expected: Resolves to IP address

# 3. Verify app connects
docker logs mongo-stone
# Expected: Connection accepted from <your-ip>
```

**If you see these, discovery is working. Skip to "Complete Three-Stone Setup."**

---

## Troubleshooting

**Problem: `avahi-browse` not found**
```bash
# Linux
sudo apt install avahi-utils avahi-daemon
sudo systemctl start avahi-daemon

# macOS
# mDNS (Bonjour) is built-in, use dns-sd instead:
dns-sd -B _zen-garden._tcp
```

**Problem: Can't resolve `zen-garden:mongodb`**

Check discovery layer:
```bash
# Are Stones announcing?
avahi-browse -rt _zen-garden._tcp
# Nothing appears? Check firewall (port 5353/UDP)

# Can you reach Stone by IP?
docker inspect mongo-stone | grep IPAddress
ping <that-ip>
# Works by IP but not by name? Check /etc/nsswitch.conf:
# hosts: files mdns4_minimal [NOTFOUND=return] dns mdns4
```

**Problem: Firewall blocking mDNS**
```bash
# Ubuntu/Debian
sudo ufw allow 5353/udp

# CentOS/RHEL
sudo firewall-cmd --add-service=mdns --permanent
sudo firewall-cmd --reload

# Windows: Open UDP 5353 in Windows Firewall
```

**Problem: Different subnets**

mDNS doesn't cross subnets. Check:
```bash
# Your laptop
ip addr show | grep inet

# Stone device
docker network inspect bridge
```

If subnets differ (e.g., 192.168.1.x vs 192.168.2.x), mDNS won't work. Options:
- Move devices to same subnet
- Use Lantern (HTTP directory, works across subnets)
- Configure mDNS reflector on router (advanced)

**Problem: Docker permission denied**
```bash
# Add yourself to docker group
sudo usermod -aG docker $USER
# Log out and back in for changes to take effect
```

**Problem: Stone started but not discoverable**

Verify ANNOUNCE_SERVICE environment variable:
```bash
docker inspect mongo-stone | grep ANNOUNCE_SERVICE
# Should return: "ANNOUNCE_SERVICE=mongodb"
```

Check Stone logs for announcement:
```bash
docker logs mongo-stone | grep announce
# Expected: [mDNS] announcing service: mongodb
```

---

## Complete Three-Stone Setup

Build a real infrastructure: database + storage + compute.

### Hardware Options

**Option A: All Docker (fastest, learning)**
- Use Docker containers on your laptop
- Good for: Understanding concepts, development

**Option B: Physical Stones (realistic, production-like)**
- Old laptops, Raspberry Pis, thin clients
- Good for: Actual self-hosting, production use
- See [HARDWARE.md](HARDWARE.md) for builds
- See "Creating Physical Stones" section below for USB installer

### Setup Steps

**1. Start Lantern (optional but recommended):**
```bash
docker run -d \
  --name lantern \
  -p 8080:8080 \
  zen-garden/lantern:latest

# Access dashboard: http://lantern.local:8080
```

**2. Start Database Stone:**
```bash
docker run -d \
  --name db-stone \
  -p 27017:27017 \
  -v mongo-data:/data/db \
  -e ANNOUNCE_SERVICE=mongodb \
  mongo:latest
```

**3. Start Storage Stone:**
```bash
docker run -d \
  --name storage-stone \
  -p 9000:9000 \
  -v minio-data:/data \
  -e ANNOUNCE_SERVICE=storage \
  -e MINIO_ROOT_USER=admin \
  -e MINIO_ROOT_PASSWORD=adminpass \
  minio/minio:latest server /data
```

**4. Start Compute Stone:**
```bash
docker run -d \
  --name compute-stone \
  -v /var/run/docker.sock:/var/run/docker.sock \
  -e ANNOUNCE_SERVICE=docker \
  zen-garden/compute-stone:latest
```

**5. Deploy an app:**
```bash
garden-rake push myapp \
  --image myapp:latest \
  --env DB=zen-garden:mongodb \
  --env FILES=zen-garden:storage

# Output:
# [rake] resolved: zen-garden:mongodb -> mongodb://db-stone:27017
# [rake] resolved: zen-garden:storage -> http://storage-stone:9000
# [rake] deploying to: compute-stone
# [rake] app live at: http://myapp.garden/
```

---

## Creating Physical Stones (USB Installer)

For production or persistent infrastructure, create physical Stone devices.

### Quick Version

**On Windows:**
```powershell
cd path\to\zen-garden\installer
.\NewStone.ps1  # Auto-detects USB drive
```

**Result:** Bootable USB that installs Debian Stone automatically (8-15 min unattended).

**Full instructions:** See [HARDWARE.md](HARDWARE.md#creating-stone-usb-installer) for:
- Prerequisites (USB drive, target hardware)
- Step-by-step USB creation
- Installation walkthrough
- Hardware recommendations

---

## Real-World Demos

Scenarios that show Zen Garden's value.

### Demo 1: Stone Swapping (Resilience)

**Setup:** Three physical Stones running a notes app

**Scenario:**
1. App connects to `zen-garden:mongodb` (resolves to blue Stone)
2. Unplug blue Stone during live demo
3. Plug in different Stone with MongoDB
4. App reconnects automatically

**What this proves:** Infrastructure resilience without configuration changes

**Time:** 2 minutes

---

### Demo 2: Development to Production

**Setup:** Laptop with Docker Stones, production rack with physical Stones

**Scenario:**
1. Developer writes app on laptop: `APP_DB=zen-garden:mongodb npm start`
2. App discovers laptop's Docker Stone
3. Deploy exact same container to production rack
4. App discovers production Stone automatically
5. Same code, zero config changes

**What this proves:** True environment parity—dev and prod use identical discovery

**Time:** 5 minutes

---

### Demo 3: Network Changes (IP Chaos)

**Setup:** Router with DHCP

**Scenario:**
1. App running, connected to Stones
2. Reboot router (IPs change)
3. Stones re-announce with new IPs
4. App queries again, reconnects
5. Zero downtime

**What this proves:** IP changes don't break Zen Garden infrastructure

**Time:** 3 minutes

---

## Troubleshooting

### Discovery Not Working

**Symptom:** Can't connect to `zen-garden:mongodb`

**Diagnosis:**
```bash
# Check mDNS
avahi-browse -a -t -r | grep zen-garden

# Check Lantern (if running)
curl http://lantern.local/api/stones

# Verify Stone announcing
# (on Stone device)
systemctl status avahi-daemon
```

**Common fixes:**
- Firewall blocking mDNS (port 5353 UDP)
- Different subnets (mDNS only works on same LAN)
- Avahi not running on Stone

---

### Lantern Unreachable

**Symptom:** `curl http://lantern.local` fails

**Fix:**
```bash
# Find Lantern IP manually
avahi-browse _http._tcp --resolve

# Or use IP directly
curl http://192.168.1.x:8080/api/stones
```

---

### Slow Discovery

**Symptom:** Connections take 2-5 seconds

**Diagnosis:**
- Without Lantern: Peer-to-peer mDNS broadcast (100-200ms normal)
- With Lantern: Directory lookup (10-50ms normal)
- Slow network/congestion causing delays

**Fix:** Add Lantern for instant (<10ms) discovery

---

## Next Steps

**Explore concepts:**
- [UNDERSTANDING.md](UNDERSTANDING.md) - How discovery works, security model
- [REFERENCE.md](REFERENCE.md) - API documentation, garden-rake tool

**Build infrastructure:**
- [HARDWARE.md](HARDWARE.md) - Physical Stone builds, USB installer details
- [SECURITY.md](SECURITY.md) - Enable Pond security for production

**Deploy apps:**
- Use `garden-rake` to push containers
- Configure apps with `zen-garden:*` URIs
- Add monitoring via Lantern dashboard

---

**Philosophy:** Start simple. One Docker Stone, one app. See it work. Add complexity (Lantern, Pond, physical hardware) only when needed.

## Next Steps

- [Understand the Philosophy](../introduction/why-zen-garden.md)
- [Explore Hardware Options](../hardware/philosophy.md)
- [Try the Demos](./demos.md)
- [Learn About Limitations](../reference/limitations.md)
# Hands-On Demos



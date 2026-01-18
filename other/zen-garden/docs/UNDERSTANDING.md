# Understanding Zen Garden

**How automatic service discovery makes self-hosting accessible.**

---

## The Core Idea

Self-hosting fails when IP addresses change:

```bash
# This breaks when your router reboots
MONGODB_URI=mongodb://192.168.1.50:27017/mydb
```

**Zen Garden uses intent instead of location:**

```bash
# This never changes
MONGODB_URI=zen-garden:mongodb/mydb
```

Services announce "I offer MongoDB." Apps discover "Who has MongoDB?" Connections form automatically.

---

## Hello World (60 Seconds)

```bash
# Terminal 1: Start MongoDB Stone
docker run -d -p 27017:27017 --name mongo-stone \
  -e ANNOUNCE_SERVICE=mongodb \
  zen-garden/stone:latest

# Terminal 2: App connects
CONNECTION_STRING="zen-garden:mongodb" node app.js
```

**What happened:**
1. Stone announced "I offer MongoDB" via mDNS
2. App asked "Who has MongoDB?"
3. Stone responded with location
4. Connection established

**This is the entire system.** Everything else (Lanterns, Ponds, security) is optional.

---

## Core Concepts

### Stones

**A Stone is a device offering a service.**

```bash
# MongoDB Stone
docker run -d -e ANNOUNCE_SERVICE=mongodb mongo:7

# Redis Stone  
docker run -d -e ANNOUNCE_SERVICE=redis redis:latest

# PostgreSQL Stone
docker run -d -e ANNOUNCE_SERVICE=postgresql postgres:16
```

Any device can be a Stone:
- 2015 laptop → MongoDB Stone
- Decommissioned thin client → Redis Stone
- Old desktop → Storage Stone

### Discovery Protocol

**mDNS (Multicast DNS) - proven technology, 20+ years old:**

```
Stone announces:
  Service Type: _koan-stone._tcp.local.
  TXT Record: offering=mongodb, port=27017

App queries:
  "Who offers mongodb?"
  
Stone responds:
  "I do: mongodb://stone-01:27017"
```

Built into macOS (Bonjour), Linux (Avahi). No infrastructure required.

### Connection Strings

**Standard format:**

```
zen-garden:<service-type>[/<database>]
```

**Examples:**
```bash
zen-garden:mongodb          # Discover MongoDB
zen-garden:mongodb/mydb     # Discover MongoDB, use 'mydb' database
zen-garden:redis            # Discover Redis
zen-garden:postgres/app     # Discover PostgreSQL, use 'app' database
```

**Resolution:**
- App uses standard drivers (MongoDB, PostgreSQL, Redis clients)
- Resolver translates `zen-garden:mongodb` → `mongodb://stone-01:27017`
- App never knows IP address changed

### Optional: Lantern

**Problem:** Windows lacks mDNS. Docker Desktop isolates containers. VLANs block broadcasts.

**Solution:** Lantern (HTTP directory)

```bash
# Stones register with Lantern
POST http://lantern.local/api/register
{ "offering": "mongodb", "host": "192.168.1.50", "port": 27017 }

# Apps query Lantern
GET http://lantern.local/api/resolve?service=mongodb
{ "uri": "mongodb://192.168.1.50:27017", "healthy": true }
```

**When to use:**
- Windows clients (no built-in mDNS)
- Cross-subnet discovery
- Dashboard/monitoring UI

**When not needed:**
- Linux/macOS on same LAN
- Peer-to-peer discovery sufficient

### Optional: Pond

**Problem:** Rogue devices can announce fake services. Network sniffing exposes traffic.

**Solution:** Pond (cryptographic binding)

```bash
# Initialize garden with security
garden-rake init --pond

# Bind each Stone
garden-rake bind stone-01

# Stone receives certificate
# Future announcements include cert fingerprint
# Apps validate certificates before connecting
```

**When to use:**
- Production workloads
- Sensitive data (PII, financial records)
- Untrusted networks

**When not needed:**
- Home lab / personal projects
- Strong physical security (locked office)
- Non-sensitive data

---

## Example: Classroom Demo

*Three devices labeled blue (Database), green (Storage), orange (Compute).*

**Lantern running:**
```
[lantern] stone joined: db-stone-01 (mongodb)
[lantern] stone joined: storage-stone-01 (minio)
[lantern] stone joined: compute-stone-01 (docker)
```

**App connects:**
```bash
APP_DB=zen-garden:mongodb APP_FILES=zen-garden:storage dotnet run

# Output
[resolver] mongodb -> mongodb://db-stone-01:27017
[resolver] storage -> http://storage-stone-01:9000
[app] connected to database
[app] ready
```

**Teacher unplugs blue stone, plugs in different one:**
```
[lantern] stone left: db-stone-01
[lantern] stone joined: db-stone-02 (mongodb)
[resolver] mongodb -> mongodb://db-stone-02:27017
[app] reconnected
```

**No config files changed. Infrastructure became physical, swappable.**

---

## Discovery Resolution Order

```
1. Try mDNS (50-100ms timeout)
   ├─ Success → Connect
   └─ Fail → Try Lantern

2. Try Lantern HTTP (if LANTERN_URL set)
   ├─ Success → Connect
   └─ Fail → Error

3. Error with diagnostic guidance
   - Check mDNS daemon (Avahi/Bonjour)
   - Check firewall (UDP port 5353)
   - Check Stone is running
   - Check same subnet/VLAN
```

---

## What This Enables

**Infrastructure as Lego blocks:**
- Swap database Stone without reconfiguring apps
- Add cache Stone, apps discover automatically
- Move services between devices transparently

**Hardware reuse:**
- 2015 laptop → productive infrastructure (not landfill)
- Decommissioned thin clients → cache/database servers
- Extended device lifespan: +3-5 years average

**Learning through physicality:**
- See services as physical devices
- Understand infrastructure by touching it
- Debug by proximity, not remote tunnels

**Digital sovereignty:**
- Own your data locally
- Zero monthly fees
- No vendor tracking

---

## Limitations (What This Is Not)

**Not for:**
- ❌ Cloud-scale deployments (100+ servers)
- ❌ Mission-critical uptime (no HA built-in)
- ❌ Zero-trust networks (Pond adds security, not zero-trust)
- ❌ Cross-internet discovery (local network only)

**Best for:**
- ✅ Home labs (3-20 devices)
- ✅ Small businesses (5-30 services)
- ✅ Educational environments (hands-on learning)
- ✅ Development/staging (rapid experimentation)

---

## Technical Details

**Protocol:** mDNS (RFC 6762/6763)  
**Service Type:** `_koan-stone._tcp.local.`  
**TXT Records:** `offering=<type>, version=1.0, capabilities=direct`  
**Transport:** Native protocols (MongoDB wire protocol, PostgreSQL, Redis, etc.)  
**Security:** Optional mTLS (Pond), self-signed certificates with pinning

**Supported platforms:**
- Linux (Avahi)
- macOS (Bonjour)
- Windows (via Lantern HTTP fallback)

**Supported service types:** MongoDB, PostgreSQL, Redis, SQL Server, RabbitMQ, MinIO, Ollama, and more (see [REFERENCE.md](REFERENCE.md))

---

## Next Steps

- [Getting Started](GETTING-STARTED.md) - Run your first Stone
- [Technical Reference](REFERENCE.md) - Deep dive into protocol
- [Hardware Guide](HARDWARE.md) - Build physical Stones
- [Security Model](SECURITY.md) - Add Pond security

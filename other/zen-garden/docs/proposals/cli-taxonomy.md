# Zen Garden CLI + API Taxonomy v2 (Final Design)

**Status:** Proposal (Final)  
**Date:** 2026-01-17  
**Scope:** Complete CLI/API redesign with dual syntax (zen + normative)  
**Back-Compat:** Not required — greenfield approach  

---

## Executive Summary

This proposal defines a **dual-syntax CLI** that serves both zen purists and normative users:

### **Zen Path** (Primary)
- **Poetic verbs:** offer, rest, wake, nourish, release, observe, watch, explore, tend
- **Natural syntax:** `garden-rake offer mongo at stone-02` (positional "at")
- **Philosophy:** Infrastructure as living ecosystem, commands read like poetry

### **Normative Path** (Standard)
- **Industry verbs:** create, stop, start, update, delete, status, logs, list
- **Standard syntax:** `garden-rake services create mongo --at stone-02` (flags)
- **Philosophy:** Familiar kubectl/docker patterns for muscle memory

### **Key Innovation**
**Syntax follows vocabulary:** Zen verbs use zen syntax (positional), normative verbs use standard syntax (flags). This creates self-teaching consistency.

---

## Core Design Principles

1. **Dual vocabulary, dual syntax** — zen verbs with natural language, normative verbs with flags
2. **Context-aware suggestions** — every command guides the next step (API response payload)
3. **Observe = snapshot, Watch = stream** — clear distinction between awareness and meditation
4. **Always tending** — no "untended" state, Rake auto-tends on startup to first responding stone
5. **API versioned** — `/api/v1/...` for stability, versionless redirects to latest
6. **Quiet mode** — `--quiet` / `-q` / `quietly` (zen) suppresses suggestions
7. **Tend affects all commands** — both zen and normative commands default to tended stone

---

## Zen Vocabulary & Philosophy

| **Zen Verb**   | **Normative**    | **Metaphor**                                  | **Intent**                |
|----------------|------------------|-----------------------------------------------|---------------------------|
| **offer**      | create           | Give a gift to your stone                     | Install service           |
| **rest**       | stop             | Natural pause, let it rest                    | Stop service              |
| **wake**       | start            | Gentle awakening                              | Start service             |
| **nourish**    | update, upgrade  | Feed and improve                              | Upgrade service           |
| **release**    | delete, remove   | Let go, return to earth                       | Remove service            |
| **observe**    | status           | Mindful awareness, see current state          | View snapshot             |
| **watch**      | logs, stream     | Continuous meditation, flow awareness         | Stream events             |
| **explore**    | list             | Wander and discover                           | Browse catalog            |
| **touch**      | inspect          | Feel the stone's warmth and texture           | Deep diagnostics          |
| **tend**       | (zen-only)       | Care for, direct attention                    | Set context               |
| **garden**     | topology         | The whole ecosystem                           | Multi-stone view          |

---

## CLI Syntax Rules

### Rule 1: Zen Verbs Use Positional "at"
```bash
garden-rake offer mongo at stone-02
garden-rake rest grafana at stone-02
garden-rake watch redis at stone-03
garden-rake observe at stone-02
```

**Pattern:** Natural language flow, reads like prose

---

### Rule 2: Normative Verbs Use Flag "--at"
```bash
garden-rake services create mongo --at stone-02
garden-rake services stop grafana --at stone-02
garden-rake logs redis --at stone-03
garden-rake status --at stone-02
```

**Pattern:** Industry-standard CLI conventions

---

### Rule 3: Mixing Syntax is Rejected
```bash
# ✗ Zen verb with flag syntax
$ garden-rake offer mongo --at stone-02
Error: Zen commands use natural syntax. Try: garden-rake offer mongo at stone-02

# ✗ Normative verb with positional syntax
$ garden-rake services create mongo at stone-02
Error: Standard commands use flags. Try: garden-rake services create mongo --at stone-02
```

---

## Complete Command Reference

### 🎋 Discovery & Context

| **Zen Syntax** | **Normative Syntax** | **API Endpoint** |
|----------------|----------------------|------------------|
| `garden-rake explore` | `garden-rake list` | `GET /api/v1/offerings` |
| `garden-rake explore database` | `garden-rake list database` | `GET /api/v1/offerings?q=database` |
| `garden-rake explore mongo --inspect` | `garden-rake list mongo --inspect` | `GET /api/v1/offerings/mongo` |
| `garden-rake tend stone-02` | *(zen-only)* | N/A (client-side) |
| `garden-rake tend` (show) | *(zen-only)* | N/A |

**Flags:** `--quiet` / `-q`, `--prefer ssd`, `--anywhere`

---

### 🎁 Offering Services (Installation)

#### Zen Syntax
```bash
garden-rake offer mongodb                    # Offer to tended stone
garden-rake offer mongodb at stone-02        # Offer to specific stone
garden-rake offer mongodb at stone-02 --anywhere-on-fail
```

#### Normative Syntax
```bash
garden-rake services create mongodb
garden-rake services create mongodb --at stone-02
garden-rake services create mongodb --at stone-02 --anywhere-on-fail
```

#### API
```http
POST /api/v1/services
Body: {"offering": "mongodb", "config": {...}}
```

**Response (success):**
```json
{
  "service": "mongodb",
  "job_id": "job_abc123",
  "status": "creating"
}
```

**Suggestions (after success):**
```
✓ Service mongodb offered successfully

Next steps:
  garden-rake observe mongodb      View service details
  garden-rake watch mongodb        Stream logs
  Connect: mongodb://localhost:27017
```

---

### 🛌 Rest (Stop Services)

#### Zen Syntax
```bash
garden-rake rest grafana                     # Let grafana rest
garden-rake rest grafana at stone-02
```

#### Normative Syntax
```bash
garden-rake services stop grafana
garden-rake services stop grafana --at stone-02
```

#### API
```http
POST /api/v1/services/grafana/rest
```

**Response:**
```json
{
  "service": "grafana",
  "status": "resting",
  "message": "Service stopped gracefully"
}
```

**Suggestions:**
```
✓ grafana is now resting

Next steps:
  garden-rake observe grafana      Check service status
  garden-rake wake grafana         Start service again
  garden-rake release grafana      Remove if no longer needed
```

---

### 🌅 Wake (Start Services)

#### Zen Syntax
```bash
garden-rake wake prometheus
garden-rake wake prometheus at stone-03
```

#### Normative Syntax
```bash
garden-rake services start prometheus
garden-rake services start prometheus --at stone-03
```

#### API
```http
POST /api/v1/services/prometheus/wake
```

**Response:**
```json
{
  "service": "prometheus",
  "status": "awake",
  "message": "Service started successfully"
}
```

---

### 🌱 Nourish (Upgrade Services)

#### Zen Syntax
```bash
garden-rake nourish redis                    # Nourish single service
garden-rake nourish redis at stone-02
garden-rake nourish --all                    # Nourish all services
garden-rake nourish --all at stone-02
```

#### Normative Syntax
```bash
garden-rake services update redis
garden-rake services update redis --at stone-02
garden-rake services update --all
garden-rake services update --all --at stone-02
```

#### API
```http
POST /api/v1/services/redis/nourish
POST /api/v1/services/_nourish_all          # All services
```

**Response:**
```json
{
  "service": "redis",
  "job_id": "job_xyz789",
  "status": "nourishing",
  "message": "Upgrade initiated"
}
```

---

### 🍂 Release (Remove Services)

#### Zen Syntax
```bash
garden-rake release mongodb
garden-rake release mongodb at stone-02
```

#### Normative Syntax
```bash
garden-rake services delete mongodb
garden-rake services delete mongodb --at stone-02
```

#### API
```http
DELETE /api/v1/services/mongodb
```

**Response:**
```json
{
  "service": "mongodb",
  "status": "released",
  "message": "Service removed successfully"
}
```

**Suggestions:**
```
✓ mongodb has been released

Your garden now has space for new offerings.

Next steps:
  garden-rake observe              View remaining services
  garden-rake explore              Browse available offerings
```

---

### 👁️ Observe (Snapshot / Awareness)

#### Zen Syntax
```bash
garden-rake observe                          # Observe tended stone (snapshot)
garden-rake observe all                      # Observe all stones (garden snapshot)
garden-rake observe at stone-02              # Stone overview
garden-rake observe mongodb                  # Service details
garden-rake observe mongodb at stone-03      # Service on specific stone
```

#### Normative Syntax
```bash
garden-rake status                           # Tended stone (snapshot)
garden-rake status --all                     # All stones (garden snapshot)
garden-rake status --at stone-02
garden-rake services info mongodb
garden-rake services info mongodb --at stone-03
```

#### API
```http
GET /api/v1/garden                           # Garden overview
GET /api/v1/garden/stones/stone-02           # Stone details
GET /api/v1/services/mongodb                 # Service details
```

**Output Example (tended stone - `observe` with no args):**
```
🪨 stone-01.local
─────────────────────────────────────────────────
Status:     Healthy ✓
Uptime:     3 days, 14 hours
Services:   3 active, 1 resting

mongodb     ✓ awake     (27017)    mem: 128 MB
redis       ✓ awake     (6379)     mem: 64 MB
elasticsearch ✓ awake   (9200)     mem: 2.1 GB
postgres    ⏸ resting

Suggested:
  garden-rake observe all          View all stones
  garden-rake watch                Stream this stone's events
  garden-rake explore              Browse more offerings
```

**Output Example (garden overview - `observe all`):**
```
🏮 Garden Overview
─────────────────────────────────────────────────
3 stones, 12 services (10 awake, 2 resting)

stone-01.local (192.168.1.101)          ✓ healthy
  └─ mongodb (awake) ✓
  └─ redis (awake) ✓
  └─ postgres (resting)

stone-02.local (192.168.1.102)          ✓ healthy
  └─ rabbitmq (awake) ✓
  └─ elasticsearch (awake) ✓

stone-03.local (192.168.1.103)          ⚠ degraded
  └─ grafana (awake) ⚠ high memory
  └─ prometheus (resting)

Suggested:
  garden-rake tend stone-03        Focus on degraded stone
  garden-rake explore              Browse more offerings
  garden-rake watch                Stream garden events
```

**Output Example (garden overview with pond):**
```
🏮 Garden Overview
─────────────────────────────────────────────────
Pond: Active 🔒 (Cornerstone: stone-01)
4 stones (3 secured, 1 plain), 12 services (10 awake, 2 resting)

stone-01.local (Cornerstone)            🔒 ✓ healthy
  └─ mongodb (awake) ✓
  └─ redis (awake) ✓

stone-02.local                          🔒 ✓ healthy
  └─ rabbitmq (awake) ✓
  └─ elasticsearch (awake) ✓

stone-03.local                          ⚠️ PLAIN (not in pond)
  └─ postgres (awake) ✓

stone-04.local                          🔒 ⚠ degraded
  └─ grafana (awake) ⚠ high memory
  └─ prometheus (resting)

Suggested:
  garden-rake invite stone         Add stone-03 to pond
  garden-rake tend stone-04        Focus on degraded stone
  garden-rake explore              Browse more offerings
  garden-rake watch                Stream garden events
```

**Output Example (service details):**
```
🍃 Service: mongodb
─────────────────────────────────────────────────
Status:       Awake ✓ healthy
Stone:        stone-01.local
Offering:     mongodb (mongo:8.0)
Created:      2h ago
Ports:        27017 → 127.0.0.1:27017
Volumes:      /data/mongodb → /data/db
Memory:       512 MB / 2 GB limit

Suggested:
  garden-rake watch mongodb        Stream logs
  garden-rake rest mongodb         Stop service
  garden-rake nourish mongodb      Upgrade to latest
  garden-rake release mongodb      Remove service
```

---

### 🔮 Watch (Stream / Meditation)

#### Zen Syntax
```bash
garden-rake watch                            # Watch tended stone events
garden-rake watch at stone-02                # Watch stone-02 events
garden-rake watch mongodb                    # Watch mongodb logs
garden-rake watch mongodb at stone-03        # Watch service on specific stone
garden-rake watch mongodb until 'ready'      # Watch until condition
garden-rake watch mongodb at stone-03 until 'ready'  # Combined: specific stone + condition
```

#### Normative Syntax
```bash
garden-rake logs
garden-rake logs --at stone-02
garden-rake logs mongodb
garden-rake logs mongodb --at stone-03
garden-rake logs mongodb --until 'ready'
garden-rake logs mongodb --at stone-03 --until 'ready'  # Combined
```

#### API
```http
GET /api/v1/events                           # All events (SSE stream)
GET /api/v1/events?service=mongodb           # Service-specific
GET /api/v1/services/mongodb/logs            # Service logs only
```

**Output Example:**
```
🔮 Watching mongodb...
─────────────────────────────────────────────────
[10:30:23] service.log        MongoDB starting...
[10:30:24] service.log        Waiting for connections on port 27017
[10:30:24] service.log        MongoDB is ready
[10:30:25] service.healthy    Health check passed
[10:30:30] service.log        Connection accepted from 172.17.0.1:54321
^C

Suggested:
  garden-rake observe mongodb      View current status
  garden-rake nourish mongodb      Upgrade service
```

**With `--until` flag:**
```
🔮 Watching mongodb until 'ready'...
─────────────────────────────────────────────────
[10:30:23] service.log        MongoDB starting...
[10:30:24] service.log        MongoDB is ready
✓ Condition met: 'ready' found. Exiting.
```

---

### 🤲 Touch (Deep Inspection)

#### Zen Syntax
```bash
garden-rake touch stone-02                   # Deep inspection of stone
garden-rake touch mongodb at stone-02        # Deep inspection of service
```

#### Normative Syntax
```bash
garden-rake inspect stone-02
garden-rake inspect mongodb --at stone-02
```

#### API
```http
GET /api/v1/garden/stones/stone-02?detailed=true    # Deep stone diagnostics
GET /api/v1/services/mongodb?detailed=true          # Deep service diagnostics
```

**Output Example (stone):**
```
🤲 Touching stone-02.local...
─────────────────────────────────────────────────
Hardware:
  CPU:     Intel Xeon E5-2680 v4 (14 cores, 28 threads)
           Load: 23.5% (1m), 18.2% (5m), 15.7% (15m)
  Memory:  48 GB total, 32 GB used (67%)
           Swap: 16 GB total, 2 GB used
  Disk:    /dev/sda1 (SSD, 960 GB)
           Used: 420 GB (44%), IOPS: 1.2K read, 850 write
  Network: eth0 (1 Gbps)
           RX: 142 MB/s, TX: 87 MB/s

Moss Daemon:
  Version:    0.2.0.42
  Uptime:     12d 8h 42m
  PID:        1234
  Memory:     128 MB
  Threads:    8 active, 2 idle

Containers:
  Runtime:    Docker 25.0.3
  Images:     12 cached (8.2 GB)
  Volumes:    5 active (142 GB)
  Networks:   bridge, host, zen-garden

Security:
  Pond:       Active 🔒 (cert expires in 42 min)
  Cornerstone: No (origin: stone-01)
  Firewall:   Active (22/tcp, 7185/tcp, 7186/udp open)

Suggested:
  garden-rake watch stone-02       Monitor stone events
  garden-rake observe              Return to overview
```

**Output Example (service):**
```
🤲 Touching mongodb at stone-02...
─────────────────────────────────────────────────
Container:
  ID:         abc123def456
  Image:      mongo:8.0 (sha256:789abc...)
  Created:    2h ago
  State:      Running (healthy)
  Restart:    on-failure (max 3)

Resources:
  CPU:        8.5% (340ms / 4000ms limit)
  Memory:     512 MB / 2 GB limit (26%)
  Disk I/O:   142 MB read, 87 MB write
  Network:    12.4 MB in, 8.7 MB out

Ports:
  27017/tcp → 127.0.0.1:27017

Volumes:
  /data/mongodb → /data/db (142 GB used)

Environment:
  MONGO_INITDB_ROOT_USERNAME: admin
  MONGO_INITDB_ROOT_PASSWORD: ******* (hidden)

Health Checks:
  Last:       2s ago ✓ healthy
  Interval:   30s
  Failures:   0 consecutive (3 threshold)

Logs (last 5 lines):
  [10:30:30] Connection accepted from 172.17.0.1:54321
  [10:30:35] Command: find, took 12ms
  [10:30:40] WiredTiger checkpoint completed
  [10:30:45] Connection closed from 172.17.0.1:54321
  [10:30:50] Periodic task: fsync

Suggested:
  garden-rake watch mongodb at stone-02    Stream full logs
  garden-rake nourish mongodb at stone-02  Upgrade service
```

---

### �️ Pond (Security)

#### Philosophy

**Pond** is the trust boundary connecting stones with mTLS authentication. Optional, opt-in after initial setup.

**"Set your stones, make sure everything is working, fill the pond."**

Stones start without pond (frictionless Phase 1), then secure the garden when ready.

**Distributed by Design:**
- Keystone (Pond CA keypair) is replicated to all stones
- Any stone can issue certificates, handle invitations, validate tokens
- Cornerstone = origin stone (audit metadata only, not operational dependency)
- If cornerstone goes down, pond continues operating normally

#### Zen Syntax
```bash
garden-rake place keystone                     # Initialize pond security
garden-rake lift keystone                      # Remove pond entirely

garden-rake invite stone                     # Generate TOTP code for join
garden-rake place stone AJ4R9X               # Join pond with code
garden-rake lift stone stone-03              # Remove stone from pond
```

#### Normative Syntax
```bash
garden-rake pond init
garden-rake pond remove

garden-rake pond invite
garden-rake pond join AJ4R9X
garden-rake pond untrust stone-03
```

#### API
```http
POST /api/v1/pond/init                       # Initialize pond (creates keystone)
DELETE /api/v1/pond                          # Remove pond from all stones

POST /api/v1/pond/invite                     # Generate TOTP invitation code
POST /api/v1/pond/join                       # Join pond with code
DELETE /api/v1/pond/stones/{stone_name}      # Remove stone from pond

GET /api/v1/pond/status                      # Pond health and membership
```

**Response (`POST /api/v1/pond/invite`):**
```json
{
  "code": "AJ4R9X",
  "expires_at": "2026-01-17T10:35:00Z",
  "ttl_seconds": 300,
  "inviter_stone": "stone-01"
}
```

**Response (`GET /api/v1/pond/status`):**
```json
{
  "active": true,
  "cornerstone": "stone-01",
  "stones": [
    {
      "name": "stone-01",
      "is_cornerstone": true,
      "certificate_expires": "2026-01-17T11:30:00Z",
      "joined_at": "2026-01-01T00:00:00Z"
    },
    {
      "name": "stone-02",
      "is_cornerstone": false,
      "certificate_expires": "2026-01-17T11:28:00Z",
      "joined_at": "2026-01-05T14:20:00Z"
    }
  ],
  "tier": "garden-pond",
  "note": "Cornerstone is origin stone (audit metadata only). All stones can issue certificates."
}
```

---

#### Place Keystone (Initialize Pond)

**Zen:**
```bash
$ garden-rake place keystone
🏞️  Filling the pond...
─────────────────────────────────────────────────
Generating Pond CA keypair...
Creating keystone (encrypted with passphrase)...

Enter passphrase (20+ characters): **********************
Confirm passphrase: **********************

✓ Pond initialized on stone-01 (Cornerstone)
✓ Keystone stored: /var/lib/zen-garden/keystone
✓ Certificate issued (expires in 1h, auto-renews)

Your garden is now secured with mTLS.
All stones in the pond can issue certificates and handle invitations.

Next steps:
  garden-rake observe              View pond status
  garden-rake invite stone         Add another stone
```

**Normative:**
```bash
$ garden-rake pond init
[Same output]
```

---

#### Invite Stone (Generate TOTP)

**Zen:**
```bash
$ garden-rake invite stone
🎫 Invitation code generated
─────────────────────────────────────────────────
Code:      AJ4R9X
Expires:   5 minutes from now
Stone:     stone-01 (handling join)

On the new stone, run:
  garden-rake place stone AJ4R9X

Waiting for stone to join...
```

**Normative:**
```bash
$ garden-rake pond invite
[Same output]
```

---

#### Place Stone (Join Pond)

**Zen:**
```bash
$ garden-rake place stone AJ4R9X
🏞️  Joining pond...
─────────────────────────────────────────────────
Broadcasting join request (encrypted)...
stone-01 responded with keystone
Validating certificate...
✓ Joined pond successfully

You are now part of the secured garden.

Next steps:
  garden-rake observe              View your stone status
  garden-rake explore              Browse offerings
```

**Normative:**
```bash
$ garden-rake pond join AJ4R9X
[Same output]
```

---

#### Lift Stone (Remove from Pond)

**Zen:**
```bash
$ garden-rake lift stone stone-03
⚠️  Remove stone-03 from pond?
─────────────────────────────────────────────────
This will:
  • Revoke stone-03's certificate
  • Block stone-03 from accessing secured stones
  • Not affect stone-03's services (they continue running)

Confirm removal (yes/no): yes

✓ stone-03 removed from pond
✓ Certificate revoked

stone-03 is no longer part of the secured garden.
```

**Normative:**
```bash
$ garden-rake pond untrust stone-03
[Same output]
```

---

#### Lift Keystone (Remove Pond)

**Zen:**
```bash
$ garden-rake lift keystone
⚠️  Remove pond security from all stones?
─────────────────────────────────────────────────
This will:
  • Remove mTLS authentication
  • Delete all certificates
  • Revert to open garden (no security)
  • Not affect running services

Affected stones: stone-01, stone-02, stone-04 (3 total)

Confirm pond removal (yes/no): yes

✓ Pond removed from all stones
✓ Keystone deleted
✓ Garden is now open (no security)

You can re-secure later with: garden-rake place keystone
```

**Normative:**
```bash
$ garden-rake pond remove
[Same output]
```

---

### �🌸 Garden (Multi-Stone Topology)

#### Zen Syntax
```bash
garden-rake garden                           # View garden topology
garden-rake garden watch                     # Watch garden-wide events
garden-rake garden observe stone-02          # Observe specific stone from garden
```

#### Normative Syntax
```bash
garden-rake topology
garden-rake topology watch
garden-rake topology info stone-02
```

#### API (Lantern)
```http
GET /api/v1/garden                           # Garden overview
GET /api/v1/garden/stones                    # List all stones
GET /api/v1/garden/stones/stone-02           # Stone details
GET /api/v1/garden/events                    # Garden-wide event stream (SSE)
```

**Output Example:**
```
🌸 Garden Topology
─────────────────────────────────────────────────
Lantern:      lantern-01 (primary)
Stones:       3 healthy, 0 resting
Services:     12 total (10 awake, 2 resting)
Last sync:    5s ago

stone-01.local    192.168.1.101    4 services    ✓ healthy
stone-02.local    192.168.1.102    5 services    ✓ healthy
stone-03.local    192.168.1.103    3 services    ⚠ degraded

Suggested:
  garden-rake tend stone-03        Investigate degraded stone
  garden-rake garden watch         Monitor garden events
  garden-rake observe              View detailed garden state
```

---

### 🧘 Tend (Context Management)

**Tend** sets your focus to a specific stone. All subsequent commands (zen and normative) default to the tended stone.

**Rake auto-tends on startup** to the first responding stone.

#### Zen Syntax
```bash
garden-rake tend                             # Show current context
garden-rake tend stone-02                    # Tend to stone-02
garden-rake tend this                        # Tend to localhost
garden-rake tend auto                        # Auto-discover and tend
```

#### Normative Syntax
```bash
garden-rake context show                     # Show current context
garden-rake context set stone-02             # Set context to stone-02
garden-rake context clear                    # Clear context
```

**Output Example (show):**
```
🧘 Tending to: stone-01.local
─────────────────────────────────────────────────
Endpoint:     http://192.168.1.101:7185
Set:          2m ago
Expires:      88s from now

All commands target this stone unless overridden with 'at <stone>'

Suggested:
  garden-rake observe              View stone's services
  garden-rake explore              Browse offerings
  garden-rake tend stone-02        Switch to another stone
```

**Output Example (switch):**
```
✓ Now tending to: stone-02.local (http://192.168.1.102:7185)

Suggested:
  garden-rake observe              View stone's services
  garden-rake watch                Stream stone events
```

---

## Quiet Mode (Suppress Suggestions)

### Flags (All Equivalent)
```bash
--quiet           # Primary flag (industry standard)
-q                # Short flag
--succinct        # Zen alias
-s                # Succinct short flag
quietly           # Zen positional keyword (zen commands only)
```

### Usage
```bash
# Standard flags (work with all commands)
garden-rake observe --quiet
garden-rake observe -q
garden-rake services create mongodb --at stone-02 -q

# Zen positional keyword (zen commands only)
garden-rake offer mongodb at stone-02 quietly
garden-rake observe quietly
garden-rake watch redis quietly
```

### Environment Variable
```bash
export GARDEN_QUIET=true
garden-rake observe                          # No suggestions
```

### Config File
```yaml
# ~/.config/zen-garden/rake.yaml
quiet: true
```

**Output (quiet mode):**
```
🏮 Garden Overview
─────────────────────────────────────────────────
3 stones, 12 services (10 awake, 2 resting)

stone-01.local    4 services    ✓ healthy
stone-02.local    5 services    ✓ healthy
stone-03.local    3 services    ⚠ degraded

# No suggestions shown
```

---

## HTTP API Reference (v1)

### Versioning Strategy

**Explicit version (recommended for stability):**
```http
GET /api/v1/offerings
POST /api/v1/services
```

**Versionless (redirects to latest stable):**
```http
GET /api/offerings              → 200 OK (responds with v1, header: X-API-Version: v1)
POST /api/services              → 200 OK
```

---

### Moss API (Stone-Local)

#### Stone Metadata
```http
GET /api/v1/stone                            # Identity, capabilities, health, metrics
GET /api/v1/stone/capabilities               # Hardware capabilities (detailed)
GET /api/v1/stone/metrics                    # Current metrics only
```

**Response (`GET /api/v1/stone`):**
```json
{
  "stone_name": "stone-01",
  "version": "0.2.0.42",
  "health": "healthy",
  "uptime_seconds": 86400,
  "capabilities": {
    "cpu": {
      "model": "Apple M1",
      "cores": 8,
      "architecture": "aarch64",
      "features": ["neon", "aes"]
    },
    "memory": {
      "total_mb": 16384
    },
    "disk": {
      "type": "ssd",
      "total_gb": 512
    },
    "gpu": []
  },
  "metrics": {
    "services_count": 3,
    "cpu_usage_percent": 12.5,
    "memory_used_mb": 2048
  }
}
```

---

#### Offerings (Catalog)
```http
GET  /api/v1/offerings?q=database&category=data&tags=nosql
GET  /api/v1/offerings/mongodb
POST /api/v1/offerings/_refresh             # Admin operation (rebuild index)
```

**Response (`GET /api/v1/offerings`):**
```json
{
  "offerings": [
    {
      "name": "mongodb",
      "category": "data",
      "description": "MongoDB document database",
      "tags": ["database", "document", "nosql"],
      "image": "mongo:8.0",
      "ports": [{"container": 27017, "host": 27017}],
      "compatibility": {
        "decision": "pass",
        "reason": null
      }
    }
  ]
}
```

---

#### Services (Runtime)
```http
GET    /api/v1/services?status=awake
GET    /api/v1/services/mongodb
POST   /api/v1/services                     # Offer service
DELETE /api/v1/services/mongodb             # Release service
POST   /api/v1/services/mongodb/rest        # Let service rest
POST   /api/v1/services/mongodb/wake        # Wake service
POST   /api/v1/services/mongodb/nourish     # Nourish service
POST   /api/v1/services/_nourish_all        # Nourish all services
GET    /api/v1/services/mongodb/logs?follow=true&tail=100
```

**Request (`POST /api/v1/services`):**
```json
{
  "offering": "mongodb",
  "config": {
    "environment": {
      "MONGO_INITDB_ROOT_USERNAME": "admin",
      "MONGO_INITDB_ROOT_PASSWORD": "secret"
    },
    "volumes": [
      {"host": "/data/mongodb", "container": "/data/db"}
    ]
  }
}
```

**Response:**
```json
{
  "service": "mongodb",
  "job_id": "job_abc123",
  "status": "creating",
  "message": "Service creation initiated"
}
```

---

#### Events (Observability)
```http
GET /api/v1/events                           # SSE stream (all events)
GET /api/v1/events?service=mongodb           # Filter by service
GET /api/v1/events?type=logs                 # Filter by event type
GET /api/v1/events?type=lifecycle            # Lifecycle events only
```

**SSE Event Types:**
```
event: service.offered
data: {"service": "mongodb", "offering": "mongodb", "timestamp": "2026-01-17T10:30:00Z"}

event: service.creating
data: {"service": "mongodb", "status": "pulling_image", "timestamp": "..."}

event: service.awake
data: {"service": "mongodb", "timestamp": "..."}

event: service.log
data: {"service": "mongodb", "line": "MongoDB starting...", "timestamp": "..."}

event: service.healthy
data: {"service": "mongodb", "timestamp": "..."}

event: service.resting
data: {"service": "mongodb", "timestamp": "..."}

event: service.nourishing
data: {"service": "mongodb", "job_id": "job_xyz", "timestamp": "..."}

event: service.released
data: {"service": "mongodb", "timestamp": "..."}
```

---

#### Jobs (Async Operations)
```http
GET /api/v1/jobs?status=running
GET /api/v1/jobs/job_abc123
```

**Response:**
```json
{
  "job_id": "job_abc123",
  "operation": "offer",
  "service": "mongodb",
  "status": "completed",
  "started_at": "2026-01-17T10:30:00Z",
  "completed_at": "2026-01-17T10:30:15Z",
  "result": {
    "status": "success",
    "message": "Service created successfully"
  }
}
```

---

#### Peer Discovery
```http
GET /api/v1/peers                            # Discover peer stones (UDP broadcast)
```

**Response:**
```json
{
  "peers": [
    {
      "stone_name": "stone-02",
      "endpoint": "http://192.168.1.102:7185",
      "discovered_at": "2026-01-17T10:30:00Z"
    }
  ]
}
```

---

#### Pond Security
```http
POST   /api/v1/pond/init                     # Initialize pond (create keystone)
DELETE /api/v1/pond                          # Remove pond from all stones
POST   /api/v1/pond/invite                   # Generate TOTP invitation code
POST   /api/v1/pond/join                     # Join pond with code
DELETE /api/v1/pond/stones/:stone_name       # Remove stone from pond
GET    /api/v1/pond/status                   # Pond health and membership
```

**Request (`POST /api/v1/pond/init`):**
```json
{
  "passphrase": "my-secure-passphrase-20-chars-minimum"
}
```

**Response:**
```json
{
  "cornerstone": "stone-01",
  "keystone_path": "/var/lib/zen-garden/keystone",
  "certificate_expires": "2026-01-17T11:30:00Z",
  "status": "active",
  "note": "All stones in pond can issue certificates and handle invitations."
}
```

**Request (`POST /api/v1/pond/invite`):**
```json
{}
```

**Response:**
```json
{
  "code": "AJ4R9X",
  "expires_at": "2026-01-17T10:35:00Z",
  "ttl_seconds": 300,
  "inviter_stone": "stone-01"
}
```

**Request (`POST /api/v1/pond/join`):**
```json
{
  "code": "AJ4R9X"
}
```

**Response:**
```json
{
  "stone_name": "stone-02",
  "cornerstone": "stone-01",
  "certificate_expires": "2026-01-17T11:28:00Z",
  "status": "joined"
}
```

**Response (`GET /api/v1/pond/status`):**
```json
{
  "active": true,
  "cornerstone": "stone-01",
  "stones": [
    {
      "name": "stone-01",
      "is_cornerstone": true,
      "certificate_expires": "2026-01-17T11:30:00Z",
      "joined_at": "2026-01-01T00:00:00Z"
    },
    {
      "name": "stone-02",
      "is_cornerstone": false,
      "certificate_expires": "2026-01-17T11:28:00Z",
      "joined_at": "2026-01-05T14:20:00Z"
    }
  ],
  "tier": "garden-pond",
  "note": "Cornerstone is origin stone (audit metadata only). All stones can issue certificates."
}
```

---

#### System Administration
```http
POST /api/v1/system/reconcile                # Reconcile containers with registry
POST /api/v1/system/refresh                  # Refresh moss/rake binaries (dev)
GET  /api/v1/system/templates/:name/sources  # Debug template resolution (dev)
PUT  /api/v1/system/templates/:name/compatibility  # Upload compatibility rules (dev)
```

---

### Lantern API (Garden Registry)

#### Garden Topology
```http
GET  /api/v1/garden                          # Garden overview
GET  /api/v1/garden/stones?health=healthy    # List all stones
POST /api/v1/garden/stones                   # Register stone (heartbeat)
GET  /api/v1/garden/stones/stone-02          # Stone details
GET  /api/v1/garden/events                   # Garden-wide event stream (SSE)
```

**Response (`GET /api/v1/garden`):**
```json
{
  "garden": {
    "stone_count": 3,
    "healthy_stones": 3,
    "total_services": 12,
    "last_updated": "2026-01-17T10:30:00Z"
  },
  "stones": [
    {
      "stone_name": "stone-01",
      "endpoint": "http://192.168.1.101:7185",
      "health": "healthy",
      "services_count": 4,
      "last_heartbeat": "2026-01-17T10:29:55Z"
    }
  ]
}
```

---

#### Service Resolution
```http
GET /api/v1/resolve?service=mongodb          # Resolve service to stone endpoint
```

**Response:**
```json
{
  "service": "mongodb",
  "stone_name": "stone-01",
  "endpoint": "http://192.168.1.101:7185",
  "resolved_at": "2026-01-17T10:30:00Z"
}
```

---

#### Lantern Health
```http
GET /api/v1/lantern                          # Lantern health + election state
```

**Response:**
```json
{
  "lantern_name": "lantern-01",
  "version": "0.2.0.42",
  "role": "primary",
  "election_state": "leader",
  "stones_count": 3,
  "uptime_seconds": 172800
}
```

---

## Scenario Examples (Zen + Normative)

### Scenario 1: First Run (Empty Garden)

#### Zen Path
```bash
# See what's available
$ garden-rake observe
🏮 Garden Overview
─────────────────────────────────────────────────
Stones:    1 (localhost)
Services:  None yet

Your garden is peaceful and empty.

Suggested:
  garden-rake explore              Browse available offerings
  garden-rake offer mongodb        Offer your first service

# Explore offerings
$ garden-rake explore
📚 Offerings Catalog
─────────────────────────────────────────────────
Data:
  mongodb      MongoDB document database
  postgres     PostgreSQL relational database
  redis        Redis in-memory cache

Suggested:
  garden-rake explore mongodb --inspect    See details
  garden-rake offer mongodb                Install mongodb

# Offer mongodb
$ garden-rake offer mongodb
🎁 Offering mongodb to stone-01...
✓ Service created successfully

Next steps:
  garden-rake observe mongodb      View service details
  garden-rake watch mongodb        Stream logs
  Connect: mongodb://localhost:27017

# Watch it start
$ garden-rake watch mongodb until 'ready'
🔮 Watching mongodb until 'ready'...
─────────────────────────────────────────────────
[10:35:42] service.log  MongoDB starting...
[10:35:43] service.log  MongoDB is ready
✓ Condition met. Exiting.

# Observe the result
$ garden-rake observe
🏮 Garden Overview
─────────────────────────────────────────────────
stone-01.local (localhost)                  ✓ healthy
  └─ mongodb (awake) ✓ healthy

Suggested:
  garden-rake watch mongodb        Stream logs
  garden-rake nourish mongodb      Upgrade service
  garden-rake explore              Browse more offerings
```

#### Normative Path
```bash
# Check status
$ garden-rake status
🏮 Garden Overview
─────────────────────────────────────────────────
Stones:    1 (localhost)
Services:  None yet

Suggested:
  garden-rake list                 Browse available offerings
  garden-rake services create mongodb  Install your first service

# List offerings
$ garden-rake list
📚 Offerings Catalog
─────────────────────────────────────────────────
Data:
  mongodb      MongoDB document database
  postgres     PostgreSQL relational database
  redis        Redis in-memory cache

# Create service
$ garden-rake services create mongodb
🎁 Creating mongodb...
✓ Service created successfully

Next steps:
  garden-rake services info mongodb    View service details
  garden-rake logs mongodb             Stream logs

# Stream logs
$ garden-rake logs mongodb --until 'ready'
🔮 Watching mongodb until 'ready'...
─────────────────────────────────────────────────
[10:35:42] service.log  MongoDB starting...
[10:35:43] service.log  MongoDB is ready
✓ Condition met. Exiting.

# Check status
$ garden-rake status
🏮 Garden Overview
─────────────────────────────────────────────────
stone-01.local (localhost)                  ✓ healthy
  └─ mongodb (awake) ✓ healthy
```

---

### Scenario 2: Multi-Stone Garden Management

#### Zen Path
```bash
# View garden
$ garden-rake observe
🏮 Garden Overview
─────────────────────────────────────────────────
3 stones, 12 services (10 awake, 2 resting)

stone-01.local    4 services    ✓ healthy
stone-02.local    5 services    ✓ healthy
stone-03.local    3 services    ⚠ degraded

Suggested:
  garden-rake tend stone-03        Focus on degraded stone

# Tend to degraded stone
$ garden-rake tend stone-03
✓ Now tending to: stone-03.local

# Observe it
$ garden-rake observe
🪨 Stone: stone-03.local
─────────────────────────────────────────────────
Health:   ⚠ Degraded
Reason:   High memory usage (92%)

  grafana        awake    ⚠ 3.2 GB memory
  prometheus     resting

Suggested:
  garden-rake rest grafana         Free memory
  garden-rake watch stone-03       Monitor events

# Let grafana rest
$ garden-rake rest grafana
✓ grafana is now resting

# Wake prometheus
$ garden-rake wake prometheus
✓ prometheus is awake

# Observe again
$ garden-rake observe
🪨 Stone: stone-03.local
─────────────────────────────────────────────────
Health:   ✓ Healthy

  grafana        resting
  prometheus     awake    ✓

# Offer to different stone
$ garden-rake offer redis at stone-02
🎁 Offering redis to stone-02...
✓ Service created successfully
```

#### Normative Path
```bash
# Check topology
$ garden-rake topology
🌸 Garden Topology
─────────────────────────────────────────────────
3 stones, 12 services

stone-01.local    4 services    ✓ healthy
stone-02.local    5 services    ✓ healthy
stone-03.local    3 services    ⚠ degraded

# Check degraded stone
$ garden-rake status --at stone-03
🪨 Stone: stone-03.local
─────────────────────────────────────────────────
Health:   ⚠ Degraded

  grafana        awake    ⚠ 3.2 GB memory
  prometheus     resting

# Stop grafana
$ garden-rake services stop grafana --at stone-03
✓ grafana stopped

# Start prometheus
$ garden-rake services start prometheus --at stone-03
✓ prometheus started

# Create service on different stone
$ garden-rake services create redis --at stone-02
✓ Service created on stone-02
```

---

### Scenario 3: Debugging & Monitoring

#### Zen Path
```bash
# Watch garden events
$ garden-rake garden watch
🌸 Watching garden events...
─────────────────────────────────────────────────
[11:10:00] stone-01  service.awake     mongodb
[11:10:05] stone-02  service.resting   elasticsearch
[11:10:10] stone-01  service.log       [mongodb] Connection accepted
[11:10:12] stone-03  service.nourishing grafana
[11:10:15] stone-03  service.awake     grafana (upgraded)
^C

# Watch specific service on remote stone
$ garden-rake watch redis at stone-02
🔮 Watching redis on stone-02...
─────────────────────────────────────────────────
[11:15:20] service.log  Redis 7.2 ready
[11:15:25] service.log  Accepted connection from 172.17.0.1
^C

# Observe service details
$ garden-rake observe redis at stone-02
🍃 Service: redis
─────────────────────────────────────────────────
Status:       Awake ✓
Stone:        stone-02.local
Ports:        6379 → 127.0.0.1:6379
Memory:       128 MB / 512 MB limit

Suggested:
  garden-rake watch redis at stone-02    Stream logs
  garden-rake nourish redis at stone-02  Upgrade service
```

#### Normative Path
```bash
# Stream garden events
$ garden-rake topology watch
🌸 Watching garden events...
─────────────────────────────────────────────────
[11:10:00] stone-01  service.awake     mongodb
[11:10:05] stone-02  service.resting   elasticsearch
^C

# Stream service logs
$ garden-rake logs redis --at stone-02
🔮 Streaming logs from redis...
─────────────────────────────────────────────────
[11:15:20] service.log  Redis 7.2 ready
[11:15:25] service.log  Accepted connection
^C

# Get service info
$ garden-rake services info redis --at stone-02
🍃 Service: redis
─────────────────────────────────────────────────
Status:       Running ✓
Stone:        stone-02.local
Ports:        6379 → 127.0.0.1:6379
```

---

### Scenario 4: Batch Operations

#### Zen Path
```bash
# Nourish all services
$ garden-rake nourish --all
🌱 Nourishing all services...
─────────────────────────────────────────────────
mongodb      → Upgrade initiated (job_001)
redis        → Upgrade initiated (job_002)
postgres     → Already latest version

2 services nourishing, 1 up-to-date

Suggested:
  garden-rake watch                Stream events
  garden-rake observe              Check status

# Nourish all on specific stone
$ garden-rake nourish --all at stone-02
🌱 Nourishing all services on stone-02...
─────────────────────────────────────────────────
rabbitmq         → Upgrade initiated
elasticsearch    → Upgrade initiated

# Watch until all complete
$ garden-rake watch at stone-02 until 'completed'
🔮 Watching stone-02 until 'completed'...
─────────────────────────────────────────────────
[11:20:15] rabbitmq      Pulling image...
[11:20:22] rabbitmq      Upgrade completed
[11:20:25] elasticsearch Pulling image...
[11:20:35] elasticsearch Upgrade completed
✓ Condition met. Exiting.
```

#### Normative Path
```bash
# Update all services
$ garden-rake services update --all
🌱 Updating all services...
─────────────────────────────────────────────────
mongodb      → Upgrade initiated (job_001)
redis        → Upgrade initiated (job_002)
postgres     → Already latest version

# Update all on specific stone
$ garden-rake services update --all --at stone-02
🌱 Updating all services on stone-02...
─────────────────────────────────────────────────
rabbitmq         → Upgrade initiated
elasticsearch    → Upgrade initiated

# Monitor progress
$ garden-rake logs --at stone-02 --until 'completed'
🔮 Streaming logs until 'completed'...
─────────────────────────────────────────────────
[11:20:22] rabbitmq      Upgrade completed
[11:20:35] elasticsearch Upgrade completed
✓ Condition met. Exiting.
```

---

### Scenario 5: Quiet Mode (Scripting)

#### Zen Path (with quietly)
```bash
#!/bin/bash
# setup-garden.sh - Provision services across stones

# Zen quiet mode uses 'quietly' positional keyword
garden-rake offer mongodb at stone-01 quietly
garden-rake offer redis at stone-01 quietly
garden-rake offer postgres at stone-02 quietly
garden-rake offer rabbitmq at stone-02 quietly

# Wait for all to be ready
garden-rake watch at stone-01 until 'healthy' quietly
garden-rake watch at stone-02 until 'healthy' quietly

# Verify (JSON output for parsing)
garden-rake observe quietly --output json | jq '.stones[].services_count'
```

#### Normative Path (with --quiet)
```bash
#!/bin/bash
# Provision with standard syntax

garden-rake services create mongodb --at stone-01 -q
garden-rake services create redis --at stone-01 -q
garden-rake services create postgres --at stone-02 -q
garden-rake services create rabbitmq --at stone-02 -q

# Monitor
garden-rake logs --at stone-01 --until 'healthy' -q
garden-rake logs --at stone-02 --until 'healthy' -q

# Verify
garden-rake status -q --output json | jq '.stones[].services_count'
```

---

### Scenario 6: Securing Garden with Pond

#### Zen Path
```bash
# Initial setup without security (Phase 1)
garden-rake offer mongodb at stone-01
garden-rake offer redis at stone-02
garden-rake observe

# Verify everything works, then secure
garden-rake place keystone
# Enter passphrase (20+ characters): **********************
# ✓ Pond initialized on stone-01 (Cornerstone)

# Add stone-02 to pond
# (on stone-01)
garden-rake invite stone
# Code: AJ4R9X (expires in 5 minutes)

# (on stone-02)
garden-rake place stone AJ4R9X
# ✓ Joined pond successfully

# View secured garden
garden-rake observe
# Pond: Active 🔒 (Cornerstone: stone-01)
# 2 stones (2 secured, 0 plain), 2 services

# Later: add new stone
garden-rake invite stone  # on stone-01
garden-rake place stone BK8T2Y  # on stone-03

# Remove compromised stone
garden-rake lift stone stone-03
# ⚠️ Remove stone-03 from pond?
# Confirm: yes
# ✓ stone-03 removed from pond
```

#### Normative Path
```bash
# Phase 1 setup
garden-rake services create mongodb --at stone-01
garden-rake services create redis --at stone-02
garden-rake status

# Initialize security
garden-rake pond init
# (passphrase prompt)

# Add stones
garden-rake pond invite  # on stone-01
garden-rake pond join AJ4R9X  # on stone-02

# View status
garden-rake status
# Shows pond membership

# Management
garden-rake pond invite  # add stone-03
garden-rake pond join BK8T2Y  # on stone-03
garden-rake pond untrust stone-03  # remove later
```

---

## Command Summary Matrix

| **Intent** | **Zen Syntax** | **Normative Syntax** | **HTTP API** |
|------------|----------------|----------------------|--------------|
| Browse catalog | `explore` | `list` | `GET /api/v1/offerings` |
| Search catalog | `explore database` | `list database` | `GET /api/v1/offerings?q=database` |
| Inspect offering | `explore mongo --inspect` | `list mongo --inspect` | `GET /api/v1/offerings/mongo` |
| Install service | `offer mongo at stone-02` | `services create mongo --at stone-02` | `POST /api/v1/services` |
| Stop service | `rest grafana at stone-02` | `services stop grafana --at stone-02` | `POST /api/v1/services/grafana/rest` |
| Start service | `wake redis at stone-02` | `services start redis --at stone-02` | `POST /api/v1/services/redis/wake` |
| Upgrade service | `nourish postgres at stone-02` | `services update postgres --at stone-02` | `POST /api/v1/services/postgres/nourish` |
| Upgrade all | `nourish --all at stone-02` | `services update --all --at stone-02` | `POST /api/v1/services/_nourish_all` |
| Remove service | `release mongo at stone-02` | `services delete mongo --at stone-02` | `DELETE /api/v1/services/mongo` |
| View stone | `observe` | `status` | `GET /api/v1/garden/stones/{tended}` |
| View garden | `observe all` | `status --all` | `GET /api/v1/garden` |
| View specific stone | `observe at stone-02` | `status --at stone-02` | `GET /api/v1/garden/stones/stone-02` |
| View service | `observe mongo at stone-02` | `services info mongo --at stone-02` | `GET /api/v1/services/mongo` |
| Deep inspect stone | `touch stone-02` | `inspect stone-02` | `GET /api/v1/garden/stones/stone-02?detailed=true` |
| Deep inspect service | `touch mongo at stone-02` | `inspect mongo --at stone-02` | `GET /api/v1/services/mongo?detailed=true` |
| Initialize pond | `place keystone` | `pond init` | `POST /api/v1/pond/init` |
| Remove pond | `lift keystone` | `pond remove` | `DELETE /api/v1/pond` |
| Invite stone | `invite stone` | `pond invite` | `POST /api/v1/pond/invite` |
| Join pond | `place stone [code]` | `pond join [code]` | `POST /api/v1/pond/join` |
| Remove from pond | `lift stone stone-03` | `pond untrust stone-03` | `DELETE /api/v1/pond/stones/stone-03` |
| Pond status | `observe` (shows pond) | `status` (shows pond) | `GET /api/v1/pond/status` |
| Stream events | `watch at stone-02` | `logs --at stone-02` | `GET /api/v1/events` (SSE) |
| Stream service | `watch mongo at stone-02` | `logs mongo --at stone-02` | `GET /api/v1/services/mongo/logs` (SSE) |
| View topology | `garden` | `topology` | `GET /api/v1/garden` (Lantern) |
| Garden events | `garden watch` | `topology watch` | `GET /api/v1/garden/events` (SSE) |
| Set context | `tend stone-02` | `context set stone-02` | N/A (client-side) |
| Show context | `tend` | `context show` | N/A |
| Clear context | *(implicit)* | `context clear` | N/A |

---

## Help Text Examples

### Top-Level Help
```bash
$ garden-rake --help

garden-rake 0.2.0
Tend to your Zen Garden — offer services, observe stones, cultivate your ecosystem

USAGE:
    garden-rake [OPTIONS] <COMMAND>

COMMANDS:
    Zen Verbs (natural syntax):
      tend           Tend to a stone (set context)
      explore        Explore offerings catalog (alias: list)
      offer          Offer a service to your stone
      rest           Let a service rest (alias: stop)
      wake           Wake a service (alias: start)
      nourish        Nourish a service (alias: update, upgrade)
      release        Release a service (alias: delete, remove)
      observe        Observe services or stones (alias: status)
      watch          Watch events in real-time (alias: logs)
      touch          Deep inspect stone or service (alias: inspect)
      place          Place keystone (init pond) or stone (join pond)
      lift           Lift keystone (remove pond) or stone (untrust)
      invite         Invite stone to join pond
      garden         View garden topology (alias: topology)
    
    Standard Commands (flag syntax):
      services       Service management (create, stop, start, update, delete)
      list           List offerings (alias for explore)
      status         Show status (alias for observe)
      logs           Stream logs (alias for watch)
      inspect        Deep diagnostics (alias for touch)
      pond           Pond security (init, invite, join, untrust, remove)
      context        Context management (set, show, clear)
      topology       Garden topology (alias for garden)
    
    System:
      reconcile      Reconcile containers with registry
      refresh        Refresh system components

OPTIONS:
    -q, --quiet, --succinct    Suppress helpful suggestions
    -h, --help                 Print help
    -V, --version              Print version

EXAMPLES:
    # Zen syntax (natural language)
    garden-rake explore
    garden-rake offer mongodb at stone-02
    garden-rake observe
    garden-rake watch mongodb

    # Standard syntax (explicit flags)
    garden-rake list
    garden-rake services create mongodb --at stone-02
    garden-rake status
    garden-rake logs mongodb

ENVIRONMENT:
    GARDEN_STONE     Default stone endpoint
    GARDEN_QUIET     Suppress suggestions (true/false)
    RUST_LOG         Log level (trace, debug, info, warn, error)

Learn more: https://zen-garden.dev/docs
```

---

### Zen Command Help
```bash
$ garden-rake offer --help

garden-rake-offer
Offer a service to your stone

USAGE:
    garden-rake offer <NAME> [at <STONE>] [OPTIONS]

ARGS:
    <NAME>    Offering name (e.g., mongodb, redis, postgres)

SYNTAX:
    at <STONE>    Target a specific stone
                  Examples: "at stone-02", "at 192.168.1.102"
    quietly       Suppress suggestions (zen style)

OPTIONS:
    --anywhere-on-fail    Auto-recommend alternatives on compatibility failure
    -q, --quiet           Suppress suggestions (standard style)
    -h, --help            Print help

EXAMPLES:
    garden-rake offer mongodb
    garden-rake offer mongodb at stone-02
    garden-rake offer mongodb at stone-02 quietly
    garden-rake offer mongodb at stone-02 --anywhere-on-fail quietly

PHILOSOPHY:
    "Offer" means to present a gift to your stone. The moss daemon receives
    your offering and nurtures it into a running service. This is installation
    as an act of care, not deployment as a mechanical process.
```

---

### Normative Command Help
```bash
$ garden-rake services create --help

garden-rake-services-create
Create a service from an offering

USAGE:
    garden-rake services create <NAME> [OPTIONS]

ARGS:
    <NAME>    Offering name (e.g., mongodb, redis, postgres)

OPTIONS:
    --at <STONE>          Target a specific stone
    --anywhere-on-fail    Auto-recommend alternatives on compatibility failure
    -q, --quiet           Suppress suggestions
    -h, --help            Print help

EXAMPLES:
    garden-rake services create mongodb
    garden-rake services create mongodb --at stone-02
    garden-rake services create mongodb --at stone-02 --quiet

NOTE:
    This command is an alias for 'garden-rake offer'. Use whichever feels
    more natural. Zen verbs use natural syntax (at stone-02), standard
    commands use flags (--at stone-02).
```

---

## Implementation Checklist

### Phase 1: Core API (Moss + Lantern)
- [ ] Implement `/api/v1/*` routes in Moss
- [ ] Implement zen sub-resource actions: `/rest`, `/wake`, `/nourish`
- [ ] Update event types: `service.offered`, `service.resting`, etc.
- [ ] Consolidate `/api/v1/stone` endpoint
- [ ] Implement Lantern `/api/v1/garden/*` routes
- [ ] Add versionless route handling (respond with v1, header)
- [ ] OpenAPI spec for v1 API

### Phase 2: CLI Dual Syntax
- [ ] Implement zen commands with positional "at" parsing
- [ ] Implement zen "quietly" positional keyword
- [ ] Implement `services` subcommand tree with `--at` flags
- [ ] Add command aliases (explore↔list, offer↔create, etc.)
- [ ] Implement `--quiet` / `-q` / `--succinct` / `-s` flag handling
- [ ] Environment variable support (`GARDEN_QUIET`)
- [ ] Syntax validation (reject zen verbs with `--at`, reject normative with positional)
- [ ] Context-aware help text showing both syntaxes

### Phase 3: Pond Security
- [ ] Implement `/api/v1/pond/*` routes in Moss
- [ ] `place keystone` / `pond init` - Initialize pond with keystone creation
- [ ] `invite stone` / `pond invite` - Generate TOTP codes
- [ ] `place stone [code]` / `pond join [code]` - Join pond with code validation
- [ ] `lift stone` / `pond untrust` - Remove stone from pond, revoke certificate
- [ ] `lift keystone` / `pond remove` - Remove pond from all stones
- [ ] Certificate issuance and auto-renewal (1h validity, renew every 30min)
- [ ] mTLS handshake validation
- [ ] Pond status in `observe` output (🔒 secured vs plain stones)
- [ ] TOTP code generation and time validation (RFC 6238)

### Phase 4: Suggestions System
- [ ] Implement suggestion engine (state machine)
- [ ] Context-aware suggestion logic per command
- [ ] Terminal detection (suppress in pipes/CI)
- [ ] Priority/relevance scoring
- [ ] Progressive disclosure (max 3-4 suggestions)
- [ ] Quiet mode integration

### Phase 5: Observe/Watch/Touch Enhancements
- [ ] `observe` with no args → garden view
- [ ] `observe at <stone>` → stone view
- [ ] `observe <service>` → service details
- [ ] `touch stone` → deep stone diagnostics (hardware, daemon, containers, security)
- [ ] `touch <service>` → deep service diagnostics (container details, resources, health, logs tail)
- [ ] `watch` SSE streaming with `--until` condition
- [ ] Rich formatting (emojis, colors, tables)
- [ ] JSON output mode (`--output json`)

### Phase 6: Documentation
- [ ] Update primary docs to zen-first examples
- [ ] Create side-by-side reference (zen + normative)
- [ ] API reference documentation
- [ ] Migration guide (v0 → v1)
- [ ] Philosophy explainer
- [ ] Pond security guide

### Phase 7: Testing & Polish
- [ ] Unit tests for positional "at" parsing
- [ ] Integration tests for dual syntax
- [ ] E2E tests (zen path, normative path, mixed)
- [ ] Error message quality (helpful, actionable)
- [ ] Performance testing (SSE streams, large gardens)

---

## Success Criteria

1. **✅ Dual syntax works flawlessly** — zen and normative paths both feel native
2. **✅ Self-teaching** — users learn patterns through consistency (zen = positional, normative = flags)
3. **✅ Helpful by default** — suggestions guide next steps without being overwhelming
4. **✅ Scriptable** — quiet mode + JSON output + exit codes work reliably
5. **✅ RESTful API** — follows HTTP semantics, versioned, documented
6. **✅ Zen philosophy preserved** — commands reinforce the garden metaphor
7. **✅ No confusion** — syntax mixing is rejected with clear error messages

---

**End of Proposal**

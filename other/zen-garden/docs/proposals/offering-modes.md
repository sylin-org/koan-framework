# Zen Garden Offering Modes Specification

**Planted, Adopted, and Borrowed: Three ways to bring services into your garden**

**Status:** Proposal  
**Date:** January 2026  
**Authors:** Collaborative design session

---

## Table of Contents

1. [Overview](#overview)
2. [The Three Modes](#the-three-modes)
3. [Planted Offerings](#planted-offerings)
4. [Adopted Offerings](#adopted-offerings)
5. [Borrowed Offerings](#borrowed-offerings)
6. [Human Discovery Layer](#human-discovery-layer)
7. [CLI Reference](#cli-reference)
8. [API Specification](#api-specification)
9. [Configuration](#configuration)

---

## Overview

### The Problem

Zen Garden began as a Docker orchestrator. But Docker isn't always the right answer:

| Scenario | Docker | Native | External |
|----------|--------|--------|----------|
| GPU workloads (CUDA) | 10-20% overhead, driver complexity | Full performance | N/A |
| Existing installations | Duplicate, waste resources | Use what's there | N/A |
| Network devices (NAS) | Can't containerize | Can't install | Just announce |
| Gaming rig sharing AI | WSL2 friction, VRAM limits | Direct CUDA access | N/A |

**The insight:** Moss shouldn't just orchestrate containers. It should coordinate services—regardless of how they're deployed.

### The Solution: Three Offering Modes

```
┌─────────────────────────────────────────────────────────────────┐
│                         MOSS                                    │
│                   (service coordinator)                         │
├─────────────────────────────────────────────────────────────────┤
│                                                                 │
│   PLANTED              ADOPTED              BORROWED            │
│   (containers)         (native)             (external)          │
│                                                                 │
│   ┌─────────┐         ┌─────────┐         ┌─────────┐          │
│   │ mongodb │         │ ollama  │         │ NAS     │          │
│   │ redis   │         │ postgres│         │ printer │          │
│   │ minio   │         │ (CUDA)  │         │ IoT hub │          │
│   └─────────┘         └─────────┘         └─────────┘          │
│                                                                 │
│   Full lifecycle      Configurable         Announce only        │
│   Pull, start, stop   • Monitor always     Register endpoint    │
│   Health, restart     • Start/stop opt-in  Optional health ping │
│   Update              • No update          No lifecycle         │
│                                                                 │
└─────────────────────────────────────────────────────────────────┘
```

| Mode | Source | Lifecycle Control | Moss Role |
|------|--------|-------------------|-----------|
| **Planted** | Docker container | Full | Orchestrator |
| **Adopted** | Native process on this machine | Configurable | Monitor + Coordinator |
| **Borrowed** | External network device | None | Gateway / Announcer |

### Why "Borrowed"?

From the Japanese garden concept **shakkei** (借景), meaning "borrowed scenery." 

A traditional zen garden might frame a distant mountain as part of its composition. The mountain isn't in your garden. You don't own it. You don't control it. But it's part of your garden's view.

Your NAS is borrowed scenery. It's not on any stone. Moss just knows it exists and makes it visible to the garden.

### Docker Is Optional

A Stone doesn't require Docker at all. Moss can run with only adopted and borrowed offerings:

```
┌─────────────────────────────────────────────────────────────────┐
│                    STONE: grandmas-laptop                       │
│                    Windows 10, No Docker                        │
├─────────────────────────────────────────────────────────────────┤
│                                                                 │
│   PLANTED: (none - no Docker installed)                         │
│                                                                 │
│   ADOPTED (Native Windows)                                      │
│   ┌─────────────────────────────────────────────────────────┐  │
│   │  ollama              (simple local AI chat)             │  │
│   └─────────────────────────────────────────────────────────┘  │
│                                                                 │
│   BORROWED (Family NAS visible through this Stone)              │
│   ┌─────────────────────────────────────────────────────────┐  │
│   │  family-nas          (photo backup, shared files)       │  │
│   └─────────────────────────────────────────────────────────┘  │
│                                                                 │
└─────────────────────────────────────────────────────────────────┘
```

**Installation tiers:**

| Setup | Docker | Planted | Adopted | Borrowed |
|-------|--------|---------|---------|----------|
| Full | ✓ | ✓ | ✓ | ✓ |
| Native only | ✗ | ✗ | ✓ | ✓ |
| Gateway only | ✗ | ✗ | ✗ | ✓ |

The gateway-only mode is useful for a Raspberry Pi that just announces network devices to the garden—no local services, just borrowed scenery.

---

## The Three Modes

### Comparison

| Aspect | Planted | Adopted | Borrowed |
|--------|---------|---------|----------|
| **Where it runs** | Docker on a Stone | Native on a Stone | External device |
| **Installation** | Moss pulls image | Already installed | Already exists |
| **Start/Stop** | Moss controls | Configurable | No control |
| **Restart on failure** | Automatic | Configurable | No |
| **Updates** | Moss can update | Manual (notify only) | No |
| **Health checks** | Container health | HTTP/process check | Optional ping |
| **Uninstall** | Moss removes | Moss stops monitoring | Moss forgets |

### When to Use Each

**Planted** — Best for:
- Services you want fully managed
- Easy deployment from catalog
- Isolation and reproducibility
- Services without special hardware needs

**Adopted** — Best for:
- GPU workloads (CUDA, ROCm)
- Services already installed and configured
- Performance-critical applications
- Windows native applications

**Borrowed** — Best for:
- Network-attached storage (NAS)
- Printers and IoT devices
- Services on devices where Moss can't be installed
- Third-party appliances

### Coexistence: All Three Modes on One Stone

A single Stone running Moss can have all three modes simultaneously:

```
┌─────────────────────────────────────────────────────────────────┐
│                    STONE: gaming-pc                             │
│                    Windows 11, RTX 4090                         │
├─────────────────────────────────────────────────────────────────┤
│                                                                 │
│   PLANTED (Docker Desktop)                                      │
│   ┌─────────────────────────────────────────────────────────┐  │
│   │  mongodb       redis        postgres      minio         │  │
│   │  (container)   (container)  (container)   (container)   │  │
│   └─────────────────────────────────────────────────────────┘  │
│                                                                 │
│   ADOPTED (Native Windows)                                      │
│   ┌─────────────────────────────────────────────────────────┐  │
│   │  ollama                    postgresql                   │  │
│   │  (CUDA direct, 48GB VRAM)  (Windows service)            │  │
│   └─────────────────────────────────────────────────────────┘  │
│                                                                 │
│   BORROWED (External devices this Stone announces)              │
│   ┌─────────────────────────────────────────────────────────┐  │
│   │  nas.local                 printer.local                │  │
│   │  (SMB, NFS)                (IPP)                        │  │
│   └─────────────────────────────────────────────────────────┘  │
│                                                                 │
└─────────────────────────────────────────────────────────────────┘
```

**Why this matters:**

| Service | Mode | Reason |
|---------|------|--------|
| ollama | Adopted | Direct CUDA access, no Docker GPU overhead |
| mongodb | Planted | Easy lifecycle, don't need GPU |
| redis | Planted | Easy lifecycle, ephemeral cache |
| postgresql | Adopted | Already installed, configured, has data |
| NAS | Borrowed | Can't install Moss on Synology |

**The gaming PC scenario:**
- João's gaming rig runs Windows with Docker Desktop
- Native ollama uses the GPU at full performance (adopted)
- Docker handles databases and caches (planted)
- His NAS is announced through this Stone (borrowed)
- One Stone, three modes, best tool for each job

### Unified Visibility

All three modes appear in the same views:

```bash
$ garden-rake observe

STONE: gaming-pc                    Windows 11 / Docker Desktop
───────────────────────────────────────────────────────────────────
OFFERINGS
  Planted (Docker)
    mongodb            [thriving]    database:mongodb
    redis              [thriving]    cache:redis
    postgres           [thriving]    database:postgresql
    minio              [thriving]    storage:s3
    
  Adopted (Native)  
    ollama             [thriving]    ai:chat, ai:embeddings, ai:vision
                       CUDA 12.4     48GB VRAM, managed
    postgresql         [dormant]     database:postgresql
                       Windows svc   unmanaged
    
  Borrowed (External)
    synology-nas       [reachable]   storage:smb, storage:nfs
    brother-printer    [reachable]   printing:ipp
```

Apps don't care which mode an offering uses. They resolve `zen-garden:mongodb` and get a connection string.

---

## Planted Offerings

### Overview

Planted offerings are Docker containers managed entirely by Moss. This is the original Zen Garden model.

### Lifecycle

```
┌─────────────┐
│   ABSENT    │ ← Not installed
└──────┬──────┘
       │ garden-rake offer
       ▼
┌─────────────┐
│  PLANTING   │ ← Pulling image, creating container
└──────┬──────┘
       │
       ▼
┌─────────────┐
│  THRIVING   │ ← Running and healthy
└──────┬──────┘
       │
       ├─────────────────┬─────────────────┐
       │                 │                 │
       ▼                 ▼                 ▼
┌─────────────┐   ┌─────────────┐   ┌─────────────┐
│   DORMANT   │   │  ATTENTION  │   │  RELEASED   │
│  (stopped)  │   │  (unhealthy)│   │  (removed)  │
└─────────────┘   └─────────────┘   └─────────────┘
```

### Commands

```bash
# Install from catalog
garden-rake offer mongodb
garden-rake offer mongodb at stone-02

# Stop (preserves data)
garden-rake rest mongodb

# Start
garden-rake wake mongodb

# Update to latest
garden-rake nourish mongodb

# Remove completely
garden-rake release mongodb
```

### Manifest

Planted offerings use the standard frontmatter manifest:

```yaml
# mongodb.frontmatter.yaml
name: mongodb
category: database
image: mongo:7
ports:
  mongodb: 27017
health:
  test: ["CMD", "mongosh", "--eval", "db.adminCommand('ping')"]
  interval: 30s
  timeout: 10s
```

### Moss Responsibilities

| Action | Moss Does |
|--------|-----------|
| Install | Pull image, create container, configure network |
| Start | `docker start` |
| Stop | `docker stop` (graceful) |
| Restart | Automatic on failure (configurable) |
| Update | Pull new image, recreate container |
| Health | Run health check, report status |
| Remove | Stop container, remove container, optionally remove volume |

---

## Adopted Offerings

### Overview

Adopted offerings are native processes already running on the Stone. Moss monitors them, announces them to the garden, and optionally controls their lifecycle.

### Use Cases

1. **GPU workloads** — Native ollama with direct CUDA access
2. **Existing databases** — PostgreSQL installed via apt/brew
3. **Windows services** — Native Windows applications
4. **Performance-critical** — Avoid container overhead

### Lifecycle

```
┌─────────────┐
│  UNKNOWN    │ ← Running but not in garden
└──────┬──────┘
       │ garden-rake adopt
       ▼
┌─────────────┐
│  ADOPTED    │ ← Moss monitoring
└──────┬──────┘
       │
       ├─────────────────┬─────────────────┐
       │                 │                 │
       ▼                 ▼                 ▼
┌─────────────┐   ┌─────────────┐   ┌─────────────┐
│  THRIVING   │   │   DORMANT   │   │  DISOWNED   │
│  (running)  │   │  (stopped)  │   │  (removed)  │
└─────────────┘   └─────────────┘   └─────────────┘
```

### Commands

```bash
# Adopt a running service
garden-rake adopt ollama at localhost:11434

# Adopt with lifecycle control
garden-rake adopt ollama at localhost:11434 --managed

# Adopt by auto-detection
garden-rake adopt ollama    # Moss scans known ports

# Stop monitoring
garden-rake disown ollama

# If managed: stop/start work
garden-rake rest ollama     # Runs configured stop command
garden-rake wake ollama     # Runs configured start command
```

### Adoption Flow

```bash
$ garden-rake adopt ollama at localhost:11434

ADOPT OFFERING
───────────────────────────────────────────────
  Offering:      ollama
  Type:          native (not managed by Moss)
  Endpoint:      http://localhost:11434
  
  Detecting capabilities...
  ✓ ai:chat
  ✓ ai:embeddings (nomic-embed-text loaded)
  ✓ ai:vision (llava loaded)
  
  Health check:  GET /api/tags → 200 OK
  
  Moss will:
    • Monitor health (every 30s)
    • Announce to garden (Lantern)
    • Report capabilities to apps
    
  Moss will NOT:
    • Start or stop ollama
    • Update ollama
    • Restart on failure
    
  Want Moss to manage lifecycle? Re-run with --managed
    
Adopt? [Y/n]: y
✓ ollama adopted
✓ Announced to garden: ai:chat, ai:embeddings, ai:vision
```

### Managed Adoption

```bash
$ garden-rake adopt ollama at localhost:11434 --managed

ADOPT OFFERING (MANAGED)
───────────────────────────────────────────────
  Offering:      ollama
  Type:          native (managed by Moss)
  Endpoint:      http://localhost:11434
  
  Detecting platform... Windows 11
  
  Lifecycle commands needed:
  
  Start command [net start ollama]: 
  Stop command [net stop ollama]: 
  
  Or specify process to manage:
  Executable [C:\Program Files\Ollama\ollama.exe]: 
  Arguments [serve]: 
  
  Restart on failure? [Y/n]: y
  Max restarts (0=unlimited) [3]: 
  
  Moss will:
    • Monitor health (every 30s)
    • Announce to garden
    • Start/stop on command
    • Restart on failure (max 3 times)
    
Adopt? [Y/n]: y
✓ ollama adopted (managed)
✓ Lifecycle commands configured
```

### Manifest Format

```yaml
# /etc/zen-garden/adopted/ollama.adopted.yaml

offering:
  name: ollama
  type: adopted
  adopted_at: 2026-01-20T15:30:00Z
  
endpoint:
  host: localhost
  port: 11434
  protocol: http
  
detection:
  # How Moss finds this service if not explicitly specified
  endpoints:
    - http://localhost:11434
    - http://127.0.0.1:11434
  process_name: ollama       # Windows: ollama.exe
  
health:
  endpoint: /api/tags
  method: GET
  interval_seconds: 30
  timeout_seconds: 5
  healthy_status: [200]
  
capabilities:
  # Static capabilities (always available when healthy)
  static:
    - ai:chat
    
  # Dynamic capabilities (queried from service)
  dynamic:
    endpoint: /api/tags
    parser: ollama_models
    # Parser extracts: ai:embeddings if embedding model, ai:vision if vision model
    
lifecycle:
  # Is Moss allowed to start/stop this service?
  managed: true
  
  # Platform-specific commands
  windows:
    start: "net start ollama"
    stop: "net stop ollama"
    # Alternative: direct process management
    # executable: "C:\\Program Files\\Ollama\\ollama.exe"
    # arguments: ["serve"]
    
  linux:
    start: "systemctl start ollama"
    stop: "systemctl stop ollama"
    # Alternative:
    # start: "/usr/local/bin/ollama serve &"
    # stop: "pkill ollama"
    
  darwin:
    start: "brew services start ollama"
    stop: "brew services stop ollama"
    
  # Restart policy (only if managed)
  restart_on_failure: true
  max_restarts: 3
  restart_delay_seconds: 5
  restart_window_seconds: 300    # Reset counter after 5min stability
  
announcement:
  category: ai
  protocols:
    - http
  metadata:
    gpu: nvidia
    vram_gb: 48
```

### Capability Detection

For adopted services, Moss can detect capabilities dynamically:

```yaml
# Built-in parsers
capabilities:
  dynamic:
    endpoint: /api/tags
    parser: ollama_models
    
# Parser: ollama_models
# Input: {"models": [{"name": "llama3.2"}, {"name": "nomic-embed-text"}]}
# Output: ["ai:chat", "ai:completion", "ai:embeddings"]

# Custom parser (jq-like syntax)
capabilities:
  dynamic:
    endpoint: /v1/models
    parser: custom
    extract: ".data[].id"
    map:
      "gpt-*": "ai:chat"
      "text-embedding-*": "ai:embeddings"
      "whisper-*": "ai:audio.speech-to-text"
```

### Moss Responsibilities (Adopted)

| Action | Unmanaged | Managed |
|--------|-----------|---------|
| Install | ✗ Not Moss's job | ✗ Not Moss's job |
| Start | ✗ User does this | ✓ Run start command |
| Stop | ✗ User does this | ✓ Run stop command |
| Restart | ✗ User does this | ✓ Automatic (if configured) |
| Update | ✗ Notify only | ✗ Notify only |
| Health | ✓ Monitor and report | ✓ Monitor and report |
| Announce | ✓ Register with Lantern | ✓ Register with Lantern |

---

## Borrowed Offerings

### Overview

Borrowed offerings are services on external devices that Moss cannot install software on. Moss acts as a gateway, announcing their existence to the garden.

### Use Cases

1. **Network-Attached Storage** — Synology, QNAP, TrueNAS
2. **Printers** — Network printers with IPP
3. **IoT Hubs** — Home Assistant, Hubitat
4. **Appliances** — Routers, managed switches
5. **Third-party services** — Plex on a different machine

### Lifecycle

```
┌─────────────┐
│  EXTERNAL   │ ← Exists on network, unknown to garden
└──────┬──────┘
       │ garden-rake borrow
       ▼
┌─────────────┐
│  BORROWED   │ ← Moss announcing
└──────┬──────┘
       │
       ├─────────────────┐
       ▼                 ▼
┌─────────────┐   ┌─────────────┐
│  REACHABLE  │   │  FORGOTTEN  │
│  (online)   │   │  (removed)  │
└─────────────┘   └─────────────┘
       │
       ▼
┌─────────────┐
│ UNREACHABLE │
│  (offline)  │
└─────────────┘
```

### Commands

```bash
# Borrow an external service
garden-rake borrow storage from nas.local
garden-rake borrow storage from nas.local as synology-main

# Borrow with explicit capabilities
garden-rake borrow storage:smb,storage:nfs from nas.local

# Borrow with health check
garden-rake borrow printing from printer.local --health-ping

# Stop announcing
garden-rake forget nas.local
garden-rake forget synology-main
```

### Borrowing Flow

```bash
$ garden-rake borrow storage from nas.local

BORROW OFFERING
───────────────────────────────────────────────
  Device:        nas.local
  Name:          nas-local (auto-generated)
  
  Scanning services...
  ✓ SMB (port 445)    → storage:smb
  ✓ NFS (port 2049)   → storage:nfs
  ✓ HTTP (port 5000)  → management:web
  
  Health monitoring:
  • Ping check every 60s
  • No restart capability (external device)
  
  Moss will:
    • Announce to garden
    • Monitor reachability
    
  Moss will NOT:
    • Control the device
    • Restart on failure
    • Access credentials
    
Borrow? [Y/n]: y
✓ nas.local borrowed as "nas-local"
✓ Announced: storage:smb, storage:nfs
```

### Manifest Format

```yaml
# /etc/zen-garden/borrowed/synology-main.borrowed.yaml

offering:
  name: synology-main
  type: borrowed
  borrowed_at: 2026-01-20T15:45:00Z
  
device:
  hostname: nas.local
  addresses:
    - 192.168.1.50
  
services:
  - name: smb
    port: 445
    protocol: smb
    capability: storage:smb
    endpoint: smb://nas.local
    
  - name: nfs
    port: 2049
    protocol: nfs
    capability: storage:nfs
    endpoint: nfs://nas.local/volume1
    
  - name: webui
    port: 5000
    protocol: https
    capability: management:web
    endpoint: https://nas.local:5000
    
health:
  # Simple reachability check
  method: ping
  interval_seconds: 60
  timeout_seconds: 5
  
  # Or HTTP check if available
  # method: http
  # endpoint: https://nas.local:5000/webapi/entry.cgi?api=SYNO.API.Info&version=1&method=query
  # healthy_status: [200]
  
announcement:
  category: storage
  metadata:
    vendor: synology
    model: DS920+
```

### Moss Responsibilities (Borrowed)

| Action | Moss Does |
|--------|-----------|
| Install | ✗ Not possible |
| Start | ✗ Not possible |
| Stop | ✗ Not possible |
| Restart | ✗ Not possible |
| Update | ✗ Not possible |
| Health | ✓ Ping/HTTP check (reachability only) |
| Announce | ✓ Register with Lantern |

---

## Human Discovery Layer

### The Insight

Zen Garden isn't just for zen-garden-compatible apps. It's a **discovery layer for humans**.

Most users won't build capability-aware applications. But every user wants to know:
- What services are running on my network?
- What's the connection string for my database?
- Where's my NAS?

### Wishes (Human-Friendly Discovery)

```bash
$ garden-rake wishes

WISHES
───────────────────────────────────────────────
  Your garden has these offerings available:
  
  DATABASE
    mongodb              stone-01        planted
    postgresql           stone-01        planted
    mariadb              stone-02        adopted
    
  AI
    ollama               stone-gaming    adopted     ai:chat, ai:embeddings
    
  STORAGE  
    minio                stone-01        planted     storage:s3
    synology-main        nas.local       borrowed    storage:smb, storage:nfs
    
  CACHE
    redis                stone-01        planted
    
  Use 'garden-rake wish for <category>' for connection details.
```

### Wish for Category

```bash
$ garden-rake wish for database

DATABASE OFFERINGS
───────────────────────────────────────────────
  mongodb              stone-01            planted     [thriving]
    Category:          database:mongodb
    Connection:        mongodb://stone-01.local:27017
    
    Connect with:
      mongosh           mongosh "mongodb://stone-01.local:27017"
      MongoDB Compass   mongodb://stone-01.local:27017
      Python            pymongo.MongoClient("mongodb://stone-01.local:27017")
      Node.js           new MongoClient("mongodb://stone-01.local:27017")
      
  postgresql           stone-01            planted     [thriving]
    Category:          database:postgresql
    Connection:        postgresql://postgres:postgres@stone-01.local:5432/postgres
    
    Connect with:
      psql              psql "postgresql://postgres:postgres@stone-01.local:5432/postgres"
      pgAdmin           postgresql://stone-01.local:5432
      Python            psycopg2.connect("postgresql://postgres:postgres@stone-01.local:5432/postgres")
      
  mariadb              stone-02            adopted     [thriving]
    Category:          database:mysql
    Connection:        mysql://root@stone-02.local:3306
    
    Connect with:
      mysql             mysql -h stone-02.local -u root
      DBeaver           mysql://stone-02.local:3306

Copy connection string: garden-rake copy <offering>
```

### Wish for AI

```bash
$ garden-rake wish for ai

AI OFFERINGS
───────────────────────────────────────────────
  ollama               stone-gaming        adopted     [thriving]
    Capabilities:      ai:chat, ai:embeddings, ai:vision
    Endpoint:          http://stone-gaming.local:11434
    GPU:               NVIDIA RTX 4090 (48GB VRAM)
    Models loaded:     llama3.2, nomic-embed-text, llava
    
    API Examples:
    
    Chat completion:
      curl http://stone-gaming.local:11434/api/chat \
        -d '{"model": "llama3.2", "messages": [{"role": "user", "content": "Hello"}]}'
        
    Embeddings:
      curl http://stone-gaming.local:11434/api/embeddings \
        -d '{"model": "nomic-embed-text", "prompt": "Hello world"}'
        
    Python (ollama library):
      import ollama
      client = ollama.Client(host='http://stone-gaming.local:11434')
      response = client.chat(model='llama3.2', messages=[{'role': 'user', 'content': 'Hello'}])
      
    OpenAI-compatible:
      from openai import OpenAI
      client = OpenAI(base_url='http://stone-gaming.local:11434/v1', api_key='unused')
      response = client.chat.completions.create(model='llama3.2', messages=[...])
```

### Wish for Storage

```bash
$ garden-rake wish for storage

STORAGE OFFERINGS
───────────────────────────────────────────────
  minio                stone-01            planted     [thriving]
    Category:          storage:s3
    Endpoint:          http://stone-01.local:9000
    Console:           http://stone-01.local:9001
    
    Credentials:
      Access Key:      minioadmin
      Secret Key:      minioadmin
      
    Connect with:
      AWS CLI:         aws --endpoint-url http://stone-01.local:9000 s3 ls
      mc (minio cli):  mc alias set local http://stone-01.local:9000 minioadmin minioadmin
      Python (boto3):  boto3.client('s3', endpoint_url='http://stone-01.local:9000', ...)
      
  synology-main        nas.local           borrowed    [reachable]
    Category:          storage:smb, storage:nfs
    
    SMB:
      Windows:         \\nas.local\share
      macOS:           smb://nas.local/share
      Linux:           mount -t cifs //nas.local/share /mnt/nas
      
    NFS:
      Linux:           mount -t nfs nas.local:/volume1 /mnt/nas
      
    Web UI:            https://nas.local:5000
```

### Copy to Clipboard

```bash
$ garden-rake copy mongodb
✓ Copied to clipboard: mongodb://stone-01.local:27017

$ garden-rake copy ollama --format python
✓ Copied to clipboard:
import ollama
client = ollama.Client(host='http://stone-gaming.local:11434')

$ garden-rake copy minio --format env
✓ Copied to clipboard:
AWS_ENDPOINT_URL=http://stone-01.local:9000
AWS_ACCESS_KEY_ID=minioadmin
AWS_SECRET_ACCESS_KEY=minioadmin
```

### Two Audiences

**Audience 1: Developers building zen-garden-compatible apps**
- Use the SDK
- Register capabilities  
- Subscribe to changes
- Apps grow/shrink features dynamically

**Audience 2: Everyone else (much larger)**
- Just want to know what's on their network
- Want connection strings for existing tools
- Don't want to change their apps
- Zen Garden = fancy service catalog with live health

Both are valid. Both are valuable. The human discovery layer requires **zero app changes**.

---

## CLI Reference

### Planted (Docker)

```bash
# Install from catalog
garden-rake offer <offering>
garden-rake offer <offering> at <stone>

# Stop (preserves data)
garden-rake rest <offering>

# Start
garden-rake wake <offering>

# Update
garden-rake nourish <offering>

# Remove
garden-rake release <offering>
garden-rake release <offering> --with-data    # Also remove volumes
```

### Adopted (Native)

```bash
# Adopt running service
garden-rake adopt <offering> at <endpoint>
garden-rake adopt <offering> at <endpoint> --managed

# Adopt with auto-detection
garden-rake adopt <offering>

# Configure lifecycle (interactive)
garden-rake adopt <offering> --configure

# Stop monitoring
garden-rake disown <offering>

# If managed:
garden-rake rest <offering>     # Run stop command
garden-rake wake <offering>     # Run start command
```

### Borrowed (External)

```bash
# Borrow external device
garden-rake borrow <category> from <hostname>
garden-rake borrow <category> from <hostname> as <name>

# Borrow with explicit capabilities
garden-rake borrow <cap1>,<cap2> from <hostname>

# Borrow with health check
garden-rake borrow <category> from <hostname> --health-ping
garden-rake borrow <category> from <hostname> --health-http /api/health

# Stop announcing
garden-rake forget <name>
garden-rake forget <hostname>
```

### Human Discovery

```bash
# See all offerings
garden-rake wishes

# See specific category
garden-rake wish for database
garden-rake wish for ai
garden-rake wish for storage
garden-rake wish for cache

# Copy connection string
garden-rake copy <offering>
garden-rake copy <offering> --format <format>

# Available formats: url, env, python, node, curl, docker
```

### Normative Aliases

```bash
# Planted
garden-rake services create <offering>
garden-rake services stop <offering>
garden-rake services start <offering>
garden-rake services update <offering>
garden-rake services delete <offering>

# Adopted
garden-rake services adopt <offering> --endpoint <endpoint>
garden-rake services adopt <offering> --managed
garden-rake services unadopt <offering>

# Borrowed  
garden-rake devices add <hostname> --capabilities <caps>
garden-rake devices remove <hostname>

# Discovery
garden-rake services list
garden-rake services list --category database
garden-rake services connection <offering>
```

---

## API Specification

### Adoption Endpoints

**Adopt service:**
```http
POST /api/v1/adopted
Content-Type: application/json

{
  "offering": "ollama",
  "endpoint": "http://localhost:11434",
  "managed": true,
  "lifecycle": {
    "start": "net start ollama",
    "stop": "net stop ollama",
    "restart_on_failure": true,
    "max_restarts": 3
  }
}

Response 201:
{
  "offering": "ollama",
  "type": "adopted",
  "status": "thriving",
  "capabilities": ["ai:chat", "ai:embeddings", "ai:vision"],
  "managed": true
}
```

**List adopted:**
```http
GET /api/v1/adopted

Response 200:
{
  "offerings": [
    {
      "name": "ollama",
      "endpoint": "http://localhost:11434",
      "status": "thriving",
      "capabilities": ["ai:chat", "ai:embeddings"],
      "managed": true
    }
  ]
}
```

**Disown service:**
```http
DELETE /api/v1/adopted/ollama

Response 200:
{
  "offering": "ollama",
  "status": "disowned"
}
```

### Borrowed Endpoints

**Borrow device:**
```http
POST /api/v1/borrowed
Content-Type: application/json

{
  "name": "synology-main",
  "hostname": "nas.local",
  "services": [
    {"capability": "storage:smb", "port": 445},
    {"capability": "storage:nfs", "port": 2049}
  ],
  "health": {
    "method": "ping",
    "interval_seconds": 60
  }
}

Response 201:
{
  "name": "synology-main",
  "type": "borrowed",
  "status": "reachable",
  "capabilities": ["storage:smb", "storage:nfs"]
}
```

**List borrowed:**
```http
GET /api/v1/borrowed

Response 200:
{
  "offerings": [
    {
      "name": "synology-main",
      "hostname": "nas.local",
      "status": "reachable",
      "capabilities": ["storage:smb", "storage:nfs"]
    }
  ]
}
```

**Forget device:**
```http
DELETE /api/v1/borrowed/synology-main

Response 200:
{
  "name": "synology-main",
  "status": "forgotten"
}
```

### Unified Offerings Endpoint

**List all offerings (all modes):**
```http
GET /api/v1/offerings

Response 200:
{
  "offerings": [
    {
      "name": "mongodb",
      "type": "planted",
      "stone": "stone-01",
      "status": "thriving",
      "capabilities": ["database:mongodb"],
      "endpoint": "mongodb://stone-01.local:27017"
    },
    {
      "name": "ollama",
      "type": "adopted",
      "stone": "stone-gaming",
      "status": "thriving",
      "capabilities": ["ai:chat", "ai:embeddings"],
      "endpoint": "http://stone-gaming.local:11434"
    },
    {
      "name": "synology-main",
      "type": "borrowed",
      "hostname": "nas.local",
      "status": "reachable",
      "capabilities": ["storage:smb", "storage:nfs"],
      "endpoints": {
        "smb": "smb://nas.local",
        "nfs": "nfs://nas.local/volume1"
      }
    }
  ]
}
```

**Filter by category:**
```http
GET /api/v1/offerings?category=database

Response 200:
{
  "offerings": [
    {
      "name": "mongodb",
      "type": "planted",
      ...
    }
  ]
}
```

### Connection Info Endpoint

**Get connection details:**
```http
GET /api/v1/offerings/mongodb/connection

Response 200:
{
  "offering": "mongodb",
  "connection_string": "mongodb://stone-01.local:27017",
  "examples": {
    "cli": "mongosh \"mongodb://stone-01.local:27017\"",
    "python": "pymongo.MongoClient(\"mongodb://stone-01.local:27017\")",
    "node": "new MongoClient(\"mongodb://stone-01.local:27017\")",
    "env": "MONGODB_URI=mongodb://stone-01.local:27017"
  }
}
```

---

## Configuration

### Directory Structure

```
/etc/zen-garden/
├── moss.toml                    # Main config
├── offerings/                   # Planted offering manifests
│   ├── mongodb.frontmatter.yaml
│   └── redis.frontmatter.yaml
├── adopted/                     # Adopted offering configs
│   ├── ollama.adopted.yaml
│   └── postgresql.adopted.yaml
└── borrowed/                    # Borrowed device configs
    ├── synology-main.borrowed.yaml
    └── brother-printer.borrowed.yaml
```

### Main Configuration

```toml
# /etc/zen-garden/moss.toml

[offerings]
# Enable different modes
planted_enabled = true      # Requires Docker
adopted_enabled = true
borrowed_enabled = true

[docker]
# Docker configuration (ignored if planted_enabled = false)
socket = "/var/run/docker.sock"           # Linux/macOS
# socket = "npipe:////./pipe/docker_engine"  # Windows

# If Docker isn't available, Moss will:
# - Log warning at startup
# - Disable planted offerings automatically
# - Continue with adopted and borrowed

[adopted]
# Default health check interval for adopted services
health_interval_seconds = 30

# Auto-detect known services on startup
auto_detect = true
auto_detect_services = ["ollama", "postgresql", "mysql", "redis"]

[borrowed]
# Default health check for borrowed devices
health_interval_seconds = 60
health_method = "ping"    # or "http"

[discovery]
# Include all modes in service discovery
announce_planted = true
announce_adopted = true
announce_borrowed = true
```

### No-Docker Configuration

For machines without Docker:

```toml
# /etc/zen-garden/moss.toml

[offerings]
planted_enabled = false     # No Docker, no containers
adopted_enabled = true      # Native services only
borrowed_enabled = true     # External devices

[adopted]
health_interval_seconds = 30
auto_detect = true
auto_detect_services = ["ollama"]

[borrowed]
health_interval_seconds = 60
```

Moss starts cleanly without Docker—no errors, no warnings about missing daemon.

### Platform-Specific Lifecycle Commands

```yaml
# Common patterns for adopted services

# PostgreSQL
lifecycle:
  windows:
    start: "net start postgresql-x64-16"
    stop: "net stop postgresql-x64-16"
  linux:
    start: "systemctl start postgresql"
    stop: "systemctl stop postgresql"
  darwin:
    start: "brew services start postgresql@16"
    stop: "brew services stop postgresql@16"

# Ollama
lifecycle:
  windows:
    start: "net start ollama"
    stop: "net stop ollama"
  linux:
    start: "systemctl start ollama"
    stop: "systemctl stop ollama"
  darwin:
    start: "brew services start ollama"
    stop: "brew services stop ollama"

# Custom executable
lifecycle:
  windows:
    executable: "C:\\Program Files\\MyApp\\app.exe"
    arguments: ["--serve", "--port", "8080"]
    working_directory: "C:\\Program Files\\MyApp"
  linux:
    executable: "/opt/myapp/bin/myapp"
    arguments: ["--serve"]
    working_directory: "/opt/myapp"
```

---

## Security Considerations

### Adopted Services

- Run with user privileges, not isolated
- Moss trusts what it adopts
- Lifecycle commands execute with Moss's permissions
- Consider: allowlist of known-safe services for auto-detection

### Borrowed Devices

- Moss only announces endpoints, doesn't handle auth
- Credentials stored separately (not in borrowed manifest)
- Health checks may expose service existence to network

### Recommendations

1. **Adopted lifecycle commands** — Review before enabling `--managed`
2. **Borrowed devices** — Only borrow from trusted network devices
3. **Auto-detection** — Disable in untrusted environments
4. **Credential handling** — Use environment variables or secrets manager

---

## References

- [Offerings Specification](zen-garden-spec-offerings.md) — Offering manifest format
- [Federation Specification](zen-garden-spec-federation.md) — Bridges and Meadows
- [CLI Proposals](zen-garden-proposals-active.md) — Command vocabulary

---

## Glossary

| Term | Meaning |
|------|---------|
| **Planted** | Docker container, full Moss lifecycle control |
| **Adopted** | Native service, Moss monitors and optionally controls |
| **Borrowed** | External device, Moss announces only |
| **Shakkei** | 借景 — "borrowed scenery" in Japanese garden design |
| **Wish** | Human-friendly discovery of available services |

---

**Last Updated:** January 2026  
**Status:** Proposal — pending review and implementation

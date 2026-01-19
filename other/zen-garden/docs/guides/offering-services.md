# Managing Service Offerings

**Purpose:** Complete guide to service lifecycle management—plant, upgrade, take away, customize.  
**Audience:** Operators managing services on production Stones, developers creating custom offerings.

---

## Table of Contents

1. [Service Lifecycle Overview](#service-lifecycle-overview)
2. [Discovering Available Offerings](#discovering-available-offerings)
3. [Planting Services (Install)](#planting-services-install)
4. [Checking Service Status](#checking-service-status)
5. [Upgrading Services](#upgrading-services)
6. [Rest and Wake (Stop/Start)](#rest-and-wake-stopstart)
7. [Taking Away Services (Uninstall)](#taking-away-services-uninstall)
8. [Query-Based Recommendations](#query-based-recommendations)
9. [Creating Custom Offerings](#creating-custom-offerings)
10. [Agnostic Sidecars](#agnostic-sidecars)
11. [Troubleshooting](#troubleshooting)

---

## Service Lifecycle Overview

### Core Operations

Services in Zen Garden follow a simple lifecycle managed by Garden-Rake CLI:

1. **Offer (Plant)** - Install service from template
2. **Check Status** - Verify health and resource usage
3. **Upgrade** - Update to newer version
4. **Rest** - Stop service, preserve data
5. **Wake** - Resume stopped service
6. **Take Away** - Remove service completely

### Service vs Offering

- **Offering**: Service template (e.g., mongodb.yml defines how to deploy MongoDB)
- **Service**: Running instance of an offering (e.g., `mongodb` container on stone-01)

### Native vs Agnostic

Each offering can deploy:

- **Native service**: Database/service on its protocol (MongoDB:27017, Redis:6379)
- **Agnostic sidecar**: Optional HTTP REST API (port 8080+) for database-neutral access

Sidecars are **per-service**, not shared. Stone with MongoDB + PostgreSQL = 2 sidecars.

---

## Discovering Available Offerings

### List All Offerings

```bash
# Show all available offerings (curated templates)
garden-rake offer

# Output organized by category:
# DATA:
#   mongodb      - Document database (NoSQL, transactions, aggregation)
#   postgresql   - Relational database (SQL, ACID, extensions)
#   redis        - In-memory cache (key-value, pub/sub, streams)
#
# VECTOR:
#   weaviate     - Vector database (semantic search, ML)
#   qdrant       - Vector search engine (embeddings, similarity)
#
# MESSAGING:
#   rabbitmq     - Message broker (AMQP, queues, exchanges)
```

### Get Offering Details

```bash
# Show metadata, versions, ports, compatibility
garden-rake offer mongodb info

# Output:
# Offering: mongodb
# Category: data
# Tags: database, document, nosql
# Description: Document database with ACID transactions
# 
# Versions Available:
#   7.0 (latest, recommended)
#   6.0 (stable)
#   5.0 (legacy)
# 
# Ports:
#   Native: 27017 (MongoDB protocol)
#   Agnostic: 8080+ (HTTP REST API)
# 
# Compatibility:
#   Stone: stone-01 → PASS (x86_64, 4GB RAM)
#   Image: mongo:7
# 
# Volumes:
#   /data/db (persistent)
# 
# Connection String:
#   zen-garden:mongodb/myapp
```

### Filter by Target Stone

```bash
# Check what's installed on specific Stone
garden-rake list --at stone-01

# Output:
# Stone: stone-01 (192.168.1.42:7185)
# 
# Installed Services (3):
#   mongodb        Running    5d 2h     450 MB
#   redis          Running    3d 12h     80 MB
#   postgresql     Running    1d 6h     320 MB
# 
# Available Offerings (12):
#   weaviate, qdrant, rabbitmq, sqlserver, ...
```

### Cross-Stone Discovery

```bash
# List all services across all Stones
garden-rake list --all

# Output shows Stone-by-Stone inventory:
# stone-01 (192.168.1.42):
#   ├─ mongodb       Running
#   └─ redis         Running
# 
# stone-02 (192.168.1.43):
#   ├─ postgresql    Running
#   └─ weaviate      Running
```

---

## Planting Services (Install)

### Basic Installation

```bash
# Install MongoDB on local Stone
garden-rake offer mongodb

# Install on specific Stone
garden-rake offer mongodb --at stone-01

# Install with specific version
garden-rake offer mongodb --version 6.0
```

### Installation Process (What Happens)

1. **Rake discovers Stone** via localhost cache (instant) or network discovery (mDNS/UDP/Lantern)
2. **Rake sends HTTP POST** to `http://stone-01:7185/api/v1/offerings` with `{name: "mongodb"}`
3. **Moss validates offering** (template exists, port available, compatibility check)
4. **Moss reads template** from `/usr/share/garden-moss/templates/mongodb.yaml`
5. **Moss updates docker-compose.yml** atomically (backup, merge, validate, apply)
6. **Moss runs Docker Compose** (`docker compose up -d`)
7. **Moss announces service** via mDNS (updates TXT record with `offering=mongodb,port=27017`)
8. **Rake polls health** until service reports healthy (60s timeout)

### Installation Output

```
Discovering Garden-Moss Daemons... ✓ (found stone-01)
Installing mongodb on stone-01...
  [1/4] Validating template... ✓
  [2/4] Checking port availability... ✓ (27017, 8080)
  [3/4] Updating docker-compose.yml... ✓
  [4/4] Starting containers... ✓

✓ Successfully installed mongodb 7.0.4

Services:
  - mongodb (native): mongodb://stone-01.local:27017
  - mongodb-agnostic (HTTP): http://stone-01.local:8080

Connection string: zen-garden:mongodb

Next steps:
  1. Add to .env: MONGODB_URI=zen-garden:mongodb/myapp
  2. Check status: garden-rake observe
```

### Compatibility Failures

If installation fails due to hardware incompatibility:

```bash
# Attempt install
garden-rake offer weaviate --at stone-raspberry-pi

# Output:
✗ Compatibility check failed on stone-raspberry-pi

Issue: weaviate requires AVX2 (x86_64), but stone-raspberry-pi is ARM64

Recommendations (based on tags: vector,search,ml):
  1. qdrant --at stone-raspberry-pi  (ARM64-compatible)
  2. weaviate --at stone-intel-nuc   (x86_64 with AVX2)

Try: garden-rake offer qdrant --at stone-raspberry-pi
```

**Automatic fallback:**

```bash
# Auto-search other Stones on failure
garden-rake offer weaviate --at stone-raspberry-pi --anywhere-on-fail

# If stone-raspberry-pi incompatible, Rake automatically:
# 1. Searches all Stones via topology cache
# 2. Filters compatible Stones (x86_64 with AVX2)
# 3. Suggests best alternative Stone
```

---

## Checking Service Status

### Observe All Services

```bash
# Show status with resource usage
garden-rake observe

# Output:
# ●  stone-01 (Healthy, uptime: 4d 12h)
#    OFFERINGS:
#    ├─ mongodb       Run   1.2%        450 MB  ↓  1.2 MB  5d 2h
#    ├─ redis         Run   0.3%         80 MB  ↓  512 KB  3d 12h
#    └─ postgresql    Run   2.1%        320 MB  ↓  2.5 MB  1d 6h
# 
# Stone Resources:
#   CPU: 3.6% (8 cores)
#   Memory: 850 MB / 4 GB (21%)
#   Disk: 42 GB / 500 GB (8%)
```

### Describe Specific Service

```bash
# Detailed service information
garden-rake describe mongodb

# Output:
# Service: mongodb
# Offering: mongodb
# Template: /usr/share/garden-moss/templates/mongodb.yaml
# Image: mongo:7.0.4
# Status: Running
# Health: Passing
# Uptime: 5d 2h 15m
# 
# Ports:
#   Native: 27017 → stone-01.local:27017
#   Agnostic: 8080 → stone-01.local:8080
# 
# Resources:
#   CPU: 1.2% (8 cores available)
#   Memory: 450 MB / 4 GB (11%)
#   Network: ↓ 1.2 MB ↑ 800 KB
#   Disk: /data/db → 2.3 GB
# 
# Connection Strings:
#   Native: mongodb://stone-01.local:27017
#   Agnostic: http://stone-01.local:8080
#   Auto-discovery: zen-garden:mongodb
# 
# Diagnostics:
#   Restart count: 0
#   Last health check: 2026-01-19 14:23:45 UTC (3s ago)
```

### Watch Real-Time Logs

```bash
# Stream logs continuously (like tail -f)
garden-rake watch offering mongodb logs

# Output (streaming):
# 2026-01-19T14:23:45Z [MongoDB] Starting server...
# 2026-01-19T14:23:46Z [MongoDB] Listening on port 27017
# 2026-01-19T14:23:47Z [MongoDB] Connection accepted from 192.168.1.10
# ^C

# Dump last 100 lines and exit (no streaming)
garden-rake watch offering mongodb logs --tail 100

# With timestamps
garden-rake watch offering mongodb logs --timestamps
```

---

## Upgrading Services

### Upgrade to Latest Version

```bash
# Upgrade MongoDB to latest version
garden-rake upgrade mongodb

# Upgrade all services on local Stone
garden-rake upgrade

# Upgrade all services across all Stones
garden-rake upgrade --all
```

### Upgrade to Specific Version

```bash
# Upgrade to MongoDB 8.0
garden-rake upgrade mongodb --version 8.0 --at stone-01
```

### Upgrade Process (What Happens)

1. **Rake sends upgrade request** to Moss with optional version
2. **Moss reads template** and determines latest/specified version
3. **Moss updates docker-compose.yml** with new image tag
4. **Moss runs Docker Compose** (`docker compose pull` then `docker compose up -d`)
5. **Docker performs rolling update** (pull new image, stop old container, start new container)
6. **Moss monitors health** and verifies service restarts successfully
7. **Moss updates mDNS** announcement with new version

**Rollback on failure:**

If upgrade fails (container crashes, health check fails):

1. **Moss detects failure** (container exited, restart loop)
2. **Moss restores backup** docker-compose.yml (previous version)
3. **Moss runs Docker Compose** (`docker compose up -d` with old config)
4. **Moss verifies rollback** (service healthy with previous version)
5. **Rake displays error** with diagnostics and rollback confirmation

### Dry-Run Mode

```bash
# Preview what would change (no actual upgrade)
garden-rake upgrade --dry-run --all

# Output:
# Dry-run: No changes will be made
# 
# stone-01:
#   mongodb: 7.0.4 → 7.0.5
#   redis: 7.2.3 → 7.2.4
# 
# stone-02:
#   postgresql: 15.2 → 15.3
# 
# Total: 3 services would upgrade
```

### Garden-Wide Upgrades

```bash
# Upgrade all services across all Stones (parallel execution)
garden-rake upgrade --all

# Output:
# Upgrading services across 3 Stones...
# 
# stone-01 (2 services):
#   ✓ mongodb: 7.0.4 → 7.0.5
#   ✓ redis: 7.2.3 → 7.2.4
# 
# stone-02 (1 service):
#   ✓ postgresql: 15.2 → 15.3
# 
# stone-03 (1 service):
#   ✗ weaviate: Upgrade failed (compatibility)
# 
# Summary: 3 succeeded, 1 failed
```

**Operation ID tracking:**

Garden-wide operations use GUIDv7 operation IDs for correlation:

```bash
# Rake generates operation_id: 01936d2e-8f4a-7890-b123-456789abcdef
# Each Moss logs with this ID:
# [stone-01] Started upgrade, operation_id: 01936d2e-...
# [stone-02] Started upgrade, operation_id: 01936d2e-...
# [stone-03] Started upgrade, operation_id: 01936d2e-...
# 
# Enables correlation in centralized logging (Loki, Elasticsearch)
```

---

## Rest and Wake (Stop/Start)

### Rest (Stop Service, Preserve Data)

```bash
# Stop MongoDB without removing data
garden-rake rest mongodb --at stone-01

# Output:
# Stopping mongodb on stone-01...
# ✓ Container stopped
# ✓ Data preserved at /var/lib/docker/volumes/mongodb_data
# ✓ mDNS announcement removed
# 
# To resume: garden-rake wake mongodb --at stone-01
```

**Use cases:**

- Maintenance window (reduce resource usage)
- Testing connectivity without service
- Rotating certificates without data loss
- Investigating disk space issues

### Wake (Start Stopped Service)

```bash
# Resume MongoDB from rest state
garden-rake wake mongodb --at stone-01

# Output:
# Waking mongodb on stone-01...
# ✓ Container started
# ✓ Health check passing
# ✓ mDNS announcement restored
# 
# Connection: zen-garden:mongodb
```

**Behavior:**

- Starts existing container (preserves all data)
- Does NOT re-pull image (uses existing)
- Does NOT reset configuration
- Restores mDNS announcement

---

## Taking Away Services (Uninstall)

### Basic Removal

```bash
# Remove MongoDB (preserves volumes by default)
garden-rake take-away mongodb --at stone-01

# Output:
# Removing mongodb from stone-01...
# ✓ Container stopped and removed
# ⚠ Volumes preserved at /var/lib/docker/volumes/mongodb_data
# ✓ mDNS announcement removed
# ✓ docker-compose.yml updated
# 
# To remove volumes: garden-rake take-away mongodb --volumes
```

### Remove Including Data

```bash
# Remove MongoDB and delete all data (DESTRUCTIVE)
garden-rake take-away mongodb --at stone-01 --volumes

# Output:
# ⚠ WARNING: This will delete all data for mongodb
# Type 'DELETE' to confirm: DELETE
# 
# Removing mongodb from stone-01...
# ✓ Container stopped and removed
# ✓ Volumes removed: mongodb_data (2.3 GB freed)
# ✓ mDNS announcement removed
# ✓ docker-compose.yml updated
```

### Remove Pond Security

Special command to remove Pond security layer from all Stones:

```bash
# Remove Pond mTLS security (reverts to open network)
garden-rake take-away keystone

# Output:
# ⚠ WARNING: This will remove Pond security from all Stones
# All certificates will be revoked
# HTTP API will become unauthenticated
# 
# Type 'REMOVE POND' to confirm: REMOVE POND
# 
# Removing Pond from garden...
# ✓ stone-01: Certificates revoked, Keystone removed
# ✓ stone-02: Certificates revoked, Keystone removed
# ✓ stone-03: Certificates revoked, Keystone removed
# 
# Pond security removed. Garden now operates without authentication.
```

---

## Query-Based Recommendations

### Discovering Services by Intent

When you know *what* you need but not *which* offering:

```bash
# Find document databases
garden-rake offer database,document

# Output:
# Top 3 recommendations for "database,document":
# 
# 1. mongodb (PASS on stone-01)
#    Category: data | Tags: database, document, nosql
#    Description: Document database with ACID transactions
#    Install: garden-rake offer mongodb --at stone-01
# 
# 2. couchdb (PASS on stone-01)
#    Category: data | Tags: database, document, nosql
#    Description: Distributed document store with REST API
#    Install: garden-rake offer couchdb --at stone-01
# 
# 3. arangodb (FALLBACK on stone-01)
#    Category: data | Tags: database, document, graph
#    Description: Multi-model database (documents, graphs, search)
#    Install: garden-rake offer arangodb --at stone-01
```

### Query Syntax

- **Comma-separated**: `database,document` (AND logic - both tags must match)
- **Whitespace**: `vector search` (OR logic - either tag matches)
- **Synonym expansion**: `db` → `database`, `doc` → `document`, `mq` → `messaging`

**Synonym dictionary** (`manifests/taxonomy.dictionary.yaml`):

```yaml
db: database
doc: document
docs: document
nosql: nosql
sql: sql
mq: messaging
queue: messaging
fts: search
vector: vector
ml: inference
```

### Preference Scoring

Bias recommendations without hard filtering:

```bash
# Prefer SSD/NVMe storage for databases
garden-rake offer database --prefer ssd

# Output (ranking adjusted):
# 1. postgresql (PASS on stone-intel-nuc)  ← SSD detected, boosted
#    Disk: SSD (NVMe 500GB)
# 
# 2. mongodb (PASS on stone-raspberry-pi)
#    Disk: SD Card (128GB)  ← Functional but slower
```

**Prefer tokens:**

- `ssd`, `nvme` - Boost score for Stones with fast storage
- `hdd` - Boost score for Stones with spinning disks (archival workloads)
- Future: `gpu`, `high-memory`, `low-power`

**Philosophy:** `--prefer` is advisory, not mandatory. All compatible Stones remain viable.

### Cross-Stone Recommendations

Query across all Stones in garden:

```bash
# Find best Stone for vector database
garden-rake offer vector --at anywhere --prefer ssd,high-memory

# Output:
# Top 3 (stone, offering) recommendations for "vector":
# 
# 1. weaviate on stone-intel-nuc (PASS)
#    Stone: x86_64, 16GB RAM, NVMe SSD
#    Offering: Vector database (semantic search, ML)
#    Install: garden-rake offer weaviate --at stone-intel-nuc
# 
# 2. qdrant on stone-raspberry-pi (PASS)
#    Stone: ARM64, 8GB RAM, SD Card
#    Offering: Vector search engine (embeddings, similarity)
#    Install: garden-rake offer qdrant --at stone-raspberry-pi
# 
# 3. milvus on stone-intel-nuc (FALLBACK)
#    Stone: x86_64, 16GB RAM, NVMe SSD
#    Offering: Cloud-native vector database
#    Install: garden-rake offer milvus --at stone-intel-nuc
```

**Ranking logic:**

1. **Category match** > **Tag match** (category is authoritative)
2. **Compatibility status**: `PASS` > `FALLBACK` > `FAIL` (fails excluded)
3. **Prefer scoring**: SSD boost, high-memory boost, etc.
4. **Stone health**: Healthy > Degraded
5. **Resource availability**: More free RAM/disk = higher rank

---

## Creating Custom Offerings

### Offering Template Structure

Create custom offerings in `/etc/zen-garden/templates/custom/`:

```yaml
# /etc/zen-garden/templates/custom/myapp.yaml
---
name: myapp
offering: myapp
category: application
tags: [web, api, nodejs]
description: Custom Node.js API service

versions:
  default: latest
  supported:
    - latest
    - 1.0.0

docker:
  native:
    offering_name: myapp
    image: mycompany/myapp
    image_tag: ${VERSION}
    ports:
      - container: 3000
        host: 3000
        protocol: tcp
    volumes:
      - name: myapp_data
        mount: /app/data
        type: volume
    environment:
      NODE_ENV: production
      DATABASE_URL: zen-garden:mongodb/myapp
    health_check:
      test: ["CMD", "curl", "-f", "http://localhost:3000/health"]
      interval: 30s
      timeout: 10s
      retries: 3

mdns:
  - offering: myapp
    protocol: native
    port_source: 3000
```

### Template Validation

```bash
# Refresh offering index to include custom templates
garden-rake offer refresh --at stone-01

# Output:
# Refreshing offerings index on stone-01...
# Scanning templates: /usr/share/garden-moss/templates/
# ✓ Found 15 curated offerings
# Scanning custom: /etc/zen-garden/templates/custom/
# ✓ Found 1 custom offering (myapp)
# ⚠ Skipped 1 invalid template (validation failed)
# 
# Available offerings: 16 total
```

**Validation rules:**

- Template must have `name`, `offering`, `docker` sections
- Image tags must be valid (no shell injection)
- Port numbers: 1-65535
- Environment variables: `${VAR}` or `${VAR:-default}` only
- Volume names: `^[a-z0-9-]+$`
- No arbitrary shell commands

### Advanced: Agnostic Sidecar

Add HTTP REST API to custom service:

```yaml
docker:
  native:
    # ... native config ...

  agnostic:
    offering_name: myapp-agnostic
    image: mycompany/myapp-agnostic
    image_tag: ${VERSION}
    ports:
      - container: 8080
        host_start: 8080
        protocol: tcp
    environment:
      BACKEND_SERVICE: myapp
      BACKEND_URL: http://myapp:3000
    depends_on:
      - myapp

mdns:
  - offering: myapp
    protocol: native
    port_source: 3000
  - offering: myapp-agnostic
    protocol: agnostic
    port_source: 8080
    capabilities: [crud, query, filter]
```

---

## Agnostic Sidecars

### What Are Agnostic Sidecars?

HTTP REST API wrappers for database-specific protocols:

- **Purpose**: Database-neutral client access (no vendor-specific drivers)
- **Pattern**: Based on Koan EntityController (`/v1/data/{set}/entities/{type}`)
- **Protocol**: HTTP REST with JSON payloads
- **Deployment**: Per-service sidecar (not shared across services)

### When to Use Agnostic Sidecars

**Use agnostic sidecars when:**

- Backend portability required (swap MongoDB ↔ PostgreSQL without client changes)
- Client cannot install vendor drivers (mobile, serverless)
- Polyglot environment (multiple languages accessing same data)
- HTTP firewalls block native protocols

**Use native protocol when:**

- Performance critical (no HTTP overhead)
- Full feature set required (transactions, aggregations, stored procedures)
- Vendor-specific extensions needed (MongoDB change streams, PostgreSQL LISTEN/NOTIFY)

### Sidecar Architecture

```
┌─────────────────────────────────────────┐
│ Stone                                   │
│                                         │
│  ┌──────────────┐    ┌──────────────┐  │
│  │ MongoDB      │◄───│ Sidecar      │  │
│  │ :27017       │    │ :8080        │  │
│  │ (native)     │    │ (agnostic)   │  │
│  └──────────────┘    └──────────────┘  │
│                                         │
│  ┌──────────────┐    ┌──────────────┐  │
│  │ PostgreSQL   │◄───│ Sidecar      │  │
│  │ :5432        │    │ :8081        │  │
│  │ (native)     │    │ (agnostic)   │  │
│  └──────────────┘    └──────────────┘  │
└─────────────────────────────────────────┘
```

**Key properties:**

- Each service gets dedicated sidecar
- Sidecars auto-discover backend via Docker Compose networks
- Port allocation automatic (8080, 8081, 8082, ...)
- Sidecars announce via mDNS with `protocol=agnostic`

### Connection String Resolution

```bash
# Native protocol (vendor-specific)
zen-garden:mongodb → mongodb://stone-01.local:27017

# Agnostic protocol (HTTP REST)
zen-garden:database → http://stone-01.local:8080
```

**Category-based resolution:**

- `zen-garden:database` → any database sidecar (MongoDB, PostgreSQL, SQL Server)
- `zen-garden:document-database` → document store sidecar (MongoDB, CouchDB)
- `zen-garden:vector` → vector database sidecar (Weaviate, Qdrant)

---

## Troubleshooting

### Service Won't Start

**Symptom:** `garden-rake observe` shows "Exited" or "Restarting"

**Diagnosis:**

```bash
# Check logs
garden-rake watch offering mongodb logs --tail 100

# Common issues:
# - Port conflict: "bind: address already in use"
# - Volume permissions: "Permission denied /data/db"
# - Image pull failure: "manifest not found"
```

**Solutions:**

1. **Port conflict**: Change port in template or remove conflicting service
2. **Volume permissions**: `docker exec <container> chown -R mongodb:mongodb /data/db`
3. **Image pull failure**: Check network, verify image exists, try different version

### Offering Not Found

**Symptom:** `✗ Error: Offering 'mysql' not found`

**Diagnosis:**

```bash
# List available offerings
garden-rake offer

# Refresh offering index
garden-rake offer refresh --at stone-01
```

**Solutions:**

- Typo in offering name (correct: `mongodb` not `mongo`)
- Custom offering not in `/etc/zen-garden/templates/custom/`
- Template validation failed (check Moss logs)

### Upgrade Failed

**Symptom:** Service unhealthy after upgrade

**Diagnosis:**

```bash
# Check service status
garden-rake describe mongodb

# Output:
# Status: Restarting
# Health: Failing
# Restart count: 5
# Last error: Container exited with code 1
```

**Automatic rollback:**

Moss detects failure and automatically restores previous version:

```
✗ Upgrade failed: Container unhealthy after 60s
✓ Automatic rollback successful
✓ Service restored to version 7.0.4
```

**Manual rollback:**

```bash
# Downgrade to specific version
garden-rake upgrade mongodb --version 7.0.4 --at stone-01
```

### Connection String Not Resolving

**Symptom:** Application cannot resolve `zen-garden:mongodb`

**Diagnosis:**

```bash
# Test mDNS resolution
avahi-resolve -n stone-01.local  # Linux
dns-sd -G v4 stone-01.local      # macOS

# Check service announcement
garden-rake observe --at stone-01
```

**Solutions:**

1. **mDNS not enabled**: Install Avahi (Linux) or Bonjour (Windows)
2. **Service not announced**: Verify service running and healthy
3. **Wrong connection string**: Use `zen-garden:mongodb` not `zen-garden://mongodb`

---

## Next Steps

- **Advanced customization**: [Creating Service Sets](../reference/offerings.md)
- **Security**: [Enable Pond Security](../security/pond-setup.md)
- **Monitoring**: [Operations Guide](../ops/maintainers.md)
- **Troubleshooting**: [Common Issues](troubleshooting.md)

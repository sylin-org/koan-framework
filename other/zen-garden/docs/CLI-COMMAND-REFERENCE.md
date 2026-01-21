# Zen Garden CLI Command Reference

**Version**: 1.0 (Draft based on Dual-Ergonomics Design)
**Date**: 2026-01-21
**Status**: Reference Implementation Guide

---

## Introduction

This document provides a comprehensive lexicographical reference for all Zen Garden CLI commands, organized by scope and showing graduated examples from simple to complex usage.

### Dual Syntax Philosophy

Every command has two equivalent forms:

- **Zen**: Expressive, metaphorical, optimized for human interaction
- **Normative**: Precise, standard, optimized for scripting

**Example**:
```bash
# Zen
garden-rake offer mongodb on stone-02

# Normative
garden-rake services create --name mongodb --on stone-02
```

### Scope Types

Commands are organized by their operational scope:

1. **Stone-Scoped**: Operate on a single stone (respects tending context)
2. **Garden-Wide**: Operate across multiple stones in the garden
3. **Local-Only**: Local operations (no network calls)

### Reserved Keywords

The following names are **reserved** and cannot be used as stone names or service names:

| Keyword | Reason |
|---------|--------|
| `keystone` | Special pond concept - the founding stone that initializes the pond. Used in `place keystone` and `lift keystone`. |
| `stone` | CLI keyword for stone operations (e.g., `lift stone <name>`, `place stone --code`). |
| `strays` | CLI keyword for `find strays` command. |
| `on` | Positional keyword for stone targeting (e.g., `offer mongodb on stone-02`). |
| `from` | Positional keyword for borrowing (e.g., `borrow redis from <url>`). |
| `info` | Subcommand for `offer <name> info`. |

**Note**: If you must reference a stone or service with a reserved name, use quoting: `garden-rake lift stone "keystone-legacy"`. However, it's strongly recommended to avoid reserved names entirely.

---

## Table of Contents

### Stone-Scoped Commands
- [offer](#offer) - List or install offerings
- [rest](#rest) - Stop a service
- [wake](#wake) - Start a service
- [remove](#remove) - Soft delete a service
- [uproot](#uproot) - Hard delete a service (destroy container)
- [nourish / upgrade](#upgrade--nourish) - Update a service
- [list](#list) - List services on a stone
- [adopt](#adopt) - Adopt an existing container
- [release](#release) - Release an adopted service
- [borrow](#borrow) - Register an external service
- [return](#return) - Unregister a borrowed service
- [find strays](#find-strays) - List adoptable containers
- [adopted](#adopted) - List adopted services
- [borrowed](#borrowed) - List borrowed services
- [watch](#watch) - Stream events or logs
- [status](#status) - Show stone status
- [place](#place) - Initialize or join pond
- [invite](#invite) - Generate pond invitation
- [lift](#lift) - Destroy pond or remove stone from pond
- [make](#make) - Control console output
- [refresh](#refresh) - Upgrade stone binaries
- [take-root](#take-root) - Install as system service
- [reconcile](#reconcile) - Reconcile inventory
- [template](#template) - Manage offering templates
- [ceremony](#ceremony) - Run guided workflows

### Garden-Wide Commands
- [observe](#observe) - Observe garden state
- [explore](#explore) - Discover stones (proposed)

### Local-Only Commands
- [tend](#tend) - Manage tending context

---

## Stone-Scoped Commands

### offer

**Purpose**: List available offerings, install an offering, or show offering details.

**Scope**: Stone-scoped (respects tending context)

**Syntax**:
```bash
# Zen
garden-rake offer [offering] [info] [on <stone>]

# Normative
garden-rake offerings list [--on <stone>]                    # List
garden-rake services create --name <offering> [--on <stone>] # Install
garden-rake offerings show --name <offering> [--on <stone>]  # Show details
```

**Examples** (ordered by complexity):

**1. List all available offerings** (Simple)
```bash
# Zen
garden-rake offer

# Normative
garden-rake offerings list

# Output:
# Available offerings:
#   📦 mongodb (Database) - Compatible
#   📦 redis (Cache) - Compatible
#   🔍 elasticsearch (Search) - Requires 4GB+ RAM
```

**2. Install an offering** (Simple)
```bash
# Zen
garden-rake offer mongodb

# Normative
garden-rake services create --name mongodb

# Output:
# Installing mongodb...
# ✓ Created service zen-offering-mongodb
# ✓ Service is running
```

**3. Show offering details before installing** (Medium)
```bash
# Zen
garden-rake offer mongodb info

# Normative
garden-rake offerings show --name mongodb

# Output:
# Offering: mongodb
# Category: Database
# Image: mongo:7.0
# Compatibility: ✓ Compatible
# Requirements:
#   - 2GB RAM minimum
#   - 10GB disk space
```

**4. Install offering on specific stone** (Medium)
```bash
# Zen (positional syntax)
garden-rake offer mongodb on stone-02

# Zen (shorthand)
garden-rake offer mongodb @stone-02

# Normative
garden-rake services create --name mongodb --on stone-02
```

**5. List offerings on specific stone** (Medium)
```bash
# Zen
garden-rake offer on stone-02

# Normative
garden-rake offerings list --on stone-02
```

**6. Show offering details on remote stone** (Complex)
```bash
# Zen
garden-rake offer mongodb info on stone-02

# Normative
garden-rake offerings show --name mongodb --on stone-02
```

**Related Commands**: [list](#list), [remove](#remove), [status](#status)

---

### rest

**Purpose**: Stop a running service (put into rest/dormant state).

**Scope**: Stone-scoped (respects tending context)

**Metaphor**: In a garden, services rest (dormancy) rather than being stopped mechanically. The intent is temporary pause, not permanent termination.

**Syntax**:
```bash
# Zen
garden-rake rest <service> [on <stone>]

# Normative
garden-rake services stop --name <service> [--on <stone>]
```

**Examples** (ordered by complexity):

**1. Stop a service on tended stone** (Simple)
```bash
# Zen
garden-rake rest mongodb

# Normative
garden-rake services stop --name mongodb

# Output:
# ✓ Service mongodb resting
```

**2. Stop service on specific stone** (Medium)
```bash
# Zen
garden-rake rest mongodb on stone-02

# Normative
garden-rake services stop --name mongodb --on stone-02
```

**3. Stop service using shorthand** (Medium)
```bash
# Zen
garden-rake rest mongodb @stone-02

# Normative (same)
garden-rake services stop --name mongodb --on stone-02
```

**4. Stop multiple services sequentially** (Complex)
```bash
# Zen
garden-rake rest mongodb
garden-rake rest redis
garden-rake rest elasticsearch

# Normative (scriptable)
for service in mongodb redis elasticsearch; do
  garden-rake services stop --name "$service"
done
```

**5. Stop service with explicit endpoint** (Complex)
```bash
# Zen
garden-rake rest mongodb --at http://192.168.1.108:7185

# Normative
garden-rake services stop --name mongodb --on http://192.168.1.108:7185
```

**Related Commands**: [wake](#wake), [remove](#remove), [uproot](#uproot)

---

### wake

**Purpose**: Start a stopped service (wake from rest/dormant state).

**Scope**: Stone-scoped (respects tending context)

**Metaphor**: Services wake from rest, like plants blooming after dormancy.

**Syntax**:
```bash
# Zen
garden-rake wake <service> [on <stone>]

# Normative
garden-rake services start --name <service> [--on <stone>]
```

**Examples** (ordered by complexity):

**1. Start a service on tended stone** (Simple)
```bash
# Zen
garden-rake wake mongodb

# Normative
garden-rake services start --name mongodb

# Output:
# ✓ Service mongodb awakened
```

**2. Start service on specific stone** (Medium)
```bash
# Zen
garden-rake wake mongodb on stone-02

# Normative
garden-rake services start --name mongodb --on stone-02
```

**3. Start service using shorthand** (Medium)
```bash
# Zen
garden-rake wake mongodb @stone-02

# Normative (same)
garden-rake services start --name mongodb --on stone-02
```

**4. Restart workflow (rest then wake)** (Complex)
```bash
# Zen
garden-rake rest mongodb && garden-rake wake mongodb

# Normative (or use restart API directly)
garden-rake services stop --name mongodb && \
garden-rake services start --name mongodb
```

**5. Wake with health check** (Complex)
```bash
# Zen (wait for healthy state)
garden-rake wake mongodb
garden-rake watch offering mongodb logs --until "ready"

# Normative
garden-rake services start --name mongodb
garden-rake services logs --name mongodb --follow | grep -m 1 "ready"
```

**Related Commands**: [rest](#rest), [status](#status), [watch](#watch)

---

### remove

**Purpose**: Soft delete a service (remove from registry but preserve container).

**Scope**: Stone-scoped (respects tending context)

**Semantics**: Like pulling a plant from the garden—the service is removed from Zen Garden's management, but the container still exists on the stone and can be re-adopted.

**Syntax**:
```bash
# Zen
garden-rake remove <service> [on <stone>] [--force]

# Normative
garden-rake services delete --name <service> [--on <stone>]
```

**Examples** (ordered by complexity):

**1. Remove a service (with confirmation)** (Simple)
```bash
# Zen
garden-rake remove mongodb

# Output:
# Remove service 'mongodb'? [y/N]: y
# ✓ Service mongodb removed from garden

# Normative
garden-rake services delete --name mongodb
```

**2. Remove service without confirmation** (Medium)
```bash
# Zen
garden-rake remove mongodb --force

# Normative (--force implied in scripting)
garden-rake services delete --name mongodb
```

**3. Remove service on specific stone** (Medium)
```bash
# Zen
garden-rake remove mongodb on stone-02 --force

# Normative
garden-rake services delete --name mongodb --on stone-02
```

**4. Remove and verify** (Complex)
```bash
# Zen
garden-rake remove mongodb --force
garden-rake list | grep mongodb || echo "Successfully removed"

# Normative
garden-rake services delete --name mongodb
garden-rake services list --format json | jq -e '.[] | select(.name=="mongodb")' > /dev/null || echo "Successfully removed"
```

**5. Remove all services matching pattern** (Complex)
```bash
# Normative (bash scripting)
garden-rake services list --format json | \
  jq -r '.[] | select(.name | startswith("test-")) | .name' | \
  while read -r service; do
    garden-rake services delete --name "$service"
  done
```

**Related Commands**: [uproot](#uproot), [rest](#rest), [adopt](#adopt)

---

### uproot

**Purpose**: Hard delete a service (destroy container completely).

**Scope**: Stone-scoped (respects tending context)

**Semantics**: Like uprooting a plant completely—removes from registry AND destroys the container. Cannot be re-adopted.

**Syntax**:
```bash
# Zen
garden-rake uproot <service> [on <stone>] [--force]

# Normative
garden-rake services destroy --name <service> [--on <stone>]
```

**Examples** (ordered by complexity):

**1. Completely destroy a service** (Simple)
```bash
# Zen
garden-rake uproot mongodb

# Output:
# ⚠️  This will DESTROY the mongodb container completely.
# Cannot be recovered or re-adopted.
# Destroy service 'mongodb'? [y/N]: y
# ✓ Service mongodb uprooted (container destroyed)

# Normative
garden-rake services destroy --name mongodb
```

**2. Force destroy without confirmation** (Medium)
```bash
# Zen
garden-rake uproot mongodb --force

# Normative
garden-rake services destroy --name mongodb --force
```

**3. Destroy service on specific stone** (Medium)
```bash
# Zen
garden-rake uproot mongodb on stone-02 --force

# Normative
garden-rake services destroy --name mongodb --on stone-02
```

**4. Cleanup failed installation** (Complex)
```bash
# Zen (destroy failed service and retry)
garden-rake uproot mongodb --force || true
garden-rake offer mongodb

# Normative
garden-rake services destroy --name mongodb --force || true
garden-rake services create --name mongodb
```

**5. Emergency cleanup of all test services** (Complex)
```bash
# Normative (dangerous - use with caution)
garden-rake services list --format json | \
  jq -r '.[] | select(.name | startswith("test-")) | .name' | \
  while read -r service; do
    garden-rake services destroy --name "$service" --force
  done
```

**Related Commands**: [remove](#remove), [rest](#rest), [find strays](#find-strays)

---

### upgrade / nourish

**Purpose**: Update a service to a newer version or configuration.

**Scope**: Stone-scoped (respects tending context)

**Metaphor**: Nourish the service—provide updates and patches to help it grow.

**Syntax**:
```bash
# Zen
garden-rake nourish [service] [on <stone>] [--all]

# Normative
garden-rake services upgrade --name <service> [--on <stone>]
garden-rake services upgrade --all [--on <stone>]  # Update all
```

**Examples** (ordered by complexity):

**1. Update a single service** (Simple)
```bash
# Zen
garden-rake nourish mongodb

# Normative
garden-rake services upgrade --name mongodb

# Output:
# Nourishing mongodb...
# ✓ Updated to latest version
```

**2. Update service on specific stone** (Medium)
```bash
# Zen
garden-rake nourish mongodb on stone-02

# Normative
garden-rake services upgrade --name mongodb --on stone-02
```

**3. Update all services on tended stone** (Medium)
```bash
# Zen
garden-rake nourish --all

# Normative
garden-rake services upgrade --all
```

**4. Update all services on specific stone** (Complex)
```bash
# Zen
garden-rake nourish --all on stone-02

# Normative
garden-rake services upgrade --all --on stone-02
```

**5. Selective update with health checks** (Complex)
```bash
# Normative (update one at a time with health verification)
for service in mongodb redis elasticsearch; do
  echo "Nourishing $service..."
  garden-rake services upgrade --name "$service"
  sleep 10
  health=$(garden-rake services show --name "$service" --format json | jq -r '.health')
  if [[ "$health" != "healthy" ]]; then
    echo "ERROR: $service unhealthy after update"
    exit 1
  fi
done
```

**Related Commands**: [offer](#offer), [rest](#rest), [wake](#wake)

---

### list

**Purpose**: List all services running on a stone.

**Scope**: Stone-scoped (respects tending context)

**Syntax**:
```bash
# Zen
garden-rake list [on <stone>]

# Normative
garden-rake services list [--on <stone>] [--format json|table]
```

**Examples** (ordered by complexity):

**1. List services on tended stone** (Simple)
```bash
# Zen
garden-rake list

# Normative
garden-rake services list

# Output:
# Services on stone-01:
#   mongodb    (running, 2h ago)
#   redis      (running, 5m ago)
#   grafana    (stopped, 1d ago)
```

**2. List services on specific stone** (Medium)
```bash
# Zen
garden-rake list on stone-02

# Normative
garden-rake services list --on stone-02
```

**3. List with JSON output for scripting** (Medium)
```bash
# Normative
garden-rake services list --format json

# Output:
# [
#   {"name": "mongodb", "status": "running", "uptime": "2h"},
#   {"name": "redis", "status": "running", "uptime": "5m"}
# ]
```

**4. Filter running services** (Complex)
```bash
# Normative
garden-rake services list --format json | jq -r '.[] | select(.status=="running") | .name'

# Output:
# mongodb
# redis
```

**5. Cross-stone service count** (Complex)
```bash
# Normative (count services across multiple stones)
for stone in stone-01 stone-02 stone-03; do
  count=$(garden-rake services list --on "$stone" --format json | jq 'length')
  echo "$stone: $count services"
done
```

**Related Commands**: [offer](#offer), [status](#status), [observe](#observe)

---

### adopt

**Purpose**: Adopt an existing container into Zen Garden management.

**Scope**: Stone-scoped (respects tending context)

**Metaphor**: Like adopting a stray plant that's already growing—bring it under garden management without recreating it.

**Syntax**:
```bash
# Zen
garden-rake adopt <container-name> [on <stone>]

# Normative
garden-rake adoption adopt --name <container-name> [--on <stone>]
```

**Examples** (ordered by complexity):

**1. Adopt a container on tended stone** (Simple)
```bash
# Zen
garden-rake adopt my-mongodb-container

# Normative
garden-rake adoption adopt --name my-mongodb-container

# Output:
# ✓ Adopted container 'my-mongodb-container' as 'mongodb'
```

**2. Adopt container on specific stone** (Medium)
```bash
# Zen
garden-rake adopt my-mongodb-container on stone-02

# Normative
garden-rake adoption adopt --name my-mongodb-container --on stone-02
```

**3. Find and adopt strays** (Medium)
```bash
# Zen workflow
garden-rake find strays
# Shows: my-mongodb-container, legacy-redis, test-postgres
garden-rake adopt my-mongodb-container

# Normative workflow
garden-rake adoption list-adoptable
garden-rake adoption adopt --name my-mongodb-container
```

**4. Adopt with offering type inference** (Complex)
```bash
# Zen (Zen Garden infers offering type from image)
garden-rake adopt my-mongodb-container

# Output:
# Detected offering: mongodb
# ✓ Adopted as mongodb service
```

**5. Bulk adoption of legacy containers** (Complex)
```bash
# Normative (adopt all containers with prefix)
garden-rake adoption list-adoptable --format json | \
  jq -r '.[] | select(.name | startswith("legacy-")) | .name' | \
  while read -r container; do
    echo "Adopting $container..."
    garden-rake adoption adopt --name "$container"
  done
```

**Related Commands**: [find strays](#find-strays), [release](#release), [remove](#remove)

---

### release

**Purpose**: Release an adopted service back to the wild (stop managing, but keep container).

**Scope**: Stone-scoped (respects tending context)

**Metaphor**: Like releasing an adopted plant back to grow wild—remove from Zen Garden management but leave the container running.

**Syntax**:
```bash
# Zen
garden-rake release <service> [on <stone>]

# Normative
garden-rake adoption unadopt --name <service> [--on <stone>]
```

**Examples** (ordered by complexity):

**1. Release an adopted service** (Simple)
```bash
# Zen
garden-rake release mongodb

# Normative
garden-rake adoption unadopt --name mongodb

# Output:
# ✓ Released service 'mongodb' (container still running)
```

**2. Release service on specific stone** (Medium)
```bash
# Zen
garden-rake release mongodb on stone-02

# Normative
garden-rake adoption unadopt --name mongodb --on stone-02
```

**3. Release and verify container still exists** (Medium)
```bash
# Zen
garden-rake release mongodb
# Verify with Docker
docker ps | grep mongodb

# Normative
garden-rake adoption unadopt --name mongodb
docker ps --filter "name=mongodb" --format "{{.Names}}"
```

**4. Migrate service management** (Complex - release from Zen Garden, adopt to different system)
```bash
# Release from Zen Garden
garden-rake release mongodb

# Container is now unmanaged, can be adopted by different orchestration
other-orchestrator adopt mongodb-container
```

**5. Temporary release for external maintenance** (Complex)
```bash
# Release from Zen Garden for maintenance
garden-rake release mongodb

# External maintenance
docker exec mongodb mongorestore --archive=/backup/latest.archive

# Re-adopt after maintenance
garden-rake adopt mongodb-container
```

**Related Commands**: [adopt](#adopt), [remove](#remove), [find strays](#find-strays)

---

### borrow

**Purpose**: Register an external network service (not managed by this garden) for reference and discovery.

**Scope**: Stone-scoped (respects tending context)

**Metaphor**: Like borrowing a tool from a neighbor's garden—acknowledge its existence and integrate it into your workflows without managing it directly.

**Syntax**:
```bash
# Zen
garden-rake borrow <service> from <url> [on <stone>]

# Normative
garden-rake adoption borrow --name <service> --url <url> [--on <stone>]
```

**Examples** (ordered by complexity):

**1. Borrow an external Redis instance** (Simple)
```bash
# Zen
garden-rake borrow redis from redis://company-cache:6379

# Normative
garden-rake adoption borrow --name redis --url redis://company-cache:6379

# Output:
# ✓ Registered borrowed service 'redis' at company-cache:6379
```

**2. Borrow service using flag syntax** (Medium)
```bash
# Zen (alternative flag syntax)
garden-rake borrow redis --from redis://company-cache:6379

# Normative (same)
garden-rake adoption borrow --name redis --url redis://company-cache:6379
```

**3. Borrow service on specific stone** (Medium)
```bash
# Zen
garden-rake borrow redis from redis://company-cache:6379 on stone-02

# Normative
garden-rake adoption borrow --name redis --url redis://company-cache:6379 --on stone-02
```

**4. Borrow multiple company services** (Complex)
```bash
# Zen
garden-rake borrow redis from redis://company-cache:6379
garden-rake borrow postgres from postgresql://company-db:5432
garden-rake borrow elasticsearch from http://company-search:9200

# Normative (scriptable)
services=(
  "redis:redis://company-cache:6379"
  "postgres:postgresql://company-db:5432"
  "elasticsearch:http://company-search:9200"
)

for entry in "${services[@]}"; do
  name="${entry%%:*}"
  url="${entry#*:}"
  garden-rake adoption borrow --name "$name" --url "$url"
done
```

**5. Borrow with connection validation** (Complex)
```bash
# Normative (validate connectivity before registering)
redis_url="redis://company-cache:6379"

# Test connection
if redis-cli -u "$redis_url" ping > /dev/null 2>&1; then
  echo "Connection successful, registering..."
  garden-rake adoption borrow --name redis --url "$redis_url"
else
  echo "ERROR: Cannot connect to $redis_url"
  exit 1
fi
```

**Related Commands**: [return](#return), [borrowed](#borrowed), [adopt](#adopt)

---

### return

**Purpose**: Unregister a borrowed external service.

**Scope**: Stone-scoped (respects tending context)

**Metaphor**: Return a borrowed tool to the neighbor's garden—stop referencing the external service.

**Syntax**:
```bash
# Zen
garden-rake return <service> [on <stone>]

# Normative
garden-rake adoption unborrow --name <service> [--on <stone>]
```

**Examples** (ordered by complexity):

**1. Return a borrowed service** (Simple)
```bash
# Zen
garden-rake return redis

# Normative
garden-rake adoption unborrow --name redis

# Output:
# ✓ Unregistered borrowed service 'redis'
```

**2. Return service on specific stone** (Medium)
```bash
# Zen
garden-rake return redis on stone-02

# Normative
garden-rake adoption unborrow --name redis --on stone-02
```

**3. Return all borrowed services** (Complex)
```bash
# Normative
garden-rake adoption list-borrowed --format json | \
  jq -r '.[].name' | \
  while read -r service; do
    garden-rake adoption unborrow --name "$service"
  done
```

**Related Commands**: [borrow](#borrow), [borrowed](#borrowed)

---

### find strays

**Purpose**: List containers running on a stone that are not managed by Zen Garden (adoptable containers).

**Scope**: Stone-scoped (respects tending context)

**Metaphor**: Like finding stray plants growing wild in your garden space—containers that exist but aren't under Zen Garden management.

**Syntax**:
```bash
# Zen
garden-rake find strays [on <stone>]

# Normative
garden-rake adoption list-adoptable [--on <stone>]
```

**Examples** (ordered by complexity):

**1. Find strays on tended stone** (Simple)
```bash
# Zen
garden-rake find strays

# Normative
garden-rake adoption list-adoptable

# Output:
# Adoptable containers:
#   my-mongodb-container (image: mongo:7.0)
#   legacy-redis (image: redis:6.2)
#   test-postgres (image: postgres:15)
```

**2. Find strays on specific stone** (Medium)
```bash
# Zen
garden-rake find strays on stone-02

# Normative
garden-rake adoption list-adoptable --on stone-02
```

**3. Find and count strays** (Medium)
```bash
# Normative
stray_count=$(garden-rake adoption list-adoptable --format json | jq 'length')
echo "Found $stray_count adoptable containers"
```

**4. Find strays and adopt them selectively** (Complex)
```bash
# Normative (adopt all mongodb containers)
garden-rake adoption list-adoptable --format json | \
  jq -r '.[] | select(.image | contains("mongo")) | .name' | \
  while read -r container; do
    echo "Adopting MongoDB container: $container"
    garden-rake adoption adopt --name "$container"
  done
```

**5. Cross-stone stray report** (Complex)
```bash
# Normative (find strays across all stones)
echo "Stray Container Report"
echo "====================="
for stone in stone-01 stone-02 stone-03; do
  echo "Stone: $stone"
  garden-rake adoption list-adoptable --on "$stone" --format json | \
    jq -r '.[] | "  - \(.name) (\(.image))"'
  echo ""
done
```

**Related Commands**: [adopt](#adopt), [release](#release), [list](#list)

---

### adopted

**Purpose**: List services that were adopted from existing containers.

**Scope**: Stone-scoped (respects tending context)

**Syntax**:
```bash
# Zen
garden-rake adopted [on <stone>]

# Normative
garden-rake adoption list-adopted [--on <stone>]
```

**Examples** (ordered by complexity):

**1. List adopted services on tended stone** (Simple)
```bash
# Zen
garden-rake adopted

# Normative
garden-rake adoption list-adopted

# Output:
# Adopted services:
#   mongodb (adopted from: my-mongodb-container)
#   redis (adopted from: legacy-redis)
```

**2. List adopted services on specific stone** (Medium)
```bash
# Zen
garden-rake adopted on stone-02

# Normative
garden-rake adoption list-adopted --on stone-02
```

**3. Compare adopted vs offered services** (Complex)
```bash
# Normative
echo "=== Offered Services ==="
garden-rake services list --format json | jq -r '.[] | select(.mode=="offered") | .name'

echo ""
echo "=== Adopted Services ==="
garden-rake adoption list-adopted --format json | jq -r '.[].name'
```

**Related Commands**: [adopt](#adopt), [borrowed](#borrowed), [list](#list)

---

### borrowed

**Purpose**: List external services that have been borrowed (registered but not managed).

**Scope**: Stone-scoped (respects tending context)

**Syntax**:
```bash
# Zen
garden-rake borrowed [on <stone>]

# Normative
garden-rake adoption list-borrowed [--on <stone>]
```

**Examples** (ordered by complexity):

**1. List borrowed services on tended stone** (Simple)
```bash
# Zen
garden-rake borrowed

# Normative
garden-rake adoption list-borrowed

# Output:
# Borrowed services:
#   redis (redis://company-cache:6379)
#   postgres (postgresql://company-db:5432)
```

**2. List borrowed services on specific stone** (Medium)
```bash
# Zen
garden-rake borrowed on stone-02

# Normative
garden-rake adoption list-borrowed --on stone-02
```

**3. Export borrowed services configuration** (Complex)
```bash
# Normative
garden-rake adoption list-borrowed --format json | \
  jq -r '.[] | "\(.name)=\(.url)"' > borrowed-services.env

# Output file:
# redis=redis://company-cache:6379
# postgres=postgresql://company-db:5432
```

**Related Commands**: [borrow](#borrow), [return](#return), [adopted](#adopted)

---

### watch

**Purpose**: Stream real-time events or service logs.

**Scope**: Stone-scoped (respects tending context)

**Syntax**:
```bash
# Zen
garden-rake watch [offering <name> logs] [on <stone>] [--until <string>]

# Normative
garden-rake events stream [--on <stone>]                       # Watch events
garden-rake services logs --name <service> [--on <stone>]      # Watch logs
```

**Examples** (ordered by complexity):

**1. Watch all events on tended stone** (Simple)
```bash
# Zen
garden-rake watch

# Normative
garden-rake events stream

# Output (streaming):
# [10:23:45] SERVICE mongodb started
# [10:23:47] SERVICE redis health check passed
# [10:24:02] OFFERING grafana compatibility check completed
```

**2. Watch service logs** (Simple)
```bash
# Zen
garden-rake watch offering mongodb logs

# Normative
garden-rake services logs --name mongodb

# Output (streaming):
# 2026-01-21T10:23:45.123Z [INFO] MongoDB starting...
# 2026-01-21T10:23:46.456Z [INFO] Listening on port 27017
```

**3. Watch events on specific stone** (Medium)
```bash
# Zen
garden-rake watch on stone-02

# Normative
garden-rake events stream --on stone-02
```

**4. Watch logs with exit condition** (Medium)
```bash
# Zen (exit when "ready" appears)
garden-rake watch offering mongodb logs --until "ready"

# Normative
garden-rake services logs --name mongodb | grep -m 1 "ready"
```

**5. Monitor deployment health** (Complex)
```bash
# Zen (watch until service is healthy)
garden-rake offer mongodb &
garden-rake watch offering mongodb logs --until "listening"
echo "MongoDB is ready!"

# Normative
garden-rake services create --name mongodb &
garden-rake services logs --name mongodb --follow | grep -m 1 "listening"
echo "MongoDB is ready!"
```

**6. Multi-service log aggregation** (Complex)
```bash
# Normative (tail logs from multiple services)
services=("mongodb" "redis" "elasticsearch")
for service in "${services[@]}"; do
  garden-rake services logs --name "$service" --follow --tail 10 &
done
wait
```

**Related Commands**: [list](#list), [status](#status), [offer](#offer)

---

### status

**Purpose**: Show detailed status of the local/tended stone.

**Scope**: Stone-scoped (respects tending context)

**Syntax**:
```bash
# Zen
garden-rake status [on <stone>]

# Normative
garden-rake stones status [--on <stone>]
```

**Examples** (ordered by complexity):

**1. Show status of tended stone** (Simple)
```bash
# Zen
garden-rake status

# Normative
garden-rake stones status

# Output:
# Stone: stone-01
# Vitality: Thriving ✓
# Services: 3 running, 1 stopped
# Uptime: 5 days
# Resources:
#   CPU: 4 cores (45% usage)
#   Memory: 16GB (8GB used)
#   Disk: 500GB (120GB used)
```

**2. Show status of specific stone** (Medium)
```bash
# Zen
garden-rake status on stone-02

# Normative
garden-rake stones status --on stone-02
```

**3. Status with JSON output** (Medium)
```bash
# Normative
garden-rake stones status --format json | jq .

# Output:
# {
#   "name": "stone-01",
#   "vitality": "thriving",
#   "services": {"running": 3, "stopped": 1},
#   "resources": {"cpu_cores": 4, "memory_mb": 16384, "disk_gb": 500}
# }
```

**4. Monitor stone health over time** (Complex)
```bash
# Normative (check every 30 seconds)
while true; do
  clear
  date
  garden-rake stones status --on stone-02
  sleep 30
done
```

**5. Alert on resource thresholds** (Complex)
```bash
# Normative (check disk usage, alert if >80%)
disk_usage=$(garden-rake stones status --format json | jq -r '.resources.disk_usage_percent')
if (( $(echo "$disk_usage > 80" | bc -l) )); then
  echo "ALERT: Disk usage at ${disk_usage}%"
  # Send alert...
fi
```

**Related Commands**: [observe](#observe), [list](#list), [watch](#watch)

---

### place

**Purpose**: Initialize pond security (place keystone) or join an existing pond (place stone).

**Scope**: Stone-scoped (respects tending context)

**Metaphor**: Place a keystone to establish the foundation of pond trust, or place a stone to join the pond.

**Syntax**:
```bash
# Zen
garden-rake place keystone [--passphrase <pass>] [on <stone>]
garden-rake place stone --code <code> [on <stone>]

# Normative
garden-rake pond init [--passphrase <pass>] [--on <stone>]
garden-rake pond join --code <code> [--on <stone>]
```

**Important**: `keystone` and `stone` are **reserved keywords** in this context. `keystone` refers to the foundational security element; `stone` indicates joining an existing pond. See [Reserved Keywords](#reserved-keywords).

**Examples** (ordered by complexity):

**1. Initialize pond (place keystone)** (Simple)
```bash
# Zen
garden-rake place keystone

# Normative
garden-rake pond init

# Output:
# Initializing pond security...
# ✓ Generated keystone certificate
# ✓ Pond initialized
# This stone is now the cornerstone
```

**2. Initialize with passphrase** (Medium)
```bash
# Zen
garden-rake place keystone --passphrase "my-secure-passphrase"

# Normative
garden-rake pond init --passphrase "my-secure-passphrase"
```

**3. Join pond with invitation code** (Medium)
```bash
# Zen
garden-rake place stone --code 123456

# Normative
garden-rake pond join --code 123456

# Output:
# Joining pond...
# ✓ Verified invitation code
# ✓ Obtained certificate from cornerstone
# ✓ Joined pond
```

**4. Initialize pond on specific stone** (Complex)
```bash
# Zen
garden-rake place keystone on stone-01

# Normative
garden-rake pond init --on stone-01
```

**5. Multi-stone pond setup workflow** (Complex)
```bash
# Normative (establish pond across 3 stones)
# Step 1: Initialize on stone-01
garden-rake pond init --on stone-01

# Step 2: Generate invitation
invite_code=$(garden-rake pond invite --on stone-01 --format json | jq -r '.code')

# Step 3: Join from stone-02
garden-rake pond join --code "$invite_code" --on stone-02

# Step 4: Generate new invitation and join from stone-03
invite_code=$(garden-rake pond invite --on stone-01 --format json | jq -r '.code')
garden-rake pond join --code "$invite_code" --on stone-03

# Verify
garden-rake pond status --on stone-01
```

**Related Commands**: [invite](#invite), [lift](#lift)

---

### invite

**Purpose**: Generate a time-limited TOTP invitation code for another stone to join the pond.

**Scope**: Stone-scoped (respects tending context)

**Metaphor**: Invite another stone into the trusted pond community.

**Syntax**:
```bash
# Zen
garden-rake invite [on <stone>]

# Normative
garden-rake pond invite [--on <stone>]
```

**Examples** (ordered by complexity):

**1. Generate invitation on tended stone** (Simple)
```bash
# Zen
garden-rake invite

# Normative
garden-rake pond invite

# Output:
# Invitation Code: 234567
# Expires: 2026-01-21 11:30:45 (5 minutes)
#
# To join, run on the new stone:
#   garden-rake place stone --code 234567
```

**2. Generate invitation on specific stone** (Medium)
```bash
# Zen
garden-rake invite on stone-01

# Normative
garden-rake pond invite --on stone-01
```

**3. Generate invitation with JSON output** (Medium)
```bash
# Normative
garden-rake pond invite --format json

# Output:
# {
#   "code": "345678",
#   "expires_at": "2026-01-21T11:35:45Z",
#   "ttl_seconds": 300,
#   "inviter_stone": "stone-01"
# }
```

**4. Automated invitation workflow** (Complex)
```bash
# Normative (generate invitation and pass to joining stone)
invite_code=$(garden-rake pond invite --on stone-01 --format json | jq -r '.code')

# Use invitation immediately on another stone
ssh stone-02 "garden-rake pond join --code $invite_code"
```

**5. Invitation expiry handling** (Complex)
```bash
# Normative (generate invitation with retry on expiry)
max_attempts=3
attempt=0

while [ $attempt -lt $max_attempts ]; do
  invite_code=$(garden-rake pond invite --on stone-01 --format json | jq -r '.code')

  if ssh stone-02 "garden-rake pond join --code $invite_code"; then
    echo "Successfully joined pond"
    break
  else
    echo "Invitation expired, regenerating..."
    ((attempt++))
  fi
done
```

**Related Commands**: [place](#place), [lift](#lift)

---

### lift

**Purpose**: Remove the pond entirely (lift keystone) or remove a specific stone from the pond (lift stone).

**Scope**: Stone-scoped (respects tending context)

**Metaphor**: Lift the keystone to collapse the entire pond structure, or lift a specific stone out of the pond to remove it from the trusted community.

**Syntax**:
```bash
# Zen
garden-rake lift keystone [on <stone>]                    # Destroy entire pond
garden-rake lift stone <stone-name> [on <coordinator-stone>]  # Remove stone from pond

# Normative
garden-rake pond remove [--on <stone>]                    # Destroy entire pond
garden-rake pond untrust --name <stone-name> [--on <coordinator-stone>]  # Remove stone
```

**Important**: `keystone` is a **reserved keyword**, not a stone name. It refers to the foundational security element of the pond. Do not name stones "keystone".

**Examples** (ordered by complexity):

**1. Remove a stone from the pond** (Simple)
```bash
# Zen
garden-rake lift stone stone-03

# Normative
garden-rake pond untrust --name stone-03

# Output:
# ⚠️  This will remove stone-03 from the pond.
# Remove stone 'stone-03' from pond? [y/N]: y
# ✓ Stone stone-03 removed from pond
```

**2. Remove stone from specific pond coordinator** (Medium)
```bash
# Zen
garden-rake lift stone stone-03 on stone-01

# Normative
garden-rake pond untrust --name stone-03 --on stone-01
```

**3. Remove entire pond (lift keystone)** (Medium)
```bash
# Zen
garden-rake lift keystone

# Normative
garden-rake pond remove

# Output:
# ⚠️  WARNING: This will destroy the entire pond!
# All stones will lose pond certificates.
# Destroy pond? [y/N]: y
# ✓ Pond destroyed
```

**4. Remove compromised stone** (Complex)
```bash
# Normative (remove compromised stone from all coordinators)
compromised_stone="stone-03"

for stone in stone-01 stone-02; do
  echo "Removing $compromised_stone from $stone..."
  garden-rake pond untrust --name "$compromised_stone" --on "$stone"
done

# Verify removal
garden-rake pond status --on stone-01
```

**Related Commands**: [place](#place), [invite](#invite)

---

### make

**Purpose**: Control stone console output verbosity.

**Scope**: Stone-scoped (respects tending context)

**Metaphor**: Make the stone sing (verbose), be quiet (informative), silent (no output), or minimal (critical only).

**Syntax**:
```bash
# Zen
garden-rake make stone <mode> [--forever] [on <stone>]
# Modes: sing (verbose), quiet (informative), silent, minimal

# Normative
garden-rake console set-mode --mode <mode> [--persist] [--on <stone>]
# Modes: verbose, informative, silent, minimal
```

**Examples** (ordered by complexity):

**1. Make stone verbose temporarily** (Simple)
```bash
# Zen
garden-rake make stone sing

# Normative
garden-rake console set-mode --mode verbose

# Output:
# ✓ Console mode: informative → verbose (30 min timeout)
```

**2. Make stone verbose permanently** (Medium)
```bash
# Zen
garden-rake make stone sing --forever

# Normative
garden-rake console set-mode --mode verbose --persist
```

**3. Return to default verbosity** (Simple)
```bash
# Zen
garden-rake make stone quiet

# Normative
garden-rake console set-mode --mode informative
```

**4. Silence stone for systemd/service use** (Medium)
```bash
# Zen
garden-rake make stone silent on stone-02

# Normative
garden-rake console set-mode --mode silent --on stone-02 --persist
```

**5. Minimal mode for critical events only** (Medium)
```bash
# Zen
garden-rake make stone minimal

# Normative
garden-rake console set-mode --mode minimal
```

**6. Debug workflow (verbose during investigation)** (Complex)
```bash
# Normative (enable verbose, investigate, then restore)
# Save current mode
current_mode=$(garden-rake console get-mode --format json | jq -r '.mode')

# Enable verbose
garden-rake console set-mode --mode verbose

# Investigate issue
garden-rake services restart --name mongodb
garden-rake services logs --name mongodb

# Restore original mode
garden-rake console set-mode --mode "$current_mode"
```

**Related Commands**: [watch](#watch), [status](#status)

---

### refresh

**Purpose**: Upgrade moss or rake binary on a stone (development/maintenance use).

**Scope**: Stone-scoped (respects tending context)

**Syntax**:
```bash
# Zen
garden-rake refresh <component> --from <path> [on <stone>]
# Components: moss, rake

# Normative
garden-rake stones upgrade --component <component> --from <path> [--on <stone>]
```

**Examples** (ordered by complexity):

**1. Upgrade moss daemon** (Simple)
```bash
# Zen
garden-rake refresh moss --from ./target/release/garden-moss

# Normative
garden-rake stones upgrade --component moss --from ./target/release/garden-moss

# Output:
# Validating binary...
# ✓ Architecture compatible (x86_64)
# Uploading binary...
# ✓ Binary uploaded
# Restarting moss...
# ✓ Moss upgraded successfully
```

**2. Upgrade rake CLI** (Simple)
```bash
# Zen
garden-rake refresh rake --from ./dist/linux-x64/garden-rake

# Normative
garden-rake stones upgrade --component rake --from ./dist/linux-x64/garden-rake
```

**3. Upgrade on specific stone** (Medium)
```bash
# Zen
garden-rake refresh moss --from ./garden-moss on stone-02

# Normative
garden-rake stones upgrade --component moss --from ./garden-moss --on stone-02
```

**4. Upgrade all stones to new version** (Complex)
```bash
# Normative (deploy new moss to all stones)
moss_binary="./release/garden-moss-v1.2.0"

for stone in stone-01 stone-02 stone-03; do
  echo "Upgrading $stone..."
  garden-rake stones upgrade \
    --component moss \
    --from "$moss_binary" \
    --on "$stone"

  # Wait for restart
  sleep 5

  # Verify
  version=$(garden-rake stones status --on "$stone" --format json | jq -r '.version')
  echo "$stone upgraded to $version"
done
```

**5. Canary deployment pattern** (Complex)
```bash
# Normative (upgrade one stone, verify, then upgrade rest)
new_moss="./garden-moss-v1.2.0"

# Upgrade canary
echo "Upgrading canary stone-01..."
garden-rake stones upgrade --component moss --from "$new_moss" --on stone-01

# Monitor for 10 minutes
sleep 600

# Check health
if garden-rake stones status --on stone-01 --format json | jq -e '.vitality=="thriving"'; then
  echo "Canary successful, upgrading remaining stones..."
  for stone in stone-02 stone-03; do
    garden-rake stones upgrade --component moss --from "$new_moss" --on "$stone"
  done
else
  echo "Canary failed, rolling back..."
  garden-rake stones upgrade --component moss --from "./garden-moss-v1.1.0" --on stone-01
fi
```

**Related Commands**: [status](#status), [take-root](#take-root)

---

### take-root

**Purpose**: Install moss as a Windows system service (self-install).

**Scope**: Stone-scoped (respects tending context)

**Metaphor**: The stone "takes root" in the system—becomes a permanent fixture.

**Syntax**:
```bash
# Zen
garden-rake take-root [on <stone>]

# Normative
garden-rake stones install-service [--on <stone>]
```

**Examples** (ordered by complexity):

**1. Install service on tended stone** (Simple)
```bash
# Zen
garden-rake take-root

# Normative
garden-rake stones install-service

# Output:
# Installing ZenGardenMoss service...
# ✓ Service installed
# ✓ Service started
#
# To uninstall: sc delete ZenGardenMoss
```

**2. Install service on specific stone** (Medium)
```bash
# Zen (requires stone to be Windows)
garden-rake take-root on windows-stone-01

# Normative
garden-rake stones install-service --on windows-stone-01
```

**3. Install across multiple Windows stones** (Complex)
```bash
# Normative
windows_stones=("windows-stone-01" "windows-stone-02")

for stone in "${windows_stones[@]}"; do
  echo "Installing service on $stone..."
  garden-rake stones install-service --on "$stone"

  # Verify service is running
  sleep 3
  if garden-rake stones status --on "$stone" | grep -q "running"; then
    echo "✓ $stone service running"
  else
    echo "✗ $stone service failed"
  fi
done
```

**Related Commands**: [refresh](#refresh), [status](#status)

---

### reconcile

**Purpose**: Reconcile moss registry with actual container state (heal/adopt orphaned containers).

**Scope**: Stone-scoped (respects tending context)

**Syntax**:
```bash
# Zen
garden-rake reconcile [--drop-invalid] [on <stone>]

# Normative
garden-rake services reconcile [--drop-invalid] [--on <stone>]
```

**Examples** (ordered by complexity):

**1. Reconcile registry on tended stone** (Simple)
```bash
# Zen
garden-rake reconcile

# Normative
garden-rake services reconcile

# Output:
# Reconciling registry...
# Found 2 orphaned containers:
#   - zen-offering-mongodb (adopted)
#   - zen-offering-redis (adopted)
# ✓ Reconciliation complete
```

**2. Reconcile with cleanup** (Medium)
```bash
# Zen
garden-rake reconcile --drop-invalid

# Normative
garden-rake services reconcile --drop-invalid

# Output:
# Reconciling registry...
# Found 1 invalid container: zen-offering-unknown
# ✓ Removed invalid container
# ✓ Reconciliation complete
```

**3. Reconcile on specific stone** (Medium)
```bash
# Zen
garden-rake reconcile on stone-02

# Normative
garden-rake services reconcile --on stone-02
```

**4. Post-restart reconciliation** (Complex)
```bash
# Normative (reconcile after moss restart)
# Moss just restarted, registry may be out of sync

echo "Reconciling after restart..."
garden-rake services reconcile

# List adopted services
echo "Adopted services:"
garden-rake adoption list-adopted
```

**5. Automated reconciliation after backup restore** (Complex)
```bash
# Normative (restore container state and reconcile)
# After restoring Docker containers from backup

echo "Restoring containers..."
docker load < /backup/containers.tar

echo "Reconciling registry..."
garden-rake services reconcile

# Verify all services are accounted for
expected_services=("mongodb" "redis" "elasticsearch")
for service in "${expected_services[@]}"; do
  if garden-rake services list --format json | jq -e ".[] | select(.name==\"$service\")" > /dev/null; then
    echo "✓ $service reconciled"
  else
    echo "✗ $service missing"
  fi
done
```

**Related Commands**: [adopt](#adopt), [find strays](#find-strays), [list](#list)

---

### template

**Purpose**: List or show offering template YAML files.

**Scope**: Stone-scoped (respects tending context)

**Syntax**:
```bash
# Zen
garden-rake template list [on <stone>]
garden-rake template show <name> [on <stone>]

# Normative
garden-rake templates list [--on <stone>]
garden-rake templates show --name <name> [--on <stone>]
```

**Examples** (ordered by complexity):

**1. List available templates** (Simple)
```bash
# Zen
garden-rake template list

# Normative
garden-rake templates list

# Output:
# Available templates:
#   mongodb (Database)
#   redis (Cache)
#   elasticsearch (Search)
#   grafana (Monitoring)
```

**2. Show template content** (Simple)
```bash
# Zen
garden-rake template show mongodb

# Normative
garden-rake templates show --name mongodb

# Output:
# name: mongodb
# category: Database
# image: mongo:7.0
# ports:
#   - 27017:27017
# volumes:
#   - mongodb-data:/data/db
```

**3. List templates on specific stone** (Medium)
```bash
# Zen
garden-rake template list on stone-02

# Normative
garden-rake templates list --on stone-02
```

**4. Export template to file** (Medium)
```bash
# Normative
garden-rake templates show --name mongodb > mongodb-template.yml
```

**5. Template validation workflow** (Complex)
```bash
# Normative (validate all templates)
templates=$(garden-rake templates list --format json | jq -r '.[].name')

for template in $templates; do
  echo "Validating $template..."

  # Get template
  content=$(garden-rake templates show --name "$template")

  # Basic YAML validation
  if echo "$content" | yq eval . > /dev/null 2>&1; then
    echo "✓ $template is valid YAML"
  else
    echo "✗ $template has invalid YAML"
  fi
done
```

**Related Commands**: [offer](#offer), [list](#list)

---

### ceremony

**Purpose**: Run guided multi-step workflows for beginners and complex operations.

**Scope**: Stone-scoped (respects tending context)

**Syntax**:
```bash
# Zen
garden-rake ceremonies          # List available ceremonies
garden-rake ceremony <name>     # Run ceremony

# Normative
garden-rake ceremonies list
garden-rake ceremonies start --name <name>
```

**Examples** (ordered by complexity):

**1. List available ceremonies** (Simple)
```bash
# Zen
garden-rake ceremonies

# Normative
garden-rake ceremonies list

# Output:
# Available ceremonies:
#   first-stone      - Guide for setting up your first stone
#   place-keystone   - Guided pond initialization
#   join-garden      - Join an existing garden
```

**2. Run first stone setup ceremony** (Simple)
```bash
# Zen
garden-rake ceremony first-stone

# Interactive output:
# Welcome to Zen Garden!
#
# This ceremony will guide you through setting up your first stone.
#
# Step 1: Choose a stone name
# Enter name (e.g., stone-01): _
```

**3. Run keystone placement ceremony** (Medium)
```bash
# Zen
garden-rake ceremony place-keystone

# Interactive:
# Keystone Placement Ceremony
# ============================
#
# This will initialize pond security on this stone.
#
# Step 1: Choose a passphrase (optional)
# Passphrase (leave empty for none): _
#
# Step 2: Confirm initialization
# Initialize pond? [y/N]: y
#
# ✓ Pond initialized
# ✓ This stone is now the cornerstone
```

**4. Run join garden ceremony** (Medium)
```bash
# Zen
garden-rake ceremony join-garden

# Interactive:
# Join Garden Ceremony
# ====================
#
# Step 1: Enter invitation code
# Code (from garden administrator): 234567
#
# Step 2: Verify connection
# Connecting to pond...
# ✓ Connected to pond
# Cornerstone: stone-01
#
# Step 3: Confirm join
# Join this pond? [y/N]: y
#
# ✓ Joined pond
```

**5. Custom ceremony for complex deployment** (Complex - concept)
```bash
# Normative (future: custom ceremonies)
garden-rake ceremonies start --name multi-service-deploy

# Interactive:
# Multi-Service Deployment Ceremony
# ==================================
#
# This ceremony will deploy a full stack.
#
# Services to deploy:
#   ☐ MongoDB
#   ☐ Redis
#   ☐ Elasticsearch
#   ☐ Grafana
#
# Select services (use arrow keys, space to select): _
```

**Related Commands**: [offer](#offer), [place](#place)

---

## Garden-Wide Commands

### observe

**Purpose**: Observe the entire garden state (all stones) or a specific stone.

**Scope**: Garden-wide (queries multiple stones)

**Metaphor**: Stand back and observe the panoramic view of your garden.

**Syntax**:
```bash
# Zen
garden-rake observe [stone-name] [--offering <filter>]

# Normative
garden-rake stones list [--name <stone-name>]
```

**Examples** (ordered by complexity):

**1. Observe entire garden** (Simple)
```bash
# Zen
garden-rake observe

# Normative
garden-rake stones list

# Output:
# Garden Overview
# ===============
#
# Stone: stone-01 (Thriving ✓)
#   Services: 3 running
#   mongodb, redis, grafana
#
# Stone: stone-02 (Thriving ✓)
#   Services: 2 running
#   elasticsearch, postgres
```

**2. Observe specific stone** (Simple)
```bash
# Zen
garden-rake observe stone-02

# Normative
garden-rake stones list --name stone-02

# Output:
# Stone: stone-02 (Thriving ✓)
# Services: 2 running, 0 stopped
#   elasticsearch (running)
#   postgres (running)
```

**3. Filter by offering** (Medium)
```bash
# Zen
garden-rake observe --offering mongodb

# Output:
# Stones with mongodb:
#   stone-01: mongodb (running)
#   stone-03: mongodb (running)
```

**4. Observe with JSON output** (Medium)
```bash
# Normative
garden-rake stones list --format json | jq .

# Output:
# [
#   {
#     "name": "stone-01",
#     "vitality": "thriving",
#     "services": ["mongodb", "redis", "grafana"]
#   },
#   {
#     "name": "stone-02",
#     "vitality": "thriving",
#     "services": ["elasticsearch", "postgres"]
#   }
# ]
```

**5. Garden health report** (Complex)
```bash
# Normative (generate comprehensive health report)
echo "Zen Garden Health Report"
echo "========================"
echo "Generated: $(date)"
echo ""

# Stone count
stone_count=$(garden-rake stones list --format json | jq 'length')
echo "Total Stones: $stone_count"

# Service count per stone
garden-rake stones list --format json | \
  jq -r '.[] | "\(.name): \(.services | length) services"'

echo ""

# Vitality summary
healthy=$(garden-rake stones list --format json | jq '[.[] | select(.vitality=="thriving")] | length')
echo "Healthy Stones: $healthy / $stone_count"

# Service distribution
echo ""
echo "Service Distribution:"
garden-rake stones list --format json | \
  jq -r '.[].services[]' | \
  sort | uniq -c | sort -rn | \
  awk '{print "  " $2 ": " $1 " instance(s)"}'
```

**Related Commands**: [list](#list), [status](#status), [watch](#watch)

---

### explore

**Purpose**: Discover stones on the network via UDP broadcast.

**Scope**: Network-wide (discovery)

**Status**: Proposed (not yet implemented)

**Syntax**:
```bash
# Zen
garden-rake explore

# Normative
garden-rake stones discover
```

**Examples** (ordered by complexity):

**1. Discover stones on network** (Simple)
```bash
# Zen
garden-rake explore

# Expected output:
# Discovering stones...
#
# Found 3 stones:
#   stone-01 (192.168.1.101:7185) - Thriving
#   stone-02 (192.168.1.102:7185) - Thriving
#   stone-03 (192.168.1.103:7185) - Needs Attention
```

**2. Discover with JSON output** (Medium)
```bash
# Normative
garden-rake stones discover --format json

# Expected output:
# [
#   {
#     "name": "stone-01",
#     "endpoint": "http://192.168.1.101:7185",
#     "vitality": "thriving"
#   },
#   {
#     "name": "stone-02",
#     "endpoint": "http://192.168.1.102:7185",
#     "vitality": "thriving"
#   }
# ]
```

**3. Discover and cache** (Complex)
```bash
# Normative (discover and cache stone endpoints)
garden-rake stones discover --format json > discovered-stones.json

# Use discovered stones
cat discovered-stones.json | jq -r '.[].endpoint' | \
  while read -r endpoint; do
    echo "Checking $endpoint..."
    garden-rake stones status --on "$endpoint"
  done
```

**Related Commands**: [observe](#observe), [tend](#tend)

---

## Local-Only Commands

### tend

**Purpose**: Manage tending context (which stone rake commands target by default).

**Scope**: Local-only (no network calls, manages local cache)

**Metaphor**: Choose which part of the garden you're tending to—sets your working context.

**Syntax**:
```bash
# Zen
garden-rake tend [target] [--clear] [--verbose]

# Normative
garden-rake context show [--verbose]
garden-rake context set --target <target>
garden-rake context clear
```

**Examples** (ordered by complexity):

**1. Show current tending state** (Simple)
```bash
# Zen
garden-rake tend

# Normative
garden-rake context show

# Output:
# Tending: stone-02 (http://192.168.1.102:7185)
# Age: 45 seconds (fresh, 45s remaining in cache)
```

**2. Set tending to localhost** (Simple)
```bash
# Zen
garden-rake tend this

# Normative
garden-rake context set --target localhost

# Output:
# ✓ Now tending: localhost (http://localhost:7185)
```

**3. Set tending to specific stone by name** (Simple)
```bash
# Zen
garden-rake tend stone-02

# Normative
garden-rake context set --target stone-02

# Output:
# Resolving stone-02...
# ✓ Now tending: stone-02 (http://192.168.1.102:7185)
```

**4. Set tending to explicit endpoint** (Medium)
```bash
# Zen
garden-rake tend http://192.168.1.105:7185

# Normative
garden-rake context set --target http://192.168.1.105:7185

# Output:
# ✓ Now tending: http://192.168.1.105:7185
```

**5. Auto-discover and set** (Medium)
```bash
# Zen
garden-rake tend auto

# Normative
garden-rake context set --target auto

# Output:
# Discovering stones...
# Found: stone-01 (192.168.1.101:7185)
# ✓ Now tending: stone-01
```

**6. Clear tending context** (Simple)
```bash
# Zen
garden-rake tend --clear

# Normative
garden-rake context clear

# Output:
# ✓ Tending cleared (will auto-discover on next command)
```

**7. Show verbose tending info** (Medium)
```bash
# Zen
garden-rake tend --verbose

# Normative
garden-rake context show --verbose

# Output:
# Tending Context (Detailed)
# ==========================
#
# Target: stone-02
# Endpoint: http://192.168.1.102:7185
# Stone Name: stone-02
# Cached At: 2026-01-21 10:23:45
# Age: 45 seconds
# TTL: 90 seconds
# Expires In: 45 seconds
# Source: Manual (user set via 'tend stone-02')
#
# Capabilities:
#   CPU: 8 cores (x86_64)
#   Memory: 32GB
#   Disk: 1TB SSD
```

**8. Workflow with automatic expiry** (Complex)
```bash
# Zen (tending expires after 90 seconds)
garden-rake tend stone-02

# Work with stone-02 for a while
garden-rake list
garden-rake offer mongodb

# ... wait 91 seconds ...

# Next command auto-discovers because tending expired
garden-rake list
# Output: Discovering stones... (tending cache expired)
```

**9. Session-based tending with environment variable** (Complex)
```bash
# Normative (use env var for session-wide tending)
export GARDEN_STONE="http://stone-02:7185"

# All commands target stone-02
garden-rake services list
garden-rake services create --name mongodb
garden-rake services start --name mongodb

# Unset to return to auto-discovery
unset GARDEN_STONE
```

**10. Multi-stone workflow with explicit targeting** (Complex)
```bash
# Normative (work with multiple stones, override tending when needed)
# Set default
garden-rake context set --target stone-01

# Work with default stone
garden-rake services list
garden-rake services create --name mongodb

# Override for specific operations
garden-rake services list --on stone-02
garden-rake services create --name redis --on stone-02

# Default still applies for next operation
garden-rake services list  # Lists stone-01 services
```

**Related Commands**: [observe](#observe), [explore](#explore), [status](#status)

---

## Tending Resolution Chain

Understanding how stone targeting works:

```
Command Execution
    ↓
Priority 1: --on flag (explicit override)
    ↓
Priority 2: GARDEN_STONE env var
    ↓
Priority 3: Tending cache (90s TTL)
    ↓
Priority 4: Auto-discover via UDP broadcast
```

**Examples**:

```bash
# Scenario 1: Explicit override wins
garden-rake tend stone-01
garden-rake list --on stone-02  # Uses stone-02 (explicit override)

# Scenario 2: Environment variable beats tending
garden-rake tend stone-01
export GARDEN_STONE="http://stone-03:7185"
garden-rake list  # Uses stone-03 (env var)

# Scenario 3: Tending cache used
garden-rake tend stone-01
garden-rake list  # Uses stone-01 (cached tending)

# Scenario 4: Auto-discovery fallback
# (No tending set, no env var)
garden-rake list  # Auto-discovers and uses first stone found
```

---

## Appendix: Quick Reference Tables

### Zen to Normative Command Map

| Zen | Normative | Function |
|-----|-----------|----------|
| **Service Lifecycle** | | |
| `offer` | `offerings list` | List offerings |
| `offer <name>` | `services create --name <name>` | Install offering |
| `rest <name>` | `services stop --name <name>` | Stop service |
| `wake <name>` | `services start --name <name>` | Start service |
| `nourish <name>` | `services upgrade --name <name>` | Upgrade service |
| `remove <name>` | `services delete --name <name>` | Soft delete (container → stray) |
| `uproot <name>` | `services destroy --name <name>` | Hard delete (destroy container) |
| **Adoption** | | |
| `adopt <name>` | `offerings adopt --name <name>` | Adopt stray container |
| `release <name>` | `offerings unadopt --name <name>` | Release adopted service |
| `find strays` | `offerings list-adoptable` | List adoptable containers |
| `adopted` | `offerings list-adopted` | List adopted services |
| `borrowed` | `offerings list-borrowed` | List borrowed services |
| `borrow <svc> from <url>` | `adoption borrow --name <svc> --url <url>` | Register external service |
| `return <name>` | `adoption unborrow --name <name>` | Unregister external service |
| **Discovery** | | |
| `list` | `services list` | List services on stone |
| `status` | `stones status` | Show stone status |
| `observe` | `stones list` | View garden |
| `watch` | `events stream` | Stream events |
| **Aliases** | | |
| `explore` | `offerings list` | Browse catalog (alias for `offer` with no args) |
| `touch` | `stones status` | Deep diagnostics (alias for `status`) |
| `garden` | `stones list` | Multi-stone view (alias for `observe`) |
| **Management** | | |
| `tend` | `context show` | Show context |
| `tend <target>` | `context set --target <target>` | Set context |
| `reconcile` | `services reconcile` | Reconcile inventory |
| `refresh <comp> --from <f>` | `stones upgrade --component <comp> --from <f>` | Upgrade binary |
| **Pond** | | |
| `place keystone` | `pond init` | Initialize pond |
| `place stone --code <c>` | `pond join --code <c>` | Join pond |
| `invite` | `pond invite` | Generate invitation |
| `lift keystone` | `pond remove` | Destroy pond |
| `lift stone <name>` | `pond untrust --name <name>` | Remove stone from pond |
| **System** | | |
| `make stone sing` | `console set-mode --mode verbose` | Set verbose |
| `take-root` | `install-service` | Install as system service |
| `template list` | `templates list` | List templates |
| `ceremony <name>` | `ceremonies start --name <name>` | Run ceremony |
| `browse-commands` | `browse-commands` | Interactive command reference |

### Command Scope Reference

| Command | Scope | Description |
|---------|-------|-------------|
| offer, rest, wake, nourish, remove, uproot | Stone-scoped | Service lifecycle management |
| list, status, watch | Stone-scoped | Service and stone monitoring |
| adopt, release, find strays | Stone-scoped | Container adoption |
| borrow, return, borrowed, adopted | Stone-scoped | External service management |
| place, invite, lift | Stone-scoped | Pond security |
| make, refresh, take-root, reconcile | Stone-scoped | Stone operations |
| template, ceremony | Stone-scoped | Templates and workflows |
| observe, explore, garden | Garden-wide | Multi-stone visibility |
| tend | Local-only | Context management |
| touch | Stone-scoped | Alias for status |

---

**End of Command Reference**
**Version**: 1.0 Draft
**Last Updated**: 2026-01-21

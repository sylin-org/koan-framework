# Offerings

*Or: what the garden grows, and how you reach it.*

---

## Still Just MongoDB

Here is something you should know before we go further: Zen Garden doesn't replace anything.

When you deploy MongoDB through an Offering, it's still MongoDB. Port 27017. Standard wire protocol. Any MongoDB driver in any language connects to it the same way it always has:

```
mongodb://stone-01.local:27017/mydb
```

That works. It will always work. Zen Garden doesn't intercept it, proxy it, or transform it. The database is just a database, running on a machine, listening on a port.

What Zen Garden *adds* is a choice:

```
zen-garden:mongodb/mydb
```

This resolves to the same place. But it doesn't require you to know *which* machine MongoDB is running on. If MongoDB moves from stone-01 to stone-02—because the hardware failed, or you needed the resources elsewhere—applications using the Zen Garden connection string keep working. Applications using the direct connection string need updating.

The choice is yours. Direct access is always available. The abstraction is *offered*, not *imposed*.

This is what it means for moss to grow atop stone. The stone doesn't change. You can still touch it directly. But the moss adds something—a softer surface, a different way to interact—without taking anything away.

---

## The Problem with Ad-Hoc

Before we talk about what Offerings provide, let's talk about what they prevent.

Here's how most people deploy a database:

```bash
docker run -d \
  --name mongodb \
  -p 27017:27017 \
  -v /data/mongo:/data/db \
  -e MONGO_INITDB_ROOT_USERNAME=admin \
  -e MONGO_INITDB_ROOT_PASSWORD=secret123 \
  mongo:7
```

This works. It's also a minefield:

- No healthcheck. You won't know if MongoDB is actually accepting connections.
- Hardcoded password. Committed to shell history. Probably reused elsewhere.
- Volume path is arbitrary. Will you remember `/data/mongo` six months from now?
- No restart policy. Machine reboots, MongoDB doesn't come back.
- Image tag is vague. Is `mongo:7` the same today as it will be next month?

Now multiply this by every service, on every Stone, configured by every person who's touched the system. Configuration drift is not a risk. It's a certainty.

Offerings exist to encode what "correctly deployed" means, so you don't have to remember it every time.

---

## Anatomy of an Offering

An Offering is a YAML template that describes how to deploy a service. Here's a simplified example:

```yaml
name: mongodb
category: database
description: Document database with ACID transactions

versions:
  default: "7.0"
  supported: ["7.0", "6.0", "5.0"]

docker:
  image: mongo
  tag: "${VERSION}"
  ports:
    - container: 27017
      host: 27017
  volumes:
    - name: mongodb-data
      mount: /data/db
  environment:
    MONGO_INITDB_ROOT_USERNAME: "${MONGO_USER:-admin}"
    MONGO_INITDB_ROOT_PASSWORD: "${MONGO_PASSWORD}"
  healthcheck:
    test: ["CMD", "mongosh", "--eval", "db.adminCommand('ping')"]
    interval: 30s
    timeout: 10s
    retries: 3
  restart: unless-stopped
```

When you run `garden-rake offer mongodb`, Moss reads this template, substitutes variables (generating a secure password if you didn't provide one), writes a Docker Compose configuration, and starts the service.

What you get:

- **Healthcheck included.** Moss knows if MongoDB is actually working.
- **Generated password.** Secure by default, stored where you can retrieve it.
- **Consistent volume naming.** `mongodb-data`, not whatever you typed at 2 AM.
- **Restart policy.** Machine reboots, MongoDB comes back.
- **Pinned version.** `7.0`, not "whatever latest means today."
- **Announced automatically.** Discovery knows MongoDB exists without additional configuration.

The template encodes the decisions so you don't have to make them every time. You can override any of them—the defaults aren't prisons—but you don't have to.

---

## The Catalog

Moss ships with a catalog of curated Offerings:

```
Databases:        mongodb, postgresql, redis, elasticsearch
Messaging:        rabbitmq
AI/ML:            ollama
Storage:          minio
Observability:    aspire-dashboard
Secrets:          vault
```

These are services we've tested, configured correctly, and documented. They deploy with sane defaults. They announce themselves for discovery. They work.

You can also create custom Offerings. The template format is documented; if you have a service that isn't in the catalog, you can write a template for it. Custom templates live in `/etc/zen-garden/templates/custom/` and appear in the catalog alongside the built-in ones.

```bash
$ garden-rake offer

Available Offerings:

  database
    mongodb          Document database with ACID transactions
    postgresql       Relational database
    redis            In-memory cache and data store

  messaging
    rabbitmq         Message broker with AMQP protocol

  ai
    ollama           Local large language model runtime

  custom
    myapp            Your custom application
```

The catalog is what the garden offers. The Offerings are what the garden has been tended to grow.

---

## Two Ways to See

Remember the dual-layer API from the architecture decisions? It manifests here.

**Offerings API** — for most people, most of the time:

```bash
$ garden-rake offer mongodb

✓ MongoDB planted on stone-01
  Connection: zen-garden:mongodb
  Status: healthy
```

Simple. What service, where it went, how to reach it. No container IDs, no image digests, no volume paths. Just: you wanted MongoDB, you have MongoDB.

**Services API** — when you need to see underneath:

```bash
$ garden-rake services show mongodb

Service: mongodb
Container: zen-mongodb-abc123
Image: mongo:7.0.4@sha256:3e8f...
Status: running (healthy)
Uptime: 3 days, 7 hours
Ports: 27017:27017/tcp
Volumes:
  mongodb-data → /data/db
Resources:
  CPU: 2.3%
  Memory: 412 MB / 2 GB limit
Healthcheck:
  Last: passed (230ms)
  Failures: 0
```

Everything. Container ID, image SHA, resource usage, healthcheck latency. This is what you need when something's wrong and you need to understand exactly what's happening.

Both views describe the same service. The Offerings API is the moss—soft, simple, what you usually want. The Services API is the stone—hard, detailed, always there when you need to touch it directly.

---

## Compatibility

Not all hardware runs all services.

MongoDB 5.0 and later require AVX instructions—a CPU feature that some older processors lack. If you try to deploy MongoDB 7 on a 2010-era Celeron, it will fail. Not gracefully—it will crash on startup with an illegal instruction error.

Offerings handle this through compatibility rules:

```yaml
compatibility:
  - condition:
      architecture: x86_64
      feature_missing: avx
    action: fallback
    image: mongo:4.4
    reason: "MongoDB 5.0+ requires AVX; falling back to 4.4"
```

When you deploy MongoDB, Moss checks the Stone's capabilities. If AVX is missing, it automatically uses the older image that works. You get MongoDB—maybe not the latest version, but a working database instead of a crash.

```bash
$ garden-rake offer mongodb

⚠ MongoDB 7.0 requires AVX instructions (not available)
  Falling back to MongoDB 4.4

✓ MongoDB planted on stone-01
  Version: 4.4 (compatibility fallback)
  Connection: zen-garden:mongodb
```

The system tells you what happened and why. You're not left guessing why MongoDB won't start on your old hardware.

---

## The Lifecycle

Offerings have a lifecycle: plant, grow, rest, wake, take away.

**Plant** — deploy the service:
```bash
$ garden-rake offer mongodb
```

**Grow** — the service runs, healthchecks pass, discovery announces it. Normal operation.

**Rest** — stop the service without removing it:
```bash
$ garden-rake rest mongodb
```
The container stops. The data remains. The configuration remains. Useful when you need to free resources temporarily.

**Wake** — restart a resting service:
```bash
$ garden-rake wake mongodb
```
Comes back exactly as it was.

**Take away** — remove the service entirely:
```bash
$ garden-rake take-away mongodb
```
Container gone. Configuration removed. Volumes preserved (by default) in case you need the data.

The vocabulary is the garden vocabulary. You don't "deploy" and "terminate." You plant and take away. The language reinforces what kind of system this is.

---

## Direct Access, Always

Let me return to where we started, because it matters.

Zen Garden provides a layer. It provides convenience, consistency, discoverability. It provides connection strings that survive hardware changes and templates that encode best practices.

But it never *requires* you to use it.

MongoDB is still listening on 27017. You can connect directly:

```python
# This works
client = MongoClient("mongodb://stone-01.local:27017/mydb")

# This also works
client = MongoClient(resolve("zen-garden:mongodb/mydb"))
```

The first is direct. You know where MongoDB is. You don't need discovery. Maybe you're debugging and want to eliminate variables. Maybe you're running a quick test. Maybe you just prefer explicit connection strings.

The second is discovered. You don't need to know where MongoDB is. If it moves, you don't update configuration. Maybe you're building for resilience. Maybe you don't want to hardcode machine names. Maybe you like the abstraction.

Both are valid. Zen Garden offers a path; it doesn't block the other paths. The moss grows atop the stone. The stone is still there, unchanged, directly accessible.

This is what "non-intrusive" means. The system adds options. It doesn't remove them.

---

## What Comes Next

Offerings describe what grows in the garden. But where does the garden keep its knowledge? Where is truth?

The next pillar, **State**, explains Zen Garden's unusual approach: a stateless daemon in a world that expects databases. How can Moss know anything if it doesn't store anything? The answer is stranger and simpler than you might expect.

---

*Zen Garden Documentation — Technical Pillars*

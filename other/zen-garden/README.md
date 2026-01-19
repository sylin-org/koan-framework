# Zen Garden

**Automatic service discovery for self-hosted infrastructure. No hardcoded IPs.**

Turn old laptops into database servers. Swap failed hardware without updating configs.

---

## 30-Second Pitch

Your app asks "Where's MongoDB?" A Stone answers. Connection established.

```bash
# This never changes, even when hardware fails
MONGODB_URI=zen-garden:mongodb/mydb
```

When your laptop's hard drive dies, swap in a replacement. Apps reconnect automatically.

**That's the entire idea.** Everything else (security, registries, configuration) is optional.

---

## 2-Minute Mental Model

### The Problem: Configuration Brittleness

```bash
# Traditional approach: tightly coupled to machines
MONGODB_URI=mongodb://old-laptop-01.local:27017
REDIS_URL=redis://thin-client-02.local:6379

# When old-laptop-01 dies:
# → Deploy replacement
# → Rename new machine to old-laptop-01 (complex networking)
#    OR update every app's config (error-prone, downtime)
```

Self-hosting fails because **machines fail**. Every hardware swap requires coordination across your entire stack.

### The Solution: Resource Abstraction

```bash
# Connection strings reference SERVICES, not MACHINES
MONGODB_URI=zen-garden:mongodb/mydb

# Stone announces: "I offer MongoDB"
# App discovers: "Who has MongoDB?"
# Connection: Automatic, no config updates
```

**Stones** are devices offering services (any laptop, desktop, Raspberry Pi, thin client).  
**Discovery** is automatic via mDNS (same protocol as AirPlay, Chromecast—20+ years proven).  
**Connection strings** never change, even when hardware is replaced.

### The Mission: Repurpose, Don't Discard

**62 million tonnes of e-waste in 2024.** Much of it functional but "too slow" for primary use.

A 2015 laptop is insufficient for video editing but perfect for MongoDB (handles 1,000+ req/sec). Zen Garden makes repurposing **frictionless**:

- No networking expertise required (discovery is automatic)
- Physical infrastructure (point at blue device = database)
- Zero monthly cloud costs ($90-350/month → $2-5/month electricity)

**Environmental + Economic + Digital Sovereignty.**

---

## Quickstart (60 Seconds)

```bash
# Terminal 1: Start MongoDB Stone
docker run -d -p 27017:27017 --name mongo-stone \
  -e ANNOUNCE_SERVICE=mongodb \
  zen-garden/stone:latest

# Terminal 2: App connects automatically
CONNECTION_STRING="zen-garden:mongodb" node app.js
```

**What happened:**
1. Stone announced "I offer MongoDB" via mDNS
2. App asked "Who has MongoDB?" via multicast
3. Stone responded with connection details
4. Connection established

Want to run on real hardware? → **[Start Here Guide](docs/start-here.md)**

---

## Documentation

**New here?**  
→ [Start Here Guide](docs/start-here.md) - Zero to first working Stone in 30 minutes

**Need the why?**  
→ [Mission](docs/mission.md) - E-waste crisis, self-hosting barriers, social value

**Want to understand the design?**  
→ [Core Concepts](docs/concepts/overview.md) - 2-minute mental model  
→ [Architecture](docs/concepts/architecture.md) - Component relationships  
→ [Use Cases](docs/concepts/stories.md) - Real people using Zen Garden

**Operating Stones?**  
→ [Hardware Guide](docs/guides/hardware.md) - Turn old devices into Stones  
→ [Offering Services](docs/guides/offering-services.md) - Plant and manage services  
→ [Troubleshooting](docs/guides/troubleshooting.md) - Common problems and solutions

**Need technical references?**  
→ [API Reference](docs/reference/api.md) - Moss daemon HTTP API  
→ [Connection Strings](docs/reference/connection-strings.md) - Protocol details  
→ [Offerings Catalog](docs/reference/offerings.md) - Available service templates  
→ [Rake CLI](docs/reference/rake-cli.md) - Command reference

**Security concerns?**  
→ [Security Overview](docs/security/overview.md) - Threat models and guarantees  
→ [Pond Setup](docs/security/pond-setup.md) - Enable mTLS authentication

**Contributing or maintaining?**  
→ [Maintainer Docs](docs/ops/maintainers.md) - System invariants, debugging  
→ [Roadmap](docs/ops/roadmap.md) - Implementation timeline  
→ [Architecture Decisions](docs/decisions/) - ADRs documenting design choices

**Complete navigation**: [Documentation Hub](docs/README.md)

---

## Project Status

**Phase:** Protocol specification + prototype development (Q1 2026)  
**Current milestone:** Phase 0 complete (specifications), Phase 1 in progress (Python prototype, February 2026)

**What exists today:**
- Complete documentation (architecture, security, API specifications)
- NewStone.ps1 USB installer (creates bootable Debian Stones)
- Service manifest templates (14 YAML files)

**What's coming:**
- Rust CLI (`garden-rake`) and daemon (`garden-moss`) - Q2 2026
- C# client libraries - Q3 2026
- Pond security layer (mTLS) - Q3 2026

See [Roadmap](docs/ops/roadmap.md) for detailed timeline.

---

## Contributing

**Ways to help:**
- Test the prototype on your hardware
- Report issues and suggest improvements
- Write service offering templates
- Improve documentation
- Share your Stone builds

**For contributors:** See [Architecture Decisions](docs/decisions/) for design rationale.

---

## License & Maintainership

**Maintained by:** Sylin.org (Koan Framework maintainer)  
**License:** Apache 2.0 (see [LICENSE](LICENSE))  
**Repository:** [github.com/sylin/zen-garden](https://github.com/sylin/zen-garden)

---

**Zen Garden**: Because hardware shouldn't be disposable, and self-hosting shouldn't be hard.

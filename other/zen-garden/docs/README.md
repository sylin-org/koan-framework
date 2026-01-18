# Zen Garden

**Automatic service discovery for self-hosted infrastructure. Plug in a device running MongoDB—apps discover it automatically. No hardcoded IPs or server names. No configuration files.**

---

## What This Is

Zen Garden is an open protocol for discovering services on your local network. Instead of hardcoding `mongodb://192.168.1.50:27017` in every config file, you write `zen-garden:mongodb`. Services announce themselves via mDNS. Apps discover them by intent, not location.

**The result:** Turn old laptops into useful infrastructure. Reduce e-waste. Own your data.

---

## Quick Example

```bash
# Start a MongoDB Stone
docker run -d -p 27017:27017 -e ANNOUNCE_SERVICE=mongodb zen-garden/stone:latest

# Your app connects automatically
CONNECTION_STRING="zen-garden:mongodb" node app.js
```

That's it. No IP addresses, no DNS configuration, no service registry.

---

## Why This Exists

**Two problems:**

1. **Self-hosting is too hard** - IP addresses change, configurations break, coordination work scales poorly
2. **Functional hardware becomes waste** - 62 million tonnes of e-waste annually (UN 2024), much of it still functional

**One solution:** Make service discovery automatic. Give old hardware new purpose.

---

## Documentation

**Start here:**

- [Understanding Zen Garden](UNDERSTANDING.md) - How it works, core concepts
- [Getting Started](GETTING-STARTED.md) - Run your first Stone in 5 minutes

**Go deeper:**

- [Technical Reference](REFERENCE.md) - Protocol details, API documentation
- [Hardware Guide](HARDWARE.md) - Turn old laptops into Stones
- [Security Model](SECURITY.md) - When to add cryptographic binding

**Context:**

- [Mission & Impact](MISSION.md) - Environmental and social goals
- [Development Roadmap](ROADMAP.md) - Implementation timeline
- [Community Stories](STORIES.md) - Real people using Zen Garden

---

## Current Status

**Phase:** Active design + prototype development  
**Maintained by:** Sylin.org (Koan Framework maintainer)  
**License:** Open source (see LICENSE)

**What exists today:**

- Protocol specification (in development)
- Service manifests (14 service types documented)
- USB installer (Debian Stone setup via NewStone.ps1)

**What's next:**

- Hello World prototype (February 2026)
- mDNS discovery implementation
- Community validation

---

## Contributing

This is an open protocol. Multiple implementations welcome (Rust, Python, Go, JavaScript).

**How to help:**

- Test the prototype on your hardware
- Report issues, suggest improvements
- Write integrations for your favorite services
- Translate documentation
- Share your Stone builds

See [CONTRIBUTING.md](../CONTRIBUTING.md) for details.

---

## Contact

- GitHub Issues: Bug reports, feature requests
- Discussions: Questions, ideas, proposals
- Maintainer: Available via GitHub

---

_"Infrastructure you can hold, swap, and own."_

# Koan Framework Samples

**Scenario-driven examples demonstrating framework capabilities through real-world applications.**

---

## 📚 Browse the Samples

**→ [Complete Sample Catalog](CATALOG.md)** - Full descriptions, capability matrix, learning paths

### Quick Index

| Sample | Description | Complexity | Status |
|--------|-------------|------------|--------|
| **S0.ConsoleJsonRepo** | Minimal bootstrap - your first Koan app | ⭐ Beginner | ✅ Active |
| **S1.Web** | CRUD fundamentals with relationships | ⭐ Beginner | ✅ Active |
| **S3.NotifyHub** | Multi-channel notification platform | ⭐⭐ Intermediate | 🔨 Building |
| **S4.DevHub** | Secret management & DevOps dashboard | ⭐⭐ Intermediate | 🔨 Building |
| **S5.Recs** | AI-powered recommendation engine | ⭐⭐⭐ Advanced | ✅ Active |
| **S6.MediaHub** | Media processing & storage pipeline | ⭐⭐ Intermediate | 🔨 Building |
| **S8.Canon** | Canon Runtime pipelines | ⭐⭐⭐ Advanced | ✅ Active |
| **S9.OrderFlow** | Event sourcing & CQRS | ⭐⭐⭐⭐ Expert | 🔨 Building |
| **S10.DevPortal** | Framework capabilities showcase | ⭐⭐ Demo | ✅ Active |
| **S14.AdapterBench** | Provider performance benchmarking | ⭐⭐ Demo | ✅ Active |
| **S16.PantryPal** | Vision AI & MCP Code Mode | ⭐⭐⭐ Advanced | ✅ Active |
| **S18.Prism** | Personal Knowledge Intelligence — AI-powered Pulse feed | ⭐⭐⭐⭐ Expert | ✅ Active |

---

## 🚀 Getting Started

### I'm brand new to Koan
1. **S0.ConsoleJsonRepo** (5 min) - See it work
2. **S1.Web** (30 min) - Learn the patterns
3. **S10.DevPortal** (20 min) - Understand capabilities

### I'm evaluating frameworks
1. **S10.DevPortal** - See multi-provider transparency
2. **S14.AdapterBench** - Get objective performance data
3. **S5.Recs** - See a complete production-ready app

### I'm building with AI
1. **S5.Recs** - Recommendations & vector search
2. **S16.PantryPal** - Vision AI & MCP integration

### I need specific capabilities
→ **See [CATALOG.md](CATALOG.md#capability-coverage-matrix)** for "Which sample shows X?" lookup

---

## 📖 Sample Organization

### Family Structure (when applicable)

Some samples use multi-project family structure:

```
SXX.SampleName/
├── API/                # Web API application
├── Core/               # Shared contracts/models (optional)
├── MCP/                # MCP service host (optional)
├── Tools/              # CLI/worker tools (optional)
└── README.md          # Sample documentation
```

### Standalone Structure

Most samples use single-project structure:

```
SXX.SampleName/
├── Controllers/        # API endpoints
├── Models/            # Entity<T> domain models
├── Services/          # Business logic
├── wwwroot/           # UI assets (if applicable)
├── docker/            # Container definitions
├── start.bat          # One-command run script
├── README.md          # Sample documentation
└── Program.cs         # Koan bootstrap
```

---

## 🗂️ Guides

**guides/g1c1.GardenCoop** - Narrative guide demonstrating Koan patterns through community garden scenario

---

## 📦 Archived Samples

The following samples have been archived (see `archive/ARCHIVED.md` for details):

- S2, S4.Web, S6.Auth, S6.SocialCreator - No documentation or unclear purpose
- S12.MedTrials, S15.RedisInbox - Redundant or too minimal
- KoanAspireIntegration - Integration example, not sample app

**Migration guidance**: See [archive/ARCHIVED.md](archive/ARCHIVED.md)

---

## 🎯 Sample Principles

All Koan samples follow these principles:

1. **Domain-Focused** - Real applications, not "FooService" demos
2. **Entity-First** - Use Entity<T> patterns, not manual repositories
3. **Auto-Registration** - Demonstrate "Reference = Intent" philosophy
4. **Progressive Complexity** - Start simple, add sophistication naturally
5. **Production Patterns** - Show real-world error handling, testing, deployment

---

## 📚 Documentation

- **Sample Catalog**: [CATALOG.md](CATALOG.md) - Complete reference
- **Capability Map**: `docs/architecture/capability-map.md` - Framework layers
- **Port Allocations**: `docs/decisions/OPS-0014-samples-port-allocation.md`
- **Strategic Plan**: `docs/decisions/DX-0045-sample-collection-strategic-realignment.md`

---

## 🤝 Contributing

Samples welcome! Follow the established patterns:
- README following S5.Recs template (tutorial-style)
- Test examples demonstrating key patterns
- One-command run via `start.bat`/`start.sh`
- Explain *why*, not just *what*

See main repository CONTRIBUTING.md for details.

---

**Questions?** Check the [Complete Catalog](CATALOG.md) or open a GitHub issue.

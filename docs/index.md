---
type: GUIDE
domain: core
title: "Koan Framework documentation"
audience: [developers, architects, ai-agents]
status: current
last_updated: 2025-09-28
framework_version: v0.6.2
validation:
    date_last_tested: 2025-09-28
    status: verified
    scope: docs/index.md
---

# Koan Framework Documentation

**Zero-configuration .NET for the container-native era. Build sophisticated services that just work.**

## Quick Navigation

### 🚀 [Getting Started](getting-started/overview.md)

Get a Koan API running in 5 minutes or less.

### 📖 [Developer Guides](guides/README.md)

Step-by-step guides for building with Koan Framework:

- [Building APIs](guides/building-apis.md) - REST APIs with zero configuration
- [Data Modeling](guides/data-modeling.md) - Entity-first development patterns
- [AI Integration](guides/ai-integration.md) - Add AI capabilities to your apps
- [Authentication](guides/authentication-setup.md) - Zero-config OAuth, JWT, and service-to-service auth

### 🚨 [Troubleshooting Hub](support/troubleshooting.md)

Common issues and their solutions:

- **[Adapter & Data Connectivity](support/troubleshooting.md#adapter--data-connectivity)** - Database connectivity and provisioning failures
- **[Boot & Auto-Registration](support/troubleshooting.md#boot--auto-registration)** - Startup discoveries and initialization problems

### 🔬 [Deep Dive](guides/deep-dive/auto-provisioning-system.md)

Advanced architectural documentation:

- **[Auto-Provisioning System](guides/deep-dive/auto-provisioning-system.md)** - How schema provisioning works automatically
- **[Bootstrap Lifecycle](guides/deep-dive/bootstrap-lifecycle.md)** - Multi-layer application initialization process

### 📚 [API Reference](api/index.md)

Complete API documentation for all Koan modules:

- **[Core](reference/core/index.md)** - Foundational abstractions and configuration
- **[Data](reference/data/index.md)** - Entity-first data access across providers
- **[Web](reference/web/index.md)** - ASP.NET Core integration and controllers
- **[AI](reference/ai/index.md)** - Vector stores, embeddings, and agent endpoints
- **[Auto-Generated API Docs](api/index.md)** - Complete API reference from code comments

### 🏗️ [Architecture](architecture/principles.md)

Framework principles and design decisions:

- **[Framework Principles](architecture/principles.md)** - Core architectural patterns
- **[Architecture Decisions](decisions/index.md)** - Complete ADR system with 70+ decisions

### 📂 [Case Studies](case-studies/s13-docmind/index.md)

Scenario-first walkthroughs showing framework patterns in production samples.

## Framework Features

### Zero Configuration

```csharp
var builder = WebApplication.CreateBuilder(args);

// This single line handles all the complexity:
// Database setup, API generation, validation, error handling
builder.Services.AddKoan();

var app = builder.Build();
app.UseKoan();
app.Run();
```

### Entity-First Development

```csharp
public class Product : Entity<Product>
{
    public string Name { get; set; } = "";
    public decimal Price { get; set; }
    public string Category { get; set; } = "";
}

// Full REST API auto-generated
// GET/POST/PUT/DELETE /api/products
// Validation, error handling, health checks included
```

### Provider Transparency

```csharp
// Same code, different storage backends:
var products = await Product.All();  // Works with SQLite, PostgreSQL, MongoDB, etc.
```

## Current Status

- **Version**: v0.2.18 (Early Development)
- **License**: Apache 2.0
- **Target Framework**: .NET 9.0
- **Contributors**: 2 active developers

## Contributing

Ready to contribute? Check out our [Contributing Guide](../CONTRIBUTING.md) to get started.

---

**Talk to your code, don't fight it.**

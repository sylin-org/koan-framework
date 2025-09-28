# Koan Framework Documentation

**Zero-configuration .NET for the container-native era. Build sophisticated services that just work.**

## Quick Navigation

### 🚀 [Getting Started](../documentation/getting-started/quickstart.md)

Get a Koan API running in 5 minutes or less.

### 📖 [Developer Guides](../documentation/guides/)

Step-by-step guides for building with Koan Framework:

- [Building APIs](../documentation/guides/building-apis.md) - REST APIs with zero configuration
- [Data Modeling](../documentation/guides/data-modeling.md) - Entity-first development patterns
- [AI Integration](../documentation/guides/ai-integration.md) - Add AI capabilities to your apps
- [Authentication](../documentation/guides/authentication-setup.md) - Zero-config OAuth, JWT, and service-to-service auth

### 🚨 [Troubleshooting](../documentation/guides/troubleshooting/)

Common issues and their solutions:

- **[Adapter Connection Issues](../documentation/guides/troubleshooting/adapter-connection-issues.md)** - Database connectivity and provisioning failures
- **[Bootstrap Failures](../documentation/guides/troubleshooting/bootstrap-failures.md)** - Application startup and initialization problems

### 🔬 [Deep Dive](../documentation/guides/deep-dive/)

Advanced architectural documentation:

- **[Auto-Provisioning System](../documentation/guides/deep-dive/auto-provisioning-system.md)** - How schema provisioning works automatically
- **[Bootstrap Lifecycle](../documentation/guides/deep-dive/bootstrap-lifecycle.md)** - Multi-layer application initialization process

### 📚 [API Reference](../api/)

Complete API documentation for all Koan modules:

- **[Core](../documentation/reference/core/index.md)** - Foundational abstractions and configuration
- **[Data](../documentation/reference/data/index.md)** - Entity-first data access across providers
- **[Web](../documentation/reference/web/index.md)** - ASP.NET Core integration and controllers
- **[AI](../documentation/reference/ai/index.md)** - Vector stores, embeddings, and agent endpoints
- **[Auto-Generated API Docs](../api/)** - Complete API reference from code comments

### 🏗️ [Architecture](../documentation/architecture/principles.md)

Framework principles and design decisions:

- **[Framework Principles](../documentation/architecture/principles.md)** - Core architectural patterns
- **[Architecture Decisions](../documentation/decisions/)** - Complete ADR system with 70+ decisions

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

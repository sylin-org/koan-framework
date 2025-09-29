---
type: REF
domain: core
title: "Core Pillar Reference"
audience: [developers, architects, ai-agents]
status: current
last_updated: 2025-09-28
framework_version: v0.6.2
validation:
  date_last_tested: 2025-09-28
  status: verified
  scope: docs/reference/core/index.md
---

# Core Pillar Reference

**Document Type**: REF
**Target Audience**: Developers, Architects, AI Agents
**Last Updated**: 2025-09-28
**Framework Version**: v0.6.2

---

## Overview

Foundation layer providing auto-registration, health checks, configuration, and semantic streaming pipelines.

**Package**: `Koan.Core`

## Semantic Streaming Pipelines

Koan’s semantic pipelines are now documented inside the Flow pillar, alongside intake stages and controller guidance. Use that reference as the canonical source for DSL syntax, AI integration, branching, and performance patterns.

➤ **[Flow Pillar – Semantic Pipelines](../flow/index.md#semantic-pipelines)**

## Auto-Registration

Zero-config module discovery loads all referenced Koan modules automatically.

```csharp
// Program.cs
builder.Services.AddKoan();
```

### Custom Modules

```csharp
public class MyModule : IKoanAutoRegistrar
{
    public string ModuleName => "MyModule";
    public string ModuleVersion => "1.0.0";

    public void Initialize(IServiceCollection services)
    {
        services.AddScoped<IMyService, MyService>();
    }
}
```

That's it. Your module registers automatically when referenced.

## Health Checks

Built-in health endpoints with custom contributors.

### Endpoints

- `GET /api/health` - Overall health
- `GET /api/health/live` - Liveness probe
- `GET /api/health/ready` - Readiness probe

### Custom Health Checks

```csharp
public class DatabaseHealth : IHealthContributor
{
    public string Name => "database";
    public bool IsCritical => true;

    public async Task<HealthReport> CheckAsync(CancellationToken ct)
    {
        try
        {
            await CheckDatabaseConnection();
            return HealthReport.Healthy();
        }
        catch (Exception ex)
        {
            return HealthReport.Unhealthy("Database down", ex);
        }
    }
}
```

## Environment Detection

```csharp
if (KoanEnv.IsDevelopment)
{
    // Development code
}

if (KoanEnv.InContainer)
{
    // Container-specific setup
}
```

## Configuration

```csharp
// Read configuration with defaults
var setting = Configuration.Read(config, "MyApp:Setting", "default");

// Multiple fallback keys
var value = Configuration.ReadFirst(config,
    "MyApp:NewKey",
    "MyApp:OldKey");
```

Environment variables work too:

```bash
export Koan__MyApp__Setting=value
```

## Boot Reports

Development-only module discovery reporting:

```csharp
// Program.cs
var app = builder.Build();

// Logs discovered modules in development
// [INFO] Koan:modules data→sqlite web→controllers
```

Redacted in production automatically.

---

**Last Validation**: 2025-01-17 by Framework Specialist
**Framework Version Tested**: v0.2.18+

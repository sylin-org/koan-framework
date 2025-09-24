---
type: REF
domain: core
title: "Core Pillar Reference"
audience: [developers, architects, ai-agents]
last_updated: 2025-01-17
framework_version: "v0.2.18+"
status: current
validation: 2025-01-17
---

# Core Pillar Reference

**Document Type**: REF
**Target Audience**: Developers, Architects, AI Agents
**Last Updated**: 2025-01-17
**Framework Version**: v0.2.18+

---

## Overview

Foundation layer providing auto-registration, health checks, and configuration.

**Package**: `Koan.Core`

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
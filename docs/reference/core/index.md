---
type: REF
domain: core
title: "Core Pillar Reference"
audience: [developers, architects, ai-agents]
status: current
last_updated: 2025-09-28
framework_version: v0.6.3
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

Foundation layer providing auto-registration, health checks, configuration, and shared runtime contracts.

**Package**: `Koan.Core`

## Guard Utilities

Fluent, zero-allocation parameter validation with natural language syntax.

```csharp
string title = userInput.Must().NotBe.Blank();
int priority = userPriority.Must().Be.InRange(1, 5);
string email = userEmail.Must().Be.ValidEmail();
```

**Features:**
- Natural language: `value.Must().NotBe.Blank()`, `value.Must().Be.Between(1, 10, RangeType.Inclusive)`
- Automatic parameter name capture via `CallerArgumentExpression`
- Zero heap allocations (`ref struct` pattern)
- Type-safe extension methods
- Comprehensive validation: nulls, blanks, ranges, emails, URLs, enums, collections

➤ **[Guard Utilities Reference](guard-utilities.md)**

## Auto-Registration

Zero-config module discovery loads all referenced Koan modules automatically.

```csharp
// Program.cs
builder.Services.AddKoan();
```

### Custom Modules

```csharp
public sealed class MyModule : KoanModule
{
    public override void Register(IServiceCollection services)
    {
        services.AddScoped<IMyService, MyService>();
    }
}
```

That's it. Identity is derived from the package/assembly; the module activates automatically when referenced.

## Health Checks

Built-in health endpoints with custom contributors.

### Endpoints

- `GET /health/live` - Process liveness; does not probe dependencies
- `GET /health/ready` - Aggregated dependency readiness; returns `503` when unhealthy
- `GET /health` - Human-friendly process up-check
- `GET /api/health` - Lightweight compatibility up-check; not dependency readiness

### Custom Health Checks

```csharp
public class DatabaseHealth : IHealthContributor
{
    public string Name => "database";
    public bool IsCritical => true;

    public async Task<HealthReport> Check(CancellationToken ct = default)
    {
        try
        {
            await CheckDatabaseConnection();
            return new HealthReport(Name, HealthState.Healthy, "Database ready", null, null);
        }
        catch (Exception ex)
        {
            return new HealthReport(Name, HealthState.Unhealthy, ex.Message, null, null);
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

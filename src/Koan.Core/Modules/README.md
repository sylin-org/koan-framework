# Configuration & Module Utilities

This directory contains **configuration**, **options**, and **module registration utilities** for Koan Framework.

---

## 🔧 Available Utilities

### OptionsExtensions (Static Extensions)

**File**: `OptionsExtensions.cs`
**Pattern**: Static extension methods for IServiceCollection
**When to Use**: Registering options in `KoanModule.Register` or anywhere you need typed configuration

#### What It Provides

- ✅ Configuration binding with section paths
- ✅ Automatic validation setup
- ✅ Post-configuration support
- ✅ Consistent patterns across framework
- ✅ Less boilerplate in modules

#### Quick Example

```csharp
using Koan.Core.Modules;

// In your KoanModule
public override void Register(IServiceCollection services)
{
    // Basic registration
    services.AddKoanOptions<RedisOptions>("Koan:Redis");

    // With validation
    services.AddKoanOptionsWithValidation<PostgresOptions>(
        configuration,
        "Koan:Data:Postgres"
    )
    .Validate(opts => !string.IsNullOrEmpty(opts.Host), "Host is required");

    // With post-configuration
    services.AddKoanOptions<MongoOptions>(
        configuration,
        "Koan:Data:Mongo",
        opts => opts.DefaultDatabase ??= "default"
    );
}
```

#### Available Methods

```csharp
// Core registration
AddKoanOptions<TOptions>(IConfiguration, string sectionName, Action<TOptions>? configure = null)

// With validation builder
AddKoanOptionsWithValidation<TOptions>(IConfiguration, string sectionName)

// Post-configuration only
ConfigureKoanOptions<TOptions>(Action<TOptions> configure)
```

#### Common Use Cases

✅ `KoanModule.Register` implementations
✅ Options configuration in connectors
✅ Layered configuration (appsettings → env vars → code)
✅ Options validation patterns

**Full Documentation**: [Framework Utilities Guide](../../../docs/guides/framework-utilities.md#optionsextensions)

---

## 📦 Other Key Files

### Pillars/KoanPillarCatalog.cs

**Purpose**: Central registry of framework pillars (Data, AI, Cache, Web, etc.)

Framework pillars self-register via `KoanPillarManifest` attributes and are discovered at boot time.

**When to Use**: Creating a new framework pillar

---

## 📚 Related

- **ADR**: [ARCH-0068 - Refactoring Strategy](../../../docs/decisions/ARCH-0068-refactoring-strategy-static-vs-di.md)
- **Examples**: See the domain-named `*Module` classes in `src/Connectors/**/Initialization/`
- **Pattern**: [Reference = Intent](../../../docs/decisions/ARCH-0001-reference-equals-intent.md)

---

## ❓ When to Use What

| Scenario | Use This |
|----------|----------|
| Register options in a module | `services.AddKoanOptions<T>()` |
| Options with validation rules | `services.AddKoanOptionsWithValidation<T>()` |
| Modify options after registration | `services.ConfigureKoanOptions<T>()` |
| Custom options registration | Use `IOptions<T>` pattern directly |

---

## 💡 Best Practices

### ✅ DO

```csharp
// Use OptionsExtensions for consistency
services.AddKoanOptions<MyOptions>(configuration, "Koan:MyFeature");
```

### ❌ DON'T

```csharp
// Don't manually bind configuration
services.Configure<MyOptions>(opts =>
{
    configuration.GetSection("Koan:MyFeature").Bind(opts);
});
```

The `AddKoanOptions` pattern:
- Reduces boilerplate
- Ensures consistent section naming
- Supports validation out of the box
- Easier to test

---

**Last Updated**: 2025-11-03

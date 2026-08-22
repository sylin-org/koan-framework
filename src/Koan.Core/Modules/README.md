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
    // Bind and validate from a section
    services.AddKoanOptions<RedisOptions>(RedisOptions.SectionPath);

    // Add a rule of your own to the same builder
    services.AddKoanOptions<PostgresOptions>(PostgresOptions.SectionPath)
        .Validate(opts => !string.IsNullOrEmpty(opts.Host), "Host is required");

    // Normalize what was bound
    services.PostConfigure<MongoOptions>(opts => opts.DefaultDatabase ??= "default");
}
```

#### Available Methods

```csharp
// Bind a section, validate data annotations, validate at host start
AddKoanOptions<TOptions>(string? configPath = null, bool validateOnStart = true)

// The same, with a registered IConfigureOptions<TOptions> configurator
AddKoanOptions<TOptions, TConfigurator>(string? configPath = null, bool validateOnStart = true, ...)

// The same, taking IConfiguration explicitly plus an optional post-configure step
AddKoanOptions<TOptions>(IConfiguration cfg, string sectionPath, Action<TOptions>? postConfigure = null, ...)
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

Each pillar owns a static manifest -- `CorePillarManifest`, `AiPillarManifest`, and so on -- holding its code, label, colour, and icon. A pillar's module calls `EnsureRegistered()` during `Register`, and the shared `PillarManifest` latches the declaration so repeated calls are idempotent.

**When to Use**: Creating a new framework pillar

---

## 📚 Related

- **ADR**: [ARCH-0068 - Refactoring Strategy](../../../docs/decisions/ARCH-0068-refactoring-strategy-static-vs-di.md)
- **Examples**: See the domain-named `*Module` classes in `src/Connectors/**/Initialization/`
- **Pattern**: [Reference = Intent](../../../docs/decisions/ARCH-0114-layered-capability-activation.md)

---

## ❓ When to Use What

| Scenario | Use This |
|----------|----------|
| Register options in a module | `services.AddKoanOptions<T>()` |
| Extra validation rules | chain `.Validate(...)` on the returned `OptionsBuilder<T>` |
| Modify options after binding | `services.PostConfigure<T>()` |
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

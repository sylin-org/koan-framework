# Defensive Publication: Integration Recipes — Composable Cross-Cutting Concern Bundles with Package-Activated Discovery

## Header Block

- **Title:** Composable Cross-Cutting Concern Bundles with Package-Reference Activation, Layered Configuration Precedence, and Capability-Gated Application
- **Inventor:** Leo Botinelly (Leonardo Milson Botinelly Soares)
- **Disclosure Date:** 2026-03-24
- **Field of Invention:** Application framework bootstrap infrastructure, specifically methods for packaging and automatically applying cross-cutting operational concerns (observability, caching, resilience) as discoverable, composable bundles activated by package reference.
- **Keywords:** recipe, integration recipe, cross-cutting concern, auto-activation, package reference, capability gating, configuration layering, dry-run, bootstrap, observability, caching, resilience

---

## 1. Problem Statement

Enterprise applications require consistent operational wiring across services: structured logging, distributed tracing, health checks, caching, circuit breakers, retry policies. This wiring is repetitive — each new service must add the same `services.AddOpenTelemetry()`, `services.AddHealthChecks()`, `services.AddDistributedCache()` calls with project-specific configuration.

Existing approaches have limitations. Manual DI registration is error-prone (developers forget steps). NuGet meta-packages bundle assemblies but don't wire services. ASP.NET Core's convention-based configuration requires explicit method calls. Spring Boot auto-configuration scans classpath and activates conditionally, but provides no explicit configuration precedence model, no capability gating, and no dry-run mode.

What is needed is a system where adding a package reference automatically applies best-practice operational wiring, with explicit configuration layering, the ability to gate application on required capabilities, and a dry-run mode for validating what would be applied.

---

## 2. Prior Art Summary

**ASP.NET Core Extensions:** Each cross-cutting concern requires explicit `services.AddXxx()` calls in `Program.cs`. No auto-activation. No bundling of related concerns.

**Spring Boot Auto-Configuration:** Scans classpath for `@ConditionalOnClass` / `@ConditionalOnProperty` annotations. Closest prior art. However: no explicit configuration precedence model (Spring's order is implicit via `@Order`/`@AutoConfigureAfter`), no capability gating (conditions are binary, not capability-aware), no dry-run mode, and no structured logging of apply/skip decisions with stable event IDs.

**NuGet Meta-Packages (e.g., `Microsoft.AspNetCore.App`):** Bundle assemblies but do not register services. Adding a meta-package reference does not wire any DI services.

**Specific gaps:**
1. No framework provides an explicit configuration precedence chain: `Provider defaults < Recipe defaults < AppSettings/Env < Code overrides < Recipe forced overrides`.
2. No framework provides capability gating: recipes that check for required services before applying.
3. No framework provides dry-run mode for recipe validation.
4. No framework provides structured logging of recipe decisions with stable event IDs.

---

## 3. Detailed Description of the Invention

### 3.1 IKoanRecipe Interface

```
IKoanRecipe:
  Name: string                      — unique recipe identifier
  Order: int (default 0)            — execution order (lower first)
  ShouldApply(cfg, env): bool       — capability gate (default: true)
  Apply(services, cfg, env): void   — DI registration and configuration
```

Recipes are small, focused bootstrap bundles. Each encapsulates the wiring for one cross-cutting concern (e.g., "observability", "distributed-cache", "resilience").

### 3.2 Discovery Mechanisms

**Mechanism 1: Assembly Attribute (AOT-safe, auto-activation)**
```
[assembly: KoanRecipe(typeof(ObservabilityRecipe))]
```
Adding the package reference causes the assembly attribute to be discovered during AppBootstrapper's assembly scan. The recipe is automatically registered.

**Mechanism 2: Explicit Registration**
```
services.AddRecipe<ObservabilityRecipe>();
```

**Mechanism 3: Configuration List**
```
Koan:Recipes:Active: ["observability", "caching"]
```

All mechanisms populate the same `RecipeRegistry`. Duplicates are deduplicated by recipe Name.

### 3.3 Configuration Layering (Explicit Precedence)

```
Layer 1: Provider defaults (lowest priority)
  — Adapter/provider-specific defaults from Options classes

Layer 2: Recipe defaults
  — Values set by the recipe's Apply() method
  — Override provider defaults

Layer 3: AppSettings / Environment Variables
  — Standard ASP.NET Core configuration
  — Override recipe defaults

Layer 4: Code overrides
  — Explicit services.Configure<T>() calls in Program.cs
  — Override AppSettings

Layer 5: Recipe forced overrides (highest priority)
  — Recipes with ForceOverrides enabled
  — Override everything (use sparingly)
  — Controlled by: Koan:Recipes:<RecipeName>:ForceOverrides = true
```

### 3.4 Capability Gating

`ShouldApply()` can check for required services or capabilities:

```
public bool ShouldApply(IConfiguration cfg, IHostEnvironment env)
{
    // Only apply if distributed cache is registered
    return services.Any(d => d.ServiceType == typeof(IDistributedCache));
}
```

Recipes that fail the gate are skipped silently (logged at Debug level). This prevents activation failures when optional dependencies are missing.

### 3.5 RecipeApplier Orchestration

```
RecipeApplier.Apply(services, cfg, env):
  1. Discover recipes from RecipeRegistry + assembly attributes
  2. Sort by Order (ascending)
  3. For each recipe:
     a. Check ShouldApply(cfg, env)
     b. If true: call Apply(services, cfg, env)
     c. Log decision with stable EventId:
        - 41000: Recipe Applied
        - 41001: Recipe Skipped (gate failed)
        - 41002: Recipe Skipped (not in Active list)
        - 41003: Recipe Failed (exception during Apply)
        - 41004: Recipe DryRun (would apply, but DryRun mode)
        - 41005: Recipe DryRun Skip (would skip)
```

### 3.6 Dry-Run Mode

```
KoanRecipeOptions:
  Active: string[] = []            — filter to specific recipes
  AllowOverrides: bool = false     — enable forced overrides
  DryRun: bool = false             — log decisions without executing
```

When `DryRun = true`, the applier calls `ShouldApply()` but does not call `Apply()`. All decisions are logged with DryRun-specific event IDs. This allows developers to verify what recipes would be applied before enabling them.

### 3.7 AI Recipe Provider Integration

The recipe pattern extends to AI model selection:
```
Koan:Ai:ActiveRecipe: production-balanced
Koan:Ai:Recipes:production-balanced:
  Chat: qwen3.5:9b
  Embed: nomic-embed-text
  Vision: llava:13b
```

AI recipes participate in the category-scoped resolution chain (described in a related disclosure), providing named model configurations that can be switched by changing a single configuration value.

---

## 4. Claims-Style Disclosure

1. A recipe-based bootstrap system for application frameworks wherein cross-cutting concerns (observability, caching, resilience) are packaged as composable `IKoanRecipe` implementations that are automatically discovered via assembly-level attributes when their package is referenced, distinct from explicit DI registration in that no method call is required in application startup code.

2. An explicit five-layer configuration precedence model (provider defaults < recipe defaults < appsettings < code overrides < recipe forced overrides) that governs how recipe-applied configuration interacts with other configuration sources, distinct from Spring Boot's implicit ordering in that the precedence chain is documented, deterministic, and visible.

3. A capability gating mechanism via `ShouldApply()` wherein recipes check for required services or capabilities before activation, silently skipping when dependencies are absent, distinct from conditional compilation or feature flags in that the gate operates at DI service registration time.

4. A dry-run mode for recipe validation that logs all apply/skip decisions with stable event IDs without executing recipe logic, enabling pre-deployment verification of recipe behavior.

5. Structured logging of recipe decisions with stable event IDs (41000-41005) covering Applied, Skipped (gate), Skipped (filter), Failed, DryRun Apply, and DryRun Skip states, enabling automated monitoring of recipe behavior across deployments.

6. An AI recipe provider pattern that extends the recipe concept to AI model selection, providing named model configurations per AI category (Chat, Embed, Vision) that participate in the ambient category-scoped resolution chain.

---

## 5. Implementation Evidence

- **Interface:** `IKoanRecipe` in `src/Koan.Recipe.Abstractions/IKoanRecipe.cs`
- **Attribute:** `KoanRecipeAttribute` in `src/Koan.Recipe.Abstractions/KoanRecipeAttribute.cs`
- **Options:** `KoanRecipeOptions` in `src/Koan.Recipe.Abstractions/KoanRecipeOptions.cs`
- **Applier:** `RecipeApplier` in `src/Koan.Recipe.Abstractions/RecipeApplier.cs`
- **Registry:** `RecipeRegistry` in `src/Koan.Recipe.Abstractions/RecipeRegistry.cs`
- **Logging:** `RecipeLog` in `src/Koan.Recipe.Abstractions/RecipeLog.cs`
- **AI Recipe:** `AiRecipeProvider` in `src/Koan.AI/Pipeline/AiRecipeProvider.cs`
- **ADR:** `docs/decisions/ARCH-0046-recipes-intention-driven-bootstrap-and-layered-config.md`
- **Framework Version:** Koan Framework v0.6.3

---

## 6. Publication Notice

This document is published as a defensive disclosure to establish prior art. The inventor(s) dedicate this disclosure to the public domain and assert no patent rights over the described inventions. All rights to use, implement, and build upon these inventions are hereby granted to the public.

---

## Antagonist Review Log

### Pass 1
**Antagonist:** This is Spring Boot auto-configuration ported to .NET. The `ShouldApply()` method is equivalent to `@ConditionalOnClass`.

**Author revision:** Spring Boot auto-configuration is the closest prior art. Key differences: (1) Spring has no explicit 5-layer configuration precedence — Spring's property source ordering is implicit and environment-dependent. (2) Spring has no dry-run mode. (3) Spring has no stable event IDs for decision logging. (4) Spring conditions are annotation-based (compile-time); Koan's `ShouldApply()` can inspect the runtime service collection. (5) Spring has no forced-override layer. Strengthened the Spring comparison with specific gap analysis.

### Pass 2
**Antagonist:** No further objections. The configuration precedence model, dry-run mode, and service-collection-aware gating are sufficiently distinct from Spring Boot's auto-configuration.

### Final Status
✅ CLEARED — Antagonist found no further weaknesses. Safe to publish.

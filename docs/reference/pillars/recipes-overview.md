---
title: Recipes — intention-driven bootstrap bundles
description: How to create and use Koan Recipes for health/telemetry/reliability wiring with predictable configuration layering.
---

# Recipes — intention-driven bootstrap bundles

This page explains how to create and use Recipes: small, composable packages that apply best-practice operational wiring (health checks, telemetry, reliability, workers) on top of referenced Koan modules.

Lead contract
- Inputs: referenced modules, configuration (appsettings/env), optional recipe selection (code or config).
- Outputs: DI registrations for health/OTEL/reliability/workers; predictable, layered options configuration.
- Error modes: capability missing (skip with log), validation failures (fail-fast with actionable message), override conflicts (warn and last-wins).
- Success criteria: startup logs show discovered/applied recipes; health endpoints and telemetry active; options layering follows documented precedence.

## Activation — three ways

1) Reference = intent (zero code)
- Add a package named `Koan.Recipe.*` (or `<Org>.Koan.Recipe.*`). The recipe self-registers.
- Default behavior: all discovered recipes apply unless filtered by configuration.

2) Config-only selection
- Set `Koan:Recipes:Active` to a comma-separated list: `"observability,research-institute"`.
- Use `Koan:Recipes:AllowOverrides` to globally allow forced overrides (default false).

3) Explicit code registration
- `services.AddRecipe<ResearchInstituteRecipe>();`
- or `services.AddRecipe("research-institute");`

Notes
- Prefer explicit registration in NativeAOT scenarios. Self-registration uses assembly attributes; no broad assembly scans.

## Layered configuration (precedence)

Order (low → high):
1) Provider defaults
2) Recipe defaults
3) AppSettings / Environment
4) Code overrides (host code)
5) Recipe forced overrides (off by default; requires global + per-recipe flags)

Guidelines
- Recipes should apply defaults conservatively and respect user configuration.
- Forced overrides must log old → new (secrets redacted) with the recipe name and reason.

## Logging and diagnostics

EventIds (as implemented)
- Applying (41000): starting recipe application; includes name and order
- AppliedOk (41001): recipe applied successfully
- SkippedNotActive (41002): recipe not in the active list
- SkippedShouldApplyFalse (41003): recipe’s `ShouldApply` returned false
- DryRun (41004): dry-run mode — would apply but skipped mutations
- ApplyFailed (41005): recipe threw during `Apply`; processing continues

Notes
- Logs are emitted during bootstrap using the framework logger if available; otherwise a safe null logger is used to avoid building a provider at this stage.
- Dry-run: set `Koan:Recipes:DryRun=true` to log decisions without mutating DI (dev-only).

## Capability gating

Recipes should only apply wiring when the required capabilities exist. Prefer checking registered services or options configuration over raw config keys.

Helpers (available to recipes)
- `services.ServiceExists<TService>()` — true if a service type is already registered
- `services.OptionsConfigured<TOptions>()` — true if options have any configure/post-configure actions bound

Example
```csharp
// Within IKoanRecipe.Apply
if (services.ServiceExists<IMongoClient>() || services.OptionsConfigured<MongoOptions>())
{
  // Add health checks or OTEL enrichers for Mongo
}
```

## Minimal usage example

Project file
- Reference your providers and a recipe package:
  - `Koan.Data.Mongo`, `Koan.Web.Diagnostics`, `Koan.Recipe.Observability`

Program.cs
// Reference-only activation (no code needed beyond AddKoan)
builder.Services.AddKoan();

appsettings.json
{
  "Koan": {
    "Data": { "Mongo": { "ConnectionString": "mongodb://localhost:27017", "Database": "app" } },
    "Recipes": { "Active": "observability" }
  }
}

Outcome
- Health checks and OTEL are registered; Mongo traces appear; /health shows Mongo liveness.

## Typical recipe structure (authoring)

Contract
public interface IKoanRecipe {
  string Name { get; }
  int Order { get; }
  bool ShouldApply(IConfiguration cfg, IHostEnvironment env);
  void Apply(IServiceCollection services, IConfiguration cfg, IHostEnvironment env);
}

Authoring checklist
- Namespace: `Koan.Recipe.<Name>`.
- Single public recipe class per package.
- Apply health checks, telemetry, reliability policies; no inline endpoints.
- Use options layering helpers (see below) to set defaults without clobbering user config.

## Options layering helpers (recommended)

Use Koan.Core options helpers to express intent without memorizing Configure/PostConfigure ordering:
- WithProviderDefaults(Action<TOptions>)
- WithRecipeDefaults(Action<TOptions>)
- BindFromConfiguration(IConfigurationSection)
- WithCodeOverrides(Action<TOptions>)
- WithRecipeForcedOverrides(Action<TOptions>) // only when overrides are allowed and enabled

Example (MongoOptions)
services.AddOptions<MongoOptions>()
  .WithProviderDefaults(o => o.TimeoutMs ??= 3000)
  .WithRecipeDefaults(o => o.MaxConnections ??= 100)
  .BindFromConfiguration(cfg.GetSection("Koan:Data:Mongo"))
  .WithCodeOverrides(o => o.Database ??= "app")
  .WithRecipeForcedOverrides(o => o.TimeoutMs = Math.Min(o.TimeoutMs ?? 3000, 2000));

Force overrides (with gating)
```csharp
services.AddOptions<MongoOptions>()
  // ... provider + recipe defaults + bind + code overrides ...
  .WithRecipeForcedOverridesIfEnabled(cfg, recipeName: "observability",
    o => o.TimeoutMs = Math.Min(o.TimeoutMs ?? 3000, 2000));
```

## Advanced composition

Multiple recipes
- Compose via `Order`; last writer wins. Emit Conflict(1004) when a later recipe changes a previously set value.

Forced overrides
- Global gate: `Koan:Recipes:AllowOverrides=true`
- Per-recipe: `Koan:Recipes:<RecipeName>:ForceOverrides=true`
- Apply only via `WithRecipeForcedOverridesIfEnabled`; emit your own warn/info logs if you change values (avoid leaking secrets).

Development troubleshooting
- Set `Koan:Recipes:DryRun=true` to preview which recipes would apply and why.
- Optional dev-only endpoint can dump the list of applied recipes and option sources (no secrets).

## Full example — “research-institute” bundle

Scenario
- App uses Mongo + RabbitMQ + Weaviate + AI provider. The org wants standard health/OTEL/retry/worker wiring.

Activation
// Code (optional):
builder.Services.AddKoan().AddRecipe("research-institute");

// Or config-only:
// "Koan:Recipes:Active": "research-institute"

What the recipe does (Apply)
- HealthChecks: Mongo, RabbitMQ, Weaviate, AI endpoint.
- OTEL: enable outgoing traces for DB/bus/vector/AI.
- Reliability: HTTP client retries + circuit breaker for AI calls.
- Workers: start EmbedderWorker and ProjectionWorker when corresponding modules are present.

Options layering inside Apply
- Sets conservative defaults using `WithRecipeDefaults`.
- Binds `appsettings.*`.
- Only if both global and per-recipe override flags are true, applies `WithRecipeForcedOverrides` to cap timeouts and enforce minimal resilience settings.

## Validation

Recipes may register `IValidateOptions<T>` for invariants (e.g., endpoint required in Production). Fail-fast with a clear message and a link to documentation.

## FAQ

Q: Do recipes replace module references?
A: No. Referencing providers still activates them. Recipes enrich bootstrap with cross-cutting wiring.

Q: Is reflection used for discovery?
A: Prefer assembly-level registration or explicit `AddRecipe<T>()`. No broad AppDomain scans to remain AOT-friendly.

Q: What about secrets?
A: Recipes never hardcode secrets; all values come from options bound from configuration or environment.

## References

- ARCH-0046 — Recipes: intention-driven bootstrap and layered config
- ARCH-0040 — Config and constants naming
- WEB-0035 — Controller-only routes

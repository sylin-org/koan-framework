---
uid: reference.modules.koan.orchestration.generators
title: Koan.Orchestration.Generators – Technical Reference
description: Roslyn analyzers and source generators that materialize Koan orchestration manifests and diagnostics.
since: 0.6.3
packages: [Sylin.Koan.Orchestration.Generators]
source: src/Koan.Orchestration.Generators/
validation:
  date: 2025-09-29
  status: verified
---

## Contract

- Provide Roslyn tooling that inspects Koan orchestration adapters at compile time and emits the canonical `__KoanOrchestrationManifest` JSON blob consumed by planners and the CLI.
- Surface diagnostics (`Koan0049*`) enforcing `ARCH-0049` rules around `KoanServiceAttribute`, short codes, container metadata, and manifest slots.
- Aggregate optional assembly-level attributes (auth providers, manifest overrides) and app metadata to keep runtime discovery fast and reflection-free.
- Fail safely—generator exceptions are swallowed to avoid breaking builds; consumers still compile even when manifest output is skipped.

## Pipeline overview

1. **Analyzer input** – The generator scans the compilation for:
   - Classes annotated with `KoanServiceAttribute`, `ServiceIdAttribute`, `ContainerDefaultsAttribute`, `EndpointDefaultsAttribute`, `HealthEndpointDefaultsAttribute`, and `AppEnvDefaultsAttribute`.
   - Assembly-level attributes `AuthProviderDescriptorAttribute` and `OrchestrationServiceManifestAttribute`.
   - Classes implementing `IKoanManifest` or decorated with `KoanAppAttribute` (used to emit app metadata).
2. **Diagnostics** – As symbols are processed, the generator reports diagnostics when rules are violated:
   - `Koan0049A` – `[KoanService]` must live on a class implementing `IServiceAdapter`.
   - `Koan0049B` – Invalid short code (length/character constraints).
   - `Koan0049C` – Reserved short code identifiers.
   - `Koan0049D` – Poorly formatted `qualifiedCode` values.
   - `Koan0049E` – Missing container image when `DeploymentKind` is `Container`.
   - `Koan0049F` – Warn on `latest` default tags.
   - `Koan0049G` – Duplicate short codes within a compilation.
3. **Candidate capture** – For each service, the generator captures:
   - Container image + tag, default ports, env variables, volumes, app environment, and capabilities.
   - Endpoint defaults (container vs local), health metadata, service kind/type, descriptive fields, provides/consumes, and version overrides.
   - Persisted data is normalized (e.g., first port used when endpoint port missing).
4. **Manifest synthesis** – Service, app, and auth provider data feeds into a JSON structure:
   - `schemaVersion` (currently `1`).
   - Optional `app` section (code, name, default public port, description).
   - `authProviders` array when `AuthProviderDescriptorAttribute` is present.
   - `services` array with both legacy fields (`id`, `image`, `ports`, `env`, `type`) and ARCH-0049 unified fields (`shortCode`, `containerImage`, `defaultTag`, `kind`, `capabilities`, etc.).
5. **Generated output** – JSON is embedded in `__KoanOrchestrationManifest.g.cs` under the `Koan.Orchestration` namespace:
   ```csharp
   public static class __KoanOrchestrationManifest
   {
       public const string Json = "{ ... }";
   }
   ```
   Planner/discovery code reads this constant via `ProjectDependencyAnalyzer` to avoid reflection.

## App metadata & auth providers

- `KoanAppAttribute` and `IKoanManifest` implementations produce a minimal app entry (shortCode, name, default port, capabilities) so planners treat the primary app as declared.
- Assembly-level `AuthProviderDescriptorAttribute` values are surfaced through `ManifestAuthProviders`, allowing the CLI to list available authentication providers during `inspect`.

## Error resilience

- All reflection and symbol analysis is wrapped with `try/catch`; diagnostics are best-effort.
- If the generator cannot parse JSON or encounters an unrecoverable condition, the manifest is omitted rather than breaking compilation.
- When container metadata is missing, diagnostics still fire to steer authors; planners handle the absence gracefully at runtime.

## Validation notes

- Reviewed `OrchestrationManifestGenerator.cs` on 2025-09-29, including diagnostic definitions, short-code validation, manifest builder, and escape helpers.
- Confirmed that duplicate short codes, reserved identifiers, and invalid qualified codes produce diagnostics in sample projects.
- Verified generated manifest structure by inspecting `obj/<tfm>/__KoanOrchestrationManifest.g.cs` after a `dotnet build`.
- Doc build (`docs:build`) executed after documentation updates; no new warnings introduced for this module.

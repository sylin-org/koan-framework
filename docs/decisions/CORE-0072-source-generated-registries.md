# CORE-0072: Source-generated registries for bootstrap and service discovery

> **Superseded in part by [ARCH-0116](ARCH-0116-one-module-lifecycle.md) (2026-07-17).** Source
> generation remains, but initializer and auto-registrar catalogs were removed; module activation is
> represented only by semantic module descriptors.

Status: Accepted  
Date: 2025-11-12

## Contract

- Inputs: concrete types that implement `IKoanInitializer`, `IKoanAutoRegistrar`, `IKoanBackgroundService`, or `IServiceDiscoveryAdapter` within Koan assemblies.
- Outputs: generated module initializers that populate `KoanRegistry` descriptor tables before bootstrap runs.
- Error Modes: generator emits diagnostics when a candidate type is abstract, generic without concrete arguments, or violates interface requirements; runtime falls back to no-op if a registry is empty.
- Success Criteria: eliminate reflection-based assembly scans, ensure deterministic startup ordering, preserve "reference = intent" behavior under NativeAOT trimming.

## Context

Reflection-heavy discovery (assembly scans + `Activator.CreateInstance`) powered bootstrap and service registration across Koan. This approach destabilized NativeAOT (linker could not see usage), delayed failure signals, and made startup dependent on runtime assembly load order. The `AssemblyCache` helper only mitigated repeated scans; it did not solve determinism or trimming. We observed repeated trim warnings and native crashes when assemblies were not eagerly loaded.

## Decision

- Introduce `KoanRegistry`, a thread-safe central registry that stores manifest descriptors for initializers, auto-registrars, background services (with attribute metadata), and service discovery adapters.
- Add the `Koan.Core.Registry.Generators` project implementing an incremental Roslyn generator (`RegistrySourceGenerator`). For each assembly it:
  - Finds eligible types via semantic analysis.
  - Captures attribute metadata (`KoanBackgroundServiceAttribute`) into lightweight descriptor records.
  - Emits a module initializer that registers descriptors with `KoanRegistry`.
- Update core runtime paths to consume the registry:
  - `AppBootstrapper` still loads referenced assemblies (ensuring module initializers fire) but instantiates initializers from `KoanRegistry` instead of scanning types.
  - `AppRuntime` collects provenance by instantiating auto-registrars from the registry.
  - `KoanBackgroundServiceAutoRegistrar` and `ServiceDiscoveryAutoRegistrar` operate on descriptor data rather than re-enumerating assemblies.
  - `Koan.Data.AI` relies on generated manifests to populate `EmbeddingRegistry`, removing runtime scans for `[Embedding]` entities.
- Wire the generator as an analyzer for all projects via `Directory.Build.props` and include it in `Koan.sln` for CI builds.

### Edge Cases

- Assemblies must still load to trigger module initializers; `AppBootstrapper` retains referenced assembly loading to uphold "reference = intent" even when no direct type is used.
- Missing generator output (e.g., project not referencing Koan.Core) simply yields empty registries; runtime logic guards on empty arrays.
- Background services with `Enabled = false` are recorded but skipped based on environment toggles.
- Generator deduplicates entries per assembly; duplicate partial classes do not register twice.
- If a future type forgets to implement the expected interface, it will not appear in the registry; tests should validate expected descriptors.

## Consequences

- Startup cost and NativeAOT trimming warnings drop significantly—no runtime reflection scans over every assembly.
- Deterministic registries enable compile-time validation and diagnostics when metadata is inconsistent.
- `KoanRegistry` provides a reusable pattern for other discovery surfaces (controllers, transformers, MCP entities) to migrate away from reflection.
- Background service registration now carries attribute metadata without reflection, unlocking simpler unit tests and future options binding.

## Follow-ups

1. Migrate remaining reflection consumers (`Koan.Data.AI` embeddings, transformer discovery, MCP registries, JSON adapters) onto the registry generator.
2. Add unit tests covering generator output snapshots and `KoanRegistry.ResetForTesting()` helpers.
3. Remove `AssemblyCache` once all reflection paths are retired; update trimming descriptors and docs accordingly.
4. Extend the generator to emit richer metadata (e.g., periodic intervals, startup ordering) when downstream registrars need it.

## References

- `src/Koan.Core/Hosting/Registry/KoanRegistry.cs`
- `src/Koan.Core.Registry.Generators/RegistrySourceGenerator.cs`
- `src/Koan.Core/Hosting/Bootstrap/AppBootstrapper.cs`
- `src/Koan.Core/Orchestration/ServiceDiscoveryAutoRegistrar.cs`
- `src/Koan.Core/BackgroundServices/KoanBackgroundServiceAutoRegistrar.cs`

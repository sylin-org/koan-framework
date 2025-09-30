---
id: ARCH-0057
slug: koanlog-facade-and-stage-logging
domain: Architecture
status: approved
date: 2025-10-05
title: KoanLog façade and stage logging centralization
---

## Contract

- **Inputs**: Koan.Core logging formatter pipeline, adapters and registrars relying on stage-aware logs, DI bootstrap that wires logging for host applications.
- **Outputs**: A single KoanLog façade that captures the ambient `ILoggerFactory`, exposes reusable scopes, and removes per-adapter logging shims while preserving stage metadata.
- **Error Modes**: Factory not attached (no logs emitted), duplicate service-provider creation to obtain loggers, stage/action drift per adapter, regressions in formatter tokens when null loggers are used.
- **Success Criteria**: `KoanLog.For<T>()` delivers ready-to-use scopes in adapters/registrars, zero `BuildServiceProvider()` logger grabs remain, stage tokens render identically to earlier helpers, tests/build continue green.

## Context

Early adapter refactors added convenience helpers (for example, `LogConfigInfo`) to wrapper classes so stage-aware logging was available without repeating formatter calls. While this simplified individual files, it violated Koan's separation of concerns: each adapter owned bespoke helpers and often spun temporary service providers merely to fetch a logger factory. The result was boilerplate and avoidable allocations during boot.

Koan already ships a stage-aware formatter (`KoanLogStageLoggerExtensions`) and a centralized static helper (`KoanLog`). By elevating KoanLog into a façade that attaches to the runtime `ILoggerFactory`, adapters can obtain reusable scopes without building transient containers. This keeps DX consistent, reduces duplication, and respects the "Reference = Intent" principle for modules that simply reference Koan.Core.

## Decision

**APPROVED**: Promote KoanLog to a first-class façade that captures the ambient logger factory, provides scoped stage helpers, and eliminates per-adapter logging shims. Modules consume logging via `KoanLog.For<T>()` (or category-based scopes) and log actions/outcomes with existing stage helpers.

### Key Elements

- `KoanLogFactoryBridge` is registered once in `AddKoanCore()` as a hosted service. On host start it binds the runtime `ILoggerFactory` to the static KoanLog façade and detaches during shutdown.
- `KoanLogScope` encapsulates category resolution and lazy logger creation. Stage-specific methods (`BootInfo`, `ConfigWarning`, etc.) are available without passing `ILogger` manually.
- Adapters and registrars retain their domain-specific action constants but no longer build temporary service providers just to emit boot logs.
- The façade remains test-friendly via `KoanLog.AttachFactory`/`DetachFactory`, so integration tests can inject custom factories when running outside a full host.

## Implementation Guidelines

1. Register `KoanLogFactoryBridge` inside `AddKoanCore()` and ensure it is added via `TryAddEnumerable` so multiple registrations co-exist cleanly.
2. Extend `KoanLog` with factory attachment helpers, `KoanLogScope`, and fallbacks when the factory is not yet available.
3. Refactor existing registrars/adapters to declare a static scope (`private static readonly KoanLogScope Log = KoanLog.For<MyType>();`) and replace ad-hoc logger creation.
4. Remove helper shims or temporary `ILoggerFactory` retrievals from adapter base classes, keeping code DRY and focused on configuration concerns.
5. Validate via `dotnet build` and the strict docs build to confirm no formatter regressions and documentation references stay current.

## Edge Cases

1. **Hostless execution**: When tests run without a host, `KoanLogScope` resolves to `null` until `AttachFactory` is called explicitly. The façade must no-op gracefully.
2. **Multiple hosts**: Only the most recent factory should remain attached; the bridge uses `Interlocked` exchange to prevent cross-host contamination.
3. **Early boot logging**: Scopes may be invoked before the host starts. Logs simply skip emission until the factory is available—no throw or provider construction.
4. **Shutdown races**: `DetachFactory` compares the captured factory instance before clearing it, ensuring parallel hosts do not detach each other's factories.
5. **Formatter invariants**: Stage, action, and outcome tokens must match prior output so downstream log processing continues to function.

## Sample Usage

```csharp
using Koan.Core.Logging;
using KoanLogScope = Koan.Core.Logging.KoanLog.KoanLogScope;

internal sealed class VectorModuleRegistrar
{
    private static readonly KoanLogScope Log = KoanLog.For<VectorModuleRegistrar>();

    public void Initialize(IServiceCollection services)
    {
        Log.BootDebug("registrar.init", "loaded", ("module", "Koan.Data.Vector"));
        services.AddKoanDataVector();
        Log.BootDebug("registrar.init", "services-registered", ("module", "Koan.Data.Vector"));
    }
}
```

## Consequences

- **Positive**: Consistent stage logging across modules, reduced DI churn, and an obvious entry point for developers adding instrumentation.
- **Neutral**: Host startup introduces a no-op hosted service solely to attach the logger factory; runtime overhead is negligible.
- **Risks**: Forgetting to call `KoanLog.For<>()` (or leaving legacy helpers) reintroduces duplication—linting and reviews must enforce the pattern.

## References

- [ARCH-0011 – Logging and headers layering](./ARCH-0011-logging-and-headers-layering.md)
- [ARCH-0044 – Standardized module config and discovery](./ARCH-0044-standardized-module-config-and-discovery.md)
- Koan.Core logging formatter (`KoanLogStageLoggerExtensions`) implementation in source.

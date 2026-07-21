---
name: koan-debugging
description: Diagnose Koan startup, provider election, health, runtime facts, composition-lock drift, configuration rejection, and Entity query failures from the framework's corrective evidence
pillar: core
status: current
last_validated: 2026-07-19
---

# Koan debugging

## Trigger this skill when you see

- startup or module-composition failure
- an unexpected or unavailable Data, Cache, Storage, AI, or Communication provider
- `/health/ready`, `/.well-known/Koan/facts`, or `koan://facts`
- `koan.lock.json` drift
- a malformed/unknown filter, unsupported provider capability, or ambiguous Entity model
- a request to guess at registration order or silently fall back from configured intent

## Core principle

**Read Koan's compiled decision before changing application code.** Startup reporting, health, runtime
facts, and the composition lock are projections of the same host-owned plan. A configured requirement
must resolve or reject with its owner and correction; do not hide the failure by adding registration,
catching the exception, or selecting a weaker provider.

<!-- validate -->
```csharp
using Koan.Core;
using Koan.Data.Core.Model;
using Microsoft.Extensions.Logging;

public sealed class Todo : Entity<Todo>
{
    public string Title { get; set; } = "";
}

public static class AppDiagnostics
{
    public static void RecordEnvironment(ILogger logger) =>
        logger.LogInformation(
            "Koan environment {Environment}; container={Container}",
            KoanEnv.EnvironmentName,
            KoanEnv.InContainer);
}
```

## Inspect in this order

1. Read the first corrective startup exception in full.
2. Read the startup report for referenced modules, provider candidates, election reasons, and rejected
   configuration.
3. Check `/health/live` for process liveness and `/health/ready` for required dependency readiness.
4. Read `/.well-known/Koan/facts` or `koan://facts` for the same redacted machine-readable decisions.
5. Compare `koan.lock.json` when the loaded module set differs from the project references you expect.
6. Check the generated product surface before treating an available provider as supported or assuming
   backend parity.

## Common corrections

| Symptom | Meaning | Correction |
|---|---|---|
| Referenced capability absent from facts | The package is not in the active direct/bundle composition graph | Add the intended `PackageReference`, rebuild, and inspect the lock/facts again. |
| Explicit provider is unavailable | Configured intent cannot be satisfied | Supply the named endpoint/credential or remove the explicit intent; do not add silent fallback. |
| Default provider surprises you | More than one eligible provider is available or priority changed | State stable business intent with the pillar's standard source/provider configuration or Entity annotation. |
| Readiness is unhealthy while liveness is healthy | The process runs but a required elected dependency is unavailable | Repair the dependency named by readiness/facts. |
| HTTP filter returns 400 | The filter is malformed or names no portable Entity property | Correct the JSON filter/property name; Koan never drops the predicate. |
| Entity activation rejects `Name` and `name` | Public properties collide under portable case-insensitive identity | Rename one property before the adapter is created. |
| MCP tool is missing | The Entity/tool is not declared, visible, or admitted by the current access map | Inspect `koan://entities`, `koan://self`, and the access explanation before adding code. |
| `koan.lock.json` changed unexpectedly | Referenced module composition changed | Review the project/package delta; regenerate only when the new composition is intentional. |

## Anti-patterns to flag

| If you see | Suggest |
|---|---|
| Manual module or controller registration added after `AddKoan()` | Fix the missing reference/module contribution that the report identifies. |
| Catching startup/configuration rejection and continuing | Preserve fail-loud behavior and correct the named intent. |
| Switching to the local provider to make a remote test pass | Repair the intended provider or make the test explicitly local. |
| Logging connection strings, tokens, or exception objects verbatim | Use Koan's redacted facts/logging path and log bounded identifiers only. |
| Debugging from package presence alone | Read generated maturity, active composition, election, and readiness separately. |
| Running the full release ratchet for one local defect | Start with the smallest owner suite and affected package build. |

## Escape hatches

- Use standard .NET logging categories and OpenTelemetry configuration for deeper application traces.
- Use provider-owned direct diagnostics only after Koan facts identify the responsible adapter.
- Use an explicit local profile in tests when local infrastructure is the actual test intent.
- Inspect a package-only candidate in a temporary consumer when source-project references could mask a
  packaging defect.

## See also

- [Troubleshooting](../../../docs/support/troubleshooting.md)
- [Core runtime reference](../../../docs/reference/core/index.md)
- [Data adapter diagnostics](../../../docs/reference/data/adapter-diagnostics.md)
- [Composition lockfile](../../../docs/guides/composition-lockfile.md)
- [Product surface](../../../docs/reference/product-surface.md)
- [FirstUse](../../../samples/FirstUse/README.md)

---
type: REFERENCE
domain: operations
title: "Testing and operations"
audience: [developers, operators, support-engineers, ai-agents]
status: current
last_updated: 2026-07-22
framework_version: v0.20.0
validation:
  date_last_tested: 2026-07-22
  status: verified
  scope: application conformance, health, facts, diagnostics, and troubleshooting
---

# Testing and operations

Use this pillar to prove one application guarantee, inspect the runtime decision that implements it,
and correct failures without reconstructing composition from logs or source code.

## Test application behavior

Reference `Sylin.Koan.Testing` and express a valid Entity fixture:

```csharp
public sealed class TodoConformance : EntityConformanceSpecs<Todo>
{
    protected override Todo NewValid() => new() { Title = "A valid business example" };
}
```

Koan owns the real host, isolated test state, and Entity binding. Capability traits skip only when the
provider does not declare that behavior; a skip is not certification.

## Inspect the running application

| Question | Surface |
|---|---|
| Is the process alive? | `/health/live` |
| Are selected dependencies ready? | `/health/ready` |
| What modules and providers were resolved? | startup report and `/.well-known/Koan/facts` |
| What can an MCP client see? | `koan://facts`, `koan://entities`, `koan://self` |
| Did referenced composition drift? | `koan.lock.json` |

These surfaces project the same runtime decisions. They do not independently select providers or
change configuration.

## Correct failures

Start with the named intent, provider, capability, and corrective action in the failure. Check
participation-aware readiness and redacted facts before adding configuration or registration code.
An available but unused provider may remain non-critical; an elected or first-used provider becomes
part of readiness according to its capability contract.

## Deeper contracts

- [Testing an application](../../guides/testing-your-app.md)
- [Composition lockfile](../../guides/composition-lockfile.md)
- [Troubleshooting](../../support/troubleshooting.md)
- [Optional OpenTelemetry integration](observability.md)
- [External infrastructure boundary](external-topology.md)
- [NativeAOT deployment boundary](native-aot.md)
- [Product and package surface](../product-surface.md)

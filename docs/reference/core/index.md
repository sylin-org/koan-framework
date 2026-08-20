---
type: REF
domain: core
title: "Compose and inspect an application"
audience: [developers, architects, ai-agents]
status: current
last_updated: 2026-07-17
framework_version: v0.20.0
validation:
  date_last_tested: 2026-07-17
  status: verified
  scope: AddKoan, KoanModule lifecycle, compiled composition, runtime facts, and health projection
---

# Compose and inspect an application

Use this pillar to understand what `AddKoan()` composes, how referenced packages become eligible,
and where to inspect the provider and configuration decisions that result.

## Contract

`AddKoan()` is the complete default bootstrap. It compiles the generated module constitution for the
application's referenced Koan assemblies, registers Core once, lets each retained module register
services, applies host-owned declarations, and freezes one semantic composition.

```csharp
var builder = WebApplication.CreateBuilder(args);
builder.Services.AddKoan();
var app = builder.Build();
await app.RunAsync();
```

Use the declaration overload only for application-owned semantic declarations that must participate
in the same composition:

```csharp
builder.Services.AddKoan(() =>
{
    Order.Lifecycle.BeforeUpsert(order => order.Validate());
});
```

Calling the declaration overload after composition is frozen rejects with a corrective error.

## Module authoring

A functional Koan assembly contributes one domain-named `KoanModule` only when it owns registration,
one-time startup work, or reporting:

```csharp
public sealed class BillingModule : KoanModule
{
    public override void Register(IServiceCollection services)
        => services.AddSingleton<InvoicePolicy>();

    public override Task Start(IServiceProvider services, CancellationToken ct)
        => Task.CompletedTask;
}
```

Module identity derives from ordinary package/assembly identity. Do not add a parallel Koan ID or
descriptor attribute. Shared cross-module vocabulary belongs in an isolated Core, Abstractions, or
Contracts assembly; referencing contracts must not activate functionality.

`Register` owns DI composition. `Start` owns ordered one-time startup after DI is available. `Report`
and `ReportComposition` project resolved facts; applications do not call them. Recurring or pokable
work belongs to the background-service or Jobs contracts, not `Start`.

## Environment posture

A module that gates behavior on the environment names the decision it is making. `KoanEnv.IsDevelopment`
and `IsProduction` stay available and are the right read for diagnostics — log level, banner detail —
but they are the wrong tool for deciding whether a capability composes, because that has a law.

**A convenience that is safe everywhere but risky in Production** — schema auto-creation, endpoint
auto-discovery, a local key file — declares itself and lets the gate apply the law:

```csharp
KoanEnv.Gate.Enforce(new KoanMagic(
    Capability: "automatic schema creation",
    Risk: "Koan issues CREATE and ALTER against whatever database the connection string resolves to.",
    Remedy: "provision the schema out of band, or set AllowProductionDdl on the source",
    Consent: options.AllowProductionDdl), environment, logger);
```

Production is the gate, not Development. The capability runs from a bare reference in Development,
Staging, Test, and CI — warning outside Development — and in Production asks for consent rather than
disappearing. Consent is the capability's own option **or** `Koan:AllowMagicInProduction`. A refusal
names the capability, the risk, the remedy, and the flag, so an operator can act on it without reading
source. Use `Gate.Announce` instead of `Enforce` when skipping is a coherent outcome and refusing the
host would be wrong.

**A surface that must not exist outside Development** — an admin console, a dev token endpoint, seeded
credentials — uses the other gate:

```csharp
if (!KoanEnv.Gate.DevelopmentOnly(environment)) return;
```

It takes no consent parameter and no flag unlocks it, deliberately: a convenience flag that also
switched on seeded credentials would be an authentication bypass. Whatever it guards should be absent
from the production service graph, not merely unreachable.

**An unconfigured default** uses `KoanEnv.Gate.LooksDeployed(environment)` — Production, or anything in
a container. It picks what a setting should be when nobody said; it is a heuristic and never decides
whether a request is authorized.

See [ARCH-0128](../../decisions/ARCH-0128-environment-posture-is-a-named-decision.md).

## Composition and provider decisions

Core compiles structural contributions once per host shape. Pillars then own semantic policy and
immutable plans; adapters own mechanics. Runtime operations consume those plans without rediscovering
contributors or renegotiating providers on every call.

Reference means availability, not universal activation. A pillar may ship a low-priority local
provider; a referenced eligible provider can supersede it. Explicit configured intent wins or fails
with a correction. It does not silently fall back to an incompatible guarantee.

## Explanation surfaces

One host-owned runtime-facts envelope feeds:

- startup reporting;
- `/.well-known/Koan/facts`;
- `koan://facts` when MCP is present;
- readiness contributors; and
- composition failure details.

The checked-in `koan.lock.json` records statically referenced composition. Runtime facts add resolved
elections and capability decisions. These are projections of the same composition, not alternate
configuration sources.

## Health

With Koan Web referenced:

- `GET /health/live` reports process liveness without dependency probes.
- `GET /health/ready` reports aggregate readiness and returns `503` when a critical active dependency
  is unhealthy.

An available but unelected optional provider must not make the application unready merely because it
is referenced. Active modules and selected providers own truthful health contributions.

## Support boundary

The current source proves compiled module activation, one retained module lifecycle, ordered startup,
canonical facts, and focused host ownership. It does not make every package in the repository assessed
or every provider production-certified. Use the [product surface](../product-surface.md) for maturity
and [troubleshooting](../../support/troubleshooting.md) for corrective paths.

Module and connector authors can continue to
[adapter registration and external topology](../../architecture/adapter-and-orchestration-registration.md).

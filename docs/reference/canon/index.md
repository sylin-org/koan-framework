---
type: REF
domain: canon
title: "Canon Pillar Reference"
audience: [developers, architects, ai-agents]
status: current
last_updated: 2026-07-17
framework_version: v0.17.0
validation:
  date_last_tested: 2026-07-17
  status: tested
  scope: Canon unit 37/37, integration 6/6, non-Web bootstrap 1/1, CustomerCanon host 1/1
---

# Canon pillar reference

Canon turns messy arrivals into one trusted Entity. Application code owns identity and business rules;
Koan owns discovery, deterministic phase execution, metadata, convergence, persistence, inspection, and
optional HTTP projection.

## Reach for Canon when

- several arrivals may describe the same customer, device, account, or other business identity;
- input needs normalization or validation before it becomes trusted state;
- conflict policy, source attribution, lineage, lifecycle, or readiness must travel with the result;
- later projections or distribution should run as explicit phases.

Use ordinary `Entity<T>` when the application already has one unambiguous source of truth. Canon is not
an event store, workflow engine, message transport, distributed lock, or universal MDM certification.

## Shortest Web path

Reference `Sylin.Koan.Canon.Web`, `Sylin.Koan.Data.Connector.Json`, and the usual Koan Web packages, then:

```csharp
using Koan.Canon;
using Koan.Core;
using Koan.Web.Extensions;

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddKoan().AsWebApi();
var app = builder.Build();
await app.RunAsync();

public sealed class Customer : CanonEntity<Customer>
{
    [AggregationKey]
    public string Email { get; set; } = "";
}
```

The customer is available at `/api/canon/customer`, and `/api/canon/models` explains why. No controller,
registrar, application module, or explicit `AddCanonRuntime()` is required.

## Add a business rule

```csharp
public sealed class NormalizeCustomer : ICanonPipelineContributor<Customer>
{
    public CanonPipelinePhase Phase => CanonPipelinePhase.Validation;

    public ValueTask<CanonizationEvent?> Execute(
        CanonPipelineContext<Customer> context,
        CancellationToken cancellationToken)
    {
        context.Entity.Email = context.Entity.Email.Trim().ToLowerInvariant();
        return ValueTask.FromResult<CanonizationEvent?>(null);
    }
}
```

Koan discovers it from source-generated metadata. Ordering is phase, optional `Order`, then type name.
A contributor returning a failed or parked event terminates before later phases, indexing, or canonical
persistence. Web maps those outcomes to 422 and 202 respectively.

## Package boundaries

| Package | Responsibility |
|---|---|
| `Sylin.Koan.Canon.Contracts` | Inert models, metadata, annotations, results, and extension contracts. |
| `Sylin.Koan.Canon` | Functional activation, discovery, pipeline execution, defaults, persistence, and audit. |
| `Sylin.Koan.Canon.Web` | Optional generated HTTP, model catalog, rebuild, and inspection surfaces. |

Modules that merely describe Canon-aware types may reference Contracts without activating Canon. Any
reference to the functional package makes `AddKoan()` activate the runtime.

## Runtime surfaces

| Surface | Meaning |
|---|---|
| `entity.Canonize(...)` | Canonize one Entity through the active host. |
| `ICanonRuntime.Canonize<T>(...)` | Execute the compiled pipeline directly. |
| `RebuildViews<T>(...)` | Reload a canonical snapshot and run requested projections. |
| `RegisterObserver(...)` | Observe phase boundaries and errors until registration disposal. |
| `Replay(...)` | Read bounded process-local result snapshots. |
| `AddCanonRuntime(...)` | Advanced explicit pipeline/runtime override; unnecessary for normal use. |

The default `ICanonPersistence` uses the selected Koan Data provider for canonical snapshots, stages,
and aggregation indexes. Replacing it is a complete storage decision. The runtime, discovery catalog,
configuration, and defaults are host-owned singletons.

## Operational honesty

- Replay records do not survive restart and are not event sourcing.
- `CanonizationEvent` is a phase result, not Koan Communication transport.
- Canon currently runs in-process; distributed delivery, locking, retry, and recovery need explicit
  application or adapter capability.
- Provider concurrency, transaction, and durability guarantees come from the selected Data/persistence
  implementation.
- Generated admin routes require deployment-appropriate authorization.

## Evidence

- [Contracts source](../../../src/Koan.Canon.Contracts/)
- [Runtime source](../../../src/Koan.Canon/)
- [Web source](../../../src/Koan.Canon.Web/)
- [CustomerCanon golden sample](../../../samples/applications/CustomerCanon/README.md)
- [Canon unit suite](../../../tests/Suites/Canon/Unit/)
- [Canon integration suite](../../../tests/Suites/Canon/Integration/)
- [ARCH-0058 — Canon runtime architecture](../../decisions/ARCH-0058-canon-runtime-architecture.md)

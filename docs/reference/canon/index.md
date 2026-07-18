---
type: REF
domain: canon
title: "Canon Pillar Reference"
audience: [developers, architects, ai-agents]
status: current
last_updated: 2026-07-18
framework_version: source-first
validation:
  date_last_tested: 2026-07-18
  status: tested
  scope: Canon unit 35/35, integration 7/7, CustomerCanon host 1/1
---

# Canon pillar reference

Canon turns imperfect arrivals into one trusted Entity. Application code owns identity and business
rules; Koan owns discovery, deterministic phase execution, metadata, convergence, persistence,
inspection, and optional HTTP projection.

## Reach for Canon when

- several arrivals may describe the same customer, device, account, or other business identity;
- input needs normalization or validation before it becomes trusted state;
- conflict policy, source attribution, lineage, lifecycle, or readiness must travel with the result;
- later projections or distribution should run as explicit phases.

Use ordinary `Entity<T>` when the application already has one unambiguous source of truth. Canon is not
an event store, workflow engine, message transport, distributed lock, or universal MDM certification.

## Shortest Web path

Reference `Sylin.Koan.Canon.Web`, `Sylin.Koan.Data.Connector.Json`, and the usual Koan Web packages:

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
registrar, application module, or Canon-specific registration call is required.

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

Koan discovers the contributor from source-generated metadata. Ordering is phase, optional `Order`,
then type name. The first failed or parked contributor terminates before later contributors, phases,
indexing, audit, or canonical persistence. Web maps those outcomes to 422 and 202.

Models without application contributors still receive the built-in aggregation and policy phases.

## Package boundaries

| Package | Responsibility |
|---|---|
| `Sylin.Koan.Canon` | Canon models and metadata, functional activation, one host composition plan, pipeline execution, persistence, and audit. |
| `Sylin.Koan.Canon.Web` | Optional projection of that plan into Canon-aware Entity routes and model inspection. |

Any reference to Canon activates the functional runtime. Web adds no second domain model or pipeline
authority.

## Runtime surfaces

| Surface | Meaning |
|---|---|
| `entity.Canonize(...)` | Canonize one Entity through the active host. |
| `ICanonRuntime.Canonize<T>(...)` | Execute the compiled pipeline directly. |
| `ICanonRuntime.RebuildViews<T>(...)` | Reload a canonical snapshot and run requested projections from application code. |
| `ICanonPersistence` | Replace canonical, stage, and aggregation-index storage as one decision. |
| `ICanonAuditSink` | Replace audit delivery independently of canonical storage. |
| `ICanonPipelineCatalog` | Read compiled pipeline metadata; Canon Web uses this for inspection. |

The default persistence and audit implementations use the selected Koan Data provider. The runtime,
plan, pipeline catalog, persistence, and audit sink are host-owned singletons.

## Commit and failure contract

After all phases succeed, the default runtime writes in this order:

1. canonical Entity;
2. aggregation indexes;
3. audit entries.

The sequence is not an atomic transaction across all providers. A canonical failure attempts neither
indexes nor audit. An index failure can leave canonical state and a prefix of indexes durable, and
skips audit. An audit failure occurs after canonical state and indexes are durable. The exception names
the failed checkpoint and preserves the provider exception; Canon provides no rollback or blind-retry
safety.

## Operational honesty

- `CanonizationEvent` is a phase result, not Koan Communication transport.
- Stage-only arrivals persist as stages and return parked. Failed and parked pipeline outcomes remain
  non-canonical.
- Canon currently runs in-process; distributed delivery, locking, durable replay, retry, and recovery
  require explicit application or adapter capability.
- Provider concurrency, transaction, and durability guarantees come from the selected persistence
  implementation.
- Canon Web generates model and inspection routes only; it has no admin, replay, rebuild, or value-object
  route family. The host's ordinary ASP.NET authorization policy applies.

## Evidence

- [Runtime source](../../../src/Koan.Canon/)
- [Web source](../../../src/Koan.Canon.Web/)
- [CustomerCanon golden sample](../../../samples/applications/CustomerCanon/README.md)
- [Canon unit suite](../../../tests/Suites/Canon/Unit/)
- [Canon integration suite](../../../tests/Suites/Canon/Integration/)
- [ARCH-0058 — historical Canon runtime architecture](../../decisions/ARCH-0058-canon-runtime-architecture.md)

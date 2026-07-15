# Sylin.Koan.Canon.Domain

Entity-first canonicalization for Koan applications. Canon turns source records into one governed
`CanonEntity<T>` through a deterministic, in-process pipeline while carrying source attribution,
policy evidence, lineage, lifecycle, and readiness state.

- Target framework: .NET 10
- License: Apache-2.0
- Maturity: pre-1.0; implemented and test-owned, not a distributed MDM certification

## Install

```powershell
dotnet add package Sylin.Koan.Canon.Domain
```

## Shortest path

```csharp
using Koan.Canon.Domain.Annotations;
using Koan.Canon.Domain.Model;
using Koan.Canon.Domain.Runtime;
using Koan.Core;

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddKoan();
builder.Services.AddCanonRuntime();
var app = builder.Build();

app.MapPost("/customers", (Customer customer, CancellationToken ct) =>
    customer.Canonize(origin: "customer-api", cancellationToken: ct));

await app.RunAsync();

public sealed class Customer : CanonEntity<Customer>
{
    [AggregationKey]
    public string Email { get; set; } = "";

    public string DisplayName { get; set; } = "";
}
```

Post a customer and the endpoint returns its persisted canonization result. With no configured
pipeline, Canon persists through Koan Data. Configured pipelines run `Intake`, `Validation`,
`Aggregation`, `Policy`, `Projection`, and `Distribution` in order.

## Persistence

The default `ICanonPersistence` uses the active Koan Data provider for canonical reads and writes,
stage writes, and aggregation-index access. A custom persistence implementation owns that complete
boundary; Canon does not bypass it through Entity/Data when loading prior canonical state or rebuilding
views.

Use a custom implementation for event sourcing, CQRS, or another domain store. Implement canonical
read/write, stage write, and index lookup/upsert together. A `null` canonical read means absent;
storage failures propagate.

## Host ownership

- `entity.Canonize(...)` uses the active Koan host after `AddKoan()` and `AddCanonRuntime()` composition.
- Overloads receiving an `IServiceProvider` select that provider for the complete operation and restore
  the caller's previous flow scope afterward.
- Calling `ICanonRuntime` directly with default persistence requires an already active host scope.
- A fully custom persistence implementation can run without Koan Data or an ambient host when the
  audit sink and pipeline contributors are also host-independent. The default audit sink uses Koan Data.

See the
[technical contract](https://github.com/sylin-org/Koan-framework/blob/dev/src/Koan.Canon.Domain/TECHNICAL.md)
for deeper boundaries and the
[current Canon reference](https://github.com/sylin-org/Koan-framework/blob/dev/docs/reference/canon/index.md)
for the public capability inventory.

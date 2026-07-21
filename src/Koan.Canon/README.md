# Sylin.Koan.Canon

Entity-first canonicalization for Koan applications. Referencing the package makes `AddKoan()` discover
every concrete `CanonEntity<T>` and its optional contributors, then compile one host-owned plan.

```powershell
dotnet add package Sylin.Koan.Canon
```

## Meaningful result

```csharp
using Koan.Canon;
using Koan.Core;

var builder = Host.CreateApplicationBuilder(args);
builder.Services.AddKoan();
using var app = builder.Build();
await app.StartAsync();

var result = await new Customer { Email = " Alice@Example.com " }.Canonize();

public sealed class Customer : CanonEntity<Customer>
{
    [AggregationKey]
    public string Email { get; set; } = "";
}

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

Contributors are ordered by phase, optional `Order`, then type name. The first failed or parked event
terminates its phase and the operation. A contributor-free model still receives built-in aggregation
and policy behavior and persists through the selected Koan Data provider.

## Requirements and limits

- `CanonEntity<T>` is the supported canonical model shape; `AddKoan()` is the only registration step.
- Default commit order is canonical Entity, aggregation indexes, then audit. That sequence is not an
  atomic transaction across all Data providers.
- A canonical-write failure attempts neither indexes nor audit. An index failure can leave canonical
  state and some indexes durable; audit is skipped. An audit failure occurs after canonical state and
  indexes are durable. Canon reports the failed checkpoint and does not claim rollback or blind-retry
  safety.
- Canon is in-process. It does not provide distributed locking, transport delivery, durable replay, or
  automatic recovery.
- `ICanonPersistence` and `ICanonAuditSink` are the replacement seams. A custom persistence owns
  canonical, stage, and aggregation-index operations together.

Reference `Sylin.Koan.Canon.Web` only when Canon models should also receive automatic HTTP surfaces.
See the [technical reference](TECHNICAL.md) and
[CustomerCanon sample](../../samples/applications/CustomerCanon/README.md).

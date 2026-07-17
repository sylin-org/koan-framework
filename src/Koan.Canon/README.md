# Sylin.Koan.Canon

Entity-first canonicalization for Koan applications. Define a canonical entity and small contributors;
referencing the package makes `AddKoan()` discover and compose the pipeline.

```powershell
dotnet add package Sylin.Koan.Canon
```

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

Contributors are ordered by phase, optional `Order`, then type name. A failed or parked phase stops the
pipeline before later phases and canonical persistence. With no application contributor, Canon still
uses its default aggregation and policy behavior and persists through the selected Koan Data provider.

Use `Sylin.Koan.Canon.Web` when the models should also receive automatic HTTP surfaces. See the
[technical reference](TECHNICAL.md) and [CustomerCanon sample](../../samples/applications/CustomerCanon/README.md).

# Sylin.Koan.Canon.Contracts

Inert Canon vocabulary for applications, adapters, and Koan modules. It contains canonical entities,
metadata, pipeline contracts, persistence contracts, and annotations without activating a runtime.

- Target framework: .NET 10
- License: Apache-2.0
- Maturity: pre-1.0

## Install

```powershell
dotnet add package Sylin.Koan.Canon.Contracts
```

Most applications should reference `Sylin.Koan.Canon` or `Sylin.Koan.Canon.Web` instead. Reference the
contracts package directly when a library must describe Canon models or contributors without enabling
Canon in its host.

```csharp
using Koan.Canon;

public sealed class Customer : CanonEntity<Customer>
{
    [AggregationKey]
    public string Email { get; set; } = "";
}

public sealed class CustomerValidation : ICanonPipelineContributor<Customer>
{
    public CanonPipelinePhase Phase => CanonPipelinePhase.Validation;

    public ValueTask<CanonizationEvent?> Execute(
        CanonPipelineContext<Customer> context,
        CancellationToken cancellationToken) => ValueTask.FromResult<CanonizationEvent?>(null);
}
```

The functional runtime discovers those types only when `Sylin.Koan.Canon` is also referenced. No
activation metadata or inert-reference switch is required.

See the [technical contract](TECHNICAL.md) and the
[Canon pillar reference](../../docs/reference/canon/index.md).

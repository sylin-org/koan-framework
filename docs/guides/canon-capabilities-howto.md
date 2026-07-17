---
type: GUIDE
domain: canon
title: "Build a trusted canonical Entity"
audience: [developers, architects, ai-agents]
status: current
last_updated: 2026-07-17
framework_version: v0.17.0
validation:
  date_last_tested: 2026-07-17
  status: tested
  scope: CustomerCanon real-host golden path
related_guides:
  - entity-capabilities-howto.md
---

# Build a trusted canonical Entity

Use Canon when multiple or imperfect arrivals must converge into trusted business state. The application
defines identity and rules. Koan discovers them and owns pipeline composition, persistence, and optional
Web exposure.

## 1. Add the capability

For a Web application, reference:

```powershell
dotnet add package Sylin.Koan.Canon.Web
dotnet add package Sylin.Koan.Data.Connector.Json
```

The ordinary host remains ordinary:

```csharp
var builder = WebApplication.CreateBuilder(args);
builder.Services.AddKoan().AsWebApi();
var app = builder.Build();
await app.RunAsync();
```

Referencing Canon is intent. Do not add a Canon registrar, application module, controller, or explicit
`AddCanonRuntime()` for the common path.

## 2. Define canonical identity

```csharp
using Koan.Canon;

public sealed class Customer : CanonEntity<Customer>
{
    [AggregationKey]
    public string Email { get; set; } = "";

    public string FirstName { get; set; } = "";
    public string LastName { get; set; } = "";
    public string DisplayName { get; set; } = "";
}
```

`AggregationKey` declares how arrivals converge. Canon also carries metadata, source attribution,
lineage, lifecycle, and readiness alongside the Entity.

## 3. Add one business-aligned contributor

```csharp
public sealed class CustomerValidation : ICanonPipelineContributor<Customer>
{
    public CanonPipelinePhase Phase => CanonPipelinePhase.Validation;

    public ValueTask<CanonizationEvent?> Execute(
        CanonPipelineContext<Customer> context,
        CancellationToken cancellationToken)
    {
        var customer = context.Entity;
        customer.Email = customer.Email.Trim().ToLowerInvariant();
        customer.FirstName = customer.FirstName.Trim();
        customer.LastName = customer.LastName.Trim();

        if (customer.Email.Contains('@'))
        {
            return ValueTask.FromResult<CanonizationEvent?>(null);
        }

        return ValueTask.FromResult<CanonizationEvent?>(new CanonizationEvent
        {
            Phase = Phase,
            StageStatus = CanonStageStatus.Failed,
            Message = "Customer validation failed",
            Detail = "A valid email is required"
        });
    }
}
```

Contributor discovery is automatic. Koan runs phases in this order:

1. `Intake`
2. `Validation`
3. `Aggregation`
4. `Policy`
5. `Projection`
6. `Distribution`

Within a phase, optional `Order` then type name make ordering deterministic. A failed or parked phase
stops before later phases, aggregation indexing, and canonical persistence.

## 4. Use and inspect it

With `Sylin.Koan.Canon.Web` referenced:

```http
POST /api/canon/customer
Content-Type: application/json

{
  "email": " Alice@Example.com ",
  "firstName": " Alice ",
  "lastName": " Example "
}
```

- `200` means the canonical Entity was materialized.
- `202` means the pipeline parked the arrival.
- `422` means a contributor rejected it; the response includes phase events and reasons.

Inspect `/api/canon/models` for discovered models, routes, pipelines, and aggregation keys. Inspect
`/.well-known/Koan/facts` and startup reporting for the activated runtime, Web projection, and selected
Data provider.

For non-Web code, call `await customer.Canonize()` within an active Koan host.

## Advanced boundaries

- Replace `ICanonPersistence` only when taking ownership of canonical reads/writes, stages, and indexes
  as one unit.
- Use explicit `AddCanonRuntime(...)` only when discovery is insufficient and a model pipeline truly
  needs host-owned programmatic override.
- Runtime replay is bounded and process-local. It is not a durable event log.
- A Canon phase event is not a Communication event or transport message.
- Secure generated admin and rebuild routes for the deployment.

The complete runnable example is [CustomerCanon](../../samples/applications/CustomerCanon/README.md).
For all supported surfaces and limits, see the [Canon pillar reference](../reference/canon/index.md).

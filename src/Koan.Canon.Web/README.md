# Sylin.Koan.Canon.Web

Automatic HTTP projection for the Canon models already composed by `Sylin.Koan.Canon`. Reference the
package, call `AddKoan().AsWebApi()`, and each concrete `CanonEntity<T>` receives Canon-aware routes.

```powershell
dotnet add package Sylin.Koan.Canon.Web
```

## Meaningful result

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

`POST /api/canon/customer` enters the compiled Canon pipeline. `/api/canon/models` explains the exact
models, routes, pipelines, aggregation keys, policies, and audit posture. Successful writes return
`200`, parked writes return `202`, and failed validation returns `422` with Canon events. Bulk writes
return `200` with one result per input.

## Boundaries

- Web consumes Canon's host-owned composition plan; it does not rediscover or independently configure
  canonical models.
- A route-slug collision fails host composition and names the conflicting CLR model types.
- This package generates no admin, replay, rebuild, or value-object routes. Use the headless
  `ICanonRuntime.RebuildViews<T>` operation from application code when required.
- Generated model routes use the host's ordinary ASP.NET authentication and authorization policy; this
  package does not invent a privileged authorization scheme.
- Runtime commit ordering and partial-write limits belong to `Sylin.Koan.Canon` and are unchanged by
  HTTP projection.

See [CustomerCanon](../../samples/applications/CustomerCanon/README.md) for the runnable four-line host.

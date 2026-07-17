# Sylin.Koan.Canon.Web

Automatic HTTP projection for Canon models. Reference the package, call `AddKoan().AsWebApi()`, and
concrete `CanonEntity<T>` types receive Canon-aware routes without application controllers.

```powershell
dotnet add package Sylin.Koan.Canon.Web
```

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

That model appears at `/api/canon/customer`; `/api/canon/models` explains the discovered route,
pipeline, and aggregation keys. Successful writes return `200`, parked writes return `202`, and failed
pipeline validation returns `422` with Canon events. Bulk writes currently return `200` with one result
per input.

The admin/rebuild surfaces are mechanics, not an authorization policy. Secure them for the deployment.
See [CustomerCanon](../../samples/applications/CustomerCanon/README.md) for a complete runnable example.

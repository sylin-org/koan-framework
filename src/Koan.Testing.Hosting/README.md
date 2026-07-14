# Koan.Testing.Hosting

**Boot the application composition you actually want to test.** `KoanIntegrationHost` wraps a real
.NET generic host without taking a dependency on xUnit or choosing which Koan modules to activate.
Use it when a test needs DI, hosted-service lifecycle, and Koan's normal reflective discovery path.

Repository development references `src/Koan.Testing.Hosting` directly. Consume published Koan
packages only as one coherent version set; external package-set readiness is tracked separately from
this module's source contract.

## Start a host

```csharp
using Koan.Core;
using Koan.Testing.Integration;
using Microsoft.Extensions.DependencyInjection;

await using var host = await KoanIntegrationHost.Configure()
    .WithSetting("Koan:Data:DefaultProvider", "inmemory")
    .ConfigureServices(services => services.AddKoan())
    .StartAsync();

var service = host.Services.GetRequiredService<MyApplicationService>();
```

The default environment is `Test`. In-memory settings are applied first; configuration sources added
through `ConfigureAppConfiguration` can override them. `ConfigureServices` is additive and may be
called more than once.

The helper deliberately does not call `AddKoan()` for you. Tests can choose full discovery, a smaller
Core composition, or entirely custom registrations without the hosting package inferring intent from
assemblies it does not own.

## Ownership contract

- `Build()` transfers an unstarted host to the caller, who must dispose it.
- `StartAsync()` owns the built host until startup succeeds. If startup fails, it disposes the host
  before rethrowing the original startup exception.
- A successfully returned host belongs to the caller and should be used with `await using`.
- Disposal asks hosted services to stop best-effort, then uses the host's asynchronous disposal path
  when available.

## Choose it when

- a focused integration test needs the same generic-host lifecycle as the application;
- a module or adapter test needs explicit configuration and service overrides;
- an agent needs a small, inspectable composition seam instead of recreating bootstrap wiring.

Do not use it for pure unit tests or as an application runtime abstraction. Real Koan hosts in the
same test process should remain sequential: owner-safe teardown prevents an older host from clearing
a newer owner, but simultaneous static Entity operations still share the process-default binding.

See [`TECHNICAL.md`](./TECHNICAL.md) for the lifecycle and failure contract.

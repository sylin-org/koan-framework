---
type: GUIDE
domain: engineering
title: "Auth Controller Discovery Regression"
audience: [developers, maintainers, ai-agents]
status: current
last_updated: 2025-09-30
framework_version: v0.2.18+
validation:
  date_last_tested: 2025-09-30
  status: verified
  scope: samples/S5.Recs
---

# Auth Controller Discovery Regression

## Contract

- **Scope**: Registration flow for Koan Web Auth controllers and the discovery endpoint at `/.well-known/auth/providers`.
- **Inputs**: `KoanAutoRegistrar.Initialize` execution during module bootstrapping and ASP.NET Core MVC `ApplicationPartManager` state.
- **Outputs**: Discovery controller availability, authentication provider metadata, and healthy redirect flow for test providers.
- **Failure modes**: MVC omits the auth controllers, endpoint returns 404, auth UI cannot fetch provider list, sign-in redirects fail.
- **Success criteria**: Auth controller assembly added to MVC parts exactly once, discovery endpoint returns provider metadata, login journey proceeds.

## Edge Cases

1. **Multiple `AddControllers()` calls**: Ensure duplicate application parts are not added when host projects register additional MVC services.
2. **Custom composition hosts**: Verify auto-registrar logic runs even when services are built through bespoke hosting pipelines.
3. **Dynamic provider configuration**: Keep discovery payload stable when providers are added via configuration or contributors at runtime.
4. **Strict production environments**: Respect `AllowDynamicProvidersInProduction` gate while surfacing controller routes.
5. **Module reuse across samples**: Confirm samples sharing the auth module inherit the fix without bespoke wiring.

## Summary

The S5.Recs stack reported 404s from `/.well-known/auth/providers` despite loading `Koan.Web.Auth`, `Koan.Web.Auth.Connector.Test`, and `Koan.Web.Auth.Services`. Investigation showed the auto-registrar never added its controller assembly to MVC's `ApplicationPartManager`, so the discovery controller failed to load.

## Resolution Steps

1. Update `KoanAutoRegistrar.Initialize` to call `services.AddControllers()` and register an `AssemblyPart` for `DiscoveryController` when absent.
2. Rebuild the solution (`dotnet build --nologo --verbosity:minimal`).
3. Restart the S5.Recs Docker stack via `samples/S5.Recs/start.bat`.
4. Verify `http://localhost:5084/.well-known/auth/providers` returns the expected provider payload.
5. Tear down the stack (`docker compose -p koan-s5-recs -f docker/compose.yml down`).

```csharp
public void Initialize(IServiceCollection services)
{
    services.AddKoanWebAuth();
    services.TryAddEnumerable(ServiceDescriptor.Singleton<IStartupFilter, KoanWebAuthStartupFilter>());

    var assembly = typeof(DiscoveryController).Assembly;
    var mvc = services.AddControllers();
    if (!mvc.PartManager.ApplicationParts.OfType<AssemblyPart>().Any(p => p.Assembly == assembly))
    {
        mvc.PartManager.ApplicationParts.Add(new AssemblyPart(assembly));
    }
}
```

## Validation

- `dotnet build --nologo --verbosity:minimal`
- `samples/S5.Recs/start.bat`
- `curl http://localhost:5084/.well-known/auth/providers`
- `docker compose -p koan-s5-recs -f docker/compose.yml down`

## Follow-Ups

- Audit other Koan modules with controllers registered via auto-registrars to ensure they add their assemblies to MVC part managers.
- Add a regression test that spins up a minimal host with `Koan.Web.Auth` and asserts discovery route availability.


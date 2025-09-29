# Koan.Secrets.Core

## Contract
- **Purpose**: Provide the secret runtime for Koan, coordinating providers, caching, and secret materialization.
- **Primary inputs**: Secret descriptors from `Koan.Secrets.Abstractions`, provider registrations, Koan configuration sources.
- **Outputs**: Scoped secret resolutions, provider health diagnostics, and boot notes summarizing secret availability.
- **Failure modes**: Provider connectivity failures, missing descriptors, or attempts to resolve secrets without scope information.
- **Success criteria**: Secrets resolve quickly with appropriate scoping, provider failures surface via diagnostics, and rotation policies can be enforced centrally.

## Quick start
```csharp
using Koan.Secrets.Core;

public sealed class SecretsAutoRegistrar : IKoanAutoRegistrar
{
    public string ModuleName => "Secrets";

    public void Initialize(IServiceCollection services)
    {
        services.AddSecretsCore(options =>
        {
            options.CacheDuration = TimeSpan.FromMinutes(5);
        });
    }

    public void Describe(BootReport report, IConfiguration cfg, IHostEnvironment env)
        => report.AddNote("Secrets runtime enabled");
}

public async Task<string> ResolveStripeKeyAsync(CancellationToken ct)
{
    var secret = await SecretResolver.ResolveAsync("stripe:api-key", SecretScope.Environment("Production"), ct);
    return secret.Value;
}
```
- Register the secrets runtime to enable provider discovery, caching, and diagnostics.
- Resolve secrets through `SecretResolver.ResolveAsync` using descriptor IDs and scopes.

## Configuration
- Configure caching strategy (`CacheDuration`, `CacheSize`) to balance freshness and performance.
- Enable health checks by calling `AddSecretsHealthChecks()`; integrate into Koan Web health endpoints.
- Wire environment fallbacks using `SecretResolver.TryResolveAsync` to handle optional secrets.

## Edge cases
- Provider outages: fallback to cached secrets but log warnings; consider short cache lifetimes for high-rotation secrets.
- Missing tenants: ensure scope parameters include tenant identifiers to avoid cross-tenant leaks.
- Rotations: call `SecretResolver.InvalidateAsync` after pushing new values to force refresh.
- Async deadlocks: always use async APIs; synchronous calls can block on provider I/O.

## Related packages
- `Koan.Secrets.Abstractions` – descriptor and provider contracts.
- `Koan.Secrets.Vault` – HashiCorp Vault provider to plug into the runtime.
- `Koan.Core` – boot reporting and DI helpers used by the runtime.

## Reference
- `SecretResolver` – main entry point for resolving secrets.
- `SecretsOptions` – configuration object for runtime behavior.
- `SecretProviderRegistry` – internal registry managing providers and descriptors.

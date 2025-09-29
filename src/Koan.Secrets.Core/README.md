# Koan.Secrets.Core

> ✅ Validated against configuration bootstrap upgrades, provider chaining, and cache expiry refresh on **2025-09-29**. See [`TECHNICAL.md`](./TECHNICAL.md) for runtime architecture and edge-case coverage.

Koan’s secrets runtime coordinates provider discovery, caching, and configuration interpolation so apps can consume `secret://` URIs without bespoke wiring.

## Quick start

```csharp
using Koan.Secrets.Abstractions;
using Koan.Secrets.Core.Configuration;
using Koan.Secrets.Core.DI;

var builder = WebApplication.CreateBuilder(args);

// Resolve ${secret://...} placeholders from configuration
builder.Configuration.AddSecretsReferenceConfiguration();

// Register the secrets runtime (env + configuration providers by default)
builder.Services
    .AddKoanSecrets(options => options.DefaultTtl = TimeSpan.FromMinutes(10))
    .AddProvider<VaultSecretProvider>(); // optional custom provider

var app = builder.Build();

// Upgrade bootstrap configuration providers to the DI-backed resolver once the container is ready
SecretResolvingConfigurationExtensions.UpgradeSecretsConfiguration(app.Services);

app.MapGet("/stripe-key", async (ISecretResolver resolver, CancellationToken ct) =>
{
    var secret = await resolver.GetAsync(SecretId.Parse("secret://stripe/api-key"), ct);
    return Results.Ok(secret.AsString());
});

app.Run();

sealed class VaultSecretProvider : ISecretProvider
{
    public Task<SecretValue> GetAsync(SecretId id, CancellationToken ct) => throw new NotImplementedException();
}
```

- `AddKoanSecrets` wires the default env/config providers, memory cache, and resolver chain; return value lets you append custom providers.
- Call `UpgradeSecretsConfiguration` after DI is fully built so configuration uses the chained resolver instead of the bootstrap fallback.

## Configuration & caching guidelines

- `SecretsOptions.DefaultTtl` controls how long cached material stays valid when providers omit TTL metadata.
- Provide per-secret TTLs via `SecretMetadata.Ttl` to align with rotation policies; the cache honours the shortest value returned.
- Configuration values containing `${secret://scope/name}` or whole `secret://` URIs resolve automatically once the upgrade step runs.
- Combine with health checks by adding a probing provider (e.g., Vault) that validates connectivity during app boot.

## Operational tips

- Prefer provider-qualified URIs (`secret+vault://team/api-key`) when multiple backends are active; the runtime respects the `Provider` hint.
- Cache misses fall through each registered provider until one succeeds; order providers from fastest to slowest (config → env → remote).
- When rotating secrets, clear cached values by setting `SecretMetadata.Ttl` to a low value or restarting the app; a targeted invalidation helper can be added via custom resolver wrappers.
- Use structured logging (inject `ILogger<ChainSecretResolver>`) to trace provider fallbacks during incident diagnosis.

## Related docs

- [`Koan.Secrets.Abstractions`](../Koan.Secrets.Abstractions/README.md) – identifier and payload contracts.
- [`Koan.Secrets.Vault`](../Koan.Secrets.Vault/README.md) – concrete provider leveraging Vault.
- [`docs/architecture/capability-map.md`](../../docs/architecture/capability-map.md) – high-level capability overview including the secrets stack.

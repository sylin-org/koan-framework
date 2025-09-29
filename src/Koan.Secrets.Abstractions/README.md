# Koan.Secrets.Abstractions

> ✅ Validated against `SecretId` parsing, `SecretValue` projections, and resolver template paths on **2025-09-29**. See [`TECHNICAL.md`](./TECHNICAL.md) for full contract details and edge cases.
 
Shared primitives for expressing secret identifiers, payloads, and provider contracts. Concrete providers (`Koan.Secrets.Core`, Vault, environment-based resolvers) wire into these interfaces so apps can request secrets without binding to a specific backend.

## Quick start

Implement a provider that fetches payloads and register a resolver that supports templated strings:

```csharp
using System.Text.RegularExpressions;
using Koan.Secrets.Abstractions;

public sealed class EnvSecretProvider : ISecretProvider
{
    public Task<SecretValue> GetAsync(SecretId id, CancellationToken ct)
    {
        var key = $"SECRETS__{id.Scope}__{id.Name}".ToUpperInvariant();
        var value = Environment.GetEnvironmentVariable(key)
            ?? throw new SecretNotFoundException(id.ToString());

        var secret = new SecretValue(
            System.Text.Encoding.UTF8.GetBytes(value),
            SecretContentType.Text,
            new SecretMetadata { Provider = "env", Version = id.Version });

        return Task.FromResult(secret);
    }
}

public sealed class TemplateSecretResolver : ISecretResolver
{
    private readonly ISecretProvider _provider;
    private static readonly Regex TokenRegex = new(@"secret://[A-Za-z0-9\-_.~/]+(?:\?version=[^}\""\s]+)?", RegexOptions.Compiled | RegexOptions.CultureInvariant);

    public TemplateSecretResolver(ISecretProvider provider) => _provider = provider;

    public Task<SecretValue> GetAsync(SecretId id, CancellationToken ct = default)
        => _provider.GetAsync(id, ct);

    public async Task<string> ResolveAsync(string template, CancellationToken ct = default)
    {
        if (!TokenRegex.IsMatch(template)) return template;

        var result = template;
        foreach (Match match in TokenRegex.Matches(template))
        {
            ct.ThrowIfCancellationRequested();
            var parsed = SecretId.Parse(match.Value);
            var secret = await _provider.GetAsync(parsed, ct);
            result = result.Replace(match.Value, secret.AsString(), StringComparison.Ordinal);
        }

        return result;
    }
}
```

- `SecretId` URIs (`secret://scope/name`) keep scopes and names canonical; optional provider hints (`secret+vault://`) steer routing when multiple providers coexist.
- `SecretValue` wraps the payload and ensures projections (`AsString()`, `AsJson<T>()`) match the declared `SecretContentType`.

## Contract highlights

- `ISecretProvider.GetAsync` is the single source of truth for fetching secrets. Throw `SecretNotFoundException`, `SecretUnauthorizedException`, or `SecretProviderUnavailableException` for precise error semantics.
- `ISecretResolver.ResolveAsync` performs best-effort templating—strings without tokens short-circuit without provider calls.
- `SecretMetadata` captures provider hints, versions, and TTLs so rotation tooling can make informed decisions.
- `SecretId.Parse` rejects malformed URIs early (missing scope/name, unsupported schemes) to avoid propagating invalid identifiers.

## Edge cases

- Whitespace or relative URIs throw `ArgumentException` during `SecretId.Parse`.
- Binary payloads (`SecretContentType.Bytes`) can only be accessed via `AsBytes()`; attempting to call `AsString()`/`AsJson<T>()` raises `InvalidOperationException`.
- Provider-qualified URIs (`secret+vault://prod/payment-key`) fill the `Provider` property so orchestrators can direct the call to a specific backend.
- Hostless URIs (`secret:///prod/payment-key`) remain valid; parsing normalizes host/path combinations to `(Scope="prod", Name="payment-key")`.

## Validation checklist

- Unit tests: `tests/Koan.Secrets.Core.Tests` cover resolver templating, ID parsing, and provider routing logic. Run them after provider changes.
- DocFX: `pwsh -File scripts/build-docs.ps1 -ConfigPath docs/api/docfx.json -Strict` ensures this documentation stays linked and warning-free.

## Related docs

- [`TECHNICAL.md`](./TECHNICAL.md) – complete contract narrative and architectural positioning.
- `/docs/architecture/principles.md` – cross-cutting security principles referenced by secrets modules.
- `src/Koan.Secrets.Core/README.md` – runtime orchestrator that consumes these abstractions.

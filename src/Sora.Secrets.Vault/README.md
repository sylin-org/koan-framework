# Sora.Secrets.Vault

HashiCorp Vault secret provider for Sora Secrets. Production-safe, simple to adopt, and wired automatically via Sora’s auto-registrar.

What you get
- KV v1 and v2 support (configurable via `UseKvV2`).
- Provider-forced URIs: `secret+vault://<scope>/<name>` to ensure Vault routing.
- Options binding from configuration (`Sora:Secrets:Vault`).
- Health checks (tag: `secrets`).
- Auto-registration (no manual DI when using StartSora).

## Contract (short)
- Input: `SecretId` with `secret+vault://scope/name` (optionally `?version=` ignored by Vault KV).
- Output: `SecretValue` with type Text/Json/Bytes and `SecretMetadata` (Provider, TTL when available or configured).
- Errors: `SecretNotFoundException`, `SecretUnauthorizedException`, `SecretProviderUnavailableException`.
- Success: Read secret from KV v2 (`/<mount>/data/<scope>/<name>`) or KV v1 (`/<mount>/<scope>/<name>`).

Edge cases
- Namespace auth: missing/incorrect `X-Vault-Namespace` → unauthorized.
- Mount mismatch: wrong `Mount` or v1/v2 mismatch → not found.
- Provider forcing: use `secret+vault://…` when you must bypass other providers in the chain.

## Configure
Configuration path: `Sora:Secrets:Vault`

Options
- `Enabled` (bool, default true)
- `Address` (string, e.g., `http://localhost:8200`)
- `Token` (string; use a reference or env var indirection)
- `Namespace` (string, optional)
- `Mount` (string, default `secret`)
- `UseKvV2` (bool, default true)
- `Timeout` (TimeSpan, default 10s)
- `DefaultTtl` (TimeSpan, optional; used when Vault response has no TTL)

Example appsettings.json

```
{
  "ConnectionStrings": {
    "Default": "Host=pg;Password=${secret+vault://db/main};Database=app"
  },
  "Sora": {
    "Secrets": {
      "Vault": {
        "Address": "http://localhost:8200",
        "Token": "${secret://env/VAULT_TOKEN}",
        "Mount": "secret",
        "UseKvV2": true
      }
    }
  }
}
```

Notes
- Token should not be in plain text; prefer indirection like `${secret://env/VAULT_TOKEN}`.
- KV v2 path format is handled internally (`/v1/<mount>/data/...`).

## DI and wiring
- Using StartSora: auto-detected via `ISoraAutoRegistrar` and registered automatically. No manual calls needed.
- Manual DI (advanced):
  - Call `services.AddSoraSecrets();` (core).
  - Bind `VaultOptions` from configuration (`Sora:Secrets:Vault`).
  - Register a typed `HttpClient` with `X-Vault-Token` and optional `X-Vault-Namespace` headers.
  - Add `VaultSecretProvider` to the `ISecretProvider` chain.

Code example (manual)

```csharp
var services = new ServiceCollection();
services.AddOptions();
services.AddSoraSecrets();
services.AddHttpClient("Sora.Secrets.Vault", (sp, http) =>
{
    var opt = sp.GetRequiredService<IOptions<VaultOptions>>().Value;
    http.BaseAddress = new Uri(opt.Address);
    http.DefaultRequestHeaders.Add("X-Vault-Token", opt.Token);
    if (!string.IsNullOrWhiteSpace(opt.Namespace))
        http.DefaultRequestHeaders.Add("X-Vault-Namespace", opt.Namespace);
});
services.Configure<VaultOptions>(config.GetSection("Sora:Secrets:Vault"));
services.AddSingleton<ISecretProvider, VaultSecretProvider>();
```

## Health checks
- Registered automatically by the auto-registrar when `Enabled`.
- Tag: `secrets`.
- Probe: `v1/sys/health` against `Address` with headers from options.

## Usage snippets
Resolve placeholder in config

```json
{
  "ConnStr": "Host=pg;Password=${secret+vault://db/main};Database=app"
}
```

Resolve via `ISecretResolver`

```csharp
var id = SecretId.Parse("secret+vault://db/main");
var secret = await resolver.GetAsync(id, ct);
var pw = secret.AsString();
```

## Troubleshooting
- 404 from Vault → check `Mount`, `UseKvV2`, and path casing.
- 403/permission issues → check `Token` policies and `Namespace`.
- Slow or timeouts → increase `Timeout`; verify network routes.

# Sora.Secrets.Vault

HashiCorp Vault secret provider for Sora Secrets.

- Supported: KV v1 and v2 (configurable via `UseKvV2`).
- First-class integration: DI, health checks, and auto-registration.

## Basic use

- Configure via `Sora:Secrets:Vault` options:
  - `Address` (e.g., `http://localhost:8200`), `Token`, optional `Namespace`, `Mount` (default `secret`), `UseKvV2` (default true).
- Reference secrets with provider-forced URIs:
  - `secret+vault://<scope>/<name>` → reads `/<mount>/data/<scope>/<name>` when v2.

Example appsettings.json:

```
{
  "ConnectionStrings": {
    "Default": "Host=pg;Password=${secret+vault://db/main};Database=app"
  },
  "Sora": {
    "Secrets": {
      "Vault": {
        "Address": "http://localhost:8200",
        "Token": "${secret://env/VAULT_TOKEN}",
        "Mount": "secret",
        "UseKvV2": true
      }
    }
  }
}
```

## Health

Registers `VaultHealthCheck` under the `secrets` tag.

## Notes

- Errors map to `SecretNotFound`, `SecretUnauthorized`, or provider unavailable.
- TTL is honored by the resolver cache (falls back to a 5 min default when unspecified).

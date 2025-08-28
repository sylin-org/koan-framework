# Sora.Secrets.Vault — TECHNICAL

Reference + architecture for the HashiCorp Vault secret provider.

## Overview
- Implements `ISecretProvider` for Sora Secrets.
- Routes explicit secrets with `secret+vault://scope/name` and supports KV v1/v2.
- Uses `HttpClient` with Vault headers and configurable base address.
- Auto-registered via `ISoraAutoRegistrar` with options binding and health check.

## Contracts
- `VaultSecretProvider : ISecretProvider` — resolves `SecretId` and returns `SecretValue`.
- `VaultOptions` — strongly-typed options bound from `Sora:Secrets:Vault`.
- `VaultHealthCheck` — checks `v1/sys/health`.

### SecretId semantics
- Provider-forced scheme: `secret+vault://` ensures this provider is used even when other providers are in the chain.
- Scope maps to the first path segment; name maps to the second segment.
- Optional `?version=` is parsed by `SecretId` but ignored by KV APIs.

## Options binding
Configuration path: `Sora:Secrets:Vault`

- `Enabled` (bool, default true): if false, registrar skips provider registration and registers a health check that reports unavailable.
- `Address` (string): Vault server base URL, e.g., `http://localhost:8200`.
- `Token` (string): Vault auth token. Prefer indirection (`${secret://env/VAULT_TOKEN}`) instead of plaintext.
- `Namespace` (string, optional): Vault Enterprise namespace.
- `Mount` (string, default `secret`): KV mount path.
- `UseKvV2` (bool, default true): whether to use KV v2 layout.
- `Timeout` (TimeSpan, default 00:00:10): HTTP timeout for the typed client.
- `DefaultTtl` (TimeSpan, optional): Fallback TTL when Vault response lacks TTL metadata.

## Pathing
- KV v2 read: `GET /v1/{mount}/data/{scope}/{name}` → data in `data.data` and metadata in `data.metadata`.
- KV v1 read: `GET /v1/{mount}/{scope}/{name}` → data is the raw payload (we treat as text unless JSON is detected/configured upstream).

## Caching and TTL
- The resolver (`ChainSecretResolver`) caches `SecretValue` using `SecretMetadata.Ttl` when provided; otherwise default 5 minutes.
- Vault provider sets TTL via:
  - v2: may use metadata where applicable; else rely on `DefaultTtl`.
  - v1: no TTL from server; uses `DefaultTtl` if provided.

## DI wiring
- Auto-registrar (`Initialization/SoraAutoRegistrar.cs`):
  - Binds `VaultOptions` from configuration.
  - Registers typed `HttpClient` named `Sora.Secrets.Vault` with base address, `X-Vault-Token`, and optional `X-Vault-Namespace`.
  - Adds `VaultSecretProvider` to `ISecretProvider` chain only when `Enabled`.
  - Adds `VaultHealthCheck` with tag `secrets`.

- Manual registration (when not using StartSora):
  - `services.AddSoraSecrets();`
  - `services.Configure<VaultOptions>(config.GetSection("Sora:Secrets:Vault"));`
  - `services.AddHttpClient("Sora.Secrets.Vault", ...)` with headers.
  - `services.AddSingleton<ISecretProvider, VaultSecretProvider>();`

## Health checks
- Endpoint: `v1/sys/health`.
- Success: HTTP 200/204/429 as per Vault semantics; health check normalizes to Healthy/Degraded.
- Failures map to Unhealthy with diagnostic message and exception.
- Tag: `secrets` so you can filter in health UI/pipelines.

## Error mapping
- 404 → `SecretNotFoundException`.
- 403/401 → `SecretUnauthorizedException`.
- Network/5xx → `SecretProviderUnavailableException("vault", reason)`.

## Security notes
- Do not log secret values; `SecretValue.ToString()` is redacted.
- Keep tokens out of appsettings; prefer env/secret indirection.
- Scope your token policies to only the required paths.
- Consider using a short `DefaultTtl` for rotation-sensitive secrets.

## Observability
- Health check (as above) and structured logs from provider (warning on not found, error on failures).
- Consider adding request duration metrics via `HttpClient` handlers if needed.

## Testing
- Unit tests use a fake `HttpMessageHandler` to simulate KV v1/v2 responses.
- Ensure provider-forced routing (`secret+vault://…`) returns `NotFound` when provider doesn’t match.

## Compatibility
- .NET 9, Microsoft.Extensions.* (Http, Options, Logging, HealthChecks).
- Vault OpenAPI not required; simple REST JSON.

## Upgrade & bootstrap
- During bootstrap, secrets in configuration may resolve from env/config.
- After DI initialization, the secret-resolving configuration upgrades to the DI-backed resolver so Vault can serve references; a change token triggers a config reload so bindings refresh.

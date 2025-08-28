---
title: Secrets — references, configuration resolution, and provider chain
description: How to use secret references (secret://) in configuration and code; DI wiring; orchestration behavior.
---

## Contract

- Inputs: SecretId (secret://scope/name?version=… or secret+vault://scope/name).
- Outputs: SecretValue (string/bytes/json) with metadata (version, ttl, provider).
- Errors: NotFound, Unauthorized, ProviderUnavailable (no payloads).

## Usage

Registration (DI)

- services.AddSoraSecrets(); // env + config providers
- configuration.AddSecretsReferenceConfiguration(); // resolve on read

appsettings.json

- Db:Password: "secret://db/main"
- ConnectionStrings:Default: "Host=pg;Password=${secret://db/main};Database=app"

Code

- var pw = await resolver.GetAsync(SecretId.Parse("secret://db/main"), ct);
- var conn = await resolver.ResolveAsync(configuration.GetConnectionString("Default"), ct);

### Development alignment (minimize provider swap drift)

- Prefer provider-agnostic references (secret://…) in appsettings and placeholders; avoid forcing provider unless strictly required.
- Keep the chain order stable across environments: [Vault (if present), Configuration (User Secrets/.env/appsettings), Environment]. Recipes add/remove providers, not reorder them.
- In Development, add User Secrets to IConfiguration; the existing ConfigurationSecretProvider will resolve references without extra adapters.
- Error semantics are unified: NotFound → next provider (unless forced scheme); Unauthorized/ProviderUnavailable → fail fast.
- TTL and reload: provider TTL is honored when present; otherwise the resolver uses a configured default. Options binding sees a consistent reload behavior.

Recommended Development wiring

- Host configuration: add .NET User Secrets (dev-only). The chain then reads values for secret://scope/name from config keys (Secrets:scope:name).
- Do not store raw secrets in appsettings.\*; keep secrets outside the repo (User Secrets, .env, env vars).

## Notes

- Provider forcing: use secret+vault:// to route to a specific adapter.
- Orchestration: exporters prefer references (secretsRefOnly). They emit envRef placeholders for dev shells and map to platform-native SecretRefs in Compose/Helm when available. Adapters should not create secrets; only reference by name.
- Redaction: values never logged; placeholders are redacted.

### Vault adapter

- Package: Sora.Secrets.Vault (auto-registered via SoraAutoRegistrar).
- Configure under Sora:Secrets:Vault:
  - Enabled: true
  - Address: "https://vault:8200"
  - Token: "${env://VAULT_TOKEN}" (use env, not literal)
  - Namespace: optional
  - Mount: "secret" (KV engine mount)
  - UseKvV2: true

Examples

- Force provider in a reference: secret+vault://db/main
- Whole value: "secret+vault://db/main?version=2"
- Placeholder: "Password=${secret+vault://db/main}"

### Orchestration envRef (contract)

- envRef entries are stable names the app expects (e.g., APP_DB_PASSWORD → secret://db/main). Exporters render these as environment references in manifests, never materializing raw values.
- Capabilities: ExporterCapabilities.SecretsRefOnly indicates an exporter never injects literal secrets and requires external provisioning (Vault, K8s, cloud Secret Manager).

### Recipes behavior

- Recipes enable/disable providers while keeping precedence intact so the same secret:// references work in Development and Production without edits.
- Forced schemes (secret+vault://…) never fall back; use only when the target backend must be enforced.

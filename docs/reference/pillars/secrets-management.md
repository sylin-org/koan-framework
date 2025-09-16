---
title: Working with Secrets — solution developers
description: Scenario-driven guidance for using secrets across .NET and Koan.Secrets, from simplest dev setup to production-capable patterns.
---

## Contract (what success looks like)

- Inputs
  - References: secret://scope/name (provider-agnostic) and inline ${secret://…}
  - Optional forced scheme: secret+vault://scope/name when a specific backend must be used
- Outputs
  - Materialized values for configuration binding and code, with redaction in logs
  - Stable behavior across Development/CI/Production via a consistent provider chain
- Error modes
  - NotFound → fall through to next provider (unless forced scheme)
  - Unauthorized/ProviderUnavailable → fail fast with redacted errors
- Success criteria
  - Same config works in dev and prod; no raw secrets in repo; rotation-friendly via TTL/change tokens

References: ARCH-0050, ARCH-0051; see also reference/secrets.md for module details.

## Quick wiring (one-time)

- Register secrets and the resolve-on-read configuration wrapper
  - services.AddKoanSecrets();
  - configuration.AddSecretsReferenceConfiguration();
- Prefer provider-agnostic references in appsettings: secret://scope/name
- Keep secrets out of repo; use .NET User Secrets, environment variables, or your platform secret manager

## Scenarios (from simplest to more advanced)

1. Environment variables only (no files)

- Set an environment variable per secret and reference it via configuration mapping if needed
- With Koan’s config-backed provider, you can also map canonical keys directly (see Scenario 2)

2. Development using .NET User Secrets (preferred, no extra provider)

- Add User Secrets to IConfiguration in Development
- Store secrets under the canonical key space Secrets:scope:name
- Example user-secrets store (user profile, not in repo):
  {
  "Secrets": {
  "db": {
  "main": "p@ssw0rd-dev"
  }
  }
  }
- In appsettings.json, reference the secret:
  - Db:Password: "secret://db/main"
  - ConnectionStrings:Default: "Host=pg;Password=${secret://db/main};Database=app"
- Result: ConfigurationSecretProvider resolves the reference from IConfiguration; no new adapter required

3. .env or environment fallbacks (optional)

- If your host includes .env or additional config sources, map to the same canonical key
- Env var naming: Secrets**db**main (double underscores)

4. Inline placeholders in composite values

- Use ${secret://…} inside connection strings or URLs
- Example: "Host=pg;User Id=app;Password=${secret://db/main};Database=app"

5. Whole-value references (JSON/opaque payloads)

- For JSON or binary secrets, use the whole value form, then parse in code if needed
- Example appsettings: ApiKeys:ServiceX: "secret://api/servicex"
- Code: var key = await resolver.GetAsync(SecretId.Parse("secret://api/servicex"), ct); // parse JSON as needed

6. Forcing a specific backend (when required)

- Use secret+vault://scope/name to route to Vault; this never falls back on NotFound
- Prefer secret://… for portability; only force when backend-specific features must be guaranteed

7. Recipes swap providers without config edits

- Chain order is stable across envs: [Vault (if present), Configuration (User Secrets/.env/appsettings), Environment]
- In Development, Vault is typically absent → config/env serve values; in Prod, Vault takes precedence
- Keep references provider-agnostic to avoid edits when moving between envs

8. Code-based resolution for ad-hoc needs

- Inject ISecretResolver and resolve on demand
- Examples
  - var id = SecretId.Parse("secret://db/main");
  - var pw = await resolver.GetAsync(id, ct);
  - var composed = await resolver.ResolveAsync(configuration["Some:Template"], ct);
- Notes: values are cached by TTL; ToString on SecretValue is redacted

9. Rotation and reload basics

- Providers may advertise TTL; the resolver honors provider TTL else falls back to a configured default
- The resolve-on-read configuration wrapper swaps change tokens on upgrade and reload; OptionsMonitor observes updates

10. Gating startup on required secrets (optional)

- During startup, you may attempt to resolve critical secrets with a timeout and fail closed if missing/unauthorized
- Keep the list minimal and scoped to app readiness (not build-time)

## Edge cases and guidance

- Missing secret (NotFound): falls through chain (unless scheme is forced). Validate required settings early when necessary
- Unauthorized/ProviderUnavailable: fail fast; do not retry tight loops; rely on health checks
- JSON vs string: prefer whole-value references for JSON; parse after resolution
- Large/slow secrets: avoid frequent ResolveAsync calls; rely on TTL caching
- Concurrency/timeouts: set reasonable timeouts per provider; don’t block critical paths on non-essential secrets
- Redaction: never log raw values; identifiers only

## Minimal examples (copy/paste)

- appsettings.json

  - Db:Password: "secret://db/main"
  - ConnectionStrings:Default: "Host=pg;Password=${secret://db/main};Database=app"

- Development user-secrets JSON (conceptual)
  {
  "Secrets": { "db": { "main": "p@ssw0rd-dev" } }
  }

- Environment variable equivalent (Windows PowerShell)
  $env:Secrets**db**main = "p@ssw0rd-dev"

## More examples (progressively harder)

1. Program wiring (ASP.NET Core)

```csharp
// Program.cs
var builder = WebApplication.CreateBuilder(args);

// Load dev-only .NET User Secrets (kept outside the repo)
if (builder.Environment.IsDevelopment())
{
  builder.Configuration.AddUserSecrets<Program>(optional: true);
}

// Register Koan secrets and the resolve-on-read configuration wrapper
builder.Services.AddKoanSecrets();
builder.Configuration.AddSecretsReferenceConfiguration();

var app = builder.Build();
app.Run();
```

2. Strongly-typed options with secret references

```json
// appsettings.json
{
  "Db": {
    "Password": "secret://db/main",
    "ConnectionString": "Host=pg;Password=${secret://db/main};Database=app"
  }
}
```

```csharp
// DbOptions.cs
public sealed class DbOptions
{
  public string? Password { get; set; }
  public string? ConnectionString { get; set; }
}

// Program.cs (registration excerpt)
builder.Services.Configure<DbOptions>(builder.Configuration.GetSection("Db"));

// Any service or controller
public sealed class UsesDb(IOptionsMonitor<DbOptions> opts, ILogger<UsesDb> log)
{
  public void Dump()
  {
    // Values are materialized by the configuration wrapper; never log raw secrets
    var cs = opts.CurrentValue.ConnectionString;
    log.LogInformation("DB connection template resolved (length only): {Len}", cs?.Length ?? 0);
  }
}
```

3. Whole-value secret (JSON payload) and parsing via IConfiguration

```json
// appsettings.json
{
  "ApiKeys": {
    "ServiceX": "secret://api/servicex" // value is JSON in the secret store
  }
}
```

```csharp
// Anywhere you have IConfiguration (after AddSecretsReferenceConfiguration())
var json = builder.Configuration["ApiKeys:ServiceX"]; // resolved raw JSON string
if (!string.IsNullOrEmpty(json))
{
  var model = Newtonsoft.Json.JsonConvert.DeserializeObject<ServiceXKey>(json);
  // ...use model
}

public sealed record ServiceXKey(string KeyId, string Secret);
```

4. Forcing a specific backend and handling errors

```csharp
// Force Vault; NotFound will not fall back
var id = Koan.Secrets.Abstractions.SecretId.Parse("secret+vault://db/main");
try
{
  var resolver = app.Services.GetRequiredService<Koan.Secrets.Abstractions.ISecretResolver>();
  var value = await resolver.GetAsync(id, default);
  // use value (do not log)
}
catch (Exception ex)
{
  // Keep logs redacted and actionable
  app.Logger.LogError(ex, "Failed to resolve required secret {Secret}", id);
  throw; // fail closed for required secrets
}
```

5. Gating startup on required secrets with a timeout

```csharp
var ids = new[]
{
  Koan.Secrets.Abstractions.SecretId.Parse("secret://db/main"),
  Koan.Secrets.Abstractions.SecretId.Parse("secret://api/servicex")
};

var resolver = app.Services.GetRequiredService<Koan.Secrets.Abstractions.ISecretResolver>();
using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));

foreach (var sid in ids)
{
  try { await resolver.GetAsync(sid, cts.Token); }
  catch (Exception ex)
  {
    app.Logger.LogError(ex, "Startup blocked by missing/unauthorized secret {Secret}", sid);
    throw; // stop boot to avoid running misconfigured
  }
}
```

6. Reacting to secret-driven config changes (OptionsMonitor)

```csharp
// Secrets that change in the backend will trigger config reload; observe via IOptionsMonitor
var listener = app.Services.GetRequiredService<IOptionsMonitor<DbOptions>>();
listener.OnChange(o => app.Logger.LogInformation("Db options updated (ConnString length): {Len}", o.ConnectionString?.Length ?? 0));
```

7. Environment variable forms (Windows and Linux)

```powershell
# Windows PowerShell
$env:Secrets__db__main = "p@ssw0rd-dev"
```

```bash
# Linux/macOS Bash
export Secrets__db__main='p@ssw0rd-dev'
```

## Orchestration compatibility (envRef)

- Exporters prefer references (secretsRefOnly) and emit envRef entries (e.g., APP_DB_PASSWORD → secret://db/main)
- Renderers map envRef to platform-native mechanisms (Kubernetes secretKeyRef/ESO, Compose secrets) without injecting raw values

## See also

- ARCH-0050 — secrets management and configuration resolution
- ARCH-0051 — dev alignment and provider-swap guardrails
- reference/secrets.md — module capabilities and adapter options

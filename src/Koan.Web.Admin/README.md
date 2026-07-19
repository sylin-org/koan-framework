# Sylin.Koan.Web.Admin

**See what your Koan application actually composed while you develop it.** The package adds one authenticated,
read-only dashboard over Koan's canonical provenance, health, environment, and bounded process facts. It is active
only in the `Development` host environment.

## Install

```powershell
dotnet add package Sylin.Koan.Web.Admin
```

Keep the application's existing Koan bootstrap:

```csharp
var builder = WebApplication.CreateBuilder(args);
builder.Services.AddKoan();
var app = builder.Build();
await app.RunAsync();
```

The package is discovered from the reference and mounts automatically. It does not add another registration method
or endpoint-mapping step. The application must provide a standard ASP.NET Core authentication scheme. In
Development, the default `KoanAdmin` policy accepts any authenticated user unless the application registers a policy
with that name.

Open `/.koan/admin/`. The package also exposes:

- `/.koan/admin/status` for the complete read-only dashboard payload;
- `/.koan/admin/health` for the current Koan health snapshot.

## Use the application's authorization policy

Register the named policy with ordinary ASP.NET Core authorization:

```csharp
builder.Services.AddAuthorization(options =>
    options.AddPolicy("KoanAdmin", policy => policy.RequireRole("developer")));
```

Or select another policy in configuration:

```json
{
  "Koan": {
    "Admin": {
      "Authorization": {
        "Policy": "LocalDiagnostics"
      }
    }
  }
}
```

## Configuration

| Key | Default | Meaning |
|---|---|---|
| `Koan:Admin:Enabled` | `true` | Enables routes when the host is also in Development |
| `Koan:Admin:PathPrefix` | `.koan` | Moves the UI and both APIs together |
| `Koan:Admin:Authorization:Policy` | `KoanAdmin` | Standard ASP.NET Core policy required by every request |
| `Koan:Admin:Authorization:AutoCreateDevelopmentPolicy` | `true` | Creates the authenticated-user default when the named policy is absent |

For example, `PathPrefix=ops` moves the root to `/ops/admin/`. Route configuration is read during startup; restart
the host after changing it. An invalid prefix or blank policy rejects startup with the configuration correction.

## Safety boundary

Non-Development and disabled hosts return 404. Secrets are always replaced with `********`. User name, command line,
executable and working paths, machine name, and domain are never projected. Authorization is still required in
Development: the environment boundary is not an authentication substitute.

This package does not generate Compose/Aspire topology, scaffold clients, stream logs, mutate resources, expose a raw
manifest, or model a service mesh. Applications and their standard deployment tools own topology; Admin reports only
the runtime truth Koan already knows.

See [`TECHNICAL.md`](./TECHNICAL.md) for the wire, lifecycle, and ownership contract.

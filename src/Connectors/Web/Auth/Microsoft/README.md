# Sylin.Koan.Web.Auth.Connector.Microsoft

Reference = intent for Microsoft identity platform OIDC sign-in. The connector supplies the standard `common` v2.0
authority and minimal profile scopes; Web Auth owns activation and the complete flow.

```powershell
dotnet add package Sylin.Koan.Web.Auth.Connector.Microsoft
```

## Meaningful use

Supply the Entra application credentials the provider console issued:

```json
{
  "Koan": { "Web": { "Auth": { "Providers": { "microsoft": {
    "ClientId": "{MICROSOFT_CLIENT_ID}",
    "ClientSecret": "{MICROSOFT_CLIENT_SECRET}"
  } } } } }
}
```

With `AddKoan()`, start at `GET /auth/microsoft/challenge?return=/`. Register
`https://your-app/auth/microsoft/callback` in the Entra application.

## Guarantees and boundaries

- The reference alone is inert until complete provider configuration exists.
- Explicit complete configuration outranks automatic local providers; incomplete intent stops startup.
- Default authority is multi-tenant `https://login.microsoftonline.com/common/v2.0`. Override `Authority` with a
  tenant-specific URL when the application requires tenant restriction.
- Koan does not infer allowed tenants, app-registration policy, consent, redirect URIs, or secret rotation.

See [TECHNICAL.md](TECHNICAL.md).

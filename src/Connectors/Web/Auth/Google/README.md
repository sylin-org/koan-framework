# Sylin.Koan.Web.Auth.Connector.Google

Reference = intent for Google OIDC sign-in. The connector contributes Google's stable authority, display metadata,
scopes, and priority; Web Auth owns eligibility, scheme creation, endpoints, and reporting.

```powershell
dotnet add package Sylin.Koan.Web.Auth.Connector.Google
```

## Meaningful use

Supply the Google application credentials the provider console issued:

```json
{
  "Koan": { "Web": { "Auth": { "Providers": { "google": {
    "ClientId": "{GOOGLE_CLIENT_ID}",
    "ClientSecret": "{GOOGLE_CLIENT_SECRET}"
  } } } } }
}
```

With the application's normal `AddKoan()`, start at `GET /auth/google/challenge?return=/`. Register
`https://your-app/auth/google/callback` as an authorized redirect URI in Google.

## Guarantees and boundaries

- Merely referencing the connector does not activate an unconfigured Google provider.
- Complete explicit configuration makes `google` eligible and causes it to outrank automatic local providers.
- Incomplete explicit intent fails startup with a correction.
- Default scopes are `openid`, `email`, and `profile`; add broader scopes only when the application needs them.
- Google application registration, consent-screen policy, redirect URIs, and secret rotation remain deployment duties.

See [TECHNICAL.md](TECHNICAL.md).

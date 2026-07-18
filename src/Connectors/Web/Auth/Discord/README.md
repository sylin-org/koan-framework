# Sylin.Koan.Web.Auth.Connector.Discord

Reference = intent for Discord OAuth2 sign-in. The connector contributes Discord's endpoints and minimal identity
scopes; Web Auth owns activation, PKCE, callback handling, claims, cookies, and reporting.

```powershell
dotnet add package Sylin.Koan.Web.Auth.Connector.Discord
```

## Meaningful use

Supply the Discord application credentials the provider console issued:

```json
{
  "Koan": { "Web": { "Auth": { "Providers": { "discord": {
    "ClientId": "{DISCORD_CLIENT_ID}",
    "ClientSecret": "{DISCORD_CLIENT_SECRET}"
  } } } } }
}
```

With `AddKoan()`, start at `GET /auth/discord/challenge?return=/`. Register
`https://your-app/auth/discord/callback` in the Discord application.

## Guarantees and boundaries

- The reference alone does not activate an unconfigured provider.
- Complete explicit configuration outranks automatic local providers; incomplete intent stops startup.
- Default scopes are `identify` and `email`.
- Guild membership, Discord API access, token persistence, consent, callback registration, and secret rotation are not
  inferred or managed by this connector.

See [TECHNICAL.md](TECHNICAL.md).

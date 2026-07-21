# Sylin.Koan.Web.Auth.Connector.Google — technical contract

The module registers one `AuthProviderDefinition`:

- ID `google`; protocol `oidc`; priority `200`;
- authority `https://accounts.google.com`;
- scopes `openid`, `email`, `profile`;
- automatic activation `false`.

Web Auth overlays `Koan:Web:Auth:Providers:google`, requires `ClientId` and `ClientSecret`, and seeds an ASP.NET
`OpenIdConnectHandler` only when the compiled provider is eligible. The callback is
`/auth/google/callback`; challenge is `/auth/google/challenge`.

The connector has no middleware, controller, startup filter, election logic, static registry, or credential access.
It reports only its module identity; the Web Auth provider plan reports the realized decision.

Common failures are an unregistered callback URI, invalid credentials, denied consent, or an issuer/tenant policy
outside this connector's scope. Provider tokens are not persisted for later Google API calls.

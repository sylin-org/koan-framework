# Sylin.Koan.Web.Auth.Connector.Test

A self-hosted OAuth2/OIDC simulator for meaningful local and integration testing. Reference it and `AddKoan()` makes
two automatic providers available in Development: `test` and `test-oidc`.

```powershell
dotnet add package Sylin.Koan.Web.Auth.Connector.Test
```

Start the maintained OIDC flow at:

```text
GET /auth/test-oidc/challenge?return=/
```

The local login page lets a developer choose a subject, roles, permissions, and custom claims. The resulting cookie
travels through the same Web Auth scheme, callback, claim mapping, external-identity link, and lifecycle pipeline used
by real providers.

## Useful surfaces

- login UI: `/.testoauth/login.html`;
- OIDC discovery: `/.testoauth/.well-known/openid-configuration`;
- OAuth2 authorize/token/userinfo: `/.testoauth/authorize`, `/token`, `/userinfo`;
- JWKS: `/.testoauth/jwks`.

Use `prompt=login` or `prompt=select_account` to force persona selection. Application logout also clears the local
persona cookie.

## Guarantees and boundaries

- Automatic only when `TestProviderOptions.IsActive` is true: Development by default, or explicit enablement outside
  Development.
- Stable attribute-routed protocol endpoints; no configurable route base or startup-order dependency.
- Container and reverse-proxy flows preserve the browser-visible issuer while discovery, token, userinfo, and JWKS
  calls use the application's internal bound address automatically; no Docker hostname setting is required.
- The provider is a protocol simulator, not a security or production identity system. Do not enable it in production.
- Personas persisted in browser LocalStorage are developer convenience, not durable identity data.

See [TECHNICAL.md](TECHNICAL.md).

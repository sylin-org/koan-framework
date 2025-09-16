---
title: Koan.Web.Auth.TestProvider — README
description: Development-only OAuth-like TestProvider for local sign-in flows with roles/permissions/custom claims.
---

Koan.Web.Auth.TestProvider is a development-only identity provider to simulate sign-in flows locally. It issues short-lived auth codes and bearer tokens, exposes a simple login UI, and lets developers inject roles, permissions, and custom claims for testing authorization.

Highlights
- Supports Authorization Code flow for local scenarios (no refresh tokens).
- Login UI with persona persistence (local storage) and a lightweight cookie to skip re-login.
- Query-driven roles/permissions/custom claims injection with sensible caps.
- Strict redirect_uri whitelist and PKCE method enforcement for safer defaults in dev.

Minimal setup
1) Reference the project and add MVC controllers:
  - ProjectReference → src/Koan.Web.Auth.TestProvider/Koan.Web.Auth.TestProvider.csproj
  - services.AddControllers().AddApplicationPart(typeof(Koan.Web.Auth.TestProvider.Controllers.StaticController).Assembly)
2) Map the TestProvider controller routes (honors RouteBase from options):
  - app.MapKoanTestProviderEndpoints(); // requires using Koan.Web.Auth.TestProvider.Extensions
3) Configure options (at minimum AllowedRedirectUris and ClientId):

Inputs/Outputs (contract)
- Inputs (authorize): response_type=code, client_id, redirect_uri, optional: scope, state, code_challenge (+ code_challenge_method=S256), prompt=login/select_account
- Output (authorize): 302 redirect to redirect_uri with code and optional state
- Error modes: 400 invalid_redirect_uri | unauthorized_redirect_uri | unsupported_code_challenge_method; 401 when client_id mismatches; 404 when disabled outside Development

Examples
- Whitelist by absolute URL or by exact path:
  - AllowedRedirectUris: ["https://app.localhost/callback", "/signin-test"]
  - authorize (default base): /.testoauth/authorize?response_type=code&client_id=test-client&redirect_uri=https://app.localhost/callback
  - authorize (default base): /.testoauth/authorize?response_type=code&client_id=test-client&redirect_uri=https://host.any/signin-test

Notes
- PKCE: when code_challenge_method is supplied, only S256 is accepted.
- Endpoint base is configurable via Koan:Web:Auth:TestProvider:RouteBase (default "/.testoauth").
- The login UI is served at {RouteBase}/login.html and forwards to authorize.
- Central logout in Koan.Web.Auth clears the TestProvider dev cookie to avoid silent re-login.

See also
- Reference: Web Authentication — docs/reference/web-auth.md
- Engineering front door — docs/engineering/index.md
- Troubleshooting: container callback rewrite and double-prefix authorize UI issues under docs/support/troubleshooting/

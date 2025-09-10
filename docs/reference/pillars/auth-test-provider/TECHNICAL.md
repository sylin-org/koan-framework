---
title: Sora.Web.Auth.TestProvider — TECHNICAL
description: Design and operational reference for the development-only TestProvider.
---

Routes
- Conventional routing is used via an endpoint mapping extension that honors `RouteBase`.
- GET {RouteBase}/login.html — static login page (served from wwwroot or embedded resource)
- GET {RouteBase}/authorize — issues authorization code and redirects to redirect_uri
- POST {RouteBase}/token — exchanges code for a short-lived access token
- GET {RouteBase}/userinfo — returns a profile for the provided bearer token

Options (TestProviderOptions)
- Enabled: bool (default false). Outside Development, endpoints return 404 unless explicitly enabled.
- RouteBase: string (default "/.testoauth"). Base for all routes.
- ClientId: string (default "test-client"). Single dev client.
- ClientSecret: string (default "test-secret"). Used by token exchange in dev.
- ExposeInDiscoveryOutsideDevelopment: bool (default false). Whether to reveal in discovery.
- AllowedRedirectUris: string[] (default empty). Whitelist for authorize redirect:
  - Absolute entries must match redirect_uri AbsoluteUri exactly.
  - Relative entries must start with '/' and match redirect_uri AbsolutePath exactly (host-agnostic).
  - If the list is empty, all redirects are rejected.
- Caps/DX knobs: MaxRoles, MaxPermissions, MaxCustomClaimTypes, MaxValuesPerClaimType
- UI defaults: DefaultRoles, PersistPersona (login UI behavior)

Security and validation
- redirect_uri validation is mandatory: invalid format → 400 invalid_redirect_uri; not whitelisted → 400 unauthorized_redirect_uri.
- PKCE: if code_challenge_method is provided, only S256 is accepted; otherwise → 400 unsupported_code_challenge_method. The code_challenge is stored with the issued code; token endpoint acknowledges PKCE fields.
- Logging hygiene: authorization logs omit secrets and redirect URIs; only non-sensitive identifiers (client_id) are logged.
- Prompt handling: prompt=login/select_account clears the dev cookie and forces the login UI.

Claims injection (authorize)
- roles=csv or sora.roles=csv (multi-value aware)
- perms=csv | permissions=csv | sora.permissions=csv
- claim.{type}=csv supports repeating keys and values are deduped with caps.

Extensibility
- Replace the login HTML by copying wwwroot/testprovider-login.html into your app; the StaticController serves the local file first, then embedded.
- Override options via configuration at `Sora:Web:Auth:TestProvider`.
- Map endpoints in your composition root: `app.MapSoraTestProviderEndpoints();` (namespace `Sora.Web.Auth.TestProvider.Extensions`).

Operational notes
- Development-only component; do not deploy to production.
- Central logout (Sora.Web.Auth) clears the TestProvider cookie (_tp_user) as a best-effort to avoid auto-login loops.

References
- Web Authentication: docs/reference/web-auth.md
- Engineering front door: docs/engineering/index.md
- Decisions: WEB-0035 entitycontroller transformers (payload shaping), ARCH-0042 per-project companion docs

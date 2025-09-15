# Web Auth TestProvider: callback fails in container (token endpoint resolves to localhost:8080)

Purpose
- Diagnose and fix a container-only callback failure where the OAuth token/userinfo calls resolve to http://localhost:8080 and fail with connection refused.

Symptoms
- Sign-in via the dev TestProvider appears to work until the callback; then the app logs show attempts to call:
  - http://localhost:8080/.testoauth/token
  - http://localhost:8080/.testoauth/userinfo
- The request fails with connection refused when running inside a container or behind a reverse proxy.

Context
- Component: `Koan.Web.Auth` (OAuth/OIDC-like flow managed by `AuthController`).
- Provider: `Koan.Web.Auth.TestProvider` (development-only).
- Environment: Containerized app or reverse-proxied Kestrel where the internal bound address/port differs from localhost:8080.

Root cause
- The callback path assembled absolute URLs for the provider endpoints using a hardcoded fallback to port 8080 when the current server address wasn’t available.
- In containers, `localhost:8080` is not reachable from inside the container. The app needed to derive its own served base URL from runtime server configuration rather than a fixed port.

Resolution (code)
- `AuthController.BuildAbsoluteServer(...)` was updated to resolve the base server URL in this order:
  1) Prefer `ASPNETCORE_URLS` (first address),
  2) Else use `IServerAddressesFeature` (first address),
  3) Else fall back to the current HTTP request’s scheme/host/port,
  - No hardcoded ports.
- With this change, token/userinfo URLs are built against the app’s actual bound address, working in containers and host environments.

Affected versions
- Affected: Builds before the change dated 2025-08-30.
- Fixed: dev branch after 2025-08-30. Pull latest or update to a version with the fix.

Workarounds (if you’re on an older build)
- Set `ASPNETCORE_URLS` to the correct internal binding (e.g., http://0.0.0.0:8080) so the resolver picks it up.
- Or configure Kestrel to advertise the desired address via `IServerAddressesFeature`.
- Avoid relying on `localhost` inside containers; ensure the callback builds absolute URLs using in-container addresses.

Verification steps
- Run your app in the container with `ASPNETCORE_URLS` set to its bound address.
- Trigger TestProvider login and inspect logs for `tokenEndpointResolved` or similar diagnostics.
- Confirm resolved URLs point to the app’s actual internal address (not localhost:8080) and that the token/userinfo calls succeed.

References
- Web Authentication reference: ../../reference/web-auth.md
- Source: `src/Koan.Web.Auth/Controllers/AuthController.cs` (base URL resolution).

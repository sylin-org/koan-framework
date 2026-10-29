---
id: SEC-0027
slug: the-consent-flow-lives-under-the-base-path
domain: Security
status: Accepted
date: 2026-10-29
title: The authorization server's consent flow lives under the base URL's path
related:
  - SEC-0006
  - WEB-0078
  - WEB-0080
---

# SEC-0027: The authorization server's consent flow lives under the base URL's path

## Context

WEB-0078 put the authorization server's issuer and every advertised endpoint under the base URL's path, and the Device
flow's `verification_uri` and done redirect followed, since they are absolute URLs built on the public origin. Two
paths the server writes for the browser were still rooted at `/`: `/oauth/authorize` redirected to the bare consent path
(`Location: /consent?rid=…` under `https://example.test/notes`), and the browser-binding cookie that the approval must
present was set at `Path=/oauth`. Under a path the person landed outside the application, and an approval posted to
`/notes/oauth/request/{rid}/approve` never carried the cookie, so the code flow could not complete. The request
projection's provider `challengeUrl` links were rooted at `/` as well.

Measured by a consumer whose base URL is `https://example.test/notes`.

## Decision

**Every path Koan writes for the browser is application-relative and gets the application's path base exactly once.**
The path base is the configured base URL's (`KoanWebBaseUrl.PathBase`, canonical as configured, so a browser that typed
another casing is still sent to the canonical path the cookie names), else the request's. Web Auth's
`ApplicationPaths.Under` is the one implementation; it prefixes even when the path begins with the path base's own
segment, so `/oauth/request/{rid}/approve` under `/oauth` is `/oauth/oauth/request/{rid}/approve`.

1. `/oauth/authorize` redirects to the consent path under the path base (`/notes/consent?rid=…`).
2. The browser-binding cookie is set and cleared at `{path base}/oauth` (`/notes/oauth`).
3. The request projection's provider `challengeUrl` and `PendingConsent.Approve` and `Deny` (SEC-0028) are Koan's routes
   under the path base.
4. `ConsentPath` and `DonePath` are application-relative wherever they appear: in the redirect, and in the absolute URLs
   — the device `verification_uri` and the done redirect — which are the public origin (carrying the path base once)
   plus the configured path. A path written with the path base already in it gets it again, everywhere alike.
5. Web Auth follows the same rule: the cookie's login redirect sends a browser to the elected provider's challenge route
   under the path base, and the discovery document's `challengeUrl` carries it. `IAuthProviderCatalog.ChallengePath`
   stays the application-relative route.
6. The default landing of a sign-in or a logout that names no acceptable `return` is `ReturnUrl:DefaultPath`,
   application-relative, under the path base once (`/` under `/notes` is `/notes/`). The failure landing (WEB-0080) is
   application-relative too and gets the configured path base once: under `/me`, `failure=/sign-in` lands on
   `/me/sign-in` and `failure=/me/sign-in` on `/me/me/sign-in`.

## Compatibility

- At the root nothing changes: the path base is empty.
- Under a path, a `ConsentPath` or `DonePath` configured with the path base already in it now gets it twice
  (`/notes/notes/consent`) in the redirect, the `verification_uri` and the done redirect alike; configure them
  application-relative (`/consent`). Under a base URL whose path is `/me`, the default `/me/connect` is
  `/me/me/connect` everywhere.
- Under a path, Web Auth's login redirect and discovery `challengeUrl` now carry the path base; they were root-relative
  and left the application.
- Under a path, a sign-in or logout with no `return` lands on `{path base}/` instead of `/`, outside the application.
- A failure landing that already began with the path base used to be kept; it now gets the base again, like every other
  application-relative path, and it uses the configured base rather than the request's.
- A binding cookie issued at `Path=/oauth` before this change is not found by an approval under the path, as before;
  the person restarts the consent within its ten-minute lifetime.
- `ApplicationPaths` (Web Auth) is added.

## Evidence

- `PathBaseFlowSpec` (Koan.Web.Auth.Server.IntegrationTests, Kestrel under `https://www.example.com/tangent`): authorize
  redirects to `/tangent/me/connect?rid=…` and sets `koan.oauth.{rid}` with `path=/tangent/oauth`, which the cookie jar
  sends to the approval URL; the request projection reports the person signed in; the form approval under the path
  returns the client's redirect with the code and state; the PKCE exchange under the path issues a token for the
  resource whose issuer is the base URL. The Device flow's `verification_uri` is the base URL's consent page and its
  approval lands on the base URL's done page. With the change reverted, the code-flow spec fails at the consent
  redirect (`/me/connect?rid=…`) and the device spec passes, as the consumer observed.
- `OAuthBasePathCollisionSpec` and `AuthBasePathCollisionSpec` (base paths `/oauth` and `/auth`, a configuration-only
  OAuth2 provider): approve is `{base}/oauth/request/{rid}/approve` and returns the code, deny is
  `{base}/oauth/request/{rid}/deny` and returns `access_denied`, the provider's `challengeUrl` is `{base}/auth/corp/challenge`
  and redirects to the provider. Before the rule, approve under `/oauth` and the challenge link under `/auth` lost the
  base.
- `PrefixedConsentPathRuleSpec` (base `/notes`, `ConsentPath=/notes/consent`) and `MeBaseConsentPathRuleSpec` (base `/me`,
  default paths): the authorize redirect, the `verification_uri` and the done redirect agree on `/notes/notes/consent`
  and `/me/me/connect`. Before the rule they disagreed.
- `LoginRedirectUnderPathSpec` (Koan.Web.Auth.Integration.Tests, bases `/app` and `/auth`): a browser asking a protected
  page is redirected to `{base}/auth/{provider}/challenge` with `return={base}/edge/protected`, the challenge answers,
  and every discovery `challengeUrl` is `{base}/auth/{id}/challenge`. Before the rule both specs fail.
- `ReturnLandingSpec.No_return_lands_on_the_application_root` (Koan.Web.Auth.Integration.Tests, base `/tangent`, the dev
  Test provider): a sign-in round trip and a logout with no `return` land on `/tangent/`. `FailureLandingUnderPathSpec`
  (base `/me`): a refusal at the provider lands `failure=/sign-in` on `/me/sign-in?error=access_denied` and
  `failure=/me/sign-in` on `/me/me/sign-in?error=access_denied`. `ReturnUrlPolicyTests.The_default_landing_gets_the_base_once`
  (Koan.Web.Auth.Tests): `/` is `/notes/`, `home` is `/notes/home`, an absolute default stays. Before the change the
  no-return round trip, its logout and the `/me/sign-in` landing fail.
- `BaseUrlPathSpec` and the root flow specs still pass.

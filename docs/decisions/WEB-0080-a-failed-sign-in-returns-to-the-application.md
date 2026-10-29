---
id: WEB-0080
slug: a-failed-sign-in-returns-to-the-application
domain: Web
status: Accepted
date: 2026-10-29
title: A failed sign-in returns to the application's own page with a sanitized code
related:
  - WEB-0043
  - WEB-0071
  - WEB-0078
  - SEC-0001
---

# WEB-0080: A failed sign-in returns to the application's own page with a sanitized code

## Context

Web Auth owned where a completed sign-in returns to — `return` on the challenge, checked by `ReturnUrlPolicy` against
`Koan:Web:Auth:ReturnUrl` — and nothing about where a failed one lands. Each handler answered a failure on its own
terms: the atproto connector wrote its own HTTP 400 page when discovery or PAR could not start, with a "Return to
sign-in" link to `/` that ignored the base path, and answered `{"error":"atproto_authentication_failed"}` as JSON
when a callback could not be verified; the maintained OAuth2 and OIDC handlers threw, which the exception handler
turned into a 500. An application that renders its own sign-in page could not get the person back to it, and could
not tell them what happened without reading the provider's body, which must never reach the browser.

Measured by a consumer that renders one sign-in page as a resource of its own and runs under a base path.

## Decision

**Web Auth carries one failure-return convention; every handler consumes it.**

1. **The application names the landing.** Per challenge, beside `return`: `GET /auth/{provider}/challenge?return=
   /home&failure=/sign-in`, carried to the handler in `AuthenticationProperties.Items[".koan.failure"]`. For every
   provider, in configuration: `Koan:Web:Auth:ReturnUrl:FailurePath`. The per-challenge value wins. A landing is a
   rooted, application-relative path (`/sign-in`, `/sign-in?from=home`), placed under the application's path base
   exactly once — the configured base URL's, else the request's (amended by SEC-0027; this record first kept a landing
   that already began with the exact path base) — so a sign-in under `/tangent` lands on `/tangent/sign-in`,
   `/TANGENT/x` on `/tangent/TANGENT/x` and `/tangent/sign-in` on `/tangent/tangent/sign-in`. It must stay there and be a valid header: it is written in printable ASCII from the characters
   a path and a query carry (RFC 3986 `pchar`, `/`, `?`, `%`), and after percent-decoding it names no host (`//`), no
   `.` or `..` segment, no backslash, no encoded slash or backslash in its path, no control character and no
   character outside printable ASCII, so `/../admin`, `/%2e%2e/admin`, `/a%2Fb` and `/é` are refused; a fragment is
   refused; and its query does not name `error`, which carries the code. A landing the rule refuses answers 400 at
   the challenge, naming the rule without echoing the value, and in configuration fails the start naming the key.
2. **The code is a fixed vocabulary.** `SignInFailure` names five outcomes and `SignInFailureReturn.Code` their
   query values: `provider_unreachable` (the provider could not be reached or is not a usable provider),
   `handle_unresolved` (the account identifier could not be resolved), `access_denied` (the person refused at the
   provider), `callback_mismatch` (state, correlation or issuer did not match the sign-in that was started) and
   `sign_in_failed` (everything else). The landing receives it as `?error=<code>`, appended to the landing's own
   query, with `Cache-Control: no-store`. The provider's response, the exception and its message stay server-side.
3. **`SignInFailureReturn` is the one owner.** `Landing(context, properties)` resolves the per-challenge value then
   the configured one; `Redirect(context, properties, failure)` writes the redirect and answers true, or leaves the
   response untouched and answers false when the application named nothing. A handler asks it first and keeps its
   own answer otherwise, so an application that names no landing sees today's behaviour.
4. **The atproto connector classifies at the phase that failed.** `AtprotoSignInException` carries the
   `SignInFailure`: identity resolution (`HandleUnresolved`), authorization-server discovery, metadata and the
   pushed request (`ProviderUnreachable`), an unknown or expired state, a failed correlation or an issuer mismatch
   (`CallbackMismatch`), a callback answering our own state with `error=access_denied` (`AccessDenied`) or another
   error (`Failed`). Both failure sites — the challenge that cannot start and the callback that cannot be verified
   — send the person to the landing when one is named. When none is, the challenge page stands with its link under
   the request's path base, and the callback answers the JSON 400 as before, now `no-store`. The missing-scopes page
   keeps its own text, since it tells the person their previous sign-in was kept, with its link under the path base.
5. **The seeded OAuth2 and OIDC handlers honour the same landing.** `AuthSchemeSeeder` sets `OnRemoteFailure`
   (`Failed`) and `OnAccessDenied` (`AccessDenied`) on both; each handles the response only when a landing is named,
   so the maintained handlers' own failure posture is unchanged otherwise.

## Compatibility

- Additive. A challenge without `failure` and a configuration without `FailurePath` compose and answer as before,
  except that the atproto failure page's link now carries the base path and the JSON 400 carries `no-store`.
- `failure` is a new query parameter on the challenge; a caller that passes one the landing rule refuses gets 400.
- `ReturnUrlOptions.FailurePath`, `AuthConstants.Query`, `AuthConstants.Items`, `SignInFailure`,
  `SignInFailureReturn` and `ReturnUrlPolicy.UnderPathBase` are added; `AuthSchemeSeeder`'s private copy of the
  latter moved to `ReturnUrlPolicy`. `AuthController.Challenge` gains a parameter. The atproto connector's
  `AtprotoSignInException` is internal and derives from `InvalidOperationException`, so an existing expectation of
  that type still holds.

## Evidence

- `FailedSignInReturn` (Koan.Web.Auth.Atproto.Tests, a TestServer host under `https://www.example.com/tangent` with
  a loopback PLC directory and provider admitted through the Development gate): a challenge whose identity cannot
  be resolved lands on `/tangent/sign-in?error=handle_unresolved` and one whose provider answers 503 on
  `?error=provider_unreachable`, both `no-store` and without the provider's body; a callback with an unknown state
  lands on the configured landing with `callback_mismatch`; a real pushed request followed by a callback answering
  our state with `error=access_denied` lands on `/tangent/sign-in?from=home&error=access_denied`; an absolute, a
  protocol-relative, a non-rooted and a backslashed landing, `/../admin`, `/%2e%2e/admin`, `/a/./b`, `/a%2Fb` and
  `/sign-in?error=forged` are refused at the challenge with 400 and no request is pushed; `failure=/TANGENT/x` lands on
  `/tangent/TANGENT/x?error=handle_unresolved`; with nothing named, the challenge answers the 400 page whose link is `/tangent/` and the callback the JSON
  400.
- `FailureLandingSpec` (Koan.Web.Auth.Integration.Tests, Kestrel, the dev Test provider): with `FailurePath` set,
  a callback with a bogus state lands on `/signin?error=sign_in_failed` and a refusal after a real challenge on
  `/signin?error=access_denied`; without it the maintained handler keeps its own answer; a configured landing the
  rule refuses (absolute, `/../admin`, `/%2e%2e/admin`, `/a%2Fb`, `/é`, `/%C3%A9`, `/signin?error=forged`) refuses the
  start naming the key. Kestrel, unlike TestServer, throws on a header outside printable ASCII, so on this host a
  challenge landing of `/é`, a look-alike slash (`U+2215`) or `/%C3%A9` answers 400 rather than a 500 at the failure,
  and an accepted `/signin?from=a%20b` reaches the browser as `Location: /signin?from=a%20b&error=access_denied`.
- `SignInFailureReturnTests` (Koan.Web.Auth.Tests): the rule's table, accepted and refused, each refusal naming its
  problem.
- The rest of both suites, `ProtocolGuards` and the base-URL round trips included, still pass.

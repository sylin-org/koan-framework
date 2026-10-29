---
id: SEC-0029
slug: a-return-is-a-local-path-of-the-application
domain: Security
status: Accepted
date: 2026-10-29
title: A sign-in or logout return is a local path of the application
related:
  - SEC-0001
  - SEC-0027
  - WEB-0043
  - WEB-0080
---

# SEC-0029: A sign-in or logout return is a local path of the application

## Context

`ReturnUrlPolicy` (SEC-0001 §10) accepted any `return` that `Uri.TryCreate(…, UriKind.Relative)` accepts and any
absolute URL with an allow-listed prefix. A protocol-relative value such as `//evil.example/x` is "relative" to that
check, so `GET /auth/{provider}/challenge?return=//evil.example/x` put it in `AuthenticationProperties.RedirectUri`, and
the maintained remote handler, which does not check it again, sent the person there after a successful sign-in — an
open redirect from the application's own sign-in. The same value on `/auth/logout` reached `LocalRedirect`, which
refused it with a 500. `/\evil.example` and percent-encoded forms of both behave the same in browsers.

Found by a review of the K6 slice; the defect predates it.

## Decision

**A `return` is honoured only when it is a local path of this application or an allow-listed absolute URL; anything
else lands on the default landing, never on the value.**

1. A local path starts with exactly one `/` (not `//` or `/\`), is printable ASCII without spaces or backslashes, and its
   path — after percent-decoding, up to three passes so a double-encoded slash is seen — names no host (`//`), holds no
   backslash or control character and no `.` or `..` segment, and begins with the application's path base at a segment
   boundary (SEC-0027), so it stays under the application. `ReturnUrlPolicy.IsLocalPath` answers this rule.
2. An absolute http(s) URL whose prefix is in `Koan:Web:Auth:ReturnUrl:AllowList` is kept, as before (WEB-0043); the
   allow-list is the only way to leave the application.
3. Everything else, and no `return` at all, resolves to the default landing (`ReturnUrlPolicy.DefaultLanding`): an
   application-relative `ReturnUrl:DefaultPath` under the path base once, or an absolute http(s) `DefaultPath` as it is.
4. The challenge and logout both resolve through `ReturnUrlPolicy.Resolve(HttpContext, candidate, ReturnUrlOptions)`, so
   the remote handler only ever receives an accepted landing and `LocalRedirect` only ever sees a local path.
5. The cookie's login redirect names the page asked for under the path base, so it passes the rule.

## Compatibility

- A `return` that is not under the application's path base — `/other/x` under `/notes` — now lands on the default
  landing; it used to leave the application. An application passing application-relative returns under a base path must
  pass them with the base (`/notes/x`), as the cookie's login redirect and Koan's own pages do.
- Protocol-relative, backslashed, encoded-host, dotted and whitespace-bearing returns land on the default landing.
- Logout with such a value answers the default landing instead of 500.
- `ReturnUrlPolicy.Resolve(HttpContext, string?, ReturnUrlOptions)`, `DefaultLanding` and `IsLocalPath` are added; the
  existing `Resolve` gains an optional path base.

## Evidence

- `ReturnLandingSpec` (Koan.Web.Auth.Integration.Tests, base `/tangent`, the dev Test provider driven through challenge,
  authorize and callback): `//evil.example/x`, `/\evil.example`, `https://evil.example`, `%2F%2Fevil.example` and
  `/%2F%2Fevil.example` as `return` land the callback on `/tangent/`, and logout with each answers 302 to `/tangent/`; a
  local path under the base (`/tangent/e2e/whoami`) is kept at sign-in and at logout. Before the change every refused
  value's spec fails.
- `ReturnUrlPolicyTests` (Koan.Web.Auth.Tests): the refused table — the five values above, a double-encoded slash, an
  encoded backslash, a tab, `..` plain and encoded, a sibling path outside the base, a prefix that is not a segment
  boundary and `javascript:` — falls back; `/notes`, `/notes/` and a path with an encoded space, a query and a fragment
  are kept; the existing allow-list specs still pass.

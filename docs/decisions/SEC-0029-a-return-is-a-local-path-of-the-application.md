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
2. An absolute URL is kept only under an entry of `Koan:Web:Auth:ReturnUrl:AllowList` (WEB-0043), and the allow-list
   is the only way to leave the application. Origins are compared, not strings: the URL, as parsed, is http(s), carries
   no userinfo, has the entry's scheme, host and port exactly (a host compared without case in its IDNA form, a default
   port equal to its omission), its path holds no encoded slash or backslash and no `.` or `..` segment once each
   segment is percent-decoded with rule 1's passes, and its path is the entry's path or lies under it at a segment
   boundary. Under the entry
   `https://app.example`, `https://app.example.evil.com/`, `https://app.example@evil.com/`,
   `https://app.example%2F@evil.com/` and `https://app.example:8443/` are other origins and land on the default; under
   `https://partner.example.com/cb`, `/cbx` is not under `/cb`, and `/cb/..%2Fadmin` and `/cb/%2E%2E%2Fadmin` are
   refused, because a partner server that decodes `%2F` before it resolves dot segments serves `/admin`. The redirect
   is built from exactly what was compared — scheme, the host's IDNA (ASCII) form, a non-default port, and the escaped
   path, query and fragment — so the browser receives what passed and the `Location` header is ASCII: a Unicode
   spelling of an allowed host (full-width `ａｐｐ.example`) is the host IDNA maps it to and is sent as `app.example`.
   Canonicalising rather than refusing keeps the rule one comparison: the host the browser would resolve is the one
   compared, and writing it in that form leaves no second spelling to check. An entry is an absolute http(s) URL naming an origin and optionally a path,
   with no userinfo, query or fragment; any other entry refuses the start, naming the key.
3. Everything else, and no `return` at all, resolves to the default landing (`ReturnUrlPolicy.DefaultLanding`): an
   application-relative `ReturnUrl:DefaultPath` under the path base once, or an absolute http(s) `DefaultPath` as it is.
   `DefaultPath` is checked at start: rooted as the landing roots it, it must pass rule 1's local-path rule (without
   the path base, which the landing adds), or be an absolute URL that rule 2 keeps, which the landing then writes in
   rule 2's compared form. `//evil.example`, `/\evil.example`
   or an absolute URL under no entry refuses the start naming the key, so the default landing never leaves the
   application except through the allow-list and logout's `LocalRedirect` never sees a non-local path.
4. The challenge and logout both resolve through `ReturnUrlPolicy.Resolve(HttpContext, candidate, ReturnUrlOptions)`, so
   the remote handler only ever receives an accepted landing and `LocalRedirect` only ever sees a local path.
5. The cookie's login redirect names the page asked for under the path base in its configured spelling, so it passes
   the rule. `UsePathBase` matches the base without regard to case and keeps the request's spelling, so a request to
   `/TANGENT/x` under `/tangent` is named `/tangent/x`: the authentication cookie is scoped to the configured spelling
   and a browser matches a cookie's path by case (RFC 6265 §5.1.4), so landing on `/TANGENT/x` would carry no cookie
   and ask for sign-in again. The local-path rule stays ordinal for the same reason: the configured spelling is the
   one under which the person is signed in. A return the login redirect is handed that does not begin with the
   request's path base is unchanged.

## Compatibility

- A `return` that is not under the application's path base — `/other/x` under `/notes` — now lands on the default
  landing; it used to leave the application. An application passing application-relative returns under a base path must
  pass them with the base (`/notes/x`), as the cookie's login redirect and Koan's own pages do.
- Protocol-relative, backslashed, encoded-host, dotted and whitespace-bearing returns land on the default landing.
- Logout with such a value answers the default landing instead of 500.
- An allow-list entry used to be a string prefix: `https://app.example` admitted `https://app.example.evil.com/`,
  `https://app.example@evil.com/` and another port. It now admits only its own origin, and paths under its path at a
  segment boundary. An allow-listed return reaches the browser as parsed (`HTTP://Host.test/a` as
  `http://host.test/a`).
- An allow-list entry that is not an absolute http(s) origin with an optional path, and a `DefaultPath` that is not a
  local path or an allow-listed URL, now refuse the start; they used to be accepted (an entry that matched nothing, a
  default landing that left the application).
- Under a base path, a protected page asked with another casing of the base (`/TANGENT/x`) used to send the person to
  the default landing after sign-in; they now land on the page, under the base as configured.
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
- Origin comparison and start checks (K8, after a review of K6): `ReturnUrlPolicyTests` refuses, under the entry
  `https://app.example`, `https://app.example.evil.com/`, `https://app.example@evil.com/`,
  `https://app.example%2F@evil.com/`, `https://app.example:8443/`, the `http` scheme and userinfo, and keeps
  `https://app.example`, a path with a query and fragment, and `https://APP.example:443/x` as `https://app.example/x`;
  under `https://partner.example.com/cb` it keeps `/cb` and `/cb/done` and refuses `/cbx`, `/other`, `/cb/../admin` and
  `/cb%2F..%2Fadmin`; it tables the entry rule and the `DefaultPath` rule. `ReturnUrlConfigurationSpec`
  (Koan.Web.Auth.Integration.Tests, Kestrel): an entry with a query, a fragment or userinfo, a bare host and a path
  refuse the start naming `ReturnUrl:AllowList`; `//evil.example`, `/\evil.example` and `https://evil.example/` as
  `DefaultPath` refuse it naming `ReturnUrl:DefaultPath`; `/home` under `/notes` lands a sign-in and a logout with no
  return on `/notes/home`. `ReturnLandingSpec.A_page_asked_with_another_casing_of_the_base_is_where_sign_in_lands`
  (base `/tangent`): `/TANGENT/edge/protected` redirects to sign-in, the callback lands on `/tangent/edge/protected`,
  and that page answers 200. Before the change the four look-alikes were kept, every refused entry and default
  started, and the casing spec landed on `/tangent/`; the `/home` landing passed before and after.
- A review of K8 found two escapes from rule 2, now closed: `ReturnUrlPolicyTests` keeps a full-width spelling of
  `app.example` as `https://app.example/x` (and an escaped path, query and fragment unchanged), always printable ASCII,
  refuses `/cb/..%2Fadmin`, `/cb/%2E%2E%2Fadmin`, a double-encoded form and an encoded backslash under
  `https://partner.example.com/cb`, keeps `/cb/x%20y`, and lands a full-width absolute default on its ASCII form.
  `ReturnUrlConfigurationSpec.A_unicode_spelling_of_an_allowed_host_lands_on_its_ascii_form` (Kestrel, allow-list
  `https://app.example`, `DefaultPath` the full-width spelling plus `/home`): the host starts, the sign-in callback
  lands a full-width `return` on `https://app.example/x` and no `return` on `https://app.example/home`, and logout
  answers 302 to `https://app.example/`. Before the fix the full-width value was kept as typed and the callback
  answered 500 on the non-ASCII header; the encoded-dot paths were kept.

---
id: SEC-0028
slug: a-consent-page-reads-koans-answer
domain: Security
status: Accepted
date: 2026-10-29
title: A server-rendered consent page reads Koan's answer, and the done page knows the outcome
related:
  - SEC-0006
  - SEC-0024
  - SEC-0026
  - SEC-0027
---

# SEC-0028: A server-rendered consent page reads Koan's answer, and the done page knows the outcome

## Context

SEC-0006 gives the application two pages and one seam for them, `GET /oauth/request/{rid|user_code}`, a JSON projection
for a page that runs in the browser. An application that renders its consent page on the server had no in-process
form: it read `ConsentRequest`, `DeviceCode` and `OAuthClient` itself, copied the internal user-code normalising and
the request route to build its Allow and Deny actions, and rate-limited typed codes with its own key, so its page and
Koan's endpoint had separate allowances. And the Device flow's approve and deny both landed on the same done URL with
nothing saying which happened, so the done page could not tell the person what they had decided; a plain query
parameter would let anyone link a person to a page claiming an approval that never happened.

Measured by a consumer that renders its consent and done pages on the server.

## Decision

**`ConsentPage` is the in-process seam: it finds the pending request, says where its forms post, and reads the
outcome only this server wrote.**

1. `ConsentPage.Find(HttpContext, id)` answers a `ConsentLookup`. A pending, unexpired code-flow request is found by its
   `rid`; otherwise one attempt is counted against the address's `UserCodeVerificationRateLimitPerMinute` under the same
   key Koan's own endpoint, approve and deny use (`usercode:{address}`), and a pending, unexpired device request is
   found by the user code normalised as stored (any case, hyphen and spaces ignored). Past the allowance the answer is
   `Throttled`. Ids longer than 128 characters find nothing.
2. `PendingConsent` carries what the page renders and where it posts: the id approve and deny address (the `rid`, or the
   normalised user code), the client id and its registered name (or the id when it has none — untrusted display
   text), the scopes, the resource, the display user code or the client's redirect, and `Approve` and `Deny`, the
   seam's paths under the application's path base (SEC-0027).
3. `GET /oauth/request/{id}` answers from the same lookup, so normalising and the allowance have one owner. Its JSON is
   unchanged except that a client registered without a name is shown by its `client_id`.
4. A Device-flow approval or denial redirects to the done page with `outcome`: a time-limited Data Protection payload
   (purpose `Koan.Web.Auth.Server.ConsentOutcome.v1`, valid for `ConsentRequestLifetime`) holding the outcome and the
   subject of the session that decided. `ConsentPage.Outcome(HttpContext)` answers `Approved` or `Denied` only for a
   value this server protected, unexpired, and — when it names a subject — read by that subject's session; a plain,
   altered, expired or other person's value reads as null. A Device-flow denial, like an approval, needs the person's
   session (`401 login_required` otherwise), so every outcome names the person who decided.

## Compatibility

- Additive for the pages. An application that read the entities itself replaces that code with `ConsentPage.Find` and
  posts to `PendingConsent.Approve` and `Deny`; its own rate limiter goes, since the lookup spends the shared allowance.
- The done URL of a Device-flow decision now carries `?outcome=…`; a done page that ignores its query is unaffected.
- A Device-flow denial now needs a signed-in person; it was accepted from anyone who knew the code.
- `ConsentPage`, `PendingConsent`, `ConsentLookup` and `ConsentOutcome` are added; `AuthServerRoutes.RequestId` names the
  route's placeholder.

## Evidence

- `ConsentPageSpec` (Koan.Web.Auth.Server.IntegrationTests, Kestrel under `https://www.example.com/notes`, the
  application's pages at `/consent` and `/consent/done` built on `ConsentPage`): the consent page found by the redirect
  answers the code-flow request by `rid` with the client's registered name, scopes, resource and redirect and approve and
  deny paths under `/notes/oauth/request/{rid}/`, and posting to `Approve` returns the code; a user code typed in lower
  case without its hyphen finds the device request with its normalised id and display code, and posting to `Approve`
  lands on the done page reading `Approved`; a denial reads `Denied`; a denial without a session answers
  `401 login_required` and leaves the request waiting; an unknown code and an over-long id find nothing;
  `?outcome=Approved`, an altered value and no value read as none, and the genuine approval read by another signed-in
  person or a visitor reads as none.
- `ConsentPageAllowanceSpec` (allowance of two a minute): two lookups through the page spend it, the third is
  throttled, and Koan's own `/oauth/request/{code}` then answers 429.
- `OAuthDeviceFlowSpec`, `OAuthAuthCodeFlowSpec` and `DevExplorerClientSeedSpec` still drive the JSON seam.

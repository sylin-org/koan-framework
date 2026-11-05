---
id: SEC-0040
slug: a-person-is-koans-own
domain: Security
status: Accepted
date: 2026-11-05
title: A person is Koan's own, and binds the logins that arrive to it
related:
  - SEC-0007
  - SEC-0034
  - WEB-0071
  - WEB-0080
---

# SEC-0040: A person is Koan's own, and binds the logins that arrive to it

Amends [SEC-0007](SEC-0007-koan-identity-module.md) D1 and Layer 0 (`Identity.Id` == the claims `sub`) and builds the
follow-on of its decided open question on identity keys: the second provider's challenge and callback.

## Context

`Koan.Identity` made a person's id the subject of the first provider that signed them in. A provider's subject means
nothing outside that provider, and a person who changes the account they sign in with is still the same person. Under that
rule, unbinding a person's first login changed nothing, since its subject was still the person's id; `IdentityLinkService`
verified nothing and no callback called it; and the service-auth bridge signed a DID in with no provider at all.

## Decision

1. **The id is Koan's.** A login seen for the first time makes a person whose id is a GUID v7, the entity's own default,
   and never the provider's subject. `ExternalIdentityLink` (provider, hashed subject, its id composed from both) is the
   one way from a credential back to its person. A first sign-in writes it only if absent (`Entity.Insert`), so two
   concurrent first sign-ins of one login end on one person: the loser of the write reads the winner's. A sign-in no
   provider made is the application's statement, and its subject names the person directly.
2. **A login arrives at one seam.** Every verified login is handed to `LoginArrival.Arrive` before the cookie is signed
   in: the seeded OAuth2 and OIDC handlers, the atproto callback and the service-auth bridge. An ordinary sign-in goes to
   `IExternalIdentityStore.Link`, and arrives as the person the login is bound to; a sign-in while signed in replaces the
   session, as it always did.
3. **Binding is an explicit intent.** A form `POST /me/connections/{provider}/link` (`AuthConstants.Routes.ConnectionsLink`),
   with `return` and `failure` in the form, is the provider's own challenge, carrying the signed-in person in the protected
   properties. Its callback binds the login the provider verified to that person (`IExternalIdentityStore.Bind`), so no
   stray person is made, and the session continues as them carrying the new login's claims, such as `atproto:did` and
   `atproto:handle`.
   - Binding never starts from another site. A POST is what SEC-0039's guard judges, and a page of another site cannot send
     one with the session cookie, which is `SameSite=Lax`; there is no GET. A connector that names an account reads it
     from the form (atproto's `identifier`). Otherwise a page elsewhere could send a signed-in person to bind an account
     its author holds at a provider that approves anything, and then sign in as them.
   - The person who asked must still be the one signed in at the callback, and be themselves: an operator impersonating
     them binds nothing, as for SEC-0007 D8's other identity verbs.
   - The sign-in must be no older than `Koan:Web:Auth:ReConsent:MaxAge` (10 minutes): Web Auth stamps when a cookie's
     person signed in, which a sliding renewal keeps. Otherwise the person signs in again first, so a stolen cookie alone
     binds nothing. `ReConsent:ForceOnLink` is on by default; an application turns it off only by saying so.
   - A person holds at most one login per provider: binding a new one of a provider replaces their earlier one in the
     same step, and the earlier subject's next sign-in arrives as a new person.
   - A login that is another person's stays theirs, and the binding is refused saying why. Nothing merges.
   - A refusal lands on the challenge's failure landing (WEB-0080) with `sign_in_first` or `login_taken`. With none
     named, it answers Koan's Problem with that code as its word and the reason as its sentence: 401 for
     `sign_in_first`, 409 for `login_taken`, at the challenge and at every callback alike, never a server error.
4. **Unbinding never takes the last login.** `IdentityLinkService.Unbind`, served by the self-service console's
   `DELETE /api/identity/me/connected/{linkId}`, unbinds one of the person's own logins and refuses their last with 409.
   The unbound subject's next sign-in arrives as a new person.
5. **The bridge follows the same lifecycle.** `AtprotoServiceAuth.SignIn` names its provider to the cookie
   (`AuthConstants.Items.Provider`), so a proved DID lands on the person its login is bound to, and an unbound one makes a
   person, as a browser's callback does.
6. **Grants stay keyed by DID.** `AtprotoSession` names no person.

## Compatibility

A person stored before this record has the subject of their first login as their id, and that login still resolves to
them, so they keep working unchanged; nothing re-keys them. `ExternalIdentity.UserId` is now `Subject`, which a login read
back from a store does not carry. `IExternalIdentityStore.Unlink`, which nothing called and which keyed a login by its
person, is gone: unbinding is Koan.Identity's. Web Auth's in-memory store knows no people and refuses to bind.

The development seed of SEC-0007 (a dev user, alice and bob) is gone, with the posture that existed only for it
(`Koan:Identity:Posture`, `SeedDevUsers`, `DevUser`). A seeded person held no login, so no sign-in could reach them, and
the Test connector makes alice at her first sign-in with no network, as it makes anyone. Binding their Test-connector
logins instead would have put that connector's provider ids into Koan.Identity.

## Limits

Not built: merging people, binding by proof without a browser, and a public lookup by provider and subject. Known and
held, each rare:

- Two bindings of one provider by one person at the same instant can each remove the other's new login.
- Two unbindings of a person's last two logins at the same instant can leave them none.
- An OIDC provider without a userinfo endpoint never reaches the arrival, so its binding signs in as an ordinary sign-in.
- A callback path cased differently from its provider (`/auth/GOOGLE/callback`) names the provider one way at the
  cookie's sign-in and another at the arrival.
- Ending an impersonation signs the operator in again, which counts as a recent sign-in.
- Unbinding is allowed while impersonating.
- Binding relies on SEC-0039's cross-site guard to refuse a POST from another site: an application that turns the guard
  off leaves binding open to its own sibling subdomains, whose pages share the site and so send the `SameSite=Lax`
  cookie.
- A store must support insert-only writes (`DataCaps.Write.InsertOnly`: SQLite, MongoDB and in-memory today); another
  refuses the first sign-in with Koan's own correction, not at boot.

## Evidence

`tests/Suites/Integration/Identity/Koan.Identity.Logins.Tests` signs people in through a real `AddKoan()` host: the Test
connector's OAuth2 and OIDC providers on loopback Kestrel, and the atproto connector against a loopback PDS. It proves a
first sign-in's GUID v7, concurrent first sign-ins ending on one person, a second provider's login bound and either signing
in, a new login of a provider replacing the earlier one, another person's login refused, a refusal with no landing
answering its Problem, `ForceOnLink` by default, the last login kept, an ordinary sign-in binding nothing, a cross-site
POST and a navigation binding nothing, an atproto login bound with its claims, and the bridge. `Koan.Identity.Tests` proves
the same at the reconciler and link service.

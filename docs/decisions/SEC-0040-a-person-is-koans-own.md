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
3. **Binding is an explicit intent.** `GET /me/connections/{provider}/link` (`AuthConstants.Routes.ConnectionsLink`) is the
   provider's own challenge, carrying the signed-in person in the protected properties. Its callback binds the login the
   provider verified to that person (`IExternalIdentityStore.Bind`), so no stray person is made, and the session continues
   as them carrying the new login's claims, such as `atproto:did` and `atproto:handle`.
   - The person who asked must still be the one signed in at the callback, and be themselves: an operator impersonating
     them binds nothing, as for SEC-0007 D8's other identity verbs.
   - With `Koan:Web:Auth:ReConsent:ForceOnLink`, the sign-in must be no older than `ReConsent:MaxAge` (10 minutes): Web
     Auth stamps when a cookie's person signed in, which a sliding renewal keeps. Otherwise the person signs in again
     first, so a stolen cookie alone binds nothing.
   - A person holds at most one login per provider: binding a new one of a provider replaces their earlier one in the
     same step, and the earlier subject's next sign-in arrives as a new person.
   - A login that is another person's stays theirs, and the binding is refused saying why. Nothing merges.
   - A refusal lands on the challenge's failure landing (WEB-0080) with `sign_in_first` or `login_taken`. With none
     named, the binding challenge answers 401, and a refusal at the callback is that handler's own failure.
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
person, is gone: unbinding is Koan.Identity's. Web Auth's in-memory store knows no people and refuses to bind. The dev seed
names its people by their ids and gives them no login.

## Limits

Not built: merging people, binding by proof without a browser, and a public lookup by provider and subject. Two bindings of
one provider by one person at the same instant, or two unbindings of a person's last two logins, are not serialized.
A store must support insert-only writes (`DataCaps.Write.InsertOnly`: SQLite, MongoDB and in-memory today); another refuses
the first sign-in with Koan's own correction.

## Evidence

`tests/Suites/Integration/Identity/Koan.Identity.Logins.Tests` signs people in through a real `AddKoan()` host: the Test
connector's OAuth2 and OIDC providers on loopback Kestrel, and the atproto connector against a loopback PDS. It proves a
first sign-in's GUID v7, concurrent first sign-ins ending on one person, a second provider's login bound and either signing
in, a new login of a provider replacing the earlier one, another person's login refused, `ForceOnLink`, the last login kept,
an ordinary sign-in binding nothing, an atproto login bound with its claims, and the bridge. `Koan.Identity.Tests` proves
the same at the reconciler and link service.

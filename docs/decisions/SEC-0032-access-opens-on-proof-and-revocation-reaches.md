---
id: SEC-0032
slug: access-opens-on-proof-and-revocation-reaches
domain: Security
status: Accepted
date: 2026-10-30
title: Access opens on proof, and what is taken from a person reaches what they hold
amends:
  - SEC-0005
  - SEC-0006
  - SEC-0007
related:
  - SEC-0009
  - SEC-0030
  - WEB-0065
  - DATA-0163
---

# SEC-0032: Access opens on proof, and what is taken from a person reaches what they hold

## Context

Measured on the code at `bd70b7b01`, each with a spec that failed there:

1. **A display name opened the tenancy console.** In Closed posture the `Operators` allow-list matched
   `Identity.Name`, a bare `sub` and any email claim. Anyone can set their display name at Google or Discord. The
   role-list file granted roles on an email with no regard for `email_verified`, and every provider's `roles` and
   `claims` became claims, Koan's reserved `koan:*` roles among them.
2. **Roles were frozen into the cookie.** Sign-in stamped the bag's roles, and sliding expiration kept them. Someone
   whose `koan:identity-operator` was removed stayed an operator for as long as they kept using the site.
3. **A refresh replayed a snapshot.** For a grant's 30 days, `/oauth/token` minted tokens with the roles and name
   captured at approval and never asked whether the person could still act. Suspend and deactivate closed cookies only.
4. **Tenant resolution followed registration order.** The claim carrier won because it was registered first, so a
   member of two tenants whose session named A got a request to `/t/b` scoped to A while the page showed B.
5. **Failures were swallowed.** A bootstrap failure was logged and the host came up, although the dispatcher's own
   contract says bootstrap fails closed. Every `InvalidOperationException` from a store read as "no grants".

## Decision

### 1. Access a list grants keys on proof

`SignInProof` (Koan.Web.Auth.Abstractions) names what a sign-in proved. A provider's sign-in stamps
`koan_provider_subject`, `{provider}:{subject}`, before anything rewrites the subject, so a linked person's entry
stays the one they signed in with. `VerifiedEmails` answers only when the provider said `email_verified`.

- The console's `Operators` matches a provider-qualified subject or a verified email, and nothing else.
- The role-list file keys on a verified email alone.
- A provider's `roles`, `permissions` and `claims` map only where it sets `ProviderOptions.MapRolesAndClaims`, off by
  default. An operator opts in for a provider whose roles mean something here; the local simulator opts in.
- `koan:*` roles are stripped from every provider's sign-in, opted in or not. An extra claim never takes a `koan*` type.

The stripping is one chokepoint, `ProviderClaimPolicy`, where a provider's sign-in reaches the cookie. Mapping userinfo
selectively would not be enough: an OIDC id_token's `roles` are role claims before Koan sees them. An application's own
`SignInAsync` is the application's statement and passes untouched. The role-list file runs after the policy, so an
operator's file may still grant a reserved role.

An OAuth2 provider carries no email claim, so a verified-email entry cannot match one. Name it by provider-qualified
subject.

### 2. A request's roles are projected per request

Chosen over stamping a bag version on the cookie. `IdentityAuthFlowHandler.OnValidatePrincipal` already reads the person
and the session on every cookie request, and the compiled bag is a keyed cache read with no lock when warm, so
projection adds almost nothing. A version stamp would need a durable per-person counter written on every role change
and a read to compare it on every request, to arrive at the same read, and it would keep a second copy of the roles
in the cookie to go stale in the ways this decision exists to end.

The cookie carries no role from the bag. Sign-in stamps none, impersonation stamps none, and the projection rides on a
carrier identity of the request's principal. A sliding cookie is reissued from the ticket the handler read, not from
that principal, and a spec renews a cookie to prove nothing freezes.

### 3. Every mint asks the subject's standing

`ISubjectStandingContributor` (Abstractions, `[KoanDiscoverable]`) is one owner's word on a subject: active, and their
name and roles, each optional. `SubjectStandings` (Koan.Web.Auth) composes them without order: any contributor saying
inactive decides, and roles are the union. Two names resolve by contributor type name. Koan.Identity contributes the
person's row and their role rows, both read from the store rather than the compiled bag, because a process that did not
make a change cannot know its bag is stale and a refresh is rare enough to pay for the truth. An application
contributes by implementing the interface: a place's ban or time-out answers `new SubjectStanding(Active: false)`.

The token endpoint asks before every mint, at code redemption, device exchange and refresh, and before spending the
code or token. What no contributor speaks to stands as captured, which is all a host with no owner of its people has.
An inactive subject gets `invalid_grant`, the reason staying the application's. A subject Koan.Identity has no row for
was erased or never signed in, and is inactive.

A refused refresh token is not spent. An agent refused during a time-out resumes with the same token when it ends, and
that is no reuse. A store that cannot answer fails the request and costs the client nothing.

### 4. Closing a person closes their agents

`AgentGrant.RemoveApprovals(subject)` removes a subject's client grants, a page at a time, and each removal forgets its
remembered consent as `Remove()` does. Suspend, deactivate and erase call it through `ClientApprovals.Revoke`, after the
status write that already refuses the refresh. A grant is what the approval list shows and what reactivation would
otherwise restore, so a person reactivated has no agents until they approve again. A report through the seam only
refuses: a time-out ends and the agent resumes. An application whose ban should outlast its lifting calls
`RemoveApprovals` itself.

Under Koan.Tenancy an `AgentGrant` is tenant-scoped, so with Tenancy closed and no tenant in scope `ClientApprovals`
reaches none. The standing still refuses every refresh.

### 5. The address the person sees decides

Chosen over refusing every disagreement. `ITenantResolver.Rank` is explicit: `Address` (subdomain, path), then
`Request` (header), then `Session` (claim), and the lowest rank that names a tenant decides. The ordinary disagreement
is legitimate: a member of two tenants, whose session names the first, opens a page of the second. Refusing would
break moving between one's own tenants, and the page says what the person means. An address naming a tenant the person
is no member of scopes nothing and never falls through. Two carriers of one rank naming different tenants have no order
between them, and only there is the request refused, with 404.

### 6. Failures surface

A flow handler's bootstrap failure is logged and stops the host. `AgentGrantAccessContributor` catches
`SegmentationRequiredException` alone. Indexes on `Session`, `IdentityEmail`, `ExternalIdentityLink`,
`ImpersonationGrant` and `Membership` serve what `/me` and a sign-in read, and a returning sign-in reads its email
factor by the id composed from person and address.

## Consequences

- Stores are rebuilt; nothing migrates. A cookie issued before this change keeps the roles stamped into it until it
  expires, and a deployment that cannot wait rotates its key ring.
- `ITenantResolver` gains a required member, and `ImpersonationClaims.BuildImpersonatedIdentity` and
  `ImpersonationService.BuildSessionAsync` lose their roles parameter.

### Named, not chased

- **An access token outlives what closed its person** by up to its 15-minute lifetime. Refresh is what this decision
  closes; an application that must close sooner evaluates its own standing per request.
- **The compiled bag is per process.** A role change on another process reaches this process's cookies when its bag is
  next compiled, as it reaches everything that reads the bag (SEC-0030). Standing reads the store, so tokens do not
  share this. Cross-process invalidation is the bag's to solve.
- **Role-list and provider roles are as of sign-in.** Removing an address from the file reaches the next sign-in, and
  with a contributor reporting roles, an agent's tokens carry the standing's roles and not these.
- **Refresh token rows keep the name, email and roles last written** until they expire, including for an erased person
  whose grants are gone and whose tokens can no longer be used.
- **A rank's refusal tells a signed-in person a tenant's code exists.** They could already learn that from any carrier.

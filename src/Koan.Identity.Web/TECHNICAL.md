# Sylin.Koan.Identity.Web — technical contract

## Ownership and activation

`SecIdentityWebModule` registers this assembly's MVC controllers, the impersonation response filter, HTTP context
access, and actor attribution. Package reference is activation intent; there is no endpoint contributor, middleware
call, or application controller base to add.

The package owns HTTP authorization and projection only. Durable records and business operations remain in
`Sylin.Koan.Identity` services and Entity statics.

## Route and authority inventory

| Authority | Route group | Operations |
|---|---|---|
| authenticated subject | `/api/identity/me` | profile, emails, connected links, owner unlink, sessions, sign out others |
| `koan:identity-operator` | `/api/identity/admin/identities` | bounded list/search, get, suspend, reactivate, delete |
| `koan:identity-operator` | `/api/identity/admin/identities/{id}/access` | effective view, why/can explanation, global role grant/revoke |
| `koan:identity-operator` | `/api/identity/admin/impersonation` | request, approve, revoke, target view, start, stop |
| application scoped authority | `/api/identity/scoped-roles/{tenant}/{scopeType}/{scopeId}` | bounded roles, assignments, policies, effective access and preview |

Controllers use attribute routing and ordinary ASP.NET `[Authorize]`. Self-service never accepts a subject ID from the
request. The operator role is a global Identity-plane role; Identity Tenancy strips it from membership projection so a
tenant role cannot unlock this host surface. Destructive operator verbs are additionally blocked while impersonating
even if the target principal carries the operator role.

The scoped-role controller accepts only operation DTOs; actor, issuer, tenant, scope, role identity and persisted version
fields come from the authenticated request, route, provider row and `If-Match`. Reads are authorized before their
provider-filtered lookup. Directory filters and pagination must be fully provider handled. Cookie-authenticated unsafe methods validate antiforgery. An actual
non-cookie authenticated identity may use the configured bearer/other scheme without an antiforgery token.
Current-subject effective checks return the same safe denial for an absent scope and a registered scope with no
access. Effective and authorized preview responses intentionally omit role IDs, policy IDs, binding IDs, reasons and
version provenance.

Authority freshness is a provider re-read immediately inside the one-shot lifecycle permit before adapter dispatch.
It is not a serializable cross-record transaction between the authority row and the target row; providers that need
that stronger guarantee must supply a shared transactional integration rather than infer it from this pre-dispatch
check.

## Impersonation session behavior

Request requires a reason. Approval clamps lifetime to 1–480 minutes and requires an approver other than the actor.
Start verifies that the caller owns an active approved grant, resolves the target's effective roles, and signs in a
principal containing target subject plus the real `koan_actor`. Cookie validation in Identity core rechecks the grant.
Stop restores an actor principal and effective roles through the normal cookie sign-in path.

`ImpersonationBannerFilter` adds `X-Koan-Impersonating` to MVC action responses while acting-as. It does not cover
static files or responses short-circuited before MVC.

## Audit attribution and reporting

`HttpContextActorAccessor` supplies the real operator subject to Identity audit hooks, preferring the actor claim while
impersonating. Background operations without a request may remain unattributed under the core best-effort contract.

Startup reporting exposes the self-service and operator route groups as tools. It does not report identity records,
roles, provider links, or other personal data.

## Unsupported and deferred

- bundled HTML/SPA consoles;
- personal access token issuance or authentication;
- group management or group-derived access;
- a provider-link initiation/callback UI;
- target-owned impersonation review/revoke self-service;
- database-native text search/pagination across large identity sets;
- non-MVC impersonation banner coverage.
- cross-scope role/assignment directories and arbitrary predicate-based authority projection;
- assignable-role directories, pending an authority predicate that can be pushed before paging;
- scoped audit reads, pending a non-forgeable append/read owner rather than generic `AuditEvent` access;
- serializable cross-record authority + target commits;
- bundled scoped-role settings UI.

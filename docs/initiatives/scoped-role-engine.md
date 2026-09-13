---
type: SPEC
domain: identity
title: "Scoped role engine — reusable authorization for Koan applications"
audience: [maintainers, framework-authors, application-authors, ai-agents]
status: implemented-poc
last_updated: 2026-09-13
validation:
  status: implementation-verified
  scope: Identity, Web, SQLite and Mongo receipts described below
---

# Scoped role engine

## Charter

Koan supplies a domain-neutral, Discord-like scoped role engine. Applications declare capabilities,
scope structure, mandatory guards and authority contributors. Administrators define roles, manage
membership collections and set action audiences inside those boundaries. The engine owns persistence,
authorization, compiled evaluation, explanation and provider-safe queries.

Tangent Space is the first intended consumer, but the framework contains no Tangent-specific policy.
Consumer adoption and UI remain outside this repository change.

## Public membership contract

Scoped role membership is collection membership:

```csharp
await roles.Add(new ScopedRoleMember(scope, subject, roleId), ct);
await roles.Remove(new ScopedRoleMember(scope, subject, roleId), ct);
```

The element identity is exactly tenant + scope type + scope ID + role/group ID + subject. `Add` and
`Remove` are idempotent. A duplicate add and an absent or repeated remove return `false`; an actual
change returns `true`. Successful no-ops do not emit lifecycle events.

The persistence model is one internal participant row for each exact tenant + scope + subject. It
contains separate `Roles` and `Groups` sets and is physically deleted when both sets become empty.
It is not a generic Entity resource and is never exposed by the management API.

Membership at an ancestor scope applies to registered descendants. The registered scope tree is the
only inheritance mechanism; there is no per-member inheritance switch. Role and policy versions remain
only on those independently mutable resources.

## Read projections

The engine exposes two authorized, collection-shaped reads:

- `Memberships(subject, scope)` returns that participant's visible `Roles` and `Groups` at the exact
  scope.
- `Members(roleId, scope, page, pageSize)` returns a provider-bounded page of subject identifiers and
  an exact total for the exact scope.

These methods are projections, not mutable resources. They expose no persistence identity, lifecycle
state or concurrency token. Authority `RoleIds` ceilings filter the projections. The role-members query
requires complete provider filter pushdown and provider-bounded paging, preventing management clients
from needing per-participant reads or an in-process scan.

## Logical model

| Concept | Meaning |
|---|---|
| Subject | Stable identity independent of credential, runner, model or device |
| Capability | Application-declared, namespaced action supported by selected scope types |
| Scope | Tenant-bound location in a bounded, single-parent registered tree |
| Role definition | Named set of correlated capability grants, with mutable status and presentation |
| Participant membership | Role and group sets for one subject at one exact scope |
| Policy | Nearest explicit audience replacement for one capability, or inheritance |
| Authority envelope | Complete ceiling for one administrative operation |
| Access plan | Immutable decision, membership tokens, audience and safe provenance |

Role names convey no intrinsic power. Capability-bearing roles use `role:*`; grantless audience groups
use `group:*`. `permission:*` tokens are compiled results and cannot be persisted as membership input.

## Evaluation semantics

For a subject, capability and target scope, the engine:

1. resolves the tenant-bound target and bounded ancestry;
2. loads participant collections for those exact scopes;
3. ignores disabled or retired role definitions;
4. finds the nearest explicit policy replacement for the capability;
5. otherwise evaluates applicable role grant clauses;
6. evaluates each clause as a complete conjunction and alternatives as OR;
7. intersects the result with every live mandatory application guard.

An explicit empty replacement audience means nobody. Role/group audience selectors use the current
compiled members, so role or policy edits affect existing members immediately. Conditions preserve
their correlated clause shape; independent scalar limits are never combined into broader authority.

## Mutation and freshness safety

Every mutation obtains the actor from the trusted accessor, normalizes tenant/scope/subject/role input,
loads fresh scope and role state, selects one complete authority envelope, and revalidates its proof at
the lifecycle boundary immediately before provider dispatch. Direct generic writes to the internal
membership model are rejected by lifecycle enforcement.

A host-owned keyed coordinator serializes changes to the same participant collection. This makes
concurrent same-process duplicate adds/removes converge to one actual change and one lifecycle event.
It is deliberately not described as a distributed transaction. Multi-process deployments must use
their provider/integration boundary for stronger cross-host coordination and broadcast external domain
version changes to every process.

`Entity.Role.MemberAdding` and `MemberRemoving` may veto. `MemberAdded` and `MemberRemoved` run after
durable change and snapshot invalidation. A post-handler failure is reported while the durable change
remains committed. Recursive role mutation from a lifecycle handler is rejected.

## Compiled access path

Runtime checks use a bounded host-owned LRU of immutable target-scope snapshots. Single-flight builds
read bounded ancestry, participant collections, referenced role definitions and tenant policies, then
publish subject-to-roles/capabilities and role-to-members indexes. Membership, role, policy and external
domain changes evict affected descendant snapshots. An invalidation racing an in-flight build prevents
the stale generation from being returned.

`Check`, `Plan`, `Preview`, `Constrain`, `Query`, `QueryWithCount`, `Get` and `Count` share this access
plan. Resource queries conjoin tenant and scope constraints before provider dispatch and reject adapters
that cannot push the full filter.

## Web complement

Referencing `Koan.Identity.Web` exposes authenticated routes under:

```text
/api/identity/scoped-roles/{tenantId}/{scopeType}/{scopeId}
```

Membership routes are collection operations:

```text
PUT    roles/{roleId}/members/{subject}
DELETE roles/{roleId}/members/{subject}
GET    roles/{roleId}/members?page=1&pageSize=50
GET    members/{subject}
```

The mutation routes have no request body, concurrency header or membership response payload. Both
return `204` for actual changes and valid no-ops. The reads return subject pages or role/group arrays.
Role and policy resources keep their own optimistic concurrency contract. Cookie mutations require
antiforgery; other authenticated schemes follow the application's configured transport posture.

## Bounds and configuration

```jsonc
{
  "Koan": {
    "Identity": {
      "ScopedRoles": {
        "MaxAncestryDepth": 16,
        "MaxMembersPerScope": 1024,
        "MaxDirectoryPageSize": 100,
        "MaxCompiledSnapshots": 1024
      }
    }
  }
}
```

Identifiers, names, descriptions, presentation metadata, clauses and scalar parameters also have
fixed or configured bounds. Nested parameter objects and arrays are rejected.

## Verification and current boundary

The 2026-09-13 correction is covered by:

- Identity: duplicate add, absent/repeated and concurrent remove, event-once/no-op behavior, physical
  empty-row deletion, descendant access, immutable snapshot invalidation and direct-write rejection;
- SQLite: collection persistence, exact-scope/tenant isolation, physical deletion, authorized
  participant projections, provider-pushed role-member filtering, paging and exact total;
- Mongo: collection persistence, exact-scope/tenant isolation, physical deletion and provider-bounded
  member paging/count;
- Web + SQLite TestServer: idempotent bodyless mutations, member reads, no membership concurrency or
  resource fields, authentication, antiforgery, role/policy concurrency and safe denial shapes.

The engine is a proven framework POC, not a claim of distributed serializability or complete provider
coverage. InMemory deliberately does not advertise provider-bounded directory paging. Assignable-role
directories, scoped audit reads, a bundled settings UI and a non-forgeable scoped audit owner remain
outside the current surface.

## References

- [SEC-0004](../decisions/SEC-0004-capability-authorization-gate-constrain-project.md)
- [SEC-0007](../decisions/SEC-0007-koan-identity-module.md)
- [SEC-0008](../decisions/SEC-0008-access-enforcement-at-the-data-layer.md)

---
id: SEC-0021
slug: resource-overwrites-resolve-in-discord-order
domain: Security
status: Accepted
date: 2026-10-29
title: A resource allows and denies beyond what roles grant, through overwrites resolved in Discord's order
related:
  - SEC-0007
  - SEC-0009
  - DATA-0131
amended_by: SEC-0030
---

# SEC-0021: A resource allows and denies beyond what roles grant, through overwrites resolved in Discord's order

> **Amended by [SEC-0030](SEC-0030-bounds-belong-to-reads.md) (2026-10-30): the role model's bounds belong to its
> reads.** Resolution, its order, the deciding fact, revoke, events, the guard and the deferred-scope refusal stand.
> These parts are replaced: `MaxOverwritesPerResource` and `MaxOverwritesPerSubject` are removed, and no overwrite
> write is refused for a count; a bag is evaluated over `RoleCollection.OverwritesFor(bag, resource)` — the resource's
> role overwrites, compiled per resource, and the person's own, read by key — while `Overwrites(resource, after, take)`
> and `OverwritesOf(...)` are paged listings; `PermissionOverwriteAccessContributor` is removed, so the identity-wide
> effective-access view carries no overwrite facts and `AccessExplainer.WhyAsync` reads them for the resource asked;
> the caches evict instead of emptying; and a role's members are `RoleMember` rows, so `MaxMembersPerRole` and its
> 409 are gone. The text below records the decision as it was made.

## Context

The server role model ([scoped-role-engine](../initiatives/scoped-role-engine.md)) compiles one bag per person: the
roles they are members of and the `global:*` permissions those roles grant. Nothing expressed "on this resource,
this role or this person may, or may not". Every application with containers — channels, boards, projects,
folders — needs exactly that, and Discord's permission overwrites are the battle-tested shape: per channel, an
allow and a deny for a role or a member, resolved in a documented order
(<https://discord.com/developers/docs/topics/permissions#permission-overwrites>). A consumer composing the step
itself beside Identity met five defects in the same code:

1. `RoleBagCache` compiled only member roles, so the `everyone` role's own permissions never reached a bag that
   holds the `everyone` token.
2. `RoleAccessContributor` emitted role permissions as Kind `permission` while `EffectiveAccess.Capabilities`
   selected Kind `capability`, so role permissions never appeared in the effective view.
3. `RoleBag`'s constructor was internal, so a pure evaluation over a bag could not be built or tested outside Identity.
4. `AccessExplainer` could not name the fact that decided a missing permission.
5. A `RoleCollection` change inside a deferred `EntityContext.Transaction` scope returned as if it had committed:
   its before- and after-events ran, its bags were invalidated and recompiled from the uncommitted store, and its
   write was held for the commit, where the role lifecycle guard — whose permit lives in the call that issued the
   write — refused it, so the commit failed. Measured on SQLite with an atomic scope: `Add` returned the member,
   the bag compiled inside the scope lacked the role, and `Commit` threw `TransactionException` wrapping
   `EntityLifecycleCancelledException` ("Role is managed by RoleCollection").

## Decision

**`PermissionOverwrite` is the role model's per-resource allow and deny.** One row per resource and subject:
`ResourceId` (the application's, opaque to Identity), `SubjectKind` (`Role` or `Person`), `SubjectId` (a role key,
`everyone` for everyone, or a person id), `Allow` and `Deny` (`global:*` permissions), and an optional `ExpiresAt`
after which it no longer applies. `RoleCollection` is its only mutation owner, as it is the role's:

- `Overwrite(resourceId, subjectKind, subjectId, allow, deny, expiresAt?)` sets it, `RemoveOverwrite(...)` removes it,
  `Overwrites(resourceId)` reads a resource's overwrites from the compiled cache as copies. A role subject other than
  the implicit `everyone` and `authenticated` must exist. A permission outside `global:*`, the administrator
  permission, an overwrite that names nothing, or an expiry already past is refused before any write.
- Bounds fail closed with the key to change: `MaxOverwritesPerResource` (256) and `MaxOverwritesPerSubject` (4096)
  refuse a new row with `RoleBoundException`, an `InvalidOperationException` that names the bound's
  `ConfigurationKey`, as `MaxMembersPerRole` now refuses a member; a replacement always lands. Reading a resource whose rows exceed the bound throws. Each of allow
  and deny is bounded by `MaxPermissionsPerRole`.
- `Entity.Role.OverwriteChanging/Changed` and `OverwriteRemoving/Removed` publish around each change; a before-event
  may veto. The role lifecycle guard covers the overwrite: a generic `Save()` or `Remove()` is refused.
- Deleting a role deletes the overwrites that name it, every before-event first, so a role defined again under the
  key starts with none. The implicit roles' overwrites stay, because those roles hold every bag with or without a row.
- A person's overwrites are core-owned person rows: identity erasure removes them and counts them as
  `permission-overwrites`. Changes self-audit with a privacy-safe snapshot that omits a person subject's id.

**Resolution is one pure call over loaded rows.** `bag.On(resourceId, overwrites, now)` returns
`EffectivePermissions` with no I/O and no clock:

1. Base: the bag's permissions — its member roles' and the implicit roles' (defect 1, below).
2. The `everyone` overwrite: its deny is removed, then its allow is added.
3. The overwrites of the other roles the bag is in, `authenticated` included: the union of their denies is removed,
   then the union of their allows is added.
4. The person's own overwrite: deny, then allow.

A role the bag is not in, another person, another resource and an expired row contribute nothing. A permission in
both lists of one step is held, as on Discord. **`global:administrator`** (`RoleTokens.Administrator`) in the base
holds every permission on the resource and no overwrite applies; only a role grants it. `Role.CanDo`, the
resource-free intersection, is unchanged and does not expand it. Discord's implicit permissions (a missing
view hiding everything else) and its guild owner are the application's to apply.

**The deciding fact.** `EffectivePermissions.Explain(permission)` and `AccessExplainer.Explain(identityId, resourceId,
permission)` return the explainer's `AccessDecision`. Every public resolution and explanation call — `AccessExplainer.WhyAsync`
included — trims its permission, resource id and role key once at entry, so `" global:post"` and `"global:post"`
decide alike; a role key that cannot name a role (blank or too long) is simply not held, and its row contributes
nothing. For a missing permission the one contributing fact is the
deny of the most specific step that withheld it with no allow at that step or after — Kind `deny`, naming the
overwrite row, its subject and its resource — or, when no overwrite withheld it, a Kind `ungranted` fact that no
held role grants it, which no row backs. Within one step the deciding row is the first by subject id. An
application says "the Guests role is denied Post in The workshop" from the fact's subject, value and resource.

**The contributor.** `PermissionOverwriteAccessContributor` emits the overwrites that apply to a person as facts on
their resource: an allowed permission as Kind `permission`, a withheld one as Kind `deny`, each with `RowType`
`PermissionOverwrite`, `RowId` the row and `Subject` the role or person. Kinds and the global scope are named in
`AccessFactKind` and `AccessFactScope`. `AccessExplainer.WhyAsync` compares an overwrite fact's resource id exactly,
as resolution does; an agent-grant fact keeps the comparison its gate uses (`AgentGrant.AppliesTo`, without case).

**A revoke removes exactly one fact.** One overwrite row backs several facts, so `AccessFactRef` carries the fact's
`Kind` and `Value` beside `RowType`, `RowId` and `Subject`, and `AccessFact.Ref` builds that handle.
`AccessExplainer.RevokeAsync` takes the one permission out of the row's allow list (Kind `permission`) or deny list
(Kind `deny`) and leaves the rest of the row as it was; the row is removed only when both lists are then empty. So
revoking an allow never lifts a deny, and revoking a deny lifts that deny alone. A handle on an overwrite that does
not name its kind and a valid permission, or whose id cannot name a row, revokes nothing and answers false.

**The cache.** Overwrites compile per resource, and an overwrite change invalidates its resource, not bags.
`RoleBagCache` holds one compiled overwrite set per resource beside the bags, bounded by `MaxCachedResources` (1024),
single-flight and invalidated by generation; a warm read is cache-only. A bag holds no overwrite, so an overwrite
change leaves every bag valid; a role change invalidates the bags of the people it affects, as before. This is
where the decision departs from invalidating "the affected people" on an overwrite change: compiling overwrites
into bags would copy every resource's `everyone` overwrite into every bag, and an audience change would flush them
all, while the resource's own set is the exact unit an overwrite change affects.
`InvalidateOverwrites(resourceId)` and `InvalidateAll()` are public beside `Invalidate(person)` for a multi-process
host's own propagation.

**The five defects.**

1. A bag compiles the permissions of the implicit roles it holds: `everyone` for every bag, `authenticated` for an
   authenticated one. A change to either role invalidates every bag, the anonymous visitor's included, which is
   now compiled and cached like any other.
2. `EffectiveAccess.Capabilities` is everything the identity can do somewhere: Kind `capability` and Kind
   `permission`, each fact naming its resource; a deny is resolved per resource, not subtracted there. The role
   contributor also reports the permissions of the implicit roles that exist, with no membership to revoke. It
   never reports an implicit role as a role fact: a sign-in stamps no role claim for `everyone` or `authenticated`,
   and `AccessExplainer.CanAsync` and an impersonation session build their principal from the role facts, so they
   carry exactly the role claims a real sign-in of that person carries.
3. `RoleBag(personId, authenticated, roles, permissions)` is public; it adds the implicit roles, refuses a permission
   outside `global:*`, and exposes `Roles` and `Permissions` beside `Tokens`. `RoleBag.Anonymous`, which could not
   carry the `everyone` role's permissions, is removed.
4. The deciding fact above.
5. A role or overwrite change inside a deferred scope is refused with `NotSupportedException` before any event,
   write or invalidation, naming the correction: make the change after `EntityContext.Commit()`, outside the scope.
   The change is a read-modify-write under the collection's lock with events and invalidation around one write;
   Koan's transaction law already refuses a write that cannot be deferred (DATA-0131), and deferring this one would
   publish events and invalidations for a write the scope may never commit. A change asked for by a role lifecycle
   handler is refused before any lock is taken, rather than waiting on the lock its caller holds.

**Routes.** `Koan.Identity.Web` mounts `api/identity/overwrites` beside the role routes, operator-gated:
`GET ?resource=`, `PUT {role|person}/{subjectId}?resource=` with `{ allow, deny, expiresAt }`, and
`DELETE {role|person}/{subjectId}?resource=`. The resource id travels in the query, so any id fits. A bound answers
409 with the correction, an unknown role 404, a malformed overwrite 400. The role descriptor names the
administrator permission and the new bounds. A member bound on `PUT api/identity/roles/{key}/members/{personId}`
answers the same 409 with its correction. Only a `RoleBoundException` answers 409; any other failure of the change,
a lifecycle handler's own exception included, keeps its own answer.

**Writes are refused while impersonating (SEC-0007 D8).** Every overwrite write and every role write — the role
routes' put, patch, delete, member add and member remove, and the access console's role revoke — answers 403 with
the guard's answer while the caller carries an actor claim. `ImpersonationGuard.DangerousActions` gains
`role.manage` and `overwrite.manage`, each enforced by those routes.

## Compatibility

- `RoleBag`'s internal three-argument constructor is replaced by the public four-argument one, and
  `RoleBag.Anonymous` is removed; `RoleCollection.Bag(null, false)` returns the compiled anonymous bag.
- A bag now carries the permissions of an existing `everyone` or `authenticated` role, so an application that
  loaded the `everyone` role beside the bag stops doing so. An application that defined those roles with permissions
  it did not mean to grant everyone sees them granted.
- `EffectiveAccess.Capabilities` now includes role permissions and overwrite allows; the role contributor adds facts
  for existing implicit roles.
- A `RoleCollection` change inside a deferred scope now throws `NotSupportedException` where it used to fail at commit.
- `RoleCollection.Delete` removes the role's overwrites. Erasure receipts carry one more count.
- `AccessFactRef` gains optional `Kind` and `Value`; a handle on an overwrite without them revokes nothing.
  `AccessExplainer.WhyAsync` matches a non-grant resource id exactly where it used to ignore case.
- Role and overwrite writes answer 403 while impersonating; the member bound answers 409 where it answered 500, with
  a message that names the key to change. The write bounds throw `RoleBoundException`, which derives from
  `InvalidOperationException`, so a caller that caught the latter still does.
- `RoleOptions` gains `MaxOverwritesPerResource`, `MaxOverwritesPerSubject` and `MaxCachedResources`.
  `PermissionOverwrite` is a new stored entity; nothing existing is migrated.

## Evidence

- `PermissionOverwriteSpec` (Koan.Identity.Tests, real `AddKoan()`, offline): Discord's seven documented cases as a
  theory, each missing permission naming its deciding row; administrator; the ungranted fact; a role not held,
  another person and another resource contributing nothing; expiry at the instant; the role step's union and its
  deterministic deciding row; the explainer and revoke through the fact's ref; the contributor's facts and its
  expiry; both bounds and a replacement at the bound; malformed overwrites; resource invalidation with bags left
  valid; events, veto and the guard; recursion refused without waiting; role deletion.
- Defect specs, each failing before the change: `RoleCollectionSpec.Everyone_role_permissions_compile_into_every_bag_and_its_change_reaches_cached_bags`
  (the bag lacked the permission), `IdentityAccessSpec.Role_permissions_appear_among_the_effective_capabilities`
  (Capabilities was empty), `RoleSqliteSpec.A_role_change_inside_an_atomic_scope_refuses_before_any_write_and_after_commit_the_bag_is_correct`
  (no exception was thrown), `ConsumerBagSpec` (Koan.Identity.Web.Tests, no internals access; did not compile:
  CS1729), and the explainer case above (the method did not exist).
- `RoleSqliteSpec` and `RoleMongoSpec` round-trip an overwrite through SQLite and MongoDB; `RoleWebSpec` drives the
  routes through TestServer; `IdentityErasureSpec` erases a person's overwrite.
- Review findings, each spec failing before its change: `PermissionOverwriteSpec.Revoking_one_fact_of_an_overwrite_removes_only_that_fact_and_never_lifts_a_deny`
  (revoking an allow lifted the row's deny), `PermissionOverwriteSpec.A_padded_permission_names_the_same_deciding_fact`
  (the deny was reported as ungranted), `PermissionOverwriteSpec.Why_compares_an_overwrite_resource_id_exactly_as_resolution_does`
  (another resource's deny was reported), `IdentityDayTwoSpec.Simulated_principals_carry_exactly_the_role_claims_a_real_sign_in_carries`
  (`everyone` and `authenticated` were role claims), `IdentityDayTwoSpec.Role_and_overwrite_writes_are_refused_while_impersonating_and_succeed_otherwise`
  (the writes succeeded under impersonation), and `RoleWebSpec.A_member_bound_answers_409_with_its_correction`.
- Re-review findings, each failing before its change: the padded `WhyAsync` case in
  `PermissionOverwriteSpec.Why_compares_an_overwrite_resource_id_exactly_as_resolution_does` (no fact was reported),
  the non-permission handle in `PermissionOverwriteSpec.Revoking_one_fact_of_an_overwrite_removes_only_that_fact_and_never_lifts_a_deny`
  (it threw `ArgumentException`), `PermissionOverwriteSpec.A_row_whose_role_key_cannot_name_a_role_contributes_nothing`
  (it threw `ArgumentException`), and `PermissionOverwriteSpec.Only_a_bound_answers_409_and_a_handler_failure_keeps_its_own_exception`
  (a handler's `InvalidOperationException` answered 409).

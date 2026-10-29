# Koan.Identity technical notes

`RoleCollection` is the sole mutation owner for `Role` and `PermissionOverwrite`. A lifecycle guard rejects direct
generic saves and removes of either. Per-role coordination serializes same-process role edits; one lock serializes
same-process overwrite edits, taken after the subject role's lock when the subject is a role. Mutations persist
first, invalidate the compiled state they affect, then publish post-events. A change is refused before any lock when
a role lifecycle handler asks for it, and before anything happens inside a deferred `EntityContext.Transaction`
scope (SEC-0021): the scope would hold the write until its commit while the events and the invalidation ran now.

`RoleBagCache` holds two bounded, single-flight maps. Each invalidation increments an entry's generation and
replaces it; a build only returns when its generation is still current.

- Bags, one per person and one for the anonymous visitor. Compilation performs one bounded query for roles whose
  `Members` collection contains the person and one read of the implicit roles the bag holds (`everyone`, and
  `authenticated` for a person). It adds the member roles' stable keys plus the literal `global:*` grants of those
  roles and of the implicit roles. A role change invalidates its previous and current members; a change to an
  implicit role invalidates every bag.
- Overwrite sets, one per resource. Compilation performs one query bounded by `MaxOverwritesPerResource` and fails
  closed when the resource holds more. An overwrite change invalidates its resource; it never touches a bag, which
  holds no overwrite. `RoleCollection.Overwrites` hands out copies.

Authorization is pure: `Role.CanDo(PermissionCriteria, RoleBag)` intersects tokens, and
`RoleBag.On(resourceId, overwrites, now)` resolves a resource in Discord's order — base, `everyone` overwrite, the
union of the other held roles' overwrites, the person's overwrite; deny before allow within each step;
`global:administrator` in the base holds everything and skips overwrites. It records, per missing permission, the
row of the latest step that denied it, which `EffectivePermissions.Explain` returns as the deciding fact.

New overwrites check both bounds with exact counts under the overwrite lock; replacing a row checks none. A revoke
through the explainer takes one permission out of one list under the same lock and saves the rest, or removes the
row when nothing is left; it publishes the same events as any overwrite change. Deleting
a role removes the overwrites that name it after every before-event has run, the overwrites first and the role last,
so a failure leaves the role in place for a retry. The implicit roles' overwrites are never cascaded.

This model deliberately has no binding entity, scope tree, policy replacement, tombstone, ETag, or request-time
membership query. Multi-process hosts must propagate invalidation through their deployment integration —
`Invalidate(person)`, `InvalidateAll()` and `InvalidateOverwrites(resourceId)` are public for that; the in-process
cache does not claim distributed coherence.

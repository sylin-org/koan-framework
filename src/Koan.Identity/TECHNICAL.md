# Koan.Identity technical notes

`RoleCollection` is the sole mutation owner for `Role`, `RoleMember` and `PermissionOverwrite`. A lifecycle guard
rejects direct generic saves and removes of each. Per-role coordination serializes same-process changes to a role
and its members; joins of one person are serialized by a striped person lock, taken after the role's lock, so the
roles-per-person bound holds in-process; one lock serializes same-process overwrite edits, taken after the subject
role's lock when the subject is a role. No path takes a role's lock after another lock, so none can deadlock. Mutations persist first, invalidate the compiled state they affect, then publish post-events. A
change is refused before any lock when a role lifecycle handler asks for it, and before anything happens inside a
deferred `EntityContext.Transaction` scope (SEC-0021): the scope would hold the write until its commit while the
events and the invalidation ran now.

A role's row id is `Role.KeyFor(key)`, composed from its key; `Role.Key` is the token, and every read and write of a
role by key goes through the composed id and requires the key read back to match exactly.

What grows is rows, read a page at a time (SEC-0030). `RoleMember` is one row per role and person, its id composed
from both, indexed by person and by role then person; adding or removing a member writes or deletes that row.
`PermissionOverwrite` is one row per resource and subject, its id composed from both, indexed by resource, kind and
subject and by kind, subject and resource. Every id is composed from length-prefixed parts and encoded strictly, and
every key refuses control characters and unpaired surrogates, so no two keys compose one id.

`RoleReads` owns every read of what grows. A store may compare keys by its own collation — one that ignores case
matches `Doc` when asked for `doc` — so each read that feeds authorization keeps only the rows whose keys equal the
asked ones ordinally, after the store answers: a resource's role overwrites, a person's own overwrite read by key, a
role's members, a person's memberships and the roles they name, and the administration listings. Each listing is
ordered by its key and then by row id, which every paged read breaks ties with. Its cursor is the last row the store
returned for the page — that row's own key and id — and the next page is the rows whose key sorts after the cursor's,
or equals it with a later id, as the store compares them; of those it keeps the exact matches, so a page can hold
fewer rows than asked while a cursor follows. A resource's listing reads its role rows, then its person rows, and its
cursor names the kind it is reading.

`RoleBagCache` holds three single-flight maps, each invalidated by generation: an invalidation replaces an entry with
a newer generation, and a build only returns when its generation is still current. Each map's capacity sizes memory.
Admitting a key past capacity evicts one entry by a clock sweep: the hand spares an entry read since it last passed
and clears its mark, so the entry evicted is one nothing read for a full turn; the entry being admitted is never the
victim. A read that finds its entry takes no lock.

- Bags, one per person and one for the anonymous visitor. Compilation reads the person's memberships by person —
  at most `MaxRolesPerPerson`, failing closed beyond it — the roles they name, and the implicit roles the bag holds
  (`everyone`, and `authenticated` for a person). It adds the member roles' keys plus the literal `global:*` grants of
  those roles and of the implicit roles. A membership change invalidates that person; defining a role or changing
  its permissions invalidates every bag rather than reading its members; deleting a role invalidates every bag when
  it clears the role's permissions, and each member again as their membership row goes.
- Role overwrites, one set per resource, keyed by role: compilation reads the resource's role rows a page at a time.
  There is at most one per role, so the set is bounded by the roles the application defines.
- A person's own overwrite on a resource, or its absence, read by key.

`RoleCollection.OverwritesFor(bag, resource)` hands out copies of the role rows for the roles the bag is in and of
the person's own row; an overwrite change invalidates the role set of its resource or the one person's entry, never a
bag. `InvalidateOverwrites(resourceId)` invalidates both kinds on one resource.

Authorization is pure: `Role.CanDo(PermissionCriteria, RoleBag)` intersects tokens, and
`RoleBag.On(resourceId, overwrites, now)` resolves a resource in Discord's order — base, `everyone` overwrite, the
union of the other held roles' overwrites, the person's overwrite; deny before allow within each step;
`global:administrator` in the base holds everything and skips overwrites. It records, per missing permission, the
row of the latest step that denied it, which `EffectivePermissions.Explain` returns as the deciding fact. A row the
bag cannot meet contributes nothing, so resolving over `OverwritesFor` equals resolving over every row on the resource.

Overwrites carry no count bound; each allow and deny list is bounded by `MaxPermissionsPerRole`. A revoke through the
explainer takes one permission out of one list under the overwrite lock and saves the rest, or removes the row when
nothing is left; it publishes the same events as any overwrite change. Deleting a role runs its before-event and
every overwrite's before-event, a page at a time, before any write. It then neutralizes the role — clears its
permissions, takes the allows out of its overwrites, removing those that only allowed, and invalidates every bag —
removes each membership, removes the overwrites that remain, which only deny, and removes the role last, publishing
each overwrite's after-event as it goes. No write takes away a deny while the role still grants anything; once
neutralized, a failure leaves the members not yet removed holding only the role's denies, in place for a retry. The
implicit roles' overwrites are never cascaded.

Erasure removes a person's membership rows a page at a time, never through the read bounded by `MaxRolesPerPerson`,
so it finishes for a person that processes sharing a store left over the bound.

The identity-wide effective-access view lists roles, their permissions and agent grants; an overwrite holds on one
resource, so `AccessExplainer.WhyAsync` and `Explain` read it for the resource asked about.

This model deliberately has no binding entity, scope tree, policy replacement, tombstone, ETag, or request-time
membership query. Multi-process hosts must propagate invalidation through their deployment integration —
`Invalidate(person)`, `InvalidateAll()` and `InvalidateOverwrites(resourceId)` are public for that; the in-process
cache and locks do not claim distributed coherence.

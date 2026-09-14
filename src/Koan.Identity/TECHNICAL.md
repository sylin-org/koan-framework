# Koan.Identity technical notes

`RoleCollection` is the sole mutation owner for `Role`. A lifecycle guard rejects direct generic saves and removes.
Per-role coordination serializes same-process collection edits. Mutations persist first, invalidate every affected
person bag, then publish post-events.

`RoleBagCache` is bounded and single-flight per person. Each invalidation increments a person generation and evicts
the entry; a build only returns when its generation is still current. Compilation performs one bounded query for
roles whose `Members` collection contains the person. It adds the stable role keys plus the roles' literal
`global:*` grants. Authorization is the pure intersection `Role.CanDo(PermissionCriteria, RoleBag)`.

This model deliberately has no binding entity, scope tree, policy replacement, tombstone, ETag, or request-time
membership query. Multi-process hosts must propagate role invalidation through their deployment integration; the
in-process cache does not claim distributed coherence.

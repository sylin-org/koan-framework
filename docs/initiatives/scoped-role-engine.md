---
type: SPEC
domain: identity
title: "Server role collections and compiled person bags"
audience: [maintainers, framework-authors, application-authors, ai-agents]
status: implemented-poc
last_updated: 2026-09-14
validation:
  status: implementation-verified
  scope: Identity, Web, SQLite and Mongo receipts
---

# Server role collections and compiled person bags

## Outcome

The earlier scoped-role prototype was superseded by one semantically minimal model: one server `Role` collection,
one hot compiled `RoleBag` per person, one `PermissionCriteria` (`AnyOf` in this POC), and one pure
`Role.CanDo(criteria, bag)` intersection. There are no bindings, scoped role entities, audience/replacement policies,
tombstones, or per-request membership reads.

## Architectural card

- **Intent:** let applications express Discord-like role/group membership and resource-selected permissions without
  importing application scope or policy concepts into Identity.
- **Expression:** stable role/group keys, direct `Members`, literal `global:*` grants, opaque bounded metadata, and a
  criteria selector applied after a resource is loaded.
- **Guarantee:** bounded/fail-closed cold compilation; cache-only hot authorization; mutation events and affected-bag
  invalidation; role rename never changes the key.
- **Coalescence:** removes both the global `IdentityRole` binding and the greenfield `ScopedRole*` hierarchy. One
  mutation surface and one bag compiler remain.
- **Ergonomics:** `RoleCollection.Define/Add/Remove/Bag`, `PermissionCriteria.Any`, and the optional Identity.Web role
  catalog/member endpoints.

## Token semantics

Tokens pass through unchanged: `role:member`, `group:gardeners`, `global:topic_read`, `global:post_create`, and
`global:post_remove`. A `global:*` grant is never wrapped in `permission:*`. Every bag contains `everyone`, and an
authenticated person's bag also contains `authenticated`.

## Web complement and proof

`/api/identity/roles` supplies a descriptor, bounded list/search/page, role CRUD, member list/add/remove, and the
current-person bag. Role metadata round-trips for application presentation such as purpose/color and has no policy
meaning. Focused Identity tests cover pure intersection, permission derivation, rename-stable keys, invalidation,
anonymous/authenticated tokens, hot-path reuse, lifecycle behavior, and the loaded-resource hook. SQLite and Mongo
receipts cover provider persistence/compilation; TestServer covers authentication, operator bounds, metadata,
membership, and current-person bags.

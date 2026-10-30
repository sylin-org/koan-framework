---
id: SEC-0030
slug: bounds-belong-to-reads
domain: Security
status: Accepted
date: 2026-10-30
title: The role model's bounds belong to its reads, never to what an application may hold
amends:
  - SEC-0021
related:
  - SEC-0007
  - SEC-0021
  - DATA-0107
---

# SEC-0030: The role model's bounds belong to its reads, never to what an application may hold

## Context

The server role model ([scoped-role-engine](../initiatives/scoped-role-engine.md), SEC-0021) turned storage and
caching shortcuts into limits an application's own users met:

1. **Members lived inside the role.** `Role.Members` was a list on the role's document. Adding a member rewrote the
   whole document under the role's lock, and `MaxMembersPerRole` (4096) existed only to keep that document small. A
   consumer whose place gives every participant a member role found that participant 4097 could not post: the join
   threw `RoleBoundException`. Every join cost the size of the role, and every join contended on one row.
2. **A person's roles were found by scanning membership lists.** The bag compiler and `ForPerson` queried
   `Role.Members.Contains(personId)`, which no index serves.
3. **Overwrites were read per subject, across every resource.** Evaluating a person read the overwrites of their
   roles and of them across all resources, bounded by `MaxOverwritesPerSubject` (4096). The implicit `everyone` role
   holds one overwrite per resource, so that bound capped how many resources an application could have; the consumer
   documented "at most 1000" of its containers because of it. `MaxOverwritesPerResource` (256) separately bounded a
   resource's compiled set, which held every person's overwrite on it.
4. **Caches emptied themselves.** `CompiledStateMap` cleared every entry once it reached capacity. Past
   `MaxCachedBags` people or `MaxCachedResources` resources, every miss emptied the cache; the consumer sized its
   container bound below the cache to avoid the cliff.

Measured on the unchanged code at `51b2c33` (Koan.Identity.Tests, InMemory): the 4097th of 5000 joins threw
`RoleBoundException` naming `MaxMembersPerRole`; with `MaxOverwritesPerSubject` at the consumer's 1000, the 1001st of
2000 `everyone` overwrites threw it naming `MaxOverwritesPerSubject`; and a bag cache of three, after a fourth bag,
kept none of the first three warm.

## Decision

**Bounds belong to reads, never to what an application may hold.**

- Every read is bounded by paging, or by the size of the one thing it evaluates.
- A collection that grows is rows, never a list inside one document.
- A cache evicts entries, never clears.
- Configuration sizes memory and nothing a person can meet.
- Bounds that stay are properties of the model — the roles one person holds, the permission vocabulary of a role or
  an overwrite list, a role's metadata — never stand-ins for storage.

### 1. Membership is rows

`RoleMember` is one person's membership of one role: `RoleKey`, `PersonId`, `AddedAt` and `AddedBy`. Its id is
`RoleMember.KeyFor(roleKey, personId)`, composed from both, so adding the same member twice writes the same row. It is
indexed by person, and by role then person. `RoleCollection` is its only mutation owner and the role lifecycle guard
refuses a generic save or remove of it, as it does a role's.

- **Writes.** `Add(roleKey, personId)` writes one row and answers it; `Remove(roleKey, personId)` deletes one and
  answers whether there was one. `MemberAdding`/`MemberAdded` and `MemberRemoving`/`MemberRemoved` fire with their
  meaning unchanged, only when a row is written or deleted, and the person's bag is invalidated. A membership whose
  role no longer exists is removed without events, since no role is left to name. Each row self-audits as
  `rolemember.created` or `rolemember.deleted` against the person, its privacy-safe snapshot naming the role and the
  time, never the person.
- **`Role.Members` is gone.** The role document keeps its key, name, permissions, metadata and timestamps.
- **A role's row id is composed from its key.** `Role.Key` is the authorization token, and the row id is
  `Role.KeyFor(roleKey)`, composed and hashed like every other row id here. A store that compares ids by a
  case-ignoring collation would otherwise make `Define("Admin")` overwrite `admin`'s row — its name and permissions,
  and with them what every member of `admin` holds. Filtering reads cannot undo a write that merges, so every read and
  write of a role by key goes through the composed id, and the key read back must equal the asked one exactly.
- **A person's roles** are one read of their membership rows by person and one read of the roles they name:
  `ForPerson` and the bag compiler. The roles one person holds stay bounded by `MaxRolesPerPerson`, a property of the
  model: `Add` refuses one more with `RoleBoundException` naming its key, and a store holding more fails the bag
  closed rather than compiling part of it. Joins of one person are serialized within a process — a lock per person,
  striped so its memory stays fixed, taken after the role's lock — so the bound holds on one node. Processes sharing
  a store do not coordinate: two of them can each admit a person's last role and leave them one over. That fails
  closed — their bag refuses to compile until a role is removed — and never grants more; erasure still removes every
  membership of such a person, a page at a time, never through the bounded read.
- **A role's members** are `Members(roleKey, after, take)`: a page in person order, `take` at most `MaxPageSize`,
  each page continuing from the cursor the previous one returned (`RoleMemberPage.Next`).
- **Membership tests** are the bag's `InRole`, or one read of `RoleMember.KeyFor`.
- **`MaxMembersPerRole` is removed**, with every path that threw on it.
- **What a role change invalidates, chosen:** defining a role or changing its permissions invalidates every bag,
  the anonymous one included, instead of reading the role's members; such changes are rare, and a bag compiles from
  two bounded reads. A rename or a metadata change invalidates nothing, since a bag holds role keys and permissions
  only. A membership change invalidates the one person.
- **Deleting a role fails closed at every step.** The role's before-event and every overwrite's before-event run before
  any write. Then the role is neutralized: its permissions are cleared, and the allows are taken out of each of its
  overwrites — one that only allowed is removed, one that also denies keeps its denies — and every bag is
  invalidated. Then its membership rows go, a page at a time, each member's bag invalidated as their row goes; then the
  overwrites that remain, which only deny; and the role last. Each overwrite's after-event is published as it is
  removed. No write takes away a deny while the role still grants anything, so no state between the writes grants
  more than the role did, and none grants its permissions without its denies. Once neutralized, a deletion that
  stops leaves the members not yet removed holding only the role's denies, and a retry finishes it.

### 2. Overwrites are read by what is evaluated

Evaluating a bag on a resource needs the resource's overwrites for the bag's roles and, at most, one for the person.
`RoleCollection.OverwritesFor(bag, resourceId)` returns exactly those, as copies:

- **Role overwrites** compile once per resource into a set keyed by role, read a page at a time by resource, kind and
  subject. There is at most one per role, so the set is bounded by the roles the application defines.
- **The person's own overwrite** is one read by its key, `PermissionOverwrite.KeyFor(resource, Person, person)`, and
  its row or its absence is cached per resource and person.

`RoleBag.On` and `EffectivePermissions` are unchanged and give the same answers, in Discord's order: a row the bag
cannot meet contributed nothing before, so leaving it unread changes nothing. `PermissionOverwrite` is indexed by
resource, kind and subject, and by kind, subject and resource.

- **`Overwrites(resourceId, after, take)`** pages a resource's overwrites for administration — its role overwrites by
  role key, then its people's by person id, the cursor naming the kind — and **`OverwritesOf(subjectKind, subjectId,
  after, take)`** pages one subject's across resources in resource order. Neither loads everything. The across-resource
  `OverwritesFor(bag)` is removed: no read lists every overwrite a person can meet.
- **Explanation is asked of a resource.** `AccessExplainer.WhyAsync(identityId, resource)` adds the overwrites that
  apply to the person on that resource to the identity-wide facts; `Explain` reads them as `OverwritesFor` does. The
  identity-wide effective-access view no longer lists overwrite facts and `PermissionOverwriteAccessContributor` is
  removed: an overwrite holds on one resource, and listing every one a person can meet across resources is the
  unbounded read this decision removes. `EffectiveAccess.Capabilities` is what the identity can do everywhere — role
  permissions and agent-grant capabilities.
- **`MaxOverwritesPerSubject` and `MaxOverwritesPerResource` are removed**, with every path that threw on them. No
  count of overwrites is a property of the model; the bound that stays is `MaxPermissionsPerRole` on each allow and
  deny list.

### 3. Caches evict

`CompiledStateMap` evicts one entry when admitting a key past capacity, chosen by a clock sweep — an approximation of
least recently used: the hand spares an entry read since it last passed and clears its mark, and evicts the first it
reaches unread; the entry being admitted is never the victim. It never clears everything. A read that finds its entry
takes no lock; admission takes one. Single-flight compilation and generation-based invalidation are unchanged: an
evicted entry whose compilation was in flight is simply not current, so its caller compiles again, exactly as after
an invalidation.

`RoleBagCache` holds bags (`MaxCachedBags`), role overwrite sets (`MaxCachedResources`) and person overwrites
(`MaxCachedPersonOverwrites`, new). Each capacity sizes memory; none refuses anything. `InvalidateOverwrites(resourceId)`
invalidates a resource's role set and every cached person overwrite on it.

### 4. Reads keep exactly what they were asked for, and continue from the last row read

A store may compare keys by its own collation: SQL Server's default ignores case, so a read for resource `doc` also
matches `Doc`. Every read that feeds authorization therefore keeps only the rows whose keys equal the asked ones
ordinally, checked by Koan after the store answers — a resource's role overwrites, a person's own overwrite read by
key, a role's members, a person's memberships and the roles they name, and the administration listings. One key never
lends its rows to another.

Every listing is ordered by its key and then by row id, which every paged read breaks ties with. Its cursor is the last
row the store returned for the page — that row's own key and id, never the asked scope — and the next page is the rows
whose key sorts after the cursor's key, or equals it with a later id, as the store compares them. A page keeps, of
those rows, the ones that match exactly, so it can hold fewer rows than asked, or none, while a cursor still follows;
on a store that compares keys ordinally it holds every row of its scope, each once. A cursor is opaque: the listing
hands it back and takes it again.

### 5. A key is well-formed and holds no control character

Every key — a role key, a person id, a resource id, a subject, a permission, a metadata key — is refused when it
holds a control character or a surrogate without its pair, so no two keys can be confused where they are joined or
encoded: UTF-8 would turn a lone surrogate into the replacement character, and `person:` with either would compose
one id. A role's name and a metadata value are text and may hold a control character. The ids composed from keys —
`Role.KeyFor`, `RoleMember.KeyFor` and `PermissionOverwrite.KeyFor` — prefix each part with its length and encode
strictly besides, so parts that hold any text compose one id only from the same parts, and a string that is not
well-formed is refused rather than hashed.

### 6. Stores are rebuilt, not migrated

Nothing moves existing data. A store written before this decision — role documents keyed by their raw key and carrying
`Members`, overwrite rows under ids composed without length prefixes and indexed by resource and by subject alone — is
rebuilt, not migrated: its roles are defined again through `RoleCollection.Define`, its memberships added through
`RoleCollection.Add` and its overwrites through `RoleCollection.Overwrite`.

## Compatibility

- `Role.Members` is removed; `RoleMember` is a new stored entity.
- `Role.Id` is a composed row id; the authorization token is the new `Role.Key`, and `Role.KeyFor(roleKey)` gives the
  row id. Code that read a role's key from `Id` reads `Key`.
- `RoleCollection.Add` answers `RoleMember` (it answered `Role`); `Remove` answers `bool` (it answered `Role`, and threw
  `KeyNotFoundException` for an unknown role). New: `Members(roleKey, after, take)`, `OverwritesFor(bag, resourceId)`,
  `RoleMemberPage`, `OverwritePage`. `Overwrites(resourceId)` becomes `Overwrites(resourceId, after, take)` answering a
  page; `OverwritesOf` is public and paged; the internal across-resource `OverwritesFor(bag)` is removed.
- `RoleOptions.MaxMembersPerRole`, `MaxOverwritesPerSubject` and `MaxOverwritesPerResource` are removed;
  `MaxCachedPersonOverwrites` is added. A configuration still naming a removed key is ignored.
- `RoleBoundException` is thrown only for `MaxRolesPerPerson`; `Overwrite` no longer throws it.
- A rename or metadata change no longer invalidates bags; a permission change invalidates every bag.
- Role deletion neutralizes the role first and publishes each overwrite's after-event as it is removed, before the
  role is removed, rather than all after it; an overwrite that also denies is changed once, to its denies, before it
  is removed.
- A key holding a control character or an unpaired surrogate is refused. `PermissionOverwrite.KeyFor` composes a
  different id than before.
- Joins of one person are serialized within a process.
- A listing's cursor is opaque; a member page's `Next` is no longer a person id.
- `GET api/identity/roles/{key}/members` takes `after` and `take` and answers `{ items, next }`;
  `DELETE …/members/{personId}` answers 204, or 404 for a non-member; `PUT …/members/{personId}` answers the
  membership, and 409 at `MaxRolesPerPerson`. `GET api/identity/overwrites` takes `after` and `take` and answers
  `{ items, next }`; `PUT api/identity/overwrites/…` no longer answers 409. The access console's grant answers the
  membership and 409 at the bound; its revoke answers 404 for a non-member.
- A role membership's access fact names its `RoleMember` row: `RowType` `RoleMember`, `RowId` the membership id.
- The identity-wide effective-access view carries no overwrite facts; `WhyAsync` carries them for the resource asked.

## Evidence

Each spec below failed before this change, by the measurement above, by not compiling against the removed surface,
or as noted.

- `RoleSqliteSpec.Five_thousand_members_join_one_role_each_by_one_row_and_each_is_evaluated` (SQLite, real
  `AddKoan()`): 5000 joins leave the role document's `UpdatedAt` unchanged, each member's bag evaluates on a
  resource, and the members page back each once, in person order.
- `RoleSqliteSpec.Reads_in_the_connectors_shape_by_person_and_by_role_then_person_are_planned_with_their_indexes`:
  SQLite plans the person read, and a page of a role's members after a cursor, with the person index and the
  role-then-person index by name. The connector offers no hook that hands back its command text, so the explained SQL
  is written in the shape the connector issues — its document reads, the scope, the seek, the order with its id
  tie-break and the page — rather than captured. `RoleCollectionSpec.A_persons_roles_are_read_through_the_person_index`
  checks the declared indexes and the read.
- `RoleCollectionSpec.A_roles_members_page_with_a_cursor`, and the same on MongoDB in
  `RoleMongoSpec.Mongo_pages_a_roles_members_and_reads_a_persons_roles_by_person`.
- `RoleSqliteSpec.Two_thousand_resources_each_with_an_everyone_overwrite_evaluate_correctly_and_no_subject_is_bounded`:
  two thousand resources, caches of 64, every resource evaluated in Discord's order for a member and a visitor, the
  `everyone` overwrites paged back each once; and a resource holding 300 people's overwrites evaluates each by their
  own row.
- `RoleCollectionSpec.The_bag_cache_past_its_capacity_evicts_one_entry_and_keeps_the_rest_warm`.
- `RoleCollectionSpec.A_delete_that_fails_after_the_first_member_page_leaves_no_member_holding_what_the_role_granted`:
  a deletion stopped after its first page of members leaves every remaining member without the role's permission
  anywhere and without any overwrite's allow, while the role's deny still withholds what another role grants, and
  only deny overwrites remain; a retry finishes. With overwrites removed first and the permissions intact, the
  remaining members still held the permission, and the spec failed so.
- `RoleReadsSpec.Role_keys_that_differ_only_in_case_compose_different_ids_and_stay_separate`: the composed ids differ
  even compared without case, and `Admin` and `admin` stay two roles in every read.
- `RoleReadsSpec.A_key_with_an_unpaired_surrogate_is_refused_and_an_id_is_never_hashed_from_one`.
- `RoleCollectionSpec.Erasure_removes_every_membership_of_a_person_over_the_bound`, and
  `RoleCollectionSpec.Concurrent_joins_of_one_person_never_exceed_the_roles_one_person_holds`: without the person
  lock, all eight concurrent joins of a person bounded at two succeeded.
- `RoleReadsSpec.A_page_keeps_only_exact_rows_and_continues_from_the_last_row_the_store_returned` (rows a store
  ignoring case would return, so no such store is needed), `RoleReadsSpec.Keys_that_differ_only_in_case_never_share_rows`,
  and `RoleReadsSpec.A_key_with_a_control_character_is_refused_and_composed_ids_never_collide`: before, a key with
  U+001F was accepted, and `KeyFor("role:a\u001fx", "person:c")` equalled `KeyFor("role:a", "x\u001fperson:c")`.
- `PermissionOverwriteSpec.OverwritesFor_resolves_as_every_overwrite_on_the_resource_does`: the same permissions and
  deciding facts from `OverwritesFor` as from every row on the resource, for several bags.
- `PermissionOverwriteSpec.A_resources_and_a_subjects_overwrites_page_with_a_cursor`,
  `PermissionOverwriteSpec.Why_names_the_overwrites_on_the_resource_asked_about_and_skips_an_expired_one`,
  `RoleCollectionSpec.Adding_a_person_past_the_roles_one_person_holds_is_refused_with_its_correction`,
  `RoleCollectionSpec.Deleting_a_role_removes_its_members_and_a_role_defined_again_starts_with_none`,
  `RoleCollectionSpec.A_permission_change_reaches_every_member_without_reading_the_members`,
  `RoleCollectionSpec.A_membership_row_is_written_only_by_the_role_collection`, and `RoleWebSpec`'s paged routes.
- SEC-0021's specs pass unchanged in what they assert about resolution, explanation, revoke, events and the guard.

---
id: SEC-0031
slug: roles-stand-in-one-order
domain: Security
status: Accepted
date: 2026-10-30
title: Roles stand in one order, and an actor manages the roles below their highest
related:
  - SEC-0021
  - SEC-0030
---

# SEC-0031: Roles stand in one order, and an actor manages the roles below their highest

## Context

Koan's server roles follow Discord's model (SEC-0021, SEC-0030), but a `Role` had no position, so nothing said which
role manages which. An application with a role editor — custom roles, an order, and the rule that someone manages only
the roles below their own highest and those roles' members — had to fake a rank from the roles it seeded, and could
not rank a role a person created.

## Decision

**Every role stands in one order, and Koan answers who manages what.**

### 1. The order

- `Role.Position` places each role in one order per role collection, a higher position above a lower:
  `everyone` at 0, every other role at one of 1 to the number of them. The order is dense; it is indexed by position.
- **Placing.** `Define(key, name, permissions, metadata, position)` places a new role at `position` — the role there
  and every role above it move up one — or at the top when `position` is null. Passing the creator's `bag.Highest`
  places it directly below their highest role. A role that exists moves to a given `position`, or stays where it is.
  `everyone` is always at 0, and a new `authenticated` is placed at 1 unless a position is given: every signed-in
  person holds it, so at the top it would rank every signed-in person above every role.
- **Moving.** `Move(key, position)` moves a role; each role it passes moves one toward the place it left. A move
  publishes `RoleChanging`/`RoleChanged` and can be vetoed. Moving `everyone` is refused.
- **Deleting** a role moves every role above it down one, closing its gap.
- A position past either end is clamped to that end, so a dragged role or a creator's `Highest` of 0 lands on a valid
  place rather than failing.
- **Reads are bounded.** `Page` reads the order a page at a time, the highest first. A placement reads the top
  position once; a shift reads the roles it moves a page at a time, from the end they move toward, so a row once
  moved has left the range still to read. Nothing bounds how many roles an application defines.
- **Concurrency.** Every write of a role's row — a role's own change and each position a shift moves — is serialized
  by one order lock in the process, taken after any other lock, so concurrent moves leave one dense order and a
  rename or permission change never writes back a position a shift has since moved. Until the moving role itself is
  written, it can share its position with the role passing it; the two then stand as they stood or as they will.

### 2. Who manages what

`RoleBag.Highest` is the position of the highest role a bag is in, 0 when that is `everyone`; it compiles with the
bag from the role rows the bag already reads, and every placement, move or deletion compiles every bag again.

`RoleBag.Manages(role, to)`, pure, and `RoleCollection.Manages(actor, roleKey, to)`, one read of the role, answer one
rule:

- an actor manages a role whose position is below their highest, and the memberships of that role; given `to`, they
  may move it there only when `to` is below their highest too;
- a bag whose roles grant `global:administrator` manages every role and moves it anywhere;
- nobody manages their own highest role, and nobody moves `everyone`.

A key no role has yet is judged where `Define` would place it: at `to`, managed when `to` is at most the actor's
`Highest`, or at the top, which only an administrator manages.

`Manages` is the hierarchy, not a permission: whether an actor may manage roles at all is the application's own
permission check, beside it. Koan's mutations do not call it; the application judges, then changes.

### 3. Stores are rebuilt

No migration: a role written before this decision reads back at position 0. Such a store is rebuilt — its roles
defined again through `RoleCollection.Define`, in order, and its members added again.

## Consequences

- One dense order serves a role editor's list, its drag and drop, and its rule in one call; `bag.Highest` also ranks
  two people against each other.
- A move writes one row per role it passes. Role orders are arranged by people, and moves are rare.
- Processes sharing a store do not coordinate placements, as they do not coordinate the rest of the role model
  (SEC-0030): two processes moving roles at the same instant can leave two roles on one position or a position empty,
  and caches in another process keep a bag's `Highest` until invalidated. A tie fails closed: a role at the actor's
  highest position is never below it.
- A check made before a change is not held through it: a role moved between an application's `Manages` and its
  `Move` is judged as it stood.

## Compatibility

- `Role.Position` is new and indexed; `RoleBag.Highest` and `RoleBag.Manages(role, to)` are new, and `RoleBag`'s
  constructor takes an optional `highest`.
- `RoleCollection.Define` takes `int? position` before its cancellation token; a caller passing the token by position
  passes it by name. `RoleCollection.Move(roleKey, position)` and `RoleCollection.Manages(actor, roleKey, to)` are new.
- `RoleCollection.Page` returns the roles in their order, the highest first.
- A role's audit snapshot carries its position.
- `GET api/identity/roles` pages the order; a role's JSON carries `position`, and a bag's carries `highest`.

## Evidence

In `RolePositionSpec` (Koan.Identity.Tests, a real `AddKoan()` host on the in-memory store):

- `A_new_role_is_placed_at_the_top_or_at_the_position_asked_and_the_roles_above_move_up`;
- `A_role_moves_up_and_down_and_the_roles_it_passes_close_ranks`;
- `Concurrent_moves_and_changes_keep_one_dense_order_and_lose_no_change`: 48 concurrent moves beside renames and
  permission changes leave one dense order and every change in place. With the order lock taken out, two runs left
  positions `{1, 5, 6, 7, 7, 7, 8, 8}` and `{2, 3, 4, 6, 6, 7, 7, 7}`;
- `Deleting_a_role_closes_its_gap_and_its_neighbours_bags_compile_again`;
- `A_new_authenticated_role_is_placed_at_the_bottom_since_every_signed_in_person_holds_it`;
- `Each_actor_manages_the_roles_below_their_highest_an_administrator_every_role_and_nobody_their_own_highest`: a
  member, a moderator, an administrator and a visitor, a role created directly below the moderator, and a move judged
  below and at the moderator's highest.

`RoleSqliteSpec.Sqlite_orders_moves_and_closes_role_positions_as_numbers` holds thirteen positions through a move, a
placement and a deletion on SQLite, ordered and paged as numbers.

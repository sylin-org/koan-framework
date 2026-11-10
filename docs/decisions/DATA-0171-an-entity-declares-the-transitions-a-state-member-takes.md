---
id: DATA-0171
slug: an-entity-declares-the-transitions-a-state-member-takes
domain: Data
status: Accepted
date: 2026-11-10
title: An entity declares the transitions a state member takes, and the lifecycle refuses any other change
related:
  - DATA-0167
  - ARCH-0143
  - ARCH-0145
  - WEB-0086
---

# DATA-0171: An entity declares the transitions a state member takes, and the lifecycle refuses any other change

## Context

An application that keeps a state on a row (an order's status, a participant's standing) writes what may follow what as code in
every place that changes it: an entity method per change, a judge per surface, and again for each page that offers the changes. The
copies drift, and a save from anywhere else (a seed, a fixture, a second command) changes the state with no table consulted at all.
Koan already says "transition" for a change of state (`JobTransition`, `CanonStageTransition`, `TransitionContext`), and its lifecycle
already judges every save at the outer repository boundary.

**The business sentence:** a state goes only where its entity says it goes, whoever saves it.

## Decision

1. **One method on the entity's lifecycle.** `Order.Lifecycle.Transitions(order => order.Status, (Status.Open, Status.Shipped), …)`
   takes the member and the pairs of values it goes from and to, as value tuples, and registers one before-upsert rule. There is no
   new type.
2. **The rule judges the row as it stood.** A save whose prior row stood at one value and would stand at another that no pair names
   throws `Refusal` with `Reason.Conflict("transition", …)`, whose sentence names the member, the value it stands at and the values it
   goes to from there, or that nothing follows it. A save that leaves the member as it stands passes, and so does a save with no row as
   it stood.
3. **It refuses as an entity's own rule does.** A command's `Send` answers the `Refusal` as `Outcome.Refused` and leaves nothing; a
   declared endpoint answers it as a 409 problem whose word is `transition`. The word is `Constants.Lifecycle.Transition`.
4. **What it judges.** Every path that hands a before-upsert rule the row as it stood: a save, `UpsertMany`, a batch, a conditional
   replace, and a declared endpoint's write. It does not judge an `Insert`, which has no row as it stood, nor an exact transfer, which
   skips the lifecycle (DATA-0149). Under a deferred `EntityContext.Transaction` a save is judged when the scope commits, so the
   refusal surfaces at `Commit`; inside a hold it surfaces at the save.

Not decided here: who may make a transition, a route or tool drawn from the table, a state that ends by itself, and the guards on
Koan's own state members, `IdentityStatus` and `ReviewStatus`, which declare none yet.

## Coalescence

- **Owner:** `Koan.Data.Core` lifecycle, beside `BeforeUpsert`, which it is built on.
- **Closest pattern:** an application's own `BeforeUpsert` rule throwing `Refusal`, as `Ledger` and Tangent's `Post.Holds` do.

## Ergonomics

One call states the table where the entity is composed. Public concepts added: `EntityLifecycleBuilder.Transitions` and the word
`transition`.

## Evidence

- `EntityTransitionsSpec` (Koan.Tests.Data.Core): a declared transition saves; any other change is refused on a plain save with the
  word and the sentence and leaves the row; a save that leaves the member, a first save and an insert pass; a command on SQLite answers
  `Outcome.Refused` with the word and leaves nothing.
- `EntityWriteSpecsBase.A_declared_transition_writes_and_a_change_outside_the_table_answers_a_conflict_whose_word_is_transition`, on
  SQLite and in memory: a declared endpoint answers the change outside the table 409 with the word and the sentence.

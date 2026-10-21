---
id: SEC-0019
slug: delete-bounds-evaluate-counterpart-read-constraints
domain: Security
status: Accepted
date: 2026-10-21
title: Delete bounds evaluate counterpart Read constraints through the Data facade
related:
  - SEC-0013
  - SEC-0004
  - WEB-0077
---

# SEC-0019: Delete bounds evaluate counterpart Read constraints through the Data facade

## Context

SEC-0013 made the Read constraint the delete bound when a realization declares no Delete constraint, and required
that bound to be row-only. A Read constraint may be a counterpart predicate (`Filter.SameIdIn`: the same id in another
partition satisfies a predicate), which only a connector with native counterpart reads can answer; MongoDB is one.
Every request on such an entity then threw "Delete access bound cannot enforce a counterpart predicate": the deletes,
and also every read that asked for the per-row access manifest, because the manifest computed the same bound
(ledger NK-318). The manifest also ran the Read declaration a second time instead of using the one the read executed.

## Decision

**The delete bound is still the Delete constraint, else the Read constraint; a counterpart bound is evaluated by the
store, not refused.** This amends SEC-0013's row-only requirement for the Read fallback.

- `Delete` and `DeleteMany` load their target rows through one helper. A row-only bound is checked on each loaded
  row, as before. A counterpart bound runs as one scoped query (`Id in ids` and the bound) through the Data facade,
  the same counterpart-capable path a governed read takes.
- `DeleteByQuery` and `DeleteAll` already select their rows with a facade query; the bound now reaches it unchanged.
- The manifest's `remove` verb follows the row's executed read when no Delete constraint is declared: the read's
  query proof for a counterpart, its frozen filter otherwise. The Read declaration runs once per request.
- A declared Delete constraint stays row-only, like Update and Patch: a counterpart in a mutation declaration is still
  refused before hooks and storage.

A connector without native counterpart reads refuses the delete with the facade's corrective message, as it refuses
the read. That refusal cannot move to boot: a constraint is a function of the request's principal and services, so
whether it yields a counterpart predicate is only known when a request runs it. The read of the same entity fails the
same way first, so a deployment meets the refusal before any delete.

## Compatibility

- Entities whose Read constraint is a counterpart predicate work again on MongoDB: reads with the access manifest,
  and every delete verb, bounded to the rows the caller can read.
- No public signature or stored format changes. `RowProjection`'s public constructor is unchanged.

## Evidence

- `CounterpartEndpointSpec` (Koan.Data.Connector.Mongo.Tests): seven cells threw the bound refusal at ac556b77f and
  pass again; `Counterpart_read_constraint_bounds_every_delete_verb` deletes the visible row and keeps the hidden one
  for `Delete`, `DeleteMany`, `DeleteByQuery` and `DeleteAll`, with delete hooks raised for the visible row only.
- `ReadConstraintDeleteSpec` (Koan.Web.Extensions.Tests) still proves the row-only fallback for every verb.

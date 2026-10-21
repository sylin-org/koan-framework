---
id: DATA-0141
slug: postgres-inserts-only-and-deletes-conditionally
domain: Data
status: Accepted
date: 2026-10-21
title: PostgreSQL implements insert-only and conditional delete
related:
  - DATA-0140
---

# DATA-0141: PostgreSQL implements insert-only and conditional delete

## Context

The Web create path requires an atomic insert whenever an access policy constrains or stamps a create, and guarded
deletes require a conditional delete. SQLite and MongoDB implement both. The shared Npgsql repository implemented
neither and declared neither capability, so the facade refused them and an owner-stamped `POST` answered 501
`insert-unsupported` on PostgreSQL (and CockroachDB) only. Ledger NK-057; NK-139 is related.

## Decision

`NpgsqlRepository` implements `IInsertOnlyRepository` and `IConditionalDeleteRepository`, and `NpgsqlFeatures` declares
`Write.InsertOnly` and `Write.ConditionalDelete`:

- Insert writes `INSERT ... ON CONFLICT (<identity>) DO NOTHING`. One row is Inserted/Committed. Zero rows is
  Conflict/NotCommitted only after the identity is seen to exist, so a trigger that swallowed the insert is a failure.
  The conflict target is the identity alone, so a violation of any other unique index still throws. A generated
  identity is inserted without a conflict target and returned with `RETURNING`.
- Conditional delete is one `DELETE ... WHERE <identity> AND (<guard>)` and succeeds on exactly one row.

## Compatibility

Additive: PostgreSQL and CockroachDB now accept owner-stamped creates, `Entity.Insert()` and guarded deletes that the
facade refused. No existing behaviour, signature or stored data changes.

## Evidence

`NpgsqlWriteCapabilitiesSpec` (Data.Relational, no server) proves the interfaces and declarations; it failed before.
`PostgresInsertOnlySpec` (duplicate, eight concurrent inserts, another unique index, conditional delete) compiles; it
needs a server and was not run, nor was the PostgreSQL Web adapter-surface suite.

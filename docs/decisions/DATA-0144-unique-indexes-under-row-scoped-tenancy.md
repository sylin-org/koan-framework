---
id: DATA-0144
slug: unique-indexes-under-row-scoped-tenancy
domain: Data
status: Proposed
date: 2026-10-21
title: How a unique [Index] behaves under row-scoped tenancy (owner decision)
related:
  - ARCH-0099
  - DATA-0142
---

# DATA-0144: How a unique [Index] behaves under row-scoped tenancy (owner decision)

## Context

Row-scoped tenancy stores the tenant in a framework-managed field inside the document, not in a CLR property.
`MappingIndexCompiler` and `IndexMetadata` build index keys from CLR properties only, and `IndexAttribute` has no scope
option, so `[Index(Unique = true)] Email` is unique across every tenant. Tenant B cannot register an address tenant A
already holds, and the duplicate-key error tells B that some other tenant holds the value, a one-bit leak across
tenants. The application cannot add the managed field to the index because it is not a property. Ledger NK-039,
confirmed in source.

## Why this is not implemented

Every option that gives per-tenant uniqueness changes the index Koan provisions for existing tables and collections:
a new key that leads with the tenant field, beside the existing global index, which keeps refusing cross-tenant
duplicates until it is dropped by a migration Koan does not run. An existing deployment may also already hold rows
that a per-tenant index accepts and the old one refused, or the reverse. That is a change to existing indexes, outside
the scope of a defect fix.

## Options

- **A. Per-tenant by default.** When the entity's segmentation plan has row-scoped managed fields, every unique index
  leads with them: `(("Json" #>> '{__koan_tenant}'), <expr>)` on PostgreSQL, `{__koan_tenant: 1, email: 1}` on MongoDB,
  the dialect read on SQLite. Matches what most multi-tenant applications mean. Needs a migration recipe that builds
  the new index, verifies it, and drops the global one; generated names change, which DATA-0142's verification would
  otherwise report as a collision.
- **B. An explicit scope on the attribute.** `IndexAttribute.Scope` (`Global` or `Tenant`), default `Global`. No
  existing index changes until an application opts in; the leak remains for applications that do not.
- **C. B with a `Tenant` default for unique indexes on tenant-scoped entities, introduced at a version boundary.**
  The safe meaning becomes the default for new deployments; existing ones keep `Global` until they migrate, which
  requires recording the scope an index was built with.
- **D. Keep global uniqueness and document it.** No change; the cross-tenant refusal and its one-bit leak stay.

## Recommendation

C, with A's key shapes and an index migration path that DATA-0142's catalog verification can confirm. Whatever is
chosen, the duplicate-key failure should be reported as a conflict without naming whether another tenant holds the value.

## Evidence required before acceptance

An adapter-surface spec on SQLite, PostgreSQL and MongoDB under row-scoped tenancy: one value accepted in two tenants,
a duplicate within one tenant refused, and an existing deployment migrated without losing either guarantee.

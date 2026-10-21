---
id: DATA-0137
slug: mongo-paths-use-the-serializer-field-names
domain: Data
status: Accepted
date: 2026-10-21
title: MongoDB filter and sort paths use the field names the serializer writes; index key spelling is an owner decision
related:
  - DATA-0134
---

# DATA-0137: MongoDB filter and sort paths use the field names the serializer writes; index key spelling is an owner decision

## Context

The MongoDB adapter serializes a managed document with Newtonsoft's `CamelCaseNamingStrategy`, which lower-cases a
whole leading run of capitals: `ISBN` is stored as `isbn`, `URL` as `url`, `IPAddress` as `ipAddress`. Filter, sort
and collection-order paths were built by a private `Camel` that lower-cased only the first letter, so they addressed
`iSBN`, `uRL` and `iPAddress`, fields no document holds. Measured against a local MongoDB 8.3 before this change:
`Book.Query(b => b.ISBN == "978-2")` returned nothing for a stored book with that ISBN, and sorting by `IPAddress` had
no effect. The same code works on SQLite and PostgreSQL. Ledger NK-029.

Index keys were built by the same function, so `[Index] ISBN` created an index on `iSBN`.

## Decision

1. Filter, sort and collection-order paths resolve each segment through the serializer's own naming strategy (one shared
   `CamelCaseNamingStrategy`), so a path names the field the document holds.
2. Index keys keep their current first-letter spelling. Correcting them changes what Koan provisions on existing
   collections: an auto-named index gains a second, correctly keyed index beside the old one, and an explicitly named
   index (`[Index(Name = ...)]`) trips the incompatible-index refusal in `MongoSchema` and needs a migration. That is a
   change to existing indexes, which this change does not make.

### Owner decision: index key spelling

- **A. Correct the keys for every collection.** New deployments get usable indexes. Existing collections gain a second
  index per acronym-leading key (the old one is dead weight and can be dropped by hand); explicitly named ones stop the
  entity until migrated. A release note and a migration recipe are required.
- **B. Correct the keys only where Koan creates the collection.** No existing collection changes; a collection created
  by an older version keeps the dead index until migrated. The rule depends on history, which is harder to explain.
- **C. Keep the legacy spelling (current state).** Nothing changes on disk; filters on acronym-leading members are
  correct but not served by their declared index. A unique `[Index]` on such a member indexes a missing field, so every
  document has the same null key and a second insert fails with a duplicate-key error; that defect remains.

## Compatibility

Behavioural on MongoDB: filters and sorts on members whose names start with two or more capitals now return and order
the stored documents. Names with a single leading capital (`Name`, `Id`) are unchanged. An explicit map is unaffected,
since its paths come from the map. No public signature, stored document or index changes.

## Evidence

`MongoAcronymFieldSpec` (Connector.Mongo, against a local `mongod` 8.3): equality on `ISBN` and `IPAddress`, `$in` on
`ISBN`, and paged sorts on `IPAddress` and `-ISBN`. It failed before this change and passes after.

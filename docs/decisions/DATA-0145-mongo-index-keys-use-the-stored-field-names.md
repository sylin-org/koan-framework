---
id: DATA-0145
slug: mongo-index-keys-use-the-stored-field-names
domain: Data
status: Accepted
date: 2026-10-21
title: MongoDB index keys use the field names the serializer writes
related:
  - DATA-0137
---

# DATA-0145: MongoDB index keys use the field names the serializer writes

## Context

DATA-0137 made MongoDB filter, sort and collection-order paths follow the serializer's naming (`ISBN` is stored as
`isbn`) and left index keys on the older first-letter spelling (`iSBN`), pending an owner decision among three options.
An index on an acronym-leading member therefore covered no stored field, and a unique `[Index]` on one indexed a field
every document lacks: every document had the same null key, so the second insert failed with a duplicate-key error.
Ledger NK-029.

## Decision

Owner decision, option A of DATA-0137: index keys are corrected everywhere, to the serializer's naming.

1. A managed index is keyed by the stored field name (`[Index] ISBN` is `{ isbn: 1 }`), the same resolution filters and
   sorts use. A unique `[Index]` on an acronym member now enforces uniqueness on a new collection.
2. A collection an older version provisioned gains the correctly keyed index. The old index (`iSBN_1`) is left in
   place: Koan never drops or renames an existing index. It is redundant, and a warning at provisioning names it and the
   `db.getCollection("<collection>").dropIndex("<name>")` call that removes it. If it is unique it still rejects a
   second document until dropped, as it did before this change.
3. Where an explicitly named index (`[Index(Name = ...)]`) exists with the old keys, creating the declared index would
   collide on the name. Provisioning fails with an `InvalidOperationException` that names the old index, shows its keys
   and the declared keys, and gives the `dropIndex` call. After the operator drops it and restarts, Koan creates the
   correctly keyed index under the declared name. A name collision with any other definition keeps the existing
   "incompatible index" refusal.
4. Explicit maps are unaffected, since their paths come from the map. Members whose names start with a single capital
   (`Name`, `Id`) produce the same keys as before, so nothing changes for them.

## Compatibility

Behavioural on MongoDB, for entities with an `[Index]` on a member whose name starts with two or more capitals:

- New collections get a usable index, and a unique one enforces uniqueness.
- Existing collections gain a second index per such declaration (extra write cost until the old one is dropped).
- An entity with an explicitly named index on such a member stops at provisioning until the old index is dropped.

No public signature or stored document changes. The old index is never removed by Koan.

## Evidence

`MongoAcronymIndexSpec` (Connector.Mongo, against MongoDB 8.3): a unique `[Index]` on `ISBN` is keyed `isbn` and rejects
a duplicate while accepting distinct values; an existing collection with an `iSBN_1` index gains `isbn_1`; an explicitly
named index with `iSBN` keys is refused with the drop call and, once dropped, recreated keyed `isbn`. Three of the four
specs failed before this change.

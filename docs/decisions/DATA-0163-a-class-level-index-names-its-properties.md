---
id: DATA-0163
slug: a-class-level-index-names-its-properties
domain: Data
status: Accepted
date: 2026-10-30
title: A class-level index names its properties, or the mapping refuses it
related:
  - DATA-0119
  - SEC-0032
---

# DATA-0163: A class-level index names its properties, or the mapping refuses it

## Context

`[Index]` on a class names its properties through `Fields`. Declared without them, the mapping compiler and
`IndexMetadata` both skipped it. `TenantInvite` declared two indexes that way, with names and no fields, so the token
lookup behind every invitation acceptance scanned the table and nothing reported that the declared indexes did not
exist.

## Decision

A class-level `[Index]` without `Fields` is refused where the entity's indexes are compiled: at mapping, as a
`MappingCompilationException` naming the entity type and the index, and by `IndexMetadata.GetIndexes`, naming the same.
The correction is in the message: put `[Index]` on each property it covers, or name them with `Fields`.

`TenantInvite` puts its indexes on `TokenHash` and `TenantId`.

## Consequences

An entity that declared such an index stops mapping until it says what it meant. Nothing that worked stops working,
since such a declaration never built anything.

---
id: DATA-0153
slug: transfer-endpoints-can-name-the-base-set
domain: Data
status: Accepted
date: 2026-10-21
title: An empty transfer axis names its base value, and naming a source or an adapter names the route
related:
  - DATA-0079
  - DATA-0125
  - DATA-0152
---

# DATA-0153: An empty transfer axis names its base value, and naming a source or an adapter names the route

Amends [DATA-0079](DATA-0079-entity-transfer-helpers.md), which defines `From()` and `To()`.

## Context

`From(source, adapter, partition)` and `To(...)` turned every blank argument into "not given", then entered
`EntityContext.With(source, adapter, partition)`, which inherits each omitted axis from the ambient context separately
(NK-110). So one transfer line meant different things depending on the scope around it, and some meanings could not be
written at all:

- inside `EntityContext.Partition("hot")`, `Todo.Copy().To(partition: "cold")` read `hot`, and no endpoint could name
  the base set: `From(partition: "")` was the same as `From()`;
- inside `EntityContext.Source("Preview")`, `To(adapter: "mongo")` inherited the source as well and threw, because a
  source and an adapter cannot both be set.

`EntityContext.With` already documents an empty axis as "clear it"; the transfer endpoints discarded that before it
arrived.

## Decision

1. **An omitted axis inherits** the ambient `EntityContext`, as before.
2. **An empty or blank axis names its base value.** `source: ""` is the default source, `adapter: ""` is no adapter
   override, `partition: ""` is the base partition. `TransferContextOptions` keeps it as `string.Empty` and passes it to
   `EntityContext.With`, which clears that axis.
3. **Naming a source or an adapter names the route.** A source and an adapter are two ways to say where an Entity is
   stored, so an endpoint that names one does not inherit the other: `To(adapter: "mongo")` inside
   `EntityContext.Source("Preview")` reaches MongoDB's default store. Naming both on one endpoint still throws.
4. `From(source: "", partition: "")` therefore reads the base set under any ambient scope. No separate verb is added.

## Consequences

- A transfer written with an explicit empty axis no longer inherits that axis. Code that passed `""` meaning "inherit"
  now gets the base value. Nothing that worked fails at boot and no data is lost; a transfer that relied on it reads or
  writes the base value, and its result's `ResolvedFrom` and `ResolvedTo` say so.
- `To(adapter:)` or `From(adapter:)` inside an ambient source scope runs instead of throwing.
- The declared snapshot in results and audits carries `""` for an axis named empty.

## Evidence

`EntityTransferDslSpec` (Data.Core): `Transfer_EmptyPartition_NamesTheBaseSet_AndAnOmittedOneInherits` and
`Transfer_NamingAnAdapterInsideAnAmbientSource_ReachesThatAdapter` fail before this change and pass after it.

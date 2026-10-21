---
id: DATA-0127
slug: datasource-attribute-routes-the-entity
domain: Data
status: Accepted
date: 2026-10-21
title: "[DataSource] routes the entity to its declared source"
related:
  - DATA-0077
  - DATA-0111
  - DATA-0126
  - ARCH-0103
---

# DATA-0127: [DataSource] routes the entity to its declared source

## Context

`Koan.Data.Abstractions` ships a public `[DataSource(name)]` attribute documented as declaring "a logical data source
name for an entity". Nothing read it: `AdapterResolver` consulted `[SourceAdapter]` and `[DataAdapter]` and then the
default route. An entity decorated `[DataSource("Analytics")]` compiled, booted and persisted into the default
database with no warning and no fact, mixing data meant for a separate store with the default data (ledger NK-038).
The analytics initiative plans `[DataSource("analytics")]` entities.

Removing the attribute would also have closed the hole; the planned use, the documented meaning and the existing
`EntityContext.Source(...)` route made implementing it the smaller change.

## Decision

**`[DataSource(name)]` is a route: every operation on the entity uses the configured source `name`.**

1. `RoutedSource`, the one routing decision the record and vector planes share, gains `RouteKind.Declared`. The order
   is: an explicit `EntityContext.Source(...)`, then a Database-mode data axis, then the entity's `[DataSource]`, then
   none. The attribute is read once per type.
2. `AdapterResolver` resolves a declared route like an explicit source: `Koan:Data:Sources:{name}` must exist and name
   an `Adapter`, otherwise the first operation throws `InvalidOperationException` naming the entity, the attribute and
   the configuration key. The route's origin is `EntityAttribute`.
3. The source's connection follows DATA-0126: a declared source without a connection is refused.
4. A decorated root is outside default-route promotion (`DataEntityRouteScope.OutsideDefault`), as roots decorated with
   `[SourceAdapter]` or `[DataAdapter]` already are, so a cutover does not copy it.

## Compatibility

- An entity already decorated `[DataSource(name)]` now reads and writes the named source. Rows it wrote into the
  default database before this change stay there and are visible only under an explicit
  `EntityContext.Source("Default")`, which is how to read them for a move.
- If the named source is not configured, the entity's first operation now throws instead of using the default.
- A vector entity decorated `[DataSource(name)]` resolves its vector space for `name`, as under
  `EntityContext.Source(name)`.
- `RouteKind` gains a member; no public signature or stored format changes.

## Guarantee and correction

- A decorated entity's rows live in the source it names, or its operations fail with a corrective message.
- An explicit `EntityContext.Source(...)` still overrides the declaration for one scope.

## Evidence

- `SqliteDataSourceAttributeSpec` (Koan.Data.Connector.Sqlite.Tests): a `[DataSource("Archive")]` entity is stored in
  the Archive file and not in the default one; `EntityContext.Source("Default")` overrides the declaration; an
  unconfigured `[DataSource("Vault")]` is refused naming `Koan:Data:Sources:Vault`; the decorated root is outside
  default-route promotion. Three of the four failed before this change (the row went to the default database, the
  unconfigured source was not refused, the root was promotable).

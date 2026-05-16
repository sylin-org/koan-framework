# Defensive Publication: Two-Tier Relational Command Cache with Dialect Templates and Per-Entity Rendered Commands

## Header Block

- **Title:** Two-Tier SQL Command Cache Separating Dialect-Level Templates from Per-Entity Rendered Commands with Compiled Binders
- **Inventor:** Leo Botinelly (Leonardo Milson Botinelly Soares)
- **Disclosure Date:** 2026-03-24
- **Field of Invention:** Database query optimization in multi-provider ORM frameworks, specifically methods for caching SQL command generation at two granularity levels to eliminate repeated string composition and reflection in data access hot paths.
- **Keywords:** SQL cache, command cache, dialect template, rendered command, compiled binder, relational, ORM, hot path, zero-reflection, string composition, multi-dialect

---

## 1. Problem Statement

Relational data adapters in ORM frameworks must generate SQL commands (SELECT, INSERT, UPDATE, DELETE) for each entity type. This generation involves: reflecting on entity properties to determine column lists, constructing SQL strings with provider-specific syntax (quoting rules, parameter prefixes, upsert strategies), and creating parameter binder delegates that map entity properties to command parameters.

In a multi-provider framework, this generation is more complex because different SQL dialects use different syntax. PostgreSQL uses `INSERT ... ON CONFLICT DO UPDATE`, SQL Server uses `MERGE`, SQLite uses `INSERT OR REPLACE`. The column lists and parameter bindings are the same across dialects, but the SQL skeletons differ.

Existing ORM caches (like EF Core's query compilation cache) are tightly coupled to LINQ expression trees and operate at a different abstraction level. Lightweight ORMs like Dapper perform no caching of SQL generation — each call reconstructs the command. Neither approach separates dialect-level skeletons from entity-level column rendering.

---

## 2. Prior Art Summary

**EF Core Query Cache:** Caches compiled LINQ expression trees, not SQL strings. Operates at query plan level, tightly coupled to DbContext. Not applicable to non-LINQ operations (raw commands, upserts).

**Dapper:** No SQL caching. Each `Query<T>()` call constructs the command from scratch (though Dapper does cache type-to-property mappings for result materialization).

**NHibernate:** HQL-to-SQL compilation cache. Heavyweight, operates at query language level. Not separated into dialect and entity tiers.

**Specific gaps:**
1. No ORM separates SQL caching into dialect-level templates (shared across entities) and per-entity rendered commands (entity-specific).
2. No system caches compiled parameter binder delegates alongside rendered SQL strings.
3. No system keys caches by `(entityType, keyType, dialectType, optionsHash, version)` to handle variant queries for the same entity.

---

## 3. Detailed Description of the Invention

### 3.1 Two-Tier Architecture

**Tier 1: Dialect Templates (adapter scope)**
SQL skeleton patterns per provider dialect, shared across all entities using that dialect:
```
PostgreSQL upsert template:
  INSERT INTO {table} ({columns}) VALUES ({params})
  ON CONFLICT ({key}) DO UPDATE SET {setClause}

SQL Server upsert template:
  MERGE INTO {table} AS target
  USING (SELECT {params}) AS source
  ON target.{key} = source.{key}
  WHEN MATCHED THEN UPDATE SET {setClause}
  WHEN NOT MATCHED THEN INSERT ({columns}) VALUES ({params})

SQLite upsert template:
  INSERT OR REPLACE INTO {table} ({columns}) VALUES ({params})
```

Templates are cached per `dialectType`. They contain placeholders, not entity-specific content.

**Tier 2: Per-Entity Rendered Commands**
Entity-specific SQL strings with actual column names, table names, and parameter markers:
```
For Entity: Product (id, name, price, category)
  PostgreSQL rendered SELECT:
    SELECT "id", "name", "price", "category" FROM "products" WHERE "id" = @p0

  PostgreSQL rendered UPSERT:
    INSERT INTO "products" ("id", "name", "price", "category")
    VALUES (@p0, @p1, @p2, @p3)
    ON CONFLICT ("id") DO UPDATE SET "name" = @p1, "price" = @p2, "category" = @p3
```

Rendered commands are cached in `AggregateConfig<TEntity, TKey>._bags` — a per-entity static dictionary.

### 3.2 Cache Key Structure

```
Tier 1 key: dialectType (e.g., typeof(PostgreSqlDialect))
Tier 2 key: (entityType, keyType, dialectType, optionsHash, version)
```

- `optionsHash` varies when query options differ (field selection, sorting)
- `version` tracks schema changes (column additions/removals)
- This ensures cache invalidation when the query shape changes

### 3.3 Compiled Parameter Binders

Alongside cached SQL strings, the cache stores compiled property-to-parameter delegate chains:

```
For Product:
  Binder[0]: (product, cmd) => cmd.Parameters["@p0"].Value = product.Id
  Binder[1]: (product, cmd) => cmd.Parameters["@p1"].Value = product.Name
  Binder[2]: (product, cmd) => cmd.Parameters["@p2"].Value = product.Price
  Binder[3]: (product, cmd) => cmd.Parameters["@p3"].Value = product.Category
```

These delegates are compiled once (via expression trees or `DynamicMethod`) and reused for all subsequent operations. This eliminates per-operation reflection.

### 3.4 RelationalCommandCache Static API

```
public static class RelationalCommandCache
{
    private static readonly ConcurrentDictionary<string, string> _selectCache = new();

    public static string GetOrAddSelect<TEntity>(ILinqSqlDialect dialect, Func<string> factory)
    {
        var key = $"select|{typeof(TEntity).FullName}|{dialect.GetType().FullName}";
        return _selectCache.GetOrAdd(key, _ => factory());
    }
    // Similar for upsert, delete, etc.
}
```

### 3.5 ILinqSqlDialect Interface

Each dialect provides:
- Column quoting rules (e.g., `"column"` for PostgreSQL, `[column]` for SQL Server)
- Parameter prefix (e.g., `@` for SQL Server, `$` for PostgreSQL native)
- Table name resolution
- Upsert strategy
- Escaping rules

---

## 4. Claims-Style Disclosure

1. A two-tier SQL command cache wherein dialect-level templates (SQL skeletons with placeholders) are cached separately from per-entity rendered commands (entity-specific SQL with actual column names), enabling template reuse across entity types while caching entity-specific rendering.

2. A cache key structure incorporating `(entityType, keyType, dialectType, optionsHash, version)` that provides fine-grained cache invalidation when query shape, dialect, or schema changes, without invalidating unrelated cache entries.

3. A compiled parameter binder cache wherein property-to-parameter delegate chains are compiled once per entity type per dialect and stored alongside rendered SQL commands, eliminating per-operation reflection in data access hot paths.

4. A static command cache API (`RelationalCommandCache`) using `ConcurrentDictionary` with composite string keys that provides thread-safe, lock-free cache access for SQL command lookup and generation.

5. An `ILinqSqlDialect` abstraction that provides per-provider quoting, parameter, and syntax rules consumed by both the template tier (for skeleton generation) and the rendering tier (for entity-specific SQL), ensuring dialect consistency across cache tiers.

---

## 5. Implementation Evidence

- **Cache:** `src/Koan.Data.Relational/Linq/RelationalCommandCache.cs`
- **Dialect interface:** `ILinqSqlDialect` in `src/Koan.Data.Relational/Linq/`
- **Entity config bags:** `src/Koan.Data.Core/AggregateConfig.cs`, `src/Koan.Data.Core/AggregateBags.cs`
- **ADR:** `docs/decisions/DATA-0008-relational-command-caching.md`
- **Framework Version:** Koan Framework v0.6.3

---

## 6. Publication Notice

This document is published as a defensive disclosure to establish prior art. The inventor(s) dedicate this disclosure to the public domain and assert no patent rights over the described inventions. All rights to use, implement, and build upon these inventions are hereby granted to the public.

---

## Antagonist Review Log

### Pass 1
**Antagonist:** SQL caching is standard practice. EF Core caches query plans. The two-tier separation is an obvious optimization.

**Author revision:** EF Core caches LINQ-to-SQL compilation (expression tree → SQL), not SQL string composition. The disclosure describes caching at the SQL string level for non-LINQ operations (upserts, deletes, schema-specific commands). The two-tier separation is specific to multi-dialect frameworks where the same entity needs different SQL per provider. EF Core targets one provider per DbContext, so this separation doesn't apply. Added explicit EF Core distinction.

### Pass 2
**Antagonist:** No further objections. The two-tier separation for multi-dialect frameworks with compiled binders is sufficiently distinct from single-provider query caches.

### Final Status
✅ CLEARED — Antagonist found no further weaknesses. Safe to publish.

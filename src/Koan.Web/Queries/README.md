# Web Query Parsing Utilities

This directory contains **query string parsing and normalization** utilities for Koan Web APIs.

---

## 🔧 Available Utilities

### EntityQueryParser (Static Helper)

**File**: `EntityQueryParser.cs`
**Pattern**: Static parsing utilities extracted from EntityController
**When to Use**: Parsing HTTP query strings for filtering, sorting, pagination, field selection

#### What It Provides

One pass over an HTTP query collection, producing the `QueryOptions` the endpoint pipeline consumes:
free-text `q`, paging, sort specs resolved against the entity, shape, view, and declared extras. The
same parse backs every Entity endpoint, so the query grammar does not drift between surfaces.

#### Available Methods

```csharp
public static QueryOptions Parse<TEntity>(
    IQueryCollection query, EntityEndpointOptions defaults, bool lenient = false);

public static QueryOptions Parse(
    Type entityType, IQueryCollection query, EntityEndpointOptions defaults, bool lenient = false);
```

One call reads the whole query string into `QueryOptions`: free-text `q`, page and page size, sort
specs resolved against the entity, shape, view, and any extras the endpoint declared. There is no
per-concern parse to assemble.

Sort fields resolve against `TEntity`, so an unknown field throws `InvalidSortFieldException` --
which `EntityController` turns into a `400` naming the field. Pass `lenient: true` where an unknown
field should be dropped instead of refused.

```csharp
protected override QueryOptions BuildOptions()
{
    var options = base.BuildOptions();
    options.PageSize = Math.Min(options.PageSize, 25);
    return options;
}
```

`EntityController.BuildOptions()` is this call, so overriding it is the supported way to adjust the
parsed result before the query runs.

#### Common Use Cases

- A controller narrowing or extending what the parsed options say
- A non-Entity surface that wants Koan's query grammar over its own store
- Tests asserting how a query string resolves

---

## 📚 Related

- **ADR**: [ARCH-0068 - Refactoring Strategy](../../../docs/decisions/ARCH-0068-refactoring-strategy-static-vs-di.md) (P1.10)
- **Controller**: See `src/Koan.Web/Controllers/EntityController.cs` for usage
- **Query syntax**: URL-encoded JSON filters, `-`/`+` sort prefixes -- see below

---

## 💡 Query syntax

The parser reads `q`, `page`, `pageSize` (or `size`), `sort`, `dir`, `view`, `output`, and
`ignoreUnknownSort`. `filter` is read by the controller and passed alongside.

### Filtering

`filter` carries URL-encoded **JSON**, not an expression language:

```bash
curl --get --data-urlencode 'filter={"status":"Pending"}' http://localhost:5000/api/todos
```

`q` is a separate free-text slot, and the two compose. A malformed filter, an unknown field, or an
operator the adapter cannot execute answers `400` -- none of them degrades into an unfiltered read.

### Sorting

Comma-separated fields; `-` for descending, `+` or nothing for ascending:

```
?sort=createdAt
?sort=-createdAt
?sort=-priority,createdAt
```

Fields resolve against the entity, so an unknown one answers `400` naming the field. Pass
`?ignoreUnknownSort=true` to drop it instead; the skipped names come back in the options' extras.

### Paging

```
?page=2
?page=2&pageSize=100
?page=2&size=100        # size is the accepted alias, and what Link headers carry
```

Page size defaults to **50** and is capped at **200** unless the controller's `[Pagination]` or the
deployment's `Koan:Web:Pagination` bounds say otherwise. A request above the ceiling is clamped, not
refused. See the [pagination reference](../../../docs/reference/web/pagination.md).

### Shaping

`?view=` selects a declared view and `?output=` the output shape; both are validated against what the
endpoint allows. There is no field-projection parameter -- shape the payload with a view or a
transformer rather than a per-request field list.

---

## ❓ When to use what

| Scenario | Use this |
|----------|----------|
| Read a whole query string into options | `EntityQueryParser.Parse<TEntity>(query, defaults)` |
| Adjust the parsed options for one controller | override `EntityController.BuildOptions()` |
| Accept an unknown sort field instead of refusing | `Parse(..., lenient: true)` or `?ignoreUnknownSort=true` |
| Give a surface its own grammar | parse it yourself and fill `QueryOptions` |

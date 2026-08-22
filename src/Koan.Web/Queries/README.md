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
- **Query Syntax**: OData-inspired filtering (eq, ne, gt, lt, and, or, etc.)

---

## 💡 Query Syntax Examples

### Filter Expressions

```
# Simple equality
?filter=status eq 'active'

# Comparison operators
?filter=priority gt 5
?filter=createdAt ge '2024-01-01'

# Logical operators
?filter=status eq 'active' and priority gt 5
?filter=category eq 'work' or category eq 'personal'

# String operations
?filter=title contains 'meeting'
?filter=email endswith '@example.com'
```

### Sort Clauses

```
# Single field ascending (default)
?sort=createdAt

# Single field descending
?sort=createdAt desc

# Multiple fields
?sort=priority desc,createdAt asc

# Explicit ascending
?sort=title asc
```

### Pagination

```
# Page 1, default page size (20)
?page=1

# Page 2, custom page size
?page=2&pageSize=50

# Max page size enforced (defaults to 100)
?page=1&pageSize=999  # capped at 100
```

### Field Selection

```
# Select specific fields
?fields=id,title,status

# Reduce payload size
?fields=id,name  # returns only id and name
```

---

## ❓ When to Use What

| Scenario | Use This |
|----------|----------|
| Parse filter from query string | `EntityQueryParser.ParseFilter()` |
| Parse sort from query string | `EntityQueryParser.ParseSort()` |
| Validate pagination params | `EntityQueryParser.ParsePagination()` |
| Parse field selection | `EntityQueryParser.ParseFields()` |
| Custom query DSL | Implement your own parser |

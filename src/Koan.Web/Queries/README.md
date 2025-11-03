# Web Query Parsing Utilities

This directory contains **query string parsing and normalization** utilities for Koan Web APIs.

---

## 🔧 Available Utilities

### EntityQueryParser (Static Helper)

**File**: `EntityQueryParser.cs`
**Pattern**: Static parsing utilities extracted from EntityController
**When to Use**: Parsing HTTP query strings for filtering, sorting, pagination, field selection

#### What It Provides

- ✅ Parse filter expressions from query strings
- ✅ Parse sort clauses (multi-field, ascending/descending)
- ✅ Parse pagination parameters with validation
- ✅ Parse field selection for partial responses
- ✅ Consistent query syntax across all endpoints

#### Quick Example

```csharp
using Koan.Web.Queries;

[HttpGet]
public async Task<IActionResult> GetTodos(
    [FromQuery] string? filter,     // e.g., "status eq 'active' and priority gt 5"
    [FromQuery] string? sort,       // e.g., "createdAt desc,title asc"
    [FromQuery] int? page,          // e.g., 1
    [FromQuery] int? pageSize,      // e.g., 20
    [FromQuery] string? fields)     // e.g., "id,title,status"
{
    var filterClause = EntityQueryParser.ParseFilter(filter);
    var sortClause = EntityQueryParser.ParseSort(sort);
    var pagination = EntityQueryParser.ParsePagination(page, pageSize);
    var fieldSelection = EntityQueryParser.ParseFields(fields);

    var results = await _repository.QueryAsync(filterClause, sortClause, pagination);
    return Ok(results.Select(fieldSelection.Project));
}
```

#### Available Methods

```csharp
// Parse filter expressions (OData-like syntax)
public static FilterClause? ParseFilter(string? filter)

// Parse sort clauses (comma-separated, asc/desc)
public static SortClause? ParseSort(string? sort)

// Parse and validate pagination (with defaults and limits)
public static PaginationParams ParsePagination(
    int? page,
    int? pageSize,
    int defaultPageSize = 20,
    int maxPageSize = 100
)

// Parse field selection for sparse fieldsets
public static FieldSelection ParseFields(string? fields)
```

#### Common Use Cases

✅ Custom EntityController implementations
✅ GraphQL resolvers translating to repository queries
✅ API endpoints with flexible query capabilities
✅ Admin panels with dynamic filtering/sorting

**Full Documentation**: [Framework Utilities Guide](../../../docs/guides/framework-utilities.md#entityqueryparser)

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

---

**Last Updated**: 2025-11-03

# Pagination system reference

**Contract**

- **Audience**: API developers adjusting pagination behaviour across controllers.
- **Inputs**: Controllers built on `EntityController<T>` or manual endpoints that opt into Koan pagination helpers.
- **Outputs**: Declarative attributes, global bounds, and response contracts for consistent paging.
- **Error modes**: Overriding safety bounds, disabling pagination for large datasets, or mixing conflicting attribute settings across inheritance hierarchies.
- **Success criteria**: Consumers can configure pagination without manual plumbing, and responses stay within performance guardrails.

**Edge cases**

1. The `Optional` mode returns the full dataset unless clients supply paging params—avoid for large tables.
2. Disabling `IncludeCount` stops total counts even when clients expect them; update API docs accordingly.
3. Default sort expressions must map to indexed columns in relational stores; otherwise expect degraded performance.
4. Safety bounds apply across providers; raising `AbsoluteMaxRecords` impacts Mongo, SQL, and in-memory adapters alike.
5. Custom controllers calling `EntityEndpointService` should mirror `PaginationOptions` to avoid double filtering.

**Declarative pagination controls for EntityController endpoints with safety bounds and optimization.**

The Koan Framework provides a comprehensive pagination system through attributes, enabling fine-grained control over data retrieval patterns while maintaining performance and safety across all provider backends.

## Overview

The pagination system consists of four key components:

- **PaginationAttribute**: Declarative configuration on controllers/methods
- **PaginationMode**: Behavior modes (On, Required, Optional, Off)
- **PaginationSafetyBounds**: Global limits and constraints
- **QueryResult&lt;T&gt;**: Enhanced result type with pagination metadata

## Quick Start

```csharp
[Pagination(Mode = PaginationMode.On, DefaultSize = 20, MaxSize = 100)]
public class ProductsController : EntityController<Product>
{
    // GET /api/products?page=1&pageSize=20
    // Automatically paginated with safety bounds
}
```

## PaginationMode Options

### On (Default)

Always paginate with configurable limits. Clients can adjust page size within bounds.

```csharp
[Pagination(Mode = PaginationMode.On, DefaultSize = 25, MaxSize = 100)]
public class TodosController : EntityController<Todo>
{
    // GET /api/todos           → Returns first 25 items
    // GET /api/todos?page=2    → Returns items 26-50
    // GET /api/todos?pageSize=50 → Returns first 50 items (within MaxSize)
}
```

### Required

Always paginate, ignore client requests to disable pagination.

```csharp
[Pagination(Mode = PaginationMode.Required, DefaultSize = 10, MaxSize = 50)]
public class AuditLogController : EntityController<AuditLog>
{
    // GET /api/auditlog?all=true → Still paginated (security requirement)
    // Forces pagination for performance/security
}
```

### Optional

Paginate only when explicitly requested by client.

```csharp
[Pagination(Mode = PaginationMode.Optional, DefaultSize = 100)]
public class MetricsController : EntityController<Metric>
{
    // GET /api/metrics              → Returns all metrics
    // GET /api/metrics?page=1       → Returns paginated results
    // GET /api/metrics?pageSize=20  → Returns first 20 items
}
```

### Off

Never paginate - always return full dataset.

```csharp
[Pagination(Mode = PaginationMode.Off)]
public class StatusController : EntityController<SystemStatus>
{
    // GET /api/status → Always returns complete status list
    // Use for small, bounded datasets only
}
```

## Configuration Options

### Method-Level Override

Method attributes take precedence over controller-level settings:

```csharp
[Pagination(DefaultSize = 50)]  // Controller default
public class ProductsController : EntityController<Product>
{
    [Pagination(Mode = PaginationMode.Required, DefaultSize = 10)]
    public override async Task<ActionResult<QueryResult<Product>>> GetFiltered(
        [FromQuery] string? category)
    {
        // This endpoint uses Required mode with size 10
        return await base.GetFiltered(category);
    }
}
```

### Include Count Control

Control whether total record counts are calculated:

```csharp
[Pagination(IncludeCount = false)]  // Skip expensive COUNT queries
public class BigDataController : EntityController<LogEntry>
{
    // Returns pagination metadata without TotalCount
    // Improves performance for large datasets
}
```

### Default Sorting

Specify default sort order for consistent pagination:

```csharp
[Pagination(DefaultSort = "-createdAt,name")]
public class EventsController : EntityController<Event>
{
    // Ensures consistent pagination across requests
    // Sorts by createdAt descending, then name ascending
}
```

## Safety Bounds Configuration

Configure global limits in `appsettings.json`:

```json
{
  "Pagination": {
    "MinPageSize": 1,
    "MaxPageSize": 500,
    "AbsoluteMaxRecords": 10000
  }
}
```

Register with dependency injection:

```csharp
builder.Services.AddPaginationSafetyBounds(builder.Configuration);
```

Safety bounds ensure:

- No page sizes below MinPageSize or above MaxPageSize
- Total query results never exceed AbsoluteMaxRecords
- Framework prevents memory exhaustion and poor performance

## QueryResult&lt;T&gt; Response Format

The framework returns enhanced metadata for paginated endpoints:

```csharp
public class QueryResult<T>
{
    public IReadOnlyList<T> Items { get; init; }
    public int TotalCount { get; init; }
    public int Page { get; init; }
    public int PageSize { get; init; }
    public int TotalPages => // Calculated
    public bool HasNextPage => // Calculated
    public bool HasPreviousPage => // Calculated
}
```

### Example Response

```json
{
  "items": [
    { "id": "01234567-89ab-cdef-0123-456789abcdef", "name": "Product 1" },
    { "id": "01234567-89ab-cdef-0123-456789abcdf0", "name": "Product 2" }
  ],
  "totalCount": 150,
  "page": 1,
  "pageSize": 20,
  "totalPages": 8,
  "hasNextPage": true,
  "hasPreviousPage": false
}
```

## Performance Optimizations

### Provider-Specific Optimization

The framework automatically detects provider capabilities:

```csharp
// PostgreSQL: Uses LIMIT/OFFSET with count optimization
// MongoDB: Uses skip/limit with efficient counting
// SQLite: Uses appropriate pagination strategy
// All providers: Automatic query plan optimization
```

### Single-Query Optimization

When providers support `IPagedRepository`, the framework uses optimized single-query pagination:

```csharp
// Traditional approach: 2 queries (data + count)
// Optimized approach: 1 query with window functions (PostgreSQL)
// Automatic fallback for providers without optimization support
```

## Client Usage Patterns

### Query Parameters

Standard pagination parameters work across all endpoints:

```http
GET /api/products?page=2&pageSize=50
GET /api/products?page=1&pageSize=25&sort=name
GET /api/products?pageSize=100  # page defaults to 1
```

### Sorting

Consistent sort syntax across all endpoints:

```http
# Single field ascending
GET /api/products?sort=name

# Single field descending
GET /api/products?sort=-price

# Multiple fields
GET /api/products?sort=category,-price,name
```

## Best Practices

### Choose Appropriate Modes

```csharp
// Large datasets - always paginate
[Pagination(Mode = PaginationMode.Required, DefaultSize = 20, MaxSize = 100)]
public class TransactionsController : EntityController<Transaction> { }

// Reference data - optional pagination
[Pagination(Mode = PaginationMode.Optional, DefaultSize = 1000)]
public class CountriesController : EntityController<Country> { }

// Small bounded sets - no pagination
[Pagination(Mode = PaginationMode.Off)]
public class UserRolesController : EntityController<UserRole> { }
```

### Performance Considerations

```csharp
// Large tables - disable count for performance
[Pagination(IncludeCount = false, DefaultSize = 50)]
public class LogsController : EntityController<LogEntry> { }

// Ensure consistent sort for stable pagination
[Pagination(DefaultSort = "id")]  // Use indexed column
public class ProductsController : EntityController<Product> { }
```

### Security Considerations

```csharp
// Sensitive data - force small page sizes
[Pagination(Mode = PaginationMode.Required, MaxSize = 25)]
public class PersonalDataController : EntityController<PersonalData> { }

// Public APIs - reasonable defaults
[Pagination(DefaultSize = 50, MaxSize = 200)]
public class PublicProductsController : EntityController<Product> { }
```

## Migration from Legacy Patterns

### From KoanDataBehaviorAttribute

**Before:**

```csharp
[KoanDataBehavior(DisablePagination = true)]
public class LegacyController : EntityController<Item> { }
```

**After:**

```csharp
[Pagination(Mode = PaginationMode.Off)]
public class ModernController : EntityController<Item> { }
```

### Migration Mapping

- `DisablePagination = true` → `Mode = PaginationMode.Off`
- `DisablePagination = false` → `Mode = PaginationMode.On`
- No attribute → Uses framework defaults (PaginationMode.On)

### Compatibility Period

The framework maintains backward compatibility:

- Existing `KoanDataBehaviorAttribute` continues to work
- New `PaginationAttribute` takes precedence when both are present
- Migration warnings logged in development mode

## Advanced Scenarios

### Custom Pagination Logic

Override pagination behavior for specific business requirements:

```csharp
public class CustomController : EntityController<Product>
{
    protected override PaginationPolicy GetPaginationPolicy()
    {
        // Custom logic based on user role, tenant, etc.
        if (User.IsInRole("Admin"))
        {
            return new PaginationPolicy
            {
                Mode = PaginationMode.Optional,
                DefaultSize = 100,
                MaxSize = 1000,
                IncludeCount = true,
                AbsoluteMaxRecords = 50000
            };
        }

        return base.GetPaginationPolicy();
    }
}
```

### Tenant-Specific Configuration

Different pagination rules per tenant:

```csharp
[Pagination(Mode = PaginationMode.On)]
public class TenantAwareController : EntityController<Order>
{
    protected override async Task<QueryResult<Order>> ExecuteQuery(
        object? query, DataQueryOptions options)
    {
        // Adjust pagination based on tenant limits
        var tenantLimits = await GetTenantLimits();
        options.PageSize = Math.Min(options.PageSize, tenantLimits.MaxPageSize);

        return await base.ExecuteQuery(query, options);
    }
}
```

## Troubleshooting

### Common Issues

**Issue**: Pagination not working
**Solution**: Ensure `services.AddPaginationSafetyBounds()` is called during startup

**Issue**: Count queries too slow
**Solution**: Use `IncludeCount = false` for large datasets

**Issue**: Inconsistent pagination results
**Solution**: Always specify `DefaultSort` for stable ordering

### Debug Information

Enable detailed pagination logging:

```json
{
  "Logging": {
    "LogLevel": {
      "Koan.Web.Pagination": "Debug"
    }
  }
}
```

This enables detailed logs showing:

- Pagination policy resolution
- Safety bounds application
- Query optimization decisions
- Performance metrics

---

The pagination system provides enterprise-grade data access controls while maintaining the simplicity of Koan's entity-first development patterns.

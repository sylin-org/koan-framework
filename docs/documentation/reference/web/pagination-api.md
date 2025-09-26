# Pagination API Reference

## Koan.Web.Attributes

### PaginationAttribute

Declarative attribute for configuring pagination behavior on EntityController endpoints.

```csharp
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method,
                Inherited = true, AllowMultiple = false)]
public sealed class PaginationAttribute : Attribute
```

#### Properties

| Property | Type | Default | Description |
|----------|------|---------|-------------|
| `Mode` | `PaginationMode` | `PaginationMode.On` | Pagination behavior mode |
| `DefaultSize` | `int` | `KoanWebConstants.Defaults.DefaultPageSize` | Default number of items per page |
| `MaxSize` | `int` | `KoanWebConstants.Defaults.MaxPageSize` | Maximum allowed page size |
| `IncludeCount` | `bool` | `true` | Whether to include total count in responses |
| `DefaultSort` | `string?` | `null` | Default sort order (e.g., "-createdAt,name") |

#### Usage Examples

```csharp
// Basic pagination
[Pagination]
public class ProductsController : EntityController<Product> { }

// Custom configuration
[Pagination(Mode = PaginationMode.Required, DefaultSize = 10, MaxSize = 50)]
public class AuditController : EntityController<AuditLog> { }

// Method-level override
[Pagination(DefaultSize = 25)]
public class OrdersController : EntityController<Order>
{
    [Pagination(Mode = PaginationMode.Off)]
    public async Task<ActionResult<List<Order>>> GetSummary()
    {
        // This method never paginates
    }
}
```

### PaginationMode

Enumeration defining pagination behavior modes.

```csharp
public enum PaginationMode
{
    On = 0,       // Always paginate with configurable limits
    Required = 1, // Always paginate, ignore disable requests
    Optional = 2, // Paginate only when explicitly requested
    Off = 3       // Never paginate - full dataset
}
```

#### Mode Behaviors

| Mode | Client Request | Behavior |
|------|----------------|----------|
| `On` | `?page=1` | Paginated with DefaultSize |
| `On` | `?pageSize=50` | Paginated with specified size (within MaxSize) |
| `On` | `?all=true` | Still paginated (safety bounds enforced) |
| `Required` | `?all=true` | Ignores client request, always paginates |
| `Optional` | No pagination params | Returns all results |
| `Optional` | `?page=1` | Paginated with DefaultSize |
| `Off` | Any params | Always returns complete dataset |

### PaginationPolicy

Runtime resolved pagination policy with safety bounds applied.

```csharp
public sealed record PaginationPolicy
{
    public required PaginationMode Mode { get; init; }
    public required int DefaultSize { get; init; }
    public required int MaxSize { get; init; }
    public required bool IncludeCount { get; init; }
    public required int AbsoluteMaxRecords { get; init; }
    public string? DefaultSort { get; init; }
}
```

#### Static Methods

##### FromAttribute
Creates policy from attribute with safety bounds applied.

```csharp
public static PaginationPolicy FromAttribute(
    PaginationAttribute attr,
    PaginationSafetyBounds safety)
```

**Parameters:**
- `attr`: PaginationAttribute configuration
- `safety`: Global safety bounds

**Returns:** PaginationPolicy with bounds enforced

##### Resolve
Resolves policy from service provider and optional attribute.

```csharp
public static PaginationPolicy Resolve(
    IServiceProvider services,
    PaginationAttribute? attribute)
```

**Parameters:**
- `services`: Service provider for accessing safety bounds
- `attribute`: Optional pagination attribute (uses defaults if null)

**Returns:** Fully resolved PaginationPolicy

## Koan.Web.Infrastructure

### PaginationSafetyBounds

Configuration class for global pagination limits and safety constraints.

```csharp
public sealed class PaginationSafetyBounds
{
    public int MinPageSize { get; set; } = 1;
    public int MaxPageSize { get; set; } = KoanWebConstants.Defaults.MaxPageSize;
    public int AbsoluteMaxRecords { get; set; } = 10_000;
}
```

#### Properties

| Property | Type | Default | Description |
|----------|------|---------|-------------|
| `MinPageSize` | `int` | `1` | Minimum allowed page size |
| `MaxPageSize` | `int` | Framework default | Maximum allowed page size |
| `AbsoluteMaxRecords` | `int` | `10_000` | Maximum total records in any query |

#### Static Properties

##### Default
Provides default safety bounds instance.

```csharp
public static PaginationSafetyBounds Default { get; }
```

#### Instance Methods

##### Clone
Creates a deep copy of the safety bounds.

```csharp
public PaginationSafetyBounds Clone()
```

**Returns:** New PaginationSafetyBounds instance with copied values

#### Configuration

Configure in `appsettings.json`:

```json
{
  "Pagination": {
    "MinPageSize": 1,
    "MaxPageSize": 500,
    "AbsoluteMaxRecords": 10000
  }
}
```

Register with DI container:

```csharp
services.AddPaginationSafetyBounds(configuration);
```

### PaginationServiceCollectionExtensions

Extension methods for configuring pagination services.

```csharp
public static class PaginationServiceCollectionExtensions
```

#### Methods

##### AddPaginationSafetyBounds
Registers PaginationSafetyBounds with configuration binding and validation.

```csharp
public static IServiceCollection AddPaginationSafetyBounds(
    this IServiceCollection services,
    IConfiguration configuration)
```

**Parameters:**
- `services`: Service collection
- `configuration`: Configuration containing "Pagination" section

**Returns:** Service collection for method chaining

**Configuration:**
- Binds `PaginationSafetyBounds` to "Pagination" configuration section
- Applies post-configuration validation:
  - `MinPageSize` ≥ 1
  - `MaxPageSize` between `MinPageSize` and 1,000
  - `AbsoluteMaxRecords` ≥ `MaxPageSize`

## Koan.Data.Core

### QueryResult&lt;T&gt;

Enhanced result type providing pagination metadata with entity results.

```csharp
public sealed class QueryResult<TEntity>
{
    public required IReadOnlyList<TEntity> Items { get; init; }
    public required int TotalCount { get; init; }
    public required int Page { get; init; }
    public required int PageSize { get; init; }
    public int TotalPages => PageSize <= 0 ? 0 : (int)Math.Ceiling((double)TotalCount / PageSize);
    public bool HasNextPage => Page < TotalPages;
    public bool HasPreviousPage => Page > 1;
}
```

#### Properties

| Property | Type | Description |
|----------|------|-------------|
| `Items` | `IReadOnlyList<TEntity>` | Entity results for current page |
| `TotalCount` | `int` | Total number of entities across all pages |
| `Page` | `int` | Current page number (1-based) |
| `PageSize` | `int` | Number of items per page |
| `TotalPages` | `int` | Total number of pages (calculated) |
| `HasNextPage` | `bool` | Whether additional pages exist (calculated) |
| `HasPreviousPage` | `bool` | Whether previous pages exist (calculated) |

#### Usage Examples

```csharp
// EntityController automatically returns QueryResult<T>
public class ProductsController : EntityController<Product>
{
    // GET /api/products returns QueryResult<Product>
}

// Custom usage in business logic
public async Task<QueryResult<Order>> GetOrdersByStatus(string status)
{
    return await Data<Order, string>.QueryWithCount(
        new { Status = status },
        new DataQueryOptions { Page = 1, PageSize = 20 });
}
```

### Data&lt;T,K&gt; Extensions

Enhanced data access methods supporting pagination.

#### QueryWithCount
Executes query with pagination and count optimization.

```csharp
public static async Task<QueryResult<TEntity>> QueryWithCount(
    object? query,
    DataQueryOptions? options,
    CancellationToken ct = default,
    int? absoluteMaxRecords = null)
```

**Parameters:**
- `query`: Query object/criteria
- `options`: Query options including pagination settings
- `ct`: Cancellation token
- `absoluteMaxRecords`: Override for safety bounds

**Returns:** QueryResult&lt;TEntity&gt; with items and metadata

**Optimization:** Automatically uses `IPagedRepository` single-query optimization when available.

## Koan.Data.Abstractions

### IPagedRepository&lt;T,K&gt;

Optimized repository interface for single-query pagination support.

```csharp
public interface IPagedRepository<TEntity, TKey>
    where TEntity : IEntity<TKey>
{
    Task<PagedRepositoryResult<TEntity>> QueryPageAsync(
        object? query,
        DataQueryOptions options,
        CancellationToken ct = default);
}
```

#### Methods

##### QueryPageAsync
Executes optimized single-query pagination.

**Parameters:**
- `query`: Query criteria
- `options`: Query options with pagination settings
- `ct`: Cancellation token

**Returns:** PagedRepositoryResult&lt;TEntity&gt; with items and metadata

**Implementation Notes:**
- PostgreSQL: Uses window functions for single-query pagination
- MongoDB: Uses aggregation pipeline with count
- Other providers: Falls back to separate count query

### PagedRepositoryResult&lt;T&gt;

Result type from optimized paged repository queries.

```csharp
public sealed class PagedRepositoryResult<TEntity>
{
    public required IReadOnlyList<TEntity> Items { get; init; }
    public required int TotalCount { get; init; }
}
```

## EntityController Integration

### GetPaginationPolicy
Override to customize pagination policy resolution.

```csharp
protected virtual PaginationPolicy GetPaginationPolicy()
```

**Returns:** PaginationPolicy for current request

**Default Resolution Order:**
1. Method-level PaginationAttribute
2. Controller-level PaginationAttribute
3. Legacy KoanDataBehaviorAttribute compatibility
4. Framework defaults

#### Customization Example

```csharp
public class CustomController : EntityController<Product>
{
    protected override PaginationPolicy GetPaginationPolicy()
    {
        // Role-based pagination limits
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

## Client Query Parameters

Standard pagination parameters supported across all EntityController endpoints:

| Parameter | Type | Description | Example |
|-----------|------|-------------|---------|
| `page` | `int` | Page number (1-based) | `?page=2` |
| `pageSize` | `int` | Items per page | `?pageSize=50` |
| `sort` | `string` | Sort specification | `?sort=-createdAt,name` |
| `all` | `bool` | Request all results (mode-dependent) | `?all=true` |

### Sort Syntax

| Format | Description | Example |
|--------|-------------|---------|
| `field` | Ascending sort | `sort=name` |
| `-field` | Descending sort | `sort=-price` |
| `field1,field2` | Multiple fields | `sort=category,name` |
| `-field1,field2` | Mixed directions | `sort=-createdAt,name` |

---

**See also:**
- [Pagination System Guide](../guides/pagination-system.md) - Developer guide with examples
- [Building APIs Guide](../guides/building-apis.md) - EntityController patterns
- [Data Modeling Guide](../guides/data-modeling.md) - Entity patterns and queries
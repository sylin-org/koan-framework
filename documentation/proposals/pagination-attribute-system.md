# Pagination Attribute System Proposal

**Status**: Draft
**Author**: Koan Framework Team
**Date**: 2025-01-25
**Target Version**: Koan Framework v2.0

## Executive Summary

This proposal introduces a declarative attribute-based pagination system for EntityController endpoints, replacing the current inconsistent approach with explicit, type-safe policies that ensure both safety and flexibility at API design time.

## Problem Statement

### Current Architecture Issues

1. **Binary Pagination Trap**: Current system forces developers to choose between "always paginate" or "never paginate" with no middle ground
2. **Dangerous Defaults**: EntityController endpoints can accidentally return millions of records with no safeguards
3. **Implicit Behavior**: No clear way to determine pagination behavior by examining controller code
4. **Framework vs User Intent Confusion**: MaxPageSize enforcement applied to internal operations (AllStream) where it shouldn't apply
5. **Inconsistent DX**: Similar endpoints behave differently based on hidden implementation details

### Real-World Impact

```csharp
// Current problematic scenarios:

// 1. Accidental full scan - dangerous
public class UsersController : EntityController<User>
{
    // GET /api/users returns ALL users (potentially millions)
}

// 2. Inconsistent behavior based on implementation details
await User.All();        // No limits after recent fix
await User.Query(null);  // May or may not paginate depending on adapter
```

## Proposed Solution: Declarative Pagination Policies

### Core Design Principles

1. **Explicit Over Implicit**: Pagination behavior declared at compile time
2. **Safe by Default**: Framework provides sensible defaults that prevent accidents
3. **Intent-Driven**: Attribute names clearly express developer intent
4. **Flexible Override**: Method-level overrides for special cases
5. **Type-Safe**: Compile-time validation of pagination policies

### Attribute Definition

```csharp
public enum PaginationMode
{
    /// <summary>
    /// Framework default: Always paginate with configurable limits.
    /// Users can adjust page size via query params within MaxSize bounds.
    /// </summary>
    Auto = 0,

    /// <summary>
    /// Always paginate, users cannot disable pagination.
    /// Suitable for large datasets where full scans are dangerous.
    /// </summary>
    Required = 1,

    /// <summary>
    /// Paginate only when user explicitly requests it via query parameters.
    /// Without pagination params, returns full dataset.
    /// </summary>
    Optional = 2,

    /// <summary>
    /// Never paginate - always return full dataset.
    /// Dangerous for large datasets, use only for small reference data.
    /// </summary>
    Off = 3,

    /// <summary>
    /// Stream response using IAsyncEnumerable or chunked transfer.
    /// Suitable for real-time feeds or large dataset exports.
    /// </summary>
    Streaming = 4
}

[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method, Inherited = true)]
public class PaginationAttribute : Attribute
{
    /// <summary>Pagination behavior mode</summary>
    public PaginationMode Mode { get; set; } = PaginationMode.Auto;

    /// <summary>Default page size when pagination is active</summary>
    public int DefaultSize { get; set; } = 50;

    /// <summary>Maximum allowed page size</summary>
    public int MaxSize { get; set; } = 1000;

    /// <summary>Include total count in paginated responses (affects performance)</summary>
    public bool IncludeCount { get; set; } = true;

    /// <summary>Default sort order when none specified</summary>
    public string? DefaultSort { get; set; }
}
```

## Detailed Behavior Specification

### Mode Behavior Matrix

| Mode | No Query Params | ?page=2&pageSize=100 | ?all=true | Response Headers |
|------|-----------------|---------------------|-----------|------------------|
| **Auto** | Paginate (page=1, size=50) | Honor params (≤MaxSize) | Ignore, paginate | X-Total-Count, X-Page, X-PageSize |
| **Required** | Paginate (page=1, size=50) | Honor params (≤MaxSize) | Ignore, paginate | X-Total-Count, X-Page, X-PageSize |
| **Optional** | Return all records | Apply pagination | Return all | X-Total-Count only if paginated |
| **Off** | Return all records | Ignore params | Return all | No pagination headers |
| **Streaming** | Stream all records | Ignore params | Stream all | Transfer-Encoding: chunked |

### Query Parameter Handling

**Standard Parameters**:
- `page` (int): 1-based page number
- `pageSize` (int): Records per page (subject to MaxSize)
- `filter` (string): Filter expression
- `sort` (string): Sort specification
- `all` (bool): Explicit request for all records (ignored by Required/Off modes)

**Parameter Validation**:
- `page < 1` → defaults to 1
- `pageSize > MaxSize` → clamped to MaxSize
- `pageSize < 1` → defaults to DefaultSize
- Invalid filter/sort → 400 Bad Request

## Implementation Plan

### Phase 1: Core Infrastructure

#### 1.1 Enhanced DataQueryOptions

```csharp
public class DataQueryOptions
{
    // Nullable to distinguish between "not specified" and "specified as 1/50"
    public int? Page { get; set; }
    public int? PageSize { get; set; }
    public string? Filter { get; set; }
    public string? Sort { get; set; }
    public string? Set { get; set; }

    // Computed properties
    public bool HasPagination => Page.HasValue || PageSize.HasValue;
    public int EffectivePage(int defaultValue = 1) => Page ?? defaultValue;
    public int EffectivePageSize(int defaultValue = 50) => PageSize ?? defaultValue;

    // Factory methods
    public static DataQueryOptions FromQueryString(IQueryCollection query)
    {
        return new DataQueryOptions
        {
            Page = query.TryGetValue("page", out var p) && int.TryParse(p, out var pageNum) && pageNum > 0 ? pageNum : null,
            PageSize = query.TryGetValue("pageSize", out var ps) && int.TryParse(ps, out var sizeNum) && sizeNum > 0 ? sizeNum : null,
            Filter = query.TryGetValue("filter", out var f) ? f.ToString() : null,
            Sort = query.TryGetValue("sort", out var s) ? s.ToString() : null
        };
    }

    public DataQueryOptions WithPagination(int page, int pageSize)
    {
        return new DataQueryOptions
        {
            Page = page,
            PageSize = pageSize,
            Filter = this.Filter,
            Sort = this.Sort,
            Set = this.Set
        };
    }
}
```

#### 1.2 QueryResult Types

```csharp
public class QueryResult<T>
{
    public IReadOnlyList<T> Items { get; set; } = Array.Empty<T>();
    public int TotalCount { get; set; }
    public int Page { get; set; }
    public int PageSize { get; set; }
    public int TotalPages => (int)Math.Ceiling((double)TotalCount / PageSize);
    public bool HasNextPage => Page < TotalPages;
    public bool HasPreviousPage => Page > 1;
}

public class StreamResult<T>
{
    public IAsyncEnumerable<T> Items { get; set; } = AsyncEnumerable.Empty<T>();
    public string? ContentType { get; set; } = "application/json";
    public long? EstimatedCount { get; set; }
}
```

#### 1.3 Data Layer Updates

```csharp
// Enhanced Data<T,K> methods
public static class Data<TEntity, TKey>
    where TEntity : class, IEntity<TKey>
    where TKey : notnull
{
    // Existing methods remain unchanged for backward compatibility
    public static Task<IReadOnlyList<TEntity>> All(CancellationToken ct = default)
        => Repo.QueryAsync(null, ct);

    // New methods for pagination-aware queries
    public static async Task<QueryResult<TEntity>> QueryWithCount(
        object? query,
        DataQueryOptions? options,
        CancellationToken ct = default)
    {
        if (options?.HasPagination == true)
        {
            // Apply pagination with count
            var countTask = Repo.CountAsync(query, ct);
            var itemsTask = Repo.QueryAsync(query, options, ct);

            await Task.WhenAll(countTask, itemsTask);

            return new QueryResult<TEntity>
            {
                Items = itemsTask.Result,
                TotalCount = countTask.Result,
                Page = options.EffectivePage(),
                PageSize = options.EffectivePageSize()
            };
        }
        else
        {
            // Return all without pagination
            var items = await Repo.QueryAsync(query, ct);
            return new QueryResult<TEntity>
            {
                Items = items,
                TotalCount = items.Count,
                Page = 1,
                PageSize = items.Count
            };
        }
    }

    public static StreamResult<TEntity> QueryStream(
        object? query,
        DataQueryOptions? options,
        CancellationToken ct = default)
    {
        var stream = query == null
            ? AllStream(batchSize: null, ct)
            : QueryStreamInternal(query, options, ct);

        return new StreamResult<TEntity>
        {
            Items = stream,
            ContentType = "application/json"
        };
    }

    private static async IAsyncEnumerable<TEntity> QueryStreamInternal(
        object? query,
        DataQueryOptions? options,
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        // Implementation depends on whether adapter supports streaming queries
        // For now, fallback to loading all and streaming
        var items = await Repo.QueryAsync(query, ct);
        foreach (var item in items)
        {
            ct.ThrowIfCancellationRequested();
            yield return item;
        }
    }
}
```

### Phase 2: EntityController Integration

#### 2.1 Base EntityController Updates

```csharp
public abstract class EntityController<T> : EntityController<T, string>
    where T : class, IEntity<string>
{
}

public abstract class EntityController<T, TKey> : ControllerBase
    where T : class, IEntity<TKey>
    where TKey : notnull
{
    protected virtual PaginationAttribute GetPaginationPolicy()
    {
        // Check method-level attribute first
        var methodInfo = ControllerContext.ActionDescriptor.MethodInfo;
        var methodAttr = methodInfo.GetCustomAttribute<PaginationAttribute>();
        if (methodAttr != null) return methodAttr;

        // Check controller-level attribute
        var controllerAttr = GetType().GetCustomAttribute<PaginationAttribute>();
        if (controllerAttr != null) return controllerAttr;

        // Framework default
        return new PaginationAttribute { Mode = PaginationMode.Auto };
    }

    [HttpGet]
    public virtual async Task<IActionResult> Get(
        [FromQuery] int? page = null,
        [FromQuery] int? pageSize = null,
        [FromQuery] string? filter = null,
        [FromQuery] string? sort = null,
        [FromQuery] bool? all = null,
        CancellationToken ct = default)
    {
        var policy = GetPaginationPolicy();
        var options = DataQueryOptions.FromQueryString(Request.Query);

        return policy.Mode switch
        {
            PaginationMode.Auto => await HandleAutoMode(options, policy, ct),
            PaginationMode.Required => await HandleRequiredMode(options, policy, ct),
            PaginationMode.Optional => await HandleOptionalMode(options, policy, all, ct),
            PaginationMode.Off => await HandleOffMode(options, ct),
            PaginationMode.Streaming => HandleStreamingMode(options, policy, ct),
            _ => throw new InvalidOperationException($"Unknown pagination mode: {policy.Mode}")
        };
    }

    private async Task<IActionResult> HandleAutoMode(
        DataQueryOptions options,
        PaginationAttribute policy,
        CancellationToken ct)
    {
        // Always paginate, but allow user to customize within limits
        var page = options.EffectivePage(1);
        var pageSize = Math.Min(options.EffectivePageSize(policy.DefaultSize), policy.MaxSize);

        var paginatedOptions = options.WithPagination(page, pageSize);
        var result = await Data<T, TKey>.QueryWithCount(null, paginatedOptions, ct);

        AddPaginationHeaders(result, policy);
        return Ok(result.Items);
    }

    private async Task<IActionResult> HandleRequiredMode(
        DataQueryOptions options,
        PaginationAttribute policy,
        CancellationToken ct)
    {
        // Force pagination regardless of query params
        var page = Math.Max(options.EffectivePage(1), 1);
        var pageSize = Math.Min(options.EffectivePageSize(policy.DefaultSize), policy.MaxSize);

        var paginatedOptions = options.WithPagination(page, pageSize);
        var result = await Data<T, TKey>.QueryWithCount(null, paginatedOptions, ct);

        AddPaginationHeaders(result, policy);
        return Ok(result.Items);
    }

    private async Task<IActionResult> HandleOptionalMode(
        DataQueryOptions options,
        PaginationAttribute policy,
        bool? all,
        CancellationToken ct)
    {
        if (options.HasPagination && all != true)
        {
            // User requested pagination
            var page = options.EffectivePage(1);
            var pageSize = Math.Min(options.EffectivePageSize(policy.DefaultSize), policy.MaxSize);

            var paginatedOptions = options.WithPagination(page, pageSize);
            var result = await Data<T, TKey>.QueryWithCount(null, paginatedOptions, ct);

            AddPaginationHeaders(result, policy);
            return Ok(result.Items);
        }
        else
        {
            // Return all records
            var result = await Data<T, TKey>.QueryWithCount(null, null, ct);

            if (policy.IncludeCount)
            {
                Response.Headers.Add("X-Total-Count", result.TotalCount.ToString());
            }

            return Ok(result.Items);
        }
    }

    private async Task<IActionResult> HandleOffMode(DataQueryOptions options, CancellationToken ct)
    {
        // Never paginate - always return all
        var items = await Data<T, TKey>.All(ct);
        return Ok(items);
    }

    private IActionResult HandleStreamingMode(
        DataQueryOptions options,
        PaginationAttribute policy,
        CancellationToken ct)
    {
        var stream = Data<T, TKey>.QueryStream(null, options, ct);

        Response.ContentType = stream.ContentType ?? "application/json";

        if (stream.EstimatedCount.HasValue)
        {
            Response.Headers.Add("X-Estimated-Count", stream.EstimatedCount.Value.ToString());
        }

        return Ok(stream.Items);
    }

    private void AddPaginationHeaders(QueryResult<T> result, PaginationAttribute policy)
    {
        Response.Headers.Add("X-Total-Count", result.TotalCount.ToString());
        Response.Headers.Add("X-Page", result.Page.ToString());
        Response.Headers.Add("X-Page-Size", result.PageSize.ToString());
        Response.Headers.Add("X-Total-Pages", result.TotalPages.ToString());
        Response.Headers.Add("X-Has-Next-Page", result.HasNextPage.ToString().ToLower());
        Response.Headers.Add("X-Has-Previous-Page", result.HasPreviousPage.ToString().ToLower());
    }
}
```

### Phase 3: Framework Integration

#### 3.1 OpenAPI Integration

```csharp
public class PaginationOperationFilter : IOperationFilter
{
    public void Apply(OpenApiOperation operation, OperationFilterContext context)
    {
        var paginationAttr = context.MethodInfo.GetCustomAttribute<PaginationAttribute>()
                           ?? context.MethodInfo.DeclaringType?.GetCustomAttribute<PaginationAttribute>();

        if (paginationAttr == null) return;

        switch (paginationAttr.Mode)
        {
            case PaginationMode.Auto:
            case PaginationMode.Required:
                AddPaginationParameters(operation, paginationAttr);
                AddPaginationResponses(operation, paginationAttr);
                break;

            case PaginationMode.Optional:
                AddOptionalPaginationParameters(operation, paginationAttr);
                AddPaginationResponses(operation, paginationAttr);
                break;

            case PaginationMode.Streaming:
                AddStreamingResponse(operation);
                break;

            // Off mode needs no special handling
        }
    }

    private void AddPaginationParameters(OpenApiOperation operation, PaginationAttribute attr)
    {
        operation.Parameters.Add(new OpenApiParameter
        {
            Name = "page",
            In = ParameterLocation.Query,
            Description = "Page number (1-based)",
            Schema = new OpenApiSchema { Type = "integer", Minimum = 1, Default = new OpenApiInteger(1) }
        });

        operation.Parameters.Add(new OpenApiParameter
        {
            Name = "pageSize",
            In = ParameterLocation.Query,
            Description = $"Page size (max {attr.MaxSize})",
            Schema = new OpenApiSchema { Type = "integer", Minimum = 1, Maximum = attr.MaxSize, Default = new OpenApiInteger(attr.DefaultSize) }
        });
    }
}
```

## Usage Examples

### Example Controllers

```csharp
// Default behavior - safe pagination
public class ProductsController : EntityController<Product>
{
    // GET /api/products returns paginated results (page=1, pageSize=50)
    // GET /api/products?page=2&pageSize=25 returns page 2 with 25 items
}

// Large dataset - force pagination
[Pagination(Mode = Required, DefaultSize = 25, MaxSize = 100)]
public class TransactionsController : EntityController<Transaction>
{
    // Always paginated, users cannot disable
    // GET /api/transactions?all=true still returns paginated results
}

// Small reference data - allow full scan
[Pagination(Mode = Off)]
public class CountriesController : EntityController<Country>
{
    // GET /api/countries returns all countries (assuming small dataset)
}

// Flexible behavior based on request
[Pagination(Mode = Optional, DefaultSize = 50, MaxSize = 500)]
public class UsersController : EntityController<User>
{
    // GET /api/users returns ALL users (dangerous but explicit)
    // GET /api/users?page=1 returns paginated results

    // Method-level override for admin endpoint
    [HttpGet("admin")]
    [Pagination(Mode = Required, MaxSize = 50)]
    public async Task<IActionResult> GetAdminUsers()
    {
        // Always paginated for admin view
        return await base.Get();
    }
}

// Streaming for large datasets
[Pagination(Mode = Streaming)]
public class LogsController : EntityController<AuditLog>
{
    // Returns IAsyncEnumerable<AuditLog> - client must handle streaming
}
```

### Client Usage

```typescript
// TypeScript client examples

// Standard paginated endpoint
interface PagedResponse<T> {
  items: T[];
  totalCount: number;
  page: number;
  pageSize: number;
  totalPages: number;
}

async function getProducts(page = 1, pageSize = 50): Promise<PagedResponse<Product>> {
  const response = await fetch(`/api/products?page=${page}&pageSize=${pageSize}`);
  const items = await response.json();

  return {
    items,
    totalCount: parseInt(response.headers.get('X-Total-Count') || '0'),
    page: parseInt(response.headers.get('X-Page') || '1'),
    pageSize: parseInt(response.headers.get('X-Page-Size') || '50'),
    totalPages: parseInt(response.headers.get('X-Total-Pages') || '1')
  };
}

// Non-paginated endpoint
async function getAllCountries(): Promise<Country[]> {
  const response = await fetch('/api/countries');
  return response.json();
}

// Streaming endpoint
async function streamLogs(): Promise<AuditLog[]> {
  const response = await fetch('/api/logs');
  const reader = response.body?.getReader();
  const logs: AuditLog[] = [];

  // Handle streaming response
  while (reader) {
    const { done, value } = await reader.read();
    if (done) break;

    // Parse JSON lines or handle chunked transfer
    const chunk = new TextDecoder().decode(value);
    // ... parsing logic
  }

  return logs;
}
```

## Migration Strategy

### Phase 1: Backward Compatibility (v2.0)

1. **Existing Controllers**: Continue working without changes (Auto mode default)
2. **New Attribute**: Add `[Pagination]` attribute support alongside existing behavior
3. **Opt-In Enhancement**: Teams can gradually add attributes to controllers

### Phase 2: Framework Default (v2.1)

1. **Auto Mode Default**: All EntityController instances use Auto mode unless explicitly configured
2. **Deprecation Warnings**: Log warnings for controllers without explicit pagination attributes
3. **Documentation**: Provide migration guide for common scenarios

### Phase 3: Enforcement (v3.0 - Breaking)

1. **Required Attributes**: Compilation warnings/errors for EntityControllers without pagination attributes
2. **Remove Legacy**: Clean up backward compatibility code
3. **Enhanced Tooling**: IDE analyzers and code fixes

### Migration Examples

```csharp
// Before (v1.x) - unpredictable behavior
public class UsersController : EntityController<User>
{
    // Behavior depends on framework internals
}

// After (v2.0) - explicit and safe
[Pagination(Mode = Auto, DefaultSize = 25, MaxSize = 100)]
public class UsersController : EntityController<User>
{
    // Clear pagination behavior, safe defaults
}

// Special cases
[Pagination(Mode = Off)]  // Explicit "return all" for small datasets
public class StatusesController : EntityController<Status> { }

[Pagination(Mode = Required)]  // Force pagination for large datasets
public class EventsController : EntityController<Event> { }
```

## Testing Strategy

### Unit Tests

```csharp
[TestClass]
public class PaginationAttributeTests
{
    [TestMethod]
    public async Task AutoMode_WithoutParams_ReturnsDefaultPagination()
    {
        // Arrange
        var controller = new TestController();  // Uses Auto mode
        var context = CreateControllerContext(query: "");

        // Act
        var result = await controller.Get();

        // Assert
        var okResult = result.Should().BeOfType<OkObjectResult>().Subject;
        var items = okResult.Value.Should().BeAssignableTo<IEnumerable<TestEntity>>().Subject;
        items.Count().Should().Be(50);  // Default page size

        // Check headers
        controller.Response.Headers["X-Total-Count"].Should().Be("1000");
        controller.Response.Headers["X-Page"].Should().Be("1");
    }

    [TestMethod]
    public async Task RequiredMode_WithAllParam_StillPaginates()
    {
        // Arrange
        var controller = new TestRequiredController();
        var context = CreateControllerContext(query: "?all=true");

        // Act
        var result = await controller.Get(all: true);

        // Assert - should ignore 'all' parameter and still paginate
        controller.Response.Headers["X-Page"].Should().NotBeNull();
    }

    [TestMethod]
    public async Task OffMode_ReturnsAllRecords()
    {
        // Arrange
        var controller = new TestOffController();

        // Act
        var result = await controller.Get();

        // Assert
        var okResult = result.Should().BeOfType<OkObjectResult>().Subject;
        var items = okResult.Value.Should().BeAssignableTo<IEnumerable<TestEntity>>().Subject;
        items.Count().Should().Be(1000);  // All records

        // No pagination headers
        controller.Response.Headers.Should().NotContainKey("X-Page");
    }
}

[Pagination(Mode = PaginationMode.Required)]
public class TestRequiredController : EntityController<TestEntity> { }

[Pagination(Mode = PaginationMode.Off)]
public class TestOffController : EntityController<TestEntity> { }
```

### Integration Tests

```csharp
[TestClass]
public class PaginationIntegrationTests
{
    [TestMethod]
    public async Task GetProducts_DefaultPagination_ReturnsCorrectHeaders()
    {
        // Arrange
        var client = CreateTestClient();

        // Act
        var response = await client.GetAsync("/api/products");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        response.Headers.GetValues("X-Total-Count").First().Should().Be("10000");
        response.Headers.GetValues("X-Page").First().Should().Be("1");
        response.Headers.GetValues("X-Page-Size").First().Should().Be("50");
    }

    [TestMethod]
    public async Task GetCountries_OffMode_NoHeaders()
    {
        // Arrange
        var client = CreateTestClient();

        // Act
        var response = await client.GetAsync("/api/countries");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        response.Headers.Should().NotContain(h => h.Key.StartsWith("X-Page"));

        var countries = await response.Content.ReadAsAsync<Country[]>();
        countries.Length.Should().Be(195);  // All countries
    }
}
```

### Performance Tests

```csharp
[TestClass]
public class PaginationPerformanceTests
{
    [TestMethod]
    public async Task AutoMode_LargeDataset_PerformsWell()
    {
        // Test pagination performance with large datasets
        var stopwatch = Stopwatch.StartNew();

        var result = await controller.Get(page: 100, pageSize: 50);

        stopwatch.ElapsedMilliseconds.Should().BeLessThan(1000);
    }

    [TestMethod]
    public async Task StreamingMode_LargeDataset_MemoryEfficient()
    {
        // Test streaming doesn't load entire dataset into memory
        var initialMemory = GC.GetTotalMemory(false);

        await foreach (var item in streamingController.StreamItems())
        {
            // Process items without accumulating
            ProcessItem(item);
        }

        var finalMemory = GC.GetTotalMemory(true);
        var memoryIncrease = finalMemory - initialMemory;

        memoryIncrease.Should().BeLessThan(10 * 1024 * 1024); // Less than 10MB
    }
}
```

## Error Handling

### Validation Errors

```csharp
public class PaginationValidationFilter : ActionFilterAttribute
{
    public override void OnActionExecuting(ActionExecutingContext context)
    {
        var policy = GetPaginationPolicy(context);

        // Validate page parameter
        if (context.ActionArguments.TryGetValue("page", out var pageObj)
            && pageObj is int page && page < 1)
        {
            context.Result = new BadRequestObjectResult(new
            {
                error = "Invalid page number",
                message = "Page number must be greater than 0",
                parameter = "page",
                value = page
            });
            return;
        }

        // Validate pageSize parameter
        if (context.ActionArguments.TryGetValue("pageSize", out var sizeObj)
            && sizeObj is int pageSize)
        {
            if (pageSize < 1)
            {
                context.Result = new BadRequestObjectResult(new
                {
                    error = "Invalid page size",
                    message = "Page size must be greater than 0",
                    parameter = "pageSize",
                    value = pageSize
                });
                return;
            }

            if (pageSize > policy.MaxSize)
            {
                context.Result = new BadRequestObjectResult(new
                {
                    error = "Page size too large",
                    message = $"Page size cannot exceed {policy.MaxSize}",
                    parameter = "pageSize",
                    value = pageSize,
                    maxAllowed = policy.MaxSize
                });
                return;
            }
        }

        base.OnActionExecuting(context);
    }
}
```

### Streaming Errors

```csharp
public async IAsyncEnumerable<T> StreamWithErrorHandling<T>(
    IAsyncEnumerable<T> source,
    [EnumeratorCancellation] CancellationToken ct = default)
{
    var count = 0;
    try
    {
        await foreach (var item in source.WithCancellation(ct))
        {
            count++;
            yield return item;
        }
    }
    catch (OperationCanceledException)
    {
        // Log partial completion
        Logger.LogInformation("Streaming cancelled after {Count} items", count);
        throw;
    }
    catch (Exception ex)
    {
        // Log error with context
        Logger.LogError(ex, "Streaming failed after {Count} items", count);
        throw;
    }
}
```

## Security Considerations

### Rate Limiting

```csharp
[Pagination(Mode = PaginationMode.Optional)]
[RateLimit(MaxRequests = 10, WindowMinutes = 1)]  // Limit full scans
public class UsersController : EntityController<User>
{
    // Optional mode with rate limiting to prevent abuse
}
```

### Authorization

```csharp
public class UsersController : EntityController<User>
{
    [HttpGet]
    [Authorize]
    public override async Task<IActionResult> Get(...)
    {
        var policy = GetPaginationPolicy();

        // Adjust limits based on user role
        if (User.IsInRole("Admin"))
        {
            policy.MaxSize = 1000;  // Admins can request larger pages
        }
        else
        {
            policy.MaxSize = 100;   // Regular users have smaller limits
        }

        return await base.Get(...);
    }
}
```

## Performance Considerations

### Database Impact

1. **Count Queries**: `IncludeCount = false` for better performance when totals aren't needed
2. **Index Strategy**: Ensure proper indexes for common sort/filter combinations
3. **Query Optimization**: Framework should optimize pagination queries at adapter level

### Memory Management

1. **Streaming Mode**: Use for large datasets to avoid memory pressure
2. **Batch Processing**: Internal streaming uses configurable batch sizes
3. **Connection Pooling**: Ensure streaming doesn't exhaust connection pools

### Caching Strategy

```csharp
[Pagination(Mode = PaginationMode.Auto)]
[ResponseCache(Duration = 300, VaryByQueryKeys = new[] { "page", "pageSize", "filter" })]
public class ProductsController : EntityController<Product>
{
    // Cache paginated responses for better performance
}
```

## Future Enhancements

### Phase 4: Advanced Features

1. **Cursor Pagination**: For better performance on large datasets
2. **GraphQL Integration**: Support for GraphQL pagination patterns
3. **Real-time Updates**: WebSocket/SignalR integration for live data
4. **Export Formats**: Built-in CSV/Excel export with streaming

### Framework Evolution

1. **AI Integration**: Automatic pagination policy suggestions based on data patterns
2. **Performance Monitoring**: Built-in analytics for pagination usage
3. **A/B Testing**: Framework support for testing different pagination strategies

## Implementation Checklist

### Phase 1: Core (Required for MVP)
- [ ] `PaginationAttribute` definition
- [ ] Enhanced `DataQueryOptions`
- [ ] `QueryResult<T>` and `StreamResult<T>` types
- [ ] Updated `Data<T,K>` methods
- [ ] Basic mode support (Auto, Required, Optional, Off)

### Phase 2: EntityController (Essential)
- [ ] Base `EntityController<T>` updates
- [ ] Mode-specific handlers
- [ ] Header management
- [ ] Query parameter validation
- [ ] Error handling

### Phase 3: Framework Integration (Important)
- [ ] OpenAPI integration
- [ ] Validation filters
- [ ] Default policy configuration
- [ ] Migration documentation

### Phase 4: Advanced (Nice to Have)
- [ ] Streaming mode implementation
- [ ] Performance optimizations
- [ ] Security enhancements
- [ ] Monitoring and analytics

## Conclusion

This proposal provides a comprehensive, type-safe solution to pagination that:

1. **Eliminates Current Problems**: No more accidental full scans or inconsistent behavior
2. **Provides Excellent DX**: Clear, discoverable, compile-time verified behavior
3. **Supports All Scenarios**: From small reference data to massive streaming datasets
4. **Enables Safe Defaults**: Framework guides developers toward secure patterns
5. **Future-Proof Design**: Extensible for advanced features like cursor pagination

The attribute-based approach ensures that pagination behavior is explicit, intentional, and maintainable while providing the flexibility needed for diverse API scenarios.
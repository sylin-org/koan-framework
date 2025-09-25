# Pagination Attribute System Proposal

**Status**: Draft
**Author**: Koan Framework Team
**Date**: 2025-01-25
**Target Version**: Koan Framework v2.0

## Executive Summary

This revision defaults every `EntityController<>` to `PaginationMode.On` (page size 50, max 200, totals enabled) while allowing explicit opt-in to other modes via attributes. It also removes the previously proposed streaming mode in favor of focusing on reliable page/window semantics supported by repository adapters.

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
    /// Framework default: Always paginate with configurable limits that callers can tune within safe bounds.
    /// </summary>
    On = 0,

    /// <summary>
    /// Always paginate and ignore requests to disable pagination.
    /// Suitable for large datasets where full scans are dangerous.
    /// </summary>
    Required = 1,

    /// <summary>
    /// Paginate only when the caller explicitly requests it via query parameters.
    /// Without pagination params, returns the full dataset.
    /// </summary>
    Optional = 2,

    /// <summary>
    /// Never paginate - always return the full dataset.
    /// Dangerous for large datasets, use only for small reference data.
    /// </summary>
    Off = 3
}

[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method, Inherited = true)]
public class PaginationAttribute : Attribute
{
    /// <summary>Pagination behavior mode</summary>
    public PaginationMode Mode { get; set; } = PaginationMode.On;

    /// <summary>Default page size when pagination is active</summary>
    public int DefaultSize { get; set; } = 50;

    /// <summary>Maximum allowed page size</summary>
    public int MaxSize { get; set; } = 200;

    /// <summary>Include total count in paginated responses (affects performance)</summary>
    public bool IncludeCount { get; set; } = true;

    /// <summary>Default sort order when none specified</summary>
    public string? DefaultSort { get; set; }
}
```

> **Important:** Attribute instances are cached and shared by the runtime. The framework MUST treat them as immutable configuration
> hints and project them into per-request policy snapshots before applying any business rules.

### Runtime Policy Snapshot

```csharp
public sealed record PaginationPolicy
{
    public required PaginationMode Mode { get; init; }
    public required int DefaultSize { get; init; }
    public required int MaxSize { get; init; }
    public required bool IncludeCount { get; init; }
    public required int AbsoluteMaxRecords { get; init; }
    public string? DefaultSort { get; init; }

    public static PaginationPolicy FromAttribute(PaginationAttribute attr, PaginationSafetyBounds safety)
    {
        // Defensive copy to avoid mutating attribute instances that are reused across requests.
        var defaultSize = Math.Clamp(attr.DefaultSize, safety.MinPageSize, safety.MaxPageSize);
        var maxSize = Math.Clamp(attr.MaxSize, safety.MinPageSize, safety.MaxPageSize);

        if (maxSize < defaultSize)
        {
            maxSize = defaultSize;
        }

        return new PaginationPolicy
        {
            Mode = attr.Mode,
            DefaultSize = defaultSize,
            MaxSize = maxSize,
            IncludeCount = attr.IncludeCount,
            AbsoluteMaxRecords = safety.AbsoluteMaxRecords,
            DefaultSort = attr.DefaultSort
        };
    }
}

public sealed record PaginationSafetyBounds
{
    public required int MinPageSize { get; init; }
    public required int MaxPageSize { get; init; }
    public required int AbsoluteMaxRecords { get; init; }
}
```

## Detailed Behavior Specification

### Mode Behavior Matrix

| Mode | No Query Params | ?page=2&pageSize=100 | ?all=true | Response Headers |
|------|-----------------|---------------------|-----------|------------------|
| **On** | Paginate (page=1, size=50) | Honor params (≤MaxSize) | Ignore, paginate | X-Total-Count, X-Page, X-PageSize |
| **Required** | Paginate (page=1, size=50) | Honor params (≤MaxSize) | Ignore, paginate | X-Total-Count, X-Page, X-PageSize |
| **Optional** | Return all records (subject to global cap) | Apply pagination | Return all (subject to global cap) | X-Total-Count only if paginated |
| **Off** | Return all records (subject to global cap) | Ignore params | Return all (subject to global cap) | No pagination headers |

> **Global cap**: Even when pagination is bypassed (Optional/Off), controllers must enforce `PaginationSafetyBounds.AbsoluteMaxRecords`
> to prevent unbounded result sets. Responses that would exceed the cap should return `413 Payload Too Large` (or a configurable
> error) instructing clients to use pagination instead.

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
public sealed class QueryResult<T>
{
    public required IReadOnlyList<T> Items { get; init; }
    public required int TotalCount { get; init; }
    public required int Page { get; init; }
    public required int PageSize { get; init; }
    public int TotalPages => PageSize == 0 ? 0 : (int)Math.Ceiling((double)TotalCount / PageSize);
    public bool HasNextPage => Page < TotalPages;
    public bool HasPreviousPage => Page > 1;
}
```

#### 1.3 Data Layer Updates

```csharp
public interface IPagedRepository<TEntity>
{
    Task<PagedRepositoryResult<TEntity>> QueryPageAsync(
        object? query,
        int page,
        int pageSize,
        CancellationToken ct = default);
}

public sealed class PagedRepositoryResult<TEntity>
{
    public required IReadOnlyList<TEntity> Items { get; init; }
    public required int TotalCount { get; init; }
    public required int Page { get; init; }
    public required int PageSize { get; init; }
}

// Enhanced Data<T,K> methods
public static class Data<TEntity, TKey>
    where TEntity : class, IEntity<TKey>
    where TKey : notnull
{
    public static Task<IReadOnlyList<TEntity>> All(CancellationToken ct = default)
        => Repo.QueryAsync(null, ct);

    public static async Task<QueryResult<TEntity>> QueryWithCount(
        object? query,
        DataQueryOptions? options,
        CancellationToken ct = default,
        int? absoluteMaxRecords = null)
    {
        var page = options?.EffectivePage(1) ?? 1;
        var pageSize = options?.EffectivePageSize(50) ?? int.MaxValue;

        if (options?.HasPagination == true && Repo is IPagedRepository<TEntity> pagedRepo)
        {
            var repoResult = await pagedRepo.QueryPageAsync(query, page, pageSize, ct);
            return new QueryResult<TEntity>
            {
                Items = repoResult.Items,
                TotalCount = repoResult.TotalCount,
                Page = repoResult.Page,
                PageSize = repoResult.PageSize
            };
        }

        if (options?.HasPagination == true)
        {
            // Adapter lacks native paging; emulate with double query.
            var items = await Repo.QueryAsync(query, options, ct);
            var totalCount = await Repo.CountAsync(query, ct);

            return new QueryResult<TEntity>
            {
                Items = items,
                TotalCount = totalCount,
                Page = page,
                PageSize = pageSize
            };
        }

        if (absoluteMaxRecords.HasValue)
        {
            var totalCount = await Repo.CountAsync(query, ct);
            if (totalCount > absoluteMaxRecords.Value)
            {
                return new QueryResult<TEntity>
                {
                    Items = Array.Empty<TEntity>(),
                    TotalCount = totalCount,
                    Page = 1,
                    PageSize = 0
                };
            }

            var items = await Repo.QueryAsync(query, options, ct);
            return new QueryResult<TEntity>
            {
                Items = items,
                TotalCount = totalCount,
                Page = 1,
                PageSize = items.Count
            };
        }

        // Legacy fallback for non-paginated requests without explicit safety bounds
        var legacyItems = await Repo.QueryAsync(query, options, ct);
        return new QueryResult<TEntity>
        {
            Items = legacyItems,
            TotalCount = legacyItems.Count,
            Page = 1,
            PageSize = legacyItems.Count
        };
    }
}
```

> When `absoluteMaxRecords` is provided, `QueryWithCount` will short-circuit and return an empty item set if the source contains
> more records than allowed. Controllers can use the reported `TotalCount` to emit a `413` response without materializing the fu
> ll dataset.

#### 1.4 Global Safety Bounds

```csharp
public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddPaginationSafetyBounds(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services.Configure<PaginationSafetyBounds>(configuration.GetSection("Pagination"));

        // Sensible defaults if config is missing.
        services.PostConfigure<PaginationSafetyBounds>(bounds =>
        {
            bounds.MinPageSize = Math.Max(bounds.MinPageSize, 1);
            bounds.MaxPageSize = Math.Clamp(bounds.MaxPageSize, bounds.MinPageSize, 1_000);
            bounds.AbsoluteMaxRecords = Math.Max(bounds.AbsoluteMaxRecords, bounds.MaxPageSize);
        });

        return services;
    }
}
```

> **Repository requirement**: Adapters should implement `IPagedRepository<TEntity>` so `QueryWithCount` can fetch the page window and total count in a single round trip. When the interface is not implemented the framework falls back to `Repo.QueryAsync` + `Repo.CountAsync`, which is functional but slower for large datasets.

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
    private static readonly PaginationAttribute FrameworkDefault = new()
    {
        Mode = PaginationMode.On,
        DefaultSize = 50,
        MaxSize = 200,
        IncludeCount = true
    };

    protected virtual PaginationPolicy GetPaginationPolicy()
    {
        var safety = HttpContext.RequestServices
            .GetService<IOptions<PaginationSafetyBounds>>()?.Value
            ?? new PaginationSafetyBounds
            {
                MinPageSize = 1,
                MaxPageSize = 200,
                AbsoluteMaxRecords = 10_000
            };

        PaginationAttribute? ResolveAttribute()
        {
            var methodInfo = ControllerContext.ActionDescriptor.MethodInfo;
            var methodAttr = methodInfo.GetCustomAttribute<PaginationAttribute>();
            if (methodAttr != null) return methodAttr;

            var controllerAttr = GetType().GetCustomAttribute<PaginationAttribute>();
            if (controllerAttr != null) return controllerAttr;

            return FrameworkDefault;
        }

        return PaginationPolicy.FromAttribute(ResolveAttribute()!, safety);
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
            PaginationMode.On => await HandleOnMode(options, policy, ct),
            PaginationMode.Required => await HandleRequiredMode(options, policy, ct),
            PaginationMode.Optional => await HandleOptionalMode(options, policy, all, ct),
            PaginationMode.Off => await HandleOffMode(options, policy, ct),
            _ => throw new InvalidOperationException($"Unknown pagination mode: {policy.Mode}")
        };
    }

    private async Task<IActionResult> HandleOnMode(
        DataQueryOptions options,
        PaginationPolicy policy,
        CancellationToken ct)
    {
        // Always paginate, but allow user to customize within limits
        var page = options.EffectivePage(1);
        var pageSize = Math.Min(options.EffectivePageSize(policy.DefaultSize), policy.MaxSize);

        var paginatedOptions = options.WithPagination(page, pageSize);
        var result = await Data<T, TKey>.QueryWithCount(null, paginatedOptions, ct, policy.AbsoluteMaxRecords);

        AddPaginationHeaders(result, policy);
        return Ok(result.Items);
    }

    private async Task<IActionResult> HandleRequiredMode(
        DataQueryOptions options,
        PaginationPolicy policy,
        CancellationToken ct)
    {
        // Force pagination regardless of query params
        var page = Math.Max(options.EffectivePage(1), 1);
        var pageSize = Math.Min(options.EffectivePageSize(policy.DefaultSize), policy.MaxSize);

        var paginatedOptions = options.WithPagination(page, pageSize);
        var result = await Data<T, TKey>.QueryWithCount(null, paginatedOptions, ct, policy.AbsoluteMaxRecords);

        AddPaginationHeaders(result, policy);
        return Ok(result.Items);
    }

    private async Task<IActionResult> HandleOptionalMode(
        DataQueryOptions options,
        PaginationPolicy policy,
        bool? all,
        CancellationToken ct)
    {
        if (options.HasPagination && all != true)
        {
            // User requested pagination
            var page = options.EffectivePage(1);
            var pageSize = Math.Min(options.EffectivePageSize(policy.DefaultSize), policy.MaxSize);

            var paginatedOptions = options.WithPagination(page, pageSize);
            var result = await Data<T, TKey>.QueryWithCount(null, paginatedOptions, ct, policy.AbsoluteMaxRecords);

            AddPaginationHeaders(result, policy);
            return Ok(result.Items);
        }
        else
        {
            // Return all records
            var capped = await Data<T, TKey>.QueryWithCount(null, null, ct, policy.AbsoluteMaxRecords);

            if (capped.TotalCount > policy.AbsoluteMaxRecords)
            {
                return StatusCode(StatusCodes.Status413PayloadTooLarge, new
                {
                    error = "Result too large",
                    message = $"This endpoint allows at most {policy.AbsoluteMaxRecords} records without pagination."
                });
            }

            if (policy.IncludeCount)
            {
                Response.Headers.Add("X-Total-Count", capped.TotalCount.ToString());
            }

            return Ok(capped.Items);
        }
    }

    private async Task<IActionResult> HandleOffMode(DataQueryOptions options, PaginationPolicy policy, CancellationToken ct)
    {
        // Never paginate - always return all
        var result = await Data<T, TKey>.QueryWithCount(null, null, ct, policy.AbsoluteMaxRecords);

        if (result.TotalCount > policy.AbsoluteMaxRecords)
        {
            return StatusCode(StatusCodes.Status413PayloadTooLarge, new
            {
                error = "Result too large",
                message = $"This endpoint must remain under the {policy.AbsoluteMaxRecords} record cap or enable pagination."
            });
        }

        return Ok(result.Items);
    }

    private void AddPaginationHeaders(QueryResult<T> result, PaginationPolicy policy)
    {
        Response.Headers.Add("X-Page", result.Page.ToString());
        Response.Headers.Add("X-Page-Size", result.PageSize.ToString());

        if (policy.IncludeCount)
        {
            Response.Headers.Add("X-Total-Count", result.TotalCount.ToString());
            Response.Headers.Add("X-Total-Pages", result.TotalPages.ToString());
            Response.Headers.Add("X-Has-Next-Page", result.HasNextPage.ToString().ToLower());
            Response.Headers.Add("X-Has-Previous-Page", result.HasPreviousPage.ToString().ToLower());
        }
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
            case PaginationMode.On:
            case PaginationMode.Required:
                AddPaginationParameters(operation, paginationAttr);
                AddPaginationResponses(operation, paginationAttr);
                break;

            case PaginationMode.Optional:
                AddOptionalPaginationParameters(operation, paginationAttr);
                AddPaginationResponses(operation, paginationAttr);
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

## Feasibility & Risk Assessment

| Area | Risk | Mitigation |
|------|------|------------|
| Repository adapters | `QueryWithCount` requires the new `IPagedRepository` path to avoid double-querying large datasets. Some adapters may lack efficient paging. | Provide a shared helper that emulates paging via SQL `OFFSET/FETCH` for relational providers, document the fallback cost, and gate GA on adapters implementing the optimized path. |
| Attribute handling | ASP.NET caches attribute instances; mutating them at runtime would cause cross-request bleeding. | Introduce `PaginationPolicy` snapshots constructed per request (see "Runtime Policy Snapshot"), keeping attributes read-only. |
| Optional/Off defaults | Returning unbounded datasets can exhaust memory and bandwidth. | Enforce `PaginationSafetyBounds.AbsoluteMaxRecords` and surface explicit 413 errors when callers bypass pagination. Provide environment-wide defaults via `IOptions<PaginationSafetyBounds>`. |
| Complexity creep | Five modes + multiple knobs can confuse teams. | Deliver curated presets in documentation, add analyzers that warn on risky combinations, and keep Optional/Off opt-in with guardrails. |
| Delivery scope | Proposal spans controllers, data layer, OpenAPI, tooling, and tests. Risk of overcommitting for a single release. | Stage work per phases, prioritize core runtime + repo support for v2.0, and mark OpenAPI/tooling as stretch if schedule slips. |

> **Honest take:** The design is desirable and achievable, but only if we invest early in repository adapter support and keep Optional/Off firmly guarded. Without those pieces, we risk reintroducing unsafe defaults under a new banner.

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
    // GET /api/users returns all users up to the global cap (explicit opt-out)
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

```

## Migration Strategy

### Phase 1: Backward Compatibility (v2.0)

1. **Existing Controllers**: Continue working without changes (On mode default)
2. **New Attribute**: Add `[Pagination]` attribute support alongside existing behavior
3. **Opt-In Enhancement**: Teams can gradually add attributes to controllers

### Phase 2: Framework Default (v2.1)

1. **On Mode Default**: All EntityController instances use On mode unless explicitly configured
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
[Pagination(Mode = On, DefaultSize = 25, MaxSize = 100)]
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
    public async Task OnMode_WithoutParams_ReturnsDefaultPagination()
    {
        // Arrange
        var controller = new TestController();  // Uses On mode
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
    public async Task OnMode_LargeDataset_PerformsWell()
    {
        var stopwatch = Stopwatch.StartNew();

        await controller.Get(page: 100, pageSize: 50);

        stopwatch.ElapsedMilliseconds.Should().BeLessThan(1000);
    }

    [TestMethod]
    public void OnMode_PageWindow_DoesNotLeakMemory()
    {
        var initialMemory = GC.GetTotalMemory(true);

        SimulatePageFetch(page: 42, pageSize: 50);

        var finalMemory = GC.GetTotalMemory(true);
        var memoryIncrease = finalMemory - initialMemory;

        memoryIncrease.Should().BeLessThan(10 * 1024 * 1024); // Less than 10MB
    }

    private void SimulatePageFetch(int page, int pageSize)
    {
        // Use a fake repository to materialize a representative window
        using var scope = _factory.Services.CreateScope();
        var repo = scope.ServiceProvider.GetRequiredService<ITestRepository>();
        repo.FetchPage(page, pageSize);
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
[Authorize]
public class UsersController : EntityController<User>
{
    protected override PaginationAttribute GetPaginationPolicy()
    {
        var basePolicy = base.GetPaginationPolicy();

        if (User?.IsInRole("Admin") == true)
        {
            return new PaginationAttribute
            {
                Mode = basePolicy.Mode,
                DefaultSize = basePolicy.DefaultSize,
                MaxSize = 1000,
                IncludeCount = basePolicy.IncludeCount,
                DefaultSort = basePolicy.DefaultSort
            };
        }

        return new PaginationAttribute
        {
            Mode = basePolicy.Mode,
            DefaultSize = basePolicy.DefaultSize,
            MaxSize = 100,
            IncludeCount = basePolicy.IncludeCount,
            DefaultSort = basePolicy.DefaultSort
        };
    }
}
```

## Performance Considerations

### Database Impact

1. **Count Queries**: `IncludeCount = false` for better performance when totals aren't needed
2. **Index Strategy**: Ensure proper indexes for common sort/filter combinations
3. **Query Optimization**: Framework should optimize pagination queries at adapter level

### Memory Management

1. **Windowed Fetches**: Keep page sizes bounded (default MaxSize = 200) to limit materialized rows.
2. **Batch Processing**: Repositories that cannot paginate natively should still chunk large results to avoid spikes.
3. **Connection Pooling**: Ensure long-running count queries or large result sets do not exhaust pooled connections.

### Caching Strategy

```csharp
[Pagination(Mode = PaginationMode.On)]
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
4. **Export Formats**: Async job-based CSV/Excel export helpers that respect pagination limits

### Framework Evolution

1. **AI Integration**: Automatic pagination policy suggestions based on data patterns
2. **Performance Monitoring**: Built-in analytics for pagination usage
3. **A/B Testing**: Framework support for testing different pagination strategies

## Implementation Checklist

### Phase 1: Core (Required for MVP)
- [ ] `PaginationAttribute` definition
- [ ] Enhanced `DataQueryOptions`
- [ ] `QueryResult<T>` type and repository paging contracts
- [ ] Updated `Data<T,K>` methods
- [ ] Basic mode support (On, Required, Optional, Off)

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
- [ ] Performance optimizations
- [ ] Security enhancements
- [ ] Monitoring and analytics

## Conclusion

This proposal provides a comprehensive, type-safe solution to pagination that:

1. **Eliminates Current Problems**: No more accidental full scans or inconsistent behavior
2. **Provides Excellent DX**: Clear, discoverable, compile-time verified behavior
3. **Supports Core Scenarios**: From small reference data to high-volume lists that require strict pagination
4. **Enables Safe Defaults**: Framework guides developers toward secure patterns
5. **Future-Proof Design**: Extensible for advanced features like cursor pagination

The attribute-based approach ensures that pagination behavior is explicit, intentional, and maintainable while providing the flexibility needed for diverse API scenarios.
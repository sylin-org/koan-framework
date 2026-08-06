---
type: GUIDE
domain: performance
title: "Performance Optimization with Koan"
audience: [developers, architects, ai-agents]
status: current
last_updated: 2026-07-15
framework_version: v0.20.0
validation:
  status: not-yet-tested
  scope: docs/guides/performance.md
related_guides:
  - entity-capabilities-howto.md
  - ../reference/data/index.md
  - ai-vector-howto.md
  - ../reference/web/index.md
---

# Performance Optimization with Koan

**Document Type**: GUIDE
**Target Audience**: Developers, Architects
**Last Updated**: 2026-07-15
**Preview line**: 0.20

---

## Data Access Patterns

### Choose the Right API

```csharp
// ✅ Small datasets - materialize everything
var todos = await Todo.All();
var active = await Todo.Query(t => !t.IsCompleted);

// ✅ Large datasets - stream in batches
await foreach (var todo in Todo.AllStream(batchSize: 1000))
{
    await ProcessTodo(todo);
}

// ✅ UI pagination - explicit pages
var page = await Todo.FirstPage(size: 50);
var nextPage = await Todo.Page(page: 2, size: 50);

// ❌ Memory issues - don't materialize large sets
var allUsers = await User.All(); // If you have millions of users
```

### Streaming for Large Datasets

Entity streams are provider-bounded only when the selected adapter advertises and realizes
`ProviderBoundedPaging` (SQLite, PostgreSQL, SQL Server, CockroachDB, MongoDB, Couchbase). InMemory,
JSON, and Redis reject before query/yield. `batchSize` bounds Koan's candidate page, not opaque driver
buffers, snapshots, or mutation-safe traversal. See [Entity access and streaming](data/entity-access-and-streaming.md).

```csharp
using Koan.Data.Core;

// Keep the provider page and the application-owned work batch explicit
await foreach (var batch in Product.AllStream(batchSize: 500).Batch(500))
{
    await ProcessBulkProducts(batch);
}

// Stream with filtering
await foreach (var order in Order.QueryStream(
    o => o.Created > DateTime.UtcNow.AddDays(-30),
    batchSize: 1000))
{
    await ProcessOrder(order);
}

// Parallel processing with an explicit concurrency bound
await Parallel.ForEachAsync(
    Todo.AllStream(batchSize: 100),
    new ParallelOptions { MaxDegreeOfParallelism = Environment.ProcessorCount },
    async (todo, _) => await ProcessTodo(todo));
```

### Query Pushdown Optimization

```csharp
// ✅ Pushed to database - fast
var products = await Product.FirstPage(
    size: 50,                                          // Pushed down (limit)
    sort: s => s.OrderBy(p => p.Price));               // Pushed down (sort)
// Filter pushdown via the predicate overload:
var electronics = await Product.Query(
    p => p.Category == "Electronics",                  // Pushed down (filter)
    sort: s => s.OrderBy(p => p.Price));               // Pushed down (sort)

// ❌ In-memory processing - slow
var products = await Product.All();           // Load everything
var filtered = products
    .Where(p => p.Category == "Electronics")  // In-memory
    .OrderBy(p => p.Price)                    // In-memory
    .Take(50);                                // In-memory
```

### Capability Detection

```csharp
// Check provider capabilities
var capabilities = Data<Product, string>.Capabilities;

if (capabilities.Has(DataCaps.Query.Linq))
{
    // Complex query pushed to database (filter via predicate; aggregate in-memory)
    var featured = await Product.Query(p => p.Tags.Contains("featured"));
    var results = featured.GroupBy(p => p.Category);
}
else
{
    // Simplified query or in-memory fallback
    var products = await Product.Query(p => p.Category == targetCategory);
}
```

## Entity ID Optimization

### Automatic GUID v7 Generation

```csharp
// ✅ Optimized - automatic GUID v7, stored as binary
public class Order : Entity<Order>
{
    public string UserId { get; set; } = "";
    public decimal Total { get; set; }
    // Id: automatically generated GUID v7; physical storage is connector-specific
}

// ✅ Explicit strings - when you need human-readable IDs
public class Category : Entity<Category, string>
{
    public string Name { get; set; } = "";
    // Id: must be set explicitly, stored as string
}
```

### Storage Efficiency

Native GUID columns can be narrower than textual GUID representations, but the actual representation,
index width, and benefit depend on the connector, schema, and database configuration. Inspect the
generated schema and measure the workload you deploy.

### Performance Impact

Koan does not publish a current, controlled cross-provider benchmark for identifier mapping. Treat
smaller keys as a potential storage and locality benefit, not a throughput promise. Benchmark the
selected connector with your schema, indexes, data distribution, and concurrency.

## Memory Management

### Efficient Batch Processing

```csharp
using Koan.Data.Core;

// Bound the application-owned work batch and concurrent processing.
// The caller supplies the stream because static Entity APIs cannot be called on generic T.
public async Task ProcessLargeDataset<T>(
    IAsyncEnumerable<T> source,
    CancellationToken ct = default)
{
    await foreach (var batch in source.Batch(1000, ct))
    {
        await Parallel.ForEachAsync(
            batch,
            new ParallelOptions
            {
                MaxDegreeOfParallelism = Environment.ProcessorCount,
                CancellationToken = ct
            },
            async (item, _) => await ProcessItem(item));
    }
}

// Avoid materializing the complete Entity source in Koan on a qualified provider
public async Task<decimal> CalculateTotalRevenue()
{
    decimal total = 0;
    await foreach (var order in Order.AllStream(batchSize: 1000))
    {
        total += order.Total;
    }
    return total;
}
```

### Connection Pooling

```csharp
// Framework automatically pools connections
// Monitor connection usage in high-throughput scenarios
public class OrderService
{
    // ✅ Each operation uses pooled connections
    public async Task<Order> CreateOrder(CreateOrderRequest request)
    {
        var order = new Order
        {
            UserId = request.UserId,
            Total = request.Total
        };

        await order.Save(); // Uses connection pool
        return order;
    }
}
```

## Async/Await Optimization

### Proper Async Patterns

```csharp
// ✅ Async enumeration for large datasets
public static async IAsyncEnumerable<Order> GetOrdersAsync(
    [EnumeratorCancellation] CancellationToken ct = default)
{
    await foreach (var order in Order.AllStream(batchSize: 1000, ct: ct))
    {
        yield return order;
    }
}

// ✅ Concurrent processing with throttling
public static async Task<TResult[]> ProcessConcurrently<TSource, TResult>(
    IEnumerable<TSource> source,
    Func<TSource, Task<TResult>> processor,
    int maxConcurrency = 10)
{
    using var semaphore = new SemaphoreSlim(maxConcurrency);

    var tasks = source.Select(async item =>
    {
        await semaphore.WaitAsync();
        try
        {
            return await processor(item);
        }
        finally
        {
            semaphore.Release();
        }
    });

    return await Task.WhenAll(tasks);
}
```

### Cancellation Token Usage

```csharp
// ✅ Always pass cancellation tokens
public async Task ProcessOrders(CancellationToken ct = default)
{
    await foreach (var order in Order.AllStream(ct: ct))
    {
        ct.ThrowIfCancellationRequested();
        await ProcessOrder(order, ct);
    }
}

// ✅ Respect timeouts in long operations
public async Task<Product[]> SearchProducts(string query, CancellationToken ct = default)
{
    using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
    cts.CancelAfter(TimeSpan.FromSeconds(30)); // 30 second timeout

    return (await Product.Query(
        p => p.Name.Contains(query),
        cts.Token)).ToArray();
}
```

## Caching Strategies

### Entity-Level Caching

```csharp
// ✅ Cache frequently accessed entities
public class ProductService
{
    private readonly IMemoryCache _cache;

    public async Task<Product?> GetProductAsync(string id)
    {
        var cacheKey = $"product:{id}";

        if (_cache.TryGetValue(cacheKey, out Product? cached))
            return cached;

    var product = await Product.Get(id);
        if (product != null)
        {
            _cache.Set(cacheKey, product, TimeSpan.FromMinutes(5));
        }

        return product;
    }
}
```

### Query Result Caching

```csharp
// ✅ Cache expensive queries
public class CategoryService
{
    private readonly IMemoryCache _cache;

    public async Task<Product[]> GetFeaturedProducts(string category)
    {
        var cacheKey = $"featured:{category}";

        if (_cache.TryGetValue(cacheKey, out Product[]? cached))
            return cached;

        var products = (await Product.Query(
            p => p.Category == category && p.IsFeatured,
            sort: s => s.OrderByDescending(p => p.Rating))).Take(20).ToArray();

        _cache.Set(cacheKey, products, TimeSpan.FromMinutes(10));
        return products;
    }
}
```

## Multi-Provider Performance

### Provider-Specific Optimizations

```csharp
// ✅ Let framework choose optimal provider based on query
public class ReportService
{
    public async Task<AnalyticsReport> GenerateReport()
    {
        // Simple lookups - any provider
        var users = await User.All();

        // Complex aggregations - SQL providers preferred
        var recentOrders = await Order.Query(o => o.Created > DateTime.UtcNow.AddDays(-30));
        var revenue = recentOrders
            .GroupBy(o => o.Created.Date)
            .Select(g => new { Date = g.Key, Total = g.Sum(o => o.Total) });

        // Vector search - vector providers only
        var similar = await Product.SimilarTo("smartphone");

        return new AnalyticsReport(users.Length, revenue, similar);
    }
}
```

### Fallback Strategies

```csharp
// ✅ Graceful degradation when provider lacks capability
public async Task<Product[]> SearchProducts(string query)
{
    var capabilities = Data<Product, string>.Capabilities;

    if (capabilities.Has(DataCaps.Query.String))
    {
        // Use provider's full-text search
        return await Product.Query($"SEARCH '{query}'");
    }
    else
    {
        // Fallback to simple string matching
        return await Product.Query(p => p.Name.Contains(query));
    }
}
```

## Background Services Performance

### Efficient Entity transport

```csharp
using Koan.Communication;

await Order.QueryStream(order => order.Ready)
    .Transport.Send(ct);
```

The stream stays provider-bounded and Transport applies the same pointwise semantics as a scalar
send. Receiver discovery, carriage bounds, and settlement remain Communication concerns; selection
and batching stay in Data.

## Performance Monitoring

### Measure Application Work and Inspect Capabilities

```csharp
using System.Diagnostics;
using Koan.Data.Core;

var started = Stopwatch.GetTimestamp();
var products = await Product.FirstPage(size: 100, ct: ct);
var elapsed = Stopwatch.GetElapsedTime(started);

logger.LogInformation(
    "Loaded {Count} products in {Elapsed}; data capabilities: {Capabilities}",
    products.Count,
    elapsed,
    Data<Product, string>.Capabilities);
```

Koan exposes the selected data provider's capability set. Latency, throughput, alert thresholds, and
health endpoints are application and deployment concerns; measure them with your normal .NET
observability stack.

### Performance Alerts

```csharp
// ✅ Set up performance monitoring
public class PerformanceMiddleware
{
    public async Task InvokeAsync(HttpContext context, RequestDelegate next)
    {
        var stopwatch = Stopwatch.StartNew();

        await next(context);

        stopwatch.Stop();

        if (stopwatch.ElapsedMilliseconds > 5000) // 5 second threshold
        {
            _logger.LogWarning("Slow request detected: {Method} {Path} took {Duration}ms",
                context.Request.Method,
                context.Request.Path,
                stopwatch.ElapsedMilliseconds);
        }
    }
}
```

## Common Anti-Patterns

### ❌ Loading Everything

```csharp
// Wrong: Loading all data when you only need some
var allUsers = await User.All();
var activeUsers = allUsers.Where(u => u.IsActive).ToArray();

// Right: Filter at database level
var activeUsers = await User.Query(u => u.IsActive);
```

### ❌ N+1 Query Problems

```csharp
// Wrong: N+1 queries
var orders = await Order.All();
foreach (var order in orders)
{
    var user = await User.Get(order.UserId); // N queries
}

// Right: Batch loading or include patterns
var orders = await Order.All();
var userIds = orders.Select(o => o.UserId).Distinct();
var users = await User.Query(u => userIds.Contains(u.Id));
var userLookup = users.ToDictionary(u => u.Id);
```

### ❌ Blocking Async

```csharp
// Wrong: Blocking async operations
var result = SomeAsyncMethod().Result; // Blocks thread

// Right: Await properly
var result = await SomeAsyncMethod();
```

### ❌ Ignoring Cancellation

```csharp
// Wrong: Not handling cancellation
public async Task ProcessData()
{
    await foreach (var item in Order.AllStream())
    {
        // No cancellation check
        await ProcessItem(item);
    }
}

// Right: Respect cancellation
public async Task ProcessData(CancellationToken ct = default)
{
    await foreach (var item in Order.AllStream(ct: ct))
    {
        ct.ThrowIfCancellationRequested();
        await ProcessItem(item, ct);
    }
}
```

## Performance Checklist

### Data Access
- [ ] Use streaming APIs for large datasets
- [ ] Choose appropriate materialization strategy
- [ ] Check provider capabilities for complex queries
- [ ] Implement proper pagination for UI

### Entity Design
- [ ] Use `Entity<T>` for automatic GUID optimization
- [ ] Use `Entity<T, string>` only when you need human-readable IDs
- [ ] Implement efficient business logic methods
- [ ] Cache frequently accessed data

### Async Patterns
- [ ] Use `ConfigureAwait(false)` in library code
- [ ] Implement proper cancellation token usage
- [ ] Avoid blocking async operations
- [ ] Use parallel processing for independent operations

### Memory Management
- [ ] Stream large datasets instead of materializing
- [ ] Dispose resources properly
- [ ] Use connection pooling
- [ ] Monitor memory usage in production

### Monitoring
- [ ] Set up performance alerts
- [ ] Monitor query execution times
- [ ] Track provider capability usage
- [ ] Monitor entity optimization status

---

**Validation status**: Not yet tested end-to-end

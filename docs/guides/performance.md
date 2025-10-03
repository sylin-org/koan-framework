---
type: GUIDE
domain: performance
title: "Performance Optimization with Koan"
audience: [developers, architects, ai-agents]
last_updated: 2025-01-17
framework_version: "v0.2.18+"
status: current
validation: 2025-01-17
---

# Performance Optimization with Koan

**Document Type**: GUIDE
**Target Audience**: Developers, Architects
**Last Updated**: 2025-01-17
**Framework Version**: v0.2.18+

---

## Data Access Patterns

### Choose the Right API

```csharp
// ✅ Small datasets - materialize everything
var todos = await Todo.All();
var active = await Todo.Where(t => !t.IsCompleted);

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

```csharp
// Process large datasets efficiently
await foreach (var batch in Product.AllStream(batchSize: 500).ToBatches(500))
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

// Parallel processing
await Todo.AllStream(batchSize: 100)
    .ForEachAsync(async todo => await ProcessTodo(todo),
                  degreeOfParallelism: Environment.ProcessorCount);
```

### Query Pushdown Optimization

```csharp
// ✅ Pushed to database - fast
var products = await Product.Query()
    .Where(p => p.Category == "Electronics")  // Pushed down
    .OrderBy(p => p.Price)                    // Pushed down
    .Take(50);                                // Pushed down

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
var capabilities = Data<Product, string>.QueryCaps;

if (capabilities.Capabilities.HasFlag(QueryCapabilities.LinqQueries))
{
    // Complex query pushed to database
    var results = await Product.Query()
        .Where(p => p.Tags.Contains("featured"))
        .GroupBy(p => p.Category);
}
else
{
    // Simplified query or in-memory fallback
    var products = await Product.Where(p => p.Category == targetCategory);
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
    // Id: automatically generated GUID v7, 56% storage reduction
}

// ✅ Explicit strings - when you need human-readable IDs
public class Category : Entity<Category, string>
{
    public string Name { get; set; } = "";
    // Id: must be set explicitly, stored as string
}
```

### Storage Efficiency

**Database storage comparison:**
- PostgreSQL: TEXT (36+ bytes) → UUID (16 bytes) = **56% reduction**
- SQL Server: NVARCHAR(256) (512+ bytes) → UNIQUEIDENTIFIER (16 bytes) = **97% reduction**
- MongoDB: String (36+ bytes) → BinData UUID (16 bytes) = **56% reduction**

### Performance Impact

```csharp
// Query performance improvements from GUID optimization:
// - PostgreSQL: 290% faster single lookups
// - SQL Server: 314% faster single lookups
// - MongoDB: 51% faster single lookups
// - Index size reduction: 65-73% smaller indexes

// Throughput improvements:
// - Single entity lookup: 3x improvement (800 → 2,400 req/sec)
// - Bulk operations: 2.3x improvement
```

## Memory Management

### Efficient Batch Processing

```csharp
// ✅ Process large datasets without memory issues
public async Task ProcessLargeDataset<T>() where T : IEntity
{
    await foreach (var batch in T.AllStream().ToBatches(1000))
    {
        await Parallel.ForEachAsync(batch, async (item, ct) =>
        {
            await ProcessItem(item);
        });
    }
}

// ✅ Memory-efficient aggregations
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
public static async IAsyncEnumerable<T> GetEntitiesAsync<T>(
    [EnumeratorCancellation] CancellationToken ct = default)
    where T : IEntity
{
    const int batchSize = 1000;
    int offset = 0;

    while (!ct.IsCancellationRequested)
    {
        var batch = await T.Query()
            .Skip(offset)
            .Take(batchSize)
            .ToArrayAsync(ct);

        if (!batch.Any()) yield break;

        foreach (var entity in batch)
            yield return entity;

        offset += batchSize;
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

    return await Product.Query()
        .Where(p => p.Name.Contains(query))
        .ToArrayAsync(cts.Token);
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

        var product = await Product.ById(id);
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

        var products = await Product.Query()
            .Where(p => p.Category == category && p.IsFeatured)
            .OrderByDescending(p => p.Rating)
            .Take(20)
            .ToArrayAsync();

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
        var revenue = await Order.Query()
            .Where(o => o.Created > DateTime.UtcNow.AddDays(-30))
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
    var capabilities = Data<Product, string>.QueryCaps;

    if (capabilities.Capabilities.HasFlag(QueryCapabilities.FullTextSearch))
    {
        // Use provider's full-text search
        return await Product.Query($"SEARCH '{query}'");
    }
    else
    {
        // Fallback to simple string matching
        return await Product.Where(p => p.Name.Contains(query));
    }
}
```

## Background Services Performance

### Efficient Message Processing

```csharp
public class OrderProcessor : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        // ✅ Process orders in batches
        await this.On<OrderCreated>(async orders =>
        {
            var batches = orders.Chunk(100);

            await Parallel.ForEachAsync(batches,
                new ParallelOptions { MaxDegreeOfParallelism = 4 },
                async (batch, ct) =>
                {
                    await ProcessOrderBatch(batch.ToArray(), ct);
                });
        });
    }

    private async Task ProcessOrderBatch(OrderCreated[] orders, CancellationToken ct)
    {
        // Batch database operations
        var orderEntities = orders.Select(o => new Order
        {
            UserId = o.UserId,
            Total = o.Total
        }).ToArray();

        await Order.SaveBatch(orderEntities, ct);
    }
}
```

## Performance Monitoring

### Built-in Metrics

```csharp
// ✅ Monitor framework performance
public class PerformanceService
{
    public async Task<PerformanceReport> GetPerformanceMetrics()
    {
        var report = new PerformanceReport();

        // Check query capabilities per provider
        report.DataCapabilities = Data<Product, string>.QueryCaps.Capabilities;

        // Monitor entity optimization status
        var optimizedTypes = StorageOptimization.GetOptimizedEntityTypes();
        report.OptimizedEntities = optimizedTypes.Count;

        // Check provider health
        var healthReports = await _healthService.CheckAllProvidersAsync();
        report.ProviderHealth = healthReports;

        return report;
    }
}
```

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
var activeUsers = await User.Where(u => u.IsActive);
```

### ❌ N+1 Query Problems

```csharp
// Wrong: N+1 queries
var orders = await Order.All();
foreach (var order in orders)
{
    var user = await User.ById(order.UserId); // N queries
}

// Right: Batch loading or include patterns
var orders = await Order.All();
var userIds = orders.Select(o => o.UserId).Distinct();
var users = await User.Where(u => userIds.Contains(u.Id));
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
    await foreach (var item in Data.AllStream())
    {
        // No cancellation check
        await ProcessItem(item);
    }
}

// Right: Respect cancellation
public async Task ProcessData(CancellationToken ct = default)
{
    await foreach (var item in Data.AllStream(ct: ct))
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

**Last Validation**: 2025-01-17 by Framework Specialist
**Framework Version Tested**: v0.2.18+
# Advanced Topics

**Document Type**: Reference Documentation (REF)  
**Target Audience**: Senior Developers, Architects, AI Agents  
**Last Updated**: 2025-01-10  
**Framework Version**: v0.2.18+

---

## 🚀 Sora Framework Advanced Topics

This document covers advanced patterns, extensibility, performance optimization, and production deployment considerations for the Sora Framework.

---

## 🔧 Advanced Framework Extensibility

### 1. **Custom Auto-Registrars**

Create modules that auto-discover and register themselves:

```csharp
public class MyCustomModule : ISoraAutoRegistrar
{
    public string ModuleName => "MyCompany.CustomModule";
    public string? ModuleVersion => Assembly.GetExecutingAssembly().GetName().Version?.ToString();
    
    public void Initialize(IServiceCollection services)
    {
        // Register core services
        services.TryAddSingleton<IMyCustomService, MyCustomService>();
        services.TryAddScoped<IMyCustomRepository, MyCustomRepository>();
        
        // Register health checks
        services.AddSingleton<IHealthContributor, MyCustomHealthCheck>();
        
        // Configure options
        services.ConfigureOptions<MyCustomOptions>();
        
        // Register background services
        services.AddHostedService<MyCustomBackgroundService>();
        
        // Scan and register custom providers
        RegisterCustomProviders(services);
        
        // Register interceptors
        RegisterInterceptors(services);
    }
    
    public string Describe() => 
        $"Module: {ModuleName} v{ModuleVersion} - Custom business logic module";
    
    private void RegisterCustomProviders(IServiceCollection services)
    {
        var assembly = Assembly.GetExecutingAssembly();
        var providerTypes = assembly.GetTypes()
            .Where(t => typeof(IMyCustomProvider).IsAssignableFrom(t) && !t.IsAbstract);
        
        foreach (var providerType in providerTypes)
        {
            services.AddSingleton(typeof(IMyCustomProvider), providerType);
        }
    }
    
    private void RegisterInterceptors(services)
    {
        // Register Flow interceptors if Flow is available
        var flowTypes = AppDomain.CurrentDomain.GetAssemblies()
            .SelectMany(a => a.GetTypes())
            .Where(t => t.Name.Contains("FlowInterceptors"));
            
        if (flowTypes.Any())
        {
            // Flow is available, register interceptors
            RegisterFlowInterceptors();
        }
    }
}
```

### 2. **Custom Data Providers**

Implement custom data providers for specialized storage:

```csharp
public class CosmosDbProvider : IDataProvider
{
    public string Name => "CosmosDB";
    public int Priority => 100; // Higher priority than default providers
    
    private readonly CosmosClient _cosmosClient;
    private readonly ILogger<CosmosDbProvider> _logger;
    
    public bool CanServe(Type entityType)
    {
        // Check if entity has CosmosDB-specific attributes
        return entityType.GetCustomAttribute<CosmosDbEntityAttribute>() != null;
    }
    
    public async Task<IDataAdapter<T>> GetAdapterAsync<T>() where T : IEntity
    {
        var entityType = typeof(T);
        var attribute = entityType.GetCustomAttribute<CosmosDbEntityAttribute>();
        
        if (attribute == null)
            throw new InvalidOperationException($"Entity {entityType.Name} is not configured for CosmosDB");
        
        var database = _cosmosClient.GetDatabase(attribute.DatabaseId);
        var container = database.GetContainer(attribute.ContainerId);
        
        return new CosmosDbAdapter<T>(container, _logger);
    }
    
    public async Task<bool> IsAvailableAsync(CancellationToken ct = default)
    {
        try
        {
            await _cosmosClient.ReadAccountAsync();
            return true;
        }
        catch
        {
            return false;
        }
    }
}

public class CosmosDbAdapter<T> : IDataAdapter<T> where T : IEntity
{
    private readonly Container _container;
    private readonly ILogger _logger;
    
    public async Task<T[]> GetAllAsync(CancellationToken ct = default)
    {
        var query = new QueryDefinition("SELECT * FROM c");
        var iterator = _container.GetItemQueryIterator<T>(query);
        
        var results = new List<T>();
        while (iterator.HasMoreResults)
        {
            var response = await iterator.ReadNextAsync(ct);
            results.AddRange(response);
        }
        
        return results.ToArray();
    }
    
    public async Task<T?> GetByIdAsync(string id, CancellationToken ct = default)
    {
        try
        {
            var response = await _container.ReadItemAsync<T>(id, new PartitionKey(id), cancellationToken: ct);
            return response.Resource;
        }
        catch (CosmosException ex) when (ex.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }
    }
    
    public IQueryable<T> Query()
    {
        return _container.GetItemLinqQueryable<T>();
    }
    
    // ... other methods
}

[AttributeUsage(AttributeTargets.Class)]
public class CosmosDbEntityAttribute : Attribute
{
    public string DatabaseId { get; set; } = "";
    public string ContainerId { get; set; } = "";
    public string? PartitionKey { get; set; }
}

// Usage
[CosmosDbEntity(DatabaseId = "MyApp", ContainerId = "Products")]
public class Product : Entity<Product>
{
    public string Name { get; set; } = "";
    public decimal Price { get; set; }
    public string Category { get; set; } = "";
}
```

### 3. **Custom AI Providers**

Create custom AI providers for specialized models:

```csharp
public class CustomLLMProvider : IAiProvider
{
    public string Name => "CustomLLM";
    public bool IsAvailable => _httpClient != null && _isHealthy;
    
    private readonly HttpClient _httpClient;
    private readonly CustomLLMOptions _options;
    private bool _isHealthy = true;
    
    public async Task<AiChatResponse> ChatAsync(AiChatRequest request, CancellationToken ct = default)
    {
        var customRequest = new CustomLLMRequest
        {
            Model = request.Model ?? _options.DefaultModel,
            Messages = request.Messages.Select(m => new CustomMessage
            {
                Role = m.Role.ToString().ToLower(),
                Content = m.Content
            }).ToArray(),
            MaxTokens = request.MaxTokens,
            Temperature = request.Temperature
        };
        
        var httpRequest = new HttpRequestMessage(HttpMethod.Post, "/v1/chat/completions")
        {
            Content = JsonContent.Create(customRequest)
        };
        
        httpRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _options.ApiKey);
        
        try
        {
            var response = await _httpClient.SendAsync(httpRequest, ct);
            response.EnsureSuccessStatusCode();
            
            var responseContent = await response.Content.ReadAsStringAsync(ct);
            var customResponse = JsonSerializer.Deserialize<CustomLLMResponse>(responseContent);
            
            return new AiChatResponse
            {
                Choices = customResponse?.Choices?.Select(c => new AiChatChoice
                {
                    Message = new AiMessage
                    {
                        Role = Enum.Parse<AiMessageRole>(c.Message.Role, ignoreCase: true),
                        Content = c.Message.Content
                    },
                    FinishReason = c.FinishReason
                }).ToArray() ?? []
            };
        }
        catch (Exception ex)
        {
            _isHealthy = false;
            throw new AiException($"Custom LLM request failed: {ex.Message}", ex);
        }
    }
    
    public async IAsyncEnumerable<AiChatStreamChunk> ChatStreamAsync(
        AiChatRequest request, 
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        // Implement streaming response
        var streamingRequest = CreateStreamingRequest(request);
        
        using var httpResponse = await _httpClient.SendAsync(streamingRequest, HttpCompletionOption.ResponseHeadersRead, ct);
        using var stream = await httpResponse.Content.ReadAsStreamAsync(ct);
        using var reader = new StreamReader(stream);
        
        string? line;
        while ((line = await reader.ReadLineAsync()) != null && !ct.IsCancellationRequested)
        {
            if (line.StartsWith("data: "))
            {
                var data = line.Substring(6);
                if (data == "[DONE]") yield break;
                
                var chunk = JsonSerializer.Deserialize<CustomLLMStreamChunk>(data);
                if (chunk != null)
                {
                    yield return new AiChatStreamChunk
                    {
                        Delta = new AiMessage
                        {
                            Role = AiMessageRole.Assistant,
                            Content = chunk.Delta?.Content ?? ""
                        },
                        FinishReason = chunk.FinishReason
                    };
                }
            }
        }
    }
    
    public async Task<AiEmbeddingResponse> EmbedAsync(AiEmbeddingRequest request, CancellationToken ct = default)
    {
        var customRequest = new CustomEmbeddingRequest
        {
            Model = request.Model ?? _options.DefaultEmbeddingModel,
            Input = request.Input
        };
        
        var httpRequest = new HttpRequestMessage(HttpMethod.Post, "/v1/embeddings")
        {
            Content = JsonContent.Create(customRequest)
        };
        
        httpRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _options.ApiKey);
        
        var response = await _httpClient.SendAsync(httpRequest, ct);
        response.EnsureSuccessStatusCode();
        
        var responseContent = await response.Content.ReadAsStringAsync(ct);
        var customResponse = JsonSerializer.Deserialize<CustomEmbeddingResponse>(responseContent);
        
        return new AiEmbeddingResponse
        {
            Embeddings = customResponse?.Data?.Select(d => new AiEmbedding
            {
                Vector = d.Embedding,
                Index = d.Index
            }).ToArray() ?? []
        };
    }
}

// Auto-registrar for custom AI provider
public class CustomAiAutoRegistrar : ISoraAutoRegistrar
{
    public string ModuleName => "MyCompany.AI.CustomLLM";
    public string? ModuleVersion => "1.0.0";
    
    public void Initialize(IServiceCollection services)
    {
        services.Configure<CustomLLMOptions>(options =>
        {
            // Configure from settings
        });
        
        services.AddHttpClient<CustomLLMProvider>(client =>
        {
            client.Timeout = TimeSpan.FromMinutes(2);
        });
        
        services.AddSingleton<IAiProvider, CustomLLMProvider>();
        services.AddSingleton<IHealthContributor, CustomLLMHealthCheck>();
    }
}
```

### 4. **Custom Flow Interceptors**

Implement sophisticated data transformation logic:

```csharp
public class AdvancedProductInterceptor : ISoraAutoRegistrar
{
    public string ModuleName => "ProductFlow.AdvancedInterceptors";
    public string? ModuleVersion => "1.0.0";
    
    public void Initialize(IServiceCollection services)
    {
        // Register services needed by interceptors
        services.AddSingleton<IProductEnrichmentService, ProductEnrichmentService>();
        services.AddSingleton<IDataQualityService, DataQualityService>();
        
        RegisterFlowInterceptors(services);
    }
    
    private void RegisterFlowInterceptors(IServiceCollection services)
    {
        FlowInterceptors
            .For<Product>()
            .BeforeIntake(async product =>
            {
                // Data quality validation
                var qualityScore = await CalculateDataQuality(product);
                if (qualityScore < 0.7)
                {
                    return FlowIntakeActions.Park(product, "QUALITY_THRESHOLD", 
                        $"Data quality score: {qualityScore:F2}");
                }
                
                return FlowIntakeActions.Continue(product);
            })
            .AfterIntake(async product =>
            {
                // Normalize and clean data
                product.Name = CleanProductName(product.Name);
                product.Category = NormalizeCategory(product.Category);
                
                return FlowStageActions.Continue(product);
            })
            .BeforeAssociation(async product =>
            {
                // Check for duplicate products with different SKUs
                var potentialDuplicates = await FindPotentialDuplicates(product);
                if (potentialDuplicates.Any())
                {
                    return FlowStageActions.Park(product, "POTENTIAL_DUPLICATE",
                        $"Similar products found: {string.Join(", ", potentialDuplicates.Select(p => p.SKU))}");
                }
                
                return FlowStageActions.Continue(product);
            })
            .OnAssociationSuccess(async product =>
            {
                // Enrich with external data
                await EnrichWithExternalData(product);
                
                // Update search index
                await UpdateSearchIndex(product);
                
                return FlowStageActions.Continue(product);
            })
            .OnAssociationFailure(async product =>
            {
                // Handle association failures
                await LogAssociationFailure(product);
                
                // Try alternative association strategies
                var alternativeMatch = await TryAlternativeMatching(product);
                if (alternativeMatch != null)
                {
                    await AssociateWithExistingProduct(product, alternativeMatch);
                    return FlowStageActions.Continue(product);
                }
                
                return FlowStageActions.Park(product, "ASSOCIATION_FAILED", 
                    "Unable to associate with existing products");
            })
            .AfterProjection(async product =>
            {
                // Post-processing notifications
                await NotifyDownstreamSystems(product);
                
                // Update analytics
                await UpdateProductAnalytics(product);
                
                return FlowStageActions.Continue(product);
            });
            
        // Conditional interceptors based on source system
        FlowInterceptors
            .For<Product>()
            .BeforeIntake(async (product, metadata) =>
            {
                if (metadata.Source.System == "legacy_erp")
                {
                    // Special handling for legacy ERP data
                    return await HandleLegacyErpProduct(product);
                }
                
                return FlowIntakeActions.Continue(product);
            });
    }
    
    private async Task<double> CalculateDataQuality(Product product)
    {
        double score = 0.0;
        int totalChecks = 0;
        
        // Name quality
        totalChecks++;
        if (!string.IsNullOrWhiteSpace(product.Name) && product.Name.Length > 3)
            score += 1.0;
        
        // SKU format
        totalChecks++;
        if (IsValidSKUFormat(product.SKU))
            score += 1.0;
        
        // Price validity
        totalChecks++;
        if (product.Price > 0)
            score += 1.0;
        
        // Category mapping
        totalChecks++;
        if (await IsValidCategory(product.Category))
            score += 1.0;
        
        return totalChecks > 0 ? score / totalChecks : 0.0;
    }
    
    private async Task<Product[]> FindPotentialDuplicates(Product product)
    {
        // Use fuzzy matching to find similar products
        var similarNameProducts = await Product.Query()
            .Where(p => p.Name.Contains(product.Name.Substring(0, Math.Min(product.Name.Length, 10))))
            .ToArrayAsync();
        
        var duplicates = new List<Product>();
        
        foreach (var similar in similarNameProducts)
        {
            var similarity = CalculateStringSimilarity(product.Name, similar.Name);
            if (similarity > 0.85) // 85% similarity threshold
            {
                duplicates.Add(similar);
            }
        }
        
        return duplicates.ToArray();
    }
    
    private async Task EnrichWithExternalData(Product product)
    {
        try
        {
            // Call external API for product enrichment
            var enrichmentData = await CallProductEnrichmentApi(product.SKU);
            if (enrichmentData != null)
            {
                product.Description = enrichmentData.Description ?? product.Description;
                product.Specifications = enrichmentData.Specifications ?? product.Specifications;
                product.Images = enrichmentData.Images ?? product.Images;
            }
        }
        catch (Exception ex)
        {
            // Log but don't fail the pipeline
            _logger.LogWarning(ex, "Failed to enrich product {SKU}", product.SKU);
        }
    }
}
```

---

## ⚡ Advanced Performance Optimization

### 1. **Data Access Optimization**

Advanced querying and caching patterns:

```csharp
public class OptimizedProductService
{
    private readonly IMemoryCache _memoryCache;
    private readonly IDistributedCache _distributedCache;
    private readonly ILogger<OptimizedProductService> _logger;
    
    public async Task<Product[]> GetProductsOptimized(ProductSearchRequest request, CancellationToken ct = default)
    {
        // Generate cache key based on request
        var cacheKey = GenerateCacheKey(request);
        
        // Try memory cache first (fastest)
        if (_memoryCache.TryGetValue(cacheKey, out Product[]? cached))
        {
            _logger.LogDebug("Cache hit (memory): {CacheKey}", cacheKey);
            return cached;
        }
        
        // Try distributed cache (faster than database)
        var distributedValue = await _distributedCache.GetStringAsync(cacheKey, ct);
        if (distributedValue != null)
        {
            var products = JsonSerializer.Deserialize<Product[]>(distributedValue);
            if (products != null)
            {
                _logger.LogDebug("Cache hit (distributed): {CacheKey}", cacheKey);
                
                // Warm memory cache
                _memoryCache.Set(cacheKey, products, TimeSpan.FromMinutes(5));
                return products;
            }
        }
        
        // Load from database with optimizations
        var query = BuildOptimizedQuery(request);
        var results = await query.ToArrayAsync(ct);
        
        // Cache at both levels
        _memoryCache.Set(cacheKey, results, TimeSpan.FromMinutes(5));
        await _distributedCache.SetStringAsync(cacheKey, 
            JsonSerializer.Serialize(results),
            new DistributedCacheEntryOptions
            {
                AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(30)
            }, ct);
        
        _logger.LogDebug("Database query executed: {CacheKey}", cacheKey);
        return results;
    }
    
    private IQueryable<Product> BuildOptimizedQuery(ProductSearchRequest request)
    {
        var query = Product.Query();
        
        // Apply filters in optimal order (most selective first)
        if (request.CategoryId.HasValue)
        {
            // Most selective filter first
            query = query.Where(p => p.CategoryId == request.CategoryId.Value);
        }
        
        if (request.MinPrice.HasValue || request.MaxPrice.HasValue)
        {
            // Range filters can use indexes
            if (request.MinPrice.HasValue)
                query = query.Where(p => p.Price >= request.MinPrice.Value);
            if (request.MaxPrice.HasValue)
                query = query.Where(p => p.Price <= request.MaxPrice.Value);
        }
        
        if (!string.IsNullOrEmpty(request.SearchTerm))
        {
            // Full-text search last (most expensive)
            query = query.Where(p => p.Name.Contains(request.SearchTerm) || 
                                   p.Description.Contains(request.SearchTerm));
        }
        
        // Optimize sorting
        if (request.SortBy == ProductSortBy.Name)
            query = query.OrderBy(p => p.Name);
        else if (request.SortBy == ProductSortBy.Price)
            query = query.OrderBy(p => p.Price);
        else
            query = query.OrderBy(p => p.Created); // Default sort with good index
        
        // Apply paging
        return query.Skip((request.Page - 1) * request.PageSize).Take(request.PageSize);
    }
    
    public async Task ProcessLargeDatasetOptimized<T>(
        Func<T, Task> processor, 
        CancellationToken ct = default) where T : IEntity
    {
        const int batchSize = 1000;
        const int maxConcurrency = Environment.ProcessorCount;
        
        using var semaphore = new SemaphoreSlim(maxConcurrency);
        var processingTasks = new List<Task>();
        
        await foreach (var batch in T.AllStream().Buffer(batchSize).WithCancellation(ct))
        {
            // Process batches in parallel with concurrency control
            var batchTask = ProcessBatch(batch, processor, semaphore, ct);
            processingTasks.Add(batchTask);
            
            // Prevent unbounded task accumulation
            if (processingTasks.Count >= maxConcurrency * 2)
            {
                await Task.WhenAny(processingTasks);
                processingTasks.RemoveAll(t => t.IsCompleted);
            }
        }
        
        // Wait for all remaining tasks
        await Task.WhenAll(processingTasks);
    }
    
    private async Task ProcessBatch<T>(
        IEnumerable<T> batch, 
        Func<T, Task> processor, 
        SemaphoreSlim semaphore, 
        CancellationToken ct)
    {
        await Parallel.ForEachAsync(batch, new ParallelOptions
        {
            MaxDegreeOfParallelism = Environment.ProcessorCount,
            CancellationToken = ct
        }, async (item, ct) =>
        {
            await semaphore.WaitAsync(ct);
            try
            {
                await processor(item);
            }
            finally
            {
                semaphore.Release();
            }
        });
    }
}

// Extension for buffering streams
public static class AsyncEnumerableExtensions
{
    public static async IAsyncEnumerable<T[]> Buffer<T>(
        this IAsyncEnumerable<T> source, 
        int bufferSize,
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        var buffer = new List<T>(bufferSize);
        
        await foreach (var item in source.WithCancellation(ct))
        {
            buffer.Add(item);
            
            if (buffer.Count >= bufferSize)
            {
                yield return buffer.ToArray();
                buffer.Clear();
            }
        }
        
        if (buffer.Count > 0)
        {
            yield return buffer.ToArray();
        }
    }
}
```

### 2. **Advanced Caching Strategies**

Multi-level caching with invalidation:

```csharp
public class AdvancedCacheManager
{
    private readonly IMemoryCache _memoryCache;
    private readonly IDistributedCache _distributedCache;
    private readonly IBus _messageBus;
    private readonly ILogger<AdvancedCacheManager> _logger;
    
    private readonly ConcurrentDictionary<string, SemaphoreSlim> _lockSemaphores = new();
    
    public async Task<T?> GetOrSetAsync<T>(
        string key, 
        Func<Task<T?>> factory, 
        CacheOptions? options = null,
        CancellationToken ct = default) where T : class
    {
        options ??= CacheOptions.Default;
        
        // Try memory cache first
        if (_memoryCache.TryGetValue(key, out T? cached))
        {
            return cached;
        }
        
        // Try distributed cache
        var distributedValue = await _distributedCache.GetStringAsync(key, ct);
        if (distributedValue != null)
        {
            try
            {
                var deserialized = JsonSerializer.Deserialize<T>(distributedValue);
                if (deserialized != null)
                {
                    // Warm memory cache
                    _memoryCache.Set(key, deserialized, options.MemoryCacheDuration);
                    return deserialized;
                }
            }
            catch (JsonException ex)
            {
                _logger.LogWarning(ex, "Failed to deserialize cached value for key {Key}", key);
            }
        }
        
        // Use semaphore to prevent cache stampede
        var semaphore = _lockSemaphores.GetOrAdd(key, _ => new SemaphoreSlim(1, 1));
        await semaphore.WaitAsync(ct);
        
        try
        {
            // Double-check cache after acquiring lock
            if (_memoryCache.TryGetValue(key, out cached))
            {
                return cached;
            }
            
            // Load from factory
            var value = await factory();
            if (value != null)
            {
                // Set both caches
                _memoryCache.Set(key, value, options.MemoryCacheDuration);
                
                var serialized = JsonSerializer.Serialize(value);
                await _distributedCache.SetStringAsync(key, serialized, 
                    new DistributedCacheEntryOptions
                    {
                        AbsoluteExpirationRelativeToNow = options.DistributedCacheDuration
                    }, ct);
                
                // Register for invalidation
                if (options.Tags?.Any() == true)
                {
                    await RegisterCacheTag(key, options.Tags);
                }
            }
            
            return value;
        }
        finally
        {
            semaphore.Release();
        }
    }
    
    public async Task InvalidateByTagsAsync(params string[] tags)
    {
        foreach (var tag in tags)
        {
            var keys = await GetKeysByTag(tag);
            await InvalidateKeysAsync(keys);
            
            // Notify other instances via messaging
            await new CacheInvalidationEvent { Tag = tag }.Send();
        }
    }
    
    private async Task RegisterCacheTag(string key, string[] tags)
    {
        foreach (var tag in tags)
        {
            var tagKey = $"cache:tag:{tag}";
            var existingKeys = await _distributedCache.GetStringAsync(tagKey) ?? "";
            var keys = existingKeys.Split(',', StringSplitOptions.RemoveEmptyEntries).ToHashSet();
            keys.Add(key);
            
            await _distributedCache.SetStringAsync(tagKey, string.Join(",", keys));
        }
    }
    
    private async Task<string[]> GetKeysByTag(string tag)
    {
        var tagKey = $"cache:tag:{tag}";
        var keysString = await _distributedCache.GetStringAsync(tagKey) ?? "";
        return keysString.Split(',', StringSplitOptions.RemoveEmptyEntries);
    }
    
    private async Task InvalidateKeysAsync(string[] keys)
    {
        var tasks = keys.Select(async key =>
        {
            _memoryCache.Remove(key);
            await _distributedCache.RemoveAsync(key);
        });
        
        await Task.WhenAll(tasks);
    }
}

public class CacheOptions
{
    public TimeSpan MemoryCacheDuration { get; set; } = TimeSpan.FromMinutes(5);
    public TimeSpan DistributedCacheDuration { get; set; } = TimeSpan.FromMinutes(30);
    public string[]? Tags { get; set; }
    
    public static CacheOptions Default => new();
    
    public static CacheOptions Short => new()
    {
        MemoryCacheDuration = TimeSpan.FromMinutes(1),
        DistributedCacheDuration = TimeSpan.FromMinutes(5)
    };
    
    public static CacheOptions Long => new()
    {
        MemoryCacheDuration = TimeSpan.FromMinutes(15),
        DistributedCacheDuration = TimeSpan.FromHours(2)
    };
}

public class CacheInvalidationEvent
{
    public string Tag { get; set; } = "";
    public DateTimeOffset Timestamp { get; set; } = DateTimeOffset.UtcNow;
}

// Usage
public class ProductService
{
    private readonly AdvancedCacheManager _cache;
    
    public async Task<Product[]> GetFeaturedProducts()
    {
        return await _cache.GetOrSetAsync(
            "products:featured",
            async () => await Product.Query().Where(p => p.IsFeatured).ToArrayAsync(),
            new CacheOptions
            {
                MemoryCacheDuration = TimeSpan.FromMinutes(10),
                DistributedCacheDuration = TimeSpan.FromMinutes(60),
                Tags = ["products", "featured"]
            }
        );
    }
    
    public async Task UpdateProduct(Product product)
    {
        await product.SaveAsync();
        
        // Invalidate related cache entries
        await _cache.InvalidateByTagsAsync("products", $"product:{product.Id}");
    }
}
```

### 3. **Advanced Messaging Patterns**

Sophisticated event handling and processing:

```csharp
public class EventSourcingService
{
    private readonly IBus _bus;
    private readonly ILogger<EventSourcingService> _logger;
    
    public async Task<TAggregate> ReplayEvents<TAggregate>(
        string aggregateId, 
        DateTimeOffset? toTimestamp = null,
        CancellationToken ct = default) where TAggregate : AggregateRoot<TAggregate>, new()
    {
        var aggregate = new TAggregate { Id = aggregateId };
        
        await foreach (var eventData in GetEventsForAggregate(aggregateId, toTimestamp).WithCancellation(ct))
        {
            var domainEvent = DeserializeEvent(eventData);
            if (domainEvent != null)
            {
                aggregate.ApplyEvent(domainEvent);
            }
        }
        
        return aggregate;
    }
    
    private async IAsyncEnumerable<EventData> GetEventsForAggregate(
        string aggregateId, 
        DateTimeOffset? toTimestamp,
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        const int batchSize = 100;
        int offset = 0;
        
        while (!ct.IsCancellationRequested)
        {
            var events = await EventStore.Query()
                .Where(e => e.AggregateId == aggregateId)
                .Where(e => !toTimestamp.HasValue || e.Timestamp <= toTimestamp.Value)
                .OrderBy(e => e.Sequence)
                .Skip(offset)
                .Take(batchSize)
                .ToArrayAsync(ct);
            
            if (!events.Any()) yield break;
            
            foreach (var eventData in events)
            {
                yield return eventData;
            }
            
            offset += batchSize;
        }
    }
}

public class SagaOrchestrator : BackgroundService
{
    private readonly IBus _bus;
    private readonly ISagaRepository _sagaRepository;
    
    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        // Handle saga initiation events
        await this.On<OrderCreated>(async evt =>
        {
            var saga = new OrderFulfillmentSaga
            {
                Id = Ulid.NewUlid().ToString(),
                OrderId = evt.OrderId,
                State = SagaState.Started,
                StepTimeouts = new Dictionary<string, DateTimeOffset>
                {
                    ["ReserveInventory"] = DateTimeOffset.UtcNow.AddMinutes(5),
                    ["ProcessPayment"] = DateTimeOffset.UtcNow.AddMinutes(10),
                    ["ShipOrder"] = DateTimeOffset.UtcNow.AddHours(2)
                }
            };
            
            await _sagaRepository.SaveAsync(saga);
            
            // Start the saga
            await new ReserveInventoryCommand
            {
                OrderId = evt.OrderId,
                SagaId = saga.Id
            }.Send();
        });
        
        // Handle saga step completions
        await this.On<InventoryReserved>(async evt =>
        {
            var saga = await _sagaRepository.GetByOrderIdAsync(evt.OrderId);
            if (saga != null && saga.State == SagaState.Started)
            {
                saga.State = SagaState.InventoryReserved;
                saga.CompletedSteps.Add("ReserveInventory");
                await _sagaRepository.SaveAsync(saga);
                
                // Continue to next step
                await new ProcessPaymentCommand
                {
                    OrderId = evt.OrderId,
                    SagaId = saga.Id
                }.Send();
            }
        });
        
        // Handle saga failures
        await this.On<PaymentFailed>(async evt =>
        {
            var saga = await _sagaRepository.GetByOrderIdAsync(evt.OrderId);
            if (saga != null)
            {
                saga.State = SagaState.Failed;
                saga.FailureReason = evt.Reason;
                await _sagaRepository.SaveAsync(saga);
                
                // Start compensation
                await new ReleaseInventoryCommand
                {
                    OrderId = evt.OrderId,
                    SagaId = saga.Id
                }.Send();
            }
        });
        
        // Handle timeouts
        _ = Task.Run(async () => await MonitorSagaTimeouts(ct), ct);
    }
    
    private async Task MonitorSagaTimeouts(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            var now = DateTimeOffset.UtcNow;
            var timedOutSagas = await _sagaRepository.GetTimedOutSagasAsync(now);
            
            foreach (var saga in timedOutSagas)
            {
                await HandleSagaTimeout(saga);
            }
            
            await Task.Delay(TimeSpan.FromMinutes(1), ct);
        }
    }
}

// Circuit breaker for external service calls
public class CircuitBreakerService<T>
{
    private readonly CircuitBreakerOptions _options;
    private volatile CircuitState _state = CircuitState.Closed;
    private volatile int _failureCount = 0;
    private volatile DateTimeOffset _lastFailureTime = DateTimeOffset.MinValue;
    private readonly object _lockObject = new();
    
    public async Task<TResult> ExecuteAsync<TResult>(Func<Task<TResult>> operation)
    {
        if (_state == CircuitState.Open)
        {
            if (DateTimeOffset.UtcNow - _lastFailureTime < _options.OpenTimeout)
            {
                throw new CircuitBreakerOpenException("Circuit breaker is open");
            }
            else
            {
                _state = CircuitState.HalfOpen;
            }
        }
        
        try
        {
            var result = await operation();
            
            if (_state == CircuitState.HalfOpen)
            {
                Reset();
            }
            
            return result;
        }
        catch (Exception ex)
        {
            RecordFailure();
            throw;
        }
    }
    
    private void RecordFailure()
    {
        lock (_lockObject)
        {
            _failureCount++;
            _lastFailureTime = DateTimeOffset.UtcNow;
            
            if (_failureCount >= _options.FailureThreshold)
            {
                _state = CircuitState.Open;
            }
        }
    }
    
    private void Reset()
    {
        lock (_lockObject)
        {
            _failureCount = 0;
            _state = CircuitState.Closed;
        }
    }
}
```

---

## 🔒 Advanced Security Patterns

### 1. **Multi-Tenant Architecture**

Secure tenant isolation:

```csharp
public class TenantContext
{
    public string TenantId { get; set; } = "";
    public string TenantName { get; set; } = "";
    public Dictionary<string, object> Properties { get; set; } = new();
}

public interface ITenantProvider
{
    Task<TenantContext?> GetTenantAsync(string identifier);
    Task<TenantContext?> GetCurrentTenantAsync();
    void SetCurrentTenant(TenantContext tenant);
}

public class MultiTenantEntity<T> : Entity<T> where T : class, new()
{
    [TenantId]
    public string TenantId { get; set; } = "";
    
    protected MultiTenantEntity()
    {
        // Auto-set tenant ID from current context
        var tenantProvider = ServiceLocator.GetService<ITenantProvider>();
        var currentTenant = tenantProvider?.GetCurrentTenantAsync().Result;
        if (currentTenant != null)
        {
            TenantId = currentTenant.TenantId;
        }
    }
    
    // Override base methods to include tenant filtering
    public new static IQueryable<T> Query()
    {
        var baseQuery = Entity<T>.Query();
        var tenantProvider = ServiceLocator.GetService<ITenantProvider>();
        var currentTenant = tenantProvider?.GetCurrentTenantAsync().Result;
        
        if (currentTenant != null)
        {
            return baseQuery.Where(e => ((MultiTenantEntity<T>)(object)e).TenantId == currentTenant.TenantId);
        }
        
        return baseQuery;
    }
}

public class TenantMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ITenantProvider _tenantProvider;
    
    public async Task InvokeAsync(HttpContext context)
    {
        var tenantId = ExtractTenantId(context);
        if (!string.IsNullOrEmpty(tenantId))
        {
            var tenant = await _tenantProvider.GetTenantAsync(tenantId);
            if (tenant != null)
            {
                _tenantProvider.SetCurrentTenant(tenant);
                context.Items["Tenant"] = tenant;
            }
        }
        
        await _next(context);
    }
    
    private string? ExtractTenantId(HttpContext context)
    {
        // Try subdomain first
        var host = context.Request.Host.Host;
        if (host.Contains('.'))
        {
            var subdomain = host.Split('.')[0];
            if (subdomain != "www" && subdomain != "api")
            {
                return subdomain;
            }
        }
        
        // Try header
        if (context.Request.Headers.TryGetValue("X-Tenant-Id", out var headerValue))
        {
            return headerValue.FirstOrDefault();
        }
        
        // Try path
        var path = context.Request.Path.Value;
        if (path?.StartsWith("/tenant/") == true)
        {
            var segments = path.Split('/');
            if (segments.Length > 2)
            {
                return segments[2];
            }
        }
        
        return null;
    }
}

[Route("api/[controller]")]
[Authorize]
public class MultiTenantProductsController : EntityController<Product>
{
    private readonly ITenantProvider _tenantProvider;
    
    public override async Task<ActionResult<Product[]>> Get()
    {
        var tenant = await _tenantProvider.GetCurrentTenantAsync();
        if (tenant == null)
        {
            return Unauthorized("No tenant context");
        }
        
        // Products are automatically filtered by tenant
        return await Product.All();
    }
}
```

### 2. **Advanced Authorization**

Policy-based authorization with dynamic policies:

```csharp
public class DynamicAuthorizationHandler : AuthorizationHandler<DynamicRequirement>
{
    private readonly IServiceProvider _serviceProvider;
    
    protected override async Task HandleRequirementAsync(
        AuthorizationHandlerContext context,
        DynamicRequirement requirement)
    {
        var authService = _serviceProvider.GetRequiredService<IDynamicAuthorizationService>();
        var user = context.User;
        
        var isAuthorized = await authService.IsAuthorizedAsync(
            user, 
            requirement.Resource, 
            requirement.Action,
            context);
        
        if (isAuthorized)
        {
            context.Succeed(requirement);
        }
        else
        {
            context.Fail();
        }
    }
}

public interface IDynamicAuthorizationService
{
    Task<bool> IsAuthorizedAsync(ClaimsPrincipal user, string resource, string action, AuthorizationHandlerContext context);
    Task<AuthorizationPolicy[]> GetPoliciesForUserAsync(ClaimsPrincipal user);
}

public class DynamicAuthorizationService : IDynamicAuthorizationService
{
    private readonly ILogger<DynamicAuthorizationService> _logger;
    
    public async Task<bool> IsAuthorizedAsync(
        ClaimsPrincipal user, 
        string resource, 
        string action, 
        AuthorizationHandlerContext context)
    {
        // Get user roles and permissions from database
        var userId = user.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        if (string.IsNullOrEmpty(userId))
            return false;
        
        var userPermissions = await GetUserPermissions(userId);
        
        // Check direct permissions
        if (userPermissions.Any(p => p.Resource == resource && p.Action == action))
            return true;
        
        // Check role-based permissions
        var userRoles = user.FindAll(ClaimTypes.Role).Select(c => c.Value);
        var rolePermissions = await GetRolePermissions(userRoles);
        
        if (rolePermissions.Any(p => p.Resource == resource && p.Action == action))
            return true;
        
        // Check attribute-based access control (ABAC)
        return await CheckAttributeBasedAccess(user, resource, action, context);
    }
    
    private async Task<bool> CheckAttributeBasedAccess(
        ClaimsPrincipal user, 
        string resource, 
        string action,
        AuthorizationHandlerContext context)
    {
        // Example ABAC rules
        if (resource == "Order" && action == "Read")
        {
            // Users can read their own orders
            if (context.Resource is Order order)
            {
                var userId = user.FindFirst(ClaimTypes.NameIdentifier)?.Value;
                return order.UserId == userId;
            }
        }
        
        if (resource == "Product" && action == "Write")
        {
            // Check if user has write access during business hours
            var currentTime = DateTime.Now.TimeOfDay;
            var businessStart = new TimeSpan(9, 0, 0);
            var businessEnd = new TimeSpan(17, 0, 0);
            
            if (currentTime < businessStart || currentTime > businessEnd)
            {
                _logger.LogWarning("Access denied outside business hours for user {UserId}", 
                    user.FindFirst(ClaimTypes.NameIdentifier)?.Value);
                return false;
            }
        }
        
        return false;
    }
}

// Usage with dynamic attributes
[Route("api/[controller]")]
[ApiController]
public class OrdersController : ControllerBase
{
    [HttpGet("{id}")]
    [Authorize(Policy = "DynamicPolicy")]
    [ResourceAuthorization("Order", "Read")]
    public async Task<ActionResult<Order>> Get(string id)
    {
        var order = await Order.ByIdAsync(id);
        if (order == null) return NotFound();
        
        return order;
    }
    
    [HttpPut("{id}")]
    [Authorize(Policy = "DynamicPolicy")]
    [ResourceAuthorization("Order", "Write")]
    public async Task<IActionResult> Put(string id, [FromBody] Order order)
    {
        // Update logic
        return Ok();
    }
}

[AttributeUsage(AttributeTargets.Method)]
public class ResourceAuthorizationAttribute : Attribute
{
    public string Resource { get; }
    public string Action { get; }
    
    public ResourceAuthorizationAttribute(string resource, string action)
    {
        Resource = resource;
        Action = action;
    }
}
```

---

## 🚀 Production Deployment Patterns

### 1. **Blue-Green Deployment**

Zero-downtime deployment strategy:

```csharp
public class BlueGreenDeploymentManager
{
    private readonly IOrchestrationService _orchestration;
    private readonly IHealthService _health;
    private readonly ILoadBalancerService _loadBalancer;
    
    public async Task<DeploymentResult> DeployAsync(DeploymentRequest request)
    {
        var currentColor = await GetCurrentColor();
        var newColor = currentColor == "blue" ? "green" : "blue";
        
        try
        {
            // 1. Deploy to inactive environment
            await DeployToEnvironment(newColor, request);
            
            // 2. Run health checks
            var healthResult = await WaitForHealthy(newColor, TimeSpan.FromMinutes(5));
            if (!healthResult.IsHealthy)
            {
                await RollbackEnvironment(newColor);
                return DeploymentResult.Failed($"Health checks failed: {healthResult.FailureReason}");
            }
            
            // 3. Run smoke tests
            var smokeTestResult = await RunSmokeTests(newColor);
            if (!smokeTestResult.Success)
            {
                await RollbackEnvironment(newColor);
                return DeploymentResult.Failed($"Smoke tests failed: {smokeTestResult.FailureReason}");
            }
            
            // 4. Switch traffic
            await _loadBalancer.SwitchTrafficAsync(newColor);
            
            // 5. Monitor for issues
            var monitoringResult = await MonitorDeployment(newColor, TimeSpan.FromMinutes(10));
            if (!monitoringResult.Success)
            {
                // Quick rollback
                await _loadBalancer.SwitchTrafficAsync(currentColor);
                return DeploymentResult.Failed($"Monitoring detected issues: {monitoringResult.FailureReason}");
            }
            
            // 6. Cleanup old environment
            await CleanupEnvironment(currentColor);
            
            return DeploymentResult.Success($"Successfully deployed to {newColor}");
        }
        catch (Exception ex)
        {
            await RollbackEnvironment(newColor);
            return DeploymentResult.Failed($"Deployment failed: {ex.Message}");
        }
    }
    
    private async Task<HealthCheckResult> WaitForHealthy(string environment, TimeSpan timeout)
    {
        var startTime = DateTime.UtcNow;
        var endpoint = GetEnvironmentEndpoint(environment);
        
        while (DateTime.UtcNow - startTime < timeout)
        {
            try
            {
                var health = await _health.CheckHealthAsync($"{endpoint}/health");
                if (health.Status == HealthStatus.Healthy)
                {
                    return HealthCheckResult.Healthy();
                }
                
                // Check readiness too
                var readiness = await _health.CheckHealthAsync($"{endpoint}/health/ready");
                if (readiness.Status == HealthStatus.Healthy)
                {
                    return HealthCheckResult.Healthy();
                }
            }
            catch (Exception ex)
            {
                // Continue trying
            }
            
            await Task.Delay(TimeSpan.FromSeconds(10));
        }
        
        return HealthCheckResult.Unhealthy("Timeout waiting for health checks");
    }
    
    private async Task<TestResult> RunSmokeTests(string environment)
    {
        var endpoint = GetEnvironmentEndpoint(environment);
        var tests = new[]
        {
            new SmokeTest("API Connectivity", async () => await TestApiConnectivity(endpoint)),
            new SmokeTest("Database Connectivity", async () => await TestDatabaseConnectivity(endpoint)),
            new SmokeTest("Core Functionality", async () => await TestCoreFunctionality(endpoint)),
            new SmokeTest("Authentication", async () => await TestAuthentication(endpoint))
        };
        
        var results = new List<TestResult>();
        foreach (var test in tests)
        {
            try
            {
                var result = await test.Execute();
                results.Add(result);
                
                if (!result.Success)
                {
                    return TestResult.Failed($"Test '{test.Name}' failed: {result.FailureReason}");
                }
            }
            catch (Exception ex)
            {
                return TestResult.Failed($"Test '{test.Name}' threw exception: {ex.Message}");
            }
        }
        
        return TestResult.Success("All smoke tests passed");
    }
}

public class CanaryDeploymentManager
{
    private readonly IMetricsService _metrics;
    private readonly ILoadBalancerService _loadBalancer;
    
    public async Task<DeploymentResult> DeployCanaryAsync(CanaryDeploymentRequest request)
    {
        try
        {
            // 1. Deploy canary version
            await DeployCanaryVersion(request);
            
            // 2. Route small percentage of traffic to canary
            await _loadBalancer.SetTrafficSplitAsync(new TrafficSplit
            {
                Production = 95,
                Canary = 5
            });
            
            // 3. Monitor metrics
            var baselineMetrics = await _metrics.GetBaselineMetricsAsync();
            
            // 4. Gradually increase traffic if metrics are good
            var trafficPercentages = new[] { 5, 10, 25, 50, 100 };
            
            foreach (var percentage in trafficPercentages)
            {
                await _loadBalancer.SetTrafficSplitAsync(new TrafficSplit
                {
                    Production = 100 - percentage,
                    Canary = percentage
                });
                
                // Monitor for the specified duration
                await Task.Delay(request.MonitoringDuration);
                
                var currentMetrics = await _metrics.GetCurrentMetricsAsync();
                var comparison = CompareMetrics(baselineMetrics, currentMetrics);
                
                if (comparison.HasRegressions)
                {
                    // Rollback
                    await _loadBalancer.SetTrafficSplitAsync(new TrafficSplit
                    {
                        Production = 100,
                        Canary = 0
                    });
                    
                    return DeploymentResult.Failed($"Metrics regression detected: {comparison.RegressionDetails}");
                }
            }
            
            // 5. Complete deployment
            await PromoteCanaryToProduction();
            
            return DeploymentResult.Success("Canary deployment completed successfully");
        }
        catch (Exception ex)
        {
            await RollbackCanary();
            return DeploymentResult.Failed($"Canary deployment failed: {ex.Message}");
        }
    }
}
```

### 2. **Microservices Orchestration**

Service mesh and distributed systems patterns:

```csharp
public class ServiceMeshManager
{
    private readonly IServiceRegistry _serviceRegistry;
    private readonly ICircuitBreakerFactory _circuitBreakerFactory;
    private readonly IRetryPolicyFactory _retryPolicyFactory;
    
    public async Task<T> CallServiceAsync<T>(
        string serviceName, 
        string endpoint, 
        object? request = null,
        CallOptions? options = null)
    {
        options ??= CallOptions.Default;
        
        // 1. Service discovery
        var service = await _serviceRegistry.DiscoverServiceAsync(serviceName);
        if (service == null)
        {
            throw new ServiceNotFoundException($"Service {serviceName} not found");
        }
        
        // 2. Load balancing
        var instance = SelectInstance(service.Instances, options.LoadBalancingStrategy);
        var url = $"{instance.BaseUrl}{endpoint}";
        
        // 3. Circuit breaker
        var circuitBreaker = _circuitBreakerFactory.GetCircuitBreaker(serviceName);
        
        // 4. Retry policy
        var retryPolicy = _retryPolicyFactory.GetRetryPolicy(serviceName);
        
        return await retryPolicy.ExecuteAsync(async () =>
        {
            return await circuitBreaker.ExecuteAsync(async () =>
            {
                using var httpClient = CreateHttpClient(options);
                
                // 5. Add tracing headers
                AddTracingHeaders(httpClient, serviceName, endpoint);
                
                // 6. Add authentication
                await AddAuthenticationAsync(httpClient, options);
                
                // 7. Make the call
                var response = request == null
                    ? await httpClient.GetAsync(url)
                    : await httpClient.PostAsync(url, JsonContent.Create(request));
                
                response.EnsureSuccessStatusCode();
                
                var content = await response.Content.ReadAsStringAsync();
                return JsonSerializer.Deserialize<T>(content) ?? throw new InvalidOperationException("Failed to deserialize response");
            });
        });
    }
    
    private ServiceInstance SelectInstance(ServiceInstance[] instances, LoadBalancingStrategy strategy)
    {
        return strategy switch
        {
            LoadBalancingStrategy.RoundRobin => SelectRoundRobin(instances),
            LoadBalancingStrategy.LeastConnections => SelectLeastConnections(instances),
            LoadBalancingStrategy.Random => SelectRandom(instances),
            LoadBalancingStrategy.WeightedRandom => SelectWeightedRandom(instances),
            _ => instances[Random.Shared.Next(instances.Length)]
        };
    }
    
    private void AddTracingHeaders(HttpClient httpClient, string serviceName, string endpoint)
    {
        var activity = Activity.Current;
        if (activity != null)
        {
            httpClient.DefaultRequestHeaders.Add("traceparent", activity.Id);
            httpClient.DefaultRequestHeaders.Add("X-Service-Name", serviceName);
            httpClient.DefaultRequestHeaders.Add("X-Endpoint", endpoint);
        }
    }
    
    private async Task AddAuthenticationAsync(HttpClient httpClient, CallOptions options)
    {
        if (options.RequireAuthentication)
        {
            var token = await GetServiceToServiceTokenAsync();
            httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        }
    }
}

public class ServiceRegistry : IServiceRegistry
{
    private readonly IDistributedCache _cache;
    private readonly IServiceDiscoveryProvider _discoveryProvider;
    
    public async Task<ServiceInfo?> DiscoverServiceAsync(string serviceName)
    {
        var cacheKey = $"service:{serviceName}";
        
        // Try cache first
        var cachedService = await _cache.GetStringAsync(cacheKey);
        if (cachedService != null)
        {
            var service = JsonSerializer.Deserialize<ServiceInfo>(cachedService);
            if (service != null && IsServiceHealthy(service))
            {
                return service;
            }
        }
        
        // Discover from provider
        var discoveredService = await _discoveryProvider.DiscoverAsync(serviceName);
        if (discoveredService != null)
        {
            // Cache for future use
            await _cache.SetStringAsync(cacheKey, JsonSerializer.Serialize(discoveredService),
                new DistributedCacheEntryOptions
                {
                    AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(5)
                });
        }
        
        return discoveredService;
    }
    
    private bool IsServiceHealthy(ServiceInfo service)
    {
        return service.Instances.Any(i => i.Status == InstanceStatus.Healthy);
    }
}

// Service registration
public class ServiceRegistrationService : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        var serviceInfo = new ServiceRegistration
        {
            Name = "my-service",
            Version = "1.0.0",
            Address = GetServiceAddress(),
            Port = GetServicePort(),
            HealthCheckUrl = "/health",
            Tags = ["web", "api"],
            Metadata = new Dictionary<string, string>
            {
                ["environment"] = Environment.GetEnvironmentVariable("ENVIRONMENT") ?? "development",
                ["region"] = Environment.GetEnvironmentVariable("REGION") ?? "us-east-1"
            }
        };
        
        // Register service
        await RegisterService(serviceInfo);
        
        // Send periodic heartbeats
        while (!ct.IsCancellationRequested)
        {
            await SendHeartbeat(serviceInfo.Name);
            await Task.Delay(TimeSpan.FromSeconds(30), ct);
        }
    }
}
```

---

## 🔍 Advanced Monitoring and Observability

### 1. **Custom Metrics and Alerting**

Comprehensive application metrics:

```csharp
public class ApplicationMetrics
{
    private static readonly Counter<int> _requestCounter = 
        Meter.CreateCounter<int>("app.requests.total", "count", "Total number of requests");
    
    private static readonly Histogram<double> _requestDuration = 
        Meter.CreateHistogram<double>("app.requests.duration", "seconds", "Request duration");
    
    private static readonly Counter<int> _errorCounter = 
        Meter.CreateCounter<int>("app.errors.total", "count", "Total number of errors");
    
    private static readonly Gauge<long> _activeConnections = 
        Meter.CreateGauge<long>("app.connections.active", "count", "Active connections");
    
    private static readonly Counter<int> _businessEventCounter = 
        Meter.CreateCounter<int>("app.business.events.total", "count", "Business events");
    
    public static void RecordRequest(string endpoint, string method, int statusCode, double duration)
    {
        _requestCounter.Add(1, new TagList
        {
            ["endpoint"] = endpoint,
            ["method"] = method,
            ["status_code"] = statusCode.ToString()
        });
        
        _requestDuration.Record(duration, new TagList
        {
            ["endpoint"] = endpoint,
            ["method"] = method
        });
    }
    
    public static void RecordError(string errorType, string endpoint)
    {
        _errorCounter.Add(1, new TagList
        {
            ["error_type"] = errorType,
            ["endpoint"] = endpoint
        });
    }
    
    public static void RecordBusinessEvent(string eventType, string source)
    {
        _businessEventCounter.Add(1, new TagList
        {
            ["event_type"] = eventType,
            ["source"] = source
        });
    }
    
    public static void SetActiveConnections(long count)
    {
        _activeConnections.Record(count);
    }
}

public class MetricsMiddleware
{
    private readonly RequestDelegate _next;
    
    public async Task InvokeAsync(HttpContext context)
    {
        var stopwatch = Stopwatch.StartNew();
        
        try
        {
            await _next(context);
        }
        catch (Exception ex)
        {
            ApplicationMetrics.RecordError(ex.GetType().Name, context.Request.Path);
            throw;
        }
        finally
        {
            stopwatch.Stop();
            
            ApplicationMetrics.RecordRequest(
                context.Request.Path,
                context.Request.Method,
                context.Response.StatusCode,
                stopwatch.Elapsed.TotalSeconds
            );
        }
    }
}

public class BusinessMetricsService : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        // Track business metrics
        await this.On<OrderCreated>(async evt =>
        {
            ApplicationMetrics.RecordBusinessEvent("order_created", "e-commerce");
        });
        
        await this.On<PaymentProcessed>(async evt =>
        {
            ApplicationMetrics.RecordBusinessEvent("payment_processed", "payment-gateway");
        });
        
        // Periodic system metrics
        _ = Task.Run(async () =>
        {
            while (!ct.IsCancellationRequested)
            {
                var activeConnections = GetActiveConnectionCount();
                ApplicationMetrics.SetActiveConnections(activeConnections);
                
                await Task.Delay(TimeSpan.FromMinutes(1), ct);
            }
        }, ct);
    }
}
```

### 2. **Distributed Tracing**

Advanced tracing with custom spans:

```csharp
public class TracingService
{
    private static readonly ActivitySource _activitySource = new("MyApp");
    
    public async Task<T> TraceAsync<T>(
        string operationName, 
        Func<Task<T>> operation, 
        Dictionary<string, object>? tags = null)
    {
        using var activity = _activitySource.StartActivity(operationName);
        
        // Add standard tags
        activity?.SetTag("service.name", "my-service");
        activity?.SetTag("service.version", "1.0.0");
        
        // Add custom tags
        if (tags != null)
        {
            foreach (var tag in tags)
            {
                activity?.SetTag(tag.Key, tag.Value);
            }
        }
        
        try
        {
            var result = await operation();
            
            activity?.SetStatus(ActivityStatusCode.Ok);
            return result;
        }
        catch (Exception ex)
        {
            activity?.SetStatus(ActivityStatusCode.Error, ex.Message);
            activity?.SetTag("error.type", ex.GetType().Name);
            activity?.SetTag("error.message", ex.Message);
            throw;
        }
    }
    
    public void AddEvent(string name, Dictionary<string, object>? attributes = null)
    {
        var activity = Activity.Current;
        if (activity != null)
        {
            var tags = attributes?.Select(kv => new KeyValuePair<string, object?>(kv.Key, kv.Value));
            activity.AddEvent(new ActivityEvent(name, DateTimeOffset.UtcNow, new ActivityTagsCollection(tags)));
        }
    }
}

// Usage in services
public class OrderService
{
    private readonly TracingService _tracing;
    
    public async Task<Order> ProcessOrderAsync(CreateOrderRequest request)
    {
        return await _tracing.TraceAsync("process_order", async () =>
        {
            _tracing.AddEvent("order_validation_started");
            
            // Validate order
            await ValidateOrder(request);
            
            _tracing.AddEvent("order_validation_completed");
            _tracing.AddEvent("inventory_check_started");
            
            // Check inventory
            var inventoryResult = await CheckInventory(request.Items);
            
            _tracing.AddEvent("inventory_check_completed", new Dictionary<string, object>
            {
                ["items_available"] = inventoryResult.AllAvailable,
                ["unavailable_count"] = inventoryResult.UnavailableItems.Count
            });
            
            // Create order
            var order = new Order { /* ... */ };
            await order.SaveAsync();
            
            _tracing.AddEvent("order_created", new Dictionary<string, object>
            {
                ["order_id"] = order.Id,
                ["total_amount"] = order.Total
            });
            
            return order;
        }, new Dictionary<string, object>
        {
            ["customer_id"] = request.CustomerId,
            ["item_count"] = request.Items.Count
        });
    }
}
```

This comprehensive advanced topics guide covers the sophisticated patterns and techniques needed for building enterprise-grade applications with the Sora Framework. Each section provides production-ready code examples and architectural guidance for scaling Sora applications to meet complex requirements.
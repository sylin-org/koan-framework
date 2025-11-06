// Batch Operations and Performance Patterns for Koan Framework

using Koan.Data.Core;

// ============================================================================
// BULK PERSISTENCE PATTERNS
// ============================================================================

public class BatchOperationExamples
{
    // ========================================================================
    // BULK CREATE
    // ========================================================================

    /// <summary>
    /// Create thousands of entities efficiently.
    /// Framework uses provider-specific batch operations.
    /// </summary>
    public async Task BulkCreate(int count, CancellationToken ct = default)
    {
        var todos = Enumerable.Range(1, count)
            .Select(i => new Todo
            {
                Title = $"Task {i}",
                Priority = i % 5,
                Completed = i % 10 == 0
            })
            .ToList();

        // Single bulk operation - much faster than N individual saves
        await todos.Save();
    }

    // ========================================================================
    // BATCH OPERATIONS (Add/Update/Delete in One Transaction)
    // ========================================================================

    /// <summary>
    /// Combine multiple operations in a single batch.
    /// Useful for complex state changes.
    /// </summary>
    public async Task MixedBatch(CancellationToken ct = default)
    {
        await Todo.Batch()
            // Add new entities
            .Add(new Todo { Title = "New task 1" })
            .Add(new Todo { Title = "New task 2" })

            // Update existing entities
            .Update("existing-id-1", todo =>
            {
                todo.Completed = true;
                todo.Priority = 5;
            })
            .Update("existing-id-2", todo => todo.Title = "Updated title")

            // Delete entities
            .Delete("old-id-1")
            .Delete("old-id-2")

            // Execute all operations together
            .SaveAsync(ct);
    }

    /// <summary>
    /// Batch update with conditional logic.
    /// </summary>
    public async Task BatchUpdateWithConditions(
        List<string> todoIds,
        CancellationToken ct = default)
    {
        // Load all todos in one query
        var todos = await Todo.Get(todoIds, ct);

        // Build batch
        var batch = Todo.Batch();
        foreach (var todo in todos.Where(t => t is not null))
        {
            if (!todo!.Completed && todo.Priority < 3)
            {
                batch.Update(todo.Id, t =>
                {
                    t.Priority = 3;
                    t.DueDate = DateTimeOffset.UtcNow.AddDays(7);
                });
            }
        }

        await batch.SaveAsync(ct);
    }

    // ========================================================================
    // BULK REMOVAL STRATEGIES
    // ========================================================================

    /// <summary>
    /// Remove all entities with Optimized strategy.
    /// Uses TRUNCATE/DROP on capable providers (10-250x faster).
    /// WARNING: Bypasses lifecycle hooks on most providers!
    /// </summary>
    public async Task BulkRemoveOptimized(CancellationToken ct = default)
    {
        // Default strategy: Optimized
        // - Postgres/SQL Server/MongoDB: Uses TRUNCATE/DROP (fast, no hooks)
        // - JSON/InMemory: Uses safe deletion (fires hooks)
        var deletedCount = await Todo.RemoveAll(ct);

        Console.WriteLine($"Deleted {deletedCount} entities");
    }

    /// <summary>
    /// Remove all with Safe strategy.
    /// Always fires lifecycle hooks - slower but auditable.
    /// Use when audit trail is required.
    /// </summary>
    public async Task BulkRemoveSafe(CancellationToken ct = default)
    {
        // Explicit Safe strategy: always fires hooks
        var deletedCount = await Todo.RemoveAll(RemoveStrategy.Safe, ct);

        Console.WriteLine($"Safely deleted {deletedCount} entities with audit trail");
    }

    /// <summary>
    /// Remove all with Fast strategy.
    /// Explicitly bypass hooks for maximum performance.
    /// Use when you're certain hooks aren't needed.
    /// </summary>
    public async Task BulkRemoveFast(CancellationToken ct = default)
    {
        // Explicit Fast strategy: bypass hooks
        var deletedCount = await Todo.RemoveAll(RemoveStrategy.Fast, ct);

        Console.WriteLine($"Fast deleted {deletedCount} entities");
    }

    /// <summary>
    /// Check provider support for fast removal.
    /// </summary>
    public void CheckFastRemoveSupport()
    {
        if (Todo.SupportsFastRemove)
        {
            Console.WriteLine("Provider supports TRUNCATE/DROP - Optimized will use Fast");
        }
        else
        {
            Console.WriteLine("Provider lacks fast path - Optimized will use Safe");
        }
    }

    // ========================================================================
    // STREAMING FOR LARGE DATASETS
    // ========================================================================

    /// <summary>
    /// Process large dataset without loading all into memory.
    /// Critical for background jobs and ETL pipelines.
    /// </summary>
    public async Task StreamAndProcess(CancellationToken ct = default)
    {
        int processedCount = 0;

        // Stream entities in batches of 1000
        await foreach (var todo in Todo.AllStream(batchSize: 1000, ct))
        {
            // Process one entity at a time
            // Only 1000 entities in memory at once
            await ProcessTodo(todo);
            processedCount++;

            if (processedCount % 1000 == 0)
            {
                Console.WriteLine($"Processed {processedCount} entities");
            }
        }
    }

    /// <summary>
    /// Stream with filter query.
    /// </summary>
    public async Task StreamFiltered(CancellationToken ct = default)
    {
        await foreach (var todo in Todo.QueryStream(
            predicate: t => !t.Completed && t.Priority >= 3,
            batchSize: 500,
            ct))
        {
            await ProcessHighPriorityTodo(todo);
        }
    }

    /// <summary>
    /// Stream and batch update.
    /// Useful for data migrations or bulk transformations.
    /// </summary>
    public async Task StreamAndBatchUpdate(CancellationToken ct = default)
    {
        var batch = new List<Todo>();
        const int batchSize = 100;

        await foreach (var todo in Todo.AllStream(batchSize: 1000, ct))
        {
            // Transform entity
            todo.Title = todo.Title.Trim();
            batch.Add(todo);

            // Save in batches of 100
            if (batch.Count >= batchSize)
            {
                await batch.Save();
                batch.Clear();
            }
        }

        // Save remaining
        if (batch.Any())
        {
            await batch.Save();
        }
    }

    // ========================================================================
    // BATCH RETRIEVAL (Prevent N+1 Queries)
    // ========================================================================

    /// <summary>
    /// Load multiple entities by ID efficiently.
    /// Single query with IN clause vs N individual queries.
    /// </summary>
    public async Task BatchRetrieve(CancellationToken ct = default)
    {
        var ids = new[]
        {
            "id1", "id2", "id3", "id4", "id5",
            "id6", "id7", "id8", "id9", "id10"
        };

        // ✅ EFFICIENT: Single bulk query
        var todos = await Todo.Get(ids, ct);

        // Result preserves order and includes nulls for missing IDs
        // [Todo?, Todo?, null, Todo?, ...]

        // Filter out nulls if needed
        var validTodos = todos.Where(t => t is not null).ToList();

        Console.WriteLine($"Loaded {validTodos.Count} of {ids.Length} requested entities");
    }

    /// <summary>
    /// Load relationships efficiently (batch load to prevent N+1).
    /// </summary>
    public async Task BatchLoadRelationships(CancellationToken ct = default)
    {
        // Load todos
        var todos = await Todo.Query(t => t.Priority >= 3, ct);

        // Extract unique user IDs
        var userIds = todos.Select(t => t.UserId).Distinct().ToArray();

        // ✅ EFFICIENT: Batch load all users in ONE query
        var users = await User.Get(userIds, ct);
        var userDict = users
            .Where(u => u is not null)
            .Cast<User>()
            .ToDictionary(u => u.Id);

        // Match todos with users
        foreach (var todo in todos)
        {
            if (userDict.TryGetValue(todo.UserId, out var user))
            {
                Console.WriteLine($"Todo '{todo.Title}' belongs to {user.Name}");
            }
        }
    }

    // ========================================================================
    // PAGINATION (Web API Scenarios)
    // ========================================================================

    /// <summary>
    /// Paginate results for web APIs.
    /// </summary>
    public async Task<(List<Todo> Items, long TotalCount)> PaginateForApi(
        int pageNumber,
        int pageSize,
        CancellationToken ct = default)
    {
        var result = await Todo.QueryWithCount(
            predicate: t => !t.Completed,
            options: new DataQueryOptions
            {
                OrderBy = nameof(Todo.Priority),
                Descending = true
            },
            ct);

        // Return items and total for pagination UI
        return (result.Items, result.TotalCount);
    }

    /// <summary>
    /// First page helper.
    /// </summary>
    public async Task<List<Todo>> GetFirstPage(int pageSize, CancellationToken ct = default)
    {
        return await Todo.FirstPage(pageSize, ct);
    }

    // ========================================================================
    // HELPER METHODS
    // ========================================================================

    private Task ProcessTodo(Todo todo)
    {
        // Simulate processing
        return Task.CompletedTask;
    }

    private Task ProcessHighPriorityTodo(Todo todo)
    {
        // Simulate processing
        return Task.CompletedTask;
    }
}

// ============================================================================
// PERFORMANCE CHARACTERISTICS
// ============================================================================

/// <summary>
/// Performance comparison for different batch strategies.
///
/// Bulk Removal (1M records):
/// - Safe (DELETE):         ~45 seconds   (fires hooks, maintains audit)
/// - Fast (TRUNCATE):       ~200ms        (225x faster, bypasses hooks)
/// - Optimized (Provider):  Auto-selects based on provider capabilities
///
/// Count Operations (10M records):
/// - Exact:                 ~25 seconds   (full table scan)
/// - Fast:                  ~5ms          (5000x faster, metadata estimate)
/// - Optimized:             Auto-selects based on scenario
///
/// Batch Retrieval:
/// - Individual Gets:       N queries     (proportional to count)
/// - Batch Get:            1 query        (constant time, 10-100x faster)
///
/// Streaming:
/// - Memory footprint:      Batch size only (vs full dataset)
/// - Use case:             Datasets > 10k records
/// </summary>
public class PerformanceNotes { }

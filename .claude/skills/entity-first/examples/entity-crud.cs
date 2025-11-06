// Complete Entity<T> CRUD Patterns for Koan Framework

using Koan.Data.Core;

// ============================================================================
// BASIC ENTITY DEFINITION
// ============================================================================

/// <summary>
/// Basic entity with auto GUID v7 generation.
/// Id is automatically assigned on first access - no manual management needed.
/// </summary>
public class Todo : Entity<Todo>
{
    public string Title { get; set; } = "";
    public bool Completed { get; set; }
    public int Priority { get; set; }
    public DateTimeOffset? DueDate { get; set; }

    // Id inherited from Entity<Todo> - auto-generated as GUID v7
    // Created and Updated timestamps inherited automatically
}

// ============================================================================
// CREATE OPERATIONS
// ============================================================================

public class TodoCrudExamples
{
    // Create single entity
    public async Task<Todo> CreateSingle()
    {
        var todo = new Todo
        {
            Title = "Buy groceries",
            Priority = 2,
            DueDate = DateTimeOffset.UtcNow.AddDays(1)
        };

        // Save generates ID automatically and persists
        return await todo.Save();
    }

    // Create multiple entities efficiently
    public async Task CreateMultiple()
    {
        var todos = new List<Todo>
        {
            new() { Title = "Task 1", Priority = 1 },
            new() { Title = "Task 2", Priority = 2 },
            new() { Title = "Task 3", Priority = 3 }
        };

        // Bulk save - framework uses provider-specific batch operations
        await todos.Save();
    }

    // Create with validation
    public async Task<Todo> CreateWithValidation(string title)
    {
        if (string.IsNullOrWhiteSpace(title))
            throw new InvalidOperationException("Title is required");

        if (title.Length > 200)
            throw new InvalidOperationException("Title too long (max 200 chars)");

        var todo = new Todo { Title = title };
        return await todo.Save();
    }

    // ========================================================================
    // READ OPERATIONS
    // ========================================================================

    // Get by ID
    public async Task<Todo?> GetById(string id, CancellationToken ct = default)
    {
        return await Todo.Get(id, ct);
    }

    // Get all
    public async Task<List<Todo>> GetAll(CancellationToken ct = default)
    {
        return await Todo.All(ct);
    }

    // Query with filter
    public async Task<List<Todo>> GetCompleted(CancellationToken ct = default)
    {
        return await Todo.Query(t => t.Completed, ct);
    }

    // Complex LINQ query
    public async Task<List<Todo>> GetHighPriorityOverdue(CancellationToken ct = default)
    {
        return await Todo.Query(t =>
            t.Priority >= 3 &&
            t.DueDate.HasValue &&
            t.DueDate.Value < DateTimeOffset.UtcNow &&
            !t.Completed,
            ct);
    }

    // Batch retrieval (prevents N+1 queries)
    public async Task<List<Todo?>> GetMany(string[] ids, CancellationToken ct = default)
    {
        // Single query fetches all IDs at once
        // Returns in same order as input, null for missing IDs
        return await Todo.Get(ids, ct);
    }

    // Pagination
    public async Task<List<Todo>> GetPage(int pageNumber, int pageSize, CancellationToken ct = default)
    {
        return await Todo.Page(pageNumber, pageSize, ct);
    }

    // Pagination with total count
    public async Task<(List<Todo> Items, long TotalCount)> GetPageWithCount(
        int pageNumber,
        int pageSize,
        CancellationToken ct = default)
    {
        var result = await Todo.QueryWithCount(
            predicate: t => !t.Completed,
            options: new DataQueryOptions
            {
                OrderBy = nameof(Todo.DueDate),
                Descending = false
            },
            ct);

        return (result.Items, result.TotalCount);
    }

    // Streaming (memory-efficient for large datasets)
    public async Task StreamAll(CancellationToken ct = default)
    {
        await foreach (var todo in Todo.AllStream(batchSize: 1000, ct))
        {
            // Process one at a time, fetched in batches
            Console.WriteLine($"Processing: {todo.Title}");
        }
    }

    // ========================================================================
    // UPDATE OPERATIONS
    // ========================================================================

    // Update single property
    public async Task<Todo> MarkCompleted(string id, CancellationToken ct = default)
    {
        var todo = await Todo.Get(id, ct);
        if (todo is null)
            throw new InvalidOperationException($"Todo {id} not found");

        todo.Completed = true;
        return await todo.Save();
    }

    // Update multiple properties
    public async Task<Todo> UpdateTodo(
        string id,
        string newTitle,
        int newPriority,
        CancellationToken ct = default)
    {
        var todo = await Todo.Get(id, ct);
        if (todo is null)
            throw new InvalidOperationException($"Todo {id} not found");

        todo.Title = newTitle;
        todo.Priority = newPriority;
        return await todo.Save();
    }

    // Batch update with operations
    public async Task BatchUpdate(CancellationToken ct = default)
    {
        await Todo.Batch()
            .Update("id1", t => t.Completed = true)
            .Update("id2", t => { t.Title = "Updated"; t.Priority = 5; })
            .Update("id3", t => t.DueDate = DateTimeOffset.UtcNow.AddDays(7))
            .SaveAsync(ct);
    }

    // ========================================================================
    // DELETE OPERATIONS
    // ========================================================================

    // Delete single entity
    public async Task DeleteById(string id, CancellationToken ct = default)
    {
        var todo = await Todo.Get(id, ct);
        if (todo is not null)
        {
            await todo.Remove();
        }
    }

    // Delete all (use with caution!)
    public async Task DeleteAll(CancellationToken ct = default)
    {
        // Default uses Optimized strategy (fast on capable providers)
        await Todo.RemoveAll(ct);
    }

    // Delete all with explicit strategy
    public async Task DeleteAllSafe(CancellationToken ct = default)
    {
        // Safe strategy always fires lifecycle hooks (slower but auditable)
        await Todo.RemoveAll(RemoveStrategy.Safe, ct);
    }

    // Batch delete
    public async Task BatchDelete(string[] idsToDelete, CancellationToken ct = default)
    {
        var batch = Todo.Batch();
        foreach (var id in idsToDelete)
        {
            batch.Delete(id);
        }
        await batch.SaveAsync(ct);
    }

    // ========================================================================
    // COUNT OPERATIONS
    // ========================================================================

    // Get count (optimized - uses metadata if available)
    public async Task<long> CountAll(CancellationToken ct = default)
    {
        return await Todo.Count;
    }

    // Exact count (guaranteed accuracy)
    public async Task<long> CountExact(CancellationToken ct = default)
    {
        return await Todo.Count.Exact(ct);
    }

    // Fast count (metadata estimate - 1000x+ faster)
    public async Task<long> CountFast(CancellationToken ct = default)
    {
        return await Todo.Count.Fast(ct);
    }

    // Filtered count
    public async Task<long> CountCompleted(CancellationToken ct = default)
    {
        return await Todo.Count.Where(t => t.Completed);
    }

    // ========================================================================
    // QUERY HELPERS (CUSTOM STATIC METHODS)
    // ========================================================================

    // Add domain-specific query methods directly on entity
}

// Extend entity with domain-specific queries
public partial class Todo
{
    /// <summary>
    /// Get todos created in the last N days.
    /// </summary>
    public static Task<List<Todo>> Recent(int days = 7, CancellationToken ct = default)
    {
        var cutoff = DateTimeOffset.UtcNow.AddDays(-days);
        return Query(t => t.Created > cutoff, ct);
    }

    /// <summary>
    /// Get overdue incomplete todos.
    /// </summary>
    public static Task<List<Todo>> Overdue(CancellationToken ct = default)
    {
        var now = DateTimeOffset.UtcNow;
        return Query(t =>
            !t.Completed &&
            t.DueDate.HasValue &&
            t.DueDate.Value < now,
            ct);
    }

    /// <summary>
    /// Get todos by priority range.
    /// </summary>
    public static Task<List<Todo>> ByPriority(int minPriority, int maxPriority, CancellationToken ct = default)
    {
        return Query(t => t.Priority >= minPriority && t.Priority <= maxPriority, ct);
    }
}

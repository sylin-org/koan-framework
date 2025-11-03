// Entity Relationship Navigation Patterns for Koan Framework

using Koan.Data.Core;

// ============================================================================
// ENTITY DEFINITIONS WITH RELATIONSHIPS
// ============================================================================

/// <summary>
/// User entity - one side of one-to-many relationship with Todo.
/// </summary>
public class User : Entity<User>
{
    public string Name { get; set; } = "";
    public string Email { get; set; } = "";

    // Navigation helper: Get all todos for this user
    public Task<List<Todo>> GetTodos(CancellationToken ct = default)
    {
        return Todo.Query(t => t.UserId == Id, ct);
    }

    // Navigation helper: Get recent todos
    public Task<List<Todo>> GetRecentTodos(int days = 7, CancellationToken ct = default)
    {
        var cutoff = DateTimeOffset.UtcNow.AddDays(-days);
        return Todo.Query(t => t.UserId == Id && t.Created > cutoff, ct);
    }
}

/// <summary>
/// Category entity - used for organizing todos.
/// </summary>
public class Category : Entity<Category>
{
    public string Name { get; set; } = "";
    public string? Description { get; set; }

    // Navigation helper: Get all todos in this category
    public Task<List<Todo>> GetTodos(CancellationToken ct = default)
    {
        return Todo.Query(t => t.CategoryId == Id, ct);
    }

    // Navigation helper: Get todo count
    public Task<long> GetTodoCount(CancellationToken ct = default)
    {
        return Todo.Count.Where(t => t.CategoryId == Id);
    }
}

/// <summary>
/// Todo entity with relationships to User and Category.
/// Many side of relationships.
/// </summary>
public class Todo : Entity<Todo>
{
    public string Title { get; set; } = "";
    public bool Completed { get; set; }

    // Foreign keys
    public string UserId { get; set; } = "";
    public string? CategoryId { get; set; }

    // Navigation helpers
    public Task<User?> GetUser(CancellationToken ct = default)
    {
        return User.Get(UserId, ct);
    }

    public Task<Category?> GetCategory(CancellationToken ct = default)
    {
        return string.IsNullOrEmpty(CategoryId)
            ? Task.FromResult<Category?>(null)
            : Category.Get(CategoryId, ct);
    }

    // One-to-many child relationship
    public Task<List<TodoItem>> GetItems(CancellationToken ct = default)
    {
        return TodoItem.Query(i => i.TodoId == Id, ct);
    }

    // Static query helper
    public static Task<List<Todo>> ForUser(string userId, CancellationToken ct = default)
    {
        return Query(t => t.UserId == userId, ct);
    }
}

/// <summary>
/// TodoItem entity - child items within a todo (one-to-many).
/// </summary>
public class TodoItem : Entity<TodoItem>
{
    public string TodoId { get; set; } = ""; // Parent foreign key
    public string Description { get; set; } = "";
    public bool IsComplete { get; set; }
    public int SortOrder { get; set; }

    // Navigation to parent
    public Task<Todo?> GetParentTodo(CancellationToken ct = default)
    {
        return Todo.Get(TodoId, ct);
    }

    // Static query helper
    public static Task<List<TodoItem>> ForTodo(string todoId, CancellationToken ct = default)
    {
        return Query(i => i.TodoId == todoId, ct);
    }
}

// ============================================================================
// RELATIONSHIP NAVIGATION EXAMPLES
// ============================================================================

public class RelationshipExamples
{
    // ========================================================================
    // ONE-TO-MANY: User -> Todos
    // ========================================================================

    /// <summary>
    /// Load user and all their todos.
    /// </summary>
    public async Task<(User User, List<Todo> Todos)> LoadUserWithTodos(
        string userId,
        CancellationToken ct = default)
    {
        // Load user
        var user = await User.Get(userId, ct);
        if (user is null)
            throw new InvalidOperationException($"User {userId} not found");

        // Load todos using navigation helper
        var todos = await user.GetTodos(ct);

        return (user, todos);
    }

    // ========================================================================
    // MANY-TO-ONE: Todo -> User (lookup parent)
    // ========================================================================

    /// <summary>
    /// Load todo and its owner.
    /// </summary>
    public async Task<(Todo Todo, User Owner)> LoadTodoWithOwner(
        string todoId,
        CancellationToken ct = default)
    {
        // Load todo
        var todo = await Todo.Get(todoId, ct);
        if (todo is null)
            throw new InvalidOperationException($"Todo {todoId} not found");

        // Load owner using navigation helper
        var owner = await todo.GetUser(ct);
        if (owner is null)
            throw new InvalidOperationException($"Owner {todo.UserId} not found");

        return (todo, owner);
    }

    // ========================================================================
    // BATCH LOADING (Prevents N+1 Queries)
    // ========================================================================

    /// <summary>
    /// Load multiple todos and their users efficiently.
    /// BAD: Would cause N+1 query problem if done individually.
    /// GOOD: Batch load all users in single query.
    /// </summary>
    public async Task<List<(Todo Todo, User? Owner)>> LoadTodosWithOwners(
        List<string> todoIds,
        CancellationToken ct = default)
    {
        // Step 1: Batch load all todos
        var todos = await Todo.Get(todoIds, ct);
        var validTodos = todos.Where(t => t is not null).Cast<Todo>().ToList();

        // Step 2: Extract unique user IDs
        var userIds = validTodos.Select(t => t.UserId).Distinct().ToArray();

        // Step 3: Batch load all users in ONE query (not N queries!)
        var users = await User.Get(userIds, ct);
        var userDict = users
            .Where(u => u is not null)
            .Cast<User>()
            .ToDictionary(u => u.Id);

        // Step 4: Match todos with their owners
        return validTodos.Select(todo =>
        {
            userDict.TryGetValue(todo.UserId, out var owner);
            return (todo, owner);
        }).ToList();
    }

    // ========================================================================
    // HIERARCHICAL RELATIONSHIPS: Todo -> TodoItems
    // ========================================================================

    /// <summary>
    /// Load todo with all its child items.
    /// </summary>
    public async Task<(Todo Todo, List<TodoItem> Items)> LoadTodoWithItems(
        string todoId,
        CancellationToken ct = default)
    {
        var todo = await Todo.Get(todoId, ct);
        if (todo is null)
            throw new InvalidOperationException($"Todo {todoId} not found");

        // Load child items using navigation helper
        var items = await todo.GetItems(ct);

        return (todo, items);
    }

    /// <summary>
    /// Load multiple todos with all their items efficiently.
    /// </summary>
    public async Task<Dictionary<Todo, List<TodoItem>>> LoadTodosWithAllItems(
        List<string> todoIds,
        CancellationToken ct = default)
    {
        // Step 1: Batch load todos
        var todos = await Todo.Get(todoIds, ct);
        var validTodos = todos.Where(t => t is not null).Cast<Todo>().ToList();

        // Step 2: Load ALL items for ALL todos in ONE query
        var allItems = await TodoItem.Query(
            i => todoIds.Contains(i.TodoId),
            ct);

        // Step 3: Group items by parent todo
        var itemsByTodo = allItems.GroupBy(i => i.TodoId).ToDictionary(
            g => g.Key,
            g => g.OrderBy(i => i.SortOrder).ToList());

        // Step 4: Build result dictionary
        return validTodos.ToDictionary(
            todo => todo,
            todo => itemsByTodo.TryGetValue(todo.Id, out var items)
                ? items
                : new List<TodoItem>());
    }

    // ========================================================================
    // OPTIONAL RELATIONSHIPS
    // ========================================================================

    /// <summary>
    /// Load todo with optional category.
    /// </summary>
    public async Task<(Todo Todo, Category? Category)> LoadTodoWithOptionalCategory(
        string todoId,
        CancellationToken ct = default)
    {
        var todo = await Todo.Get(todoId, ct);
        if (todo is null)
            throw new InvalidOperationException($"Todo {todoId} not found");

        // Category is optional - may be null
        var category = await todo.GetCategory(ct);

        return (todo, category);
    }

    // ========================================================================
    // AGGREGATE QUERIES ACROSS RELATIONSHIPS
    // ========================================================================

    /// <summary>
    /// Get statistics for a user's todos.
    /// </summary>
    public async Task<UserTodoStats> GetUserStats(string userId, CancellationToken ct = default)
    {
        // Load all user's todos
        var todos = await Todo.ForUser(userId, ct);

        return new UserTodoStats
        {
            Total = todos.Count,
            Completed = todos.Count(t => t.Completed),
            InProgress = todos.Count(t => !t.Completed),
            Overdue = todos.Count(t =>
                !t.Completed &&
                t.DueDate.HasValue &&
                t.DueDate.Value < DateTimeOffset.UtcNow)
        };
    }

    /// <summary>
    /// Get todos grouped by category.
    /// </summary>
    public async Task<Dictionary<string, List<Todo>>> GetTodosByCategory(
        string userId,
        CancellationToken ct = default)
    {
        var todos = await Todo.ForUser(userId, ct);

        return todos
            .GroupBy(t => t.CategoryId ?? "Uncategorized")
            .ToDictionary(g => g.Key, g => g.ToList());
    }
}

// ============================================================================
// SUPPORTING TYPES
// ============================================================================

public record UserTodoStats
{
    public int Total { get; init; }
    public int Completed { get; init; }
    public int InProgress { get; init; }
    public int Overdue { get; init; }
}

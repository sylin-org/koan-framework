# Entity-First Anti-Patterns: Manual Repositories

This document catalogs **incorrect patterns** that violate Koan Framework's Entity-First principle. These patterns break provider transparency, duplicate framework functionality, and increase maintenance burden.

## ❌ Anti-Pattern 1: Manual Repository Interface

### What NOT to Do

```csharp
// ❌ WRONG: Creating repository interface
public interface ITodoRepository
{
    Task<Todo> GetAsync(string id);
    Task<Todo> SaveAsync(Todo todo);
    Task<List<Todo>> GetAllAsync();
    Task DeleteAsync(string id);
}

// ❌ WRONG: Implementing repository
public class TodoRepository : ITodoRepository
{
    private readonly IDataContext _context;

    public TodoRepository(IDataContext context)
    {
        _context = context;
    }

    public async Task<Todo> GetAsync(string id)
    {
        return await _context.Get<Todo>(id);
    }

    public async Task<Todo> SaveAsync(Todo todo)
    {
        return await _context.Save(todo);
    }

    // ... more boilerplate
}
```

### Why It's Wrong

1. **Duplicates Framework Functionality**: Entity<T> already provides all these methods
2. **Breaks Provider Transparency**: Custom repository locks you to specific data access patterns
3. **Increases Boilerplate**: Requires interface + implementation + registration
4. **Complicates Testing**: Now need to mock repositories instead of using framework test helpers
5. **Violates "Reference = Intent"**: Manual registration required instead of auto-discovery

### ✅ Correct Approach

```csharp
// ✅ CORRECT: Entity<T> provides all repository functionality
public class Todo : Entity<Todo>
{
    public string Title { get; set; } = "";
    public bool Completed { get; set; }
}

// Direct usage - no repository needed
var todo = await Todo.Get(id);
await todo.Save();
var all = await Todo.All();
await todo.Remove();
```

---

## ❌ Anti-Pattern 2: Service Layer with Repository Injection

### What NOT to Do

```csharp
// ❌ WRONG: Injecting repository into service
public class TodoService
{
    private readonly ITodoRepository _repository;

    public TodoService(ITodoRepository repository)
    {
        _repository = repository;
    }

    public async Task<Todo> GetTodoAsync(string id)
    {
        return await _repository.GetAsync(id); // Unnecessary indirection
    }

    public async Task<Todo> CreateTodoAsync(string title)
    {
        var todo = new Todo { Title = title };
        return await _repository.SaveAsync(todo);
    }
}

// ❌ WRONG: Manual registration in Program.cs
builder.Services.AddScoped<ITodoRepository, TodoRepository>();
builder.Services.AddScoped<ITodoService, TodoService>();
```

### Why It's Wrong

1. **Unnecessary Layer**: Service just forwards calls to repository
2. **Manual Registration**: Breaks auto-registration pattern
3. **Tight Coupling**: Service depends on repository interface
4. **Testing Complexity**: Must mock repository even for simple operations
5. **Provider Lock-In**: Can't easily switch data providers

### ✅ Correct Approach

```csharp
// ✅ CORRECT: Service uses Entity<T> directly
public class TodoService
{
    public async Task<Todo> GetTodoAsync(string id)
    {
        return await Todo.Get(id); // Direct entity usage
    }

    public async Task<Todo> CreateTodoAsync(string title)
    {
        var todo = new Todo { Title = title };
        return await todo.Save(); // Instance method
    }

    public async Task<List<Todo>> GetCompletedAsync()
    {
        return await Todo.Query(t => t.Completed); // Static query
    }
}

// ✅ Registration via KoanAutoRegistrar
public class KoanAutoRegistrar : IKoanAutoRegistrar
{
    public void Initialize(IServiceCollection services)
    {
        services.AddScoped<TodoService>(); // Only register service
    }
}
```

---

## ❌ Anti-Pattern 3: Generic Repository Pattern

### What NOT to Do

```csharp
// ❌ WRONG: Generic repository trying to abstract Entity<T>
public interface IRepository<T> where T : class
{
    Task<T?> GetByIdAsync(string id);
    Task<List<T>> GetAllAsync();
    Task<T> AddAsync(T entity);
    Task<T> UpdateAsync(T entity);
    Task DeleteAsync(string id);
}

// ❌ WRONG: Implementation wrapping Entity<T>
public class Repository<T> : IRepository<T> where T : Entity<T>
{
    public async Task<T?> GetByIdAsync(string id)
    {
        return await Entity<T>.Get(id); // Just forwarding!
    }

    public async Task<List<T>> GetAllAsync()
    {
        return await Entity<T>.All(); // Unnecessary wrapper
    }

    // ... more forwarding methods
}
```

### Why It's Wrong

1. **Pure Indirection**: Every method just forwards to Entity<T>
2. **No Value Added**: Doesn't provide any abstraction benefit
3. **Obscures Framework**: Hides powerful Entity<T> features
4. **Breaks IntelliSense**: Developer can't discover entity methods
5. **Maintenance Burden**: Must update wrapper when Entity<T> adds features

### ✅ Correct Approach

```csharp
// ✅ CORRECT: Use Entity<T> directly - it already IS a repository
public class Todo : Entity<Todo>
{
    public string Title { get; set; } = "";
}

// No wrapper needed - full feature access
var todo = await Todo.Get(id);
var all = await Todo.All();
var filtered = await Todo.Query(t => t.Completed);
var paged = await Todo.Page(1, 20);
var withCount = await Todo.QueryWithCount(t => t.Priority > 3);
await foreach (var item in Todo.AllStream(batchSize: 1000)) { }
```

---

## ❌ Anti-Pattern 4: DbContext/ORM Directly in Services

### What NOT to Do

```csharp
// ❌ WRONG: Using EF Core DbContext directly
public class TodoService
{
    private readonly ApplicationDbContext _context;

    public TodoService(ApplicationDbContext context)
    {
        _context = context;
    }

    public async Task<Todo> GetTodoAsync(string id)
    {
        return await _context.Todos.FindAsync(id); // Provider-specific
    }

    public async Task<List<Todo>> GetAllAsync()
    {
        return await _context.Todos.ToListAsync(); // Breaks provider transparency
    }
}
```

### Why It's Wrong

1. **Provider Lock-In**: Tied to specific ORM (Entity Framework)
2. **Breaks Multi-Provider**: Can't switch to MongoDB, Redis, etc.
3. **Manual Configuration**: Requires DbContext registration and mapping
4. **No Framework Features**: Loses capability detection, context routing, streaming
5. **Violates Koan Principles**: Goes against entire framework architecture

### ✅ Correct Approach

```csharp
// ✅ CORRECT: Provider-agnostic Entity<T> usage
public class TodoService
{
    public async Task<Todo> GetTodoAsync(string id)
    {
        return await Todo.Get(id); // Works with ANY provider
    }

    public async Task<List<Todo>> GetAllAsync()
    {
        return await Todo.All(); // Provider-transparent
    }
}

// Configuration happens via appsettings.json - no code changes to switch providers
```

---

## ❌ Anti-Pattern 5: Repository Static Wrappers

### What NOT to Do

```csharp
// ❌ WRONG: Static class wrapping Entity<T> methods
public static class TodoRepository
{
    public static Task<Todo?> GetAsync(string id, CancellationToken ct = default)
    {
        return Todo.Get(id, ct); // Pointless forwarding
    }

    public static Task<List<Todo>> GetAllAsync(CancellationToken ct = default)
    {
        return Todo.All(ct); // Just adds noise
    }

    public static Task<List<Todo>> GetCompletedAsync(CancellationToken ct = default)
    {
        return Todo.Query(t => t.Completed, ct); // Obscures LINQ capability
    }
}
```

### Why It's Wrong

1. **No Benefit**: Adds zero value over direct Entity<T> usage
2. **Name Confusion**: "Repository" suggests pattern that doesn't exist
3. **Discovery Problem**: Developers look for TodoRepository instead of Todo
4. **Maintenance**: Another file to maintain for no reason
5. **Team Confusion**: Mixes patterns, unclear why wrapper exists

### ✅ Correct Approach

```csharp
// ✅ CORRECT: Use Entity<T> directly or extend it with domain methods
public partial class Todo : Entity<Todo>
{
    public string Title { get; set; } = "";
    public bool Completed { get; set; }

    // Add domain-specific queries directly on entity
    public static Task<List<Todo>> GetCompletedAsync(CancellationToken ct = default)
    {
        return Query(t => t.Completed, ct);
    }

    public static Task<List<Todo>> RecentAsync(int days = 7, CancellationToken ct = default)
    {
        var cutoff = DateTimeOffset.UtcNow.AddDays(-days);
        return Query(t => t.Created > cutoff, ct);
    }
}

// Direct usage
var completed = await Todo.GetCompletedAsync();
var recent = await Todo.RecentAsync(14);
```

---

## Summary: Why Entity-First Matters

### Framework Benefits Lost with Manual Repositories

| Feature | Entity<T> | Manual Repository |
|---------|-----------|-------------------|
| **Auto GUID v7 Generation** | ✅ Built-in | ❌ Manual implementation |
| **Provider Transparency** | ✅ Works with any adapter | ❌ Tied to specific provider |
| **Batch Operations** | ✅ `.Save()`, `.Batch()` | ❌ Must implement separately |
| **Streaming** | ✅ `.AllStream()`, `.QueryStream()` | ❌ Must implement separately |
| **Count Strategies** | ✅ Fast/Exact/Optimized | ❌ Manual queries only |
| **Context Routing** | ✅ Partition/Source/Adapter | ❌ Not available |
| **Capability Detection** | ✅ Auto-fallback | ❌ Manual checks needed |
| **Lifecycle Hooks** | ✅ Built-in | ❌ Must build separately |
| **Auto-Registration** | ✅ Via KoanAutoRegistrar | ❌ Manual DI registration |
| **Testing** | ✅ Framework test helpers | ❌ Mock repositories |

### The Correct Mental Model

```
❌ WRONG: Controller → Service → Repository → Entity<T> → Provider
                 (Too many layers, repository is unnecessary)

✅ CORRECT: Controller → Service → Entity<T> → Provider
                 (Lean, direct, provider-transparent)
```

### When to Use Services

Services are still valuable for:
- **Business Logic**: Complex workflows, validation, state transitions
- **Orchestration**: Coordinating multiple entities
- **Cross-Cutting Concerns**: Logging, caching, events

But services should **use Entity<T> directly**, not inject repositories!

```csharp
// ✅ CORRECT: Service provides business value, uses Entity<T>
public class TodoOrchestrationService
{
    private readonly ILogger<TodoOrchestrationService> _logger;

    public TodoOrchestrationService(ILogger<TodoOrchestrationService> logger)
    {
        _logger = logger;
    }

    public async Task<Todo> CompleteTodoAndNotify(string id)
    {
        // Load entity
        var todo = await Todo.Get(id);
        if (todo is null) throw new NotFoundException();

        // Business logic
        todo.Completed = true;
        todo.CompletedAt = DateTimeOffset.UtcNow;

        // Persist
        var saved = await todo.Save();

        // Side effects
        _logger.LogInformation("Todo {Id} completed", id);
        await NotificationService.SendCompletion(todo);

        return saved;
    }
}
```

---

## Migration Strategy

### If You Have Manual Repositories

1. **Identify Repository Methods**: List all methods in repository interface
2. **Map to Entity<T>**: Most map 1:1 to Entity<T> methods
3. **Update Services**: Replace repository calls with Entity<T> calls
4. **Remove Repository Files**: Delete interfaces and implementations
5. **Clean DI Registration**: Remove from Program.cs or auto-registrar
6. **Update Tests**: Use Entity<T> directly instead of mocking repositories

### Example Migration

```csharp
// BEFORE: With repository
public class TodoService
{
    private readonly ITodoRepository _repo;

    public TodoService(ITodoRepository repo) => _repo = repo;

    public async Task<Todo> GetAsync(string id) =>
        await _repo.GetAsync(id);
}

// AFTER: Entity-First
public class TodoService
{
    public async Task<Todo> GetAsync(string id) =>
        await Todo.Get(id);
}
```

---

**Remember:** Entity<T> IS the repository. Additional repository layers are anti-patterns that reduce framework benefits while adding complexity.

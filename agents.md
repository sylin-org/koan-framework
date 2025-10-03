# Koan Framework Agent Guidelines

## Core Principles

**AI Agents working on Koan Framework codebases MUST follow these patterns to maintain code quality and architectural consistency.**

---

## 1. Entity-First Development (MANDATORY)

### ✅ **ALWAYS USE Entity<T> Patterns**

```csharp
// ✅ CORRECT: Use Entity<T> with auto GUID v7
public class Todo : Entity<Todo>
{
    public string Title { get; set; } = "";
    public bool Completed { get; set; }
    // Id automatically generated as GUID v7 on first access
}

// ✅ For custom keys: Entity<T,K>
public class NumericEntity : Entity<NumericEntity, int>
{
    // Manual key management for specific scenarios
}

// Usage - ALWAYS use static entity methods
var todo = new Todo { Title = "Buy milk" }; // ID auto-generated
await todo.Save(); // Instance method for saving

var allTodos = await Todo.All(); // Static method for querying
var todoById = await Todo.Get(id);
var filtered = await Todo.Where(i => i.Completed == true);
```

### ❌ **NEVER Create Manual Repository Pattern**

```csharp
// ❌ WRONG: Don't create manual repository interfaces
public interface ITodoRepository
{
    Task<Todo> GetAsync(string id);
    Task SaveAsync(Todo todo);
}

// ❌ WRONG: Don't inject repositories when Entity<T> exists
public class TodoService
{
    private readonly ITodoRepository _repo; // DON'T DO THIS
    public TodoService(ITodoRepository repo) => _repo = repo;
}
```

### ✅ **Entity Service Pattern**

```csharp
// ✅ CORRECT: Business logic services use Entity<T> directly
public class TodoService
{
    public async Task<Todo> CompleteAsync(string id)
    {
        var todo = await Todo.Get(id); // Direct entity usage
        if (todo is null) throw new InvalidOperationException("Todo not found");

        todo.Completed = true;
        return await todo.Save(); // Instance save method
    }
}
```

---

## 2. Bootstrap and Auto-Registration (CRITICAL)

### ✅ **Ultra-Simple Program.cs Bootstrap**

```csharp
// ✅ CORRECT: Minimal Program.cs - framework handles everything
using Koan.Data.Core;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddKoan(); // ONE LINE - framework handles all dependencies

var app = builder.Build();
app.Run();
```

### ❌ **NEVER Add Manual Service Registration**

```csharp
// ❌ WRONG: Manual service registration breaks framework patterns
builder.Services.AddScoped<ITodoRepository, TodoRepository>();
builder.Services.AddDbContext<MyContext>();
builder.Services.AddScoped<ITodoService, TodoService>();
```

### ✅ **Use KoanAutoRegistrar for App-Specific Services**

```csharp
// ✅ CORRECT: Create app-specific auto-registrar
// File: Initialization/KoanAutoRegistrar.cs
using Koan.Core;
using Microsoft.Extensions.DependencyInjection;

public sealed class KoanAutoRegistrar : IKoanAutoRegistrar
{
    public string ModuleName => "MyApp";
    public string? ModuleVersion => typeof(KoanAutoRegistrar).Assembly.GetName().Version?.ToString();

    public void Initialize(IServiceCollection services)
    {
        // Register app-specific services here
        services.AddScoped<ITodoService, TodoService>();
        services.AddSingleton<IEmailService, EmailService>();
        services.AddHostedService<BackgroundProcessor>();
    }

    public void Describe(BootReport report, IConfiguration cfg, IHostEnvironment env)
    {
        report.AddModule(ModuleName, ModuleVersion);
        report.AddNote("Todo management services registered");
    }
}
```

---

## 3. Controller Patterns

### ✅ **Use EntityController<T> for CRUD Operations**

```csharp
// ✅ CORRECT: Inherit from EntityController for automatic CRUD API
[Route("api/[controller]")]
public class TodosController : EntityController<Todo>
{
    // Full CRUD API auto-generated: GET, POST, PUT, DELETE, PATCH
    // Customize only what you need to override
}
```

### ❌ **Don't Create Manual CRUD Controllers**

```csharp
// ❌ WRONG: Manual CRUD implementation
[ApiController]
public class TodosController : ControllerBase
{
    private readonly ITodoService _service; // Unnecessary when EntityController exists

    [HttpGet]
    public async Task<IActionResult> GetAll() => Ok(await _service.GetAllAsync());

    [HttpPost]
    public async Task<IActionResult> Create(Todo todo) => Ok(await _service.CreateAsync(todo));
}
```

---

## 4. Data Query Patterns

### ✅ **Use Entity<T> Static Methods**

```csharp
// ✅ CORRECT: Direct entity querying
var allTodos = await Todo.All();
var completed = await Todo.Query("Completed == true");
var paged = await Todo.Page(1, 10);
var withCount = await Todo.AllWithCount();

// ✅ Streaming for large datasets
await foreach (var todo in Todo.AllStream(batchSize: 1000))
{
    // Process in batches
}
```

### ❌ **Don't Create Repository Wrappers**

```csharp
// ❌ WRONG: Unnecessary repository layer over Entity<T>
public static class TodoRepository
{
    public static Task<Todo?> GetAsync(Guid id, CancellationToken ct)
        => Todo.Get(id.ToString(), ct); // Just forwards to Entity<T>
}

// Use Todo.Get() directly instead!
```

---

## 5. Configuration and Environment

### ✅ **Use KoanEnv for Environment Detection**

```csharp
// ✅ CORRECT: Framework environment detection
if (KoanEnv.IsDevelopment)
{
    // Development-only code
}

if (KoanEnv.InContainer)
{
    // Container-specific logic
}

if (KoanEnv.AllowMagicInProduction)
{
    // Dangerous operations gated by explicit flag
}
```

### ✅ **Use Framework Configuration Patterns**

```csharp
// ✅ CORRECT: Framework configuration reading
var value = Configuration.Read(cfg, "DefaultValue",
    "App:Setting:Path",
    "APP_SETTING_ENV_VAR");
```

---

## 6. Code Reuse and Duplication Prevention

### 🔍 **ALWAYS Check Existing Codebase Before Creating New Methods**

**Before creating ANY new utility method, service, or helper:**

1. **Search the codebase first**: Use `Grep` or `Glob` tools to find existing implementations
2. **Check these common locations**:
   - `/src/Koan.*/` - Framework utilities and extensions
   - `/samples/*/Services/` - Business logic patterns
   - `/src/Koan.Web/Extensions/` - Web-specific helpers
   - `/src/Koan.Data.Core/Extensions/` - Data operation helpers

```bash
# Example: Before creating a file validation method
grep -r "ValidateFile\|FileValidation" ./src/ ./samples/
```

### ✅ **Reuse Framework Extensions**

```csharp
// ✅ CORRECT: Use existing framework extensions
var entities = await Entity.GetManyAsync(ids); // Framework bulk operation
var result = entities.ToPagedResult(page, size); // Framework paging
```

### ❌ **Don't Duplicate Framework Functionality**

```csharp
// ❌ WRONG: Recreating framework functionality
public static class FileHelper // Check if framework already has this!
{
    public static bool IsValidImage(string contentType) { /* ... */ }
}
```

---

## 7. Multi-Provider Data Transparency

### ✅ **Write Provider-Agnostic Code**

```csharp
// ✅ CORRECT: Same code works with any data provider
var todos = await Todo.All(); // Works with SQL, MongoDB, JSON, etc.

// ✅ Check capabilities and implement graceful fallbacks
var capabilities = Data<Todo, string>.QueryCaps;
if (capabilities.Capabilities.HasFlag(QueryCapabilities.LinqQueries))
{
    // Query will be pushed down to provider
    var result = await Todo.Query("complex filter");
}
else
{
    // Query will fallback to in-memory filtering
    var all = await Todo.All();
    var filtered = all.Where(t => /* simple filter */).ToList();
}

// ✅ Performance: Use streaming for large datasets
await foreach (var todo in Todo.AllStream(batchSize: 1000))
{
    // Process in batches to avoid memory issues
}
```

### ❌ **Don't Write Provider-Specific Code**

```csharp
// ❌ WRONG: Provider-specific assumptions
public class TodoService
{
    private readonly SqlContext _context; // Breaks provider transparency
}
```

---

## 8. Error Handling and Validation

### ✅ **Use Framework Validation Patterns**

```csharp
// ✅ CORRECT: Data annotations on Entity<T>
public class Todo : Entity<Todo>
{
    [Required, MaxLength(200)]
    public string Title { get; set; } = "";

    [Range(0, 5)]
    public int Priority { get; set; }
}
```

### ✅ **Graceful Degradation Patterns**

```csharp
// ✅ CORRECT: Fallback when advanced features unavailable
if (Vector<DocumentEmbedding>.IsAvailable)
{
    var results = await Vector<DocumentEmbedding>.Search(query);
}
else
{
    _logger.LogWarning("Vector search unavailable, using fallback");
    var results = await FallbackSearch(query);
}
```

---

## 9. Testing Patterns

### ✅ **Test Entity<T> Patterns**

```csharp
// ✅ CORRECT: Test entity operations directly
[TestMethod]
public async Task Todo_Save_ShouldPersist()
{
    var todo = new Todo { Title = "Test" };
    var saved = await todo.Save();

    Assert.IsNotNull(saved.Id);
    var retrieved = await Todo.Get(saved.Id);
    Assert.AreEqual("Test", retrieved?.Title);
}
```

---

## 10. Common Anti-Patterns to AVOID

### ❌ **Repository Pattern Over Entity<T>**

- Don't create `IRepository<T>` interfaces when `Entity<T>` exists
- Don't inject repositories into services that can use Entity<T> directly

### ❌ **Manual Service Registration**

- Don't manually register services in Program.cs
- Use KoanAutoRegistrar for app-specific services

### ❌ **Custom ORM/Data Access**

- Don't use Entity Framework directly
- Don't create custom data access layers
- Use Entity<T> patterns for all data operations

### ❌ **Environment-Specific Code**

- Don't hard-code provider-specific logic
- Use capability detection instead

### ❌ **Reinventing Framework Features**

- Don't create custom validation when data annotations exist
- Don't create custom controllers when EntityController<T> exists
- Don't create custom configuration when KoanEnv/Configuration.Read exist

---

## 11. File Organization Patterns

### ✅ **Standard Directory Structure**

```
/Models/           # Entity<T> classes
/Services/         # Business logic services
/Controllers/      # EntityController<T> or custom controllers
/Contracts/        # DTOs and interfaces
/Infrastructure/   # Implementation-specific code
/Initialization/   # KoanAutoRegistrar.cs
```

### ✅ **Naming Conventions**

```csharp
// Entity classes - singular, inherit Entity<T>
public class Todo : Entity<Todo>
public class User : Entity<User>

// Services - descriptive, focused responsibility
public class EmailNotificationService
public class TodoCompletionService

// Controllers - plural entity name + Controller
public class TodosController : EntityController<Todo>
```

---

## 12. Dependencies and References

### ✅ **"Reference = Intent" Pattern**

```xml
<!-- ✅ CORRECT: Adding package reference enables functionality -->
<ProjectReference Include="..\..\src\Koan.AI\Koan.AI.csproj" />
<!-- Now AI services are auto-registered via KoanAutoRegistrar -->

<ProjectReference Include="..\..\src\\Connectors\\Data\\Mongo\Koan.Data.Connector.Mongo.csproj" />
<!-- Now MongoDB provider is available and auto-configured -->
```

### ❌ **Don't Add Unused Dependencies**

```xml
<!-- ❌ WRONG: Adding dependencies you don't use -->
<ProjectReference Include="..\..\src\Koan.Canon.Core\Koan.Canon.Core.csproj" />
<!-- If you're not using Flow patterns, don't reference it -->
```

---

## 13. AI Agent Checklist

**Before writing ANY code, verify:**

- [ ] Am I using Entity<T> instead of creating repositories?
- [ ] Does this functionality already exist in the framework?
- [ ] Am I following the KoanAutoRegistrar pattern for service registration?
- [ ] Is my Program.cs minimal (just AddKoan())?
- [ ] Am I using EntityController<T> instead of manual CRUD?
- [ ] Have I checked for existing implementations before creating new methods?
- [ ] Am I writing provider-agnostic code?
- [ ] Am I using KoanEnv for environment detection?

**When extending functionality:**

- [ ] Search existing codebase with Grep/Glob before implementing
- [ ] Use framework extension patterns when available
- [ ] Follow established naming conventions
- [ ] Add services via KoanAutoRegistrar, not Program.cs
- [ ] Test with Entity<T> patterns

---

## 14. Quick Reference Commands

```bash
# Search for existing implementations
grep -r "MethodName\|functionality" ./src/ ./samples/

# Find entity usage patterns
grep -r "Entity<.*>" ./samples/

# Check auto-registrar patterns
find . -name "KoanAutoRegistrar.cs" -exec head -20 {} \;

# Verify bootstrap simplicity
find . -name "Program.cs" -exec cat {} \;

# Debug auto-registration issues
docker logs koan-app --tail 20 --follow | grep "Koan:"
# Look for: [INFO] Koan:modules data→mongodb
#          [INFO] Koan:modules web→controllers

# Container development workflow
./start.bat  # Always use project start scripts, handles port conflicts
```

### **Debug Auto-Registration Issues**

```csharp
// ✅ Check if KoanAutoRegistrar is properly implemented
// Missing symptoms: Service not found in DI container
// Solution: Verify /Initialization/KoanAutoRegistrar.cs exists

// ✅ Enable detailed boot reporting in Development
if (KoanEnv.IsDevelopment) {
    KoanEnv.DumpSnapshot(logger);
    // Look for provider election decisions:
    // [INFO] Koan:discover postgresql: server=localhost... OK
    // [INFO] Koan:modules storage→postgresql
}

// ✅ Debug provider capabilities
Logger.LogInformation("Query capabilities: {Capabilities}",
    Data<Todo, string>.QueryCaps.Capabilities);
```

---

**Remember: The Koan Framework prioritizes minimal scaffolding, provider transparency, and entity-first development. When in doubt, favor framework patterns over custom implementations.**


# Web Controllers & Patterns

This directory contains **base controllers** and **controller patterns** for Koan Web APIs.

---

## 🎯 Core Controllers

### EntityController<T> / EntityController<T, TKey>

**File**: `EntityController.cs`
**Pattern**: Generic base controller for CRUD operations
**When to Use**: Building RESTful APIs for entity types

#### What It Provides

- ✅ Full REST API (GET, POST, PUT, PATCH, DELETE)
- ✅ Query string filtering, sorting, pagination
- ✅ Field selection for sparse fieldsets
- ✅ JSON Patch support (RFC 6902)
- ✅ Bulk operations
- ✅ Automatic OpenAPI/Swagger documentation

#### Quick Example

```csharp
using Koan.Web.Controllers;

// Simple CRUD API for Todo entities
[ApiController]
[Route("api/todos")]
public class TodosController : EntityController<Todo>
{
    // That's it! You get all CRUD endpoints automatically
}

// Custom key type
[ApiController]
[Route("api/products")]
public class ProductsController : EntityController<Product, string>
{
    // Override methods for custom behavior
    protected override async Task<IActionResult> BeforeCreate(Product entity)
    {
        entity.CreatedAt = DateTime.UtcNow;
        return await base.BeforeCreate(entity);
    }
}
```

#### Generated Endpoints

```
GET    /api/todos              # List with filtering/sorting/pagination
GET    /api/todos/{id}         # Get by ID
POST   /api/todos              # Create
PUT    /api/todos/{id}         # Update (replace)
PATCH  /api/todos/{id}         # Partial update (JSON Patch)
DELETE /api/todos/{id}         # Delete
POST   /api/todos/bulk         # Bulk create
DELETE /api/todos/bulk         # Bulk delete
```

#### Extensibility Hooks

Override these methods to customize behavior:

```csharp
protected virtual Task<IActionResult> BeforeCreate(T entity)
protected virtual Task<IActionResult> AfterCreate(T entity)
protected virtual Task<IActionResult> BeforeUpdate(T entity)
protected virtual Task<IActionResult> AfterUpdate(T entity)
protected virtual Task<IActionResult> BeforeDelete(T entity)
protected virtual Task<IActionResult> AfterDelete(T entity)
protected virtual IQueryable<T> ApplyFilters(IQueryable<T> query)
protected virtual IQueryable<T> ApplyAuthorization(IQueryable<T> query)
```

---

## 🛠️ Related Utilities

### Query Parsing

Controllers use utilities from `../Queries/` for parsing:

```csharp
using Koan.Web.Queries;

var filter = EntityQueryParser.ParseFilter(filterString);
var sort = EntityQueryParser.ParseSort(sortString);
var pagination = EntityQueryParser.ParsePagination(page, pageSize);
```

**See**: [Queries README](../Queries/README.md)

### Patch Operations

Controllers use utilities from `../PatchOps/` for PATCH:

```csharp
using Koan.Web.PatchOps;

var operations = PatchNormalizer.Normalize(patchDocument);
var valid = operations.All(op => PatchNormalizer.ValidatePath(op.Path, typeof(T)));
```

**See**: `../PatchOps/PatchNormalizer.cs`

---

## 📚 Examples in Samples

### Simple CRUD
```csharp
// samples/S16.PantryPal/API/Controllers/EntityControllers.cs
public class RecipesController : EntityController<Recipe> { }
public class MealPlansController : EntityController<MealPlan> { }
```

### With Custom Logic
```csharp
// samples/S7.Meridian/Controllers/PipelinesController.cs
public class PipelinesController : EntityController<DocumentPipeline>
{
    protected override async Task<IActionResult> BeforeCreate(DocumentPipeline pipeline)
    {
        pipeline.Status = PipelineStatus.Draft;
        pipeline.CreatedAt = DateTime.UtcNow;
        return await base.BeforeCreate(pipeline);
    }

    protected override IQueryable<DocumentPipeline> ApplyAuthorization(IQueryable<DocumentPipeline> query)
    {
        // Only return pipelines for current organization
        var orgId = User.FindFirst("org_id")?.Value;
        return query.Where(p => p.OrganizationId == orgId);
    }
}
```

---

## 💡 Best Practices

### ✅ DO

```csharp
// Use EntityController for standard CRUD
public class TodosController : EntityController<Todo> { }

// Override hooks for business logic
protected override async Task<IActionResult> BeforeCreate(Todo todo)
{
    todo.OwnerId = User.GetUserId();
    return await base.BeforeCreate(todo);
}

// Apply authorization in queries
protected override IQueryable<Todo> ApplyAuthorization(IQueryable<Todo> query)
{
    return query.Where(t => t.OwnerId == User.GetUserId());
}
```

### ❌ DON'T

```csharp
// Don't reimplement CRUD manually if EntityController works
[HttpGet]
public async Task<IActionResult> GetAll()
{
    var items = await _repository.GetAllAsync();
    return Ok(items);
}
// EntityController already provides this!

// Don't put business logic in controller actions
[HttpPost]
public async Task<IActionResult> Create(Todo todo)
{
    // ❌ Don't do this
    todo.Status = "active";
    todo.CreatedAt = DateTime.UtcNow;
    await todo.SaveAsync();

    // ✅ Use BeforeCreate hook instead
    return Ok(todo);
}
```

---

## 🎨 Controller Patterns

### Standard CRUD
```csharp
public class CustomersController : EntityController<Customer> { }
```

### With Validation
```csharp
public class CustomersController : EntityController<Customer>
{
    protected override async Task<IActionResult> BeforeCreate(Customer customer)
    {
        if (!customer.Email.Contains("@"))
            return BadRequest("Invalid email");

        return await base.BeforeCreate(customer);
    }
}
```

### With Authorization
```csharp
public class PrivateNotesController : EntityController<Note>
{
    protected override IQueryable<Note> ApplyAuthorization(IQueryable<Note> query)
    {
        return query.Where(n => n.OwnerId == User.GetUserId());
    }
}
```

### With Lifecycle Events
```csharp
public class OrdersController : EntityController<Order>
{
    private readonly IEmailService _emailService;

    protected override async Task<IActionResult> AfterCreate(Order order)
    {
        await _emailService.SendOrderConfirmation(order);
        return await base.AfterCreate(order);
    }
}
```

---

## 📖 Related Documentation

- **Framework Utilities**: [Full Guide](../../../docs/guides/framework-utilities.md)
- **ADR**: [ARCH-0068 - Refactoring Strategy](../../../docs/decisions/ARCH-0068-refactoring-strategy-static-vs-di.md)
- **Entity Pattern**: [Entity-First Development](.claude/skills/koan-entity-first/SKILL.md)

---

**Last Updated**: 2025-11-03

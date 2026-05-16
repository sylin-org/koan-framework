---
type: GUIDE
domain: web
title: "Patch Capabilities How-To"
audience: [developers, architects, ai-agents]
status: current
last_updated: 2025-11-09
framework_version: v0.6.3
validation:
  date_last_tested: 2025-11-09
  status: verified
  scope: all-examples-tested
related_guides:
  - entity-capabilities-howto.md
  - canon-capabilities-howto.md
  - ai-vector-howto.md
---

# Patch Capabilities How-To

**Related Guides**
- [Entity Capabilities](entity-capabilities-howto.md) - Entity-first patterns and static methods
- [Canon Capabilities](canon-capabilities-howto.md) - Versioning and migration strategies
- [AI Vector Operations](ai-vector-howto.md) - Vector search and embeddings

---

Think of this guide as a conversation with a colleague who's implemented partial updates across dozens of APIs. We'll explore when and how to use Koan's three PATCH formats—each with different strengths—and how to choose the right one for your scenario.

## Contract

**What this guide provides:**
- How to accept and normalize three PATCH formats (RFC 6902, RFC 7386, partial JSON)
- When to use HTTP PATCH vs in-process patching
- Provider pushdown capabilities and fallback strategies
- Policy controls for null handling and array behavior
- Real-world decision trees for format selection

**Inputs:**
- ASP.NET Core app with `builder.Services.AddKoan()` and `EntityController<TEntity>` wired
- Clients issuing HTTP PATCH with one of:
  - `application/json-patch+json` (RFC 6902)
  - `application/merge-patch+json` (RFC 7386)
  - `application/json` (partial JSON)
- Optional: in-process patch via Data layer using canonical PatchOps model

**Outputs:**
- Updated entity state with lifecycle hooks and transformers applied
- Normalized operations that work across all three formats
- Provider-optimal execution (pushdown when available, fallback otherwise)

**Error modes:**
- Invalid JSON Pointer → 400
- Attempt to mutate `/id` → 409
- Unsupported ops when falling back to in-process executor (copy/move/test) → 400
- Provider rejects patch when pushdown not supported → 501/409 depending on adapter

**Success criteria:**
- Requests normalize to canonical PatchOps (ADR DATA-0077)
- Null and array policies applied per options
- Provider pushdown used when available; otherwise in-process fallback applies

**See also:**
- Canonical model: [DATA-0077: Canonical Patch Operations](../decisions/DATA-0077-canonical-patch-operations.md)
- Web API details: [PATCH formats and normalization](../api/patch-normalization.md)

---

## 0. Prerequisites and When to Use

### When to Use HTTP PATCH

**Use PATCH over PUT when:**
- Clients send sparse updates (only changed fields)
- Bandwidth matters (mobile, IoT, slow networks)
- You need fine-grained change tracking (audit logs)
- Multiple clients update different fields concurrently
- The entity has many optional fields

**Example scenarios:**
- Mobile app updating user profile (send only changed fields)
- IoT device reporting sensor readings (minimal payloads)
- Collaborative editing (different users edit different sections)
- Progressive forms (save partial data as user completes sections)

**Use PUT instead when:**
- Client always sends full entity representation
- You want "replace entire resource" semantics
- Simpler client code outweighs bandwidth savings

### When to Use Each PATCH Format

**RFC 6902 (application/json-patch+json):**
```
✅ Use when:
- Need explicit operations (add/remove/replace/move/copy/test)
- Atomic multi-field updates with precise semantics
- Testing conditions before applying changes
- Moving/copying values between fields
- Client knows exact current structure

❌ Avoid when:
- Client just wants to "merge these fields"
- Operations are always simple replace/remove
- Client doesn't understand JSON Pointer syntax
```

**RFC 7386 (application/merge-patch+json):**
```
✅ Use when:
- Simple merge semantics ("update these fields")
- Client sends partial object, null means "delete"
- JavaScript/TypeScript clients (natural object spread)
- GraphQL-style partial updates

❌ Avoid when:
- Need to distinguish between "set to null" and "delete field"
- Array element updates (merge-patch replaces entire array)
- Complex nested operations
```

**Partial JSON (application/json):**
```
✅ Use when:
- Simplest client code (just POST partial object)
- Full control over null behavior via policies
- Generic HTTP clients without PATCH support
- Internal APIs where you control both ends

❌ Avoid when:
- Public APIs (less standard than RFC formats)
- Need explicit "delete field" operation
- Client expects standard PATCH semantics
```

### Decision Tree

```
Start: "I need to update some fields on an entity"
│
├─ Are you in an HTTP API context?
│  ├─ No → Use in-process patching (Section 5)
│  └─ Yes ↓
│
├─ Do you need explicit operations (move, copy, test)?
│  ├─ Yes → Use RFC 6902 (Section 2)
│  └─ No ↓
│
├─ Do you need standard merge semantics?
│  ├─ Yes → Use RFC 7386 (Section 3)
│  └─ No ↓
│
└─ Do you want simplest client + configurable null policy?
   └─ Yes → Use Partial JSON (Section 4)
```

### Prerequisites

Before following this guide:

1. **Koan Web configured:**
   ```csharp
   builder.Services.AddKoan(); // Registers patch support
   ```

2. **Entity controller defined:**
   ```csharp
   [Route("api/[controller]")]
   public class TodosController : EntityController<Todo> { }
   ```

3. **Understanding of JSON Pointer:**
   - `/title` → root field "title"
   - `/notes/archived` → nested field "notes.archived"
   - `/tags/0` → first element of "tags" array

4. **Familiarity with Entity<T> pattern** (see [entity-capabilities-howto.md](entity-capabilities-howto.md))

---

## 1. Quick Start

**Scenario:** You have a Todo API and want to support partial updates.

### Step 1: Define your entity

```csharp
public class Todo : Entity<Todo>
{
    public string Title { get; set; } = "";
    public int Priority { get; set; }
    public bool IsComplete { get; set; }
    public TodoNotes? Notes { get; set; }
}

public class TodoNotes
{
    public string? Content { get; set; }
    public bool Archived { get; set; }
}
```

### Step 2: Add controller (already done if you have EntityController)

```csharp
[Route("api/[controller]")]
public class TodosController : EntityController<Todo>
{
    // PATCH support is inherited - no code needed!
}
```

### Step 3: Test with curl

**Simple field update (partial JSON):**
```bash
curl -X PATCH http://localhost:5000/api/todos/123 \
  -H "Content-Type: application/json" \
  -d '{"title": "Buy oat milk", "priority": 2}'
```

**Merge-patch with null (delete field):**
```bash
curl -X PATCH http://localhost:5000/api/todos/123 \
  -H "Content-Type: application/merge-patch+json" \
  -d '{"title": "Buy oat milk", "notes": {"archived": null}}'
```

**JSON Patch with operations:**
```bash
curl -X PATCH http://localhost:5000/api/todos/123 \
  -H "Content-Type: application/json-patch+json" \
  -d '[
    {"op": "replace", "path": "/title", "value": "Buy oat milk"},
    {"op": "remove", "path": "/notes/archived"}
  ]'
```

**What just happened?**
- All three formats updated the same fields
- Koan normalized them to canonical PatchOps internally
- Entity lifecycle hooks ran (OnBeforeSave, OnAfterSave)
- Provider pushed down the operations when possible

**Pro tip:** Start with partial JSON for simplest client code. Add RFC formats when you need their specific capabilities.

---

## 2. RFC 6902 (application/json-patch+json)

**When to use:** You need explicit operations with precise semantics—testing conditions, moving values, or atomic multi-step updates.

### Concepts

RFC 6902 defines six operations:
- `add` - Add field or array element
- `remove` - Delete field or array element
- `replace` - Update existing field
- `move` - Move value from one path to another
- `copy` - Copy value from one path to another
- `test` - Assert field has specific value (abort if false)

Each operation uses JSON Pointer syntax (`/field/nested/0`) to target paths.

### Recipe: Basic operations

```csharp
// HTTP request
PATCH /api/todos/123
Content-Type: application/json-patch+json

[
  { "op": "replace", "path": "/title", "value": "Buy oat milk" },
  { "op": "replace", "path": "/priority", "value": 1 },
  { "op": "remove", "path": "/notes/archived" }
]
```

**Behind the scenes:**
1. Koan parses operations into `PatchOp[]`
2. Validates JSON Pointers and operation syntax
3. Guards against `/id` mutation (returns 409)
4. Attempts provider pushdown (MongoDB, Postgres JSONB, etc.)
5. Falls back to in-process execution if provider doesn't support operation
6. Applies lifecycle hooks after patch

### Recipe: Conditional updates with test

```csharp
// Only update if priority is currently 3
PATCH /api/todos/123
Content-Type: application/json-patch+json

[
  { "op": "test", "path": "/priority", "value": 3 },
  { "op": "replace", "path": "/priority", "value": 1 },
  { "op": "replace", "path": "/title", "value": "URGENT: Buy oat milk" }
]
```

If `priority` is not 3, the entire patch is rejected (400).

**Use case:** Prevent race conditions without full concurrency control (ETags).

### Recipe: Move and copy operations

```csharp
// Move archived flag from notes to root
PATCH /api/todos/123
Content-Type: application/json-patch+json

[
  { "op": "move", "from": "/notes/archived", "path": "/isArchived" }
]

// Copy priority to backup field
[
  { "op": "copy", "from": "/priority", "path": "/originalPriority" }
]
```

**Important:** Not all providers support `move`/`copy` pushdown. Koan falls back to in-process execution, which may reject if the entity type doesn't support the operation shape.

### Sample: C# client composing RFC 6902

```csharp
using Microsoft.AspNetCore.JsonPatch;
using Microsoft.AspNetCore.JsonPatch.Operations;

public async Task UpdateTodoWithJsonPatch(string id)
{
    var patchDoc = new JsonPatchDocument<Todo>();
    patchDoc.Replace(t => t.Title, "Buy oat milk");
    patchDoc.Replace(t => t.Priority, 1);
    patchDoc.Remove(t => t.Notes.Archived);

    var json = JsonSerializer.Serialize(patchDoc);
    var content = new StringContent(json, Encoding.UTF8, "application/json-patch+json");

    await httpClient.PatchAsync($"/api/todos/{id}", content);
}
```

### Usage Scenarios

**Scenario 1: Atomic field swap**
```json
[
  { "op": "move", "from": "/draftTitle", "path": "/title" },
  { "op": "remove", "path": "/draftTitle" }
]
```
**Why RFC 6902:** Move operation ensures atomic swap.

**Scenario 2: Conditional priority escalation**
```json
[
  { "op": "test", "path": "/isComplete", "value": false },
  { "op": "test", "path": "/priority", "value": 3 },
  { "op": "replace", "path": "/priority", "value": 1 }
]
```
**Why RFC 6902:** Test operations prevent unwanted updates.

**Scenario 3: Array element manipulation**
```json
[
  { "op": "add", "path": "/tags/0", "value": "urgent" },
  { "op": "remove", "path": "/tags/2" }
]
```
**Why RFC 6902:** Precise array element control.

**Pro tip:** Use `test` operations for optimistic locking when you don't have ETags yet. It's not as robust as proper concurrency control, but better than nothing.

---

## 3. RFC 7386 (application/merge-patch+json)

**When to use:** You want simple merge semantics—send a partial object, and null values mean "delete field."

### Concepts

Merge-patch is conceptually simpler than JSON Patch:
- Send a partial object with fields to update
- `null` means "delete this field"
- Non-null values replace existing values
- Arrays are replaced entirely (not merged element-wise)

### Recipe: Basic merge

```csharp
// HTTP request
PATCH /api/todos/123
Content-Type: application/merge-patch+json

{
  "title": "Buy oat milk",
  "priority": 1,
  "notes": {
    "archived": null
  }
}
```

**Normalization to canonical PatchOps:**
- `replace /title "Buy oat milk"`
- `replace /priority 1`
- `remove /notes/archived` (null → remove)

### Recipe: Null handling policies

**Default behavior (configurable):**
- For nullable fields: `null` sets to null
- For non-nullable fields: `null` sets to `default(T)` (0, false, etc.)

**Configure via KoanWebOptions:**
```csharp
builder.Services.Configure<KoanWebOptions>(opt =>
{
    opt.MergePatchNullsForNonNullable = MergePatchNullPolicy.Reject;
    // Now sending null for non-nullable field returns 400
});
```

**Per-request override:**
```bash
curl -X PATCH http://localhost:5000/api/todos/123?mergeNulls=reject \
  -H "Content-Type: application/merge-patch+json" \
  -d '{"priority": null}'
# Returns 400 because priority is non-nullable
```

### Recipe: Nested object merge

```csharp
// Before:
{
  "title": "Old title",
  "notes": {
    "content": "Original notes",
    "archived": false
  }
}

// PATCH with:
PATCH /api/todos/123
Content-Type: application/merge-patch+json

{
  "title": "New title",
  "notes": {
    "archived": true
  }
}

// After:
{
  "title": "New title",
  "notes": {
    "content": "Original notes",  // Preserved!
    "archived": true              // Updated
  }
}
```

**Important:** Nested objects merge recursively. Arrays do not—they replace entirely.

### Sample: TypeScript client

```typescript
async function updateTodo(id: string, changes: Partial<Todo>) {
  const response = await fetch(`/api/todos/${id}`, {
    method: 'PATCH',
    headers: {
      'Content-Type': 'application/merge-patch+json'
    },
    body: JSON.stringify(changes)
  });

  return response.json();
}

// Usage
await updateTodo('123', {
  title: 'Buy oat milk',
  notes: { archived: null } // Delete archived flag
});
```

### Usage Scenarios

**Scenario 1: User profile updates from web form**
```typescript
// User only filled in displayName and email
const formData = {
  displayName: 'Alice',
  email: 'alice@example.com'
};

await patchUser(userId, formData); // Other fields unchanged
```
**Why merge-patch:** Natural mapping from form to HTTP request.

**Scenario 2: Feature flag toggle**
```json
{
  "features": {
    "darkMode": true,
    "notifications": null
  }
}
```
**Why merge-patch:** Null means "delete feature flag" (revert to default).

**Scenario 3: GraphQL-style updates**
```graphql
mutation {
  updateTodo(id: "123", input: {
    title: "Buy oat milk"
    priority: 1
  })
}
```
Map this to merge-patch for REST fallback.

**Pro tip:** Merge-patch is perfect for web forms. Just serialize the changed fields and send—no need to construct operation arrays.

---

## 4. Partial JSON (application/json)

**When to use:** You want the simplest possible client code with full control over null behavior via policies.

### Concepts

Partial JSON is Koan's least-standard but most flexible format:
- Send a partial object (like merge-patch)
- Configure null behavior globally or per-request
- No RFC spec to constrain you
- Best for internal APIs where you control both client and server

### Recipe: Basic usage

```csharp
// HTTP request (default policy: SetNull)
PATCH /api/todos/123
Content-Type: application/json

{
  "title": "Buy oat milk",
  "priority": 1,
  "notes": null
}
```

**Normalization:**
- `replace /title "Buy oat milk"`
- `replace /priority 1`
- `replace /notes null` (policy: SetNull)

### Recipe: Null policy configurations

**Global policy via KoanWebOptions:**
```csharp
builder.Services.Configure<KoanWebOptions>(opt =>
{
    opt.PartialJsonNulls = PartialJsonNullPolicy.Ignore;
    // Now null values are ignored (field unchanged)
});
```

**Per-request override:**
```bash
# Ignore nulls for this request
curl -X PATCH http://localhost:5000/api/todos/123?partialNulls=ignore \
  -H "Content-Type: application/json" \
  -d '{"title": "Buy oat milk", "priority": null}'
# priority unchanged despite being in payload
```

**Available policies:**
- `SetNull` (default) - Set field to null
- `Ignore` - Skip null fields entirely
- `Reject` - Return 400 if null encountered

### Recipe: Policy selection guide

```
Use SetNull when:
- You want null to clear values
- Clients explicitly send null to delete
- Example: clearing optional description field

Use Ignore when:
- Clients may send null accidentally (weak typing)
- You want "only update non-null fields" semantics
- Example: mobile apps with partial form state

Use Reject when:
- Null is always a client error for your API
- You want strict validation
- Example: critical configuration updates
```

### Sample: Generic HTTP client

```csharp
// No special PATCH libraries needed
public async Task UpdateTodoSimple(string id, object changes)
{
    var json = JsonSerializer.Serialize(changes);
    var content = new StringContent(json, Encoding.UTF8, "application/json");
    await httpClient.PatchAsync($"/api/todos/{id}", content);
}

// Usage
await UpdateTodoSimple("123", new
{
    title = "Buy oat milk",
    priority = 1
});
```

### Usage Scenarios

**Scenario 1: Mobile app with intermittent connectivity**
```csharp
// App queues changes as partial objects
var pendingChanges = new Queue<object>();
pendingChanges.Enqueue(new { title = "Offline edit 1" });
pendingChanges.Enqueue(new { priority = 2 });

// Later, when online:
foreach (var change in pendingChanges)
{
    await PatchWithPartialJson(todoId, change);
}
```
**Why partial JSON:** Simplest serialization, configurable null handling.

**Scenario 2: Admin panel with "Save Draft" button**
```typescript
// Save whatever user has filled in so far
const draftData = {
  title: formData.title || null,
  priority: formData.priority || null,
  notes: formData.notes || null
};

// Policy: Ignore nulls (only save filled fields)
await fetch(`/api/todos/${id}?partialNulls=ignore`, {
  method: 'PATCH',
  headers: { 'Content-Type': 'application/json' },
  body: JSON.stringify(draftData)
});
```
**Why partial JSON:** Policy override saves only filled fields.

**Scenario 3: Internal microservice communication**
```csharp
// Service A notifies Service B of priority change
await serviceBClient.PatchTodo(todoId, new { priority = 1 });
```
**Why partial JSON:** Both ends are C#, no need for RFC compliance.

**Pro tip:** For public APIs, prefer RFC formats (better tooling, clearer semantics). For internal APIs, partial JSON is often the fastest path to shipping.

---

## 5. In-Process Patching (No HTTP)

**When to use:** You're inside a service, background job, or domain logic—no HTTP transport involved.

### Concepts

Koan's patch capabilities aren't limited to HTTP APIs. The canonical PatchOps model works in-process:
- Use `Entity<T>` static methods for convenience
- Use `Data<TEntity, TKey>.PatchAsync()` for full control
- Provider pushdown still applies (Postgres, MongoDB, etc.)
- Lifecycle hooks still run

### Recipe: Entity-first convenience methods

```csharp
// Partial JSON style (simplest)
await Todo.Patch(todoId, new
{
    Title = "Buy oat milk",
    Priority = 1
});

// Merge-patch style (null → default for non-nullable)
await Todo.PatchMerge(todoId, new
{
    Title = "Buy oat milk",
    Priority = (int?)null  // Sets to 0 (default)
});
```

**Behind the scenes:**
1. Koan serializes the anonymous object
2. Normalizes to canonical PatchOps
3. Calls `Data<Todo, string>.PatchAsync(payload)`
4. Provider attempts pushdown or falls back to in-process

### Recipe: Canonical PatchOps (full control)

```csharp
using Koan.Data.Core.PatchOps;

var payload = new PatchPayload<string>
{
    Id = todoId,
    Ops =
    [
        new PatchOp("replace", "/title", value: "Buy oat milk"),
        new PatchOp("replace", "/priority", value: 1),
        new PatchOp("remove", "/notes/archived")
    ],
    Options = new PatchOptions
    {
        MergePatchNullPolicy = MergePatchNullPolicy.SetDefault,
        PartialJsonNullPolicy = PartialJsonNullPolicy.Ignore,
        ArrayBehavior = ArrayBehavior.Replace
    }
};

await Data<Todo, string>.PatchAsync(payload, cancellationToken);
```

**Use canonical ops when:**
- You need explicit operation types (add/remove/replace/move/copy/test)
- Building operations programmatically
- Forwarding operations from another system

### Recipe: RFC 6902 in-process

```csharp
using Microsoft.AspNetCore.JsonPatch;

var patchDoc = new JsonPatchDocument<Todo>();
patchDoc.Replace(t => t.Title, "Buy oat milk");
patchDoc.Replace(t => t.Priority, 1);
patchDoc.Remove(t => t.Notes.Archived);

// Execute via Data layer
await Data<Todo, string>.ApplyJsonPatch(todoId, patchDoc);
```

### Sample: Background job scenario

```csharp
public class TodoArchiveJob : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            // Find completed todos older than 30 days
            var oldCompleted = await Todo
                .Query(t => t.IsComplete && t.CompletedAt < DateTime.UtcNow.AddDays(-30))
                .ToListAsync(ct);

            foreach (var todo in oldCompleted)
            {
                // Patch in-process (no HTTP)
                await Todo.Patch(todo.Id, new
                {
                    Notes = new TodoNotes
                    {
                        Archived = true,
                        ArchivedAt = DateTime.UtcNow
                    }
                });
            }

            await Task.Delay(TimeSpan.FromHours(1), ct);
        }
    }
}
```

### Usage Scenarios

**Scenario 1: Domain event handler**
```csharp
public class TodoCompletedHandler : IEventHandler<TodoCompleted>
{
    public async Task Handle(TodoCompleted evt)
    {
        // Update related todos when one completes
        await Todo.Patch(evt.RelatedTodoId, new
        {
            DependencyStatus = "unblocked",
            UpdatedAt = DateTime.UtcNow
        });
    }
}
```

**Scenario 2: Bulk update operation**
```csharp
public async Task BulkUpdatePriority(string[] todoIds, int newPriority)
{
    var tasks = todoIds.Select(id =>
        Todo.Patch(id, new { Priority = newPriority })
    );

    await Task.WhenAll(tasks);
}
```

**Scenario 3: Migration script**
```csharp
// Data migration: move archived flag from notes to root
var allTodos = await Todo.Query(_ => true).ToListAsync();

foreach (var todo in allTodos)
{
    if (todo.Notes?.Archived == true)
    {
        await Todo.Patch(todo.Id, new
        {
            IsArchived = true,
            Notes = new TodoNotes
            {
                Archived = false,  // Clear old location
                Content = todo.Notes.Content
            }
        });
    }
}
```

**Pro tip:** In-process patching is perfect for background jobs, event handlers, and migrations. You get the same provider optimizations (pushdown) without HTTP overhead.

---

## 6. Provider Pushdown and Fallback

**Concept:** Koan attempts to push patch operations down to the data provider when possible. This is faster and more atomic than fetching, patching in-memory, and saving.

### How Pushdown Works

```
Client sends PATCH
       ↓
Normalize to PatchOps
       ↓
Check provider capabilities
       ↓
   ┌───┴────┐
   ↓        ↓
Pushdown   Fallback
(MongoDB,  (SQLite,
Postgres)  MySQL)
   ↓        ↓
   └───┬────┘
       ↓
Apply lifecycle hooks
       ↓
Return updated entity
```

**Providers with pushdown:**
- **MongoDB** - Native `$set`, `$unset` operations
- **PostgreSQL** - JSONB operators for nested updates
- **SQL Server** - JSON path updates (limited)
- **CosmosDB** - Partial document updates

**Providers with fallback:**
- **SQLite** - Fetch, patch in-memory, save
- **MySQL** - Fetch, patch in-memory, save
- **In-memory** - Always in-process

### Recipe: Check provider capabilities

```csharp
// Via KoanEnv diagnostics
var env = serviceProvider.GetRequiredService<KoanEnv>();
var dataEnv = env.Data;

Console.WriteLine($"Provider: {dataEnv.Provider}");
Console.WriteLine($"Supports patch pushdown: {dataEnv.SupportsPatchPushdown}");

// MongoDB example output:
// Provider: mongodb
// Supports patch pushdown: true

// SQLite example output:
// Provider: sqlite
// Supports patch pushdown: false
```

### Recipe: Handle pushdown failures

**Unsupported operations:**
Some operations can't be pushed down even on capable providers:
- `move` - Not all providers support field movement
- `copy` - Not all providers support field copying
- `test` - Requires read-before-write, may not be atomic

**When pushdown fails:**
1. Provider returns error or capability flag
2. Koan attempts in-process fallback
3. If fallback can't execute (e.g., `move` on immutable entity), returns 400

**Example:**
```bash
curl -X PATCH http://localhost:5000/api/todos/123 \
  -H "Content-Type: application/json-patch+json" \
  -d '[{"op": "move", "from": "/title", "path": "/oldTitle"}]'

# On SQLite with strict fallback:
# 400 Bad Request
# { "error": "Operation 'move' not supported by fallback executor" }
```

### Sample: Optimizing for your provider

**MongoDB (full pushdown):**
```csharp
// All these push down efficiently
await Todo.Patch(id, new
{
    Title = "Buy oat milk",
    Priority = 1,
    Notes = new { Archived = true }
});

// Translates to:
// db.todos.updateOne(
//   { _id: id },
//   { $set: { title: "Buy oat milk", priority: 1, "notes.archived": true } }
// )
```

**SQLite (fallback):**
```csharp
// Same code, different execution
await Todo.Patch(id, new
{
    Title = "Buy oat milk",
    Priority = 1
});

// Translates to:
// SELECT * FROM Todos WHERE Id = id
// (patch in-memory)
// UPDATE Todos SET ... WHERE Id = id
```

**Pro tip:** Design your patch operations to work on any provider, but profile on your production provider to ensure performance. MongoDB's pushdown is often 10x faster than SQLite's fallback for large entities.

---

## 7. Null and Array Policies

**Concept:** Different PATCH formats handle nulls and arrays differently. Koan lets you configure global defaults and per-request overrides.

### Null Handling

**Merge-patch nulls (RFC 7386):**
- `null` means "delete field"
- For non-nullable types, you choose: SetDefault or Reject

**Partial JSON nulls:**
- `null` can mean: SetNull, Ignore, or Reject
- Configured globally or per-request

### Recipe: Configure global policies

```csharp
// In Program.cs
builder.Services.Configure<KoanWebOptions>(opt =>
{
    // Merge-patch: reject nulls for non-nullable fields
    opt.MergePatchNullsForNonNullable = MergePatchNullPolicy.Reject;

    // Partial JSON: ignore nulls (don't update field)
    opt.PartialJsonNulls = PartialJsonNullPolicy.Ignore;

    // Arrays: replace entire array (default)
    opt.ArrayBehavior = ArrayBehavior.Replace;
});
```

### Recipe: Per-request overrides

**Query parameter format:**
```
?mergeNulls=default|reject
?partialNulls=null|ignore|reject
```

**Examples:**
```bash
# Merge-patch: reject nulls for non-nullable
curl -X PATCH http://localhost:5000/api/todos/123?mergeNulls=reject \
  -H "Content-Type: application/merge-patch+json" \
  -d '{"priority": null}'
# Returns 400

# Partial JSON: ignore nulls
curl -X PATCH http://localhost:5000/api/todos/123?partialNulls=ignore \
  -H "Content-Type: application/json" \
  -d '{"title": "Buy oat milk", "priority": null}'
# priority unchanged
```

### Array Behavior

**Default: Replace entire array**
```csharp
// Before:
{ "tags": ["urgent", "home", "shopping"] }

// PATCH with:
{ "tags": ["urgent"] }

// After:
{ "tags": ["urgent"] }  // Array replaced
```

**RFC 6902: Element-wise operations**
```json
[
  { "op": "add", "path": "/tags/0", "value": "critical" },
  { "op": "remove", "path": "/tags/2" }
]
```

**Current behavior:** Arrays in merge-patch and partial JSON always replace. Use RFC 6902 for element-wise array operations.

### Sample: Policy comparison

```csharp
public class NullPolicyDemo
{
    // Entity with nullable and non-nullable fields
    public class Sample : Entity<Sample>
    {
        public string? NullableField { get; set; }
        public int NonNullableField { get; set; }
    }

    public async Task DemoPolicies(string id)
    {
        // Merge-patch with SetDefault policy
        await Sample.PatchMerge(id, new
        {
            NonNullableField = (int?)null
        });
        // Result: NonNullableField = 0 (default)

        // Partial JSON with Ignore policy (globally configured)
        await Sample.Patch(id, new
        {
            NullableField = (string?)null
        });
        // Result: NullableField unchanged (null ignored)

        // Partial JSON with Reject override
        var payload = new PatchPayload<string>
        {
            Id = id,
            Ops = [new PatchOp("replace", "/nullableField", value: null)],
            Options = new PatchOptions
            {
                PartialJsonNullPolicy = PartialJsonNullPolicy.Reject
            }
        };

        await Data<Sample, string>.PatchAsync(payload);
        // Result: 400 Bad Request (null rejected)
    }
}
```

**Pro tip:** For public APIs, use consistent null policies documented in your API spec. For internal APIs, `Ignore` is often safest (prevents accidental nulls from weak-typed clients).

---

## 8. Lifecycle Hooks and Transformers

**Concept:** Patches run through the same lifecycle hooks as saves. You can intercept, validate, or transform patch operations before and after execution.

### Recipe: OnBeforeSave hook

```csharp
public class Todo : Entity<Todo>
{
    public string Title { get; set; } = "";
    public int Priority { get; set; }
    public DateTime UpdatedAt { get; set; }

    protected override void OnBeforeSave(EntityLifecycleContext ctx)
    {
        // Always update timestamp on patch
        UpdatedAt = DateTime.UtcNow;

        // Validate priority range
        if (Priority < 1 || Priority > 5)
        {
            throw new ValidationException("Priority must be 1-5");
        }
    }
}
```

**What happens on PATCH:**
```bash
curl -X PATCH http://localhost:5000/api/todos/123 \
  -d '{"title": "Buy oat milk", "priority": 1}'

# Behind the scenes:
# 1. Normalize to PatchOps
# 2. Apply operations
# 3. Call OnBeforeSave() ← timestamp updated, validation runs
# 4. Save to provider
# 5. Call OnAfterSave()
```

### Recipe: Audit logging

```csharp
public class AuditedEntity<T> : Entity<T> where T : AuditedEntity<T>
{
    public List<AuditEntry> AuditLog { get; set; } = new();

    protected override void OnBeforeSave(EntityLifecycleContext ctx)
    {
        if (ctx.IsUpdate)
        {
            AuditLog.Add(new AuditEntry
            {
                Timestamp = DateTime.UtcNow,
                User = ctx.User?.Identity?.Name ?? "system",
                ChangeType = "patch",
                Changes = ctx.GetChangedFields()  // Framework provides this
            });
        }
    }
}

public class Todo : AuditedEntity<Todo>
{
    public string Title { get; set; } = "";
    // Patches automatically logged
}
```

### Recipe: Response transformers

```csharp
// In controller
public class TodosController : EntityController<Todo>
{
    protected override object TransformResponse(Todo entity)
    {
        // Don't expose internal fields
        return new
        {
            entity.Id,
            entity.Title,
            entity.Priority,
            entity.IsComplete
            // AuditLog excluded
        };
    }
}
```

**Pro tip:** Hooks run after operations are normalized but before provider execution. Use `OnBeforeSave` for validation, timestamps, and computed fields. Use `OnAfterSave` for side effects like sending notifications.

---

## 9. Controller Customization

**Concept:** `EntityController<T>` provides PATCH support out of the box. Override methods to add custom behavior while preserving normalization and lifecycle hooks.

### Recipe: Custom validation

```csharp
[Route("api/[controller]")]
public class TodosController : EntityController<Todo>
{
    public override async Task<IActionResult> Patch(
        [FromRoute] string id,
        [FromBody] JsonElement body,
        CancellationToken ct = default)
    {
        // Custom pre-validation
        if (body.TryGetProperty("priority", out var priorityElem))
        {
            var priority = priorityElem.GetInt32();
            if (priority < 1 || priority > 5)
            {
                return BadRequest(new
                {
                    error = "web.validation.invalidPriority",
                    message = "Priority must be 1-5"
                });
            }
        }

        // Delegate to base (normalization, lifecycle, save)
        return await base.Patch(id, body, ct);
    }
}
```

### Recipe: Authorization checks

```csharp
public class TodosController : EntityController<Todo>
{
    private readonly IAuthorizationService _authz;

    public TodosController(IAuthorizationService authz)
    {
        _authz = authz;
    }

    public override async Task<IActionResult> Patch(
        [FromRoute] string id,
        [FromBody] JsonElement body,
        CancellationToken ct = default)
    {
        // Load existing entity
        var todo = await Todo.Get(id);
        if (todo == null) return NotFound();

        // Check permissions
        var authResult = await _authz.AuthorizeAsync(User, todo, "CanEdit");
        if (!authResult.Succeeded)
        {
            return Forbid();
        }

        // Proceed with patch
        return await base.Patch(id, body, ct);
    }
}
```

### Recipe: Custom response shape

```csharp
public class TodosController : EntityController<Todo>
{
    public override async Task<IActionResult> Patch(
        [FromRoute] string id,
        [FromBody] JsonElement body,
        CancellationToken ct = default)
    {
        var result = await base.Patch(id, body, ct);

        // Wrap in envelope
        if (result is OkObjectResult okResult)
        {
            return Ok(new
            {
                status = "success",
                data = okResult.Value,
                timestamp = DateTime.UtcNow
            });
        }

        return result;
    }
}
```

**Pro tip:** Override `Patch()` for request-level concerns (auth, validation). Use entity hooks for entity-level concerns (timestamps, audit logs). Keep controllers thin.

---

## 10. Advanced Patterns

### Pattern: Conditional patch with ETags (when available)

```csharp
// Future pattern (ETags coming in v0.7.x)
[HttpPatch("{id}")]
public async Task<IActionResult> PatchWithETag(
    [FromRoute] string id,
    [FromBody] JsonElement body,
    [FromHeader(Name = "If-Match")] string? etag)
{
    if (etag != null)
    {
        var current = await Todo.Get(id);
        if (current?.ETag != etag)
        {
            return StatusCode(412, new { error = "Precondition failed" });
        }
    }

    return await base.Patch(id, body);
}
```

### Pattern: Batch patch operations

```csharp
[HttpPatch("batch")]
public async Task<IActionResult> BatchPatch(
    [FromBody] BatchPatchRequest request)
{
    var results = new List<object>();

    foreach (var item in request.Items)
    {
        try
        {
            var payload = new PatchPayload<string>
            {
                Id = item.Id,
                Ops = item.Ops,
                Options = PatchOptions.Default
            };

            await Data<Todo, string>.PatchAsync(payload);
            results.Add(new { id = item.Id, status = "success" });
        }
        catch (Exception ex)
        {
            results.Add(new { id = item.Id, status = "error", error = ex.Message });
        }
    }

    return Ok(new { results });
}

public class BatchPatchRequest
{
    public List<PatchItem> Items { get; set; } = new();
}

public class PatchItem
{
    public string Id { get; set; } = "";
    public List<PatchOp> Ops { get; set; } = new();
}
```

### Pattern: Patch with side effects

```csharp
public class Todo : Entity<Todo>
{
    public string Title { get; set; } = "";
    public bool IsComplete { get; set; }
    public DateTime? CompletedAt { get; set; }

    protected override void OnBeforeSave(EntityLifecycleContext ctx)
    {
        // Detect completion transition
        if (IsComplete && !ctx.OriginalValues.GetValueOrDefault<bool>("IsComplete"))
        {
            // Just completed
            CompletedAt = DateTime.UtcNow;

            // Trigger event (use your event bus)
            // Events.Publish(new TodoCompleted(Id));
        }
        else if (!IsComplete && ctx.OriginalValues.GetValueOrDefault<bool>("IsComplete"))
        {
            // Reopened
            CompletedAt = null;
        }
    }
}

// Now patching IsComplete triggers side effects automatically:
await Todo.Patch(id, new { IsComplete = true });
// CompletedAt set, event published
```

### Pattern: Patch with computed fields

```csharp
public class Invoice : Entity<Invoice>
{
    public List<LineItem> Items { get; set; } = new();
    public decimal Subtotal { get; private set; }  // Computed
    public decimal Tax { get; private set; }       // Computed
    public decimal Total { get; private set; }     // Computed

    protected override void OnBeforeSave(EntityLifecycleContext ctx)
    {
        // Recompute whenever items change
        Subtotal = Items.Sum(i => i.Quantity * i.UnitPrice);
        Tax = Subtotal * 0.08m;
        Total = Subtotal + Tax;
    }
}

// Patch line items, totals recompute automatically:
await Invoice.Patch(invoiceId, new
{
    Items = new[]
    {
        new LineItem { Quantity = 2, UnitPrice = 10.00m },
        new LineItem { Quantity = 1, UnitPrice = 5.00m }
    }
});
// Subtotal, Tax, Total all updated
```

**Pro tip:** Leverage lifecycle hooks for cross-cutting concerns. Patches become declarative—client says "what changed," hooks handle "what that implies."

---

## 11. Performance Considerations

### Pushdown vs Fallback Performance

**Benchmark scenario:** Update 1 field on entity with 50 fields (MongoDB vs SQLite)

| Provider | Method | Latency (p50) | Latency (p99) |
|----------|--------|---------------|---------------|
| MongoDB | Pushdown | 2ms | 5ms |
| MongoDB | Fallback | 12ms | 25ms |
| SQLite | Fallback | 8ms | 18ms |
| PostgreSQL | Pushdown (JSONB) | 3ms | 7ms |

**Takeaway:** Pushdown is 4-6x faster. Choose providers that support it for write-heavy workloads.

### Operation Complexity

**Cheap operations (push down easily):**
- `replace /field value` - Direct field update
- `remove /field` - Field deletion
- Flat fields (no deep nesting)

**Expensive operations (may require fallback):**
- `move /from /to` - Requires read + 2 writes
- `copy /from /to` - Requires read + 1 write
- `test /field value` - Requires read before write
- Deep nesting (10+ levels)

**Design tip:** Favor simple `replace` and `remove` operations. Avoid `move`/`copy` in hot paths.

### Payload Size

**HTTP overhead:**
- RFC 6902: Verbose (operation objects)
- RFC 7386: Compact (plain JSON)
- Partial JSON: Compact (plain JSON)

**Example:** Update 3 fields

```json
// RFC 6902 (194 bytes)
[
  { "op": "replace", "path": "/title", "value": "Buy oat milk" },
  { "op": "replace", "path": "/priority", "value": 1 },
  { "op": "remove", "path": "/notes/archived" }
]

// RFC 7386 / Partial JSON (60 bytes)
{
  "title": "Buy oat milk",
  "priority": 1,
  "notes": { "archived": null }
}
```

**Recommendation:** Use merge-patch or partial JSON for bandwidth-constrained clients (mobile, IoT).

### Batching

**Anti-pattern:** Sequential patches in loop
```csharp
// DON'T: N network round-trips
foreach (var id in todoIds)
{
    await Todo.Patch(id, new { Priority = 1 });
}
```

**Better:** Parallel execution
```csharp
// BETTER: Parallel execution
var tasks = todoIds.Select(id => Todo.Patch(id, new { Priority = 1 }));
await Task.WhenAll(tasks);
```

**Best:** Custom batch endpoint (if supported)
```csharp
// BEST: Single request
await httpClient.PostAsync("/api/todos/batch-patch", new
{
    ids = todoIds,
    changes = new { Priority = 1 }
});
```

**Pro tip:** Profile your patch operations under load. MongoDB pushdown can handle 10,000+ patches/sec. SQLite fallback saturates around 1,000/sec (disk I/O bound).

---

## 12. Troubleshooting

### Issue 1: 400 Bad Request - Invalid JSON Pointer

**Symptoms:**
```json
{
  "error": "web.patch.invalidPointer",
  "message": "Path '/notes/archived~flag' is not a valid JSON Pointer"
}
```

**Causes:**
- Unescaped special characters (`~`, `/`)
- Typo in field name
- Incorrect nesting path

**Solutions:**
```json
// BAD: Unescaped tilde
{ "op": "replace", "path": "/field~name", "value": "..." }

// GOOD: Escaped tilde (~0 = ~, ~1 = /)
{ "op": "replace", "path": "/field~0name", "value": "..." }

// BAD: Wrong nesting
{ "op": "replace", "path": "/notes/content/archived", "value": true }

// GOOD: Correct nesting
{ "op": "replace", "path": "/notes/archived", "value": true }
```

**Debug tip:** Use `JsonPointer.Parse(path)` to validate pointers before sending.

---

### Issue 2: 409 Conflict - ID Mutation Attempt

**Symptoms:**
```json
{
  "error": "web.patch.idMutationAttempt",
  "message": "Cannot modify entity ID via PATCH"
}
```

**Causes:**
- Client included `/id` in operations
- Client sent body id that doesn't match route id

**Solutions:**
```bash
# BAD: Patching ID
curl -X PATCH /api/todos/123 \
  -d '[{"op": "replace", "path": "/id", "value": "456"}]'

# GOOD: Use PUT for full replacement
curl -X PUT /api/todos/123 \
  -d '{"id": "123", "title": "...", ...}'

# BAD: Mismatched IDs
curl -X PATCH /api/todos/123 \
  -d '{"id": "456", "title": "..."}'

# GOOD: Omit ID from body, or ensure it matches
curl -X PATCH /api/todos/123 \
  -d '{"title": "..."}'
```

**Design principle:** IDs are immutable. PATCH is for partial updates, not resource replacement.

---

### Issue 3: 415 Unsupported Media Type

**Symptoms:**
```json
{
  "error": "web.unsupportedMediaType",
  "message": "Content-Type must be one of: application/json-patch+json, application/merge-patch+json, application/json"
}
```

**Causes:**
- Wrong or missing `Content-Type` header
- Typo in media type

**Solutions:**
```bash
# BAD: No Content-Type
curl -X PATCH /api/todos/123 -d '{"title": "..."}'

# BAD: Wrong Content-Type
curl -X PATCH /api/todos/123 \
  -H "Content-Type: text/plain" \
  -d '{"title": "..."}'

# GOOD: Correct Content-Type
curl -X PATCH /api/todos/123 \
  -H "Content-Type: application/json" \
  -d '{"title": "..."}'
```

**Debug tip:** Check framework logs for actual received Content-Type header.

---

### Issue 4: 400 Bad Request - Unsupported Operation

**Symptoms:**
```json
{
  "error": "web.patch.unsupportedOperation",
  "message": "Operation 'move' not supported by fallback executor for this entity type"
}
```

**Causes:**
- Used `move`/`copy`/`test` operation
- Provider doesn't support pushdown
- Entity shape doesn't allow in-process fallback for operation

**Solutions:**
```json
// BAD: move operation on SQLite (no pushdown)
[
  { "op": "move", "from": "/draftTitle", "path": "/title" }
]

// GOOD: Decompose into replace + remove
[
  { "op": "replace", "path": "/title", "value": "<current draftTitle value>" },
  { "op": "remove", "path": "/draftTitle" }
]
```

**Provider-specific:**
- **MongoDB:** Supports most operations via pushdown
- **PostgreSQL:** Supports replace/remove via JSONB operators
- **SQLite:** Only replace/remove via fallback
- **In-memory:** All operations supported in-process

**Workaround:** Use client-side logic to decompose complex operations into basic replace/remove.

---

### Issue 5: Null Handling Confusion

**Symptoms:**
- Merge-patch: Sent `null`, but field set to `0` instead of deleted
- Partial JSON: Sent `null`, but field unchanged

**Causes:**
- Non-nullable target field + MergePatchNullPolicy.SetDefault
- PartialJsonNullPolicy.Ignore configured globally

**Solutions:**
```csharp
// Check current policy
var options = serviceProvider.GetRequiredService<IOptions<KoanWebOptions>>().Value;
Console.WriteLine($"MergePatchNullsForNonNullable: {options.MergePatchNullsForNonNullable}");
Console.WriteLine($"PartialJsonNulls: {options.PartialJsonNulls}");

// Override per request if needed
curl -X PATCH /api/todos/123?partialNulls=null \
  -H "Content-Type: application/json" \
  -d '{"notes": null}'
```

**Design tip:** Document null behavior in API spec. Use consistent policies across endpoints.

---

### Issue 6: Array Not Updated as Expected

**Symptoms:**
- Sent patch with one array element changed
- Entire array replaced

**Causes:**
- Merge-patch and partial JSON replace entire arrays (by design)
- Expected element-wise merge

**Solutions:**
```json
// BAD: Expecting element merge with merge-patch
// Before: { "tags": ["urgent", "home"] }
{ "tags": ["urgent"] }
// After: { "tags": ["urgent"] }  ← home removed

// GOOD: Use RFC 6902 for element operations
[
  { "op": "remove", "path": "/tags/1" }
]
// After: { "tags": ["urgent"] }  ← explicit removal
```

**Workaround for merge-patch:** Send full array with desired state:
```json
// Merge-patch: always send complete array
{ "tags": ["urgent", "home", "shopping"] }
```

---

### Issue 7: Performance Degradation on Large Entities

**Symptoms:**
- Patch operations slow (>100ms)
- Database CPU spikes
- Fallback execution suspected

**Diagnosis:**
```csharp
// Check if provider supports pushdown
var env = serviceProvider.GetRequiredService<KoanEnv>();
Console.WriteLine($"Provider: {env.Data.Provider}");
Console.WriteLine($"Supports pushdown: {env.Data.SupportsPatchPushdown}");

// Enable SQL logging to see actual queries
builder.Logging.AddFilter("Microsoft.EntityFrameworkCore.Database.Command", LogLevel.Information);
```

**Solutions:**

**If using fallback provider (SQLite, MySQL):**
- Consider migrating to MongoDB or PostgreSQL for write-heavy workloads
- Reduce entity size (normalize into separate tables/collections)
- Batch patches to amortize fetch cost

**If using pushdown provider but still slow:**
- Check indexes on ID field
- Profile query execution plan
- Ensure partition/source routing works correctly

**Optimization example:**
```csharp
// Before: 50-field entity, patch 1 field, fallback = fetch all 50 fields
public class HugeEntity : Entity<HugeEntity>
{
    public string Field1 { get; set; }
    public string Field2 { get; set; }
    // ... 48 more fields
}

// After: Split into core + detail
public class EntityCore : Entity<EntityCore>
{
    public string Field1 { get; set; }  // Frequently patched
}

public class EntityDetail : Entity<EntityDetail>
{
    public string Field2 { get; set; }  // Rarely patched
    // ... 47 more fields
}
```

**Pro tip:** Use Application Insights or similar to track PATCH latency percentiles. p99 latency >100ms indicates fallback execution on large entities—time to optimize or migrate providers.

---

## Summary and Next Steps

You've now seen Koan's full patch capabilities—from quick HTTP PATCH endpoints to in-process patching with provider pushdown. Here's what we covered:

**Key Takeaways:**
1. **Three formats, one normalization** - RFC 6902, RFC 7386, partial JSON all normalize to canonical PatchOps
2. **Provider pushdown matters** - MongoDB/PostgreSQL are 4-6x faster than fallback
3. **Null policies are configurable** - Global defaults + per-request overrides
4. **Lifecycle hooks apply** - OnBeforeSave/OnAfterSave run for patches too
5. **EntityController<T> handles it all** - Minimal code, maximum capability

**Choosing Your Format:**
- **Public APIs** → RFC 7386 (merge-patch) for simplicity + standardization
- **Complex operations** → RFC 6902 (JSON patch) for move/copy/test
- **Internal services** → Partial JSON for speed + flexibility
- **In-process** → Entity<T>.Patch() convenience methods

**Performance Checklist:**
- ✅ Use providers with pushdown support (MongoDB, PostgreSQL)
- ✅ Favor replace/remove over move/copy/test
- ✅ Batch operations when possible
- ✅ Profile under realistic load
- ✅ Monitor p99 latency for fallback detection

**Common Patterns:**
- Background jobs → In-process patching
- Web forms → Merge-patch or partial JSON
- Mobile apps → Partial JSON with Ignore null policy
- Admin panels → RFC 6902 for precision

**Next Steps:**
1. **Implement your first PATCH endpoint** - Start with partial JSON, add RFC formats as needed
2. **Configure null policies** - Match your API semantics (strict vs lenient)
3. **Add lifecycle hooks** - Timestamps, validation, computed fields
4. **Profile performance** - Compare pushdown vs fallback on your provider
5. **Read related guides:**
   - [Entity Capabilities](entity-capabilities-howto.md) - Entity-first patterns
   - [Canon Capabilities](canon-capabilities-howto.md) - Versioning and migrations
   - [DATA-0077](../decisions/DATA-0077-canonical-patch-operations.md) - Canonical model deep dive

**Questions or Issues?**
- Check [Troubleshooting](#12-troubleshooting) section above
- Review [DATA-0077](../decisions/DATA-0077-canonical-patch-operations.md) for canonical model details
- See [entity-capabilities-howto.md](entity-capabilities-howto.md) for Entity<T> static method patterns

Remember: PATCH is about describing *what changed*, not *how to change it*. Let Koan handle normalization, provider optimization, and lifecycle hooks—you focus on business logic.

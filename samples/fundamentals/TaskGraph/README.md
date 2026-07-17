# TaskGraph — tasks and relationships

TaskGraph is the smallest complete example of a business model whose relationships remain equally readable for one entity, a finite set, and an asynchronous stream.

```text
User ─────┐
          ├──> Todo ──> TodoItem
Category ─┘
```

## Run to a meaningful result

From the repository root:

```powershell
dotnet run --project samples/fundamentals/TaskGraph -- --urls http://localhost:5000
```

Open <http://localhost:5000>, select **Reset the story**, and inspect the same relationship context across all three cardinalities.

The API path is just as short:

```http
POST http://localhost:5000/api/todos/reset-demo
GET  http://localhost:5000/api/todos/todo-proposal/context
GET  http://localhost:5000/api/todos/relationships/set?limit=3
GET  http://localhost:5000/api/todos/relationships/stream?limit=3
```

`reset-demo` is deliberately destructive and deterministic. It replaces only this sample's four collections with two people, two categories, three tasks, and four task items.

## Read the application

The host contains only Koan's bootstrap contract:

```csharp
var builder = WebApplication.CreateBuilder(args);
builder.Services.AddKoan();
var app = builder.Build();
await app.RunAsync();
```

Relationships are declared where the business says they exist:

```csharp
public sealed class Todo : Entity<Todo>
{
    [Parent(typeof(User))]
    public string UserId { get; set; } = string.Empty;

    [Parent(typeof(Category))]
    public string CategoryId { get; set; } = string.Empty;
}
```

CRUD exposure remains a declaration:

```csharp
[Route("api/users")]
public sealed class UserController : EntityController<User>;
```

The relationship operation keeps the same intent as cardinality changes:

```csharp
await todo.Relatives(ct);
await todos.Relatives(ct);
Todo.AllStream(batchSize: 2).Relatives(ct);
```

Data.Core owns relationship discovery, batching, backend query negotiation, source ordering, and graph construction. The controller owns only the sample's bounded HTTP shape.

## Referenced capability, activated behavior

The project references SQLite, Web.Extensions, and Cache. Koan discovers and composes them during `AddKoan()`:

- SQLite becomes the sole data provider, so entities need no provider annotation.
- Web.Extensions brings the web capability that discovers `EntityController<T>` declarations and serves the dashboard.
- `[Cacheable(120)]` opts `Category` into the cache pipeline; entities without the attribute remain ordinary data entities.

The source checkout also emits [koan.lock.json](koan.lock.json) through the repository's shared source-app policy. Package consumers receive the same composition target transitively from Koan.Core.

## Inspect what Koan decided

- Runtime facts: <http://localhost:5000/.well-known/Koan/facts>
- Health: <http://localhost:5000/api/health>
- Repeatable requests: [requests.http](requests.http)

Startup reporting and the facts document expose selected modules, providers, capability decisions, and any failed collections. The dashboard depends only on the public HTTP surface; it has no private testing hooks.

## Project map

- `Program.cs` — application identity and Koan bootstrap
- `User.cs`, `Category.cs`, `Todo.cs`, `TodoItem.cs` — business model
- `*Controller.cs` — declared CRUD plus the three relationship demonstrations
- `wwwroot/` — small, dependency-free executable explanation
- `requests.http` — the complete API journey

# Quick Start Part 2 — Add another database and copy data!
What's happening here:

- `[DataAdapter("mongo")]` selects the Mongo provider for the TodoDoc model.
- `[Storage]` maps to provider-appropriate concepts: `Name` is the Collection for Mongo and Table for relational, while `Namespace` is the database.

## 4) Copy data from SQLite -> Mongo

Now for the fun part — let's create a tiny migration helper you can run once. Here's a simple static method you can call from anywhere after the app boots: part, you'll discover how to: (1) add MongoDB as another data store, (2) map a model to Mongo with attributes, and (3) copy data from SQLite to Mongo.

## 1) Add the Mongo adapter

Install the package — and here's the beautiful part: no extra wiring needed, it self-registers when referenced! Start Part 2 — Add another database and copy data!

In this part, you’ll: (1) add MongoDB as another data store, (2) map a model to Mongo with attributes, and (3) copy data from SQLite to Mongo.

## 1) Add the Mongo adapter

Install the package (again, no extra wiring needed; it self-registers when referenced):

```bash
dotnet add package Sora.Data.Mongo
```

## 2) Point Sora to your Mongo database

Add MongoDB to your existing config in appsettings.json:

```json
{
  "ConnectionStrings": {
    "sqlite": "Data Source=C:\\data\\myapp.db",     // ← existing from Part 1
    "mongodb": "mongodb://localhost:27017"         // ← new for Part 2
  }
}
```

Environment variable alternatives:

- ConnectionStrings__mongodb=mongodb://localhost:27017

## 3) Choose the database per model with attributes

Here's where it gets interesting — use attributes to direct a model to Mongo and specify its storage mapping. Models without an explicit adapter keep using your default (SQLite from Part 1/Module 2).

```csharp
using Sora.Data.Abstractions.Annotations;
using Sora.Domain;

// Stays on the default adapter (SQLite)
[DataAdapter("sqlite")]
public class Todo : Entity<Todo>
{
    public string Title { get; set; } = string.Empty;
    public bool IsDone { get; set; }
}

// A Mongo-backed read/reporting projection
[DataAdapter("mongo")]
[Storage(Name = "TodoProjection", Namespace="ReportDb")]
public class TodoDoc : Entity<TodoDoc>
{
    public string Title { get; set; } = string.Empty;
    public bool IsDone { get; set; }
}
```

What’s happening:

- [DataAdapter("mongo")] selects the Mongo provider for the TodoDoc model.
- [Storage] maps to provider-appropriate concepts: `Name` is the Collection for Mongo and Table for relational, while `Namespace` is the database.

## 4) Copy data from SQLite -> Mongo

Let's create a tiny migration helper you can run once (for example, in a background task or a throwaway console command). Here’s a simple static method you can call from anywhere after the app boots:

```csharp
public static class Seed
{
    public static async Task CopyTodosToMongo(CancellationToken ct = default)
    {
        // 1) Read from the default-backed Todo (SQLite)
        var todos = await Todo.All(ct);

        // 2) Project into the Mongo-backed model
        var docs = todos.Select(t => new TodoDoc { 
                            Id = t.Id, 
                            Title = t.Title, 
                            IsDone = t.IsDone }
                        );

        // 3) Bulk upsert into Mongo
        await docs.Save(ct);

        Console.WriteLine($"Migrated {todos.Count()} docs to Mongo.");
    }
}
```

Now, you can even call this once after app startup — let's add a simple admin endpoint to your controller:

```csharp
[HttpGet("migrate-to-mongo")]
public async Task<IActionResult> MigrateTodos()
{
    await Seed.CopyTodosToMongo();
    return Ok("Migration complete");
}
```

That's it! No complex migration frameworks or ceremony — just straightforward data movement.

Next: Ready for production-grade APIs? Continue with [Module 3 - Production APIs](03-proper-apis.md), where you'll discover custom controllers, validation, and error handling.

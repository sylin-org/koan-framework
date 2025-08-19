# Quick Start Part 2 — Add another database (Mongo) and copy data between stores

In this part, you’ll: (1) add MongoDB as another data store, (2) map a model to Mongo with attributes, and (3) copy data from SQLite to Mongo.

## 1) Add the Mongo adapter

Install the package (no extra wiring needed — it self-registers when referenced):

```bash
dotnet add package Sora.Data.Mongo
```

## 2) Point Sora to your Mongo database

Minimal config via appsettings.json (keep your existing SQLite connection for the default store):

```json
{
  "ConnectionStrings": {
    "Default": "Data Source=C:\\data\\myapp.db",
    "reporting": "mongodb://localhost:27017"
  },
  "Sora": {
    "Data": {
      "Mongo": {
        "Database": "sora_dev"  // default database for Mongo (optional)
      },
      "Sources": {
        "reporting": {
          "mongo": {
            "Database": "reportsdb"
            // ConnectionString omitted -> falls back to ConnectionStrings:reporting
          }
        }
      }
    }
  }
}
```

Environment variable alternatives:

- ConnectionStrings__reporting=mongodb://localhost:27017
- Sora__Data__Sources__reporting__mongo__Database=reportsdb

## 3) Choose the database per model with attributes

Use attributes to direct a model to Mongo and specify its storage mapping. Models without an explicit adapter keep using your default (SQLite from Part 1/Module 2).

```csharp
using Sora.Data.Abstractions.Annotations;
using Sora.Domain;

// Stays on the default adapter (SQLite)
public class Todo : Entity<Todo>
{
    public string Title { get; set; } = string.Empty;
    public bool IsDone { get; set; }
}

// A Mongo-backed read/reporting projection
[DataAdapter("mongo")]           // choose the provider
[DataSource("reporting")]        // logical source name -> resolves connection by convention
[Storage(Namespace = "reportsdb", // database (optional if configured at Sora:Data:Mongo:Database)
         Name = "todos")]        // collection name
public class TodoDoc : Entity<TodoDoc>
{
    public string Title { get; set; } = string.Empty;
    public bool IsDone { get; set; }
}
```

What’s happening:

- [DataAdapter("mongo")] selects the Mongo provider for this model.
- [DataSource("reporting")] tells Sora to use the named source “reporting”.
  - Connection resolution falls back to ConnectionStrings:reporting if Sora:Data:Sources:reporting:mongo:ConnectionString is not set.
- [Storage] maps to provider-appropriate concepts: Database/Collection for Mongo; Schema/Table for relational.

## 4) Copy data from SQLite -> Mongo

Create a tiny migration helper you can run once (for example, in a background task or a throwaway console command). Here’s a simple static method you can call from anywhere after the app boots:

```csharp
using Sora.Data.Core; // for Data<TEntity, TKey>

public static class Seed
{
    public static async Task CopyTodosToMongo(CancellationToken ct = default)
    {
        // 1) Read from the default-backed Todo (SQLite)
        var todos = await Todo.All(ct);

        // 2) Project into the Mongo-backed model
        var docs = todos.Select(t => new TodoDoc { Title = t.Title, IsDone = t.IsDone });

        // 3) Bulk upsert into Mongo
        var upserted = await Data<TodoDoc, string>.UpsertManyAsync(docs, ct);
        Console.WriteLine($"Migrated {upserted} docs to Mongo.");
    }
}
```

Call it once (choose one option):

- Add a temporary endpoint in your controller for local use.
- Add a hosted service that runs on startup and then disables itself.
- Run it from a console app referencing the same project.

## Recap

- Add Sora.Data.Mongo to enable Mongo support.
- Use attributes to steer models per provider and per source.
- Storage maps cleanly across providers (table/collection) with [Storage].
- You can move data by querying from one model and upserting into another — with one line for the bulk save.

Next: continue with Production APIs in [Module 3](03-proper-apis.md).

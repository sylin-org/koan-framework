# Sylin.Koan.Cache.Adapter.Sqlite

Persistent Local Cache provider for Koan, backed by SQLite. Reference it and keep the normal `AddKoan()` boot;
SQLite automatically wins Local election over the built-in memory floor.

## Install

```powershell
dotnet add package Sylin.Koan.Cache.Adapter.Sqlite
```

```csharp
builder.Services.AddKoan();

[Cacheable(300)]
public sealed class Todo : Entity<Todo> { }
```

No SQLite registration is required. The default database is `.Koan/cache/cache.db`.

## Meaningful result

Run the application, save and read an Entity, then restart it. The elected Local cache remains queryable from the
SQLite file without changing Entity code.

## Configure only when needed

```json
{
  "Koan": {
    "Cache": {
      "Adapters": {
        "Sqlite": {
          "DatabasePath": "./state/cache.db"
        }
      }
    }
  }
}
```

The provider supports exact case-insensitive tags, binary payloads, bounded stale serving, durable persistence, and
real sliding expiration. An existing cache file is brought to the current schema when the store opens it.

## Guarantees and limits

SQLite is a Local tier: it persists across process restarts but is not a shared multi-node cache. Use it when one
process needs durable local caching or offline-friendly behavior. Add a Remote adapter for a shared layer.

Boot reporting exposes the selected provider and database path. `CacheHealthCheck` performs a real sentinel
write/read/remove against the selected tier.

See [TECHNICAL.md](TECHNICAL.md) for schema and migration behavior.

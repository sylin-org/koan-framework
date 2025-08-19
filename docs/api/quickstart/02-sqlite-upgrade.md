# SQLite Upgrade

Upgrade your TaskFlow API from JSON storage to SQLite for persistence and better querying.

## Why SQLite
- Zero server setup
- File-based persistence
- Great for dev and small apps

## Install Package

```bash
dotnet add package Sora.Data.Sqlite
```

## Configuration

Point Sora to your real SQLite database using appsettings or env vars:

- appsettings.json
```json
{
  "ConnectionStrings": {
    "Default": "Data Source=.\\App_Data\\taskflow.db"
  }
}
```

- or environment variable
```
ConnectionStrings__Default=Data Source=.\\App_Data\\taskflow.db
```

Sora auto-discovers the Sqlite adapter when the package is referenced. No explicit AddSqliteAdapter call is needed.

## Model & Controller

Your entity and controller stay the same. Sora routes work unchanged.

## Run & Verify

```bash
dotnet run
```

Create a task, stop the app, run again — data persists now.

## Next steps

- Continue to [Module 2B: Add another database (Mongo) and copy data between stores](02b-mongo-and-migration.md)
- Then go to Production APIs → [Module 3](03-proper-apis.md)

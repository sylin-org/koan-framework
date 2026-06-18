# KoanConsoleApp

A minimal [Koan](https://github.com/sylin-org/Koan-framework) console app: entity CRUD over Sqlite, no boilerplate.

## Run it

```bash
dotnet run
```

```
saved: 01J...
loaded: buy milk
open todos:
  - buy milk
```

## What's in here

| File | What it is |
|---|---|
| `Program.cs` | `new ServiceCollection().StartKoan()` boots the framework (loads `appsettings.json`, runs discovery, sets the ambient host), then `Save` / `Get` / `Query` over the `Todo` entity. |
| `Todo.cs` | `Entity<Todo>` — GUID v7 id auto-generated on `Save()`; static `Get`/`Query`/`All` + instance `Save`/`Remove` come from the base. |
| `appsettings.json` | Sqlite is the default data source (`Data Source=./app.db`). |

## Serialization defaults

JSON uses **Newtonsoft.Json** (the framework canon), **camelCase**, **nulls omitted** — global, no setup.

## Next steps

- Add a property to `Todo`, `dotnet run` again — the store follows.
- Move to Postgres/Mongo/etc.: reference that connector and point `appsettings.json` at it; the entity code doesn't change.
- Want HTTP? `dotnet new koan-web` gives you the same entity behind an auto-mapped REST API.
- Pillar maps (one screen each): [`docs/reference/cards`](https://github.com/sylin-org/Koan-framework/tree/main/docs/reference/cards).

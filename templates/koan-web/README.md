# KoanWebApp

A minimal [Koan](https://github.com/sylin-org/Koan-framework) web API. One entity, one controller, a full REST surface — over Sqlite.

## Run it

```bash
dotnet run
```

Then:

```bash
curl http://localhost:5000/api/todos                                   # []
curl -X POST http://localhost:5000/api/todos -H "content-type: application/json" -d '{"title":"buy milk"}'
curl http://localhost:5000/api/todos                                   # [{ "id": "01J...", "title": "buy milk", "done": false }]
```

## The boot report is the demo

On startup Koan prints a self-describing boot report — it *is* your confirmation that everything wired up. You'll see your data adapter, the auto-mapped controllers, and health at a glance (illustrative):

```
Koan  v0.17.x  (pre-1.0)
  Data         sqlite (Default)                         [OK]
  Web          controllers auto-mapped                  [OK]
               EntityController<Todo> -> /api/todos
  Health       probes pending (registered=...)
```

If a piece is missing or misconfigured, the report says so loudly — that's the framework's character: self-describing honesty, not a green badge.

## What's in here

| File | What it is |
|---|---|
| `Program.cs` | The canonical 4 lines: `CreateBuilder` -> `AddKoan()` -> `Build()` -> `Run()`. `AddKoan()` discovers the referenced packages and wires them (Reference = Intent). |
| `Todo.cs` | `Entity<Todo>` — GUID v7 id auto-generated on `Save()`; static `Get`/`Query`/`All` + instance `Save`/`Remove` come from the base. |
| `TodosController.cs` | `EntityController<Todo>` — the full REST surface (list/get/create/update/patch/delete + `POST /query`). |
| `appsettings.json` | Sqlite is the default data source (`Data Source=./app.db`). Swap the adapter here to move stores. |

## Serialization defaults (so they're not a surprise)

JSON uses **Newtonsoft.Json** (the framework canon), configured **camelCase** with **nulls omitted**. So `Title` serializes as `title`, and a null property simply isn't emitted. This is deliberate and global — no per-endpoint setup.

## Next steps

- Add a property to `Todo`, `dotnet run`, POST again — the store and API follow automatically.
- Add a second entity + controller the same way (two files).
- Query from the API: `GET /api/todos?filter={"done":false}&sort=-title&page=1&size=20`.
- Move to Postgres/Mongo/etc.: reference that connector package and point `appsettings.json` at it — the entity code doesn't change.
- Pillar maps (one screen each): the framework's [`docs/reference/cards`](https://github.com/sylin-org/Koan-framework/tree/main/docs/reference/cards).

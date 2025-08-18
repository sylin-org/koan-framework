# Sora Framework

Sora is a modern, modular .NET framework for building applications with clarity and flexibility—heavily inspired by Zen’s best ideas, with simpler contracts and better DX.

- .NET 9 first. Minimal magic, clear composition.
- Modular data adapters (JSON for dev, SQLite via Dapper), web bootstrap, and pragmatic “escape hatches.”
- Always-on discovery (with a production warning); explicit config always wins.

See `docs/` for design/ADRs. Start with `docs/00-index.md` and `docs/08-engineering-guardrails.md`.

Layering defaults: logging defaults live in Core, and secure headers in Web (see `docs/decisions/0011-logging-and-headers-layering.md`).

New to Sora? Read the step-by-step `docs/11-getting-started.md`.

## Features

- Data abstractions: `IDataRepository<TEntity, TKey>`, `IBatchSet`, capability flags.
- Built-in adapters: JSON (dev), SQLite (Dapper) with schema sync and optional string queries.
- Repo pipeline conveniences: identity assignment for string/Guid keys, batch helpers.
- Instruction API: execute provider-specific instructions safely (e.g., raw SQL nonquery/scalar).
- Web module: minimal auto-wiring for controllers/static files and health.
- Discovery & precedence: modules self-register; production logs a warning; explicit config overrides discovery.
- Provider priority: `ProviderPriorityAttribute` selects default adapter when more than one is present.

## Requirements

- .NET 9 SDK
- Optional: Docker/WSL2 for containerized samples

## Quickstart

Build and run the S0 console sample (JSON adapter):

```powershell
dotnet build
dotnet run --project .\samples\S0.ConsoleJsonRepo\S0.ConsoleJsonRepo.csproj
```

Run the S1 web sample (SQLite optional):

```powershell
dotnet run --project .\samples\S1.Web\S1.Web.csproj
```

- SQLite adapter self-registers via discovery. To set a connection string explicitly:

```csharp
// in Program.cs (S1.Web)
// builder.Services.AddSqliteAdapter(o => o.ConnectionString = "Data Source=.\\data\\s1.sqlite");
```

## Using data APIs

Terse static facade for common string-key entities:

```csharp
// Upsert and fetch
var todo = await new Todo { Title = "buy milk" }.Save();
var item = await Todo.Get(todo.Id);

// Batch
var result = await Todo.Batch()
	.Add(new Todo { Title = "task 1" })
	.Update(todo.Id, t => t.Title = "buy more" )
	.Save();
```

SQLite optional string queries (safe parameterization via Dapper):

```csharp
var items = await Sora.Data.Core.Data<Todo, string>.Query("Title LIKE '%milk%'"); // WHERE suffix
// or full SELECT if preferred
var items2 = await Sora.Data.Core.Data<Todo, string>.Query("SELECT Id, Title FROM Todo WHERE Title LIKE '%milk%'");
```

Instruction escape hatch (SQL): see `docs/10-execute-instructions.md`.

More walkthroughs: `docs/11-getting-started.md`.

## Tests

```powershell
dotnet test -c Release -v minimal
```

CI runs on push via `.github/workflows/ci.yml`.

## Contributing

We welcome issues and PRs. Start with `docs/support/03-adapter-checklist.md` and `docs/08-engineering-guardrails.md`.

DCO sign-off: include a Signed-off-by line in each commit. Example:

Signed-off-by: Your Name <your.email@example.com>

See the `DCO` file for the certificate text.

## License

- Code: Apache License 2.0 — see `LICENSE` and `NOTICE`.
- Documentation: Creative Commons Attribution 4.0 — see `docs/LICENSE-DOCS.md`.

Trademarks: see `TRADEMARKS.md` for simple usage guidelines.

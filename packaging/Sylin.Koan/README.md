# Sylin.Koan

The Koan foundation for console applications, workers, and modules: Core, the Entity data grammar, local Events and
Transport, and a bounded JSON provider for an immediate persistent result.

## Reference

```powershell
dotnet add package Sylin.Koan
```

Koan remains source-first until its first coherent package wave is published and observed. Today this command is
executable against a feed containing the locally compiled candidate; it is also the exact post-publication contract.

## Meaningful result

```csharp
// Program.cs
using Koan.Data.Abstractions;
using Koan.Data.Core;
using Koan.Data.Core.Model;
using Microsoft.Extensions.DependencyInjection;

using var app = new ServiceCollection().StartKoan();

var todo = await new Todo { Title = "buy milk" }.Save();
Console.WriteLine((await Todo.Get(todo.Id))?.Title);

public sealed class Todo : Entity<Todo>
{
    public string Title { get; set; } = "";
}
```

`StartKoan()` owns the standard Generic Host lifecycle. The Entity states the business shape and carries `Save`,
`Get`, `Query`, `All`, and `Remove`; no application repository or provider registration is required.

## Defaults and capability addition

The JSON floor persists under `./data`. Startup output explains the composed modules, selected provider, defaults,
and corrections. Local `model.Events` and `model.Transport` semantics are available in-process without a network
adapter.

Upgrade to durable embedded SQLite with one reference and no code or configuration change:

```powershell
dotnet add package Sylin.Koan.Data.Connector.Sqlite
```

Add `Sylin.Koan.App` instead when the application needs controller-based ASP.NET Core projection. Add provider or
capability packages only when the application intends to use them; package references are the composition language.

## Boundaries and corrections

- JSON is appropriate for bounded local data, seeds, and smoke scenarios. It is not a multi-process, high-volume,
  provider-streaming, distributed-durability, or backup guarantee.
- Local Events and Transport do not imply network delivery. Referencing a transport provider adds its stated
  delivery and topology characteristics while preserving the Entity-facing semantics.
- Remote providers still require resolvable endpoints and credentials. Koan derives safe local defaults where the
  provider supports them and reports rejected or missing intent through startup facts and health.
- This package is dependency-only: it contains no runtime assembly and intentionally emits no symbol package.

Use `Sylin.Koan.Templates` for an ordinary `dotnet new koan-console` project.

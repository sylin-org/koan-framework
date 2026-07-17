# KoanWebApp

A persisted Todo API whose application code states three Koan intents: bootstrap, Entity, and controller.

## Run

```powershell
dotnet run
```

Use the URL ASP.NET Core prints:

```powershell
Invoke-RestMethod -Method Post -Uri http://localhost:5000/api/todos -ContentType application/json -Body '{"title":"buy milk"}'
Invoke-RestMethod http://localhost:5000/api/todos
```

## Read the application

| File | Business meaning |
|---|---|
| `Todo.cs` | the state the application owns |
| `TodosController.cs` | expose Todos through the standard Entity API |
| `Program.cs` | compose referenced Koan capabilities |
| `KoanWebApp.csproj` | choose the Koan web entry and durable embedded SQLite provider |

SQLite is elected from the package reference and defaults to `.koan/data/Koan.sqlite`. No provider registration,
connection setting, schema script, repository, or endpoint mapping is required. Startup output and
`/.well-known/Koan/facts` explain the resulting composition.

Add a property to `Todo` or another Entity/controller pair and run again. To move backends, reference the intended
provider and configure only the endpoint or credentials it cannot derive; the business code does not change.

This is not a production security template. Authorization, validation, tenancy, backup, public API design, and the
chosen provider's deployment guarantees remain application decisions.

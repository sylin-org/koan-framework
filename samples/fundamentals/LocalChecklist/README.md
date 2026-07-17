# LocalChecklist — one local checklist

LocalChecklist is Koan's smallest console sample: one Entity model, one standard host, and one durable JSON provider selected by reference.

## Run to a meaningful result

From the repository root:

```powershell
dotnet run --project samples/fundamentals/LocalChecklist
```

The app replaces its small demo collection, saves three checklist items, completes one, reloads the open work, and exits cleanly:

```text
Checklist ready: 3 total, 1 complete, 2 open.
 - Review the release notes
 - Walk the dog
```

The boot report appears first. It should show the JSON data provider, the in-process Communication floor supplied by the foundation bundle, and `lockfile ok`.

## Read the application

The console host is one ordinary owned .NET lifetime:

```csharp
using var app = new ServiceCollection().StartKoan();
```

The model owns its business state and verb:

```csharp
public sealed class Todo : Entity<Todo>
{
    public string Title { get; set; } = string.Empty;
    public bool Done { get; set; }

    public async Task Complete(CancellationToken ct = default)
    {
        Done = true;
        await this.Save(ct);
    }
}
```

Application code uses the same Entity language for a collection and a query:

```csharp
await checklist.Save();
await checklist[0].Complete();
var open = await Todo.Query(todo => !todo.Done);
```

No repository, service layer, provider selector, host adapter, or manual discovery is required.

## Reference = Intent

The source project references the `Sylin.Koan` foundation bundle. Its public package supplies Core, Entity data, local Communication, and the JSON provider. Because JSON is the only data provider intent, no Entity annotation or configuration chooses it again.

JSON writes one file per Entity type under `./data` by default. Standard configuration can override `Koan:Data:Json:DirectoryPath`; the executable test uses that key to isolate its filesystem without adding a test hook to the application.

## Honest JSON boundary

JSON supports local persistence and bounded materialized operations such as `Get`, `All`, and `Query`. It does not advertise provider-bounded paging, so `AllStream()` and `QueryStream()` deliberately throw a corrective `QueryStreamRejectedException`.

Use a streaming-capable adapter when the business requires provider-bounded streaming. Do not treat an in-memory scan of an arbitrarily large JSON file as a stream guarantee.

## Project map

- `Program.cs` — deterministic checklist journey and console presentation
- `Todo.cs` — Entity state plus the `Complete()` business operation
- `LocalChecklist.csproj` — the single foundation capability reference
- `koan.lock.json` — reviewable static composition emitted on build

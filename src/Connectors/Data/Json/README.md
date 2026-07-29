# Sylin.Koan.Data.Connector.Json

Persist ordinary Koan Entities in inspectable local JSON files without running a database server.

## Use it

```powershell
dotnet add package Sylin.Koan.Data.Connector.Json
```

Keep the normal Koan bootstrap and Entity API:

```csharp
builder.Services.AddKoan();

public sealed class Todo : Entity<Todo>
{
    public string Title { get; set; } = "";
}

var saved = await new Todo { Title = "Ship" }.Save(ct);
var same = await Todo.Get(saved.Id, ct);
```

The managed default directory is `data`. Choose another root only when placement matters:

```json
{
  "Koan": {
    "Data": {
      "Json": { "DirectoryPath": "state" }
    }
  }
}
```

Named sources use the standard source grammar and may choose their own `json:DirectoryPath`.

## What succeeds

- Managed/read-write use creates the directory and Entity file on the first write.
- Every read returns a detached Entity. Editing it changes nothing until `Save()` succeeds.
- A write builds a complete candidate file, replaces the target, then publishes the new live snapshot.
- Bulk upsert and delete each perform one physical file replacement.
- A new Koan host restores root/variant identity and managed isolation fields from disk.
- Two source path spellings that resolve to the same canonical file share one in-process snapshot and write gate.

## Boundaries and failures

- Corrupt JSON, duplicate identities, incompatible Entity roots, and files larger than 64 MiB never become an empty
  successful store.
- Read-only writes fail before filesystem mutation.
- `External` requires an existing directory and Entity file; Koan never provisions either.
- Physical `Map<T>` declarations reject because the connector owns one Entity-array file shape.
- Required atomic batches and provider-bounded Entity streams reject before partial work.
- The 1,025th canonical Entity/partition file in one host rejects; the fixed 1,024-file ceiling keeps host state finite.

## Choose a database connector when

You need multi-process writers, transactions, indexes, provider-side queries, crash-recovery guarantees, files above
64 MiB, or dynamically unbounded partitions. JSON is a deliberately small local persistence floor. It does not watch
for external edits after a file enters the host cache, and lexical path canonicalization does not promise to collapse
every symlink alias.

Use `FirstPage`/`Page` to limit results returned to application code. `AllStream` and `QueryStream` remain unavailable
because loading a whole file before yielding would not be provider-bounded streaming.

See [TECHNICAL.md](TECHNICAL.md) for the exact storage and capability contract.

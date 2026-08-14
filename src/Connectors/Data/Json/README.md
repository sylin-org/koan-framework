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

By default, each Entity set remains one aggregate JSON array. Choose independent JSON object files when the file is
itself part of an application workflow such as Git review or selective publication:

```json
{
  "Koan": {
    "Data": {
      "Sources": {
        "Default": {
          "Adapter": "json",
          "json": {
            "DirectoryPath": "/workspace/src/writing",
            "Layout": "IndividualFiles",
            "IndividualFilePath": "{id}/article.json"
          }
        }
      }
    }
  }
}
```

`IndividualFilePath` defaults to `{storage}/{id}.json`. It must be a relative `.json` path containing exactly one
`{id}` token; `{storage}` is optional for a dedicated, unpartitioned source and required when one source must isolate
multiple Entity roots or partitions. Token values are encoded as safe path segments.

## What succeeds

- Managed/read-write use creates the required directories and Entity file on the first write.
- Every read returns a detached Entity. Editing it changes nothing until `Save()` succeeds.
- Aggregate writes build a complete candidate file, replace the target, then publish the new live snapshot.
- Aggregate bulk upsert and delete each perform one physical file replacement.
- Individual writes replace only the addressed Entity file and observe external file edits on the next read.
- Individual delete removes only the Entity file; it never removes its containing directory or sibling application files.
- A new Koan host restores root/variant identity and managed isolation fields from disk.
- In Aggregate layout, two source path spellings that resolve to the same canonical file share one in-process
  snapshot and write gate. IndividualFiles uses a bounded host-wide gate pool and retains no per-record snapshot.

## Boundaries and failures

- Corrupt JSON, duplicate identities, incompatible Entity roots, identity/path mismatches, unsafe path templates, and
  files larger than 64 MiB never become an empty successful store.
- Read-only writes fail before filesystem mutation.
- `External` requires an existing directory and never provisions directories or Entity files.
- Physical `Map<T>` declarations reject because the connector owns its JSON file shapes.
- Required atomic batches and provider-bounded Entity streams reject before partial work.
- In Aggregate layout, the 1,025th canonical Entity/partition file in one host rejects; the fixed 1,024-file ceiling
  keeps aggregate snapshots finite. IndividualFiles retains no record-count-proportional registry.

## Choose a database connector when

You need multi-process writers, transactions, indexes, provider-side queries, crash-recovery guarantees, individual
records above 64 MiB, or dynamically unbounded partitions. JSON is a deliberately small local persistence floor.
Aggregate layout does not watch for external edits after a file enters the host cache; IndividualFiles reads the
addressed document from disk but does not provide cross-process compare-and-swap. Lexical path canonicalization does
not promise to collapse every symlink alias.

Use `FirstPage`/`Page` to limit results returned to application code. `AllStream` and `QueryStream` remain unavailable
because loading a whole file before yielding would not be provider-bounded streaming.

See [TECHNICAL.md](TECHNICAL.md) for the exact storage and capability contract.

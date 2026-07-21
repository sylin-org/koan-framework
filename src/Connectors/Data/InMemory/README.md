# Sylin.Koan.Data.Connector.InMemory

Use this connector for fast, process-local Entity persistence in tests, conformance suites, and
explicitly ephemeral development workflows.

## Choose it when

- losing all data at process exit is acceptable;
- a test needs a real `AddKoan()` provider without files, containers, or remote services; or
- provider-neutral behavior needs a deterministic in-memory oracle.

Do not choose it for durable application state, cross-process sharing, or production recovery.

## Install

```powershell
dotnet add package Sylin.Koan.Data.Connector.InMemory
```

The direct package reference expresses intent to use the ephemeral provider. Keep the application's ordinary Koan
bootstrap; there is no provider-specific registration:

```csharp
builder.Services.AddKoan();
```

## Meaningful result

Define an Entity and use its normal persistence verbs:

```csharp
public sealed class Todo : Entity<Todo>
{
    public string Title { get; set; } = "";
}

var saved = await new Todo { Title = "Prove the rule" }.Save();
var same = await Todo.Get(saved.Id);
```

The saved value is available to every repository in the same Koan host and disappears when that host exits. No files,
container, or remote service are created.

## Selection

The provider identity is `inmemory` (`memory` is also recognized). It is a direct provider with priority `-100`, not
an automatic fallback. A direct InMemory reference wins over a merely bundle-provided automatic floor. If several Data
connectors are referenced directly, Koan applies the normal deterministic selection rules; pin the intended provider
when application durability must not change as references grow:

```json
{
  "Koan": {
    "Data": {
      "Sources": {
        "Default": { "Adapter": "inmemory" }
      }
    }
  }
}
```

## Current capability boundary

- process-local concurrent storage keyed by source, Entity type, and partition;
- in-memory filter execution;
- bulk upsert and bulk delete;
- atomic batch behavior within the in-memory store; and
- shared/container/database isolation modes through the common key-value family.

The connector does not advertise `DataCaps.Query.ProviderBoundedPaging`. `AllStream` and
`QueryStream` reject correctively with `QueryStreamRejectedException` before yielding because paging
an already resident full-source dictionary is not a provider-bounded stream.

Use `All`/`Query` for deliberately small test sets, or `FirstPage`/`Page` when a bounded result returned
to test code is sufficient. Numbered pages do not create an unbounded-data performance guarantee.

These are connector-specific claims, not a promise that remote providers behave identically. The
current connector suite passes 56/56.

For application conformance, prefer `Sylin.Koan.Testing`; it owns host isolation, partitions, and the
capability-aware battery. See [`TECHNICAL.md`](TECHNICAL.md) for the exact storage and negotiation
contract.

## References

- [DATA-0107 provider-bounded Entity streams](../../../../docs/decisions/DATA-0107-provider-bounded-entity-streams.md)
- [Entity access and streaming](../../../../docs/guides/data/entity-access-and-streaming.md)

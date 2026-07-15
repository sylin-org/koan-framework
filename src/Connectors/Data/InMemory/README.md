# Sylin.Koan.Data.Connector.InMemory

Use this connector for fast, process-local Entity persistence in tests, conformance suites, and
explicitly ephemeral development workflows.

## Choose it when

- losing all data at process exit is acceptable;
- a test needs a real `AddKoan()` provider without files, containers, or remote services; or
- provider-neutral behavior needs a deterministic in-memory oracle.

Do not choose it for durable application state, cross-process sharing, or production recovery.

## Reference = availability

Reference the connector from one coherent Koan package set, then use ordinary Entity operations:

```csharp
public sealed class Todo : Entity<Todo>
{
    public string Title { get; set; } = "";
}

var saved = await new Todo { Title = "Prove the rule" }.Save();
var same = await Todo.Get(saved.Id);
```

The provider identity is `inmemory` (`memory` is also recognized). It has priority `-100`, so a
higher-priority referenced provider wins unless the Entity, context, source, or application default
selects InMemory explicitly.

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

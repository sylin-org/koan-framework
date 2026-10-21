# Sylin.Koan.Data.SoftDelete

Make ordinary Entity deletion recoverable without changing the ordinary Entity grammar.

## Install

```powershell
dotnet add package Sylin.Koan.Data.SoftDelete
```

## Usage

Mark only the models whose durable deletion meaning is soft removal:

```csharp
using Koan.Data.Core.Model;   // Entity<T>, Get
using Koan.Data.Core;         // Remove
using Koan.Data.SoftDelete;   // SoftDelete, WithDeleted, Restore

[SoftDelete]
public sealed class Order : Entity<Order>;

await order.Remove();

using (Order.WithDeleted())
{
    var deleted = await Order.Get(order.Id);
    await deleted!.Restore();
}
```

Referencing the package activates its Data axis through the application's existing `AddKoan()` call. No service,
controller, option, or manual registration is required.

## Guarantees and boundaries

- Ordinary `Remove`, batch deletion, and `RemoveAll` set Koan's managed `__deleted` field instead of physically
  removing visible `[SoftDelete]` rows. Ordinary Entity reads hide those rows automatically.
- A soft delete changes only rows not yet deleted and counts only those. Inside `WithDeleted()`, `Remove`, `DeleteAll`
  and `RemoveAll` skip rows already in the recycle bin, so a mass delete there finishes and reports what it changed.
- `T.WithDeleted()` opens the recycle bin only for `T`; it cannot reveal deleted rows from another Entity type.
  Nested scopes compose and unwind with the async flow.
- Load a deleted row inside `WithDeleted()`, then call `.Restore()` to make it visible. Call `.HardDelete()` to purge
  a visible or already deleted row physically.
- Read scopes, including tenancy and request-contributed filters, still apply. A recycle-bin scope is not an
  authorization bypass.
- `Copy`, `Move` and `Mirror` read what the caller sees, so soft-deleted rows travel only inside `WithDeleted()`, and
  then they arrive soft-deleted. A `Move` removes the rows it moved from the source physically, because the
  destination now holds them; with `TransferFidelity.Semantic` it removes them as `Remove` does, leaving tombstones,
  and says so in `Warnings` ([DATA-0155](../../docs/decisions/DATA-0155-transfers-carry-soft-delete-state-and-a-move-removes-what-it-moved.md)).
- Soft deletion is a persistence semantic, not an audit log, retention policy, legal hold, or automatic recycle-bin
  HTTP API. Build explicit authorized controllers when the product earns that workflow.

See [TECHNICAL.md](TECHNICAL.md) for the Data-axis contract.

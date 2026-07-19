# Sylin.Koan.Data.SoftDelete

Make ordinary Entity deletion recoverable without changing the ordinary Entity grammar.

## Install

```powershell
dotnet add package Sylin.Koan.Data.SoftDelete
```

## Usage

Mark only the models whose durable deletion meaning is soft removal:

```csharp
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
- `T.WithDeleted()` opens the recycle bin only for `T`; it cannot reveal deleted rows from another Entity type.
  Nested scopes compose and unwind with the async flow.
- Load a deleted row inside `WithDeleted()`, then call `.Restore()` to make it visible. Call `.HardDelete()` to purge
  a visible or already deleted row physically.
- Read scopes, including tenancy and request-contributed filters, still apply. A recycle-bin scope is not an
  authorization bypass.
- Soft deletion is a persistence semantic, not an audit log, retention policy, legal hold, or automatic recycle-bin
  HTTP API. Build explicit authorized controllers when the product earns that workflow.

See [TECHNICAL.md](TECHNICAL.md) for the Data-axis contract.

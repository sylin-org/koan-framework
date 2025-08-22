# Messaging Reference

Core capabilities and patterns.

## Bus and batching
- IBus is the primary abstraction.
- IMessageBatch is first-class; emulate provider gaps predictably.
- References: decisions/MESS-0021..0027

## Aliasing and negotiation
- Capabilities and negotiation rules ensure predictable behavior across providers.

## Inbox contract
- Inbox contract and client patterns.

## Examples

```csharp
await bus.SendAsync(new OrderPlaced(...), ct);

await using var batch = bus.CreateBatch();
batch.Add(new ItemAdded(...));
batch.Add(new ItemAdded(...));
await batch.SendAsync(ct);
```

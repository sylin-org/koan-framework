# Messaging Reference

Canonical APIs and patterns for Sora messaging (bus, batching, aliasing, inbox).

## Contract
- Publish/send via `IBus`; batching via `IMessageBatch`.
- Aliasing defaults to full type name unless overridden by `[Message(Alias=...)]`.
- Default subscription group: `Sora:Messaging:DefaultGroup` ("workers").
- Auto-subscribe may be enabled when `ProvisionOnStart` is true and no explicit Subscriptions are configured.

## Examples

```csharp
// Publish a single message
await bus.SendAsync(new OrderPlaced(orderId, total), ct);

// Batch send
await using var batch = bus.CreateBatch();
batch.Add(new ItemAdded(orderId, sku));
batch.Add(new ItemAdded(orderId, anotherSku));
await batch.SendAsync(ct);

// Register a handler using OnMessage sugar (startup)
services.OnMessage<OrderPlaced>(async (msg, sp, ct) =>
{
		var logger = sp.GetRequiredService<ILogger<OrderPlaced>>();
		logger.LogInformation("Order {OrderId} placed for {Total}", msg.OrderId, msg.Total);
});
```

Default group and config (appsettings/environment):

```json
// appsettings.json
{
	"Sora": {
		"Messaging": {
			"DefaultGroup": "workers",
			"ProvisionOnStart": true
		}
	}
}
```

Environment variable equivalent:
```
Sora__Messaging__DefaultGroup=workers
Sora__Messaging__ProvisionOnStart=true
```

## Notes
- Explicit Subscriptions disable auto-subscribe.
- Use aliases to interop across languages when you can’t share type names.

## References
- decisions/MESS-0021-messaging-capabilities-and-negotiation.md
- decisions/MESS-0022-mq-provisioning-aliases-and-dispatcher.md
- decisions/MESS-0023-alias-defaults-default-group-and-onmessage.md
- decisions/MESS-0024-batch-semantics-and-aliasing.md
- decisions/MESS-0025-inbox-contract-and-client.md
- decisions/MESS-0027-standalone-mq-services-and-naming.md

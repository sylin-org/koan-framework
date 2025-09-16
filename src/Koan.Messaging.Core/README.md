# Sylin.Koan.Messaging.Core

Core messaging abstractions and helpers for Koan: bus configuration, auto-registration, and options binding.

- Target framework: net9.0
- License: Apache-2.0

## Install

```powershell
dotnet add package Sylin.Koan.Messaging.Core
```

## Links
- Messaging reference: https://github.com/sylin-labs/Koan-framework/tree/dev/docs/reference

## Usage

Declare messages with attributes, then send via extension methods.

```csharp
using Koan.Messaging;

[Message(Alias = "User.Registered", Version = 1)]
public sealed record UserRegistered(
	string UserId,
	[Header("x-tenant")] string Tenant,
	[IdempotencyKey] string EventId,
	[PartitionKey] string Partition,
	[DelaySeconds] int DelaySeconds = 0);

// Single send (DefaultBus)
await new UserRegistered("u-123", "acme", "evt-1", "acme:u-123").Send();

// Send to a specific bus
await new UserRegistered("u-456", "acme", "evt-2", "acme:u-456").SendTo("rabbit");

// Batch send
var batch = new object[]
{
	new UserRegistered("u-789", "acme", "evt-3", "acme:u-789"),
	new UserRegistered("u-790", "acme", "evt-4", "acme:u-790")
};
await batch.Send();           // default bus
await batch.SendTo("rabbit"); // specific bus
```

Register handlers tersely

```csharp
// Most concise (no envelope):
builder.Services.On<UserRegistered>(msg => Console.WriteLine($"New user: {msg.UserId}"));

// Async with CancellationToken
builder.Services.On<UserRegistered>((msg, ct) => HandleAsync(msg, ct));

// Keep envelope when needed
builder.Services.OnMessage<UserRegistered>((env, msg, ct) => HandleWithEnvelope(env, msg, ct));

// Intent-signaling aliases
builder.Services.OnEvent<UserRegistered>(msg => IndexUser(msg.UserId));
builder.Services.OnCommand<ReindexAll>(ct => ReindexAsync(ct));
```

Notes
- `Send`/`SendTo` are defined on `MessagingExtensions` and operate per-message when batching.
- Headers/correlation/idempotency are promoted from attributes; partitions add a `.p{n}` suffix to routing keys.

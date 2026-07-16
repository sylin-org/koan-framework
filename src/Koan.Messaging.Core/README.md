# Sylin.Koan.Messaging.Core

> **Legacy experimental implementation.** Do not treat this package as a stable messaging contract or
> add new application APIs to it. [ARCH-0113](../../docs/decisions/ARCH-0113-entity-capability-communication.md)
> replaces arbitrary-object Messaging with Entity `Events` and `Transport` over a rebuilt Communication
> pillar. The process-local Events and Transport replacements now ship in
> `Sylin.Koan.Communication`; connector and broker parity remain later work.

The current v0.17 package supplies the proxy, startup buffer, handler catalog, lifecycle, and provider
seams behind this demonstrated shape:

```csharp
builder.Services.On<UserRegistered>(Handle);
await new UserRegistered("u-1").Send(ct);
```

Current boundaries:

- `Send<T>` applies to any reference type and needs an active Koan host.
- `On<T>` retains at most one handler for each CLR type.
- the startup buffer is memory-only, not durable storage;
- provider selection is one priority/connectivity election;
- InMemory and RabbitMQ do not share copy or consumer-cardinality semantics; and
- there is no provider-neutral context, idempotency, retry, ordering, inbox/outbox, or dead-letter
  guarantee.

Attributes, aliases, `SendTo`, batch APIs, headers, partitions, and envelope-oriented handler examples
from older documents are not shipped by this implementation.

See the [truthful current boundary](../../docs/reference/messaging/index.md) and the
[Communication reference](../../docs/reference/communication/index.md) before maintaining or migrating
this package.

- Target framework: .NET 10
- License: Apache-2.0

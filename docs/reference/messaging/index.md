---
type: REF
domain: messaging
title: "Messaging — Retired Surface"
audience: [developers, architects, ai-agents]
status: deprecated
last_updated: 2026-07-17
framework_version: v0.20.0
validation:
  date_last_tested: 2026-07-17
  status: reviewed
  scope: retired Messaging vocabulary and canonical Communication replacement
---

# Messaging — retired surface

Koan's experimental general-object Messaging API has been removed. Do not use or recreate:

```csharp
services.On<SomeMessage>(...);
await message.Send();
```

The canonical replacement is the Entity-first [Communication ring](../communication/index.md):

```csharp
await order.Events.Raise<OrderApproved>(ct);
await order.Transport.Send(ct);
```

Events express a business occurrence; Transport distributes an Entity copy. Both work process-locally under
parameterless `AddKoan()`. A direct RabbitMQ connector reference can elect broker-backed Transport without
changing application Entity/receiver code. The Communication reference states the supported channels,
acceptance/settlement behavior, tenant/context carriage, RabbitMQ limits, and explicit non-claims.

The former `S3.Mq.Sample` project no longer exists and is not maintained curriculum. R10 owns its remaining ghost
directory and sample-index disposition; it is not evidence for current Communication behavior.

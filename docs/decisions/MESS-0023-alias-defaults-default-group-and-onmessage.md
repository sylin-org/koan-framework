---
id: MESS-0023
slug: MESS-0023-alias-defaults-default-group-and-onmessage
domain: MESS
status: Accepted
date: 2025-08-17
title: Alias defaults, default group, auto-subscribe, and OnMessage sugar
---
 
# ADR 0023: Alias defaults, default group, auto-subscribe, and OnMessage sugar
 

Context
- We want minimal configuration for messaging that “just works” while keeping explicit control easy.
- Routing should be predictable across languages by default, but overrideable.
- Developers prefer a lightweight way to register handlers without writing classes.

Decision
- Alias defaults:
  - When [Message] is absent or has no Alias, the alias is the full type name (Namespace.TypeName). This is implemented in DefaultTypeAliasRegistry.
  - [Message(Alias = "...")] overrides the alias.
- Default group and auto-subscribe:
  - Introduce Sora:Messaging:DefaultGroup with default "workers".
  - If ProvisionOnStart is enabled and no Subscriptions are configured, providers may auto-create a single subscription using DefaultGroup and bind to all ("#").
  - Explicit Subscriptions disable auto-subscribe.
- OnMessage sugar:
  - Add services.OnMessage<T>(...) to register delegate-based handlers. Internally wraps into IMessageHandler<T>.

Consequences
- Minimal config path: publishing and consuming works out-of-the-box with full-name alias and a default "workers" group.
- Cross-language consumers can rely on full-name alias unless projects choose to set explicit aliases.
- Simpler handler registration improves DX for quick prototypes and small services.
- Auto-subscribe is guarded by ProvisionOnStart, and only when Subscriptions are omitted, to avoid surprising production topologies.

References
- See Reference/Messaging for defaults and OnMessage usage.
 - src changes:
  - Sora.Messaging.Core: MessagingOptions.DefaultGroup
    - Sora.Messaging.Core: MessagingExtensions.OnMessage
  - Sora.Mq.RabbitMq: auto-subscription when Subscriptions omitted

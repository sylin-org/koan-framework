---
id: MESS-0029
slug: MESS-0029-terse-handler-delegates-and-aliases
domain: MESS
status: Accepted
date: 2025-08-24
title: Terse handler delegates and semantic aliases (On/Handle; Command/Event)
---

# 0029 — Terse handler delegates and semantic aliases

## Context
Developers asked for shorter, intentful handler registration similar to first-class Entity statics in Sora.Data. The existing APIs exposed `OnMessage<T>(...)` and batch variants, which are powerful but verbose for the common case of handling a typed payload without envelope access.

## Decision
Add concise registration delegates and semantic aliases in Messaging Core while preserving envelope-based handlers:

- New concise overloads
  - On<T>(Func<T, CancellationToken, Task>)
  - On<T>(Func<T, Task>)
  - On<T>(Action<T>)

- Semantic aliases (map to On<T>)
  - OnCommand<T>(...)
  - OnEvent<T>(...)
  - Handle<T>(...)

- Parity in the builder
  - MessageHandlerBuilder gains matching On/OnCommand/OnEvent/Handle overloads for fluent registration.

- Existing capabilities remain
  - Envelope-based `OnMessage<T>(...)` stays for advanced scenarios (headers, idempotency keys, partitioning info, reply-to, etc.).
  - Batch registration (`OnBatch<T>` and alias `OnMessages<T>`) is unchanged (see MESS-0024).

## Scope
Applies to Sora.Messaging.Core ergonomics. No changes to wire format or transport contracts. Providers require no changes.

## Consequences
- Improved DX for common handlers; terser code without losing expressiveness when needed.
- Clearer intent via semantic names (commands vs. events) without new types.
- Existing code continues to work; this is additive.

## Implementation notes
- Implemented in `src/Sora.Messaging.Core/MessagingExtensions.cs` alongside existing helpers.
- Aliases forward to On<T> and reuse DelegateMessageHandler<T> under the hood.
- Docs updated: `src/Sora.Messaging.Core/README.md` and `src/Sora.Messaging.RabbitMq/README.md` now show concise examples first; `TECHNICAL.md` mentions the sugar.

## Follow-ups
- Consider optional convention-based registration (scan assemblies for IMessageHandler<T> or delegates) as a separate DX decision.
- Evaluate minimal source generator for strongly-typed aliases if needed.

## References
- MESS-0024 — Batch semantics, handlers, and aliasing
- MESS-0022 — MQ provisioning defaults, type aliases/attributes, and dispatcher
- ARCH-0042 — Per-project companion docs

# WEB-0060: Flow Messaging Refactor

## Objective
Implement clean Flow messaging architecture at the Sora.Messaging/Sora.Flow framework level for improved developer experience and maintainability.

## Architectural Improvements
- AsyncLocal FlowContext for adapter identity.
- MessagingInterceptors for type-safe registration.
- entity.Send() pattern for natural developer surface.
- Consistent message handling via TransportEnvelope and MessagingTransformers.

## Rationale
Addresses lost adapter context, complex auto-handler magic, poor developer experience, and inconsistency with Sora patterns.

See also: [Flow Messaging Status](../engineering/flow-messaging-status.md), [Messaging Architecture Guide](../guides/flow/flow-messaging-architecture.md).

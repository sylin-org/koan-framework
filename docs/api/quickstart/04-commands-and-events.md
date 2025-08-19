# Commands & Events

Introduce CQRS and messaging to decouple reads/writes and react to domain events.

## Concepts
- Commands change state, queries read state
- Events notify other parts of the system

## Steps
- Define command records and handlers
- Define domain events and event handlers
- Wire messaging (in-memory by default; RabbitMQ optional)

Next: Production Ready → 05-production-ready.md

# Testing DDD Models with Sora

Layers
- Unit: aggregates and value objects — pure tests of invariants and behaviors.
- Integration: repositories against real adapters (Mongo/Relational/Json).
- End-to-end: compose stacks (e.g., S2.Compose) with health probes and CRUD flows.

Guidance
- Favor fast unit tests for domain rules.
- Use adapter-specific test fixtures (see `tests/Sora.Data.Mongo.Tests`, `Sora.Data.Relational.Tests`).
- For messaging, test outbox persistence and inbox idempotency; use RabbitMQ containers where useful.
- CI: smoke the compose stack (see `.github/workflows/s2-compose-smoke.yml`).

## Terms in plain language
- Unit Test: tests a small piece of logic in isolation (fast, no I/O).
- Integration Test: uses real infrastructure (databases, queues) to verify behavior.
- End-to-End Test: covers the whole flow through APIs, storage, and messaging.
- Fixture: reusable setup/teardown for a group of tests.

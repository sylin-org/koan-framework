# Assemblies map

The Sora solution is modular. Assemblies (namespaces) are `Sora.*`; NuGet package IDs are published as `Sylin.Sora.*`. Here’s a quick map of assemblies and their roles:

- Sora.Core — Options, observability bootstrap, base abstractions shared across features.
- Sora.Web — Web host helpers, entity controllers, well-known endpoints, headers.
- Sora.Data.Abstractions — Minimal persistence abstractions (IEntity, repositories).
- Sora.Data.Core — Core repository utilities and entity helpers.
- Sora.Data.Cqrs — CQRS decorator that splits reads/writes and forwards counts.
- Sora.Data.Cqrs.Outbox.Mongo — Mongo-backed outbox for reliable event publishing.
- Sora.Data.Json — Filesystem JSON adapter for simple/local scenarios.
- Sora.Data.Mongo — MongoDB adapter and configuration.
- Sora.Data.Relational — Relational adapter infrastructure (ADO/SQL helpers).
- Sora.Data.Sqlite — SQLite adapter for relational storage.
- Sora.Messaging.Abstractions — Messaging contracts and handler abstractions.
- Sora.Messaging.Core — Core messaging utilities.
- Sora.Messaging.Inbox.Http — HTTP-based inbox support.
- Sora.Messaging.Inbox.InMemory — In-memory inbox for testing.
- Sora.Messaging.RabbitMq — Messaging abstractions for RabbitMQ usage.
- Sora.Mq.RabbitMq — RabbitMQ transport implementation.
- Sora.Inbox.Redis — Redis-backed inbox storage.
- Sora.Service.Inbox.Redis — Inbox processor service wiring.

Samples (selected):
- samples/S2.Api — API using Sora.Web + Mongo data adapter.
- samples/S2.Client — Nginx-served static client that hits the API and well-known endpoints.
- samples/S3.Mq.Sample — RabbitMQ sample showcasing messaging patterns.

See also: docs/00-index.md and docs/ddd/00-index.md for conceptual guidance.

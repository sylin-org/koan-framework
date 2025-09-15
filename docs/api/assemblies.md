# Assemblies map

The Koan solution is modular. Assemblies (namespaces) are `Koan.*`; NuGet package IDs are published as `Sylin.Koan.*`. Here’s a quick map of assemblies and their roles:

- Koan.Core — Options, observability bootstrap, base abstractions shared across features.
- Koan.Web — Web host helpers, entity controllers, well-known endpoints, headers.
- Koan.Data.Abstractions — Minimal persistence abstractions (IEntity, repositories).
- Koan.Data.Core — Core repository utilities and entity helpers.
- Koan.Data.Cqrs — CQRS decorator that splits reads/writes and forwards counts.
- Koan.Data.Cqrs.Outbox.Mongo — Mongo-backed outbox for reliable event publishing.
- Koan.Data.Json — Filesystem JSON adapter for simple/local scenarios.
- Koan.Data.Mongo — MongoDB adapter and configuration.
- Koan.Data.Relational — Relational adapter infrastructure (ADO/SQL helpers).
- Koan.Data.Sqlite — SQLite adapter for relational storage.
- Koan.Messaging.Abstractions — Messaging contracts and handler abstractions.
- Koan.Messaging.Core — Core messaging utilities.
- Koan.Messaging.Inbox.Http — HTTP-based inbox support.
- Koan.Messaging.Inbox.InMemory — In-memory inbox for testing.
- Koan.Messaging.RabbitMq — Messaging abstractions for RabbitMQ usage.
- Koan.Mq.RabbitMq — RabbitMQ transport implementation.
- Koan.Inbox.Redis — Redis-backed inbox storage.
- Koan.Service.Inbox.Redis — Inbox processor service wiring.

Samples (selected):
- samples/S2.Api — API using Koan.Web + Mongo data adapter.
- samples/S2.Client — Nginx-served static client that hits the API and well-known endpoints.
- samples/S3.Mq.Sample — RabbitMQ sample showcasing messaging patterns.

See also: docs/00-index.md and docs/ddd/00-index.md for conceptual guidance.

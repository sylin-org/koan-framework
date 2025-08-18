# Sora Samples Plan

- S0 Console + JSON repo (implemented: samples/S0.ConsoleJsonRepo)
- S1 Web API + Dapper/Sqlite
- S1b Web API + EF Core Sqlite (optional)
- S2 Compose: Client + API + Mongo
- S3.Mq.Sample: Minimal console + RabbitMQ (publisher + handler + compose)
- S4 Compose: Client (React) + API + Sqlite + Mongo + Messaging/CQRS (RabbitMQ)
- S5 Webhooks
- S6 Agent API (PGVector/Qdrant)
- S7 Full-stack Compose

S4 details (multi-database + messaging):
- Two aggregates in one API:
	- Products: [sqlite] structured transactional data
	- Activity: [mongo] flexible event log
- Messaging/CQRS:
	- Command: CreateProduct → outbox + Event: ProductCreated → consumed to append Activity
	- RabbitMQ transport via Testcontainers in CI and Compose locally
- Client (React): two tabs (Products, Activity) sharing the S2 UX (list, add, delete, seed/clear, pagination)
- Compose: api (5064), client (5065), mongo (internal), rabbitmq; sqlite file volume mounted to api

Refer to existing details in docs/11-samples-plan.md in the root for acceptance criteria; this file mirrors and will replace it when Sora repo stands alone.

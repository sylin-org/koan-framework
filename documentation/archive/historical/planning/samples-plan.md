# Koan Samples Plan

- S0 Console + JSON repo (implemented: samples/S0.ConsoleJsonRepo)
- S1 Web API + Dapper/Sqlite
- S1b Web API + EF Core Sqlite (optional)
- S2 Compose: Client + API + Mongo
- S3.Mq.Sample: Minimal console + RabbitMQ (publisher + handler + compose)
- S4 GraphQL Client: Client (Alpine.js or React+urql) + API (GraphQL via Koan.Web.GraphQl)
- S5 Compose: Client (React) + API + Sqlite + Mongo + Messaging/CQRS (RabbitMQ)
- S6 Webhooks
- S7 Agent API (PGVector/Qdrant)
- S8 Full-stack Compose

S4 details (GraphQL client):
- Purpose: showcase the GraphQL module (ADR-0041) with typed filters/sorts and `display` field.
- Client: minimal Alpine.js (fetch POST to /graphql) or React+urql variant; no heavy build required for Alpine path.
- API: enable Koan.Web.GraphQl alongside REST; queries:
	- entity(id), entities(filter, sort, page, size, set) returning connection { items, totalCount, pageInfo }.
	- mutations: upsert, upsertMany, delete, deleteMany.
- Focus: DX of typed filters/sorts mapping to QueryOptions; coexistence with REST.

S5 details (multi-database + messaging):
- Two aggregates in one API:
	- Products: [sqlite] structured transactional data
	- Activity: [mongo] flexible event log
- Messaging/CQRS:
	- Command: CreateProduct → outbox + Event: ProductCreated → consumed to append Activity
	- RabbitMQ transport via Testcontainers in CI and Compose locally
- Client (React): two tabs (Products, Activity) sharing the S2 UX (list, add, delete, seed/clear, pagination)
- Compose: api (5064), client (5065), mongo (internal), rabbitmq; sqlite file volume mounted to api

Refer to existing details in docs/11-samples-plan.md in the root for acceptance criteria; this file mirrors and will replace it when Koan repo stands alone.

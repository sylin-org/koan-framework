---
type: REFERENCE
domain: framework
title: "What you can make with Koan today"
audience: [developers, architects, technical-leads, ai-agents]
status: current
last_updated: 2026-07-23
framework_version: v0.20.0
validation:
  date_last_tested: 2026-07-23
  status: reviewed
  scope: human-readable outcome map over the generated v0.20 product surface
---

# One Entity. A lot of places to go.

Start with the idea:

```csharp
public sealed class Todo : Entity<Todo>;
[Route("api/todos")]
public sealed class TodosController : EntityController<Todo>;
```

With the web application bundle and SQLite connector, those declarations become a durable,
queryable HTTP API. From there, add only what the application asks for. The code can keep talking
about `Todo`.

## Make it persistent and reachable

Entities can save, query, page, stream where the provider supports it, and travel through
conventional HTTP APIs. Start with durable local SQLite, or bring PostgreSQL, SQL Server,
CockroachDB, MongoDB, Couchbase, or Redis when the application needs their reach.

[Explore data](data/index.md) · [Explore HTTP APIs](web/index.md)

## Give it work to do

Run Entity-owned jobs with progress, retries, cancellation, and schedules. Raise a business event or
send an Entity snapshot without beginning with a queue topology. Keep it in-process, or use RabbitMQ
when the message must cross a process boundary.

[Explore jobs, events, and communication](work/index.md)

## Let it recognize people—and boundaries

Add sign-in, authorization, durable identities, OAuth token issuance, tenant isolation, and
classified-field encryption. Google, Microsoft, and Discord providers are available, along with a
deterministic local identity provider for development and tests.

The same access declaration can govern what HTTP callers and agents are allowed to see and do.

[Explore identity and isolation](identity/index.md)

## Teach it to understand meaning

Chat, stream responses, or create embeddings through one application-facing client when an active
provider supports the operation. Koan 0.20 works locally with Ollama, LM Studio, and in-process ONNX
within their model and runtime limits.

Use Koan's Entity vector API for semantic search. Begin in memory or with embedded `sqlite-vec`;
move to Qdrant, Milvus, Weaviate, Elasticsearch, or OpenSearch when external reach makes sense.

[Explore AI and vector search](ai/index.md)

## Invite an agent in

```csharp
[McpEntity(Name = "Todo", Description = "Work the team intends to finish")]
public sealed class Todo : Entity<Todo>;
```

Now an MCP client can discover and work with the same Entity. No mirrored agent model. No handwritten
CRUD tool handlers. Koan advertises only the operations and fields that caller is allowed to use.

[Explore agents and MCP](agents/index.md)

## Give it files, images, and memory

Bind Entities to local file storage, transform and serve images through named media recipes, or add
typed caching without changing ordinary Entity operations. Cache can stay local, survive restart
through the supported SQLite adapter, or move to another engine after checking its current support
status.

[Explore storage, media, and cache](state-content/index.md)

## Turn arrivals into trusted records

Canon can normalize and reconcile imperfect customer, device, account, or other arrivals into a
trusted Entity. Your code names the identity and business rules; Koan runs the deterministic
pipeline, persistence, lineage, audit, and optional HTTP projection.

[Explore Canon](canon/index.md)

## See what the application became

Health endpoints, runtime facts, OpenTelemetry, and reusable Entity conformance tests make Koan's
choices visible. You can keep the short application code without treating the framework as a black
box.

[Explore testing and operations](operations/index.md)

## The honest edges

Koan 0.20 is a .NET 10 preview. It does not provision production infrastructure, promise general
cross-provider transactions or transparent failover, manage backups and disaster recovery, or
support NativeAOT. Provider-specific consistency, query, durability, and deployment limits still
matter.

This page is the human map. For exact package ownership, maturity, evidence, and support status, use
the generated [Koan product surface](product-surface.md).

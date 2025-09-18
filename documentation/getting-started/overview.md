---
type: REF
domain: core
title: "Koan Framework Overview"
audience: [developers, architects, ai-agents]
last_updated: 2025-01-17
framework_version: "v0.2.18+"
status: current
validation: 2025-01-17
---

# Koan Framework Overview

**Document Type**: Reference Documentation
**Target Audience**: Developers, Architects, AI Agents
**Last Updated**: 2025-01-17
**Framework Version**: v0.2.18+

---

## 🎯 What is Koan Framework?

**Build services like you're talking to your code, not fighting it.**

Koan is a modular .NET backend framework that standardizes data, web, messaging, and AI patterns with strong governance and observability—so teams ship faster with fewer surprises, and platforms scale with consistency.

Whether you're spinning up a quick prototype or scaling into enterprise-grade patterns, Koan keeps the path clear. Start with a three-file API. Add messaging, vector search, or AI when you're ready. Nothing more, nothing less.

### Core Philosophy

**Key Principles:**

- **Start Simple**: Build a real service in a single file
- **Clear Structure**: Follow .NET conventions, not opinions
- **Honest Complexity**: Add what you need, skip what you don't
- **Escape Hatches Everywhere**: Drop to raw SQL, write custom controllers, override behavior freely

---

## 🧱 From First Line to First Endpoint

Let's start simple:

```bash
dotnet add package Koan.Core
dotnet add package Koan.Web
dotnet add package Koan.Data.Sqlite
```

Then:

```csharp
public class Todo : Entity<Todo>
{
    public string Title { get; set; } = "";
    public bool IsCompleted { get; set; }
}

[Route("api/[controller]")]
public class TodosController : EntityController<Todo> { }
```

That's a full REST API:

- `GET /api/todos`
- `POST /api/todos`
- `PUT /api/todos/{id}`
- `DELETE /api/todos/{id}`
- Health checks at `/api/health`

It works. Right now. No ceremony.

---

## 🏗️ Architecture Overview

Koan follows a **modular, composition-based architecture** with the following design principles:

### 1. **Pillars Architecture**

Each pillar is an independent module that works standalone or composes with others:

```
┌─────────────┬─────────────┬─────────────┬─────────────┐
│    Core     │    Data     │    Web      │  Messaging  │
├─────────────┼─────────────┼─────────────┼─────────────┤
│     AI      │   Storage   │   Media     │    Flow     │
├─────────────┼─────────────┼─────────────┼─────────────┤
│  Recipes    │Orchestration│ Scheduling  │     ...     │
└─────────────┴─────────────┴─────────────┴─────────────┘
```

### 2. **Auto-Registration Pattern**

Koan uses `IKoanAutoRegistrar` for zero-config module discovery:

```csharp
// Modules auto-register themselves
services.AddKoan(); // Discovers and loads all referenced Koan modules
```

### 3. **Configuration Hierarchy**

Deterministic configuration layering:

```
Provider defaults < Recipe defaults < AppSettings/Env < Code overrides < Forced overrides
```

### 4. **Entity-Centric Design**

Models are first-class citizens with static methods:

```csharp
public class Todo : Entity<Todo>
{
    public static Task<Todo[]> Recent() =>
        Query().Where(t => t.Created > DateTime.Today.AddDays(-7));
}
```

---

## 🧱 Framework Pillars

### **Core** - Foundation Layer

- **Purpose**: Unified runtime, secure defaults, health checks, observability
- **Key Features**:
  - Auto-registration pipeline (`IKoanAutoRegistrar`)
  - Health/readiness endpoints (`/api/health`, `/api/health/live`, `/api/health/ready`)
  - Configuration helpers (`KoanEnv`, configuration reading patterns)
  - OpenTelemetry integration (opt-in)
  - Boot reports with module discovery

### **Data** - Persistence Abstraction

- **Purpose**: Unified access to SQL, NoSQL, JSON, and vector databases
- **Key Features**:
  - Adapter-agnostic persistence with pushdown-first performance
  - Production-ready adapters: Postgres, SQL Server, SQLite, MongoDB, Redis, JSON file
  - Vector module (`Koan.Data.Vector`) with multi-provider support
  - CQRS patterns with outbox support
  - Direct SQL escape hatch for complex queries

### **Web** - HTTP Layer

- **Purpose**: REST and GraphQL from your models, clean routing
- **Key Features**:
  - Controllers-only HTTP (no inline endpoints)
  - Generic `EntityController<T>` for automatic REST APIs
  - Payload transformers for request/response shaping
  - Web capabilities: Moderation, SoftDelete, Audit
  - OpenAPI/Swagger generation (development)

### **Flow** - Data Pipeline

- **Purpose**: Model-typed pipeline for data ingestion, standardization, and projection
- **Key Features**:
  - Pipeline stages: Intake → Standardize → Key → Associate → Project
  - Aggregation tags and identity mapping
  - Canonical and lineage views
  - Parked records and rejection reports
  - External ID correlation system

### **Storage** - File/Blob Handling

- **Purpose**: Profile-based storage orchestration with thin providers
- **Key Features**:
  - Local filesystem provider (production-ready)
  - Profile-based routing (hot/cold storage)
  - Model-centric API with `StorageEntity<T>`
  - Server-side operations (copy, move, presign when supported)
  - Safe key handling and atomic writes

### **Media** - First-Class Media

- **Purpose**: Media objects, HTTP endpoints, transforms
- **Key Features**:
  - HTTP bytes/HEAD endpoints with range/conditional support
  - Variants and derivatives system
  - Transform pipeline (resize, rotate, type-convert)
  - Storage integration with profiles
  - Cache-control and CDN-friendly headers

### **Messaging** - Reliable Queues

- **Purpose**: Capability-aware semantics with multiple transports
- **Key Features**:
  - RabbitMQ, Redis, and in-memory transports
  - Inbox services for deduplication
  - Retry/DLQ patterns
  - Type-safe message handlers

### **AI** - AI Integration

- **Purpose**: Chat, embeddings, vector search, RAG building blocks
- **Key Features**:
  - Local LLMs via Ollama provider
  - Vector database adapters (Redis, Weaviate)
  - Budget controls and observability
  - RAG patterns with citations

### **Recipes** - Best Practices

- **Purpose**: Intention-driven bootstrap bundles
- **Key Features**:
  - Health checks, telemetry, reliability, workers
  - Capability gating and dry-run mode
  - Deterministic options layering

### **Orchestration** - DevOps Tooling

- **Purpose**: DevHost CLI for local dependencies and deployment
- **Key Features**:
  - Docker/Podman adapter support
  - Compose v2 export with profile-aware behavior
  - Readiness semantics and endpoint hints
  - Single-binary distribution (`Koan.exe`)

---

## 🌱 A Framework That Grows With You

Koan isn't trying to impress you with magic. It earns trust by staying out of your way—until you need more.

- Add AI? One line.
- Need vector search? Drop in a package.
- Ready for messaging? Plug it in.
- CQRS? Recipes exist.

You never pay for complexity you didn't ask for.

```bash
dotnet add package Koan.AI                    # Local LLMs with Ollama
dotnet add package Koan.Data.Weaviate         # Semantic search
dotnet add package Koan.Messaging.RabbitMq    # Production messaging
dotnet add package Koan.Web.GraphQl           # REST + GraphQL side-by-side
```

Everything integrates naturally. No glue scripts. No boilerplate.

---

## 🛡️ Security & Production Readiness

### Built-in Security

- **Secure headers by default**
- **HTTPS redirection** (production)
- **Content Security Policy** (opt-in)
- **Enterprise Authentication**: OAuth 2.1, OIDC, SAML 2.0 support
- **Multi-Provider Auth**: Google, Microsoft, Discord, generic OIDC, SAML, TestProvider (dev)
- **Account Linking**: Users can link multiple identity providers
- **Security Features**: PKCE, state/nonce validation, rate limiting, secret management

### Observability

- **Health checks** with detailed status
- **OpenTelemetry integration** (opt-in via observability configuration)
- **Structured logging** with event IDs
- **Boot reports** (redacted in production)

### Production Patterns

- **Configuration validation** at startup
- **Graceful degradation** with fallbacks
- **Circuit breaker patterns**
- **Rate limiting** capabilities
- **DDL governance** (NoDdl policy for production)

---

## 🔄 Framework Evolution

### Current State (v0.2.18+)

- ✅ **Stable**: Core, Data, Web, Storage, Messaging
- ✅ **Production Ready**: All adapters, health system, orchestration
- ✅ **AI Integration**: Ollama provider, vector search, RAG patterns
- 🔄 **Active Development**: Flow pipeline enhancement, MCP integration

### Near-term Roadmap

- **AI North Star**: One-call AI setup with auto-discovery
- **Flow Enhancement**: Lifecycle interceptors, bidirectional orchestration
- **MCP Integration**: Model Context Protocol support for AI agents
- **Cloud Storage**: S3/Azure Blob/GCS adapters

### Future Vision

- **Protocol Interop**: gRPC, MCP, AI-RPC adapters
- **Knowledge Systems**: SPARQL/RDF export, enhanced vector operations
- **Data Bridge**: CDC, virtualization, materialization

---

## 💡 Why Choose Koan?

### For Developers

- **Minimal Friction**: Real service in a single file
- **Clear Path**: From prototype to production without architectural rewrites
- **No Lock-in**: Escape hatches everywhere, standard .NET patterns
- **Rich Ecosystem**: Adapters for all major databases and services

### For Architects

- **Modular Design**: Add only what you need
- **Governance**: Built-in policies for security, performance, and compliance
- **Observability**: Comprehensive telemetry and health monitoring
- **Future-Proof**: Protocol adapters and standard interfaces

### For Teams

- **Consistency**: Unified patterns across all services
- **Productivity**: Less boilerplate, more business logic
- **Reliability**: Battle-tested patterns with guardrails
- **Documentation**: Comprehensive guides and examples

---

## 🧪 Real Use, Not Just Hello World

Koan is already being used to build:

- Microservices with event sourcing and inbox/outbox patterns
- Developer tools with built-in AI assistance
- Internal apps with rapid UI prototyping and API documentation

It's ready for you too.

```csharp
var todo = await new Todo { Title = "Learn Koan" }.Save();
var todos = await Todo.Where(t => !t.IsCompleted);
```

---

## 📚 Next Steps

1. **Get Started**: See [Getting Started Guide](getting-started.md)
2. **Authentication**: See [Authentication Guide](../guides/authentication-setup.md)
3. **Explore Patterns**: See [Usage Patterns](../architecture/patterns.md)
4. **Deep Dive**: See pillar-specific documentation in [/reference/](../reference/)
5. **Samples**: Explore the `samples/` directory
6. **Architecture**: Read [Architecture Principles](../architecture/principles.md)

---

**The Koan Framework: Build services like you're talking to your code, not fighting it.**

---

**Last Validation**: 2025-01-17 by Framework Specialist
**Framework Version Tested**: v0.2.18+

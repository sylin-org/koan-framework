# REF_SORA_FRAMEWORK_OVERVIEW.md

**Document Type**: Reference Documentation (REF)  
**Target Audience**: Developers, Architects, AI Agents  
**Last Updated**: 2025-01-10  
**Framework Version**: v0.2.18+

---

## 🎯 What is Sora Framework?

**Sora is a modular .NET backend framework that standardizes data, web, messaging, and AI patterns with strong governance and observability—so teams ship faster with fewer surprises, and platforms scale with consistency.**

### Core Philosophy

Build services like you're talking to your code, not fighting it. Sora keeps the path clear whether you're spinning up a quick prototype or scaling into enterprise-grade patterns.

**Key Principles:**
- **Start Simple**: Build a real service in a single file
- **Clear Structure**: Follow .NET conventions, not opinions  
- **Honest Complexity**: Add what you need, skip what you don't
- **Escape Hatches Everywhere**: Drop to raw SQL, write custom controllers, override behavior freely

---

## 🏗️ Architecture Overview

Sora follows a **modular, composition-based architecture** with the following design principles:

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
Sora uses `ISoraAutoRegistrar` for zero-config module discovery:
```csharp
// Modules auto-register themselves
services.AddSora(); // Discovers and loads all referenced Sora modules
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
    public static async Task<Todo[]> Recent() => await All().Where(t => t.Created > DateTime.Today.AddDays(-7));
}
```

---

## 🧱 Framework Pillars

### **Core** - Foundation Layer
- **Purpose**: Unified runtime, secure defaults, health checks, observability
- **Key Features**:
  - Auto-registration pipeline (`ISoraAutoRegistrar`)
  - Health/readiness endpoints (`/health`, `/health/live`, `/health/ready`)
  - Configuration helpers (`SoraEnv`, `Sora.Core.Configuration.Read`)
  - OpenTelemetry integration (opt-in)
  - Boot reports with module discovery

### **Data** - Persistence Abstraction  
- **Purpose**: Unified access to SQL, NoSQL, JSON, and vector databases
- **Key Features**:
  - Adapter-agnostic persistence with pushdown-first performance
  - Production-ready adapters: Postgres, SQL Server, SQLite, MongoDB, Redis, JSON file
  - Vector module (`Sora.Data.Vector`) with multi-provider support
  - CQRS patterns with outbox support
  - Safe Direct escape hatch for raw SQL

### **Web** - HTTP Layer
- **Purpose**: REST and GraphQL from your models, clean routing
- **Key Features**:
  - Controllers-only HTTP (no inline endpoints)
  - Generic `EntityController<T>` for automatic REST APIs
  - Built-in Swagger/OpenAPI (dev-on by default)
  - Payload transformers for request/response shaping
  - Web capabilities: Moderation, SoftDelete, Audit

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
  - Single-binary distribution (`Sora.exe`)

---

## 🚀 Quick Start Example

Here's how simple it is to get started:

### 1. Installation
```bash
dotnet add package Sora.Core
dotnet add package Sora.Web  
dotnet add package Sora.Data.Sqlite
```

### 2. Model Definition
```csharp
public class Todo : Entity<Todo>
{
    public string Title { get; set; } = "";
    public bool IsDone { get; set; }
    public DateTimeOffset Created { get; set; } = DateTimeOffset.UtcNow;
    
    // Static methods are first-class
    public static Task<Todo[]> Pending() => All().Where(t => !t.IsDone);
    public static Task<Todo[]> Recent() => All().Where(t => t.Created > DateTimeOffset.UtcNow.AddDays(-7));
}
```

### 3. Controller (Automatic REST API)
```csharp
[Route("api/[controller]")]
public class TodosController : EntityController<Todo> 
{ 
    // Automatically provides:
    // GET /api/todos
    // POST /api/todos
    // PUT /api/todos/{id}
    // DELETE /api/todos/{id}
}
```

### 4. Program.cs
```csharp
var builder = WebApplication.CreateBuilder(args);

// Single line adds all referenced Sora modules
builder.Services.AddSora();

var app = builder.Build();

// Sora handles the pipeline setup
await app.RunAsync();
```

That's a **complete, production-ready REST API** with:
- SQLite database
- Health checks at `/health`
- Swagger UI at `/swagger` (in development)
- Proper error handling and logging
- Configuration management

---

## 🎨 Usage Patterns

### **Progressive Enhancement**
Start minimal, add complexity only when needed:

```bash
# Basic web API
dotnet add package Sora.Web Sora.Data.Sqlite

# Add AI capabilities  
dotnet add package Sora.AI

# Add messaging
dotnet add package Sora.Messaging.RabbitMq

# Add vector search
dotnet add package Sora.Data.Weaviate

# Add GraphQL alongside REST
dotnet add package Sora.Web.GraphQl
```

### **Entity-First Development**
Models drive the architecture:

```csharp
// 1. Define your model
public class Product : Entity<Product>
{
    [AggregationKey] // For Flow pipeline
    public string SKU { get; set; } = "";
    
    public string Name { get; set; } = "";
    public decimal Price { get; set; }
    
    // Business logic stays with the model
    public static Task<Product[]> OnSale() => 
        All().Where(p => p.Price < p.OriginalPrice);
}

// 2. Get automatic REST API
[Route("api/[controller]")]
public class ProductsController : EntityController<Product> { }

// 3. Get automatic GraphQL schema (if GraphQL package is referenced)
// Schema auto-generated from Product model
```

### **Configuration-Driven Behavior**
Sora respects configuration hierarchy:

```json
{
  "Sora": {
    "Data": {
      "DefaultProvider": "Postgres",
      "Sqlite": {
        "ConnectionString": "Data Source=app.db"
      }
    },
    "Web": {
      "EnableSwagger": true,
      "CorsOrigins": ["http://localhost:3000"]
    },
    "AI": {
      "DefaultProvider": "Ollama",
      "Budget": {
        "MaxTokensPerRequest": 1000
      }
    }
  }
}
```

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
- **OpenTelemetry integration** (opt-in via `AddSoraObservability()`)
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

## 💡 Why Choose Sora?

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

## 📚 Next Steps

1. **Get Started**: See `REF_SORA_GETTING_STARTED.md`
2. **Authentication**: See `REF_SORA_AUTHENTICATION_GUIDE.md`
3. **Explore Patterns**: See `REF_SORA_USAGE_PATTERNS.md`  
4. **Deep Dive**: See pillar-specific REF documents
5. **Samples**: Explore the `samples/` directory
6. **Architecture**: Read `docs/architecture/principles.md`

---

**The Sora Framework: Build services like you're talking to your code, not fighting it.**
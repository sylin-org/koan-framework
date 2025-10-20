# Koan Framework Sample Catalog

**Last Updated**: 2025-10-16 | **Framework Version**: v0.6.3

This catalog provides a comprehensive guide to all Koan Framework samples, organized by learning progression and capability coverage.

---

## Quick Reference

### Learning Path

Follow this progression for optimal learning:

1. **🌱 Beginner**: S0.ConsoleJsonRepo → S1.Web
2. **📈 Intermediate**: S10.DevPortal (framework understanding) → S5.Recs (comprehensive app)
3. **🚀 Advanced**: S16.PantryPal (AI/MCP), S8.Canon (pipelines), S9.OrderFlow (event sourcing - planned)

### Sample Index

| # | Name | Category | Complexity | Key Capabilities |
|---|------|----------|------------|------------------|
| **S0** | ConsoleJsonRepo | Fundamentals | ⭐ Beginner | Core runtime, Entity<T>, JSON adapter |
| **S1** | Web | Fundamentals | ⭐ Beginner | CRUD, relationships, controllers, pagination |
| **S3** | NotifyHub | Integration | ⭐⭐ Intermediate | Messaging, inbox, scheduling, retry patterns *(planned)* |
| **S4** | DevHub | Integration | ⭐⭐ Intermediate | Secrets management, Vault, configuration *(planned)* |
| **S5** | Recs (AnimeRadar) | AI & Intelligence | ⭐⭐⭐ Advanced | AI integration, vector search, recommendations |
| **S6** | MediaHub | AI & Intelligence | ⭐⭐ Intermediate | Media processing, storage, backup *(planned)* |
| **S8** | Canon | Advanced Patterns | ⭐⭐⭐ Advanced | Canon Runtime, pipelines, validation/enrichment |
| **S9** | OrderFlow | Advanced Patterns | ⭐⭐⭐⭐ Expert | Event sourcing, CQRS, outbox pattern *(planned)* |
| **S10** | DevPortal | Framework Showcase | ⭐⭐ Demo | Multi-provider, capability detection, live switching |
| **S14** | AdapterBench | Framework Showcase | ⭐⭐ Demo | Performance benchmarking, provider comparison |
| **S16** | PantryPal | AI & Intelligence | ⭐⭐⭐ Advanced | Vision AI, MCP Code Mode, complex workflows |

### Port Allocations

| Sample | HTTP Port | Port Block | Status |
|--------|-----------|------------|--------|
| S0 | N/A | 5034-5039 | ✅ Active |
| S1 | 5044 | 5040-5049 | ✅ Active |
| S3 | 5064 | 5060-5069 | 🔨 Planned |
| S4 | 5074 | 5070-5079 | 🔨 Planned |
| S5 | 5084 | 5080-5089 | ✅ Active |
| S6 | 5094 | 5090-5099 | 🔨 Planned |
| S7 | 5104 | 5100-5109 | ⚠️ Reserved for future |
| S8 | 5114 | 5110-5119 | ✅ Active |
| S9 | 5124 | 5120-5129 | 🔨 Planned |
| S10 | 5134 | 5130-5139 | ✅ Active |
| S14 | 5174 | 5170-5179 | ✅ Active |
| S16 | 5194 | 5190-5199 | ✅ Active |

---

## Capability Coverage Matrix

### "Which sample shows...?"

Use this table to find samples demonstrating specific framework capabilities.

| Capability | Pillar | Samples | Best Example |
|------------|--------|---------|--------------|
| **Core Runtime** | | | |
| Auto-registration | Core | All samples | S0 (minimal) |
| KoanEnv | Core | S4* (config), S10 (demo) | S4* (DevHub) |
| Boot diagnostics | Core | S10, S14 | S10 (DevPortal) |
| **Data & Storage** | | | |
| Entity<T> patterns | Data | All samples | S1 (tutorial) |
| Multi-provider transparency | Data | S10, S14 | S14 (benchmarks) |
| Backup & restore | Data | S6* | S6* (MediaHub) |
| Object storage | Data | S6*, S16 | S6* (MediaHub) |
| Relationships | Data | S1, S10 | S1 (hierarchical) |
| Pagination & streaming | Data | S1, S5, S10 | S5 (comprehensive) |
| **Web & APIs** | | | |
| EntityController<T> | Web | S1, S5, S7, S8, S10 | S1 (basic), S10 (demo) |
| Authentication | Web | S5, S7 | S5 (production-ready) |
| Authorization & roles | Web | S7 | S7 (TechDocs) |
| Swagger/OpenAPI | Web | S7, S8, S10 | S10 (DevPortal) |
| GraphQL | Web | *(none yet)* | *(planned for S2*)* |
| Response transformers | Web | S1, S5 | S5 (Recs) |
| **Messaging & Async** | | | |
| RabbitMQ messaging | Messaging | S3* | S3* (NotifyHub) |
| Redis Inbox (idempotency) | Messaging | S3* | S3* (NotifyHub) |
| Outbox pattern | Messaging | S9* | S9* (OrderFlow) |
| CQRS | Messaging | S9* | S9* (OrderFlow) |
| Scheduling | Messaging | S3* | S3* (NotifyHub) |
| Retry patterns | Messaging | S3* | S3* (NotifyHub) |
| **AI, Media & Search** | | | |
| AI integration | AI | S5, S16 | S16 (vision + MCP) |
| Vector search | AI | S5, S16 | S5 (hybrid search) |
| Embeddings | AI | S5, S16 | S5 (semantic) |
| Media pipelines | AI | S6*, S16 | S6* (MediaHub) |
| Image processing | AI | S6*, S16 | S6* (comprehensive) |
| **Secrets** | | | |
| Secret management | Secrets | S4* | S4* (DevHub) |
| Vault integration | Secrets | S4* | S4* (DevHub) |
| Environment-specific config | Secrets | S4*, S10 | S4* (progressive) |
| Secret rotation | Secrets | S4* | S4* (DevHub) |
| **Recipes & Orchestration** | | | |
| Observability recipe | Recipes | *(partial in many)* | *(dedicated sample planned)* |
| CLI tooling | Orchestration | *(passive)* | *(underutilized)* |
| Docker Compose | Orchestration | S3*, S5, S10, S14 | S14 (multi-provider) |
| Container patterns | Orchestration | S5, S10, S14 | S14 (AdapterBench) |
| **Domain Pipelines** | | | |
| Canon Runtime | Canon | S8 | S8 (Canon) |
| Pipeline contributors | Canon | S8 | S8 (validation/enrichment) |
| MCP integration | Canon | S16 | S16 (Code Mode) |
| MCP entity tools | Canon | S16 | S16 (PantryPal) |
| Dapr integration | Canon | *(none yet)* | *(future S18*)* |

**Legend**: `*` = Planned sample | `(none yet)` = Gap identified

---

## Sample Descriptions

### 🌱 Fundamentals

#### S0.ConsoleJsonRepo - Minimal Bootstrap

**What**: Ultra-minimal console application showing Koan bootstrap in ~20 lines

**Purpose**: First exposure to Koan - demonstrates "Reference = Intent" philosophy

**Key Features**:
- `services.StartKoan()` - one-line bootstrap
- Entity<T> with GUID v7 auto-generation
- JSON file storage adapter
- Batch operations and streaming

**Run**: `./start.bat` from S0.ConsoleJsonRepo directory

**Learning Time**: 5-10 minutes

**Best For**: Absolute beginners, quick proof-of-concept

---

#### S1.Web - Entity Relationships & CRUD

**What**: Todo management web app with hierarchical relationships

**Purpose**: Learn Entity<T> patterns, relationships, and web fundamentals

**Key Features**:
- Entity relationships (User → Todo → TodoItem)
- CRUD operations with EntityController<T>
- Relationship navigation (GetParent, GetChildren)
- Pagination with RFC 5988 Link headers
- Streaming operations
- Minimal AngularJS UI

**Run**: `./start.bat` opens browser to http://localhost:5044

**Learning Time**: 30-45 minutes

**Best For**: Developers new to Koan wanting comprehensive CRUD patterns

---

### 🎯 Framework Showcase

#### S10.DevPortal - Multi-Provider Capability Demo

**What**: Live demonstration of Koan's multi-provider transparency and capability detection

**Purpose**: Framework evaluation - see provider switching in action

**Key Features**:
- **Live provider switching** - MongoDB ↔ PostgreSQL ↔ SQLite without code changes
- Capability detection with runtime reporting
- Bulk operations (1000+ records)
- Set routing demonstration (published/draft)
- Relationship navigation showcase
- Performance metrics comparison

**Run**: `./start.bat` opens browser to http://localhost:5134

**Learning Time**: 20-30 minutes (demo-focused)

**Best For**: Evaluators comparing frameworks, understanding Koan's differentiators

---

#### S14.AdapterBench - Performance Benchmarking

**What**: Objective performance comparison across SQLite, PostgreSQL, MongoDB, Redis

**Purpose**: Provide data for informed provider selection decisions

**Key Features**:
- Three entity complexity tiers (minimal, indexed, complex)
- Write performance (single, batch, bulk)
- Read performance (by ID, query, streaming)
- Sequential vs parallel mode testing
- Real-time progress via SignalR
- Interactive Chart.js visualizations
- Use case recommendations (not single "winner")

**Run**: `./start.bat` opens browser to http://localhost:5174

**Learning Time**: 10-15 minutes to run, review results

**Best For**: Architecture decisions, provider selection, performance requirements

---

### 🤖 AI & Intelligence

#### S5.Recs (AnimeRadar) - Recommendation Engine

**What**: Complete recommendation system with AI, vector search, and personalization

**Purpose**: Flagship sample showing production-ready AI integration patterns

**Key Features**:
- Hybrid recommendation engine (popularity + preferences + semantic)
- Vector search with Weaviate
- AI embeddings via Ollama
- User preference modeling (EWMA)
- Semantic search with graceful fallback
- Admin data seeding tools
- Test authentication with role-based access
- Comprehensive tutorial-style README

**Run**: `./start.bat` opens browser to http://localhost:5084

**Learning Time**: 1-2 hours (comprehensive)

**Best For**: Teams building recommendation systems, AI-powered search, or learning Koan comprehensively

**External Dependencies**: MongoDB (required), Weaviate (optional, graceful degradation), Ollama (optional)

---

#### S16.PantryPal - Vision AI & MCP Code Mode

**What**: AI-powered meal planning with computer vision and MCP orchestration

**Purpose**: Showcase vision AI integration and MCP Code Mode patterns

**Key Features**:
- Computer vision for grocery item detection
- Bounding boxes with multi-candidate selection
- Natural language input parsing
- MCP Code Mode for multi-step workflows
- Recipe suggestion engine
- Entity-first patterns throughout
- Comprehensive testing examples

**Run**: `./start.bat` (split architecture: API + MCP host)

**Learning Time**: 1-1.5 hours

**Best For**: AI-powered applications, MCP integration, vision processing

**External Dependencies**: MongoDB, optional Ollama for vision model

---

### 📦 Integration & Operations

#### S3.NotifyHub - Multi-Channel Notifications *(Planned)*

**What**: SaaS notification delivery platform with queue-based architecture

**Purpose**: Demonstrate messaging, inbox, and scheduling patterns comprehensively

**Planned Features**:
- Multi-channel delivery (email, SMS, push, webhooks)
- RabbitMQ queue-based routing
- Redis Inbox for idempotency
- Scheduled delivery with Koan.Scheduling
- Retry with exponential backoff
- Dead letter queue for failures
- Batch campaigns (10k+ notifications)
- Real-time delivery tracking

**Status**: 🔨 **In Development** (Phase 2: Weeks 3-6)

**Best For**: Learning async messaging patterns, building notification systems

---

#### S4.DevHub - Secret Management & DevOps *(Planned)*

**What**: Development operations dashboard with centralized secret management

**Purpose**: Fill critical enterprise gap in secret management demonstration

**Planned Features**:
- Multi-service integration (GitHub, Slack, AWS, etc.)
- Environment-specific configuration (dev/staging/prod)
- Secret rotation without downtime
- HashiCorp Vault integration
- Audit trail for secret access
- Health monitoring dashboard
- Progressive disclosure (appsettings → Vault)

**Status**: 🔨 **In Development** (Phase 3: Weeks 7-10)

**Best For**: Security-conscious teams, learning secret management patterns, enterprise deployments

---

#### S6.MediaHub - Media Processing Pipeline *(Planned)*

**What**: Photo/video management with processing, storage tiering, and backup

**Purpose**: Demonstrate media, storage, and backup capabilities together

**Planned Features**:
- Upload validation pipeline
- Image resizing and optimization
- Storage profiles (hot/cold tiering)
- Koan.Data.Backup demonstration
- CDN integration patterns
- Batch processing
- Media metadata management

**Status**: 🔨 **In Development** (Phase 4: Weeks 11-15)

**Best For**: Media-centric applications, learning storage patterns, backup/restore workflows

---

### 🚀 Advanced Patterns

#### S8.Canon - Canon Runtime Pipelines

**What**: Customer canonization pipeline with validation and enrichment phases

**Purpose**: Demonstrate Canon Runtime's pipeline-based data processing

**Key Features**:
- CanonEntity<T> with auto GUID v7
- Pipeline phase contributors (validation, enrichment)
- Business rule validation
- Data enrichment (computed fields, tier assignment)
- Auto-registration of pipeline contributors
- CanonEntitiesController<T> for API surface
- Complete entity lifecycle tracking

**Run**: `dotnet run` from S8.Canon directory

**Learning Time**: 45-60 minutes

**Best For**: Complex data validation pipelines, ETL workflows, data quality systems

---

#### S9.OrderFlow - Event Sourcing & CQRS *(Planned)*

**What**: E-commerce order management with full audit trail via event sourcing

**Purpose**: Demonstrate event sourcing, CQRS, and outbox patterns

**Planned Features**:
- Complete event sourcing (OrderPlaced, PaymentReceived, OrderShipped)
- Event replay to reconstruct state
- Multiple read models (CQRS): customer view, support view, analytics
- Outbox pattern for reliable messaging
- Compensating transactions (refunds, cancellations)
- Snapshots for performance
- Time-travel queries
- Event versioning strategies

**Status**: 🔨 **In Development** (Phase 5: Weeks 16-21)

**Complexity**: ⭐⭐⭐⭐ **Expert-level** - requires understanding of event sourcing trade-offs

**Best For**: E-commerce, fintech, systems requiring complete audit trails, architects evaluating event sourcing

---

## Guide Resources

### g1c1.GardenCoop

Location: `samples/guides/g1c1.GardenCoop/`

**What**: Guided narrative demonstrating Koan patterns through a community garden management scenario

**Purpose**: Part of larger guide narrative, instructional sequence

**Best For**: Following structured learning paths, understanding framework patterns in context

---

## Archived Samples

Several samples have been archived as part of the strategic realignment (see DX-0045). These are preserved in `samples/archive/` for reference but are not maintained:

- S2 (unclear purpose) → See S1 or S10 instead
- S4.Web (no README) → See S1 instead
- S6.Auth (redundant) → See S5 or S7 for auth patterns
- S6.SocialCreator (unclear status) → See S5 or S16 for complex apps
- S12.MedTrials (sparse docs) → See S16 for MCP patterns
- S15.RedisInbox (too minimal) → See S3* for comprehensive inbox patterns
- KoanAspireIntegration → Integration example, not sample app

See `samples/archive/ARCHIVED.md` for migration guidance.

---

## Getting Started

### For Absolute Beginners

1. **S0.ConsoleJsonRepo** (5 min) - See Koan bootstrap in action
2. **S1.Web** (30 min) - Learn Entity<T> and CRUD patterns
3. **S10.DevPortal** (20 min) - Understand framework capabilities

### For Intermediate Developers

1. **S5.Recs** (1-2 hours) - Comprehensive production-ready app
2. **S14.AdapterBench** (15 min) - Understand provider selection
3. **S16.PantryPal** (1 hour) - Advanced AI integration

### For Architects & Evaluators

1. **S10.DevPortal** - Framework differentiators (multi-provider, capability detection)
2. **S14.AdapterBench** - Objective performance data
3. **S5.Recs** - Production-ready patterns
4. **S8.Canon** or **S9.OrderFlow*** - Advanced architectural patterns

---

## Contributing

Found an issue or want to suggest improvements? Samples follow these conventions:

1. **README structure**: Follow S5.Recs template (tutorial-style)
2. **Testing**: Include test examples demonstrating key patterns
3. **Documentation**: Explain *why* not just *what*
4. **Progressive complexity**: Start simple, add sophistication incrementally

See the main repository CONTRIBUTING.md for details.

---

## References

- **Framework Documentation**: `docs/architecture/capability-map.md`
- **Sample Organization**: `samples/README.md`
- **Port Allocations**: `docs/decisions/OPS-0014-samples-port-allocation.md`
- **Strategic Plan**: `docs/decisions/DX-0045-sample-collection-strategic-realignment.md`

---

**Maintained by**: Koan Framework Team
**Questions?**: GitHub Issues or Community Discussions

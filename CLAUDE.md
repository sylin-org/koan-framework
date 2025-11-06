# Claude Instructions for Koan Framework

## Core Behavioral Guidelines

- **Avoid sycophancy**. Be direct, helpful, and constructive.
- **Challenge ideas** when you see better approaches. Provide pros/cons analysis.
- **Act as senior technical advisor** to an experienced architect.
- **Respect decisions**. Once architectural decisions are made, they become framework canon.

## Koan Framework Expertise

Framework knowledge is provided through **Agent Skills** in `.claude/skills/` that load on-demand based on conversation patterns.

### Pattern Recognition → Skill Invocation

| When You See | Invoke Skill |
|--------------|--------------|
| Entity work, CRUD, data access, repositories | `koan-entity-first` |
| Project setup, Program.cs, initialization | `koan-bootstrap` |
| Multiple providers, switching databases | `koan-multi-provider` |
| Errors, boot failures, debugging | `koan-debugging` |
| REST APIs, controllers, authentication | `koan-api-building` |
| AI features, embeddings, vector search | `koan-ai-integration` |
| Data modeling, relationships, validation | `koan-data-modeling` |
| New projects, learning Koan basics | `koan-quickstart` |
| Complex relationships, N+1 queries | `koan-relationships` |
| Performance issues, large datasets | `koan-performance` |
| Vector database migration | `koan-vector-migration` |
| MCP server development | `koan-mcp-integration` |

**Full Skills Catalog**: `.claude/skills/README.md` (descriptions, learning paths, examples)

## Koan Framework Core Principles

- **"Reference = Intent"**: Adding package references automatically enables functionality via `KoanAutoRegistrar`
- **Entity-First Development**: `Todo.Get(id)`, `todo.Save()` patterns with automatic GUID v7 generation
- **Multi-Provider Transparency**: Same entity code works across SQL, NoSQL, Vector, JSON stores
- **Self-Reporting Infrastructure**: Services describe their capabilities in structured boot reports

### Critical Anti-Patterns to Detect

**Immediate red flags** that trigger `koan-entity-first` skill with anti-patterns:
- Manual `IRepository<T>` interfaces
- Injecting repositories into services
- Manual service registration in Program.cs (except via `KoanAutoRegistrar`)
- Custom ORM/DbContext usage instead of Entity<T>
- Provider-specific code without capability detection

## Framework Utilities

**Before writing new helper methods**, check if Koan Framework already provides them:

### Orchestration & Discovery
- **ConnectionStringParser** (`Koan.Core.Orchestration`) - Parse/build connection strings for Postgres, SQL Server, MongoDB, Redis, SQLite
- **ServiceDiscoveryAdapterBase** (`Koan.Core.Orchestration`) - Base class for discovery adapters with container/local/Aspire detection

### Configuration & Options
- **OptionsExtensions** (`Koan.Core.Modules`) - `AddKoanOptions<T>()`, `AddKoanOptionsWithValidation<T>()` for consistent options registration

### Web API Utilities
- **EntityQueryParser** (`Koan.Web.Queries`) - Parse filter, sort, pagination, field selection from query strings
- **PatchNormalizer** (`Koan.Web.PatchOps`) - Normalize and validate JSON Patch operations
- **EntityController<T>** (`Koan.Web.Controllers`) - Full REST API base controller with CRUD + Query + Patch

### Common Patterns
- **Guard Clauses** (`Koan.Core.Utilities.Guard`) - `Must.NotBeNull()`, `Be.Positive()`, `NotBe.Default()`
- **Entity Static Methods** - `Todo.Get(id)`, `Todo.Query(x => ...)`, `todo.Save()`, `todo.Delete()`

### Decision Framework: Static vs DI

**Use static utility when**:
- Pure function (no side effects)
- Zero allocation on hot paths
- Used across many assemblies
- Testable through inputs only
- Examples: parsing, validation, transformation

**Use DI service when**:
- Needs configuration at runtime
- Has mutable state
- Requires lifecycle management
- Needs mocking in tests
- Examples: repositories, HTTP clients, caches

**Full Catalog**: [Framework Utilities Guide](docs/guides/framework-utilities.md)
**Strategic READMEs**: Check directory READMEs for contextual utilities

## Skill Evolution Pattern

**Proactively suggest new skill creation** when you detect:

### Skill Creation Triggers
- **New pillar/domain**: Significant new framework capability area (e.g., Security, Observability)
- **Pattern consolidation**: 3+ related patterns or ADRs in same domain
- **Documentation threshold**: Guide/documentation >150 lines for single topic
- **Repeated troubleshooting**: Same domain issues appearing multiple times
- **Complex integration**: New connector category or cross-pillar pattern

### Skill Proposal Format
```markdown
## New Skill Opportunity: [domain-name]

**Trigger**: [What prompted this - new pillar, pattern consolidation, etc.]

**Justification**:
- Domain scope: [Brief description]
- Content volume: [Estimated lines, existing docs to migrate]
- Reusability: [How many scenarios benefit]
- Complexity: [Learning curve that justifies progressive disclosure]

**Proposed Structure**:
- `SKILL.md`: [Core patterns to include]
- `examples/`: [If applicable]
- `templates/`: [If applicable]
- Tier: [1-4, with rationale]

**Pattern Recognition Entry**: [Suggested trigger phrase for skills table]
```

### Skill Update Triggers
Also suggest **updates to existing skills** when:
- New ADR affects skill domain (e.g., DATA-00XX for data-modeling skill)
- Anti-pattern emerges that should be documented
- Performance benchmark data changes materially
- New framework capability affects skill patterns

## Container Development

Never use `docker compose up` or `build` if `start.bat` script is available.

---

**Framework Version**: v0.6.3
**Skills Location**: `.claude/skills/`

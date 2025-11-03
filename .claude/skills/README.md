# Koan Framework Agent Skills Catalog

This directory contains specialized Agent Skills that provide progressive, context-aware guidance for Koan Framework development. Skills load on-demand based on conversation context, delivering exactly the knowledge needed without overwhelming developers.

## What Are Agent Skills?

Agent Skills are filesystem-based capability packages that:
- **Load progressively** - Only activated when relevant to current conversation
- **Bundle resources** - Include code examples, templates, and anti-patterns without consuming context
- **Stay focused** - Each skill covers a specific framework domain or workflow
- **Reference guides** - Point to authoritative how-to guides while providing curated excerpts

## Skill Catalog

### Tier 1: Core Framework Skills (Foundational)

| Skill | Description | When to Use |
|-------|-------------|-------------|
| **koan-entity-first** | Entity<T> patterns, GUID v7 auto-generation, static methods vs manual repositories | Creating entities, data access, CRUD operations, refactoring repositories |
| **koan-bootstrap** | Auto-registration via KoanAutoRegistrar, minimal Program.cs, "Reference = Intent" pattern | Setting up projects, initialization issues, debugging bootstrap |
| **koan-multi-provider** | Provider transparency, capability detection, context routing (partition/source/adapter) | Multi-database scenarios, switching providers, capability-aware code |
| **koan-debugging** | Framework-specific troubleshooting, boot report analysis, common error patterns | Troubleshooting issues, analyzing logs, debugging initialization |

### Tier 2: Pillar-Specific Skills (Domain-Triggered)

| Skill | Description | When to Use |
|-------|-------------|-------------|
| **koan-data-modeling** | Aggregate boundaries, relationships, lifecycle hooks, value objects | Designing domain models, complex entities, business logic encapsulation |
| **koan-api-building** | EntityController<T>, custom routes, payload transformers, auth policies | Building REST APIs, custom endpoints, authentication, API design |
| **koan-ai-integration** | Chat endpoints, embeddings, RAG workflows, vector search | Integrating AI features, semantic search, chat interfaces, embeddings |

### Tier 3: Developer Journey Skills (Progressive Complexity)

| Skill | Description | When to Use |
|-------|-------------|-------------|
| **koan-quickstart** | Zero to first Koan app in under 10 minutes (S0 + S1 patterns) | Starting new projects, learning Koan basics, quick prototypes |
| **koan-relationships** | Entity navigation, batch loading, relationship best practices | Complex data relationships, navigation patterns, performance optimization |
| **koan-performance** | Streaming, pagination, count strategies, bulk operations | Performance tuning, large datasets, optimization, production readiness |

### Tier 4: Specialized Skills (Advanced Scenarios)

| Skill | Description | When to Use |
|-------|-------------|-------------|
| **koan-vector-migration** | Vector export/import, embedding caching, provider migration | Migrating vector databases, caching embeddings, AI provider switches |
| **koan-mcp-integration** | MCP server patterns, Code Mode integration, tool building | Building MCP servers, Claude integrations, tool development |

## Skill Invocation

### Automatic Triggers

Skills load automatically based on conversation patterns:

- **Entity work** triggers `koan-entity-first`
- **Project setup** triggers `koan-bootstrap`
- **API development** triggers `koan-api-building`
- **Error troubleshooting** triggers `koan-debugging`
- **AI features** triggers `koan-ai-integration`

### Explicit Invocation

You can explicitly invoke skills using the Skill tool:

```markdown
User: "I need help with [specific task]"
Assistant: [Invokes /koan-entity-first skill]
```

## Progressive Learning Path

**Recommended skill progression for new developers:**

1. **koan-quickstart** (10 min) - First app, basic concepts
2. **koan-entity-first** (Core foundation) - Master Entity<T> patterns
3. **koan-api-building** (Expose data) - Build REST APIs
4. **koan-relationships** (Complex models) - Advanced data modeling
5. **koan-performance** (Optimization) - Production readiness
6. **koan-ai-integration** (Semantic features) - Add AI capabilities
7. **koan-vector-migration** (Advanced AI) - Scale AI infrastructure

## Skill Structure

Each skill contains:

```
skill-name/
├── SKILL.md                    # Main instructions with YAML frontmatter
├── examples/                   # Executable code examples
│   ├── basic-pattern.cs
│   └── advanced-pattern.cs
├── templates/                  # Project templates
│   └── template-file.cs
├── anti-patterns/              # What NOT to do
│   └── common-mistakes.md
└── diagnostics/                # Troubleshooting guides
    └── checklist.md
```

## Benefits Over Monolithic Instructions

### Context Efficiency
- **Before:** 1,000+ lines loaded every conversation (monolithic instruction files)
- **After:** ~100 lines meta + on-demand skills (~200-400 lines each)
- **Result:** 60-80% context reduction for typical conversations

### Developer Experience
- **Progressive disclosure** - See guidance exactly when needed
- **Just-in-time learning** - No overwhelming pattern dumps
- **Focused guidance** - Each skill covers one domain deeply

### Maintainability
- **Modular updates** - Change one skill without touching others
- **Clear boundaries** - Each skill owns specific framework domain
- **Testable** - Validate individual skills in isolation

## Relationship to Documentation

Skills complement, don't replace, documentation:

- **How-to Guides** (`/docs/guides/`) remain authoritative references
- **Skills** provide curated excerpts and progressive learning paths
- **ADRs** (`/docs/decisions/`) document canonical architectural decisions
- **Samples** (`/samples/`) demonstrate patterns in working code

Skills act as **intelligent indexes** into the full documentation, loading exactly what's needed for the current conversation.

## Framework Version

**Aligned with:** Koan Framework v0.6.3
**Last Updated:** 2025-11-03

## Migration Notes

This skills structure represents a complete migration from monolithic instruction files to modular, progressive skills:
- **CLAUDE.md** - Slimmed to meta-instructions only (65% reduction); all pattern content distributed to skills
- **Previous instruction files** - Deprecated and removed; all coding guidelines now in domain-specific skills

All content has been reorganized into focused, discoverable skills. See individual skill directories for specific guidance domains.

---

**Quick Reference:** Browse skill directories below to see available guidance domains. Each SKILL.md file contains progressive instructions designed for AI-assisted development.

---
type: REFERENCE
domain: core
title: "Koan capabilities"
audience: [ai-agents, developers]
status: current
last_updated: 2026-08-23
framework_version: v1.0.0
validation:
  status: not-yet-tested
  scope: docs/capabilities/index.md
---

# Koan capabilities

Fetch this page first, follow one domain, then follow one named outcome. Domain pages route;
capability nodes state the pieces, binding constraint, and deployment choice; leaves carry working
Entity code and provider mechanics. Stop when the leaf lets you act.

- **Start with `Entity<T>`** - see the common verbs, contexts, policy hooks, projections, work,
  intelligence, and specialized Entity shapes that can attach to one business noun:
  [Entity capability hooks](data/entities.md)
- **Compose a whole solution** - use a receipt that unions packages, Entity shapes, runtime
  dependencies, inherited constraints, and three-part proof across several capabilities:
  [solution compositions](solutions.md)

- **AI** - chat, embeddings, semantic search, vision, reasoning, model operations, evaluation:
  [ai.md](ai.md)
- **Data** - Entity grammar, relationships, stores, named sources, migration and cutover:
  [data.md](data.md)
- **Web** - entity controllers, OpenAPI, SSE, and the frontend topology decision:
  [web.md](web.md)
- **Trust and isolation** - sign-in, access rules, tenants, field protection:
  [trust.md](trust.md)
- **Work and integration** - background jobs, events and transport:
  [work.md](work.md)
- **State and content** - cache, entity-owned files, media:
  [state.md](state.md)
- **Agent surfaces** - MCP tools and resources over your entities:
  [agents.md](agents.md)
- **Trusted records** - reconcile messy arrivals into canonical entities:
  [records.md](records.md)
- **Operations** - single-binary publish, observability, hardening:
  [operations.md](operations.md)

The one-screen summary of every capability and its maturity lives in the
[capability map](../reference/capability-map.md).

## Application destinations

Use these when the request is about the whole application rather than one capability:

1. **Idea to local POC** - [turn an idea into a running application](../recipes/poc-an-idea.md)
2. **POC to shared prototype** - [let people outside your machine test it](../recipes/share-a-prototype.md)
3. **Prototype to production claim** - [harden for production](../recipes/harden-for-production.md)

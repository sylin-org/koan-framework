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

Fetch this page first, follow one link, and stop when you can act. Each domain line routes to a
page that carries decision guidance and links the working recipe. Domains without a dedicated node
yet link the current best leaf directly - the tree deepens with evidence.

- **AI** - chat, embeddings, semantic search, vision, reasoning, model operations, evaluation:
  [ai.md](ai.md)
- **Data** - entity stores, vector stores, migration and cutover:
  [data reference](../reference/data/index.md) · cutover: [harden-for-production](../recipes/harden-for-production.md)
- **Web** - entity controllers, OpenAPI, SSE, and the frontend topology decision:
  [serve-a-web-frontend](../recipes/serve-a-web-frontend.md)
- **Trust and isolation** - sign-in, access rules, tenants, field protection:
  [let-people-sign-in](../recipes/let-people-sign-in.md) · [isolate-tenants](../recipes/isolate-tenants.md)
- **Work and integration** - background jobs, events and transport:
  [run-work-in-background](../recipes/run-work-in-background.md) · [tell-another-system](../recipes/tell-another-system.md)
- **State and content** - cache, entity-owned files, media:
  [make-repeated-reads-fast](../recipes/make-repeated-reads-fast.md) · [accept-and-serve-files](../recipes/accept-and-serve-files.md)
- **Agent surfaces** - MCP tools and resources over your entities:
  [let-an-agent-use-my-app](../recipes/let-an-agent-use-my-app.md)
- **Trusted records** - reconcile messy arrivals into canonical entities:
  [reconcile-messy-arrivals](../recipes/reconcile-messy-arrivals.md)
- **Operations** - single-binary publish, observability, hardening:
  [ship-a-single-binary](../recipes/ship-a-single-binary.md) · [harden-for-production](../recipes/harden-for-production.md)

The one-screen summary of every capability and its maturity lives in the
[capability map](../reference/capability-map.md).

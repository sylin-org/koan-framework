# Koan Framework Documentation

**Build services like you're talking to your code, not fighting it.**

Welcome to the complete Koan Framework documentation. This restructured documentation provides clear, validated, and maintainable guidance for building modern .NET backend services.

## Getting Started

- **[5-Minute Quickstart](getting-started/quickstart.md)** – Get a Koan API running in minutes
- **[Framework Overview](getting-started/overview.md)** – Architecture, philosophy, and capabilities
- **[Enterprise Adoption](getting-started/enterprise-adoption.md)** – Guidance for larger teams rolling out Koan

## Reference Documentation

- **[Core](reference/core/index.md)** – Foundation, auto-registration, semantic streaming pipelines
- **[Data](reference/data/index.md)** – Entities, providers, queries, multi-storage patterns
- **[Web](reference/web/index.md)** – Controllers, authentication, GraphQL, HTTP endpoints
- **[AI](reference/ai/index.md)** – Chat, embeddings, vector search, RAG patterns
- **[Flow](reference/flow/index.md)** – Pipelines, identity resolution, event sourcing
- **[Messaging](reference/messaging/index.md)** – Events, queues, handlers, reliable delivery
- **[Storage](reference/storage/index.md)** – File/blob handling with profile routing
- **[Orchestration](reference/orchestration/index.md)** – DevHost CLI, container management

## Task-Oriented Guides

- **[Building APIs](guides/building-apis.md)** – REST and GraphQL API development
- **[Authentication Setup](guides/authentication-setup.md)** – OIDC, SAML, multi-provider auth
- **[Data Modeling](guides/data-modeling.md)** – Entity design, relationships, providers
- **[AI Integration](guides/ai-integration.md)** – Adding intelligence to applications
- **[Semantic Pipelines](guides/semantic-pipelines.md)** – Streaming data processing with AI integration
- **[Performance Optimization](guides/performance.md)** – Query optimization, caching, scaling
- **[Expose MCP over HTTP + SSE](guides/mcp-http-sse-howto.md)** – Stream Koan tools to remote IDEs and agents
- **Troubleshooting:** [Adapter Connection Issues](guides/troubleshooting/adapter-connection-issues.md) · [Bootstrap Failures](guides/troubleshooting/bootstrap-failures.md)
- **Deep Dives:** [Auto-Provisioning System](guides/deep-dive/auto-provisioning-system.md) · [Bootstrap Lifecycle](guides/deep-dive/bootstrap-lifecycle.md)

## Architecture & Engineering

- **[Framework Principles](architecture/principles.md)** – Design philosophy and core tenets
- **[Pagination Refactor Plan](architecture/pagination-refactor-plan.md)** – Evolution of query and pagination flow
- **[Entity Pattern Scaling](examples/entity-pattern-scaling.md)** – How Entity<T> patterns grow with product scope

## 📋 Architecture Decision Records

- **[Decision Index](decisions/)** – Complete ADR catalog by domain
- **[High-Signal ADRs](decisions/README.md#high-signal-adrs)** – Curated list of impact-heavy decisions

## 🛠️ Development Support

- **[Troubleshooting Guide](support/troubleshooting.md)** – Common problems and escalation paths
- **[Templates](templates/document-template.md)** – Authoring templates for new documentation
- **[Historical Archive](archive/)** – Deprecated content and previous generation docs

---

## 🎯 Quick Navigation by Role

### **New to Koan?**
1. [5-Minute Quickstart](getting-started/quickstart.md)
2. [Framework Overview](getting-started/overview.md)
3. [Enterprise Adoption](getting-started/enterprise-adoption.md)

### **Building an API?**
1. [Building APIs Guide](guides/building-apis.md)
2. [Data Modeling](guides/data-modeling.md)
3. [Authentication Setup](guides/authentication-setup.md)

### **Adding AI Features?**
1. [AI Integration Guide](guides/ai-integration.md)
2. [Semantic Pipelines](guides/semantic-pipelines.md)
3. [AI Reference](reference/ai/index.md)

### **Going to Production?**
1. [Performance Optimization](guides/performance.md)
2. [Troubleshooting – Adapter Issues](guides/troubleshooting/adapter-connection-issues.md)
3. [Troubleshooting – Bootstrap Failures](guides/troubleshooting/bootstrap-failures.md)

### **Enterprise Architecture?**
1. [Framework Principles](architecture/principles.md)
2. [Pagination Refactor Plan](architecture/pagination-refactor-plan.md)
3. [Entity Pattern Scaling](examples/entity-pattern-scaling.md)

---

## 📝 Documentation Standards

### Content Types
- **REF**: Reference documentation (API specs, technical details)
- **GUIDE**: Task-oriented how-to content
- **ARCH**: High-level architectural documentation
- **DEV**: Development patterns and engineering guides
- **SUPPORT**: Troubleshooting and problem-solving content

### Quality Standards
All documentation (except ADRs) undergoes **correctness validation** against:
- Current framework version compatibility
- Code example accuracy and testing
- API reference consistency
- Configuration example validation
- Link integrity and navigation flow

### Frontmatter Standard
```yaml
---
type: REF | GUIDE | ARCH | DEV | SUPPORT
domain: core | data | web | ai | flow | messaging | storage | media
title: "Descriptive Title"
audience: [developers, architects, ai-agents]
last_updated: 2025-01-17
framework_version: "v0.2.18+"
status: current | deprecated | draft
validation: {date-last-tested}
---
```

---

**Need help?** Start with our [support troubleshooting guide](support/troubleshooting.md) or explore targeted [domain guides](guides/).
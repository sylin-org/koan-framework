# Koan Framework Documentation

**Build services like you're talking to your code, not fighting it.**

Welcome to the complete Koan Framework documentation. This restructured documentation provides clear, validated, and maintainable guidance for building modern .NET backend services.

## Getting Started

- **[5-Minute Quickstart](getting-started/quickstart.md)** - Get a Koan API running in 5 minutes
- **[Framework Overview](getting-started/overview.md)** - Architecture, philosophy, and capabilities
- **[Installation Guide](getting-started/installation.md)** - Step-by-step setup and configuration

## Reference Documentation

### Core Framework Components
- **[Core](reference/core/)** - Foundation, auto-registration, health checks
- **[Data](reference/data/)** - Entities, providers, queries, multi-storage
- **[Web](reference/web/)** - Controllers, authentication, GraphQL, HTTP
- **[AI](reference/ai/)** - Chat, embeddings, vector search, RAG patterns
- **[Flow](reference/flow/)** - Data pipelines, identity resolution, event sourcing
- **[Messaging](reference/messaging/)** - Events, queues, handlers, reliable delivery
- **[Storage](reference/storage/)** - File/blob handling with profile routing
- **[Media](reference/media/)** - Media processing, transforms, HTTP endpoints
- **[Orchestration](reference/orchestration/)** - DevHost CLI, container management
- **[Scheduling](reference/scheduling/)** - Background jobs, startup tasks

## Task-Oriented Guides

### Common Development Tasks
- **[Building APIs](guides/building-apis.md)** - REST and GraphQL API development
- **[Authentication Setup](guides/authentication-setup.md)** - OIDC, SAML, multi-provider auth
- **[Data Modeling](guides/data-modeling.md)** - Entity design, relationships, providers
- **[Performance Optimization](guides/performance.md)** - Query optimization, caching, scaling
- **[Testing Patterns](guides/testing.md)** - Unit, integration, and end-to-end testing
- **[Container Deployment](guides/deployment.md)** - Docker, Compose, orchestration

### Specialized Implementations
- **[AI Integration](guides/ai-integration.md)** - Adding intelligence to applications
- **[Expose MCP over HTTP + SSE](guides/mcp-http-sse-howto.md)** - Stream Koan tools to remote IDEs and agents
- **[Event-Driven Architecture](guides/event-driven.md)** - Messaging and event sourcing
- **[Multi-Provider Data](guides/multi-provider-data.md)** - SQL, NoSQL, Vector, JSON stores
- **[Media Handling](guides/media-handling.md)** - File uploads, transforms, streaming

## Architecture & Engineering

### High-Level Architecture
- **[Framework Principles](architecture/principles.md)** - Design philosophy and core tenets
- **[Multi-Provider Strategy](architecture/multi-provider-strategy.md)** - Storage backend abstraction
- **[Container-Native Design](architecture/container-native.md)** - Framework positioning and deployment
- **[Security Architecture](architecture/security.md)** - Authentication, authorization, secrets

### Development Patterns
- **[Entity-First Development](development/entity-first-patterns.md)** - Primary development approach
- **[Auto-Registration Patterns](development/auto-registration.md)** - Service discovery and DI
- **[Testing Strategies](development/testing-strategies.md)** - Framework-specific testing approaches
- **[Error Handling](development/error-handling.md)** - Consistent error patterns
- **[Configuration Management](development/configuration.md)** - Environment-aware configuration

## 📋 Architecture Decision Records

- **[Decision Index](decisions/)** - Complete ADR catalog by domain
- **[High-Signal ADRs](decisions/README.md#high-signal-adrs)** - Critical architectural decisions

## 🛠️ Development Support

### Troubleshooting & Support
- **[Common Issues](troubleshooting/common-issues.md)** - Frequent problems and solutions
- **[Performance Issues](troubleshooting/performance.md)** - Debugging slow queries and bottlenecks
- **[Container Issues](troubleshooting/containers.md)** - Docker and orchestration problems
- **[Provider Issues](troubleshooting/providers.md)** - Database and storage adapter problems

### Development Tools
- **[Templates & Scaffolding](development/templates.md)** - Project and component templates
- **[CLI Tools](development/cli-tools.md)** - DevHost and utility commands
- **[IDE Integration](development/ide-setup.md)** - Development environment configuration

## 📜 Historical Documentation

- **[Archive](archive/)** - Historical implementation documents and deprecated content

---

## 🎯 Quick Navigation by Role

### **New to Koan?**
1. [5-Minute Quickstart](getting-started/quickstart.md)
2. [Framework Overview](getting-started/overview.md)
3. [Installation Guide](getting-started/installation.md)

### **Building an API?**
1. [Building APIs Guide](guides/building-apis.md)
2. [Data Modeling](guides/data-modeling.md)
3. [Authentication Setup](guides/authentication-setup.md)

### **Adding AI Features?**
1. [AI Integration Guide](guides/ai-integration.md)
2. [AI Reference](reference/ai/)
3. [Performance Optimization](guides/performance.md)

### **Going to Production?**
1. [Container Deployment](guides/deployment.md)
2. [Performance Optimization](guides/performance.md)
3. [Troubleshooting](troubleshooting/)

### **Contributing to Framework?**
1. [Development Patterns](development/)
2. [Testing Strategies](development/testing-strategies.md)
3. [Architecture Decisions](decisions/)

### **Enterprise Architecture?**
1. [Framework Principles](architecture/principles.md)
2. [Multi-Provider Strategy](architecture/multi-provider-strategy.md)
3. [Security Architecture](architecture/security.md)

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

**Need help?** Start with our [troubleshooting guide](troubleshooting/) or explore specific [domain guides](guides/).
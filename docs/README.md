# Koan Framework Documentation

**Build services like you're talking to your code, not fighting it.**

Welcome to the complete Koan Framework documentation. This guide will help you build modern .NET backend services with clarity, comfort, and the ability to grow.

## 🚀 Getting Started

- **[5-Minute Quickstart](quickstart.md)** - Get a Koan API running in 5 minutes
- **[Complete Getting Started Guide](reference/getting-started.md)** - Full step-by-step walkthrough
- **[Framework Overview](reference/framework-overview.md)** - Architecture, philosophy, and capabilities

## 📚 Reference Documentation

### Core Framework
- **[Framework Overview](reference/framework-overview.md)** - Complete architectural overview
- **[Getting Started](reference/getting-started.md)** - Comprehensive onboarding guide

### Framework Pillars
- **[AI Integration](reference/pillars/ai.md)** - Chat, embeddings, vector search, RAG patterns
- **[Authentication](reference/pillars/authentication.md)** - Auth providers, OIDC, SAML, security  
- **[Background Services](reference/pillars/backgroundservices.md)** - Workers, scheduling, lifecycle management
- **[Flow Pipeline](reference/pillars/flow-overview.md)** - Data ingestion, standardization, and projection
- **[Media Management](reference/pillars/media-overview.md)** - File handling, transforms, HTTP endpoints
- **[Messaging](reference/pillars/messaging-overview.md)** - Reliable queues, events, messaging patterns
- **[Orchestration](reference/pillars/orchestration-overview.md)** - DevHost CLI, Docker, Compose generation
- **[Recipes](reference/pillars/recipes-overview.md)** - Best-practice bundles and configurations
- **[Secrets Management](reference/pillars/secrets-management.md)** - Secure secrets handling and providers
- **[Storage](reference/pillars/storage-overview.md)** - File/blob storage with profiles

### Architecture & Design
- **[Usage Patterns](reference/architecture/patterns.md)** - Common patterns and best practices
- **[Advanced Topics](reference/advanced-topics.md)** - Performance, scaling, production patterns

## 🎯 Strategic Documentation

### Framework Assessment & Direction
- **[Framework Strategic Assessment 2025](architecture/framework-assessment-2025.md)** - Comprehensive technical and market evaluation
- **[Implementation Roadmap 2025](architecture/implementation-roadmap-2025.md)** - Actionable improvement priorities with tracking
- **[Container-Native Positioning (ARCH-0054)](decisions/ARCH-0054-framework-positioning-container-native.md)** - Strategic positioning and target market
- **[Observability Over Escape Hatches (PROP-0053)](proposals/PROP-0053-observability-over-escape-hatches.md)** - Enhanced transparency approach

### For Enterprise Architects & Decision Makers
- **Technical Assessment**: Honest evaluation of framework strengths and challenges
- **Market Positioning**: Container/swarm-native stack with complex scenario enablement
- **Implementation Planning**: Tier-based roadmap with success metrics and tracking
- **Strategic Evolution**: Quarterly review process for continuous alignment

## 📖 Guides & How-Tos

### Core Concepts
- **[Core](guides/core/)** - Hosting, composition, profiles, contracts
- **[Data](guides/data/)** - Repositories, entities, queries, adapters
- **[Web](guides/web/)** - Controllers, transformers, GraphQL, health endpoints

### Specialized Topics
- **[Messaging](guides/messaging/)** - Buses, handlers, provisioning patterns
- **[Adapters](guides/adapters/)** - SQLite, SQL Server, Postgres, MongoDB, JSON
- **[AI](guides/ai/)** - Chat, embeddings, vector search, RAG patterns

## 🏗️ Architecture & Decisions

- **[Architecture Principles](architecture/principles.md)** - Design principles and philosophy
- **[Architecture Decisions (ADRs)](decisions/)** - Documented decisions by topic
- **[Domain-Driven Design](ddd/)** - DDD patterns and ubiquitous language

## 🚀 Development & Engineering

- **[Engineering Guide](engineering/)** - Core development patterns and guardrails
- **[Testing Guide](support/testing-guide.md)** - Unit testing, integration testing patterns
- **[Release Process](support/release-process.md)** - Versioning, publishing, CI/CD

## 🛠️ Support & Development

- **[Support Documentation](support/)** - Templates, acceptance criteria, troubleshooting
- **[Adapter Templates](support/)** - Data and vector adapter authoring guides
- **[Configuration Helper](support/configuration-helper.md)** - Configuration patterns and validation
- **[Solution Filters](support/solution-filters.md)** - Development workflow optimization

## 🔍 API Reference

- **[Generated API Documentation](api/)** - Complete API reference
- **[Assembly Overview](api/assemblies.md)** - Package structure and dependencies

## 📋 Troubleshooting & Environment

- **[Environment Setup](support/environment/)** - Development environment configuration
- **[Troubleshooting](support/troubleshooting/)** - Common issues and solutions
- **[NuGet Publishing](support/nuget-publish.md)** - Package publishing guide

## 📜 Historical Documentation

- **[Archive](archive/)** - Historical implementation documents and planning materials

---

## 🎯 Quick Navigation by Role

### **New to Koan?**
1. [5-Minute Quickstart](quickstart.md)
2. [Framework Overview](reference/framework-overview.md)
3. [Getting Started Guide](reference/getting-started.md)

### **Building an API?**
1. [Web Guide](guides/web/) - Controllers and routing
2. [Data Guide](guides/data/) - Persistence and queries
3. [Authentication Guide](reference/pillars/authentication.md)

### **Adding AI?**
1. [AI Guide](guides/ai/) - Chat, embeddings, RAG
2. [Advanced Topics](reference/advanced-topics.md)

### **Going to Production?**
1. [Architecture Principles](architecture/principles.md)
2. [Engineering Guide](engineering/)
3. [Release Process](support/release-process.md)

### **Contributing?**
1. [Engineering Guide](engineering/)
2. [Testing Guide](support/testing-guide.md)
3. [Architecture Decisions](decisions/)

### **Strategic Planning?**
1. [Framework Assessment 2025](architecture/framework-assessment-2025.md)
2. [Implementation Roadmap 2025](architecture/implementation-roadmap-2025.md)
3. [Container-Native Positioning](decisions/ARCH-0054-framework-positioning-container-native.md)
4. [Observability Enhancement](proposals/PROP-0053-observability-over-escape-hatches.md)

---

## 📝 Documentation Standards

All reference documentation includes:
- **Document Type**: REF (Reference), GUIDE (How-to), ADR (Decision)
- **Target Audience**: Developers, Architects, AI Agents
- **Last Updated**: Date of last significant update
- **Framework Version**: Compatible framework versions

---

**Need help?** Check our [troubleshooting guide](support/troubleshooting.md) or explore the [samples](../samples/) directory.
# Reference Architecture: Neutral Data Ingestion, Association, and Projection

This documentation set defines a domain-agnostic reference architecture for high‑quality data ingestion, standardization, key‑based association, idempotent projection, and distribution. It targets local‑first development (containers) with production parity and optional PII/Compliance.

Contents
- Executive Overview (benefits, usage scenarios)
- Enterprise Architecture (EA)
- Enterprise Integration (EI)
- Solution Architecture (SA)
- Software Engineering (SE)

Documents
- 00-executive-overview.md
- 10-enterprise-architecture.md
- 20-enterprise-integration.md
- 30-solution-architecture.md
- 40-software-engineering.md
 - 50-lifecycle-and-developer-guide.md
 - 60-agentic-ai-developer-playbook.md
 - 70-adapter-design-and-dx.md
 - 75-adapter-sdk-surface.md

Notes
- Neutral vocabulary is used across all documents (no domain‑specific entities).
- Decisions captured: MongoDB primary store, RabbitMQ default in dev, DB‑backed PolicyBundle with file fallback, workflow‑lite orchestrator, Koan Entity<> namespaces for collection names, OTEL for observability.

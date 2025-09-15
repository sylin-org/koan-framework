# Koan Support Documentation

This directory contains practical guides to help maintain and evolve Koan as a standalone framework.

## Core Support Guides

- **[Data Instructions](data-instructions.md)** — Instruction API, safety, and SQL sugar
- **[Adapter Checklist](adapter-checklist.md)** — Building adapters (relational, document, vector)  
- **[Testing Guide](testing-guide.md)** — Unit tests, adapter tests, sample verification
- **[Release Process](release-process.md)** — Versioning, changelogs, and publishing
- **[Capability Matrix Endpoint](capability-matrix-endpoint.md)** — Well-known endpoint implementation

## Adapter Development

- **[Data Adapter Acceptance Criteria](data-adapter-acceptance-criteria.md)** — Normative, testable requirements for Data adapters
- **[Data Adapter Template](data-adapter-template.md)** — Template for new data adapters
- **[Vector Adapter Acceptance Criteria](vector-adapter-acceptance-criteria.md)** — Requirements for vector adapters
- **[Vector Adapter Template](vector-adapter-template.md)** — Template for new vector adapters

## Configuration & Environment

- **[Configuration Helper](configuration-helper.md)** — Configuration patterns and validation
- **[Environment Setup](environment/)** — Development environment configuration
- **[Solution Filters](solution-filters.md)** — Development workflow optimization

## Troubleshooting

- **[Troubleshooting Index](troubleshooting/)** — Common issues and solutions
- **[NuGet Publishing](nuget-publish.md)** — Package publishing guide
- **[Release Flow](release-flow.md)** — Release workflow and automation

## Current Adapter Status

**Production Ready:**
- **Data**: SQLite, SQL Server, Postgres, MongoDB, JSON, Redis
- **Messaging**: RabbitMQ, Redis, InMemory
- **Vector**: Weaviate, Redis  
- **Storage**: Local Filesystem
- **AI**: Ollama (local), OpenAI (cloud)

## Historical Documentation

For historical migration guides and archived planning documents, see **[Archive](../archive/)**.

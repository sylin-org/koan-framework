# Koan Docs Index

Note: This page contains historical planning materials. For day-to-day development, start with:

- Engineering front door: engineering/index.md
- Architecture principles: architecture/principles.md
- Web Authentication reference: reference/web-auth.md
  - Challenge supports `?return=` and optional `&prompt=login`; Dev TestProvider uses a remembered-user cookie that central logout clears.

This documentation set tracks the design and implementation plan for the Koan Framework.

Start here:
// Removed tutorial and quickstart links; prefer Engineering and Reference sections.

- 01-proposal.md - New framework proposal (moved from Zen docs), updated for Koan
- 02-composition-and-profiles.md - Composition model and profile presets explained
- 03-core-contracts.md - Core interfaces (data, CQRS, messaging, webhooks, AI)
- 04-adapter-authoring-guide.md - How to build adapters (Relational/Document/Vector)
- 05-samples-plan.md - Sample apps plan and acceptance criteria
  - samples/S5.Recs/README.md - Recommendations sample app (auth-enabled UI)
- 06-generators-considerations.md - Source generator alternatives and decision record
- 07-implementation-plan.md - Milestones tied to samples (S0–S7) for testable delivery
- 09-executive-pitch.md - Executive summary and benefits for enterprise architecture, integration, and dev teams

Additional guides:

- 10-execute-instructions.md - Executing provider instructions and SQL sugar
  // Removed beginner and gentle tutorial pages
- 15-entity-filtering-and-query.md - Filtering via JSON filters and query endpoints with examples
- 16-working-with-entity-data.md - Developer guide to reading, filtering, sets, and migrations
- 17-sqlite-logging-and-governance.md - SQLite adapter logging, tracing, and DDL governance
- 18-sqlserver-adapter.md - SQL Server adapter: setup, options, capabilities, and testing
- ddd/00-index.md - Domain-Driven Design in Koan: ubiquitous language, tactical design, CQRS/eventing, and more

See also: `docs/decisions` for ADRs tracking key architecture choices (capabilities, markers, naming). Notable:

- 0029 - JSON filter language and endpoints
- 0030 - Entity sets routing and storage suffixing
- 0031 - Filter $options.ignoreCase
- 0032 - Paging pushdown and in-memory fallback
- 0033 - OpenTelemetry integration (tracing + metrics)

Support docs:

- support/README.md - maintenance, adapters, testing, releases, migration
- support/08-data-adapter-acceptance-criteria.md - acceptance criteria for all Data adapters

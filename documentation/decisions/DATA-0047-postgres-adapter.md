---
id: DATA-0047
slug: DATA-0047-postgres-adapter
domain: DATA
title: PostgreSQL Adapter
status: Accepted
date: 2025-08-19
---

Context
- We need a relational adapter for PostgreSQL that adheres to the Data Adapter Acceptance Criteria.
- Existing relational tooling (LINQ translator, schema toolkit) and the SQL Server adapter define patterns for consistency.

Decision
- Implement Koan.Data.Postgres using Npgsql with JSONB storage for complex properties, server-side paging, LINQ pushdown via ILinqSqlDialect, and instruction execution.
- Use expression indexes on JSONB extraction for projected properties; generated columns may be added later as an option.
- Support atomic batches via transactions; bulk delete via WHERE Id = ANY(@ids); bulk upsert via INSERT ... ON CONFLICT for now (COPY/staging optional later).
- Expose options under Koan:Data:Postgres with defaults aligned to other relational adapters.
- Participate in OpenTelemetry via ActivitySource without hard dependency.

Consequences
- Consistent developer experience across relational adapters.
- Meets MUST criteria in 08-data-adapter-acceptance-criteria; some SHOULD items (COPY-based upsert, generated columns) are planned follow-ups.

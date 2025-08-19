---
id: DATA-0051
slug: DATA-0051-direct-routing-via-instruction-executors-legacy
domain: DATA
status: Accepted
date: 2025-08-19
title: Direct API routes via adapter instruction executors when entity is specified
---

# ADR 0051: Direct API routes via adapter instruction executors when entity is specified

Context

- The Direct API was implemented as an ADO.NET escape hatch (async-only, parameterized) with Newtonsoft-based mapping.
- Adapters now expose instruction-backed SQL operations, including `relational.sql.query` for neutral rows.
- We want Direct to take advantage of adapter logic (naming/token rewriting, governance, observability) when possible.

Decision

- When the Direct session is created with an entity hint `entity:Namespace.TypeName` and no explicit `WithConnectionString` override is set, Direct delegates to the adapter’s instruction executor:
  - Execute → `InstructionSql.NonQuery`
  - Scalar → `InstructionSql.Scalar`
  - Query → `InstructionSql.Query` (returns neutral rows)
- Fallback to ADO.NET path when an explicit connection string is provided or the entity type cannot be resolved.

Consequences

- Reuses adapter rewriting and safety features automatically.
- Keeps Direct cross-adapter while preserving control: callers can force raw ADO.NET behavior with `WithConnectionString`.
- Aligns Direct with the Instruction Execution API (ADR 0006) and new constants (ADR 0050).

Alternatives considered

- Always use ADO.NET for Direct: simpler but loses adapter behaviors and consistency.
- Require explicit adapter handles in Direct: noisier API and tighter coupling.

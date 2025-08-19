# ADR 0052: Constrain Dapper to relational adapters; Direct uses ADO.NET + Newtonsoft

Date: 2025-08-19

Status: Accepted

## Context

- Early Direct API experiments used Dapper for convenience, which implicitly coupled Direct to relational concerns.
- We need Direct to be adapter-agnostic, portable, and safe by default (async-only, parameterized; see ADR-0049).
- Adapters (SqlServer, Postgres, Sqlite) can internally choose their SQL helpers, but shared layers should not inherit those choices.
- We also need a neutral-row materialization for cross-adapter results and consistent JSON mapping.

## Decision

- Scope Dapper strictly to relational adapters for their internal SQL execution paths.
- Refactor Direct (Sora.Data.Direct) to use pure ADO.NET with parameterized commands, and Newtonsoft.Json for mapping.
- Keep Direct adapter-agnostic; when source is an entity hint and no explicit connection override is provided, delegate to adapter instruction executors per ADR-0051 (using InstructionSql helpers and reflection to call DataServiceExecuteExtensions).
- Provide a neutral results shape for `relational.sql.query`: `IReadOnlyList<Dictionary<string, object?>>` so the call pattern is uniform across providers.

## Consequences

- Clear separation of concerns: adapters may use Dapper internally; Direct stays lean and portable with ADO.NET.
- Reduced coupling and easier support for non-relational adapters.
- Consistent query results for `relational.sql.query` and consistent JSON mapping via Newtonsoft.

## Alternatives considered

- Keep Dapper in Direct: rejected due to coupling and poorer portability.
- Implement a custom lightweight mapper: unnecessary; ADO.NET + Newtonsoft meets requirements with minimal surface area.

## References

- ADR-0049: Direct Commands API (async-only, mapping, connection resolution)
- ADR-0050: Instruction name constants and scoping
- ADR-0051: Direct routing via instruction executors
- docs/10-execute-instructions.md: instruction names and query behavior

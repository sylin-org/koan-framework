---
id: DATA-0050
slug: DATA-0050-instruction-name-constants-and-scoping
domain: DATA
status: Accepted
date: 2025-08-19
title: Instruction name constants and scoping
---

# ADR 0050: Instruction name constants and scoping

Context

- Instruction names (e.g., "relational.sql.nonquery") were scattered as string literals across adapters and helpers.
- We added new instruction variants (relational.sql.query) and wanted a single source of truth to reduce drift and typos.
- Some instructions are generic (data.ensureCreated, data.clear); others are relational-only (schema.*, sql.*). Scoping should reflect that.

Decision

- Introduce centralized, scoped constants:
  - Koan.Data.Abstractions
    - Instructions/DataInstructions.cs: data.ensureCreated, data.clear
    - Instructions/RelationalInstructions.cs: relational.schema.validate|ensureCreated|clear; relational.sql.scalar|nonquery|query
- Replace magic strings across adapters and Core with these constants.
- Keep names as stable contract strings; constants only centralize definitions, not change semantics.

Consequences

- Fewer typos and easier discovery via IDE navigation.
- Future additions (e.g., relational.sql.bulkCopy) will be added in one place and referenced project-wide.
- Documentation can reference the constants alongside raw names.

Alternatives considered

- Keep literals and rely on tests: rejected; too error-prone and scattered.
- Enum-based names: rejected; string contract is easier for cross-language bindings and dynamic composition.

---
uid: reference.modules.Koan.data.direct
title: Koan.Data.Direct — Technical Reference
description: Direct ADO.NET commands with minimal overhead for relational providers.
since: 0.2.x
packages: [Sylin.Koan.Data.Direct]
source: src/Koan.Data.Direct/
---

## Contract
- Inputs/Outputs: command builders, parameterization, result mapping.
- Error modes: provider exceptions; ensure parameter binding to avoid SQL injection.

## Usage guidance
- Use for low-level operations where ORMs are not desired; keep commands localized.
- Follow ADR DATA-0049 and DATA-0052 for boundaries.

## References
- DATA-0049 Direct commands API: `/docs/decisions/DATA-0049-direct-commands-api.md`
- DATA-0052 Dapper boundary; ADO: `/docs/decisions/DATA-0052-relational-dapper-boundary-and-direct-ado.md`

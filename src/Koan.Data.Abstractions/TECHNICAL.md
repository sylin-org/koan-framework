---
uid: reference.modules.Koan.data.abstractions
title: Koan.Data.Abstractions — Technical Reference
description: Contracts shared by Koan data providers and apps.
since: 0.2.x
packages: [Sylin.Koan.Data.Abstractions]
source: src/Koan.Data.Abstractions/
---

## Contract
- Interfaces and base contracts for entities, paging, streaming, and capabilities.
- Error modes: standard exceptions; provider errors wrapped by adapters.

## Key types
- IEntity<TKey>, capability flags, pager/streaming primitives.

## Usage guidance
- Application models expose first-class statics; adapters implement these contracts.

## References
- DATA-0061 paging/streaming: `/docs/decisions/DATA-0061-data-access-pagination-and-streaming.md`
- ARCH-0040 config/constants: `/docs/decisions/ARCH-0040-config-and-constants-naming.md`

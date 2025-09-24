---
uid: reference.modules.Koan.core
title: Koan.Core - Technical Reference
description: Core utilities, primitives, and conventions used across Koan modules.
since: 0.2.x
packages: [Sylin.Koan.Core]
source: src/Koan.Core/
---

## Contract

- Inputs/Outputs: foundational types, result helpers, guards, and common abstractions.
- Options: follow ADR ARCH-0040 for constants/options.
- Error modes: standard .NET exceptions; avoid magic values.

## Key types

- Core primitives surfaced by other modules (data, web, messaging, ai).

## Usage guidance

- Prefer these utilities over bespoke helpers; keep concerns separated.

## Observability & Security

- Integrates with logging/tracing where applicable; no direct security surface.

## References

- ARCH-0040 config and constants: `/docs/decisions/ARCH-0040-config-and-constants-naming.md`
- Engineering guardrails: `/docs/engineering/index.md`

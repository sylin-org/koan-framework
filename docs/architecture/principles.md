# Architecture Principles (curated)

This page distills high-signal ADRs into actionable principles. Newer ADRs supersede older guidance when conflicts exist.

## Core principles

- Simplicity first: SoC, KISS, YAGNI, DRY. Favor small modules, explicit composition, and clear naming.
- Deterministic configuration: explicit config beats discovery; fail fast on misconfig.
- Controllers over inline endpoints for discoverability and testability.
- Centralize stable names (routes, headers, keys) in Constants classes; use typed Options for tunables.

References: ARCH-0011 (logging + headers layering), ARCH-0040 (config and constants naming).

## Data access contract

- First-class static model methods are the source of truth (All, Query, AllStream, QueryStream, FirstPage, Page).
- All/Query without paging must fully materialize. For large sets, use streaming or explicit paging.
- Paging guardrails enforced via options; streaming guarantees stable Id order.

References: DATA-0061 (semantics), DATA-0044 (paging guardrails), DATA-0029/0031/0032 (filters, ignoreCase, pushdown/ fallbacks).

## Web and payload shaping

- Attribute-routed controllers; transformers shape entity payloads consistently.
- Default secure headers applied by Web; CSP opt-in.

References: WEB-0035 (EntityController transformers), ARCH-0011 (headers layering).

## Messaging

- IBus with first-class IMessageBatch; emulate provider gaps predictably.

References: MESS-0021..0027.

## Observability

- OpenTelemetry integrated; structured logs with event ids.

References: ARCH-0033 (OTel), OPS-0050 (scheduling/bootstrap unification).

## Lifetimes and scope

- Singleton for clients/factories and orchestrators; Transient for stateless helpers; Scoped only when truly needed.

Reference: Engineering Guardrails (service lifetimes section).

## Naming and constants

- Use Sora.Core.Configuration helpers (`Read`, `ReadFirst`) and canonical `:` keys.
- One `Constants` per assembly; rely on namespaces; use using-aliases when needed.

Reference: ARCH-0040.

---

For the full ADR index, see ../decisions/index.md.

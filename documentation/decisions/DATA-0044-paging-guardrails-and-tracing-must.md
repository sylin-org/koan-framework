---
id: DATA-0044
slug: DATA-0044-paging-guardrails-and-tracing-must
domain: DATA
status: Accepted
date: 2025-08-19
---

# 0044 - Paging pushdown and tracing elevated to MUST; paging guardrails

## Context

Earlier guidance (see 0032 and 0033) recommended pushing down paging where possible and integrating with OpenTelemetry when present. In practice, in-memory materialization of large result sets caused performance and memory issues, and missing tracing made it harder to diagnose query behavior across adapters.

## Decision

- Elevate paging pushdown to MUST when the backing database supports native paging primitives (LIMIT/OFFSET, TOP/FETCH, skip/limit, etc.).
- Elevate OpenTelemetry participation to MUST when OTEL is present: adapters MUST create Activity spans around key operations and set standard db.\* attributes.
- Introduce paging guardrails across adapters:
  - DefaultPageSize: applied when the caller does not specify paging.
  - MaxPageSize: an upper bound enforced by adapters when pushdown is available, and by fallback paths otherwise.
- Provide discoverability via each adapter’s Describe; include guardrail values and capability announcements (e.g., EnsureCreatedSupported).

Notes:

- Current repository interfaces don’t pass page/size explicitly. Until interface extensions are introduced, adapters SHOULD avoid breaking behavior by limiting result sets unconditionally. Where feasible, adapters MAY expose guardrails in options and Describe now, and wire pushdown once the controller/path provides page/size to repositories.

## Consequences

- Adapters must add options for paging guardrails and announce them in Describe.
- Tests and PR gates must validate presence of guardrails and tracing spans.
- Future interface work is expected to carry explicit paging information into repositories to enable full server-side pushdown without relying on in-memory pagination.

## References

- 0032 - Paging pushdown and in-memory fallback
- 0033 - OpenTelemetry integration
- 0040 - Config and constants naming

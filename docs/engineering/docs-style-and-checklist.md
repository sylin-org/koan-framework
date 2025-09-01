# Docs style and checklist

Audience: developers and agentic AIs. Keep it instructional and reference-first.

## Style
- Contract first: Inputs/Outputs, options, error modes, success criteria.
- Edge cases: 3–5 realistic pitfalls (null/empty, large/slow, auth, concurrency/timeouts).
- Examples: short, runnable, production-safe. Prefer model statics; avoid generic facades unless necessary.
- Constants over literals: link to well-known headers/routes; centralize names in `Constants`.
- Cross-link ADRs and canonical pages (Engineering front door, Architecture principles, Decisions).

## PR checklist
- No edits in `docs/reference/_generated/**`.
- Examples compile or are trivially correct for the module.
- Links to ADRs and sample code are stable.
- Tutorial language avoided (no “quickstart”/course flows) per ARCH-0041.

## References
- decisions/ARCH-0041-docs-posture-instructions-over-tutorials.md
- docs/api/web-http-api.md (well-known headers/routes)
- docs/reference/adapter-matrix.md (adapters source and generated matrix)
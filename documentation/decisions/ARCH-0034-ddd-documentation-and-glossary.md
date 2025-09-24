---
id: ARCH-0034
slug: ARCH-0034-ddd-documentation-and-glossary
domain: ARCH
status: Accepted
date: 2025-08-17
---

# 0034 - DDD documentation section and plain-language glossaries

## Context

Developers using Koan asked for a practical DDD perspective with a beginner-friendly on-ramp. While existing docs cover core contracts, CQRS, and adapters, they didn’t frame Koan explicitly in DDD terms or define a consistent ubiquitous language. Newcomers also benefit from brief, plain-language definitions near where terms are introduced.

## Decision

- Create a dedicated `docs/ddd` section with:
  - 00-index.md (landing)
  - 01-ubiquitous-language.md (dictionary)
  - 02-bounded-contexts-and-modules.md
  - 03-tactical-design.md
  - 04-cqrs-and-eventing-in-Koan.md
  - 05-sample-walkthrough.md
  - 06-testing.md
  - 07-anti-corruption-layer.md
  - 08-cross-cutting-and-observability.md
- Add a concise “Terms in plain language” section to each page for first-time DDD readers with a technical background.
- Link the section from `docs/00-index.md` for discoverability.

## Consequences

- DDD alignment is explicit and approachable; teams can adopt Koan with a clearer model-first mindset.
- Glossaries reduce ramp-up time and help keep code and conversations consistent.
- Documentation maintenance: future features should reference and, when necessary, extend the DDD section.

## References

// tutorial removed

- 03-core-contracts.md
- 04-adapter-authoring-guide.md
- 0032-paging-pushdown-and-in-memory-fallback.md
- 0033-opentelemetry-integration.md

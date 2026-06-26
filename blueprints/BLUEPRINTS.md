# Adapter Blueprints (ARCH-0094)

**Blueprints are to EXTENDING a pillar what `.claude/skills` cards are to USING one.** Each is a per-adapter-TYPE,
agent-executable authoring procedure: it scripts the hygiene an agent skips (discover → research → reuse → implement →
gotchas → test) and states the **obligations** a conformant adapter must satisfy — grounded in Koan's own shipped,
Conformance-Gate-passing adapters so it cannot drift into fiction. The proof of a finished adapter is the **Conformance
Gate** going green against a real instance, not a code review.

This catalogue is the discovery anchor: an agent matches intent → the right blueprint by the `description` triggers
below. It is also the parity list `scripts/blueprint-lint.ps1` checks (every on-disk blueprint must be listed here).

## How a blueprint is structured

- `blueprints/<pillar>/<type>/BLUEPRINT.md` — directory leaf == frontmatter `name:` (the lint/loader key).
- Frontmatter: `name` · `description` (intent triggers) · `pillar` · `type` · `family-base` · `conformance` (the kit it must pass) · `blast` (type default ceiling; the concrete adapter's tier = the data classification it carries) · `status` · `last_validated` · `grounded-in` (the shipped exemplar source files the obligations trace to).
- Body sections: Trigger · Discover · Research · Resources · Implement · Gotchas · Test · See also.
- Obligations carry an `<!-- obligation: Type.Member @ file -->` token; the grounding-lint grep-verifies each cited member is alive in that shipped source.

## Catalogue

| Blueprint | Pillar | Type | Family base | Blast (default) | Status |
|-----------|--------|------|-------------|-----------------|--------|
| [data-sql](data/sql/BLUEPRINT.md) | data | relational / SQL | `Koan.Data.Relational` | high | current |

_More blueprints (data/kv, data/document, vector, storage, messaging, OAuth, AI, cache) are authored one per type as the
Forge scales (ARCH-0094 Phase 7), each grounded in the shipped fleet for that type._

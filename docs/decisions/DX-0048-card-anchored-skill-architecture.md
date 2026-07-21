---
id: DX-0048
title: Card-anchored skill architecture + validation gate
status: Accepted
date: 2026-06-18
area: DX / AX
supersedes: []
related: [DX-0041, H4 pillar cards, H2 doc-code gate]
---

# DX-0048 — Card-anchored skill architecture + validation gate

## Context

The `.claude/skills/` set is the framework's just-in-time guidance surface for both human
developers (DX) and AI agents (AX). A full source-accuracy audit (2026-06-18, 13 skills,
every identifier grep-verified against current `src`) graded **all 13 `refresh`**: good
structure and the right topics, but the *first copy-paste example* in most skills no longer
compiles against current source. The drift was a single event surfaced many times — the same
ghosts recur across skills (`QueryCaps`/`QueryCapabilities.HasFlag` → `CapabilitySet.Has(DataCaps…)`,
`DataQueryOptions` → `QueryDefinition`, `[VectorField]` → `[Embedding]`, manual FK navigation →
`[Parent]`/`Relatives()`, `.SaveAsync()` → `Save()`, `List<T>` → `IReadOnlyList<T>`).

Two root causes:

1. **No anchor.** Each skill re-stated its pillar's full API inline, so every downstream rename
   silently rotted it. There was no single source of truth a skill could lean on.
2. **No gate.** Nothing compiled skill examples, so drift was invisible. The rot had even
   propagated *from* the feeding guides (the skills faithfully copied ghosts the guides taught).

Meanwhile the H4 pillar cards (`docs/reference/cards/*.md`) had just been authored as
source-verified, one-screen API truth per pillar — exactly the anchor the skills lacked.

## Decision

### 1. Layered information architecture — one fact, one home

| Layer | Location | Owns |
|---|---|---|
| **Card** | `docs/reference/cards/<pillar>.md` | the one-screen **API truth** (verified, validated) |
| **Skill** | `.claude/skills/koan-<x>/SKILL.md` | **activation**: trigger · canonical pattern · anti-patterns · see-also |
| **Guide** | `docs/guides/<x>-howto.md` | the deep **narrative how-to** |
| **Sample** | `samples/Sx.*` | working **proof** |
| **ADR** | `docs/decisions/` | the **decision/rationale** |

A skill **does not** exhaustively re-document the API — that is the card. It teaches the
*pattern* + the *anti-patterns* and routes to the card/guide/sample. This removes the
duplicated API surface that was the drift vector.

### 2. The skill contract

Every skill conforms (exemplars in-tree: `koan-caching`, `koan-jobs`):

- **Frontmatter**: `name` (== directory name, kebab-case), `description` (trigger-rich — the AX
  activation surface), and optional `pillar` / `card` / `status` / `last_validated`.
- **Sections, in order**: `## Trigger this skill when you see` → `## Core principle` (immediately
  followed by **the one canonical pattern**, a single `<!-- validate -->`-marked, self-contained,
  compile-clean block that mirrors the card) → activation/effect table → `## Anti-patterns to flag`
  (the AX guards — what NOT to generate) → `## Escape hatches` → `## See also` (card anchor +
  guide + sample + ADR, every link resolving).
- **Rules**: directory name == `name`; Newtonsoft canonical (no STJ); canonical verbs
  Save/Remove/Query; no version pins; no removed/migrated surfaces presented as live; the
  canonical pattern must be **self-contained** (only framework + BCL types, no fictional helpers)
  so the gate can compile it.

### 3. The validation gate (the permanent honesty mechanism)

- **Compile gate**: `scripts/validate-code-examples.ps1` (the H2 opt-in `<!-- validate -->`
  compiler) is extended to scan `.claude/skills/**` and to reference the full pillar set. Each
  skill's canonical pattern is marked and compiled; drift → red build.
- **`scripts/skills-lint.ps1`** (new): asserts directory == `name`; frontmatter contract present;
  the `card:` anchor (when declared) resolves; every Markdown link in the skill resolves; no
  `0.6.x`/hardcoded version pins; catalog parity (README + CLAUDE.md table list exactly the
  on-disk set).
- Both are wired into `green-ratchet.ps1` and the `pr-gate.yml` (B2) so a drifted or
  contract-violating skill cannot merge.

### 4. Roster realignment

Skills realign 1:1 to the 8 pillar cards (+ 5 new pillar cards for storage/messaging/media/
orchestration/observability) plus a small set of cross-cutting foundation skills
(quickstart/entity-first/bootstrap/debugging). The data pillar's over-fragmentation
(5 skills → 1 card) is consolidated to lean facets; the `auth` card gains its missing skill.
The full keep/overhaul/new/retire roster and phasing live in the H10 program card
(`docs/assessment/prompts/06/H10-skills-overhaul.md`).

## Consequences

- **Positive**: skills stop carrying (and rotting) duplicated API; a single verified anchor per
  pillar; a CI gate makes drift a build break, not a silent liability; `description`-driven AX
  activation and anti-pattern guards become first-class; the dir==name fix removes the
  invocability ambiguity (12 of 14 skills previously had dir ≠ `name`).
- **Cost**: the canonical-pattern self-containment rule constrains how examples are written; the
  validate gate's temp project references the full pillar set (one-time build cost per gate run);
  fixing the feeding guides is in-scope (the rot's root is upstream).
- **Boundary**: `koan-mcp` is deferred while a concurrent Mcp effort owns `src/Koan.Mcp`; the AI
  skill covers the full current surface by decision, accepting churn when the ARCH-0089 AI-pillar
  dissolution lands.

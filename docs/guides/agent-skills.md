---
type: GUIDE
domain: framework
title: "Koan agent skills"
audience: [developers, ai-agents]
status: current
last_updated: 2026-08-15
validation:
  date_last_tested: 2026-08-15
  status: reviewed
  scope: official three-skill chooser and trust boundaries
---

# Choose one Koan skill

Koan has three coding-agent entry points. Choose by the outcome, not by a package or subsystem.

| Skill | Say this when you want to… | Promise |
|---|---|---|
| `$koan` | “Build this.” “Add Mongo.” “Enable authentication.” “Use AI.” “Fix it.” “Get it ready to ship.” | Compose the smallest useful Koan stack, change only what the outcome earns, and prove the result. |
| `$koan-explain` | “What does this app do?” “Why did this provider win?” “Why is readiness red?” | Explain observed behavior and honest unknowns without changing anything. |
| `$koan-upgrade` | “Bring this older Koan app forward.” | Preserve application contracts while replacing obsolete Koan expressions with current ones. |

An explicit invocation wins. Otherwise, choose `$koan-explain` for read-only work,
`$koan-upgrade` for a framework migration, and `$koan` for implementation.

A database or vector-provider switch is application evolution, so it stays with `$koan`. It becomes
an upgrade only when the Koan framework contract itself changes.

## What makes `$koan` delightful

Start with the developer's sentence. Show the proposed stack as three short parts:

- **Now** — the few pieces required for this outcome;
- **Later** — capabilities that remain easy to snap on;
- **Preserved** — routes, data, security, and topology that will not change.

Then build one working vertical slice. Prefer Koan's recognizable pieces: one `AddKoan()` composition
point, an `Entity<T>` domain model, expressive Entity operations, and `EntityController<T>` when an
HTTP surface is wanted. References declare intent; Koan composes the pieces.

The same approach scales from a tiny local application to MongoDB or PostgreSQL, authentication and
tenancy, jobs and communication, storage and media, AI and vector search, Canon, or MCP. The skill
loads only the relevant recipe and still considers the whole application so a new piece does not
create a second architecture.

Its capability chooser links each pillar and provider directly to the canonical online Koan recipe,
so exact details stay one click away without crowding the conversation.

## Trust boundaries

Skill selection never grants extra permission. Destructive data changes, credentials, publication,
external messages, and production operations retain their normal authorization requirements.

`$koan-explain` is strictly read-only. If a request begins with explanation and later asks for a fix,
the agent finishes the explanation, announces the transition to `$koan`, and only then makes changes.

`$koan-upgrade` may inspect old code to learn what the application must preserve. Old instructions do
not define the replacement; current Koan evidence does. An unknown replacement is reported rather
than guessed.

The canonical sources live under `.agents/skills/`. Compatibility and retirement records stay
outside the loaded skill experience.

For the durable design decision, see
[DX-0049](../decisions/DX-0049-three-skill-public-agent-surface.md).

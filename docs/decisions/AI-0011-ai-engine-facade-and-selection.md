# AI-0011 — AI Engine facade and selection API

Status: Accepted
Date: 2025-08-21
Owners: Sora AI

## Context

`Sora.AI.Ai.TryResolve()` improved ergonomics but reads redundant and lacks strong naming. We want a semantic, terse entrypoint to AI features that can also target specific providers/models inline, mirroring the vector facade pattern.

## Decision

Introduce `Sora.AI.Engine` as the preferred facade for AI operations:

- Default access:
  - `Engine.IsAvailable`, `Engine.Try()` for discovery
  - `Engine.Prompt(...)`, `Engine.Stream(...)`, `Engine.Embed(...)`
- Targeted access:
  - `Engine(provider)....` or `Engine(provider, model)....` returning a selector that forces routing
- Backward compatibility: keep `Sora.AI.Ai` and delegate under the hood.

Resolution precedence mirrors vector:

- Explicit selector overrides
- App defaults
- Router/provider priority
- Fail fast with a clear message

## Consequences

- Code reads semantically ("Engine") and avoids `Ai.Ai` repetition.
- Adoption can be incremental; existing code continues to work.
- Guides updated to show Engine-first usage.

## Alternatives considered

- Rename `Ai` to `Engine` directly: rejected to avoid breaking change.
- Only add aliases (methods) on `Ai`: partially improves names but misses provider/model selector ergonomics.

## Status & Migration

- Status: Accepted; to be implemented as a thin alias over `Sora.AI.Ai`.
- Migration: Prefer `Sora.AI.Engine` in new code; gradually replace `Ai.TryResolve()` sites with `Engine.Try()` or direct calls.

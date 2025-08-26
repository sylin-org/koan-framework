# Architecture Overview

Sora is modular and provider-agnostic. Core principles:

- POCO entities: `IEntity<TKey>` with simple properties.
- Adapters implement data behavior; core defines contracts and small shims.
- Decisions are tracked as ADRs in `docs/decisions`.

## Layers
- Sora.Data.Abstractions — attributes, capabilities, instruction envelope.
- Sora.Data.Core — DataService, repository façade, static facades (Data<T,..>).
- Provider packages (e.g., Sora.Data.Sqlite) — concrete repositories/dialects.
- Optional toolkits (e.g., Relational) — shared model/diff, not tied to core.

## Key design choices
- Indexes anchored to properties; Identifier implies PK/unique.
- Complex types -> JSON in relational providers; hydrate on read.
- Instruction API: namespaced operations with typed results.

## Extensibility
- New providers opt-in to capabilities (e.g., IInstructionExecutor<TEntity>).
- Keep dialects in provider projects (SoC).

## Web package layout
Sora.Web is organized for clarity:
- Controllers — MVC endpoints like Health and Capabilities; entity base controller.
- Hosting — Startup filter wires static files, secure headers, health, and MVC by default (no inline `MapGet/MapPost`).
- Infrastructure — small helpers used by controllers/hosting only.

This keeps endpoints out of startup code and avoids "god classes" while making discovery predictable.

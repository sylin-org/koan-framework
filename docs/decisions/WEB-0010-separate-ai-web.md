# ADR WEB-0010: Separate AI Web Endpoints from Sora.Web

Date: 2025-08-20

## Status
Accepted

## Context
`Sora.Web` contained `AiController`, which depended on `Sora.AI` and `Sora.AI.Contracts`. This forced consumers of the base web package to transitively reference AI packages even when they did not need AI features. This violates Sora's modularity promise ("everything is optional") and the guidelines to keep packages focused and decoupled.

## Decision
- Move `AiController` out of `Sora.Web` into a new package: `Sora.AI.Web`.
- `Sora.Web` removes project references to AI packages and becomes AI-agnostic again.
- `Sora.AI.Web` references `Sora.AI` and `Sora.AI.Contracts` and hosts AI endpoints (health, adapters, models, capabilities, chat, streaming chat, embeddings).

## Consequences
- Breaking change: apps that previously got AI endpoints from `Sora.Web` must add a reference to `Sora.AI.Web`.
- Clearer composition: AI becomes an opt-in capability mirroring other optional web modules (e.g., `Sora.Web.Swagger`, `Sora.Web.GraphQl`).
- Reduced coupling: consumers of `Sora.Web` no longer pull transitively from AI packages.

## Migration
Add a package/project reference to `Sora.AI.Web` where AI endpoints are desired. No code changes required if consumers were using the conventional routes from `Sora.AI.Contracts.Routing`.

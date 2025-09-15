# ADR WEB-0010: Separate AI Web Endpoints from Koan.Web

Date: 2025-08-20

## Status

Accepted

## Context

`Koan.Web` contained `AiController`, which depended on `Koan.AI` and `Koan.AI.Contracts`. This forced consumers of the base web package to transitively reference AI packages even when they did not need AI features. This violates Koan's modularity promise ("everything is optional") and the guidelines to keep packages focused and decoupled.

## Decision

- Move `AiController` out of `Koan.Web` into a new package: `Koan.AI.Web`.
- `Koan.Web` removes project references to AI packages and becomes AI-agnostic again.
- `Koan.AI.Web` references `Koan.AI` and `Koan.AI.Contracts` and hosts AI endpoints (health, adapters, models, capabilities, chat, streaming chat, embeddings).

## Consequences

- Breaking change: apps that previously got AI endpoints from `Koan.Web` must add a reference to `Koan.AI.Web`.
- Clearer composition: AI becomes an opt-in capability mirroring other optional web modules (e.g., `Koan.Web.Swagger`, `Koan.Web.GraphQl`).
- Reduced coupling: consumers of `Koan.Web` no longer pull transitively from AI packages.

## Migration

Add a package/project reference to `Koan.AI.Web` where AI endpoints are desired. No code changes required if consumers were using the conventional routes from `Koan.AI.Contracts.Routing`.

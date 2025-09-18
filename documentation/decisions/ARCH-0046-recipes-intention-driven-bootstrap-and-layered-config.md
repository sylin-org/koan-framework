---
id: ARCH-0046
slug: ARCH-0046-recipes-intention-driven-bootstrap-and-layered-config
domain: ARCH
status: Accepted
date: 2025-08-26
---

# ARCH-0046: Integration Recipes — intention-driven bootstrap and layered configuration

Date: 2025-08-26

Status: Accepted

## Context

- Koan is intention-driven: “reference = intent.” Referencing a provider package lights up sane defaults with options-driven overrides.
- Teams repeatedly wire cross-cutting operational concerns (health checks, OTEL, retries/circuit breakers, workers) alongside providers (DB, messaging, AI, vector, cache).
- We want a composable, code-first way to apply opinionated, best-practice wiring with strong DX, while remaining AOT/trimming friendly and predictable.

## Decision

Introduce Integration Recipes: small bootstrap bundles that apply best-practice operational wiring on top of already-referenced modules. Recipes follow these principles and guardrails:

1) DX-first activation
- Reference = intent: referencing a `Koan.Recipe.*` package declares intent to apply its recipe(s).
- Also support explicit activation for control/AOT: `services.AddRecipe<T>()` and `services.AddRecipe("name")`.
- Config-only path: `Koan:Recipes:Active` (comma-separated) to select a subset.

2) Layered configuration (predictable precedence)
- Provider defaults < Recipe defaults < AppSettings/Env < Code overrides < Recipe forced overrides.
- “Forced overrides” are disabled by default; enabling requires an explicit per-recipe flag and is further gated by a global allow switch.

3) Options-first, no magic values
- All tunables are options-bound; no scattered literals. Use constants/options per ARCH-0040.

4) Capability gating
- A recipe should apply only when required capabilities exist (prefer checking registered services/markers or “IsConfigured” on options, not raw config sniffing).

5) Logging and debuggability
- Startup logs must announce discovery, apply/skip reasons, capability misses, conflicts, and any overrides (old → new, redacted). Provide an optional dry-run mode.

6) Project and web guardrails
- Recipes are infra-only wiring; they must not declare inline endpoints. Controllers remain the only HTTP route surface (WEB-0035).

7) Packaging and naming
- Namespace/package root: `Koan.Recipe` / `Koan.Recipe.*`. Third parties may use `<Org>.Koan.Recipe.*`.
- Prefer one public recipe per package to preserve “reference = intent.”

8) Discovery strategy with AOT safety
- Prefer explicit registration (`AddRecipe<T>()`) and/or assembly-level registration attributes. Avoid broad AppDomain scans to remain trimming-friendly.

## Scope

- In scope: recipe contract, activation modes, configuration precedence, logging expectations, capability gating, packaging, and documentation.
- Out of scope: a new orchestrator or external manifest DSL. Recipes are runtime bootstrap helpers; optional DevHost/export can read applied recipes but is not required.

## Consequences

Positive
- Consistent, low-ceremony operational wiring across services.
- Observability and reliability become defaults, improving prod posture.
- Encodes organizational policy as small, testable packages.

Negative/Risks
- Hidden magic if logging is insufficient. Mitigation: verbose startup logs and optional dry-run.
- Conflicts across multiple recipes. Mitigation: ordering + warnings on override.
- AOT/trimming pitfalls with reflection. Mitigation: explicit registration or attributes; avoid broad scanning.

## Implementation notes

- Provide abstractions in a small `Koan.Recipe.Abstractions` package:
  - `public interface IKoanRecipe { string Name { get; } int Order => 0; bool ShouldApply(IConfiguration cfg, IHostEnvironment env) => true; void Apply(IServiceCollection services, IConfiguration cfg, IHostEnvironment env); }`
  - Registration helpers: `AddRecipe<T>()`, `AddRecipe(string name)`.
  - Optional assembly-level attribute for self-registration.
- Options layering helpers (in Koan.Core) to encode precedence without developers memorizing Configure/PostConfigure nuances.
- Logging categories and stable event IDs for discover/apply/skip/conflict/override; redact secrets.
- Logging reference (EventIds)
  - Applying (41000): starting recipe application
  - AppliedOk (41001): recipe applied successfully
  - SkippedNotActive (41002): recipe not in `Koan:Recipes:Active`
  - SkippedShouldApplyFalse (41003): `ShouldApply` returned false
  - DryRun (41004): dry-run enabled — no mutations
  - ApplyFailed (41005): exception during `Apply` (continue)
- Config keys (canonical):
  - `Koan:Recipes:Active` — list of active recipe names.
  - `Koan:Recipes:AllowOverrides` — global gate for forced overrides.
  - Per-recipe flags — e.g., `Koan:Recipes:<RecipeName>:ForceOverrides`.

## Follow-ups

1) Add `Koan.Recipe.Abstractions` with the minimal contract and registration helpers.
2) Add a sample recipe package and tests (health checks + OTEL + Polly policies).
3) Document options layering with examples and dry-run troubleshooting.
4) Optional: DevHost/export reads active recipes to emit matching local/CI manifests.

## References

- ARCH-0040 — Config and constants naming
- ARCH-0041 — Docs posture: instructions over tutorials
- ARCH-0043 — Lightweight parity roadmap — Observability/Resilience/Recipes
- ARCH-0045 — Foundational SoC namespaces and abstractions
- WEB-0035 — EntityController transformers (controller-only HTTP routes)

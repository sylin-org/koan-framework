# Architecture Principles (curated)

This page distills high-signal ADRs into actionable principles. Newer ADRs supersede older guidance when conflicts exist.

## Core principles

- Simplicity first: SoC, KISS, YAGNI, DRY. Favor small modules, explicit composition, and clear naming.
- Deterministic configuration: explicit config beats discovery; fail fast on misconfig.
- Controllers over inline endpoints for discoverability and testability.
- Centralize stable names (routes, headers, keys) in Constants classes; use typed Options for tunables.

References: ARCH-0011 (logging + headers layering), ARCH-0040 (config and constants naming).

## Data access contract

- First-class static model methods are the source of truth (All, Query, AllStream, QueryStream, FirstPage, Page).
- All/Query without paging must fully materialize. For large sets, use streaming or explicit paging.
- Paging guardrails enforced via options; streaming guarantees stable Id order.

References: DATA-0061 (semantics), DATA-0044 (paging guardrails), DATA-0029/0031/0032 (filters, ignoreCase, pushdown/ fallbacks).

## Web and payload shaping

- Attribute-routed controllers; transformers shape entity payloads consistently.
- Default secure headers applied by Web; CSP opt-in.

References: WEB-0035 (EntityController transformers), ARCH-0011 (headers layering).

## Orchestration (DevHost)

- Provider adapters drive hosting (Docker → Podman precedence on Windows); selection can be forced per run.
- Exporters generate deterministic artifacts (Compose v2 today; Helm/ACA planned) with safe env substitution and profile-aware persistence mounts.
- Profiles: local/ci can run; staging/prod are export-only by default. Non-prod auto-avoids port conflicts; prod fails fast on conflicts.
- Readiness: wait for running/healthy containers with timeouts; surface endpoint hints consistently.
- Discovery: prefer explicit descriptor (Koan.orchestration.{yml|json}) and minimal config over broad scans; keep trimming/AOT-friendly.

References: ARCH-0047 (hosting providers/exporters), ARCH-0048 (endpoint resolution and mounts), Reference → Orchestration (`../reference/orchestration.md`).

## Messaging

- IBus with first-class IMessageBatch; emulate provider gaps predictably.

References: MESS-0021..0027.

## Observability

- OpenTelemetry integrated; structured logs with event ids.

References: ARCH-0033 (OTel), OPS-0050 (scheduling/bootstrap unification).

## Integration recipes

- Intention-driven bootstrap bundles that apply best-practice wiring (health checks, telemetry, resilience, workers) on top of referenced modules.
- Activation: reference = intent (Koan.Recipe.*), plus explicit `AddRecipe<T>()` and config-only selection via `Koan:Recipes:Active` for precise control/AOT.
- Deterministic layering: Provider defaults < Recipe defaults < AppSettings/Env < Code overrides < Recipe forced overrides. Forced overrides are off by default and gated by `Koan:Recipes:AllowOverrides` and per-recipe `Koan:Recipes:<Name>:ForceOverrides`.
- Capability gating: apply only when prereqs exist; prefer checking registered services or configured options over raw key sniffing.
- Logging + dry-run: stable event ids for apply/skip/fail; `Koan:Recipes:DryRun=true` logs decisions without mutating DI.
- Guardrails: infra-only wiring (no inline endpoints); respect controller-only HTTP routes; no magic values—use options/constants.
- Discovery: explicit registration and/or assembly attributes; avoid broad scans to remain trimming/AOT friendly.

References: ARCH-0046 (recipes), ARCH-0040 (config/constants), WEB-0035 (controller-only routes), Reference: Recipes (`../reference/recipes.md`).

## Lifetimes and scope

- Singleton for clients/factories and orchestrators; Transient for stateless helpers; Scoped only when truly needed.

Reference: Engineering Guardrails (service lifetimes section).

## Naming and constants

- Use Koan.Core.Configuration helpers (`Read`, `ReadFirst`) and canonical `:` keys.
- One `Constants` per assembly; rely on namespaces; use using-aliases when needed.

Reference: ARCH-0040.

---

For the full ADR index, see ../decisions/index.md.

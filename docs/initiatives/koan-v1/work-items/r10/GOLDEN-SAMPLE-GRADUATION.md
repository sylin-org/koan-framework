---
type: GUIDE
domain: framework
title: "Golden Sample Graduation Standard"
audience: [maintainers, developers, ai-agents]
status: current
last_updated: 2026-07-17
framework_version: v0.20.0
validation:
  date_last_tested: 2026-07-17
  status: tested
  scope: derived from the completed GardenCoop source, HTTP, facts, lifecycle, dashboard, and NativeAOT slice
---

# Golden sample graduation standard

Use this card for every active sample. Preserve the business story; do not mechanically copy
GardenCoop's architecture into a sample with different needs.

Graduation is the only terminal state for code presented as active V1 curriculum. `Assess` and `incubate`
exist only while R10 is in progress. Historical material must be visibly archived outside the active
portfolio or removed; it cannot remain discoverable as an alternative application pattern.

## Discovery record

1. **Business sentence** — one sentence naming the input, decision/work, and meaningful result.
2. **Smallest host** — the exact supported `Program.cs`; parameterless `AddKoan()` is the default.
3. **Concept budget** — only the Koan and .NET concepts this sample intentionally teaches.
4. **Intent inventory** — direct references, configuration, infrastructure, context, and deployment prerequisites.
5. **Owner map** — application, framework pillar, adapter, and tooling responsibilities; one owner per decision.
6. **Claim inventory** — README, catalog, startup, facts, UI/API, agent, and deployment promises.
7. **Baseline** — strict build plus the documented fresh-checkout path before edits.

## Required result

- Application code reads as business state, rules, workflows, and boundaries.
- The host contains no manual framework binding, duplicate provider choice, registrar, or sample-only bootstrap.
- Entity statics and `EntityController<T>` remain the first choice; added abstractions must earn a distinct
  business responsibility.
- One application `KoanModule` is used only when the application genuinely owns composition, startup work,
  reporting, or shutdown behavior.
- One documented command reaches the defining business result. A helper exists only when it performs real
  orchestration.
- Startup and runtime facts explain the same composition that executed.
- Unsupported scenarios and special deployment shapes are qualified explicitly.

## Economical executable evidence

Prefer one cumulative business story over many implementation-shaped tests:

| Boundary | Minimum proof |
|---|---|
| Source | sample and directly repaired owners compile warning-free in the focused strict lane |
| Business | fresh state reaches the sample's defining result and one meaningful correction/recovery when applicable |
| Projection | claimed HTTP, MCP, UI, job, or operator surface observes that same result |
| Composition | application module, provider/capability decisions, and facts are present without collection failure |
| Lifecycle | real host start and stop; no manual global-host assignment or leaked process |
| Deployment | every claimed package/container/NativeAOT/external-provider shape executes, or the claim is removed/qualified |

The proof stays sample-local and business-named. Shared framework test infrastructure is welcome when it preserves
meaning; a universal sample harness that hides prerequisites or reduces every app to CRUD is not.

## Graduation record

Before marking a sample `graduated`, record:

- exact commands and focused counts;
- meaningful runtime observations;
- framework defects repaired at their owners;
- claims corrected or removed;
- remaining warnings, external requirements, and unsupported scenarios;
- `Koan.sln`, test/CI selection, README, and canonical sample-index alignment;
- docs, changed-example, diff, and privacy checks.

Otherwise assign one visible disposition: `incubate`, `archive`, or `delete`. An unassessed directory must not be
presented as maintained curriculum.

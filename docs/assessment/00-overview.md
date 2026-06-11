# Koan Framework Assessment — Executive Overview

**Date**: 2026-06-10 · **Scope**: full repo (113 projects / ~150k LOC / 280 ADRs / 10 months /
1,519 commits) · **Method**: 4 staged audit waves, ~35 parallel code auditors + adversarial
review of the conclusions; evidence preserved under [evidence/](evidence/).

## The one-paragraph verdict

**The feasibility test succeeded; the product around it has not been built yet.** The core bet —
entity-first ergonomics + Reference=Intent auto-composition + capability-graded multi-provider
transparency — is implemented, dogfooded across five non-trivial apps, and in its renovated parts
(Cache, Jobs, the Data inner ring) reaches genuinely settled, reference-quality architecture. But
the renovation has reached only ~⅓ of the surface; ~25 projects fail the repo's own "≥2 usages"
dogfood bar; the enforcement substrate is voluntary (CI disabled, 45% of test projects invisible
to the solution gate); and the public face actively misleads (25 of 59 front-door claims are
false, the documented install path cannot succeed, and a headline pillar — Flow — no longer
exists). **Placement: an L2 system with L3 islands and an L1 public face** (ladder defined in
[03-maturity-model.md](03-maturity-model.md)). The path to "less but more meaningful parts"
requires no new invention — the L3 islands already define the pattern; it requires *finishing*:
executing authorized cuts, completing in-flight migrations, and making the existing gates
mandatory.

## Ten findings that frame everything

1. **The real framework is ~25 projects (~45% of LOC); the rest is experiment, legacy, debris,
   or bundled product.** The largest single codebase in the repo is an application
   (Koan.Service.KoanContext) that is not even in the solution.
2. **Where the consolidation-era treatment was applied, it worked** — Cache (78 public types for
   a full L1/L2+coherence pillar) and Jobs (49 types, 5-tier test ladder) deliver flagship
   capability at ~⅓ the type cost of the legacy strata. The discipline is proven; it just hasn't
   been applied everywhere.
3. **Generational strata coexist everywhere**: 3 module primitives (90:7 registrar:module ratio),
   2 discovery pipelines, 2 auth flow engines (the live one returns 501 for OIDC while OIDC
   connectors ship), 2 schema generations, 2 outboxes, a superseded scheduler. 18 duplicate-
   concept clusters total; 13 are drift or unfinished migration.
4. **The front door fails verification.** Install commands reference package IDs that don't
   exist publicly (`Koan.*` vs actual `Sylin.Koan.*`, stale at 0.8.x vs repo 0.17); quickstart
   repos 404; principles.md — stamped "verified" — contains 8 core API snippets that don't
   compile; the samples catalog advertises four phantom samples as pillar exemplars.
5. **Fail-fast is canon at query time and absent at boot time.** `AddKoan()` swallows every
   module-registration failure unlogged (243 empty catch blocks repo-wide, concentrated on the
   boot path) — while the framework's own startup-service orchestrator already defaults to
   fail-fast. It's an internal inconsistency, not a philosophy gap.
6. **The test platform is top-decile; its execution is voluntary.** Differential oracles
   (FilterConvergence), per-adapter integration matrices, and KoanIntegrationHost canon exist —
   but CI is disabled, releases gate on build only, and 39/87 test projects sit outside the
   solution the ratchet runs.
7. **The concept budget is spent upside-down**: 2,351 public types, of which users live in ~27%;
   9 of 17 root static facades belong to packages with 0–1 consumers; the one mandatory package
   (Koan.Core, 224 types + unconditional OpenTelemetry) is the fattest.
8. **Docs quality is inverted relative to docs hygiene**: recent ADRs are exceptional working
   memory (empirical probes, staged ledgers, self-corrections), while status metadata decays
   (superseded ADRs still read "Accepted") and the marketing layer ossified at v0.6.3.
9. **Boundaries leak in both directions**: the kernel references the condemned orchestration
   stack; a satellite product's contracts (ZenGarden.Core) are load-bearing inside mainline
   connectors; Koan.Web hard-references Koan.Scheduling for one endpoint nobody calls.
10. **The framework undersells its actual differentiators** — the capability model, source-
    generated AOT-friendly discovery, the Jobs ladder, cache coherence, and the *honest*
    capability-graded multi-provider story — while overselling ghosts (Flow, semantic-pipeline
    `.Embed()`, enterprise governance theater).

## The documents

| Doc | Contents |
|---|---|
| [README.md](README.md) | Method, baseline metrics, how to re-run |
| [01-cartography.md](01-cartography.md) | What exists: pillar-by-pillar verdicts, systemic patterns, debris ledger |
| [02-philosophy-dx.md](02-philosophy-dx.md) | Canon vs reality: promise audit (22/12/25), newcomer walk, ergonomics scores, surface census, 18 duplicate-concept clusters |
| [03-maturity-model.md](03-maturity-model.md) | Explicit 5-level ladder calibrated to the repo's own canon; every pillar placed |
| [04-recommendations.md](04-recommendations.md) | 8 tracks (A truth, B enforcement, C cut waves, D orchestration migration, E finish migrations, F fail-loud boot, G kernel diet, H docs system), longitudinal metrics, guardrails |
| [05-strategic-position.md](05-strategic-position.md) | Mission reframe ("Rails for agentic, data-driven .NET apps"), the agent-native thesis, strategic shed list, recorded decisions (Newtonsoft canon), lesser-model session playbook, premium-DX program (docs/narrative/presentation redesign) |
| [06-prompt-stash.md](06-prompt-stash.md) | Tier-routed implementation prompts (T1/T2 for lesser models, T3 frontier-only): shared preamble, cut templates, per-track recipes, coverage map |
| [evidence/](evidence/) | Raw structured findings from all ~35 auditors + adversarial verdicts |

## How to consume this (suggested order for the maintainer)

1. **Tracks A + B of [04](04-recommendations.md)** — days of work, restores public truth and the
   enforcement floor; everything else compounds on them.
2. **Wave 0 + Wave 1 cuts** — the verified delete-only set; shrinks the tree before Facet 3
   (ambient context) touches it.
3. Re-score [03](03-maturity-model.md) per pillar as facets complete; track the §9 metrics table
   in [04](04-recommendations.md) — especially 90:7, 18 clusters, 48/87, and 25-FALSE — as the
   longitudinal consolidation dashboard.

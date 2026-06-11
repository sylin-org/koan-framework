# Koan Framework Assessment (2026-06)

A staged, evidence-based assessment of the framework's pillars, philosophy, developer
experience, and maturity — compiled to anchor the consolidation effort ("fewer but more
meaningful parts") and to serve as a reference baseline for future re-assessment.

## Method

The assessment was produced in stages, each building on the previous. **Start with
[00-overview.md](00-overview.md)** (executive summary + verdict).

| Stage | Document | Question answered |
|-------|----------|-------------------|
| — | [00-overview.md](00-overview.md) | Executive summary: verdict, ten framing findings, reading order. |
| 0 | (this file, §Baseline) | What is physically here? Hard metrics. |
| 1 | [01-cartography.md](01-cartography.md) | What are the parts, what do they do, which are alive? |
| 2 | [02-philosophy-dx.md](02-philosophy-dx.md) | What is the promised philosophy, and does the DX deliver it? |
| 3 | [03-maturity-model.md](03-maturity-model.md) | Where is each pillar on a maturity ladder? |
| 4 | [04-recommendations.md](04-recommendations.md) | What should consolidation do, in what order? |
| 5 | [05-strategic-position.md](05-strategic-position.md) | What is Koan uniquely positioned to be, what does it shed, and how do agentic sessions (incl. lesser models) work on it? |
| 6 | [06-prompt-stash.md](06-prompt-stash.md) | Ready-to-paste, tier-routed implementation prompts for every flagged issue and strategic capability. |
| 7 | [07-strategic-prompt-stash.md](07-strategic-prompt-stash.md) | Design-shape prompt cards for the second-act capabilities (05 §3.1): proposed Koan-idiom APIs + reference usage patterns, frontier design sessions. |

Evidence basis: full project census (csproj/LOC), git churn analysis, 15 parallel pillar/corpus
audits (Stage 1, [evidence/](evidence/)), 6 philosophy/DX audits incl. a 59-claim promise
verification (Stage 2, [evidence/stage2/](evidence/stage2/)), and adversarial review of the
riskiest recommendations (Stage 4, [evidence/stage4/](evidence/stage4/) — four full verdicts;
four further reviewers were spot-verified inline after an external quota cut them short:
meta-package nuspec contents, the Secrets reflection hook's soft-coupling, CI workflow status,
and the OIDC-501 / Cache-residue score evidence, each already corroborated by ≥2 independent
Stage-1/2 auditors). Claims in these documents cite concrete projects/files.

## Baseline metrics (2026-06-10, branch `dev`)

- **Age / velocity**: first commit 2025-08-18 (~10 months); 1,519 commits; effectively a
  single implementor. Nearly every project touched within the last 6 months — only
  3 of ~85 `src/` directories are cold (`Koan.Canon.Core`, `Koan.Data.Lucene`,
  `Koan.Flow.Core`).
- **Size**: ~113 csproj projects under `src/` (≈80 top-level + ~30 connectors + services),
  ~150k lines of C#.
- **Distribution**: long-tail — median project < 1,000 LOC; 30+ projects under 500 LOC;
  largest single project is a bundled service (`Koan.Service.KoanContext`, 12.9k LOC),
  not a pillar core.
- **Decision corpus**: 280 ADRs in `docs/decisions`; `docs/` has 26 subdirectories.
- **Versioning**: NBGV `version.json` = 0.17.x is authoritative; README badge (v0.6.3)
  and several docs front-matter version pins are stale.
- **Debris confirmed in `src/`**: `Koan.Data.Lucene` (empty), `Koan.Cache.Adapter.Memory`
  (empty), `Koan.Jobs.Core` (one `.lscache` file), `Koan.Flow.Core` (one orphaned
  `TECHNICAL.md`), `Koan.Context` (one orphaned `.cs` file).
- **README/reality drift**: README showcases `Flow.OnUpdate<T>` event flows; no such API
  exists in `src/` (Flow pillar was removed; only `Pipeline*` extensions remain).

## How to re-run / extend

Each stage document records its own evidence and open questions. When a consolidation
facet completes (see `docs/decisions/ARCH-0084`+ and the module ledger), re-score the
affected pillar in stage 3 rather than rewriting the whole assessment.

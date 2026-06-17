# ARCH-0089: AI pillar dissolution — entity-AI core stays; ML-platform vertical migrates to Agyo + Zen Garden

**Status**: Accepted (2026-06-17) — *planning record; execution is a follow-on cross-repo track*
**Date**: 2026-06-17
**Deciders**: Enterprise Architect
**Scope**: Resolves assessment card **S3** ("AI pillar consolidation 19 → ~8 — its own mini-plan with an ADR"). Records the decision and the sequenced plan to **dissolve the AI "vertical" out of Koan**: Koan keeps an 8-project entity-AI core; the agentic / RAG / quality layer migrates to **Agyo**; the ML substrate (compute, model lifecycle) migrates to **Zen Garden**; the out-of-sln ZenGarden connector and the vaporware Training facade are archived. This ADR is the **canonical Koan-side record + plan**; the actual migration is a follow-on program tracked separately (card `X-ai-dissolution-migration`).
**Related**: **AGYO-0001** (Agyo charter — the "not core, yet valuable → Agyo" criterion) · **STACK-0001** (Koi → Zen Garden → Koan → Agyo layering canon) · `docs/assessment/08-agyo-reorganization.md` (the C-series reorg precedent) · **ARCH-0084** (unified capability model — the residual in-Koan AI capability-token migration) · `docs/assessment/04-recommendations.md` §6.

---

## Context

The S3 card premised a "19 → ~8" consolidation by *folding/demoting/cutting* the AI "vertical" (Orchestration, Agents, Compute, Eval, Training, Models, Review). A two-round empirical re-derivation (a full surface map, then a per-project migration dossier reading the Koan, Agyo, and Zen-Garden repos) found that premise **stale**: the vertical is no longer dead. A sample (`S18.Prism`) and a real AI test suite (`Koan.AI.Integration.Tests`, `Koan.AI.EndToEnd.Tests`, `Koan.AI.Eval/Models/Review.Tests`) now exercise most of it. Cutting it would *lose real, tested capability* — and `Koan.AI.Review` isn't even in the card.

But "real **and not core to Koan**" is precisely AGYO-0001's admission criterion. AGYO-0001 fixes Koan's core identity as **data · web · cache · jobs · mcp · auth · storage** — AI is not on that list; the *entity-AI integration* (`[Embedding]`, `[MediaAnalysis]`, `EntityAi`) is part of **data**. So the correct lens is not cut-vs-keep; it is **core-vs-peripheral**, and the peripheral-but-valuable AI capabilities belong in the sibling repos, not in Koan's release train.

### The decisive constraint — layering, not taste

AGYO-0001 / STACK-0001 are binding: **Agyo and Zen Garden depend on Koan's public packages; Koan never depends on them.** So the core/periphery line is *forced* by the dependency graph:

> Anything the Koan *core* depends on stays in Koan. Anything depended on *only* by the vertical can leave.

The dossier confirmed this empirically: **there are zero reverse-dependencies from Koan core into the vertical.** `Koan.AI` (facade) and `Koan.Data.AI` depend only on Contracts / Prompt / Storage / Media — never on Orchestration/Agents/Compute/Eval/Models/Review/Training. The vertical depends *downward* on the core. Extraction is mechanically clean.

---

## Decision

### 1. Koan keeps the entity-AI core (8 projects)

`Koan.AI.Contracts`, `Koan.AI.Contracts.Shared`, `Koan.AI` (facade), `Koan.AI.Prompt`, `Koan.Data.AI`, `Koan.AI.Web`, and the two default inference batteries `Koan.AI.Connector.Ollama` + `Koan.AI.Connector.LMStudio`. These are the published boundary the migrated packages reference. `Koan.Data.AI` is forced to stay (it compile-depends on `Koan.Data.Core` for the entity system); `Contracts.Shared` stays as the lifecycle-type boundary the departing packages `PackageReference`.

### 2. Agyo gets the agentic / RAG / quality layer

- **`Koan.AI.Orchestration` + `Koan.AI.Agents` → fold into `Agyo.Rag`** (Agyo already ships `Agyo.Rag` + `Agyo.Rag.Abstractions`). Chains (Orchestration) + ReAct agents (Agents) are the composition/agentic layer over Agyo's corpora/retrieval/tools — together they form one "agentic RAG" subsystem. This realises the card's "fold Orchestration + Agents" goal — in Agyo, not Koan.
- **`Koan.AI.Eval` → `Sylin.Agyo.Eval`** (standalone — model quality gates / drift / regression).
- **`Koan.AI.Review` → `Sylin.Agyo.Review`** (standalone — human-in-the-loop review queues).

### 3. Zen Garden gets the ML substrate

- **`Koan.AI.Compute` → cut-in-favor-of-ZG.** Zen Garden already owns hardware discovery + inventory (`GardenHardwarePuller`, the Resources domain). Koan's GPU detection is redundant; remove it. Koan apps that need local-compute resolution use ZG's API (or a thin `IComputeService` shim — decided at execution).
- **`Koan.AI.Models` → port to ZG** (model lifecycle: pull/convert/quantize/deploy/version). **High** effort: `ModelEntry` is an `Entity<>` (ORM), not a DTO, so the port decouples it (a read-only Koan cache fed from ZG, or DTO + REST). **`Koan.AI.Connector.HuggingFace` travels with Models** (it depends on Models; zero other consumers).

> **Zen Garden is Rust.** The ZG-bound pieces are a C#→**Rust** re-implementation, not a lift — a materially larger and separate effort from the C#→C# Agyo lift. Compute is mostly *already there*; Models is a genuine port; see the plan.

### 4. Archive

- **`Koan.AI.Connector.ZenGarden`** — already out of `Koan.sln`, zero consumers; formally archive.
- **`Koan.AI.Training`** — vaporware (no in-repo adapter, no tests, throws on unregistered adapter). Archive the Koan facade. Fine-tuning, when built, belongs with the ML substrate (ZG), not Agyo.

### 5. Samples & the residual capability migration

- `S5.Recs`, `S6.SnapVault`, `S7.Meridian` **stay** (they use only the staying core AI layer). **`S18.Prism` → Agyo** as the flagship cross-tier demo (it exercises the whole vertical; re-points Compute/Models refs to ZG).
- **Residual ARCH-0084 work (in Koan):** once the vertical leaves, only the *core* capability declarations need token migration — Mechanism 1 (`AiCapability` string catalog in `Koan.Core/AI`) + Mechanism 2 (`AiCapabilityConfig` source config), across the staying adapters (Ollama/LMStudio) + the facade router. The vertical's enums (`ComputeCapability`/`ModelCapability`) leave with their projects. Tracked as its own follow-on (it is no longer "the largest un-migrated surface" — that surface is leaving).

---

## Sequenced plan (AGYO-0001 transition safety)

Execution is **out of this ADR's scope** (a multi-repo program). The order, lowest-risk first:

1. **Agyo lift — standalone first.** Stand up `Sylin.Agyo.Eval` + `Sylin.Agyo.Review` green in Agyo (C#→C# port; ARCH-0079 spec on arrival per AGYO-0001 §4). No Koan change yet.
2. **Agyo fold — agentic RAG.** Fold `Orchestration` + `Agents` into `Agyo.Rag` (harmonise `Agents.EntityToolGenerator` with `Agyo.Rag`'s `RagRetrievalTools`); green in Agyo.
3. **ZG substrate.** Compute: confirm ZG coverage → delete Koan.AI.Compute (+ optional shim). Models: port to ZG (decouple `ModelEntry`); HuggingFace follows. (Largest item; Rust.)
4. **Re-point.** Move `S18.Prism` to Agyo; split `Integration`/`EndToEnd` tests (Agyo + ZG); `Eval/Review.Tests`→Agyo, `Models.Tests`→ZG. `Prompt.Tests`, `Contracts.Shared.Tests`, `Data.AI.Tests`, Ollama/LMStudio unit tests **stay**.
5. **Strip.** Only after the new packages are green and consumers re-point: remove the vertical from `Koan.sln`/`src`, archive ZenGarden connector + Training, sweep the doc ledgers + SURFACES row. Koan keeps publishing the staying packages throughout.

---

## Consequences

- **Koan AI: 17 → 8 projects**, with **zero capability loss** — the value moves to the repos whose identity it fits (Agyo = app-building tools; ZG = AI compute/model substrate). Koan's release train sheds the ML-ops maintenance surface.
- **Cross-repo program, not a card.** Two unlike halves: a moderate C#→C# Agyo lift (mirrors the proven C-series reorg) and a larger C#→Rust ZG port. Sequenced + transition-safe.
- **Risks:** the Models port (ORM decoupling) is the high-complexity item; `S18.Prism` is the integration canary; the residual in-Koan ARCH-0084 token migration is a separate, now-smaller job.
- The card's literal "fold/demote in Koan" instructions are **superseded** by this dissolution.

## Appendix — per-project dossier (verified)

| Project | → | Why | Effort |
|---|---|---|---|
| Contracts, Contracts.Shared, AI, Prompt, Data.AI, AI.Web, Ollama, LMStudio | **Koan** | core / forced (Data.AI↔Data.Core; Contracts.Shared = boundary) | — |
| Orchestration + Agents | **Agyo** (fold `Agyo.Rag`) | agentic-RAG layer over Agyo corpora; 0 core reverse-deps | med |
| Eval | **Agyo** (standalone) | quality gates; deps = core contracts only | low |
| Review | **Agyo** (standalone) | HITL queues; deps = Core/Data.Core only | low |
| Compute | **ZG** (cut-in-favor-of) | ZG already has hardware discovery/inventory | low |
| Models (+ HuggingFace) | **ZG** (port) | ZG has catalog, not full lifecycle; `ModelEntry` ORM decouple | high |
| Training | **archive** (future ZG) | vaporware; no adapter/tests | low |
| Connector.ZenGarden | **archive** | already out-of-sln, 0 consumers | low |
| S18.Prism | **Agyo** | exercises the whole vertical → flagship demo | high |
| S5.Recs / S6.SnapVault / S7.Meridian | **Koan** | use only the staying core | — |

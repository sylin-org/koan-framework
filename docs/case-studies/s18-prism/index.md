---
type: GUIDE
domain: ai
title: "S18.Prism case study"
audience: [architects, developers]
status: current
last_updated: 2026-03-25
framework_version: v0.6.3
validation:
	date_last_tested: 2026-03-25
	status: verified
	scope: samples/S18.Prism
---

# S18.Prism case study

**Contract**

- **Audience**: Architects and senior engineers building self-hosted knowledge intelligence systems on Koan's AI lifecycle modules.
- **Inputs**: Familiarity with Koan entity-first development, AI abstractions (AI-0021+), and ZenGarden model discovery. Access to the `samples/S18.Prism` solution.
- **Outputs**: Repeatable blueprint for a five-phase pipeline — ingestion, knowledge storage, active research, interactive lenses, and learning feedback loops.
- **Error modes**: Missing source adapters, unreachable model endpoints, embedding provider unavailability. Each phase below documents fallback behavior.
- **Success criteria**: Teams can replay the sample end-to-end, understand bounded context boundaries, and adapt the pattern to their own knowledge workloads.

**Tagline**: Raw information in, structured knowledge out.

## Narrative

S18.Prism is the dogfood sample for Koan's AI lifecycle modules (AI-0022 through AI-0031). It composes nine bounded contexts into a personal knowledge intelligence system that ingests heterogeneous sources, extracts structured knowledge, conducts active research, and closes a learning feedback loop.

### Bounded contexts

| Context | Responsibility |
| --- | --- |
| **Knowledge** | Note storage with ContentBlocks, semantic search via embeddings |
| **Spaces** | Organizational containers for notes and sources |
| **Ingestion** | Universal loader, content extraction chain, format normalization |
| **Sources** | Adapter registry — RSS, HackerNews, GitHub, FolderWatch, Web |
| **Research** | Active research briefs, findings, crawling workflows |
| **ModelIndex** | ZenGarden integration for model discovery and catalog sync |
| **Interaction** | Lenses (view perspectives), Pulse (daily briefing), Q&A |
| **Learning** | Training corrections, LoRA fine-tuning feedback loop |
| **Setup** | Onboarding, space configuration, source binding |

### Key entities

- **Note** — primary knowledge unit with polymorphic `ContentBlock` children (text, code, table, image)
- **Space** — organizational boundary scoping notes and sources
- **Source** — adapter-backed external feed (RSS, HackerNews, GitHub, FolderWatch, Web)
- **ResearchBrief** — user-defined research objective with status lifecycle
- **Finding** — atomic research result linked to a brief and optionally to a note

### Background workers

| Worker | Role |
| --- | --- |
| `SourcePullWorker` | Polls configured sources on schedule, feeds items into the ingestion pipeline |
| `ResearchBriefWorker` | Executes active research briefs, produces findings, updates brief status |
| `ModelCrawlerWorker` | Syncs available models from ZenGarden topology into the local model index |

### Content extraction

Extractors run in priority order: **Text** (fast, deterministic) then **AI Fallback** (delegates via Chat category from AI-0021). SignalR pushes real-time extraction progress to connected clients.

## Phases

| Phase | Highlights |
| --- | --- |
| **Ingestion** | Universal loader accepts any source type, content extraction chain with priority ordering, source pull adapters on configurable schedules |
| **Knowledge** | Note persistence with ContentBlocks, automatic embedding generation, semantic search across spaces |
| **Research** | User-defined research briefs, automated finding generation, crawling workflows driven by `ResearchBriefWorker` |
| **Interaction** | Lenses provide perspective-shifted views over notes, Pulse generates daily briefings, conversational Q&A over the knowledge base |
| **Learning** | Training corrections capture user feedback, LoRA fine-tuning feedback loop refines domain-specific model behavior |

## Quick start

1. `./start.bat` from `samples/S18.Prism` to launch the API, backing stores, and model services.
2. Create a Space and bind at least one Source (e.g., an RSS feed URL).
3. `SourcePullWorker` begins ingestion automatically; watch SignalR events for extraction progress.
4. Use the Interaction endpoints to query notes via semantic search, apply Lenses, or generate a Pulse briefing.
5. Correct any AI output through the Learning surface to feed the fine-tuning loop.

## ADR references

AI-0022 through AI-0031 define the lifecycle modules that S18.Prism exercises:

- **AI-0022** Model Catalog — indexed model metadata, ZenGarden sync
- **AI-0023** Compute — inference routing and resource management
- **AI-0024** Prompt — template management, variable binding
- **AI-0025** Chain — multi-step orchestration pipelines
- **AI-0026** Training — fine-tuning job lifecycle, LoRA adapters
- **AI-0027** Eval — output quality scoring, regression detection
- **AI-0028** Review — human-in-the-loop correction capture
- **AI-0029** Media Analysis — image/audio/video content extraction
- **AI-0030** Embedding — vector generation, storage, similarity search
- **AI-0031** Intent-Capability Resolution — recipe-based AI task dispatch

## Related reading

- [`Case study: S13.DocMind`](../s13-docmind/index.md)
- [`ADR: AI-0021 Category-driven AI`](../../decisions/AI-0021-category-driven-ai-with-convention-defaults.md)
- [`ADR: AI-0032 Intent-capability resolution with recipes`](../../decisions/AI-0032-intent-capability-resolution-with-recipes.md)

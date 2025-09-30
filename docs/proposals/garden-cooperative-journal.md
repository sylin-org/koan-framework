---
type: PROPOSAL
domain: docs
title: "Garden Cooperative Journal How-To"
audience: [developers, architects, ai-agents]
status: current
last_updated: 2025-09-28
framework_version: v0.6.2
validation:
  date_last_reviewed: 2025-09-28
  status: in-progress
  scope: docs/proposals/garden-cooperative-journal.md
---

# Garden Cooperative Journal How-To Spec

## Narrative posture

- Anchor every decision to the garden storyline; avoid abstract best-practice detours unless the story calls for them.
- Keep the entity cast intentionally small (`Plot`, `Reading`, `Reminder`, `Member`) so readers can visualize the cooperative without diagrams.
- Maintain a slice-of-life tone that follows the crew through a single day in the garden; code snippets should feel like journal entries come alive.
- Favor demonstrations of Koan capabilities (entity statics, relationship helpers, Flow batches, enrichment flags) over prescriptive rules lists.
- Center the walkthrough on knowledge-building moments—each beat should teach one concrete Koan technique the reader can reuse elsewhere.

## Experience goals

- Readers should understand how to start with SQLite and leave room for future adapter swaps without breaking the narrative.
- Controllers, lifecycle hooks, and Flow pipelines must speak the same language as the storyboard moments (dawn check-in, midday review, evening journal).
- Optional extensions (digest emails, Mongo pivot, AI add-ons) belong in sidebars that invite exploration without derailing the core story.
- Relationship helpers (`GetParent`, `GetChildren`, `Relatives`) should appear exactly where the characters need them, showing utility instead of lecturing about it.

## Boundaries and follow-ups

- Keep Chapter 1 self-contained; defer heavy production guardrails, migrations, and observability patterns to later chapters.
- Document future chapters (Mongo swap, reminder digest worker, AI curation) in "Next Steps" so contributors know where to extend the story.
- Re-run the strict docs build after every revision to confirm the narrative stays publish-ready.

---

## Garden Cooperative sample (unified proposal)

### Contract

- **Inputs**: Koan v0.6.2 packages, SQLite provider defaults, existing guide narrative, developer workstation with .NET 9.
- **Outputs**: A runnable sample under `samples/guides/g1c1.GardenCoop` featuring a console host, Koan Web API, and static AngularJS client; README + validation notes wired back to the guide.
- **Error modes**: Startup misconfiguration (missing provider reference), browser POST failures (CORS/mime), lifecycle regressions (dry average logic), sample shutdown race conditions.
- **Success criteria**: Console host boots and logs lifecycle events, SPA can inject readings and observe reminders, window close triggers graceful shutdown, docs and sample cross-reference each other, validation script passes.

### Motivation

- Deliver a tangible companion to the published guide so readers can experience the “paper → slice” journey with zero scaffolding.
- Codify the naming/layout pattern for future guide-backed samples without disrupting existing `S#.*` numbering.
- Provide a baseline harness for regression tests that verify write-path automation and reminder lifecycle behavior.

### Scope

- **In scope**
  - Single console project that calls `AddKoan()` and hosts the HTTP endpoints plus console experience—no separate API assembly.
  - Static AngularJS (1.x) SPA embedded directly under the project’s `wwwroot`, providing manual/timed reading posts and reminder views.
  - Entity and lifecycle implementation matching the guide (`Plot`, `Member`, `Reading`, `Reminder`, hysteresis optional later).
  - Tests: at minimum, focused unit coverage for average/dry threshold logic plus a smoke integration check.
  - Documentation touchpoints: sample README, updates to `docs/guides/garden-cooperative-journal.md`, and validation metadata (flip status once checks pass).
- **Out of scope** (future work)
  - Mongo adapter swap, digest workers, AI journaling add-ons, infrastructure hardening (observability, migrations).
  - Replacing AngularJS with modern frameworks; the SPA intentionally stays zero-build.

### Architecture notes

- Project layout: `samples/guides/g1c1.GardenCoop/` single console project (with optional tests) containing Program, entities, automation, and `wwwroot`.
- During `Program.cs`, call `AddKoan()`, wire `UseStaticFiles()`, and expose AngularJS assets alongside the API endpoints.
- Logging: enable console output with timestamps; highlight reminder activation/ack notes to echo journal tone.
- SPA: single-page HTML loading AngularJS and supporting libraries from CDNs (locked versions) to keep local scaffolding minimal; interacts via `fetch`/`$http` against same-origin API.
- Styling/assets: rely on CDN-hosted CSS/icon sets wherever feasible; keep checked-in static files to the essential HTML shell and lightweight helpers.
- Visual polish: pair the bare HTML with a featherweight CDN stylesheet (or a handcrafted 200-line CSS file) to showcase how much ambiance is possible with minimal assets—favor gentle typography and spacing over component libraries.
- Build flow: rely on default dotnet publish; ensure `wwwroot` content gets copied (`<Content Include="wwwroot/**/*" CopyToOutputDirectory="PreserveNewest" />`).

### Naming & taxonomy

- Introduce `samples/guides/` to hold guide-first experiences; first entry `g1c1.GardenCoop`.
- Maintain existing `S#.*` root folders for legacy/current samples; subfolder communicates guide linkage without renaming everything.
- Reference the sample explicitly inside guide metadata and sample README to keep bidirectional discoverability.

### Work plan

1. Scaffold single console project (plus optional tests) under `samples/guides/g1c1.GardenCoop`.
2. Implement entities, lifecycle automation, and API/controller wiring per guide within that project.
3. Add AngularJS SPA with manual/timed posting controls and reminder dashboard under the project `wwwroot`.
4. Author README with quickstart commands, endpoints, and shutdown behavior.
5. Wire unit/integration tests; update validation stanza in both guide and proposal.
6. Update docs (guide, samples index) to reference the sample; run strict docs build.

### Validation

- `dotnet build` and sample-specific tests (Unit + smoke) must pass in CI.
- Run `pwsh scripts/build-docs.ps1 -ConfigPath docs/api/docfx.json -Strict` to ensure links and metadata remain valid.
- Manual sanity: launch console host, trigger SPA actions, close window to verify graceful shutdown.

### Follow-up decisions

- Timed sensor simulation will ship as a simple UI toggle implemented directly in the AngularJS client—no server-side configuration flags.
- Lifecycle demonstrations stay conversational: emit a `Sending email (fake)` console/log message instead of structured telemetry for now.
- For future chapters, lean toward hierarchical naming such as `g1c1`, `g1c2`, etc.; capture that convention in the sample README once we finalize chapter planning.

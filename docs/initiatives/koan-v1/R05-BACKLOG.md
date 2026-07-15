---
type: ARCHITECTURE
domain: framework
title: "R05 Golden V0-to-V1 Backlog"
audience: [architects, maintainers, ai-agents]
status: current
last_updated: 2026-07-14
framework_version: v0.17.0
validation:
  date_last_tested: 2026-07-14
  status: reviewed
  scope: dependency-ordered R05 implementation cards
---

# R05 golden V0-to-V1 backlog

R05 protects one cumulative anonymous application, not a catalogue of framework features. FirstUse
remains the shortest result. GoldenJourney proves that the same composition model survives meaningful
growth without moving infrastructure plumbing into the application.

## Architecture

- One `samples/GoldenJourney` application; no sibling versions, generators, overlays, or build flags.
- Six ordered business checkpoints are executable assertions over the cumulative result.
- `ReviewRequest : Entity<ReviewRequest>` owns business state and rules.
- A business-named controller prevents generic CRUD from bypassing those rules.
- Bounded custom MCP tools collaborate through the same domain methods and never own final approval.
- Source and package lanes use shared process/MCP probe infrastructure but retain separate evidence.
- FirstUse remains unchanged and independently release-gating.

## Sequence

```text
R05-01 business spine
  -> R05-02 reactive + agentic collaboration
      -> R05-03 package clean room + independent rehearsal
```

## Cards

| ID | Status | Meaningful result | Depends on | Evidence |
|---|---|---|---|---|
| [R05-01](work-items/r05/R05-01-business-spine.md) | passed | The cumulative app reads as a review workflow and persists/reaches one business result. | R04 | GoldenJourney domain/controller; pure rule and running REST assertions |
| [R05-02](work-items/r05/R05-02-reactive-agentic.md) | passed | Durable assessment and bounded agent recommendation remain inspectable and honest. | R05-01 | Jobs facts/progress suites; 11-step source process proof |
| [R05-03](work-items/r05/R05-03-clean-room-rehearsal.md) | in-progress | A package-only checkout and independent readers reproduce the supported journey. | R05-02 | fresh 84-package clean room passed; agent round confirmed the core and entered bounded repair-and-repeat; repairs 1/5 through 3/5 complete |

## Program rules

- Every checkpoint must add a business outcome, not preparatory scaffolding.
- The application may name infrastructure intent but may not own repositories, schemas, queue loops,
  MCP protocol plumbing, runtime-fact construction, or backend-election logic.
- A public claim requires current code plus executable evidence; retained artifacts from an earlier
  dirty-tree state cannot close the package gate.
- Human and AI rehearsals are final acceptance evidence, not automation substitutes.
- Private downstream applications inform questions only and are never named or copied into evidence.

---
type: GUIDE
domain: framework
title: "R05-01 - Establish the Business Spine"
audience: [maintainers, developers, ai-agents]
status: current
last_updated: 2026-07-14
framework_version: v0.17.0
validation:
  date_last_tested: 2026-07-14
  status: verified
  scope: GoldenJourney business and running source assertions
---

# R05-01 — Establish the business spine

- Status: `passed`
- Depends on: R04
- Owner: GoldenJourney domain and Web surface

## Meaningful result

A review request can be opened, persisted, retrieved, and assessed by reading business-named code.
The application contains no repository, schema, provider registration, or generic CRUD escape hatch.

## Result

[`ReviewRequest`](../../../../../samples/GoldenJourney/Domain/ReviewRequest.cs) expresses intake,
priority assessment, and non-final recommendation rules. The controller offers `Open`, `Get`,
`Assess`, and `Assessment`; it does not inherit the generic entity controller because arbitrary
mutation would bypass the workflow.

The project expresses SQLite intent by reference. `Program.cs` remains the complete four-line Koan
host. A pure business assertion proves a high-impact request becomes Critical; the running source
probe proves create/read persistence against an isolated SQLite store.

## Acceptance result

- Outcome: PASS
- Evidence: `samples/GoldenJourney`; `GoldenJourneyContractTests` business and process assertions.
- Unsupported: this card does not claim authorization design, distributed execution, package-only
  installation, or final human workflow.

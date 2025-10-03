---
type: DEV
domain: docs
title: "Narrative sample entity naming simplification"
audience: [developers, architects, ai-agents]
status: current
last_updated: 2025-09-28
framework_version: v0.6.2
validation:
  date_last_tested: 2025-09-28
  status: verified
  scope: docs/decisions/DX-0042-narrative-samples-simple-entity-names.md
---

# DX-0042: Narrative sample entity naming simplification

Status: Accepted

## Context

The "der" documentation slice originally introduced entities such as `GardenPlot`, `SoilReading`, and `WateringReminder`. While technically accurate, the longer names and prefixed types added friction to a narrative-first walkthrough that is meant to highlight Koan capabilities without overwhelming readers. Contributors also noted that the scenario only exposes one reminder type, making the more specific `WateringReminder` label redundant.

## Decision

1. Adopt the concise entity set `Plot`, `Reading`, `Reminder`, and `Member` for the narrative-bound garden sample.
2. Align controller routes, API examples, and Flow pipelines with the simplified names (for example, `POST /api/garden/readings` and `GET /api/garden/reminders`).
3. Surface the optional reminder-extension sidebar using the simplified vocabulary while keeping implementation details out of the main storyline.
4. Treat this naming scheme as the baseline for Chapter 1 (SQLite) material; future chapters that introduce MongoDB or alternate reminder types will explicitly call out any deviations.

## Rationale

- **Lower cognitive load** – Simple, single-word entities are easier to visualize and keep the focus on Koan behaviors instead of type names.
- **Narrative consistency** – The slice-of-life story reads more naturally when the vocabulary matches everyday language used by the cooperative members.
- **Extensibility** – Starting with terse names leaves room to introduce specialized derivatives in later chapters without rewriting earlier content.

## Scope

- Applies to all Garden Journal documentation in `/docs/guides` and supporting samples that reference the narrative.
- Does not alter framework primitives or other samples unless they explicitly adopt the Garden storyline.
- Subsequent chapters must highlight any additional reminder categories or naming changes to avoid ambiguity.

## Consequences

- Existing drafts and snippets using the longer names must be updated before publication.
- API examples, Flow pipelines, and lifecycle hook discussions will reference the simplified nouns.
- Editors should enforce the naming scheme during doc reviews to avoid regressions.

## Follow-ups

- Update the Garden Journal outline and sample snippets to match the simplified entities.
- Verify that upcoming MongoDB-focused material keeps the original names intact or clearly documents any new variants.
- Refresh cross-links in the Data Modeling Playbook once the narrative section is rewritten with the simplified vocabulary.

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

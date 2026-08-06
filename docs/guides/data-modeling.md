---
type: GUIDE
domain: data
title: "Data modeling playbook (superseded)"
audience: [developers, architects, ai-agents]
status: superseded
last_updated: 2026-07-22
framework_version: v0.20.0
validation:
  date_last_tested: 2026-07-22
  status: reviewed
  scope: compatibility pointer only
---

# Data modeling playbook (superseded)

This playbook mixed the core Entity contract with lifecycle, Communication, Jobs, AI, vector, and
application-design advice. That made the same Koan capability appear to have several owners and let
examples drift apart.

Use the task owner that matches the work:

- [Persist and query business state](../reference/data/index.md) — Entity modeling, provider choice,
  relationships, paging, testing, and correction paths.
- [Entity lifecycle](../reference/data/entity-lifecycle.md) — write policy and persistence invariants.
- [Entity access and streaming](data/entity-access-and-streaming.md) — large sequential workloads.
- [Communication](../reference/communication/index.md) — business occurrences and delivery.
- [AI](../reference/ai/index.md) — generation, embeddings, and vector retrieval.
- [Jobs](jobs-howto.md) — retryable background work.

This file remains only so existing bookmarks reach the current owners.

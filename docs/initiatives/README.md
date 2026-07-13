---
type: GUIDE
domain: framework
title: "Koan Initiatives"
audience: [architects, maintainers, ai-agents]
status: current
last_updated: 2026-07-13
framework_version: v0.17.0
validation:
  date_last_tested: 2026-07-13
  status: verified
  scope: initiative index and links
---

# Koan Initiatives

This directory contains bounded, actively governed programs that change more than one pillar or
require coordinated work across multiple sessions. It is an execution surface, not a second
architecture canon:

- architecture decisions remain in [`docs/decisions/`](../decisions/index.md);
- current framework principles remain in
  [`docs/architecture/principles.md`](../architecture/principles.md);
- shipped behavior remains proven by source, tests, and current reference documentation;
- completed or abandoned initiatives move to the archive rather than remaining active indefinitely.

## Active

| Initiative | Mission | State |
|---|---|---|
| [Koan V1 reorganization](koan-v1/README.md) | Move Koan toward an Entity-centered V1 through meaningful, gated increments | Active — R00 history rewrite |

## Initiative contract

An active initiative must provide:

1. a read-first charter;
2. a dependency-ordered roadmap without duplicated live status;
3. one authoritative progress ledger;
4. bounded, self-contained work items;
5. an acceptance contract;
6. a current-session handoff;
7. explicit archival or completion criteria.

Private application evidence never enters an initiative. Only anonymous, independently reproducible
framework findings may be recorded here.

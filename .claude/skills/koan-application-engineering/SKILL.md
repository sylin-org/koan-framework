---
name: koan-application-engineering
description: Plan and implement a greenfield Koan application or migrate an existing .NET application by mapping business intent to the smallest public capability, preserving integrations, removing bespoke duplication, and proving behavior proportionally.
pillar: framework
status: current
last_validated: 2026-07-23
card: docs/guides/agent-application-engineering.md
---

# Koan application engineering

Use this skill for application-wide design, a brownfield migration, or when a request spans several
Koan pillars. Load a narrower capability skill after this one identifies the owner.

## Before editing

State:

1. application intent;
2. complete public expression: package, C#, configuration, context, and runtime actions;
3. guarantee and corrective failure;
4. every required user/operator action;
5. one behavior owner.

For brownfield work, inventory observable contracts—routes, payloads, identity, persistence names,
external endpoints, environment keys, and topology—then classify each custom mechanism as keep,
absorb, rebuild, or delete. Infrastructure continuity is a constraint; bespoke application mechanics
are not automatically a constraint.

## Implementation loop

1. Read [`llms.txt`](../../../llms.txt) and the owning capability page.
2. Find the closest current public pattern and its focused tests.
3. Prefer `Entity<T>`, standard Entity verbs, reference-driven composition, and one `AddKoan()`.
4. Implement one ownership transfer at a time.
5. Prove focused behavior, real host composition, and corrective failure.
6. Remove the superseded mechanism and record the remaining integration risk.

Do not create repositories around ordinary Entity operations, manually register referenced Koan
modules, mirror a domain model for HTTP or MCP, or add infrastructure that merely compensates for a
missing public framework seam. Report the seam with an anonymous reproduction instead.

## Output

Keep the handoff compact:

| Intent | Previous owner | Current owner | Proof | Remaining risk |
|---|---|---|---|---|

Use the complete workflow in the
[agent application engineering guide](../../../docs/guides/agent-application-engineering.md).

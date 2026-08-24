---
type: REFERENCE
domain: mcp
title: "Agent surfaces"
audience: [ai-agents, developers]
status: current
last_updated: 2026-08-23
framework_version: v1.0.0
validation:
  date_last_tested: 2026-08-23
  status: passed
  scope: docs/capabilities/agents.md - route table verified against leaf targets
---

# Agent surfaces

There is no second agent-specific domain model to drift. An in-application agent is
orchestration your app invokes; MCP is a surface an outside client invokes. Both read the same
Entities and enforce the same `[Access]` rules as HTTP - the agent sees only what you exposed.

## Route by need

| The request says | Fetch |
|---|---|
| "let Claude (or any MCP client) work with my Entities" | [Entity MCP surface](agents/entity-mcp.md) |
| "give a local model bounded tools for a multi-step task" | [in-application agent](agents/in-app-agent.md) |
| "what would an agent actually see?" | [Entity MCP surface](agents/entity-mcp.md) - caller-visible operations, then `$koan-explain` |

## Standing constraints

- Advertisement is enforcement: a caller's tool list contains only what its identity may use.
  A forbidden Entity, operation, or field does not appear at all.
- `AllowMutations = false` removes mutation tools; it is a projection switch, not an
  authorization system - `[Access]` remains the authority.

## Do not, at this level

- Do not hand-write tool manifests, JSON schemas, or dispatch handlers beside the projection.
- Do not build a mirrored "agent DTO" model - attributes shape what the same model shows.

For the one-screen maturity view, see
[Agent surfaces in the capability map](../reference/capability-map.md#agent-surfaces).

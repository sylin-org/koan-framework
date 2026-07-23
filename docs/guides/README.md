---
type: GUIDE
domain: framework
title: "Task guides"
audience: [developers, operators, ai-agents]
status: current
last_updated: 2026-07-22
framework_version: v0.20.0
validation:
  date_last_tested: 2026-07-22
  status: verified
  scope: distinct public tasks subordinate to capability-pillar contracts
---

# Task guides

Capability pillars own Koan's semantics, guarantees, provider choices, and corrections. A task guide
exists only when one concrete action needs more depth than its pillar page; it does not redefine the
capability.

| Task | Owning pillar | Guide |
|---|---|---|
| Process a large Entity set without unbounded materialization | Data | [Entity access and streaming](data/entity-access-and-streaming.md) |
| Configure external sign-in and application authorization | Identity and isolation | [Authentication setup](authentication-setup.md) |
| Apply fail-closed tenant isolation | Identity and isolation | [Tenancy](tenancy-howto.md) |
| Add retries, schedules, chains, or durable background execution | Work and communication | [Background jobs](jobs-howto.md) |
| Build and serve a named media transform | State and content | [Media recipes](media-recipes-howto.md) |
| Build a canonical Entity pipeline | Canon | [Canon capabilities](canon-capabilities-howto.md) |
| Expose governed agent operations | Agents | [Agent-native MCP](mcp-agent-native-howto.md) |
| Host MCP over HTTP | Agents | [MCP HTTP transport](mcp-http-sse-howto.md) |
| Test Entity and provider behavior | Testing and operations | [Testing an application](testing-your-app.md) |
| Review composition drift | Foundation and composition | [Composition lockfile](composition-lockfile.md) |

For cache, storage, media, AI, vector, Web, and operational diagnostics, begin at the
[capability curriculum](../index.md). Package companions own provider-specific details.

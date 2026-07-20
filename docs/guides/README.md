---
type: GUIDE
domain: framework
title: "Developer guides"
audience: [developers, architects, ai-agents]
status: current
last_updated: 2026-07-17
framework_version: v0.20.0
validation:
  date_last_tested: 2026-07-17
  status: in-progress
  scope: current public guide map
---

# Developer guides

Start with a business need, then add the smallest semantic capability that expresses it. These are
the current public guide paths; an unlisted document in this directory may be migration or
engineering material and is not alternate product guidance.

| Need | Start here | Public concept added |
|---|---|---|
| Build a conventional HTTP API | [Web reference](../reference/web/index.md) | `EntityController<T>` |
| Persist, query, page, or stream Entities | [Entity data reference](../reference/data/index.md) | Entity statics and instance verbs |
| Cache Entity state | [Cache reference](../reference/data/cache.md) | `[Cacheable]` |
| Run durable work | [Jobs pillar map](../reference/cards/jobs.md) | `IKoanJob<T>` and `.Job` / `.Jobs` |
| Raise Entity events or transport state | [Communication reference](../reference/communication/index.md) | `.Events` and `.Transport` |
| Expose governed agent operations | [Agent-native MCP guide](mcp-agent-native-howto.md) | `[McpEntity]` and `[McpTool]` |
| Host MCP over a network | [MCP over HTTP](mcp-http-sse-howto.md) | one transport setting and security boundary |
| Add authentication and authorization | [Auth pillar map](../reference/cards/auth.md) | provider reference/config and `[Access]` |
| Add tenant segmentation | [Tenancy pillar map](../reference/cards/tenancy.md) | `Tenant.Use` and `[HostScoped]` |
| Test Entity/provider behavior | [Testing your app](testing-your-app.md) | conformance specification |
| Review composition drift | [Composition lockfile](composition-lockfile.md) | checked-in `koan.lock.json` |
| Evaluate experimental native deployment | [NativeAOT boundary](../reference/cards/nativeaot.md) | current compiler blocker and JIT fallback |
| Diagnose a running application | [Troubleshooting](../support/troubleshooting.md) | no new application concept |

For AI/vector, media, storage, orchestration, and other surfaces, check the
[generated product surface](../reference/product-surface.md) first. It distinguishes exercised
capabilities from experimental or unassessed package families and links to their current evidence.

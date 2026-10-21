---
id: SEC-0014
slug: exposure-mode-is-enforced-on-invoke
domain: Security
status: Accepted
date: 2026-10-21
title: The MCP exposure mode is enforced where tools run
related:
  - AI-0014
  - SEC-0011
  - WEB-0072
---

# SEC-0014: The MCP exposure mode is enforced where tools run

## Context

`Koan:Mcp:Exposure` chooses what an agent gets: `Code` (the `koan.code.*` sandbox only), `Tools` (entity tools and
custom verbs), or `Full`. `tools/list` applied it. `CallToolFor` checked it only for the code tools, so under `Code`
any client could still call `todo.delete-all` or a custom verb by name; the Explorer try-it and its map ignored it
entirely (ledger NK-118). AI-0014 already says hiding a tool from the listing is not a security control on its own.

## Decision

**`McpExposure` is the one exposure decision, and every surface that lists or runs a tool asks it.**

1. `McpExposure.Resolve(options)` is the former handler-private resolution (configuration, then the first
   `[McpDefaults]` assembly attribute, then `Full`; `Auto` is `Full`). `ExposesTools` and `ExposesCode` read it.
2. `CallToolFor` refuses entity tools and custom verbs with `errorCode` `exposure_mode` unless the mode exposes tools.
   Code-mode SDK calls reach the executor directly and keep working under `Code`.
3. The Explorer try-it answers `403` with `exposure_mode`, and its map lists no entity tools or custom verbs, under a
   mode that does not expose tools.

## Compatibility

- Under `Exposure = Code`, calling an entity tool or a custom verb by name now fails; the code tools are unchanged.
- The Explorer console under `Code` shows no tools and cannot run them.
- `McpExposure` and the `exposure_mode` error code are added; no signature changes.

## Evidence

- `ExposureEnforcementSpec` (Koan.Mcp.Conformance.Tests), under `Code`: an entity tool and a custom verb called by
  name are refused with `exposure_mode`; the sandbox still reads entities. The two refusals failed before.
- `ExplorerExposureSpec` (Koan.Mcp.Explorer.IntegrationTests), under `Code`: try-it answers `403 exposure_mode` and the
  map lists nothing. Both failed before.

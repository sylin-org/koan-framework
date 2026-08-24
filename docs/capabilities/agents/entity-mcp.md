---
type: REFERENCE
domain: mcp
title: "Entity MCP surface"
audience: [ai-agents, developers]
status: current
last_updated: 2026-08-23
framework_version: v1.0.0
validation:
  status: not-yet-tested
  scope: docs/capabilities/agents/entity-mcp.md
---

# Entity MCP surface

Make selected Entity operations and business workflows discoverable to an MCP client without
mirroring the model or writing CRUD tool handlers.

## You need

| Piece | Package | Note |
|---|---|---|
| Entity tools, resources, STDIO, and HTTP transport | `Sylin.Koan.Mcp` | `[McpEntity]` opts in one Entity |
| Human inspection console (optional) | `Sylin.Koan.Mcp.Explorer` | shows the caller-visible governed surface |
| Governed Jobs and Cache operations (optional) | `Sylin.Koan.Mcp.Operations` | exact grants, confirmation, and audit for operations |
| Authenticated remote callers (optional) | `Sylin.Koan.Web.Auth` | transport authentication is not business authorization |

## The constraint box

> **The constraint:** MCP and HTTP must reach the same authorization, tenant, filtering, and
> lifecycle rules. Exposure and `AllowMutations = false` are not authorization. Remote HTTP is a
> public surface; STDIO trusts the local process owner and must not be relayed as though it carried
> HTTP identity.

## Choose exposure and reach separately

| Decision | First posture | When to widen |
|---|---|---|
| Read or write | expose read-only operations first | add mutations only after deciding what the agent may do unattended |
| Entity operation or custom workflow | reuse generated Entity operations | use `[McpTool]` only for a named business action beyond them |
| Local or remote | STDIO when the client owns the local process | Streamable HTTP only when a network client must connect |
| Human inspection | facts and caller-visible resources first | add Explorer when operators need governed try-it |

## Leaves

- **Build and MCP-specific negative proof:**
  [let an agent use the app](../../recipes/let-an-agent-use-my-app.md)
- **Authoring contract:** [agent-native guide](../../guides/mcp-agent-native-howto.md)
- **Remote transport contract:** [HTTP transport guide](../../guides/mcp-http-sse-howto.md)
- **Package contract:** runtime mechanics and boundaries:
  [MCP README](https://github.com/sylin-org/koan-framework/blob/main/src/Koan.Mcp/README.md)

`koan://self`, `koan://entities`, and `koan://facts` let the client inspect what this caller can use
and why it composed.

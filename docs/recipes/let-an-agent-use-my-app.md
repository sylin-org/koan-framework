---
type: RECIPE
recipe: let-an-agent-use-my-app
title: "Let an outside agent use my application"
domain: mcp
status: current
last_updated: 2026-08-19
audience: [ai-agents, developers]
framework_version: v1.0.0
validation:
  status: not-yet-tested
  scope: docs/recipes/let-an-agent-use-my-app.md
gets_you: "Your Entities exposed as MCP tools and resources, under the rules the rest of the app already has."
works_if: "The application has Entities and operations worth exposing, and a rule about who may use them."
costs: "Local transport adds nothing to operate. Remote transport is a public surface to secure like any API."
ingredients:
  - "one | MCP tools, resources, and transports | Sylin.Koan.Mcp"
  - "optional | human console over the same surface | Sylin.Koan.Mcp.Explorer"
  - "optional | operational tools for the running app | Sylin.Koan.Mcp.Operations"
  - "optional | authorization for the exposed operations | Sylin.Koan.Web.Auth"
---

# Let an outside agent use my application

One declaration turns an Entity into an agent-visible tool. There is no second domain model, no
mirrored service, and no hand-written tool handler.

## When this is the answer

"I want Claude to be able to check our inventory." "Let an assistant file tickets for us." "Expose this
to our internal agent."

The decisive question is **who is on the other end**, and it changes everything downstream:

- **A developer's own tooling on their machine** — a local transport, no public surface, low stakes.
- **A remote agent acting for a signed-in user** — a public surface with authentication, authorization,
  and audit, exactly like any other API. Nothing about MCP makes this lighter.
- **An autonomous agent acting for the business** — decide what it may change before exposing anything.

Then: **read or write?** Read-only exposure is a genuinely small, low-risk step and usually the right
first one. Write access is a security conversation, and the honest framing is "what could this do at
3am with nobody watching".

The rule that keeps this safe is simple to state and easy to skip: **the same authorization, tenant,
and lifecycle rules must govern MCP and HTTP.** If the agent surface reaches data by a different path,
it is a way around every check you wrote.

## Assembly

```powershell
dotnet add package Sylin.Koan.Mcp
```

```csharp
[McpEntity(Name = "Todo", Description = "Work the team intends to finish")]
public sealed class Todo : Entity<Todo> { }
```

The same model already exposed over HTTP becomes agent-visible; the governance travels with it.
`koan://facts`, `koan://entities`, and `koan://self` describe the application to whatever connects.

Depth: [agent-native how-to](../guides/mcp-agent-native-howto.md) ·
[MCP over HTTP/SSE](../guides/mcp-http-sse-howto.md).

## Prove it

1. **Behavior** — a client discovers the tools and an allowed read works.
2. **Composition** — assert the exposed toolset is exactly what you intended. The failure mode here is
   exposing more than you meant, which no happy-path test detects.
3. **Correction** — a denied action is denied *through MCP*, not only through HTTP; an unauthenticated
   caller on a remote transport gets nothing; audit evidence exists for actions taken.

Write the negative tests against the agent surface specifically. Passing HTTP tests prove nothing about
this one.

## Boundaries

- Exposure is not authorization. `[McpEntity]` says visible, not permitted.
- A local transport is not a security boundary; anything that can run the process can use it.
- Koan does not decide what an autonomous agent *should* do. Bounds are yours.

## Interacts with

**Authorization and tenancy.** Both must reach the agent surface, or it becomes the way around them.

**Human review.** Agent-initiated writes plus [review before it ships](review-ai-output.md) is the
shape most teams actually want before letting a model change records.

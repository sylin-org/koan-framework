---
type: PLAN
domain: framework
title: "AE-03 - Governed agent workflow"
audience: [maintainers, ai-agents]
status: current
last_updated: 2026-09-05
framework_version: v1.0.0
validation:
  status: reviewed
  scope: work specification; HTTP and MCP behavior proof pending
---

# AE-03 — Governed agent workflow

Read the [charter](../README.md); claim and report work in [PROGRESS](../PROGRESS.md).
Dependency: AE-01's policy and application boundary. This card owns the workflow experiment;
A03 consumes demonstrated scenes for the flagship recording.

## Outcome and existing evidence

A person submits a request, an outside agent accesses permitted information and performs a
permitted operation, and the application rejects an unauthorized attempt. HTTP and MCP use
the same business policy owner and caller/tenant boundaries.

Read [agent surfaces](../../../capabilities/agents.md), the
[MCP recipe](../../../recipes/let-an-agent-use-my-app.md), and
[access rules](../../../recipes/control-who-can-do-what.md). Inspect relevant existing
[MCP evaluation tasks](../../../../evals/agent-race/matrix/tasks/mcp-enforcement/README.md).

## Deliver and prove

1. Specify caller identities, roles, tenant memberships, exposed operations, and the expected
   allow/deny matrix. Define where authentication establishes that context for each transport.
2. Project the existing Entity model through the documented MCP surface. Own custom business
   actions explicitly; CRUD exposure does not itself implement an approval process.
3. Exercise the same policy through HTTP and actual MCP calls. Check permitted actions,
   forbidden discovery, direct forbidden invocation, and cross-tenant access to known ids.
4. Attempt to bypass the business action by directly changing the protected state. Prove that
   the underlying policy rejects the attempt and that denied calls leave state unchanged.
5. Capture a short reproducible user journey with the relevant redacted facts and outcomes.
   Keep ordinary user screens about their work; inspection evidence belongs in the review path.

## Acceptance and limits

- Actual calls establish consistent policy and tenant outcomes, not only a filtered tool list.
- Mutations obey domain rules; the caller context is not inferred from agent-supplied claims.
- The application has one policy owner and one domain model across the demonstrated surfaces.
- Transport, identity configuration, and any provider limits accompany the receipt.

Redirect if parity requires duplicated policy or a mirrored agent model. A project reference,
exposure attribute, or successful tool discovery alone cannot close this card. No generalized
agent autonomy or universal cross-transport guarantee follows from this bounded workflow.

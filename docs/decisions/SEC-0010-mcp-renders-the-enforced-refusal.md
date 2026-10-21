---
id: SEC-0010
slug: mcp-renders-the-enforced-refusal
domain: Security
status: Accepted
date: 2026-10-21
title: MCP renders the endpoint's refusal as a failed tool call
related:
  - ARCH-0092
  - SEC-0004
  - SEC-0005
  - AI-0014
---

# SEC-0010: MCP renders the endpoint's refusal as a failed tool call

## Context

REST and MCP call the same `EntityEndpointService`, and its gate returns one `AuthorizeDecision`. REST translated a
denial into a bare `401` or `403`. MCP did not translate it at all: `ResponseTranslator` always returned a successful
tool result, serialized the decision record into `meta.shortCircuit`, and fell back to the empty `Items` the service
carries beside a denial. A forbidden collection therefore read `isError=false`, content `"[]"`. The Forbid record's
`Reason` is the gate's description (`requires role in [admin]`), so any authenticated caller could learn which role a
walled tool needs by calling it, the privilege enumeration SEC-0005 keeps silent in the catalog. Every other refusal
the service returns (`400`, `404`, `409`, `413` for an unpaged read over the safety cap) also rendered as success with
an empty or null payload (ledger NK-053).

## Decision

**A refusal by the shared endpoint is a failed MCP tool call with a stable code, rendered from the same decision REST
renders.**

1. A denial (`EntityEndpointResult.DeniedDecision`) becomes `isError=true` with `meta.errorCode` `unauthenticated`
   (Challenge, status 401) or `forbidden` (Forbid, status 403). The decision record is never serialized.
   `meta.shortCircuit` carries `{ type, statusCode }`.
2. What would unlock the verb is disclosed only where the Door discloses it: a `[Door]` entity whose gate is not a role
   tier adds `needs` (from `McpEntityGate.DoorNeeds`, the same gate that enforces). Every other denial says only that
   the caller may not call the tool.
3. A short circuit whose action result carries a status of 400 or more becomes `isError=true` with `meta.errorCode`
   `not_found` (404), `unauthenticated` (401), `forbidden` (403) or `rejected` (anything else). The rendered body stays
   in `meta.shortCircuit`, and the text names its `message` or `error`.
4. A short circuit with a success status renders the action's own body as the payload, as REST does, never the empty
   `Items` or `Model` carried beside it.
5. `McpToolExecutionResult.Refused` is the factory for these results; the codes live in `CodeModeErrorCodes`.
6. The code-mode SDK's `getById` returns `null` for `not_found`; every other refusal throws, as failures already did.

## Compatibility

- Clients that read a denied or refused call as success now see `isError=true`. A denied collection no longer reads as
  `[]`, a missing `getById` no longer returns `null` content with `isError=false`, and a `413` no longer reads as `[]`.
- The Forbid reason no longer appears in MCP results. Clients that parsed `meta.shortCircuit.reason` lose it; a `[Door]`
  entity's non-role needs move to `meta.shortCircuit.needs`.
- Code-mode scripts that caught nothing on a denied read now get a thrown error instead of `{}` or an empty list.
- No public signature or stored format changes; `Refused` and four error-code constants are added.

## Guarantee and correction

- An MCP caller can tell "permitted and empty" from "denied" without parsing metadata, and cannot read a role gate out of
  a denial.
- The REST and MCP renderings of one decision agree on the status.

## Evidence

- `RefusalRenderingSpec` (Koan.Mcp.Conformance.Tests): a forbidden role-gated collection is `forbidden` with no role
  text; an anonymous call on a scope gate is `unauthenticated`; a `[Door]` scope gate discloses its needs and a `[Door]`
  role gate does not; a missing row is `not_found`; a refused `q` is `rejected` with its message. All six failed before
  this change.
- The Explorer suite's try-it success check had passed on a `400` (no active host while classes ran in parallel); the
  suite now runs serially like the other MCP suites.

---
id: SEC-0016
slug: each-mcp-http-request-is-authorized-as-itself
domain: Security
status: Accepted
date: 2026-10-21
title: Each MCP HTTP request is authorized as the principal it carries
related:
  - AI-0037
  - SEC-0004
  - SEC-0006
  - SEC-0015
---

# SEC-0016: Each MCP HTTP request is authorized as the principal it carries

## Context

`McpSessionManager.Create` stamped the caller's principal with its origin tier at `initialize` and stored it on the
session. The Streamable HTTP transport and the legacy SSE shim then dispatched every later request as that stored
principal. A refreshed token with fewer scopes or roles kept the old authority until the client re-initialized; a
session started from a declared internal network stayed `origin:internal` for requests sent from anywhere; and with
`RequireAuthentication=false` (the default on hosts that do not look deployed) the session id alone carried the
initializer's principal, which could be a cookie-authenticated user (ledger NK-119).

## Decision

**A session binds a subject; it does not carry authority.** Every non-`initialize` request is dispatched as
`McpSessionManager.RequestPrincipal(context)`: the credential that request carries (the bearer the edge authenticated,
or the default scheme's user), stamped with the origin tier of that request's connection. The SEC-0006 check that a
request comes from the session's subject is unchanged when authentication is required.

## Compatibility

- A token downgrade between requests takes effect on the next request. A client that rotates to a token with fewer
  scopes loses the extra authority without re-initializing.
- Origin is the current connection's tier, so a session that moves networks is evaluated where each request comes
  from.
- Without `RequireAuthentication`, a request that carries no credential runs anonymous even on a session a signed-in
  user initialized.
- `McpSessionManager.RequestPrincipal` is added; no signature changes.

## Evidence

- `StreamableAuthSpec.A_scope_downgrade_between_requests_is_honored` (Koan.Mcp.Streamable.IntegrationTests): a session
  initialized with `items:read items:write`, then a `tools/call` upsert carrying a token for the same subject with only
  `items:read`, is forbidden. Before this change the write succeeded.

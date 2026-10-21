---
id: SEC-0015
slug: origin-is-stamped-by-the-door-that-received-the-call
domain: Security
status: Accepted
date: 2026-10-21
title: The origin tier is stamped by the door that received the call
related:
  - SEC-0004
  - WEB-0072
  - SEC-0014
---

# SEC-0015: The origin tier is stamped by the door that received the call

## Context

`[Access(... "origin:local")]` gates read the `koan:origin` claim, which SEC-0004 makes framework-stamped: REST stamps
it from the connection, STDIO stamps `local`, an HTTP MCP session stamps `remote` or `internal`. `OriginStamp.IsStamped`
accepted the claim on any identity, and the non-HTTP request path kept an existing claim. The Explorer try-it executed
tools as the default-scheme `context.User` with no stamp, so a signed-in principal whose IdP or claims transformation
carried `koan:origin=local` satisfied an `origin:local` gate through try-it while REST refused it (ledger NK-121).

## Decision

**Only the identity `OriginStamp.Apply` creates carries a stamp, and every door stamps its own tier.**

1. `Apply` labels its carrier identity; `IsStamped` recognizes only that identity. A `koan:origin` claim on any other
   identity is caller-supplied, and the non-HTTP path replaces it with `remote`.
2. The Explorer try-it stamps the tier its HTTP connection resolves (`OriginResolver.FromHttpContext` with the
   configured trusted networks), as REST does, before executing.

## Compatibility

- A principal that reached an MCP or try-it call with an origin claim from its own sign-in is now `remote`; only STDIO
  is `local`, and only a declared trusted network is `internal`.
- Code that hand-built a principal with a bare `koan:origin` claim to simulate a stamp must call `OriginStamp.Apply`.
- No signature changes.

## Evidence

- `OriginBuilderTests.Mcp_path_replaces_an_origin_claim_on_the_callers_identity` (Koan.Web.Extensions.Tests) and
  `ExplorerConsoleSpec.Execute_stamps_the_connection_origin_over_a_carried_claim` (Koan.Mcp.Explorer.IntegrationTests):
  a carried `koan:origin=local` is not a stamp and does not open an `origin:local` entity through try-it. Both failed
  before this change.

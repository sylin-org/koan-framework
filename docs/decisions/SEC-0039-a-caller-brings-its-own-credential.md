---
id: SEC-0039
slug: a-caller-brings-its-own-credential
domain: Security
status: Accepted
date: 2026-11-02
title: A caller brings its own credential, and SEC-0037 is overturned
related:
  - SEC-0006
  - SEC-0016
  - SEC-0025
  - ARCH-0141
supersedes: SEC-0037
---

# SEC-0039: A caller brings its own credential, and SEC-0037 is overturned

## Context

[SEC-0037](SEC-0037-an-edge-that-is-part-of-the-application.md) rests on a premise that is wrong: that something acts on a
person's behalf, holding a token the application mints for it, and that the MCP edge should take that token. A person signs
in with their own credentials, and an agent with its own. Nothing in an application of Koan holds a credential for someone
else, so there is no token for the application to mint and no second audience for the edge to answer to.

## Decision

1. **SEC-0037 is overturned.** Its rules 1 to 5 are gone: `Koan:Mcp:AcceptApplicationBearer`, the authentication it
   implied, `AccessToken.For` and the record it answered, and the `params` names of `KoanBearerResource.Authenticate`,
   which is again one resource. What existed only for them goes with them: the flows' token mint and standing rule are
   theirs again, `SessionPrincipal` names a client again, and `AtprotoServiceAuth.SignIn` answers nothing.
2. **What stood on its own is kept.** These were SEC-0037's rules 6 to 9 and its fixes, and they are now this record's:
   - `initialize` answers a version the server speaks, from the one list the transport checks.
   - Outside Development a request's `Host` names no audience, at any door: `KoanWebBaseUrl.Audience` is the configured or
     accepted URL, and in Development the request's. Until one exists, the pages' resource edge refuses every bearer and the
     MCP edge answers 503 with the correction. An explicit `ResourceServer:Resource` or `ResourceUri` names its own.
   - A session cookie that no longer holds is a visitor on every route (`SessionCookieHandler`). A credential a caller
     presented stays rejected.
   - A token holds what was requested and what the person holds (SEC-0006 D6), in the code flow, its remembered path and the
     device flow, and the consent page shows that.
   - `IdentityAuthFlowHandler` rewrites `sub` with the person it settles a linked subject on, so a token from the flows names
     that person.
   - An empty array binds empty, `= null` parameters included; a JSON content type with no body has no fields; a body or a
     form past its limit answers 413 on both doors; a `GET` and a `HEAD` on one action are one tool; a tool call's refusals.

## Compatibility

- `Koan:Mcp:AcceptApplicationBearer` is no longer read. An application that set it sees the edge take its own audience
  alone, and authentication follows `RequireAuthentication`, which defaults by environment.
- `AccessToken` is removed from `Koan.Web.Auth.Server.Protocol`. A door that minted one has no replacement here: a caller
  signs in with its own credential.
- `KoanBearerResource.Authenticate(context, resource)` takes one resource again.
- `AtprotoServiceAuth.SignIn` returns `Task`.

## Evidence

- `ToolRequestSpec` (Koan.Mcp.Verbs.Tests): empty arrays on both doors, a JSON content type with no body, a form and a body
  past the limit, a `GET` and a `HEAD` as one tool. `StaleCookieWithTheEdgeSpec`, `StaleCookieWithoutTheEdgeSpec`,
  `UnaddressedPagesSpec` and `AddressedPagesSpec`: SEC-0037's specs of what is kept, on the same real host.
- `GrantedScopeSpec` (Koan.Web.Auth.Server.IntegrationTests): SEC-0037's specs of the scope clamp, on the routes.

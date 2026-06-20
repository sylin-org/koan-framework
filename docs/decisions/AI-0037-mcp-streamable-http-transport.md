# AI-0037 — MCP Streamable HTTP transport (break-and-rebuild)

- **Status**: Proposed
- **Date**: 2026-06-20
- **Supersedes**: AI-0013 (MCP HTTP+SSE deployment) — the transport surface
- **Relates**: AI-0012 (MCP JSON-RPC runtime), WEB-0072 (MCP Explorer console), SEC-0006 (embedded OAuth AS / the MCP ingress), AN6 (protocol currency)
- **Card**: `docs/assessment/prompts/07/AN-cards.md` · AN6

## Context

The Model Context Protocol **deprecated** its original 2024-11-05 "HTTP+SSE" transport (two endpoints: a long-lived `GET …/sse` channel that emits an `endpoint` event, plus `POST …/rpc` whose responses ride back over the SSE channel) in favour of **Streamable HTTP** (introduced 2025-03-26, refined 2025-06-18 — Koan's `DefaultProtocolVersion`). Koan ships only the deprecated transport (`GET /mcp/sse` + `POST /mcp/rpc`).

AN6's **OAuth 2.1 ingress** half is already delivered by **SEC-0006**: RFC 9728 protected-resource metadata (`/.well-known/oauth-protected-resource{baseRoute}`), RFC 8707 resource-indicator / audience enforcement (`McpEdgeAuth` + `McpResourceIdentity`), and PKCE (mandated by the embedded AS). So AN6's net-new core is **the transport** plus the `MCP-Protocol-Version` header.

The dispatch + SEC-0004 gate/visibility/principal logic lives **inside** `HttpSseRpcBridge`, coupled to `session.Enqueue(...)`. The SSE byte-writer (`Koan.Web.Sse` `SseFormatter`/`SseResults`) and `McpRpcHandler` are already transport-agnostic.

### The Streamable HTTP wire contract (verified against the 2025-06-18 spec + the TS/Python reference SDKs)

- **Single MCP endpoint** at `{baseRoute}` supporting **POST** and **GET**; **DELETE** optional.
- **POST** (the universal send path): client MUST `Accept: application/json, text/event-stream`.
  - Body is solely response(s)/notification(s) (no request) → **`202 Accepted`, no body**.
  - Body contains a request → server replies with **either** `Content-Type: text/event-stream` (open a per-request SSE stream that carries the response, then closes) **or** `Content-Type: application/json` (the single response object). The client supports both.
  - 2025-06-18 mandates a **single** JSON-RPC message per body (batching removed; 2025-03-26 allowed arrays).
- **GET**: opens the standalone server→client SSE stream (`Accept: text/event-stream`); **one per session** (`409` on a duplicate); MUST NOT carry request responses except on resume; a server offering no push stream answers **`405`**.
- **DELETE** (with `Mcp-Session-Id`): terminates the session → `200`; thereafter that id → `404`. `405` if the server forbids client termination.
- **Headers**: `Mcp-Session-Id` (server mints on the `initialize` result, client echoes on every later request; visible-ASCII 0x21–0x7E, CSPRNG-unique), `MCP-Protocol-Version` (client sends on **non-initialize** requests; invalid → `400`; absent → assume `2025-03-26`), `Last-Event-ID` (resumption `GET`), `Accept` (required; mismatch → `406`), `Origin` (server MUST validate — DNS-rebinding).
- **Status codes**: `202` (notifications/responses-only), `200` (request → JSON or SSE), `400` (missing session id `-32000`, invalid protocol version, not-initialised), `404` (unknown/terminated session `-32001` — the **re-initialise** signal), `405` (no GET stream / no DELETE), `409` (duplicate GET stream), `406` (Accept).
- **Resumability**: SSE `id:` per event (globally unique within the session, but a **per-stream cursor**); a resumption `GET` with `Last-Event-ID` replays **only that one stream's** tail.
- **`initialize` special-casing**: the first POST carries **no** session id (server mints it on the result); protocol version is negotiated in the JSON body (`params.protocolVersion` → echoed in `InitializeResult.protocolVersion`); the `MCP-Protocol-Version` header only starts **after** negotiation.

## Decision

### D1 — One core, not two transports (break-and-rebuild)

Rather than bolt a second HTTP transport beside the legacy one, **break and rebuild onto a single core** (the "less but more meaningful moving parts" rule):

- **`McpRpcDispatcher`** (new, transport-agnostic) — extract the JSON-RPC method switch (`initialize`/`tools/list`/`tools/call`/`resources/*`/`ping`) **and** the SEC-0004 gate/visibility/principal logic out of `HttpSseRpcBridge`. Signature: dispatch a `JsonRpcEnvelope` under a `ClaimsPrincipal` to an output sink (response + any interim server messages). This is the **single** home of the security-sensitive dispatch — the two HTTP surfaces and STDIO all call it, so they cannot drift (the SEC-0004 "one projection or it drifts" discipline applied to transports).
- **Unified session** — evolve `HttpSseSession` into the one session type: `Id`, `User` (bound at creation, `SamePrincipal`-checked, never null), per-stream outbound channels, a **session-scoped event-id sequence**, and a **bounded per-stream replay buffer**. `HttpSseSessionManager` → `IMcpSessionManager` (mint/resolve/terminate/idle-reclaim).
- **Streamable HTTP is the primary surface.** The legacy `/sse`+`/rpc` becomes a **thin deprecated shim** over the same dispatcher + session (opt-in). `SseFormatter`/`ServerSentEvent` (+ an `EventId`) are reused for both per-POST and GET streams.

### D2 — The single MCP endpoint (POST/GET/DELETE `{baseRoute}`)

Implement the wire contract above on the bare `{baseRoute}`. POST default response mode is **SSE-per-request** (streaming-capable + resumable — matches the reference SDK default and the "full streaming" decision); `application/json` single-response mode is available via `StreamableJsonResponse = true` for lean request/response deployments. Both are implemented; the client advertises support for both.

### D3 — Content negotiation + the Explorer seam (the `GET {baseRoute}` crux)

`POST`/`DELETE {baseRoute}` collide with nothing today. **`GET {baseRoute}` does** — the WEB-0072 Explorer console serves HTML there. Resolution (single-owner, no contributor race): **the core owns `GET/POST/DELETE {baseRoute}`**; the `GET` handler content-negotiates —

- `Accept: text/event-stream` → the server-push SSE stream (MCP client);
- `Accept: text/html` (and a renderer is registered) → delegate to **`IMcpConsoleRenderer`** (a new seam in `Koan.Mcp`); the WEB-0072 Explorer **implements** it instead of mapping its own `GET {baseRoute}`;
- otherwise → `405` with `Allow: GET, POST, DELETE`.

This keeps the console at bare `/mcp` (WEB-0072's discoverable human face) while giving the transport sole route ownership — and removes the two-contributors-racing-for-one-route fragility. The Explorer's sub-paths (`/map.json`, `/access-map.json`, `/explorer/*`) are unaffected.

### D4 — Sessions + resumability

`Mcp-Session-Id` minted at `initialize` (128-bit CSPRNG → 32-hex, ASCII-safe), echoed on the result and required on every later request; missing → `400 -32000`, unknown/terminated → `404 -32001` (re-init signal). Each stream carries a session-scoped, per-stream-ordered `id:`; a bounded replay buffer per stream backs `Last-Event-ID` resumption (replay **that stream only**). The session `User` is fixed at creation; a POST/GET under a session is `SamePrincipal`-checked (no session-id-as-bearer hijack — the SEC-0006 invariant, preserved).

### D5 — Protocol version + transport security

`MCP-Protocol-Version` validated on non-initialize requests (unsupported → `400`; absent → assume `2025-03-26`; `initialize` exempt; the JSON-layer version negotiation stays in `McpRpcHandler.Initialize`). `Origin` validated against `AllowedOrigins` (DNS-rebinding; enforced when configured, warned otherwise — mirrors the existing `RequireAuthentication` posture). Bearer auth unchanged (`McpEdgeAuth`, SEC-0006). Single message per POST (2025-06-18); legacy batch arrays tolerated read-only for back-compat with one-initialize-per-batch enforcement.

### D6 — Defaults / coexistence

- **`EnableStreamableHttpTransport`** — defaults **on when the HTTP transport is enabled** (Streamable is the modern default).
- **`EnableLegacySseTransport`** — **opt-in, default off, deprecated** (the `/sse`+`/rpc` shim, for clients that haven't migrated).
- `EnableHttpSseTransport` is retained as the master "HTTP transport on/off" switch (back-compat); when on, Streamable mounts by default and legacy is opt-in.

## Phases (each TDD + mutation-checked + ratchet-green, ARCH-0079 real-host conformance)

1. **Extract the core** — `McpRpcDispatcher` + unified session (event-id + replay buffer) + `IMcpSessionManager`; the legacy SSE transport rides the new core (behaviour-preserving refactor; existing MCP conformance suites stay green).
2. **Streamable HTTP transport** — POST/GET/DELETE `{baseRoute}`: content negotiation, `202`, sessions, resumability, `MCP-Protocol-Version`, `Origin`, the status-code matrix. Default-on.
3. **Explorer seam + legacy shim** — `IMcpConsoleRenderer` (Explorer plugs in; core owns `GET {baseRoute}`); legacy `/sse`+`/rpc` re-expressed as the deprecated shim; the new options/defaults.
4. **Converge + conform** — STDIO onto the core; a Streamable-HTTP round-trip test, a resumption test, a session-lifecycle test, the `/.well-known/oauth-protected-resource` + bearer-gated-call tests (AN6's TEST list); finalize the ADR + cards/skills/SURFACES.

## Consequences

- **Positive**: Koan speaks the current MCP transport (interoperates with up-to-date clients); one dispatch/session/SSE core instead of parallel transports (fewer concepts, no drift); resumability + sessions are first-class; the SEC-0004 gate logic is centralised; the WEB-0072 console route ownership is de-fragilised.
- **Negative / cost**: a security-sensitive break-and-rebuild of the transport edge (principal threading, session binding, replay buffers) that must be conformance- and adversarially-tested, not assumed; the Explorer's `GET {baseRoute}` mapping moves to a seam (a WEB-0072 touch).
- **Neutral**: the legacy transport survives one release as an opt-in deprecated shim; STDIO and the (placeholder) WebSocket transport converge onto the same core.

## Rejected alternatives

- **Two parallel HTTP transports** (legacy SSE + a separate Streamable impl) — rejected per the break-and-rebuild rule: doubles the dispatch/session surface and creates a drift seam.
- **Streamable GET stream on a sub-path** (e.g. `{baseRoute}/stream`) to dodge the Explorer collision — rejected: non-conformant (the client opens `GET` on the **same** endpoint URL it POSTs to).
- **Moving the Explorer console to `{baseRoute}/console`** — rejected: loses WEB-0072's bare-`/mcp` discoverable human face; the `IMcpConsoleRenderer` seam preserves it.
- **Stateless JSON-only v1** (no SSE-per-POST, no GET stream, no sessions) — rejected by the "full streaming now" decision.

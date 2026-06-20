# AI-0037 — MCP Streamable HTTP transport (break-and-rebuild)

- **Status**: Accepted (Phases 1–3 implemented + adversarially reviewed + green; Phase 4 = doc-surface follow-ups)
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

---

## Addendum (2026-06-20) — Phase 3 Architecture Evaluation: transport/session convergence

Before implementing Phase 3 the current MCP edge was mapped in full to find every duplicate or
near-duplicate path ("less but more meaningful parts"). Findings + decisions below; this addendum
governs Phase 3 and **revises** the Phase-4 "STDIO onto the core" item.

### Current state — 3 transports, 2 session models, 2 dispatch mechanisms, 2 SSE frame types

- **STDIO** (`StdioTransport`, default-on) → `McpServer.Run` → `StreamJsonRpcTransportDispatcher`
  (StreamJsonRpc **reflection** over `McpRpcHandler`). Deliberately **ungated** (AN3 local-trust: stdin/stdout
  is the same-machine process owner; no remote principal to gate).
- **Legacy HTTP+SSE** (`HttpSseTransport`, opt-in/deprecated) → 2 endpoints: `GET /sse` (open stream, emit
  `connected`+`endpoint` control frames) + `POST /rpc` (`HttpSseRpcBridge` → `McpRpcDispatcher` → enqueue the
  response on the GET stream). Session = `HttpSseSession`/`HttpSseSessionManager` (request-linked CTS, health
  publish, heartbeat broadcast, a single `ServerSentEvent` channel). Session-id header = `X-Mcp-Session`.
- **Streamable HTTP** (`StreamableHttpTransport`, Phase 2b) → single endpoint `{baseRoute}` POST/GET/DELETE.
  Session = `McpSession`/`McpSessionManager` (session-scoped CTS, multi-stream, replay buffers). Dispatch =
  `McpRpcDispatcher`. Session-id header = `Mcp-Session-Id`.

### Duplications, ranked

| # | Duplication | Similarity | Collapse value | Risk | Decision |
|---|---|---|---|---|---|
| A | `HttpSseSession`/Manager **vs** `McpSession`/Manager | HIGH | HIGH | Med (legacy e2e) | **Collapse** |
| B | `ServerSentEvent` **vs** `SseEnvelope`/`McpSseStream` | MED | MED | Low | **Collapse (delete)** |
| C | Bare `GET {baseRoute}` owner — Explorer **vs** Streamable | COLLISION | required | Low | **Seam (core owns it)** |
| D | `McpRpcDispatcher` switch **vs** StreamJsonRpc reflection (STDIO) | LOW (justified) | LOW | High | **Keep separate** |

### Decisions

**D-C — route ownership (the Phase 2b default-off blocker).** Koan.Mcp core becomes the **sole owner** of
`GET {baseRoute}`. New seam **`IMcpConsoleRenderer`** (in Koan.Mcp). The core GET handler content-negotiates:
`Accept: text/event-stream` (+ Streamable on) → the SSE stream; browser `text/html` / `?format=html` (+ a
renderer registered) → `renderer.RenderConsoleAsync`; else 404. The WEB-0072 Explorer **drops** its bare-GET
mapping, **implements** `IMcpConsoleRenderer`, and keeps its sub-paths (`/map.json`, `/access-map.json`,
`/explorer/*`). `MapKoanMcpEndpoints` maps the bare GET when (Streamable enabled **or** a renderer is
registered) and no longer early-returns when only the Explorer is active. → **one** route owner, **one**
console path. The `AcceptsHtml` negotiation moves from the Explorer into the core handler.

**D-A + D-B — session collapse.** The legacy HTTP+SSE transport becomes a thin **deprecated shim** over the
unified `McpSession`/`McpSessionManager`/`McpRpcDispatcher`. `GET /sse` mints/opens the unified session's GET
stream, emits the legacy `connected`+`endpoint` frames, streams it, and **terminates the session when the
connection drops** (legacy request-scoped lifetime). `POST /rpc` resolves the session, dispatches inline
through `McpRpcDispatcher`, and enqueues the response on that session's GET stream (no background pump — the
per-session serial `HttpSseRpcBridge` thread is **deleted**; inline dispatch is correct because the stream's
channel is thread-safe). `ServerSentEvent`'s control-frame factories are inlined as `SseEnvelope`s.
**Deleted:** `HttpSseSession`, `HttpSseSessionManager`, `HttpSseRpcBridge`, `ServerSentEvent` (4 files). The
cross-cutting concerns the legacy manager owned (health publish via `IHealthAggregator`, idle reclaim, optional
keep-alive) **move up** into the unified `McpSessionManager` — extending self-reporting to Streamable too
(which currently has neither). Legacy wire (`X-Mcp-Session`, the `connected`/`endpoint`/`ack`/`end` events) is
**byte-preserved** so the existing legacy e2e stays green. Blast radius is contained to `HttpSseTransport.cs` +
DI + two doc-comments; `HttpSseCapabilityReporter` is registry-only (untouched).

**D-D — STDIO stays on StreamJsonRpc (revises Phase 4).** The ADR's Phase-4 "STDIO onto the core" is
**withdrawn.** StreamJsonRpc is *justified divergence*, not duplication: (a) it provides framing
(`NewLineDelimitedMessageHandler`), request/response correlation, and notification plumbing that
`McpRpcDispatcher` does not; (b) STDIO is deliberately **ungated** (AN3 local-trust) whereas `McpRpcDispatcher`
is gating-centric — routing STDIO through it would either misapply remote gating or require a "local-trust
bypass" branch that re-introduces drift; (c) STDIO is the **default** transport — a rewrite risks everything
for no dedup win. **The shared core is already `McpRpcHandler`**; STDIO (reflection) and HTTP (explicit switch +
gating) are two thin adapters over that one core. That is the correct shape — not a duplication to collapse.

### Net effect
Session models 2 → 1 · SSE frame types 2 → 1 · bare-GET owners 2 → 1 · HTTP dispatch already 1 (now serves
Streamable + the legacy shim) · **−3 files net** (4 deleted, 1 added: `IMcpConsoleRenderer`) · health +
keep-alive now cover **both** HTTP transports. STDIO untouched (correctly).

### Sub-phases
- **3a** — `IMcpConsoleRenderer` seam + core owns `GET {baseRoute}` + Explorer becomes the renderer + flip
  `EnableStreamableHttpTransport` default on-when-HTTP.
- **3b** — legacy `/sse`+`/rpc` collapsed onto the unified session/dispatch; delete the 4 legacy files; lift
  health/keep-alive into `McpSessionManager`.

### Revisions after adversarial review (2026-06-20)

A 3-lens adversarial review (verified against source) found the addendum above understated work and
leaned on a safety net that does not exist. These revisions are **binding** and supersede the conflicting
prose above. The shape (D-A/D-B/D-C collapse; D-D keep STDIO) holds and was independently re-confirmed.

1. **No legacy wire e2e exists — "byte-preserved, stays green" is withdrawn.** The only specs touching
   `GET /mcp/sse` (`McpAuthRampSpec`, `McpConfiguredResourceSpec`) assert auth/401/`no_entities` only; none read
   the `connected/endpoint/ack/message/end` frames, the `X-Mcp-Session` header, or POST `/rpc`. **Precondition
   Ph3-pre:** land a NET-NEW real-Kestrel legacy wire conformance spec against the **current** transport
   (golden bytes) BEFORE deleting anything, then re-run it against the shim.
2. **Console must NOT inherit the group's bearer-auth/CORS.** The Explorer console is mapped today on the ROOT
   builder (outside the auth group) and is anonymous-discoverable (WEB-0072). The core's bare `GET {baseRoute}`
   is therefore mapped **outside** the auth group; the handler does conditional auth **inline** — the
   `text/event-stream` (stream) branch runs `McpEdgeAuth.EnsureAuthorized` when `RequireAuthentication`; the
   `text/html` (console) branch is anonymous. POST + DELETE stay inside the auth group (always gated).
3. **Legacy frames — incl. the JSON-RPC response — emit via `EnqueueRaw`, not `EnqueueMessage`.**
   `EnqueueMessage` unconditionally stamps `id:{stream}.{seq}` + `event: message`; the legacy wire has no `id:`
   line. The shim hand-builds every `SseEnvelope` (`connected`/`endpoint`/`ack`/`end` + the `message` response)
   to reproduce the exact legacy bytes. The Ph3-pre spec asserts the **absence** of `id:` on the legacy message.
4. **Inline dispatch — ordering contract (option b, documented).** Deleting the serial `HttpSseRpcBridge` pump
   means concurrent POSTs to one legacy session enqueue responses in completion order, not submission order.
   **Decision:** the deprecated shim no longer guarantees submission-order responses — JSON-RPC `id` correlates
   responses (spec-compliant). Pinned by an order-independent pipelined-requests test. (No per-session
   serializer is re-added — that would re-grow the part we are deleting.)
5. **GET fallthrough status matrix (resolves the 404/405/406 contradiction).** `?format=html` OR (browser
   `text/html`, no `event-stream`) → console: renderer present → **200**, else **404**. `Accept:
   text/event-stream` → stream: Streamable on → [auth then] stream, Streamable off → **405** (stream not
   offered). Else → **404**. Port the Explorer's exact precedence (`?format=` wins; the 3-way `AcceptsHtml`
   rule) + `Vary: Accept` + `Cache-Control: no-store` on the rendered response.
6. **Atomicity.** The Explorer dropping its bare-GET mapping and the core registering the renderer-aware GET are
   ONE change; the `EnableStreamableHttpTransport` default-flip is gated behind it. New co-enabled integration
   test (Streamable-on + Explorer-on) asserts exactly one `GET /mcp` resolves and client/browser route correctly
   (no `AmbiguousMatchException`).
7. **Shim session lifetime.** The shim session **dies with its GET `/sse` stream** (explicit
   `_sessions.Terminate(id)` in the GET handler's `finally`). POST `/rpc` dispatches under its **own**
   `RequestAborted`, so a GET drop cannot kill an in-flight POST; a post-drop enqueue onto the completed GET
   stream is a no-op (`TryWrite` → false). Tested: GET-drop → session immediately 404; in-flight POST survives.
8. **Health/keep-alive lift is NET-NEW, transport-shaped (not "preserved").** Idle-reclaim already lives in
   `McpSessionManager`. Health-publish lifts up under a transport-neutral component id (`mcp-http`, not the old
   `mcp-http-sse`). Keep-alive becomes a raw **SSE comment** pushed on the sweep tick to each **open GET stream**
   (conformant for BOTH transports), replacing the legacy named `heartbeat` event (documented wire change;
   guarded for "no GET stream open"). Ph3-pre uses a long keep-alive interval so it does not perturb the golden
   frame sequence.
9. **Blast radius (corrected).** Relocate `HttpSseHeaders.SessionId` (the `X-Mcp-Session` constant the shim +
   CORS still need) out of the deleted `ServerSentEvent.cs`; fix the stale `<see cref="HttpSseRpcBridge"/>` in
   `McpToolAccessPolicy.cs`; rewrite the `McpPillarBootstrapSpec` remarks (hosted services = `StdioTransport` +
   `McpSessionManager`; drop the never-registered `WebSocketTransport`).
10. **D-D guard.** Keep `EnforcementConsolidationSpec`'s raw-handler assertion; STDIO never routes through the
    gating `McpRpcDispatcher` (re-confirmed correct — no change).

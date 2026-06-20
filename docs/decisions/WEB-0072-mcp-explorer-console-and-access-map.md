# WEB-0072 — The MCP Explorer Console + Capability Access Map (the human face of the agent surface)

- **Status:** Accepted — implemented (P1–P4) in `Koan.Mcp.Explorer` + the MCP `initialize` handshake in `Koan.Mcp`. One follow-on open: the interactive "play the device" grant exerciser (see §Phased build P3 + §Follow-on).
- **Date:** 2026-06-20
- **Deciders:** framework architect
- **Related:** [ARCH-0092](ARCH-0092-entity-exposure-surfaces.md) (entity exposure / `EntityToolset` / `[McpEntity]`), [SEC-0004](SEC-0004-capability-authorization-gate-constrain-project.md) (gate·constrain·project), [SEC-0005](SEC-0005-governed-agent-access-grants-audit-door.md) (grants·audit·**door**), [SEC-0006](SEC-0006-embedded-oauth-authorization-server.md) (OAuth AS — auth-code+PKCE, device, refresh, dev-token), [WEB-0068](WEB-0068-query-options-predicates.md) (read-path predicates), [WEB-0069](WEB-0069-web-pipeline-contributors.md) (single `UseEndpoints` contributors), [AI-0012](AI-0012-mcp-jsonrpc-runtime.md) (MCP runtime), [AI-0013](AI-0013-mcp-http-sse-deployment.md) (HTTP+SSE). Agent-native cards AN8 (`koan://self`), Wall/Door/Verb.

## Context

Koan MCP servers expose entities + verbs to agents over `tools/list`/`tools/call`, gated by the SEC-0004/0005 **gate·constrain·project·origin·grant** chain and reachable, since [SEC-0006](SEC-0006-embedded-oauth-authorization-server.md), via a standard OAuth on-ramp. What is missing is a **human face**: a way for a developer (or a teammate, or an auditor) to *see* what the server exposes, *try* a tool, *connect* a client, *understand* the authorization surface, and *verify* the auth flows — without standing up Claude Desktop or reading source.

The generic [MCP Inspector](https://github.com/modelcontextprotocol/inspector) is a separate app that connects *as a client*: it cannot run as the first-party cookie user, cannot show Koan's authorization honesty (the `can:[]` projection, the Door disclosure), and is one more thing to install. Koan can do materially better because it **owns the server**: the in-process executor, the per-principal projection, the compiled gate, and the descriptive metadata are all already in process.

A guiding question framed the whole design — *"is there an OpenMCP, an OpenAPI-for-MCP we should emit and drive the UI from?"* The grounded answer is **no, and we don't want one**: MCP is **self-describing at runtime** (`tools/list` returns each tool's `name`/`description`/`inputSchema` — literally JSON Schema; the protocol is the spec). REST needs OpenAPI because HTTP is blind; MCP is not. `server.json` (the official registry) is install metadata only; `.well-known/mcp` Server Cards are emerging-but-unratified; the project literally named "OpenMCP" is the *reverse* direction (OpenAPI → MCP). So the console drives off **live introspection**, never a static artifact — which is also the lowest-surface, highest-fidelity, drift-proof option.

This ADR captures the design as a coherent whole. It is **renderer-over-existing-surfaces** (~70%): the schema, the in-process executor, the per-principal projection, the compiled gate, and the app/tool/parameter descriptive metadata already exist. The new work is a thin web layer + a static SPA + a JSON-Schema form renderer.

## Decision

Ship a framework-served, Reference = Intent **MCP Explorer Console** — a Swagger-like human surface for any Koan MCP app — plus a **Capability Access Map** (the governance view) and a standing **per-caller surface endpoint**. It renders the *same* in-process projection the machine clients consume (**declare once, render every face**), executes tools **in-process as the cookie user** (a real try-it-out, no proxy), shows authorization **honestly** (Verb / Door / Wall), and matches the existing test-provider visual signature.

### The four faces of one compiled surface

The console is not a new authority or a second source of truth. One in-process catalog + one compiled gate are **projected** for four audiences:

| Face | Audience | What it shows | Disclosure |
|---|---|---|---|
| **`tools/list`** (machine) | an MCP client | callable tools, per caller | per-caller redacted (walls silent) |
| **Console describe** (human) | a developer / teammate | callable **Verbs** + disclosed **Doors**, walls silent | per-caller redacted |
| **Per-caller surface** (`/mcp/map.json`) | tooling / a human, one GET | the same as describe, machine-readable + enriched | per-caller redacted — **anonymous-safe by construction** |
| **Access map** (governance) | an operator / auditor / dev | **every** capability → its **full** requirement, walls **included** | un-redacted — **privileged** |

The first three are redacted per caller (disclosure-as-needed); the fourth is the un-redacted ground truth and is therefore **privileged**. All four derive from the same `EntityProjection.Visible` / compiled-gate surfaces, so none can drift from enforcement.

### The load-bearing decisions

**D1 — Live introspection is the single source of truth; no static "OpenMCP" descriptor backs the console.**
The console, the per-caller surface, and the access map are all generated at request time from the in-process catalog (`McpEntityRegistry.Registrations` + `McpCustomToolRegistry.Tools`), the schema (`SchemaBuilder` → `McpToolDefinition.InputSchema`), the projection (`EntityProjection.Visible`), and the compiled gate (`AccessGate` / `AccessGateEvaluator`). No emit/regen step; cannot drift; picks up `notifications/tools/list_changed` for live refresh; inherits MCP's mandatory JSON Schema to render forms for free. A static descriptor is emitted **only** as a secondary, derived, external-consumer artifact (registry `server.json`; a generated OpenAPI/AsyncAPI for a codegen/governance chain) — never the console's backing store, never hand-authored.

**D2 — Same-URL disambiguation via content negotiation (the GraphiQL pattern), serving HTML to a browser without ever intercepting a client.**
Serve the explorer **iff** `method == GET` **and** `Accept` contains `text/html` **and** `Accept` does **not** contain `text/event-stream` **and** does **not** contain `application/json`; with an explicit `?format=html|json` override taking precedence both ways, and `Vary: Accept` on the HTML. Parse `Accept` as a token set, never substring; **never** branch on `User-Agent`. This is safe **by contract**, not heuristic: the MCP spec *requires* a client to advertise `text/event-stream` (POST lists it alongside `application/json`; the GET server-stream lists it alone) and a browser navigation never does; `POST` is never browser navigation, so the entire JSON-RPC path bypasses the branch by method alone. The bare `GET {baseRoute}` (`/mcp`) is **unclaimed (404) today** — `MapKoanMcpEndpoints` only maps child routes under `MapGroup(baseRoute)` — so the explorer collides with no current client (which use `/mcp/sse` + `/mcp/rpc`), and the predicate keeps it forward-compatible if the edge later adopts single-endpoint Streamable-HTTP. *(Placement is the one open fork — see Open questions.)*

**D3 — The console renders the three-state projection (Verb / Door / Wall), not bare `tools/list`; describe is anonymous-readable, execute is authenticated.**
A generic client only ever receives `tools/list` (callable-only), so it *structurally cannot* show "you could do more if you signed in." The console reads the richer `EntityProjection.Visible(caller)` (`src/Koan.Mcp/Resources/EntityProjection.cs:22`) — **Verbs** (callable now), **Doors** (denied but `[Door]`-disclosed, with their `needs`), **Walls** (denied + silent: role-gated or un-`[Door]`-ed, omitted at `:43`). The **describe** surface is **anonymous-readable** (it self-projects to the anonymous face: public verbs + sign-in doors, walls silent — exactly what an anonymous client already sees over the wire); only **execute** requires a session. It must pass a **real anonymous `ClaimsPrincipal`, not `null`** — `McpEntityGate.DoorNeeds` treats `null` as STDIO local-trust (everything allowed, doors moot, `McpEntityGate.cs:57`), which would silently drop the disclosure. **Honesty invariant:** every face is generated from this one projection path, so "shows exactly what the client sees" is literally true, not approximately.

**D4 — Three jobs (Describe · Try · Connect) plus a per-grant conformance console; try-it executes in-process as the cookie user; device flow is an *exerciser*, not the login UX.**
- **Try** runs the tool **in-process** via `EndpointToolExecutor.Execute(name, args, ct, HttpContext.User)` (`src/Koan.Mcp/Execution/EndpointToolExecutor.cs:39`) — as the cookie principal, threaded to `EntityRequestContext.User`, **bypassing the bearer `/mcp` edge entirely**. No token juggling, no proxy: the try-it-out *actually runs* (a differentiator over every OpenAPI-bridge approach, which needs a separate REST surface). Denials ride back as `McpToolExecutionResult.shortCircuit`, not HTTP 403 — the page interprets result shape, not status codes, and renders the Door `needs` as an actionable affordance.
- **Connect** shows the connection URL + copy + "Add to …" deep-links; the **client** drives its own OAuth (do **not** route a real desktop client through a device-code detour).
- **Conformance console** lets a human *exercise* each SEC-0006 grant: an **auth-code + PKCE** exerciser (popup), and a **device-flow exerciser** where the page plays the device — `POST /oauth/device`, render the `user_code` + `verification_uri_complete`, the human consents in another tab (the existing consent seam), the page polls on the **real `interval`/`slow_down`** until the token resolves, then calls a tool with it. The device-flow exerciser is **safe even in production** (it cannot mint without genuine consent — it is a friendly UI over the public `/oauth/device`), unlike `/oauth/dev-token` (mints from the cookie with no consent → stays hard dev-gated). Label it "Simulate a headless client (device flow)" so the test harness is never mistaken for the recommended connection path.

**D5 — Two access-surface artifacts with opposite disclosure postures; the god-view is privileged and derives from `Describe()` (not the redacted `DoorNeeds`).**
*"This tool requires `role:admin`"* shows **more than any client sees** — it is the privileged god-view, not "what the client sees." Conflating them leaks the privilege map the Wall exists to hide. So:
- **Per-caller surface** — a standing `GET {baseRoute}/map.json` (e.g. `/mcp/map.json`), **anonymous-safe by construction** (it discloses nothing the protocol does not already hand that caller), generated from `EntityProjection.Visible(caller)`, enriched with the descriptive metadata (D6). It is the machine-readable twin of the describe view and a self-documenting surface every Koan MCP app exposes at a predictable URL (a natural early home for the emerging `.well-known/mcp` Server Card).
- **Access map (god-view)** — caller-**independent**, **un-redacted** (every capability → its full requirement, role/admin walls included), **privileged** (admin-gated and/or dev-only). Derived from the same compiled gates the floor enforces via `AccessGateEvaluator.Describe(actionGate)` (`src/Koan.Web/Authorization/AccessGateEvaluator.cs:112`) — the **un-redacted** renderer, explicitly **not** `McpEntityGate.DoorNeeds` (which returns `null` for role gates to keep walls secret). It walks both authz paths — entity `[Access]` DNF gates (per-action read/write/remove, via `IAccessGateCache.GetOrCompile` + `McpEntityGate.ActionFor`) and custom `[McpTool]` `RequiredScopes` + `RequireAuthentication` — and **normalizes them into one requirement vocabulary** (`anonymous` / `authenticated` / `scope:x` / `role:admin` / `origin:local` / `owner`, combinations). `RequiresRole` flags walls; `origin:` renders from the `koan:origin` claim grant; an open gate (`AnyOf.Count == 0`, allow-by-default per SEC-0004) renders explicitly as `anonymous`.

Because both artifacts derive from the *same gate the floor enforces*, they **cannot drift** — which is what makes the access map trustworthy as an audit artifact rather than documentation that lies. Exposure: the **runtime** views (admin tab + dev view) are primary, with an **on-demand "Download" button** producing the file when one is wanted (replacing a checked-in CI artifact, which described the *code* not the *running config*); a CI-emitted static map is **optional**, only if a governance pipeline asks. *(Map shape — two paths vs one `?full` — is an open fork; see Open questions.)*

**D6 — Descriptive metadata: decorate once in the most natural place, project to every face; close the app-level gap (`serverInfo` + `instructions`).**
Every human-facing label is declared once and read by both the live client surface and the console/map. The decoration surface already largely exists:
- **App** — `[KoanApp(Name, Code, Description, ContactEmail, SupportUrl, Tags)]` (or `Koan:Application:*`), today projected into `koan://self`.
- **Entity / tool** — `[McpEntity(Name, Description, ToolPrefix, Exposure)]`; per-operation `[ToolDescription(op)]` / `[ToolHidden(op)]` on an `EntityToolset`; `[McpTool(Name, Description, IsMutation)]` for custom verbs; mechanical `readOnly`/`destructive`/`idempotent` hints (opt-in markers for custom).
- **Parameter** — a fallback chain the developer mostly already populates: `[McpDescription]` → `[Display(Description=)]` → `[Description]` → property name (`src/Koan.Mcp/Schema/SchemaBuilder.cs`); `[Required]`, `[McpIgnore]`.

**Prefer inference over new attributes** (fewer, more meaningful pieces): harvest the existing `[Display]`/`[Description]` chain, and add **XML `/// <summary>` doc-comment harvesting** as a further fallback (the most natural place descriptions live). Gaps to close, in priority order: **wire `[KoanApp]` → the MCP-native `serverInfo` (name/version/title) + `instructions`** — the latter is the free-text `initialize` field the LLM reads (effectively the system prompt of the MCP surface); the app description exists but reaches clients today only via the `koan://self` resource, not the channels every client surfaces automatically *(and whether a formal `initialize` handler populates `serverInfo` at all is a small spec-conformance item to confirm)*; **custom-`[McpTool]` parameter descriptions** (a real hole — entity params have the chain, custom verbs have nothing); then per-tool **examples** and **output-schema** docs. A delight that falls out: a **"what the LLM is told"** console panel rendering (and letting you tune) the live `instructions` string.

**D7 — Activation, visual signature, and where it lives.**
Reference = Intent: a `KoanAutoRegistrar` + endpoint contributor (WEB-0069's single `UseEndpoints`), **dev-default-on / prod opt-in** (the Swagger-UI posture). The visual signature **matches `testprovider-login.html`** (`src/Connectors/Web/Auth/Test/wwwroot/`): `bg-slate-950` page, `slate-900` header/inputs, `slate-800` borders, a `from-purple-500 to-pink-500` gradient logo tile + gradient wordmark, Font Awesome glyph, `rounded-2xl` `.card` with `shadow-2xl`, `purple-600` primary CTA, and the `"Powered by Koan · MCP"` footer. **Vendor Tailwind + Font Awesome as static assets** (the test-provider page depends on CDNs and will not render offline/air-gapped; a prod console must), keeping the look byte-identical. There is **no shared dev-surface shell today** (the test-provider page is bespoke; the SEC-0006 consent page is app-owned JSON) — so **copy the chrome now** and extract a `{{title}}/{{icon}}/{{body}}/{{surface}}` shell **only if** a genuine third framework page lands; do not block the console on that extraction, and do not pull the app-owned consent page into framework rendering.

### Delight invariants (load-bearing requirements, not polish to cut)

Delight here is the gap between what a developer braces for and what they get — **work removed + doubt removed**. These are requirements:

1. **Reference = Intent appearance** — it must *be there* at the URL with no config (dev-default-on). Any required wiring murders the minute-one magic.
2. **Real in-process try-it-out** — the Run button must actually execute as the cookie user. A doc-only / proxy-required button is a broken promise.
3. **Provable non-drift** — the map and console derive from the same compiled gate the floor enforces; never a parallel computation.
4. **Honest in-line denials** — a denied verb shows its Door `needs` as an actionable hint, never a bare 403 or unexplained silence (walls excepted, by design).

## Build feasibility (~70% renderer, ~30% new)

**Already exists** — per-tool JSON Schema (`SchemaBuilder` → `McpToolDefinition.InputSchema`); in-process execution as the cookie user (`EndpointToolExecutor.Execute` / `McpRpcHandler.CallToolFor`, both take an explicit `ClaimsPrincipal`); per-principal Verb/Door/Wall filtering (`EntityProjection.Visible` + `McpEntityGate.CoarseAllows`/`DoorNeeds`); the un-redacted gate renderer (`AccessGateEvaluator.Describe`); app/entity/tool/param descriptive metadata; the Reference = Intent + static-asset-serving + dev-gate serving pattern (`Koan.Web.Admin`, the test connector).

**New** — the static SPA page(s); a cookie-`[Authorize]` controller exposing JSON endpoints (per-caller catalog+schema `GET`, execute `POST`, `map.json`, the privileged god-view behind its gate); a **client-side JSON-Schema → form** renderer (the one genuinely-absent capability — Koan ships none; lean on `@rjsf`/JSONForms or a small hand-rolled renderer, since `SchemaBuilder` output is clean); make `EntityProjection.Visible` public (it is `internal`) or re-walk via the public `McpEntityGate`; interpret `McpToolExecutionResult`; the `[KoanApp]` → `serverInfo`/`instructions` wiring; custom-tool parameter descriptions. No new auth, schema, execution, or authz machinery is invented.

## Rejected alternatives

1. **A static OpenMCP/OpenAPI descriptor as the console's backing store.** MCP is self-describing; a static file is a second source of truth that drifts. OpenAPI/AsyncAPI projection kept only as an *optional derived* artifact for external tooling (AsyncAPI is the better semantic fit for the duplex/notification protocol; OpenAPI for the per-tool call shape — but neither is needed for the console).
2. **Bending Swagger UI (the `mcpo` / .NET-SDK pattern: MCP tools → synthetic `POST /tools/{name}` → stock Swagger UI).** Proven, but it flattens to fake REST paths, cannot represent the duplex/notification half, and its "try it out" needs a proxy that serves those paths. Koan owns the executor, so a thin custom console executes for real and shows the authorization honesty Swagger UI has no vocabulary for.
3. **Device flow as the page's login UX.** RFC 8628 is for browserless/input-constrained clients; the MCP spec mandates auth-code + PKCE with a browser redirect and never mentions device grant. Kept only as a *grant exerciser* (D4) and for genuinely headless clients at the consent seam.
4. **An anonymous god-view access map** (or a single `/mcp/map.json` that flips to the god-view by query param). Publishing the un-redacted map enumerates every privilege the Wall hides. The god-view is privileged; two distinct routes are preferred over one scaled endpoint to make a leak hard to misconfigure.
5. **A CI-emitted static map as the primary governance store.** It describes the code, not the running config; runtime generation always matches the live deployment, with on-demand download covering the offline-audit need.
6. **A separate human app (MCP Inspector model).** Legitimate, but it cannot run as the first-party cookie user, cannot show `can:[]`/Door, and is another install. Koan owns the server; the in-endpoint console is the higher-fidelity, lower-friction path.

## Consequences

- **Positive.** One honest human surface for every Koan MCP app, Reference = Intent. The console *is* the live demo of the gate·constrain·project·door model (no generic inspector shows authorization honestly). A real, proxy-less try-it-out. An auditable, drift-proof authorization surface (the access map). Onboarding-by-URL. No second source of truth to keep in sync. The descriptive metadata finally reaches the channels clients read (`serverInfo`/`instructions`).
- **Negative / risks.** A new web surface to secure — the negotiation predicate (D2) must be exact (a wrong `Accept` rule could intercept a client; mitigated by the POST-never-HTML + event-stream-absent contract), and the **god-view must never leak** (D5 — privileged route + gate, not a query flag). A client-side JSON-Schema form-library dependency enters the asset bundle. Vendored Tailwind/FA add bytes (accepted for offline correctness). The describe endpoint must pass a real anonymous principal, not `null` (D3), or the disclosure silently disappears.
- **Security posture.** Describe + per-caller `map.json`: anonymous-safe by construction (no disclosure the protocol doesn't already make). Execute + grant-exercisers: authenticated (device-exerciser safe even in prod; dev-token stays dev-only). Access map god-view: admin/dev only, never anonymous. Origin and gates unchanged — the console is identity-and-rendering; the existing chain is the authority.

## Phased build

1. **Per-caller console (P1).** Describe (anonymous-readable Verb/Door/Wall projection) + Try (in-process execution as the cookie user) + the standing `GET /mcp/map.json`; Reference = Intent activation + the test-provider visual signature (vendored assets). Establishes the renderer-over-projection spine and the delight invariants.
2. **Connect + descriptive wiring (P2).** Connection URL + copy + deep-links; `[KoanApp]` → `serverInfo` + `instructions` (+ confirm/sort the `initialize` handler); custom-`[McpTool]` parameter descriptions; the "what the LLM is told" panel.
3. **Conformance console (P3).** The grant exercisers — auth-code + PKCE, device-flow (page-plays-device, real interval), refresh — the interactive twin of the SEC-0006 integration suite.
4. **Access map god-view (P4).** The privileged admin/dev tab + on-demand Download, derived via `Describe()` over both authz paths, normalized to one requirement vocabulary.

## Open questions (forks not yet pinned)

1. **Console placement** — bare `GET /mcp` via content negotiation (recommended; the single-URL vision, safe per D2) **vs** a sibling `/mcp/explorer` path (lower-risk, blunter).
2. **`map.json` shape** — two distinct routes (recommended: public `/mcp/map.json` + a separately-gated god-view route) **vs** one path with a `?full` flag behind an admin gate (fewer routes, dogfoods the model, but easier to misconfigure into a leak).
3. **`instructions` wiring timing** — first cut (P1) **vs** fast-follow (P2). Listed in P2 above; promotable to P1 if the app-level face matters early.
4. **Access map primary audience** — settled toward **operator/dev-first** (runtime console tab + on-demand download), with a CI artifact only on demand; revisit if a governance pipeline becomes the primary consumer.

## Follow-on — the interactive "play the device" grant exerciser

P3 shipped the dev-token quick-mint + a hand-off to a real client. The interactive device-flow exerciser (the
console *plays the headless device*, you consent in another tab, it polls to a token) was **deliberately deferred**
after verifying against the real SEC-0006 contract: the device flow needs a **registered `OAuthClient`** + `scope`
+ `resource`, and SEC-0006 DCR (D5) is **loopback-only** — but the console SPA is same-origin, so it cannot
self-register via the loopback rule. The design hinge (greenlit) is to **auto-register a well-known dev OAuth
client on boot in Development** (forced-public, a known id), so the console can drive the device flow without a
manual registration step — mirroring how dev OAuth tooling pre-provisions a client. The exerciser then runs the
real grant (device → poll on the true interval → consent at `/me/connect` → token), with the AS wired into a
dedicated Explorer test fixture to integration-test the mechanics. To be specified in a WEB-0072/SEC-0006 addendum
or its own ADR before building (no guessed OAuth UI — validate against the live AS).

## References

- [SEC-0006](SEC-0006-embedded-oauth-authorization-server.md) (the OAuth on-ramp this console exercises), [ARCH-0092](ARCH-0092-entity-exposure-surfaces.md), [SEC-0004](SEC-0004-capability-authorization-gate-constrain-project.md), [SEC-0005](SEC-0005-governed-agent-access-grants-audit-door.md).
- MCP spec: Streamable HTTP transport (2025-06-18) — client MUST advertise `text/event-stream`; `tools/list` tool shape (`name`/`description`/`inputSchema`/`annotations`); `initialize` `serverInfo` + `instructions`.
- Prior art: GraphiQL / Apollo / Hot Chocolate (same-URL human IDE via `GET` + `Accept: text/html`); MCP Inspector and `mcpo` (live-introspection consoles); the official MCP Registry `server.json` (install metadata only).

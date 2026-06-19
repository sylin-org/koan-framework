# Agent-Native Projection — build cards (Stage 9 harvest)

> Net-new cards discovered by [09-agent-native-projection.md](../../09-agent-native-projection.md)
> (the grounded harvest of the agent-native charter). They extend the [07 strategic ladder](../../07-strategic-prompt-stash.md).
>
> **Preamble**: paste the `[SESSION-PREAMBLE]` from [../../07-strategic-prompt-stash.md](../../07-strategic-prompt-stash.md) atop each card.
> **Disclaimer**: shapes are TARGET shapes; type/attribute names (`project()`, `[McpApplication]`,
> `Grant.SignedIn`, `Needs`/`Door`) are *illustrative* — bind to Koan's real primitives
> (`IEntityEndpointService`, `EntityEndpointDescriptor`, WEB-0068 predicates, `[McpEntity]`,
> `RequiredScopes`, the SEC-0002 `IAuthorize` seam). Tracked in [../PROGRESS.md](../PROGRESS.md).

## The one principle these serve

There is **one projector** — `project(model × grant) → { resources, verbs, edges, schemas, errors }`.
Description and enforcement are the *same* projection, or they drift — and a drifting security
boundary is a boundary that lies. Every card below either **builds** that projector or **removes a
second code path** that lets it drift. **Default is Wall** (least privilege is the lazy posture).

---

## AN-leak · Relationship-expansion visibility bypass 〔SECURITY — do first · T3〕

> **Runnable card**: [AN-leak-relationship-expansion-visibility.md](AN-leak-relationship-expansion-visibility.md)
> — the full self-contained session (preamble + anchors + the asymmetric T1/T2 tests as the spec).

**Confirmed finding (09 §10), high severity.** Relationship expansion (`?with=all` REST +
`with:"all"` MCP) and `GetRelatives`/`GetChildren`/`GetParents` fetch related rows via
`Data<T,K>.All()`/`.Get()` directly ([Entity.cs:863-920](../../../../src/Koan.Data.Core/Model/Entity.cs#L863-L920)),
bypassing the WEB-0068 visibility predicates the root read applies
([EntityEndpointService.cs:262-268](../../../../src/Koan.Web/Endpoints/EntityEndpointService.cs#L262-L268)).
A caller reads a *visible* parent, expands, and receives child rows a direct query would hide — on
REST **and** MCP. Same class as the get-by-id bypass (WEB-0068), on a surface the 2026-06-14 sweep
missed. See memory `web-readpath-visibility-per-surface`.

- **BUILD**: route relationship traversal/expansion through the governed read path so expanded rows
  re-apply the same predicates/hook pipeline as a direct query of the child entity (the *"edge
  inherits its resolved query's projection"* rule). Minimal increment: re-filter `GetRelatives`
  output through `context.Options.Predicates` at the endpoint layer. Proper fix: make the traversal
  loaders predicate-aware (push request context down; query by FK instead of `All()`+in-memory).
- **TEST (the spec)**: the charter's **T1** (walled child → expansion returns empty-by-predicate;
  no count, no field name, no existence) + **T2** (two edges to the same target type, asymmetric
  disclosure). Both MISSING today — write them first; they should FAIL pre-fix, PASS post-fix.
- **NOTE**: also fixes an N-load perf bug (`All()` loads every row of the child type to filter by FK).
- **Boundary**: touches `src/Koan.Web` + `src/Koan.Data.Core` + `src/Koan.Mcp` (read path). The MCP
  surface only *consumes* the fix — no governance logic belongs in the transport.

---

## AN1 · MCP wire-shape conformance 〔foundational precondition〕

**Why first**: Koan's MCP tool shape is non-spec — it serializes `input_schema` (snake_case) + a
custom `metadata` bag, where the MCP spec uses `inputSchema` (camelCase) + a dedicated `annotations`
object ([McpRpcHandler.cs ToolDescriptor.From ~505-540](../../../../src/Koan.Mcp/Hosting/McpRpcHandler.cs)).
Hints/annotations placed in `metadata` are silently ignored by spec-compliant clients → **blocks AN4**.

- **BUILD**: emit the spec tool object — `inputSchema` + a real `annotations` object — at the wire
  layer; keep Koan's internal richer metadata if useful, but the spec fields must be present and
  correctly named. Add an `outputSchema`/`structuredContent` slot (even if empty) so AN-structured
  can fill it later.
- **TEST**: a conformance spec asserting the serialized tool object matches the MCP tool schema
  (camelCase `inputSchema`, `annotations` present) for an entity tool and a `[McpTool]` verb.

## AN2 · One schema projector (OpenAPI = a face of the entity descriptor) 〔T2/hard〕

**Gap (09 O1ˢ)**: OpenAPI/Swagger comes from ASP.NET's stock reflective generator — blind to
`[McpIgnore]`/`[McpDescription]` — while MCP comes from `SchemaBuilder`. An entity can hide a field
from an agent and **leak it in Swagger**. Two field-level schema generators that silently drift.

- **BUILD**: make OpenAPI a *face of the same descriptor + field policy* the MCP `SchemaBuilder`
  uses, so a field hidden from agents is hidden from Swagger too. Touches the ASP.NET schema-
  transformer seam — bound the first increment to the field-visibility policy ([McpIgnore]); risks
  Swagger-output changes, so pin current Swagger output with a test first.
- **TEST**: an entity with an `[McpIgnore]` field — assert it is absent from BOTH the MCP tool
  schema and the generated OpenAPI body schema.

## AN3 · Enforcement consolidation (one choke point, all transports) 〔T3〕

**Gap (09 O3/O5/O8)**: per-principal access (scope/grant + visibility) is enforced **per-transport**
— `HttpSseRpcBridge` filters HTTP/SSE, while default-on STDIO binds the raw `McpRpcHandler`
(unfiltered). Per-verb topology lives on the MCP attribute, not the shared descriptor.

- **BUILD**: move the *effective-access* decision (attribute exposure ∩ grant ∩ visibility) to the
  shared choke point (`EndpointToolExecutor`/`RequestTranslator`) so STDIO, HTTP/SSE, and Code-Mode
  are governed by **one** projection. STDIO either routes through the same filter or documents the
  local-trust invariant + a tripwire test. Per-verb topology declared once at the operation level,
  consumed by REST and MCP (no MCP-only authz vocabulary — the WEB-0068 per-surface-drift lesson).
- **TEST**: same tool, same caller → identical visibility/denial over STDIO and HTTP/SSE; a verb
  scoped at the operation level is gated identically on REST and MCP.

## AN4 · Verb-derived tool annotations 〔← AN1〕

**Gap (09 O11)**: only `isMutation` is emitted. The 12-op verb enum + the `MutationKinds` HashSet
make `readOnlyHint`/`destructiveHint`/`idempotentHint` mechanically derivable.

- **BUILD**: emit annotations into the spec `annotations` object (requires AN1): `Query`/`Get` →
  `readOnly`; `Remove`/`Delete*` → `destructive`; `Save`/`Upsert*` → `idempotent`. Custom
  `[McpTool]` verbs gain nothing automatically — add `[ReadOnly]`/`[Destructive]`/`[Idempotent]`
  attributes (the dangerous hand-written verbs are exactly the ones that need explicit marking).
- **TEST**: assert each entity verb carries the right annotation; a `[Destructive]` custom verb too.

## AN5 · Edge-of-capability disclosure (the Door) 〔opt-in, deny-by-default〕

**Gap (09 O4)**: denials are a bare `-32604 "Forbidden."`; the reason already exists
(`AuthorizeDecision.Forbid(Reason)`, `RbacAuthorizationProvider` emits "requires one of: {roles}")
but is discarded at the sink. The Door projection state (§9.2) is its home.

- **BUILD**: turn a locked verb whose owner marked it a *Door* into a signposted projection — name
  the requirement (`Needs`/`RequiredScopes`, already computed) + the developer's one-line unlock
  string. Stop discarding `decision.Reason` (Web `ProblemDetails` extension; MCP `-32604`
  `error.data`). **Deny-by-default**: terse `"Forbidden."` stays the default; disclosure is opt-in
  per capability — disclosing the authz lattice is a privilege-enumeration oracle (Invariant §6.8).
  *What's required vs held* = framework's job; *how to acquire it* = a templated developer slot.
- **DECIDED — admin is a Wall, not a Door (09 §11.4)**: privileged/admin capabilities are **silent
  walls**, never signposted doors — reachable for the right grant, *invisible* to every lesser one
  (silent even at the door tier). Never default a capability to Door. Signposting an admin tier ships
  an attack-surface map ("not-projecting ≠ hiding"). A Door is only ever a *deliberate, per-capability*
  promotion of a non-sensitive gap.
- **TEST**: anonymous sees a Door (named + unlock) for a door-marked verb; a wall-marked verb stays
  **absent**; an admin-marked verb is **absent at every lesser tier** (charter T6); the denial error
  names the requirement only when disclosure is opted in.

## AN6 · Protocol currency (Streamable HTTP + OAuth 2.1) 〔T3/hard〕

**Gap (09 O12)**: Koan is on classic HTTP+SSE (deprecated) + OAuth 2.0 basic; no Streamable HTTP,
no RFC 9728 resource-server metadata.

- **BUILD**: Streamable HTTP transport (the SSE deprecation successor) + OAuth 2.1 ingress
  (RFC 9728 protected-resource metadata, RFC 8707 resource indicators, PKCE). Transport churn
  obsoletes the SSE stack — dual-transport during transition. *In-charter*: transport + ingress
  validation. *Out of scope*: dynamic client registration (agent-runtime territory).
- **TEST**: a Streamable-HTTP round-trip; a `/.well-known/oauth-protected-resource` document; a
  bearer-gated tool call.

## AN7 · Governed edge traversal (edges-as-sugar, not verbs) 〔T3 — subsumes AN-leak〕

**The charter's §5, harvested.** Relationships are already declared (`[Parent]`/`[ParentOf]`,
field-keyed, same-target-different-field supported —
[RelationshipMetadataService.cs:49-56](../../../../src/Koan.Data.Core/Relationships/RelationshipMetadataService.cs#L49-L56),
[Entity.cs traversal ~600-760](../../../../src/Koan.Data.Core/Model/Entity.cs#L600-L760)). The
projector should **read** them and expose navigation as terse sugar — `{ "works-authored": "id" }`
→ `Work.query(authorId: "id")` **through the one projector, under the caller's grant** — so the
whole graph is navigable **without any edge appearing in the tool catalog** (avoids catalog
explosion: 70 entities × relationships = hundreds of edge-verbs = the bloat we engineer against).

- **DECIDED (the rule)**: an edge is a **route, never a verb**. It inherits the projection of its
  *specific resolved query* (per-edge governance: `works-authored` open, `works-reviewed` walled —
  same target type, asymmetric disclosure), never the target type in the abstract. A walled edge is
  **silent** (no count, no field name, no existence — §9.4). The same `GetChildren` is *app-authority*
  when service code calls it and *grant-bounded* when the projector resolves an edge — the clamp
  lives at the projector boundary (this is where AN-leak's fix lands).
- **BUILD order**: (1) AN-leak first (governed traversal is the precondition); (2) edge metadata in
  the introspection resources (the one place an edge earns a descriptor — the field name gives the
  mechanism, not the meaning); (3) edge-traversal sugar over the MCP read surface, projected
  per-grant with the full Wall/Door/Verb trichotomy.
- **TEST**: the charter's T1 + T2 (also AN-leak's tests), plus: a grant that opens reviewer
  visibility makes `works-reviewed` appear, navigable, sized to that grant.

## AN8 · Self-introduction surface (`koan://self` — prose + structured) 〔v2, 09 §11.1〕

Render the projector's per-grant output as `koan://self` in **two faces**: first-person **prose**
(the menu, written from the `[McpApplication]`/`[McpEntity]`/Door descriptors) + the **structured**
projection beneath it. The menu reshapes per grant and is authored by nobody (rename → updates; wall
→ vanishes; promote a door → invitation appears). The "one step further" door line is a built-in,
lie-proof conversion funnel.

- **BUILD**: a `koan://self` MCP resource (rides P1.2/O6 introspection resources) emitting (1) the
  structured self-projection — entities/verbs/edges/capabilities visible to THIS grant — and (2) a
  `ToProse()` rendering of it. Not a new surface: it's one rendering over the projector output.
- **DECIDED**: both faces in one resource; **prose is the greeting, structured is the contract —
  prose is NEVER the only form** (prose is lossy: exact verb names/schemas don't survive a friendly
  sentence). On re-pull after a grant change, the agent diffs to the delta (deltas-add-only, §11.2).
- **TEST**: charter **T6** — anonymous menu shows verbs + the door's "one step further" line and **no
  mention of any admin tier**; signed-in promotes the door to a verb; admin shows the admin verbs;
  each tier is a *complete, self-consistent* world, not a redaction.
- **DEPS**: P1.2/O6 (resources) + the Wall/Door/Verb projection (AN3 enforcement). Examples neutral.

## AN9 · Authority-free correlation (the "pin") 〔v2, 09 §11.3 — grounded〕

A per-conversation id (client-facing **pin**, builder-facing **correlation id**) for **audit
stitching + continuity**, carrying **zero authority**.

- **DECIDED — the invariant**: **continuity ≠ authority.** The pin is *never* consulted for
  permission; authorization is per-request against the grant (the token), always. Accepting a pin in
  place of a grant is **session fixation**. (Grounding: Koan HONORS this today — the MCP session id is
  opaque and auth is re-checked per RPC from the captured `ClaimsPrincipal`,
  `HttpSseRpcBridge.cs:259-321` — **no session-fixation risk to fix; preserve it**.)
- **BUILD**: mint via the existing **`StringId.New()`** (GUIDv7, [`src/Koan.Core/StringId.cs`](../../../../src/Koan.Core/StringId.cs)
  — free, time-ordered, orders the audit trail for free); accept/propagate a caller-supplied
  **`x-correlation-id`** (Koan already emits passive `Koan-Trace-Id` from OTel `Activity`); thread it
  into audit so a trajectory stitches into one ordered story (`CanonAuditLog.Evidence` freeform bag,
  or a dedicated field). Optional stateful scratch keyed off it; a stateless app ignores it.
- **TEST**: charter **T7** (replay the same pin WITHOUT the token → the **anonymous** world; the pin
  alone unlocks nothing) + **T8** (stepped continuity across *different nodes* — statelessness; the
  upgrade delta lists additions only).
- **NOTE**: net-new but cheap (GUIDv7 minter + passive trace header already exist); pair with P3.1
  grants/audit. The continuity≠authority contract is a *standing guardrail* for all of P3 — never let
  a pin/session/connection id gate authorization.

## AN10 · Auth on-ramp — device grant (RFC 8628), Reference=Intent 〔companion, 09 §12〕

**The missing half of the cold-start funnel (AN8).** A headless agent earns a grant via the OAuth 2.0
Device Authorization Grant (RFC 8628), seated in MCP's OAuth-2.1 resource-server model — net-new
(Koan has only interactive auth-code/OIDC + inbound bearer today), built on the WEB-0071 handler
substrate. The menu *advertises* the door; this *opens* it.

- **DECIDED — Reference = Intent, NOT enumeration (the architect's correction):** there is **no
  `[McpAuth(providers…)]` attribute and no marker class.** The on-ramp projects automatically when
  `Koan.Mcp` + ≥1 configured+healthy auth provider are present, offering exactly the set from
  **`IProviderRegistry.EffectiveProviders`** — the same set Koan already projects at
  `GET /.well-known/auth/providers` ([DiscoveryController.cs:10-14](../../../../src/Koan.Web.Auth/Controllers/DiscoveryController.cs#L10-L14));
  reuse it, the provider list is a projection, not authored. Production gating
  ([ProviderRegistry.cs:65-83](../../../../src/Koan.Web.Auth/Providers/ProviderRegistry.cs#L65-L83))
  is inherited. Posture = config (`Koan:Mcp:Auth:*` to disable the on-ramp or restrict which
  configured providers are offered to agents), **defaulting to all configured** — opt-out, never
  enumeration.
- **DECIDED — adopt, don't invent:** implement RFC 8628 device grant on the maintained ASP.NET
  OAuth/OIDC handlers (WEB-0071 `AuthSchemeSeeder`), PKCE on the browser leg. Do not hand-roll code
  entropy / expiry / backoff / single-use redemption.
- **DECIDED — the three strings (invariant #13):** `user_code` (human, low-entropy, say-aloud),
  `device_code` (agent-only, high-entropy, single-use secret — the poll key + bearer; never logged in
  full), `pin` (authority-free correlation, AN9). Never equal/derived/interchangeable. The pin is
  **never accepted at the token endpoint**; authority redeems on the `device_code` alone.
- **BUILD:** MCP capability `auth.signin { provider }` → device-authorization request → return
  `verification_uri(_complete)` + `user_code` + `device_code` + `interval` + `expires_in`;
  `auth.poll { device_code }` → `authorization_pending` / `slow_down` / `complete(grant)` / `expired`
  / `denied`. On complete, bind the grant to the session and **trigger re-projection** (charter §5);
  stale-greeting-fresh-enforcement holds (revocation caught at next action). The human consent event
  is a first-class audit entity stitched on the pin.
- **TEST:** charter **T9** (poll-key ≠ pin / single-use / `device_code` never logged in full) +
  **T10** (cold-start walkable end-to-end; additions-only delta; one pin). Plus: the offered provider
  set equals `IProviderRegistry` (no enumeration drift); production gating respected.
- **DEPS:** the grant model (P3.1) + the projector (AN3) + AN9 (the pin) + the WEB-0071 auth
  substrate. It is the **front door** — built after the room exists.

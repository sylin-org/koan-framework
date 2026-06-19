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

## AN-leak · Relationship-expansion visibility bypass 〔SECURITY — do first〕

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
- **TEST**: anonymous sees a Door (named + unlock) for a door-marked verb; a wall-marked verb stays
  **absent**; the denial error names the requirement only when disclosure is opted in.

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

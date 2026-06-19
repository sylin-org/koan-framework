# Stage 9 — The agent-native projection (grounded)

**Date**: 2026-06-18 · **Status**: strategic harvest, grounded against the codebase and
adversarially verified. Extends [05 §3](05-strategic-position.md) (the agent-native thesis) and
[07](07-strategic-prompt-stash.md) (the build ladder) with a sharper *definition*, a structural
*reframe*, and a verified *opportunity catalog*.

> ⚠️ Like 07, the shapes below are TARGET shapes and verified GAPS — not a claim that every named
> API exists. Each "Koan has X" / "Koan lacks Y" was grounded at a `file:line` an agent actually
> read, then adversarially re-checked; the verification pass corrected optimism in **7 of 10**
> groundings (one was *upgraded* — Code Mode is more real than first scored — and one *downgraded*
> — per-caller filtering is transport-asymmetric, not "complete"). This file is excluded from the
> snippet-compile lint.

---

## §1 What "agent-native" means

An **agent-native application framework** is not an agent runtime (orchestration, memory, model
routing — that lane is crowded: Semantic Kernel, LangGraph, Mastra, Vercel AI SDK). It is the
*server side of the agent seam*, done so well the developer writes no tool:

> **The application projects itself — what it is, what it can do, what it requires, where its edges
> are — as a single truth, computed for whoever is asking, such that the developer writes no tool
> and the agent holds no false belief.**

The decisive idea is **one honest projection, seen from two faces.** "The app explains itself to a
human" and "the app is operable by an agent" are not two systems someone integrated — they are the
*same truth* viewed from opposite sides of one seam. Which face you see depends only on which side
you stand. That is what *native* means: you cannot pry the app-ness and the agent-ness apart,
because the thing that makes the app legible is the identical thing that makes it operable.

The wish list has two authors, and they are the same wish twice:

| The developer wishes | …is the agent's | …seen from the other face |
|---|---|---|
| I declare my domain once; never write a tool/schema/auth-rule | I meet a world my own size | the surface is *reflected*, not authored |
| The secure posture is the lazy one — walls until I open a door | everything I perceive, I can do; I never reason about permission | least-privilege is the default, not a setting |
| I never write upsell; the app advertises its depth truthfully | at my edge, I'm told what lies beyond and how to reach it | locked doors name themselves |
| My surface can't drift | I hold no false beliefs | one source, projected — not two kept in sync |
| Any agent, from any framework, can operate my app | no protocol to negotiate; I act on a shared reality | MCP as the universal seam; marry no runtime |
| When an agent breaks something, I read exactly what happened | — | history is just entities |
| My app gets *safer* as models get smarter | when I fail, the error names the fix | honesty is the contract, not the model |

**Why this is the durable bet.** A smarter frontier model makes *orchestration* matter less — it
plans, decomposes, and selects tools that a runtime spent a release scaffolding, so the runtime
depreciates with every capability jump. But no model release makes your application automatically
safe to touch, honest to read, or sized to its caller. **The world appreciates while the mind
commoditizes.** Build the ground every mind walks on — it is the only thing in this landscape worth
more next year than today. This is the strategic answer to "won't frontier models obsolete this?":
Koan is the *application* framework, not the agent framework, on purpose.

---

## §2 The reframe: one execution core, many projection faces

Grounding the vision against the codebase produces one finding that reorganizes the whole program:

**Koan's "one projection" is already TRUE on the axis where it executes, and NOT YET true on the
axis where it describes and enforces.**

- **Unified today — the execution core.** REST and MCP run through the *same*
  `IEntityEndpointService<T,K>` + the *same* `EntityEndpointDescriptor` + the *same* WEB-0068
  visibility predicates + the *same* hook pipeline. A tool call and an HTTP call are one motion.
  Verified: [EndpointToolExecutor.cs:44-60](../../src/Koan.Mcp/Execution/EndpointToolExecutor.cs#L44-L60)
  (MCP resolves the same service by reflection), [McpEntityRegistry.cs:155-156](../../src/Koan.Mcp/McpEntityRegistry.cs#L155-L156)
  (identical descriptor provider + mapper), `EntityEndpointService` predicate enforcement on keyed
  reads (`PassesRequestPredicates`).
- **Divergent today — the projection / enforcement faces.**
  1. **Schema is generated twice.** OpenAPI/Swagger comes from ASP.NET's stock reflective
     generator — *blind to `[McpIgnore]`/`[McpDescription]`* — while MCP comes from `SchemaBuilder`.
     An entity can hide a field from an agent and **leak it in Swagger.** (The "shared descriptor"
     is an *operation-kind* catalog, not a field-level schema model — OpenAPI doesn't consume it.)
  2. **Enforcement lives per-transport.** Per-principal `tools/list` filtering + scope gating live
     only in `HttpSseRpcBridge`; the **default-on STDIO transport binds the raw `McpRpcHandler`**,
     which lists every tool unfiltered and executes `tools/call` with no scope check.
  3. **The self-describing runtime is HTTP-only.** Provenance, `CapabilitySet`,
     `/.well-known/aggregates`, `/mcp/health` exist — but MCP exposes **only `tools/*` + `ping`**;
     there is **no `resources/list` / `resources/read`.** The agent cannot *ask*.

**So the work is not "build agent features." It is: collapse the projection-and-enforcement axis
down to the already-unified execution axis.** That is the vision's "one surface, two faces," stated
in Koan's own architecture.

### §2.1 The master principle: declare once, project and enforce from there

Every catch in the catalog below reduces to the **same** structural hazard — *anything declared or
enforced per-surface drifts.* This is the team's own WEB-0068 lesson ("visibility is per-surface;
sweep them all") generalized into a design law:

> **Declare the topology once at the shared descriptor/operation layer; project the schema and
> enforce the access from there to every face (REST, OpenAPI, MCP tools, MCP resources, the typed
> SDK).** A field policy, a verb's door/wall status, or a scope that lives on *one* surface is a
> latent "gated on one door, open on the other" footgun.

This single principle de-risks default-to-wall, per-verb topology, grants, *and* schema unification
at once. It is the spine of the cards in §8.

### §2.2 A structural validation

The adversarial pass judged **all ten wishes sound and in-charter for an application framework** —
none required becoming an agent runtime. The places that *edged* toward over-reach were exactly the
unbuilt parts the framework already, correctly, punts (the Auto-mode client-capability handshake;
a bespoke per-operation RBAC DSL; hard cryptographic revocation). That is itself a harvest: *native
= one projection, not features bolted on* is structurally true here — the domain-exposure mandate
already owns every wish.

---

## §3 The verified opportunity catalog

Applicability scale: **true-today** · **achievable-cheap** (bounded) · **achievable-hard** (a real,
design-needed program). No wish scored *aspirational* or *out-of-scope*.

### Tier 0 — already real (lead with these)

| # | Opportunity | Verdict | The honest catch |
|---|---|---|---|
| O9 | **Code Mode** — act on the whole world in one motion (Jint sandbox + generated TS SDK + entity proxy + quotas + a tested sample suite) | **true-today, ahead of the field** | Auto-mode silently degrades to *Full* (worst-of-both for tokens). Fix is **docs/default guidance** (`Exposure=Code`), not a nonexistent capability-negotiation RFC. Plus an ADR/code drift: per-entity exposure is documented but `ResolveExposureMode()` doesn't implement it. |
| O3 | **Per-caller sized projection** — `tools/list` filtered per principal | **true-today on HTTP/SSE** | Filtering + call-time gating live only in `HttpSseRpcBridge`; default-on **STDIO binds the raw handler** (unfiltered). Defensible as a local trust boundary — but make it explicit + tripwired, not "complete." `resources/list` half is moot until O6 ships. |
| O1ᵉ | **The unified execution core** — one service + descriptor + predicates + hooks behind REST and MCP | **true-today** | The foundation that makes everything below cheap. Its *schema* half is not unified (Tier 2, O1ˢ). |
| O8ᵃ | **Audit substrate** — `CanonAuditLog : Entity<T>` auto-records policy-eval mutations, queryable | **partially true-today** | The *agent*-mutation audit path is the missing slice (Tier 2, O8). "History is just entities" is closer to true than first scored. |

### Tier 1 — the doorway (cheap, high-leverage)

| # | Opportunity | Verdict | The honest catch |
|---|---|---|---|
| O6 | **The app explains itself as MCP resources** (`koan://app`/`entities`/`capabilities`/`boot-report`/`health`) — the neglected primitive | **achievable-cheap** | Two `[JsonRpcMethod]` handlers + a URI parser over material that already exists. Client-gated (not all MCP clients consume resources). *This is P1.2 — the self-introspection gap the competitive scan said nobody fills well.* |
| O2 | **Default-to-wall** — flip `AllowMutations` default to false | **achievable-cheap** | One line + ~16-declaration audit (not 50). But: (a) already *decided* inside P3.1 — don't double-count; (b) the flip walls **writes only** — reads still return every field by default. True default-to-wall must also default the **read surface** closed. Breaking change that bites silently (tools vanish from `tools/list`) → major-version + migration guide + boot warning. |
| AN4 | **Verb-derived annotations** (`readOnly`/`destructive`/`idempotent` from the 12-op verb enum) | **achievable-cheap** | "For free" only after **AN1**: Koan's wire shape is non-spec (`input_schema` + a custom `metadata` bag), so hints dumped there are *ignored by compliant clients*. Custom `[McpTool]` verbs (most likely to be dangerous) get nothing automatically — they need explicit attributes. |
| AN5 | **Edge-of-capability disclosure** — turn bare `"Forbidden."` into "required: scope/role X" | **achievable-cheap** | The reason **already exists** (`AuthorizeDecision.Forbid(Reason)`, `RbacAuthorizationProvider` emits "requires one of: {roles}") and is *discarded at the sink*. The real work is a **policy gate, not plumbing**: disclosure is a privilege-enumeration oracle → must ship **deny-by-default, opt-in per surface.** *What's required vs held* = framework's job; *how to acquire it* = a templated slot for app/IdP logic. |
| O7ʰ | **Honest errors (the swallow half)** — the 3 genuine silent catches ([ResponseTranslator.cs:149](../../src/Koan.Mcp/Execution/ResponseTranslator.cs#L149), [McpCustomToolInvoker.cs:70](../../src/Koan.Mcp/CustomTools/McpCustomToolInvoker.cs#L70)) | **achievable-cheap** | **This is the deferred F2-mcp card** — pre-scoped hygiene, reframed as the "honest tools" *feature*. The structured-output half is Tier 2. |

### Tier 2 — the cathedral (hard; "declare once" or it drifts)

| # | Opportunity | Verdict | The honest catch |
|---|---|---|---|
| O1ˢ / AN2 | **One schema projector** — make OpenAPI a *face of the same descriptor + field policy* | **achievable-hard** | Closes the "hidden from MCP, leaked in Swagger" drift. Touches ASP.NET's schema-transformer seam → risks Swagger-output changes. The honest first increment of the whole "one projection" vision. |
| O5 / AN-topology | **Per-verb door/wall topology** — extend the single read/write axis (`AllowMutations` already partitions the 11 kinds) to per-operation scopes | **achievable-cheap to build, hard to keep honest** | **Must be declared once at the shared operation level**, consumed by REST *and* MCP — MCP-only re-opens the per-surface drift class WEB-0068 just fixed. (Folded into AN3.) |
| O8 | **Grants + agent identity + single choke point** — `AgentGrant`/`AgentAction` as entities | **achievable-cheap in LOC, do-it-carefully in review** | RequiredScopes *is* enforced today (per-transport). Enforcement must **consolidate** to one choke point (AN3) before a grant gate is added, or you ship two divergent authz paths. Agent-identity projection is genuinely new. **"Revoked in seconds, fleet-wide" via cache coherence is best-effort, not a hard guarantee** (P3.1 punts real epoch/CAEP to the Trust fabric). |
| O7ˢ | **MCP structured output** (`outputSchema`/`structuredContent`) | **achievable-hard** | The part that delivers "agent understands the response shape" — but it is a spec-coupled schema-derivation effort across collection/model/short-circuit shapes, not a 2-line add. |
| AN6 | **Protocol currency** — Streamable HTTP (SSE is deprecated) + OAuth 2.1 (RFC 9728/8707) | **achievable-hard** | Transport churn obsoletes the just-shipped SSE stack. Transport + ingress validation are in-charter; dynamic client registration is over-reach. Table-stakes for "any agent from any framework." |

---

## §4 Cross-cutting discoveries (surfaced by grounding, not in the original list)

1. **The "declare once" master principle** (§2.1) — the de-risker for O1ˢ/O3/O5/O8 *as a set*.
   Harvest it as the governing law, not four separate fixes.
2. **Non-spec MCP wire shape** — Koan serializes `input_schema` (snake_case) + a custom `metadata`
   bag where the MCP spec uses `inputSchema` (camelCase) + a dedicated `annotations` object. A
   conformance debt that **blocks AN4** and bites any strict client. Cheap, foundational. → **AN1**.
3. **SSE is deprecated** in favor of Streamable HTTP. → **AN6**.
4. **Default-to-wall is internally inconsistent today** — `EnableHttpSseTransport=false` and
   `RequireAuthentication=true`-in-prod already wall-by-default, while `AllowMutations=true` does
   not. The flip is consistency, not just policy.
5. **"Walls" covers writes only** — the read-surface default-exposure is the *unaddressed half* of
   default-to-wall (every public field is returned by default). → folded into the P3.1/O2 refinement.

---

## §5 The meta-harvests (principles, positioning, sequencing)

- **Single-seam as the architectural north-star** — derive *every* external face (REST, OpenAPI,
  MCP tools, MCP resources, typed SDK) from one entity projection. Koan proves it on execution; the
  program is to extend it to schema, enforcement, and introspection.
- **Default-to-wall as a design law** — the zero-config posture must be the maximally safe one; you
  cannot misconfigure into danger because the lazy path is the locked path.
- **The "appreciating ground" positioning** (§1) — the defensible answer to model-commoditization,
  and the reason Koan is the *app* framework.
- **Doorway-first sequencing** — build the small honest doorway before any cathedral.

---

## §6 Honest caveats (so we don't oversell)

Perception-equals-capability is **transport-asymmetric** (STDIO is unfiltered by design).
"Revoked in seconds" via cache coherence is **best-effort**, not a security guarantee.
"Told how to earn it" is a **privilege-enumeration oracle** unless opt-in/deny-by-default.
"Annotations for free" covers **entity CRUD only**, not the hand-written verbs most likely to be
dangerous. Code Mode's *Auto* default **defeats its own token premise**. None of these sink the
vision — each is the difference between a true claim and a manifesto.

---

## §7 The doorway (one gesture), grounded

The vision's proof is that the whole list collapses into one gesture: **`[McpEntity]`,
default-to-wall, point an agent at it.** Anonymous, the agent meets a true and bounded reading-room
that honestly names the doors it hasn't earned and how to earn them; authenticated, the walls move,
new verbs exist, and the dangerous ones wear their warnings where the agent can read them. Same
code, same entities, no tool/schema/upsell/false-belief on either side.

Grounded, that gesture is **reachable from Tier 0 + Tier 1**: it needs the **O2** default-to-wall
flip (+ read-surface-closed), **O6/P1.2** resources (so the agent can ask), **AN5** opt-in
disclosure (so locked doors name themselves), and **AN1** the wire-shape fix (so **AN4** annotations
land where clients read them). That is the doorway. The cathedral — **AN2** schema unification,
**AN3** enforcement consolidation, **O8/P3.1** grants, **AN6** protocol currency — grows behind it,
once anyone can walk through.

---

## §8 Card map

How the catalog maps onto the existing ladder and the net-new cards this harvest discovered (cards
in [`prompts/07/`](prompts/), tracked in [`prompts/PROGRESS.md`](prompts/PROGRESS.md)):

| Catalog item | Card | State |
|---|---|---|
| O6 — app explains itself as resources | **P1.2** (sharpened: resources are the core) | existing |
| O2 — default-to-wall + O8 grants/audit | **P3.1** (sharpened: + read-surface-closed; enforce via AN3) | existing |
| O9 — Code Mode | Tier-0 asset; fix = docs/default guidance + the exposure-resolver drift | — |
| O3 — per-caller filtering | Tier-0; STDIO tripwire | — |
| Wire-shape conformance (`inputSchema` + `annotations`) | **AN1** | new |
| One schema projector (OpenAPI = a face) | **AN2** | new |
| Enforcement consolidation (one choke point, all transports) | **AN3** | new |
| Verb-derived annotations | **AN4** (← AN1) | new |
| Edge-of-capability disclosure (opt-in) | **AN5** | new |
| Protocol currency (Streamable HTTP + OAuth 2.1) | **AN6** | new |
| **Relationship-expansion visibility leak** (confirmed — §10) | **AN-leak** | new (security) |
| Governed edge traversal (edges-as-sugar) | **AN7** | new |
| Self-introduction surface (`koan://self` prose + structured) | **AN8** | new (v2 — §11.1) |
| Authority-free correlation (the "pin") | **AN9** | new (v2 — §11.3) |
| Auth on-ramp — device grant (RFC 8628), Reference=Intent | **AN10** | new (companion — §12) |

Cards are stashed in [prompts/07/AN-cards.md](prompts/07/AN-cards.md). As each ships, re-score the
affected pillar in [03-maturity-model.md](03-maturity-model.md) and update the
[05 §3.1](05-strategic-position.md) capability table.

---

## §9 The design charter, harvested

A second, more detailed frontier-model spec (the "Agent-Native Projection Design Charter") was
read against this harvest. Treated as *meaning, not implementation* (its API shapes are
illustrative), it sharpens the program in four ways and adds one genuinely new surface (§10).

**§9.1 The One Projector — §2.1 crystallized into a function.** The charter states the
"declare once" law as a single function:

```text
project(model × grant) → { resources, verbs, edges, schemas, errors }
```

…and names the reason it matters: *"the thing that enforces a boundary and the thing that
describes it must be the same projection, or they drift — and a drifting security boundary is a
boundary that lies."* This is the resolution of the legible-governance gap the competitive scan
found unfilled industry-wide: **everyone builds two artifacts — an enforcement policy and a
separate explanation of it — and they drift forever. The projector collapses them: the explanation
*is* the enforcement.** This reframes **AN2** (one schema projector) + **AN3** (one enforcement
choke point) as facets of one mandate — *there is one projector* — and tells us how to build it:
it is not an architecture decorated with governance; it *is* the governance.

**§9.2 Wall / Door / Verb — O2 + O4/AN5 + O5 are one model, not three features.** Project each
capability by comparing its `Needs` to the caller's grant:

| State | Condition | Agent sees | Can call |
|---|---|---|---|
| **Verb** | `Needs ≤ grant` | listed, schema'd, real | yes |
| **Door** | `Needs > grant` *and* a door is set | named + how-to-unlock, truthfully | no |
| **Wall** | `Needs > grant`, no door | nothing — absent | no |

Default is **Wall**. A "Door" is not a separate concept — it is a *projection state* of a verb
whose `Needs` exceeds the grant, and its signpost is **drift-proof because it derives from the same
`Needs` that enforces it** (Invariant: *Description = Enforcement*). So default-to-wall (O2),
honest-upsell disclosure (O4/AN5), and per-verb topology (O5) are one mechanism: *a per-grant
projection with three states.* The agent "reasons about permission zero times — it wakes up in a
room its own size."

**§9.3 Reflection is the default voice; descriptors earn their place.** Three self-similar scales
(app → entity → field/verb), each with one optional short descriptor *only for what the type cannot
say.* "The empty descriptor must feel complete, not negligent" — otherwise the culture becomes
*annotate everything* and rebuilds the schema-bloat we engineer against. Koan already has the
reflective half (`[McpEntity]`/`[McpDescription]` + XML-doc). The new bit worth harvesting: an
**app-level descriptor as the single visible home of app-wide posture** (`DefaultExposure`,
`Audit`) — default-to-wall *stated once, reviewable*, not scattered per-entity.

**§9.4 Walled means silent — cardinality and existence are disclosures.** A walled capability
discloses *nothing*: not a count, not a field name, not the shape. `"this Patient has 4 [walled]
records"` leaks volume. This sharpens O3 ("no false beliefs") into a hard invariant and is the
breach the §10 finding exemplifies.

**§9.5 The breaches are the spec.** The charter's acceptance tests (T1 lateral-movement tunnel; T2
divergent edges, same target, asymmetric disclosure; T3 revocation-race honest failure; T4
destructive dry-run-from-one-annotation; T5 three-tier projection from one declaration) live in the
*asymmetric and dynamic* cases, not the happy path — *"write these first; if they pass, everything
simpler is a special case."* This maps onto Koan's ARCH-0079 integration-tests-as-canon. Grounding
T1/T2 against the real code found a live bug (§10).

---

## §10 Confirmed finding — the relationship-expansion visibility leak

Grounding the charter's §5 (edges) and T1/T2 against the codebase surfaced a **live read-path
visibility bypass**, confirmed by two independent code-readings and verified first-hand:

- The **root** get-by-id read applies the WEB-0068 visibility predicates
  ([EntityEndpointService.cs:252-260](../../src/Koan.Web/Endpoints/EntityEndpointService.cs#L252-L260)) —
  the keyed-read gate closed in WEB-0068 (the `PassesRequestPredicates` / NotFound-on-hidden path).
- But **relationship expansion** (`?with=all` over REST and `with:"all"` over MCP) then calls
  `entity.GetRelatives(...)` with **no predicate re-application**
  ([EntityEndpointService.cs:262-268](../../src/Koan.Web/Endpoints/EntityEndpointService.cs#L262-L268)).
- And the traversal loaders fetch related rows via `Data<TChild,TKey>.All()` / `.Get()` directly,
  filtering by foreign key in-memory — **raw, app-authority, no hook pipeline**
  ([Entity.cs:863-920](../../src/Koan.Data.Core/Model/Entity.cs#L863-L920)).

**Effect:** a caller reads a *visible* parent, expands, and receives related rows that a direct
query of the child entity would have hidden — on **both REST and MCP**. MCP amplifies it: the agent
cannot distinguish "forbidden to see" from "doesn't exist" (the §9.4 invariant, violated).

This is the **same class** as the get-by-id bypass the WEB-0068 comment at
[EntityEndpointService.cs:237-240](../../src/Koan.Web/Endpoints/EntityEndpointService.cs#L237-L240)
describes — but on the **relationship-expansion / traversal surface**, which the 2026-06-14
per-surface read-path sweep missed. (The WEB-0068 suite's `Relationship_expansion_path_is_also_gated`
test checks only that the *root* is gated, not the expanded children.) **The charter's T1/T2 are
exactly the missing test.**

**Disposition:** high-severity read-path leak; should be triaged for a fix independent of the
broader agent-native program. The fix *is* the charter's edge principle — traversal must run
through the governed read path so an edge "inherits its resolved query's projection." Tracked as
**AN-leak** (the fix, security-priority) and subsumed by **AN7** (governed edge traversal as the
general design). Secondary: `All()`+in-memory-filter is also an N-load performance bug.

---

## §11 The conversational surface (charter v2), harvested

A v2 of the charter adds an *agent-experience* layer over the projector. Most of it restates §9; four
things are genuinely new. (Examples kept neutral — no app/persona names, per the repo's
persona-separation rule.)

**§11.1 The self-introduction is the projection's voice → AN8.** `koan://self` renders the
projector's per-grant output two ways: first-person **prose** *and* the **structured** projection
beneath it.

```text
I'm a directory of makers and their works.     (you: anonymous)
You can:
  • browse & query works     works
One step further:
  • favorite a work          → sign in to save works you like
```

The menu *writes itself from the descriptors* (app Description = the "I'm X" line; each entity
Description = a menu line; each Door = a "one step further" line), reshapes per grant, and is
drift-proof (rename → menu updates; wall → vanishes; promote a door → invitation appears). The "one
step further" door line is a built-in, **lie-proof conversion funnel** — the answer to an anonymous
agent's blank-room cold start, in the app's own voice. **Guardrail: prose is lossy** — the greeting
*invites*, the structured surface *operates*; `koan://self` must offer BOTH faces or the app is
"charming and unusable." Extends O6/P1.2 (resources) with the prose dimension.

**§11.2 The projector is stateless; the stepped conversation lives in the client.** The
"ask → here's your world → present a token → here's your bigger world → silence" feel is **emergent,
not implemented**: MCP resources are *pulled*, so the agent re-pulls `koan://self` after a grant
change and gets the delta; in steady state it stops pulling and just acts. The app holds **no
per-conversation state** — *the app projects; the agent remembers.* This **affirms the thesis
boundary**: conversational memory is the agent's job (agent-runtime territory), not the framework's —
Koan stays out of it. Two invariants fall out:
- **Deltas add only** — a widened grant announces what *entered* the world; it never enumerates
  remaining walls (that would leak their shape).
- **Stale greeting, fresh enforcement** — the greeting is cacheable; capability is not. Every
  *action* re-projects and re-checks, so a narrowed grant is caught at call-time with an honest
  error even when the cached greeting lags. *"That gap is exactly where the honest error lives — the
  conversational design and the structured-error work are the same work."* → ties directly to
  **AN-leak** / honest errors (O7).

**§11.3 The "pin" — authority-free correlation → AN9 (grounded).** A per-conversation id
(client-facing *pin*, builder-facing *correlation id*) for **audit stitching + continuity**, carrying
**zero authority**. The load-bearing invariant: **continuity ≠ authority** — a pin is never accepted
in place of a grant (else **session fixation**: seed/steal a pre-auth pin, ride it into the
authenticated world). Grounded against the code:
- The GUIDv7 minter already exists and is **free** — `StringId.New()` → `Guid.CreateVersion7()`
  ([StringId.cs](../../src/Koan.Core/StringId.cs)), time-ordered (orders the audit trail for free).
- `Koan-Trace-Id` (OTel `Activity`) is a passive response correlation today; the MCP `correlationId`
  param exists but is diagnostic-log-only, not threaded to audit.
- **Koan already HONORS continuity ≠ authority**: the MCP session id is opaque and auth is re-checked
  per RPC from the captured `ClaimsPrincipal`
  ([HttpSseRpcBridge.cs:259-321](../../src/Koan.Mcp/Hosting/HttpSseRpcBridge.cs#L259-L321)) — **no
  session-fixation risk today.** So AN9 is (a) a *preservation guardrail* (never let a pin/session id
  gate auth) + (b) net-new but cheap plumbing: a caller-supplied `x-correlation-id` threaded into
  audit (`CanonAuditLog.Evidence` is a freeform bag that can carry it, or a field).

**§11.4 Admin is a Wall, not a Door** (sharpens AN5 + the Wall/Door/Verb model). Privileged tiers
must be **silent walls** — reachable for the right grant, *invisible* to every lesser one, silent even
at the door tier. Signposting an admin tier ("more options for administrators") ships an
attack-surface map to anonymous callers. The framing: **not-projecting ≠ hiding** — *"you moved the
building, you didn't lock the door"*; an attacker can't form an intention toward a capability they
were never shown. Hard rule for the disclosure model: **never default a capability to Door;
admin/privileged capabilities are Walls.** Reinforces the deny-by-default + privilege-enumeration
caution already in AN5.

**§11.5 Invariants & tests.** v2 grows the invariant set to 12 (adds: stateless projector;
admin-is-a-wall; continuity ≠ authority; stale-greeting-fresh-enforcement; deltas-add-only) and the
acceptance tests to T8 — **T6** (self-introduction reshapes per grant; admin invisible; each tier a
complete self-consistent world), **T7** (the pin carries no authority — replay it without the token →
the anonymous world; session-fixation resistance), **T8** (stepped continuity across *different
nodes* — statelessness; the upgrade delta lists additions only). These fold into AN8/AN9 as their
specs.

---

## §12 The auth on-ramp — device grant, harvested (charter companion)

A companion spec (`[McpAuth]` / "Agent-Native Authentication") answers the question the projection
charter assumed away: **how a headless agent earns a grant.** It is the missing half of the
cold-start funnel (§11.1 / AN8) — the menu *advertises* the door; the on-ramp *opens* it. → card
**AN10**.

**§12.1 The shape is right: device grant (RFC 8628).** A headless agent can't show a browser, so use
the OAuth 2.0 Device Authorization Grant: the human completes the browser leg elsewhere and the two
halves rendezvous on the `device_code` poll. **Adopt, don't invent** — the parts you'd reinvent
(code entropy, expiry, polling backoff, single-use redemption, PKCE) are the hardened parts; a DIY
auth hole in the most-exercised path is exactly what "Built on Koan = the wall holds" cannot survive.
Grounded: device-grant is **net-new** (Koan has only interactive auth-code/OIDC + inbound bearer
today), built on the maintained ASP.NET OAuth/OIDC handler substrate from WEB-0071/E5.

**§12.2 The three strings = continuity ≠ authority, applied to auth (invariant #13).** The flow mints
three strings with three jobs and three sensitivities, never equal/derived/interchangeable:
- `user_code` — human-held, low-entropy, safe to say aloud (inert without a human browser login).
- `device_code` — **agent-only, high-entropy, single-use SECRET**; the poll key AND the bearer of the
  result. Whoever presents it gets the token. Never logged in full, never shared.
- `pin` — the authority-free correlation id (§11.3 / AN9); least-sensitive, lives in the greeting.

**Invariant #13 — the poll key is not the pin.** Authority redeems on the `device_code` alone; the
`pin` is **never accepted at the token endpoint.** This is AN9's continuity ≠ authority extended to
the auth flow, and the reason the three strings stay distinct end-to-end.

**§12.3 The deviation — and why Koan already dissolves it.** The spec proposes
`[McpAuth(ProviderA, ProviderB)]` (enumerating providers on a marker class). **That contradicts
Reference = Intent**, and grounding shows Koan already does it better:
- "Configured ⇒ available, no enumeration" is Koan's real model — providers self-register via
  `IAuthProviderContributor` per connector package, composed in `ProviderRegistry`, seeded as ASP.NET
  schemes by `AuthSchemeSeeder`
  ([ProviderRegistry.cs:15-83](../../src/Koan.Web.Auth/Providers/ProviderRegistry.cs#L15-L83)). No
  per-app provider list.
- **The provider set is ALREADY a projection** — `IProviderRegistry.GetDescriptors()` is surfaced at
  `GET /.well-known/auth/providers`
  ([DiscoveryController.cs:10-14](../../src/Koan.Web.Auth/Controllers/DiscoveryController.cs#L10-L14)).
  The on-ramp reuses *that*; the list is discovered, not authored.
- **Production gating already exists** — dynamic providers are off in prod unless explicitly
  configured ([ProviderRegistry.cs:65-83](../../src/Koan.Web.Auth/Providers/ProviderRegistry.cs#L65-L83)).

**The corrected surface (Koan-idiomatic):** no `[McpAuth]` enumeration, no marker class. Reference
`Koan.Mcp` + reference ≥1 auth connector + configure it → the `auth.signin` device-grant capability
**projects automatically over MCP, offering exactly the configured+healthy providers from
`IProviderRegistry`**. Posture (turn the on-ramp off, or restrict which configured providers are
offered to agents) is **config, defaulting to all configured** — opt-out, never enumeration. This is
the architect's principle verbatim: *if an auth provider is present and properly configured, it
should be available.* It also kills a drift class — an enumerating attribute would be a second source
of truth that silently diverges from what's actually configured.

**§12.4 Tests.** **T9** (poll-key ≠ pin: presenting the `pin` / `user_code` / a random value at the
token endpoint yields nothing; only the single-use `device_code` redeems, and only once;
`device_code` never appears in full in any audit entity). **T10** (cold-start on-ramp walkable
end-to-end: anonymous reads the door → `auth.signin` → artifacts → [human browser leg] → poll on
`device_code` → grant → re-project; additions-only delta; the whole trajectory stitched on one pin).

---

## §13 Concept fold map — bind to existing primitives before minting new ones

The harvested specs share a pattern worth naming as the program's **first discipline**: they propose
*new* surface where Koan often already carries the intent. Four dissolutions have landed this way —
edges → `[Parent]`/`GetChildren`; the pin → `StringId.New()`/correlation; auth providers →
`IProviderRegistry`; and `[McpApplication]` → `[KoanApp]` (below). This is the "fewer but more
meaningful parts" / concept-budget discipline ([[koan-redesign-discipline]]), and it guards against
the very drift the program exists to kill: **an authored copy that silently diverges from the
primitive it duplicates.** So before any AN card is built, each agentic concept **binds to its
existing Koan primitive; net-new is the exception that earns its place** — the charter's own
"descriptors earn their place" rule, turned on the framework's own surface.

| Charter surface | What it carries | Existing Koan primitive | Disposition |
|---|---|---|---|
| `[McpApplication]` — Description | app "I'm X" identity | `[KoanApp]` → `ApplicationIdentitySnapshot` / `KoanEnv.CurrentSnapshot.Application` | **FOLD** — no new attribute; AN8 reads the snapshot |
| `[McpApplication]` — DefaultExposure | wall-by-default agent posture | `[McpDefaults]` + `McpServerOptions.Exposure` | **FOLD** — already there |
| `[McpApplication]` — Audit | per-app mutation history | *(none in Koan.Mcp; `CanonAuditLog` lives elsewhere)* | **NET-NEW** — a `McpServerOptions`/`McpAuditOptions` flag, stitched by the pin (AN9), tied to P3.1 |
| `[McpField]` | a field rule the type can't say | `[McpDescription]` (+ `[McpIgnore]` for exclusion) | **FOLD** — exists |
| `[McpMethod]` | custom-verb truth + Needs/Door | `[McpTool]` + the entity verb taxonomy + the authz seam | **FOLD** — `[McpTool]` exists; Needs/Door = authz |
| `[Door]` / `Grant` / `Needs` | the `Needs ≤ grant` comparison | SEC-0002 `IAuthorize` + `RequiredScopes` (+ P3.1 `AgentGrant`) | **BIND** to existing authz; mint only `AgentGrant` (P3.1) |
| `project(model × grant)` | the one projector | `IEntityEndpointService` + `EntityEndpointDescriptor` + WEB-0068 predicates + hook pipeline | **EXTEND** the execution core (AN2/AN3) — don't invent |
| the `pin` | authority-free correlation | `StringId.New()` (GUIDv7) + `Koan-Trace-Id` | **EXTEND** (AN9) |
| `[McpAuth]` provider list | the auth providers | `IProviderRegistry` / `GET /.well-known/auth/providers` | **FOLD** — no enumeration (AN10) |
| edge-traversal sugar | navigable relationships | `[Parent]`/`[ParentOf]` + `GetChildren` | **EXTEND** (AN7) governed |

**The headline:** of the ~10 surfaces the specs propose, **only two are genuinely net-new** — the
`AgentGrant` entity (P3.1) and the MCP mutation-audit option. Everything else **folds into or extends
an existing primitive.** The agent-native program is ~80% *a new lens on primitives Koan already has*,
not new machinery — the strongest evidence it's the right shape, and the reassurance it is not
over-building. **Build = mostly extension; invention is the exception.** Dispositions marked
FOLD/BIND/EXTEND on a strong inference are *verify-at-build* (the cards are target shapes); the four
already grounded at `file:line` (`[McpApplication]`, the pin, `[McpAuth]`, edges) are firm.

**`[McpApplication]` specifically (the architect's catch, grounded):** don't create it. `[KoanApp]`
(assembly attribute, [KoanAppAttribute.cs](../../src/Koan.Core/Hosting/App/KoanAppAttribute.cs) —
`Name`/`Code`/`Description`/…) already carries app identity, resolved into `ApplicationIdentitySnapshot`
(precedence: `Koan:Application:*` config → `[KoanApp]` → assembly metadata → host env) and surfaced at
`KoanEnv.CurrentSnapshot.Application` + Provenance. AN8's self-introduction *reads that*. Posture is
already `[McpDefaults]` + `McpServerOptions.Exposure`. Only the per-app mutation-audit toggle is new,
and it belongs in `McpServerOptions`, not a new attribute.

---

## §14 Addendum A — agent-side ergonomics, harvested

A third companion (Addendum A) folds in field reports from agents operating *on the far side of the
seam*. Its meta-principle is the §13 fold map applied recursively by the spec itself: *"almost
nothing here is a new mechanism — it is the one projector pointed at new questions"* (dry-run =
projection of a *hypothetical* action; delta = of a *completed* one; did-you-mean = of a *constraint
at the error*; etag = the projection's *fingerprint*; depth = *variable verbosity*; volume =
*respecting memory*). The two lines it draws — `depth` controls verbosity never *visibility*; an
agent-issued id is a *label* never a *key* — are the program's master invariants (visibility and
authority were never the agent's to set). **Verdict: adopt all ten — but grounded, that is ~1
already-built, several folds, and ~2 genuinely-new builds.**

**Grounded fold map (A1–A10):**

| Item | What it adds | Grounded status | Disposition |
|---|---|---|---|
| **A1** Universal dry-run | `dry_run:true` on every mutating verb → prospective delta, commits nothing | NET-NEW (no validate-only path) | **AN11** — the build; rehearsability is capability-graded (A10) |
| **A2** State deltas | mutation returns a semantic diff (same shape as A1) | NET-NEW for general entities (Canon has the per-field `Previous/CurrentValue` primitive) | **AN11** — scope v1 to payload-touched fields (avoid a full before-snapshot) |
| **A3** "Did you mean?" | validation errors project a correction from **schema only** (enums/types/required), never rows | partial (`SchemaBuilder` is projectable) | **AN11** — schema-not-rows IS walled-means-silent (#6) on the error channel |
| **A4** Volume projection | page size stated in the contract; cursor-as-edge | **ALREADY-HAVE** — `X-Total-Count`/`X-Total-Pages`/`[Pagination]` policy | **AN8** — only surface the existing contract in the MCP tool result; **skip cursor-as-edge** (offset+count is more informative to an agent) |
| **A5** ETag heartbeat | `If-None-Match` on `koan://self`; etag = hash of grant-specific projection | NET-NEW for entities; **media has a complete ref impl** (`StorageMediaController`/`MediaController`) | **AN8** — reuse the media ETag pattern; grant-specific hash makes stale-greeting-fresh cheap |
| **A6** `?depth=` | density ladder `menu\|schema\|full`; verbosity never visibility | PARTIAL (`?shape=`/`?view=` exist; arbitrary `?fields=` deliberately unwired) | **AN8** — bounded; the "no query language" bound is already Koan policy (GraphQL cut) |
| **A7** Agent-issued id | agent generates+threads the correlation id globally; opaque/untrusted/authority-free | nearly true (MCP `correlationId` already caller-supplied + powerless) | **AN9** — sharpen: client-OWNED; never validated/dedup-for-trust/grant-associated; **`device_code` stays server-issued** (#13/#20) |
| **A8** Goal doors | optional `Goal` (why) beside `Door` (how) — value, never pitch | NET-NEW small (doors are AN5) | **AN5** — optional, honesty-leashed |
| **A9** Side-effect descriptors | verb prose for create-vs-overwrite-vs-draft / idempotency — the "type can't say" case | clarification (no mechanism) | **AN4** — the human-readable half pairing AN4's machine `readOnly/destructive/idempotent`; `[McpDescription]` carries it |
| **A10** External-effect dry-run | declarative external effects + honest partial-rehearsal; saga = v3 | design constraint on A1 | **AN11** — the rehearsability gate; parallels filter-pushdown ("rehearse only what you can declare" ≈ "push down only what lowers to SQL", ARCH-0084) |

New invariants **#14–#20** and tests **T12–T18** ride their dispositions (#14 dry-run-everywhere ·
#15 delta-as-response · #16 errors-from-schema-not-rows · #17/#19 depth-verbosity-not-visibility ·
#18 etag-grant-specific · #20 client-id-opaque-untrusted).

**The two lines to hold** (both already master invariants): `depth` never changes *what exists* (else
diffing depths enumerates walls); an agent-issued id never gates anything (else continuity becomes
authority — the `device_code`/pin separation, #13). And the honesty gate: a dry-run that silently
rehearses only half a verb's effects is **worse** than none — A10's "name the wall of the sandbox" is
what makes A1 trustworthy.

**Net-new build = AN11 (dry-run + state-delta + did-you-mean), gated by the A10 rehearsability
discipline.** Everything else folds into AN4/AN5/AN8/AN9 — the same concept-budget win as §13.

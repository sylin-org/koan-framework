# ARCH-0092: Entity exposure surfaces — terse attributes, realization classes, and the unified access floor

**Status**: Accepted (2026-06-19) — direction signed off by the Enterprise Architect. Two forks resolved at sign-off: **full `IAuthorize` unification, no additive MCP-only bridge**; and **`EntityToolset<T>`** (not `EntityMcpController`/`EntityMcpToolset`). **Phase 1 (EntityToolset realization) landed 2026-06-19** (`7d055b3c` hierarchy + toolset discovery · `af01f0b7` `[McpTool]` instance verbs · `c76c741f` `[ToolDescription]`/`[ToolHidden]`) — additive, conformance-green. **Phase 2 (`[RestEntity]` terse REST exposure) landed 2026-06-19** (`90dcede0`) — additive, over the existing `GenericControllers` machinery, with explicit-controller precedence; Web.Extensions e2e 16 green, MCP conformance 39 green. The `[McpEntity]` access-config demotion is **held for Phase 3** (it needs the unified floor to receive `RequiredScopes` first). **Phase 3 design resolved 2026-06-19** to a *consolidation* (not the original "two entry points"): relocate the seam abstraction down to `Koan.Web`, gate REST+MCP at one point in the shared `EntityEndpointService`, hard-cut `CanRead/CanWrite/CanRemove`, and dissolve the dormant `IAuthorizeHook` + reduce `McpToolAccessPolicy` — see Decision D "Phase 3 resolved design." Phase 3 (4 slices) + Phase 4 pending.
**Date**: 2026-06-19
**Deciders**: Enterprise Architect
**Scope**: The entity-**exposure** model across the REST and MCP surfaces — how an `Entity<T>` becomes a RESTful resource and an MCP toolset, how access is declared, and where each concern lives. Establishes the framework pattern that P3.1 (governed agent access) builds on, and folds in the `X-mcp-rest-authz-unify` direction.
**Related**: [ARCH-0084](ARCH-0084-unified-capability-model.md) (the "one execution core, two faces" precedent) · [SEC-0002](SEC-0002-unified-authorization-model.md) (the `IAuthorize` seam — this ADR makes it the **canonical cross-surface evaluator**) · [WEB-0068](WEB-0068-query-options-predicates.md) (read-path visibility predicates, honored by both surfaces) · the agent-native program ([docs/assessment/09](../assessment/09-agent-native-projection.md), the AN cards) · **P3.1** (governed agent access — `AgentGrant`/`AgentAction` sit on this ADR's access floor).

---

## Context

Koan exposes an `Entity<T>` over REST with **`EntityController<T>`** ([src/Koan.Web/Controllers/EntityController.cs](../../src/Koan.Web/Controllers/EntityController.cs)) — a controller class that realizes the entity's CRUD/query verbs over HTTP, with room for custom actions, content negotiation, and per-action access. Over MCP it exposes the entity by annotating it **`[McpEntity]`** ([src/Koan.Mcp/McpEntityAttribute.cs](../../src/Koan.Mcp/McpEntityAttribute.cs)) — an attribute that triggers registry discovery and carries, on one attribute, exposure *and* access config (`RequiredScopes`, `AllowMutations`, `Exposure`, `RequireAuthentication`, …).

This produced two problems.

### 1. The conflation

`[McpEntity(Expose=…, Audit=…, RequiredScopes=…)]` jams three distinct concerns onto the entity attribute:

1. **Domain** — what the entity *is* (belongs on `Entity<T>`).
2. **Surface realization** — *that* it's exposed over MCP, with what verbs and descriptions.
3. **Access invariant** — what authority a mutation *needs*.

REST already separates concern 1 from 2+3 (the controller). MCP overloads all three onto the entity. The original P3.1 card inherited this conflation by adding still more access knobs (`Expose`, `Audit`, grants) to the same attribute.

### 2. The asymmetry

REST exposure is **class-driven** (`EntityController<T>`); MCP exposure is **attribute-driven** (`[McpEntity]`). A developer learns two different shapes for the same act ("expose this entity here").

### Grounding (what's actually true today)

- **One execution core already exists.** `EntityController<T>` and the MCP path both run through `IEntityEndpointService<T,K>` ([src/Koan.Web/Endpoints/EntityEndpointService.cs](../../src/Koan.Web/Endpoints/EntityEndpointService.cs)) with the same WEB-0068 predicates and hook pipeline. "One core, two faces" (ARCH-0084) is already the literal structure.
- **The generic-controller machinery already exists.** [GenericControllers](../../src/Koan.Web.Extensions/GenericControllers/GenericControllers.cs) materializes a closed generic controller over an entity type (`IApplicationFeatureProvider<ControllerFeature>` + a route convention) — in production for the capability controllers. An attribute-driven REST exposure (`[RestEntity]`) is a thin convention over machinery that exists, **not** a new ASP.NET integration.
- **Neither surface routes base CRUD through the unified seam.** `EntityController<T>` gates with custom `CanRead`/`CanWrite`/`CanRemove` virtuals ([EntityController.cs:49-51, 169](../../src/Koan.Web/Controllers/EntityController.cs)); the SEC-0002 `IAuthorize` seam is invoked only by `[RequireCapability]` on capability controllers; `UseAuthorization` is registered but **inert for entity endpoints**. The MCP edge gates only via `McpToolAccessPolicy` (scope claims) ([src/Koan.Mcp/Execution/McpToolAccessPolicy.cs](../../src/Koan.Mcp/Execution/McpToolAccessPolicy.cs)); it honors no standard attribute. So **`[Authorize]` on an entity is a silent no-op on both base CRUD and MCP today.**
- **ASP.NET has no native scope primitive.** OAuth scopes are modeled as policies / claim requirements. MCP `RequiredScopes` is checked directly as scope claims.

### Forces

1. **Reference = Intent.** The terse path (annotate-and-go) must survive — it's Koan's ergonomic core and the agent-native "the app projects itself" pitch.
2. **Description must not drift from enforcement.** The agent-native program's master law (09 §2.1, §9.1): "the thing that enforces a boundary and the thing that describes it must be the same projection, or they drift — and a drifting security boundary lies." An attribute that *appears* to gate a surface but is silently ignored there is that drift.
3. **Concept budget** (the "fewer but more meaningful parts" redesign discipline). Prefer dissolving/standardizing concepts over minting them.
4. **Preserve the agent-native plumbing (AN1–AN11).** Schema projection, annotations, dry-run, edges, resources, and the access policy all read `McpEntityRegistration`/`McpToolDefinition`. The reshape must not invalidate them.

---

## Decision

### A. Three orthogonal concerns, finally unbundled

| Concern | Home |
|---|---|
| Domain shape / relationships / lifecycle | `Entity<T>` |
| **Access invariant** (the cross-surface floor: "needs auth / role / policy / scope; mutation-gated") | entity-level, transport-agnostic → the **`IAuthorize` seam** (SEC-0002) |
| **Exposure + realization** (per surface, two interchangeable modes) | terse attribute **or** realization class |

### B. Exposure: terse attribute ↔ realization class, symmetric per surface

| | Terse (auto-registers the generic realization) | Explicit (subclass — override + extend) |
|---|---|---|
| **REST** | `[RestEntity]` *(new, thin over GenericControllers)* | `EntityController<T>` *(exists)* |
| **MCP** | `[McpEntity]` *(exists, reduced to pure exposure)* | `EntityToolset<T>` *(new)* |

Both modes materialize the **same realization over the same `IEntityEndpointService`**. The terse attribute auto-registers the open generic; the subclass is what you write when you need custom verbs, per-method descriptions, or surface-specific tightening. **Precedence:** an explicit realization class for a given entity+surface wins; the terse attribute for that pair is ignored (and may warn). This is the ASP.NET conventional-routing-beside-controllers pattern — progressive disclosure, **one controller, two registration paths** (no parallel implementation).

The terse path is *simple but fully featured*: because both modes ride the one core, the attribute inherits the entire governed surface (filtering, pagination, parent/child expansion, WEB-0068 visibility, dry-run, edges). The control path adds *authoring power*, not *features*.

### C. Naming: `EntityToolset<T>` is the MCP realization

The MCP realization is **`EntityToolset<T>`**, paired with `EntityController<T>`. The pairing is honest at the type level, not cosmetic: both are **containers of the same operation set** (the 12 `EntityEndpointOperationKind` verbs) over the same `IEntityEndpointService` — a controller realizes them as REST actions, a toolset realizes them as MCP tools. *Controller : actions :: Toolset : tools, same verbs underneath.*

"Controller" is rejected for the MCP class: `EntityController<T>` is literally `: ControllerBase` with `[ApiController]` + ASP.NET routing; the MCP realization is a registry-discovered class whose `[McpTool]` verbs are bound by reflection and dispatched over JSON-RPC. Naming it `*Controller` asserts MVC kinship the type does not have. Every comparable framework uses a **surface-native noun** for non-HTTP realizations (Orleans `Grain`, gRPC `Service`, Hot Chocolate `Type`, MediatR `Handler`); the **official MCP C# SDK** groups verbs in `[McpServerToolType]` classes — the canonical noun is *tool/toolset*, never controller.

`EntityToolset<T>` carries no `Mcp` prefix **because its peer `EntityController<T>` carries no `Web`/`Rest` prefix** — the symmetry is structural; adding `Mcp` would break the very rhyme it relies on. ("Toolset" is a Koan coinage — the MCP spec says "tools"; it names the *collection* an entity produces, which "tool" singular does not.) Non-entity tool collections are a bare **`Toolset`**; `EntityToolset<T>` is the entity-bound specialization.

### D. Access floor: the `IAuthorize` seam is the single cross-surface evaluator

**Full unification — no additive MCP-only bridge.** The SEC-0002 `IAuthorize` seam becomes the **canonical decision engine that every surface calls**: REST base CRUD is routed through it (replacing the inline `CanRead`/`CanWrite`/`CanRemove` path), and the MCP edge calls it (replacing the `McpToolAccessPolicy`-only path).

**Phase 3 resolved design (2026-06-19) — refines the original "two entry points."** Grounding for Phase 3 surfaced that base-CRUD/MCP authz is *four* parallel mechanisms, not one seam with two faces: the `CanRead/CanWrite/CanRemove` virtuals (REST-only, live), a **dormant `IAuthorizeHook` pipeline step** (`HookRunner.Authorize`/`DefaultEntityHookPipeline.Authorize` — *zero callers*), the SEC-0002 `IAuthorize` seam (live, but only on capability controllers), and `McpToolAccessPolicy` (MCP-only, live). And the decisive layering fact: **`IAuthorize` lives in `Koan.Web.Extensions`, which `Koan.Mcp` does not reference and which sits *above* `Koan.Web`** — so the "canonical evaluator" is in a package *neither* shared entry point can call. Reviving the dormant hook would make it five parts, not fewer. The resolved design is a consolidation, not a revival:

- **Relocate the seam *abstraction*** (`IAuthorize`, `AuthorizeRequest`, `IAuthorizationProvider`, `AuthorizeOptions`, `Authorizer`) **down to `Koan.Web`** — the lowest common ancestor of the two real consumers (the shared `EntityEndpointService` and `Koan.Mcp`). The ASP.NET-bound *providers* (RBAC, named-policy) stay where their deps live and register into the ladder. (Abstraction sinks; implementations don't. The Core-ward move that would serve jobs/bus is deferred until a jobs/bus consumer actually exists — dogfood-driven, ≥2 usages.)
- **One evaluation point, not two** — the seam call lives *inside the shared `IEntityEndpointService`*, keyed by operation→action (read/write/remove). REST and MCP are gated by the *same call*, once; no per-surface filter copy. The `Koan-Access-*` headers are recomputed from a seam pre-check (honest capability advertisement, not a removed feature).
- **Dissolve the redundancy:** delete the dormant `IAuthorizeHook` path (dead code + a second `AuthorizeRequest` type); reduce `McpToolAccessPolicy` to a thin edge call into the seam; **hard-cut `CanRead/CanWrite/CanRemove`** (the REST-only knob is replaced by the entity floor — no compat rung; settle it while greenfield).

**Developer-delight axis (standing criterion):** every choice is tested against "does a developer learn *one* honest thing, or another surface-specific dialect?" The end-state is *declare once on the entity* (`[Authorize]`/`[AllowAnonymous]`/`[RequireScope]`), enforced identically on REST and MCP, provably (the TestKit honesty proof) — no `CanWrite`-REST-only, no `McpToolAccessPolicy`-MCP-only, no `RequireCapability`-only, no dead hook.

Declarations reuse standard .NET where it is honest:

- **`[Authorize]` / `[AllowAnonymous]`** for authentication / roles / named policies — standard `Microsoft.AspNetCore.Authorization`, no Koan tags.
- **`[RequireScope("items:write")]`** — the *one* new primitive, for the OAuth-scope requirement ASP.NET cannot express. It maps to a scope-claim requirement evaluated by the seam (or, equivalently, the `[Authorize(Policy="scope:items:write")]` convention). Scope is the only gap worth a new attribute; everything else is standard.

**The honesty invariant:** a declared requirement must be enforced on *every* surface that exposes the entity, or not declared at all. Reusing `[Authorize]` is honest **only because** the seam — not ASP.NET middleware alone — is the evaluator on all surfaces. A surface that silently ignores a declared requirement is the drift this ADR (and the whole agent-native program) exists to prevent.

**The terse exposure attributes carry no access config.** `[RestEntity]`/`[McpEntity]` say "project this entity here," nothing about who may touch it. Access lives on the floor. This is the conflation-fix; if access creeps back onto `[McpEntity]`, the fix is undone.

### E. `[McpEntity]` is demoted to pure exposure

`[McpEntity]`'s current access config (`RequiredScopes`, `AllowMutations`, `RequireAuthentication`) moves to the access floor (`[RequireScope]`/`[Authorize]` + the toolset's per-method control). Its identity/exposure role (`Name`, `Description`, `Exposure`, transports) stays. **Breaking**, signposted: a major-version change with a migration note and a boot warning; the dogfood sample (S16.PantryPal) is updated in the same change.

### F. P3.1 sits on the floor

`AgentGrant`/`AgentAction` (P3.1, governed agent access) attach to the unified floor: an agent's grant is the scopes/claims it holds, evaluated by the seam; the mutation-gate and audit are floor/toolset concerns, not entity-attribute concerns. P3.1 is no longer a parallel authz axis — it's a tier on the one evaluator. **A grant may target a *toolset*, not only an entity type** (see "Strategic surface" §3) — design `AgentGrant` for toolset-grained grants up front so grants are not built twice.

### G. AN1–AN11 are preserved

`McpEntityRegistration`/`McpToolDefinition` gain a second population source (`EntityToolset<T>` discovery) beside the `[McpEntity]` attribute. All agent-native projection machinery (schema, annotations, dry-run, edges, resources, the access policy — now delegating to the seam) reads the registration unchanged.

### H. `EntityToolset<T>` authoring shape

The realization class mirrors `EntityController<T>`'s authoring model (empty subclass works; resolve the shared `IEntityEndpointService`; add your own verbs) with mechanisms honest to the MCP surface.

**Hierarchy** — a `Toolset` base (standalone, non-entity tools) and the entity specialization, with a single-arg convenience matching `EntityController`:

```csharp
public abstract class Toolset { }                              // a named bundle of [McpTool] verbs
public abstract class EntityToolset<TEntity, TKey> : Toolset   // + the 12 entity verbs as tools
    where TEntity : class, IEntity<TKey> where TKey : notnull { }
public abstract class EntityToolset<TEntity> : EntityToolset<TEntity, string> { }
```

**Zero-config** — `public sealed class OrderToolset : EntityToolset<Order> { }` exposes all 12 verbs as tools, each with a **template description** derived from the entity name (`"Search Orders…"`, `"Fetch one Order by id."`, …). Templates live at the **registration layer**, so the terse `[McpEntity]` path gets identical descriptions — the class only adds override power.

**Custom verbs** — `[McpTool]`-marked **instance** methods. The win over the static `[McpTool]` model is `this`-context (data access, the current principal/grant, helpers) and per-verb floor attributes:

```csharp
public sealed class OrderToolset : EntityToolset<Order>
{
    [McpTool("Fulfills an order: reserves stock, charges, and ships.")]
    [RequireScope("orders:fulfill")]
    public async Task<Order> Fulfill(string id, CancellationToken ct) { /* governed composition, audited as one tool */ }
}
```

**Built-in tuning** — operation-keyed **class attributes** (attribute-first; reuses the `[McpDescription(Operation=)]` precedent; no builder DSL):

```csharp
[ToolDescription(Operation.Query, "Search orders by status, customer, or date range.")]
[ToolHidden(Operation.Delete)]      // absolute removal — distinct from a per-grant Wall
public sealed class OrderToolset : EntityToolset<Order> { … }
```

**Two deliberate constraints** (divergences from `EntityController`, accepted):

1. **Built-in verbs are tune-only, not logic-overridable.** They always run the governed `IEntityEndpointService`; custom logic is a custom `[McpTool]` verb. `EntityController` permits replacing an action body (a known REST footgun) — `EntityToolset` keeps the governed core authoritative on the higher-stakes agent surface.
2. **Per-built-in-op access is the entity floor in v1** (per-op scope granularity is deferred). Built-ins are gated by the entity-level floor + the read/mutate split; only *custom verbs* carry their own `[RequireScope]`. `[ToolHidden]` is absolute removal; per-grant visibility is the Wall.

**Decisions recorded (2026-06-19):** custom-verb attribute stays **`[McpTool]`** (a globally-applied attribute keeps the prefix for call-site clarity, even though the *class* drops `Mcp`); built-in tuning is **attributes** (`[ToolDescription]`/`[ToolHidden]`), not a `Configure` builder; built-ins are **tune-only** (constraint 1). Net-new types this introduces: `Toolset`, `EntityToolset<TEntity[,TKey]>`, `[ToolDescription]`, `[ToolHidden]`, `[RequireScope]` (the access-floor scope primitive, §D).

---

## Consequences

**Positive**

- **One mental model** across REST and MCP: terse attribute → realization class, over one core, with access as a separate standard floor.
- **Honest description = enforcement on every surface** — the master law made structural; a requirement is evaluated by the same engine everywhere.
- **Concept-budget win**: `[McpEntity]` dissolves toward pure exposure; access becomes standard `[Authorize]`/`[AllowAnonymous]` + one `[RequireScope]`; the MCP-specific authz vocabulary retires. Closes `X-mcp-rest-authz-unify`.
- **P3.1 lands clean** on a settled floor instead of a conflated attribute.
- **Terse path stays fully featured** (same engine); control path is opt-in.

**Costs / negative**

- **Breaking changes**: the `[McpEntity]` access-config demotion; the REST base-CRUD authz refactor onto the seam (must not regress existing `CanRead`/`CanWrite` consumers — needs a compat/migration path).
- **New surface to build**: `EntityToolset<T>` (+ base `Toolset`), `[RestEntity]`, `[RequireScope]`, and the seam wiring on both entry points.
- **Two exposure modes to teach** per surface (mitigated: they're one realization, two registration paths).

**Risks**

- The access unification touches REST's *live* authorization path. The migration must preserve current behavior for apps relying on `CanRead`/`CanWrite` until they adopt the floor.
- The honesty invariant is load-bearing: every surface must invoke the seam, or `[Authorize]` becomes the silent-no-op lie again.

---

## Strategic surface — what the toolset unlocks (forward-looking, non-binding)

Making the agent-facing realization a **first-class, typed, composable unit over a unified core** is not only a tidier `[McpEntity]`; it keeps reachable a set of strategic moves this decision deliberately enables. These are opportunities the model *unlocks*, not commitments — recorded so the immediate design does not foreclose them.

1. **The exposure model is a surface factory.** `[McpEntity]` + `EntityToolset<T>` is a template — domain core + one thin realization + one terse attribute. The same shape yields new faces; the highest-value near-term is a **direct LLM function-calling surface** (the raw OpenAI/Anthropic tool-schema format, *not* MCP) — a small sibling realization that makes a Koan app callable by *any* function-calling model, not only MCP clients. GraphQL / gRPC / CLI follow from the same factory. **Positioning:** *declare your domain once; it is operable by every agent protocol, with a provably identical security boundary* — the identical-boundary guarantee is the differentiator the legible-governance gap leaves open. **Read:** the factory framing is free (it is this ADR); the direct-function-calling face is a near-term, high-ROI second surface.

2. **Toolsets as distributable capability units.** A bare `Toolset` is a named bundle of tools, therefore shippable — a vendor `Toolset` (a payments pack, a source-control pack, a domain pack) published as a package that any app references and instantly, *governed-ly* exposes to its agents. "Reference = Intent" extended from infrastructure to *agent capability*; the seed of a toolset ecosystem (third parties publish, apps compose). **Read:** a platform option, not a near-term build — but keep `Toolset` composable and packageable from the start so the option stays open.

3. **Toolset-granular governance.** Because access is one evaluator and toolsets are first-class, the grant unit can be the **toolset** ("this agent holds the read-toolset; that one the admin-toolset"), with per-toolset audit, metrics, cost, and revocation — agent capability *management* as a product surface (which agent holds which toolsets, usage, what it mutated). The Wall/Door/Verb model at a coarser, more operable grain. **Read (fold in now):** P3.1's `AgentGrant` should target a toolset, not only an entity type (Decision F).

4. **Toolsets are the composition unit for recipes and Code Mode.** A toolset method need not be CRUD — `EntityToolset<Order>.Fulfill(id)` composes several governed entity ops into one audited tool, so agents receive *intentions*, not just primitives. The toolset boundary is the correct **sandbox boundary for Code Mode** ([AI-0014](AI-0014-mcp-code-mode.md)): a script gets a toolset, and the toolset's access floor *is* its capability ceiling. **Read:** the deepest moat — where a toolset stops being "CRUD over JSON-RPC" and becomes the unit of *curated* agent capability.

5. **Provable per-toolset contracts.** A toolset is a typed class with a known shape (verbs + schemas + the AN1–AN11 projection), so a conformance proof per toolset (round-trips, dry-run matches commit, edges resolve, walls stay silent) attaches automatically — the `Koan.Mcp.TestKit` harness already does exactly this. "Every toolset ships with a proven honesty contract" turns the program's thesis (description = enforcement) into a pointable artifact. **Read:** cheap, and the credibility layer under the rest.

**The thread.** This model is the architectural realization of the agent-native thesis — *one honest projection, many faces* — and of the gap the industry leaves open (everyone ships an enforcement policy plus a separate, drifting explanation of it). The toolset is where that thesis becomes a unit developers hold and third parties extend. Two of these (§3 toolset-grained grants, §2 keep `Toolset` packageable) shape the *immediate* design and are flagged accordingly; the rest are reachable futures the model is built not to foreclose.

---

## Implementation (phased — green ratchet + dogfood between phases)

1. **`EntityToolset<T>`** over `IEntityEndpointService` (MCP realization) per the authoring shape in Decision H — base `Toolset`, the `<TEntity[,TKey]>` hierarchy, `[McpTool]` instance verbs, template descriptions at the registration layer, `[ToolDescription]`/`[ToolHidden]` tuning, tune-only built-ins; + registration from toolset discovery (Decision G).
2. ✅ **`[RestEntity]`** (`90dcede0`) terse attribute over the existing `GenericControllers` machinery — auto-registers a concrete `RestEntityController<TEntity,TKey>`, explicit-controller precedence, additive. *Reducing `[McpEntity]` to pure exposure moved to Phase 3* — the access-config demotion needs the floor to receive `RequiredScopes` first.
3. **Access-floor unification (the consolidation — see Decision D "Phase 3 resolved design").** Four reviewable, individually-green slices:
   - **3.1** Relocate the seam abstraction `Koan.Web.Extensions` → `Koan.Web` (providers stay); add `[RequireScope]` + a built-in scope provider; register the seam by default (no provider → default Allow = backward-compat).
   - **3.2** `EntityEndpointService` calls the seam per operation (read/write/remove); **hard-cut `CanRead/CanWrite/CanRemove`**; recompute `Koan-Access-*` headers from a seam pre-check. (Gates REST + MCP in one place.)
   - **3.3** MCP edge → seam (`McpToolAccessPolicy` delegates; tools/list filter + tools/call); migrate `[McpEntity(RequiredScopes/RequireAuthentication)]` → `[RequireScope]`/`[Authorize]` on the entity; demote `[McpEntity]`; update the S16.PantryPal dogfood.
   - **3.4** Delete the dormant `IAuthorizeHook` path (`IAuthorizeHook`, hook `AuthorizeRequest`, `HookRunner.Authorize`, `*.Authorize` pipeline members); keep `AuthorizeDecision`.
4. **P3.1** — `AgentGrant`/`AgentAction` on the floor (grants, audit, cache-coherence revocation).

## Explicitly deferred / out of scope

- **Read-surface default-to-wall** (flipping the *read* default closed, not just write) — a larger breaking change; its own decision after this model proves out.
- **Per-operation scope granularity** (a grant/scope scoped to specific verbs) — the floor supports it; the default grain stays per-entity-type read/mutate (P3.1).
- **Core-ward relocation of the seam** (so `Koan.Jobs`/`Koan.Messaging` authorize through it) — deferred from Phase 3, which lands the abstraction in `Koan.Web` (the LUB of the real consumers); move it down when a jobs/bus consumer actually exists. *(Supersedes the earlier "compat shim for `CanRead`/`CanWrite`" item — resolved to a hard-cut, no shim.)*

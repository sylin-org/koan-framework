# SEC-0002: Unified Authorization Model — one decision seam, declarative requirement sources, capability-graded providers

**Status**: **Proposed (2026-06-07)** — architect selected option **A** (a Koan-native graded seam that reuses ASP.NET `IAuthorizationService` as a provider) in design discussion. Not yet implemented.
**Date**: 2026-06-07
**Deciders**: Enterprise Architect
**Scope**: Collapse Koan's fragmented authorization mechanisms into **one** Koan-native decision seam (`IAuthorize`) fed by **declarative requirement sources** and backed by a **capability-graded provider ladder**. Elaborates SEC-0001 §8 (where authorization lives); generalizes and supersedes WEB-0047's capability resolution; supersedes the parallel `IAuthorize`/`RbacAuthorizer` sketch shipped in SEC-0001 Phase 2 (increment 2f).
**Related**: **SEC-0001 §8** (coarse-roles-in-token / fine-at-resource; the §8 provider ladder) · **WEB-0047** (capability authorization fallback + defaults) · **WEB-0049** (role attribution / claims transformation) · **ARCH-0084** (capability model + provider election — the grading pattern reused here) · `IAuthorizeHook<TEntity>` (entity pipeline) · `Koan.Security.Trust.Identity` (ambient principal, SEC-0001 2e) · the Koan redesign ("fewer but more meaningful parts").

---

## 1. Context — four ways to answer one question

A branch audit (SEC-0001 Phase 2) found authorization in Koan is answered by **four overlapping mechanisms**:

1. **ASP.NET `IAuthorizationService`** + named policies + `[Authorize(Policy=…)]` — the framework engine.
2. **`ICapabilityAuthorizer`** (WEB-0047) — maps `(entityType, capabilityAction)` → a named policy via a layered fallback (Entity → Defaults → `DefaultBehavior`) → calls #1. Gates the moderation / soft-delete / audit capability controllers.
3. **`IAuthorize` / `RbacAuthorizer`** — the resource-side seam sketched in SEC-0001 §8 and shipped as increment 2f. A role-check floor. **Currently has zero real consumers** (a parallel, speculative seam — a premature abstraction smell).
4. **`IAuthorizeHook<TEntity>`** — the per-entity pipeline hook returning `AuthorizeDecision`.

Four mechanisms for "may this subject perform this action on this resource?" is exactly the fragmentation the redesign exists to collapse. Increment 2f's attempt to "flip `CapabilityAuthorizer` through `IAuthorize`" exposed the problem: #2 (named-policy evaluation) is *richer* than #3 (role checks), so a naive flip would lose richness. The fix is not a flip — it is a single coherent model the others fold into.

---

## 2. Forces / principles

1. **One decision seam.** Exactly one thing answers the authorization question; everything else either *declares requirements* or *implements a decision strategy*.
2. **Separate WHAT from HOW.** "What is required" (declarative: roles / policy / capability) is distinct from "whether the principal meets it" (the engine). They are currently tangled in `CapabilityAuthorizer`.
3. **Reuse, don't reinvent.** ASP.NET `IAuthorizationService` is a mature, extensible policy engine. It becomes a **provider behind** the seam — not a competitor to delete or duplicate.
4. **Capability-graded providers (ARCH-0084).** The decision strategy is a ladder of self-describing, Reference = Intent providers: an in-process RBAC floor → a named-policy provider → external PDP/ReBAC adapters.
5. **Channel-agnostic.** The seam reads the ambient `Identity.Current` and a plain `(action, resource)` — no `HttpContext` dependency — so HTTP, the message bus, and jobs authorize through the *same* call.
6. **Terse, Koan-native surface.** Hide ASP.NET's policy/requirement/handler ceremony behind a Koan idiom, consistent with `Entity<T>`, `.Job`, `Identity.Current`.

---

## 3. Vocabulary (decided)

| Term | Meaning |
|---|---|
| **`IAuthorize`** | the single decision seam: `AuthorizeAsync(AuthorizeRequest) → AuthorizeDecision`. |
| **`AuthorizeRequest`** | `{ ClaimsPrincipal Subject, string Action, object? Resource, IReadOnlyDictionary<string,object?>? Context }`. `Subject` defaults to `Identity.Current` when omitted. |
| **`AuthorizeDecision`** | the existing `Koan.Web.Hooks` record — `Allow` / `Forbid(reason)` / `Challenge`. Reused as the one decision vocabulary. |
| **`IAuthorizationProvider`** | a graded decision strategy: `EvaluateAsync(AuthorizeRequest) → AuthorizeDecision?` where **`null` = "no opinion, defer to the next rung."** |
| **Requirement source** | anything that *declares* what an action needs and drives the seam: `[RequireCapability]`, `[Authorize]`, the capability→policy map, an `IAuthorizeHook<T>`. Declares; does not decide. |

---

## 4. Decision — the seam

```csharp
public interface IAuthorize
{
    Task<AuthorizeDecision> AuthorizeAsync(AuthorizeRequest request, CancellationToken ct = default);
}
```

- **Subject is ambient by default.** `AuthorizeRequest` may omit `Subject`; the seam fills it from `Identity.Current` (SEC-0001 2e). This is what makes one call work in HTTP, bus, and jobs.
- **The seam runs the provider ladder in order**, returns the **first non-`null`** provider decision, and if every provider defers, applies the configured **default behavior** (`Allow` | `Forbid`) — see §5.
- **`AuthorizeDecision` is the only result type**, so cookie and bearer principals, capability gates, and entity hooks all speak one vocabulary.

---

## 5. Decision — the capability-graded provider ladder

```csharp
public interface IAuthorizationProvider          // ARCH-0084-style graded capability
{
    int Order { get; }                            // lower runs first
    Task<AuthorizeDecision?> EvaluateAsync(AuthorizeRequest request, CancellationToken ct);
}
```

The default ladder (each a Reference = Intent rung; first definitive decision wins):

| Order | Provider | Decides by |
|---|---|---|
| 0 | **`RbacAuthorizationProvider`** (floor, no deps) | coarse roles from the token (`ClaimTypes.Role`) — SEC-0001 §8 Tier-0. |
| 100 | **`PolicyAuthorizationProvider`** | **delegates to ASP.NET `IAuthorizationService`** for a named policy resolved from the request's action (this is where WEB-0047's capability→policy map + Entity→Defaults fallback lives). Named-policy richness is **preserved**, not lost. |
| 200+ | **PDP / ReBAC adapters** (future, opt-in) | Cerbos / Cedar (ABAC) · OpenFGA / SpiceDB (ReBAC). Added by package reference. |

**The crucial generalization:** WEB-0047's fallback (Entity-mapping → Defaults → `DefaultBehavior`) is exactly a provider ladder with a default-behavior tail. So SEC-0002 **generalizes WEB-0047** — its semantics survive verbatim as the `PolicyAuthorizationProvider` + the seam's default-behavior fallback; only the *plumbing* unifies. `DefaultBehavior` (Allow/Deny) is the seam-level fallback when all providers defer.

**Provider election** reuses ARCH-0084 ordering (`[Order]`/priority), self-reporting each active rung in the boot report.

---

## 6. Decision — requirement sources funnel into the one seam

The existing declarative surfaces stay (no consumer churn at the controller level) but route through `IAuthorize`:

- **`[RequireCapability(entity, action)]`** → builds an `AuthorizeRequest{ Action = capabilityAction, Resource = entity }` and calls `IAuthorize`. `CapabilityAuthorizer`'s entity+action→policy mapping moves *into* `PolicyAuthorizationProvider`; `ICapabilityAuthorizer` becomes a thin shim over `IAuthorize` (kept for source compatibility, `[Obsolete]`-forwarding) or is removed once the attribute is rewired.
- **`[Authorize(Policy/Roles)]`** → unchanged at the boundary; the framework's middleware still runs, and `PolicyAuthorizationProvider`/`RbacAuthorizationProvider` are the same evaluators, so behavior is identical.
- **`IAuthorizeHook<TEntity>`** → reframed as an **entity-scoped requirement source**: the entity pipeline still invokes the hook, but its `AuthorizeDecision` is produced through (or short-circuits) the same seam, so the entity hook and the capability gate can no longer disagree.

---

## 7. The `IAuthorizationService` reconciliation (why nothing is lost)

The objection that stalled 2k — "we'd lose named-policy richness" — dissolves once `IAuthorizationService` is seen as **the backend of the `PolicyAuthorizationProvider`**, not a rival engine. Arbitrary ASP.NET policies/requirements/handlers keep working; they are simply reached *through* the one seam. Koan adds a terse, ambient, channel-agnostic front door; .NET keeps doing the policy evaluation it is good at.

---

## 8. Relationship to existing ADRs

- **SEC-0001 §8** — *elaborated.* §8 named the seam and the ladder at a high level; SEC-0002 fixes the concrete shapes (`AuthorizeRequest`, `IAuthorizationProvider`, the default ladder) and the requirement-source model.
- **WEB-0047** — *generalized / superseded mechanism.* The layered fallback semantics are preserved as a provider + default-behavior tail; `ICapabilityAuthorizer` as a standalone engine is retired. Mark WEB-0047 *Superseded by SEC-0002* on acceptance (its **posture** — allow/deny-by-default, per-entity overrides — remains canon).
- **SEC-0001 increment 2f** — *superseded.* The parallel `IAuthorize`/`RbacAuthorizer` sketch is reworked into this designed seam + `RbacAuthorizationProvider`. (Until implemented, 2f's seam is a sketch, not canon — flagged in code.)
- **WEB-0049** — role attribution / claims transformation feeds the principal the `RbacAuthorizationProvider` reads; unchanged.

---

## 9. Consequences

**Positive**
- Four authorization mechanisms collapse to **one seam + one provider interface + the existing declarative sources** — a genuine concept-reduction (the redesign bar), driven by ≥2 real consumers (the moderation/soft-delete/audit gates) rather than speculation.
- The PDP/ReBAC story (SEC-0001 §8 Tiers 1–2) becomes a clean `IAuthorizationProvider` adapter point — Reference = Intent, no core change.
- Authorization is identical across HTTP, bus, and jobs (ambient subject), which the fabric needs for cross-channel work.
- No loss of ASP.NET policy expressiveness.

**Negative / costs**
- A real (if focused) refactor touching `Koan.Web.Extensions.Authorization` and the capability controllers; must keep WEB-0047 behavior bit-for-bit (covered by the existing capability specs + new seam specs).
- One more Koan abstraction over .NET's — justified only because it *removes* three others and adds the graded-provider + channel-agnostic value .NET's seam doesn't give terse.

**Risks**
- Behavior drift in the capability gates during the move → mitigation: characterization tests on `CapabilityAuthorizer` *before* moving its logic into `PolicyAuthorizationProvider`; both green simultaneously before the old path is removed (the SEC-0001 copy-then-verify-then-delete discipline).

---

## 10. Alternatives considered

1. **(B) Lean on `IAuthorizationService` directly; delete `IAuthorize`.** Most idiomatic .NET, fewest new types. *Rejected:* the policy/requirement/handler model is ceremony-heavy and HTTP-flavored; you would build terse Koan helpers + a channel-agnostic front anyway — i.e. half of (A) — and lose the uniform graded-provider ladder. Architect chose (A).
2. **Naive 2k flip (`CapabilityAuthorizer` → `RbacAuthorizer`).** *Rejected:* loses named-policy richness; conflates the WHAT (capability mapping) with the HOW (decision).
3. **Defer indefinitely.** *Rejected:* leaves the four-mechanism fragmentation and an orphan 2f seam — debt, not resolution.

---

## 11. Phased plan (effort-sized; copy-then-verify-then-delete)

| # | Step | Size | Notes |
|---|---|---|---|
| 0 | Characterization specs pinning current capability-gate behavior (allow/deny-by-default, per-entity override, the moderation/soft-delete/audit actions) | **S** | Safety net before any move. |
| 1 | Rework the seam: `AuthorizeRequest` (+ ambient subject) and `IAuthorize.AuthorizeAsync`; `IAuthorizationProvider` | **M** | Replaces 2f's `IAuthorize`/`RbacAuthorizer` shapes. |
| 2 | `RbacAuthorizationProvider` (port 2f's RBAC floor) | **S** | |
| 3 | `PolicyAuthorizationProvider` — absorb `CapabilityAuthorizer`'s entity+action→policy map (WEB-0047) and delegate to `IAuthorizationService` | **M** | Both old + new green simultaneously. |
| 4 | Rewire `[RequireCapability]` + the capability controllers to call `IAuthorize`; `ICapabilityAuthorizer` → thin shim / removed | **M** | No controller-surface churn. |
| 5 | Reframe `IAuthorizeHook<TEntity>` as a source through the seam | **S** | |
| 6 | Seam specs + remove the dead old paths | **S** | |
| — | PDP/ReBAC adapters (Cerbos/Cedar, OpenFGA/SpiceDB) | **L** | Future, opt-in, Reference = Intent. |

---

## 12. Open questions

1. **`ICapabilityAuthorizer` fate** — keep as an `[Obsolete]` forwarding shim for one release, or remove now? (Greenfield argues remove; a shim eases any out-of-tree consumer.)
2. **Async seam vs sync** — providers may be async (PDP calls); the seam is async. Confirm the capability-gate call sites tolerate async (they currently call `.GetAwaiter().GetResult()` — that sync-over-async is itself debt to remove here).
3. **Resource typing** — `Resource` as `object?` (flexible) vs a typed envelope. Start `object?`; revisit if PDP adapters want structure.
4. **Where the seam lives** — `Koan.Web.Extensions` (where the capability authz is) vs a lower assembly so bus/jobs can consume it without a web reference. Likely a small `Koan.Security.Authorization` (or into `Koan.Security.Trust`) so non-web channels reach it. **Decide at impl step 1.**

---

## 13. References

- **SEC-0001** (fleet identity & trust fabric) §8 · **WEB-0047** (capability authorization) · **WEB-0049** (role attribution) · **ARCH-0084** (capability model + provider election).
- ASP.NET Core authorization (policies / requirements / handlers): https://learn.microsoft.com/aspnet/core/security/authorization/
- Policy-as-code / ReBAC providers for the future ladder: Cerbos · AWS Cedar · OpenFGA · SpiceDB (per SEC-0001 §8).

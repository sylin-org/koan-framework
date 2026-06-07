# WEB-0069: Web Pipeline Contributors — a position-aware seam for module-contributed middleware

**Status**: **Accepted (2026-06-07)** — architect-approved. Generalizes the `IPostAuthenticationContributor` introduced as the SEC-0001 dev-identity fix.
**Date**: 2026-06-07
**Deciders**: Enterprise Architect
**Scope**: Replace the ad-hoc ways modules inject middleware into Koan's web pipeline with **one** supported, ordering-safe, position-aware seam owned by `KoanWebStartupFilter`.
**Related**: SEC-0001 §4 (dev identity — the bug that motivated this) · CORE-0091 (initializer ordering) · `KoanWebStartupFilter` · `KoanWebAuthStartupFilter` · Koan.Mcp endpoint mapping.

---

## 1. Context — three ad-hoc ways to inject middleware, one of them broken

`KoanWebStartupFilter` is the **single owner** of Koan's canonical web middleware order: exception/headers → routing → authentication → authorization → endpoints. Modules legitimately need to slot middleware (or endpoint mappings) into that order at specific positions. Today they do it three incompatible ways:

1. **A competing startup filter.** The zero-config dev identity inserted `UseKoanDevIdentity` from a *second* `IStartupFilter` (`KoanWebAuthStartupFilter`). Because that filter is `[After]` Koan.Web's registrar, it is the *inner* filter; `KoanWebStartupFilter` (outer) builds a **terminal** `UseEndpoints` segment, so the inner filter's middleware ran *after* the endpoints — i.e. **never**. The dev identity was dead in real apps (caught by the SEC-0001 HTTP e2e suite — ARCH-0079).
2. **A reflection hack.** MCP endpoints are mapped from inside `KoanWebStartupFilter` via `Type.GetType("Koan.Mcp…").GetMethod("MapKoanMcpEndpoints")` — because Koan.Web cannot reference Koan.Mcp.
3. **A one-off interface.** The dev-identity fix added `IPostAuthenticationContributor` for exactly one position.

Three mechanisms, one of which silently failed. The root problem is the **absence of a single, position-aware contribution seam**: modules are forced to fight `IStartupFilter` registration order, which is not guaranteed.

---

## 2. Decision

Introduce **one seam, two shapes** (middleware vs. endpoint mapping are genuinely different injection points), owned and invoked by `KoanWebStartupFilter`:

```csharp
// Middleware, at a named stage of the pipeline.
public interface IKoanWebPipelineContributor
{
    KoanWebPipelineStage Stage { get; }
    int Order => 0;                       // tie-break within a stage (lower first)
    void Configure(IApplicationBuilder app);
}

public enum KoanWebPipelineStage
{
    BeforeRouting,        // exception handling, request context, secure headers
    AfterAuthentication,  // dev identity, claims enrichment   ← the SEC-0001 hook
    AfterAuthorization,   // post-authz auditing / tenancy assertions
}

// Endpoint mapping, inside the single UseEndpoints block.
public interface IKoanEndpointContributor
{
    int Order => 0;
    void Map(IEndpointRouteBuilder endpoints);
}
```

`KoanWebStartupFilter` resolves both from DI and runs them at the right boundary:

| Boundary | What runs |
|---|---|
| before `UseRouting()` | `IKoanWebPipelineContributor` where `Stage == BeforeRouting` |
| after `UseAuthentication()` | `… Stage == AfterAuthentication` |
| after `UseAuthorization()` | `… Stage == AfterAuthorization` |
| inside `UseEndpoints` (after `MapControllers`) | every `IKoanEndpointContributor` |

Contributors within a boundary run ordered by `Order`. **`KoanWebStartupFilter` remains the sole owner of the canonical order** — contributors fill named slots in it, they do not register competing filters.

---

## 3. Why two interfaces (and why named stages, not numeric order)

- **Middleware vs. endpoints** take different builders (`IApplicationBuilder` vs. `IEndpointRouteBuilder`) and live at different points (the endpoint stage is *inside* `UseEndpoints`). One interface can't honestly model both; forcing it would mean a contributor calling `app.UseEndpoints(...)` itself, producing multiple endpoint blocks. Two small interfaces are clearer than one leaky one.
- **Named stages** (prior art: the OWIN / IIS integrated-pipeline *stages*) are self-documenting and stable. A numeric "order" across the whole pipeline is opaque and brittle (every module guessing magic numbers). The stage set is deliberately small and tied to Koan's actual pipeline shape.

---

## 4. Migration

| From | To |
|---|---|
| `IPostAuthenticationContributor` (Koan.Web) | **removed** — replaced by `IKoanWebPipelineContributor` |
| `DevIdentityContributor` (Koan.Web.Auth) | implements `IKoanWebPipelineContributor`, `Stage = AfterAuthentication` |
| MCP reflection in `KoanWebStartupFilter` | **removed** — `McpEndpointContributor : IKoanEndpointContributor` in Koan.Mcp (which already references Koan.Web) |
| dev-identity insertion in `KoanWebAuthStartupFilter` | already removed (SEC-0001 fix); the filter remains only as the auth-middleware provider for the non-`AutoMapControllers` path |

---

## 5. Consequences

**Positive**
- One supported, discoverable, ordering-safe way to extend the pipeline — the class of bug that killed the dev identity cannot recur (no module depends on startup-filter order).
- A **net reduction** in mechanisms: the reflection hack and the one-off post-auth interface both collapse into this seam (consolidation, not addition — passes the redesign bar; ≥2 real consumers today: dev identity + MCP).
- Koan.Web sheds a reflection dependency on Koan.Mcp's type name.

**Negative / risk**
- Touches `KoanWebStartupFilter` (core, broad blast radius). Mitigation: the contributor loops are additive and a no-op when no contributor is registered; the SEC-0001 e2e suite + the MCP sample tests guard the two real consumers; full web + bootstrap sweeps before commit.
- `BeforeRouting` and `AfterAuthorization` ship without a consumer. Justified: they complete a small, coherent stage set for a routing→authn→authz pipeline (not speculative breadth), and cost one empty `foreach` each.

---

## 6. Alternatives considered

1. **Fix the startup-filter ordering instead.** Brittle — relies on registration order that any future module can perturb; doesn't address the MCP reflection.
2. **Reflection for everything** (extend the MCP pattern). Untyped, undiscoverable, and smelly for first-class features like identity.
3. **A single contributor interface with a position enum including `Endpoints`.** Rejected — the endpoint stage needs `IEndpointRouteBuilder`; a single `Configure(IApplicationBuilder)` can't map endpoints cleanly.

---

## 7. References

- `src/Koan.Web/Hosting/KoanWebStartupFilter.cs` · `src/Koan.Web/Hosting/IKoanWebPipelineContributor.cs` · `IKoanEndpointContributor.cs`
- SEC-0001 §4 (the dev-identity wiring bug this generalizes) · ARCH-0079 (integration tests as canon — how it was caught)
- OWIN/IIS integrated pipeline stages (prior art for named stages)

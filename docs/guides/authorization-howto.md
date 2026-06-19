# Authorization How-To

Koan answers one question — *may this subject perform this action on this resource?* — through a single
seam, `IAuthorize`, backed by a capability-graded ladder of providers ([SEC-0002](../decisions/SEC-0002-unified-authorization-model.md)). On top of that seam, **entity surfaces** are authorized with the developer-facing **gate · constrain · project** model ([SEC-0004](../decisions/SEC-0004-capability-authorization-gate-constrain-project.md)) — start there for everyday entity authz; drop to the raw seam (below) for custom actions, capability gates, or a PDP/ReBAC provider.

> Decision records: [SEC-0004 — capability authorization (gate · constrain · project)](../decisions/SEC-0004-capability-authorization-gate-constrain-project.md) · [SEC-0002 — unified authorization seam](../decisions/SEC-0002-unified-authorization-model.md).
> For sign-in / identity, see [authentication-setup.md](./authentication-setup.md).

## Entity surfaces: gate · constrain · project (SEC-0004)

The everyday path. One model authorizes an `Entity<T>` identically on REST **and** MCP, composable from nothing up — mark the entity and it just works; add a layer only when you need it.

| You write | You get |
|---|---|
| nothing | open (allow-by-default) — "just works" |
| `[Access(...)]` on the entity | a coarse per-action RBAC **gate** |
| an `EntityAccess<T>` realization (`Owner` + `Constrain`) | fine-grained, resource-aware **rows** (+ create-stamp, mass-delete bound) |

**Gate** — *who may touch this entity at all, per action.* A token bag per action; allow-by-default (an unspecified action is open). Legacy `[Authorize]` / `[AllowAnonymous]` / `[RequireScope]` lower into the same gate as sugar.

```csharp
// read open, write needs sign-in, remove needs the admin role. Tokens: anyone | authenticated | is:role |
// has:scope:x | has:role:y | has:claim:t=v | owner — comma-separates as OR; the row layer stays typed.
[Access(read: "anyone", write: "authenticated", remove: "is:admin")]
public sealed class Ledger : Entity<Ledger> { public string Entry { get; set; } = ""; }
```

**Constrain** — *which rows, per action* (the query transform: the rule that authorizes one row IS the filter that scopes the collection). Declare `Owner` once; the same `Constrain` filters the list, 404s an out-of-scope fetch, bounds a mass delete, and **stamps** the owner on create (server-truth — a forged owner id cannot escalate).

```csharp
public sealed class MemoAccess : EntityAccess<Memo>
{
    protected override Expression<Func<Memo, bool>>? Owner => m => m.OwnerId == CurrentUserId;

    public override IAccessFilter<Memo> Constrain(IAccessFilter<Memo> q, AccessAction action) => action switch
    {
        AccessAction.Create => q.Stamp(m => m.OwnerId, CurrentUserId),                  // no row yet → stamp
        AccessAction.Update => q.Where(Owner!).Stamp(m => m.OwnerId, CurrentUserId),    // own rows; freeze owner
        _ => q.Where(Owner!),                                                           // read/delete: own rows
    };
}
```

**Project** — *what you may actually do, per item* (the honesty counterweight to the open default). Every response advertises the realized `can:[...]` verbs: a single item → the `Koan-Access: read, write` header; a collection → an opt-in `{ items, access: { "<id>": { can: [...] } } }` sidecar (`?access=true`); MCP → per-item `can` in the tool metadata, **default-on**. The per-row `can` is free — gate-result-per-verb ∩ whether the row satisfies that verb's `Constrain`.

The rest of this guide is the **seam beneath** the entity model — use it for custom (non-entity) actions, the generic capability gates, and adding your own provider rung.

## The model in one picture

```
[RequireCapability] / [Authorize] / IAuthorize.AuthorizeAsync(...)   ← requirement sources (the WHAT)
                              │
                          IAuthorize  ← the one decision seam (subject defaults to Identity.Current)
                              │  runs providers in Order; first non-null wins; else AuthorizeOptions.DefaultDecision
        ┌─────────────────────┼───────────────────────────────┐
  RbacAuthorizationProvider   PolicyAuthorizationProvider       (your) PDP / ReBAC provider
   (Order 0, role floor)      (Order 100, named policies via    (Order 200+, opt-in)
                               ASP.NET IAuthorizationService)
```

The decision is always an `AuthorizeDecision`: `Allow` · `Forbid(reason)` · `Challenge`.

## Turn it on

```csharp
services.AddKoanAuthorization(); // registers the seam + RBAC floor + policy provider + the capability gates
```

`AddCapabilityAuthorization(...)` also registers the seam, so the capability controllers enforce whenever
capability authz is configured.

## Capability gates (moderation / soft-delete / audit)

The generic capability controllers are gated with `[RequireCapability]`, which routes through `IAuthorize`.
Configure who may do what with the WEB-0047 resolution (Entity → Defaults → DefaultBehavior):

```csharp
services.AddKoanAuthorization(configureCapabilities: caps =>
{
    caps.DefaultBehavior = CapabilityDefaultBehavior.Deny;              // strict: only mapped actions allowed
    caps.Defaults.SoftDelete.Delete = "can-delete";                    // map an action to an ASP.NET policy
    caps.Entities["Article"] = new CapabilityPolicy                     // per-entity override
    {
        Moderation = { Approve = "is-editor" }
    };
});
```

`"can-delete"` / `"is-editor"` are ordinary ASP.NET authorization policies (`AddAuthorization(o => o.AddPolicy(...))`).
The `PolicyAuthorizationProvider` evaluates them through `IAuthorizationService` — so your existing policies,
requirements, and handlers keep working unchanged.

## Authorize directly

```csharp
public sealed class ReportController(IAuthorize authorize) : ControllerBase
{
    public async Task<IActionResult> Export()
    {
        var decision = await authorize.AuthorizeAsync(new AuthorizeRequest
        {
            Subject = User,                    // or omit to use Identity.Current
            Action = "report.export",
            RequiredRoles = new[] { "analyst" },
        });
        return decision is AuthorizeDecision.Allow ? Ok() : Forbid();
    }
}
```

The `RbacAuthorizationProvider` decides this one (a role requirement is present): `Allow` if the subject holds
`analyst`, `Forbid` if authenticated but lacking it, `Challenge` if unauthenticated. With no `RequiredRoles`
and no mapped policy, every provider defers and `AuthorizeOptions.DefaultDecision` applies (default `Allow`;
set `Koan:Web:Authorization:DefaultDecision=Forbid` for a deny-by-default posture).

## Add a provider (PDP / ReBAC / custom)

Authorization is extended by implementing `IAuthorizationProvider` — return a decision, or `null` to defer:

```csharp
public sealed class CerbosAuthorizationProvider : IAuthorizationProvider
{
    public int Order => 200; // after the RBAC floor and named policies

    public async Task<AuthorizeDecision?> EvaluateAsync(AuthorizeRequest request, CancellationToken ct = default)
    {
        var allowed = await _cerbos.CheckAsync(request.Subject, request.Action, request.Resource, ct);
        return allowed ? AuthorizeDecision.Allowed() : AuthorizeDecision.Forbidden("cerbos denied");
    }
}

// register as another rung
services.TryAddEnumerable(ServiceDescriptor.Scoped<IAuthorizationProvider, CerbosAuthorizationProvider>());
```

## Notes

- **One vocabulary.** Cookie and bearer principals, capability gates, and direct calls all yield an
  `AuthorizeDecision` — no per-mechanism result shapes.
- **Channel-agnostic by contract.** `AuthorizeRequest` carries a `ClaimsPrincipal` (not an `HttpContext`), so
  the same call authorizes in HTTP, the message bus, and jobs.
- **Coarse in the token, fine at the resource.** Carry broad roles in the credential; resolve fine-grained,
  revocable decisions here (SEC-0001 §8).

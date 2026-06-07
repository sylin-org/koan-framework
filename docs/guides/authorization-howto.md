# Authorization How-To (SEC-0002)

Koan answers one question — *may this subject perform this action on this resource?* — through a single
seam, `IAuthorize`, backed by a capability-graded ladder of providers. This guide shows the everyday usage.

> Decision record: [SEC-0002 — Unified Authorization Model](../decisions/SEC-0002-unified-authorization-model.md).
> For sign-in / identity, see [authentication-setup.md](./authentication-setup.md).

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

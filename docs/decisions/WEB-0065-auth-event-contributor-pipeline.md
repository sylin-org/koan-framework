# WEB-0065 Auth event contributor pipeline

**Status**: Accepted, 2026-05-15
**Drivers**: Source-of-truth correctness for role assignments, unified extensibility surface
**Deciders**: Koan Framework maintainers
**Inputs**: `Koan.Web.Auth`, `Koan.Web.Auth.Roles`, downstream platform (gposingway emporium)
**Outputs**: New `IKoanAuthEventContributor` contract, removal of per-request role attribution pipeline
**Supersedes**: WEB-0049 (role attribution layer & claims transformation)

**Contract**
Inputs: Existing Koan.Web.Auth lifecycle, application User entities, IClaimsTransformation-based role enrichment.
Outputs: Event-driven, auto-discovered contributor pipeline; role attribution at sign-in only.
Error Modes: Stale roles in live sessions until re-login (acceptable); contributor exceptions soft-fail per-contributor.
Acceptance Criteria: Application User row is the authoritative source of role claims; built-in `RoleListFileContributor` provides override/preset channel.

## Context

WEB-0049 introduced `IRoleMapContributor` + `KoanRoleClaimsTransformation` to enrich every
authenticated request's `ClaimsPrincipal` with normalized roles. The model assumed role
attribution should be a per-request decoration anchored to a per-userId
`IRoleAttributionCache`.

Two operational problems surfaced in downstream platforms:

1. **No clean seam for "user row is the source of truth."** Applications that persist a
   `User` entity with a `Roles` field have no idiomatic way to expose that to the framework.
   `IRoleMapContributor` is the right surface, but the framework offered no built-in bridge,
   and the per-request invocation pattern made every role check a DB hit unless the
   application implemented its own caching layer.

2. **Cache invalidation puzzle.** `InMemoryRoleAttributionCache` caches the computed result
   per userId indefinitely. Once a user signs in, subsequent role changes do not surface
   until either the singleton cache is cleared (`POST /api/auth/roles/reload`, app restart,
   or import) or the user's userId is evicted. Per-user invalidation is not exposed on the
   `IRoleAttributionCache` contract. As a consequence, `RoleListContributor`'s own mtime-poll
   reload is effectively dead code once a user has been attributed once — the cached result
   wins.

The combination meant that the documented "role-list.json source of truth" was actually
"role-list.json at first sign-in after app restart, frozen until cache clear." Applications
that *intended* their User entity to be authoritative had no mechanism to make it so.

## Decision

Replace the per-request role attribution pipeline with an event-driven contributor model
owned by `Koan.Web.Auth`.

### `IKoanAuthEventContributor`

Auto-discovered (assembly scan, no `services.AddX<>()` registration). Three lifecycle hooks:

```csharp
namespace Koan.Web.Auth.Contributors;

public interface IKoanAuthEventContributor
{
    int Priority => 0;
    Task OnBootstrap(AuthBootstrapContext ctx, CancellationToken ct) => Task.CompletedTask;
    Task OnSignIn(AuthSignInContext ctx, CancellationToken ct) => Task.CompletedTask;
    Task OnSignOut(AuthSignOutContext ctx, CancellationToken ct) => Task.CompletedTask;
}
```

`Priority` controls dispatch order within a single event. Each contributor runs exactly
once per event; the pipeline does not loop. Identity-mapping work (e.g. provider sub →
platform user id) uses very low priorities (`int.MinValue`) to run first; role attribution
and audit work run at default priority; one-shot bootstraps (FirstUser admin elevation) run
at higher priorities so they observe what other contributors stamped.

### Contexts

```csharp
public sealed record AuthBootstrapContext(IServiceProvider Services, IHostEnvironment Environment);

public sealed class AuthSignInContext
{
    public required string UserId { get; init; }                  // platform user id post-mapping
    public required string? Provider { get; init; }                // discord/google/test
    public required ClaimsIdentity Identity { get; init; }         // mutate to bake claims
    public required IServiceProvider Services { get; init; }
    public required HttpContext HttpContext { get; init; }
    public string? RejectReason { get; private set; }
    public void Reject(string reason);                              // short-circuits remaining contributors
}

public sealed record AuthSignOutContext(string? UserId, IServiceProvider Services, HttpContext HttpContext);
```

`AuthBootstrapContext` provides `IServiceProvider` and lets the contributor reach for Koan
`Entity<T>` statics directly (`await User.All(ct)` etc.) rather than the framework abstracting
over the user entity shape. The framework has no built-in concept of "the user collection";
contributors that need set-wide iteration know which entity they care about.

### Dispatcher and lifecycle wiring

`AuthEventDispatcher` (internal) holds the enumerable of contributors and dispatches each
event in `Priority` order. Contributor exceptions are logged and swallowed except for
`OperationCanceledException`, which propagates. `Reject(reason)` on the sign-in context
short-circuits subsequent contributors and sets `HttpContext.Items` markers for outer
middleware (e.g. the existing conflict-redirect filter pattern).

Framework owns `CookieAuthenticationOptions.Events.OnSigningIn` and `OnSignedOut`. The
framework's PostConfigure assigns the dispatcher; applications no longer set these slots.
Migration: existing app-side `OnSigningIn` handlers become high-priority
`IKoanAuthEventContributor` implementations.

A framework-registered `AuthBootstrapHostedService` fires `OnBootstrap` once during
`StartAsync`. Contributor failures during bootstrap are logged and do not prevent the host
from starting.

### Built-in contributors (Koan.Web.Auth)

Two built-ins ship with the framework, both auto-discovered:

1. **`RoleListFileContributor`** (port of the old `RoleListContributor`). Reads the
   email-keyed JSON allow/revoke file at sign-in only. mtime-cached. Empty `FilePath`
   disables the contributor. Becomes the explicit "preset / override" channel rather than
   the de-facto source of truth.

2. **`AdminBootstrapContributor`**. Implements `FirstUser` / `ClaimMatch` admin elevation
   (logic ported verbatim from `DefaultRoleAttributionService.TryApplyBootstrap`). Uses
   `IRoleBootstrapStateStore` (kept from the Roles module) for one-shot persistence.

### Removals (Koan.Web.Auth.Roles)

Deleted:

- `IRoleMapContributor` + all implementations (`RoleListContributor`,
  `DefaultCapabilityContributor`)
- `IRoleAttributionService` + `DefaultRoleAttributionService`
- `IRoleAttributionCache` + `InMemoryRoleAttributionCache`
- `KoanRoleClaimsTransformation`
- `RoleAttributionOptions.Bootstrap*` config (moved to `Koan.Web.Auth`)

Kept:

- `Role`, `RoleAlias`, `RolePolicyBinding` entities + stores (data shapes for role admin)
- `RolesAdminController` (admin REST surface — minus `/reload` cache-clear)
- `IRoleConfigSnapshotProvider` (still useful for the admin surface)
- `RoleBootstrapHostedService` (trimmed: now only seeds Role / RoleAlias / RolePolicyBinding
  entities from config templates; the admin-bootstrap modes moved to the new built-in
  contributor)

### Version bumps

- `Koan.Web.Auth` 0.6.4 → 0.7.0 (new public surface)
- `Koan.Web.Auth.Roles` 0.6.5 → 0.7.0 (breaking removal of `IRoleMapContributor`)

## Consequences

**Positive**

- Single extensibility surface for auth-time work: roles, audit, last-login, account-state,
  telemetry. No more split between the one-slot `o.Events.OnSigningIn` (app-owned) and the
  many-slot `IRoleMapContributor` (per-request).
- Source of truth aligns with the data model. Applications expose role assignments by
  reading their `User` entity inside a contributor — no framework-side cache to fight.
- Per-request DB pressure drops to zero for role attribution.
- `role-list.json` becomes what its name implies: an explicit allow/revoke override file,
  loaded at sign-in.
- Bootstrap-time (`OnBootstrap`) contributors enable migration / repair / reconcile
  scenarios that had no clean home before.

**Negative / Trade-offs**

- Breaking change. Applications using `IRoleMapContributor` must migrate. Koan is
  greenfield and break-and-rebuild is the explicit posture for this layer.
- Role mutations do not propagate to already-signed-in sessions. Re-login required for
  refresh. Acceptable for moderation roles; revisit if user-facing role mutations become
  common (could be addressed with an admin endpoint that invalidates specific sessions).
- Application code that previously assigned `o.Events.OnSigningIn` must move that logic into
  an `IKoanAuthEventContributor` with a low priority. The framework owns the cookie event
  slot now.

## Migration

For applications previously using `IRoleMapContributor`:

```csharp
// Before
public sealed class MyRoleContributor : IRoleMapContributor
{
    public Task Contribute(ClaimsPrincipal p, ISet<string> roles, ISet<string> perms,
        RoleAttributionContext? ctx, CancellationToken ct) { roles.Add("admin"); return Task.CompletedTask; }
}

// After
public sealed class MyRoleContributor : IKoanAuthEventContributor
{
    public Task OnSignIn(AuthSignInContext ctx, CancellationToken ct)
    {
        ctx.Identity.AddClaim(new Claim(ClaimTypes.Role, "admin"));
        return Task.CompletedTask;
    }
}
```

For applications previously assigning `o.Events.OnSigningIn = MyHandler.OnSigningInAsync`:

```csharp
// Before (in PostConfigure)
o.Events.OnSigningIn = MyHandler.OnSigningInAsync;

// After: delete the assignment. Implement as a contributor:
public sealed class MyHandler : IKoanAuthEventContributor
{
    public int Priority => int.MinValue;        // run first (identity mapping)
    public Task OnSignIn(AuthSignInContext ctx, CancellationToken ct) { /* mapping logic */ }
}
```

## References

- WEB-0049 — Role attribution layer (superseded by this ADR)
- WEB-0043 — Multi-protocol authentication
- WEB-0047 — Capability authorization defaults

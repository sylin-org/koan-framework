# WEB-0066 Auth flow handler pipeline

**Status**: Proposed, 2026-05-17
**Drivers**: Unified, extensible cookie-auth lifecycle; remove hardcoded JSON-challenge heuristic
**Deciders**: Koan Framework maintainers
**Inputs**: `Koan.Web.Auth`, downstream platform (gposingway)
**Outputs**: New `IKoanAuthFlowHandler` contract, configurable `JsonChallengeHandler` built-in, `Koan:Web:Auth:Challenge` options surface
**Supersedes (in part)**: WEB-0065 (`IKoanAuthEventContributor` is kept as a compatibility shim)

## Context

WEB-0065 introduced `IKoanAuthEventContributor` as the discoverable hook into the cookie-auth
lifecycle. It covers three events — bootstrap, sign-in, sign-out — and is the right pattern in
the right shape. But three cookie-event slots stayed inline inside `AddKoanWebAuth` with no
extension point:

1. **`OnRedirectToLogin`** — challenge to the configured sign-in URL.
2. **`OnRedirectToAccessDenied`** — 403 redirect.
3. **`OnValidatePrincipal`** — per-request validation tick (not wired at all).

The first two contain a private `WantsJson(HttpRequest)` heuristic that converts the 302
redirect into 401/403 when the request looks JSON-ish (`Accept: application/json`,
`X-Requested-With: XMLHttpRequest`, or path under `/api`/`/.well-known`/`/me`). Two operational
problems:

1. **Hardcoded path list.** Downstream platforms with API surfaces outside `/api` (the gposingway
   platform's `/account/*` and `/v1/account/*` are the immediate trigger) silently fall back to
   redirect-mode for unauthenticated XHR — the SPA sees an opaque-redirect fetch failure instead
   of a clean 401 it can recover from.

2. **No replacement seam.** The cookie events are wired by `services.AddCookie(...)` inside the
   framework's own setup. Applications can't replace the `WantsJson` heuristic without rewriting
   the entire cookie configuration via `PostConfigure<CookieAuthenticationOptions>`, which the
   framework explicitly forbids ("Overwriting these via a later PostConfigure will break the
   lifecycle pipeline").

The fix is to widen WEB-0065's discoverable-handler pattern to cover the rest of the lifecycle
and to ship the framework's own JSON-challenge behaviour as one of those handlers, configurable
via options. Apps that don't care get identical behaviour to today; apps that need different
heuristics either flip options or ship a higher-priority handler.

## Decision

Introduce `IKoanAuthFlowHandler` — a single discoverable interface that covers every cookie
auth lifecycle event. Auto-discovered (assembly scan, no DI registration), scoped lifetime,
dispatched in `Priority` order, sequentially.

### `IKoanAuthFlowHandler`

```csharp
namespace Koan.Web.Auth.Flow;

public interface IKoanAuthFlowHandler
{
    int Priority => 0;

    Task OnBootstrap(AuthBootstrapContext ctx, CancellationToken ct) => Task.CompletedTask;
    Task OnValidatePrincipal(AuthValidatePrincipalContext ctx, CancellationToken ct) => Task.CompletedTask;
    Task OnSignIn(AuthSignInContext ctx, CancellationToken ct) => Task.CompletedTask;
    Task OnSignOut(AuthSignOutContext ctx, CancellationToken ct) => Task.CompletedTask;
    Task OnChallenge(AuthChallengeContext ctx, CancellationToken ct) => Task.CompletedTask;
    Task OnAccessDenied(AuthAccessDeniedContext ctx, CancellationToken ct) => Task.CompletedTask;
}
```

Default-implemented methods let handlers override only what they care about. Single interface
(not split per concern) because per-concern splits force consumer apps to register N adapters
for one piece of code, which ages badly; default methods on one interface ages well.

### Lifecycle contexts

`AuthBootstrapContext`, `AuthSignInContext`, `AuthSignOutContext` are reused verbatim from
WEB-0065. Three new contexts:

```csharp
public sealed class AuthChallengeContext
{
    public required HttpContext HttpContext { get; init; }
    public required IServiceProvider Services { get; init; }
    public required string DefaultRedirectUri { get; init; }
    public string RedirectUri { get; set; } = string.Empty;     // mutable
    public bool ResponseHandled { get; set; }                   // short-circuit
}

public sealed class AuthAccessDeniedContext { /* mirror of AuthChallengeContext */ }

public sealed class AuthValidatePrincipalContext
{
    public required CookieValidatePrincipalContext Inner { get; init; }
    public required IServiceProvider Services { get; init; }
}
```

`ResponseHandled` is the new short-circuit signal — handlers that have fully shaped the response
(set a status code, written the body) set it true to suppress the framework's default redirect.
Subsequent handlers still run for side effects but treat the response as final.

### `AuthFlowDispatcher`

Single dispatcher. Receives `IEnumerable<IKoanAuthFlowHandler>` plus
`IEnumerable<IKoanAuthEventContributor>` (legacy) and wraps each legacy contributor in a
`LegacyAuthContributorAdapter` at construction time so both registration paths converge into
one sorted pipeline. Stable sort: primary key `Priority`, tie-break by `Type.FullName`.

Per-event short-circuit policy:

- **Sign-in**: stops when `AuthSignInContext.RejectReason` is set (preserves WEB-0065 behavior).
- **Challenge / access-denied**: all handlers run, but they treat the response as finalized
  once `ResponseHandled` is true. Lets a high-priority handler shape the response while still
  letting later handlers emit audit / metrics for side effects.
- **Validate-principal**: no short-circuit; every handler observes the same context.
- **Bootstrap / sign-out**: every handler runs; soft-fails per handler on exception.

### Built-in `JsonChallengeHandler`

The previous inline `WantsJson` heuristic now ships as a discoverable handler at
`Flow/Builtin/JsonChallengeHandler.cs` — priority `int.MinValue + 1000` so it runs early but
can still be preempted by an app handler at a lower priority. Configurable via
`ChallengeOptions` bound from `Koan:Web:Auth:Challenge`:

```jsonc
{
  "Koan": {
    "Web": {
      "Auth": {
        "Challenge": {
          "Enabled": true,
          "ApiPaths": ["/api", "/.well-known", "/me", "/account", "/v1"],
          "TreatAcceptJsonAsApi": true,
          "TreatXhrHeaderAsApi": true
        }
      }
    }
  }
}
```

Defaults match the legacy heuristic plus `/account` and `/v1` so the immediate downstream pain
(gposingway) is solved out of the box. Apps with different conventions either extend `ApiPaths`
or flip `Enabled` to false and ship their own.

### Discovery and registration

`AddKoanWebAuth` runs two scans:

1. `DiscoverAndRegisterAuthEventContributors` — existing WEB-0065 scan. Registers each
   contributor as a scoped `IKoanAuthEventContributor`. No adapter glue at registration time.
2. `DiscoverAndRegisterAuthFlowHandlers` — new scan. Registers each non-abstract
   `IKoanAuthFlowHandler` as a scoped service. Skips `LegacyAuthContributorAdapter` (instantiated
   at dispatch time by the dispatcher itself).

The dispatcher constructor takes both enumerables and merges legacy contributors via the
adapter at construction time. One sort pass, one pipeline.

### Cookie event wiring

`AddKoanWebAuth`'s cookie event slots all dispatch through `AuthFlowDispatcher`:

```csharp
o.Events = new CookieAuthenticationEvents
{
    OnRedirectToLogin = async ctx => { /* dispatch challenge, emit redirect unless ResponseHandled */ },
    OnRedirectToAccessDenied = async ctx => { /* dispatch access-denied, emit redirect unless ResponseHandled */ },
    OnValidatePrincipal = async ctx => { /* dispatch validate-principal */ },
    OnSigningIn = async ctx => { /* dispatch sign-in */ },
    OnSigningOut = async ctx => { /* dispatch sign-out */ },
};
```

The previous inline JSON-challenge logic moves into `JsonChallengeHandler`; the previous inline
sign-in/sign-out dispatch into the legacy `AuthEventDispatcher` moves into the new dispatcher
(which still services legacy contributors via the adapter).

## Migration

- **WEB-0065 contributors keep working unchanged.** `LegacyAuthContributorAdapter` projects each
  `IKoanAuthEventContributor` as a `IKoanAuthFlowHandler` at construction time. No code change
  required. The `[Obsolete]` warning lands in a follow-up release once the new interface has
  bedded in.
- **New code implements `IKoanAuthFlowHandler` directly.** Same auto-discovery, broader event
  coverage.
- **Apps that previously hand-rolled `CookieAuthenticationOptions.Events`** must move that logic
  into a flow handler. Same constraint WEB-0065 imposed; the framework still owns the cookie
  event slots.

## Consequences

**Wins**:
- One interface, six events, sequential dispatch, deterministic ordering.
- The framework's own JSON-challenge behavior is a flow handler — eats its own dog food.
- `ResponseHandled` short-circuit gives apps a clean override seam without `PostConfigure`.
- `ChallengeOptions` gives configuration-driven control for the common case.
- Validate-principal is now an exposed extension point (used to be implicit ASP.NET only).

**Trade-offs**:
- `IKoanAuthFlowHandler` and `IKoanAuthEventContributor` coexist during the migration window.
  Adds one indirection layer (the adapter) for legacy contributors; cheap and isolated.
- Per-request adapter allocations for legacy contributors. The adapter is a single reference;
  GC pressure is negligible at expected auth-traffic scale.

## Acceptance criteria

- All existing `IKoanAuthEventContributor` implementations continue to fire unchanged.
- Anonymous XHR to a cookie-protected endpoint with `Accept: application/json` receives 401, not
  302, with the default `ChallengeOptions`.
- Anonymous browser navigation to the same endpoint receives 302 to sign-in, unchanged.
- An app-provided `IKoanAuthFlowHandler` at priority `int.MinValue` runs before
  `JsonChallengeHandler`, can set `ResponseHandled`, and observably skips Koan's default
  response.
- An app that flips `Enabled: false` gets the raw cookie redirect for every unauthenticated
  request (built-in JSON conversion off), unless its own handler reintroduces the conversion.

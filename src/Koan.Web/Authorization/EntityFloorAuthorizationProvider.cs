using System;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using Koan.Web.Hooks;

namespace Koan.Web.Authorization;

/// <summary>
/// SEC-0004 (§A) — the built-in per-action gate rung. Looks up the precompiled <see cref="AccessGate"/> for the
/// resource entity type (which already folds in <c>[Access]</c>, lowered legacy floor sugar, and — Slice B — an
/// <c>EntityAccess&lt;T&gt;</c> realization) and evaluates the bag for <see cref="AuthorizeRequest.Action"/>. An
/// OPEN action defers (<c>null</c>) so allow-by-default and higher rungs are preserved; a declared action returns a
/// concrete <see cref="AuthorizeDecision"/>. Registered by default in <c>Koan.Web</c> at <c>Order = 50</c>; richer
/// rungs (RBAC floor, named-policy, PDP/ReBAC) stack around it via <see cref="IAuthorizationProvider"/>.
/// </summary>
/// <remarks>
/// At the coarse gate no row is loaded (the resource is the entity <see cref="Type"/>), so an <c>owner</c> term
/// degrades to <c>authenticated</c> — "signed in, so they might own some row." Slice B's Constrain narrows the
/// actual rows; Slice C's per-row projection re-evaluates ownership per item. This keeps the gate from 403-ing a
/// legitimate owner before we know which rows they own (the inverse of the "rules are not filters" footgun).
/// </remarks>
public sealed class EntityFloorAuthorizationProvider : IAuthorizationProvider
{
    private readonly IAccessGateCache _cache;

    public EntityFloorAuthorizationProvider(IAccessGateCache cache)
        => _cache = cache ?? throw new ArgumentNullException(nameof(cache));

    /// <summary>After the RBAC floor (0), before named-policy (100): the entity gate is authoritative for the
    /// authority it understands (the <c>[Access]</c> declaration), deferring policy/PDP to higher rungs.</summary>
    public int Order => 50;

    public Task<AuthorizeDecision?> EvaluateAsync(AuthorizeRequest request, CancellationToken ct = default)
    {
        if (request.Resource is not Type entityType)
        {
            return Task.FromResult<AuthorizeDecision?>(null); // the gate only understands entity-typed resources
        }

        var actionGate = _cache.GetOrCompile(entityType).For(request.Action);
        if (actionGate.IsOpen)
        {
            return Task.FromResult<AuthorizeDecision?>(null); // open action → defer (allow-by-default preserved)
        }

        // A null subject (reflection-built request / misbehaving provider) is treated as anonymous, never NRE.
        var subject = request.Subject ?? new ClaimsPrincipal(new ClaimsIdentity());
        // owner degrades to authenticated at the coarse gate (no row to bind).
        var ownerAtGate = subject.Identity?.IsAuthenticated == true;
        var decision = AccessGateEvaluator.Evaluate(actionGate, subject, ownerSatisfied: ownerAtGate);
        return Task.FromResult<AuthorizeDecision?>(decision);
    }
}

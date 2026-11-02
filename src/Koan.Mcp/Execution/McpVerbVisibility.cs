using System;
using System.Collections.Generic;
using System.Reflection;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using Koan.Web.Authorization;
using Koan.Web.Endpoints;
using Koan.Web.Hooks;
using Microsoft.Extensions.DependencyInjection;

namespace Koan.Mcp.Execution;

/// <summary>
/// SEC-0011: answers "may this caller invoke this entity verb?" for one listing, with the decision the endpoint
/// enforces (<see cref="EntityAuthorization"/> over a scoped <see cref="IAuthorize"/>, as the principal the call
/// would run as). One instance serves one <c>tools/list</c> or resource read and memoizes per (entity, action), so an
/// external PDP rung is asked once per verb.
/// </summary>
internal sealed class McpVerbVisibility : IDisposable
{
    private readonly IServiceScope? _scope;
    private readonly IAuthorize? _authorize;
    private readonly IAccessGateCache? _compiledOnly;
    private readonly ClaimsPrincipal? _user;
    private readonly Dictionary<(MemberInfo Verb, string Action), bool> _memo = new();

    private McpVerbVisibility(IServiceScope? scope, IAccessGateCache? compiledOnly, ClaimsPrincipal? user)
    {
        _scope = scope;
        _authorize = scope?.ServiceProvider.GetService<IAuthorize>();
        _compiledOnly = compiledOnly;
        _user = user is null ? null : EntityRequestContextBuilder.NonHttpPrincipal(user);
    }

    /// <summary>A visibility over the authorization ladder of a fresh scope from <paramref name="scopes"/>. Without a
    /// scope factory (a provider built by its legacy constructor) it falls back to the compiled gate alone.</summary>
    public static McpVerbVisibility For(IServiceScopeFactory? scopes, IAccessGateCache gates, ClaimsPrincipal? user)
        => scopes is null
            ? new McpVerbVisibility(null, gates, user)
            : new McpVerbVisibility(scopes.CreateScope(), null, user);

    /// <summary>A <c>null</c> principal is STDIO local trust and sees every verb.</summary>
    public async Task<bool> Allows(Type entityType, EntityEndpointOperationKind operation, CancellationToken ct = default)
    {
        if (_user is null) return true;
        var action = McpEntityGate.ActionFor(operation);
        if (_memo.TryGetValue((entityType, action), out var known)) return known;

        var allowed = _compiledOnly is not null
            ? McpEntityGate.CoarseAllows(_compiledOnly, entityType, operation, _user)
            : await EntityAuthorization.Decide(_authorize, _user, action, entityType, ct).ConfigureAwait(false) is null;
        _memo[(entityType, action)] = allowed;
        return allowed;
    }

    /// <summary>The same question of a custom verb, with no resource at hand (ARCH-0141).</summary>
    public async Task<bool> Allows(MethodInfo verb, string action, CancellationToken ct = default)
    {
        if (_user is null) return true;
        if (_memo.TryGetValue((verb, action), out var known)) return known;

        var allowed = _compiledOnly is not null
            ? AccessGateEvaluator.Evaluate(_compiledOnly.GetOrCompile(verb).For(action), _user, ownerSatisfied: _user.Identity?.IsAuthenticated == true) is AuthorizeDecision.Allow
            : await EntityAuthorization.Decide(_authorize, _user, action, verb, ct: ct).ConfigureAwait(false) is null;
        _memo[(verb, action)] = allowed;
        return allowed;
    }

    public void Dispose() => _scope?.Dispose();
}

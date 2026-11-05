using System;
using System.Reflection;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using Koan.Web.Hooks;
using Microsoft.AspNetCore.Http;

namespace Koan.Web.Authorization;

/// <summary>
/// SEC-0011: the one decision base CRUD enforces for an entity verb. The shared entity endpoint asks it before every
/// operation, and every surface that advertises a verb (MCP <c>tools/list</c>, <c>koan://entities</c>,
/// <c>koan://self</c>, the Explorer map) asks it before listing one, so a listed verb is a callable verb.
/// </summary>
public static class EntityAuthorization
{
    /// <summary>
    /// Runs the <see cref="IAuthorize"/> ladder (every provider, then <see cref="AuthorizeOptions.DefaultDecision"/>)
    /// for <paramref name="action"/> on <paramref name="entityType"/>. Returns <c>null</c> to proceed, otherwise the
    /// denying Forbid or Challenge. No seam registered means allow, as the endpoint has always treated it.
    /// </summary>
    public static async Task<AuthorizeDecision?> Decide(IAuthorize? authorize, ClaimsPrincipal subject, string action,
        Type entityType, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(subject);
        ArgumentNullException.ThrowIfNull(entityType);
        if (authorize is null) return null;
        var decision = await authorize.AuthorizeAsync(new AuthorizeRequest
        {
            Subject = subject,
            Action = action,
            Resource = entityType,
        }, ct).ConfigureAwait(false);
        return decision is AuthorizeDecision.Allow ? null : decision;
    }

    /// <summary>
    /// The one decision of a custom verb, on every surface (ARCH-0141). The application's own
    /// <see cref="IAuthorizationProvider"/> can answer for <paramref name="verb"/>: a listing asks with no
    /// <paramref name="resource"/>, and a call says what it addresses.
    /// </summary>
    public static async Task<AuthorizeDecision?> Decide(IAuthorize? authorize, ClaimsPrincipal subject, string action,
        MethodInfo verb, IReadOnlyDictionary<string, object?>? resource = null, CancellationToken ct = default)
    {
        var decision = await Decision(authorize, subject, action, verb, resource, ct).ConfigureAwait(false);
        return decision is AuthorizeDecision.Allow ? null : decision;
    }

    /// <summary>
    /// The same decision whole (ARCH-0146): an allow with what the provider found, which a route's gate puts on the request, or what refuses.
    /// No seam registered is an allow with nothing found.
    /// </summary>
    public static async Task<AuthorizeDecision> Decision(IAuthorize? authorize, ClaimsPrincipal subject, string action,
        MethodInfo verb, IReadOnlyDictionary<string, object?>? resource = null, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(subject);
        ArgumentNullException.ThrowIfNull(verb);
        if (authorize is null) return AuthorizeDecision.Allowed();
        return await authorize.AuthorizeAsync(new AuthorizeRequest
        {
            Subject = subject,
            Action = action,
            Resource = verb,
            Context = resource,
        }, ct).ConfigureAwait(false);
    }

    /// <summary>The action an HTTP method performs: a read, a removal, or otherwise a write.</summary>
    public static string ActionOf(string httpMethod)
        => HttpMethods.IsGet(httpMethod) || HttpMethods.IsHead(httpMethod) ? EntityAuthorizeActions.Read
            : HttpMethods.IsDelete(httpMethod) ? EntityAuthorizeActions.Remove
            : EntityAuthorizeActions.Write;
}

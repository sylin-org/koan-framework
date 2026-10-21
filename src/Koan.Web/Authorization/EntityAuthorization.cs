using System;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using Koan.Web.Hooks;

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
}

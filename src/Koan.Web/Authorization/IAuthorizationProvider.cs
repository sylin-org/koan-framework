using Koan.Web.Hooks;

namespace Koan.Web.Authorization;

/// <summary>
/// SEC-0002 — a graded authorization decision strategy (ARCH-0084-style capability rung). Returns a
/// definitive <see cref="AuthorizeDecision"/>, or <c>null</c> to defer to the next rung. The default ladder is
/// RBAC floor (<c>Order=0</c>) → named-policy (<c>Order=100</c>, delegates to ASP.NET <c>IAuthorizationService</c>)
/// → PDP/ReBAC adapters (<c>Order=200+</c>, opt-in by package reference).
/// </summary>
public interface IAuthorizationProvider
{
    /// <summary>Lower runs first.</summary>
    int Order { get; }

    Task<AuthorizeDecision?> EvaluateAsync(AuthorizeRequest request, CancellationToken ct = default);
}

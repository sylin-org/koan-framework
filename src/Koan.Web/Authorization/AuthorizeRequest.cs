using System.Security.Claims;

namespace Koan.Web.Authorization;

/// <summary>
/// SEC-0002 — the authorization question: may <see cref="Subject"/> perform <see cref="Action"/> on the
/// optional <see cref="Resource"/>? Deliberately <c>HttpContext</c>-free so the seam is channel-agnostic.
/// </summary>
public sealed class AuthorizeRequest
{
    public required ClaimsPrincipal Subject { get; init; }

    public required string Action { get; init; }

    public object? Resource { get; init; }

    /// <summary>
    /// An explicit role requirement carried by the caller (e.g. <c>[Authorize(Roles=…)]</c>). Providers may
    /// also resolve requirements from configuration (the policy provider resolves the capability→policy map).
    /// </summary>
    public IReadOnlyCollection<string>? RequiredRoles { get; init; }

    public IReadOnlyDictionary<string, object?>? Context { get; init; }
}

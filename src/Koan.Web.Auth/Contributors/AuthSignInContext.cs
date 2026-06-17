using System.Security.Claims;
using Microsoft.AspNetCore.Http;

namespace Koan.Web.Auth.Contributors;

/// <summary>
/// Context passed to <see cref="Flow.IKoanAuthFlowHandler.OnSignIn"/>. Carries the mutable
/// <see cref="ClaimsIdentity"/> being baked into the auth cookie, the originating provider, and
/// a per-request <see cref="IServiceProvider"/>.
/// </summary>
/// <remarks>
/// <para>
/// <see cref="UserId"/> is computed from the <see cref="Identity"/>'s current
/// <see cref="ClaimTypes.NameIdentifier"/> claim (with <c>sub</c> as fallback). It re-reads on
/// every access — earlier contributors (typically an identity-mapping contributor at very low
/// priority) can rewrite the principal's NameIdentifier from the provider's sub to the platform
/// user id, and later contributors observe the new value.
/// </para>
/// <para>
/// <see cref="Identity"/> is the live <see cref="ClaimsIdentity"/> on the principal that
/// <c>SignInAsync</c> is about to persist. Contributors stamp claims here (role claims, custom
/// scoped claims, etc.) so the resulting cookie carries them.
/// </para>
/// <para>
/// <see cref="Reject(string)"/> records a marker and short-circuits subsequent contributors.
/// Outer middleware can read <see cref="Infrastructure.AuthLifecycleMarkers.SignInRejected"/> on
/// <see cref="HttpContext.Items"/> to translate the marker into a redirect or distinct response.
/// Contributors should call <see cref="Reject(string)"/> rather than throwing — exceptions are
/// logged and swallowed by the dispatcher.
/// </para>
/// </remarks>
public sealed class AuthSignInContext
{
    /// <summary>
    /// Platform user id read live from <see cref="Identity"/>. Reflects any rewrites earlier
    /// contributors made (e.g. provider sub → platform user id mapping). Empty when neither
    /// <see cref="ClaimTypes.NameIdentifier"/> nor <c>sub</c> is present.
    /// </summary>
    public string UserId => Identity.FindFirst(ClaimTypes.NameIdentifier)?.Value
        ?? Identity.FindFirst("sub")?.Value
        ?? string.Empty;

    /// <summary>Auth provider that initiated this sign-in (e.g. <c>discord</c>, <c>google</c>, <c>test</c>).</summary>
    public required string? Provider { get; init; }

    /// <summary>The mutable identity being baked into the cookie. Add or remove claims here.</summary>
    public required ClaimsIdentity Identity { get; init; }

    /// <summary>Per-request service provider.</summary>
    public required IServiceProvider Services { get; init; }

    /// <summary>The originating <see cref="HttpContext"/>.</summary>
    public required HttpContext HttpContext { get; init; }

    /// <summary>Reject reason set by <see cref="Reject(string)"/>, or <see langword="null"/> if not rejected.</summary>
    public string? RejectReason { get; private set; }

    /// <summary>
    /// Marks this sign-in as rejected. Subsequent contributors in the pipeline are skipped. The
    /// outer flow may use this to clear the cookie or redirect the user. Does not mutate
    /// <see cref="Identity"/> — contributors choosing to clear claims should do so themselves.
    /// </summary>
    public void Reject(string reason)
    {
        if (RejectReason is null)
            RejectReason = reason;
    }
}

using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Http;

namespace Koan.Web.Context;

/// <summary>A result a web context placed on the request (<see cref="WebContext.UsePrincipal"/>).</summary>
internal sealed class PlacedResult(AuthenticateResult result) : IAuthenticateResultFeature
{
    public AuthenticateResult? AuthenticateResult { get; set; } = result;
}

/// <summary>
/// Keeps a principal a web context placed when the request is authenticated again (ARCH-0142). <c>WebApplication</c> adds authentication of its own around what an
/// application adds in <c>Program</c>, after Koan's, and that would put back the principal Koan's context replaced. A placed result answers for its scheme, and everything
/// else goes through as it is.
/// </summary>
internal sealed class PlacedAuthentication(IAuthenticationService inner) : IAuthenticationService
{
    public Task<AuthenticateResult> AuthenticateAsync(HttpContext context, string? scheme)
        => context.Features.Get<IAuthenticateResultFeature>() is PlacedResult { AuthenticateResult: { Ticket: { } ticket } placed } && ticket.AuthenticationScheme == scheme
            ? Task.FromResult(placed)
            : inner.AuthenticateAsync(context, scheme);

    public Task ChallengeAsync(HttpContext context, string? scheme, AuthenticationProperties? properties) => inner.ChallengeAsync(context, scheme, properties);

    public Task ForbidAsync(HttpContext context, string? scheme, AuthenticationProperties? properties) => inner.ForbidAsync(context, scheme, properties);

    public Task SignInAsync(HttpContext context, string? scheme, ClaimsPrincipal principal, AuthenticationProperties? properties) => inner.SignInAsync(context, scheme, principal, properties);

    public Task SignOutAsync(HttpContext context, string? scheme, AuthenticationProperties? properties) => inner.SignOutAsync(context, scheme, properties);
}

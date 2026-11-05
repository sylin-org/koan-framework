using System.Security.Claims;
using Koan.Web.Auth.Connector.Atproto;
using Koan.Web.Auth.Connector.Atproto.Protocol;
using Koan.Web.Auth.Domain;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;

namespace Koan.Identity.Logins.Tests;

/// <summary>What the specs read back: who is signed in, the page a refused sign-in lands on, and a proof's exchange.</summary>
[ApiController]
public sealed class LoginProbeController : ControllerBase
{
    public const string WhoAmI = "/k52/whoami";
    public const string LandingPath = "/k52/landing";
    public const string Prove = "/k52/prove";
    public const string Audience = "did:web:app.example#k52";
    public const string Method = "org.example.k52.exchange";

    [HttpGet(WhoAmI)]
    public IActionResult Who() => Ok(new
    {
        id = User.FindFirst(ClaimTypes.NameIdentifier)?.Value,
        providerSubject = SignInProof.ProviderSubjectOf(User),
        did = User.FindFirst(AtprotoClaimTypes.Did)?.Value,
        handle = User.FindFirst(AtprotoClaimTypes.Handle)?.Value,
    });

    [HttpGet(LandingPath)]
    public IActionResult Landing([FromQuery] string? error) => Ok(new { error });

    /// <summary>Exchanges a service-auth token for a session, as an application's proof door does.</summary>
    [HttpPost(Prove)]
    public async Task<IActionResult> Exchange()
    {
        var proofs = HttpContext.RequestServices.GetRequiredService<AtprotoServiceAuth>();
        var header = Request.Headers.Authorization.ToString();
        var proof = await proofs.Verify(header["Bearer ".Length..], Audience, Method, HttpContext.RequestAborted);
        if (!proof.Verified) return Unauthorized(proof.Outcome.ToString());
        await proofs.SignIn(HttpContext, proof);
        return NoContent();
    }
}

using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Koan.Web.Auth.Server.Protocol;

namespace Koan.Web.Auth.Server.IntegrationTests;

/// <summary>
/// An application's server-rendered consent and done pages, reduced to what they ask Koan: the consent page finds the
/// pending request by <c>rid</c> or typed user code and answers it as JSON (with where Allow and Deny post); the done
/// page answers the outcome it reads from its own address. Mounted at <c>/consent</c> and <c>/consent/done</c>, the
/// paths <see cref="ConsentPageFixture"/> names.
/// </summary>
[ApiController]
public sealed class TestConsentPageController : ControllerBase
{
    [HttpGet("/consent")]
    public async Task<IActionResult> Consent([FromQuery] string? rid, [FromQuery(Name = "user_code")] string? userCode, CancellationToken ct)
    {
        var lookup = await ConsentPage.Find(HttpContext, rid ?? userCode, ct);
        if (lookup.Throttled) return StatusCode(429);
        return lookup.Request is { } pending ? Ok(pending) : NotFound();
    }

    [HttpGet("/consent/done")]
    public async Task<IActionResult> Done() => Ok(new { outcome = (await ConsentPage.Outcome(HttpContext))?.ToString() });
}

using System.Security.Claims;
using System.Text.Encodings.Web;
using Koan.Data.Abstractions.Annotations;
using Koan.Data.Core.Model;
using Koan.Web.Authorization;
using Koan.Web.Controllers;
using Koan.Web.Extensions;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Koan.Web.Extensions.Tests;

// ARCH-0092 Phase 2 — terse [RestEntity] entities + one explicit-controller entity for the precedence test.
// Short [StorageName]s keep the in-memory store key tidy (the fully-qualified type name carries dots).

/// <summary>Terse default — CRUD auto-exposed at <c>api/trinket</c> (kebab of the type name).</summary>
[RestEntity]
[StorageName("rest_trinkets")]
public sealed class Trinket : Entity<Trinket>
{
    public string Name { get; set; } = "";
}

/// <summary>Terse with an explicit route override — CRUD at <c>api/gizmos</c>, not <c>api/gizmo</c>.</summary>
[RestEntity("api/gizmos")]
[StorageName("rest_gizmos")]
public sealed class Gizmo : Entity<Gizmo>
{
    public string Label { get; set; } = "";
}

/// <summary>
/// Precedence: carries <c>[RestEntity]</c> AND a hand-written <see cref="CogController"/>. The explicit
/// controller must win — <c>api/cogs</c> is live and the terse <c>api/cog</c> is never registered.
/// </summary>
[RestEntity]
[StorageName("rest_cogs")]
public sealed class Cog : Entity<Cog>
{
    public string Teeth { get; set; } = "";
}

[ApiController]
[Route("api/cogs")]
public sealed class CogController : EntityController<Cog>
{
}

/// <summary>
/// ARCH-0092 Phase 3.2 — a gated entity. <c>[RequireScope]</c> is enforced by the unified seam inside the
/// shared <c>EntityEndpointService</c>, so the same declaration that gates REST also gates the MCP edge.
/// </summary>
[RestEntity]
[RequireScope("vault:read")]
[StorageName("rest_vaults")]
public sealed class Vault : Entity<Vault>
{
    public string Secret { get; set; } = "";
}

/// <summary>
/// Test authentication handler: a request carrying <c>X-Test-Scopes</c> is authenticated with those scopes
/// (space-delimited); without the header the request stays anonymous.
/// </summary>
public sealed class TestAuthHandler : AuthenticationHandler<AuthenticationSchemeOptions>
{
    public const string SchemeName = "Test";

    public TestAuthHandler(IOptionsMonitor<AuthenticationSchemeOptions> options, ILoggerFactory logger, UrlEncoder encoder)
        : base(options, logger, encoder)
    {
    }

    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        if (!Request.Headers.TryGetValue("X-Test-Scopes", out var scopes))
        {
            return Task.FromResult(AuthenticateResult.NoResult());
        }

        var identity = new ClaimsIdentity(new[] { new Claim("scope", scopes.ToString()) }, SchemeName);
        var ticket = new AuthenticationTicket(new ClaimsPrincipal(identity), SchemeName);
        return Task.FromResult(AuthenticateResult.Success(ticket));
    }
}

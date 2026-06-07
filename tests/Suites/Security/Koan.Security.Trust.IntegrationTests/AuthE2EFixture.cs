using System.Collections.Generic;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Koan.Core;
using Koan.Testing;
using Koan.Web;
using Koan.Web.Controllers;
using Koan.Web.Extensions;
using Koan.Web.Extensions.Authorization;
using Koan.Web.Hosting;
using Koan.Security.Trust.Issuer;

namespace Koan.Security.Trust.IntegrationTests;

/// <summary>
/// Boots a real Koan web app over an in-memory TestServer (Development) with the full auth/authz fabric, and
/// can mint bearer KSVIDs via the app's own <see cref="IIssuer"/> — so the inbound bearer scheme (which
/// validates against that same issuer) accepts them. This is the harness for HTTP-level end-to-end auth tests.
/// </summary>
public sealed class AuthE2EFixture : KoanTestPipelineFixtureBase
{
    public AuthE2EFixture() : base(typeof(AuthE2EFixture)) { }

    protected override void ConfigureAppConfiguration(IConfigurationBuilder builder)
        => builder.AddInMemoryCollection(new Dictionary<string, string?>
        {
            // offline-only, mirrors the bootstrap specs
            ["Koan:Data:Redis:ConnectionString"] = "localhost:0",
        });

    protected override void ConfigureTestServices(IServiceCollection services)
    {
        // AddKoan() discovery already runs the Koan.Web.Auth registrar (cookie + bearer schemes); calling
        // AddKoanWebAuth() again would double-register "Koan.cookie". Authorization is opt-in (not auto-run).
        services.AddKoan();
        services.AddKoanWeb();
        services.AddKoanAuthorization();
        services.AddKoanControllersFrom<E2EController>();

        // WEB-0069: verify the endpoint-contributor seam end-to-end (same path MCP uses).
        services.AddSingleton<IKoanEndpointContributor, TestEndpointContributor>();
    }

    // ConfigureApp intentionally left as the base no-op: KoanWebStartupFilter (AutoMapControllers) builds the
    // full routing→authn→authz→endpoints pipeline, so this exercises the REAL startup-filter wiring.

    public string MintBearer(string subject, params string[] roles)
        => Services.GetRequiredService<IIssuer>().Issue(new TrustClaims { Subject = subject, Roles = roles });
}

/// <summary>WEB-0069 — proves an <see cref="IKoanEndpointContributor"/> is invoked inside Koan's UseEndpoints block.</summary>
internal sealed class TestEndpointContributor : IKoanEndpointContributor
{
    public void Map(IEndpointRouteBuilder endpoints)
        => endpoints.MapGet("/e2e/contributed", () => Results.Ok(new { contributed = true }));
}

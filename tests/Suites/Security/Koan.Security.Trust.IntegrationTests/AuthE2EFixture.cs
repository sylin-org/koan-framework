using System.Collections.Generic;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Koan.Core;
using Koan.Core.BackgroundServices;
using Koan.Testing;
using Koan.Web;
using Koan.Web.Controllers;
using Koan.Web.Extensions;
using Koan.Web.Auth.Extensions;
using Koan.Web.Extensions.Authorization;
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

        // The base fixture runs as Development, where Koan.Core's *example* background services
        // (DatabaseMigrationService et al.) auto-run and abort startup. This auth e2e doesn't exercise
        // background services — disable the orchestrator to isolate the auth/authz pipeline.
        services.Configure<KoanBackgroundServiceOptions>(o => o.Enabled = false);
    }

    protected override void ConfigureApp(IApplicationBuilder app)
    {
        app.UseRouting();
        app.UseAuthentication();
        app.UseAuthorization();
        app.UseEndpoints(endpoints => endpoints.MapControllers());
    }

    public string MintBearer(string subject, params string[] roles)
        => Services.GetRequiredService<IIssuer>().Issue(new TrustClaims { Subject = subject, Roles = roles });
}

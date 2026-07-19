using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Koan.Core;
using Koan.Web;
using Koan.Web.Controllers;
using Koan.Web.Extensions;
using Koan.Web.Extensions.Authorization;
using Koan.Web.Hosting;
using Koan.Security.Trust.Issuer;
using Xunit;

namespace Koan.Security.Trust.IntegrationTests;

/// <summary>
/// Boots a real Koan web app over an in-memory TestServer (Development) with the full auth/authz fabric, and
/// can mint bearer tokens via the app's own <see cref="IIssuer"/> — so the inbound bearer scheme (which
/// validates against that same issuer) accepts them. This is the harness for HTTP-level end-to-end auth tests.
/// </summary>
/// <remarks>
/// ARCH-0091: self-composed generic-host TestServer (replaces the deleted bespoke
/// <c>KoanTestPipelineFixtureBase</c>). There is no sample <c>Program</c> here — the spec wires the Koan web
/// fabric itself, so <see cref="WebApplicationFactory{TEntryPoint}"/> does not apply; we build the host
/// directly and let <c>KoanWebStartupFilter</c> assemble the request pipeline.
/// </remarks>
public sealed class AuthE2EFixture : IAsyncLifetime
{
    private IHost? _host;
    private HttpClient? _client;

    private IServiceProvider Services =>
        _host?.Services ?? throw new InvalidOperationException("Host not started");

    public async ValueTask InitializeAsync()
    {
        var builder = Host.CreateDefaultBuilder()
            // Offline-only, mirrors the bootstrap specs.
            .ConfigureAppConfiguration(cfg => cfg.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:Redis"] = "localhost:0",
            }))
            .ConfigureWebHost(web =>
            {
                web.UseTestServer();
                web.UseEnvironment("Development");
                web.ConfigureServices(services =>
                {
                    // AddKoan() discovery already runs the Koan.Web.Auth registrar (cookie + bearer schemes);
                    // calling AddKoanWebAuth() again would double-register "Koan.cookie". Authorization is
                    // opt-in (not auto-run).
                    services.AddKoan();
                    services.AddKoanWeb();
                    services.AddKoanAuthorization();
                    services.AddKoanControllersFrom<E2EController>();

                    // WEB-0069: verify the endpoint-contributor seam end-to-end (same path MCP uses).
                    services.AddSingleton<IKoanEndpointContributor, TestEndpointContributor>();
                });

                // An empty Configure body is intentional: KoanWebStartupFilter (AutoMapControllers) builds the
                // full routing→authn→authz→endpoints pipeline, so this exercises the REAL startup-filter wiring.
                web.Configure(_ => { });
            });

        _host = await builder.StartAsync(TestContext.Current.CancellationToken);
        _client = _host.GetTestClient();
        _client.BaseAddress = new Uri("http://localhost");
    }

    public async ValueTask DisposeAsync()
    {
        _client?.Dispose();
        if (_host is not null)
        {
            await _host.StopAsync();
            if (_host is IAsyncDisposable asyncDisposableHost)
                await asyncDisposableHost.DisposeAsync();
            else
                _host.Dispose();
        }
    }

    public HttpClient CreateClient() => _client ?? throw new InvalidOperationException("Fixture not initialized");

    public string MintBearer(string subject, params string[] roles)
        => Services.GetRequiredService<IIssuer>().Issue(new TrustClaims { Subject = subject, Roles = roles });
}

/// <summary>WEB-0069 — proves an <see cref="IKoanEndpointContributor"/> is invoked inside Koan's UseEndpoints block.</summary>
internal sealed class TestEndpointContributor : IKoanEndpointContributor
{
    public void Map(IEndpointRouteBuilder endpoints)
        => endpoints.MapGet("/e2e/contributed", () => Results.Ok(new { contributed = true }));
}

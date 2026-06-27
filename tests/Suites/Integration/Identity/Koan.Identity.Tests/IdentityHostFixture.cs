using Koan.Core;
using Koan.Testing.Integration;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Koan.Identity.Tests;

/// <summary>
/// A single shared, OFFLINE host (in-memory data adapter) for the SEC-0007 P0 / Layer-0 acceptance, in the
/// neutral <c>"Test"</c> environment (non-production so the data adapter is permissive, non-Development so the
/// full Koan service graph — incl. ASP.NET's web-only MVC services from the auth pillar — is not strict-validated
/// against a generic host). A single shared host keeps every fact on the same store (<c>AppHost.Current</c> binds
/// if-null at the first start). <c>Koan.Tenancy</c> is referenced live (Closed posture) so the facts exercise that
/// identity entities are ambient-exempt (the global plane) and the <c>Membership</c> soft-FK resolves.
/// </summary>
public sealed class IdentityHostFixture : IAsyncLifetime
{
    /// <summary>The dev person id used by the dev-seed fact (matches the tenancy dev membership so they reconcile).</summary>
    public const string DevUser = "devboss";

    private IntegrationHost? _host;

    public IServiceProvider Services =>
        _host?.Services ?? throw new InvalidOperationException("Host not started.");

    public async ValueTask InitializeAsync()
    {
        _host = await KoanIntegrationHost.Configure()
            .WithSetting("Koan:Orchestration:EnableSelfOrchestration", "false")
            .ConfigureServices(s => s.AddKoan())
            .StartAsync();
    }

    public async ValueTask DisposeAsync()
    {
        if (_host is not null) await _host.DisposeAsync();
    }
}

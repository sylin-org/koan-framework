using System.Threading.Tasks;
using AwesomeAssertions;
using Koan.Core;
using Koan.Testing.Integration;
using Koan.Web.Auth.Providers;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
using Xunit.Abstractions;

namespace Koan.Tests.Integration.Bootstrap.Specs;

/// <summary>
/// Boot-smoke for the Auth pillar (per ARCH-0079). Proves <c>IProviderRegistry</c> resolves
/// through real <c>AddKoan()</c> reflective discovery.
/// </summary>
/// <remarks>
/// <c>IProviderRegistry</c> is <b>scoped</b> (not singleton), so the resolution goes through
/// <c>IServiceScopeFactory.CreateScope()</c> rather than the root container. The auth
/// auto-registrar wires in-memory <c>IUserStore</c> / <c>IExternalIdentityStore</c> defaults
/// and an <c>IStartupFilter</c> — safe offline. Auto-discovery of
/// <c>IKoanAuthEventContributor</c> and <c>IAuthProviderContributor</c> finds nothing in this
/// test, so the registry constructs with an empty provider set.
/// </remarks>
public sealed class AuthPillarBootstrapSpec
{
    private readonly ITestOutputHelper _output;

    public AuthPillarBootstrapSpec(ITestOutputHelper output)
    {
        _output = output;
    }

    [Fact]
    public async Task AddKoan_resolves_IProviderRegistry_through_real_bootstrap()
    {
        await using var host = await KoanIntegrationHost.Configure()
            // Offline-only — see DataCorePillarBootstrapSpec remarks.
            .WithSetting("Koan:Data:Redis:ConnectionString", "localhost:0")
            .ConfigureServices(services => services.AddKoan())
            .StartAsync();

        // Scoped service — must resolve through a scope, not the root container.
        using var scope = host.Services.CreateScope();
        var registry = scope.ServiceProvider.GetRequiredService<IProviderRegistry>();
        registry.Should().NotBeNull();
    }
}

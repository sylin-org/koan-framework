using System.Threading.Tasks;
using AwesomeAssertions;
using Koan.Core;
using Koan.Testing.Integration;
using Koan.Web.Auth.Providers;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Koan.Tests.Integration.Bootstrap.Pillars.Specs;

/// <summary>
/// Boot-smoke for the Auth pillar (per ARCH-0079). Proves the immutable provider catalog resolves
/// through real <c>AddKoan()</c> reflective discovery.
/// </summary>
/// <remarks>
/// The auth module wires in-memory store defaults and compiles a host-owned, credential-free projection. With no
/// provider connector in this host, the catalog is valid and empty.
/// </remarks>
public sealed class AuthPillarBootstrapSpec
{
    private readonly ITestOutputHelper _output;

    public AuthPillarBootstrapSpec(ITestOutputHelper output)
    {
        _output = output;
    }

    [Fact]
    public async Task AddKoan_registers_the_host_owned_provider_catalog()
    {
        await using var host = PillarHost.Configure()
            .ConfigureServices(services => services.AddKoan())
            .Build();

        var catalog = host.Services.GetRequiredService<IAuthProviderCatalog>();
        catalog.Providers.Should().BeEmpty();
        catalog.Default.Should().BeNull();
    }
}

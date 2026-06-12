using System.Threading.Tasks;
using AwesomeAssertions;
using Koan.Core;
using Koan.Testing.Integration;
using Koan.Web.Endpoints;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
using Xunit.Abstractions;

namespace Koan.Tests.Integration.Bootstrap.Specs;

/// <summary>
/// Boot-smoke for the Web pillar (per ARCH-0079). Web is largely *enablement* (controllers,
/// middleware) with no single "Web pillar entry" interface — we resolve
/// <c>IEntityEndpointDescriptorProvider</c> as proof that <c>AddKoanWeb()</c> ran end-to-end
/// through reflective discovery.
/// </summary>
public sealed class WebPillarBootstrapSpec
{
    private readonly ITestOutputHelper _output;

    public WebPillarBootstrapSpec(ITestOutputHelper output)
    {
        _output = output;
    }

    [Fact]
    public async Task AddKoan_resolves_IEntityEndpointDescriptorProvider_through_real_bootstrap()
    {
        await using var host = await KoanIntegrationHost.Configure()
            // Offline-only — see DataCorePillarBootstrapSpec remarks.
            .WithSetting("Koan:Data:Redis:ConnectionString", "localhost:0")
            .ConfigureServices(services => services.AddKoan())
            .StartAsync();

        var provider = host.Services.GetRequiredService<IEntityEndpointDescriptorProvider>();
        provider.Should().NotBeNull();
    }
}

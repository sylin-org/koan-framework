using System.Threading.Tasks;
using FluentAssertions;
using Koan.Core;
using Koan.Media.Core.Operators;
using Koan.Testing.Integration;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
using Xunit.Abstractions;

namespace Koan.Tests.Integration.Bootstrap.Specs;

/// <summary>
/// Boot-smoke for the Media pillar (per ARCH-0079). Proves <c>IMediaOperatorRegistry</c>
/// resolves through real <c>AddKoan()</c> reflective discovery. Media is the cleanest
/// pillar of the eight — pure CPU/registry, no external services, no hosted services.
/// </summary>
public sealed class MediaPillarBootstrapSpec
{
    private readonly ITestOutputHelper _output;

    public MediaPillarBootstrapSpec(ITestOutputHelper output)
    {
        _output = output;
    }

    [Fact]
    public async Task AddKoan_resolves_IMediaOperatorRegistry_through_real_bootstrap()
    {
        await using var host = await KoanIntegrationHost.Configure()
            // Offline-only — see DataCorePillarBootstrapSpec remarks.
            .WithSetting("Koan:Data:Redis:ConnectionString", "localhost:0")
            .ConfigureServices(services => services.AddKoan())
            .StartAsync();

        var registry = host.Services.GetRequiredService<IMediaOperatorRegistry>();
        registry.Should().NotBeNull();
    }
}

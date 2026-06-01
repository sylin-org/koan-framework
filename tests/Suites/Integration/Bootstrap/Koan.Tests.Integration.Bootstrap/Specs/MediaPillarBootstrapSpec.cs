using System.Threading.Tasks;
using AwesomeAssertions;
using Koan.Core;
using Koan.Media.Abstractions.Recipes;
using Koan.Testing.Integration;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
using Xunit.Abstractions;

namespace Koan.Tests.Integration.Bootstrap.Specs;

/// <summary>
/// Boot-smoke for the Media pillar (per ARCH-0079, updated for MEDIA-0004
/// recipe pipeline). Proves <see cref="IMediaRecipeRegistry"/> resolves
/// through real <c>AddKoan()</c> reflective discovery. Media is the
/// cleanest pillar — pure CPU/registry, no external services.
/// </summary>
public sealed class MediaPillarBootstrapSpec
{
    private readonly ITestOutputHelper _output;

    public MediaPillarBootstrapSpec(ITestOutputHelper output)
    {
        _output = output;
    }

    [Fact]
    public async Task AddKoan_resolves_IMediaRecipeRegistry_through_real_bootstrap()
    {
        await using var host = await KoanIntegrationHost.Configure()
            // Offline-only — see DataCorePillarBootstrapSpec remarks.
            .WithSetting("Koan:Data:Redis:ConnectionString", "localhost:0")
            .ConfigureServices(services => services.AddKoan())
            .StartAsync();

        var registry = host.Services.GetRequiredService<IMediaRecipeRegistry>();
        registry.Should().NotBeNull();
        registry.FormatShortcuts.Should().NotBeEmpty("the registry always advertises format shortcuts");
    }
}

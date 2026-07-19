using System.Threading.Tasks;
using AwesomeAssertions;
using Koan.Canon;
using Koan.Core;
using Koan.Testing.Integration;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Koan.Tests.Integration.Bootstrap.Pillars.Specs;

/// <summary>
/// Boot-smoke for the Canon pillar (per ARCH-0079). Proves <c>ICanonRuntime</c> resolves
/// through real <c>AddKoan()</c> semantic activation.
/// </summary>
public sealed class CanonPillarBootstrapSpec
{
    private readonly ITestOutputHelper _output;

    public CanonPillarBootstrapSpec(ITestOutputHelper output)
    {
        _output = output;
    }

    [Fact]
    public async Task AddKoan_resolves_ICanonRuntime_through_real_bootstrap()
    {
        await using var host = await PillarHost.Configure()
            .ConfigureServices(services => services.AddKoan())
            .StartAsync();

        var runtime = host.Services.GetRequiredService<ICanonRuntime>();
        runtime.Should().NotBeNull();
    }
}

using System.Threading.Tasks;
using AwesomeAssertions;
using Koan.Canon.Domain.Runtime;
using Koan.Core;
using Koan.Testing.Integration;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Koan.Tests.Integration.Bootstrap.Pillars.Specs;

/// <summary>
/// Boot-smoke for the Canon pillar (per ARCH-0079). Proves <c>ICanonRuntime</c> resolves
/// through real <c>AddKoan()</c> semantic activation.
/// </summary>
/// <remarks>
/// <para>
/// <b>Architectural note:</b> Canon currently has no functional module in
/// <c>Koan.Canon.Core</c> or <c>Koan.Canon.Domain</c>; activation lives in <c>Koan.Canon.Web</c>.
/// This is asymmetric with Data/Storage which wire through
/// their core packages, and forces apps that don't want the Web surface to wire Canon
/// manually. This spec therefore references <c>Koan.Canon.Web</c>. Worth raising as a
/// follow-up: a Domain-level module would let non-Web hosts adopt Canon via
/// Reference = Intent.
/// </para>
/// </remarks>
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
        await using var host = await KoanIntegrationHost.Configure()
            .ConfigureServices(services => services.AddKoan())
            .StartAsync();

        var runtime = host.Services.GetRequiredService<ICanonRuntime>();
        runtime.Should().NotBeNull();
    }
}

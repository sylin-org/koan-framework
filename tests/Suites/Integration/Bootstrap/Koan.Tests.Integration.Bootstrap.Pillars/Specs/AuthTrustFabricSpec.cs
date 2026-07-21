using System.IO;
using System.Threading.Tasks;
using AwesomeAssertions;
using Koan.Core;
using Koan.Security.Trust.Issuer;
using Koan.Testing.Integration;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Koan.Tests.Integration.Bootstrap.Pillars.Specs;

/// <summary>
/// Per ARCH-0079: Trust composes through real <c>AddKoan()</c> discovery with one ES256 issuer.
/// </summary>
public sealed class AuthTrustFabricSpec
{
    [Fact]
    public async Task AddKoan_registers_the_trust_issuer_through_real_bootstrap()
    {
        await using var host = await PillarHost.Configure()
            .WithSetting("Koan:Storage:DefaultProfile", "local")
            .WithSetting("Koan:Storage:Profiles:local:Provider", "local")
            .WithSetting("Koan:Storage:Profiles:local:Container", "trust-bootstrap")
            .WithSetting("Koan:Storage:Providers:Local:BasePath", Path.GetTempPath())
            .ConfigureServices(services => services.AddKoan())
            .StartAsync();

        var issuer = host.Services.GetService<IIssuer>();
        issuer.Should().NotBeNull();
        issuer!.PublishedKeys.Should().ContainSingle();
    }
}

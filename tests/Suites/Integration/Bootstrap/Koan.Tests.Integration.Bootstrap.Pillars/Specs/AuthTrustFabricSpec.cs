using System.Threading.Tasks;
using AwesomeAssertions;
using Koan.Core;
using Koan.Security.Trust.Issuer;
using Koan.Testing.Integration;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Koan.Tests.Integration.Bootstrap.Pillars.Specs;

/// <summary>
/// SEC-0001 Phase 2 (2g), per ARCH-0079: the trust fabric composes through real <c>AddKoan()</c> reflective
/// discovery — the asymmetric issuer is registered and resolvable. The fail-closed boot guard itself is
/// unit-tested in <c>Koan.Security.Trust.Tests</c> (a full-AddKoan Production boot would also trip unrelated
/// production guards, so it is not exercised here).
/// </summary>
public sealed class AuthTrustFabricSpec
{
    [Fact]
    public async Task AddKoan_registers_the_trust_issuer_through_real_bootstrap()
    {
        await using var host = await KoanIntegrationHost.Configure()
            .ConfigureServices(services => services.AddKoan())
            .StartAsync();

        var issuer = host.Services.GetService<IIssuer>();
        issuer.Should().NotBeNull();
        issuer!.KeyId.Should().NotBeNullOrEmpty();
    }
}

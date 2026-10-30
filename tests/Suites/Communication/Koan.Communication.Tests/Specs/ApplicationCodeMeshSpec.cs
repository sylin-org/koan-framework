using Koan.Core;
using Koan.Core.Hosting.App;
using Koan.Testing.Integration;
using Microsoft.Extensions.DependencyInjection;

namespace Koan.Communication.Tests.Specs;

/// <summary>The application's code names its mesh, so hosts configured apart never share one.</summary>
public sealed class ApplicationCodeMeshSpec
{
    [Fact]
    public async Task Each_host_is_on_the_mesh_its_code_names()
    {
        await using var first = await Start("mesh-first");
        await using var second = await Start("mesh-second");

        first.Services.GetRequiredService<ApplicationIdentitySnapshot>().Code.Should().Be("mesh-first");
        first.Services.GetRequiredService<Koan.Communication.Runtime.CommunicationRouter>().MeshId.Should().Be("mesh-first");
        second.Services.GetRequiredService<Koan.Communication.Runtime.CommunicationRouter>().MeshId.Should().Be("mesh-second");
    }

    private static Task<IntegrationHost> Start(string code) =>
        KoanIntegrationHost.Configure()
            .WithSetting("Koan:Application:Code", code)
            .ConfigureServices(static services => services.AddKoan())
            .StartAsync(TestContext.Current.CancellationToken);
}

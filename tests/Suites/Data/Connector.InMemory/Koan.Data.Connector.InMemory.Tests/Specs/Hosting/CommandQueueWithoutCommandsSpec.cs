using Koan.Core;
using Koan.Core.Hosting.Registry;
using Koan.Core.Observability.Health;
using Koan.Data.Core.Commands;
using Microsoft.Extensions.DependencyInjection;

namespace Koan.Data.Connector.InMemory.Tests.Specs.Hosting;

/// <summary>
/// DATA-0167: this assembly declares no command, on a route that cannot hold, which is every application that never heard of
/// commands. What leaves its queue healthy is that discovery, since a queue answers for the route when the host starts:
/// <c>CommandRouteSpec</c> proves the other side, on this same route.
/// </summary>
public sealed class CommandQueueWithoutCommandsSpec(InMemoryFixture fixture, ITestOutputHelper output)
    : KoanDataSpec<InMemoryFixture>(fixture, output)
{
    [Fact]
    public async Task An_application_with_no_commands_is_left_alone_on_a_route_with_no_holds()
    {
        await using var host = await BootAsync();

        KoanRegistry.GetDiscoveredImplementors(typeof(ICommand)).Should().BeEmpty();
        var hold = async () => { using (await EntityContext.Hold("cannot")) { } };
        (await hold.Should().ThrowAsync<NotSupportedException>()).Which.Message.Should().Contain("write.heldTransaction");

        var report = await host.Services.GetServices<IHealthContributor>().Single(contributor => contributor.Name == nameof(CommandQueue))
            .Check(TestContext.Current.CancellationToken);
        report.State.Should().Be(HealthState.Healthy);
    }
}

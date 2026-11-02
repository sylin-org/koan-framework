using Koan.Core.Observability.Health;
using Koan.Data.Core.Commands;
using Microsoft.Extensions.DependencyInjection;

namespace Koan.Data.Connector.InMemory.Tests.Specs.Hosting;

/// <summary>
/// DATA-0167: this assembly declares no command, on a route that cannot hold, which is every application that never heard of
/// commands. Its queue answers for no route and stays healthy. <c>CommandRouteSpec</c> proves the other side.
/// </summary>
public sealed class CommandQueueWithoutCommandsSpec(InMemoryFixture fixture, ITestOutputHelper output)
    : KoanDataSpec<InMemoryFixture>(fixture, output)
{
    [Fact]
    public async Task An_application_with_no_commands_is_left_alone_on_a_route_with_no_holds()
    {
        await using var host = await BootAsync();

        // Long past the start, when an application with commands would have been refused.
        await Task.Delay(TimeSpan.FromSeconds(1), TestContext.Current.CancellationToken);

        var report = await host.Services.GetRequiredService<CommandQueue>().Check(TestContext.Current.CancellationToken);
        report.State.Should().Be(HealthState.Healthy);
    }
}

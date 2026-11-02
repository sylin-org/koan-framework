using AwesomeAssertions;
using Koan.Core.Observability.Health;
using Koan.Data.Abstractions.Failures;
using Koan.Data.Core.Commands;
using Koan.Tests.Data.Core.Support;
using Microsoft.Extensions.DependencyInjection;

namespace Koan.Tests.Data.Core.Specs.Transactions;

/// <summary>Saves a row, if it ever runs.</summary>
public sealed record SaveATodo(string Title) : ICommand<string>
{
    public async Task<Outcome<string>> Run(CancellationToken ct)
    {
        var todo = await new TodoEntity { Title = Title }.Save(ct);
        return new Outcome<string>.Done(todo.Id);
    }
}

/// <summary>
/// DATA-0167: an application with commands on a route that cannot hold is told at start, and so is every send. The refusal is
/// the one a hold gives, naming the route, the capability and the correction, and the queue reports it through its health.
/// </summary>
public sealed class CommandRouteSpec
{
    [Theory]
    [InlineData("json")]
    [InlineData("inmemory")]
    public async Task A_route_with_no_holds_ends_the_queue_at_start_and_every_send_is_told_why(string adapter)
    {
        await using var runtime = await DataCoreRuntimeFixture.CreateAsync(extraSettings: new Dictionary<string, string?>
        {
            ["Koan:Data:Sources:Default:Adapter"] = adapter,
        });
        var queue = runtime.Services.GetRequiredService<CommandQueue>();

        var outcome = await new SaveATodo("refused").Send().WaitAsync(TimeSpan.FromSeconds(30));

        var failed = outcome.Should().BeOfType<Outcome<string>.Failed>().Which;
        failed.Landed.Should().Be(DataCommitOutcome.NotDispatched);
        failed.Cause.Should().BeOfType<NotSupportedException>().Which.Message
            .Should().Contain(adapter).And.Contain("write.heldTransaction").And.Contain("EntityContext.Source");
        var report = await queue.Check();
        report.State.Should().Be(HealthState.Unhealthy);
        report.Description.Should().Contain("write.heldTransaction");
        (await TodoEntity.Query(todo => todo.Title == "refused")).Should().BeEmpty();
    }
}

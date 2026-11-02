using AwesomeAssertions;
using Koan.Core;
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
/// the one a hold gives, naming the route, the capability and the correction, and the queue reports it through its health. A
/// command runs in its sender's context, so a source its sender names that cannot hold is refused the same way.
/// </summary>
public sealed class CommandRouteSpec
{
    private static IHealthContributor Health(DataCoreRuntimeFixture runtime) =>
        runtime.Services.GetServices<IHealthContributor>().Single(contributor => contributor.Name == nameof(CommandQueue));

    [Theory]
    [InlineData("json")]
    [InlineData("inmemory")]
    public async Task A_route_with_no_holds_ends_the_queue_at_start_and_every_send_is_told_why(string adapter)
    {
        await using var runtime = await DataCoreRuntimeFixture.CreateAsync(extraSettings: new Dictionary<string, string?>
        {
            ["Koan:Data:Sources:Default:Adapter"] = adapter,
        });

        var report = await Health(runtime).Check();
        report.State.Should().Be(HealthState.Unhealthy, "this is known at start, before any send");
        report.Description.Should().Contain("write.heldTransaction");

        var outcome = await new SaveATodo("refused").Send().WaitAsync(TimeSpan.FromSeconds(30));

        var failed = outcome.Should().BeOfType<Outcome<string>.Failed>().Which;
        failed.Landed.Should().Be(DataCommitOutcome.NotDispatched);
        failed.Cause.Should().BeOfType<NotSupportedException>().Which.Message
            .Should().Contain(adapter).And.Contain("write.heldTransaction").And.Contain("EntityContext.Source");
        (await TodoEntity.Query(todo => todo.Title == "refused")).Should().BeEmpty();
    }

    [Fact]
    public async Task A_source_its_sender_names_that_cannot_hold_is_refused_and_one_that_can_is_served()
    {
        await using var runtime = await DataCoreRuntimeFixture.CreateAsync(
            includeSqlite: true,
            extraSettings: new Dictionary<string, string?>
            {
                ["Koan:Data:Sources:Default:Adapter"] = "sqlite",
                ["Koan:Data:Sources:Plain:Adapter"] = "json",
            });
        (await Health(runtime).Check()).State.Should().Be(HealthState.Healthy, "the default route can hold");

        Outcome<string> refused;
        using (EntityContext.Source("Plain"))
            refused = await new SaveATodo("elsewhere").Send().WaitAsync(TimeSpan.FromSeconds(30));
        var failed = refused.Should().BeOfType<Outcome<string>.Failed>().Which;
        failed.Landed.Should().Be(DataCommitOutcome.NotDispatched);
        failed.Cause.Should().BeOfType<NotSupportedException>().Which.Message.Should().Contain("Plain").And.Contain("write.heldTransaction");

        (await new SaveATodo("here").Send().WaitAsync(TimeSpan.FromSeconds(30))).Should().BeOfType<Outcome<string>.Done>("the queue goes on");
    }
}

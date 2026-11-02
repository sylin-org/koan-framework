using AwesomeAssertions;
using Koan.Data.Abstractions.Failures;
using Koan.Data.Core.Commands;
using Koan.Tests.Data.Core.Support;

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
/// DATA-0167: a command sent on a route that cannot hold never runs, and its sender is told why. The refusal is the one a hold gives,
/// naming the route, the capability and the correction. A command runs in its sender's context, so this covers the default route and a
/// source the sender names alike.
/// </summary>
public sealed class CommandRouteSpec
{
    [Theory]
    [InlineData("json")]
    [InlineData("inmemory")]
    public async Task A_route_with_no_holds_refuses_every_send_and_says_why(string adapter)
    {
        await using var runtime = await DataCoreRuntimeFixture.CreateAsync(extraSettings: new Dictionary<string, string?>
        {
            ["Koan:Data:Sources:Default:Adapter"] = adapter,
        });

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

        Outcome<string> refused;
        using (EntityContext.Source("Plain"))
            refused = await new SaveATodo("elsewhere").Send().WaitAsync(TimeSpan.FromSeconds(30));
        var failed = refused.Should().BeOfType<Outcome<string>.Failed>().Which;
        failed.Landed.Should().Be(DataCommitOutcome.NotDispatched);
        failed.Cause.Should().BeOfType<NotSupportedException>().Which.Message.Should().Contain("Plain").And.Contain("write.heldTransaction");

        (await new SaveATodo("here").Send().WaitAsync(TimeSpan.FromSeconds(30))).Should().BeOfType<Outcome<string>.Done>();
    }
}

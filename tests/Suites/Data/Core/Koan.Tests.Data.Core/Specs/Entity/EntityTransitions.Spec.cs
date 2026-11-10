using Koan.Data.Core.Commands;
using Koan.Data.Core.Model;
using Koan.Tests.Data.Core.Support;
using DataConstants = Koan.Data.Core.Infrastructure.Constants;

namespace Koan.Tests.Data.Core.Specs.Entity;

/// <summary>DATA-0171: an entity declares the transitions a state member takes, and the lifecycle refuses any other change, whoever saves.</summary>
public sealed class EntityTransitionsSpec
{
    private static string NewPartition() => $"data-core-transitions-{Guid.CreateVersion7():n}";

    private static void Declare() =>
        Ticket.Lifecycle.Transitions(ticket => ticket.Stage, (Stage.Draft, Stage.Open), (Stage.Open, Stage.Closed), (Stage.Closed, Stage.Open));

    [Fact]
    public async Task A_declared_transition_saves_and_any_other_change_is_refused_with_its_word_and_sentence()
    {
        await using var runtime = await DataCoreRuntimeFixture.CreateAsync(configureServices: _ => Declare());
        using var partition = runtime.UsePartition(NewPartition());

        var ticket = await new Ticket { Title = "one" }.Save();
        ticket.Stage = Stage.Open;
        await ticket.Save();
        ticket.Stage = Stage.Draft;

        var refused = (await ((Func<Task>)(() => ticket.Save())).Should().ThrowAsync<Refusal>()).Which.Reason;

        refused.Kind.Should().Be(ReasonKind.Conflict);
        refused.Word.Should().Be(DataConstants.Lifecycle.Transition);
        refused.Text.Should().Be("Stage does not go from Open to Draft; from Open it goes to Closed.");
        (await Ticket.Get(ticket.Id))!.Stage.Should().Be(Stage.Open);
    }

    [Fact]
    public async Task A_save_that_leaves_the_member_as_it_stands_passes_and_so_do_a_first_save_and_an_insert()
    {
        await using var runtime = await DataCoreRuntimeFixture.CreateAsync(configureServices: _ => Declare());
        using var partition = runtime.UsePartition(NewPartition());

        var first = await new Ticket { Title = "closed from the start", Stage = Stage.Closed }.Save();
        first.Title = "renamed";
        await first.Save();
        (await new Ticket { Title = "inserted", Stage = Stage.Open }.Insert()).Outcome.Should().Be(MutationOutcome.Inserted);

        (await Ticket.Get(first.Id))!.Title.Should().Be("renamed");
    }

    [Fact]
    public async Task A_value_nothing_follows_says_so()
    {
        await using var runtime = await DataCoreRuntimeFixture.CreateAsync(configureServices: _ =>
            Ticket.Lifecycle.Transitions(ticket => ticket.Stage, (Stage.Draft, Stage.Closed)));
        using var partition = runtime.UsePartition(NewPartition());

        var ticket = await new Ticket { Stage = Stage.Closed }.Save();
        ticket.Stage = Stage.Draft;

        var refused = await ((Func<Task>)(() => ticket.Save())).Should().ThrowAsync<Refusal>();
        refused.Which.Reason.Text.Should().Be("Stage does not go from Closed to Draft; nothing follows Closed.");
    }

    [Fact]
    public async Task A_command_that_saves_a_change_outside_the_table_is_refused_and_leaves_nothing()
    {
        await using var runtime = await DataCoreRuntimeFixture.CreateAsync(
            includeSqlite: true,
            extraSettings: new Dictionary<string, string?> { ["Koan:Data:Sources:Default:Adapter"] = "sqlite" },
            configureServices: _ => Declare());

        var ticket = await new Ticket { Title = "draft" }.Save();

        var outcome = await new MoveTicket(ticket.Id, Stage.Closed, "moved").Send().WaitAsync(TimeSpan.FromSeconds(30));

        outcome.Should().BeOfType<Outcome<Ticket>.Refused>().Which.Reason.Word.Should().Be(DataConstants.Lifecycle.Transition);
        var stored = await Ticket.Get(ticket.Id);
        (stored!.Stage, stored.Title).Should().Be((Stage.Draft, "draft"));
        (await new MoveTicket(ticket.Id, Stage.Open, "opened").Send().WaitAsync(TimeSpan.FromSeconds(30)))
            .Should().BeOfType<Outcome<Ticket>.Done>();
    }

    /// <summary>Moves a ticket to a stage, renaming it in the same save.</summary>
    private sealed record MoveTicket(string Id, Stage To, string Title) : ICommand<Ticket>
    {
        public async Task<Outcome<Ticket>> Run(CancellationToken ct)
        {
            var ticket = (await Ticket.Get(Id, ct))!;
            ticket.Stage = To;
            ticket.Title = Title;
            return new Outcome<Ticket>.Done(await ticket.Save(ct));
        }
    }

    private enum Stage { Draft, Open, Closed }

    private sealed class Ticket : Entity<Ticket, string>
    {
        [Identifier]
        public override string Id { get; set; } = default!;
        public string Title { get; set; } = "";
        public Stage Stage { get; set; }
    }
}

using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using AwesomeAssertions;
using Koan.Core.Hosting.App;
using Xunit;

namespace Koan.Web.AdapterSurface.TestKit.EntityWrites;

/// <summary>
/// WEB-0086: an entity write commits as one and refuses like a command. Where the adapter holds a transaction, a hook's write and the row
/// stand together or neither does; where it cannot, the write runs as it does without one. Either way a thrown refusal or a lifecycle
/// cancellation answers its problem, and a hook that stops a write with the stored row answers it through the emit, as the first write did.
/// </summary>
public abstract class EntityWriteSpecsBase<TFactory> : IClassFixture<TFactory>
    where TFactory : class, IAdapterTestFactory
{
    protected EntityWriteSpecsBase(TFactory factory) => Factory = factory;

    protected TFactory Factory { get; }

    /// <summary>Whether this adapter holds a native transaction, so what a refused write did is undone.</summary>
    protected abstract bool Holds { get; }

    [Fact]
    public async Task A_write_that_stands_keeps_the_row_and_the_hooks_line_and_answers_through_the_emit()
    {
        var id = NewId();
        var answer = await Post(id, "kept");

        answer.StatusCode.Should().Be(HttpStatusCode.OK, await answer.Content.ReadAsStringAsync());
        var receipt = await answer.Content.ReadFromJsonAsync<JsonElement>();
        receipt.GetProperty("emitted").GetBoolean().Should().BeTrue();
        (await Stored(id)).Should().Be((true, 1));
    }

    [Fact]
    public async Task A_refusal_thrown_after_the_save_answers_its_problem_and_leaves_nothing_where_the_write_holds()
    {
        var id = NewId();
        var answer = await Post(id, LedgerNotes.RefuseAfter);

        answer.StatusCode.Should().Be(HttpStatusCode.Conflict);
        var problem = await Problem(answer);
        problem.GetProperty("word").GetString().Should().Be(LedgerNotes.AfterWord);
        problem.GetProperty("kind").GetString().Should().Be("conflict");
        (await Stored(id)).Should().Be(Holds ? (false, 0) : (true, 1));
    }

    [Fact]
    public async Task A_refusal_thrown_before_the_save_names_its_field()
    {
        var id = NewId();
        var answer = await Post(id, LedgerNotes.RefuseBefore);

        answer.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var problem = await Problem(answer);
        problem.GetProperty("word").GetString().Should().Be("invalid");
        problem.GetProperty("errors").GetProperty("note")[0].GetString().Should().Be("A ledger cannot say that.");
        (await Stored(id)).Should().Be(Holds ? (false, 0) : (false, 1));
    }

    [Fact]
    public async Task A_stop_of_400_or_more_after_the_save_leaves_nothing_where_the_write_holds()
    {
        var id = NewId();
        var answer = await Post(id, LedgerNotes.StopAfter);

        answer.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await Stored(id)).Should().Be(Holds ? (false, 0) : (true, 1));
    }

    [Fact]
    public async Task A_lifecycle_cancellation_answers_a_conflict_with_its_code_as_the_word()
    {
        var id = NewId();
        var answer = await Post(id, LedgerNotes.Cancel);

        answer.StatusCode.Should().Be(HttpStatusCode.Conflict);
        var problem = await Problem(answer);
        problem.GetProperty("word").GetString().Should().Be(LedgerNotes.CancelCode);
        problem.GetProperty("detail").GetString().Should().Be("This ledger cannot be kept.");
        (await Stored(id)).Should().Be(Holds ? (false, 0) : (false, 1));
    }

    [Fact]
    public async Task A_replay_stopped_with_the_stored_row_answers_as_the_first_write_did()
    {
        var id = NewId();
        var first = await Post(id, "first");
        first.StatusCode.Should().Be(HttpStatusCode.OK);
        var written = await first.Content.ReadAsStringAsync();

        var replay = await Post(id, LedgerNotes.Replay);

        replay.StatusCode.Should().Be(HttpStatusCode.OK);
        (await replay.Content.ReadAsStringAsync()).Should().Be(written);
        (await Stored(id)).Should().Be((true, 1));
    }

    [Fact]
    public async Task A_create_whose_id_another_write_took_first_answers_id_taken_where_the_caller_may_read_that_row()
    {
        // The row the race wrote is on no desk: the caller may read it, though not change it.
        var id = NewId();
        var answer = await Post(id, LedgerNotes.Race);

        answer.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await Problem(answer)).GetProperty("word").GetString().Should().Be("id-taken");
        (await Stored(id)).Should().Be(Holds ? (false, 0) : (true, 1));
    }

    [Fact]
    public async Task A_create_whose_id_another_write_took_first_answers_not_found_where_the_callers_read_hides_that_row()
    {
        var id = NewId();
        var answer = await Post(id, LedgerNotes.HiddenRace);

        answer.StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await Stored(id)).Should().Be(Holds ? (false, 0) : (true, 1));
    }

    private static string NewId() => Guid.CreateVersion7().ToString("N");

    private Task<HttpResponseMessage> Post(string id, string note)
        => Factory.Client.PostAsJsonAsync("/api/ledgers", new { id, note }, TestContext.Current.CancellationToken);

    private static async Task<JsonElement> Problem(HttpResponseMessage answer)
    {
        answer.Content.Headers.ContentType?.MediaType.Should().Be("application/problem+json");
        return await answer.Content.ReadFromJsonAsync<JsonElement>();
    }

    /// <summary>Whether the ledger stands, and how many lines its hook wrote that stand.</summary>
    private async Task<(bool Ledger, int Lines)> Stored(string id)
    {
        using var scope = AppHost.PushScope(Factory.Services);
        var ledger = await Ledger.Get(id);
        var lines = await LedgerLine.Query(line => line.LedgerId == id);
        return (ledger is not null, lines.Count);
    }
}

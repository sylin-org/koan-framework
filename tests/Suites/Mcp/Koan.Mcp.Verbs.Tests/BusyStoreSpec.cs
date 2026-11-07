using AwesomeAssertions;
using Koan.Data.Core;
using Koan.Data.Core.Commands;
using Newtonsoft.Json.Linq;
using Xunit;

namespace Koan.Mcp.Verbs.Tests;

/// <summary>A forum whose store keeps a writer waiting at most a second.</summary>
public sealed class BusyForumFixture : ForumFixture
{
    protected override Dictionary<string, string?> Settings() => new() { ["Koan:Data:Sqlite:BusyTimeout"] = "00:00:01" };
}

/// <summary>
/// K57, WEB-0086: an entity write whose hold cannot begin behind a busy store never ran, so it answers as a command that never ran does,
/// <c>503</c> with <c>Retry-After</c>, on REST and through <c>/mcp</c>, nothing of it written, and sent again once the store is free it lands.
/// </summary>
public sealed class BusyStoreSpec(BusyForumFixture forum) : IClassFixture<BusyForumFixture>
{
    private const string Mara = "mara";

    private static readonly TimeSpan Bound = TimeSpan.FromSeconds(10);

    [Fact]
    public async Task A_write_that_never_ran_behind_a_busy_store_answers_503_with_retry_after_on_both_doors_and_writes_nothing()
    {
        var ct = TestContext.Current.CancellationToken;
        var made = await forum.Route(HttpMethod.Post, "/signs", new JObject { ["text"] = "Before" }, Mara);
        made.Status.Should().Be(201, made.ToString());
        var id = JObject.Parse(made.Text)["id"]!.Value<string>()!;
        var byRoute = Guid.CreateVersion7().ToString("N");
        var byTool = Guid.CreateVersion7().ToString("N");

        var release = new TaskCompletionSource();
        var running = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        Task<Outcome<bool>> held;
        using (forum.App()) held = new Holding(release.Task, running).Send(ct);
        await running.Task.WaitAsync(Bound, ct);

        Answer[] answers;
        JToken called;
        try
        {
            answers =
            [
                await forum.Route(HttpMethod.Post, "/signs", new JObject { ["id"] = byRoute, ["text"] = "Never" }, Mara),
                await forum.Route(HttpMethod.Patch, $"/signs/{id}", new JObject { ["text"] = "Never" }, Mara),
                await forum.Tool("reshape_sign", new JObject { ["sign"] = id, ["text"] = "Never" }, Mara),
            ];
            called = await forum.Call("put_up_sign", new JObject { ["id"] = byTool, ["text"] = "Never" }, Mara);
        }
        finally
        {
            release.SetResult();
            (await held.WaitAsync(Bound, ct)).Should().BeOfType<Outcome<bool>.Done>();
        }

        foreach (var answer in answers)
        {
            answer.Status.Should().Be(503, answer.ToString());
            answer.Kind.Should().Be("failed");
            answer.RetryAfter.Should().Be("1");
            answer.Text.Should().Contain("safe to send again");
        }

        var meta = (JObject)called["_meta"]!;
        meta["status"]!.Value<int>().Should().Be(503, called.ToString());
        meta["retryAfter"]!.Value<string>().Should().Be("1");
        meta["landed"]!.Value<bool>().Should().BeFalse();

        using (forum.App())
        {
            (await Sign.Get(byRoute, ct)).Should().BeNull();
            (await Sign.Get(byTool, ct)).Should().BeNull();
            (await Sign.Get(id, ct))!.Text.Should().Be("Before");
        }

        var again = await forum.Route(HttpMethod.Post, "/signs", new JObject { ["id"] = byRoute, ["text"] = "Sent again" }, Mara);
        again.Status.Should().Be(201, again.ToString());
    }
}

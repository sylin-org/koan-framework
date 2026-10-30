using System.Runtime.CompilerServices;
using System.Text;
using AwesomeAssertions;
using Koan.Web.Sse;
using Koan.Web.Sse.Initialization;
using Koan.Web.Sse.Options;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Xunit;

namespace Koan.Web.Sse.Tests;

/// <summary>
/// WEB-0084: a stream keeps itself alive through a quiet source, ends when it is bounded to, says where a reader had
/// reached, and a hub fans out to bounded queues where one slow reader costs no other anything.
/// </summary>
public sealed class SseLivenessSpec
{
    private static readonly TimeSpan Beat = TimeSpan.FromMilliseconds(30);

    [Fact]
    public async Task A_quiet_source_is_answered_with_heartbeats_and_loses_nothing()
    {
        var context = Context();

        await Sse.Stream(QuietThenSays("after the quiet", TimeSpan.FromMilliseconds(200)), heartbeat: Beat).ExecuteAsync(context);

        var body = await Body(context);
        body.Should().Contain(": heartbeat\n\n");
        body.Should().EndWith("event: message\ndata: after the quiet\n\n", "the read held across the beats still answers");
    }

    [Fact]
    public async Task The_configured_heartbeat_applies_and_zero_sends_none()
    {
        var beating = Context(options => options.Heartbeat = Beat);
        await Sse.Stream(QuietThenSays("x", TimeSpan.FromMilliseconds(150))).ExecuteAsync(beating);
        (await Body(beating)).Should().Contain(": heartbeat");

        var silent = Context(options => options.Heartbeat = TimeSpan.Zero);
        await Sse.Stream(QuietThenSays("x", TimeSpan.FromMilliseconds(100))).ExecuteAsync(silent);
        (await Body(silent)).Should().NotContain(": heartbeat");
    }

    [Fact]
    public async Task A_stream_that_keeps_writing_sends_no_heartbeat()
    {
        var context = Context();

        await Sse.Stream(Steadily(5, TimeSpan.FromMilliseconds(5)), heartbeat: TimeSpan.FromSeconds(5)).ExecuteAsync(context);

        (await Body(context)).Should().NotContain(": heartbeat");
    }

    [Fact]
    public async Task A_lifetime_ends_the_stream_cleanly_and_releases_its_source()
    {
        var context = Context();
        var released = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        await Sse.Stream(NeverEnding(released), heartbeat: TimeSpan.Zero, lifetime: TimeSpan.FromMilliseconds(80))
            .ExecuteAsync(context);

        released.Task.IsCompleted.Should().BeTrue("the source's token was cancelled and its enumerator disposed");
    }

    [Fact]
    public async Task A_reader_leaving_ends_the_stream_and_releases_its_source()
    {
        var context = Context();
        using var leaving = new CancellationTokenSource(TimeSpan.FromMilliseconds(60));
        context.RequestAborted = leaving.Token;
        var released = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        var execute = () => Sse.Stream(NeverEnding(released), heartbeat: Beat).ExecuteAsync(context);

        await execute.Should().ThrowAsync<OperationCanceledException>();
        released.Task.IsCompleted.Should().BeTrue();
    }

    [Fact]
    public async Task A_source_that_fails_surfaces()
    {
        var execute = () => Sse.Stream(Failing(), heartbeat: Beat).ExecuteAsync(Context());

        await execute.Should().ThrowAsync<InvalidOperationException>().WithMessage("the source failed");
    }

    [Theory]
    [InlineData("41", "41")]
    [InlineData("", null)]
    public void Last_event_id_is_what_a_reconnecting_reader_sent(string sent, string? expected)
    {
        var context = new DefaultHttpContext();
        context.Request.Headers["Last-Event-ID"] = sent;

        Sse.LastEventId(context.Request).Should().Be(expected);
    }

    [Fact]
    public void Last_event_id_is_null_on_a_first_connection()
        => Sse.LastEventId(new DefaultHttpContext().Request).Should().BeNull();

    [Fact]
    public void The_hub_admits_within_each_reader_s_limit_and_the_whole_limit()
    {
        var hub = Hub(perReader: 2, inAll: 3);

        var first = hub.Open("a", () => { });
        var second = hub.Open("a", () => { });
        hub.Open("a", () => { }).Should().BeNull("a reader holds at most two");
        var third = hub.Open("b", () => { });
        hub.Open("c", () => { }).Should().BeNull("the hub holds at most three");
        hub.Count("a").Should().Be((3, 2));

        first.Should().NotBeNull();
        second!.Dispose();
        second.Dispose();
        hub.Count("a").Should().Be((2, 1), "a stream gives its place back, once");
        hub.Open("c", () => { }).Should().NotBeNull();
        third.Should().NotBeNull();
    }

    [Fact]
    public async Task A_published_value_reaches_every_open_stream_in_order()
    {
        var hub = Hub();
        using var a = hub.Open("a", () => { })!;
        using var b = hub.Open("b", () => { })!;

        hub.Publish(1);
        hub.Publish(2);

        (await a.Queue.ReadAsync(TestContext.Current.CancellationToken)).Should().Be(1);
        (await a.Queue.ReadAsync(TestContext.Current.CancellationToken)).Should().Be(2);
        (await b.Queue.ReadAsync(TestContext.Current.CancellationToken)).Should().Be(1);
    }

    [Fact]
    public async Task A_slow_stream_is_closed_alone_and_its_place_returned()
    {
        var hub = Hub(queueBound: 2);
        var aborted = 0;
        var slow = hub.Open("slow", () => aborted++)!;
        using var keeping = hub.Open("keeping", () => { })!;

        hub.Publish(1);
        hub.Publish(2);
        (await keeping.Queue.ReadAsync(TestContext.Current.CancellationToken)).Should().Be(1);
        (await keeping.Queue.ReadAsync(TestContext.Current.CancellationToken)).Should().Be(2);
        hub.Publish(3);

        slow.Slow.Should().BeTrue();
        aborted.Should().Be(1);
        hub.Count("slow").Should().Be((1, 0));
        (await keeping.Queue.ReadAsync(TestContext.Current.CancellationToken)).Should().Be(3);
        keeping.Slow.Should().BeFalse();

        // What it held still reads, then the queue reports its end.
        (await slow.Queue.ReadAsync(TestContext.Current.CancellationToken)).Should().Be(1);
        (await slow.Queue.ReadAsync(TestContext.Current.CancellationToken)).Should().Be(2);
        (await slow.Queue.WaitToReadAsync(TestContext.Current.CancellationToken)).Should().BeFalse();

        slow.Dispose();
        hub.Count("slow").Should().Be((1, 0), "closing it twice returns its place once");
    }

    [Fact]
    public void A_limit_below_one_is_refused_naming_the_key()
    {
        var create = () => Hub(perReader: 0);

        create.Should().Throw<InvalidOperationException>().Which.Message.Should().Contain("Koan:Web:Sse:Hub:PerReader");
    }

    [Fact]
    public void The_module_offers_a_hub_for_any_value_type()
    {
        var services = new ServiceCollection();
        services.AddSingleton<IConfiguration>(new ConfigurationBuilder().Build());
        services.AddOptions();
        new SseModule().Register(services);
        using var provider = services.BuildServiceProvider();

        provider.GetRequiredService<SseHub<int>>().Should().BeSameAs(provider.GetRequiredService<SseHub<int>>());
    }

    private static SseHub<int> Hub(int queueBound = 8, int perReader = 6, int inAll = 16)
        => new(Microsoft.Extensions.Options.Options.Create(new KoanSseOptions
        {
            Hub = new SseHubOptions { QueueBound = queueBound, PerReader = perReader, InAll = inAll },
        }));

    private static DefaultHttpContext Context(Action<KoanSseOptions>? configure = null)
    {
        var services = new ServiceCollection();
        services.AddOptions();
        services.Configure(configure ?? (_ => { }));
        var context = new DefaultHttpContext { RequestServices = services.BuildServiceProvider() };
        context.Response.Body = new MemoryStream();
        return context;
    }

    private static async Task<string> Body(DefaultHttpContext context)
    {
        context.Response.Body.Seek(0, SeekOrigin.Begin);
        using var reader = new StreamReader(context.Response.Body, Encoding.UTF8, leaveOpen: true);
        return await reader.ReadToEndAsync(TestContext.Current.CancellationToken);
    }

    private static async IAsyncEnumerable<string> QuietThenSays(string text, TimeSpan quiet,
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        await Task.Delay(quiet, ct);
        yield return text;
    }

    private static async IAsyncEnumerable<string> Steadily(int count, TimeSpan every,
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        for (var i = 0; i < count; i++)
        {
            yield return i.ToString();
            await Task.Delay(every, ct);
        }
    }

    private static async IAsyncEnumerable<string> NeverEnding(TaskCompletionSource released,
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        try
        {
            await Task.Delay(Timeout.InfiniteTimeSpan, ct);
            yield return "never";
        }
        finally
        {
            released.TrySetResult();
        }
    }

    private static async IAsyncEnumerable<string> Failing()
    {
        await Task.Yield();
        if (DateTime.UtcNow.Year > 0) throw new InvalidOperationException("the source failed");
        yield return "never";
    }
}

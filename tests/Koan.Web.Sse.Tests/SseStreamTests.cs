using System.Text;
using AwesomeAssertions;
using Koan.Web.Sse;
using Koan.Web.Sse.Formatting;
using Koan.Web.Sse.Options;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Abstractions;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Koan.Web.Sse.Tests;

public sealed class SseStreamTests
{
    [Fact]
    public async Task Typed_stream_writes_json_frames_with_the_configured_default_event()
    {
        var context = CreateHttpContext(options => options.DefaultEvent = "delta");

        var result = Sse.Stream(GetNumbers());
        await result.ExecuteAsync(context);

        var payload = await ReadBody(context);
        payload.Should().Contain("event: delta");
        payload.Should().Contain("data: {\"Value\":1}");
        context.Response.Headers["X-Accel-Buffering"].ToString().Should().Be("no");
    }

    [Fact]
    public async Task Text_stream_honors_the_explicit_event_name_without_json_quoting()
    {
        var context = CreateHttpContext();

        var result = Sse.Stream(GetStrings(), eventName: "token");
        await result.ExecuteAsync(context);

        var payload = await ReadBody(context);
        payload.Should().Contain("event: token");
        payload.Should().Contain("data: first");
        payload.Should().NotContain("data: \"first\"");
    }

    [Fact]
    public void Formatter_splits_multiline_payloads()
    {
        var envelope = new SseEnvelope("message", "one\ntwo");

        var formatted = SseFormatter.ToWireFormat(envelope);

        formatted.Should().Contain("data: one\n");
        formatted.Should().Contain("data: two\n");
    }

    [Fact]
    public async Task The_same_result_executes_through_mvc()
    {
        var context = CreateHttpContext();
        var result = Sse.Stream(GetStrings(), eventName: "delta");
        IActionResult action = result;

        await action.ExecuteResultAsync(new ActionContext(context, new RouteData(), new ActionDescriptor()));

        (await ReadBody(context)).Should().Contain("event: delta").And.Contain("data: first");
    }

    [Fact]
    public async Task Envelope_stream_preserves_explicit_events_and_fills_missing_names()
    {
        var context = CreateHttpContext(options => options.DefaultEvent = "heartbeat");

        var result = Sse.Stream(GetMixedEnvelopes());
        await result.ExecuteAsync(context);

        var payload = await ReadBody(context);
        payload.Should().Contain("event: heartbeat");
        payload.Should().Contain("event: custom");
    }

    private static DefaultHttpContext CreateHttpContext(Action<KoanSseOptions>? configure = null)
    {
        var services = new ServiceCollection();
        services.AddOptions();
        services.Configure(configure ?? (_ => { }));

        var context = new DefaultHttpContext { RequestServices = services.BuildServiceProvider() };
        context.Response.Body = new MemoryStream();
        return context;
    }

    private static async Task<string> ReadBody(DefaultHttpContext context)
    {
        context.Response.Body.Seek(0, SeekOrigin.Begin);
        using var reader = new StreamReader(context.Response.Body, Encoding.UTF8, leaveOpen: true);
        return await reader.ReadToEndAsync(TestContext.Current.CancellationToken);
    }

    private static async IAsyncEnumerable<TestMessage> GetNumbers()
    {
        for (var value = 1; value <= 2; value++)
        {
            yield return new TestMessage { Value = value };
            await Task.Yield();
        }
    }

    private static async IAsyncEnumerable<string> GetStrings()
    {
        yield return "first";
        await Task.Yield();
        yield return "second";
    }

    private static async IAsyncEnumerable<SseEnvelope> GetMixedEnvelopes()
    {
        yield return new SseEnvelope(null, "noop");
        await Task.Yield();
        yield return new SseEnvelope("custom", "data");
    }

    private sealed class TestMessage
    {
        public int Value { get; set; }
    }
}

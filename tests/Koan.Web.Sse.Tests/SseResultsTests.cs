using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using AwesomeAssertions;
using Koan.Web.Sse;
using Koan.Web.Sse.Formatting;
using Koan.Web.Sse.Mvc;
using Koan.Web.Sse.Options;
using Koan.Web.Sse.Results;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Abstractions;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Koan.Web.Sse.Tests;

public sealed class SseResultsTests
{
    [Fact]
    public async Task StreamJson_WritesFrames_WithDefaultEvent()
    {
        var context = CreateHttpContext(static options =>
        {
            options.DefaultEvent = "delta";
        });

        var source = GetNumbers();
        var result = SseResults.StreamJson(source);

        await result.ExecuteAsync(context);
        var payload = await ReadBody(context);

        payload.Should().Contain("event: delta");
        payload.Should().Contain("data: {\"Value\":1}");
        context.Response.Headers["X-Accel-Buffering"].ToString().Should().Be("no");
    }

    [Fact]
    public async Task StreamText_HonorsExplicitEventName()
    {
        var context = CreateHttpContext();
        var source = GetStrings();
        var result = SseResults.StreamText(source, eventName: "token");

        await result.ExecuteAsync(context);
        var payload = await ReadBody(context);

        payload.Should().Contain("event: token");
        payload.Should().Contain("data: first");
    }

    [Fact]
    public void Formatter_SplitsMultiLinePayload()
    {
        var envelope = new SseEnvelope("message", "one\ntwo");
        var formatted = SseFormatter.ToWireFormat(envelope);

        formatted.Should().Contain("data: one\n");
        formatted.Should().Contain("data: two\n");
    }

    [Fact]
    public async Task ActionResult_StreamText_WritesResponse()
    {
        var context = CreateHttpContext();
        var action = SseActionResult.StreamText(GetStrings(), eventName: "delta");
        var actionContext = new ActionContext(context, new RouteData(), new ActionDescriptor());

        await action.ExecuteResultAsync(actionContext);

        var payload = await ReadBody(context);
        payload.Should().Contain("event: delta");
        payload.Should().Contain("data: first");
    }

    [Fact]
    public async Task StreamEnvelopes_UsesDefaultEventForMissingNames()
    {
        var context = CreateHttpContext(static options => options.DefaultEvent = "heartbeat");
        var envelopes = GetMixedEnvelopes();

        var result = SseResults.StreamEnvelopes(envelopes);
        await result.ExecuteAsync(context);

        var payload = await ReadBody(context);
        payload.Should().Contain("event: heartbeat");
        payload.Should().Contain("event: custom");
    }

    private static DefaultHttpContext CreateHttpContext(Action<KoanSseOptions>? configure = null)
    {
        var services = new ServiceCollection();
        services.AddOptions();
        if (configure is not null)
        {
            services.Configure(configure);
        }
        else
        {
            services.Configure<KoanSseOptions>(_ => { });
        }

        var provider = services.BuildServiceProvider();
        var context = new DefaultHttpContext { RequestServices = provider };
        context.Response.Body = new MemoryStream();
        return context;
    }

    private static async Task<string> ReadBody(DefaultHttpContext context)
    {
        context.Response.Body.Seek(0, SeekOrigin.Begin);
        using var reader = new StreamReader(context.Response.Body, Encoding.UTF8, leaveOpen: true);
        return await reader.ReadToEndAsync();
    }

    private static async IAsyncEnumerable<TestMessage> GetNumbers()
    {
        for (var i = 1; i <= 2; i++)
        {
            yield return new TestMessage { Value = i };
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

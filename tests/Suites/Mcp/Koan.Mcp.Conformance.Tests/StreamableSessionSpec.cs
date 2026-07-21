using System;
using Koan.Mcp.Hosting;
using Koan.Mcp.Options;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Newtonsoft.Json.Linq;

namespace Koan.Mcp.Conformance.Tests;

/// <summary>
/// AI-0037 — the MCP Streamable HTTP session model: per-stream event-id cursors + bounded replay buffers
/// (resumability), single standalone GET stream per session, retained-stream eviction, and session mint/resolve/
/// terminate with a spec-conformant id. Pure unit coverage of the primitives the transport (phase 2b) builds on.
/// </summary>
public sealed class StreamableSessionSpec
{
    private static McpSessionManager NewManager(Action<McpServerOptions>? configure = null)
    {
        var services = new ServiceCollection();
        var builder = services.AddOptions<McpServerOptions>();
        if (configure is not null) builder.Configure(configure);
        var sp = services.BuildServiceProvider();
        return new McpSessionManager(sp.GetRequiredService<IOptionsMonitor<McpServerOptions>>(), TimeProvider.System, NullLogger<McpSessionManager>.Instance);
    }

    private static McpSession NewSession(McpSessionManager manager)
    {
        var context = new DefaultHttpContext { RequestServices = new ServiceCollection().BuildServiceProvider() };
        var session = manager.Create(context);
        session.Should().NotBeNull();
        return session!;
    }

    [Fact]
    public void Event_ids_are_per_stream_monotonic_cursors()
    {
        var session = NewSession(NewManager());
        var stream = session.OpenRequestStream();

        var id1 = stream.EnqueueMessage(new JObject { ["n"] = 1 });
        var id2 = stream.EnqueueMessage(new JObject { ["n"] = 2 });

        id1.Should().Be(stream.Id + ".1");
        id2.Should().Be(stream.Id + ".2");
        McpSseStream.StreamIdOf(id2).Should().Be(stream.Id);
        McpSseStream.StreamIdOf("malformed").Should().BeNull();
    }

    [Fact]
    public void ReplayAfter_returns_only_the_tail_after_the_named_event_id()
    {
        var session = NewSession(NewManager());
        var stream = session.OpenRequestStream();
        stream.EnqueueMessage(new JObject { ["n"] = 1 });
        var id2 = stream.EnqueueMessage(new JObject { ["n"] = 2 });
        stream.EnqueueMessage(new JObject { ["n"] = 3 });

        var tail = stream.ReplayAfter(id2);
        tail.Should().HaveCount(1);
        tail[0].Data.Should().Contain("\"n\":3");

        // An id that doesn't belong to this stream (or null) is treated as "from the start" → full buffer,
        // never a cross-stream replay.
        stream.ReplayAfter("someOtherStream.5").Should().HaveCount(3);
        stream.ReplayAfter(null).Should().HaveCount(3);
    }

    [Fact]
    public void Replay_buffer_is_bounded_by_capacity()
    {
        var session = NewSession(NewManager(o => o.Transport.StreamReplayBufferSize = 2));
        var stream = session.OpenRequestStream();
        stream.EnqueueMessage(new JObject { ["n"] = 1 });
        stream.EnqueueMessage(new JObject { ["n"] = 2 });
        stream.EnqueueMessage(new JObject { ["n"] = 3 });

        // Only the last 2 are retained; replaying from the start yields the bounded tail, oldest dropped.
        var all = stream.ReplayAfter(null);
        all.Should().HaveCount(2);
        all[0].Data.Should().Contain("\"n\":2");
        all[1].Data.Should().Contain("\"n\":3");
    }

    [Fact]
    public void Get_stream_is_single_per_session()
    {
        var session = NewSession(NewManager());
        session.TryOpenGetStream().Should().NotBeNull();
        session.TryOpenGetStream().Should().BeNull("the spec allows only one standalone GET stream per session (→ 409)");
    }

    [Fact]
    public void Completed_request_streams_are_retained_up_to_the_cap_then_evicted_oldest_first()
    {
        var session = NewSession(NewManager(o => o.Transport.MaxRetainedStreamsPerSession = 2));
        var s1 = session.OpenRequestStream();
        var s2 = session.OpenRequestStream();
        var s3 = session.OpenRequestStream(); // pushes past the cap → evicts s1

        session.TryGetStream(s1.Id, out _).Should().BeFalse("the oldest retained stream is evicted past the cap");
        session.TryGetStream(s2.Id, out _).Should().BeTrue();
        session.TryGetStream(s3.Id, out _).Should().BeTrue();
    }

    [Fact]
    public void Sessions_mint_an_ascii_safe_id_resolve_then_terminate_idempotently()
    {
        var manager = NewManager();
        var session = NewSession(manager);

        session.Id.Should().MatchRegex("^[0-9a-f]{32}$"); // 128-bit CSPRNG, visible-ASCII (spec)
        manager.TryGet(session.Id, out var resolved).Should().BeTrue();
        resolved.Should().BeSameAs(session);

        manager.Terminate(session.Id).Should().BeTrue();
        manager.TryGet(session.Id, out _).Should().BeFalse();
        manager.Terminate(session.Id).Should().BeFalse("re-terminating an unknown session is a no-op (idempotent from the client's view)");
    }

    [Fact]
    public void Concurrent_session_cap_is_enforced()
    {
        var manager = NewManager(o => o.MaxConcurrentSessions = 1);
        _ = NewSession(manager);

        var context = new DefaultHttpContext { RequestServices = new ServiceCollection().BuildServiceProvider() };
        manager.Create(context).Should().BeNull("the concurrent-session cap is reached (→ 429)");
    }
}

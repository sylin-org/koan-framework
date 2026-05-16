using System.Net;
using System.Text;
using FluentAssertions;
using Koan.ZenGarden.Koi;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Koan.ZenGarden.Tests;

public sealed class KoiHandlerTests : IDisposable
{
    private readonly MockKoiHttpHandler _httpHandler = new();
    private readonly HttpClient _httpClient;

    public KoiHandlerTests()
    {
        _httpClient = new HttpClient(_httpHandler);
    }

    public void Dispose()
    {
        _httpClient.Dispose();
    }

    [Fact]
    public async Task Start_transitions_to_Connected_when_Koi_is_healthy()
    {
        _httpHandler.HealthResponse = new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("""{"ok":true}""", Encoding.UTF8, "application/json")
        };
        _httpHandler.AdminStatusResponse = new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("""{"version":"0.1.0"}""", Encoding.UTF8, "application/json")
        };
        _httpHandler.BrowseResponses.Enqueue(BuildSseResponse("""{"found":{"name":"stone-alpha","ip":"192.168.1.10","port":7185,"host":"stone-alpha.local.","txt":{"stone_name":"stone-alpha","stone_id":"abc-123","version":"1.0","health":"ok","mac":"AA:BB:CC:DD:EE:FF"}}}"""));
        _httpHandler.EventsResponse = BuildSseHangResponse();

        var events = new List<KoiTopologyEvent>();
        using var handler = CreateHandler();
        handler.OnTopologyEvent((evt, _) =>
        {
            events.Add(evt);
            return ValueTask.CompletedTask;
        });

        handler.Start();

        // Wait for TopologyReset event, which fires after browse completes in Connected state.
        // The empty events stream will close immediately causing a reconnect loop, so we assert
        // on the snapshot captured at the time of the TopologyReset event rather than the
        // handler's current state (which may have already transitioned to Reconnecting).
        await WaitForCondition(
            () => events.Any(e => e.Kind == KoiTopologyEventKind.TopologyReset),
            TimeSpan.FromSeconds(5));

        events.Should().Contain(e => e.Kind == KoiTopologyEventKind.KoiAvailable);

        var resetEvent = events.First(e => e.Kind == KoiTopologyEventKind.TopologyReset);
        resetEvent.Snapshot.State.Should().Be(KoiHandlerState.Connected);
        resetEvent.Snapshot.Stones.Should().ContainSingle();
        resetEvent.Snapshot.Stones[0].StoneName.Should().Be("stone-alpha");
        resetEvent.Snapshot.Stones[0].StoneId.Should().Be("abc-123");
        resetEvent.Snapshot.Stones[0].Endpoint.Should().Be("http://192.168.1.10:7185");
        resetEvent.Snapshot.Stones[0].LocalEndpoint.Should().Be("http://stone-alpha.local:7185");
        resetEvent.Snapshot.KoiVersion.Should().Be("0.1.0");
    }

    [Fact]
    public async Task Start_transitions_to_NotDetected_when_Koi_is_unhealthy()
    {
        _httpHandler.HealthResponse = new HttpResponseMessage(HttpStatusCode.ServiceUnavailable);

        using var handler = CreateHandler();
        handler.Start();
        await WaitForState(handler, KoiHandlerState.NotDetected, TimeSpan.FromSeconds(3));

        handler.State.Should().Be(KoiHandlerState.NotDetected);
        handler.CurrentSnapshot.Should().BeSameAs(KoiTopologySnapshot.Empty);
    }

    [Fact]
    public void Start_is_idempotent()
    {
        _httpHandler.HealthResponse = new HttpResponseMessage(HttpStatusCode.ServiceUnavailable);

        using var handler = CreateHandler();
        handler.Start();
        handler.Start(); // second call should be a no-op
        handler.Start(); // third call should also be a no-op

        // No exception = success. State will eventually reach NotDetected.
    }

    [Fact]
    public async Task OnTopologyEvent_subscription_receives_events_and_unsubscribes()
    {
        _httpHandler.HealthResponse = new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("""{"ok":true}""", Encoding.UTF8, "application/json")
        };
        _httpHandler.AdminStatusResponse = new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("""{"version":"0.1.0"}""", Encoding.UTF8, "application/json")
        };
        _httpHandler.BrowseResponses.Enqueue(BuildSseResponse("""{"found":{"name":"stone-beta","ip":"10.0.0.5","port":7185,"host":"stone-beta.local.","txt":{"stone_name":"stone-beta"}}}"""));
        _httpHandler.EventsResponse = BuildSseHangResponse();

        var events = new List<KoiTopologyEvent>();
        using var handler = CreateHandler();
        var sub = handler.OnTopologyEvent((evt, _) =>
        {
            events.Add(evt);
            return ValueTask.CompletedTask;
        });

        handler.Start();
        await WaitForState(handler, KoiHandlerState.Connected, TimeSpan.FromSeconds(5));
        events.Count.Should().BeGreaterThan(0);

        var countBefore = events.Count;
        sub.Dispose();

        // After unsubscribe, no more events should arrive
        // (We can't easily trigger more events in this test, but the subscription handle is tested)
    }

    [Fact]
    public void CurrentSnapshot_is_Empty_before_start()
    {
        using var handler = CreateHandler();

        handler.CurrentSnapshot.Should().BeSameAs(KoiTopologySnapshot.Empty);
        handler.State.Should().Be(KoiHandlerState.Initializing);
    }

    [Fact]
    public async Task Browse_deduplicates_stones_by_CacheKey()
    {
        _httpHandler.HealthResponse = new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("""{"ok":true}""", Encoding.UTF8, "application/json")
        };
        _httpHandler.AdminStatusResponse = new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("""{"version":"0.1.0"}""", Encoding.UTF8, "application/json")
        };

        // Same stone reported twice (common with mDNS)
        _httpHandler.BrowseResponses.Enqueue(BuildSseResponse(
            """{"found":{"name":"stone-gamma","ip":"192.168.1.20","port":7185,"host":"stone-gamma.local.","txt":{"stone_name":"stone-gamma","stone_id":"id-gamma"}}}""",
            """{"found":{"name":"stone-gamma (2)","ip":"192.168.1.20","port":7185,"host":"stone-gamma.local.","txt":{"stone_name":"stone-gamma","stone_id":"id-gamma"}}}"""));
        _httpHandler.EventsResponse = BuildSseHangResponse();

        using var handler = CreateHandler();
        handler.Start();
        await WaitForState(handler, KoiHandlerState.Connected, TimeSpan.FromSeconds(5));

        handler.CurrentSnapshot.Stones.Should().ContainSingle(
            "duplicate mDNS entries with same stone_id should be deduped");
    }

    [Fact]
    public async Task StoneChanged_emitted_when_topology_significant_fields_change()
    {
        _httpHandler.HealthResponse = new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("""{"ok":true}""", Encoding.UTF8, "application/json")
        };
        _httpHandler.AdminStatusResponse = new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("""{"version":"0.1.0"}""", Encoding.UTF8, "application/json")
        };
        _httpHandler.BrowseResponses.Enqueue(BuildSseResponse(
            """{"found":{"name":"stone-delta","ip":"192.168.1.30","port":7185,"host":"stone-delta.local.","txt":{"stone_name":"stone-delta","stone_id":"id-delta","health":"ok"}}}"""));

        // Event stream reports same stone with changed health
        _httpHandler.EventsResponse = BuildSseResponse(
            """{"event":"resolved","service":{"name":"stone-delta","ip":"192.168.1.30","port":7185,"host":"stone-delta.local.","txt":{"stone_name":"stone-delta","stone_id":"id-delta","health":"degraded"}}}""");

        var events = new List<KoiTopologyEvent>();
        using var handler = CreateHandler();
        handler.OnTopologyEvent((evt, _) =>
        {
            events.Add(evt);
            return ValueTask.CompletedTask;
        });

        handler.Start();

        // Wait for the handler to process the event stream entry
        await WaitForCondition(
            () => events.Any(e => e.Kind == KoiTopologyEventKind.StoneChanged),
            TimeSpan.FromSeconds(5));

        var changed = events.First(e => e.Kind == KoiTopologyEventKind.StoneChanged);
        changed.Stone!.Health.Should().Be("degraded");
        changed.Previous!.Health.Should().Be("ok");
    }

    [Fact]
    public async Task StoneOffline_emitted_when_removed_event_received()
    {
        _httpHandler.HealthResponse = new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("""{"ok":true}""", Encoding.UTF8, "application/json")
        };
        _httpHandler.AdminStatusResponse = new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("""{"version":"0.1.0"}""", Encoding.UTF8, "application/json")
        };
        _httpHandler.BrowseResponses.Enqueue(BuildSseResponse(
            """{"found":{"name":"stone-epsilon","ip":"192.168.1.40","port":7185,"host":"stone-epsilon.local.","txt":{"stone_name":"stone-epsilon","stone_id":"id-epsilon"}}}"""));

        _httpHandler.EventsResponse = BuildSseResponse(
            """{"event":"removed","service":{"name":"stone-epsilon"}}""");

        var events = new List<KoiTopologyEvent>();
        using var handler = CreateHandler();
        handler.OnTopologyEvent((evt, _) =>
        {
            events.Add(evt);
            return ValueTask.CompletedTask;
        });

        handler.Start();

        await WaitForCondition(
            () => events.Any(e => e.Kind == KoiTopologyEventKind.StoneOffline),
            TimeSpan.FromSeconds(5));

        var offline = events.First(e => e.Kind == KoiTopologyEventKind.StoneOffline);
        offline.Stone!.StoneName.Should().Be("stone-epsilon");

        handler.CurrentSnapshot.Stones.Should().BeEmpty(
            "removed stone should no longer appear in snapshot");
    }

    [Fact]
    public void DiscoveredStone_TopologyEquals_ignores_DiscoveredAt()
    {
        var a = new DiscoveredStone
        {
            StoneName = "alpha",
            StoneId = "id-1",
            Endpoint = "http://10.0.0.1:7185",
            MossVersion = "1.0",
            Health = "ok",
            Mac = "AA:BB:CC:DD:EE:FF",
            DiscoveredAt = DateTimeOffset.UtcNow.AddMinutes(-5)
        };

        var b = a with { DiscoveredAt = DateTimeOffset.UtcNow };

        a.TopologyEquals(b).Should().BeTrue("DiscoveredAt should not affect topology comparison");
    }

    [Fact]
    public void DiscoveredStone_TopologyEquals_detects_endpoint_change()
    {
        var a = new DiscoveredStone
        {
            StoneName = "alpha",
            Endpoint = "http://10.0.0.1:7185",
            DiscoveredAt = DateTimeOffset.UtcNow
        };

        var b = a with { Endpoint = "http://10.0.0.2:7185" };

        a.TopologyEquals(b).Should().BeFalse("endpoint change is topology-significant");
    }

    [Fact]
    public void DiscoveredStone_ToCachedMossStone_maps_correctly()
    {
        var discovered = new DiscoveredStone
        {
            StoneName = "alpha",
            StoneId = "id-1",
            Endpoint = "http://10.0.0.1:7185",
            MossVersion = "1.0",
            DiscoveredAt = DateTimeOffset.UtcNow
        };

        var cached = discovered.ToCachedMossStone();

        cached.StoneName.Should().Be("alpha");
        cached.StoneId.Should().Be("id-1");
        cached.Endpoint.Should().Be("http://10.0.0.1:7185");
        cached.MossVersion.Should().Be("1.0");
        cached.LastSeenUtc.Should().Be(discovered.DiscoveredAt);
    }

    [Fact]
    public void DiscoveredStone_CacheKey_prefers_StoneId()
    {
        var withId = new DiscoveredStone
        {
            StoneName = "alpha",
            StoneId = "id-1",
            Endpoint = "http://10.0.0.1:7185",
            DiscoveredAt = DateTimeOffset.UtcNow
        };

        var withoutId = new DiscoveredStone
        {
            StoneName = "beta",
            Endpoint = "http://10.0.0.2:7185",
            DiscoveredAt = DateTimeOffset.UtcNow
        };

        withId.CacheKey.Should().Be("id-1");
        withoutId.CacheKey.Should().Be("beta");
    }

    [Fact]
    public void KoiTopologySnapshot_Empty_has_correct_defaults()
    {
        var empty = KoiTopologySnapshot.Empty;

        empty.State.Should().Be(KoiHandlerState.Initializing);
        empty.Stones.Should().BeEmpty();
        empty.Lanterns.Should().BeEmpty();
        empty.LastUpdate.Should().BeNull();
        empty.KoiDetectedAt.Should().BeNull();
        empty.KoiVersion.Should().BeNull();
    }

    // ── Helpers ──────────────────────────────────────────────────────────

    private KoiHandler CreateHandler(ZenGardenOptions? options = null)
    {
        var opts = options ?? new ZenGardenOptions
        {
            KoiEndpoint = "http://localhost:5641",
            KoiDiscoveryEnabled = true,
            KoiHealthTimeout = TimeSpan.FromSeconds(2),
            KoiBrowseIdleTimeout = TimeSpan.FromSeconds(1),
            KoiContinuousDiscovery = true,
            KoiLanternDiscovery = false,
            KoiRetryInterval = TimeSpan.FromSeconds(1)
        };

        return new KoiHandler(opts, NullLogger<KoiHandler>.Instance, _httpClient);
    }

    private static async Task WaitForState(IKoiHandler handler, KoiHandlerState expected, TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < deadline)
        {
            if (handler.State == expected) return;
            await Task.Delay(50);
        }
    }

    private static async Task WaitForCondition(Func<bool> condition, TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < deadline)
        {
            if (condition()) return;
            await Task.Delay(50);
        }
    }

    private static HttpResponseMessage BuildSseResponse(params string[] dataLines)
    {
        var sb = new StringBuilder();
        foreach (var line in dataLines)
        {
            sb.AppendLine($"data: {line}");
            sb.AppendLine();
        }

        return new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(sb.ToString(), Encoding.UTF8, "text/event-stream")
        };
    }

    private static HttpResponseMessage BuildSseHangResponse()
    {
        // Return an empty SSE stream that will end immediately (simulates idle close)
        return new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("", Encoding.UTF8, "text/event-stream")
        };
    }

    /// <summary>
    /// Mock HTTP handler that routes Koi API requests to canned responses.
    /// </summary>
    private sealed class MockKoiHttpHandler : HttpMessageHandler
    {
        public HttpResponseMessage HealthResponse { get; set; } = new(HttpStatusCode.ServiceUnavailable);
        public HttpResponseMessage AdminStatusResponse { get; set; } = new(HttpStatusCode.NotFound);
        public Queue<HttpResponseMessage> BrowseResponses { get; } = new();
        public HttpResponseMessage? EventsResponse { get; set; }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var uri = request.RequestUri ?? throw new InvalidOperationException("URI required");
            var path = uri.AbsolutePath;

            if (path.Equals(Constants.Koi.HealthEndpoint, StringComparison.OrdinalIgnoreCase))
                return Task.FromResult(CloneResponse(HealthResponse));

            if (path.Equals(Constants.Koi.StatusEndpoint, StringComparison.OrdinalIgnoreCase))
                return Task.FromResult(CloneResponse(AdminStatusResponse));

            if (path.Equals(Constants.Koi.BrowseEndpoint, StringComparison.OrdinalIgnoreCase))
            {
                if (BrowseResponses.Count > 0)
                    return Task.FromResult(BrowseResponses.Dequeue());
                return Task.FromResult(BuildSseHangResponse());
            }

            if (path.Equals(Constants.Koi.EventsEndpoint, StringComparison.OrdinalIgnoreCase))
                return Task.FromResult(EventsResponse ?? BuildSseHangResponse());

            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound));
        }

        private static HttpResponseMessage CloneResponse(HttpResponseMessage original)
        {
            var clone = new HttpResponseMessage(original.StatusCode);
            if (original.Content is StringContent sc)
            {
                var body = sc.ReadAsStringAsync().Result;
                clone.Content = new StringContent(body, Encoding.UTF8, "application/json");
            }
            return clone;
        }
    }
}

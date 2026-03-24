using System.Text;
using System.Net;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Koan.ZenGarden.Tests;

public sealed class ZenGardenCapabilityWishTests
{
    [Fact]
    public async Task WishAsync_posts_missing_capabilities_and_returns_in_progress_state()
    {
        var handler = new ScriptedMossHandler();
        using var httpClient = new HttpClient(handler);
        using var client = new ZenGardenClient(
            httpClient,
            NullLogger<ZenGardenClient>.Instance,
            new ZenGardenOptions
            {
                Endpoint = "http://moss.test:7185",
                EnableDiscovery = false,
                StreamReconnectDelaySeconds = 30
            });

        var wish = await client.Wish("ollama", ["model1", "model2"]);

        wish.ToolFqid.Should().Be("ollama");
        wish.Status.Should().Be("in_progress");
        wish.IsFulfilled.Should().BeFalse();
        wish.Missing.Should().BeEquivalentTo("model1", "model2");
        handler.CapabilityEnsureRequests.Should().HaveCount(2);
    }

    [Fact]
    public async Task SubscribeCapability_emits_initial_progress_from_cached_wish()
    {
        var handler = new ScriptedMossHandler();
        using var httpClient = new HttpClient(handler);
        using var client = new ZenGardenClient(
            httpClient,
            NullLogger<ZenGardenClient>.Instance,
            new ZenGardenOptions
            {
                Endpoint = "http://moss.test:7185",
                EnableDiscovery = false,
                StreamReconnectDelaySeconds = 30
            });

        var wish = await client.Wish("ollama", ["model1"]);

        var tcs = new TaskCompletionSource<ZenGardenCapabilityProgressEvent>(TaskCreationOptions.RunContinuationsAsynchronously);
        using var subscription = client.SubscribeCapability(
            wish.RequestId,
            (evt, ct) =>
            {
                tcs.TrySetResult(evt);
                return ValueTask.CompletedTask;
            },
            new ZenGardenCapabilityWatchOptions { EmitInitialState = true });

        var progress = await tcs.Task.WaitAsync(TimeSpan.FromSeconds(2));
        progress.Wish.RequestId.Should().Be(wish.RequestId);
        progress.Kind.Should().Be(ZenGardenCapabilityProgressEventKind.InProgress);
    }

    private sealed class ScriptedMossHandler : HttpMessageHandler
    {
        public List<string> CapabilityEnsureRequests { get; } = new();

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var path = request.RequestUri?.AbsolutePath ?? string.Empty;
            var query = request.RequestUri?.Query ?? string.Empty;

            if (path.Equals("/health", StringComparison.OrdinalIgnoreCase))
            {
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK));
            }

            if (path.Equals("/api/v1/garden/tools", StringComparison.OrdinalIgnoreCase))
            {
                var body = """
                {
                  "data": {
                    "cursor": 1,
                    "tools": [
                      {
                        "fqid": "ollama",
                        "tool": { "name": "", "type": "ollama", "category": "offering", "id": "", "tags": [] },
                        "stone": { "id": "", "name": "", "endpoint": "" },
                        "service": { "status": "running", "ready": true, "protocol": "http", "uris": [] },
                        "revision": 1,
                        "capabilities": []
                      }
                    ]
                  }
                }
                """;
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(body, Encoding.UTF8, "application/json")
                });
            }

            if (path.Equals("/api/v1/garden/tools/stream", StringComparison.OrdinalIgnoreCase))
            {
                var stream = "event: tools.heartbeat\ndata: {}\n\n";
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(stream, Encoding.UTF8, "text/event-stream")
                });
            }

            if (path.StartsWith("/api/v1/stone/offerings/", StringComparison.OrdinalIgnoreCase) &&
                path.EndsWith("/capabilities", StringComparison.OrdinalIgnoreCase))
            {
                CapabilityEnsureRequests.Add($"{path}{query}");
                var body = """
                {
                  "data": {
                    "status": "started"
                  }
                }
                """;
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(body, Encoding.UTF8, "application/json")
                });
            }

            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound));
        }
    }
}

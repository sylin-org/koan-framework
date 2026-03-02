using System.Net;
using System.Text;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Koan.ZenGarden.Tests;

[Collection("ZenGardenEnvSerial")]
public sealed class ZenGardenContainerResolutionTests
{
    [Fact]
    public async Task CatalogAsync_in_container_mode_uses_configured_container_host_endpoint()
    {
        using var env = EnvironmentScope.Override(new Dictionary<string, string?>
        {
            [Constants.EnvironmentVariables.DotnetRunningInContainer] = "true",
            [Constants.EnvironmentVariables.ContainerHost] = "moss-container",
            [Constants.EnvironmentVariables.GardenStone] = null,
            [Constants.EnvironmentVariables.PreferredStoneName] = null,
            [Constants.EnvironmentVariables.CachePath] = null
        });

        var handler = new ContainerHostMossHandler("moss-container", healthy: true);
        using var httpClient = new HttpClient(handler);
        using var client = new ZenGardenClient(
            httpClient,
            NullLogger<ZenGardenClient>.Instance,
            new ZenGardenOptions
            {
                EnableDiscovery = false,
                RequireHostMossWhenContainerized = true,
                PersistDiscoveryCache = false,
                KoiDiscoveryEnabled = false
            });

        var tools = await client.CatalogAsync(ZenGardenSubscription.ForOffering("ollama"));

        tools.Should().ContainSingle();
        handler.RequestHosts.Should().Contain("moss-container");
    }

    [Fact]
    public async Task CatalogAsync_in_container_mode_fails_fast_when_host_moss_is_unreachable()
    {
        using var env = EnvironmentScope.Override(new Dictionary<string, string?>
        {
            [Constants.EnvironmentVariables.DotnetRunningInContainer] = "true",
            [Constants.EnvironmentVariables.ContainerHost] = "moss-container",
            [Constants.EnvironmentVariables.GardenStone] = null,
            [Constants.EnvironmentVariables.PreferredStoneName] = null,
            [Constants.EnvironmentVariables.CachePath] = null
        });

        var handler = new ContainerHostMossHandler("moss-container", healthy: false);
        using var httpClient = new HttpClient(handler);
        using var client = new ZenGardenClient(
            httpClient,
            NullLogger<ZenGardenClient>.Instance,
            new ZenGardenOptions
            {
                EnableDiscovery = false,
                RequireHostMossWhenContainerized = true,
                PersistDiscoveryCache = false,
                KoiDiscoveryEnabled = false
            });

        var act = async () => await client.CatalogAsync(ZenGardenSubscription.ForOffering("ollama"));

        await act.Should()
            .ThrowAsync<InvalidOperationException>()
            .WithMessage("*Containerized runtime requires host Moss endpoint*");
    }

    private sealed class ContainerHostMossHandler : HttpMessageHandler
    {
        private readonly string _expectedHost;
        private readonly bool _healthy;

        public List<string> RequestHosts { get; } = new();

        public ContainerHostMossHandler(string expectedHost, bool healthy)
        {
            _expectedHost = expectedHost;
            _healthy = healthy;
        }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var uri = request.RequestUri ?? throw new InvalidOperationException("Request URI is required.");
            RequestHosts.Add(uri.Host);

            if (!string.Equals(uri.Host, _expectedHost, StringComparison.OrdinalIgnoreCase))
            {
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.BadRequest));
            }

            if (uri.AbsolutePath.Equals("/health", StringComparison.OrdinalIgnoreCase))
            {
                return Task.FromResult(new HttpResponseMessage(_healthy ? HttpStatusCode.OK : HttpStatusCode.ServiceUnavailable));
            }

            if (uri.AbsolutePath.Equals("/api/v1/garden/tools", StringComparison.OrdinalIgnoreCase))
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

            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound));
        }
    }

    private sealed class EnvironmentScope : IDisposable
    {
        private static readonly object Gate = new();
        private readonly Dictionary<string, string?> _originalValues;

        private EnvironmentScope(Dictionary<string, string?> originalValues)
        {
            _originalValues = originalValues;
        }

        public static EnvironmentScope Override(IReadOnlyDictionary<string, string?> values)
        {
            lock (Gate)
            {
                var original = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
                foreach (var pair in values)
                {
                    original[pair.Key] = Environment.GetEnvironmentVariable(pair.Key);
                    Environment.SetEnvironmentVariable(pair.Key, pair.Value);
                }

                return new EnvironmentScope(original);
            }
        }

        public void Dispose()
        {
            lock (Gate)
            {
                foreach (var pair in _originalValues)
                {
                    Environment.SetEnvironmentVariable(pair.Key, pair.Value);
                }
            }
        }
    }
}

[CollectionDefinition("ZenGardenEnvSerial", DisableParallelization = true)]
public sealed class ZenGardenEnvSerialCollection;

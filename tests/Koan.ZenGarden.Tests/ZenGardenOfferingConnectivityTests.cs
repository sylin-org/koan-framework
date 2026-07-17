using System.Net;
using System.Text.Json;
using AwesomeAssertions;
using Koan.ZenGarden.Core;
using Koan.ZenGarden.Extensions;
using Koan.ZenGarden.Models;
using Koan.ZenGarden.Tests.Support;
using Microsoft.Extensions.DependencyInjection;
using MongoDB.Bson;
using MongoDB.Driver;
using Xunit;

namespace Koan.ZenGarden.Tests;

public sealed class ZenGardenOfferingConnectivityTests : IClassFixture<ZenGardenFixture>
{
    private readonly ZenGardenFixture _fixture;
    private readonly ITestOutputHelper _output;

    public ZenGardenOfferingConnectivityTests(ZenGardenFixture fixture, ITestOutputHelper output)
    {
        _fixture = fixture;
        _output = output;
    }

    [Fact]
    public async Task MongoDb_resolution_through_zen_garden_connects_when_offering_exists()
    {
        if (!EnsureGardenAvailable())
        {
            return;
        }

        var candidates = await FindOfferingCandidates("mongodb");
        if (candidates.Count == 0)
        {
            _output.WriteLine("No mongodb offering found in the garden; skipping mongo connectivity probe.");
            return;
        }

        await using var scope = BuildInitializationScope();
        var provider = scope.GetRequiredService<IZenGardenInitializationProvider>();
        ZenGardenConnectionIntent.TryParse("zen-garden://mongodb", out var intent).Should().BeTrue();

        var resolved = await provider.Resolve(intent!);
        resolved.Should().NotBeNull(DescribeFailure("mongodb", candidates));

        var mongoUri = ZenGardenEndpointContractHandler.ResolveMongoEndpointOrThrow(resolved!);
        mongoUri.Should().NotBeNullOrWhiteSpace("mongodb offering must expose a usable mongodb URI");

        await AssertMongoPing(mongoUri!);
    }

    [Fact]
    public async Task Ollama_resolution_through_zen_garden_connects_when_offering_exists()
    {
        if (!EnsureGardenAvailable())
        {
            return;
        }

        var candidates = await FindOfferingCandidates("ollama");
        if (candidates.Count == 0)
        {
            _output.WriteLine("No ollama offering found in the garden; skipping ollama connectivity probe.");
            return;
        }

        await using var scope = BuildInitializationScope();
        var provider = scope.GetRequiredService<IZenGardenInitializationProvider>();
        ZenGardenConnectionIntent.TryParse("zen-garden://ollama", out var intent).Should().BeTrue();

        var resolved = await provider.Resolve(intent!);
        resolved.Should().NotBeNull(DescribeFailure("ollama", candidates));

        var endpoint = ZenGardenEndpointContractHandler.ResolveOllamaEndpointOrThrow(resolved!);
        endpoint.Should().NotBeNullOrWhiteSpace("ollama offering must expose an HTTP endpoint");

        await AssertOllamaTags(endpoint!);
    }

    private bool EnsureGardenAvailable()
    {
        if (_fixture.IsAvailable)
        {
            return true;
        }

        var reason = string.IsNullOrWhiteSpace(_fixture.UnavailableReason)
            ? "unknown"
            : _fixture.UnavailableReason;
        var message = $"Zen Garden endpoint resolution '{_fixture.EndpointDisplay}' unavailable: {reason}";

        if (_fixture.RequireAvailable)
        {
            Assert.Fail(message);
        }

        _output.WriteLine(message);
        _output.WriteLine("Set KOAN_TESTS_ZENGARDEN_REQUIRED=1 to make this a hard failure.");
        return false;
    }

    private async Task<IReadOnlyList<ZenGardenToolSnapshot>> FindOfferingCandidates(string offering)
    {
        var all = await _fixture.Client.Catalog(new ZenGardenSubscription
        {
            ToolType = ZenGardenToolType.Offering
        });

        var query = Core.ToolFqid.Parse(offering);

        return all
            .Where(tool => query.MatchesSnapshot(tool.ToolFqid, tool.OfferingType, tool.Aliases))
            .OrderBy(tool => string.Equals(tool.ToolFqid, offering, StringComparison.OrdinalIgnoreCase) ? 0 : 1)
            .ThenBy(tool => tool.Aliases.Any(alias => string.Equals(alias, offering, StringComparison.OrdinalIgnoreCase)) ? 0 : 1)
            .ThenBy(tool => tool.ToolFqid, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static string DescribeFailure(string offering, IReadOnlyList<ZenGardenToolSnapshot> candidates)
    {
        var states = string.Join(
            ", ",
            candidates.Select(candidate =>
                $"{candidate.ToolFqid} ready={candidate.Ready} state={candidate.State}"));
        return $"offering '{offering}' exists in catalog but did not resolve a ready endpoint ({states})";
    }

    private ServiceProvider BuildInitializationScope()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddZenGardenRuntime(configure: options =>
        {
            options.Endpoint = _fixture.Endpoint;
            options.EnableDiscovery = true;
            options.HttpTimeoutSeconds = 8;
            options.StreamReconnectDelaySeconds = 1;
        });
        return services.BuildServiceProvider();
    }

    private static async Task AssertMongoPing(string connectionString)
    {
        var settings = MongoClientSettings.FromConnectionString(connectionString);
        settings.ConnectTimeout = TimeSpan.FromSeconds(5);
        settings.ServerSelectionTimeout = TimeSpan.FromSeconds(8);
        settings.SocketTimeout = TimeSpan.FromSeconds(8);

        var client = new MongoClient(settings);
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(12));
        var result = await client.GetDatabase("admin").RunCommandAsync<BsonDocument>(
            new BsonDocument("ping", 1),
            cancellationToken: cts.Token);

        result.TryGetValue("ok", out var okValue).Should().BeTrue();
        okValue.ToDouble().Should().BeGreaterThanOrEqualTo(1.0);
    }

    private static async Task AssertOllamaTags(string endpoint)
    {
        if (!Uri.TryCreate(endpoint, UriKind.Absolute, out var uri))
        {
            Assert.Fail($"Resolved ollama endpoint '{endpoint}' is not a valid absolute URI.");
            return;
        }

        if (!string.Equals(uri.Scheme, Uri.UriSchemeHttp, StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(uri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase))
        {
            var builder = new UriBuilder(uri)
            {
                Scheme = Uri.UriSchemeHttp,
                Port = uri.IsDefaultPort ? -1 : uri.Port
            };
            uri = builder.Uri;
        }

        var normalized = new UriBuilder(uri)
        {
            Path = "/"
        }.Uri;

        using var client = new HttpClient
        {
            BaseAddress = normalized,
            Timeout = TimeSpan.FromSeconds(10)
        };

        using var response = await client.GetAsync("api/tags");
        response.StatusCode.Should().NotBe(HttpStatusCode.NotFound);
        response.IsSuccessStatusCode.Should().BeTrue($"ollama endpoint '{normalized}' should answer GET /api/tags");

        var payload = await response.Content.ReadAsStringAsync();
        payload.Should().NotBeNullOrWhiteSpace();

        using var document = JsonDocument.Parse(payload);
        document.RootElement.TryGetProperty("models", out var models).Should().BeTrue();
        models.ValueKind.Should().Be(JsonValueKind.Array);
    }
}

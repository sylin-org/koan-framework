using System.Net;
using AwesomeAssertions;
using Koan.Core.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Koan.Web.WellKnown.Tests;

public sealed class WellKnownFactsSpec : IClassFixture<WellKnownWebApplicationFactory>
{
    private readonly WellKnownWebApplicationFactory _factory;
    private readonly HttpClient _client;

    public WellKnownFactsSpec(WellKnownWebApplicationFactory factory)
    {
        _factory = factory;
        _client = factory.CreateClient();
    }

    [Fact]
    public async Task Endpoint_projects_the_exact_host_fact_envelope()
    {
        var response = await _client.GetAsync("/.well-known/Koan/facts");
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        response.Headers.CacheControl!.NoStore.Should().BeTrue();

        var json = await response.Content.ReadAsStringAsync();
        var expected = KoanFactJson.Serialize(
            _factory.Services.GetRequiredService<IKoanRuntimeFacts>().Current);

        json.Should().Be(expected);
        var envelope = KoanFactJson.Deserialize(json);
        envelope.Should().NotBeNull();
        envelope!.Complete.Should().BeTrue();
        envelope.Facts.Should().Contain(fact => fact.Kind == KoanFactKind.Election);
    }
}

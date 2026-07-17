using System.Net;
using System.Net.Http.Json;
using AwesomeAssertions;
using GardenCoopEmbedded;
using Koan.Core.Diagnostics;
using Microsoft.Extensions.DependencyInjection;

namespace Koan.Samples.GardenCoopEmbedded.Tests;

public sealed class GardenCoopEmbeddedGoldenPathSpec(GardenCoopEmbeddedFixture fixture)
    : IClassFixture<GardenCoopEmbeddedFixture>
{
    [Fact]
    public async Task Fresh_host_finds_tomatoes_locally_and_explains_its_composition()
    {
        using var client = fixture.CreateClient();

        var dashboard = await client.GetAsync("/");
        dashboard.StatusCode.Should().Be(HttpStatusCode.OK);
        (await dashboard.Content.ReadAsStringAsync()).Should().Contain("Find produce by meaning");

        var produce = await client.GetFromJsonAsync<Produce[]>("/api/produce");
        produce.Should().HaveCount(5);

        var hits = await client.GetFromJsonAsync<SearchHit[]>(
            "/api/produce/search?q=ripe%20red%20tomato&k=3");
        hits.Should().HaveCount(3);
        hits![0].Id.Should().Be("heirloom-tomatoes");
        hits[0].Name.Should().Be("Heirloom Tomatoes");
        hits[0].Score.Should().BeGreaterThan(0.7);

        (await client.GetAsync("/health/ready")).StatusCode.Should().Be(HttpStatusCode.OK);

        var factsResponse = await client.GetAsync("/.well-known/Koan/facts");
        factsResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var factsJson = await factsResponse.Content.ReadAsStringAsync();
        factsJson.Should().Contain("Sylin.Koan.AI.Connector.Onnx");
        factsJson.Should().Contain("Sylin.Koan.Data.Vector.Connector.SqliteVec");
        factsJson.Should().NotContain("CollectionFailed");

        var facts = fixture.Services.GetRequiredService<IKoanRuntimeFacts>().Current;
        facts.Complete.Should().BeTrue();
        facts.Facts.Should().Contain(fact =>
            fact.Code == "koan.data.adapter.selected"
            && fact.Subject == "data:default"
            && fact.Summary.Contains("sqlite", StringComparison.OrdinalIgnoreCase));
        facts.Facts.Should().NotContain(fact => fact.State == KoanFactState.CollectionFailed);
    }

    public sealed record SearchHit(string Id, string Name, string Category, double Score);
}

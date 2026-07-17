using System.Net;
using System.Text.Json;
using AwesomeAssertions;
using Koan.Core.Diagnostics;
using Koan.Data.Core;
using Microsoft.Extensions.DependencyInjection;
using S10.DevPortal.Models;

namespace Koan.Samples.S10DevPortal.Tests;

public sealed class S10DevPortalGoldenPathSpec(S10DevPortalFixture fixture) : IClassFixture<S10DevPortalFixture>
{
    [Fact]
    public async Task Fresh_host_publishes_the_approved_set_idempotently_and_explains_its_default()
    {
        using var client = fixture.CreateClient();

        var dashboard = await client.GetAsync("/");
        dashboard.StatusCode.Should().Be(HttpStatusCode.OK);
        (await dashboard.Content.ReadAsStringAsync()).Should().Contain("Publish the business intent");

        var readiness = await client.GetAsync("/health/ready");
        readiness.StatusCode.Should().Be(HttpStatusCode.OK);

        var reset = await ReadJson(await client.PostAsync("/api/publication/reset", content: null));
        reset.RootElement.GetProperty("total").GetInt32().Should().Be(3);
        reset.RootElement.GetProperty("approved").GetInt32().Should().Be(2);
        reset.RootElement.GetProperty("drafts").GetInt32().Should().Be(1);

        var first = await ReadJson(await client.PostAsync("/api/publication/preview", content: null));
        AssertPreview(first.RootElement);

        var second = await ReadJson(await client.PostAsync("/api/publication/preview", content: null));
        AssertPreview(second.RootElement);

        var preview = await ReadJson(await client.GetAsync("/api/publication/preview"));
        preview.RootElement.GetProperty("count").GetInt32().Should().Be(2);
        ReadIds(preview.RootElement.GetProperty("ids")).Should().Equal(ExpectedApprovedIds);

        var editorial = await Article.All(TestContext.Current.CancellationToken);
        editorial.Should().HaveCount(3);
        editorial.Count(article => article.Status == ArticleStatus.Draft).Should().Be(1);

        var unavailable = await client.PostAsync("/api/publication/documents", content: null);
        unavailable.StatusCode.Should().Be(HttpStatusCode.ServiceUnavailable);
        (await unavailable.Content.ReadAsStringAsync()).Should().Contain("docker compose");

        (await Article.All(TestContext.Current.CancellationToken)).Should().HaveCount(3);

        var factsResponse = await client.GetAsync("/.well-known/Koan/facts");
        factsResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        (await factsResponse.Content.ReadAsStringAsync()).Should().NotContain("CollectionFailed");

        var snapshot = fixture.Services.GetRequiredService<IKoanRuntimeFacts>().Current;
        snapshot.Complete.Should().BeTrue();
        snapshot.Facts.Should().Contain(fact =>
            fact.Code == "koan.data.adapter.selected"
            && fact.Subject == "data:default"
            && fact.Summary.Contains("sqlite", StringComparison.OrdinalIgnoreCase));
        snapshot.Facts.Should().Contain(fact =>
            fact.Subject == "data:preview"
            && fact.Summary.Contains("sqlite", StringComparison.OrdinalIgnoreCase));
        snapshot.Facts.Should().NotContain(fact => fact.State == KoanFactState.CollectionFailed);
    }

    private static void AssertPreview(JsonElement response)
    {
        response.GetProperty("channel").GetString().Should().Be("Preview");
        response.GetProperty("readCount").GetInt32().Should().Be(2);
        response.GetProperty("copiedCount").GetInt32().Should().Be(2);
        var snapshot = response.GetProperty("articles");
        snapshot.GetProperty("count").GetInt32().Should().Be(2);
        ReadIds(snapshot.GetProperty("ids")).Should().Equal(ExpectedApprovedIds);
    }

    private static readonly string[] ExpectedApprovedIds =
    [
        "article-composition",
        "article-entities"
    ];

    private static string[] ReadIds(JsonElement ids) =>
        ids.EnumerateArray().Select(id => id.GetString()!).ToArray();

    private static async Task<JsonDocument> ReadJson(HttpResponseMessage response)
    {
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        return await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync());
    }
}

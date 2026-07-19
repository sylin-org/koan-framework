using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using AnimeRecommendations.Domain;
using AwesomeAssertions;

namespace Koan.Samples.AnimeRecommendations.Tests;

public sealed class AnimeRecommendationsGoldenPathSpec(AnimeRecommendationsFixture fixture)
    : IClassFixture<AnimeRecommendationsFixture>
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    [Fact]
    public async Task Ratings_shape_local_explainable_recommendations_without_application_plumbing()
    {
        using var client = fixture.CreateClient();

        var page = await client.GetAsync("/");
        page.StatusCode.Should().Be(HttpStatusCode.OK);
        (await page.Content.ReadAsStringAsync()).Should().Contain("Find your next <em>obsession.</em>");

        var catalog = await client.GetFromJsonAsync<Anime[]>("/api/anime/catalog", Json);
        catalog.Should().HaveCount(24);

        var library = await client.GetFromJsonAsync<LibraryEntry[]>("/api/anime/library", Json);
        library.Should().HaveCount(3);

        var feed = await client.GetFromJsonAsync<RecommendationFeed>(
            "/api/anime/recommendations?viewerId=demo&mood=hopeful%20adventure&take=6",
            Json);
        feed.Should().NotBeNull();
        feed!.TasteAnchors.Should().BeEquivalentTo(
            "Cowboy Bebop",
            "Frieren: Beyond Journey's End",
            "Spy × Family");
        feed.Items.Should().HaveCount(6);
        feed.Items.Should().OnlyContain(item => !library!.Any(entry => entry.AnimeId == item.Anime.Id));
        feed.Items.Should().OnlyContain(item => item.Score > 0 && !string.IsNullOrWhiteSpace(item.Reason));

        var ratedRecommendation = feed.Items[0].Anime;
        var ratingResponse = await client.PutAsJsonAsync(
            $"/api/anime/viewers/demo/ratings/{ratedRecommendation.Id}",
            new { Rating = 5 });
        ratingResponse.StatusCode.Should().Be(HttpStatusCode.OK);

        var refreshed = await client.GetFromJsonAsync<RecommendationFeed>(
            "/api/anime/recommendations?viewerId=demo&mood=hopeful%20adventure&take=6",
            Json);
        refreshed!.Items.Should().NotContain(item => item.Anime.Id == ratedRecommendation.Id);
        refreshed.TasteAnchors.Should().Contain(ratedRecommendation.Title);

        var invalidRating = await client.PutAsJsonAsync(
            "/api/anime/viewers/demo/ratings/pluto",
            new { Rating = 6 });
        invalidRating.StatusCode.Should().Be(HttpStatusCode.BadRequest);

        var unboundedRequest = await client.GetAsync(
            "/api/anime/recommendations?viewerId=demo&mood=anything&take=21");
        unboundedRequest.StatusCode.Should().Be(HttpStatusCode.BadRequest);

        (await client.GetAsync("/health/ready")).StatusCode.Should().Be(HttpStatusCode.OK);
        var facts = await client.GetStringAsync("/.well-known/Koan/facts");
        facts.Should().Contain("Sylin.Koan.AI.Connector.Onnx");
        facts.Should().Contain("Sylin.Koan.Data.Vector.Connector.SqliteVec");
        facts.Should().NotContain("CollectionFailed");
    }
}

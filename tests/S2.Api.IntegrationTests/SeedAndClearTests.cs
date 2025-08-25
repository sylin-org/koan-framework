using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Xunit;

namespace S2.Api.IntegrationTests;

public class SeedAndClearTests : IClassFixture<MongoFixture>
{
    private readonly MongoFixture _fx;
    public SeedAndClearTests(MongoFixture fx) => _fx = fx;

    [Fact]
    public async Task Seed_replaces_contents_and_Clear_empties_with_pagination_headers()
    {
        if (!_fx.Available)
        {
            return; // Skip when Docker/Testcontainers not available.
        }

        await using var app = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.ConfigureAppConfiguration((ctx, cfg) =>
            {
                cfg.AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["DOTNET_ENVIRONMENT"] = "Development",
                    ["ConnectionStrings:Default"] = _fx.ConnectionString,
                    ["Sora:Data:Mongo:Database"] = "s2test_seedclear"
                });
            });
        });

        var client = app.CreateClient();

        // Start from a known state
        _ = await client.DeleteAsync("/api/items/clear");

        // Seed 5
        var s5 = await client.PostAsync("/api/items/seed/5", new StringContent("{}", System.Text.Encoding.UTF8, "application/json"));
        s5.EnsureSuccessStatusCode();

        var list5 = await client.GetAsync("/api/items?page=1&size=200");
        list5.StatusCode.Should().Be(HttpStatusCode.OK);
        var items5 = await list5.Content.ReadFromJsonAsync<List<JsonElement>>();
        items5.Should().NotBeNull();
        items5!.Count.Should().Be(5);
        list5.Headers.TryGetValues("X-Total-Count", out var tc5).Should().BeTrue();
        tc5!.Should().ContainSingle().Which.Should().Be("5");

        // Seed 3 (replace all)
        var s3 = await client.PostAsync("/api/items/seed/3", new StringContent("{}", System.Text.Encoding.UTF8, "application/json"));
        s3.EnsureSuccessStatusCode();

        var list3 = await client.GetAsync("/api/items?page=1&size=200");
        var items3 = await list3.Content.ReadFromJsonAsync<List<JsonElement>>();
        items3.Should().NotBeNull();
        items3!.Count.Should().Be(3);
        list3.Headers.TryGetValues("X-Total-Count", out var tc3).Should().BeTrue();
        tc3!.Should().ContainSingle().Which.Should().Be("3");

        // Clear
        var clr = await client.DeleteAsync("/api/items/clear");
        clr.EnsureSuccessStatusCode();

        var list0 = await client.GetAsync("/api/items?page=1&size=200");
        var items0 = await list0.Content.ReadFromJsonAsync<List<JsonElement>>();
        (items0?.Count ?? -1).Should().Be(0);
        list0.Headers.TryGetValues("X-Total-Count", out var tc0).Should().BeTrue();
        tc0!.Should().ContainSingle().Which.Should().Be("0");
    }
}
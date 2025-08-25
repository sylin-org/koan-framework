using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using System.Net.Http.Json;
using Xunit;

namespace S2.Api.IntegrationTests;

public class ApiTests : IClassFixture<MongoFixture>
{
    private readonly MongoFixture _fx;
    public ApiTests(MongoFixture fx) => _fx = fx;

    [Fact]
    public async Task Health_and_CRUD_work_with_Mongo()
    {
        if (!_fx.Available)
        {
            // Skip when Docker/Testcontainers not available in the environment.
            return;
        }
        await using var app = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.ConfigureAppConfiguration((ctx, cfg) =>
            {
                cfg.AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["DOTNET_ENVIRONMENT"] = "Development",
                    ["ConnectionStrings:Default"] = _fx.ConnectionString,
                    ["Sora:Data:Mongo:Database"] = "s2test"
                });
            });
        });

        var client = app.CreateClient();
        var health = await client.GetStringAsync("/api/health");
        health.Should().Contain("ok");

        var res = await client.PostAsJsonAsync("/api/items", new { name = "Hello" });
        res.EnsureSuccessStatusCode();

        var list = await client.GetFromJsonAsync<List<dynamic>>("/api/items");
        list.Should().NotBeNull();
        list!.Count.Should().BeGreaterThan(0);
    }
}
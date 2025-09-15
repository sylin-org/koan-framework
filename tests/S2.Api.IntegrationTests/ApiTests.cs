using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Newtonsoft.Json;
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
                    ["Koan:Data:Mongo:Database"] = "s2test"
                });
            });
        });

        var client = app.CreateClient();
        var health = await client.GetStringAsync("/api/health");
        health.Should().Contain("ok");

    var res = await client.PostAsync("/api/items", new StringContent(JsonConvert.SerializeObject(new { name = "Hello" }), System.Text.Encoding.UTF8, "application/json"));
        res.EnsureSuccessStatusCode();

    var listJson = await client.GetStringAsync("/api/items");
    var list = JsonConvert.DeserializeObject<List<dynamic>>(listJson);
    list.Should().NotBeNull();
    list!.Count.Should().BeGreaterThan(0);
    }
}
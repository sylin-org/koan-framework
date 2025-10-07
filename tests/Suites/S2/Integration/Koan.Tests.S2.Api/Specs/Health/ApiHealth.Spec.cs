using Koan.Web.Transformers;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using S2.Api;
using S2.Api.Controllers;
using System.Text;
using System.Text.Json;
using Xunit.Abstractions;

namespace Koan.Tests.S2.Api.Specs.Health;

public sealed class ApiHealthSpec
{
    private readonly ITestOutputHelper _output;

    public ApiHealthSpec(ITestOutputHelper output)
    {
        _output = output;
    }

    [Fact]
    public async Task Health_and_crud_flow_works_against_mongo_fixture()
    {
        var databaseName = $"koan_tests_{Guid.NewGuid():N}";

        await TestPipeline.For<ApiHealthSpec>(_output, nameof(Health_and_crud_flow_works_against_mongo_fixture))
            .RequireDocker()
            .UsingMongoContainer(database: databaseName)
            .Act(async ctx =>
            {
                var mongo = ctx.GetMongoFixture();
                if (!mongo.IsAvailable || string.IsNullOrWhiteSpace(mongo.ConnectionString))
                {
                    throw new InvalidOperationException($"Mongo unavailable: {mongo.UnavailableReason}");
                }

                await using var app = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
                {
                    builder.UseSetting("urls", "http://localhost:0");
                    builder.UseSetting("DOTNET_ENVIRONMENT", "Development");
                    builder.UseSetting("Koan:Data:Mongo:ConnectionString", mongo.ConnectionString);
                    builder.UseSetting("Koan:Data:Mongo:Database", databaseName);
                    builder.UseSetting("Koan:Data:Mongo:CollectionPrefix", ctx.ExecutionId.ToString("N"));
                    builder.ConfigureServices(services =>
                    {
                        services.AddEntityTransformer<Item, string, ItemCsvTransformer>("text/csv");
                    });
                });

                await using (var scope = app.Services.CreateAsyncScope())
                {
                    var registry = scope.ServiceProvider.GetService(typeof(ITransformerRegistry)) as ITransformerRegistry;
                    if (registry is null)
                    {
                        throw new InvalidOperationException("Transformer registry not available");
                    }

                    var contentTypes = registry.GetContentTypes<Item>();
                    contentTypes.Should().Contain("text/csv");
                }

                using var client = app.CreateClient();
                var cancellation = ctx.Cancellation;

                var healthResponse = await client.GetAsync("/api/health", cancellation).ConfigureAwait(false);
                healthResponse.EnsureSuccessStatusCode();
                var healthBody = await healthResponse.Content.ReadAsStringAsync(cancellation).ConfigureAwait(false);
                healthBody.Should().Contain("ok");

                var clearResponse = await client.DeleteAsync("/api/items/clear", cancellation).ConfigureAwait(false);
                if (!clearResponse.IsSuccessStatusCode)
                {
                    var clearBody = await clearResponse.Content.ReadAsStringAsync(cancellation).ConfigureAwait(false);
                    throw new InvalidOperationException($"Failed to clear items: {clearResponse.StatusCode} {clearBody}");
                }

                var newId = Guid.NewGuid().ToString("N");
                var csvPayload = $"Id,Name\n{newId},integration";
                using var createContent = new StringContent(csvPayload, Encoding.UTF8, "text/csv");
                var createResponse = await client.PostAsync("/api/items", createContent, cancellation).ConfigureAwait(false);
                if (!createResponse.IsSuccessStatusCode)
                {
                    var createBody = await createResponse.Content.ReadAsStringAsync(cancellation).ConfigureAwait(false);
                    throw new InvalidOperationException($"Failed to create item: {createResponse.StatusCode} {createBody}");
                }

                var listResponse = await client.GetAsync("/api/items", cancellation).ConfigureAwait(false);
                if (!listResponse.IsSuccessStatusCode)
                {
                    var listErrorBody = await listResponse.Content.ReadAsStringAsync(cancellation).ConfigureAwait(false);
                    throw new InvalidOperationException($"Failed to list items: {listResponse.StatusCode} {listErrorBody}");
                }
                var listBody = await listResponse.Content.ReadAsStringAsync(cancellation).ConfigureAwait(false);
                using var listDoc = JsonDocument.Parse(listBody);
                listDoc.RootElement.ValueKind.Should().Be(JsonValueKind.Array);
                listDoc.RootElement.GetArrayLength().Should().BeGreaterThan(0);
            })
            .RunAsync();
    }
}

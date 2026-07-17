using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using AwesomeAssertions;
using Koan.Core.Diagnostics;
using Microsoft.Extensions.DependencyInjection;

namespace Koan.Samples.TaskGraph.Tests;

public sealed class TaskGraphGoldenPathSpec(TaskGraphFixture fixture) : IClassFixture<TaskGraphFixture>
{
    [Fact]
    public async Task Fresh_host_executes_and_explains_one_set_and_stream_relationships()
    {
        using var client = fixture.CreateClient();

        var dashboard = await client.GetAsync("/");
        dashboard.StatusCode.Should().Be(HttpStatusCode.OK);
        (await dashboard.Content.ReadAsStringAsync()).Should().Contain("Read the business, not the plumbing.");

        var reset = await client.PostAsync("/api/todos/reset-demo", content: null);
        reset.StatusCode.Should().Be(HttpStatusCode.OK);
        var resetJson = await ReadJson(reset);
        resetJson.RootElement.GetProperty("users").GetInt32().Should().Be(2);
        resetJson.RootElement.GetProperty("categories").GetInt32().Should().Be(2);
        resetJson.RootElement.GetProperty("todos").GetInt32().Should().Be(3);
        resetJson.RootElement.GetProperty("todoItems").GetInt32().Should().Be(4);

        var scalar = await client.GetAsync("/api/todos/todo-proposal/context");
        scalar.StatusCode.Should().Be(HttpStatusCode.OK);
        var scalarJson = await ReadJson(scalar);
        var scalarRoot = scalarJson.RootElement;
        scalarRoot.GetProperty("entity").GetProperty("id").GetString().Should().Be("todo-proposal");
        scalarRoot.GetProperty("parents").EnumerateObject().Should().HaveCount(2);
        scalarRoot.GetProperty("children").GetProperty("todoItem").GetProperty("todoId")
            .GetArrayLength().Should().Be(2);

        var set = await ReadJson(await client.GetAsync("/api/todos/relationships/set?limit=3"));
        var stream = await ReadJson(await client.GetAsync("/api/todos/relationships/stream?limit=3"));
        EntityIds(set.RootElement).Should().Equal(EntityIds(stream.RootElement));
        EntityIds(stream.RootElement).Should().HaveCount(3);

        (await client.GetAsync("/api/categories/category-work")).StatusCode.Should().Be(HttpStatusCode.OK);
        (await client.GetAsync("/api/categories/category-work")).StatusCode.Should().Be(HttpStatusCode.OK);

        var factsResponse = await client.GetAsync("/.well-known/Koan/facts");
        factsResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        (await factsResponse.Content.ReadAsStringAsync()).Should().NotContain("CollectionFailed");

        var snapshot = fixture.Services.GetRequiredService<IKoanRuntimeFacts>().Current;
        snapshot.Complete.Should().BeTrue();
        snapshot.Facts.Should().Contain(fact =>
            fact.Code == "koan.cache.entity-plan.resolved"
            && fact.Subject.EndsWith("TaskGraph.Category", StringComparison.Ordinal));
        snapshot.Facts.Should().NotContain(fact => fact.State == KoanFactState.CollectionFailed);
    }

    private static async Task<JsonDocument> ReadJson(HttpResponseMessage response)
    {
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        return await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync());
    }

    private static string[] EntityIds(JsonElement graphs)
        => graphs.EnumerateArray()
            .Select(graph => graph.GetProperty("entity").GetProperty("id").GetString()!)
            .ToArray();
}

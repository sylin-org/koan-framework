using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using AwesomeAssertions;
using GardenCoop.Infrastructure;
using GardenCoop.Models;
using Koan.Core.Diagnostics;
using Microsoft.Extensions.DependencyInjection;

namespace Koan.Samples.GardenCoop.C02.Tests;

public sealed class GardenCoopC02GoldenPathSpec(GardenCoopC02Fixture fixture)
    : IClassFixture<GardenCoopC02Fixture>
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() },
    };

    [Fact]
    public async Task Chapter_two_preserves_the_garden_story_and_adds_local_discovery()
    {
        using var client = fixture.CreateClient();

        var dashboard = await client.GetAsync("/");
        dashboard.StatusCode.Should().Be(HttpStatusCode.OK);
        var dashboardHtml = await dashboard.Content.ReadAsStringAsync();
        dashboardHtml.Should().Contain("Garden Cooperative Journal");
        dashboardHtml.Should().Contain("Find the harvest by meaning");

        var plots = await client.GetFromJsonAsync<Plot[]>(GardenApiRoutes.Plots, Json);
        var sensors = await client.GetFromJsonAsync<Sensor[]>(GardenApiRoutes.Sensors, Json);
        var readings = await client.GetFromJsonAsync<Reading[]>(GardenApiRoutes.Readings, Json);
        var reminders = await client.GetFromJsonAsync<Reminder[]>(GardenApiRoutes.Reminders, Json);

        plots.Should().HaveCount(3);
        sensors.Should().HaveCount(3);
        readings.Should().HaveCount(3);
        reminders.Should().ContainSingle(reminder => reminder.Status == ReminderStatus.Active);

        var dryPlot = plots!.Single(plot => plot.Name == "Bed 3");
        var drySensor = sensors!.Single(sensor => sensor.PlotId == dryPlot.Id);
        var recovery = new Reading
        {
            SensorSerial = drySensor.Id,
            SoilHumidity = 100,
            TemperatureC = 22,
            SampledAt = DateTimeOffset.UtcNow,
        };

        var recoveryResponse = await client.PostAsJsonAsync(GardenApiRoutes.Readings, recovery);
        recoveryResponse.StatusCode.Should().Be(HttpStatusCode.OK);

        reminders = await client.GetFromJsonAsync<Reminder[]>(GardenApiRoutes.Reminders, Json);
        reminders.Should().ContainSingle(reminder =>
            reminder.PlotId == dryPlot.Id && reminder.Status == ReminderStatus.Acknowledged);

        var produce = await client.GetFromJsonAsync<Produce[]>(GardenApiRoutes.Produce, Json);
        produce.Should().HaveCount(5);

        var hits = await client.GetFromJsonAsync<SearchHit[]>(
            $"/{GardenApiRoutes.ProduceSearch}?q=ripe%20red%20tomato&k=3");
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

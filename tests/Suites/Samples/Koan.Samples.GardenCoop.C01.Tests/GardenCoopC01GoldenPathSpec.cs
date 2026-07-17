using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using AwesomeAssertions;
using GardenCoop.Infrastructure;
using GardenCoop.Models;
using Koan.Core.Diagnostics;
using Microsoft.Extensions.DependencyInjection;

namespace Koan.Samples.GardenCoop.C01.Tests;

public sealed class GardenCoopC01GoldenPathSpec(GardenCoopC01Fixture fixture) : IClassFixture<GardenCoopC01Fixture>
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() },
    };

    [Fact]
    public async Task Fresh_host_explains_and_executes_the_complete_garden_story()
    {
        using var client = fixture.CreateClient();

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

        var response = await client.PostAsJsonAsync(GardenApiRoutes.Readings, recovery);
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        reminders = await client.GetFromJsonAsync<Reminder[]>(GardenApiRoutes.Reminders, Json);
        reminders.Should().ContainSingle(reminder =>
            reminder.PlotId == dryPlot.Id && reminder.Status == ReminderStatus.Acknowledged);

        var factsResponse = await client.GetAsync("/.well-known/Koan/facts");
        factsResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var factsJson = await factsResponse.Content.ReadAsStringAsync();
        factsJson.Should().Contain("Sylin.GardenCoop.C01");
        factsJson.Should().NotContain("CollectionFailed");

        var facts = fixture.Services.GetRequiredService<IKoanRuntimeFacts>().Current;
        facts.Complete.Should().BeTrue();
    }
}

using System;
using g1c1.GardenCoop.Models;
using Koan.Data.Core;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace g1c1.GardenCoop.Infrastructure;

public static class GardenSeeder
{
    public static async Task EnsureSampleDataAsync(IServiceProvider services, ILogger logger, CancellationToken cancellationToken = default)
    {
        using var scope = services.CreateScope();
        var ct = cancellationToken;

        if (await Plot.Count(ct) > 0)
        {
            return;
        }

        logger.LogInformation("Planting starter plots for the Garden Cooperative sample.");

        var riley = await new Member { DisplayName = "Riley" }.Save(ct);
        var mara = await new Member { DisplayName = "Mara" }.Save(ct);
        var devon = await new Member { DisplayName = "Devon" }.Save(ct);

        var bed1 = await new Plot { Name = "Bed 1", MemberId = riley.Id, Notes = "Morning sun—herbs and lettuces." }.Save(ct);
        var bed2 = await new Plot { Name = "Bed 2", MemberId = mara.Id, Notes = "Shade tolerant greens; rotate weekly." }.Save(ct);
        var bed3 = await new Plot { Name = "Bed 3", MemberId = devon.Id, Notes = "Tomatoes + peppers; dries out fastest." }.Save(ct);

        var sensorBed1Serial = Guid.NewGuid().ToString();
        var sensorBed1 = await new Sensor
        {
            Serial = sensorBed1Serial,
            DisplayName = $"Sensor {sensorBed1Serial[..8]}",
            PlotId = bed1.Id,
            Capabilities = SensorCapabilities.SoilHumidity | SensorCapabilities.Temperature
        }.Save(ct);

        var sensorBed2Serial = Guid.NewGuid().ToString();
        var sensorBed2 = await new Sensor
        {
            Serial = sensorBed2Serial,
            DisplayName = $"Sensor {sensorBed2Serial[..8]}",
            PlotId = bed2.Id,
            Capabilities = SensorCapabilities.SoilHumidity | SensorCapabilities.Temperature
        }.Save(ct);

        var sensorBed3Serial = Guid.NewGuid().ToString();
        var sensorBed3 = await new Sensor
        {
            Serial = sensorBed3Serial,
            DisplayName = $"Sensor {sensorBed3Serial[..8]}",
            PlotId = bed3.Id,
            Capabilities = SensorCapabilities.SoilHumidity | SensorCapabilities.Temperature
        }.Save(ct);

        // Give the journal something to read day one.
        await new Reading
        {
            SensorId = sensorBed1.Id,
            PlotId = bed1.Id,
            SoilHumidity = 28.5,
            TemperatureC = 23.8,
            SampledAt = DateTimeOffset.UtcNow.AddHours(-4)
        }.Save(ct);

        await new Reading
        {
            SensorId = sensorBed3.Id,
            PlotId = bed3.Id,
            SoilHumidity = 18.0,
            TemperatureC = 26.1,
            SampledAt = DateTimeOffset.UtcNow.AddHours(-2)
        }.Save(ct);

        await new Reading
        {
            SensorId = sensorBed3.Id,
            PlotId = bed3.Id,
            SoilHumidity = 17.2,
            TemperatureC = 25.7,
            SampledAt = DateTimeOffset.UtcNow.AddHours(-1)
        }.Save(ct);

        sensorBed1.LastSeenAt = DateTimeOffset.UtcNow.AddHours(-4);
        sensorBed2.LastSeenAt = DateTimeOffset.UtcNow.AddHours(-3);
        sensorBed3.LastSeenAt = DateTimeOffset.UtcNow.AddHours(-1);

        await sensorBed1.Save(ct);
        await sensorBed2.Save(ct);
        await sensorBed3.Save(ct);

        logger.LogInformation("Starter sensors and readings inserted – Bed 3 will nudge the team right away.");
    }
}

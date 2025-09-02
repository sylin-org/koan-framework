using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Sora.Core;
using Sora.Messaging;
using Sora.Messaging.RabbitMq;
using S8.Flow.Shared;
using Sora.Core.Hosting.App;
using Sora.Flow.Actions;
using Sora.Flow.Sending;
using Sora.Flow.Attributes;
using Sora.Flow;
using Sora.Data.Core; // AddSora()
using System;
using System.Linq;

var builder = Host.CreateApplicationBuilder(args);

if (!Sora.Core.SoraEnv.InContainer)
{
    Console.Error.WriteLine("S8.Flow.Adapters.Oem is container-only. Use samples/S8.Compose/docker-compose.yml.");
    return;
}

builder.Configuration
    .AddJsonFile("appsettings.json", optional: true)
    .AddEnvironmentVariables();

// Minimal boot: Sora + auto-registrars provide Messaging Core, RabbitMQ, Flow identity stamper,
// and auto-start this adapter (BackgroundService with [FlowAdapter]) in container environments.
builder.Services.AddSora();

// React to command VOs published by orchestrators or tools
builder.Services.OnMessages(h =>
{
    h.On<ControlCommand>(async (env, cmd, ct) =>
    {
        try
        {
            var verb = (cmd.Verb ?? string.Empty).Trim().ToLowerInvariant();
            switch (verb)
            {
                case "announce":
                {
                    // From SensorKey (INV::SER::CODE) infer device seeds and publish via FlowEvent
                    if (TryParseSensorKey(cmd.SensorKey, out var inv, out var ser, out var _))
                    {
                        var d = SampleProfiles.Fleet.FirstOrDefault(x => x.Inventory == inv && x.Serial == ser);
                        if (d is not null)
                        {
                            var oemDeviceId = $"{d.Inventory}:{d.Serial}";
                            var devEvent = FlowEvent.ForModel("device")
                                .With(Keys.Device.Inventory, d.Inventory)
                                .With(Keys.Device.Serial, d.Serial)
                                .With(Keys.Device.Manufacturer, d.Manufacturer)
                                .With(Keys.Device.Model, d.Model)
                                .With(Keys.Device.Kind, d.Kind)
                                .With(Keys.Device.Code, d.Code)
                                .With("identifier.external.oem", oemDeviceId)
                                .With("source", "oem");
                            devEvent.SourceId = "oem";
                            devEvent.CorrelationId = oemDeviceId;
                            await devEvent.Send(ct);
                            foreach (var s in SampleProfiles.SensorsForOem(d))
                            {
                                var sensorEvent = FlowEvent.ForModel("sensor")
                                    .With(Keys.Sensor.Key, s.SensorKey)
                                    .With("DeviceId", s.DeviceId)
                                    .With(Keys.Sensor.Code, s.Code)
                                    .With(Keys.Sensor.Unit, s.Unit)
                                    .With("source", "oem");
                                sensorEvent.SourceId = "oem";
                                sensorEvent.CorrelationId = s.SensorKey;
                                await sensorEvent.Send(ct);
                            }
                        }
                    }
                    break;
                }
                case "ping":
                default:
                {
                    // Ack back to the system; keep it simple for the sample
                    await new Sora.Flow.Actions.FlowAck(
                        Model: "controlcommand",
                        Verb: verb,
                        ReferenceId: cmd.SensorKey,
                        Status: verb == "ping" ? "ok" : "unsupported",
                        Message: verb == "ping" ? null : $"Unknown verb '{cmd.Verb}'",
                        CorrelationId: env.CorrelationId
                    ).Send(ct);
                    break;
                }
            }
        }
        catch (Exception ex)
        {
            // Best-effort error ack
            await new Sora.Flow.Actions.FlowAck("controlcommand", cmd.Verb ?? string.Empty, cmd.SensorKey, "error", ex.Message, env.CorrelationId).Send(ct);
        }
    });
});

var app = builder.Build();
await app.RunAsync();

[FlowAdapter(system: "oem", adapter: "oem", DefaultSource = "oem")]
public sealed class OemPublisher : BackgroundService
{
    private readonly ILogger<OemPublisher> _log;
    public OemPublisher(ILogger<OemPublisher> log) { _log = log; }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
    // Initial bulk seed on startup via MQ (resilient to broker warm-up)
    await SeedAllWithRetryAsync(stoppingToken);

        var rng = new Random();
        var lastAnnounce = DateTimeOffset.MinValue;
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var idx = rng.Next(0, SampleProfiles.Fleet.Length);
                var d = SampleProfiles.Fleet[idx];
                var code = rng.Next(0, 2) == 0 ? SensorCodes.PWR : SensorCodes.COOLANT_PRESSURE;
                var unit = code == SensorCodes.PWR ? Units.Watt : Units.KPa;
                var value = code == SensorCodes.PWR ? Math.Round(100 + rng.NextDouble() * 900, 2) : Math.Round(200 + rng.NextDouble() * 50, 2);
                // Periodic independent announcements
                if (DateTimeOffset.UtcNow - lastAnnounce > TimeSpan.FromMinutes(5))
                {
                    // Seed Device via FlowEvent (pure MQ)
                    var oemDeviceId = $"{d.Inventory}:{d.Serial}";
                    var deviceBag = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
                    {
                        [Keys.Device.Inventory] = d.Inventory,
                        [Keys.Device.Serial] = d.Serial,
                        [Keys.Device.Manufacturer] = d.Manufacturer,
                        [Keys.Device.Model] = d.Model,
                        [Keys.Device.Kind] = d.Kind,
                        [Keys.Device.Code] = d.Code,
                        ["identifier.external.oem"] = oemDeviceId,
                        ["source"] = "oem",
                    };
                    var devEvent = FlowEvent.ForModel("device");
                    devEvent.SourceId = "oem";
                    devEvent.CorrelationId = oemDeviceId;
                    foreach (var kv in deviceBag) devEvent.With(kv.Key, kv.Value);
                    await devEvent.Send(stoppingToken);

                    // Seed sensors via FlowEvent (pure MQ)
                    foreach (var s in SampleProfiles.SensorsForOem(d))
                    {
                        var sensorEvent = FlowEvent.ForModel("sensor")
                            .With(Keys.Sensor.Key, s.SensorKey)
                            .With("DeviceId", s.DeviceId)
                            .With(Keys.Sensor.Code, s.Code)
                            .With(Keys.Sensor.Unit, s.Unit)
                            .With("source", "oem");
                        sensorEvent.SourceId = "oem";
                        sensorEvent.CorrelationId = s.SensorKey;
                        await sensorEvent.Send(stoppingToken);
                    }
                    lastAnnounce = DateTimeOffset.UtcNow;
                    _log.LogInformation("OEM announced Device {Inv}/{Serial} and OEM sensors", d.Inventory, d.Serial);
                }

                // Slim reading VO
                var key = $"{d.Inventory}::{d.Serial}::{code}";
                // Publish reading via FlowEvent
                var at = DateTimeOffset.UtcNow;
                var readingEvent = FlowEvent.ForModel("reading")
                    .With(Keys.Sensor.Key, key)
                    .With(Keys.Reading.Value, value)
                    .With(Keys.Reading.CapturedAt, at.ToString("O"))
                    .With(Keys.Sensor.Unit, unit)
                    .With(Keys.Reading.Source, "oem");
                readingEvent.SourceId = "oem";
                readingEvent.CorrelationId = key;
                await readingEvent.Send(stoppingToken);
                _log.LogInformation("OEM sent Reading {Key}={Value}{Unit} at {At}", key, value, unit, at);
            }
            catch (Exception ex)
            {
                _log.LogWarning(ex, "OEM publish failed");
            }
            try { await Task.Delay(TimeSpan.FromSeconds(2.5), stoppingToken); } catch (TaskCanceledException) { }
        }
    }

    private static bool TryParseSensorKey(string sensorKey, out string inventory, out string serial, out string code)
    {
        inventory = serial = code = string.Empty;
        if (string.IsNullOrWhiteSpace(sensorKey)) return false;
        var parts = sensorKey.Split("::", StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length != 3) return false;
        (inventory, serial, code) = (parts[0], parts[1], parts[2]);
        return true;
    }

    // Iterate device catalog and publish normalized seeds via MQ
    private static async Task SeedAllAsync(CancellationToken ct)
    {
        foreach (var d in SampleProfiles.Fleet)
        {
            var oemDeviceId = $"{d.Inventory}:{d.Serial}";
            var deviceEvent = FlowEvent.ForModel("device")
                .With(Keys.Device.Inventory, d.Inventory)
                .With(Keys.Device.Serial, d.Serial)
                .With(Keys.Device.Manufacturer, d.Manufacturer)
                .With(Keys.Device.Model, d.Model)
                .With(Keys.Device.Kind, d.Kind)
                .With(Keys.Device.Code, d.Code)
                .With("identifier.external.oem", oemDeviceId)
                .With("source", "oem");
            deviceEvent.SourceId = "oem";
            deviceEvent.CorrelationId = oemDeviceId;
            await deviceEvent.Send(ct);

            foreach (var s in SampleProfiles.SensorsForOem(d))
            {
                var sensorEvent = FlowEvent.ForModel("sensor")
                    .With(Keys.Sensor.Key, s.SensorKey)
                    .With("DeviceId", s.DeviceId)
                    .With(Keys.Sensor.Code, s.Code)
                    .With(Keys.Sensor.Unit, s.Unit)
                    .With("source", "oem");
                sensorEvent.SourceId = "oem";
                sensorEvent.CorrelationId = s.SensorKey;
                await sensorEvent.Send(ct);
            }
        }
    }

    // Retry wrapper to avoid host crash if RabbitMQ isn’t ready yet
    private async Task SeedAllWithRetryAsync(CancellationToken ct)
    {
        const int maxAttempts = 30;
        for (int attempt = 1; attempt <= maxAttempts && !ct.IsCancellationRequested; attempt++)
        {
            try
            {
                await SeedAllAsync(ct);
                _log.LogInformation("Initial seed completed on attempt {Attempt}", attempt);
                return;
            }
            catch (Exception ex)
            {
                _log.LogWarning(ex, "Initial seed attempt {Attempt} failed; retrying in 1s", attempt);
                try { await Task.Delay(TimeSpan.FromSeconds(1), ct); } catch (TaskCanceledException) { break; }
            }
        }
    }
}

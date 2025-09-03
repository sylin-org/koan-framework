using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Sora.Core;
using Sora.Messaging;
using Sora.Messaging.RabbitMq;
using S8.Flow.Shared;
// removed events; use plain-bag seeds and direct VO Send()
using Sora.Flow.Actions;
using Sora.Core.Hosting.App;
using Sora.Flow.Attributes;
using Sora.Flow.Sending;
using Sora.Data.Core;
using Sora.Flow;
using System;
using System.Collections.Generic;
using System.Linq;

var builder = Host.CreateApplicationBuilder(args);

if (!Sora.Core.SoraEnv.InContainer)
{
    Console.Error.WriteLine("S8.Flow.Adapters.Bms is container-only. Use samples/S8.Compose/docker-compose.yml.");
    return;
}

// Minimal boot: Sora + auto-registrars wire Messaging Core, RabbitMQ, Flow identity stamper,
// and auto-start this adapter (BackgroundService with [FlowAdapter]) in container environments.
builder.Services.AddSora();

// No local ControlCommand responders; announce/ping handled by API-side responders (opt-out via config).

var app = builder.Build();
await app.RunAsync();

// Adapter identity + capabilities constants (single source of truth)
internal static class BmsAdapterConstants
{
    public const string System = "bms";
    public const string Adapter = "bms";
    public const string Source = "bms"; // DefaultSource & SourceId
    public const string SourceKey = "source"; // bag key
    public static class Cap
    {
        public const string Seed = "seed";
        public const string Reading = "reading";
    }
    public static class ExternalIds
    {
        public const string Device = "identifier.external.bms"; // device correlation id key
    }
}

[FlowAdapter(system: BmsAdapterConstants.System, adapter: BmsAdapterConstants.Adapter, DefaultSource = BmsAdapterConstants.Source, Capabilities = new[] { BmsAdapterConstants.Cap.Seed, BmsAdapterConstants.Cap.Reading })]
public sealed class BmsPublisher : BackgroundService
{
    private readonly ILogger<BmsPublisher> _log;
    public BmsPublisher(ILogger<BmsPublisher> log)
    { _log = log; }

    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        // Initial bulk seed on startup via MQ (resilient to broker warm-up)
        await SeedAllWithRetryAsync(ct);

        var rng = new Random();
        var lastAnnounce = DateTimeOffset.MinValue;
        while (!ct.IsCancellationRequested)
        {
            try
            {
                var idx = rng.Next(0, SampleProfiles.Fleet.Length);
                var d = SampleProfiles.Fleet[idx];
                // Periodic independent announcements (devices + sensors)
                if (DateTimeOffset.UtcNow - lastAnnounce > TimeSpan.FromMinutes(5))
                {
                    // Seed device via FlowEvent (plain bag)
                    var bmsDeviceId = $"{d.Inventory}:{d.Serial}";
                    var deviceBag = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
                    {
                        [Keys.Device.Inventory] = d.Inventory,
                        [Keys.Device.Serial] = d.Serial,
                        [Keys.Device.Manufacturer] = d.Manufacturer,
                        [Keys.Device.Model] = d.Model,
                        [Keys.Device.Kind] = d.Kind,
                        [Keys.Device.Code] = d.Code,
                        [BmsAdapterConstants.ExternalIds.Device] = bmsDeviceId,
                        [BmsAdapterConstants.SourceKey] = BmsAdapterConstants.Source,
                    };
                    var devEvent = FlowEvent.ForModel("device");
                    devEvent.SourceId = BmsAdapterConstants.Source;
                    devEvent.CorrelationId = bmsDeviceId;
                    foreach (var kv in deviceBag) devEvent.With(kv.Key, kv.Value);
                    await devEvent.Send(ct);

                    // Seed temperature sensor via FlowEvent
                    foreach (var s in SampleProfiles.SensorsForBms(d))
                    {
                        var sensorEvent = FlowEvent.ForModel("sensor")
                            .With(Keys.Sensor.Key, s.SensorKey)
                            .With("DeviceId", s.DeviceId)
                            .With(Keys.Sensor.Code, s.Code)
                            .With(Keys.Sensor.Unit, s.Unit)
                            .With(BmsAdapterConstants.SourceKey, BmsAdapterConstants.Source);
                        sensorEvent.SourceId = BmsAdapterConstants.Source;
                        sensorEvent.CorrelationId = s.SensorKey;
                        await sensorEvent.Send(ct);
                    }
                    lastAnnounce = DateTimeOffset.UtcNow;
                    _log.LogInformation("BMS announced Device {Inv}/{Serial} and Sensor TEMP", d.Inventory, d.Serial);
                }

                // Publish reading via FlowEvent
                var key = $"{d.Inventory}::{d.Serial}::{SensorCodes.TEMP}";
                var at = DateTimeOffset.UtcNow;
                var val = Math.Round(20 + rng.NextDouble() * 10, 2);
                var readingEvent = FlowEvent.ForModel("reading")
                    .With(Keys.Sensor.Key, key)
                    .With(Keys.Reading.Value, val)
                    .With(Keys.Reading.CapturedAt, at.ToString("O"))
                    .With(Keys.Sensor.Unit, Units.C)
                    .With(Keys.Reading.Source, BmsAdapterConstants.Source);
                readingEvent.SourceId = BmsAdapterConstants.Source;
                readingEvent.CorrelationId = key;
                await readingEvent.Send(ct);
                _log.LogInformation("BMS sent Reading {Key}={Value}{Unit} at {At}", key, val, Units.C, at);
            }
            catch (Exception ex)
            {
                _log.LogWarning(ex, "BMS publish failed");
            }
            try { await Task.Delay(TimeSpan.FromSeconds(1.5), ct); } catch (TaskCanceledException) { }
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
            var bmsDeviceId = $"{d.Inventory}:{d.Serial}";
            var deviceEvent = FlowEvent.ForModel("device")
                .With(Keys.Device.Inventory, d.Inventory)
                .With(Keys.Device.Serial, d.Serial)
                .With(Keys.Device.Manufacturer, d.Manufacturer)
                .With(Keys.Device.Model, d.Model)
                .With(Keys.Device.Kind, d.Kind)
                .With(Keys.Device.Code, d.Code)
                .With(BmsAdapterConstants.ExternalIds.Device, bmsDeviceId)
                .With(BmsAdapterConstants.SourceKey, BmsAdapterConstants.Source);
            deviceEvent.SourceId = BmsAdapterConstants.Source;
            deviceEvent.CorrelationId = bmsDeviceId;
            await deviceEvent.Send(ct);

            foreach (var s in SampleProfiles.SensorsForBms(d))
            {
                var sensorEvent = FlowEvent.ForModel("sensor")
                    .With(Keys.Sensor.Key, s.SensorKey)
                    .With("DeviceId", s.DeviceId)
                    .With(Keys.Sensor.Code, s.Code)
                    .With(Keys.Sensor.Unit, s.Unit)
                    .With(BmsAdapterConstants.SourceKey, BmsAdapterConstants.Source);
                sensorEvent.SourceId = BmsAdapterConstants.Source;
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

using S8.Flow.Shared;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Sora.Core;
using Sora.Messaging;
using Sora.Messaging.RabbitMq;
using Sora.Flow.Model;
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
// ITL 2025-09-02 (github copilot)
// - Introduced OemAdapterConstants for system, adapter, source, capabilities, and external ID keys.
// - Replaced all inline literals in FlowAdapter attribute, event bag, and SourceId assignments with constants.
// - Rationale: single source of truth for adapter identity, deduplication, and maintainability.
// - See also: BmsAdapterConstants in S8.Flow.Adapters.Bms.
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

// No local ControlCommand responders; announce/ping handled by API-side responders (opt-out via config).

var app = builder.Build();
await app.RunAsync();

// Adapter identity + capabilities constants (single source of truth)
internal static class OemAdapterConstants
{
    public const string System = "oem";
    public const string Adapter = "oem";
    public const string Source = "oem"; // DefaultSource & SourceId
    public const string SourceKey = "source"; // bag key
    public static class Cap
    {
        public const string Seed = "seed";
        public const string Reading = "reading";
    }
    public static class ExternalIds
    {
        public const string Device = "identifier.external.oem"; // device correlation id key
    }
}

[FlowAdapter(system: OemAdapterConstants.System, adapter: OemAdapterConstants.Adapter, DefaultSource = OemAdapterConstants.Source, Capabilities = new[] { OemAdapterConstants.Cap.Seed, OemAdapterConstants.Cap.Reading })]
public sealed class OemPublisher : BackgroundService
{
    private readonly ILogger<OemPublisher> _log;
    public OemPublisher(ILogger<OemPublisher> log) { _log = log; }

    protected override Task ExecuteAsync(CancellationToken ct)
    {
        // No-op: all work is event/message driven
        return Task.CompletedTask;
    }

    // Handle ControlCommand messages (seed, etc)
    public async Task HandleAsync(Sora.Flow.Model.ControlCommand cmd, CancellationToken ct)
    {
        if (cmd.Verb?.Equals(OemAdapterConstants.Cap.Seed, StringComparison.OrdinalIgnoreCase) == true)
        {
            int total = 1000;
            if (cmd.Parameters != null && cmd.Parameters.TryGetValue("count", out var countElem) && countElem.TryGetInt32(out var parsed))
                total = parsed;
            await SeedProceduralAsync(total, ct);
            _log.LogInformation("OEM adapter completed procedural seed for {Total} devices", total);
        }
    }

    // Procedural seed: emits both SampleProfiles and generated fakes, batching by 100
    private async Task SeedProceduralAsync(int total, CancellationToken ct)
    {
        var batchSize = 100;
        // 1. Emit all SampleProfiles
        foreach (var d in SampleProfiles.Fleet)
        {
            await EmitDeviceAndSensors(d, ct);
        }
        // 2. Emit procedural fakes in batches
        var devices = SampleProfiles.GenerateDevices(total).ToArray();
        for (int i = 0; i < devices.Length; i += batchSize)
        {
            var batch = devices.Skip(i).Take(batchSize);
            var tasks = batch.Select(d => EmitDeviceAndSensors(d, ct));
            await Task.WhenAll(tasks);
        }
    }

    private async Task EmitDeviceAndSensors(Device d, CancellationToken ct)
    {
        var oemDeviceId = $"{d.Inventory}:{d.Serial}";
        var deviceEvent = FlowEvent.ForModel("device")
            .With(Keys.Device.Inventory, d.Inventory)
            .With(Keys.Device.Serial, d.Serial)
            .With(Keys.Device.Manufacturer, d.Manufacturer)
            .With(Keys.Device.Model, d.Model)
            .With(Keys.Device.Kind, d.Kind)
            .With(Keys.Device.Code, d.Code)
            .With(OemAdapterConstants.ExternalIds.Device, oemDeviceId)
            .With(OemAdapterConstants.SourceKey, OemAdapterConstants.Source);
        deviceEvent.SourceId = OemAdapterConstants.Source;
        deviceEvent.CorrelationId = oemDeviceId;
        await deviceEvent.Send(ct);
        foreach (var s in SampleProfiles.GenerateSensorsForOem(d))
        {
            var sensorEvent = FlowEvent.ForModel("sensor")
                .With(Keys.Sensor.Key, s.SensorKey)
                .With("DeviceId", s.DeviceId)
                .With(Keys.Sensor.Code, s.Code)
                .With(Keys.Sensor.Unit, s.Unit)
                .With(OemAdapterConstants.SourceKey, OemAdapterConstants.Source);
            sensorEvent.SourceId = OemAdapterConstants.Source;
            sensorEvent.CorrelationId = s.SensorKey;
            await sensorEvent.Send(ct);
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
                .With(OemAdapterConstants.ExternalIds.Device, oemDeviceId)
                .With(OemAdapterConstants.SourceKey, OemAdapterConstants.Source);
            deviceEvent.SourceId = OemAdapterConstants.Source;
            deviceEvent.CorrelationId = oemDeviceId;
            await deviceEvent.Send(ct);

            foreach (var s in SampleProfiles.SensorsForOem(d))
            {
                var sensorEvent = FlowEvent.ForModel("sensor")
                    .With(Keys.Sensor.Key, s.SensorKey)
                    .With("DeviceId", s.DeviceId)
                    .With(Keys.Sensor.Code, s.Code)
                    .With(Keys.Sensor.Unit, s.Unit)
                    .With(OemAdapterConstants.SourceKey, OemAdapterConstants.Source);
                sensorEvent.SourceId = OemAdapterConstants.Source;
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

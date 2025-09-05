using System;
using Microsoft.Extensions.Logging;
using Sora.Flow.Sending;

namespace S8.Flow.Shared;

// Centralized constants and helper utilities for S8 Flow sample adapters & API.
// Consolidates magic literals (sources, timing, routes) and provides shared seeding logic.
public static class FlowSampleConstants
{
    public static class Sources
    {
        public const string Bms = "bms";
        public const string Oem = "oem";
        public const string Api = "api";
        public const string Events = "events"; // generic event source fallback
    }

    public static class Timing
    {
        public static readonly TimeSpan AnnouncementInterval = TimeSpan.FromMinutes(5);
        public static readonly TimeSpan BmsLoopDelay = TimeSpan.FromSeconds(1.5);
        public static readonly TimeSpan OemLoopDelay = TimeSpan.FromSeconds(2.5);
        public const int SeedMaxAttempts = 30;
        public static readonly TimeSpan SeedRetryDelay = TimeSpan.FromSeconds(1);
    }

    public static class Routes
    {
        public const string ApiPrefix = "api";
    }
}

// Shared seeding helper to DRY initial device & sensor announcements across adapters.
public static class AdapterSeeding
{
    public static async Task SeedCatalogAsync(
        string source,
        IEnumerable<Device> devices,
        Func<Device, IEnumerable<Sensor>> sensorSelector,
        CancellationToken ct,
        TimeSpan? maxThreshold = null)
    {
        // Use the new messaging-first pattern for reliable delivery
        // This demonstrates both approaches: direct entity send and FlowEvent pattern
        
        foreach (var d in devices)
        {
            // Option 1: Direct entity send (preferred for typed models)
            var device = new Device
            {
                DeviceId = d.DeviceId,
                Inventory = d.Inventory,
                Serial = d.Serial,
                Manufacturer = d.Manufacturer,
                Model = d.Model,
                Kind = d.Kind,
                Code = d.Code
            };
            
            await Sora.Flow.Sending.FlowEntitySendExtensions.Send(device, ct);

            // Option 2: FlowEvent pattern (for dynamic/untyped scenarios)
            // Demonstrating that FlowEvent can also work with messaging
            /*
            var devEvent = FlowEvent.ForModel("device")
                .With(Keys.Device.Inventory, d.Inventory)
                .With(Keys.Device.Serial, d.Serial)
                .With(Keys.Device.Manufacturer, d.Manufacturer)
                .With(Keys.Device.Model, d.Model)
                .With(Keys.Device.Kind, d.Kind)
                .With(Keys.Device.Code, d.Code)
                .With($"identifier.external.{source}", $"{d.Inventory}:{d.Serial}")
                .With(Keys.Reading.Source, source);
            devEvent.SourceId = source;
            devEvent.CorrelationId = $"{d.Inventory}:{d.Serial}";
            
            await Sora.Flow.Sending.FlowEventSendExtensions.Send(devEvent, ct);
            */

            // Send Sensor entities through messaging system
            foreach (var s in sensorSelector(d))
            {
                var sensor = new Sensor
                {
                    SensorKey = s.SensorKey,
                    DeviceId = s.DeviceId,
                    Code = s.Code,
                    Unit = s.Unit
                };
                
                await Sora.Flow.Sending.FlowEntitySendExtensions.Send(sensor, ct);
            }
        }
    }

    public static async Task SeedCatalogWithRetryAsync(
        string source,
        IEnumerable<Device> devices,
        Func<Device, IEnumerable<Sensor>> sensorSelector,
        ILogger log,
        CancellationToken ct)
    {
        for (int attempt = 1; attempt <= FlowSampleConstants.Timing.SeedMaxAttempts && !ct.IsCancellationRequested; attempt++)
        {
            try
            {
                await SeedCatalogAsync(source, devices, sensorSelector, ct);
                log.LogInformation("Initial seed ({Source}) completed on attempt {Attempt}", source, attempt);
                return;
            }
            catch (Exception ex)
            {
                log.LogWarning(ex, "Initial seed ({Source}) attempt {Attempt} failed; retrying in {Delay}s", source, attempt, FlowSampleConstants.Timing.SeedRetryDelay.TotalSeconds);
                try { await Task.Delay(FlowSampleConstants.Timing.SeedRetryDelay, ct); } catch (TaskCanceledException) { break; }
            }
        }
    }
}
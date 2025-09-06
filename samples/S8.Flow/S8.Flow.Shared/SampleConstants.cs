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
        // Use the messaging-first pattern for reliable delivery
        foreach (var d in devices)
        {
            // Send Device entity through messaging system
            var device = new Device
            {
                Id = d.Id,
                Inventory = d.Inventory,
                Serial = d.Serial,
                Manufacturer = d.Manufacturer,
                Model = d.Model,
                Kind = d.Kind,
                Code = d.Code
            };
            await Sora.Messaging.MessagingExtensions.Send(device, cancellationToken: ct);

            // Send Sensor entities through messaging system
            foreach (var s in sensorSelector(d))
            {
                var sensor = new Sensor
                {
                    Id = s.Id,
                    SensorKey = s.SensorKey,
                    DeviceId = s.DeviceId,
                    Code = s.Code,
                    Unit = s.Unit
                };
                await Sora.Messaging.MessagingExtensions.Send(sensor, cancellationToken: ct);
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
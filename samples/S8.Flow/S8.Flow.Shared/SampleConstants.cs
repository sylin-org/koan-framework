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
        CancellationToken ct)
    {
    // Resolve sender once (optional in tests)
    var sp = Sora.Core.Hosting.App.AppHost.Current;
    var sender = sp?.GetService(typeof(Sora.Flow.Sending.IFlowSender)) as Sora.Flow.Sending.IFlowSender;
    foreach (var d in devices)
        {
            var externalId = $"{d.Inventory}:{d.Serial}";
            var devEvent = FlowEvent.ForModel("device");
            devEvent.SourceId = source;
            devEvent.CorrelationId = externalId;
            devEvent
                .With(Keys.Device.Inventory, d.Inventory)
                .With(Keys.Device.Serial, d.Serial)
                .With(Keys.Device.Manufacturer, d.Manufacturer)
                .With(Keys.Device.Model, d.Model)
                .With(Keys.Device.Kind, d.Kind)
                .With(Keys.Device.Code, d.Code)
                .With($"identifier.external.{source}", externalId)
                .With(Keys.Reading.Source, source); // uniform source tag
            if (sender is not null)
            {
                var bag = devEvent.Bag; // already constructed
                var item = Sora.Flow.Sending.FlowSendPlainItem.Of<object>(bag, devEvent.SourceId ?? source, devEvent.OccurredAt ?? DateTimeOffset.UtcNow, devEvent.CorrelationId);
                await sender.SendAsync(new[] { item }, null, null, hostType: null, ct);
            }

            foreach (var s in sensorSelector(d))
            {
                var sensorEvent = FlowEvent.ForModel("sensor")
                    .With(Keys.Sensor.Key, s.SensorKey)
                    .With("DeviceId", s.DeviceId)
                    .With(Keys.Sensor.Code, s.Code)
                    .With(Keys.Sensor.Unit, s.Unit)
                    .With(Keys.Reading.Source, source);
                sensorEvent.SourceId = source;
                sensorEvent.CorrelationId = s.SensorKey;
                if (sender is not null)
                {
                    var bag = sensorEvent.Bag;
                    var item = Sora.Flow.Sending.FlowSendPlainItem.Of<object>(bag, sensorEvent.SourceId ?? source, sensorEvent.OccurredAt ?? DateTimeOffset.UtcNow, sensorEvent.CorrelationId);
                    await sender.SendAsync(new[] { item }, null, null, hostType: null, ct);
                }
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
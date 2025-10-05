using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using System;
using System.Linq;
using S8.Canon.Shared; // SampleProfiles, AdapterSeeding, FlowSampleConstants

namespace S8.Canon.Api;

public sealed class FlowSeeder : IFlowSeeder
{
    private readonly ILogger<FlowSeeder> _log;
    private readonly IServiceProvider _sp;
    public FlowSeeder(ILogger<FlowSeeder> log, IServiceProvider sp)
    {
        _log = log;
        _sp = sp;
    }

    public async Task<object> SeedAllAdaptersAsync(int count, CancellationToken ct)
    {
        if (count <= 0) count = 1;
        var fleet = SampleProfiles.Fleet;
        var take = Math.Min(count, fleet.Length);
        var subset = fleet.Take(take).ToArray();

        var started = DateTimeOffset.UtcNow;
        _log.LogInformation("[Seeder] Seeding {DeviceCount} devices (subset of {Total}) for sources: bms, oem", subset.Length, fleet.Length);

        try
        {
            // Extend readiness threshold (startup race with broker/adapters can exceed 30s in compose)
            var readinessThreshold = TimeSpan.FromSeconds(90);

            // Seed for BMS (temperature sensors only)
            await AdapterSeeding.SeedCatalogAsync(
                FlowSampleConstants.Sources.Bms,
                subset,
                SampleProfiles.SensorsForBms,
                ct,
                readinessThreshold);

            // Seed for OEM (power + coolant pressure)
            await AdapterSeeding.SeedCatalogAsync(
                FlowSampleConstants.Sources.Oem,
                subset,
                SampleProfiles.SensorsForOem,
                ct,
                readinessThreshold);
        }
        catch (TimeoutException tex)
        {
            _log.LogWarning(tex, "[Seeder] Messaging readiness timeout; returning error payload");
            return new
            {
                status = "error",
                reason = "messaging_not_ready",
                message = tex.Message,
                requested = count,
                seededDevices = 0,
                sources = new[] { FlowSampleConstants.Sources.Bms, FlowSampleConstants.Sources.Oem }
            };
        }

        var ended = DateTimeOffset.UtcNow;
        var elapsed = ended - started;
        _log.LogInformation("[Seeder] Completed seed for {DeviceCount} devices across adapters in {Elapsed}ms", subset.Length, elapsed.TotalMilliseconds.ToString("F0"));
        return new
        {
            status = "ok",
            requested = count,
            seededDevices = subset.Length,
            sources = new[] { FlowSampleConstants.Sources.Bms, FlowSampleConstants.Sources.Oem },
            perSource = new {
                bms = new { devices = subset.Length, sensors = subset.Length * 3 },
                oem = new { devices = subset.Length, sensors = subset.Length * 3 }
            },
            elapsedMs = Math.Round(elapsed.TotalMilliseconds, 0)
        };
    }
}

using Microsoft.Extensions.Logging;
using S5.Recs.Models;
using S5.Recs.Services;
using Koan.Scheduling;

namespace S5.Recs.Tasks;

// Runs on app startup to ensure reference data is seeded for manual import operations.
internal sealed class S5BootstrapTask : IScheduledTask, IOnStartup, IHasTimeout
{
    private readonly ISeedService _seeder;
    private readonly ILogger<S5BootstrapTask>? _logger;

    public S5BootstrapTask(ISeedService seeder, ILogger<S5BootstrapTask>? logger = null)
    {
        _seeder = seeder;
        _logger = logger;
    }

    public string Id => "s5:bootstrap";

    // Bound by scheduler; also enforces a ceiling for our internal polling loop
    public TimeSpan Timeout => TimeSpan.FromMinutes(5);

    public async Task RunAsync(CancellationToken ct)
    {
        // Quick check: skip only if both docs and vectors are present; otherwise seed to ensure vectors
        var (media, _, vectors) = await _seeder.GetStatsAsync(ct);
        await CensorTagBootstrapper.EnsureCensorTagsPopulated(ct);
        if (media > 0 && vectors > 0)
        {
            _logger?.LogInformation("S5 bootstrap: dataset already present (media={Media}, vectors={Vectors}). Skipping seeding.", media, vectors);
            // Best-effort ensure catalogs exist
            _ = _seeder.RebuildTagCatalogAsync(ct);
            _ = _seeder.RebuildGenreCatalogAsync(ct);
            return;
        }
        // Only run if no media exists at all - just ensure reference data is set up
        if (media == 0)
        {
            _logger?.LogInformation("S5 bootstrap: no media found, ensuring reference data is seeded...");
            var bootstrapper = new Services.DataBootstrapper();
            await bootstrapper.SeedReferenceDataAsync(ct);

            // Wait a moment for the data to be committed
            await Task.Delay(1000, ct);

            // Verify MediaTypes were created
            var mediaTypes = await Models.MediaType.All(ct);
            if (mediaTypes.Any())
            {
                var mediaTypeNames = string.Join(", ", mediaTypes.Select(mt => $"'{mt.Name}'"));
                _logger?.LogInformation("S5 bootstrap: reference data seeded successfully. Media types available: {Names}. Import data manually from the dashboard.", mediaTypeNames);
            }
            else
            {
                _logger?.LogWarning("S5 bootstrap: Failed to seed reference data - no MediaTypes found.");
            }
            return;
        }

        if (media > 0 && vectors == 0)
        {
            _logger?.LogInformation("S5 bootstrap: documents present but no vectors (media={Media}, vectors={Vectors}). Consider running vector rebuild from the dashboard.", media, vectors);
            return;
        }
    }
}

// Self-registration via Koan.Core discovery
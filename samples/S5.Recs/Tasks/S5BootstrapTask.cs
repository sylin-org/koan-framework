using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using S5.Recs.Models;
using S5.Recs.Services;
using Koan.Data.Core.Model;
using Koan.Jobs;

namespace S5.Recs.Tasks;

// Runs once on app startup (JOBS-0005 @boot schedule) to ensure reference data is seeded for
// manual import operations. Migrated to an entity-first Koan.Jobs job:
// [JobAction(Schedule="@boot")] fires it once per boot, [JobIdempotent] coalesces concurrent
// multi-node boots, MaxAttempts=1 preserves the original one-shot semantics (the previous
// startup task never retried until the next boot).
[JobAction(Bootstrap, Schedule = "@boot", Timeout = "00:05:00", MaxAttempts = 1)]
[JobIdempotent(nameof(Marker))]
internal sealed class S5BootstrapTask : Entity<S5BootstrapTask>, IKoanJob<S5BootstrapTask>
{
    public const string Bootstrap = nameof(Bootstrap);

    // Stable idempotency key so concurrent boots coalesce onto a single in-flight job.
    public string Marker { get; set; } = "s5:bootstrap";

    public static async Task Execute(S5BootstrapTask job, JobContext ctx, CancellationToken ct)
    {
        var seeder = ctx.Services.GetRequiredService<ISeedService>();
        var logger = ctx.Services.GetService<ILogger<S5BootstrapTask>>();

        // Quick check: skip only if both docs and vectors are present; otherwise seed to ensure vectors
        var (media, _, vectors) = await seeder.GetStats(ct);
        await CensorTagBootstrapper.EnsureCensorTagsPopulated(ct);
        if (media > 0 && vectors > 0)
        {
            logger?.LogInformation("S5 bootstrap: dataset already present (media={Media}, vectors={Vectors}). Skipping seeding.", media, vectors);
            // Best-effort ensure catalogs exist
            _ = seeder.RebuildTagCatalog(ct);
            _ = seeder.RebuildGenreCatalog(ct);
            return;
        }
        // Only run if no media exists at all - just ensure reference data is set up
        if (media == 0)
        {
            logger?.LogInformation("S5 bootstrap: no media found, ensuring reference data is seeded...");
            var bootstrapper = new Services.DataBootstrapper();
            await bootstrapper.SeedReferenceData(ct);

            // Wait a moment for the data to be committed
            await Task.Delay(1000, ct);

            // Verify MediaTypes were created
            var mediaTypes = await Models.MediaType.All(ct);
            if (mediaTypes.Any())
            {
                var mediaTypeNames = string.Join(", ", mediaTypes.Select(mt => $"'{mt.Name}'"));
                logger?.LogInformation("S5 bootstrap: reference data seeded successfully. Media types available: {Names}. Import data manually from the dashboard.", mediaTypeNames);
            }
            else
            {
                logger?.LogWarning("S5 bootstrap: Failed to seed reference data - no MediaTypes found.");
            }
            return;
        }

        if (media > 0 && vectors == 0)
        {
            logger?.LogInformation("S5 bootstrap: documents present but no vectors (media={Media}, vectors={Vectors}). Consider running vector rebuild from the dashboard.", media, vectors);
            return;
        }
    }
}

using System.Diagnostics;
using Microsoft.Extensions.Logging;
using S5.Recs.Models;
using S5.Recs.Services;
using Sora.Data.Vector;
using Microsoft.Extensions.DependencyInjection;
using Sora.Scheduling;

namespace S5.Recs.Tasks;

// Runs on app startup to ensure a minimal dataset is present for a smooth demo.
internal sealed class S5BootstrapTask : IScheduledTask, IOnStartup, IHasTimeout
{
    private readonly ISeedService _seeder;
    private readonly ILogger<S5BootstrapTask>? _logger;
    // Polling and logging cadence
    private static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(1);
    private static readonly TimeSpan LogInterval = TimeSpan.FromSeconds(5);
    // If docs exist but vectors don't show up within this window, proceed without vectors to avoid noisy logs
    private static readonly TimeSpan NoVectorGrace = TimeSpan.FromSeconds(30);

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
        var (anime, _, vectors) = await _seeder.GetStatsAsync(ct);
        if (anime > 0 && vectors > 0)
        {
            _logger?.LogInformation("S5 bootstrap: dataset already present (anime={Anime}, vectors={Vectors}). Skipping seeding.", anime, vectors);
            return;
        }
        if (anime > 0 && vectors == 0)
        {
            _logger?.LogInformation("S5 bootstrap: documents present but no vectors (anime={Anime}, vectors={Vectors}). Running embedding/index seeding.", anime, vectors);
        }

        _logger?.LogInformation("S5 bootstrap: seeding from local provider…");
        var jobId = await _seeder.StartAsync(source: "local", limit: 200, overwrite: false, ct);

        // Poll until we see content or timeout/cancel.
        var sw = Stopwatch.StartNew();
        var vectorAware = Vector<AnimeDoc>.IsAvailable;
        var targetAnime = 1; // any content
        var targetVectors = vectorAware ? 1 : 0; // only wait for vectors if vector adapter is wired
        var lastLogAt = DateTimeOffset.MinValue;

        while (!ct.IsCancellationRequested && sw.Elapsed < Timeout)
        {
            await Task.Delay(PollInterval, ct);
            var (a, _, v) = await _seeder.GetStatsAsync(ct);
            if (DateTimeOffset.UtcNow - lastLogAt >= LogInterval)
            {
                lastLogAt = DateTimeOffset.UtcNow;
                _logger?.LogDebug("S5 bootstrap: waiting for data… anime={Anime} vectors={Vectors} (job={JobId})", a, v, jobId);
            }
            if (a >= targetAnime && v >= targetVectors)
            {
                _logger?.LogInformation("S5 bootstrap: dataset ready (anime={Anime}, vectors={Vectors}).", a, v);
                return;
            }

            // If docs are present but vectors remain missing beyond grace, proceed without blocking further
            if (a >= targetAnime && v < targetVectors && sw.Elapsed >= NoVectorGrace)
            {
                _logger?.LogWarning("S5 bootstrap: docs ready but vectors still 0 after {Seconds:n0}s. Proceeding without vector features. You can seed vectors later from Admin → Seed.", sw.Elapsed.TotalSeconds);
                return;
            }
        }

        _logger?.LogWarning("S5 bootstrap: timed out waiting for dataset (waited {Seconds:n0}s). The background seeding job may still be running.", sw.Elapsed.TotalSeconds);
    }
}

// Self-registration via Sora.Core discovery
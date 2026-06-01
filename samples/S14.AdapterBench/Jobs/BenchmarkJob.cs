using Koan.Core.Hosting.App;
using Koan.Jobs.Model;
using Koan.Jobs.Progress;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.DependencyInjection;
using S14.AdapterBench.Hubs;
using S14.AdapterBench.Models;
using S14.AdapterBench.Services;

namespace S14.AdapterBench.Jobs;

/// <summary>
/// Background job for running benchmarks asynchronously (JOBS-0003 CRTP model): the request payload
/// lives on <see cref="Context"/>, the outcome on <see cref="Result"/>, and the work runs in
/// <see cref="Do"/>. Runs in-memory by default (ephemeral).
/// </summary>
public class BenchmarkJob : Job<BenchmarkJob>
{
    /// <summary>The benchmark request payload (set at submit time).</summary>
    public BenchmarkRequest Context { get; set; } = new();

    /// <summary>The benchmark outcome (written when the job completes).</summary>
    public BenchmarkResult? Result { get; set; }

    protected override async Task Do(IJobProgress progress, CancellationToken cancellationToken)
    {
        // The Jobs runtime no longer injects a ServiceProvider; resolve services from the ambient host.
        var services = AppHost.Current;
        var hubContext = services?.GetService<IHubContext<BenchmarkHub>>();
        var logger = services?.GetService<ILogger<BenchmarkService>>();

        var benchmarkService = new BenchmarkService(logger);

        // Forward benchmark progress to both the Job framework and SignalR for UI visualization.
        var benchmarkProgress = new Progress<BenchmarkProgress>(p =>
        {
            var progressValue = p.TotalTests > 0 ? (double)p.CompletedTests / p.TotalTests : 0.0;
            var message = $"{p.CurrentProvider} - {p.CurrentTest}: {p.CurrentOperationCount}/{p.TotalOperations} ({p.CurrentOperationsPerSecond:F0} ops/sec)";
            progress.Report(progressValue, message);

            if (hubContext != null && p.ProviderProgress is { Count: > 0 })
            {
                try
                {
                    hubContext.Clients.Group("BenchmarkProgress")
                        .SendAsync("ProviderProgressUpdate", p)
                        .GetAwaiter().GetResult();
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[BenchmarkJob] SignalR send failed: {ex.Message}");
                }
            }
        });

        Result = await benchmarkService.RunBenchmark(Context, benchmarkProgress, cancellationToken);
    }
}

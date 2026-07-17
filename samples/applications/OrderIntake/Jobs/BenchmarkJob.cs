using Koan.Data.Core.Model;
using Koan.Jobs;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using OrderIntake.Hubs;
using OrderIntake.Models;
using OrderIntake.Services;

namespace OrderIntake.Jobs;

/// <summary>
/// Background benchmark run (JOBS-0005 entity-first model): the request rides on <see cref="Context"/>, the outcome
/// on <see cref="Result"/>, and the work runs in the static <see cref="Execute"/> handler. Ephemeral (in-memory tier).
/// </summary>
public sealed class BenchmarkJob : Entity<BenchmarkJob>, IKoanJob<BenchmarkJob>
{
    /// <summary>The benchmark request payload (set at submit time).</summary>
    public BenchmarkRequest Context { get; set; } = new();

    /// <summary>The benchmark outcome (written when the job completes; persisted as work-item state).</summary>
    public BenchmarkResult? Result { get; set; }

    public static async Task Execute(BenchmarkJob job, JobContext ctx, CancellationToken ct)
    {
        var hubContext = ctx.Services.GetService<IHubContext<BenchmarkHub>>();
        var logger = ctx.Services.GetService<ILogger<BenchmarkService>>();
        var benchmarkService = new BenchmarkService(logger);

        // Forward benchmark progress to both the durable ledger (ctx.Progress) and SignalR for UI visualization.
        var benchmarkProgress = new Progress<BenchmarkProgress>(p =>
        {
            var value = p.TotalTests > 0 ? (double)p.CompletedTests / p.TotalTests : 0.0;
            var message = $"{p.CurrentProvider} - {p.CurrentTest}: {p.CurrentOperationCount}/{p.TotalOperations} ({p.CurrentOperationsPerSecond:F0} ops/sec)";
            _ = ctx.Progress(value, message);

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

        job.Result = await benchmarkService.RunBenchmark(job.Context, benchmarkProgress, ct);
    }
}

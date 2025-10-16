using Koan.Jobs.Model;
using Koan.Jobs.Progress;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.DependencyInjection;
using S14.AdapterBench.Hubs;
using S14.AdapterBench.Models;
using S14.AdapterBench.Services;

namespace S14.AdapterBench.Jobs;

/// <summary>
/// Background job for running benchmarks asynchronously.
/// Allows clients to submit benchmark requests and poll for results.
/// Runs in-memory by default (no .Persist() call = ephemeral).
/// </summary>
public class BenchmarkJob : Job<BenchmarkJob, BenchmarkRequest, BenchmarkResult>
{
    // Injected by Jobs framework
    public IServiceProvider? ServiceProvider { get; set; }

    protected override async Task<BenchmarkResult> Execute(
        BenchmarkRequest context,
        IJobProgress progress,
        CancellationToken cancellationToken)
    {
        // Get SignalR hub context from ServiceProvider
        var hubContext = ServiceProvider?.GetService<IHubContext<BenchmarkHub>>();
        var logger = ServiceProvider?.GetService<ILogger<BenchmarkService>>();

        // Create BenchmarkService
        var benchmarkService = new BenchmarkService(logger);

        // Create progress adapter that forwards to both Job progress AND SignalR
        var benchmarkProgress = new Progress<BenchmarkProgress>(p =>
        {
            // Map benchmark progress to job progress
            double progressValue = 0.0;
            if (p.TotalTests > 0)
            {
                progressValue = (double)p.CompletedTests / p.TotalTests;
            }

            var message = $"{p.CurrentProvider} - {p.CurrentTest}: {p.CurrentOperationCount}/{p.TotalOperations} ({p.CurrentOperationsPerSecond:F0} ops/sec)";

            // Report to Job framework
            progress.Report(progressValue, message);

            // Forward detailed progress to SignalR for UI visualization
            Console.WriteLine($"[BenchmarkJob] Progress callback - hubContext={(hubContext != null)}, ProviderProgress={(p.ProviderProgress?.Count ?? 0)} providers");

            if (hubContext != null && p.ProviderProgress != null && p.ProviderProgress.Count > 0)
            {
                try
                {
                    Console.WriteLine($"[BenchmarkJob] Sending ProviderProgressUpdate to SignalR with {p.ProviderProgress.Count} providers");
                    hubContext.Clients.Group("BenchmarkProgress")
                        .SendAsync("ProviderProgressUpdate", p)
                        .GetAwaiter().GetResult();
                    Console.WriteLine("[BenchmarkJob] SignalR message sent successfully");
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[BenchmarkJob] SignalR send failed: {ex.Message}");
                }
            }
            else
            {
                if (hubContext == null)
                    Console.WriteLine("[BenchmarkJob] hubContext is NULL - cannot send SignalR");
                if (p.ProviderProgress == null || p.ProviderProgress.Count == 0)
                    Console.WriteLine($"[BenchmarkJob] ProviderProgress is empty or null - count={p.ProviderProgress?.Count ?? 0}");
            }
        });

        // Run the benchmark with progress reporting
        var result = await benchmarkService.RunBenchmarkAsync(context, benchmarkProgress, cancellationToken);

        return result;
    }
}

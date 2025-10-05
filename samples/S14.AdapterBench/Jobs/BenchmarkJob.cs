using Koan.Jobs.Model;
using Koan.Jobs.Progress;
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
    protected override async Task<BenchmarkResult> Execute(
        BenchmarkRequest context,
        IJobProgress progress,
        CancellationToken cancellationToken)
    {
        // Get the benchmark service from DI (Jobs automatically have access to IServiceProvider)
        var benchmarkService = GetService<IBenchmarkService>();

        // Create progress adapter to bridge IProgress<BenchmarkProgress> to IJobProgress
        var benchmarkProgress = new Progress<BenchmarkProgress>(p =>
        {
            // Map benchmark progress to job progress
            double progressValue = 0.0;
            if (p.TotalTests > 0)
            {
                progressValue = (double)p.CompletedTests / p.TotalTests;
            }

            var message = $"{p.CurrentProvider} - {p.CurrentTest}: {p.CurrentOperationCount}/{p.TotalOperations} ({p.CurrentOperationsPerSecond:F0} ops/sec)";

            // Use the correct IJobProgress.Report signature
            progress.Report(progressValue, message);
        });

        // Run the benchmark with progress reporting
        var result = await benchmarkService.RunBenchmarkAsync(context, benchmarkProgress, cancellationToken);

        return result;
    }

    private T GetService<T>() where T : notnull
    {
        // Jobs framework automatically injects dependencies via constructor or property injection
        // For now, we'll use a simple workaround - create the service manually
        // TODO: Use proper DI integration when Jobs framework exposes service provider
        var loggerFactory = Microsoft.Extensions.Logging.Abstractions.NullLoggerFactory.Instance;
        var logger = loggerFactory.CreateLogger<BenchmarkService>();
        return (T)(object)new BenchmarkService(logger);
    }
}

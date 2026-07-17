using OrderIntake.Models;

namespace OrderIntake.Services;

public interface IBenchmarkService
{
    Task<BenchmarkResult> RunBenchmark(
        BenchmarkRequest request,
        IProgress<BenchmarkProgress>? progress = null,
        CancellationToken cancellationToken = default);
}

using S14.AdapterBench.Models;

namespace S14.AdapterBench.Services;

public interface IBenchmarkService
{
    Task<BenchmarkResult> RunBenchmarkAsync(
        BenchmarkRequest request,
        IProgress<BenchmarkProgress>? progress = null,
        CancellationToken cancellationToken = default);
}

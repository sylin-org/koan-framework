using Koan.Data.Core;
using Koan.Samples.Meridian.Models;

namespace Koan.Samples.Meridian.Services;

public interface IPipelineQualityDashboard
{
    Task<PipelineQualitySnapshot?> GetLatestAsync(string pipelineId, CancellationToken ct = default);
    Task<IReadOnlyList<PipelineQualitySnapshot>> GetHistoryAsync(string pipelineId, int take, CancellationToken ct = default);
    Task<double> GetAverageCitationCoverageAsync(string pipelineId, CancellationToken ct = default);
}

public sealed class PipelineQualityDashboard : IPipelineQualityDashboard
{
    public async Task<PipelineQualitySnapshot?> GetLatestAsync(string pipelineId, CancellationToken ct = default)
    {
        var snapshots = await PipelineQualitySnapshot.Query(s => s.PipelineId == pipelineId, ct);
        return snapshots
            .OrderByDescending(s => s.CreatedAt)
            .FirstOrDefault();
    }

    public async Task<IReadOnlyList<PipelineQualitySnapshot>> GetHistoryAsync(string pipelineId, int take, CancellationToken ct = default)
    {
        take = Math.Clamp(take, 1, 100);
        var snapshots = await PipelineQualitySnapshot.Query(s => s.PipelineId == pipelineId, ct);
        return snapshots
            .OrderByDescending(s => s.CreatedAt)
            .Take(take)
            .ToList();
    }

    public async Task<double> GetAverageCitationCoverageAsync(string pipelineId, CancellationToken ct = default)
    {
        var snapshots = await PipelineQualitySnapshot.Query(s => s.PipelineId == pipelineId, ct);
        if (snapshots.Count == 0)
        {
            return 0.0;
        }

        return snapshots.Average(s => s.CitationCoverage);
    }
}

using System.Collections.Generic;
using Koan.Jobs.Model;

namespace Koan.Jobs.Web;

/// <summary>
/// Maps Job entities to API DTOs.
/// Stateless — all methods are pure functions.
/// </summary>
public static class JobMapper
{
    /// <summary>
    /// Projects a <see cref="Job"/> into a <see cref="JobSummaryDto"/>.
    /// </summary>
    public static JobSummaryDto ToSummary(Job job, int executionCount = 0) =>
        new(
            Id: job.Id,
            Name: job.Name,
            Status: job.Status.ToString(),
            Progress: job.Progress,
            ProgressMessage: job.ProgressMessage,
            CreatedAt: job.CreatedAt,
            CompletedAt: job.CompletedAt,
            LastError: job.LastError,
            ExecutionCount: executionCount);

    /// <summary>
    /// Projects a collection of jobs into summaries (without execution counts).
    /// </summary>
    public static IReadOnlyList<JobSummaryDto> ToSummaries(IEnumerable<Job> jobs)
    {
        var result = new List<JobSummaryDto>();
        foreach (var job in jobs)
        {
            result.Add(ToSummary(job));
        }
        return result;
    }
}

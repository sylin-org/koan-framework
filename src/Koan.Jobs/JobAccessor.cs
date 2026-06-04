using Koan.Core.Hosting.App;
using Koan.Data.Core.Model;
using Microsoft.Extensions.DependencyInjection;

namespace Koan.Jobs;

/// <summary>Resolves the ambient <see cref="IJobCoordinator"/> for the terse <c>.Job</c>/<c>.Jobs</c> accessors
/// (which run on POCO work-items with no DI of their own).</summary>
internal static class JobAmbient
{
    public static IJobCoordinator Coordinator =>
        (AppHost.Current ?? throw new InvalidOperationException(
            "The Koan host is not initialized; build the host (AddKoan) before submitting jobs."))
        .GetRequiredService<IJobCoordinator>();
}

/// <summary>Instance job operations for one work-item: <c>model.Job.Submit(action)</c> / <c>.Cancel()</c> / <c>.Status()</c>.</summary>
public readonly struct JobOps<T> where T : Entity<T>, IKoanJob<T>
{
    private readonly T _model;
    internal JobOps(T model) => _model = model;

    /// <summary>Enqueue an action on this work-item (empty action = a single-action job).</summary>
    public Task<JobHandle> Submit(string action = "", CancellationToken ct = default)
        => JobAmbient.Coordinator.SubmitAsync(_model, action, null, ct);

    /// <summary>Enqueue an action to become visible after a delay.</summary>
    public Task<JobHandle> Submit(string action, TimeSpan after, CancellationToken ct = default)
        => JobAmbient.Coordinator.SubmitAsync(_model, action, after, ct);

    /// <summary>Durably cancel this work-item's active job(s).</summary>
    public Task Cancel(CancellationToken ct = default)
        => JobAmbient.Coordinator.CancelWorkAsync(typeof(T).FullName!, _model.Id, ct);

    /// <summary>Latest job status for this work-item.</summary>
    public Task<JobStatus?> Status(CancellationToken ct = default)
        => JobAmbient.Coordinator.StatusAsync(typeof(T).FullName!, _model.Id, ct);
}

/// <summary>Type-level job subsystem for a work-type: <c>MyModel.Jobs.Submit(list)</c> / <c>.Cancel(id)</c> /
/// <c>.Status(id)</c> / <c>.Query(...)</c>.</summary>
public readonly struct JobStatics<T> where T : Entity<T>, IKoanJob<T>
{
    /// <summary>Bulk-enqueue one action across many work-items.</summary>
    public Task<int> Submit(IEnumerable<T> models, string action = "", CancellationToken ct = default)
        => JobAmbient.Coordinator.SubmitManyAsync(models.Cast<object>(), action, null, ct);

    /// <summary>Durably cancel a work-item's active job(s) by id.</summary>
    public Task Cancel(string workId, CancellationToken ct = default)
        => JobAmbient.Coordinator.CancelWorkAsync(typeof(T).FullName!, workId, ct);

    /// <summary>Latest job status for a work-item by id.</summary>
    public Task<JobStatus?> Status(string workId, CancellationToken ct = default)
        => JobAmbient.Coordinator.StatusAsync(typeof(T).FullName!, workId, ct);

    /// <summary>Query this type's jobs (facade / dashboard).</summary>
    public Task<IReadOnlyList<JobRecord>> Query(JobQuery query, CancellationToken ct = default)
        => JobAmbient.Coordinator.WhereAsync(query with { WorkType = typeof(T).FullName! }, ct);

    /// <summary>Convenience: this type's jobs in a given status.</summary>
    public Task<IReadOnlyList<JobRecord>> WithStatus(JobStatus status, CancellationToken ct = default)
        => JobAmbient.Coordinator.WhereAsync(new JobQuery(WorkType: typeof(T).FullName!, Status: status), ct);
}

/// <summary>The <c>.Job</c> (instance) / <c>.Jobs</c> (static) accessors and the batch <c>Submit</c>, delivered via
/// C# 14 extension members — no source generator, no <c>partial</c> requirement (JOBS-0005 §12.14).</summary>
public static class JobAccessorExtensions
{
    extension<T>(T model) where T : Entity<T>, IKoanJob<T>
    {
        /// <summary>This work-item's job operations.</summary>
        public JobOps<T> Job => new(model);
    }

    extension<T>(T) where T : Entity<T>, IKoanJob<T>
    {
        /// <summary>The job subsystem for this work-type.</summary>
        public static JobStatics<T> Jobs => default;
    }

    extension<T>(IEnumerable<T> models) where T : Entity<T>, IKoanJob<T>
    {
        /// <summary>Bulk-enqueue one action across this collection of work-items.</summary>
        public Task<int> Submit(string action = "", CancellationToken ct = default)
            => JobAmbient.Coordinator.SubmitManyAsync(models.Cast<object>(), action, null, ct);
    }
}

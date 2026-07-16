using Koan.Jobs;

public sealed class CompiledJob : Entity<CompiledJob>, IKoanJob<CompiledJob>
{
    public static Task Execute(CompiledJob job, JobContext context, CancellationToken ct)
        => Task.CompletedTask;
}

public static class JobsSubmissionAccessConsumer
{
    public static Task<JobHandle> Scalar(CompiledJob job)
        => job.Job.Submit();

    public static Task<JobSubmission> Many(IEnumerable<CompiledJob> jobs)
        => jobs.Submit();

    public static Task<JobSubmission> Stream(IAsyncEnumerable<CompiledJob> jobs)
        => jobs.Submit();

    public static JobStatics<CompiledJob> ControlPlane()
        => CompiledJob.Jobs;
}

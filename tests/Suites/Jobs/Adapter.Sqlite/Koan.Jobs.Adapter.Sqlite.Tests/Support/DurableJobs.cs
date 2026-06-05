using System.Collections.Generic;
using Koan.Data.Core.Model;

namespace Koan.Jobs.Adapter.Sqlite.Tests.Support;

public static class DStage
{
    public const string A = "A";
    public const string B = "B";
    public const string C = "C";
}

/// <summary>Single-action durable job: persists its mutation through the SQLite data layer.</summary>
public sealed class DurableJob : Entity<DurableJob>, IKoanJob<DurableJob>
{
    public string Input { get; set; } = "";
    public string? Output { get; set; }
    public static int Executions;

    public static Task Execute(DurableJob job, JobContext ctx, CancellationToken ct)
    {
        Interlocked.Increment(ref Executions);
        job.Output = job.Input + "-done";
        return Task.CompletedTask;
    }

    public static void Reset() => Executions = 0;
}

/// <summary>Type-level trigger target over the durable store (singleton persisted via SQLite).</summary>
public sealed class DurableTick : Entity<DurableTick>, IKoanJob<DurableTick>
{
    public static int Executions;

    public static Task Execute(DurableTick job, JobContext ctx, CancellationToken ct)
    {
        Interlocked.Increment(ref Executions);
        return Task.CompletedTask;
    }

    public static void Reset() => Executions = 0;
}

/// <summary>Durable chain: each stage is its own JobRecord; the work-item's Trail accumulates across stages.</summary>
[JobChain(DStage.A, DStage.B, DStage.C)]
public sealed class DurablePipeline : Entity<DurablePipeline>, IKoanJob<DurablePipeline>
{
    public List<string> Trail { get; set; } = new();

    public static Task Execute(DurablePipeline job, JobContext ctx, CancellationToken ct)
    {
        job.Trail.Add(ctx.Action);
        return Task.CompletedTask;
    }
}

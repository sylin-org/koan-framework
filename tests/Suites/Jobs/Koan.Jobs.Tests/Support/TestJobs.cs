using Koan.Data.Core.Model;

namespace Koan.Jobs.Tests.Support;

/// <summary>Action/stage tokens for the test pipeline.</summary>
public static class Stage
{
    public const string Fetch = nameof(Fetch);
    public const string Parse = nameof(Parse);
    public const string Mint = nameof(Mint);
    public const string Publish = nameof(Publish);
    public const string PrepareToFetch = nameof(PrepareToFetch);
}

/// <summary>Single-action job: ignores ctx.Action, mutates the work-item.</summary>
public sealed class GreetJob : Entity<GreetJob>, IKoanJob<GreetJob>
{
    public string Name { get; set; } = "";
    public string? Greeting { get; set; }
    public static int Executions;

    public static Task Execute(GreetJob job, JobContext ctx, CancellationToken ct)
    {
        Interlocked.Increment(ref Executions);
        job.Greeting = $"Hello, {job.Name}";
        return Task.CompletedTask;
    }

    public static void Reset() => Executions = 0;
}

/// <summary>Multi-action linear pipeline (declared chain). Carries saga state across stages.</summary>
[JobChain(Stage.Fetch, Stage.Parse, Stage.Mint, Stage.Publish)]
public sealed class Pipeline : Entity<Pipeline>, IKoanJob<Pipeline>
{
    public string? Fetched { get; set; }
    public string? Parsed { get; set; }
    public bool Minted { get; set; }
    public bool Published { get; set; }
    public List<string> Trail { get; set; } = new();

    public static Task Execute(Pipeline job, JobContext ctx, CancellationToken ct)
    {
        job.Trail.Add(ctx.Action);
        switch (ctx.Action)
        {
            case Stage.Fetch: job.Fetched = "raw"; break;
            case Stage.Parse: job.Parsed = (job.Fetched ?? "") + "-parsed"; break;
            case Stage.Mint: job.Minted = true; break;
            case Stage.Publish: job.Published = true; break;
        }
        return Task.CompletedTask;
    }
}

/// <summary>Fails until its attempt count reaches <see cref="SucceedAtAttempt"/> (retry/backoff/poison testing).</summary>
[JobAction(Action, MaxAttempts = 3)]
public sealed class FlakyJob : Entity<FlakyJob>, IKoanJob<FlakyJob>
{
    public const string Action = "work";
    public static int Executions;
    public static int SucceedAtAttempt = 1; // succeed once Attempt >= this

    public static Task Execute(FlakyJob job, JobContext ctx, CancellationToken ct)
    {
        Interlocked.Increment(ref Executions);
        if (ctx.State.Attempt < SucceedAtAttempt) throw new InvalidOperationException("transient boom");
        return Task.CompletedTask;
    }

    public static void Reset() { Executions = 0; SucceedAtAttempt = 1; }
}

/// <summary>Cooperatively reschedules until it has been deferred <see cref="RescheduleUntil"/> times.</summary>
public sealed class RescheduleJob : Entity<RescheduleJob>, IKoanJob<RescheduleJob>
{
    public static int Executions;
    public static int RescheduleUntil = 2;

    public static Task Execute(RescheduleJob job, JobContext ctx, CancellationToken ct)
    {
        Interlocked.Increment(ref Executions);
        if (ctx.State.Reschedules < RescheduleUntil) ctx.Reschedule(TimeSpan.FromMinutes(5));
        return Task.CompletedTask;
    }

    public static void Reset() { Executions = 0; RescheduleUntil = 2; }
}

/// <summary>Contends for a shared host gate; on the first run it "gets a 429" and backs the whole host off.</summary>
[JobGate(nameof(Host))]
public sealed class GatedJob : Entity<GatedJob>, IKoanJob<GatedJob>
{
    public string Host { get; set; } = "api";
    public static int Executions;
    public static bool Trip429;

    public static Task Execute(GatedJob job, JobContext ctx, CancellationToken ct)
    {
        Interlocked.Increment(ref Executions);
        if (Trip429)
        {
            Trip429 = false;
            ctx.Backoff(TimeSpan.FromMinutes(5));
        }
        return Task.CompletedTask;
    }

    public static void Reset() { Executions = 0; Trip429 = false; }
}

/// <summary>Coalesces concurrent/duplicate submits by a declared key.</summary>
[JobIdempotent(nameof(Key))]
public sealed class DedupeJob : Entity<DedupeJob>, IKoanJob<DedupeJob>
{
    public string Key { get; set; } = "";
    public static int Executions;

    public static Task Execute(DedupeJob job, JobContext ctx, CancellationToken ct)
    {
        Interlocked.Increment(ref Executions);
        return Task.CompletedTask;
    }

    public static void Reset() => Executions = 0;
}

/// <summary>Lane-capped job that records peak observed concurrency.</summary>
[JobAction(Action, Lane = "slow", MaxConcurrency = 2)]
public sealed class SlowJob : Entity<SlowJob>, IKoanJob<SlowJob>
{
    public const string Action = "slow";
    public static int Current;
    public static int Peak;
    public static TimeSpan Hold = TimeSpan.FromMilliseconds(40);

    public static async Task Execute(SlowJob job, JobContext ctx, CancellationToken ct)
    {
        var now = Interlocked.Increment(ref Current);
        InterlockedMax(ref Peak, now);
        try { await Task.Delay(Hold, ct); }
        finally { Interlocked.Decrement(ref Current); }
    }

    private static void InterlockedMax(ref int target, int value)
    {
        int seen;
        do { seen = target; if (value <= seen) return; }
        while (Interlocked.CompareExchange(ref target, value, seen) != seen);
    }

    public static void Reset() { Current = 0; Peak = 0; }
}

/// <summary>Cancellation/timeout: optionally waits forever on the token.</summary>
[JobAction(Action, Timeout = "00:00:30", MaxAttempts = 1)]
public sealed class WaitJob : Entity<WaitJob>, IKoanJob<WaitJob>
{
    public const string Action = "wait";
    public static int Executions;
    public static int Cancellations;

    public static async Task Execute(WaitJob job, JobContext ctx, CancellationToken ct)
    {
        Interlocked.Increment(ref Executions);
        try { await Task.Delay(Timeout.InfiniteTimeSpan, ct); }
        catch (OperationCanceledException) { Interlocked.Increment(ref Cancellations); throw; }
    }

    public static void Reset() { Executions = 0; Cancellations = 0; }
}

/// <summary>Chain steps for the on-failure fixtures.</summary>
public static class Step
{
    public const string One = "one";
    public const string Two = "two";
}

/// <summary>Chain job that can stop or branch from its first step.</summary>
[JobChain("a", "b", "c")]
public sealed class BranchJob : Entity<BranchJob>, IKoanJob<BranchJob>
{
    public List<string> Trail { get; set; } = new();
    public static string Mode = "chain"; // "chain" | "stop" | "branch"

    public static Task Execute(BranchJob job, JobContext ctx, CancellationToken ct)
    {
        job.Trail.Add(ctx.Action);
        if (ctx.Action == "a")
        {
            if (Mode == "stop") ctx.StopChain();
            else if (Mode == "branch") ctx.ContinueWith("z");
        }
        return Task.CompletedTask;
    }

    public static void Reset() => Mode = "chain";
}

/// <summary>First step always fails; <c>OnFailure=Continue</c> means the chain still advances to step two.</summary>
[JobChain(Step.One, Step.Two)]
[JobAction(Step.One, MaxAttempts = 1, OnFailure = OnFailure.Continue)]
public sealed class ContinueChain : Entity<ContinueChain>, IKoanJob<ContinueChain>
{
    public bool TwoRan { get; set; }

    public static Task Execute(ContinueChain job, JobContext ctx, CancellationToken ct)
    {
        if (ctx.Action == Step.One) throw new InvalidOperationException("step one fails");
        job.TwoRan = true;
        return Task.CompletedTask;
    }
}

/// <summary>First step always fails; default <c>OnFailure=Abort</c> means step two never runs.</summary>
[JobChain(Step.One, Step.Two)]
[JobAction(Step.One, MaxAttempts = 1)]
public sealed class AbortChain : Entity<AbortChain>, IKoanJob<AbortChain>
{
    public bool TwoRan { get; set; }

    public static Task Execute(AbortChain job, JobContext ctx, CancellationToken ct)
    {
        if (ctx.Action == Step.One) throw new InvalidOperationException("step one fails");
        job.TwoRan = true;
        return Task.CompletedTask;
    }
}

/// <summary>Type-level trigger target: records the action it ran (no caller instance — runs against a singleton).</summary>
public sealed class TickJob : Entity<TickJob>, IKoanJob<TickJob>
{
    public static int Executions;
    public static string? LastAction;

    public static Task Execute(TickJob job, JobContext ctx, CancellationToken ct)
    {
        Interlocked.Increment(ref Executions);
        LastAction = ctx.Action;
        return Task.CompletedTask;
    }

    public static void Reset() { Executions = 0; LastAction = null; }
}

/// <summary>Idempotent singleton sweep: overlapping type-level triggers coalesce onto one in-flight tick.</summary>
[JobIdempotent(nameof(Key))]
public sealed class SweepTick : Entity<SweepTick>, IKoanJob<SweepTick>
{
    public string Key => "sweep-singleton"; // stable coalesce key
    public static int Executions;

    public static Task Execute(SweepTick job, JobContext ctx, CancellationToken ct)
    {
        Interlocked.Increment(ref Executions);
        return Task.CompletedTask;
    }

    public static void Reset() => Executions = 0;
}

/// <summary>Level-triggered reconcile: a scheduled action that runs only when its sweep releases it.</summary>
[JobAction(Stage.PrepareToFetch, Schedule = "00:10:00")]
public sealed class Reconciled : Entity<Reconciled>, IKoanJob<Reconciled>
{
    public static int Executions;

    public static Task Execute(Reconciled job, JobContext ctx, CancellationToken ct)
    {
        Interlocked.Increment(ref Executions);
        return Task.CompletedTask;
    }

    public static void Reset() => Executions = 0;
}

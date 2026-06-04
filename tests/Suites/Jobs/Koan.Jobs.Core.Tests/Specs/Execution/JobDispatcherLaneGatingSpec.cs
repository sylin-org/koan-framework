using System;
using System.IO;
using System.Runtime.CompilerServices;

namespace Koan.Jobs.Core.Tests.Specs.Execution;

/// <summary>
/// Structural spec for JOBS Option B: the per-fanout-task lane-global permit must be acquired
/// BEFORE the first <c>Job&lt;T&gt;.Get</c> round-trip, so a backlog of N fanned-out items cannot
/// open N Mongo connections at once.
///
/// A deterministic concurrency assertion against the real dispatcher is hard to write here without
/// a Mongo fixture and a hookable load-counter — per the bug-fix spec, the pragmatic substitute is
/// to assert the order in the dispatcher source. If <c>JobDispatcher.Run</c> ever reverts to
/// <c>Get</c>-then-<c>AcquireAsync</c>, this test will fail and pinpoint the regression.
/// </summary>
public sealed class JobDispatcherLaneGatingSpec
{
    [Fact(DisplayName = "JobDispatcher: lane permit is acquired before any Job<T>.Get call")]
    public void Lane_permit_acquired_before_job_get()
    {
        var source = LoadDispatcherSource();

        var acquireIdx = source.IndexOf("lanes.AcquireAsync(", StringComparison.Ordinal);
        var getIdx = source.IndexOf("Job<T>.Get(", StringComparison.Ordinal);

        acquireIdx.Should().BeGreaterThan(-1, "JobDispatcher must call lanes.AcquireAsync");
        getIdx.Should().BeGreaterThan(-1, "JobDispatcher must call Job<T>.Get");
        acquireIdx.Should().BeLessThan(
            getIdx,
            "Option B requires the lane permit to be acquired before the first Mongo round-trip; otherwise a fanout of N items opens N connections before throttling.");
    }

    [Fact(DisplayName = "JobDispatcher: the first lane acquire happens inside Run (not RunGated)")]
    public void First_acquire_is_in_run_entry_point()
    {
        // Defence-in-depth: ensure the structural change wasn't undone by moving the gate INTO the
        // method that already has the loaded job — that would re-introduce the bug.
        var source = LoadDispatcherSource();
        var runMethodIdx = source.IndexOf("public static async Task Run(", StringComparison.Ordinal);
        var runGatedIdx = source.IndexOf("private static async Task RunGated(", StringComparison.Ordinal);

        runMethodIdx.Should().BeGreaterThan(-1);
        runGatedIdx.Should().BeGreaterThan(runMethodIdx);

        var runBody = source.Substring(runMethodIdx, runGatedIdx - runMethodIdx);
        runBody.Should().Contain(
            "lanes.AcquireAsync(",
            "Run must acquire the lane permit at the top, before delegating to RunGated which performs the Mongo load.");
    }

    private static string LoadDispatcherSource([CallerFilePath] string callerFilePath = "")
    {
        // [CallerFilePath] hands us the compile-time absolute path of THIS file regardless of where
        // the test binary actually lives at runtime (centralized %TEMP%\Koan-framework\... output
        // makes a binary-relative walk-up unreliable). From here, walk up to the repo root.
        var dir = new DirectoryInfo(Path.GetDirectoryName(callerFilePath)!);
        for (var i = 0; i < 12 && dir is not null; i++)
        {
            var candidate = Path.Combine(dir.FullName, "src", "Koan.Jobs.Core", "Execution", "JobDispatcher.cs");
            if (File.Exists(candidate)) return File.ReadAllText(candidate);
            dir = dir.Parent;
        }
        throw new FileNotFoundException($"Unable to locate JobDispatcher.cs by walking parent directories from caller path '{callerFilePath}'.");
    }
}
